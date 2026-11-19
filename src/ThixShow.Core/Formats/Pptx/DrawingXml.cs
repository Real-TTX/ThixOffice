using System.Globalization;
using System.Xml.Linq;
using ThixShow.Core.Model;
using static ThixShow.Core.Formats.Pptx.Ns;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>What reading DrawingML needs from the part it is in: pictures and link targets by relationship id.</summary>
internal interface IReadLinks
{
    ImageData? ImageOf(string relationshipId);

    /// <summary>An external address, or the slide id a relationship to a slide part names.</summary>
    (string? Url, uint? SlideId) TargetOf(string relationshipId);

    /// <summary>The part a relationship id points at ("/ppt/charts/chart1.xml"), or null.</summary>
    string? PartUriOf(string relationshipId);
}

/// <summary>What writing DrawingML needs from the part it goes into: relationship ids for pictures and links.</summary>
internal interface IWriteLinks
{
    string ImageId(ImageData image);

    /// <summary>The relationship id of a link's target ("" for actions without a target).</summary>
    string LinkId(Hyperlink link);
}

/// <summary>
/// Reads and writes DrawingML (ECMA-376 Part 1, 20.1/21.1) between XML and the model: colours with their transforms, fills, lines,
/// effects, geometry, transforms, links and text bodies. Writing produces elements in schema order; what the model does not own
/// comes back from the "Extra" fragments the reader kept.
/// </summary>
internal static class DrawingXml
{
    private static readonly string[] ColorNames = ["srgbClr", "schemeClr", "sysClr", "prstClr", "hslClr", "scrgbClr", "scRgbClr"];
    private static readonly string[] FillNames = ["noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill"];

    public static readonly string[] ShapePropertiesOrder = ["xfrm", "custGeom", "prstGeom", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "ln", "effectLst", "effectDag", "scene3d", "sp3d", "extLst"];
    public static readonly string[] LineOrder = ["noFill", "solidFill", "gradFill", "pattFill", "prstDash", "custDash", "round", "bevel", "miter", "headEnd", "tailEnd", "extLst"];
    public static readonly string[] RunOrder = ["ln", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "effectLst", "effectDag", "highlight", "uLnTx", "uLn", "uFillTx", "uFill", "latin", "ea", "cs", "sym", "hlinkClick", "hlinkMouseOver", "rtl", "extLst"];
    public static readonly string[] ParagraphOrder = ["lnSpc", "spcBef", "spcAft", "buClrTx", "buClr", "buSzTx", "buSzPct", "buSzPts", "buFontTx", "buFont", "buNone", "buAutoNum", "buChar", "buBlip", "tabLst", "defRPr", "extLst"];
    public static readonly string[] BodyOrder = ["prstTxWarp", "noAutofit", "normAutofit", "spAutoFit", "scene3d", "sp3d", "flatTx", "extLst"];

    private static readonly string[] OwnedRunAttributes = ["lang", "altLang", "sz", "b", "i", "u", "strike", "kern", "cap", "spc", "baseline", "dirty", "err"];
    private static readonly string[] OwnedRunChildren = ["ln", "noFill", "solidFill", "gradFill", "blipFill", "pattFill", "grpFill", "highlight", "latin", "ea", "cs", "sym", "hlinkClick", "hlinkMouseOver"];

    // ---------------------------------------------------------------- colours

    public static ColorSpec? ReadColor(XElement? parent)
    {
        var element = parent?.Elements().FirstOrDefault(child => child.Name.Namespace == A && ColorNames.Contains(child.Name.LocalName));
        if (element is null)
        {
            return null;
        }

        var modifiers = element.Elements().Select(child => new ColorModifier(child.Name.LocalName, child.Int("val"))).ToList();
        return element.Name.LocalName switch
        {
            "srgbClr" => new ColorSpec(ColorKind.Rgb, element.Attr("val") ?? "000000", modifiers),
            "schemeClr" => new ColorSpec(ColorKind.Scheme, element.Attr("val") ?? "tx1", modifiers),
            "sysClr" => new ColorSpec(ColorKind.System, element.Attr("val") ?? "windowText", modifiers, element.Attr("lastClr")),
            "prstClr" => new ColorSpec(ColorKind.Preset, element.Attr("val") ?? "black", modifiers),
            "hslClr" => new ColorSpec(ColorKind.Hsl, $"{element.Attr("hue") ?? "0"},{element.Attr("sat") ?? "0"},{element.Attr("lum") ?? "0"}", modifiers),
            _ => new ColorSpec(ColorKind.ScRgb, $"{element.Attr("r") ?? "0"},{element.Attr("g") ?? "0"},{element.Attr("b") ?? "0"}", modifiers),
        };
    }

    public static XElement WriteColor(ColorSpec color)
    {
        XElement element;
        switch (color.Kind)
        {
            case ColorKind.Scheme:
                element = new XElement(A + "schemeClr", new XAttribute("val", color.Value));
                break;
            case ColorKind.System:
                element = new XElement(A + "sysClr", new XAttribute("val", color.Value));
                element.Set("lastClr", color.LastColor);
                break;
            case ColorKind.Preset:
                element = new XElement(A + "prstClr", new XAttribute("val", color.Value));
                break;
            case ColorKind.Hsl:
                {
                    var parts = color.Value.Split(',');
                    element = new XElement(A + "hslClr", new XAttribute("hue", parts[0]), new XAttribute("sat", parts[1]), new XAttribute("lum", parts[2]));
                    break;
                }

            case ColorKind.ScRgb:
                {
                    var parts = color.Value.Split(',');
                    element = new XElement(A + "scrgbClr", new XAttribute("r", parts[0]), new XAttribute("g", parts[1]), new XAttribute("b", parts[2]));
                    break;
                }

            default:
                element = new XElement(A + "srgbClr", new XAttribute("val", color.Value));
                break;
        }

        foreach (var modifier in color.Modifiers)
        {
            var child = new XElement(A + modifier.Name);
            child.Set("val", modifier.Value);
            element.Add(child);
        }

        return element;
    }

    /// <summary>A wrapper element (a:solidFill, a:fgClr …) holding a colour.</summary>
    public static XElement ColorElement(XName name, ColorSpec color) => new(name, WriteColor(color));

    // ---------------------------------------------------------------- fills

    public static Fill? ReadFill(XElement? parent, IReadLinks links)
    {
        var element = parent?.Elements().FirstOrDefault(child => child.Name.Namespace == A && FillNames.Contains(child.Name.LocalName));
        return element is null ? null : ReadFillElement(element, links);
    }

    public static Fill? ReadFillElement(XElement element, IReadLinks links)
    {
        switch (element.Name.LocalName)
        {
            case "noFill":
                return NoFill.Instance;
            case "grpFill":
                return GroupFill.Instance;
            case "solidFill":
                return new SolidFill(ReadColor(element) ?? ColorSpec.Rgb("000000"));
            case "pattFill":
                return new PatternFill(element.Attr("prst") ?? "pct5", ReadColor(element.Element(A + "fgClr")), ReadColor(element.Element(A + "bgClr")));
            case "gradFill":
                {
                    var stops = element.Element(A + "gsLst")?.Elements(A + "gs")
                        .Select(stop => new GradientStop(stop.Int("pos") ?? 0, ReadColor(stop) ?? ColorSpec.Rgb("000000"))).ToList() ?? [];
                    var linear = element.Element(A + "lin");
                    var path = element.Element(A + "path");
                    return new GradientFill(
                        stops,
                        linear.Int("ang") ?? (path is null && linear is null ? null : linear is null ? null : 0),
                        linear.Bool("scaled"),
                        path is null ? null : path.Attr("path") switch
                        {
                            "rect" => GradientPathKind.Rectangle,
                            "shape" => GradientPathKind.Shape,
                            _ => GradientPathKind.Circle,
                        },
                        ReadRect(path?.Element(A + "fillToRect")),
                        ReadRect(element.Element(A + "tileRect")),
                        element.Bool("rotWithShape"),
                        element.Attr("flip"));
                }

            case "blipFill":
                return ReadPictureFill(element, links);
            default:
                return null;
        }
    }

    public static PictureFill ReadPictureFill(XElement element, IReadLinks links)
    {
        var blip = element.Element(A + "blip");
        var embed = blip?.Attribute(R + "embed")?.Value;
        var image = embed is null ? null : links.ImageOf(embed);
        PictureFillMode mode = StretchMode.Full;
        if (element.Element(A + "tile") is { } tile)
        {
            mode = new TileMode(tile.Long("tx") ?? 0, tile.Long("ty") ?? 0, tile.Int("sx") ?? 100000, tile.Int("sy") ?? 100000, tile.Attr("flip"), tile.Attr("algn"));
        }
        else if (element.Element(A + "stretch")?.Element(A + "fillRect") is { } fillRect)
        {
            mode = new StretchMode(ReadRect(fillRect) ?? RelativeRect.Zero);
        }

        XmlFragment? effects = null;
        var opacity = 1.0;
        if (blip is not null)
        {
            var copy = new XElement(blip);
            copy.Attribute(R + "embed")?.Remove();
            copy.Attribute(R + "link")?.Remove();
            copy.Element(A + "alphaModFix")?.Remove();
            if (copy.HasElements || copy.HasAttributes)
            {
                effects = XmlFragment.From(copy);
            }

            if (blip.Element(A + "alphaModFix")?.Int("amt") is { } amount)
            {
                opacity = amount / 100000.0;
            }
        }

        return new PictureFill(
            image,
            ReadRect(element.Element(A + "srcRect")) ?? RelativeRect.Zero,
            mode,
            element.Int("dpi"),
            element.Bool("rotWithShape"),
            effects,
            blip?.Attribute(R + "link")?.Value)
        {
            Opacity = opacity,
        };
    }

    public static RelativeRect? ReadRect(XElement? element) => element is null
        ? null
        : new RelativeRect(element.Int("l") ?? 0, element.Int("t") ?? 0, element.Int("r") ?? 0, element.Int("b") ?? 0);

    public static XElement WriteRect(XName name, RelativeRect rect)
    {
        var element = new XElement(name);
        element.Set("l", rect.Left == 0 ? null : rect.Left);
        element.Set("t", rect.Top == 0 ? null : rect.Top);
        element.Set("r", rect.Right == 0 ? null : rect.Right);
        element.Set("b", rect.Bottom == 0 ? null : rect.Bottom);
        return element;
    }

    public static XElement? WriteFill(Fill? fill, IWriteLinks links, XNamespace? blipNamespace = null)
    {
        switch (fill)
        {
            case null:
                return null;
            case NoFill:
                return new XElement(A + "noFill");
            case GroupFill:
                return new XElement(A + "grpFill");
            case SolidFill solid:
                return ColorElement(A + "solidFill", solid.Color);
            case PatternFill pattern:
                {
                    var element = new XElement(A + "pattFill", new XAttribute("prst", pattern.Preset));
                    if (pattern.Foreground is not null)
                    {
                        element.Add(ColorElement(A + "fgClr", pattern.Foreground));
                    }

                    if (pattern.Background is not null)
                    {
                        element.Add(ColorElement(A + "bgClr", pattern.Background));
                    }

                    return element;
                }

            case GradientFill gradient:
                {
                    var element = new XElement(A + "gradFill");
                    element.Set("flip", gradient.Flip);
                    element.Set("rotWithShape", gradient.RotateWithShape);
                    element.Add(new XElement(A + "gsLst", gradient.Stops.Select(stop => new XElement(A + "gs", new XAttribute("pos", Text(stop.Position)), WriteColor(stop.Color)))));
                    if (gradient.PathKind is { } kind)
                    {
                        var path = new XElement(A + "path", new XAttribute("path", kind switch
                        {
                            GradientPathKind.Rectangle => "rect",
                            GradientPathKind.Shape => "shape",
                            _ => "circle",
                        }));
                        if (gradient.FillToRect is { } focus)
                        {
                            path.Add(WriteRect(A + "fillToRect", focus));
                        }

                        element.Add(path);
                    }
                    else if (gradient.LinearAngle is { } angle)
                    {
                        var linear = new XElement(A + "lin", new XAttribute("ang", Text(angle)));
                        linear.Set("scaled", gradient.LinearScaled);
                        element.Add(linear);
                    }

                    if (gradient.TileRect is { } tile)
                    {
                        element.Add(WriteRect(A + "tileRect", tile));
                    }

                    return element;
                }

            case PictureFill picture:
                return WritePictureFill(picture, links, A + "blipFill");
            default:
                return null;
        }
    }

    /// <summary>a:blipFill (or p:blipFill for pictures: <paramref name="name"/>).</summary>
    public static XElement WritePictureFill(PictureFill picture, IWriteLinks links, XName name)
    {
        var element = new XElement(name);
        element.Set("dpi", picture.Dpi);
        element.Set("rotWithShape", picture.RotateWithShape);
        var blip = picture.BlipEffects?.ToElement() ?? new XElement(A + "blip");
        if (picture.Image is { } image)
        {
            blip.SetAttributeValue(R + "embed", links.ImageId(image));
        }

        if (picture.LinkTarget is not null && picture.Image is null)
        {
            blip.SetAttributeValue(R + "link", picture.LinkTarget);
        }

        var alpha = blip.Element(A + "alphaModFix");
        if (picture.Opacity < 1)
        {
            if (alpha is null)
            {
                alpha = new XElement(A + "alphaModFix");
                blip.AddFirst(alpha);
            }

            alpha.SetAttributeValue("amt", Text((int)Math.Round(picture.Opacity * 100000)));
        }
        else
        {
            alpha?.Remove();
        }

        element.Add(blip);
        if (!picture.Crop.IsZero)
        {
            element.Add(WriteRect(A + "srcRect", picture.Crop));
        }

        switch (picture.Mode)
        {
            case TileMode tile:
                {
                    var tileElement = new XElement(A + "tile");
                    tileElement.Set("tx", tile.OffsetX);
                    tileElement.Set("ty", tile.OffsetY);
                    tileElement.Set("sx", tile.ScaleX);
                    tileElement.Set("sy", tile.ScaleY);
                    tileElement.Set("flip", tile.Flip);
                    tileElement.Set("algn", tile.Alignment);
                    element.Add(tileElement);
                    break;
                }

            case StretchMode stretch:
                element.Add(new XElement(A + "stretch", WriteRect(A + "fillRect", stretch.FillRect)));
                break;
        }

        return element;
    }

    // ---------------------------------------------------------------- lines and effects

    private static LineEndSize Size(string? value) => value switch
    {
        "sm" => LineEndSize.Small,
        "lg" => LineEndSize.Large,
        _ => LineEndSize.Medium,
    };

    private static string Size(LineEndSize size) => size switch
    {
        LineEndSize.Small => "sm",
        LineEndSize.Large => "lg",
        _ => "med",
    };

    private static LineEnd? ReadLineEnd(XElement? element) => element is null ? null : new LineEnd(
        element.Attr("type") switch
        {
            "triangle" => LineEndKind.Triangle,
            "stealth" => LineEndKind.Stealth,
            "diamond" => LineEndKind.Diamond,
            "oval" => LineEndKind.Oval,
            "arrow" => LineEndKind.Arrow,
            _ => LineEndKind.None,
        },
        Size(element.Attr("w")),
        Size(element.Attr("len")));

    public static Outline? ReadOutline(XElement? line, IReadLinks links)
    {
        if (line is null)
        {
            return null;
        }

        LineDash? dash = null;
        if (line.Element(A + "prstDash") is { } preset)
        {
            dash = new LineDash(preset.Attr("val") ?? "solid");
        }
        else if (line.Element(A + "custDash") is { } custom)
        {
            dash = new LineDash(null, custom.Elements(A + "ds").Select(stop => (stop.Int("d") ?? 0, stop.Int("sp") ?? 0)).ToList());
        }

        LineJoin? join = line.Element(A + "round") is not null ? LineJoin.Round : line.Element(A + "bevel") is not null ? LineJoin.Bevel : line.Element(A + "miter") is not null ? LineJoin.Miter : null;
        return new Outline(
            line.Long("w"),
            ReadFill(line, links),
            dash,
            line.Attr("cap") switch
            {
                "rnd" => LineCap.Round,
                "sq" => LineCap.Square,
                "flat" => LineCap.Flat,
                _ => null,
            },
            join,
            line.Element(A + "miter")?.Int("lim"),
            line.Attr("cmpd"),
            line.Attr("algn"),
            ReadLineEnd(line.Element(A + "headEnd")),
            ReadLineEnd(line.Element(A + "tailEnd")));
    }

    public static XElement WriteOutline(Outline outline, IWriteLinks links, XName? name = null)
    {
        var line = new XElement(name ?? A + "ln");
        line.Set("w", outline.Width);
        line.Set("cap", outline.Cap switch
        {
            LineCap.Round => "rnd",
            LineCap.Square => "sq",
            LineCap.Flat => "flat",
            _ => null,
        });
        line.Set("cmpd", outline.Compound);
        line.Set("algn", outline.Alignment);
        if (WriteFill(outline.Fill, links) is { } fill)
        {
            line.Add(fill);
        }

        if (outline.Dash is { } dash)
        {
            line.Add(dash.Preset is not null
                ? new XElement(A + "prstDash", new XAttribute("val", dash.Preset))
                : new XElement(A + "custDash", dash.Custom.Select(stop => new XElement(A + "ds", new XAttribute("d", Text(stop.Dash)), new XAttribute("sp", Text(stop.Space))))));
        }

        switch (outline.Join)
        {
            case LineJoin.Round:
                line.Add(new XElement(A + "round"));
                break;
            case LineJoin.Bevel:
                line.Add(new XElement(A + "bevel"));
                break;
            case LineJoin.Miter:
                {
                    var miter = new XElement(A + "miter");
                    miter.Set("lim", outline.MiterLimit);
                    line.Add(miter);
                    break;
                }
        }

        foreach (var (end, endName) in new[] { (outline.Head, "headEnd"), (outline.Tail, "tailEnd") })
        {
            if (end is null)
            {
                continue;
            }

            var element = new XElement(A + endName, new XAttribute("type", end.Kind switch
            {
                LineEndKind.Triangle => "triangle",
                LineEndKind.Stealth => "stealth",
                LineEndKind.Diamond => "diamond",
                LineEndKind.Oval => "oval",
                LineEndKind.Arrow => "arrow",
                _ => "none",
            }));
            if (end.Width != LineEndSize.Medium)
            {
                element.SetAttributeValue("w", Size(end.Width));
            }

            if (end.Length != LineEndSize.Medium)
            {
                element.SetAttributeValue("len", Size(end.Length));
            }

            line.Add(element);
        }

        return line;
    }

    /// <summary>The effects of a shape properties element (a:effectLst / a:effectDag), or null when it has none.</summary>
    public static ShapeEffects? ReadEffects(XElement? parent)
    {
        if (parent?.Element(A + "effectDag") is { } dag)
        {
            return new ShapeEffects(null, XmlFragment.From(dag), true);
        }

        if (parent?.Element(A + "effectLst") is not { } list)
        {
            return null;
        }

        OuterShadow? shadow = null;
        if (list.Element(A + "outerShdw") is { } outer)
        {
            shadow = new OuterShadow(
                outer.Long("blurRad") ?? 0,
                outer.Long("dist") ?? 0,
                outer.Int("dir") ?? 0,
                ReadColor(outer) ?? ColorSpec.Rgb("000000"),
                outer.Attr("algn"),
                outer.Bool("rotWithShape"),
                outer.Int("sx") ?? 100000,
                outer.Int("sy") ?? 100000,
                outer.Int("kx") ?? 0,
                outer.Int("ky") ?? 0);
        }

        var others = list.Without("outerShdw");
        return new ShapeEffects(shadow, others.HasElements ? XmlFragment.From(others) : null);
    }

    private static readonly string[] EffectOrder = ["blur", "fillOverlay", "glow", "innerShdw", "outerShdw", "prstShdw", "reflection", "softEdge"];

    public static XElement WriteEffects(ShapeEffects effects)
    {
        if (effects.IsDag && effects.Others is { } dag)
        {
            return dag.ToElement();
        }

        var list = effects.Others?.ToElement() ?? new XElement(A + "effectLst");
        if (effects.Shadow is { } shadow)
        {
            var outer = new XElement(A + "outerShdw");
            outer.Set("blurRad", shadow.BlurRadius == 0 ? null : shadow.BlurRadius);
            outer.Set("dist", shadow.Distance == 0 ? null : shadow.Distance);
            outer.Set("dir", shadow.Direction == 0 ? null : shadow.Direction);
            outer.Set("sx", shadow.ScaleX == 100000 ? null : shadow.ScaleX);
            outer.Set("sy", shadow.ScaleY == 100000 ? null : shadow.ScaleY);
            outer.Set("kx", shadow.SkewX == 0 ? null : shadow.SkewX);
            outer.Set("ky", shadow.SkewY == 0 ? null : shadow.SkewY);
            outer.Set("algn", shadow.Alignment);
            outer.Set("rotWithShape", shadow.RotateWithShape);
            outer.Add(WriteColor(shadow.Color));
            list.Place(outer, EffectOrder);
        }

        return list;
    }

    // ---------------------------------------------------------------- geometry and transforms

    public static ShapeGeometry? ReadGeometry(XElement? properties)
    {
        if (properties?.Element(A + "prstGeom") is { } preset)
        {
            var adjusts = preset.Element(A + "avLst")?.Elements(A + "gd")
                .Select(gd => new GeometryAdjust(gd.Attr("name") ?? "adj", ParseVal(gd.Attr("fmla")))).ToList() ?? [];
            return new ShapeGeometry(preset.Attr("prst") ?? "rect", adjusts);
        }

        if (properties?.Element(A + "custGeom") is { } custom)
        {
            return new ShapeGeometry(null, Custom: XmlFragment.From(custom));
        }

        return null;
    }

    private static long ParseVal(string? formula)
    {
        var parts = (formula ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && parts[0] == "val" && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    public static XElement WriteGeometry(ShapeGeometry geometry)
    {
        if (geometry.Custom is { } custom)
        {
            return custom.ToElement();
        }

        return new XElement(A + "prstGeom", new XAttribute("prst", geometry.Preset ?? "rect"),
            new XElement(A + "avLst", geometry.Adjustments.Select(adjust => new XElement(A + "gd", new XAttribute("name", adjust.Name), new XAttribute("fmla", "val " + Text(adjust.Value))))));
    }

    public static Transform2D? ReadTransform(XElement? xfrm)
    {
        if (xfrm is null)
        {
            return null;
        }

        var offset = xfrm.Element(A + "off");
        var extent = xfrm.Element(A + "ext");
        var childOffset = xfrm.Element(A + "chOff");
        var childExtent = xfrm.Element(A + "chExt");
        return new Transform2D(
            offset.Long("x") ?? 0,
            offset.Long("y") ?? 0,
            extent.Long("cx") ?? 0,
            extent.Long("cy") ?? 0,
            xfrm.Int("rot") ?? 0,
            xfrm.Bool("flipH") ?? false,
            xfrm.Bool("flipV") ?? false,
            childOffset.Long("x") ?? 0,
            childOffset.Long("y") ?? 0,
            childExtent.Long("cx") ?? 0,
            childExtent.Long("cy") ?? 0);
    }

    public static XElement WriteTransform(Transform2D transform, XName name, bool group = false)
    {
        var xfrm = new XElement(name);
        xfrm.Set("rot", transform.Rotation == 0 ? null : transform.Rotation);
        xfrm.Set("flipH", transform.FlipHorizontal ? true : null);
        xfrm.Set("flipV", transform.FlipVertical ? true : null);
        xfrm.Add(new XElement(A + "off", new XAttribute("x", Text(transform.X)), new XAttribute("y", Text(transform.Y))));
        xfrm.Add(new XElement(A + "ext", new XAttribute("cx", Text(Math.Max(0, transform.Width))), new XAttribute("cy", Text(Math.Max(0, transform.Height)))));
        if (group)
        {
            xfrm.Add(new XElement(A + "chOff", new XAttribute("x", Text(transform.ChildX)), new XAttribute("y", Text(transform.ChildY))));
            xfrm.Add(new XElement(A + "chExt", new XAttribute("cx", Text(Math.Max(0, transform.ChildWidth))), new XAttribute("cy", Text(Math.Max(0, transform.ChildHeight)))));
        }

        return xfrm;
    }

    // ---------------------------------------------------------------- links

    public static Hyperlink? ReadHyperlink(XElement? element, IReadLinks links)
    {
        if (element is null)
        {
            return null;
        }

        var id = element.Attribute(R + "id")?.Value;
        var action = element.Attr("action");
        var (url, slideId) = string.IsNullOrEmpty(id) ? (null, null) : links.TargetOf(id);
        var kind = action switch
        {
            null or "" => HyperlinkKind.Url,
            "ppaction://hlinksldjump" => HyperlinkKind.Slide,
            "ppaction://hlinkshowjump?jump=nextslide" => HyperlinkKind.NextSlide,
            "ppaction://hlinkshowjump?jump=previousslide" => HyperlinkKind.PreviousSlide,
            "ppaction://hlinkshowjump?jump=firstslide" => HyperlinkKind.FirstSlide,
            "ppaction://hlinkshowjump?jump=lastslide" => HyperlinkKind.LastSlide,
            "ppaction://hlinkshowjump?jump=endshow" => HyperlinkKind.EndShow,
            _ => HyperlinkKind.Other,
        };
        var extra = new XElement(element);
        extra.Attribute(R + "id")?.Remove();
        extra.Attribute("action")?.Remove();
        extra.Attribute("tooltip")?.Remove();
        return new Hyperlink(kind, url, slideId, action, element.Attr("tooltip"), extra.HasAttributes || extra.HasElements ? XmlFragment.From(extra) : null);
    }

    public static XElement WriteHyperlink(Hyperlink link, XName name, IWriteLinks links)
    {
        var element = link.Extra?.ToElement() ?? new XElement(name);
        element.Name = name;
        element.SetAttributeValue(R + "id", links.LinkId(link));
        element.Set("action", link.Action);
        element.Set("tooltip", link.Tooltip);
        return element;
    }

    // ---------------------------------------------------------------- text

    public static TextBody ReadTextBody(XElement body, IReadLinks links)
    {
        var bodyProperties = ReadBodyProperties(body.Element(A + "bodyPr"));
        var listStyle = ReadListStyle(body.Element(A + "lstStyle"), links);
        var paragraphs = body.Elements(A + "p").Select(paragraph => ReadParagraph(paragraph, links)).ToList();
        return new TextBody(bodyProperties, listStyle, paragraphs);
    }

    public static BodyProperties ReadBodyProperties(XElement? element)
    {
        if (element is null)
        {
            return BodyProperties.Empty;
        }

        AutoFit? autoFit = null;
        if (element.Element(A + "noAutofit") is not null)
        {
            autoFit = NoAutoFit.Instance;
        }
        else if (element.Element(A + "spAutoFit") is not null)
        {
            autoFit = ShapeAutoFit.Instance;
        }
        else if (element.Element(A + "normAutofit") is { } normal)
        {
            autoFit = new NormalAutoFit(normal.Int("fontScale"), normal.Int("lnSpcReduction"));
        }

        var extra = element.Without("noAutofit", "spAutoFit", "normAutofit");
        foreach (var name in new[] { "lIns", "tIns", "rIns", "bIns", "anchor", "anchorCtr", "wrap", "vert", "rot", "numCol", "spcCol", "upright", "rtlCol" })
        {
            extra.Attribute(name)?.Remove();
        }

        return new BodyProperties(
            element.Long("lIns"),
            element.Long("tIns"),
            element.Long("rIns"),
            element.Long("bIns"),
            element.Attr("anchor") switch
            {
                "t" => TextAnchor.Top,
                "ctr" => TextAnchor.Center,
                "b" => TextAnchor.Bottom,
                "just" => TextAnchor.Justified,
                "dist" => TextAnchor.Distributed,
                _ => null,
            },
            element.Bool("anchorCtr"),
            element.Attr("wrap") switch
            {
                "none" => false,
                "square" => true,
                _ => null,
            },
            element.Attr("vert"),
            element.Int("rot"),
            element.Int("numCol"),
            element.Long("spcCol"),
            autoFit,
            element.Bool("upright"),
            element.Bool("rtlCol"),
            extra.HasAttributes || extra.HasElements ? XmlFragment.From(extra) : null);
    }

    public static XElement WriteBodyProperties(BodyProperties body)
    {
        var element = body.Extra?.ToElement() ?? new XElement(A + "bodyPr");
        element.Name = A + "bodyPr";
        element.Set("rot", body.Rotation);
        element.Set("vert", body.Vertical);
        element.Set("wrap", body.Wrap switch
        {
            true => "square",
            false => "none",
            _ => null,
        });
        element.Set("lIns", body.LeftInset);
        element.Set("tIns", body.TopInset);
        element.Set("rIns", body.RightInset);
        element.Set("bIns", body.BottomInset);
        element.Set("numCol", body.Columns);
        element.Set("spcCol", body.ColumnSpacing);
        element.Set("rtlCol", body.RightToLeftColumns);
        element.Set("anchor", body.Anchor switch
        {
            TextAnchor.Top => "t",
            TextAnchor.Center => "ctr",
            TextAnchor.Bottom => "b",
            TextAnchor.Justified => "just",
            TextAnchor.Distributed => "dist",
            _ => null,
        });
        element.Set("anchorCtr", body.AnchorCenter);
        element.Set("upright", body.Upright);
        XElement? fit = body.AutoFit switch
        {
            NoAutoFit => new XElement(A + "noAutofit"),
            ShapeAutoFit => new XElement(A + "spAutoFit"),
            NormalAutoFit normal => NormalFit(normal),
            _ => null,
        };
        element.Place(fit, BodyOrder, A + "noAutofit", A + "spAutoFit", A + "normAutofit");
        return element;
    }

    private static XElement NormalFit(NormalAutoFit normal)
    {
        var element = new XElement(A + "normAutofit");
        element.Set("fontScale", normal.FontScale);
        element.Set("lnSpcReduction", normal.LineSpacingReduction);
        return element;
    }

    public static ListStyle ReadListStyle(XElement? element, IReadLinks links)
    {
        if (element is null)
        {
            return ListStyle.Empty;
        }

        var levels = new List<ParagraphProperties?>();
        for (var level = 1; level <= 9; level++)
        {
            levels.Add(element.Element(A + $"lvl{level}pPr") is { } properties ? ReadParagraphProperties(properties, links) : null);
        }

        while (levels.Count > 0 && levels[^1] is null)
        {
            levels.RemoveAt(levels.Count - 1);
        }

        var extensions = element.Element(A + "extLst");
        return new ListStyle(
            element.Element(A + "defPPr") is { } defaults ? ReadParagraphProperties(defaults, links) : null,
            levels,
            extensions is null ? null : XmlFragment.From(extensions));
    }

    /// <summary>A list style into an element named <paramref name="name"/> (a:lstStyle, p:titleStyle, p:defaultTextStyle …).</summary>
    public static XElement WriteListStyle(ListStyle style, XName name, IWriteLinks links)
    {
        var element = new XElement(name);
        if (style.Default is { } defaults)
        {
            element.Add(WriteParagraphProperties(defaults, A + "defPPr", links));
        }

        for (var level = 0; level < style.Levels.Count; level++)
        {
            if (style.Levels[level] is { } properties)
            {
                element.Add(WriteParagraphProperties(properties, A + $"lvl{level + 1}pPr", links));
            }
        }

        if (style.Extensions is { } extensions)
        {
            element.Add(extensions.ToElement());
        }

        return element;
    }

    private static TextSpacing? ReadSpacing(XElement? element)
    {
        if (element?.Element(A + "spcPct") is { } percent)
        {
            return TextSpacing.Percent(percent.Int("val") ?? 100000);
        }

        if (element?.Element(A + "spcPts") is { } points)
        {
            return new TextSpacing(points.Int("val") ?? 0, true);
        }

        return null;
    }

    private static XElement WriteSpacing(XName name, TextSpacing spacing) =>
        new(name, new XElement(A + (spacing.IsPoints ? "spcPts" : "spcPct"), new XAttribute("val", Text(spacing.Value))));

    private static TextAlignment? Alignment(string? value) => value switch
    {
        "l" => TextAlignment.Left,
        "ctr" => TextAlignment.Center,
        "r" => TextAlignment.Right,
        "just" => TextAlignment.Justify,
        "justLow" => TextAlignment.JustifyLow,
        "dist" => TextAlignment.Distributed,
        "thaiDist" => TextAlignment.ThaiDistributed,
        _ => null,
    };

    private static string? Alignment(TextAlignment? value) => value switch
    {
        TextAlignment.Left => "l",
        TextAlignment.Center => "ctr",
        TextAlignment.Right => "r",
        TextAlignment.Justify => "just",
        TextAlignment.JustifyLow => "justLow",
        TextAlignment.Distributed => "dist",
        TextAlignment.ThaiDistributed => "thaiDist",
        _ => null,
    };

    public static TextFont ReadFont(XElement element) =>
        new(element.Attr("typeface") ?? "", element.Attr("panose"), element.Attr("pitchFamily"), element.Attr("charset"));

    public static XElement WriteFont(XName name, TextFont font)
    {
        var element = new XElement(name, new XAttribute("typeface", font.Typeface));
        element.Set("panose", font.Panose);
        element.Set("pitchFamily", font.PitchFamily);
        element.Set("charset", font.CharacterSet);
        return element;
    }

    public static ParagraphProperties ReadParagraphProperties(XElement element, IReadLinks links)
    {
        Bullet? bullet = null;
        if (element.Element(A + "buNone") is not null)
        {
            bullet = NoBullet.Instance;
        }
        else if (element.Element(A + "buChar") is { } character)
        {
            bullet = new CharacterBullet(character.Attr("char") ?? "•");
        }
        else if (element.Element(A + "buAutoNum") is { } number)
        {
            bullet = new AutoNumberBullet(number.Attr("type") ?? "arabicPeriod", number.Int("startAt"));
        }
        else if (element.Element(A + "buBlip") is { } picture)
        {
            var embed = picture.Element(A + "blip")?.Attribute(R + "embed")?.Value;
            bullet = new PictureBullet(embed is null ? null : links.ImageOf(embed));
        }

        TextSpacing? bulletSize = null;
        if (element.Element(A + "buSzPct") is { } sizePercent)
        {
            bulletSize = TextSpacing.Percent(sizePercent.Int("val") ?? 100000);
        }
        else if (element.Element(A + "buSzPts") is { } sizePoints)
        {
            bulletSize = new TextSpacing(sizePoints.Int("val") ?? 0, true);
        }

        var tabs = element.Element(A + "tabLst") is { } tabList
            ? (ValueList<TabStop>?)tabList.Elements(A + "tab").Select(tab => new TabStop(tab.Long("pos") ?? 0, tab.Attr("algn") ?? "l")).ToList()
            : null;
        var extensions = element.Element(A + "extLst");
        return new ParagraphProperties(
            element.Int("lvl"),
            Alignment(element.Attr("algn")),
            element.Long("marL"),
            element.Long("marR"),
            element.Long("indent"),
            element.Long("defTabSz"),
            element.Bool("rtl"),
            element.Attr("fontAlgn"),
            element.Bool("eaLnBrk"),
            element.Bool("latinLnBrk"),
            element.Bool("hangingPunct"),
            ReadSpacing(element.Element(A + "lnSpc")),
            ReadSpacing(element.Element(A + "spcBef")),
            ReadSpacing(element.Element(A + "spcAft")),
            element.Element(A + "buClr") is { } bulletColor ? ReadColor(bulletColor) : null,
            element.Element(A + "buClrTx") is not null ? true : null,
            bulletSize,
            element.Element(A + "buSzTx") is not null ? true : null,
            element.Element(A + "buFont") is { } bulletFont ? ReadFont(bulletFont) : null,
            element.Element(A + "buFontTx") is not null ? true : null,
            bullet,
            tabs,
            element.Element(A + "defRPr") is { } defaults ? ReadRunProperties(defaults, links) : null,
            extensions is null ? null : XmlFragment.From(extensions));
    }

    public static XElement WriteParagraphProperties(ParagraphProperties properties, XName name, IWriteLinks links)
    {
        var element = new XElement(name);
        element.Set("marL", properties.LeftMargin);
        element.Set("marR", properties.RightMargin);
        element.Set("lvl", properties.Level);
        element.Set("indent", properties.Indent);
        element.Set("algn", Alignment(properties.Alignment));
        element.Set("defTabSz", properties.DefaultTabSize);
        element.Set("rtl", properties.RightToLeft);
        element.Set("eaLnBrk", properties.EastAsianLineBreak);
        element.Set("fontAlgn", properties.FontAlignment);
        element.Set("latinLnBrk", properties.LatinLineBreak);
        element.Set("hangingPunct", properties.HangingPunctuation);
        if (properties.LineSpacing is { } lineSpacing)
        {
            element.Add(WriteSpacing(A + "lnSpc", lineSpacing));
        }

        if (properties.SpaceBefore is { } before)
        {
            element.Add(WriteSpacing(A + "spcBef", before));
        }

        if (properties.SpaceAfter is { } after)
        {
            element.Add(WriteSpacing(A + "spcAft", after));
        }

        if (properties.BulletColorFollowsText == true)
        {
            element.Add(new XElement(A + "buClrTx"));
        }
        else if (properties.BulletColor is { } bulletColor)
        {
            element.Add(ColorElement(A + "buClr", bulletColor));
        }

        if (properties.BulletSizeFollowsText == true)
        {
            element.Add(new XElement(A + "buSzTx"));
        }
        else if (properties.BulletSize is { } bulletSize)
        {
            element.Add(new XElement(A + (bulletSize.IsPoints ? "buSzPts" : "buSzPct"), new XAttribute("val", Text(bulletSize.Value))));
        }

        if (properties.BulletFontFollowsText == true)
        {
            element.Add(new XElement(A + "buFontTx"));
        }
        else if (properties.BulletFont is { } bulletFont)
        {
            element.Add(WriteFont(A + "buFont", bulletFont));
        }

        switch (properties.Bullet)
        {
            case NoBullet:
                element.Add(new XElement(A + "buNone"));
                break;
            case CharacterBullet character:
                element.Add(new XElement(A + "buChar", new XAttribute("char", character.Character)));
                break;
            case AutoNumberBullet number:
                {
                    var autoNumber = new XElement(A + "buAutoNum", new XAttribute("type", number.Scheme));
                    autoNumber.Set("startAt", number.StartAt);
                    element.Add(autoNumber);
                    break;
                }

            case PictureBullet { Image: { } image }:
                element.Add(new XElement(A + "buBlip", new XElement(A + "blip", new XAttribute(R + "embed", links.ImageId(image)))));
                break;
        }

        if (properties.Tabs is { } tabs)
        {
            element.Add(new XElement(A + "tabLst", tabs.Select(tab => new XElement(A + "tab", new XAttribute("pos", Text(tab.Position)), new XAttribute("algn", tab.Alignment)))));
        }

        if (properties.DefaultRun is { } defaults)
        {
            element.Add(WriteRunProperties(defaults, A + "defRPr", links));
        }

        if (properties.Extra is { } extra)
        {
            element.Add(extra.ToElement());
        }

        return element;
    }

    public static RunProperties ReadRunProperties(XElement element, IReadLinks links)
    {
        var extra = new XElement(A + "rPr");
        foreach (var attribute in element.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration && !OwnedRunAttributes.Contains(attribute.Name.LocalName)))
        {
            extra.Add(new XAttribute(attribute));
        }

        foreach (var child in element.Elements().Where(child => !OwnedRunChildren.Contains(child.Name.LocalName)))
        {
            extra.Add(new XElement(child));
        }

        return new RunProperties(
            element.Int("sz"),
            element.Bool("b"),
            element.Bool("i"),
            element.Attr("u"),
            element.Attr("strike"),
            element.Int("baseline"),
            element.Attr("cap"),
            element.Int("spc"),
            element.Int("kern"),
            element.Attr("lang"),
            ReadFill(element, links),
            element.Element(A + "ln") is { } line ? ReadOutline(line, links) : null,
            element.Element(A + "highlight") is { } highlight ? ReadColor(highlight) : null,
            element.Element(A + "latin") is { } latin ? ReadFont(latin) : null,
            element.Element(A + "ea") is { } eastAsian ? ReadFont(eastAsian) : null,
            element.Element(A + "cs") is { } complex ? ReadFont(complex) : null,
            element.Element(A + "sym") is { } symbol ? ReadFont(symbol) : null,
            ReadHyperlink(element.Element(A + "hlinkClick"), links),
            ReadHyperlink(element.Element(A + "hlinkMouseOver"), links),
            element.Attr("altLang"),
            element.Bool("dirty"),
            element.Bool("err"),
            extra.HasAttributes || extra.HasElements ? XmlFragment.From(extra) : null);
    }

    public static XElement WriteRunProperties(RunProperties properties, XName name, IWriteLinks links)
    {
        var element = new XElement(name);
        element.Set("lang", properties.Language);
        element.Set("altLang", properties.AlternativeLanguage);
        element.Set("sz", properties.Size);
        element.Set("b", properties.Bold);
        element.Set("i", properties.Italic);
        element.Set("u", properties.Underline);
        element.Set("strike", properties.Strike);
        element.Set("kern", properties.Kerning);
        element.Set("cap", properties.Caps);
        element.Set("spc", properties.Spacing);
        element.Set("baseline", properties.Baseline);
        element.Set("dirty", properties.Dirty);
        element.Set("err", properties.SpellingError);
        var extra = properties.Extra?.ToElement();
        if (extra is not null)
        {
            foreach (var attribute in extra.Attributes().Where(attribute => !attribute.IsNamespaceDeclaration))
            {
                element.SetAttributeValue(attribute.Name, attribute.Value);
            }
        }

        if (properties.Outline is { } outline)
        {
            element.Add(WriteOutline(outline, links));
        }

        if (WriteFill(properties.Fill, links) is { } fill)
        {
            element.Add(fill);
        }

        if (properties.Highlight is { } highlight)
        {
            element.Add(ColorElement(A + "highlight", highlight));
        }

        if (properties.Latin is { } latin)
        {
            element.Add(WriteFont(A + "latin", latin));
        }

        if (properties.EastAsian is { } eastAsian)
        {
            element.Add(WriteFont(A + "ea", eastAsian));
        }

        if (properties.ComplexScript is { } complex)
        {
            element.Add(WriteFont(A + "cs", complex));
        }

        if (properties.Symbol is { } symbol)
        {
            element.Add(WriteFont(A + "sym", symbol));
        }

        if (properties.Click is { } click)
        {
            element.Add(WriteHyperlink(click, A + "hlinkClick", links));
        }

        if (properties.Hover is { } hover)
        {
            element.Add(WriteHyperlink(hover, A + "hlinkMouseOver", links));
        }

        if (extra is not null)
        {
            foreach (var child in extra.Elements())
            {
                element.Place(new XElement(child), RunOrder);
            }
        }

        return element;
    }

    public static TextParagraph ReadParagraph(XElement paragraph, IReadLinks links)
    {
        var properties = paragraph.Element(A + "pPr") is { } pPr ? ReadParagraphProperties(pPr, links) : ParagraphProperties.Empty;
        var runs = new List<TextRun>();
        foreach (var child in paragraph.Elements())
        {
            switch (child.Name.LocalName)
            {
                case "pPr" or "endParaRPr":
                    break;
                case "r" when child.Name.Namespace == A:
                    runs.Add(new TextSpan(child.Element(A + "t")?.Value ?? "", RunPropertiesOf(child, links)));
                    break;
                case "br" when child.Name.Namespace == A:
                    runs.Add(new LineBreak(RunPropertiesOf(child, links)));
                    break;
                case "fld" when child.Name.Namespace == A:
                    runs.Add(new TextField(
                        child.Attr("id") ?? "{00000000-0000-0000-0000-000000000000}",
                        child.Attr("type"),
                        child.Element(A + "t")?.Value ?? "",
                        RunPropertiesOf(child, links),
                        child.Element(A + "pPr") is { } fieldParagraph ? ReadParagraphProperties(fieldParagraph, links) : null));
                    break;
                default:
                    runs.Add(new PreservedRun(XmlFragment.From(child)));
                    break;
            }
        }

        var end = paragraph.Element(A + "endParaRPr");
        return new TextParagraph(properties, runs, end is null ? null : ReadRunProperties(end, links));
    }

    private static RunProperties RunPropertiesOf(XElement run, IReadLinks links) =>
        run.Element(A + "rPr") is { } properties ? ReadRunProperties(properties, links) : RunProperties.Empty;

    public static XElement WriteParagraph(TextParagraph paragraph, IWriteLinks links)
    {
        var element = new XElement(A + "p");
        if (paragraph.Properties != ParagraphProperties.Empty)
        {
            element.Add(WriteParagraphProperties(paragraph.Properties, A + "pPr", links));
        }

        foreach (var run in paragraph.Runs)
        {
            switch (run)
            {
                case TextSpan span:
                    element.Add(new XElement(A + "r", RunElement(span.Properties, links), new XElement(A + "t", span.Content)));
                    break;
                case LineBreak lineBreak:
                    element.Add(new XElement(A + "br", RunElement(lineBreak.Properties, links)));
                    break;
                case TextField field:
                    {
                        var fieldElement = new XElement(A + "fld", new XAttribute("id", field.Id));
                        fieldElement.Set("type", field.Type);
                        fieldElement.Add(RunElement(field.Properties, links));
                        if (field.FieldParagraphProperties is { } fieldParagraph)
                        {
                            fieldElement.Add(WriteParagraphProperties(fieldParagraph, A + "pPr", links));
                        }

                        fieldElement.Add(new XElement(A + "t", field.Content));
                        element.Add(fieldElement);
                        break;
                    }

                case PreservedRun preserved:
                    element.Add(preserved.Xml.ToElement());
                    break;
            }
        }

        if (paragraph.EndProperties is { } end)
        {
            element.Add(WriteRunProperties(end, A + "endParaRPr", links));
        }

        return element;
    }

    private static XElement RunElement(RunProperties properties, IWriteLinks links) => WriteRunProperties(properties, A + "rPr", links);

    /// <summary>A text body into p:txBody / a:txBody (<paramref name="name"/>): bodyPr, lstStyle, paragraphs (at least one).</summary>
    public static XElement WriteTextBody(TextBody body, XName name, IWriteLinks links)
    {
        var element = new XElement(name, WriteBodyProperties(body.Body), WriteListStyle(body.ListStyle, A + "lstStyle", links));
        var paragraphs = body.Paragraphs.IsEmpty ? [TextParagraph.Empty] : body.Paragraphs;
        foreach (var paragraph in paragraphs)
        {
            element.Add(WriteParagraph(paragraph, links));
        }

        return element;
    }
}
