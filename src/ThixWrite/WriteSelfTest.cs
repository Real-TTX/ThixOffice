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

        window.Close();
        await Settle();
        return $"{WriteFonts.Default.Body}, {WriteFonts.Families.Count} font families, docx {new FileInfo(path).Length} bytes, PDF {bytes.Length} bytes";
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
