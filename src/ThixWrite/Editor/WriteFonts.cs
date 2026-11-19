using ThixOffice.Text.Skia;
using ThixWrite.Core.Documents;

namespace ThixWrite.Editor;

/// <summary>
/// The text measurer of the process (one for every window: typefaces and shaping fonts are cached once; it is thread-safe)
/// and the fonts of new documents chosen from the installed families.
/// </summary>
public static class WriteFonts
{
    private static readonly Lazy<SkiaTextMeasurer> SharedMeasurer = new(() => new SkiaTextMeasurer());
    private static readonly Lazy<DocumentFonts> DefaultFonts = new(() => DocumentFonts.Choose(Measurer.GetFamilies()));

    /// <summary>The measurer every layout, page picture and PDF of the process uses.</summary>
    public static SkiaTextMeasurer Measurer => SharedMeasurer.Value;

    /// <summary>The body and heading font of new documents (Aptos, Calibri, Helvetica Neue or Arial – what the machine has).</summary>
    public static DocumentFonts Default => DefaultFonts.Value;

    /// <summary>The installed font families, sorted (the ribbon's Schriftart list).</summary>
    public static IReadOnlyList<string> Families => Measurer.GetFamilies();
}
