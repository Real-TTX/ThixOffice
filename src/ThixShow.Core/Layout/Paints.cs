using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>
/// Turns DrawingML fills, outlines and shadows into display-list paints and strokes for a box of a given size (points): linear
/// gradients along their angle across the box, path gradients as radial paints from their focus rectangle, pictures stretched,
/// cropped or tiled, patterns as the mix of their two colours; the lighten/darken path modes of 3-D looking presets; dash
/// patterns in multiples of the width; arrowheads.
/// </summary>
public static class Paints
{
    /// <summary>The width of a line without one (0.75 pt, PowerPoint's default).</summary>
    public const double DefaultLineWidth = 0.75;

    public static Paint? Fill(Fill? fill, ColorContext colors, double width, double height, PathFillMode mode = PathFillMode.Normal)
    {
        if (mode == PathFillMode.None)
        {
            return null;
        }

        var paint = fill switch
        {
            SolidFill solid => new SolidPaint(ColorResolver.Resolve(solid.Color, colors)),
            GradientFill gradient when gradient.Stops.Count > 0 => Gradient(gradient, colors, width, height),
            PictureFill picture => Picture(picture, width, height),
            PatternFill pattern => Pattern(pattern, colors),
            _ => (Paint?)null,
        };
        return mode == PathFillMode.Normal || paint is null ? paint : Modify(paint, mode);
    }

    private static Paint Gradient(GradientFill gradient, ColorContext colors, double width, double height)
    {
        var stops = gradient.Stops
            .OrderBy(stop => stop.Position)
            .Select(stop => new GradientColorStop(Math.Clamp(stop.Position / 100000.0, 0, 1), ColorResolver.Resolve(stop.Color, colors)))
            .ToList();
        if (gradient.PathKind is { } kind)
        {
            var focus = gradient.FillToRect ?? new RelativeRect(50000, 50000, 50000, 50000);
            var centerX = width * ((focus.Left / 100000.0) + (1 - (focus.Right / 100000.0))) / 2;
            var centerY = height * ((focus.Top / 100000.0) + (1 - (focus.Bottom / 100000.0))) / 2;
            if (kind == GradientPathKind.Circle)
            {
                var radius = new[] { (0.0, 0.0), (width, 0.0), (0.0, height), (width, height) }
                    .Max(corner => Math.Sqrt(Math.Pow(corner.Item1 - centerX, 2) + Math.Pow(corner.Item2 - centerY, 2)));
                return new RadialGradientPaint(centerX, centerY, Math.Max(radius, 0.01), Math.Max(radius, 0.01), stops);
            }

            var radiusX = Math.Max(centerX, width - centerX) * Math.Sqrt(2);
            var radiusY = Math.Max(centerY, height - centerY) * Math.Sqrt(2);
            return new RadialGradientPaint(centerX, centerY, Math.Max(radiusX, 0.01), Math.Max(radiusY, 0.01), stops);
        }

        var degrees = (gradient.LinearAngle ?? 0) / 60000.0;
        var radians = degrees * Math.PI / 180;
        if (gradient.LinearScaled == true && width > 0 && height > 0)
        {
            radians = Math.Atan2(Math.Sin(radians) * height / width, Math.Cos(radians));
        }

        var (dx, dy) = (Math.Cos(radians), Math.Sin(radians));
        var half = (Math.Abs(width / 2 * dx)) + (Math.Abs(height / 2 * dy));
        var (cx, cy) = (width / 2, height / 2);
        return new LinearGradientPaint(cx - (dx * half), cy - (dy * half), cx + (dx * half), cy + (dy * half), stops);
    }

    private static Paint Picture(PictureFill picture, double width, double height)
    {
        if (picture.Image is not { IsDrawable: true } image)
        {
            return new SolidPaint(new RgbColor(0xD9, 0xD9, 0xD9));
        }

        var crop = picture.Crop;
        if (picture.Mode is TileMode tile)
        {
            var tileWidth = Math.Max(1, image.PixelWidth * 0.75 * tile.ScaleX / 100000.0);
            var tileHeight = Math.Max(1, image.PixelHeight * 0.75 * tile.ScaleY / 100000.0);
            return new ImagePaint(image.Key, image.Bytes, Units.EmuToPoints(tile.OffsetX), Units.EmuToPoints(tile.OffsetY), tileWidth, tileHeight, picture.Opacity, Tile: true);
        }

        var fillRect = (picture.Mode as StretchMode)?.FillRect ?? RelativeRect.Zero;
        var x = width * fillRect.Left / 100000.0;
        var y = height * fillRect.Top / 100000.0;
        var w = width * (1 - (fillRect.Left / 100000.0) - (fillRect.Right / 100000.0));
        var h = height * (1 - (fillRect.Top / 100000.0) - (fillRect.Bottom / 100000.0));
        return new ImagePaint(image.Key, image.Bytes, x, y, w, h, picture.Opacity, crop.Left / 100000.0, crop.Top / 100000.0, crop.Right / 100000.0, crop.Bottom / 100000.0);
    }

    private static Paint Pattern(PatternFill pattern, ColorContext colors)
    {
        var foreground = pattern.Foreground is null ? RgbColor.Black : ColorResolver.Resolve(pattern.Foreground, colors);
        var background = pattern.Background is null ? RgbColor.White : ColorResolver.Resolve(pattern.Background, colors);
        var density = pattern.Preset.StartsWith("pct", StringComparison.Ordinal) && int.TryParse(pattern.Preset[3..], out var percent) ? percent / 100.0 : 0.5;
        return new SolidPaint(Blend(foreground, background, density));
    }

    public static RgbColor Blend(RgbColor first, RgbColor second, double amountOfFirst) => new(
        (byte)Math.Round((first.R * amountOfFirst) + (second.R * (1 - amountOfFirst))),
        (byte)Math.Round((first.G * amountOfFirst) + (second.G * (1 - amountOfFirst))),
        (byte)Math.Round((first.B * amountOfFirst) + (second.B * (1 - amountOfFirst))),
        (byte)Math.Round((first.A * amountOfFirst) + (second.A * (1 - amountOfFirst))));

    private static Paint Modify(Paint paint, PathFillMode mode)
    {
        RgbColor Change(RgbColor color) => mode switch
        {
            PathFillMode.Darken => Blend(color, RgbColor.Black with { A = color.A }, 0.6),
            PathFillMode.DarkenLess => Blend(color, RgbColor.Black with { A = color.A }, 0.8),
            PathFillMode.Lighten => Blend(color, RgbColor.White with { A = color.A }, 0.6),
            PathFillMode.LightenLess => Blend(color, RgbColor.White with { A = color.A }, 0.8),
            _ => color,
        };

        return paint switch
        {
            SolidPaint solid => new SolidPaint(Change(solid.Color)),
            LinearGradientPaint linear => linear with { Stops = linear.Stops.Select(stop => stop with { Color = Change(stop.Color) }).ToList() },
            RadialGradientPaint radial => radial with { Stops = radial.Stops.Select(stop => stop with { Color = Change(stop.Color) }).ToList() },
            _ => paint,
        };
    }

    /// <summary>The stroke of an outline, or null when it draws nothing.</summary>
    public static StrokeStyle? Stroke(Outline? outline, ColorContext colors)
    {
        if (outline?.Fill is null or NoFill)
        {
            return null;
        }

        var color = ColorResolver.ResolveFill(outline.Fill, colors) ?? RgbColor.Black;
        var width = outline.Width is { } emu ? Math.Max(0.25, Units.EmuToPoints(emu)) : DefaultLineWidth;
        var cap = outline.Cap switch
        {
            LineCap.Round => StrokeCap.Round,
            LineCap.Square => StrokeCap.Square,
            _ => StrokeCap.Butt,
        };
        var join = outline.Join switch
        {
            LineJoin.Bevel => StrokeJoin.Bevel,
            LineJoin.Miter => StrokeJoin.Miter,
            _ => StrokeJoin.Round,
        };
        double[]? dashes = null;
        if (outline.Dash is { IsSolid: false } dash)
        {
            var pattern = dash.Pattern(outline.Cap ?? LineCap.Flat);
            if (pattern.Length > 0)
            {
                dashes = pattern.Select(value => Math.Max(0.01, value * width)).ToArray();
            }
        }

        return new StrokeStyle(new SolidPaint(color), width, dashes, cap, join, (outline.MiterLimit ?? 800000) / 100000.0);
    }

    /// <summary>A soft shadow from a:outerShdw (offset by distance and direction, blur radius halved as a sigma-like softness).</summary>
    public static DropShadow? Shadow(OuterShadow? shadow, ColorContext colors)
    {
        if (shadow is null)
        {
            return null;
        }

        var distance = Units.EmuToPoints(shadow.Distance);
        var radians = shadow.Direction / 60000.0 * Math.PI / 180;
        return new DropShadow(distance * Math.Cos(radians), distance * Math.Sin(radians), Units.EmuToPoints(shadow.BlurRadius) / 2, ColorResolver.Resolve(shadow.Color, colors));
    }

    /// <summary>
    /// The arrowheads of an open path: <paramref name="head"/> at its start, <paramref name="tail"/> at its end (filled shapes in
    /// the stroke's colour; the open "arrow" as strokes). Sizes: small/medium/large = 2/3/5 × the line width.
    /// </summary>
    public static IEnumerable<VectorPathItem> Arrowheads(VectorPath path, LineEnd? head, LineEnd? tail, StrokeStyle stroke)
    {
        if (path.IsEmpty || path.Verbs.Contains(PathVerb.Close))
        {
            yield break;
        }

        var points = path.Points;
        if (points.Count < 4)
        {
            yield break;
        }

        if (head is { Kind: not LineEndKind.None })
        {
            var (x, y) = (points[0], points[1]);
            var (px, py) = (points[2], points[3]);
            foreach (var item in Arrowhead(head, x, y, x - px, y - py, stroke))
            {
                yield return item;
            }
        }

        if (tail is { Kind: not LineEndKind.None })
        {
            var (x, y) = (points[^2], points[^1]);
            var (px, py) = (points[^4], points[^3]);
            foreach (var item in Arrowhead(tail, x, y, x - px, y - py, stroke))
            {
                yield return item;
            }
        }
    }

    private static IEnumerable<VectorPathItem> Arrowhead(LineEnd end, double x, double y, double directionX, double directionY, StrokeStyle stroke)
    {
        var lengthOfDirection = Math.Sqrt((directionX * directionX) + (directionY * directionY));
        if (lengthOfDirection < 1e-9)
        {
            yield break;
        }

        var (ux, uy) = (directionX / lengthOfDirection, directionY / lengthOfDirection);
        var (nx, ny) = (-uy, ux);
        static double Factor(LineEndSize size) => size switch
        {
            LineEndSize.Small => 2,
            LineEndSize.Large => 5,
            _ => 3,
        };

        var lineWidth = Math.Max(stroke.Width, 1);
        var length = Factor(end.Length) * lineWidth;
        var halfWidth = Factor(end.Width) * lineWidth / 2;
        var paint = stroke.Paint;
        (double X, double Y) At(double along, double across) => (x - (ux * along) + (nx * across), y - (uy * along) + (ny * across));

        var builder = new VectorPathBuilder();
        switch (end.Kind)
        {
            case LineEndKind.Triangle:
                {
                    var (bx1, by1) = At(length, halfWidth);
                    var (bx2, by2) = At(length, -halfWidth);
                    builder.MoveTo(x, y).LineTo(bx1, by1).LineTo(bx2, by2).Close();
                    yield return new VectorPathItem(builder.Build(), paint);
                    break;
                }

            case LineEndKind.Stealth:
                {
                    var (bx1, by1) = At(length, halfWidth);
                    var (mx, my) = At(length * 0.5, 0);
                    var (bx2, by2) = At(length, -halfWidth);
                    builder.MoveTo(x, y).LineTo(bx1, by1).LineTo(mx, my).LineTo(bx2, by2).Close();
                    yield return new VectorPathItem(builder.Build(), paint);
                    break;
                }

            case LineEndKind.Diamond:
                {
                    var (fx, fy) = At(-length / 2, 0);
                    var (sx1, sy1) = At(0, halfWidth);
                    var (bx, by) = At(length / 2, 0);
                    var (sx2, sy2) = At(0, -halfWidth);
                    builder.MoveTo(fx, fy).LineTo(sx1, sy1).LineTo(bx, by).LineTo(sx2, sy2).Close();
                    yield return new VectorPathItem(builder.Build(), paint);
                    break;
                }

            case LineEndKind.Oval:
                {
                    var ellipse = VectorPath.Ellipse(-length / 2, -halfWidth, length, halfWidth * 2);
                    var angle = Math.Atan2(uy, ux) * 180 / Math.PI;
                    yield return new VectorPathItem(ellipse.Transform(Matrix2D.Rotation(angle) * Matrix2D.Translation(x, y)), paint);
                    break;
                }

            case LineEndKind.Arrow:
                {
                    var (bx1, by1) = At(length, halfWidth);
                    var (bx2, by2) = At(length, -halfWidth);
                    builder.MoveTo(bx1, by1).LineTo(x, y).LineTo(bx2, by2);
                    yield return new VectorPathItem(builder.Build(), null, stroke with { Dashes = null, Join = StrokeJoin.Miter });
                    break;
                }
        }
    }
}
