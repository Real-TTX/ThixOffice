using System.Globalization;
using Avalonia.Media;
using SkiaSharp;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// The colours that are DOCUMENT data, not the app's chrome – the one place of ThixWrite with literal colours (the rules test
/// exempts exactly this file): the paper (white in light and dark mode, like Word), ink of the caret and of the formatting
/// marks on that paper, and the palettes of Schriftfarbe and Hervorhebung (Word's standard colours, the theme colours of
/// the document with their tints). Everything else takes the Mat tokens.
/// </summary>
public static class DocumentPalette
{
    /// <summary>The paper of every page.</summary>
    public static readonly Color Paper = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>The paper over the body while a header or footer is edited (the body shows dimmed through it, as in Word).</summary>
    public static readonly Color DimmedBody = Color.FromArgb(0x99, 0xFF, 0xFF, 0xFF);

    /// <summary>The paper for Skia.</summary>
    public static readonly SKColor PaperSkia = new(0xFF, 0xFF, 0xFF);

    /// <summary>The bars and the grid of the chart picture of the sample report (Word's accent blue, a light grey).</summary>
    public static readonly SKColor[] SampleChart = [new(0x44, 0x72, 0xC4), new(0xD9, 0xD9, 0xD9), new(0x40, 0x40, 0x40)];

    /// <summary>The caret on the paper.</summary>
    public static readonly Color Ink = Color.FromRgb(0x00, 0x00, 0x00);

    /// <summary>The formatting marks (¶, ·, →) – Word's muted blue-grey.</summary>
    public static readonly SKColor Marks = new(0x5B, 0x7A, 0xA6);

    /// <summary>The colour of new text in Schriftfarbe before anything else was chosen (Word: red).</summary>
    public const string DefaultFontColor = "#C00000";

    /// <summary>Word's ten standard colours (the last row of its palette).</summary>
    public static IReadOnlyList<string> StandardColors { get; } =
    [
        "#C00000", "#FF0000", "#FFC000", "#FFFF00", "#92D050", "#00B050", "#00B0F0", "#0070C0", "#002060", "#7030A0",
    ];

    /// <summary>The theme slots of a palette row, as Word orders them: Hintergrund 1, Text 1, Hintergrund 2, Text 2, Akzent 1–6.</summary>
    private static readonly string[] ThemeSlots = ["lt1", "dk1", "lt2", "dk2", "accent1", "accent2", "accent3", "accent4", "accent5", "accent6"];

    /// <summary>
    /// The palette of Schriftfarbe: the document's theme colours, four rows of their tints and shades (Word's 80/60/40 %
    /// lighter, 25 % darker), then the standard colours – ten columns, row by row, <c>#rrggbb</c>.
    /// </summary>
    public static IReadOnlyList<string> FontColors(Theme theme)
    {
        var bases = ThemeSlots.Select(slot => theme.Palette.TryGetValue(slot, out var hex) ? hex : "808080").ToList();
        var colors = new List<string>();
        colors.AddRange(bases.Select(hex => "#" + hex.ToUpperInvariant()));
        foreach (var amount in new[] { 0.8, 0.6, 0.4, -0.25 })
        {
            colors.AddRange(bases.Select(hex => Mix(hex, amount)));
        }

        colors.AddRange(StandardColors);
        return colors;
    }

    /// <summary>The highlight colours Word offers (its 15 marker colours), <c>#rrggbb</c>, in Word's order.</summary>
    public static IReadOnlyList<string> HighlightColors { get; } = HighlightOrder.Select(HexOf).ToList();

    private static IEnumerable<HighlightColor> HighlightOrder =>
    [
        HighlightColor.Yellow, HighlightColor.BrightGreen, HighlightColor.Cyan, HighlightColor.Magenta, HighlightColor.Blue,
        HighlightColor.Red, HighlightColor.DarkBlue, HighlightColor.DarkCyan, HighlightColor.DarkGreen, HighlightColor.DarkMagenta,
        HighlightColor.DarkRed, HighlightColor.DarkYellow, HighlightColor.DarkGray, HighlightColor.LightGray, HighlightColor.Black,
    ];

    /// <summary>The <c>#rrggbb</c> of a highlight colour.</summary>
    public static string HexOf(HighlightColor highlight) => "#" + DisplayListBuilder.ColorOf(highlight).ToHex();

    /// <summary>The highlight colour of a <c>#rrggbb</c> of <see cref="HighlightColors"/> (None for null or an unknown one).</summary>
    public static HighlightColor HighlightOf(string? hex) =>
        HighlightOrder.FirstOrDefault(highlight => string.Equals(HexOf(highlight), hex, StringComparison.OrdinalIgnoreCase), HighlightColor.None);

    /// <summary>Mixes a colour toward white (positive <paramref name="amount"/>) or black (negative).</summary>
    private static string Mix(string hex, double amount)
    {
        var value = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
        int Channel(int shift)
        {
            var channel = (value >> shift) & 0xFF;
            return (int)Math.Round(amount >= 0 ? channel + ((255 - channel) * amount) : channel * (1 + amount));
        }

        return $"#{Channel(16):X2}{Channel(8):X2}{Channel(0):X2}";
    }
}
