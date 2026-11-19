using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Media;
using SkiaSharp;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Skia;
using ThixShow.Core.Editing;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;
using ThixShow.Core.Show;
using ThixShow.Show;

namespace ThixShow.Editor;

/// <summary>
/// The slide in the middle of the window: the pasteboard (the area around the slide), the slide's picture at the zoom (vector,
/// crisp at any zoom and screen scaling) and, on the <see cref="SlideOverlay"/> above, the selection: frames, handles, smart
/// guides, the text caret and selection. Pointer and keys (SlideCanvas.Input.cs) change the presentation only through the session.
/// Coordinates: slide points × <see cref="Scale"/> + <see cref="Origin"/> = device-independent pixels of this control.
/// </summary>
internal sealed partial class SlideCanvas : Control
{
    /// <summary>The pasteboard around the slide in device-independent pixels.</summary>
    public const double PasteboardMargin = 36;

    private readonly ShowEditor _editor;

    public SlideCanvas(ShowEditor editor)
    {
        _editor = editor;
        Overlay = new SlideOverlay(this);
        Focusable = true;
        ClipToBounds = true;
        InputClient = new SlideInputClient(this);
        AddHandler(TextInputMethodClientRequestedEvent, (_, e) => e.Client = InputClient, Avalonia.Interactivity.RoutingStrategies.Bubble);
        AddHandler(DragDrop.DragOverEvent, OnDragOver);
        AddHandler(DragDrop.DropEvent, OnDrop);
        DragDrop.SetAllowDrop(this, true);
        Avalonia.Automation.AutomationProperties.SetName(this, "Folie");
    }

    public EditingSession Session => _editor.Session;

    public Presentation Presentation => Session.Presentation;

    public Slide? Slide => Session.CurrentSlide;

    /// <summary>The selection layer above the slide.</summary>
    public SlideOverlay Overlay { get; }

    internal SlideInputClient InputClient { get; }

    /// <summary>Device-independent pixels per slide point (zoom 100 % = the slide's real size on the screen).</summary>
    public double Scale => _editor.Zoom / 100 * 96 / 72;

    /// <summary>The slide's size in points.</summary>
    public (double Width, double Height) SlideSize =>
        (Units.EmuToPoints(Presentation.SlideSize.Width), Units.EmuToPoints(Presentation.SlideSize.Height));

    /// <summary>The slide's top-left corner in this control (centred while it is smaller than the view).</summary>
    public Point Origin
    {
        get
        {
            var (width, height) = SlideSize;
            return new Point(Math.Max(PasteboardMargin, (Bounds.Width - (width * Scale)) / 2), Math.Max(PasteboardMargin, (Bounds.Height - (height * Scale)) / 2));
        }
    }

    /// <summary>The slide's rectangle in this control.</summary>
    public Rect SlideRect
    {
        get
        {
            var (width, height) = SlideSize;
            return new Rect(Origin, new Size(width * Scale, height * Scale));
        }
    }

    /// <summary>A slide point in this control.</summary>
    public Point ToView(double x, double y)
    {
        var origin = Origin;
        return new Point(origin.X + (x * Scale), origin.Y + (y * Scale));
    }

    /// <summary>A point of this control on the slide (points).</summary>
    public (double X, double Y) ToSlide(Point point)
    {
        var origin = Origin;
        return ((point.X - origin.X) / Scale, (point.Y - origin.Y) / Scale);
    }

    /// <summary>The size the slide needs at the zoom, with the pasteboard around it (the scroll viewer's extent).</summary>
    protected override Size MeasureOverride(Size availableSize)
    {
        var (width, height) = SlideSize;
        return new Size((width * Scale) + (2 * PasteboardMargin), (height * Scale) + (2 * PasteboardMargin));
    }

    public override void Render(DrawingContext context)
    {
        context.FillRectangle(Ui.Brush("Mat.Bg"), new Rect(Bounds.Size));
        if (Slide is not { } slide)
        {
            return;
        }

        var rect = SlideRect;
        var shadow = Ui.Brush("Mat.Overlay") is ISolidColorBrush solid ? solid.Color : default;
        context.FillRectangle(new SolidColorBrush(new Color((byte)(shadow.A * 0.25), shadow.R, shadow.G, shadow.B)), rect.Translate(new Vector(0, 2)).Inflate(1));
        var scale = Scale;
        if (_preview is { } preview)
        {
            var frame = preview.Frame().ToDisplayList();
            context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
            {
                canvas.Save();
                canvas.ClipRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
                canvas.Translate((float)rect.X, (float)rect.Y);
                canvas.Scale((float)scale);
                SkiaPageRenderer.Draw(canvas, frame, OfficeFonts.Skia);
                canvas.Restore();
            }));
            return;
        }

        var picture = _editor.Pictures.Editor(Presentation, slide);
        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
        {
            canvas.Save();
            canvas.ClipRect(SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height));
            canvas.DrawColor(ShowPalette.EmptySlide);
            canvas.Translate((float)rect.X, (float)rect.Y);
            canvas.Scale((float)scale);
            canvas.DrawPicture(picture);
            canvas.Restore();
        }));
    }

    private ShowController? _preview;

    /// <summary>True while Vorschau plays the slide.</summary>
    public bool IsPreviewing => _preview is not null;

    /// <summary>Vorschau: plays the slide's transition and all its build steps on the slide, then shows the slide again.</summary>
    public void PlayPreview(int slideIndex)
    {
        StopPreview();
        var controller = new ShowController(Presentation, new SlideShowOptions(StartSlide: slideIndex, IncludeHidden: true, UseTimings: false));
        _preview = controller;
        controller.Changed += (_, _) =>
        {
            if (_preview != controller)
            {
                return;
            }

            InvalidateVisual();
            if (controller.IsTicking)
            {
                return;
            }

            // Between steps: the next click comes by itself; after the last one the editor shows the slide again.
            if (controller.Show.StepsStarted < controller.Show.Timeline.Steps.Count)
            {
                Avalonia.Threading.Dispatcher.UIThread.Post(controller.Next, Avalonia.Threading.DispatcherPriority.Background);
            }
            else
            {
                Avalonia.Threading.DispatcherTimer.RunOnce(StopPreview, TimeSpan.FromMilliseconds(400));
            }
        };
        controller.Start();
        if (controller.Show.Shown.ToList().IndexOf(slideIndex) is var position and > 0)
        {
            controller.GoTo(position);
        }

        Overlay.InvalidateVisual();
    }

    public void StopPreview()
    {
        if (_preview is null)
        {
            return;
        }

        _preview = null;
        InvalidateVisual();
        Overlay.InvalidateVisual();
    }

    /// <summary>The slide or the zoom changed: measure and draw again (the selection layer too).</summary>
    public void Refresh()
    {
        InvalidateMeasure();
        InvalidateVisual();
        Overlay.InvalidateVisual();
        InputClient.NotifyChanged();
    }

    // ===== Geometry of the selection =====

    /// <summary>The ids of the selected shapes on the current slide.</summary>
    public IReadOnlyList<int> SelectedIds => [.. Session.Selection.Shapes];

    /// <summary>The corners of a shape in this control (top-left, top-right, bottom-right, bottom-left of its box).</summary>
    public IReadOnlyList<Point> CornersOf(int shapeId) =>
        Slide is { } slide ? [.. SlideHitTesting.CornersOf(Presentation, slide, shapeId).Select(corner => ToView(corner.X, corner.Y))] : [];

    /// <summary>The text layout being edited on the slide, or null.</summary>
    public PlacedTextLayout? EditedText =>
        Session.Selection.Text is { } text && Slide is { } slide ? TextGeometry.Of(Presentation, slide, text.Target, OfficeFonts.Cached, DateTime.Now) : null;

    /// <summary>The caret's rectangle in this control (the IME window goes there), or null.</summary>
    public Rect? CaretRect
    {
        get
        {
            if (Session.Selection.Text is not { } text || EditedText is not { } layout)
            {
                return null;
            }

            var (top, bottom) = layout.CaretLine(text.Active);
            var (a, b) = (ToView(top.X, top.Y), ToView(bottom.X, bottom.Y));
            return new Rect(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(1, Math.Abs(a.X - b.X)), Math.Max(1, Math.Abs(a.Y - b.Y)));
        }
    }
}
