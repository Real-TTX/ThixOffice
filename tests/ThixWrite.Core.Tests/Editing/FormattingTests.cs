using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

public class FormattingTests
{
    [Fact]
    public void Bold_on_a_range_splits_runs_and_the_query_reports_mixed()
    {
        var session = Session("Hallo Welt");
        session.Select(At(0, 0), At(0, 5));

        session.ToggleBold();

        var paragraph = session.Document.ParagraphAt(0)!;
        Assert.Equal(2, paragraph.Inlines.Length);
        Assert.True(paragraph.Inlines[0].Properties.Bold);
        Assert.Null(paragraph.Inlines[1].Properties.Bold);
        Assert.True(session.GetFormatting().Bold.Is(true));

        session.SelectAll();
        Assert.True(session.GetFormatting().Bold.IsMixed);

        session.ToggleBold();
        Assert.True(session.GetFormatting().Bold.Is(true));
        session.ToggleBold();
        Assert.True(session.GetFormatting().Bold.Is(false));

        // Not bold again: the direct formatting is gone, not "bold = false" (the style is not bold).
        Assert.All(session.Document.ParagraphAt(0)!.Inlines, inline => Assert.Null(inline.Properties.Bold));
        Assert.Single(session.Document.ParagraphAt(0)!.Inlines);
    }

    [Fact]
    public void Formatting_at_a_caret_between_words_is_pending_for_the_next_typing()
    {
        var session = Session("Hallo ");
        session.SetCaret(At(0, 6));

        session.ToggleItalic();
        Assert.True(session.GetFormatting().Italic.Is(true));
        Assert.False(session.CanUndo);

        session.InsertText("Welt");

        var paragraph = session.Document.ParagraphAt(0)!;
        Assert.True(paragraph.Inlines[^1].Properties.Italic);
        Assert.Equal("Welt", ((TextRun)paragraph.Inlines[^1]).Text);
    }

    [Fact]
    public void Pending_formatting_is_dropped_when_the_caret_moves()
    {
        var session = Session("Hallo ");
        session.SetCaret(At(0, 6));
        session.ToggleBold();

        session.Move(CaretMovement.CharacterLeft);

        Assert.Null(session.PendingFormat);
        Assert.True(session.GetFormatting().Bold.Is(false));
    }

    [Fact]
    public void Formatting_at_a_caret_inside_a_word_formats_the_word()
    {
        var session = Session("eins zwei drei");
        session.SetCaret(At(0, 7));

        session.ToggleBold();

        var paragraph = session.Document.ParagraphAt(0)!;
        var bold = paragraph.Inlines.Single(inline => inline.Properties.Bold == true);
        Assert.Equal("zwei", ((TextRun)bold).Text);
    }

    [Fact]
    public void Formatting_an_empty_paragraph_changes_its_mark()
    {
        var session = Session("");

        session.SetFontSize(20);

        Assert.Equal(20, session.Document.ParagraphAt(0)!.MarkProperties.FontSize);
        session.InsertText("groß");
        Assert.Equal(20, session.Document.ParagraphAt(0)!.Inlines[0].Properties.FontSize);
    }

    [Fact]
    public void Font_family_size_colour_and_highlight_reach_the_query()
    {
        var session = Session("Text");
        session.SelectAll();

        session.SetFontFamily("Arial");
        session.SetFontSize(14);
        session.SetTextColor("FF0000");
        session.SetHighlight(HighlightColor.Yellow);
        session.ToggleUnderline();
        session.ToggleStrikethrough();
        session.ToggleSuperscript();

        var formatting = session.GetFormatting();
        Assert.True(formatting.FontFamily.Is("Arial"));
        Assert.True(formatting.FontSize.Is(14));
        Assert.True(formatting.Color.Is("FF0000"));
        Assert.True(formatting.Highlight.Is(HighlightColor.Yellow));
        Assert.True(formatting.Underline.Is(UnderlineStyle.Single));
        Assert.True(formatting.Strikethrough.Is(true));
        Assert.True(formatting.Superscript.Is(true));

        session.ToggleSubscript();
        Assert.True(session.GetFormatting().Subscript.Is(true));
        Assert.True(session.GetFormatting().Superscript.Is(false));

        session.GrowFont();
        Assert.True(session.GetFormatting().FontSize.Is(16));
        session.GrowFont(grow: false);
        session.GrowFont(grow: false);
        Assert.True(session.GetFormatting().FontSize.Is(12));
    }

    [Fact]
    public void Clearing_formatting_resets_runs_paragraphs_and_style()
    {
        var session = Session("Text");
        session.SelectAll();
        session.ApplyStyle(BuiltInStyles.Heading2);
        session.ToggleBold();
        session.SetAlignment(ParagraphAlignment.Center);

        session.ClearFormatting();

        var paragraph = session.Document.ParagraphAt(0)!;
        Assert.True(paragraph.Properties.IsEmpty);
        Assert.True(paragraph.Inlines[0].Properties.IsEmpty);
        Assert.Equal("Standard", session.GetFormatting().ParagraphStyleName);
    }

    [Fact]
    public void Paragraph_formatting_applies_to_every_touched_paragraph()
    {
        var session = Session("A", "B", "C");
        session.Select(At(0, 0), At(1, 1));

        session.SetAlignment(ParagraphAlignment.Justify);
        session.SetLineSpacing(new LineSpacing(LineSpacingRule.Multiple, 1.5));
        session.SetSpacing(6, 12);
        session.SetIndents(18, 9, -9);

        var formatting = session.GetFormatting();
        Assert.True(formatting.Alignment.Is(ParagraphAlignment.Justify));
        Assert.True(formatting.LineSpacing.Is(new LineSpacing(LineSpacingRule.Multiple, 1.5)));
        Assert.True(formatting.SpacingBefore.Is(6));
        Assert.True(formatting.SpacingAfter.Is(12));
        Assert.True(formatting.IndentLeft.Is(18));
        Assert.True(formatting.FirstLineIndent.Is(-9));
        Assert.Null(session.Document.ParagraphAt(2)!.Properties.Alignment);

        session.SelectAll();
        Assert.True(session.GetFormatting().Alignment.IsMixed);
    }

    [Fact]
    public void A_selection_ending_at_a_paragraph_start_does_not_format_that_paragraph()
    {
        var session = Session("A", "B");
        session.SelectParagraph(At(0, 0));

        session.SetAlignment(ParagraphAlignment.Right);

        Assert.Equal(ParagraphAlignment.Right, session.Document.ParagraphAt(0)!.Properties.Alignment);
        Assert.Null(session.Document.ParagraphAt(1)!.Properties.Alignment);
    }

    [Fact]
    public void Applying_a_style_adds_a_missing_built_in_style_and_reports_its_german_name()
    {
        var document = new Document([new Paragraph("Text")], new StyleSheet([BuiltInStyles.Definition(BuiltInStyles.Normal)!]));
        var session = Session(document);

        session.ApplyStyle(BuiltInStyles.Quote);

        Assert.NotNull(session.Document.Styles.Find(BuiltInStyles.Quote));
        Assert.Equal("Zitat", session.GetFormatting().ParagraphStyleName);
        Assert.True(session.GetFormatting().Italic.Is(true));

        session.Undo();
        Assert.Null(session.Document.Styles.Find(BuiltInStyles.Quote));
    }

    [Fact]
    public void A_character_style_formats_the_selected_text()
    {
        var session = Session("ganz wichtig");
        session.Select(At(0, 5), At(0, 12));

        session.ApplyStyle(BuiltInStyles.Strong);

        Assert.True(session.GetFormatting().Bold.Is(true));
        Assert.True(session.GetFormatting().CharacterStyleId.Is(BuiltInStyles.Strong));
        session.SelectAll();
        Assert.True(session.GetFormatting().CharacterStyleId.IsMixed);
    }

    [Fact]
    public void Page_setup_changes_every_section()
    {
        var first = new Paragraph([new TextRun("Abschnitt 1", CharacterProperties.Empty)], null, null, SectionProperties.Default);
        var document = Create([first, new Paragraph("Abschnitt 2")]);
        var session = Session(document);

        session.SetPageSetup(setup => setup.WithOrientation(PageOrientation.Landscape) with { MarginLeft = 36 });

        Assert.Equal(PageOrientation.Landscape, session.Document.FinalSection.Orientation);
        Assert.Equal(36, session.Document.FinalSection.MarginLeft);
        Assert.Equal(PageOrientation.Landscape, session.Document.ParagraphAt(0)!.SectionBreak!.Orientation);
        Assert.True(session.Document.FinalSection.PageWidth > session.Document.FinalSection.PageHeight);

        session.Undo();
        Assert.Equal(PageOrientation.Portrait, session.Document.FinalSection.Orientation);
        Assert.Equal(PageOrientation.Portrait, session.Document.ParagraphAt(0)!.SectionBreak!.Orientation);
    }

    [Fact]
    public void The_formatting_query_is_cached_until_something_changes()
    {
        var session = Session("Text");
        var first = session.GetFormatting();

        Assert.Same(first, session.GetFormatting());

        session.SelectAll();
        Assert.NotSame(first, session.GetFormatting());
        Assert.True(session.GetFormatting().HasSelection);
    }

    [Fact]
    public void The_query_knows_undo_and_redo()
    {
        var session = Session("");
        session.InsertText("x");

        Assert.True(session.GetFormatting().CanUndo);
        Assert.Equal("Eingabe", session.GetFormatting().UndoLabel);
        session.Undo();
        Assert.True(session.GetFormatting().CanRedo);
        Assert.False(session.GetFormatting().CanUndo);
    }
}
