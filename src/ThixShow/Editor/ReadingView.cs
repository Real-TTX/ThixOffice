using Avalonia.Controls;
using Avalonia.Input;
using ThixShow.Core.Show;
using ThixShow.Show;

namespace ThixShow.Editor;

/// <summary>
/// Leseansicht: the show inside the window (transitions and animations included, the ribbon and status bar stay), driven by the
/// show's keys and clicks; Esc or the end goes back to the normal view on the slide it showed.
/// </summary>
internal sealed class ReadingView : Panel
{
    private readonly ShowEditor _editor;
    private ShowController? _controller;

    public ReadingView(ShowEditor editor)
    {
        _editor = editor;
        Surface = new ShowSurface();
        Children.Add(Surface);
        Focusable = true;
        Surface.PointerPressed += (_, e) =>
        {
            var point = e.GetCurrentPoint(Surface);
            if (_controller is null)
            {
                return;
            }

            if (point.Properties.IsRightButtonPressed)
            {
                Surface.ContextMenu = ShowNavigation.Menu(_controller, new ShowMenuActions());
                return;
            }

            _controller.Next();
        };
        Surface.PointerWheelChanged += (_, e) =>
        {
            if (_controller is not null)
            {
                ShowNavigation.HandleWheel(_controller, e);
            }
        };
        Avalonia.Automation.AutomationProperties.SetName(this, "Leseansicht");
    }

    public ShowSurface Surface { get; }

    public ShowController? Controller => _controller;

    /// <summary>Starts the show at a slide.</summary>
    public void Start(int slideIndex)
    {
        Stop();
        var controller = new ShowController(_editor.Session.Presentation, new SlideShowOptions(StartSlide: slideIndex, IncludeHidden: true));
        controller.Start();
        var position = controller.Show.Shown.ToList().IndexOf(slideIndex);
        if (position > 0)
        {
            controller.GoTo(position);
        }

        controller.Ended += (_, _) =>
        {
            if (_controller == controller)
            {
                var index = controller.Show.SlideIndex;
                _controller = null;
                Surface.Controller = null;
                _editor.EndReading(index);
            }
        };
        _controller = controller;
        Surface.Controller = controller;
    }

    public void Stop()
    {
        if (_controller is { } controller)
        {
            _controller = null;
            Surface.Controller = null;
            controller.End();
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled && _controller is not null)
        {
            e.Handled = ShowNavigation.HandleKey(_controller, e.Key, e.KeyModifiers);
        }
    }
}
