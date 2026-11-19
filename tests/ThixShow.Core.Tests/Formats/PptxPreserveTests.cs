using System.Xml.Linq;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ThixShow.Core.Model;
using static ThixShow.Core.Tests.Formats.PptxTestSupport;

namespace ThixShow.Core.Tests.Formats;

public class PptxPreserveTests
{
    private static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    private static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    private static XElement Load(OpenXmlPart part)
    {
        using var stream = part.GetStream();
        return XElement.Load(stream);
    }

    private static void Save(OpenXmlPart part, XElement root)
    {
        using var stream = part.GetStream(FileMode.Create);
        root.Save(stream);
    }

    /// <summary>
    /// A pptm made by ThixShow and then extended with the SDK the way other programs write: a chart part with its graphic frame, a
    /// VBA project, custom XML, a shape with an unknown extension, and an mc:AlternateContent shape requiring an unknown namespace.
    /// </summary>
    private static byte[] Fixture()
    {
        var presentation = TestDecks.Deck(2) with { IsMacroEnabled = true };
        var bytes = Write(presentation);
        using var stream = new MemoryStream();
        stream.Write(bytes);
        using (var package = PresentationDocument.Open(stream, true))
        {
            var presentationPart = package.PresentationPart!;
            var slidePart = presentationPart.SlideParts.First(part => Load(part).Descendants(A + "t").Any(text => text.Value == "Folie 1"));
            var chart = slidePart.AddNewPart<ChartPart>();
            Save(chart, new XElement(C + "chartSpace", new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName), new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
                new XElement(C + "chart", new XElement(C + "plotArea", new XElement(C + "layout"),
                    new XElement(C + "barChart", new XElement(C + "barDir", new XAttribute("val", "col")), new XElement(C + "axId", new XAttribute("val", "1")), new XElement(C + "axId", new XAttribute("val", "2"))),
                    new XElement(C + "catAx", new XElement(C + "axId", new XAttribute("val", "1")), new XElement(C + "scaling"), new XElement(C + "axPos", new XAttribute("val", "b")), new XElement(C + "crossAx", new XAttribute("val", "2"))),
                    new XElement(C + "valAx", new XElement(C + "axId", new XAttribute("val", "2")), new XElement(C + "scaling"), new XElement(C + "axPos", new XAttribute("val", "l")), new XElement(C + "crossAx", new XAttribute("val", "1")))))));
            var chartId = slidePart.GetIdOfPart(chart);
            var root = Load(slidePart);
            var tree = root.Descendants(P + "spTree").First();
            tree.Add(new XElement(P + "graphicFrame",
                new XElement(P + "nvGraphicFramePr", new XElement(P + "cNvPr", new XAttribute("id", "90"), new XAttribute("name", "Diagramm 1")), new XElement(P + "cNvGraphicFramePr"), new XElement(P + "nvPr")),
                new XElement(P + "xfrm", new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")), new XElement(A + "ext", new XAttribute("cx", "3000000"), new XAttribute("cy", "2000000"))),
                new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", C.NamespaceName),
                    new XElement(C + "chart", new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName), new XAttribute(R + "id", chartId))))));
            var rectangle = tree.Elements(P + "sp").Last();
            rectangle.Add(new XElement(P + "extLst", new XElement(P + "ext", new XAttribute("uri", "{12345678-TEST}"), new XElement(XName.Get("marker", "urn:thixshow:test"), new XAttribute("value", "kept")))));
            tree.Add(new XElement(Mc + "AlternateContent", new XAttribute(XNamespace.Xmlns + "mc", Mc.NamespaceName),
                new XElement(Mc + "Choice", new XAttribute(XNamespace.Xmlns + "zz", "urn:thixshow:unknown"), new XAttribute("Requires", "zz"),
                    new XElement(P + "sp", new XElement(P + "nvSpPr", new XElement(P + "cNvPr", new XAttribute("id", "91"), new XAttribute("name", "Zukunft")), new XElement(P + "cNvSpPr"), new XElement(P + "nvPr")), new XElement(P + "spPr"))),
                new XElement(Mc + "Fallback",
                    new XElement(P + "sp", new XElement(P + "nvSpPr", new XElement(P + "cNvPr", new XAttribute("id", "91"), new XAttribute("name", "Ersatz")), new XElement(P + "cNvSpPr"), new XElement(P + "nvPr")),
                        new XElement(P + "spPr", new XElement(A + "xfrm", new XElement(A + "off", new XAttribute("x", "100"), new XAttribute("y", "100")), new XElement(A + "ext", new XAttribute("cx", "1000"), new XAttribute("cy", "1000"))), new XElement(A + "prstGeom", new XAttribute("prst", "rect"), new XElement(A + "avLst")))))));
            Save(slidePart, root);

            var vba = presentationPart.AddNewPart<VbaProjectPart>();
            using (var data = new MemoryStream([0xD0, 0xCF, 0x11, 0xE0, 1, 2, 3, 4]))
            {
                vba.FeedData(data);
            }

            var custom = presentationPart.AddCustomXmlPart(CustomXmlPartType.CustomXml);
            Save(custom, new XElement(XName.Get("daten", "urn:thixshow:custom"), new XElement(XName.Get("wert", "urn:thixshow:custom"), "42")));
        }

        return stream.ToArray();
    }

    private static List<string> Errors(byte[] bytes)
    {
        using var package = PresentationDocument.Open(new MemoryStream(bytes), false);
        return new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package).Select(error => $"{error.Path?.XPath}: {error.Description}").ToList();
    }

    [Fact]
    public void Unknown_content_is_listed_in_german()
    {
        var presentation = Read(Fixture());
        Assert.True(presentation.IsMacroEnabled);
        var messages = presentation.UnsupportedContent.Select(content => content.Message).ToList();
        Assert.Contains("1 × Diagramm (wird erhalten, als Platzhalter angezeigt)", messages);
        Assert.Contains("1 × Makros (werden erhalten, aber nie ausgeführt)", messages);
        Assert.Contains(messages, message => message.StartsWith("1 × Benutzerdefinierte XML-Daten", StringComparison.Ordinal));
        var frame = ShapeTree.All(presentation.Slides[0].Shapes).OfType<GraphicFrame>().Single();
        Assert.Equal(GraphicKind.Chart, frame.Kind);
        Assert.EndsWith("chart1.xml", frame.ChartPartUri);
        var alternate = presentation.Slides[0].Shapes.Single(shape => shape.Source?.Xml.Contains("AlternateContent", StringComparison.Ordinal) == true);
        Assert.Equal("Ersatz", alternate.Name);
    }

    [Fact]
    public void Editing_one_shape_keeps_everything_else()
    {
        var fixture = Fixture();
        var presentation = Read(fixture);
        var slide = presentation.Slides[0];
        var title = slide.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true);
        var marked = slide.Shapes.Single(shape => shape.Source?.Xml.Contains("urn:thixshow:test", StringComparison.Ordinal) == true && shape.Source.Xml.StartsWith("<p:sp", StringComparison.Ordinal));
        var changed = slide with
        {
            Shapes = ShapeTree.Replace(
                ShapeTree.Replace(slide.Shapes, title.Id, shape => ((AutoShape)shape) with { Text = TextBody.FromText("Neuer Titel") }),
                marked.Id,
                shape => shape with { Transform = shape.Transform! with { X = shape.Transform.X + 12700 } }),
        };
        var output = Write(presentation with { Slides = presentation.Slides.SetItem(0, changed) });

        // No validation errors beyond those the fixture already had.
        Assert.Empty(Errors(output).Except(Errors(fixture)));

        using var package = PresentationDocument.Open(new MemoryStream(output), false);
        var presentationPart = package.PresentationPart!;
        Assert.NotNull(presentationPart.VbaProjectPart);
        Assert.Equal(PresentationDocumentType.MacroEnabledPresentation, package.DocumentType);
        Assert.Single(presentationPart.CustomXmlParts);
        var slidePart = presentationPart.SlideParts.First(part => Load(part).Descendants(A + "t").Any(text => text.Value == "Neuer Titel"));
        var root = Load(slidePart);
        var chartId = root.Descendants(C + "chart").Single().Attribute(R + "id")!.Value;
        Assert.IsType<ChartPart>(slidePart.GetPartById(chartId));
        var moved = root.Descendants(P + "sp").Single(sp => sp.Descendants(XName.Get("marker", "urn:thixshow:test")).Any());
        Assert.Equal("kept", moved.Descendants(XName.Get("marker", "urn:thixshow:test")).Single().Attribute("value")!.Value);
        Assert.Equal((marked.Transform!.X + 12700).ToString(System.Globalization.CultureInfo.InvariantCulture), moved.Element(P + "spPr")!.Element(A + "xfrm")!.Element(A + "off")!.Attribute("x")!.Value);
        Assert.Single(root.Elements(P + "cSld").Elements(P + "spTree").Elements(Mc + "AlternateContent"));

        // The second slide was not touched at all.
        using var original = PresentationDocument.Open(new MemoryStream(fixture), false);
        string Text(PresentationDocument document, string title) => Load(document.PresentationPart!.SlideParts.First(part => Load(part).Descendants(A + "t").Any(text => text.Value == title))).ToString();
        Assert.Equal(Text(original, "Folie 2"), Text(package, "Folie 2"));

        // And it reads back with the change.
        var read = Read(output);
        Assert.Equal("Neuer Titel", read.Slides[0].Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true).Text!.PlainText);
        Assert.Equal(GraphicKind.Chart, ShapeTree.All(read.Slides[0].Shapes).OfType<GraphicFrame>().Single().Kind);
    }

    [Fact]
    public void A_duplicated_slide_with_a_chart_points_its_frame_at_the_chart()
    {
        var presentation = Read(Fixture());
        var copy = presentation.Slides[0] with { Id = presentation.NextSlideId };
        var output = Write(presentation with { Slides = presentation.Slides.Add(copy) });
        using var package = PresentationDocument.Open(new MemoryStream(output), false);
        foreach (var slidePart in package.PresentationPart!.SlideParts.Where(part => Load(part).Descendants(C + "chart").Any()))
        {
            var id = Load(slidePart).Descendants(C + "chart").Single().Attribute(R + "id")!.Value;
            Assert.IsType<ChartPart>(slidePart.GetPartById(id));
        }

        Assert.Equal(2, package.PresentationPart!.SlideParts.Count(part => Load(part).Descendants(C + "chart").Any()));
    }
}
