using ThixWrite.Core.Editing;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using ThixWrite.Core.Tests.RealWorld;

namespace ThixWrite.Core.Tests.Editing;

/// <summary>Layout › Spalten / Umbrüche, Entwurf › Seitenfarbe, Groß-/Kleinschreibung and accepting/rejecting tracked changes: undoable, saved.</summary>
public sealed class LayoutReviewTests
{
    private static EditingSession Session(params string[] paragraphs) =>
        new(new Document(paragraphs.Select(text => new Paragraph(text))));

    [Fact]
    public void Columns_are_set_for_the_section_saved_and_undone()
    {
        var session = Session("Eins", "Zwei");
        session.SetColumns(3, separator: true);
        Assert.Equal(3, session.Document.FinalSection.ColumnCount);
        Assert.True(session.Document.FinalSection.ColumnSeparator);
        var read = DocxTestSupport.RoundTrip(session.Document);
        Assert.Equal(3, read.FinalSection.ColumnCount);
        Assert.True(read.FinalSection.ColumnSeparator);

        session.SetUnequalColumns(narrowLeft: true);
        Assert.True(session.Document.FinalSection.Columns[0].Width < session.Document.FinalSection.Columns[1].Width);
        session.Undo();
        session.Undo();
        Assert.Equal(1, session.Document.FinalSection.ColumnCount);
    }

    [Fact]
    public void A_continuous_section_break_splits_the_paragraph_and_starts_a_section_there()
    {
        var session = Session("Vorher Nachher");
        session.SetCaret(new DocumentPosition(0, 7));
        session.InsertSectionBreak(SectionStart.Continuous);
        Assert.Equal(2, session.Document.Sections.Count);
        Assert.Equal("Vorher ", ((Paragraph)session.Document.Blocks[0]).Text);
        Assert.NotNull(((Paragraph)session.Document.Blocks[0]).SectionBreak);
        Assert.Equal(SectionStart.Continuous, session.Document.FinalSection.Start);
        session.SetColumns(2);
        Assert.Equal(1, session.Document.Sections[0].Properties.ColumnCount);
        Assert.Equal(2, session.Document.FinalSection.ColumnCount);

        session.InsertColumnBreak();
        Assert.Contains(((Paragraph)session.Document.Blocks[1]).Inlines, inline => inline is OpaqueInline { Kind: PreservedKind.ColumnBreak });
        Assert.Contains("w:type=\"column\"", DocxTestSupport.DocumentXml(DocxTestSupport.Write(session.Document)));
    }

    [Fact]
    public void Groß_und_Kleinschreibung_changes_the_letters_and_keeps_the_formatting()
    {
        var session = new EditingSession(new Document([new Paragraph([new TextRun("hallo ", CharacterProperties.Empty), new TextRun("welt", new CharacterProperties { Bold = true })])]));
        session.SelectAll();
        session.CycleCase();
        Assert.Equal("HALLO WELT", ((Paragraph)session.Document.Blocks[0]).Text);
        Assert.True(((TextRun)((Paragraph)session.Document.Blocks[0]).Inlines[1]).Properties.Bold);
        session.CycleCase();
        Assert.Equal("Hallo Welt", ((Paragraph)session.Document.Blocks[0]).Text);
        session.ChangeCase(CaseChange.Toggle);
        Assert.Equal("hALLO wELT", ((Paragraph)session.Document.Blocks[0]).Text);
        session.Undo();
        Assert.Equal("Hallo Welt", ((Paragraph)session.Document.Blocks[0]).Text);
    }

    [Fact]
    public void The_page_colour_is_set_saved_and_undone()
    {
        var session = Session("Text");
        session.SetPageColor("FFF2CC");
        Assert.Equal("FFF2CC", session.Document.PageColor);
        var bytes = DocxTestSupport.Write(session.Document);
        Assert.Contains("w:background w:color=\"FFF2CC\"", DocxTestSupport.DocumentXml(bytes));
        Assert.Equal("FFF2CC", DocxTestSupport.Read(bytes).PageColor);
        session.Undo();
        Assert.Null(session.Document.PageColor);
    }

    [Fact]
    public void Tracked_changes_from_Word_are_accepted_or_rejected_as_a_whole()
    {
        var accepted = new EditingSession(DocxReader.Read(new MemoryStream(Corpus.Bytes("08-demo-review.docx"))));
        Assert.True(accepted.HasRevisions);
        accepted.AcceptAllRevisions();
        Assert.False(accepted.HasRevisions);
        var text = string.Join("\n", accepted.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Text));
        Assert.Contains("Ein Satz mit eingefügtem und Text.", text);
        Assert.DoesNotContain("gelöschtem", text);
        Assert.DoesNotMatch(@"<w:(ins|del)[ >]", DocxTestSupport.DocumentXml(DocxTestSupport.Write(accepted.Document)));

        var rejected = new EditingSession(DocxReader.Read(new MemoryStream(Corpus.Bytes("08-demo-review.docx"))));
        rejected.RejectAllRevisions();
        var original = string.Join("\n", rejected.Document.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Text));
        Assert.Contains("Ein Satz mit und gelöschtem Text.", original);
        Assert.DoesNotContain("Ein ganz neuer Absatz.", original);
        rejected.Undo();
        Assert.True(rejected.HasRevisions);
    }
}
