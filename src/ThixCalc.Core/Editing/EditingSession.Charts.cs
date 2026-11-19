using ThixCalc.Core.Charts;
using ThixCalc.Core.Model;
using ThixOffice.Charts;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>Where a chart goes in the stacking order (Format › Anordnen).</summary>
public enum ChartOrder : byte
{
    BringForward,
    BringToFront,
    SendBackward,
    SendToBack,
}

/// <summary>The charts: inserting, selecting, moving and sizing, changing (type, data, elements, colours), ordering, deleting – each one undo step.</summary>
public sealed partial class EditingSession
{
    private int? _selectedChart;

    /// <summary>The chart selected on the active sheet (instead of cells), or null.</summary>
    public SheetChart? SelectedChart => _selectedChart is { } id ? ActiveSheet.Charts.FirstOrDefault(chart => chart.Id == id) : null;

    /// <summary>Selects a chart of the active sheet (null: back to the cells).</summary>
    public void SelectChart(int? id)
    {
        if (_selectedChart == id)
        {
            return;
        }

        _selectedChart = id is { } value && ActiveSheet.Charts.Any(chart => chart.Id == value) ? id : null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// Einfügen › Diagramm: a chart of <paramref name="template"/> from the selection (a single cell: its current region),
    /// series by Excel's rules, at <paramref name="anchor"/>; it is selected afterwards.
    /// </summary>
    public SheetChart InsertChart(ChartTemplate template, ChartAnchor anchor, CellRange? source = null)
    {
        var sheet = ActiveSheet;
        var layout = ChartSources.Analyze(sheet, source ?? Selection, scatter: template.Type == ChartType.Scatter);
        var chart = ChartFactory.Create(template, layout.Series, layout.DateCategories);
        var number = Workbook.Sheets.Sum(each => each.Charts.Count) + 1;
        var entry = new SheetChart(Workbook.NextChartId(), anchor, chart) { Name = $"Diagramm {number}" };
        Execute("Diagramm einfügen", () => ChangeSheetState(sheet, () => sheet.Charts.Add(entry)));
        SelectChart(entry.Id);
        return entry;
    }

    /// <summary>Puts a chart (a copy from the clipboard) on the active sheet at <paramref name="anchor"/> and selects it.</summary>
    public SheetChart PasteChart(Chart chart, ChartAnchor anchor, string? name = null)
    {
        var sheet = ActiveSheet;
        var entry = new SheetChart(Workbook.NextChartId(), anchor, chart) { Name = name ?? $"Diagramm {Workbook.Sheets.Sum(each => each.Charts.Count) + 1}" };
        Execute("Einfügen", () => ChangeSheetState(sheet, () => sheet.Charts.Add(entry)));
        SelectChart(entry.Id);
        return entry;
    }

    /// <summary>Moves or sizes a chart.</summary>
    public void SetChartAnchor(int id, ChartAnchor anchor, string label = "Diagramm verschieben") =>
        ChangeEntry(id, label, entry => entry with { Anchor = anchor });

    /// <summary>Changes a chart (Diagrammentwurf, Format): <paramref name="change"/> gets the chart and returns the new one.</summary>
    public void ChangeChart(int id, string label, Func<Chart, Chart> change)
    {
        var entry = FindOnActiveSheet(id);
        if (!entry.Chart.IsSupported)
        {
            throw new BusinessException("Dieser Diagrammtyp kann in ThixCalc nicht bearbeitet werden. Er bleibt beim Speichern erhalten.");
        }

        ChangeEntry(id, label, current => current with { Chart = change(current.Chart) });
    }

    public void ChangeChartType(int id, ChartTemplate template) =>
        ChangeChart(id, "Diagrammtyp ändern", chart => ChartFactory.WithType(chart, template));

    /// <summary>Zeile/Spalte wechseln: the series run along the other direction of the chart's range.</summary>
    public void SwitchRowColumn(int id)
    {
        var entry = FindOnActiveSheet(id);
        if (ChartSources.RangeOf(Workbook, entry.Chart) is not { } source)
        {
            throw new BusinessException("Die Daten dieses Diagramms bilden keinen zusammenhängenden Bereich.");
        }

        var inRows = entry.Chart.AllSeries.FirstOrDefault()?.Values.Formula is { } formula
            && ChartReferences.Resolve(Workbook, formula) is [var (_, values), ..] && values.RowCount == 1 && values.ColumnCount > 1;
        var scatter = entry.Chart.Plots.Any(plot => plot.Type == ChartType.Scatter);
        var layout = ChartSources.Analyze(source.Sheet, source.Range, seriesInRows: !inRows, scatter);
        ChangeChart(id, "Zeile/Spalte wechseln", chart => ChartEdits.ReplaceSeries(chart, layout.Series, layout.DateCategories));
    }

    /// <summary>Daten auswählen: the chart shows <paramref name="reference"/> ("=Tabelle1!$A$1:$C$13" or "A1:C13" on the active sheet).</summary>
    public void SetChartData(int id, string reference, bool? seriesInRows = null)
    {
        var text = reference.Trim().TrimStart('=');
        IReadOnlyList<(Worksheet Sheet, CellRange Range)> areas = CellRange.TryParse(text.Replace("$", string.Empty, StringComparison.Ordinal)) is { } local
            ? [(ActiveSheet, local)]
            : ChartReferences.Resolve(Workbook, text);
        if (areas.Count != 1)
        {
            throw new BusinessException("Geben Sie einen zusammenhängenden Bereich ein, z. B. =Tabelle1!$A$1:$C$13.", "range");
        }

        var entry = FindOnActiveSheet(id);
        var scatter = entry.Chart.Plots.Any(plot => plot.Type == ChartType.Scatter);
        var layout = ChartSources.Analyze(areas[0].Sheet, areas[0].Range, seriesInRows, scatter);
        ChangeChart(id, "Daten auswählen", chart => ChartEdits.ReplaceSeries(chart, layout.Series, layout.DateCategories));
    }

    public void DeleteChart(int id)
    {
        var sheet = ActiveSheet;
        var entry = FindOnActiveSheet(id);
        Execute("Diagramm löschen", () => ChangeSheetState(sheet, () => sheet.Charts.Remove(entry)));
        _selectedChart = null;
        SelectionChanged?.Invoke(this, EventArgs.Empty);
        StateChanged?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Format › Anordnen: a chart in front of or behind the others.</summary>
    public void OrderChart(int id, ChartOrder order)
    {
        var sheet = ActiveSheet;
        var entry = FindOnActiveSheet(id);
        var index = sheet.Charts.IndexOf(entry);
        var target = order switch
        {
            ChartOrder.BringForward => Math.Min(sheet.Charts.Count - 1, index + 1),
            ChartOrder.BringToFront => sheet.Charts.Count - 1,
            ChartOrder.SendBackward => Math.Max(0, index - 1),
            _ => 0,
        };
        if (target == index)
        {
            return;
        }

        Execute("Anordnen", () => ChangeSheetState(sheet, () =>
        {
            sheet.Charts.RemoveAt(index);
            sheet.Charts.Insert(target, entry);
        }));
    }

    private SheetChart FindOnActiveSheet(int id) =>
        ActiveSheet.Charts.FirstOrDefault(chart => chart.Id == id) ?? throw new BusinessException("Das Diagramm gibt es nicht mehr.");

    private void ChangeEntry(int id, string label, Func<SheetChart, SheetChart> change)
    {
        var sheet = ActiveSheet;
        var entry = FindOnActiveSheet(id);
        var changed = change(entry);
        if (changed == entry)
        {
            return;
        }

        Execute(label, () => ChangeSheetState(sheet, () =>
        {
            var index = sheet.Charts.FindIndex(chart => chart.Id == id);
            if (index >= 0)
            {
                sheet.Charts[index] = changed;
            }
        }));
    }
}
