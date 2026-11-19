using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Which borders "Rahmen" sets on the selected cells (Word's border menu).</summary>
public enum BorderChoice
{
    None,
    All,
    Outside,
    Inside,
    Top,
    Bottom,
    Left,
    Right,
    InsideHorizontal,
    InsideVertical,
}

/// <summary>
/// The structural edits of tables. A table is loaded into a mutable working copy (<see cref="TableModel"/>: properties, grid,
/// rows, cells with their paragraphs), changed there, and emitted as a NEW <see cref="Table"/> with new paragraphs naming it –
/// one <see cref="BlocksStep"/> replacing the table's blocks, so undo and redo are exact like every other edit. After every
/// change the grid is consistent (every row spans it) and every cell's width is the sum of its grid columns.
/// </summary>
internal static class TableEdits
{
    /// <summary>The smallest column a change leaves (points).</summary>
    public const double MinimumColumn = 12;

    /// <summary>A new table of <paramref name="rows"/> × <paramref name="columns"/> empty cells over <paramref name="width"/> points ("Tabellenraster").</summary>
    public static IReadOnlyList<Block> CreateTable(int rows, int columns, double width, Paragraph template, string? styleId)
    {
        var columnWidth = width / columns;
        var model = new TableModel
        {
            Properties = new TableProperties
            {
                StyleId = styleId,
                Width = TableWidth.Auto,
                UnknownXml = "<w:tblLook w:val=\"04a0\" xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" />", // as the SDK writes it back
            },
            Grid = [.. Enumerable.Repeat(columnWidth, columns)],
        };
        for (var row = 0; row < rows; row++)
        {
            var rowModel = new RowModel { Properties = TableRowProperties.Empty };
            for (var column = 0; column < columns; column++)
            {
                rowModel.Cells.Add(new CellModel { Properties = TableCellProperties.Empty, Paragraphs = [EmptyLike(template)] });
            }

            model.Rows.Add(rowModel);
        }

        return model.Emit();
    }

    /// <summary>An empty paragraph with the paragraph and mark formatting of <paramref name="template"/> (no cell, no section break).</summary>
    public static Paragraph EmptyLike(Paragraph template) => Paragraph.Empty(template.Properties, template.MarkProperties);

    /// <summary>The step that replaces the table of <paramref name="span"/> with <paramref name="blocks"/> (another table, or nothing).</summary>
    public static BlocksStep Replace(Document document, TableSpan span, IReadOnlyList<Block> blocks) =>
        new(span.Start, ContentEdits.Slice(document, span.Start, span.BlockCount), blocks, new BlockReplaceMap(span.Start, span.BlockCount, blocks));

    /// <summary>
    /// The selected cells of a table as a rectangle of grid columns and rows (a caret: its cell). Cells merged across the edge
    /// widen the rectangle, so that it holds whole cells.
    /// </summary>
    public static (int FirstRow, int LastRow, int FirstColumn, int LastColumn) Rectangle(Table table, CellRef first, CellRef last)
    {
        var rows = (Math.Min(first.Row, last.Row), Math.Max(first.Row, last.Row));
        var firstColumn = table.GridColumnOf(first.Row, first.Cell);
        var lastColumn = table.GridColumnOf(last.Row, last.Cell);
        var columns = (Math.Min(firstColumn, lastColumn),
            Math.Max(firstColumn + table.CellAt(first.Row, first.Cell).GridSpan, lastColumn + table.CellAt(last.Row, last.Cell).GridSpan) - 1);
        return (rows.Item1, rows.Item2, columns.Item1, columns.Item2);
    }

    /// <summary>The working copy of a table.</summary>
    public sealed class TableModel
    {
        public required TableProperties Properties { get; set; }

        public List<double> Grid { get; set; } = [];

        public List<RowModel> Rows { get; } = [];

        public int ColumnCount => Grid.Count;

        public static TableModel Load(Document document, TableSpan span)
        {
            var table = span.Table;
            var model = new TableModel { Properties = table.Properties, Grid = [.. table.Grid] };
            for (var row = 0; row < table.RowCount; row++)
            {
                var rowModel = new RowModel { Properties = table.Rows[row].Properties };
                for (var cell = 0; cell < table.Rows[row].CellCount; cell++)
                {
                    var (first, last) = span.BlocksOf(row, cell);
                    var paragraphs = new List<Paragraph>();
                    for (var block = first; first >= 0 && block <= last; block++)
                    {
                        paragraphs.Add(((Paragraph)document.Blocks[block]).WithCell(null));
                    }

                    if (paragraphs.Count == 0)
                    {
                        paragraphs.Add(Paragraph.Empty());
                    }

                    rowModel.Cells.Add(new CellModel { Properties = table.CellAt(row, cell), Paragraphs = paragraphs });
                }

                model.Rows.Add(rowModel);
            }

            model.EnsureGrid();
            return model;
        }

        /// <summary>The cell of <paramref name="row"/> covering grid column <paramref name="column"/> (index), or -1.</summary>
        public int CellAt(int row, int column)
        {
            var start = 0;
            var cells = Rows[row].Cells;
            for (var index = 0; index < cells.Count; index++)
            {
                if (column >= start && column < start + cells[index].Span)
                {
                    return index;
                }

                start += cells[index].Span;
            }

            return -1;
        }

        public int ColumnOf(int row, int cell) => Rows[row].Cells.Take(cell).Sum(item => item.Span);

        /// <summary>The paragraphs with their cells (a new <see cref="Table"/>), after making widths and the grid consistent.</summary>
        public IReadOnlyList<Block> Emit()
        {
            EnsureGrid();
            var rows = new List<TableRow>();
            foreach (var row in Rows)
            {
                var cells = new List<TableCellProperties>();
                var column = 0;
                foreach (var cell in row.Cells)
                {
                    var width = Grid.Skip(column).Take(cell.Span).Sum();
                    var keepsWidth = cell.Properties.Width is { Kind: WidthKind.Percent or WidthKind.Auto or WidthKind.Nil };
                    cells.Add(cell.Properties with
                    {
                        GridSpan = cell.Span,
                        Width = keepsWidth ? cell.Properties.Width : TableWidth.Points(Math.Round(width, 2)),
                    });
                    column += cell.Span;
                }

                rows.Add(new TableRow(row.Properties, new EquatableArray<TableCellProperties>(cells)));
            }

            var properties = Properties.Width is { Kind: WidthKind.Points }
                ? Properties with { Width = TableWidth.Points(Math.Round(Grid.Sum(), 2)) }
                : Properties;
            var table = new Table(properties, new EquatableArray<double>(Grid.Select(width => Math.Round(width, 2))), new EquatableArray<TableRow>(rows));
            var blocks = new List<Block>();
            for (var row = 0; row < Rows.Count; row++)
            {
                for (var cell = 0; cell < Rows[row].Cells.Count; cell++)
                {
                    foreach (var paragraph in Rows[row].Cells[cell].Paragraphs)
                    {
                        blocks.Add(paragraph.WithSectionBreak(null).WithCell(new CellRef(table, row, cell)));
                    }
                }
            }

            return blocks;
        }

        /// <summary>A grid that every row spans: missing columns get the average width, rows short of it get an empty cell.</summary>
        private void EnsureGrid()
        {
            var widest = Rows.Select(row => row.Cells.Sum(cell => cell.Span)).DefaultIfEmpty(1).Max();
            if (Grid.Count == 0)
            {
                Grid = [.. Enumerable.Repeat(72.0, widest)];
            }

            while (Grid.Count < widest)
            {
                Grid.Add(Grid.Average());
            }

            foreach (var row in Rows)
            {
                var span = row.Cells.Sum(cell => cell.Span);
                if (span < Grid.Count)
                {
                    var template = row.Cells.Count > 0 ? row.Cells[^1].Paragraphs[0] : Paragraph.Empty();
                    row.Cells.Add(new CellModel { Properties = TableCellProperties.Empty with { GridSpan = Grid.Count - span }, Paragraphs = [EmptyLike(template)] });
                }
            }
        }
    }

    public sealed class RowModel
    {
        public required TableRowProperties Properties { get; set; }

        public List<CellModel> Cells { get; } = [];
    }

    public sealed class CellModel
    {
        public required TableCellProperties Properties { get; set; }

        public required List<Paragraph> Paragraphs { get; set; }

        public int Span
        {
            get => Math.Max(1, Properties.GridSpan);
            set => Properties = Properties with { GridSpan = Math.Max(1, value) };
        }

        public bool IsEmpty => Paragraphs.All(paragraph => paragraph.Length == 0);

        /// <summary>A new empty cell like this one: same formatting and span, not merged, one empty paragraph.</summary>
        public CellModel CloneEmpty() => new()
        {
            Properties = Properties with { VerticalMerge = VerticalMerge.None },
            Paragraphs = [EmptyLike(Paragraphs[0])],
        };
    }

    // ---------------------------------------------------------------- rows and columns

    /// <summary>Inserts <paramref name="count"/> rows like row <paramref name="row"/> above or below it.</summary>
    public static void InsertRows(TableModel model, int row, bool below, int count)
    {
        var source = model.Rows[row];
        var at = below ? row + 1 : row;
        for (var index = 0; index < count; index++)
        {
            var inserted = new RowModel { Properties = source.Properties with { IsHeader = !below && source.Properties.IsHeader } };
            for (var cell = 0; cell < source.Cells.Count; cell++)
            {
                var clone = source.Cells[cell].CloneEmpty();

                // Inside a vertical merge (the row below continues it) the new row's cell continues it too.
                var column = model.ColumnOf(row, cell);
                var next = at < model.Rows.Count ? model.CellAt(at, column) : -1;
                if (below && next >= 0 && model.Rows[at].Cells[next].Properties.VerticalMerge == VerticalMerge.Continue
                    && source.Cells[cell].Properties.VerticalMerge != VerticalMerge.None)
                {
                    clone.Properties = clone.Properties with { VerticalMerge = VerticalMerge.Continue };
                }

                inserted.Cells.Add(clone);
            }

            model.Rows.Insert(at, inserted);
        }
    }

    /// <summary>
    /// Inserts <paramref name="count"/> grid columns left or right of the cell at (<paramref name="row"/>, <paramref name="cell"/>),
    /// as wide as its first column; the table keeps its width (every column shrinks a little).
    /// </summary>
    public static void InsertColumns(TableModel model, int row, int cell, bool right, int count)
    {
        var firstColumn = model.ColumnOf(row, cell);
        var at = right ? firstColumn + model.Rows[row].Cells[cell].Span : firstColumn;
        var width = model.Grid[Math.Min(firstColumn, model.Grid.Count - 1)];
        var total = model.Grid.Sum();
        for (var index = 0; index < count; index++)
        {
            model.Grid.Insert(at, width);
        }

        var factor = total / model.Grid.Sum();
        model.Grid = [.. model.Grid.Select(column => column * factor)];
        foreach (var rowModel in model.Rows)
        {
            // The cell left of the new column (or the first one) is the pattern; the new cells go at the column boundary.
            var start = 0;
            var position = rowModel.Cells.Count;
            CellModel? pattern = null;
            for (var index = 0; index < rowModel.Cells.Count; index++)
            {
                if (start >= at)
                {
                    position = index;
                    break;
                }

                pattern = rowModel.Cells[index];
                if (start + rowModel.Cells[index].Span > at)
                {
                    // The boundary is inside a merged cell: it grows instead.
                    rowModel.Cells[index].Span += count;
                    position = -1;
                    break;
                }

                start += rowModel.Cells[index].Span;
            }

            if (position < 0)
            {
                continue;
            }

            pattern ??= rowModel.Cells[0];
            for (var index = 0; index < count; index++)
            {
                var clone = pattern.CloneEmpty();
                clone.Span = 1;
                rowModel.Cells.Insert(position, clone);
            }
        }
    }

    /// <summary>Deletes rows [<paramref name="first"/>, <paramref name="last"/>]; a merge they started passes to the row after them.</summary>
    public static void DeleteRows(TableModel model, int first, int last)
    {
        if (last + 1 < model.Rows.Count)
        {
            var after = model.Rows[last + 1];
            for (var index = 0; index < after.Cells.Count; index++)
            {
                if (after.Cells[index].Properties.VerticalMerge != VerticalMerge.Continue)
                {
                    continue;
                }

                var column = model.ColumnOf(last + 1, index);
                var above = first > 0 ? model.CellAt(first - 1, column) : -1;
                var continuesAbove = above >= 0 && model.Rows[first - 1].Cells[above].Properties.VerticalMerge != VerticalMerge.None;
                if (!continuesAbove)
                {
                    after.Cells[index].Properties = after.Cells[index].Properties with { VerticalMerge = VerticalMerge.Restart };
                }
            }
        }

        model.Rows.RemoveRange(first, last - first + 1);
    }

    /// <summary>Deletes grid columns [<paramref name="first"/>, <paramref name="last"/>]: cells inside go, cells across them shrink.</summary>
    public static void DeleteColumns(TableModel model, int first, int last)
    {
        foreach (var row in model.Rows)
        {
            var start = 0;
            for (var index = 0; index < row.Cells.Count; index++)
            {
                var cell = row.Cells[index];
                var end = start + cell.Span - 1;
                var overlap = Math.Min(end, last) - Math.Max(start, first) + 1;
                start += cell.Span;
                if (overlap <= 0)
                {
                    continue;
                }

                if (overlap >= cell.Span)
                {
                    row.Cells.RemoveAt(index);
                    index--;
                    continue;
                }

                cell.Span -= overlap;
            }
        }

        model.Grid.RemoveRange(first, last - first + 1);
        model.Rows.RemoveAll(row => row.Cells.Count == 0);
    }

    /// <summary>"Zellen löschen – Zellen nach links verschieben": the cells go, each row is filled up with an empty cell at its end.</summary>
    public static void DeleteCellsShiftLeft(TableModel model, int firstRow, int lastRow, int firstColumn, int lastColumn)
    {
        for (var row = firstRow; row <= lastRow; row++)
        {
            var rowModel = model.Rows[row];
            var removed = 0;
            var start = 0;
            for (var index = 0; index < rowModel.Cells.Count; index++)
            {
                var cell = rowModel.Cells[index];
                if (start >= firstColumn && start + cell.Span - 1 <= lastColumn)
                {
                    removed += cell.Span;
                    start += cell.Span;
                    rowModel.Cells.RemoveAt(index);
                    index--;
                    continue;
                }

                start += cell.Span;
            }

            if (removed > 0)
            {
                var template = rowModel.Cells.Count > 0 ? rowModel.Cells[^1] : null;
                var filler = template?.CloneEmpty() ?? new CellModel { Properties = TableCellProperties.Empty, Paragraphs = [Paragraph.Empty()] };
                filler.Span = removed;
                rowModel.Cells.Add(filler);
            }
        }
    }

    // ---------------------------------------------------------------- merge and split

    /// <summary>
    /// Merges the cells of the rectangle into one: its first row gets one cell over the columns (the text of all cells, in reading
    /// order), the rows below continue it as a vertical merge. False when the rectangle cuts through a merged cell.
    /// </summary>
    public static bool Merge(TableModel model, int firstRow, int lastRow, int firstColumn, int lastColumn)
    {
        var paragraphs = new List<Paragraph>();
        var ranges = new List<(int Row, int First, int Last)>();
        for (var row = firstRow; row <= lastRow; row++)
        {
            var first = model.CellAt(row, firstColumn);
            var last = model.CellAt(row, lastColumn);
            if (first < 0 || last < 0 || model.ColumnOf(row, first) != firstColumn || model.ColumnOf(row, last) + model.Rows[row].Cells[last].Span - 1 != lastColumn)
            {
                return false;
            }

            ranges.Add((row, first, last));
            for (var cell = first; cell <= last; cell++)
            {
                paragraphs.AddRange(model.Rows[row].Cells[cell].Paragraphs.Where(paragraph => paragraph.Length > 0));
            }
        }

        if (ranges.Count == 1 && ranges[0].First == ranges[0].Last)
        {
            return false; // one cell: nothing to merge
        }

        foreach (var (row, first, last) in ranges)
        {
            var cells = model.Rows[row].Cells;
            var origin = cells[first];
            var merged = new CellModel
            {
                Properties = origin.Properties with
                {
                    GridSpan = lastColumn - firstColumn + 1,
                    VerticalMerge = lastRow == firstRow ? VerticalMerge.None : row == firstRow ? VerticalMerge.Restart : VerticalMerge.Continue,
                },
                Paragraphs = row == firstRow && paragraphs.Count > 0 ? paragraphs : [EmptyLike(origin.Paragraphs[0])],
            };
            cells.RemoveRange(first, last - first + 1);
            cells.Insert(first, merged);
        }

        return true;
    }

    /// <summary>
    /// Splits a cell into <paramref name="columns"/> cells side by side (the grid gets columns where the cell is narrower than
    /// that, the other rows' cells across them widen) and ends a vertical merge it starts (<paramref name="unmergeRows"/>).
    /// The text stays in the first new cell.
    /// </summary>
    public static void Split(TableModel model, int row, int cell, int columns, bool unmergeRows)
    {
        var target = model.Rows[row].Cells[cell];
        if (unmergeRows && target.Properties.VerticalMerge == VerticalMerge.Restart)
        {
            var column = model.ColumnOf(row, cell);
            target.Properties = target.Properties with { VerticalMerge = VerticalMerge.None };
            for (var below = row + 1; below < model.Rows.Count; below++)
            {
                var index = model.CellAt(below, column);
                if (index < 0 || model.Rows[below].Cells[index].Properties.VerticalMerge != VerticalMerge.Continue)
                {
                    break;
                }

                model.Rows[below].Cells[index].Properties = model.Rows[below].Cells[index].Properties with { VerticalMerge = VerticalMerge.None };
            }
        }

        if (columns <= 1)
        {
            return;
        }

        var firstColumn = model.ColumnOf(row, cell);
        var span = target.Span;
        if (span < columns)
        {
            // More grid columns: the cell's last column is divided; every other cell across it widens.
            var extra = columns - span;
            var divided = firstColumn + span - 1;
            var width = model.Grid[divided] / (extra + 1);
            model.Grid[divided] = width;
            model.Grid.InsertRange(divided + 1, Enumerable.Repeat(width, extra));
            for (var other = 0; other < model.Rows.Count; other++)
            {
                var index = model.CellAt(other, divided);
                if (index >= 0 && !(other == row && index == cell))
                {
                    model.Rows[other].Cells[index].Span += extra;
                }
            }

            span = columns;
        }

        var cells = model.Rows[row].Cells;
        var pieces = new List<CellModel>();
        for (var index = 0; index < columns; index++)
        {
            var piece = index == 0 ? target : target.CloneEmpty();
            piece.Span = (span / columns) + (index == columns - 1 ? span % columns : 0);
            pieces.Add(piece);
        }

        cells.RemoveAt(cell);
        cells.InsertRange(cell, pieces);
    }

    // ---------------------------------------------------------------- formatting of cells

    /// <summary>The cells of the rectangle (row, cell index) whose start column lies in it.</summary>
    public static IEnumerable<(int Row, int Cell)> CellsIn(TableModel model, int firstRow, int lastRow, int firstColumn, int lastColumn)
    {
        for (var row = firstRow; row <= lastRow; row++)
        {
            var start = 0;
            for (var cell = 0; cell < model.Rows[row].Cells.Count; cell++)
            {
                var span = model.Rows[row].Cells[cell].Span;
                if (start + span - 1 >= firstColumn && start <= lastColumn)
                {
                    yield return (row, cell);
                }

                start += span;
            }
        }
    }

    /// <summary>Word's border menu on a rectangle of cells: sets (or clears) the lines of the chosen kind.</summary>
    public static void SetBorders(TableModel model, int firstRow, int lastRow, int firstColumn, int lastColumn, BorderChoice choice, BorderLine line)
    {
        foreach (var (row, cell) in CellsIn(model, firstRow, lastRow, firstColumn, lastColumn).ToList())
        {
            var target = model.Rows[row].Cells[cell];
            var start = model.ColumnOf(row, cell);
            var end = start + target.Span - 1;
            var isTop = row == firstRow;
            var isBottom = row == lastRow;
            var isLeft = start <= firstColumn;
            var isRight = end >= lastColumn;
            var borders = target.Properties.Borders ?? BorderSet.Empty;
            borders = choice switch
            {
                BorderChoice.None => BorderSet.All(BorderLine.None) with { InsideHorizontal = null, InsideVertical = null },
                BorderChoice.All => new BorderSet(line, line, line, line),
                BorderChoice.Outside => borders with
                {
                    Top = isTop ? line : borders.Top,
                    Bottom = isBottom ? line : borders.Bottom,
                    Left = isLeft ? line : borders.Left,
                    Right = isRight ? line : borders.Right,
                },
                BorderChoice.Inside => borders with
                {
                    Top = !isTop ? line : borders.Top,
                    Bottom = !isBottom ? line : borders.Bottom,
                    Left = !isLeft ? line : borders.Left,
                    Right = !isRight ? line : borders.Right,
                },
                BorderChoice.Top => isTop ? borders with { Top = line } : borders,
                BorderChoice.Bottom => isBottom ? borders with { Bottom = line } : borders,
                BorderChoice.Left => isLeft ? borders with { Left = line } : borders,
                BorderChoice.Right => isRight ? borders with { Right = line } : borders,
                BorderChoice.InsideHorizontal => borders with { Top = !isTop ? line : borders.Top, Bottom = !isBottom ? line : borders.Bottom },
                BorderChoice.InsideVertical => borders with { Left = !isLeft ? line : borders.Left, Right = !isRight ? line : borders.Right },
                _ => borders,
            };
            target.Properties = target.Properties with { Borders = borders };
        }
    }
}
