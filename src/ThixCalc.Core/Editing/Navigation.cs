using ThixCalc.Core.Model;

namespace ThixCalc.Core.Editing;

public enum Direction : byte
{
    Up,
    Down,
    Left,
    Right,
}

/// <summary>
/// Moving through a sheet like Excel: one cell (skipping hidden rows/columns and stepping over merged cells), to the edge of
/// the data (Strg+Pfeil: to the end of a block of filled cells, or to the next filled cell, or to the sheet's border), the
/// current region (Strg+*: the block of filled cells around a cell, bounded by empty rows and columns).
/// </summary>
public static class Navigation
{
    public static CellAddress Step(Worksheet sheet, CellAddress cell, Direction direction)
    {
        // Leave a merged area from its far side.
        if (sheet.Merges.Find(cell.Row, cell.Column) is { } merge)
        {
            cell = direction switch
            {
                Direction.Down => cell with { Row = merge.LastRow },
                Direction.Right => cell with { Column = merge.LastColumn },
                Direction.Up => cell with { Row = merge.FirstRow },
                _ => cell with { Column = merge.FirstColumn },
            };
        }

        var next = cell;
        do
        {
            var moved = Move(next, direction);
            if (moved == next)
            {
                return cell;
            }

            next = moved;
        }
        while (IsHidden(sheet, next, direction));

        return next;
    }

    /// <summary>Strg+Pfeil.</summary>
    public static CellAddress Edge(Worksheet sheet, CellAddress cell, Direction direction)
    {
        var next = Step(sheet, cell, direction);
        if (next == cell)
        {
            return cell;
        }

        if (sheet.HasContent(cell.Row, cell.Column) && sheet.HasContent(next.Row, next.Column))
        {
            while (true)
            {
                var further = Step(sheet, next, direction);
                if (further == next || !sheet.HasContent(further.Row, further.Column))
                {
                    return next;
                }

                next = further;
            }
        }

        return NextFilled(sheet, next, direction) ?? Border(cell, direction);
    }

    /// <summary>The block of filled cells around <paramref name="cell"/> (Strg+* / Strg+A inside data).</summary>
    public static CellRange CurrentRegion(Worksheet sheet, CellAddress cell)
    {
        var region = CellRange.Cell(cell);
        bool grown;
        do
        {
            grown = false;
            var around = new CellRange(Math.Max(0, region.FirstRow - 1), Math.Max(0, region.FirstColumn - 1),
                Math.Min(SheetLimits.LastRow, region.LastRow + 1), Math.Min(SheetLimits.LastColumn, region.LastColumn + 1));
            foreach (var entry in sheet.GetCells(around))
            {
                if (!sheet.HasContent(entry.Row, entry.Column) || region.Contains(entry.Row, entry.Column))
                {
                    continue;
                }

                region = region.Union(CellRange.Cell(entry.Row, entry.Column));
                grown = true;
            }
        }
        while (grown);

        return sheet.Merges.Expand(region);
    }

    private static CellAddress Move(CellAddress cell, Direction direction) => direction switch
    {
        Direction.Up => cell with { Row = Math.Max(0, cell.Row - 1) },
        Direction.Down => cell with { Row = Math.Min(SheetLimits.LastRow, cell.Row + 1) },
        Direction.Left => cell with { Column = Math.Max(0, cell.Column - 1) },
        _ => cell with { Column = Math.Min(SheetLimits.LastColumn, cell.Column + 1) },
    };

    private static bool IsHidden(Worksheet sheet, CellAddress cell, Direction direction) => direction is Direction.Up or Direction.Down
        ? sheet.Rows.IsHidden(cell.Row)
        : sheet.Columns.IsHidden(cell.Column);

    private static CellAddress Border(CellAddress cell, Direction direction) => direction switch
    {
        Direction.Up => cell with { Row = 0 },
        Direction.Down => cell with { Row = SheetLimits.LastRow },
        Direction.Left => cell with { Column = 0 },
        _ => cell with { Column = SheetLimits.LastColumn },
    };

    /// <summary>The first filled cell from <paramref name="start"/> on (inclusive) in a direction, using the sparse storage.</summary>
    private static CellAddress? NextFilled(Worksheet sheet, CellAddress start, Direction direction)
    {
        var store = sheet.Store;
        switch (direction)
        {
            case Direction.Down:
                for (var index = store.RowLowerBound(start.Row); index < store.RowCount; index++)
                {
                    var row = store.RowKeyAt(index);
                    if (!sheet.Rows.IsHidden(row) && sheet.HasContent(row, start.Column))
                    {
                        return start with { Row = row };
                    }
                }

                return null;
            case Direction.Up:
                for (var index = store.RowLowerBound(start.Row + 1) - 1; index >= 0; index--)
                {
                    var row = store.RowKeyAt(index);
                    if (!sheet.Rows.IsHidden(row) && sheet.HasContent(row, start.Column))
                    {
                        return start with { Row = row };
                    }
                }

                return null;
            default:
                var data = store.GetRow(start.Row);
                if (data is null)
                {
                    return null;
                }

                if (direction == Direction.Right)
                {
                    for (var position = data.LowerBound(start.Column); position < data.Count; position++)
                    {
                        var column = data.ColumnAt(position);
                        if (!sheet.Columns.IsHidden(column) && sheet.HasContent(start.Row, column))
                        {
                            return start with { Column = column };
                        }
                    }

                    return null;
                }

                for (var position = data.LowerBound(start.Column + 1) - 1; position >= 0; position--)
                {
                    var column = data.ColumnAt(position);
                    if (!sheet.Columns.IsHidden(column) && sheet.HasContent(start.Row, column))
                    {
                        return start with { Column = column };
                    }
                }

                return null;
        }
    }
}
