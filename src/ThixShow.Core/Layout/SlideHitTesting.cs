using ThixOffice.Text.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>What a point on a slide hits: the element (inside groups: the path of group ids above it).</summary>
public sealed record ShapeHit(int ShapeId, IReadOnlyList<int> GroupPath)
{
    /// <summary>The id a click selects: the outermost group, unless the group <paramref name="entered"/> is open.</summary>
    public int SelectableId(int? entered)
    {
        if (GroupPath.Count == 0)
        {
            return ShapeId;
        }

        if (entered is { } open)
        {
            var index = GroupPath.ToList().IndexOf(open);
            if (index >= 0)
            {
                return index + 1 < GroupPath.Count ? GroupPath[index + 1] : ShapeId;
            }
        }

        return GroupPath[0];
    }
}

/// <summary>A selection handle of a shape: which one and where (slide points).</summary>
public enum HandleKind
{
    TopLeft,
    Top,
    TopRight,
    Right,
    BottomRight,
    Bottom,
    BottomLeft,
    Left,
    Rotate,
    Adjust,
    LineStart,
    LineEnd,
}

/// <summary>A handle on the slide (points); <see cref="AdjustIndex"/> names the geometry's adjust handle.</summary>
public sealed record SelectionHandle(HandleKind Kind, double X, double Y, int AdjustIndex = -1);

/// <summary>
/// Hit testing on a slide (UI-free, slide points): the topmost element under a point (groups entered or not), each element's
/// transform to the slide and its rotated corners / axis-aligned bounds, and the handles of a selected shape (eight resize handles,
/// the rotation handle above, the geometry's adjust handles, the ends of lines).
/// </summary>
public static class SlideHitTesting
{
    /// <summary>The tolerance (points) around thin lines.</summary>
    public const double LineTolerance = 4;

    /// <summary>The topmost element at (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
    public static ShapeHit? HitTest(Presentation presentation, Slide slide, double x, double y)
    {
        var context = SlideContext.For(presentation, slide);
        return HitTest(context, slide.Shapes, Matrix2D.Identity, x, y, []);
    }

    private static ShapeHit? HitTest(SlideContext context, IReadOnlyList<SlideElement> shapes, Matrix2D parent, double x, double y, IReadOnlyList<int> path)
    {
        for (var index = shapes.Count - 1; index >= 0; index--)
        {
            var shape = shapes[index];
            if (shape.Hidden)
            {
                continue;
            }

            if (shape is GroupShape group)
            {
                var groupMatrix = group.Transform is { } groupTransform ? SlideRenderer.GroupMatrix(groupTransform) * parent : parent;
                if (HitTest(context, group.Children, groupMatrix, x, y, [.. path, group.Id]) is { } inner)
                {
                    return inner;
                }

                continue;
            }

            var transform = ShapeResolver.Resolve(context, shape).Transform;
            var matrix = SlideRenderer.ShapeMatrix(transform) * parent;
            var (localX, localY) = matrix.Invert().Apply(x, y);
            var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
            var scale = Math.Sqrt(Math.Abs(matrix.Determinant));
            var tolerance = LineTolerance / Math.Max(scale, 1e-6);
            var thin = width < tolerance * 2 || height < tolerance * 2 || shape is ConnectorShape;
            if (shape is ConnectorShape || (shape is AutoShape { Geometry.Preset: "line" or "straightConnector1" }))
            {
                if (DistanceToSegment(localX, localY, (0, 0, width, height)) <= tolerance
                    || (!IsStraight(shape) && Inside(localX, localY, width, height, tolerance)))
                {
                    return new ShapeHit(shape.Id, path);
                }

                continue;
            }

            if (Inside(localX, localY, width, height, thin ? tolerance : 0))
            {
                return new ShapeHit(shape.Id, path);
            }
        }

        return null;
    }

    private static bool IsStraight(SlideElement shape) => shape switch
    {
        ConnectorShape connector => connector.Geometry.Preset is "line" or "straightConnector1",
        _ => true,
    };

    private static bool Inside(double x, double y, double width, double height, double tolerance) =>
        x >= -tolerance && y >= -tolerance && x <= width + tolerance && y <= height + tolerance;

    private static double DistanceToSegment(double x, double y, (double X1, double Y1, double X2, double Y2) segment)
    {
        var (dx, dy) = (segment.X2 - segment.X1, segment.Y2 - segment.Y1);
        var lengthSquared = (dx * dx) + (dy * dy);
        var t = lengthSquared < 1e-12 ? 0 : Math.Clamp((((x - segment.X1) * dx) + ((y - segment.Y1) * dy)) / lengthSquared, 0, 1);
        var (px, py) = (segment.X1 + (t * dx), segment.Y1 + (t * dy));
        return Math.Sqrt(((x - px) * (x - px)) + ((y - py) * (y - py)));
    }

    /// <summary>The transform of an element's box (0..w, 0..h points) to the slide, groups above included; null when not found.</summary>
    public static Matrix2D? MatrixOf(Presentation presentation, Slide slide, int shapeId)
    {
        var context = SlideContext.For(presentation, slide);
        return MatrixOf(context, slide.Shapes, shapeId, Matrix2D.Identity);
    }

    private static Matrix2D? MatrixOf(SlideContext context, IEnumerable<SlideElement> shapes, int id, Matrix2D parent)
    {
        foreach (var shape in shapes)
        {
            var transform = ShapeResolver.Resolve(context, shape).Transform;
            if (shape.Id == id)
            {
                return SlideRenderer.ShapeMatrix(transform) * parent;
            }

            if (shape is GroupShape group && group.Transform is { } groupTransform && MatrixOf(context, group.Children, id, SlideRenderer.GroupMatrix(groupTransform) * parent) is { } inner)
            {
                return inner;
            }
        }

        return null;
    }

    /// <summary>The four corners of an element on the slide (top-left, top-right, bottom-right, bottom-left of its box).</summary>
    public static IReadOnlyList<(double X, double Y)> CornersOf(Presentation presentation, Slide slide, int shapeId)
    {
        var shape = slide.FindShape(shapeId);
        var matrix = MatrixOf(presentation, slide, shapeId);
        if (shape is null || matrix is not { } m)
        {
            return [];
        }

        var transform = ShapeResolver.Resolve(SlideContext.For(presentation, slide), shape).Transform;
        var (width, height) = (Units.EmuToPoints(transform.Width), Units.EmuToPoints(transform.Height));
        return [m.Apply(0, 0), m.Apply(width, 0), m.Apply(width, height), m.Apply(0, height)];
    }

    /// <summary>The axis-aligned bounds of an element on the slide (points): left, top, right, bottom.</summary>
    public static (double Left, double Top, double Right, double Bottom) BoundsOf(Presentation presentation, Slide slide, int shapeId)
    {
        var corners = CornersOf(presentation, slide, shapeId);
        if (corners.Count == 0)
        {
            return (0, 0, 0, 0);
        }

        return (corners.Min(point => point.X), corners.Min(point => point.Y), corners.Max(point => point.X), corners.Max(point => point.Y));
    }

    /// <summary>
    /// The handles of a selected element: eight resize handles on its rotated box and the rotation handle 18 pt above its top
    /// edge, or the two ends of a line; plus the geometry's adjust handles.
    /// </summary>
    public static IReadOnlyList<SelectionHandle> HandlesOf(Presentation presentation, Slide slide, int shapeId)
    {
        var shape = slide.FindShape(shapeId);
        var matrix = MatrixOf(presentation, slide, shapeId);
        if (shape is null || matrix is not { } m)
        {
            return [];
        }

        var resolved = ShapeResolver.Resolve(SlideContext.For(presentation, slide), shape);
        var (width, height) = (Units.EmuToPoints(resolved.Transform.Width), Units.EmuToPoints(resolved.Transform.Height));
        var handles = new List<SelectionHandle>();
        SelectionHandle At(HandleKind kind, double x, double y, int adjust = -1)
        {
            var (sx, sy) = m.Apply(x, y);
            return new SelectionHandle(kind, sx, sy, adjust);
        }

        var isLine = shape is ConnectorShape || (shape is AutoShape { Geometry.Preset: "line" or "straightConnector1" });
        if (isLine)
        {
            handles.Add(At(HandleKind.LineStart, 0, 0));
            handles.Add(At(HandleKind.LineEnd, width, height));
        }
        else
        {
            handles.Add(At(HandleKind.TopLeft, 0, 0));
            handles.Add(At(HandleKind.Top, width / 2, 0));
            handles.Add(At(HandleKind.TopRight, width, 0));
            handles.Add(At(HandleKind.Right, width, height / 2));
            handles.Add(At(HandleKind.BottomRight, width, height));
            handles.Add(At(HandleKind.Bottom, width / 2, height));
            handles.Add(At(HandleKind.BottomLeft, 0, height));
            handles.Add(At(HandleKind.Left, 0, height / 2));
            if (shape is not GraphicFrame)
            {
                var scale = Math.Sqrt(Math.Abs(m.Determinant));
                handles.Add(At(HandleKind.Rotate, width / 2, resolved.Transform.FlipVertical ? height + (18 / scale) : -18 / scale));
            }
        }

        if (shape is AutoShape or ConnectorShape)
        {
            var geometry = Drawing.GeometryEngine.Evaluate(resolved.Geometry, width, height);
            foreach (var handle in geometry.Handles.Where(handle => handle.Handle.ReferenceX is not null || handle.Handle.ReferenceY is not null))
            {
                handles.Add(At(HandleKind.Adjust, handle.X, handle.Y, handle.Index));
            }
        }

        return handles;
    }

    /// <summary>The handle within <paramref name="tolerance"/> points of (<paramref name="x"/>, <paramref name="y"/>), adjust handles first.</summary>
    public static SelectionHandle? HandleAt(IReadOnlyList<SelectionHandle> handles, double x, double y, double tolerance = 5) =>
        handles.OrderBy(handle => handle.Kind == HandleKind.Adjust ? 0 : 1)
            .FirstOrDefault(handle => Math.Abs(handle.X - x) <= tolerance && Math.Abs(handle.Y - y) <= tolerance);
}
