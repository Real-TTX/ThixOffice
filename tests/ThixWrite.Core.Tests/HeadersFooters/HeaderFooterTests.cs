using DocumentFormat.OpenXml.Packaging;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.HeadersFooters;

/// <summary>Headers, footers and page numbers: which page shows which, the fields, docx.</summary>
public class HeaderFooterTests
{
    /// <summary>Three pages (two page breaks) with the caret at the start.</summary>
    private static (EditingSession Session, DocumentLayout Layout) ThreePages()
    {
        var (session, layout) = Editor(Create("Eins", "Zwei", "Drei"));
        session.SetCaret(At(0, 4));
        session.InsertPageBreak();
        session.SetCaret(At(2, 4));
        session.InsertPageBreak();
        session.SetCaret(At(0, 0));
        Assert.Equal(3, layout.PageCount);
        return (session, layout);
    }

    private static string TextOn(IReadOnlyList<PlacedLine> lines, DocumentLayout layout, int page) =>
        string.Concat(layout.GetDisplayList(page).Items.OfType<GlyphRunItem>()
            .Where(run => lines.Any(line => Math.Abs(run.Y - (line.Top + line.Line.Baseline)) < 0.5))
            .Select(run => run.Text));

    [Fact]
    public void A_page_number_at_the_bottom_shows_each_page_its_number_and_undo_removes_it()
    {
        var (session, layout) = ThreePages();
        session.InsertPageNumber(top: false, ParagraphAlignment.Center);

        for (var page = 0; page < 3; page++)
        {
            var footer = layout.Pages[page].FooterLines;
            Assert.NotEmpty(footer);
            Assert.Equal((page + 1).ToString(), TextOn(footer, layout, page));
            var setup = layout.Pages[page].Section.Properties;
            Assert.True(footer[^1].Top + footer[^1].Line.Height <= setup.PageHeight - setup.FooterDistance + 0.01);
        }

        Assert.Equal(BuiltInStyles.Footer, ((Paragraph)session.Document.FinalSection.Footers.Default!.Blocks[0]).Properties.StyleId);
        session.Undo();
        Assert.Empty(layout.Pages[1].FooterLines);
    }

    [Fact]
    public void The_first_page_and_even_pages_show_their_own_headers()
    {
        var (session, layout) = ThreePages();
        var section = session.CurrentSection;
        session.SetHeaderFooter(section, HeaderFooterKind.Default, false, new Story([new Paragraph("Ungerade")]));
        session.SetHeaderFooter(section, HeaderFooterKind.Even, false, new Story([new Paragraph("Gerade")]));
        session.SetHeaderFooter(section, HeaderFooterKind.First, false, new Story([new Paragraph("Titel")]));

        Assert.Equal("Ungerade", TextOn(layout.Pages[0].HeaderLines, layout, 0));
        session.SetTitlePage(true);
        session.SetEvenAndOddHeaders(true);
        Assert.Equal(["Titel", "Gerade", "Ungerade"], Enumerable.Range(0, 3).Select(page => TextOn(layout.Pages[page].HeaderLines, layout, page)));
        Assert.Equal(HeaderFooterKind.Even, layout.Pages[1].HeaderKind);
        Editing.RandomEditTests.AssertSameLayout(layout, session.Document);
    }

    [Fact]
    public void Fields_show_the_page_the_number_of_pages_and_the_date()
    {
        var page = FieldInline.Page(CharacterProperties.Empty);
        Assert.Equal("7", page.Text(7, 9, DateTime.Today));
        Assert.Equal("9", FieldInline.NumPages(CharacterProperties.Empty).Text(7, 9, DateTime.Today));
        var date = new FieldInline(FieldKind.Date, " DATE \\@ \"d. MMMM yyyy\" ", "", CharacterProperties.Empty);
        Assert.Equal("5. Dezember 2026", date.Text(1, 1, new DateTime(2026, 12, 5)));
        Assert.Equal(FieldKind.Page, FieldInline.KindOf(" PAGE \\* Arabic "));
        Assert.Null(FieldInline.KindOf(" HYPERLINK \"x\" "));

        var (session, _) = Editor(Create("Heute: "));
        session.SetCaret(At(0, 7));
        session.InsertField(FieldKind.Date, new DateTime(2026, 11, 19));
        Assert.Equal("19.11.2026", session.Document.ParagraphAt(0)!.Inlines.OfType<FieldInline>().Single().Result);
    }

    [Fact]
    public void Headers_and_footers_with_fields_survive_docx_and_stay_in_their_parts()
    {
        var (session, _) = ThreePages();
        session.InsertPageNumber(top: false, ParagraphAlignment.Right);
        var header = new Story([new Paragraph([new TextRun("Bericht – Seite ", CharacterProperties.Empty), FieldInline.Page(CharacterProperties.Empty),
            new TextRun(" von ", CharacterProperties.Empty), FieldInline.NumPages(CharacterProperties.Empty)])]);
        session.SetHeaderFooter(session.CurrentSection, HeaderFooterKind.Default, false, header);
        session.SetTitlePage(true);
        session.SetEvenAndOddHeaders(true);

        var bytes = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(bytes);
        var read = DocxTestSupport.Read(bytes);
        DocxTestSupport.AssertSameContent(session.Document, read);
        Assert.True(read.Settings.EvenAndOddHeaders);
        Assert.Contains(read.FinalSection.Headers.Default!.Blocks.OfType<Paragraph>().Single().Inlines, inline => inline is FieldInline { Kind: FieldKind.NumPages });

        // Written again: into the same parts, no new ones.
        var again = DocxTestSupport.Write(read);
        using var first = WordprocessingDocument.Open(new MemoryStream(bytes), false);
        using var second = WordprocessingDocument.Open(new MemoryStream(again), false);
        Assert.Equal(first.MainDocumentPart!.HeaderParts.Count(), second.MainDocumentPart!.HeaderParts.Count());
        Assert.Equal(first.MainDocumentPart.FooterParts.Count(), second.MainDocumentPart.FooterParts.Count());
        Assert.Contains("PAGE", second.MainDocumentPart.FooterParts.Single().Footer!.OuterXml);
    }
}
