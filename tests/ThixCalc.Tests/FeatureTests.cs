using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Input.Platform;
using MatDesktop;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using ThixOffice.Core.Business;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>Sheet tabs, the ribbon's state, find/replace, the clipboard, dialogs and files through the real window.</summary>
public sealed class FeatureTests
{
    [AvaloniaFact]
    public async Task Sheet_tabs_add_activate_rename_and_reorder()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        editor.Tabs.AddButton.Command?.Execute(null);
        editor.Tabs.AddButton.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();
        Assert.Equal(2, editor.Session.Workbook.Sheets.Count);
        Assert.Equal(1, editor.Session.Workbook.ActiveSheetIndex);
        Assert.Equal(2, editor.Tabs.Tabs.Count);

        // Strg+Bild auf goes back; the tab's double click renames in place.
        await PressAsync(window, PhysicalKey.PageUp, CommandKey);
        Assert.Equal(0, editor.Session.Workbook.ActiveSheetIndex);
        editor.Tabs.RenameActive();
        await SettleAsync();
        var box = editor.Tabs.Tabs.OfType<TextBox>().Single();
        box.Text = "Umsatz";
        box.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = Key.Enter, Source = box });
        await SettleAsync();
        Assert.Equal("Umsatz", editor.Session.Workbook.Sheets[0].Name);

        editor.Session.MoveSheet(0, 1);
        await SettleAsync();
        Assert.Equal("Umsatz", editor.Session.Workbook.Sheets[1].Name);
        Assert.Equal("Umsatz", ((TextBlock)((Panel)((Border)editor.Tabs.Tabs[1]).Child!).Children[0]).Text);
    }

    [AvaloniaFact]
    public async Task The_ribbon_follows_the_active_cell()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "fett");
        await PressAsync(window, PhysicalKey.Enter);
        await PressAsync(window, PhysicalKey.ArrowUp);
        await PressAsync(window, PhysicalKey.F, CommandKey | RawInputModifiers.Shift);
        await SettleAsync();
        Assert.True(EditorOf(window).Session.ActiveStyle.Font.Bold);
        Assert.True(window.RibbonDefinition.FindAll(CalcCommandIds.Bold).OfType<MatRibbonToggle>().All(toggle => toggle.IsChecked));
        await PressAsync(window, PhysicalKey.ArrowDown);
        await SettleAsync();
        Assert.DoesNotContain(window.RibbonDefinition.FindAll(CalcCommandIds.Bold).OfType<MatRibbonToggle>(), toggle => toggle.IsChecked);

        // While a cell is edited, the commands that change cells are off.
        await TypeAsync(window, "x");
        await SettleAsync();
        Assert.False(window.Commands[CalcCommandIds.Bold].IsEnabled);
        Assert.Equal("Standard", window.Commands.Find(CalcCommandIds.NumberFormat) is ThixOffice.Core.Commands.OfficeValueCommand number ? number.Value : null);
    }

    [AvaloniaFact]
    public async Task Find_and_replace_through_the_bar()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "Apfel");
        await EnterAsync(window, "Birne");
        await EnterAsync(window, "Apfelkuchen");
        await PressAsync(window, PhysicalKey.F, CommandKey);
        var editor = EditorOf(window);
        Assert.True(editor.FindBar.IsVisible);
        editor.FindBar.SearchBox.Text = "apfel";
        await SettleAsync();
        Assert.Equal("Treffer 1 von 2", editor.FindBar.CountText);
        Assert.True(editor.FindBar.FindNext());
        Assert.Equal(new CellAddress(2, 0), editor.Session.ActiveCell);
        editor.FindBar.ReplacementBox.Text = "Kirsch";
        Assert.Equal(2, editor.FindBar.ReplaceAll());
        Assert.Equal("Kirschkuchen", ValueAt(window, "A3").Text);
    }

    [AvaloniaFact]
    public async Task Copy_and_paste_keep_formulas_and_give_other_programs_text_and_html()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "2");
        await EnterAsync(window, "=A1*3");
        var editor = EditorOf(window);
        editor.Session.Select(new CellRange(0, 0, 1, 0));
        await PressAsync(window, PhysicalKey.C, CommandKey);
        Assert.NotNull(editor.Surface.CopiedRange);
        var text = await window.Clipboard!.TryGetTextAsync();
        Assert.Equal("2\r\n6", text?.TrimEnd());
        editor.Session.Select(new CellAddress(0, 2));
        await PressAsync(window, PhysicalKey.V, CommandKey);
        await SettleAsync();
        editor.Session.Select(new CellAddress(1, 2));
        Assert.Equal("=C1*3", editor.Session.FormulaBarText);

        // Text of another program: tab-separated, each field read like typed input.
        await window.Clipboard!.SetTextAsync("Name\tBetrag\r\nMiete\t1.150,50");
        editor.Session.Select(new CellAddress(5, 0));
        editor.Surface.CopiedRange = null;
        await editor.Clipboard.PasteAsync(textOnly: true);
        Assert.Equal(1150.5, ValueAt(window, "B7").Number);
    }

    [AvaloniaFact]
    public void Html_tables_of_other_programs_are_read()
    {
        var rows = Core.Formats.Html.HtmlTables.Import("<html><body><table><tr><th>A</th><th colspan=2>B</th></tr><tr><td>1</td><td>x&amp;y</td><td>3<br>4</td></tr></table>");
        Assert.NotNull(rows);
        Assert.Equal(["A", "B", ""], rows[0]);
        Assert.Equal(["1", "x&y", "3\n4"], rows[1]);
        var wrapped = Core.Formats.Html.HtmlTables.WrapForClipboard("<table></table>");
        Assert.StartsWith("Version:0.9", wrapped, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Format_cells_dialog_applies_number_format_wrap_and_fill()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "1234,5");
        var editor = EditorOf(window);
        editor.Session.Select(new CellAddress(0, 0));
        var dialog = new FormatCellsDialog(editor);
        dialog.Category.SelectedIndex = 1; // Zahl
        dialog.Decimals.Value = 1;
        dialog.Thousands.IsChecked = true;
        Assert.Equal("1.234,5", dialog.SampleText);
        dialog.WrapText.IsChecked = true;
        dialog.FillChoice.Value = "#FFFF00";
        dialog.BorderBottom.IsChecked = true;
        dialog.Confirm();
        var style = editor.Session.ActiveStyle;
        Assert.Equal("#,##0.0", style.NumberFormat);
        Assert.True(style.Alignment.WrapText);
        Assert.Equal(FillPattern.Solid, style.Fill.Pattern);
        Assert.True(style.Borders.Bottom.IsVisible);
        Assert.True(editor.Session.CanUndo);
    }

    [AvaloniaFact]
    public async Task Sort_dialog_sorts_by_two_keys_with_a_header()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        string[][] rows = [["Name", "Gruppe", "Wert"], ["b", "x", "3"], ["a", "y", "1"], ["c", "x", "2"]];
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < 3; column++)
            {
                editor.Session.Select(new CellAddress(row, column));
                editor.Session.EnterInput(rows[row][column]);
            }
        }

        editor.Session.Select(new CellAddress(1, 0));
        var dialog = SortDialog.Create(editor);
        Assert.True(dialog.HasHeader.IsChecked);
        dialog.Levels[0].Column.SelectedIndex = 1;
        dialog.Confirm();
        Assert.Equal("b", ValueAt(window, "A2").Text);
        Assert.Equal("c", ValueAt(window, "A3").Text);
        Assert.Equal("a", ValueAt(window, "A4").Text);
    }

    [AvaloniaFact]
    public async Task Insert_function_dialog_finds_by_description()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var dialog = InsertFunctionDialog.Create(EditorOf(window).Session.Locale);
        dialog.SearchBox.Text = "verweis";
        await SettleAsync();
        Assert.Contains("SVERWEIS", (IEnumerable<string>)dialog.Functions.ItemsSource!);
        dialog.Confirm();
        Assert.NotNull(dialog.Chosen);
    }

    [AvaloniaFact]
    public async Task A_workbook_is_saved_as_xlsx_and_opened_again()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "Umsatz");
        await EnterAsync(window, "=SUMME(1;2)");
        var path = Path.Combine(data.DocumentsFolder, "Mappe.xlsx");
        Assert.True(await window.SaveToAsync(path));
        var opened = DocumentService.Open(path, TestData.App.CreateDocument) as CalcDocument;
        Assert.NotNull(opened);
        Assert.Equal(3, opened.Workbook.Sheets[0].GetValue(1, 0).Number);
        Assert.False(window.Document.IsDirty);
    }

    [AvaloniaFact]
    public async Task The_status_bar_shows_the_chosen_aggregates()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await EnterAsync(window, "2");
        await EnterAsync(window, "4");
        var editor = EditorOf(window);
        editor.Session.Select(new CellRange(0, 0, 1, 0));
        await SettleAsync();
        Assert.Equal("Mittelwert: 3", window.StatusBar.TextOf(CalcEditor.AverageItem));
        Assert.Equal("Anzahl: 2", window.StatusBar.TextOf(CalcEditor.CountItem));
        Assert.Null(window.StatusBar.TextOf(CalcEditor.MaximumItem));
        editor.StatusAggregates.Add(CalcEditor.MaximumItem);
        editor.UpdateStatus(window.StatusBar);
        Assert.Equal("Maximum: 4", window.StatusBar.TextOf(CalcEditor.MaximumItem));
    }
}
