using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Dynamic array functions: FILTER, SORT, SORTBY, UNIQUE, TAKE, DROP, stacking, choosing and reshaping.</summary>
internal static class ArrayFunctions
{
    private static readonly ArgumentKind[] FirstArray = [ArgumentKind.Array, ArgumentKind.Value];

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Lookup;
        r.Add("FILTER", 2, 3, Filter, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Any], array: true);
        r.Add("SORT", 1, 4, Sort, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);
        r.Add("SORTBY", 2, 254, SortBy, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true, repeatGroup: 2);
        r.Add("UNIQUE", 1, 3, Unique, FirstArray, array: true);
        r.Add("TAKE", 2, 3, call => Slice(call, take: true), FirstArray, array: true);
        r.Add("DROP", 2, 3, call => Slice(call, take: false), FirstArray, array: true);
        r.Add("HSTACK", 1, 254, call => Stack(call, horizontal: true), [ArgumentKind.Array], array: true);
        r.Add("VSTACK", 1, 254, call => Stack(call, horizontal: false), [ArgumentKind.Array], array: true);
        r.Add("CHOOSECOLS", 2, 254, call => Choose(call, columns: true), FirstArray, array: true);
        r.Add("CHOOSEROWS", 2, 254, call => Choose(call, columns: false), FirstArray, array: true);
        r.Add("EXPAND", 2, 4, Expand, FirstArray, array: true);
        r.Add("TOCOL", 1, 3, call => ToLine(call, column: true), FirstArray, array: true);
        r.Add("TOROW", 1, 3, call => ToLine(call, column: false), FirstArray, array: true);
        r.Add("WRAPCOLS", 2, 3, call => Wrap(call, columns: true), FirstArray, array: true);
        r.Add("WRAPROWS", 2, 3, call => Wrap(call, columns: false), FirstArray, array: true);
    }

    private static Operand Result(List<CellValue[]> rows, int columns)
    {
        if (rows.Count == 0 || columns == 0)
        {
            return CellError.Calc;
        }

        var result = new ArrayValue(rows.Count, columns);
        for (var row = 0; row < rows.Count; row++)
        {
            Array.Copy(rows[row], 0, result.Values, row * columns, columns);
        }

        return result;
    }

    private static CellValue[] Row(ArrayValue array, int row)
    {
        var values = new CellValue[array.Columns];
        Array.Copy(array.Values, row * array.Columns, values, 0, array.Columns);
        return values;
    }

    private static Operand Filter(FunctionCall call)
    {
        var array = call.Array(0);
        var include = call.Array(1);
        var byRows = include.Columns == 1 && include.Rows == array.Rows;
        var byColumns = include.Rows == 1 && include.Columns == array.Columns;
        if (!byRows && !byColumns)
        {
            return CellError.Value;
        }

        var keep = new List<int>();
        var count = byRows ? array.Rows : array.Columns;
        for (var index = 0; index < count; index++)
        {
            var flag = include.Values[index];
            if (flag.IsError)
            {
                return flag;
            }

            if (Coercion.TryNumber(flag, call.Context.Locale, call.Context.Date1904, out var number, out _) ? number != 0 : throw new CalcErrorException(CellError.Value))
            {
                keep.Add(index);
            }
        }

        if (keep.Count == 0)
        {
            return call.Has(2) ? call[2] : CellError.Calc;
        }

        if (byRows)
        {
            return Result(keep.Select(row => Row(array, row)).ToList(), array.Columns);
        }

        var result = new ArrayValue(array.Rows, keep.Count);
        for (var row = 0; row < array.Rows; row++)
        {
            for (var index = 0; index < keep.Count; index++)
            {
                result[row, index] = array[row, keep[index]];
            }
        }

        return result;
    }

    /// <summary>The order of SORT: numbers, texts, booleans, errors, blanks last.</summary>
    internal static int CompareForSort(CellValue left, CellValue right)
    {
        var leftRank = SortRank(left);
        var rightRank = SortRank(right);
        if (leftRank != rightRank)
        {
            return leftRank.CompareTo(rightRank);
        }

        return leftRank is 3 or 4 ? 0 : ValueComparer.Compare(left, right);
    }

    private static int SortRank(CellValue value) => value.Kind switch
    {
        CellValueKind.Number => 0,
        CellValueKind.Text => 1,
        CellValueKind.Boolean => 2,
        CellValueKind.Error => 3,
        _ => 4,
    };

    private static Operand Sort(FunctionCall call)
    {
        var array = call.Array(0);
        var byColumn = call.Boolean(3, false);
        var indices = call.Has(1) ? call.Array(1).Values.Select(value => (int)Coercion.ToNumber(value, call.Context.Locale, false)).ToArray() : [1];
        var orders = call.Has(2) ? call.Array(2).Values.Select(value => (int)Coercion.ToNumber(value, call.Context.Locale, false)).ToArray() : [1];
        var source = byColumn ? array.Transpose() : array;
        foreach (var index in indices)
        {
            Require(index >= 1 && index <= source.Columns, CellError.Value);
        }

        Require(orders.All(order => order is 1 or -1), CellError.Value);
        var rows = Enumerable.Range(0, source.Rows).ToList();
        var keys = indices.Select((column, position) => (Column: column - 1, Descending: (position < orders.Length ? orders[position] : orders[^1]) == -1)).ToList();
        rows = rows.OrderBy(row => row, Comparer<int>.Create((first, second) =>
        {
            foreach (var (column, descending) in keys)
            {
                var comparison = CompareForSort(source[first, column], source[second, column]);
                if (comparison != 0)
                {
                    return descending && SortRank(source[first, column]) < 3 && SortRank(source[second, column]) < 3 ? -comparison : comparison;
                }
            }

            return 0;
        })).ToList();
        var sorted = new ArrayValue(source.Rows, source.Columns);
        for (var row = 0; row < rows.Count; row++)
        {
            Array.Copy(source.Values, rows[row] * source.Columns, sorted.Values, row * source.Columns, source.Columns);
        }

        return byColumn ? sorted.Transpose() : sorted;
    }

    private static Operand SortBy(FunctionCall call)
    {
        var array = call.Array(0);
        var keys = new List<(ArrayValue Values, bool Descending)>();
        for (var index = 1; index < call.Count; index += 2)
        {
            var by = call.Array(index);
            var direction = call.Has(index + 1) ? call.Integer(index + 1) : 1;
            Require(direction is 1 or -1, CellError.Value);
            keys.Add((by, direction == -1));
        }

        var byColumns = keys[0].Values.Rows == 1 && keys[0].Values.Columns == array.Columns && array.Rows != 1;
        var source = byColumns ? array.Transpose() : array;
        foreach (var (values, _) in keys)
        {
            Require(values.Count == source.Rows && (values.Rows == 1 || values.Columns == 1), CellError.Value);
        }

        var order = Enumerable.Range(0, source.Rows).OrderBy(row => row, Comparer<int>.Create((first, second) =>
        {
            foreach (var (values, descending) in keys)
            {
                var comparison = CompareForSort(values.Values[first], values.Values[second]);
                if (comparison != 0)
                {
                    return descending ? -comparison : comparison;
                }
            }

            return 0;
        })).ToList();
        var sorted = new ArrayValue(source.Rows, source.Columns);
        for (var row = 0; row < order.Count; row++)
        {
            Array.Copy(source.Values, order[row] * source.Columns, sorted.Values, row * source.Columns, source.Columns);
        }

        return byColumns ? sorted.Transpose() : sorted;
    }

    private static Operand Unique(FunctionCall call)
    {
        var array = call.Array(0);
        var byColumn = call.Boolean(1, false);
        var exactlyOnce = call.Boolean(2, false);
        var source = byColumn ? array.Transpose() : array;
        var groups = new List<(CellValue[] Row, int Count)>();
        for (var row = 0; row < source.Rows; row++)
        {
            var values = Row(source, row);
            var found = groups.FindIndex(group => SameRow(group.Row, values));
            if (found >= 0)
            {
                groups[found] = (groups[found].Row, groups[found].Count + 1);
            }
            else
            {
                groups.Add((values, 1));
            }
        }

        var rows = groups.Where(group => !exactlyOnce || group.Count == 1).Select(group => group.Row).ToList();
        var result = Result(rows, source.Columns);
        return byColumn && result.Kind == OperandKind.Array ? result.Array.Transpose() : result;
    }

    private static bool SameRow(CellValue[] first, CellValue[] second)
    {
        for (var index = 0; index < first.Length; index++)
        {
            if (first[index].Kind != second[index].Kind || ValueComparer.Compare(first[index], second[index]) != 0)
            {
                return false;
            }
        }

        return true;
    }

    private static Operand Slice(FunctionCall call, bool take)
    {
        var array = call.Array(0);
        var rows = call.Has(1) ? call.Integer(1) : (take ? array.Rows : 0);
        var columns = call.Has(2) ? call.Integer(2) : (take ? array.Columns : 0);
        (int First, int Count) Range(int size, int amount)
        {
            if (take)
            {
                var count = Math.Min(Math.Abs(amount), size);
                return amount >= 0 ? (0, count) : (size - count, count);
            }

            var dropped = Math.Min(Math.Abs(amount), size);
            return amount >= 0 ? (dropped, size - dropped) : (0, size - dropped);
        }

        var (firstRow, rowCount) = Range(array.Rows, rows);
        var (firstColumn, columnCount) = Range(array.Columns, columns);
        if (rowCount == 0 || columnCount == 0)
        {
            return CellError.Calc;
        }

        var result = new ArrayValue(rowCount, columnCount);
        for (var row = 0; row < rowCount; row++)
        {
            for (var column = 0; column < columnCount; column++)
            {
                result[row, column] = array[firstRow + row, firstColumn + column];
            }
        }

        return result;
    }

    private static Operand Stack(FunctionCall call, bool horizontal)
    {
        var arrays = Enumerable.Range(0, call.Count).Select(call.Array).ToList();
        var rows = horizontal ? arrays.Max(array => array.Rows) : arrays.Sum(array => array.Rows);
        var columns = horizontal ? arrays.Sum(array => array.Columns) : arrays.Max(array => array.Columns);
        var result = ArrayValue.Filled(rows, columns, CellValue.FromError(CellError.NotAvailable));
        var offset = 0;
        foreach (var array in arrays)
        {
            for (var row = 0; row < array.Rows; row++)
            {
                for (var column = 0; column < array.Columns; column++)
                {
                    var value = array[row, column];
                    if (horizontal)
                    {
                        result[row, offset + column] = value;
                    }
                    else
                    {
                        result[offset + row, column] = value;
                    }
                }
            }

            offset += horizontal ? array.Columns : array.Rows;
        }

        return result;
    }

    private static Operand Choose(FunctionCall call, bool columns)
    {
        var array = call.Array(0);
        var size = columns ? array.Columns : array.Rows;
        var chosen = new List<int>();
        for (var index = 1; index < call.Count; index++)
        {
            foreach (var value in call.Values(index))
            {
                var number = (int)Coercion.Truncate(Coercion.ToNumber(value, call.Context.Locale, false));
                Require(number != 0 && Math.Abs(number) <= size, CellError.Value);
                chosen.Add(number > 0 ? number - 1 : size + number);
            }
        }

        var result = columns ? new ArrayValue(array.Rows, chosen.Count) : new ArrayValue(chosen.Count, array.Columns);
        for (var position = 0; position < chosen.Count; position++)
        {
            for (var other = 0; other < (columns ? array.Rows : array.Columns); other++)
            {
                if (columns)
                {
                    result[other, position] = array[other, chosen[position]];
                }
                else
                {
                    result[position, other] = array[chosen[position], other];
                }
            }
        }

        return result;
    }

    private static Operand Expand(FunctionCall call)
    {
        var array = call.Array(0);
        var rows = call.Has(1) ? call.Integer(1) : array.Rows;
        var columns = call.Has(2) ? call.Integer(2) : array.Columns;
        Require(rows >= array.Rows && columns >= array.Columns, CellError.Value);
        var pad = call.Has(3) ? call.Value(3) : CellValue.FromError(CellError.NotAvailable);
        var result = ArrayValue.Filled(rows, columns, pad);
        for (var row = 0; row < array.Rows; row++)
        {
            for (var column = 0; column < array.Columns; column++)
            {
                result[row, column] = array[row, column];
            }
        }

        return result;
    }

    private static Operand ToLine(FunctionCall call, bool column)
    {
        var array = call.Array(0);
        var ignore = call.Integer(1, 0);
        var byColumn = call.Boolean(2, false);
        Require(ignore is >= 0 and <= 3, CellError.Value);
        var source = byColumn ? array.Transpose() : array;
        var values = source.Values.Where(value => !((ignore is 1 or 3 && value.IsBlank) || (ignore is 2 or 3 && value.IsError))).ToArray();
        if (values.Length == 0)
        {
            return CellError.Calc;
        }

        return column ? new ArrayValue(values.Length, 1, values) : new ArrayValue(1, values.Length, values);
    }

    private static Operand Wrap(FunctionCall call, bool columns)
    {
        var vector = call.Array(0);
        Require(vector.Rows == 1 || vector.Columns == 1, CellError.Value);
        var count = call.Integer(1);
        Require(count >= 1, CellError.Number);
        var pad = call.Has(2) ? call.Value(2) : CellValue.FromError(CellError.NotAvailable);
        var lines = (vector.Count + count - 1) / count;
        var result = columns ? ArrayValue.Filled(count, lines, pad) : ArrayValue.Filled(lines, count, pad);
        for (var index = 0; index < vector.Count; index++)
        {
            if (columns)
            {
                result[index % count, index / count] = vector.Values[index];
            }
            else
            {
                result[index / count, index % count] = vector.Values[index];
            }
        }

        return result;
    }
}
