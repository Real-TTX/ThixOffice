using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;

namespace ThixWrite.Editor;

/// <summary>ThixWrite's pages for printing and the preview: the shared <see cref="ThixOffice.Desktop.Rendering.PrintablePages"/> on white paper.</summary>
public sealed class PrintablePages(IReadOnlyList<PageDisplayList> pages, SkiaTextMeasurer? measurer, string title)
    : ThixOffice.Desktop.Rendering.PrintablePages(pages, measurer, title, DocumentPalette.PaperSkia);
