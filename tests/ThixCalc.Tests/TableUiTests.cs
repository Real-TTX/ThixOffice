using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MatDesktop;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using ThixOffice.Desktop.Shell;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>Tables in the real ThixCalc: Strg+T and its dialog, Tabellenentwurf (options, name, total row), the header's filter arrows, typing below.</summary>
public sealed class TableUiTests
{
    private static async Task<OfficeWindow> DataAsync()
    {
        var window = await NewWindowAsync();
        foreach (var row in new[] { new[] { "Region", "Menge" }, ["Nord", "10"], ["Süd", "4"], ["Ost", "7"] })
        {
            foreach (var value in row)
            {
                await TypeAsync(window, value);
                await PressAsync(window, PhysicalKey.Tab);
            }

            await PressAsync(window, PhysicalKey.Enter);
            EditorOf(window).Session.Select(new CellAddress(EditorOf(window).Session.ActiveCell.Row, 0));
        }

        EditorOf(window).Session.Select(new CellAddress(1, 1));
        await SettleAsync();
        return window;
    }

    [AvaloniaFact]
    public async Task Strg_T_proposes_the_region_and_makes_a_table()
    {
        using var data = new TestData();
        var window = await DataAsync();
        var editor = EditorOf(window);

        var dialog = CreateTableDialog.Create(editor);
        Assert.Equal("=Tabelle1!$A$1:$B$4", dialog.RangeBox.Text);
        Assert.True(dialog.HeaderBox.IsChecked);
        dialog.Confirm();
        await SettleAsync();

        var table = Assert.Single(editor.Session.ActiveSheet.Tables);
        Assert.Equal(CellRange.Parse("A1:B4"), table.Range);
        Assert.Contains(CalcTableCommands.Context, window.RibbonDefinition.ActiveContexts);
        Assert.True(window.RibbonDefinition.FindAll(CalcCommandIds.TableBandedRows).OfType<MatRibbonToggle>().All(toggle => toggle.IsChecked));

        // Strg+T in a table does nothing more (the command is off there).
        Assert.False(window.Commands[CalcCommandIds.InsertTable]!.IsEnabled);
    }

    [AvaloniaFact]
    public async Task Tabellenentwurf_switches_options_renames_and_adds_the_total_row()
    {
        using var data = new TestData();
        var window = await DataAsync();
        var editor = EditorOf(window);
        editor.Session.CreateTable(CellRange.Parse("A1:B4"), true);
        await SettleAsync();

        await window.Commands.ExecuteAsync(CalcCommandIds.TableTotalRow);
        await window.Commands.ExecuteAsync(CalcCommandIds.TableBandedRows);
        await ((ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[CalcCommandIds.TableName]).ApplyAsync("Verkauf");
        await ((ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[CalcCommandIds.TableStyles]).ApplyAsync("TableStyleMedium7");
        await SettleAsync();

        var table = editor.Session.ActiveSheet.Tables[0];
        Assert.Equal("Verkauf", table.Name);
        Assert.True(table.HasTotals);
        Assert.False(table.BandedRows);
        Assert.Equal("TableStyleMedium7", table.Style);
        Assert.Equal(21, ValueAt(window, "B5").Number);
        Assert.Equal("Ergebnis", ValueAt(window, "A5").Text);
    }

    [AvaloniaFact]
    public async Task The_header_arrow_filters_the_table()
    {
        using var data = new TestData();
        var window = await DataAsync();
        var editor = EditorOf(window);
        editor.Session.CreateTable(CellRange.Parse("A1:B4"), true);
        await SettleAsync();
        var viewport = editor.Surface.Viewport();
        var button = GridRenderer.FilterButtonRect(viewport, new CellAddress(0, 0))!.Value;
        var scale = editor.Surface.Scale;

        await ClickAsync(window, SurfacePoint(window, (button.X + button.Width / 2) / scale, (button.Y + button.Height / 2) / scale));

        var menu = editor.Surface.FilterMenu;
        Assert.NotNull(menu);
        Assert.Equal(3, menu.Values.Count);
        menu.Apply(new FilterColumn { Values = ["Süd"] });
        await SettleAsync();
        Assert.True(editor.Session.ActiveSheet.Rows.IsHidden(1));
        Assert.False(editor.Session.ActiveSheet.Rows.IsHidden(2));
    }

    [AvaloniaFact]
    public async Task Typing_below_the_table_extends_it()
    {
        using var data = new TestData();
        var window = await DataAsync();
        var editor = EditorOf(window);
        editor.Session.CreateTable(CellRange.Parse("A1:B4"), true);
        editor.Session.Select(CellAddress.Parse("A5"));
        await SettleAsync();

        await EnterAsync(window, "West");

        Assert.Equal(CellRange.Parse("A1:B5"), editor.Session.ActiveSheet.Tables[0].Range);
        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Equal(CellRange.Parse("A1:B4"), editor.Session.ActiveSheet.Tables[0].Range);
    }
}
