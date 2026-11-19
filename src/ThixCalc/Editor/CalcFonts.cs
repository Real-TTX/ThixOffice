using ThixOffice.Desktop.Rendering;
using ThixOffice.Text;
using ThixOffice.Text.Skia;

namespace ThixCalc.Editor;

/// <summary>ThixCalc's names for the process's shared measurers (<see cref="OfficeFonts"/>).</summary>
public static class CalcFonts
{
    /// <summary>The Skia measurer (the renderer takes its typefaces; PDF and printing measure with it).</summary>
    public static SkiaTextMeasurer Skia => OfficeFonts.Skia;

    /// <summary>The measurer of the grids on the UI thread (shaped texts cached).</summary>
    public static ITextMeasurer Grid => OfficeFonts.Cached;

    /// <summary>The installed font families, sorted (the ribbon's Schriftart list).</summary>
    public static IReadOnlyList<string> Families => OfficeFonts.Families;
}
