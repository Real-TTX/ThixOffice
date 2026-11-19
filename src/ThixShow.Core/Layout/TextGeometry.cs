using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Editing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>A text body laid out where the slide shows it: the layout in its box and the matrix from the box to the slide (points).</summary>
public sealed record PlacedTextLayout(TextBodyLayout Layout, Matrix2D Matrix)
{
    /// <summary>The caret at a position on the slide: its top and bottom point (a rotated shape gives a slanted caret).</summary>
    public ((double X, double Y) Top, (double X, double Y) Bottom) CaretLine(TextPosition position)
    {
        var (x, top, height) = Layout.CaretAt(position.Paragraph, position.Offset);
        return (Matrix.Apply(x, top), Matrix.Apply(x, top + height));
    }

    /// <summary>The text position nearest to a point on the slide.</summary>
    public TextPosition HitTest(double slideX, double slideY)
    {
        var (x, y) = Matrix.Invert().Apply(slideX, slideY);
        var (paragraph, offset) = Layout.HitTest(x, y);
        return new TextPosition(paragraph, offset);
    }

    /// <summary>
    /// The selected stretches between two positions as quadrilaterals on the slide (one per line: top-left, top-right,
    /// bottom-right, bottom-left).
    /// </summary>
    public IReadOnlyList<(double X, double Y)[]> SelectionQuads(TextPosition start, TextPosition end)
    {
        var quads = new List<(double X, double Y)[]>();
        foreach (var line in Layout.Lines)
        {
            var lineStart = new TextPosition(line.Paragraph, line.Start);
            var lineEnd = new TextPosition(line.Paragraph, line.End);
            if (lineEnd < start || lineStart > end || (start == end))
            {
                continue;
            }

            var from = TextPosition.Max(start, lineStart);
            var to = TextPosition.Min(end, lineEnd);
            var left = Layout.CaretAt(from.Paragraph, from.Offset).X;
            var right = Layout.CaretAt(to.Paragraph, to.Offset).X;
            if (to.Paragraph > line.Paragraph || (end > lineEnd && to == lineEnd))
            {
                right += 4; // the paragraph mark of a line that ends inside the selection
            }

            if (right - left < 0.5)
            {
                continue;
            }

            quads.Add([Matrix.Apply(left, line.Top), Matrix.Apply(right, line.Top), Matrix.Apply(right, line.Bottom), Matrix.Apply(left, line.Bottom)]);
        }

        return quads;
    }
}

/// <summary>
/// Where text is on a slide (UI-free): the layout of a shape's or a table cell's text body placed on the slide, for the editor's
/// caret, selection and clicks. Laid out exactly like <see cref="SlideRenderer"/> draws it (geometry text box, autofit, vertical text).
/// </summary>
public static class TextGeometry
{
    /// <summary>The layout of <paramref name="target"/>'s text on <paramref name="slide"/>, or null when it has no text body.</summary>
    public static PlacedTextLayout? Of(Presentation presentation, Slide slide, TextTarget target, ITextMeasurer measurer, DateTime? now = null)
    {
        if (SlideHitTesting.Locate(presentation, slide, target.ShapeId) is not { } placed)
        {
            return null;
        }

        var context = SlideContext.For(presentation, slide);
        if (target.IsTableCell)
        {
            if (placed.Element is not GraphicFrame { Table: { } table })
            {
                return null;
            }

            var geometry = TableRenderer.Measure(context, table, measurer);
            var box = geometry.Cells.FirstOrDefault(cell => cell.Row == target.Row && cell.Column == target.Column);
            return box is null ? null : new PlacedTextLayout(TableRenderer.LayoutCell(context, box.Cell, box.Look, measurer, box.X, box.Y, box.Width, box.Height), placed.Matrix);
        }

        if (placed.Element is not AutoShape shape)
        {
            return null;
        }

        var resolved = ShapeResolver.Resolve(context, shape);
        var text = shape.Text ?? TextBody.Empty;
        var transform = resolved.Transform;
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        var shapeGeometry = GeometryEngine.Evaluate(resolved.Geometry, width, height);
        var options = new TextLayoutOptions(SlideNumber: presentation.IndexOf(slide.Id) + 1, Now: now);
        var layout = SlideRenderer.LayoutTextBody(resolved, text, context, measurer, shapeGeometry, width, height, options, out var textMatrix);
        var parent = SlideRenderer.ShapeMatrix(transform).Invert() * placed.Matrix;
        return new PlacedTextLayout(layout, textMatrix * SlideRenderer.TextMatrix(transform) * parent);
    }

    /// <summary>
    /// The text target a click at (<paramref name="x"/>, <paramref name="y"/>) on shape <paramref name="shapeId"/> edits: the
    /// table cell under the point, the shape's text (shapes that can hold text), or null (pictures, lines, charts).
    /// </summary>
    public static TextTarget? TargetAt(Presentation presentation, Slide slide, int shapeId, double x, double y, ITextMeasurer measurer)
    {
        if (SlideHitTesting.Locate(presentation, slide, shapeId) is not { } placed)
        {
            return null;
        }

        switch (placed.Element)
        {
            case GraphicFrame { Table: { } table }:
                var (localX, localY) = placed.Matrix.Invert().Apply(x, y);
                var cell = TableRenderer.Measure(SlideContext.For(presentation, slide), table, measurer).CellAt(localX, localY);
                return cell is null ? null : new TextTarget(shapeId, cell.Row, cell.Column);
            case AutoShape { Geometry.Preset: "line" or "straightConnector1" }:
                return null;
            case AutoShape:
                return new TextTarget(shapeId);
            default:
                return null;
        }
    }
}
