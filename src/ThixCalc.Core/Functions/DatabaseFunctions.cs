using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>
/// Database functions (Datenbank): DSUM, DCOUNT … over a list with a header row, a field (header text or column number)
/// and a criteria range (header row; each further row is one alternative, its cells must all match).
/// </summary>
internal static class DatabaseFunctions
{
    private static readonly ArgumentKind[] Kinds = [ArgumentKind.Reference, ArgumentKind.Value, ArgumentKind.Reference];

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Database;
        r.Add("DSUM", 3, 3, call => Result(Aggregates.Sum(Numbers(call))), Kinds);
        r.Add("DAVERAGE", 3, 3, call => Result(Aggregates.Mean(Numbers(call))), Kinds);
        r.Add("DCOUNT", 3, 3, call => call.IsMissing(1) ? Rows(call).Count : Numbers(call).Count, Kinds);
        r.Add("DCOUNTA", 3, 3, call => call.IsMissing(1) ? Rows(call).Count : Values(call).Count(value => !value.IsBlank), Kinds);
        r.Add("DMAX", 3, 3, call => Numbers(call) is { Count: > 0 } numbers ? numbers.Max() : 0, Kinds);
        r.Add("DMIN", 3, 3, call => Numbers(call) is { Count: > 0 } numbers ? numbers.Min() : 0, Kinds);
        r.Add("DPRODUCT", 3, 3, call => Numbers(call) is { Count: > 0 } numbers ? Result(numbers.Aggregate(1.0, (product, value) => product * value)) : 0, Kinds);
        r.Add("DSTDEV", 3, 3, call => Result(Math.Sqrt(Aggregates.Variance(Numbers(call), sample: true))), Kinds);
        r.Add("DSTDEVP", 3, 3, call => Result(Math.Sqrt(Aggregates.Variance(Numbers(call), sample: false))), Kinds);
        r.Add("DVAR", 3, 3, call => Result(Aggregates.Variance(Numbers(call), sample: true)), Kinds);
        r.Add("DVARP", 3, 3, call => Result(Aggregates.Variance(Numbers(call), sample: false)), Kinds);
        r.Add("DGET", 3, 3, call =>
        {
            var values = Values(call);
            if (values.Count == 0)
            {
                return CellError.Value;
            }

            return values.Count > 1 ? CellError.Number : values[0];
        }, Kinds);
    }

    private static ArrayValue Table(FunctionCall call, int index)
    {
        var operand = call[index];
        if (operand.IsError)
        {
            throw new CalcErrorException(operand.Value.Error);
        }

        var table = call.ToArray(operand);
        Require(table.Rows >= 1, CellError.Value);
        return table;
    }

    private static int FieldColumn(FunctionCall call, ArrayValue database)
    {
        var field = call.ValueOrThrow(1);
        if (field.IsNumber)
        {
            var column = (int)Math.Truncate(field.Number);
            Require(column >= 1 && column <= database.Columns, CellError.Value);
            return column - 1;
        }

        var name = Coercion.ToText(field, call.Context.Locale);
        for (var column = 0; column < database.Columns; column++)
        {
            if (ValueComparer.TextEquals(Coercion.ToText(database[0, column], call.Context.Locale), name))
            {
                return column;
            }
        }

        throw new CalcErrorException(CellError.Value);
    }

    /// <summary>The data rows (1-based in the table) that match the criteria.</summary>
    private static List<int> Rows(FunctionCall call)
    {
        var database = Table(call, 0);
        var criteria = Table(call, 2);
        var locale = call.Context.Locale;
        var conditions = new List<List<(int Column, Criterion Criterion)>>();
        for (var row = 1; row < criteria.Rows; row++)
        {
            var alternative = new List<(int, Criterion)>();
            for (var column = 0; column < criteria.Columns; column++)
            {
                var cell = criteria[row, column];
                if (cell.IsBlank)
                {
                    continue;
                }

                var header = Coercion.ToText(criteria[0, column], locale);
                var target = -1;
                for (var candidate = 0; candidate < database.Columns; candidate++)
                {
                    if (ValueComparer.TextEquals(Coercion.ToText(database[0, candidate], locale), header))
                    {
                        target = candidate;
                        break;
                    }
                }

                Require(target >= 0, CellError.Value);
                alternative.Add((target, Criterion.Parse(cell, locale, call.Context.Date1904)));
            }

            conditions.Add(alternative);
        }

        var rows = new List<int>();
        for (var row = 1; row < database.Rows; row++)
        {
            if (conditions.Count == 0 || conditions.Any(alternative => alternative.All(condition => condition.Criterion.Matches(database[row, condition.Column]))))
            {
                rows.Add(row);
            }
        }

        return rows;
    }

    private static List<CellValue> Values(FunctionCall call)
    {
        var database = Table(call, 0);
        var column = FieldColumn(call, database);
        return Rows(call).Select(row => database[row, column]).ToList();
    }

    private static List<double> Numbers(FunctionCall call)
    {
        var numbers = new List<double>();
        foreach (var value in Values(call))
        {
            if (value.IsError)
            {
                throw new CalcErrorException(value.Error);
            }

            if (value.IsNumber)
            {
                numbers.Add(value.Number);
            }
        }

        return numbers;
    }
}
