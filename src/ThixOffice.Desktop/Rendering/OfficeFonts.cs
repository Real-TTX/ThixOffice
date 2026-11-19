using ThixOffice.Text;
using ThixOffice.Text.Skia;

namespace ThixOffice.Desktop.Rendering;

/// <summary>
/// The text measurer of the process, shared by every app: one <see cref="SkiaTextMeasurer"/> (typefaces and shaping fonts cached
/// once; thread-safe – layouts, pictures, PDF and printing measure with it, the renderer takes its typefaces) and, for the UI
/// thread, a cache of shaped texts in front of it (a grid or a slide shows the same short texts frame after frame).
/// </summary>
public static class OfficeFonts
{
    private static readonly Lazy<SkiaTextMeasurer> SharedMeasurer = new(() => new SkiaTextMeasurer());
    private static readonly Lazy<CachingTextMeasurer> SharedCache = new(() => new CachingTextMeasurer(SharedMeasurer.Value));

    /// <summary>The Skia measurer of the process.</summary>
    public static SkiaTextMeasurer Skia => SharedMeasurer.Value;

    /// <summary>The measurer of views on the UI thread (shaped texts cached; not thread-safe).</summary>
    public static ITextMeasurer Cached => SharedCache.Value;

    /// <summary>The installed font families, sorted (the ribbon's Schriftart list).</summary>
    public static IReadOnlyList<string> Families => Skia.GetFamilies();
}
