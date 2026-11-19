using System.Globalization;
using Avalonia.Media;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixOffice.Desktop;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Editor;

/// <summary>
/// The colours that are DOCUMENT data, not the app's chrome – the one place of ThixCalc with literal colours (the rules test
/// exempts exactly this file): the cells (white in light and dark mode, like Excel's sheet), the grid lines, the default ink,
/// the colours of references while a formula is edited (Excel's order), the palettes of Schriftfarbe / Füllfarbe / Rahmen
/// (the workbook's theme colours with their tints, then Excel's standard colours), the cell styles of the gallery, and the
/// colours of conditional formatting presets. The headers and the selection take the Mat tokens of the theme (chrome).
/// </summary>
public static class GridPalette
{
    /// <summary>The sheet's background (every cell without a fill).</summary>
    public static readonly RgbColor Sheet = RgbColor.White;

    /// <summary>Excel's grid lines.</summary>
    public static readonly RgbColor GridLine = RgbColor.FromHex("E1E1E1");

    /// <summary>Text without a colour of its own.</summary>
    public static readonly RgbColor Ink = RgbColor.Black;

    /// <summary>The sheet for Avalonia (the in-cell editor's background).</summary>
    public static readonly Color SheetColor = Color.FromRgb(0xFF, 0xFF, 0xFF);

    /// <summary>The sheet for Skia (the self test's frame).</summary>
    public static readonly SkiaSharp.SKColor SheetSkia = new(0xFF, 0xFF, 0xFF);

    /// <summary>The in-cell editor's text.</summary>
    public static readonly Color InkColor = Color.FromRgb(0x00, 0x00, 0x00);

    /// <summary>The fill over selected cells (translucent, so fills and text show through).</summary>
    public static readonly RgbColor SelectionFill = new(0, 0, 0, 24);

    /// <summary>The frame of an AutoFill drag.</summary>
    public static readonly RgbColor FillPreview = RgbColor.FromHex("6E6E6E");

    /// <summary>The colours of the sample workbook (snapshots, screenshots): the accent, white header text, a light green group fill, the data bar, a light red alert with dark red text, a blue tab.</summary>
    public static readonly CalcColor SampleAccent = CalcColor.FromHex("107C41");

    public static readonly CalcColor SampleHeaderText = CalcColor.FromHex("FFFFFF");

    public static readonly CalcColor SampleGroupFill = CalcColor.FromHex("E2EFDA");

    public static readonly CalcColor SampleBar = CalcColor.FromHex("63BE7B");

    public static readonly CalcColor SampleAlertFill = CalcColor.FromHex("FFC7CE");

    public static readonly CalcColor SampleAlertText = CalcColor.FromHex("9C0006");

    public static readonly CalcColor SampleTab = CalcColor.FromHex("4472C4");

    /// <summary>The colours of the references of an edited formula, in Excel's order (repeating).</summary>
    public static IReadOnlyList<RgbColor> ReferenceColors { get; } =
    [
        RgbColor.FromHex("4472C4"), RgbColor.FromHex("C00000"), RgbColor.FromHex("7030A0"), RgbColor.FromHex("00B050"),
        RgbColor.FromHex("ED7D31"), RgbColor.FromHex("0070C0"), RgbColor.FromHex("BF8F00"), RgbColor.FromHex("FF0066"),
    ];

    /// <summary>A reference colour as an Avalonia colour (the editor's tokens).</summary>
    public static Color ToColor(RgbColor color) => Color.FromArgb(color.A, color.R, color.G, color.B);

    /// <summary>The default of Schriftfarbe's button (Excel: red) and of Füllfarbe's (Excel: yellow).</summary>
    public const string DefaultFontColor = "#FF0000";

    public const string DefaultFillColor = "#FFFF00";

    /// <summary>Excel's ten standard colours.</summary>
    public static IReadOnlyList<string> StandardColors { get; } =
    [
        "#C00000", "#FF0000", "#FFC000", "#FFFF00", "#92D050", "#00B050", "#00B0F0", "#0070C0", "#002060", "#7030A0",
    ];

    /// <summary>The theme slots of a palette row in Excel's order: Hintergrund 1, Text 1, Hintergrund 2, Text 2, Akzent 1–6.</summary>
    private static readonly int[] ThemeSlots = [0, 1, 2, 3, 4, 5, 6, 7, 8, 9];

    /// <summary>The colour palette of the ribbon's colour pickers: the theme row, four rows of tints/shades, the standard colours.</summary>
    public static IReadOnlyList<string> Colors(ThemeColors theme)
    {
        var colors = new List<string>();
        colors.AddRange(ThemeSlots.Select(slot => Hex(theme[slot])));
        foreach (var tint in new[] { 0.8, 0.6, 0.4, -0.25 })
        {
            colors.AddRange(ThemeSlots.Select(slot => Hex(ThemeColors.ApplyTint(theme[slot], slot == 1 && tint > 0 ? tint - 0.3 : tint))));
        }

        colors.AddRange(StandardColors);
        return colors;
    }

    /// <summary>A "#rrggbb" as a brush (swatches of menus).</summary>
    public static IBrush Brush(string hex) => new SolidColorBrush(Color.Parse(hex));

    /// <summary>The German name of a standard colour (the menus' entries).</summary>
    public static string ColorName(string hex) => StandardColors.ToList().IndexOf(hex.ToUpperInvariant()) switch
    {
        0 => "Dunkelrot",
        1 => "Rot",
        2 => "Orange",
        3 => "Gelb",
        4 => "Hellgrün",
        5 => "Grün",
        6 => "Hellblau",
        7 => "Blau",
        8 => "Dunkelblau",
        9 => "Lila",
        _ => hex,
    };

    public static string Hex(RgbColor color) => $"#{color.R:X2}{color.G:X2}{color.B:X2}";

    /// <summary>A "#rrggbb" of a palette as the colour of a cell.</summary>
    public static CalcColor? ToCalc(string? hex) => hex is null ? null : CalcColor.FromHex(hex);

    /// <summary>The colours of the grid drawing: the sheet's and the headers' (from the Mat tokens of the current theme).</summary>
    public static GridRenderOptions RenderOptions(IReadOnlyList<CellRange> selection) => new()
    {
        Background = Sheet,
        GridLine = GridLine,
        Text = Ink,
        HeaderBackground = Token("Mat.Surface2"),
        HeaderText = Token("Mat.TextMuted"),
        HeaderLine = Token("Mat.Border"),
        HeaderHighlight = Token("Mat.AccentSoft"),
        HeaderSelected = Token("Mat.Accent"),
        HeaderSelectedText = Token("Mat.AccentText"),
        HeaderMarker = Token("Mat.Accent"),
        FreezeLine = Token("Mat.BorderStrong"),
        Selection = selection,
    };

    /// <summary>The selection's look (border in the accent, translucent fill).</summary>
    public static SelectionLook SelectionLook(IReadOnlyList<CellRange> ranges, CellAddress active) => new()
    {
        Ranges = ranges,
        ActiveCell = active,
        Border = Token("Mat.Accent"),
        Fill = SelectionFill,
        HandleOutline = Sheet,
        Preview = FillPreview,
    };

    /// <summary>A Mat token's colour as drawing data (the theme decides: light or dark).</summary>
    public static RgbColor Token(string key)
    {
        if (Ui.Resource(key) is ISolidColorBrush brush)
        {
            var color = brush.Color;
            var alpha = (byte)Math.Round(color.A * brush.Opacity);
            return new RgbColor(color.R, color.G, color.B, alpha);
        }

        return Ink;
    }

    /// <summary>The cell styles of Start › Zellenformatvorlagen (Excel's "Gut, Schlecht und Neutral" and headings).</summary>
    public static IReadOnlyList<(string Id, string Label, string? Fill, string? Font, bool Bold, double? Size, string? Border)> CellStyles { get; } =
    [
        ("normal", "Standard", null, null, false, null, null),
        ("good", "Gut", "#C6EFCE", "#006100", false, null, null),
        ("bad", "Schlecht", "#FFC7CE", "#9C0006", false, null, null),
        ("neutral", "Neutral", "#FFEB9C", "#9C5700", false, null, null),
        ("heading1", "Überschrift 1", null, "#44546A", true, 15, "#4472C4"),
        ("heading2", "Überschrift 2", null, "#44546A", true, 13, "#A9C4EB"),
        ("title", "Titel", null, "#44546A", false, 18, null),
        ("total", "Ergebnis", null, null, true, null, "#4472C4"),
        ("input", "Eingabe", "#FFCC99", "#3F3F76", false, null, "#7F7F7F"),
        ("calculation", "Berechnung", "#F2F2F2", "#FA7D00", true, null, "#7F7F7F"),
        ("note", "Notiz", "#FFFFCC", null, false, null, "#B2B2B2"),
        ("accent1", "Akzent1", "#4472C4", "#FFFFFF", false, null, null),
    ];

    /// <summary>The fills of the conditional formatting presets (Excel's "Hellrote Füllung 2 mit dunkelrotem Text" …).</summary>
    public static IReadOnlyList<(string Id, string Label, string Fill, string Font)> HighlightPresets { get; } =
    [
        ("lightRed", "Hellrote Füllung mit dunkelrotem Text", "#FFC7CE", "#9C0006"),
        ("yellow", "Gelbe Füllung mit dunkelgelbem Text", "#FFEB9C", "#9C5700"),
        ("green", "Grüne Füllung mit dunkelgrünem Text", "#C6EFCE", "#006100"),
    ];

    /// <summary>The data bar colours (Excel's blue, green, red, orange, light blue, purple).</summary>
    public static IReadOnlyList<(string Id, string Label, string Color)> DataBars { get; } =
    [
        ("blue", "Blau", "#638EC6"), ("green", "Grün", "#63BE7B"), ("red", "Rot", "#FF555A"),
        ("orange", "Orange", "#FFB628"), ("lightBlue", "Hellblau", "#008AEF"), ("purple", "Lila", "#D6007B"),
    ];

    /// <summary>The colour scales (low, middle, high).</summary>
    public static IReadOnlyList<(string Id, string Label, string Low, string? Middle, string High)> ColorScales { get; } =
    [
        ("greenYellowRed", "Grün – Gelb – Rot", "#F8696B", "#FFEB84", "#63BE7B"),
        ("redYellowGreen", "Rot – Gelb – Grün", "#63BE7B", "#FFEB84", "#F8696B"),
        ("greenWhite", "Grün – Weiß", "#FFFFFF", null, "#63BE7B"),
        ("whiteRed", "Weiß – Rot", "#FFFFFF", null, "#F8696B"),
    ];

    /// <summary>A hex colour as text for tests and logs.</summary>
    public static string Describe(RgbColor color) => string.Create(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
}
