namespace ThixOffice.Charts;

/// <summary>The families of the Einfügen › Diagramme gallery (one ribbon button each).</summary>
public enum ChartFamily : byte
{
    Column,
    Bar,
    Line,
    Pie,
    Area,
    Scatter,
    Combo,
}

/// <summary>
/// A chart type as the user picks it (Gruppierte Säulen, Linie mit Datenpunkten, Ring …): its family, the plot type and
/// grouping, and the options the type implies (markers, smoothed or no lines, a line on the secondary axis).
/// </summary>
public sealed record ChartTemplate(string Id, string Label, ChartFamily Family, ChartType Type, ChartGrouping Grouping)
{
    public bool Markers { get; init; }

    public bool Smooth { get; init; }

    /// <summary>Scatter: markers only (no connecting lines).</summary>
    public bool NoLines { get; init; }

    /// <summary>Combo: the last series as a line (on the secondary axis when set).</summary>
    public bool ComboSecondary { get; init; }

    /// <summary>The German explanation of the gallery's tooltip.</summary>
    public string Description { get; init; } = string.Empty;
}

/// <summary>The chart types ThixOffice creates, in the order of Excel's galleries.</summary>
public static class ChartTemplates
{
    public static IReadOnlyList<ChartTemplate> All { get; } =
    [
        new("column", "Gruppierte Säulen", ChartFamily.Column, ChartType.Column, ChartGrouping.Clustered) { Description = "Vergleicht Werte über Kategorien mit senkrechten Säulen." },
        new("column-stacked", "Gestapelte Säulen", ChartFamily.Column, ChartType.Column, ChartGrouping.Stacked) { Description = "Zeigt, wie Teile zu einer Summe beitragen." },
        new("column-percent", "Gestapelte Säulen (100 %)", ChartFamily.Column, ChartType.Column, ChartGrouping.PercentStacked) { Description = "Zeigt den Anteil jedes Werts an der Summe einer Kategorie." },
        new("bar", "Gruppierte Balken", ChartFamily.Bar, ChartType.Bar, ChartGrouping.Clustered) { Description = "Vergleicht Werte mit waagrechten Balken – gut bei langen Kategorienamen." },
        new("bar-stacked", "Gestapelte Balken", ChartFamily.Bar, ChartType.Bar, ChartGrouping.Stacked),
        new("bar-percent", "Gestapelte Balken (100 %)", ChartFamily.Bar, ChartType.Bar, ChartGrouping.PercentStacked),
        new("line", "Linie", ChartFamily.Line, ChartType.Line, ChartGrouping.Clustered) { Description = "Zeigt Trends über die Zeit oder geordnete Kategorien." },
        new("line-markers", "Linie mit Datenpunkten", ChartFamily.Line, ChartType.Line, ChartGrouping.Clustered) { Markers = true, Description = "Eine Linie, deren Werte als Punkte hervorgehoben sind." },
        new("line-stacked", "Gestapelte Linie", ChartFamily.Line, ChartType.Line, ChartGrouping.Stacked),
        new("line-percent", "Gestapelte Linie (100 %)", ChartFamily.Line, ChartType.Line, ChartGrouping.PercentStacked),
        new("pie", "Kreis", ChartFamily.Pie, ChartType.Pie, ChartGrouping.Clustered) { Description = "Zeigt die Anteile einer Datenreihe am Ganzen." },
        new("doughnut", "Ring", ChartFamily.Pie, ChartType.Doughnut, ChartGrouping.Clustered) { Description = "Wie ein Kreis, mit einem Loch in der Mitte; mehrere Datenreihen als Ringe." },
        new("area", "Fläche", ChartFamily.Area, ChartType.Area, ChartGrouping.Clustered) { Description = "Zeigt Trends als gefüllte Flächen." },
        new("area-stacked", "Gestapelte Fläche", ChartFamily.Area, ChartType.Area, ChartGrouping.Stacked) { Description = "Zeigt Trends und den Beitrag jedes Teils zur Summe." },
        new("area-percent", "Gestapelte Fläche (100 %)", ChartFamily.Area, ChartType.Area, ChartGrouping.PercentStacked),
        new("scatter", "Punkt (XY)", ChartFamily.Scatter, ChartType.Scatter, ChartGrouping.Clustered) { Markers = true, NoLines = true, Description = "Zeigt die Beziehung zwischen zwei Zahlenreihen." },
        new("scatter-smooth-markers", "Punkte mit interpolierten Linien und Datenpunkten", ChartFamily.Scatter, ChartType.Scatter, ChartGrouping.Clustered) { Markers = true, Smooth = true },
        new("scatter-smooth", "Punkte mit interpolierten Linien", ChartFamily.Scatter, ChartType.Scatter, ChartGrouping.Clustered) { Smooth = true },
        new("scatter-lines-markers", "Punkte mit geraden Linien und Datenpunkten", ChartFamily.Scatter, ChartType.Scatter, ChartGrouping.Clustered) { Markers = true },
        new("scatter-lines", "Punkte mit geraden Linien", ChartFamily.Scatter, ChartType.Scatter, ChartGrouping.Clustered),
        new("combo", "Gruppierte Säulen – Linie", ChartFamily.Combo, ChartType.Column, ChartGrouping.Clustered) { Description = "Säulen und eine Linie in einem Diagramm." },
        new("combo-secondary", "Gruppierte Säulen – Linie auf der Sekundärachse", ChartFamily.Combo, ChartType.Column, ChartGrouping.Clustered) { ComboSecondary = true, Description = "Säulen und eine Linie mit eigener Achse rechts – für Werte verschiedener Größenordnung." },
    ];

    public static ChartTemplate? Find(string id) => All.FirstOrDefault(template => template.Id == id);

    /// <summary>The German name of a family (the ribbon buttons).</summary>
    public static string FamilyLabel(ChartFamily family) => family switch
    {
        ChartFamily.Column => "Säule",
        ChartFamily.Bar => "Balken",
        ChartFamily.Line => "Linie",
        ChartFamily.Pie => "Kreis",
        ChartFamily.Area => "Fläche",
        ChartFamily.Scatter => "Punkt (XY)",
        _ => "Verbund",
    };

    /// <summary>The template that matches a chart's first plot (the selected entry of "Diagrammtyp ändern"), or null.</summary>
    public static ChartTemplate? Of(Chart chart)
    {
        if (!chart.IsSupported)
        {
            return null;
        }

        if (chart.Plots.Count > 1)
        {
            var secondary = chart.Plots[1].AxisIds.Count > 1 && chart.Plots[0].AxisIds.Count > 1 && chart.Plots[1].AxisIds[1] != chart.Plots[0].AxisIds[1];
            return Find(secondary ? "combo-secondary" : "combo");
        }

        var plot = chart.Plots[0];
        var series = plot.Series.FirstOrDefault();
        return All.FirstOrDefault(template => template.Family != ChartFamily.Combo && template.Type == plot.Type && template.Grouping == plot.Grouping && plot.Type switch
        {
            ChartType.Line => template.Markers == (plot.ShowMarkers && series?.Marker != MarkerSymbol.None) || template.Grouping != ChartGrouping.Clustered,
            ChartType.Scatter => template.Smooth == (series?.Smooth ?? false) && template.NoLines == (series?.NoLine ?? false) && template.Markers == (series?.Marker != MarkerSymbol.None),
            _ => true,
        });
    }
}

/// <summary>A series to build: its name, categories (or x values) and values as references, with the cached values.</summary>
public sealed record SeriesSpec(ChartText? Name, ChartDataRef? Categories, ChartDataRef Values);

/// <summary>
/// Makes new charts the way Excel 2016+ makes them: an automatic title, the legend at the bottom (none for a single series
/// unless pie), value gridlines, gap widths and overlaps of the type, a time axis for date categories, and the axes of
/// the type (bars: categories at the left; combo: the line on a secondary axis at the right).
/// </summary>
public static class ChartFactory
{
    public const int CategoryAxisId = 500000001;
    public const int ValueAxisId = 500000002;
    public const int SecondaryCategoryAxisId = 500000003;
    public const int SecondaryValueAxisId = 500000004;

    public static Chart Create(ChartTemplate template, IReadOnlyList<SeriesSpec> specs, bool dateCategories = false)
    {
        var series = specs.Select((spec, index) => new ChartSeries(index, spec.Values) { Order = index, Name = spec.Name, Categories = spec.Categories }).ToList();
        var chart = new Chart { Title = new ChartTitle(), Legend = new ChartLegend(LegendPosition.Bottom) };
        return WithType(chart, template, series, dateCategories);
    }

    /// <summary>The chart with another type: its series (names, references, own colours, labels) in plots and axes of <paramref name="template"/>.</summary>
    public static Chart WithType(Chart chart, ChartTemplate template, IReadOnlyList<ChartSeries>? series = null, bool? dateCategories = null)
    {
        var all = (series ?? chart.AllSeries.ToList()).Select((entry, index) => entry with { Index = index, Order = index }).ToList();
        var isDate = dateCategories ?? chart.Axes.Any(axis => axis.Kind == AxisKind.Date);
        var round = template.Type is ChartType.Pie or ChartType.Doughnut;
        var labels = chart.Plots.FirstOrDefault()?.Labels;
        all = all.Select(entry => Adapt(entry, template)).ToList();

        var plots = new List<ChartPlot>();
        var axes = new List<ChartAxis>();
        if (round)
        {
            var kept = template.Type == ChartType.Pie ? all.Take(1).ToList() : all;
            plots.Add(new ChartPlot(template.Type, ChartGrouping.Clustered, kept) { VaryColors = true, HoleSize = 75, Labels = labels });
        }
        else if (template.Family == ChartFamily.Combo && all.Count > 1)
        {
            var columns = all.Take(all.Count - 1).ToList();
            var line = all[^1] with { Marker = MarkerSymbol.None, Fill = null, ShapeXml = null };
            plots.Add(BarPlot(ChartType.Column, ChartGrouping.Clustered, columns, labels) with { AxisIds = [CategoryAxisId, ValueAxisId] });
            var lineAxes = template.ComboSecondary ? new[] { SecondaryCategoryAxisId, SecondaryValueAxisId } : new[] { CategoryAxisId, ValueAxisId };
            plots.Add(new ChartPlot(ChartType.Line, ChartGrouping.Clustered, [line]) { ShowMarkers = false, AxisIds = lineAxes, Labels = labels });
            axes.AddRange(CartesianAxes(ChartType.Column, isDate));
            if (template.ComboSecondary)
            {
                axes.Add(new ChartAxis(SecondaryCategoryAxisId, isDate ? AxisKind.Date : AxisKind.Category, AxisPosition.Bottom, SecondaryValueAxisId) { Deleted = true });
                axes.Add(new ChartAxis(SecondaryValueAxisId, AxisKind.Value, AxisPosition.Right, SecondaryCategoryAxisId) { Crosses = AxisCrossing.Maximum });
            }
        }
        else
        {
            var type = template.Family == ChartFamily.Combo ? ChartType.Column : template.Type;
            var plot = type switch
            {
                ChartType.Column or ChartType.Bar => BarPlot(type, template.Grouping, all, labels),
                ChartType.Line => new ChartPlot(type, template.Grouping, all) { ShowMarkers = template.Markers, Labels = labels },
                _ => new ChartPlot(type, template.Grouping, all) { Labels = labels },
            };
            plots.Add(plot with { AxisIds = [CategoryAxisId, ValueAxisId] });
            axes.AddRange(CartesianAxes(type, isDate && type != ChartType.Scatter));
        }

        // Keep the user's axis titles and gridline choices where the axes still exist.
        axes = axes.Select(axis => chart.Axes.FirstOrDefault(old => old.Id == axis.Id) is { } old
            ? axis with { Title = old.Title, MajorGridlines = old.MajorGridlines, MinorGridlines = old.MinorGridlines, Deleted = old.Deleted && axis.Deleted }
            : axis).ToList();
        var legend = chart.Legend;
        if (chart.Plots.Count == 0)
        {
            legend = all.Count > 1 || round ? new ChartLegend(LegendPosition.Bottom) : null;
        }

        return chart with { Plots = plots, Axes = round ? [] : axes, Legend = legend };
    }

    private static ChartSeries Adapt(ChartSeries series, ChartTemplate template) => template.Type switch
    {
        ChartType.Line => series with { Marker = template.Markers ? MarkerSymbol.Auto : MarkerSymbol.None, Smooth = false, NoLine = false, ShapeXml = series.Fill is null && series.Line is null ? null : series.ShapeXml },
        ChartType.Scatter => series with
        {
            Marker = template.Markers ? MarkerSymbol.Auto : MarkerSymbol.None,
            Smooth = template.Smooth,
            NoLine = template.NoLines,
            ShapeXml = null,
        },
        _ => series with { Marker = MarkerSymbol.Auto, Smooth = false, NoLine = false, ShapeXml = null, Line = null, Fill = series.Fill ?? series.Line },
    };

    private static ChartPlot BarPlot(ChartType type, ChartGrouping grouping, IReadOnlyList<ChartSeries> series, DataLabels? labels) =>
        grouping == ChartGrouping.Clustered
            ? new ChartPlot(type, grouping, series) { GapWidth = 219, Overlap = -27, Labels = labels }
            : new ChartPlot(type, grouping, series) { GapWidth = 150, Overlap = 100, Labels = labels };

    private static IEnumerable<ChartAxis> CartesianAxes(ChartType type, bool dateCategories)
    {
        var horizontal = type == ChartType.Bar;
        var scatter = type == ChartType.Scatter;
        var categoryKind = scatter ? AxisKind.Value : dateCategories ? AxisKind.Date : AxisKind.Category;
        yield return new ChartAxis(CategoryAxisId, scatter ? AxisKind.Category : categoryKind, horizontal ? AxisPosition.Left : AxisPosition.Bottom, ValueAxisId)
        {
            MajorGridlines = scatter,
            CrossBetween = type != ChartType.Area,
        };
        yield return new ChartAxis(ValueAxisId, AxisKind.Value, horizontal ? AxisPosition.Bottom : AxisPosition.Left, CategoryAxisId)
        {
            MajorGridlines = true,
            CrossBetween = type is not (ChartType.Area or ChartType.Scatter),
        };
    }
}
