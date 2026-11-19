using ThixOffice.Text.Unicode;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>What to search for: the text, case-sensitive or not, whole words only.</summary>
public sealed record FindOptions(string Text, bool MatchCase = false, bool WholeWord = false);

/// <summary>Finding text in a document. Matches never span paragraphs (like Word without special characters).</summary>
public static class TextSearch
{
    /// <summary>
    /// The first match at or after <paramref name="from"/> (<paramref name="forward"/>) or the last one ending at or before it,
    /// wrapping around the document when <paramref name="wrap"/>; null when there is none.
    /// </summary>
    public static DocumentRange? Find(Document document, FindOptions options, DocumentPosition from, bool forward = true, bool wrap = true)
    {
        if (options.Text.Length == 0)
        {
            return null;
        }

        var count = document.Blocks.Count;
        var steps = wrap ? count + 1 : forward ? count - from.Block : from.Block + 1;
        for (var step = 0; step < steps; step++)
        {
            var block = forward ? (from.Block + step) % count : ((from.Block - step) % count + count) % count;
            if (document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            var isFirst = step == 0;
            var isWrappedFirst = step == count;
            int? offset = forward
                ? FindForward(paragraph.Text, options, isFirst ? from.Offset : 0, isWrappedFirst ? from.Offset : int.MaxValue)
                : FindBackward(paragraph.Text, options, isFirst ? from.Offset : int.MaxValue, isWrappedFirst ? from.Offset : 0);
            if (offset is { } start)
            {
                return new DocumentRange(new DocumentPosition(block, start), new DocumentPosition(block, start + options.Text.Length));
            }
        }

        return null;
    }

    /// <summary>All matches in document order.</summary>
    public static IReadOnlyList<DocumentRange> FindAll(Document document, FindOptions options)
    {
        var matches = new List<DocumentRange>();
        if (options.Text.Length == 0)
        {
            return matches;
        }

        for (var block = 0; block < document.Blocks.Count; block++)
        {
            if (document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            var offset = 0;
            while (FindForward(paragraph.Text, options, offset, int.MaxValue) is { } start)
            {
                matches.Add(new DocumentRange(new DocumentPosition(block, start), new DocumentPosition(block, start + options.Text.Length)));
                offset = start + options.Text.Length;
            }
        }

        return matches;
    }

    /// <summary>True when <paramref name="range"/> is exactly a match of <paramref name="options"/>.</summary>
    public static bool IsMatch(Document document, DocumentRange range, FindOptions options)
    {
        if (range.IsMultiBlock || document.Blocks[range.Start.Block] is not Paragraph paragraph)
        {
            return false;
        }

        var length = range.End.Offset - range.Start.Offset;
        if (length != options.Text.Length || range.End.Offset > paragraph.Length)
        {
            return false;
        }

        return string.Compare(paragraph.Text, range.Start.Offset, options.Text, 0, length, Comparison(options)) == 0
            && (!options.WholeWord || IsWholeWord(paragraph.Text, range.Start.Offset, length));
    }

    private static int? FindForward(string text, FindOptions options, int from, int endLimit)
    {
        var position = Math.Max(0, from);
        while (position <= text.Length - options.Text.Length)
        {
            var index = text.IndexOf(options.Text, position, Comparison(options));
            if (index < 0 || index + options.Text.Length > endLimit)
            {
                return null;
            }

            if (!options.WholeWord || IsWholeWord(text, index, options.Text.Length))
            {
                return index;
            }

            position = index + 1;
        }

        return null;
    }

    private static int? FindBackward(string text, FindOptions options, int endAt, int startLimit)
    {
        var end = Math.Min(endAt, text.Length);
        while (end - options.Text.Length >= startLimit && end >= options.Text.Length)
        {
            var index = text.LastIndexOf(options.Text, end - 1, end, Comparison(options));
            if (index < 0 || index < startLimit)
            {
                return null;
            }

            if (index + options.Text.Length <= end && (!options.WholeWord || IsWholeWord(text, index, options.Text.Length)))
            {
                return index;
            }

            end = index + options.Text.Length - 1;
        }

        return null;
    }

    private static StringComparison Comparison(FindOptions options) =>
        options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    private static bool IsWholeWord(string text, int start, int length)
    {
        var startsWord = start == 0 || TextBoundaries.KindAt(text, start - 1) != WordCharKind.Word || TextBoundaries.KindAt(text, start) != WordCharKind.Word;
        var end = start + length;
        var endsWord = end >= text.Length || TextBoundaries.KindAt(text, end) != WordCharKind.Word || TextBoundaries.KindAt(text, end - 1) != WordCharKind.Word;
        return startsWord && endsWord;
    }
}
