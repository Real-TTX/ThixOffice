using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Layout;

/// <summary>Where the editor puts the caret: text laid out on the slide like the renderer draws it (rotation included), clicks mapped back.</summary>
public class TextGeometryTests
{
    [Fact]
    public void The_caret_and_a_click_meet_in_a_rotated_text_box()
    {
        var box = TestDecks.TextBox(10, "Hallo Welt") with { Transform = new Transform2D(914400, 914400, 3657600, 914400, Rotation: 90 * 60000) };
        var presentation = TestDecks.Blank(box);
        var slide = presentation.Slides[0];

        var placed = TextGeometry.Of(presentation, slide, new TextTarget(10), TestDecks.Measurer)!;
        var (top, bottom) = placed.CaretLine(new TextPosition(0, 5));

        Assert.True(Math.Abs(top.Y - bottom.Y) < 0.01, "a turned box has a horizontal caret");
        var middle = ((top.X + bottom.X) / 2, (top.Y + bottom.Y) / 2);
        Assert.Equal(new TextPosition(0, 5), placed.HitTest(middle.Item1, middle.Item2));
    }

    [Fact]
    public void A_click_in_a_table_cell_edits_that_cell()
    {
        var session = new EditingSession(TestDecks.Blank());
        session.GoToSlide(session.Presentation.Slides[0].Id);
        var id = session.InsertTable(2, 2, new EmuRect(0, 0, 2540000, 762000));

        var target = TextGeometry.TargetAt(session.Presentation, session.CurrentSlide!, id, 150, 45, TestDecks.Measurer);

        Assert.Equal(new TextTarget(id, 1, 1), target);
        Assert.NotNull(TextGeometry.Of(session.Presentation, session.CurrentSlide!, target!, TestDecks.Measurer));
    }

    [Fact]
    public void Selected_text_becomes_one_quad_per_line()
    {
        var presentation = TestDecks.Blank(TestDecks.TextBox(10, "Eins\nZwei"));
        var placed = TextGeometry.Of(presentation, presentation.Slides[0], new TextTarget(10), TestDecks.Measurer)!;

        Assert.Equal(2, placed.SelectionQuads(new TextPosition(0, 1), new TextPosition(1, 2)).Count);
        Assert.Empty(placed.SelectionQuads(new TextPosition(0, 1), new TextPosition(0, 1)));
    }

    [Fact]
    public void Slide_titles_and_the_shapes_catalog()
    {
        var presentation = TestDecks.Deck(2);
        Assert.Equal("Folie 2", SlideTitles.TitleOf(presentation, presentation.Slides[1]));
        Assert.Equal("Folie 1", SlideTitles.TitleOf(presentation, presentation.Slides[0]));
        Assert.Contains(ShapeCatalog.Categories, category => category.Name == "Blockpfeile" && category.Presets.Contains("rightArrow"));
        Assert.All(ShapeCatalog.Categories.SelectMany(category => category.Presets), preset => Assert.True(ShapeCatalog.IsLine(preset) || PresetGeometries.IsKnown(preset)));
    }
}
