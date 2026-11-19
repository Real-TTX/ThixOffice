using ThixWrite.Core.Editing;
using ThixWrite.Core.Formats.Html;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>Pasting HTML of other programs: formatting, lists, tables, links and pictures kept, everything active dropped.</summary>
public class HtmlImportTests
{
    private static List<Paragraph> Paragraphs(DocumentFragment fragment) => fragment.Blocks.OfType<Paragraph>().Where(paragraph => paragraph.Length > 0 || paragraph.Cell is not null).ToList();

    [Fact]
    public void Headings_paragraphs_and_inline_formatting_come_over()
    {
        var fragment = HtmlImport.Import(
            "<h1>Titel</h1><p>Ein <b>fetter</b>, <i>kursiver</i> und <span style=\"color:#c00000;font-size:16pt;text-decoration:underline\">roter</span>\n   Text.</p>"
            + "<p style='text-align:center'>Mitte<br>zweite Zeile</p>")!;

        var paragraphs = Paragraphs(fragment);
        Assert.Equal(["Titel", "Ein fetter, kursiver und roter Text.", "Mitte\vzweite Zeile"], paragraphs.Select(paragraph => paragraph.Text));
        Assert.Equal(BuiltInStyles.Heading1, paragraphs[0].Properties.StyleId);
        Assert.Equal(ParagraphAlignment.Center, paragraphs[2].Properties.Alignment);
        var runs = paragraphs[1].Inlines.OfType<TextRun>().ToList();
        Assert.True(runs.Single(run => run.Text == "fetter").Properties.Bold);
        Assert.True(runs.Single(run => run.Text == "kursiver").Properties.Italic);
        var red = runs.Single(run => run.Text == "roter").Properties;
        Assert.Equal(("C00000", 16.0, UnderlineStyle.Single), (red.Color!.Value, red.FontSize, red.Underline));
        Assert.NotNull(fragment.Styles!.Find(BuiltInStyles.Heading1));
    }

    [Fact]
    public void Lists_tables_links_and_embedded_pictures_come_over_and_nothing_active_does()
    {
        var png = Convert.ToBase64String(Images.ImageTests.Png(40, 20));
        var html = "<script>alert('x')</script><style>p{color:red}</style><!-- Kommentar -->"
                   + "<ul><li>Eins</li><li>Zwei<ol><li>Unterpunkt</li></ol></li></ul>"
                   + "<table><tr><th>Name</th><th>Wert</th></tr><tr><td>A</td><td>1</td></tr></table>"
                   + "<p><a href=\"https://www.thix.de\">Webseite</a> und <a href=\"javascript:alert(1)\">böse</a></p>"
                   + $"<p><img src=\"data:image/png;base64,{png}\" alt=\"Logo\"> <img src=\"https://tracker.example/pixel.png\"></p>"
                   + "<iframe src=\"https://example.com\">Rahmen</iframe><button onclick=\"x()\">Knopf</button>";

        var fragment = HtmlImport.Import(html)!;
        var blocks = fragment.Blocks.OfType<Paragraph>().ToList();
        Assert.DoesNotContain(blocks, paragraph => paragraph.Text.Contains("alert", StringComparison.Ordinal) || paragraph.Text.Contains("color", StringComparison.Ordinal)
                                                   || paragraph.Text.Contains("Rahmen", StringComparison.Ordinal) || paragraph.Text.Contains("Knopf", StringComparison.Ordinal));

        var list = blocks.Where(paragraph => paragraph.Properties.Numbering is not null).ToList();
        Assert.Equal(["Eins", "Zwei", "Unterpunkt"], list.Select(paragraph => paragraph.Text));
        Assert.Equal(1, list[2].Properties.Numbering!.Level);
        Assert.NotNull(fragment.Numbering!.Instance(list[0].Properties.Numbering!.NumberingId));

        var cells = blocks.Where(paragraph => paragraph.Cell is not null).ToList();
        Assert.Equal(["Name", "Wert", "A", "1"], cells.Select(paragraph => paragraph.Text));
        Assert.True(cells[0].Inlines.OfType<TextRun>().Single().Properties.Bold);

        var links = blocks.SelectMany(paragraph => paragraph.Inlines).Where(inline => inline.Link is not null).ToList();
        Assert.Equal("https://www.thix.de", Assert.Single(links).Link!.Uri);
        var picture = Assert.Single(blocks.SelectMany(paragraph => paragraph.Inlines).OfType<ImageInline>());
        Assert.Equal(("Logo", 40), (picture.Description, picture.Image.PixelWidth));
    }

    [Fact]
    public void The_Windows_clipboard_format_and_its_fragment_markers_are_read()
    {
        const string html = "<html><body><!--StartFragment--><p>Nur <b>das</b></p><!--EndFragment--></body></html>";
        var header = "Version:0.9\r\nStartHTML:{0:D10}\r\nEndHTML:{1:D10}\r\n";
        var length = string.Format(header, 0, 0).Length;
        var data = string.Format(header, length, length + System.Text.Encoding.UTF8.GetByteCount(html)) + html;

        var fragment = HtmlImport.Import(HtmlImport.FromClipboardFormat(data))!;
        Assert.Equal("Nur das", Assert.Single(Paragraphs(fragment)).Text);
    }

    [Fact]
    public void A_pasted_fragment_brings_its_styles_lists_and_tables_into_the_document()
    {
        var (session, _) = Editor(Create("Vorher", "Nachher"));
        session.SetCaret(At(0, 6));
        session.Paste(HtmlImport.Import("<h2>Kopf</h2><ol><li>a</li><li>b</li></ol><table><tr><td>x</td><td>y</td></tr></table>")!);

        AssertInvariants(session);
        Assert.Single(TableSpan.All(session.Document));
        Assert.NotNull(session.Document.Styles.Find(BuiltInStyles.Heading2));
        Assert.Contains(session.Document.Blocks.OfType<Paragraph>(), paragraph => paragraph.Text == "b" && session.Resolver.ResolveParagraph(paragraph).Numbering is not null);
    }
}
