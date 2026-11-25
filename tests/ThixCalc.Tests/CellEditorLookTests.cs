using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using static ThixCalc.Tests.TestData;
using PixelRect = Avalonia.PixelRect;
using VerticalAlignment = ThixCalc.Core.Model.VerticalAlignment;

namespace ThixCalc.Tests;

/// <summary>
/// How editing a cell looks, checked on rendered frames: the text stays where the grid drew it (every vertical alignment, every
/// zoom), the active cell's border stays as it was and grows with the field, several lines start at the top, and the
/// formula bar's Namenfeld and content field share one line.
/// </summary>
public sealed class CellEditorLookTests
{
    private const string Text = "Miete";
    private static readonly CellAddress Cell = new(1, 1);

    [AvaloniaFact]
    public async Task F2_keeps_the_text_and_the_border_where_they_were_in_every_vertical_alignment()
    {
        using var data = new TestData();
        var window = await PrepareAsync();
        var editor = EditorOf(window);
        editor.Session.SetRowHeight(1, 1, 36);
        foreach (var alignment in new[] { VerticalAlignment.Bottom, VerticalAlignment.Top, VerticalAlignment.Center })
        {
            editor.Session.SetVerticalAlignment(alignment);
            await SettleAsync();
            await AssertEditingKeepsTheCellAsync(window, $"{alignment}");
        }
    }

    [AvaloniaFact]
    public async Task F2_keeps_the_text_and_the_border_at_50_and_200_percent()
    {
        using var data = new TestData();
        var window = await PrepareAsync();
        var editor = EditorOf(window);
        foreach (var zoom in new[] { 50.0, 200.0, 100.0 })
        {
            editor.Zoom = zoom;
            await SettleAsync();
            await AssertEditingKeepsTheCellAsync(window, $"{zoom} %");
        }
    }

    [AvaloniaFact]
    public async Task A_double_click_edits_with_the_text_in_place()
    {
        using var data = new TestData();
        var window = await PrepareAsync();
        var editor = EditorOf(window);
        var before = Frame.Capture(window);
        await ClickAsync(window, PointOf(window, Cell), clicks: 2);
        Assert.True(editor.Editing.IsEditing);
        Assert.Equal("Bearbeiten", window.StatusBar.TextOf(CalcEditor.ModeItem));
        AssertTextOriginMatchesTheGrid(window);
        var text = TextRegion(window);
        var after = Frame.Capture(window);
        Assert.Equal(before.Ink(text), after.Ink(text));
    }

    [AvaloniaFact]
    public async Task The_border_surrounds_the_field_when_it_grows_right_and_down()
    {
        using var data = new TestData();
        var window = await PrepareAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "Ein langer Text, der weit über die Zelle hinausreicht");
        var cell = CellRect(window);
        var box = BoxRect(window);
        Assert.True(box.Width > cell.Width + 40, $"the field grows to the right ({box.Width} > {cell.Width})");
        Assert.Equal(cell.Bottom, box.Bottom);
        AssertFramed(window, box);

        // Alt+Eingabe: a second line – the field grows down, the text starts at the top.
        await PressAsync(window, PhysicalKey.Enter, RawInputModifiers.Alt);
        await TypeAsync(window, "zweite Zeile");
        box = BoxRect(window);
        Assert.True(box.Height > cell.Height + 10, $"the field grows down ({box.Height} > {cell.Height})");
        AssertFramed(window, box);
        var origin = Origin(window);
        Assert.Equal(origin.TopBaseline, BoxBaseline(window), 2);
        await PressAsync(window, PhysicalKey.Escape);
        Assert.False(editor.Editing.IsEditing);
    }

    [AvaloniaFact]
    public async Task A_wrapped_cell_wraps_at_its_width_and_the_field_grows_down()
    {
        using var data = new TestData();
        var window = await PrepareAsync();
        var editor = EditorOf(window);
        editor.Session.ApplyStyle(style => style with { Alignment = style.Alignment with { WrapText = true } });
        await SettleAsync();
        await TypeAsync(window, "Ein langer Text, der in der Zelle umbricht");
        var cell = CellRect(window);
        var box = BoxRect(window);
        Assert.Equal(cell.Width + 1, box.Width);
        Assert.True(box.Height > cell.Height + 10, $"the field grows down ({box.Height} > {cell.Height})");
        AssertFramed(window, box);
        Assert.Equal(Origin(window).TopBaseline, BoxBaseline(window), 2);
    }

    [AvaloniaFact]
    public async Task Point_mode_keeps_working_in_the_framed_field()
    {
        using var data = new TestData();
        var window = await PrepareAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "=");
        await PressAsync(window, PhysicalKey.ArrowUp);
        Assert.Equal("Zeigen", window.StatusBar.TextOf(CalcEditor.ModeItem));
        Assert.Equal("=B1", editor.Editing.CellBox.Text);
        Assert.Single(editor.Editing.Highlights);
        AssertFramed(window, BoxRect(window));
        await PressAsync(window, PhysicalKey.Escape);
    }

    [AvaloniaTheory]
    [InlineData(1.25, 100)]
    [InlineData(1.5, 100)]
    [InlineData(1.25, 75)]
    [InlineData(1.5, 200)]
    [InlineData(1, 50)]
    public void The_field_and_its_border_are_whole_device_pixels_at_every_scaling(double scaling, double zoom)
    {
        var workbook = new Workbook();
        var sheet = workbook.AddSheet("Tabelle1");
        var metrics = new GridMetrics(CalcFonts.Grid, workbook, ThixCalc.Core.Globalization.CalcLocale.German);
        var layout = new SheetLayout(sheet, metrics);
        var viewport = layout.Viewport(1200 * scaling, 800 * scaling, zoom / 100 * scaling, 0, 0);
        foreach (var cell in new[] { new CellAddress(0, 0), new CellAddress(3, 2), new CellAddress(10, 7) })
        {
            var geometry = CellBoxGeometry.Of(viewport, metrics, cell, scaling);
            var frame = SelectionRenderer.ActiveFrame(viewport, CellRange.Cell(cell));
            AssertWhole(geometry.Left * scaling, frame.X);
            AssertWhole(geometry.Top * scaling, frame.Y);
            AssertWhole(geometry.Width * scaling, frame.Width);
            AssertWhole(geometry.Height * scaling, frame.Height);
            AssertWhole(geometry.Frame.Right * scaling, SelectionRenderer.ActiveBorderWidth);
            AssertWhole(geometry.Frame.Bottom * scaling, SelectionRenderer.ActiveBorderWidth);
            var origin = GridRenderer.TextOrigin(viewport, metrics, cell);
            Assert.Equal(origin.X, (geometry.Left + geometry.Padding.Left) * scaling, 6);
            Assert.Equal(origin.Baseline, (geometry.Top + geometry.Baseline) * scaling, 6);
        }

        static void AssertWhole(double devicePixels, double expected)
        {
            Assert.Equal(expected, devicePixels, 6);
            Assert.Equal(Math.Round(devicePixels), devicePixels, 6);
        }
    }

    [AvaloniaFact]
    public async Task The_Namenfeld_and_the_content_field_share_one_line()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var bar = EditorOf(window).FormulaBar;
        var name = RectIn(bar.NameBox, bar);
        var field = RectIn(bar.FieldFrame, bar);
        Assert.Equal(FormulaBar.RowHeight, name.Height, 3);
        Assert.Equal(name.Top, field.Top, 3);
        Assert.Equal(name.Bottom, field.Bottom, 3);

        // The chevron of the names lies inside the Namenfeld's frame.
        var chevron = RectIn(bar.NamesButton, bar);
        Assert.True(name.Contains(chevron), $"{chevron} inside {name}");

        // X, check, fx and the separator are centred on the same line.
        var line = name.Center.Y;
        foreach (var control in ((Grid)bar.Child!).Children.Where(control => control != bar.NameBox && control != bar.FieldFrame))
        {
            Assert.Equal(line, RectIn(control, bar).Center.Y, 1);
        }

        // The content field's text is centred in it (one line).
        var text = bar.Field.TranslatePoint(bar.Field.TextOrigin, bar)!.Value;
        var middle = text.Y + bar.Field.LineHeight / 2;
        Assert.Equal(line, middle, 1);

        // Expanded: the field is taller, the Namenfeld and the buttons stay on the top line.
        bar.Expanded = true;
        await SettleAsync();
        var expanded = RectIn(bar.FieldFrame, bar);
        Assert.True(expanded.Height > 3 * name.Height, $"{expanded.Height}");
        Assert.Equal(name.Top, RectIn(bar.NameBox, bar).Top, 3);
        Assert.Equal(name.Top, expanded.Top, 3);
        Assert.Equal(text.Y, bar.Field.TranslatePoint(bar.Field.TextOrigin, bar)!.Value.Y, 3);
        foreach (var control in ((Grid)bar.Child!).Children.Where(control => control != bar.NameBox && control != bar.FieldFrame))
        {
            Assert.Equal(line, RectIn(control, bar).Center.Y, 1);
        }

        bar.Expanded = false;
    }

    // ===== Helpers =====

    /// <summary>A window with <see cref="Text"/> in B2 and B2 active.</summary>
    private static async Task<OfficeWindow> PrepareAsync()
    {
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        editor.Session.Select(Cell);
        await EnterAsync(window, Text);
        editor.Session.Select(Cell);
        await SettleAsync();
        return window;
    }

    /// <summary>Ready → F2: the text pixels and the border pixels are the same; Esc.</summary>
    private static async Task AssertEditingKeepsTheCellAsync(OfficeWindow window, string what)
    {
        var editor = EditorOf(window);
        var before = Frame.Capture(window);
        await PressAsync(window, PhysicalKey.F2);
        Assert.True(editor.Editing.IsEditing, what);
        AssertTextOriginMatchesTheGrid(window);
        var after = Frame.Capture(window);
        var text = TextRegion(window);
        Assert.True(before.Ink(text) > 0, $"{what}: the cell shows its text");
        Assert.True(before.Ink(text) == after.Ink(text), $"{what}: the text moved when editing started");

        // The border: the same pixels on every side (the box covers neither the grid lines nor the border).
        var frame = BoxRect(window);
        Assert.Equal(FrameRect(window), frame);
        foreach (var (x, y) in Edges(frame))
        {
            Assert.True(before.At(x, y) == after.At(x, y), $"{what}: the border changed at ({x}, {y})");
            Assert.Equal(Accent, after.At(x, y));
        }

        await PressAsync(window, PhysicalKey.Escape);
        Assert.False(editor.Editing.IsEditing);
    }

    private static void AssertTextOriginMatchesTheGrid(OfficeWindow window)
    {
        var editor = EditorOf(window);
        var box = editor.Editing.CellBox;
        var scaling = Scaling(window);
        var origin = Origin(window);
        var text = box.TranslatePoint(box.TextOrigin, editor.Surface)!.Value;
        Assert.Equal(origin.X / scaling, text.X, 3);
        Assert.Equal(origin.Baseline / scaling, BoxBaseline(window), 3);
    }

    /// <summary>The grid's text origin of the cell (device pixels of the viewport).</summary>
    private static (double X, double Baseline, double TopBaseline) Origin(OfficeWindow window)
    {
        var editor = EditorOf(window);
        return GridRenderer.TextOrigin(editor.Surface.Viewport(), editor.Metrics, Cell);
    }

    /// <summary>The field's first baseline in the surface's DIPs.</summary>
    private static double BoxBaseline(OfficeWindow window)
    {
        var editor = EditorOf(window);
        var box = editor.Editing.CellBox;
        return box.TranslatePoint(new Point(0, box.FirstBaseline), editor.Surface)!.Value.Y;
    }

    private static double Scaling(OfficeWindow window) => window.RenderScaling;

    /// <summary>The cell in window pixels.</summary>
    private static PixelRect CellRect(OfficeWindow window) => ToWindow(window, EditorOf(window).Surface.Viewport().CellRect(Cell));

    /// <summary>The active border's pixels (window pixels).</summary>
    private static PixelRect FrameRect(OfficeWindow window)
    {
        var viewport = EditorOf(window).Surface.Viewport();
        return ToWindow(window, SelectionRenderer.ActiveFrame(viewport, CellRange.Cell(Cell)));
    }

    /// <summary>The cell's field in window pixels.</summary>
    private static PixelRect BoxRect(OfficeWindow window)
    {
        var box = EditorOf(window).Editing.CellBox;
        var origin = box.TranslatePoint(default, window)!.Value;
        var scaling = Scaling(window);
        return new PixelRect((int)Math.Round(origin.X * scaling), (int)Math.Round(origin.Y * scaling), (int)Math.Round(box.Bounds.Width * scaling), (int)Math.Round(box.Bounds.Height * scaling));
    }

    /// <summary>Where the cell's text is drawn, before the caret (window pixels).</summary>
    private static PixelRect TextRegion(OfficeWindow window)
    {
        var cell = CellRect(window);
        var width = Math.Min(cell.Width / 2 - 6, 24);
        return new PixelRect(cell.X + 2, cell.Y + 1, width, cell.Height - 3);
    }

    private static PixelRect ToWindow(OfficeWindow window, ThixCalc.Core.Layout.PixelRect rect)
    {
        var surface = EditorOf(window).Surface;
        var scaling = Scaling(window);
        var origin = surface.TranslatePoint(default, window)!.Value;
        return new PixelRect((int)Math.Round(origin.X * scaling + rect.X), (int)Math.Round(origin.Y * scaling + rect.Y), (int)Math.Round(rect.Width), (int)Math.Round(rect.Height));
    }

    /// <summary>A point in the middle of each side of the 2-pixel border (both pixels).</summary>
    private static IEnumerable<(int X, int Y)> Edges(PixelRect frame)
    {
        var middleX = frame.X + frame.Width / 2;
        var middleY = frame.Y + frame.Height / 2;
        yield return (middleX, frame.Y);
        yield return (middleX, frame.Y + 1);
        yield return (middleX, frame.Bottom - 1);
        yield return (middleX, frame.Bottom - 2);
        yield return (frame.X, middleY);
        yield return (frame.X + 1, middleY);
        yield return (frame.Right - 1, middleY);
        yield return (frame.Right - 2, middleY);
        yield return (frame.Right - 1, frame.Y);
        yield return (frame.X, frame.Bottom - 1);
    }

    private static void AssertFramed(OfficeWindow window, PixelRect box)
    {
        var frame = Frame.Capture(window);
        foreach (var (x, y) in Edges(box))
        {
            Assert.Equal(Accent, frame.At(x, y));
        }

        // Just outside the field there is no border.
        Assert.NotEqual(Accent, frame.At(box.Right + 1, box.Y + box.Height / 2));
        Assert.NotEqual(Accent, frame.At(box.X + box.Width / 2, box.Bottom + 1));
        Assert.Equal(GridPalette.SheetColor, frame.At(box.Right - 3, box.Y + 2));
    }

    private static Color Accent => ((ISolidColorBrush)Ui.Brush("Mat.Accent")).Color;

    private static Rect RectIn(Control control, Visual relativeTo)
    {
        var origin = control.TranslatePoint(default, relativeTo)!.Value;
        return new Rect(origin, control.Bounds.Size);
    }

    /// <summary>A rendered frame of the window as colours.</summary>
    private sealed class Frame
    {
        private readonly int[] _pixels;
        private readonly int _width;
        private readonly bool _rgba;

        private Frame(int[] pixels, int width, bool rgba)
        {
            _pixels = pixels;
            _width = width;
            _rgba = rgba;
        }

        public static Frame Capture(OfficeWindow window)
        {
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            using var bitmap = window.CaptureRenderedFrame() ?? throw new InvalidOperationException("no frame");
            using var buffer = ((WriteableBitmap)bitmap).Lock();
            var width = buffer.Size.Width;
            var pixels = new int[width * buffer.Size.Height];
            for (var row = 0; row < buffer.Size.Height; row++)
            {
                Marshal.Copy(buffer.Address + row * buffer.RowBytes, pixels, row * width, width);
            }

            return new Frame(pixels, width, buffer.Format == PixelFormat.Rgba8888);
        }

        public Color At(int x, int y)
        {
            var value = _pixels[y * _width + x];
            var first = (byte)value;
            var second = (byte)(value >> 8);
            var third = (byte)(value >> 16);
            return _rgba ? Color.FromRgb(first, second, third) : Color.FromRgb(third, second, first);
        }

        /// <summary>A fingerprint of the dark (text) pixels of a region: equal when the text lies on the same pixels.</summary>
        public long Ink(PixelRect region)
        {
            long sum = 0;
            for (var y = region.Y; y < region.Bottom; y++)
            {
                for (var x = region.X; x < region.Right; x++)
                {
                    var color = At(x, y);
                    if ((color.R + color.G + color.B) / 3 < 140)
                    {
                        sum += (x - region.X + 1) * 7919L + (y - region.Y + 1) * 104729L;
                    }
                }
            }

            return sum;
        }
    }
}
