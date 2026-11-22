using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixContacts.Editor;
using ThixOffice.Pim.Desktop.People;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop;
using ThixOffice.Pim.Model;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixContacts.Pages;

/// <summary>
/// The printed contacts, in points (A4 portrait): a list (Name, Firma, E-Mail, Telefon – a heading, a line per contact, a page footer) or cards (two
/// columns of boxes: name, position and company, every e-mail address, number and address). The same pages for the printer, the preview and the PDF.
/// </summary>
public static class ContactsPrinting
{
    public const double PageWidth = 595.28;
    public const double PageHeight = 841.89;
    public const double Margin = 48;
    private const string Font = "Segoe UI";

    private static RgbColor Ink => PrintPalette.Ink;

    private static RgbColor Muted => PrintPalette.Muted;

    private static RgbColor Rule => PrintPalette.Rule;

    public static IReadOnlyList<string> Layouts { get; } = ["Liste", "Karten"];

    public static IReadOnlyList<PageDisplayList> Pages(IReadOnlyList<Person> people, bool cards, ITextMeasurer measurer, string title)
    {
        var pages = new List<List<DisplayItem>>();
        if (cards)
        {
            Cards(people, measurer, pages, title);
        }
        else
        {
            List(people, measurer, pages, title);
        }

        if (pages.Count == 0)
        {
            pages.Add([]);
        }

        var result = new List<PageDisplayList>();
        for (var i = 0; i < pages.Count; i++)
        {
            Text(pages[i], measurer, $"Seite {i + 1} von {pages.Count}", PageWidth - Margin, PageHeight - Margin + 18, 8, false, Muted, alignRight: true);
            result.Add(new PageDisplayList(i, PageWidth, PageHeight, pages[i]));
        }

        return result;
    }

    private static void List(IReadOnlyList<Person> people, ITextMeasurer measurer, List<List<DisplayItem>> pages, string title)
    {
        double[] columns = [Margin, Margin + 160, Margin + 290, Margin + 430];
        double[] widths = [155, 125, 135, PageWidth - Margin - columns[3]];
        List<DisplayItem>? page = null;
        double y = 0;
        void NewPage()
        {
            page = [];
            pages.Add(page);
            y = Margin;
            if (pages.Count == 1)
            {
                Text(page, measurer, title, Margin, y + 14, 16, true, Ink);
                y += 30;
            }

            string[] heads = ["Name", "Firma", "E-Mail", "Telefon"];
            for (var c = 0; c < heads.Length; c++)
            {
                Text(page, measurer, heads[c], columns[c], y + 10, 9, true, Ink);
            }

            y += 16;
            page.Add(new LineItem(Margin, y, PageWidth - Margin, y, Ink, 0.75));
            y += 4;
        }

        NewPage();
        foreach (var person in people)
        {
            if (y + 16 > PageHeight - Margin)
            {
                NewPage();
            }

            string[] values = [person.Name, person.Data.Company, person.Email, person.Phone];
            for (var c = 0; c < values.Length; c++)
            {
                Text(page!, measurer, Fit(measurer, values[c], widths[c] - 6, 9), columns[c], y + 11, 9, c == 0, c == 0 ? Ink : Muted);
            }

            y += 16;
            page!.Add(new LineItem(Margin, y, PageWidth - Margin, y, Rule, 0.4));
        }
    }

    private static void Cards(IReadOnlyList<Person> people, ITextMeasurer measurer, List<List<DisplayItem>> pages, string title)
    {
        const double gap = 14;
        var width = (PageWidth - 2 * Margin - gap) / 2;
        List<DisplayItem>? page = null;
        var columnY = new double[2];
        void NewPage()
        {
            page = [];
            pages.Add(page);
            var top = Margin;
            if (pages.Count == 1)
            {
                Text(page, measurer, title, Margin, top + 14, 16, true, Ink);
                top += 30;
            }

            columnY[0] = columnY[1] = top;
        }

        NewPage();
        foreach (var person in people)
        {
            var lines = new List<(string Text, double Size, bool Bold, RgbColor Color)> { (person.Name, 11, true, Ink) };
            var work = string.Join(" · ", new[] { person.Data.JobTitle, person.Data.Company }.Where(p => p.Length > 0));
            if (work.Length > 0)
            {
                lines.Add((work, 9, false, Muted));
            }

            lines.AddRange(person.Data.Emails.Select(e => ($"{MatCloud.Contract.Contacts.ContactLabels.NameOf(e.Label)}: {e.Value}", 9.0, false, Ink)));
            lines.AddRange(person.Data.Phones.Select(p => ($"{MatCloud.Contract.Contacts.ContactLabels.NameOf(p.Label)}: {p.Value}", 9.0, false, Ink)));
            foreach (var address in person.Data.Addresses)
            {
                lines.AddRange(PersonCard.Lines(address).Select(l => (l, 9.0, false, Ink)));
            }

            if (person.Data.Birthday.Length > 0)
            {
                lines.Add(("Geburtstag: " + PersonCard.DateText(person.Data.Birthday), 9, false, Muted));
            }

            var height = 16 + lines.Sum(l => l.Size + 4);
            var column = columnY[0] <= columnY[1] ? 0 : 1;
            if (columnY[column] + height > PageHeight - Margin)
            {
                column = 1 - column;
                if (columnY[column] + height > PageHeight - Margin)
                {
                    NewPage();
                    column = 0;
                }
            }

            var x = Margin + column * (width + gap);
            var top = columnY[column];
            page!.Add(new RectangleItem(x, top, width, height, null, Rule, 0.75));
            var lineY = top + 8;
            foreach (var (text, size, bold, color) in lines)
            {
                lineY += size + 2;
                Text(page, measurer, Fit(measurer, text, width - 16, size), x + 8, lineY, size, bold, color);
                lineY += 2;
            }

            columnY[column] = top + height + gap;
        }
    }

    /// <summary>Shortens a text with "…" until it fits.</summary>
    private static string Fit(ITextMeasurer measurer, string text, double width, double size)
    {
        if (text.Length == 0 || measurer.Shape(text, new FontSpec(Font, size)).Width <= width)
        {
            return text;
        }

        var cut = text;
        while (cut.Length > 1 && measurer.Shape(cut + "…", new FontSpec(Font, size)).Width > width)
        {
            cut = cut[..^1];
        }

        return cut + "…";
    }

    private static void Text(List<DisplayItem> items, ITextMeasurer measurer, string text, double x, double baseline, double size, bool bold, RgbColor color, bool alignRight = false)
    {
        if (text.Length == 0)
        {
            return;
        }

        var font = new FontSpec(Font, size, bold);
        var shaped = measurer.Shape(text.Replace('\n', ' '), font);
        var left = alignRight ? x - shaped.Width : x;
        var characterX = new double[shaped.Text.Length];
        var position = 0.0;
        for (var i = 0; i < shaped.Text.Length; i++)
        {
            characterX[i] = position;
            position += shaped.Advances[i];
        }

        if (shaped.GlyphRuns.Count == 0)
        {
            items.Add(new GlyphRunItem(left, baseline, shaped.Text, font, new FontFace(Font, bold, false), color, characterX, [], []));
            return;
        }

        foreach (var run in shaped.GlyphRuns)
        {
            items.Add(new GlyphRunItem(left, baseline, shaped.Text, font, run.Face, color, characterX, run.Glyphs, run.Positions));
        }
    }
}

/// <summary>Datei › Drucken of ThixContacts: the shared page with "Liste" or "Karten" and "Alle Kontakte" or "Auswahl".</summary>
public sealed class ContactsPrintPage(OfficeWindow window, ContactsEditor editor) : OfficePrintPage(window, new ContactsPrintSource(editor))
{
    public static BackstageItem Item(OfficeWindow window) =>
        new(PageId, "Drucken", "print", () => new ContactsPrintPage(window, (ContactsEditor)window.Editor), Shortcut: window.Commands.Find(ContactsCommandIds.Print)?.CurrentShortcut);

    /// <summary>The contacts to print: what the list shows (its order), or only the selection.</summary>
    internal static IReadOnlyList<Person> PeopleOf(ContactsEditor editor, bool selection) => selection && editor.Selected.Count > 0 ? editor.Selected : editor.Shown;

    private sealed class ContactsPrintSource(ContactsEditor editor) : IPrintSource
    {
        public IReadOnlyList<string> Scopes => ContactsPrinting.Layouts;

        public int Scope { get; set; }

        public int CurrentPage => 0;

        public Func<Task<bool>>? PageSetup => null;

        public IReadOnlyList<string> Variants => ["Alle Kontakte der Liste", "Nur die Auswahl"];

        public string VariantLabel => "Kontakte";

        public int Variant { get; set; }

        public PrintablePages Snapshot()
        {
            var title = "Kontakte";
            var pages = ContactsPrinting.Pages(PeopleOf(editor, Variant == 1), Scope == 1, OfficeFonts.Skia, title);
            return new PrintablePages(pages, OfficeFonts.Skia, title, PrintPalette.Paper);
        }
    }
}

/// <summary>
/// Datei › Importieren/Exportieren: tiles like the backstage of Outlook's "Öffnen und exportieren" – Importieren (vCard, CSV), Exportieren (vCard,
/// CSV) and Als PDF speichern (the list as on paper).
/// </summary>
public sealed class ContactsTransferPage : UserControl
{
    public const string PageId = "transfer";

    public ContactsTransferPage(OfficeWindow window)
    {
        var editor = (ContactsEditor)window.Editor;
        ImportButton = Tile("upload", "Importieren", "Kontakte aus einer vCard-Datei (Outlook, Apple, Google) oder einer CSV-Datei übernehmen");
        ExportButton = Tile("download", "Exportieren", "Kontakte als vCard 3.0, vCard 4.0 oder CSV speichern – alle oder die ausgewählten");
        DuplicatesButton = Tile("copy", "Dubletten suchen", "Kontakte mit demselben Namen, derselben E-Mail-Adresse oder Telefonnummer zusammenführen");
        ImportButton.Click += async (_, _) => await Run(window, editor, ContactsCommandIds.Import);
        ExportButton.Click += async (_, _) => await Run(window, editor, ContactsCommandIds.Export);
        DuplicatesButton.Click += async (_, _) => await Run(window, editor, ContactsCommandIds.Duplicates);
        var title = new TextBlock { Text = "Importieren/Exportieren" }.Themed("MatText.PageTitle");
        Content = new ScrollViewer
        {
            Content = new StackPanel
            {
                Margin = new Thickness(32, 26),
                Spacing = 16,
                MaxWidth = 900,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children = { title, ImportButton, ExportButton, DuplicatesButton },
            },
        };
    }

    public Button ImportButton { get; }

    public Button ExportButton { get; }

    public Button DuplicatesButton { get; }

    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Importieren/Exportieren", "upload", () => new ContactsTransferPage(window));

    private static async Task Run(OfficeWindow window, ContactsEditor editor, string id)
    {
        window.Backstage.Close();
        await editor.RunAsync(id);
    }

    private static Button Tile(string icon, string title, string text)
    {
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        content.Children.Add(new MatIcon { Icon = icon, Size = 28, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center }.Use(MatIcon.ForegroundProperty, "Mat.Accent"));
        var texts = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }.Themed("MatText.Muted"),
            },
        };
        Grid.SetColumn(texts, 1);
        content.Children.Add(texts);
        var button = new Button { Content = content, Padding = new Thickness(14, 12), MinWidth = 260, MaxWidth = 560, HorizontalAlignment = HorizontalAlignment.Left, HorizontalContentAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(button, title);
        return button;
    }
}

/// <summary>Datei › Als PDF speichern: the list (as the printer gets it) as a PDF.</summary>
public sealed class ContactsPdfPage(OfficeWindow window) : OfficeExportPage(window,
    () => ContactsPrinting.Pages(((ContactsEditor)window.Editor).Shown, false, OfficeFonts.Skia, "Kontakte"), OfficeFonts.Skia,
    "Die Kontakte der Liste als Tabelle – Name, Firma, E-Mail, Telefon")
{
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Als PDF speichern", "export", () => new ContactsPdfPage(window));
}
