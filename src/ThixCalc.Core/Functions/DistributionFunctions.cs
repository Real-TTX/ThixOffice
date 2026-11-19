using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;
using static ThixCalc.Core.Functions.SpecialFunctions;

namespace ThixCalc.Core.Functions;

/// <summary>Probability distributions and tests (Statistik): normal, lognormal, t, χ², F, binomial, Poisson, gamma, beta …</summary>
internal static class DistributionFunctions
{
    private static readonly ArgumentKind[] TwoArraysAndValues = [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value];

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Statistical;

        // Normal and lognormal.
        r.Add("NORM.DIST", 4, 4, call => Normal(call.Number(0), call.Number(1), Positive(call.Number(2)), call.Boolean(3)));
        r.Alias("NORMDIST", "NORM.DIST");
        r.Add("NORM.INV", 3, 3, call => Result(call.Number(1) + Positive(call.Number(2)) * NormalInverse(Probability(call.Number(0)))));
        r.Alias("NORMINV", "NORM.INV");
        r.Add("NORM.S.DIST", 2, 2, call => Normal(call.Number(0), 0, 1, call.Boolean(1)));
        r.Add("NORMSDIST", 1, 1, call => Result(NormalCdf(call.Number(0))));
        r.Add("NORM.S.INV", 1, 1, call => Result(NormalInverse(Probability(call.Number(0)))));
        r.Alias("NORMSINV", "NORM.S.INV");
        r.Number("PHI", NormalPdf);
        r.Number("GAUSS", z => NormalCdf(z) - 0.5);
        r.Add("LOGNORM.DIST", 4, 4, call =>
        {
            var x = call.Number(0);
            var mean = call.Number(1);
            var deviation = Positive(call.Number(2));
            Require(x > 0);
            var z = (Math.Log(x) - mean) / deviation;
            return Result(call.Boolean(3) ? NormalCdf(z) : NormalPdf(z) / (x * deviation));
        });
        r.Add("LOGNORMDIST", 3, 3, call =>
        {
            var x = call.Number(0);
            Require(x > 0);
            return Result(NormalCdf((Math.Log(x) - call.Number(1)) / Positive(call.Number(2))));
        });
        r.Add("LOGNORM.INV", 3, 3, call => Result(Math.Exp(call.Number(1) + Positive(call.Number(2)) * NormalInverse(Probability(call.Number(0))))));
        r.Alias("LOGINV", "LOGNORM.INV");

        // Student's t.
        r.Add("T.DIST", 3, 3, call =>
        {
            var degrees = Degrees(call.Number(1));
            var x = call.Number(0);
            return Result(call.Boolean(2) ? StudentCdf(x, degrees) : StudentPdf(x, degrees));
        });
        r.Add("T.DIST.2T", 2, 2, call =>
        {
            var x = call.Number(0);
            Require(x >= 0);
            return Result(2 * (1 - StudentCdf(x, Degrees(call.Number(1)))));
        });
        r.Add("T.DIST.RT", 2, 2, call => Result(1 - StudentCdf(call.Number(0), Degrees(call.Number(1)))));
        r.Add("TDIST", 3, 3, call =>
        {
            var x = call.Number(0);
            var tails = Coercion.Truncate(call.Number(2));
            Require(x >= 0 && tails is 1 or 2);
            return Result(tails * (1 - StudentCdf(x, Degrees(call.Number(1)))));
        });
        r.Add("T.INV", 2, 2, call => Result(StudentInverse(OpenProbability(call.Number(0)), Degrees(call.Number(1)))));
        r.Add("T.INV.2T", 2, 2, call => Result(Math.Abs(StudentInverse(OpenProbability(call.Number(0)) / 2, Degrees(call.Number(1))))));
        r.Alias("TINV", "T.INV.2T");
        r.Add("T.TEST", 4, 4, TTest, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value, ArgumentKind.Value]);
        r.Alias("TTEST", "T.TEST");

        // Chi-square.
        r.Add("CHISQ.DIST", 3, 3, call =>
        {
            var x = call.Number(0);
            var degrees = Degrees(call.Number(1));
            Require(x >= 0);
            if (call.Boolean(2))
            {
                return Result(ChiSquareCdf(x, degrees));
            }

            return Result(x == 0 && degrees == 2 ? 0.5 : Math.Exp((degrees / 2 - 1) * Math.Log(x) - x / 2 - degrees / 2 * Math.Log(2) - LogGamma(degrees / 2)));
        });
        r.Add("CHISQ.DIST.RT", 2, 2, call =>
        {
            var x = call.Number(0);
            Require(x >= 0);
            return Result(GammaQ(Degrees(call.Number(1)) / 2, x / 2));
        });
        r.Alias("CHIDIST", "CHISQ.DIST.RT");
        r.Add("CHISQ.INV", 2, 2, call =>
        {
            var p = call.Number(0);
            var degrees = Degrees(call.Number(1));
            Require(p is >= 0 and < 1);
            return Result(p == 0 ? 0 : Inverse(x => ChiSquareCdf(x, degrees), p, 0, degrees + 10));
        });
        r.Add("CHISQ.INV.RT", 2, 2, call =>
        {
            var p = call.Number(0);
            var degrees = Degrees(call.Number(1));
            Require(p is > 0 and <= 1);
            return Result(p == 1 ? 0 : Inverse(x => ChiSquareCdf(x, degrees), 1 - p, 0, degrees + 10));
        });
        r.Alias("CHIINV", "CHISQ.INV.RT");
        r.Add("CHISQ.TEST", 2, 2, ChiTest, [ArgumentKind.Array, ArgumentKind.Array]);
        r.Alias("CHITEST", "CHISQ.TEST");

        // F.
        r.Add("F.DIST", 4, 4, call =>
        {
            var x = call.Number(0);
            var d1 = Degrees(call.Number(1));
            var d2 = Degrees(call.Number(2));
            Require(x >= 0);
            if (call.Boolean(3))
            {
                return Result(FCdf(x, d1, d2));
            }

            var logDensity = 0.5 * (d1 * Math.Log(d1 * x) + d2 * Math.Log(d2) - (d1 + d2) * Math.Log(d1 * x + d2))
                - Math.Log(x) - (LogGamma(d1 / 2) + LogGamma(d2 / 2) - LogGamma((d1 + d2) / 2));
            return Result(Math.Exp(logDensity));
        });
        r.Add("F.DIST.RT", 3, 3, call =>
        {
            var x = call.Number(0);
            Require(x >= 0);
            return Result(1 - FCdf(x, Degrees(call.Number(1)), Degrees(call.Number(2))));
        });
        r.Alias("FDIST", "F.DIST.RT");
        r.Add("F.INV", 3, 3, call =>
        {
            var p = call.Number(0);
            var d1 = Degrees(call.Number(1));
            var d2 = Degrees(call.Number(2));
            Require(p is >= 0 and < 1);
            return Result(p == 0 ? 0 : Inverse(x => FCdf(x, d1, d2), p, 0, 10));
        });
        r.Add("F.INV.RT", 3, 3, call =>
        {
            var p = call.Number(0);
            var d1 = Degrees(call.Number(1));
            var d2 = Degrees(call.Number(2));
            Require(p is > 0 and <= 1);
            return Result(p == 1 ? 0 : Inverse(x => FCdf(x, d1, d2), 1 - p, 0, 10));
        });
        r.Alias("FINV", "F.INV.RT");
        r.Add("F.TEST", 2, 2, call =>
        {
            var first = Aggregates.NumbersOf(call, call[0]);
            var second = Aggregates.NumbersOf(call, call[1]);
            Require(first.Count >= 2 && second.Count >= 2, CellError.DivisionByZero);
            var v1 = Aggregates.Variance(first, sample: true);
            var v2 = Aggregates.Variance(second, sample: true);
            Require(v1 > 0 && v2 > 0, CellError.DivisionByZero);
            var cdf = FCdf(v1 / v2, first.Count - 1, second.Count - 1);
            return Result(2 * Math.Min(cdf, 1 - cdf));
        }, [ArgumentKind.Array, ArgumentKind.Array]);
        r.Alias("FTEST", "F.TEST");

        // Discrete distributions.
        r.Add("BINOM.DIST", 4, 4, call =>
        {
            var k = Coercion.Truncate(call.Number(0));
            var n = Coercion.Truncate(call.Number(1));
            var p = call.Number(2);
            Require(k >= 0 && n >= k && p is >= 0 and <= 1);
            if (!call.Boolean(3))
            {
                return Result(BinomialPmf(k, n, p));
            }

            var sum = 0.0;
            for (var index = 0; index <= k; index++)
            {
                sum += BinomialPmf(index, n, p);
            }

            return Result(Math.Min(sum, 1));
        });
        r.Alias("BINOMDIST", "BINOM.DIST");
        r.Add("BINOM.DIST.RANGE", 3, 4, call =>
        {
            var n = Coercion.Truncate(call.Number(0));
            var p = call.Number(1);
            var first = Coercion.Truncate(call.Number(2));
            var last = call.Has(3) ? Coercion.Truncate(call.Number(3)) : first;
            Require(n >= 0 && p is >= 0 and <= 1 && first >= 0 && first <= n && last >= first && last <= n);
            var sum = 0.0;
            for (var index = first; index <= last; index++)
            {
                sum += BinomialPmf(index, n, p);
            }

            return Result(sum);
        });
        r.Add("BINOM.INV", 3, 3, call =>
        {
            var n = Coercion.Truncate(call.Number(0));
            var p = call.Number(1);
            var alpha = call.Number(2);
            Require(n >= 0 && p is >= 0 and <= 1 && alpha is > 0 and < 1);
            var sum = 0.0;
            for (var k = 0; k <= n; k++)
            {
                sum += BinomialPmf(k, n, p);
                if (sum >= alpha - 1e-12)
                {
                    return k;
                }
            }

            return n;
        });
        r.Alias("CRITBINOM", "BINOM.INV");
        r.Add("NEGBINOM.DIST", 4, 4, call =>
        {
            var failures = Coercion.Truncate(call.Number(0));
            var successes = Coercion.Truncate(call.Number(1));
            var p = call.Number(2);
            Require(failures >= 0 && successes >= 1 && p is >= 0 and <= 1);
            double Pmf(double f) => Math.Exp(LogCombinations(f + successes - 1, successes - 1) + successes * Math.Log(p) + f * Math.Log(1 - p));
            if (!call.Boolean(3))
            {
                return Result(Pmf(failures));
            }

            return Result(BetaRegularized(p, successes, failures + 1));
        });
        r.Add("NEGBINOMDIST", 3, 3, call =>
        {
            var failures = Coercion.Truncate(call.Number(0));
            var successes = Coercion.Truncate(call.Number(1));
            var p = call.Number(2);
            Require(failures >= 0 && successes >= 1 && p is >= 0 and <= 1);
            return Result(Math.Exp(LogCombinations(failures + successes - 1, successes - 1) + successes * Math.Log(p) + failures * Math.Log(1 - p)));
        });
        r.Add("HYPGEOM.DIST", 5, 5, call => Hypergeometric(call, call.Boolean(4)));
        r.Add("HYPGEOMDIST", 4, 4, call => Hypergeometric(call, false));
        r.Add("POISSON.DIST", 3, 3, call =>
        {
            var x = Coercion.Truncate(call.Number(0));
            var mean = call.Number(1);
            Require(x >= 0 && mean >= 0);
            if (call.Boolean(2))
            {
                return Result(mean == 0 ? 1 : GammaQ(x + 1, mean));
            }

            return Result(mean == 0 ? (x == 0 ? 1 : 0) : Math.Exp(x * Math.Log(mean) - mean - LogGamma(x + 1)));
        });
        r.Alias("POISSON", "POISSON.DIST");

        // Continuous distributions.
        r.Add("EXPON.DIST", 3, 3, call =>
        {
            var x = call.Number(0);
            var lambda = call.Number(1);
            Require(x >= 0 && lambda > 0);
            return Result(call.Boolean(2) ? 1 - Math.Exp(-lambda * x) : lambda * Math.Exp(-lambda * x));
        });
        r.Alias("EXPONDIST", "EXPON.DIST");
        r.Add("GAMMA", 1, 1, call => Result(Gamma(call.Number(0))));
        r.Add("GAMMALN", 1, 1, call =>
        {
            var x = call.Number(0);
            Require(x > 0);
            return Result(LogGamma(x));
        });
        r.Alias("GAMMALN.PRECISE", "GAMMALN");
        r.Add("GAMMA.DIST", 4, 4, call =>
        {
            var x = call.Number(0);
            var alpha = call.Number(1);
            var beta = call.Number(2);
            Require(x >= 0 && alpha > 0 && beta > 0);
            if (call.Boolean(3))
            {
                return Result(GammaP(alpha, x / beta));
            }

            return Result(x == 0 ? (alpha == 1 ? 1 / beta : 0) : Math.Exp((alpha - 1) * Math.Log(x) - x / beta - alpha * Math.Log(beta) - LogGamma(alpha)));
        });
        r.Alias("GAMMADIST", "GAMMA.DIST");
        r.Add("GAMMA.INV", 3, 3, call =>
        {
            var p = call.Number(0);
            var alpha = call.Number(1);
            var beta = call.Number(2);
            Require(p is >= 0 and < 1 && alpha > 0 && beta > 0);
            return Result(p == 0 ? 0 : Inverse(x => GammaP(alpha, x / beta), p, 0, alpha * beta + 10 * beta));
        });
        r.Alias("GAMMAINV", "GAMMA.INV");
        r.Add("BETA.DIST", 4, 6, call =>
        {
            var (x, alpha, beta, low, high) = BetaArguments(call, 0, 1, 2, call.Has(4) ? 4 : -1, call.Has(5) ? 5 : -1);
            var scaled = (x - low) / (high - low);
            if (call.Boolean(3))
            {
                return Result(BetaRegularized(scaled, alpha, beta));
            }

            var logDensity = (alpha - 1) * Math.Log(scaled) + (beta - 1) * Math.Log(1 - scaled) - (LogGamma(alpha) + LogGamma(beta) - LogGamma(alpha + beta));
            return Result(Math.Exp(logDensity) / (high - low));
        });
        r.Add("BETADIST", 3, 5, call =>
        {
            var (x, alpha, beta, low, high) = BetaArguments(call, 0, 1, 2, call.Has(3) ? 3 : -1, call.Has(4) ? 4 : -1);
            return Result(BetaRegularized((x - low) / (high - low), alpha, beta));
        });
        r.Add("BETA.INV", 3, 5, call =>
        {
            var p = call.Number(0);
            var alpha = call.Number(1);
            var beta = call.Number(2);
            var low = call.Number(3, 0);
            var high = call.Number(4, 1);
            Require(p is > 0 and < 1 && alpha > 0 && beta > 0 && low < high);
            return Result(low + (high - low) * Inverse(x => BetaRegularized(x, alpha, beta), p, 0, 1, expandHigh: false));
        });
        r.Alias("BETAINV", "BETA.INV");
        r.Add("WEIBULL.DIST", 4, 4, call =>
        {
            var x = call.Number(0);
            var alpha = call.Number(1);
            var beta = call.Number(2);
            Require(x >= 0 && alpha > 0 && beta > 0);
            var power = Math.Pow(x / beta, alpha);
            return Result(call.Boolean(3) ? 1 - Math.Exp(-power) : alpha / beta * Math.Pow(x / beta, alpha - 1) * Math.Exp(-power));
        });
        r.Alias("WEIBULL", "WEIBULL.DIST");

        // Confidence and z test.
        r.Add("CONFIDENCE.NORM", 3, 3, call =>
        {
            var (alpha, deviation, size) = ConfidenceArguments(call);
            return Result(NormalInverse(1 - alpha / 2) * deviation / Math.Sqrt(size));
        });
        r.Alias("CONFIDENCE", "CONFIDENCE.NORM");
        r.Add("CONFIDENCE.T", 3, 3, call =>
        {
            var (alpha, deviation, size) = ConfidenceArguments(call);
            Require(size > 1, CellError.DivisionByZero);
            return Result(Math.Abs(StudentInverse(alpha / 2, size - 1)) * deviation / Math.Sqrt(size));
        });
        r.Add("Z.TEST", 2, 3, call =>
        {
            var numbers = Aggregates.NumbersOf(call, call[0]);
            Require(numbers.Count > 0, CellError.NotAvailable);
            var x = call.Number(1);
            var sigma = call.Has(2) ? call.Number(2) : Math.Sqrt(Aggregates.Variance(numbers, sample: true));
            Require(sigma > 0, CellError.DivisionByZero);
            return Result(1 - NormalCdf((Aggregates.Mean(numbers) - x) / (sigma / Math.Sqrt(numbers.Count))));
        }, [ArgumentKind.Array, ArgumentKind.Value, ArgumentKind.Value]);
        r.Alias("ZTEST", "Z.TEST");
        _ = TwoArraysAndValues;
    }

    private static double Positive(double value)
    {
        Require(value > 0);
        return value;
    }

    private static double Probability(double p)
    {
        Require(p is > 0 and < 1);
        return p;
    }

    private static double OpenProbability(double p)
    {
        Require(p is > 0 and <= 1);
        return p;
    }

    private static double Degrees(double degrees)
    {
        degrees = Coercion.Truncate(degrees);
        Require(degrees >= 1 && degrees <= 1e10);
        return degrees;
    }

    private static Operand Normal(double x, double mean, double deviation, bool cumulative)
    {
        var z = (x - mean) / deviation;
        return Result(cumulative ? NormalCdf(z) : NormalPdf(z) / deviation);
    }

    /// <summary>The left-tailed t quantile.</summary>
    private static double StudentInverse(double p, double degrees)
    {
        if (p == 0.5)
        {
            return 0;
        }

        if (p == 1)
        {
            throw new CalcErrorException(CellError.Number);
        }

        var upper = p > 0.5;
        var tail = upper ? 1 - p : p;
        var t = Inverse(x => StudentCdf(x, degrees), 1 - tail, 0, 10);
        return upper ? t : -t;
    }

    private static Operand TTest(FunctionCall call)
    {
        var first = call.Array(0);
        var second = call.Array(1);
        var tails = Coercion.Truncate(call.Number(2));
        var type = Coercion.Truncate(call.Number(3));
        Require(tails is 1 or 2 && type is 1 or 2 or 3);
        double t;
        double degrees;
        if (type == 1)
        {
            Require(first.Count == second.Count, CellError.NotAvailable);
            var differences = new List<double>();
            for (var index = 0; index < first.Count; index++)
            {
                if (first.Values[index].IsNumber && second.Values[index].IsNumber)
                {
                    differences.Add(first.Values[index].Number - second.Values[index].Number);
                }
            }

            Require(differences.Count >= 2, CellError.DivisionByZero);
            var deviation = Math.Sqrt(Aggregates.Variance(differences, sample: true));
            Require(deviation > 0, CellError.DivisionByZero);
            t = Aggregates.Mean(differences) / (deviation / Math.Sqrt(differences.Count));
            degrees = differences.Count - 1;
        }
        else
        {
            var xs = first.Values.Where(value => value.IsNumber).Select(value => value.Number).ToList();
            var ys = second.Values.Where(value => value.IsNumber).Select(value => value.Number).ToList();
            Require(xs.Count >= 2 && ys.Count >= 2, CellError.DivisionByZero);
            var v1 = Aggregates.Variance(xs, sample: true);
            var v2 = Aggregates.Variance(ys, sample: true);
            var difference = Aggregates.Mean(xs) - Aggregates.Mean(ys);
            if (type == 2)
            {
                var pooled = ((xs.Count - 1) * v1 + (ys.Count - 1) * v2) / (xs.Count + ys.Count - 2);
                t = difference / Math.Sqrt(pooled * (1.0 / xs.Count + 1.0 / ys.Count));
                degrees = xs.Count + ys.Count - 2;
            }
            else
            {
                var a = v1 / xs.Count;
                var b = v2 / ys.Count;
                t = difference / Math.Sqrt(a + b);
                degrees = (a + b) * (a + b) / (a * a / (xs.Count - 1) + b * b / (ys.Count - 1));
            }
        }

        var oneTail = 1 - StudentCdf(Math.Abs(t), degrees);
        return Result(tails * oneTail);
    }

    private static Operand ChiTest(FunctionCall call)
    {
        var actual = call.Array(0);
        var expected = call.Array(1);
        Require(actual.Rows == expected.Rows && actual.Columns == expected.Columns, CellError.NotAvailable);
        var chi = 0.0;
        for (var index = 0; index < actual.Count; index++)
        {
            if (!actual.Values[index].IsNumber || !expected.Values[index].IsNumber)
            {
                continue;
            }

            var e = expected.Values[index].Number;
            Require(e != 0, CellError.DivisionByZero);
            chi += Math.Pow(actual.Values[index].Number - e, 2) / e;
        }

        var degrees = actual.Rows > 1 && actual.Columns > 1 ? (actual.Rows - 1) * (actual.Columns - 1) : actual.Count - 1;
        Require(degrees >= 1, CellError.NotAvailable);
        return Result(GammaQ(degrees / 2.0, chi / 2));
    }

    private static Operand Hypergeometric(FunctionCall call, bool cumulative)
    {
        var successes = Coercion.Truncate(call.Number(0));
        var sample = Coercion.Truncate(call.Number(1));
        var populationSuccesses = Coercion.Truncate(call.Number(2));
        var population = Coercion.Truncate(call.Number(3));
        Require(successes >= 0 && sample > 0 && sample <= population && populationSuccesses > 0 && populationSuccesses <= population
            && successes <= Math.Min(sample, populationSuccesses) && successes >= Math.Max(0, sample - population + populationSuccesses));
        double Pmf(double k) => Math.Exp(LogCombinations(populationSuccesses, k) + LogCombinations(population - populationSuccesses, sample - k) - LogCombinations(population, sample));
        if (!cumulative)
        {
            return Result(Pmf(successes));
        }

        var sum = 0.0;
        for (var k = Math.Max(0, sample - population + populationSuccesses); k <= successes; k++)
        {
            sum += Pmf(k);
        }

        return Result(Math.Min(sum, 1));
    }

    private static (double X, double Alpha, double Beta, double Low, double High) BetaArguments(FunctionCall call, int x, int alpha, int beta, int low, int high)
    {
        var value = call.Number(x);
        var a = call.Number(alpha);
        var b = call.Number(beta);
        var lower = low >= 0 ? call.Number(low) : 0;
        var upper = high >= 0 ? call.Number(high) : 1;
        Require(a > 0 && b > 0 && lower < upper && value >= lower && value <= upper);
        return (value, a, b, lower, upper);
    }

    private static (double Alpha, double Deviation, double Size) ConfidenceArguments(FunctionCall call)
    {
        var alpha = call.Number(0);
        var deviation = call.Number(1);
        var size = Coercion.Truncate(call.Number(2));
        Require(alpha is > 0 and < 1 && deviation > 0 && size >= 1);
        return (alpha, deviation, size);
    }
}
