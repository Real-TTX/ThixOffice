using ThixOffice.Text;
using ThixOffice.Text.Skia;

namespace ThixCalc.Editor;

/// <summary>
/// The text measurer of the process (typefaces and shaping fonts cached once; thread-safe) and, for the UI thread, a cache of
/// shaped texts: a grid shows the same short texts frame after frame, so shaping each only once keeps scrolling smooth.
/// </summary>
public static class CalcFonts
{
    private static readonly Lazy<SkiaTextMeasurer> SharedMeasurer = new(() => new SkiaTextMeasurer());
    private static readonly Lazy<CachingTextMeasurer> SharedCache = new(() => new CachingTextMeasurer(SharedMeasurer.Value));

    /// <summary>The Skia measurer (the renderer takes its typefaces; PDF and printing measure with it).</summary>
    public static SkiaTextMeasurer Skia => SharedMeasurer.Value;

    /// <summary>The measurer of the grids on the UI thread (shaped texts cached).</summary>
    public static ITextMeasurer Grid => SharedCache.Value;

    /// <summary>The installed font families, sorted (the ribbon's Schriftart list).</summary>
    public static IReadOnlyList<string> Families => Skia.GetFamilies();
}
