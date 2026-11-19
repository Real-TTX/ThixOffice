using System.Globalization;
using System.Text;
using ThixOffice.Charts;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Tests.Charts;

/// <summary>
/// The chart layout with the fixed-width measurer: deterministic, inside its rectangle, one shape per point, labels that do
/// not collide (wrapped, slanted or thinned out), legends that wrap, pies with one slice per value, placeholder frames.
/// </summary>
public class ChartRendererTests
{
    private static readonly string[] Months = ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"];

    internal static SeriesSpec Series(string name, IEnumerable<string> categories, params double?[] values) =>
        new(ChartText.Of(name), ChartDataRef.LiteralText(categories), ChartDataRef.Literal(values));

    internal static IReadOnlyList<DisplayItem> Render(Chart chart, double width = 360, double height = 216, double scale = 1)
    {
        var data = ChartDataResolver.Resolve(chart, null);
        return ChartRenderer.Render(data, new ChartRenderOptions
        {
            Measurer = new FixedWidthTextMeasurer(),
            Width = width * scale,
            Height = height * scale,
            Scale = scale,
            PixelSize = scale > 1 ? 1 : 0,
        });
    }

    /// <summary>A text form of the items (positions rounded) – two renderings are the same when these are.</summary>
    internal static string Describe(IEnumerable<DisplayItem> items)
    {
        var builder = new StringBuilder();
        string N(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
        foreach (var item in items)
        {
            builder.Append(item switch
            {
                GlyphRunItem run => $"T {N(run.X)},{N(run.Y)} {run.Text} {run.Color}",
                RectangleItem rect => $"R {N(rect.X)},{N(rect.Y)} {N(rect.Width)}x{N(rect.Height)} {rect.Fill} {rect.Stroke}",
                LineItem line => $"L {N(line.X1)},{N(line.Y1)} {N(line.X2)},{N(line.Y2)} {line.Color}",
                PathItem path => $"P {path.Verbs.Length} {string.Join(",", path.Points.Select(N))} {path.Fill} {path.Stroke}",
                TransformBeginItem transform => $"X {N(transform.X)},{N(transform.Y)} {N(transform.Rotation)}",
                _ => item.GetType().Name,
            }).Append('\n');
        }

        return builder.ToString();
    }

    private static Chart MonthlyColumns(int series = 2) =>
        ChartFactory.Create(ChartTemplates.Find("column")!, Enumerable.Range(0, series).Select(index => Series("Reihe " + (index + 1), Months, Months.Select((_, month) => (double?)(100 + month * 10 + index * 25)).ToArray())).ToList());

    [Fact]
    public void The_same_chart_renders_the_same_items()
    {
        var chart = MonthlyColumns();

        Assert.Equal(Describe(Render(chart)), Describe(Render(chart)));
    }

    [Fact]
    public void A_clustered_column_chart_has_one_bar_per_point_inside_the_chart()
    {
        var chart = MonthlyColumns(series: 2);
        var items = Render(chart);
        var accents = ChartRenderOptions.OfficeAccents;

        var bars = items.OfType<RectangleItem>().Where(rect => rect.Fill == accents[0] || rect.Fill == accents[1]).ToList();
        var legendBoxes = 2;
        Assert.Equal(24 + legendBoxes, bars.Count);
        Assert.All(items.OfType<RectangleItem>(), rect => Assert.True(rect.X >= -0.01 && rect.Right() <= 360.01 && rect.Y >= -0.01 && rect.Bottom() <= 216.01, $"{rect.X},{rect.Y} {rect.Width}x{rect.Height} outside"));
        Assert.All(Unrotated(items), run => Assert.True(run.X >= 0 && run.Y <= 216, $"{run.Text} at {run.X},{run.Y}"));
    }

    /// <summary>The texts outside transformations (their coordinates are the chart's).</summary>
    internal static IEnumerable<GlyphRunItem> Unrotated(IEnumerable<DisplayItem> items)
    {
        var depth = 0;
        foreach (var item in items)
        {
            switch (item)
            {
                case TransformBeginItem:
                    depth++;
                    break;
                case TransformEndItem:
                    depth--;
                    break;
                case GlyphRunItem run when depth == 0:
                    yield return run;
                    break;
            }
        }
    }

    [Fact]
    public void Value_labels_show_a_nice_scale_and_the_title_is_automatic()
    {
        var chart = MonthlyColumns(series: 2);
        var texts = Render(chart).OfType<GlyphRunItem>().Select(run => run.Text).ToList();

        Assert.Contains("Diagrammtitel", texts);
        Assert.Contains("0", texts);
        Assert.Contains("Reihe 1", texts);
        Assert.Contains("Reihe 2", texts);
        var numbers = texts.Select(text => double.TryParse(text, NumberStyles.Float, CultureInfo.GetCultureInfo("de-DE"), out var value) ? value : (double?)null).Where(value => value is not null).Select(value => value!.Value).ToList();
        Assert.True(numbers.Max() >= 235, "the axis reaches above the largest value");
    }

    [Fact]
    public void A_single_series_chart_is_titled_by_the_series_and_has_no_legend()
    {
        var chart = ChartFactory.Create(ChartTemplates.Find("column")!, [Series("Umsatz", ["A", "B", "C"], 1, 2, 3)]);

        Assert.Null(chart.Legend);
        var texts = Render(chart).OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.Equal(1, texts.Count(text => text == "Umsatz"));
    }

    [Fact]
    public void Category_labels_never_overlap()
    {
        foreach (var width in new[] { 600.0, 360, 220, 150 })
        {
            var chart = MonthlyColumns();
            var items = Render(chart, width, 216);
            var labels = items.OfType<GlyphRunItem>().Where(run => Months.Contains(run.Text) || run.Text.EndsWith('…')).ToList();
            if (items.OfType<TransformBeginItem>().Any())
            {
                continue; // slanted: spaced by their line height
            }

            var boxes = labels.Select(run => (run.Y, Left: run.X, Right: run.X + run.CharacterX.Length * 9 * FixedWidthTextMeasurer.CharacterWidth)).OrderBy(box => box.Left).ToList();
            for (var index = 1; index < boxes.Count; index++)
            {
                if (Math.Abs(boxes[index].Y - boxes[index - 1].Y) < 1)
                {
                    Assert.True(boxes[index].Left >= boxes[index - 1].Right, $"width {width}: labels overlap");
                }
            }
        }
    }

    [Fact]
    public void Long_category_labels_slant_in_a_narrow_chart()
    {
        var categories = Enumerable.Range(1, 12).Select(index => "Kategorie mit langem Namen " + index).ToArray();
        var chart = ChartFactory.Create(ChartTemplates.Find("column")!, [Series("Werte", categories, categories.Select((_, index) => (double?)index).ToArray())]);

        var items = Render(chart, 300, 260);

        Assert.Contains(items.OfType<TransformBeginItem>(), item => item.Rotation is -45 or -90);
    }

    [Fact]
    public void A_legend_of_many_series_wraps_into_rows()
    {
        var chart = ChartFactory.Create(ChartTemplates.Find("line")!, Enumerable.Range(1, 9).Select(index => Series("Datenreihe Nummer " + index, ["A", "B", "C"], index, index + 1, index + 2)).ToList());

        var legend = Render(chart).OfType<GlyphRunItem>().Where(run => run.Text.StartsWith("Datenreihe", StringComparison.Ordinal)).ToList();

        Assert.True(legend.Select(run => Math.Round(run.Y)).Distinct().Count() >= 2, "several rows");
    }

    [Fact]
    public void A_pie_has_one_slice_per_value_and_a_legend_of_categories()
    {
        var chart = ChartFactory.Create(ChartTemplates.Find("pie")!, [Series("Anteile", ["Nord", "Süd", "Ost", "West"], 10, 20, 30, 40)]);
        var items = Render(chart);

        Assert.Equal(4, items.OfType<PathItem>().Count(path => path.Fill is { } fill && ChartRenderOptions.OfficeAccents.Contains(fill)));
        var texts = items.OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.Contains("Nord", texts);
        Assert.Contains("West", texts);
    }

    [Fact]
    public void Data_labels_of_a_line_chart_avoid_each_other()
    {
        var chart = ChartFactory.Create(ChartTemplates.Find("line-markers")!, [Series("A", Months, Months.Select(_ => (double?)50).ToArray()), Series("B", Months, Months.Select(_ => (double?)51).ToArray())]);
        chart = ChartEdits.SetDataLabels(chart, true);

        var labels = Render(chart, 500, 300).OfType<GlyphRunItem>().Where(run => run.Text is "50" or "51").ToList();
        for (var first = 0; first < labels.Count; first++)
        {
            for (var second = first + 1; second < labels.Count; second++)
            {
                var a = labels[first];
                var b = labels[second];
                var overlapX = a.X < b.X + 9 && b.X < a.X + 9;
                var overlapY = Math.Abs(a.Y - b.Y) < 9;
                Assert.False(overlapX && overlapY, $"labels at {a.X},{a.Y} and {b.X},{b.Y} overlap");
            }
        }
    }

    [Fact]
    public void Every_type_renders_without_text_outside_the_chart()
    {
        foreach (var template in ChartTemplates.All)
        {
            var chart = ChartFactory.Create(template, [Series("Erste", ["A", "B", "C", "D"], 4, -2, 7, 3), Series("Zweite", ["A", "B", "C", "D"], 1, 5, null, 2)]);
            chart = ChartEdits.SetDataLabels(chart, true);
            var items = Render(chart);
            Assert.NotEmpty(items);
            Assert.All(Unrotated(items), run => Assert.True(run.Y > 0 && run.Y <= 216, $"{template.Id}: {run.Text} at {run.Y}"));
        }
    }

    [Fact]
    public void Gridlines_snap_to_device_pixels()
    {
        var chart = MonthlyColumns();
        var lines = Render(chart, scale: 1.5).OfType<LineItem>().Where(line => line.Y1 == line.Y2).ToList();

        Assert.NotEmpty(lines);
        Assert.All(lines, line => Assert.Equal(0.5, line.Y1 - Math.Floor(line.Y1), 6));
    }

    [Fact]
    public void An_unsupported_chart_shows_a_labelled_frame()
    {
        var chart = new Chart { UnsupportedType = "Netz" };

        var texts = Render(chart).OfType<GlyphRunItem>().Select(run => run.Text).ToList();

        Assert.Contains("Diagramm: Netz", texts);
    }

    [Fact]
    public void Hidden_points_are_left_out()
    {
        var categories = new ChartDataRef(null, [new ChartValue(null, "A"), new ChartValue(null, "B", IsHidden: true), new ChartValue(null, "C")]) { IsNumeric = false };
        var values = new ChartDataRef(null, [new ChartValue(1, null), new ChartValue(2, null, IsHidden: true), new ChartValue(3, null)]);
        var chart = ChartFactory.Create(ChartTemplates.Find("column")!, [new SeriesSpec(null, categories, values)]);

        var data = ChartDataResolver.Resolve(chart, null);

        Assert.Equal(["A", "C"], data.Categories);
        Assert.Equal([1, 3], data.Plots[0].Series[0].Values);
    }
}

internal static class RectangleExtensions
{
    public static double Right(this RectangleItem item) => item.X + item.Width;

    public static double Bottom(this RectangleItem item) => item.Y + item.Height;
}
