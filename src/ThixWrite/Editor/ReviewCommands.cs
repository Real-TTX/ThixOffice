using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;

namespace ThixWrite.Editor;

/// <summary>Überprüfen › Kommentare: Neuer Kommentar (Strg+Alt+K), Löschen, Vorheriger, Weiter, Kommentare anzeigen.</summary>
public static class ReviewCommands
{
    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool InBody() => Editor() is { IsEditingHeaderFooter: false, IsEditingNote: false };
        bool HasComments() => Editor() is { } editor && editor.Document.Session.Comments().Count > 0;
        bool OnComment() => Editor()?.Document.Session.CommentAtCaret is not null;

        var commands = window.Commands;
        commands.Add(new OfficeCommand(WriteCommandIds.NewComment, "Neuer Kommentar", () => Editor()?.AddComment())
        {
            Icon = "comment", Shortcut = "Ctrl+Alt+K", CanExecute = InBody,
            Description = "Fügt einen Kommentar zum markierten Text (oder zum Wort an der Einfügemarke) hinzu.",
            Keywords = ["kommentar", "anmerkung", "notiz", "kommentieren", "comment"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.DeleteComment, "Löschen", () => DeleteAtCaret(Editor()))
        {
            Icon = "trash", CanExecute = HasComments,
            Description = "Löscht den Kommentar an der Einfügemarke – oder alle Kommentare des Dokuments.",
            Keywords = ["kommentar löschen", "kommentare entfernen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.DeleteCommentOne, "Löschen", () => DeleteAtCaret(Editor()))
        {
            Icon = "trash", CanExecute = OnComment, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(WriteCommandIds.DeleteAllComments, "Alle Kommentare im Dokument löschen", () => Editor()?.Document.Session.DeleteAllComments())
        {
            Icon = "trash", CanExecute = HasComments,
            Description = "Löscht alle Kommentare – der Text bleibt.", Keywords = ["alle kommentare löschen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PreviousComment, "Vorheriger", () => Step(Editor(), forward: false))
        {
            Icon = "chevron-left", CanExecute = HasComments, Description = "Springt zum vorherigen Kommentar.", Keywords = ["vorheriger kommentar"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.NextComment, "Weiter", () => Step(Editor(), forward: true))
        {
            Icon = "chevron-right", CanExecute = HasComments, Description = "Springt zum nächsten Kommentar.", Keywords = ["nächster kommentar"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ShowComments, "Kommentare anzeigen", () => Toggle(Editor()))
        {
            Icon = "message-square", CanExecute = () => Editor() is not null, IsChecked = () => Editor()?.ShowComments == true,
            Description = "Zeigt die Kommentare rechts neben den Seiten.", Keywords = ["kommentare anzeigen", "kommentarbereich"],
        });
    }

    private static void DeleteAtCaret(WriteEditor? editor)
    {
        if (editor?.Document.Session.CommentAtCaret is { } placed)
        {
            editor.Document.Session.DeleteComment(placed.Reference);
        }
    }

    private static void Step(WriteEditor? editor, bool forward)
    {
        if (editor is null)
        {
            return;
        }

        editor.CloseHeaderFooter();
        editor.ShowComments = true;
        editor.Document.Session.NextComment(forward);
        editor.FocusDocument();
    }

    private static void Toggle(WriteEditor? editor)
    {
        if (editor is not null)
        {
            editor.ShowComments = !editor.ShowComments;
        }
    }
}
