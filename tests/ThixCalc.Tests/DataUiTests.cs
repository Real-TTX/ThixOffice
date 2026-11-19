using Avalonia.Headless.XUnit;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>Autofilter arrows and lists, list validation, notes and links in the grid.</summary>
public sealed class DataUiTests
{
    [AvaloniaFact]
    public async Task A_click_on_a_filter_arrow_opens_the_list_and_filters()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        string[] values = ["Obst", "Apfel", "Birne", "Apfel", "Kiwi"];
        foreach (var value in values)
        {
            await EnterAsync(window, value);
        }

        editor.Session.Select(new CellAddress(0, 0));
        await window.Commands.ExecuteAsync(Editor.CalcCommandIds.Filter);
        await SettleAsync();
        var viewport = editor.Surface.Viewport();
        var button = GridRenderer.FilterButtonRect(viewport, 0)!.Value;
        var scale = editor.Surface.Scale;
        await ClickAsync(window, SurfacePoint(window, (button.X + button.Width / 2) / scale, (button.Y + button.Height / 2) / scale));
        var menu = editor.Surface.FilterMenu;
        Assert.NotNull(menu);
        Assert.Equal(3, menu.Values.Count);
        menu.Values.First(box => (string)box.Content! == "Apfel").IsChecked = false;
        menu.Apply(new FilterColumn { Values = ["Birne", "Kiwi"] });
        await SettleAsync();
        Assert.True(editor.Session.ActiveSheet.Rows.IsHidden(1));
        Assert.False(editor.Session.ActiveSheet.Rows.IsHidden(2));
    }

    [AvaloniaFact]
    public async Task A_list_validation_offers_its_entries()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        editor.Session.AddValidation(new DataValidation { Ranges = [CellRange.Cell(0, 0)], Type = ValidationType.List, Formula1 = "\"Ja,Nein\"" });
        await TypeAsync(window, "Vielleicht");
        await PressAsync(window, Avalonia.Input.PhysicalKey.Enter);
        Assert.True(editor.Editing.IsEditing); // refused: the cell stays open
        await PressAsync(window, Avalonia.Input.PhysicalKey.Escape);
        foreach (var dialog in window.OwnedWindows.ToList())
        {
            dialog.Close();
        }

        await SettleAsync();
        editor.Session.Select(new CellAddress(0, 0));
        await TypeAsync(window, "Ja");
        await PressAsync(window, Avalonia.Input.PhysicalKey.Enter);
        Assert.Equal("Ja", ValueAt(window, "A1").Text);
    }

    [AvaloniaFact]
    public void Notes_get_their_red_triangle()
    {
        var workbook = Workbook.Create();
        var sheet = workbook.Sheets[0];
        sheet.Comments[new CellAddress(1, 1)] = new CellComment("Anna", "Prüfen");
        var metrics = new GridMetrics(new ThixOffice.Text.FixedWidthTextMeasurer(), workbook, Core.Globalization.CalcLocale.German);
        var viewport = new SheetLayout(sheet, metrics).Viewport(400, 200, 1, 0, 0);
        var options = new GridRenderOptions();
        var list = GridRenderer.Render(viewport, metrics, options);
        Assert.Contains(list.Items.OfType<ThixOffice.Text.Drawing.LineItem>(), line => line.Color == options.NoteMarker);
    }
}
