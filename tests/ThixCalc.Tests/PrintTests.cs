using Avalonia.Headless.XUnit;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using ThixCalc.Pages;
using ThixOffice.Core.Models;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>Seitenlayout, Datei › Drucken and Exportieren through the window.</summary>
public sealed class PrintTests
{
    private static void Fill(CalcEditor editor, int rows)
    {
        var sheet = editor.Session.ActiveSheet;
        for (var row = 0; row < rows; row++)
        {
            sheet.SetValue(row, 0, CellValue.FromText("Posten " + row));
            sheet.SetValue(row, 1, row * 1.5);
        }

        editor.Session.Recalculate();
    }

    [AvaloniaFact]
    public async Task Strg_P_shows_the_pages_and_prints_to_the_chosen_printer()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        Fill(EditorOf(window), 140);
        await window.Commands.ExecuteAsync(CalcCommandIds.Print);
        await SettleAsync();
        var page = Assert.IsType<CalcPrintPage>(window.Backstage.CurrentPage);
        Assert.Equal("Seite 1 von 3", page.PageLabel);
        Assert.Equal("Hochformat", page.OrientationText);
        page.Page(1);
        Assert.Equal("Seite 2 von 3", page.PageLabel);
        Assert.True(await page.PrintAsync());
        var job = Assert.Single(data.Printers.Jobs);
        Assert.Equal(3, job.Pages.Count);
    }

    [AvaloniaFact]
    public async Task Querformat_and_fit_to_one_page_change_the_pages()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        Fill(editor, 140);
        await window.Commands.ExecuteAsync(CalcCommandIds.Landscape);
        await window.Commands.ExecuteAsync($"{CalcCommandIds.FitToPage}.page");
        Assert.True(editor.Session.ActiveSheet.PageSetup.Landscape);
        var lists = CalcPages.DisplayLists(editor, 0, "Mappe", out _);
        Assert.Single(lists);
        Assert.True(lists[0].Width > lists[0].Height);
        Assert.True(window.Commands[$"{CalcCommandIds.FitToPage}.page"].Checked);
    }

    [AvaloniaFact]
    public async Task Page_setup_dialog_sets_titles_and_a_footer()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        Fill(editor, 140);
        var dialog = PageSetupDialog.Create(editor);
        dialog.Titles.Text = "$1:$1";
        dialog.FooterChoice.SelectedIndex = 2;
        dialog.Confirm();
        Assert.Equal("&CSeite &P von &N", editor.Session.ActiveSheet.PageSetup.Footer);
        var lists = CalcPages.DisplayLists(editor, 0, "Mappe", out _);
        Assert.Contains(lists[1].Items.OfType<ThixOffice.Text.Drawing.GlyphRunItem>(), run => run.Text == "Posten 0");
        Assert.Contains(lists[1].Items.OfType<ThixOffice.Text.Drawing.GlyphRunItem>(), run => run.Text == "Seite 2 von 3");
    }

    [AvaloniaFact]
    public async Task Als_PDF_exportieren_writes_the_sheet()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Budget.pdf");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        Fill(EditorOf(window), 20);
        await window.Commands.ExecuteAsync(CalcCommandIds.ExportPdf);
        await SettleAsync();
        var page = Assert.IsType<CalcExportPage>(window.Backstage.CurrentPage);
        Assert.True(await page.ExportPdfAsync());
        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(File.ReadAllBytes(target), 0, 4));
    }
}
