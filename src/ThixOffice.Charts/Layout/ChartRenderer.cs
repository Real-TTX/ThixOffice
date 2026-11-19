using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixOffice.Charts;

/// <summary>
/// Where and how a chart is drawn: the measurer and number formatter, the document's accent colours and font, the rectangle
/// in the target's units (<see cref="X"/>, <see cref="Y"/>, <see cref="Width"/>, <see cref="Height"/>), the target units per
/// point (<see cref="Scale"/>: 1 for a page in points, zoom × 4/3 × screen scaling for the grid's device pixels) and the size
/// of a device pixel in target units (<see cref="PixelSize"/>; gridlines snap to it; 0 = no snapping, for PDF and print).
/// </summary>
public sealed record ChartRenderOptions
{
    /// <summary>The six accents of the Office theme (2013–2022).</summary>
    public static IReadOnlyList<RgbColor> OfficeAccents { get; } =
    [
        RgbColor.FromHex("4472C4"), RgbColor.FromHex("ED7D31"), RgbColor.FromHex("A5A5A5"),
        RgbColor.FromHex("FFC000"), RgbColor.FromHex("5B9BD5"), RgbColor.FromHex("70AD47"),
    ];

    public required ITextMeasurer Measurer { get; init; }

    public IChartValueFormatter Formatter { get; init; } = new SimpleChartFormatter();

    public IReadOnlyList<RgbColor> Accents { get; init; } = OfficeAccents;

    public string FontFamily { get; init; } = "Calibri";

    public double X { get; init; }

    public double Y { get; init; }

    public double Width { get; init; }

    public double Height { get; init; }

    public double Scale { get; init; } = 1;

    public double PixelSize { get; init; }

    public RgbColor Background { get; init; } = RgbColor.White;

    /// <summary>The thin border of the chart area (Office's default chart has one).</summary>
    public bool DrawFrame { get; init; } = true;
}

/// <summary>A rectangle of the layout (target units).</summary>
internal readonly record struct Box(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;

    public double Bottom => Y + Height;

    public double CenterX => X + Width / 2;

    public double CenterY => Y + Height / 2;

    public bool Intersects(Box other) => X < other.Right && other.X < Right && Y < other.Bottom && other.Y < Bottom;

    public Box Inflate(double amount) => new(X - amount, Y - amount, Width + 2 * amount, Height + 2 * amount);
}

/// <summary>An entry of the legend: its text, colour and symbol.</summary>
internal sealed record LegendEntry(string Label, RgbColor Color, bool IsLine, MarkerSymbol Marker, bool HasLine);

/// <summary>
/// Lays a resolved chart out and draws it into display items, the way Office 2016+ charts look by default: a white chart area
/// with a light border, a 14 pt title, 9 pt grey axis labels and legend, light gridlines, series in the theme's accents;
/// automatic axis scales (<see cref="AxisScaling"/>), category labels that wrap, slant or thin out before they collide,
/// a legend that wraps into rows, data labels that avoid each other. Deterministic for the same input and measurer; the
/// result is in the options' target units, so the grid, a slide, a printed page and a PDF get the same chart.
/// </summary>
public static partial class ChartRenderer
{
    internal const double TitleSize = 14;
    internal const double LabelSize = 9;
    internal const double AxisTitleSize = 10;

    /// <summary>The chart's display items.</summary>
    public static IReadOnlyList<DisplayItem> Render(ResolvedChart data, ChartRenderOptions options)
    {
        var canvas = new ChartCanvas(options);
        var bounds = new Box(options.X, options.Y, options.Width, options.Height);
        if (bounds.Width <= 0 || bounds.Height <= 0)
        {
            return [];
        }

        canvas.Rectangle(bounds.X, bounds.Y, bounds.Width, bounds.Height, options.Background);
        canvas.Clip(bounds.X, bounds.Y, bounds.Width, bounds.Height);
        if (!data.Chart.IsSupported)
        {
            Placeholder(canvas, bounds, data.Chart.UnsupportedType ?? "Unbekannt");
        }
        else
        {
            var inner = new Box(bounds.X + canvas.Pt(9), bounds.Y + canvas.Pt(9), bounds.Width - canvas.Pt(18), bounds.Height - canvas.Pt(18));
            inner = DrawTitle(canvas, data, inner);
            inner = DrawLegend(canvas, data, inner, bounds);
            if (inner.Width > canvas.Pt(10) && inner.Height > canvas.Pt(10))
            {
                if (data.Plots.All(plot => plot.Plot.IsRound))
                {
                    DrawRound(canvas, data, inner);
                }
                else
                {
                    DrawCartesian(canvas, data, inner);
                }
            }
        }

        canvas.EndClip();
        if (options.DrawFrame)
        {
            var width = canvas.LineWidth(0.75);
            var half = width / 2;
            var left = canvas.Snap(bounds.X + half, width);
            var top = canvas.Snap(bounds.Y + half, width);
            var right = canvas.Snap(bounds.Right - half, width);
            var bottom = canvas.Snap(bounds.Bottom - half, width);
            canvas.Rectangle(left, top, right - left, bottom - top, null, ChartColors.Gridline, width);
        }

        return canvas.Items;
    }

    /// <summary>A labelled frame for a chart type that is kept but not drawn.</summary>
    private static void Placeholder(ChartCanvas canvas, Box bounds, string type)
    {
        var margin = canvas.Pt(6);
        var path = new PathBuilder()
            .MoveTo(bounds.X + margin, bounds.Y + margin).LineTo(bounds.Right - margin, bounds.Y + margin)
            .LineTo(bounds.Right - margin, bounds.Bottom - margin).LineTo(bounds.X + margin, bounds.Bottom - margin).Close();
        canvas.Items.Add(new PathItem([.. path.Verbs], [.. path.Points], ChartColors.Lighten(ChartColors.Gridline, 0.7), ChartColors.Gridline, canvas.LineWidth(1), LineStyle.Dashed, LineJoin.Miter));
        var title = $"Diagramm: {type}";
        var note = "Bleibt beim Speichern erhalten, wird aber nicht angezeigt.";
        var lineHeight = canvas.LineHeight(LabelSize + 2);
        var y = bounds.CenterY - lineHeight;
        canvas.Text(Fit(canvas, title, LabelSize + 2, bounds.Width - 4 * margin, bold: true), bounds.CenterX, y, LabelSize + 2, ChartColors.Text, TextAlign.Center, bold: true);
        canvas.Text(Fit(canvas, note, LabelSize, bounds.Width - 4 * margin), bounds.CenterX, y + lineHeight + canvas.Pt(2), LabelSize, ChartColors.Text, TextAlign.Center);
    }

    // ===== Title =====

    /// <summary>The title text: the given one, or the automatic one (the only series' name, else "Diagrammtitel"); null for none.</summary>
    internal static string? TitleText(ResolvedChart data)
    {
        var chart = data.Chart;
        if (chart.Title is { } title)
        {
            if (title.Text is { } text)
            {
                return data.TextOf(text) ?? string.Empty;
            }

            return data.AutoTitle ?? "Diagrammtitel";
        }

        return !chart.AutoTitleDeleted && data.AutoTitle is { } auto ? auto : null;
    }

    private static Box DrawTitle(ChartCanvas canvas, ResolvedChart data, Box inner)
    {
        if (TitleText(data) is not { Length: > 0 } text)
        {
            return inner;
        }

        var title = data.Chart.Title;
        var size = title?.FontSize ?? TitleSize;
        var bold = title?.Bold ?? false;
        var lines = Wrap(canvas, text, size, inner.Width * 0.9, bold, maxLines: 3);
        var y = inner.Y;
        var lineHeight = canvas.LineHeight(size, bold);
        foreach (var line in lines)
        {
            canvas.Text(line, inner.CenterX, y, size, ChartColors.Text, TextAlign.Center, bold);
            y += lineHeight;
        }

        if (title?.Overlay == true)
        {
            return inner;
        }

        var used = y - inner.Y + canvas.Pt(6);
        return inner with { Y = inner.Y + used, Height = Math.Max(0, inner.Height - used) };
    }

    // ===== Legend =====

    internal static IReadOnlyList<LegendEntry> LegendEntries(ResolvedChart data, IReadOnlyList<RgbColor> accents)
    {
        var chart = data.Chart;
        var entries = new List<LegendEntry>();
        var round = data.Plots.Count == 1 && (data.Plots[0].Plot.IsRound || data.Plots[0].Plot.VaryColors && data.Plots[0].Series.Count == 1);
        if (round)
        {
            var plot = data.Plots[0];
            var series = plot.Series.FirstOrDefault();
            for (var index = 0; index < data.CategoryCount; index++)
            {
                var label = index < data.Categories.Count ? data.Categories[index] : (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture);
                entries.Add(new LegendEntry(label, PointColor(chart, series, index, data.CategoryCount, accents), false, MarkerSymbol.None, false));
            }

            return entries;
        }

        foreach (var plot in data.Plots)
        {
            foreach (var series in plot.Series)
            {
                var isLine = plot.Plot.Type is ChartType.Line or ChartType.Scatter;
                var marker = isLine ? MarkerOf(plot.Plot, series.Series) : MarkerSymbol.None;
                entries.Add(new LegendEntry(series.Name, SeriesColor(chart, series, data.SeriesCount, accents), isLine, marker, isLine && !series.Series.NoLine));
            }
        }

        return entries;
    }

    private static Box DrawLegend(ChartCanvas canvas, ResolvedChart data, Box inner, Box bounds)
    {
        if (data.Chart.Legend is not { } legend)
        {
            return inner;
        }

        var entries = LegendEntries(data, canvas.Options.Accents);
        if (entries.Count == 0)
        {
            return inner;
        }

        var size = legend.FontSize ?? LabelSize;
        var lineHeight = canvas.LineHeight(size);
        var symbolWidth = entries.Any(entry => entry.IsLine) ? canvas.Pt(20) : canvas.Pt(7);
        var textGap = canvas.Pt(4);
        var entryGap = canvas.Pt(11);
        var horizontal = legend.Position is LegendPosition.Top or LegendPosition.Bottom;
        if (horizontal)
        {
            var maxWidth = inner.Width;
            var rows = new List<List<(LegendEntry Entry, string Text, double Width)>> { new() };
            var rowWidth = 0.0;
            foreach (var entry in entries)
            {
                var text = Fit(canvas, entry.Label, size, Math.Max(canvas.Pt(20), maxWidth - symbolWidth - textGap));
                var width = symbolWidth + textGap + canvas.Measure(text, size).Width;
                var needed = rows[^1].Count == 0 ? width : rowWidth + entryGap + width;
                if (rows[^1].Count > 0 && needed > maxWidth)
                {
                    rows.Add([]);
                    needed = width;
                }

                rows[^1].Add((entry, text, width));
                rowWidth = needed;
            }

            // At most a third of the chart for the legend.
            var rowHeight = lineHeight + canvas.Pt(2);
            var maxRows = Math.Max(1, (int)(inner.Height / 3 / rowHeight));
            if (rows.Count > maxRows)
            {
                rows = rows.Take(maxRows).ToList();
            }

            var height = rows.Count * rowHeight;
            var top = legend.Position == LegendPosition.Bottom ? inner.Bottom - height : inner.Y;
            for (var index = 0; index < rows.Count; index++)
            {
                var row = rows[index];
                var width = row.Sum(item => item.Width) + entryGap * (row.Count - 1);
                var x = inner.CenterX - width / 2;
                foreach (var (entry, text, entryWidth) in row)
                {
                    DrawLegendEntry(canvas, entry, text, x, top + index * rowHeight, lineHeight, symbolWidth, textGap, size);
                    x += entryWidth + entryGap;
                }
            }

            if (legend.Overlay)
            {
                return inner;
            }

            var used = height + canvas.Pt(6);
            return legend.Position == LegendPosition.Bottom
                ? inner with { Height = Math.Max(0, inner.Height - used) }
                : inner with { Y = inner.Y + used, Height = Math.Max(0, inner.Height - used) };
        }

        // Left, right, top right: one column.
        var columnMax = inner.Width * 0.38;
        var texts = entries.Select(entry => Fit(canvas, entry.Label, size, Math.Max(canvas.Pt(20), columnMax - symbolWidth - textGap))).ToList();
        var columnWidth = texts.Max(text => canvas.Measure(text, size).Width) + symbolWidth + textGap;
        var spacing = lineHeight + canvas.Pt(3);
        var count = Math.Max(1, Math.Min(entries.Count, (int)(inner.Height / spacing)));
        var columnHeight = count * spacing - canvas.Pt(3);
        var x0 = legend.Position == LegendPosition.Left ? inner.X : inner.Right - columnWidth;
        var y0 = legend.Position == LegendPosition.TopRight ? inner.Y : Math.Max(inner.Y, bounds.CenterY - columnHeight / 2);
        for (var index = 0; index < count; index++)
        {
            DrawLegendEntry(canvas, entries[index], texts[index], x0, y0 + index * spacing, lineHeight, symbolWidth, textGap, size);
        }

        if (legend.Overlay)
        {
            return inner;
        }

        var reserved = columnWidth + canvas.Pt(9);
        return legend.Position == LegendPosition.Left
            ? inner with { X = inner.X + reserved, Width = Math.Max(0, inner.Width - reserved) }
            : inner with { Width = Math.Max(0, inner.Width - reserved) };
    }

    private static void DrawLegendEntry(ChartCanvas canvas, LegendEntry entry, string text, double x, double top, double lineHeight, double symbolWidth, double textGap, double size)
    {
        var middle = top + lineHeight / 2;
        if (entry.IsLine)
        {
            if (entry.HasLine)
            {
                canvas.Line(x, middle, x + symbolWidth, middle, entry.Color, canvas.Pt(2.25));
            }

            if (entry.Marker != MarkerSymbol.None)
            {
                DrawMarker(canvas, entry.Marker, x + symbolWidth / 2, middle, canvas.Pt(5), entry.Color);
            }
        }
        else
        {
            var box = canvas.Pt(7);
            canvas.Rectangle(x + (symbolWidth - box) / 2, middle - box / 2, box, box, entry.Color);
        }

        canvas.Text(text, x + symbolWidth + textGap, top, size, ChartColors.Text);
    }

    // ===== Colours and markers =====

    internal static RgbColor SeriesColor(Chart chart, ResolvedSeries series, int count, IReadOnlyList<RgbColor> accents) =>
        (series.Series.Fill ?? series.Series.Line ?? ChartColors.SeriesColor(chart.ColorSet, series.ColorIndex, count)).Resolve(accents);

    internal static RgbColor LineColor(Chart chart, ResolvedSeries series, int count, IReadOnlyList<RgbColor> accents) =>
        (series.Series.Line ?? series.Series.Fill ?? ChartColors.SeriesColor(chart.ColorSet, series.ColorIndex, count)).Resolve(accents);

    /// <summary>The colour of point <paramref name="index"/>: its own, else (varied colours) the set's colour of the point, else the series'.</summary>
    internal static RgbColor PointColor(Chart chart, ResolvedSeries? series, int index, int count, IReadOnlyList<RgbColor> accents)
    {
        if (series?.Series.PointColors is { } points && points.TryGetValue(index, out var own))
        {
            return own.Resolve(accents);
        }

        return ChartColors.SeriesColor(chart.ColorSet, index, count).Resolve(accents);
    }

    internal static MarkerSymbol MarkerOf(ChartPlot plot, ChartSeries series)
    {
        if (series.Marker != MarkerSymbol.Auto)
        {
            return series.Marker;
        }

        if (plot.Type == ChartType.Line && !plot.ShowMarkers)
        {
            return MarkerSymbol.None;
        }

        return MarkerSymbol.Circle;
    }

    internal static void DrawMarker(ChartCanvas canvas, MarkerSymbol symbol, double x, double y, double size, RgbColor color)
    {
        var half = size / 2;
        var path = new PathBuilder();
        switch (symbol)
        {
            case MarkerSymbol.None:
                return;
            case MarkerSymbol.Square:
                path.MoveTo(x - half, y - half).LineTo(x + half, y - half).LineTo(x + half, y + half).LineTo(x - half, y + half).Close();
                break;
            case MarkerSymbol.Diamond:
                path.MoveTo(x, y - half * 1.2).LineTo(x + half * 1.2, y).LineTo(x, y + half * 1.2).LineTo(x - half * 1.2, y).Close();
                break;
            case MarkerSymbol.Triangle:
                path.MoveTo(x, y - half * 1.15).LineTo(x + half * 1.15, y + half * 0.85).LineTo(x - half * 1.15, y + half * 0.85).Close();
                break;
            case MarkerSymbol.X or MarkerSymbol.Plus or MarkerSymbol.Dash or MarkerSymbol.Star:
                var width = canvas.Pt(1.25);
                if (symbol is MarkerSymbol.X or MarkerSymbol.Star)
                {
                    canvas.Line(x - half, y - half, x + half, y + half, color, width);
                    canvas.Line(x - half, y + half, x + half, y - half, color, width);
                }

                if (symbol is MarkerSymbol.Plus or MarkerSymbol.Star)
                {
                    canvas.Line(x, y - half, x, y + half, color, width);
                }

                if (symbol is MarkerSymbol.Plus or MarkerSymbol.Dash or MarkerSymbol.Star)
                {
                    canvas.Line(x - half, y, x + half, y, color, width);
                }

                return;
            default:
                path.Circle(x, y, half);
                break;
        }

        canvas.Path(path, color, color, canvas.Pt(0.75), LineJoin.Miter);
    }

    // ===== Text helpers =====

    /// <summary>The text shortened with "…" to <paramref name="width"/>.</summary>
    internal static string Fit(ChartCanvas canvas, string text, double size, double width, bool bold = false)
    {
        if (canvas.Measure(text, size, bold).Width <= width || text.Length <= 1)
        {
            return text;
        }

        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (canvas.Measure(text[..middle].TrimEnd() + "…", size, bold).Width <= width)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        return low == 0 ? "…" : text[..low].TrimEnd() + "…";
    }

    /// <summary>The text broken at spaces into lines of at most <paramref name="width"/> (the last one shortened when they run out).</summary>
    internal static List<string> Wrap(ChartCanvas canvas, string text, double size, double width, bool bold = false, int maxLines = 2)
    {
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
        {
            var current = string.Empty;
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : current + " " + word;
                if (current.Length > 0 && canvas.Measure(candidate, size, bold).Width > width)
                {
                    lines.Add(current);
                    current = word;
                }
                else
                {
                    current = candidate;
                }
            }

            lines.Add(current);
        }

        if (lines.Count > maxLines)
        {
            var kept = lines.Take(maxLines).ToList();
            kept[^1] = Fit(canvas, string.Join(" ", lines.Skip(maxLines - 1)), size, width, bold);
            return kept;
        }

        return lines.Select(line => Fit(canvas, line, size, width, bold)).ToList();
    }
}
