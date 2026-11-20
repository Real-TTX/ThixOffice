using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Layouts;

/// <summary>Layout rules that make documents from Word look as they do in Word (borders take room, text around objects across pages …).</summary>
public sealed class FidelityLayoutTests
{
    private static readonly ParagraphProperties Plain = new() { SpacingBefore = 0, SpacingAfter = 0, LineSpacing = LineSpacing.Single };

    [Fact]
    public void Paragraph_borders_take_their_room_above_and_below_and_a_box_of_equal_paragraphs_takes_it_once()
    {
        var border = new BorderLine("single", 1.5, "auto", 4);
        var framed = Plain with { Borders = new BorderSet(border, border, border, border) };
        var document = Create([new Paragraph("vorher", Plain), new Paragraph("eins", framed), new Paragraph("zwei", framed), new Paragraph("nachher", Plain)]);
        using var layout = NewLayout(document);
        var lines = layout.Pages[0].Lines;
        var height = lines[0].Line.Height;
        Assert.Equal(lines[0].Top + height + 5.5, lines[1].Top, 3); // the top line and its distance
        Assert.Equal(lines[1].Top + height, lines[2].Top, 3); // the same box: no line between the two
        Assert.Equal(lines[2].Top + height + 5.5, lines[3].Top, 3); // the bottom line and its distance

        var plain = Create([new Paragraph("vorher", Plain), new Paragraph("eins", Plain with { Shading = new Shading("FFF2CC") }), new Paragraph("nachher", Plain)]);
        using var shaded = NewLayout(plain);
        Assert.Equal(shaded.Pages[0].Lines[0].Top + height, shaded.Pages[0].Lines[1].Top, 3); // shading alone takes no room
    }

    [Fact]
    public void A_paragraph_that_goes_on_on_the_next_page_gets_the_full_width_there_when_the_object_it_flowed_around_stays_behind()
    {
        // Filler down to near the foot of the page, then a long paragraph with a tall square-wrapped object at the right of its column.
        var setup = SectionProperties.Default;
        using var probe = NewLayout(Create([new Paragraph("x", Plain)]));
        var lineHeight = probe.Pages[0].Lines[0].Line.Height;
        var filler = Enumerable.Range(0, (int)((setup.ContentHeight - 60) / lineHeight)).Select(_ => (Block)new Paragraph("x", Plain)).ToList();
        var anchor = new DrawingAnchor(DrawingFrame.Column, 0, "right", DrawingFrame.Paragraph, 0, null, DrawingWrap.Square, false, 9, 0, 9, 0);
        var shape = new OpaqueInline("<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\"/>", PreservedKind.Drawing, true, CharacterProperties.Empty)
        {
            Graphic = new DrawingGraphic(150, 400, anchor, EquatableArray<DrawingElement>.Empty),
        };
        filler.Add(new Paragraph([shape, new TextRun(Words(400), CharacterProperties.Empty)], Plain));
        var document = Create(filler);
        using var layout = NewLayout(document);
        var block = filler.Count - 1;
        var placed = layout.Pages[0].Floats.Single();
        var first = layout.Pages[0].Lines.Where(line => line.Block == block).ToList();
        var next = layout.Pages[1].Lines.Where(line => line.Block == block).ToList();
        Assert.NotEmpty(first);
        Assert.NotEmpty(next);
        static double Right(PlacedLine line) => line.Left + line.Line.Items.Max(item => item.X + item.Width);
        Assert.All(first, line => Assert.True(Right(line) <= placed.X - 9 + 3)); // beside the object on its page
        Assert.Contains(next, line => Right(line) > placed.X + 10); // the whole width on the next page

        // The text of the paragraph is all there, once, in order.
        var offsets = first.Concat(next).Select(line => (line.Line.Start, line.Line.End)).ToList();
        for (var index = 1; index < offsets.Count; index++)
        {
            Assert.Equal(offsets[index - 1].End, offsets[index].Start);
        }

        Assert.Equal(((Paragraph)document.Blocks[block]).Length, offsets[^1].End);
    }
}
