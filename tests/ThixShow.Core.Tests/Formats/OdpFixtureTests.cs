using System.IO.Compression;
using System.Text;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formats.Odp;
using ThixShow.Core.Model;

namespace ThixShow.Core.Tests.Formats;

/// <summary>
/// A presentation written by hand the way LibreOffice Impress writes one (master page with title/outline placeholders and
/// presentation styles, automatic styles, a custom shape with equations, a picture with a clip, a table, notes, an effect), read by
/// ThixShow. No LibreOffice runs here: the fixture follows its file layout, interop with real LibreOffice output is not verified.
/// </summary>
public sealed class OdpFixtureTests
{
    private const string Namespaces =
        "xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\" xmlns:style=\"urn:oasis:names:tc:opendocument:xmlns:style:1.0\" " +
        "xmlns:text=\"urn:oasis:names:tc:opendocument:xmlns:text:1.0\" xmlns:table=\"urn:oasis:names:tc:opendocument:xmlns:table:1.0\" " +
        "xmlns:draw=\"urn:oasis:names:tc:opendocument:xmlns:drawing:1.0\" xmlns:fo=\"urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0\" " +
        "xmlns:xlink=\"http://www.w3.org/1999/xlink\" xmlns:svg=\"urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0\" " +
        "xmlns:presentation=\"urn:oasis:names:tc:opendocument:xmlns:presentation:1.0\" xmlns:smil=\"urn:oasis:names:tc:opendocument:xmlns:smil-compatible:1.0\" " +
        "xmlns:anim=\"urn:oasis:names:tc:opendocument:xmlns:animation:1.0\" xmlns:dr3d=\"urn:oasis:names:tc:opendocument:xmlns:dr3d:1.0\" " +
        "xmlns:loext=\"urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0\" xmlns:xml=\"http://www.w3.org/XML/1998/namespace\"";

    private const string Styles = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-styles {Namespaces} office:version="1.3">
          <office:font-face-decls>
            <style:font-face style:name="Liberation Sans" svg:font-family="'Liberation Sans'" style:font-family-generic="roman"/>
          </office:font-face-decls>
          <office:styles>
            <draw:gradient draw:name="Gradient_20_1" draw:display-name="Gradient 1" draw:style="linear" draw:start-color="#ff0000" draw:end-color="#0000ff" draw:start-intensity="100%" draw:end-intensity="100%" draw:angle="900" draw:border="0%"/>
            <draw:stroke-dash draw:name="Fine_20_Dashed" draw:display-name="Fine Dashed" draw:style="rect" draw:dots1="1" draw:dots1-length="400%" draw:distance="300%"/>
            <draw:marker draw:name="Arrow" svg:viewBox="0 0 20 30" svg:d="M10 0l-10 30h20z"/>
            <style:default-style style:family="graphic">
              <style:graphic-properties svg:stroke-color="#3465a4" draw:fill-color="#729fcf"/>
              <style:paragraph-properties fo:text-align="start"/>
              <style:text-properties style:font-name="Liberation Sans" fo:font-size="18pt"/>
            </style:default-style>
            <style:style style:name="standard" style:family="graphic">
              <style:graphic-properties draw:stroke="solid" svg:stroke-width="0cm" svg:stroke-color="#3465a4" draw:fill="solid" draw:fill-color="#729fcf"/>
            </style:style>
            <style:style style:name="objectwithoutfill" style:family="graphic" style:parent-style-name="standard">
              <style:graphic-properties draw:fill="none"/>
            </style:style>
            <style:style style:name="Default-title" style:family="presentation">
              <style:graphic-properties draw:stroke="none" draw:fill="none" draw:textarea-vertical-align="middle"/>
              <style:paragraph-properties fo:text-align="center"/>
              <style:text-properties fo:font-family="'Liberation Sans'" fo:font-size="44pt" fo:color="#1f1f1f"/>
            </style:style>
            <style:style style:name="Default-subtitle" style:family="presentation">
              <style:graphic-properties draw:stroke="none" draw:fill="none"/>
              <style:paragraph-properties fo:text-align="center"/>
              <style:text-properties fo:font-size="32pt"/>
            </style:style>
            <style:style style:name="Default-outline1" style:family="presentation">
              <style:graphic-properties draw:stroke="none" draw:fill="none">
                <text:list-style style:name="Default-outline1">
                  <text:list-level-style-bullet text:level="1" text:bullet-char="●"><style:list-level-properties text:min-label-width="0.6cm"/><style:text-properties fo:font-family="OpenSymbol" fo:font-size="45%"/></text:list-level-style-bullet>
                  <text:list-level-style-bullet text:level="2" text:bullet-char="–"><style:list-level-properties text:space-before="0.6cm" text:min-label-width="0.6cm"/></text:list-level-style-bullet>
                </text:list-style>
              </style:graphic-properties>
              <style:paragraph-properties fo:margin-top="0.5cm" fo:margin-bottom="0cm"/>
              <style:text-properties fo:font-family="'Liberation Sans'" fo:font-size="32pt"/>
            </style:style>
            <style:style style:name="Default-outline2" style:family="presentation" style:parent-style-name="Default-outline1">
              <style:paragraph-properties fo:margin-top="0.4cm"/>
              <style:text-properties fo:font-size="28pt"/>
            </style:style>
          </office:styles>
          <office:automatic-styles>
            <style:page-layout style:name="PM1">
              <style:page-layout-properties fo:margin-top="0cm" fo:margin-bottom="0cm" fo:margin-left="0cm" fo:margin-right="0cm" fo:page-width="28cm" fo:page-height="15.75cm" style:print-orientation="landscape"/>
            </style:page-layout>
            <style:style style:name="Mdp1" style:family="drawing-page">
              <style:drawing-page-properties draw:background-size="full" draw:fill="solid" draw:fill-color="#fafafa"/>
            </style:style>
            <style:style style:name="Mpr1" style:family="graphic">
              <style:graphic-properties draw:stroke="none" draw:fill="none"/>
              <style:paragraph-properties fo:text-align="end"/>
              <style:text-properties fo:font-size="14pt"/>
            </style:style>
          </office:automatic-styles>
          <office:master-styles>
            <style:master-page style:name="Default" style:page-layout-name="PM1" draw:style-name="Mdp1">
              <draw:frame presentation:style-name="Default-title" draw:layer="backgroundobjects" svg:width="25.199cm" svg:height="2.629cm" svg:x="1.4cm" svg:y="0.628cm" presentation:class="title" presentation:placeholder="true"><draw:text-box/></draw:frame>
              <draw:frame presentation:style-name="Default-outline1" draw:layer="backgroundobjects" svg:width="25.199cm" svg:height="9.134cm" svg:x="1.4cm" svg:y="3.685cm" presentation:class="outline" presentation:placeholder="true"><draw:text-box/></draw:frame>
              <draw:frame draw:style-name="Mpr1" draw:layer="backgroundobjects" svg:width="6.523cm" svg:height="1.085cm" svg:x="20.076cm" svg:y="14.348cm" presentation:class="page-number"><draw:text-box><text:p><text:page-number>&lt;Nummer&gt;</text:page-number></text:p></draw:text-box></draw:frame>
              <draw:rect draw:style-name="Mpr1" draw:layer="backgroundobjects" svg:width="28cm" svg:height="0.3cm" svg:x="0cm" svg:y="15.45cm"/>
            </style:master-page>
          </office:master-styles>
        </office:document-styles>
        """;

    private const string Content = $"""
        <?xml version="1.0" encoding="UTF-8"?>
        <office:document-content {Namespaces} office:version="1.3">
          <office:automatic-styles>
            <style:style style:name="dp1" style:family="drawing-page">
              <style:drawing-page-properties presentation:background-visible="true" presentation:background-objects-visible="true" presentation:display-footer="true" presentation:display-page-number="true" presentation:display-date-time="false" smil:type="fade" smil:subtype="crossfade" presentation:transition-speed="slow"/>
            </style:style>
            <style:style style:name="gr1" style:family="graphic" style:parent-style-name="standard">
              <style:graphic-properties draw:fill="gradient" draw:fill-gradient-name="Gradient_20_1" draw:stroke="dash" draw:stroke-dash="Fine_20_Dashed" svg:stroke-width="0.1cm" svg:stroke-color="#000000" draw:marker-end="Arrow" draw:marker-end-width="0.5cm" draw:textarea-vertical-align="middle"/>
            </style:style>
            <style:style style:name="gr2" style:family="graphic" style:parent-style-name="objectwithoutfill">
              <style:graphic-properties draw:stroke="none" draw:auto-grow-height="true" fo:padding-left="0.25cm"/>
            </style:style>
            <style:style style:name="gr3" style:family="graphic">
              <style:graphic-properties draw:stroke="none" draw:fill="none" fo:clip="rect(0cm, 0cm, 0cm, 0.846667cm)" style:mirror="horizontal"/>
            </style:style>
            <style:style style:name="pr1" style:family="presentation" style:parent-style-name="Default-title"><style:graphic-properties fo:min-height="2.629cm"/></style:style>
            <style:style style:name="pr2" style:family="presentation" style:parent-style-name="Default-outline1"><style:graphic-properties fo:min-height="9.134cm"/></style:style>
            <style:style style:name="P1" style:family="paragraph"><style:paragraph-properties fo:text-align="center"/></style:style>
            <style:style style:name="T1" style:family="text"><style:text-properties fo:font-weight="bold" fo:color="#00a000"/></style:style>
            <style:style style:name="co1" style:family="table-column"><style:table-column-properties style:column-width="5cm"/></style:style>
            <style:style style:name="ro1" style:family="table-row"><style:table-row-properties style:row-height="1cm"/></style:style>
            <style:style style:name="ce1" style:family="table-cell">
              <style:graphic-properties draw:fill="solid" draw:fill-color="#eeeeee"/>
              <style:paragraph-properties fo:border="0.03cm solid #000000"/>
              <style:text-properties fo:font-size="12pt"/>
            </style:style>
            <text:list-style style:name="L1"><text:list-level-style-number text:level="1" style:num-format="1" style:num-suffix="."/></text:list-style>
          </office:automatic-styles>
          <office:body>
            <office:presentation>
              <draw:page draw:name="page1" draw:style-name="dp1" draw:master-page-name="Default" presentation:presentation-page-layout-name="AL1T1">
                <draw:frame presentation:style-name="pr1" draw:layer="layout" svg:width="25.199cm" svg:height="2.629cm" svg:x="1.4cm" svg:y="0.628cm" presentation:class="title">
                  <draw:text-box><text:p>Quartal <text:span text:style-name="T1">drei</text:span></text:p></draw:text-box>
                </draw:frame>
                <draw:frame presentation:style-name="pr2" draw:layer="layout" svg:width="25.199cm" svg:height="9.134cm" svg:x="1.4cm" svg:y="3.685cm" presentation:class="outline">
                  <draw:text-box>
                    <text:list><text:list-item><text:p>Erster Punkt</text:p><text:list><text:list-item><text:p>Unterpunkt</text:p></text:list-item></text:list></text:list-item>
                    <text:list-item><text:p>Zweiter   Punkt</text:p></text:list-item></text:list>
                  </draw:text-box>
                </draw:frame>
                <draw:custom-shape draw:style-name="gr1" draw:name="Abgerundet" svg:width="6cm" svg:height="3cm" svg:x="2cm" svg:y="10cm" xml:id="id1" draw:id="id1">
                  <text:p text:style-name="P1">Im Kasten</text:p>
                  <draw:enhanced-geometry svg:viewBox="0 0 21600 21600" draw:text-areas="?f3 ?f4 ?f5 ?f6" draw:type="round-rectangle" draw:modifiers="3600" draw:enhanced-path="M ?f7 0 X 0 ?f8 L 0 ?f9 Y ?f7 21600 L ?f10 21600 X 21600 ?f9 L 21600 ?f8 Y ?f10 0 Z N">
                    <draw:equation draw:name="f0" draw:formula="45"/>
                    <draw:equation draw:name="f1" draw:formula="$0 *sin(?f0 *(pi/180))"/>
                    <draw:equation draw:name="f2" draw:formula="?f1 *3163/7636"/>
                    <draw:equation draw:name="f3" draw:formula="left+?f2 "/>
                    <draw:equation draw:name="f4" draw:formula="top+?f2 "/>
                    <draw:equation draw:name="f5" draw:formula="right-?f2 "/>
                    <draw:equation draw:name="f6" draw:formula="bottom-?f2 "/>
                    <draw:equation draw:name="f7" draw:formula="left+$0 "/>
                    <draw:equation draw:name="f8" draw:formula="top+$0 "/>
                    <draw:equation draw:name="f9" draw:formula="bottom-$0 "/>
                    <draw:equation draw:name="f10" draw:formula="right-$0 "/>
                    <draw:handle draw:handle-position="$0 top" draw:handle-switched="true" draw:handle-range-x-minimum="0" draw:handle-range-x-maximum="10800"/>
                  </draw:enhanced-geometry>
                </draw:custom-shape>
                <draw:frame draw:style-name="gr3" draw:name="Foto" svg:width="4cm" svg:height="2.5cm" svg:x="9cm" svg:y="10cm">
                  <draw:image xlink:href="Pictures/foto.png" xlink:type="simple" xlink:show="embed" xlink:actuate="onLoad"><text:p/></draw:image>
                  <svg:desc>Ein Foto</svg:desc>
                </draw:frame>
                <draw:frame draw:style-name="gr2" draw:name="Notiz" svg:width="6cm" svg:height="1cm" svg:x="14cm" svg:y="10cm">
                  <draw:text-box><text:list text:style-name="L1"><text:list-item><text:p>Eins</text:p></text:list-item><text:list-item><text:p>Zwei</text:p></text:list-item></text:list></draw:text-box>
                </draw:frame>
                <draw:frame draw:name="Tabelle" svg:width="10cm" svg:height="2cm" svg:x="14cm" svg:y="12cm">
                  <table:table table:use-first-row-styles="true">
                    <table:table-column table:style-name="co1" table:number-columns-repeated="2"/>
                    <table:table-row table:style-name="ro1"><table:table-cell table:style-name="ce1"><text:p>A</text:p></table:table-cell><table:table-cell table:style-name="ce1"><text:p>B</text:p></table:table-cell></table:table-row>
                    <table:table-row table:style-name="ro1"><table:table-cell table:number-columns-spanned="2"><text:p>Beide</text:p></table:table-cell><table:covered-table-cell/></table:table-row>
                  </table:table>
                </draw:frame>
                <draw:line draw:style-name="gr1" svg:x1="1cm" svg:y1="14cm" svg:x2="5cm" svg:y2="13cm"/>
                <draw:frame draw:name="Diagramm" svg:width="5cm" svg:height="4cm" svg:x="20cm" svg:y="4cm"><draw:object xlink:href="./Object 1" draw:notify-on-update-of-ranges="Tabelle1.A1:Tabelle1.B3"/></draw:frame>
                <draw:measure svg:x1="0cm" svg:y1="0cm" svg:x2="1cm" svg:y2="0cm"/>
                <dr3d:scene svg:width="2cm" svg:height="2cm" svg:x="0cm" svg:y="0cm"/>
                <draw:unknown-thing svg:x="0cm"/>
                <unknown:element xmlns:unknown="urn:example:unknown"><unknown:child/></unknown:element>
                <anim:par presentation:node-type="timing-root">
                  <anim:seq presentation:node-type="main-sequence">
                    <anim:par smil:begin="next">
                      <anim:par smil:begin="0s">
                        <anim:par smil:begin="0s" smil:fill="hold" presentation:node-type="on-click" presentation:preset-class="entrance" presentation:preset-id="ooo-entrance-fade-in">
                          <anim:set smil:begin="0s" smil:dur="0.001s" smil:fill="hold" smil:targetElement="id1" smil:attributeName="visibility" smil:to="visible"/>
                          <anim:transitionFilter smil:dur="1s" smil:targetElement="id1" smil:type="fade" smil:subtype="crossfade"/>
                        </anim:par>
                      </anim:par>
                    </anim:par>
                    <anim:par smil:begin="next">
                      <anim:par smil:begin="0s">
                        <anim:par smil:begin="0s" smil:fill="hold" presentation:node-type="on-click" presentation:preset-class="entrance" presentation:preset-id="ooo-entrance-fly-in" presentation:preset-sub-type="from-left">
                          <anim:set smil:begin="0s" smil:dur="0.001s" smil:fill="hold" smil:targetElement="id1" smil:attributeName="visibility" smil:to="visible"/>
                          <anim:animate smil:dur="0.5s" smil:fill="hold" smil:targetElement="id1" smil:attributeName="x" smil:values="0-width/2;x" smil:keyTimes="0;1" presentation:additive="base"/>
                          <anim:animate smil:dur="0.5s" smil:fill="hold" smil:targetElement="id1" smil:attributeName="y" smil:values="y;y" smil:keyTimes="0;1" presentation:additive="base"/>
                        </anim:par>
                      </anim:par>
                    </anim:par>
                    <anim:par smil:begin="next">
                      <anim:par smil:begin="0s">
                        <anim:par smil:begin="0s" smil:fill="hold" presentation:node-type="on-click" presentation:preset-class="entrance" presentation:preset-id="ooo-entrance-wipe" presentation:preset-sub-type="from-left">
                          <anim:set smil:begin="0s" smil:dur="0.001s" smil:fill="hold" smil:targetElement="id1" smil:attributeName="visibility" smil:to="visible"/>
                          <anim:transitionFilter smil:dur="0.5s" smil:targetElement="id1" smil:type="barWipe" smil:subtype="leftToRight"/>
                        </anim:par>
                      </anim:par>
                    </anim:par>
                  </anim:seq>
                </anim:par>
                <presentation:notes>
                  <draw:page-thumbnail svg:width="14cm" svg:height="8cm" svg:x="3cm" svg:y="2cm" draw:page-number="1" presentation:class="page"/>
                  <draw:frame svg:width="16cm" svg:height="10cm" svg:x="2cm" svg:y="12cm" presentation:class="notes"><draw:text-box><text:p>Hier spricht der Vortragende.</text:p></draw:text-box></draw:frame>
                </presentation:notes>
              </draw:page>
              <draw:page draw:name="Zweite" draw:master-page-name="Default">
                <draw:frame svg:width="25cm" svg:height="2cm" svg:x="1.4cm" svg:y="2cm" presentation:class="title" presentation:placeholder="true"><draw:text-box/></draw:frame>
                <draw:frame svg:width="25cm" svg:height="3cm" svg:x="1.4cm" svg:y="5cm" presentation:class="subtitle"><draw:text-box><text:p>Untertitel</text:p></draw:text-box></draw:frame>
                <draw:frame svg:width="8cm" svg:height="2cm" svg:x="1cm" svg:y="9cm"><draw:text-box><text:p><text:a xlink:href="#page1" xlink:type="simple">Zurück</text:a> zum Anfang</text:p></draw:text-box></draw:frame>
              </draw:page>
            </office:presentation>
          </office:body>
        </office:document-content>
        """;

    private static byte[] Fixture()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            void Add(string path, byte[] bytes, CompressionLevel level = CompressionLevel.Optimal)
            {
                using var stream = zip.CreateEntry(path, level).Open();
                stream.Write(bytes);
            }

            Add("mimetype", Encoding.ASCII.GetBytes(OdpWriter.MimeType), CompressionLevel.NoCompression);
            Add("content.xml", Encoding.UTF8.GetBytes(Content));
            Add("styles.xml", Encoding.UTF8.GetBytes(Styles));
            Add("Pictures/foto.png", TestImages.Png(160, 100));
            Add("Object 1/content.xml", Encoding.UTF8.GetBytes("<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"><office:body><office:chart/></office:body></office:document-content>"));
        }

        return buffer.ToArray();
    }

    [Fact]
    public void A_presentation_in_LibreOffice_style_reads_with_its_master_shapes_and_text()
    {
        var bytes = Fixture();
        Assert.True(OdpReader.IsOdp(new MemoryStream(bytes)));
        var presentation = OdpReader.Read(new MemoryStream(bytes));

        // 28 × 15.75 cm, one master with its title/outline placeholders, the outline's bullets and sizes, the background.
        Assert.Equal(new SlideSize(10080000, 5670000), presentation.SlideSize);
        var master = Assert.Single(presentation.Masters);
        Assert.Equal("Default", master.Name);
        var title = master.TextStyles.Title.LevelOf(0)!;
        Assert.Equal(TextAlignment.Center, title.Alignment);
        Assert.Equal(4400, title.DefaultRun!.Size);
        Assert.Equal("Liberation Sans", title.DefaultRun.Latin!.Typeface);
        Assert.Equal(new CharacterBullet("●"), master.TextStyles.Body.LevelOf(0)!.Bullet);
        Assert.Equal(new CharacterBullet("–"), master.TextStyles.Body.LevelOf(1)!.Bullet);
        Assert.Equal(2800, master.TextStyles.Body.LevelOf(1)!.DefaultRun!.Size); // outline2 inherits outline1's font, sets its size
        Assert.Equal("Liberation Sans", master.TextStyles.Body.LevelOf(1)!.DefaultRun!.Latin!.Typeface);
        Assert.Equal(new SolidFill(ColorSpec.Rgb("FAFAFA")), master.Background!.Fill);
        Assert.Contains(master.Shapes, shape => shape is AutoShape { Placeholder.Type: PlaceholderType.SlideNumber });
        Assert.Contains(master.Shapes, shape => shape is AutoShape { Placeholder: null, Geometry.Preset: "rect" });
        Assert.Equal(TextAlignment.Center, master.Layouts.First(layout => layout.Type == "title").Shapes.OfType<AutoShape>()
            .Single(shape => shape.Placeholder?.Type == PlaceholderType.Subtitle).Text!.ListStyle.LevelOf(0)!.Alignment);

        var slide = presentation.Slides[0];
        Assert.Equal("obj", presentation.LayoutOf(slide).Type);
        Assert.Equal(new SlideTransition(TransitionKind.Fade, Speed: "slow"), slide.Transition);
        var shapes = slide.Shapes;

        var heading = shapes.OfType<AutoShape>().Single(shape => shape.Placeholder?.Type == PlaceholderType.Title);
        Assert.Null(heading.Transform); // where the master has it
        Assert.Equal("Quartal drei", heading.Text!.PlainText);
        var bold = heading.Text.Paragraphs[0].Runs[1];
        Assert.True(bold.Properties.Bold);
        Assert.Equal(new SolidFill(ColorSpec.Rgb("00A000")), bold.Properties.Fill);

        var outline = shapes.OfType<AutoShape>().Single(shape => shape.Placeholder?.Type == PlaceholderType.Object);
        Assert.Equal(["Erster Punkt", "Unterpunkt", "Zweiter Punkt"], outline.Text!.Paragraphs.Select(paragraph => paragraph.Text));
        Assert.Equal([0, 1, 0], outline.Text.Paragraphs.Select(paragraph => paragraph.Level));
        Assert.All(outline.Text.Paragraphs, paragraph => Assert.Null(paragraph.Properties.Bullet)); // the master's

        var rounded = shapes.OfType<AutoShape>().Single(shape => shape.Name == "Abgerundet");
        Assert.True(rounded.Geometry!.IsCustom);
        var path = GeometryEngine.Evaluate(rounded.Geometry, 170, 85).Paths.Single();
        Assert.Contains(path.Path.Verbs, verb => verb == ThixOffice.Text.Drawing.PathVerb.CubicTo); // the quarter ellipses
        Assert.IsType<GradientFill>(rounded.Fill);
        var gradient = (GradientFill)rounded.Fill!;
        Assert.Equal(0, gradient.LinearAngle); // ODF 90° (left to right) is DrawingML's 0°
        Assert.Equal(ColorSpec.Rgb("FF0000"), gradient.Stops[0].Color);
        Assert.Equal(new LineDash("dash"), rounded.Outline!.Dash);
        Assert.Equal(LineEndKind.Triangle, rounded.Outline.Tail!.Kind);
        Assert.Equal(TextAnchor.Center, rounded.Text!.Body.Anchor);
        Assert.Equal(TextAlignment.Center, rounded.Text.Paragraphs[0].Properties.Alignment);
        Assert.Equal("Liberation Sans", rounded.Text.ListStyle.Default!.DefaultRun!.Latin!.Typeface); // from the default style

        var picture = shapes.OfType<PictureShape>().Single();
        Assert.Equal(new RelativeRect(20000, 0, 0, 0), picture.Picture.Crop); // 0.8467 cm of 4.233 cm (160 px)
        Assert.True(picture.Transform!.FlipHorizontal);
        Assert.Equal("Ein Foto", picture.Description);

        var note = shapes.OfType<AutoShape>().Single(shape => shape.Name == "Notiz");
        Assert.True(note.IsTextBox);
        Assert.Equal(NoFill.Instance, note.Fill);
        Assert.Equal(ShapeAutoFit.Instance, note.Text!.Body.AutoFit);
        Assert.Equal(new AutoNumberBullet("arabicPeriod"), note.Text.Paragraphs[0].Properties.Bullet);

        var table = shapes.OfType<GraphicFrame>().Single().Table!;
        Assert.Equal([1800000, 1800000], table.ColumnWidths);
        Assert.Equal("A", table.CellAt(0, 0).Text.PlainText);
        Assert.Equal(new SolidFill(ColorSpec.Rgb("EEEEEE")), table.CellAt(0, 0).Properties.Fill);
        Assert.Equal(10800, table.CellAt(0, 0).Properties.TopBorder!.Width);
        Assert.Equal(2, table.CellAt(1, 0).GridSpan);
        Assert.True(table.CellAt(1, 1).HorizontalMerge);
        Assert.True(table.Properties.FirstRow);

        var line = shapes.OfType<ConnectorShape>().Single();
        Assert.Equal(new Transform2D(360000, 4680000, 1440000, 360000, FlipVertical: true), line.Transform);

        // What was not shown is named, and the effect ThixShow plays is there.
        var kinds = presentation.UnsupportedContent.ToDictionary(content => content.Kind, content => content.Count);
        Assert.Equal(1, kinds["chart"]);
        Assert.Equal(1, kinds["3d"]);
        Assert.Equal(1, kinds["element"]); // the measure
        Assert.Equal(1, kinds["animation"]); // the wipe
        Assert.Equal(2, slide.Timing!.MainSequence.Count);
        var effect = slide.Timing.MainSequence[0];
        Assert.Equal(AnimationPreset.Fade, AnimationPresets.PresetOf(effect));
        Assert.Equal(rounded.Id, effect.ShapeId);
        Assert.Equal(1000, effect.DurationMs);
        var fly = slide.Timing.MainSequence[1];
        Assert.Equal(AnimationPreset.FlyIn, AnimationPresets.PresetOf(fly));
        Assert.Equal(AnimationDirection.Left, AnimationPresets.DirectionOf(fly.PresetSubtype));
        Assert.Equal(500, fly.DurationMs);
        Assert.Equal(AnimationTrigger.OnClick, fly.Trigger);
        Assert.Equal("Hier spricht der Vortragende.", slide.NotesText);
        Assert.Contains(shapes, shape => shape.Placeholder?.Type == PlaceholderType.SlideNumber);

        // The second slide: an empty title and a subtitle make a title slide; a link to the first slide.
        var second = presentation.Slides[1];
        Assert.Equal("title", presentation.LayoutOf(second).Type);
        Assert.Equal("Zweite", second.Name);
        var empty = second.Shapes.OfType<AutoShape>().Single(shape => shape.Placeholder?.Type == PlaceholderType.CenteredTitle);
        Assert.True(empty.Text!.IsEmpty);
        var link = second.Shapes.OfType<AutoShape>().Single(shape => shape.IsTextBox).Text!.Paragraphs[0].Runs[0];
        Assert.Equal(Hyperlink.ToSlide(presentation.Slides[0].Id), link.Properties.Click);
        Assert.Equal("Zurück zum Anfang", second.Shapes.OfType<AutoShape>().Single(shape => shape.IsTextBox).Text!.PlainText);

        // What was read writes and reads again unchanged.
        var again = OdpTests.RoundTrip(presentation);
        PptxTestSupport.AssertSameModel(again, OdpTests.RoundTrip(again));
    }

    [Fact]
    public void Every_preset_geometry_comes_back_as_its_preset()
    {
        var shapes = PresetGeometries.Names.Select((preset, index) => (SlideElement)new AutoShape
        {
            Id = index + 2,
            Name = preset,
            Transform = new Transform2D(91440 * (index % 40), 91440 * (index / 40), 914400, 685800),
            Geometry = ShapeGeometry.Of(preset),
            Fill = new SolidFill(ColorSpec.Rgb("1F6FB2")),
        }).ToArray();
        var read = OdpTests.RoundTrip(TestDecks.Blank(shapes));
        Assert.Equal(shapes.Select(shape => ((AutoShape)shape).Geometry), read.Slides[0].Shapes.Select(shape => ((AutoShape)shape).Geometry));
    }
}
