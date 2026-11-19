using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// Inhalte einfügen … (Strg+Alt+V), Ausfüllen › Reihe …, and Strg+Plus / Strg+Minus (also on the number pad): whole rows or
/// columns go in or out at once, otherwise the small dialog asks where the cells move – as in Excel.
/// </summary>
public static class CalcEditCommands
{
    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        var commands = window.Commands;

        commands.Add(new OfficeCommand(CalcCommandIds.PasteSpecial, "Inhalte einfügen …", () => Editor() is { } editor && editor.Editing.Commit() ? PasteSpecialDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "paste", Shortcut = "Ctrl+Alt+V", CanExecute = () => Ready() && Editor()!.Clipboard.HasOwnContent,
            Description = "Fügt nur Werte, Formeln oder Formate ein, rechnet mit den Zielwerten, überspringt Leerzellen oder transponiert.",
            Keywords = ["inhalte einfügen", "werte einfügen", "transponieren", "addieren"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FillSeries, "Reihe …", () => Editor() is { } editor && editor.Editing.Commit() ? SeriesDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "list-ordered", CanExecute = Ready, Keywords = ["reihe", "datenreihe", "zahlenreihe", "ausfüllen", "inkrement"],
            Description = "Füllt die Markierung mit einer Zahlen- oder Datumsreihe (Inkrement, Endwert).",
        });

        foreach (var (id, shortcut, insert) in new (string, string, bool)[]
                 {
                     (CalcCommandIds.InsertByKey, "Ctrl+OemPlus", true), (CalcCommandIds.InsertByKeyPad, "Ctrl+Add", true),
                     (CalcCommandIds.DeleteByKey, "Ctrl+OemMinus", false), (CalcCommandIds.DeleteByKeyPad, "Ctrl+Subtract", false),
                 })
        {
            var inserting = insert;
            commands.Add(new OfficeCommand(id, insert ? "Zellen einfügen" : "Zellen löschen", () => Editor() is { } editor ? ByKeyAsync(window, editor, inserting) : Task.CompletedTask)
            {
                Icon = insert ? "row-insert" : "trash", Shortcut = shortcut, CanExecute = Ready, IsSearchable = false,
            });
        }
    }

    /// <summary>Strg+Plus / Strg+Minus: rows or columns at once when they are selected, else the dialog.</summary>
    private static Task ByKeyAsync(OfficeWindow window, CalcEditor editor, bool insert)
    {
        if (!editor.Editing.Commit())
        {
            return Task.CompletedTask;
        }

        var session = editor.Session;
        var range = session.Selection;
        if (range.IsWholeRows && !range.IsWholeColumns)
        {
            return editor.RunAsync(insert ? "Zeilen einfügen" : "Zeilen löschen", () =>
            {
                if (insert)
                {
                    session.InsertRows(range.FirstRow, range.RowCount);
                }
                else
                {
                    session.DeleteRows(range.FirstRow, range.RowCount);
                }
            });
        }

        if (range.IsWholeColumns && !range.IsWholeRows)
        {
            return editor.RunAsync(insert ? "Spalten einfügen" : "Spalten löschen", () =>
            {
                if (insert)
                {
                    session.InsertColumns(range.FirstColumn, range.ColumnCount);
                }
                else
                {
                    session.DeleteColumns(range.FirstColumn, range.ColumnCount);
                }
            });
        }

        return InsertDeleteCellsDialog.ShowAsync(window, editor, insert);
    }
}
