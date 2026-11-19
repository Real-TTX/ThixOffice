using Avalonia;
using Avalonia.Automation;
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

/// <summary>
/// A message in a tinted box with an icon (results, hints, errors without a field). Hidden until shown; a closable one has an
/// "x" at its right end.
/// </summary>
public sealed class InfoBar : Border
{
    private readonly MatIcon _icon = new() { Size = 18, VerticalAlignment = VerticalAlignment.Top, Margin = new Thickness(0, 1, 10, 0) };
    private readonly SelectableTextBlock _text = new() { TextWrapping = TextWrapping.Wrap, FontSize = 13 };
    private readonly Button _close;

    public InfoBar()
    {
        IsVisible = false;
        CornerRadius = new CornerRadius(10);
        Padding = new Thickness(14, 10);
        Margin = new Thickness(0, 0, 0, 12);
        BorderThickness = new Thickness(1);
        _close = new Button
        {
            Content = new MatIcon { Icon = "x", Size = 14 },
            Theme = Ui.Theme("MatButton.Ghost"),
            Padding = new Thickness(6),
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(10, -4, -6, 0),
            IsVisible = false,
        };
        AutomationProperties.SetName(_close, "Meldung schließen");
        ToolTip.SetTip(_close, "Schließen");
        _close.Click += (_, _) => Hide();
        var dock = new DockPanel();
        DockPanel.SetDock(_icon, Dock.Left);
        DockPanel.SetDock(_close, Dock.Right);
        dock.Children.Add(_icon);
        dock.Children.Add(_close);
        dock.Children.Add(_text);
        Child = dock;
    }

    /// <summary>The text shown (tests).</summary>
    public string Text => _text.Text ?? "";

    /// <summary>The "x" of a closable message (tests click it).</summary>
    public Button CloseButton => _close;

    public void Show(string message, InfoSeverity severity, bool isClosable = false)
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
        _close.IsVisible = isClosable;
        IsVisible = true;
    }

    public void Hide() => IsVisible = false;
}
