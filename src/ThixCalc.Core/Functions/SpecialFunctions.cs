using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Functions;

/// <summary>
/// The special functions behind the distributions, from their mathematical definitions: log-gamma (Lanczos), the regularized
/// incomplete gamma (series / continued fraction) and beta (continued fraction, modified Lentz), erf/erfc, the normal
/// distribution and its inverse (rational approximation refined by Halley steps), and a bracketing inverse for any CDF.
/// </summary>
internal static class SpecialFunctions
{
    private const double Epsilon = 1e-15;
    private const int MaxIterations = 1000;

    private static readonly double[] Lanczos =
    [
        0.99999999999980993, 676.5203681218851, -1259.1392167224028, 771.32342877765313, -176.61502916214059,
        12.507343278686905, -0.13857109526572012, 9.9843695780195716e-6, 1.5056327351493116e-7,
    ];

    public static double LogGamma(double x)
    {
        if (x < 0.5)
        {
            // Reflection: Γ(x)Γ(1−x) = π / sin(πx).
            return Math.Log(Math.PI / Math.Abs(Math.Sin(Math.PI * x))) - LogGamma(1 - x);
        }

        x -= 1;
        var sum = Lanczos[0];
        var t = x + 7.5;
        for (var index = 1; index < Lanczos.Length; index++)
        {
            sum += Lanczos[index] / (x + index);
        }

        return 0.5 * Math.Log(2 * Math.PI) + (x + 0.5) * Math.Log(t) - t + Math.Log(sum);
    }

    public static double Gamma(double x)
    {
        if (x == Math.Floor(x) && x <= 0)
        {
            throw new CalcErrorException(CellError.Number);
        }

        if (x == Math.Floor(x) && x <= 171)
        {
            return MathFunctions.Factorial(x - 1);
        }

        if (x < 0.5)
        {
            return Math.PI / (Math.Sin(Math.PI * x) * Gamma(1 - x));
        }

        return Math.Exp(LogGamma(x));
    }

    /// <summary>The regularized lower incomplete gamma P(a, x).</summary>
    public static double GammaP(double a, double x)
    {
        if (x <= 0)
        {
            return 0;
        }

        if (x < a + 1)
        {
            // Series: P = e^(−x) x^a / Γ(a+1) Σ x^n / ((a+1)…(a+n)).
            var term = 1 / a;
            var sum = term;
            for (var n = 1; n < MaxIterations; n++)
            {
                term *= x / (a + n);
                sum += term;
                if (Math.Abs(term) < Math.Abs(sum) * Epsilon)
                {
                    break;
                }
            }

            return sum * Math.Exp(-x + a * Math.Log(x) - LogGamma(a));
        }

        return 1 - GammaQ(a, x);
    }

    /// <summary>The regularized upper incomplete gamma Q(a, x) = 1 − P(a, x).</summary>
    public static double GammaQ(double a, double x)
    {
        if (x <= 0)
        {
            return 1;
        }

        if (x < a + 1)
        {
            return 1 - GammaP(a, x);
        }

        // Continued fraction (modified Lentz).
        const double tiny = 1e-300;
        var b = x + 1 - a;
        var c = 1 / tiny;
        var d = 1 / b;
        var h = d;
        for (var n = 1; n < MaxIterations; n++)
        {
            var an = -n * (n - a);
            b += 2;
            d = an * d + b;
            d = Math.Abs(d) < tiny ? tiny : d;
            c = b + an / c;
            c = Math.Abs(c) < tiny ? tiny : c;
            d = 1 / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < Epsilon)
            {
                break;
            }
        }

        return Math.Exp(-x + a * Math.Log(x) - LogGamma(a)) * h;
    }

    /// <summary>The regularized incomplete beta I_x(a, b).</summary>
    public static double BetaRegularized(double x, double a, double b)
    {
        if (x <= 0)
        {
            return 0;
        }

        if (x >= 1)
        {
            return 1;
        }

        var front = Math.Exp(LogGamma(a + b) - LogGamma(a) - LogGamma(b) + a * Math.Log(x) + b * Math.Log(1 - x));
        if (x < (a + 1) / (a + b + 2))
        {
            return front * BetaFraction(x, a, b) / a;
        }

        return 1 - front * BetaFraction(1 - x, b, a) / b;
    }

    private static double BetaFraction(double x, double a, double b)
    {
        const double tiny = 1e-300;
        var qab = a + b;
        var qap = a + 1;
        var qam = a - 1;
        var c = 1.0;
        var d = 1 - qab * x / qap;
        d = Math.Abs(d) < tiny ? tiny : d;
        d = 1 / d;
        var h = d;
        for (var m = 1; m < MaxIterations; m++)
        {
            var m2 = 2 * m;
            var aa = m * (b - m) * x / ((qam + m2) * (a + m2));
            d = 1 + aa * d;
            d = Math.Abs(d) < tiny ? tiny : d;
            c = 1 + aa / c;
            c = Math.Abs(c) < tiny ? tiny : c;
            d = 1 / d;
            h *= d * c;
            aa = -(a + m) * (qab + m) * x / ((a + m2) * (qap + m2));
            d = 1 + aa * d;
            d = Math.Abs(d) < tiny ? tiny : d;
            c = 1 + aa / c;
            c = Math.Abs(c) < tiny ? tiny : c;
            d = 1 / d;
            var delta = d * c;
            h *= delta;
            if (Math.Abs(delta - 1) < Epsilon)
            {
                break;
            }
        }

        return h;
    }

    public static double Erf(double x) => x < 0 ? -GammaP(0.5, x * x) : GammaP(0.5, x * x);

    public static double Erfc(double x) => x < 0 ? 1 + GammaP(0.5, x * x) : GammaQ(0.5, x * x);

    public static double NormalPdf(double z) => Math.Exp(-0.5 * z * z) / Math.Sqrt(2 * Math.PI);

    public static double NormalCdf(double z) => 0.5 * Erfc(-z / Math.Sqrt(2));

    /// <summary>The standard normal quantile: Acklam's rational approximation with two Halley refinements (≈1e-15).</summary>
    public static double NormalInverse(double p)
    {
        if (p <= 0 || p >= 1)
        {
            throw new CalcErrorException(CellError.Number);
        }

        double[] a = [-3.969683028665376e+01, 2.209460984245205e+02, -2.759285104469687e+02, 1.383577518672690e+02, -3.066479806614716e+01, 2.506628277459239e+00];
        double[] b = [-5.447609879822406e+01, 1.615858368580409e+02, -1.556989798598866e+02, 6.680131188771972e+01, -1.328068155288572e+01];
        double[] c = [-7.784894002430293e-03, -3.223964580411365e-01, -2.400758277161838e+00, -2.549732539343734e+00, 4.374664141464968e+00, 2.938163982698783e+00];
        double[] d = [7.784695709041462e-03, 3.224671290700398e-01, 2.445134137142996e+00, 3.754408661907416e+00];
        const double low = 0.02425;
        double x;
        if (p < low)
        {
            var q = Math.Sqrt(-2 * Math.Log(p));
            x = (((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }
        else if (p <= 1 - low)
        {
            var q = p - 0.5;
            var r = q * q;
            x = (((((a[0] * r + a[1]) * r + a[2]) * r + a[3]) * r + a[4]) * r + a[5]) * q / (((((b[0] * r + b[1]) * r + b[2]) * r + b[3]) * r + b[4]) * r + 1);
        }
        else
        {
            var q = Math.Sqrt(-2 * Math.Log(1 - p));
            x = -(((((c[0] * q + c[1]) * q + c[2]) * q + c[3]) * q + c[4]) * q + c[5]) / ((((d[0] * q + d[1]) * q + d[2]) * q + d[3]) * q + 1);
        }

        for (var step = 0; step < 2; step++)
        {
            var error = NormalCdf(x) - p;
            var u = error * Math.Sqrt(2 * Math.PI) * Math.Exp(x * x / 2);
            x -= u / (1 + x * u / 2);
        }

        return x;
    }

    /// <summary>Student's t cumulative distribution.</summary>
    public static double StudentCdf(double t, double degrees)
    {
        var tail = 0.5 * BetaRegularized(degrees / (degrees + t * t), degrees / 2, 0.5);
        return t > 0 ? 1 - tail : tail;
    }

    public static double StudentPdf(double t, double degrees) =>
        Math.Exp(LogGamma((degrees + 1) / 2) - LogGamma(degrees / 2)) / Math.Sqrt(degrees * Math.PI) * Math.Pow(1 + t * t / degrees, -(degrees + 1) / 2);

    public static double ChiSquareCdf(double x, double degrees) => GammaP(degrees / 2, x / 2);

    public static double FCdf(double x, double d1, double d2) => x <= 0 ? 0 : BetaRegularized(d1 * x / (d1 * x + d2), d1 / 2, d2 / 2);

    /// <summary>
    /// The x with <paramref name="cdf"/>(x) = <paramref name="p"/> for a non-decreasing CDF, by bracketing then bisection
    /// refined with the secant method; #N/A when it cannot be found (as Excel reports).
    /// </summary>
    public static double Inverse(Func<double, double> cdf, double p, double low, double high, bool expandHigh = true)
    {
        var lowValue = cdf(low) - p;
        var highValue = cdf(high) - p;
        for (var step = 0; expandHigh && highValue < 0 && step < 200; step++)
        {
            low = high;
            lowValue = highValue;
            high = high * 2 + 1;
            highValue = cdf(high) - p;
        }

        if (lowValue > 0 || highValue < 0)
        {
            throw new CalcErrorException(CellError.NotAvailable);
        }

        for (var step = 0; step < 300; step++)
        {
            var middle = (low + high) / 2;
            if (high - low <= Math.Max(Math.Abs(middle), 1e-300) * 1e-15)
            {
                return middle;
            }

            var value = cdf(middle) - p;
            if (value == 0)
            {
                return middle;
            }

            if (value < 0)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    /// <summary>ln C(n, k) for the discrete distributions.</summary>
    public static double LogCombinations(double n, double k) => LogGamma(n + 1) - LogGamma(k + 1) - LogGamma(n - k + 1);

    public static double BinomialPmf(double k, double n, double p)
    {
        if (p == 0)
        {
            return k == 0 ? 1 : 0;
        }

        if (p == 1)
        {
            return k == n ? 1 : 0;
        }

        return Math.Exp(LogCombinations(n, k) + k * Math.Log(p) + (n - k) * Math.Log(1 - p));
    }
}
