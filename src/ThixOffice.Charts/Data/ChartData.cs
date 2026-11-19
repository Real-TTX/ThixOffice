using System.Globalization;

namespace ThixOffice.Charts;

/// <summary>
/// Where a chart's references point: ThixCalc resolves "Tabelle1!$B$2:$B$13" in the workbook (live), ThixShow / ThixWrite in
/// an embedded workbook or not at all (then the values the file cached are shown).
/// </summary>
public interface IChartDataSource
{
    /// <summary>The values of <paramref name="formula"/> in reading order (hidden cells marked), or null when it cannot be resolved.</summary>
    IReadOnlyList<ChartValue>? Resolve(string formula);
}

/// <summary>Formats axis and label numbers: ThixCalc passes its number-format engine; <see cref="SimpleChartFormatter"/> is the default.</summary>
public interface IChartValueFormatter
{
    /// <summary>The text of <paramref name="value"/> in <paramref name="formatCode"/> (null or "General" = Standard).</summary>
    string Format(double value, string? formatCode);

    /// <summary>Whether the code shows dates (a category axis of dates becomes a time axis).</summary>
    bool IsDateFormat(string? formatCode);
}

/// <summary>
/// A formatter without a spreadsheet engine: "General" with up to 10 significant digits, fixed decimals ("0.00"), thousands
/// ("#,##0"), percent ("0%"), in a culture (German by default). Enough for slides that only have the cached values.
/// </summary>
public sealed class SimpleChartFormatter(CultureInfo? culture = null) : IChartValueFormatter
{
    private readonly CultureInfo _culture = culture ?? CultureInfo.GetCultureInfo("de-DE");

    public string Format(double value, string? formatCode)
    {
        var code = formatCode ?? "General";
        if (code.Contains('%', StringComparison.Ordinal))
        {
            return (value * 100).ToString("0." + new string('#', Decimals(code)), _culture).TrimEnd('.', ',') + " %";
        }

        if (string.Equals(code, "General", StringComparison.OrdinalIgnoreCase) || code.Length == 0)
        {
            return Math.Round(value, 10).ToString("G10", _culture);
        }

        var decimals = Decimals(code);
        var pattern = code.Contains(',', StringComparison.Ordinal) ? "#,##0" : "0";
        if (decimals > 0)
        {
            pattern += "." + new string('0', decimals);
        }

        return value.ToString(pattern, _culture);
    }

    public bool IsDateFormat(string? formatCode) =>
        formatCode is not null && !formatCode.Contains("General", StringComparison.OrdinalIgnoreCase)
        && formatCode.Replace("\"", string.Empty, StringComparison.Ordinal).IndexOfAny(['d', 'm', 'y', 'D', 'M', 'Y', 'J', 'T']) >= 0
        && !formatCode.Contains('0', StringComparison.Ordinal);

    private static int Decimals(string code)
    {
        var point = code.IndexOf('.', StringComparison.Ordinal);
        if (point < 0)
        {
            return 0;
        }

        var count = 0;
        for (var index = point + 1; index < code.Length && code[index] is '0' or '#'; index++)
        {
            count++;
        }

        return count;
    }
}

/// <summary>A series with its data resolved: name, values (null = blank), x values of a scatter, the values' number format.</summary>
public sealed record ResolvedSeries(ChartSeries Series, string Name, double?[] Values, double?[]? XValues, string? FormatCode, string? XFormatCode)
{
    /// <summary>Its position among all series of the chart (the automatic colour).</summary>
    public int ColorIndex { get; init; }
}

/// <summary>A plot with its series resolved.</summary>
public sealed record ResolvedPlot(ChartPlot Plot, IReadOnlyList<ResolvedSeries> Series);

/// <summary>
/// A chart with its data: the category labels shared by the plots (formatted as the cells show them), the categories as
/// numbers (dates on a time axis), and per plot the resolved series. What the layout draws.
/// </summary>
public sealed record ResolvedChart(Chart Chart, IReadOnlyList<string> Categories, IReadOnlyList<double?> CategoryNumbers, string? CategoryFormat, IReadOnlyList<ResolvedPlot> Plots, string? AutoTitle)
{
    /// <summary>The source the data came from (titles that refer to cells are resolved through it), or null.</summary>
    public IChartDataSource? Source { get; init; }

    public int SeriesCount => Plots.Sum(plot => plot.Series.Count);

    /// <summary>The text of a title or name (a reference resolved, else its cache).</summary>
    public string? TextOf(ChartText? text) => ChartDataResolver.TextOf(text, Source);

    /// <summary>The number of category slots (the longest series).</summary>
    public int CategoryCount => Math.Max(Categories.Count, Plots.SelectMany(plot => plot.Series).Select(series => series.Values.Length).DefaultIfEmpty(0).Max());
}

/// <summary>
/// Resolves a chart's references through an <see cref="IChartDataSource"/> (the cache where it cannot), drops hidden points
/// when the chart plots visible cells only, and turns blanks into zeros where the chart says so.
/// </summary>
public static class ChartDataResolver
{
    public static ResolvedChart Resolve(Chart chart, IChartDataSource? source)
    {
        var allSeries = chart.AllSeries.ToList();
        var categorySource = allSeries.Select(series => series.Categories).FirstOrDefault(reference => reference is not null);
        var categoryValues = Values(categorySource, source);
        var seriesValues = allSeries.Select(series => Values(series.Values, source)).ToList();

        // Hidden rows/columns: a point goes when its category or every series' value is hidden.
        var length = Math.Max(categoryValues.Count, seriesValues.Select(values => values.Count).DefaultIfEmpty(0).Max());
        var keep = new List<int>(length);
        for (var index = 0; index < length; index++)
        {
            var hidden = chart.PlotVisibleOnly
                && ((index < categoryValues.Count && categoryValues[index].IsHidden)
                    || (seriesValues.Count > 0 && seriesValues.All(values => index < values.Count && values[index].IsHidden)));
            if (!hidden)
            {
                keep.Add(index);
            }
        }

        var categoryFormat = categorySource?.FormatCode ?? categoryValues.Select(value => value.FormatCode).FirstOrDefault(code => code is not null);
        var categories = new List<string>(keep.Count);
        var categoryNumbers = new List<double?>(keep.Count);
        foreach (var index in keep)
        {
            var value = index < categoryValues.Count ? categoryValues[index] : ChartValue.Blank;
            categories.Add(categorySource is null ? (index + 1).ToString(CultureInfo.InvariantCulture) : value.Text ?? value.Number?.ToString(CultureInfo.CurrentCulture) ?? string.Empty);
            categoryNumbers.Add(categorySource is null ? index + 1 : value.Number);
        }

        var plots = new List<ResolvedPlot>();
        var colorIndex = 0;
        var seriesPosition = 0;
        foreach (var plot in chart.Plots)
        {
            var resolved = new List<ResolvedSeries>();
            foreach (var series in plot.Series)
            {
                var values = seriesValues[seriesPosition++];
                var numbers = keep.Select(index => index < values.Count ? Blank(values[index].Number, plot, chart) : Blank(null, plot, chart)).ToArray();
                double?[]? x = null;
                string? xFormat = null;
                if (plot.Type == ChartType.Scatter && series.Categories is { } xReference)
                {
                    var xValues = Values(xReference, source);
                    x = keep.Select(index => index < xValues.Count ? xValues[index].Number : null).ToArray();
                    xFormat = xReference.FormatCode ?? xValues.Select(value => value.FormatCode).FirstOrDefault(code => code is not null);
                }

                var format = series.Values.FormatCode ?? values.Select(value => value.FormatCode).FirstOrDefault(code => code is not null);
                resolved.Add(new ResolvedSeries(series, NameOf(series, source), numbers, x, format, xFormat) { ColorIndex = colorIndex++ });
            }

            plots.Add(new ResolvedPlot(plot, resolved));
        }

        var autoTitle = allSeries.Count == 1 && plots.Count > 0 && plots[0].Series.Count == 1 && allSeries[0].Name is not null ? plots[0].Series[0].Name : null;
        return new ResolvedChart(chart, categories, categoryNumbers, categoryFormat, plots, autoTitle) { Source = source };
    }

    /// <summary>The text of a title or name: the literal, the resolved cell, or the cache.</summary>
    public static string? TextOf(ChartText? text, IChartDataSource? source)
    {
        if (text is null)
        {
            return null;
        }

        if (text.Literal is { } literal)
        {
            return literal;
        }

        var values = Values(text.Reference, source);
        return values.Count == 0 ? string.Empty : string.Join(" ", values.Select(value => value.Text ?? value.Number?.ToString(CultureInfo.CurrentCulture) ?? string.Empty));
    }

    private static string NameOf(ChartSeries series, IChartDataSource? source) =>
        TextOf(series.Name, source) is { Length: > 0 } name ? name : $"Datenreihe{series.Index + 1}";

    private static double? Blank(double? value, ChartPlot plot, Chart chart)
    {
        if (value is not null || chart.DisplayBlanksAs != BlankCells.Zero || plot.Type is ChartType.Column or ChartType.Bar or ChartType.Pie or ChartType.Doughnut)
        {
            return value;
        }

        return 0;
    }

    private static IReadOnlyList<ChartValue> Values(ChartDataRef? reference, IChartDataSource? source)
    {
        if (reference is null)
        {
            return [];
        }

        if (reference.Formula is { Length: > 0 } formula && source?.Resolve(formula) is { } resolved)
        {
            return resolved;
        }

        return reference.Cache ?? [];
    }
}
