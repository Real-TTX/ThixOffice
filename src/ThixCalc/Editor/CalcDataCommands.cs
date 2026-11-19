using ThixCalc.Core.Editing;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// ThixCalc's commands of the tabs Einfügen, Formeln, Daten and Überprüfen. Shortcuts: Umschalt+F3 Funktion einfügen, Strg+F3
/// Namens-Manager, F9 Neu berechnen, Umschalt+F9 Blatt berechnen, Strg+K Link, Umschalt+F2 Notiz, Alt+Umschalt+Rechts / Links
/// Gruppieren / Gruppierung aufheben.
/// </summary>
public static class CalcDataCommands
{
    /// <summary>How many functions a category's menu of the function library lists (the most used first; the rest in the dialog).</summary>
    private const int MenuFunctions = 16;

    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        bool HasEditor() => Editor() is not null;
        void With(Action<EditingSession> action)
        {
            if (Editor() is { } editor && editor.Editing.Commit())
            {
                action(editor.Session);
            }
        }

        Task Dialog(Func<CalcEditor, Task> show) => Editor() is { } editor && editor.Editing.Commit() ? show(editor) : Task.CompletedTask;

        var commands = window.Commands;

        // ===== Einfügen =====
        commands.Add(new OfficeCommand(CalcCommandIds.Hyperlink, "Link", () => Dialog(editor => HyperlinkDialog.ShowAsync(window, editor)))
        {
            Icon = "link", Shortcut = "Ctrl+K", CanExecute = Ready,
            Description = "Verknüpft die Zelle mit einer Webseite, einer E-Mail-Adresse oder einer Stelle der Mappe.", Keywords = ["hyperlink", "verknüpfung", "url"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Comment, "Notiz", () => Dialog(editor => CommentDialog.ShowAsync(window, editor)))
        {
            Icon = "comment", Shortcut = "Shift+F2", CanExecute = Ready,
            Description = "Schreibt eine Notiz an die Zelle (ein rotes Dreieck zeigt sie).", Keywords = ["kommentar", "anmerkung"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.NewComment, "Neue Notiz", () => Dialog(editor => CommentDialog.ShowAsync(window, editor)))
        {
            Icon = "comment", CanExecute = Ready, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DeleteCommentReview, "Notiz löschen", () => With(session => session.SetComment(session.ActiveCell, null)))
        {
            Icon = "trash", CanExecute = () => Ready() && Editor()!.Session.ActiveSheet.Comments.ContainsKey(Editor()!.Session.ActiveCell),
            Keywords = ["kommentar entfernen"],
        });

        // ===== Formeln =====
        commands.Add(new OfficeCommand(CalcCommandIds.InsertFunction, "Funktion einfügen", () => Editor()?.InsertFunctionAsync() ?? Task.CompletedTask)
        {
            Icon = "sum", Shortcut = "Shift+F3", CanExecute = HasEditor,
            Description = "Sucht eine Funktion nach Name oder Beschreibung und zeigt ihre Argumente.", Keywords = ["funktion", "formel", "assistent", "fx"],
        });
        foreach (var (category, name) in FunctionHelp.Categories)
        {
            var id = $"{CalcCommandIds.FunctionLibrary}.{category.ToString().ToLowerInvariant()}";
            commands.Add(new OfficeCommand(id, name, () => Editor()?.InsertFunctionAsync() ?? Task.CompletedTask) { Icon = IconOf(category), CanExecute = HasEditor, IsSearchable = false });
        }

        foreach (var info in MenuFunctionsOf(Core.Globalization.CalcLocale.German).SelectMany(group => group.Functions))
        {
            var function = info;
            commands.Add(new OfficeCommand(FunctionId(info.Canonical), info.Name, () => Editor()?.InsertFunctionByName(function.Name))
            {
                CanExecute = HasEditor, IsSearchable = false, Description = info.Description,
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.NameManager, "Namens-Manager", () => Dialog(editor => NameManagerDialog.ShowAsync(window, editor)))
        {
            Icon = "tag", Shortcut = "Ctrl+F3", CanExecute = Ready,
            Description = "Definiert, bearbeitet und löscht Namen für Zellen, Bereiche und Formeln.", Keywords = ["namen", "bereichsnamen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DefineName, "Namen definieren", () => Dialog(editor => NameManagerDialog.DefineAsync(window, editor)))
        {
            Icon = "tag", CanExecute = Ready, Keywords = ["name", "bereich benennen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ShowFormulas, "Formeln anzeigen", () => Editor()?.SetView(view => view with { ShowFormulas = !view.ShowFormulas }))
        {
            Icon = "code", CanExecute = Ready, IsChecked = () => Editor()?.Session.ActiveSheet.View.ShowFormulas == true,
            Description = "Zeigt in jeder Zelle die Formel statt ihres Ergebnisses.", Keywords = ["formeln", "formelansicht"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.CalculateNow, "Neu berechnen", () => With(session => session.Recalculate(force: true)))
        {
            Icon = "refresh", Shortcut = "F9", CanExecute = Ready, Keywords = ["berechnen", "aktualisieren"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.CalculateSheet, "Blatt berechnen", () => With(session => session.Recalculate(force: true)))
        {
            Icon = "refresh", Shortcut = "Shift+F9", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.CalculationOptions, "Berechnungsoptionen", () => { }) { Icon = "settings", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.CalculationAutomatic, "Automatisch", () => With(session => session.SetCalculation(manual: false)))
        {
            Icon = "check", CanExecute = Ready, IsChecked = () => Editor()?.Session.Workbook.CalculationSettings.Manual == false, Keywords = ["automatisch berechnen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.CalculationManual, "Manuell", () => With(session => session.SetCalculation(manual: true)))
        {
            Icon = "pause", CanExecute = Ready, IsChecked = () => Editor()?.Session.Workbook.CalculationSettings.Manual == true, Keywords = ["manuell berechnen"],
        });

        // ===== Daten =====
        commands.Add(new OfficeCommand(CalcCommandIds.DataSortAscending, "Aufsteigend sortieren", () => With(session => CalcCommands.SortSelection(session, descending: false)))
        {
            Icon = "sort-asc", CanExecute = Ready, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DataSortDescending, "Absteigend sortieren", () => With(session => CalcCommands.SortSelection(session, descending: true)))
        {
            Icon = "sort-desc", CanExecute = Ready, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DataSort, "Sortieren", () => Dialog(editor => SortDialog.ShowAsync(window, editor)))
        {
            Icon = "sort", CanExecute = Ready, Keywords = ["sortieren", "ordnen", "reihenfolge"],
            Description = "Sortiert die Liste nach einer oder mehreren Spalten.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DataFilter, "Filtern", () => With(session => session.ToggleAutoFilter()))
        {
            Icon = "filter", CanExecute = Ready, IsChecked = () => Editor()?.Session.ActiveSheet.AutoFilter is not null, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DataFilterClear, "Löschen", () => With(session => session.ClearFilters()))
        {
            Icon = "x", CanExecute = () => Ready() && Editor()!.Session.ActiveSheet.AutoFilter is { Columns.Count: > 0 }, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.TextToColumns, "Text in Spalten", () => Dialog(editor => TextToColumnsDialog.ShowAsync(window, editor)))
        {
            Icon = "columns", CanExecute = Ready, Keywords = ["trennen", "aufteilen", "spalten"],
            Description = "Teilt den Text einer Spalte an Trennzeichen auf mehrere Spalten auf.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.RemoveDuplicates, "Duplikate entfernen", () => Dialog(editor => RemoveDuplicatesDialog.ShowAsync(window, editor)))
        {
            Icon = "list-checks", CanExecute = Ready, Keywords = ["doppelte", "duplikate", "eindeutig"],
            Description = "Entfernt Zeilen, deren Werte in den gewählten Spalten schon weiter oben stehen.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Validation, "Datenüberprüfung", () => Dialog(editor => ValidationDialog.ShowAsync(window, editor)))
        {
            Icon = "check-square", CanExecute = Ready, Keywords = ["gültigkeit", "dropdown", "liste", "eingabe prüfen"],
            Description = "Legt fest, welche Werte in die Zellen eingegeben werden dürfen (z. B. eine Auswahlliste).",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Group, "Gruppieren", () => With(Group))
        {
            Icon = "layers", Shortcut = "Alt+Shift+Right", CanExecute = Ready, Keywords = ["gliederung", "gruppe"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Ungroup, "Gruppierung aufheben", () => With(session => Group(session, group: false)))
        {
            Icon = "layers", Shortcut = "Alt+Shift+Left", CanExecute = Ready,
        });
    }

    /// <summary>Gruppieren: whole columns group columns, everything else the rows of the selection.</summary>
    private static void Group(EditingSession session) => Group(session, group: true);

    private static void Group(EditingSession session, bool group)
    {
        var range = session.Selection;
        if (range.IsWholeColumns && !range.IsWholeRows)
        {
            session.Group(rows: false, range.FirstColumn, range.LastColumn, group);
        }
        else
        {
            session.Group(rows: true, range.FirstRow, range.LastRow, group);
        }
    }

    /// <summary>The command of a function in a library menu.</summary>
    public static string FunctionId(string canonical) => $"calc.function.{canonical}";

    /// <summary>The library's menus: per category the most used functions (those with German help first).</summary>
    public static IReadOnlyList<(FunctionCategory Category, IReadOnlyList<FunctionInfo> Functions)> MenuFunctionsOf(Core.Globalization.CalcLocale locale)
    {
        var all = FunctionHelp.All(locale);
        return FunctionHelp.Categories
            .Select(entry => (entry.Category, (IReadOnlyList<FunctionInfo>)all
                .Where(info => info.Category == entry.Category)
                .OrderBy(info => info.Description.StartsWith("Funktion der Kategorie", StringComparison.Ordinal) ? 1 : 0)
                .ThenBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
                .Take(MenuFunctions)
                .OrderBy(info => info.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList()))
            .ToList();
    }

    private static string IconOf(FunctionCategory category) => category switch
    {
        FunctionCategory.Financial => "currency",
        FunctionCategory.DateTime => "calendar",
        FunctionCategory.Math => "calculator",
        FunctionCategory.Statistical => "chart-bar",
        FunctionCategory.Lookup => "search",
        FunctionCategory.Database => "database",
        FunctionCategory.Text => "type",
        FunctionCategory.Logical => "check-circle",
        FunctionCategory.Information => "info",
        _ => "wrench",
    };
}
