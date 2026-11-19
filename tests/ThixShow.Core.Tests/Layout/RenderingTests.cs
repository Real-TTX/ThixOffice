using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Layout;

public class RenderingTests
{
    private static readonly ITextMeasurer Measurer = TestDecks.Measurer;

    [Fact]
    public void Rendering_is_deterministic()
    {
        var presentation = TestDecks.Deck(3);
        foreach (var slide in presentation.Slides)
        {
            var first = TestDecks.Describe(SlideRenderer.Render(presentation, slide, Measurer).Items);
            var second = TestDecks.Describe(SlideRenderer.Render(presentation, slide, Measurer).Items);
            Assert.Equal(first, second);
            Assert.Contains("Folie", first);
        }
    }

    [Fact]
    public void Background_comes_first_and_shapes_follow_in_z_order()
    {
        var presentation = TestDecks.Blank(
            TestDecks.Rectangle(10, 0, 0, 1270000, 1270000, "accent1"),
            TestDecks.Rectangle(11, 0, 0, 1270000, 1270000, "accent2"));
        var page = SlideRenderer.Render(presentation, presentation.Slides[0], Measurer);
        var background = Assert.IsType<RectangleItem>(page.Items[0]);
        Assert.Equal(RgbColor.White, background.Fill);
        Assert.Equal(960, background.Width);
        var fills = page.Items.OfType<VectorPathItem>().Select(item => ((SolidPaint)item.Fill!).Color.ToHex()).ToList();
        Assert.Equal(["C43E1C", "1F6FB2"], fills);
        Assert.Equal(960, page.Width);
        Assert.Equal(540, page.Height);
    }

    [Fact]
    public void Rotation_and_flips_are_transforms_around_the_centre()
    {
        var shape = TestDecks.Rectangle(10, 1270000, 1270000, 2540000, 1270000, "accent1") with
        {
            Transform = new Transform2D(1270000, 1270000, 2540000, 1270000, Rotation: 90 * 60000),
        };
        var presentation = TestDecks.Blank(shape);
        var corners = SlideHitTesting.CornersOf(presentation, presentation.Slides[0], 10);
        // A 200 × 100 pt box at (100, 100) turned by 90° about its centre (200, 150): top-left goes to (250, 50).
        Assert.Equal(250, corners[0].X, 6);
        Assert.Equal(50, corners[0].Y, 6);
        var bounds = SlideHitTesting.BoundsOf(presentation, presentation.Slides[0], 10);
        Assert.Equal((150.0, 50.0, 250.0, 250.0), (Math.Round(bounds.Left, 6), Math.Round(bounds.Top, 6), Math.Round(bounds.Right, 6), Math.Round(bounds.Bottom, 6)));

        var flipped = shape with { Transform = new Transform2D(0, 0, 1270000, 1270000, FlipHorizontal: true) };
        var matrix = SlideRenderer.ShapeMatrix(flipped.Transform!);
        Assert.Equal((100.0, 0.0), matrix.Apply(0, 0));
    }

    [Fact]
    public void Group_children_map_from_the_child_space()
    {
        var child = TestDecks.Rectangle(11, 0, 0, 1000, 1000, "accent1");
        var group = new GroupShape
        {
            Id = 10,
            Name = "Gruppe 9",
            Transform = new Transform2D(1270000, 2540000, 2540000, 1270000, ChildX: 0, ChildY: 0, ChildWidth: 2000, ChildHeight: 1000),
            Children = [child],
        };
        var presentation = TestDecks.Blank(group);
        var corners = SlideHitTesting.CornersOf(presentation, presentation.Slides[0], 11);
        // The child covers the left half of the group's child space: (100, 200) to (200, 300) on the slide.
        Assert.Equal((100.0, 200.0), (Math.Round(corners[0].X, 6), Math.Round(corners[0].Y, 6)));
        Assert.Equal((200.0, 300.0), (Math.Round(corners[2].X, 6), Math.Round(corners[2].Y, 6)));
        var page = SlideRenderer.Render(presentation, presentation.Slides[0], Measurer);
        Assert.Equal(2, page.Items.OfType<TransformBeginItem>().Count());
        Assert.Equal(page.Items.OfType<TransformBeginItem>().Count(), page.Items.OfType<TransformEndItem>().Count());
    }

    [Fact]
    public void Text_wraps_and_aligns_with_the_fixed_width_measurer()
    {
        // 18 pt text: 9 pt per character, 4.5 pt per space. Box 200 pt wide minus 7.2 pt insets each side = 185.6 pt.
        var box = TestDecks.TextBox(10, "aaaa bbbb cccc dddd eeee ffff gggg", width: 2540000);
        var presentation = TestDecks.Blank(box);
        var context = SlideContext.For(presentation, presentation.Slides[0]);
        var resolved = ShapeResolver.Resolve(context, box);
        var layout = TextBodyLayouter.Layout(resolved, box.Text!, context, Measurer, 0, 0, 200, 72);
        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal(0, layout.Lines[0].Start);
        Assert.Equal(20, layout.Lines[1].Start); // four words of 40.5 pt fit into 185.6 pt, the fifth does not
        Assert.Equal(7.2, layout.Lines[0].CaretX[0], 6);
        var runs = layout.Items.OfType<GlyphRunItem>().ToList();
        Assert.Equal(18, runs[0].Font.Size);
        Assert.Equal("Aptos", runs[0].Font.Family);
    }

    [Fact]
    public void Normal_autofit_shrinks_the_text_until_it_fits()
    {
        var presentation = TestDecks.With("obj");
        var slide = presentation.Slides[0];
        var body = slide.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.Type == PlaceholderType.Object);
        var longText = string.Join("\n", Enumerable.Range(1, 30).Select(index => $"Punkt {index} mit etwas Text"));
        var filled = body with { Text = TextBody.FromText(longText) };
        slide = slide with { Shapes = ShapeTree.Replace(slide.Shapes, body.Id, _ => filled) };
        presentation = presentation with { Slides = [slide] };
        var context = SlideContext.For(presentation, slide);
        var resolved = ShapeResolver.Resolve(context, filled);
        Assert.IsType<NormalAutoFit>(resolved.Body.AutoFit);
        var (width, height) = (Units.EmuToPoints(resolved.Transform.Width), Units.EmuToPoints(resolved.Transform.Height));
        var layout = TextBodyLayouter.Layout(resolved, filled.Text!, context, Measurer, 0, 0, width, height);
        Assert.True(layout.FontScale < 1);
        Assert.Equal(0.2, layout.LineSpacingReduction);
        Assert.False(layout.Overflows);

        var noFit = resolved with { Body = resolved.Body with { AutoFit = NoAutoFit.Instance } };
        Assert.True(TextBodyLayouter.Layout(noFit, filled.Text!, context, Measurer, 0, 0, width, height).Overflows);
    }

    [Fact]
    public void Bullets_and_numbering_are_drawn_before_the_text()
    {
        var presentation = TestDecks.With("obj");
        var slide = presentation.Slides[0];
        var body = slide.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.Type == PlaceholderType.Object);
        var numbered = new ParagraphProperties(Bullet: new AutoNumberBullet("arabicPeriod"));
        var text = new TextBody(BodyProperties.Empty, ListStyle.Empty,
        [
            TextParagraph.Plain("Eins", paragraph: numbered),
            TextParagraph.Plain("Zwei", paragraph: numbered),
            TextParagraph.Plain("Unterpunkt", paragraph: new ParagraphProperties(Level: 1)),
            TextParagraph.Plain("Drei", paragraph: numbered),
        ]);
        slide = slide with { Shapes = ShapeTree.Replace(slide.Shapes, body.Id, shape => ((AutoShape)shape) with { Text = text }) };
        presentation = presentation with { Slides = [slide] };
        var texts = SlideRenderer.Render(presentation, slide, Measurer).Items.OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.Equal(["1.", "Eins", "2.", "Zwei", "–", "Unterpunkt", "3.", "Drei"], texts);
        Assert.Equal("iv.", TextBodyLayouter.Number("romanLcPeriod", 4));
        Assert.Equal("(c)", TextBodyLayouter.Number("alphaLcParenBoth", 3));
        Assert.Equal("AA)", TextBodyLayouter.Number("alphaUcParenR", 27));
    }

    [Fact]
    public void Empty_placeholders_are_invisible_but_show_prompts_in_the_editor()
    {
        var presentation = MasterFactory.CreatePresentation();
        var slide = presentation.Slides[0];
        var shown = SlideRenderer.Render(presentation, slide, Measurer);
        Assert.Empty(shown.Items.OfType<GlyphRunItem>());
        var editor = SlideRenderer.Render(presentation, slide, Measurer, new RenderOptions(ShowPlaceholderPrompts: true));
        var texts = editor.Items.OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.Contains("Titel durch Klicken hinzufügen", string.Join(" ", texts));
        Assert.Contains(editor.Items.OfType<VectorPathItem>(), item => item.Stroke?.Dashes is not null);
    }

    [Fact]
    public void Slide_number_fields_show_the_slide_number()
    {
        var presentation = TestDecks.Deck(3);
        var layout = presentation.LayoutOf(presentation.Slides[2]);
        var numberPlaceholder = (AutoShape)layout.Shapes.First(shape => shape.Placeholder?.Type == PlaceholderType.SlideNumber);
        var number = numberPlaceholder with
        {
            Id = 50,
            Transform = null,
            Text = new TextBody(BodyProperties.Empty, ListStyle.Empty, [new TextParagraph(ParagraphProperties.Empty, [new TextField("{1}", "slidenum", "‹Nr.›", RunProperties.Empty)])]),
        };
        var slide = presentation.Slides[2] with { Shapes = presentation.Slides[2].Shapes.Add(number) };
        presentation = presentation with { Slides = presentation.Slides.SetItem(2, slide) };
        var texts = SlideRenderer.Render(presentation, slide, Measurer).Items.OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.Contains("3", texts);
    }

    [Fact]
    public void Lines_get_arrowheads_and_dashes()
    {
        var line = new ConnectorShape
        {
            Id = 10,
            Name = "Gerade Verbindung 9",
            Transform = new Transform2D(0, 0, 1270000, 0),
            Geometry = ShapeGeometry.Of("straightConnector1"),
            Outline = new Outline(25400, new SolidFill(ColorSpec.Rgb("FF0000")), new LineDash("dash"), Tail: new LineEnd(LineEndKind.Triangle)),
        };
        var presentation = TestDecks.Blank(line);
        var paths = SlideRenderer.Render(presentation, presentation.Slides[0], Measurer).Items.OfType<VectorPathItem>().ToList();
        Assert.Equal(2, paths.Count);
        Assert.Equal([8.0, 6.0], paths[0].Stroke!.Dashes!);
        Assert.Equal("M 100 0 L 94 3 L 94 -3 Z", paths[1].Path.ToString());
    }

    [Fact]
    public void Gradients_and_pictures_become_paints()
    {
        var gradient = new GradientFill([new GradientStop(0, ColorSpec.Rgb("FF0000")), new GradientStop(100000, ColorSpec.Rgb("0000FF"))], LinearAngle: 5400000);
        var shape = TestDecks.Rectangle(10, 0, 0, 1270000, 2540000) with { Fill = gradient };
        var presentation = TestDecks.Blank(shape);
        var paint = Assert.IsType<LinearGradientPaint>(SlideRenderer.Render(presentation, presentation.Slides[0], Measurer).Items.OfType<VectorPathItem>().Single().Fill);
        // 90°: top to bottom through the centre of the 100 × 200 box.
        Assert.Equal((50.0, 0.0, 50.0, 200.0), (Math.Round(paint.X1, 6), Math.Round(paint.Y1, 6), Math.Round(paint.X2, 6), Math.Round(paint.Y2, 6)));

        var picture = new PictureShape
        {
            Id = 11,
            Name = "Bild 10",
            Transform = new Transform2D(0, 0, 1270000, 1270000),
            Picture = new PictureFill(ImageData.From(TestImages.Png(4, 2)), new RelativeRect(25000, 0, 25000, 0), StretchMode.Full),
        };
        presentation = TestDecks.Blank(picture);
        var image = Assert.IsType<ImagePaint>(SlideRenderer.Render(presentation, presentation.Slides[0], Measurer).Items.OfType<VectorPathItem>().Single().Fill);
        Assert.Equal(0.25, image.SourceLeft);
        Assert.Equal(100, image.Width);
    }

    [Fact]
    public void Tables_grow_rows_to_their_text_and_use_the_style()
    {
        var table = Table.Create(2, 2, 2540000, 254000);
        table = table.WithCell(1, 0, table.CellAt(1, 0) with { Text = TextBody.FromText("eins zwei drei vier fünf sechs sieben") });
        var frame = new GraphicFrame { Id = 10, Name = "Tabelle 9", Kind = GraphicKind.Table, Table = table, Transform = new Transform2D(0, 0, 2540000, 508000) };
        var presentation = TestDecks.Blank(frame);
        var context = SlideContext.For(presentation, presentation.Slides[0]);
        var geometry = TableRenderer.Measure(context, table, Measurer);
        Assert.Equal(25.2, geometry.RowY[1], 6); // an empty 18 pt line and the margins are taller than the 20 pt row
        Assert.True(geometry.RowY[2] - geometry.RowY[1] > 40);
        Assert.Equal(4, geometry.Cells.Count);
        Assert.True(geometry.Cells[0].Look.Bold);
        var items = SlideRenderer.Render(presentation, presentation.Slides[0], Measurer).Items;
        Assert.Contains(items.OfType<VectorPathItem>(), item => item.Fill is SolidPaint { Color: var color } && color.ToHex() == "C43E1C");
    }

    [Fact]
    public void Charts_use_the_seam_and_other_frames_show_a_placeholder()
    {
        var chart = new GraphicFrame { Id = 10, Name = "Diagramm 9", Kind = GraphicKind.Chart, ChartPartUri = "/ppt/charts/chart1.xml", Transform = new Transform2D(0, 0, 1270000, 1270000) };
        var presentation = TestDecks.Blank(chart);
        var plain = SlideRenderer.Render(presentation, presentation.Slides[0], Measurer).Items;
        Assert.Contains(plain.OfType<GlyphRunItem>(), run => run.Text == "Diagramm");
        var seam = new FakeCharts();
        var drawn = SlideRenderer.Render(presentation, presentation.Slides[0], Measurer, new RenderOptions(Charts: seam)).Items;
        Assert.Equal("/ppt/charts/chart1.xml", seam.Seen);
        Assert.Contains(drawn.OfType<RectangleItem>(), item => item.Fill == new RgbColor(1, 2, 3));
    }

    private sealed class FakeCharts : IChartRenderer
    {
        public string? Seen { get; private set; }

        public IReadOnlyList<DisplayItem>? Render(GraphicFrame frame, double width, double height, SlideContext context, ITextMeasurer measurer)
        {
            Seen = frame.ChartPartUri;
            return [new RectangleItem(0, 0, width, height, new RgbColor(1, 2, 3))];
        }
    }

    [Fact]
    public void Thumbnails_scale_to_the_requested_width()
    {
        var presentation = TestDecks.Deck(1);
        var thumbnail = SlideRenderer.RenderThumbnail(presentation, presentation.Slides[0], Measurer, 192);
        Assert.Equal(192, thumbnail.Width, 6);
        Assert.Equal(108, thumbnail.Height, 6);
        var scale = Assert.IsType<TransformBeginItem>(thumbnail.Items[0]);
        Assert.Equal(0.2, scale.Matrix.M11, 6);
    }

    [Fact]
    public void Hit_testing_finds_the_topmost_shape_and_groups()
    {
        var child = TestDecks.Rectangle(21, 0, 0, 1270000, 1270000, "accent1");
        var group = new GroupShape { Id = 20, Name = "Gruppe", Transform = new Transform2D(3810000, 0, 1270000, 1270000, ChildWidth: 1270000, ChildHeight: 1270000), Children = [child] };
        var presentation = TestDecks.Blank(
            TestDecks.Rectangle(10, 0, 0, 1270000, 1270000, "accent1"),
            TestDecks.Rectangle(11, 635000, 0, 1270000, 1270000, "accent2"),
            group);
        var slide = presentation.Slides[0];
        Assert.Equal(11, SlideHitTesting.HitTest(presentation, slide, 60, 50)!.ShapeId);
        Assert.Equal(10, SlideHitTesting.HitTest(presentation, slide, 20, 50)!.ShapeId);
        Assert.Null(SlideHitTesting.HitTest(presentation, slide, 500, 500));
        var inGroup = SlideHitTesting.HitTest(presentation, slide, 320, 50)!;
        Assert.Equal(21, inGroup.ShapeId);
        Assert.Equal(20, inGroup.SelectableId(null));
        Assert.Equal(21, inGroup.SelectableId(20));

        var handles = SlideHitTesting.HandlesOf(presentation, slide, 10);
        Assert.Equal(9, handles.Count);
        Assert.Equal(HandleKind.Rotate, SlideHitTesting.HandleAt(handles, 50, -18)!.Kind);
        var round = TestDecks.Rectangle(30, 0, 0, 1270000, 635000, "accent1", "roundRect");
        presentation = TestDecks.Blank(round);
        Assert.Single(SlideHitTesting.HandlesOf(presentation, presentation.Slides[0], 30), handle => handle.Kind == HandleKind.Adjust);
    }

    [Fact]
    public void Handouts_and_notes_pages()
    {
        var presentation = TestDecks.Deck(7);
        var slide = presentation.Slides[0] with
        {
            Notes = new NotesSlide
            {
                Shapes = [new AutoShape { Id = 3, Name = "Notizenplatzhalter 2", Placeholder = new PlaceholderInfo(PlaceholderType.Body, 1), Text = TextBody.FromText("Daran denken: Zahlen nennen") }],
            },
        };
        presentation = presentation with { Slides = presentation.Slides.SetItem(0, slide) };
        Assert.Equal(2, PrintLayouts.Handouts(presentation, Measurer, 6).Count);
        Assert.Equal(3, PrintLayouts.Handouts(presentation, Measurer, 3).Count);
        Assert.Equal(7, PrintLayouts.Handouts(presentation, Measurer, 1).Count);
        var notes = PrintLayouts.NotesPages(presentation, Measurer);
        Assert.Equal(7, notes.Count);
        Assert.Contains(notes[0].Items.OfType<GlyphRunItem>(), run => run.Text.Contains("Zahlen", StringComparison.Ordinal));
        Assert.Equal(540, notes[0].Width, 6);

        var hidden = presentation with { Slides = presentation.Slides.SetItem(1, presentation.Slides[1] with { Hidden = true }) };
        Assert.Equal(6, PrintLayouts.Slides(hidden, Measurer).Count);
        Assert.Equal(7, PrintLayouts.Slides(hidden, Measurer, includeHidden: true).Count);
    }
}
