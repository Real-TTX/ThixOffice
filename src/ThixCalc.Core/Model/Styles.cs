using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Model;

/// <summary>How a <see cref="CalcColor"/> is given.</summary>
public enum ColorKind : byte
{
    /// <summary>The automatic colour (black text, the window's colours).</summary>
    Auto,
    Rgb,
    Theme,
    Indexed,
}

/// <summary>
/// A colour as SpreadsheetML gives it: ARGB, a theme colour with a tint (−1 … 1), an index into the legacy palette or
/// automatic. Kept as given so files round-trip; <see cref="Resolve"/> turns it into a drawing colour.
/// </summary>
public readonly record struct CalcColor(ColorKind Kind, uint Argb = 0, int Index = 0, double Tint = 0)
{
    public static readonly CalcColor Automatic = new(ColorKind.Auto);

    public static CalcColor FromRgb(byte red, byte green, byte blue) => new(ColorKind.Rgb, 0xFF000000u | ((uint)red << 16) | ((uint)green << 8) | blue);

    /// <summary>"FF00B050" or "00B050".</summary>
    public static CalcColor FromHex(string hex)
    {
        var value = Convert.ToUInt32(hex.TrimStart('#'), 16);
        if (hex.TrimStart('#').Length <= 6)
        {
            value |= 0xFF000000u;
        }

        return new CalcColor(ColorKind.Rgb, value);
    }

    public static CalcColor FromTheme(int index, double tint = 0) => new(ColorKind.Theme, 0, index, tint);

    public static CalcColor FromIndex(int index) => new(ColorKind.Indexed, 0, index);

    /// <summary>The colour to draw with; <paramref name="automatic"/> stands in for <see cref="ColorKind.Auto"/>.</summary>
    public RgbColor Resolve(ThemeColors theme, RgbColor automatic)
    {
        var color = Kind switch
        {
            ColorKind.Rgb => new RgbColor((byte)(Argb >> 16), (byte)(Argb >> 8), (byte)Argb),
            ColorKind.Theme => theme[Index],
            ColorKind.Indexed => Index is 64 or 65 ? automatic : IndexedColors.Get(Index),
            _ => automatic,
        };

        return Tint == 0 ? color : ThemeColors.ApplyTint(color, Tint);
    }

    public string ToHex() => Argb.ToString("X8", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>The legacy 64-colour palette of SpreadsheetML (indexed colours, ECMA-376 Part 1, 18.8.27).</summary>
public static class IndexedColors
{
    private static readonly uint[] Palette =
    [
        0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
        0x000000, 0xFFFFFF, 0xFF0000, 0x00FF00, 0x0000FF, 0xFFFF00, 0xFF00FF, 0x00FFFF,
        0x800000, 0x008000, 0x000080, 0x808000, 0x800080, 0x008080, 0xC0C0C0, 0x808080,
        0x9999FF, 0x993366, 0xFFFFCC, 0xCCFFFF, 0x660066, 0xFF8080, 0x0066CC, 0xCCCCFF,
        0x000080, 0xFF00FF, 0xFFFF00, 0x00FFFF, 0x800080, 0x800000, 0x008080, 0x0000FF,
        0x00CCFF, 0xCCFFFF, 0xCCFFCC, 0xFFFF99, 0x99CCFF, 0xFF99CC, 0xCC99FF, 0xFFCC99,
        0x3366FF, 0x33CCCC, 0x99CC00, 0xFFCC00, 0xFF9900, 0xFF6600, 0x666699, 0x969696,
        0x003366, 0x339966, 0x003300, 0x333300, 0x993300, 0x993366, 0x333399, 0x333333,
    ];

    public static RgbColor Get(int index)
    {
        var rgb = index >= 0 && index < Palette.Length ? Palette[index] : 0;
        return new RgbColor((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);
    }
}

/// <summary>
/// The twelve colours of the workbook's theme in SpreadsheetML index order: 0 lt1, 1 dk1, 2 lt2, 3 dk2, 4–9 accent1–6,
/// 10 hlink, 11 folHlink (the light/dark pairs are swapped against the theme part, as Excel does).
/// </summary>
public sealed record ThemeColors(IReadOnlyList<RgbColor> Colors)
{
    /// <summary>The Office theme (2013–2022).</summary>
    public static ThemeColors Office { get; } = new([
        RgbColor.FromHex("FFFFFF"), RgbColor.FromHex("000000"), RgbColor.FromHex("E7E6E6"), RgbColor.FromHex("44546A"),
        RgbColor.FromHex("4472C4"), RgbColor.FromHex("ED7D31"), RgbColor.FromHex("A5A5A5"), RgbColor.FromHex("FFC000"),
        RgbColor.FromHex("5B9BD5"), RgbColor.FromHex("70AD47"), RgbColor.FromHex("0563C1"), RgbColor.FromHex("954F72"),
    ]);

    public RgbColor this[int index] => index >= 0 && index < Colors.Count ? Colors[index] : RgbColor.Black;

    /// <summary>Lightens (tint &gt; 0) or darkens (tint &lt; 0) in HSL luminance, as SpreadsheetML defines the tint.</summary>
    public static RgbColor ApplyTint(RgbColor color, double tint)
    {
        var (hue, saturation, luminance) = ToHsl(color);
        luminance = tint < 0 ? luminance * (1 + tint) : luminance * (1 - tint) + tint;
        return FromHsl(hue, saturation, Math.Clamp(luminance, 0, 1), color.A);
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
            var gray = (byte)Math.Round(luminance * 255);
            return new RgbColor(gray, gray, gray, alpha);
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

public enum UnderlineKind : byte
{
    None,
    Single,
    Double,
    SingleAccounting,
    DoubleAccounting,
}

public enum FontScript : byte
{
    Baseline,
    Superscript,
    Subscript,
}

/// <summary>A cell font (styles.xml &lt;font&gt;).</summary>
public sealed record CellFont
{
    public static CellFont Default { get; } = new();

    public string Name { get; init; } = "Calibri";

    public double Size { get; init; } = 11;

    public bool Bold { get; init; }

    public bool Italic { get; init; }

    public UnderlineKind Underline { get; init; }

    public bool Strikethrough { get; init; }

    public FontScript Script { get; init; }

    public CalcColor? Color { get; init; }

    /// <summary>The font family class (2 = roman, 2 = swiss …) as the file gives it.</summary>
    public int? Family { get; init; }

    /// <summary>"minor" / "major" when the font follows the theme.</summary>
    public string? Scheme { get; init; }

    public int? Charset { get; init; }

    public bool Outline { get; init; }

    public bool Shadow { get; init; }
}

/// <summary>The pattern of a cell fill (ST_PatternType).</summary>
public enum FillPattern : byte
{
    None,
    Solid,
    MediumGray,
    DarkGray,
    LightGray,
    DarkHorizontal,
    DarkVertical,
    DarkDown,
    DarkUp,
    DarkGrid,
    DarkTrellis,
    LightHorizontal,
    LightVertical,
    LightDown,
    LightUp,
    LightGrid,
    LightTrellis,
    Gray125,
    Gray0625,
}

/// <summary>
/// A cell fill: a pattern with foreground and background colours (a solid fill's colour is the foreground). A gradient fill
/// is kept as its XML (<see cref="GradientXml"/>) and drawn with its first stop.
/// </summary>
public sealed record CellFill(FillPattern Pattern = FillPattern.None, CalcColor? Foreground = null, CalcColor? Background = null, string? GradientXml = null)
{
    public static CellFill None { get; } = new();

    /// <summary>The second fill every styles part has.</summary>
    public static CellFill Gray125 { get; } = new(FillPattern.Gray125);

    public static CellFill Solid(CalcColor color) => new(FillPattern.Solid, color, CalcColor.FromIndex(64));
}

/// <summary>The line style of a border edge (ST_BorderStyle).</summary>
public enum BorderStyle : byte
{
    None,
    Thin,
    Medium,
    Dashed,
    Dotted,
    Thick,
    Double,
    Hair,
    MediumDashed,
    DashDot,
    MediumDashDot,
    DashDotDot,
    MediumDashDotDot,
    SlantDashDot,
}

public readonly record struct BorderEdge(BorderStyle Style, CalcColor? Color = null)
{
    public static readonly BorderEdge None = default;

    public bool IsVisible => Style != BorderStyle.None;
}

/// <summary>The borders of a cell (styles.xml &lt;border&gt;).</summary>
public sealed record CellBorders
{
    public static CellBorders None { get; } = new();

    public BorderEdge Left { get; init; }

    public BorderEdge Right { get; init; }

    public BorderEdge Top { get; init; }

    public BorderEdge Bottom { get; init; }

    public BorderEdge Diagonal { get; init; }

    public bool DiagonalUp { get; init; }

    public bool DiagonalDown { get; init; }

    public static CellBorders All(BorderStyle style, CalcColor? color = null) => new()
    {
        Left = new BorderEdge(style, color),
        Right = new BorderEdge(style, color),
        Top = new BorderEdge(style, color),
        Bottom = new BorderEdge(style, color),
    };
}

public enum HorizontalAlignment : byte
{
    General,
    Left,
    Center,
    Right,
    Fill,
    Justify,
    CenterContinuous,
    Distributed,
}

public enum VerticalAlignment : byte
{
    Bottom,
    Top,
    Center,
    Justify,
    Distributed,
}

/// <summary>Alignment of a cell (styles.xml &lt;alignment&gt;).</summary>
public sealed record CellAlignment
{
    public static CellAlignment Default { get; } = new();

    public HorizontalAlignment Horizontal { get; init; }

    public VerticalAlignment Vertical { get; init; }

    public bool WrapText { get; init; }

    public int Indent { get; init; }

    /// <summary>0–90 counter-clockwise, 91–180 clockwise (90 + degrees), 255 vertical stacked text.</summary>
    public int TextRotation { get; init; }

    public bool ShrinkToFit { get; init; }

    /// <summary>0 context, 1 left-to-right, 2 right-to-left.</summary>
    public int ReadingOrder { get; init; }
}

public sealed record CellProtection(bool Locked = true, bool Hidden = false)
{
    public static CellProtection Default { get; } = new();
}

/// <summary>
/// The look of a cell: number format code, font, fill, borders, alignment, protection. Immutable with value equality; cells
/// refer to it by an id of the workbook's <see cref="StyleTable"/>, so equal styles are stored once.
/// </summary>
public sealed record CellStyle
{
    public static CellStyle Default { get; } = new();

    /// <summary>The format code in the file's (English) form, "General" by default.</summary>
    public string NumberFormat { get; init; } = "General";

    public CellFont Font { get; init; } = CellFont.Default;

    public CellFill Fill { get; init; } = CellFill.None;

    public CellBorders Borders { get; init; } = CellBorders.None;

    public CellAlignment Alignment { get; init; } = CellAlignment.Default;

    public CellProtection Protection { get; init; } = CellProtection.Default;

    /// <summary>The index of the named cell style (cellStyleXfs) this format is based on; 0 = "Standard".</summary>
    public int ParentStyle { get; init; }

    /// <summary>The quote prefix: the text was typed with a leading apostrophe.</summary>
    public bool QuotePrefix { get; init; }
}

/// <summary>
/// A differential format of a conditional format or table style (styles.xml &lt;dxf&gt;): only what it sets. <see cref="Xml"/>
/// keeps the original element so parts the model does not show are written back unchanged.
/// </summary>
public sealed record DifferentialFormat
{
    public string? NumberFormat { get; init; }

    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public bool? Strikethrough { get; init; }

    public UnderlineKind? Underline { get; init; }

    public CalcColor? FontColor { get; init; }

    public CalcColor? FillColor { get; init; }

    public CalcColor? BorderColor { get; init; }

    public BorderStyle? BorderStyle { get; init; }

    /// <summary>The &lt;dxf&gt; this was read from (written back as it is while the format is unchanged), or null.</summary>
    public string? Xml { get; init; }
}

/// <summary>A named cell style ("Standard", "Gut", "Überschrift 1" …, styles.xml cellStyles), with its built-in id.</summary>
public sealed record NamedCellStyle(string Name, int FormatIndex, int? BuiltInId, string? Xml = null);

/// <summary>
/// The workbook's shared, immutable cell styles: id 0 is the default; <see cref="Intern"/> returns the id of an equal style
/// (adding it once). Ids never change while the workbook is open, so cells and undo steps can keep them.
/// </summary>
public sealed class StyleTable
{
    private readonly List<CellStyle> _styles = [];
    private readonly Dictionary<CellStyle, int> _ids = [];

    public StyleTable(CellStyle defaultStyle)
    {
        Intern(defaultStyle);
    }

    public int Count => _styles.Count;

    public CellStyle Default => _styles[0];

    public CellStyle this[int id] => id >= 0 && id < _styles.Count ? _styles[id] : _styles[0];

    public IReadOnlyList<CellStyle> All => _styles;

    /// <summary>The differential formats (conditional formats, table styles), by dxfId.</summary>
    public List<DifferentialFormat> DifferentialFormats { get; } = [];

    /// <summary>The named styles (cellStyleXfs with cellStyles), kept for the round trip.</summary>
    public List<CellStyle> NamedStyleFormats { get; } = [];

    public List<NamedCellStyle> NamedStyles { get; } = [];

    /// <summary>XML of styles.xml parts the model keeps without showing them (tableStyles, colors, extLst).</summary>
    public List<string> PreservedXml { get; } = [];

    public int Intern(CellStyle style)
    {
        if (_ids.TryGetValue(style, out var id))
        {
            return id;
        }

        id = _styles.Count;
        _styles.Add(style);
        _ids[style] = id;
        return id;
    }

    /// <summary>Adds a style as the next id even when an equal one exists (keeps the xf ids of a file stable).</summary>
    internal int AddExact(CellStyle style)
    {
        var id = _styles.Count;
        _styles.Add(style);
        _ids.TryAdd(style, id);
        return id;
    }

    /// <summary>Loading: the file's default format becomes style 0.</summary>
    internal void ReplaceDefault(CellStyle style)
    {
        _ids.Remove(_styles[0]);
        _styles[0] = style;
        _ids[style] = 0;
    }

    /// <summary>The id of <paramref name="id"/>'s style changed by <paramref name="change"/>.</summary>
    public int With(int id, Func<CellStyle, CellStyle> change) => Intern(change(this[id]));
}
