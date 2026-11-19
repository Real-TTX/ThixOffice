using SkiaSharp;
using ThixOffice.Text.Drawing;

namespace ThixOffice.Text.Skia;

// The vector layer of SkiaPageRenderer: paths, paints (solid, gradients, pictures), strokes and shadows.
public static partial class SkiaPageRenderer
{
    /// <summary>The Skia path of a vector path.</summary>
    public static SKPath ToSkia(VectorPath source, PathFillRule rule = PathFillRule.NonZero)
    {
        var path = new SKPath { FillType = rule == PathFillRule.EvenOdd ? SKPathFillType.EvenOdd : SKPathFillType.Winding };
        source.Visit((verb, points) =>
        {
            switch (verb)
            {
                case PathVerb.MoveTo:
                    path.MoveTo((float)points[0], (float)points[1]);
                    break;
                case PathVerb.LineTo:
                    path.LineTo((float)points[0], (float)points[1]);
                    break;
                case PathVerb.QuadTo:
                    path.QuadTo((float)points[0], (float)points[1], (float)points[2], (float)points[3]);
                    break;
                case PathVerb.CubicTo:
                    path.CubicTo((float)points[0], (float)points[1], (float)points[2], (float)points[3], (float)points[4], (float)points[5]);
                    break;
                default:
                    path.Close();
                    break;
            }
        });
        return path;
    }

    public static SKMatrix ToSkia(Matrix2D matrix) => new(
        (float)matrix.M11, (float)matrix.M21, (float)matrix.OffsetX,
        (float)matrix.M12, (float)matrix.M22, (float)matrix.OffsetY,
        0, 0, 1);

    private static void DrawPath(SKCanvas canvas, VectorPathItem item)
    {
        using var path = ToSkia(item.Path, item.FillRule);
        if (item.Shadow is { } shadow && (item.Fill is not null || item.Stroke is not null))
        {
            DrawShadow(canvas, path, item, shadow);
        }

        if (item.Fill is { } fill)
        {
            FillPath(canvas, path, fill);
        }

        if (item.Stroke is { } stroke)
        {
            // A width of 0 is a hairline (one device pixel), like Office's thinnest outline.
            using var paint = StrokePaint(stroke);
            canvas.DrawPath(path, paint);
        }
    }

    private static void DrawShadow(SKCanvas canvas, SKPath path, VectorPathItem item, DropShadow shadow)
    {
        using var shadowPaint = new SKPaint
        {
            Color = ToSkia(shadow.Color),
            IsAntialias = true,
            Style = item.Fill is null ? SKPaintStyle.Stroke : SKPaintStyle.Fill,
            StrokeWidth = (float)(item.Stroke?.Width ?? 0),
            MaskFilter = shadow.Blur > 0 ? SKMaskFilter.CreateBlur(SKBlurStyle.Normal, (float)Math.Max(0.1, shadow.Blur)) : null,
        };
        canvas.Save();
        canvas.Translate((float)shadow.OffsetX, (float)shadow.OffsetY);
        canvas.DrawPath(path, shadowPaint);
        canvas.Restore();
    }

    private static SKPaint StrokePaint(StrokeStyle stroke)
    {
        var paint = new SKPaint
        {
            IsAntialias = true,
            Style = SKPaintStyle.Stroke,
            StrokeWidth = (float)stroke.Width,
            StrokeCap = stroke.Cap switch
            {
                StrokeCap.Round => SKStrokeCap.Round,
                StrokeCap.Square => SKStrokeCap.Square,
                _ => SKStrokeCap.Butt,
            },
            StrokeJoin = stroke.Join switch
            {
                StrokeJoin.Miter => SKStrokeJoin.Miter,
                StrokeJoin.Bevel => SKStrokeJoin.Bevel,
                _ => SKStrokeJoin.Round,
            },
            StrokeMiter = (float)stroke.MiterLimit,
        };
        if (stroke.Dashes is { Count: >= 2 } dashes)
        {
            var intervals = dashes.Select(value => (float)value).ToArray();
            paint.PathEffect = SKPathEffect.CreateDash(intervals.Length % 2 == 0 ? intervals : [.. intervals, .. intervals], 0);
        }

        ApplyPaint(paint, stroke.Paint);
        return paint;
    }

    private static void FillPath(SKCanvas canvas, SKPath path, Paint fill)
    {
        if (fill is ImagePaint image)
        {
            FillImage(canvas, path, image);
            return;
        }

        using var paint = new SKPaint { IsAntialias = true, Style = SKPaintStyle.Fill };
        ApplyPaint(paint, fill);
        canvas.DrawPath(path, paint);
    }

    private static void ApplyPaint(SKPaint paint, Paint source)
    {
        switch (source)
        {
            case SolidPaint solid:
                paint.Color = ToSkia(solid.Color);
                break;
            case LinearGradientPaint linear:
                paint.Shader = SKShader.CreateLinearGradient(
                    new SKPoint((float)linear.X1, (float)linear.Y1),
                    new SKPoint((float)linear.X2, (float)linear.Y2),
                    linear.Stops.Select(stop => ToSkia(stop.Color)).ToArray(),
                    linear.Stops.Select(stop => (float)stop.Offset).ToArray(),
                    SKShaderTileMode.Clamp);
                break;
            case RadialGradientPaint radial:
                var scaleY = radial.RadiusX > 0 ? (float)(radial.RadiusY / radial.RadiusX) : 1;
                var local = SKMatrix.CreateScale(1, scaleY, (float)radial.CenterX, (float)radial.CenterY);
                paint.Shader = SKShader.CreateRadialGradient(
                    new SKPoint((float)radial.CenterX, (float)radial.CenterY),
                    (float)Math.Max(0.01, radial.RadiusX),
                    radial.Stops.Select(stop => ToSkia(stop.Color)).ToArray(),
                    radial.Stops.Select(stop => (float)stop.Offset).ToArray(),
                    SKShaderTileMode.Clamp,
                    local);
                break;
            case ImagePaint image:
                paint.Color = SKColors.LightGray.WithAlpha(Alpha(image.Opacity));
                break;
        }
    }

    private static void FillImage(SKCanvas canvas, SKPath path, ImagePaint image)
    {
        var decoded = DecodeImage(image.Key, image.Data);
        if (decoded is null)
        {
            using var placeholder = new SKPaint { Color = new SKColor(0xD9, 0xD9, 0xD9), IsAntialias = true };
            canvas.DrawPath(path, placeholder);
            return;
        }

        canvas.Save();
        canvas.ClipPath(path, antialias: true);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha(Alpha(image.Opacity)) };
        if (image.Tile)
        {
            var scale = SKMatrix.CreateScale((float)(image.Width / decoded.Width), (float)(image.Height / decoded.Height));
            var shaderMatrix = scale.PostConcat(SKMatrix.CreateTranslation((float)image.X, (float)image.Y));
            paint.Shader = SKShader.CreateImage(decoded, SKShaderTileMode.Repeat, SKShaderTileMode.Repeat, shaderMatrix);
            canvas.DrawPath(path, paint);
        }
        else
        {
            // The used part of the picture (a:srcRect: fractions cut off each side; negative values extend it).
            var left = image.SourceLeft * decoded.Width;
            var top = image.SourceTop * decoded.Height;
            var right = decoded.Width - (image.SourceRight * decoded.Width);
            var bottom = decoded.Height - (image.SourceBottom * decoded.Height);
            var source = new SKRect((float)left, (float)top, (float)right, (float)bottom);
            var destination = SKRect.Create((float)image.X, (float)image.Y, (float)image.Width, (float)image.Height);
            canvas.DrawImage(decoded, source, destination, paint);
        }

        canvas.Restore();
    }
}
