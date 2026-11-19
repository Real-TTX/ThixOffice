using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;
using ThixWrite.Editor;

namespace ThixWrite.Pages;

/// <summary>Datei › Exportieren of ThixWrite: the shared <see cref="OfficeExportPage"/> with the layout's pages.</summary>
public sealed class ExportPage(OfficeWindow window)
    : OfficeExportPage(window, () => [.. ((WriteEditor)window.Editor).Layout.GetDisplayLists()], WriteFonts.Measurer)
{
    /// <summary>The backstage entry of the page.</summary>
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Exportieren", "export", () => new ExportPage(window));
}
