using System.Collections.Concurrent;
using SkiaSharp;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Unicode;

namespace ThixOffice.Text.Skia;

/// <summary>
/// Draws a <see cref="PageDisplayList"/> on any <see cref="SKCanvas"/> (the editor control through Avalonia's Skia lease, a
/// bitmap, a PDF page, a printer). Canvas units are points: the caller scales for zoom and device resolution. Glyph runs with
/// shaped glyphs are drawn glyph by glyph with the measurer's typeface (pass the measurer that produced the layout so that the
/// glyph ids match); runs without glyphs are drawn as text at their character positions.
/// </summary>
public static class SkiaPageRenderer
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
    /// Draws <paramref name="items"/> in order. <see cref="ClipBeginItem"/> / <see cref="ClipEndItem"/> nest (each clip is
    /// intersected with the one around it); clips left open at the end are closed, a stray end is ignored – the canvas leaves
    /// as it came.
    /// </summary>
    public static void Draw(SKCanvas canvas, IReadOnlyList<DisplayItem> items, SkiaTextMeasurer? measurer = null, bool forPaper = false)
    {
        var clips = 0;
        foreach (var item in items)
        {
            switch (item)
            {
                case ClipBeginItem clip:
                    canvas.Save();
                    canvas.ClipRect(SKRect.Create((float)clip.X, (float)clip.Y, (float)Math.Max(0, clip.Width), (float)Math.Max(0, clip.Height)));
                    clips++;
                    break;
                case ClipEndItem when clips > 0:
                    canvas.Restore();
                    clips--;
                    break;
                case TransformBeginItem transform:
                    canvas.Save();
                    canvas.Translate((float)transform.X, (float)transform.Y);
                    if (transform.Rotation != 0)
                    {
                        canvas.RotateDegrees((float)transform.Rotation);
                    }

                    if (transform.Scale != 1)
                    {
                        canvas.Scale((float)transform.Scale);
                    }

                    clips++;
                    break;
                case TransformEndItem when clips > 0:
                    canvas.Restore();
                    clips--;
                    break;
                case PathItem path:
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

        for (; clips > 0; clips--)
        {
            canvas.Restore();
        }
    }

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

    private static void DrawPath(SKCanvas canvas, PathItem item)
    {
        using var path = new SKPath();
        var points = item.Points;
        var index = 0;
        foreach (var verb in item.Verbs)
        {
            switch (verb)
            {
                case PathVerb.Move when index + 1 < points.Length:
                    path.MoveTo((float)points[index], (float)points[index + 1]);
                    index += 2;
                    break;
                case PathVerb.Line when index + 1 < points.Length:
                    path.LineTo((float)points[index], (float)points[index + 1]);
                    index += 2;
                    break;
                case PathVerb.Cubic when index + 5 < points.Length:
                    path.CubicTo((float)points[index], (float)points[index + 1], (float)points[index + 2], (float)points[index + 3], (float)points[index + 4], (float)points[index + 5]);
                    index += 6;
                    break;
                case PathVerb.Close:
                    path.Close();
                    break;
            }
        }

        if (item.Fill is { } fill)
        {
            using var paint = new SKPaint { Color = ToSkia(fill), Style = SKPaintStyle.Fill, IsAntialias = true };
            canvas.DrawPath(path, paint);
        }

        if (item.Stroke is { } stroke && item.StrokeWidth > 0)
        {
            var width = (float)item.StrokeWidth;
            using var paint = new SKPaint
            {
                Color = ToSkia(stroke),
                Style = SKPaintStyle.Stroke,
                StrokeWidth = width,
                IsAntialias = true,
                StrokeJoin = item.Join == LineJoin.Round ? SKStrokeJoin.Round : SKStrokeJoin.Miter,
                StrokeCap = item.Join == LineJoin.Round ? SKStrokeCap.Round : SKStrokeCap.Butt,
            };
            if (item.Style == LineStyle.Dashed)
            {
                paint.PathEffect = SKPathEffect.CreateDash([width * 4, width * 3], 0);
            }
            else if (item.Style == LineStyle.Dotted)
            {
                paint.PathEffect = SKPathEffect.CreateDash([width, width * 2], 0);
            }

            canvas.DrawPath(path, paint);
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
