using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using SkiaSharp;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Skia;
using ThixShow.Editor;

namespace ThixShow.Show;

/// <summary>
/// Draws a running show's current frame, fitted into the control with black around a slide of another aspect ratio (or the
/// black/white screen). <see cref="IsFramed"/> (the presenter view) draws only the slide, with rounded corners and a soft shadow on
/// the page around it. Draws only when the controller says something changed.
/// </summary>
internal sealed class ShowSurface : Control
{
    /// <summary>The room around a framed slide (for its shadow).</summary>
    private const double FrameMargin = 14;

    private const double FrameRadius = 10;

    private ShowController? _controller;
    private bool _isFramed;

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

    /// <summary>The slide as a card on the page (the presenter view) instead of full screen with black around it.</summary>
    public bool IsFramed
    {
        get => _isFramed;
        set
        {
            _isFramed = value;
            ClipToBounds = !value;
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
            var margin = _isFramed ? FrameMargin : 0;
            var room = new Size(Math.Max(1, Bounds.Width - (2 * margin)), Math.Max(1, Bounds.Height - (2 * margin)));
            var scale = Math.Min(room.Width / width, room.Height / height);
            var top = _isFramed ? margin : (Bounds.Height - (height * scale)) / 2;
            return new Rect((Bounds.Width - (width * scale)) / 2, top, width * scale, height * scale);
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
        var slide = SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
        var framed = _isFramed;
        if (framed)
        {
            var overlay = Ui.Brush("Mat.Overlay") is ISolidColorBrush solid ? solid.Color : default;
            var shadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 22, Color = new Color((byte)(overlay.A * 0.55), overlay.R, overlay.G, overlay.B) });
            context.DrawRectangle(Ui.Brush("Mat.Surface"), null, new RoundedRect(rect, FrameRadius), shadow);
        }

        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
        {
            canvas.Save();
            if (framed)
            {
                canvas.ClipRoundRect(new SKRoundRect(slide, (float)FrameRadius), antialias: true);
            }
            else
            {
                canvas.ClipRect(bounds);
            }

            using (var background = new SKPaint { Color = blank == BlankScreen.White ? ShowPalette.ShowWhite : ShowPalette.ShowBlack })
            {
                canvas.DrawRect(framed ? slide : bounds, background);
            }

            if (blank == BlankScreen.None)
            {
                canvas.ClipRect(slide);
                canvas.Translate((float)rect.X, (float)rect.Y);
                canvas.Scale((float)scale);
                SkiaPageRenderer.Draw(canvas, frame, OfficeFonts.Skia);
            }

            canvas.Restore();
        }));
    }
}
