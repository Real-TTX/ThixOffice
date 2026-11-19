using ThixShow.Core.Model;
using static ThixShow.Core.Tests.Formats.PptxTestSupport;

namespace ThixShow.Core.Tests.Formats;

public class PptxRoundTripTests
{
    /// <summary>A deck with every kind ThixShow models.</summary>
    internal static Presentation Rich()
    {
        var presentation = TestDecks.Deck(3);
        var image = ImageData.From(TestImages.Png(6, 4));
        var gradient = new GradientFill([new GradientStop(0, ColorSpec.Scheme("accent1").LumMod(60000).LumOff(40000)), new GradientStop(100000, ColorSpec.Scheme("accent2").Alpha(50000))], LinearAngle: 2700000, LinearScaled: true);
        var radial = new GradientFill([new GradientStop(0, ColorSpec.Rgb("FFFFFF")), new GradientStop(100000, ColorSpec.Preset("navy"))], LinearAngle: null, PathKind: GradientPathKind.Circle, FillToRect: new RelativeRect(50000, 50000, 50000, 50000));
        var text = new TextBody(
            new BodyProperties(LeftInset: 0, Anchor: TextAnchor.Center, Wrap: true, AutoFit: new NormalAutoFit(90000, 10000), Columns: 2, ColumnSpacing: 91440),
            new ListStyle(null, [new ParagraphProperties(Alignment: TextAlignment.Center)]),
            [
                new TextParagraph(new ParagraphProperties(Bullet: new AutoNumberBullet("arabicParenR", 3), BulletColor: ColorSpec.Rgb("FF0000"), BulletSize: TextSpacing.Percent(80000)),
                [
                    new TextSpan("Fett ", new RunProperties(Bold: true, Size: 2400, Language: "de-DE")),
                    new TextSpan("und Link", new RunProperties(Click: Hyperlink.Url("https://example.org/", "Beispiel"), Underline: "sng")),
                    new LineBreak(RunProperties.Empty),
                    new TextField("{B6F15528-21DE-4FAA-801E-634DDDAF4B2B}", "slidenum", "2", RunProperties.Empty),
                ], new RunProperties(Size: 2400)),
                new TextParagraph(new ParagraphProperties(Level: 1, LineSpacing: TextSpacing.Points(20), SpaceBefore: TextSpacing.Percent(50000), Bullet: new CharacterBullet("§"), BulletFont: new TextFont("Wingdings", PitchFamily: "2", CharacterSet: "2"), Tabs: [new TabStop(914400, "r")]),
                [
                    new TextSpan("Hochgestellt", new RunProperties(Baseline: 30000, Caps: "all", Spacing: 100, Highlight: ColorSpec.Rgb("FFFF00"), Latin: new TextFont("Georgia"))),
                    new TextSpan(" Folie 1", new RunProperties(Click: Hyperlink.ToSlide(256))),
                ]),
                new TextParagraph(new ParagraphProperties(Bullet: new PictureBullet(image)), [new TextSpan("Bildaufzählung", RunProperties.Empty)]),
            ]);
        var shape = new AutoShape
        {
            Id = 20,
            Name = "Abgerundetes Rechteck 19",
            Description = "Alternativtext",
            AltTitle = "Titel",
            Transform = new Transform2D(914400, 914400, 3657600, 1828800, Rotation: 1800000, FlipHorizontal: true),
            Geometry = ShapeGeometry.Of("roundRect").WithAdjust("adj", 30000),
            Fill = gradient,
            Outline = new Outline(38100, new SolidFill(ColorSpec.Scheme("accent6").Shade(50000)), new LineDash("dashDot"), LineCap.Round, LineJoin.Bevel, Compound: "dbl"),
            Effects = new ShapeEffects(new OuterShadow(50800, 38100, 2700000, ColorSpec.Rgb("000000").Alpha(40000), "tl", false)),
            Style = new ShapeStyle(new StyleReference(2, ColorSpec.Scheme("accent1").Shade(50000)), new StyleReference(1, ColorSpec.Scheme("accent1")), new StyleReference(0, ColorSpec.Scheme("accent1")), new FontReference("minor", ColorSpec.Scheme("lt1"))),
            Text = text,
            Click = Hyperlink.ToSlide(258),
        };
        var connector = new ConnectorShape
        {
            Id = 21,
            Name = "Gewinkelte Verbindung 20",
            Transform = new Transform2D(0, 0, 1000000, 500000, FlipVertical: true),
            Geometry = ShapeGeometry.Of("bentConnector3").WithAdjust("adj1", 25000),
            Outline = new Outline(12700, new SolidFill(ColorSpec.Rgb("000000")), Head: new LineEnd(LineEndKind.Oval, LineEndSize.Small, LineEndSize.Large), Tail: new LineEnd(LineEndKind.Stealth)),
            Start = new Connection(20, 3),
        };
        var picture = new PictureShape
        {
            Id = 22,
            Name = "Bild 21",
            Description = "Ein Bild",
            Transform = new Transform2D(5000000, 3000000, 2000000, 1500000),
            Picture = new PictureFill(image, new RelativeRect(10000, 0, 5000, 20000), StretchMode.Full) { Opacity = 0.5 },
            Geometry = ShapeGeometry.Of("ellipse"),
            Outline = new Outline(25400, new SolidFill(ColorSpec.Scheme("tx1"))),
        };
        var tiled = TestDecks.Rectangle(23, 0, 3000000, 1000000, 1000000) with { Fill = new PictureFill(image, RelativeRect.Zero, new TileMode(10, 20, 50000, 50000, "xy", "tl")) };
        var patterned = TestDecks.Rectangle(24, 1000000, 3000000, 1000000, 1000000) with { Fill = new PatternFill("pct20", ColorSpec.Rgb("FF0000"), ColorSpec.Scheme("bg1")), Outline = Outline.None };
        var group = new GroupShape
        {
            Id = 30,
            Name = "Gruppieren 29",
            Transform = new Transform2D(6000000, 500000, 2000000, 1000000, ChildX: 0, ChildY: 0, ChildWidth: 4000000, ChildHeight: 2000000),
            Children = [TestDecks.Rectangle(31, 0, 0, 2000000, 2000000, "accent3"), TestDecks.Rectangle(32, 2000000, 0, 2000000, 2000000, "accent4", "ellipse") with { Fill = radial }],
        };
        var table = Table.Create(2, 3, 6000000, 370840, "{5C22544A-7EE6-4342-B048-85BDC9FD1C3A}");
        table = table.WithCell(0, 0, table.CellAt(0, 0) with { Text = TextBody.FromText("Kopf"), GridSpan = 2 })
            .WithCell(0, 1, table.CellAt(0, 1) with { HorizontalMerge = true })
            .WithCell(1, 2, new TableCell(TextBody.FromText("Zelle"), new TableCellProperties(LeftMargin: 0, Anchor: TextAnchor.Bottom, TopBorder: new Outline(12700, new SolidFill(ColorSpec.Rgb("00FF00"))), Fill: new SolidFill(ColorSpec.Rgb("EEEEEE")))));
        var frame = new GraphicFrame { Id = 40, Name = "Tabelle 39", Kind = GraphicKind.Table, Table = table, GraphicUri = "http://schemas.openxmlformats.org/drawingml/2006/table", Transform = new Transform2D(914400, 4000000, 6000000, 741680) };

        var first = presentation.Slides[0];
        first = first with
        {
            Shapes = first.Shapes.AddRange([shape, connector, picture, tiled, patterned, group, frame]),
            Background = new Background(gradient),
            Transition = new SlideTransition(TransitionKind.Push, "d", 1200, AdvanceAfterMs: 5000),
            Timing = new SlideTiming(
            [
                AnimationPresets.Create(AnimationPreset.FlyIn, 20, direction: AnimationDirection.Left),
                AnimationPresets.Create(AnimationPreset.Fade, 22, AnimationTrigger.WithPrevious),
                AnimationPresets.Create(AnimationPreset.Spin, 30, AnimationTrigger.AfterPrevious, delayMs: 250),
                AnimationPresets.Create(AnimationPreset.WipeOut, 22, AnimationTrigger.OnClick, direction: AnimationDirection.Top),
                AnimationPresets.Create(AnimationPreset.MotionLine, 40, AnimationTrigger.OnClick, direction: AnimationDirection.Right),
            ]),
            Notes = new NotesSlide
            {
                Shapes =
                [
                    new AutoShape { Id = 2, Name = "Folienbildplatzhalter 1", Placeholder = new PlaceholderInfo(PlaceholderType.SlideImage) },
                    new AutoShape { Id = 3, Name = "Notizenplatzhalter 2", Placeholder = new PlaceholderInfo(PlaceholderType.Body, 1), Text = TextBody.FromText("Erste Notiz\nZweite Zeile") },
                ],
            },
        };
        var second = presentation.Slides[1] with
        {
            Hidden = true,
            Transition = new SlideTransition(TransitionKind.Fade, ThroughBlack: true, Speed: "fast", AdvanceOnClick: false),
            ColorMapOverride = new ColorMap("dk1", "lt1", "dk2", "lt2"),
        };
        var third = presentation.Slides[2] with
        {
            Transition = new SlideTransition(TransitionKind.Morph, DurationMs: 2000),
            Background = new Background(new SolidFill(ColorSpec.Scheme("accent5").LumMod(20000).LumOff(80000))),
        };
        return presentation with
        {
            Slides = [first, second, third],
            Sections = [new SlideSection("Einleitung", "{11111111-2222-3333-4444-555555555555}", [256, 257]), new SlideSection("Schluss", "{66666666-7777-8888-9999-000000000000}", [258])],
            Title = "Testpräsentation",
        };
    }

    [Fact]
    public void A_new_presentation_is_a_valid_package_that_reads_back_the_same()
    {
        var presentation = MasterFactory.CreatePresentation();
        var bytes = Write(presentation);
        AssertValid(bytes);
        AssertSameModel(presentation, Read(bytes));
    }

    [Fact]
    public void Every_built_in_design_and_both_sizes_write_valid_packages()
    {
        foreach (var design in BuiltInThemes.All)
        {
            foreach (var size in new[] { SlideSize.Widescreen, SlideSize.Standard })
            {
                var presentation = MasterFactory.CreatePresentation(design, size);
                var read = RoundTrip(presentation);
                AssertSameModel(presentation, read);
                Assert.Equal(design.Theme.Fonts.Major.Latin, read.Masters[0].Theme.Fonts.Major.Latin);
            }
        }
    }

    [Fact]
    public void Every_modelled_kind_survives_a_round_trip()
    {
        var presentation = Rich();
        var read = RoundTrip(presentation);
        AssertSameModel(presentation, read);
        Assert.Equal("Testpräsentation", read.Title);
        Assert.Equal(2, read.Sections.Count);
        Assert.True(read.Slides[1].Hidden);
        Assert.Equal("Erste Notiz\nZweite Zeile", read.Slides[0].NotesText);
        Assert.NotNull(read.NotesMasterPartUri);
    }

    [Fact]
    public void Reading_what_was_written_is_stable()
    {
        var once = RoundTrip(Rich());
        var twice = RoundTrip(once);
        AssertSameModel(once, twice);

        // Written a second time without a change, every slide part stays as it was.
        var first = Write(once);
        var second = Write(Read(first));
        Assert.Equal(SlideXml(first), SlideXml(second));
    }

    private static IReadOnlyList<string> SlideXml(byte[] bytes)
    {
        using var package = DocumentFormat.OpenXml.Packaging.PresentationDocument.Open(new MemoryStream(bytes), false);
        return package.PresentationPart!.SlideParts.OrderBy(part => part.Uri.OriginalString).Select(part =>
        {
            using var reader = new StreamReader(part.GetStream());
            return reader.ReadToEnd();
        }).ToList();
    }

    [Fact]
    public void Slides_can_be_reordered_deleted_and_added_and_layouts_change()
    {
        var read = RoundTrip(Rich());
        var layout = read.Masters[0].Layouts.First(item => item.Type == "twoObj");
        var added = MasterFactory.CreateSlide(layout, read.NextSlideId);
        var changed = read with
        {
            Slides = [read.Slides[2] with { LayoutId = layout.Id }, read.Slides[0], added],
            Sections = default,
        };
        var again = RoundTrip(changed);
        Assert.Equal([258u, 256u, added.Id], again.Slides.Select(slide => slide.Id).ToList());
        Assert.Equal("twoObj", again.LayoutOf(again.Slides[0]).Type);
        Assert.Equal("twoObj", again.LayoutOf(again.Slides[2]).Type);
        AssertSameModel(changed, again);
    }

    [Fact]
    public void A_duplicated_slide_gets_a_part_of_its_own_with_its_pictures()
    {
        var read = RoundTrip(Rich());
        var copy = read.Slides[0] with { Id = read.NextSlideId, Notes = null };
        var again = RoundTrip(read with { Slides = read.Slides.Insert(1, copy), Sections = default });
        Assert.Equal(4, again.Slides.Count);
        Assert.NotEqual(again.Slides[0].PartUri, again.Slides[1].PartUri);
        var picture = ShapeTree.All(again.Slides[1].Shapes).OfType<PictureShape>().Single();
        Assert.NotNull(picture.Picture.Image);
        Assert.Equal(again.Slides[0].Shapes.Count, again.Slides[1].Shapes.Count);
    }

    [Fact]
    public void Changing_the_theme_replaces_the_master()
    {
        var read = RoundTrip(Rich());
        var design = MasterFactory.CreateMaster(BuiltInThemes.Natur, read.SlideSize, read.NextMasterOrLayoutId);
        var slides = read.Slides.Select(slide => slide with { LayoutId = design.Layouts.First(layout => layout.Type == read.LayoutOf(slide).Type).Id }).ToList();
        var again = RoundTrip(read with { Masters = [design], Slides = slides });
        Assert.Single(again.Masters);
        Assert.Equal("Natur", again.Masters[0].Theme.Name);
        Assert.Equal("Georgia", again.Masters[0].Theme.Fonts.Major.Latin);
    }
}
