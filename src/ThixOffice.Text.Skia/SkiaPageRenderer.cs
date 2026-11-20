using System.Collections.Concurrent;
using SkiaSharp;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Unicode;

namespace ThixOffice.Text.Skia;

/// <summary>
/// THE renderer of display lists: draws every item of ThixOffice.Text (glyph runs, rectangles, lines, pictures, clips, and the
/// vector layer – paths with solid/gradient/picture paints, dashed strokes, shadows, transforms, path clips, opacity groups) on
/// any <see cref="SKCanvas"/>: an editor surface through Avalonia's Skia lease, a bitmap (<see cref="RenderPng"/>), a PDF page
/// (<see cref="PdfExport"/>), a printer band. Canvas units are points: the caller scales for zoom and device resolution. Glyph
/// runs with shaped glyphs are drawn glyph by glyph with the measurer's typeface (pass the measurer that produced the layout so
/// that the glyph ids match); runs without glyphs are drawn as text at their character positions.
/// </summary>
public static partial class SkiaPageRenderer
{
    private static readonly ConcurrentDictionary<FontFace, SKTypeface> Typefaces = new();
    private static readonly ConcurrentDictionary<string, SKImage?> Images = new(StringComparer.Ordinal);
    private const int ImageCacheLimit = 64;

    /// <summary>
    /// Draws every item of <paramref name="page"/> in order (no page background: the caller fills it). <paramref name="forPaper"/>:
    /// text without subpixel (colour) antialiasing – for printers and bitmaps that are not shown on this screen.
    /// </summary>
    public static void Draw(SKCanvas canvas, PageDisplayList page, SkiaTextMeasurer? measurer = null, bool forPaper = false) =>
        Draw(canvas, page.Items, measurer, forPaper);

    /// <summary>
    /// Draws <paramref name="items"/> in order. Clips (rectangle or path), transforms and opacity groups nest; each end item closes
    /// the innermost open scope, scopes left open at the end are closed, a stray end is ignored – the canvas leaves as it came.
    /// </summary>
    public static void Draw(SKCanvas canvas, IReadOnlyList<DisplayItem> items, SkiaTextMeasurer? measurer = null, bool forPaper = false)
    {
        var scopes = 0;
        foreach (var item in items)
        {
            switch (item)
            {
                case ClipBeginItem clip:
                    canvas.Save();
                    ClipRectangle(canvas, clip);
                    scopes++;
                    break;
                case ClipPathBeginItem clipPath:
                    canvas.Save();
                    using (var path = ToSkia(clipPath.Path))
                    {
                        canvas.ClipPath(path, antialias: true);
                    }

                    scopes++;
                    break;
                case TransformBeginItem transform:
                    canvas.Save();
                    var matrix = ToSkia(transform.Matrix);
                    canvas.Concat(ref matrix);
                    scopes++;
                    break;
                case OpacityBeginItem opacity:
                    using (var paint = new SKPaint { Color = SKColors.White.WithAlpha(Alpha(opacity.Opacity)) })
                    {
                        canvas.SaveLayer(paint);
                    }

                    scopes++;
                    break;
                case ClipEndItem or TransformEndItem or OpacityEndItem:
                    if (scopes > 0)
                    {
                        canvas.Restore();
                        scopes--;
                    }

                    break;
                case VectorPathItem path:
                    DrawPath(canvas, path);
                    break;
                case RectangleItem rectangle:
                    DrawRectangle(canvas, rectangle);
                    break;
                case LineItem line:
                    DrawLine(canvas, line);
                    break;
                case GlyphRunItem run:
                    DrawGlyphRun(canvas, run, measurer, forPaper);
                    break;
                case ImageItem image:
                    DrawImage(canvas, image);
                    break;
            }
        }

        for (; scopes > 0; scopes--)
        {
            canvas.Restore();
        }
    }

    /// <summary>Renders a page into a PNG <paramref name="pixelWidth"/> pixels wide (the height follows the page's aspect).</summary>
    public static byte[] RenderPng(PageDisplayList page, int pixelWidth, SkiaTextMeasurer? measurer = null, RgbColor? background = null) =>
        RenderImage(page, pixelWidth, RasterFormat.Png, measurer, background);

    /// <summary>
    /// Renders a page into an encoded picture <paramref name="pixelWidth"/> pixels wide: PNG (lossless) or JPEG (<paramref name="quality"/>
    /// 1–100). Slides exported as pictures, thumbnails, tests.
    /// </summary>
    public static byte[] RenderImage(PageDisplayList page, int pixelWidth, RasterFormat format, SkiaTextMeasurer? measurer = null, RgbColor? background = null, int quality = 92)
    {
        var scale = pixelWidth / Math.Max(1, page.Width);
        var pixelHeight = Math.Max(1, (int)Math.Round(page.Height * scale));
        using var bitmap = new SKBitmap(Math.Max(1, pixelWidth), pixelHeight, SKColorType.Rgba8888, SKAlphaType.Premul);
        using (var canvas = new SKCanvas(bitmap))
        {
            canvas.Clear(ToSkia(background ?? RgbColor.White));
            canvas.Scale((float)scale);
            Draw(canvas, page.Items, measurer, forPaper: true);
        }

        using var image = SKImage.FromBitmap(bitmap);
        using var data = format == RasterFormat.Jpeg ? image.Encode(SKEncodedImageFormat.Jpeg, Math.Clamp(quality, 1, 100)) : image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    /// <summary>
    /// A rectangle clip: aliased while the canvas is axis-aligned (cell edges stay on whole device pixels, the grid's lines stay
    /// sharp), antialiased under a rotation or skew.
    /// </summary>
    private static void ClipRectangle(SKCanvas canvas, ClipBeginItem clip)
    {
        var matrix = canvas.TotalMatrix;
        var axisAligned = matrix.SkewX == 0 && matrix.SkewY == 0;
        canvas.ClipRect(SKRect.Create((float)clip.X, (float)clip.Y, (float)Math.Max(0, clip.Width), (float)Math.Max(0, clip.Height)), antialias: !axisAligned);
    }

    private static byte Alpha(double opacity) => (byte)Math.Round(Math.Clamp(opacity, 0, 1) * 255);

    private static SKColor ToSkia(RgbColor color) => new(color.R, color.G, color.B, color.A);

    /// <summary>The picture, decoded once per key (a picture Skia cannot decode is a grey box with a cross).</summary>
    public static SKImage? DecodeImage(string key, byte[] data)
    {
        if (Images.Count > ImageCacheLimit)
        {
            Images.Clear();
        }

        return Images.GetOrAdd(key, _ => SKImage.FromEncodedData(data));
    }

    private static void DrawImage(SKCanvas canvas, ImageItem item)
    {
        var rect = SKRect.Create((float)item.X, (float)item.Y, (float)item.Width, (float)item.Height);
        if (DecodeImage(item.Key, item.Data) is { } image)
        {
            using var paint = new SKPaint { FilterQuality = SKFilterQuality.High, IsAntialias = true };
            if (item.Crop is { } crop)
            {
                // The shown part of the picture stretched over the rectangle (what lies outside the picture stays empty).
                var source = SKRect.Create((float)(crop.Left * image.Width), (float)(crop.Top * image.Height),
                    (float)(image.Width * Math.Max(0.001, 1 - crop.Left - crop.Right)), (float)(image.Height * Math.Max(0.001, 1 - crop.Top - crop.Bottom)));
                canvas.Save();
                canvas.ClipRect(rect);
                canvas.DrawImage(image, source, rect, paint);
                canvas.Restore();
                return;
            }

            canvas.DrawImage(image, rect, paint);
            return;
        }

        using var stroke = new SKPaint { Color = new SKColor(0xA6, 0xA6, 0xA6), Style = SKPaintStyle.Stroke, StrokeWidth = 0.75f, IsAntialias = true };
        canvas.DrawRect(rect, stroke);
        canvas.DrawLine(rect.Left, rect.Top, rect.Right, rect.Bottom, stroke);
        canvas.DrawLine(rect.Left, rect.Bottom, rect.Right, rect.Top, stroke);
    }

    private static void DrawRectangle(SKCanvas canvas, RectangleItem item)
    {
        var rect = SKRect.Create((float)item.X, (float)item.Y, (float)item.Width, (float)item.Height);
        if (item.Fill is { } fill)
        {
            using var paint = new SKPaint { Color = ToSkia(fill), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawRect(rect, paint);
        }

        if (item.Stroke is { } stroke && item.StrokeWidth > 0)
        {
            using var paint = new SKPaint
            {
                Color = ToSkia(stroke),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = (float)item.StrokeWidth,
                IsAntialias = true,
            };
            canvas.DrawRect(rect, paint);
        }
    }

    private static void DrawLine(SKCanvas canvas, LineItem item)
    {
        var thickness = (float)Math.Max(item.Thickness, 0.1);
        using var paint = new SKPaint
        {
            Color = ToSkia(item.Color),
            Style = SKPaintStyle.Stroke,
            StrokeWidth = thickness,
            IsAntialias = true,
        };
        var (x1, y1, x2, y2) = ((float)item.X1, (float)item.Y1, (float)item.X2, (float)item.Y2);
        switch (item.Style)
        {
            case LineStyle.Double:
                canvas.DrawLine(x1, y1, x2, y2, paint);
                if (x1 == x2 && y1 != y2)
                {
                    canvas.DrawLine(x1 + thickness * 2, y1, x2 + thickness * 2, y2, paint);
                }
                else
                {
                    canvas.DrawLine(x1, y1 + thickness * 2, x2, y2 + thickness * 2, paint);
                }

                return;
            case LineStyle.Dotted:
                paint.PathEffect = SKPathEffect.CreateDash([thickness, thickness * 2], 0);
                break;
            case LineStyle.Dashed:
                paint.PathEffect = SKPathEffect.CreateDash([thickness * 4, thickness * 3], 0);
                break;
            case LineStyle.Wavy:
                DrawWave(canvas, x1, y1, x2, thickness, paint);
                return;
        }

        canvas.DrawLine(x1, y1, x2, y2, paint);
    }

    private static void DrawWave(SKCanvas canvas, float x1, float y, float x2, float thickness, SKPaint paint)
    {
        var step = Math.Max(thickness * 2, 1.5f);
        using var path = new SKPath();
        path.MoveTo(x1, y);
        var up = true;
        for (var x = x1; x < x2; x += step)
        {
            var end = Math.Min(x + step, x2);
            path.QuadTo(x + ((end - x) / 2), y + (up ? -step : step), end, y);
            up = !up;
        }

        canvas.DrawPath(path, paint);
    }

    private static void DrawGlyphRun(SKCanvas canvas, GlyphRunItem item, SkiaTextMeasurer? measurer, bool forPaper)
    {
        var typeface = measurer?.GetTypeface(item.Face) ?? TypefaceOf(item.Face);
        using var font = new SKFont(typeface, (float)item.Font.Size) { Subpixel = true, Edging = forPaper ? SKFontEdging.Antialias : SKFontEdging.SubpixelAntialias };
        using var paint = new SKPaint { Color = ToSkia(item.Color), IsAntialias = true };
        if (item.Glyphs.Length > 0 && item.Glyphs.Length == item.GlyphX.Length)
        {
            using var builder = new SKTextBlobBuilder();
            var run = builder.AllocatePositionedRun(font, item.Glyphs.Length);
            run.SetGlyphs(item.Glyphs);
            var positions = new SKPoint[item.Glyphs.Length];
            for (var index = 0; index < positions.Length; index++)
            {
                positions[index] = new SKPoint(item.GlyphX[index], 0);
            }

            run.SetPositions(positions);
            using var blob = builder.Build();
            if (blob is not null)
            {
                canvas.DrawText(blob, (float)item.X, (float)item.Y, paint);
            }

            return;
        }

        var text = item.Text;
        var position = 0;
        while (position < text.Length)
        {
            var length = TextBoundaries.GraphemeLength(text, position);
            var grapheme = text.Substring(position, length);
            var x = item.X + (position < item.CharacterX.Length ? item.CharacterX[position] : 0);
            if (!char.IsControl(grapheme[0]))
            {
                canvas.DrawText(grapheme, (float)x, (float)item.Y, font, paint);
            }

            position += length;
        }
    }

    private static SKTypeface TypefaceOf(FontFace face) =>
        Typefaces.GetOrAdd(face, key => SKTypeface.FromFamilyName(
            key.Family,
            key.Bold ? SKFontStyleWeight.Bold : SKFontStyleWeight.Normal,
            SKFontStyleWidth.Normal,
            key.Italic ? SKFontStyleSlant.Italic : SKFontStyleSlant.Upright) ?? SKTypeface.Default);
}

/// <summary>The picture formats a page renders into.</summary>
public enum RasterFormat
{
    Png,
    Jpeg,
}
