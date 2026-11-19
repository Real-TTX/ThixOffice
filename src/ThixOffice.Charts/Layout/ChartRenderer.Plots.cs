using ThixOffice.Text.Drawing;

namespace ThixOffice.Charts;

/// <summary>What the plot drawings share: the canvas, the data, the plot rectangle, the category geometry, the placed labels.</summary>
internal sealed class PlotContext(ChartCanvas canvas, ResolvedChart data, Box inner, CategoryGeometry geometry, bool horizontal, List<Box> labels)
{
    public ChartCanvas Canvas { get; } = canvas;

    public ResolvedChart Data { get; } = data;

    public Box Inner { get; } = inner;

    public CategoryGeometry Geometry { get; } = geometry;

    public bool Horizontal { get; } = horizontal;

    /// <summary>Data labels are drawn after every plot (on top); their boxes avoid each other.</summary>
    public List<Action> PendingLabels { get; } = [];

    public List<Box> Labels { get; } = labels;

    public int SeriesCount => Data.SeriesCount;

    /// <summary>The screen position of category <paramref name="index"/>'s centre along the category axis.</summary>
    public double CategoryCenter(int index) => Horizontal
        ? Inner.Bottom - Geometry.Center(index) * Inner.Height
        : Inner.X + Geometry.Center(index) * Inner.Width;

    /// <summary>One category slot in screen units.</summary>
    public double SlotLength => Geometry.SlotWidth * (Horizontal ? Inner.Height : Inner.Width);
}

public static partial class ChartRenderer
{
    // ===== Columns and bars =====

    private static void DrawBars(PlotContext context, ResolvedPlot plot, ValueAxisLayout values, IReadOnlyList<ResolvedPlot> _)
    {
        var canvas = context.Canvas;
        var chart = context.Data.Chart;
        var series = plot.Series;
        if (series.Count == 0)
        {
            return;
        }

        var (_, valueAxis) = chart.AxesOf(plot.Plot);
        var clustered = plot.Plot.Grouping == ChartGrouping.Clustered;
        var percent = plot.Plot.Grouping == ChartGrouping.PercentStacked;
        var count = clustered ? series.Count : 1;
        var gap = Math.Clamp(plot.Plot.GapWidth, 0, 500) / 100.0;
        var overlap = clustered ? Math.Clamp(plot.Plot.Overlap, -100, 100) / 100.0 : 1;
        var slot = context.SlotLength;
        var bar = slot / (count - (count - 1) * overlap + gap);
        var crossing = Crossing(values, valueAxis);
        var categories = series.Max(entry => entry.Values.Length);
        var vary = plot.Plot.VaryColors && series.Count == 1;
        for (var index = 0; index < categories; index++)
        {
            var center = context.CategoryCenter(index);
            var start = context.Horizontal ? center + slot / 2 : center - slot / 2;
            var positive = 0.0;
            var negative = 0.0;
            var total = percent ? series.Sum(entry => index < entry.Values.Length ? Math.Abs(entry.Values[index] ?? 0) : 0) : 1;
            for (var position = 0; position < series.Count; position++)
            {
                var entry = series[position];
                if (index >= entry.Values.Length || entry.Values[index] is not { } raw)
                {
                    continue;
                }

                var value = percent ? (total == 0 ? 0 : raw / total) : raw;
                double from;
                double to;
                if (clustered)
                {
                    (from, to) = (crossing, value);
                }
                else if (value >= 0)
                {
                    (from, to) = (positive, positive + value);
                    positive = to;
                }
                else
                {
                    (from, to) = (negative, negative + value);
                    negative = to;
                }

                var offset = gap * bar / 2 + (clustered ? position * bar * (1 - overlap) : 0);
                var color = vary || entry.Series.PointColors?.ContainsKey(index) == true
                    ? PointColor(chart, entry, index, categories, canvas.Options.Accents)
                    : SeriesColor(chart, entry, context.SeriesCount, canvas.Options.Accents);
                Box rect;
                if (context.Horizontal)
                {
                    var bottom = start - offset;
                    var x1 = ValueX(context.Inner, values.Scale, Clamp(from, values.Scale), valueAxis);
                    var x2 = ValueX(context.Inner, values.Scale, Clamp(to, values.Scale), valueAxis);
                    rect = SnapBox(canvas, new Box(Math.Min(x1, x2), bottom - bar, Math.Abs(x2 - x1), bar));
                }
                else
                {
                    var left = start + offset;
                    var y1 = ValueY(context.Inner, values.Scale, Clamp(from, values.Scale), valueAxis);
                    var y2 = ValueY(context.Inner, values.Scale, Clamp(to, values.Scale), valueAxis);
                    rect = SnapBox(canvas, new Box(left, Math.Min(y1, y2), bar, Math.Abs(y2 - y1)));
                }

                canvas.Rectangle(rect.X, rect.Y, rect.Width, rect.Height, color);
                if (LabelsOf(plot.Plot, entry.Series) is { IsVisible: true } labels)
                {
                    var text = LabelText(context.Canvas.Options.Formatter, context.Data, entry, index, raw, percent ? raw / total : null, labels);
                    var place = labels.Position == DataLabelPosition.Auto ? (clustered ? DataLabelPosition.OutsideEnd : DataLabelPosition.Center) : labels.Position;
                    var positiveValue = to >= from;
                    var barRect = rect;
                    var barColor = color;
                    context.PendingLabels.Add(() => PlaceBarLabel(context, text, barRect, place, positiveValue, barColor));
                }
            }
        }
    }

    private static double Clamp(double value, AxisScale scale) => Math.Clamp(value, scale.Minimum, scale.Maximum);

    /// <summary>Bar edges on whole device pixels (no blurred sides on screen).</summary>
    private static Box SnapBox(ChartCanvas canvas, Box box)
    {
        var pixel = canvas.Options.PixelSize;
        if (pixel <= 0)
        {
            return box;
        }

        var left = Math.Round(box.X / pixel) * pixel;
        var right = Math.Round(box.Right / pixel) * pixel;
        var top = Math.Round(box.Y / pixel) * pixel;
        var bottom = Math.Round(box.Bottom / pixel) * pixel;
        return new Box(left, top, Math.Max(0, right - left), Math.Max(0, bottom - top));
    }

    private static void PlaceBarLabel(PlotContext context, string text, Box bar, DataLabelPosition position, bool positive, RgbColor barColor)
    {
        var canvas = context.Canvas;
        var line = canvas.Measure(text, LabelSize);
        var gap = canvas.Pt(2);
        double x;
        double y;
        var inside = position is DataLabelPosition.Center or DataLabelPosition.InsideEnd or DataLabelPosition.InsideBase;
        if (context.Horizontal)
        {
            y = bar.CenterY - line.Height / 2;
            x = position switch
            {
                DataLabelPosition.Center => bar.CenterX - line.Width / 2,
                DataLabelPosition.InsideEnd => positive ? bar.Right - gap - line.Width : bar.X + gap,
                DataLabelPosition.InsideBase => positive ? bar.X + gap : bar.Right - gap - line.Width,
                _ => positive ? bar.Right + gap : bar.X - gap - line.Width,
            };
        }
        else
        {
            x = bar.CenterX - line.Width / 2;
            y = position switch
            {
                DataLabelPosition.Center => bar.CenterY - line.Height / 2,
                DataLabelPosition.InsideEnd => positive ? bar.Y + gap : bar.Bottom - gap - line.Height,
                DataLabelPosition.InsideBase => positive ? bar.Bottom - gap - line.Height : bar.Y + gap,
                _ => positive ? bar.Y - gap - line.Height : bar.Bottom + gap,
            };
        }

        var box = new Box(x, y, line.Width, line.Height);
        if (inside && (line.Width > (context.Horizontal ? bar.Width : bar.Width + gap) || line.Height > (context.Horizontal ? bar.Height + gap : bar.Height)))
        {
            // Does not fit inside the bar: Excel lets it overflow; keep it legible on the background instead.
            inside = false;
        }

        if (context.Labels.Any(placed => placed.Intersects(box)))
        {
            return;
        }

        context.Labels.Add(box);
        canvas.Text(text, x, y, LabelSize, inside ? ChartColors.ContrastText(barColor) : ChartColors.LabelText);
    }

    // ===== Lines =====

    private static void DrawLines(PlotContext context, ResolvedPlot plot, ValueAxisLayout values)
    {
        var canvas = context.Canvas;
        var chart = context.Data.Chart;
        var (_, valueAxis) = chart.AxesOf(plot.Plot);
        var stacked = plot.Plot.Grouping != ChartGrouping.Clustered;
        var percent = plot.Plot.Grouping == ChartGrouping.PercentStacked;
        var categories = plot.Series.Select(entry => entry.Values.Length).DefaultIfEmpty(0).Max();
        var sums = new double[categories];
        var totals = Enumerable.Range(0, categories).Select(index => plot.Series.Sum(entry => index < entry.Values.Length ? Math.Abs(entry.Values[index] ?? 0) : 0)).ToArray();
        foreach (var entry in plot.Series)
        {
            var points = new List<(int Index, double X, double Y, double Value)>();
            for (var index = 0; index < categories; index++)
            {
                double? value = index < entry.Values.Length ? entry.Values[index] : null;
                if (value is not { } raw)
                {
                    points.Add((index, double.NaN, double.NaN, double.NaN));
                    continue;
                }

                var shown = percent ? (totals[index] == 0 ? 0 : raw / totals[index]) : raw;
                if (stacked)
                {
                    sums[index] += shown;
                    shown = sums[index];
                }

                points.Add((index, context.CategoryCenter(index), ValueY(context.Inner, values.Scale, shown, valueAxis), raw));
            }

            var color = LineColor(chart, entry, context.SeriesCount, canvas.Options.Accents);
            DrawSeriesLine(context, plot, entry, points, color);
        }
    }

    /// <summary>A line through the points (gaps at blanks unless the chart spans them), its markers and labels.</summary>
    private static void DrawSeriesLine(PlotContext context, ResolvedPlot plot, ResolvedSeries entry, List<(int Index, double X, double Y, double Value)> points, RgbColor color)
    {
        var canvas = context.Canvas;
        var span = context.Data.Chart.DisplayBlanksAs == BlankCells.Span;
        var width = canvas.Pt(entry.Series.LineWidth ?? 2.25);
        if (!entry.Series.NoLine)
        {
            var segment = new List<(double X, double Y)>();
            void Flush()
            {
                if (segment.Count >= 2)
                {
                    var path = new PathBuilder();
                    if (entry.Series.Smooth)
                    {
                        path.Smooth(segment);
                    }
                    else
                    {
                        path.Polyline(segment);
                    }

                    canvas.Path(path, null, color, width);
                }

                segment.Clear();
            }

            foreach (var point in points)
            {
                if (double.IsNaN(point.Y))
                {
                    if (!span)
                    {
                        Flush();
                    }

                    continue;
                }

                segment.Add((point.X, point.Y));
            }

            Flush();
        }

        var marker = MarkerOf(plot.Plot, entry.Series);
        var size = canvas.Pt(entry.Series.MarkerSize ?? 5);
        foreach (var point in points.Where(point => !double.IsNaN(point.Y)))
        {
            var pointColor = entry.Series.PointColors?.TryGetValue(point.Index, out var own) == true ? own.Resolve(canvas.Options.Accents) : color;
            DrawMarker(canvas, marker, point.X, point.Y, size, pointColor);
        }

        if (LabelsOf(plot.Plot, entry.Series) is { IsVisible: true } labels)
        {
            foreach (var point in points.Where(point => !double.IsNaN(point.Y)))
            {
                var text = LabelText(context.Canvas.Options.Formatter, context.Data, entry, point.Index, point.Value, null, labels);
                var place = labels.Position == DataLabelPosition.Auto ? DataLabelPosition.Right : labels.Position;
                var (x, y) = (point.X, point.Y);
                context.PendingLabels.Add(() => PlacePointLabel(context, text, x, y, place, marker == MarkerSymbol.None ? 0 : size / 2));
            }
        }
    }

    private static void PlacePointLabel(PlotContext context, string text, double x, double y, DataLabelPosition position, double radius)
    {
        var canvas = context.Canvas;
        var line = canvas.Measure(text, LabelSize);
        var gap = canvas.Pt(3) + radius;
        Box At(DataLabelPosition place) => place switch
        {
            DataLabelPosition.Left => new Box(x - gap - line.Width, y - line.Height / 2, line.Width, line.Height),
            DataLabelPosition.Above => new Box(x - line.Width / 2, y - gap - line.Height, line.Width, line.Height),
            DataLabelPosition.Below => new Box(x - line.Width / 2, y + gap, line.Width, line.Height),
            DataLabelPosition.Center => new Box(x - line.Width / 2, y - line.Height / 2, line.Width, line.Height),
            _ => new Box(x + gap, y - line.Height / 2, line.Width, line.Height),
        };

        // The wanted place, else the others that do not collide with labels already placed.
        DataLabelPosition[] order = [position, DataLabelPosition.Above, DataLabelPosition.Below, DataLabelPosition.Right, DataLabelPosition.Left];
        foreach (var place in order)
        {
            var box = At(place);
            if (!context.Labels.Any(placed => placed.Intersects(box)))
            {
                context.Labels.Add(box);
                canvas.Text(text, box.X, box.Y, LabelSize, ChartColors.LabelText);
                return;
            }
        }
    }

    // ===== Areas =====

    private static void DrawAreas(PlotContext context, ResolvedPlot plot, ValueAxisLayout values)
    {
        var canvas = context.Canvas;
        var chart = context.Data.Chart;
        var (_, valueAxis) = chart.AxesOf(plot.Plot);
        var stacked = plot.Plot.Grouping != ChartGrouping.Clustered;
        var percent = plot.Plot.Grouping == ChartGrouping.PercentStacked;
        var categories = plot.Series.Select(entry => entry.Values.Length).DefaultIfEmpty(0).Max();
        if (categories == 0)
        {
            return;
        }

        var crossing = Crossing(values, valueAxis);
        var baseline = Enumerable.Repeat(stacked ? 0.0 : crossing, categories).ToArray();
        var totals = Enumerable.Range(0, categories).Select(index => plot.Series.Sum(entry => index < entry.Values.Length ? Math.Abs(entry.Values[index] ?? 0) : 0)).ToArray();
        foreach (var entry in plot.Series)
        {
            var tops = new double[categories];
            for (var index = 0; index < categories; index++)
            {
                var raw = index < entry.Values.Length ? entry.Values[index] ?? 0 : 0;
                var value = percent ? (totals[index] == 0 ? 0 : raw / totals[index]) : raw;
                tops[index] = stacked ? baseline[index] + value : value;
            }

            var path = new PathBuilder();
            for (var index = 0; index < categories; index++)
            {
                var x = context.CategoryCenter(index);
                var y = ValueY(context.Inner, values.Scale, Clamp(tops[index], values.Scale), valueAxis);
                if (index == 0)
                {
                    path.MoveTo(x, y);
                }
                else
                {
                    path.LineTo(x, y);
                }
            }

            for (var index = categories - 1; index >= 0; index--)
            {
                path.LineTo(context.CategoryCenter(index), ValueY(context.Inner, values.Scale, Clamp(baseline[index], values.Scale), valueAxis));
            }

            path.Close();
            var color = SeriesColor(chart, entry, context.SeriesCount, canvas.Options.Accents);
            canvas.Path(path, color, null, 0, LineJoin.Miter);
            if (LabelsOf(plot.Plot, entry.Series) is { IsVisible: true } labels)
            {
                for (var index = 0; index < categories; index++)
                {
                    if (index >= entry.Values.Length || entry.Values[index] is not { } raw)
                    {
                        continue;
                    }

                    var text = LabelText(context.Canvas.Options.Formatter, context.Data, entry, index, raw, null, labels);
                    var x = context.CategoryCenter(index);
                    var y = ValueY(context.Inner, values.Scale, (tops[index] + baseline[index]) / 2, valueAxis);
                    context.PendingLabels.Add(() => PlacePointLabel(context, text, x, y, DataLabelPosition.Center, 0));
                }
            }

            if (stacked)
            {
                baseline = tops;
            }
        }
    }

    // ===== Scatter =====

    private static void DrawScatter(PlotContext context, ResolvedPlot plot, ValueAxisLayout values, ValueAxisLayout xValues)
    {
        var canvas = context.Canvas;
        var chart = context.Data.Chart;
        var (xAxis, valueAxis) = chart.AxesOf(plot.Plot);
        foreach (var entry in plot.Series)
        {
            var points = new List<(int Index, double X, double Y, double Value)>();
            for (var index = 0; index < entry.Values.Length; index++)
            {
                var x = entry.XValues is { } xs ? (index < xs.Length ? xs[index] : null) : index + 1;
                if (entry.Values[index] is not { } y || x is null)
                {
                    points.Add((index, double.NaN, double.NaN, double.NaN));
                    continue;
                }

                points.Add((index, ValueX(context.Inner, xValues.Scale, x.Value, xAxis), ValueY(context.Inner, values.Scale, y, valueAxis), y));
            }

            DrawSeriesLine(context, plot, entry, points, LineColor(chart, entry, context.SeriesCount, canvas.Options.Accents));
        }
    }

    // ===== Pie and doughnut =====

    private static void DrawRound(ChartCanvas canvas, ResolvedChart data, Box area)
    {
        var plot = data.Plots[0];
        if (plot.Series.Count == 0)
        {
            return;
        }

        var chart = data.Chart;
        var doughnut = plot.Plot.Type == ChartType.Doughnut;
        var rings = doughnut ? plot.Series : [plot.Series[0]];
        var anyLabels = rings.Any(entry => LabelsOf(plot.Plot, entry.Series) is { IsVisible: true });
        var lineHeight = canvas.LineHeight(LabelSize);
        var explosion = rings.Max(entry => entry.Series.Explosion) / 100.0;
        var radius = Math.Min(area.Width, area.Height) / 2 / (1 + explosion);
        if (anyLabels && !doughnut)
        {
            radius = Math.Max(radius * 0.5, radius - lineHeight * 1.4);
        }

        radius = Math.Max(1, radius);
        var centerX = area.CenterX;
        var centerY = area.CenterY;
        var hole = doughnut ? Math.Clamp(plot.Plot.HoleSize, 10, 90) / 100.0 * radius : 0;
        var ringWidth = (radius - hole) / rings.Count;
        var border = data.Chart.RoundedCorners ? 0 : canvas.LineWidth(0.75);
        var startAngle = (plot.Plot.FirstSliceAngle - 90) * Math.PI / 180;
        var labelBoxes = new List<Box>();
        var labels = new List<Action>();
        for (var ring = 0; ring < rings.Count; ring++)
        {
            var entry = rings[ring];
            var values = entry.Values.Select(value => Math.Abs(value ?? 0)).ToArray();
            var total = values.Sum();
            if (total <= 0)
            {
                continue;
            }

            var inner = doughnut ? hole + ring * ringWidth : 0;
            var outer = doughnut ? inner + ringWidth : radius;
            var angle = startAngle;
            var slices = values.Count(value => value > 0);
            for (var index = 0; index < values.Length; index++)
            {
                if (values[index] <= 0)
                {
                    continue;
                }

                var sweep = values[index] / total * 2 * Math.PI;
                var middle = angle + sweep / 2;
                var shift = (entry.Series.Explosion / 100.0) * radius;
                var cx = centerX + Math.Cos(middle) * shift;
                var cy = centerY + Math.Sin(middle) * shift;
                var color = PointColor(chart, entry, index, values.Length, canvas.Options.Accents);
                var path = new PathBuilder();
                if (slices == 1 && !doughnut)
                {
                    path.Circle(cx, cy, outer);
                }
                else if (doughnut)
                {
                    path.Arc(cx, cy, outer, angle, angle + sweep, startFigure: true);
                    path.Arc(cx, cy, inner, angle + sweep, angle, startFigure: false);
                    path.Close();
                }
                else
                {
                    path.MoveTo(cx, cy);
                    path.Arc(cx, cy, outer, angle, angle + sweep, startFigure: false);
                    path.Close();
                }

                canvas.Path(path, color, border > 0 ? canvas.Options.Background : null, border, LineJoin.Round);
                if (LabelsOf(plot.Plot, entry.Series) is { IsVisible: true } dataLabels)
                {
                    var text = LabelText(canvas.Options.Formatter, data, entry, index, entry.Values[index] ?? 0, values[index] / total, dataLabels);
                    var (labelCx, labelCy, labelInner, labelOuter, labelMiddle) = (cx, cy, inner, outer, middle);
                    labels.Add(() => PlaceSliceLabel(canvas, text, labelCx, labelCy, labelInner, labelOuter, labelMiddle, sweep, dataLabels.Position, doughnut, color, labelBoxes));
                }

                angle += sweep;
            }
        }

        foreach (var label in labels)
        {
            label();
        }
    }

    private static void PlaceSliceLabel(ChartCanvas canvas, string text, double cx, double cy, double inner, double outer, double middle, double sweep,
        DataLabelPosition position, bool doughnut, RgbColor color, List<Box> placed)
    {
        var line = canvas.Measure(text, LabelSize);
        var (cos, sin) = (Math.Cos(middle), Math.Sin(middle));
        var insideRadius = doughnut ? (inner + outer) / 2 : outer * (position == DataLabelPosition.Center ? 0.5 : 0.65);
        var chord = 2 * insideRadius * Math.Sin(Math.Min(Math.PI / 2, sweep / 2));
        var fitsInside = line.Width <= chord * 1.1 && line.Height <= (outer - inner) * 0.9;
        var outside = position is DataLabelPosition.OutsideEnd || (!doughnut && position is DataLabelPosition.BestFit or DataLabelPosition.Auto && !fitsInside);
        Box box;
        if (outside)
        {
            var gap = canvas.Pt(4);
            var x = cx + cos * (outer + gap);
            var y = cy + sin * (outer + gap);
            box = new Box(cos >= 0 ? x : x - line.Width, y - line.Height / 2 + sin * line.Height / 2, line.Width, line.Height);
        }
        else
        {
            box = new Box(cx + cos * insideRadius - line.Width / 2, cy + sin * insideRadius - line.Height / 2, line.Width, line.Height);
        }

        if (placed.Any(other => other.Intersects(box)))
        {
            return;
        }

        placed.Add(box);
        canvas.Text(text, box.X, box.Y, LabelSize, outside ? ChartColors.LabelText : ChartColors.ContrastText(color));
    }

    // ===== Labels =====

    internal static DataLabels? LabelsOf(ChartPlot plot, ChartSeries series) => series.Labels ?? plot.Labels;

    /// <summary>The text of a data label: series name, category, value and percentage as asked, separated by "; ".</summary>
    internal static string LabelText(IChartValueFormatter formatter, ResolvedChart data, ResolvedSeries series, int index, double value, double? percent, DataLabels labels)
    {
        var parts = new List<string>(4);
        if (labels.ShowSeriesName)
        {
            parts.Add(series.Name);
        }

        if (labels.ShowCategory && index < data.Categories.Count)
        {
            parts.Add(data.Categories[index]);
        }


        if (labels.ShowValue)
        {
            parts.Add(formatter.Format(value, labels.NumberFormat ?? series.FormatCode));
        }

        if (labels.ShowPercent && percent is { } share)
        {
            parts.Add(formatter.Format(share, "0%"));
        }

        return string.Join("; ", parts);
    }
}
