// MatDesktop 0.3.1 – a copy from MatDesign, do not edit here
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.Media;

namespace MatDesktop;

/// <summary>
/// The backstage of a document program: what the file tab of <see cref="MatRibbon"/> opens, over the whole window (docs/DESKTOP.md, "Document programs"). At the left a column like the menu
/// (<see cref="MatNav"/>): the arrow back at the top, the entries with icon and label (the current one tinted, COLOR-08: <c>Neu</c>, <c>Öffnen</c>, <c>Speichern</c> …), a line and the lower
/// entries (<c>Konto</c>, <c>Optionen</c>), and at the foot the program's own (<see cref="Foot"/>) and the version (<c>Version 1.4.2 · release</c>, SETUP-07), which opens About. At the right
/// the page of the current entry. An entry is a page (<see cref="AddPage"/>) or a command that closes the backstage (<see cref="AddCommand"/>); a page without an entry (About) is shown by
/// <see cref="ShowPage"/>. Escape and the arrow close it.
/// Put it over the content of the window (the last child of a <see cref="Panel"/> that holds the title bar, the ribbon and the document); it is hidden until <see cref="Open"/>.
/// </summary>
public sealed class MatBackstage : Border
{
    /// <summary>The width of the column of entries (the open menu's).</summary>
    public const double ColumnWidth = MatNav.OpenWidth;

    /// <summary>What the program shows at the foot of the column above the version, or null.</summary>
    public static readonly StyledProperty<Control?> FootProperty = AvaloniaProperty.Register<MatBackstage, Control?>(nameof(Foot));

    /// <summary>The name of the program, for "About &lt;App&gt;".</summary>
    public static readonly StyledProperty<string> AppNameProperty = AvaloniaProperty.Register<MatBackstage, string>(nameof(AppName), string.Empty);

    /// <summary>The version number; null shows no version.</summary>
    public static readonly StyledProperty<string?> VersionProperty = AvaloniaProperty.Register<MatBackstage, string?>(nameof(Version));

    /// <summary>The channel (release, nightly, local).</summary>
    public static readonly StyledProperty<string> ChannelProperty = AvaloniaProperty.Register<MatBackstage, string>(nameof(Channel), MatVersion.Release);

    private readonly StackPanel _items = new() { Spacing = 2 };
    private readonly Border _line = new() { Height = 1, Margin = new Thickness(12, 8, 12, 8), IsVisible = false };
    private readonly StackPanel _lower = new() { Spacing = 2 };
    private readonly ContentControl _footHost = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch };
    private readonly TextBlock _versionText = new() { TextTrimming = TextTrimming.CharacterEllipsis, TextWrapping = TextWrapping.NoWrap };
    private readonly Button _version = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(8, 0, 0, 0), IsVisible = false };
    private readonly Button _back = new() { HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(2, 0, 0, 16) };
    private readonly ContentControl _page = new() { HorizontalContentAlignment = HorizontalAlignment.Stretch, VerticalContentAlignment = VerticalAlignment.Top };
    private readonly Dictionary<MatNavItem, Control> _pages = [];
    private readonly Dictionary<MatNavItem, Action> _commands = [];
    private readonly List<MatNavItem> _upper = [];
    private readonly List<MatNavItem> _lowerItems = [];

    static MatBackstage()
    {
        FootProperty.Changed.AddClassHandler<MatBackstage>((b, e) => b._footHost.Content = e.NewValue);
        AppNameProperty.Changed.AddClassHandler<MatBackstage>((b, _) => b.RefreshVersion());
        VersionProperty.Changed.AddClassHandler<MatBackstage>((b, _) => b.RefreshVersion());
        ChannelProperty.Changed.AddClassHandler<MatBackstage>((b, _) => b.RefreshVersion());
    }

    /// <summary>An empty backstage, hidden: add entries, then <see cref="Open"/>.</summary>
    public MatBackstage()
    {
        IsVisible = false;
        this.Use(BackgroundProperty, "Mat.Bg");
        _line.Use(BackgroundProperty, "Mat.Border");
        _back.Themed("MatButton.Icon");
        _back.Content = new MatIcon { Icon = "back", Size = 20 };
        var back = MatText.Get("backstage.back");
        AutomationProperties.SetName(_back, back);
        ToolTip.SetTip(_back, $"{back} (Esc)");
        _back.Click += (_, _) => Close();
        _version.Themed("MatButton.Link");
        _version.Content = _versionText;
        _version.Click += (_, _) => AboutRequested?.Invoke(this, EventArgs.Empty);

        var top = new StackPanel { Children = { _back, _items, _line, _lower } };
        var bottom = new StackPanel { Spacing = 8, Children = { _footHost, _version } };
        var dock = new DockPanel { Margin = new Thickness(12, 16, 12, 14) };
        DockPanel.SetDock(top, Dock.Top);
        DockPanel.SetDock(bottom, Dock.Bottom);
        dock.Children.Add(top);
        dock.Children.Add(bottom);
        dock.Children.Add(new Panel());
        var column = new Border { Width = ColumnWidth, BorderThickness = new Thickness(0, 0, 1, 0), Child = dock };
        column.Use(BackgroundProperty, "Mat.NavBg");
        column.Use(BorderBrushProperty, "Mat.Border");

        var scroll = new ScrollViewer { Content = new Border { Padding = new Thickness(40, 28, 40, 28), Child = _page } };
        Grid.SetColumn(scroll, 1);
        Child = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Children = { column, scroll } };
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        RefreshVersion();
    }

    /// <summary>The backstage was opened.</summary>
    public event EventHandler? Opened;

    /// <summary>The backstage was closed (Escape, the arrow, a command): the focus goes back to the document.</summary>
    public event EventHandler? Closed;

    /// <summary>An entry was chosen (a page shown or a command run).</summary>
    public event EventHandler<MatNavItem>? ItemInvoked;

    /// <summary>The version at the foot was chosen: open About.</summary>
    public event EventHandler? AboutRequested;

    /// <summary>What the program shows at the foot above the version (a card, the state of the cloud), or null.</summary>
    public Control? Foot
    {
        get => GetValue(FootProperty);
        set => SetValue(FootProperty, value);
    }

    /// <summary>The name of the program.</summary>
    public string AppName
    {
        get => GetValue(AppNameProperty);
        set => SetValue(AppNameProperty, value);
    }

    /// <summary>The version number, shown at the foot (<c>Version 1.4.2 · release</c>); null for none.</summary>
    public string? Version
    {
        get => GetValue(VersionProperty);
        set => SetValue(VersionProperty, value);
    }

    /// <summary>The channel.</summary>
    public string Channel
    {
        get => GetValue(ChannelProperty);
        set => SetValue(ChannelProperty, value);
    }

    /// <summary>True while it is shown.</summary>
    public bool IsOpen => IsVisible;

    /// <summary>The entries above the line, in their order.</summary>
    public IReadOnlyList<MatNavItem> Items => _upper;

    /// <summary>The entries below the line.</summary>
    public IReadOnlyList<MatNavItem> LowerItems => _lowerItems;

    /// <summary>The entry whose page is shown, or null.</summary>
    public MatNavItem? Current => _upper.Concat(_lowerItems).FirstOrDefault(i => i.IsCurrent);

    /// <summary>The page shown, or null.</summary>
    public Control? CurrentPage => _page.Content as Control;

    /// <summary>The arrow back (for tests).</summary>
    public Button BackButton => _back;

    /// <summary>The version at the foot (for tests).</summary>
    public Button VersionButton => _version;

    /// <summary>Adds an entry that shows a page and returns it.</summary>
    public MatNavItem AddPage(MatNavItem item, Control page, bool lower = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(page);
        _pages[item] = page;
        return Add(item, lower);
    }

    /// <summary>Adds an entry that runs a command (Speichern) and closes the backstage, and returns it.</summary>
    public MatNavItem AddCommand(MatNavItem item, Action command, bool lower = false)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(command);
        _commands[item] = command;
        return Add(item, lower);
    }

    /// <summary>
    /// Shows the backstage with the page of <paramref name="item"/> (or the current one, or the first page) and puts the focus on its entry. <paramref name="byKeyboard"/> says that the
    /// keyboard opened it (Alt, F; Enter on the file tab; a shortcut): only then the entry shows the focus ring (<see cref="MatRibbonFileEventArgs.ByKeyboard"/>).
    /// </summary>
    public void Open(MatNavItem? item = null, bool byKeyboard = false)
    {
        var page = item is not null && _pages.ContainsKey(item) ? item : Current ?? _upper.Concat(_lowerItems).FirstOrDefault(_pages.ContainsKey);
        if (page is not null)
        {
            Select(page);
        }

        Show(page ?? (Control)_back, byKeyboard);
    }

    /// <summary>
    /// Shows a page that has no entry in the column (About, opened by the version at the foot; a page a link of another page leads to) and opens the backstage when it is closed. No entry is
    /// current; the focus goes to the arrow back, and the arrow and Escape close the backstage as always. <see cref="Open"/> or a click on an entry shows the pages of the entries again.
    /// </summary>
    public void ShowPage(Control page, bool byKeyboard = false)
    {
        ArgumentNullException.ThrowIfNull(page);
        foreach (var each in _upper.Concat(_lowerItems))
        {
            each.IsCurrent = false;
        }

        _page.Content = page;
        Show(_back, byKeyboard);
    }

    /// <summary>Hides the backstage.</summary>
    public void Close()
    {
        if (!IsVisible)
        {
            return;
        }

        IsVisible = false;
        Closed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>Shows the page of an entry and makes it the current one.</summary>
    public void Select(MatNavItem item)
    {
        ArgumentNullException.ThrowIfNull(item);
        if (!_pages.TryGetValue(item, out var page))
        {
            return;
        }

        foreach (var each in _upper.Concat(_lowerItems))
        {
            each.IsCurrent = each == item;
        }

        _page.Content = page;
    }

    /// <summary>Makes the backstage visible and focuses <paramref name="focus"/>, with the focus ring only for the keyboard.</summary>
    private void Show(Control focus, bool byKeyboard)
    {
        var wasOpen = IsVisible;
        IsVisible = true;
        focus.Focus(byKeyboard ? NavigationMethod.Tab : NavigationMethod.Pointer);
        if (!wasOpen)
        {
            Opened?.Invoke(this, EventArgs.Empty);
        }
    }

    private MatNavItem Add(MatNavItem item, bool lower)
    {
        (lower ? _lowerItems : _upper).Add(item);
        (lower ? _lower : _items).Children.Add(item);
        item.Click += OnItemClick;
        _line.IsVisible = _upper.Count > 0 && _lowerItems.Count > 0;
        return item;
    }

    private void OnItemClick(object? sender, RoutedEventArgs e)
    {
        if (sender is not MatNavItem item)
        {
            return;
        }

        if (_commands.TryGetValue(item, out var command))
        {
            command();
            ItemInvoked?.Invoke(this, item);
            Close();
            return;
        }

        Select(item);
        ItemInvoked?.Invoke(this, item);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape && !e.Handled)
        {
            Close();
            e.Handled = true;
        }
    }

    private void RefreshVersion()
    {
        _version.IsVisible = !string.IsNullOrWhiteSpace(Version);
        if (string.IsNullOrWhiteSpace(Version))
        {
            return;
        }

        var version = MatVersion.Format(Version, Channel);
        _versionText.Text = version;
        ToolTip.SetTip(_version, $"{MatText.Format("nav.about", AppName)} – {version}");
        AutomationProperties.SetName(_version, version);
    }
}
