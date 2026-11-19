using SkiaSharp;
using ThixOffice.Text.Drawing;

namespace ThixOffice.Text.Skia;

/// <summary>
/// Draws display lists that use the vector items of ThixOffice.Text (VectorPathItem with solid/gradient/picture paints, strokes
/// with dashes, soft shadows, TransformBegin/End, ClipPathBegin, OpacityBegin/End) as well as the basic ones (glyph runs,
/// rectangles, lines, pictures, rectangular clips – drawn by <see cref="SkiaPageRenderer"/>). Also renders a display list into a
/// PNG (thumbnails, tests) and exports pages to PDF. Additive to SkiaPageRenderer: callers whose lists have no vector items can
/// keep using it.
/// </summary>
public static class SkiaVectorRenderer
{
    private enum Scope : byte
    {
        Clip,
        Transform,
        Opacity,
    }

    /// <summary>Draws <paramref name="items"/> in order; the canvas leaves as it came (open scopes are closed).</summary>
    public static void Draw(SKCanvas canvas, IReadOnlyList<DisplayItem> items, SkiaTextMeasurer? measurer = null, bool forPaper = false)
    {
        var scopes = new Stack<Scope>();
        var single = new DisplayItem[1];
        foreach (var item in items)
        {
            switch (item)
            {
                case ClipBeginItem clip:
                    canvas.Save();
                    canvas.ClipRect(SKRect.Create((float)clip.X, (float)clip.Y, (float)Math.Max(0, clip.Width), (float)Math.Max(0, clip.Height)), antialias: true);
                    scopes.Push(Scope.Clip);
                    break;
                case ClipPathBeginItem clipPath:
                    canvas.Save();
                    using (var path = ToSkia(clipPath.Path))
                    {
                        canvas.ClipPath(path, antialias: true);
                    }

                    scopes.Push(Scope.Clip);
                    break;
                case TransformBeginItem transform:
                    canvas.Save();
                    var matrix = ToSkia(transform.Matrix);
                    canvas.Concat(ref matrix);
                    scopes.Push(Scope.Transform);
                    break;
                case OpacityBeginItem opacity:
                    using (var paint = new SKPaint { Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(opacity.Opacity, 0, 1) * 255)) })
                    {
                        canvas.SaveLayer(paint);
                    }

                    scopes.Push(Scope.Opacity);
                    break;
                case ClipEndItem or TransformEndItem or OpacityEndItem when scopes.Count > 0:
                    canvas.Restore();
                    scopes.Pop();
                    break;
                case ClipEndItem or TransformEndItem or OpacityEndItem:
                    break;
                case VectorPathItem path:
                    DrawPath(canvas, path);
                    break;
                default:
                    single[0] = item;
                    SkiaPageRenderer.Draw(canvas, single, measurer, forPaper);
                    break;
            }
        }

        while (scopes.Count > 0)
        {
            canvas.Restore();
            scopes.Pop();
        }
    }

    public static void Draw(SKCanvas canvas, PageDisplayList page, SkiaTextMeasurer? measurer = null, bool forPaper = false) =>
        Draw(canvas, page.Items, measurer, forPaper);

    /// <summary>Renders a page into a PNG of <paramref name="pixelWidth"/> pixels wide (the height follows the page's aspect).</summary>
    public static byte[] RenderPng(PageDisplayList page, int pixelWidth, SkiaTextMeasurer? measurer = null, RgbColor? background = null)
    {
        var scale = pixelWidth / Math.Max(1, page.Width);
        var pixelHeight = Math.Max(1, (int)Math.Round(page.Height * scale));
        using var bitmap = new SKBitmap(Math.Max(1, pixelWidth), pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            var color = background ?? RgbColor.White;
            canvas.Clear(new SKColor(color.R, color.G, color.B, color.A));
            canvas.Scale((float)scale);
            Draw(canvas, page.Items, measurer, forPaper: true);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>Exports pages to PDF (one page per display list, its size in points), vector items included.</summary>
    public static void ExportPdf(IEnumerable<PageDisplayList> pages, Stream output, PdfMetadata? metadata = null, SkiaTextMeasurer? measurer = null)
    {
        ArgumentNullException.ThrowIfNull(pages);
        ArgumentNullException.ThrowIfNull(output);
        var info = metadata ?? new PdfMetadata();
        var pdfMetadata = SKDocumentPdfMetadata.Default;
        pdfMetadata.Title = info.Title ?? "";
        pdfMetadata.Author = info.Author ?? "";
        pdfMetadata.Subject = info.Subject ?? "";
        pdfMetadata.Creator = info.Creator;
        pdfMetadata.Producer = info.Producer;
        pdfMetadata.Creation = DateTime.Now;
        pdfMetadata.Modified = DateTime.Now;

        using var stream = new SKManagedWStream(output, disposeManagedStream: false);
        using var document = SKDocument.CreatePdf(stream, pdfMetadata) ?? throw new InvalidOperationException("Skia cannot create a PDF document.");
        foreach (var page in pages)
        {
            var canvas = document.BeginPage((float)page.Width, (float)page.Height);
            Draw(canvas, page.Items, measurer, forPaper: true);
            document.EndPage();
        }

        document.Close();
        stream.Flush();
    }

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

    private static SKColor ToSkia(RgbColor color) => new(color.R, color.G, color.B, color.A);

    private static void DrawPath(SKCanvas canvas, VectorPathItem item)
    {
        using var path = ToSkia(item.Path, item.FillRule);
        if (item.Shadow is { } shadow && (item.Fill is not null || item.Stroke is not null))
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

        if (item.Fill is { } fill)
        {
            FillPath(canvas, path, fill);
        }

        if (item.Stroke is { } stroke)
        {
            using var paint = new SKPaint
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
                var intervals = dashes.Count % 2 == 0 ? dashes.Select(value => (float)value).ToArray() : [.. dashes.Select(value => (float)value), .. dashes.Select(value => (float)value)];
                paint.PathEffect = SKPathEffect.CreateDash(intervals, 0);
            }

            ApplyPaint(paint, stroke.Paint);
            canvas.DrawPath(path, paint);
        }
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
                {
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
                }

            case ImagePaint image:
                paint.Color = SKColors.LightGray.WithAlpha((byte)Math.Round(image.Opacity * 255));
                break;
        }
    }

    private static void FillImage(SKCanvas canvas, SKPath path, ImagePaint image)
    {
        var decoded = SkiaPageRenderer.DecodeImage(image.Key, image.Data);
        if (decoded is null)
        {
            using var placeholder = new SKPaint { Color = new SKColor(0xD9, 0xD9, 0xD9), IsAntialias = true };
            canvas.DrawPath(path, placeholder);
            return;
        }

        canvas.Save();
        canvas.ClipPath(path, antialias: true);
        using var paint = new SKPaint { IsAntialias = true, FilterQuality = SKFilterQuality.High, Color = SKColors.White.WithAlpha((byte)Math.Round(Math.Clamp(image.Opacity, 0, 1) * 255)) };
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
