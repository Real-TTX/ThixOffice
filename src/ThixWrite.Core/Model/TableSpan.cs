namespace ThixWrite.Core.Model;

/// <summary>
/// Where a table is in the document's blocks: [<see cref="Start"/>, <see cref="End"/>], the structure and, per cell, its first and
/// last block. Built by one scan over the table's blocks (cheap; build it again after a change).
/// </summary>
public sealed class TableSpan
{
    private readonly int[][] _first;
    private readonly int[][] _last;

    private TableSpan(Table table, int start, int end, int[][] first, int[][] last)
    {
        Table = table;
        Start = start;
        End = end;
        _first = first;
        _last = last;
    }

    public Table Table { get; }

    /// <summary>The first block of the table.</summary>
    public int Start { get; }

    /// <summary>The last block of the table.</summary>
    public int End { get; }

    public int BlockCount => End - Start + 1;

    public bool Contains(int block) => block >= Start && block <= End;

    /// <summary>The blocks of a cell (first, last); (-1, -1) when the document lacks it (a damaged table).</summary>
    public (int First, int Last) BlocksOf(int row, int cell) =>
        row < _first.Length && cell < _first[row].Length ? (_first[row][cell], _last[row][cell]) : (-1, -1);

    /// <summary>The table that block <paramref name="block"/> is in, or null.</summary>
    public static TableSpan? At(Document document, int block)
    {
        if (document.ParagraphAt(block)?.Cell is not { } cell)
        {
            return null;
        }

        var table = cell.Table;
        var start = block;
        while (start > 0 && IsOf(document, start - 1, table))
        {
            start--;
        }

        var end = block;
        while (end < document.Blocks.Count - 1 && IsOf(document, end + 1, table))
        {
            end++;
        }

        return Build(document, table, start, end);
    }

    /// <summary>Every table of the document, in order.</summary>
    public static IEnumerable<TableSpan> All(Document document)
    {
        var block = 0;
        while (block < document.Blocks.Count)
        {
            if (At(document, block) is { } span)
            {
                yield return span;
                block = span.End + 1;
                continue;
            }

            block++;
        }
    }

    /// <summary>
    /// The table's problems, empty when it is sound: its paragraphs in reading order, every cell with at least one paragraph,
    /// every row's cells within the grid (the tests check every edit with it).
    /// </summary>
    public IReadOnlyList<string> Problems(Document document)
    {
        var problems = new List<string>();
        var previous = (-1, -1);
        for (var block = Start; block <= End; block++)
        {
            var cell = document.ParagraphAt(block)!.Cell!.Value;
            if ((cell.Row, cell.Cell).CompareTo(previous) < 0)
            {
                problems.Add($"block {block} ({cell}) is out of order");
            }

            previous = (cell.Row, cell.Cell);
        }

        for (var row = 0; row < Table.RowCount; row++)
        {
            for (var cell = 0; cell < Table.Rows[row].CellCount; cell++)
            {
                if (BlocksOf(row, cell).First < 0)
                {
                    problems.Add($"cell R{row + 1}C{cell + 1} has no paragraph");
                }
            }

            var span = Table.Rows[row].Cells.Sum(item => Math.Max(1, item.GridSpan));
            if (Table.Grid.Count > 0 && span != Table.Grid.Count)
            {
                problems.Add($"row {row + 1} spans {span} of {Table.Grid.Count} grid columns");
            }
        }

        return problems;
    }

    private static bool IsOf(Document document, int block, Table table) => document.ParagraphAt(block)?.Cell is { } cell && ReferenceEquals(cell.Table, table);

    private static TableSpan Build(Document document, Table table, int start, int end)
    {
        var first = new int[table.RowCount][];
        var last = new int[table.RowCount][];
        for (var row = 0; row < table.RowCount; row++)
        {
            first[row] = Enumerable.Repeat(-1, table.Rows[row].CellCount).ToArray();
            last[row] = Enumerable.Repeat(-1, table.Rows[row].CellCount).ToArray();
        }

        for (var block = start; block <= end; block++)
        {
            var cell = document.ParagraphAt(block)!.Cell!.Value;
            if (cell.Row >= first.Length || cell.Cell >= first[cell.Row].Length)
            {
                continue;
            }

            if (first[cell.Row][cell.Cell] < 0)
            {
                first[cell.Row][cell.Cell] = block;
            }

            last[cell.Row][cell.Cell] = block;
        }

        return new TableSpan(table, start, end, first, last);
    }
}
