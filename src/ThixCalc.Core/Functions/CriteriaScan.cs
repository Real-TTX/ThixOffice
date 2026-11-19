using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Functions;

/// <summary>
/// The shared core of SUMIF(S), COUNTIF(S), AVERAGEIF(S), MAXIFS, MINIFS: the positions (offsets from the top-left) where
/// every criterion matches its range. Sparse: only stored cells are visited unless a criterion matches blank cells, and even
/// then only the used part of the sheets – the blank rest is counted arithmetically.
/// </summary>
internal static class CriteriaScan
{
    public sealed class Area
    {
        public Area(FunctionCall call, Operand operand)
        {
            Call = call;
            if (operand.Kind == OperandKind.Reference && operand.Reference.IsSingleArea)
            {
                Reference = operand.Reference.First;
                Rows = Reference.Value.Range.RowCount;
                Columns = Reference.Value.Range.ColumnCount;
            }
            else if (operand.Kind == OperandKind.Reference)
            {
                throw new CalcErrorException(CellError.Value);
            }
            else
            {
                Array = call.ToArray(operand);
                Rows = Array.Rows;
                Columns = Array.Columns;
            }
        }

        public FunctionCall Call { get; }

        public SheetArea? Reference { get; }

        public ArrayValue? Array { get; }

        public int Rows { get; set; }

        public int Columns { get; set; }

        public CellValue ValueAt(int row, int column)
        {
            if (Array is not null)
            {
                return row < Array.Rows && column < Array.Columns ? Array[row, column] : CellValue.Blank;
            }

            var area = Reference!.Value;
            var targetRow = area.Range.FirstRow + row;
            var targetColumn = area.Range.FirstColumn + column;
            if (targetRow > SheetLimits.LastRow || targetColumn > SheetLimits.LastColumn)
            {
                return CellValue.Blank;
            }

            return Call.Context.Engine.GetValue(area.Sheet, targetRow, targetColumn);
        }

        /// <summary>The offsets of this area that may hold something (the used part of the sheet).</summary>
        public CellRange? UsedOffsets()
        {
            if (Array is not null)
            {
                return new CellRange(0, 0, Rows - 1, Columns - 1);
            }

            var area = Reference!.Value;
            var range = new CellRange(area.Range.FirstRow, area.Range.FirstColumn,
                Math.Min(SheetLimits.LastRow, area.Range.FirstRow + Rows - 1), Math.Min(SheetLimits.LastColumn, area.Range.FirstColumn + Columns - 1));
            if (area.Sheet.UsedRange?.Intersect(range) is not { } used)
            {
                return null;
            }

            return used.Offset(-area.Range.FirstRow, -area.Range.FirstColumn);
        }

        /// <summary>The offsets of stored non-blank cells.</summary>
        public IEnumerable<(int Row, int Column)> StoredOffsets()
        {
            if (Array is not null)
            {
                for (var row = 0; row < Rows; row++)
                {
                    for (var column = 0; column < Columns; column++)
                    {
                        yield return (row, column);
                    }
                }

                yield break;
            }

            var area = Reference!.Value;
            foreach (var (row, column, _) in Call.Context.Engine.EnumerateValues(area))
            {
                yield return (row - area.Range.FirstRow, column - area.Range.FirstColumn);
            }
        }
    }

    /// <summary>
    /// The matching offsets for criteria pairs (all ranges must have the first one's size, else #VALUE!) and how many blank
    /// cells outside the used part match too (only when every criterion matches blank).
    /// </summary>
    public static (List<(int Row, int Column)> Matches, long BlankMatches) Find(IReadOnlyList<(Area Range, Criterion Criterion)> pairs)
    {
        var first = pairs[0].Range;
        foreach (var (range, _) in pairs)
        {
            if (range.Rows != first.Rows || range.Columns != first.Columns)
            {
                throw new CalcErrorException(CellError.Value);
            }
        }

        var matches = new List<(int Row, int Column)>();
        var blankMatches = pairs.All(pair => pair.Criterion.Matches(CellValue.Blank));
        if (!blankMatches)
        {
            // Candidates: the stored cells of the range whose criterion rejects blanks.
            var driver = pairs.First(pair => !pair.Criterion.Matches(CellValue.Blank));
            foreach (var (row, column) in driver.Range.StoredOffsets())
            {
                if (AllMatch(pairs, row, column))
                {
                    matches.Add((row, column));
                }
            }

            matches.Sort();
            return (matches, 0);
        }

        CellRange? bounds = null;
        foreach (var (range, _) in pairs)
        {
            if (range.UsedOffsets() is { } used)
            {
                bounds = bounds is { } existing ? existing.Union(used) : used;
            }
        }

        var total = (long)first.Rows * first.Columns;
        if (bounds is not { } scan)
        {
            return (matches, total);
        }

        for (var row = scan.FirstRow; row <= scan.LastRow; row++)
        {
            for (var column = scan.FirstColumn; column <= scan.LastColumn; column++)
            {
                if (AllMatch(pairs, row, column))
                {
                    matches.Add((row, column));
                }
            }
        }

        return (matches, total - scan.CellCount);
    }

    private static bool AllMatch(IReadOnlyList<(Area Range, Criterion Criterion)> pairs, int row, int column)
    {
        foreach (var (range, criterion) in pairs)
        {
            if (!criterion.Matches(range.ValueAt(row, column)))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>The (range, criterion) pairs of arguments [from, …) in steps of two.</summary>
    public static List<(Area Range, Criterion Criterion)> Pairs(FunctionCall call, int from)
    {
        if ((call.Count - from) % 2 != 0)
        {
            throw new CalcErrorException(CellError.Value);
        }

        var pairs = new List<(Area, Criterion)>();
        for (var index = from; index + 1 < call.Count; index += 2)
        {
            var criterion = Criterion.Parse(call.Value(index + 1), call.Context.Locale, call.Context.Date1904);
            pairs.Add((new Area(call, call[index]), criterion));
        }

        return pairs;
    }
}
