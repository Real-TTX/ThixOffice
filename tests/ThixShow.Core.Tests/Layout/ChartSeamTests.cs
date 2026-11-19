using System.IO.Compression;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ThixOffice.Charts;
using ThixOffice.Charts.Formats;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using static ThixShow.Core.Tests.Formats.PptxTestSupport;

namespace ThixShow.Core.Tests.Layout;

/// <summary>Charts embedded in a pptx are drawn by the shared chart component and written back untouched.</summary>
public class ChartSeamTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    /// <summary>A deck whose first slide holds a column chart (cached values in the part, as Office writes them).</summary>
    private static byte[] DeckWithChart()
    {
        var chart = ChartFactory.Create(ChartTemplates.Find("column")!, [
            new SeriesSpec(ChartText.Of("Umsatz"), ChartDataRef.LiteralText(["Nord", "Süd", "Ost"]), ChartDataRef.Literal([10, 20, 30])),
        ]);
        using var stream = new MemoryStream();
        stream.Write(Write(TestDecks.Deck(1)));
        using (var package = PresentationDocument.Open(stream, true))
        {
            var slidePart = package.PresentationPart!.SlideParts.First();
            var chartPart = slidePart.AddNewPart<ChartPart>();
            using (var writer = new StreamWriter(chartPart.GetStream(FileMode.Create)))
            {
                writer.Write(ChartXmlWriter.Write(chart));
            }

            XElement root;
            using (var read = slidePart.GetStream())
            {
                root = XElement.Load(read);
            }

            root.Descendants(P + "spTree").First().Add(new XElement(P + "graphicFrame",
                new XElement(P + "nvGraphicFramePr", new XElement(P + "cNvPr", new XAttribute("id", "90"), new XAttribute("name", "Diagramm 1")), new XElement(P + "cNvGraphicFramePr"), new XElement(P + "nvPr")),
                new XElement(P + "xfrm", new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")), new XElement(A + "ext", new XAttribute("cx", "4572000"), new XAttribute("cy", "2743200"))),
                new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", C.NamespaceName),
                    new XElement(C + "chart", new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName), new XAttribute(R + "id", slidePart.GetIdOfPart(chartPart)))))));
            using var write = slidePart.GetStream(FileMode.Create);
            root.Save(write);
        }

        return stream.ToArray();
    }

    [Fact]
    public void A_chart_in_a_pptx_is_drawn_by_the_shared_chart_component_in_the_theme_accents()
    {
        var presentation = Read(DeckWithChart());
        var slide = presentation.Slides[0];

        var items = SlideRenderer.Render(presentation, slide, TestDecks.Measurer).Items;

        var texts = items.OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.Contains("Nord", texts);
        Assert.Contains("Umsatz", texts);
        Assert.DoesNotContain("Diagramm", texts);
        var accent = Drawing.ColorResolver.Resolve(ColorSpec.Scheme("accent1"), Formatting.SlideContext.For(presentation, slide).Colors);
        Assert.Equal(3, items.OfType<RectangleItem>().Count(rect => rect.Fill == accent && rect.Height > 1));
    }

    [Fact]
    public void An_unchanged_chart_part_is_written_back_byte_for_byte()
    {
        var original = DeckWithChart();
        var presentation = Read(original);
        var changed = presentation with { Slides = presentation.Slides.Add(presentation.Slides[0] with { Id = presentation.NextSlideId, Shapes = [] }) };

        var written = Write(changed);

        Assert.Equal(ChartPartBytes(original), ChartPartBytes(written));
    }

    private static Chart SampleChart() => ChartFactory.Create(ChartTemplates.Find("column")!, [
        new SeriesSpec(ChartText.Of("Umsatz"), ChartDataRef.LiteralText(["Nord", "Süd"]), ChartDataRef.Literal([5, 7])),
    ]);

    [Fact]
    public void An_inserted_chart_is_drawn_written_into_a_part_of_its_own_and_read_back()
    {
        var session = new EditingSession(TestDecks.Blank());
        session.GoToSlide(session.Presentation.Slides[0].Id);
        var id = session.InsertChart(SampleChart());

        var drawn = SlideRenderer.Render(session.Presentation, session.CurrentSlide!, TestDecks.Measurer).Items;
        Assert.Contains(drawn.OfType<GlyphRunItem>(), run => run.Text == "Nord");

        var bytes = Write(session.Presentation);
        AssertValid(bytes);
        var read = Read(bytes);
        var frame = Assert.Single(ShapeTree.All(read.Slides[0].Shapes).OfType<GraphicFrame>(), shape => shape.Id == id);
        Assert.Equal(GraphicKind.Chart, frame.Kind);
        Assert.NotNull(frame.ChartPartUri);
        Assert.Equal(2, SharedChartRenderer.ChartOf(frame, read)!.AllSeries.Single().Values.Cache!.Count);
    }

    [Fact]
    public void A_chart_copied_into_another_presentation_keeps_its_data()
    {
        var source = Read(DeckWithChart());
        var copy = new EditingSession(source);
        var frame = ShapeTree.All(source.Slides[0].Shapes).OfType<GraphicFrame>().Single();
        copy.GoToSlide(source.Slides[0].Id);
        copy.SelectShapes([frame.Id]);
        var fragment = ShowFragment.FromBytes(copy.Copy()!.ToBytes())!;

        var target = new EditingSession(TestDecks.Blank());
        target.GoToSlide(target.Presentation.Slides[0].Id);
        target.Paste(fragment);

        var pasted = ShapeTree.All(target.CurrentSlide!.Shapes).OfType<GraphicFrame>().Single();
        Assert.NotNull(pasted.Chart);
        var written = Read(Write(target.Presentation));
        var back = ShapeTree.All(written.Slides[0].Shapes).OfType<GraphicFrame>().Single();
        Assert.Equal(3, SharedChartRenderer.ChartOf(back, written)!.AllSeries.Single().Values.Cache!.Count);
    }

    private static byte[] ChartPartBytes(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        var entry = zip.Entries.Single(item => item.FullName.Contains("charts/chart", StringComparison.Ordinal) && item.FullName.EndsWith(".xml", StringComparison.Ordinal));
        using var stream = new MemoryStream();
        using (var input = entry.Open())
        {
            input.CopyTo(stream);
        }

        return stream.ToArray();
    }
}
