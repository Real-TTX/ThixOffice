using ThixWrite.Core.Model;
using static ThixWrite.Core.Editing.TableEdits;

namespace ThixWrite.Core.Editing;

/// <summary>"Zellen löschen": what goes with the selected cells.</summary>
public enum CellDeletion
{
    ShiftLeft,
    EntireRow,
    EntireColumn,
}

/// <summary>The table commands of the editing session (Einfügen › Tabelle, the contextual tabs, Tab between cells).</summary>
public sealed partial class EditingSession
{
    /// <summary>The largest table "Tabelle einfügen" makes.</summary>
    public const int MaxTableRows = 500;

    public const int MaxTableColumns = 63;

    /// <summary>The table the caret (or the selection's active end) is in, or null.</summary>
    public TableSpan? CurrentTable => TableSpan.At(Document, _selection.Active.Block) ?? TableSpan.At(Document, _selection.Anchor.Block);

    /// <summary>The cells the selection holds in <see cref="CurrentTable"/> (the caret's cell for a caret), or null outside tables.</summary>
    public TableCellRange? CurrentCells => CurrentTable is { } span ? TableSelection.CellsIn(Document, span, _selection.Anchor, _selection.Active) : null;

    /// <summary>
    /// Einfügen › Tabelle: a table of <paramref name="rows"/> × <paramref name="columns"/> empty cells ("Tabellenraster") over the
    /// text width, at the caret (a paragraph with text is split there); the caret goes into the first cell. Not inside tables.
    /// </summary>
    public void InsertTable(int rows, int columns)
    {
        rows = Math.Clamp(rows, 1, MaxTableRows);
        columns = Math.Clamp(columns, 1, MaxTableColumns);
        if (CurrentTable is not null)
        {
            return;
        }

        Run("Tabelle einfügen", EditKind.Other, null, () =>
        {
            if (!_selection.IsCollapsed)
            {
                var deleted = (ReplaceMap)Apply(ContentEdits.Delete(Document, _selection.Range));
                SetSelectionQuiet(Selection.Caret(CaretStops.Snap(Document, deleted.From)));
            }

            var caret = _selection.Start;
            if (caret.Offset > 0)
            {
                var split = (ReplaceMap)Apply(ContentEdits.SplitParagraph(Document, DocumentRange.At(caret)));
                caret = split.EndOfInserted;
            }

            EnsureStyle(BuiltInStyles.TableGrid);
            var paragraph = (Paragraph)Document.Blocks[caret.Block];
            var template = Paragraph.Empty(ParagraphProperties.Empty, paragraph.MarkProperties);
            var width = Document.SectionOf(caret.Block).Properties.ContentWidth;
            var blocks = CreateTable(rows, columns, width, template, BuiltInStyles.TableGrid);
            Apply(new BlocksStep(caret.Block, [], blocks, new BlockReplaceMap(caret.Block, 0, blocks)));
            SetSelectionQuiet(Selection.Caret(new DocumentPosition(caret.Block, 0)));
        });
    }

    /// <summary>Zeilen oberhalb / unterhalb einfügen: as many rows as are selected, like the row they are inserted at.</summary>
    public void InsertTableRows(bool below) => ChangeTable(below ? "Zeilen unterhalb einfügen" : "Zeilen oberhalb einfügen", (model, cells) =>
    {
        var count = cells.LastRow - cells.FirstRow + 1;
        var row = below ? cells.LastRow : cells.FirstRow;
        InsertRows(model, row, below, count);
        return (below ? cells.LastRow + 1 : cells.FirstRow, 0);
    });

    /// <summary>Spalten links / rechts einfügen: as many columns as are selected; the table keeps its width.</summary>
    public void InsertTableColumns(bool right) => ChangeTable(right ? "Spalten rechts einfügen" : "Spalten links einfügen", (model, cells) =>
    {
        var count = cells.LastColumn - cells.FirstColumn + 1;
        var row = cells.FirstRow;
        var cell = model.CellAt(row, right ? cells.LastColumn : cells.FirstColumn);
        if (cell < 0)
        {
            return null;
        }

        var column = right ? model.ColumnOf(row, cell) + model.Rows[row].Cells[cell].Span : model.ColumnOf(row, cell);
        InsertColumns(model, row, cell, right, count);
        return (row, Math.Max(0, model.CellAt(row, column)));
    });

    /// <summary>Zeilen löschen.</summary>
    public void DeleteTableRows() => ChangeTable("Zeilen löschen", (model, cells) =>
    {
        DeleteRows(model, cells.FirstRow, cells.LastRow);
        return (Math.Min(cells.FirstRow, model.Rows.Count - 1), 0);
    });

    /// <summary>Spalten löschen.</summary>
    public void DeleteTableColumns() => ChangeTable("Spalten löschen", (model, cells) =>
    {
        DeleteColumns(model, cells.FirstColumn, cells.LastColumn);
        var row = Math.Min(cells.FirstRow, model.Rows.Count - 1);
        return (row, row < 0 ? 0 : Math.Max(0, model.CellAt(row, Math.Min(cells.FirstColumn, model.ColumnCount - 1))));
    });

    /// <summary>Zellen löschen (the choice of the dialog).</summary>
    public void DeleteTableCells(CellDeletion deletion)
    {
        switch (deletion)
        {
            case CellDeletion.EntireRow:
                DeleteTableRows();
                return;
            case CellDeletion.EntireColumn:
                DeleteTableColumns();
                return;
        }

        ChangeTable("Zellen löschen", (model, cells) =>
        {
            DeleteCellsShiftLeft(model, cells.FirstRow, cells.LastRow, cells.FirstColumn, cells.LastColumn);
            return (cells.FirstRow, Math.Max(0, model.CellAt(cells.FirstRow, cells.FirstColumn)));
        });
    }

    /// <summary>Tabelle löschen: the table goes, the caret to the paragraph after it.</summary>
    public void DeleteTable() => ChangeTable("Tabelle löschen", (model, _) =>
    {
        model.Rows.Clear();
        return (0, 0);
    });

    /// <summary>Zellen verbinden: the selected rectangle becomes one cell (nothing happens when it cuts through a merged cell).</summary>
    public void MergeTableCells() => ChangeTable("Zellen verbinden", (model, cells) =>
        Merge(model, cells.FirstRow, cells.LastRow, cells.FirstColumn, cells.LastColumn)
            ? (cells.FirstRow, model.CellAt(cells.FirstRow, cells.FirstColumn))
            : null);

    /// <summary>True when the selection is a rectangle of more than one cell that can be merged.</summary>
    public bool CanMergeTableCells
    {
        get
        {
            if (CurrentTable is not { } span || CurrentCells is not { WholeCells: true } cells)
            {
                return false;
            }

            var model = TableModel.Load(Document, span);
            return Merge(model, cells.FirstRow, cells.LastRow, cells.FirstColumn, cells.LastColumn);
        }
    }

    /// <summary>Zellen teilen: the caret's cell into <paramref name="columns"/> cells; <paramref name="unmergeRows"/> ends its vertical merge.</summary>
    public void SplitTableCell(int columns, bool unmergeRows = true)
    {
        if (Document.ParagraphAt(_selection.Active.Block)?.Cell is not { } cell)
        {
            return;
        }

        ChangeTable("Zellen teilen", (model, _) =>
        {
            Split(model, cell.Row, cell.Cell, Math.Clamp(columns, 1, MaxTableColumns), unmergeRows);
            return (cell.Row, cell.Cell);
        });
    }

    /// <summary>Schattierung of the selected cells ("RRGGBB", or null for "Keine Farbe").</summary>
    public void SetTableCellShading(string? fill) => ChangeCells("Schattierung", (cell, _) =>
        cell.Properties = cell.Properties with { Shading = fill is null ? null : new Shading(fill.TrimStart('#').ToUpperInvariant()) });

    /// <summary>Rahmen of the selected cells (Word's border menu).</summary>
    public void SetTableBorders(BorderChoice choice, BorderLine? line = null) => ChangeTable("Rahmen", (model, cells) =>
    {
        SetBorders(model, cells.FirstRow, cells.LastRow, cells.FirstColumn, cells.LastColumn, choice, line ?? BorderLine.Single);
        return KeepSelection;
    });

    /// <summary>Ausrichtung in the selected cells: vertical (the cell) and horizontal (its paragraphs).</summary>
    public void SetTableCellAlignment(CellVerticalAlignment vertical, ParagraphAlignment horizontal) => ChangeCells("Ausrichtung", (cell, _) =>
    {
        cell.Properties = cell.Properties with { VerticalAlignment = vertical == CellVerticalAlignment.Top ? null : vertical };
        cell.Paragraphs = [.. cell.Paragraphs.Select(paragraph => paragraph.WithProperties(paragraph.Properties with { Alignment = horizontal == ParagraphAlignment.Left ? null : horizontal }))];
    });

    /// <summary>"Überschriften wiederholen": the rows from the first to the selection's last repeat on every page (or none).</summary>
    public void SetRepeatHeaderRows(bool repeat) => ChangeTable("Überschriften wiederholen", (model, cells) =>
    {
        for (var row = 0; row < model.Rows.Count; row++)
        {
            var isHeader = repeat && row <= cells.LastRow;
            model.Rows[row].Properties = model.Rows[row].Properties with { IsHeader = isHeader };
        }

        return KeepSelection;
    });

    /// <summary>Zeilen verteilen: the selected rows get the same height (at least <paramref name="height"/> points).</summary>
    public void DistributeTableRows(double height) => ChangeTable("Zeilen verteilen", (model, cells) =>
    {
        for (var row = cells.FirstRow; row <= cells.LastRow; row++)
        {
            model.Rows[row].Properties = model.Rows[row].Properties with { Height = Math.Round(height, 2), ExactHeight = false };
        }

        return KeepSelection;
    });

    /// <summary>Spalten verteilen: the selected grid columns (all, at a caret) get the same width.</summary>
    public void DistributeTableColumns() => ChangeTable("Spalten verteilen", (model, cells) =>
    {
        var (first, last) = cells.WholeCells ? (cells.FirstColumn, cells.LastColumn) : (0, model.ColumnCount - 1);
        var average = model.Grid.Skip(first).Take(last - first + 1).Average();
        for (var column = first; column <= last; column++)
        {
            model.Grid[column] = average;
        }

        return KeepSelection;
    });

    /// <summary>Column widths by dragging a border: the table at <paramref name="tableStart"/> gets <paramref name="grid"/> (points).</summary>
    public void SetTableColumnWidths(int tableStart, IReadOnlyList<double> grid)
    {
        if (TableSpan.At(Document, tableStart) is not { } span || grid.Count != span.Table.ColumnCount)
        {
            return;
        }

        ChangeTable("Spaltenbreite", span, model =>
        {
            model.Grid = [.. grid.Select(width => Math.Max(MinimumColumn, width))];
            model.Properties = model.Properties with { Layout = TableLayoutKind.Fixed };
            return KeepSelection;
        });
    }

    /// <summary>The alignment of the table between the margins.</summary>
    public void SetTableAlignment(TableAlignment alignment) => ChangeTable("Tabellenausrichtung", (model, _) =>
    {
        model.Properties = model.Properties with { Alignment = alignment == TableAlignment.Left ? null : alignment };
        return KeepSelection;
    });

    // ---------------------------------------------------------------- moving and selecting

    /// <summary>
    /// Tab / Umschalt+Tab in a table: the next / previous cell with its text selected; Tab in the last cell adds a row. False
    /// outside tables.
    /// </summary>
    public bool MoveToCell(bool forward)
    {
        if (CurrentTable is not { } span || Document.ParagraphAt(_selection.Active.Block)?.Cell is not { } cell)
        {
            return false;
        }

        var table = span.Table;
        var (row, index) = (cell.Row, cell.Cell);
        while (true)
        {
            (row, index) = forward
                ? index + 1 < table.Rows[row].CellCount ? (row, index + 1) : (row + 1, 0)
                : index > 0 ? (row, index - 1) : row > 0 ? (row - 1, table.Rows[row - 1].CellCount - 1) : (-1, -1);
            if (row < 0)
            {
                return true; // the first cell: Umschalt+Tab stays
            }

            if (row >= table.RowCount)
            {
                AddRowAtEnd(span);
                return true;
            }

            if (!table.IsCovered(row, index))
            {
                break;
            }
        }

        SelectCell(span, row, index);
        return true;
    }

    /// <summary>Layout › Auswählen › Zelle: the text of the caret's cell.</summary>
    public void SelectTableCell()
    {
        if (CurrentTable is { } span && Document.ParagraphAt(_selection.Active.Block)?.Cell is { } cell)
        {
            SelectCell(span, cell.Row, cell.Cell);
        }
    }

    /// <summary>Layout › Auswählen › Zeile: the rows of the selection, as whole cells.</summary>
    public void SelectTableRow()
    {
        if (CurrentCells is not { } cells)
        {
            return;
        }

        var span = cells.Span;
        var (firstRow, firstCell) = span.Table.MergeOrigin(cells.FirstRow, 0);
        var (lastRow, lastCell) = span.Table.MergeOrigin(cells.LastRow, span.Table.Rows[cells.LastRow].CellCount - 1);
        var first = span.BlocksOf(firstRow, firstCell).First;
        var last = span.BlocksOf(lastRow, lastCell).Last;
        SetSelection(new Selection(new DocumentPosition(first, 0), new DocumentPosition(last, Document.Blocks[last].Length)));
    }

    /// <summary>Layout › Auswählen › Spalte: the columns of the selection, as whole cells (top to bottom).</summary>
    public void SelectTableColumn()
    {
        if (CurrentCells is not { } cells)
        {
            return;
        }

        var table = cells.Table;
        var top = table.CellAtColumn(0, cells.FirstColumn);
        var bottomRow = table.RowCount - 1;
        var bottom = table.CellAtColumn(bottomRow, cells.LastColumn);
        if (top < 0 || bottom < 0)
        {
            return;
        }

        var (bottomOrigin, bottomCell) = table.MergeOrigin(bottomRow, bottom);
        var first = cells.Span.BlocksOf(0, top).First;
        var last = cells.Span.BlocksOf(bottomOrigin, bottomCell).Last;
        SetSelection(new Selection(new DocumentPosition(first, 0), new DocumentPosition(last, Document.Blocks[last].Length)));
    }

    /// <summary>Layout › Auswählen › Tabelle.</summary>
    public void SelectWholeTable()
    {
        if (CurrentTable is not { } span)
        {
            return;
        }

        SetSelection(new Selection(new DocumentPosition(span.Start, 0), new DocumentPosition(span.End, Document.Blocks[span.End].Length)));
    }

    // ---------------------------------------------------------------- internals

    private void SelectCell(TableSpan span, int row, int cell)
    {
        var (first, last) = span.BlocksOf(row, cell);
        if (first < 0)
        {
            return;
        }

        SetSelection(new Selection(new DocumentPosition(first, 0), new DocumentPosition(last, Document.Blocks[last].Length)));
    }

    private void AddRowAtEnd(TableSpan span)
    {
        var lastRow = span.Table.RowCount - 1;
        ChangeTable("Zeile einfügen", span, model =>
        {
            InsertRows(model, lastRow, below: true, 1);
            return (lastRow + 1, 0);
        });
    }

    /// <summary>What a change returns to keep the selection where it is (formats of cells, widths).</summary>
    private static readonly (int Row, int Cell) KeepSelection = (-1, -1);

    /// <summary>A change of the selected cells' working copies.</summary>
    private void ChangeCells(string label, Action<CellModel, (int Row, int Cell)> change) => ChangeTable(label, (model, cells) =>
    {
        foreach (var (row, cell) in TableEdits.CellsIn(model, cells.FirstRow, cells.LastRow, cells.FirstColumn, cells.LastColumn))
        {
            change(model.Rows[row].Cells[cell], (row, cell));
        }

        return KeepSelection;
    });

    /// <summary>A change of the current table, with the cells of the selection; the change returns the cell for the caret (null: no change).</summary>
    private void ChangeTable(string label, Func<TableModel, TableCellRange, (int Row, int Cell)?> change)
    {
        if (CurrentTable is not { } span)
        {
            return;
        }

        var cells = TableSelection.CellsIn(Document, span, _selection.Anchor, _selection.Active);
        ChangeTable(label, span, model => change(model, cells));
    }

    /// <summary>Loads the table, changes it and puts the new table (or nothing, when no row is left) in its place, as one step.</summary>
    private void ChangeTable(string label, TableSpan span, Func<TableModel, (int Row, int Cell)?> change)
    {
        var selection = _selection;
        Run(label, EditKind.Other, null, () =>
        {
            var model = TableModel.Load(Document, span);
            var target = change(model);
            if (model.Rows.Count == 0 || model.ColumnCount == 0 || model.Rows.All(row => row.Cells.Count == 0))
            {
                Apply(TableEdits.Replace(Document, span, []));
                SetSelectionQuiet(Selection.Caret(CaretStops.Snap(Document, new DocumentPosition(span.Start, 0))));
                return;
            }

            if (target is null)
            {
                return;
            }

            var blocks = model.Emit();
            Apply(TableEdits.Replace(Document, span, blocks));
            if (target == KeepSelection)
            {
                // A format of cells or a width keeps the selection (the table has as many paragraphs as before).
                SetSelectionQuiet(new Selection(CaretStops.Snap(Document, selection.Anchor), CaretStops.Snap(Document, selection.Active), selection.Affinity));
                return;
            }

            if (target is { } cell && TableSpan.At(Document, span.Start) is { } updated)
            {
                var row = Math.Clamp(cell.Row, 0, updated.Table.RowCount - 1);
                var index = Math.Clamp(cell.Cell, 0, updated.Table.Rows[row].CellCount - 1);
                var (origin, originCell) = updated.Table.MergeOrigin(row, index);
                var first = updated.BlocksOf(origin, originCell).First;
                SetSelectionQuiet(Selection.Caret(new DocumentPosition(Math.Max(first, span.Start), 0)));
            }
        });
    }
}
