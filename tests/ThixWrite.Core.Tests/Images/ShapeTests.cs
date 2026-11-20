using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Images;

/// <summary>Shapes and text boxes ThixWrite makes: insert, format, text, wrap, the docx round trip, the layout.</summary>
public sealed class ShapeTests
{
    private static readonly DrawingAnchor Front = new(DrawingFrame.Column, 50, null, DrawingFrame.Paragraph, 20, null, DrawingWrap.None, false, 9, 0, 9, 0, 251659264);

    [Fact]
    public void A_shape_is_inserted_floating_formatted_and_written_as_a_wps_shape_that_reads_back_the_same()
    {
        var session = new EditingSession(new Document([new Paragraph("Text neben der Form")], BuiltInStyles.CreateDefault()));
        session.InsertShape(new ShapeInline(EditingSession.DefaultShape("star5"), 100, 80, CharacterProperties.Empty) { Anchor = Front }, At(0, 0));
        Assert.IsType<ShapeInline>(session.SelectedObject!.Value.Object);
        session.SetShapeFill(new DrawingFill(DrawingColor.Of("FFC000")));
        session.SetShapeLine(null);
        session.SetObjectWrap(PictureWrap.Square);
        session.SetShapeText(session.SelectedObject!.Value.Position, new Story([new Paragraph("Neu!")]));

        var written = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(written);
        var xml = DocxTestSupport.DocumentXml(written);
        Assert.Contains("wps:wsp", xml);
        Assert.Contains("prst=\"star5\"", xml);
        Assert.Contains("FFC000", xml);
        Assert.Contains("wp:wrapSquare", xml);
        Assert.Contains("txbxContent", xml);

        var reread = DocxTestSupport.Read(written);
        var shape = Assert.IsType<ShapeInline>(reread.ParagraphAt(0)!.Inlines[0]);
        Assert.Equal(session.SelectedShape, shape);
        Assert.Equal("Neu!", ((Paragraph)shape.Shape.Text!.Blocks[0]).Text);

        // Unchanged, it is written as read.
        var again = DocxTestSupport.Read(DocxTestSupport.Write(reread));
        Assert.Equal(shape.SourceXml, ((ShapeInline)again.ParagraphAt(0)!.Inlines[0]).SourceXml);
    }

    [Fact]
    public void A_floating_shape_is_drawn_where_its_anchor_puts_it_and_the_text_flows_around_it()
    {
        var session = new EditingSession(new Document([new Paragraph(Words(120))], BuiltInStyles.CreateDefault()));
        session.InsertShape(new ShapeInline(EditingSession.DefaultShape("ellipse"), 120, 120, CharacterProperties.Empty) { Anchor = Front with { Wrap = DrawingWrap.Square } }, At(0, 0));
        using var layout = NewLayout(session.Document);
        var placed = layout.Pages[0].Floats.Single();
        var section = session.Document.FinalSection;
        Assert.Equal(section.MarginLeft + 50, placed.X, 1);
        Assert.Contains(layout.GetDisplayList(0).Items, item => item is VectorPathItem { Fill: not null });
        var beside = layout.Pages[0].Lines.Where(line => line.Top < placed.Y + placed.Height && line.Top + line.Line.Height > placed.Y).ToList();
        Assert.NotEmpty(beside);
        Assert.All(beside, line => Assert.True(line.Left + line.Line.Items.Where(item => item.Width > 0).Select(item => item.X).DefaultIfEmpty(0).Min() >= placed.X + placed.Width - 1
                                                 || line.Left + line.Line.Items.Select(item => item.X + item.Width).DefaultIfEmpty(0).Max() <= placed.X + 3));
    }
}
