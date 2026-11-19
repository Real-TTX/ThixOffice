using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// What a PDF of a document is made of: its pages in points (as laid out for printing) and the measurer that laid them out (the
/// fonts that are embedded). Every app's editor gives one (<see cref="OfficeEditor.Pdf"/>); "Speichern unter" › PDF, "Als PDF speichern"
/// and Datei › Exportieren write it through <see cref="PdfExport"/>.
/// </summary>
public sealed record PdfSource(Func<IReadOnlyList<PageDisplayList>> Pages, SkiaTextMeasurer Measurer)
{
    /// <summary>Writes the PDF of the pages into <paramref name="stream"/>, titled <paramref name="title"/>.</summary>
    public void Write(Stream stream, string title) =>
        PdfExport.Export(Pages(), stream, new PdfMetadata(Title: Path.GetFileNameWithoutExtension(title)), Measurer);
}
