using ThixShow.Core.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Model;

public class InheritanceTests
{
    private static (Presentation Presentation, Slide Slide) SlideOf(string layoutType, ThemeDesign? design = null)
    {
        var presentation = MasterFactory.CreatePresentation(design);
        var layout = presentation.Masters[0].Layouts.First(item => item.Type == layoutType);
        var slide = MasterFactory.CreateSlide(layout, 300);
        return (presentation with { Slides = [slide] }, slide);
    }

    private static AutoShape PlaceholderOf(Slide slide, PlaceholderType type) =>
        slide.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.Type == type);

    [Fact]
    public void A_new_presentation_has_one_title_slide_and_eight_layouts()
    {
        var presentation = MasterFactory.CreatePresentation();
        Assert.Single(presentation.Slides);
        Assert.Equal(8, presentation.Masters[0].Layouts.Count);
        Assert.Equal("title", presentation.LayoutOf(presentation.Slides[0]).Type);
        Assert.Equal(256u, presentation.Slides[0].Id);
        Assert.Equal(2, presentation.Slides[0].Shapes.Count);
        Assert.True(presentation.Masters[0].Layouts.Select(layout => layout.Id).Distinct().Count() == 8);
        Assert.All(presentation.Masters[0].Layouts, layout => Assert.True(layout.Id > MasterFactory.FirstMasterId));
    }

    [Fact]
    public void Slide_placeholder_without_position_takes_the_layouts_then_the_masters()
    {
        var (presentation, slide) = SlideOf("obj");
        var context = SlideContext.For(presentation, slide);
        var title = ShapeResolver.Resolve(context, PlaceholderOf(slide, PlaceholderType.Title));
        var masterTitle = presentation.Masters[0].Shapes.First(shape => shape.Placeholder?.Type == PlaceholderType.Title);
        Assert.Equal(masterTitle.Transform, title.Transform);
        Assert.Equal(TextAnchor.Center, title.Body.Anchor);

        var (titlePresentation, titleSlide) = SlideOf("title");
        var centered = ShapeResolver.Resolve(SlideContext.For(titlePresentation, titleSlide), PlaceholderOf(titleSlide, PlaceholderType.CenteredTitle));
        var layoutTitle = titlePresentation.Masters[0].Layouts.First(layout => layout.Type == "title").Shapes.First(shape => shape.Placeholder?.Type == PlaceholderType.CenteredTitle);
        Assert.Equal(layoutTitle.Transform, centered.Transform);
        Assert.Equal(TextAnchor.Bottom, centered.Body.Anchor);
    }

    [Fact]
    public void Own_position_wins()
    {
        var (presentation, slide) = SlideOf("obj");
        var moved = PlaceholderOf(slide, PlaceholderType.Title) with { Transform = new Transform2D(1, 2, 3, 4) };
        var resolved = ShapeResolver.Resolve(SlideContext.For(presentation, slide), moved);
        Assert.Equal(new Transform2D(1, 2, 3, 4), resolved.Transform);
    }

    [Fact]
    public void Text_sizes_come_from_the_master_styles_and_the_layout_list_styles()
    {
        var (presentation, slide) = SlideOf("obj");
        var context = SlideContext.For(presentation, slide);
        var title = ShapeResolver.Resolve(context, PlaceholderOf(slide, PlaceholderType.Title));
        Assert.Equal(4400, ShapeResolver.ParagraphOf(title, TextParagraph.Empty).DefaultRun!.Size);

        var body = ShapeResolver.Resolve(context, PlaceholderOf(slide, PlaceholderType.Object));
        var level1 = ShapeResolver.ParagraphOf(body, TextParagraph.Empty);
        Assert.Equal(2800, level1.DefaultRun!.Size);
        Assert.Equal(new CharacterBullet("•"), level1.Bullet);
        var level2 = ShapeResolver.ParagraphOf(body, TextParagraph.Empty with { Properties = new ParagraphProperties(Level: 1) });
        Assert.Equal(2400, level2.DefaultRun!.Size);
        Assert.Equal(new CharacterBullet("–"), level2.Bullet);
        Assert.Equal(685800, level2.LeftMargin);

        var (titlePresentation, titleSlide) = SlideOf("title");
        var titleContext = SlideContext.For(titlePresentation, titleSlide);
        var centered = ShapeResolver.Resolve(titleContext, PlaceholderOf(titleSlide, PlaceholderType.CenteredTitle));
        var centeredParagraph = ShapeResolver.ParagraphOf(centered, TextParagraph.Empty);
        Assert.Equal(6000, centeredParagraph.DefaultRun!.Size);
        Assert.Equal(TextAlignment.Center, centeredParagraph.Alignment);
        var subtitle = ShapeResolver.Resolve(titleContext, PlaceholderOf(titleSlide, PlaceholderType.Subtitle));
        Assert.Equal(NoBullet.Instance, ShapeResolver.ParagraphOf(subtitle, TextParagraph.Empty).Bullet);
    }

    [Fact]
    public void Theme_fonts_and_colours_resolve()
    {
        var (presentation, slide) = SlideOf("obj");
        var context = SlideContext.For(presentation, slide);
        var title = ShapeResolver.Resolve(context, PlaceholderOf(slide, PlaceholderType.Title));
        var run = ShapeResolver.Effective(ShapeResolver.RunOf(ShapeResolver.ParagraphOf(title, TextParagraph.Empty), RunProperties.Empty), context);
        Assert.Equal("Aptos Display", run.Family);
        Assert.Equal(44, run.Size);
        Assert.Equal("1F1F1F", run.Color.ToHex());

        var (darkPresentation, darkSlide) = SlideOf("obj", BuiltInThemes.Kontrast);
        var darkContext = SlideContext.For(darkPresentation, darkSlide);
        var darkTitle = ShapeResolver.Resolve(darkContext, PlaceholderOf(darkSlide, PlaceholderType.Title));
        var darkRun = ShapeResolver.Effective(ShapeResolver.RunOf(ShapeResolver.ParagraphOf(darkTitle, TextParagraph.Empty), RunProperties.Empty), darkContext);
        Assert.Equal("F5F5F5", darkRun.Color.ToHex());
        Assert.Equal("111418", ColorResolver.ResolveFill(ShapeResolver.ResolveBackground(darkContext), darkContext.Colors)!.Value.ToHex());
    }

    [Fact]
    public void Shape_style_references_give_fill_line_and_font_colour()
    {
        var (presentation, slide) = SlideOf("blank");
        var shape = new AutoShape
        {
            Id = 10,
            Name = "Rechteck 9",
            Transform = new Transform2D(0, 0, 1000000, 500000),
            Geometry = ShapeGeometry.Rectangle,
            Style = new ShapeStyle(
                new StyleReference(2, ColorSpec.Scheme("accent1").Shade(50000)),
                new StyleReference(1, ColorSpec.Scheme("accent1")),
                new StyleReference(0, ColorSpec.Scheme("accent1")),
                new FontReference("minor", ColorSpec.Scheme("lt1"))),
            Text = TextBody.FromText("Hallo"),
        };
        var context = SlideContext.For(presentation, slide with { Shapes = [shape] });
        var resolved = ShapeResolver.Resolve(context, shape);
        Assert.Equal("C43E1C", ColorResolver.ResolveFill(resolved.Fill, context.Colors)!.Value.ToHex());
        Assert.Equal(19050, resolved.Outline!.Width);
        Assert.NotEqual("C43E1C", ColorResolver.ResolveFill(resolved.Outline.Fill, context.Colors)!.Value.ToHex());
        var run = ShapeResolver.Effective(ShapeResolver.RunOf(ShapeResolver.ParagraphOf(resolved, resolved.Text!.Paragraphs[0]), RunProperties.Empty), context);
        Assert.Equal("FFFFFF", run.Color.ToHex());
        Assert.Equal("Aptos", run.Family);

        var own = shape with { Fill = new SolidFill(ColorSpec.Rgb("00FF00")), Outline = new Outline(Width: 50800) };
        var ownResolved = ShapeResolver.Resolve(context, own);
        Assert.Equal("00FF00", ColorResolver.ResolveFill(ownResolved.Fill, context.Colors)!.Value.ToHex());
        Assert.Equal(50800, ownResolved.Outline!.Width);
        Assert.NotNull(ownResolved.Outline.Fill);
    }

    [Fact]
    public void Footer_placeholders_of_layouts_take_the_masters_positions()
    {
        var presentation = MasterFactory.CreatePresentation();
        var master = presentation.Masters[0];
        var layout = master.Layouts[1];
        var number = layout.Shapes.First(shape => shape.Placeholder?.Type == PlaceholderType.SlideNumber);
        var resolved = ShapeResolver.Resolve(SlideContext.ForLayout(presentation, master, layout), number);
        Assert.Equal(master.Shapes.First(shape => shape.Placeholder?.Type == PlaceholderType.SlideNumber).Transform, resolved.Transform);
    }

    [Fact]
    public void Value_lists_compare_by_content_and_trees_replace_inside_groups()
    {
        ValueList<int> first = [1, 2, 3];
        ValueList<int> second = [1, 2, 3];
        Assert.Equal(first, second);
        Assert.NotEqual(first, second.Add(4));

        var child = new AutoShape { Id = 5, Name = "Kind" };
        var group = new GroupShape { Id = 4, Name = "Gruppe", Children = [child] };
        ValueList<SlideElement> shapes = [new AutoShape { Id = 2 }, group];
        var renamed = ShapeTree.Replace(shapes, 5, shape => shape with { Name = "Neu" });
        Assert.Equal("Neu", ShapeTree.Find(renamed, 5)!.Name);
        Assert.Equal([4], ShapeTree.PathTo(renamed, 5)!);
        Assert.Null(ShapeTree.Find(ShapeTree.Replace(renamed, 5, _ => null), 5));
        Assert.True(ShapeTree.Replace(shapes, 99, shape => shape).IsSameInstance(shapes));
    }

    [Fact]
    public void Built_in_designs_are_complete()
    {
        Assert.Equal(6, BuiltInThemes.All.Count);
        Assert.Equal(6, BuiltInThemes.All.Select(design => design.Name).Distinct().Count());
        foreach (var design in BuiltInThemes.All)
        {
            Assert.Equal(3, design.Theme.Formats.FillStyles.Count);
            Assert.Equal(3, design.Theme.Formats.LineStyles.Count);
            Assert.Equal(3, design.Theme.Formats.EffectStyles.Count);
            Assert.Equal(3, design.Theme.Formats.BackgroundFillStyles.Count);
            Assert.False(string.IsNullOrEmpty(design.Theme.Fonts.Major.Latin));
        }
    }
}
