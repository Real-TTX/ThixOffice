using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using MatDesktop;
using SkiaSharp;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;
using ThixShow.Core.Model;
using ThixShow.Editor;

namespace ThixShow.Show;

/// <summary>
/// PowerPoint's Referentenansicht on the presenter's screen: the timer (pause, reset) and the clock at the top, the current slide
/// as the audience sees it, the next slide (or "Nächster Schritt" while the current one still builds), the notes with their font
/// size, and at the bottom Zurück · "Folie X von Y" · Weiter, Alle Folien (a navigator), Bildschirm schwarz and Präsentation beenden.
/// The same keys as the show work here. Chrome in Mat tokens; the slides as the show draws them.
/// </summary>
internal sealed class PresenterWindow : Window
{
    private readonly ShowController _controller;
    private readonly DispatcherTimer _clockTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private double _notesSize = 18;

    public PresenterWindow(ShowController controller)
    {
        _controller = controller;
        Title = "Referentenansicht";
        Width = 1280;
        Height = 760;
        this.Use(BackgroundProperty, "Mat.Bg");

        Current = new ShowSurface { Controller = controller };
        Next = new SlidePreview();
        TimerText = new TextBlock { FontSize = 22, VerticalAlignment = VerticalAlignment.Center };
        ClockText = new TextBlock { FontSize = 22, VerticalAlignment = VerticalAlignment.Center, HorizontalAlignment = HorizontalAlignment.Right };
        PositionText = new TextBlock { FontSize = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0) };
        NextLabel = new TextBlock { Text = "Nächste Folie", FontSize = 14, Margin = new Thickness(0, 0, 0, 6) };
        NextLabel.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        NotesText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = _notesSize };
        TimerText.Use(TextBlock.ForegroundProperty, "Mat.Text");
        ClockText.Use(TextBlock.ForegroundProperty, "Mat.Text");

        var pause = IconButton("pause", "Zeitgeber anhalten oder fortsetzen", controller.ToggleTimerPause);
        var reset = IconButton("refresh", "Zeitgeber zurücksetzen", controller.ResetTimer);
        var top = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(20, 12) };
        Grid.SetColumn(pause, 1);
        Grid.SetColumn(reset, 2);
        Grid.SetColumn(ClockText, 3);
        top.Children.Add(TimerText);
        top.Children.Add(pause);
        top.Children.Add(reset);
        top.Children.Add(ClockText);

        var bigger = IconButton("font-grow", "Notizen vergrößern", () => SetNotesSize(_notesSize + 2));
        var smaller = IconButton("font-shrink", "Notizen verkleinern", () => SetNotesSize(_notesSize - 2));
        var notesTools = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4, Children = { bigger, smaller } };
        var notes = new ScrollViewer { Content = NotesText, Padding = new Thickness(0, 4, 8, 4) };
        var right = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,Auto,*"), Margin = new Thickness(16, 0, 20, 0) };
        Grid.SetRow(Next, 1);
        Grid.SetRow(notesTools, 2);
        Grid.SetRow(notes, 3);
        Next.Height = 220;
        notesTools.Margin = new Thickness(0, 12, 0, 6);
        right.Children.Add(NextLabel);
        right.Children.Add(Next);
        right.Children.Add(notesTools);
        right.Children.Add(notes);

        var middle = new Grid { ColumnDefinitions = new ColumnDefinitions("3*,2*"), Margin = new Thickness(20, 0, 0, 0) };
        Grid.SetColumn(right, 1);
        middle.Children.Add(Current);
        middle.Children.Add(right);

        var previous = IconButton("chevron-left", "Zurück", controller.Previous);
        var next = IconButton("chevron-right", "Weiter", controller.Next);
        AllSlides = new Button { Content = new IconLabel { Icon = "grid", Text = "Alle Folien" } };
        AllSlides.Click += (_, _) => ShowNavigator();
        var black = new Button { Content = new IconLabel { Icon = "moon", Text = "Bildschirm schwarz" } };
        black.Click += (_, _) => controller.ToggleBlank(BlankScreen.Black);
        var end = new Button { Content = "Präsentation beenden" }.Themed("MatButton.Danger");
        end.Click += (_, _) => controller.End();
        var bottom = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Margin = new Thickness(20, 12), Children = { previous, PositionText, next, AllSlides, black } };
        var bottomBar = new DockPanel { Children = { bottom } };
        DockPanel.SetDock(end, Dock.Right);
        end.Margin = new Thickness(20, 12);
        bottomBar.Children.Insert(0, end);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,*,Auto") };
        Grid.SetRow(middle, 1);
        Grid.SetRow(bottomBar, 2);
        layout.Children.Add(top);
        layout.Children.Add(middle);
        layout.Children.Add(bottomBar);
        Content = layout;

        controller.Changed += (_, _) => Refresh();
        _clockTimer.Tick += (_, _) => UpdateTimes();
        _clockTimer.Start();
        Closed += (_, _) => _clockTimer.Stop();
        Current.PointerPressed += (_, e) =>
        {
            if (e.GetCurrentPoint(Current).Properties.IsLeftButtonPressed)
            {
                controller.Next();
            }
        };
        Refresh();
    }

    public ShowSurface Current { get; }

    public SlidePreview Next { get; }

    public TextBlock NextLabel { get; }

    public TextBlock NotesText { get; }

    public TextBlock TimerText { get; }

    public TextBlock ClockText { get; }

    public TextBlock PositionText { get; }

    public Button AllSlides { get; }

    /// <summary>The time the clock shows (tests set it).</summary>
    internal Func<DateTime> Now { get; set; } = () => DateTime.Now;

    private static Button IconButton(string icon, string tooltip, Action action)
    {
        var button = new Button { Content = new MatIcon { Icon = icon, Size = 18 } }.Themed("MatButton.Icon");
        Avalonia.Automation.AutomationProperties.SetName(button, tooltip);
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private void SetNotesSize(double size)
    {
        _notesSize = Math.Clamp(size, 10, 48);
        NotesText.FontSize = _notesSize;
    }

    /// <summary>Follows the show: the position, the next slide or step, the notes, the times.</summary>
    private void Refresh()
    {
        var view = _controller.Show.Presenter(_controller.Now, Now());
        PositionText.Text = $"Folie {view.Position.ToString(CultureInfo.CurrentCulture)} von {view.Count.ToString(CultureInfo.CurrentCulture)}";
        NotesText.Text = view.Notes.Length == 0 ? "Keine Notizen" : view.Notes;
        NotesText.Use(TextBlock.ForegroundProperty, view.Notes.Length == 0 ? "Mat.TextMuted" : "Mat.Text");
        if (view.NextIsBuildStep)
        {
            NextLabel.Text = "Nächster Schritt";
            Next.Page = _controller.Show.Thumbnail(_controller.Show.SlideIndex, 320);
        }
        else if (view.NextSlideIndex is { } nextIndex)
        {
            NextLabel.Text = "Nächste Folie";
            Next.Page = _controller.Show.Thumbnail(nextIndex, 320);
        }
        else
        {
            NextLabel.Text = "Ende der Bildschirmpräsentation";
            Next.Page = null;
        }

        UpdateTimes();
    }

    private void UpdateTimes()
    {
        var elapsed = _controller.Elapsed;
        TimerText.Text = $"{(int)elapsed.TotalHours}:{elapsed.Minutes:00}:{elapsed.Seconds:00}";
        ClockText.Text = Now().ToString("HH:mm", CultureInfo.CurrentCulture);
    }

    /// <summary>Alle Folien: every slide of the show by number and title; choosing one goes there.</summary>
    private void ShowNavigator()
    {
        var menu = new ContextMenu();
        foreach (var index in _controller.Show.Shown)
        {
            var slide = _controller.Presentation.Slides[index];
            var position = _controller.Show.Shown.ToList().IndexOf(index);
            var item = new MenuItem { Header = $"{index + 1}  {SlideTitles.TitleOf(_controller.Presentation, slide)}" };
            item.Click += (_, _) => _controller.GoTo(position);
            menu.Items.Add(item);
        }

        menu.Open(AllSlides);
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (!e.Handled)
        {
            e.Handled = ShowNavigation.HandleKey(_controller, e.Key, e.KeyModifiers);
        }
    }
}

/// <summary>A slide's display list drawn fitted into the control (the presenter's next slide).</summary>
internal sealed class SlidePreview : Control
{
    private PageDisplayList? _page;

    public PageDisplayList? Page
    {
        get => _page;
        set
        {
            _page = value;
            InvalidateVisual();
        }
    }

    public override void Render(DrawingContext context)
    {
        if (_page is not { } page || Bounds.Width <= 0)
        {
            return;
        }

        var scale = Math.Min(Bounds.Width / page.Width, Bounds.Height / page.Height);
        var rect = new Rect(0, 0, page.Width * scale, page.Height * scale);
        context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
        {
            canvas.Save();
            canvas.ClipRect(SKRect.Create((float)rect.Width, (float)rect.Height));
            canvas.DrawColor(ShowPalette.EmptySlide);
            canvas.Scale((float)scale);
            SkiaPageRenderer.Draw(canvas, page, OfficeFonts.Skia);
            canvas.Restore();
        }));
    }
}
