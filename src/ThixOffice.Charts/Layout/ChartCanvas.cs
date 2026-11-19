using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixOffice.Charts;

/// <summary>How text is anchored horizontally at its x.</summary>
internal enum TextAlign : byte
{
    Left,
    Center,
    Right,
}

/// <summary>A measured line of text: its shape, width and vertical metrics (target units).</summary>
internal sealed record TextLine(ShapedText Shaped, double Width, double Ascent, double Descent)
{
    public double Height => Ascent + Descent;
}

/// <summary>
/// The drawing surface of the chart layout: emits display items in the target's units (points × <see cref="Scale"/>), measures
/// text, snaps hairlines to device pixels, and builds paths (polylines, polygons, arcs as Béziers, markers).
/// </summary>
internal sealed class ChartCanvas(ChartRenderOptions options)
{
    private readonly Dictionary<(string Text, double Size, bool Bold), TextLine> _measured = [];

    public List<DisplayItem> Items { get; } = [];

    public ChartRenderOptions Options { get; } = options;

    /// <summary>Target units per point.</summary>
    public double Scale => Options.Scale;

    /// <summary>A length in points as target units.</summary>
    public double Pt(double points) => points * Options.Scale;

    public FontSpec Font(double sizePoints, bool bold = false) => new(Options.FontFamily, sizePoints * Options.Scale, bold);

    public TextLine Measure(string text, double sizePoints, bool bold = false)
    {
        var key = (text, sizePoints, bold);
        if (_measured.TryGetValue(key, out var line))
        {
            return line;
        }

        var font = Font(sizePoints, bold);
        var shaped = Options.Measurer.Shape(text, font);
        var metrics = Options.Measurer.GetMetrics(font);
        line = new TextLine(shaped, shaped.Width, metrics.Ascent, metrics.Descent);
        _measured[key] = line;
        return line;
    }

    /// <summary>The line height of a font size (ascent + descent).</summary>
    public double LineHeight(double sizePoints, bool bold = false) => Measure("Ag", sizePoints, bold).Height;

    /// <summary>Draws a line of text with its top at <paramref name="top"/>.</summary>
    public void Text(string text, double x, double top, double sizePoints, RgbColor color, TextAlign align = TextAlign.Left, bool bold = false)
    {
        if (text.Length == 0)
        {
            return;
        }

        var line = Measure(text, sizePoints, bold);
        var left = align switch
        {
            TextAlign.Center => x - line.Width / 2,
            TextAlign.Right => x - line.Width,
            _ => x,
        };
        AddRun(left, top + line.Ascent, line.Shaped, color);
    }

    /// <summary>Text rotated by <paramref name="degrees"/> around (<paramref name="x"/>, <paramref name="y"/>); drawn from there as from the origin.</summary>
    public void RotatedText(string text, double x, double y, double degrees, double sizePoints, RgbColor color, TextAlign align, double verticalOffset = 0, bool bold = false)
    {
        Items.Add(new TransformBeginItem(x, y, degrees));
        Text(text, 0, verticalOffset, sizePoints, color, align, bold);
        Items.Add(TransformEndItem.Instance);
    }

    private void AddRun(double x, double baseline, ShapedText shaped, RgbColor color)
    {
        var characterX = new double[shaped.Text.Length];
        var position = 0.0;
        for (var index = 0; index < shaped.Text.Length; index++)
        {
            characterX[index] = position;
            position += shaped.Advances[index];
        }

        if (shaped.GlyphRuns.Count == 0)
        {
            Items.Add(new GlyphRunItem(x, baseline, shaped.Text, shaped.Font, new FontFace(shaped.Font.Family, shaped.Font.Bold, shaped.Font.Italic), color, characterX, [], []));
            return;
        }

        foreach (var run in shaped.GlyphRuns)
        {
            Items.Add(new GlyphRunItem(x, baseline, shaped.Text, shaped.Font, run.Face, color, characterX, run.Glyphs, run.Positions));
        }
    }

    /// <summary>A hairline-aware width: <paramref name="points"/> but never thinner than one device pixel.</summary>
    public double LineWidth(double points) => Options.PixelSize > 0 ? Math.Max(Options.PixelSize, Pt(points)) : Pt(points);

    /// <summary>
    /// Snaps the coordinate of a line of <paramref name="width"/> to the device pixel grid (a 1-pixel line on a pixel centre),
    /// so gridlines stay sharp on screen.
    /// </summary>
    public double Snap(double value, double width)
    {
        var pixel = Options.PixelSize;
        if (pixel <= 0)
        {
            return value;
        }

        var pixels = Math.Max(1, Math.Round(width / pixel));
        var origin = Math.Round(value / pixel) * pixel;
        return pixels % 2 == 1 ? origin + pixel / 2 : origin;
    }

    public void Rectangle(double x, double y, double width, double height, RgbColor? fill, RgbColor? stroke = null, double strokeWidth = 0)
    {
        if (width <= 0 || height <= 0)
        {
            return;
        }

        Items.Add(new RectangleItem(x, y, width, height, fill, stroke, strokeWidth));
    }

    public void Line(double x1, double y1, double x2, double y2, RgbColor color, double width, LineStyle style = LineStyle.Solid) =>
        Items.Add(new LineItem(x1, y1, x2, y2, color, width, style));

    /// <summary>A horizontal hairline snapped to pixels.</summary>
    public void HorizontalLine(double x1, double x2, double y, RgbColor color, double widthPoints)
    {
        var width = LineWidth(widthPoints);
        var snapped = Snap(y, width);
        Line(x1, snapped, x2, snapped, color, width);
    }

    public void VerticalLine(double x, double y1, double y2, RgbColor color, double widthPoints)
    {
        var width = LineWidth(widthPoints);
        var snapped = Snap(x, width);
        Line(snapped, y1, snapped, y2, color, width);
    }

    public void Path(PathBuilder path, RgbColor? fill, RgbColor? stroke = null, double strokeWidth = 0, LineJoin join = LineJoin.Round)
    {
        if (path.IsEmpty)
        {
            return;
        }

        Items.Add(new PathItem(path.Verbs.ToArray(), path.Points.ToArray(), fill, stroke, strokeWidth, LineStyle.Solid, join));
    }

    public void Clip(double x, double y, double width, double height) => Items.Add(new ClipBeginItem(x, y, width, height));

    public void EndClip() => Items.Add(ClipEndItem.Instance);
}

/// <summary>Collects path verbs and points; arcs become cubic Béziers of at most 90°.</summary>
internal sealed class PathBuilder
{
    public List<PathVerb> Verbs { get; } = [];

    public List<double> Points { get; } = [];

    public bool IsEmpty => Verbs.Count == 0;

    public PathBuilder MoveTo(double x, double y)
    {
        Verbs.Add(PathVerb.Move);
        Points.Add(x);
        Points.Add(y);
        return this;
    }

    public PathBuilder LineTo(double x, double y)
    {
        Verbs.Add(PathVerb.Line);
        Points.Add(x);
        Points.Add(y);
        return this;
    }

    public PathBuilder CubicTo(double x1, double y1, double x2, double y2, double x, double y)
    {
        Verbs.Add(PathVerb.Cubic);
        Points.AddRange([x1, y1, x2, y2, x, y]);
        return this;
    }

    public PathBuilder Close()
    {
        Verbs.Add(PathVerb.Close);
        return this;
    }

    /// <summary>
    /// An arc of the circle around (<paramref name="cx"/>, <paramref name="cy"/>) from <paramref name="start"/> to
    /// <paramref name="end"/> (radians, clockwise on screen from 3 o'clock), continuing the current figure (or starting one).
    /// </summary>
    public PathBuilder Arc(double cx, double cy, double radius, double start, double end, bool startFigure)
    {
        var sweep = end - start;
        var x0 = cx + radius * Math.Cos(start);
        var y0 = cy + radius * Math.Sin(start);
        if (startFigure)
        {
            MoveTo(x0, y0);
        }
        else
        {
            LineTo(x0, y0);
        }

        var segments = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 2) - 1e-9));
        var step = sweep / segments;
        var k = 4.0 / 3 * Math.Tan(step / 4);
        var angle = start;
        for (var index = 0; index < segments; index++)
        {
            var next = angle + step;
            var (cos1, sin1) = (Math.Cos(angle), Math.Sin(angle));
            var (cos2, sin2) = (Math.Cos(next), Math.Sin(next));
            CubicTo(
                cx + radius * (cos1 - k * sin1), cy + radius * (sin1 + k * cos1),
                cx + radius * (cos2 + k * sin2), cy + radius * (sin2 - k * cos2),
                cx + radius * cos2, cy + radius * sin2);
            angle = next;
        }

        return this;
    }

    public PathBuilder Circle(double cx, double cy, double radius)
    {
        Arc(cx, cy, radius, 0, 2 * Math.PI, startFigure: true);
        return Close();
    }

    /// <summary>
    /// A smooth curve through the points (Catmull–Rom as Béziers, tension 1/6 like Office's smoothed lines); the current
    /// figure continues from the first point.
    /// </summary>
    public PathBuilder Smooth(IReadOnlyList<(double X, double Y)> points)
    {
        if (points.Count == 0)
        {
            return this;
        }

        MoveTo(points[0].X, points[0].Y);
        for (var index = 0; index < points.Count - 1; index++)
        {
            var previous = points[Math.Max(0, index - 1)];
            var current = points[index];
            var next = points[index + 1];
            var after = points[Math.Min(points.Count - 1, index + 2)];
            CubicTo(
                current.X + (next.X - previous.X) / 6, current.Y + (next.Y - previous.Y) / 6,
                next.X - (after.X - current.X) / 6, next.Y - (after.Y - current.Y) / 6,
                next.X, next.Y);
        }

        return this;
    }

    public PathBuilder Polyline(IReadOnlyList<(double X, double Y)> points)
    {
        for (var index = 0; index < points.Count; index++)
        {
            if (index == 0)
            {
                MoveTo(points[index].X, points[index].Y);
            }
            else
            {
                LineTo(points[index].X, points[index].Y);
            }
        }

        return this;
    }
}
