using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixCalc.Core.Model;
using ThixCalc.Core.Model.Pivot;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>PivotTables through the real window: Einfügen › PivotTable, the field list (ticks, drag, menus), settings, filter, refresh.</summary>
public sealed class PivotUiTests
{
    private static async Task FillSalesAsync(ThixOffice.Desktop.Shell.OfficeWindow window)
    {
        string[][] rows =
        [
            ["Region", "Produkt", "Umsatz"],
            ["Nord", "Äpfel", "100"],
            ["Süd", "Äpfel", "200"],
            ["Nord", "Birnen", "50"],
            ["West", "Birnen", "80"],
            ["Süd", "Birnen", "120"],
        ];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                await TypeAsync(window, row[column]);
                await PressAsync(window, column < row.Length - 1 ? PhysicalKey.Tab : PhysicalKey.Enter);
            }
        }

        EditorOf(window).Session.Select(new CellAddress(1, 0));
        await SettleAsync();
    }

    [AvaloniaFact]
    public async Task A_pivot_is_built_from_the_insert_dialog_and_the_field_list()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await FillSalesAsync(window);
        var editor = EditorOf(window);

        // Einfügen › PivotTable: the dialog proposes the region, OK puts an empty PivotTable on a new sheet.
        var dialog = InsertPivotDialog.Create(editor);
        Assert.Equal("Tabelle1!$A$1:$C$6", dialog.SourceBox.Text);
        dialog.Confirm();
        editor.ShowFieldList(true);
        await SettleAsync();
        Assert.Equal(2, editor.Session.Workbook.Sheets.Count);
        Assert.NotNull(editor.Session.ActivePivot);
        Assert.True(editor.FieldList.IsVisible);
        Assert.Contains(window.RibbonDefinition.VisibleTabs, tab => tab.Id == CalcEditor.PivotAnalyzeTab);

        // Ticking Region (text) puts it into Zeilen, Umsatz (numbers) into Werte.
        var boxes = editor.FieldList.FieldBoxes;
        Assert.Equal(["Region", "Produkt", "Umsatz"], boxes.Select(box => (string)box.Content!));
        await ClickAsync(window, Center(boxes[0], window));
        await ClickAsync(window, Center(editor.FieldList.FieldBoxes[2], window));
        Assert.Equal(["Region"], editor.FieldList.NamesIn(PivotAxis.Row));
        Assert.Equal(["Summe von Umsatz"], editor.FieldList.NamesIn(PivotAxis.None));
        var sheet = editor.Session.ActiveSheet;
        Assert.Equal("Nord", sheet["A4"].Text);
        Assert.Equal(150.0, sheet["B4"].Number);
        Assert.Equal(550.0, sheet["B7"].Number);

        // Produkt dragged from the list into Spalten.
        await DragAsync(window, Center(editor.FieldList.FieldBoxes[1], window), Center(editor.FieldList.AreaOf(PivotAxis.Column), window));
        Assert.Equal(["Produkt"], editor.FieldList.NamesIn(PivotAxis.Column));
        Assert.Equal("Spaltenbeschriftungen", sheet["B3"].Text);
        Assert.Equal("Äpfel", sheet["B4"].Text);

        // Undo takes the drag back.
        editor.Session.Undo();
        await SettleAsync();
        Assert.Empty(editor.FieldList.NamesIn(PivotAxis.Column));
    }

    [AvaloniaFact]
    public async Task Value_settings_filter_refresh_and_details_work_through_the_window()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await FillSalesAsync(window);
        var editor = EditorOf(window);
        var pivot = editor.Session.CreatePivotTable("Tabelle1!A1:C6", new Core.Editing.PivotDestination(NewSheet: true));
        pivot = editor.Session.AddPivotField(pivot, 0);
        pivot = editor.Session.AddPivotField(pivot, 2);
        await SettleAsync();
        var sheet = editor.Session.ActiveSheet;

        // Wertfeldeinstellungen: Mittelwert; the name follows.
        editor.Session.Select(new CellAddress(3, 1));
        await SettleAsync();
        Assert.True(window.Commands[CalcCommandIds.PivotValueSettings].IsEnabled);
        var settings = ValueFieldSettingsDialog.Create(editor, editor.Session.ActivePivot!, 0);
        settings.FunctionList.SelectedIndex = 2;
        Assert.Equal("Mittelwert von Umsatz", settings.NameBox.Text);
        settings.Confirm();
        await SettleAsync();
        Assert.Equal(75.0, sheet["B4"].Number);

        // Typing into the pivot is refused and leaves it unchanged.
        await EnterAsync(window, "5");
        await SettleAsync();
        Assert.Equal(75.0, sheet["B4"].Number);
        await PressAsync(window, PhysicalKey.Escape);

        // The arrow of the row labels: West unticked.
        editor.Session.Select(new CellAddress(2, 0));
        editor.Surface.OpenPivotFilter(editor.Session.ActivePivot!, 0);
        await SettleAsync();
        var menu = editor.Surface.PivotFilterMenu!;
        menu.Set("West", false);
        menu.Confirm();
        await SettleAsync();
        Assert.DoesNotContain(Enumerable.Range(3, 5), row => sheet.GetValue(row, 0).Text == "West");

        // The source changes, Alt+F5 refreshes.
        var source = editor.Session.Workbook.Sheets.First(candidate => candidate.Name == "Tabelle1");
        source.SetValue("C2", 300);
        editor.Session.Select(new CellAddress(3, 1));
        await PressAsync(window, PhysicalKey.F5, RawInputModifiers.Alt);
        Assert.Equal(175.0, sheet["B4"].Number);

        // A double click on a value shows its records on a new sheet.
        await ClickAsync(window, PointOf(window, new CellAddress(3, 1)), clicks: 2);
        await SettleAsync();
        Assert.Equal("Region", editor.Session.ActiveSheet["A1"].Text);
        Assert.Equal(3, editor.Session.Workbook.Sheets.Count);
    }

    private static Point Center(Control control, Window window) =>
        control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)!.Value;
}
