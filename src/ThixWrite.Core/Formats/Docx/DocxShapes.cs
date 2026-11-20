using System.Globalization;
using System.Security;
using System.Text;
using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Shapes and text boxes ThixWrite edits (<see cref="ShapeInline"/>): a w:drawing that holds one wps:wsp is read as one (keeping its XML
/// for an unchanged write); writing makes the w:drawing from the model – wp:inline / wp:anchor, the preset geometry, fill, outline,
/// shadow, the text box's w:txbxContent (its blocks written like the body) and the body properties.
/// </summary>
internal static class DocxShapes
{
    private const string ShapeUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const double EmuPerPoint = 12700;

    /// <summary>The shape of a w:drawing with exactly one wps:wsp (not a group, canvas, picture or chart), or null.</summary>
    public static ShapeInline? Read(W.Drawing drawing, DocxDrawings.IDrawingContext context, CharacterProperties properties, Hyperlink? link)
    {
        var container = drawing.ChildElements.FirstOrDefault(child => child.LocalName is "inline" or "anchor");
        var data = container?.Descendants().FirstOrDefault(element => element.LocalName == "graphicData");
        if (data is null || DocxValues.AttributeValue(data, "uri") != ShapeUri || data.ChildElements.Count(child => child.LocalName == "wsp") != 1)
        {
            return null;
        }

        if (DocxDrawings.Read(drawing, context) is not { Elements: [DrawingShape shape] } graphic)
        {
            return null;
        }

        var textBox = data.Descendants().FirstOrDefault(element => element.LocalName == "cNvSpPr") is { } nonVisual
                      && DocxValues.AttributeValue(nonVisual, "txBox") is "1" or "true";
        var result = new ShapeInline(shape with { X = 0, Y = 0, Width = 0, Height = 0 }, graphic.Width, graphic.Height, properties, link) { Anchor = graphic.Anchor, IsTextBox = textBox };
        return result with { SourceXml = XmlFragments.Canonical(drawing), ReadAs = result };
    }

    /// <summary>
    /// The w:drawing of a shape: the one read while it is unchanged, else made from the model (<paramref name="writeBlocks"/> writes the
    /// text box's paragraphs; <paramref name="docPrId"/> gives a fresh drawing id).
    /// </summary>
    public static OpenXmlElement Write(ShapeInline shape, Func<IReadOnlyList<Block>, IEnumerable<OpenXmlElement>> writeBlocks, Func<uint> docPrId, Func<string, bool> claimDocPr)
    {
        if (shape is { SourceXml: { } source, IsUnchanged: true } && claimDocPr(source) && XmlFragments.Parse<W.Run>(source).FirstOrDefault() is { } kept)
        {
            return kept;
        }

        var id = docPrId();
        var drawing = (W.Drawing)XmlFragments.Parse<W.Run>(Xml(shape, id)).Single();
        if (shape.Shape.Text is { } text && drawing.Descendants().FirstOrDefault(element => element.LocalName == "txbxContent") is OpenXmlCompositeElement content)
        {
            foreach (var element in writeBlocks(text.Blocks))
            {
                content.AppendChild(element);
            }
        }

        DocxAnchors.Place(drawing, shape.Width, shape.Height, shape.Anchor);
        return drawing;
    }

    private static string Emu(double points) => ((long)Math.Round(points * EmuPerPoint)).ToString(CultureInfo.InvariantCulture);

    /// <summary>The XML of a new w:drawing with the shape in the line (the caller makes it float).</summary>
    private static string Xml(ShapeInline inline, uint id)
    {
        var shape = inline.Shape;
        var (cx, cy) = (Emu(inline.Width), Emu(inline.Height));
        var name = inline.IsTextBox ? $"Textfeld {id}" : $"Form {id}";
        var xml = new StringBuilder();
        xml.Append("<w:drawing xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" ")
            .Append("xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" xmlns:wps=\"").Append(ShapeUri).Append("\">")
            .Append("<wp:inline distT=\"0\" distB=\"0\" distL=\"0\" distR=\"0\">")
            .Append($"<wp:extent cx=\"{cx}\" cy=\"{cy}\"/><wp:effectExtent l=\"0\" t=\"0\" r=\"0\" b=\"0\"/>")
            .Append($"<wp:docPr id=\"{id.ToString(CultureInfo.InvariantCulture)}\" name=\"{name}\"/><wp:cNvGraphicFramePr/>")
            .Append($"<a:graphic><a:graphicData uri=\"{ShapeUri}\"><wps:wsp>")
            .Append(inline.IsTextBox ? "<wps:cNvSpPr txBox=\"1\"/>" : "<wps:cNvSpPr/>")
            .Append("<wps:spPr>");
        var transform = new StringBuilder("<a:xfrm");
        if (shape.Rotation != 0)
        {
            transform.Append($" rot=\"{((long)Math.Round(shape.Rotation * 60000)).ToString(CultureInfo.InvariantCulture)}\"");
        }

        if (shape.FlipHorizontal)
        {
            transform.Append(" flipH=\"1\"");
        }

        if (shape.FlipVertical)
        {
            transform.Append(" flipV=\"1\"");
        }

        xml.Append(transform).Append($"><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{cx}\" cy=\"{cy}\"/></a:xfrm>");
        if (shape.CustomGeometryXml is { } custom)
        {
            xml.Append(custom);
        }
        else
        {
            xml.Append($"<a:prstGeom prst=\"{SecurityElement.Escape(shape.Preset)}\"><a:avLst>");
            foreach (var adjustment in shape.Adjustments)
            {
                xml.Append($"<a:gd name=\"{SecurityElement.Escape(adjustment.Name)}\" fmla=\"val {adjustment.Value.ToString(CultureInfo.InvariantCulture)}\"/>");
            }

            xml.Append("</a:avLst></a:prstGeom>");
        }

        xml.Append(Fill(shape.Fill));
        xml.Append(shape.Line is { } line
            ? $"<a:ln w=\"{Emu(line.Width)}\"><a:solidFill>{Color(line.Color)}</a:solidFill><a:prstDash val=\"{SecurityElement.Escape(line.Dash)}\"/></a:ln>"
            : "<a:ln><a:noFill/></a:ln>");
        if (shape.Shadow is { } shadow)
        {
            xml.Append($"<a:effectLst><a:outerShdw blurRad=\"{Emu(shadow.Blur)}\" dist=\"{Emu(shadow.Distance)}\" dir=\"{((long)Math.Round(shadow.Direction * 60000)).ToString(CultureInfo.InvariantCulture)}\" algn=\"tl\" rotWithShape=\"0\">")
                .Append(Color(shadow.Color)).Append("</a:outerShdw></a:effectLst>");
        }

        xml.Append("</wps:spPr>");
        if (shape.Text is not null)
        {
            xml.Append("<wps:txbx><w:txbxContent></w:txbxContent></wps:txbx>");
        }

        var (left, top, right, bottom) = shape.Insets;
        var anchor = shape.TextAnchor switch { DrawingTextAnchor.Center => "ctr", DrawingTextAnchor.Bottom => "b", _ => "t" };
        xml.Append($"<wps:bodyPr rot=\"0\" vert=\"horz\" wrap=\"square\" lIns=\"{Emu(left)}\" tIns=\"{Emu(top)}\" rIns=\"{Emu(right)}\" bIns=\"{Emu(bottom)}\" anchor=\"{anchor}\" anchorCtr=\"0\">")
            .Append(shape.AutoFit ? "<a:spAutoFit/>" : "<a:noAutofit/>").Append("</wps:bodyPr>");
        xml.Append("</wps:wsp></a:graphicData></a:graphic></wp:inline></w:drawing>");
        return xml.ToString();
    }

    private static string Fill(DrawingFill? fill)
    {
        if (fill is null)
        {
            return "<a:noFill/>";
        }

        if (fill.Stops.Count >= 2)
        {
            var stops = string.Concat(fill.Stops.Select(stop => $"<a:gs pos=\"{((long)Math.Round(stop.Position * 100000)).ToString(CultureInfo.InvariantCulture)}\">{Color(stop.Color)}</a:gs>"));
            return $"<a:gradFill rotWithShape=\"1\"><a:gsLst>{stops}</a:gsLst><a:lin ang=\"{((long)Math.Round(fill.Angle * 60000)).ToString(CultureInfo.InvariantCulture)}\" scaled=\"0\"/></a:gradFill>";
        }

        return fill.Color is { } color ? $"<a:solidFill>{Color(color)}</a:solidFill>" : "<a:noFill/>";
    }

    private static string Color(DrawingColor color)
    {
        var modifiers = string.Concat(color.Modifiers.Select(modifier => modifier.Value is { } value
            ? $"<a:{modifier.Name} val=\"{value.ToString(CultureInfo.InvariantCulture)}\"/>"
            : $"<a:{modifier.Name}/>"));
        return color.Scheme is { } scheme
            ? $"<a:schemeClr val=\"{SecurityElement.Escape(scheme)}\">{modifiers}</a:schemeClr>"
            : $"<a:srgbClr val=\"{SecurityElement.Escape(color.Rgb ?? "000000")}\">{modifiers}</a:srgbClr>";
    }
}
