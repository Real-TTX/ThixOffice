using System.Runtime.CompilerServices;
using ThixOffice.Charts;
using ThixOffice.Charts.Formats;
using ThixOffice.Text;
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
                case ShapeInline { Anchor: { } anchor } shape:
                    yield return (paragraph.StartOf(index), shape, anchor, shape.Width, shape.Height);
                    break;
            }
        }
    }

    /// <summary>
    /// The offsets from its frames that put the floating object anchored at <paramref name="position"/> with its top-left corner at
    /// (<paramref name="x"/>, <paramref name="y"/>) of its page (moving it with the mouse); null when no such object is placed.
    /// </summary>
    public (double OffsetX, double OffsetY)? AnchorOffsetsFor(DocumentPosition position, double x, double y)
    {
        Update();
        foreach (var page in _pages)
        {
            if (page.Floats.FirstOrDefault(placed => placed.Block == position.Block && placed.Offset == position.Offset) is not { } placed)
            {
                continue;
            }

            var origin = FloatPosition(placed.Anchor with { AlignX = null, OffsetX = 0, AlignY = null, OffsetY = 0 }, placed.Width, placed.Height,
                page.Section.Properties, placed.ParagraphTop, placed.AreaLeft, placed.AreaRight - placed.AreaLeft);
            return (x - origin.X, y - origin.Y);
        }

        return null;
    }

    /// <summary>The left of the text area and the top of block <paramref name="block"/> on page <paramref name="page"/> (its first line there).</summary>
    public (double Left, double Top) ParagraphOrigin(int block, int page)
    {
        Update();
        if (_placements[Math.Clamp(block, 0, _placements.Count - 1)] is not { } placement || placement.Lines.Length == 0)
        {
            var setup = _pages[Math.Clamp(page, 0, _pages.Count - 1)].Section.Properties;
            return (setup.MarginLeft + setup.Gutter, setup.MarginTop);
        }

        var index = Math.Max(0, Array.IndexOf(placement.Pages, page));
        return (placement.Lines[index].Left, placement.Lines[index].Top);
    }

    /// <summary>
    /// Where an object in the line is, as offsets from the left of its text area and the top of its paragraph (the place it keeps when it
    /// starts to float); null when it is not laid out.
    /// </summary>
    public (double X, double Y)? InlineOffsetsOf(DocumentPosition position)
    {
        if (GetObjectRect(position) is not { } rect || _placements[position.Block] is not { } placement)
        {
            return null;
        }

        var index = Array.FindIndex(placement.Pages, page => page == rect.Page);
        var line = placement.Lines[Math.Max(0, index)];
        return (rect.X - line.Left, rect.Y - line.Top);
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

    /// <summary>
    /// Objects positioned on the page (or the margin) that reach up beside text placed before their anchor: known per page, they are on
    /// the page from its start the next time it is laid out, so that the text above the anchor flows around them too (as in Word).
    /// </summary>
    private readonly Dictionary<int, List<PlacedFloat>> _earlyFloats = [];

    /// <summary>How often the current pagination started a page again for such an object (bounded).</summary>
    private int _restarts;

    /// <summary>Forgets the early objects of blocks from <paramref name="block"/> on (they are found again when their anchors are placed).</summary>
    private void ForgetEarlyFloats(int block)
    {
        foreach (var list in _earlyFloats.Values)
        {
            list.RemoveAll(placed => placed.Block >= block);
        }
    }

    private sealed partial class Flow
    {
        /// <summary>The block pagination has to go back to (the first of a page whose early objects changed), or null.</summary>
        public int? RestartFrom { get; private set; }

        /// <summary>Puts the anchored objects of a paragraph that starts at <paramref name="top"/> onto the current page.</summary>
        private void PlaceFloats(int block, Paragraph paragraph, double top, double areaLeft, double areaWidth)
        {
            var before = Page.Floats.Count;
            foreach (var (offset, inline, anchor, width, height) in AnchoredIn(paragraph))
            {
                if (Page.Floats.Any(placed => placed.Block == block && placed.Offset == offset))
                {
                    continue; // already on the page from its start (an early object)
                }

                var (x, y) = FloatPosition(anchor, width, height, Setup, top, areaLeft, areaWidth);
                Page.MutableFloats.Add(new PlacedFloat(block, offset, inline, anchor, x, y, width, height) { AreaLeft = areaLeft, AreaRight = areaLeft + areaWidth, ParagraphTop = top });
            }

            if (ReachesBack(before, top))
            {
                RestartFrom = PageStartBlock();
            }
        }

        /// <summary>
        /// True when an object just placed sits on the page or margin above <paramref name="top"/> beside lines placed already: it becomes an
        /// early object of the page (once, and only a bounded number of times per pagination).
        /// </summary>
        private bool ReachesBack(int firstNew, double top)
        {
            var found = false;
            for (var index = firstNew; index < Page.Floats.Count; index++)
            {
                var placed = Page.Floats[index];
                if (placed.Exclusion is not { } area || area.Top >= top - Epsilon || placed.Anchor.VerticalFrame is DrawingFrame.Paragraph or DrawingFrame.Line)
                {
                    continue;
                }

                var beside = Page.Lines.Any(line => line.Top + line.Line.Height > area.Top && line.Top < area.Bottom
                                                     && line.Left < area.Right && line.Left + line.Paragraph.Width > area.Left);
                if (!beside || owner._restarts >= 20)
                {
                    continue;
                }

                if (!owner._earlyFloats.TryGetValue(_page, out var list))
                {
                    list = [];
                    owner._earlyFloats[_page] = list;
                }

                if (list.Any(early => early.Block == placed.Block && early.Offset == placed.Offset))
                {
                    continue;
                }

                list.Add(placed);
                owner._restarts++;
                found = true;
            }

            return found;
        }

        /// <summary>The first block placed on the current page (a table counts from its first block).</summary>
        private int PageStartBlock()
        {
            var first = Page.Lines.Select(line => line.Block).Concat(Page.Cells.Select(cell => cell.TableStart)).DefaultIfEmpty(0).Min();
            while (first > 0 && owner.IsInsideTable(first))
            {
                first--;
            }

            return first;
        }

        /// <summary>Puts the early objects of the current page onto it (a page that is laid out again, or new).</summary>
        public void SeedFloats()
        {
            if (!owner._earlyFloats.TryGetValue(_page, out var list))
            {
                return;
            }

            foreach (var placed in list.Where(early => !Page.Floats.Any(existing => existing.Block == early.Block && existing.Offset == early.Offset)))
            {
                Page.MutableFloats.Add(placed);
            }
        }

        /// <summary>
        /// The paragraph laid out around the floating objects of the page that reach below <see cref="_y"/> (the layout as it is when
        /// none is in its way).
        /// </summary>
        private ParagraphLayout Wrapped(int block, Paragraph paragraph, ParagraphLayout layout, double areaLeft, double width)
        {
            var exclusions = ExclusionsHere(areaLeft, 0, layout.Height + 200);
            return exclusions.Count == 0 ? layout : LayoutAround(block, paragraph, layout, width, exclusions);
        }

        /// <summary>
        /// The floating objects' areas of this page from <see cref="_y"/> down to <paramref name="reach"/> below it, in the coordinates of a
        /// paragraph whose text area starts at <paramref name="areaLeft"/> and that is <paramref name="consumed"/> points into its lines here.
        /// </summary>
        private List<(double Left, double Top, double Right, double Bottom)> ExclusionsHere(double areaLeft, double consumed, double reach)
        {
            var exclusions = new List<(double Left, double Top, double Right, double Bottom)>();
            foreach (var placed in Page.Floats)
            {
                if (placed.Exclusion is { } area && area.Bottom > _y && area.Top < _y + reach)
                {
                    exclusions.Add((area.Left - areaLeft, area.Top - _y + consumed, area.Right - areaLeft, area.Bottom - _y + consumed));
                }
            }

            return exclusions;
        }

        private ParagraphLayout LayoutAround(int block, Paragraph paragraph, ParagraphLayout layout, double width, List<(double Left, double Top, double Right, double Bottom)> exclusions)
        {
            var label = block < owner._labels.Length ? owner._labels[block] : null;
            owner._context.NoteStart = layout.NoteStart;
            var wrapped = ParagraphLayouter.Layout(paragraph, owner._context.Resolver.ResolveParagraph(paragraph), label, width, owner._context, exclusions);
            wrapped.NoteStart = layout.NoteStart;
            owner._layouts[block] = wrapped;
            return wrapped;
        }

        /// <summary>
        /// A paragraph that goes on in the next column or on the next page from line <paramref name="index"/>: the rest is broken again for
        /// the room THERE (the objects of the place it came from no longer narrow it, the ones of the new place do). The lines already
        /// placed stay as they are; when they would come out differently, the layout as it was is kept.
        /// </summary>
        private ParagraphLayout Rewrapped(int block, Paragraph paragraph, ParagraphLayout layout, int index, double areaLeft)
        {
            var consumed = layout.Lines.Take(index).Sum(line => line.Height);
            var kept = (layout.Exclusions ?? []).Where(area => area.Top < consumed).Select(area => (area.Left, area.Top, area.Right, Math.Min(area.Bottom, consumed))).ToList();
            var here = ExclusionsHere(areaLeft, consumed, layout.Height - consumed + 200);
            if (here.Count == 0 && !layout.IsWrapped)
            {
                return layout;
            }

            var exclusions = kept.Concat(here).ToList();
            var label = block < owner._labels.Length ? owner._labels[block] : null;
            owner._context.NoteStart = layout.NoteStart;
            var again = ParagraphLayouter.Layout(paragraph, owner._context.Resolver.ResolveParagraph(paragraph), label, layout.Width, owner._context, exclusions.Count > 0 ? exclusions : null);
            again.NoteStart = layout.NoteStart;
            if (again.Lines.Count <= index || again.Lines[index - 1].End != layout.Lines[index - 1].End)
            {
                return layout;
            }

            owner._layouts[block] = again;
            return again;
        }
    }

    // ---------------------------------------------------------------- drawing

    /// <summary>The display items of an object at (<paramref name="x"/>, <paramref name="y"/>) in a box of <paramref name="width"/> × <paramref name="height"/>.</summary>
    internal IEnumerable<DisplayItem> DrawObject(Inline inline, double x, double y, double width, double height)
    {
        switch (inline)
        {
            case ImageInline image:
                return DrawPicture(image, x, y, width, height);
            case OpaqueInline { Graphic: { } graphic }:
                return DrawGraphic(graphic, x, y, width, height);
            case ShapeInline shape:
                return DrawGraphic(shape.Graphic, x, y, width, height);
            case OpaqueInline { Nested: { } nested }:
                return [TransformBeginItem.At(x, y), .. StoryBox(nested, width).GetDisplayList(0).Items, TransformEndItem.Instance];
            case OpaqueInline { Math: { } math } formula:
                return DrawMath(formula, math, x, y, width, height);
            default:
                return [];
        }
    }

    /// <summary>A picture: its shadow under it, the shown part of it ("Zuschneiden").</summary>
    private List<DisplayItem> DrawPicture(ImageInline image, double x, double y, double width, double height)
    {
        var items = new List<DisplayItem>();
        if (image.Shadow is { } shadow && Shadow(shadow) is { } dropShadow)
        {
            var frame = new VectorPathBuilder().AddRectangle(x, y, width, height).Build();
            items.Add(new VectorPathItem(frame, new SolidPaint(RgbColor.White), null, dropShadow));
        }

        if (image.Washout)
        {
            items.Add(new OpacityBeginItem(0.3)); // "Auswaschen": the picture pale on the paper, like Word's watermark
        }

        items.Add(new ImageItem(x, y, width, height, image.Image.Key, image.Image.Bytes)
        {
            Crop = image.Crop is { } crop ? (crop.Left, crop.Top, crop.Right, crop.Bottom) : null,
        });
        if (image.Washout)
        {
            items.Add(OpacityEndItem.Instance);
        }

        return items;
    }

    /// <summary>A shadow of the model as the vector layer draws it (null when its colour names nothing known).</summary>
    private DropShadow? Shadow(DrawingShadow shadow)
    {
        if (Color(shadow.Color) is not { } color)
        {
            return null;
        }

        var (dx, dy) = shadow.Offset;
        return new DropShadow(dx, dy, shadow.Blur, color);
    }

    private readonly ConditionalWeakTable<MathNode, MathBox> _mathBoxes = new();

    /// <summary>The size of a formula set at <paramref name="size"/> points (kept for drawing it).</summary>
    private (double Width, double Ascent, double Descent) MeasureMath(OpaqueInline formula, double size)
    {
        var box = MathLayouter.Layout(formula.Math!, size, formula.IsDisplayMath, Measurer);
        _mathBoxes.AddOrUpdate(formula.Math!, box);
        return (box.Width, box.Ascent, box.Descent);
    }

    /// <summary>A formula in its box (top left <paramref name="x"/>, <paramref name="y"/>; the baseline <paramref name="height"/> below it), centred when it is a display formula.</summary>
    private List<DisplayItem> DrawMath(OpaqueInline formula, MathNode math, double x, double y, double width, double height)
    {
        if (!_mathBoxes.TryGetValue(math, out var box))
        {
            box = MathLayouter.Layout(math, 11, formula.IsDisplayMath, Measurer);
        }

        var left = x + (formula.IsDisplayMath ? Math.Max(0, (width - box.Width) / 2) : 0);
        var baseline = y + height;
        var items = new List<DisplayItem>();
        foreach (var piece in box.Pieces)
        {
            switch (piece)
            {
                case MathGlyphs glyphs:
                    var (gx, gy) = (left + glyphs.X, baseline + glyphs.Y);
                    var stretched = Math.Abs(glyphs.ScaleY - 1) > 0.001;
                    if (stretched)
                    {
                        items.Add(new TransformBeginItem(Matrix2D.Scaling(1, glyphs.ScaleY, gx, gy)));
                    }

                    DisplayListBuilder.AddText(items, gx, gy, glyphs.Shaped, 0, glyphs.Shaped.Text.Length, glyphs.Font, RgbColor.Black, 0);
                    if (stretched)
                    {
                        items.Add(TransformEndItem.Instance);
                    }

                    break;
                case MathRule rule:
                    items.Add(new LineItem(left + rule.X1, baseline + rule.Y1, left + rule.X2, baseline + rule.Y2, RgbColor.Black, rule.Thickness));
                    break;
                case MathStroke stroke:
                    items.Add(VectorPathItem.Solid(stroke.Path.Transform(Matrix2D.Translation(left, baseline)), null, RgbColor.Black, stroke.Thickness));
                    break;
            }
        }

        return items;
    }

    /// <summary>The size of a nested table laid out at <paramref name="width"/>: the width, and the height down to its last row.</summary>
    private (double Width, double Height) MeasureNested(Story story, double width)
    {
        var page = StoryBox(story, width).Pages[0];
        var height = page.Cells.Select(cell => cell.Y + cell.Height).DefaultIfEmpty(0).Max();
        var lastBlock = story.Blocks.Count - 1; // the empty paragraph a story ends with is not part of the table
        foreach (var line in page.Lines.Where(line => line.Block < lastBlock || page.Cells.Count == 0))
        {
            height = Math.Max(height, line.Top + line.Line.Height);
        }

        return (width, Math.Max(1, height));
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
                case DrawingWordArt wordArt:
                    DrawWordArt(wordArt, items);
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

    /// <summary>Text stretched over its box (a text watermark): shaped once at 100 pt, scaled to the box in both directions.</summary>
    private void DrawWordArt(DrawingWordArt wordArt, List<DisplayItem> items)
    {
        var text = wordArt.Text.Replace('\n', ' ').Trim();
        if (text.Length == 0 || wordArt.Width <= 0 || wordArt.Height <= 0)
        {
            return;
        }

        var font = new FontSpec(wordArt.FontFamily, 100, wordArt.Bold, wordArt.Italic);
        var shaped = Measurer.Shape(text, font);
        var metrics = Measurer.GetMetrics(font);
        var textHeight = metrics.Ascent + metrics.Descent;
        if (shaped.Width <= 0 || textHeight <= 0)
        {
            return;
        }

        var color = Color(wordArt.Color) ?? new RgbColor(0xC0, 0xC0, 0xC0);
        var opacity = Math.Clamp(wordArt.Opacity, 0, 1) < 1;
        if (opacity)
        {
            items.Add(new OpacityBeginItem(Math.Clamp(wordArt.Opacity, 0, 1)));
        }

        items.Add(new TransformBeginItem(Matrix2D.Scaling(wordArt.Width / shaped.Width, wordArt.Height / textHeight) * Matrix2D.Translation(wordArt.X, wordArt.Y)));
        DisplayListBuilder.AddText(items, 0, metrics.Ascent, shaped, 0, text.Length, font, color, 0);
        items.Add(TransformEndItem.Instance);
        if (opacity)
        {
            items.Add(OpacityEndItem.Instance);
        }
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
            var shadow = shape.Shadow is { } shapeShadow ? Shadow(shapeShadow) : null;
            foreach (var path in geometry.Paths)
            {
                var placed = path.Path.Transform(Matrix2D.Translation(shape.X, shape.Y));
                var pathFill = path.Fill == PathFillMode.None ? null : fill;
                var pathStroke = path.Stroke ? stroke : null;
                if (pathFill is not null || pathStroke is not null)
                {
                    items.Add(new VectorPathItem(placed, pathFill, pathStroke, shadow));
                    shadow = null; // one shadow, under the first path
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

    /// <summary>
    /// The link under a point of a page inside a table shown in a table cell (<see cref="OpaqueInline.Nested"/>: a mail's columns), or null – the
    /// text of such a table is read-only and has no caret place of its own, so its links are found through its own layout.
    /// </summary>
    public Hyperlink? NestedLinkAt(int page, double x, double y)
    {
        Update();
        if (page < 0 || page >= _pages.Count)
        {
            return null;
        }

        foreach (var placed in _pages[page].Lines)
        {
            if (y < placed.Top || y > placed.Top + placed.Line.Height)
            {
                continue;
            }

            foreach (var item in placed.Line.Items)
            {
                if (item is not ObjectItem { Inline: OpaqueInline { Nested: { } nested } } box)
                {
                    continue;
                }

                var height = box.Height > 0 ? box.Height : placed.Line.Ascent;
                var left = placed.Left + box.X;
                var top = placed.Top + placed.Line.Baseline - height;
                if (x < left || x > left + box.Width || y < top || y > top + height)
                {
                    continue;
                }

                var inner = StoryBox(nested, box.Width);
                return inner.NestedLinkAt(0, x - left, y - top) ?? inner.LinkAt(0, x - left, y - top);
            }
        }

        return null;
    }

    /// <summary>The link of the text at a point of a page (the character the point is on), or null.</summary>
    public Hyperlink? LinkAt(int page, double x, double y)
    {
        var position = HitTest(page, x, y).Place.Position;
        if (position.Block < 0 || position.Block >= Document.Blocks.Count || Document.Blocks[position.Block] is not Paragraph paragraph)
        {
            return null;
        }

        Inline? before = null;
        var start = 0;
        foreach (var inline in paragraph.Inlines)
        {
            var end = start + inline.Length;
            if (position.Offset >= start && position.Offset < end)
            {
                return inline.Link ?? (position.Offset == start ? before?.Link : null);
            }

            before = inline;
            start = end;
        }

        return before?.Link;
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
