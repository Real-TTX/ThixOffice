using ThixOffice.Text.Drawing;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using static ThixShow.Core.Tests.Formats.PptxTestSupport;

namespace ThixShow.Core.Tests.Editing;

/// <summary>Kopf- und Fußzeile: date, slide number and footer placeholders on slides, drawn with their fields and kept in pptx.</summary>
public class HeaderFooterTests
{
    [Fact]
    public void Footer_slide_number_and_date_go_onto_the_slides_except_the_title_slide_and_come_back_from_pptx()
    {
        var presentation = MasterFactory.CreatePresentation();
        var session = new EditingSession(presentation, () => new DateTime(2026, 11, 20, 9, 0, 0, DateTimeKind.Utc));
        session.GoToSlide(presentation.Slides[0].Id);
        session.AddSlide();
        var all = session.Presentation.Slides.Select(slide => slide.Id).ToList();

        session.SetHeaderFooter(new HeaderFooterSettings(Date: true, SlideNumber: true, Footer: true, FooterText: "Nordlicht Reisen", NotOnTitleSlide: true), all);

        var title = session.Presentation.Slides[0];
        var content = session.Presentation.Slides[1];
        Assert.DoesNotContain(title.Shapes, shape => shape.Placeholder?.IsFooterKind == true);
        Assert.Equal(3, content.Shapes.Count(shape => shape.Placeholder?.IsFooterKind == true));
        var texts = SlideRenderer.Render(session.Presentation, content, TestDecks.Measurer, new RenderOptions(Now: new DateTime(2026, 11, 20))).Items.OfType<GlyphRunItem>().Select(run => run.Text).ToList();
        Assert.Contains("Nordlicht Reisen", texts);
        Assert.Contains("2", texts);
        Assert.Contains("20.11.2026", texts);

        var bytes = Write(session.Presentation);
        AssertValid(bytes);
        var read = Read(bytes);
        var back = new EditingSession(read).HeaderFooterOf(read.Slides[1]);
        Assert.True(back.Date && back.DateAutomatic && back.SlideNumber && back.Footer);
        Assert.Equal("Nordlicht Reisen", back.FooterText);

        // Switched off again: the placeholders go; one undo brings them back.
        session.SetHeaderFooter(new HeaderFooterSettings(), all);
        Assert.DoesNotContain(session.Presentation.Slides[1].Shapes, shape => shape.Placeholder?.IsFooterKind == true);
        session.Undo();
        Assert.Equal(3, session.Presentation.Slides[1].Shapes.Count(shape => shape.Placeholder?.IsFooterKind == true));
    }

    [Fact]
    public void A_fixed_date_stays_as_typed()
    {
        var presentation = MasterFactory.CreatePresentation();
        var session = new EditingSession(presentation);
        session.SetHeaderFooter(new HeaderFooterSettings(Date: true, DateAutomatic: false, FixedDate: "Herbst 2026"), [presentation.Slides[0].Id]);
        var settings = session.HeaderFooterOf(session.Presentation.Slides[0]);
        Assert.False(settings.DateAutomatic);
        Assert.Equal("Herbst 2026", settings.FixedDate);
    }
}
