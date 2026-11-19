using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>
/// A row or column to search (of a range or an array). For ranges only the used part of the sheet is searched; values are
/// read on demand, so VLOOKUP over A:B touches what it needs.
/// </summary>
internal sealed class LookupVector
{
    private readonly ArrayValue? _array;
    private readonly SheetArea? _area;
    private readonly bool _vertical;
    private readonly int _line;
    private readonly CalcEngine? _engine;

    private LookupVector(ArrayValue? array, SheetArea? area, bool vertical, int line, int count, CalcEngine? engine)
    {
        _array = array;
        _area = area;
        _vertical = vertical;
        _line = line;
        Count = count;
        _engine = engine;
    }

    /// <summary>The length to search (bounded by the sheet's used range for references).</summary>
    public int Count { get; }

    /// <summary>The full length (rows or columns of the range).</summary>
    public int Length { get; private init; }

    /// <summary>Line <paramref name="line"/> (0-based) of the operand: a column when <paramref name="vertical"/>, else a row.</summary>
    public static LookupVector Of(FunctionCall call, Operand operand, bool vertical, int line = 0)
    {
        if (operand.Kind == OperandKind.Reference)
        {
            if (!operand.Reference.IsSingleArea)
            {
                throw new CalcErrorException(CellError.NotAvailable);
            }

            var area = operand.Reference.First;
            var length = vertical ? area.Range.RowCount : area.Range.ColumnCount;
            var count = length;
            if (area.Sheet.UsedRange is { } used)
            {
                count = vertical
                    ? Math.Clamp(used.LastRow - area.Range.FirstRow + 1, 0, length)
                    : Math.Clamp(used.LastColumn - area.Range.FirstColumn + 1, 0, length);
            }
            else
            {
                count = 0;
            }

            return new LookupVector(null, area, vertical, line, count, call.Context.Engine) { Length = length };
        }

        var array = call.ToArray(operand);
        var size = vertical ? array.Rows : array.Columns;
        return new LookupVector(array, null, vertical, line, size, null) { Length = size };
    }

    public CellValue this[int index]
    {
        get
        {
            if (_array is not null)
            {
                return _vertical ? _array[index, _line] : _array[_line, index];
            }

            var area = _area!.Value;
            var row = area.Range.FirstRow + (_vertical ? index : _line);
            var column = area.Range.FirstColumn + (_vertical ? _line : index);
            return _engine!.GetValue(area.Sheet, row, column);
        }
    }
}

/// <summary>Lookup and reference functions (Nachschlagen und Verweisen).</summary>
internal static class LookupFunctions
{
    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Lookup;
        r.Add("VLOOKUP", 3, 4, call => TableLookup(call, vertical: true), [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Value, ArgumentKind.Value]);
        r.Add("HLOOKUP", 3, 4, call => TableLookup(call, vertical: false), [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Value, ArgumentKind.Value]);
        r.Add("LOOKUP", 2, 3, Lookup, [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Reference]);
        r.Add("MATCH", 2, 3, Match, [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Value]);
        r.Add("XMATCH", 2, 4, XMatch, [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Value, ArgumentKind.Value]);
        r.Add("XLOOKUP", 3, 6, XLookup, [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Reference, ArgumentKind.Any, ArgumentKind.Value, ArgumentKind.Value], returnsReference: true);
        r.Add("INDEX", 2, 4, Index, [ArgumentKind.Reference, ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Value], returnsReference: true);
        r.Add("OFFSET", 3, 5, Offset, [ArgumentKind.Reference, ArgumentKind.Value], isVolatile: true, returnsReference: true);
        r.Add("INDIRECT", 1, 2, Indirect, isVolatile: true, returnsReference: true);
        r.Add("ROW", 0, 1, call => Position(call, rows: true), [ArgumentKind.Reference]);
        r.Add("COLUMN", 0, 1, call => Position(call, rows: false), [ArgumentKind.Reference]);
        r.Add("ROWS", 1, 1, call => Size(call, rows: true), [ArgumentKind.Reference]);
        r.Add("COLUMNS", 1, 1, call => Size(call, rows: false), [ArgumentKind.Reference]);
        r.Add("AREAS", 1, 1, call => (call.Reference(0) ?? throw new CalcErrorException(CellError.Value)).Areas.Count, [ArgumentKind.Reference]);
        r.Add("ADDRESS", 2, 5, Address);
        r.Add("TRANSPOSE", 1, 1, call => call.Array(0).Transpose(), [ArgumentKind.Array]);
        r.Add("FORMULATEXT", 1, 1, FormulaText, [ArgumentKind.Reference]);
        r.Add("HYPERLINK", 1, 2, call => call.Has(1) ? call.Value(1) : call.Value(0));
    }

    // ---------------------------------------------------------------- searching

    internal enum MatchMode
    {
        Exact = 0,
        ExactOrSmaller = -1,
        ExactOrLarger = 1,
        Wildcard = 2,
    }

    /// <summary>The first index (or the last, searching backwards) equal to the value; wildcards for texts when asked.</summary>
    internal static int FindExact(LookupVector vector, CellValue value, bool wildcards, bool backwards = false)
    {
        var useWildcards = wildcards && value.IsText && Wildcard.HasWildcards(value.Text);
        for (var step = 0; step < vector.Count; step++)
        {
            var index = backwards ? vector.Count - 1 - step : step;
            var candidate = vector[index];
            if (Equal(candidate, value, useWildcards))
            {
                return index;
            }
        }

        if (value.IsBlank || (value.IsText && value.Text.Length == 0))
        {
            // Blank cells beyond the used part.
            return vector.Count < vector.Length ? vector.Count : -1;
        }

        return -1;
    }

    private static bool Equal(CellValue candidate, CellValue value, bool wildcards)
    {
        if (wildcards)
        {
            return candidate.IsText && Wildcard.IsMatch(value.Text, candidate.Text);
        }

        if (candidate.Kind != value.Kind && !(candidate.IsBlank || value.IsBlank))
        {
            return false;
        }

        if (candidate.IsBlank != value.IsBlank)
        {
            return false;
        }

        return ValueComparer.Compare(candidate, value) == 0;
    }

    /// <summary>
    /// Binary search of a sorted vector (ascending, or descending): the last index whose value ≤ (≥ for descending) the
    /// searched value among values of its kind, or −1. Cells of other kinds are stepped over as Excel does.
    /// </summary>
    internal static int FindSorted(LookupVector vector, CellValue value, bool descending = false)
    {
        var low = 0;
        var high = vector.Count - 1;
        var result = -1;
        while (low <= high)
        {
            var middle = low + (high - low) / 2;
            var probe = middle;
            // Step over values of other kinds (blanks, texts among numbers) toward low.
            while (probe >= low && !SameClass(vector[probe], value))
            {
                probe--;
            }

            if (probe < low)
            {
                low = middle + 1;
                continue;
            }

            var comparison = ValueComparer.Compare(vector[probe], value);
            if (descending)
            {
                comparison = -comparison;
            }

            if (comparison <= 0)
            {
                result = probe;
                low = middle + 1;
            }
            else
            {
                high = probe - 1;
            }
        }

        return result;
    }

    private static bool SameClass(CellValue candidate, CellValue value) =>
        candidate.Kind == value.Kind || (candidate.IsNumber && value.IsBoolean && false);

    /// <summary>XMATCH/XLOOKUP's search: modes −1/0/1/2 and search modes 1, −1, 2, −2.</summary>
    internal static int Search(LookupVector vector, CellValue value, MatchMode mode, int searchMode)
    {
        if (searchMode is 2 or -2)
        {
            var descending = searchMode == -2;
            var index = FindSorted(vector, value, descending);
            if (index >= 0 && Equal(vector[index], value, false))
            {
                return index;
            }

            return mode switch
            {
                MatchMode.ExactOrSmaller => descending ? NextIndex(vector, index, value, smaller: true) : index,
                MatchMode.ExactOrLarger => descending ? index : NextLarger(vector, index, value),
                _ => -1,
            };
        }

        var backwards = searchMode == -1;
        var exact = FindExact(vector, value, mode == MatchMode.Wildcard, backwards);
        if (exact >= 0 || mode is MatchMode.Exact or MatchMode.Wildcard)
        {
            return exact;
        }

        // Linear search for the nearest smaller/larger value of the same kind.
        var best = -1;
        for (var step = 0; step < vector.Count; step++)
        {
            var index = backwards ? vector.Count - 1 - step : step;
            var candidate = vector[index];
            if (candidate.Kind != value.Kind)
            {
                continue;
            }

            var comparison = ValueComparer.Compare(candidate, value);
            if (mode == MatchMode.ExactOrSmaller && comparison < 0 && (best < 0 || ValueComparer.Compare(candidate, vector[best]) > 0))
            {
                best = index;
            }
            else if (mode == MatchMode.ExactOrLarger && comparison > 0 && (best < 0 || ValueComparer.Compare(candidate, vector[best]) < 0))
            {
                best = index;
            }
        }

        return best;
    }

    private static int NextLarger(LookupVector vector, int index, CellValue value)
    {
        for (var next = index + 1; next < vector.Count; next++)
        {
            if (vector[next].Kind == value.Kind && ValueComparer.Compare(vector[next], value) > 0)
            {
                return next;
            }
        }

        return -1;
    }

    private static int NextIndex(LookupVector vector, int index, CellValue value, bool smaller)
    {
        _ = smaller;
        for (var next = index + 1; next < vector.Count; next++)
        {
            if (vector[next].Kind == value.Kind && ValueComparer.Compare(vector[next], value) < 0)
            {
                return next;
            }
        }

        return -1;
    }

    // ---------------------------------------------------------------- the functions

    private static Operand TableLookup(FunctionCall call, bool vertical)
    {
        var value = call.ValueOrThrow(0);
        var table = call[1];
        if (table.IsError)
        {
            return table;
        }

        var index = call.Integer(2);
        var approximate = call.Has(3) ? call.Boolean(3) : true;
        var size = Dimensions(call, table);
        if (index < 1)
        {
            return CellError.Value;
        }

        if (index > (vertical ? size.Columns : size.Rows))
        {
            return CellError.Reference;
        }

        var keys = LookupVector.Of(call, table, vertical);
        var found = approximate ? FindSorted(keys, value) : FindExact(keys, value, wildcards: true);
        if (found < 0)
        {
            return CellError.NotAvailable;
        }

        var results = LookupVector.Of(call, table, vertical, index - 1);
        return found < results.Count ? results[found] : CellValue.Blank;
    }

    private static (int Rows, int Columns) Dimensions(FunctionCall call, Operand operand)
    {
        if (operand.Kind == OperandKind.Reference)
        {
            var range = operand.Reference.First.Range;
            return (range.RowCount, range.ColumnCount);
        }

        var array = call.ToArray(operand);
        return (array.Rows, array.Columns);
    }

    private static Operand Lookup(FunctionCall call)
    {
        var value = call.ValueOrThrow(0);
        var source = call[1];
        if (source.IsError)
        {
            return source;
        }

        var (rows, columns) = Dimensions(call, source);
        if (call.Has(2))
        {
            var keys = LookupVector.Of(call, source, rows >= columns || columns == 1);
            var found = FindSorted(keys, value);
            if (found < 0)
            {
                return CellError.NotAvailable;
            }

            var target = call[2];
            var (targetRows, targetColumns) = Dimensions(call, target);
            var results = LookupVector.Of(call, target, targetColumns == 1 || targetRows > 1 && targetColumns < targetRows);
            return found < results.Length ? results[found] : CellError.NotAvailable;
        }

        // Array form: search the first row/column, return from the last one.
        var vertical = rows >= columns;
        var vector = LookupVector.Of(call, source, vertical);
        var index = FindSorted(vector, value);
        if (index < 0)
        {
            return CellError.NotAvailable;
        }

        return LookupVector.Of(call, source, vertical, (vertical ? columns : rows) - 1)[index];
    }

    private static LookupVector Vector(FunctionCall call, Operand operand)
    {
        if (operand.IsError)
        {
            throw new CalcErrorException(operand.Value.Error);
        }

        var (rows, columns) = Dimensions(call, operand);
        if (rows > 1 && columns > 1)
        {
            throw new CalcErrorException(CellError.NotAvailable);
        }

        return LookupVector.Of(call, operand, columns == 1 && rows >= 1 && !(rows == 1 && columns > 1));
    }

    private static Operand Match(FunctionCall call)
    {
        var value = call.ValueOrThrow(0);
        var vector = Vector(call, call[1]);
        var type = call.Has(2) ? (int)Math.Truncate(call.Number(2)) : 1;
        var index = type switch
        {
            0 => FindExact(vector, value, wildcards: true),
            > 0 => FindSorted(vector, value),
            _ => FindSorted(vector, value, descending: true),
        };
        return index < 0 ? CellError.NotAvailable : index + 1;
    }

    private static Operand XMatch(FunctionCall call)
    {
        var value = call.ValueOrThrow(0);
        var vector = Vector(call, call[1]);
        var mode = (MatchMode)call.Integer(2, 0);
        var searchMode = call.Integer(3, 1);
        Require(Enum.IsDefined(mode) && searchMode is 1 or -1 or 2 or -2, CellError.Value);
        var index = Search(vector, value, mode, searchMode);
        return index < 0 ? CellError.NotAvailable : index + 1;
    }

    private static Operand XLookup(FunctionCall call)
    {
        var value = call.ValueOrThrow(0);
        var keysOperand = call[1];
        var vector = Vector(call, keysOperand);
        var mode = (MatchMode)call.Integer(4, 0);
        var searchMode = call.Integer(5, 1);
        Require(Enum.IsDefined(mode) && searchMode is 1 or -1 or 2 or -2, CellError.Value);
        var index = Search(vector, value, mode, searchMode);
        if (index < 0)
        {
            return call.Has(3) ? call[3] : CellError.NotAvailable;
        }

        var (keyRows, keyColumns) = Dimensions(call, keysOperand);
        var vertical = keyColumns == 1 && !(keyRows == 1 && keyColumns == 1 && false);
        var results = call[2];
        if (results.IsError)
        {
            return results;
        }

        var (rows, columns) = Dimensions(call, results);
        if (vertical ? rows != keyRows : columns != keyColumns)
        {
            return CellError.Value;
        }

        if (results.Kind == OperandKind.Reference)
        {
            var area = results.Reference.First;
            var range = vertical
                ? new CellRange(area.Range.FirstRow + index, area.Range.FirstColumn, area.Range.FirstRow + index, area.Range.LastColumn)
                : new CellRange(area.Range.FirstRow, area.Range.FirstColumn + index, area.Range.LastRow, area.Range.FirstColumn + index);
            return new ReferenceValue(area.Sheet, range);
        }

        var array = call.ToArray(results);
        if (vertical)
        {
            var row = new ArrayValue(1, array.Columns);
            for (var column = 0; column < array.Columns; column++)
            {
                row[0, column] = array[index, column];
            }

            return row.Count == 1 ? row.Values[0] : row;
        }

        var slice = new ArrayValue(array.Rows, 1);
        for (var row = 0; row < array.Rows; row++)
        {
            slice[row, 0] = array[row, index];
        }

        return slice.Count == 1 ? slice.Values[0] : slice;
    }

    private static Operand Index(FunctionCall call)
    {
        var source = call[0];
        if (source.IsError)
        {
            return source;
        }

        var row = call.Integer(1, 0);
        var column = call.Integer(2, 0);
        var areaNumber = call.Integer(3, 1);
        Require(row >= 0 && column >= 0, CellError.Value);
        if (source.Kind == OperandKind.Reference)
        {
            var areas = source.Reference.Areas;
            Require(areaNumber >= 1 && areaNumber <= areas.Count, CellError.Reference);
            var area = areas[areaNumber - 1];
            var range = area.Range;
            if (range.RowCount == 1 && call.IsMissing(2) && call.Count < 3)
            {
                // A single row: the one number is the column.
                (row, column) = (1, row);
            }

            Require(row <= range.RowCount && column <= range.ColumnCount, CellError.Reference);
            var firstRow = row == 0 ? range.FirstRow : range.FirstRow + row - 1;
            var lastRow = row == 0 ? range.LastRow : firstRow;
            var firstColumn = column == 0 ? range.FirstColumn : range.FirstColumn + column - 1;
            var lastColumn = column == 0 ? range.LastColumn : firstColumn;
            return new ReferenceValue(area.Sheet, new CellRange(firstRow, firstColumn, lastRow, lastColumn));
        }

        var array = call.ToArray(source);
        if (array.Rows == 1 && call.Count < 3)
        {
            (row, column) = (1, row);
        }
        else if (array.Columns == 1 && call.Count < 3)
        {
            column = 1;
        }

        Require(row <= array.Rows && column <= array.Columns, CellError.Reference);
        if (row > 0 && column > 0)
        {
            return array[row - 1, column - 1];
        }

        if (row == 0 && column == 0)
        {
            return array;
        }

        if (row == 0)
        {
            var slice = new ArrayValue(array.Rows, 1);
            for (var index = 0; index < array.Rows; index++)
            {
                slice[index, 0] = array[index, column - 1];
            }

            return slice;
        }

        var line = new ArrayValue(1, array.Columns);
        for (var index = 0; index < array.Columns; index++)
        {
            line[0, index] = array[row - 1, index];
        }

        return line;
    }

    private static Operand Offset(FunctionCall call)
    {
        var reference = call.Reference(0) ?? throw new CalcErrorException(CellError.Value);
        var area = reference.First;
        var rows = call.Integer(1);
        var columns = call.Integer(2);
        var height = call.Has(3) ? call.Integer(3) : area.Range.RowCount;
        var width = call.Has(4) ? call.Integer(4) : area.Range.ColumnCount;
        Require(height != 0 && width != 0, CellError.Reference);
        var firstRow = (long)area.Range.FirstRow + rows;
        var firstColumn = (long)area.Range.FirstColumn + columns;
        var lastRow = firstRow + (height > 0 ? height - 1 : height + 1);
        var lastColumn = firstColumn + (width > 0 ? width - 1 : width + 1);
        if (Math.Min(firstRow, lastRow) < 0 || Math.Max(firstRow, lastRow) > SheetLimits.LastRow
            || Math.Min(firstColumn, lastColumn) < 0 || Math.Max(firstColumn, lastColumn) > SheetLimits.LastColumn)
        {
            return CellError.Reference;
        }

        return new ReferenceValue(area.Sheet, new CellRange((int)firstRow, (int)firstColumn, (int)lastRow, (int)lastColumn));
    }

    private static Operand Indirect(FunctionCall call)
    {
        var text = call.Text(0).Trim();
        var a1 = call.Boolean(1, true);
        var context = call.Context;
        var syntax = new FormulaSyntax(context.Locale, a1 ? ReferenceStyle.A1 : ReferenceStyle.R1C1);
        FormulaNode node;
        try
        {
            node = FormulaParser.ParseNode(text, context.Workbook, context.Origin, syntax);
        }
        catch (FormulaParseException)
        {
            try
            {
                node = FormulaParser.ParseNode(text, context.Workbook, context.Origin, a1 ? FormulaSyntax.Canonical : syntax with { Locale = Globalization.CalcLocale.English });
            }
            catch (FormulaParseException)
            {
                return CellError.Reference;
            }
        }

        if (node is not (ReferenceNode or NameNode or BinaryNode { Operator: BinaryOperator.Range }))
        {
            return CellError.Reference;
        }

        var result = Evaluator.Evaluate(node, context);
        return result.Kind == OperandKind.Reference ? result : CellError.Reference;
    }

    private static Operand Position(FunctionCall call, bool rows)
    {
        if (call.IsMissing(0))
        {
            return (rows ? call.Context.Row : call.Context.Column) + 1;
        }

        var reference = call.Reference(0) ?? throw new CalcErrorException(CellError.Value);
        var range = reference.First.Range;
        var count = rows ? range.RowCount : range.ColumnCount;
        var first = (rows ? range.FirstRow : range.FirstColumn) + 1;
        if (count == 1 || !call.Context.ArrayMode)
        {
            return first;
        }

        var result = rows ? new ArrayValue(count, 1) : new ArrayValue(1, count);
        for (var index = 0; index < count; index++)
        {
            result.Values[index] = first + index;
        }

        return result;
    }

    private static Operand Size(FunctionCall call, bool rows)
    {
        var operand = call[0];
        if (operand.IsError)
        {
            return operand;
        }

        if (operand.Kind == OperandKind.Reference)
        {
            var range = operand.Reference.First.Range;
            return rows ? range.RowCount : range.ColumnCount;
        }

        var array = call.ToArray(operand);
        return rows ? array.Rows : array.Columns;
    }

    private static Operand Address(FunctionCall call)
    {
        var row = call.Integer(0);
        var column = call.Integer(1);
        var mode = call.Integer(2, 1);
        var a1 = call.Boolean(3, true);
        Require(row >= 1 && row <= SheetLimits.MaxRows && column >= 1 && column <= SheetLimits.MaxColumns && mode is >= 1 and <= 4, CellError.Value);
        var rowAbsolute = mode is 1 or 2;
        var columnAbsolute = mode is 1 or 3;
        string text;
        if (a1)
        {
            text = A1.Format(new CellAddress(row - 1, column - 1), columnAbsolute, rowAbsolute);
        }
        else
        {
            var locale = call.Context.Locale;
            text = locale.RowLetter + (rowAbsolute ? row.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"[{row}]")
                + locale.ColumnLetter + (columnAbsolute ? column.ToString(System.Globalization.CultureInfo.InvariantCulture) : $"[{column}]");
        }

        if (call.Has(4))
        {
            text = FormulaFormatter.QuoteSheetName(call.Text(4)) + "!" + text;
        }

        return text;
    }

    private static Operand FormulaText(FunctionCall call)
    {
        var reference = call.Reference(0) ?? throw new CalcErrorException(CellError.Value);
        var area = reference.First;
        var cell = new CellAddress(area.Range.FirstRow, area.Range.FirstColumn);
        var formula = area.Sheet.GetFormula(cell.Row, cell.Column);
        if (formula is null)
        {
            return CellError.NotAvailable;
        }

        return "=" + FormulaFormatter.Format(formula, call.Context.Workbook, cell, new FormulaSyntax(call.Context.Locale));
    }
}
