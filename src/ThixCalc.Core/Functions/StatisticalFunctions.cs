using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Statistics (Statistik): counting, averages, extremes, spread, ranks, percentiles, regression.</summary>
internal static class StatisticalFunctions
{
    private static readonly ArgumentKind[] Any = [ArgumentKind.Any];
    private static readonly ArgumentKind[] ArrayThenValue = [ArgumentKind.Array, ArgumentKind.Value];
    private static readonly ArgumentKind[] TwoArrays = [ArgumentKind.Array, ArgumentKind.Array];

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Statistical;
        r.Add("COUNT", 1, 255, Count, Any);
        r.Add("COUNTA", 1, 255, CountA, Any);
        r.Add("COUNTBLANK", 1, 1, CountBlank, [ArgumentKind.Reference]);
        r.Add("COUNTIF", 2, 2, CountIf, [ArgumentKind.Reference, ArgumentKind.Value]);
        r.Add("COUNTIFS", 2, 254, CountIf, [ArgumentKind.Reference, ArgumentKind.Value], repeatGroup: 2);
        r.Add("AVERAGE", 1, 255, call => Result(Aggregates.Mean(Aggregates.Numbers(call))), Any);
        r.Add("AVERAGEA", 1, 255, call => Result(Aggregates.Mean(Aggregates.Numbers(call, mode: AggregateMode.NumbersTextAsZero))), Any);
        r.Add("AVERAGEIF", 2, 3, AverageIf, [ArgumentKind.Reference, ArgumentKind.Value, ArgumentKind.Reference]);
        r.Add("AVERAGEIFS", 3, 255, call => MathFunctions.ConditionalAggregate(call, 1, Aggregates.Mean),
            [ArgumentKind.Reference, ArgumentKind.Reference, ArgumentKind.Value], repeatGroup: 2);
        r.Add("MAX", 1, 255, call => Result(Extreme(Aggregates.Numbers(call), max: true)), Any);
        r.Add("MIN", 1, 255, call => Result(Extreme(Aggregates.Numbers(call), max: false)), Any);
        r.Add("MAXA", 1, 255, call => Result(Extreme(Aggregates.Numbers(call, mode: AggregateMode.NumbersTextAsZero), max: true)), Any);
        r.Add("MINA", 1, 255, call => Result(Extreme(Aggregates.Numbers(call, mode: AggregateMode.NumbersTextAsZero), max: false)), Any);
        r.Add("MAXIFS", 3, 255, call => MathFunctions.ConditionalAggregate(call, 1, values => Extreme(values, max: true)),
            [ArgumentKind.Reference, ArgumentKind.Reference, ArgumentKind.Value], repeatGroup: 2);
        r.Add("MINIFS", 3, 255, call => MathFunctions.ConditionalAggregate(call, 1, values => Extreme(values, max: false)),
            [ArgumentKind.Reference, ArgumentKind.Reference, ArgumentKind.Value], repeatGroup: 2);
        r.Add("MEDIAN", 1, 255, call => Result(Aggregates.Median(Aggregates.Numbers(call))), Any);
        r.Add("MODE.SNGL", 1, 255, call => Result(Aggregates.Mode(Aggregates.Numbers(call))), Any);
        r.Alias("MODE", "MODE.SNGL");
        r.Add("MODE.MULT", 1, 255, ModeMultiple, Any);
        r.Add("STDEV.S", 1, 255, call => Result(Math.Sqrt(Aggregates.Variance(Aggregates.Numbers(call), sample: true))), Any);
        r.Add("STDEV.P", 1, 255, call => Result(Math.Sqrt(Aggregates.Variance(Aggregates.Numbers(call), sample: false))), Any);
        r.Alias("STDEV", "STDEV.S");
        r.Alias("STDEVP", "STDEV.P");
        r.Add("STDEVA", 1, 255, call => Result(Math.Sqrt(Aggregates.Variance(Aggregates.Numbers(call, mode: AggregateMode.NumbersTextAsZero), sample: true))), Any);
        r.Add("STDEVPA", 1, 255, call => Result(Math.Sqrt(Aggregates.Variance(Aggregates.Numbers(call, mode: AggregateMode.NumbersTextAsZero), sample: false))), Any);
        r.Add("VAR.S", 1, 255, call => Result(Aggregates.Variance(Aggregates.Numbers(call), sample: true)), Any);
        r.Add("VAR.P", 1, 255, call => Result(Aggregates.Variance(Aggregates.Numbers(call), sample: false)), Any);
        r.Alias("VAR", "VAR.S");
        r.Alias("VARP", "VAR.P");
        r.Add("VARA", 1, 255, call => Result(Aggregates.Variance(Aggregates.Numbers(call, mode: AggregateMode.NumbersTextAsZero), sample: true)), Any);
        r.Add("VARPA", 1, 255, call => Result(Aggregates.Variance(Aggregates.Numbers(call, mode: AggregateMode.NumbersTextAsZero), sample: false)), Any);
        r.Add("DEVSQ", 1, 255, call => Result(Aggregates.SquaredDeviations(NonEmpty(Aggregates.Numbers(call)))), Any);
        r.Add("AVEDEV", 1, 255, call =>
        {
            var numbers = NonEmpty(Aggregates.Numbers(call));
            var mean = Aggregates.Mean(numbers);
            return Result(numbers.Sum(value => Math.Abs(value - mean)) / numbers.Count);
        }, Any);
        r.Add("GEOMEAN", 1, 255, call =>
        {
            var numbers = NonEmpty(Aggregates.Numbers(call));
            Require(numbers.All(value => value > 0));
            return Result(Math.Exp(numbers.Sum(Math.Log) / numbers.Count));
        }, Any);
        r.Add("HARMEAN", 1, 255, call =>
        {
            var numbers = NonEmpty(Aggregates.Numbers(call));
            Require(numbers.All(value => value > 0));
            return Result(numbers.Count / numbers.Sum(value => 1 / value));
        }, Any);
        r.Add("TRIMMEAN", 2, 2, call =>
        {
            var numbers = NonEmpty(Aggregates.NumbersOf(call, call[0]));
            var percent = call.Number(1);
            Require(percent is >= 0 and < 1);
            var exclude = (int)Math.Floor(numbers.Count * percent / 2);
            numbers.Sort();
            return Result(Aggregates.Mean(numbers.Skip(exclude).Take(numbers.Count - 2 * exclude).ToList()));
        }, ArrayThenValue);
        r.Add("KURT", 1, 255, call => Result(Kurtosis(Aggregates.Numbers(call))), Any);
        r.Add("SKEW", 1, 255, call => Result(Skew(Aggregates.Numbers(call), population: false)), Any);
        r.Add("SKEW.P", 1, 255, call => Result(Skew(Aggregates.Numbers(call), population: true)), Any);
        r.Add("LARGE", 2, 2, call => Kth(call, largest: true), ArrayThenValue);
        r.Add("SMALL", 2, 2, call => Kth(call, largest: false), ArrayThenValue);
        r.Add("RANK.EQ", 2, 3, call => Rank(call, average: false), [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Value]);
        r.Alias("RANK", "RANK.EQ");
        r.Add("RANK.AVG", 2, 3, call => Rank(call, average: true), [ArgumentKind.Value, ArgumentKind.Reference, ArgumentKind.Value]);
        r.Add("PERCENTILE.INC", 2, 2, call => Result(Aggregates.PercentileInclusive(SortedNumbers(call), call.Number(1))), ArrayThenValue);
        r.Alias("PERCENTILE", "PERCENTILE.INC");
        r.Add("PERCENTILE.EXC", 2, 2, call => Result(Aggregates.PercentileExclusive(SortedNumbers(call), call.Number(1))), ArrayThenValue);
        r.Add("QUARTILE.INC", 2, 2, call =>
        {
            var quart = Math.Floor(call.Number(1));
            Require(quart is >= 0 and <= 4);
            return Result(Aggregates.PercentileInclusive(SortedNumbers(call), quart / 4));
        }, ArrayThenValue);
        r.Alias("QUARTILE", "QUARTILE.INC");
        r.Add("QUARTILE.EXC", 2, 2, call =>
        {
            var quart = Math.Floor(call.Number(1));
            Require(quart is >= 1 and <= 3);
            return Result(Aggregates.PercentileExclusive(SortedNumbers(call), quart / 4));
        }, ArrayThenValue);
        r.Add("PERCENTRANK.INC", 2, 3, call => PercentRank(call, exclusive: false), ArrayThenValue);
        r.Alias("PERCENTRANK", "PERCENTRANK.INC");
        r.Add("PERCENTRANK.EXC", 2, 3, call => PercentRank(call, exclusive: true), ArrayThenValue);
        r.Add("CORREL", 2, 2, call => Result(Correlation(Pairs(call, 0, 1))), TwoArrays);
        r.Alias("PEARSON", "CORREL");
        r.Add("RSQ", 2, 2, call => Result(Math.Pow(Correlation(Pairs(call, 0, 1)), 2)), TwoArrays);
        r.Add("SLOPE", 2, 2, call => Result(Regression(Pairs(call, 1, 0)).Slope), TwoArrays);
        r.Add("INTERCEPT", 2, 2, call => Result(Regression(Pairs(call, 1, 0)).Intercept), TwoArrays);
        r.Add("STEYX", 2, 2, call =>
        {
            var pairs = Pairs(call, 1, 0);
            Require(pairs.Count >= 3, CellError.DivisionByZero);
            var (slope, intercept) = Regression(pairs);
            var residual = pairs.Sum(pair => Math.Pow(pair.Y - (intercept + slope * pair.X), 2));
            return Result(Math.Sqrt(residual / (pairs.Count - 2)));
        }, TwoArrays);
        r.Add("FORECAST.LINEAR", 3, 3, call =>
        {
            var x = call.Number(0);
            var (slope, intercept) = Regression(Pairs(call, 2, 1));
            return Result(intercept + slope * x);
        }, [ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Array]);
        r.Alias("FORECAST", "FORECAST.LINEAR");
        r.Add("COVARIANCE.P", 2, 2, call => Result(Covariance(Pairs(call, 0, 1), sample: false)), TwoArrays);
        r.Alias("COVAR", "COVARIANCE.P");
        r.Add("COVARIANCE.S", 2, 2, call => Result(Covariance(Pairs(call, 0, 1), sample: true)), TwoArrays);
        r.Add("STANDARDIZE", 3, 3, call =>
        {
            var deviation = call.Number(2);
            Require(deviation > 0);
            return Result((call.Number(0) - call.Number(1)) / deviation);
        });
        r.Add("FISHER", 1, 1, call =>
        {
            var x = call.Number(0);
            Require(x is > -1 and < 1);
            return Result(0.5 * Math.Log((1 + x) / (1 - x)));
        });
        r.Number("FISHERINV", y => Math.Tanh(y));
        r.Add("PERMUT", 2, 2, call =>
        {
            var n = Coercion.Truncate(call.Number(0));
            var k = Coercion.Truncate(call.Number(1));
            Require(n > 0 && k >= 0 && k <= n);
            var result = 1.0;
            for (var index = 0; index < k; index++)
            {
                result *= n - index;
            }

            return Result(result);
        });
        r.Add("PERMUTATIONA", 2, 2, call =>
        {
            var n = Coercion.Truncate(call.Number(0));
            var k = Coercion.Truncate(call.Number(1));
            Require(n >= 0 && k >= 0);
            return Result(Math.Pow(n, k));
        });
        r.Add("FREQUENCY", 2, 2, Frequency, TwoArrays);
        r.Add("PROB", 3, 4, Probability, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value]);
        r.Add("TREND", 1, 4, call => Trend(call, exponential: false), [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);
        r.Add("GROWTH", 1, 4, call => Trend(call, exponential: true), [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);
        r.Add("LINEST", 1, 4, call => Estimate(call, exponential: false), [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);
        r.Add("LOGEST", 1, 4, call => Estimate(call, exponential: true), [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value], array: true);
    }

    private static List<double> NonEmpty(List<double> numbers)
    {
        Require(numbers.Count > 0);
        return numbers;
    }

    private static double Extreme(List<double> numbers, bool max) => numbers.Count == 0 ? 0 : max ? numbers.Max() : numbers.Min();

    private static Operand Count(FunctionCall call)
    {
        var count = 0;
        for (var index = 0; index < call.Count; index++)
        {
            var operand = call[index];
            if (operand.Kind == OperandKind.Value)
            {
                var value = operand.Value;
                if (value.IsNumber || value.IsBoolean || call.IsMissing(index)
                    || (value.IsText && Coercion.TryNumber(value, call.Context.Locale, call.Context.Date1904, out _, out _)))
                {
                    count++;
                }

                continue;
            }

            foreach (var value in call.ValuesOf(operand))
            {
                if (value.IsNumber)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Operand CountA(FunctionCall call)
    {
        var count = 0;
        for (var index = 0; index < call.Count; index++)
        {
            var operand = call[index];
            if (operand.Kind == OperandKind.Value)
            {
                count++;
                continue;
            }

            foreach (var value in call.ValuesOf(operand))
            {
                if (!value.IsBlank)
                {
                    count++;
                }
            }
        }

        return count;
    }

    private static Operand CountBlank(FunctionCall call)
    {
        var operand = call[0];
        if (operand.Kind != OperandKind.Reference)
        {
            return CellError.Value;
        }

        long total = 0;
        long filled = 0;
        foreach (var area in operand.Reference.Areas)
        {
            total += area.Range.CellCount;
            foreach (var (_, _, value) in call.Context.Engine.EnumerateValues(area))
            {
                if (!value.IsBlank && !(value.IsText && value.Text.Length == 0))
                {
                    filled++;
                }
            }
        }

        return total - filled;
    }

    private static Operand CountIf(FunctionCall call)
    {
        var (matches, blanks) = CriteriaScan.Find(CriteriaScan.Pairs(call, 0));
        return matches.Count + blanks;
    }

    private static Operand AverageIf(FunctionCall call)
    {
        var range = new CriteriaScan.Area(call, call[0]);
        var criterion = Criterion.Parse(call.Value(1), call.Context.Locale, call.Context.Date1904);
        var target = call.Has(2) ? new CriteriaScan.Area(call, call[2]) { Rows = range.Rows, Columns = range.Columns } : range;
        var (matches, _) = CriteriaScan.Find([(range, criterion)]);
        var values = new List<double>();
        foreach (var (row, column) in matches)
        {
            var value = target.ValueAt(row, column);
            if (value.IsError)
            {
                return value;
            }

            if (value.IsNumber)
            {
                values.Add(value.Number);
            }
        }

        return Result(Aggregates.Mean(values));
    }

    private static Operand ModeMultiple(FunctionCall call)
    {
        var numbers = Aggregates.Numbers(call);
        var counts = new Dictionary<double, int>();
        foreach (var number in numbers)
        {
            counts[number] = counts.TryGetValue(number, out var count) ? count + 1 : 1;
        }

        var best = counts.Count == 0 ? 0 : counts.Values.Max();
        if (best < 2)
        {
            return CellError.NotAvailable;
        }

        var modes = numbers.Distinct().Where(number => counts[number] == best).ToList();
        var result = new ArrayValue(modes.Count, 1);
        for (var index = 0; index < modes.Count; index++)
        {
            result.Values[index] = modes[index];
        }

        return result;
    }

    private static double Kurtosis(List<double> numbers)
    {
        var count = numbers.Count;
        Require(count >= 4, CellError.DivisionByZero);
        var mean = Aggregates.Mean(numbers);
        var deviation = Math.Sqrt(Aggregates.Variance(numbers, sample: true));
        Require(deviation > 0, CellError.DivisionByZero);
        var sum = numbers.Sum(value => Math.Pow((value - mean) / deviation, 4));
        double n = count;
        return n * (n + 1) / ((n - 1) * (n - 2) * (n - 3)) * sum - 3 * (n - 1) * (n - 1) / ((n - 2) * (n - 3));
    }

    private static double Skew(List<double> numbers, bool population)
    {
        var count = numbers.Count;
        Require(count >= (population ? 1 : 3), CellError.DivisionByZero);
        var mean = Aggregates.Mean(numbers);
        var deviation = Math.Sqrt(Aggregates.Variance(numbers, sample: !population));
        Require(deviation > 0, CellError.DivisionByZero);
        var sum = numbers.Sum(value => Math.Pow((value - mean) / deviation, 3));
        double n = count;
        return population ? sum / n : n / ((n - 1) * (n - 2)) * sum;
    }

    private static Operand Kth(FunctionCall call, bool largest)
    {
        var numbers = Aggregates.NumbersOf(call, call[0]);
        var k = (int)Math.Ceiling(call.Number(1) - 1e-12);
        Require(k >= 1 && k <= numbers.Count);
        numbers.Sort();
        return largest ? numbers[^k] : numbers[k - 1];
    }

    private static Operand Rank(FunctionCall call, bool average)
    {
        var number = call.Number(0);
        var numbers = Aggregates.NumbersOf(call, call[1]);
        var ascending = call.Has(2) && call.Number(2) != 0;
        var equal = numbers.Count(value => ValueComparer.CompareNumbers(value, number) == 0);
        if (equal == 0)
        {
            return CellError.NotAvailable;
        }

        var better = numbers.Count(value => ValueComparer.CompareNumbers(value, number) != 0 && (ascending ? value < number : value > number));
        return average ? better + (equal + 1) / 2.0 : better + 1;
    }

    private static List<double> SortedNumbers(FunctionCall call)
    {
        var numbers = Aggregates.NumbersOf(call, call[0]);
        numbers.Sort();
        return numbers;
    }

    private static Operand PercentRank(FunctionCall call, bool exclusive)
    {
        var numbers = SortedNumbers(call);
        var x = call.Number(1);
        var significance = call.Has(2) ? (int)Math.Truncate(call.Number(2)) : 3;
        Require(numbers.Count > 0 && significance >= 1);
        if (x < numbers[0] || x > numbers[^1])
        {
            return CellError.NotAvailable;
        }

        double RankAt(int index) => exclusive ? (index + 1.0) / (numbers.Count + 1) : numbers.Count == 1 ? 1 : index / (numbers.Count - 1.0);

        double result;
        var exact = numbers.FindIndex(value => value == x);
        if (exact >= 0)
        {
            result = RankAt(exact);
        }
        else
        {
            var lower = numbers.FindLastIndex(value => value < x);
            var fraction = (x - numbers[lower]) / (numbers[lower + 1] - numbers[lower]);
            result = RankAt(lower) + fraction * (RankAt(lower + 1) - RankAt(lower));
        }

        var factor = Math.Pow(10, significance);
        return Math.Floor(result * factor + 1e-9) / factor;
    }

    private static List<(double X, double Y)> Pairs(FunctionCall call, int xIndex, int yIndex)
    {
        var xs = call.Array(xIndex);
        var ys = call.Array(yIndex);
        Require(xs.Count == ys.Count, CellError.NotAvailable);
        var pairs = new List<(double, double)>();
        for (var index = 0; index < xs.Count; index++)
        {
            var x = xs.Values[index];
            var y = ys.Values[index];
            if (x.IsError)
            {
                throw new CalcErrorException(x.Error);
            }

            if (y.IsError)
            {
                throw new CalcErrorException(y.Error);
            }

            if (x.IsNumber && y.IsNumber)
            {
                pairs.Add((x.Number, y.Number));
            }
        }

        return pairs;
    }

    private static double Correlation(List<(double X, double Y)> pairs)
    {
        Require(pairs.Count >= 2, CellError.DivisionByZero);
        var meanX = pairs.Average(pair => pair.X);
        var meanY = pairs.Average(pair => pair.Y);
        double sxy = 0, sxx = 0, syy = 0;
        foreach (var (x, y) in pairs)
        {
            sxy += (x - meanX) * (y - meanY);
            sxx += (x - meanX) * (x - meanX);
            syy += (y - meanY) * (y - meanY);
        }

        Require(sxx > 0 && syy > 0, CellError.DivisionByZero);
        return sxy / Math.Sqrt(sxx * syy);
    }

    private static (double Slope, double Intercept) Regression(List<(double X, double Y)> pairs)
    {
        Require(pairs.Count >= 1, CellError.DivisionByZero);
        var meanX = pairs.Average(pair => pair.X);
        var meanY = pairs.Average(pair => pair.Y);
        double sxy = 0, sxx = 0;
        foreach (var (x, y) in pairs)
        {
            sxy += (x - meanX) * (y - meanY);
            sxx += (x - meanX) * (x - meanX);
        }

        Require(sxx > 0, CellError.DivisionByZero);
        var slope = sxy / sxx;
        return (slope, meanY - slope * meanX);
    }

    private static double Covariance(List<(double X, double Y)> pairs, bool sample)
    {
        Require(pairs.Count >= (sample ? 2 : 1), CellError.DivisionByZero);
        var meanX = pairs.Average(pair => pair.X);
        var meanY = pairs.Average(pair => pair.Y);
        return pairs.Sum(pair => (pair.X - meanX) * (pair.Y - meanY)) / (pairs.Count - (sample ? 1 : 0));
    }

    private static Operand Frequency(FunctionCall call)
    {
        var data = Aggregates.NumbersOf(call, call[0]);
        var bins = Aggregates.NumbersOf(call, call[1]);
        var order = Enumerable.Range(0, bins.Count).OrderBy(index => bins[index]).ToList();
        var counts = new int[bins.Count + 1];
        foreach (var value in data)
        {
            var placed = false;
            foreach (var index in order)
            {
                if (value <= bins[index])
                {
                    counts[index]++;
                    placed = true;
                    break;
                }
            }

            if (!placed)
            {
                counts[^1]++;
            }
        }

        var result = new ArrayValue(counts.Length, 1);
        for (var index = 0; index < counts.Length; index++)
        {
            result.Values[index] = counts[index];
        }

        return result;
    }

    private static Operand Probability(FunctionCall call)
    {
        var xs = call.Array(0);
        var probabilities = call.Array(1);
        Require(xs.Count == probabilities.Count, CellError.NotAvailable);
        var lower = call.Number(2);
        var upper = call.Number(3, lower);
        var total = 0.0;
        var sum = 0.0;
        for (var index = 0; index < xs.Count; index++)
        {
            if (!xs.Values[index].IsNumber || !probabilities.Values[index].IsNumber)
            {
                continue;
            }

            var probability = probabilities.Values[index].Number;
            Require(probability is >= 0 and <= 1);
            total += probability;
            var x = xs.Values[index].Number;
            if (x >= lower && x <= upper)
            {
                sum += probability;
            }
        }

        Require(Math.Abs(total - 1) < 1e-9);
        return sum;
    }

    private static List<(double X, double Y)> TrendPairs(FunctionCall call, bool exponential, out ArrayValue knownY)
    {
        knownY = call.Array(0);
        var knownX = call.Has(1) ? call.Array(1) : null;
        Require(knownX is null || knownX.Count == knownY.Count, CellError.Reference);
        var pairs = new List<(double X, double Y)>();
        for (var index = 0; index < knownY.Count; index++)
        {
            var y = knownY.Values[index];
            Require(y.IsNumber, CellError.Value);
            var x = knownX is null ? index + 1.0 : Coercion.ToNumber(knownX.Values[index], call.Context.Locale, false);
            Require(!exponential || y.Number > 0);
            pairs.Add((x, exponential ? Math.Log(y.Number) : y.Number));
        }

        return pairs;
    }

    private static (double Slope, double Intercept) Fit(List<(double X, double Y)> pairs, bool constant)
    {
        if (constant)
        {
            return Regression(pairs);
        }

        var sxy = pairs.Sum(pair => pair.X * pair.Y);
        var sxx = pairs.Sum(pair => pair.X * pair.X);
        Require(sxx > 0, CellError.DivisionByZero);
        return (sxy / sxx, 0);
    }

    private static Operand Trend(FunctionCall call, bool exponential)
    {
        var pairs = TrendPairs(call, exponential, out var knownY);
        var constant = call.Boolean(3, true);
        var (slope, intercept) = Fit(pairs, constant);
        var newX = call.Has(2) ? call.Array(2) : (call.Has(1) ? call.Array(1) : null);
        var shape = newX ?? knownY;
        var result = new ArrayValue(shape.Rows, shape.Columns);
        for (var index = 0; index < shape.Count; index++)
        {
            var x = newX is null ? index + 1.0 : Coercion.ToNumber(newX.Values[index], call.Context.Locale, false);
            var y = intercept + slope * x;
            result.Values[index] = exponential ? Math.Exp(y) : y;
        }

        return result;
    }

    private static Operand Estimate(FunctionCall call, bool exponential)
    {
        var pairs = TrendPairs(call, exponential, out _);
        var constant = call.Boolean(2, true);
        var (slope, intercept) = Fit(pairs, constant);
        var result = new ArrayValue(1, 2);
        result.Values[0] = exponential ? Math.Exp(slope) : slope;
        result.Values[1] = exponential ? Math.Exp(intercept) : intercept;
        return result;
    }
}
