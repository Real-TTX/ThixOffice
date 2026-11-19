using System.Globalization;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Drawing;

/// <summary>
/// What a colour is resolved against: the theme's scheme, the colour map in effect (master's p:clrMap or a slide's override) and
/// the placeholder colour ("phClr": the colour of the style reference that uses a theme style).
/// </summary>
public sealed record ColorContext(ColorScheme Scheme, ColorMap Map, ColorSpec? PlaceholderColor = null)
{
    public ColorContext WithPlaceholder(ColorSpec? color) => color is null ? this : this with { PlaceholderColor = color };
}

/// <summary>
/// Resolves DrawingML colours (ECMA-376 Part 1, 20.1.2.3) to RGB: the base colour (sRGB, scheme through the colour map, system
/// with its last colour, preset names, HSL, scRGB) and then every transform in file order. Luminance, saturation and hue
/// transforms work in HSL; tint, shade and the red/green/blue transforms in linear RGB (gamma-expanded sRGB), the way PowerPoint
/// computes them: accent1 #4472C4 with lumMod 75000 is #2F5597, with lumMod 60000 lumOff 40000 #8FAADC, with shade 50000 #2F528F.
/// </summary>
public static class ColorResolver
{
    private static readonly Dictionary<string, string> PresetColors = BuildPresetColors();

    /// <summary>The colour as RGB with alpha.</summary>
    public static RgbColor Resolve(ColorSpec color, ColorContext context)
    {
        var state = Base(color, context, 0);
        foreach (var modifier in color.Modifiers)
        {
            state = Apply(state, modifier);
        }

        return state.ToRgb();
    }

    /// <summary>Resolves a fill's colour for places that need one colour (solid fills; gradients give their middle stop).</summary>
    public static RgbColor? ResolveFill(Fill? fill, ColorContext context) => fill switch
    {
        SolidFill solid => Resolve(solid.Color, context),
        GradientFill gradient when !gradient.Stops.IsEmpty => Resolve(gradient.Stops[gradient.Stops.Count / 2].Color, context),
        PatternFill pattern when pattern.Foreground is not null => Resolve(pattern.Foreground, context),
        _ => null,
    };

    private readonly record struct ColorState(double R, double G, double B, double A)
    {
        public RgbColor ToRgb() => new(ToByte(R), ToByte(G), ToByte(B), ToByte(A));

        private static byte ToByte(double value) => (byte)Math.Round(Math.Clamp(value, 0, 1) * 255);
    }

    private static ColorState Base(ColorSpec color, ColorContext context, int depth)
    {
        switch (color.Kind)
        {
            case ColorKind.Rgb:
                return FromHex(color.Value);
            case ColorKind.System:
                return FromHex(color.LastColor ?? (color.Value is "window" or "windowText" ? (color.Value == "window" ? "FFFFFF" : "000000") : "000000"));
            case ColorKind.Preset:
                return FromHex(PresetColors.TryGetValue(color.Value, out var hex) ? hex : "000000");
            case ColorKind.Hsl:
                {
                    var parts = ParseInts(color.Value);
                    var (r, g, b) = HslToRgb(parts[0] / 60000.0 / 360.0, parts[1] / 100000.0, parts[2] / 100000.0);
                    return new ColorState(r, g, b, 1);
                }

            case ColorKind.ScRgb:
                {
                    var parts = ParseInts(color.Value);
                    return new ColorState(ToSrgb(parts[0] / 100000.0), ToSrgb(parts[1] / 100000.0), ToSrgb(parts[2] / 100000.0), 1);
                }

            case ColorKind.Scheme:
                if (depth > 4)
                {
                    return new ColorState(0, 0, 0, 1);
                }

                if (color.IsPlaceholder)
                {
                    if (context.PlaceholderColor is not { } placeholder || placeholder.IsPlaceholder)
                    {
                        return new ColorState(0, 0, 0, 1);
                    }

                    var resolved = Base(placeholder, context, depth + 1);
                    foreach (var modifier in placeholder.Modifiers)
                    {
                        resolved = Apply(resolved, modifier);
                    }

                    return resolved;
                }

                var slot = context.Map.Map(color.Value);
                if (Array.IndexOf(ColorScheme.SlotNames, slot) < 0)
                {
                    return new ColorState(0, 0, 0, 1);
                }

                var scheme = context.Scheme[slot];
                var state = Base(scheme, context, depth + 1);
                foreach (var modifier in scheme.Modifiers)
                {
                    state = Apply(state, modifier);
                }

                return state;
            default:
                return new ColorState(0, 0, 0, 1);
        }
    }

    private static ColorState FromHex(string hex)
    {
        var rgb = RgbColor.FromHex(hex);
        return new ColorState(rgb.R / 255.0, rgb.G / 255.0, rgb.B / 255.0, 1);
    }

    private static int[] ParseInts(string value)
    {
        var parts = value.Split(',').Select(part => int.TryParse(part, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : 0).ToList();
        while (parts.Count < 3)
        {
            parts.Add(0);
        }

        return [.. parts];
    }

    private static ColorState Apply(ColorState state, ColorModifier modifier)
    {
        var value = (modifier.Value ?? 0) / 100000.0;
        switch (modifier.Name)
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

    private static ColorState Linear(ColorState state, Func<double, double> change) => state with
    {
        R = ToSrgb(Math.Clamp(change(ToLinear(state.R)), 0, 1)),
        G = ToSrgb(Math.Clamp(change(ToLinear(state.G)), 0, 1)),
        B = ToSrgb(Math.Clamp(change(ToLinear(state.B)), 0, 1)),
    };

    private static ColorState LinearChannel(ColorState state, int channel, Func<double, double> change) => channel switch
    {
        0 => state with { R = ToSrgb(Math.Clamp(change(ToLinear(state.R)), 0, 1)) },
        1 => state with { G = ToSrgb(Math.Clamp(change(ToLinear(state.G)), 0, 1)) },
        _ => state with { B = ToSrgb(Math.Clamp(change(ToLinear(state.B)), 0, 1)) },
    };

    private static ColorState Hsl(ColorState state, Func<double, double, double, (double H, double S, double L)> change)
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

    private static Dictionary<string, string> BuildPresetColors()
    {
        // ST_PresetColorVal (ECMA-376 20.1.10.47): the CSS colour names; "dk"/"lt"/"med" spellings are aliases.
        const string table =
            "aliceBlue F0F8FF antiqueWhite FAEBD7 aqua 00FFFF aquamarine 7FFFD4 azure F0FFFF beige F5F5DC bisque FFE4C4 black 000000 " +
            "blanchedAlmond FFEBCD blue 0000FF blueViolet 8A2BE2 brown A52A2A burlyWood DEB887 cadetBlue 5F9EA0 chartreuse 7FFF00 " +
            "chocolate D2691E coral FF7F50 cornflowerBlue 6495ED cornsilk FFF8DC crimson DC143C cyan 00FFFF darkBlue 00008B " +
            "darkCyan 008B8B darkGoldenrod B8860B darkGray A9A9A9 darkGrey A9A9A9 darkGreen 006400 darkKhaki BDB76B darkMagenta 8B008B " +
            "darkOliveGreen 556B2F darkOrange FF8C00 darkOrchid 9932CC darkRed 8B0000 darkSalmon E9967A darkSeaGreen 8FBC8F " +
            "darkSlateBlue 483D8B darkSlateGray 2F4F4F darkSlateGrey 2F4F4F darkTurquoise 00CED1 darkViolet 9400D3 deepPink FF1493 " +
            "deepSkyBlue 00BFFF dimGray 696969 dimGrey 696969 dodgerBlue 1E90FF firebrick B22222 floralWhite FFFAF0 forestGreen 228B22 " +
            "fuchsia FF00FF gainsboro DCDCDC ghostWhite F8F8FF gold FFD700 goldenrod DAA520 gray 808080 grey 808080 green 008000 " +
            "greenYellow ADFF2F honeydew F0FFF0 hotPink FF69B4 indianRed CD5C5C indigo 4B0082 ivory FFFFF0 khaki F0E68C lavender E6E6FA " +
            "lavenderBlush FFF0F5 lawnGreen 7CFC00 lemonChiffon FFFACD lightBlue ADD8E6 lightCoral F08080 lightCyan E0FFFF " +
            "lightGoldenrodYellow FAFAD2 lightGray D3D3D3 lightGrey D3D3D3 lightGreen 90EE90 lightPink FFB6C1 lightSalmon FFA07A " +
            "lightSeaGreen 20B2AA lightSkyBlue 87CEFA lightSlateGray 778899 lightSlateGrey 778899 lightSteelBlue B0C4DE lightYellow FFFFE0 " +
            "lime 00FF00 limeGreen 32CD32 linen FAF0E6 magenta FF00FF maroon 800000 mediumAquamarine 66CDAA mediumBlue 0000CD " +
            "mediumOrchid BA55D3 mediumPurple 9370DB mediumSeaGreen 3CB371 mediumSlateBlue 7B68EE mediumSpringGreen 00FA9A " +
            "mediumTurquoise 48D1CC mediumVioletRed C71585 midnightBlue 191970 mintCream F5FFFA mistyRose FFE4E1 moccasin FFE4B5 " +
            "navajoWhite FFDEAD navy 000080 oldLace FDF5E6 olive 808000 oliveDrab 6B8E23 orange FFA500 orangeRed FF4500 orchid DA70D6 " +
            "paleGoldenrod EEE8AA paleGreen 98FB98 paleTurquoise AFEEEE paleVioletRed DB7093 papayaWhip FFEFD5 peachPuff FFDAB9 " +
            "peru CD853F pink FFC0CB plum DDA0DD powderBlue B0E0E6 purple 800080 red FF0000 rosyBrown BC8F8F royalBlue 4169E1 " +
            "saddleBrown 8B4513 salmon FA8072 sandyBrown F4A460 seaGreen 2E8B57 seaShell FFF5EE sienna A0522D silver C0C0C0 " +
            "skyBlue 87CEEB slateBlue 6A5ACD slateGray 708090 slateGrey 708090 snow FFFAFA springGreen 00FF7F steelBlue 4682B4 " +
            "tan D2B48C teal 008080 thistle D8BFD8 tomato FF6347 turquoise 40E0D0 violet EE82EE wheat F5DEB3 white FFFFFF " +
            "whiteSmoke F5F5F5 yellow FFFF00 yellowGreen 9ACD32";
        var parts = table.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var colors = new Dictionary<string, string>(StringComparer.Ordinal);
        for (var index = 0; index + 1 < parts.Length; index += 2)
        {
            colors[parts[index]] = parts[index + 1];
            if (parts[index].StartsWith("dark", StringComparison.Ordinal))
            {
                colors["dk" + parts[index][4..]] = parts[index + 1];
            }
            else if (parts[index].StartsWith("light", StringComparison.Ordinal))
            {
                colors["lt" + parts[index][5..]] = parts[index + 1];
            }
            else if (parts[index].StartsWith("medium", StringComparison.Ordinal))
            {
                colors["med" + parts[index][6..]] = parts[index + 1];
            }
        }

        return colors;
    }
}
