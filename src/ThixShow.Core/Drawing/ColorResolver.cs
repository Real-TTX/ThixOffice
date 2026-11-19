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
            state = ColorTransforms.Apply(state, modifier.Name, modifier.Value);
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

    private static ColorValue Base(ColorSpec color, ColorContext context, int depth)
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
                    var (r, g, b) = ColorTransforms.HslToRgb(parts[0] / 60000.0 / 360.0, parts[1] / 100000.0, parts[2] / 100000.0);
                    return new ColorValue(r, g, b, 1);
                }

            case ColorKind.ScRgb:
                {
                    var parts = ParseInts(color.Value);
                    return new ColorValue(ColorTransforms.ToSrgb(parts[0] / 100000.0), ColorTransforms.ToSrgb(parts[1] / 100000.0), ColorTransforms.ToSrgb(parts[2] / 100000.0), 1);
                }

            case ColorKind.Scheme:
                if (depth > 4)
                {
                    return new ColorValue(0, 0, 0, 1);
                }

                if (color.IsPlaceholder)
                {
                    if (context.PlaceholderColor is not { } placeholder || placeholder.IsPlaceholder)
                    {
                        return new ColorValue(0, 0, 0, 1);
                    }

                    var resolved = Base(placeholder, context, depth + 1);
                    foreach (var modifier in placeholder.Modifiers)
                    {
                        resolved = ColorTransforms.Apply(resolved, modifier.Name, modifier.Value);
                    }

                    return resolved;
                }

                var slot = context.Map.Map(color.Value);
                if (Array.IndexOf(ColorScheme.SlotNames, slot) < 0)
                {
                    return new ColorValue(0, 0, 0, 1);
                }

                var scheme = context.Scheme[slot];
                var state = Base(scheme, context, depth + 1);
                foreach (var modifier in scheme.Modifiers)
                {
                    state = ColorTransforms.Apply(state, modifier.Name, modifier.Value);
                }

                return state;
            default:
                return new ColorValue(0, 0, 0, 1);
        }
    }

    private static ColorValue FromHex(string hex)
    {
        var rgb = RgbColor.FromHex(hex);
        return new ColorValue(rgb.R / 255.0, rgb.G / 255.0, rgb.B / 255.0, 1);
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
