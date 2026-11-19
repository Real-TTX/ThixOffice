using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Model;

/// <summary>The look of a table style in a workbook's theme: header, bands, lines, total row.</summary>
public sealed record TableStyleLook(
    RgbColor? HeaderFill,
    RgbColor? HeaderText,
    RgbColor? BandFill,
    RgbColor Line,
    bool HeaderLine,
    RgbColor? TotalFill = null);

/// <summary>What a table makes of one cell: a fill, bold text, a text colour (each null = the cell's own).</summary>
public readonly record struct TableCellLook(RgbColor? Fill, bool Bold, RgbColor? Text);

/// <summary>
/// The table styles ThixCalc draws (the Office ones by name, in the theme's accents): Hell 1 (lines and grey bands), Hell 9
/// (a coloured header), Mittel 2–7 (a coloured header with white text and light bands – Mittel 2 is Excel's default).
/// Other names in files draw as Mittel 2 of the nearest accent and stay in the file as written.
/// </summary>
public static class TableStyles
{
    public const string DefaultStyle = "TableStyleMedium2";

    /// <summary>The gallery of Tabellenformatvorlagen / Als Tabelle formatieren: (style name, German label).</summary>
    public static IReadOnlyList<(string Id, string Label)> Gallery { get; } =
    [
        ("TableStyleLight1", "Hell 1"), ("TableStyleLight9", "Hell 9"),
        ("TableStyleMedium2", "Mittel 2"), ("TableStyleMedium3", "Mittel 3"), ("TableStyleMedium4", "Mittel 4"),
        ("TableStyleMedium5", "Mittel 5"), ("TableStyleMedium6", "Mittel 6"), ("TableStyleMedium7", "Mittel 7"),
    ];

    /// <summary>The look of <paramref name="style"/> in <paramref name="theme"/>, or null for no style.</summary>
    public static TableStyleLook? Resolve(string? style, ThemeColors theme)
    {
        if (string.IsNullOrEmpty(style))
        {
            return null;
        }

        // Office numbers its styles in runs of seven: black/grey first, then accent 1 to 6.
        var number = int.TryParse(new string(style.Where(char.IsAsciiDigit).ToArray()), out var value) ? Math.Max(1, value) : 2;
        var position = (number - 1) % 7;
        var color = position == 0 ? RgbColor.FromHex("404040") : theme[3 + position];
        var run = (number - 1) / 7;
        if (style.StartsWith("TableStyleLight", StringComparison.OrdinalIgnoreCase))
        {
            return run == 0
                ? new TableStyleLook(null, null, ThemeColors.ApplyTint(position == 0 ? RgbColor.FromHex("808080") : color, 0.75), color, HeaderLine: true)
                : new TableStyleLook(color, RgbColor.White, null, color, HeaderLine: false);
        }

        if (style.StartsWith("TableStyleDark", StringComparison.OrdinalIgnoreCase))
        {
            var dark = ThemeColors.ApplyTint(color, -0.5);
            return new TableStyleLook(RgbColor.Black, RgbColor.White, ThemeColors.ApplyTint(color, 0.6), dark, HeaderLine: false, dark);
        }

        return new TableStyleLook(color, RgbColor.White, ThemeColors.ApplyTint(color, 0.8), color, HeaderLine: false);
    }

    /// <summary>What the tables of the sheet make of a cell (none: default).</summary>
    public static TableCellLook? LookAt(Worksheet sheet, int row, int column)
    {
        foreach (var table in sheet.Tables)
        {
            if (!table.Range.Contains(row, column))
            {
                continue;
            }

            if (Resolve(table.Style, sheet.Workbook.Theme) is not { } look)
            {
                return null;
            }

            var offset = column - table.Range.FirstColumn;
            var bold = (table.FirstColumn && offset == 0) || (table.LastColumn && column == table.Range.LastColumn);
            if (table.HasHeader && row == table.Range.FirstRow)
            {
                return new TableCellLook(look.HeaderFill, true, look.HeaderText);
            }

            if (table.HasTotals && row == table.Range.LastRow)
            {
                return new TableCellLook(look.TotalFill, true, look.TotalFill is null ? null : RgbColor.White);
            }

            var dataRow = row - table.DataRange.FirstRow;
            var banded = (table.BandedRows && dataRow % 2 == 0) || (table.BandedColumns && offset % 2 == 0);
            return new TableCellLook(banded ? look.BandFill : null, bold, null);
        }

        return null;
    }
}
