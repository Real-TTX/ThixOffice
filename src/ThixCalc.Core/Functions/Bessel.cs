namespace ThixCalc.Core.Functions;

/// <summary>
/// Bessel functions of whole order (BESSELJ, BESSELY, BESSELI, BESSELK) to about 12 significant digits: J and Y from their
/// integral representations (the trapezoid rule converges exponentially on these smooth periodic or fast-decaying
/// integrands, Simpson for Y's non-periodic part), I from its power series, K from ∫ e^(−x·cosh t)·cosh(nt) dt.
/// </summary>
internal static class Bessel
{
    /// <summary>Jn(x) = 1/π ∫₀^π cos(nθ − x·sin θ) dθ.</summary>
    public static double J(int n, double x)
    {
        var steps = Math.Max(64, (int)(Math.Abs(x) + n) * 2 + 64);
        var h = Math.PI / steps;
        var sum = 0.0;
        for (var step = 0; step <= steps; step++)
        {
            var theta = step * h;
            var value = Math.Cos(n * theta - x * Math.Sin(theta));
            sum += step == 0 || step == steps ? value / 2 : value;
        }

        return sum * h / Math.PI;
    }

    /// <summary>In(x) = Σ (x/2)^(2k+n) / (k!(k+n)!).</summary>
    public static double I(int n, double x)
    {
        var half = x / 2;
        var term = 1.0;
        for (var k = 1; k <= n; k++)
        {
            term *= half / k;
        }

        var sum = term;
        for (var k = 1; k < 10_000; k++)
        {
            term *= half * half / (k * (double)(k + n));
            sum += term;
            if (Math.Abs(term) < Math.Abs(sum) * 1e-17)
            {
                break;
            }
        }

        return sum;
    }

    /// <summary>Kn(x) = ∫₀^∞ e^(−x·cosh t)·cosh(nt) dt (x &gt; 0).</summary>
    public static double K(int n, double x)
    {
        const double h = 0.01;
        var sum = 0.0;
        for (var step = 0; step < 1_000_000; step++)
        {
            var t = step * h;
            var exponent = -x * Math.Cosh(t) + n * t;
            var value = Math.Exp(exponent) * (1 + Math.Exp(-2 * n * t)) / 2;
            sum += step == 0 ? value / 2 : value;
            if (t > 1 && exponent < -60)
            {
                break;
            }
        }

        return sum * h;
    }

    /// <summary>Yn(x) = 1/π ∫₀^π sin(x·sin θ − nθ) dθ − 1/π ∫₀^∞ (e^(nt) + (−1)^n e^(−nt)) e^(−x·sinh t) dt (x &gt; 0).</summary>
    public static double Y(int n, double x)
    {
        var steps = 2 * Math.Max(2000, (int)(x + n) * 40);
        var h = Math.PI / steps;
        var oscillating = 0.0;
        for (var step = 0; step <= steps; step++)
        {
            var theta = step * h;
            var value = Math.Sin(x * Math.Sin(theta) - n * theta);
            oscillating += value * (step == 0 || step == steps ? 1 : step % 2 == 1 ? 4 : 2);
        }

        oscillating *= h / 3;

        // The decaying part by Simpson's rule up to where the integrand is below e^-60.
        var end = 1.0;
        while (n * end - x * Math.Sinh(end) > -60)
        {
            end += 0.5;
        }

        var parts = 2 * (int)Math.Ceiling(end / 0.002);
        var dt = end / parts;
        var sign = n % 2 == 0 ? 1.0 : -1.0;
        var decaying = 0.0;
        for (var step = 0; step <= parts; step++)
        {
            var t = step * dt;
            var damping = -x * Math.Sinh(t);
            var value = Math.Exp(n * t + damping) + sign * Math.Exp(-n * t + damping);
            decaying += value * (step == 0 || step == parts ? 1 : step % 2 == 1 ? 4 : 2);
        }

        decaying *= dt / 3;
        return (oscillating - decaying) / Math.PI;
    }
}
