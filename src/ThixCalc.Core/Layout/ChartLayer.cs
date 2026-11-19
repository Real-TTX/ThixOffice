using ThixCalc.Core.Charts;
using ThixCalc.Core.Model;
using ThixOffice.Charts;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Layout;

/// <summary>
/// The charts of a sheet on the grid: their rectangles from the anchors (pixels at the viewport's zoom, frozen panes
/// respected), hit testing (the front-most first), anchors back from rectangles (moving, resizing), and drawing them – the
/// shared <see cref="ChartRenderer"/> with the workbook's data, number formats, theme accents and font – on top of the cells.
/// </summary>
public static class ChartLayer
{
    /// <summary>Screen pixels per point at 100 %.</summary>
    public const double PixelsPerPoint = 96.0 / 72;

    /// <summary>A chart's rectangle in the viewport (pixels; also when off screen).</summary>
    public static PixelRect ViewRect(GridViewport viewport, ChartAnchor anchor)
    {
        var layout = viewport.Layout;
        var zoom = viewport.Zoom;
        var left = viewport.ColumnX(anchor.From.Column) + Offset(anchor.FromX, layout.ColumnWidth(anchor.From.Column, zoom), zoom);
        var top = viewport.RowY(anchor.From.Row) + Offset(anchor.FromY, layout.RowHeight(anchor.From.Row, zoom), zoom);
        var right = viewport.ColumnX(anchor.To.Column) + Offset(anchor.ToX, layout.ColumnWidth(anchor.To.Column, zoom), zoom);
        var bottom = viewport.RowY(anchor.To.Row) + Offset(anchor.ToY, layout.RowHeight(anchor.To.Row, zoom), zoom);
        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    /// <summary>A chart's rectangle on the whole sheet (pixels at <paramref name="zoom"/> from A1's corner).</summary>
    public static PixelRect SheetRect(SheetLayout layout, ChartAnchor anchor, double zoom)
    {
        var left = layout.ColumnLeft(anchor.From.Column, zoom) + Offset(anchor.FromX, layout.ColumnWidth(anchor.From.Column, zoom), zoom);
        var top = layout.RowTop(anchor.From.Row, zoom) + Offset(anchor.FromY, layout.RowHeight(anchor.From.Row, zoom), zoom);
        var right = layout.ColumnLeft(anchor.To.Column, zoom) + Offset(anchor.ToX, layout.ColumnWidth(anchor.To.Column, zoom), zoom);
        var bottom = layout.RowTop(anchor.To.Row, zoom) + Offset(anchor.ToY, layout.RowHeight(anchor.To.Row, zoom), zoom);
        return new PixelRect(left, top, Math.Max(1, right - left), Math.Max(1, bottom - top));
    }

    /// <summary>The anchor of a rectangle on the sheet (pixels at <paramref name="zoom"/>), corners clamped to the sheet.</summary>
    public static ChartAnchor AnchorOf(SheetLayout layout, PixelRect rect, double zoom)
    {
        var x = Math.Max(0, rect.X);
        var y = Math.Max(0, rect.Y);
        var (fromColumn, fromX) = ColumnPoint(layout, x, zoom);
        var (fromRow, fromY) = RowPoint(layout, y, zoom);
        var (toColumn, toX) = ColumnPoint(layout, x + Math.Max(4, rect.Width), zoom);
        var (toRow, toY) = RowPoint(layout, y + Math.Max(4, rect.Height), zoom);
        return new ChartAnchor(new CellAddress(fromRow, fromColumn), fromX, fromY, new CellAddress(toRow, toColumn), toX, toY);
    }

    /// <summary>An anchor of <paramref name="widthPoints"/> × <paramref name="heightPoints"/> with its top-left corner at a cell.</summary>
    public static ChartAnchor AnchorAt(SheetLayout layout, CellAddress cell, double widthPoints, double heightPoints)
    {
        const double zoom = 1;
        var x = layout.ColumnLeft(cell.Column, zoom);
        var y = layout.RowTop(cell.Row, zoom);
        return AnchorOf(layout, new PixelRect(x, y, widthPoints * PixelsPerPoint, heightPoints * PixelsPerPoint), zoom);
    }

    private static (int Column, double Offset) ColumnPoint(SheetLayout layout, double x, double zoom)
    {
        var column = Math.Clamp(layout.ColumnAt(x, zoom), 0, SheetLimits.LastColumn);
        return (column, Math.Max(0, (x - layout.ColumnLeft(column, zoom)) / (PixelsPerPoint * zoom)));
    }

    private static (int Row, double Offset) RowPoint(SheetLayout layout, double y, double zoom)
    {
        var row = Math.Clamp(layout.RowAt(y, zoom), 0, SheetLimits.LastRow);
        return (row, Math.Max(0, (y - layout.RowTop(row, zoom)) / (PixelsPerPoint * zoom)));
    }

    private static double Offset(double points, double cellPixels, double zoom) => Math.Min(points * PixelsPerPoint * zoom, cellPixels);

    /// <summary>The front-most chart at a viewport point, or null.</summary>
    public static SheetChart? HitTest(GridViewport viewport, double x, double y)
    {
        if (x < viewport.HeaderWidth || y < viewport.HeaderHeight)
        {
            return null;
        }

        var charts = viewport.Layout.Sheet.Charts;
        for (var index = charts.Count - 1; index >= 0; index--)
        {
            if (ViewRect(viewport, charts[index].Anchor).Contains(x, y))
            {
                return charts[index];
            }
        }

        return null;
    }

    /// <summary>Draws the sheet's charts that reach into the viewport, clipped to the cells (not the headers).</summary>
    public static void Render(List<DisplayItem> items, GridViewport viewport, GridMetrics metrics, double pixelSize)
    {
        var sheet = viewport.Layout.Sheet;
        if (sheet.Charts.Count == 0)
        {
            return;
        }

        var clip = new PixelRect(viewport.HeaderWidth, viewport.HeaderHeight, viewport.Width - viewport.HeaderWidth, viewport.Height - viewport.HeaderHeight);
        items.Add(new ClipBeginItem(clip.X, clip.Y, clip.Width, clip.Height));
        var source = new WorkbookChartSource(sheet.Workbook, metrics.Locale);
        foreach (var chart in sheet.Charts)
        {
            var rect = ViewRect(viewport, chart.Anchor);
            if (rect.Right < clip.X || rect.Bottom < clip.Y || rect.X > clip.Right || rect.Y > clip.Bottom)
            {
                continue;
            }

            items.AddRange(RenderChart(chart.Chart, sheet.Workbook, metrics, source, rect, PixelsPerPoint * viewport.Zoom, pixelSize));
        }

        items.Add(ClipEndItem.Instance);
    }

    /// <summary>One chart's display items in a rectangle (target units; <paramref name="scale"/> target units per point).</summary>
    public static IReadOnlyList<DisplayItem> RenderChart(Chart chart, Workbook workbook, GridMetrics metrics, IChartDataSource source, PixelRect rect, double scale, double pixelSize)
    {
        var data = ChartDataResolver.Resolve(chart, source);
        return ChartRenderer.Render(data, Options(workbook, metrics, rect, scale, pixelSize));
    }

    /// <summary>The render options of the workbook: its theme's accents, its default font, ThixCalc's number formats.</summary>
    public static ChartRenderOptions Options(Workbook workbook, GridMetrics metrics, PixelRect rect, double scale, double pixelSize) => new()
    {
        Measurer = metrics.Measurer,
        Formatter = new CalcChartFormatter(metrics.Locale, workbook.Date1904),
        Accents = [workbook.Theme[4], workbook.Theme[5], workbook.Theme[6], workbook.Theme[7], workbook.Theme[8], workbook.Theme[9]],
        FontFamily = workbook.Styles[0].Font.Name,
        X = rect.X,
        Y = rect.Y,
        Width = rect.Width,
        Height = rect.Height,
        Scale = scale,
        PixelSize = pixelSize,
    };
}
