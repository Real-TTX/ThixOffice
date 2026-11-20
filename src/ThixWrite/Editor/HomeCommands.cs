using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>
/// Start › Absatz: Sortieren, Rahmen (menu and "Rahmen und Schattierung …"), Schattierung, Mehrstufige Liste with "Listenebene ändern";
/// Entwurf › Seitenränder; Word's keys Strg+Umschalt+Q/D, Strg+Plus/#, Strg+0, Strg+T/Strg+Umschalt+T, Strg+Q.
/// </summary>
public static class HomeCommands
{
    /// <summary>The border menu: id, label, borders (null: Kein Rahmen).</summary>
    public static readonly IReadOnlyList<(string Id, string Label, Func<BorderSet?> Borders)> BorderChoices =
    [
        ("bottom", "Rahmenlinie unten", () => new BorderSet(Bottom: new BorderLine("single", 0.5, "auto", 1))),
        ("top", "Rahmenlinie oben", () => new BorderSet(Top: new BorderLine("single", 0.5, "auto", 1))),
        ("box", "Rahmenlinien außen", () => BorderSet.All(new BorderLine("single", 0.5, "auto", 1)) with { InsideHorizontal = null, InsideVertical = null }),
        ("none", "Kein Rahmen", () => null),
    ];

    /// <summary>The gallery of Mehrstufige Liste: id, label.</summary>
    public static readonly IReadOnlyList<(string Id, string Label)> MultilevelChoices =
        [("outline", "1. 1.1. 1.1.1."), ("parenthesis", "1) a) i)"), ("bullets", "• o ▪")];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;
        void With(Action<EditingSession> action)
        {
            if (Editor() is { } editor)
            {
                action(editor.Session);
            }
        }

        var commands = window.Commands;
        commands.Add(new OfficeCommand(WriteCommandIds.Sort, "Sortieren", () => Editor() is { } editor ? SortDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "sort", CanExecute = HasEditor,
            Description = "Sortiert die markierten Absätze oder die Zeilen der Tabelle – alphabetisch, nach Zahl oder Datum.", Keywords = ["sortieren", "alphabetisch", "ordnen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Borders, "Rahmen", () => Task.CompletedTask)
        {
            Icon = "borders", CanExecute = HasEditor,
            Description = "Linien um die markierten Absätze.", Keywords = ["rahmen", "linie", "absatzrahmen"],
        });
        foreach (var (id, label, borders) in BorderChoices)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.Borders}.{id}", label, () => With(session => session.SetParagraphBorders(borders())))
            {
                Icon = "borders", CanExecute = HasEditor, IsSearchable = false,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.BordersDialog, "Rahmen und Schattierung …", () => Editor() is { } editor ? BordersDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "borders", CanExecute = HasEditor,
            Description = "Rahmen, Seitenrand und Schattierung im Dialog.", Keywords = ["rahmen und schattierung", "seitenrand", "schattierung"],
        });
        commands.Add(new OfficeValueCommand(WriteCommandIds.ParagraphShading, "Schattierung", OfficeValueKind.Color,
            () => Editor()?.Session is { } session && session.Document.ParagraphAt(session.Selection.Start.Block) is { } paragraph
                ? session.Resolver.ResolveParagraph(paragraph).ShadingFill is { } fill ? "#" + fill : null
                : null,
            value => With(session => session.SetParagraphShading(string.IsNullOrEmpty(value) ? null : value)))
        {
            Icon = "fill", CanExecute = HasEditor,
            Description = "Färbt den Hintergrund der markierten Absätze.", Keywords = ["schattierung", "hintergrund", "absatzfarbe"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PageBorders, "Seitenränder", () => Editor() is { } editor ? BordersDialog.ShowAsync(window, editor, BordersDialog.PageTab) : Task.CompletedTask)
        {
            Icon = "margins", CanExecute = HasEditor,
            Description = "Ein Rahmen um jede Seite des Abschnitts.", Keywords = ["seitenrand", "seitenrahmen", "rahmen um die seite"],
        });

        commands.Add(new OfficeCommand(WriteCommandIds.Multilevel, "Liste mit mehreren Ebenen", () => Task.CompletedTask)
        {
            Icon = "list-ordered", CanExecute = HasEditor,
            Description = "Eine Liste mit Nummern für jede Ebene (1., 1.1., 1.1.1. …).", Keywords = ["mehrstufige liste", "gliederung", "ebenen"],
        });
        foreach (var (id, label) in MultilevelChoices)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.Multilevel}.{id}", label, () => With(session => session.ApplyMultilevelList(id)))
            {
                Icon = "list-ordered", CanExecute = HasEditor, IsSearchable = false,
            });
        }

        for (var level = 1; level <= 9; level++)
        {
            var zeroBased = level - 1;
            commands.Add(new OfficeCommand($"{WriteCommandIds.ListLevel}.{level}", $"Listenebene {level}", () => With(session => session.SetListLevel(zeroBased)))
            {
                Icon = "indent", IsSearchable = false, CanExecute = () => Editor()?.Session.CurrentListLevel is not null,
                IsChecked = () => Editor()?.Session.CurrentListLevel == zeroBased,
            });
        }

        void Key(string id, string label, string shortcut, string? mac, Action<EditingSession> action) =>
            commands.Add(new OfficeCommand(id, label, () => With(action)) { Shortcut = shortcut, MacShortcut = mac, CanExecute = HasEditor, IsSearchable = false });

        Key(WriteCommandIds.KeySmallCaps, "Kapitälchen", "Ctrl+Shift+Q", "Ctrl+Shift+K", session => session.ToggleSmallCaps());
        Key(WriteCommandIds.KeyDoubleUnderline, "Doppelt unterstrichen", "Ctrl+Shift+D", null, session => session.ToggleDoubleUnderline());
        Key(WriteCommandIds.KeySuperscript, "Hochgestellt", "Ctrl+OemPlus", "Ctrl+Shift+OemPlus", session => session.ToggleSuperscript());
        Key(WriteCommandIds.KeySubscript, "Tiefgestellt", "Ctrl+OemQuestion", "Ctrl+OemPlus", session => session.ToggleSubscript());
        Key(WriteCommandIds.KeySpaceBefore, "Abstand vor", "Ctrl+D0", null, session => session.ToggleSpaceBefore());
        Key(WriteCommandIds.KeyHanging, "Hängender Einzug", "Ctrl+T", null, session => session.ChangeHangingIndent(1));
        Key(WriteCommandIds.KeyUnhang, "Hängenden Einzug verkleinern", "Ctrl+Shift+T", null, session => session.ChangeHangingIndent(-1));
        Key(WriteCommandIds.KeyResetParagraph, "Absatzformat zurücksetzen", "Ctrl+Q", "Ctrl+Alt+Q", session => session.ResetParagraphFormatting());
    }
}
