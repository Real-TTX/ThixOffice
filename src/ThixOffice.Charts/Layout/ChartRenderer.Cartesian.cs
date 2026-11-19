using ThixOffice.Text.Drawing;

namespace ThixOffice.Charts;

/// <summary>The labels and scale of a value axis.</summary>
internal sealed record ValueAxisLayout(ChartAxis? Axis, AxisScale Scale, IReadOnlyList<(double Value, string Text)> Labels, double LabelWidth, double LabelSize, bool Visible);

/// <summary>The labels of a category axis: where each sits (fraction along the axis), its lines, rotation and the space they take.</summary>
internal sealed record CategoryLabels(IReadOnlyList<(double Position, IReadOnlyList<string> Lines)> Labels, double Rotation, double Extent, double Size, bool Visible);

/// <summary>Where the categories sit along their axis.</summary>
internal sealed class CategoryGeometry
{
    public required int Count { get; init; }

    /// <summary>The number of slots (categories, or base units of a time axis).</summary>
    public required double Slots { get; init; }

    /// <summary>The slot (0-based, may be fractional on a time axis) of category <c>i</c>.</summary>
    public required Func<int, double> SlotOf { get; init; }

    /// <summary>Points sit on the tick marks (line/area "Auf Teilstrichen") instead of between them.</summary>
    public bool OnTicks { get; init; }

    public bool Reverse { get; init; }

    /// <summary>The fraction 0–1 along the axis of the centre of category <paramref name="index"/>.</summary>
    public double Center(int index) => Fraction(SlotOf(index) + (OnTicks ? 0 : 0.5));

    public double Fraction(double slot)
    {
        var fraction = OnTicks ? (Slots <= 1 ? 0.5 : slot / (Slots - 1)) : slot / Slots;
        return Reverse ? 1 - fraction : fraction;
    }

    /// <summary>The width of one slot as a fraction of the axis.</summary>
    public double SlotWidth => OnTicks ? 1 / Math.Max(1, Slots - 1) : 1 / Math.Max(1, Slots);
}

public static partial class ChartRenderer
{
    private static void DrawCartesian(ChartCanvas canvas, ResolvedChart data, Box area)
    {
        var chart = data.Chart;
        var plots = data.Plots.Where(plot => !plot.Plot.IsRound).ToList();
        var primary = plots[0];
        var (categoryAxis, valueAxis) = chart.AxesOf(primary.Plot);
        var secondaryPlots = plots.Where(plot => !SameValueAxis(plot.Plot, primary.Plot)).ToList();
        var primaryPlots = plots.Where(plot => !secondaryPlots.Contains(plot)).ToList();
        var secondaryAxis = secondaryPlots.Count > 0 ? chart.AxesOf(secondaryPlots[0].Plot).Value : null;
        var horizontal = primary.Plot.IsHorizontal;
        var scatter = primary.Plot.Type == ChartType.Scatter;
        var geometry = Geometry(data, categoryAxis, valueAxis, primary.Plot);

        var labelSize = valueAxis?.FontSize ?? LabelSize;
        var lineHeight = canvas.LineHeight(labelSize);
        var gap = canvas.Pt(4);

        // Axis titles take their room first.
        var plot = area;
        var valueTitle = AxisTitleText(data, valueAxis);
        var categoryTitle = AxisTitleText(data, categoryAxis);
        var secondaryTitle = AxisTitleText(data, secondaryAxis);
        var titleHeight = canvas.LineHeight(AxisTitleSize) + canvas.Pt(5);
        var (leftTitle, bottomTitle) = horizontal ? (categoryTitle, valueTitle) : (valueTitle, categoryTitle);
        if (leftTitle is not null)
        {
            plot = plot with { X = plot.X + titleHeight, Width = plot.Width - titleHeight };
        }

        if (secondaryTitle is not null)
        {
            plot = plot with { Width = plot.Width - titleHeight };
        }

        if (bottomTitle is not null)
        {
            plot = plot with { Height = plot.Height - titleHeight };
        }

        // Half a label above the top tick and room for the axes' labels.
        var top = lineHeight / 2;
        plot = plot with { Y = plot.Y + top, Height = plot.Height - top };
        var primaryRange = ValueRange(primaryPlots, data.CategoryCount);
        var secondaryRange = ValueRange(secondaryPlots, data.CategoryCount);
        var xRange = scatter ? XRange(primaryPlots, data.CategoryCount) : default;
        Box inner;
        ValueAxisLayout values;
        ValueAxisLayout? secondary = null;
        ValueAxisLayout? xValues = null;
        CategoryLabels? categories = null;
        if (horizontal)
        {
            // Bars: categories at the left, values along the bottom.
            var categoryVisible = categoryAxis is { Deleted: false, HideLabels: false };
            var categoryWidth = categoryVisible ? Math.Min(plot.Width * 0.4, data.Categories.Select(text => canvas.Measure(text, categoryAxis?.FontSize ?? LabelSize).Width).DefaultIfEmpty(0).Max()) + gap : 0;
            var valueVisible = valueAxis is { Deleted: false, HideLabels: false };
            var bottom = valueVisible ? lineHeight + gap : 0;
            var width = plot.Width - categoryWidth;
            values = BuildValueAxis(canvas, valueAxis, primaryRange, width, vertical: false, primary);
            var rightPad = values.Visible ? values.LabelWidth / 2 : canvas.Pt(2);
            inner = new Box(plot.X + categoryWidth, plot.Y, Math.Max(1, plot.Width - categoryWidth - rightPad), Math.Max(1, plot.Height - bottom));
            values = BuildValueAxis(canvas, valueAxis, primaryRange, inner.Width, vertical: false, primary);
            categories = LayoutVerticalCategories(canvas, data, categoryAxis, geometry, inner.Height, categoryWidth - gap);
        }
        else
        {
            var categoryVisible = categoryAxis is { Deleted: false, HideLabels: false };
            var estimate = categoryVisible ? lineHeight + gap : 0;
            values = BuildValueAxis(canvas, valueAxis, primaryRange, plot.Height - estimate, vertical: true, primary);
            if (secondaryPlots.Count > 0)
            {
                secondary = BuildValueAxis(canvas, secondaryAxis, secondaryRange, plot.Height - estimate, vertical: true, secondaryPlots[0]);
            }

            var left = values.Visible ? values.LabelWidth + gap : 0;
            var right = secondary is { Visible: true } ? secondary.LabelWidth + gap : canvas.Pt(2);
            var width = Math.Max(1, plot.Width - left - right);
            double bottom;
            if (scatter)
            {
                xValues = BuildValueAxis(canvas, categoryAxis, xRange, width, vertical: false, primary, forX: true);
                bottom = xValues.Visible ? lineHeight + gap : 0;
                if (xValues.Visible)
                {
                    right = Math.Max(right, xValues.LabelWidth / 2);
                    width = Math.Max(1, plot.Width - left - right);
                }
            }
            else
            {
                categories = LayoutHorizontalCategories(canvas, data, categoryAxis, geometry, width, plot.Height * 0.45);

                // Slanted labels reach left of their tick: the plot moves right until the first ones fit in the chart.
                var overhang = LeftOverhang(canvas, categories, width) - left - (plot.X - area.X) - canvas.Pt(5);
                if (overhang > 0)
                {
                    left += overhang;
                    width = Math.Max(1, plot.Width - left - right);
                    categories = LayoutHorizontalCategories(canvas, data, categoryAxis, geometry, width, plot.Height * 0.45);
                }

                bottom = categories.Visible ? categories.Extent + gap : 0;
            }

            inner = new Box(plot.X + left, plot.Y, width, Math.Max(1, plot.Height - bottom));

            // Second pass: the real height may allow more or fewer ticks.
            values = BuildValueAxis(canvas, valueAxis, primaryRange, inner.Height, vertical: true, primary);
            if (secondaryPlots.Count > 0)
            {
                secondary = BuildValueAxis(canvas, secondaryAxis, secondaryRange, inner.Height, vertical: true, secondaryPlots[0]);
            }

            if (scatter)
            {
                xValues = BuildValueAxis(canvas, categoryAxis, xRange, inner.Width, vertical: false, primary, forX: true);
            }
        }

        DrawGridlines(canvas, inner, values, horizontal, valueAxis);
        if (xValues is not null && categoryAxis is { MajorGridlines: true })
        {
            DrawGridlines(canvas, inner, xValues, horizontal: true, categoryAxis);
        }
        else if (categoryAxis is { MajorGridlines: true } && !scatter)
        {
            DrawCategoryGridlines(canvas, inner, geometry, horizontal);
        }

        var labels = new List<Box>();
        var context = new PlotContext(canvas, data, inner, geometry, horizontal, labels);
        foreach (var resolved in plots.OrderBy(entry => DrawOrder(entry.Plot.Type)))
        {
            var scale = secondaryPlots.Contains(resolved) && secondary is not null ? secondary : values;
            switch (resolved.Plot.Type)
            {
                case ChartType.Column or ChartType.Bar:
                    DrawBars(context, resolved, scale, plots.Count(entry => entry.Plot.Type == resolved.Plot.Type && SameValueAxis(entry.Plot, resolved.Plot)) > 1 ? plots : [resolved]);
                    break;
                case ChartType.Area:
                    DrawAreas(context, resolved, scale);
                    break;
                case ChartType.Line:
                    DrawLines(context, resolved, scale);
                    break;
                case ChartType.Scatter:
                    DrawScatter(context, resolved, scale, xValues ?? values);
                    break;
            }
        }

        // The category axis line where the value axis crosses (zero, or the scale's end).
        if (categoryAxis is { Deleted: false } && !scatter)
        {
            var crossing = Crossing(values, valueAxis);
            if (horizontal)
            {
                canvas.VerticalLine(ValueX(inner, values.Scale, crossing, valueAxis), inner.Y, inner.Bottom, ChartColors.Gridline, 0.75);
            }
            else
            {
                canvas.HorizontalLine(inner.X, inner.Right, ValueY(inner, values.Scale, crossing, valueAxis), ChartColors.Gridline, 0.75);
            }
        }
        else if (scatter && categoryAxis is { Deleted: false })
        {
            canvas.HorizontalLine(inner.X, inner.Right, ValueY(inner, values.Scale, Crossing(values, valueAxis), valueAxis), ChartColors.Gridline, 0.75);
        }

        DrawValueLabels(canvas, inner, values, horizontal ? AxisPosition.Bottom : AxisPosition.Left, valueAxis);
        if (secondary is not null)
        {
            DrawValueLabels(canvas, inner, secondary, AxisPosition.Right, secondaryAxis);
        }

        if (xValues is not null)
        {
            DrawValueLabels(canvas, inner, xValues, AxisPosition.Bottom, categoryAxis);
        }

        if (categories is not null)
        {
            DrawCategoryLabels(canvas, inner, categories, horizontal, gap);
        }

        // Axis titles.
        var titleColor = ChartColors.Text;
        if (leftTitle is not null)
        {
            var titleAxis = horizontal ? categoryAxis : valueAxis;
            canvas.RotatedText(Fit(canvas, leftTitle, titleAxis?.Title?.FontSize ?? AxisTitleSize, inner.Height), area.X, inner.CenterY, -90, titleAxis?.Title?.FontSize ?? AxisTitleSize, titleColor, TextAlign.Center);
        }

        if (secondaryTitle is not null)
        {
            canvas.RotatedText(Fit(canvas, secondaryTitle, AxisTitleSize, inner.Height), area.Right, inner.CenterY, 90, AxisTitleSize, titleColor, TextAlign.Center);
        }

        if (bottomTitle is not null)
        {
            var titleAxis = horizontal ? valueAxis : categoryAxis;
            var size = titleAxis?.Title?.FontSize ?? AxisTitleSize;
            canvas.Text(Fit(canvas, bottomTitle, size, inner.Width), inner.CenterX, area.Bottom - canvas.LineHeight(size), size, titleColor, TextAlign.Center);
        }

        foreach (var label in context.PendingLabels)
        {
            label();
        }
    }

    private static int DrawOrder(ChartType type) => type switch
    {
        ChartType.Area => 0,
        ChartType.Column or ChartType.Bar => 1,
        ChartType.Line => 2,
        _ => 3,
    };

    private static bool SameValueAxis(ChartPlot first, ChartPlot second) =>
        first.AxisIds.Count < 2 || second.AxisIds.Count < 2 || first.AxisIds[1] == second.AxisIds[1];

    private static string? AxisTitleText(ResolvedChart data, ChartAxis? axis)
    {
        if (axis is not { Title: { } title, Deleted: false })
        {
            return null;
        }

        return title.Text is null ? "Achsentitel" : data.TextOf(title.Text) ?? string.Empty;
    }

    // ===== Geometry and scales =====

    private static CategoryGeometry Geometry(ResolvedChart data, ChartAxis? axis, ChartAxis? valueAxis, ChartPlot plot)
    {
        var count = Math.Max(1, data.CategoryCount);

        // The value axis says where it crosses: between the categories, or on them (midCat: lines and areas reach the edges).
        var onTicks = valueAxis is { CrossBetween: false } && plot.Type is ChartType.Line or ChartType.Area;
        var reverse = axis?.Reverse ?? false;
        if (axis is { Kind: AxisKind.Date } && data.CategoryNumbers.Count == data.CategoryCount && data.CategoryCount > 0 && data.CategoryNumbers.All(number => number is not null))
        {
            var numbers = data.CategoryNumbers.Select(number => number!.Value).ToList();
            var unit = AxisScaling.BaseUnit(numbers);
            var origin = numbers.Min();
            var span = AxisScaling.DateOffset(numbers.Max(), origin, unit);
            return new CategoryGeometry
            {
                Count = count,
                Slots = span + 1,
                SlotOf = index => index < numbers.Count ? AxisScaling.DateOffset(numbers[index], origin, unit) : index,
                OnTicks = onTicks,
                Reverse = reverse,
            };
        }

        return new CategoryGeometry { Count = count, Slots = count, SlotOf = index => index, OnTicks = onTicks, Reverse = reverse };
    }

    /// <summary>The smallest and largest value a value axis must show (stacked plots by their sums), and whether it is a 100 % axis.</summary>
    private static (double Minimum, double Maximum, bool Percent, bool HasNegative, bool Any) ValueRange(IEnumerable<ResolvedPlot> plots, int categories)
    {
        var minimum = double.MaxValue;
        var maximum = double.MinValue;
        var percent = false;
        var negative = false;
        var any = false;
        foreach (var plot in plots)
        {
            var series = plot.Series;
            if (plot.Plot.Grouping == ChartGrouping.PercentStacked && plot.Plot.Type != ChartType.Scatter)
            {
                percent = true;
                negative |= series.Any(entry => entry.Values.Any(value => value < 0));
                any = true;
                continue;
            }

            if (plot.Plot.Grouping == ChartGrouping.Stacked && plot.Plot.Type != ChartType.Scatter)
            {
                for (var index = 0; index < categories; index++)
                {
                    var positive = 0.0;
                    var negatives = 0.0;
                    foreach (var entry in series)
                    {
                        if (index < entry.Values.Length && entry.Values[index] is { } value)
                        {
                            any = true;
                            if (value >= 0)
                            {
                                positive += value;
                            }
                            else
                            {
                                negatives += value;
                            }
                        }
                    }

                    minimum = Math.Min(minimum, negatives);
                    maximum = Math.Max(maximum, positive);
                }

                continue;
            }

            foreach (var value in series.SelectMany(entry => entry.Values))
            {
                if (value is { } number)
                {
                    any = true;
                    minimum = Math.Min(minimum, number);
                    maximum = Math.Max(maximum, number);
                }
            }
        }

        if (!any || minimum == double.MaxValue)
        {
            return (0, 1, percent, negative, any);
        }

        return (minimum, maximum, percent, negative || minimum < 0, any);
    }

    private static (double Minimum, double Maximum, bool Percent, bool HasNegative, bool Any) XRange(IEnumerable<ResolvedPlot> plots, int categories)
    {
        var values = plots.SelectMany(plot => plot.Series).SelectMany(series => series.XValues ?? Enumerable.Range(1, categories).Select(index => (double?)index).ToArray()).Where(value => value is not null).Select(value => value!.Value).ToList();
        return values.Count == 0 ? (0, 1, false, false, false) : (values.Min(), values.Max(), false, values.Min() < 0, true);
    }

    private static ValueAxisLayout BuildValueAxis(ChartCanvas canvas, ChartAxis? axis, (double Minimum, double Maximum, bool Percent, bool HasNegative, bool Any) range, double length, bool vertical, ResolvedPlot plot, bool forX = false)
    {
        var size = axis?.FontSize ?? LabelSize;
        var lineHeight = canvas.LineHeight(size);
        var visible = axis is { Deleted: false, HideLabels: false };
        var format = axis is { SourceLinked: false, NumberFormat: { } own } ? own
            : range.Percent ? "0%"
            : (forX ? plot.Series.Select(series => series.XFormatCode).FirstOrDefault(code => code is not null) : plot.Series.Select(series => series.FormatCode).FirstOrDefault(code => code is not null)) ?? axis?.NumberFormat ?? "General";
        var formatter = canvas.Options.Formatter;

        AxisScale ScaleFor(int intervals) => range.Percent
            ? AxisScaling.PercentScale(range.HasNegative, intervals)
            : AxisScaling.ValueScale(range.Minimum, range.Maximum, intervals, axis?.Minimum, axis?.Maximum, axis?.MajorUnit);

        List<(double Value, string Text)> LabelsOf(AxisScale scale) => scale.Ticks.Select(value => (value, formatter.Format(value, format))).ToList();

        // Excel's density: about one gridline per 1.3 label heights (a default 5 × 3 inch chart gets 6–9 intervals).
        var maxIntervals = vertical ? (int)Math.Floor(length / (lineHeight * 1.3)) : 10;
        maxIntervals = Math.Clamp(maxIntervals, 1, 10);
        var scale = ScaleFor(maxIntervals);
        var labels = LabelsOf(scale);
        if (!vertical)
        {
            // Horizontal: fewer ticks until the labels fit side by side.
            for (var attempt = 0; attempt < 8 && maxIntervals > 1; attempt++)
            {
                var widest = labels.Max(label => canvas.Measure(label.Text, size).Width);
                if ((widest + canvas.Pt(8)) * (labels.Count - 1) <= length)
                {
                    break;
                }

                maxIntervals = Math.Max(1, Math.Min(maxIntervals - 1, (int)(length / (widest + canvas.Pt(8)))));
                scale = ScaleFor(maxIntervals);
                labels = LabelsOf(scale);
            }
        }

        var width = labels.Count == 0 ? 0 : labels.Max(label => canvas.Measure(label.Text, size).Width);
        return new ValueAxisLayout(axis, scale, labels, width, size, visible);
    }

    private static double Crossing(ValueAxisLayout values, ChartAxis? axis)
    {
        var scale = values.Scale;
        return axis?.Crosses switch
        {
            AxisCrossing.Minimum => scale.Minimum,
            AxisCrossing.Maximum => scale.Maximum,
            AxisCrossing.Value when axis.CrossesAt is { } at => Math.Clamp(at, scale.Minimum, scale.Maximum),
            _ => Math.Clamp(0, scale.Minimum, scale.Maximum),
        };
    }

    internal static double ValueY(Box inner, AxisScale scale, double value, ChartAxis? axis)
    {
        var fraction = scale.Fraction(value);
        return axis?.Reverse == true ? inner.Y + fraction * inner.Height : inner.Bottom - fraction * inner.Height;
    }

    internal static double ValueX(Box inner, AxisScale scale, double value, ChartAxis? axis)
    {
        var fraction = scale.Fraction(value);
        return axis?.Reverse == true ? inner.Right - fraction * inner.Width : inner.X + fraction * inner.Width;
    }

    // ===== Gridlines and labels =====

    private static void DrawGridlines(ChartCanvas canvas, Box inner, ValueAxisLayout values, bool horizontal, ChartAxis? axis)
    {
        if (axis is not { MajorGridlines: true })
        {
            return;
        }

        foreach (var (value, _) in values.Labels)
        {
            if (horizontal)
            {
                canvas.VerticalLine(ValueX(inner, values.Scale, value, axis), inner.Y, inner.Bottom, ChartColors.Gridline, 0.75);
            }
            else
            {
                canvas.HorizontalLine(inner.X, inner.Right, ValueY(inner, values.Scale, value, axis), ChartColors.Gridline, 0.75);
            }
        }
    }

    private static void DrawCategoryGridlines(ChartCanvas canvas, Box inner, CategoryGeometry geometry, bool horizontal)
    {
        var slots = (int)Math.Round(geometry.Slots);
        for (var slot = 0; slot <= slots; slot++)
        {
            var fraction = geometry.OnTicks ? geometry.Fraction(slot) : (geometry.Reverse ? 1 - slot / geometry.Slots : slot / geometry.Slots);
            if (horizontal)
            {
                canvas.HorizontalLine(inner.X, inner.Right, inner.Bottom - fraction * inner.Height, ChartColors.Gridline, 0.75);
            }
            else
            {
                canvas.VerticalLine(inner.X + fraction * inner.Width, inner.Y, inner.Bottom, ChartColors.Gridline, 0.75);
            }
        }
    }

    private static void DrawValueLabels(ChartCanvas canvas, Box inner, ValueAxisLayout values, AxisPosition position, ChartAxis? axis)
    {
        if (!values.Visible)
        {
            return;
        }

        var size = values.LabelSize;
        var lineHeight = canvas.LineHeight(size);
        var gap = canvas.Pt(4);
        foreach (var (value, text) in values.Labels)
        {
            switch (position)
            {
                case AxisPosition.Bottom:
                    canvas.Text(text, ValueX(inner, values.Scale, value, axis), inner.Bottom + gap, size, ChartColors.Text, TextAlign.Center);
                    break;
                case AxisPosition.Right:
                    canvas.Text(text, inner.Right + gap, ValueY(inner, values.Scale, value, axis) - lineHeight / 2, size, ChartColors.Text);
                    break;
                default:
                    canvas.Text(text, inner.X - gap, ValueY(inner, values.Scale, value, axis) - lineHeight / 2, size, ChartColors.Text, TextAlign.Right);
                    break;
            }
        }
    }

    /// <summary>The texts of the category axis: the categories, or for a time axis the dates at its major unit.</summary>
    private static List<(double Position, string Text)> CategoryTexts(ChartCanvas canvas, ResolvedChart data, ChartAxis? axis, CategoryGeometry geometry, double length, double size)
    {
        var texts = new List<(double Position, string Text)>();
        if (axis is { Kind: AxisKind.Date } && data.CategoryCount > 1 && data.CategoryNumbers.All(number => number is not null))
        {
            var numbers = data.CategoryNumbers.Where(number => number is not null).Select(number => number!.Value).ToList();
            if (numbers.Count > 0)
            {
                var unit = AxisScaling.BaseUnit(numbers);
                var origin = numbers.Min();
                var span = AxisScaling.DateOffset(numbers.Max(), origin, unit);
                var format = axis is { SourceLinked: false, NumberFormat: { } own } ? own : data.CategoryFormat;
                var sample = canvas.Options.Formatter.Format(numbers.Max(), format);
                var maxLabels = Math.Max(1, (int)(length / (canvas.Measure(sample, size).Width + canvas.Pt(8))));
                var major = axis.MajorUnit is > 0 ? (int)axis.MajorUnit.Value : AxisScaling.DateMajorUnit(span, unit, maxLabels);
                for (var offset = 0.0; offset <= span + 1e-9; offset += major)
                {
                    var serial = AxisScaling.AddUnits(origin, offset, unit);
                    texts.Add((geometry.Fraction(offset + (geometry.OnTicks ? 0 : 0.5)), canvas.Options.Formatter.Format(serial, format)));
                }

                return texts;
            }
        }

        for (var index = 0; index < data.CategoryCount; index++)
        {
            texts.Add((geometry.Center(index), index < data.Categories.Count ? data.Categories[index] : (index + 1).ToString(System.Globalization.CultureInfo.CurrentCulture)));
        }

        return texts;
    }

    /// <summary>Labels below the plot: one line if they fit, two lines wrapped at spaces, slanted at 45°, vertical, or every n-th.</summary>
    private static CategoryLabels LayoutHorizontalCategories(ChartCanvas canvas, ResolvedChart data, ChartAxis? axis, CategoryGeometry geometry, double width, double maxExtent)
    {
        var size = axis?.FontSize ?? LabelSize;
        var visible = axis is { Deleted: false, HideLabels: false };
        var texts = CategoryTexts(canvas, data, axis, geometry, width, size);
        var lineHeight = canvas.LineHeight(size);
        if (!visible || texts.Count == 0)
        {
            return new CategoryLabels([], 0, 0, size, false);
        }

        var spacing = texts.Count > 1 ? Math.Abs(texts[1].Position - texts[0].Position) * width : width;
        var widest = texts.Max(text => canvas.Measure(text.Text, size).Width);
        if (axis?.LabelRotation is null or 0 && widest <= spacing - canvas.Pt(3))
        {
            return new CategoryLabels(texts.Select(text => (text.Position, (IReadOnlyList<string>)[text.Text])).ToList(), 0, lineHeight, size, true);
        }

        if (axis?.LabelRotation is null && texts.Any(text => text.Text.Contains(' ', StringComparison.Ordinal)))
        {
            var wrapped = texts.Select(text => (text.Position, Lines: Wrap(canvas, text.Text, size, spacing - canvas.Pt(3), maxLines: 2))).ToList();
            var fits = wrapped.All(entry => entry.Lines.All(line => !line.EndsWith('…') && canvas.Measure(line, size).Width <= spacing - canvas.Pt(3)));
            if (fits)
            {
                var lines = wrapped.Max(entry => entry.Lines.Count);
                return new CategoryLabels(wrapped.Select(entry => (entry.Position, (IReadOnlyList<string>)entry.Lines)).ToList(), 0, lines * lineHeight, size, true);
            }
        }

        var rotation = axis?.LabelRotation is { } given and not 0 ? given : spacing >= lineHeight * 1.15 ? -45 : -90;
        var interval = Math.Max(1, (int)Math.Ceiling(lineHeight * (rotation == -45 ? 1.15 : 1.0) / Math.Max(0.1, spacing)));
        var radians = Math.Abs(rotation) * Math.PI / 180;
        var limit = Math.Max(lineHeight, (maxExtent - lineHeight * Math.Cos(radians)) / Math.Max(0.2, Math.Sin(radians)));
        var labels = new List<(double, IReadOnlyList<string>)>();
        var longest = 0.0;
        for (var index = 0; index < texts.Count; index += interval)
        {
            var text = Fit(canvas, texts[index].Text, size, limit);
            longest = Math.Max(longest, canvas.Measure(text, size).Width);
            labels.Add((texts[index].Position, [text]));
        }

        var extent = longest * Math.Sin(radians) + lineHeight * Math.Cos(radians);
        return new CategoryLabels(labels, rotation, extent, size, true);
    }

    /// <summary>How far slanted labels reach left of the plot (0 for straight ones).</summary>
    private static double LeftOverhang(ChartCanvas canvas, CategoryLabels categories, double width)
    {
        if (!categories.Visible || categories.Rotation == 0)
        {
            return 0;
        }

        var radians = Math.Abs(categories.Rotation) * Math.PI / 180;
        var lineHeight = canvas.LineHeight(categories.Size);
        return categories.Labels.Select(label => canvas.Measure(label.Lines[0], categories.Size).Width * Math.Cos(radians) + lineHeight * Math.Sin(radians) / 2 - label.Position * width).DefaultIfEmpty(0).Max();
    }

    /// <summary>Labels left of horizontal bars: right-aligned, shortened to the room, every n-th when the bars are thin.</summary>
    private static CategoryLabels LayoutVerticalCategories(ChartCanvas canvas, ResolvedChart data, ChartAxis? axis, CategoryGeometry geometry, double height, double width)
    {
        var size = axis?.FontSize ?? LabelSize;
        var visible = axis is { Deleted: false, HideLabels: false };
        if (!visible)
        {
            return new CategoryLabels([], 0, 0, size, false);
        }

        var texts = CategoryTexts(canvas, data, axis, geometry, height, size);
        var lineHeight = canvas.LineHeight(size);
        var spacing = texts.Count > 1 ? Math.Abs(texts[1].Position - texts[0].Position) * height : height;
        var interval = Math.Max(1, (int)Math.Ceiling(lineHeight / Math.Max(0.1, spacing)));
        var labels = new List<(double, IReadOnlyList<string>)>();
        for (var index = 0; index < texts.Count; index += interval)
        {
            labels.Add((texts[index].Position, [Fit(canvas, texts[index].Text, size, Math.Max(canvas.Pt(10), width))]));
        }

        return new CategoryLabels(labels, 0, width, size, true);
    }

    private static void DrawCategoryLabels(ChartCanvas canvas, Box inner, CategoryLabels categories, bool horizontal, double gap)
    {
        if (!categories.Visible)
        {
            return;
        }

        var size = categories.Size;
        var lineHeight = canvas.LineHeight(size);
        foreach (var (position, lines) in categories.Labels)
        {
            if (horizontal)
            {
                // Bars: the first category at the bottom (Excel's order), labels right-aligned beside the axis.
                var y = inner.Bottom - position * inner.Height;
                canvas.Text(lines[0], inner.X - gap, y - lineHeight / 2, size, ChartColors.Text, TextAlign.Right);
                continue;
            }

            var x = inner.X + position * inner.Width;
            if (categories.Rotation == 0)
            {
                for (var index = 0; index < lines.Count; index++)
                {
                    canvas.Text(lines[index], x, inner.Bottom + gap + index * lineHeight, size, ChartColors.Text, TextAlign.Center);
                }

                continue;
            }

            // Slanted or vertical: the text ends at the tick and runs down to the left.
            var rotation = categories.Rotation;
            var lift = Math.Abs(Math.Cos(rotation * Math.PI / 180)) * lineHeight / 2;
            canvas.RotatedText(lines[0], x, inner.Bottom + gap + lift, rotation, size, ChartColors.Text, TextAlign.Right, -lineHeight / 2);
        }
    }
}
