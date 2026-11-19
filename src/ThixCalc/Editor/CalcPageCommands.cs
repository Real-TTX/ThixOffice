namespace ThixCalc.Editor;

/// <summary>The choices of Seitenlayout (margins, paper sizes, fit to pages) as German Excel names them.</summary>
public static partial class CalcPageCommands
{
    /// <summary>The margin presets (cm: top, bottom, left, right, header, footer), as Excel names them.</summary>
    public static IReadOnlyList<(string Id, string Label, double Top, double Bottom, double Left, double Right, double Header, double Footer)> MarginPresets { get; } =
    [
        ("normal", "Normal", 1.91, 1.91, 1.78, 1.78, 0.76, 0.76),
        ("wide", "Breit", 2.54, 2.54, 2.54, 2.54, 1.27, 1.27),
        ("narrow", "Schmal", 1.91, 1.91, 0.64, 0.64, 0.76, 0.76),
    ];

    /// <summary>The paper sizes (mm, portrait).</summary>
    public static IReadOnlyList<(string Id, string Label, double Width, double Height)> PaperSizes { get; } =
    [
        ("a4", "A4 (21 × 29,7 cm)", 210, 297),
        ("a5", "A5 (14,8 × 21 cm)", 148, 210),
        ("a3", "A3 (29,7 × 42 cm)", 297, 420),
        ("letter", "Letter (21,59 × 27,94 cm)", 215.9, 279.4),
        ("legal", "Legal (21,59 × 35,56 cm)", 215.9, 355.6),
    ];

    /// <summary>Skalierung: no scaling, the sheet on one page, all columns on one page, all rows on one page.</summary>
    public static IReadOnlyList<(string Id, string Label, int Wide, int Tall)> FitChoices { get; } =
    [
        ("none", "Keine Skalierung", 0, 0),
        ("page", "Blatt auf einer Seite darstellen", 1, 1),
        ("columns", "Alle Spalten auf einer Seite darstellen", 1, 0),
        ("rows", "Alle Zeilen auf einer Seite darstellen", 0, 1),
    ];
}
