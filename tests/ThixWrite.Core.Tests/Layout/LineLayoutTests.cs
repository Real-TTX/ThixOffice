using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Layouts;

/// <summary>
/// Line breaking and placement with the fixed-width measurer: Calibri 11 → every character 5.5 pt, a space 2.75 pt; a line of
/// the default style is 11 × 259/240 pt high; the A4 text area is 453.6 pt wide.
/// </summary>
public class LineLayoutTests
{
    private const double Width = 595.3 - 70.85 - 70.85;
    private const double Character = 5.5;

    private static ParagraphLayout LayoutOf(params Block[] blocks)
    {
        using var layout = NewLayout(Create(blocks));
        return layout.GetBlockLayout(0);
    }

    [Fact]
    public void A_word_longer_than_the_line_breaks_at_grapheme_boundaries()
    {
        var paragraph = LayoutOf(new Paragraph(new string('x', 200)));

        var perLine = (int)Math.Floor(Width / Character);
        Assert.Equal([0, perLine, 2 * perLine], paragraph.Lines.Select(line => line.Start));
        Assert.Equal(200, paragraph.Lines[^1].End);
        Assert.Equal(LineEnding.Paragraph, paragraph.Lines[^1].Ending);
    }

    [Fact]
    public void Lines_break_after_spaces_cover_the_text_and_fit_the_width()
    {
        var text = Words(300);
        var paragraph = LayoutOf(new Paragraph(text));

        Assert.True(paragraph.Lines.Count > 5);
        Assert.Equal(0, paragraph.Lines[0].Start);
        for (var index = 0; index < paragraph.Lines.Count; index++)
        {
            var line = paragraph.Lines[index];
            if (index > 0)
            {
                Assert.Equal(paragraph.Lines[index - 1].End, line.Start);
            }

            if (line.Ending == LineEnding.Wrap)
            {
                Assert.Equal(' ', text[line.End - 1]);
                Assert.NotEqual(' ', text[line.End]);
            }

            var visibleEnd = line.End;
            while (visibleEnd > line.Start && text[visibleEnd - 1] == ' ')
            {
                visibleEnd--;
            }

            Assert.True(line.XOf(visibleEnd) <= Width + 0.01, $"line {index} is too wide");
        }

        Assert.Equal(text.Length, paragraph.Lines[^1].End);
    }

    [Fact]
    public void Layout_is_deterministic()
    {
        var document = Create(Words(500, seed: 3), Words(80, seed: 4));
        using var first = NewLayout(document);
        using var second = NewLayout(document);

        RandomEditTestsAccess.AssertSameLayout(first, document);
        Assert.Equal(
            first.Pages.SelectMany(page => page.Lines).Select(line => line.Line.StopX.Sum()),
            second.Pages.SelectMany(page => page.Lines).Select(line => line.Line.StopX.Sum()));
    }

    [Theory]
    [InlineData(ParagraphAlignment.Left, 0)]
    [InlineData(ParagraphAlignment.Center, (Width - 22) / 2)]
    [InlineData(ParagraphAlignment.Right, Width - 22)]
    public void Alignment_moves_the_line(ParagraphAlignment alignment, double expectedX)
    {
        var paragraph = LayoutOf(new Paragraph("abcd", new ParagraphProperties { Alignment = alignment }));

        Assert.Equal(expectedX, paragraph.Lines[0].XOf(0), 3);
    }

    [Fact]
    public void Justified_lines_reach_the_right_edge_except_the_last()
    {
        var paragraph = LayoutOf(new Paragraph(Words(60), new ParagraphProperties { Alignment = ParagraphAlignment.Justify }));
        var text = ((Paragraph)paragraph.Block).Text;

        var first = paragraph.Lines[0];
        var visibleEnd = first.End;
        while (text[visibleEnd - 1] == ' ')
        {
            visibleEnd--;
        }

        Assert.Equal(Width, first.XOf(visibleEnd), 3);
        Assert.Equal(0, paragraph.Lines[^1].XOf(paragraph.Lines[^1].Start), 3);
        Assert.True(paragraph.Lines[^1].XOf(text.Length) < Width - 1);
    }

    [Fact]
    public void Tabs_go_to_the_default_stops_and_to_paragraph_stops()
    {
        var defaultStops = LayoutOf(new Paragraph("a\tb\tc"));
        Assert.Equal(35.4, defaultStops.Lines[0].XOf(2), 3);
        Assert.Equal(70.8, defaultStops.Lines[0].XOf(4), 3);

        var tabs = new EquatableArray<TabStop>([new TabStop(200, TabAlignment.Right), new TabStop(300, TabAlignment.Center)]);
        var custom = LayoutOf(new Paragraph("a\tbb\tcccc", new ParagraphProperties { Tabs = tabs }));
        Assert.Equal(200 - 2 * Character, custom.Lines[0].XOf(2), 3);
        Assert.Equal(300 - 2 * Character, custom.Lines[0].XOf(5), 3);
    }

    [Fact]
    public void Indents_place_the_first_and_the_following_lines()
    {
        var firstLine = LayoutOf(new Paragraph(Words(100), new ParagraphProperties { IndentLeft = 20, IndentRight = 30, FirstLineIndent = 36 }));
        Assert.Equal(56, firstLine.Lines[0].XOf(0), 3);
        Assert.Equal(20, firstLine.Lines[1].XOf(firstLine.Lines[1].Start), 3);

        var hanging = LayoutOf(new Paragraph(Words(100), new ParagraphProperties { IndentLeft = 36, FirstLineIndent = -18 }));
        Assert.Equal(18, hanging.Lines[0].XOf(0), 3);
        Assert.Equal(36, hanging.Lines[1].XOf(hanging.Lines[1].Start), 3);
    }

    [Fact]
    public void A_list_item_has_its_label_hanging_and_the_text_at_the_indent()
    {
        var numbering = new NumberingDefinitions([BuiltInLists.Create(ListKind.Numbered, 0)], [new NumberingInstance { Id = 1, AbstractId = 0 }]);
        var document = new Document([new Paragraph("Punkt", new ParagraphProperties { Numbering = new NumberingReference(1, 0) })], numbering: numbering);
        using var layout = NewLayout(document);

        var line = layout.GetBlockLayout(0).Lines[0];
        var label = Assert.IsType<LabelItem>(line.Items[0]);
        Assert.Equal("1.", label.Shaped.Text);
        Assert.Equal(18, label.X, 3);
        Assert.Equal(36, line.XOf(0), 3);
    }

    [Fact]
    public void Bullets_of_symbol_fonts_are_shown_as_unicode_bullets()
    {
        var numbering = new NumberingDefinitions([BuiltInLists.Create(ListKind.Bullet, 0)], [new NumberingInstance { Id = 1, AbstractId = 0 }]);
        var document = new Document([new Paragraph("Punkt", new ParagraphProperties { Numbering = new NumberingReference(1, 0) })], numbering: numbering);
        using var layout = NewLayout(document);

        var label = Assert.IsType<LabelItem>(layout.GetBlockLayout(0).Lines[0].Items[0]);
        Assert.Equal("•", label.Shaped.Text);
    }

    [Fact]
    public void Line_spacing_rules_set_the_line_height()
    {
        static double HeightOf(LineSpacing spacing) =>
            LayoutOf(new Paragraph("Text", new ParagraphProperties { LineSpacing = spacing })).Lines[0].Height;

        Assert.Equal(11, HeightOf(LineSpacing.Single), 3);
        Assert.Equal(22, HeightOf(new LineSpacing(LineSpacingRule.Multiple, 2)), 3);
        Assert.Equal(30, HeightOf(new LineSpacing(LineSpacingRule.AtLeast, 30)), 3);
        Assert.Equal(11, HeightOf(new LineSpacing(LineSpacingRule.AtLeast, 5)), 3);
        Assert.Equal(7, HeightOf(new LineSpacing(LineSpacingRule.Exact, 7)), 3);
    }

    [Fact]
    public void Line_breaks_end_lines_and_an_empty_paragraph_has_one_line()
    {
        var paragraph = LayoutOf(new Paragraph("eins\vzwei\v"));

        Assert.Equal(3, paragraph.Lines.Count);
        Assert.Equal(LineEnding.LineBreak, paragraph.Lines[0].Ending);
        Assert.Equal([0, 1, 2, 3, 4], paragraph.Lines[0].Stops);
        Assert.Equal(10, paragraph.Lines[2].Start);

        var empty = LayoutOf(Paragraph.Empty());
        Assert.Single(empty.Lines);
        Assert.Equal([0], empty.Lines[0].Stops);
    }

    [Fact]
    public void Caret_stops_skip_the_inside_of_grapheme_clusters_and_markers()
    {
        var marker = new OpaqueInline("<w:bookmarkStart />", PreservedKind.Bookmark, false, CharacterProperties.Empty);
        var paragraph = LayoutOf(new Paragraph([new TextRun("é", CharacterProperties.Empty), marker, new TextRun("b\U0001F600", CharacterProperties.Empty)]));

        Assert.Equal([0, 2, 4, 6], paragraph.Lines[0].Stops);
    }

    [Fact]
    public void Superscript_is_smaller_and_raised()
    {
        var paragraph = LayoutOf(new Paragraph([
            new TextRun("x", CharacterProperties.Empty),
            new TextRun("2", new CharacterProperties { VerticalPosition = VerticalPosition.Superscript }),
        ]));

        var items = paragraph.Lines[0].Items.OfType<TextItem>().ToList();
        Assert.True(items[1].Font.Size < items[0].Font.Size);
        Assert.True(items[1].BaselineShift < 0);
    }

    [Fact]
    public void A_preserved_block_is_shown_as_its_lines_with_a_caption()
    {
        var table = new OpaqueBlock("<w:tbl />", PreservedKind.Table, ["A\tB", "C\tD"]);
        using var layout = NewLayout(Create([table, new Paragraph("Text")]));

        var block = layout.GetBlockLayout(0);
        Assert.True(block.IsPreserved);
        Assert.Equal(3, block.Lines.Count);

        var hit = layout.HitTest(0, 100, layout.Pages[0].Lines[0].Top + 2);
        Assert.Equal(0, hit.PreservedBlock);
        Assert.Equal(At(1, 0), hit.Position);
    }
}

/// <summary>Gives the layout tests the comparison of the property tests.</summary>
internal static class RandomEditTestsAccess
{
    public static void AssertSameLayout(DocumentLayout layout, Document document) =>
        Editing.RandomEditTests.AssertSameLayout(layout, document);
}
