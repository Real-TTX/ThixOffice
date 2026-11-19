using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Layout;
using ThixShow.Editor;

namespace ThixShow.Pages;

/// <summary>
/// What ThixShow prints and exports, as PowerPoint offers it: Ganzseitige Folien, Notizenseiten, Handzettel with 1, 2, 3, 4, 6 or 9
/// slides per page (<see cref="PrintLayouts"/>); hidden slides are left out. Colour or Graustufen.
/// </summary>
public static class ShowPages
{
    /// <summary>The layouts in the order of the print page's list.</summary>
    public static IReadOnlyList<(string Label, PrintKind Kind, int PerPage)> Layouts { get; } =
    [
        ("Ganzseitige Folien", PrintKind.Slides, 1),
        ("Notizenseiten", PrintKind.NotesPages, 1),
        ("Handzettel: 1 Folie pro Seite", PrintKind.Handouts, 1),
        ("Handzettel: 2 Folien pro Seite", PrintKind.Handouts, 2),
        ("Handzettel: 3 Folien pro Seite (mit Linien)", PrintKind.Handouts, 3),
        ("Handzettel: 4 Folien pro Seite", PrintKind.Handouts, 4),
        ("Handzettel: 6 Folien pro Seite", PrintKind.Handouts, 6),
        ("Handzettel: 9 Folien pro Seite", PrintKind.Handouts, 9),
    ];

    public static IReadOnlyList<string> ColorModes { get; } = ["Farbe", "Graustufen"];

    /// <summary>The pages of a layout (index into <see cref="Layouts"/>).</summary>
    public static IReadOnlyList<PageDisplayList> DisplayLists(ShowEditor editor, int layout)
    {
        editor.Notes.Commit();
        var (_, kind, perPage) = Layouts[Math.Clamp(layout, 0, Layouts.Count - 1)];
        var pages = PrintLayouts.Pages(editor.Session.Presentation, OfficeFonts.Skia, kind, perPage, includeHidden: false, new RenderOptions(Now: DateTime.Now));
        if (pages.Count > 0)
        {
            return pages;
        }

        var size = editor.Session.Presentation.SlideSize;
        return [new PageDisplayList(0, size.WidthPoints, size.HeightPoints, [])];
    }

    /// <summary>The page with the current slide in a layout.</summary>
    public static int PageOf(ShowEditor editor, int layout)
    {
        var presentation = editor.Session.Presentation;
        var shown = presentation.Slides.Select((slide, index) => (slide, index)).Where(pair => !pair.slide.Hidden).Select(pair => pair.index).ToList();
        var position = Math.Max(0, shown.IndexOf(editor.CurrentIndex));
        var perPage = Layouts[Math.Clamp(layout, 0, Layouts.Count - 1)] is { Kind: PrintKind.Handouts } handout ? handout.PerPage : 1;
        return position / perPage;
    }
}

/// <summary>Datei › Drucken of ThixShow (Strg+P): the shared page with PowerPoint's layouts, Farbe / Graustufen and the preview.</summary>
public sealed class ShowPrintPage(OfficeWindow window, ShowEditor editor) : OfficePrintPage(window, new ShowPrintSource(window, editor))
{
    public static BackstageItem Item(OfficeWindow window) =>
        new(PageId, "Drucken", "print", () => new ShowPrintPage(window, (ShowEditor)window.Editor), Shortcut: window.Commands.Find(ShowCommandIds.Print)?.CurrentShortcut);

    private sealed class ShowPrintSource(OfficeWindow window, ShowEditor editor) : IPrintSource
    {
        public IReadOnlyList<string> Scopes => [.. ShowPages.Layouts.Select(layout => layout.Label)];

        public int Scope { get; set; }

        public IReadOnlyList<string> Variants => ShowPages.ColorModes;

        public int Variant { get; set; }

        public int CurrentPage => ShowPages.PageOf(editor, Scope);

        public Func<Task<bool>>? PageSetup => null;

        public PrintablePages Snapshot() => new(ShowPages.DisplayLists(editor, Scope), OfficeFonts.Skia, window.Document.Title, ShowPalette.PaperSkia) { Grayscale = Variant == 1 };
    }
}

/// <summary>Datei › Exportieren of ThixShow: the slides as PDF (one page per slide, like PowerPoint's default export).</summary>
public sealed class ShowExportPage(OfficeWindow window) : OfficeExportPage(window,
    () => ShowPages.DisplayLists((ShowEditor)window.Editor, 0), OfficeFonts.Skia,
    "Die Folien als PDF – eine Seite pro Folie, Text bleibt Text")
{
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Exportieren", "export", () => new ShowExportPage(window));
}
