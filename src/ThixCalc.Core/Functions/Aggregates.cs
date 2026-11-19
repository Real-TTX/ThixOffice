using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Functions;

/// <summary>
/// How aggregates treat values. Excel's rules: a number typed as an argument counts, a boolean or numeric text typed as an
/// argument is converted (other text: #VALUE!); inside ranges and arrays only numbers count (texts, booleans, blanks are
/// skipped) – except the "A" functions (AVERAGEA, MAXA …), which count texts as 0 and booleans as 1/0. Errors propagate.
/// </summary>
internal enum AggregateMode : byte
{
    Numbers,
    NumbersTextAsZero,
}

internal static class Aggregates
{
    /// <summary>Calls <paramref name="action"/> for every number of arguments [from, to) per the aggregate rules.</summary>
    public static void ForEachNumber(FunctionCall call, Action<double> action, int from = 0, int to = int.MaxValue, AggregateMode mode = AggregateMode.Numbers)
    {
        var last = Math.Min(to, call.Count);
        for (var index = from; index < last; index++)
        {
            if (call.IsMissing(index))
            {
                action(0);
                continue;
            }

            var operand = call[index];
            if (operand.Kind == OperandKind.Value)
            {
                var value = operand.Value;
                switch (value.Kind)
                {
                    case CellValueKind.Error:
                        throw new CalcErrorException(value.Error);
                    case CellValueKind.Blank:
                        action(0);
                        break;
                    default:
                        action(Coercion.ToNumber(value, call.Context.Locale, call.Context.Date1904));
                        break;
                }

                continue;
            }

            foreach (var value in call.ValuesOf(operand))
            {
                switch (value.Kind)
                {
                    case CellValueKind.Number:
                        action(value.Number);
                        break;
                    case CellValueKind.Error:
                        throw new CalcErrorException(value.Error);
                    case CellValueKind.Text when mode == AggregateMode.NumbersTextAsZero:
                        action(0);
                        break;
                    case CellValueKind.Boolean when mode == AggregateMode.NumbersTextAsZero:
                        action(value.Number);
                        break;
                }
            }
        }
    }

    public static List<double> Numbers(FunctionCall call, int from = 0, int to = int.MaxValue, AggregateMode mode = AggregateMode.Numbers)
    {
        var numbers = new List<double>();
        ForEachNumber(call, numbers.Add, from, to, mode);
        return numbers;
    }

    /// <summary>The numbers of one operand (a range/array: numbers only; a value: converted).</summary>
    public static List<double> NumbersOf(FunctionCall call, Operand operand, bool ignoreErrors = false)
    {
        var numbers = new List<double>();
        if (operand.Kind == OperandKind.Value)
        {
            if (operand.Value.IsError)
            {
                if (ignoreErrors)
                {
                    return numbers;
                }

                throw new CalcErrorException(operand.Value.Error);
            }

            numbers.Add(Coercion.ToNumber(operand.Value, call.Context.Locale, call.Context.Date1904));
            return numbers;
        }

        foreach (var value in call.ValuesOf(operand))
        {
            if (value.IsNumber)
            {
                numbers.Add(value.Number);
            }
            else if (value.IsError && !ignoreErrors)
            {
                throw new CalcErrorException(value.Error);
            }
        }

        return numbers;
    }

    public static double Sum(IReadOnlyList<double> values)
    {
        // Kahan summation keeps long sums of decimals close to Excel's results.
        var sum = 0.0;
        var compensation = 0.0;
        foreach (var value in values)
        {
            var y = value - compensation;
            var t = sum + y;
            compensation = t - sum - y;
            sum = t;
        }

        return sum;
    }

    public static double Mean(IReadOnlyList<double> values)
    {
        if (values.Count == 0)
        {
            throw new CalcErrorException(CellError.DivisionByZero);
        }

        return Sum(values) / values.Count;
    }

    /// <summary>The sum of squared deviations from the mean.</summary>
    public static double SquaredDeviations(IReadOnlyList<double> values)
    {
        var mean = Mean(values);
        var sum = 0.0;
        foreach (var value in values)
        {
            sum += (value - mean) * (value - mean);
        }

        return sum;
    }

    public static double Variance(IReadOnlyList<double> values, bool sample)
    {
        var minimum = sample ? 2 : 1;
        if (values.Count < minimum)
        {
            throw new CalcErrorException(CellError.DivisionByZero);
        }

        return SquaredDeviations(values) / (values.Count - (sample ? 1 : 0));
    }

    public static double Median(List<double> values)
    {
        if (values.Count == 0)
        {
            throw new CalcErrorException(CellError.Number);
        }

        values.Sort();
        var middle = values.Count / 2;
        return values.Count % 2 == 1 ? values[middle] : (values[middle - 1] + values[middle]) / 2;
    }

    /// <summary>PERCENTILE.INC on sorted values.</summary>
    public static double PercentileInclusive(List<double> sorted, double k)
    {
        if (sorted.Count == 0 || k < 0 || k > 1)
        {
            throw new CalcErrorException(CellError.Number);
        }

        var position = k * (sorted.Count - 1);
        var lower = (int)Math.Floor(position);
        var fraction = position - lower;
        return lower + 1 < sorted.Count ? sorted[lower] + fraction * (sorted[lower + 1] - sorted[lower]) : sorted[lower];
    }

    /// <summary>PERCENTILE.EXC on sorted values.</summary>
    public static double PercentileExclusive(List<double> sorted, double k)
    {
        var count = sorted.Count;
        if (count == 0 || k <= 0 || k >= 1)
        {
            throw new CalcErrorException(CellError.Number);
        }

        var position = k * (count + 1);
        if (position < 1 || position > count)
        {
            throw new CalcErrorException(CellError.Number);
        }

        var lower = (int)Math.Floor(position);
        var fraction = position - lower;
        return lower < count ? sorted[lower - 1] + fraction * (sorted[lower] - sorted[lower - 1]) : sorted[lower - 1];
    }

    /// <summary>The most frequent value (the first one reached when several are equally frequent), or #N/A.</summary>
    public static double Mode(IReadOnlyList<double> values)
    {
        var counts = new Dictionary<double, int>();
        var best = double.NaN;
        var bestCount = 1;
        foreach (var value in values)
        {
            counts[value] = counts.TryGetValue(value, out var count) ? count + 1 : 1;
        }

        foreach (var value in values)
        {
            if (counts[value] > bestCount)
            {
                bestCount = counts[value];
                best = value;
            }
        }

        if (double.IsNaN(best))
        {
            throw new CalcErrorException(CellError.NotAvailable);
        }

        return best;
    }

    // ---------------------------------------------------------------- SUBTOTAL / AGGREGATE

    /// <summary>
    /// The values of reference arguments for SUBTOTAL/AGGREGATE: cells of hidden rows skipped when asked (rows hidden by an
    /// autofilter always for SUBTOTAL), cells holding SUBTOTAL or AGGREGATE formulas skipped, errors skipped when asked.
    /// </summary>
    public static List<CellValue> SubtotalValues(FunctionCall call, int from, int to, bool skipHidden, bool skipErrors, bool skipNested = true)
    {
        var values = new List<CellValue>();
        for (var index = from; index < Math.Min(to, call.Count); index++)
        {
            var operand = call[index];
            if (operand.Kind != OperandKind.Reference)
            {
                foreach (var value in call.ValuesOf(operand))
                {
                    if (value.IsError && skipErrors)
                    {
                        continue;
                    }

                    values.Add(value);
                }

                continue;
            }

            foreach (var area in operand.Reference.Areas)
            {
                var sheet = area.Sheet;
                foreach (var (row, column, value) in call.Context.Engine.EnumerateValues(area))
                {
                    if (skipHidden ? sheet.Rows.IsHidden(row) : IsFiltered(sheet, row))
                    {
                        continue;
                    }

                    if (skipNested && sheet.GetFormula(row, column) is { } formula && IsSubtotalFormula(formula.Root))
                    {
                        continue;
                    }

                    if (value.IsError && skipErrors)
                    {
                        continue;
                    }

                    values.Add(value);
                }
            }
        }

        return values;
    }

    private static bool IsFiltered(Worksheet sheet, int row) =>
        sheet.AutoFilter is { } filter && row > filter.Range.FirstRow && row <= filter.Range.LastRow && sheet.Rows.IsHidden(row);

    private static bool IsSubtotalFormula(FormulaNode node) => node switch
    {
        FunctionNode function => function.Name is "SUBTOTAL" or "AGGREGATE" || function.Arguments.Any(IsSubtotalFormula),
        BinaryNode binary => IsSubtotalFormula(binary.Left) || IsSubtotalFormula(binary.Right),
        UnaryNode unary => IsSubtotalFormula(unary.Operand),
        ParenthesesNode parentheses => IsSubtotalFormula(parentheses.Inner),
        _ => false,
    };
}
