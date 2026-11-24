using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formats.Odt;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>OpenDocument Text: written and read back, a file as LibreOffice writes it, the package rules.</summary>
public class OdtTests
{
    private static readonly XNamespace Office = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    private static readonly XNamespace Text = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    private static readonly XNamespace Table = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";

    private static byte[] Write(Document document)
    {
        using var stream = new MemoryStream();
        OdtWriter.Write(document, stream);
        return stream.ToArray();
    }

    private static Document Read(byte[] bytes) => OdtReader.Read(new MemoryStream(bytes));

    private static string TextOf(Paragraph paragraph) => paragraph.Text.Replace("￼", "", StringComparison.Ordinal);

    /// <summary>A document with what the model has: headings, formatting, a list, a table, a picture, a link, notes, a header and a footer, a comment.</summary>
    private static Document Rich()
    {
        var session = new EditingSession(Document.CreateBlank());
        session.ApplyStyle(BuiltInStyles.Heading1);
        session.InsertText("Bericht");
        session.InsertParagraphBreak();
        session.ApplyStyle(BuiltInStyles.Normal);
        session.InsertText("Normal ");
        session.SetBold(true);
        session.InsertText("fett");
        session.SetBold(false);
        session.InsertText(" und ");
        session.SetTextColor("C00000");
        session.InsertText("rot");
        session.SetTextColor(null);
        session.InsertText(".\tTab  zwei Leer");
        session.SetAlignment(ParagraphAlignment.Justify);
        session.InsertParagraphBreak();
        session.SetAlignment(ParagraphAlignment.Left);
        session.ToggleList(ListKind.Bullet);
        session.InsertText("Punkt eins");
        session.InsertParagraphBreak();
        session.HandleTab(shift: false);
        session.InsertText("Unterpunkt");
        session.InsertParagraphBreak();
        session.RemoveList();
        session.ToggleList(ListKind.Numbered);
        session.InsertText("Erstens");
        session.InsertParagraphBreak();
        session.RemoveList();
        session.InsertText("Mit Fußnote");
        SampleNote(session, NoteKind.Footnote, "Die Quelle.");
        session.InsertText(" und Endnote");
        SampleNote(session, NoteKind.Endnote, "Am Schluss.");
        session.InsertParagraphBreak();
        session.InsertText("Link");
        session.Select(new DocumentPosition(session.Selection.Active.Block, 0), new DocumentPosition(session.Selection.Active.Block, 4));
        session.SetHyperlink("https://example.org", null);
        session.SetCaret(session.Document.End);
        session.InsertParagraphBreak();
        session.InsertTable(2, 3);
        session.InsertText("A1");
        session.HandleTab(shift: false);
        session.InsertText("B1");
        session.SetCaret(new DocumentPosition(session.CurrentTable!.End + 1, 0));
        session.InsertImage(ImageData.FromBytes(Png())!);
        session.SetImageSize(60, 30);
        session.SetCaret(session.Document.End);
        session.InsertParagraphBreak();
        session.InsertText("Kommentiert");
        session.Select(new DocumentPosition(session.Selection.Active.Block, 0), new DocumentPosition(session.Selection.Active.Block, 11));
        session.AddComment("Anna Berger", "AB", "Prüfen", new DateTime(2026, 11, 19, 9, 0, 0));
        session.InsertPageNumber(top: false, ParagraphAlignment.Center);
        session.SetHeaderFooter(0, HeaderFooterKind.Default, false, new Story([new Paragraph("Kopf")]));
        session.SetPageSetup(section => section with { PageWidth = 419.5, PageHeight = 595.3, Orientation = PageOrientation.Portrait, MarginLeft = 56.7 });
        return session.Document;
    }

    private static void SampleNote(EditingSession session, NoteKind kind, string text)
    {
        var reference = session.InsertNote(kind);
        var content = NoteNumbering.NewContent(kind);
        var paragraph = (Paragraph)content.Blocks[0];
        session.SetNoteContent(reference, content.WithBlocks([paragraph.WithInlines([paragraph.Inlines[0], new TextRun(" " + text, CharacterProperties.Empty)])]));
    }

    internal static byte[] Png()
    {
        // A 2 × 1 PNG (red, blue).
        return Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAIAAAB7QOjdAAAAEElEQVR4nGP4z8DwnwEIGAAP+wH/2VN2dQAAAABJRU5ErkJggg==");
    }

    [Fact]
    public void The_package_starts_with_its_mimetype_stored_and_has_a_manifest()
    {
        var bytes = Write(Rich());
        Assert.Equal("mimetype", Encoding.ASCII.GetString(bytes, 30, 8));
        Assert.Equal("application/vnd.oasis.opendocument.text", Encoding.ASCII.GetString(bytes, 38, 39));
        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal(ZipArchiveMode.Read, zip.Mode);
        var manifest = XDocument.Load(zip.GetEntry("META-INF/manifest.xml")!.Open());
        var paths = manifest.Descendants().Select(element => (string?)element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == "full-path")).Where(path => path is not null).ToList();
        Assert.Contains("content.xml", paths);
        Assert.Contains("styles.xml", paths);
        Assert.Contains(paths, path => path!.StartsWith("Pictures/", StringComparison.Ordinal));
        Assert.True(OdtReader.IsOdt(new MemoryStream(bytes)));
        Assert.False(OdtReader.IsOdt(new MemoryStream(DocxTestSupport.Write(Rich()))));
    }

    [Fact]
    public void What_the_model_has_survives_odt()
    {
        var original = Rich();
        var read = Read(Write(original));
        Assert.Empty(read.UnsupportedContent);
        var paragraphs = read.Blocks.OfType<Paragraph>().ToList();

        // Headings and character formatting.
        var heading = paragraphs.First(paragraph => TextOf(paragraph) == "Bericht");
        Assert.Equal(BuiltInStyles.Heading1, heading.Properties.StyleId);
        var body = paragraphs.First(paragraph => TextOf(paragraph).StartsWith("Normal", StringComparison.Ordinal));
        Assert.Equal("Normal fett und rot.\tTab  zwei Leer", TextOf(body));
        Assert.Contains(body.Inlines, inline => inline is TextRun { Text: "fett", Properties.Bold: true });
        Assert.Contains(body.Inlines, inline => inline is TextRun { Text: "rot", Properties.Color.Value: "C00000" });
        Assert.Equal(ParagraphAlignment.Justify, body.Properties.Alignment);

        // Lists: bullets with a second level, then numbers.
        var bullet = paragraphs.First(paragraph => TextOf(paragraph) == "Punkt eins");
        var sub = paragraphs.First(paragraph => TextOf(paragraph) == "Unterpunkt");
        var number = paragraphs.First(paragraph => TextOf(paragraph) == "Erstens");
        Assert.Equal(0, bullet.Properties.Numbering!.Level);
        Assert.Equal(1, sub.Properties.Numbering!.Level);
        Assert.Equal(NumberFormat.Bullet, read.Numbering.ResolveLevel(bullet.Properties.Numbering.NumberingId, 0)!.Format);
        Assert.Equal(NumberFormat.Decimal, read.Numbering.ResolveLevel(number.Properties.Numbering!.NumberingId, 0)!.Format);

        // Notes, the link, the table, the picture, the comment.
        var notes = NoteNumbering.All(read).ToList();
        Assert.Equal([NoteKind.Footnote, NoteKind.Endnote], notes.Select(note => note.Note.Kind));
        Assert.Equal("Die Quelle.", notes[0].Note.PlainText.Trim());
        Assert.IsType<NoteMarkInline>(((Paragraph)notes[0].Note.Content.Blocks[0]).Inlines[0]);
        Assert.Contains(paragraphs.SelectMany(paragraph => paragraph.Inlines), inline => inline is TextRun { Text: "Link", Link.Uri: "https://example.org" });
        var table = Assert.Single(TableSpan.All(read));
        Assert.Equal((2, 3), (table.Table.RowCount, table.Table.ColumnCount));
        Assert.Equal("B1", TextOf((Paragraph)read.Blocks[table.BlocksOf(0, 1).First]));
        Assert.Empty(table.Problems(read));
        var picture = paragraphs.SelectMany(paragraph => paragraph.Inlines).OfType<ImageInline>().Single();
        Assert.Equal((60, 30), (Math.Round(picture.Width), Math.Round(picture.Height)));
        var comment = Assert.Single(Model.Comments.All(read));
        Assert.Equal(("Anna Berger", "Prüfen"), (comment.Comment.Author, comment.Comment.Text));
        Assert.Equal("Kommentiert", read.ParagraphAt(comment.Range.Start.Block)!.Text[comment.Range.Start.Offset..comment.Range.End.Offset].Replace("￼", ""));

        // Page, header, footer.
        Assert.Equal(419.5, read.FinalSection.PageWidth, 1);
        Assert.Equal(56.7, read.FinalSection.MarginLeft, 1);
        Assert.Equal("Kopf", ((Paragraph)read.FinalSection.Headers.Default!.Blocks[0]).Text);
        Assert.True(read.FinalSection.Footers.Default!.HasPageFields);

        // Written again from what was read: the same text.
        var again = Read(Write(read));
        Assert.Equal(read.GetPlainText(), again.GetPlainText());
    }

    [Fact]
    public void A_file_as_LibreOffice_writes_it_is_read_with_styles_lists_tables_notes_and_its_page()
    {
        var document = Read(OdtFixtures.LibreOffice());
        var paragraphs = document.Blocks.OfType<Paragraph>().ToList();

        Assert.Equal("Überschrift", TextOf(paragraphs[0]));
        Assert.Equal(BuiltInStyles.Heading1, paragraphs[0].Properties.StyleId);
        Assert.Equal("Überschrift 1", BuiltInStyles.DisplayName(document.Styles.Find(BuiltInStyles.Heading1)!));
        Assert.Equal("Ein fetter Satz mit  zwei Leerzeichen,\tTab und\vUmbruch.", TextOf(paragraphs[1]));
        Assert.Contains(paragraphs[1].Inlines, inline => inline is TextRun { Text: "fetter", Properties.Bold: true });
        Assert.Equal(ParagraphAlignment.Center, paragraphs[1].Properties.Alignment);
        Assert.Equal(BuiltInStyles.Normal, paragraphs[1].Properties.StyleId);

        Assert.Equal(["Apfel", "Birne"], paragraphs.Where(paragraph => paragraph.Properties.Numbering is not null).Select(TextOf));
        var table = Assert.Single(TableSpan.All(document)).Table;
        Assert.Equal(2, table.RowCount);
        Assert.Equal(2, table.CellAt(0, 0).GridSpan); // A1 spans both columns
        Assert.Equal("D9E2F3", table.CellAt(0, 0).Shading!.Fill);

        var note = NoteNumbering.All(document).Single().Note;
        Assert.Equal("Fußnotentext.", note.PlainText.Trim());
        Assert.Equal("Anmerkung", Model.Comments.All(document).Single().Comment.Text);

        Assert.Equal(595.3, document.FinalSection.PageWidth, 0);
        Assert.Equal(56.7, document.FinalSection.MarginLeft, 0);
        Assert.True(document.FinalSection.Footers.Default!.HasPageFields);
        Assert.Equal("de-DE", DocumentLanguage.Of(document));

        // The shape is not shown and cannot be kept: the user is told.
        Assert.Equal(PreservedKind.Drawing, Assert.Single(document.UnsupportedContent).Kind);
    }

    [Fact]
    public void A_WordDocument_opens_and_saves_odt_by_its_content()
    {
        var word = new WordDocument("Test");
        typeof(ThixOffice.Core.Documents.OfficeDocument).GetMethod("ReadContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(word, [new MemoryStream(OdtFixtures.LibreOffice()), "LibreOffice.odt", null]);
        Assert.Equal(WordFileFormat.Odt, word.LoadedFormat);
        Assert.Equal(WordFileFormat.Odt, word.SaveFormat);
        Assert.Equal(WordFileFormat.Odt, WordDocument.FormatOf(".ODT"));
        using var saved = new MemoryStream();
        typeof(ThixOffice.Core.Documents.OfficeDocument).GetMethod("WriteContent", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!.Invoke(word, [saved]);
        Assert.True(OdtReader.IsOdt(new MemoryStream(saved.ToArray())));
        Assert.Contains(ThixWriteApp.Definition.SupportedTypes, type => type.Extension == ".odt");
    }
}
