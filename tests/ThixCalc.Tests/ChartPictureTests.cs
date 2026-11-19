using SkiaSharp;
using ThixCalc.Editor;
using ThixOffice.Charts;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;

namespace ThixCalc.Tests;

/// <summary>
/// Every chart type drawn with the real fonts into a picture (Excel's default size, 5 × 3 inches, at 2× for a sharp look).
/// THIXCALC_SCREENSHOTS=&lt;folder&gt; keeps them in &lt;folder&gt;/charts – look at them next to Excel's.
/// </summary>
public sealed class ChartPictureTests
{
    private static readonly string[] Months = ["Januar", "Februar", "März", "April", "Mai", "Juni"];

    private static SeriesSpec Series(string name, IReadOnlyList<string> categories, params double?[] values) =>
        new(ChartText.Of(name), ChartDataRef.LiteralText(categories), ChartDataRef.Literal(values));

    public static IEnumerable<(string Name, Chart Chart)> Samples()
    {
        var budget = new[]
        {
            Series("Einnahmen", Months, 3870, 3830, 3960, 3980, 3910, 4060),
            Series("Ausgaben", Months, 2988, 3020, 2986, 3417, 3085, 3151),
            Series("Überschuss", Months, 882, 810, 974, 563, 825, 909),
        };
        foreach (var template in ChartTemplates.All)
        {
            var series = template.Type is ChartType.Pie or ChartType.Doughnut
                ? [Series("Ausgaben", ["Miete", "Strom", "Lebensmittel", "Versicherungen", "Mobilität", "Freizeit", "Sparen"], 1150, 86, 612, 214, 185, 240, 500)]
                : template.Type == ChartType.Scatter
                    ? [new SeriesSpec(ChartText.Of("Messung"), ChartDataRef.Literal([1, 2, 3, 4, 5, 6, 7, 8]), ChartDataRef.Literal([2.1, 3.9, 6.2, 7.8, 10.1, 12.2, 13.8, 16.1]))]
                    : budget;
            yield return (template.Id, ChartFactory.Create(template, series));
        }

        var labelled = ChartEdits.SetDataLabels(ChartFactory.Create(ChartTemplates.Find("column")!, budget.Take(2).ToList()), true);
        yield return ("column-labels", ChartEdits.SetAxisTitle(ChartEdits.SetAxisTitle(labelled, AxisRole.PrimaryVertical, true, "Euro"), AxisRole.PrimaryHorizontal, true, "Monat"));
        yield return ("pie-labels", ChartEdits.SetDataLabels(ChartFactory.Create(ChartTemplates.Find("pie")!, [Series("Ausgaben", ["Miete", "Strom", "Lebensmittel", "Versicherungen", "Mobilität", "Freizeit", "Sparen"], 1150, 86, 612, 214, 185, 240, 500)]), true, DataLabelPosition.BestFit));
        var longNames = Enumerable.Range(1, 14).Select(index => $"Filiale Nummer {index}").ToArray();
        yield return ("column-long-categories", ChartFactory.Create(ChartTemplates.Find("column")!, [Series("Umsatz", longNames, longNames.Select((_, index) => (double?)(1000 + index * 137 % 900)).ToArray())]));
        yield return ("bar-legend-right", ChartEdits.SetLegend(ChartFactory.Create(ChartTemplates.Find("bar")!, budget), LegendPosition.Right));
        yield return ("unsupported", new Chart { UnsupportedType = "Netz" });
    }

    [Fact]
    public void Every_type_draws_a_picture()
    {
        var folder = Environment.GetEnvironmentVariable("THIXCALC_SCREENSHOTS") is { Length: > 0 } kept ? Path.Combine(kept, "charts") : null;
        if (folder is not null)
        {
            Directory.CreateDirectory(folder);
        }

        const double scale = 2;
        foreach (var (name, chart) in Samples())
        {
            var data = ChartDataResolver.Resolve(chart, null);
            var items = ChartRenderer.Render(data, new ChartRenderOptions
            {
                Measurer = CalcFonts.Skia,
                FontFamily = "Calibri",
                Width = 360 * scale,
                Height = 216 * scale,
                Scale = scale,
                PixelSize = 1,
            });
            using var surface = SKSurface.Create(new SKImageInfo((int)(360 * scale), (int)(216 * scale)));
            surface.Canvas.Clear(SKColors.White);
            SkiaPageRenderer.Draw(surface.Canvas, items, CalcFonts.Skia);
            using var image = surface.Snapshot();
            using var png = image.Encode(SKEncodedImageFormat.Png, 100);
            Assert.True(png.Size > 3000, $"{name} is (nearly) empty");
            Assert.Contains(items, item => item is GlyphRunItem);
            if (folder is not null)
            {
                File.WriteAllBytes(Path.Combine(folder, name + ".png"), png.ToArray());
            }
        }
    }
}
