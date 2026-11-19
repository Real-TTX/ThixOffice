namespace ThixShow.Core.Model;

/// <summary>A built-in design: its theme and whether its master maps the background to the dark colour (dark slides).</summary>
public sealed record ThemeDesign(string Id, string Name, Theme Theme, bool IsDark);

/// <summary>
/// ThixShow's own designs (original colour and font schemes, not copies of Office's): "ThixShow" (the default) and five more. Each
/// becomes a master with the layouts of <see cref="MasterFactory"/>.
/// </summary>
public static class BuiltInThemes
{
    public static ThemeDesign ThixShow { get; } = Create("thixshow", "ThixShow", false, "Aptos Display", "Aptos",
        "1F1F1F", "FFFFFF", "2B3A4A", "EEF1F4", "C43E1C", "1F6FB2", "2E9E6A", "E3A21A", "7A4FB3", "2AA6B8", "1F6FB2", "7A4FB3");

    public static ThemeDesign Klar { get; } = Create("klar", "Klar", false, "Calibri Light", "Calibri",
        "222222", "FFFFFF", "34495E", "F4F6F7", "2D7FF9", "18A8E0", "20A97A", "F2A900", "E8355E", "7B4AE2", "2D7FF9", "7B4AE2");

    public static ThemeDesign Kontrast { get; } = Create("kontrast", "Kontrast", true, "Arial", "Arial",
        "111418", "F5F5F5", "1E2A38", "D9E1EA", "FFB000", "FE6100", "DC267F", "785EF0", "648FFF", "2EC4B6", "FFB000", "648FFF");

    public static ThemeDesign Natur { get; } = Create("natur", "Natur", false, "Georgia", "Verdana",
        "2F3B2F", "FFFFFF", "3E5641", "F1EFE7", "5B8C5A", "A3B18A", "D4A373", "8C6A4F", "588157", "BC6C25", "588157", "8C6A4F");

    public static ThemeDesign Abend { get; } = Create("abend", "Abend", true, "Trebuchet MS", "Trebuchet MS",
        "0B132B", "FFFFFF", "1C2541", "E0E6F0", "5BC0BE", "3A86FF", "FFBE0B", "FB5607", "FF006E", "8338EC", "5BC0BE", "8338EC");

    public static ThemeDesign Papier { get; } = Create("papier", "Papier", false, "Cambria", "Calibri",
        "3B3B3B", "FFFEFA", "5C4B3B", "F3EDE2", "B5523B", "4F6D7A", "C0A062", "6B8F71", "8E6C8A", "D08C60", "4F6D7A", "8E6C8A");

    /// <summary>Every built-in design, the default first.</summary>
    public static IReadOnlyList<ThemeDesign> All { get; } = [ThixShow, Klar, Kontrast, Natur, Abend, Papier];

    public static ThemeDesign? Find(string id) => All.FirstOrDefault(design => design.Id == id);

    private static ThemeDesign Create(string id, string name, bool dark, string major, string minor, params string[] colors) =>
        new(id, name, new Theme(name, ColorScheme.FromHex(name, colors), new FontScheme(name, new FontCollection(major), new FontCollection(minor)), Formats(name)), dark);

    /// <summary>ThixShow's format scheme: calm fills (solid, a soft gradient, a deeper solid), lines of 1, 1.5 and 2 pt, a soft shadow.</summary>
    public static FormatScheme Formats(string name)
    {
        var placeholder = ColorSpec.Placeholder;
        var softGradient = new GradientFill(
            [
                new GradientStop(0, placeholder.LumMod(105000).With("satMod", 103000).Tint(80000)),
                new GradientStop(100000, placeholder.LumMod(100000).Shade(92000)),
            ],
            LinearAngle: 5400000,
            LinearScaled: false,
            RotateWithShape: true);
        var deepGradient = new GradientFill(
            [
                new GradientStop(0, placeholder.Tint(94000)),
                new GradientStop(100000, placeholder.Shade(85000)),
            ],
            LinearAngle: 5400000,
            LinearScaled: false,
            RotateWithShape: true);
        var shadow = new OuterShadow(50800, 19050, 5400000, ColorSpec.Rgb("000000").Alpha(40000), "ctr", false);
        return new FormatScheme(
            name,
            [new SolidFill(placeholder), softGradient, deepGradient],
            [
                new Outline(12700, new SolidFill(placeholder), LineDash.Solid, LineCap.Flat, LineJoin.Miter, 800000, "sng", "ctr"),
                new Outline(19050, new SolidFill(placeholder), LineDash.Solid, LineCap.Flat, LineJoin.Miter, 800000, "sng", "ctr"),
                new Outline(25400, new SolidFill(placeholder), LineDash.Solid, LineCap.Flat, LineJoin.Miter, 800000, "sng", "ctr"),
            ],
            [ShapeEffects.Empty, ShapeEffects.Empty, new ShapeEffects(shadow)],
            [
                new SolidFill(placeholder),
                new SolidFill(placeholder.Tint(95000).With("satMod", 170000)),
                new GradientFill(
                    [
                        new GradientStop(0, placeholder.Tint(93000).With("satMod", 150000)),
                        new GradientStop(100000, placeholder.Shade(98000).With("satMod", 120000)),
                    ],
                    LinearAngle: 5400000,
                    LinearScaled: false,
                    RotateWithShape: true),
            ]);
    }
}
