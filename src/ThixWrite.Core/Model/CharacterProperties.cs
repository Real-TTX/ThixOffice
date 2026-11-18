namespace ThixWrite.Core.Model;

/// <summary>Underline kinds (the values of Word's w:u).</summary>
public enum UnderlineStyle
{
    None,
    Single,
    Words,
    Double,
    Thick,
    Dotted,
    DottedHeavy,
    Dash,
    DashedHeavy,
    DashLong,
    DashLongHeavy,
    DotDash,
    DashDotHeavy,
    DotDotDash,
    DashDotDotHeavy,
    Wave,
    WavyHeavy,
    WavyDouble,
}

/// <summary>Raised or lowered text.</summary>
public enum VerticalPosition
{
    Baseline,
    Superscript,
    Subscript,
}

/// <summary>The highlight colours of Word (the "Texthervorhebungsfarbe" palette).</summary>
public enum HighlightColor
{
    None,
    Yellow,
    BrightGreen,
    Cyan,
    Magenta,
    Blue,
    Red,
    DarkBlue,
    DarkCyan,
    DarkGreen,
    DarkMagenta,
    DarkRed,
    DarkYellow,
    DarkGray,
    LightGray,
    Black,
    White,
}

/// <summary>
/// A text colour: <see cref="Value"/> "RRGGBB" or "auto" (black on white), optionally tied to a theme colour (kept for the
/// round trip; the theme colour wins when the theme is known).
/// </summary>
public sealed record TextColor(string Value, string? ThemeColor = null, string? ThemeTint = null, string? ThemeShade = null)
{
    public static readonly TextColor Auto = new("auto");

    public bool IsAuto => string.Equals(Value, "auto", StringComparison.OrdinalIgnoreCase);

    public static TextColor FromHex(string hex) => new(hex.TrimStart('#').ToUpperInvariant());
}

/// <summary>Which theme font a run uses instead of a named family.</summary>
public enum ThemeFontSlot
{
    /// <summary>The body font (Word: "+Textkörper", minorHAnsi).</summary>
    Minor,
    /// <summary>The heading font (Word: "+Überschriften", majorHAnsi).</summary>
    Major,
}

/// <summary>
/// Character formatting, immutable. Every property is nullable: null means "inherit" (from the character style, the paragraph
/// style, the document defaults – see Layout.StyleResolver). <see cref="UnknownXml"/> keeps the run-property children ThixWrite
/// does not model (w:lang, w:kern, w:spacing …) verbatim for the docx round trip.
/// </summary>
public sealed record CharacterProperties
{
    public static readonly CharacterProperties Empty = new();

    /// <summary>The character style (w:rStyle), on runs only.</summary>
    public string? StyleId { get; init; }

    /// <summary>The font family (w:rFonts ascii / hAnsi).</summary>
    public string? FontFamily { get; init; }

    /// <summary>A theme font instead of <see cref="FontFamily"/>.</summary>
    public ThemeFontSlot? FontTheme { get; init; }

    /// <summary>The w:rFonts element as read (east Asian and complex script fonts …), written back while the font is unchanged.</summary>
    public string? FontsXml { get; init; }

    /// <summary>Size in points (half-point steps in docx).</summary>
    public double? FontSize { get; init; }

    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public UnderlineStyle? Underline { get; init; }

    public bool? Strikethrough { get; init; }

    public bool? DoubleStrikethrough { get; init; }

    public VerticalPosition? VerticalPosition { get; init; }

    public TextColor? Color { get; init; }

    public HighlightColor? Highlight { get; init; }

    /// <summary>Run properties ThixWrite does not model, as XML, kept for the round trip.</summary>
    public string? UnknownXml { get; init; }

    public bool IsEmpty => Equals(Empty);

    /// <summary>These properties with every non-null property of <paramref name="other"/> on top.</summary>
    public CharacterProperties Merge(CharacterProperties? other)
    {
        if (other is null || other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return other;
        }

        var fontChanges = other.FontFamily is not null || other.FontTheme is not null;
        return new CharacterProperties
        {
            StyleId = other.StyleId ?? StyleId,
            FontFamily = fontChanges ? other.FontFamily : FontFamily,
            FontTheme = fontChanges ? other.FontTheme : FontTheme,
            FontsXml = fontChanges ? other.FontsXml : FontsXml,
            FontSize = other.FontSize ?? FontSize,
            Bold = other.Bold ?? Bold,
            Italic = other.Italic ?? Italic,
            Underline = other.Underline ?? Underline,
            Strikethrough = other.Strikethrough ?? Strikethrough,
            DoubleStrikethrough = other.DoubleStrikethrough ?? DoubleStrikethrough,
            VerticalPosition = other.VerticalPosition ?? VerticalPosition,
            Color = other.Color ?? Color,
            Highlight = other.Highlight ?? Highlight,
            UnknownXml = other.UnknownXml ?? UnknownXml,
        };
    }

    /// <summary>A direct font choice by the user: a named family, no theme font, no other scripts' fonts.</summary>
    public CharacterProperties WithFont(string family) => this with { FontFamily = family, FontTheme = null, FontsXml = null };
}
