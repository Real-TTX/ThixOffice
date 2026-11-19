using ThixOffice.Core.Commands;
using ThixOffice.Core.Spelling;
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
        commands.Add(new OfficeCommand(WriteCommandIds.Spelling, "Rechtschreibung", () => Editor() is { } editor ? CheckSpellingAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "spell-check", Shortcut = "F7", MacShortcut = "Ctrl+Alt+L",
            CanExecute = () => Editor() is { IsEditingHeaderFooter: false, IsEditingNote: false } editor && editor.Spelling.Checker is not null,
            Description = "Prüft die Rechtschreibung von der Einfügemarke an – Wort für Wort, mit Vorschlägen.",
            Keywords = ["rechtschreibung", "rechtschreibprüfung", "korrektur", "tippfehler", "spelling", "prüfen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.WordCount, "Wörter zählen", () => Editor() is { } editor ? Dialogs.WordCountDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "file-text", CanExecute = () => Editor() is not null,
            Description = "Zählt Seiten, Wörter, Zeichen und Absätze – des Dokuments oder der Markierung.",
            Keywords = ["wörter zählen", "wortanzahl", "zeichen", "statistik", "anzahl"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Language, "Sprache", () => { })
        {
            Icon = "globe", CanExecute = () => Editor() is not null && LanguageChoices().Count > 0,
            Description = "Legt fest, in welcher Sprache die Rechtschreibung geprüft wird.",
            Keywords = ["sprache", "korrekturhilfen", "rechtschreibung sprache"],
        });
        foreach (var tag in LanguageChoices())
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.Language}.{tag}", SpellingService.DisplayName(tag), () => Editor()?.Document.Session.SetLanguage(tag))
            {
                Icon = "globe", IsSearchable = false, CanExecute = () => Editor() is not null,
                IsChecked = () => Editor() is { } editor && SpellingService.Resolve(editor.Document.Session.Language) is { } used && string.Equals(used, tag, StringComparison.OrdinalIgnoreCase),
            });
        }

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

    /// <summary>The languages the Sprache menu offers: those the system can check (at most twelve, German and English first).</summary>
    public static IReadOnlyList<string> LanguageChoices() =>
        SpellingService.Languages
            .OrderBy(tag => tag.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? 0 : tag.StartsWith("en", StringComparison.OrdinalIgnoreCase) ? 1 : 2)
            .ThenBy(tag => tag, StringComparer.OrdinalIgnoreCase)
            .Where(tag => tag.All(character => char.IsLetterOrDigit(character) || character == '-'))
            .Take(12)
            .ToList();

    /// <summary>Rechtschreibung (F7): the dialog steps through the unknown words from the caret on.</summary>
    private static async Task CheckSpellingAsync(OfficeWindow window, WriteEditor editor)
    {
        editor.CloseHeaderFooter();
        await Dialogs.SpellingDialog.ShowAsync(window, editor);
        editor.FocusDocument();
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
