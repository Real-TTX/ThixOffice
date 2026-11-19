using System.Globalization;
using System.Text;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>The bigger parts of the corpus as Word writes them: numbering definitions, a chart with cached values, a SmartArt.</summary>
internal static class CorpusParts
{
    /// <summary>
    /// numbering.xml: abstract 0 = Word's bullet list (Symbol F0B7, Courier New "o", Wingdings F0A7), 1 = the heading outline list
    /// "1 / 1.1 / 1.1.1" linked to Überschrift 1–3, 2 = "1. / a) / i." numbered list, 3 = Wingdings arrows and checks.
    /// Instances 1–4 name them; 5 restarts list 2 at 1 (lvlOverride startOverride).
    /// </summary>
    public static string Numbering()
    {
        var builder = new StringBuilder();
        builder.Append("<w:abstractNum w:abstractNumId=\"0\" w15:restartNumberingAfterBreak=\"0\"><w:nsid w:val=\"0A1B2C3D\"/><w:multiLevelType w:val=\"hybridMultilevel\"/><w:tmpl w:val=\"04070001\"/>");
        var bullets = new[] { ("", "Symbol"), ("o", "Courier New"), ("", "Wingdings") };
        for (var level = 0; level < 9; level++)
        {
            var (text, font) = bullets[level % 3];
            builder.Append($"<w:lvl w:ilvl=\"{level}\" w:tplc=\"0407000{(level % 3 == 0 ? 1 : level % 3 == 1 ? 3 : 5)}\"{(level > 0 ? " w:tentative=\"1\"" : "")}><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"{text}\"/><w:lvlJc w:val=\"left\"/>" +
                           $"<w:pPr><w:ind w:left=\"{720 + level * 720}\" w:hanging=\"360\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"{font}\" w:hAnsi=\"{font}\"{(font == "Courier New" ? " w:cs=\"Courier New\"" : "")} w:hint=\"default\"/></w:rPr></w:lvl>");
        }

        builder.Append("</w:abstractNum>");
        builder.Append("<w:abstractNum w:abstractNumId=\"1\" w15:restartNumberingAfterBreak=\"0\"><w:nsid w:val=\"1F2E3D4C\"/><w:multiLevelType w:val=\"multilevel\"/><w:tmpl w:val=\"0407001D\"/>");
        for (var level = 0; level < 9; level++)
        {
            var text = string.Join(".", Enumerable.Range(1, level + 1).Select(n => $"%{n}"));
            var style = level < 3 ? $"<w:pStyle w:val=\"berschrift{level + 1}\"/>" : "";
            builder.Append($"<w:lvl w:ilvl=\"{level}\"><w:start w:val=\"1\"/><w:numFmt w:val=\"decimal\"/>{style}<w:lvlText w:val=\"{text}\"/><w:lvlJc w:val=\"left\"/>" +
                           $"<w:pPr><w:ind w:left=\"{432 + level * 144}\" w:hanging=\"{432 + level * 144}\"/></w:pPr></w:lvl>");
        }

        builder.Append("</w:abstractNum>");
        builder.Append("<w:abstractNum w:abstractNumId=\"2\" w15:restartNumberingAfterBreak=\"0\"><w:nsid w:val=\"2E3D4C5B\"/><w:multiLevelType w:val=\"hybridMultilevel\"/><w:tmpl w:val=\"0407000F\"/>");
        var formats = new[] { ("decimal", "%1."), ("lowerLetter", "%2)"), ("lowerRoman", "%3."), ("decimal", "%4."), ("upperLetter", "%5."), ("upperRoman", "%6."), ("decimal", "%7."), ("lowerLetter", "%8."), ("lowerRoman", "%9.") };
        for (var level = 0; level < 9; level++)
        {
            var (format, text) = formats[level];
            builder.Append($"<w:lvl w:ilvl=\"{level}\" w:tplc=\"0407000F\"{(level > 0 ? " w:tentative=\"1\"" : "")}><w:start w:val=\"1\"/><w:numFmt w:val=\"{format}\"/><w:lvlText w:val=\"{text}\"/>" +
                           $"<w:lvlJc w:val=\"{(format.EndsWith("Roman", StringComparison.Ordinal) ? "right" : "left")}\"/><w:pPr><w:ind w:left=\"{720 + level * 720}\" w:hanging=\"{(format.EndsWith("Roman", StringComparison.Ordinal) ? 180 : 360)}\"/></w:pPr></w:lvl>");
        }

        builder.Append("</w:abstractNum>");
        builder.Append("<w:abstractNum w:abstractNumId=\"3\" w15:restartNumberingAfterBreak=\"0\"><w:nsid w:val=\"3D4C5B6A\"/><w:multiLevelType w:val=\"hybridMultilevel\"/><w:tmpl w:val=\"04070003\"/>");
        var marks = new[] { "", "", "" };
        for (var level = 0; level < 3; level++)
        {
            builder.Append($"<w:lvl w:ilvl=\"{level}\" w:tplc=\"04070001\"><w:start w:val=\"1\"/><w:numFmt w:val=\"bullet\"/><w:lvlText w:val=\"{marks[level]}\"/><w:lvlJc w:val=\"left\"/>" +
                           $"<w:pPr><w:ind w:left=\"{720 + level * 720}\" w:hanging=\"360\"/></w:pPr><w:rPr><w:rFonts w:ascii=\"Wingdings\" w:hAnsi=\"Wingdings\" w:hint=\"default\"/><w:color w:val=\"4472C4\" w:themeColor=\"accent1\"/></w:rPr></w:lvl>");
        }

        builder.Append("</w:abstractNum>");
        builder.Append("<w:num w:numId=\"1\" w16cid:durableId=\"101\" xmlns:w16cid=\"http://schemas.microsoft.com/office/word/2016/wordml/cid\"><w:abstractNumId w:val=\"0\"/></w:num>")
            .Append("<w:num w:numId=\"2\"><w:abstractNumId w:val=\"1\"/></w:num><w:num w:numId=\"3\"><w:abstractNumId w:val=\"2\"/></w:num><w:num w:numId=\"4\"><w:abstractNumId w:val=\"3\"/></w:num>")
            .Append("<w:num w:numId=\"5\"><w:abstractNumId w:val=\"2\"/><w:lvlOverride w:ilvl=\"0\"><w:startOverride w:val=\"1\"/></w:lvlOverride></w:num>");
        return builder.ToString().Replace(" w16cid:durableId=\"101\" xmlns:w16cid=\"http://schemas.microsoft.com/office/word/2016/wordml/cid\"", "");
    }

    /// <summary>A clustered column chart (c:chartSpace) with three series and their cached values, colours from the theme's accents.</summary>
    public static string ColumnChart(string title, string[] categories, params (string Name, double[] Values)[] series)
    {
        string Cache(IEnumerable<string> values, bool numbers)
        {
            var list = values.ToList();
            var tag = numbers ? "numCache" : "strCache";
            return $"<c:{tag}>{(numbers ? "<c:formatCode>General</c:formatCode>" : "")}<c:ptCount val=\"{list.Count}\"/>" +
                   string.Concat(list.Select((value, index) => $"<c:pt idx=\"{index}\"><c:v>{value}</c:v></c:pt>")) + $"</c:{tag}>";
        }

        var columns = "BCDEFG";
        var builder = new StringBuilder("<c:chartSpace xmlns:c=\"http://schemas.openxmlformats.org/drawingml/2006/chart\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                                        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" xmlns:c16r2=\"http://schemas.microsoft.com/office/drawing/2015/06/chart\">" +
                                        "<c:date1904 val=\"0\"/><c:lang val=\"de-DE\"/><c:roundedCorners val=\"0\"/><c:chart>" +
                                        $"<c:title><c:tx><c:rich><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr sz=\"1400\" b=\"0\"/></a:pPr><a:r><a:rPr lang=\"de-DE\"/><a:t>{Wml.Esc(title)}</a:t></a:r></a:p></c:rich></c:tx><c:overlay val=\"0\"/></c:title>" +
                                        "<c:autoTitleDeleted val=\"0\"/><c:plotArea><c:layout/><c:barChart><c:barDir val=\"col\"/><c:grouping val=\"clustered\"/><c:varyColors val=\"0\"/>");
        for (var index = 0; index < series.Length; index++)
        {
            var (name, values) = series[index];
            builder.Append($"<c:ser><c:idx val=\"{index}\"/><c:order val=\"{index}\"/><c:tx><c:strRef><c:f>Tabelle1!${columns[index]}$1</c:f>{Cache([name], false)}</c:strRef></c:tx>" +
                           $"<c:spPr><a:solidFill><a:schemeClr val=\"accent{index + 1}\"/></a:solidFill><a:ln><a:noFill/></a:ln></c:spPr><c:invertIfNegative val=\"0\"/>" +
                           $"<c:cat><c:strRef><c:f>Tabelle1!$A$2:$A${categories.Length + 1}</c:f>{Cache(categories, false)}</c:strRef></c:cat>" +
                           $"<c:val><c:numRef><c:f>Tabelle1!${columns[index]}$2:${columns[index]}${categories.Length + 1}</c:f>{Cache(values.Select(value => value.ToString(CultureInfo.InvariantCulture)), true)}</c:numRef></c:val></c:ser>");
        }

        builder.Append("<c:gapWidth val=\"219\"/><c:overlap val=\"-27\"/><c:axId val=\"500000001\"/><c:axId val=\"500000002\"/></c:barChart>" +
                       "<c:catAx><c:axId val=\"500000001\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"b\"/><c:numFmt formatCode=\"General\" sourceLinked=\"1\"/>" +
                       "<c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"500000002\"/><c:crosses val=\"autoZero\"/><c:auto val=\"1\"/><c:lblAlgn val=\"ctr\"/><c:lblOffset val=\"100\"/><c:noMultiLvlLbl val=\"0\"/></c:catAx>" +
                       "<c:valAx><c:axId val=\"500000002\"/><c:scaling><c:orientation val=\"minMax\"/></c:scaling><c:delete val=\"0\"/><c:axPos val=\"l\"/><c:majorGridlines><c:spPr><a:ln w=\"9525\"><a:solidFill><a:schemeClr val=\"tx1\"><a:lumMod val=\"15000\"/><a:lumOff val=\"85000\"/></a:schemeClr></a:solidFill></a:ln></c:spPr></c:majorGridlines>" +
                       "<c:numFmt formatCode=\"General\" sourceLinked=\"1\"/><c:majorTickMark val=\"none\"/><c:minorTickMark val=\"none\"/><c:tickLblPos val=\"nextTo\"/><c:crossAx val=\"500000001\"/><c:crosses val=\"autoZero\"/><c:crossBetween val=\"between\"/></c:valAx>" +
                       "</c:plotArea><c:legend><c:legendPos val=\"b\"/><c:overlay val=\"0\"/></c:legend><c:plotVisOnly val=\"1\"/><c:dispBlanksAs val=\"gap\"/></c:chart>" +
                       "<c:spPr><a:solidFill><a:schemeClr val=\"bg1\"/></a:solidFill><a:ln w=\"9525\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:noFill/><a:round/></a:ln></c:spPr>" +
                       "<c:txPr><a:bodyPr/><a:lstStyle/><a:p><a:pPr><a:defRPr/></a:pPr><a:endParaRPr lang=\"de-DE\"/></a:p></c:txPr></c:chartSpace>");
        return builder.ToString();
    }

    /// <summary>
    /// A "Einfacher Prozess" SmartArt (three steps with arrows): the data model with its points and connections, a minimal layout
    /// definition, quick style and colours – and the drawing part (dsp:drawing) Word writes as the pre-rendered fallback.
    /// </summary>
    public static (string Data, string Layout, string Style, string Colors, string Drawing) SmartArtProcess(string[] steps, double widthCm, double heightCm)
    {
        const string Dgm = "xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\"";
        var points = new StringBuilder($"<dgm:pt modelId=\"{{00000000-0000-0000-0000-000000000000}}\" type=\"doc\"><dgm:prSet loTypeId=\"urn:microsoft.com/office/officeart/2005/8/layout/process1\" loCatId=\"process\" qsTypeId=\"urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1\" qsCatId=\"simple\" csTypeId=\"urn:microsoft.com/office/officeart/2005/8/colors/accent1_2\" csCatId=\"accent1\" phldr=\"0\"/><dgm:spPr/><dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:endParaRPr lang=\"de-DE\"/></a:p></dgm:t></dgm:pt>");
        var connections = new StringBuilder();
        for (var index = 0; index < steps.Length; index++)
        {
            points.Append($"<dgm:pt modelId=\"{{1000000{index}-0000-0000-0000-000000000000}}\"><dgm:prSet phldrT=\"[Text]\"/><dgm:spPr/><dgm:t><a:bodyPr/><a:lstStyle/><a:p><a:r><a:rPr lang=\"de-DE\"/><a:t>{Wml.Esc(steps[index])}</a:t></a:r></a:p></dgm:t></dgm:pt>");
            connections.Append($"<dgm:cxn modelId=\"{{2000000{index}-0000-0000-0000-000000000000}}\" srcId=\"{{00000000-0000-0000-0000-000000000000}}\" destId=\"{{1000000{index}-0000-0000-0000-000000000000}}\" srcOrd=\"{index}\" destOrd=\"0\"/>");
        }

        var data = $"<dgm:dataModel {Dgm}><dgm:ptLst>{points}</dgm:ptLst><dgm:cxnLst>{connections}</dgm:cxnLst><dgm:bg/><dgm:whole/>" +
                   "<dgm:extLst><a:ext uri=\"http://schemas.microsoft.com/office/drawing/2008/diagram\"><dsp:dataModelExt xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" relId=\"rIdSmartArtDrawing\" minVer=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\"/></a:ext></dgm:extLst></dgm:dataModel>";
        var layout = $"<dgm:layoutDef {Dgm} uniqueId=\"urn:microsoft.com/office/officeart/2005/8/layout/process1\"><dgm:title val=\"\"/><dgm:desc val=\"\"/><dgm:catLst><dgm:cat type=\"process\" pri=\"1000\"/></dgm:catLst>" +
                     "<dgm:layoutNode name=\"Name0\"><dgm:alg type=\"lin\"/><dgm:shape xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" r:blip=\"\"><dgm:adjLst/></dgm:shape><dgm:presOf/></dgm:layoutNode></dgm:layoutDef>";
        var style = $"<dgm:styleDef {Dgm} uniqueId=\"urn:microsoft.com/office/officeart/2005/8/quickstyle/simple1\"><dgm:title val=\"\"/><dgm:desc val=\"\"/><dgm:catLst><dgm:cat type=\"simple\" pri=\"10100\"/></dgm:catLst>" +
                    "<dgm:scene3d><a:camera prst=\"orthographicFront\"/><a:lightRig rig=\"threePt\" dir=\"t\"/></dgm:scene3d><dgm:styleLbl name=\"node1\"><dgm:scene3d><a:camera prst=\"orthographicFront\"/><a:lightRig rig=\"threePt\" dir=\"t\"/></dgm:scene3d><dgm:sp3d/><dgm:txPr/>" +
                    "<dgm:style><a:lnRef idx=\"2\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:lnRef><a:fillRef idx=\"1\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:fillRef><a:effectRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:effectRef><a:fontRef idx=\"minor\"><a:schemeClr val=\"lt1\"/></a:fontRef></dgm:style></dgm:styleLbl></dgm:styleDef>";
        var colors = $"<dgm:colorsDef {Dgm} uniqueId=\"urn:microsoft.com/office/officeart/2005/8/colors/accent1_2\"><dgm:title val=\"\"/><dgm:desc val=\"\"/><dgm:catLst><dgm:cat type=\"accent1\" pri=\"11200\"/></dgm:catLst>" +
                     "<dgm:styleLbl name=\"node1\"><dgm:fillClrLst meth=\"repeat\"><a:schemeClr val=\"accent1\"/></dgm:fillClrLst><dgm:linClrLst meth=\"repeat\"><a:schemeClr val=\"lt1\"/></dgm:linClrLst><dgm:effectClrLst/><dgm:txLinClrLst/><dgm:txFillClrLst meth=\"repeat\"><a:schemeClr val=\"lt1\"/></dgm:txFillClrLst><dgm:txEffectClrLst/></dgm:styleLbl></dgm:colorsDef>";

        // dsp:drawing: the shapes as laid out (EMU in the frame's space), boxes and arrows.
        var width = Wml.Emu(widthCm);
        var height = Wml.Emu(heightCm);
        var box = width / (steps.Length * 1.5);
        var gap = (width - box * steps.Length) / Math.Max(1, steps.Length - 1);
        var boxHeight = height * 0.6;
        var top = (height - boxHeight) / 2;
        var shapes = new StringBuilder();
        for (var index = 0; index < steps.Length; index++)
        {
            var x = (long)(index * (box + gap));
            shapes.Append($"<dsp:sp modelId=\"{{3000000{index}-0000-0000-0000-000000000000}}\"><dsp:nvSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvSpPr/></dsp:nvSpPr><dsp:spPr><a:xfrm><a:off x=\"{x}\" y=\"{(long)top}\"/><a:ext cx=\"{(long)box}\" cy=\"{(long)boxHeight}\"/></a:xfrm>" +
                          "<a:prstGeom prst=\"roundRect\"><a:avLst><a:gd name=\"adj\" fmla=\"val 10000\"/></a:avLst></a:prstGeom><a:solidFill><a:schemeClr val=\"accent1\"><a:hueOff val=\"0\"/><a:satOff val=\"0\"/><a:lumOff val=\"0\"/><a:alphaOff val=\"0\"/></a:schemeClr></a:solidFill>" +
                          "<a:ln w=\"12700\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"lt1\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln></dsp:spPr>" +
                          "<dsp:style><a:lnRef idx=\"2\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:lnRef><a:fillRef idx=\"1\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:fillRef><a:effectRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:effectRef><a:fontRef idx=\"minor\"><a:schemeClr val=\"lt1\"/></a:fontRef></dsp:style>" +
                          $"<dsp:txBody><a:bodyPr spcFirstLastPara=\"0\" vert=\"horz\" wrap=\"square\" lIns=\"57150\" tIns=\"57150\" rIns=\"57150\" bIns=\"57150\" numCol=\"1\" spcCol=\"1270\" anchor=\"ctr\" anchorCtr=\"0\"><a:noAutofit/></a:bodyPr><a:lstStyle/>" +
                          $"<a:p><a:pPr marL=\"0\" lvl=\"0\" indent=\"0\" algn=\"ctr\" defTabSz=\"666750\"><a:lnSpc><a:spcPct val=\"90000\"/></a:lnSpc><a:spcBef><a:spcPct val=\"0\"/></a:spcBef><a:spcAft><a:spcPct val=\"35000\"/></a:spcAft></a:pPr><a:r><a:rPr lang=\"de-DE\" sz=\"1500\" kern=\"1200\"/><a:t>{Wml.Esc(steps[index])}</a:t></a:r></a:p></dsp:txBody>" +
                          $"<dsp:txXfrm><a:off x=\"{x}\" y=\"{(long)top}\"/><a:ext cx=\"{(long)box}\" cy=\"{(long)boxHeight}\"/></dsp:txXfrm></dsp:sp>");
            if (index < steps.Length - 1)
            {
                var arrowWidth = (long)(gap * 0.6);
                var arrowX = (long)(x + box + (gap - arrowWidth) / 2);
                var arrowHeight = (long)(boxHeight * 0.3);
                shapes.Append($"<dsp:sp modelId=\"{{4000000{index}-0000-0000-0000-000000000000}}\"><dsp:nvSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvSpPr/></dsp:nvSpPr><dsp:spPr><a:xfrm><a:off x=\"{arrowX}\" y=\"{(long)(height / 2 - arrowHeight / 2)}\"/><a:ext cx=\"{arrowWidth}\" cy=\"{arrowHeight}\"/></a:xfrm>" +
                              "<a:prstGeom prst=\"rightArrow\"><a:avLst><a:gd name=\"adj1\" fmla=\"val 60000\"/><a:gd name=\"adj2\" fmla=\"val 50000\"/></a:avLst></a:prstGeom><a:solidFill><a:schemeClr val=\"accent1\"><a:tint val=\"60000\"/></a:schemeClr></a:solidFill><a:ln><a:noFill/></a:ln></dsp:spPr><dsp:style><a:lnRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:lnRef><a:fillRef idx=\"1\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:fillRef><a:effectRef idx=\"0\"><a:scrgbClr r=\"0\" g=\"0\" b=\"0\"/></a:effectRef><a:fontRef idx=\"minor\"><a:schemeClr val=\"lt1\"/></a:fontRef></dsp:style></dsp:sp>");
            }
        }

        var drawing = "<dsp:drawing xmlns:dgm=\"http://schemas.openxmlformats.org/drawingml/2006/diagram\" xmlns:dsp=\"http://schemas.microsoft.com/office/drawing/2008/diagram\" xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\">" +
                      $"<dsp:spTree><dsp:nvGrpSpPr><dsp:cNvPr id=\"0\" name=\"\"/><dsp:cNvGrpSpPr/></dsp:nvGrpSpPr><dsp:grpSpPr/>{shapes}</dsp:spTree></dsp:drawing>";
        return (data, layout, style, colors, drawing);
    }
}
