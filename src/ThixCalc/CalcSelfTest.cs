using Avalonia.Input;
using Avalonia.Threading;
using SkiaSharp;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Text.Skia;

namespace ThixCalc;

/// <summary>
/// ThixCalc's part of <c>--self-test</c> (the trimmed, published program): a new workbook in a real window, values and German
/// formulas typed into the grid as text input (SUMME, SVERWEIS, SUMMEWENN), a format through the command, a sort, saved as
/// xlsx, opened again and compared (Open XML SDK after trimming), a frame of the grid drawn with Skia (HarfBuzz shaping, the
/// grid picture), and the sample workbook written and read.
/// </summary>
internal static class CalcSelfTest
{
    public static async Task<string> RunAsync(App app)
    {
        var window = app.NewDocumentWindow();
        await Settle();
        var editor = window.Editor as CalcEditor ?? throw new InvalidOperationException("the window has no CalcEditor");
        editor.FocusDocument();
        await Settle();

        // Typed as a user types: text input into the grid, Enter to commit.
        string[][] rows =
        [
            ["Obst", "Menge", "Preis"],
            ["Äpfel", "12", "0,45"],
            ["Birnen", "7", "0,60"],
            ["Kiwis", "20", "0,25"],
        ];
        foreach (var row in rows)
        {
            for (var column = 0; column < row.Length; column++)
            {
                editor.Session.Select(new CellAddress(Array.IndexOf(rows, row), column));
                Type(editor, row[column]);
                Press(editor, Key.Enter);
            }
        }

        editor.Session.Select(new CellAddress(4, 1));
        Type(editor, "=SUMME(B2:B4)");
        Press(editor, Key.Enter);
        editor.Session.Select(new CellAddress(5, 1));
        Type(editor, "=SVERWEIS(\"Birnen\";A2:C4;3;FALSCH)");
        Press(editor, Key.Enter);
        editor.Session.Select(new CellAddress(6, 1));
        Type(editor, "=SUMMEWENN(B2:B4;\">10\";C2:C4)");
        Press(editor, Key.Enter);
        await Settle();

        var sheet = editor.Session.ActiveSheet;
        Ensure(sheet.GetValue(1, 0).Text == "Äpfel", "typed text not in the cell");
        Ensure(sheet.GetValue(4, 1).Number == 39, $"SUMME is {sheet.GetValue(4, 1)}");
        Ensure(Math.Abs(sheet.GetValue(5, 1).Number - 0.6) < 1e-9, $"SVERWEIS is {sheet.GetValue(5, 1)}");
        Ensure(Math.Abs(sheet.GetValue(6, 1).Number - 0.7) < 1e-9, $"SUMMEWENN is {sheet.GetValue(6, 1)}");

        editor.Session.Select(new CellRange(0, 0, 0, 2));
        await window.Commands[CalcCommandIds.Bold].ExecuteAsync();
        Ensure(sheet.GetStyle(0, 1).Font.Bold, "Fett did not apply");
        editor.Session.Select(new CellRange(0, 0, 3, 2));
        editor.Session.Sort(new CellRange(0, 0, 3, 2), [new SortKey(1, Descending: true)], new SortOptions(HasHeader: true));
        Ensure(sheet.GetValue(1, 0).Text == "Kiwis", "the sort did not apply");

        var folder = Path.Combine(AppPaths.DataDirectory, "Dokumente");
        Directory.CreateDirectory(folder);
        var path = DocumentService.SaveAs(window.Document, Path.Combine(folder, "Selbsttest.xlsx"));
        var reopened = DocumentService.Open(path, app.CreateDocument) as CalcDocument ?? throw new InvalidOperationException("not a CalcDocument");
        var read = reopened.Workbook.Sheets[0];
        Ensure(read.GetValue(1, 0).Text == "Kiwis" && read.GetStyle(0, 1).Font.Bold, "the xlsx read back differs");
        Ensure(read.GetFormula(5, 1) is not null && Math.Abs(read.GetValue(5, 1).Number - 0.6) < 1e-9, "the formula is lost in the xlsx");

        // A frame of the grid, drawn as the screen draws it.
        var viewport = editor.LayoutOf(sheet).Viewport(800, 400, 1.0, 0, 0);
        var list = GridRenderer.Render(viewport, editor.Metrics, GridPalette.RenderOptions([]));
        using var bitmap = new SKBitmap(800, 400);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(GridPalette.SheetSkia);
            SkiaPageRenderer.Draw(canvas, list, CalcFonts.Skia);
        }

        var inked = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                inked += bitmap.GetPixel(x, y).Red < 128 ? 1 : 0;
            }
        }

        Ensure(inked > 20, "the grid shows no text");

        // The sample workbook (formats, conditional formats, frozen panes, two sheets) through xlsx.
        var sample = (CalcDocument)app.CreateDocument("Beispiel");
        sample.Load(SampleWorkbook.Create());
        var samplePath = DocumentService.SaveAs(sample, Path.Combine(folder, "Budget.xlsx"));
        var sampleRead = DocumentService.Open(samplePath, app.CreateDocument) as CalcDocument ?? throw new InvalidOperationException("not a CalcDocument");
        Ensure(sampleRead.Workbook.Sheets.Count == 2 && sampleRead.Workbook.Sheets[0].ConditionalFormats.Count == 2, "the sample workbook lost content");

        // The sheet's pages as PDF and – where the machine has it – through the system's printer into a PDF.
        var lists = Pages.CalcPages.DisplayLists(editor, 0, "Selbsttest", out _);
        using var pdf = new MemoryStream();
        PdfExport.Export(lists, pdf, new PdfMetadata("Selbsttest"), CalcFonts.Skia);
        var bytes = pdf.ToArray();
        Ensure(bytes.Length > 1000 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F', "the PDF is not a PDF");
        var printed = await PrintAsync(lists, folder);

        window.Close();
        await Settle();
        return $"{editor.Metrics.MaxDigitWidthPixels} px digit, {CalcFonts.Families.Count} font families, xlsx {new FileInfo(path).Length} bytes, sample {new FileInfo(samplePath).Length} bytes, PDF {bytes.Length} bytes, {printed}, frame {editor.Surface.LastRenderTime.TotalMilliseconds:0.0} ms";
    }

    /// <summary>Printing through GDI into "Microsoft Print to PDF" when the machine has it (the P/Invoke after trimming).</summary>
    private static async Task<string> PrintAsync(IReadOnlyList<ThixOffice.Text.Drawing.PageDisplayList> lists, string folder)
    {
        var printers = PrintService.GetPrinters();
        const string pdfPrinter = "Microsoft Print to PDF";
        if (!OperatingSystem.IsWindows() || !printers.Any(printer => printer.Name == pdfPrinter))
        {
            return $"{printers.Count} printer(s), none printed to";
        }

        var output = Path.Combine(folder, "Gedruckt.pdf");
        var pages = new ThixOffice.Desktop.Rendering.PrintablePages(lists, CalcFonts.Skia, "Selbsttest", GridPalette.SheetSkia);
        await Task.Run(() => PrintService.Print(new ThixOffice.Core.Models.PrintJob("Selbsttest", pdfPrinter, 1, [0], pages, output)));
        var printed = File.ReadAllBytes(output);
        Ensure(printed.Length > 1000 && printed[0] == '%' && printed[1] == 'P', "the printer wrote no PDF");
        return $"printed {printed.Length} bytes";
    }

    private static void Type(CalcEditor editor, string text) =>
        editor.Surface.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = text, Source = editor.Surface });

    private static void Press(CalcEditor editor, Key key) =>
        editor.Surface.RaiseEvent(new KeyEventArgs { RoutedEvent = InputElement.KeyDownEvent, Key = key, Source = editor.Surface });

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task Settle()
    {
        for (var round = 0; round < 5; round++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(40);
        }
    }
}
