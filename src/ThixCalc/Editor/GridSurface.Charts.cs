using Avalonia;
using Avalonia.Input;
using SkiaSharp;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using PixelRect = ThixCalc.Core.Layout.PixelRect;

namespace ThixCalc.Editor;

/// <summary>
/// The charts on the grid: a click selects one (a frame with eight handles, the chart tabs show), dragging moves it, the
/// handles size it (Umschalt keeps the proportions at the corners), the arrow keys nudge it, Entf deletes it, Esc goes back to
/// the cells; the right button opens the chart's menu. Moving and sizing show an outline and become one undo step on release.
/// </summary>
internal sealed partial class GridSurface
{
    /// <summary>The size of a handle in device-independent pixels.</summary>
    private const double ChartHandleSize = 8;

    private ChartDrag? _chartDrag;

    /// <summary>A chart being moved (<see cref="Handle"/> null) or sized by a handle (0–7 clockwise from the top left).</summary>
    private sealed record ChartDrag(int ChartId, int? Handle, Point Start, PixelRect Original, bool Proportional)
    {
        public PixelRect Current { get; set; } = Original;
    }

    /// <summary>The selected chart's frame on screen (device pixels), or null.</summary>
    private PixelRect? SelectedChartRect(GridViewport viewport) =>
        Session.SelectedChart is { } chart ? ChartLayer.ViewRect(viewport, chart.Anchor) : null;

    /// <summary>A press on a chart or a handle of the selected one: selects, starts moving or sizing. True when it was one.</summary>
    private bool PressChart(PointerPressedEventArgs e, PointerPoint point, Point device, GridViewport viewport)
    {
        if (_editor.Editing.IsEditing)
        {
            return false;
        }

        var shift = e.KeyModifiers.HasFlag(KeyModifiers.Shift);
        if (point.Properties.IsLeftButtonPressed && SelectedChartRect(viewport) is { } selected && Session.SelectedChart is { } current
            && HandleAt(selected, device) is { } handle)
        {
            _chartDrag = new ChartDrag(current.Id, handle, device, selected, shift);
            e.Pointer.Capture(this);
            e.Handled = true;
            return true;
        }

        if (ChartLayer.HitTest(viewport, device.X, device.Y) is not { } chart)
        {
            return false;
        }

        Focus();
        Session.SelectChart(chart.Id);
        if (point.Properties.IsRightButtonPressed)
        {
            ContextMenu = CalcContextMenus.ForChart(_editor.Window);
            return true;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            _chartDrag = new ChartDrag(chart.Id, null, device, ChartLayer.ViewRect(viewport, chart.Anchor), shift);
            e.Pointer.Capture(this);
            e.Handled = true;
        }

        return true;
    }

    /// <summary>Follows the pointer while a chart is moved or sized (an outline shows where it goes).</summary>
    private bool DragChart(Point device, KeyModifiers modifiers)
    {
        if (_chartDrag is not { } drag)
        {
            return false;
        }

        var dx = device.X - drag.Start.X;
        var dy = device.Y - drag.Start.Y;
        var original = drag.Original;
        if (drag.Handle is not { } handle)
        {
            drag.Current = original with { X = original.X + dx, Y = original.Y + dy };
            InvalidateVisual();
            return true;
        }

        var horizontal = handle switch { 0 or 6 or 7 => -1, 2 or 3 or 4 => 1, _ => 0 };
        var vertical = handle switch { 0 or 1 or 2 => -1, 4 or 5 or 6 => 1, _ => 0 };
        var minimum = 12 * RenderScaling;
        var width = Math.Max(minimum, original.Width + horizontal * dx);
        var height = Math.Max(minimum, original.Height + vertical * dy);
        if (horizontal != 0 && vertical != 0 && (drag.Proportional || modifiers.HasFlag(KeyModifiers.Shift)))
        {
            var scale = Math.Abs(width / original.Width - 1) >= Math.Abs(height / original.Height - 1) ? width / original.Width : height / original.Height;
            (width, height) = (original.Width * scale, original.Height * scale);
        }

        var x = horizontal < 0 ? original.Right - width : original.X;
        var y = vertical < 0 ? original.Bottom - height : original.Y;
        drag.Current = new PixelRect(x, y, width, height);
        InvalidateVisual();
        return true;
    }

    /// <summary>Ends a move or size: the new anchor as one undo step (a click without moving changes nothing).</summary>
    private bool ReleaseChart()
    {
        if (_chartDrag is not { } drag)
        {
            return false;
        }

        _chartDrag = null;
        var moved = Math.Abs(drag.Current.X - drag.Original.X) + Math.Abs(drag.Current.Y - drag.Original.Y)
            + Math.Abs(drag.Current.Width - drag.Original.Width) + Math.Abs(drag.Current.Height - drag.Original.Height);
        if (moved < 2)
        {
            InvalidateVisual();
            return true;
        }

        SetChartRect(drag.ChartId, drag.Original, drag.Current, drag.Handle is null ? "Diagramm verschieben" : "Diagrammgröße ändern");
        return true;
    }

    /// <summary>Moves a chart from one screen rectangle to another (the same change on the sheet's own coordinates).</summary>
    private void SetChartRect(int chartId, PixelRect from, PixelRect to, string label)
    {
        if (Session.ActiveSheet.Charts.FirstOrDefault(chart => chart.Id == chartId) is not { } entry)
        {
            return;
        }

        var viewport = Viewport();
        var layout = viewport.Layout;
        var sheet = ChartLayer.SheetRect(layout, entry.Anchor, viewport.Zoom);
        var target = new PixelRect(sheet.X + (to.X - from.X), sheet.Y + (to.Y - from.Y), sheet.Width + (to.Width - from.Width), sheet.Height + (to.Height - from.Height));
        var anchor = ChartLayer.AnchorOf(layout, target, viewport.Zoom);
        _ = _editor.RunAsync(label, () => Session.SetChartAnchor(chartId, anchor, label));
    }

    /// <summary>The keys while a chart is selected: arrows nudge it (Strg: by more), Entf deletes it, Esc returns to the cells.</summary>
    private bool HandleChartKey(Key key, KeyModifiers modifiers)
    {
        if (Session.SelectedChart is not { } chart)
        {
            return false;
        }

        var step = (modifiers.HasFlag(OperatingSystem.IsMacOS() ? KeyModifiers.Meta : KeyModifiers.Control) ? 10 : 1) * RenderScaling;
        var rect = ChartLayer.ViewRect(Viewport(), chart.Anchor);
        switch (key)
        {
            case Key.Delete or Key.Back:
                _ = _editor.RunAsync("Diagramm löschen", () => Session.DeleteChart(chart.Id));
                return true;
            case Key.Escape:
                Session.SelectChart(null);
                return true;
            case Key.Left:
                SetChartRect(chart.Id, rect, rect with { X = rect.X - step }, "Diagramm verschieben");
                return true;
            case Key.Right:
                SetChartRect(chart.Id, rect, rect with { X = rect.X + step }, "Diagramm verschieben");
                return true;
            case Key.Up:
                SetChartRect(chart.Id, rect, rect with { Y = rect.Y - step }, "Diagramm verschieben");
                return true;
            case Key.Down:
                SetChartRect(chart.Id, rect, rect with { Y = rect.Y + step }, "Diagramm verschieben");
                return true;
            case Key.Tab or Key.Enter or Key.F2:
                Session.SelectChart(null);
                return true;
            default:
                return false;
        }
    }

    /// <summary>The cursor over charts: the move cross over a chart, the size arrows over the selected one's handles.</summary>
    private Cursor? ChartCursor(GridViewport viewport, Point device)
    {
        if (_editor.Editing.IsEditing)
        {
            return null;
        }

        if (SelectedChartRect(viewport) is { } selected && HandleAt(selected, device) is { } handle)
        {
            return new Cursor(handle switch
            {
                0 or 4 => StandardCursorType.TopLeftCorner,
                2 or 6 => StandardCursorType.TopRightCorner,
                1 or 5 => StandardCursorType.SizeNorthSouth,
                _ => StandardCursorType.SizeWestEast,
            });
        }

        return ChartLayer.HitTest(viewport, device.X, device.Y) is not null ? new Cursor(StandardCursorType.SizeAll) : null;
    }

    private int? HandleAt(PixelRect rect, Point device)
    {
        var reach = ChartHandleSize * RenderScaling * 0.75;
        foreach (var (handle, x, y) in Handles(rect))
        {
            if (Math.Abs(device.X - x) <= reach && Math.Abs(device.Y - y) <= reach)
            {
                return handle;
            }
        }

        return null;
    }

    private static IEnumerable<(int Handle, double X, double Y)> Handles(PixelRect rect)
    {
        var centerX = rect.X + rect.Width / 2;
        var centerY = rect.Y + rect.Height / 2;
        yield return (0, rect.X, rect.Y);
        yield return (1, centerX, rect.Y);
        yield return (2, rect.Right, rect.Y);
        yield return (3, rect.Right, centerY);
        yield return (4, rect.Right, rect.Bottom);
        yield return (5, centerX, rect.Bottom);
        yield return (6, rect.X, rect.Bottom);
        yield return (7, rect.X, centerY);
    }

    /// <summary>The selected chart's frame and handles and the outline of a move or size (device pixels; theme colours).</summary>
    private void DrawChartSelection(SKCanvas canvas, GridViewport viewport, PixelRect? frame, PixelRect? outline, double scaling)
    {
        if (frame is null && outline is null)
        {
            return;
        }

        var accent = GridPalette.Token("Mat.Accent");
        var surface = GridPalette.Token("Mat.Surface");
        canvas.Save();
        canvas.ClipRect(SKRect.Create((float)viewport.HeaderWidth, (float)viewport.HeaderHeight, (float)(viewport.Width - viewport.HeaderWidth), (float)(viewport.Height - viewport.HeaderHeight)));
        using var line = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(1, Math.Round(scaling)), Color = new SKColor(accent.R, accent.G, accent.B), IsAntialias = false };
        if (frame is { } rect)
        {
            canvas.DrawRect(Snapped(rect), line);
            using var fill = new SKPaint { Style = SKPaintStyle.Fill, Color = new SKColor(surface.R, surface.G, surface.B), IsAntialias = true };
            using var border = new SKPaint { Style = SKPaintStyle.Stroke, StrokeWidth = (float)Math.Max(1, Math.Round(scaling)), Color = new SKColor(accent.R, accent.G, accent.B), IsAntialias = true };
            var size = (float)(ChartHandleSize * scaling);
            foreach (var (_, x, y) in Handles(rect))
            {
                var handle = SKRect.Create((float)Math.Round(x - size / 2) + 0.5f, (float)Math.Round(y - size / 2) + 0.5f, size, size);
                canvas.DrawRoundRect(handle, size / 4, size / 4, fill);
                canvas.DrawRoundRect(handle, size / 4, size / 4, border);
            }
        }

        if (outline is { } moving)
        {
            line.PathEffect = SKPathEffect.CreateDash([4 * (float)scaling, 3 * (float)scaling], 0);
            canvas.DrawRect(Snapped(moving), line);
        }

        canvas.Restore();
    }

    private static SKRect Snapped(PixelRect rect) =>
        SKRect.Create((float)Math.Round(rect.X) + 0.5f, (float)Math.Round(rect.Y) + 0.5f, (float)Math.Round(rect.Width) - 1, (float)Math.Round(rect.Height) - 1);
}
