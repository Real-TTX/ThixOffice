using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

/// <summary>Änderungen nachverfolgen: recording, the markup views, Annehmen / Ablehnen, Vorherige / Nächste, the docx round trip.</summary>
public sealed class RevisionTests
{
    private static EditingSession Tracking(params string[] paragraphs)
    {
        var session = new EditingSession(new Document(paragraphs.Select(text => new Paragraph(text)), BuiltInStyles.CreateDefault()), () => new DateTime(2026, 11, 20, 9, 30, 15, DateTimeKind.Utc))
        {
            RevisionAuthor = "Anna Berger",
        };
        session.SetTrackChanges(true);
        return session;
    }

    [Fact]
    public void Typing_and_deleting_while_tracked_are_recorded_with_author_and_time()
    {
        var session = Tracking("Der Umsatz stieg.");
        session.SetCaret(At(0, 11));
        session.InsertText("deutlich ");
        var inserted = session.Document.ParagraphAt(0)!.Inlines.OfType<TextRun>().Single(run => run.Revision is not null);
        Assert.Equal(("deutlich ", RevisionKind.Insertion, "Anna Berger"), (inserted.Text, inserted.Revision!.Kind, inserted.Revision.Author));
        Assert.Equal(new DateTime(2026, 11, 20, 9, 30, 0, DateTimeKind.Utc), inserted.Revision.Date);

        // Deleting "Umsatz": it stays, marked deleted; Backspace over own insertion removes it for good.
        session.Select(At(0, 4), At(0, 10));
        session.DeleteSelection();
        var paragraph = session.Document.ParagraphAt(0)!;
        Assert.Equal("Der Umsatz deutlich stieg.", paragraph.Text);
        Assert.Contains(paragraph.Inlines.OfType<TextRun>(), run => run is { Text: "Umsatz", Revision.Kind: RevisionKind.Deletion });
        session.SetCaret(At(0, 19));
        session.DeleteBackward();
        Assert.Equal("Der Umsatz deutlic stieg.", session.Document.ParagraphAt(0)!.Text);

        // The final text hides the deletion; "Alle Markups" shows it struck through and the insertion underlined.
        using var layout = NewLayout(session.Document);
        Assert.DoesNotContain(layout.GetDisplayList(0).Items.OfType<GlyphRunItem>(), run => run.Text.Contains("Umsatz", StringComparison.Ordinal));
        Assert.Contains(layout.GetDisplayList(0).Items, item => item is LineItem { X1: var x1, X2: var x2 } && Math.Abs(x1 - x2) < 0.01); // the change bar
        layout.RevisionView = RevisionView.AllMarkup;
        Assert.Contains(layout.GetDisplayList(0).Items.OfType<GlyphRunItem>(), run => run.Text.Contains("Umsatz", StringComparison.Ordinal));
        layout.RevisionView = RevisionView.Original;
        Assert.DoesNotContain(layout.GetDisplayList(0).Items.OfType<GlyphRunItem>(), run => run.Text.Contains("deutlic", StringComparison.Ordinal));
    }

    [Fact]
    public void Changes_are_accepted_and_rejected_one_by_one_and_found_with_next_and_previous()
    {
        var session = Tracking("Eins zwei drei.");
        session.Select(At(0, 5), At(0, 10));
        session.DeleteSelection(); // "zwei " deleted
        session.SetCaret(At(0, 15));
        session.InsertText(" vier");

        session.SetCaret(At(0, 0));
        Assert.True(session.NextRevision(forward: true));
        Assert.Equal((At(0, 5), At(0, 10)), (session.Selection.Start, session.Selection.End));
        session.ResolveRevision(accept: true); // the deletion goes
        Assert.Equal("Eins drei. vier", session.Document.ParagraphAt(0)!.Text);
        Assert.True(session.NextRevision(forward: true));
        session.ResolveRevision(accept: false); // the insertion goes
        Assert.Equal("Eins drei.", session.Document.ParagraphAt(0)!.Text);
        Assert.False(session.HasRevisions);
        Assert.False(session.NextRevision(forward: true));
    }

    [Fact]
    public void Recorded_changes_and_the_tracking_setting_survive_the_docx_round_trip()
    {
        var session = Tracking("Alt.");
        session.Select(At(0, 0), At(0, 3));
        session.InsertText("Neu");
        var written = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(written);
        var xml = DocxTestSupport.DocumentXml(written);
        Assert.Contains("<w:del ", xml);
        Assert.Contains("<w:delText>Alt</w:delText>", xml);
        Assert.Contains("<w:ins ", xml);
        Assert.Contains("w:author=\"Anna Berger\"", xml);
        var reread = DocxTestSupport.Read(written);
        Assert.True(reread.Settings.TrackRevisions);
        Assert.Equal(session.Document.ParagraphAt(0)!.Inlines, reread.ParagraphAt(0)!.Inlines.Select(inline => inline.Revision is null ? inline : inline with { Revision = inline.Revision with { Id = null } }));
    }
}
