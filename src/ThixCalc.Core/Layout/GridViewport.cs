using ThixCalc.Core.Model;

namespace ThixCalc.Core.Layout;

/// <summary>A visible row or column: its index, start and size in pixels (relative to the grid's top-left corner, headers included).</summary>
public readonly record struct AxisSegment(int Index, double Start, double Size)
{
    public double End => Start + Size;
}

/// <summary>A rectangle in pixels.</summary>
public readonly record struct PixelRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public bool Contains(double x, double y) => x >= X && x < Right && y >= Y && y < Bottom;
}

/// <summary>What a point of the grid is.</summary>
public enum HitKind : byte
{
    None,
    Cell,
    RowHeader,
    ColumnHeader,
    Corner,

    /// <summary>The border right of a column header (drag to resize, double-click for optimal width).</summary>
    ColumnResize,

    /// <summary>The border below a row header.</summary>
    RowResize,

    /// <summary>The small square at the bottom right of the selection (AutoFill).</summary>
    FillHandle,
}

/// <summary>The result of <see cref="GridViewport.HitTest"/>: the kind, the cell (or row/column index) hit.</summary>
public readonly record struct HitResult(HitKind Kind, CellAddress Cell = default, int Index = -1);

/// <summary>
/// The geometry of a sheet in pixels at a zoom, pixel-snapped: rows from the cumulative heights (points × 4/3 × zoom,
/// rounded at every boundary, so grid lines never blur), columns from Excel's width-to-pixel rule. O(log n) for any index;
/// column prefix sums are cached per zoom.
/// </summary>
public sealed class SheetLayout
{
    private readonly Worksheet _sheet;
    private readonly GridMetrics _metrics;
    private double[]? _columnStarts;
    private double _cachedZoom = double.NaN;

    public SheetLayout(Worksheet sheet, GridMetrics metrics)
    {
        _sheet = sheet;
        _metrics = metrics;
        sheet.Columns.Changed += (_, _) => _columnStarts = null;
    }

    public Worksheet Sheet => _sheet;

    public GridMetrics Metrics => _metrics;

    public double RowTop(int row, double zoom) => Math.Round(GridMetrics.RowPixels(_sheet.Rows.OffsetOf(row)) * zoom);

    public double RowHeight(int row, double zoom) => RowTop(row + 1, zoom) - RowTop(row, zoom);

    public double ColumnLeft(int column, double zoom)
    {
        var starts = ColumnStarts(zoom);
        return starts[Math.Clamp(column, 0, SheetLimits.MaxColumns)];
    }

    public double ColumnWidth(int column, double zoom) => ColumnLeft(column + 1, zoom) - ColumnLeft(column, zoom);

    /// <summary>The row at a vertical offset (pixels from row 0).</summary>
    public int RowAt(double y, double zoom)
    {
        var low = 0;
        var high = SheetLimits.LastRow;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (RowTop(middle, zoom) <= y)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return _sheet.Rows.FirstVisibleFrom(low);
    }

    public int ColumnAt(double x, double zoom)
    {
        var starts = ColumnStarts(zoom);
        var index = Array.BinarySearch(starts, x);
        var column = index >= 0 ? index : ~index - 1;
        return _sheet.Columns.FirstVisibleFrom(Math.Clamp(column, 0, SheetLimits.LastColumn));
    }

    public double TotalHeight(double zoom) => RowTop(SheetLimits.MaxRows, zoom);

    public double TotalWidth(double zoom) => ColumnLeft(SheetLimits.MaxColumns, zoom);

    private double[] ColumnStarts(double zoom)
    {
        if (_columnStarts is not null && _cachedZoom == zoom)
        {
            return _columnStarts;
        }

        var starts = new double[SheetLimits.MaxColumns + 1];
        var columns = _sheet.Columns;
        var defaultWidth = _metrics.ColumnPixels(columns.DefaultSize);
        var exact = 0.0;
        var entries = columns.Entries.GetEnumerator();
        var hasEntry = entries.MoveNext();
        for (var column = 0; column < SheetLimits.MaxColumns; column++)
        {
            starts[column] = Math.Round(exact);
            double width;
            if (hasEntry && entries.Current.Index == column)
            {
                var info = entries.Current.Info;
                width = info.Hidden ? 0 : _metrics.ColumnPixels(info.Size ?? columns.DefaultSize);
                hasEntry = entries.MoveNext();
            }
            else
            {
                width = defaultWidth;
            }

            exact += width * zoom;
        }

        starts[SheetLimits.MaxColumns] = Math.Round(exact);
        _columnStarts = starts;
        _cachedZoom = zoom;
        return starts;
    }

    /// <summary>
    /// The viewport for a grid of <paramref name="width"/> × <paramref name="height"/> pixels: the frozen rows/columns, then
    /// the scrolling ones from the view's top-left cell, with the header sizes. <paramref name="zoom"/> defaults to the view's.
    /// </summary>
    public GridViewport Viewport(double width, double height, double? zoom = null)
    {
        var view = _sheet.View;
        var scale = zoom ?? view.Zoom / 100.0;
        var firstRow = Math.Max(view.TopLeft.Row, view.FrozenRows);
        var firstColumn = Math.Max(view.TopLeft.Column, view.FrozenColumns);
        return Viewport(width, height, scale, ColumnLeft(firstColumn, scale), RowTop(firstRow, scale));
    }

    /// <summary>The smallest scroll position (pixels at <paramref name="zoom"/>): the first row and column after the frozen ones.</summary>
    public (double X, double Y) MinimumScroll(double zoom) => (ColumnLeft(_sheet.View.FrozenColumns, zoom), RowTop(_sheet.View.FrozenRows, zoom));

    /// <summary>
    /// The viewport scrolled by pixels (smooth scrolling): <paramref name="scrollX"/> / <paramref name="scrollY"/> are the
    /// sheet positions (<see cref="ColumnLeft"/> / <see cref="RowTop"/> at <paramref name="zoom"/>) shown at the left / top
    /// edge of the scrolling pane; the first row and column may be cut off there (the renderer clips the panes).
    /// </summary>
    public GridViewport Viewport(double width, double height, double zoom, double scrollX, double scrollY)
    {
        var view = _sheet.View;
        var scale = zoom;
        var (minimumX, minimumY) = MinimumScroll(scale);
        scrollX = Math.Max(minimumX, Math.Round(scrollX));
        scrollY = Math.Max(minimumY, Math.Round(scrollY));
        var headerHeight = view.ShowHeaders ? Math.Round(GridMetrics.RowPixels(_sheet.Rows.DefaultSize) * scale) : 0;
        var lastRowGuess = Math.Min(SheetLimits.MaxRows, RowAt(scrollY, scale) + (int)(height / Math.Max(1, RowHeight(0, scale))) + 1);
        var digits = Math.Max(3, lastRowGuess.ToString(System.Globalization.CultureInfo.InvariantCulture).Length);
        var headerWidth = view.ShowHeaders ? Math.Round((digits * _metrics.MaxDigitWidthPixels + 10) * scale) : 0;

        var frozenRows = new List<AxisSegment>();
        var y = headerHeight;
        for (var row = 0; row < view.FrozenRows && y < height; row++)
        {
            var size = RowHeight(row, scale);
            if (size > 0)
            {
                frozenRows.Add(new AxisSegment(row, y, size));
            }

            y += size;
        }

        var scrollTop = y;
        var rows = new List<AxisSegment>();
        var rowOrigin = scrollY - y;
        for (var row = Math.Max(view.FrozenRows, RowAt(scrollY, scale)); row < SheetLimits.MaxRows && y < height; row++)
        {
            var size = RowHeight(row, scale);
            if (size > 0)
            {
                rows.Add(new AxisSegment(row, RowTop(row, scale) - rowOrigin, size));
                y = RowTop(row, scale) - rowOrigin + size;
            }
        }

        var frozenColumns = new List<AxisSegment>();
        var x = headerWidth;
        for (var column = 0; column < view.FrozenColumns && x < width; column++)
        {
            var size = ColumnWidth(column, scale);
            if (size > 0)
            {
                frozenColumns.Add(new AxisSegment(column, x, size));
            }

            x += size;
        }

        var scrollLeft = x;
        var columns = new List<AxisSegment>();
        var columnOrigin = scrollX - x;
        for (var column = Math.Max(view.FrozenColumns, ColumnAt(scrollX, scale)); column < SheetLimits.MaxColumns && x < width; column++)
        {
            var size = ColumnWidth(column, scale);
            if (size > 0)
            {
                columns.Add(new AxisSegment(column, ColumnLeft(column, scale) - columnOrigin, size));
                x = ColumnLeft(column, scale) - columnOrigin + size;
            }
        }

        return new GridViewport(this, width, height, scale, headerWidth, headerHeight, frozenRows, rows, frozenColumns, columns, rowOrigin, columnOrigin)
        {
            ScrollTop = scrollTop,
            ScrollLeft = scrollLeft,
            ScrollX = scrollX,
            ScrollY = scrollY,
        };
    }

    /// <summary>
    /// The pixel scroll position (at <paramref name="zoom"/>) that shows <paramref name="cell"/> whole in a grid of
    /// <paramref name="viewport"/>'s size: unchanged when it is visible, else moved by as little as possible (Excel's way).
    /// </summary>
    public (double X, double Y) ScrollToShow(GridViewport viewport, CellAddress cell)
    {
        var view = _sheet.View;
        var zoom = viewport.Zoom;
        var x = viewport.ScrollX;
        var y = viewport.ScrollY;
        var range = _sheet.Merges.Find(cell.Row, cell.Column) ?? CellRange.Cell(cell);
        if (cell.Row >= view.FrozenRows)
        {
            var top = RowTop(range.FirstRow, zoom);
            var bottom = RowTop(range.LastRow + 1, zoom);
            var paneHeight = Math.Max(1, viewport.Height - viewport.ScrollTop);
            if (top < y)
            {
                y = top;
            }
            else if (bottom > y + paneHeight)
            {
                y = Math.Min(top, bottom - paneHeight);
            }
        }

        if (cell.Column >= view.FrozenColumns)
        {
            var left = ColumnLeft(range.FirstColumn, zoom);
            var right = ColumnLeft(range.LastColumn + 1, zoom);
            var paneWidth = Math.Max(1, viewport.Width - viewport.ScrollLeft);
            if (left < x)
            {
                x = left;
            }
            else if (right > x + paneWidth)
            {
                x = Math.Min(left, right - paneWidth);
            }
        }

        return (x, y);
    }
}

/// <summary>
/// What a grid shows: its size, zoom, header sizes, the visible frozen and scrolling rows and columns in pixels, and the
/// geometry queries of the grid control: cell rectangles (merged areas whole), hit testing, the visible cell range.
/// </summary>
public sealed class GridViewport
{
    private const double ResizeTolerance = 3;

    internal GridViewport(SheetLayout layout, double width, double height, double zoom, double headerWidth, double headerHeight,
        IReadOnlyList<AxisSegment> frozenRows, IReadOnlyList<AxisSegment> rows, IReadOnlyList<AxisSegment> frozenColumns,
        IReadOnlyList<AxisSegment> columns, double rowOrigin, double columnOrigin)
    {
        Layout = layout;
        Width = width;
        Height = height;
        Zoom = zoom;
        HeaderWidth = headerWidth;
        HeaderHeight = headerHeight;
        FrozenRows = frozenRows;
        Rows = rows;
        FrozenColumns = frozenColumns;
        Columns = columns;
        RowOrigin = rowOrigin;
        ColumnOrigin = columnOrigin;
    }

    public SheetLayout Layout { get; }

    public double Width { get; }

    public double Height { get; }

    public double Zoom { get; }

    public double HeaderWidth { get; }

    public double HeaderHeight { get; }

    public IReadOnlyList<AxisSegment> FrozenRows { get; }

    public IReadOnlyList<AxisSegment> Rows { get; }

    public IReadOnlyList<AxisSegment> FrozenColumns { get; }

    public IReadOnlyList<AxisSegment> Columns { get; }

    /// <summary>Pixels to subtract from a scrolling row's absolute top to get its position on screen.</summary>
    internal double RowOrigin { get; }

    internal double ColumnOrigin { get; }

    public IEnumerable<AxisSegment> AllRows => FrozenRows.Concat(Rows);

    public IEnumerable<AxisSegment> AllColumns => FrozenColumns.Concat(Columns);

    /// <summary>The y where the scrolling pane starts (below headers and frozen rows).</summary>
    public double ScrollTop { get; init; }

    /// <summary>The x where the scrolling pane starts (right of the row headers and the frozen columns).</summary>
    public double ScrollLeft { get; init; }

    /// <summary>The sheet position (pixels at <see cref="Zoom"/>) shown at the scrolling pane's left edge.</summary>
    public double ScrollX { get; init; }

    /// <summary>The sheet position shown at the scrolling pane's top edge.</summary>
    public double ScrollY { get; init; }

    /// <summary>The first scrolling row whose top is not cut off (the view's TopLeft row).</summary>
    public int FirstWholeRow => Rows.FirstOrDefault(row => row.Start >= ScrollTop - 0.5, Rows.Count > 0 ? Rows[0] : new AxisSegment(Layout.Sheet.View.FrozenRows, 0, 0)).Index;

    /// <summary>The first scrolling column whose left is not cut off.</summary>
    public int FirstWholeColumn => Columns.FirstOrDefault(column => column.Start >= ScrollLeft - 0.5, Columns.Count > 0 ? Columns[0] : new AxisSegment(Layout.Sheet.View.FrozenColumns, 0, 0)).Index;

    /// <summary>The cells the scrolling pane shows (frozen panes not included).</summary>
    public CellRange VisibleRange => Rows.Count == 0 || Columns.Count == 0
        ? CellRange.Cell(0, 0)
        : new CellRange(Rows[0].Index, Columns[0].Index, Rows[^1].Index, Columns[^1].Index);

    /// <summary>The screen rectangle of a cell (a merged area as a whole), for any cell (also off screen).</summary>
    public PixelRect CellRect(CellAddress cell)
    {
        var range = Layout.Sheet.Merges.Find(cell.Row, cell.Column) ?? CellRange.Cell(cell);
        return RangeRect(range);
    }

    public PixelRect RangeRect(CellRange range)
    {
        var top = RowY(range.FirstRow);
        var bottom = RowY(range.LastRow + 1);
        var left = ColumnX(range.FirstColumn);
        var right = ColumnX(range.LastColumn + 1);
        return new PixelRect(left, top, right - left, bottom - top);
    }

    /// <summary>The screen y of a row's top (frozen rows fixed, scrolling ones shifted).</summary>
    public double RowY(int row)
    {
        var frozen = Layout.Sheet.View.FrozenRows;
        var zoom = Zoom;
        if (row < frozen)
        {
            return HeaderHeight + Layout.RowTop(row, zoom);
        }

        return Layout.RowTop(row, zoom) - RowOrigin;
    }

    public double ColumnX(int column)
    {
        var frozen = Layout.Sheet.View.FrozenColumns;
        if (column < frozen)
        {
            return HeaderWidth + Layout.ColumnLeft(column, Zoom);
        }

        return Layout.ColumnLeft(column, Zoom) - ColumnOrigin;
    }

    /// <summary>What is at (<paramref name="x"/>, <paramref name="y"/>): a cell, a header, a resize border or the fill handle of <paramref name="selection"/>.</summary>
    public HitResult HitTest(double x, double y, CellRange? selection = null)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height)
        {
            return new HitResult(HitKind.None);
        }

        if (selection is { } range)
        {
            var rect = RangeRect(range);
            var handle = Math.Max(3, 3 * Zoom);
            if (Math.Abs(x - rect.Right) <= handle && Math.Abs(y - rect.Bottom) <= handle)
            {
                return new HitResult(HitKind.FillHandle, range.Last);
            }
        }

        if (y < HeaderHeight && x < HeaderWidth)
        {
            return new HitResult(HitKind.Corner);
        }

        if (y < HeaderHeight)
        {
            foreach (var column in AllColumns)
            {
                if (Math.Abs(x - column.End) <= ResizeTolerance)
                {
                    return new HitResult(HitKind.ColumnResize, Index: column.Index);
                }
            }

            return new HitResult(HitKind.ColumnHeader, Index: ColumnHit(x));
        }

        if (x < HeaderWidth)
        {
            foreach (var row in AllRows)
            {
                if (Math.Abs(y - row.End) <= ResizeTolerance)
                {
                    return new HitResult(HitKind.RowResize, Index: row.Index);
                }
            }

            return new HitResult(HitKind.RowHeader, Index: RowHit(y));
        }

        var cell = new CellAddress(RowHit(y), ColumnHit(x));
        var merge = Layout.Sheet.Merges.Find(cell.Row, cell.Column);
        return new HitResult(HitKind.Cell, merge?.First ?? cell);
    }

    private int RowHit(double y)
    {
        foreach (var row in AllRows)
        {
            if (y >= row.Start && y < row.End)
            {
                return row.Index;
            }
        }

        // Past the last visible row: compute from the layout.
        return Layout.RowAt(y + RowOrigin, Zoom);
    }

    private int ColumnHit(double x)
    {
        foreach (var column in AllColumns)
        {
            if (x >= column.Start && x < column.End)
            {
                return column.Index;
            }
        }

        return Layout.ColumnAt(x + ColumnOrigin, Zoom);
    }

    /// <summary>The top-left cell that makes <paramref name="cell"/> fully visible (for the view's TopLeft), or the current one.</summary>
    public CellAddress ScrollTarget(CellAddress cell)
    {
        var view = Layout.Sheet.View;
        var topLeft = new CellAddress(Math.Max(view.TopLeft.Row, view.FrozenRows), Math.Max(view.TopLeft.Column, view.FrozenColumns));
        var row = topLeft.Row;
        var column = topLeft.Column;
        if (cell.Row >= view.FrozenRows)
        {
            if (cell.Row < topLeft.Row)
            {
                row = cell.Row;
            }
            else if (RowY(cell.Row + 1) > Height)
            {
                // Scroll down until the row's bottom fits.
                var target = Layout.RowTop(cell.Row + 1, Zoom) - (Height - ScrollTop);
                row = Math.Max(view.FrozenRows, Layout.RowAt(target, Zoom) + 1);
                row = Math.Min(row, cell.Row);
            }
        }

        if (cell.Column >= view.FrozenColumns)
        {
            if (cell.Column < topLeft.Column)
            {
                column = cell.Column;
            }
            else if (ColumnX(cell.Column + 1) > Width)
            {
                var target = Layout.ColumnLeft(cell.Column + 1, Zoom) - (Width - ScrollLeft);
                column = Math.Max(view.FrozenColumns, Layout.ColumnAt(target, Zoom) + 1);
                column = Math.Min(column, cell.Column);
            }
        }

        return new CellAddress(row, column);
    }
}
