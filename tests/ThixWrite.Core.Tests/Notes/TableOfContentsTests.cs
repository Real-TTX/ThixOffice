using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Tests.Notes;

/// <summary>The table of contents: entries from the headings with page numbers and links, updating, the TOC field in docx.</summary>
public class TableOfContentsTests
{
    private static Paragraph Heading(int level, string text) => new(text, new ParagraphProperties { StyleId = $"Heading{level}" });

    /// <summary>An empty paragraph for the table, then headings 1–4 with text; "Zwei" starts page 2.</summary>
    private static (EditingSession Session, Layout.DocumentLayout Layout) Report()
    {
        var styles = BuiltInStyles.CreateDefault();
        foreach (var id in new[] { "Heading4" })
        {
            styles = BuiltInStyles.Ensure(styles, id);
        }

        var document = new Document(
        [
            Paragraph.Empty(),
            Heading(1, "Eins"),
            new Paragraph("Text."),
            Heading(2, "Eins Punkt eins"),
            new Paragraph("Mehr Text.\f"),
            Heading(1, "Zwei"),
            Heading(3, "Zwei Punkt eins Punkt eins"),
            Heading(4, "Zu tief"),
            new Paragraph("Ende."),
        ], styles);
        var (session, layout) = Editor(document);
        session.SetCaret(At(0, 0));
        return (session, layout);
    }

    private static List<Paragraph> Entries(EditingSession session)
    {
        var region = session.FindTableOfContents()!;
        return Enumerable.Range(region.Start, region.End - region.Start + 1).Select(block => (Paragraph)session.Document.Blocks[block]).ToList();
    }

    private static string EntryText(Paragraph entry) => string.Concat(entry.Inlines.OfType<TextRun>().Select(run => run.Text));

    [Fact]
    public void Inserting_lists_headings_1_to_3_with_dotted_tabs_page_numbers_and_links_to_bookmarks()
    {
        var (session, layout) = Report();
        session.InsertTableOfContents();
        AssertInvariants(session);

        Assert.Equal(TableOfContents.Heading, session.Document.ParagraphAt(0)!.Text);
        Assert.Equal(BuiltInStyles.TocHeading, session.Document.ParagraphAt(0)!.Properties.StyleId);
        var entries = Entries(session);
        Assert.Equal(["Eins\t1", "Eins Punkt eins\t1", "Zwei\t2", "Zwei Punkt eins Punkt eins\t2"], entries.Select(EntryText));
        Assert.Equal(["TOC1", "TOC2", "TOC1", "TOC3"], entries.Select(entry => entry.Properties.StyleId));
        Assert.Equal(TabLeader.Dot, entries[0].Properties.Tabs!.Value[0].Leader);
        Assert.IsType<TocMarkInline>(entries[0].Inlines[0]);
        Assert.True(((TocMarkInline)entries[^1].Inlines[^1]).IsEnd);
        Assert.Equal(2, layout.PageNumberOf(session.FindBookmark("_Toc100000003")!.Value.Block));

        // Every entry links to its heading's bookmark; following it puts the caret on the heading.
        var anchor = entries[2].Inlines.OfType<TextRun>().First().Link!.Anchor!;
        Assert.True(session.GoToBookmark(anchor));
        Assert.Equal("Zwei", session.Document.ParagraphAt(session.Selection.Active.Block)!.Text.Trim('￼'));
        session.Undo();
        Assert.Null(session.FindTableOfContents());
        Assert.Equal("Eins", session.Document.ParagraphAt(1)!.Text);
    }

    [Fact]
    public void Updating_takes_new_headings_and_page_numbers_and_is_one_undo_step()
    {
        var (session, layout) = Report();
        session.InsertTableOfContents();
        var region = session.FindTableOfContents()!;
        var eins = region.End + 2; // "Eins" after the table and the empty paragraph
        Assert.Equal("Eins", session.Document.ParagraphAt(eins)!.Text.Trim('￼'));

        // A page break before "Eins": everything moves one page on; a new heading at the end.
        session.SetCaret(At(eins - 1, 0));
        session.InsertPageBreak();
        session.SetCaret(session.Document.End);
        session.InsertParagraphBreak();
        session.ApplyStyle(BuiltInStyles.Heading1);
        session.InsertText("Anhang");
        Assert.True(session.UpdateTableOfContents());

        var entries = Entries(session);
        Assert.Equal(["Eins\t2", "Eins Punkt eins\t2", "Zwei\t3", "Zwei Punkt eins Punkt eins\t3", "Anhang\t3"], entries.Select(EntryText));
        session.Undo();
        Assert.Equal(4, Entries(session).Count);
        Editing.RandomEditTests.AssertSameLayout(layout, session.Document);
    }

    [Fact]
    public void A_table_of_contents_is_a_real_TOC_field_in_docx_and_reads_back()
    {
        var (session, _) = Report();
        session.InsertTableOfContents();

        var bytes = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(bytes);
        var xml = DocxTestSupport.DocumentXml(bytes);
        Assert.Contains("TOC \\o \"1-3\" \\h \\z \\u", xml);
        Assert.Contains("w:fldCharType=\"begin\"", xml);
        Assert.Contains("w:fldCharType=\"separate\"", xml);
        Assert.Contains("w:fldCharType=\"end\"", xml);
        Assert.Contains("w:anchor=\"_Toc100000001\"", xml);
        Assert.Contains("w:bookmarkStart", xml);

        var read = DocxTestSupport.Read(bytes);
        Assert.Empty(read.UnsupportedContent);
        DocxTestSupport.AssertSameContent(session.Document, read);
        Assert.NotNull(new EditingSession(read).FindTableOfContents());
    }

    [Fact]
    public void A_table_of_contents_as_Word_writes_it_is_recognised_and_can_be_updated()
    {
        // Word 2016+: a content control "Table of Contents" around the heading and the TOC field, entries with PAGEREF fields.
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            static W.Run Char(W.FieldCharValues type) => new(new W.FieldChar { FieldCharType = type });
            static W.Run Code(string code) => new(new W.FieldCode(code) { Space = SpaceProcessingModeValues.Preserve });
            var entry = new W.Paragraph(
                new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Verzeichnis1" }),
                Char(W.FieldCharValues.Begin), Code(" TOC \\o \"1-3\" \\h \\z \\u "), Char(W.FieldCharValues.Separate),
                new W.Hyperlink(
                    new W.Run(new W.Text("Einleitung")), new W.Run(new W.TabChar()),
                    Char(W.FieldCharValues.Begin), Code(" PAGEREF _Toc1 \\h "), Char(W.FieldCharValues.Separate), new W.Run(new W.Text("1")), Char(W.FieldCharValues.End))
                { Anchor = "_Toc1", History = true });
            var control = new W.SdtBlock(
                new W.SdtProperties(new W.SdtContentDocPartObject(new W.DocPartGallery { Val = "Table of Contents" }, new W.DocPartUnique())),
                new W.SdtContentBlock(
                    new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Inhaltsverzeichnisberschrift" }), new W.Run(new W.Text("Inhalt"))),
                    entry,
                    new W.Paragraph(Char(W.FieldCharValues.End))));
            main.Document = new W.Document(new W.Body(
                control,
                new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }),
                    new W.BookmarkStart { Id = "0", Name = "_Toc1" }, new W.Run(new W.Text("Einleitung")), new W.BookmarkEnd { Id = "0" }),
                new W.Paragraph(new W.ParagraphProperties(new W.ParagraphStyleId { Val = "Heading1" }), new W.Run(new W.Text("Neu")))));
            main.AddNewPart<StyleDefinitionsPart>().Styles = new W.Styles(
                new W.Style(new W.StyleName { Val = "heading 1" }, new W.StyleParagraphProperties(new W.OutlineLevel { Val = 0 })) { Type = W.StyleValues.Paragraph, StyleId = "Heading1" });
        }

        var document = DocxTestSupport.Read(stream.ToArray());
        Assert.Empty(document.UnsupportedContent);
        var session = new EditingSession(document);
        var region = session.FindTableOfContents()!;
        Assert.Equal((1, 2), (region.Start, region.End));
        Assert.True(session.GoToBookmark("_Toc1"));

        session.UpdateTableOfContents();
        Assert.Equal(["Einleitung\t1", "Neu\t1"], Entries(session).Select(EntryText));
        Assert.Equal("_Toc1", Entries(session)[0].Inlines.OfType<TextRun>().First().Link!.Anchor); // the bookmark Word made is reused
        DocxTestSupport.AssertValid(DocxTestSupport.Write(document));
    }

    [Fact]
    public void Removing_takes_the_heading_and_the_entries()
    {
        var (session, _) = Report();
        session.InsertTableOfContents();
        session.RemoveTableOfContents();
        Assert.Null(session.FindTableOfContents());
        Assert.Equal("", session.Document.ParagraphAt(0)!.Text);
        Assert.Equal("Eins", session.Document.ParagraphAt(1)!.Text.Trim('￼'));
        AssertInvariants(session);
    }
}
