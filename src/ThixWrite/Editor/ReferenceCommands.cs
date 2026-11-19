using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>Referenzen: Fußnote einfügen (Strg+Alt+F), Endnote einfügen (Strg+Alt+D), Nächste Fußnote.</summary>
public static class ReferenceCommands
{
    /// <summary>The entries of "Nächste Fußnote": (id suffix, label, kind, forward).</summary>
    public static IReadOnlyList<(string Id, string Label, NoteKind Kind, bool Forward)> NoteSteps { get; } =
    [
        ("footnote.next", "Nächste Fußnote", NoteKind.Footnote, true),
        ("footnote.previous", "Vorherige Fußnote", NoteKind.Footnote, false),
        ("endnote.next", "Nächste Endnote", NoteKind.Endnote, true),
        ("endnote.previous", "Vorherige Endnote", NoteKind.Endnote, false),
    ];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool InBody() => Editor() is { IsEditingHeaderFooter: false };
        bool HasNotes() => Editor() is { } editor && NoteNumbering.All(editor.Document.Content).Any();

        var commands = window.Commands;
        commands.Add(new OfficeCommand(WriteCommandIds.InsertFootnote, "Fußnote einfügen", () => Editor()?.InsertNote(NoteKind.Footnote))
        {
            Icon = "footnote", Shortcut = "Ctrl+Alt+F", MacShortcut = "Ctrl+Alt+Shift+F", CanExecute = InBody,
            Description = "Fügt an der Einfügemarke eine Fußnote ein – ihr Text steht unten auf der Seite.",
            Keywords = ["fußnote", "anmerkung", "quelle", "fussnote", "footnote"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.InsertEndnote, "Endnote einfügen", () => Editor()?.InsertNote(NoteKind.Endnote))
        {
            Icon = "list-ordered", Shortcut = "Ctrl+Alt+D", MacShortcut = "Ctrl+Alt+E", CanExecute = InBody,
            Description = "Fügt an der Einfügemarke eine Endnote ein – ihr Text steht am Ende des Dokuments.",
            Keywords = ["endnote", "anmerkung", "quelle", "literatur"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.NextFootnote, "Nächste Fußnote", () => Step(Editor(), NoteKind.Footnote, true))
        {
            Icon = "arrow-down", CanExecute = HasNotes,
            Description = "Springt zur nächsten (oder vorherigen) Fuß- oder Endnote im Text.",
            Keywords = ["nächste fußnote", "vorherige fußnote", "endnote", "springen"],
        });
        foreach (var (id, label, kind, forward) in NoteSteps)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.NextFootnote}.{id}", label, () => Step(Editor(), kind, forward))
            {
                Icon = forward ? "arrow-down" : "arrow-up", CanExecute = HasNotes, IsSearchable = false,
            });
        }
    }

    private static void Step(WriteEditor? editor, NoteKind kind, bool forward)
    {
        if (editor is null)
        {
            return;
        }

        editor.CloseHeaderFooter();
        editor.Document.Session.NextNote(kind, forward);
        editor.FocusDocument();
    }
}
