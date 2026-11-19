using DocumentFormat.OpenXml.Packaging;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Notes;

/// <summary>Footnotes and endnotes: inserting, numbering, the page foot, continuation, endnotes, docx.</summary>
public class NoteTests
{
    private static Story Text(NoteKind kind, string text) => new(
    [
        new Paragraph([new NoteMarkInline(kind, new CharacterProperties { StyleId = NoteNumbering.ReferenceStyle(kind) }), new TextRun(" " + text, CharacterProperties.Empty)],
            new ParagraphProperties { StyleId = NoteNumbering.TextStyle(kind) }),
    ]);

    private static string Glyphs(DocumentLayout layout, int page) =>
        string.Concat(layout.GetDisplayList(page).Items.OfType<GlyphRunItem>().Select(run => run.Text));

    [Fact]
    public void Inserting_a_footnote_numbers_it_puts_its_text_at_the_foot_of_the_page_and_undo_removes_it()
    {
        var (session, layout) = Editor(Create("Erster Satz.", "Zweiter Satz."));
        session.SetCaret(At(1, 13));
        var second = session.InsertNote(NoteKind.Footnote);
        session.SetNoteContent(second, Text(NoteKind.Footnote, "Die zweite."));
        session.SetCaret(At(0, 6));
        var first = session.InsertNote(NoteKind.Footnote);
        session.SetNoteContent(first, Text(NoteKind.Footnote, "Die erste."));
        AssertInvariants(session);

        Assert.Equal(At(0, 6), first);
        Assert.Equal("1", layout.NoteLabel(first));
        Assert.Equal("2", layout.NoteLabel(At(1, 13)));
        var page = layout.Pages[0];
        Assert.Equal(2, page.Footnotes.Count);
        Assert.Equal(["1", "2"], page.Footnotes.Select(note => note.Label));
        Assert.NotNull(page.FootnoteSeparator);
        var setup = page.Section.Properties;
        var last = page.Footnotes[^1].Lines[^1];
        Assert.True(last.Top + last.Line.Height <= setup.PageHeight - setup.MarginBottom + 0.01);
        Assert.True(page.Footnotes[0].Lines[0].Top > page.Lines[^1].Top);
        Assert.Contains("1 Die erste.", Glyphs(layout, 0).Replace("  ", " "));
        Assert.Contains(BuiltInStyles.Definition(NoteNumbering.FootnoteText)!.Id, session.Document.Styles.Styles.Select(style => style.Id));

        session.Undo();
        session.Undo();
        Assert.Single(layout.Pages[0].Footnotes);
        Assert.Equal("1", layout.NoteLabel(At(1, 13)));
        Editing.RandomEditTests.AssertSameLayout(layout, session.Document);
    }

    [Fact]
    public void A_long_footnote_continues_on_the_next_page_under_a_long_separator()
    {
        var paragraphs = Enumerable.Range(0, 40).Select(index => new Paragraph(Words(18, index))).ToList<Block>();
        var (session, layout) = Editor(Create(paragraphs));
        session.SetCaret(At(30, 5));
        var reference = session.InsertNote(NoteKind.Footnote);
        session.SetNoteContent(reference, Text(NoteKind.Footnote, Words(1500, 7)));

        var page = layout.PageOf(reference);
        var placed = layout.Pages[page].Footnotes.Single();
        Assert.False(placed.IsContinuation);
        var next = layout.Pages[page + 1];
        var continued = next.Footnotes.First();
        Assert.True(continued.IsContinuation);
        Assert.Equal(reference, continued.Reference);
        Assert.Equal(next.Section.Properties.ContentWidth, next.FootnoteSeparator!.Value.Width);
        var lines = layout.Pages.SelectMany(p => p.Footnotes).Where(note => note.Reference == reference).Sum(note => note.Lines.Count);
        Assert.True(lines > placed.Lines.Count);
        foreach (var each in layout.Pages)
        {
            var bottom = each.Section.Properties.PageHeight - each.Section.Properties.MarginBottom;
            Assert.All(each.Footnotes.SelectMany(note => note.Lines), line => Assert.True(line.Top + line.Line.Height <= bottom + 0.01));
            Assert.All(each.Lines, line => Assert.True(line.Top + line.Line.Height <= bottom - each.FootnoteHeight + 0.01));
        }

        Editing.RandomEditTests.AssertSameLayout(layout, session.Document);
    }

    [Fact]
    public void Endnotes_follow_the_text_and_are_numbered_with_roman_numerals()
    {
        var (session, layout) = Editor(Create("Eins.", "Zwei.", "Drei."));
        session.SetCaret(At(2, 5));
        session.SetNoteContent(session.InsertNote(NoteKind.Endnote), Text(NoteKind.Endnote, "Später."));
        session.SetCaret(At(0, 5));
        session.SetNoteContent(session.InsertNote(NoteKind.Endnote), Text(NoteKind.Endnote, "Früher."));

        var page = layout.Pages[^1];
        Assert.Equal(["i", "ii"], page.Endnotes.Select(note => note.Label));
        Assert.NotNull(page.EndnoteSeparator);
        Assert.True(page.EndnoteSeparator!.Value.Y > page.Lines[^1].Top);
        Assert.Contains("iFrüher.", Glyphs(layout, layout.PageCount - 1).Replace(" ", ""));
        Assert.Empty(page.Footnotes);
        Assert.Equal("ii", layout.NoteLabel(At(2, 5)));
    }

    [Fact]
    public void Notes_survive_docx_with_their_text_and_ids_and_the_separators_stay()
    {
        var (session, _) = Editor(Create("Mit Fußnote.", "Mit Endnote."));
        session.SetCaret(At(0, 12));
        session.SetNoteContent(session.InsertNote(NoteKind.Footnote), Text(NoteKind.Footnote, "Quelle: ThixWrite."));
        session.SetCaret(At(1, 12));
        session.SetNoteContent(session.InsertNote(NoteKind.Endnote), Text(NoteKind.Endnote, "Am Ende."));

        var bytes = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(bytes);
        var read = DocxTestSupport.Read(bytes);
        Assert.Empty(read.UnsupportedContent);
        DocxTestSupport.AssertSameContent(session.Document, read);
        var note = read.ParagraphAt(0)!.Inlines.OfType<NoteInline>().Single();
        Assert.Equal("Quelle: ThixWrite.", note.PlainText.Trim());
        Assert.Equal("1", note.SourceId);

        using (var package = WordprocessingDocument.Open(new MemoryStream(bytes), false))
        {
            var footnotes = package.MainDocumentPart!.FootnotesPart!.Footnotes!;
            Assert.Equal(3, footnotes.ChildElements.Count); // separator, continuation separator, the note
            Assert.Contains("footnoteRef", footnotes.OuterXml);
            Assert.NotNull(package.MainDocumentPart.EndnotesPart);
        }

        // Written again: the same ids, the same number of notes; a deleted note leaves the part.
        var again = DocxTestSupport.Write(read);
        Assert.Equal(DocxTestSupport.DocumentXml(bytes), DocxTestSupport.DocumentXml(again));
        var (edited, _) = Editor(read);
        edited.Select(At(0, 12), At(0, 13));
        edited.DeleteSelection();
        using var after = WordprocessingDocument.Open(new MemoryStream(DocxTestSupport.Write(read)), false);
        Assert.Equal(2, after.MainDocumentPart!.FootnotesPart!.Footnotes!.ChildElements.Count);
    }

    [Fact]
    public void Copying_text_with_a_note_copies_the_note()
    {
        var session = Session("Text");
        session.SetCaret(At(0, 4));
        session.SetNoteContent(session.InsertNote(NoteKind.Footnote), Text(NoteKind.Footnote, "Kopiert."));
        session.SelectAll();
        var fragment = ClipboardFormats.TryDeserialize(ClipboardFormats.Serialize(session.Copy()!))!;
        session.SetCaret(session.Document.End);
        session.Paste(fragment);
        var notes = NoteNumbering.All(session.Document).ToList();
        Assert.Equal(2, notes.Count);
        Assert.All(notes, note => Assert.Equal("Kopiert.", note.Note.PlainText.Trim()));
        AssertInvariants(session);
    }

    [Fact]
    public void Next_footnote_moves_the_caret_behind_the_following_reference()
    {
        var session = Session("Ein Satz.", "Noch einer.");
        session.SetCaret(At(0, 3));
        session.InsertNote(NoteKind.Footnote);
        session.SetCaret(At(1, 4));
        session.InsertNote(NoteKind.Footnote);
        session.SetCaret(At(0, 0));
        Assert.Equal(At(0, 3), session.NextNote(NoteKind.Footnote));
        Assert.Equal(At(0, 4), session.Selection.Active);
        Assert.Equal(At(1, 4), session.NextNote(NoteKind.Footnote));
        Assert.Null(session.NextNote(NoteKind.Footnote));
        Assert.Equal(At(0, 3), session.NextNote(NoteKind.Footnote, forward: false));
    }
}
