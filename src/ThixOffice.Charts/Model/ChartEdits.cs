namespace ThixOffice.Charts;

/// <summary>Which axis an element command means: the horizontal (category / x) or the vertical (value / y) one, primary or secondary.</summary>
public enum AxisRole : byte
{
    PrimaryHorizontal,
    PrimaryVertical,
    SecondaryVertical,
}

/// <summary>
/// The changes of Diagrammentwurf › Diagrammelemente and Farben ändern as pure functions: each returns a new chart. An edited
/// part drops the XML it was read with (its SourceXml), so the writer writes the new state instead of the old one.
/// </summary>
public static class ChartEdits
{
    public static Chart SetTitle(Chart chart, bool visible, string? text = null)
    {
        if (!visible)
        {
            return chart with { Title = null, AutoTitleDeleted = true };
        }

        var title = text is null ? chart.Title ?? new ChartTitle() : new ChartTitle(ChartText.Of(text));
        return chart with { Title = title with { SourceXml = text is null ? title.SourceXml : null }, AutoTitleDeleted = false };
    }

    public static Chart SetLegend(Chart chart, LegendPosition? position) =>
        chart with { Legend = position is { } value ? new ChartLegend(value, false, chart.Legend?.FontSize) : null };

    /// <summary>The axis an element command means, or null when the chart has none (pie).</summary>
    public static ChartAxis? AxisOf(Chart chart, AxisRole role)
    {
        var plot = chart.Plots.FirstOrDefault(entry => !entry.IsRound);
        if (plot is null)
        {
            return null;
        }

        var (category, value) = chart.AxesOf(plot);
        if (role == AxisRole.SecondaryVertical)
        {
            var secondary = chart.Plots.FirstOrDefault(entry => entry.AxisIds.Count > 1 && value is not null && entry.AxisIds[1] != value.Id);
            return secondary is null ? null : chart.AxesOf(secondary).Value;
        }

        var horizontal = plot.IsHorizontal;
        return (role == AxisRole.PrimaryHorizontal) != horizontal ? category : value;
    }

    public static Chart ChangeAxis(Chart chart, AxisRole role, Func<ChartAxis, ChartAxis> change)
    {
        if (AxisOf(chart, role) is not { } axis)
        {
            return chart;
        }

        return chart with { Axes = chart.Axes.Select(entry => entry.Id == axis.Id ? change(entry) : entry).ToList() };
    }

    public static Chart SetAxisVisible(Chart chart, AxisRole role, bool visible) => ChangeAxis(chart, role, axis => axis with { Deleted = !visible });

    public static Chart SetAxisTitle(Chart chart, AxisRole role, bool visible, string? text = null) =>
        ChangeAxis(chart, role, axis => axis with { Title = visible ? (text is null ? axis.Title ?? new ChartTitle() : new ChartTitle(ChartText.Of(text))) : null });

    public static Chart SetGridlines(Chart chart, AxisRole role, bool major, bool minor) =>
        ChangeAxis(chart, role, axis => axis with { MajorGridlines = major, MinorGridlines = minor });

    /// <summary>Data labels on every plot (values; pies show percentages too … as Excel's quick element does: values only).</summary>
    public static Chart SetDataLabels(Chart chart, bool visible, DataLabelPosition position = DataLabelPosition.Auto) =>
        chart with
        {
            Plots = chart.Plots.Select(plot => plot with
            {
                Labels = visible ? new DataLabels(true, Position: position) : null,
                Series = plot.Series.Select(series => series with { Labels = null }).ToList(),
            }).ToList(),
        };

    /// <summary>Farben ändern: the set's automatic colours (series' own colours and shapes go).</summary>
    public static Chart SetColorSet(Chart chart, int colorSet) =>
        chart with
        {
            ColorSet = colorSet,
            Plots = chart.Plots.Select(plot => plot with
            {
                Series = plot.Series.Select(series => series with { Fill = null, Line = null, PointColors = null, ShapeXml = null }).ToList(),
            }).ToList(),
        };

    /// <summary>New series (Zeile/Spalte wechseln, Daten auswählen) in the chart's type; own colours of series kept by position.</summary>
    public static Chart ReplaceSeries(Chart chart, IReadOnlyList<SeriesSpec> specs, bool dateCategories)
    {
        var old = chart.AllSeries.ToList();
        var series = specs.Select((spec, index) =>
        {
            var template = index < old.Count ? old[index] : new ChartSeries(index, spec.Values);
            return template with { Index = index, Order = index, Name = spec.Name, Categories = spec.Categories, Values = spec.Values, PointColors = null };
        }).ToList();
        var type = ChartTemplates.Of(chart) ?? ChartTemplates.All[0];
        var result = ChartFactory.WithType(chart, type, series, dateCategories);
        var single = series.Count == 1 && !result.Plots.Any(plot => plot.IsRound);
        return result with { Legend = single ? null : result.Legend ?? new ChartLegend(LegendPosition.Bottom) };
    }

    /// <summary>The chart's data references (series names, categories, values) as one list – what follows edits of the sheet.</summary>
    public static Chart MapReferences(Chart chart, Func<string, string> map)
    {
        ChartDataRef? Map(ChartDataRef? reference) => reference is { Formula: { Length: > 0 } formula } ? reference with { Formula = map(formula) } : reference;
        ChartText? MapText(ChartText? text) => text?.Reference is { } reference ? text with { Reference = Map(reference) } : text;
        return chart with
        {
            Title = chart.Title is { } title ? title with { Text = MapText(title.Text) } : null,
            Plots = chart.Plots.Select(plot => plot with
            {
                Series = plot.Series.Select(series => series with
                {
                    Name = MapText(series.Name),
                    Categories = Map(series.Categories),
                    Values = Map(series.Values)!,
                }).ToList(),
            }).ToList(),
        };
    }

    /// <summary>Every reference text of the chart (for tests and the Daten auswählen dialog).</summary>
    public static IEnumerable<string> References(Chart chart)
    {
        if (chart.Title?.Text?.Reference?.Formula is { } title)
        {
            yield return title;
        }

        foreach (var series in chart.AllSeries)
        {
            if (series.Name?.Reference?.Formula is { } name)
            {
                yield return name;
            }

            if (series.Categories?.Formula is { } categories)
            {
                yield return categories;
            }

            if (series.Values.Formula is { } values)
            {
                yield return values;
            }
        }
    }
}
