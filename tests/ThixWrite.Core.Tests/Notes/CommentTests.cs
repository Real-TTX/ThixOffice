using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Tests.Notes;

/// <summary>Comments: anchored to text, edited, deleted with their text, stepped through, comments.xml with Word's threads.</summary>
public class CommentTests
{
    private static readonly DateTime When = new(2026, 11, 19, 10, 30, 0);

    [Fact]
    public void A_comment_covers_the_selection_or_the_word_at_the_caret_and_undo_removes_it()
    {
        var session = Session("Der Umsatz stieg deutlich.", "Zweiter Absatz.");
        session.Select(At(0, 4), At(0, 16));
        var reference = session.AddComment("Anna Berger", "AB", "Quelle?", When);
        AssertInvariants(session);

        var placed = Assert.Single(session.Comments());
        Assert.Equal(reference, placed.Reference);
        Assert.Equal(At(0, 4), placed.Range.Start);
        Assert.Equal("Quelle?", placed.Comment.Text);
        Assert.Equal("Anna Berger", placed.Comment.Author);
        Assert.Equal("Der Umsatz stieg deutlich.", session.Document.ParagraphAt(0)!.Text.Replace("￼", ""));

        // On the word at the caret when nothing is selected.
        session.SetCaret(At(1, 3));
        session.AddComment("Anna Berger", "AB", "", When);
        var second = session.Comments()[1];
        Assert.Equal("Zweiter", Slice(session, second.Range));
        Assert.Equal("2", second.Comment.Id);

        session.Undo();
        session.Undo();
        Assert.Empty(session.Comments());
        Assert.Equal("Der Umsatz stieg deutlich.", session.Document.ParagraphAt(0)!.Text);
    }

    private static string Slice(EditingSession session, DocumentRange range) =>
        session.Document.ParagraphAt(range.Start.Block)!.Text[range.Start.Offset..range.End.Offset].Replace("￼", "");

    [Fact]
    public void Editing_deleting_and_stepping_through_comments()
    {
        var session = Session("Eins zwei drei vier.");
        session.Select(At(0, 0), At(0, 4));
        var first = session.AddComment("A", "A", "Erster", When);
        session.Select(At(0, 11), At(0, 15));
        session.AddComment("B", "B", "Zweiter", When);

        session.SetCommentText(first, "Erster, geändert\nzweite Zeile");
        Assert.Equal("Erster, geändert\nzweite Zeile", session.CommentAt(first)!.Text);
        Assert.Equal(2, session.CommentAt(first)!.Content.Blocks.Count);

        session.SetCaret(At(0, 0));
        Assert.Equal("Erster, geändert\nzweite Zeile", session.NextComment(forward: true)!.Comment.Text);
        Assert.Equal("Zweiter", session.NextComment(forward: true)!.Comment.Text);
        Assert.Null(session.NextComment(forward: true));
        Assert.Equal("Erster, geändert\nzweite Zeile", session.NextComment(forward: false)!.Comment.Text);

        session.DeleteComment(session.Comments()[0].Reference);
        Assert.Equal("Zweiter", Assert.Single(session.Comments()).Comment.Text);
        Assert.Equal("Eins zwei drei vier.", session.Document.ParagraphAt(0)!.Text.Replace("￼", ""));
        session.DeleteAllComments();
        Assert.Equal("Eins zwei drei vier.", session.Document.ParagraphAt(0)!.Text);

        // Deleting the text a comment is about deletes the comment (as in Word).
        session.Undo();
        session.Undo();
        session.SelectAll();
        session.DeleteSelection();
        Assert.Empty(session.Comments());
        AssertInvariants(session);
    }

    [Fact]
    public void Comments_survive_docx_with_author_initials_date_and_text()
    {
        var session = Session("Text mit Kommentar.");
        session.Select(At(0, 5), At(0, 8));
        session.AddComment("Anna Berger", "AB", "Bitte prüfen.", When);

        var bytes = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(bytes);
        var xml = DocxTestSupport.DocumentXml(bytes);
        Assert.Contains("commentRangeStart", xml);
        Assert.Contains("commentReference", xml);
        var read = DocxTestSupport.Read(bytes);
        Assert.Empty(read.UnsupportedContent);
        var comment = Assert.Single(Model.Comments.All(read)).Comment;
        Assert.Equal(("Anna Berger", "AB", When, "Bitte prüfen."), (comment.Author, comment.Initials, comment.Date, comment.Text));
        Assert.Equal("mit", new EditingSession(read).Comments()[0] is { } placed ? read.ParagraphAt(0)!.Text[placed.Range.Start.Offset..placed.Range.End.Offset].Replace("￼", "") : "");
        DocxTestSupport.AssertSameContent(session.Document, read);
    }

    [Fact]
    public void Word_threads_stay_replies_are_known_and_a_deleted_comment_leaves_the_thread_parts()
    {
        using var stream = new MemoryStream();
        using (var package = WordprocessingDocument.Create(stream, WordprocessingDocumentType.Document))
        {
            var main = package.AddMainDocumentPart();
            main.Document = new W.Document(new W.Body(new W.Paragraph(
                new W.CommentRangeStart { Id = "0" }, new W.CommentRangeStart { Id = "1" },
                new W.Run(new W.Text("Strittig")),
                new W.CommentRangeEnd { Id = "0" }, new W.Run(new W.CommentReference { Id = "0" }),
                new W.CommentRangeEnd { Id = "1" }, new W.Run(new W.CommentReference { Id = "1" }))));
            var comments = main.AddNewPart<WordprocessingCommentsPart>();
            comments.Comments = new W.Comments(
                new W.Comment(new W.Paragraph(new W.Run(new W.AnnotationReferenceMark()), new W.Run(new W.Text("Warum?"))) { ParagraphId = "11111111" })
                    { Id = "0", Author = "Anna", Initials = "A", Date = new DateTime(2026, 1, 2) },
                new W.Comment(new W.Paragraph(new W.Run(new W.AnnotationReferenceMark()), new W.Run(new W.Text("Darum."))) { ParagraphId = "22222222" })
                    { Id = "1", Author = "Ben", Initials = "B" });
            var extended = main.AddNewPart<WordprocessingCommentsExPart>();
            const string W15 = "http://schemas.microsoft.com/office/word/2012/wordml";
            var root = new DocumentFormat.OpenXml.Office2013.Word.CommentsEx();
            foreach (var (paraId, parent) in new[] { ("11111111", (string?)null), ("22222222", "11111111") })
            {
                var entry = new DocumentFormat.OpenXml.Office2013.Word.CommentEx();
                entry.SetAttribute(new OpenXmlAttribute("w15", "paraId", W15, paraId));
                if (parent is not null)
                {
                    entry.SetAttribute(new OpenXmlAttribute("w15", "paraIdParent", W15, parent));
                }

                entry.SetAttribute(new OpenXmlAttribute("w15", "done", W15, "0"));
                root.AppendChild(entry);
            }

            extended.CommentsEx = root;
        }

        var document = DocxTestSupport.Read(stream.ToArray());
        Assert.Empty(document.UnsupportedContent);
        var all = Model.Comments.All(document);
        Assert.Equal(2, all.Count);
        Assert.Equal("11111111", all[1].Comment.ParentParaId);
        Assert.True(all[1].Comment.IsReply);
        var session = new EditingSession(document);
        Assert.Single(session.RepliesTo(all[0].Comment));

        // Unchanged: written as read (the paragraph ids, Word's threads). Deleting the first comment takes its reply along.
        var again = DocxTestSupport.Write(document);
        DocxTestSupport.AssertValid(again);
        using (var package = WordprocessingDocument.Open(new MemoryStream(again), false))
        {
            Assert.Contains("22222222", package.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.OuterXml);
            Assert.Equal(2, package.MainDocumentPart.WordprocessingCommentsExPart!.RootElement!.ChildElements.Count);
        }

        session.DeleteComment(all[0].Reference);
        Assert.Empty(session.Comments());
        using var after = WordprocessingDocument.Open(new MemoryStream(DocxTestSupport.Write(document)), false);
        Assert.Empty(after.MainDocumentPart!.WordprocessingCommentsPart!.Comments!.ChildElements);
        Assert.Empty(after.MainDocumentPart.WordprocessingCommentsExPart!.RootElement!.ChildElements);
    }
}
