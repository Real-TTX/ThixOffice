using System.Globalization;
using System.Text;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Mathematics and trigonometry (Mathematik und Trigonometrie).</summary>
internal static class MathFunctions
{
    private static readonly ArgumentKind[] Any = [ArgumentKind.Any];
    private static readonly ArgumentKind[] Arrays = [ArgumentKind.Array];

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Math;
        r.Number("ABS", Math.Abs);
        r.Number("ACOS", x => Domain(x is >= -1 and <= 1, Math.Acos(x)));
        r.Number("ACOSH", x => Domain(x >= 1, Math.Acosh(x)));
        r.Number("ACOT", x => Math.PI / 2 - Math.Atan(x));
        r.Number("ACOTH", x => Domain(Math.Abs(x) > 1, 0.5 * Math.Log((x + 1) / (x - 1))));
        r.Number("ASIN", x => Domain(x is >= -1 and <= 1, Math.Asin(x)));
        r.Number("ASINH", Math.Asinh);
        r.Number("ATAN", Math.Atan);
        r.Number("ATAN2", (x, y) => x == 0 && y == 0 ? throw new CalcErrorException(CellError.DivisionByZero) : Math.Atan2(y, x));
        r.Number("ATANH", x => Domain(x is > -1 and < 1, Math.Atanh(x)));
        r.Number("COS", x => Domain(Math.Abs(x) < 134217728, Math.Cos(x)));
        r.Number("COSH", Math.Cosh);
        r.Number("COT", x => x == 0 ? throw new CalcErrorException(CellError.DivisionByZero) : 1 / Math.Tan(x));
        r.Number("COTH", x => x == 0 ? throw new CalcErrorException(CellError.DivisionByZero) : 1 / Math.Tanh(x));
        r.Number("CSC", x => x == 0 ? throw new CalcErrorException(CellError.DivisionByZero) : 1 / Math.Sin(x));
        r.Number("CSCH", x => x == 0 ? throw new CalcErrorException(CellError.DivisionByZero) : 1 / Math.Sinh(x));
        r.Number("SEC", x => 1 / Math.Cos(x));
        r.Number("SECH", x => 1 / Math.Cosh(x));
        r.Number("SIN", x => Domain(Math.Abs(x) < 134217728, Math.Sin(x)));
        r.Number("SINH", Math.Sinh);
        r.Number("TAN", x => Domain(Math.Abs(x) < 134217728, Math.Tan(x)));
        r.Number("TANH", Math.Tanh);
        r.Number("DEGREES", x => x * 180 / Math.PI);
        r.Number("RADIANS", x => x * Math.PI / 180);
        r.Add("PI", 0, 0, _ => Math.PI);
        r.Number("EXP", Math.Exp);
        r.Number("LN", x => Domain(x > 0, Math.Log(x)));
        r.Number("LOG10", x => Domain(x > 0, Math.Log10(x)));
        r.Add("LOG", 1, 2, call =>
        {
            var value = call.Number(0);
            var logBase = call.Number(1, 10);
            Require(value > 0 && logBase > 0);
            Require(logBase != 1, CellError.DivisionByZero);
            return Result(Math.Log(value) / Math.Log(logBase));
        });
        r.Number("SQRT", x => Domain(x >= 0, Math.Sqrt(x)));
        r.Number("SQRTPI", x => Domain(x >= 0, Math.Sqrt(x * Math.PI)));
        r.Add("POWER", 2, 2, call => Evaluator.Power(call.Number(0), call.Number(1)));
        r.Number("SIGN", x => Math.Sign(x));
        r.Number("INT", Math.Floor);
        r.Number("EVEN", x => RoundAwayToMultiple(x, 2, 0));
        r.Number("ODD", Odd);
        r.Number("FACT", x => Factorial(Coercion.Truncate(x)));
        r.Number("FACTDOUBLE", x => DoubleFactorial(Coercion.Truncate(x)));
        r.Add("MOD", 2, 2, call => Result(Mod(call.Number(0), call.Number(1))));
        r.Add("QUOTIENT", 2, 2, call =>
        {
            var divisor = call.Number(1);
            Require(divisor != 0, CellError.DivisionByZero);
            return Result(Math.Truncate(call.Number(0) / divisor));
        });
        r.Add("ROUND", 2, 2, call => Result(Round(call.Number(0), call.Integer(1))));
        r.Add("ROUNDUP", 2, 2, call => Result(RoundDirected(call.Number(0), call.Integer(1), awayFromZero: true)));
        r.Add("ROUNDDOWN", 2, 2, call => Result(RoundDirected(call.Number(0), call.Integer(1), awayFromZero: false)));
        r.Add("TRUNC", 1, 2, call => Result(RoundDirected(call.Number(0), call.Integer(1, 0), awayFromZero: false)));
        r.Add("MROUND", 2, 2, call =>
        {
            var number = call.Number(0);
            var multiple = call.Number(1);
            if (multiple == 0)
            {
                return Result(0);
            }

            Require(Math.Sign(number) * Math.Sign(multiple) >= 0);
            return Result(Math.Sign(number) * Math.Abs(multiple) * Math.Floor(Math.Abs(number / multiple) + 0.5 + 1e-12));
        });
        r.Add("CEILING", 2, 2, call => Result(Ceiling(call.Number(0), call.Number(1))));
        r.Add("FLOOR", 2, 2, call => Result(Floor(call.Number(0), call.Number(1))));
        r.Add("CEILING.MATH", 1, 3, call => Result(CeilingMath(call.Number(0), call.Number(1, 1), call.Number(2, 0) != 0)));
        r.Add("FLOOR.MATH", 1, 3, call => Result(FloorMath(call.Number(0), call.Number(1, 1), call.Number(2, 0) != 0)));
        r.Add("CEILING.PRECISE", 1, 2, call => Result(ToMultiple(call.Number(0), Math.Abs(call.Number(1, 1)), Math.Ceiling)));
        r.Alias("ISO.CEILING", "CEILING.PRECISE");
        r.Add("FLOOR.PRECISE", 1, 2, call => Result(ToMultiple(call.Number(0), Math.Abs(call.Number(1, 1)), Math.Floor)));
        r.Add("COMBIN", 2, 2, call => Result(Combinations(Coercion.Truncate(call.Number(0)), Coercion.Truncate(call.Number(1)))));
        r.Add("COMBINA", 2, 2, call =>
        {
            var n = Coercion.Truncate(call.Number(0));
            var k = Coercion.Truncate(call.Number(1));
            Require(n >= 0 && k >= 0 && (n > 0 || k == 0));
            return Result(k == 0 ? 1 : Combinations(n + k - 1, k));
        });
        r.Add("GCD", 1, 255, call => Result(IntegerFold(call, Gcd)), Any);
        r.Add("LCM", 1, 255, call => Result(IntegerFold(call, (a, b) => a == 0 || b == 0 ? 0 : a / Gcd(a, b) * b)), Any);
        r.Add("MULTINOMIAL", 1, 255, call =>
        {
            var numbers = Aggregates.Numbers(call).Select(Coercion.Truncate).ToList();
            Require(numbers.All(number => number >= 0));
            var result = 1.0;
            var total = 0.0;
            foreach (var number in numbers)
            {
                total += number;
                result *= Combinations(total, number);
            }

            return Result(result);
        }, Any);
        r.Add("SUM", 1, 255, call =>
        {
            var sum = 0.0;
            var compensation = 0.0;
            Aggregates.ForEachNumber(call, value =>
            {
                var y = value - compensation;
                var t = sum + y;
                compensation = t - sum - y;
                sum = t;
            });
            return Result(sum);
        }, Any);
        r.Add("PRODUCT", 1, 255, call =>
        {
            var product = 1.0;
            var any = false;
            Aggregates.ForEachNumber(call, value =>
            {
                product *= value;
                any = true;
            });
            return Result(any ? product : 0);
        }, Any);
        r.Add("SUMSQ", 1, 255, call =>
        {
            var sum = 0.0;
            Aggregates.ForEachNumber(call, value => sum += value * value);
            return Result(sum);
        }, Any);
        r.Add("SUMPRODUCT", 1, 255, SumProduct, Arrays);
        r.Add("SUMX2MY2", 2, 2, call => Result(PairSum(call, (x, y) => x * x - y * y)), Arrays);
        r.Add("SUMX2PY2", 2, 2, call => Result(PairSum(call, (x, y) => x * x + y * y)), Arrays);
        r.Add("SUMXMY2", 2, 2, call => Result(PairSum(call, (x, y) => (x - y) * (x - y))), Arrays);
        r.Add("SUMIF", 2, 3, SumIf, [ArgumentKind.Reference, ArgumentKind.Value, ArgumentKind.Reference]);
        r.Add("SUMIFS", 3, 255, call => ConditionalAggregate(call, 1, values => Aggregates.Sum(values)),
            [ArgumentKind.Reference, ArgumentKind.Reference, ArgumentKind.Value], repeatGroup: 2);
        r.Add("SUBTOTAL", 2, 255, Subtotal, [ArgumentKind.Value, ArgumentKind.Reference]);
        r.Add("AGGREGATE", 3, 253, Aggregate, [ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Reference]);
        r.Add("RAND", 0, 0, call => call.Context.Engine.Random.NextDouble(), isVolatile: true);
        r.Add("RANDBETWEEN", 2, 2, call =>
        {
            var bottom = Math.Ceiling(call.Number(0));
            var top = Math.Floor(call.Number(1));
            Require(bottom <= top);
            return Result(bottom + Math.Floor(call.Context.Engine.Random.NextDouble() * (top - bottom + 1)));
        }, isVolatile: true);
        r.Add("RANDARRAY", 0, 5, RandomArray, isVolatile: true, array: true);
        r.Add("SEQUENCE", 1, 4, Sequence, array: true);
        r.Add("MMULT", 2, 2, MatrixMultiply, Arrays);
        r.Add("MDETERM", 1, 1, call => Result(Determinant(Matrix(call.Array(0)))), Arrays);
        r.Add("MINVERSE", 1, 1, call => Inverse(Matrix(call.Array(0))), Arrays);
        r.Add("MUNIT", 1, 1, call =>
        {
            var size = call.Integer(0);
            Require(size >= 1, CellError.Value);
            var result = ArrayValue.Filled(size, size, CellValue.Zero);
            for (var index = 0; index < size; index++)
            {
                result[index, index] = 1;
            }

            return result;
        });
        r.Add("SERIESSUM", 4, 4, call =>
        {
            var x = call.Number(0);
            var n = call.Number(1);
            var m = call.Number(2);
            var coefficients = Aggregates.NumbersOf(call, call[3]);
            var sum = 0.0;
            for (var index = 0; index < coefficients.Count; index++)
            {
                sum += coefficients[index] * Math.Pow(x, n + index * m);
            }

            return Result(sum);
        }, [ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Array]);
        r.Add("ROMAN", 1, 2, call =>
        {
            var number = Coercion.Truncate(call.Number(0));
            Require(number is >= 0 and <= 3999, CellError.Value);
            return Roman((int)number);
        });
        r.Add("ARABIC", 1, 1, call => Result(Arabic(call.Text(0))));
        r.Add("BASE", 2, 3, call =>
        {
            var number = Coercion.Truncate(call.Number(0));
            var radix = Coercion.Truncate(call.Number(1));
            var length = call.Integer(2, 0);
            Require(number is >= 0 and < 9007199254740992 && radix is >= 2 and <= 36 && length is >= 0 and <= 255);
            return ToBase((long)number, (int)radix).PadLeft(length, '0');
        });
        r.Add("DECIMAL", 2, 2, call =>
        {
            var text = call.Text(0).Trim();
            var radix = (int)Coercion.Truncate(call.Number(1));
            Require(radix is >= 2 and <= 36);
            double value = 0;
            foreach (var character in text.ToUpperInvariant())
            {
                var digit = char.IsAsciiDigit(character) ? character - '0' : character is >= 'A' and <= 'Z' ? character - 'A' + 10 : 99;
                Require(digit < radix);
                value = value * radix + digit;
            }

            return Result(value);
        });
    }

    // ---------------------------------------------------------------- helpers

    private static double Domain(bool valid, double value) => valid ? value : throw new CalcErrorException(CellError.Number);

    /// <summary>ROUND: half away from zero on the 15-digit decimal value (ROUND(2.675;2) = 2.68 as in Excel).</summary>
    public static double Round(double value, int digits)
    {
        if (value == 0 || double.IsNaN(value))
        {
            return value;
        }

        digits = Math.Clamp(digits, -308, 308);
        var precise = NumberText.RoundToSignificant(value, 15);
        if (Math.Abs(precise) < 7.9e27 && digits is >= 0 and <= 28)
        {
            return (double)Math.Round((decimal)precise, Math.Min(digits, 28), MidpointRounding.AwayFromZero);
        }

        var factor = Math.Pow(10, digits);
        var scaled = precise * factor;
        if (double.IsInfinity(scaled))
        {
            return value;
        }

        return Math.Round(scaled, MidpointRounding.AwayFromZero) / factor;
    }

    /// <summary>ROUNDUP / ROUNDDOWN / TRUNC: away from or toward zero, on the 15-digit value.</summary>
    public static double RoundDirected(double value, int digits, bool awayFromZero)
    {
        if (value == 0)
        {
            return 0;
        }

        digits = Math.Clamp(digits, -308, 308);
        var precise = NumberText.RoundToSignificant(value, 15);
        if (Math.Abs(precise) < 7.9e27 && digits is >= 0 and <= 28)
        {
            var number = (decimal)precise;
            var rounded = Math.Round(number, digits, awayFromZero ? MidpointRounding.AwayFromZero : MidpointRounding.ToZero);
            if (awayFromZero && rounded != number && Math.Abs(rounded) < Math.Abs(number))
            {
                rounded += Math.Sign(number) * (decimal)Math.Pow(10, -digits);
            }

            return (double)rounded;
        }

        var factor = Math.Pow(10, digits);
        var scaled = Math.Abs(precise) * factor;
        var result = awayFromZero ? Math.Ceiling(scaled - 1e-9) : Math.Floor(scaled + 1e-9);
        return Math.Sign(precise) * result / factor;
    }

    public static double Mod(double number, double divisor)
    {
        if (divisor == 0)
        {
            throw new CalcErrorException(CellError.DivisionByZero);
        }

        var result = number - divisor * Math.Floor(number / divisor);
        if (Math.Abs(result - divisor) < Math.Abs(divisor) * 1e-15)
        {
            return 0;
        }

        return result;
    }

    private static double Odd(double value)
    {
        var magnitude = Math.Ceiling(Math.Abs(value) - 1e-12);
        if (magnitude % 2 == 0)
        {
            magnitude++;
        }

        return value < 0 ? -magnitude : magnitude;
    }

    private static double RoundAwayToMultiple(double value, double multiple, double offset)
    {
        _ = offset;
        var magnitude = Math.Ceiling(Math.Abs(value) / multiple - 1e-12) * multiple;
        return value < 0 ? -magnitude : magnitude;
    }

    private static double ToMultiple(double value, double significance, Func<double, double> direction)
    {
        if (significance == 0 || value == 0)
        {
            return 0;
        }

        var quotient = value / significance;
        var rounded = Math.Round(quotient);
        if (Math.Abs(quotient - rounded) < 1e-11 * Math.Max(1, Math.Abs(quotient)))
        {
            quotient = rounded;
        }

        return direction(quotient) * significance;
    }

    private static double Ceiling(double number, double significance)
    {
        if (significance == 0)
        {
            return 0;
        }

        Require(!(number > 0 && significance < 0));
        if (number < 0 && significance > 0)
        {
            // Negative numbers with a positive significance round toward zero.
            return -ToMultiple(-number, significance, Math.Floor);
        }

        return ToMultiple(Math.Abs(number), Math.Abs(significance), Math.Ceiling) * Math.Sign(number);
    }

    private static double Floor(double number, double significance)
    {
        if (significance == 0)
        {
            Require(number == 0, CellError.DivisionByZero);
            return 0;
        }

        Require(!(number > 0 && significance < 0));
        if (number < 0 && significance > 0)
        {
            return -ToMultiple(-number, significance, Math.Ceiling);
        }

        return ToMultiple(Math.Abs(number), Math.Abs(significance), Math.Floor) * Math.Sign(number);
    }

    private static double CeilingMath(double number, double significance, bool awayFromZero)
    {
        significance = Math.Abs(significance);
        if (significance == 0)
        {
            return 0;
        }

        if (number < 0)
        {
            return awayFromZero ? -ToMultiple(-number, significance, Math.Ceiling) : -ToMultiple(-number, significance, Math.Floor);
        }

        return ToMultiple(number, significance, Math.Ceiling);
    }

    private static double FloorMath(double number, double significance, bool towardZero)
    {
        significance = Math.Abs(significance);
        if (significance == 0)
        {
            return 0;
        }

        if (number < 0)
        {
            return towardZero ? -ToMultiple(-number, significance, Math.Floor) : -ToMultiple(-number, significance, Math.Ceiling);
        }

        return ToMultiple(number, significance, Math.Floor);
    }

    public static double Factorial(double n)
    {
        Require(n >= 0);
        Require(n <= 170);
        var result = 1.0;
        for (var index = 2; index <= n; index++)
        {
            result *= index;
        }

        return result;
    }

    private static double DoubleFactorial(double n)
    {
        Require(n >= -1);
        var result = 1.0;
        for (var index = n; index > 1; index -= 2)
        {
            result *= index;
        }

        return result;
    }

    public static double Combinations(double n, double k)
    {
        Require(n >= 0 && k >= 0 && k <= n);
        k = Math.Min(k, n - k);
        var result = 1.0;
        for (var index = 1; index <= k; index++)
        {
            result = result * (n - k + index) / index;
        }

        return Math.Round(result);
    }

    private static double Gcd(double a, double b)
    {
        while (b != 0)
        {
            (a, b) = (b, a % b);
        }

        return a;
    }

    private static double IntegerFold(FunctionCall call, Func<double, double, double> fold)
    {
        var numbers = Aggregates.Numbers(call).Select(Coercion.Truncate).ToList();
        Require(numbers.All(number => number >= 0 && number < 9007199254740992));
        return numbers.Count == 0 ? 0 : numbers.Skip(1).Aggregate(numbers[0], fold);
    }

    private static Operand SumProduct(FunctionCall call)
    {
        var arrays = new List<ArrayValue>();
        for (var index = 0; index < call.Count; index++)
        {
            var array = call.Array(index);
            if (arrays.Count > 0 && (array.Rows != arrays[0].Rows || array.Columns != arrays[0].Columns))
            {
                return CellError.Value;
            }

            arrays.Add(array);
        }

        var sum = 0.0;
        for (var element = 0; element < arrays[0].Count; element++)
        {
            var product = 1.0;
            foreach (var array in arrays)
            {
                var value = array.Values[element];
                if (value.IsError)
                {
                    return value;
                }

                product *= value.IsNumber ? value.Number : 0;
            }

            sum += product;
        }

        return Result(sum);
    }

    private static double PairSum(FunctionCall call, Func<double, double, double> term)
    {
        var first = call.Array(0);
        var second = call.Array(1);
        Require(first.Count == second.Count, CellError.NotAvailable);
        var sum = 0.0;
        for (var index = 0; index < first.Count; index++)
        {
            var x = first.Values[index];
            var y = second.Values[index];
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
                sum += term(x.Number, y.Number);
            }
        }

        return sum;
    }

    private static Operand SumIf(FunctionCall call)
    {
        var range = new CriteriaScan.Area(call, call[0]);
        var criterion = Criterion.Parse(call.Value(1), call.Context.Locale, call.Context.Date1904);
        var target = call.Has(2) ? new CriteriaScan.Area(call, call[2]) { Rows = range.Rows, Columns = range.Columns } : range;
        var (matches, _) = CriteriaScan.Find([(range, criterion)]);
        var sum = 0.0;
        foreach (var (row, column) in matches)
        {
            var value = target.ValueAt(row, column);
            if (value.IsError)
            {
                return value;
            }

            if (value.IsNumber)
            {
                sum += value.Number;
            }
        }

        return Result(sum);
    }

    /// <summary>SUMIFS/AVERAGEIFS/MAXIFS/MINIFS: argument 0 is the value range, then (range, criterion) pairs.</summary>
    internal static Operand ConditionalAggregate(FunctionCall call, int pairsFrom, Func<List<double>, double> aggregate)
    {
        var target = new CriteriaScan.Area(call, call[0]);
        var pairs = CriteriaScan.Pairs(call, pairsFrom);
        if (pairs[0].Range.Rows != target.Rows || pairs[0].Range.Columns != target.Columns)
        {
            return CellError.Value;
        }

        var (matches, _) = CriteriaScan.Find(pairs);
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

        return Result(aggregate(values));
    }

    private static Operand Subtotal(FunctionCall call)
    {
        var code = call.Integer(0);
        var skipHidden = code > 100;
        var function = code % 100;
        Require(function is >= 1 and <= 11, CellError.Value);
        var values = Aggregates.SubtotalValues(call, 1, call.Count, skipHidden, skipErrors: false);
        return Result(ApplyAggregate(function, values, call, 0));
    }

    private static Operand Aggregate(FunctionCall call)
    {
        var function = call.Integer(0);
        var options = call.Integer(1);
        Require(function is >= 1 and <= 19 && options is >= 0 and <= 7, CellError.Value);
        var skipHidden = options is 1 or 3 or 5 or 7;
        var skipErrors = options is 2 or 3 or 6 or 7;
        var skipNested = options <= 3;
        if (function >= 14)
        {
            Require(call.Count == 4, CellError.Value);
            var values = Aggregates.SubtotalValues(call, 2, 3, skipHidden, skipErrors, skipNested);
            return Result(ApplyAggregate(function, values, call, call.Number(3)));
        }

        return Result(ApplyAggregate(function, Aggregates.SubtotalValues(call, 2, call.Count, skipHidden, skipErrors, skipNested), call, 0));
    }

    private static double ApplyAggregate(int function, List<CellValue> values, FunctionCall call, double k)
    {
        foreach (var value in values)
        {
            if (value.IsError)
            {
                throw new CalcErrorException(value.Error);
            }
        }

        var numbers = values.Where(value => value.IsNumber).Select(value => value.Number).ToList();
        _ = call;
        switch (function)
        {
            case 1:
                return Aggregates.Mean(numbers);
            case 2:
                return numbers.Count;
            case 3:
                return values.Count(value => !value.IsBlank);
            case 4:
                return numbers.Count == 0 ? 0 : numbers.Max();
            case 5:
                return numbers.Count == 0 ? 0 : numbers.Min();
            case 6:
                return numbers.Count == 0 ? 0 : numbers.Aggregate(1.0, (product, number) => product * number);
            case 7:
                return Math.Sqrt(Aggregates.Variance(numbers, sample: true));
            case 8:
                return Math.Sqrt(Aggregates.Variance(numbers, sample: false));
            case 9:
                return Aggregates.Sum(numbers);
            case 10:
                return Aggregates.Variance(numbers, sample: true);
            case 11:
                return Aggregates.Variance(numbers, sample: false);
            case 12:
                return Aggregates.Median(numbers);
            case 13:
                return Aggregates.Mode(numbers);
            case 14:
            case 15:
            {
                var position = (int)Coercion.Truncate(k);
                Require(position >= 1 && position <= numbers.Count);
                numbers.Sort();
                return function == 14 ? numbers[^position] : numbers[position - 1];
            }

            case 16:
                numbers.Sort();
                return Aggregates.PercentileInclusive(numbers, k);
            case 17:
                numbers.Sort();
                Require(k is >= 0 and <= 4);
                return Aggregates.PercentileInclusive(numbers, Math.Floor(k) / 4);
            case 18:
                numbers.Sort();
                return Aggregates.PercentileExclusive(numbers, k);
            default:
                numbers.Sort();
                Require(k is > 0 and < 4);
                return Aggregates.PercentileExclusive(numbers, Math.Floor(k) / 4);
        }
    }

    private static Operand RandomArray(FunctionCall call)
    {
        var rows = call.Integer(0, 1);
        var columns = call.Integer(1, 1);
        var minimum = call.Number(2, 0);
        var maximum = call.Number(3, 1);
        var integers = call.Boolean(4, false);
        Require(rows >= 1 && columns >= 1, CellError.Value);
        Require(minimum <= maximum, CellError.Value);
        var random = call.Context.Engine.Random;
        var result = new ArrayValue(rows, columns);
        for (var index = 0; index < result.Count; index++)
        {
            result.Values[index] = integers
                ? Math.Ceiling(minimum) + Math.Floor(random.NextDouble() * (Math.Floor(maximum) - Math.Ceiling(minimum) + 1))
                : minimum + random.NextDouble() * (maximum - minimum);
        }

        return result;
    }

    private static Operand Sequence(FunctionCall call)
    {
        var rows = call.Integer(0);
        var columns = call.Integer(1, 1);
        var start = call.Number(2, 1);
        var step = call.Number(3, 1);
        Require(rows >= 1 && columns >= 1, CellError.Value);
        Require((long)rows * columns <= SheetLimits.MaxRows * 16L, CellError.Value);
        var result = new ArrayValue(rows, columns);
        for (var index = 0; index < result.Count; index++)
        {
            result.Values[index] = start + index * step;
        }

        return result;
    }

    internal static double[,] Matrix(ArrayValue array)
    {
        var matrix = new double[array.Rows, array.Columns];
        for (var row = 0; row < array.Rows; row++)
        {
            for (var column = 0; column < array.Columns; column++)
            {
                var value = array[row, column];
                if (value.IsError)
                {
                    throw new CalcErrorException(value.Error);
                }

                Require(value.IsNumber, CellError.Value);
                matrix[row, column] = value.Number;
            }
        }

        return matrix;
    }

    private static Operand MatrixMultiply(FunctionCall call)
    {
        var left = Matrix(call.Array(0));
        var right = Matrix(call.Array(1));
        Require(left.GetLength(1) == right.GetLength(0), CellError.Value);
        var result = new ArrayValue(left.GetLength(0), right.GetLength(1));
        for (var row = 0; row < result.Rows; row++)
        {
            for (var column = 0; column < result.Columns; column++)
            {
                var sum = 0.0;
                for (var index = 0; index < left.GetLength(1); index++)
                {
                    sum += left[row, index] * right[index, column];
                }

                result[row, column] = sum;
            }
        }

        return result;
    }

    internal static double Determinant(double[,] matrix)
    {
        var size = matrix.GetLength(0);
        Require(size == matrix.GetLength(1), CellError.Value);
        var work = (double[,])matrix.Clone();
        var determinant = 1.0;
        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < size; row++)
            {
                if (Math.Abs(work[row, column]) > Math.Abs(work[pivot, column]))
                {
                    pivot = row;
                }
            }

            if (work[pivot, column] == 0)
            {
                return 0;
            }

            if (pivot != column)
            {
                for (var index = 0; index < size; index++)
                {
                    (work[pivot, index], work[column, index]) = (work[column, index], work[pivot, index]);
                }

                determinant = -determinant;
            }

            determinant *= work[column, column];
            for (var row = column + 1; row < size; row++)
            {
                var factor = work[row, column] / work[column, column];
                for (var index = column; index < size; index++)
                {
                    work[row, index] -= factor * work[column, index];
                }
            }
        }

        return determinant;
    }

    private static Operand Inverse(double[,] matrix)
    {
        var size = matrix.GetLength(0);
        Require(size == matrix.GetLength(1), CellError.Value);
        var work = new double[size, size * 2];
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                work[row, column] = matrix[row, column];
            }

            work[row, size + row] = 1;
        }

        for (var column = 0; column < size; column++)
        {
            var pivot = column;
            for (var row = column + 1; row < size; row++)
            {
                if (Math.Abs(work[row, column]) > Math.Abs(work[pivot, column]))
                {
                    pivot = row;
                }
            }

            Require(Math.Abs(work[pivot, column]) > 1e-300);
            for (var index = 0; index < size * 2; index++)
            {
                (work[pivot, index], work[column, index]) = (work[column, index], work[pivot, index]);
            }

            var divisor = work[column, column];
            for (var index = 0; index < size * 2; index++)
            {
                work[column, index] /= divisor;
            }

            for (var row = 0; row < size; row++)
            {
                if (row == column)
                {
                    continue;
                }

                var factor = work[row, column];
                for (var index = 0; index < size * 2; index++)
                {
                    work[row, index] -= factor * work[column, index];
                }
            }
        }

        var result = new ArrayValue(size, size);
        for (var row = 0; row < size; row++)
        {
            for (var column = 0; column < size; column++)
            {
                result[row, column] = work[row, size + column];
            }
        }

        return result;
    }

    private static string Roman(int number)
    {
        var values = new[] { 1000, 900, 500, 400, 100, 90, 50, 40, 10, 9, 5, 4, 1 };
        var symbols = new[] { "M", "CM", "D", "CD", "C", "XC", "L", "XL", "X", "IX", "V", "IV", "I" };
        var builder = new StringBuilder();
        for (var index = 0; index < values.Length; index++)
        {
            while (number >= values[index])
            {
                builder.Append(symbols[index]);
                number -= values[index];
            }
        }

        return builder.ToString();
    }

    private static double Arabic(string text)
    {
        text = text.Trim().ToUpperInvariant();
        var negative = text.StartsWith('-');
        text = text.TrimStart('-');
        Require(text.Length <= 255, CellError.Value);
        var total = 0;
        var previous = 0;
        for (var index = text.Length - 1; index >= 0; index--)
        {
            var value = text[index] switch
            {
                'I' => 1,
                'V' => 5,
                'X' => 10,
                'L' => 50,
                'C' => 100,
                'D' => 500,
                'M' => 1000,
                _ => throw new CalcErrorException(CellError.Value),
            };
            total += value < previous ? -value : value;
            previous = Math.Max(previous, value);
        }

        return negative ? -total : total;
    }

    internal static string ToBase(long number, int radix)
    {
        if (number == 0)
        {
            return "0";
        }

        var builder = new StringBuilder();
        while (number > 0)
        {
            var digit = (int)(number % radix);
            builder.Insert(0, (char)(digit < 10 ? '0' + digit : 'A' + digit - 10));
            number /= radix;
        }

        return builder.ToString();
    }

    internal static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);
}
