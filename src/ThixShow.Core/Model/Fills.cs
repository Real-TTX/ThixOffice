namespace ThixShow.Core.Model;

/// <summary>How a shape, background or text is filled (EG_FillProperties).</summary>
public abstract record Fill;

/// <summary>a:noFill.</summary>
public sealed record NoFill : Fill
{
    public static NoFill Instance { get; } = new();
}

/// <summary>a:solidFill.</summary>
public sealed record SolidFill(ColorSpec Color) : Fill;

/// <summary>A colour at a position (0–100000) of a gradient.</summary>
public sealed record GradientStop(int Position, ColorSpec Color);

/// <summary>The kind of a path gradient (a:path path="…").</summary>
public enum GradientPathKind
{
    Circle,
    Rectangle,
    Shape,
}

/// <summary>
/// a:gradFill: stops plus either a linear direction (<see cref="LinearAngle"/> in 60000ths of a degree, clockwise from
/// left→right; <see cref="LinearScaled"/>) or a path gradient (<see cref="PathKind"/> with its focus <see cref="FillToRect"/>).
/// </summary>
public sealed record GradientFill(
    ValueList<GradientStop> Stops,
    int? LinearAngle = 0,
    bool? LinearScaled = null,
    GradientPathKind? PathKind = null,
    RelativeRect? FillToRect = null,
    RelativeRect? TileRect = null,
    bool? RotateWithShape = null,
    string? Flip = null) : Fill
{
    public bool IsLinear => PathKind is null;
}

/// <summary>How a picture fills its area: stretched into an inset rectangle or tiled.</summary>
public abstract record PictureFillMode;

/// <summary>a:stretch/a:fillRect: the picture fills the area minus the insets.</summary>
public sealed record StretchMode(RelativeRect FillRect) : PictureFillMode
{
    public static StretchMode Full { get; } = new(RelativeRect.Zero);
}

/// <summary>a:tile: offset (EMU), scale (1000ths of a percent), flip and alignment of the tiles.</summary>
public sealed record TileMode(long OffsetX = 0, long OffsetY = 0, int ScaleX = 100000, int ScaleY = 100000, string? Flip = null, string? Alignment = null)
    : PictureFillMode;

/// <summary>
/// a:blipFill of a shape or a picture: the picture (<see cref="Image"/>; null when linked or missing), the crop (a:srcRect) and
/// the fill mode. <see cref="BlipEffects"/> keeps the a:blip's children (alphaModFix, lum, duotone …) – only the alpha is drawn.
/// </summary>
public sealed record PictureFill(
    ImageData? Image,
    RelativeRect Crop,
    PictureFillMode Mode,
    int? Dpi = null,
    bool? RotateWithShape = null,
    XmlFragment? BlipEffects = null,
    string? LinkTarget = null) : Fill
{
    /// <summary>The opacity of the picture (a:alphaModFix inside the blip), 1 when none.</summary>
    public double Opacity { get; init; } = 1;
}

/// <summary>a:pattFill: a preset pattern ("pct50", "dkHorz" …) in two colours (drawn as a mix of both).</summary>
public sealed record PatternFill(string Preset, ColorSpec? Foreground, ColorSpec? Background) : Fill;

/// <summary>a:grpFill: the fill of the group the shape is in.</summary>
public sealed record GroupFill : Fill
{
    public static GroupFill Instance { get; } = new();
}
