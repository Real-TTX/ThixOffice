namespace ThixOffice.Text.Drawing;

// Vector drawing for display lists: paths with paints (solid, linear/radial gradients, pictures), strokes with dashes, caps and
// joins, soft shadows, nested transforms, path clips and opacity groups. Additive to DisplayList.cs: renderers that do not know
// these items skip them; ThixOffice.Text.Skia's SkiaVectorRenderer draws every item (these and the basic ones).

/// <summary>The commands of a <see cref="VectorPath"/>.</summary>
public enum PathVerb : byte
{
    /// <summary>Starts a figure at one point.</summary>
    MoveTo,

    /// <summary>A straight line to one point.</summary>
    LineTo,

    /// <summary>A quadratic Bézier: control point, end point.</summary>
    QuadTo,

    /// <summary>A cubic Bézier: two control points, end point.</summary>
    CubicTo,

    /// <summary>Closes the figure (no points).</summary>
    Close,
}

/// <summary>How overlapping figures of a path fill.</summary>
public enum PathFillRule : byte
{
    NonZero,
    EvenOdd,
}

/// <summary>
/// An immutable path in points: verbs and their points (x, y pairs; MoveTo/LineTo one point, QuadTo two, CubicTo three). Arcs
/// are given as cubic Béziers (see <see cref="VectorPathBuilder.ArcTo"/>), so every renderer – Skia, a browser canvas, PDF – can
/// draw it with the four primitive commands.
/// </summary>
public sealed class VectorPath : IEquatable<VectorPath>
{
    private readonly PathVerb[] _verbs;
    private readonly double[] _points;

    public VectorPath(PathVerb[] verbs, double[] points)
    {
        _verbs = verbs;
        _points = points;
    }

    public static VectorPath Empty { get; } = new([], []);

    public IReadOnlyList<PathVerb> Verbs => _verbs;

    /// <summary>The points as x, y pairs in verb order.</summary>
    public IReadOnlyList<double> Points => _points;

    public bool IsEmpty => _verbs.Length == 0;

    /// <summary>A rectangle as a closed path.</summary>
    public static VectorPath Rectangle(double x, double y, double width, double height) =>
        new VectorPathBuilder().MoveTo(x, y).LineTo(x + width, y).LineTo(x + width, y + height).LineTo(x, y + height).Close().Build();

    /// <summary>An ellipse in the rectangle as a closed path (four Béziers).</summary>
    public static VectorPath Ellipse(double x, double y, double width, double height) =>
        new VectorPathBuilder().AddEllipse(x, y, width, height).Build();

    /// <summary>The path with every point mapped through <paramref name="matrix"/>.</summary>
    public VectorPath Transform(Matrix2D matrix)
    {
        var points = new double[_points.Length];
        for (var index = 0; index + 1 < _points.Length; index += 2)
        {
            var (x, y) = matrix.Apply(_points[index], _points[index + 1]);
            points[index] = x;
            points[index + 1] = y;
        }

        return new VectorPath(_verbs, points);
    }

    /// <summary>The bounding box of the points (control points included): left, top, right, bottom.</summary>
    public (double Left, double Top, double Right, double Bottom) Bounds()
    {
        if (_points.Length < 2)
        {
            return (0, 0, 0, 0);
        }

        double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
        for (var index = 0; index + 1 < _points.Length; index += 2)
        {
            left = Math.Min(left, _points[index]);
            right = Math.Max(right, _points[index]);
            top = Math.Min(top, _points[index + 1]);
            bottom = Math.Max(bottom, _points[index + 1]);
        }

        return (left, top, right, bottom);
    }

    /// <summary>Calls the visitor per command with its points (absolute).</summary>
    public void Visit(Action<PathVerb, ReadOnlySpan<double>> visitor)
    {
        var position = 0;
        foreach (var verb in _verbs)
        {
            var count = PointCount(verb) * 2;
            visitor(verb, _points.AsSpan(position, count));
            position += count;
        }
    }

    public static int PointCount(PathVerb verb) => verb switch
    {
        PathVerb.MoveTo or PathVerb.LineTo => 1,
        PathVerb.QuadTo => 2,
        PathVerb.CubicTo => 3,
        _ => 0,
    };

    public bool Equals(VectorPath? other) =>
        other is not null && _verbs.AsSpan().SequenceEqual(other._verbs) && _points.AsSpan().SequenceEqual(other._points);

    public override bool Equals(object? obj) => obj is VectorPath other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_verbs.Length, _points.Length, _points.Length > 0 ? _points[0] : 0);

    /// <summary>"M 0 0 L 10 0 C … Z" with two decimals – for tests and debugging.</summary>
    public override string ToString()
    {
        var parts = new List<string>();
        Visit((verb, points) =>
        {
            var letter = verb switch
            {
                PathVerb.MoveTo => "M",
                PathVerb.LineTo => "L",
                PathVerb.QuadTo => "Q",
                PathVerb.CubicTo => "C",
                _ => "Z",
            };
            var coordinates = new List<string> { letter };
            foreach (var value in points)
            {
                coordinates.Add(value.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture));
            }

            parts.Add(string.Join(" ", coordinates));
        });
        return string.Join(" ", parts);
    }
}

/// <summary>Builds a <see cref="VectorPath"/>; tracks the current point for arcs.</summary>
public sealed class VectorPathBuilder
{
    private readonly List<PathVerb> _verbs = [];
    private readonly List<double> _points = [];
    private double _startX;
    private double _startY;

    public double CurrentX { get; private set; }

    public double CurrentY { get; private set; }

    public bool IsEmpty => _verbs.Count == 0;

    public VectorPathBuilder MoveTo(double x, double y)
    {
        _verbs.Add(PathVerb.MoveTo);
        _points.Add(x);
        _points.Add(y);
        (CurrentX, CurrentY, _startX, _startY) = (x, y, x, y);
        return this;
    }

    public VectorPathBuilder LineTo(double x, double y)
    {
        EnsureStarted();
        _verbs.Add(PathVerb.LineTo);
        _points.Add(x);
        _points.Add(y);
        (CurrentX, CurrentY) = (x, y);
        return this;
    }

    public VectorPathBuilder QuadTo(double x1, double y1, double x, double y)
    {
        EnsureStarted();
        _verbs.Add(PathVerb.QuadTo);
        _points.AddRange([x1, y1, x, y]);
        (CurrentX, CurrentY) = (x, y);
        return this;
    }

    public VectorPathBuilder CubicTo(double x1, double y1, double x2, double y2, double x, double y)
    {
        EnsureStarted();
        _verbs.Add(PathVerb.CubicTo);
        _points.AddRange([x1, y1, x2, y2, x, y]);
        (CurrentX, CurrentY) = (x, y);
        return this;
    }

    public VectorPathBuilder Close()
    {
        if (_verbs.Count > 0 && _verbs[^1] != PathVerb.Close)
        {
            _verbs.Add(PathVerb.Close);
            (CurrentX, CurrentY) = (_startX, _startY);
        }

        return this;
    }

    /// <summary>
    /// An elliptical arc of the ellipse with centre (<paramref name="centerX"/>, <paramref name="centerY"/>) and radii, from
    /// parametric angle <paramref name="startRadians"/> sweeping <paramref name="sweepRadians"/> (positive = clockwise on screen,
    /// y down), as cubic Béziers of at most 90° each. Draws a line to the arc's start when the current point is elsewhere.
    /// </summary>
    public VectorPathBuilder ArcTo(double centerX, double centerY, double radiusX, double radiusY, double startRadians, double sweepRadians)
    {
        var startX = centerX + (radiusX * Math.Cos(startRadians));
        var startY = centerY + (radiusY * Math.Sin(startRadians));
        if (_verbs.Count == 0 || _verbs[^1] == PathVerb.Close)
        {
            MoveTo(startX, startY);
        }
        else if (Math.Abs(startX - CurrentX) > 1e-9 || Math.Abs(startY - CurrentY) > 1e-9)
        {
            LineTo(startX, startY);
        }

        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweepRadians) / (Math.PI / 2) - 1e-9));
        var step = sweepRadians / segments;
        var kappa = 4.0 / 3.0 * Math.Tan(step / 4);
        var angle = startRadians;
        for (var segment = 0; segment < segments; segment++)
        {
            var (cos1, sin1) = (Math.Cos(angle), Math.Sin(angle));
            var (cos2, sin2) = (Math.Cos(angle + step), Math.Sin(angle + step));
            CubicTo(
                centerX + (radiusX * (cos1 - (kappa * sin1))),
                centerY + (radiusY * (sin1 + (kappa * cos1))),
                centerX + (radiusX * (cos2 + (kappa * sin2))),
                centerY + (radiusY * (sin2 - (kappa * cos2))),
                centerX + (radiusX * cos2),
                centerY + (radiusY * sin2));
            angle += step;
        }

        return this;
    }

    /// <summary>Adds a closed ellipse figure inscribed in the rectangle.</summary>
    public VectorPathBuilder AddEllipse(double x, double y, double width, double height)
    {
        var (centerX, centerY) = (x + (width / 2), y + (height / 2));
        MoveTo(x + width, centerY);
        ArcTo(centerX, centerY, width / 2, height / 2, 0, Math.PI * 2);
        return Close();
    }

    public VectorPathBuilder AddRectangle(double x, double y, double width, double height) =>
        MoveTo(x, y).LineTo(x + width, y).LineTo(x + width, y + height).LineTo(x, y + height).Close();

    /// <summary>Appends all figures of <paramref name="path"/>.</summary>
    public VectorPathBuilder Append(VectorPath path)
    {
        path.Visit((verb, points) =>
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    MoveTo(points[0], points[1]);
                    break;
                case PathVerb.LineTo:
                    LineTo(points[0], points[1]);
                    break;
                case PathVerb.QuadTo:
                    QuadTo(points[0], points[1], points[2], points[3]);
                    break;
                case PathVerb.CubicTo:
                    CubicTo(points[0], points[1], points[2], points[3], points[4], points[5]);
                    break;
                default:
                    Close();
                    break;
            }
        });
        return this;
    }

    public VectorPath Build() => new([.. _verbs], [.. _points]);

    private void EnsureStarted()
    {
        if (_verbs.Count == 0 || _verbs[^1] == PathVerb.Close)
        {
            MoveTo(CurrentX, CurrentY);
        }
    }
}

/// <summary>An affine transform: x' = M11·x + M21·y + OffsetX, y' = M12·x + M22·y + OffsetY.</summary>
public readonly record struct Matrix2D(double M11, double M12, double M21, double M22, double OffsetX, double OffsetY)
{
    public static Matrix2D Identity { get; } = new(1, 0, 0, 1, 0, 0);

    public bool IsIdentity => this == Identity;

    public static Matrix2D Translation(double x, double y) => new(1, 0, 0, 1, x, y);

    public static Matrix2D Scaling(double x, double y) => new(x, 0, 0, y, 0, 0);

    /// <summary>Scaling about the point (<paramref name="centerX"/>, <paramref name="centerY"/>).</summary>
    public static Matrix2D Scaling(double x, double y, double centerX, double centerY) =>
        Translation(-centerX, -centerY) * Scaling(x, y) * Translation(centerX, centerY);

    /// <summary>A clockwise rotation (y down) by <paramref name="degrees"/> about the point.</summary>
    public static Matrix2D Rotation(double degrees, double centerX = 0, double centerY = 0)
    {
        var radians = degrees * Math.PI / 180;
        var (cos, sin) = (Math.Cos(radians), Math.Sin(radians));
        var rotation = new Matrix2D(cos, sin, -sin, cos, 0, 0);
        return Translation(-centerX, -centerY) * rotation * Translation(centerX, centerY);
    }

    /// <summary>First <paramref name="first"/>, then <paramref name="second"/>.</summary>
    public static Matrix2D operator *(Matrix2D first, Matrix2D second) => new(
        (first.M11 * second.M11) + (first.M12 * second.M21),
        (first.M11 * second.M12) + (first.M12 * second.M22),
        (first.M21 * second.M11) + (first.M22 * second.M21),
        (first.M21 * second.M12) + (first.M22 * second.M22),
        (first.OffsetX * second.M11) + (first.OffsetY * second.M21) + second.OffsetX,
        (first.OffsetX * second.M12) + (first.OffsetY * second.M22) + second.OffsetY);

    public (double X, double Y) Apply(double x, double y) =>
        ((M11 * x) + (M21 * y) + OffsetX, (M12 * x) + (M22 * y) + OffsetY);

    public double Determinant => (M11 * M22) - (M12 * M21);

    /// <summary>The inverse (identity when the matrix cannot be inverted).</summary>
    public Matrix2D Invert()
    {
        var determinant = Determinant;
        if (Math.Abs(determinant) < 1e-15)
        {
            return Identity;
        }

        var inverse = 1 / determinant;
        return new Matrix2D(
            M22 * inverse,
            -M12 * inverse,
            -M21 * inverse,
            M11 * inverse,
            ((M21 * OffsetY) - (M22 * OffsetX)) * inverse,
            ((M12 * OffsetX) - (M11 * OffsetY)) * inverse);
    }
}

/// <summary>How a path or stroke is painted.</summary>
public abstract record Paint;

/// <summary>One colour.</summary>
public sealed record SolidPaint(RgbColor Color) : Paint;

/// <summary>A colour at an offset (0–1) of a gradient.</summary>
public readonly record struct GradientColorStop(double Offset, RgbColor Color);

/// <summary>A linear gradient from (X1, Y1) to (X2, Y2) in the coordinates the path is drawn in.</summary>
public sealed record LinearGradientPaint(double X1, double Y1, double X2, double Y2, IReadOnlyList<GradientColorStop> Stops) : Paint;

/// <summary>A radial gradient: offset 0 at the centre, 1 on the ellipse with the radii.</summary>
public sealed record RadialGradientPaint(double CenterX, double CenterY, double RadiusX, double RadiusY, IReadOnlyList<GradientColorStop> Stops) : Paint;

/// <summary>
/// A picture (encoded bytes, named by <see cref="Key"/>) drawn into the rectangle (X, Y, Width, Height) and clipped by the path;
/// <see cref="SourceLeft"/> … the part of the picture used (fractions; negative extends with transparency). <see cref="Tile"/>
/// repeats it from the rectangle's origin at the rectangle's size.
/// </summary>
public sealed record ImagePaint(
    string Key,
    byte[] Data,
    double X,
    double Y,
    double Width,
    double Height,
    double Opacity = 1,
    double SourceLeft = 0,
    double SourceTop = 0,
    double SourceRight = 0,
    double SourceBottom = 0,
    bool Tile = false) : Paint;

/// <summary>Stroke caps.</summary>
public enum StrokeCap : byte
{
    Butt,
    Round,
    Square,
}

/// <summary>Stroke joins.</summary>
public enum StrokeJoin : byte
{
    Miter,
    Round,
    Bevel,
}

/// <summary>A stroke: paint, width (points), dash pattern in points (on, off, …; null = solid), cap, join.</summary>
public sealed record StrokeStyle(
    Paint Paint,
    double Width,
    IReadOnlyList<double>? Dashes = null,
    StrokeCap Cap = StrokeCap.Butt,
    StrokeJoin Join = StrokeJoin.Round,
    double MiterLimit = 8);

/// <summary>A soft shadow under a path: offset (points), blur radius (points), colour with alpha.</summary>
public sealed record DropShadow(double OffsetX, double OffsetY, double Blur, RgbColor Color);

/// <summary>A path filled and/or stroked; the shadow (when there is one) is drawn first, under both.</summary>
public sealed record VectorPathItem(
    VectorPath Path,
    Paint? Fill,
    StrokeStyle? Stroke = null,
    DropShadow? Shadow = null,
    PathFillRule FillRule = PathFillRule.NonZero) : DisplayItem;

/// <summary>
/// Starts a transform: items until the matching <see cref="TransformEndItem"/> are drawn through <see cref="Matrix"/> (applied
/// after the transforms around it). Rotated and flipped shapes, group transforms, the scale of a thumbnail.
/// </summary>
public sealed record TransformBeginItem(Matrix2D Matrix) : DisplayItem;

/// <summary>Ends the innermost <see cref="TransformBeginItem"/>.</summary>
public sealed record TransformEndItem : DisplayItem
{
    public static TransformEndItem Instance { get; } = new();
}

/// <summary>Starts clipping to a path; ended by <see cref="ClipEndItem"/> like <see cref="ClipBeginItem"/>.</summary>
public sealed record ClipPathBeginItem(VectorPath Path) : DisplayItem;

/// <summary>
/// Starts a group drawn with <see cref="Opacity"/> as a whole (overlaps inside do not add up): until the matching
/// <see cref="OpacityEndItem"/>.
/// </summary>
public sealed record OpacityBeginItem(double Opacity) : DisplayItem;

/// <summary>Ends the innermost <see cref="OpacityBeginItem"/>.</summary>
public sealed record OpacityEndItem : DisplayItem
{
    public static OpacityEndItem Instance { get; } = new();
}
