using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Layouts;

public class GeometryTests
{
    private static Document Sample() => Create([
        new Paragraph(Words(120, seed: 7)),
        new Paragraph("Zentriert\tmit Tab", new ParagraphProperties { Alignment = ParagraphAlignment.Center }),
        new Paragraph("é\U0001F600 Grapheme"),
        Paragraph.Empty(),
        new Paragraph(Words(60, seed: 8), new ParagraphProperties { Alignment = ParagraphAlignment.Justify, IndentLeft = 20, FirstLineIndent = -10 }),
    ]);

    [Fact]
    public void Every_caret_stop_survives_caret_geometry_and_hit_testing()
    {
        var document = Sample();
        using var layout = NewLayout(document);

        foreach (var page in layout.Pages)
        {
            foreach (var placed in page.Lines)
            {
                var line = placed.Line;
                foreach (var stop in line.Stops)
                {
                    var affinity = stop == line.End && line.Ending == LineEnding.Wrap ? CaretAffinity.Upstream : CaretAffinity.Downstream;
                    var place = new CaretPlace(new DocumentPosition(placed.Block, stop), affinity);

                    var caret = layout.GetCaret(place);
                    var hit = layout.HitTest(caret.Page, caret.X, caret.Top + caret.Height / 2);

                    Assert.Equal(page.Index, caret.Page);
                    Assert.Equal(place, hit.Place);
                }
            }
        }
    }

    [Fact]
    public void Hits_outside_the_text_go_to_the_nearest_line_and_stop()
    {
        using var layout = NewLayout(Create("kurz", "zweiter"));

        Assert.Equal(At(0, 4), layout.HitTest(0, 400, layout.Pages[0].Lines[0].Top + 1).Position);
        Assert.Equal(At(0, 0), layout.HitTest(0, -50, 0).Position);
        Assert.Equal(At(1, 7), layout.HitTest(0, 500, 800).Position);
    }

    [Fact]
    public void Stacked_coordinates_put_pages_under_each_other()
    {
        using var layout = NewLayout(Create(Enumerable.Range(0, 120).Select(index => $"Absatz {index}").ToArray()));

        Assert.True(layout.PageCount >= 2);
        Assert.Equal(0, layout.PageTop(0));
        Assert.Equal(841.9 + 12, layout.PageTop(1), 3);
        var hit = layout.HitTestStacked(100, layout.PageTop(1) + layout.Pages[1].Lines[0].Top + 2);
        Assert.Equal(1, hit.Page);
        Assert.Equal(layout.Pages[1].Lines[0].Block, hit.Position.Block);
    }

    [Fact]
    public void Selection_rects_cover_each_line_and_reach_past_paragraph_ends()
    {
        var document = Sample();
        using var layout = NewLayout(document);
        var paragraph = layout.GetBlockLayout(0);

        var rects = layout.GetSelectionRects(new DocumentRange(At(0, 3), At(1, 4)));

        Assert.Equal(paragraph.Lines.Count + 1, rects.Count);
        var lastOfFirst = rects[paragraph.Lines.Count - 1];
        Assert.True(lastOfFirst.Width > 0);
        Assert.Empty(layout.GetSelectionRects(new DocumentRange(At(0, 3), At(0, 3))));
    }

    [Fact]
    public void Vertical_moves_keep_the_column_and_home_end_follow_the_line()
    {
        var document = Create(new string('x', 200));
        var (session, layout) = Editor(document);
        using var _ = layout;
        var perLine = layout.GetBlockLayout(0).Lines[1].Start;
        session.SetCaret(At(0, 5));

        session.Move(CaretMovement.LineDown);
        Assert.Equal(At(0, perLine + 5), session.Selection.Active);

        session.Move(CaretMovement.LineDown);
        session.Move(CaretMovement.LineUp);
        Assert.Equal(At(0, perLine + 5), session.Selection.Active);

        session.Move(CaretMovement.LineEnd);
        Assert.Equal(At(0, 2 * perLine), session.Selection.Active);
        Assert.Equal(CaretAffinity.Upstream, session.Selection.Affinity);
        Assert.Equal(layout.GetCaret(new CaretPlace(At(0, perLine + 5))).Top, layout.GetCaret(new CaretPlace(session.Selection.Active, session.Selection.Affinity)).Top);

        session.Move(CaretMovement.LineStart);
        Assert.Equal(At(0, perLine), session.Selection.Active);

        session.Move(CaretMovement.LineUp);
        session.Move(CaretMovement.LineUp);
        Assert.Equal(At(0, 0), session.Selection.Active);
    }

    [Fact]
    public void Page_down_moves_about_a_page()
    {
        var document = Create(Enumerable.Range(0, 200).Select(index => $"Absatz {index}").ToArray());
        var (session, layout) = Editor(document);
        using var _ = layout;

        session.Move(CaretMovement.PageDown);

        Assert.Equal(1, layout.PageOf(session.Selection.Active));
        session.Move(CaretMovement.PageUp);
        Assert.Equal(0, layout.PageOf(session.Selection.Active));
        Assert.True(session.Selection.Active.Block <= 1);
    }

    [Fact]
    public void Character_and_word_moves_with_shift_extend_the_selection()
    {
        var (session, layout) = Editor(Create("Eins zwei", "drei"));
        using var _ = layout;

        session.Move(CaretMovement.WordRight, extend: true);
        Assert.Equal(new Selection(At(0, 0), At(0, 5)), session.Selection);

        session.Move(CaretMovement.CharacterRight);
        Assert.Equal(Selection.Caret(At(0, 5)), session.Selection);

        session.Move(CaretMovement.DocumentEnd);
        Assert.Equal(At(1, 4), session.Selection.Active);
        session.Move(CaretMovement.ParagraphUp);
        Assert.Equal(At(1, 0), session.Selection.Active);
        session.Move(CaretMovement.CharacterLeft);
        Assert.Equal(At(0, 9), session.Selection.Active);
    }

    [Fact]
    public void The_display_list_draws_the_text_highlights_and_underlines()
    {
        var document = Create([
            new Paragraph([
                new TextRun("Hallo ", new CharacterProperties { Highlight = HighlightColor.Yellow }),
                new TextRun("Welt", new CharacterProperties { Underline = UnderlineStyle.Double, Color = TextColor.FromHex("FF0000") }),
            ]),
            new Paragraph("Zweite Zeile"),
        ]);
        using var layout = NewLayout(document);

        var list = layout.GetDisplayList(0);

        Assert.Equal(595.3, list.Width, 3);
        var texts = list.Items.OfType<GlyphRunItem>().Select(item => item.Text).ToList();
        Assert.Equal(["Hallo ", "Welt", "Zweite Zeile"], texts);
        var highlight = Assert.Single(list.Items.OfType<RectangleItem>());
        Assert.Equal(new RgbColor(255, 255, 0), highlight.Fill);
        var underline = Assert.Single(list.Items.OfType<LineItem>());
        Assert.Equal(LineStyle.Double, underline.Style);
        Assert.Equal(new RgbColor(255, 0, 0), underline.Color);
        Assert.Same(list, layout.GetDisplayList(0));
    }

    [Fact]
    public void Display_lists_are_rebuilt_only_for_changed_pages()
    {
        var document = Create(Enumerable.Range(0, 150).Select(index => $"Absatz {index}").ToArray());
        var (session, layout) = Editor(document);
        using var _ = layout;
        var first = layout.GetDisplayList(0);
        var last = layout.GetDisplayList(layout.PageCount - 1);

        session.SetCaret(new DocumentPosition(document.Blocks.Count - 1, 0));
        session.InsertText("x");

        Assert.Same(first, layout.GetDisplayList(0));
        Assert.NotSame(last, layout.GetDisplayList(layout.PageCount - 1));
    }
}
