using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>
/// The commands of tables: Einfügen › Tabelle (the grid of the ribbon, the dialog), the contextual tabs Tabellenentwurf (Rahmen,
/// Schattierung) and Layout (Auswählen, Löschen, Einfügen, Zusammenführen, Zellengröße, Ausrichtung, Überschriften wiederholen).
/// They act on the editor's session; the ones of the contextual tabs only while the caret is in a table.
/// </summary>
public static class TableCommands
{
    /// <summary>The context of the contextual tabs (shown while the caret is in a table).</summary>
    public const string Context = "table";

    /// <summary>The borders menu, in Word's order: (id suffix, label, choice).</summary>
    public static IReadOnlyList<(string Id, string Label, BorderChoice Choice)> BorderChoices { get; } =
    [
        ("bottom", "Rahmenlinie unten", BorderChoice.Bottom),
        ("top", "Rahmenlinie oben", BorderChoice.Top),
        ("left", "Rahmenlinie links", BorderChoice.Left),
        ("right", "Rahmenlinie rechts", BorderChoice.Right),
        ("none", "Kein Rahmen", BorderChoice.None),
        ("all", "Alle Rahmenlinien", BorderChoice.All),
        ("outside", "Rahmenlinien außen", BorderChoice.Outside),
        ("inside", "Rahmenlinien innen", BorderChoice.Inside),
        ("insideH", "Innere horizontale Rahmenlinie", BorderChoice.InsideHorizontal),
        ("insideV", "Innere vertikale Rahmenlinie", BorderChoice.InsideVertical),
    ];

    /// <summary>The nine alignments of Layout › Ausrichtung: (id suffix, label, vertical, horizontal).</summary>
    public static IReadOnlyList<(string Id, string Label, CellVerticalAlignment Vertical, ParagraphAlignment Horizontal)> CellAlignments { get; } =
    [
        ("topLeft", "Oben links ausrichten", CellVerticalAlignment.Top, ParagraphAlignment.Left),
        ("topCenter", "Oben zentriert ausrichten", CellVerticalAlignment.Top, ParagraphAlignment.Center),
        ("topRight", "Oben rechts ausrichten", CellVerticalAlignment.Top, ParagraphAlignment.Right),
        ("centerLeft", "Mitte links ausrichten", CellVerticalAlignment.Center, ParagraphAlignment.Left),
        ("center", "Zentriert ausrichten", CellVerticalAlignment.Center, ParagraphAlignment.Center),
        ("centerRight", "Mitte rechts ausrichten", CellVerticalAlignment.Center, ParagraphAlignment.Right),
        ("bottomLeft", "Unten links ausrichten", CellVerticalAlignment.Bottom, ParagraphAlignment.Left),
        ("bottomCenter", "Unten zentriert ausrichten", CellVerticalAlignment.Bottom, ParagraphAlignment.Center),
        ("bottomRight", "Unten rechts ausrichten", CellVerticalAlignment.Bottom, ParagraphAlignment.Right),
    ];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        EditingSession? Session() => Editor()?.Session;
        bool CanInsert() => Session() is { CurrentTable: null };
        bool InTable() => Session()?.CurrentTable is not null;
        void With(Action<EditingSession> action)
        {
            if (Session() is { } session)
            {
                action(session);
            }
        }

        var commands = window.Commands;
        commands.Add(new OfficeCommand(WriteCommandIds.InsertTable, "Tabelle einfügen …", () => Editor() is { } editor ? InsertTableDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "table", CanExecute = CanInsert,
            Description = "Fügt eine Tabelle mit der gewählten Anzahl Spalten und Zeilen ein.", Keywords = ["tabelle", "raster", "zeilen", "spalten"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.TablePicker, "Tabelle", () => Task.CompletedTask)
        {
            Icon = "table", CanExecute = CanInsert, IsSearchable = false,
        });

        // Tabellenentwurf
        commands.Add(new OfficeCommand(WriteCommandIds.TableBorders, "Rahmen", () => With(session => session.SetTableBorders(BorderChoice.All)))
        {
            Icon = "borders", CanExecute = InTable,
            Description = "Rahmenlinien der markierten Zellen: alle, außen, innen, keine …", Keywords = ["rahmen", "linien", "gitter", "tabelle"],
        });
        foreach (var (id, label, choice) in BorderChoices)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.TableBorders}.{id}", label, () => With(session => session.SetTableBorders(choice)))
            {
                Icon = "borders", CanExecute = InTable, IsSearchable = false,
            });
        }

        commands.Add(new OfficeValueCommand(WriteCommandIds.TableShading, "Schattierung", OfficeValueKind.Color,
            () => Session()?.CurrentCells is { } cells && Session()!.Document.ParagraphAt(Session()!.Selection.Active.Block)?.Cell is { } cell
                ? cells.Table.CellAt(cell.Row, cell.Cell).Shading?.VisibleFill is { } fill ? "#" + fill : null
                : null,
            value => With(session => session.SetTableCellShading(value)))
        {
            Icon = "fill", CanExecute = InTable,
            Description = "Füllt die markierten Zellen mit einer Farbe.", Keywords = ["füllfarbe", "hintergrund", "zelle", "farbe"],
        });

        // Layout – Tabelle, Zeilen und Spalten, Zusammenführen, Zellengröße, Ausrichtung, Daten
        commands.Add(new OfficeCommand(WriteCommandIds.TableSelect, "Auswählen", () => With(session => session.SelectWholeTable()))
        {
            Icon = "grid", CanExecute = InTable, Description = "Markiert die Zelle, die Zeile, die Spalte oder die ganze Tabelle.",
        });
        Add(commands, WriteCommandIds.TableSelectCell, "Zelle auswählen", "grid", InTable, () => With(session => session.SelectTableCell()));
        Add(commands, WriteCommandIds.TableSelectColumn, "Spalte auswählen", "columns", InTable, () => With(session => session.SelectTableColumn()));
        Add(commands, WriteCommandIds.TableSelectRow, "Zeile auswählen", "row-insert", InTable, () => With(session => session.SelectTableRow()));
        Add(commands, WriteCommandIds.TableSelectTable, "Tabelle auswählen", "table", InTable, () => With(session => session.SelectWholeTable()));

        commands.Add(new OfficeCommand(WriteCommandIds.TableDelete, "Löschen", () => With(session => session.DeleteTableRows()))
        {
            Icon = "trash", CanExecute = InTable, Description = "Löscht Zellen, Zeilen, Spalten oder die ganze Tabelle.",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.TableDeleteCells, "Zellen löschen …", () => Editor() is { } editor ? DeleteCellsDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "trash", CanExecute = InTable, IsSearchable = false,
        });
        Add(commands, WriteCommandIds.TableDeleteColumns, "Spalten löschen", "trash", InTable, () => With(session => session.DeleteTableColumns()), searchable: true);
        Add(commands, WriteCommandIds.TableDeleteRows, "Zeilen löschen", "trash", InTable, () => With(session => session.DeleteTableRows()), searchable: true);
        Add(commands, WriteCommandIds.TableDeleteTable, "Tabelle löschen", "trash", InTable, () => With(session => session.DeleteTable()), searchable: true);

        Add(commands, WriteCommandIds.TableInsertAbove, "Darüber einfügen", "row-insert", InTable, () => With(session => session.InsertTableRows(below: false)), searchable: true,
            description: "Fügt über der Markierung so viele Zeilen ein, wie markiert sind.");
        Add(commands, WriteCommandIds.TableInsertBelow, "Darunter einfügen", "row-insert", InTable, () => With(session => session.InsertTableRows(below: true)), searchable: true,
            description: "Fügt unter der Markierung so viele Zeilen ein, wie markiert sind.");
        Add(commands, WriteCommandIds.TableInsertLeft, "Links einfügen", "column-insert", InTable, () => With(session => session.InsertTableColumns(right: false)), searchable: true,
            description: "Fügt links der Markierung Spalten ein; die Tabelle bleibt so breit.");
        Add(commands, WriteCommandIds.TableInsertRight, "Rechts einfügen", "column-insert", InTable, () => With(session => session.InsertTableColumns(right: true)), searchable: true,
            description: "Fügt rechts der Markierung Spalten ein; die Tabelle bleibt so breit.");

        commands.Add(new OfficeCommand(WriteCommandIds.TableMerge, "Zellen verbinden", () => With(session => session.MergeTableCells()))
        {
            Icon = "merge-cells", CanExecute = () => Session()?.CanMergeTableCells == true,
            Description = "Verbindet die markierten Zellen zu einer.", Keywords = ["verbinden", "zusammenführen", "zellen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.TableSplit, "Zellen teilen …", () => Editor() is { } editor ? SplitCellsDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "split-cells", CanExecute = InTable,
            Description = "Teilt die Zelle in mehrere Spalten.", Keywords = ["teilen", "aufteilen", "zellen"],
        });

        Add(commands, WriteCommandIds.TableDistributeRows, "Zeilen verteilen", "row-insert", InTable, () =>
        {
            if (Editor() is { } editor)
            {
                editor.Session.DistributeTableRows(editor.AverageRowHeight());
            }
        }, searchable: true, description: "Gibt den markierten Zeilen dieselbe Höhe.");
        Add(commands, WriteCommandIds.TableDistributeColumns, "Spalten verteilen", "columns", InTable, () => With(session => session.DistributeTableColumns()), searchable: true,
            description: "Gibt den markierten Spalten (sonst allen) dieselbe Breite.");

        foreach (var (id, label, vertical, horizontal) in CellAlignments)
        {
            var icon = horizontal switch { ParagraphAlignment.Center => "align-center", ParagraphAlignment.Right => "align-right", _ => "align-left" };
            commands.Add(new OfficeCommand($"{WriteCommandIds.TableAlign}.{id}", label, () => With(session => session.SetTableCellAlignment(vertical, horizontal)))
            {
                Icon = icon, CanExecute = InTable, IsSearchable = false,
                IsChecked = () => CaretAlignment(Session()) == (vertical, horizontal),
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.TableRepeatHeader, "Überschriften wiederholen", () =>
        {
            With(session => session.SetRepeatHeaderRows(!IsHeaderRow(session)));
        })
        {
            Icon = "header", CanExecute = InTable, IsChecked = () => Session() is { } session && IsHeaderRow(session),
            Description = "Wiederholt die markierten obersten Zeilen auf jeder Seite, über die die Tabelle läuft.", Keywords = ["kopfzeile", "überschrift", "wiederholen", "tabelle"],
        });
    }

    private static void Add(OfficeCommandRegistry commands, string id, string label, string icon, Func<bool> canExecute, Action run, bool searchable = false, string? description = null) =>
        commands.Add(new OfficeCommand(id, label, run) { Icon = icon, CanExecute = canExecute, IsSearchable = searchable, Description = description });

    private static bool IsHeaderRow(EditingSession session) =>
        session.Document.ParagraphAt(session.Selection.Active.Block)?.Cell is { } cell && cell.Table.Rows[cell.Row].Properties.IsHeader;

    private static (CellVerticalAlignment, ParagraphAlignment)? CaretAlignment(EditingSession? session)
    {
        if (session?.Document.ParagraphAt(session.Selection.Active.Block) is not { Cell: { } cell } paragraph)
        {
            return null;
        }

        var vertical = cell.Table.CellAt(cell.Row, cell.Cell).VerticalAlignment ?? CellVerticalAlignment.Top;
        var horizontal = session.Resolver.ResolveParagraph(paragraph).Alignment;
        return (vertical, horizontal == ParagraphAlignment.Justify ? ParagraphAlignment.Left : horizontal);
    }
}
