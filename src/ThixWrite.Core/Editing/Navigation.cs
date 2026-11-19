using ThixOffice.Text.Unicode;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>How the caret moves (arrow keys, Home/End, Page Up/Down, Ctrl combinations).</summary>
public enum CaretMovement
{
    CharacterLeft,
    CharacterRight,
    WordLeft,
    WordRight,
    LineUp,
    LineDown,
    LineStart,
    LineEnd,
    ParagraphUp,
    ParagraphDown,
    PageUp,
    PageDown,
    DocumentStart,
    DocumentEnd,
}

/// <summary>A caret place as the layout sees it: position plus the line it shows on.</summary>
public readonly record struct CaretPlace(DocumentPosition Position, CaretAffinity Affinity = CaretAffinity.Downstream);

/// <summary>
/// What the editing session asks the layout for the movements that depend on lines and pages. The layout engine
/// (<c>Layout.DocumentLayout</c>) implements it; without one, line moves fall back to paragraph moves.
/// </summary>
public interface ICaretNavigator
{
    /// <summary>The x (points, in the page) of the caret – the "goal x" vertical moves keep.</summary>
    double CaretX(CaretPlace place);

    /// <summary>The place one line above (<paramref name="direction"/> -1) or below (+1) at <paramref name="goalX"/>; null at the first / last line.</summary>
    CaretPlace? MoveByLine(CaretPlace place, int direction, double goalX);

    /// <summary>The place one screen page above / below (the height the view reports, else a page) at <paramref name="goalX"/>.</summary>
    CaretPlace MoveByPage(CaretPlace place, int direction, double goalX);

    /// <summary>The start of the line the caret is on.</summary>
    CaretPlace LineStart(CaretPlace place);

    /// <summary>The end of the line the caret is on (before a trailing space of a wrapped line, upstream).</summary>
    CaretPlace LineEnd(CaretPlace place);
}

/// <summary>
/// Caret stops inside the text: grapheme cluster boundaries; invisible markers (bookmarks, field characters) stick to the
/// character after them, so that the caret never stops between a marker and its character. Preserved blocks are skipped.
/// </summary>
public static class CaretStops
{
    /// <summary>The next caret stop after <paramref name="position"/> (the next paragraph's start at a paragraph end).</summary>
    public static DocumentPosition Next(Document document, DocumentPosition position)
    {
        if (document.Blocks[position.Block] is not Paragraph paragraph || position.Offset >= paragraph.Length)
        {
            return NextParagraphStart(document, position.Block) ?? document.End;
        }

        var text = paragraph.Text;
        var next = TextBoundaries.NextGrapheme(text, position.Offset);
        while (next < text.Length && IsMarker(paragraph, next - 1))
        {
            next = TextBoundaries.NextGrapheme(text, next);
        }

        return position with { Offset = next };
    }

    /// <summary>The caret stop before <paramref name="position"/> (the previous paragraph's end at a paragraph start).</summary>
    public static DocumentPosition Previous(Document document, DocumentPosition position)
    {
        if (document.Blocks[position.Block] is not Paragraph paragraph || position.Offset == 0)
        {
            return PreviousParagraphEnd(document, position.Block) ?? FirstPosition(document);
        }

        var text = paragraph.Text;
        var previous = TextBoundaries.PreviousGrapheme(text, Math.Min(position.Offset, text.Length));
        while (previous > 0 && IsMarker(paragraph, previous - 1))
        {
            previous = TextBoundaries.PreviousGrapheme(text, previous);
        }

        return position with { Offset = previous };
    }

    /// <summary>Ctrl+Right: the start of the next word (the next paragraph's start at a paragraph end).</summary>
    public static DocumentPosition NextWord(Document document, DocumentPosition position)
    {
        if (document.Blocks[position.Block] is not Paragraph paragraph || position.Offset >= paragraph.Length)
        {
            return NextParagraphStart(document, position.Block) ?? document.End;
        }

        return position with { Offset = TextBoundaries.NextWordStart(paragraph.Text, position.Offset) };
    }

    /// <summary>Ctrl+Left: the start of this or the previous word.</summary>
    public static DocumentPosition PreviousWord(Document document, DocumentPosition position)
    {
        if (document.Blocks[position.Block] is not Paragraph paragraph || position.Offset == 0)
        {
            return PreviousParagraphEnd(document, position.Block) ?? FirstPosition(document);
        }

        return position with { Offset = TextBoundaries.PreviousWordStart(paragraph.Text, position.Offset) };
    }

    /// <summary>The first caret position of the document (the first paragraph's start).</summary>
    public static DocumentPosition FirstPosition(Document document) => new(ContentEdits.NextParagraph(document, 0), 0);

    /// <summary>The start of the first paragraph after block <paramref name="block"/>, or null.</summary>
    public static DocumentPosition? NextParagraphStart(Document document, int block)
    {
        for (var index = block + 1; index < document.Blocks.Count; index++)
        {
            if (document.Blocks[index] is Paragraph)
            {
                return new DocumentPosition(index, 0);
            }
        }

        return null;
    }

    /// <summary>The end of the last paragraph before block <paramref name="block"/>, or null.</summary>
    public static DocumentPosition? PreviousParagraphEnd(Document document, int block)
    {
        for (var index = block - 1; index >= 0; index--)
        {
            if (document.Blocks[index] is Paragraph paragraph)
            {
                return new DocumentPosition(index, paragraph.Length);
            }
        }

        return null;
    }

    /// <summary>A valid caret position near <paramref name="position"/>: clamped, on a paragraph, at a caret stop.</summary>
    public static DocumentPosition Snap(Document document, DocumentPosition position)
    {
        var clamped = ContentEdits.Clamp(document, position);
        if (document.Blocks[clamped.Block] is not Paragraph paragraph)
        {
            return new DocumentPosition(ContentEdits.NextParagraph(document, clamped.Block), 0);
        }

        var offset = clamped.Offset;
        var text = paragraph.Text;
        if (offset == 0 || offset >= text.Length)
        {
            return clamped;
        }

        // Inside a grapheme cluster: back to its start.
        var previous = TextBoundaries.PreviousGrapheme(text, offset);
        var isBoundary = TextBoundaries.NextGrapheme(text, previous) == offset;
        return isBoundary ? clamped : clamped with { Offset = previous };
    }

    private static bool IsMarker(Paragraph paragraph, int offset) => paragraph.InlineAt(offset) is OpaqueInline { IsMarker: true };
}
