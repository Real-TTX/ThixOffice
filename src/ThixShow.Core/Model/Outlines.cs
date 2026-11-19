namespace ThixShow.Core.Model;

/// <summary>Line end shapes (a:headEnd / a:tailEnd type).</summary>
public enum LineEndKind
{
    None,
    Triangle,
    Stealth,
    Diamond,
    Oval,
    Arrow,
}

/// <summary>Small / medium / large (line end width and length).</summary>
public enum LineEndSize
{
    Small,
    Medium,
    Large,
}

/// <summary>An arrowhead or other end of a line.</summary>
public sealed record LineEnd(LineEndKind Kind, LineEndSize Width = LineEndSize.Medium, LineEndSize Length = LineEndSize.Medium);

/// <summary>Line caps (a:ln cap).</summary>
public enum LineCap
{
    Flat,
    Round,
    Square,
}

/// <summary>Line joins (a:round, a:bevel, a:miter).</summary>
public enum LineJoin
{
    Round,
    Bevel,
    Miter,
}

/// <summary>
/// A dash pattern: a preset (a:prstDash "solid", "dash", "sysDot" …) or custom stops (a:custDash: dash and space lengths in
/// 1000ths of a percent of the line width).
/// </summary>
public sealed record LineDash(string? Preset, ValueList<(int Dash, int Space)> Custom = default)
{
    public static LineDash Solid { get; } = new("solid");

    public bool IsSolid => Preset == "solid" || (Preset is null && Custom.IsEmpty);

    /// <summary>The dash pattern in multiples of the line width (dash, gap, dash, gap …); empty for a solid line.</summary>
    public double[] Pattern(LineCap cap)
    {
        if (!Custom.IsEmpty)
        {
            return Custom.SelectMany(stop => new[] { stop.Dash / 100000.0, stop.Space / 100000.0 }).ToArray();
        }

        // ECMA-376 20.1.10.48 (ST_PresetLineDashVal): the "sys" dashes are relative to the width, measured without caps.
        return Preset switch
        {
            "dot" => [1, 3],
            "dash" => [4, 3],
            "lgDash" => [8, 3],
            "dashDot" => [4, 3, 1, 3],
            "lgDashDot" => [8, 3, 1, 3],
            "lgDashDotDot" => [8, 3, 1, 3, 1, 3],
            "sysDash" => [3, 1],
            "sysDot" => [1, 1],
            "sysDashDot" => [3, 1, 1, 1],
            "sysDashDotDot" => [3, 1, 1, 1, 1, 1],
            _ => [],
        };
    }
}

/// <summary>
/// a:ln: every property optional, so that a shape's own outline layers over its style's line (p:style/a:lnRef) and a
/// placeholder's inherited one. <see cref="Width"/> in EMU; <see cref="Compound"/> "sng", "dbl" … (drawn single);
/// <see cref="Alignment"/> "ctr" / "in".
/// </summary>
public sealed record Outline(
    long? Width = null,
    Fill? Fill = null,
    LineDash? Dash = null,
    LineCap? Cap = null,
    LineJoin? Join = null,
    int? MiterLimit = null,
    string? Compound = null,
    string? Alignment = null,
    LineEnd? Head = null,
    LineEnd? Tail = null)
{
    public static Outline None { get; } = new(Fill: NoFill.Instance);

    /// <summary>The properties of <paramref name="over"/> where it has them, else these.</summary>
    public Outline Merge(Outline? over) => over is null ? this : new Outline(
        over.Width ?? Width,
        over.Fill ?? Fill,
        over.Dash ?? Dash,
        over.Cap ?? Cap,
        over.Join ?? Join,
        over.MiterLimit ?? MiterLimit,
        over.Compound ?? Compound,
        over.Alignment ?? Alignment,
        over.Head ?? Head,
        over.Tail ?? Tail);
}

/// <summary>
/// a:outerShdw: blur radius and distance in EMU, direction in 60000ths of a degree, colour, scale (1000ths of a percent) and the
/// alignment of the shadow ("b", "ctr" …).
/// </summary>
public sealed record OuterShadow(
    long BlurRadius,
    long Distance,
    int Direction,
    ColorSpec Color,
    string? Alignment = null,
    bool? RotateWithShape = null,
    int ScaleX = 100000,
    int ScaleY = 100000,
    int SkewX = 0,
    int SkewY = 0);

/// <summary>
/// The effects of a shape (a:effectLst): the outer shadow is drawn and editable; every other effect (glow, soft edges,
/// reflection, inner shadow …) stays in <see cref="Others"/> (an a:effectLst without the shadow) and is written back.
/// <see cref="IsDag"/>: the effects were an a:effectDag, kept whole in <see cref="Others"/>.
/// </summary>
public sealed record ShapeEffects(OuterShadow? Shadow, XmlFragment? Others = null, bool IsDag = false)
{
    public static ShapeEffects Empty { get; } = new(Shadow: null);
}

/// <summary>A reference into the theme's format scheme (p:style a:lnRef/a:fillRef/a:effectRef/a:bgRef): index and colour.</summary>
public sealed record StyleReference(int Index, ColorSpec? Color);

/// <summary>A font reference of a shape style (a:fontRef idx="minor"/"major"/"none" with a colour).</summary>
public sealed record FontReference(string Index, ColorSpec? Color);

/// <summary>p:style: the theme styles a shape takes its line, fill, effects and text font/colour from.</summary>
public sealed record ShapeStyle(StyleReference Line, StyleReference Fill, StyleReference Effect, FontReference Font);
