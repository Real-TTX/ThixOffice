using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

public class TextEditingTests
{
    [Fact]
    public void Typing_inserts_at_the_caret_and_moves_it()
    {
        var session = Session("Hallo");
        session.SetCaret(At(0, 5));

        session.InsertText(" Welt");

        Assert.Equal("Hallo Welt", Texts(session.Document));
        Assert.Equal(Selection.Caret(At(0, 10)), session.Selection);
    }

    [Fact]
    public void Typing_replaces_the_selection()
    {
        var session = Session("Hallo schöne Welt");
        session.Select(At(0, 6), At(0, 12));

        session.InsertText("neue");

        Assert.Equal("Hallo neue Welt", Texts(session.Document));
        Assert.Equal(At(0, 10), session.Selection.Active);
    }

    [Fact]
    public void Continuous_typing_is_one_undo_step_and_a_pause_starts_a_new_one()
    {
        var now = new DateTime(2026, 11, 18, 12, 0, 0);
        var session = Session(Create(""), () => now);
        foreach (var character in "Hallo")
        {
            session.InsertText(character.ToString());
            now = now.AddMilliseconds(200);
        }

        now = now.AddSeconds(5);
        session.InsertText("!");

        Assert.Equal("Hallo!", Texts(session.Document));
        Assert.Equal("Eingabe", session.UndoLabel);
        session.Undo();
        Assert.Equal("Hallo", Texts(session.Document));
        session.Undo();
        Assert.Equal("", Texts(session.Document));
        Assert.False(session.CanUndo);
        session.Redo();
        session.Redo();
        Assert.Equal("Hallo!", Texts(session.Document));
    }

    [Fact]
    public void Moving_the_caret_ends_the_typing_group()
    {
        var session = Session("ab");
        session.SetCaret(At(0, 2));
        session.InsertText("c");
        session.SetCaret(At(0, 0));
        session.InsertText("x");

        session.Undo();

        Assert.Equal("abc", Texts(session.Document));
    }

    [Fact]
    public void Backspace_and_delete_remove_whole_grapheme_clusters()
    {
        var family = "\U0001F468\u200D\U0001F469\u200D\U0001F467";
        var session = Session($"a{family}e\u0301b");
        session.SetCaret(At(0, 1 + family.Length));

        session.DeleteBackward();
        Assert.Equal("ae\u0301b", Texts(session.Document));

        session.DeleteForward();
        Assert.Equal("ab", Texts(session.Document));
    }

    [Fact]
    public void Word_wise_deletion_removes_to_the_word_boundary()
    {
        var session = Session("Eins zwei drei");
        session.SetCaret(At(0, 14));

        session.DeleteBackward(DeleteUnit.Word);
        Assert.Equal("Eins zwei ", Texts(session.Document));

        session.SetCaret(At(0, 0));
        session.DeleteForward(DeleteUnit.Word);
        Assert.Equal("zwei ", Texts(session.Document));
    }

    [Fact]
    public void Enter_splits_and_backspace_merges_again()
    {
        var session = Session("HalloWelt");
        session.SetCaret(At(0, 5));

        session.InsertParagraphBreak();
        Assert.Equal("Hallo|Welt", Texts(session.Document));
        Assert.Equal(At(1, 0), session.Selection.Active);

        session.DeleteBackward();
        Assert.Equal("HalloWelt", Texts(session.Document));
        Assert.Equal(At(0, 5), session.Selection.Active);
    }

    [Fact]
    public void Enter_at_the_end_of_a_heading_continues_with_its_next_style()
    {
        var session = Session("Titel");
        session.ApplyStyle(BuiltInStyles.Heading1);
        session.SetCaret(At(0, 5));

        session.InsertParagraphBreak();

        Assert.Equal(BuiltInStyles.Heading1, session.Document.ParagraphAt(0)!.Properties.StyleId);
        Assert.Null(session.Document.ParagraphAt(1)!.Properties.StyleId);
        Assert.Equal("Standard", session.GetFormatting().ParagraphStyleName);
    }

    [Fact]
    public void Merging_paragraphs_keeps_the_first_paragraphs_formatting()
    {
        var document = Create([
            new Paragraph("Überschrift", new ParagraphProperties { StyleId = BuiltInStyles.Heading1 }),
            new Paragraph("Text"),
        ]);
        var session = Session(document);
        session.SetCaret(At(1, 0));

        session.DeleteBackward();

        Assert.Equal(BuiltInStyles.Heading1, session.Document.ParagraphAt(0)!.Properties.StyleId);
        Assert.Equal("ÜberschriftText", Texts(session.Document));
    }

    [Fact]
    public void Deleting_a_whole_first_paragraph_keeps_the_formatting_of_the_one_after()
    {
        var document = Create([
            new Paragraph("Weg damit", new ParagraphProperties { StyleId = BuiltInStyles.Heading1 }),
            new Paragraph("Bleibt", new ParagraphProperties { Alignment = ParagraphAlignment.Center }),
        ]);
        var session = Session(document);
        session.Select(At(0, 0), At(1, 0));

        session.DeleteSelection();

        var paragraph = Assert.Single(session.Document.Blocks.Cast<Paragraph>());
        Assert.Equal("Bleibt", paragraph.Text);
        Assert.Equal(ParagraphAlignment.Center, paragraph.Properties.Alignment);
    }

    [Fact]
    public void Deleting_across_paragraphs_is_one_step_and_undo_restores_the_instances()
    {
        var session = Session("Eins", "Zwei", "Drei");
        var before = session.Document.Blocks.ToList();
        session.Select(At(0, 2), At(2, 2));

        session.DeleteSelection();
        Assert.Equal("Eiei", Texts(session.Document));

        session.Undo();
        Assert.Equal(before, session.Document.Blocks);
        Assert.Equal(new Selection(At(0, 2), At(2, 2)), session.Selection);
    }

    [Fact]
    public void Line_tab_and_page_breaks_are_characters_of_the_paragraph()
    {
        var session = Session("ab");
        session.SetCaret(At(0, 1));

        session.InsertLineBreak();
        session.InsertTab();

        Assert.Equal("a\v\tb", Texts(session.Document));

        session.InsertPageBreak();
        Assert.Equal("a\v\t\f|b", Texts(session.Document));
        Assert.Equal(At(1, 0), session.Selection.Active);
    }

    [Fact]
    public void Typing_continues_the_formatting_of_the_character_before()
    {
        var bold = new CharacterProperties { Bold = true };
        var session = Session(Create([new Paragraph([new TextRun("fett", bold), new TextRun(" normal", CharacterProperties.Empty)])]));
        session.SetCaret(At(0, 4));

        session.InsertText("er");

        var paragraph = session.Document.ParagraphAt(0)!;
        Assert.Equal("fetter", ((TextRun)paragraph.Inlines[0]).Text);
        Assert.Equal(bold, paragraph.Inlines[0].Properties);
    }

    [Fact]
    public void Typing_inside_a_link_continues_it_but_not_at_its_end()
    {
        var link = new Hyperlink("https://example.org");
        var session = Session(Create([new Paragraph([new TextRun("Link", CharacterProperties.Empty, link), new TextRun(" x", CharacterProperties.Empty)])]));
        session.SetCaret(At(0, 2));
        session.InsertText("n");
        session.SetCaret(At(0, 5));
        session.InsertText("!");

        var paragraph = session.Document.ParagraphAt(0)!;
        Assert.Equal("Linnk", ((TextRun)paragraph.Inlines[0]).Text);
        Assert.Null(paragraph.Inlines[1].Link);
    }

    [Fact]
    public void Text_with_line_breaks_is_pasted_as_paragraphs()
    {
        var session = Session("AZ");
        session.SetCaret(At(0, 1));

        session.InsertText("b\r\nc\nd");

        Assert.Equal("Ab|c|dZ", Texts(session.Document));
        Assert.Equal(At(2, 1), session.Selection.Active);
    }

    [Fact]
    public void A_tracked_position_follows_the_edits_before_it()
    {
        var session = Session("Eins", "Zwei");
        var tracked = session.Track(At(1, 2));
        session.SetCaret(At(0, 0));

        session.InsertText("Null ");
        Assert.Equal(At(1, 2), tracked.Position);

        session.SetCaret(At(0, 9));
        session.InsertParagraphBreak();
        Assert.Equal(At(2, 2), tracked.Position);

        session.SetCaret(At(2, 0));
        session.InsertText("xx");
        Assert.Equal(At(2, 4), tracked.Position);
    }

    [Fact]
    public void A_preserved_block_is_selected_first_and_deleted_with_the_next_key()
    {
        var table = new OpaqueBlock("<w:tbl xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" />", PreservedKind.Table, ["A\tB"]);
        var session = Session(Create([new Paragraph("Vorher"), table, new Paragraph("Nachher")]));
        session.SetCaret(At(0, 6));

        session.DeleteForward();
        Assert.Equal(new Selection(At(1, 0), At(2, 0)), session.Selection);
        Assert.Equal(3, session.Document.Blocks.Count);

        session.DeleteForward();
        Assert.Equal("Vorher|Nachher", Texts(session.Document));

        session.Undo();
        Assert.Same(table, session.Document.Blocks[1]);
    }

    [Fact]
    public void Backspace_after_a_preserved_block_selects_it()
    {
        var table = new OpaqueBlock("<x />", PreservedKind.Table, ["Zelle"]);
        var session = Session(Create([new Paragraph("Vorher"), table, new Paragraph("Nachher")]));
        session.SetCaret(At(2, 0));

        session.DeleteBackward();
        session.DeleteBackward();

        Assert.Equal("Vorher|Nachher", Texts(session.Document));
    }

    [Fact]
    public void Statistics_count_words_and_characters()
    {
        var session = Session("Ein kurzer Satz.", "", "Noch\tzwei");

        var statistics = session.GetStatistics(pages: 1);

        Assert.Equal(5, statistics.Words);
        Assert.Equal(16 + 9, statistics.Characters);
        Assert.Equal(14 + 8, statistics.CharactersWithoutSpaces);
        Assert.Equal(2, statistics.Paragraphs);
        Assert.Equal(1, statistics.Pages);

        session.Select(At(0, 4), At(0, 10));
        Assert.Equal(1, session.GetSelectionStatistics().Words);
    }

    [Fact]
    public void Events_tell_the_views_what_happened()
    {
        var session = Session("Text");
        var content = 0;
        var selection = 0;
        var state = 0;
        var blockChanges = new List<DocumentChange>();
        session.ContentChanged += (_, _) => content++;
        session.SelectionChanged += (_, _) => selection++;
        session.StateChanged += (_, _) => state++;
        session.Document.Changed += (_, change) => blockChanges.Add(change);

        session.SetCaret(At(0, 4));
        session.InsertText("e");

        Assert.Equal(1, content);
        Assert.Equal(2, selection);
        Assert.True(state >= 2);
        var change = Assert.Single(blockChanges);
        Assert.Equal(new DocumentChange(DocumentChangeKind.Blocks, 0, 1, 1), change);
    }
}
