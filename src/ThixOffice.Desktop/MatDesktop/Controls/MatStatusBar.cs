// MatDesktop 0.3.1 – a copy from MatDesign, do not edit here
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Collections;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;

namespace MatDesktop;

/// <summary>
/// The status bar of a document program, under the document: the program's items at the left (<c>Seite 2 von 5</c>, <c>1.204 Wörter</c>; in a sheet the sum and the count of the selection)
/// and at the right, then the zoom: minus, a slider, plus and the percent, which asks for a zoom level (<see cref="ZoomLevelRequested"/>). The slider is 10 % to 100 % on its left half and
/// 100 % to 500 % on its right half, like an office program's; minus and plus go to the next step of 10 %.
/// </summary>
public sealed class MatStatusBar : Border
{
    /// <summary>The height of the bar.</summary>
    public const double DefaultHeight = 28;

    /// <summary>The zoom in percent.</summary>
    public static readonly StyledProperty<double> ZoomProperty = AvaloniaProperty.Register<MatStatusBar, double>(nameof(Zoom), 100, coerce: (o, v) => ((MatStatusBar)o).Clamp(v));

    /// <summary>Whether the zoom is shown.</summary>
    public static readonly StyledProperty<bool> ShowZoomProperty = AvaloniaProperty.Register<MatStatusBar, bool>(nameof(ShowZoom), true);

    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, Spacing = 4, VerticalAlignment = VerticalAlignment.Center };
    private readonly StackPanel _zoom = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(8, 0, 0, 0) };
    private readonly Button _zoomOut = Small("minus");
    private readonly Button _zoomIn = Small("plus");
    private readonly Slider _slider = new() { Width = 110, Minimum = 0, Maximum = 100, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(2, 0) };
    private readonly Button _percent = new Button { MinWidth = 48, HorizontalContentAlignment = HorizontalAlignment.Right }.Themed("MatButton.Status");
    private bool _syncing;

    static MatStatusBar()
    {
        ZoomProperty.Changed.AddClassHandler<MatStatusBar>((bar, e) => bar.OnZoomChanged(e));
        ShowZoomProperty.Changed.AddClassHandler<MatStatusBar>((bar, _) => bar._zoom.IsVisible = bar.ShowZoom);
    }

    /// <summary>An empty status bar with the zoom at 100 %.</summary>
    public MatStatusBar()
    {
        Height = DefaultHeight;
        BorderThickness = new Thickness(0, 1, 0, 0);
        Padding = new Thickness(10, 0, 8, 0);
        this.Use(BackgroundProperty, "Mat.Surface");
        this.Use(BorderBrushProperty, "Mat.Border");
        AutomationProperties.SetName(this, MatText.Get("status.name"));
        TextElement.SetFontSize(this, MatTokens.Type.FsXs);

        // the slider of the bar is smaller than a slider of a form: a track of 4 and a thumb of 12
        _slider.Resources["SliderHorizontalThumbWidth"] = 12d;
        _slider.Resources["SliderHorizontalThumbHeight"] = 12d;
        _slider.Resources["SliderTrackThemeHeight"] = 4d;
        _slider.Resources["SliderHorizontalHeight"] = 20d;
        _slider.Resources["SliderPreContentMargin"] = new GridLength(0);
        _slider.Resources["SliderPostContentMargin"] = new GridLength(0);
        _slider.Use(Slider.ForegroundProperty, "Mat.Accent");

        Describe(_zoomOut, MatText.Get("status.zoomOut"));
        Describe(_zoomIn, MatText.Get("status.zoomIn"));
        Describe(_percent, MatText.Get("status.zoomLevel"));
        AutomationProperties.SetName(_slider, MatText.Get("status.zoom"));
        _zoomOut.Click += (_, _) => Zoom = Math.Ceiling(Zoom / ZoomStep - 1) * ZoomStep;
        _zoomIn.Click += (_, _) => Zoom = Math.Floor(Zoom / ZoomStep + 1) * ZoomStep;
        _percent.Click += (_, _) => ZoomLevelRequested?.Invoke(this, EventArgs.Empty);
        _slider.ValueChanged += (_, e) =>
        {
            if (!_syncing)
            {
                Zoom = Math.Round(FromSlider(e.NewValue));
            }
        };
        _zoom.Children.Add(_zoomOut);
        _zoom.Children.Add(_slider);
        _zoom.Children.Add(_zoomIn);
        _zoom.Children.Add(_percent);

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto,Auto") };
        Grid.SetColumn(_right, 1);
        Grid.SetColumn(_zoom, 2);
        grid.Children.Add(_left);
        grid.Children.Add(_right);
        grid.Children.Add(_zoom);
        Child = grid;

        Left.CollectionChanged += (_, _) => Fill(_left, Left);
        Right.CollectionChanged += (_, _) => Fill(_right, Right);
        ShowZoomValue();
    }

    /// <summary>The zoom changed (by the buttons, the slider or the program); the value is the new zoom in percent.</summary>
    public event EventHandler<double>? ZoomChanged;

    /// <summary>The percent was chosen: show the dialog of the zoom.</summary>
    public event EventHandler? ZoomLevelRequested;

    /// <summary>The items at the left, in their order.</summary>
    public AvaloniaList<Control> Left { get; } = [];

    /// <summary>The items at the right, before the zoom.</summary>
    public AvaloniaList<Control> Right { get; } = [];

    /// <summary>The zoom in percent (between <see cref="MinZoom"/> and <see cref="MaxZoom"/>).</summary>
    public double Zoom
    {
        get => GetValue(ZoomProperty);
        set => SetValue(ZoomProperty, value);
    }

    /// <summary>Whether the zoom is shown.</summary>
    public bool ShowZoom
    {
        get => GetValue(ShowZoomProperty);
        set => SetValue(ShowZoomProperty, value);
    }

    /// <summary>The smallest zoom in percent.</summary>
    public double MinZoom { get; set; } = 10;

    /// <summary>The largest zoom in percent.</summary>
    public double MaxZoom { get; set; } = 500;

    /// <summary>The step of minus and plus in percent.</summary>
    public double ZoomStep { get; set; } = 10;

    /// <summary>The button minus (for tests).</summary>
    public Button ZoomOutButton => _zoomOut;

    /// <summary>The button plus (for tests).</summary>
    public Button ZoomInButton => _zoomIn;

    /// <summary>The slider (for tests).</summary>
    public Slider ZoomSlider => _slider;

    /// <summary>The percent (for tests).</summary>
    public Button ZoomButton => _percent;

    /// <summary>A text of the bar (muted, small): <c>Seite 2 von 5</c>.</summary>
    public static TextBlock Text(string text)
    {
        var block = new TextBlock { Text = text, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(6, 0) };
        block.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        return block;
    }

    /// <summary>A text of the bar that acts (<c>1.204 Wörter</c> opens the count): muted, a tint under the pointer.</summary>
    public static Button Action(string text, Action onClick)
    {
        ArgumentNullException.ThrowIfNull(onClick);
        var button = new Button { Content = text }.Themed("MatButton.Status");
        button.Click += (_, _) => onClick();
        return button;
    }

    /// <summary>Where the slider stands for a zoom: 10–100 % on the left half, 100–500 % on the right half.</summary>
    public double ToSlider(double zoom) => zoom <= 100
        ? 50 * (zoom - MinZoom) / (100 - MinZoom)
        : 50 + (50 * (zoom - 100) / (MaxZoom - 100));

    /// <summary>The zoom of a place of the slider.</summary>
    public double FromSlider(double value) => value <= 50
        ? MinZoom + ((100 - MinZoom) * value / 50)
        : 100 + ((MaxZoom - 100) * (value - 50) / 50);

    private double Clamp(double zoom) => double.IsNaN(zoom) ? 100 : Math.Clamp(zoom, MinZoom, MaxZoom);

    private static Button Small(string icon) => new Button { Content = new MatIcon { Icon = icon, Size = 14 }, Width = 22, Height = 22, MinHeight = 22 }.Themed("MatButton.Quick");

    private static void Describe(Control control, string text)
    {
        AutomationProperties.SetName(control, text);
        ToolTip.SetTip(control, text);
    }

    private static void Fill(StackPanel panel, IEnumerable<Control> items)
    {
        panel.Children.Clear();
        foreach (var item in items)
        {
            panel.Children.Add(item);
        }
    }

    private void OnZoomChanged(AvaloniaPropertyChangedEventArgs e)
    {
        ShowZoomValue();
        if (!Equals(e.OldValue, e.NewValue))
        {
            ZoomChanged?.Invoke(this, Zoom);
        }
    }

    private void ShowZoomValue()
    {
        _syncing = true;
        _slider.Value = ToSlider(Zoom);
        _syncing = false;
        _percent.Content = MatText.Format("status.percent", Math.Round(Zoom).ToString(CultureInfo.InvariantCulture));
        _zoomOut.IsEnabled = Zoom > MinZoom;
        _zoomIn.IsEnabled = Zoom < MaxZoom;
    }
}
