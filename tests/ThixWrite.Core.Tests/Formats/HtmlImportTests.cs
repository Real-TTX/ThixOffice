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
    private static DocumentFragment Mail(string html, double width = 450) => HtmlImport.Import(html, HtmlImportOptions.Mail(null, width))!;

    private static IEnumerable<Inline> AllInlines(IEnumerable<Block> blocks) =>
        blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).SelectMany(inline => inline is OpaqueInline { Nested: { } nested } ? AllInlines(nested.Blocks) : [inline]);

    [Fact]
    public void A_style_sheet_gives_elements_their_looks_by_tag_class_id_and_descendant_but_no_media_rule()
    {
        var fragment = Mail("<html><head><style><!-- p.MsoNormal {margin:0cm} .lead { color:#C00000 } #total b { color:#00B050 } td .note { font-style:italic }"
            + "@media (max-width:480px) { .lead { color:#0000FF !important } .phone { display:block !important } } .phone { display:none } a:hover { color:#FF0000 } --></style></head>"
            + "<body><p class=MsoNormal>Erster</p><p class=\"lead x\">Rot</p><div id=total><b>Grün</b></div><div class=phone>Nur Telefon</div><table><tr><td><span class=note>schräg</span></td></tr></table></body></html>");

        var paragraphs = fragment.Blocks.OfType<Paragraph>().Where(paragraph => paragraph.Length > 0).ToList();
        Assert.DoesNotContain(paragraphs, paragraph => paragraph.Text.Contains("Nur Telefon", StringComparison.Ordinal));
        Assert.Equal(0, paragraphs[0].Properties.SpacingAfter); // Outlook's MsoNormal from its sheet, not HTML's paragraph margin
        Assert.Equal("C00000", paragraphs[1].Inlines.OfType<TextRun>().Single().Properties.Color!.Value);
        Assert.Equal("00B050", paragraphs[2].Inlines.OfType<TextRun>().Single().Properties.Color!.Value);
        Assert.True(paragraphs.Single(paragraph => paragraph.Text == "schräg").Inlines.OfType<TextRun>().Single().Properties.Italic);
    }

    [Fact]
    public void A_newsletter_keeps_its_columns_side_by_side_inside_its_frame()
    {
        var fragment = Mail("<table width=100% bgcolor=#EEEEEE><tr><td align=center><table width=600 bgcolor=#FFFFFF>"
            + "<tr><td bgcolor=#1F3B57 style='padding:20px;color:#FFFFFF'>Kopf</td></tr>"
            + "<tr><td><table width=100%><tr><td width=50%>Jacke<br><a href='https://shop.example/jacke'>Ansehen</a></td><td width=50%>Schal</td></tr></table></td></tr>"
            + "</table></td></tr></table>", 450);

        var frame = Assert.Single(TableSpan.All(new Document(fragment.Blocks))).Table;
        Assert.Equal(2, frame.RowCount); // the outer wrapper is gone, the frame stays a table
        Assert.Equal(TableAlignment.Center, frame.Properties.Alignment); // centred by the wrapper's align=center
        Assert.Equal("1F3B57", frame.Rows[0].Cells[0].Shading!.Fill); // the colour belongs to the cell: no gaps between its paragraphs
        Assert.Equal(15, frame.Rows[0].Cells[0].Margins!.Top); // padding:20px
        var carrier = fragment.Blocks.OfType<Paragraph>().Single(paragraph => paragraph.Inlines is [OpaqueInline { Nested: not null }]);
        var columns = Assert.Single(TableSpan.All(new Document(((OpaqueInline)carrier.Inlines[0]).Nested!.Blocks))).Table;
        Assert.Equal(2, columns.Rows[0].CellCount); // side by side, not below each other
        Assert.Equal(columns.Grid[0], columns.Grid[1], 3);
        Assert.Contains(AllInlines(fragment.Blocks), inline => inline.Link?.Uri == "https://shop.example/jacke");
    }

    [Fact]
    public void A_signature_table_is_as_wide_as_its_content_and_given_widths_count()
    {
        var auto = Mail("<table border=0 cellpadding=0><tr><td><img width=120 height=40 src='cid:x'>Logo</td><td>Maria Schulz<br>Tel. 030 1234</td></tr></table>");
        var autoTable = Assert.Single(TableSpan.All(new Document(auto.Blocks))).Table;
        Assert.True(autoTable.Grid.Sum() < 200, $"the signature takes {autoTable.Grid.Sum()} pt");

        var given = Mail("<table cellpadding=0><tr><td style='width:72px'>Bild</td><td style='width:260px'>Text</td></tr></table>");
        var givenTable = Assert.Single(TableSpan.All(new Document(given.Blocks))).Table;
        Assert.Equal([54, 195], givenTable.Grid.Select(width => Math.Round(width)));
    }

    [Fact]
    public void A_last_line_break_and_Words_o_p_add_no_blank_lines()
    {
        var fragment = Mail("<div>Prima.<br><br>Anna</div><br><div>Am Montag schrieb Jonas:<br></div><p class=MsoNormal>Hallo<o:p></o:p></p><p class=MsoNormal><o:p>&nbsp;</o:p></p><p class=MsoNormal>Text<o:p></o:p></p>");

        var texts = fragment.Blocks.OfType<Paragraph>().Select(paragraph => paragraph.Text).ToList();
        Assert.Equal(["Prima.\v\vAnna", "", "Am Montag schrieb Jonas:", "Hallo", "\u00A0", "Text"], texts);
        Assert.All(fragment.Blocks.OfType<Paragraph>(), paragraph => Assert.True((paragraph.Properties.SpacingAfter ?? 0) == 0));
    }

    [Fact]
    public void A_link_inside_a_column_of_a_mail_is_found_where_it_is_drawn()
    {
        var fragment = Mail("<table width=400><tr><td bgcolor=#1F3B57>Kopf</td></tr><tr><td><table width=100%><tr><td width=50%>Links</td><td width=50%><a href='https://shop.example/rechts'>Rechts</a></td></tr></table></td></tr></table>", 400);
        var document = new Document(fragment.Blocks, fragment.Styles, fragment.Numbering, SectionProperties.Default with { PageWidth = 400, MarginLeft = 0, MarginRight = 0 });
        var layout = NewLayout(document);
        layout.Update();

        var found = Enumerable.Range(0, 100).SelectMany(y => Enumerable.Range(0, 40).Select(x => layout.NestedLinkAt(0, x * 10, SectionProperties.Default.MarginTop + y)))
            .FirstOrDefault(link => link is not null);
        Assert.Equal("https://shop.example/rechts", found?.Uri);
        Assert.Null(layout.NestedLinkAt(0, 20, layout.Pages[0].Lines[2].Top + 10)); // on the left column
    }
}
