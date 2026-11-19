using System.IO.Compression;
using System.Text;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.Formats.DocxTestSupport;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>Real-world files: missing parts, other writers' markup, invalid values, files that are no docx.</summary>
public class DocxToleranceTests
{
    private const string W = DocxFixtures.WordNamespace;

    [Fact]
    public void A_file_without_styles_numbering_and_settings_is_read_and_written_valid()
    {
        var document = Read(DocxFixtures.CreateWithoutStyles());

        Assert.Equal("Nur Text", document.Blocks[0].ToString());
        Assert.Empty(document.Styles.Styles);
        Assert.True(document.Numbering.IsEmpty);
        Assert.Equal(SectionProperties.Default, document.FinalSection);
        Assert.Equal(Theme.Office, document.Theme);

        var again = RoundTrip(document);
        AssertSameContent(document, again);
    }

    [Fact]
    public void LibreOffice_markup_is_read()
    {
        const string styles =
            "<w:styles xmlns:w=\"" + W + "\"><w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:ascii=\"Liberation Serif\" w:eastAsia=\"Noto Serif CJK SC\" w:cs=\"Lohit Devanagari\" w:hAnsi=\"Liberation Serif\"/>"
            + "<w:kern w:val=\"2\"/><w:sz w:val=\"24\"/><w:szCs w:val=\"24\"/><w:lang w:val=\"de-DE\" w:eastAsia=\"zh-CN\" w:bidi=\"hi-IN\"/></w:rPr></w:rPrDefault>"
            + "<w:pPrDefault><w:pPr><w:widowControl/><w:suppressAutoHyphens w:val=\"true\"/></w:pPr></w:pPrDefault></w:docDefaults>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"Normal\"><w:name w:val=\"Normal\"/><w:qFormat/><w:pPr><w:widowControl/><w:bidi w:val=\"0\"/><w:spacing w:before=\"0\" w:after=\"0\"/><w:jc w:val=\"start\"/></w:pPr><w:rPr/></w:style>"
            + "<w:style w:type=\"paragraph\" w:styleId=\"TextBody\"><w:name w:val=\"Body Text\"/><w:basedOn w:val=\"Normal\"/><w:pPr><w:spacing w:before=\"0\" w:after=\"140\" w:line=\"276\" w:lineRule=\"auto\"/></w:pPr><w:rPr/></w:style>"
            + "</w:styles>";
        const string document =
            "<w:document xmlns:w=\"" + W + "\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"><w:body>"
            + "<w:p><w:pPr><w:pStyle w:val=\"TextBody\"/><w:numPr><w:ilvl w:val=\"0\"/><w:numId w:val=\"1\"/></w:numPr><w:bidi w:val=\"0\"/><w:jc w:val=\"start\"/><w:rPr></w:rPr></w:pPr>"
            + "<w:r><w:rPr><w:b/><w:bCs/></w:rPr><w:t xml:space=\"preserve\">Fett </w:t></w:r><w:r><w:rPr></w:rPr><w:t>und normal</w:t></w:r></w:p>"
            + "<w:sectPr><w:type w:val=\"nextPage\"/><w:pgSz w:w=\"11906\" w:h=\"16838\"/><w:pgMar w:left=\"1134\" w:right=\"1134\" w:header=\"0\" w:top=\"1134\" w:footer=\"0\" w:bottom=\"1134\" w:gutter=\"0\"/>"
            + "<w:pgNumType w:fmt=\"decimal\"/><w:formProt w:val=\"false\"/><w:textDirection w:val=\"lrTb\"/><w:docGrid w:type=\"default\" w:linePitch=\"100\" w:charSpace=\"0\"/></w:sectPr>"
            + "</w:body></w:document>";
        const string numbering =
            "<w:numbering xmlns:w=\"" + W + "\"><w:abstractNum w:abstractNumId=\"1\"><w:multiLevelType w:val=\"hybridMultilevel\"/><w:lvl w:ilvl=\"0\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/>"
            + "<w:lvlText w:val=\"•\"/><w:lvlJc w:val=\"left\"/><w:pPr><w:tabs><w:tab w:val=\"num\" w:pos=\"720\"/></w:tabs><w:ind w:left=\"720\" w:hanging=\"360\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"OpenSymbol\" w:hAnsi=\"OpenSymbol\" w:cs=\"OpenSymbol\" w:hint=\"default\"/></w:rPr></w:lvl></w:abstractNum>"
            + "<w:num w:numId=\"1\"><w:abstractNumId w:val=\"1\"/></w:num></w:numbering>";

        var read = Read(DocxFixtures.CreateRaw(document, styles, numberingXml: numbering));

        Assert.Equal("Liberation Serif", read.Styles.CharacterDefaults.FontFamily);
        Assert.Equal(12, read.Styles.CharacterDefaults.FontSize);
        Assert.Equal(276 / 240.0, read.Styles.Find("TextBody")!.Paragraph.LineSpacing!.Value);
        Assert.Equal(ParagraphAlignment.Left, read.Styles.Find("Normal")!.Paragraph.Alignment);
        var paragraph = Assert.IsType<Paragraph>(read.Blocks[0]);
        Assert.Equal("Fett und normal", paragraph.Text);
        Assert.Equal(new NumberingReference(1, 0), paragraph.Properties.Numbering);
        Assert.Equal("•", read.Numbering.ResolveLevel(1, 0)!.Text);
        Assert.Equal(56.7, read.FinalSection.MarginLeft);
        Assert.Equal(0, read.FinalSection.HeaderDistance);
        Assert.Contains("docGrid", read.FinalSection.UnknownXml);

        var again = RoundTrip(read);
        AssertSameContent(read, again);
    }

    [Fact]
    public void Invalid_values_are_ignored_not_fatal()
    {
        const string document =
            "<w:document xmlns:w=\"" + W + "\"><w:body>"
            + "<w:p><w:pPr><w:pStyle w:val=\"GibtEsNicht\"/><w:jc w:val=\"irgendwie\"/><w:ind w:left=\"12.5pt\" w:firstLine=\"abc\"/><w:spacing w:after=\"-\" w:line=\"x\"/>"
            + "<w:numPr><w:ilvl w:val=\"12\"/><w:numId w:val=\"99\"/></w:numPr><w:outlineLvl w:val=\"77\"/></w:pPr>"
            + "<w:r><w:rPr><w:sz w:val=\"abc\"/><w:b w:val=\"vielleicht\"/><w:i w:val=\"0\"/><w:u w:val=\"seltsam\"/><w:highlight w:val=\"pink\"/><w:vertAlign w:val=\"oben\"/></w:rPr><w:t>Text</w:t></w:r>"
            + "<w:r><w:sym w:font=\"Wingdings\" w:char=\"F04A\"/><w:sym w:char=\"zz\"/></w:r></w:p>"
            + "<w:sectPr><w:pgSz w:w=\"breit\" w:h=\"16838\"/><w:cols w:num=\"2\" w:space=\"720\" w:sep=\"1\"/><w:type w:val=\"seltsam\"/></w:sectPr>"
            + "</w:body></w:document>";

        var read = Read(DocxFixtures.CreateRaw(document));

        var paragraph = Assert.IsType<Paragraph>(read.Blocks[0]);
        Assert.Equal("GibtEsNicht", paragraph.Properties.StyleId);
        Assert.Null(paragraph.Properties.Alignment);
        Assert.Equal(new NumberingReference(99, 8), paragraph.Properties.Numbering);
        Assert.Equal(9, paragraph.Properties.OutlineLevel);
        var run = Assert.IsType<TextRun>(paragraph.Inlines[0]);
        Assert.Null(run.Properties.FontSize);
        Assert.True(run.Properties.Bold);
        Assert.False(run.Properties.Italic);
        Assert.Equal(UnderlineStyle.Single, run.Properties.Underline);
        Assert.Null(run.Properties.Highlight);
        var symbol = Assert.IsType<TextRun>(paragraph.Inlines[1]);
        Assert.Equal("", symbol.Text);
        Assert.Equal("Wingdings", symbol.Properties.FontFamily);
        Assert.Equal(SectionProperties.Default.PageWidth, read.FinalSection.PageWidth);
        Assert.Equal(1, read.FinalSection.ColumnCount);
        Assert.Contains("w:sep", read.FinalSection.UnknownXml);
        Assert.Equal(SectionStart.NextPage, read.FinalSection.Start);

        var written = Write(read);
        AssertValid(written);
        Assert.Contains("w:sep=\"1\"", DocumentXml(written));
    }

    [Fact]
    public void Body_level_bookmarks_move_into_the_next_paragraph()
    {
        const string document =
            "<w:document xmlns:w=\"" + W + "\"><w:body><w:bookmarkStart w:id=\"3\" w:name=\"Oben\"/><w:p><w:r><w:t>Absatz</w:t></w:r></w:p>"
            + "<w:bookmarkEnd w:id=\"3\"/><w:proofErr w:type=\"spellStart\"/></w:body></w:document>";

        var read = Read(DocxFixtures.CreateRaw(document));

        Assert.Equal(2, read.Blocks.Count);
        Assert.IsType<OpaqueInline>(((Paragraph)read.Blocks[0]).Inlines[0]);
        Assert.IsType<OpaqueInline>(((Paragraph)read.Blocks[1]).Inlines.Single());
        Assert.Empty(read.UnsupportedContent);
        AssertValid(Write(read));
    }

    [Fact]
    public void Markup_compatibility_content_keeps_its_namespaces()
    {
        const string document =
            "<w:document xmlns:w=\"" + W + "\" xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" "
            + "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" mc:Ignorable=\"w14\"><w:body>"
            + "<w:p w14:paraId=\"1A2B3C4D\"><w:r><mc:AlternateContent><mc:Choice Requires=\"w14\"><w:t>neu</w:t></mc:Choice><mc:Fallback><w:t>alt</w:t></mc:Fallback></mc:AlternateContent></w:r></w:p>"
            + "</w:body></w:document>";

        var read = Read(DocxFixtures.CreateRaw(document));
        var opaque = Assert.IsType<OpaqueInline>(((Paragraph)read.Blocks[0]).Inlines.Single());
        Assert.True(opaque.Xml.Contains("xmlns:w14", StringComparison.Ordinal), opaque.Xml);

        var again = Read(Write(read));
        AssertSameContent(read, again);
        var fresh = new Document(read.Blocks, read.Styles, read.Numbering, read.FinalSection);
        AssertSameContent(fresh, Read(Write(fresh)));
    }

    [Fact]
    public void A_stream_that_is_no_docx_is_refused_with_a_clear_message()
    {
        var text = new MemoryStream(Encoding.UTF8.GetBytes("Hallo, ich bin Text."));
        var exception = Assert.Throws<InvalidDataException>(() => DocxReader.Read(text));
        Assert.Equal(DocxReader.NotADocxMessage, exception.Message);

        using var zip = new MemoryStream();
        using (var archive = new ZipArchive(zip, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = new StreamWriter(archive.CreateEntry("hallo.txt").Open());
            entry.Write("kein Word");
        }

        zip.Position = 0;
        Assert.Throws<InvalidDataException>(() => DocxReader.Read(zip));
    }
}
