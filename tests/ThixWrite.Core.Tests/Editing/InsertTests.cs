using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

/// <summary>Einfügen: Deckblatt, Leere Seite, Initiale, Lesezeichen.</summary>
public sealed class InsertTests
{
    private static EditingSession NewSession(params string[] paragraphs) => new(new Document(paragraphs.Select(text => new Paragraph(text)), BuiltInStyles.CreateDefault()));

    [Fact]
    public void A_cover_page_goes_before_the_text_on_a_page_of_its_own_and_a_second_one_replaces_it()
    {
        var session = NewSession("Der Text.");
        session.InsertCoverPage(CoverDesign.Band, "Jahresbericht", "2026", "Anna Berger", new DateTime(2026, 11, 20));
        Assert.Equal("Jahresbericht", string.Concat(session.Document.ParagraphAt(1 - 1)!.Inlines.OfType<TextRun>().Select(run => run.Text)));
        Assert.Contains(session.Document.ParagraphAt(0)!.Inlines, inline => inline is ShapeInline { Anchor.BehindText: true });
        using (var layout = NewLayout(session.Document))
        {
            Assert.Equal(2, layout.PageCount);
            Assert.Contains(layout.Pages[1].Lines, line => ((Paragraph)session.Document.Blocks[line.Block]).Text == "Der Text.");
        }

        session.InsertCoverPage(CoverDesign.Frame, "Neu", "", "Anna", new DateTime(2026, 11, 20));
        Assert.Single(session.Document.Blocks.OfType<Paragraph>(), paragraph => paragraph.Text == "Der Text.");
        Assert.Single(session.Document.Blocks.OfType<Paragraph>(), paragraph => paragraph.Inlines.OfType<TextRun>().Any(run => run.Text == "Neu"));
        Assert.DoesNotContain(session.Document.Blocks.OfType<Paragraph>(), paragraph => paragraph.Inlines.OfType<TextRun>().Any(run => run.Text == "Jahresbericht"));

        var written = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(written);
        Assert.Contains("Cover Pages", DocxTestSupport.DocumentXml(written));
    }

    [Fact]
    public void A_blank_page_is_a_page_of_its_own()
    {
        var session = NewSession("Vorher", "Nachher");
        session.SetCaret(At(1, 0));
        session.InsertBlankPage();
        using var layout = NewLayout(session.Document);
        Assert.Equal(3, layout.PageCount);
    }

    [Fact]
    public void A_drop_cap_takes_the_first_letter_into_a_frame_paragraph_and_goes_back_into_the_text()
    {
        var session = NewSession("Wenn die Sonne im Dezember nicht mehr aufgeht.");
        session.SetCaret(At(0, 5));
        session.SetDropCap(3);
        Assert.Equal("W", session.Document.ParagraphAt(0)!.Text);
        Assert.Equal(3, session.Document.ParagraphAt(0)!.Properties.DropCapLines);
        Assert.Equal("enn die Sonne im Dezember nicht mehr aufgeht.", session.Document.ParagraphAt(1)!.Text);
        Assert.Equal(3, session.DropCapLines);

        var reread = DocxTestSupport.RoundTrip(session.Document);
        Assert.Equal(3, reread.ParagraphAt(0)!.Properties.DropCapLines);

        session.SetDropCap(0);
        Assert.Equal("Wenn die Sonne im Dezember nicht mehr aufgeht.", session.Document.ParagraphAt(0)!.Text);
        Assert.Null(session.Document.ParagraphAt(0)!.Properties.DropCapLines);
    }

    [Fact]
    public void A_bookmark_marks_the_selection_a_link_jumps_to_it_and_it_can_be_deleted()
    {
        var session = NewSession("Hier steht das Ziel.", "Und hier der Link.");
        Assert.NotNull(EditingSession.BookmarkNameProblem("mit Leerzeichen"));
        Assert.NotNull(EditingSession.BookmarkNameProblem("1Ziffer"));
        Assert.Null(EditingSession.BookmarkNameProblem("Ziel_1"));
        session.Select(At(0, 5), At(0, 10));
        session.AddBookmark("Ziel");
        Assert.Equal([("Ziel", At(0, 5))], session.Bookmarks());
        Assert.Equal("Hier steht das Ziel.", session.Document.ParagraphAt(0)!.Text.Replace(SpecialCharacters.Object.ToString(), "", StringComparison.Ordinal));

        session.Select(At(1, 9), At(1, 13));
        session.SetHyperlink("", null, anchor: "Ziel");
        Assert.Contains(session.Document.ParagraphAt(1)!.Inlines, inline => inline.Link?.Anchor == "Ziel");
        Assert.True(session.GoToBookmark("Ziel"));
        Assert.Equal(0, session.Selection.Start.Block);

        var reread = DocxTestSupport.RoundTrip(session.Document);
        Assert.Contains(new EditingSession(reread).Bookmarks(), bookmark => bookmark.Name == "Ziel");

        session.DeleteBookmark("Ziel");
        Assert.Empty(session.Bookmarks());
        Assert.Equal("Hier steht das Ziel.", session.Document.ParagraphAt(0)!.Text);
    }
}
