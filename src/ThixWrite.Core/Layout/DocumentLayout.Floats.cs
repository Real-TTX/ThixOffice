using System.Runtime.CompilerServices;
using ThixOffice.Charts;
using ThixOffice.Charts.Formats;

using ThixOffice.Text.Drawing;
using ThixOffice.Text.Drawing.Geometry;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>
/// Floating objects (wp:anchor pictures, shapes, text boxes, groups, charts) and the drawings ThixWrite keeps as XML: an anchored
/// object goes onto the page of its paragraph at the place its anchor describes (relative to the page, the margins, the column, the
/// paragraph or the line, by offset or alignment); text after it keeps clear of its area with the distances of its wrap ("Quadrat",
/// "Passend", "Oben und unten"), objects behind the text or in front of it leave the text alone. Drawing: the shapes through the
/// shared geometry engine and vector layer, text boxes as a small layout of their story, charts through ThixOffice.Charts from the
/// cached values.
/// </summary>
public sealed partial class DocumentLayout
{
    private static readonly string[] AccentNames = ["accent1", "accent2", "accent3", "accent4", "accent5", "accent6"];
    private readonly ConditionalWeakTable<Story, Dictionary<double, DocumentLayout>> _storyBoxes = new();
    private readonly ConditionalWeakTable<string, Chart?> _charts = new();

    /// <summary>The anchored objects of a paragraph: the inline, its offset, anchor and size.</summary>
    private static IEnumerable<(int Offset, Inline Inline, DrawingAnchor Anchor, double Width, double Height)> AnchoredIn(Paragraph paragraph)
    {
        for (var index = 0; index < paragraph.Inlines.Length; index++)
        {
            switch (paragraph.Inlines[index])
            {
                case ImageInline { Anchor: { } anchor } image:
                    yield return (paragraph.StartOf(index), image, anchor, image.Width, image.Height);
                    break;
                case OpaqueInline { Graphic: { Anchor: { } anchor } graphic } opaque:
                    yield return (paragraph.StartOf(index), opaque, anchor, graphic.Width, graphic.Height);
                    break;
            }
        }
    }

    /// <summary>
    /// The top-left corner of an anchored object of <paramref name="width"/> × <paramref name="height"/> on a page of
    /// <paramref name="setup"/>: <paramref name="paragraphTop"/> is the top of its paragraph (frames "paragraph" and "line"),
    /// <paramref name="areaLeft"/>/<paramref name="areaWidth"/> the text area it is in (frame "column").
    /// </summary>
    internal static (double X, double Y) FloatPosition(DrawingAnchor anchor, double width, double height, SectionProperties setup, double paragraphTop,
        double areaLeft, double areaWidth)
    {
        var marginLeft = setup.MarginLeft + setup.Gutter;
        var (frameX, frameWidth) = anchor.HorizontalFrame switch
        {
            DrawingFrame.Page => (0.0, setup.PageWidth),
            DrawingFrame.Margin => (marginLeft, setup.ContentWidth),
            DrawingFrame.LeftMargin or DrawingFrame.InsideMargin => (0.0, marginLeft),
            DrawingFrame.RightMargin or DrawingFrame.OutsideMargin => (setup.PageWidth - setup.MarginRight, setup.MarginRight),
            _ => (areaLeft, areaWidth),
        };
        var (frameY, frameHeight) = anchor.VerticalFrame switch
        {
            DrawingFrame.Page => (0.0, setup.PageHeight),
            DrawingFrame.Margin => (setup.MarginTop, setup.ContentHeight),
            DrawingFrame.TopMargin => (0.0, setup.MarginTop),
            DrawingFrame.BottomMargin => (setup.PageHeight - setup.MarginBottom, setup.MarginBottom),
            _ => (paragraphTop, 0.0),
        };
        var x = anchor.AlignX switch
        {
            "center" => frameX + (frameWidth - width) / 2,
            "right" or "outside" => frameX + frameWidth - width,
            "left" or "inside" => frameX,
            _ => frameX + anchor.OffsetX,
        };
        var y = anchor.AlignY switch
        {
            "center" => frameY + (frameHeight - height) / 2,
            "bottom" or "outside" => frameY + frameHeight - height,
            "top" or "inside" => frameY,
            _ => frameY + anchor.OffsetY,
        };
        return (x, y);
    }

    private sealed partial class Flow
    {
        /// <summary>Puts the anchored objects of a paragraph that starts at <paramref name="top"/> onto the current page.</summary>
        private void PlaceFloats(int block, Paragraph paragraph, double top, double areaLeft, double areaWidth)
        {
            foreach (var (offset, inline, anchor, width, height) in AnchoredIn(paragraph))
            {
                var (x, y) = FloatPosition(anchor, width, height, Setup, top, areaLeft, areaWidth);
                Page.MutableFloats.Add(new PlacedFloat(block, offset, inline, anchor, x, y, width, height));
            }
        }

        /// <summary>
        /// The paragraph laid out around the floating objects of the page that reach below <see cref="_y"/> (the layout as it is when
        /// none is in its way).
        /// </summary>
        private ParagraphLayout Wrapped(int block, Paragraph paragraph, ParagraphLayout layout, double areaLeft, double width)
        {
            var exclusions = new List<(double Left, double Top, double Right, double Bottom)>();
            foreach (var placed in Page.Floats)
            {
                if (placed.Exclusion is { } area && area.Bottom > _y && area.Top < _y + layout.Height + 200)
                {
                    exclusions.Add((area.Left - areaLeft, area.Top - _y, area.Right - areaLeft, area.Bottom - _y));
                }
            }

            if (exclusions.Count == 0)
            {
                return layout;
            }

            var label = block < owner._labels.Length ? owner._labels[block] : null;
            owner._context.NoteStart = layout.NoteStart;
            var wrapped = ParagraphLayouter.Layout(paragraph, owner._context.Resolver.ResolveParagraph(paragraph), label, width, owner._context, exclusions);
            wrapped.NoteStart = layout.NoteStart;
            owner._layouts[block] = wrapped;
            return wrapped;
        }
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>The display items of an object at (<paramref name="x"/>, <paramref name="y"/>) in a box of <paramref name="width"/> × <paramref name="height"/>.</summary>
    internal IEnumerable<DisplayItem> DrawObject(Inline inline, double x, double y, double width, double height)
    {
        switch (inline)
        {
            case ImageInline image:
                return [new ImageItem(x, y, width, height, image.Image.Key, image.Image.Bytes)];
            case OpaqueInline { Graphic: { } graphic }:
                return DrawGraphic(graphic, x, y, width, height);
            default:
                return [];
        }
    }

    private List<DisplayItem> DrawGraphic(DrawingGraphic graphic, double x, double y, double width, double height)
    {
        var items = new List<DisplayItem>();
        var scaleX = graphic.Width > 0 ? width / graphic.Width : 1;
        var scaleY = graphic.Height > 0 ? height / graphic.Height : 1;
        items.Add(new TransformBeginItem(Matrix2D.Scaling(scaleX, scaleY) * Matrix2D.Translation(x, y)));
        foreach (var element in graphic.Elements)
        {
            var center = (X: element.X + element.Width / 2, Y: element.Y + element.Height / 2);
            var transformed = element.Rotation != 0 || element.FlipHorizontal || element.FlipVertical;
            if (transformed)
            {
                var matrix = Matrix2D.Scaling(element.FlipHorizontal ? -1 : 1, element.FlipVertical ? -1 : 1, center.X, center.Y) * Matrix2D.Rotation(element.Rotation, center.X, center.Y);
                items.Add(new TransformBeginItem(matrix));
            }

            switch (element)
            {
                case DrawingShape shape:
                    DrawShape(shape, items);
                    break;
                case DrawingPicture picture:
                    items.Add(new ImageItem(picture.X, picture.Y, picture.Width, picture.Height, picture.Image.Key, picture.Image.Bytes));
                    break;
                case DrawingChart chart:
                    DrawChart(chart, items);
                    break;
            }

            if (transformed)
            {
                items.Add(TransformEndItem.Instance);
            }
        }

        items.Add(TransformEndItem.Instance);
        return items;
    }

    private void DrawShape(DrawingShape shape, List<DisplayItem> items)
    {
        var geometry = GeometryEvaluator.Evaluate(shape.Preset, shape.Adjustments.Items, Math.Max(shape.Width, 0.01), Math.Max(shape.Height, 0.01), shape.CustomGeometryXml);
        if (shape.Width > 0 || shape.Height > 0)
        {
            var fill = PaintOf(shape.Fill, shape);
            var stroke = shape.Line is { } line && Color(line.Color) is { } lineColor
                ? new StrokeStyle(new SolidPaint(lineColor), Math.Max(0.25, line.Width), Dashes(line.Dash, line.Width))
                : null;
            foreach (var path in geometry.Paths)
            {
                var placed = path.Path.Transform(Matrix2D.Translation(shape.X, shape.Y));
                var pathFill = path.Fill == PathFillMode.None ? null : fill;
                var pathStroke = path.Stroke ? stroke : null;
                if (pathFill is not null || pathStroke is not null)
                {
                    items.Add(new VectorPathItem(placed, pathFill, pathStroke));
                }
            }
        }

        if (shape.Text is { Blocks.Count: > 0 } text)
        {
            DrawText(shape, text, geometry, items);
        }
    }

    /// <summary>The text of a shape or text box: its story laid out at the box's inner width, placed by the vertical anchor.</summary>
    private void DrawText(DrawingShape shape, Story text, EvaluatedGeometry geometry, List<DisplayItem> items)
    {
        // The text rectangle of the geometry (an ellipse's inner box …), less the insets; the text is cut at the shape's box like in Word.
        var (insetLeft, insetTop, insetRight, insetBottom) = shape.Insets;
        var boxLeft = shape.X + geometry.TextLeft;
        var boxTop = shape.Y + geometry.TextTop;
        var boxWidth = geometry.TextRight - geometry.TextLeft;
        var boxHeight = geometry.TextBottom - geometry.TextTop;
        var width = Math.Max(8, boxWidth - insetLeft - insetRight);
        var layout = StoryBox(text, width);
        var page = layout.Pages[0];
        var bottom = page.Lines.Count == 0 ? 0 : page.Lines.Max(line => line.Top + line.Line.Height);
        foreach (var cell in page.Cells)
        {
            bottom = Math.Max(bottom, cell.Y + cell.Height);
        }

        var room = boxHeight - insetTop - insetBottom;
        var top = boxTop + insetTop + shape.TextAnchor switch
        {
            DrawingTextAnchor.Center => (room - bottom) / 2,
            DrawingTextAnchor.Bottom => room - bottom,
            _ => 0,
        };
        var clip = !shape.AutoFit;
        if (clip)
        {
            items.Add(new ClipBeginItem(shape.X, shape.Y, Math.Max(1, shape.Width), Math.Max(1, shape.Height)));
        }

        items.Add(TransformBeginItem.At(boxLeft + insetLeft, top));
        items.AddRange(layout.GetDisplayList(0).Items);
        items.Add(TransformEndItem.Instance);
        if (clip)
        {
            items.Add(ClipEndItem.Instance);
        }
    }

    /// <summary>A layout of a story as one tall page of <paramref name="width"/> without margins (text boxes), cached per story and width.</summary>
    private DocumentLayout StoryBox(Story story, double width)
    {
        var byWidth = _storyBoxes.GetOrCreateValue(story);
        var key = Math.Round(width, 1);
        if (!byWidth.TryGetValue(key, out var layout))
        {
            var setup = new SectionProperties
            {
                PageWidth = width,
                PageHeight = 20000,
                MarginLeft = 0,
                MarginRight = 0,
                MarginTop = 0,
                MarginBottom = 0,
                HeaderDistance = 0,
                FooterDistance = 0,
            };
            var document = new Document(story.Blocks, Document.Styles, Document.Numbering, setup, Document.Theme, Document.Settings);
            layout = new DocumentLayout(document, Measurer, Options);
            byWidth[key] = layout;
        }

        return layout;
    }

    private void DrawChart(DrawingChart element, List<DisplayItem> items)
    {
        var chart = _charts.GetValue(element.ChartXml, xml =>
        {
            try
            {
                return ChartXml.Read(xml);
            }
            catch (Exception exception) when (exception is System.Xml.XmlException or FormatException or InvalidOperationException or ArgumentException)
            {
                return null;
            }
        });
        if (chart is null)
        {
            items.Add(new RectangleItem(element.X, element.Y, element.Width, element.Height, null, new RgbColor(0xA6, 0xA6, 0xA6), 0.5));
            return;
        }

        var data = ChartDataResolver.Resolve(chart, null);
        var rendered = ChartRenderer.Render(data, new ChartRenderOptions
        {
            Measurer = Measurer,
            Accents = [.. AccentNames.Select(name => Document.Theme.ColorOf(name) is { } hex ? RgbColor.FromHex(hex) : RgbColor.Black)],
            FontFamily = Document.Theme.MinorFont,
            Width = element.Width,
            Height = element.Height,
        });
        items.Add(TransformBeginItem.At(element.X, element.Y));
        items.AddRange(rendered);
        items.Add(TransformEndItem.Instance);
    }

    private Paint? PaintOf(DrawingFill? fill, DrawingShape shape)
    {
        if (fill is null)
        {
            return null;
        }

        if (fill.Stops.Count >= 2)
        {
            var stops = fill.Stops.Select(stop => new GradientColorStop(stop.Position, Color(stop.Color) ?? RgbColor.White)).ToList();
            var radians = fill.Angle * Math.PI / 180;
            var (cx, cy) = (shape.X + shape.Width / 2, shape.Y + shape.Height / 2);
            var (dx, dy) = (Math.Cos(radians) * shape.Width / 2, Math.Sin(radians) * shape.Height / 2);
            return new LinearGradientPaint(cx - dx, cy - dy, cx + dx, cy + dy, stops);
        }

        return fill.Color is { } color && Color(color) is { } rgb ? new SolidPaint(rgb) : null;
    }

    /// <summary>A drawing colour with the document's theme (null when it names nothing known).</summary>
    private RgbColor? Color(DrawingColor color)
    {
        var baseHex = color.Rgb ?? (color.Scheme is { } scheme ? Document.Theme.ColorOf(DocxDrawings.SchemeName(scheme)) : null);
        return baseHex is { Length: 6 } ? ColorTransforms.Apply(RgbColor.FromHex(baseHex), color.Modifiers) : null;
    }

    private static IReadOnlyList<double>? Dashes(string dash, double width)
    {
        var unit = Math.Max(1, width);
        return dash switch
        {
            "dash" or "sysDash" => [unit * 4, unit * 3],
            "dot" or "sysDot" => [unit, unit * 2],
            "dashDot" or "sysDashDot" => [unit * 4, unit * 2, unit, unit * 2],
            "lgDash" => [unit * 8, unit * 3],
            _ => null,
        };
    }
}
