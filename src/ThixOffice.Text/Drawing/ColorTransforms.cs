namespace ThixOffice.Text.Drawing;

/// <summary>A colour while DrawingML transforms work on it: sRGB channels and alpha, 0–1, unrounded.</summary>
public readonly record struct ColorValue(double R, double G, double B, double A)
{
    public RgbColor ToRgb() => new(ToByte(R), ToByte(G), ToByte(B), ToByte(A));

    public static ColorValue FromRgb(RgbColor color) => new(color.R / 255.0, color.G / 255.0, color.B / 255.0, color.A / 255.0);

    private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
}

/// <summary>
/// The colour transforms of DrawingML (ECMA-376 Part 1, 20.1.2.3) shared by every app: luminance, saturation and hue transforms in
/// HSL; tint, shade and the red/green/blue transforms in linear RGB (gamma-expanded sRGB), the way Office computes them – accent1
/// #4472C4 with lumMod 75000 is #2F5597, with lumMod 60000 lumOff 40000 #8FAADC, with shade 50000 #2F528F.
/// </summary>
public static class ColorTransforms
{
    /// <summary>Applies the transforms in order to a colour.</summary>
    public static RgbColor Apply(RgbColor color, IEnumerable<(string Name, int? Value)> modifiers)
    {
        var state = ColorValue.FromRgb(color);
        foreach (var (name, value) in modifiers)
        {
            state = Apply(state, name, value);
        }

        return state.ToRgb();
    }

    /// <summary>Applies one DrawingML colour transform (a:lumMod val="75000" → <paramref name="name"/> "lumMod", <paramref name="value"/> 75000).</summary>
    public static ColorValue Apply(ColorValue state, string name, int? value100k)
    {
        var value = (value100k ?? 0) / 100000.0;
        switch (name)
        {
            case "alpha":
                return state with { A = value };
            case "alphaMod":
                return state with { A = state.A * value };
            case "alphaOff":
                return state with { A = Math.Clamp(state.A + value, 0, 1) };
            case "tint":
                return Linear(state, channel => 1 - ((1 - channel) * value));
            case "shade":
                return Linear(state, channel => channel * value);
            case "gamma":
                return state with { R = ToSrgb(state.R), G = ToSrgb(state.G), B = ToSrgb(state.B) };
            case "invGamma":
                return state with { R = ToLinear(state.R), G = ToLinear(state.G), B = ToLinear(state.B) };
            case "inv":
                return state with { R = 1 - state.R, G = 1 - state.G, B = 1 - state.B };
            case "gray":
                {
                    var gray = (0.3 * state.R) + (0.59 * state.G) + (0.11 * state.B);
                    return state with { R = gray, G = gray, B = gray };
                }

            case "comp":
                return Hsl(state, (h, s, l) => ((h + 0.5) % 1, s, l));
            case "hue":
                return Hsl(state, (_, s, l) => (value * 100000 / 60000 / 360 % 1, s, l));
            case "hueOff":
                return Hsl(state, (h, s, l) => ((((h + (value * 100000 / 60000 / 360)) % 1) + 1) % 1, s, l));
            case "hueMod":
                return Hsl(state, (h, s, l) => (h * value % 1, s, l));
            case "sat":
                return Hsl(state, (h, _, l) => (h, Math.Clamp(value, 0, 1), l));
            case "satMod":
                return Hsl(state, (h, s, l) => (h, Math.Clamp(s * value, 0, 1), l));
            case "satOff":
                return Hsl(state, (h, s, l) => (h, Math.Clamp(s + value, 0, 1), l));
            case "lum":
                return Hsl(state, (h, s, _) => (h, s, Math.Clamp(value, 0, 1)));
            case "lumMod":
                return Hsl(state, (h, s, l) => (h, s, Math.Clamp(l * value, 0, 1)));
            case "lumOff":
                return Hsl(state, (h, s, l) => (h, s, Math.Clamp(l + value, 0, 1)));
            case "red":
                return LinearChannel(state, 0, _ => value);
            case "redMod":
                return LinearChannel(state, 0, channel => channel * value);
            case "redOff":
                return LinearChannel(state, 0, channel => channel + value);
            case "green":
                return LinearChannel(state, 1, _ => value);
            case "greenMod":
                return LinearChannel(state, 1, channel => channel * value);
            case "greenOff":
                return LinearChannel(state, 1, channel => channel + value);
            case "blue":
                return LinearChannel(state, 2, _ => value);
            case "blueMod":
                return LinearChannel(state, 2, channel => channel * value);
            case "blueOff":
                return LinearChannel(state, 2, channel => channel + value);
            default:
                return state;
        }
    }

    private static ColorValue Linear(ColorValue state, Func<double, double> change) => state with
    {
        R = ToSrgb(Math.Clamp(change(ToLinear(state.R)), 0, 1)),
        G = ToSrgb(Math.Clamp(change(ToLinear(state.G)), 0, 1)),
        B = ToSrgb(Math.Clamp(change(ToLinear(state.B)), 0, 1)),
    };

    private static ColorValue LinearChannel(ColorValue state, int channel, Func<double, double> change) => channel switch
    {
        0 => state with { R = ToSrgb(Math.Clamp(change(ToLinear(state.R)), 0, 1)) },
        1 => state with { G = ToSrgb(Math.Clamp(change(ToLinear(state.G)), 0, 1)) },
        _ => state with { B = ToSrgb(Math.Clamp(change(ToLinear(state.B)), 0, 1)) },
    };

    private static ColorValue Hsl(ColorValue state, Func<double, double, double, (double H, double S, double L)> change)
    {
        var (h, s, l) = RgbToHsl(state.R, state.G, state.B);
        var (nh, ns, nl) = change(h, s, l);
        var (r, g, b) = HslToRgb(nh, ns, nl);
        return state with { R = r, G = g, B = b };
    }

    /// <summary>sRGB (0–1) to linear light.</summary>
    public static double ToLinear(double value) =>
        value <= 0.04045 ? value / 12.92 : Math.Pow((value + 0.055) / 1.055, 2.4);

    /// <summary>Linear light to sRGB (0–1).</summary>
    public static double ToSrgb(double value) =>
        value <= 0.0031308 ? value * 12.92 : (1.055 * Math.Pow(value, 1 / 2.4)) - 0.055;

    /// <summary>RGB (0–1) to hue (0–1), saturation, luminance.</summary>
    public static (double H, double S, double L) RgbToHsl(double r, double g, double b)
    {
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max - min < 1e-12)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        double h;
        if (max == r)
        {
            h = ((g - b) / d) + (g < b ? 6 : 0);
        }
        else if (max == g)
        {
            h = ((b - r) / d) + 2;
        }
        else
        {
            h = ((r - g) / d) + 4;
        }

        return (h / 6, s, l);
    }

    public static (double R, double G, double B) HslToRgb(double h, double s, double l)
    {
        if (s <= 0)
        {
            return (l, l, l);
        }

        var q = l < 0.5 ? l * (1 + s) : l + s - (l * s);
        var p = (2 * l) - q;
        return (Hue(p, q, h + (1.0 / 3)), Hue(p, q, h), Hue(p, q, h - (1.0 / 3)));
    }

    private static double Hue(double p, double q, double t)
    {
        if (t < 0)
        {
            t += 1;
        }

        if (t > 1)
        {
            t -= 1;
        }

        if (t < 1.0 / 6)
        {
            return p + ((q - p) * 6 * t);
        }

        if (t < 0.5)
        {
            return q;
        }

        if (t < 2.0 / 3)
        {
            return p + ((q - p) * ((2.0 / 3) - t) * 6);
        }

        return p;
    }
}
