using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>
/// Headers and footers on the pages: every page gets the story its section names for it (first page, even pages, default – or the
/// one of a section before), laid out at the text width with its fields showing that page's number and the number of pages; the
/// header starts at the header distance from the top, the footer ends at the footer distance from the bottom. Recomputed after
/// every update (cheap: the stories' paragraph layouts are cached); a page whose header or footer looks different gets a new
/// display list.
/// </summary>
public sealed partial class DocumentLayout
{
    private readonly Dictionary<(Block Block, string Fields, double Width), ParagraphLayout> _storyLayouts = [];
    private readonly List<(Story? Header, string HeaderKey, Story? Footer, string FooterKey)> _pageStories = [];

    /// <summary>
    /// What fields show in this layout's own paragraphs (null: their last result). The editor of a header sets it to the numbers of
    /// the page being edited; setting it lays everything out again.
    /// </summary>
    public Func<FieldInline, string>? FieldText
    {
        get => _context.FieldText;
        set
        {
            _context.FieldText = value;
            _clearAll = true;
        }
    }

    /// <summary>Places the headers and footers of all pages; returns the first page that looks different now (int.MaxValue: none).</summary>
    private int PlaceHeadersAndFooters()
    {
        var firstChanged = int.MaxValue;
        var today = DateTime.Today;
        var section = -1;
        var inSection = 0;
        if (_storyLayouts.Count > 2000)
        {
            _storyLayouts.Clear();
        }

        for (var index = 0; index < _pages.Count; index++)
        {
            var page = _pages[index];
            inSection = page.Section.Index == section ? inSection + 1 : 0;
            section = page.Section.Index;
            page.IndexInSection = inSection;
            var (header, headerKind) = Document.HeaderFooterOf(section, inSection, page.Number, footer: false);
            var (footer, footerKind) = Document.HeaderFooterOf(section, inSection, page.Number, footer: true);
            var headerKey = header is { HasPageFields: true } ? $"{page.Number}/{_pages.Count}" : "";
            var footerKey = footer is { HasPageFields: true } ? $"{page.Number}/{_pages.Count}" : "";
            var stories = (header, headerKey, footer, footerKey);
            var same = index < _pageStories.Count && ReferenceEquals(_pageStories[index].Header, header) && ReferenceEquals(_pageStories[index].Footer, footer)
                       && _pageStories[index].HeaderKey == headerKey && _pageStories[index].FooterKey == footerKey;
            if (same && (page.HeaderLines.Count > 0 || header is null) && (page.FooterLines.Count > 0 || footer is null))
            {
                continue;
            }

            if (index < _pageStories.Count)
            {
                _pageStories[index] = stories;
            }
            else
            {
                _pageStories.Add(stories);
            }

            page.Header = header;
            page.HeaderKind = headerKind;
            page.Footer = footer;
            page.FooterKind = footerKind;
            var floats = new List<PlacedFloat>();
            var cells = new List<PlacedCell>();
            page.HeaderLines = header is null ? [] : PlaceStory(header, page, today, atBottom: false, floats, cells);
            page.FooterLines = footer is null ? [] : PlaceStory(footer, page, today, atBottom: true, floats, cells);
            page.HeaderFooterFloats = floats;
            page.HeaderFooterCells = cells;
            _displayLists.Remove(index);
            firstChanged = Math.Min(firstChanged, index);
        }

        if (_pageStories.Count > _pages.Count)
        {
            _pageStories.RemoveRange(_pages.Count, _pageStories.Count - _pages.Count);
        }

        return firstChanged;
    }

    /// <summary>The lines of a header (from the header distance down) or a footer (up to the footer distance) of a page.</summary>
    private PlacedLine[] PlaceStory(Story story, PageLayout page, DateTime today, bool atBottom, List<PlacedFloat>? floats = null, List<PlacedCell>? cells = null)
    {
        var setup = page.Section.Properties;
        if (cells is not null && story.Blocks.Any(block => block is Paragraph { Cell: not null }))
        {
            return PlaceStoryWithTables(story, page, today, atBottom, cells);
        }

        var layouts = LayoutStory(story, setup.ContentWidth, page.Number, _pages.Count, today);
        var height = layouts.Sum(item => item.Before + item.Layout.Height + item.After);
        var y = atBottom ? setup.PageHeight - setup.FooterDistance - height : setup.HeaderDistance;
        var left = setup.MarginLeft + setup.Gutter;
        var lines = new List<PlacedLine>();
        foreach (var (block, layout, before, after) in layouts)
        {
            y += before;
            if (floats is not null && story.Blocks[block] is Paragraph paragraph)
            {
                foreach (var (offset, inline, anchor, objectWidth, objectHeight) in AnchoredIn(paragraph))
                {
                    var (x, top) = FloatPosition(anchor, objectWidth, objectHeight, setup, y, left, setup.ContentWidth);
                    floats.Add(new PlacedFloat(block, offset, inline, anchor, x, top, objectWidth, objectHeight, IsHeaderFooter: true));
                }
            }

            for (var index = 0; index < layout.Lines.Count; index++)
            {
                lines.Add(new PlacedLine(block, index, y, left, layout.Lines[index], layout));
                y += layout.Lines[index].Height;
            }

            y += after;
        }

        return [.. lines];
    }

    private readonly Dictionary<(Story Story, string Fields, double Width), DocumentLayout> _storyTables = [];

    /// <summary>
    /// A header or footer with tables (a footer with the company's details in three columns …): the story laid out as a document of its
    /// own (tables, lists, fields with this page's numbers), its lines and cells moved onto the page.
    /// </summary>
    private PlacedLine[] PlaceStoryWithTables(Story story, PageLayout page, DateTime today, bool atBottom, List<PlacedCell> cells)
    {
        var setup = page.Section.Properties;
        var fields = story.HasPageFields ? $"{page.Number}/{_pages.Count}" : "";
        var key = (story, fields, setup.ContentWidth);
        if (!_storyTables.TryGetValue(key, out var layout))
        {
            if (_storyTables.Count > 200)
            {
                _storyTables.Clear();
            }

            var box = new SectionProperties
            {
                PageWidth = setup.ContentWidth,
                PageHeight = 20000,
                MarginLeft = 0,
                MarginRight = 0,
                MarginTop = 0,
                MarginBottom = 0,
            };
            var number = page.Number;
            var pages = _pages.Count;
            layout = new DocumentLayout(new Document(story.Blocks, Document.Styles, Document.Numbering, box, Document.Theme, Document.Settings), Measurer, Options)
            {
                FieldText = field => field.Text(number, pages, today),
            };
            _storyTables[key] = layout;
        }

        var sub = layout.Pages[0];
        var height = sub.Lines.Select(line => line.Top + line.Line.Height).Concat(sub.Cells.Select(cell => cell.Y + cell.Height)).DefaultIfEmpty(0).Max();
        var top = atBottom ? setup.PageHeight - setup.FooterDistance - height : setup.HeaderDistance;
        var left = setup.MarginLeft + setup.Gutter;
        cells.AddRange(sub.Cells.Select(cell => cell with { X = cell.X + left, Y = cell.Y + top }));
        return [.. sub.Lines.Select(line => line with { Top = line.Top + top, Left = line.Left + left })];
    }

    /// <summary>The paragraphs of a story laid out for a page (fields with its numbers), with the spacing before and after each.</summary>
    internal IReadOnlyList<(int Block, ParagraphLayout Layout, double Before, double After)> LayoutStory(Story story, double width, int page, int pages, DateTime today)
    {
        var fields = story.HasPageFields ? $"{page}/{pages}" : "";
        var result = new List<(int, ParagraphLayout, double, double)>();
        var previous = _context.FieldText;
        _context.FieldText = field => field.Text(page, pages, today);
        try
        {
            for (var block = 0; block < story.Blocks.Count; block++)
            {
                if (story.Blocks[block] is not Paragraph paragraph)
                {
                    continue;
                }

                var key = (story.Blocks[block], fields, width);
                if (!_storyLayouts.TryGetValue(key, out var layout))
                {
                    layout = ParagraphLayouter.Layout(paragraph, _context.Resolver.ResolveParagraph(paragraph), null, width, _context);
                    _storyLayouts[key] = layout;
                }

                var before = block == 0 ? 0 : layout.Format.SpacingBefore;
                var after = block == story.Blocks.Count - 1 ? 0 : layout.Format.SpacingAfter;
                result.Add((block, layout, before, after));
            }
        }
        finally
        {
            _context.FieldText = previous;
        }

        return result;
    }
}
