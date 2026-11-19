using ThixOffice.Text;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>Options of the layout.</summary>
public sealed record LayoutOptions
{
    public static readonly LayoutOptions Default = new();

    /// <summary>The gap between pages in the stacked "document" coordinates (the editor's continuous view).</summary>
    public double PageGap { get; init; } = 12;
}

/// <summary>Something laid out on a line, x relative to the start of the line's text area (the page's left margin).</summary>
public abstract record LayoutItem(int Start, int End, double X, double Width);

/// <summary>
/// Text in one format: characters [Start, End) of the paragraph, taken from <see cref="Shaped"/> starting at
/// <see cref="ShapedStart"/>; every space (U+0020) is widened by <see cref="SpaceExtra"/> (justification).
/// </summary>
public sealed record TextItem(
    int Start,
    int End,
    double X,
    double Width,
    ShapedText Shaped,
    int ShapedStart,
    EffectiveCharacterFormat Format,
    FontSpec Font,
    FontMetrics Metrics,
    double BaselineShift,
    double SpaceExtra) : LayoutItem(Start, End, X, Width);

/// <summary>The space a tab character takes (up to its stop).</summary>
public sealed record TabItem(int Start, double X, double Width, TabLeader Leader, EffectiveCharacterFormat Format, FontSpec Font)
    : LayoutItem(Start, Start + 1, X, Width);

/// <summary>An inline object: a picture (<see cref="ImageInline"/>), a placeholder box (preserved drawings) or its non-editable text (field results, tracked insertions).</summary>
public sealed record ObjectItem(
    int Start,
    double X,
    double Width,
    double Height,
    Inline Inline,
    ShapedText? Shaped,
    EffectiveCharacterFormat Format,
    FontSpec Font,
    FontMetrics Metrics,
    double BaselineShift = 0) : LayoutItem(Start, Start + 1, X, Width);

/// <summary>The label of a list item ("1.", "•").</summary>
public sealed record LabelItem(double X, double Width, ShapedText Shaped, EffectiveCharacterFormat Format, FontSpec Font, FontMetrics Metrics)
    : LayoutItem(0, 0, X, Width);

/// <summary>
/// One line of a paragraph: characters [<see cref="Start"/>, <see cref="End"/>) (with trailing spaces and the break character),
/// its height (line spacing included) and baseline, its items and the caret stops (offsets with their x).
/// </summary>
public sealed class LineLayout
{
    internal LineLayout(
        int start,
        int end,
        double height,
        double baseline,
        double ascent,
        double descent,
        IReadOnlyList<LayoutItem> items,
        int[] stops,
        double[] stopX,
        LineEnding ending,
        bool isFirst,
        bool isLast,
        double markWidth)
    {
        Start = start;
        End = end;
        Height = height;
        Baseline = baseline;
        Ascent = ascent;
        Descent = descent;
        Items = items;
        Stops = stops;
        StopX = stopX;
        Ending = ending;
        IsFirst = isFirst;
        IsLast = isLast;
        MarkWidth = markWidth;
    }

    public int Start { get; }

    public int End { get; }

    /// <summary>The height of the line box, line spacing included.</summary>
    public double Height { get; }

    /// <summary>The baseline, from the top of the line box.</summary>
    public double Baseline { get; }

    /// <summary>The ascent of the tallest font on the line (the caret's height is Ascent + Descent).</summary>
    public double Ascent { get; }

    public double Descent { get; }

    public IReadOnlyList<LayoutItem> Items { get; }

    /// <summary>The caret positions on this line (paragraph offsets), ascending.</summary>
    public int[] Stops { get; }

    /// <summary>The x of every stop, relative to the text area's left edge.</summary>
    public double[] StopX { get; }

    public LineEnding Ending { get; }

    public bool IsFirst { get; }

    public bool IsLast { get; }

    /// <summary>The width a selection shows for the paragraph mark / break at the end of the line.</summary>
    public double MarkWidth { get; }

    /// <summary>The x of the caret at <paramref name="offset"/> (clamped to the line).</summary>
    public double XOf(int offset)
    {
        var index = Array.BinarySearch(Stops, offset);
        if (index >= 0)
        {
            return StopX[index];
        }

        index = ~index;
        if (index == 0)
        {
            return StopX[0];
        }

        return StopX[Math.Min(index - 1, StopX.Length - 1)];
    }

    /// <summary>The stop nearest to <paramref name="x"/>.</summary>
    public int OffsetAt(double x)
    {
        var best = 0;
        var bestDistance = double.MaxValue;
        for (var index = 0; index < Stops.Length; index++)
        {
            var distance = Math.Abs(StopX[index] - x);
            if (distance < bestDistance)
            {
                best = index;
                bestDistance = distance;
            }
        }

        return Stops[best];
    }

    public override string ToString() => $"Line [{Start}, {End}) h={Height:0.##} {Ending}";
}

/// <summary>How a line ends.</summary>
public enum LineEnding
{
    /// <summary>A soft wrap: the next line continues the paragraph.</summary>
    Wrap,
    /// <summary>A manual line break (Shift+Enter).</summary>
    LineBreak,
    /// <summary>A page break: the next line starts a new page.</summary>
    PageBreak,
    /// <summary>The end of the paragraph.</summary>
    Paragraph,
}

/// <summary>
/// A laid-out block, independent of the page: its lines, spacing and keep rules. Preserved blocks get lines too (their text,
/// framed). Reused as long as the block instance, the width and the list label are the same.
/// </summary>
public sealed class ParagraphLayout
{
    internal ParagraphLayout(Block block, double width, string? label, EffectiveParagraphFormat format, IReadOnlyList<LineLayout> lines)
    {
        Block = block;
        Width = width;
        Label = label;
        Format = format;
        Lines = lines;
        Height = lines.Sum(line => line.Height);
    }

    public Block Block { get; }

    public double Width { get; }

    public string? Label { get; }

    public EffectiveParagraphFormat Format { get; }

    public IReadOnlyList<LineLayout> Lines { get; }

    /// <summary>The height of all lines (without spacing before / after).</summary>
    public double Height { get; }

    public bool IsPreserved => Block is OpaqueBlock;

    /// <summary>True when the lines keep clear of floating objects on the page (laid out for that place, not reused elsewhere).</summary>
    internal bool IsWrapped { get; init; }

    /// <summary>The numbers of the paragraph's first footnote and endnote it was laid out with ((0, 0) without notes).</summary>
    internal (int Footnote, int Endnote) NoteStart { get; set; }
}

/// <summary>A line placed on a page: its block, its index in the block's layout, the top (page y) and the left of the text area (page x).</summary>
public sealed record PlacedLine(int Block, int LineIndex, double Top, double Left, LineLayout Line, ParagraphLayout Paragraph);

/// <summary>
/// A cell (or the part of a cell) placed on a page: its table (first block), row and cell, the rectangle (page points) and what
/// is drawn of it – the four border lines as they apply here and the fill. <see cref="IsRepeat"/>: a heading row repeated at the
/// top of a page (drawn, not editable there).
/// </summary>
public sealed record PlacedCell(
    int TableStart,
    int Row,
    int Cell,
    double X,
    double Y,
    double Width,
    double Height,
    BorderLine? Top,
    BorderLine? Left,
    BorderLine? Bottom,
    BorderLine? Right,
    string? Fill,
    bool IsRepeat)
{
    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>
/// A floating object placed on a page (a picture, shape, text box, group, chart with a wp:anchor): the block and the inline that
/// anchors it, its rectangle (page points), the anchor (wrap, behind the text, z order). Pictures are drawn from <see cref="Inline"/>,
/// the rest from its <see cref="OpaqueInline.Graphic"/>. <see cref="IsHeaderFooter"/>: anchored in the page's header or footer.
/// </summary>
public sealed record PlacedFloat(int Block, int Offset, Inline Inline, DrawingAnchor Anchor, double X, double Y, double Width, double Height, bool IsHeaderFooter = false)
{
    /// <summary>The area text keeps clear of (the rectangle with the distances to the text), or null when the text ignores it.</summary>
    public (double Left, double Top, double Right, double Bottom)? Exclusion => Anchor.Wrap switch
    {
        DrawingWrap.None => null,
        DrawingWrap.TopAndBottom => (-1e6, Y - Anchor.DistanceTop, 1e6, Y + Height + Anchor.DistanceBottom), // no text beside it
        _ => (X - Anchor.DistanceLeft, Y - Anchor.DistanceTop, X + Width + Anchor.DistanceRight, Y + Height + Anchor.DistanceBottom),
    };

    public bool Contains(double x, double y) => x >= X && x < X + Width && y >= Y && y < Y + Height;
}

/// <summary>One page: size, margins (from its section), the lines on it in order, and the table cells on it.</summary>
public sealed class PageLayout
{
    private readonly List<PlacedLine> _lines = [];
    private readonly List<PlacedCell> _cells = [];
    private readonly List<PlacedLine> _repeatedLines = [];
    private readonly List<PlacedFloat> _floats = [];

    internal PageLayout(int index, SectionInfo section)
    {
        Index = index;
        Section = section;
    }

    public int Index { get; }

    public SectionInfo Section { get; }

    public double Width => Section.Properties.PageWidth;

    public double Height => Section.Properties.PageHeight;

    public IReadOnlyList<PlacedLine> Lines => _lines;

    /// <summary>The first block with a line on this page (-1 for an empty page).</summary>
    public int FirstBlock => _lines.Count == 0 ? -1 : _lines[0].Block;

    public int LastBlock => _lines.Count == 0 ? -1 : _lines[^1].Block;

    internal List<PlacedLine> MutableLines => _lines;

    /// <summary>The table cells on this page (their rectangles, borders and fills), in the order they were placed.</summary>
    public IReadOnlyList<PlacedCell> Cells => _cells;

    /// <summary>The lines of heading rows repeated at the top of this page: drawn like <see cref="Lines"/>, never a caret place.</summary>
    public IReadOnlyList<PlacedLine> RepeatedLines => _repeatedLines;

    internal List<PlacedCell> MutableCells => _cells;

    /// <summary>The floating objects of the body on this page, in the order their anchors were placed.</summary>
    public IReadOnlyList<PlacedFloat> Floats => _floats;

    internal List<PlacedFloat> MutableFloats => _floats;

    /// <summary>The floating objects of the page's header and footer.</summary>
    public IReadOnlyList<PlacedFloat> HeaderFooterFloats { get; internal set; } = [];

    /// <summary>The header this page shows (its lines in <see cref="HeaderLines"/>, block numbers of the story), or null.</summary>
    public Story? Header { get; internal set; }

    public HeaderFooterKind HeaderKind { get; internal set; }

    public Story? Footer { get; internal set; }

    public HeaderFooterKind FooterKind { get; internal set; }

    /// <summary>The lines of the header, placed (the block numbers are the story's).</summary>
    public IReadOnlyList<PlacedLine> HeaderLines { get; internal set; } = [];

    public IReadOnlyList<PlacedLine> FooterLines { get; internal set; } = [];

    /// <summary>The number of the page as its fields show it (1-based).</summary>
    public int Number => Index + 1;

    /// <summary>The index of this page within its section (0 = the section's first page).</summary>
    public int IndexInSection { get; internal set; }

    internal List<PlacedLine> MutableRepeatedLines => _repeatedLines;

    private readonly List<DocumentLayout.NoteSlice> _footnoteSlices = [];
    private readonly List<PlacedNote> _endnotes = [];

    internal List<DocumentLayout.NoteSlice> MutableFootnoteSlices => _footnoteSlices;

    internal List<PlacedNote> MutableEndnotes => _endnotes;

    /// <summary>The footnotes (or their parts) at the foot of this page, top to bottom (lines: block numbers of each note's story).</summary>
    public IReadOnlyList<PlacedNote> Footnotes { get; internal set; } = [];

    /// <summary>The separator above the footnotes (a short line; across the text width above a continued note), or null.</summary>
    public NoteSeparator? FootnoteSeparator { get; internal set; }

    /// <summary>The endnotes (or parts) on this page, after the end of the text.</summary>
    public IReadOnlyList<PlacedNote> Endnotes => _endnotes;

    /// <summary>The separator above the endnotes when they start on this page, or null.</summary>
    public NoteSeparator? EndnoteSeparator { get; internal set; }

    /// <summary>The height the footnotes take at the foot of the page (separator included; 0 without footnotes).</summary>
    public double FootnoteHeight { get; private set; }

    internal void AddFootnote(DocumentLayout.NoteSlice slice)
    {
        _footnoteSlices.Add(slice);
        UpdateFootnoteHeight();
    }

    internal void RemoveFootnotes(Predicate<DocumentLayout.NoteSlice> match)
    {
        if (_footnoteSlices.RemoveAll(match) > 0)
        {
            UpdateFootnoteHeight();
        }
    }

    private void UpdateFootnoteHeight() =>
        FootnoteHeight = _footnoteSlices.Count == 0 ? 0 : DocumentLayout.NoteSeparatorHeight + _footnoteSlices.Sum(slice => slice.Height);

    /// <summary>Removes what blocks from <paramref name="block"/> on placed here (re-pagination from that block).</summary>
    internal void RemoveFrom(int block)
    {
        _lines.RemoveAll(line => line.Block >= block);
        _cells.RemoveAll(cell => cell.TableStart >= block);
        _floats.RemoveAll(placed => placed.Block >= block);
        _repeatedLines.RemoveAll(line => line.Block >= block);
        RemoveFootnotes(slice => slice.RefBlock >= block);
        _endnotes.Clear();
        EndnoteSeparator = null;
    }

    public override string ToString() => $"Page {Index + 1}: {_lines.Count} lines, blocks {FirstBlock}–{LastBlock}";
}

/// <summary>Where the caret is drawn: page, x, top and height (points, page coordinates).</summary>
public readonly record struct CaretGeometry(int Page, double X, double Top, double Height);

/// <summary>A rectangle on a page (selection highlight, find results).</summary>
public readonly record struct PageRect(int Page, double X, double Y, double Width, double Height);

/// <summary>The result of a hit test: the caret place, and the preserved block when the point is on one.</summary>
public readonly record struct HitResult(CaretPlace Place, int Page, int? PreservedBlock = null)
{
    public DocumentPosition Position => Place.Position;
}

/// <summary>What the last <see cref="DocumentLayout.Update"/> did (for diagnostics and the performance test).</summary>
public readonly record struct LayoutUpdateInfo(int ParagraphsLaidOut, int FirstPage, int PageCount, TimeSpan Duration);

/// <summary>Raised after the layout changed: pages from <see cref="FirstChangedPage"/> on may look different.</summary>
public sealed class LayoutChangedEventArgs(int firstChangedPage, int pageCount) : EventArgs
{
    public int FirstChangedPage { get; } = firstChangedPage;

    public int PageCount { get; } = pageCount;
}
