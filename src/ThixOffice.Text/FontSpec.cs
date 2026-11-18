namespace ThixOffice.Text;

/// <summary>A font as the layout asks for it: family name, size in points, weight and slant.</summary>
public readonly record struct FontSpec(string Family, double Size, bool Bold = false, bool Italic = false)
{
    public FontSpec WithSize(double size) => this with { Size = size };

    public override string ToString() => $"{Family} {Size:0.##}pt{(Bold ? " bold" : "")}{(Italic ? " italic" : "")}";
}

/// <summary>
/// Vertical metrics of a font at a size, in points. <see cref="Ascent"/> and <see cref="Descent"/> are both positive
/// (above / below the baseline); the single line height of a word processor is <see cref="LineHeight"/>.
/// </summary>
public readonly record struct FontMetrics(
    double Ascent,
    double Descent,
    double LineGap,
    double UnderlinePosition,
    double UnderlineThickness,
    double StrikeoutPosition,
    double StrikeoutThickness)
{
    /// <summary>Ascent + descent + line gap: the height of a line with single spacing.</summary>
    public double LineHeight => Ascent + Descent + LineGap;
}
