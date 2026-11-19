using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Layouts;

/// <summary>
/// Pagination with lines of exactly 20 pt and no paragraph spacing: the A4 text area (714.35 pt) takes 35 lines.
/// </summary>
public class PaginationTests
{
    private static readonly ParagraphProperties Exact = new()
    {
        LineSpacing = new LineSpacing(LineSpacingRule.Exact, 20),
        SpacingAfter = 0,
        SpacingBefore = 0,
        WidowControl = true,
    };

    private static List<Block> Fillers(int count) =>
        Enumerable.Range(0, count).Select(index => (Block)new Paragraph($"Zeile {index}", Exact)).ToList();

    /// <summary>A paragraph of exactly <paramref name="lines"/> lines (manual line breaks).</summary>
    private static Paragraph Lines(int lines, ParagraphProperties? properties = null) =>
        new(string.Join("\v", Enumerable.Range(0, lines).Select(index => $"L{index}")), properties ?? Exact);

    private static int PageOfLine(DocumentLayout layout, int block, int line) =>
        layout.Pages.First(page => page.Lines.Any(placed => placed.Block == block && placed.LineIndex == line)).Index;

    [Fact]
    public void Lines_fill_pages_inside_the_margins()
    {
        using var layout = NewLayout(Create(Fillers(100)));

        Assert.Equal(3, layout.PageCount);
        Assert.Equal(35, layout.Pages[0].Lines.Count);
        foreach (var page in layout.Pages)
        {
            foreach (var line in page.Lines)
            {
                Assert.True(line.Top >= 70.85 - 0.01);
                Assert.True(line.Top + line.Line.Height <= 841.9 - 56.7 + 0.01);
                Assert.Equal(70.85, line.Left, 3);
            }
        }
    }

    [Fact]
    public void A_page_break_character_starts_a_new_page()
    {
        using var layout = NewLayout(Create([new Paragraph("vorher\fnachher", Exact), new Paragraph("Ende", Exact)]));

        Assert.Equal(2, layout.PageCount);
        Assert.Equal(0, PageOfLine(layout, 0, 0));
        Assert.Equal(1, PageOfLine(layout, 0, 1));
        Assert.Equal(1, PageOfLine(layout, 1, 0));
    }

    [Fact]
    public void Page_break_before_starts_the_paragraph_on_a_new_page()
    {
        using var layout = NewLayout(Create([new Paragraph("eins", Exact), new Paragraph("zwei", Exact with { PageBreakBefore = true })]));

        Assert.Equal(1, PageOfLine(layout, 1, 0));
    }

    [Fact]
    public void Keep_with_next_moves_a_heading_to_the_page_of_its_paragraph()
    {
        var blocks = Fillers(34);
        blocks.Add(new Paragraph("Überschrift", Exact with { KeepWithNext = true }));
        blocks.Add(new Paragraph("Text", Exact));
        using var keeping = NewLayout(Create(blocks));

        Assert.Equal(1, PageOfLine(keeping, 34, 0));
        Assert.Equal(1, PageOfLine(keeping, 35, 0));

        blocks[34] = new Paragraph("Überschrift", Exact);
        using var notKeeping = NewLayout(Create(blocks));
        Assert.Equal(0, PageOfLine(notKeeping, 34, 0));
    }

    [Fact]
    public void A_keep_with_next_chain_moves_as_a_whole()
    {
        var blocks = Fillers(32);
        blocks.Add(new Paragraph("Kapitel", Exact with { KeepWithNext = true }));
        blocks.Add(new Paragraph("Abschnitt", Exact with { KeepWithNext = true }));
        blocks.Add(new Paragraph("Unterabschnitt", Exact with { KeepWithNext = true }));
        blocks.Add(Lines(3));
        using var layout = NewLayout(Create(blocks));

        Assert.Equal(1, PageOfLine(layout, 32, 0));
        Assert.Equal(1, PageOfLine(layout, 35, 0));
    }

    [Fact]
    public void Widow_control_moves_a_line_or_the_whole_paragraph()
    {
        // 33 fillers leave room for 2 lines.
        var five = Fillers(33);
        five.Add(Lines(5));
        using var fiveLines = NewLayout(Create(five));
        Assert.Equal(0, PageOfLine(fiveLines, 33, 1));
        Assert.Equal(1, PageOfLine(fiveLines, 33, 2));

        // 3 lines: the last would be alone (widow), moving it leaves the first alone (orphan): all go.
        var three = Fillers(33);
        three.Add(Lines(3));
        using var threeLines = NewLayout(Create(three));
        Assert.Equal(1, PageOfLine(threeLines, 33, 0));

        // Without widow control two lines stay.
        three[33] = Lines(3, Exact with { WidowControl = false });
        using var uncontrolled = NewLayout(Create(three));
        Assert.Equal(0, PageOfLine(uncontrolled, 33, 1));
        Assert.Equal(1, PageOfLine(uncontrolled, 33, 2));
    }

    [Fact]
    public void A_single_first_line_is_not_left_alone_at_the_bottom()
    {
        var blocks = Fillers(34);
        blocks.Add(Lines(4));
        using var layout = NewLayout(Create(blocks));

        Assert.Equal(1, PageOfLine(layout, 34, 0));
    }

    [Fact]
    public void Keep_lines_together_moves_the_paragraph()
    {
        var blocks = Fillers(33);
        blocks.Add(Lines(5, Exact with { KeepLinesTogether = true }));
        using var layout = NewLayout(Create(blocks));

        Assert.Equal(1, PageOfLine(layout, 33, 0));
    }

    [Fact]
    public void Spacing_before_is_dropped_at_the_top_of_a_page_after_a_natural_break()
    {
        var blocks = Fillers(35);
        blocks.Add(new Paragraph("oben", Exact with { SpacingBefore = 30 }));
        blocks.Add(new Paragraph("nach Umbruch", Exact with { SpacingBefore = 30, PageBreakBefore = true }));
        using var layout = NewLayout(Create(blocks));

        Assert.Equal(70.85, layout.Pages[1].Lines[0].Top, 3);
        Assert.Equal(70.85 + 30, layout.Pages[2].Lines[0].Top, 3);
    }

    [Fact]
    public void Paragraph_spacing_adds_and_contextual_spacing_removes_it_between_equal_styles()
    {
        var spaced = Exact with { SpacingAfter = 10, SpacingBefore = 5 };
        using var layout = NewLayout(Create([new Paragraph("a", spaced), new Paragraph("b", spaced)]));
        Assert.Equal(70.85 + 5 + 20 + 10 + 5, layout.Pages[0].Lines[1].Top, 3);

        var contextual = spaced with { ContextualSpacing = true };
        using var tight = NewLayout(Create([new Paragraph("a", contextual), new Paragraph("b", contextual)]));
        Assert.Equal(70.85 + 5 + 20, tight.Pages[0].Lines[1].Top, 3);
    }

    [Fact]
    public void Sections_have_their_own_page_setup()
    {
        var landscape = SectionProperties.Default.WithOrientation(PageOrientation.Landscape);
        var document = new Document(
            [
                new Paragraph([new TextRun("hoch", CharacterProperties.Empty)], Exact, null, SectionProperties.Default),
                new Paragraph("quer", Exact),
            ],
            finalSection: landscape);
        using var layout = NewLayout(document);

        Assert.Equal(2, layout.PageCount);
        Assert.True(layout.Pages[0].Height > layout.Pages[0].Width);
        Assert.True(layout.Pages[1].Width > layout.Pages[1].Height);
        Assert.Equal(landscape.ContentWidth, layout.GetBlockLayout(1).Width, 3);
    }

    [Fact]
    public void A_continuous_section_continues_on_the_same_page()
    {
        var continuous = SectionProperties.Default with { Start = SectionStart.Continuous };
        var document = new Document(
            [new Paragraph([new TextRun("eins", CharacterProperties.Empty)], Exact, null, SectionProperties.Default), new Paragraph("zwei", Exact)],
            finalSection: continuous);
        using var layout = NewLayout(document);

        Assert.Equal(1, layout.PageCount);
    }

    [Fact]
    public void An_odd_page_section_inserts_a_blank_page_when_needed()
    {
        var oddPage = SectionProperties.Default with { Start = SectionStart.OddPage };
        var document = new Document(
            [
                new Paragraph("eins", Exact),
                new Paragraph([new TextRun("zwei", CharacterProperties.Empty)], Exact, null, SectionProperties.Default),
                new Paragraph("drei", Exact),
            ],
            finalSection: oddPage);
        using var layout = NewLayout(document);

        // Page 2 would be even: it stays empty and the section starts on page 3.
        Assert.Equal(3, layout.PageCount);
        Assert.Empty(layout.Pages[1].Lines);
        Assert.Equal(2, PageOfLine(layout, 2, 0));
    }

    [Fact]
    public void An_empty_document_has_one_page_with_one_line()
    {
        using var layout = NewLayout(Document.CreateBlank());

        var page = Assert.Single(layout.Pages);
        Assert.Single(page.Lines);
    }
}
