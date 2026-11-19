using SkiaSharp;
using ThixOffice.Core.Models;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;

namespace ThixOffice.Desktop.Rendering;

/// <summary>
/// The laid-out pages of a document for printing and the print preview (every app): a snapshot of the display lists in points
/// (taken on the UI thread, so that the worker that prints never touches the layout), drawn with <see cref="SkiaPageRenderer"/>
/// into pixels on the app's paper colour or written as a PDF (<see cref="PdfExport"/>) – the same pictures as on screen and in
/// the exported PDF. <see cref="Grayscale"/> prints in shades of grey (PowerPoint's Graustufen).
/// </summary>
public class PrintablePages(IReadOnlyList<PageDisplayList> pages, SkiaTextMeasurer? measurer, string title, SKColor paper) : IPrintablePages
{
    /// <summary>The luminance (Rec. 709) into every channel, alpha kept.</summary>
    private static readonly float[] GrayMatrix =
    [
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0.2126f, 0.7152f, 0.0722f, 0, 0,
        0, 0, 0, 1, 0,
    ];

    /// <summary>Prints and previews in shades of grey (the paper stays as it is).</summary>
    public bool Grayscale { get; init; }

    public int Count => pages.Count;

    public (double Width, double Height) SizeOf(int index) => (pages[index].Width, pages[index].Height);

    /// <summary>The display list of a page (the preview draws it).</summary>
    public PageDisplayList this[int index] => pages[index];

    public PrintRaster Render(int index, double dotsPerInch, int top, int height)
    {
        var page = pages[index];
        var scale = (float)(dotsPerInch / 72);
        var width = Math.Max(1, (int)Math.Ceiling(page.Width * scale));
        var info = new SKImageInfo(width, Math.Max(1, height), SKColorType.Bgra8888, SKAlphaType.Premul);
        using var bitmap = new SKBitmap(info);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(paper);
            canvas.Translate(0, -top);
            canvas.Scale(scale);
            DrawPage(canvas, page);
        }

        return new PrintRaster(info.Width, info.Height, bitmap.GetPixelSpan().ToArray());
    }

    public void WritePdf(IReadOnlyList<int> indexes, Stream output)
    {
        if (!Grayscale)
        {
            PdfExport.Export(indexes.Select(index => pages[index]), output, new PdfMetadata(Title: title), measurer);
            return;
        }

        var metadata = SKDocumentPdfMetadata.Default;
        metadata.Title = title;
        metadata.Producer = "ThixOffice";
        using var stream = new SKManagedWStream(output, disposeManagedStream: false);
        using var document = SKDocument.CreatePdf(stream, metadata) ?? throw new InvalidOperationException("Skia cannot create a PDF document.");
        foreach (var index in indexes)
        {
            var page = pages[index];
            DrawPage(document.BeginPage((float)page.Width, (float)page.Height), page);
            document.EndPage();
        }

        document.Close();
        stream.Flush();
    }

    /// <summary>Draws a page (through a grey colour filter when <see cref="Grayscale"/>).</summary>
    private void DrawPage(SKCanvas canvas, PageDisplayList page)
    {
        if (!Grayscale)
        {
            SkiaPageRenderer.Draw(canvas, page, measurer, forPaper: true);
            return;
        }

        using var paint = new SKPaint { ColorFilter = SKColorFilter.CreateColorMatrix(GrayMatrix) };
        canvas.SaveLayer(paint);
        SkiaPageRenderer.Draw(canvas, page, measurer, forPaper: true);
        canvas.Restore();
    }
}
