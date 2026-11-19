using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>The grid driven by real keys, text and the pointer: entering, edit modes, navigation, point mode, F4, autofill, resizing.</summary>
public sealed class GridTests
{
    [AvaloniaFact]
    public async Task Typing_replaces_and_Enter_moves_down_Tab_moves_right()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "12");
        await TypeAsync(window, "Text");
        await PressAsync(window, PhysicalKey.Tab);
        await EnterAsync(window, "1,5");
        var editor = EditorOf(window);
        Assert.Equal(12, ValueAt(window, "A1").Number);
        Assert.Equal("Text", ValueAt(window, "A2").Text);
        Assert.Equal(1.5, ValueAt(window, "B2").Number);
        Assert.Equal(new CellAddress(2, 1), editor.Session.ActiveCell);
        Assert.Equal("Bereit", window.StatusBar.TextOf(CalcEditor.ModeItem));
    }

    [AvaloniaFact]
    public async Task Modes_Eingeben_Bearbeiten_and_Escape()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "ab");
        Assert.Equal("Eingeben", window.StatusBar.TextOf(CalcEditor.ModeItem));
        Assert.Equal("ab", editor.FormulaBar.Field.Text);
        await PressAsync(window, PhysicalKey.F2);
        Assert.Equal("Bearbeiten", window.StatusBar.TextOf(CalcEditor.ModeItem));
        await PressAsync(window, PhysicalKey.ArrowLeft);
        await TypeAsync(window, "X");
        await PressAsync(window, PhysicalKey.Enter);
        Assert.Equal("aXb", ValueAt(window, "A1").Text);

        // Esc keeps the old content.
        await PressAsync(window, PhysicalKey.ArrowUp);
        await TypeAsync(window, "neu");
        await PressAsync(window, PhysicalKey.Escape);
        Assert.Equal("aXb", ValueAt(window, "A1").Text);
        Assert.False(editor.Editing.IsEditing);

        // An arrow in Eingeben mode ends the input and moves.
        await TypeAsync(window, "7");
        await PressAsync(window, PhysicalKey.ArrowRight);
        Assert.Equal(7, ValueAt(window, "A1").Number);
        Assert.Equal(new CellAddress(0, 1), editor.Session.ActiveCell);
    }

    [AvaloniaFact]
    public async Task Ctrl_arrows_jump_to_the_data_edge_and_Shift_extends()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        for (var row = 0; row < 5; row++)
        {
            await EnterAsync(window, (row + 1).ToString(System.Globalization.CultureInfo.InvariantCulture));
        }

        var session = EditorOf(window).Session;
        session.Select(new CellAddress(0, 0));
        await PressAsync(window, PhysicalKey.ArrowDown, CommandKey);
        Assert.Equal(new CellAddress(4, 0), session.ActiveCell);
        await PressAsync(window, PhysicalKey.ArrowUp, CommandKey | RawInputModifiers.Shift);
        Assert.Equal(new CellRange(0, 0, 4, 0), session.Selection);
        Assert.Contains("Summe: 15", window.StatusBar.TextOf(CalcEditor.SumItem), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_formula_is_pointed_with_arrows_and_cycled_with_F4()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "4");
        await EnterAsync(window, "6");
        await TypeAsync(window, "=");
        await PressAsync(window, PhysicalKey.ArrowUp);
        await PressAsync(window, PhysicalKey.ArrowUp);
        var editor = EditorOf(window);
        Assert.Equal("=A1", editor.Editing.CellBox.Text);
        Assert.Equal("Zeigen", window.StatusBar.TextOf(CalcEditor.ModeItem));
        Assert.Single(editor.Editing.Highlights);
        await PressAsync(window, PhysicalKey.F4);
        Assert.Equal("=$A$1", editor.Editing.CellBox.Text);
        await TypeAsync(window, "+");
        await PressAsync(window, PhysicalKey.ArrowUp);
        await PressAsync(window, PhysicalKey.Enter);
        Assert.Equal(10, ValueAt(window, "A3").Number);
        editor.Session.Select(new CellAddress(2, 0));
        Assert.Equal("=$A$1+A2", editor.Session.FormulaBarText);
    }

    [AvaloniaFact]
    public async Task Clicking_a_cell_while_typing_a_formula_points_it_in()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        editor.Session.Select(new CellAddress(2, 1));
        editor.Session.SetValue(new CellAddress(0, 3), 21);
        await TypeAsync(window, "=2*");
        await ClickAsync(window, PointOf(window, new CellAddress(0, 3)));
        Assert.Equal("=2*D1", editor.Editing.CellBox.Text);
        await PressAsync(window, PhysicalKey.Enter);
        Assert.Equal(42, ValueAt(window, "B3").Number);
    }

    [AvaloniaFact]
    public async Task Function_names_are_suggested_and_the_argument_tip_follows()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "=SVER");
        Assert.Contains(editor.Editing.Suggestions, info => info.Name == "SVERWEIS");
        await PressAsync(window, PhysicalKey.Tab);
        Assert.Equal("=SVERWEIS(", editor.Editing.CellBox.Text);
        Assert.Equal(0, editor.Editing.ArgumentTip?.Argument);
        await TypeAsync(window, "1;");
        Assert.Equal(1, editor.Editing.ArgumentTip?.Argument);
        Assert.Equal("Matrix", editor.Editing.ArgumentTip?.Function.ArgumentName(1));
    }

    [AvaloniaFact]
    public async Task An_invalid_formula_keeps_the_cell_open()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        await TypeAsync(window, "=1+*2");
        await PressAsync(window, PhysicalKey.Enter);
        Assert.True(editor.Editing.IsEditing);
        Assert.True(ValueAt(window, "A1").IsBlank);
    }

    [AvaloniaFact]
    public async Task Ctrl_Enter_fills_the_selection()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        EditorOf(window).Session.Select(new CellRange(0, 0, 2, 1));
        await TypeAsync(window, "x");
        await PressAsync(window, PhysicalKey.Enter, CommandKey);
        Assert.Equal("x", ValueAt(window, "B3").Text);
    }

    [AvaloniaFact]
    public async Task Dragging_the_fill_handle_continues_a_series()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "1");
        await EnterAsync(window, "2");
        var editor = EditorOf(window);
        editor.Session.Select(new CellRange(0, 0, 1, 0));
        await SettleAsync();
        var viewport = editor.Surface.Viewport();
        var rect = viewport.RangeRect(new CellRange(0, 0, 1, 0));
        var scaling = editor.Surface.Scale;
        var handle = SurfacePoint(window, rect.Right / scaling - 1, rect.Bottom / scaling - 1);
        await DragAsync(window, handle, PointOf(window, new CellAddress(4, 0)));
        Assert.Equal(5, ValueAt(window, "A5").Number);
    }

    [AvaloniaFact]
    public async Task Dragging_a_column_border_resizes_and_a_double_click_fits()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var viewport = editor.Surface.Viewport();
        var border = viewport.Columns[0].End / editor.Surface.Scale;
        var y = viewport.HeaderHeight / editor.Surface.Scale / 2;
        await DragAsync(window, SurfacePoint(window, border - 1, y), SurfacePoint(window, border + 40, y));
        Assert.True(editor.Session.ActiveSheet.Columns[0].Size > 12);

        editor.Session.SetValue(new CellAddress(0, 1), CellValue.FromText("Ein recht langer Text in B"));
        await SettleAsync();
        viewport = editor.Surface.Viewport();
        var second = viewport.Columns[1].End / editor.Surface.Scale;
        await ClickAsync(window, SurfacePoint(window, second - 1, y), clicks: 2);
        Assert.True(editor.Session.ActiveSheet.Columns[1].Size > 15);
    }

    [AvaloniaFact]
    public async Task Header_clicks_select_rows_and_columns_and_the_corner_everything()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var viewport = editor.Surface.Viewport();
        var scale = editor.Surface.Scale;
        await ClickAsync(window, SurfacePoint(window, (viewport.Columns[2].Start + 10) / scale, viewport.HeaderHeight / scale / 2));
        Assert.True(editor.Session.Selection.IsWholeColumns);
        Assert.Equal(2, editor.Session.Selection.FirstColumn);
        await ClickAsync(window, SurfacePoint(window, viewport.HeaderWidth / scale / 2, (viewport.Rows[3].Start + 5) / scale));
        Assert.True(editor.Session.Selection.IsWholeRows);
        await ClickAsync(window, SurfacePoint(window, 3, 3));
        Assert.Equal(CellRange.Whole, editor.Session.Selection);
    }

    [AvaloniaFact]
    public async Task The_wheel_scrolls_by_pixels_and_Ctrl_wheel_zooms()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var point = PointOf(window, new CellAddress(3, 3));
        window.MouseWheel(point, new Avalonia.Vector(0, -1));
        await SettleAsync();
        Assert.True(editor.Surface.ScrollOffset.Y > 0);
        Assert.Equal(3, editor.Session.ActiveSheet.View.TopLeft.Row);
        window.MouseWheel(point, new Avalonia.Vector(0, 1), CommandKey);
        await SettleAsync();
        Assert.Equal(110, editor.Zoom);
    }

    [AvaloniaFact]
    public async Task Moving_far_down_scrolls_and_a_large_sheet_draws_fast()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var sheet = editor.Session.ActiveSheet;
        for (var row = 0; row < 100_000; row++)
        {
            sheet.SetValue(row, 0, row);
            sheet.SetValue(row, 1, CellValue.FromText("Zeile " + row));
        }

        editor.Session.Recalculate();
        editor.Session.Select(new CellAddress(0, 0));
        await SettleAsync();
        await PressAsync(window, PhysicalKey.ArrowDown, CommandKey);
        Assert.Equal(new CellAddress(99_999, 0), editor.Session.ActiveCell);
        Assert.Contains(editor.Surface.Viewport().Rows, row => row.Index == 99_999);
        Assert.True(editor.Surface.LastRenderTime < TimeSpan.FromMilliseconds(250), $"a frame took {editor.Surface.LastRenderTime.TotalMilliseconds} ms");
    }
}
