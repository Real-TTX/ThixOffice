using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// Einfügen › Kopfzeile, Fußzeile, Seitenzahl and the contextual tab Kopf- und Fußzeile (shown while one is edited): fields,
/// "Erste Seite anders", "Gerade & ungerade Seiten unterschiedlich", "Kopf- und Fußzeile schließen".
/// </summary>
public static class HeaderFooterCommands
{
    /// <summary>The context of the contextual tab.</summary>
    public const string Context = "headerFooter";

    /// <summary>Einfügen › Seitenzahl: (id suffix, label, top, alignment).</summary>
    public static IReadOnlyList<(string Id, string Label, bool Top, ParagraphAlignment Alignment)> PageNumberPlaces { get; } =
    [
        ("top.left", "Seitenanfang, links", true, ParagraphAlignment.Left),
        ("top.center", "Seitenanfang, zentriert", true, ParagraphAlignment.Center),
        ("top.right", "Seitenanfang, rechts", true, ParagraphAlignment.Right),
        ("bottom.left", "Seitenende, links", false, ParagraphAlignment.Left),
        ("bottom.center", "Seitenende, zentriert", false, ParagraphAlignment.Center),
        ("bottom.right", "Seitenende, rechts", false, ParagraphAlignment.Right),
    ];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;
        bool Editing() => Editor()?.IsEditingHeaderFooter == true;
        int Page() => Editor()?.CaretPage ?? 0;

        var commands = window.Commands;
        foreach (var footer in new[] { false, true })
        {
            var id = footer ? WriteCommandIds.Footer : WriteCommandIds.Header;
            var name = footer ? "Fußzeile" : "Kopfzeile";
            commands.Add(new OfficeCommand(id, name, () => Editor()?.EditHeaderFooter(Page(), footer))
            {
                Icon = footer ? "footer" : "header", CanExecute = HasEditor,
                Description = footer ? "Bearbeitet die Fußzeile – Text am unteren Rand jeder Seite." : "Bearbeitet die Kopfzeile – Text am oberen Rand jeder Seite.",
                Keywords = footer ? ["fußzeile", "unten", "seitenende"] : ["kopfzeile", "oben", "seitenanfang"],
            });
            commands.Add(new OfficeCommand($"{id}.edit", $"{name} bearbeiten", () => Editor()?.EditHeaderFooter(Page(), footer))
            {
                Icon = footer ? "footer" : "header", CanExecute = HasEditor, IsSearchable = false,
            });
            commands.Add(new OfficeCommand($"{id}.remove", $"{name} entfernen", () => Remove(Editor(), footer))
            {
                Icon = "trash", CanExecute = HasEditor, IsSearchable = false,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.PageNumber, "Seitenzahl", () => Editor()?.Document.Session.InsertPageNumber(top: false, ParagraphAlignment.Center))
        {
            Icon = "page-number", CanExecute = HasEditor,
            Description = "Nummeriert die Seiten – oben oder unten, links, zentriert oder rechts.", Keywords = ["seitenzahl", "nummerierung", "seiten", "zahl"],
        });
        foreach (var (id, label, top, alignment) in PageNumberPlaces)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.PageNumber}.{id}", label, () => Editor()?.Document.Session.InsertPageNumber(top, alignment))
            {
                Icon = "page-number", CanExecute = HasEditor, IsSearchable = false,
            });
        }

        // Kopf- und Fußzeile (contextual)
        commands.Add(new OfficeCommand(WriteCommandIds.HeaderFooterPage, "Seitenzahl einfügen", () => Editor()?.Session.InsertField(FieldKind.Page))
        {
            Icon = "page-number", CanExecute = Editing, Description = "Fügt an der Einfügemarke die Nummer der Seite ein.",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.HeaderFooterPages, "Seitenanzahl", () => Editor()?.Session.InsertField(FieldKind.NumPages))
        {
            Icon = "file-text", CanExecute = Editing, Description = "Fügt an der Einfügemarke die Anzahl der Seiten ein.",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.HeaderFooterDate, "Datum", () => Editor()?.Session.InsertField(FieldKind.Date))
        {
            Icon = "calendar", CanExecute = Editing, Description = "Fügt das Datum ein – es zeigt immer den heutigen Tag.",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.HeaderFooterFirstPage, "Erste Seite anders", () => ToggleFirstPage(Editor()))
        {
            Icon = "file", CanExecute = Editing,
            IsChecked = () => Editor() is { } editor && editor.Document.Content.Sections[editor.Document.Session.CurrentSection].Properties.TitlePage,
            Description = "Die erste Seite des Abschnitts bekommt eine eigene Kopf- und Fußzeile (z. B. keine).",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.HeaderFooterEvenOdd, "Gerade & ungerade Seiten unterschiedlich", () => ToggleEvenOdd(Editor()))
        {
            Icon = "columns", CanExecute = Editing,
            IsChecked = () => Editor()?.Document.Content.Settings.EvenAndOddHeaders == true,
            Description = "Gerade Seiten bekommen eigene Kopf- und Fußzeilen.",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.HeaderFooterClose, "Kopf- und Fußzeile schließen", () => Editor()?.CloseHeaderFooter())
        {
            Icon = "x", CanExecute = Editing, Description = "Zurück zum Text des Dokuments (Esc, Doppelklick in den Text).",
        });
    }

    /// <summary>Kopfzeile / Fußzeile entfernen: the one the caret's page shows (in the section that has it).</summary>
    private static void Remove(WriteEditor? editor, bool footer)
    {
        if (editor is null)
        {
            return;
        }

        editor.CloseHeaderFooter();
        var page = editor.Layout.Pages[editor.CaretPage];
        var kind = footer ? page.FooterKind : page.HeaderKind;
        var session = editor.Document.Session;
        session.SetHeaderFooter(session.DefiningSection(page.Section.Index, kind, footer), kind, footer, null);
    }

    /// <summary>"Erste Seite anders" changes which story the page shows: the editor opens the one the page shows now.</summary>
    private static void ToggleFirstPage(WriteEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        var (page, footer) = editor.EditedPlace;
        editor.CloseHeaderFooter();
        var session = editor.Document.Session;
        var section = editor.Layout.Pages[page].Section.Index;
        session.SetTitlePage(!editor.Document.Content.Sections[section].Properties.TitlePage, section);
        editor.EditHeaderFooter(page, footer);
    }

    private static void ToggleEvenOdd(WriteEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        var (page, footer) = editor.EditedPlace;
        editor.CloseHeaderFooter();
        editor.Document.Session.SetEvenAndOddHeaders(!editor.Document.Content.Settings.EvenAndOddHeaders);
        editor.EditHeaderFooter(page, footer);
    }
}
