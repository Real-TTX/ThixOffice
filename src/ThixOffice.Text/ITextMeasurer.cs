namespace ThixOffice.Text;

/// <summary>
/// Measures text for a layout engine, without any UI framework: font metrics and the shaped advances of a text in a font.
/// Implementations: <see cref="FixedWidthTextMeasurer"/> (deterministic, for tests), <c>SkiaTextMeasurer</c> in
/// ThixOffice.Text.Skia (SkiaSharp + HarfBuzz, the desktop), later one for the browser. Implementations must be
/// deterministic for the same input and safe to call from one thread at a time.
/// </summary>
public interface ITextMeasurer
{
    /// <summary>The vertical metrics of <paramref name="font"/>.</summary>
    FontMetrics GetMetrics(FontSpec font);

    /// <summary>Shapes <paramref name="text"/> (one line's worth or more, never containing line breaks) in <paramref name="font"/>.</summary>
    ShapedText Shape(string text, FontSpec font);

    /// <summary>The font families the user can choose from (the ribbon's font list), sorted.</summary>
    IReadOnlyList<string> GetFamilies();
}
