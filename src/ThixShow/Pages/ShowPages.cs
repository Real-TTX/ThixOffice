using Avalonia.Controls;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;
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

/// <summary>
/// Datei › Exportieren of ThixShow: the slides as PDF (one page per slide, like PowerPoint's default export) and as pictures – all
/// slides or the current one, PNG or JPEG, in a width of 1280, 1920 or 3840 pixels (PowerPoint's "Als Bild speichern"). All slides
/// become one file per slide beside the chosen name ("Vortrag – Folie 3.png").
/// </summary>
public sealed class ShowExportPage : OfficeExportPage
{
    public static IReadOnlyList<(string Label, int Width)> Sizes { get; } = [("HD (1280 Pixel breit)", 1280), ("Full HD (1920 Pixel breit)", 1920), ("4K (3840 Pixel breit)", 3840)];

    private readonly OfficeWindow _window;

    public ShowExportPage(OfficeWindow window)
        : base(window, () => ShowPages.DisplayLists((ShowEditor)window.Editor, 0), OfficeFonts.Skia, "Die Folien als PDF – eine Seite pro Folie, Text bleibt Text")
    {
        _window = window;
        Scope = new ComboBox { ItemsSource = new[] { "Alle Folien", "Nur die aktuelle Folie" }, SelectedIndex = 0, MinWidth = 220 };
        Format = new ComboBox { ItemsSource = new[] { "PNG (verlustfrei)", "JPEG (kleinere Dateien)" }, SelectedIndex = 0, MinWidth = 220 };
        PictureSize = new ComboBox { ItemsSource = Sizes.Select(size => size.Label).ToList(), SelectedIndex = 1, MinWidth = 220 };
        Avalonia.Automation.AutomationProperties.SetName(Scope, "Folien");
        Avalonia.Automation.AutomationProperties.SetName(Format, "Dateiformat");
        Avalonia.Automation.AutomationProperties.SetName(PictureSize, "Größe");
        PictureButton = new Button { Content = new IconLabel { Icon = "image", Text = "Als Bild exportieren" }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left }.Themed("MatButton.Primary");
        PictureButton.Click += async (_, _) => await ExportPicturesAsync();
        var heading = new TextBlock { Text = "Als Bild exportieren", FontWeight = Avalonia.Media.FontWeight.SemiBold };
        AddSection(new StackPanel
        {
            Spacing = 10,
            Margin = new Avalonia.Thickness(0, 12, 0, 0),
            Children =
            {
                heading,
                new MatDesktop.MatFormField { Label = "Folien", Content = Scope },
                new MatDesktop.MatFormField { Label = "Dateiformat", Content = Format },
                new MatDesktop.MatFormField { Label = "Größe", Content = PictureSize },
                PictureButton,
            },
        });
    }

    public ComboBox Scope { get; }

    public ComboBox Format { get; }

    public ComboBox PictureSize { get; }

    public Button PictureButton { get; }

    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Exportieren", "export", () => new ShowExportPage(window));

    /// <summary>Asks for the name, writes one picture per slide (or the current one) and says where; false when cancelled or refused.</summary>
    public async Task<bool> ExportPicturesAsync()
    {
        if (_window.Editor is not ShowEditor editor || editor.Session.Presentation.SlideCount == 0)
        {
            return false;
        }

        var jpeg = Format.SelectedIndex == 1;
        var extension = jpeg ? ".jpg" : ".png";
        var name = Path.ChangeExtension(DocumentService.SuggestedFileName(_window.Document), extension);
        var target = await OfficeApplication.FileDialogs.PickExportTargetAsync(_window, name, DocumentService.SuggestedFolder(_window.Document), jpeg ? "JPEG-Bild" : "PNG-Bild", extension);
        if (target is null)
        {
            return false;
        }

        try
        {
            var written = ExportPictures(editor, target, Scope.SelectedIndex == 1, jpeg ? RasterFormat.Jpeg : RasterFormat.Png, Sizes[Math.Max(0, PictureSize.SelectedIndex)].Width);
            Message.Show(written.Count == 1 ? $"Exportiert: {written[0]}" : $"{written.Count} Folien exportiert: {Path.GetDirectoryName(written[0])}", InfoSeverity.Success);
            return true;
        }
        catch (BusinessException exception)
        {
            Message.Show(exception.Message, InfoSeverity.Error);
            return false;
        }
    }

    /// <summary>Writes the pictures (the files through the business layer); the paths written.</summary>
    public static IReadOnlyList<string> ExportPictures(ShowEditor editor, string target, bool currentOnly, RasterFormat format, int width)
    {
        editor.Notes.Commit();
        var presentation = editor.Session.Presentation;
        var extension = format == RasterFormat.Jpeg ? ".jpg" : ".png";
        var indexes = currentOnly ? [Math.Max(0, editor.CurrentIndex)] : Enumerable.Range(0, presentation.SlideCount).ToList();
        var folder = Path.GetDirectoryName(Path.GetFullPath(target)) ?? ".";
        var stem = Path.GetFileNameWithoutExtension(target);
        var written = new List<string>();
        foreach (var index in indexes)
        {
            var page = SlideRenderer.Render(presentation, presentation.Slides[index], OfficeFonts.Skia, new RenderOptions(Now: DateTime.Now));
            var bytes = SkiaPageRenderer.RenderImage(page, width, format, OfficeFonts.Skia);
            var path = currentOnly ? target : Path.Combine(folder, $"{stem} – Folie {index + 1}{extension}");
            written.Add(DocumentService.Export(path, extension, stream => stream.Write(bytes)));
        }

        return written;
    }
}
