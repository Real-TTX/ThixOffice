using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>How an Excel user finds and uses functions: typing, the suggestions, the argument tip, the dialogs, the search.</summary>
public sealed class FunctionDiscoveryTests
{
    private static async Task FillLookupTableAsync(Avalonia.Controls.Window window)
    {
        var editor = EditorOf((ThixOffice.Desktop.Shell.OfficeWindow)window);
        string[][] rows = [["10", "Zehn"], ["20", "Zwanzig"], ["30", "Dreißig"]];
        for (var row = 0; row < rows.Length; row++)
        {
            for (var column = 0; column < 2; column++)
            {
                editor.Session.Select(new CellAddress(row, column));
                editor.Session.EnterInput(rows[row][column]);
            }
        }

        editor.Session.Select(new CellAddress(5, 0));
        await SettleAsync();
    }

    [AvaloniaFact]
    public async Task Sverweis_typed_with_suggestions_tip_and_results()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await FillLookupTableAsync(window);
        var editor = EditorOf(window);

        await TypeAsync(window, "=SVER");
        Assert.Contains(editor.Editing.Suggestions, info => info.Name == "SVERWEIS");
        await TypeAsync(window, "WEIS(");
        Assert.NotNull(editor.Editing.ArgumentTip);
        Assert.Equal("SVERWEIS", editor.Editing.ArgumentTip!.Value.Function.Name);
        Assert.Equal(0, editor.Editing.ArgumentTip!.Value.Argument);
        await TypeAsync(window, "20;A1:B3;");
        Assert.Equal(2, editor.Editing.ArgumentTip!.Value.Argument);
        await TypeAsync(window, "2;FALSCH)");
        await PressAsync(window, PhysicalKey.Enter);
        Assert.Equal("Zwanzig", ValueAt(window, "A6").Text);

        await EnterAsync(window, "=SVERWEIS(25;A1:B3;2;WAHR)");
        Assert.Equal("Zwanzig", ValueAt(window, "A7").Text);
        await EnterAsync(window, "=SVERWEIS(25;A1:B3;2;FALSCH)");
        Assert.Equal(CellError.NotAvailable, ValueAt(window, "A8").Error);
        await EnterAsync(window, "=sverweis(30;a1:b3;2;0)");
        Assert.Equal("Dreißig", ValueAt(window, "A9").Text);
        editor.Session.Select(new CellAddress(8, 0));
        Assert.Equal("=SVERWEIS(30;A1:B3;2;0)", editor.Session.FormulaBarText);
    }

    [AvaloniaFact]
    public async Task English_names_and_separators_are_accepted_in_the_german_ui()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await FillLookupTableAsync(window);
        var editor = EditorOf(window);
        await EnterAsync(window, "=VLOOKUP(10;A1:B3;2;FALSE)");
        Assert.Equal("Zehn", ValueAt(window, "A6").Text);
        editor.Session.Select(new CellAddress(5, 0));
        Assert.Equal("=SVERWEIS(10;A1:B3;2;FALSCH)", editor.Session.FormulaBarText);

        // A comma between arguments (English habit) is read as the separator when it cannot be a decimal comma.
        editor.Session.Select(new CellAddress(6, 0));
        await EnterAsync(window, "=SVERWEIS(10,A1:B3,2,FALSCH)");
        Assert.Equal("Zehn", ValueAt(window, "A7").Text);
        await EnterAsync(window, "=RUNDEN(2,345;1)");
        Assert.Equal(2.3, ValueAt(window, "A8").Number);
    }

    [AvaloniaFact]
    public async Task The_search_box_finds_functions_by_german_and_english_name()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var found = window.Commands.Search("SVERWEIS");
        Assert.Equal(CalcDataCommands.FunctionId("VLOOKUP"), found[0].Id);
        Assert.Equal(CalcDataCommands.FunctionId("VLOOKUP"), window.Commands.Search("vlookup")[0].Id);

        // Ordinary commands stay first: "summe" offers AutoSumme before the SUMME functions.
        Assert.Equal(CalcCommandIds.AutoSum, window.Commands.Search("summe")[0].Id);
        Assert.Contains(window.Commands.Search("summewenn"), command => command.Id == CalcDataCommands.FunctionId("SUMIF"));
    }

    [AvaloniaFact]
    public async Task Every_function_is_in_a_menu_of_the_function_library()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var ribbon = window.RibbonDefinition;
        var missing = Core.Functions.FunctionHelp.All(Core.Globalization.CalcLocale.German)
            .Where(info => ribbon.Find(CalcDataCommands.FunctionId(info.Canonical)) is null)
            .Select(info => info.Name)
            .ToList();
        Assert.Empty(missing);
        var lookup = (MatDesktop.MatRibbonMenuButton)ribbon.Find(CalcDataCommands.CategoryId(Core.Functions.FunctionCategory.Lookup))!;
        Assert.Equal(CalcDataCommands.CategoryId(Core.Functions.FunctionCategory.Lookup) + ".insert", lookup.Menu[^1].Id);
        Assert.Contains(lookup.Menu, item => item.Id == CalcDataCommands.FunctionId("XLOOKUP"));
    }

    [AvaloniaFact]
    public async Task Function_arguments_show_live_values_and_put_the_call_into_the_cell()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await FillLookupTableAsync(window);
        var editor = EditorOf(window);
        var vlookup = Core.Functions.FunctionHelp.Find("SVERWEIS", editor.Session.Locale)!;
        var dialog = FunctionArgumentsDialog.Create(editor.Session, vlookup);
        Assert.Equal(4, dialog.ArgumentBoxes.Count);
        dialog.ArgumentBoxes[0].Text = "30";
        dialog.ArgumentBoxes[1].Text = "A1:B3";
        dialog.ArgumentBoxes[2].Text = "2";
        dialog.ArgumentBoxes[3].Text = "FALSCH";
        await SettleAsync();
        Assert.Equal("= 30", dialog.Previews[0]);
        Assert.StartsWith("= {10.\"Zehn\";20.\"Zwanzig\"", dialog.Previews[1], StringComparison.Ordinal);
        Assert.Equal("Formelergebnis = \"Dreißig\"", dialog.ResultText);
        dialog.Confirm();
        Assert.Equal("SVERWEIS(30;A1:B3;2;FALSCH)", dialog.CallText);

        // A repeating argument gets a new field once the last one is filled.
        var sum = FunctionArgumentsDialog.Create(editor.Session, Core.Functions.FunctionHelp.Find("SUMME", editor.Session.Locale)!);
        var before = sum.ArgumentBoxes.Count;
        sum.ArgumentBoxes[^1].Text = "1";
        await SettleAsync();
        Assert.Equal(before + 1, sum.ArgumentBoxes.Count);

        // An existing call is split back into its arguments.
        var split = Core.Formulas.FunctionCallText.Split("=SVERWEIS(\"a;b\";Tabelle2!A:B;2;WENN(A1>0;1;0))", editor.Session.Locale);
        Assert.Equal(["\"a;b\"", "Tabelle2!A:B", "2", "WENN(A1>0;1;0)"], split!.Value.Arguments);
    }
}
