using SkiaSharp;
using ThixOffice.Text.Drawing;

namespace ThixOffice.Text.Skia;

/// <summary>Document information written into a PDF.</summary>
public sealed record PdfMetadata(
    string? Title = null,
    string? Author = null,
    string? Subject = null,
    string Creator = "ThixWrite",
    string Producer = "ThixOffice");

/// <summary>
/// Exports pages to PDF with Skia's PDF backend (SKDocument.CreatePdf): one PDF page per display list, page size in points,
/// fonts embedded (subset) by Skia, text stays text.
/// </summary>
public static class PdfExport
{
    /// <summary>Writes <paramref name="pages"/> as a PDF into <paramref name="output"/> (left open).</summary>
    public static void Export(IEnumerable<PageDisplayList> pages, Stream output, PdfMetadata? metadata = null, SkiaTextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(output);
        var info = metadata ?? new PdfMetadata();
        var pdfMetadata = SKDocumentPdfMetadata.Default;
        pdfMetadata.Title = info.Title ?? "";
        pdfMetadata.Author = info.Author ?? "";
        pdfMetadata.Subject = info.Subject ?? "";
        pdfMetadata.Creator = info.Creator;
        pdfMetadata.Producer = info.Producer;
        pdfMetadata.Creation = DateTime.Now;
        pdfMetadata.Modified = DateTime.Now;

        using var stream = new SKManagedWStream(output, disposeManagedStream: false);
        using var document = SKDocument.CreatePdf(stream, pdfMetadata)
                             ?? throw new InvalidOperationException("Skia cannot create a PDF document.");
        foreach (var page in pages)
        {
            var canvas = document.BeginPage((float)page.Width, (float)page.Height);
            SkiaPageRenderer.Draw(canvas, page, measurer);
            document.EndPage();
        }

        document.Close();
        stream.Flush();
    }
}
