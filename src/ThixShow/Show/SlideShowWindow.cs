using System.Globalization;
using Avalonia;
using Avalonia.Animation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Show;
using ThixShow.Editor;

namespace ThixShow.Show;

/// <summary>
/// Where and how the show runs (Bildschirmpräsentation › Bildschirm, Referentenansicht, Wiederholen; the notes' size of the presenter
/// view), remembered between sessions in the app's settings (<see cref="SettingsService.RememberAppValue"/>).
/// </summary>
public static class ShowSettings
{
    public const double MinNotesSize = 10;

    public const double MaxNotesSize = 48;

    public const double DefaultNotesSize = 18;

    private const string ScreenKey = "show.screen";
    private const string PresenterKey = "show.presenterView";
    private const string LoopKey = "show.loop";
    private const string NotesSizeKey = "show.notesSize";

    /// <summary>
    /// The screen of the audience: null = automatic (<see cref="ShowScreens.AutomaticRule"/>), else the index in the system's list. A
    /// remembered screen that is not connected now counts as automatic (<see cref="ShowScreens.Chosen"/>) and stays remembered.
    /// </summary>
    public static int? AudienceScreen
    {
        get => int.TryParse(SettingsService.AppValue(ScreenKey), NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) ? index : null;
        set => SettingsService.RememberAppValue(ScreenKey, value?.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>Referentenansicht verwenden (PowerPoint's default: on when there are two screens).</summary>
    public static bool UsePresenterView
    {
        get => SettingsService.AppValue(PresenterKey) != "false";
        set => SettingsService.RememberAppValue(PresenterKey, value ? null : "false");
    }

    /// <summary>Wiederholen, bis „Esc“ gedrückt wird.</summary>
    public static bool Loop
    {
        get => SettingsService.AppValue(LoopKey) == "true";
        set => SettingsService.RememberAppValue(LoopKey, value ? "true" : null);
    }

    /// <summary>The font size of the notes in the presenter view (points).</summary>
    public static double NotesSize
    {
        get => double.TryParse(SettingsService.AppValue(NotesSizeKey), NumberStyles.Float, CultureInfo.InvariantCulture, out var size)
            ? Math.Clamp(size, MinNotesSize, MaxNotesSize)
            : DefaultNotesSize;
        set => SettingsService.RememberAppValue(NotesSizeKey, value == DefaultNotesSize ? null : value.ToString(CultureInfo.InvariantCulture));
    }
}

/// <summary>
/// The slide show (F5, Umschalt+F5, Alt+F5): a borderless full-screen window on the audience's screen drawing the running show
/// (<see cref="ShowSurface"/>) with black around the slide, PowerPoint's keys and mouse (<see cref="ShowNavigation"/>), the quiet control
/// bar at the bottom left (it fades in when the mouse moves and out after <see cref="IdleDelay"/>; the cursor hides with it), "Alle
/// Folien", and – with two screens and the option on – the presenter view on the other screen. "Anzeige tauschen" trades the screens.
/// Alt+F5 tries the presenter view on one screen: it opens in a window, the show window waits hidden until "Anzeige tauschen".
/// Esc or the end screen closes everything; the editor then shows the slide the show was on.
/// </summary>
internal sealed class SlideShowWindow : Window
{
    private readonly ShowController _controller;
    private readonly ShowEditor? _editor;
    private readonly DispatcherTimer _idle;
    private readonly Border _bar;
    private readonly TextBlock _barPosition;
    private PresenterWindow? _presenter;
    private ShowScreen? _audienceScreen;
    private ShowScreen? _presenterScreen;
    private Point? _lastPointer;
    private bool _menuOpen;
    private bool _pinControls;

    private SlideShowWindow(ShowEditor? editor, ShowController controller)
    {
        _editor = editor;
        _controller = controller;
        Title = "Bildschirmpräsentation";
        SystemDecorations = SystemDecorations.None;
        CanResize = false;
        ShowInTaskbar = false;
        Width = 1280;
        Height = 720;
        Surface = new ShowSurface { Controller = controller };

        // The chrome over the show is dark in either theme (the show is black around the slide), still Mat's tokens.
        _barPosition = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0), FontSize = 13 };
        _barPosition.Use(TextBlock.ForegroundProperty, "Mat.Text");
        var previous = BarButton("chevron-left", "Zurück", controller.Previous);
        var next = BarButton("chevron-right", "Weiter", controller.Next);
        AllSlidesButton = BarButton("grid", "Alle Folien", ShowNavigator);
        var black = BarButton("moon", "Bildschirm schwarz (B)", () => controller.ToggleBlank(BlankScreen.Black));
        MoreButton = BarButton("more-horizontal", "Weitere Optionen", OpenMoreMenu);
        _bar = new Border
        {
            CornerRadius = new CornerRadius(22),
            Padding = new Thickness(6, 4),
            Margin = new Thickness(24),
            BorderThickness = new Thickness(1),
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Bottom,
            Opacity = 0,
            IsHitTestVisible = false,
            Transitions = [new DoubleTransition { Property = OpacityProperty, Duration = TimeSpan.FromMilliseconds(220) }],
            Child = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                Spacing = 2,
                Children = { previous, _barPosition, next, ShowButtons.Divider(20), AllSlidesButton, black, MoreButton },
            },
        }.Use(Border.BackgroundProperty, "Mat.Surface").Use(Border.BorderBrushProperty, "Mat.Border");
        Navigator = new SlideNavigator();
        Navigator.Finished += (_, _) => Surface.Focus();
        Content = new Panel
        {
            Children =
            {
                Surface,
                new ThemeVariantScope { RequestedThemeVariant = ThemeVariant.Dark, Child = new Panel { Children = { _bar, Navigator } } },
            },
        };

        _idle = new DispatcherTimer { Interval = IdleDelay };
        _idle.Tick += (_, _) => OnIdle();
        controller.Ended += (_, _) => Finish();
        controller.Changed += (_, _) => UpdateBar();
        Surface.PointerPressed += OnPointerPressed;
        Surface.PointerWheelChanged += (_, e) => ShowNavigation.HandleWheel(_controller, e);
        PointerMoved += OnPointerMoved;
        UpdateBar();
    }

    /// <summary>How long the mouse rests before the control bar and the cursor go (tests make it short).</summary>
    internal static TimeSpan IdleDelay { get; set; } = TimeSpan.FromSeconds(3);

    /// <summary>The window of the running show of a process (one at a time; tests reach it here).</summary>
    public static SlideShowWindow? Current { get; private set; }

    public ShowSurface Surface { get; }

    public ShowController Controller => _controller;

    public PresenterWindow? PresenterView => _presenter;

    public SlideNavigator Navigator { get; }

    public Button AllSlidesButton { get; }

    public Button MoreButton { get; }

    /// <summary>The control bar at the bottom left.</summary>
    public Border ControlBar => _bar;

    /// <summary>True while the control bar is shown (the mouse moved lately).</summary>
    public bool ControlsShown { get; private set; }

    /// <summary>True while the mouse rests and its cursor is hidden.</summary>
    public bool IsCursorHidden { get; private set; }

    /// <summary>Alt+F5: the presenter view in a window on one screen, the show window hidden until "Anzeige tauschen".</summary>
    public bool IsTestMode { get; private set; }

    /// <summary>The screen the show is on (null: no screen known).</summary>
    public ShowScreen? AudienceScreen => _audienceScreen;

    /// <summary>The screen of the presenter view when it fills one.</summary>
    public ShowScreen? PresenterScreen => _presenterScreen;

    /// <summary>True when "Anzeige tauschen" has something to trade (two screens with the presenter view, or the test mode).</summary>
    public bool CanSwap => IsTestMode || (_presenter is not null && _presenterScreen is not null && _audienceScreen is not null);

    /// <summary>Starts the show from the first slide or the current one; <paramref name="testPresenter"/> = Alt+F5.</summary>
    public static Task StartAsync(OfficeWindow owner, ShowEditor editor, bool fromCurrent, bool testPresenter = false)
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
        var screens = ShowScreens.All(owner);
        var own = ShowScreens.ScreenOf(owner, screens);
        if (testPresenter)
        {
            window.IsTestMode = true;
            window._audienceScreen = own;
            window.OpenPresenter(null, owner);
            return Task.CompletedTask;
        }

        var arrangement = ShowScreens.Arrange(screens, own);
        window._audienceScreen = arrangement.Audience;
        window.Position = arrangement.Audience?.Bounds.Position ?? owner.Position;
        window.ShowFullScreen();
        if (ShowSettings.UsePresenterView && arrangement.Presenter is { } presenterScreen)
        {
            window.OpenPresenter(presenterScreen, null);
        }

        return Task.CompletedTask;
    }

    /// <summary>A show window for a snapshot: not the process's show, the control bar shown for good.</summary>
    internal static SlideShowWindow ForSnapshot(ShowController controller)
    {
        var window = new SlideShowWindow(null, controller) { _pinControls = true };
        window._bar.Transitions = null; // drawn at once, not in the middle of the fade
        window.ShowControls();
        return window;
    }

    private Button BarButton(string icon, string name, Action action)
    {
        var button = ShowButtons.Icon(icon, name, action, 18);
        button.Focusable = false; // the keys stay the show's (Leertaste must not press a button)
        return button;
    }

    private void ShowFullScreen()
    {
        Show();
        WindowState = WindowState.FullScreen;
        Activate();
        Surface.Focus();
        Dispatcher.UIThread.Post(() => Surface.Focus(), DispatcherPriority.Input);
    }

    /// <summary>Shows (or hides) the presenter view: full screen on another screen when there is one, else in a window.</summary>
    public void ShowPresenter(ShowScreen? screen)
    {
        if (_presenter is not null)
        {
            var closing = _presenter;
            _presenter = null;
            _presenterScreen = null;
            closing.Close();
            return;
        }

        OpenPresenter(screen, this);
    }

    private void OpenPresenter(ShowScreen? screen, Window? centerOn)
    {
        var presenter = new PresenterWindow(_controller);
        _presenter = presenter;
        _presenterScreen = screen;
        if (screen is not null)
        {
            presenter.Position = screen.Bounds.Position;
        }
        else if (centerOn is not null)
        {
            presenter.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        }

        presenter.Closed += (_, _) =>
        {
            if (_presenter != presenter)
            {
                return;
            }

            _presenter = null;
            _presenterScreen = null;
            if (IsTestMode)
            {
                // The test mode has no other view: closing it ends the show.
                _controller.End();
            }
        };
        presenter.SwapDisplays = CanSwap ? SwapDisplays : null;
        presenter.Show();
        if (screen is not null)
        {
            presenter.WindowState = WindowState.FullScreen;
        }

        presenter.Activate();
        presenter.Focus();
    }

    /// <summary>
    /// Anzeige tauschen: with two screens the show and the presenter view trade screens; in the test mode (one screen) the show
    /// window and the presenter view trade places on it.
    /// </summary>
    public void SwapDisplays()
    {
        if (IsTestMode)
        {
            if (_presenter is { IsVisible: true } shown)
            {
                if (_audienceScreen is { } screen)
                {
                    Position = screen.Bounds.Position;
                }

                ShowFullScreen();
                shown.Hide();
            }
            else if (_presenter is { } hidden)
            {
                hidden.Show();
                hidden.Activate();
                Hide();
            }

            return;
        }

        if (_presenter is not { } presenter || _presenterScreen is not { } presenterScreen || _audienceScreen is not { } audienceScreen)
        {
            return;
        }

        (_audienceScreen, _presenterScreen) = (presenterScreen, audienceScreen);
        MoveFullScreen(this, presenterScreen);
        MoveFullScreen(presenter, audienceScreen);
        Surface.Focus();
    }

    private static void MoveFullScreen(Window window, ShowScreen screen)
    {
        window.WindowState = WindowState.Normal;
        window.Position = screen.Bounds.Position;
        window.WindowState = WindowState.FullScreen;
    }

    /// <summary>Alle Folien over the show.</summary>
    public void ShowNavigator()
    {
        Navigator.Open(_controller);
        ShowControls();
    }

    /// <summary>The bar's "…": the show's menu.</summary>
    private void OpenMoreMenu()
    {
        var menu = ShowNavigation.Menu(_controller, MenuActions());
        _menuOpen = true;
        menu.Closed += (_, _) =>
        {
            _menuOpen = false;
            RestartIdle();
            Surface.Focus();
        };
        menu.Open(MoreButton);
    }

    private ShowMenuActions MenuActions() => new(
        TogglePresenter: IsTestMode ? null : () => ShowPresenter(_presenter is null ? FreeScreen() : null),
        PresenterShown: _presenter is not null,
        SwapDisplays: CanSwap ? SwapDisplays : null,
        AllSlides: ShowNavigator);

    /// <summary>A screen the show is not on (for the presenter view), else null (it then opens in a window).</summary>
    private ShowScreen? FreeScreen()
    {
        var screens = ShowScreens.All(this);
        return screens.Count > 1 && _audienceScreen is { } audience ? screens.FirstOrDefault(screen => screen != audience) : null;
    }

    /// <summary>The control bar fades in, the cursor shows; both go again after <see cref="IdleDelay"/> without a move.</summary>
    public void ShowControls()
    {
        ControlsShown = true;
        _bar.Opacity = 0.94;
        _bar.IsHitTestVisible = true;
        if (IsCursorHidden)
        {
            IsCursorHidden = false;
            Cursor = Cursor.Default;
        }

        RestartIdle();
    }

    private void RestartIdle()
    {
        _idle.Stop();
        _idle.Interval = IdleDelay;
        if (!_pinControls)
        {
            _idle.Start();
        }
    }

    private void OnIdle()
    {
        if (_bar.IsPointerOver || _menuOpen || Navigator.IsOpen)
        {
            return; // the timer runs on and asks again
        }

        _idle.Stop();
        ControlsShown = false;
        _bar.Opacity = 0;
        _bar.IsHitTestVisible = false;
        IsCursorHidden = true;
        Cursor = new Cursor(StandardCursorType.None);
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        var point = e.GetPosition(this);
        if (_lastPointer is { } last && Math.Abs(last.X - point.X) + Math.Abs(last.Y - point.Y) < 3)
        {
            return; // a window that appears under a resting mouse gets a move too: that is no movement
        }

        var first = _lastPointer is null;
        _lastPointer = point;
        if (!first)
        {
            ShowControls();
        }
    }

    private void UpdateBar()
    {
        var show = _controller.Show;
        _barPosition.Text = $"{(show.Position + 1).ToString(CultureInfo.CurrentCulture)} / {show.Shown.Count.ToString(CultureInfo.CurrentCulture)}";
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
        {
            return;
        }

        if (Navigator.IsOpen)
        {
            if (e.Key == Key.Escape)
            {
                Navigator.Close();
            }

            e.Handled = true;
            return;
        }

        e.Handled = ShowNavigation.HandleKey(_controller, e.Key, e.KeyModifiers);
    }

    private void OnPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(Surface);
        if (point.Properties.IsRightButtonPressed)
        {
            Surface.ContextMenu = ShowNavigation.Menu(_controller, MenuActions());
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
        if (Current == this)
        {
            Current = null;
        }

        _idle.Stop();
        var presenter = _presenter;
        _presenter = null;
        presenter?.Close();
        Close();
        if (_editor is null)
        {
            return;
        }

        if (slideIndex >= 0 && slideIndex < _editor.Session.Presentation.SlideCount)
        {
            _editor.Session.GoToSlide(_editor.Session.Presentation.Slides[slideIndex].Id);
        }

        _editor.FocusDocument();
    }

    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);
        _idle.Stop();
        if (Current == this)
        {
            Current = null;
            _controller.End();
        }
    }
}
