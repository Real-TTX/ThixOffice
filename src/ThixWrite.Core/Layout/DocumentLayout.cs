using System.Diagnostics;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>
/// The page layout of a document, kept up to date INCREMENTALLY: it listens to the document's changes, re-lays only the blocks
/// that changed (block layouts are cached per immutable block instance, width and list label) and re-paginates from the page
/// where the first change is (moving back over keep-with-next chains). Pages before it, their display lists and every
/// unchanged paragraph are reused. Updates happen lazily on the next access (or explicitly with <see cref="Update"/>).
/// <para>
/// Answers the editor control: pages, lines, hit testing (point → position), caret and selection geometry (position → rects),
/// the display list of a page, and the line / page movements of the caret (<see cref="ICaretNavigator"/>).
/// Coordinates are points, page-local (origin top left of the page); "stacked" coordinates put the pages under each other
/// with <see cref="LayoutOptions.PageGap"/> between them.
/// </para>
/// </summary>
public sealed partial class DocumentLayout : ICaretNavigator, IDisposable
{
    private const double Epsilon = 0.01;

    private readonly LayoutContext _context;
    private readonly List<ParagraphLayout?> _layouts = [];
    private readonly List<FlowState?> _states = [];
    private readonly List<BlockPlacement?> _placements = [];
    private readonly List<PageLayout> _pages = [];
    private readonly Dictionary<int, PageDisplayList> _displayLists = [];
    private readonly Dictionary<Block, ParagraphLayout> _rescued = new(ReferenceEqualityComparer.Instance);
    private ListLabel?[] _labels = [];
    private double[] _pageTops = [];
    private int _dirtyFrom;
    private bool _clearAll = true;

    public DocumentLayout(Document document, ITextMeasurer measurer, LayoutOptions? options = null, StyleResolver? resolver = null)
    {
        Document = document;
        Measurer = measurer;
        Options = options ?? LayoutOptions.Default;
        _context = new LayoutContext(measurer, resolver ?? new StyleResolver(document)) { MeasureNested = MeasureNested };
        Document.Changed += OnDocumentChanged;
        _dirtyFrom = 0;
    }

    /// <summary>Raised after an update changed pages (from <see cref="LayoutChangedEventArgs.FirstChangedPage"/> on).</summary>
    public event EventHandler<LayoutChangedEventArgs>? Changed;

    public Document Document { get; }

    public ITextMeasurer Measurer { get; }

    public LayoutOptions Options { get; }

    /// <summary>The height a Page Up / Page Down moves (the editor sets its viewport height; default: one page).</summary>
    public double? ViewportHeight { get; set; }

    /// <summary>What the last update did.</summary>
    public LayoutUpdateInfo LastUpdate { get; private set; }

    /// <summary>The pages (up to date).</summary>
    public IReadOnlyList<PageLayout> Pages
    {
        get
        {
            Update();
            return _pages;
        }
    }

    public int PageCount => Pages.Count;

    /// <summary>The layout of block <paramref name="block"/>.</summary>
    public ParagraphLayout GetBlockLayout(int block)
    {
        Update();
        return _layouts[block]!;
    }

    /// <summary>Brings the layout up to date with the document (cheap when nothing changed).</summary>
    public void Update()
    {
        if (_dirtyFrom == int.MaxValue && !_clearAll)
        {
            return;
        }

        var watch = Stopwatch.StartNew();
        if (_clearAll)
        {
            Reset();
        }

        var labels = ListNumbering.Compute(Document, _context.Resolver);
        for (var index = 0; index < labels.Length; index++)
        {
            var old = index < _labels.Length ? _labels[index] : null;
            if (old != labels[index])
            {
                _dirtyFrom = Math.Min(_dirtyFrom, index);
            }
        }

        _labels = labels;
        var noteStarts = ComputeNoteStarts();
        for (var index = 0; index < noteStarts.Length; index++)
        {
            var old = index < _noteStarts.Length ? _noteStarts[index] : (0, 0);
            if (old != noteStarts[index])
            {
                _dirtyFrom = Math.Min(_dirtyFrom, index);
                break;
            }
        }

        _noteStarts = noteStarts;
        var start = ResumeBlock(Math.Min(_dirtyFrom, Document.Blocks.Count - 1));
        var laidOut = Paginate(start);
        _dirtyFrom = int.MaxValue;
        _rescued.Clear();

        var firstPage = _states[start]?.Page ?? 0;
        foreach (var page in _displayLists.Keys.Where(page => page >= firstPage).ToList())
        {
            _displayLists.Remove(page);
        }

        PlaceFootnoteAreas(firstPage);

        _pageTops = BuildPageTops();
        firstPage = Math.Min(firstPage, PlaceHeadersAndFooters());
        LastUpdate = new LayoutUpdateInfo(laidOut, firstPage, _pages.Count, watch.Elapsed);
        Changed?.Invoke(this, new LayoutChangedEventArgs(firstPage, _pages.Count));
    }

    // ---------------------------------------------------------------- geometry

    /// <summary>The y of the top of page <paramref name="page"/> in stacked coordinates.</summary>
    public double PageTop(int page)
    {
        Update();
        return _pageTops[Math.Clamp(page, 0, _pageTops.Length - 1)];
    }

    /// <summary>The height of all pages stacked (with gaps).</summary>
    public double StackedHeight
    {
        get
        {
            Update();
            return _pages.Count == 0 ? 0 : _pageTops[^1] + _pages[^1].Height;
        }
    }

    /// <summary>The page at stacked y <paramref name="y"/> and the y within it.</summary>
    public (int Page, double Y) PageAt(double y)
    {
        Update();
        var index = Array.BinarySearch(_pageTops, y);
        index = index >= 0 ? index : Math.Max(0, ~index - 1);
        return (index, y - _pageTops[index]);
    }

    /// <summary>The caret place at a point of page <paramref name="page"/> (nearest line, nearest caret stop).</summary>
    public HitResult HitTest(int page, double x, double y)
    {
        Update();
        var pageLayout = _pages[Math.Clamp(page, 0, _pages.Count - 1)];
        var line = NearestLine(pageLayout, x, y);
        if (line is null)
        {
            var position = CaretStops.Snap(Document, new DocumentPosition(NearestBlockOn(pageLayout.Index), 0));
            return new HitResult(new CaretPlace(position), pageLayout.Index);
        }

        return HitLine(pageLayout.Index, line, x);
    }

    /// <summary>The caret place at a point in stacked coordinates.</summary>
    public HitResult HitTestStacked(double x, double y)
    {
        var (page, pageY) = PageAt(y);
        return HitTest(page, x, pageY);
    }

    /// <summary>Where the caret is drawn for <paramref name="place"/>.</summary>
    public CaretGeometry GetCaret(CaretPlace place)
    {
        var (page, placed) = Locate(place);
        var x = placed.Left + placed.Line.XOf(place.Position.Offset);
        var top = placed.Top + placed.Line.Baseline - placed.Line.Ascent;
        return new CaretGeometry(page, x, top, placed.Line.Ascent + placed.Line.Descent);
    }

    /// <summary>The page <paramref name="position"/> is on.</summary>
    public int PageOf(DocumentPosition position) => Locate(new CaretPlace(position)).Page;

    /// <summary>The rectangles that highlight <paramref name="range"/>, per line (whole line height, the mark included across lines).</summary>
    public IReadOnlyList<PageRect> GetSelectionRects(DocumentRange range)
    {
        Update();
        var rects = new List<PageRect>();
        if (range.IsEmpty)
        {
            return rects;
        }

        // Whole cells: the cells are highlighted, not their lines (as in Word).
        var wholeCells = TableSelection.WholeCells(Document, new Selection(range.Start, range.End));
        foreach (var cells in wholeCells)
        {
            AddCellRects(cells, rects);
        }

        for (var block = range.Start.Block; block <= range.End.Block && block < _placements.Count; block++)
        {
            if (wholeCells.FirstOrDefault(cells => cells.Span.Contains(block)) is { } table)
            {
                block = table.Span.End;
                continue;
            }

            var placement = _placements[block];
            if (placement is null)
            {
                continue;
            }

            for (var lineIndex = 0; lineIndex < placement.Lines.Length; lineIndex++)
            {
                var placed = placement.Lines[lineIndex];
                var line = placed.Line;
                if (placed.Paragraph.IsPreserved)
                {
                    var isSelected = range.Start <= new DocumentPosition(block, 0) && range.End > new DocumentPosition(block, 0);
                    if (isSelected)
                    {
                        rects.Add(new PageRect(placement.Pages[lineIndex], placed.Left, placed.Top, placed.Paragraph.Width, line.Height));
                    }

                    continue;
                }

                var lineStart = new DocumentPosition(block, line.Start);
                var lineEnd = new DocumentPosition(block, line.End);
                if (range.End < lineStart || range.Start > lineEnd || (range.End == lineStart && range.Start < lineStart))
                {
                    continue;
                }

                var from = range.Start.Block == block ? Math.Max(range.Start.Offset, line.Start) : line.Start;
                var to = range.End.Block == block ? Math.Min(range.End.Offset, line.End) : line.End;
                var x1 = line.XOf(from);
                var x2 = line.XOf(to);
                var continuesPastLine = range.End > lineEnd || (range.End == lineEnd && line.Ending != LineEnding.Wrap && range.End.Block > block);
                if (continuesPastLine && line.Ending != LineEnding.Wrap)
                {
                    x2 = line.StopX[^1] + line.MarkWidth;
                }

                if (x2 - x1 < Epsilon && !continuesPastLine)
                {
                    continue;
                }

                rects.Add(new PageRect(placement.Pages[lineIndex], placed.Left + x1, placed.Top, Math.Max(x2 - x1, line.MarkWidth / 2), line.Height));
            }
        }

        return rects;
    }

    private void AddCellRects(TableCellRange cells, List<PageRect> rects)
    {
        var selected = cells.Cells.ToHashSet();
        var first = _placements[cells.Span.Start]?.Pages.FirstOrDefault() ?? 0;
        var last = _placements[cells.Span.End]?.Pages.LastOrDefault() ?? first;
        for (var page = first; page <= last && page < _pages.Count; page++)
        {
            foreach (var cell in _pages[page].Cells)
            {
                if (!cell.IsRepeat && cell.TableStart == cells.Span.Start && selected.Contains((cell.Row, cell.Cell)))
                {
                    rects.Add(new PageRect(page, cell.X, cell.Y, cell.Width, cell.Height));
                }
            }
        }
    }

    /// <summary>The rectangle of the inline object (a picture) at <paramref name="position"/>, or null when there is none.</summary>
    public PageRect? GetObjectRect(DocumentPosition position)
    {
        Update();
        for (var index = 0; index < _pages.Count; index++)
        {
            if (_pages[index].Floats.FirstOrDefault(placed => placed.Block == position.Block && placed.Offset == position.Offset) is { } floating)
            {
                return new PageRect(index, floating.X, floating.Y, floating.Width, floating.Height);
            }
        }

        var (page, placed) = Locate(new CaretPlace(position));
        foreach (var item in placed.Line.Items)
        {
            if (item is ObjectItem box && box.Start == position.Offset)
            {
                var height = box.Height > 0 ? box.Height : placed.Line.Ascent;
                return new PageRect(page, placed.Left + box.X, placed.Top + placed.Line.Baseline - height, box.Width, height);
            }
        }

        return null;
    }

    /// <summary>The position of the picture under a point of a page, or null.</summary>
    public DocumentPosition? ImageAt(int page, double x, double y)
    {
        Update();
        if (page < 0 || page >= _pages.Count)
        {
            return null;
        }

        // Floating pictures first (in front of the text), the topmost of them.
        if (_pages[page].Floats.Where(placed => placed.Inline is ImageInline && placed.Contains(x, y)).OrderByDescending(placed => placed.Anchor.ZOrder).FirstOrDefault() is { } floating)
        {
            return new DocumentPosition(floating.Block, floating.Offset);
        }

        foreach (var placed in _pages[page].Lines)
        {
            if (y < placed.Top || y > placed.Top + placed.Line.Height)
            {
                continue;
            }

            foreach (var item in placed.Line.Items)
            {
                if (item is ObjectItem { Inline: ImageInline } box)
                {
                    var top = placed.Top + placed.Line.Baseline - box.Height;
                    if (x >= placed.Left + box.X && x <= placed.Left + box.X + box.Width && y >= top && y <= top + box.Height)
                    {
                        return new DocumentPosition(placed.Block, box.Start);
                    }
                }
            }
        }

        return null;
    }

    /// <summary>The cell placed at a point of a page (not a repeated heading row), or null.</summary>
    public PlacedCell? CellAt(int page, double x, double y)
    {
        Update();
        return page < 0 || page >= _pages.Count ? null : _pages[page].Cells.FirstOrDefault(cell => !cell.IsRepeat && cell.Contains(x, y));
    }

    /// <summary>The page the first line of block <paramref name="block"/> is on.</summary>
    public int PageOfBlock(int block)
    {
        Update();
        return _placements[Math.Clamp(block, 0, _placements.Count - 1)]?.Pages.FirstOrDefault() ?? 0;
    }

    public int PageNumberOf(int block) => PageOfBlock(block) + 1;

    /// <summary>The display list of page <paramref name="page"/> (cached until the page changes).</summary>
    public PageDisplayList GetDisplayList(int page)
    {
        Update();
        if (_displayLists.TryGetValue(page, out var cached))
        {
            return cached;
        }

        var list = DisplayListBuilder.Build(_pages[page], DrawObject, Document.PageColor is { } color ? RgbColor.FromHex(color) : null);
        _displayLists[page] = list;
        return list;
    }

    /// <summary>The display lists of all pages (printing, PDF).</summary>
    public IEnumerable<PageDisplayList> GetDisplayLists()
    {
        Update();
        for (var page = 0; page < _pages.Count; page++)
        {
            yield return GetDisplayList(page);
        }
    }

    // ---------------------------------------------------------------- ICaretNavigator

    public double CaretX(CaretPlace place) => GetCaret(place).X;

    public CaretPlace? MoveByLine(CaretPlace place, int direction, double goalX)
    {
        var (page, placed) = Locate(place);
        var pageIndex = page;
        var edge = direction > 0 ? placed.Top + placed.Line.Height - Epsilon : placed.Top + Epsilon;
        var candidate = NextLineVertically(_pages[pageIndex], placed, edge, direction, goalX);
        while (candidate is null)
        {
            pageIndex += direction;
            if (pageIndex < 0 || pageIndex >= _pages.Count)
            {
                return null;
            }

            candidate = NextLineVertically(_pages[pageIndex], null, direction > 0 ? double.MinValue : double.MaxValue, direction, goalX);
        }

        return HitLine(pageIndex, candidate, goalX).Place;
    }

    /// <summary>
    /// The line one step above / below <paramref name="edge"/> on a page: of the lines beyond it, the nearest whose width holds
    /// <paramref name="goalX"/> (so that the caret goes down a table column and leaves the table below it), else the nearest.
    /// </summary>
    private static PlacedLine? NextLineVertically(PageLayout page, PlacedLine? from, double edge, int direction, double goalX)
    {
        PlacedLine? best = null;
        PlacedLine? bestInColumn = null;
        foreach (var line in page.Lines)
        {
            if (ReferenceEquals(line, from) || line.Paragraph.IsPreserved)
            {
                continue;
            }

            var beyond = direction > 0 ? line.Top >= edge : line.Top + line.Line.Height <= edge;
            if (!beyond)
            {
                continue;
            }

            if (best is null || Closer(line, best, direction))
            {
                best = line;
            }

            var holdsX = goalX >= line.Left - Epsilon && goalX <= line.Left + line.Paragraph.Width + Epsilon;
            if (holdsX && (bestInColumn is null || Closer(line, bestInColumn, direction)))
            {
                bestInColumn = line;
            }
        }

        return bestInColumn ?? best;

        static bool Closer(PlacedLine line, PlacedLine than, int direction) => direction > 0 ? line.Top < than.Top : line.Top > than.Top;
    }

    public CaretPlace MoveByPage(CaretPlace place, int direction, double goalX)
    {
        var caret = GetCaret(place);
        var height = ViewportHeight ?? _pages[caret.Page].Height;
        var target = PageTop(caret.Page) + caret.Top + caret.Height / 2 + direction * height;
        target = Math.Clamp(target, 0, StackedHeight - 0.1);
        var hit = HitTestStacked(goalX, target);
        return hit.Place;
    }

    public CaretPlace LineStart(CaretPlace place)
    {
        var (_, placed) = Locate(place);
        return new CaretPlace(new DocumentPosition(placed.Block, placed.Line.Stops[0]));
    }

    public CaretPlace LineEnd(CaretPlace place)
    {
        var (_, placed) = Locate(place);
        var line = placed.Line;
        if (line.Ending == LineEnding.Wrap)
        {
            return new CaretPlace(new DocumentPosition(placed.Block, line.End), CaretAffinity.Upstream);
        }

        return new CaretPlace(new DocumentPosition(placed.Block, line.Stops[^1]));
    }

    public void Dispose() => Document.Changed -= OnDocumentChanged;

    // ---------------------------------------------------------------- change tracking

    private void OnDocumentChanged(object? sender, DocumentChange change)
    {
        switch (change.Kind)
        {
            case DocumentChangeKind.Blocks:
                Splice(change.StartBlock, change.RemovedCount, change.InsertedCount);
                _dirtyFrom = Math.Min(_dirtyFrom, change.StartBlock);
                break;
            case DocumentChangeKind.Numbering:
                _dirtyFrom = Math.Min(_dirtyFrom, 0);
                break;
            default:
                _clearAll = true;
                break;
        }
    }

    private void Splice(int start, int removed, int inserted)
    {
        var expectedBefore = Document.Blocks.Count - inserted + removed;
        if (_clearAll || _layouts.Count != expectedBefore)
        {
            // Not laid out yet (or out of step): the next update starts from scratch anyway.
            _clearAll = true;
            return;
        }

        for (var index = start; index < start + removed && index < _layouts.Count; index++)
        {
            if (_layouts[index] is { } layout)
            {
                _rescued[layout.Block] = layout;
            }
        }

        var removable = Math.Max(0, Math.Min(removed, _layouts.Count - start));
        _layouts.RemoveRange(start, removable);
        _states.RemoveRange(start, removable);
        _placements.RemoveRange(start, removable);
        for (var index = 0; index < inserted; index++)
        {
            var block = Document.Blocks[start + index];
            _layouts.Insert(start + index, _rescued.TryGetValue(block, out var layout) ? layout : null);
            _states.Insert(start + index, null);
            _placements.Insert(start + index, null);
        }

        // The block after a pure removal (a deleted table) kept the state from after the removed blocks: it is stale now.
        if (inserted == 0 && start < _states.Count)
        {
            _states[start] = null;
        }

        // Labels shift with the blocks, so that only real label changes count as changes.
        var labels = _labels.ToList();
        if (start <= labels.Count)
        {
            labels.RemoveRange(start, Math.Max(0, Math.Min(removed, labels.Count - start)));
            labels.InsertRange(start, Enumerable.Repeat<ListLabel?>(null, inserted));
            _labels = [.. labels];
        }
    }

    private void Reset()
    {
        _layouts.Clear();
        _states.Clear();
        _placements.Clear();
        _pages.Clear();
        _displayLists.Clear();
        _rescued.Clear();
        _context.Clear();
        _storyLayouts.Clear();
        _pageStories.Clear();
        _noteLayouts.Clear();
        _labels = [];
        _noteStarts = [];
        for (var index = 0; index < Document.Blocks.Count; index++)
        {
            _layouts.Add(null);
            _states.Add(null);
            _placements.Add(null);
        }

        _dirtyFrom = 0;
        _clearAll = false;
    }

    /// <summary>The block to re-paginate from: before a change, back over the keep-with-next chain that leads to it.</summary>
    private int ResumeBlock(int dirty)
    {
        var block = Math.Max(0, dirty);
        while (block > 0 && (_states[block] is null || PreviousKeepsWithThis(block) || IsInsideTable(block)))
        {
            block--;
        }

        return block;
    }

    private bool PreviousKeepsWithThis(int block) => _layouts[block - 1] is { } previous && previous.Format.KeepWithNext;

    /// <summary>True for a block of a table that is not its first (a table is paginated from its first block).</summary>
    private bool IsInsideTable(int block) => Document.Blocks[block] is Paragraph { Cell: { } cell } && IsSameTable(block - 1, cell.Table);

    private bool IsSameTable(int block, Table table) => block >= 0 && Document.Blocks[block] is Paragraph { Cell: { } cell } && ReferenceEquals(cell.Table, table);

    // ---------------------------------------------------------------- layout of blocks

    private ParagraphLayout EnsureLayout(int block, double width)
    {
        var cached = _layouts[block];
        var current = Document.Blocks[block];
        var label = block < _labels.Length ? _labels[block] : null;
        var notes = block < _noteStarts.Length ? _noteStarts[block] : (0, 0);
        if (cached is not null && !cached.IsWrapped && ReferenceEquals(cached.Block, current) && Math.Abs(cached.Width - width) < Epsilon && cached.Label == label?.Text && cached.NoteStart == notes)
        {
            return cached;
        }

        _laidOut++;
        _context.NoteStart = notes;
        var layout = current switch
        {
            Paragraph paragraph => ParagraphLayouter.Layout(paragraph, _context.Resolver.ResolveParagraph(paragraph), label, width, _context),
            OpaqueBlock opaque => ParagraphLayouter.LayoutPreserved(opaque, width, _context),
            _ => throw new NotSupportedException($"Unknown block {current.GetType().Name}."),
        };
        layout.NoteStart = notes;
        _layouts[block] = layout;
        return layout;
    }

    private int _laidOut;

    // ---------------------------------------------------------------- pagination

    private sealed record FlowState(int Page, double Y, double PendingAfter, string? PreviousStyle, bool PreviousContextual, bool AfterHardBreak, int Section, System.Collections.Immutable.ImmutableList<NoteSlice>? Carry = null,
        int Column = 0, double ColumnTop = double.NaN);

    private sealed class BlockPlacement(PlacedLine[] lines, int[] pages)
    {
        public PlacedLine[] Lines { get; } = lines;

        public int[] Pages { get; } = pages;
    }

    private int Paginate(int startBlock)
    {
        _laidOut = 0;
        var state = _states[startBlock];
        if (startBlock == 0 || state is null)
        {
            _pages.Clear();
            startBlock = 0;
            state = new FlowState(0, Document.SectionOf(0).Properties.MarginTop, 0, null, false, true, 0, null, 0, Document.SectionOf(0).Properties.MarginTop);
            _pages.Add(new PageLayout(0, Document.SectionOf(0)));
        }
        else
        {
            _pages.RemoveRange(state.Page + 1, _pages.Count - state.Page - 1);
            _pages[state.Page].RemoveFrom(startBlock);
        }

        _restarts = 0;
        ForgetEarlyFloats(startBlock);
        var flow = new Flow(this, state);
        flow.SeedFloats();
        for (var block = startBlock; block < Document.Blocks.Count; block++)
        {
            _states[block] = flow.State;
            flow.Place(block);
            if (flow.RestartFrom is { } restart && _states[restart] is { } again)
            {
                // An object on the page reaches up beside text placed before its anchor: the page again, with the object on it.
                _pages.RemoveRange(again.Page + 1, _pages.Count - again.Page - 1);
                _pages[again.Page].RemoveFrom(restart);
                flow = new Flow(this, again);
                flow.SeedFloats();
                block = restart - 1;
            }
        }

        flow.Finish();
        return _laidOut;
    }

    /// <summary>The pagination of the blocks from a saved state on (one pass, keeps every rule local to the block it concerns).</summary>
    private sealed partial class Flow(DocumentLayout owner, FlowState start)
    {
        private int _page = start.Page;
        private double _y = start.Y;
        private double _pendingAfter = start.PendingAfter;
        private string? _previousStyle = start.PreviousStyle;
        private bool _previousContextual = start.PreviousContextual;
        private bool _afterHardBreak = start.AfterHardBreak;
        private int _section = start.Section;
        private int _column = start.Column;
        private double _columnTop = double.IsNaN(start.ColumnTop) ? start.Y : start.ColumnTop;

        public FlowState State => new(_page, _y, _pendingAfter, _previousStyle, _previousContextual, _afterHardBreak, _section, _carry, _column, _columnTop);

        /// <summary>The setup of the section being placed (the page keeps the one it started with; a continuous section may change the columns).</summary>
        private SectionProperties Current => owner.Document.Sections[Math.Min(_section, owner.Document.Sections.Count - 1)].Properties;

        /// <summary>The column the text goes into: its left edge on the page and its width.</summary>
        private (double Left, double Width) ColumnArea
        {
            get
            {
                var frames = Current.ColumnFrames();
                var (left, width) = frames[Math.Min(_column, frames.Count - 1)];
                return (Setup.MarginLeft + Setup.Gutter + left, width);
            }
        }

        private PageLayout Page => owner._pages[_page];

        private SectionProperties Setup => Page.Section.Properties;

        private double Bottom => Setup.PageHeight - Setup.MarginBottom - Page.FootnoteHeight;

        public void Place(int block)
        {
            var section = owner.Document.SectionOf(block);
            if (section.Index != _section)
            {
                StartSection(section);
            }

            if (owner.Document.Blocks[block] is Paragraph { Cell: { } cell })
            {
                if (!owner.IsSameTable(block - 1, cell.Table))
                {
                    PlaceTable(block, section);
                }

                return; // the other blocks of a table are placed with its first one
            }

            var (areaLeft, areaWidth) = ColumnArea;
            var layout = owner.EnsureLayout(block, areaWidth);
            var format = layout.Format;
            if (format.PageBreakBefore && Page.Lines.Count > 0)
            {
                NewPage(hard: true);
            }

            var style = format.StyleId;
            var before = format.SpacingBefore;
            var after = _pendingAfter;
            if (style == _previousStyle && format.ContextualSpacing)
            {
                before = 0;
            }

            if (style == _previousStyle && _previousContextual)
            {
                after = 0;
            }

            var spacing = SpacingAt(before, after);
            var available = Bottom - (_y + spacing);
            var fullPage = Setup.PageHeight - Setup.MarginTop - Setup.MarginBottom;
            if (Page.Lines.Count > 0 && format.KeepWithNext && !owner.PreviousKeepsWithThisLaidOut(block))
            {
                var chain = ChainHeight(block, spacing);
                if (chain > available + Epsilon && chain <= fullPage)
                {
                    NextColumn(); // the next column, or a new page after the last
                    spacing = SpacingAt(before, after);
                    available = Bottom - (_y + spacing);
                }
            }

            var keepTogether = format.KeepLinesTogether || layout.IsPreserved;
            if (Page.Lines.Count > 0 && keepTogether && layout.Height > available + Epsilon && layout.Height <= fullPage)
            {
                NextColumn();
                spacing = SpacingAt(before, after);
            }

            _y += spacing;
            var (borderTop, borderBottom) = owner.BorderSpace(block, format);
            _y += borderTop;
            if (owner.Document.Blocks[block] is Paragraph paragraph)
            {
                (areaLeft, areaWidth) = ColumnArea;
                if (Math.Abs(layout.Width - areaWidth) > Epsilon)
                {
                    layout = owner.EnsureLayout(block, areaWidth); // a page break above moved the paragraph into other columns
                }

                PlaceFloats(block, paragraph, _y, areaLeft, areaWidth);
                if (RestartFrom is not null)
                {
                    return; // the page is laid out again with the object on it from the start
                }

                if (format.DropCapLines > 0 && block + 1 < owner.Document.Blocks.Count && owner.Document.Blocks[block + 1] is Paragraph { Cell: null })
                {
                    PlaceDropCap(block, layout, areaLeft, areaWidth);
                    return;
                }

                if (Page.Floats.Count > 0)
                {
                    layout = Wrapped(block, paragraph, layout, areaLeft, areaWidth);
                }
            }

            PlaceLines(block, layout);
            _y += borderBottom;

            _pendingAfter = format.SpacingAfter;
            _previousStyle = style;
            _previousContextual = format.ContextualSpacing;
        }

        /// <summary>
        /// An "Initiale": the big letter's paragraph is placed where the next paragraph starts, and that paragraph flows beside it (the
        /// letter's box is an area the text keeps clear of, like a floating object with square wrap).
        /// </summary>
        private void PlaceDropCap(int block, ParagraphLayout layout, double areaLeft, double areaWidth)
        {
            var top = _y;
            PlaceLines(block, layout);
            var height = Math.Max(1, _y - top);
            var width = layout.Lines.Select(line => line.Items.Select(item => item.X + item.Width).DefaultIfEmpty(0).Max()).DefaultIfEmpty(0).Max();
            var frame = new DrawingAnchor(DrawingFrame.Column, 0, null, DrawingFrame.Paragraph, 0, null, DrawingWrap.Square, false, 0, 0, 4, 0);
            Page.MutableFloats.Add(new PlacedFloat(block, 0, new TextRun("", CharacterProperties.Empty), frame, areaLeft, top, Math.Max(1, width), height)
            {
                AreaLeft = areaLeft,
                AreaRight = areaLeft + areaWidth,
            });
            _y = top;
            _pendingAfter = 0;
        }

        private void PlaceLines(int block, ParagraphLayout layout)
        {
            var lines = layout.Lines;
            var placed = new List<PlacedLine>(lines.Count);
            var pages = new List<int>(lines.Count);
            var left = ColumnArea.Left;
            var firstOnPage = 0;
            var pageHadContent = Page.Lines.Count > 0;
            var index = 0;
            while (index < lines.Count)
            {
                var line = lines[index];
                var notes = owner.NotesIn(block, line);
                if (_y + line.Height + NoteReserve(notes, Setup.ContentWidth) > Bottom + Epsilon && Page.Lines.Count > 0)
                {
                    var split = index;
                    if (layout.Format.WidowControl && lines.Count > 1 && !layout.IsPreserved)
                    {
                        if (lines.Count - split == 1 && split - firstOnPage >= 2)
                        {
                            split--;
                        }

                        if (split - firstOnPage == 1 && firstOnPage == 0)
                        {
                            split = 0;
                        }

                        if (split == firstOnPage && !pageHadContent)
                        {
                            split = index;
                        }
                    }

                    if (split < index)
                    {
                        Page.MutableLines.RemoveRange(Page.Lines.Count - (index - split), index - split);
                        placed.RemoveRange(split, index - split);
                        pages.RemoveRange(split, index - split);
                        RemoveFootnotesFrom(block, split);
                        index = split;
                    }

                    NextColumn();
                    left = ColumnArea.Left;
                    if (index > 0 && owner.Document.Blocks[block] is Paragraph paragraph && !layout.IsPreserved)
                    {
                        layout = Rewrapped(block, paragraph, layout, index, left);
                        lines = layout.Lines;
                    }

                    firstOnPage = index;
                    pageHadContent = Page.Lines.Count > 0;
                    line = lines[index];
                    notes = owner.NotesIn(block, line);
                }

                var placedLine = new PlacedLine(block, index, _y, left, line, layout);
                Page.MutableLines.Add(placedLine);
                placed.Add(placedLine);
                pages.Add(_page);
                _y += line.Height;
                if (notes.Count > 0)
                {
                    AddFootnotes(block, index, notes, _y);
                }

                index++;

                if (line.Ending is LineEnding.PageBreak or LineEnding.ColumnBreak)
                {
                    if (line.Ending == LineEnding.ColumnBreak)
                    {
                        NextColumn();
                    }
                    else
                    {
                        NewPage(hard: true);
                    }

                    left = ColumnArea.Left;
                    firstOnPage = index;
                    pageHadContent = Page.Lines.Count > 0;
                }
            }

            owner._placements[block] = new BlockPlacement([.. placed], [.. pages]);
        }

        private double SpacingAt(double before, double after)
        {
            if (Page.Lines.Count > 0)
            {
                return before + after;
            }

            // At the top of a page: space before only after a hard break or at the start of the document / section.
            return _afterHardBreak ? before : 0;
        }

        private double ChainHeight(int block, double spacing)
        {
            var height = spacing;
            var document = owner.Document;
            var index = block;
            var width = document.SectionOf(block).Properties.ContentWidth;
            while (index < document.Blocks.Count)
            {
                if (document.Blocks[index] is Paragraph { Cell: not null })
                {
                    break; // a table: its rows decide on their own where they go
                }

                var layout = owner.EnsureLayout(index, width);
                if (index > block)
                {
                    height += layout.Format.SpacingBefore;
                }

                if (!layout.Format.KeepWithNext || index == document.Blocks.Count - 1 || document.SectionOf(index + 1).Index != document.SectionOf(index).Index)
                {
                    // The first line of the paragraph the chain keeps with.
                    height += index == block ? layout.Height : layout.Lines[0].Height;
                    break;
                }

                height += layout.Height + layout.Format.SpacingAfter;
                index++;
            }

            return height;
        }

        private void StartSection(SectionInfo section)
        {
            var continuous = section.Properties.Start == SectionStart.Continuous
                && Math.Abs(section.Properties.PageWidth - Setup.PageWidth) < Epsilon
                && Math.Abs(section.Properties.PageHeight - Setup.PageHeight) < Epsilon;
            if (continuous)
            {
                BalanceColumns(); // the columns of the section before end level, the new section starts below them
            }

            CloseColumnRegion();
            _section = section.Index;
            if (continuous || (Page.Lines.Count == 0 && _page == 0))
            {
                if (Page.Lines.Count == 0)
                {
                    ReplaceEmptyPage(section);
                }

                _column = 0;
                _columnTop = _y;
                return;
            }

            NewPage(hard: true, section);
            var parity = section.Properties.Start switch
            {
                SectionStart.EvenPage => 1,
                SectionStart.OddPage => 0,
                _ => -1,
            };
            if (parity >= 0 && _page % 2 != parity)
            {
                NewPage(hard: true, section);
            }
        }

        private void ReplaceEmptyPage(SectionInfo section)
        {
            owner._pages[_page] = new PageLayout(_page, section);
            _y = section.Properties.MarginTop;
        }

        /// <summary>Continues in the next column of the section, or on a new page after the last one.</summary>
        private void NextColumn()
        {
            if (_column < Current.ColumnFrames().Count - 1)
            {
                _column++;
                _y = _columnTop;
                _pendingAfter = 0;
                return;
            }

            NewPage(hard: false);
        }

        /// <summary>
        /// Word's balanced columns before a continuous section break: the lines of the section's columns on this page are shared out so
        /// that the columns end at about the same height (not when a table is among them).
        /// </summary>
        private void BalanceColumns()
        {
            var frames = Current.ColumnFrames();
            var info = owner.Document.Sections[Math.Min(_section, owner.Document.Sections.Count - 1)];
            if (frames.Count < 2)
            {
                return;
            }

            var lines = Page.Lines.Select((line, index) => (line, index))
                .Where(item => item.line.Block >= info.StartBlock && item.line.Block <= info.EndBlock && item.line.Top >= _columnTop - Epsilon)
                .ToList();
            if (lines.Count < 2 || Page.Cells.Any(cell => cell.TableStart >= info.StartBlock && cell.TableStart <= info.EndBlock))
            {
                return;
            }

            // The flow of the lines as one column: every line with the gap above it (0 at the top of a column).
            var flow = new List<(int Index, PlacedLine Line, double Gap)>();
            PlacedLine? previous = null;
            foreach (var (line, index) in lines)
            {
                var gap = previous is null || line.Left != previous.Left ? 0 : Math.Max(0, line.Top - (previous.Top + previous.Line.Height));
                flow.Add((index, line, gap));
                previous = line;
            }

            var total = flow.Sum(item => item.Gap + item.Line.Line.Height);
            var target = total / frames.Count;
            var column = 0;
            var y = _columnTop;
            var bottom = _columnTop;
            var marginLeft = Setup.MarginLeft + Setup.Gutter;
            var breakAfter = false;
            foreach (var (index, line, gap) in flow)
            {
                var g = y > _columnTop ? gap : 0;
                if ((breakAfter || (y > _columnTop && y + g + line.Line.Height / 2 > _columnTop + target)) && column < frames.Count - 1)
                {
                    column++;
                    y = _columnTop;
                    g = 0;
                }

                var moved = line with { Top = y + g, Left = marginLeft + frames[column].Left };
                Page.MutableLines[index] = moved;
                if (owner._placements[line.Block] is { } placement)
                {
                    var at = Array.IndexOf(placement.Lines, line);
                    if (at >= 0)
                    {
                        placement.Lines[at] = moved;
                    }
                }

                y += g + line.Line.Height;
                bottom = Math.Max(bottom, y);
                breakAfter = line.Line.Ending == LineEnding.ColumnBreak;
            }

            _y = bottom;
            _column = 0;
        }

        /// <summary>Notes the line between the columns of the region that ends here (w:cols w:sep).</summary>
        private void CloseColumnRegion()
        {
            var setup = Current;
            var frames = setup.ColumnFrames();
            if (!setup.ColumnSeparator || frames.Count < 2)
            {
                return;
            }

            var bottom = Page.Lines.Where(line => line.Top >= _columnTop - Epsilon).Select(line => line.Top + line.Line.Height).DefaultIfEmpty(_columnTop).Max();
            if (bottom <= _columnTop + Epsilon)
            {
                return;
            }

            var marginLeft = Setup.MarginLeft + Setup.Gutter;
            for (var index = 1; index < frames.Count; index++)
            {
                var x = marginLeft + (frames[index - 1].Left + frames[index - 1].Width + frames[index].Left) / 2;
                Page.MutableColumnLines.Add((x, _columnTop, bottom));
            }
        }

        private void NewPage(bool hard, SectionInfo? section = null)
        {
            CloseColumnRegion();
            _column = 0;
            var info = section ?? Page.Section;
            _page++;
            owner._pages.Add(new PageLayout(_page, info));
            _y = info.Properties.MarginTop;
            _columnTop = _y;
            _afterHardBreak = hard;
            _pendingAfter = 0;
            SeedFloats();
            PlaceCarry();
        }
    }

    private bool PreviousKeepsWithThisLaidOut(int block) => block > 0 && _layouts[block - 1] is { Format.KeepWithNext: true };

    /// <summary>
    /// The room a paragraph's borders take ("Rahmen": line width + its distance to the text), above and below. Paragraphs with the same
    /// borders and indents form one box (as Word draws them): the first gets the top line, the last the bottom one, the ones between
    /// the "between" line above them.
    /// </summary>
    private (double Top, double Bottom) BorderSpace(int block, EffectiveParagraphFormat format)
    {
        if (format.Borders is not { } borders || Document.Blocks[block] is not Paragraph paragraph)
        {
            return (0, 0);
        }

        bool SharesBox(int other)
        {
            if (other < 0 || other >= Document.Blocks.Count || Document.Blocks[other] is not Paragraph neighbour
                || !ReferenceEquals(neighbour.Cell?.Table, paragraph.Cell?.Table) || neighbour.Cell?.Row != paragraph.Cell?.Row || neighbour.Cell?.Cell != paragraph.Cell?.Cell)
            {
                return false;
            }

            var resolved = _context.Resolver.ResolveParagraph(neighbour);
            return resolved.Borders == borders && Math.Abs(resolved.IndentLeft - format.IndentLeft) < Epsilon && Math.Abs(resolved.IndentRight - format.IndentRight) < Epsilon;
        }

        static double Room(BorderLine? line) => line is { IsVisible: true } ? line.Space + line.Width : 0;
        return (SharesBox(block - 1) ? Room(borders.InsideHorizontal) : Room(borders.Top), SharesBox(block + 1) ? 0 : Room(borders.Bottom));
    }

    // ---------------------------------------------------------------- lookups

    private double[] BuildPageTops()
    {
        var tops = new double[_pages.Count];
        var y = 0.0;
        for (var index = 0; index < _pages.Count; index++)
        {
            tops[index] = y;
            y += _pages[index].Height + Options.PageGap;
        }

        return tops;
    }

    private (int Page, PlacedLine Line) Locate(CaretPlace place)
    {
        Update();
        var position = CaretStops.Snap(Document, place.Position);
        var placement = _placements[position.Block]!;
        var lines = placement.Lines;
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Line;
            var isLastLine = index == lines.Length - 1;
            if (position.Offset < line.End || isLastLine)
            {
                if (position.Offset < line.Start)
                {
                    continue;
                }

                return (placement.Pages[index], lines[index]);
            }

            if (position.Offset == line.End && line.Ending == LineEnding.Wrap && place.Affinity == CaretAffinity.Upstream)
            {
                return (placement.Pages[index], lines[index]);
            }
        }

        return (placement.Pages[^1], lines[^1]);
    }

    /// <summary>
    /// The line nearest to a point: inside a table cell the nearest line of that cell; else the line whose height holds y and
    /// whose width holds x (beside a table: the nearest of them), else the nearest line above or below.
    /// </summary>
    private PlacedLine? NearestLine(PageLayout page, double x, double y)
    {
        var cell = page.Cells.FirstOrDefault(candidate => !candidate.IsRepeat && candidate.Contains(x, y));
        IEnumerable<PlacedLine> lines = page.Lines;
        if (cell is not null && TableSpan.At(Document, cell.TableStart) is { } span)
        {
            var (first, last) = span.BlocksOf(cell.Row, cell.Cell);
            lines = page.Lines.Where(line => line.Block >= first && line.Block <= last);
        }

        PlacedLine? best = null;
        var bestDistance = double.MaxValue;
        foreach (var line in lines)
        {
            var dy = y < line.Top ? line.Top - y : y >= line.Top + line.Line.Height ? y - (line.Top + line.Line.Height) : 0;
            var dx = x < line.Left ? line.Left - x : x > line.Left + line.Paragraph.Width ? x - (line.Left + line.Paragraph.Width) : 0;
            var distance = (dy * 1000) + dx; // the line's height first, the side second
            if (distance < bestDistance)
            {
                best = line;
                bestDistance = distance;
            }
        }

        return best;
    }

    private HitResult HitLine(int page, PlacedLine placed, double x)
    {
        if (placed.Paragraph.IsPreserved)
        {
            return new HitResult(new CaretPlace(CaretStops.Snap(Document, new DocumentPosition(placed.Block, 0))), page, placed.Block);
        }

        var line = placed.Line;
        var offset = line.OffsetAt(x - placed.Left);
        var affinity = offset == line.End && line.Ending == LineEnding.Wrap ? CaretAffinity.Upstream : CaretAffinity.Downstream;
        return new HitResult(new CaretPlace(new DocumentPosition(placed.Block, offset), affinity), page);
    }

    private int NearestBlockOn(int page)
    {
        for (var index = page; index >= 0; index--)
        {
            if (_pages[index].Lines.Count > 0)
            {
                return _pages[index].Lines[^1].Block;
            }
        }

        return 0;
    }
}
