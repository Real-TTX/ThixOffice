using System.Reflection;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ThixOffice.Core.Documents;
using ThixOffice.Text;
using ThixOffice.Text.Skia;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Documents;

public class WordDocumentTests
{
    private static void ReadInto(OfficeDocument document, byte[] bytes) =>
        typeof(OfficeDocument).GetMethod("ReadContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(document, [new MemoryStream(bytes)]);

    private static byte[] WriteOf(OfficeDocument document)
    {
        using var stream = new MemoryStream();
        typeof(OfficeDocument).GetMethod("WriteContent", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(document, [stream]);
        return stream.ToArray();
    }

    [Fact]
    public void Editing_marks_the_document_unsaved_and_undo_goes_through_the_session()
    {
        var document = new WordDocument("Dokument1");
        var changes = 0;
        document.Changed += (_, _) => changes++;

        document.Session.InsertText("Hallo");

        Assert.True(document.IsDirty);
        Assert.True(document.CanUndo);
        Assert.True(changes > 0);
        document.Undo();
        Assert.Equal("", document.Content.GetPlainText());
        Assert.True(document.CanRedo);
    }

    [Fact]
    public void A_docx_is_read_and_written_as_docx()
    {
        var source = new WordDocument("Dokument1");
        source.Session.InsertText("Überschrift");
        source.Session.ApplyStyle(BuiltInStyles.Heading1);
        var bytes = WriteOf(source);

        var reopened = new WordDocument("Dokument2");
        var replaced = 0;
        reopened.ContentReplaced += (_, _) => replaced++;
        ReadInto(reopened, bytes);

        Assert.Equal(1, replaced);
        Assert.Equal(WordFileFormat.Docx, reopened.LoadedFormat);
        Assert.Equal("Überschrift", reopened.Content.GetPlainText());
        Assert.Equal(BuiltInStyles.Heading1, reopened.Content.ParagraphAt(0)!.Properties.StyleId);
        Assert.False(reopened.CanUndo);

        using var package = WordprocessingDocument.Open(new MemoryStream(WriteOf(reopened)), false);
        Assert.Empty(new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package));
    }

    [Fact]
    public void Plain_text_stays_plain_text_in_its_encoding()
    {
        var latin1 = Encoding.Latin1.GetBytes("Größe\r\nzweite Zeile");
        var document = new WordDocument("Dokument1");

        ReadInto(document, latin1);

        Assert.Equal(WordFileFormat.PlainText, document.LoadedFormat);
        Assert.Equal("Größe\nzweite Zeile", document.Content.GetPlainText());
        Assert.Equal(latin1, WriteOf(document));

        document.SaveFormat = WordFileFormat.Docx;
        Assert.Equal((byte)'P', WriteOf(document)[0]);
        Assert.Equal(WordFileFormat.Docx, WordDocument.FormatOf(".DOCX"));
        Assert.Null(WordDocument.FormatOf(".odt"));
    }
}

public class ClipboardFormatTests
{
    [Fact]
    public void The_rich_format_carries_formatting_styles_and_lists_between_processes()
    {
        var source = Session("Eins", "Zwei", "Drei");
        source.SelectAll();
        source.ToggleList(ListKind.Numbered);
        source.Select(At(0, 0), At(0, 4));
        source.ToggleBold();
        source.SelectAll();

        var data = ClipboardFormats.Export(source.Copy()!);
        var fragment = ClipboardFormats.Import(data)!;

        Assert.Equal("Eins\r\nZwei\r\nDrei", Encoding.UTF8.GetString(data[ClipboardFormats.PlainText]));
        Assert.Null(fragment.SourceDocumentId);
        Assert.Equal(3, fragment.Blocks.Count);
        Assert.True(((Paragraph)fragment.Blocks[0]).Inlines[0].Properties.Bold);

        var target = Session("");
        target.Paste(fragment);
        Assert.Equal("Eins|Zwei|Drei", Texts(target.Document));
        Assert.Equal("1.,2.,3.", string.Join(",", ListNumbering.Compute(target.Document, target.Resolver).Select(label => label?.Text)));
    }

    [Fact]
    public void Plain_text_and_registered_importers_are_used_when_there_is_no_rich_fragment()
    {
        var plain = ClipboardFormats.Import(new Dictionary<string, byte[]> { [ClipboardFormats.PlainText] = Encoding.UTF8.GetBytes("a\r\nb") })!;
        Assert.Equal(2, plain.Blocks.Count);

        ClipboardFormats.Register(new FakeHtmlImporter());
        var html = ClipboardFormats.Import(new Dictionary<string, byte[]>
        {
            [ClipboardFormats.Html] = Encoding.UTF8.GetBytes("<b>x</b>"),
            [ClipboardFormats.PlainText] = Encoding.UTF8.GetBytes("x"),
        })!;
        Assert.Equal("HTML", ((Paragraph)html.Blocks[0]).Text);

        Assert.Null(ClipboardFormats.Import(new Dictionary<string, byte[]>()));
        Assert.Null(ClipboardFormats.TryDeserialize([1, 2, 3]));
    }

    private sealed class FakeHtmlImporter : IClipboardImporter
    {
        public string Format => ClipboardFormats.Html;

        public DocumentFragment? Import(byte[] data) => DocumentFragment.FromPlainText("HTML");
    }
}

public class PdfExportFromLayoutTests
{
    [Fact]
    public void A_laid_out_document_exports_one_pdf_page_per_page()
    {
        var blocks = Enumerable.Range(0, 120).Select(index => (Block)new Paragraph($"Absatz {index} – {Words(30, index)}")).ToList();
        blocks.Insert(5, new Paragraph("Neue Seite", new ParagraphProperties { PageBreakBefore = true }));
        var document = new Document(blocks);
        using var measurer = new SkiaTextMeasurer();
        using var layout = new DocumentLayout(document, measurer);

        using var pdf = new MemoryStream();
        PdfExport.Export(layout.GetDisplayLists(), pdf, new PdfMetadata(Title: "Test"), measurer);

        var text = Encoding.Latin1.GetString(pdf.ToArray());
        Assert.StartsWith("%PDF", text);
        Assert.Contains($"/Count {layout.PageCount}", text);
        Assert.True(layout.PageCount >= 5);
    }

    [Fact]
    public void Display_lists_of_the_fixed_measurer_are_renderer_neutral()
    {
        using var layout = NewLayout(Create("Hallo Welt"));

        var run = Assert.Single(layout.GetDisplayList(0).Items.OfType<ThixOffice.Text.Drawing.GlyphRunItem>());

        Assert.Equal("Hallo Welt", run.Text);
        Assert.Empty(run.Glyphs);
        Assert.Equal(10, run.CharacterX.Length);
        Assert.Equal(5 * 5.5 + 2.75, run.CharacterX[6], 3);
        _ = new FixedWidthTextMeasurer();
    }
}
