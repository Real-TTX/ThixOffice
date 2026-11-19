namespace ThixOffice.Text;

/// <summary>
/// A text shaped in one font: the advance of every UTF-16 code unit and the glyphs a renderer draws.
/// <para>
/// <see cref="Advances"/> is grapheme-cluster safe: only the first code unit of a grapheme cluster has an advance, the others
/// have 0, so the sum of the advances before any grapheme boundary is the x of the caret there. A glyph cluster that covers
/// several graphemes (a ligature like "fi") shares its advance evenly among its graphemes.
/// </para>
/// </summary>
public sealed class ShapedText
{
    public ShapedText(string text, FontSpec font, double[] advances, IReadOnlyList<ShapedGlyphRun> glyphRuns)
    {
        if (advances.Length != text.Length)
        {
            throw new ArgumentException("One advance per UTF-16 code unit is required.", nameof(advances));
        }

        Text = text;
        Font = font;
        Advances = advances;
        GlyphRuns = glyphRuns;
        Width = advances.Sum();
    }

    public string Text { get; }

    public FontSpec Font { get; }

    /// <summary>The advance of every code unit (points); see the type remarks.</summary>
    public double[] Advances { get; }

    /// <summary>The glyphs of the text (several runs when fallback fonts were needed; empty for measurers without glyphs).</summary>
    public IReadOnlyList<ShapedGlyphRun> GlyphRuns { get; }

    public double Width { get; }

    /// <summary>The width of the code units [<paramref name="start"/>, <paramref name="start"/> + <paramref name="length"/>).</summary>
    public double WidthOf(int start, int length)
    {
        var width = 0.0;
        for (var index = start; index < start + length; index++)
        {
            width += Advances[index];
        }

        return width;
    }
}

/// <summary>
/// Glyphs of a part of a shaped text in one actual typeface. <see cref="Positions"/> are x offsets (points) from the start of
/// the shaped text; <see cref="Clusters"/> is the index of the first code unit (in the shaped text) each glyph belongs to.
/// </summary>
public sealed record ShapedGlyphRun(
    FontFace Face,
    int Start,
    int Length,
    ushort[] Glyphs,
    float[] Positions,
    int[] Clusters);

/// <summary>The typeface a renderer must use for a glyph run (the family actually found, maybe a fallback).</summary>
public readonly record struct FontFace(string Family, bool Bold, bool Italic);
