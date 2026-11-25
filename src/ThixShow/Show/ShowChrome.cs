using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using SkiaSharp;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;
using ThixShow.Core.Model;
using ThixShow.Editor;

namespace ThixShow.Show;

/// <summary>The buttons of the show's chrome (presenter view, control bar, navigator).</summary>
internal static class ShowButtons
{
    /// <summary>A square button with only an icon: its name and tooltip say what it does.</summary>
    public static Button Icon(string icon, string name, Action action, double size = 18)
    {
        var button = new Button { Content = new MatIcon { Icon = icon, Size = size } }.Themed("MatButton.Icon");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A button with an icon and a label.</summary>
    public static Button Labeled(string icon, string text, Action action, string? tooltip = null)
    {
        var button = new Button { Content = new IconLabel { Icon = icon, Text = text } };
        AutomationProperties.SetName(button, text);
        if (tooltip is not null)
        {
            ToolTip.SetTip(button, tooltip);
        }

        button.Click += (_, _) => action();
        return button;
    }

    /// <summary>A thin upright line between groups of buttons.</summary>
    public static Border Divider(double height = 24) =>
        new Border { Width = 1, Height = height, Margin = new Thickness(6, 0), VerticalAlignment = VerticalAlignment.Center }.Use(Border.BackgroundProperty, "Mat.Border");
}

/// <summary>
/// A slide's display list drawn fitted into the control with rounded corners and a thin line around it (the presenter's next slide,
/// the tiles of "Alle Folien"). It asks for the slide's own aspect ratio: as tall as its width needs.
/// </summary>
internal sealed class SlidePreview : Control
{
    private PageDisplayList? _page;

    /// <summary>Width ÷ height while no page is set.</summary>
    public double Aspect { get; set; } = 16.0 / 9;

    public double Radius { get; set; } = 6;

    public PageDisplayList? Page
    {
        get => _page;
        set
        {
            _page = value;
            InvalidateMeasure();
            InvalidateVisual();
        }
    }

    private double ActualAspect => _page is { Width: > 0, Height: > 0 } page ? page.Width / page.Height : Aspect;

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = double.IsFinite(availableSize.Width) ? availableSize.Width : 240;
        var height = width / ActualAspect;
        if (double.IsFinite(availableSize.Height) && height > availableSize.Height)
        {
            height = availableSize.Height;
            width = height * ActualAspect;
        }

        return new Size(width, height);
    }

    public override void Render(DrawingContext context)
    {
        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            return;
        }

        var width = Math.Min(Bounds.Width, Bounds.Height * ActualAspect);
        var height = width / ActualAspect;
        var rect = new Rect((Bounds.Width - width) / 2, (Bounds.Height - height) / 2, width, height);
        var radius = Radius;
        if (_page is { } page)
        {
            context.Custom(new SkiaDrawOperation(new Rect(Bounds.Size), canvas =>
            {
                var clip = SKRect.Create((float)rect.X, (float)rect.Y, (float)rect.Width, (float)rect.Height);
                canvas.Save();
                canvas.ClipRoundRect(new SKRoundRect(clip, (float)radius), antialias: true);
                canvas.DrawColor(ShowPalette.EmptySlide);
                canvas.Translate(clip.Left, clip.Top);
                canvas.Scale((float)(rect.Width / page.Width));
                SkiaPageRenderer.Draw(canvas, page, OfficeFonts.Skia);
                canvas.Restore();
            }));
        }
        else
        {
            context.DrawRectangle(Ui.Brush("Mat.Surface2"), null, new RoundedRect(rect, radius));
        }

        context.DrawRectangle(null, new Pen(Ui.Brush("Mat.Border"), 1), new RoundedRect(rect.Deflate(0.5), radius));
    }
}

/// <summary>
/// "Alle Folien": every slide of the running show as a picture with its number and title over the view; a click (or Eingabe) goes
/// there, Esc or ✕ closes it. The arrow keys move between the tiles. One code for the presenter view and the show window.
/// </summary>
internal sealed class SlideNavigator : Border
{
    private const double TileWidth = 224;

    private readonly WrapPanel _tiles = new() { Orientation = Orientation.Horizontal };
    private readonly ScrollViewer _scroll;
    private ShowController? _controller;

    public SlideNavigator()
    {
        IsVisible = false;
        this.Use(BackgroundProperty, "Mat.Bg");
        var title = new TextBlock { Text = "Alle Folien", VerticalAlignment = VerticalAlignment.Center }.Themed("MatText.SectionTitle");
        var close = ShowButtons.Icon("x", "Alle Folien schließen (Esc)", Close);
        DockPanel.SetDock(close, Dock.Right);
        var header = new DockPanel { Margin = new Thickness(28, 18, 20, 8), Children = { close, title } };
        _scroll = new ScrollViewer { Content = _tiles, Padding = new Thickness(20, 0, 20, 20), HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled };
        var layout = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        layout.Children.Add(header);
        layout.Children.Add(_scroll);
        Child = layout;
        KeyDown += OnKeyDown;
    }

    /// <summary>A slide was chosen (its position among the shown slides) or the navigator closed (null).</summary>
    public event EventHandler<int?>? Finished;

    public bool IsOpen => IsVisible;

    /// <summary>The tiles in order (tests click them).</summary>
    public IReadOnlyList<Button> Tiles => [.. _tiles.Children.OfType<Button>()];

    /// <summary>Opens over the view with a tile per shown slide; the current one is marked and has the keyboard.</summary>
    public void Open(ShowController controller)
    {
        _controller = controller;
        _tiles.Children.Clear();
        var shown = controller.Show.Shown;
        var current = controller.Show.Position;
        for (var position = 0; position < shown.Count; position++)
        {
            _tiles.Children.Add(Tile(controller, shown[position], position, position == current));
        }

        IsVisible = true;
        if (current >= 0 && current < _tiles.Children.Count && _tiles.Children[current] is Button tile)
        {
            Avalonia.Threading.Dispatcher.UIThread.Post(() =>
            {
                tile.Focus();
                tile.BringIntoView();
            });
        }
    }

    public void Close() => Finish(null);

    private void Finish(int? position)
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        _tiles.Children.Clear();
        if (position is { } chosen)
        {
            _controller?.GoTo(chosen);
        }

        Finished?.Invoke(this, position);
    }

    private Button Tile(ShowController controller, int slideIndex, int position, bool isCurrent)
    {
        var slide = controller.Presentation.Slides[slideIndex];
        var preview = new SlidePreview { Page = controller.Show.Thumbnail(slideIndex, TileWidth), Width = TileWidth };
        var frame = new Border
        {
            BorderThickness = new Thickness(isCurrent ? 2 : 0),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(isCurrent ? 2 : 4),
            Child = preview,
        }.Use(Border.BorderBrushProperty, "Mat.Accent");
        var number = new TextBlock { Text = (slideIndex + 1).ToString(CultureInfo.CurrentCulture), FontWeight = FontWeight.SemiBold, VerticalAlignment = VerticalAlignment.Center };
        number.Use(TextBlock.ForegroundProperty, isCurrent ? "Mat.AccentText" : "Mat.TextMuted");
        var title = new TextBlock { Text = SlideTitles.TitleOf(controller.Presentation, slide), TextTrimming = TextTrimming.CharacterEllipsis, VerticalAlignment = VerticalAlignment.Center };
        title.Use(TextBlock.ForegroundProperty, "Mat.Text");
        var caption = new DockPanel { Margin = new Thickness(4, 6, 4, 0), MaxWidth = TileWidth + 4, Children = { number, title } };
        DockPanel.SetDock(number, Dock.Left);
        number.Margin = new Thickness(0, 0, 8, 0);
        var tile = new Button
        {
            Content = new StackPanel { Children = { frame, caption } },
            Padding = new Thickness(8),
            Margin = new Thickness(4),
            HorizontalContentAlignment = HorizontalAlignment.Left,
        }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(tile, $"Folie {(slideIndex + 1).ToString(CultureInfo.CurrentCulture)}: {title.Text}");
        tile.Click += (_, _) => Finish(position);
        return tile;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        var tiles = Tiles;
        if (tiles.Count == 0)
        {
            return;
        }

        var focused = tiles.ToList().FindIndex(tile => tile.IsFocused);
        var columns = Math.Max(1, (int)(_tiles.Bounds.Width / Math.Max(1, tiles[0].Bounds.Width + 8)));
        var step = e.Key switch
        {
            Key.Left => -1,
            Key.Right => 1,
            Key.Up => -columns,
            Key.Down => columns,
            _ => 0,
        };
        if (step == 0)
        {
            return;
        }

        var target = Math.Clamp((focused < 0 ? 0 : focused) + step, 0, tiles.Count - 1);
        tiles[target].Focus(NavigationMethod.Directional);
        tiles[target].BringIntoView();
        e.Handled = true;
    }
}
