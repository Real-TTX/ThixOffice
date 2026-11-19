using System.Xml.Linq;
using ThixShow.Core.Model;
using static ThixShow.Core.Formats.Pptx.Ns;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>The writer's side of a part: links for new content, and remapping of relationship ids in kept XML.</summary>
internal interface IPartWriter : IWriteLinks
{
    /// <summary>Points the relationship ids inside <paramref name="element"/> (kept XML) at this part's relationships.</summary>
    XElement Relink(XElement element, ValueList<PartReference> references);
}

/// <summary>
/// Reads and writes the shape tree (p:spTree's p:sp, p:cxnSp, p:pic, p:grpSp, p:graphicFrame; anything else is kept). Writing an
/// element starts from the XML it was read from (relinked into the target part): unchanged elements are written as read; changed
/// ones get their owned parts replaced (non-visual properties, transform, geometry, fill, line, effects, style, text, table) while
/// everything else in them stays.
/// </summary>
internal static class ShapeXml
{
    private static readonly string[] SpOrder = ["nvSpPr", "spPr", "style", "txBody", "extLst"];
    private static readonly string[] PicOrder = ["nvPicPr", "blipFill", "spPr", "style", "extLst"];
    private static readonly string[] CxnOrder = ["nvCxnSpPr", "spPr", "style", "extLst"];
    private static readonly string[] NonVisualOrder = ["cNvPr", "cNvSpPr", "cNvCxnSpPr", "cNvPicPr", "cNvGrpSpPr", "cNvGraphicFramePr", "nvPr"];
    private static readonly string[] CNvPrOrder = ["hlinkClick", "hlinkHover", "extLst"];
    private static readonly string[] NvPrOrder = ["ph", "audioCd", "wavAudioFile", "audioFile", "videoFile", "quickTimeFile", "custDataLst", "extLst"];
    private static readonly string[] CellOrder = ["lnL", "lnR", "lnT", "lnB", "lnTlToBr", "lnBlToTr", "cell3D", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "headers", "extLst"];

    // ---------------------------------------------------------------- reading

    /// <summary>Reads the shapes of a p:spTree / p:grpSp (after its group properties).</summary>
    public static ValueList<SlideElement> ReadShapes(XElement tree, IReadLinks links, Func<XElement, ValueList<PartReference>> references) =>
        tree.Elements()
            .Where(element => element.Name.LocalName is not ("nvGrpSpPr" or "grpSpPr" or "extLst"))
            .Select(element => ReadElement(element, links, references))
            .ToList();

    public static SlideElement ReadElement(XElement element, IReadLinks links, Func<XElement, ValueList<PartReference>> references)
    {
        var source = XmlFragment.From(element);
        var refs = references(element);
        if (element.Name == Mc + "AlternateContent")
        {
            // A choice ThixShow cannot know about: shown from the fallback (or the first choice), written back whole while unchanged.
            var inner = element.Element(Mc + "Fallback")?.Elements().FirstOrDefault() ?? element.Elements(Mc + "Choice").FirstOrDefault()?.Elements().FirstOrDefault();
            if (inner is not null && inner.Name.Namespace == P && inner.Name.LocalName is "sp" or "pic" or "graphicFrame" or "grpSp" or "cxnSp")
            {
                return ReadElement(inner, links, references) with { Source = source, References = refs };
            }

            return new PreservedElement { Source = source, References = refs, Id = 0, Name = "" };
        }

        if (element.Name.Namespace != P)
        {
            return new PreservedElement { Source = source, References = refs };
        }

        return element.Name.LocalName switch
        {
            "sp" => ReadShape(element, links) with { Source = source, References = refs },
            "cxnSp" => ReadConnector(element, links) with { Source = source, References = refs },
            "pic" => ReadPicture(element, links) with { Source = source, References = refs },
            "grpSp" => ReadGroup(element, links, references) with { Source = source, References = refs },
            "graphicFrame" => ReadFrame(element, links) with { Source = source, References = refs },
            _ => NonVisual(new PreservedElement { Source = source, References = refs }, element.Elements().FirstOrDefault(), links),
        };
    }

    private static T NonVisual<T>(T shape, XElement? nonVisual, IReadLinks links)
        where T : SlideElement
    {
        var properties = nonVisual?.Element(P + "cNvPr");
        var nvPr = nonVisual?.Element(P + "nvPr");
        return shape with
        {
            Id = properties.Int("id") ?? 0,
            Name = properties.Attr("name") ?? "",
            Description = properties.Attr("descr"),
            AltTitle = properties.Attr("title"),
            Hidden = properties.Bool("hidden") ?? false,
            Click = DrawingXml.ReadHyperlink(properties?.Element(A + "hlinkClick"), links),
            Hover = DrawingXml.ReadHyperlink(properties?.Element(A + "hlinkHover"), links),
            Placeholder = ReadPlaceholder(nvPr?.Element(P + "ph")),
        };
    }

    public static PlaceholderInfo? ReadPlaceholder(XElement? ph) => ph is null ? null : new PlaceholderInfo(
        ph.Attr("type") switch
        {
            "title" => PlaceholderType.Title,
            "body" => PlaceholderType.Body,
            "ctrTitle" => PlaceholderType.CenteredTitle,
            "subTitle" => PlaceholderType.Subtitle,
            "dt" => PlaceholderType.DateAndTime,
            "sldNum" => PlaceholderType.SlideNumber,
            "ftr" => PlaceholderType.Footer,
            "hdr" => PlaceholderType.Header,
            "chart" => PlaceholderType.Chart,
            "tbl" => PlaceholderType.Table,
            "clipArt" => PlaceholderType.ClipArt,
            "dgm" => PlaceholderType.Diagram,
            "media" => PlaceholderType.Media,
            "sldImg" => PlaceholderType.SlideImage,
            "pic" => PlaceholderType.Picture,
            _ => PlaceholderType.Object,
        },
        ph.UInt("idx"),
        ph.Attr("orient"),
        ph.Attr("sz"),
        ph.Bool("hasCustomPrompt") ?? false);

    private static ShapeStyle? ReadStyle(XElement? style)
    {
        if (style is null)
        {
            return null;
        }

        StyleReference Reference(string name) => new(style.Element(A + name).Int("idx") ?? 0, DrawingXml.ReadColor(style.Element(A + name)));
        var font = style.Element(A + "fontRef");
        return new ShapeStyle(Reference("lnRef"), Reference("fillRef"), Reference("effectRef"), new FontReference(font.Attr("idx") ?? "minor", DrawingXml.ReadColor(font)));
    }

    private static AutoShape ReadShape(XElement element, IReadLinks links)
    {
        var nonVisual = element.Element(P + "nvSpPr");
        var properties = element.Element(P + "spPr");
        var locks = nonVisual?.Element(P + "cNvSpPr");
        var shape = NonVisual(new AutoShape(), nonVisual, links) with
        {
            IsTextBox = locks.Bool("txBox") ?? false,
            ShapeLocks = locks is { } cNvSpPr && (cNvSpPr.HasElements || cNvSpPr.Attributes().Any(attribute => attribute.Name.LocalName != "txBox")) ? XmlFragment.From(cNvSpPr) : null,
            Transform = DrawingXml.ReadTransform(properties?.Element(A + "xfrm")),
            Geometry = DrawingXml.ReadGeometry(properties),
            Fill = DrawingXml.ReadFill(properties, links),
            Outline = DrawingXml.ReadOutline(properties?.Element(A + "ln"), links),
            Effects = DrawingXml.ReadEffects(properties),
            Style = ReadStyle(element.Element(P + "style")),
            Text = element.Element(P + "txBody") is { } body ? DrawingXml.ReadTextBody(body, links) : null,
        };
        return shape;
    }

    private static ConnectorShape ReadConnector(XElement element, IReadLinks links)
    {
        var nonVisual = element.Element(P + "nvCxnSpPr");
        var properties = element.Element(P + "spPr");
        var connection = nonVisual?.Element(P + "cNvCxnSpPr");
        Connection? Read(string name) => connection?.Element(A + name) is { } end ? new Connection(end.Int("id") ?? 0, end.Int("idx") ?? 0) : null;
        return NonVisual(new ConnectorShape(), nonVisual, links) with
        {
            Transform = DrawingXml.ReadTransform(properties?.Element(A + "xfrm")),
            Geometry = DrawingXml.ReadGeometry(properties) ?? ShapeGeometry.Of("line"),
            Fill = DrawingXml.ReadFill(properties, links),
            Outline = DrawingXml.ReadOutline(properties?.Element(A + "ln"), links),
            Effects = DrawingXml.ReadEffects(properties),
            Style = ReadStyle(element.Element(P + "style")),
            Start = Read("stCxn"),
            End = Read("endCxn"),
        };
    }

    private static PictureShape ReadPicture(XElement element, IReadLinks links)
    {
        var nonVisual = element.Element(P + "nvPicPr");
        var properties = element.Element(P + "spPr");
        var nvPr = nonVisual?.Element(P + "nvPr");
        var media = nvPr?.Element(A + "videoFile") is not null || nvPr?.Element(A + "quickTimeFile") is not null ? MediaKind.Video
            : nvPr?.Element(A + "audioFile") is not null || nvPr?.Element(A + "wavAudioFile") is not null ? MediaKind.Audio
            : nvPr?.Descendants(P14 + "media").Any() == true ? MediaKind.Video : MediaKind.None;
        var locks = nonVisual?.Element(P + "cNvPicPr");
        var blipFill = element.Element(P + "blipFill");
        return NonVisual(new PictureShape { Picture = blipFill is null ? new PictureFill(null, RelativeRect.Zero, StretchMode.Full) : DrawingXml.ReadPictureFill(blipFill, links) }, nonVisual, links) with
        {
            Transform = DrawingXml.ReadTransform(properties?.Element(A + "xfrm")),
            Geometry = DrawingXml.ReadGeometry(properties) ?? ShapeGeometry.Rectangle,
            Outline = DrawingXml.ReadOutline(properties?.Element(A + "ln"), links),
            Effects = DrawingXml.ReadEffects(properties),
            Style = ReadStyle(element.Element(P + "style")),
            Media = media,
            PictureLocks = locks is { } cNvPicPr && (cNvPicPr.HasElements || cNvPicPr.HasAttributes) ? XmlFragment.From(cNvPicPr) : null,
        };
    }

    private static GroupShape ReadGroup(XElement element, IReadLinks links, Func<XElement, ValueList<PartReference>> references)
    {
        var properties = element.Element(P + "grpSpPr");
        return NonVisual(new GroupShape(), element.Element(P + "nvGrpSpPr"), links) with
        {
            Transform = DrawingXml.ReadTransform(properties?.Element(A + "xfrm")),
            Fill = DrawingXml.ReadFill(properties, links),
            Effects = DrawingXml.ReadEffects(properties),
            Children = ReadShapes(element, links, references),
        };
    }

    private static GraphicFrame ReadFrame(XElement element, IReadLinks links)
    {
        var data = element.Element(A + "graphic")?.Element(A + "graphicData");
        var uri = data.Attr("uri");
        var kind = uri switch
        {
            TableUri => GraphicKind.Table,
            ChartUri => GraphicKind.Chart,
            DiagramUri => GraphicKind.Diagram,
            OleUri => GraphicKind.OleObject,
            _ when data?.Descendants().Any(child => child.Name.LocalName == "chart") == true => GraphicKind.Chart,
            _ => GraphicKind.Other,
        };
        ImageData? fallback = null;
        if (kind == GraphicKind.OleObject || kind == GraphicKind.Other)
        {
            var blip = data?.Descendants(A + "blip").FirstOrDefault()?.Attribute(R + "embed")?.Value;
            fallback = blip is null ? null : links.ImageOf(blip);
        }

        return NonVisual(new GraphicFrame(), element.Element(P + "nvGraphicFramePr"), links) with
        {
            Transform = DrawingXml.ReadTransform(element.Element(P + "xfrm")),
            Kind = kind,
            GraphicUri = uri,
            Table = kind == GraphicKind.Table && data?.Element(A + "tbl") is { } table ? ReadTable(table, links) : null,
            ChartPartUri = kind == GraphicKind.Chart && data?.Descendants().FirstOrDefault(child => child.Name.LocalName == "chart")?.Attribute(R + "id")?.Value is { } chartId ? links.PartUriOf(chartId) : null,
            Fallback = fallback,
        };
    }

    public static Table ReadTable(XElement table, IReadLinks links)
    {
        var properties = table.Element(A + "tblPr");
        var extra = properties?.Without("noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "tableStyleId");
        foreach (var name in new[] { "firstRow", "lastRow", "firstCol", "lastCol", "bandRow", "bandCol", "rtl" })
        {
            extra?.Attribute(name)?.Remove();
        }

        var tableProperties = new TableProperties(
            properties?.Element(A + "tableStyleId")?.Value,
            properties.Bool("firstRow") ?? false,
            properties.Bool("lastRow") ?? false,
            properties.Bool("firstCol") ?? false,
            properties.Bool("lastCol") ?? false,
            properties.Bool("bandRow") ?? false,
            properties.Bool("bandCol") ?? false,
            properties.Bool("rtl") ?? false,
            DrawingXml.ReadFill(properties, links),
            extra is { } rest && (rest.HasElements || rest.HasAttributes) ? XmlFragment.From(rest) : null);
        var widths = table.Element(A + "tblGrid")?.Elements(A + "gridCol").Select(column => column.Long("w") ?? 0).ToList() ?? [];
        var rows = table.Elements(A + "tr").Select(row =>
        {
            var cells = row.Elements(A + "tc").Select(cell => ReadCell(cell, links)).ToList();
            var rowExtra = row.Element(A + "extLst");
            return new TableRow(row.Long("h") ?? 0, cells, rowExtra is null ? null : XmlFragment.From(rowExtra));
        }).ToList();
        return new Table(tableProperties, widths, rows);
    }

    private static TableCell ReadCell(XElement cell, IReadLinks links)
    {
        var properties = cell.Element(A + "tcPr");
        var extra = properties?.Without("lnL", "lnR", "lnT", "lnB", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill");
        foreach (var name in new[] { "marL", "marR", "marT", "marB", "anchor", "vert" })
        {
            extra?.Attribute(name)?.Remove();
        }

        var cellProperties = new TableCellProperties(
            properties.Long("marL"),
            properties.Long("marR"),
            properties.Long("marT"),
            properties.Long("marB"),
            properties.Attr("anchor") switch
            {
                "t" => TextAnchor.Top,
                "ctr" => TextAnchor.Center,
                "b" => TextAnchor.Bottom,
                _ => null,
            },
            properties.Attr("vert"),
            DrawingXml.ReadOutline(properties?.Element(A + "lnL"), links),
            DrawingXml.ReadOutline(properties?.Element(A + "lnR"), links),
            DrawingXml.ReadOutline(properties?.Element(A + "lnT"), links),
            DrawingXml.ReadOutline(properties?.Element(A + "lnB"), links),
            DrawingXml.ReadFill(properties, links),
            extra is { } rest && (rest.HasElements || rest.HasAttributes) ? XmlFragment.From(rest) : null);
        var text = cell.Element(A + "txBody") is { } body ? DrawingXml.ReadTextBody(body, links) : TextBody.Empty;
        return new TableCell(text, cellProperties, cell.Int("gridSpan") ?? 1, cell.Int("rowSpan") ?? 1, cell.Bool("hMerge") ?? false, cell.Bool("vMerge") ?? false);
    }

    // ---------------------------------------------------------------- writing

    /// <summary>Writes the shapes into a tree element (after its group properties, replacing the old shapes).</summary>
    public static void WriteShapes(XElement tree, IEnumerable<SlideElement> shapes, IPartWriter writer, Func<SlideElement, bool> unchanged)
    {
        foreach (var old in tree.Elements().Where(element => element.Name.LocalName is not ("nvGrpSpPr" or "grpSpPr" or "extLst")).ToList())
        {
            old.Remove();
        }

        var extensions = tree.Element(P + "extLst");
        foreach (var shape in shapes)
        {
            var element = WriteElement(shape, writer, unchanged);
            if (element is null)
            {
                continue;
            }

            if (extensions is null)
            {
                tree.Add(element);
            }
            else
            {
                extensions.AddBeforeSelf(element);
            }
        }
    }

    /// <summary>The element's XML: as read when <paramref name="unchanged"/>, else patched or new; null for an unwritable element.</summary>
    public static XElement? WriteElement(SlideElement shape, IPartWriter writer, Func<SlideElement, bool> unchanged)
    {
        var source = shape.Source is null ? null : writer.Relink(shape.Source.ToElement(), shape.References);
        if (source is not null && unchanged(shape))
        {
            return source;
        }

        if (source is not null && source.Name == Mc + "AlternateContent")
        {
            // Changed: the modelled element replaces the alternatives (the fallback's content is what ThixShow showed).
            var inner = source.Element(Mc + "Fallback")?.Elements().FirstOrDefault() ?? source.Elements(Mc + "Choice").FirstOrDefault()?.Elements().FirstOrDefault();
            source = inner is null ? null : new XElement(inner);
        }

        return shape switch
        {
            AutoShape auto => WriteShape(auto, source, writer),
            ConnectorShape connector => WriteConnector(connector, source, writer),
            PictureShape picture => WritePicture(picture, source, writer),
            GroupShape group => WriteGroup(group, source, writer, unchanged),
            GraphicFrame frame => WriteFrame(frame, source, writer),
            _ => source,
        };
    }

    private static XElement Ensure(XElement parent, XName name, string[] order)
    {
        if (parent.Element(name) is { } existing)
        {
            return existing;
        }

        var created = new XElement(name);
        parent.Place(created, order);
        return created;
    }

    private static void WriteNonVisual(XElement nonVisual, SlideElement shape, IWriteLinks links, XName locksName, XElement? locks = null)
    {
        var properties = Ensure(nonVisual, P + "cNvPr", NonVisualOrder);
        properties.SetAttributeValue("id", Text(shape.Id));
        properties.SetAttributeValue("name", shape.Name);
        properties.Set("descr", shape.Description);
        properties.Set("hidden", shape.Hidden ? true : null);
        properties.Set("title", shape.AltTitle);
        properties.Place(shape.Click is null ? null : DrawingXml.WriteHyperlink(shape.Click, A + "hlinkClick", links), CNvPrOrder, A + "hlinkClick");
        properties.Place(shape.Hover is null ? null : DrawingXml.WriteHyperlink(shape.Hover, A + "hlinkHover", links), CNvPrOrder, A + "hlinkHover");
        if (locks is not null)
        {
            nonVisual.Place(locks, NonVisualOrder, locksName);
        }
        else
        {
            Ensure(nonVisual, locksName, NonVisualOrder);
        }

        var nvPr = Ensure(nonVisual, P + "nvPr", NonVisualOrder);
        nvPr.Place(shape.Placeholder is null ? null : WritePlaceholder(shape.Placeholder), NvPrOrder, P + "ph");
    }

    public static XElement WritePlaceholder(PlaceholderInfo placeholder)
    {
        var ph = new XElement(P + "ph");
        ph.Set("type", placeholder.Type switch
        {
            PlaceholderType.Title => "title",
            PlaceholderType.Body => "body",
            PlaceholderType.CenteredTitle => "ctrTitle",
            PlaceholderType.Subtitle => "subTitle",
            PlaceholderType.DateAndTime => "dt",
            PlaceholderType.SlideNumber => "sldNum",
            PlaceholderType.Footer => "ftr",
            PlaceholderType.Header => "hdr",
            PlaceholderType.Chart => "chart",
            PlaceholderType.Table => "tbl",
            PlaceholderType.ClipArt => "clipArt",
            PlaceholderType.Diagram => "dgm",
            PlaceholderType.Media => "media",
            PlaceholderType.SlideImage => "sldImg",
            PlaceholderType.Picture => "pic",
            _ => null,
        });
        ph.Set("orient", placeholder.Orientation);
        ph.Set("sz", placeholder.Size);
        if (placeholder.Index is { } index)
        {
            ph.SetAttributeValue("idx", Text(index));
        }

        ph.Set("hasCustomPrompt", placeholder.HasCustomPrompt ? true : null);
        return ph;
    }

    private static XElement WriteStyle(ShapeStyle style)
    {
        XElement Reference(string name, StyleReference reference)
        {
            var element = new XElement(A + name, new XAttribute("idx", Text(reference.Index)));
            if (reference.Color is not null)
            {
                element.Add(DrawingXml.WriteColor(reference.Color));
            }

            return element;
        }

        var font = new XElement(A + "fontRef", new XAttribute("idx", style.Font.Index));
        if (style.Font.Color is not null)
        {
            font.Add(DrawingXml.WriteColor(style.Font.Color));
        }

        return new XElement(P + "style", Reference("lnRef", style.Line), Reference("fillRef", style.Fill), Reference("effectRef", style.Effect), font);
    }

    private static void WriteShapeProperties(XElement properties, Transform2D? transform, ShapeGeometry? geometry, Fill? fill, Outline? outline, ShapeEffects? effects, IWriteLinks links, bool group = false)
    {
        var order = DrawingXml.ShapePropertiesOrder;
        properties.Place(transform is null ? null : DrawingXml.WriteTransform(transform, A + "xfrm", group), order, A + "xfrm");
        if (!group)
        {
            properties.Place(geometry is null ? null : DrawingXml.WriteGeometry(geometry), order, A + "prstGeom", A + "custGeom");
        }

        properties.Place(DrawingXml.WriteFill(fill, links), order, A + "noFill", A + "solidFill", A + "gradFill", A + "blipFill", A + "pattFill", A + "grpFill");
        if (!group)
        {
            properties.Place(outline is null ? null : DrawingXml.WriteOutline(outline, links), order, A + "ln");
        }

        properties.Place(effects is null ? null : DrawingXml.WriteEffects(effects), order, A + "effectLst", A + "effectDag");
    }

    private static XElement WriteShape(AutoShape shape, XElement? source, IPartWriter writer)
    {
        var element = source ?? new XElement(P + "sp");
        var nonVisual = Ensure(element, P + "nvSpPr", SpOrder);
        var locks = shape.ShapeLocks?.ToElement() ?? nonVisual.Element(P + "cNvSpPr") ?? new XElement(P + "cNvSpPr");
        locks.Set("txBox", shape.IsTextBox ? true : null);
        WriteNonVisual(nonVisual, shape, writer, P + "cNvSpPr", new XElement(locks));
        WriteShapeProperties(Ensure(element, P + "spPr", SpOrder), shape.Transform, shape.Geometry, shape.Fill, shape.Outline, shape.Effects, writer);
        element.Place(shape.Style is null ? null : WriteStyle(shape.Style), SpOrder, P + "style");
        element.Place(shape.Text is null ? null : DrawingXml.WriteTextBody(shape.Text, P + "txBody", writer), SpOrder, P + "txBody");
        return element;
    }

    private static XElement WriteConnector(ConnectorShape shape, XElement? source, IPartWriter writer)
    {
        var element = source ?? new XElement(P + "cxnSp");
        var nonVisual = Ensure(element, P + "nvCxnSpPr", CxnOrder);
        var connection = new XElement(nonVisual.Element(P + "cNvCxnSpPr") ?? new XElement(P + "cNvCxnSpPr"));
        connection.Element(A + "stCxn")?.Remove();
        connection.Element(A + "endCxn")?.Remove();
        var locks = connection.Element(A + "cxnSpLocks");
        var position = locks;
        foreach (var (end, name) in new[] { (shape.Start, "stCxn"), (shape.End, "endCxn") })
        {
            if (end is null)
            {
                continue;
            }

            var endElement = new XElement(A + name, new XAttribute("id", Text(end.ShapeId)), new XAttribute("idx", Text(end.Site)));
            if (position is null)
            {
                connection.AddFirst(endElement);
            }
            else
            {
                position.AddAfterSelf(endElement);
            }

            position = endElement;
        }

        WriteNonVisual(nonVisual, shape, writer, P + "cNvCxnSpPr", connection);
        WriteShapeProperties(Ensure(element, P + "spPr", CxnOrder), shape.Transform, shape.Geometry, shape.Fill, shape.Outline, shape.Effects, writer);
        element.Place(shape.Style is null ? null : WriteStyle(shape.Style), CxnOrder, P + "style");
        return element;
    }

    private static XElement WritePicture(PictureShape shape, XElement? source, IPartWriter writer)
    {
        var element = source ?? new XElement(P + "pic");
        var nonVisual = Ensure(element, P + "nvPicPr", PicOrder);
        var locks = shape.PictureLocks?.ToElement() ?? new XElement(P + "cNvPicPr");
        WriteNonVisual(nonVisual, shape, writer, P + "cNvPicPr", locks);
        element.Place(DrawingXml.WritePictureFill(shape.Picture, writer, P + "blipFill"), PicOrder, P + "blipFill");
        WriteShapeProperties(Ensure(element, P + "spPr", PicOrder), shape.Transform, shape.Geometry, null, shape.Outline, shape.Effects, writer);
        element.Place(shape.Style is null ? null : WriteStyle(shape.Style), PicOrder, P + "style");
        return element;
    }

    private static XElement WriteGroup(GroupShape group, XElement? source, IPartWriter writer, Func<SlideElement, bool> unchanged)
    {
        var element = source ?? new XElement(P + "grpSp");
        string[] order = ["nvGrpSpPr", "grpSpPr"];
        var nonVisual = Ensure(element, P + "nvGrpSpPr", order);
        WriteNonVisual(nonVisual, group, writer, P + "cNvGrpSpPr");
        WriteShapeProperties(Ensure(element, P + "grpSpPr", order), group.Transform, null, group.Fill, null, group.Effects, writer, group: true);
        WriteShapes(element, group.Children, writer, unchanged);
        return element;
    }

    private static XElement WriteFrame(GraphicFrame frame, XElement? source, IPartWriter writer)
    {
        string[] order = ["nvGraphicFramePr", "xfrm", "graphic", "extLst"];
        var element = source ?? new XElement(P + "graphicFrame");
        var nonVisual = Ensure(element, P + "nvGraphicFramePr", order);
        WriteNonVisual(nonVisual, frame, writer, P + "cNvGraphicFramePr");
        element.Place(DrawingXml.WriteTransform(frame.Transform ?? new Transform2D(0, 0, 0, 0), P + "xfrm"), order, P + "xfrm");
        if (frame.Table is { } table)
        {
            var graphic = new XElement(A + "graphic", new XElement(A + "graphicData", new XAttribute("uri", TableUri), WriteTable(table, writer)));
            element.Place(graphic, order, A + "graphic");
        }

        return element;
    }

    public static XElement WriteTable(Table table, IWriteLinks links)
    {
        var properties = table.Properties.Extra?.ToElement() ?? new XElement(A + "tblPr");
        properties.Name = A + "tblPr";
        properties.Set("rtl", table.Properties.RightToLeft ? true : null);
        properties.Set("firstRow", table.Properties.FirstRow ? true : null);
        properties.Set("firstCol", table.Properties.FirstColumn ? true : null);
        properties.Set("lastRow", table.Properties.LastRow ? true : null);
        properties.Set("lastCol", table.Properties.LastColumn ? true : null);
        properties.Set("bandRow", table.Properties.BandRows ? true : null);
        properties.Set("bandCol", table.Properties.BandColumns ? true : null);
        string[] order = ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "effectLst", "effectDag", "tableStyleId", "extLst"];
        properties.Place(DrawingXml.WriteFill(table.Properties.Fill, links), order, A + "noFill", A + "solidFill", A + "gradFill", A + "blipFill", A + "pattFill", A + "grpFill");
        properties.Place(table.Properties.StyleId is null ? null : new XElement(A + "tableStyleId", table.Properties.StyleId), order, A + "tableStyleId");
        var element = new XElement(A + "tbl", properties, new XElement(A + "tblGrid", table.ColumnWidths.Select(width => new XElement(A + "gridCol", new XAttribute("w", Text(width))))));
        foreach (var row in table.Rows)
        {
            var rowElement = new XElement(A + "tr", new XAttribute("h", Text(row.Height)));
            foreach (var cell in row.Cells)
            {
                rowElement.Add(WriteCell(cell, links));
            }

            if (row.Extra is { } extra)
            {
                rowElement.Add(extra.ToElement());
            }

            element.Add(rowElement);
        }

        return element;
    }

    private static XElement WriteCell(TableCell cell, IWriteLinks links)
    {
        var element = new XElement(A + "tc");
        element.Set("gridSpan", cell.GridSpan > 1 ? cell.GridSpan : null);
        element.Set("rowSpan", cell.RowSpan > 1 ? cell.RowSpan : null);
        element.Set("hMerge", cell.HorizontalMerge ? true : null);
        element.Set("vMerge", cell.VerticalMerge ? true : null);
        element.Add(DrawingXml.WriteTextBody(cell.Text, A + "txBody", links));
        var properties = cell.Properties;
        var tcPr = properties.Extra?.ToElement() ?? new XElement(A + "tcPr");
        tcPr.Name = A + "tcPr";
        tcPr.Set("marL", properties.LeftMargin);
        tcPr.Set("marR", properties.RightMargin);
        tcPr.Set("marT", properties.TopMargin);
        tcPr.Set("marB", properties.BottomMargin);
        tcPr.Set("vert", properties.Vertical);
        tcPr.Set("anchor", properties.Anchor switch
        {
            TextAnchor.Top => "t",
            TextAnchor.Center => "ctr",
            TextAnchor.Bottom => "b",
            _ => null,
        });
        foreach (var (border, name) in new[] { (properties.LeftBorder, "lnL"), (properties.RightBorder, "lnR"), (properties.TopBorder, "lnT"), (properties.BottomBorder, "lnB") })
        {
            tcPr.Place(border is null ? null : DrawingXml.WriteOutline(border, links, A + name), CellOrder, A + name);
        }

        tcPr.Place(DrawingXml.WriteFill(properties.Fill, links), CellOrder, A + "noFill", A + "solidFill", A + "gradFill", A + "blipFill", A + "pattFill", A + "grpFill");
        element.Add(tcPr);
        return element;
    }
}
