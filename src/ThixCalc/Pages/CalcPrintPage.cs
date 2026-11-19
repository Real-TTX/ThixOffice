using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Pages;

/// <summary>
/// The pages ThixCalc prints and exports: the active sheet, the whole workbook (visible sheets) or the selection, each sheet cut
/// by its own page setup (<see cref="SheetPagination"/>), numbered through. A sheet with nothing on it gives one empty page.
/// </summary>
public static class CalcPages
{
    public static IReadOnlyList<string> Scopes { get; } = ["Aktives Blatt drucken", "Gesamte Arbeitsmappe drucken", "Auswahl drucken"];

    public static IReadOnlyList<PageDisplayList> DisplayLists(CalcEditor editor, int scope, string title, out int activeCellPage)
    {
        var session = editor.Session;
        var entries = new List<PrintedPage>();
        activeCellPage = 0;
        var sheets = scope == 1 ? session.Workbook.Sheets.Where(sheet => sheet.Visibility == SheetVisibility.Visible).ToList() : [session.ActiveSheet];
        foreach (var sheet in sheets)
        {
            var pages = SheetPagination.Paginate(sheet, editor.Metrics, scope == 2 ? session.Selection : null);
            if (sheet == session.ActiveSheet && pages.Select((page, index) => (page, index)).FirstOrDefault(entry => entry.page.Cells.Contains(session.ActiveCell)) is { page: not null } found)
            {
                activeCellPage = entries.Count + found.index;
            }

            entries.AddRange(pages);
        }

        if (entries.Count == 0)
        {
            var (width, height) = session.ActiveSheet.PageSetup.PaperPoints;
            return [new PageDisplayList(0, width, height, [])];
        }

        var now = DateTime.Now;
        return [.. entries.Select((page, index) => SheetPagination.Render(page, editor.Metrics, index, (page.Sheet.PageSetup.FirstPageNumber ?? 1) + index, entries.Count, title, now))];
    }
}

/// <summary>Datei › Drucken of ThixCalc: the shared page with Aktives Blatt / Gesamte Arbeitsmappe / Auswahl and Seite einrichten.</summary>
public sealed class CalcPrintPage(OfficeWindow window, CalcEditor editor) : OfficePrintPage(window, new CalcPrintSource(window, editor))
{
    public static BackstageItem Item(OfficeWindow window) =>
        new(PageId, "Drucken", "print", () => new CalcPrintPage(window, (CalcEditor)window.Editor), Shortcut: window.Commands.Find(CalcCommandIds.Print)?.CurrentShortcut);

    private sealed class CalcPrintSource(OfficeWindow window, CalcEditor editor) : IPrintSource
    {
        private int _current;

        public IReadOnlyList<string> Scopes => CalcPages.Scopes;

        public int Scope { get; set; }

        public int CurrentPage => _current;

        public Func<Task<bool>>? PageSetup => () => PageSetupDialog.ShowAsync(window, editor);

        public PrintablePages Snapshot()
        {
            var lists = CalcPages.DisplayLists(editor, Scope, window.Document.Title, out _current);
            return new PrintablePages(lists, CalcFonts.Skia, window.Document.Title, GridPalette.SheetSkia);
        }
    }
}

/// <summary>Datei › Exportieren of ThixCalc: the active sheet's pages as PDF.</summary>
public sealed class CalcExportPage(OfficeWindow window) : OfficeExportPage(window,
    () => CalcPages.DisplayLists((CalcEditor)window.Editor, 0, window.Document.Title, out _), CalcFonts.Skia,
    "Die Seiten des aktiven Blatts wie gedruckt – mit Seitenlayout, Kopf- und Fußzeile")
{
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Exportieren", "export", () => new CalcExportPage(window));
}
