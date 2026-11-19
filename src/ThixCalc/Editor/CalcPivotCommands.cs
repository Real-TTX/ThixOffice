using ThixCalc.Core.Model.Pivot;
using ThixCalc.Dialogs;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// The PivotTable commands: Einfügen › PivotTable, and the contextual tabs PivotTable-Analyse (Name, Aktualisieren Alt+F5, Alle
/// aktualisieren Strg+Alt+F5, Datenquelle ändern, Feldeinstellungen, Gruppieren, Löschen, Verschieben, Feldliste, Details) and
/// Entwurf (Teilergebnisse, Gesamtergebnisse, Berichtslayout, Zeilen-/Spaltenüberschriften, gebänderte Zeilen/Spalten,
/// Formatvorlagen). The logic is EditingSession's; commands only call it.
/// </summary>
public static class CalcPivotCommands
{
    /// <summary>The context of the PivotTable tabs (shown while the active cell is in a PivotTable).</summary>
    public const string Context = "pivot";

    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        PivotTable? Pivot() => Editor() is { Editing.IsEditing: false } editor && editor.Session.SelectedChart is null ? editor.Session.ActivePivot : null;
        bool InPivot() => Pivot() is not null;
        bool Editable() => Pivot() is { Unsupported: null };
        void Change(string label, Func<PivotTable, PivotTable> change)
        {
            if (Editor() is { } editor && Pivot() is { } pivot)
            {
                _ = editor.RunAsync(label, () => editor.Session.SetPivotOptions(pivot, change, label));
            }
        }

        var commands = window.Commands;
        commands.Add(new OfficeCommand(CalcCommandIds.InsertPivot, "PivotTable", () => Editor() is { } editor ? InsertPivotDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "table", CanExecute = () => Ready() && !InPivot(), Keywords = ["pivot", "pivottabelle", "kreuztabelle", "auswertung", "zusammenfassen"],
            Description = "Fasst Daten in einer PivotTable zusammen: Felder in Zeilen, Spalten, Werte und Filter ziehen.",
        });

        // ===== PivotTable-Analyse =====
        commands.Add(new OfficeValueCommand(CalcCommandIds.PivotName, "PivotTable-Name", OfficeValueKind.Text, () => Pivot()?.Name, value =>
        {
            if (Editor() is { } editor && Pivot() is { } pivot && value is { Length: > 0 } name && name != pivot.Name)
            {
                if (editor.Session.FindPivot(name) is not null)
                {
                    _ = QuestionDialog.ShowMessageAsync(window, "PivotTable-Name", "Eine PivotTable mit diesem Namen gibt es schon.");
                    return;
                }

                _ = editor.RunAsync("PivotTable umbenennen", () => editor.Session.SetPivotOptions(pivot, table => table with { Name = name }, "PivotTable umbenennen"));
            }
        })
        {
            CanExecute = Editable, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotRefresh, "Aktualisieren", () => Editor() is { } editor && Pivot() is { } pivot ? editor.RunAsync("Aktualisieren", () => editor.Session.RefreshPivot(pivot)) : Task.CompletedTask)
        {
            Icon = "refresh", Shortcut = "Alt+F5", CanExecute = Editable, Keywords = ["pivot aktualisieren", "neu laden"],
            Description = "Liest die Datenquelle der PivotTable neu ein.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotRefreshAll, "Alle aktualisieren", () => Editor() is { } editor ? editor.RunAsync("Alle aktualisieren", editor.Session.RefreshAllPivots) : Task.CompletedTask)
        {
            Icon = "refresh", Shortcut = "Ctrl+Alt+F5", CanExecute = () => Ready() && Editor()!.Session.Workbook.PivotCaches.Count > 0, Keywords = ["alle pivottabellen aktualisieren"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotChangeSource, "Datenquelle ändern", () => Editor() is { } editor && Pivot() is { } pivot ? PivotReferenceDialog.SourceAsync(window, editor, pivot) : Task.CompletedTask)
        {
            Icon = "database", CanExecute = Editable, Keywords = ["quelle der pivottabelle", "bereich ändern"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotFieldSettings, "Feldeinstellungen", () => Editor()?.ActiveFieldSettingsAsync() ?? Task.CompletedTask)
        {
            Icon = "settings", CanExecute = () => Editable() && Editor()!.ActivePivotField() is not null,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotValueSettings, "Wertfeldeinstellungen", () => Editor()?.ActiveValueSettingsAsync() ?? Task.CompletedTask)
        {
            Icon = "sum", CanExecute = () => Editable() && Editor()!.ActivePivotValue() is not null, Keywords = ["werte zusammenfassen nach", "werte anzeigen als"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotGroup, "Gruppieren", () => Editor() is { } editor && Pivot() is { } pivot && editor.ActivePivotField() is { } field ? PivotGroupDialog.ShowAsync(window, editor, pivot, field) : Task.CompletedTask)
        {
            Icon = "layers", CanExecute = () => Editable() && Editor()!.ActivePivotField() is { } field && Editor()!.Session.Workbook.FindPivotCache(Pivot()!.CacheId)!.Fields[field].IsNumeric,
            Keywords = ["nach monaten gruppieren", "jahre", "quartale"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotUngroup, "Gruppierung aufheben", () =>
        {
            if (Editor() is { } editor && Pivot() is { } pivot && editor.ActivePivotField() is { } field)
            {
                _ = editor.RunAsync("Gruppierung aufheben", () => editor.Session.UngroupPivotField(pivot, field));
            }
        })
        {
            Icon = "layers", CanExecute = () => Editable() && Editor()!.ActivePivotField() is { } field && Pivot()!.Fields[field].IsGrouped,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotDelete, "PivotTable löschen", async () =>
        {
            if (Editor() is { } editor && Pivot() is { } pivot
                && await QuestionDialog.ConfirmAsync(window, "PivotTable löschen", $"Möchten Sie die PivotTable „{pivot.Name}“ löschen?", "Löschen"))
            {
                await editor.RunAsync("PivotTable löschen", () => editor.Session.DeletePivot(pivot));
            }
        })
        {
            Icon = "trash", CanExecute = InPivot,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotMove, "PivotTable verschieben", () => Editor() is { } editor && Pivot() is { } pivot ? PivotReferenceDialog.MoveAsync(window, editor, pivot) : Task.CompletedTask)
        {
            Icon = "external", CanExecute = Editable,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotFieldList, "Feldliste", () => Editor()?.ShowFieldList(!Editor()!.FieldListVisible))
        {
            Icon = "sidebar", CanExecute = InPivot, IsChecked = () => Editor()?.FieldListVisible == true, Keywords = ["pivottable-felder", "felder anzeigen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PivotDetails, "Details anzeigen", () => Editor() is { } editor ? editor.RunAsync("Details anzeigen", () => editor.Session.ShowPivotDetails(editor.Session.ActiveCell)) : Task.CompletedTask)
        {
            Icon = "table", CanExecute = () => Editable() && Editor()!.ActivePivotValue() is not null, IsSearchable = false,
        });
        foreach (var (id, label, descending) in new[] { (CalcCommandIds.PivotSortAscending, "Von A bis Z sortieren", false), (CalcCommandIds.PivotSortDescending, "Von Z bis A sortieren", true) })
        {
            var down = descending;
            commands.Add(new OfficeCommand(id, label, () =>
            {
                if (Editor() is not { } editor || Pivot() is not { } pivot)
                {
                    return;
                }

                if (editor.ActivePivotValue() is { } data && editor.ActivePivotRowField() is { } rowField)
                {
                    _ = editor.RunAsync("Sortieren", () => editor.Session.ChangePivotField(pivot, rowField, field => field with { Sort = new PivotSort(down, data) }, "Sortieren"));
                }
                else if (editor.ActivePivotField() is { } field)
                {
                    _ = editor.RunAsync("Sortieren", () => editor.Session.ChangePivotField(pivot, field, settings => settings with { Sort = new PivotSort(down) }, "Sortieren"));
                }
            })
            {
                Icon = descending ? "sort-desc" : "sort-asc", CanExecute = Editable, IsSearchable = false,
            });
        }

        // ===== Entwurf =====
        commands.Add(new OfficeCommand(CalcCommandIds.PivotSubtotals, "Teilergebnisse", () => { }) { Icon = "list", CanExecute = Editable, IsSearchable = false });
        foreach (var (suffix, label, on, top) in new[] { ("none", "Teilergebnisse nicht anzeigen", false, true), ("bottom", "Alle Teilergebnisse unten in der Gruppe anzeigen", true, false), ("top", "Alle Teilergebnisse oben in der Gruppe anzeigen", true, true) })
        {
            var (show, atTop, name) = (on, top, label);
            commands.Add(new OfficeCommand($"{CalcCommandIds.PivotSubtotals}.{suffix}", label, () => Change("Teilergebnisse", table => table with
            {
                Fields = [.. table.Fields.Select(field => field with { Subtotals = show })],
                SubtotalsAtTop = atTop,
            }))
            {
                CanExecute = Editable, IsSearchable = false,
                IsChecked = () => Pivot() is { } pivot && (show ? pivot.RowFields.Any(field => field >= 0 && pivot.Fields[field].Subtotals) && pivot.SubtotalsAtTop == atTop : pivot.RowFields.All(field => field < 0 || !pivot.Fields[field].Subtotals)),
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.PivotGrandTotals, "Gesamtergebnisse", () => { }) { Icon = "sum", CanExecute = Editable, IsSearchable = false });
        foreach (var (suffix, label, rows, columns) in new[] { ("off", "Für Zeilen und Spalten deaktiviert", false, false), ("both", "Für Zeilen und Spalten aktiviert", true, true), ("rows", "Nur für Zeilen aktiviert", true, false), ("columns", "Nur für Spalten aktiviert", false, true) })
        {
            var (forRows, forColumns) = (rows, columns);
            commands.Add(new OfficeCommand($"{CalcCommandIds.PivotGrandTotals}.{suffix}", label, () => Change("Gesamtergebnisse", table => table with { RowGrandTotals = forRows, ColumnGrandTotals = forColumns }))
            {
                CanExecute = Editable, IsSearchable = false, IsChecked = () => Pivot() is { } pivot && pivot.RowGrandTotals == forRows && pivot.ColumnGrandTotals == forColumns,
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.PivotLayout, "Berichtslayout", () => { }) { Icon = "grid", CanExecute = Editable, IsSearchable = false });
        foreach (var (suffix, label, layout) in new[] { ("compact", "In Kurzformat anzeigen", PivotLayout.Compact), ("outline", "In Gliederungsformat anzeigen", PivotLayout.Outline), ("tabular", "In Tabellenformat anzeigen", PivotLayout.Tabular) })
        {
            var chosen = layout;
            commands.Add(new OfficeCommand($"{CalcCommandIds.PivotLayout}.{suffix}", label, () => Change("Berichtslayout", table => table with { Layout = chosen }))
            {
                CanExecute = Editable, IsSearchable = false, IsChecked = () => Pivot()?.Layout == chosen,
            });
        }

        commands.Add(new OfficeCommand($"{CalcCommandIds.PivotLayout}.repeat", "Alle Elementnamen wiederholen", () => Change("Berichtslayout", table => table with { RepeatLabels = true }))
        {
            CanExecute = Editable, IsSearchable = false, IsChecked = () => Pivot()?.RepeatLabels == true,
        });
        commands.Add(new OfficeCommand($"{CalcCommandIds.PivotLayout}.norepeat", "Elementnamen nicht wiederholen", () => Change("Berichtslayout", table => table with { RepeatLabels = false }))
        {
            CanExecute = Editable, IsSearchable = false, IsChecked = () => Pivot()?.RepeatLabels == false,
        });
        foreach (var (id, label, isOn, toggle) in new (string, string, Func<PivotTable, bool>, Func<PivotTable, PivotTable>)[]
                 {
                     (CalcCommandIds.PivotRowHeaders, "Zeilenüberschriften", table => table.RowHeaders, table => table with { RowHeaders = !table.RowHeaders }),
                     (CalcCommandIds.PivotColumnHeaders, "Spaltenüberschriften", table => table.ColumnHeaders, table => table with { ColumnHeaders = !table.ColumnHeaders }),
                     (CalcCommandIds.PivotBandedRows, "Gebänderte Zeilen", table => table.BandedRows, table => table with { BandedRows = !table.BandedRows }),
                     (CalcCommandIds.PivotBandedColumns, "Gebänderte Spalten", table => table.BandedColumns, table => table with { BandedColumns = !table.BandedColumns }),
                 })
        {
            var (name, test, apply) = (label, isOn, toggle);
            commands.Add(new OfficeCommand(id, label, () => Change(name, apply))
            {
                CanExecute = Editable, IsChecked = () => Pivot() is { } pivot && test(pivot), IsSearchable = false,
            });
        }

        commands.Add(new OfficeValueCommand(CalcCommandIds.PivotStyles, "PivotTable-Formatvorlagen", OfficeValueKind.Choice, () => Pivot()?.Style, value =>
        {
            if (value is not null)
            {
                Change("PivotTable-Formatvorlage", table => table with { Style = value });
            }
        })
        {
            Icon = "template", Options = [.. PivotStyles.Gallery.Select(style => style.Id)], CanExecute = Editable, IsSearchable = false,
        });
    }
}
