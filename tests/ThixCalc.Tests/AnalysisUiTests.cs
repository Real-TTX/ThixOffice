using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>Block D through the real window: Teilergebnis, Zielwertsuche, Formelüberwachung, Namen, Inhalte auswählen, Blattschutz.</summary>
public sealed class AnalysisUiTests
{
    private static async Task FillAsync(ThixOffice.Desktop.Shell.OfficeWindow window, string[][] rows)
    {
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                await TypeAsync(window, row[column]);
                await PressAsync(window, column < row.Length - 1 ? PhysicalKey.Tab : PhysicalKey.Enter);
            }
        }
    }

    [AvaloniaFact]
    public async Task Subtotals_and_goal_seek_through_their_dialogs()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await FillAsync(window, [["Region", "Umsatz"], ["Nord", "10"], ["Nord", "20"], ["Süd", "5"]]);
        var editor = EditorOf(window);
        editor.Session.Select(new CellAddress(1, 0));
        var subtotals = SubtotalDialog.Create(editor);
        Assert.Equal(["Region", "Umsatz"], (IEnumerable<string>)subtotals.GroupBox.ItemsSource!);
        Assert.True(subtotals.Columns[1].IsChecked);
        subtotals.Confirm();
        await SettleAsync();
        Assert.Equal("Nord Ergebnis", ValueAt(window, "A4").Text);
        Assert.Equal(35.0, ValueAt(window, "B7").Number);

        // Zielwertsuche: B9 = B8*2; which B8 gives 50?
        editor.Session.Select(new CellAddress(7, 1));
        await EnterAsync(window, "3");
        await EnterAsync(window, "=B8*2");
        var goal = GoalSeekDialog.Create(editor);
        goal.TargetBox.Text = "B9";
        goal.ValueBox.Text = "50";
        goal.ChangingBox.Text = "B8";
        goal.Confirm();
        Assert.Equal(25.0, ValueAt(window, "B8").Number, 3);
        Assert.Contains("Lösung gefunden", goal.Report, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Auditing_names_selection_and_protection_through_commands()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await FillAsync(window, [["Preis", "Menge"], ["2", "3"]]);
        var editor = EditorOf(window);
        editor.Session.Select(CellRange.Parse("A1:B2"));
        var names = CreateNamesDialog.Create(editor);
        Assert.True(names.Top.IsChecked);
        names.Confirm();
        editor.Session.Select(new CellAddress(2, 0));
        await EnterAsync(window, "=Preis*Menge");
        Assert.Equal(6.0, ValueAt(window, "A3").Number);

        editor.Session.Select(new CellAddress(2, 0));
        await window.Commands[CalcCommandIds.TracePrecedents].ExecuteAsync();
        await SettleAsync();
        Assert.Equal(2, editor.Session.TraceArrows.Count);
        await window.Commands[CalcCommandIds.RemoveArrows].ExecuteAsync();
        Assert.Empty(editor.Session.TraceArrows);

        // Inhalte auswählen › Formeln.
        editor.Session.Select(new CellAddress(0, 0));
        var special = SelectSpecialDialog.Create(editor);
        special.Options[2].IsChecked = true;
        special.Confirm();
        Assert.Equal(new CellAddress(2, 0), editor.Session.ActiveCell);

        // Blatt schützen: typing into a locked cell is refused, the cell stays.
        var protect = ProtectSheetDialog.Create(editor);
        protect.Confirm();
        await SettleAsync();
        Assert.True(window.Commands[CalcCommandIds.ProtectSheet].Checked);
        editor.Session.Select(new CellAddress(1, 0));
        await EnterAsync(window, "9");
        await SettleAsync();
        Assert.Equal(2.0, ValueAt(window, "A2").Number);
        await PressAsync(window, PhysicalKey.Escape);
        editor.Session.UnprotectSheet(null);
        Assert.False(editor.Session.IsSheetProtected);

        // A link to a cell of this sheet and to a name.
        editor.Session.Select(new CellAddress(5, 0));
        var link = HyperlinkDialog.Create(editor);
        link.Address.Text = "B2";
        link.Display.Text = "Zur Menge";
        link.Confirm();
        await SettleAsync();
        Assert.Equal("Tabelle1!B2", editor.Session.ActiveSheet.Hyperlinks.Single().Location);
        editor.Session.Select(new CellAddress(6, 0));
        var named = HyperlinkDialog.Create(editor);
        named.Address.Text = "Preis";
        named.Confirm();
        await SettleAsync();
        Assert.Contains(editor.Session.ActiveSheet.Hyperlinks, entry => entry.Location == "Preis");
    }
}
