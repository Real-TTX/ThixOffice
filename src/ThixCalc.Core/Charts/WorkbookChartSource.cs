using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Charts;

namespace ThixCalc.Core.Charts;

/// <summary>
/// A chart's data from the workbook, live: a reference resolves to the cells' current values (formulas as last
/// calculated), each with the text the cell shows (category labels look like the cells), its number format, and whether
/// its row or column is hidden. Unions and names that refer to ranges work; anything else falls back to the file's cache.
/// </summary>
public sealed class WorkbookChartSource(Workbook workbook, CalcLocale locale) : IChartDataSource
{
    /// <summary>More cells than a chart can show are cut (a whole column as a series).</summary>
    private const int MaxPoints = 32_000;

    public IReadOnlyList<ChartValue>? Resolve(string formula)
    {
        var areas = ChartReferences.Resolve(workbook, formula);
        if (areas.Count == 0)
        {
            return null;
        }

        var values = new List<ChartValue>();
        foreach (var (sheet, range) in areas)
        {
            var area = sheet.UsedRange is { } used && (range.IsWholeColumns || range.IsWholeRows)
                ? new CellRange(range.FirstRow, range.FirstColumn, Math.Min(range.LastRow, used.LastRow), Math.Min(range.LastColumn, used.LastColumn))
                : range;
            for (var row = area.FirstRow; row <= area.LastRow; row++)
            {
                for (var column = area.FirstColumn; column <= area.LastColumn; column++)
                {
                    if (values.Count >= MaxPoints)
                    {
                        return values;
                    }

                    values.Add(ValueOf(sheet, row, column));
                }
            }
        }

        return values;
    }

    private ChartValue ValueOf(Worksheet sheet, int row, int column)
    {
        var hidden = sheet.Rows.IsHidden(row) || sheet.Columns.IsHidden(column);
        var value = sheet.GetValue(row, column);
        var code = sheet.GetStyle(row, column).NumberFormat;
        switch (value.Kind)
        {
            case CellValueKind.Number:
                var text = NumberFormatter.Format(value, code, locale, workbook.Date1904).Text;
                return new ChartValue(value.Number, text, code, hidden);
            case CellValueKind.Text:
                return new ChartValue(null, value.Text, null, hidden);
            case CellValueKind.Boolean:
                return new ChartValue(null, NumberFormatter.Format(value, "General", locale).Text, null, hidden);
            default:
                // Blanks and errors (#NV) are gaps.
                return new ChartValue(null, null, null, hidden);
        }
    }
}

/// <summary>ThixCalc's number formats for chart labels (the same engine as the cells, in the user's locale).</summary>
public sealed class CalcChartFormatter(CalcLocale locale, bool date1904) : IChartValueFormatter
{
    public string Format(double value, string? formatCode) =>
        NumberFormatter.Format(CellValue.FromNumber(value), string.IsNullOrEmpty(formatCode) ? "General" : formatCode, locale, date1904).Text;

    public bool IsDateFormat(string? formatCode) => formatCode is { Length: > 0 } && NumberFormat.Parse(formatCode).IsDateTime;
}
