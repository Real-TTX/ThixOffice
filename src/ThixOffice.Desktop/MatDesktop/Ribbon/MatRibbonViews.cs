// MatDesktop 0.3.1 – a copy from MatDesign, do not edit here
using System.ComponentModel;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.VisualTree;

namespace MatDesktop;

/// <summary>
/// What draws one item of a ribbon definition: its control (<see cref="Root"/>), kept in step with the state of the item (visible, enabled, checked, value) by its
/// <see cref="INotifyPropertyChanged"/>, so that a change of state touches only this control. A group gives it the largest size it may take (<see cref="Apply"/>).
/// </summary>
internal abstract class RibbonItemView
{
    private bool _attached;

    protected RibbonItemView(MatRibbonItem item, MatRibbon ribbon)
    {
        Item = item;
        Ribbon = ribbon;
    }

    public MatRibbonItem Item { get; }

    public MatRibbon Ribbon { get; }

    /// <summary>The control in the group.</summary>
    public abstract Control Root { get; }

    /// <summary>The control a key tip stands on and the keyboard reaches.</summary>
    public virtual Control Target => Root;

    /// <summary>True while it takes a column of its own at the full height of the group.</summary>
    public virtual bool IsTall => false;

    /// <summary>The views inside this one (a stack, a row).</summary>
    public virtual IEnumerable<RibbonItemView> Inner => [];

    /// <summary>True when this view gets a key tip (an item that does something).</summary>
    public virtual bool TakesKeyTip => true;

    /// <summary>Draws the item no larger than <paramref name="cap"/>.</summary>
    public virtual void Apply(MatRibbonSize cap)
    {
    }

    /// <summary>What its key tip does: press it, open its menu, put the focus into its field.</summary>
    public abstract void Act();

    public void Attach()
    {
        if (_attached)
        {
            return;
        }

        _attached = true;
        Item.PropertyChanged += OnItemChanged;
        Sync(null);
        foreach (var inner in Inner)
        {
            inner.Attach();
        }
    }

    public void Detach()
    {
        _attached = false;
        Item.PropertyChanged -= OnItemChanged;
        foreach (var inner in Inner)
        {
            inner.Detach();
        }
    }

    /// <summary>Brings the control in step with the item; <paramref name="property"/> is the property that changed, or null for all.</summary>
    protected virtual void Sync(string? property)
    {
        if (property is null or nameof(MatRibbonItem.IsVisible))
        {
            Root.IsVisible = Item.IsVisible;
        }

        if (property is null or nameof(MatRibbonItem.IsEnabled))
        {
            Root.IsEnabled = Item.IsEnabled;
        }

        if (property is null or nameof(MatRibbonItem.Label) or nameof(MatRibbonItem.Shortcut) or nameof(MatRibbonItem.ToolTip) or nameof(MatRibbonItem.ToolTipTitle))
        {
            if (!string.IsNullOrEmpty(Item.Label))
            {
                AutomationProperties.SetName(Target, Item.Label);
                ToolTip.SetTip(Target, Tip(Item));
            }

            if (!string.IsNullOrWhiteSpace(Item.Shortcut))
            {
                AutomationProperties.SetAcceleratorKey(Target, Item.Shortcut.Trim());
            }
        }
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        Sync(e.PropertyName);
        if (e.PropertyName is nameof(MatRibbonItem.IsVisible) or nameof(MatRibbonItem.Label) or nameof(MatRibbonItem.Icon) or nameof(MatRibbonButton.Size) or nameof(MatRibbonButton.MinSize))
        {
            Ribbon.SizesChanged(this);
        }
    }

    /// <summary>The tooltip: the label with the shortcut (<c>Fett (Strg+B)</c>, DESK-07) and, where the item has one, the sentence under it.</summary>
    public static object Tip(MatRibbonItem item)
    {
        if (string.IsNullOrWhiteSpace(item.ToolTip))
        {
            return item.ToolTipTitle;
        }

        return new StackPanel
        {
            MaxWidth = 280,
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = item.ToolTipTitle, FontWeight = FontWeight.SemiBold, TextWrapping = TextWrapping.Wrap },
                new TextBlock { Text = item.ToolTip, TextWrapping = TextWrapping.Wrap },
            },
        };
    }

    /// <summary>The size a button is drawn in under a cap: no larger than it was defined, no smaller than its smallest, never the icon alone without an icon.</summary>
    public static MatRibbonSize Effective(MatRibbonButton button, MatRibbonSize cap)
    {
        var size = (MatRibbonSize)Math.Min((int)button.Size, (int)cap);
        var smallest = (MatRibbonSize)Math.Min((int)button.MinSize, (int)button.Size);
        if (size < smallest)
        {
            size = smallest;
        }

        return size == MatRibbonSize.Small && string.IsNullOrEmpty(button.Icon) ? MatRibbonSize.Medium : size;
    }

    /// <summary>The view of an item.</summary>
    public static RibbonItemView Create(MatRibbonItem item, MatRibbon ribbon) => item switch
    {
        MatRibbonSplitButton split => new SplitView(split, ribbon),
        MatRibbonMenuButton menu => new MenuView(menu, ribbon),
        MatRibbonToggle toggle => new ToggleView(toggle, ribbon),
        MatRibbonButton button => new ButtonView(button, ribbon),
        MatRibbonComboBox combo => new ComboView(combo, ribbon),
        MatRibbonNumber number => new NumberView(number, ribbon),
        MatRibbonGallery gallery => new GalleryView(gallery, ribbon),
        MatRibbonSeparator separator => new SeparatorView(separator, ribbon),
        MatRibbonStack stack => new StackView(stack, ribbon),
        MatRibbonRow row => new RowView(row, ribbon),
        MatRibbonCustom custom => new CustomView(custom, ribbon),
        _ => throw new NotSupportedException($"The ribbon cannot draw a {item.GetType().Name}."),
    };
}

/// <summary>The face of a button: its icon, its label and, for a menu, the chevron, laid out for a size (large: the icon over the label; medium: beside it; small: the icon alone).</summary>
internal sealed class RibbonFace
{
    private readonly StackPanel _panel = new();
    private readonly MatIcon _icon = new();
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly MatIcon _chevron = new() { Icon = "chevron-down", Size = 12 };
    private readonly Border? _colorBar;
    private readonly StackPanel _iconBox = new() { VerticalAlignment = VerticalAlignment.Center };

    public RibbonFace(bool chevron, bool colorBar = false)
    {
        _chevron.IsVisible = chevron;
        _iconBox.Children.Add(_icon);
        if (colorBar)
        {
            _colorBar = new Border { Height = 4, CornerRadius = new CornerRadius(1), Margin = new Thickness(0, -3, 0, 0) };
            _iconBox.Children.Add(_colorBar);
        }

        _panel.Children.Add(_iconBox);
        _panel.Children.Add(_label);
        _panel.Children.Add(_chevron);
        _label.Use(TextBlock.FontSizeProperty, "Mat.FontSizeSm");
    }

    public Control Content => _panel;

    public MatRibbonSize Size { get; private set; } = MatRibbonSize.Medium;

    public void Set(string? icon, string label, bool showChevron)
    {
        _icon.Icon = icon;
        _iconBox.IsVisible = !string.IsNullOrEmpty(icon);
        _label.Text = label;
        _chevron.IsVisible = showChevron;
        Apply(Size);
    }

    /// <summary>The colour under the icon of a colour button: the document's colour (data); none for the automatic one.</summary>
    public void SetColor(string? color)
    {
        if (_colorBar is null)
        {
            return;
        }

        var known = MatRibbonDefinition.IsHexColor(color);
        _colorBar.Background = known ? new SolidColorBrush(Color.Parse(color!)) : null;
        _colorBar.Opacity = known ? 1 : 0;
    }

    public void Apply(MatRibbonSize size)
    {
        Size = size;
        var large = size == MatRibbonSize.Large;
        _panel.Orientation = large ? Orientation.Vertical : Orientation.Horizontal;
        _panel.Spacing = large ? 2 : 6;
        _panel.HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _icon.Size = large ? 26 : 16;
        _icon.HorizontalAlignment = HorizontalAlignment.Center;
        _iconBox.Margin = large ? new Thickness(0, 6, 0, 2) : default;
        _iconBox.HorizontalAlignment = HorizontalAlignment.Center;
        _label.IsVisible = size != MatRibbonSize.Small && !string.IsNullOrEmpty(_label.Text);
        _label.TextWrapping = large ? TextWrapping.Wrap : TextWrapping.NoWrap;
        _label.TextAlignment = large ? TextAlignment.Center : TextAlignment.Left;
        _label.MaxWidth = large ? LargeLabelWidth(_label.Text) : double.PositiveInfinity;
        _label.MaxLines = large ? 2 : 1;
        _label.LineHeight = large ? 15 : double.NaN;
        _label.HorizontalAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _chevron.HorizontalAlignment = HorizontalAlignment.Center;
        _chevron.Margin = large ? new Thickness(0, 1, 0, 0) : new Thickness(size == MatRibbonSize.Small ? -4 : -2, 0, 0, 0);
        _chevron.Size = large ? 12 : 10;
    }

    /// <summary>
    /// The width a large label wraps at: two words go on two lines (<c>Format übertragen</c>), but a word is never broken (<c>Seitenumbruch</c> stays whole): at least 72 px and the
    /// longest word (about 7.5 px a letter at the small size).
    /// </summary>
    private static double LargeLabelWidth(string? text)
    {
        var longest = (text ?? string.Empty).Split(' ', StringSplitOptions.RemoveEmptyEntries).Select(w => w.Length).DefaultIfEmpty(0).Max();
        return Math.Max(72, (longest * 7.5) + 4);
    }

    /// <summary>Sets the shape of a button for a size: large is the full height of a group with the face at its top, medium and small one row.</summary>
    public static void Shape(Button button, MatRibbonSize size, bool alone = true)
    {
        var large = size == MatRibbonSize.Large;
        button.Height = large ? MatRibbon.ContentHeight : MatRibbon.RowHeight;
        button.MinWidth = large ? 46 : MatRibbon.RowHeight;
        button.Padding = large ? new Thickness(4, 0, 4, 4) : new Thickness(size == MatRibbonSize.Small && alone ? 4 : 5, 0);
        button.HorizontalContentAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        button.VerticalContentAlignment = large ? VerticalAlignment.Top : VerticalAlignment.Center;
    }
}

/// <summary>A button: it runs its command.</summary>
internal class ButtonView : RibbonItemView
{
    private readonly RibbonFace _face;
    private MatRibbonSize _size = MatRibbonSize.Large;

    public ButtonView(MatRibbonButton item, MatRibbon ribbon, bool chevron = false)
        : base(item, ribbon)
    {
        Button = MakeButton();
        _face = new RibbonFace(chevron);
        Button.Content = _face.Content;
        Button.Click += (_, _) => OnClick();
    }

    public new MatRibbonButton Item => (MatRibbonButton)base.Item;

    public Button Button { get; }

    public override Control Root => Button;

    public override bool IsTall => _size == MatRibbonSize.Large;

    protected RibbonFace Face => _face;

    public override void Apply(MatRibbonSize cap)
    {
        _size = Effective(Item, cap);
        _face.Apply(_size);
        RibbonFace.Shape(Button, _size);
    }

    public override void Act() => OnClick();

    protected virtual Button MakeButton() => new Button().Themed("MatRibbon.Item");

    protected virtual void OnClick() => Ribbon.Run(() => Ribbon.Definition.Invoke(Item));

    protected override void Sync(string? property)
    {
        base.Sync(property);
        if (property is null or nameof(MatRibbonItem.Icon) or nameof(MatRibbonItem.Label))
        {
            _face.Set(Item.Icon, Item.Label, this is MenuView);
            _face.Apply(_size);
        }
    }
}

/// <summary>A toggle: checked is the tint of the accent; one of a choice stays checked when it is pressed again.</summary>
internal sealed class ToggleView : RibbonItemView
{
    private readonly ToggleButton _button = new ToggleButton().Themed("MatRibbon.Toggle");
    private readonly RibbonFace _face = new(chevron: false);
    private MatRibbonSize _size = MatRibbonSize.Large;
    private bool _syncing;

    public ToggleView(MatRibbonToggle item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _button.Content = _face.Content;
        _button.IsCheckedChanged += (_, _) => OnCheckedChanged();
    }

    public new MatRibbonToggle Item => (MatRibbonToggle)base.Item;

    public override Control Root => _button;

    public override bool IsTall => _size == MatRibbonSize.Large;

    public override void Apply(MatRibbonSize cap)
    {
        _size = Effective(Item, cap);
        _face.Apply(_size);
        _button.Height = _size == MatRibbonSize.Large ? MatRibbon.ContentHeight : MatRibbon.RowHeight;
        _button.MinWidth = _size == MatRibbonSize.Large ? 46 : MatRibbon.RowHeight;
        _button.Padding = _size == MatRibbonSize.Large ? new Thickness(4, 0, 4, 4) : new Thickness(_size == MatRibbonSize.Small ? 4 : 5, 0);
        _button.HorizontalContentAlignment = _size == MatRibbonSize.Large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        _button.VerticalContentAlignment = _size == MatRibbonSize.Large ? VerticalAlignment.Top : VerticalAlignment.Center;
    }

    public override void Act() => Ribbon.Run(Press);

    protected override void Sync(string? property)
    {
        base.Sync(property);
        if (property is null or nameof(MatRibbonItem.Icon) or nameof(MatRibbonItem.Label))
        {
            _face.Set(Item.Icon, Item.Label, false);
            _face.Apply(_size);
        }

        if (property is null or nameof(MatRibbonToggle.IsChecked))
        {
            Show();
        }
    }

    private void Press()
    {
        Ribbon.Definition.Invoke(Item);
        Show();
    }

    private void OnCheckedChanged()
    {
        if (_syncing || _button.IsChecked == Item.IsChecked)
        {
            return;
        }

        Ribbon.Run(Press);
    }

    private void Show()
    {
        _syncing = true;
        _button.IsChecked = Item.IsChecked;
        _syncing = false;
    }
}

/// <summary>A button that opens its menu (built when it opens, so that it shows the state of its items).</summary>
internal class MenuView : ButtonView
{
    public MenuView(MatRibbonMenuButton item, MatRibbon ribbon)
        : base(item, ribbon, chevron: true)
    {
    }

    public new MatRibbonMenuButton Item => (MatRibbonMenuButton)base.Item;

    protected override void OnClick() => OpenMenu(Button);

    protected void OpenMenu(Control at)
    {
        var flyout = RibbonMenus.Menu(Ribbon, Item.Menu);
        flyout.Placement = PlacementMode.BottomEdgeAlignedLeft;
        Ribbon.Open(flyout, at);
    }
}

/// <summary>A button of two parts: the larger part runs the command, the arrow opens the menu (or the palette of a colour button).</summary>
internal sealed class SplitView : MenuView
{
    private readonly Border _frame = new() { BorderThickness = new Thickness(1), Background = Brushes.Transparent };
    private readonly StackPanel _parts = new();
    private readonly Button _arrow = new Button().Themed("MatRibbon.Item");
    private readonly RibbonFace _arrowFace = new(chevron: true);
    private readonly RibbonFace _mainFace;
    private MatRibbonSize _size = MatRibbonSize.Large;

    public SplitView(MatRibbonSplitButton item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _mainFace = new RibbonFace(chevron: false, colorBar: item is MatRibbonColorPicker);
        Button.Content = _mainFace.Content;
        _arrow.Content = _arrowFace.Content;
        _arrow.Click += (_, _) => OpenArrow();
        _frame.Use(Border.CornerRadiusProperty, "Mat.RadiusSm");
        _frame.PointerEntered += (_, _) => _frame.Use(Border.BorderBrushProperty, "Mat.Border");
        _frame.PointerExited += (_, _) => _frame.ClearValue(Border.BorderBrushProperty);
        _parts.Children.Add(Button);
        _parts.Children.Add(_arrow);
        _frame.Child = _parts;
    }

    public new MatRibbonSplitButton Item => (MatRibbonSplitButton)base.Item;

    public override Control Root => _frame;

    public override Control Target => Button;

    public override bool IsTall => _size == MatRibbonSize.Large;

    /// <summary>The arrow part (for tests).</summary>
    public Button Arrow => _arrow;

    public override void Apply(MatRibbonSize cap)
    {
        _size = Effective(Item, cap);
        var large = _size == MatRibbonSize.Large;
        var r = MatTokens.Size.RadiusSm;
        _parts.Orientation = large ? Orientation.Vertical : Orientation.Horizontal;
        _mainFace.Apply(large ? MatRibbonSize.Large : _size);
        _arrowFace.Apply(MatRibbonSize.Medium);
        Button.Padding = large ? new Thickness(4, 0) : new Thickness(_size == MatRibbonSize.Small ? 4 : 5, 0);
        Button.Height = large ? 40 : MatRibbon.RowHeight - 2;
        Button.MinWidth = large ? 46 : MatRibbon.RowHeight - 2;
        Button.HorizontalContentAlignment = large ? HorizontalAlignment.Center : HorizontalAlignment.Left;
        Button.VerticalContentAlignment = large ? VerticalAlignment.Top : VerticalAlignment.Center;
        Button.CornerRadius = large ? new CornerRadius(r, r, 0, 0) : new CornerRadius(r, 0, 0, r);
        _arrow.CornerRadius = large ? new CornerRadius(0, 0, r, r) : new CornerRadius(0, r, r, 0);
        _arrow.Height = large ? MatRibbon.ContentHeight - 42 : MatRibbon.RowHeight - 2;
        _arrow.Padding = large ? new Thickness(4, 0, 4, 4) : new Thickness(2, 0);
        _arrow.HorizontalContentAlignment = HorizontalAlignment.Center;
        _arrow.VerticalContentAlignment = large ? VerticalAlignment.Top : VerticalAlignment.Center;
        _arrowFace.Set(null, large ? Item.Label : string.Empty, showChevron: true);
        _arrowFace.Apply(large ? MatRibbonSize.Large : MatRibbonSize.Small);
        _mainFace.Set(Item.Icon, large ? string.Empty : Item.Label, false);
        _mainFace.Apply(large ? MatRibbonSize.Large : _size);
        if (large)
        {
            Button.HorizontalAlignment = HorizontalAlignment.Stretch;
            _arrow.HorizontalAlignment = HorizontalAlignment.Stretch;
        }
    }

    public override void Act() => OpenArrow();

    protected override void OnClick() => Ribbon.Run(() => Ribbon.Definition.Invoke(Item));

    protected override void Sync(string? property)
    {
        base.Sync(property);
        if (property is null or nameof(MatRibbonItem.Icon) or nameof(MatRibbonItem.Label))
        {
            Apply(_size);
        }

        if (Item is MatRibbonColorPicker picker && property is null or nameof(MatRibbonColorPicker.Color))
        {
            _mainFace.SetColor(picker.Color);
        }

        if (property is null or nameof(MatRibbonItem.Label) or nameof(MatRibbonItem.ToolTipTitle))
        {
            AutomationProperties.SetName(_arrow, Item.Label);
            ToolTip.SetTip(_arrow, Item.Label);
        }
    }

    private void OpenArrow()
    {
        if (Item is MatRibbonColorPicker picker)
        {
            var flyout = new Flyout { Content = RibbonMenus.Palette(Ribbon, picker), Placement = PlacementMode.BottomEdgeAlignedLeft };
            Ribbon.Open(flyout, _frame);
            return;
        }

        OpenMenu(_frame);
    }
}

/// <summary>A combo box one row high; a value is committed when it is chosen, or typed and confirmed with Enter or by leaving the field.</summary>
internal sealed class ComboView : RibbonItemView
{
    private readonly StackPanel _root = new() { Orientation = Orientation.Horizontal, Spacing = 6, Height = MatRibbon.RowHeight };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly ComboBox _combo = new ComboBox().Themed("MatRibbon.ComboBox");
    private bool _syncing;

    public ComboView(MatRibbonComboBox item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _label.Use(TextBlock.FontSizeProperty, "Mat.FontSizeSm");
        _combo.ItemsSource = item.Options;
        _combo.IsEditable = item.IsEditable;
        _combo.Width = item.Width;
        RibbonMenus.Compact(_combo);
        _label.IsVisible = item.ShowLabel;
        _root.Children.Add(_label);
        _root.Children.Add(_combo);
        _combo.SelectionChanged += (_, _) => OnSelectionChanged();
        _combo.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _combo.LostFocus += (_, _) => CommitTyped();
    }

    public new MatRibbonComboBox Item => (MatRibbonComboBox)base.Item;

    public override Control Root => _root;

    public override Control Target => _combo;

    /// <summary>The combo box (for tests).</summary>
    public ComboBox Combo => _combo;

    public override void Act() => _combo.Focus(Avalonia.Input.NavigationMethod.Tab);

    protected override void Sync(string? property)
    {
        base.Sync(property);
        if (property is null or nameof(MatRibbonItem.Label))
        {
            _label.Text = Item.Label;
            _combo.PlaceholderText = Item.ShowLabel ? null : Item.Label;
        }

        if (property is null or nameof(MatRibbonComboBox.Text))
        {
            _syncing = true;
            var text = Item.Text;
            _combo.SelectedItem = text is not null && Item.Options.Contains(text) ? text : null;
            if (Item.IsEditable)
            {
                _combo.Text = text;
            }

            _syncing = false;
        }
    }

    private void OnSelectionChanged()
    {
        if (_syncing || _combo.SelectedItem is not string chosen || chosen == Item.Text)
        {
            return;
        }

        Ribbon.Run(() => Ribbon.Definition.Commit(Item, chosen));
    }

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key == Avalonia.Input.Key.Enter && Item.IsEditable)
        {
            CommitTyped();
            e.Handled = true;
        }
    }

    private void CommitTyped()
    {
        if (!Item.IsEditable || _syncing)
        {
            return;
        }

        var text = _combo.Text?.Trim();
        if (!string.IsNullOrEmpty(text) && text != Item.Text)
        {
            Ribbon.Run(() => Ribbon.Definition.Commit(Item, text));
        }
    }
}

/// <summary>
/// A number field one row high with its label (or icon) at the left: the number with its unit (<c>6 pt</c>, <c>1,5 cm</c>) and two small arrows stacked at its right, the arrow keys and
/// Page up and down step it; a typed number counts after Enter or when the field is left.
/// </summary>
internal sealed class NumberView : RibbonItemView
{
    private readonly StackPanel _root = new() { Orientation = Orientation.Horizontal, Spacing = 6, Height = MatRibbon.RowHeight };
    private readonly MatIcon _icon = new() { Size = 16 };
    private readonly TextBlock _label = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Border _frame = new() { BorderThickness = new Thickness(1), Height = MatRibbon.RowHeight };
    private readonly TextBox _text = new()
    {
        BorderThickness = default,
        Background = Brushes.Transparent,
        MinHeight = 0,
        Padding = new Thickness(6, 0, 2, 0),
        VerticalContentAlignment = VerticalAlignment.Center,
    };

    private readonly Button _up = Arrow("chevron-up");
    private readonly Button _down = Arrow("chevron-down");
    private readonly NumberFormatInfo _format;

    public NumberView(MatRibbonNumber item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        // the decimal comma in German (built here: a program may run with the invariant globalization, which knows no de-DE)
        _format = MatText.IsGerman ? new NumberFormatInfo { NumberDecimalSeparator = ",", NumberGroupSeparator = "." } : CultureInfo.InvariantCulture.NumberFormat;
        _label.Use(TextBlock.FontSizeProperty, "Mat.FontSizeSm");
        _text.Use(TextBox.FontSizeProperty, "Mat.FontSizeSm");
        RibbonMenus.Compact(_text);
        _text.Resources["TextControlBackgroundFocused"] = Brushes.Transparent;
        _text.Resources["TextControlBackgroundPointerOver"] = Brushes.Transparent;
        _frame.Width = item.Width;
        _frame.Use(Border.BackgroundProperty, "Mat.Surface");
        _frame.Use(Border.BorderBrushProperty, "Mat.BorderStrong");
        _frame.Use(Border.CornerRadiusProperty, "Mat.RadiusSm");
        var arrows = new StackPanel { Children = { _up, _down }, VerticalAlignment = VerticalAlignment.Center };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(arrows, 1);
        grid.Children.Add(_text);
        grid.Children.Add(arrows);
        _frame.Child = grid;
        _root.Children.Add(_icon);
        _root.Children.Add(_label);
        _root.Children.Add(_frame);

        _up.Click += (_, _) => Change(Item.Value + Item.Step);
        _down.Click += (_, _) => Change(Item.Value - Item.Step);
        _text.AddHandler(Avalonia.Input.InputElement.KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel);
        _text.GotFocus += (_, _) => _frame.Use(Border.BorderBrushProperty, "Mat.Accent");
        _text.LostFocus += (_, _) =>
        {
            _frame.Use(Border.BorderBrushProperty, "Mat.BorderStrong");
            CommitTyped();
        };
    }

    public new MatRibbonNumber Item => (MatRibbonNumber)base.Item;

    public override Control Root => _root;

    public override Control Target => _text;

    /// <summary>The text shown (for tests).</summary>
    public string? Text => _text.Text;

    public override void Act() => _text.Focus(Avalonia.Input.NavigationMethod.Tab);

    /// <summary>The value as it is shown: its decimals and its unit, <c>1,5 cm</c> in German.</summary>
    public string Format(double value)
    {
        var number = value.ToString("F" + Math.Max(0, Item.Decimals).ToString(CultureInfo.InvariantCulture), _format);
        return string.IsNullOrWhiteSpace(Item.Unit) ? number : $"{number} {Item.Unit.Trim()}";
    }

    protected override void Sync(string? property)
    {
        base.Sync(property);
        if (property is null or nameof(MatRibbonItem.Label) or nameof(MatRibbonItem.Icon))
        {
            _icon.Icon = Item.Icon;
            _icon.IsVisible = Item.ShowLabel && !string.IsNullOrEmpty(Item.Icon);
            _label.Text = Item.Label;
            _label.IsVisible = Item.ShowLabel && string.IsNullOrEmpty(Item.Icon);
            AutomationProperties.SetName(_up, Item.Label);
            AutomationProperties.SetName(_down, Item.Label);
        }

        if (property is null or nameof(MatRibbonItem.IsEnabled))
        {
            _frame.Opacity = Item.IsEnabled ? 1 : 0.5;
        }

        if (property is null or nameof(MatRibbonNumber.Value))
        {
            _text.Text = Format(Item.Value);
            _up.IsEnabled = Item.Value < Item.Maximum;
            _down.IsEnabled = Item.Value > Item.Minimum;
        }
    }

    private static Button Arrow(string icon) => new Button
    {
        Content = new MatIcon { Icon = icon, Size = 10 },
        Width = 18,
        Height = 11,
        MinHeight = 0,
        MinWidth = 0,
        Padding = default,
        CornerRadius = default,
        Focusable = false,
        HorizontalContentAlignment = HorizontalAlignment.Center,
    }.Themed("MatRibbon.Item");

    private void Change(double value) => Ribbon.Run(() => Ribbon.Definition.ChangeValue(Item, value));

    private void OnKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        var step = e.Key switch
        {
            Avalonia.Input.Key.Up => Item.Step,
            Avalonia.Input.Key.Down => -Item.Step,
            Avalonia.Input.Key.PageUp => Item.Step * 10,
            Avalonia.Input.Key.PageDown => -Item.Step * 10,
            _ => 0,
        };
        if (step != 0)
        {
            Ribbon.Definition.ChangeValue(Item, Item.Value + step);
            e.Handled = true;
        }
        else if (e.Key == Avalonia.Input.Key.Enter)
        {
            CommitTyped();
            e.Handled = true;
        }
    }

    /// <summary>A typed number: the digits, a decimal comma or point, the unit left out; what is no number puts the value back.</summary>
    private void CommitTyped()
    {
        var typed = new string([.. (_text.Text ?? string.Empty).Where(c => char.IsDigit(c) || c is ',' or '.' or '-')]).Replace(',', '.');
        if (double.TryParse(typed, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) && Math.Abs(value - Item.Value) > double.Epsilon)
        {
            Ribbon.Definition.ChangeValue(Item, value);
        }

        _text.Text = Format(Item.Value);
    }
}

/// <summary>A gallery: a row of entries in the ribbon with buttons for the rows and the whole gallery; in a narrow window one large button that opens it.</summary>
internal sealed class GalleryView : RibbonItemView
{
    private readonly Panel _root = new();
    private readonly Border _frame = new() { BorderThickness = new Thickness(1), Height = MatRibbon.ContentHeight };
    private readonly StackPanel _strip = new() { Orientation = Orientation.Horizontal, Spacing = 4, Margin = new Thickness(4), ClipToBounds = true };
    private readonly Button _up = Scroll("chevron-up");
    private readonly Button _down = Scroll("chevron-down");
    private readonly Button _more = Scroll("more-horizontal");
    private readonly Button _button = new Button().Themed("MatRibbon.Item");
    private readonly RibbonFace _face = new(chevron: true);
    private readonly List<ToggleButton> _entries = [];
    private int _first;
    private bool _inline = true;

    public GalleryView(MatRibbonGallery item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _frame.Use(Border.BorderBrushProperty, "Mat.Border");
        _frame.Use(Border.BackgroundProperty, "Mat.Surface");
        _frame.Use(Border.CornerRadiusProperty, "Mat.RadiusSm");
        var side = new StackPanel { Width = 20, Children = { _up, _down, _more } };
        side.Use(Panel.BackgroundProperty, "Mat.Surface2");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        Grid.SetColumn(side, 1);
        grid.Children.Add(_strip);
        grid.Children.Add(side);
        _frame.Child = grid;
        _frame.ClipToBounds = true;
        _button.Content = _face.Content;
        _button.Click += (_, _) => OpenAll(_button);
        _up.Click += (_, _) => Page(-1);
        _down.Click += (_, _) => Page(1);
        _more.Click += (_, _) => OpenAll(_frame);
        AutomationProperties.SetName(_up, MatText.Get("ribbon.previous"));
        ToolTip.SetTip(_up, MatText.Get("ribbon.previous"));
        AutomationProperties.SetName(_down, MatText.Get("ribbon.next"));
        ToolTip.SetTip(_down, MatText.Get("ribbon.next"));
        AutomationProperties.SetName(_more, MatText.Get("ribbon.more"));
        ToolTip.SetTip(_more, MatText.Get("ribbon.more"));
        _root.Children.Add(_frame);
        _root.Children.Add(_button);
    }

    public new MatRibbonGallery Item => (MatRibbonGallery)base.Item;

    public override Control Root => _root;

    public override Control Target => _inline ? _frame : _button;

    public override bool IsTall => true;

    /// <summary>True while the row of entries is in the ribbon (false: the one button).</summary>
    public bool IsInline => _inline;

    /// <summary>The entries shown in the ribbon (for tests).</summary>
    public IReadOnlyList<ToggleButton> InlineEntries => [.. _strip.Children.OfType<ToggleButton>()];

    public override void Apply(MatRibbonSize cap)
    {
        _inline = cap == MatRibbonSize.Large;
        _frame.IsVisible = _inline;
        _button.IsVisible = !_inline;
        _face.Apply(MatRibbonSize.Large);
        RibbonFace.Shape(_button, MatRibbonSize.Large);
    }

    public override void Act() => OpenAll(Target);

    protected override void Sync(string? property)
    {
        base.Sync(property);
        if (property is null or nameof(MatRibbonItem.Icon) or nameof(MatRibbonItem.Label))
        {
            _face.Set(Item.Icon, Item.Label, true);
            _face.Apply(MatRibbonSize.Large);
            AutomationProperties.SetName(_frame, Item.Label);
        }

        if (property is null)
        {
            FillStrip();
        }

        if (property is null or nameof(MatRibbonGallery.SelectedId))
        {
            ShowSelected();
        }
    }

    private static Button Scroll(string icon) => new Button
    {
        Content = new MatIcon { Icon = icon, Size = 12 },
        Height = (MatRibbon.ContentHeight - 2) / 3,
        Width = 20,
        Padding = default,
        CornerRadius = default,
        HorizontalContentAlignment = HorizontalAlignment.Center,
        Focusable = false,
    }.Themed("MatRibbon.Item");

    private void Page(int direction)
    {
        var count = Math.Max(1, Item.InlineCount);
        var last = Math.Max(0, (Item.Entries.Count - 1) / count * count);
        _first = Math.Clamp(_first + (direction * count), 0, last);
        FillStrip();
    }

    private void FillStrip()
    {
        foreach (var entry in _strip.Children.OfType<ToggleButton>())
        {
            _entries.Remove(entry);
        }

        _strip.Children.Clear();
        foreach (var entry in Item.Entries.Skip(_first).Take(Math.Max(1, Item.InlineCount)))
        {
            _strip.Children.Add(Entry(entry, MatRibbon.ContentHeight - 10));
        }

        _up.IsEnabled = _first > 0;
        _down.IsEnabled = _first + Item.InlineCount < Item.Entries.Count;
        ShowSelected();
    }

    private ToggleButton Entry(MatRibbonGalleryEntry entry, double height)
    {
        var tile = new ToggleButton { Width = Item.EntryWidth, Height = height, Content = Ribbon.GalleryEntryContent?.Invoke(entry) ?? Picture(entry), Tag = entry.Id }.Themed("MatRibbon.GalleryEntry");
        AutomationProperties.SetName(tile, entry.Label);
        ToolTip.SetTip(tile, entry.Label);
        tile.Click += (_, _) => Ribbon.Run(() => Ribbon.Definition.Choose(Item, entry.Id));
        _entries.Add(tile);
        return tile;
    }

    private static Control Picture(MatRibbonGalleryEntry entry)
    {
        var label = new TextBlock { Text = entry.Label, TextTrimming = TextTrimming.CharacterEllipsis, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Bottom };
        label.Use(TextBlock.FontSizeProperty, "Mat.FontSizeXs");
        label.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        Control picture;
        if (!string.IsNullOrEmpty(entry.Icon))
        {
            picture = new MatIcon { Icon = entry.Icon, Size = 24, HorizontalAlignment = HorizontalAlignment.Center };
        }
        else
        {
            var sample = new TextBlock
            {
                Text = entry.Sample ?? entry.Label,
                FontWeight = entry.SampleIsBold ? FontWeight.SemiBold : FontWeight.Normal,
                FontStyle = entry.SampleIsItalic ? FontStyle.Italic : FontStyle.Normal,
                TextTrimming = TextTrimming.CharacterEllipsis,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
            };
            if (entry.SampleSize is { } size)
            {
                sample.FontSize = size;
            }

            if (entry.SampleIsAccent)
            {
                sample.Use(TextBlock.ForegroundProperty, "Mat.AccentText");
            }

            picture = sample;
        }

        var grid = new Grid { RowDefinitions = new RowDefinitions("*,Auto") };
        Grid.SetRow(label, 1);
        grid.Children.Add(picture);
        grid.Children.Add(label);
        return grid;
    }

    private void ShowSelected()
    {
        foreach (var tile in _entries)
        {
            tile.IsChecked = Equals(tile.Tag, Item.SelectedId);
        }
    }

    private void OpenAll(Control at)
    {
        var wrap = new WrapPanel { MaxWidth = (Item.EntryWidth + 4) * 5, ItemSpacing = 4, LineSpacing = 4 };
        foreach (var entry in Item.Entries)
        {
            wrap.Children.Add(Entry(entry, 60));
        }

        ShowSelected();
        var content = new StackPanel { Spacing = 6, Children = { wrap } };
        if (Item.Menu.Count > 0)
        {
            var line = new Border { Height = 1, Margin = new Thickness(-4, 2) };
            line.Use(Border.BackgroundProperty, "Mat.Border");
            content.Children.Add(line);
            foreach (var menuItem in Item.Menu.OfType<MatRibbonButton>())
            {
                var face = new RibbonFace(chevron: false);
                face.Set(menuItem.Icon, menuItem.Label, false);
                face.Apply(MatRibbonSize.Medium);
                var button = new Button { Content = face.Content, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 28, IsEnabled = menuItem.IsEnabled }.Themed("MatRibbon.Item");
                button.Click += (_, _) => Ribbon.Run(() => Ribbon.Definition.Invoke(menuItem));
                content.Children.Add(button);
            }
        }

        var flyout = new Flyout { Content = content, Placement = PlacementMode.BottomEdgeAlignedLeft };
        flyout.Closed += (_, _) =>
        {
            foreach (var tile in wrap.Children.OfType<ToggleButton>())
            {
                _entries.Remove(tile);
            }
        };
        Ribbon.Open(flyout, at);
    }
}

/// <summary>A thin line between items.</summary>
internal sealed class SeparatorView : RibbonItemView
{
    private readonly Border _line = new() { Width = 1, Margin = new Thickness(3, 4) };

    public SeparatorView(MatRibbonSeparator item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _line.Use(Border.BackgroundProperty, "Mat.Border");
    }

    public override Control Root => _line;

    public override bool IsTall => true;

    public override bool TakesKeyTip => false;

    public override void Act()
    {
    }
}

/// <summary>A stack: its items one under the other in a column of their own.</summary>
internal sealed class StackView : RibbonItemView
{
    private readonly StackPanel _panel = new() { Spacing = MatRibbon.RowGap, Height = MatRibbon.ContentHeight };
    private readonly List<RibbonItemView> _inner;

    public StackView(MatRibbonStack item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _inner = [.. item.Items.Select(i => Create(i, ribbon))];
        foreach (var view in _inner)
        {
            view.Root.HorizontalAlignment = HorizontalAlignment.Left;
            _panel.Children.Add(view.Root);
        }
    }

    public override Control Root => _panel;

    public override bool IsTall => true;

    public override bool TakesKeyTip => false;

    public override IEnumerable<RibbonItemView> Inner => _inner;

    public override void Apply(MatRibbonSize cap)
    {
        foreach (var view in _inner)
        {
            view.Apply((MatRibbonSize)Math.Min((int)cap, (int)MatRibbonSize.Medium));
        }
    }

    public override void Act()
    {
    }
}

/// <summary>A row: small items side by side, close together.</summary>
internal sealed class RowView : RibbonItemView
{
    private readonly StackPanel _panel = new() { Orientation = Orientation.Horizontal, Spacing = 1, Height = MatRibbon.RowHeight };
    private readonly List<RibbonItemView> _inner;

    public RowView(MatRibbonRow item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _inner = [.. item.Items.Select(i => Create(i, ribbon))];
        foreach (var view in _inner)
        {
            _panel.Children.Add(view.Root);
            if (view is SeparatorView)
            {
                view.Root.Margin = new Thickness(3, 3);
            }
        }
    }

    public override Control Root => _panel;

    public override bool TakesKeyTip => false;

    public override IEnumerable<RibbonItemView> Inner => _inner;

    public override void Apply(MatRibbonSize cap)
    {
        foreach (var view in _inner)
        {
            view.Apply(MatRibbonSize.Small);
        }
    }

    public override void Act()
    {
    }
}

/// <summary>A place for a control of the program's own (<see cref="MatRibbon.CustomContent"/>).</summary>
internal sealed class CustomView : RibbonItemView
{
    private readonly ContentControl _host = new() { VerticalContentAlignment = VerticalAlignment.Center };

    public CustomView(MatRibbonCustom item, MatRibbon ribbon)
        : base(item, ribbon)
    {
        _host.Content = ribbon.CustomContent?.Invoke(item);
        _host.Height = item.IsTall ? MatRibbon.ContentHeight : MatRibbon.RowHeight;
    }

    public new MatRibbonCustom Item => (MatRibbonCustom)base.Item;

    public override Control Root => _host;

    public override bool IsTall => Item.IsTall;

    public override bool TakesKeyTip => _host.Content is Control { Focusable: true };

    public override void Act() => (_host.Content as Control)?.Focus(Avalonia.Input.NavigationMethod.Tab);
}

/// <summary>The menus and the palette of the ribbon, built when they open.</summary>
internal static class RibbonMenus
{
    /// <summary>A field one row high inside the ribbon: the text field of Fluent is 34 high by its resources, so the field gets its own.</summary>
    public static void Compact(Control field)
    {
        field.Resources["TextControlThemeMinHeight"] = MatRibbon.RowHeight - 2;
        field.Resources["TextControlThemePadding"] = new Thickness(8, 0, 4, 0);
        field.Resources["ComboBoxMinHeight"] = MatRibbon.RowHeight;
        field.Resources["TextControlThemeMinWidth"] = 0d;
    }

    public static MenuFlyout Menu(MatRibbon ribbon, IEnumerable<MatRibbonItem> items)
    {
        var flyout = new MenuFlyout();
        foreach (var entry in Entries(ribbon, items))
        {
            flyout.Items.Add(entry);
        }

        return flyout;
    }

    private static IEnumerable<Control> Entries(MatRibbon ribbon, IEnumerable<MatRibbonItem> items)
    {
        foreach (var item in items.Where(i => i.IsVisible))
        {
            if (item is MatRibbonSeparator)
            {
                yield return new Separator();
                continue;
            }

            var entry = new MenuItem
            {
                Header = item.Label,
                IsEnabled = item.IsEnabled,
                Icon = string.IsNullOrEmpty(item.Icon) ? null : new MatIcon { Icon = item.Icon, Size = 16 },
            };
            if (!string.IsNullOrWhiteSpace(item.Shortcut))
            {
                entry.InputGesture = Gesture(item.Shortcut);
            }

            AutomationProperties.SetName(entry, item.Label);
            switch (item)
            {
                case MatRibbonMenuButton sub when item is not MatRibbonSplitButton:
                    foreach (var child in Entries(ribbon, sub.Menu))
                    {
                        entry.Items.Add(child);
                    }

                    break;
                case MatRibbonToggle toggle:
                    entry.ToggleType = toggle.GroupName is null ? MenuItemToggleType.CheckBox : MenuItemToggleType.Radio;
                    entry.IsChecked = toggle.IsChecked;
                    entry.Click += (_, _) => ribbon.Run(() => ribbon.Definition.Invoke(toggle));
                    break;
                default:
                    entry.Click += (_, _) => ribbon.Run(() => ribbon.Definition.Invoke(item));
                    break;
            }

            yield return entry;
        }
    }

    private static Avalonia.Input.KeyGesture? Gesture(string shortcut)
    {
        try
        {
            return Avalonia.Input.KeyGesture.Parse(shortcut.Replace("Strg", "Ctrl", StringComparison.Ordinal).Replace("Umschalt", "Shift", StringComparison.Ordinal));
        }
        catch (ArgumentException)
        {
            return null;
        }
    }

    /// <summary>The palette of a colour button: the automatic colour, the colours of the document in rows, More colours.</summary>
    public static Control Palette(MatRibbon ribbon, MatRibbonColorPicker picker)
    {
        var panel = new StackPanel { Spacing = 8 };
        if (picker.AutomaticLabel is not null)
        {
            var swatch = new Border { Width = 16, Height = 16, BorderThickness = new Thickness(1) };
            swatch.Use(Border.BackgroundProperty, "Mat.Text");
            swatch.Use(Border.BorderBrushProperty, "Mat.Border");
            swatch.Use(Border.CornerRadiusProperty, "Mat.RadiusXs");
            var automatic = new Button
            {
                Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { swatch, new TextBlock { Text = picker.AutomaticLabel, VerticalAlignment = VerticalAlignment.Center } } },
                HorizontalAlignment = HorizontalAlignment.Stretch,
                Height = 28,
            }.Themed("MatRibbon.Item");
            automatic.Click += (_, _) => ribbon.Run(() => ribbon.Definition.ChooseColor(picker, null));
            panel.Children.Add(automatic);
        }

        var grid = new UniformGrid { Columns = Math.Max(1, picker.Columns) };
        foreach (var color in picker.Palette.Where(MatRibbonDefinition.IsHexColor))
        {
            var swatch = new Button { Background = new SolidColorBrush(Color.Parse(color)), Margin = new Thickness(2), Tag = color }.Themed("MatRibbon.Swatch");
            if (string.Equals(color, picker.Color, StringComparison.OrdinalIgnoreCase))
            {
                swatch.Classes.Add("chosen");
            }

            AutomationProperties.SetName(swatch, color);
            ToolTip.SetTip(swatch, color);
            swatch.Click += (_, _) => ribbon.Run(() => ribbon.Definition.ChooseColor(picker, color));
            grid.Children.Add(swatch);
        }

        panel.Children.Add(grid);
        if (picker.MoreColors is { } more)
        {
            var button = new Button { Content = new TextBlock { Text = MatText.Get("ribbon.moreColors") }, HorizontalAlignment = HorizontalAlignment.Stretch, Height = 28 }.Themed("MatRibbon.Item");
            button.Click += (_, _) => ribbon.Run(more);
            panel.Children.Add(button);
        }

        return panel;
    }
}

/// <summary>
/// A group of a ribbon: its items laid out by <see cref="RibbonGroupPanel"/> in the size of its state, its label under them with the button of its dialog, a line at its right; collapsed, one
/// button with its icon and label that opens the group in a flyout.
/// </summary>
internal sealed class RibbonGroupView : Grid
{
    private readonly RibbonGroupPanel _panel = new();
    private readonly Grid _labelRow = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Height = 18 };
    private readonly TextBlock _label = new() { HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
    private readonly Button _collapsed = new Button().Themed("MatRibbon.Item");
    private readonly RibbonFace _collapsedFace = new(chevron: true);
    private readonly Border _line = new() { Width = 1, Margin = new Thickness(0, 6, 0, 8) };
    private readonly StackPanel _body = new();
    private MatRibbonGroupState _state;
    private Flyout? _flyout;

    public RibbonGroupView(MatRibbonGroup group, MatRibbon ribbon)
    {
        Group = group;
        Ribbon = ribbon;
        Views = [.. group.Items.Select(i => RibbonItemView.Create(i, ribbon))];
        foreach (var view in Views)
        {
            _panel.Add(view);
        }

        ColumnDefinitions = new ColumnDefinitions("*,Auto");
        _line.Use(Border.BackgroundProperty, "Mat.Border");
        _label.Text = group.Label;
        _label.Use(TextBlock.FontSizeProperty, "Mat.FontSizeXs");
        _label.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _labelRow.Children.Add(_label);
        if (group.Launcher is { } launcher)
        {
            var open = new Button { Content = new MatIcon { Icon = "arrow-down-right", Size = 11 }, Width = 16, Height = 16, Padding = default, Margin = new Thickness(0, 0, 2, 0) }.Themed("MatRibbon.Item");
            open.HorizontalContentAlignment = HorizontalAlignment.Center;
            open.MinHeight = 16;
            var name = MatText.Format("ribbon.launcher", group.Label);
            AutomationProperties.SetName(open, name);
            ToolTip.SetTip(open, name);
            open.Click += (_, _) => ribbon.Run(launcher);
            Grid.SetColumn(open, 1);
            _labelRow.Children.Add(open);
            Launcher = open;
        }

        _body.Margin = new Thickness(4, 0);
        _body.Children.Add(_panel);
        _body.Children.Add(_labelRow);
        Children.Add(_body);

        _collapsedFace.Set(group.Icon ?? group.Items.SelectMany(Walk).Select(i => i.Icon).FirstOrDefault(i => i is not null), group.Label, true);
        _collapsedFace.Apply(MatRibbonSize.Large);
        RibbonFace.Shape(_collapsed, MatRibbonSize.Large);
        _collapsed.Height = MatRibbon.ContentHeight + 18;
        _collapsed.Margin = new Thickness(4, 0);
        _collapsed.Content = _collapsedFace.Content;
        _collapsed.IsVisible = false;
        AutomationProperties.SetName(_collapsed, group.Label);
        ToolTip.SetTip(_collapsed, group.Label);
        _collapsed.Click += (_, _) => OpenCollapsed();
        Children.Add(_collapsed);

        Grid.SetColumn(_line, 1);
        Children.Add(_line);
        AutomationProperties.SetName(this, group.Label);
        Apply(MatRibbonGroupState.Large);
    }

    public MatRibbonGroup Group { get; }

    public MatRibbon Ribbon { get; }

    public IReadOnlyList<RibbonItemView> Views { get; }

    /// <summary>The button of the group's dialog, or null.</summary>
    public Button? Launcher { get; }

    /// <summary>The button of the collapsed group.</summary>
    public Button CollapsedButton => _collapsed;

    /// <summary>The widths of the four states, measured once and again after a label, an icon or a visibility changed.</summary>
    public double[]? Widths { get; set; }

    public MatRibbonGroupState State => _state;

    /// <summary>True while the collapsed group is open in its flyout.</summary>
    public bool IsOpen => _flyout?.IsOpen == true;

    public void Apply(MatRibbonGroupState state)
    {
        _state = state;
        var collapsed = state == MatRibbonGroupState.Collapsed;
        _collapsed.IsVisible = collapsed;
        _body.IsVisible = !collapsed;
        var cap = state switch
        {
            MatRibbonGroupState.Medium => MatRibbonSize.Medium,
            MatRibbonGroupState.Small => MatRibbonSize.Small,
            _ => MatRibbonSize.Large,
        };
        foreach (var view in Views)
        {
            view.Apply(cap);
        }

        // a panel measured again with the same room answers from its last measure: everything of the group measures anew
        foreach (var layoutable in _body.GetVisualDescendants().OfType<Layoutable>().Prepend(_body))
        {
            layoutable.InvalidateMeasure();
        }

        InvalidateMeasure();
    }

    /// <summary>Every view of the group, those inside stacks and rows included.</summary>
    public IEnumerable<RibbonItemView> AllViews() => Views.SelectMany(Flatten);

    public void OpenCollapsed()
    {
        if (_state != MatRibbonGroupState.Collapsed)
        {
            return;
        }

        // the group itself moves into the flyout (its controls are the same, with their state) and back when it closes
        Children.Remove(_body);
        _body.IsVisible = true;
        foreach (var view in Views)
        {
            view.Apply(MatRibbonSize.Large);
        }

        _flyout = new Flyout { Content = _body, Placement = PlacementMode.BottomEdgeAlignedLeft };
        _flyout.Closed += (_, _) =>
        {
            _flyout!.Content = null;
            Children.Insert(0, _body);
            Apply(_state);
            _flyout = null;
        };
        Ribbon.Open(_flyout, _collapsed);
    }

    private static IEnumerable<RibbonItemView> Flatten(RibbonItemView view) => view.Inner.SelectMany(Flatten).Prepend(view);

    private static IEnumerable<MatRibbonItem> Walk(MatRibbonItem item) => item.Children.SelectMany(Walk).Prepend(item);
}

/// <summary>
/// The items of a group in columns, like an office ribbon: a large item (and a stack, a gallery, a separator) is a column of its own at the full height; medium and small items stack three in a
/// column, from the top.
/// </summary>
internal sealed class RibbonGroupPanel : Panel
{
    private readonly List<RibbonItemView> _views = [];

    public RibbonGroupPanel()
    {
        Height = MatRibbon.ContentHeight;
    }

    public void Add(RibbonItemView view)
    {
        _views.Add(view);
        Children.Add(view.Root);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var width = 0.0;
        foreach (var column in Columns(measure: true))
        {
            width += column.Width + (width > 0 ? 2 : 0);
        }

        return new Size(width, MatRibbon.ContentHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var column in Columns(measure: false))
        {
            if (column.Tall)
            {
                var view = column.Views[0];
                view.Root.Arrange(new Rect(x, 0, column.Width, MatRibbon.ContentHeight));
            }
            else
            {
                for (var row = 0; row < column.Views.Count; row++)
                {
                    var root = column.Views[row].Root;
                    root.Arrange(new Rect(x, row * (MatRibbon.RowHeight + MatRibbon.RowGap), root.DesiredSize.Width, MatRibbon.RowHeight));
                }
            }

            x += column.Width + 2;
        }

        return finalSize;
    }

    private List<(bool Tall, List<RibbonItemView> Views, double Width)> Columns(bool measure)
    {
        var columns = new List<(bool Tall, List<RibbonItemView> Views, double Width)>();
        List<RibbonItemView>? open = null;
        var openWidth = 0.0;
        foreach (var view in _views)
        {
            var root = view.Root;
            if (!root.IsVisible)
            {
                if (measure)
                {
                    root.Measure(default);
                }

                continue;
            }

            if (view.IsTall)
            {
                if (open is not null)
                {
                    columns.Add((false, open, openWidth));
                    open = null;
                }

                if (measure)
                {
                    root.Measure(new Size(double.PositiveInfinity, MatRibbon.ContentHeight));
                }

                columns.Add((true, [view], root.DesiredSize.Width));
                continue;
            }

            if (measure)
            {
                root.Measure(new Size(double.PositiveInfinity, MatRibbon.RowHeight));
            }

            if (open is null || open.Count == 3)
            {
                if (open is not null)
                {
                    columns.Add((false, open, openWidth));
                }

                open = [];
                openWidth = 0;
            }

            open.Add(view);
            openWidth = Math.Max(openWidth, root.DesiredSize.Width);
        }

        if (open is not null)
        {
            columns.Add((false, open, openWidth));
        }

        return columns;
    }
}

/// <summary>The groups of a tab side by side; when they do not fit, they shrink in the order of <see cref="MatRibbonLayout"/>.</summary>
internal sealed class RibbonPagePanel : Panel
{
    public RibbonPagePanel(MatRibbonTab tab, IEnumerable<RibbonGroupView> groups)
    {
        Tab = tab;
        Groups = [.. groups];
        foreach (var group in Groups)
        {
            Children.Add(group);
        }

        ClipToBounds = true;
    }

    public MatRibbonTab Tab { get; }

    public IReadOnlyList<RibbonGroupView> Groups { get; }

    /// <summary>How many times the widths of the groups were measured (a change of state must not measure them again; for tests).</summary>
    public int WidthMeasures { get; private set; }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visible = Groups.Where(g => g.IsVisible).ToList();
        var height = MatRibbon.ContentHeight + 18;
        foreach (var group in visible.Where(g => g.Widths is null && !g.IsOpen))
        {
            var widths = new double[4];
            for (var state = MatRibbonGroupState.Large; state <= MatRibbonGroupState.Collapsed; state++)
            {
                group.Apply(state);
                group.Measure(new Size(double.PositiveInfinity, height));
                widths[(int)state] = group.DesiredSize.Width;
            }

            group.Widths = widths;
            WidthMeasures++;
        }

        // a group that is open in its flyout stays collapsed until the flyout closes
        var states = MatRibbonLayout.Fit([.. visible.Select(g => g.Widths ?? [0, 0, 0, 0])], [.. visible.Select(g => g.Group.ShrinkOrder)], availableSize.Width);
        var total = 0.0;
        for (var i = 0; i < visible.Count; i++)
        {
            if (visible[i].State != states[i] && !visible[i].IsOpen)
            {
                visible[i].Apply(states[i]);
            }

            visible[i].Measure(new Size(double.PositiveInfinity, height));
            total += visible[i].DesiredSize.Width;
        }

        foreach (var group in Groups.Where(g => !g.IsVisible))
        {
            group.Measure(default);
        }

        return new Size(double.IsInfinity(availableSize.Width) ? total : Math.Min(total, availableSize.Width), height);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var x = 0.0;
        foreach (var group in Groups.Where(g => g.IsVisible))
        {
            group.Arrange(new Rect(x, 0, group.DesiredSize.Width, finalSize.Height));
            x += group.DesiredSize.Width;
        }

        return finalSize;
    }
}
