using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// The table commands: Einfügen › Tabelle (Strg+T, also Strg+L as in Excel), Start › Als Tabelle formatieren, and the contextual
/// tab Tabellenentwurf (Tabellenname, Tabellengröße ändern, In Bereich konvertieren, the style options and the styles).
/// </summary>
public static class CalcTableCommands
{
    /// <summary>The context of Tabellenentwurf (shown while the active cell is in a table).</summary>
    public const string Context = "table";

    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        TableDefinition? Table() => Editor() is { Editing.IsEditing: false } editor && editor.Session.SelectedChart is null ? editor.Session.ActiveTable : null;
        bool InTable() => Table() is not null;
        void Options(string label, Func<TableDefinition, TableDefinition> change)
        {
            if (Editor() is { } editor && Table() is { } table)
            {
                _ = editor.RunAsync(label, () => editor.Session.SetTableOptions(table.Name, change, label));
            }
        }

        var commands = window.Commands;
        commands.Add(new OfficeCommand(CalcCommandIds.InsertTable, "Tabelle", () => Editor() is { } editor ? CreateTableDialog.ShowAsync(window, editor, null) : Task.CompletedTask)
        {
            Icon = "table", Shortcut = "Ctrl+T", CanExecute = () => Ready() && !InTable(), Keywords = ["tabelle erstellen", "liste", "als tabelle"],
            Description = "Macht aus dem Bereich eine Tabelle: Überschriften mit Filtern, gebänderte Zeilen, Ergebniszeile, wächst mit.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.InsertTableAlias, "Tabelle", () => Editor() is { } editor ? CreateTableDialog.ShowAsync(window, editor, null) : Task.CompletedTask)
        {
            Icon = "table", Shortcut = "Ctrl+L", CanExecute = () => Ready() && !InTable(), IsSearchable = false,
        });
        commands.Add(new OfficeValueCommand(CalcCommandIds.FormatAsTable, "Als Tabelle formatieren", OfficeValueKind.Choice, () => Table()?.Style, value =>
        {
            if (Editor() is not { } editor || value is null)
            {
                return Task.CompletedTask;
            }

            if (Table() is { } table)
            {
                return editor.RunAsync("Tabellenformatvorlage", () => editor.Session.SetTableOptions(table.Name, current => current with { Style = value }, "Tabellenformatvorlage"));
            }

            return CreateTableDialog.ShowAsync(window, editor, value);
        })
        {
            Icon = "table", Options = [.. TableStyles.Gallery.Select(style => style.Id)], CanExecute = Ready,
            Description = "Macht aus dem Bereich eine Tabelle mit einer Formatvorlage.", Keywords = ["tabellenformat", "formatvorlage tabelle"],
        });

        // ===== Tabellenentwurf =====
        commands.Add(new OfficeValueCommand(CalcCommandIds.TableName, "Tabellenname", OfficeValueKind.Text, () => Table()?.Name, value =>
        {
            if (Editor() is { } editor && Table() is { } table && value is { Length: > 0 })
            {
                _ = editor.RunAsync("Tabelle umbenennen", () => editor.Session.RenameTable(table.Name, value));
            }
        })
        {
            Icon = "", CanExecute = InTable, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.TableResize, "Tabellengröße ändern", () => Editor() is { } editor && Table() is { } table ? ResizeTableDialog.ShowAsync(window, editor, table) : Task.CompletedTask)
        {
            Icon = "crop", CanExecute = InTable, Keywords = ["tabelle vergrößern", "bereich der tabelle"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.TableConvert, "In Bereich konvertieren", async () =>
        {
            if (Editor() is not { } editor || Table() is not { } table)
            {
                return;
            }

            if (await QuestionDialog.ConfirmAsync(window, "In Bereich konvertieren", "Möchten Sie die Tabelle in einen normalen Bereich konvertieren?", "Konvertieren"))
            {
                await editor.RunAsync("In Bereich konvertieren", () => editor.Session.ConvertTableToRange(table.Name));
            }
        })
        {
            Icon = "grid", CanExecute = InTable, Keywords = ["tabelle aufheben", "normaler bereich"],
            Description = "Macht die Tabelle wieder zu einem normalen Zellbereich; das Aussehen bleibt.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.TableHeaderRow, "Überschrift", () =>
        {
            if (Editor() is { } editor && Table() is { } table)
            {
                _ = editor.RunAsync("Überschrift", () => editor.Session.SetTableHeader(table.Name, !table.HasHeader));
            }
        })
        {
            CanExecute = InTable, IsChecked = () => Table()?.HasHeader == true, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.TableTotalRow, "Ergebniszeile", () =>
        {
            if (Editor() is { } editor && Table() is { } table)
            {
                _ = editor.RunAsync("Ergebniszeile", () => editor.Session.SetTotalsRow(table.Name, !table.HasTotals));
            }
        })
        {
            CanExecute = InTable, IsChecked = () => Table()?.HasTotals == true, Keywords = ["summe der tabelle", "ergebnis"],
            Description = "Zeigt unter der Tabelle eine Zeile mit Ergebnissen (Summe der sichtbaren Zeilen).",
        });
        foreach (var (id, label, isOn, change) in new (string, string, Func<TableDefinition, bool>, Func<TableDefinition, TableDefinition>)[]
                 {
                     (CalcCommandIds.TableBandedRows, "Gebänderte Zeilen", table => table.BandedRows, table => table with { BandedRows = !table.BandedRows }),
                     (CalcCommandIds.TableFirstColumn, "Erste Spalte", table => table.FirstColumn, table => table with { FirstColumn = !table.FirstColumn }),
                     (CalcCommandIds.TableLastColumn, "Letzte Spalte", table => table.LastColumn, table => table with { LastColumn = !table.LastColumn }),
                     (CalcCommandIds.TableBandedColumns, "Gebänderte Spalten", table => table.BandedColumns, table => table with { BandedColumns = !table.BandedColumns }),
                     (CalcCommandIds.TableFilterButton, "Schaltfläche \"Filter\"", table => table.ShowFilterButton, table => table with { ShowFilterButton = !table.ShowFilterButton }),
                 })
        {
            var test = isOn;
            var apply = change;
            var name = label;
            commands.Add(new OfficeCommand(id, label, () => Options(name, apply))
            {
                CanExecute = InTable, IsChecked = () => Table() is { } table && test(table), IsSearchable = false,
            });
        }

        commands.Add(new OfficeValueCommand(CalcCommandIds.TableStyles, "Tabellenformatvorlagen", OfficeValueKind.Choice, () => Table()?.Style, value =>
        {
            if (value is not null)
            {
                Options("Tabellenformatvorlage", table => table with { Style = value });
            }
        })
        {
            Icon = "template", Options = [.. TableStyles.Gallery.Select(style => style.Id)], CanExecute = InTable, IsSearchable = false,
        });
    }
}
