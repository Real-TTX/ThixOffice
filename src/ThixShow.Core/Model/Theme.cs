namespace ThixShow.Core.Model;

/// <summary>The fonts of one kind of a font scheme: Latin, East Asian and complex script typefaces (a:majorFont / a:minorFont).</summary>
public sealed record FontCollection(string Latin, string EastAsian = "", string ComplexScript = "", XmlFragment? Supplemental = null);

/// <summary>a:fontScheme: the heading (major) and body (minor) fonts that "+mj-lt" / "+mn-lt" name.</summary>
public sealed record FontScheme(string Name, FontCollection Major, FontCollection Minor)
{
    /// <summary>The typeface a theme font reference names ("+mn-lt" → the minor Latin font); other names stay.</summary>
    public string Resolve(string typeface) => typeface switch
    {
        "+mj-lt" => Major.Latin,
        "+mn-lt" => Minor.Latin,
        "+mj-ea" => Major.EastAsian.Length > 0 ? Major.EastAsian : Major.Latin,
        "+mn-ea" => Minor.EastAsian.Length > 0 ? Minor.EastAsian : Minor.Latin,
        "+mj-cs" => Major.ComplexScript.Length > 0 ? Major.ComplexScript : Major.Latin,
        "+mn-cs" => Minor.ComplexScript.Length > 0 ? Minor.ComplexScript : Minor.Latin,
        _ => typeface,
    };
}

/// <summary>
/// a:fmtScheme: the theme's fill, line, effect and background styles (index 1–3 each; background fills 1001–1003) that shapes
/// reference through p:style. Colours inside are usually <see cref="ColorSpec.Placeholder"/>.
/// </summary>
public sealed record FormatScheme(
    string Name,
    ValueList<Fill> FillStyles,
    ValueList<Outline> LineStyles,
    ValueList<ShapeEffects> EffectStyles,
    ValueList<Fill> BackgroundFillStyles)
{
    /// <summary>The fill of a fill reference index (1-based; 1001+ are background fills; 0 = none).</summary>
    public Fill? FillOf(int index)
    {
        if (index >= 1001)
        {
            var background = index - 1001;
            return background < BackgroundFillStyles.Count ? BackgroundFillStyles[background] : null;
        }

        return index >= 1 && index <= FillStyles.Count ? FillStyles[index - 1] : null;
    }

    public Outline? LineOf(int index) => index >= 1 && index <= LineStyles.Count ? LineStyles[index - 1] : null;

    public ShapeEffects? EffectOf(int index) => index >= 1 && index <= EffectStyles.Count ? EffectStyles[index - 1] : null;
}

/// <summary>
/// A theme (a:theme): colours, fonts, format styles. <see cref="Source"/> is the part's XML as read (object defaults, extra colour
/// lists and extensions are written back from it).
/// </summary>
public sealed record Theme(string Name, ColorScheme Colors, FontScheme Fonts, FormatScheme Formats, XmlFragment? Source = null);
