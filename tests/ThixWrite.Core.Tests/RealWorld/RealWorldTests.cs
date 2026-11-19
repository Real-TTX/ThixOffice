using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Formatting;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// Regression facts of the real-world corpus: page counts, where things are, how they look (resolved formats, cell fills, text
/// box rectangles, columns), and the docx round trip of every file (re-read equals, unknown parts kept). Laid out with the
/// deterministic fixed-width measurer, so the numbers do not depend on the fonts of the machine.
/// </summary>
public sealed class RealWorldTests
{
    public static TheoryData<string> Files =>
    [
        .. Directory.GetFiles(Corpus.Folder, "*.docx", SearchOption.AllDirectories).Select(Path.GetFileName).OfType<string>().Order(StringComparer.Ordinal),
    ];

    private static (Document Document, DocumentLayout Layout) Open(string name)
    {
        var document = DocxReader.Read(new MemoryStream(Corpus.Bytes(name)));
        var layout = new DocumentLayout(document, new FixedWidthTextMeasurer());
        layout.Update();
        return (document, layout);
    }

    [Theory]
    [MemberData(nameof(Files))]
    public void Every_file_opens_lays_out_and_survives_a_round_trip(string name)
    {
        var bytes = Corpus.Bytes(name);
        var document = DocxReader.Read(new MemoryStream(bytes));
        using (var layout = new DocumentLayout(document, new FixedWidthTextMeasurer()))
        {
            layout.Update();
            Assert.True(layout.PageCount >= 1);
        }

        var written = DocxTestSupport.Write(document);
        var reread = DocxTestSupport.Read(written);
        DocxTestSupport.AssertSameContent(document, reread);
        Assert.Equal(PartNames(bytes), PartNames(written)); // no part lost (pictures, charts, SmartArt, comments …)
    }

    private static IReadOnlyList<string> PartNames(byte[] bytes)
    {
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(bytes));
        return [.. zip.Entries.Select(entry => entry.FullName).Where(entry => !entry.EndsWith(".rels", StringComparison.Ordinal) && !entry.StartsWith("docProps/", StringComparison.Ordinal)
                                                                              && entry is not "[Content_Types].xml" and not "word/webSettings.xml" and not "word/fontTable.xml").Order(StringComparer.Ordinal)];
    }

    [Fact]
    public void Table_styles_give_the_heading_row_its_fill_and_white_bold_text_and_the_rows_their_bands()
    {
        var (document, layout) = Open("07-demo-tables.docx");
        var cells = layout.Pages[0].Cells;
        var first = cells.Where(cell => cell.TableStart == cells[0].TableStart).ToList();
        Assert.Equal("4472C4", first.Single(cell => cell.Row == 0 && cell.Cell == 1).Fill); // Überschrift: accent 1
        Assert.Equal(WordStyles.Tint("4472C4", 0.2), first.Single(cell => cell.Row == 1 && cell.Cell == 1).Fill); // band 1: the accent at 20 %
        Assert.Null(first.Single(cell => cell.Row == 2 && cell.Cell == 1).Fill); // band 2: none
        Assert.False(first.Single(cell => cell.Row == 0 && cell.Cell == 1).Left?.IsVisible ?? false); // no inside lines in the heading row

        var resolver = new StyleResolver(document);
        var header = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "2024");
        var format = resolver.ResolveCharacter(header, ((TextRun)header.Inlines[0]).Properties);
        Assert.True(format.Bold);
        Assert.Equal("FFFFFF", format.Color);
        var nord = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "Nord");
        Assert.True(resolver.ResolveCharacter(nord, ((TextRun)nord.Inlines[0]).Properties).Bold); // erste Spalte
        var value = document.Blocks.OfType<Paragraph>().First(paragraph => paragraph.Text == "4,3");
        Assert.False(resolver.ResolveCharacter(value, ((TextRun)value.Inlines[0]).Properties).Bold);
    }

    private const double PointsPerCentimetre = 72 / 2.54;

    [Fact]
    public void Floating_pictures_shapes_and_text_boxes_sit_at_their_anchors_and_the_text_flows_around_them()
    {
        var (document, layout) = Open("02-demo-floating.docx");
        Assert.Empty(document.UnsupportedContent);
        var floats = layout.Pages[0].Floats;
        var sidebar = floats.Single(placed => placed.Inline is OpaqueInline { Graphic.Elements: [DrawingShape { Text: not null }] } && placed.Anchor.HorizontalFrame == DrawingFrame.Page);
        Assert.Equal(14.5 * PointsPerCentimetre, sidebar.X, 1);
        Assert.Equal(19 * PointsPerCentimetre, sidebar.Y, 1);
        Assert.Equal(5.5 * PointsPerCentimetre, sidebar.Width, 1);

        // The square-wrapped photo on the right: the first paragraph's lines end before it.
        var photo = floats.First(placed => placed.Inline is ImageInline);
        Assert.Equal(DrawingWrap.Square, photo.Anchor.Wrap);
        var section = document.FinalSection;
        Assert.Equal(section.MarginLeft + section.ContentWidth, photo.X + photo.Width, 1); // aligned right in the column
        var beside = layout.Pages[0].Lines.Where(line => line.Block == 1 && line.Top + 1 < photo.Y + photo.Height).ToList();
        Assert.NotEmpty(beside);
        Assert.All(beside, line => Assert.True(line.Left + line.Line.Items.Max(item => item.X + item.Width) <= photo.X - photo.Anchor.DistanceLeft + 3)); // a trailing space may hang

        // A group keeps its children in their places; the star is a star.
        Assert.Contains(floats, placed => placed.Inline is OpaqueInline { Graphic.Elements.Count: 3 });
        Assert.Contains(floats, placed => placed.Inline is OpaqueInline { Graphic.Elements: [DrawingShape { Preset: "star5" }] });
    }

    [Fact]
    public void A_cover_page_in_a_content_control_opens_as_its_paragraphs_with_its_shapes_on_the_page()
    {
        var (document, layout) = Open("01-demo-cover-toc.docx");
        var first = (Paragraph)document.Blocks[0];
        Assert.IsType<ContentControlMarkInline>(first.Inlines[0]);
        var background = layout.Pages[0].Floats.Single(placed => placed.Anchor.BehindText);
        Assert.Equal((0, 0), (background.X, background.Y));
        Assert.Equal(document.FinalSection.PageWidth, background.Width, 0);
        var title = layout.Pages[0].Floats.Single(placed => !placed.Anchor.BehindText);
        var shape = Assert.IsType<DrawingShape>(Assert.IsType<OpaqueInline>(title.Inline).Graphic!.Elements[0]);
        Assert.Contains("JAHRESBERICHT 2026", ((Paragraph)shape.Text!.Blocks[0]).Text);
        Assert.Equal(DrawingTextAnchor.Bottom, shape.TextAnchor);
    }

    [Fact]
    public void Charts_and_SmartArt_are_drawn_from_the_file()
    {
        var (document, layout) = Open("03-demo-chart-smartart.docx");
        Assert.DoesNotContain(document.UnsupportedContent, note => note.Kind == PreservedKind.Drawing);
        var drawings = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<OpaqueInline>().Where(inline => inline.Graphic is not null).ToList();
        Assert.Contains(drawings, drawing => drawing.Graphic!.Elements is [DrawingChart]);
        var smartArt = drawings.Single(drawing => drawing.Graphic!.Elements.Count == 5);
        Assert.Equal(3, smartArt.Graphic!.Elements.OfType<DrawingShape>().Count(shape => shape.Preset == "roundRect"));
        var list = layout.GetDisplayList(0);
        Assert.Contains(list.Items, item => item is GlyphRunItem { Text: "Q3" }); // the category axis of the chart
    }

    [Fact]
    public void The_letter_has_its_logo_in_the_header_and_the_address_field_where_DIN_5008_puts_it()
    {
        var (_, layout) = Open("09-letter-din5008.docx");
        var page = layout.Pages[0];
        var logo = Assert.Single(page.HeaderFooterFloats);
        Assert.Equal(14 * PointsPerCentimetre, logo.X, 1);
        var address = page.Floats.Single(placed => placed.Inline is OpaqueInline { Graphic.Elements: [DrawingShape { Text: { } text }] } && ((Paragraph)text.Blocks[0]).Text.StartsWith("Nordlicht", StringComparison.Ordinal));
        Assert.Equal((2.5 * PointsPerCentimetre, 4.5 * PointsPerCentimetre), (Math.Round(address.X, 1), Math.Round(address.Y, 1)), new ToleranceComparer(0.2));
    }

    private sealed class ToleranceComparer(double tolerance) : IEqualityComparer<(double, double)>
    {
        public bool Equals((double, double) x, (double, double) y) => Math.Abs(x.Item1 - y.Item1) <= tolerance && Math.Abs(x.Item2 - y.Item2) <= tolerance;

        public int GetHashCode((double, double) obj) => 0;
    }
}
