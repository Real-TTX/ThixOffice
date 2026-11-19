using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>
/// The chart seam: draws a chart graphic frame into a box of <paramref name="width"/> × <paramref name="height"/> points (items in
/// the frame's coordinates, 0,0 = its top-left). The shared chart project plugs in here; without one ThixShow draws a placeholder.
/// </summary>
public interface IChartRenderer
{
    IReadOnlyList<DisplayItem>? Render(GraphicFrame frame, double width, double height, SlideContext context);
}

/// <summary>
/// How a slide is drawn: <see cref="ShowPlaceholderPrompts"/> for the editor (empty placeholders show their prompt and a dashed
/// frame; off for thumbnails, the show, PDF), the chart renderer, "now" for date fields (null keeps their stored text – deterministic).
/// </summary>
public sealed record RenderOptions(bool ShowPlaceholderPrompts = false, IChartRenderer? Charts = null, DateTime? Now = null);

/// <summary>
/// Renders slides, layouts and masters into ThixOffice.Text display lists (points, 0,0 = the slide's top-left), UI-free and
/// deterministic: background (own, layout's, master's, theme reference), the master's and layout's own shapes when shown, then the
/// slide's shapes in z-order – geometry paths with fills, outlines, dashes, arrowheads and shadows, pictures (cropped, stretched,
/// tiled, clipped to their geometry), groups with nested transforms, tables, graphic frames (chart seam, placeholders), text bodies
/// with autofit. Rotation and flips are display-list transforms.
/// </summary>
public static class SlideRenderer
{
    /// <summary>The slide as a display list at <paramref name="scale"/> (1 = points of the slide size).</summary>
    public static PageDisplayList Render(Presentation presentation, Slide slide, ITextMeasurer measurer, RenderOptions? options = null, double scale = 1)
    {
        var context = SlideContext.For(presentation, slide);
        var items = new List<DisplayItem>();
        RenderSlideContent(items, context, measurer, options ?? new RenderOptions(), presentation.IndexOf(slide.Id) + 1, null);
        return Page(presentation.IndexOf(slide.Id), presentation.SlideSize, items, scale);
    }

    /// <summary>A thumbnail: the slide scaled to <paramref name="width"/> points wide.</summary>
    public static PageDisplayList RenderThumbnail(Presentation presentation, Slide slide, ITextMeasurer measurer, double width) =>
        Render(presentation, slide, measurer, null, width / presentation.SlideSize.WidthPoints);

    /// <summary>A layout as the editor's master view shows it (its master's shapes under its own, prompts visible).</summary>
    public static PageDisplayList RenderLayout(Presentation presentation, SlideMaster master, SlideLayout layout, ITextMeasurer measurer, double scale = 1)
    {
        var context = SlideContext.ForLayout(presentation, master, layout);
        var items = new List<DisplayItem>();
        var options = new RenderOptions(ShowPlaceholderPrompts: true);
        AddBackground(items, context, presentation.SlideSize);
        if (layout.ShowMasterShapes)
        {
            RenderElements(items, SlideContext.ForMaster(presentation, master), master.Shapes.Where(shape => shape.Placeholder is null), measurer, options, 1);
        }

        RenderElements(items, context, layout.Shapes, measurer, options, 1);
        return Page(0, presentation.SlideSize, items, scale);
    }

    /// <summary>A master with its placeholders (prompts visible).</summary>
    public static PageDisplayList RenderMaster(Presentation presentation, SlideMaster master, ITextMeasurer measurer, double scale = 1)
    {
        var context = SlideContext.ForMaster(presentation, master);
        var items = new List<DisplayItem>();
        AddBackground(items, context, presentation.SlideSize);
        RenderElements(items, context, master.Shapes, measurer, new RenderOptions(ShowPlaceholderPrompts: true), 1);
        return Page(0, presentation.SlideSize, items, scale);
    }

    private static PageDisplayList Page(int index, SlideSize size, List<DisplayItem> items, double scale)
    {
        if (Math.Abs(scale - 1) > 1e-12)
        {
            items.Insert(0, new TransformBeginItem(Matrix2D.Scaling(scale, scale)));
            items.Add(TransformEndItem.Instance);
        }

        return new PageDisplayList(index, size.WidthPoints * scale, size.HeightPoints * scale, items);
    }

    /// <summary>
    /// Background, master and layout shapes, then the slide's shapes (or only those <paramref name="filter"/> accepts – the slide
    /// show draws animated shapes as layers of their own).
    /// </summary>
    internal static void RenderSlideContent(List<DisplayItem> items, SlideContext context, ITextMeasurer measurer, RenderOptions options, int slideNumber, Func<SlideElement, bool>? filter)
    {
        var slide = (Slide)context.Slide;
        if (filter is null)
        {
            RenderBackdrop(items, context, measurer, options, slideNumber);
        }

        RenderElements(items, context, filter is null ? slide.Shapes : slide.Shapes.Where(filter), measurer, options, slideNumber);
    }

    /// <summary>The background and the master's and layout's own (non-placeholder) shapes under a slide.</summary>
    internal static void RenderBackdrop(List<DisplayItem> items, SlideContext context, ITextMeasurer measurer, RenderOptions options, int slideNumber)
    {
        var slide = (Slide)context.Slide;
        AddBackground(items, context, context.Presentation.SlideSize);
        if (!slide.ShowMasterShapes)
        {
            return;
        }

        if (context.Layout is { ShowMasterShapes: true })
        {
            RenderElements(items, SlideContext.ForMaster(context.Presentation, context.Master), context.Master.Shapes.Where(shape => shape.Placeholder is null), measurer, options, slideNumber);
        }

        if (context.Layout is { } layout)
        {
            RenderElements(items, SlideContext.ForLayout(context.Presentation, context.Master, layout), layout.Shapes.Where(shape => shape.Placeholder is null), measurer, options, slideNumber);
        }
    }

    internal static void AddBackground(List<DisplayItem> items, SlideContext context, SlideSize size)
    {
        var fill = ShapeResolver.ResolveBackground(context);
        var (width, height) = (size.WidthPoints, size.HeightPoints);
        if (Paints.Fill(fill, context.Colors, width, height) is not { } paint)
        {
            items.Add(new RectangleItem(0, 0, width, height, RgbColor.White));
            return;
        }

        if (paint is SolidPaint solid && solid.Color.A == 255)
        {
            items.Add(new RectangleItem(0, 0, width, height, solid.Color));
            return;
        }

        items.Add(new RectangleItem(0, 0, width, height, RgbColor.White));
        items.Add(new VectorPathItem(VectorPath.Rectangle(0, 0, width, height), paint));
    }

    internal static void RenderElements(List<DisplayItem> items, SlideContext context, IEnumerable<SlideElement> elements, ITextMeasurer measurer, RenderOptions options, int slideNumber)
    {
        foreach (var element in elements)
        {
            RenderElement(items, context, element, measurer, options, slideNumber, null);
        }
    }

    /// <summary>Renders one element (and a group's children) in slide coordinates.</summary>
    public static void RenderElement(List<DisplayItem> items, SlideContext context, SlideElement element, ITextMeasurer measurer, RenderOptions options, int slideNumber, Fill? groupFill)
    {
        if (element.Hidden)
        {
            return;
        }

        switch (element)
        {
            case GroupShape group:
                RenderGroup(items, context, group, measurer, options, slideNumber);
                break;
            case AutoShape or ConnectorShape:
                RenderShape(items, context, element, measurer, options, slideNumber, groupFill);
                break;
            case PictureShape picture:
                RenderPicture(items, context, picture);
                break;
            case GraphicFrame frame:
                RenderFrame(items, context, frame, measurer, options);
                break;
        }
    }

    /// <summary>The transform of a shape's box (0..w, 0..h in points) onto its parent's space: flips, rotation about the centre, offset.</summary>
    public static Matrix2D ShapeMatrix(Transform2D transform)
    {
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        var (centerX, centerY) = (width / 2, height / 2);
        var matrix = Matrix2D.Identity;
        if (transform.FlipHorizontal)
        {
            matrix *= Matrix2D.Scaling(-1, 1, centerX, centerY);
        }

        if (transform.FlipVertical)
        {
            matrix *= Matrix2D.Scaling(1, -1, centerX, centerY);
        }

        if (transform.Rotation != 0)
        {
            matrix *= Matrix2D.Rotation(transform.Rotation / 60000.0, centerX, centerY);
        }

        return matrix * Matrix2D.Translation(Units.EmuToPoints(transform.X), Units.EmuToPoints(transform.Y));
    }

    /// <summary>The transform of a shape's text: rotation (plus 180° when flipped vertically) but never mirrored.</summary>
    public static Matrix2D TextMatrix(Transform2D transform)
    {
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        var rotation = (transform.Rotation / 60000.0) + (transform.FlipVertical ? 180 : 0);
        var matrix = rotation % 360 != 0 ? Matrix2D.Rotation(rotation, width / 2, height / 2) : Matrix2D.Identity;
        return matrix * Matrix2D.Translation(Units.EmuToPoints(transform.X), Units.EmuToPoints(transform.Y));
    }

    /// <summary>The transform of a group's child space onto its parent's space.</summary>
    public static Matrix2D GroupMatrix(Transform2D transform)
    {
        var scaleX = transform.ChildWidth != 0 ? transform.Width / (double)transform.ChildWidth : 1;
        var scaleY = transform.ChildHeight != 0 ? transform.Height / (double)transform.ChildHeight : 1;
        return Matrix2D.Translation(-Units.EmuToPoints(transform.ChildX), -Units.EmuToPoints(transform.ChildY))
               * Matrix2D.Scaling(scaleX, scaleY)
               * ShapeMatrix(transform);
    }

    private static void RenderGroup(List<DisplayItem> items, SlideContext context, GroupShape group, ITextMeasurer measurer, RenderOptions options, int slideNumber)
    {
        if (group.Transform is not { } transform)
        {
            foreach (var child in group.Children)
            {
                RenderElement(items, context, child, measurer, options, slideNumber, group.Fill);
            }

            return;
        }

        items.Add(new TransformBeginItem(GroupMatrix(transform)));
        foreach (var child in group.Children)
        {
            RenderElement(items, context, child, measurer, options, slideNumber, group.Fill);
        }

        items.Add(TransformEndItem.Instance);
    }

    private static void RenderShape(List<DisplayItem> items, SlideContext context, SlideElement shape, ITextMeasurer measurer, RenderOptions options, int slideNumber, Fill? groupFill)
    {
        var resolved = ShapeResolver.Resolve(context, shape);
        var text = resolved.Text;
        var emptyPlaceholder = shape.Placeholder is not null && (text is null || text.IsEmpty);
        if (emptyPlaceholder && context.Kind == SlideKind.Slide && !options.ShowPlaceholderPrompts)
        {
            return;
        }

        var transform = resolved.Transform;
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        var geometry = GeometryEngine.Evaluate(resolved.Geometry, width, height);
        var fill = resolved.Fill is GroupFill ? groupFill : resolved.Fill;
        var stroke = Paints.Stroke(resolved.Outline, resolved.Colors);
        var shadow = Paints.Shadow(resolved.Shadow, resolved.Colors);

        items.Add(new TransformBeginItem(ShapeMatrix(transform)));
        var first = true;
        foreach (var path in geometry.Paths)
        {
            var paint = Paints.Fill(fill, resolved.Colors, width, height, path.Fill);
            var pathStroke = path.Stroke ? stroke : null;
            if (paint is null && pathStroke is null)
            {
                continue;
            }

            items.Add(new VectorPathItem(path.Path, paint, pathStroke, first ? shadow : null));
            first = false;
            if (pathStroke is not null && resolved.Outline is { } outline && (outline.Head is not null || outline.Tail is not null))
            {
                items.AddRange(Paints.Arrowheads(path.Path, outline.Head, outline.Tail, pathStroke));
            }
        }

        if (emptyPlaceholder && options.ShowPlaceholderPrompts)
        {
            var frame = new StrokeStyle(new SolidPaint(new RgbColor(0x8C, 0x8C, 0x8C)), 0.75, [3, 2]);
            items.Add(new VectorPathItem(VectorPath.Rectangle(0, 0, width, height), null, frame));
        }

        items.Add(TransformEndItem.Instance);

        var shownText = text;
        if (emptyPlaceholder && options.ShowPlaceholderPrompts)
        {
            shownText = PromptOf(context, shape, resolved);
        }

        if (shownText is null || shownText.IsEmpty)
        {
            return;
        }

        var layoutOptions = new TextLayoutOptions(SlideNumber: slideNumber, Now: options.Now);
        var textItems = LayoutText(resolved, shownText, context, measurer, geometry, width, height, layoutOptions, out var textMatrix);
        items.Add(new TransformBeginItem(textMatrix * TextMatrix(transform)));
        items.AddRange(textItems);
        items.Add(TransformEndItem.Instance);
    }

    /// <summary>Lays out the text in the geometry's text rectangle (vertical text turned into its box).</summary>
    internal static IReadOnlyList<DisplayItem> LayoutText(ResolvedShape resolved, TextBody text, SlideContext context, ITextMeasurer measurer, EvaluatedGeometry geometry, double width, double height, TextLayoutOptions options, out Matrix2D matrix)
    {
        var layout = LayoutTextBody(resolved, text, context, measurer, geometry, width, height, options, out matrix);
        return layout.Items;
    }

    /// <summary>The text layout of a shape in its own box coordinates (and the matrix that maps vertical text into the box).</summary>
    public static TextBodyLayout LayoutTextBody(ResolvedShape resolved, TextBody text, SlideContext context, ITextMeasurer measurer, EvaluatedGeometry geometry, double width, double height, TextLayoutOptions options, out Matrix2D matrix)
    {
        var left = geometry.TextLeft;
        var top = geometry.TextTop;
        var textWidth = Math.Max(0, geometry.TextRight - geometry.TextLeft);
        var textHeight = Math.Max(0, geometry.TextBottom - geometry.TextTop);
        if (resolved.Transform.FlipHorizontal)
        {
            left = width - geometry.TextRight;
        }

        if (resolved.Transform.FlipVertical)
        {
            top = height - geometry.TextBottom;
        }

        matrix = Matrix2D.Identity;
        switch (resolved.Body.Vertical)
        {
            case "vert" or "eaVert" or "mongolianVert" or "wordArtVertRtl":
                matrix = Matrix2D.Translation(-left, -top) * Matrix2D.Rotation(90) * Matrix2D.Translation(left + textWidth, top);
                return TextBodyLayouter.Layout(resolved, text, context, measurer, left, top, textHeight, textWidth, options);
            case "vert270":
                matrix = Matrix2D.Translation(-left, -top) * Matrix2D.Rotation(-90) * Matrix2D.Translation(left, top + textHeight);
                return TextBodyLayouter.Layout(resolved, text, context, measurer, left, top, textHeight, textWidth, options);
            default:
                return TextBodyLayouter.Layout(resolved, text, context, measurer, left, top, textWidth, textHeight, options);
        }
    }

    /// <summary>The prompt an empty placeholder shows in the editor: the layout's custom prompt, else the type's German default.</summary>
    private static TextBody PromptOf(SlideContext context, SlideElement shape, ResolvedShape resolved)
    {
        var placeholder = shape.Placeholder!;
        string prompt;
        if (context.LayoutPlaceholder(placeholder) is AutoShape { Placeholder.HasCustomPrompt: true, Text: { IsEmpty: false } layoutText })
        {
            prompt = layoutText.PlainText;
        }
        else
        {
            prompt = placeholder.Type switch
            {
                PlaceholderType.Title or PlaceholderType.CenteredTitle => "Titel durch Klicken hinzufügen",
                PlaceholderType.Subtitle => "Untertitel durch Klicken hinzufügen",
                PlaceholderType.Picture => "Bild durch Klicken auf Symbol hinzufügen",
                PlaceholderType.Chart => "Diagramm durch Klicken auf Symbol hinzufügen",
                PlaceholderType.Table => "Tabelle durch Klicken auf Symbol hinzufügen",
                PlaceholderType.DateAndTime or PlaceholderType.Footer or PlaceholderType.SlideNumber or PlaceholderType.Header => "",
                _ => "Text durch Klicken hinzufügen",
            };
            if (context.Kind != SlideKind.Slide && shape is AutoShape { Text: { IsEmpty: false } own })
            {
                prompt = own.PlainText;
            }
        }

        var gray = new RunProperties(Fill: new SolidFill(ColorSpec.Scheme("tx1").LumMod(50000).LumOff(50000)));
        return new TextBody(BodyProperties.Empty, ListStyle.Empty, prompt.Split('\n').Select(line => TextParagraph.Plain(line, gray)).ToList());
    }

    private static void RenderPicture(List<DisplayItem> items, SlideContext context, PictureShape picture)
    {
        var resolved = ShapeResolver.Resolve(context, picture);
        var transform = resolved.Transform;
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        var geometry = GeometryEngine.Evaluate(resolved.Geometry, width, height);
        var stroke = Paints.Stroke(resolved.Outline, resolved.Colors);
        var shadow = Paints.Shadow(resolved.Shadow, resolved.Colors);
        items.Add(new TransformBeginItem(ShapeMatrix(transform)));
        var path = geometry.Paths.FirstOrDefault()?.Path ?? VectorPath.Rectangle(0, 0, width, height);
        if (picture.Picture.Image is { IsDrawable: true })
        {
            items.Add(new VectorPathItem(path, Paints.Fill(picture.Picture, resolved.Colors, width, height), stroke, shadow));
        }
        else
        {
            AddPlaceholderBox(items, width, height, picture.Picture.Image is null ? "Verknüpftes Bild" : "Bild");
            if (stroke is not null)
            {
                items.Add(new VectorPathItem(path, null, stroke));
            }
        }

        items.Add(TransformEndItem.Instance);
    }

    private static void RenderFrame(List<DisplayItem> items, SlideContext context, GraphicFrame frame, ITextMeasurer measurer, RenderOptions options)
    {
        var transform = ShapeResolver.Resolve(context, frame).Transform;
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        items.Add(new TransformBeginItem(ShapeMatrix(transform)));
        if (frame.Kind == GraphicKind.Table && frame.Table is { } table)
        {
            TableRenderer.Render(items, context, table, measurer, width);
        }
        else if (frame.Kind == GraphicKind.Chart && options.Charts?.Render(frame, width, height, context) is { } chart)
        {
            items.Add(new ClipBeginItem(0, 0, width, height));
            items.AddRange(chart);
            items.Add(ClipEndItem.Instance);
        }
        else if (frame.Fallback is { IsDrawable: true } fallback)
        {
            items.Add(new ImageItem(0, 0, width, height, fallback.Key, fallback.Bytes));
        }
        else
        {
            AddPlaceholderBox(items, width, height, frame.KindLabel);
        }

        items.Add(TransformEndItem.Instance);
    }

    /// <summary>A light box with a frame and a cross: what ThixShow shows for content it keeps but does not draw.</summary>
    internal static void AddPlaceholderBox(List<DisplayItem> items, double width, double height, string label)
    {
        var line = new RgbColor(0xBF, 0xBF, 0xBF);
        items.Add(new RectangleItem(0, 0, width, height, new RgbColor(0xF2, 0xF2, 0xF2), line, 0.75));
        items.Add(new LineItem(0, 0, width, height, line, 0.5));
        items.Add(new LineItem(0, height, width, 0, line, 0.5));
        items.Add(new GlyphRunItem(6, 16, label, new FontSpec("Arial", 11), new FontFace("Arial", false, false), new RgbColor(0x59, 0x59, 0x59), CharacterPositions(label, 11), [], []));
    }

    private static double[] CharacterPositions(string text, double size) =>
        Enumerable.Range(0, text.Length).Select(index => index * size * 0.55).ToArray();
}
