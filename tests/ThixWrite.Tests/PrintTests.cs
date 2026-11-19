using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core;
using ThixWrite.Editor;
using ThixWrite.Pages;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Datei › Drucken through real keys and clicks, printing to the test's printers (never a real one).</summary>
public sealed class PrintTests
{
    /// <summary>Three pages: "Eins", page break, "Zwei", page break, "Drei"; the caret on page 3.</summary>
    private static async Task<OfficeWindow> ThreePagesAsync()
    {
        var window = await NewWindowAsync();
        await TypeAsync(window, "Eins");
        await PressAsync(window, PhysicalKey.Enter, CommandKey);
        await TypeAsync(window, "Zwei");
        await PressAsync(window, PhysicalKey.Enter, CommandKey);
        await TypeAsync(window, "Drei");
        Assert.Equal(3, EditorOf(window).Layout.PageCount);
        return window;
    }

    [AvaloniaFact]
    public async Task Ctrl_P_opens_Drucken_with_the_printers_the_preview_and_the_paper_of_the_document()
    {
        using var data = new TestData();
        var window = await ThreePagesAsync();

        await PressAsync(window, PhysicalKey.P, CommandKey);
        var page = Assert.IsType<PrintPage>(window.Backstage.CurrentPage);
        Assert.Equal(PrintPage.PageId, window.Backstage.CurrentPageId);
        Assert.Equal(["Büro – Laserdrucker (Standard)", "Empfang – Farbdrucker"], page.Printer.ItemsSource!.Cast<string>());
        Assert.Equal(0, page.Printer.SelectedIndex);
        Assert.True(page.PrintButton.IsEnabled);
        Assert.Equal("Hochformat", page.OrientationText);
        Assert.Equal("A4 (21 × 29,7 cm)", page.PaperText);

        // The preview starts at the caret's page and pages through all of them.
        Assert.Equal("Seite 3 von 3", page.PageLabel);
        Assert.False(page.NextButton.IsEnabled);
        page.PreviousButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        page.PreviousButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal("Seite 1 von 3", page.PageLabel);
        Assert.Equal(0, page.PreviewPage);
        Assert.False(page.PreviousButton.IsEnabled);
    }

    [AvaloniaFact]
    public async Task Typed_pages_and_copies_go_to_the_chosen_printer_and_the_backstage_closes()
    {
        using var data = new TestData();
        var window = await ThreePagesAsync();
        await PressAsync(window, PhysicalKey.P, CommandKey);
        var page = Assert.IsType<PrintPage>(window.Backstage.CurrentPage);

        page.CustomPages.Focus();
        window.KeyTextInput("3-2");
        await SettleAsync();
        Assert.Equal(PrintRange.Custom, page.Range); // typing pages chooses "Benutzerdefinierter Druck"
        Assert.Equal("Seite 1 von 2", page.PageLabel);
        Assert.Equal(2, page.PreviewPage);
        page.Copies.Value = 2;
        page.Printer.SelectedIndex = 1;

        page.PrintButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await WaitForAsync(() => data.Printers.Jobs.Count == 1);
        var job = data.Printers.Jobs.Single();
        Assert.Equal("Empfang – Farbdrucker", job.PrinterName);
        Assert.Equal(2, job.Copies);
        Assert.Equal([2, 1], job.Pages);
        Assert.Equal(3, job.Source.Count);
        await WaitForAsync(() => !window.Backstage.IsOpen);

        // What goes to the printer is the page as drawn: white paper with black text somewhere.
        var raster = job.Source.Render(0, 50, 0, 100);
        Assert.Equal((int)Math.Ceiling(595.3 / 72 * 50), raster.Width);
        Assert.Contains(raster.Pixels.Chunk(4), pixel => pixel[0] < 128);
    }

    [AvaloniaFact]
    public async Task The_current_page_wrong_pages_and_a_refusing_printer_are_handled()
    {
        using var data = new TestData();
        var window = await ThreePagesAsync();
        await PressAsync(window, PhysicalKey.P, CommandKey);
        var page = Assert.IsType<PrintPage>(window.Backstage.CurrentPage);

        page.Range = PrintRange.Current;
        await SettleAsync();
        Assert.Equal("Seite 1 von 1", page.PageLabel);
        Assert.Equal(2, page.PreviewPage);

        page.CustomPages.Text = "2, 9";
        await SettleAsync();
        Assert.False(await page.PrintAsync());
        Assert.Empty(data.Printers.Jobs);
        Assert.Contains(page.GetLogicalDescendantsOfType<MatDesktop.MatFormField>(), field => field.Error == "Das Dokument hat nur 3 Seiten.");
        Assert.True(window.Backstage.IsOpen);

        page.CustomPages.Text = "1";
        data.Printers.Failure = "Der Drucker ist offline.";
        Assert.False(await page.PrintAsync());
        Assert.Contains(page.GetLogicalDescendantsOfType<MatDesktop.MatFormField>(), field => field.Error == "Der Drucker ist offline.");

        data.Printers.Failure = null;
        Assert.True(await page.PrintAsync());
        Assert.Equal([0], data.Printers.Jobs.Single().Pages);
    }

    [AvaloniaFact]
    public async Task The_policy_DisablePrinting_turns_printing_off_and_says_so()
    {
        using var data = new TestData();
        AccessPolicy.UsePolicyReaderForTests(name => name == AccessPolicy.DisablePrinting);
        var window = await NewWindowAsync();
        await PressAsync(window, PhysicalKey.P, CommandKey);
        var page = Assert.IsType<PrintPage>(window.Backstage.CurrentPage);

        Assert.False(page.PrintButton.IsEnabled);
        Assert.Contains("DisablePrinting", page.Message.Text);
        Assert.Empty(PrintService.GetPrinters());
        var job = new PrintJob("x", "Büro – Laserdrucker", 1, [0], new PrintablePages([], null, "x"));
        Assert.Throws<BusinessException>(() => PrintService.Print(job));
        Assert.Empty(data.Printers.Jobs);
    }

    [AvaloniaFact]
    public async Task Without_printers_the_page_says_what_to_do()
    {
        using var data = new TestData();
        data.Printers.Printers.Clear();
        var window = await NewWindowAsync();
        await PressAsync(window, PhysicalKey.P, CommandKey);
        var page = Assert.IsType<PrintPage>(window.Backstage.CurrentPage);

        Assert.False(page.PrintButton.IsEnabled);
        Assert.Contains("kein Drucker", page.Message.Text);
    }

    [AvaloniaFact]
    public async Task On_Windows_the_GDI_printer_writes_the_pages_through_Microsoft_Print_to_PDF()
    {
        if (!OperatingSystem.IsWindows())
        {
            return;
        }

        using var data = new TestData();
        PrintService.System = null!; // the system's printers
        const string pdfPrinter = "Microsoft Print to PDF";
        if (!PrintService.GetPrinters().Any(printer => printer.Name == pdfPrinter))
        {
            return; // a machine without it (Windows Server core)
        }

        var window = await ThreePagesAsync();
        var editor = EditorOf(window);
        var pages = new PrintablePages([.. editor.Layout.GetDisplayLists()], WriteFonts.Measurer, "Drei Seiten");
        var output = Path.Combine(data.Folder, "gedruckt.pdf");
        await Task.Run(() => PrintService.Print(new PrintJob("Drei Seiten", pdfPrinter, 1, [0, 2], pages, output)));

        var bytes = await File.ReadAllBytesAsync(output);
        if (Environment.GetEnvironmentVariable(ThixWriteApp.Definition.ScreenshotsVariable) is { Length: > 0 } pictures)
        {
            File.Copy(output, Path.Combine(pictures, "thixwrite-printed-gdi.pdf"), overwrite: true); // to look at
        }

        Assert.Equal("%PDF", System.Text.Encoding.ASCII.GetString(bytes, 0, 4));
        Assert.Equal(2, System.Text.RegularExpressions.Regex.Matches(System.Text.Encoding.Latin1.GetString(bytes), @"/Type\s*/Page[^s]").Count);
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var round = 0; round < 200 && !condition(); round++)
        {
            await Task.Delay(10);
            Settle();
        }

        Assert.True(condition());
    }
}

internal static class LogicalExtensions
{
    public static IEnumerable<T> GetLogicalDescendantsOfType<T>(this Avalonia.LogicalTree.ILogical root) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<T>();
}
