using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>A table of contents in the document: its entries are blocks [<see cref="Start"/>, <see cref="End"/>].</summary>
public sealed record TocRegion(int Start, int End, string Instruction);

/// <summary>A heading the table of contents lists: its block, level (1-based), text and the bookmark its entry links to (null: none yet).</summary>
public sealed record TocHeading(int Block, int Level, string Text, string? Bookmark);

/// <summary>Referenzen › Inhaltsverzeichnis: insert, update, remove; headings and bookmarks (links of the entries, Strg+Klick jumps).</summary>
public sealed partial class EditingSession
{
    private const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The first table of contents of the document, or null.</summary>
    public TocRegion? FindTableOfContents()
    {
        for (var block = 0; block < Document.Blocks.Count; block++)
        {
            if (Document.Blocks[block] is not Paragraph paragraph || paragraph.Inlines.OfType<TocMarkInline>().FirstOrDefault(mark => !mark.IsEnd) is not { } start)
            {
                continue;
            }

            if (paragraph.Inlines.OfType<TocMarkInline>().Any(mark => mark.IsEnd))
            {
                return new TocRegion(block, block, start.Instruction);
            }

            var end = block;
            for (var next = block + 1; next < Document.Blocks.Count - 1; next++)
            {
                if (Document.Blocks[next] is not Paragraph entry)
                {
                    break;
                }

                if (entry.Inlines.OfType<TocMarkInline>().Any(mark => mark.IsEnd))
                {
                    end = next;
                    break;
                }

                if (entry.Properties.StyleId?.StartsWith("TOC", StringComparison.Ordinal) != true || entry.Inlines.OfType<TocMarkInline>().Any())
                {
                    break; // no end mark (deleted): the entries are the TOC-styled paragraphs after the start
                }

                end = next;
            }

            return new TocRegion(block, end, start.Instruction);
        }

        return null;
    }

    /// <summary>The headings of levels 1 to <paramref name="levels"/> (by outline level, so Überschrift 1–9 and every style with one) outside <paramref name="skip"/>.</summary>
    public IReadOnlyList<TocHeading> Headings(int levels = 9, TocRegion? skip = null)
    {
        var headings = new List<TocHeading>();
        for (var block = 0; block < Document.Blocks.Count; block++)
        {
            if (skip is not null && block >= skip.Start && block <= skip.End)
            {
                continue;
            }

            if (Document.Blocks[block] is not Paragraph paragraph || paragraph.Length == 0)
            {
                continue;
            }

            var level = Resolver.ResolveParagraph(paragraph).OutlineLevel + 1;
            var text = HeadingText(paragraph);
            if (level < 1 || level > levels || text.Length == 0)
            {
                continue;
            }

            headings.Add(new TocHeading(block, level, text, TocBookmarkOf(paragraph)));
        }

        return headings;
    }

    /// <summary>
    /// Referenzen › Inhaltsverzeichnis › Automatisches Verzeichnis: the heading "Inhalt" and the entries of headings 1 to
    /// <paramref name="levels"/> (text, dotted tab, page number, a link to the heading) before the caret's paragraph (in an empty
    /// paragraph: there). One step; the page numbers are those after the insertion.
    /// </summary>
    public void InsertTableOfContents(int levels = 3)
    {
        Run("Inhaltsverzeichnis", EditKind.Other, null, () =>
        {
            ClearWholeCells();
            var caret = _selection.Start;
            var block = ContentEdits.NextParagraph(Document, caret.Block);
            if (Document.ParagraphAt(block)?.Cell is not null)
            {
                return; // not inside a table cell
            }

            EnsureStyle(BuiltInStyles.TocHeading);
            var instruction = TableOfContents.InstructionFor(levels);
            var headings = PrepareHeadings(levels, null);
            var heading = new Paragraph(TableOfContents.Heading, new ParagraphProperties { StyleId = BuiltInStyles.TocHeading });
            var entries = Entries(headings, instruction, Document.SectionOf(block).Properties.ContentWidth, null);
            var inserted = new List<Block> { heading };
            inserted.AddRange(entries);
            Apply(new BlocksStep(block, [], inserted, new BlockReplaceMap(block, 0, inserted)));
            var region = new TocRegion(block + 1, block + entries.Count, instruction);
            FixPageNumbers(region);
            SetSelectionQuiet(Selection.Caret(new DocumentPosition(region.End + 1, 0)));
        });
    }

    /// <summary>Tabelle aktualisieren: the entries made anew from the headings as they are now, with their page numbers. False without a table of contents.</summary>
    public bool UpdateTableOfContents()
    {
        if (FindTableOfContents() is not { } found)
        {
            return false;
        }

        Run("Inhaltsverzeichnis aktualisieren", EditKind.Other, null, () =>
        {
            var levels = TableOfContents.LevelsOf(found.Instruction);
            var headings = PrepareHeadings(levels, found);
            var region = FindTableOfContents()!;
            var first = (Paragraph)Document.Blocks[region.Start];
            var entries = Entries(headings, region.Instruction, Document.SectionOf(region.Start).Properties.ContentWidth, first);
            var removed = ContentEdits.Slice(Document, region.Start, region.End - region.Start + 1);
            Apply(new BlocksStep(region.Start, removed, entries, new BlockReplaceMap(region.Start, removed.Count, entries)));
            FixPageNumbers(region with { End = region.Start + entries.Count - 1 });
        });
        return true;
    }

    /// <summary>Removes the table of contents (its entries and the "Inhalt" heading right before it).</summary>
    public void RemoveTableOfContents()
    {
        if (FindTableOfContents() is not { } region)
        {
            return;
        }

        Run("Inhaltsverzeichnis entfernen", EditKind.Other, null, () =>
        {
            var start = region.Start > 0 && Document.ParagraphAt(region.Start - 1)?.Properties.StyleId == BuiltInStyles.TocHeading ? region.Start - 1 : region.Start;
            var removed = ContentEdits.Slice(Document, start, region.End - start + 1);
            var keep = Document.Blocks.Count - removed.Count == 0 || region.End == Document.Blocks.Count - 1 ? new List<Block> { Paragraph.Empty() } : [];
            Apply(new BlocksStep(start, removed, keep, new BlockReplaceMap(start, removed.Count, keep)));
            SetSelectionQuiet(Selection.Caret(CaretStops.Snap(Document, new DocumentPosition(Math.Min(start, Document.Blocks.Count - 1), 0))));
        });
    }

    /// <summary>The position of a bookmark (a link's anchor: "_Toc…"), or null when the document has none of that name.</summary>
    public DocumentPosition? FindBookmark(string name)
    {
        var needle = $"w:name=\"{name}\"";
        for (var block = 0; block < Document.Blocks.Count; block++)
        {
            if (Document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            for (var index = 0; index < paragraph.Inlines.Length; index++)
            {
                if (paragraph.Inlines[index] is OpaqueInline { Kind: PreservedKind.Bookmark } bookmark && bookmark.Xml.Contains(needle, StringComparison.Ordinal))
                {
                    return new DocumentPosition(block, paragraph.StartOf(index));
                }
            }
        }

        return null;
    }

    /// <summary>Follows a link inside the document: the caret to the start of the bookmark's paragraph. False when there is no such bookmark.</summary>
    public bool GoToBookmark(string name)
    {
        if (FindBookmark(name) is not { } position)
        {
            return false;
        }

        SetCaret(position with { Offset = 0 });
        return true;
    }

    /// <summary>The headings, each with a bookmark (added to the headings that have none – inside the running step).</summary>
    private List<TocHeading> PrepareHeadings(int levels, TocRegion? skip)
    {
        var headings = Headings(levels, skip).ToList();
        var nextId = NextBookmarkId();
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            if (heading.Bookmark is not null)
            {
                continue;
            }

            var id = nextId++;
            var name = "_Toc" + (100000000 + id).ToString(CultureInfo.InvariantCulture);
            var paragraph = (Paragraph)Document.Blocks[heading.Block];
            var start = new OpaqueInline($"<w:bookmarkStart w:name=\"{name}\" w:id=\"{id}\" xmlns:w=\"{WordNamespace}\" />", PreservedKind.Bookmark, false, CharacterProperties.Empty);
            var end = new OpaqueInline($"<w:bookmarkEnd w:id=\"{id}\" xmlns:w=\"{WordNamespace}\" />", PreservedKind.Bookmark, false, CharacterProperties.Empty);
            ApplyOptional(ContentEdits.FormatParagraphs(Document, heading.Block, heading.Block, _ => paragraph.WithInlines([start, .. paragraph.Inlines, end])));
            headings[index] = heading with { Bookmark = name };
        }

        return headings;
    }

    /// <summary>The entry paragraphs (the first with the start mark, the last with the end mark); page numbers as the layout has them now.</summary>
    private List<Block> Entries(IReadOnlyList<TocHeading> headings, string instruction, double width, Paragraph? previousFirst)
    {
        var properties = previousFirst?.Inlines.OfType<TocMarkInline>().FirstOrDefault()?.Properties ?? CharacterProperties.Empty;
        var start = new TocMarkInline(false, instruction, properties);
        var end = new TocMarkInline(true, "", properties);
        if (headings.Count == 0)
        {
            return [new Paragraph([start, new TextRun(TableOfContents.NoEntries, CharacterProperties.Empty), end])];
        }

        var entries = new List<Block>();
        for (var index = 0; index < headings.Count; index++)
        {
            var heading = headings[index];
            EnsureStyle(TableOfContents.EntryStyle(heading.Level));
            var link = new Hyperlink(null, heading.Bookmark);
            var inlines = new List<Inline>();
            if (index == 0)
            {
                inlines.Add(start);
            }

            inlines.Add(new TextRun(heading.Text, CharacterProperties.Empty, link));
            inlines.Add(new TextRun("\t", CharacterProperties.Empty, link));
            inlines.Add(new TextRun(PageNumberOf(heading.Block).ToString(CultureInfo.InvariantCulture), CharacterProperties.Empty, link));
            if (index == headings.Count - 1)
            {
                inlines.Add(end);
            }

            entries.Add(new Paragraph(inlines, new ParagraphProperties
            {
                StyleId = TableOfContents.EntryStyle(heading.Level),
                Tabs = [new TabStop(Math.Round(width, 1), TabAlignment.Right, TabLeader.Dot)],
            }));
        }

        return entries;
    }

    /// <summary>
    /// After the entries are in place the headings may sit on other pages (the entries take room): the numbers are set again from the
    /// layout until they hold (twice at most, inside the same step).
    /// </summary>
    private void FixPageNumbers(TocRegion region)
    {
        for (var round = 0; round < 2; round++)
        {
            var changed = false;
            for (var block = region.Start; block <= region.End && block < Document.Blocks.Count; block++)
            {
                if (Document.Blocks[block] is not Paragraph entry || entry.Inlines.FirstOrDefault(inline => inline is TextRun { Link.Anchor: not null }) is not TextRun { Link.Anchor: { } anchor })
                {
                    continue;
                }

                if (FindBookmark(anchor) is not { } target)
                {
                    continue;
                }

                var page = PageNumberOf(target.Block).ToString(CultureInfo.InvariantCulture);
                var index = entry.Inlines.Length - 1;
                while (index >= 0 && entry.Inlines[index] is not TextRun)
                {
                    index--;
                }

                if (index < 0 || entry.Inlines[index] is not TextRun number || number.Text == page || !number.Text.All(char.IsDigit))
                {
                    continue;
                }

                var inlines = entry.Inlines.SetItem(index, number with { Text = page });
                ApplyOptional(ContentEdits.FormatParagraphs(Document, block, block, _ => entry.WithInlines(inlines)));
                changed = true;
            }

            if (!changed)
            {
                return;
            }
        }
    }

    private int PageNumberOf(int block) => Navigator?.PageNumberOf(block) ?? 1;

    /// <summary>The text of a heading for its entry (tabs and breaks as spaces, objects left out).</summary>
    private static string HeadingText(Paragraph paragraph)
    {
        var builder = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is TextRun run)
            {
                builder.Append(run.Text.Replace('\t', ' ').Replace(SpecialCharacters.LineBreak, ' ').Replace(SpecialCharacters.PageBreak, ' '));
            }
        }

        return builder.ToString().Trim();
    }

    /// <summary>The name of a "_Toc…" bookmark that starts in the paragraph, or null.</summary>
    private static string? TocBookmarkOf(Paragraph paragraph)
    {
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is OpaqueInline { Kind: PreservedKind.Bookmark } bookmark && bookmark.Xml.StartsWith("<w:bookmarkStart", StringComparison.Ordinal)
                && BookmarkName().Match(bookmark.Xml) is { Success: true } match && match.Groups[1].Value.StartsWith("_Toc", StringComparison.Ordinal))
            {
                return match.Groups[1].Value;
            }
        }

        return null;
    }

    /// <summary>One more than the highest bookmark id in the document.</summary>
    private int NextBookmarkId()
    {
        var max = 0;
        foreach (var paragraph in Document.Blocks.OfType<Paragraph>())
        {
            foreach (var inline in paragraph.Inlines)
            {
                if (inline is OpaqueInline { Kind: PreservedKind.Bookmark } bookmark && BookmarkId().Match(bookmark.Xml) is { Success: true } match
                    && int.TryParse(match.Groups[1].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id))
                {
                    max = Math.Max(max, id);
                }
            }
        }

        return max + 1;
    }

    [GeneratedRegex("w:name=\"([^\"]*)\"")]
    private static partial Regex BookmarkName();

    [GeneratedRegex("w:id=\"(-?\\d+)\"")]
    private static partial Regex BookmarkId();
}
