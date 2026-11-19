using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// The cells a selection holds in one table: rows [<see cref="FirstRow"/>, <see cref="LastRow"/>] and grid columns
/// [<see cref="FirstColumn"/>, <see cref="LastColumn"/>]. <see cref="WholeCells"/>: the selection reaches over more than one cell
/// (or over the table's edge) – then, as in Word, it is a selection of whole cells: highlighted as cells, formatted per cell,
/// cleared per cell.
/// </summary>
public sealed record TableCellRange(TableSpan Span, int FirstRow, int LastRow, int FirstColumn, int LastColumn, bool WholeCells)
{
    public Table Table => Span.Table;

    /// <summary>The cells of the range (row, cell index), covered cells of vertical merges included.</summary>
    public IEnumerable<(int Row, int Cell)> Cells
    {
        get
        {
            for (var row = FirstRow; row <= LastRow; row++)
            {
                for (var cell = 0; cell < Table.Rows[row].CellCount; cell++)
                {
                    var start = Table.GridColumnOf(row, cell);
                    var end = start + Math.Max(1, Table.CellAt(row, cell).GridSpan) - 1;
                    if (end >= FirstColumn && start <= LastColumn)
                    {
                        yield return (row, cell);
                    }
                }
            }
        }
    }

    /// <summary>True when the range holds every cell of the table.</summary>
    public bool IsWholeTable => FirstRow == 0 && LastRow == Table.RowCount - 1 && FirstColumn == 0 && LastColumn >= Table.ColumnCount - 1;

    /// <summary>True when the range holds whole rows.</summary>
    public bool IsWholeRows => FirstColumn == 0 && LastColumn >= Table.ColumnCount - 1;

    /// <summary>The range of a cell's text, from its first paragraph's start to its last one's end.</summary>
    public DocumentRange RangeOf(Document document, int row, int cell)
    {
        var (first, last) = Span.BlocksOf(row, cell);
        return new DocumentRange(new DocumentPosition(first, 0), new DocumentPosition(last, document.Blocks[last].Length));
    }
}

/// <summary>How selections and tables meet: the tables a range touches and the cells it holds in each.</summary>
public static class TableSelection
{
    /// <summary>The tables with a block in [<paramref name="range"/>.Start.Block, <paramref name="range"/>.End.Block], in order.</summary>
    public static IEnumerable<TableSpan> Touched(Document document, DocumentRange range)
    {
        var block = range.Start.Block;
        while (block <= range.End.Block && block < document.Blocks.Count)
        {
            if (TableSpan.At(document, block) is { } span)
            {
                yield return span;
                block = span.End + 1;
                continue;
            }

            block++;
        }
    }

    /// <summary>The cells the selection from <paramref name="anchor"/> to <paramref name="active"/> holds in the table of <paramref name="span"/>.</summary>
    public static TableCellRange CellsIn(Document document, TableSpan span, DocumentPosition anchor, DocumentPosition active)
    {
        var anchorCell = span.Contains(anchor.Block) ? document.ParagraphAt(anchor.Block)!.Cell : null;
        var activeCell = span.Contains(active.Block) ? document.ParagraphAt(active.Block)!.Cell : null;
        var table = span.Table;
        var lastColumn = Math.Max(0, table.ColumnCount - 1);
        if (anchorCell is { } first && activeCell is { } second)
        {
            var (firstRow, lastRow, firstColumn, lastColumnOfCells) = TableEdits.Rectangle(table, first, second);
            return new TableCellRange(span, firstRow, lastRow, firstColumn, lastColumnOfCells, !first.IsSameCell(second));
        }

        // Over the edge of the table: whole rows, from the row inside to the edge the selection crosses.
        var inside = anchorCell ?? activeCell;
        var outside = anchorCell is null ? anchor : active;
        if (inside is not { } cell)
        {
            return new TableCellRange(span, 0, table.RowCount - 1, 0, lastColumn, true);
        }

        return outside.Block < span.Start
            ? new TableCellRange(span, 0, cell.Row, 0, lastColumn, true)
            : new TableCellRange(span, cell.Row, table.RowCount - 1, 0, lastColumn, true);
    }

    /// <summary>The whole-cell selections of <paramref name="selection"/> (empty when it is text inside one cell or outside tables).</summary>
    public static IReadOnlyList<TableCellRange> WholeCells(Document document, Selection selection)
    {
        if (selection.IsCollapsed)
        {
            return [];
        }

        return [.. Touched(document, selection.Range)
            .Select(span => CellsIn(document, span, selection.Anchor, selection.Active))
            .Where(cells => cells.WholeCells)];
    }

    /// <summary>
    /// The ranges a command acts on for <paramref name="selection"/>: the selection itself, or – with whole cells selected – the text
    /// outside the tables and every selected cell's text, in document order.
    /// </summary>
    public static IReadOnlyList<DocumentRange> Ranges(Document document, Selection selection)
    {
        var tables = WholeCells(document, selection);
        if (tables.Count == 0)
        {
            return [selection.Range];
        }

        var range = selection.Range;
        var ranges = new List<DocumentRange>();
        var cursor = range.Start;
        foreach (var cells in tables)
        {
            if (cursor.Block < cells.Span.Start)
            {
                var before = cells.Span.Start - 1;
                ranges.Add(new DocumentRange(cursor, new DocumentPosition(before, document.Blocks[before].Length)));
            }

            foreach (var (row, cell) in cells.Cells)
            {
                if (cells.Span.BlocksOf(row, cell).First >= 0)
                {
                    ranges.Add(cells.RangeOf(document, row, cell));
                }
            }

            cursor = new DocumentPosition(cells.Span.End + 1, 0);
        }

        if (cursor <= range.End && cursor.Block < document.Blocks.Count)
        {
            ranges.Add(new DocumentRange(cursor, range.End));
        }

        return ranges;
    }
}
