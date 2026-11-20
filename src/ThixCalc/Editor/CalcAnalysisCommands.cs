using ThixCalc.Core.Editing;
using ThixCalc.Dialogs;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// Daten › Gliederung (Teilergebnis, Detail anzeigen/ausblenden) and Prognose (Was-wäre-wenn › Zielwertsuche), Formeln ›
/// Formelüberwachung (Spur zum Vorgänger/Nachfolger, Pfeile entfernen, Fehlerüberprüfung, Formel auswerten) and Definierte Namen
/// (In Formel verwenden F3, Aus Auswahl erstellen Strg+Umschalt+F3), Start › Suchen und Auswählen › Inhalte auswählen and Nur
/// sichtbare Zellen (Alt+;), Überprüfen › Blatt schützen / Arbeitsmappe schützen, Format › Zelle sperren.
/// </summary>
public static class CalcAnalysisCommands
{
    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        bool HasEditor() => Editor() is not null;
        Task Run(string label, Action<EditingSession> action) =>
            Editor() is { } editor && editor.Editing.Commit() ? editor.RunAsync(label, () => action(editor.Session)) : Task.CompletedTask;
        Task Dialog(Func<CalcEditor, Task> show) => Editor() is { } editor && editor.Editing.Commit() ? show(editor) : Task.CompletedTask;

        var commands = window.Commands;
        commands.Add(new OfficeCommand(CalcCommandIds.Subtotals, "Teilergebnis", () => Dialog(editor => SubtotalDialog.ShowAsync(window, editor)))
        {
            Icon = "sum", CanExecute = Ready, Keywords = ["teilergebnisse", "zwischensumme", "gruppen summieren"],
            Description = "Fügt unter jeder Gruppe gleicher Werte eine Zeile mit TEILERGEBNIS ein und gliedert die Liste.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ShowDetail, "Detail anzeigen", () => Run("Detail einblenden", session => session.ShowDetail(true, session.ActiveCell.Row, show: true)))
        {
            Icon = "plus", CanExecute = Ready, Keywords = ["gruppe aufklappen", "einblenden"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.HideDetail, "Detail ausblenden", () => Run("Detail ausblenden", session => session.ShowDetail(true, session.ActiveCell.Row, show: false)))
        {
            Icon = "minus", CanExecute = Ready, Keywords = ["gruppe zuklappen", "ausblenden"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.WhatIf, "Was-wäre-wenn-Analyse", () => { }) { Icon = "help-circle", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.GoalSeek, "Zielwertsuche", () => Dialog(editor => GoalSeekDialog.ShowAsync(window, editor)))
        {
            Icon = "help-circle", CanExecute = Ready, Keywords = ["was wäre wenn", "zielwert", "rückwärts rechnen"],
            Description = "Ändert eine Zelle so lange, bis eine Formel den gewünschten Wert ergibt.",
        });

        // ===== Formelüberwachung =====
        commands.Add(new OfficeCommand(CalcCommandIds.TracePrecedents, "Spur zum Vorgänger", () => Run("Spur zum Vorgänger", session => session.TracePrecedents()).ContinueWith(_ => Editor()?.Surface.InvalidateVisual(), TaskScheduler.FromCurrentSynchronizationContext()))
        {
            Icon = "arrow-right", CanExecute = Ready, Keywords = ["vorgänger", "bezüge anzeigen", "formel herkunft"],
            Description = "Zeigt Pfeile von den Zellen, auf die sich die Formel der aktiven Zelle bezieht.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.TraceDependents, "Spur zum Nachfolger", () => Run("Spur zum Nachfolger", session => session.TraceDependents()).ContinueWith(_ => Editor()?.Surface.InvalidateVisual(), TaskScheduler.FromCurrentSynchronizationContext()))
        {
            Icon = "arrow-down-right", CanExecute = Ready, Keywords = ["nachfolger", "abhängige formeln"],
            Description = "Zeigt Pfeile zu den Formeln, die sich auf die aktive Zelle beziehen.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.RemoveArrows, "Pfeile entfernen", () =>
        {
            Editor()?.Session.RemoveArrows();
            Editor()?.Surface.InvalidateVisual();
        })
        {
            Icon = "x", CanExecute = () => Editor()?.Session.TraceArrows.Count > 0, Keywords = ["spurpfeile entfernen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ErrorChecking, "Fehlerüberprüfung", async () =>
        {
            if (Editor() is not { } editor || !editor.Editing.Commit())
            {
                return;
            }

            if (editor.Session.NextError() is not { } cell)
            {
                await QuestionDialog.ShowMessageAsync(window, "Fehlerüberprüfung", "Die Fehlerüberprüfung für das gesamte Blatt ist abgeschlossen. Es wurden keine Fehler gefunden.");
                return;
            }

            editor.ScrollIntoView(cell);
            var value = editor.Session.ActiveSheet.GetValue(cell);
            var formula = editor.Session.FormulaBarText;
            await QuestionDialog.ShowMessageAsync(window, "Fehlerüberprüfung", $"Fehler in Zelle {cell}{Environment.NewLine}{formula}{Environment.NewLine}{Environment.NewLine}{EditingSession.ExplainError(value.Error)}");
        })
        {
            Icon = "alert", CanExecute = Ready, Keywords = ["fehler suchen", "formelfehler"],
            Description = "Springt zur nächsten Zelle mit einem Fehler und erklärt ihn.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.EvaluateFormula, "Formel auswerten", () => Dialog(editor => EvaluateFormulaDialog.ShowAsync(window, editor)))
        {
            Icon = "code", CanExecute = () => Ready() && Editor()!.Session.ActiveSheet.GetFormula(Editor()!.Session.ActiveCell.Row, Editor()!.Session.ActiveCell.Column) is not null,
            Keywords = ["formelauswertung", "schrittweise berechnen"],
            Description = "Zeigt die Formel der aktiven Zelle Schritt für Schritt bis zum Ergebnis.",
        });

        // ===== Definierte Namen =====
        commands.Add(new OfficeCommand(CalcCommandIds.UseInFormula, "In Formel verwenden", () => Editor() is { } editor ? PasteNameDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "tag", Shortcut = "F3", CanExecute = () => HasEditor() && Editor()!.Session.Workbook.Names.Count > 0, Keywords = ["namen einfügen", "name in formel"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.CreateNames, "Aus Auswahl erstellen", () => Dialog(editor => CreateNamesDialog.ShowAsync(window, editor)))
        {
            Icon = "tag", Shortcut = "Ctrl+Shift+F3", CanExecute = Ready, Keywords = ["namen aus auswahl", "namen erstellen"],
            Description = "Erstellt Namen aus den Beschriftungen der obersten Zeile oder linken Spalte der Auswahl.",
        });

        // ===== Suchen und Auswählen =====
        commands.Add(new OfficeCommand(CalcCommandIds.SelectSpecial, "Inhalte auswählen …", () => Dialog(editor => SelectSpecialDialog.ShowAsync(window, editor)))
        {
            Icon = "list-checks", CanExecute = Ready, Keywords = ["leerzellen markieren", "formeln markieren", "gehe zu inhalte"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.SelectVisible, "Nur sichtbare Zellen", () => Run("Nur sichtbare Zellen", session => session.SelectSpecial(SpecialCells.VisibleCells)))
        {
            Icon = "eye", CanExecute = Ready, Keywords = ["sichtbare zellen markieren"],
        });

        // ===== Schützen =====
        commands.Add(new OfficeCommand(CalcCommandIds.ProtectSheet, "Blatt schützen", () => Dialog(editor => editor.Session.IsSheetProtected ? PasswordDialog.UnprotectSheetAsync(window, editor) : ProtectSheetDialog.ShowAsync(window, editor)))
        {
            Icon = "lock", CanExecute = Ready, IsChecked = () => Editor()?.Session.IsSheetProtected == true, Keywords = ["blattschutz", "schützen", "kennwort", "blattschutz aufheben"],
            Description = "Schützt gesperrte Zellen vor Änderungen (Blattschutz aufheben, wenn das Blatt geschützt ist).",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ProtectWorkbook, "Arbeitsmappe schützen", () => Dialog(editor => editor.Session.Workbook.IsStructureProtected ? PasswordDialog.UnprotectWorkbookAsync(window, editor) : PasswordDialog.ProtectWorkbookAsync(window, editor)))
        {
            Icon = "shield", CanExecute = Ready, IsChecked = () => Editor()?.Session.Workbook.IsStructureProtected == true, Keywords = ["mappe schützen", "struktur schützen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.LockCell, "Zelle sperren", () => Run("Zelle sperren", session =>
        {
            var locked = session.ActiveStyle.Protection.Locked;
            session.ApplyStyle(style => style with { Protection = style.Protection with { Locked = !locked } }, "Zelle sperren");
        }))
        {
            Icon = "lock", CanExecute = Ready, IsChecked = () => Editor()?.Session.ActiveStyle.Protection.Locked == true, Keywords = ["zellschutz", "gesperrt"],
        });
    }
}
