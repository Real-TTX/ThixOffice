using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

public sealed partial class EditingSession
{
    private void EditTable(string label, int shapeId, Func<Table, Table> change)
    {
        var slideId = RequireSlide();
        Apply(label, presentation => WithShape(presentation, slideId, shapeId, shape =>
        {
            if (shape is not GraphicFrame { Table: { } table } frame)
            {
                return shape;
            }

            var changed = change(table);
            if (ReferenceEquals(changed, table))
            {
                return shape;
            }

            var transform = frame.Transform;
            if (transform is not null)
            {
                transform = transform with
                {
                    Width = changed.ColumnWidths.Sum(),
                    Height = Math.Max(transform.Height * changed.RowCount / Math.Max(1, table.RowCount), changed.Rows.Sum(row => row.Height)),
                };
            }

            return frame with { Table = changed, Transform = transform };
        }));
    }

    /// <summary>Inserts a row above or below <paramref name="row"/> (same height, empty cells with the row's cell properties).</summary>
    public void InsertTableRow(int shapeId, int row, bool below) => EditTable(below ? "Zeile unterhalb einfügen" : "Zeile oberhalb einfügen", shapeId, table =>
    {
        var source = table.Rows[Math.Clamp(row, 0, table.RowCount - 1)];
        var cells = source.Cells.Select(cell => new TableCell(TextBody.Empty, cell.Properties)).ToList();
        return table with { Rows = table.Rows.Insert(Math.Clamp(below ? row + 1 : row, 0, table.RowCount), new TableRow(source.Height, cells)) };
    });

    /// <summary>Inserts a column left or right of <paramref name="column"/> (as wide as it; the table grows).</summary>
    public void InsertTableColumn(int shapeId, int column, bool right) => EditTable(right ? "Spalte rechts einfügen" : "Spalte links einfügen", shapeId, table =>
    {
        var reference = Math.Clamp(column, 0, table.ColumnCount - 1);
        var at = Math.Clamp(right ? reference + 1 : reference, 0, table.ColumnCount);
        return table with
        {
            ColumnWidths = table.ColumnWidths.Insert(at, table.ColumnWidths[reference]),
            Rows = table.Rows.Select(row => row with { Cells = row.Cells.Insert(at, new TableCell(TextBody.Empty, row.Cells[reference].Properties)) }).ToList(),
        };
    });

    /// <summary>Deletes rows (never all of them; merges crossing them are undone).</summary>
    public void DeleteTableRows(int shapeId, int first, int count) => EditTable("Zeilen löschen", shapeId, table =>
    {
        count = Math.Min(count, table.RowCount - first);
        if (count <= 0 || count >= table.RowCount)
        {
            return table;
        }

        return Unmerged(table) with { Rows = Unmerged(table).Rows.RemoveRange(first, count) };
    });

    /// <summary>Deletes columns (never all of them).</summary>
    public void DeleteTableColumns(int shapeId, int first, int count) => EditTable("Spalten löschen", shapeId, table =>
    {
        count = Math.Min(count, table.ColumnCount - first);
        if (count <= 0 || count >= table.ColumnCount)
        {
            return table;
        }

        var plain = Unmerged(table);
        return plain with
        {
            ColumnWidths = plain.ColumnWidths.RemoveRange(first, count),
            Rows = plain.Rows.Select(row => row with { Cells = row.Cells.RemoveRange(first, count) }).ToList(),
        };
    });

    /// <summary>Merges the cell rectangle into its top-left cell (the texts join there).</summary>
    public void MergeTableCells(int shapeId, int firstRow, int firstColumn, int lastRow, int lastColumn) => EditTable("Zellen verbinden", shapeId, table =>
    {
        (firstRow, lastRow) = (Math.Min(firstRow, lastRow), Math.Max(firstRow, lastRow));
        (firstColumn, lastColumn) = (Math.Min(firstColumn, lastColumn), Math.Max(firstColumn, lastColumn));
        if (firstRow == lastRow && firstColumn == lastColumn)
        {
            return table;
        }

        var paragraphs = new List<TextParagraph>();
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var text = table.CellAt(row, column).Text;
                if (!text.IsEmpty)
                {
                    paragraphs.AddRange(text.Paragraphs);
                }
            }
        }

        var result = Unmerged(table);
        for (var row = firstRow; row <= lastRow; row++)
        {
            for (var column = firstColumn; column <= lastColumn; column++)
            {
                var cell = result.CellAt(row, column);
                result = result.WithCell(row, column, row == firstRow && column == firstColumn
                    ? cell with
                    {
                        GridSpan = lastColumn - firstColumn + 1,
                        RowSpan = lastRow - firstRow + 1,
                        Text = cell.Text with { Paragraphs = paragraphs.Count == 0 ? [TextParagraph.Empty] : paragraphs },
                    }
                    : cell with { Text = TextBody.Empty, HorizontalMerge = column > firstColumn, VerticalMerge = row > firstRow });
            }
        }

        return result;
    });

    /// <summary>Splits a merged cell back into its cells (the text stays in the first).</summary>
    public void SplitTableCell(int shapeId, int row, int column) => EditTable("Zellen teilen", shapeId, table =>
    {
        var cell = table.CellAt(row, column);
        if (cell.GridSpan <= 1 && cell.RowSpan <= 1)
        {
            return table;
        }

        var result = table;
        for (var r = row; r < Math.Min(table.RowCount, row + cell.RowSpan); r++)
        {
            for (var c = column; c < Math.Min(table.ColumnCount, column + cell.GridSpan); c++)
            {
                var current = result.CellAt(r, c);
                result = result.WithCell(r, c, current with { GridSpan = 1, RowSpan = 1, HorizontalMerge = false, VerticalMerge = false });
            }
        }

        return result;
    });

    /// <summary>Sets the fill of cells (null: the table style's).</summary>
    public void SetTableCellFill(int shapeId, IEnumerable<(int Row, int Column)> cells, Fill? fill) => EditTable("Schattierung", shapeId, table =>
        cells.Aggregate(table, (current, cell) => current.WithCell(cell.Row, cell.Column, current.CellAt(cell.Row, cell.Column) with
        {
            Properties = current.CellAt(cell.Row, cell.Column).Properties with { Fill = fill },
        })));

    /// <summary>Tabellenformatoptionen: header row, total row, banded rows, first/last column, banded columns.</summary>
    public void SetTableStyleOptions(int shapeId, Func<TableProperties, TableProperties> change) =>
        EditTable("Tabellenformatoptionen", shapeId, table => table with { Properties = change(table.Properties) });

    /// <summary>Sets the width of a column (EMU; the table grows or shrinks).</summary>
    public void SetTableColumnWidth(int shapeId, int column, long width) => EditTable("Spaltenbreite", shapeId, table =>
        table with { ColumnWidths = table.ColumnWidths.SetItem(column, Math.Max(Units.EmuPerPoint * 4, width)) });

    private static Table Unmerged(Table table) => table with
    {
        Rows = table.Rows.Select(row => row with
        {
            Cells = row.Cells.Select(cell => cell.GridSpan > 1 || cell.RowSpan > 1 || cell.IsCovered ? cell with { GridSpan = 1, RowSpan = 1, HorizontalMerge = false, VerticalMerge = false } : cell).ToList(),
        }).ToList(),
    };
}
