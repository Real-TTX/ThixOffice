using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// The pure edit functions of the engine: each computes the new blocks of a change and returns it as a step (nothing is applied
/// here). All content changes are one primitive – replacing a range with content – which keeps the position mapping uniform.
/// </summary>
internal static class ContentEdits
{
    /// <summary>
    /// Replaces <paramref name="range"/> with <paramref name="parts"/> (first and last a paragraph; one part = inline content).
    /// Paragraph properties follow the mark rule: a paragraph keeps the properties of the paragraph whose mark ends it; when a
    /// deletion merges paragraphs the first one's properties win unless nothing of it is left.
    /// </summary>
    public static BlocksStep Replace(Document document, DocumentRange range, IReadOnlyList<Block> parts)
    {
        var (from, to) = Normalize(document, range);
        var first = document.Blocks[from.Block] as Paragraph;
        var last = (Paragraph)document.Blocks[to.Block];
        var head = first?.Slice(0, from.Offset) ?? [];
        var tail = last.Slice(to.Offset, last.Length);
        var headIsEmpty = first is null || from.Offset == 0;

        var result = new List<Block>(parts.Count);
        int lastLength;
        if (parts.Count == 1)
        {
            var inserted = (Paragraph)parts[0];
            var basis = from.Block == to.Block || !headIsEmpty ? first ?? last : last;
            result.Add(new Paragraph(head.Concat(inserted.Inlines).Concat(tail), basis.Properties, basis.MarkProperties, last.SectionBreak));
            lastLength = inserted.Length;
        }
        else
        {
            var firstPart = (Paragraph)parts[0];
            var lastPart = (Paragraph)parts[^1];
            var firstBasis = headIsEmpty || first is null ? firstPart : first;
            result.Add(new Paragraph(head.Concat(firstPart.Inlines), firstBasis.Properties, firstBasis.MarkProperties, firstPart.SectionBreak));
            for (var index = 1; index < parts.Count - 1; index++)
            {
                result.Add(parts[index]);
            }

            // Into an empty place (nothing of the target paragraph is left) the last part keeps its own formatting.
            var lastBasis = headIsEmpty && tail.Count == 0 ? lastPart : last;
            result.Add(new Paragraph(lastPart.Inlines.Concat(tail), lastBasis.Properties, lastBasis.MarkProperties, last.SectionBreak));
            lastLength = lastPart.Length;
        }

        var removed = Slice(document, from.Block, to.Block - from.Block + 1);
        return new BlocksStep(from.Block, removed, result, new ReplaceMap(from, to, parts.Count, lastLength));
    }

    /// <summary>Inserts plain <paramref name="text"/> (no paragraph breaks) at <paramref name="position"/> with <paramref name="properties"/>.</summary>
    public static BlocksStep InsertText(Document document, DocumentRange range, string text, CharacterProperties properties, Hyperlink? link = null) =>
        Replace(document, range, [new Paragraph([new TextRun(text, properties, link)])]);

    /// <summary>Deletes <paramref name="range"/>.</summary>
    public static BlocksStep Delete(Document document, DocumentRange range) => Replace(document, range, [Paragraph.Empty()]);

    /// <summary>Replaces <paramref name="range"/> with a paragraph break (Enter); both halves keep the paragraph's properties.</summary>
    public static BlocksStep SplitParagraph(Document document, DocumentRange range)
    {
        var (from, to) = Normalize(document, range);
        var last = (Paragraph)document.Blocks[to.Block];
        var current = document.Blocks[from.Block] as Paragraph ?? last;
        var first = Paragraph.Empty(current.Properties, current.MarkProperties);
        return Replace(document, new DocumentRange(from, to), [first, Paragraph.Empty(last.Properties, last.MarkProperties)]);
    }

    /// <summary>Changes the character formatting of the text in <paramref name="range"/> (and the marks of paragraphs fully in it).</summary>
    public static BlocksStep? FormatCharacters(Document document, DocumentRange range, Func<Paragraph, CharacterProperties, CharacterProperties> change)
    {
        var start = range.Start;
        var end = range.End;
        var inserted = new List<Block>();
        var changed = false;
        for (var index = start.Block; index <= end.Block; index++)
        {
            var block = document.Blocks[index];
            if (block is not Paragraph paragraph)
            {
                inserted.Add(block);
                continue;
            }

            var from = index == start.Block ? start.Offset : 0;
            var to = index == end.Block ? end.Offset : paragraph.Length;
            var includesMark = index < end.Block || (from == 0 && to == paragraph.Length);
            var updated = FormatParagraphCharacters(paragraph, from, to, includesMark, change);
            changed |= !ReferenceEquals(updated, paragraph);
            inserted.Add(updated);
        }

        if (!changed)
        {
            return null;
        }

        return new BlocksStep(start.Block, Slice(document, start.Block, end.Block - start.Block + 1), inserted, PositionMap.Identity);
    }

    /// <summary>Changes the paragraphs of blocks [<paramref name="startBlock"/>, <paramref name="endBlock"/>]; null when nothing changes.</summary>
    public static BlocksStep? FormatParagraphs(Document document, int startBlock, int endBlock, Func<Paragraph, Paragraph> change)
    {
        var inserted = new List<Block>();
        var changed = false;
        for (var index = startBlock; index <= endBlock; index++)
        {
            var block = document.Blocks[index];
            if (block is not Paragraph paragraph)
            {
                inserted.Add(block);
                continue;
            }

            var updated = change(paragraph);
            if (updated.ContentEquals(paragraph))
            {
                inserted.Add(paragraph);
                continue;
            }

            changed = true;
            inserted.Add(updated);
        }

        if (!changed)
        {
            return null;
        }

        return new BlocksStep(startBlock, Slice(document, startBlock, endBlock - startBlock + 1), inserted, PositionMap.Identity);
    }

    /// <summary>
    /// The range made safe for replacing: an empty range on a preserved block moves to the next paragraph; a range ending on a
    /// preserved block extends over it to the next paragraph (the block is part of what is replaced).
    /// </summary>
    public static (DocumentPosition From, DocumentPosition To) Normalize(Document document, DocumentRange range)
    {
        var from = Clamp(document, range.Start);
        var to = Clamp(document, range.End);
        if (document.Blocks[to.Block] is not Paragraph)
        {
            var next = NextParagraph(document, to.Block);
            to = new DocumentPosition(next, 0);
            if (range.IsEmpty)
            {
                from = to;
            }
        }

        return (from, to);
    }

    /// <summary>The position clamped into the document.</summary>
    public static DocumentPosition Clamp(Document document, DocumentPosition position)
    {
        var block = Math.Clamp(position.Block, 0, document.Blocks.Count - 1);
        var offset = Math.Clamp(position.Offset, 0, document.Blocks[block].Length);
        return new DocumentPosition(block, offset);
    }

    /// <summary>The index of the first paragraph at or after <paramref name="block"/> (the last block always is one).</summary>
    public static int NextParagraph(Document document, int block)
    {
        var index = Math.Max(0, block);
        while (index < document.Blocks.Count - 1 && document.Blocks[index] is not Paragraph)
        {
            index++;
        }

        return index;
    }

    /// <summary>The index of the last paragraph at or before <paramref name="block"/>, or -1.</summary>
    public static int PreviousParagraph(Document document, int block)
    {
        var index = Math.Min(block, document.Blocks.Count - 1);
        while (index >= 0 && document.Blocks[index] is not Paragraph)
        {
            index--;
        }

        return index;
    }

    public static List<Block> Slice(Document document, int start, int count)
    {
        var blocks = new List<Block>(count);
        for (var index = start; index < start + count; index++)
        {
            blocks.Add(document.Blocks[index]);
        }

        return blocks;
    }

    private static Paragraph FormatParagraphCharacters(
        Paragraph paragraph,
        int from,
        int to,
        bool includesMark,
        Func<Paragraph, CharacterProperties, CharacterProperties> change)
    {
        var inlines = new List<Inline>(paragraph.Inlines.Length + 2);
        var changed = false;
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            var inline = paragraph.Inlines[index];
            var start = paragraph.StartOf(index);
            var end = start + inline.Length;
            if (end <= from || start >= to || from == to)
            {
                inlines.Add(inline);
                continue;
            }

            if (inline is TextRun run && (start < from || end > to))
            {
                var cutStart = Math.Max(from, start) - start;
                var cutEnd = Math.Min(to, end) - start;
                if (cutStart > 0)
                {
                    inlines.Add(run with { Text = run.Text[..cutStart] });
                }

                var middle = run with { Text = run.Text[cutStart..cutEnd], Properties = change(paragraph, run.Properties) };
                changed |= middle.Properties != run.Properties;
                inlines.Add(middle);
                if (cutEnd < run.Text.Length)
                {
                    inlines.Add(run with { Text = run.Text[cutEnd..] });
                }

                continue;
            }

            var properties = change(paragraph, inline.Properties);
            changed |= properties != inline.Properties;
            inlines.Add(inline with { Properties = properties });
        }

        var mark = includesMark ? change(paragraph, paragraph.MarkProperties) : paragraph.MarkProperties;
        changed |= mark != paragraph.MarkProperties;
        if (!changed)
        {
            return paragraph;
        }

        return new Paragraph(inlines, paragraph.Properties, mark, paragraph.SectionBreak);
    }
}
