using System.Globalization;
using System.Text.RegularExpressions;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>The cover page designs of Einfügen › Deckblatt.</summary>
public enum CoverDesign
{
    /// <summary>"Band": a band in the accent colour over the top of the page, the title white on it.</summary>
    Band,

    /// <summary>"Seitenleiste": a bar in the accent colour down the left edge, the title beside it.</summary>
    Sidebar,

    /// <summary>"Rahmen": a frame around the page, the title centred.</summary>
    Frame,
}

/// <summary>
/// Einfügen › Seiten (Deckblatt, Leere Seite), Initiale and Lesezeichen. Each is one undoable step.
/// </summary>
public sealed partial class EditingSession
{
    private const string CoverGallery = "Cover Pages";

    /// <summary>
    /// "Deckblatt": a cover page at the start of the document – a shape behind the text, the title, the subtitle, author and date, a page
    /// break – inside a "Cover Pages" content control as Word makes it; a cover page that is there already is replaced.
    /// </summary>
    public void InsertCoverPage(CoverDesign design, string title, string subtitle, string author, DateTime date)
    {
        Run("Deckblatt", EditKind.Other, null, () =>
        {
            EnsureStyle(BuiltInStyles.Title);
            EnsureStyle(BuiltInStyles.Subtitle);
            var existing = CoverPageEnd();
            var setup = Document.SectionOf(0).Properties;
            var (shape, titleColor, subtitleColor, alignment, indent, before, authorBefore) = CoverLayout(design, setup);
            var start = new ContentControlMarkInline(false,
                $"<w:sdtPr xmlns:w=\"{WordNamespace}\"><w:id w:val=\"{(Document.Blocks.Count * 7919 % 900000) + 100000}\" /><w:docPartObj><w:docPartGallery w:val=\"{CoverGallery}\" /><w:docPartUnique /></w:docPartObj></w:sdtPr>",
                CharacterProperties.Empty);
            var end = new ContentControlMarkInline(true, null, CharacterProperties.Empty);
            var titleFormat = new CharacterProperties { FontSize = 40, Color = titleColor };
            var blocks = new List<Block>
            {
                new Paragraph([start, shape, new TextRun(title, titleFormat)], new ParagraphProperties { StyleId = BuiltInStyles.Title, Alignment = alignment, IndentLeft = indent, SpacingBefore = before, SpacingAfter = 12 }),
                new Paragraph([new TextRun(subtitle, new CharacterProperties { FontSize = 16, Color = subtitleColor })], new ParagraphProperties { StyleId = BuiltInStyles.Subtitle, Alignment = alignment, IndentLeft = indent }),
                new Paragraph([new TextRun($"{author} · {date.ToString("d. MMMM yyyy", CultureInfo.GetCultureInfo("de-DE"))}", CharacterProperties.Empty)],
                    new ParagraphProperties { Alignment = alignment, IndentLeft = indent, SpacingBefore = authorBefore }),
                new Paragraph([new TextRun(SpecialCharacters.PageBreak.ToString(), CharacterProperties.Empty), end]),
            };
            var removed = ContentEdits.Slice(Document, 0, existing + 1);
            Apply(new BlocksStep(0, removed, blocks, new BlockReplaceMap(0, removed.Count, blocks)));
            SetSelectionQuiet(Selection.Caret(new DocumentPosition(0, 2)));
        });
    }

    /// <summary>The last block of a cover page the document starts with, or -1.</summary>
    private int CoverPageEnd()
    {
        if (Document.Blocks.Count == 0 || Document.Blocks[0] is not Paragraph first
            || first.Inlines.OfType<ContentControlMarkInline>().FirstOrDefault() is not { IsEnd: false, PropertiesXml: { } properties } || !properties.Contains(CoverGallery, StringComparison.Ordinal))
        {
            return -1;
        }

        var depth = 0;
        for (var block = 0; block < Document.Blocks.Count; block++)
        {
            if (Document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            foreach (var mark in paragraph.Inlines.OfType<ContentControlMarkInline>().Where(mark => !mark.IsInline))
            {
                depth += mark.IsEnd ? -1 : 1;
                if (depth == 0)
                {
                    return Math.Min(block, Document.Blocks.Count - 2);
                }
            }
        }

        return -1;
    }

    private static (ShapeInline Shape, TextColor Title, TextColor Subtitle, ParagraphAlignment Alignment, double Indent, double Before, double AuthorBefore) CoverLayout(CoverDesign design, SectionProperties setup)
    {
        var accent = new DrawingColor(null, "accent1", EquatableArray<(string, int?)>.Empty);
        ShapeInline Behind(double x, double y, double width, double height, DrawingShape shape) =>
            new(shape, width, height, CharacterProperties.Empty)
            {
                Anchor = new DrawingAnchor(DrawingFrame.Page, x, null, DrawingFrame.Page, y, null, DrawingWrap.None, true, 0, 0, 0, 0, 251658240),
            };
        switch (design)
        {
            case CoverDesign.Sidebar:
                var bar = 130.0;
                return (Behind(0, 0, bar, setup.PageHeight, new DrawingShape(0, 0, 0, 0) { Fill = new DrawingFill(accent) }),
                    TextColor.FromHex("2F5496"), TextColor.FromHex("595959"), ParagraphAlignment.Left, Math.Max(0, bar + 24 - setup.MarginLeft), 220, 260);
            case CoverDesign.Frame:
                return (Behind(30, 30, setup.PageWidth - 60, setup.PageHeight - 60, new DrawingShape(0, 0, 0, 0) { Line = new DrawingLine(accent, 4) }),
                    TextColor.FromHex("1F3864"), TextColor.FromHex("595959"), ParagraphAlignment.Center, 0, 240, 260);
            default:
                var band = Math.Round(setup.PageHeight * 0.42);
                return (Behind(0, 0, setup.PageWidth, band, new DrawingShape(0, 0, 0, 0) { Fill = new DrawingFill(accent) }),
                    TextColor.FromHex("FFFFFF"), TextColor.FromHex("FFFFFF"), ParagraphAlignment.Left, 0, 120, Math.Max(24, band - setup.MarginTop - 120));
        }
    }

    /// <summary>"Leere Seite": a page break, an empty paragraph on a page of its own, a page break.</summary>
    public void InsertBlankPage()
    {
        Run("Leere Seite", EditKind.Other, null, () =>
        {
            ClearWholeCells();
            var (properties, _) = TypingFormat();
            var breakRun = new TextRun(SpecialCharacters.PageBreak.ToString(), properties);
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, _selection.Range, [new Paragraph([breakRun]), new Paragraph([breakRun])]));
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
    }

    /// <summary>
    /// "Initiale": the first letter of the caret's paragraph becomes a drop cap <paramref name="lines"/> lines tall (Word's paragraph with
    /// w:framePr dropCap="drop" before the rest); 0 puts it back into the text.
    /// </summary>
    public void SetDropCap(int lines)
    {
        var block = _selection.Start.Block;
        if (block > 0 && Document.ParagraphAt(block)?.Properties.DropCapLines is null && Document.ParagraphAt(block - 1)?.Properties.DropCapLines > 0)
        {
            block--; // the caret in the paragraph after its drop cap
        }

        if (Document.ParagraphAt(block) is not { Cell: null } paragraph)
        {
            return;
        }

        var isCap = paragraph.Properties.DropCapLines > 0;
        var target = isCap ? block + 1 : block;
        if (Document.ParagraphAt(target) is not { Cell: null } text || (isCap && lines > 0 && paragraph.Properties.DropCapLines == lines))
        {
            return;
        }

        Run(lines > 0 ? "Initiale" : "Initiale entfernen", EditKind.Other, null, () =>
        {
            if (isCap)
            {
                // Back into the text: the letter (in the text's size) at the start of its paragraph.
                var letter = paragraph.Inlines.Select(inline => inline is TextRun run ? run with { Properties = run.Properties with { FontSize = null } } : inline);
                var joined = text.WithInlines([.. letter, .. text.Inlines]);
                if (lines == 0)
                {
                    Apply(new BlocksStep(block, [paragraph, text], [joined], new BlockReplaceMap(block, 2, [joined])));
                    SetSelectionQuiet(Selection.Caret(new DocumentPosition(block, 0)));
                    return;
                }

                text = joined;
                Apply(new BlocksStep(block, [paragraph, Document.Blocks[block + 1]], [joined], new BlockReplaceMap(block, 2, [joined])));
            }

            if (lines == 0 || text.Length == 0)
            {
                return;
            }

            var first = ThixOffice.Text.Unicode.TextBoundaries.NextGrapheme(text.Text, 0);
            var size = Resolver.ResolveCharacter(text, text.InlineAt(0)?.Properties ?? CharacterProperties.Empty).FontSize;
            var capInlines = text.Slice(0, first).Select(inline => inline is TextRun run ? run with { Properties = run.Properties with { FontSize = Math.Round(lines * size * 1.68 * 2) / 2 } } : inline);
            var cap = new Paragraph(capInlines, new ParagraphProperties
            {
                StyleId = text.Properties.StyleId,
                SpacingBefore = 0,
                SpacingAfter = 0,
                LineSpacing = new LineSpacing(LineSpacingRule.Exact, Math.Round(lines * size * 1.25, 1)),
                DropCapLines = lines,
                UnknownXml = $"<w:framePr w:dropCap=\"drop\" w:lines=\"{lines}\" w:wrap=\"around\" w:vAnchor=\"text\" w:hAnchor=\"text\" xmlns:w=\"{WordNamespace}\" />",
            });
            var rest = text.WithInlines(text.Slice(first, text.Length));
            Apply(new BlocksStep(block, [Document.Blocks[block]], [cap, rest], new BlockReplaceMap(block, 1, [cap, rest])));
            SetSelectionQuiet(Selection.Caret(new DocumentPosition(block + 1, 0)));
        });
    }

    /// <summary>The lines of the drop cap of the caret's paragraph (or of the paragraph it starts), 0 without one.</summary>
    public int DropCapLines =>
        Document.ParagraphAt(_selection.Start.Block) is { } paragraph
            ? paragraph.Properties.DropCapLines ?? (_selection.Start.Block > 0 && Document.ParagraphAt(_selection.Start.Block - 1)?.Properties.DropCapLines is { } before ? before : 0)
            : 0;

    // ---------------------------------------------------------------- bookmarks

    /// <summary>The bookmarks of the document a user names ("Textmarke"; Word's hidden "_Toc…" ones left out), with their positions, by name.</summary>
    public IReadOnlyList<(string Name, DocumentPosition Position)> Bookmarks()
    {
        var result = new List<(string, DocumentPosition)>();
        for (var block = 0; block < Document.Blocks.Count; block++)
        {
            if (Document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            for (var index = 0; index < paragraph.Inlines.Length; index++)
            {
                if (paragraph.Inlines[index] is OpaqueInline { Kind: PreservedKind.Bookmark } marker && marker.Xml.StartsWith("<w:bookmarkStart", StringComparison.Ordinal)
                    && BookmarkName().Match(marker.Xml) is { Success: true } match && !match.Groups[1].Value.StartsWith('_'))
                {
                    result.Add((match.Groups[1].Value, new DocumentPosition(block, paragraph.StartOf(index))));
                }
            }
        }

        return [.. result.OrderBy(item => item.Item1, StringComparer.CurrentCultureIgnoreCase)];
    }

    /// <summary>Why <paramref name="name"/> cannot name a bookmark (German, for the dialog), or null.</summary>
    public static string? BookmarkNameProblem(string? name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return "Bitte geben Sie einen Namen ein.";
        }

        return ValidBookmarkName().IsMatch(name)
            ? null
            : "Ein Textmarkenname beginnt mit einem Buchstaben und enthält nur Buchstaben, Ziffern und Unterstriche (höchstens 40 Zeichen, keine Leerzeichen).";
    }

    /// <summary>"Textmarke hinzufügen": a bookmark around the selection (at the caret: a point); one of that name is moved here.</summary>
    public void AddBookmark(string name)
    {
        if (BookmarkNameProblem(name) is { } problem)
        {
            throw new ArgumentException(problem, nameof(name));
        }

        var range = _selection.Range;
        Run("Textmarke", EditKind.Other, null, () =>
        {
            RemoveBookmarkMarkers(name);
            var id = NextBookmarkId();
            var start = new OpaqueInline($"<w:bookmarkStart w:name=\"{name}\" w:id=\"{id}\" xmlns:w=\"{WordNamespace}\" />", PreservedKind.Bookmark, false, CharacterProperties.Empty);
            var end = new OpaqueInline($"<w:bookmarkEnd w:id=\"{id}\" xmlns:w=\"{WordNamespace}\" />", PreservedKind.Bookmark, false, CharacterProperties.Empty);
            var endAt = CaretStops.Snap(Document, range.End);
            var startAt = CaretStops.Snap(Document, range.Start);
            Apply(ContentEdits.Replace(Document, new DocumentRange(endAt, endAt), [new Paragraph([end])]));
            Apply(ContentEdits.Replace(Document, new DocumentRange(startAt, startAt), [new Paragraph([start])]));
            SetSelectionQuiet(_selection);
        });
    }

    /// <summary>"Textmarke löschen": the bookmark goes, the text stays.</summary>
    public void DeleteBookmark(string name) => Run("Textmarke löschen", EditKind.Other, null, () => RemoveBookmarkMarkers(name));

    private void RemoveBookmarkMarkers(string name)
    {
        string? id = null;
        foreach (var paragraph in Document.Blocks.OfType<Paragraph>())
        {
            foreach (var marker in paragraph.Inlines.OfType<OpaqueInline>().Where(marker => marker.Kind == PreservedKind.Bookmark && marker.Xml.Contains($"w:name=\"{name}\"", StringComparison.Ordinal)))
            {
                id = BookmarkId().Match(marker.Xml) is { Success: true } match ? match.Groups[1].Value : null;
            }
        }

        if (id is null)
        {
            return;
        }

        bool IsOne(Inline inline) => inline is OpaqueInline { Kind: PreservedKind.Bookmark } marker && BookmarkId().Match(marker.Xml) is { Success: true } match && match.Groups[1].Value == id;
        for (var block = 0; block < Document.Blocks.Count; block++)
        {
            if (Document.Blocks[block] is Paragraph paragraph && paragraph.Inlines.Any(IsOne))
            {
                ApplyOptional(ContentEdits.FormatParagraphs(Document, block, block, item => item.WithInlines(item.Inlines.Where(inline => !IsOne(inline)))));
            }
        }
    }

    [GeneratedRegex(@"^\p{L}[\p{L}\p{Nd}_]{0,39}$")]
    private static partial Regex ValidBookmarkName();
}
