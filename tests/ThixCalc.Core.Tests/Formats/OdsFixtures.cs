using System.IO.Compression;
using System.Text;
using ThixCalc.Core.Formats.Ods;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Tests.Formats;

/// <summary>
/// Hand-written OpenDocument spreadsheets in the way LibreOffice writes them (no LibreOffice runs here, so its real output is not
/// checked): repeated rows and cells, covered cells, automatic styles with data styles, a set of formulas, settings, page styles.
/// </summary>
public class OdsFixtures
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:number=\"urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0\" " +
        "xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" xmlns:of=\"urn:oasis:names:tc:opendocument:xmlns:of:1.2\" " +
        "xmlns:config=\"urn:oasis:names:tc:opendocument:xmlns:config:1.0\" xmlns:tableooo=\"http://openoffice.org/2009/table\" " +
        "xmlns:calcext=\"urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0\" xmlns:loext=\"urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0\" " +
        "xmlns:foo=\"urn:example:unknown\"";

    internal static byte[] Package(string content, string? styles = null, string? settings = null, IEnumerable<(string Path, byte[] Bytes)>? extra = null)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, string text, CompressionLevel level = CompressionLevel.Optimal)
            {
                using var stream = zip.CreateEntry(path, level).Open();
                stream.Write(Encoding.UTF8.GetBytes(text));
            }

            Add("mimetype", OdsWriter.MimeType, CompressionLevel.NoCompression);
            Add("content.xml", content);
            if (styles is not null)
            {
                Add("styles.xml", styles);
            }

            if (settings is not null)
            {
                Add("settings.xml", settings);
            }

            foreach (var (path, bytes) in extra ?? [])
            {
                using var stream = zip.CreateEntry(path).Open();
                stream.Write(bytes);
            }

            Add("META-INF/manifest.xml", "<manifest:manifest xmlns:manifest=\"urn:oasis:names:tc:opendocument:xmlns:manifest:1.0\"><manifest:file-entry manifest:full-path=\"/\" manifest:media-type=\"application/vnd.oasis.opendocument.spreadsheet\"/></manifest:manifest>");
        }

        return buffer.ToArray();
    }

    private static readonly string Styles = $"""
        <office:document-styles {Namespaces} office:version="1.3">
          <office:font-face-decls><style:font-face style:name="Liberation Sans" svg:font-family="'Liberation Sans'"/></office:font-face-decls>
          <office:styles>
            <style:default-style style:family="table-cell">
              <style:paragraph-properties style:tab-stop-distance="1.25cm"/>
              <style:text-properties style:font-name="Liberation Sans" fo:font-size="10pt" fo:language="de" fo:country="DE"/>
            </style:default-style>
            <number:number-style style:name="N0"><number:number number:min-integer-digits="1"/></number:number-style>
            <style:style style:name="Default" style:family="table-cell"/>
            <style:style style:name="Good" style:family="table-cell" style:parent-style-name="Default">
              <style:table-cell-properties fo:background-color="#ccffcc"/>
              <style:text-properties fo:color="#006600"/>
            </style:style>
          </office:styles>
          <office:automatic-styles>
            <style:page-layout style:name="Mpm1">
              <style:page-layout-properties fo:page-width="29.7cm" fo:page-height="21cm" style:print-orientation="landscape" fo:margin-top="2cm" fo:margin-bottom="2cm" fo:margin-left="2cm" fo:margin-right="2cm" style:print="grid headers charts drawings objects zero-values" style:scale-to="75%" style:table-centering="none" style:first-page-number="continue" style:print-page-order="ttb"/>
              <style:header-style><style:header-footer-properties fo:min-height="0.75cm" fo:margin-left="0cm" fo:margin-right="0cm" fo:margin-bottom="0.25cm"/></style:header-style>
              <style:footer-style/>
            </style:page-layout>
          </office:automatic-styles>
          <office:master-styles>
            <style:master-page style:name="Default" style:page-layout-name="Mpm1">
              <style:header><style:region-left><text:p><text:sheet-name>???</text:sheet-name></text:p></style:region-left><style:region-right><text:p>Seite <text:page-number>1</text:page-number> / <text:page-count>99</text:page-count></text:p></style:region-right></style:header>
              <style:header-left style:display="false"/>
              <style:footer style:display="false"/>
            </style:master-page>
          </office:master-styles>
        </office:document-styles>
        """;

    private static readonly string Content = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content {Namespaces} office:version="1.3">
          <office:scripts/>
          <office:font-face-decls><style:font-face style:name="Liberation Sans" svg:font-family="'Liberation Sans'"/></office:font-face-decls>
          <office:automatic-styles>
            <style:style style:name="co1" style:family="table-column"><style:table-column-properties fo:break-before="auto" style:column-width="2.258cm"/></style:style>
            <style:style style:name="co2" style:family="table-column"><style:table-column-properties fo:break-before="auto" style:column-width="4.5cm"/></style:style>
            <style:style style:name="ro1" style:family="table-row"><style:table-row-properties style:row-height="0.452cm" fo:break-before="auto" style:use-optimal-row-height="true"/></style:style>
            <style:style style:name="ro2" style:family="table-row"><style:table-row-properties style:row-height="1cm" fo:break-before="auto" style:use-optimal-row-height="false"/></style:style>
            <style:style style:name="ta1" style:family="table" style:master-page-name="Default"><style:table-properties table:display="true" style:writing-mode="lr-tb" tableooo:tab-color="#2a6099"/></style:style>
            <style:style style:name="ta2" style:family="table" style:master-page-name="Default"><style:table-properties table:display="false" style:writing-mode="lr-tb"/></style:style>
            <number:currency-style style:name="N104P0" style:volatile="true"><number:number number:decimal-places="2" number:min-decimal-places="2" number:min-integer-digits="1" number:grouping="true"/><number:text> </number:text><number:currency-symbol number:language="de" number:country="DE">€</number:currency-symbol></number:currency-style>
            <number:currency-style style:name="N104"><style:text-properties fo:color="#ff0000"/><number:text>-</number:text><number:number number:decimal-places="2" number:min-decimal-places="2" number:min-integer-digits="1" number:grouping="true"/><number:text> </number:text><number:currency-symbol number:language="de" number:country="DE">€</number:currency-symbol><style:map style:condition="value()&gt;=0" style:apply-style-name="N104P0"/></number:currency-style>
            <number:date-style style:name="N37" number:automatic-order="true"><number:day number:style="long"/><number:text>.</number:text><number:month number:style="long"/><number:text>.</number:text><number:year number:style="long"/></number:date-style>
            <number:percentage-style style:name="N11"><number:number number:decimal-places="2" number:min-decimal-places="2" number:min-integer-digits="1"/><number:text>%</number:text></number:percentage-style>
            <style:style style:name="ce1" style:family="table-cell" style:parent-style-name="Default"><style:table-cell-properties fo:background-color="#dddddd" fo:border="0.06pt solid #000000" style:vertical-align="middle" fo:wrap-option="wrap"/><style:paragraph-properties fo:text-align="center"/><style:text-properties fo:font-weight="bold" style:font-weight-asian="bold"/></style:style>
            <style:style style:name="ce2" style:family="table-cell" style:parent-style-name="Default" style:data-style-name="N104"/>
            <style:style style:name="ce3" style:family="table-cell" style:parent-style-name="Default" style:data-style-name="N37"/>
            <style:style style:name="ce4" style:family="table-cell" style:parent-style-name="Default" style:data-style-name="N11"><style:table-cell-properties style:rotation-angle="90"/></style:style>
            <style:style style:name="ce5" style:family="table-cell" style:parent-style-name="Good"/>
          </office:automatic-styles>
          <office:body>
            <office:spreadsheet>
              <table:calculation-settings table:automatic-find-labels="false"/>
              <table:content-validations>
                <table:content-validation table:name="val1" table:condition="of:cell-content-is-in-list(&quot;Ja&quot;;&quot;Nein&quot;)" table:allow-empty-cell="true" table:display-list="unsorted" table:base-cell-address="Umsatz.E2">
                  <table:help-message table:display="false"><text:p/></table:help-message>
                  <table:error-message table:message-type="stop" table:display="true" table:title="Ungültig"><text:p>Bitte Ja oder Nein</text:p></table:error-message>
                </table:content-validation>
              </table:content-validations>
              <table:table table:name="Umsatz" table:style-name="ta1" table:print-ranges="Umsatz.A1:Umsatz.E10">
                <foo:unknown foo:attribute="x"><foo:child/></foo:unknown>
                <table:table-column table:style-name="co2" table:default-cell-style-name="Default"/>
                <table:table-column table:style-name="co1" table:number-columns-repeated="3" table:default-cell-style-name="Default"/>
                <table:table-column table:style-name="co1" table:visibility="collapse" table:default-cell-style-name="ce2"/>
                <table:table-column table:style-name="co1" table:number-columns-repeated="1019" table:default-cell-style-name="Default"/>
                <table:table-header-rows>
                  <table:table-row table:style-name="ro2">
                    <table:table-cell table:style-name="ce1" office:value-type="string" calcext:value-type="string"><text:p>Monat</text:p></table:table-cell>
                    <table:table-cell table:style-name="ce1" table:number-columns-spanned="2" table:number-rows-spanned="1" office:value-type="string" calcext:value-type="string"><text:p>Umsatz <text:span text:style-name="T1">netto</text:span></text:p></table:table-cell>
                    <table:covered-table-cell table:style-name="ce1"/>
                    <table:table-cell table:style-name="ce1" office:value-type="string" calcext:value-type="string"><text:p>Anteil</text:p></table:table-cell>
                    <table:table-cell table:number-columns-repeated="1020"/>
                  </table:table-row>
                </table:table-header-rows>
                <table:table-row table:style-name="ro1">
                  <table:table-cell table:style-name="ce3" office:value-type="date" office:date-value="2024-01-31" calcext:value-type="date"><text:p>31.01.2024</text:p></table:table-cell>
                  <table:table-cell table:style-name="ce2" office:value-type="currency" office:currency="EUR" office:value="1250.5" calcext:value-type="currency"><text:p>1.250,50 €</text:p></table:table-cell>
                  <table:table-cell table:style-name="ce2" office:value-type="currency" office:currency="EUR" office:value="-20" calcext:value-type="currency"><text:p>-20,00 €</text:p></table:table-cell>
                  <table:table-cell table:style-name="ce4" table:formula="of:=[.B2]/[.$B$5]" office:value-type="percentage" office:value="0.5" calcext:value-type="percentage"><text:p>50,00%</text:p></table:table-cell>
                  <table:table-cell table:content-validation-name="val1" office:value-type="string" calcext:value-type="string"><office:annotation office:display="false"><dc:creator>Lena</dc:creator><dc:date>2024-02-01T10:00:00</dc:date><text:p>Geprüft</text:p></office:annotation><text:p>Ja</text:p></table:table-cell>
                  <table:table-cell table:number-columns-repeated="1019"/>
                </table:table-row>
                <table:table-row table:style-name="ro1" table:number-rows-repeated="2">
                  <table:table-cell table:style-name="ce3" office:value-type="date" office:date-value="2024-02-29T12:00:00" calcext:value-type="date"><text:p>29.02.2024</text:p></table:table-cell>
                  <table:table-cell table:style-name="ce2" office:value-type="currency" office:currency="EUR" office:value="1250.5" calcext:value-type="currency"><text:p>1.250,50 €</text:p></table:table-cell>
                  <table:table-cell table:number-columns-repeated="2"/>
                  <table:table-cell table:content-validation-name="val1"/>
                  <table:table-cell table:number-columns-repeated="1019"/>
                </table:table-row>
                <table:table-row table:style-name="ro1">
                  <table:table-cell office:value-type="string" calcext:value-type="string"><text:p>Summe</text:p></table:table-cell>
                  <table:table-cell table:style-name="ce2" table:formula="of:=SUM([.B2:.B4])" office:value-type="currency" office:currency="EUR" office:value="3751.5" calcext:value-type="currency"><text:p>3.751,50 €</text:p></table:table-cell>
                  <table:table-cell table:formula="of:=ORG.OPENOFFICE.STYLE(&quot;Good&quot;)" office:value-type="float" office:value="0" calcext:value-type="float"><text:p>0</text:p></table:table-cell>
                  <table:table-cell table:formula="of:=IF([.B5]&gt;1000;&quot;hoch&quot;;&quot;niedrig&quot;)" office:value-type="string" office:string-value="hoch" calcext:value-type="string"><text:p>hoch</text:p></table:table-cell>
                  <table:table-cell table:formula="of:=1/0" office:value-type="string" office:string-value="" calcext:value-type="error"><text:p>#DIV/0!</text:p></table:table-cell>
                  <table:table-cell table:style-name="ce5" table:formula="of:=COM.MICROSOFT.TEXTJOIN(&quot;, &quot;;TRUE();[.A2:.A3])" office:value-type="string" office:string-value="x" calcext:value-type="string"><text:p>x</text:p></table:table-cell>
                  <table:table-cell office:value-type="boolean" office:boolean-value="true" calcext:value-type="boolean"><text:p>WAHR</text:p></table:table-cell>
                  <table:table-cell office:value-type="time" office:time-value="PT36H30M00S" calcext:value-type="time"><text:p>36:30:00</text:p></table:table-cell>
                  <table:table-cell office:value-type="string" calcext:value-type="string"><text:p><text:a xlink:href="https://example.org/" xlink:type="simple">Web</text:a></text:p></table:table-cell>
                  <table:table-cell table:number-columns-repeated="1015"/>
                </table:table-row>
                <table:table-row table:style-name="ro1" table:number-rows-repeated="1048570">
                  <table:table-cell table:number-columns-repeated="1024"/>
                </table:table-row>
                <calcext:conditional-formats>
                  <calcext:conditional-format calcext:target-range-address="Umsatz.B2:Umsatz.B4">
                    <calcext:condition calcext:apply-style-name="Good" calcext:value="&gt;1000" calcext:base-cell-address="Umsatz.B2"/>
                    <calcext:color-scale><calcext:color-scale-entry calcext:value="0" calcext:type="minimum" calcext:color="#ff0000"/></calcext:color-scale>
                  </calcext:conditional-format>
                </calcext:conditional-formats>
              </table:table>
              <table:table table:name="Versteckt" table:style-name="ta2">
                <table:table-column table:style-name="co1" table:number-columns-repeated="1024" table:default-cell-style-name="Default"/>
                <table:table-row table:style-name="ro1"><table:table-cell table:formula="of:=[$Umsatz.B5]*2" office:value-type="float" office:value="7503" calcext:value-type="float"><text:p>7503</text:p></table:table-cell></table:table-row>
                <table:shapes><draw:frame draw:z-index="0"><draw:object xlink:href="./Object 1"/></draw:frame></table:shapes>
              </table:table>
              <table:named-expressions>
                <table:named-range table:name="Umsätze" table:base-cell-address="$Umsatz.$B$2" table:cell-range-address="$Umsatz.$B$2:.$B$4"/>
                <table:named-expression table:name="Doppelt" table:base-cell-address="$Umsatz.$A$1" table:expression="of:=[$Versteckt.$A$1]/2"/>
              </table:named-expressions>
              <table:database-ranges>
                <table:database-range table:name="__Anonymous_Sheet_DB__0" table:target-range-address="Umsatz.A1:Umsatz.E4" table:display-filter-buttons="true">
                  <table:filter><table:filter-condition table:field-number="4" table:value="Ja" table:operator="="><table:filter-set-item table:value="Ja"/></table:filter-condition></table:filter>
                </table:database-range>
              </table:database-ranges>
            </office:spreadsheet>
          </office:body>
        </office:document-content>
        """;

    private static readonly string Settings = $"""
        <office:document-settings {Namespaces} office:version="1.3">
          <office:settings>
            <config:config-item-set config:name="ooo:view-settings">
              <config:config-item-map-indexed config:name="Views">
                <config:config-item-map-entry>
                  <config:config-item config:name="ViewId" config:type="string">view1</config:config-item>
                  <config:config-item-map-named config:name="Tables">
                    <config:config-item-map-entry config:name="Umsatz">
                      <config:config-item config:name="CursorPositionX" config:type="int">1</config:config-item>
                      <config:config-item config:name="CursorPositionY" config:type="int">2</config:config-item>
                      <config:config-item config:name="HorizontalSplitMode" config:type="short">0</config:config-item>
                      <config:config-item config:name="VerticalSplitMode" config:type="short">2</config:config-item>
                      <config:config-item config:name="HorizontalSplitPosition" config:type="int">0</config:config-item>
                      <config:config-item config:name="VerticalSplitPosition" config:type="int">1</config:config-item>
                      <config:config-item config:name="PositionBottom" config:type="int">1</config:config-item>
                      <config:config-item config:name="ZoomValue" config:type="int">130</config:config-item>
                    </config:config-item-map-entry>
                  </config:config-item-map-named>
                  <config:config-item config:name="ActiveTable" config:type="string">Umsatz</config:config-item>
                  <config:config-item config:name="ShowGrid" config:type="boolean">false</config:config-item>
                </config:config-item-map-entry>
              </config:config-item-map-indexed>
            </config:config-item-set>
          </office:settings>
        </office:document-settings>
        """;

    [Fact]
    public void A_LibreOffice_style_spreadsheet_is_read()
    {
        var workbook = OdsReader.Read(new MemoryStream(Package(Content, Styles, Settings, [("Basic/Standard/Module1.xml", [1, 2, 3])])));
        Assert.Equal(["Umsatz", "Versteckt"], workbook.Sheets.Select(sheet => sheet.Name));
        var sheet = workbook["Umsatz"];
        var hidden = workbook["Versteckt"];

        // Values of every type; repeated rows give copies.
        Assert.Equal("Monat", sheet["A1"].Text);
        Assert.Equal("Umsatz netto", sheet["B1"].Text);
        Assert.Equal(new DateTime(2024, 1, 31), ThixCalc.Core.Calculation.DateSerial.ToDateTime(sheet["A2"].Number));
        Assert.Equal(1250.5, sheet["B2"].Number);
        Assert.Equal(-20, sheet["C2"].Number);
        Assert.Equal(new DateTime(2024, 2, 29, 12, 0, 0), ThixCalc.Core.Calculation.DateSerial.ToDateTime(sheet["A3"].Number));
        Assert.Equal(sheet["A3"], sheet["A4"]);
        Assert.Equal(1250.5, sheet["B4"].Number);
        Assert.Equal(3751.5, sheet["B5"].Number);
        Assert.Equal("hoch", sheet["D5"].Text);
        Assert.Equal(CellError.DivisionByZero, sheet["E5"].Error);
        Assert.True(sheet["G5"].Boolean);
        Assert.Equal(1.5208333333333333, sheet["H5"].Number, 9);
        Assert.Equal(0, sheet["C5"].Number); // the formula with LibreOffice's own function keeps its value
        Assert.Null(sheet.GetFormula(4, 2));

        // Formulas.
        Assert.Equal("B2/$B$5", FormulaFormatter.Format(sheet.GetFormula(1, 3)!, workbook, new CellAddress(1, 3)));
        Assert.Equal("SUM(B2:B4)", FormulaFormatter.Format(sheet.GetFormula(4, 1)!, workbook, new CellAddress(4, 1)));
        Assert.Equal("TEXTJOIN(\", \",TRUE,A2:A3)", FormulaFormatter.Format(sheet.GetFormula(4, 5)!, workbook, new CellAddress(4, 5)));
        Assert.Equal("Umsatz!B5*2", FormulaFormatter.Format(hidden.GetFormula(0, 0)!, workbook, default));
        workbook.Calculation.RecalculateAll();
        Assert.Equal(3751.5, sheet["B5"].Number);
        Assert.Equal(7503, hidden["A1"].Number);

        // Styles and data styles.
        var header = sheet.GetStyle(0, 0);
        Assert.True(header.Font.Bold);
        Assert.Equal("Liberation Sans", header.Font.Name);
        Assert.Equal(10, header.Font.Size);
        Assert.Equal(CellFill.Solid(CalcColor.FromHex("DDDDDD")), header.Fill);
        Assert.Equal(BorderStyle.Hair, header.Borders.Left.Style);
        Assert.Equal(HorizontalAlignment.Center, header.Alignment.Horizontal);
        Assert.Equal(VerticalAlignment.Center, header.Alignment.Vertical);
        Assert.True(header.Alignment.WrapText);
        Assert.Equal("#,##0.00 [$€-407];[Red]-#,##0.00 [$€-407]", sheet.GetStyle(1, 1).NumberFormat);
        Assert.Equal("dd.mm.yyyy", sheet.GetStyle(1, 0).NumberFormat);
        Assert.Equal("0.00%", sheet.GetStyle(1, 3).NumberFormat);
        Assert.Equal(90, sheet.GetStyle(1, 3).Alignment.TextRotation);
        Assert.Equal(CellFill.Solid(CalcColor.FromHex("CCFFCC")), sheet.GetStyle(4, 5).Fill);
        Assert.Equal("Liberation Sans", workbook.Styles.Default.Font.Name);

        // Columns, rows, merges, the view.
        Assert.Equal(OdsWriter.ColumnCharacters(4.5 * 72 / 2.54), sheet.Columns[0].Size!.Value, 6);
        Assert.Null(sheet.Columns[1].Size);
        Assert.True(sheet.Columns[4].Hidden);
        Assert.Equal("#,##0.00 [$€-407];[Red]-#,##0.00 [$€-407]", workbook.Styles[sheet.Columns[4].StyleId!.Value].NumberFormat);
        Assert.Equal(Math.Round(0.452 * 72 / 2.54, 6), sheet.Rows.DefaultSize, 4);
        Assert.Equal(1 * 72 / 2.54, sheet.Rows[0].Size!.Value, 4);
        Assert.True(sheet.Rows[0].CustomSize);
        Assert.Equal([CellRange.Parse("B1:C1")], sheet.Merges.Ranges);
        Assert.Equal(1, sheet.View.FrozenRows);
        Assert.Equal(0, sheet.View.FrozenColumns);
        Assert.Equal(130, sheet.View.Zoom);
        Assert.Equal(new CellAddress(2, 1), sheet.View.ActiveCell);
        Assert.False(sheet.View.ShowGridLines);
        Assert.Equal(CalcColor.FromHex("2A6099"), sheet.TabColor);
        Assert.Equal(SheetVisibility.Hidden, hidden.Visibility);
        Assert.True(sheet.CellCount < 50);

        // Validation, note, link, names, print ranges, filter, conditional format.
        var validation = Assert.Single(sheet.Validations);
        Assert.Equal(ValidationType.List, validation.Type);
        Assert.Equal("\"Ja,Nein\"", validation.Formula1);
        Assert.Equal([CellRange.Parse("E2:E4")], validation.Ranges);
        Assert.Equal("Bitte Ja oder Nein", validation.Error);
        Assert.Equal("Ungültig", validation.ErrorTitle);
        Assert.Equal(new CellComment("Lena", "Geprüft"), sheet.Comments[new CellAddress(1, 4)]);
        Assert.Equal("https://example.org/", sheet.Hyperlinks.Single().Target);
        Assert.Contains(workbook.Names, name => name.Name == "Umsätze" && name.Formula == "Umsatz!$B$2:$B$4");
        Assert.Contains(workbook.Names, name => name.Name == "Doppelt" && name.Formula == "Versteckt!$A$1/2");
        Assert.Contains(workbook.Names, name => name.Name == "_xlnm.Print_Area" && name.SheetId == sheet.Id && name.Formula == "Umsatz!$A$1:$E$10");
        Assert.Contains(workbook.Names, name => name.Name == "_xlnm.Print_Titles" && name.SheetId == sheet.Id && name.Formula == "Umsatz!$1:$1");
        Assert.Equal(CellRange.Parse("A1:E4"), sheet.AutoFilter!.Range);
        Assert.Equal(["Ja"], sheet.AutoFilter.Columns.Single().Values);
        var rule = Assert.Single(Assert.Single(sheet.ConditionalFormats).Rules);
        Assert.Equal(ConditionalFormatType.CellIs, rule.Type);
        Assert.Equal(["1000"], rule.Formulas);
        Assert.Equal(CalcColor.FromHex("CCFFCC"), workbook.Styles.DifferentialFormats[rule.FormatId!.Value].FillColor);

        // Page setup from the master page.
        var setup = sheet.PageSetup;
        Assert.True(setup.Landscape);
        Assert.Equal(9, setup.PaperSize);
        Assert.Equal(75, setup.Scale);
        Assert.True(setup.PrintGridLines);
        Assert.True(setup.PrintHeadings);
        Assert.Equal("&L&A&RSeite &P / &N", setup.Header);
        Assert.Null(setup.Footer);
        Assert.Equal(2 / 2.54, setup.HeaderMargin, 5);
        Assert.Equal(3 / 2.54, setup.TopMargin, 5);

        // What was not taken over is named.
        Assert.Contains(workbook.UnsupportedContent, note => note.StartsWith("Formeln, die ThixCalc nicht", StringComparison.Ordinal));
        Assert.Contains(workbook.UnsupportedContent, note => note.StartsWith("Farbskalen", StringComparison.Ordinal));
        Assert.Contains(workbook.UnsupportedContent, note => note.StartsWith("Diagramme", StringComparison.Ordinal));
        Assert.Contains(workbook.UnsupportedContent, note => note.StartsWith("Makros", StringComparison.Ordinal));
        Assert.Contains(workbook.UnsupportedContent, note => note.StartsWith("Teilweise formatierter Text", StringComparison.Ordinal));
    }

    [Fact]
    public void A_minimal_spreadsheet_without_styles_or_settings_is_read()
    {
        var content = $"""
            <office:document-content {Namespaces}><office:body><office:spreadsheet>
            <table:table table:name="Tabelle1"><table:table-row><table:table-cell office:value-type="float" office:value="7"/><table:table-cell table:formula="of:=[.A1]*6"/></table:table-row></table:table>
            </office:spreadsheet></office:body></office:document-content>
            """;
        var workbook = OdsReader.Read(new MemoryStream(Package(content)));
        var sheet = workbook.Sheets[0];
        Assert.Equal(7, sheet["A1"].Number);
        Assert.Equal(42, sheet["B1"].Number); // no cached result: recalculated on load
    }
}
