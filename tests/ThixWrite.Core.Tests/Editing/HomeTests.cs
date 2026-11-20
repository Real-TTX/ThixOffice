using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

/// <summary>Start: Sortieren, Rahmen und Schattierung, Seitenränder, Mehrstufige Liste, Word's keys.</summary>
public sealed class HomeTests
{
    private static EditingSession NewSession(params string[] paragraphs) => new(new Document(paragraphs.Select(text => new Paragraph(text)), BuiltInStyles.CreateDefault()));

    [Fact]
    public void Paragraphs_are_sorted_by_text_number_and_backwards()
    {
        var session = NewSession("Zebra", "Äpfel", "apfel", "Birne", "");
        session.Select(At(0, 0), At(3, 5));
        Assert.True(session.Sort(SortKind.Text, descending: false));
        Assert.Equal(["apfel", "Äpfel", "Birne", "Zebra"], session.Document.Blocks.Take(4).Cast<Paragraph>().Select(paragraph => paragraph.Text));

        var numbers = NewSession("10 Stück", "2 Stück", "33 Stück", "");
        numbers.Select(At(0, 0), At(2, 3));
        numbers.Sort(SortKind.Number, descending: true);
        Assert.Equal(["33 Stück", "10 Stück", "2 Stück"], numbers.Document.Blocks.Take(3).Cast<Paragraph>().Select(paragraph => paragraph.Text));
        numbers.Undo();
        Assert.Equal("10 Stück", numbers.Document.ParagraphAt(0)!.Text);
    }

    [Fact]
    public void Table_rows_are_sorted_by_a_column_below_the_heading_row()
    {
        var (session, _) = Editor(new Document([new Paragraph("vor"), new Paragraph("")], BuiltInStyles.CreateDefault()));
        session.SetCaret(At(1, 0));
        session.InsertTable(3, 2);
        string[] values = ["Name", "Wert", "Zug", "3", "Auto", "1"];
        for (var index = 0; index < values.Length; index++)
        {
            session.SetCaret(new DocumentPosition(1 + index, 0));
            session.InsertText(values[index]);
        }

        var span = TableSpan.At(session.Document, 1)!;
        session.SetCaret(At(1, 0));
        session.SetRepeatHeaderRows(true);
        session.SetCaret(At(1, 0));
        Assert.True(session.Sort(SortKind.Text, descending: false, column: 0));
        Assert.Equal(["Name", "Wert", "Auto", "1", "Zug", "3"], Enumerable.Range(1, 6).Select(block => session.Document.ParagraphAt(block)!.Text));
        Assert.True(session.Sort(SortKind.Number, descending: true, column: 1));
        Assert.Equal("Zug", session.Document.ParagraphAt(3)!.Text);
        _ = span;
    }

    [Fact]
    public void Paragraph_borders_shading_and_page_borders_are_drawn_and_written()
    {
        var session = NewSession("Hinweis");
        session.SetParagraphBorders(BorderSet.All(new BorderLine("single", 1, "C00000", 2)) with { InsideHorizontal = null, InsideVertical = null });
        session.SetParagraphShading("#FFF2CC");
        session.SetPageBorders(new BorderSet(new BorderLine("double", 1.5, "4472C4", 24), new BorderLine("double", 1.5, "4472C4", 24), new BorderLine("double", 1.5, "4472C4", 24), new BorderLine("double", 1.5, "4472C4", 24)));
        using (var layout = NewLayout(session.Document))
        {
            var items = layout.GetDisplayList(0).Items;
            Assert.Contains(items, item => item is RectangleItem { Fill: { } fill } && fill == RgbColor.FromHex("FFF2CC"));
            Assert.Contains(items, item => item is LineItem { Color: var color } && color == RgbColor.FromHex("4472C4")); // the page border
            Assert.Contains(items, item => item is LineItem { Color: var color } && color == RgbColor.FromHex("C00000"));
        }

        var reread = DocxTestSupport.RoundTrip(session.Document);
        Assert.Equal("4472C4", reread.FinalSection.PageBorders!.Top!.Color);
        Assert.Equal("FFF2CC", reread.ParagraphAt(0)!.Properties.Shading!.Fill);
        session.SetPageBorders(null);
        Assert.Null(session.Document.FinalSection.PageBorders);
        Assert.DoesNotContain("pgBorders", session.Document.FinalSection.UnknownXml ?? "", StringComparison.Ordinal);
    }

    [Fact]
    public void A_multilevel_list_numbers_each_level_with_the_numbers_above_it()
    {
        var session = NewSession("Eins", "Eins Punkt eins", "Zwei");
        session.SelectAll();
        session.ApplyMultilevelList("outline");
        session.SetCaret(At(1, 0));
        session.SetListLevel(1);
        Assert.Equal(1, session.CurrentListLevel);
        using var layout = NewLayout(session.Document);
        var labels = Enumerable.Range(0, 3).Select(block => layout.GetBlockLayout(block).Label).ToList();
        Assert.Equal(["1.", "1.1.", "2."], labels);
    }

    [Fact]
    public void Words_keys_small_caps_double_underline_space_before_hanging_indent_and_reset()
    {
        var session = NewSession("Text");
        session.SelectAll();
        session.ToggleSmallCaps();
        session.ToggleDoubleUnderline();
        var run = (TextRun)session.Document.ParagraphAt(0)!.Inlines[0];
        Assert.True(run.Properties.SmallCaps);
        Assert.Equal(UnderlineStyle.Double, run.Properties.Underline);

        session.ToggleSpaceBefore();
        Assert.Equal(12, session.Document.ParagraphAt(0)!.Properties.SpacingBefore);
        session.ChangeHangingIndent(1);
        var properties = session.Document.ParagraphAt(0)!.Properties;
        Assert.True(properties.IndentLeft > 0);
        Assert.Equal(-properties.IndentLeft, properties.FirstLineIndent);
        session.ResetParagraphFormatting();
        Assert.Null(session.Document.ParagraphAt(0)!.Properties.IndentLeft);
        Assert.Null(session.Document.ParagraphAt(0)!.Properties.SpacingBefore);
    }
}
