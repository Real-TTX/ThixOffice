using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;

namespace ThixOffice.Desktop.Controls;

public enum InfoSeverity
{
    Info,
    Success,
    Warning,
    Error,
}

/// <summary>A message in a tinted box with an icon (results, hints, errors without a field). Hidden until shown.</summary>
public sealed class InfoBar : Border
{
    private readonly MatIcon _icon = new() { Size = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
    private readonly SelectableTextBlock _text = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };

    public InfoBar()
    {
        IsVisible = false;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(14, 10);
        Margin = new Thickness(0, 0, 0, 12);
        BorderThickness = new Thickness(1);
        var dock = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        dock.Children.Add(_icon);
        dock.Children.Add(_text);
        Child = dock;
    }

    public void Show(string message, InfoSeverity severity)
    {
        var (background, foreground, icon) = severity switch
        {
            InfoSeverity.Success => ("Mat.SuccessBg", "Mat.Success", "check-circle"),
            InfoSeverity.Warning => ("Mat.WarningBg", "Mat.Warning", "alert"),
            InfoSeverity.Error => ("Mat.DangerBg", "Mat.Danger", "x-circle"),
            _ => ("Mat.AccentSoft", "Mat.AccentText", "info"),
        };
        // Bound to the theme (not copied): the bar follows a switch between light and dark.
        this.Use(BackgroundProperty, background);
        this.Use(BorderBrushProperty, "Mat.Border");
        _icon.Use(MatIcon.ForegroundProperty, foreground);
        _icon.Icon = icon;
        _text.Text = message;
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}
