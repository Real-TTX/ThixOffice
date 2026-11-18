using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

public class ListTests
{
    private static string Labels(EditingSession session) =>
        string.Join(",", ListNumbering.Compute(session.Document, session.Resolver).Select(label => label?.Text ?? "-"));

    [Fact]
    public void Numbering_on_three_paragraphs_counts_them_and_indent_starts_a_sub_level()
    {
        var session = Session("Eins", "Zwei", "Drei");
        session.SelectAll();

        session.ToggleList(ListKind.Numbered);

        Assert.Equal("1.,2.,3.", Labels(session));
        Assert.True(session.GetFormatting().List.Is(ListKind.Numbered));
        Assert.Equal(BuiltInStyles.ListParagraph, session.Document.ParagraphAt(0)!.Properties.StyleId);

        session.SetCaret(At(1, 0));
        session.HandleTab(shift: false);
        Assert.Equal("1.,a.,2.", Labels(session));

        session.HandleTab(shift: true);
        Assert.Equal("1.,2.,3.", Labels(session));
    }

    [Fact]
    public void Bullets_toggle_off_again()
    {
        var session = Session("Eins", "Zwei");
        session.SelectAll();

        session.ToggleList(ListKind.Bullet);
        Assert.Equal("\uF0B7,\uF0B7", Labels(session));

        session.ToggleList(ListKind.Bullet);
        Assert.Equal("-,-", Labels(session));
        Assert.Null(session.Document.ParagraphAt(0)!.Properties.StyleId);
        Assert.True(session.GetFormatting().List.Is(null));
    }

    [Fact]
    public void A_new_list_item_next_to_a_list_continues_it()
    {
        var session = Session("Eins", "Zwei", "Drei");
        session.SetCaret(At(0, 0));
        session.ToggleList(ListKind.Numbered);
        session.SetCaret(At(1, 0));

        session.ToggleList(ListKind.Numbered);

        Assert.Equal("1.,2.,-", Labels(session));
        Assert.Single(session.Document.Numbering.Instances);
    }

    [Fact]
    public void Enter_continues_the_list_and_enter_in_an_empty_item_ends_it()
    {
        var session = Session("Eins");
        session.ToggleList(ListKind.Numbered);
        session.SetCaret(At(0, 4));

        session.InsertParagraphBreak();
        session.InsertText("Zwei");
        session.InsertParagraphBreak();
        Assert.Equal("1.,2.,3.", Labels(session));

        session.InsertParagraphBreak();
        Assert.Equal("1.,2.,-", Labels(session));
    }

    [Fact]
    public void Backspace_at_the_start_of_a_list_item_removes_its_number_first()
    {
        var session = Session("Eins", "Zwei");
        session.SelectAll();
        session.ToggleList(ListKind.Bullet);
        session.SetCaret(At(1, 0));

        session.DeleteBackward();

        Assert.Equal("\uF0B7,-", Labels(session));
        Assert.Equal("Eins|Zwei", Texts(session.Document));
    }

    [Fact]
    public void Restart_numbering_starts_again_at_one()
    {
        var session = Session("A", "B", "C", "D");
        session.SelectAll();
        session.ToggleList(ListKind.Numbered);
        session.SetCaret(At(2, 0));

        session.RestartNumbering();

        Assert.Equal("1.,2.,1.,2.", Labels(session));
        session.Undo();
        Assert.Equal("1.,2.,3.,4.", Labels(session));
    }
}

public class ClipboardTests
{
    [Fact]
    public void Copy_and_paste_within_a_document_keeps_formatting_and_paragraphs()
    {
        var document = Create([
            new Paragraph([new TextRun("fett", new CharacterProperties { Bold = true }), new TextRun(" normal", CharacterProperties.Empty)]),
            new Paragraph("Zweiter", new ParagraphProperties { StyleId = BuiltInStyles.Heading2 }),
        ]);
        var session = Session(document);
        session.Select(At(0, 2), At(1, 4));

        var fragment = session.Copy()!;
        Assert.Equal("tt normal\r\nZwei", fragment.PlainText);

        session.SetCaret(session.Document.End);
        session.Paste(fragment);

        Assert.Equal("fett normal|Zweitertt normal|Zwei", Texts(session.Document));
        var pasted = session.Document.ParagraphAt(1)!;
        Assert.True(pasted.Inlines.First(inline => ((TextRun)inline).Text == "tt").Properties.Bold);
        Assert.Equal(At(2, 4), session.Selection.Active);
    }

    [Fact]
    public void Cut_removes_and_returns_the_selection()
    {
        var session = Session("Hallo Welt");
        session.Select(At(0, 5), At(0, 10));

        var fragment = session.Cut();

        Assert.Equal(" Welt", fragment!.PlainText);
        Assert.Equal("Hallo", Texts(session.Document));
        Assert.Equal("Ausschneiden", session.UndoLabel);
    }

    [Fact]
    public void Pasting_into_another_document_brings_styles_and_new_list_instances()
    {
        var source = Session("Eins", "Zwei", "Drei");
        source.SelectAll();
        source.ToggleList(ListKind.Numbered);
        source.ApplyStyle(BuiltInStyles.Heading3);
        source.SelectAll();
        var fragment = source.Copy()!;

        var target = Session(new Document([new Paragraph("Ziel")], new StyleSheet([BuiltInStyles.Definition(BuiltInStyles.Normal)!])));
        target.SetCaret(At(0, 4));
        target.Paste(fragment);

        Assert.NotNull(target.Document.Styles.Find(BuiltInStyles.Heading3));
        Assert.Single(target.Document.Numbering.Instances);
        var numbering = target.Document.ParagraphAt(1)!.Properties.Numbering!;
        Assert.Equal(target.Document.Numbering.Instances[0].Id, numbering.NumberingId);
    }

    [Fact]
    public void Pasting_into_another_document_leaves_out_content_that_needs_the_other_package()
    {
        var image = new OpaqueInline("<w:drawing xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" />",
            PreservedKind.Drawing, true, CharacterProperties.Empty, Width: 20, Height: 20);
        var source = Session(Create([new Paragraph([new TextRun("Bild ", CharacterProperties.Empty), image])]));
        source.SelectAll();
        var fragment = source.Copy()!;

        var target = Session("");
        target.Paste(fragment);

        Assert.Equal("Bild ", Texts(target.Document));
    }

    [Fact]
    public void Plain_text_takes_the_formatting_of_the_target()
    {
        var session = Session(Create([new Paragraph([new TextRun("fett", new CharacterProperties { Bold = true })])]));
        session.SetCaret(At(0, 4));

        session.PasteText("eins\r\nzwei");

        Assert.Equal("fetteins|zwei", Texts(session.Document));
        Assert.All(session.Document.Blocks.Cast<Paragraph>(), paragraph => Assert.True(paragraph.Inlines[0].Properties.Bold));
    }
}

public class FindReplaceTests
{
    [Fact]
    public void Find_next_selects_matches_and_wraps_around()
    {
        var session = Session("Das Haus", "das Dach", "Haus");
        var options = new FindOptions("haus");

        Assert.True(session.FindNext(options));
        Assert.Equal(new Selection(At(0, 4), At(0, 8)), session.Selection);
        Assert.True(session.FindNext(options));
        Assert.Equal(At(2, 0), session.Selection.Start);
        Assert.True(session.FindNext(options));
        Assert.Equal(At(0, 4), session.Selection.Start);

        Assert.True(session.FindNext(options, forward: false));
        Assert.Equal(At(2, 0), session.Selection.Start);
    }

    [Fact]
    public void Match_case_and_whole_word()
    {
        var document = Create("Das Haus, das Haustier, das HAUS");

        Assert.Equal(3, TextSearch.FindAll(document, new FindOptions("haus")).Count);
        Assert.Single(TextSearch.FindAll(document, new FindOptions("Haus", MatchCase: true, WholeWord: true)));
        Assert.Equal(2, TextSearch.FindAll(document, new FindOptions("haus", WholeWord: true)).Count);
        Assert.Empty(TextSearch.FindAll(document, new FindOptions("")));
    }

    [Fact]
    public void Replace_all_is_one_undo_step_and_keeps_formatting()
    {
        var session = Session(Create([
            new Paragraph([new TextRun("alt", new CharacterProperties { Italic = true }), new TextRun(" und alt", CharacterProperties.Empty)]),
            new Paragraph("nicht alt"),
        ]));

        var count = session.ReplaceAll(new FindOptions("alt", WholeWord: true), "neu");

        Assert.Equal(3, count);
        Assert.Equal("neu und neu|nicht neu", Texts(session.Document));
        Assert.True(session.Document.ParagraphAt(0)!.Inlines[0].Properties.Italic);
        Assert.Equal("Alle ersetzen", session.UndoLabel);
        session.Undo();
        Assert.Equal("alt und alt|nicht alt", Texts(session.Document));
        Assert.False(session.CanUndo);
    }

    [Fact]
    public void Replace_replaces_the_current_match_and_finds_the_next()
    {
        var session = Session("a b a b");
        var options = new FindOptions("a");
        session.FindNext(options);

        Assert.True(session.Replace(options, "x"));
        Assert.Equal("x b a b", Texts(session.Document));
        Assert.Equal(new Selection(At(0, 4), At(0, 5)), session.Selection);
    }
}
