using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formats.Odp;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Formats;

/// <summary>OpenDocument Presentation: the model through .odp and back, the package's structure, tolerance and failures.</summary>
public sealed class OdpTests
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Draw = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    private static readonly XNamespace Manifest = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    public static byte[] Write(Presentation presentation)
    {
        using var stream = new MemoryStream();
        OdpWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    public static Presentation Read(byte[] bytes) => OdpReader.Read(new MemoryStream(bytes));

    public static Presentation RoundTrip(Presentation presentation) => Read(Write(presentation));

    private static Slide SlideWith(params SlideElement[] shapes) => TestDecks.Blank(shapes).Slides[0];

    private static Presentation Blank(params SlideElement[] shapes) => TestDecks.Blank(shapes);

    /// <summary>The shapes of the first slide after a round trip equal the shapes written, one by one (a failure names the shape).</summary>
    private static Presentation AssertShapesSurvive(Presentation presentation)
    {
        var read = RoundTrip(presentation);
        var expected = presentation.Slides[0].Shapes;
        var actual = read.Slides[0].Shapes;
        Assert.Equal(expected.Count, actual.Count);
        for (var index = 0; index < expected.Count; index++)
        {
            if (expected[index] is GraphicFrame { Table: { } table } && actual[index] is GraphicFrame { Table: { } other })
            {
                Assert.Equal(table.Properties, other.Properties);
                Assert.Equal(table.ColumnWidths, other.ColumnWidths);
                for (var row = 0; row < table.RowCount; row++)
                {
                    Assert.Equal(table.Rows[row].Height, other.Rows[row].Height);
                    for (var column = 0; column < table.ColumnCount; column++)
                    {
                        Assert.Equal(table.CellAt(row, column).Properties, other.CellAt(row, column).Properties);
                        Assert.Equal(table.CellAt(row, column), other.CellAt(row, column));
                    }
                }
            }

            Assert.Equal(expected[index], actual[index]);
        }

        return read;
    }

    [Fact]
    public void The_slide_size_survives()
    {
        foreach (var size in new[] { SlideSize.Widescreen, SlideSize.Standard, new SlideSize(10000000, 7000000) })
        {
            var presentation = MasterFactory.CreatePresentation(size: size);
            Assert.Equal(size, RoundTrip(presentation).SlideSize);
        }
    }

    [Fact]
    public void Shapes_with_fills_outlines_and_geometry_survive()
    {
        var rectangle = new AutoShape
        {
            Id = 2,
            Name = "Rechteck 1",
            Description = "Ein rotes Rechteck",
            AltTitle = "Rechteck",
            Transform = new Transform2D(914400, 914400, 2743200, 1371600),
            Geometry = ShapeGeometry.Rectangle,
            Fill = new SolidFill(ColorSpec.Rgb("C43E1C")),
            Outline = new Outline(19050, new SolidFill(ColorSpec.Rgb("1F1F1F")), new LineDash("dash"), LineCap.Round, LineJoin.Round),
        };
        var ellipse = new AutoShape
        {
            Id = 3,
            Name = "Ellipse 2",
            Transform = new Transform2D(4572000, 914400, 1828800, 1828800, Rotation: 30 * 60000),
            Geometry = ShapeGeometry.Of("ellipse"),
            Fill = new GradientFill([new GradientStop(0, ColorSpec.Rgb("FFFFFF")), new GradientStop(50000, ColorSpec.Rgb("2E9E6A")), new GradientStop(100000, ColorSpec.Rgb("1F6FB2"))], LinearAngle: 45 * 60000),
            Outline = new Outline(Fill: NoFill.Instance),
        };
        var arrow = new AutoShape
        {
            Id = 4,
            Name = "Pfeil 3",
            Transform = new Transform2D(914400, 3200400, 2743200, 1371600, FlipHorizontal: true),
            Geometry = new ShapeGeometry("rightArrow", [new GeometryAdjust("adj1", 30000)]),
            Fill = new SolidFill(ColorSpec.Rgb("1F6FB2").WithOpacity(0.5)),
            Outline = new Outline(12700, new SolidFill(ColorSpec.Rgb("000000")), Head: new LineEnd(LineEndKind.Oval, LineEndSize.Small), Tail: new LineEnd(LineEndKind.Triangle, LineEndSize.Large)),
        };
        var radial = new AutoShape
        {
            Id = 5,
            Name = "Rund 4",
            Transform = new Transform2D(6400800, 3200400, 1828800, 1371600, Rotation: 270 * 60000, FlipVertical: true),
            Geometry = new ShapeGeometry("roundRect"),
            Fill = new GradientFill([new GradientStop(0, ColorSpec.Rgb("FFFFFF")), new GradientStop(100000, ColorSpec.Rgb("000000"))], LinearAngle: null, PathKind: GradientPathKind.Circle, FillToRect: new RelativeRect(50000, 50000, 50000, 50000)),
        };
        AssertShapesSurvive(Blank(rectangle, ellipse, arrow, radial));
    }

    [Fact]
    public void A_preset_is_written_as_the_path_the_geometry_engine_computes()
    {
        var shape = new AutoShape
        {
            Id = 2,
            Name = "Stern",
            Transform = new Transform2D(0, 0, 1828800, 1371600),
            Geometry = new ShapeGeometry("star5", [new GeometryAdjust("adj", 25000)]),
        };
        var bytes = Write(Blank(shape));
        var geometry = Content(bytes).Descendants(Draw + "enhanced-geometry").Single();
        Assert.Equal("ooxml-star5", (string?)geometry.Attribute(Draw + "type"));
        var expected = OdpGeometry.PathText(GeometryEngine.Evaluate(shape.Geometry, 1828800, 1371600).Paths);
        Assert.Equal(expected, (string?)geometry.Attribute(Draw + "enhanced-path"));
        Assert.Equal("0 0 1828800 1371600", (string?)geometry.Attribute(XNamespace.Get("urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0") + "viewBox"));
        Assert.StartsWith("25000", (string?)geometry.Attribute(Draw + "modifiers"));
    }

    [Fact]
    public void A_custom_geometry_survives_as_a_path()
    {
        var custom = OdpGeometry.CustomGeometry([new OdpGeometry.SubPath(new ThixOffice.Text.Drawing.VectorPathBuilder().MoveTo(0, 0).LineTo(20000, 5000).CubicTo(15000, 10000, 10000, 20000, 0, 20000).Close().Build(), true, true)], 20000, 20000, (1000, 1000, 19000, 19000));
        var shape = new AutoShape { Id = 2, Name = "Freihandform 1", Transform = new Transform2D(914400, 914400, 1828800, 1828800), Geometry = custom, Fill = new SolidFill(ColorSpec.Rgb("7A4FB3")) };
        AssertShapesSurvive(Blank(shape));
    }

    [Fact]
    public void Text_with_runs_paragraph_formats_and_lists_survives()
    {
        var bold = new RunProperties(Size: 2400, Bold: true, Fill: new SolidFill(ColorSpec.Rgb("C43E1C")), Latin: new TextFont("Arial"), Language: "de-DE");
        var italic = new RunProperties(Italic: true, Underline: "sng", Strike: "sngStrike", Baseline: 30000, Highlight: ColorSpec.Rgb("FFFF00"));
        var link = new RunProperties(Click: Hyperlink.Url("https://example.org/"));
        var text = new TextBody(new BodyProperties(LeftInset: 91440, Anchor: TextAnchor.Center, Wrap: true, AutoFit: new NormalAutoFit()), ListStyle.Empty,
        [
            new TextParagraph(new ParagraphProperties(Alignment: TextAlignment.Center, SpaceBefore: TextSpacing.Points(6), LineSpacing: TextSpacing.Percent(90000)),
                [new TextSpan("Fett ", bold), new TextSpan("kursiv", italic), new LineBreak(RunProperties.Empty), new TextSpan("  zwei  Leerzeichen\tTab ", RunProperties.Empty), new TextSpan("Link", link)]),
            new TextParagraph(new ParagraphProperties(Bullet: new CharacterBullet("•"), LeftMargin: 342900, Indent: -342900), [new TextSpan("Punkt eins", RunProperties.Empty)]),
            new TextParagraph(new ParagraphProperties(Level: 1, Bullet: new CharacterBullet("–")), [new TextSpan("Unterpunkt", RunProperties.Empty)]),
            new TextParagraph(new ParagraphProperties(Bullet: new CharacterBullet("•")), [new TextSpan("Punkt zwei", RunProperties.Empty)]),
            new TextParagraph(new ParagraphProperties(Bullet: new AutoNumberBullet("arabicPeriod")), [new TextSpan("Erstens", RunProperties.Empty)]),
            new TextParagraph(new ParagraphProperties(Bullet: new AutoNumberBullet("alphaLcParenR", 3)), [new TextSpan("c)", RunProperties.Empty)]),
            new TextParagraph(new ParagraphProperties(Level: 2), [new TextSpan("Eingerückt ohne Aufzählung", RunProperties.Empty)]),
            new TextParagraph(ParagraphProperties.Empty, [], new RunProperties(Size: 1400)),
            TextParagraph.Plain("Feld: ", new RunProperties(Language: "de-DE")),
        ]);
        text = text with
        {
            Paragraphs = text.Paragraphs.SetItem(8, new TextParagraph(ParagraphProperties.Empty,
                [new TextSpan("Folie ", RunProperties.Empty), new TextField(OdpNames.SlideNumberFieldId, "slidenum", "3", RunProperties.Empty)])),
        };
        var box = TestDecks.TextBox(2, "") with { Text = text };
        AssertShapesSurvive(Blank(box));
    }

    [Fact]
    public void Pictures_with_crop_tables_groups_lines_and_connectors_survive()
    {
        var image = ImageData.From(TestImages.Png(320, 200));
        var picture = new PictureShape
        {
            Id = 2,
            Name = "Bild 1",
            Description = "Ein Bild",
            Transform = new Transform2D(914400, 914400, 2743200, 1714500, FlipHorizontal: true),
            Picture = new PictureFill(image, new RelativeRect(10000, 5000, 20000, 0), StretchMode.Full),
            Outline = new Outline(12700, new SolidFill(ColorSpec.Rgb("000000"))),
        };
        var cell = new TableCell(TextBody.FromText("Kopf"), new TableCellProperties(LeftMargin: 91440, RightMargin: 91440, TopMargin: 45720, BottomMargin: 45720, Anchor: TextAnchor.Center,
            TopBorder: new Outline(12700, new SolidFill(ColorSpec.Rgb("1F1F1F"))), BottomBorder: Outline.None, Fill: new SolidFill(ColorSpec.Rgb("EEF1F4"))));
        var plain = new TableCell(TextBody.FromText("Wert"), TableCellProperties.Empty);
        var table = new Table(new TableProperties(OdpReader.NoStyleTableId, FirstRow: true, BandRows: true), [1828800, 1828800, 1828800],
        [
            new TableRow(457200, [cell with { GridSpan = 2 }, TableCell.Empty with { HorizontalMerge = true }, plain]),
            new TableRow(457200, [plain with { RowSpan = 2 }, plain, plain]),
            new TableRow(457200, [TableCell.Empty with { VerticalMerge = true }, plain, plain]),
        ]);
        var frame = new GraphicFrame { Id = 3, Name = "Tabelle 2", Kind = GraphicKind.Table, Table = table, Transform = new Transform2D(914400, 3200400, 5486400, 1371600) };
        var first = TestDecks.Rectangle(5, 6400800, 914400, 914400, 914400) with { Fill = new SolidFill(ColorSpec.Rgb("2E9E6A")) };
        var second = TestDecks.Rectangle(6, 7543800, 1371600, 914400, 914400) with { Fill = new SolidFill(ColorSpec.Rgb("E3A21A")) };
        var group = new GroupShape
        {
            Id = 4,
            Name = "Gruppe 3",
            Transform = new Transform2D(6400800, 914400, 2057400, 1371600, ChildX: 6400800, ChildY: 914400, ChildWidth: 2057400, ChildHeight: 1371600),
            Children = [first, second],
        };
        var line = new ConnectorShape { Id = 7, Name = "Linie 6", Transform = new Transform2D(914400, 5029200, 1828800, 457200, FlipVertical: true), Outline = new Outline(25400, new SolidFill(ColorSpec.Rgb("C43E1C")), Tail: new LineEnd(LineEndKind.Arrow)) };
        var connector = new ConnectorShape
        {
            Id = 8,
            Name = "Verbinder 7",
            Transform = new Transform2D(7315200, 2286000, 685800, 914400),
            Geometry = ShapeGeometry.Of("straightConnector1"),
            Outline = new Outline(12700, new SolidFill(ColorSpec.Rgb("000000"))),
            Start = new Connection(5, 2),
            End = new Connection(6, 0),
        };
        var presentation = Blank(picture, frame, group, line, connector) with { TableStyles = [OdpReader.NoStyleTable] };
        var read = AssertShapesSurvive(presentation);
        Assert.Equal(presentation.TableStyles, read.TableStyles);
    }

    [Fact]
    public void Notes_transitions_animations_and_backgrounds_survive()
    {
        var box = TestDecks.TextBox(2, "Erscheint");
        var shape = TestDecks.Rectangle(3, 4572000, 914400, 914400, 914400) with { Fill = new SolidFill(ColorSpec.Rgb("C43E1C")) };
        var notes = new NotesSlide
        {
            Shapes =
            [
                new AutoShape { Id = 2, Name = "Folienbildplatzhalter 1", Placeholder = new PlaceholderInfo(PlaceholderType.SlideImage) },
                new AutoShape { Id = 3, Name = "Notizenplatzhalter 2", Placeholder = new PlaceholderInfo(PlaceholderType.Body, 1), Text = TextBody.FromText("Begrüßung\nZweiter Gedanke") },
            ],
        };
        var timing = new SlideTiming(
        [
            AnimationPresets.Create(AnimationPreset.Fade, 2, durationMs: 700),
            AnimationPresets.Create(AnimationPreset.Appear, 3, AnimationTrigger.AfterPrevious, delayMs: 500),
            AnimationPresets.Create(AnimationPreset.FadeOut, 2, AnimationTrigger.WithPrevious),
        ]);
        var presentation = Blank(box, shape);
        var slide = presentation.Slides[0] with
        {
            Notes = notes,
            Transition = new SlideTransition(TransitionKind.Push, "u", 600, AdvanceAfterMs: 5000),
            Timing = timing,
            Background = new Background(Fill: new SolidFill(ColorSpec.Rgb("EEF1F4"))),
            Hidden = true,
        };
        var second = MasterFactory.CreateSlide(presentation.Masters[0].Layouts.First(layout => layout.Type == "blank"), 257) with { Transition = new SlideTransition(TransitionKind.Fade), Name = "Schluss" };
        presentation = presentation with { Slides = [slide, second] };

        var read = RoundTrip(presentation);
        Assert.Equal(slide.Notes, read.Slides[0].Notes);
        Assert.Equal(slide.Transition, read.Slides[0].Transition);
        Assert.Equal(slide.Timing!.MainSequence.ToList(), read.Slides[0].Timing!.MainSequence.ToList());
        Assert.Equal(slide.Background, read.Slides[0].Background);
        Assert.True(read.Slides[0].Hidden);
        Assert.Equal(second.Transition, read.Slides[1].Transition);
        Assert.Equal("Schluss", read.Slides[1].Name);
    }

    [Fact]
    public void A_presentation_read_from_odp_reads_the_same_after_another_round_trip()
    {
        var deck = TestDecks.Deck(4);
        var first = RoundTrip(deck);
        var second = RoundTrip(first);
        PptxTestSupport.AssertSameModel(first, second);

        Assert.Equal(4, first.SlideCount);
        var title = first.Slides[1].Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true);
        Assert.Equal("Folie 2", title.Text!.PlainText);
        Assert.Null(title.Transform); // at the master's place: inherited
        var body = first.Slides[1].Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.Type == PlaceholderType.Object);
        Assert.Equal(3, body.Text!.Paragraphs.Count);
        Assert.All(body.Text.Paragraphs, paragraph => Assert.Null(paragraph.Properties.Bullet)); // the master's bullets
        Assert.Equal("obj", first.LayoutOf(first.Slides[1]).Type);
    }

    [Fact]
    public void The_new_presentation_with_its_title_slide_reads_back_with_title_and_subtitle()
    {
        var presentation = MasterFactory.CreatePresentation();
        var slide = presentation.Slides[0];
        var shapes = slide.Shapes.Select(shape => shape is AutoShape { Placeholder: { } placeholder } auto
            ? auto with { Text = TextBody.FromText(placeholder.IsTitle ? "Jahresrückblick" : "Ein Untertitel") }
            : shape).ToList();
        presentation = presentation with { Slides = [slide with { Shapes = shapes }] };

        var read = RoundTrip(presentation);
        var layout = read.LayoutOf(read.Slides[0]);
        Assert.Equal("title", layout.Type);
        var title = read.Slides[0].Shapes.OfType<AutoShape>().Single(shape => shape.Placeholder?.Type == PlaceholderType.CenteredTitle);
        var subtitle = read.Slides[0].Shapes.OfType<AutoShape>().Single(shape => shape.Placeholder?.Type == PlaceholderType.Subtitle);
        Assert.Equal("Jahresrückblick", title.Text!.PlainText);
        Assert.Equal("Ein Untertitel", subtitle.Text!.PlainText);
        PptxTestSupport.AssertSameModel(read, RoundTrip(read));

        // The title's look (centred, 60 pt) comes along through the master page of the title layout.
        var master = read.MasterOf(read.Slides[0]);
        Assert.Equal(TextAlignment.Center, master.TextStyles.Title.LevelOf(0)!.Alignment);
        Assert.Equal(6000, master.TextStyles.Title.LevelOf(0)!.DefaultRun!.Size);
    }

    [Fact]
    public void The_package_has_its_mimetype_first_stored_and_a_manifest_of_every_part()
    {
        var presentation = Blank(new PictureShape { Id = 2, Name = "Bild", Transform = new Transform2D(0, 0, 914400, 914400), Picture = new PictureFill(ImageData.From(TestImages.Png(16, 16)), RelativeRect.Zero, StretchMode.Full) });
        var bytes = Write(presentation);
        Assert.Equal("mimetype", Encoding.ASCII.GetString(bytes, 30, 8));
        Assert.Equal(OdpWriter.MimeType, Encoding.ASCII.GetString(bytes, 38, OdpWriter.MimeType.Length));
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal(zip.Entries[0].Length, zip.Entries[0].CompressedLength);

        var manifest = XDocument.Load(zip.GetEntry("META-INF/manifest.xml")!.Open());
        var listed = manifest.Root!.Elements(Manifest + "file-entry").Select(entry => (string)entry.Attribute(Manifest + "full-path")!).ToHashSet();
        Assert.Contains("/", listed);
        foreach (var entry in zip.Entries.Where(entry => entry.FullName is not ("mimetype" or "META-INF/manifest.xml")))
        {
            Assert.Contains(entry.FullName, listed);
        }

        Assert.Contains(listed, path => path.StartsWith("Pictures/", StringComparison.Ordinal));
        foreach (var entry in zip.Entries.Where(entry => entry.FullName.EndsWith(".xml", StringComparison.Ordinal)))
        {
            using var stream = entry.Open();
            var document = XDocument.Load(stream); // well-formed
            if (entry.FullName == "content.xml")
            {
                Assert.Equal(Office + "document-content", document.Root!.Name);
                Assert.Equal("1.3", (string?)document.Root.Attribute(Office + "version"));
                Assert.NotNull(document.Root.Element(Office + "body")!.Element(Office + "presentation"));
            }

            if (entry.FullName == "styles.xml")
            {
                Assert.Equal(Office + "document-styles", document.Root!.Name);
                Assert.NotEmpty(document.Root.Element(Office + "master-styles")!.Elements());
            }
        }
    }

    [Fact]
    public void Malformed_files_fail_with_a_German_message()
    {
        var notZip = Assert.Throws<InvalidDataException>(() => OdpReader.Read(new MemoryStream(Encoding.UTF8.GetBytes("keine Präsentation"))));
        Assert.Equal("Die Datei ist keine gültige OpenDocument-Präsentation (.odp).", notZip.Message);

        var broken = Package(("content.xml", "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"><office:body>"));
        Assert.Equal(notZip.Message, Assert.Throws<InvalidDataException>(() => OdpReader.Read(new MemoryStream(broken))).Message);

        var empty = Package(("styles.xml", "<x/>"));
        Assert.Equal(notZip.Message, Assert.Throws<InvalidDataException>(() => OdpReader.Read(new MemoryStream(empty))).Message);

        var noPresentation = Package(("content.xml", "<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"><office:body><office:text/></office:body></office:document-content>"));
        Assert.Equal(notZip.Message, Assert.Throws<InvalidDataException>(() => OdpReader.Read(new MemoryStream(noPresentation))).Message);
    }

    [Fact]
    public void Losses_name_what_odp_cannot_take()
    {
        var chart = new GraphicFrame { Id = 2, Name = "Diagramm 1", Kind = GraphicKind.Chart, Transform = new Transform2D(0, 0, 914400, 914400) };
        var diagram = new GraphicFrame { Id = 3, Name = "SmartArt 2", Kind = GraphicKind.Diagram, Transform = new Transform2D(0, 0, 914400, 914400) };
        var glowing = TestDecks.Rectangle(4, 0, 0, 914400, 914400) with { Effects = new ShapeEffects(null, new XmlFragment("<a:effectLst xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:glow rad=\"1\"/></a:effectLst>")) };
        var presentation = Blank(chart, diagram, glowing) with { IsMacroEnabled = true };
        var slide = presentation.Slides[0] with { Timing = new SlideTiming([AnimationPresets.Create(AnimationPreset.FlyIn, 4)]) };
        presentation = presentation with { Slides = [slide] };

        var losses = OdpWriter.Losses(presentation).ToDictionary(loss => loss.Kind, loss => loss.Count);
        Assert.Equal(1, losses["chart"]);
        Assert.Equal(1, losses["diagram"]);
        Assert.Equal(1, losses["effects"]);
        Assert.Equal(1, losses["animation"]);
        Assert.Equal(1, losses["macros"]);
        Assert.Empty(OdpWriter.Losses(TestDecks.Deck(2)));

        var read = RoundTrip(presentation);
        Assert.DoesNotContain(ShapeTree.All(read.Slides[0].Shapes), shape => shape is GraphicFrame);
    }

    // ------------------------------------------------------------ helpers

    internal static XDocument Content(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes), ZipArchiveMode.Read);
        using var stream = zip.GetEntry("content.xml")!.Open();
        return XDocument.Load(stream);
    }

    internal static byte[] Package(params (string Path, string Text)[] parts)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var writer = new StreamWriter(mime.Open(), new UTF8Encoding(false)))
            {
                writer.Write(OdpWriter.MimeType);
            }

            foreach (var (path, text) in parts)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open(), new UTF8Encoding(false));
                writer.Write(text);
            }
        }

        return buffer.ToArray();
    }
}
