using System.IO.Compression;
using System.Text;

namespace ThixWrite.Core.Tests.Formats;

/// <summary>
/// OpenDocument files written by hand the way LibreOffice writes them (namespaces at the root, automatic styles P1/T1, the
/// "Heading_20_1" style with its display name, a bullet list style, a table with a span and a fill, a footnote, an annotation,
/// a custom shape ThixWrite does not show, styles.xml with the page layout and a footer with the page number).
/// </summary>
internal static class OdtFixtures
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" " +
        "xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" " +
        "xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" " +
        "xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:xlink=\"http://www.w3.org/1999/xlink\" " +
        "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
        "office:version=\"1.3\"";

    private const string Content = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content {Namespaces}>
          <office:automatic-styles>
            <style:style style:name="P1" style:family="paragraph" style:parent-style-name="Standard">
              <style:paragraph-properties fo:text-align="center"/>
            </style:style>
            <style:style style:name="T1" style:family="text">
              <style:text-properties fo:font-weight="bold"/>
            </style:style>
            <style:style style:name="Tabelle1" style:family="table">
              <style:table-properties style:width="17cm" table:align="margins"/>
            </style:style>
            <style:style style:name="Tabelle1.A" style:family="table-column">
              <style:table-column-properties style:column-width="8.5cm"/>
            </style:style>
            <style:style style:name="Tabelle1.A1" style:family="table-cell">
              <style:table-cell-properties fo:background-color="#d9e2f3" fo:padding="0.097cm" fo:border="0.5pt solid #000000"/>
            </style:style>
            <text:list-style style:name="L1">
              <text:list-level-style-bullet text:level="1" text:bullet-char="•">
                <style:list-level-properties text:list-level-position-and-space-mode="label-alignment">
                  <style:list-level-label-alignment text:label-followed-by="listtab" fo:text-indent="-0.635cm" fo:margin-left="1.27cm"/>
                </style:list-level-properties>
              </text:list-level-style-bullet>
            </text:list-style>
          </office:automatic-styles>
          <office:body>
            <office:text>
              <text:sequence-decls/>
              <text:h text:style-name="Heading_20_1" text:outline-level="1">Überschrift</text:h>
              <text:p text:style-name="P1">Ein <text:span text:style-name="T1">fetter</text:span> Satz mit <text:s/>zwei Leerzeichen,<text:tab/>Tab und<text:line-break/>Umbruch.</text:p>
              <text:list text:style-name="L1">
                <text:list-item><text:p text:style-name="Standard">Apfel</text:p></text:list-item>
                <text:list-item><text:p text:style-name="Standard">Birne</text:p></text:list-item>
              </text:list>
              <table:table table:name="Tabelle1" table:style-name="Tabelle1">
                <table:table-column table:style-name="Tabelle1.A" table:number-columns-repeated="2"/>
                <table:table-row>
                  <table:table-cell table:style-name="Tabelle1.A1" table:number-columns-spanned="2" office:value-type="string"><text:p>A1</text:p></table:table-cell>
                  <table:covered-table-cell/>
                </table:table-row>
                <table:table-row>
                  <table:table-cell office:value-type="string"><text:p>A2</text:p></table:table-cell>
                  <table:table-cell office:value-type="string"><text:p>B2</text:p></table:table-cell>
                </table:table-row>
              </table:table>
              <text:p text:style-name="Standard">Mit Fußnote<text:note text:id="ftn1" text:note-class="footnote"><text:note-citation>1</text:note-citation><text:note-body><text:p text:style-name="Footnote">Fußnotentext.</text:p></text:note-body></text:note> und <office:annotation office:name="__Annotation__1"><dc:creator>Anna</dc:creator><dc:date>2026-11-19T10:00:00</dc:date><text:p>Anmerkung</text:p></office:annotation>Kommentar<office:annotation-end office:name="__Annotation__1"/>.</text:p>
              <text:p text:style-name="Standard"><draw:custom-shape draw:name="Form 1" svg:width="2cm" svg:height="1cm"/></text:p>
            </office:text>
          </office:body>
        </office:document-content>
        """;

    private const string Styles = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-styles {Namespaces}>
          <office:styles>
            <style:default-style style:family="paragraph">
              <style:paragraph-properties fo:margin-bottom="0.247cm"/>
              <style:text-properties style:font-name="Liberation Serif" fo:font-size="12pt" fo:language="de" fo:country="DE"/>
            </style:default-style>
            <style:style style:name="Standard" style:family="paragraph" style:class="text"/>
            <style:style style:name="Heading" style:family="paragraph" style:parent-style-name="Standard" style:next-style-name="Text_20_body">
              <style:text-properties fo:font-size="14pt"/>
            </style:style>
            <style:style style:name="Heading_20_1" style:display-name="Heading 1" style:family="paragraph" style:parent-style-name="Heading" style:default-outline-level="1">
              <style:text-properties fo:font-size="130%" fo:font-weight="bold"/>
            </style:style>
            <style:style style:name="Text_20_body" style:display-name="Text body" style:family="paragraph" style:parent-style-name="Standard">
              <style:paragraph-properties fo:margin-top="0cm" fo:margin-bottom="0.247cm" fo:line-height="115%"/>
            </style:style>
            <style:style style:name="Footnote" style:family="paragraph" style:parent-style-name="Standard">
              <style:text-properties fo:font-size="10pt"/>
            </style:style>
          </office:styles>
          <office:automatic-styles>
            <style:page-layout style:name="pm1">
              <style:page-layout-properties fo:page-width="21.001cm" fo:page-height="29.7cm" style:print-orientation="portrait" fo:margin-top="2cm" fo:margin-bottom="1cm" fo:margin-left="2cm" fo:margin-right="2cm"/>
              <style:header-style/>
              <style:footer-style>
                <style:header-footer-properties fo:min-height="0cm" fo:margin-top="0.5cm"/>
              </style:footer-style>
            </style:page-layout>
          </office:automatic-styles>
          <office:master-styles>
            <style:master-page style:name="Standard" style:page-layout-name="pm1">
              <style:footer><text:p text:style-name="Standard">Seite <text:page-number text:select-page="current">1</text:page-number></text:p></style:footer>
            </style:master-page>
          </office:master-styles>
        </office:document-styles>
        """;

    private const string Manifest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">
          <manifest:file-entry manifest:full-path="/" manifest:version="1.3" manifest:media-type="application/vnd.oasis.opendocument.text"/>
          <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml"/>
          <manifest:file-entry manifest:full-path="styles.xml" manifest:media-type="text/xml"/>
        </manifest:manifest>
        """;

    /// <summary>The package as bytes.</summary>
    public static byte[] LibreOffice()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "mimetype", "application/vnd.oasis.opendocument.text", CompressionLevel.NoCompression);
            Add(zip, "content.xml", Content.Trim());
            Add(zip, "styles.xml", Styles.Trim());
            Add(zip, "META-INF/manifest.xml", Manifest.Trim());
        }

        return buffer.ToArray();
    }

    private static void Add(ZipArchive zip, string path, string text, CompressionLevel level = CompressionLevel.Optimal)
    {
        using var stream = zip.CreateEntry(path, level).Open();
        stream.Write(Encoding.UTF8.GetBytes(text));
    }
}
