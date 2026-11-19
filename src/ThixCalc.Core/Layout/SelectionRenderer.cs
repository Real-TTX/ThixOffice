using ThixCalc.Core.Model;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Layout;

/// <summary>A range shown in a colour while a formula is edited (its reference token has the same colour).</summary>
public readonly record struct RangeHighlight(CellRange Range, RgbColor Color);

/// <summary>What the selection layer shows over the cells.</summary>
public sealed record SelectionLook
{
    public IReadOnlyList<CellRange> Ranges { get; init; } = [];

    public CellAddress ActiveCell { get; init; }

    /// <summary>Whether the fill handle shows (one range, not while a cell is edited).</summary>
    public bool ShowFillHandle { get; init; } = true;

    /// <summary>Whether the active cell's range gets the thick border (off while a formula is pointed into).</summary>
    public bool ShowBorder { get; init; } = true;

    /// <summary>The ranges of the edited formula's references, each in its colour.</summary>
    public IReadOnlyList<RangeHighlight> Highlights { get; init; } = [];

    /// <summary>The target of an AutoFill drag (a grey frame), or null.</summary>
    public CellRange? FillPreview { get; init; }

    public RgbColor Border { get; init; } = RgbColor.FromHex("107C41");

    public RgbColor Fill { get; init; } = new(0, 0, 0, 22);

    public RgbColor HandleOutline { get; init; } = RgbColor.White;

    public RgbColor Preview { get; init; } = RgbColor.FromHex("6E6E6E");
}

/// <summary>
/// The selection layer of the grid as display items (pixels, the viewport's coordinates): the translucent fill of selected
/// ranges (not over the active cell), the thick border of the active range, the fill handle, the coloured frames of the
/// references of an edited formula and the AutoFill preview – each pane clipped on its own, so frozen panes stay right.
/// Drawn over <see cref="GridRenderer.Render"/>'s list; cheap enough to make again for every selection change.
/// </summary>
public static class SelectionRenderer
{
    public static IReadOnlyList<DisplayItem> Render(GridViewport viewport, SelectionLook look)
    {
        var items = new List<DisplayItem>();
        var sheet = viewport.Layout.Sheet;
        var view = sheet.View;
        var frozenRows = (0, view.FrozenRows - 1);
        var scrollRows = (view.FrozenRows, SheetLimits.LastRow);
        var frozenColumns = (0, view.FrozenColumns - 1);
        var scrollColumns = (view.FrozenColumns, SheetLimits.LastColumn);
        var panes = new[]
        {
            (Rows: scrollRows, Columns: scrollColumns, Clip: new PixelRect(viewport.ScrollLeft, viewport.ScrollTop, viewport.Width - viewport.ScrollLeft, viewport.Height - viewport.ScrollTop)),
            (Rows: frozenRows, Columns: scrollColumns, Clip: new PixelRect(viewport.ScrollLeft, viewport.HeaderHeight, viewport.Width - viewport.ScrollLeft, viewport.ScrollTop - viewport.HeaderHeight)),
            (Rows: scrollRows, Columns: frozenColumns, Clip: new PixelRect(viewport.HeaderWidth, viewport.ScrollTop, viewport.ScrollLeft - viewport.HeaderWidth, viewport.Height - viewport.ScrollTop)),
            (Rows: frozenRows, Columns: frozenColumns, Clip: new PixelRect(viewport.HeaderWidth, viewport.HeaderHeight, viewport.ScrollLeft - viewport.HeaderWidth, viewport.ScrollTop - viewport.HeaderHeight)),
        };

        var active = sheet.Merges.Find(look.ActiveCell.Row, look.ActiveCell.Column) ?? CellRange.Cell(look.ActiveCell);
        var single = look.Ranges.Count <= 1 && (look.Ranges.Count == 0 || look.Ranges[0] == active);
        foreach (var pane in panes)
        {
            if (pane.Clip.Width <= 0 || pane.Clip.Height <= 0 || pane.Rows.Item2 < pane.Rows.Item1 || pane.Columns.Item2 < pane.Columns.Item1)
            {
                continue;
            }

            var paneRange = new CellRange(pane.Rows.Item1, pane.Columns.Item1, pane.Rows.Item2, pane.Columns.Item2);
            items.Add(new ClipBeginItem(pane.Clip.X, pane.Clip.Y, pane.Clip.Width, pane.Clip.Height));

            if (!single)
            {
                foreach (var range in look.Ranges)
                {
                    Fill(items, viewport, range, paneRange, active, look.Fill);
                }
            }

            if (look.ShowBorder)
            {
                var border = look.Ranges.Count == 1 ? look.Ranges[0] : active;
                Frame(items, viewport, border, paneRange, look.Border, look.Ranges.Count > 1 ? 1 : 2);
                if (look.Ranges.Count > 1)
                {
                    Frame(items, viewport, active, paneRange, look.Border, 2);
                }
            }

            foreach (var highlight in look.Highlights)
            {
                Fill(items, viewport, highlight.Range, paneRange, null, highlight.Color with { A = 28 });
                Frame(items, viewport, highlight.Range, paneRange, highlight.Color, 1.5);
                Corners(items, viewport, highlight.Range, paneRange, highlight.Color);
            }

            if (look.FillPreview is { } preview)
            {
                Frame(items, viewport, preview, paneRange, look.Preview, 1, LineStyle.Dashed);
            }

            if (look.ShowFillHandle && look.ShowBorder && look.Ranges.Count <= 1)
            {
                var range = look.Ranges.Count == 1 ? look.Ranges[0] : active;
                if (paneRange.Contains(range.Last))
                {
                    var rect = viewport.RangeRect(range);
                    var size = Math.Max(5, Math.Round(6 * Math.Min(2, viewport.Zoom)));
                    items.Add(new RectangleItem(rect.Right - size / 2 - 1.5, rect.Bottom - size / 2 - 1.5, size + 2, size + 2, look.HandleOutline));
                    items.Add(new RectangleItem(rect.Right - size / 2 - 0.5, rect.Bottom - size / 2 - 0.5, size, size, look.Border));
                }
            }

            items.Add(ClipEndItem.Instance);
        }

        return items;
    }

    /// <summary>The fill of a range inside a pane (the active cell left clear, as in Excel).</summary>
    private static void Fill(List<DisplayItem> items, GridViewport viewport, CellRange range, CellRange pane, CellRange? hole, RgbColor color)
    {
        if (range.Intersect(pane) is not { } part)
        {
            return;
        }

        var rect = Clamp(viewport, viewport.RangeRect(part));
        if (hole is { } active && active.Intersect(part) is { } inner)
        {
            // Four bands around the hole.
            var cut = viewport.RangeRect(inner);
            AddRect(items, rect.X, rect.Y, rect.Width, cut.Y - rect.Y, color);
            AddRect(items, rect.X, cut.Bottom, rect.Width, rect.Bottom - cut.Bottom, color);
            AddRect(items, rect.X, cut.Y, cut.X - rect.X, cut.Height, color);
            AddRect(items, cut.Right, cut.Y, rect.Right - cut.Right, cut.Height, color);
            return;
        }

        AddRect(items, rect.X, rect.Y, rect.Width, rect.Height, color);
    }

    private static void AddRect(List<DisplayItem> items, double x, double y, double width, double height, RgbColor color)
    {
        if (width > 0 && height > 0)
        {
            items.Add(new RectangleItem(x, y, width, height, color));
        }
    }

    /// <summary>The edges of a range that lie in the pane (an edge cut by the pane's boundary is not drawn).</summary>
    private static void Frame(List<DisplayItem> items, GridViewport viewport, CellRange range, CellRange pane, RgbColor color, double thickness, LineStyle style = LineStyle.Solid)
    {
        if (range.Intersect(pane) is not { } part)
        {
            return;
        }

        var rect = Clamp(viewport, viewport.RangeRect(part));
        var inset = thickness / 2 - 0.5;
        var left = rect.X - 0.5 + inset;
        var top = rect.Y - 0.5 + inset;
        var right = rect.Right - 0.5 - inset;
        var bottom = rect.Bottom - 0.5 - inset;
        if (part.FirstRow == range.FirstRow)
        {
            items.Add(new LineItem(left - inset, top, right + inset, top, color, thickness, style));
        }

        if (part.LastRow == range.LastRow)
        {
            items.Add(new LineItem(left - inset, bottom, right + inset, bottom, color, thickness, style));
        }

        if (part.FirstColumn == range.FirstColumn)
        {
            items.Add(new LineItem(left, top, left, bottom, color, thickness, style));
        }

        if (part.LastColumn == range.LastColumn)
        {
            items.Add(new LineItem(right, top, right, bottom, color, thickness, style));
        }
    }

    /// <summary>The small squares at the corners of a reference's frame (Excel's handles).</summary>
    private static void Corners(List<DisplayItem> items, GridViewport viewport, CellRange range, CellRange pane, RgbColor color)
    {
        if (range.Intersect(pane) is not { } part || part != range)
        {
            return;
        }

        var rect = viewport.RangeRect(range);
        const double size = 5;
        foreach (var (x, y) in new[] { (rect.X, rect.Y), (rect.Right, rect.Y), (rect.X, rect.Bottom), (rect.Right, rect.Bottom) })
        {
            items.Add(new RectangleItem(x - size / 2 - 0.5, y - size / 2 - 0.5, size, size, color));
        }
    }

    /// <summary>Huge ranges (whole columns) end a little outside the viewport, so their geometry stays in float range.</summary>
    private static PixelRect Clamp(GridViewport viewport, PixelRect rect)
    {
        var left = Math.Max(rect.X, -10);
        var top = Math.Max(rect.Y, -10);
        var right = Math.Min(rect.Right, viewport.Width + 10);
        var bottom = Math.Min(rect.Bottom, viewport.Height + 10);
        return new PixelRect(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

}
