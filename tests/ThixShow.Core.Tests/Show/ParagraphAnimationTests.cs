using ThixOffice.Text.Drawing;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;
using ThixShow.Core.Show;
using static ThixShow.Core.Tests.Formats.PptxTestSupport;

namespace ThixShow.Core.Tests.Show;

/// <summary>Effects "nach Absätzen": each paragraph comes on its own click, the shape and the other paragraphs stay.</summary>
public class ParagraphAnimationTests
{
    [Fact]
    public void Paragraphs_appear_one_click_after_the_other_and_survive_pptx()
    {
        var presentation = TestDecks.Blank(TestDecks.TextBox(10, "Erster Punkt\nZweiter Punkt\nDritter Punkt"));
        var session = new EditingSession(presentation);
        session.GoToSlide(presentation.Slides[0].Id);
        session.SelectShapes([10]);
        session.AddAnimationByParagraph(AnimationPreset.Appear);

        var effects = session.CurrentSlide!.Timing!.MainSequence;
        Assert.Equal([0, 1, 2], effects.Select(effect => effect.ParagraphStart ?? -1));
        var read = Read(Write(session.Presentation));
        Assert.Equal([0, 1, 2], read.Slides[0].Timing!.MainSequence.Select(effect => effect.ParagraphStart ?? -1));

        var show = new SlideShow(read, TestDecks.Measurer);
        show.Start(TimeSpan.Zero);
        string Visible(TimeSpan now) => string.Join("|", show.Frame(now).Layers.SelectMany(layer => layer.Items).OfType<GlyphRunItem>()
            .Where(run => run.Color.A > 0).Select(run => run.Text));
        Assert.DoesNotContain("Erster", Visible(TimeSpan.FromSeconds(1)), StringComparison.Ordinal);

        show.Next(TimeSpan.FromSeconds(1));
        var first = Visible(TimeSpan.FromSeconds(3));
        Assert.Contains("Erster Punkt", first, StringComparison.Ordinal);
        Assert.DoesNotContain("Zweiter", first, StringComparison.Ordinal);

        show.Next(TimeSpan.FromSeconds(3));
        var second = Visible(TimeSpan.FromSeconds(5));
        Assert.Contains("Zweiter Punkt", second, StringComparison.Ordinal);
        Assert.DoesNotContain("Dritter", second, StringComparison.Ordinal);
    }
}
