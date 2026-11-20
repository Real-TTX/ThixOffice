using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixOffice.Text.Drawing.Geometry;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Reads what Word's drawings show, for the display: the anchor of a floating object (wp:anchor), and the content of drawings that
/// are no single picture – shapes and text boxes (wps:wsp with w:txbxContent), groups (wpg:wgp, nested groups flattened through their
/// transforms), pictures inside groups, charts (the chart part's XML) and SmartArt (the pre-drawn shapes of its drawing part). The
/// drawing itself stays as read (<see cref="OpaqueInline.Xml"/>); this only describes it. VML (w:pict) text boxes are read from their
/// v:textbox and style.
/// </summary>
internal static class DocxDrawings
{
    private const string WordprocessingShapeUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingShape";
    private const string WordprocessingGroupUri = "http://schemas.microsoft.com/office/word/2010/wordprocessingGroup";
    private const string PictureUri = "http://schemas.openxmlformats.org/drawingml/2006/picture";
    private const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const string DiagramUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";

    /// <summary>The services a drawing needs from the reader: the part its relationships belong to and the story of a text box.</summary>
    internal interface IDrawingContext
    {
        OpenXmlPart Part { get; }

        /// <summary>The document's theme (scheme colours of DrawingML text are resolved with it).</summary>
        Theme Theme { get; }

        Story ReadTextBox(OpenXmlElement textBoxContent);
    }

    /// <summary>The anchor of a wp:anchor.</summary>
    public static DrawingAnchor ReadAnchor(OpenXmlElement anchor)
    {
        static double Distance(OpenXmlElement element, string name) => DocxValues.EmuToPoints(DocxValues.ParseInteger(DocxValues.AttributeValue(element, name)) ?? 0);
        var horizontal = Child(anchor, "positionH");
        var vertical = Child(anchor, "positionV");
        var wrap = anchor.ChildElements.FirstOrDefault(child => child.LocalName.StartsWith("wrap", StringComparison.Ordinal))?.LocalName switch
        {
            "wrapSquare" => DrawingWrap.Square,
            "wrapTight" => DrawingWrap.Tight,
            "wrapThrough" => DrawingWrap.Through,
            "wrapTopAndBottom" => DrawingWrap.TopAndBottom,
            _ => DrawingWrap.None,
        };
        var simple = DocxValues.AttributeValue(anchor, "simplePos") is "1" or "true";
        var simplePosition = Child(anchor, "simplePos");
        var (frameX, offsetX, alignX) = Position(horizontal, simple ? simplePosition : null, "x");
        var (frameY, offsetY, alignY) = Position(vertical, simple ? simplePosition : null, "y");
        return new DrawingAnchor(
            frameX, offsetX, alignX,
            frameY, offsetY, alignY,
            wrap,
            DocxValues.AttributeValue(anchor, "behindDoc") is "1" or "true",
            Distance(anchor, "distL"), Distance(anchor, "distT"), Distance(anchor, "distR"), Distance(anchor, "distB"),
            DocxValues.ParseInteger(DocxValues.AttributeValue(anchor, "relativeHeight")) ?? 0);
    }

    private static (DrawingFrame Frame, double Offset, string? Align) Position(OpenXmlElement? position, OpenXmlElement? simple, string axis)
    {
        if (simple is not null)
        {
            return (DrawingFrame.Page, DocxValues.EmuToPoints(DocxValues.ParseInteger(DocxValues.AttributeValue(simple, axis)) ?? 0), null);
        }

        if (position is null)
        {
            return (axis == "x" ? DrawingFrame.Column : DrawingFrame.Paragraph, 0, null);
        }

        var frame = DocxValues.AttributeValue(position, "relativeFrom") switch
        {
            "page" => DrawingFrame.Page,
            "margin" => DrawingFrame.Margin,
            "paragraph" => DrawingFrame.Paragraph,
            "line" => DrawingFrame.Line,
            "character" => DrawingFrame.Character,
            "leftMargin" => DrawingFrame.LeftMargin,
            "rightMargin" => DrawingFrame.RightMargin,
            "topMargin" => DrawingFrame.TopMargin,
            "bottomMargin" => DrawingFrame.BottomMargin,
            "insideMargin" => DrawingFrame.InsideMargin,
            "outsideMargin" => DrawingFrame.OutsideMargin,
            _ => axis == "x" ? DrawingFrame.Column : DrawingFrame.Paragraph,
        };
        var align = Child(position, "align")?.InnerText.Trim();
        var offset = Child(position, "posOffset")?.InnerText.Trim() ?? Child(position, "pctPosHOffset")?.InnerText ?? Child(position, "pctPosVOffset")?.InnerText;
        return (frame, DocxValues.EmuToPoints(DocxValues.ParseInteger(offset) ?? 0), string.IsNullOrEmpty(align) ? null : align);
    }

    /// <summary>
    /// What a run child holding a drawing (w:drawing, mc:AlternateContent, w:pict) shows, or null when ThixWrite cannot draw it
    /// (an OLE object, a canvas, an unknown graphic).
    /// </summary>
    public static DrawingGraphic? Read(OpenXmlElement runChild, IDrawingContext context)
    {
        try
        {
            var drawing = runChild.LocalName == "drawing" ? runChild : Choice(runChild);
            var container = Child(drawing, "inline") ?? Child(drawing, "anchor");
            if (container is not null)
            {
                return ReadContainer(container, context);
            }

            var pict = runChild.LocalName == "pict" ? runChild : Fallback(runChild);
            return pict is null ? null : ReadVml(pict, context);
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or InvalidOperationException or OverflowException or System.Xml.XmlException)
        {
            return null; // odd content: kept as it is, shown as a placeholder
        }
    }

    /// <summary>The w:drawing of the first mc:Choice that has one.</summary>
    private static OpenXmlElement? Choice(OpenXmlElement alternate) =>
        alternate.ChildElements.Where(child => child.LocalName == "Choice").SelectMany(choice => choice.ChildElements).FirstOrDefault(child => child.LocalName == "drawing");

    private static OpenXmlElement? Fallback(OpenXmlElement alternate) =>
        alternate.ChildElements.Where(child => child.LocalName == "Fallback").SelectMany(fallback => fallback.ChildElements).FirstOrDefault(child => child.LocalName == "pict");

    private static DrawingGraphic? ReadContainer(OpenXmlElement container, IDrawingContext context)
    {
        var extent = Child(container, "extent");
        var width = DocxValues.EmuToPoints(DocxValues.ParseInteger(extent is null ? null : DocxValues.AttributeValue(extent, "cx")) ?? 0);
        var height = DocxValues.EmuToPoints(DocxValues.ParseInteger(extent is null ? null : DocxValues.AttributeValue(extent, "cy")) ?? 0);
        var anchor = container.LocalName == "anchor" ? ReadAnchor(container) : null;
        var data = container.Descendants().FirstOrDefault(element => element.LocalName == "graphicData");
        if (data is null)
        {
            return null;
        }

        var uri = DocxValues.AttributeValue(data, "uri");
        var elements = new List<DrawingElement>();
        switch (uri)
        {
            case WordprocessingShapeUri when Child(data, "wsp") is { } shape:
                elements.Add(ReadShape(shape, new Frame(0, 0, width, height), context, ownTransform: false));
                break;
            case WordprocessingGroupUri when Child(data, "wgp") is { } group:
                ReadGroup(group, Transform.Identity(width, height), elements, context);
                break;
            case PictureUri when Child(data, "pic") is { } picture && ReadPicture(picture, new Frame(0, 0, width, height), context) is { } image:
                elements.Add(image);
                break;
            case ChartUri when Child(data, "chart") is { } chart && PartXml(context.Part, Relationship(chart, "id")) is { } xml:
                elements.Add(new DrawingChart(0, 0, width, height, xml));
                break;
            case DiagramUri when Child(data, "relIds") is { } ids:
                elements.AddRange(ReadSmartArt(ids, context.Part, context.Theme));
                break;
            default:
                return null;
        }

        return elements.Count == 0 ? null : new DrawingGraphic(width, height, anchor, new EquatableArray<DrawingElement>(elements));
    }

    // ---------------------------------------------------------------- shapes

    private readonly record struct Frame(double X, double Y, double Width, double Height);

    /// <summary>A group's mapping of its child coordinates (EMU) into the drawing's frame (points).</summary>
    private readonly record struct Transform(double OffsetX, double OffsetY, double ScaleX, double ScaleY)
    {
        public static Transform Identity(double width, double height) => new(0, 0, 1 / 12700.0, 1 / 12700.0);

        public Frame Map(double x, double y, double cx, double cy) => new(OffsetX + x * ScaleX, OffsetY + y * ScaleY, cx * ScaleX, cy * ScaleY);
    }

    private static void ReadGroup(OpenXmlElement group, Transform outer, List<DrawingElement> elements, IDrawingContext context)
    {
        var properties = Child(group, "grpSpPr");
        var transform = outer;
        if (properties is not null && Child(properties, "xfrm") is { } xfrm)
        {
            var (x, y, cx, cy) = Box(xfrm);
            var childOffset = Child(xfrm, "chOff");
            var childExtent = Child(xfrm, "chExt");
            var chX = Emu(childOffset, "x");
            var chY = Emu(childOffset, "y");
            var chCx = Emu(childExtent, "cx");
            var chCy = Emu(childExtent, "cy");
            var frame = outer.Map(x, y, cx, cy);
            var scaleX = chCx > 0 ? frame.Width / chCx : outer.ScaleX;
            var scaleY = chCy > 0 ? frame.Height / chCy : outer.ScaleY;
            transform = new Transform(frame.X - chX * scaleX, frame.Y - chY * scaleY, scaleX, scaleY);
        }

        foreach (var child in group.ChildElements)
        {
            switch (child.LocalName)
            {
                case "wsp":
                    elements.Add(ReadShape(child, FrameOf(child, "spPr", transform), context, ownTransform: true));
                    break;
                case "pic" when ReadPicture(child, FrameOf(child, "spPr", transform), context) is { } picture:
                    elements.Add(picture);
                    break;
                case "grpSp":
                    ReadGroup(child, transform, elements, context);
                    break;
            }
        }
    }

    private static Frame FrameOf(OpenXmlElement shape, string propertiesName, Transform transform)
    {
        var xfrm = Child(shape, propertiesName) is { } properties ? Child(properties, "xfrm") : null;
        if (xfrm is null)
        {
            return transform.Map(0, 0, 0, 0);
        }

        var (x, y, cx, cy) = Box(xfrm);
        return transform.Map(x, y, cx, cy);
    }

    private static (double X, double Y, double Cx, double Cy) Box(OpenXmlElement xfrm)
    {
        var offset = Child(xfrm, "off");
        var extent = Child(xfrm, "ext");
        return (Emu(offset, "x"), Emu(offset, "y"), Emu(extent, "cx"), Emu(extent, "cy"));
    }

    private static double Emu(OpenXmlElement? element, string name) => element is null ? 0 : DocxValues.ParseInteger(DocxValues.AttributeValue(element, name)) ?? 0;

    /// <summary>A wps:wsp (or dsp:sp): geometry, fill, line (from spPr, else the shape style's references), text.</summary>
    private static DrawingShape ReadShape(OpenXmlElement shape, Frame frame, IDrawingContext context, bool ownTransform)
    {
        var properties = Child(shape, "spPr");
        var style = Child(shape, "style");
        var result = new DrawingShape(frame.X, frame.Y, frame.Width, frame.Height);
        if (properties is not null)
        {
            if (Child(properties, "xfrm") is { } xfrm)
            {
                result = result with
                {
                    Rotation = (DocxValues.ParseInteger(DocxValues.AttributeValue(xfrm, "rot")) ?? 0) / 60000.0,
                    FlipHorizontal = DocxValues.AttributeValue(xfrm, "flipH") is "1" or "true",
                    FlipVertical = DocxValues.AttributeValue(xfrm, "flipV") is "1" or "true",
                };
            }

            if (Child(properties, "prstGeom") is { } preset)
            {
                result = result with { Preset = DocxValues.AttributeValue(preset, "prst") ?? "rect", Adjustments = Adjustments(preset) };
            }
            else if (Child(properties, "custGeom") is { } custom)
            {
                result = result with { CustomGeometryXml = custom.OuterXml };
            }

            result = result with { Fill = ReadFill(properties) ?? StyleColor(style, "fillRef", fill: true), Line = ReadLine(properties, style), Shadow = ReadShadow(properties) };
            if (properties.ChildElements.Any(child => child.LocalName == "noFill"))
            {
                result = result with { Fill = null };
            }
        }

        var body = Child(shape, "bodyPr") ?? Child(Child(shape, "txBody"), "bodyPr");
        if (body is not null)
        {
            double Inset(string name, double fallback) => DocxValues.AttributeValue(body, name) is { } value ? DocxValues.EmuToPoints(DocxValues.ParseInteger(value) ?? 0) : fallback;
            result = result with
            {
                Insets = (Inset("lIns", 7.2), Inset("tIns", 3.6), Inset("rIns", 7.2), Inset("bIns", 3.6)),
                TextAnchor = DocxValues.AttributeValue(body, "anchor") switch { "ctr" => DrawingTextAnchor.Center, "b" => DrawingTextAnchor.Bottom, _ => DrawingTextAnchor.Top },
                AutoFit = body.ChildElements.Any(child => child.LocalName == "spAutoFit"),
            };
        }

        if (Child(shape, "txbx") is { } textBox && Child(textBox, "txbxContent") is { } content)
        {
            result = result with { Text = context.ReadTextBox(content) };
        }
        else if (Child(shape, "txBody") is { } drawingText)
        {
            result = result with { Text = DrawingText(drawingText, style, context.Theme) };
        }

        return result;
    }

    private static EquatableArray<GeometryAdjustment> Adjustments(OpenXmlElement geometry)
    {
        var list = Child(geometry, "avLst");
        if (list is null)
        {
            return EquatableArray<GeometryAdjustment>.Empty;
        }

        var result = new List<GeometryAdjustment>();
        foreach (var guide in list.ChildElements.Where(child => child.LocalName == "gd"))
        {
            var formula = DocxValues.AttributeValue(guide, "fmla") ?? "";
            if (DocxValues.AttributeValue(guide, "name") is { } name && formula.StartsWith("val ", StringComparison.Ordinal)
                && long.TryParse(formula[4..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
            {
                result.Add(new GeometryAdjustment(name, value));
            }
        }

        return new EquatableArray<GeometryAdjustment>(result);
    }

    private static DrawingFill? ReadFill(OpenXmlElement properties)
    {
        foreach (var child in properties.ChildElements)
        {
            switch (child.LocalName)
            {
                case "solidFill" when ReadColor(child) is { } color:
                    return new DrawingFill(color);
                case "gradFill":
                    var stops = Child(child, "gsLst")?.ChildElements.Where(stop => stop.LocalName == "gs")
                        .Select(stop => (Position: (DocxValues.ParseInteger(DocxValues.AttributeValue(stop, "pos")) ?? 0) / 100000.0, Color: ReadColor(stop)))
                        .Where(stop => stop.Color is not null)
                        .Select(stop => (stop.Position, stop.Color!))
                        .ToList() ?? [];
                    var angle = Child(child, "lin") is { } linear ? (DocxValues.ParseInteger(DocxValues.AttributeValue(linear, "ang")) ?? 5400000) / 60000.0 : 90;
                    return stops.Count == 0 ? null : new DrawingFill(stops[stops.Count / 2].Item2, new EquatableArray<(double, DrawingColor)>(stops), angle);
            }
        }

        return null;
    }

    /// <summary>The outer shadow of a shape's or picture's properties (a:effectLst/a:outerShdw), or null.</summary>
    public static DrawingShadow? ReadShadow(OpenXmlElement? properties)
    {
        if (Child(Child(properties, "effectLst"), "outerShdw") is not { } shadow || ReadColor(shadow) is not { } color)
        {
            return null;
        }

        double Emu(string name) => DocxValues.EmuToPoints(DocxValues.ParseInteger(DocxValues.AttributeValue(shadow, name)) ?? 0);
        return new DrawingShadow(color, Emu("blurRad"), Emu("dist"), (DocxValues.ParseInteger(DocxValues.AttributeValue(shadow, "dir")) ?? 0) / 60000.0);
    }

    private static DrawingLine? ReadLine(OpenXmlElement properties, OpenXmlElement? style)
    {
        var line = Child(properties, "ln");
        DrawingColor? color = null;
        var width = 0.75;
        var dash = "solid";
        if (line is not null)
        {
            if (line.ChildElements.Any(child => child.LocalName == "noFill"))
            {
                return null;
            }

            width = DocxValues.AttributeValue(line, "w") is { } w ? DocxValues.EmuToPoints(DocxValues.ParseInteger(w) ?? 9525) : width;
            color = Child(line, "solidFill") is { } fill ? ReadColor(fill) : null;
            dash = Child(line, "prstDash") is { } preset ? DocxValues.AttributeValue(preset, "val") ?? dash : dash;
        }

        color ??= StyleColor(style, "lnRef", fill: false)?.Color;
        return color is null ? null : new DrawingLine(color, width, dash);
    }

    /// <summary>The colour a shape style reference (wps:style a:fillRef / a:lnRef with idx &gt; 0) gives, as a fill.</summary>
    private static DrawingFill? StyleColor(OpenXmlElement? style, string reference, bool fill)
    {
        if (style is null || Child(style, reference) is not { } element || (DocxValues.ParseInteger(DocxValues.AttributeValue(element, "idx")) ?? 0) == 0)
        {
            return null;
        }

        return ReadColor(element) is { } color ? new DrawingFill(color) : null;
    }

    /// <summary>The colour inside an element (a:srgbClr, a:schemeClr, a:sysClr, a:prstClr with their transforms).</summary>
    public static DrawingColor? ReadColor(OpenXmlElement holder)
    {
        foreach (var child in holder.ChildElements)
        {
            var modifiers = new EquatableArray<(string, int?)>(child.ChildElements.Select(modifier =>
                (modifier.LocalName, (int?)DocxValues.ParseInteger(DocxValues.AttributeValue(modifier, "val")))));
            switch (child.LocalName)
            {
                case "srgbClr":
                    return new DrawingColor(DocxValues.AttributeValue(child, "val"), null, modifiers);
                case "schemeClr":
                    return new DrawingColor(null, DocxValues.AttributeValue(child, "val"), modifiers);
                case "sysClr":
                    return new DrawingColor(DocxValues.AttributeValue(child, "lastClr") ?? "000000", null, modifiers);
                case "prstClr":
                    return new DrawingColor(DocxValues.AttributeValue(child, "val") switch { "white" => "FFFFFF", "red" => "FF0000", "blue" => "0000FF", "green" => "008000", _ => "000000" }, null, modifiers);
                case "scrgbClr":
                    return new DrawingColor("000000", null, modifiers);
            }
        }

        return null;
    }

    private static DrawingPicture? ReadPicture(OpenXmlElement picture, Frame frame, IDrawingContext context)
    {
        var blip = picture.Descendants().FirstOrDefault(element => element.LocalName == "blip");
        if (blip is null || Relationship(blip, "embed") is not { } id)
        {
            return null;
        }

        try
        {
            if (context.Part.GetPartById(id) is ImagePart part && ImageData.FromBytes(Bytes(part)) is { } image)
            {
                return new DrawingPicture(frame.X, frame.Y, frame.Width, frame.Height, image);
            }
        }
        catch (ArgumentOutOfRangeException)
        {
        }

        return null;
    }

    // ---------------------------------------------------------------- SmartArt

    /// <summary>The shapes of a SmartArt's drawing part (dsp:drawing, Word's pre-drawn copy), found through its data part.</summary>
    private static IEnumerable<DrawingElement> ReadSmartArt(OpenXmlElement ids, OpenXmlPart part, Theme theme)
    {
        try
        {
            if (Relationship(ids, "dm") is not { } dataId || part.GetPartById(dataId) is not { } data)
            {
                return [];
            }

            var dataRoot = Root(data);
            var drawingId = dataRoot?.Descendants().FirstOrDefault(element => element.LocalName == "dataModelExt") is { } extension
                ? DocxValues.AttributeValue(extension, "relId")
                : null;
            if (drawingId is null || part.GetPartById(drawingId) is not { } drawing || Root(drawing) is not { } root)
            {
                return [];
            }

            var tree = root.Descendants().FirstOrDefault(element => element.LocalName == "spTree");
            if (tree is null)
            {
                return [];
            }

            var context = new NoTextContext(part, theme);
            return tree.ChildElements.Where(child => child.LocalName == "sp")
                .Select(shape => (DrawingElement)ReadShape(shape, FrameOf(shape, "spPr", new Transform(0, 0, 1 / 12700.0, 1 / 12700.0)), context, ownTransform: true))
                .ToList();
        }
        catch (ArgumentOutOfRangeException)
        {
            return [];
        }
    }

    private sealed class NoTextContext(OpenXmlPart part, Theme theme) : IDrawingContext
    {
        public OpenXmlPart Part { get; } = part;

        public Theme Theme { get; } = theme;

        public Story ReadTextBox(OpenXmlElement textBoxContent) => new([]);
    }

    /// <summary>DrawingML text (a:p / a:r / a:t with a:rPr sz, b, i, solidFill, a:pPr algn) as ThixWrite paragraphs.</summary>
    private static Story DrawingText(OpenXmlElement body, OpenXmlElement? style, Theme theme)
    {
        var defaultColor = style is not null && Child(style, "fontRef") is { } fontReference && ReadColor(fontReference) is { } reference ? Resolve(reference, theme) : null;
        var blocks = new List<Block>();
        foreach (var paragraph in body.ChildElements.Where(child => child.LocalName == "p"))
        {
            var alignment = Child(paragraph, "pPr") is { } pPr ? DocxValues.AttributeValue(pPr, "algn") : null;
            var inlines = new List<Inline>();
            foreach (var run in paragraph.ChildElements.Where(child => child.LocalName is "r" or "fld"))
            {
                var text = Child(run, "t")?.InnerText ?? "";
                var properties = defaultColor is null ? CharacterProperties.Empty : new CharacterProperties { Color = TextColor.FromHex(defaultColor) };
                if (Child(run, "rPr") is { } rPr)
                {
                    if (DocxValues.ParseInteger(DocxValues.AttributeValue(rPr, "sz")) is { } size)
                    {
                        properties = properties with { FontSize = size / 100.0 };
                    }

                    properties = properties with
                    {
                        Bold = DocxValues.AttributeValue(rPr, "b") is "1" or "true" ? true : null,
                        Italic = DocxValues.AttributeValue(rPr, "i") is "1" or "true" ? true : null,
                    };
                    if (Child(rPr, "solidFill") is { } fill && ReadColor(fill) is { } color && Resolve(color, theme) is { } rgb)
                    {
                        properties = properties with { Color = TextColor.FromHex(rgb) };
                    }
                }

                inlines.Add(new TextRun(text, properties));
            }

            var paragraphProperties = new ParagraphProperties
            {
                Alignment = alignment switch { "ctr" => ParagraphAlignment.Center, "r" => ParagraphAlignment.Right, "just" => ParagraphAlignment.Justify, _ => ParagraphAlignment.Left },
                SpacingBefore = 0,
                SpacingAfter = 0,
                LineSpacing = LineSpacing.Single,
            };
            blocks.Add(new Paragraph(inlines, paragraphProperties));
        }

        return new Story(blocks);
    }

    /// <summary>A drawing colour as "RRGGBB" with the theme's scheme (DrawingML's tx1/bg1 … names map onto it).</summary>
    public static string? Resolve(DrawingColor color, Theme theme)
    {
        var baseHex = color.Rgb ?? (color.Scheme is { } scheme ? theme.ColorOf(SchemeName(scheme)) : null);
        if (baseHex is not { Length: 6 })
        {
            return null;
        }

        var rgb = ThixOffice.Text.Drawing.ColorTransforms.Apply(ThixOffice.Text.Drawing.RgbColor.FromHex(baseHex), color.Modifiers);
        return $"{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
    }

    /// <summary>The theme slot of a DrawingML scheme colour name (tx1 → dk1, bg1 → lt1 …).</summary>
    public static string SchemeName(string scheme) => scheme switch
    {
        "tx1" or "dk1" => "dk1",
        "tx2" or "dk2" => "dk2",
        "bg1" or "lt1" => "lt1",
        "bg2" or "lt2" => "lt2",
        "phClr" => "accent1",
        _ => scheme,
    };

    // ---------------------------------------------------------------- VML

    /// <summary>A VML text box or filled rectangle of a w:pict (Word 2007 files, and the fallback of every Word 2010+ shape).</summary>
    private static DrawingGraphic? ReadVml(OpenXmlElement pict, IDrawingContext context)
    {
        var shape = pict.ChildElements.FirstOrDefault(child => child.LocalName is "shape" or "rect" or "roundrect" or "oval");
        if (shape is null)
        {
            return null;
        }

        var style = Style(DocxValues.AttributeValue(shape, "style"));
        var width = Length(style.GetValueOrDefault("width"));
        var height = Length(style.GetValueOrDefault("height"));
        if (width <= 0 || height <= 0)
        {
            return null;
        }

        var result = new DrawingShape(0, 0, width, height)
        {
            Preset = shape.LocalName switch { "oval" => "ellipse", "roundrect" => "roundRect", _ => "rect" },
            Fill = DocxValues.AttributeValue(shape, "filled") is "f" or "false" ? null
                : VmlColor(DocxValues.AttributeValue(shape, "fillcolor")) is { } fill ? new DrawingFill(fill) : null,
            Line = DocxValues.AttributeValue(shape, "stroked") is "f" or "false" ? null
                : new DrawingLine(VmlColor(DocxValues.AttributeValue(shape, "strokecolor")) ?? DrawingColor.Of("000000"), Length(DocxValues.AttributeValue(shape, "strokeweight") ?? "0.75pt")),
        };
        if (shape.ChildElements.FirstOrDefault(child => child.LocalName == "textbox") is { } textBox
            && textBox.ChildElements.FirstOrDefault(child => child.LocalName == "txbxContent") is { } content)
        {
            result = result with { Text = context.ReadTextBox(content) };
        }

        DrawingAnchor? anchor = null;
        if (style.GetValueOrDefault("position") == "absolute")
        {
            anchor = new DrawingAnchor(
                style.GetValueOrDefault("mso-position-horizontal-relative") switch { "page" => DrawingFrame.Page, "margin" => DrawingFrame.Margin, _ => DrawingFrame.Column },
                Length(style.GetValueOrDefault("margin-left")),
                style.GetValueOrDefault("mso-position-horizontal") is { } horizontal && horizontal != "absolute" ? horizontal : null,
                style.GetValueOrDefault("mso-position-vertical-relative") switch { "page" => DrawingFrame.Page, "margin" => DrawingFrame.Margin, "line" => DrawingFrame.Line, _ => DrawingFrame.Paragraph },
                Length(style.GetValueOrDefault("margin-top")),
                style.GetValueOrDefault("mso-position-vertical") is { } vertical && vertical != "absolute" ? vertical : null,
                pict.Descendants().Any(element => element.LocalName == "wrap" && DocxValues.AttributeValue(element, "type") == "topAndBottom") ? DrawingWrap.TopAndBottom
                    : pict.Descendants().Any(element => element.LocalName == "wrap" && DocxValues.AttributeValue(element, "type") is "square" or "tight") ? DrawingWrap.Square : DrawingWrap.None,
                (DocxValues.ParseInteger(style.GetValueOrDefault("z-index")) ?? 0) < 0);
        }

        return new DrawingGraphic(width, height, anchor, [result]);
    }

    private static Dictionary<string, string> Style(string? style) =>
        (style ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries)
            .Select(pair => pair.Split(':', 2))
            .Where(pair => pair.Length == 2)
            .GroupBy(pair => pair[0].Trim(), StringComparer.OrdinalIgnoreCase)
            .ToDictionary(group => group.Key, group => group.Last()[1].Trim(), StringComparer.OrdinalIgnoreCase);

    /// <summary>A CSS length of VML in points ("12pt", "1.5in", "2cm", "30mm", "96px", "12" = pixels).</summary>
    private static double Length(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        value = value.Trim();
        var unit = new string(value.SkipWhile(character => char.IsDigit(character) || character is '.' or '-' or '+').ToArray());
        var number = value[..^unit.Length];
        if (!double.TryParse(number, NumberStyles.Float, CultureInfo.InvariantCulture, out var amount))
        {
            return 0;
        }

        return unit switch
        {
            "pt" => amount,
            "in" => amount * 72,
            "cm" => amount * 72 / 2.54,
            "mm" => amount * 72 / 25.4,
            "pc" => amount * 12,
            "emu" => amount / 12700,
            _ => amount * 0.75,
        };
    }

    private static DrawingColor? VmlColor(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var hex = value.Split(' ')[0].Trim().TrimStart('#');
        return hex.Length == 6 && hex.All(Uri.IsHexDigit) ? DrawingColor.Of(hex.ToUpperInvariant())
            : hex.Length == 3 && hex.All(Uri.IsHexDigit) ? DrawingColor.Of(string.Concat(hex.Select(character => $"{character}{character}")).ToUpperInvariant())
            : hex switch { "white" => DrawingColor.Of("FFFFFF"), "black" => DrawingColor.Of("000000"), _ => null };
    }

    // ---------------------------------------------------------------- helpers

    private static OpenXmlElement? Child(OpenXmlElement? element, string localName) =>
        element?.ChildElements.FirstOrDefault(child => child.LocalName == localName);

    private static string? Relationship(OpenXmlElement element, string localName) =>
        element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == localName && attribute.NamespaceUri == DocxValues.RelationshipNamespace).Value;

    private static string? PartXml(OpenXmlPart part, string? id)
    {
        if (id is null)
        {
            return null;
        }

        try
        {
            using var stream = part.GetPartById(id).GetStream(FileMode.Open, FileAccess.Read);
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    private static OpenXmlElement? Root(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var reader = System.Xml.XmlReader.Create(stream, new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit });
        reader.MoveToContent();
        return Parse(reader.ReadOuterXml());
    }

    private static OpenXmlElement Parse(string xml)
    {
        var holder = new OpenXmlUnknownElement("x", "holder", "urn:thixwrite:holder");
        holder.InnerXml = xml;
        return holder.FirstChild!;
    }

    private static byte[] Bytes(OpenXmlPart part)
    {
        using var stream = part.GetStream(FileMode.Open, FileAccess.Read);
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }
}
