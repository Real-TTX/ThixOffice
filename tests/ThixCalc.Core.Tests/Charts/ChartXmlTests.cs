using ThixOffice.Charts;
using ThixOffice.Charts.Formats;
using static ThixCalc.Core.Tests.Charts.ChartRendererTests;

namespace ThixCalc.Core.Tests.Charts;

/// <summary>DrawingML chart parts: every type written and read back the same, what the model does not own kept, unknown types untouched.</summary>
public class ChartXmlTests
{
    private const string Namespaces = "xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";

    public static TheoryData<string> Templates()
    {
        var data = new TheoryData<string>();
        foreach (var template in ChartTemplates.All)
        {
            data.Add(template.Id);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Templates))]
    public void Every_type_survives_a_round_trip(string id)
    {
        var template = ChartTemplates.Find(id)!;
        var chart = ChartFactory.Create(template, [
            new SeriesSpec(ChartText.Ref("Tabelle1!$B$1", "Umsatz"), new ChartDataRef("Tabelle1!$A$2:$A$4", [new(null, "A"), new(null, "B"), new(null, "C")]) { IsNumeric = template.Type == ChartType.Scatter }, new ChartDataRef("Tabelle1!$B$2:$B$4", [new(1, null), new(2, null), new(3, null)])),
            new SeriesSpec(ChartText.Of("Kosten"), null, new ChartDataRef("Tabelle1!$C$2:$C$4", [new(4, null), new(5, null), new(6, null)])),
        ]);
        chart = ChartEdits.SetDataLabels(chart, true);

        var read = ChartXml.Read(ChartXmlWriter.Write(chart));

        Assert.True(read.IsSupported);
        Assert.Equal(chart.Plots.Select(plot => (plot.Type, plot.Grouping, plot.Series.Count)), read.Plots.Select(plot => (plot.Type, plot.Grouping, plot.Series.Count)));
        Assert.Equal(ChartEdits.References(chart), ChartEdits.References(read));
        Assert.Equal(ChartTemplates.Of(chart)?.Id, ChartTemplates.Of(read)?.Id);
        Assert.Equal(chart.Axes.Select(axis => (axis.Id, axis.Position, axis.Deleted, axis.MajorGridlines)), read.Axes.Select(axis => (axis.Id, axis.Position, axis.Deleted, axis.MajorGridlines)));
        Assert.Equal(chart.Legend?.Position, read.Legend?.Position);
        Assert.All(read.Plots, plot => Assert.True(plot.Labels?.ShowValue));
        Assert.Equal(Describe(Render(chart)), Describe(Render(read)));
    }

    [Fact]
    public void Titles_colours_and_options_round_trip()
    {
        var chart = ChartFactory.Create(ChartTemplates.Find("column-stacked")!, [Series("A", ["x", "y"], 1, 2), Series("B", ["x", "y"], 3, 4)]);
        chart = ChartEdits.SetTitle(chart, true, "Umsätze 2024");
        chart = ChartEdits.SetAxisTitle(chart, AxisRole.PrimaryVertical, true, "Euro");
        chart = ChartEdits.SetLegend(chart, LegendPosition.Right);
        chart = ChartEdits.SetColorSet(chart, 5);
        chart = chart with
        {
            DisplayBlanksAs = BlankCells.Span,
            Plots = [chart.Plots[0] with { Series = [chart.Plots[0].Series[0] with { Fill = ChartColor.FromRgb(ThixOffice.Text.Drawing.RgbColor.FromHex("123456")) }, chart.Plots[0].Series[1]] }],
        };

        var read = ChartXml.Read(ChartXmlWriter.Write(chart));

        Assert.Equal("Umsätze 2024", read.Title?.Text?.Literal);
        Assert.Equal("Euro", ChartEdits.AxisOf(read, AxisRole.PrimaryVertical)?.Title?.Text?.Literal);
        Assert.Equal(LegendPosition.Right, read.Legend?.Position);
        Assert.Equal(BlankCells.Span, read.DisplayBlanksAs);
        Assert.Equal("123456", read.Plots[0].Series[0].Fill?.Rgb?.ToHex());
        Assert.Equal(ChartColors.SeriesColor(5, 1, 2), read.Plots[0].Series[1].Fill);
    }

    [Fact]
    public void What_the_model_does_not_own_is_kept()
    {
        var xml = $"""
            <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
            <c:chartSpace {Namespaces}><c:date1904 val="0"/><c:lang val="en-US"/><c:roundedCorners val="0"/>
            <c:chart><c:autoTitleDeleted val="1"/><c:view3D><c:rotX val="15"/></c:view3D><c:plotArea><c:layout/>
            <c:barChart><c:barDir val="col"/><c:grouping val="clustered"/><c:varyColors val="0"/>
            <c:ser><c:idx val="0"/><c:order val="0"/><c:tx><c:v>Reihe</c:v></c:tx><c:spPr><a:gradFill><a:gsLst><a:gs pos="0"><a:srgbClr val="FF0000"/></a:gs></a:gsLst></a:gradFill></c:spPr><c:invertIfNegative val="0"/>
            <c:trendline><c:trendlineType val="linear"/><c:dispRSqr val="0"/><c:dispEq val="0"/></c:trendline>
            <c:val><c:numRef><c:f>Tabelle1!$A$1:$A$3</c:f><c:numCache><c:formatCode>General</c:formatCode><c:ptCount val="3"/><c:pt idx="0"><c:v>1</c:v></c:pt><c:pt idx="2"><c:v>3</c:v></c:pt></c:numCache></c:numRef></c:val></c:ser>
            <c:gapWidth val="80"/><c:upDownBarsMarker/><c:axId val="1"/><c:axId val="2"/></c:barChart>
            <c:catAx><c:axId val="1"/><c:scaling><c:orientation val="minMax"/></c:scaling><c:delete val="0"/><c:axPos val="b"/><c:crossAx val="2"/></c:catAx>
            <c:valAx><c:axId val="2"/><c:scaling><c:orientation val="minMax"/><c:max val="10"/></c:scaling><c:delete val="0"/><c:axPos val="l"/><c:majorGridlines/><c:crossAx val="1"/><c:crossBetween val="between"/></c:valAx>
            </c:plotArea><c:plotVisOnly val="1"/></c:chart>
            <c:externalData r:id="rId1"><c:autoUpdate val="0"/></c:externalData></c:chartSpace>
            """.Trim();

        var chart = ChartXml.Read(xml);
        Assert.True(chart.IsSupported);
        Assert.Equal(80, chart.Plots[0].GapWidth);
        Assert.Equal(10, chart.Axes[1].Maximum);
        Assert.Equal([1, null, 3], chart.Plots[0].Series[0].Values.Cache!.Select(value => value.Number));

        var edited = ChartEdits.SetTitle(chart, true, "Neu");
        var written = ChartXmlWriter.Write(edited);

        Assert.Contains("<c:view3D>", written, StringComparison.Ordinal);
        Assert.Contains("c:trendline", written, StringComparison.Ordinal);
        Assert.Contains("a:gradFill", written, StringComparison.Ordinal);
        Assert.Contains("externalData", written, StringComparison.Ordinal);
        Assert.Contains("en-US", written, StringComparison.Ordinal);
        Assert.Contains(">Neu<", written, StringComparison.Ordinal);
        Assert.DoesNotContain("upDownBarsMarker", written, StringComparison.Ordinal);
        Assert.True(written.IndexOf("c:trendline", StringComparison.Ordinal) < written.IndexOf("<c:val>", StringComparison.Ordinal), "schema order");
    }

    [Fact]
    public void Unsupported_types_are_written_exactly_as_read()
    {
        var xml = $"<c:chartSpace {Namespaces}><c:chart><c:plotArea><c:radarChart><c:radarStyle val=\"marker\"/></c:radarChart></c:plotArea></c:chart></c:chartSpace>";

        var chart = ChartXml.Read(xml);

        Assert.False(chart.IsSupported);
        Assert.Equal("Netz", chart.UnsupportedType);
        Assert.Equal(xml, ChartXmlWriter.Write(chart));
    }

    [Fact]
    public void Broken_xml_becomes_an_unsupported_chart()
    {
        var chart = ChartXml.Read("<c:chartSpace");

        Assert.False(chart.IsSupported);
        Assert.Equal("<c:chartSpace", ChartXmlWriter.Write(chart));
    }

    [Fact]
    public void Caches_come_from_the_data_source()
    {
        var chart = ChartFactory.Create(ChartTemplates.Find("line")!, [new SeriesSpec(null, new ChartDataRef("S!$A$1:$A$2") { IsNumeric = false }, new ChartDataRef("S!$B$1:$B$2"))]);
        var source = new DictionarySource(new()
        {
            ["S!$A$1:$A$2"] = [new(null, "Montag"), new(null, "Dienstag")],
            ["S!$B$1:$B$2"] = [new(7.5, "7,5", "0.0"), new(null, null)],
        });

        var written = ChartXmlWriter.Write(chart, source);
        var read = ChartXml.Read(written);

        Assert.Equal(["Montag", "Dienstag"], read.Plots[0].Series[0].Categories!.Cache!.Select(value => value.Text));
        Assert.Equal([7.5, null], read.Plots[0].Series[0].Values.Cache!.Select(value => value.Number));
        Assert.Equal("0.0", read.Plots[0].Series[0].Values.FormatCode);
    }

    private sealed class DictionarySource(Dictionary<string, IReadOnlyList<ChartValue>> values) : IChartDataSource
    {
        public IReadOnlyList<ChartValue>? Resolve(string formula) => values.TryGetValue(formula, out var result) ? result : null;
    }
}
