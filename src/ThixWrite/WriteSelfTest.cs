using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using SkiaSharp;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Text.Skia;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite;

/// <summary>
/// ThixWrite's part of <c>--self-test</c> (the trimmed, published program): a new document in a real window, text typed into the
/// editor as text input, made bold through the command, saved as docx, opened again and compared (Open XML SDK after
/// trimming), its first page drawn with Skia (HarfBuzz shaping, the page picture) and exported to PDF.
/// </summary>
internal static class WriteSelfTest
{
    private const string Text = "Grüße aus ThixWrite – ÄÖÜ ß € 😀";

    public static async Task<string> RunAsync(App app)
    {
        var window = app.NewDocumentWindow();
        await Settle();
        var editor = window.Editor as WriteEditor ?? throw new InvalidOperationException("the window has no WriteEditor");
        editor.FocusDocument();
        await Settle();
        editor.Surface.RaiseEvent(new TextInputEventArgs { RoutedEvent = InputElement.TextInputEvent, Text = Text, Source = editor.Surface });
        await Settle();
        Ensure(editor.Document.Content.GetPlainText().TrimEnd() == Text, $"typed text not in the document: '{editor.Document.Content.GetPlainText()}'");

        await window.Commands[WriteCommandIds.SelectAll].ExecuteAsync();
        await window.Commands[WriteCommandIds.Bold].ExecuteAsync();
        await Settle();
        Ensure(editor.Session.GetFormatting().Bold.Is(true), "Fett did not apply");
        Ensure(editor.Layout.PageCount == 1, "no page laid out");
        Ensure(editor.Surface.DrawnPages.Count > 0, "no page drawn");

        var folder = Path.Combine(AppPaths.DataDirectory, "Dokumente");
        Directory.CreateDirectory(folder);
        var path = DocumentService.SaveAs(window.Document, Path.Combine(folder, "Selbsttest.docx"));
        var reopened = DocumentService.Open(path, app.CreateDocument) as WordDocument ?? throw new InvalidOperationException("not a WordDocument");
        Ensure(reopened.Content.ContentEquals(editor.Document.Content), "the docx read back differs from the document written");
        Ensure(reopened.Content.Blocks[0] is Paragraph { Inlines: [TextRun { Properties.Bold: true }, ..] }, "bold is lost in the docx");

        // A Word document with a table: read as a table, a cell edited, written back as a table.
        var table = Path.Combine(folder, "Tabelle.docx");
        WriteDocxWithTable(table);
        var withTable = DocumentService.Open(table, app.CreateDocument) as WordDocument ?? throw new InvalidOperationException("not a WordDocument");
        var span = TableSpan.At(withTable.Content, 1) ?? throw new InvalidOperationException("the table was not read as a table");
        Ensure(span.Table.RowCount == 2 && span.Table.ColumnCount == 2, "the table has the wrong size");
        withTable.Session.SetCaret(new DocumentPosition(span.End, 2));
        withTable.Session.InsertText("!");
        withTable.Session.InsertTableRows(below: true);
        DocumentService.Save(withTable);
        using (var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Open(table, false))
        {
            var written = package.MainDocumentPart?.Document?.Body?.Elements<DocumentFormat.OpenXml.Wordprocessing.Table>().SingleOrDefault();
            Ensure(written?.Elements<DocumentFormat.OpenXml.Wordprocessing.TableRow>().Count() == 3 && written.InnerText.Contains("B2!", StringComparison.Ordinal), "the table was not saved with its change");
        }

        using var bitmap = new SKBitmap((int)editor.Layout.Pages[0].Width, (int)editor.Layout.Pages[0].Height);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(DocumentPalette.PaperSkia);
            SkiaPageRenderer.Draw(canvas, editor.Layout.GetDisplayList(0), WriteFonts.Measurer);
        }

        var inked = 0;
        for (var y = 0; y < bitmap.Height; y += 2)
        {
            for (var x = 0; x < bitmap.Width; x += 2)
            {
                inked += bitmap.GetPixel(x, y).Red < 128 ? 1 : 0;
            }
        }

        Ensure(inked > 20, "the page shows no text");
        using var pdf = new MemoryStream();
        PdfExport.Export(editor.Layout.GetDisplayLists(), pdf, new PdfMetadata("Selbsttest"), WriteFonts.Measurer);
        var bytes = pdf.ToArray();
        Ensure(bytes.Length > 1000 && bytes[0] == '%' && bytes[1] == 'P' && bytes[2] == 'D' && bytes[3] == 'F', "the PDF is not a PDF");
        var picture = CheckPicture(app, folder);
        var printed = await PrintAsync(editor, folder);

        window.Close();
        await Settle();
        return $"{WriteFonts.Default.Body}, {WriteFonts.Families.Count} font families, docx {new FileInfo(path).Length} bytes, PDF {bytes.Length} bytes, {picture}, {printed}";
    }

    /// <summary>A picture inserted, saved in a docx, read back and drawn by Skia (the trimmed build decodes it).</summary>
    private static string CheckPicture(App app, string folder)
    {
        var document = (WordDocument)app.CreateDocument("Bild");
        var image = ImageData.FromBytes(SampleDocument.ChartPng()) ?? throw new InvalidOperationException("the chart is no PNG");
        document.Session.InsertImage(image);
        document.Session.InsertPageNumber(top: false, ParagraphAlignment.Center);
        var path = DocumentService.SaveAs(document, Path.Combine(folder, "Bild.docx"));
        var reopened = DocumentService.Open(path, app.CreateDocument) as WordDocument ?? throw new InvalidOperationException("not a WordDocument");
        Ensure(reopened.Content.ParagraphAt(0)?.Inlines.FirstOrDefault() is ImageInline { Image.PixelWidth: 600 }, "the picture is lost in the docx");
        Ensure(reopened.Content.FinalSection.Footers.Default?.HasPageFields == true, "the page number is lost in the docx");
        Ensure(SkiaPageRenderer.DecodeImage(image.Key, image.Bytes) is { Width: 600 }, "Skia cannot decode the picture");
        Ensure(Core.Formats.Html.HtmlImport.Import("<p><b>HTML</b></p>") is { } html && html.Blocks.Count > 0, "HTML is not read");
        return $"picture and footer {new FileInfo(path).Length} bytes";
    }

    /// <summary>
    /// Printing through the system (GDI on Windows) into a file: "Microsoft Print to PDF" when the machine has it – the
    /// P/Invoke of the printer after trimming, the bands drawn by Skia, the job finished by the spooler.
    /// </summary>
    private static async Task<string> PrintAsync(WriteEditor editor, string folder)
    {
        var printers = PrintService.GetPrinters();
        const string pdfPrinter = "Microsoft Print to PDF";
        if (!OperatingSystem.IsWindows() || !printers.Any(printer => printer.Name == pdfPrinter))
        {
            return $"{printers.Count} printer(s), none printed to";
        }

        var output = Path.Combine(folder, "Gedruckt.pdf");
        var pages = new PrintablePages([.. editor.Layout.GetDisplayLists()], WriteFonts.Measurer, "Selbsttest");
        await Task.Run(() => PrintService.Print(new ThixOffice.Core.Models.PrintJob("Selbsttest", pdfPrinter, 1, [0], pages, output)));
        var printed = File.ReadAllBytes(output);
        Ensure(printed.Length > 1000 && printed[0] == '%' && printed[1] == 'P', "the printer wrote no PDF");
        return $"{printers.Count} printer(s), printed {printed.Length} bytes";
    }

    /// <summary>A docx as Word writes it, with a paragraph and a 2 × 2 table (Open XML SDK).</summary>
    private static void WriteDocxWithTable(string path)
    {
        using var package = DocumentFormat.OpenXml.Packaging.WordprocessingDocument.Create(path, DocumentFormat.OpenXml.WordprocessingDocumentType.Document);
        var main = package.AddMainDocumentPart();
        var body = new DocumentFormat.OpenXml.Wordprocessing.Body(
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text("Vor der Tabelle"))),
            new DocumentFormat.OpenXml.Wordprocessing.Table(
                new DocumentFormat.OpenXml.Wordprocessing.TableRow(Cell("A1"), Cell("B1")),
                new DocumentFormat.OpenXml.Wordprocessing.TableRow(Cell("A2"), Cell("B2"))),
            new DocumentFormat.OpenXml.Wordprocessing.Paragraph());
        main.Document = new DocumentFormat.OpenXml.Wordprocessing.Document(body);
        main.Document.Save();

        static DocumentFormat.OpenXml.Wordprocessing.TableCell Cell(string text) =>
            new(new DocumentFormat.OpenXml.Wordprocessing.Paragraph(new DocumentFormat.OpenXml.Wordprocessing.Run(new DocumentFormat.OpenXml.Wordprocessing.Text(text))));
    }

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
