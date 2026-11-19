namespace ThixShow.Core.Model;

/// <summary>The base of a DrawingML colour (EG_ColorChoice).</summary>
public enum ColorKind
{
    /// <summary>a:srgbClr, <see cref="ColorSpec.Value"/> "RRGGBB".</summary>
    Rgb,

    /// <summary>a:schemeClr, value "accent1", "tx1", "bg2", "phClr" …</summary>
    Scheme,

    /// <summary>a:sysClr, value "windowText"; <see cref="ColorSpec.LastColor"/> is the colour the file was saved with.</summary>
    System,

    /// <summary>a:prstClr, value "red", "cornflowerBlue" …</summary>
    Preset,

    /// <summary>a:hslClr, value "hue,sat,lum" (60000ths of a degree, 1000ths of a percent).</summary>
    Hsl,

    /// <summary>a:scrgbClr, value "r,g,b" (linear, 1000ths of a percent).</summary>
    ScRgb,
}

/// <summary>
/// A colour transform of DrawingML in file order: "lumMod" 75000, "alpha" 50000, "shade" 50000, "comp" (no value) … The full list
/// is kept as read, so a colour round-trips exactly; <see cref="Drawing.ColorResolver"/> applies them.
/// </summary>
public sealed record ColorModifier(string Name, int? Value = null)
{
    public override string ToString() => Value is { } value ? $"{Name}={value}" : Name;
}

/// <summary>
/// A colour as DrawingML writes it: a base (<see cref="Kind"/> + <see cref="Value"/>) and transforms in order. Resolved against a
/// theme and colour map at drawing time (<see cref="Drawing.ColorResolver"/>), so a theme change recolours everything that names
/// scheme colours.
/// </summary>
public sealed record ColorSpec(ColorKind Kind, string Value, ValueList<ColorModifier> Modifiers = default, string? LastColor = null)
{
    public static ColorSpec Rgb(string hex) => new(ColorKind.Rgb, hex.TrimStart('#').ToUpperInvariant());

    public static ColorSpec Scheme(string name) => new(ColorKind.Scheme, name);

    public static ColorSpec Preset(string name) => new(ColorKind.Preset, name);

    public static ColorSpec System(string name, string lastColor) => new(ColorKind.System, name, default, lastColor);

    /// <summary>The placeholder colour of theme styles (a:schemeClr val="phClr"), replaced by the referencing shape's colour.</summary>
    public static ColorSpec Placeholder { get; } = Scheme("phClr");

    public bool IsPlaceholder => Kind == ColorKind.Scheme && Value == "phClr";

    public ColorSpec With(string name, int? value = null) => this with { Modifiers = Modifiers.Add(new ColorModifier(name, value)) };

    public ColorSpec LumMod(int value) => With("lumMod", value);

    public ColorSpec LumOff(int value) => With("lumOff", value);

    public ColorSpec Tint(int value) => With("tint", value);

    public ColorSpec Shade(int value) => With("shade", value);

    public ColorSpec Alpha(int value) => With("alpha", value);

    /// <summary>The opacity this colour asks for (1 = opaque) from its alpha transforms, without resolving the colour.</summary>
    public double Opacity
    {
        get
        {
            var alpha = 1.0;
            foreach (var modifier in Modifiers)
            {
                alpha = modifier.Name switch
                {
                    "alpha" => (modifier.Value ?? Units.Percent100) / (double)Units.Percent100,
                    "alphaMod" => alpha * ((modifier.Value ?? Units.Percent100) / (double)Units.Percent100),
                    "alphaOff" => alpha + ((modifier.Value ?? 0) / (double)Units.Percent100),
                    _ => alpha,
                };
            }

            return Math.Clamp(alpha, 0, 1);
        }
    }

    /// <summary>The colour without its alpha transforms (for a picker that shows transparency apart).</summary>
    public ColorSpec WithoutAlpha() =>
        this with { Modifiers = Modifiers.RemoveAll(modifier => modifier.Name is "alpha" or "alphaMod" or "alphaOff") };

    /// <summary>The colour with opacity <paramref name="opacity"/> (0–1; 1 removes the alpha transforms).</summary>
    public ColorSpec WithOpacity(double opacity)
    {
        var plain = WithoutAlpha();
        return opacity >= 1 ? plain : plain.Alpha(Units.FactorToPercent(Math.Clamp(opacity, 0, 1)));
    }

    public override string ToString() => Modifiers.IsEmpty ? $"{Kind}:{Value}" : $"{Kind}:{Value} {Modifiers}";
}

/// <summary>The twelve colours of a theme (a:clrScheme), each an sRGB or system colour.</summary>
public sealed record ColorScheme(
    string Name,
    ColorSpec Dark1,
    ColorSpec Light1,
    ColorSpec Dark2,
    ColorSpec Light2,
    ColorSpec Accent1,
    ColorSpec Accent2,
    ColorSpec Accent3,
    ColorSpec Accent4,
    ColorSpec Accent5,
    ColorSpec Accent6,
    ColorSpec Hyperlink,
    ColorSpec FollowedHyperlink)
{
    /// <summary>The slot names in file order.</summary>
    public static readonly string[] SlotNames = ["dk1", "lt1", "dk2", "lt2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"];

    public ColorSpec this[string slot] => slot switch
    {
        "dk1" => Dark1,
        "lt1" => Light1,
        "dk2" => Dark2,
        "lt2" => Light2,
        "accent1" => Accent1,
        "accent2" => Accent2,
        "accent3" => Accent3,
        "accent4" => Accent4,
        "accent5" => Accent5,
        "accent6" => Accent6,
        "hlink" => Hyperlink,
        "folHlink" => FollowedHyperlink,
        _ => throw new ArgumentOutOfRangeException(nameof(slot), slot, null),
    };

    /// <summary>A scheme from twelve "RRGGBB" values in <see cref="SlotNames"/> order.</summary>
    public static ColorScheme FromHex(string name, params string[] hex)
    {
        if (hex.Length != 12)
        {
            throw new ArgumentException("Twelve colours are required.", nameof(hex));
        }

        var colors = hex.Select(ColorSpec.Rgb).ToArray();
        return new ColorScheme(name, colors[0], colors[1], colors[2], colors[3], colors[4], colors[5], colors[6], colors[7], colors[8], colors[9], colors[10], colors[11]);
    }
}

/// <summary>
/// The colour mapping of a master (p:clrMap) or a slide's override (p:clrMapOvr): which scheme slot bg1, tx1, bg2, tx2, accent1–6,
/// hlink and folHlink mean (a dark slide maps bg1 to dk1).
/// </summary>
public sealed record ColorMap(
    string Background1 = "lt1",
    string Text1 = "dk1",
    string Background2 = "lt2",
    string Text2 = "dk2",
    string Accent1 = "accent1",
    string Accent2 = "accent2",
    string Accent3 = "accent3",
    string Accent4 = "accent4",
    string Accent5 = "accent5",
    string Accent6 = "accent6",
    string Hyperlink = "hlink",
    string FollowedHyperlink = "folHlink")
{
    public static readonly ColorMap Default = new();

    /// <summary>The attribute names of p:clrMap in schema order.</summary>
    public static readonly string[] AttributeNames = ["bg1", "tx1", "bg2", "tx2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6", "hlink", "folHlink"];

    /// <summary>The scheme slot a name of a colour reference maps to ("bg1" → "lt1"; "dk1" stays "dk1").</summary>
    public string Map(string name) => name switch
    {
        "bg1" => Background1,
        "tx1" => Text1,
        "bg2" => Background2,
        "tx2" => Text2,
        "accent1" => Accent1,
        "accent2" => Accent2,
        "accent3" => Accent3,
        "accent4" => Accent4,
        "accent5" => Accent5,
        "accent6" => Accent6,
        "hlink" => Hyperlink,
        "folHlink" => FollowedHyperlink,
        _ => name,
    };

    public string ValueOf(string attribute) => attribute switch
    {
        "bg1" => Background1,
        "tx1" => Text1,
        "bg2" => Background2,
        "tx2" => Text2,
        _ => Map(attribute),
    };

    public static ColorMap FromValues(Func<string, string?> value) => new(
        value("bg1") ?? "lt1",
        value("tx1") ?? "dk1",
        value("bg2") ?? "lt2",
        value("tx2") ?? "dk2",
        value("accent1") ?? "accent1",
        value("accent2") ?? "accent2",
        value("accent3") ?? "accent3",
        value("accent4") ?? "accent4",
        value("accent5") ?? "accent5",
        value("accent6") ?? "accent6",
        value("hlink") ?? "hlink",
        value("folHlink") ?? "folHlink");
}
