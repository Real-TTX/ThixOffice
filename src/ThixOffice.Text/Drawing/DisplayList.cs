using System.Globalization;

namespace ThixOffice.Text.Drawing;

/// <summary>A colour with alpha; <see cref="FromHex"/> reads "RRGGBB" / "#RRGGBB".</summary>
public readonly record struct RgbColor(byte R, byte G, byte B, byte A = 255)
{
    public static readonly RgbColor Black = new(0, 0, 0);
    public static readonly RgbColor White = new(255, 255, 255);

    public static RgbColor FromHex(string hex)
    {
        var value = hex.TrimStart('#');
        if (value.Length != 6 || !uint.TryParse(value, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var rgb))
        {
            return Black;
        }

        return new RgbColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }

    public string ToHex() => $"{R:X2}{G:X2}{B:X2}";

    public override string ToString() => A == 255 ? $"#{ToHex()}" : $"#{ToHex()}{A:X2}";
}

/// <summary>How a line is drawn (underlines).</summary>
public enum LineStyle
{
    Solid,
    Double,
    Dotted,
    Dashed,
    Wavy,
}

/// <summary>Something to draw on a page, in points from the top-left corner of the page.</summary>
public abstract record DisplayItem;

/// <summary>
/// Text in one font and colour, starting at (<see cref="X"/>, baseline <see cref="Y"/>).
/// <see cref="CharacterX"/>: the x offset (from <see cref="X"/>) of every UTF-16 code unit of <see cref="Text"/> – enough to draw
/// the text in any renderer (browser canvas). <see cref="Glyphs"/> / <see cref="GlyphX"/>: the shaped glyphs of the measurer
/// that produced the layout (x offsets from <see cref="X"/>), drawn as they are by the renderer of the same technology; empty
/// when the measurer has no glyphs.
/// </summary>
public sealed record GlyphRunItem(
    double X,
    double Y,
    string Text,
    FontSpec Font,
    FontFace Face,
    RgbColor Color,
    double[] CharacterX,
    ushort[] Glyphs,
    float[] GlyphX) : DisplayItem;

/// <summary>A filled and/or outlined rectangle (highlight, shading, placeholder frames).</summary>
public sealed record RectangleItem(
    double X,
    double Y,
    double Width,
    double Height,
    RgbColor? Fill,
    RgbColor? Stroke = null,
    double StrokeWidth = 0) : DisplayItem;

/// <summary>A straight line (underline, strikethrough, separators).</summary>
public sealed record LineItem(
    double X1,
    double Y1,
    double X2,
    double Y2,
    RgbColor Color,
    double Thickness,
    LineStyle Style = LineStyle.Solid) : DisplayItem;

/// <summary>
/// A picture: its encoded bytes (PNG, JPEG, GIF, BMP) drawn into the rectangle (points). <see cref="Key"/> names the bytes
/// (a hash), so that a renderer decodes each picture once.
/// </summary>
public sealed record ImageItem(double X, double Y, double Width, double Height, string Key, byte[] Data) : DisplayItem;

/// <summary>Everything on one page, in drawing order; the size in points.</summary>
public sealed record PageDisplayList(int PageIndex, double Width, double Height, IReadOnlyList<DisplayItem> Items);

/// <summary>
/// Starts clipping to a rectangle: items until the matching <see cref="ClipEndItem"/> draw only inside it (grid cells whose
/// text overflows into neighbours or is cut at the cell border). Renderers that do not clip may ignore both items.
/// </summary>
public sealed record ClipBeginItem(double X, double Y, double Width, double Height) : DisplayItem;

/// <summary>Ends the innermost <see cref="ClipBeginItem"/>.</summary>
public sealed record ClipEndItem : DisplayItem
{
    public static ClipEndItem Instance { get; } = new();
}

/// <summary>What a segment of a <see cref="PathItem"/> does with its points.</summary>
public enum PathVerb : byte
{
    /// <summary>Starts a new figure at one point.</summary>
    Move,

    /// <summary>A straight line to one point.</summary>
    Line,

    /// <summary>A cubic Bézier curve through two control points to a third point.</summary>
    Cubic,

    /// <summary>Closes the figure (no point).</summary>
    Close,
}

/// <summary>How the ends and corners of a stroked path look.</summary>
public enum LineJoin : byte
{
    Miter,
    Round,
}

/// <summary>
/// A filled and/or stroked outline (chart lines, areas, pie slices, markers): <see cref="Verbs"/> with their points in
/// <see cref="Points"/> as x, y pairs (Move and Line take one pair, Cubic three, Close none). Arcs are given as Béziers, so a
/// renderer needs only these four verbs.
/// </summary>
public sealed record PathItem(
    PathVerb[] Verbs,
    double[] Points,
    RgbColor? Fill,
    RgbColor? Stroke = null,
    double StrokeWidth = 0,
    LineStyle Style = LineStyle.Solid,
    LineJoin Join = LineJoin.Round) : DisplayItem;

/// <summary>
/// Starts a transformation: items until the matching <see cref="TransformEndItem"/> are moved by (<see cref="X"/>,
/// <see cref="Y"/>), then rotated by <see cref="Rotation"/> degrees (clockwise, around that point) and scaled by
/// <see cref="Scale"/> – rotated text (axis titles, slanted category labels) and drawings placed in another unit.
/// </summary>
public sealed record TransformBeginItem(double X, double Y, double Rotation = 0, double Scale = 1) : DisplayItem;

/// <summary>Ends the innermost <see cref="TransformBeginItem"/>.</summary>
public sealed record TransformEndItem : DisplayItem
{
    public static TransformEndItem Instance { get; } = new();
}
