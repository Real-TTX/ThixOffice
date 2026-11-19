using ThixOffice.Text.Drawing;

namespace ThixOffice.Charts;

/// <summary>A colour set of the gallery "Farben ändern": its German label and the colour of every series index.</summary>
public sealed record ChartColorSet(int Id, string Label, bool IsMonochrome, Func<int, int, ChartColor> ColorOf);

/// <summary>
/// The colours of charts as Office draws them: series colours from the theme accents (a colour set picks which and how they
/// vary after the sixth series), the greys of text, gridlines and axes (the theme's dark 1 at fixed luminances), and
/// DrawingML's luminance modulation.
/// </summary>
public static class ChartColors
{
    /// <summary>Title, axis labels, legend: tx1 at 65 % (Office's "595959").</summary>
    public static readonly RgbColor Text = RgbColor.FromHex("595959");

    /// <summary>Data labels: tx1 at 75 %.</summary>
    public static readonly RgbColor LabelText = RgbColor.FromHex("404040");

    /// <summary>Gridlines, the category axis line and the chart's border: tx1 at 15 %.</summary>
    public static readonly RgbColor Gridline = RgbColor.FromHex("D9D9D9");

    /// <summary>The luminance variations after the sixth series (Office: darker, lighter, a little darker, …).</summary>
    private static readonly (double LumMod, double LumOff)[] Variations =
    [
        (1, 0), (0.6, 0), (0.8, 0.2), (0.8, 0), (0.6, 0.4), (0.5, 0),
    ];

    /// <summary>The colour sets of "Farben ändern": Farbig 1–4 and one monochrome set per accent.</summary>
    public static IReadOnlyList<ChartColorSet> Sets { get; } = CreateSets();

    /// <summary>The automatic colour of a series (or a pie slice) <paramref name="index"/> of <paramref name="count"/> in a set.</summary>
    public static ChartColor SeriesColor(int colorSet, int index, int count)
    {
        var set = Sets.FirstOrDefault(candidate => candidate.Id == colorSet) ?? Sets[0];
        return set.ColorOf(index, Math.Max(1, count));
    }

    private static List<ChartColorSet> CreateSets()
    {
        static ChartColor Colorful(int[] accents, int index)
        {
            var variation = Variations[index / accents.Length % Variations.Length];
            return ChartColor.FromAccent(accents[index % accents.Length], variation.LumMod, variation.LumOff);
        }

        var sets = new List<ChartColorSet>
        {
            new(0, "Farbig 1", false, (index, _) => Colorful([1, 2, 3, 4, 5, 6], index)),
            new(1, "Farbig 2", false, (index, _) => Colorful([2, 4, 6], index)),
            new(2, "Farbig 3", false, (index, _) => Colorful([3, 5, 1], index)),
            new(3, "Farbig 4", false, (index, _) => Colorful([4, 6, 2], index)),
        };
        for (var accent = 1; accent <= 6; accent++)
        {
            var fixedAccent = accent;
            sets.Add(new ChartColorSet(3 + accent, $"Einfarbig {accent}", true, (index, count) => Monochrome(fixedAccent, index, count)));
        }

        return sets;
    }

    /// <summary>Shades of one accent from dark to light (Office's monochromatic palettes).</summary>
    private static ChartColor Monochrome(int accent, int index, int count)
    {
        if (count <= 1)
        {
            return ChartColor.FromAccent(accent);
        }

        // From a shade at 50 % to a tint at 60 % lighter, evenly.
        var position = (double)index / (count - 1);
        var amount = Math.Round(-0.5 + position * 1.1, 3);
        return amount <= 0
            ? ChartColor.FromAccent(accent, Math.Round(1 + amount, 3))
            : ChartColor.FromAccent(accent, Math.Round(1 - amount, 3), amount);
    }

    /// <summary>DrawingML lumMod / lumOff on the HSL luminance.</summary>
    public static RgbColor ApplyLuminance(RgbColor color, double lumMod, double lumOff)
    {
        if (lumMod == 1 && lumOff == 0)
        {
            return color;
        }

        var (hue, saturation, luminance) = ToHsl(color);
        luminance = Math.Clamp(luminance * lumMod + lumOff, 0, 1);
        return FromHsl(hue, saturation, luminance, color.A);
    }

    /// <summary>A colour mixed towards white (<paramref name="amount"/> 0–1): light fills of selected or hovered parts.</summary>
    public static RgbColor Lighten(RgbColor color, double amount) => new(
        (byte)Math.Round(color.R + (255 - color.R) * amount),
        (byte)Math.Round(color.G + (255 - color.G) * amount),
        (byte)Math.Round(color.B + (255 - color.B) * amount),
        color.A);

    /// <summary>Black or white, whichever reads better on <paramref name="background"/> (labels inside bars and slices).</summary>
    public static RgbColor ContrastText(RgbColor background)
    {
        var luminance = (0.299 * background.R + 0.587 * background.G + 0.114 * background.B) / 255;
        return luminance > 0.6 ? LabelText : RgbColor.White;
    }

    private static (double Hue, double Saturation, double Luminance) ToHsl(RgbColor color)
    {
        var red = color.R / 255.0;
        var green = color.G / 255.0;
        var blue = color.B / 255.0;
        var max = Math.Max(red, Math.Max(green, blue));
        var min = Math.Min(red, Math.Min(green, blue));
        var luminance = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, luminance);
        }

        var delta = max - min;
        var saturation = luminance > 0.5 ? delta / (2 - max - min) : delta / (max + min);
        double hue;
        if (max == red)
        {
            hue = (green - blue) / delta + (green < blue ? 6 : 0);
        }
        else if (max == green)
        {
            hue = (blue - red) / delta + 2;
        }
        else
        {
            hue = (red - green) / delta + 4;
        }

        return (hue / 6, saturation, luminance);
    }

    private static RgbColor FromHsl(double hue, double saturation, double luminance, byte alpha)
    {
        if (saturation == 0)
        {
            var grey = (byte)Math.Round(luminance * 255);
            return new RgbColor(grey, grey, grey, alpha);
        }

        var q = luminance < 0.5 ? luminance * (1 + saturation) : luminance + saturation - luminance * saturation;
        var p = 2 * luminance - q;
        return new RgbColor(Channel(p, q, hue + 1.0 / 3), Channel(p, q, hue), Channel(p, q, hue - 1.0 / 3), alpha);
    }

    private static byte Channel(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        var value = t < 1.0 / 6 ? p + (q - p) * 6 * t
            : t < 0.5 ? q
            : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6
            : p;
        return (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }
}
