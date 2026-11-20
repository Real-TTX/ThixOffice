using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>Einfügen › Seiten (Deckblatt, Leere Seite), Text › Initiale and Links › Lesezeichen.</summary>
public static class InsertCommands
{
    /// <summary>The cover page designs: id, label, design.</summary>
    public static readonly IReadOnlyList<(string Id, string Label, CoverDesign Design)> CoverDesigns =
    [
        ("band", "Band", CoverDesign.Band),
        ("sidebar", "Seitenleiste", CoverDesign.Sidebar),
        ("frame", "Rahmen", CoverDesign.Frame),
    ];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;
        var commands = window.Commands;

        commands.Add(new OfficeCommand(WriteCommandIds.CoverPage, "Deckblatt", () => Task.CompletedTask)
        {
            Icon = "template", CanExecute = HasEditor,
            Description = "Setzt ein gestaltetes Deckblatt an den Anfang des Dokuments (ein vorhandenes wird ersetzt).", Keywords = ["titelseite", "deckblatt", "cover"],
        });
        foreach (var (id, label, design) in CoverDesigns)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.CoverPage}.{id}", label, () =>
            {
                if (Editor() is { } editor)
                {
                    editor.CloseHeaderFooter();
                    editor.Session.InsertCoverPage(design, "[Titel des Dokuments]", "[Untertitel]", SettingsService.AuthorName, DateTime.Today);
                }
            })
            {
                Icon = "template", CanExecute = HasEditor, IsSearchable = false,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.BlankPage, "Leere Seite", () => Editor()?.Session.InsertBlankPage())
        {
            Icon = "file-new", CanExecute = HasEditor,
            Description = "Fügt an der Einfügemarke eine leere Seite ein.", Keywords = ["seite", "leer", "einfügen"],
        });

        commands.Add(new OfficeCommand(WriteCommandIds.DropCap, "Initiale", () => Task.CompletedTask)
        {
            Icon = "font-grow", CanExecute = HasEditor,
            Description = "Macht den ersten Buchstaben des Absatzes groß über mehrere Zeilen.", Keywords = ["initiale", "großbuchstabe", "anfangsbuchstabe"],
        });
        commands.Add(new OfficeCommand($"{WriteCommandIds.DropCap}.none", "Ohne", () => Editor()?.Session.SetDropCap(0))
        {
            Icon = "x", CanExecute = HasEditor, IsSearchable = false, IsChecked = () => Editor()?.Session.DropCapLines == 0,
        });
        commands.Add(new OfficeCommand($"{WriteCommandIds.DropCap}.drop", "Im Text", () => Editor()?.Session.SetDropCap(3))
        {
            Icon = "font-grow", CanExecute = HasEditor, IsSearchable = false, IsChecked = () => Editor()?.Session.DropCapLines > 0,
        });

        commands.Add(new OfficeCommand(WriteCommandIds.Bookmark, "Lesezeichen", () => Editor() is { } editor ? BookmarkDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "tag", CanExecute = HasEditor,
            Description = "Benennt eine Stelle des Dokuments als Textmarke; Links können zu ihr springen.", Keywords = ["textmarke", "lesezeichen", "sprungmarke"],
        });
    }
}
