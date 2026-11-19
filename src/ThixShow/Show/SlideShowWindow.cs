using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Platform;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Show;
using ThixShow.Editor;

namespace ThixShow.Show;

/// <summary>Where and how the show runs (Bildschirmpräsentation › Bildschirm, Referentenansicht); kept for the process.</summary>
public static class ShowSettings
{
    /// <summary>The screen of the audience: null = automatic (the second screen when there is one), else the index in the system's list.</summary>
    public static int? AudienceScreen { get; set; }

    /// <summary>Referentenansicht verwenden (PowerPoint's default: on when there are two screens).</summary>
    public static bool UsePresenterView { get; set; } = true;

    /// <summary>Wiederholen, bis „Esc“ gedrückt wird.</summary>
    public static bool Loop { get; set; }
}

/// <summary>
/// The full-screen slide show (F5, Umschalt+F5): a borderless window on the audience's screen drawing the running show
/// (<see cref="ShowSurface"/>), PowerPoint's keys and mouse (<see cref="ShowNavigation"/>), and – with two screens and the
/// option on – the presenter view on the other. Esc or the end screen closes both; the editor then shows the slide the show was on.
/// </summary>
internal sealed class SlideShowWindow : Window
{
    private readonly ShowController _controller;
    private readonly ShowEditor _editor;
    private PresenterWindow? _presenter;

    private SlideShowWindow(ShowEditor editor, ShowController controller)
    {
        _editor = editor;
        _controller = controller;
        Title = "Bildschirmpräsentation";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Surface = new ShowSurface { Controller = controller };
        Content = Surface;
        Cursor = new Cursor(StandardCursorType.Arrow);
        controller.Ended += (_, _) => Finish();
        Surface.PointerPressed += OnPointerPressed;
        Surface.PointerWheelChanged += (_, e) => ShowNavigation.HandleWheel(_controller, e);
    }

    /// <summary>The window of the running show of a process (one at a time; tests reach it here).</summary>
    public static SlideShowWindow? Current { get; private set; }

    public ShowSurface Surface { get; }

    public ShowController Controller => _controller;

    public PresenterWindow? PresenterView => _presenter;

    /// <summary>Starts the show from the first slide or the current one.</summary>
    public static Task StartAsync(OfficeWindow owner, ShowEditor editor, bool fromCurrent)
    {
        if (Current is not null || editor.Session.Presentation.SlideCount == 0)
        {
            return Task.CompletedTask;
        }

        editor.Notes.Commit();
        editor.Session.EndTextEditing();
        var start = fromCurrent ? Math.Max(0, editor.CurrentIndex) : 0;
        var controller = new ShowController(editor.Session.Presentation, new SlideShowOptions(StartSlide: start, IncludeHidden: false, Loop: ShowSettings.Loop));
        controller.Start();
        if (fromCurrent && controller.Show.Shown.ToList().IndexOf(start) is var position and > 0)
        {
            controller.GoTo(position);
        }
        else if (fromCurrent && editor.Session.Presentation.Slides[start].Hidden)
        {
            // Umschalt+F5 on a hidden slide shows it (PowerPoint does).
            controller.GoToSlideNumber(start + 1);
        }

        var window = new SlideShowWindow(editor, controller);
        Current = window;
        var screens = owner.Screens.All;
        var audience = AudienceScreenOf(owner);
        window.Position = audience?.Bounds.Position ?? owner.Position;
        window.Show();
        window.WindowState = WindowState.FullScreen;
        window.Activate();
        window.Surface.Focus();
        Avalonia.Threading.Dispatcher.UIThread.Post(() => window.Surface.Focus(), Avalonia.Threading.DispatcherPriority.Input);
        if (ShowSettings.UsePresenterView && screens.Count > 1 && audience is not null)
        {
            window.ShowPresenter(screens.FirstOrDefault(screen => !screen.Bounds.Equals(audience.Bounds)));
        }

        return Task.CompletedTask;
    }

    /// <summary>The audience's screen: the chosen one, else the second screen when there are two, else the window's own.</summary>
    internal static Screen? AudienceScreenOf(Window owner)
    {
        var screens = owner.Screens.All;
        if (ShowSettings.AudienceScreen is { } chosen && chosen >= 0 && chosen < screens.Count)
        {
            return screens[chosen];
        }

        if (screens.Count > 1)
        {
            return screens.FirstOrDefault(screen => !screen.IsPrimary) ?? screens[^1];
        }

        return owner.Screens.ScreenFromWindow(owner) ?? owner.Screens.Primary;
    }

    /// <summary>Shows (or hides) the presenter view (on <paramref name="screen"/>; without one in a window of its own).</summary>
    public void ShowPresenter(Screen? screen)
    {
        if (_presenter is not null)
        {
            _presenter.Close();
            _presenter = null;
            return;
        }

        _presenter = new PresenterWindow(_controller);
        if (screen is not null)
        {
            _presenter.Position = screen.Bounds.Position;
        }

        _presenter.Closed += (_, _) => _presenter = null;
        _presenter.Show();
        if (screen is not null)
        {
            _presenter.WindowState = WindowState.FullScreen;
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
        {
            e.Handled = ShowNavigation.HandleKey(_controller, e.Key, e.KeyModifiers);
        }
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface);
        if (point.Properties.IsRightButtonPressed)
        {
            Surface.ContextMenu = ShowNavigation.Menu(_controller, () => ShowPresenter(null), _presenter is not null);
            return;
        }

        if (point.Properties.IsLeftButtonPressed)
        {
            e.Handled = true;
            _controller.Next();
        }
    }

    /// <summary>The show ended: both windows close, the editor shows the slide the show was on.</summary>
    private void Finish()
    {
        var slideIndex = _controller.Show.SlideIndex;
        Current = null;
        _presenter?.Close();
        _presenter = null;
        Close();
        if (slideIndex >= 0 && slideIndex < _editor.Session.Presentation.SlideCount)
        {
            _editor.Session.GoToSlide(_editor.Session.Presentation.Slides[slideIndex].Id);
        }

        _editor.FocusDocument();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        if (Current == this)
        {
            Current = null;
            _controller.End();
        }
    }
}
