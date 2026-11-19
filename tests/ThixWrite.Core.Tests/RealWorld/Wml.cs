using System.Globalization;
using System.Security;
using System.Xml.Linq;
using SkiaSharp;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>Small builders for WordprocessingML as Word writes it (rsid attributes left out; everything else in Word's order).</summary>
internal static class Wml
{
    private static int _docPr = 1;
    private static int _paraId = 0x1A2B3C00;

    public static void Reset()
    {
        _docPr = 1;
        _paraId = 0x1A2B3C00;
    }

    private static readonly string[] RunOrder = ["rStyle", "rFonts", "b", "bCs", "i", "iCs", "caps", "smallCaps", "strike", "dstrike", "outline", "shadow", "emboss", "imprint",
        "noProof", "snapToGrid", "vanish", "webHidden", "color", "spacing", "w", "kern", "position", "sz", "szCs", "highlight", "u", "effect", "bdr", "shd", "fitText",
        "vertAlign", "rtl", "cs", "em", "lang", "eastAsianLayout", "specVanish", "oMath", "ins", "del", "rPrChange"];

    private static readonly string[] ParagraphOrder = ["pStyle", "keepNext", "keepLines", "pageBreakBefore", "framePr", "widowControl", "numPr", "suppressLineNumbers", "pBdr",
        "shd", "tabs", "suppressAutoHyphens", "kinsoku", "wordWrap", "overflowPunct", "topLinePunct", "autoSpaceDE", "autoSpaceDN", "bidi", "adjustRightInd", "snapToGrid",
        "spacing", "ind", "contextualSpacing", "mirrorIndents", "suppressOverlap", "jc", "textDirection", "textAlignment", "textboxTightWrap", "outlineLvl", "divId",
        "cnfStyle", "rPr", "sectPr", "pPrChange"];

    private static readonly string[] CellOrder = ["cnfStyle", "tcW", "gridSpan", "hMerge", "vMerge", "tcBorders", "shd", "noWrap", "tcMar", "textDirection", "tcFitText", "vAlign", "hideMark"];

    private const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";

    /// <summary>The children of a property element in the schema's order (Word writes them so; the validator insists).</summary>
    public static string Ordered(string children, string[] order)
    {
        if (children.Length == 0)
        {
            return children;
        }

        var root = XElement.Parse($"<root xmlns:w=\"{W}\" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">{children}</root>");
        var sorted = root.Elements().Select((element, index) => (element, index))
            .OrderBy(item => Array.IndexOf(order, item.element.Name.LocalName) is var position and >= 0 ? position : order.Length)
            .ThenBy(item => item.index)
            .Select(item => item.element.ToString(SaveOptions.DisableFormatting));
        return string.Concat(sorted)
            .Replace($" xmlns:w=\"{W}\"", "", StringComparison.Ordinal)
            .Replace(" xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\"", "", StringComparison.Ordinal)
            .Replace(" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"", "", StringComparison.Ordinal);
    }

    public static string Esc(string text) => SecurityElement.Escape(text) ?? "";

    /// <summary>w14:paraId / w14:textId as Word 2010+ writes them on every paragraph.</summary>
    private static string Ids()
    {
        var id = _paraId++;
        return $" w14:paraId=\"{id:X8}\" w14:textId=\"{id ^ 0x55AA55AA:X8}\"";
    }

    /// <summary>A paragraph: <paramref name="style"/> (style id or null), extra pPr children, runs.</summary>
    public static string P(string? style, string runs, string pPr = "", string? markRPr = null)
    {
        var properties = Ordered((style is null ? "" : $"<w:pStyle w:val=\"{style}\"/>") + pPr + (markRPr is null ? "" : $"<w:rPr>{Ordered(markRPr, RunOrder)}</w:rPr>"), ParagraphOrder);
        return $"<w:p{Ids()}>{(properties.Length > 0 ? $"<w:pPr>{properties}</w:pPr>" : "")}{runs}</w:p>";
    }

    /// <summary>A plain paragraph with text.</summary>
    public static string Para(string text, string? style = null, string pPr = "") => P(style, R(text), pPr);

    public static string R(string text, string rPr = "")
    {
        var preserve = text.StartsWith(' ') || text.EndsWith(' ') ? " xml:space=\"preserve\"" : "";
        return $"<w:r>{(rPr.Length > 0 ? $"<w:rPr>{Ordered(rPr, RunOrder)}</w:rPr>" : "")}<w:t{preserve}>{Esc(text)}</w:t></w:r>";
    }

    public static string Tab(string rPr = "") => $"<w:r>{(rPr.Length > 0 ? $"<w:rPr>{rPr}</w:rPr>" : "")}<w:tab/></w:r>";

    public static string Br(string type = "") => type.Length == 0 ? "<w:r><w:br/></w:r>" : $"<w:r><w:br w:type=\"{type}\"/></w:r>";

    public static string Bold => "<w:b/><w:bCs/>";

    public static string Italic => "<w:i/><w:iCs/>";

    public static string Size(double points) => $"<w:sz w:val=\"{(int)(points * 2)}\"/><w:szCs w:val=\"{(int)(points * 2)}\"/>";

    public static string Color(string hex, string? theme = null, string? shade = null, string? tint = null) =>
        $"<w:color w:val=\"{hex}\"{(theme is null ? "" : $" w:themeColor=\"{theme}\"")}{(shade is null ? "" : $" w:themeShade=\"{shade}\"")}{(tint is null ? "" : $" w:themeTint=\"{tint}\"")}/>";

    public static string Font(string name) => $"<w:rFonts w:ascii=\"{name}\" w:hAnsi=\"{name}\" w:cs=\"{name}\"/>";

    public static string Shd(string fill, string? theme = null, string? tint = null) =>
        $"<w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{fill}\"{(theme is null ? "" : $" w:themeFill=\"{theme}\"")}{(tint is null ? "" : $" w:themeFillTint=\"{tint}\"")}/>";

    /// <summary>A complex field with its cached result (Word's begin / instrText / separate / result / end).</summary>
    public static string Field(string instruction, string result, string rPr = "")
    {
        var properties = rPr.Length > 0 ? $"<w:rPr>{rPr}</w:rPr>" : "";
        return $"<w:r>{properties}<w:fldChar w:fldCharType=\"begin\"/></w:r><w:r>{properties}<w:instrText xml:space=\"preserve\"> {Esc(instruction)} </w:instrText></w:r>" +
               $"<w:r>{properties}<w:fldChar w:fldCharType=\"separate\"/></w:r>{R(result, rPr)}<w:r>{properties}<w:fldChar w:fldCharType=\"end\"/></w:r>";
    }

    public static string Hyperlink(string relationshipId, string text) =>
        $"<w:hyperlink r:id=\"{relationshipId}\" w:history=\"1\">{R(text, "<w:rStyle w:val=\"Hyperlink\"/>")}</w:hyperlink>";

    /// <summary>The final w:sectPr: page in twips, margins, header/footer references, columns, title page …</summary>
    public static string Section(int width = 11906, int height = 16838, bool landscape = false, int top = 1417, int right = 1417, int bottom = 1134, int left = 1417,
        string references = "", string columns = "<w:cols w:space=\"708\"/>", string type = "", bool titlePage = false, string extra = "") =>
        $"<w:sectPr>{references}{(type.Length > 0 ? $"<w:type w:val=\"{type}\"/>" : "")}<w:pgSz w:w=\"{width}\" w:h=\"{height}\"{(landscape ? " w:orient=\"landscape\"" : "")}/>" +
        $"<w:pgMar w:top=\"{top}\" w:right=\"{right}\" w:bottom=\"{bottom}\" w:left=\"{left}\" w:header=\"708\" w:footer=\"708\" w:gutter=\"0\"/>{extra}{columns}" +
        $"{(titlePage ? "<w:titlePg/>" : "")}<w:docGrid w:linePitch=\"360\"/></w:sectPr>";

    public static long Emu(double centimetres) => (long)Math.Round(centimetres * 360000);

    private static string Number(long value) => value.ToString(CultureInfo.InvariantCulture);

    private static string NextId() => Number(_docPr++);

    private static string PictureGraphic(string relationshipId, string name, long cx, long cy, string id) =>
        "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/picture\">" +
        $"<pic:pic xmlns:pic=\"http://schemas.openxmlformats.org/drawingml/2006/picture\"><pic:nvPicPr><pic:cNvPr id=\"{id}\" name=\"{Esc(name)}\"/><pic:cNvPicPr/></pic:nvPicPr>" +
        $"<pic:blipFill><a:blip r:embed=\"{relationshipId}\"><a:extLst><a:ext uri=\"{{28A0092B-C50C-407E-A947-70E740481C1C}}\"><a14:useLocalDpi xmlns:a14=\"http://schemas.microsoft.com/office/drawing/2010/main\" val=\"0\"/></a:ext></a:extLst></a:blip>" +
        $"<a:stretch><a:fillRect/></a:stretch></pic:blipFill><pic:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></pic:spPr></pic:pic></a:graphicData></a:graphic>";

    /// <summary>An inline picture (wp:inline), size in cm.</summary>
    public static string InlinePicture(string relationshipId, double widthCm, double heightCm, string name = "Grafik", string description = "")
    {
        var (cx, cy, id) = (Emu(widthCm), Emu(heightCm), NextId());
        return "<w:r><w:rPr><w:noProof/></w:rPr><w:drawing>" +
               $"<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\" wp14:anchorId=\"{_paraId++:X8}\" wp14:editId=\"{_paraId++:X8}\"><wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>" +
               $"<wp:docPr id=\"{id}\" name=\"{Esc(name)} {id}\" descr=\"{Esc(description)}\"/><wp:cNvGraphicFramePr><a:graphicFrameLocks xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" noChangeAspect=\"1\"/></wp:cNvGraphicFramePr>" +
               PictureGraphic(relationshipId, name, cx, cy, "0") + "</wp:inline></w:drawing></w:r>";
    }

    /// <summary>Where an anchored object sits: offsets (cm) or an alignment, each relative to a frame Word knows.</summary>
    public sealed record Position(string RelativeH = "column", double? X = null, string? AlignH = null, string RelativeV = "paragraph", double? Y = null, string? AlignV = null)
    {
        public string Xml =>
            $"<wp:positionH relativeFrom=\"{RelativeH}\">{(AlignH is not null ? $"<wp:align>{AlignH}</wp:align>" : $"<wp:posOffset>{Emu(X ?? 0)}</wp:posOffset>")}</wp:positionH>" +
            $"<wp:positionV relativeFrom=\"{RelativeV}\">{(AlignV is not null ? $"<wp:align>{AlignV}</wp:align>" : $"<wp:posOffset>{Emu(Y ?? 0)}</wp:posOffset>")}</wp:positionV>";
    }

    /// <summary>The wrap element of an anchor: square, tight, topAndBottom, none (behind / in front by <c>behindDoc</c>).</summary>
    public static string Wrap(string kind, long cx = 0, long cy = 0) => kind switch
    {
        "square" => "<wp:wrapSquare wrapText=\"bothSides\"/>",
        "tight" => $"<wp:wrapTight wrapText=\"bothSides\"><wp:wrapPolygon edited=\"0\"><wp:start x=\"0\" y=\"0\"/><wp:lineTo x=\"0\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"21600\"/><wp:lineTo x=\"21600\" y=\"0\"/><wp:lineTo x=\"0\" y=\"0\"/></wp:wrapPolygon></wp:wrapTight>",
        "topAndBottom" => "<wp:wrapTopAndBottom/>",
        _ => "<wp:wrapNone/>",
    };

    private static string Anchor(Position position, double widthCm, double heightCm, string wrap, bool behind, string name, string graphic, bool lockAnchor = false, int z = 251659264)
    {
        var (cx, cy, id) = (Emu(widthCm), Emu(heightCm), NextId());
        var distance = wrap is "square" or "tight" ? "distT=\"0\" distB=\"0\" distL=\"114300\" distR=\"114300\"" : "distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"";
        return $"<wp:anchor {distance} simplePos=\"0\" relativeHeight=\"{z}\" behindDoc=\"{(behind ? 1 : 0)}\" locked=\"{(lockAnchor ? 1 : 0)}\" layoutInCell=\"1\" allowOverlap=\"1\" wp14:anchorId=\"{_paraId++:X8}\" wp14:editId=\"{_paraId++:X8}\">" +
               $"<wp:simplePos x=\"0\" y=\"0\"/>{position.Xml}<wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>{Wrap(wrap, cx, cy)}" +
               $"<wp:docPr id=\"{id}\" name=\"{Esc(name)} {id}\"/><wp:cNvGraphicFramePr/>{graphic.Replace("{ID}", id).Replace("{CX}", Number(cx)).Replace("{CY}", Number(cy))}</wp:anchor>";
    }

    /// <summary>A floating picture (wp:anchor).</summary>
    public static string FloatingPicture(string relationshipId, double widthCm, double heightCm, Position position, string wrap, bool behind = false, string name = "Grafik") =>
        "<w:r><w:rPr><w:noProof/></w:rPr><w:drawing>" +
        Anchor(position, widthCm, heightCm, wrap, behind, name, PictureGraphic(relationshipId, name, Emu(widthCm), Emu(heightCm), "0")) + "</w:drawing></w:r>";

    /// <summary>
    /// A shape (wps:wsp) with a preset geometry, fill and line – and text when <paramref name="textXml"/> is given (a text box when the
    /// geometry is "rect" and <paramref name="isTextBox"/>). Word writes it in mc:AlternateContent with a VML fallback; so does this.
    /// </summary>
    public static string Shape(string preset, double widthCm, double heightCm, Position position, string wrap, string fill, string line, string textXml = "",
        bool isTextBox = false, bool behind = false, string name = "Rechteck", string bodyPr = "", string anchorText = "t")
    {
        var spPr = $"<wps:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{{CX}}\" cy=\"{{CY}}\"/></a:xfrm><a:prstGeom prst=\"{preset}\"><a:avLst/></a:prstGeom>{fill}{line}</wps:spPr>";
        var text = textXml.Length > 0 ? $"<wps:txbx><w:txbxContent>{textXml}</w:txbxContent></wps:txbx>" : "";
        var body = $"<wps:bodyPr rot=\"0\" spcFirstLastPara=\"0\" vertOverflow=\"overflow\" horzOverflow=\"overflow\" vert=\"horz\" wrap=\"square\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" numCol=\"1\" spcCol=\"0\" rtlCol=\"0\" fromWordArt=\"0\" anchor=\"{anchorText}\" anchorCtr=\"0\" forceAA=\"0\" compatLnSpc=\"1\">{bodyPr}<a:prstTxWarp prst=\"textNoShape\"><a:avLst/></a:prstTxWarp><a:noAutofit/></wps:bodyPr>";
        var style = "<wps:style><a:lnRef idx=\"2\"><a:schemeClr val=\"accent1\"><a:shade val=\"15000\"/></a:schemeClr></a:lnRef><a:fillRef idx=\"1\"><a:schemeClr val=\"accent1\"/></a:fillRef>" +
                    "<a:effectRef idx=\"0\"><a:schemeClr val=\"accent1\"/></a:effectRef><a:fontRef idx=\"minor\"><a:schemeClr val=\"lt1\"/></a:fontRef></wps:style>";
        var graphic = "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\">" +
                      $"<wps:wsp><wps:cNvSpPr{(isTextBox ? " txBox=\"1\"" : "")}/>{spPr}{(isTextBox ? "" : style)}{text}{body}</wps:wsp></a:graphicData></a:graphic>";
        var vml = $"<w:pict><v:rect id=\"Shape{_docPr}\" o:spid=\"_x0000_s10{_docPr}\" style=\"position:absolute;margin-left:0;margin-top:0;width:{widthCm * 28.3465:0.#}pt;height:{heightCm * 28.3465:0.#}pt;z-index:251659264\" stroked=\"f\">" +
                  (textXml.Length > 0 ? $"<v:textbox><w:txbxContent>{textXml}</w:txbxContent></v:textbox>" : "") + "</v:rect></w:pict>";
        return "<w:r><w:rPr><w:noProof/></w:rPr><mc:AlternateContent><mc:Choice Requires=\"wps\"><w:drawing>" +
               Anchor(position, widthCm, heightCm, wrap, behind, name, graphic) + "</w:drawing></mc:Choice><mc:Fallback>" + vml + "</mc:Fallback></mc:AlternateContent></w:r>";
    }

    public static string SolidFill(string hex, string? scheme = null, string? lumMod = null, string? lumOff = null) =>
        scheme is null ? $"<a:solidFill><a:srgbClr val=\"{hex}\"/></a:solidFill>"
            : $"<a:solidFill><a:schemeClr val=\"{scheme}\">{(lumMod is null ? "" : $"<a:lumMod val=\"{lumMod}\"/>")}{(lumOff is null ? "" : $"<a:lumOff val=\"{lumOff}\"/>")}</a:schemeClr></a:solidFill>";

    public static string NoFill => "<a:noFill/>";

    public static string Line(string hex, double points = 1) => $"<a:ln w=\"{(int)(points * 12700)}\"><a:solidFill><a:srgbClr val=\"{hex}\"/></a:solidFill></a:ln>";

    public static string NoLine => "<a:ln><a:noFill/></a:ln>";

    /// <summary>A group (wpg:wgp) of child shapes laid out in the group's own coordinate space (EMU, same as the extent).</summary>
    public static string Group(double widthCm, double heightCm, Position position, string wrap, params (string Preset, double X, double Y, double W, double H, string Fill, string Line, string Text)[] children)
    {
        var shapes = string.Concat(children.Select((child, index) =>
            $"<wps:wsp><wps:cNvPr id=\"{_docPr + 100 + index}\" name=\"Form {index + 1}\"/><wps:cNvSpPr/><wps:spPr><a:xfrm><a:off x=\"{Emu(child.X)}\" y=\"{Emu(child.Y)}\"/><a:ext cx=\"{Emu(child.W)}\" cy=\"{Emu(child.H)}\"/></a:xfrm>" +
            $"<a:prstGeom prst=\"{child.Preset}\"><a:avLst/></a:prstGeom>{child.Fill}{child.Line}</wps:spPr>" +
            (child.Text.Length > 0 ? $"<wps:txbx><w:txbxContent>{child.Text}</w:txbxContent></wps:txbx>" : "") +
            "<wps:bodyPr rot=\"0\" vert=\"horz\" wrap=\"square\" lIns=\"91440\" tIns=\"45720\" rIns=\"91440\" bIns=\"45720\" anchor=\"ctr\" anchorCtr=\"0\"><a:noAutofit/></wps:bodyPr></wps:wsp>"));
        var graphic = "<a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\">" +
                      $"<wpg:wgp><wpg:cNvGrpSpPr/><wpg:grpSpPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{{CX}}\" cy=\"{{CY}}\"/><a:chOff x=\"0\" y=\"0\"/><a:chExt cx=\"{{CX}}\" cy=\"{{CY}}\"/></a:xfrm></wpg:grpSpPr>{shapes}</wpg:wgp></a:graphicData></a:graphic>";
        return "<w:r><w:rPr><w:noProof/></w:rPr><mc:AlternateContent><mc:Choice Requires=\"wpg\"><w:drawing>" + Anchor(position, widthCm, heightCm, wrap, false, "Gruppieren", graphic) +
               "</w:drawing></mc:Choice><mc:Fallback><w:pict><v:group style=\"position:absolute\" coordsize=\"21600,21600\"/></w:pict></mc:Fallback></mc:AlternateContent></w:r>";
    }

    /// <summary>An inline chart (c:chart in a graphic frame).</summary>
    public static string InlineChart(string relationshipId, double widthCm, double heightCm)
    {
        var (cx, cy, id) = (Emu(widthCm), Emu(heightCm), NextId());
        return $"<w:r><w:rPr><w:noProof/></w:rPr><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>" +
               $"<wp:docPr id=\"{id}\" name=\"Diagramm {id}\"/><wp:cNvGraphicFramePr/><a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/chart\">" +
               $"<c:chart xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" r:id=\"{relationshipId}\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
    }

    /// <summary>An inline SmartArt (dgm:relIds in a graphic frame).</summary>
    public static string InlineSmartArt((string Data, string Layout, string Style, string Colors) ids, double widthCm, double heightCm)
    {
        var (cx, cy, id) = (Emu(widthCm), Emu(heightCm), NextId());
        return $"<w:r><w:rPr><w:noProof/></w:rPr><w:drawing><wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\"><wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>" +
               $"<wp:docPr id=\"{id}\" name=\"Diagramm {id}\"/><wp:cNvGraphicFramePr/><a:graphic xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\"><a:graphicData uri=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\">" +
               $"<dgm:relIds xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" r:dm=\"{ids.Data}\" r:lo=\"{ids.Layout}\" r:qs=\"{ids.Style}\" r:cs=\"{ids.Colors}\"/></a:graphicData></a:graphic></wp:inline></w:drawing></w:r>";
    }

    /// <summary>A table: tblPr children, grid widths (twips), rows.</summary>
    public static string Table(string tblPr, int[] grid, params string[] rows) =>
        $"<w:tbl><w:tblPr>{tblPr}</w:tblPr><w:tblGrid>{string.Concat(grid.Select(width => $"<w:gridCol w:w=\"{width}\"/>"))}</w:tblGrid>{string.Concat(rows)}</w:tbl>";

    public static string Row(string cells, string trPr = "") => $"<w:tr{Ids()}>{(trPr.Length > 0 ? $"<w:trPr>{trPr}</w:trPr>" : "")}{cells}</w:tr>";

    public static string Cell(int widthTwips, string content, string tcPr = "") =>
        $"<w:tc><w:tcPr>{Ordered($"<w:tcW w:w=\"{widthTwips}\" w:type=\"dxa\"/>{tcPr}", CellOrder)}</w:tcPr>{content}</w:tc>";

    /// <summary>A PNG made with Skia: a gradient with soft shapes (a "photo"), or the accent mark of a logo.</summary>
    public static byte[] Picture(int width, int height, SKColor from, SKColor to, bool logo = false)
    {
        using var bitmap = new SKBitmap(width, height);
        using (var canvas = new SKCanvas(bitmap))
        {
            using var paint = new SKPaint { IsAntialias = true };
            if (logo)
            {
                canvas.Clear(SKColors.Transparent);
                paint.Color = from;
                canvas.DrawRoundRect(new SKRect(0, 0, height, height), height * 0.2f, height * 0.2f, paint);
                paint.Color = SKColors.White;
                canvas.DrawCircle(height / 2f, height / 2f, height * 0.25f, paint);
                paint.Color = to;
                canvas.DrawRect(new SKRect(height * 1.15f, height * 0.3f, width, height * 0.7f), paint);
            }
            else
            {
                paint.Shader = SKShader.CreateLinearGradient(new SKPoint(0, 0), new SKPoint(width, height), [from, to], SKShaderTileMode.Clamp);
                canvas.DrawRect(new SKRect(0, 0, width, height), paint);
                paint.Shader = null;
                paint.Color = new SKColor(255, 255, 255, 70);
                canvas.DrawCircle(width * 0.7f, height * 0.35f, height * 0.22f, paint);
                paint.Color = new SKColor(0, 0, 0, 50);
                using var hills = new SKPath();
                hills.MoveTo(0, height);
                hills.LineTo(0, height * 0.7f);
                hills.QuadTo(width * 0.3f, height * 0.45f, width * 0.6f, height * 0.75f);
                hills.QuadTo(width * 0.8f, height * 0.6f, width, height * 0.72f);
                hills.LineTo(width, height);
                hills.Close();
                canvas.DrawPath(hills, paint);
            }
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 90);
        return data.ToArray();
    }
}
