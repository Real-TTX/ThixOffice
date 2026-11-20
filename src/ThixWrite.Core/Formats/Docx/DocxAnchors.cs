using System.Globalization;
using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using Dw = DocumentFormat.OpenXml.Drawing.Wordprocessing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Puts the size, position and wrap a picture has in the model into its w:drawing (as read, or new): the extent, wp:inline ⇄ wp:anchor
/// ("Mit Text in Zeile" or floating), positionH/V, the wrap element, behind the text, the distances. Only what differs from the drawing
/// is touched, so an unchanged picture is written exactly as it was read (cropping, effects and the rest stay in any case).
/// </summary>
internal static class DocxAnchors
{
    private const double EmuPerPoint = 12700;

    private static long Emu(double points) => (long)Math.Round(points * EmuPerPoint);

    public static void Patch(W.Drawing drawing, ImageInline image)
    {
        OpenXmlCompositeElement? container = drawing.GetFirstChild<Dw.Inline>();
        container ??= drawing.GetFirstChild<Dw.Anchor>();
        if (container?.GetFirstChild<Dw.DocProperties>() is { } properties && (properties.Description?.Value ?? "") != (image.Description ?? ""))
        {
            properties.Description = image.Description ?? "";
        }

        Place(drawing, image.Width, image.Height, image.Anchor);
    }

    /// <summary>The size and the place (in the line, or floating at <paramref name="anchor"/>) of any w:drawing.</summary>
    public static void Place(W.Drawing drawing, double width, double height, DrawingAnchor? anchor)
    {
        OpenXmlCompositeElement? container = drawing.GetFirstChild<Dw.Inline>();
        container ??= drawing.GetFirstChild<Dw.Anchor>();
        if (container is null)
        {
            return;
        }

        var (cx, cy) = (Emu(width), Emu(height));
        if (container.GetFirstChild<Dw.Extent>() is { } extent && (Math.Abs((extent.Cx?.Value ?? 0) - cx) > 1 || Math.Abs((extent.Cy?.Value ?? 0) - cy) > 1))
        {
            extent.Cx = cx;
            extent.Cy = cy;
            if (container.Descendants<A.Transform2D>().FirstOrDefault()?.Extents is { } pictureExtent)
            {
                pictureExtent.Cx = cx;
                pictureExtent.Cy = cy;
            }
        }

        switch (container)
        {
            case Dw.Inline inline when anchor is { } floatingAnchor:
                drawing.ReplaceChild(BuildAnchor(floatingAnchor, inline), inline);
                break;
            case Dw.Anchor floating when anchor is null:
                drawing.ReplaceChild(BuildInline(floating), floating);
                break;
            case Dw.Anchor floating when anchor is { } changed && DocxDrawings.ReadAnchor(floating) != changed:
                Update(floating, changed);
                break;
        }
    }

    /// <summary>A wp:anchor for a picture that was in the line: the anchor's position and wrap, the inline's extent, name and graphic.</summary>
    private static Dw.Anchor BuildAnchor(DrawingAnchor anchor, Dw.Inline inline)
    {
        var result = new Dw.Anchor();
        SetAttributes(result, anchor);
        result.Append(new Dw.SimplePosition { X = 0, Y = 0 });
        result.Append(Position(anchor.HorizontalFrame, anchor.OffsetX, anchor.AlignX, horizontal: true));
        result.Append(Position(anchor.VerticalFrame, anchor.OffsetY, anchor.AlignY, horizontal: false));
        result.Append(inline.GetFirstChild<Dw.Extent>()?.CloneNode(true) ?? new Dw.Extent { Cx = 0, Cy = 0 });
        result.Append(inline.GetFirstChild<Dw.EffectExtent>()?.CloneNode(true) ?? new Dw.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 });
        result.Append(Wrap(anchor.Wrap));
        CopyTail(inline, result);
        return result;
    }

    /// <summary>A wp:inline for a floating picture put "Mit Text in Zeile".</summary>
    private static Dw.Inline BuildInline(Dw.Anchor anchor)
    {
        var result = new Dw.Inline { DistanceFromTop = 0U, DistanceFromBottom = 0U, DistanceFromLeft = 0U, DistanceFromRight = 0U };
        result.Append(anchor.GetFirstChild<Dw.Extent>()?.CloneNode(true) ?? new Dw.Extent { Cx = 0, Cy = 0 });
        result.Append(anchor.GetFirstChild<Dw.EffectExtent>()?.CloneNode(true) ?? new Dw.EffectExtent { LeftEdge = 0, TopEdge = 0, RightEdge = 0, BottomEdge = 0 });
        CopyTail(anchor, result);
        return result;
    }

    /// <summary>docPr, cNvGraphicFramePr and the graphic from one container into another.</summary>
    private static void CopyTail(OpenXmlCompositeElement from, OpenXmlCompositeElement to)
    {
        foreach (var child in from.ChildElements.Where(child => child is Dw.DocProperties or Dw.NonVisualGraphicFrameDrawingProperties or A.Graphic))
        {
            to.Append(child.CloneNode(true));
        }
    }

    /// <summary>A floating picture moved or wrapped otherwise: its position, wrap, distances and layer, the rest as read.</summary>
    private static void Update(Dw.Anchor element, DrawingAnchor anchor)
    {
        var read = DocxDrawings.ReadAnchor(element);
        SetAttributes(element, anchor);
        if (read.HorizontalFrame != anchor.HorizontalFrame || read.OffsetX != anchor.OffsetX || read.AlignX != anchor.AlignX || element.GetFirstChild<Dw.HorizontalPosition>() is null)
        {
            Replace(element, element.GetFirstChild<Dw.HorizontalPosition>(), Position(anchor.HorizontalFrame, anchor.OffsetX, anchor.AlignX, horizontal: true), after: element.GetFirstChild<Dw.SimplePosition>());
        }

        if (read.VerticalFrame != anchor.VerticalFrame || read.OffsetY != anchor.OffsetY || read.AlignY != anchor.AlignY || element.GetFirstChild<Dw.VerticalPosition>() is null)
        {
            Replace(element, element.GetFirstChild<Dw.VerticalPosition>(), Position(anchor.VerticalFrame, anchor.OffsetY, anchor.AlignY, horizontal: false), after: element.GetFirstChild<Dw.HorizontalPosition>());
        }

        if (read.Wrap != anchor.Wrap)
        {
            var wrap = element.ChildElements.FirstOrDefault(child => child.LocalName.StartsWith("wrap", StringComparison.Ordinal));
            Replace(element, wrap, Wrap(anchor.Wrap), after: element.GetFirstChild<Dw.EffectExtent>() ?? (OpenXmlElement?)element.GetFirstChild<Dw.Extent>());
        }
    }

    private static void Replace(OpenXmlCompositeElement parent, OpenXmlElement? old, OpenXmlElement replacement, OpenXmlElement? after)
    {
        if (old is not null)
        {
            parent.ReplaceChild(replacement, old);
        }
        else if (after is not null)
        {
            parent.InsertAfter(replacement, after);
        }
        else
        {
            parent.PrependChild(replacement);
        }
    }

    private static void SetAttributes(Dw.Anchor element, DrawingAnchor anchor)
    {
        element.DistanceFromTop = (uint)Math.Max(0, Emu(anchor.DistanceTop));
        element.DistanceFromBottom = (uint)Math.Max(0, Emu(anchor.DistanceBottom));
        element.DistanceFromLeft = (uint)Math.Max(0, Emu(anchor.DistanceLeft));
        element.DistanceFromRight = (uint)Math.Max(0, Emu(anchor.DistanceRight));
        element.SimplePos = false;
        element.RelativeHeight = (uint)Math.Clamp(anchor.ZOrder, 0, uint.MaxValue);
        element.BehindDoc = anchor.BehindText;
        element.Locked ??= false;
        element.LayoutInCell ??= true;
        element.AllowOverlap ??= true;
    }

    private static OpenXmlElement Position(DrawingFrame frame, double offset, string? align, bool horizontal)
    {
        var relative = frame switch
        {
            DrawingFrame.Page => "page",
            DrawingFrame.Margin => "margin",
            DrawingFrame.Line => "line",
            DrawingFrame.Character => "character",
            DrawingFrame.LeftMargin => "leftMargin",
            DrawingFrame.RightMargin => "rightMargin",
            DrawingFrame.TopMargin => "topMargin",
            DrawingFrame.BottomMargin => "bottomMargin",
            DrawingFrame.InsideMargin => "insideMargin",
            DrawingFrame.OutsideMargin => "outsideMargin",
            DrawingFrame.Column => horizontal ? "column" : "paragraph",
            _ => horizontal ? "column" : "paragraph",
        };
        OpenXmlElement value = align is not null
            ? horizontal ? new Dw.HorizontalAlignment(align) : new Dw.VerticalAlignment(align)
            : new Dw.PositionOffset(Emu(offset).ToString(CultureInfo.InvariantCulture));
        if (horizontal)
        {
            return new Dw.HorizontalPosition(value) { RelativeFrom = new Dw.HorizontalRelativePositionValues(relative) };
        }

        return new Dw.VerticalPosition(value) { RelativeFrom = new Dw.VerticalRelativePositionValues(relative) };
    }

    private static OpenXmlElement Wrap(DrawingWrap wrap) => wrap switch
    {
        DrawingWrap.TopAndBottom => new Dw.WrapTopBottom(),
        DrawingWrap.None => new Dw.WrapNone(),
        _ => new Dw.WrapSquare { WrapText = Dw.WrapTextValues.BothSides },
    };
}
