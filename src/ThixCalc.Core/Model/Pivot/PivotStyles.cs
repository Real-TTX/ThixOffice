using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Model.Pivot;

/// <summary>
/// The PivotTable styles ThixCalc draws, by Office's names in the theme's accents: Hell 16 (Excel's default: a light header, bold
/// totals with a line above), the Hell/Mittel runs (accent per position of seven like the table styles; Mittel with a coloured
/// header and white text), banded rows/columns when the table asks for them. Other names draw as the nearest and stay in the file.
/// </summary>
public static class PivotStyles
{
    public const string DefaultStyle = "PivotStyleLight16";

    /// <summary>The Formatvorlagen gallery of the tab Entwurf: (style, German label).</summary>
    public static IReadOnlyList<(string Id, string Label)> Gallery { get; } =
    [
        ("PivotStyleLight1", "Hell 1"), ("PivotStyleLight8", "Hell 8"), ("PivotStyleLight15", "Hell 15"), ("PivotStyleLight16", "Hell 16"),
        ("PivotStyleLight17", "Hell 17"), ("PivotStyleMedium2", "Mittel 2"), ("PivotStyleMedium3", "Mittel 3"), ("PivotStyleMedium9", "Mittel 9"),
    ];

    /// <summary>Header fill, header text, band fill, total line colour of a style.</summary>
    public static (RgbColor? HeaderFill, RgbColor? HeaderText, RgbColor? Band, RgbColor Line) Resolve(string? style, ThemeColors theme)
    {
        var name = string.IsNullOrEmpty(style) ? DefaultStyle : style;
        var number = int.TryParse(new string(name.Where(char.IsAsciiDigit).ToArray()), out var value) ? Math.Max(1, value) : 16;
        var position = (number - 1) % 7;
        var color = position == 0 ? RgbColor.FromHex("404040") : theme[3 + position];
        if (name.StartsWith("PivotStyleMedium", StringComparison.OrdinalIgnoreCase) || name.StartsWith("PivotStyleDark", StringComparison.OrdinalIgnoreCase))
        {
            return (color, RgbColor.White, ThemeColors.ApplyTint(color, 0.8), color);
        }

        return (ThemeColors.ApplyTint(position == 0 ? RgbColor.FromHex("808080") : color, 0.8), null, ThemeColors.ApplyTint(position == 0 ? RgbColor.FromHex("808080") : color, 0.9), color);
    }

    /// <summary>What the sheet's PivotTables make of a cell (null: not in one): fill, bold, text colour.</summary>
    public static TableCellLook? LookAt(Worksheet sheet, int row, int column)
    {
        foreach (var pivot in sheet.PivotTables)
        {
            if (pivot.Range is not { } range || !range.Contains(row, column) || sheet.OutputOf(pivot) is not { } output)
            {
                continue;
            }

            var (headerFill, headerText, band, _) = Resolve(pivot.Style, sheet.Workbook.Theme);
            if (!output.Cells.TryGetValue(new CellAddress(row, column), out var cell))
            {
                return InHeader(output, row) ? new TableCellLook(headerFill, false, headerText) : null;
            }

            return cell.Kind switch
            {
                PivotCellKind.FilterLabel or PivotCellKind.FilterValue => new TableCellLook(null, cell.Kind == PivotCellKind.FilterLabel, null),
                PivotCellKind.DataCaption or PivotCellKind.RowHeader or PivotCellKind.ColumnHeader or PivotCellKind.ColumnItem => new TableCellLook(headerFill, true, headerText),
                PivotCellKind.GrandTotalLabel or PivotCellKind.GrandTotalValue => new TableCellLook(cell.TotalRow ? headerFill : null, true, cell.TotalRow ? headerText : null),
                PivotCellKind.SubtotalLabel or PivotCellKind.SubtotalValue => new TableCellLook(null, true, null),
                _ => pivot.BandedRows && (row - output.Body.Row - output.HeaderRows) % 2 == 1 ? new TableCellLook(band, false, null)
                    : pivot.BandedColumns && (column - output.Body.Column - output.LabelColumns) % 2 == 1 ? new TableCellLook(band, false, null)
                    : null,
            };
        }

        return null;
    }

    /// <summary>The indent (in levels) of a compact row label, or 0.</summary>
    public static int IndentAt(Worksheet sheet, int row, int column)
    {
        foreach (var pivot in sheet.PivotTables)
        {
            if (pivot.Range is { } range && range.Contains(row, column) && sheet.OutputOf(pivot) is { } output
                && output.Cells.TryGetValue(new CellAddress(row, column), out var cell))
            {
                return cell.Indent;
            }
        }

        return 0;
    }

    /// <summary>The line above the grand total row (the body's last row when the table has one).</summary>
    public static IEnumerable<(int Row, int FirstColumn, int LastColumn, RgbColor Color)> TotalLines(Worksheet sheet)
    {
        foreach (var pivot in sheet.PivotTables)
        {
            if (pivot.Range is not { } range || sheet.OutputOf(pivot) is not { } output)
            {
                continue;
            }

            var color = Resolve(pivot.Style, sheet.Workbook.Theme).Line;
            if (output.Cells.TryGetValue(new CellAddress(range.LastRow, output.Body.Column), out var last) && last.Kind == PivotCellKind.GrandTotalLabel)
            {
                yield return (range.LastRow, output.Body.Column, range.LastColumn, color);
            }

            yield return (output.Body.Row + output.HeaderRows, output.Body.Column, range.LastColumn, color);
        }
    }

    private static bool InHeader(PivotOutput output, int row) => row >= output.Body.Row && row < output.Body.Row + output.HeaderRows;
}
