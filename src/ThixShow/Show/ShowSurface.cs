using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Skia;
using ThixShow.Editor;

namespace ThixShow.Show;

/// <summary>
/// Draws a running show's current frame, fitted into the control with black around a slide of another aspect ratio (or the
/// black/white screen). Draws only when the controller says something changed.
/// </summary>
internal sealed class ShowSurface : Control
{
    private ShowController? _controller;

    public ShowSurface()
    {
        ClipToBounds = true;
        Focusable = true;
    }

    public ShowController? Controller
    {
        get => _controller;
        set
        {
            if (_controller is not null)
            {
                _controller.Changed -= OnChanged;
            }

            _controller = value;
            if (value is not null)
            {
                value.Changed += OnChanged;
            }

            InvalidateVisual();
        }
    }

    /// <summary>The slide's rectangle in this control.</summary>
    public Rect SlideRect
    {
        get
        {
            if (_controller is null)
            {
                return default;
            }

            var size = _controller.Presentation.SlideSize;
            var (width, height) = (Core.Model.Units.EmuToPoints(size.Width), Core.Model.Units.EmuToPoints(size.Height));
            var scale = Math.Min(Bounds.Width / width, Bounds.Height / height);
            return new Rect((Bounds.Width - (width * scale)) / 2, (Bounds.Height - (height * scale)) / 2, width * scale, height * scale);
        }
    }

    private void OnChanged(object? sender, EventArgs e) => InvalidateVisual();

    public override void Render(DrawingContext context)
    {
        if (_controller is not { } controller || Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var frame = controller.Frame().ToDisplayList();
        var blank = controller.Blank;
        var rect = SlideRect;
        var scale = rect.Width / Math.Max(1, frame.Width);
        var bounds = SKRect.Create((float)Bounds.Width, (float)Bounds.Height);
        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
        {
            using (var background = new SKPaint { Color = blank == BlankScreen.White ? ShowPalette.ShowWhite : ShowPalette.ShowBlack })
            {
                canvas.DrawRect(bounds, background);
            }

            if (blank != BlankScreen.None)
            {
                return;
            }

            canvas.Save();
            canvas.ClipRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
            canvas.Translate((float)rect.X, (float)rect.Y);
            canvas.Scale((float)scale);
            SkiaPageRenderer.Draw(canvas, frame, OfficeFonts.Skia);
            canvas.Restore();
        }));
    }
}
