using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixShow.Core.Model;

namespace ThixShow.Show;

/// <summary>
/// PowerPoint's Referentenansicht in ThixShow's look (Mat tokens, light or dark like the app): a header with the elapsed time (anhalten,
/// fortsetzen, zurücksetzen), "Folie X von Y" and the clock, a slim progress line in the accent; the current slide as a card with
/// its own aspect ratio; at the right a card with the next slide (or "Nächster Schritt" while the slide still builds, or the end)
/// and a card with the notes (their size remembered); at the bottom Zurück · Weiter, Alle Folien (a grid of the slides), Bildschirm
/// schwarz / weiß, Anzeige tauschen and – apart – Präsentation beenden. The same keys as the show work here.
/// </summary>
internal sealed class PresenterWindow : Window
{
    private readonly ShowController _controller;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly Button _pause;
    private readonly Button _black;
    private readonly Button _white;
    private readonly TextBlock _notesSizeText;
    private readonly ProgressBar _progress;
    private readonly Border _blankBadge;
    private readonly TextBlock _blankText;
    private Action? _swapDisplays;

    public PresenterWindow(ShowController controller, Func<DateTime>? now = null)
    {
        _controller = controller;
        Now = now ?? (() => DateTime.Now);
        Title = "Referentenansicht";
        Width = 1280;
        Height = 760;
        MinWidth = 820;
        MinHeight = 520;
        this.Use(BackgroundProperty, "Mat.Bg");

        // ----- header: elapsed time · position · clock, the progress line under it
        TimerText = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        TimerText.Use(TextBlock.ForegroundProperty, "Mat.Text");
        _pause = ShowButtons.Icon("pause", "Zeitgeber anhalten", TogglePause);
        var reset = ShowButtons.Icon("refresh", "Zeitgeber zurücksetzen", controller.ResetTimer);
        var timer = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 4,
            Children = { Caption("Verstrichen", TimerText), _pause, reset },
        };

        PositionText = new TextBlock { FontSize = 15, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        PositionText.Use(TextBlock.ForegroundProperty, "Mat.Text");
        _blankText = new TextBlock();
        _blankBadge = new Border { Child = _blankText, IsVisible = false, Margin = new Thickness(10, 0, 0, 0) }.Themed("MatBadge");
        CurrentTitle = new TextBlock { HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 520, FontSize = 12 };
        CurrentTitle.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var position = new StackPanel
        {
            VerticalAlignment = VerticalAlignment.Center,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, HorizontalAlignment = HorizontalAlignment.Center, Children = { PositionText, _blankBadge } },
                CurrentTitle,
            },
        };

        ClockText = new TextBlock { FontSize = 22, FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        ClockText.Use(TextBlock.ForegroundProperty, "Mat.Text");
        var clock = Caption("Uhrzeit", ClockText);
        clock.HorizontalAlignment = HorizontalAlignment.Right;

        var headerGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,*") };
        Grid.SetColumn(position, 1);
        Grid.SetColumn(clock, 2);
        headerGrid.Children.Add(timer);
        headerGrid.Children.Add(position);
        headerGrid.Children.Add(clock);
        var header = new Border { Padding = new Thickness(20, 10), BorderThickness = new Thickness(0, 0, 0, 1), Child = headerGrid }
            .Use(Border.BackgroundProperty, "Mat.Surface").Use(Border.BorderBrushProperty, "Mat.Border");
        _progress = new ProgressBar { Minimum = 0, Maximum = 1 }.Themed("MatProgress");
        _progress.Height = 3;
        _progress.MinHeight = 3;
        _progress.CornerRadius = new CornerRadius(0);
        _progress.Use(ProgressBar.BackgroundProperty, "Mat.Bg");

        // ----- the current slide
        Current = new ShowSurface { Controller = controller, IsFramed = true, Cursor = new Cursor(StandardCursorType.Hand) };

        // ----- the next slide
        NextLabel = new TextBlock { FontWeight = FontWeight.SemiBold };
        NextLabel.Use(TextBlock.ForegroundProperty, "Mat.Text");
        Next = new SlidePreview { Aspect = SlideAspect(controller.Presentation), MaxHeight = 230 };
        NextHint = new TextBlock { TextWrapping = TextWrapping.Wrap, IsVisible = false };
        NextHint.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var nextCard = new Border
        {
            Child = new StackPanel { Spacing = 10, Children = { NextLabel, Next, NextHint } },
        }.Themed("MatCard");

        // ----- the notes
        NotesText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = ShowSettings.NotesSize, LineHeight = double.NaN };
        _notesSizeText = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 4, 0) };
        _notesSizeText.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var smaller = ShowButtons.Icon("font-shrink", "Notizen verkleinern", () => SetNotesSize(NotesText.FontSize - 2));
        var bigger = ShowButtons.Icon("font-grow", "Notizen vergrößern", () => SetNotesSize(NotesText.FontSize + 2));
        var notesTitle = new TextBlock { Text = "Notizen", FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        notesTitle.Use(TextBlock.ForegroundProperty, "Mat.Text");
        var notesTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 2, Children = { _notesSizeText, smaller, bigger } };
        DockPanel.SetDock(notesTools, Dock.Right);
        var notesHeader = new Border
        {
            Padding = new Thickness(16, 6, 8, 6),
            BorderThickness = new Thickness(0, 0, 0, 1),
            Child = new DockPanel { Children = { notesTools, notesTitle } },
        }.Use(Border.BorderBrushProperty, "Mat.Border");
        NotesScroll = new ScrollViewer { Content = NotesText, Padding = new Thickness(16, 12, 16, 16) };
        var notesLayout = new DockPanel();
        DockPanel.SetDock(notesHeader, Dock.Top);
        notesLayout.Children.Add(notesHeader);
        notesLayout.Children.Add(NotesScroll);
        var notesCard = new Border { Padding = new Thickness(0), Child = notesLayout }.Themed("MatCard");

        var side = new Grid { RowDefinitions = new RowDefinitions("Auto,16,*") };
        Grid.SetRow(notesCard, 2);
        side.Children.Add(nextCard);
        side.Children.Add(notesCard);

        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("*,20,380"), Margin = new Thickness(10, 10, 24, 16) };
        Grid.SetColumn(side, 2);
        side.Margin = new Thickness(0, 14, 0, 0);
        content.Children.Add(Current);
        content.Children.Add(side);

        Navigator = new SlideNavigator();
        Navigator.Finished += (_, _) => Focus();
        var middle = new Panel { Children = { content, Navigator } };

        // ----- the toolbar
        var previous = ShowButtons.Labeled("chevron-left", "Zurück", controller.Previous, "Zurück (←)");
        var next = ShowButtons.Labeled("chevron-right", "Weiter", controller.Next, "Weiter (→, Leertaste)");
        AllSlides = ShowButtons.Labeled("grid", "Alle Folien", ShowNavigator, "Alle Folien – zu einer Folie springen");
        _black = ShowButtons.Labeled("moon", "Bildschirm schwarz", () => controller.ToggleBlank(BlankScreen.Black), "Bildschirm schwarz (B)");
        _white = ShowButtons.Labeled("sun", "Bildschirm weiß", () => controller.ToggleBlank(BlankScreen.White), "Bildschirm weiß (W)");
        SwapButton = ShowButtons.Labeled("monitor", "Anzeige tauschen", () => _swapDisplays?.Invoke(), "Anzeige tauschen – Präsentation und Referentenansicht wechseln den Bildschirm");
        SwapButton.IsVisible = false;
        var end = ShowButtons.Labeled("x", "Präsentation beenden", controller.End, "Präsentation beenden (Esc)").Themed("MatButton.Danger");
        DockPanel.SetDock(end, Dock.Right);
        var tools = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 8,
            Children = { previous, next, ShowButtons.Divider(), AllSlides, _black, _white, SwapButton },
        };
        var toolbar = new Border
        {
            Padding = new Thickness(16, 10),
            BorderThickness = new Thickness(0, 1, 0, 0),
            Child = new DockPanel { Children = { end, tools } },
        }.Use(Border.BackgroundProperty, "Mat.Surface").Use(Border.BorderBrushProperty, "Mat.Border");
        end.Margin = new Thickness(24, 0, 0, 0);

        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        DockPanel.SetDock(_progress, Dock.Top);
        DockPanel.SetDock(toolbar, Dock.Bottom);
        layout.Children.Add(header);
        layout.Children.Add(_progress);
        layout.Children.Add(toolbar);
        layout.Children.Add(middle);
        Content = layout;

        controller.Changed += OnChanged;
        _clockTimer.Tick += (_, _) => UpdateTimes();
        _clockTimer.Start();
        Closed += (_, _) =>
        {
            _clockTimer.Stop();
            controller.Changed -= OnChanged;
        };
        Current.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(Current).Properties.IsLeftButtonPressed)
            {
                e.Handled = true;
                controller.Next();
            }
        };
        SetNotesSize(ShowSettings.NotesSize);
        Refresh();
    }

    public ShowSurface Current { get; }

    public TextBlock CurrentTitle { get; }

    public SlidePreview Next { get; }

    public TextBlock NextLabel { get; }

    /// <summary>Under the next slide: how many steps this slide still builds, or what Weiter does at the end.</summary>
    public TextBlock NextHint { get; }

    public TextBlock NotesText { get; }

    public ScrollViewer NotesScroll { get; }

    public TextBlock TimerText { get; }

    public TextBlock ClockText { get; }

    public TextBlock PositionText { get; }

    public Button AllSlides { get; }

    /// <summary>Anzeige tauschen: shown while the show window can trade places with this one.</summary>
    public Button SwapButton { get; }

    public SlideNavigator Navigator { get; }

    /// <summary>The share of the show that is behind (the progress line).</summary>
    public double Progress => _progress.Value;

    /// <summary>What "Anzeige tauschen" does (the show window sets it when there is a place to trade); null hides the button.</summary>
    public Action? SwapDisplays
    {
        get => _swapDisplays;
        set
        {
            _swapDisplays = value;
            SwapButton.IsVisible = value is not null;
        }
    }

    /// <summary>The time the clock shows (snapshots and tests give their own).</summary>
    internal Func<DateTime> Now { get; }

    private static StackPanel Caption(string caption, TextBlock value)
    {
        var label = new TextBlock { Text = caption, FontSize = 11 };
        label.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        return new StackPanel { VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 8, 0), Children = { label, value } };
    }

    private static double SlideAspect(Presentation presentation) =>
        presentation.SlideSize.Height > 0 ? (double)presentation.SlideSize.Width / presentation.SlideSize.Height : 16.0 / 9;

    private void TogglePause()
    {
        _controller.ToggleTimerPause();
        UpdateTimes();
    }

    private void SetNotesSize(double size)
    {
        var clamped = Math.Clamp(size, ShowSettings.MinNotesSize, ShowSettings.MaxNotesSize);
        NotesText.FontSize = clamped;
        ShowSettings.NotesSize = clamped;
        _notesSizeText.Text = $"{clamped.ToString(CultureInfo.CurrentCulture)} pt";
    }

    private void OnChanged(object? sender, EventArgs e) => Refresh();

    /// <summary>Follows the show: the position, the next slide or step, the notes, the screen, the times.</summary>
    private void Refresh()
    {
        var show = _controller.Show;
        var view = show.Presenter(_controller.Now, Now());
        PositionText.Text = $"Folie {view.Position.ToString(CultureInfo.CurrentCulture)} von {view.Count.ToString(CultureInfo.CurrentCulture)}";
        _progress.Value = view.Count == 0 ? 0 : (double)view.Position / view.Count;
        CurrentTitle.Text = SlideTitles.TitleOf(_controller.Presentation, show.CurrentSlide);
        NotesText.Text = view.Notes.Length == 0 ? "Keine Notizen zu dieser Folie" : view.Notes;
        NotesText.Use(TextBlock.ForegroundProperty, view.Notes.Length == 0 ? "Mat.TextMuted" : "Mat.Text");
        if (view.NextIsBuildStep)
        {
            NextLabel.Text = "Nächster Schritt";
            Next.Page = show.Thumbnail(show.SlideIndex, 360);
            Next.IsVisible = true;
            NextHint.Text = view.RemainingClicks == 1
                ? "Diese Folie baut noch einen Schritt auf."
                : $"Diese Folie baut noch {view.RemainingClicks.ToString(CultureInfo.CurrentCulture)} Schritte auf.";
            NextHint.IsVisible = true;
        }
        else if (view.NextSlideIndex is { } nextIndex)
        {
            NextLabel.Text = "Nächste Folie";
            Next.Page = show.Thumbnail(nextIndex, 360);
            Next.IsVisible = true;
            NextHint.IsVisible = false;
        }
        else
        {
            NextLabel.Text = "Ende der Präsentation";
            Next.Page = null;
            Next.IsVisible = false;
            NextHint.Text = "Mit „Weiter“ endet die Bildschirmpräsentation.";
            NextHint.IsVisible = true;
        }

        _blankBadge.IsVisible = _controller.Blank != BlankScreen.None;
        _blankText.Text = _controller.Blank == BlankScreen.White ? "Bildschirm weiß" : "Bildschirm schwarz";
        _black.Theme = _controller.Blank == BlankScreen.Black ? Ui.Theme("MatButton.Primary") : null;
        _white.Theme = _controller.Blank == BlankScreen.White ? Ui.Theme("MatButton.Primary") : null;
        UpdateTimes();
    }

    private void UpdateTimes()
    {
        var elapsed = _controller.Elapsed;
        TimerText.Text = $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        TimerText.Use(TextBlock.ForegroundProperty, _controller.IsTimerPaused ? "Mat.TextMuted" : "Mat.Text");
        ClockText.Text = Now().ToString("HH:mm", CultureInfo.CurrentCulture);
        var paused = _controller.IsTimerPaused;
        var name = paused ? "Zeitgeber fortsetzen" : "Zeitgeber anhalten";
        ((MatIcon)_pause.Content!).Icon = paused ? "play" : "pause";
        Avalonia.Automation.AutomationProperties.SetName(_pause, name);
        ToolTip.SetTip(_pause, name);
    }

    /// <summary>Alle Folien: the grid of the slides over the view.</summary>
    public void ShowNavigator() => Navigator.Open(_controller);

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
}
