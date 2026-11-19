using SkiaSharp;
using ThixOffice.Core.Models;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;

namespace ThixOffice.Desktop.Rendering;

/// <summary>
/// The laid-out pages of a document for printing and the print preview (every app): a snapshot of the display lists in points
/// (taken on the UI thread, so that the worker that prints never touches the layout), drawn with <see cref="SkiaPageRenderer"/>
/// into pixels on the app's paper colour or written as a PDF (<see cref="PdfExport"/>) – the same pictures as on screen and in
/// the exported PDF.
/// </summary>
public class PrintablePages(IReadOnlyList<PageDisplayList> pages, SkiaTextMeasurer? measurer, string title, SKColor paper) : IPrintablePages
{
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
            SkiaPageRenderer.Draw(canvas, page, measurer, forPaper: true);
        }

        return new PrintRaster(info.Width, info.Height, bitmap.GetPixelSpan().ToArray());
    }

    public void WritePdf(IReadOnlyList<int> indexes, Stream output) =>
        PdfExport.Export(indexes.Select(index => pages[index]), output, new PdfMetadata(Title: title), measurer);
}
