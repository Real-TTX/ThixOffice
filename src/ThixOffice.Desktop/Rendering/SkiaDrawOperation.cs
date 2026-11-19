using Avalonia;
using Avalonia.Media;
using Avalonia.Platform;
using Avalonia.Rendering.SceneGraph;
using Avalonia.Skia;
using SkiaSharp;

namespace ThixOffice.Desktop.Rendering;

/// <summary>
/// A custom draw operation of an editor surface through Avalonia's Skia lease, on the render thread: the callback draws on the
/// control's canvas. Everything it captures must be immutable (pictures, item lists): the render thread may draw it after the
/// UI thread moved on. Shared by every app (ThixWrite's pages, ThixCalc's grid).
/// <para>
/// <paramref name="devicePixels"/>: the canvas is reset to whole device pixels (the control's origin rounded, no scaling), so
/// content laid out in device pixels – the grid's 1-px lines – stays sharp at any zoom and screen scaling.
/// </para>
/// </summary>
public sealed class SkiaDrawOperation(Rect bounds, Action<SKCanvas> draw, bool devicePixels = false) : ICustomDrawOperation
{
    public Rect Bounds => bounds;

    public bool HitTest(Point p) => bounds.Contains(p);

    public bool Equals(ICustomDrawOperation? other) => ReferenceEquals(this, other);

    public void Dispose()
    {
    }

    public void Render(ImmediateDrawingContext context)
    {
        if (context.TryGetFeature(typeof(ISkiaSharpApiLeaseFeature)) is not ISkiaSharpApiLeaseFeature feature)
        {
            return;
        }

        using var lease = feature.Lease();
        var canvas = lease.SkCanvas;
        canvas.Save();
        try
        {
            if (devicePixels)
            {
                var matrix = canvas.TotalMatrix;
                canvas.SetMatrix(SKMatrix.CreateTranslation(MathF.Round(matrix.TransX), MathF.Round(matrix.TransY)));
            }

            draw(canvas);
        }
        finally
        {
            canvas.Restore();
        }
    }

    /// <summary>Draws pictures recorded in points at their places (device-independent pixels), each clipped to its rectangle.</summary>
    public static void DrawPictures(SKCanvas canvas, IReadOnlyList<(Rect Rect, SKPicture Picture)> pictures, double scale)
    {
        foreach (var (rect, picture) in pictures)
        {
            canvas.Save();
            canvas.ClipRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
            canvas.Translate((float)rect.X, (float)rect.Y);
            canvas.Scale((float)scale);
            canvas.DrawPicture(picture);
            canvas.Restore();
        }
    }
}
