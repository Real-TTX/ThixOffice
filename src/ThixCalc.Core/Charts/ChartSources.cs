using ThixCalc.Core.Editing;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using ThixOffice.Charts;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Charts;

/// <summary>How a range became series: the series, whether the categories are dates, the orientation, the range used.</summary>
public sealed record ChartSourceLayout(IReadOnlyList<SeriesSpec> Series, bool DateCategories, bool SeriesInRows, CellRange Range);

/// <summary>
/// Turns a range of the sheet into chart series the way Excel does: a first row of texts names the series, a first column
/// of texts (or dates) holds the categories (an empty top-left cell marks both), series run down the columns when there are
/// more rows than columns (else along the rows), and a scatter takes its x values from the first column.
/// </summary>
public static class ChartSources
{
    public static ChartSourceLayout Analyze(Worksheet sheet, CellRange range, bool? seriesInRows = null, bool scatter = false)
    {
        if (range.IsSingleCell)
        {
            range = Navigation.CurrentRegion(sheet, range.First);
        }

        if (sheet.UsedRange is not { } used || !used.Intersects(range))
        {
            throw new BusinessException("Wählen Sie zuerst die Daten aus, die das Diagramm zeigen soll.");
        }

        range = new CellRange(
            Math.Max(range.FirstRow, used.FirstRow), Math.Max(range.FirstColumn, used.FirstColumn),
            Math.Min(range.LastRow, used.LastRow), Math.Min(range.LastColumn, used.LastColumn));
        if (range.CellCount > 2_000_000)
        {
            throw new BusinessException("Der Bereich ist für ein Diagramm zu groß.");
        }

        var topLeftBlank = IsBlank(sheet, range.FirstRow, range.FirstColumn);
        var headerRow = range.RowCount >= 2 && (topLeftBlank || RowIsLabels(sheet, range));
        var headerColumn = range.ColumnCount >= 2 && (topLeftBlank || ColumnIsLabels(sheet, range, headerRow));
        var firstDataRow = range.FirstRow + (headerRow ? 1 : 0);
        var firstDataColumn = range.FirstColumn + (headerColumn ? 1 : 0);
        var dataRows = range.LastRow - firstDataRow + 1;
        var dataColumns = range.LastColumn - firstDataColumn + 1;
        if (dataRows <= 0 || dataColumns <= 0)
        {
            throw new BusinessException("Der Bereich enthält keine Werte für ein Diagramm.");
        }

        var inRows = seriesInRows ?? dataColumns > dataRows;
        var specs = new List<SeriesSpec>();
        var dates = false;
        if (!inRows)
        {
            CellRange? categories = headerColumn ? new CellRange(firstDataRow, range.FirstColumn, range.LastRow, range.FirstColumn) : null;
            var start = firstDataColumn;
            if (scatter && categories is null && dataColumns >= 2)
            {
                categories = new CellRange(firstDataRow, firstDataColumn, range.LastRow, firstDataColumn);
                start++;
            }

            dates = categories is { } dateRange && AllDates(sheet, dateRange);
            for (var column = start; column <= range.LastColumn; column++)
            {
                var name = headerRow ? Name(sheet, CellRange.Cell(range.FirstRow, column)) : null;
                specs.Add(new SeriesSpec(name, categories is { } c ? Data(sheet, c) : null, Data(sheet, new CellRange(firstDataRow, column, range.LastRow, column))));
            }
        }
        else
        {
            CellRange? categories = headerRow ? new CellRange(range.FirstRow, firstDataColumn, range.FirstRow, range.LastColumn) : null;
            var start = firstDataRow;
            if (scatter && categories is null && dataRows >= 2)
            {
                categories = new CellRange(firstDataRow, firstDataColumn, firstDataRow, range.LastColumn);
                start++;
            }

            dates = categories is { } dateRange && AllDates(sheet, dateRange);
            for (var row = start; row <= range.LastRow; row++)
            {
                var name = headerColumn ? Name(sheet, CellRange.Cell(row, range.FirstColumn)) : null;
                specs.Add(new SeriesSpec(name, categories is { } c ? Data(sheet, c) : null, Data(sheet, new CellRange(row, firstDataColumn, row, range.LastColumn))));
            }
        }

        if (specs.Count == 0)
        {
            throw new BusinessException("Der Bereich enthält keine Werte für ein Diagramm.");
        }

        return new ChartSourceLayout(specs, dates, inRows, range);
    }

    /// <summary>The range a chart's series cover together (Daten auswählen shows it), or null when they are not one block on one sheet.</summary>
    public static (Worksheet Sheet, CellRange Range)? RangeOf(Workbook workbook, Chart chart)
    {
        Worksheet? sheet = null;
        CellRange? union = null;
        foreach (var formula in ChartEdits.References(chart))
        {
            foreach (var (areaSheet, area) in ChartReferences.Resolve(workbook, formula))
            {
                if (sheet is not null && areaSheet != sheet)
                {
                    return null;
                }

                sheet = areaSheet;
                union = union is { } existing ? existing.Union(area) : area;
            }
        }

        return sheet is not null && union is { } range ? (sheet, range) : null;
    }

    private static ChartText Name(Worksheet sheet, CellRange cell)
    {
        var value = sheet.GetValue(cell.First);
        return ChartText.Ref(ChartReferences.Of(sheet, cell), value.Kind == CellValueKind.Text ? value.Text : null);
    }

    private static ChartDataRef Data(Worksheet sheet, CellRange range) => new(ChartReferences.Of(sheet, range));

    private static bool IsBlank(Worksheet sheet, int row, int column) => sheet.GetValue(row, column).IsBlank;

    private static bool IsLabel(Worksheet sheet, int row, int column) => sheet.GetValue(row, column).Kind == CellValueKind.Text;

    private static bool IsDate(Worksheet sheet, int row, int column) =>
        sheet.GetValue(row, column).IsNumber && NumberFormat.Parse(sheet.GetStyle(row, column).NumberFormat).IsDateTime;

    /// <summary>The first row holds texts (names) and no numbers after its first cell.</summary>
    private static bool RowIsLabels(Worksheet sheet, CellRange range)
    {
        var any = false;
        for (var column = range.FirstColumn + 1; column <= range.LastColumn; column++)
        {
            if (sheet.GetValue(range.FirstRow, column).IsNumber && !IsDate(sheet, range.FirstRow, column))
            {
                return false;
            }

            any |= IsLabel(sheet, range.FirstRow, column) || IsDate(sheet, range.FirstRow, column);
        }

        return any || range.ColumnCount == 1 && IsLabel(sheet, range.FirstRow, range.FirstColumn);
    }

    /// <summary>The first column holds texts or dates (categories).</summary>
    private static bool ColumnIsLabels(Worksheet sheet, CellRange range, bool headerRow)
    {
        var any = false;
        for (var row = range.FirstRow + (headerRow ? 1 : 0); row <= range.LastRow; row++)
        {
            if (sheet.GetValue(row, range.FirstColumn).IsNumber && !IsDate(sheet, row, range.FirstColumn))
            {
                return false;
            }

            any |= IsLabel(sheet, row, range.FirstColumn) || IsDate(sheet, row, range.FirstColumn);
        }

        return any;
    }

    private static bool AllDates(Worksheet sheet, CellRange range)
    {
        var any = false;
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                if (IsBlank(sheet, row, column))
                {
                    continue;
                }

                if (!IsDate(sheet, row, column))
                {
                    return false;
                }

                any = true;
            }
        }

        return any;
    }
}
