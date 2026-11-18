using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;

namespace ThixOffice.Desktop.Controls;

/// <summary>
/// Icon and text side by side: the content of most buttons ("Einfügen", "Kopieren" ...). <see cref="IsTextVisible"/> false
/// leaves the icon alone (the folded menu); the button's tool tip then names it.
/// </summary>
public sealed class IconLabel : StackPanel
{
    public static readonly StyledProperty<string?> IconProperty = AvaloniaProperty.Register<IconLabel, string?>(nameof(Icon));

    public static readonly StyledProperty<string> TextProperty = AvaloniaProperty.Register<IconLabel, string>(nameof(Text), "");

    public static readonly StyledProperty<bool> IsIconFilledProperty = AvaloniaProperty.Register<IconLabel, bool>(nameof(IsIconFilled));

    public static readonly StyledProperty<bool> IsTextVisibleProperty = AvaloniaProperty.Register<IconLabel, bool>(nameof(IsTextVisible), true);

    private readonly MatIcon _icon = new() { Size = 16 };
    private readonly TextBlock _text = new() { VerticalAlignment = VerticalAlignment.Center };

    static IconLabel()
    {
        IconProperty.Changed.AddClassHandler<IconLabel>((label, _) => label.Refresh());
        TextProperty.Changed.AddClassHandler<IconLabel>((label, _) => label.Refresh());
        IsIconFilledProperty.Changed.AddClassHandler<IconLabel>((label, _) => label.Refresh());
        IsTextVisibleProperty.Changed.AddClassHandler<IconLabel>((label, _) => label.Refresh());
    }

    public IconLabel()
    {
        Orientation = Orientation.Horizontal;
        Spacing = 8;
        Children.Add(_icon);
        Children.Add(_text);
        Refresh();
    }

    /// <summary>An icon of MatDesktop's sprite by name ("plus", "trash" …).</summary>
    public string? Icon
    {
        get => GetValue(IconProperty);
        set => SetValue(IconProperty, value);
    }

    public string Text
    {
        get => GetValue(TextProperty);
        set => SetValue(TextProperty, value);
    }

    public bool IsIconFilled
    {
        get => GetValue(IsIconFilledProperty);
        set => SetValue(IsIconFilledProperty, value);
    }

    public bool IsTextVisible
    {
        get => GetValue(IsTextVisibleProperty);
        set => SetValue(IsTextVisibleProperty, value);
    }

    private void Refresh()
    {
        _icon.Icon = Icon;
        _icon.IsFilled = IsIconFilled;
        _icon.IsVisible = Icon is not null;
        _text.Text = Text;
        _text.IsVisible = IsTextVisible && Text.Length > 0;
    }
}
