using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using ThixOffice.Desktop.Shell;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>
/// The completions of filtering and pasting in the real ThixCalc: Inhalte einfügen, Reihe, Strg+Plus / Strg+Minus, the custom
/// filter and Top 10 from the filter arrow's menu, Alle suchen, and Rückgängig inside a cell that is being edited.
/// </summary>
public sealed class EditUiTests
{
    private static async Task<OfficeWindow> ColumnAsync(params string[] values)
    {
        var window = await NewWindowAsync();
        foreach (var value in values)
        {
            await EnterAsync(window, value);
        }

        EditorOf(window).Session.Select(new CellAddress(0, 0));
        await SettleAsync();
        return window;
    }

    [AvaloniaFact]
    public async Task Inhalte_einfuegen_adds_the_copied_numbers()
    {
        using var data = new TestData();
        var window = await ColumnAsync("2", "3");
        var editor = EditorOf(window);
        editor.Session.Select(new CellAddress(0, 2));
        await EnterAsync(window, "10");
        editor.Session.Select(new CellRange(0, 0, 1, 0));
        await PressAsync(window, PhysicalKey.C, CommandKey);
        editor.Session.Select(new CellAddress(0, 2));
        Assert.True(window.Commands[CalcCommandIds.PasteSpecial]!.IsEnabled);

        var dialog = PasteSpecialDialog.Create(editor);
        dialog.Choose(PasteMode.Values, PasteOperation.Add);
        dialog.Confirm();
        await SettleAsync();

        Assert.Equal(12, ValueAt(window, "C1").Number);
        Assert.Equal(3, ValueAt(window, "C2").Number);
    }

    [AvaloniaFact]
    public async Task Strg_Plus_and_Strg_Minus_insert_and_delete_whole_rows()
    {
        using var data = new TestData();
        var window = await ColumnAsync("eins", "zwei", "drei");
        var editor = EditorOf(window);
        editor.Session.SelectRows(1, 1);

        await PressAsync(window, PhysicalKey.Equal, CommandKey);
        Assert.Equal("zwei", ValueAt(window, "A3").Text);
        Assert.True(ValueAt(window, "A2").IsBlank);

        await PressAsync(window, PhysicalKey.Minus, CommandKey);
        Assert.Equal("zwei", ValueAt(window, "A2").Text);
    }

    [AvaloniaFact]
    public async Task The_series_dialog_fills_with_an_increment()
    {
        using var data = new TestData();
        var window = await ColumnAsync("1");
        var editor = EditorOf(window);
        editor.Session.Select(new CellRange(0, 0, 3, 0));

        var dialog = SeriesDialog.Create(editor);
        dialog.StepBox.Text = "2,5";
        dialog.Confirm();
        await SettleAsync();

        Assert.Equal(8.5, ValueAt(window, "A4").Number);
    }

    [AvaloniaFact]
    public async Task The_custom_filter_and_Top_10_hide_rows()
    {
        using var data = new TestData();
        var window = await ColumnAsync("Wert", "5", "50", "500", "40");
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(CalcCommandIds.DataFilter);
        editor.Surface.OpenFilter(0);
        Assert.Equal("Zahlenfilter", ((ThixOffice.Desktop.Controls.IconLabel)editor.Surface.FilterMenu!.ConditionsButton.Content!).Text);
        editor.Surface.FilterMenu.Close();

        var custom = CustomFilterDialog.Create(editor, 0, "Wert", 2);
        custom.FirstValue.Text = "40";
        custom.Confirm();
        await SettleAsync();
        Assert.Equal([true, false, false, true], Enumerable.Range(1, 4).Select(editor.Session.ActiveSheet.Rows.IsHidden));

        var top = TopTenDialog.Create(editor, 0);
        top.Count.Value = 1;
        top.Confirm();
        await SettleAsync();
        Assert.Equal([true, true, false, true], Enumerable.Range(1, 4).Select(editor.Session.ActiveSheet.Rows.IsHidden));
    }

    [AvaloniaFact]
    public async Task Alle_suchen_lists_every_hit_and_goes_there()
    {
        using var data = new TestData();
        var window = await ColumnAsync("Apfel", "Birne", "Apfelmus");
        var editor = EditorOf(window);
        editor.FindBar.Open(replace: false);
        editor.FindBar.SearchBox.Text = "apfel";
        await SettleAsync();

        editor.FindBar.ShowAll();
        await SettleAsync();

        var hits = editor.FindBar.Results.Items.OfType<CalcFindBar.FindHit>().ToList();
        Assert.Equal(2, hits.Count);
        editor.FindBar.Results.SelectedItem = hits[1];
        Assert.Equal(new CellAddress(2, 0), editor.Session.ActiveCell);
        Assert.Equal("2 Zellen gefunden", editor.FindBar.CountText);
    }

    [AvaloniaFact]
    public async Task Undo_inside_the_cell_takes_back_what_was_typed()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);

        await TypeAsync(window, "Hallo");
        await TypeAsync(window, " Welt");
        Assert.Equal("Hallo Welt", editor.Editing.CellBox.Text);
        await PressAsync(window, PhysicalKey.Z, CommandKey);

        Assert.True(editor.Editing.IsEditing);
        Assert.Equal("Hallo", editor.Editing.CellBox.Text);
        await PressAsync(window, PhysicalKey.Enter);
        Assert.Equal("Hallo", ValueAt(window, "A1").Text);
    }
}
