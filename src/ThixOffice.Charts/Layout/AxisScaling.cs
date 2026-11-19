namespace ThixOffice.Charts;

/// <summary>A value axis' scale: from <see cref="Minimum"/> to <see cref="Maximum"/> with a tick every <see cref="MajorUnit"/>.</summary>
public readonly record struct AxisScale(double Minimum, double Maximum, double MajorUnit)
{
    public double Span => Maximum - Minimum;

    /// <summary>The tick values from the minimum to the maximum (computed by index, so no rounding drift).</summary>
    public IReadOnlyList<double> Ticks
    {
        get
        {
            var ticks = new List<double>();
            if (MajorUnit <= 0 || Span <= 0)
            {
                return [Minimum];
            }

            var count = (int)Math.Round(Span / MajorUnit);
            for (var index = 0; index <= Math.Min(count, 1000); index++)
            {
                ticks.Add(Clean(Minimum + index * MajorUnit));
            }

            return ticks;
        }
    }

    /// <summary>The fraction 0–1 of <paramref name="value"/> along the axis (not clamped).</summary>
    public double Fraction(double value) => Span <= 0 ? 0 : (value - Minimum) / Span;

    /// <summary>Removes the float noise of index × unit (0.30000000000000004 → 0.3).</summary>
    internal static double Clean(double value) => Math.Abs(value) < 1e-12 ? 0 : Math.Round(value, 12 - (int)Math.Floor(Math.Log10(Math.Abs(value))));
}

/// <summary>The unit of a time axis: days, months or years.</summary>
public enum DateUnit : byte
{
    Days,
    Months,
    Years,
}

/// <summary>
/// Automatic axis scales the way Excel chooses them: a value range that starts at zero unless the data sit far from it
/// (minimum above 5/6 of the maximum), 5 % headroom above the largest value, a major unit of 1, 2 or 5 × 10ⁿ chosen so that
/// the ticks fit the axis' length (<c>maxIntervals</c>), and the ends rounded out to the unit. Fixed minimum, maximum or
/// unit (from the file or the user) are kept. Deterministic: the same data give the same scale.
/// </summary>
public static class AxisScaling
{
    public static AxisScale ValueScale(double dataMinimum, double dataMaximum, int maxIntervals, double? fixedMinimum = null, double? fixedMaximum = null, double? fixedUnit = null)
    {
        maxIntervals = Math.Clamp(maxIntervals, 1, 20);
        if (double.IsNaN(dataMinimum) || double.IsNaN(dataMaximum) || double.IsInfinity(dataMinimum) || double.IsInfinity(dataMaximum))
        {
            (dataMinimum, dataMaximum) = (0, 1);
        }

        if (dataMinimum > dataMaximum)
        {
            (dataMinimum, dataMaximum) = (dataMaximum, dataMinimum);
        }

        if (dataMinimum == dataMaximum)
        {
            if (dataMinimum == 0)
            {
                dataMaximum = 1;
            }
            else if (dataMinimum > 0)
            {
                dataMinimum = 0;
            }
            else
            {
                dataMaximum = 0;
            }
        }

        var minimum = fixedMinimum ?? AutoMinimum(dataMinimum, dataMaximum);
        var maximum = fixedMaximum ?? AutoMaximum(dataMinimum, dataMaximum);
        if (maximum <= minimum)
        {
            maximum = fixedMaximum is null ? minimum + Math.Max(1, Math.Abs(minimum) * 0.1) : maximum;
            minimum = fixedMaximum is null ? minimum : maximum - Math.Max(1, Math.Abs(maximum) * 0.1);
        }

        // Headroom: the largest value does not touch the top (Excel adds 5 % of the range before rounding up).
        var top = fixedMaximum ?? (maximum > 0 ? maximum + (maximum - minimum) * 0.05 : maximum);
        var bottom = fixedMinimum ?? (minimum < 0 && dataMaximum <= 0 ? minimum - (maximum - minimum) * 0.05 : minimum);
        var unit = fixedUnit is > 0 ? fixedUnit.Value : NiceUnit((top - bottom) / maxIntervals);
        var low = fixedMinimum ?? Math.Floor(bottom / unit + 1e-9) * unit;
        var high = fixedMaximum ?? Math.Ceiling(top / unit - 1e-9) * unit;
        if (high <= low)
        {
            high = low + unit;
        }

        return new AxisScale(AxisScale.Clean(low), AxisScale.Clean(high), unit);
    }

    /// <summary>A 100 % axis: 0 to 1 (or −1 to 1 with negative parts) with a tick per 10 % or 20 %.</summary>
    public static AxisScale PercentScale(bool hasNegative, int maxIntervals)
    {
        var low = hasNegative ? -1.0 : 0.0;
        var unit = NiceUnit((1 - low) / Math.Clamp(maxIntervals, 1, 20));
        return new AxisScale(low, 1, Math.Max(unit, 0.1));
    }

    /// <summary>The smallest of 1, 2, 5 × 10ⁿ that is at least <paramref name="rough"/>.</summary>
    public static double NiceUnit(double rough)
    {
        if (rough <= 0 || double.IsNaN(rough) || double.IsInfinity(rough))
        {
            return 1;
        }

        var exponent = Math.Floor(Math.Log10(rough));
        var power = Math.Pow(10, exponent);
        var fraction = rough / power;
        var nice = fraction <= 1 + 1e-9 ? 1 : fraction <= 2 + 1e-9 ? 2 : fraction <= 5 + 1e-9 ? 5 : 10;
        return AxisScale.Clean(nice * power);
    }

    private static double AutoMinimum(double dataMinimum, double dataMaximum)
    {
        if (dataMinimum >= 0)
        {
            // Excel starts at zero unless the minimum is more than 5/6 of the maximum.
            return dataMaximum - dataMinimum > dataMaximum / 6 ? 0 : dataMinimum - (dataMaximum - dataMinimum) / 2;
        }

        return dataMinimum;
    }

    private static double AutoMaximum(double dataMinimum, double dataMaximum)
    {
        if (dataMaximum <= 0)
        {
            return dataMaximum - dataMinimum > -dataMinimum / 6 ? 0 : dataMaximum + (dataMaximum - dataMinimum) / 2;
        }

        return dataMaximum;
    }

    /// <summary>The unit of a time axis for the given dates: years when every date is the 1st of January, months when every one is a 1st, else days.</summary>
    public static DateUnit BaseUnit(IEnumerable<double> serials)
    {
        var dates = serials.Select(ToDate).ToList();
        if (dates.Count == 0)
        {
            return DateUnit.Days;
        }

        if (dates.All(date => date.Day == 1 && date.Month == 1) && dates.Distinct().Count() == dates.Count)
        {
            return DateUnit.Years;
        }

        return dates.All(date => date.Day == 1) && dates.Select(date => date.Year * 12 + date.Month).Distinct().Count() == dates.Count ? DateUnit.Months : DateUnit.Days;
    }

    /// <summary>The position of a date in units from <paramref name="origin"/> (fractional days for days).</summary>
    public static double DateOffset(double serial, double origin, DateUnit unit)
    {
        var date = ToDate(serial);
        var start = ToDate(origin);
        return unit switch
        {
            DateUnit.Years => date.Year - start.Year,
            DateUnit.Months => (date.Year - start.Year) * 12 + date.Month - start.Month,
            _ => Math.Floor(serial) - Math.Floor(origin),
        };
    }

    /// <summary>The serial of the date <paramref name="units"/> units after <paramref name="origin"/>.</summary>
    public static double AddUnits(double origin, double units, DateUnit unit)
    {
        var start = ToDate(origin);
        var date = unit switch
        {
            DateUnit.Years => new DateTime(Math.Clamp(start.Year + (int)units, 1, 9999), 1, 1),
            DateUnit.Months => new DateTime(start.Year, start.Month, 1).AddMonths((int)units),
            _ => start.AddDays(units),
        };
        return (date - Epoch).TotalDays;
    }

    /// <summary>A major unit (in base units) so that at most <paramref name="maxLabels"/> labels show: 1, 2, 3, 6, 12 months; 1, 2, 7, 14 days; 1, 2, 5 … years.</summary>
    public static int DateMajorUnit(double span, DateUnit unit, int maxLabels)
    {
        maxLabels = Math.Max(1, maxLabels);
        int[] steps = unit switch
        {
            DateUnit.Months => [1, 2, 3, 6, 12, 24, 60, 120],
            DateUnit.Days => [1, 2, 7, 14, 30, 61, 91, 182, 365, 730],
            _ => [1, 2, 5, 10, 20, 50, 100],
        };
        foreach (var step in steps)
        {
            if (span / step + 1 <= maxLabels)
            {
                return step;
            }
        }

        return steps[^1] * (int)Math.Ceiling(span / steps[^1] / maxLabels + 1);
    }

    private static readonly DateTime Epoch = new(1899, 12, 30);

    private static DateTime ToDate(double serial) => Epoch.AddDays(Math.Clamp(Math.Floor(serial), 0, 2_958_465));
}
