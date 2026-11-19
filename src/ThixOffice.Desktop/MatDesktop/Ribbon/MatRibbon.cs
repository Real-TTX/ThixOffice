// MatDesktop 0.3.1 – a copy from MatDesign, do not edit here
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace MatDesktop;

/// <summary>Where the key tips of a ribbon stand: none, on the tabs (after Alt), on the commands of the chosen tab (after its letter).</summary>
public enum MatRibbonKeyTipMode
{
    /// <summary>No key tips are shown.</summary>
    None,

    /// <summary>The key tips of the file tab and the tabs.</summary>
    Tabs,

    /// <summary>The key tips of the commands of the chosen tab.</summary>
    Items,
}

/// <summary>How the file tab was chosen (<see cref="MatRibbon.FileRequested"/>): by the keyboard (Enter, Space, its key tip) or by the mouse.</summary>
public sealed class MatRibbonFileEventArgs(bool byKeyboard) : EventArgs
{
    /// <summary>True when the keyboard chose it: the backstage shows the focus ring (<c>backstage.Open(byKeyboard: e.ByKeyboard)</c>).</summary>
    public bool ByKeyboard { get; } = byKeyboard;
}

/// <summary>
/// The ribbon of a document program (docs/DESKTOP.md, "Document programs": it takes the place of <see cref="MatNav"/>): a strip of tabs over the commands of the chosen tab, drawn from a
/// <see cref="MatRibbonDefinition"/>, which knows nothing of Avalonia.
/// <list type="bullet">
/// <item>The first tab, <c>Datei</c>, shows no commands: it raises <see cref="FileRequested"/> (the program opens its <see cref="MatBackstage"/>).</item>
/// <item>Contextual tabs (<see cref="MatRibbonTab.Context"/>) show while their context does (<see cref="MatRibbonDefinition.ShowContext"/>), marked in the accent.</item>
/// <item>A change of state (enabled, checked, a value) goes to the item of the definition and touches only its control; nothing is built again.</item>
/// <item>It folds to the strip of tabs (<see cref="IsCollapsed"/>: Ctrl+F1, a double click on a tab, the chevron); then a tab shows its commands over the document until one is used.
/// <see cref="CollapsedChanged"/> tells the program to remember it.</item>
/// <item>In a narrow window the groups shrink in the order of <see cref="MatRibbonLayout"/>: large items become medium, then small, then a group becomes one button that opens it.</item>
/// <item>The keyboard (DESK-07): Alt shows the key tips of the tabs, a letter chooses a tab and shows those of its commands, a letter of a command uses it, Escape goes back a step; the arrow
/// keys walk the tabs and the commands, the mouse wheel over the tabs changes the tab.</item>
/// </list>
/// The look is that of the tokens (light and dark; the fonts of <see cref="MatTheme"/>, the system's on macOS); the control themes are <c>MatRibbon.*</c> in <c>Themes/Controls.axaml</c>.
/// </summary>
public sealed class MatRibbon : Border
{
    /// <summary>The height of a row of medium and small items.</summary>
    public const double RowHeight = 24;

    /// <summary>The space between two rows.</summary>
    public const double RowGap = 2;

    /// <summary>The height of the commands of a group: three rows.</summary>
    public const double ContentHeight = (3 * RowHeight) + (2 * RowGap);

    /// <summary>The height of the strip of tabs.</summary>
    public const double TabStripHeight = 34;

    /// <summary>The id the file tab has in <see cref="ShownKeyTips"/>.</summary>
    public const string FileId = "file";

    /// <summary>True while the ribbon is folded to its strip of tabs.</summary>
    public static readonly StyledProperty<bool> IsCollapsedProperty = AvaloniaProperty.Register<MatRibbon, bool>(nameof(IsCollapsed));

    /// <summary>What stands at the right end of the strip of tabs (Freigeben, Kommentare), or null.</summary>
    public static readonly StyledProperty<Control?> TrailingContentProperty = AvaloniaProperty.Register<MatRibbon, Control?>(nameof(TrailingContent));

    private readonly string _radioGroup = "mat-ribbon-" + Guid.NewGuid().ToString("N");
    private readonly DockPanel _dock = new();
    private readonly Border _strip = new() { Height = TabStripHeight, BorderThickness = new Thickness(0, 0, 0, 1) };
    private readonly Grid _stripGrid = new() { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto,Auto"), Margin = new Thickness(8, 0) };
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, VerticalAlignment = VerticalAlignment.Bottom };
    private readonly Button _file = new Button { Margin = new Thickness(0, 0, 8, 0) }.Themed("MatRibbon.FileTab");
    private readonly Border _contextLine = new() { Width = 1, Margin = new Thickness(6, 8, 4, 8), IsVisible = false };
    private readonly ContentControl _trailing = new() { VerticalAlignment = VerticalAlignment.Center };
    private readonly Button _expand = new Button { VerticalAlignment = VerticalAlignment.Center, IsVisible = false }.Themed("MatButton.Quick");
    private readonly Border _page = new();
    private readonly Grid _pageGrid = new() { ColumnDefinitions = new ColumnDefinitions("*,Auto"), Margin = new Thickness(6, 6, 4, 4) };
    private readonly Border _pageHost = new() { HorizontalAlignment = HorizontalAlignment.Left };
    private readonly Button _collapse = new Button { VerticalAlignment = VerticalAlignment.Bottom, Width = 24, Height = 24, MinHeight = 24 }.Themed("MatButton.Quick");
    private readonly Popup _popup = new() { Placement = PlacementMode.BottomEdgeAlignedLeft, IsLightDismissEnabled = true };
    private readonly Border _popupFrame = new() { BorderThickness = new Thickness(1) };
    private readonly Dictionary<MatRibbonTab, RadioButton> _tabButtons = [];
    private readonly Dictionary<MatRibbonTab, RibbonPagePanel> _pages = [];
    private readonly Dictionary<string, RibbonItemView> _views = new(StringComparer.Ordinal);
    private readonly Dictionary<RibbonItemView, RibbonGroupView> _groupOf = [];
    private readonly List<(Visual Target, Control Badge)> _badges = [];
    private readonly Dictionary<string, string> _shownTips = new(StringComparer.Ordinal);
    private MatRibbonDefinition _definition = new();
    private MatRibbonTab? _selected;
    private FlyoutBase? _openFlyout;
    private TopLevel? _topLevel;
    private IInputElement? _focusBefore;
    private string _typed = string.Empty;
    private bool _altAlone;
    private bool _syncingTabs;
    private bool _fileByPointer;

    static MatRibbon()
    {
        IsCollapsedProperty.Changed.AddClassHandler<MatRibbon>((ribbon, e) => ribbon.OnCollapsedChanged(e));
        TrailingContentProperty.Changed.AddClassHandler<MatRibbon>((ribbon, e) => ribbon._trailing.Content = e.NewValue);
    }

    /// <summary>A ribbon that draws <paramref name="definition"/> (or nothing until <see cref="Definition"/> is set).</summary>
    public MatRibbon(MatRibbonDefinition? definition = null)
    {
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Use(BackgroundProperty, "Mat.Surface");
        this.Use(BorderBrushProperty, "Mat.Border");
        _strip.Use(BorderBrushProperty, "Mat.Border");
        _contextLine.Use(BackgroundProperty, "Mat.Border");
        _popupFrame.Use(BackgroundProperty, "Mat.Surface");
        _popupFrame.Use(BorderBrushProperty, "Mat.Border");
        _popupFrame.Use(CornerRadiusProperty, "Mat.RadiusLg");
        AutomationProperties.SetName(this, MatText.Get("ribbon.name"));
        AutomationProperties.SetName(_tabs, MatText.Get("ribbon.tabs"));

        // a click of the mouse opens the backstage without the focus ring, Enter, Space (and a screen reader) with it
        _file.AddHandler(PointerPressedEvent, (_, _) => _fileByPointer = true, RoutingStrategies.Tunnel, handledEventsToo: true);
        _file.AddHandler(KeyDownEvent, (_, _) => _fileByPointer = false, RoutingStrategies.Tunnel, handledEventsToo: true);
        _file.Click += (_, _) =>
        {
            var byKeyboard = !_fileByPointer;
            _fileByPointer = false;
            RequestFile(byKeyboard);
        };
        _file.DoubleTapped += (_, e) => e.Handled = true;
        _expand.Content = new MatIcon { Icon = "chevron-down", Size = 16 };
        _expand.Click += (_, _) => IsCollapsed = false;
        _collapse.Click += (_, _) => IsCollapsed = !IsCollapsed;

        var start = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center, Children = { _file } };
        Grid.SetColumn(_tabs, 1);
        Grid.SetColumn(_trailing, 2);
        Grid.SetColumn(_expand, 3);
        _stripGrid.Children.Add(start);
        _stripGrid.Children.Add(_tabs);
        _stripGrid.Children.Add(_trailing);
        _stripGrid.Children.Add(_expand);
        _stripGrid.Children.Add(_popup);
        _strip.Child = _stripGrid;
        _strip.PointerWheelChanged += OnStripWheel;

        Grid.SetColumn(_collapse, 1);
        _pageGrid.Children.Add(_pageHost);
        _pageGrid.Children.Add(_collapse);
        _page.Child = _pageGrid;
        _popup.PlacementTarget = _strip;
        _popup.Child = _popupFrame;
        _popup.Closed += (_, _) => PageOpenChanged?.Invoke(this, false);

        DockPanel.SetDock(_strip, Dock.Top);
        _dock.Children.Add(_strip);
        _dock.Children.Add(_page);
        Child = _dock;
        AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Bubble);
        Definition = definition ?? new MatRibbonDefinition();
        RefreshCollapse();
    }

    /// <summary>
    /// The file tab was chosen (by click, Enter or Space, or its key tip): open the backstage, with the focus ring only when it was the keyboard
    /// (<c>backstage.Open(byKeyboard: e.ByKeyboard)</c>, <see cref="MatBackstage.Open"/>).
    /// </summary>
    public event EventHandler<MatRibbonFileEventArgs>? FileRequested;

    /// <summary>Another tab was chosen.</summary>
    public event EventHandler<MatRibbonTab>? SelectedTabChanged;

    /// <summary>The ribbon was folded or opened (the value is <see cref="IsCollapsed"/>): remember it for the next start.</summary>
    public event EventHandler<bool>? CollapsedChanged;

    /// <summary>The commands of a folded ribbon were shown over the document (true) or went away (false).</summary>
    public event EventHandler<bool>? PageOpenChanged;

    /// <summary>The definition it draws; setting it builds the ribbon again (a change of state does not need that).</summary>
    public MatRibbonDefinition Definition
    {
        get => _definition;
        set
        {
            ArgumentNullException.ThrowIfNull(value);
            _definition.ContextsChanged -= OnContextsChanged;
            _definition = value;
            _definition.ContextsChanged += OnContextsChanged;
            Rebuild();
        }
    }

    /// <summary>The content of a place of the program's own (<see cref="MatRibbonCustom"/>), asked for once when the ribbon is built.</summary>
    public Func<MatRibbonCustom, Control?>? CustomContent { get; set; }

    /// <summary>The picture of an entry of a gallery when the program draws it itself (a style in its real font), or null for the sample text of the entry.</summary>
    public Func<MatRibbonGalleryEntry, Control?>? GalleryEntryContent { get; set; }

    /// <summary>True while the ribbon is folded to its strip of tabs.</summary>
    public bool IsCollapsed
    {
        get => GetValue(IsCollapsedProperty);
        set => SetValue(IsCollapsedProperty, value);
    }

    /// <summary>What stands at the right end of the strip of tabs.</summary>
    public Control? TrailingContent
    {
        get => GetValue(TrailingContentProperty);
        set => SetValue(TrailingContentProperty, value);
    }

    /// <summary>
    /// True (the default) lets the ribbon handle the keys of its window: the shortcut that folds it (<see cref="HandlesCollapseKey"/>), Alt, F10 and the key tips. A program that routes every
    /// key itself sets false and calls <see cref="ShowKeyTips"/> and <see cref="ToggleCollapsed"/>.
    /// </summary>
    public bool HandlesWindowKeys { get; set; } = true;

    /// <summary>
    /// True (the default) lets the ribbon fold itself on <see cref="CollapseGesture"/> (Ctrl+F1, ⌥⌘R) while it handles the keys of its window. A program that owns that key itself (its own
    /// table of commands) sets false and calls <see cref="ToggleCollapsed"/>; Alt, F10 and the key tips stay the ribbon's. <see cref="CollapseShortcut"/> is still named in the tooltips.
    /// </summary>
    public bool HandlesCollapseKey { get; set; } = true;

    /// <summary>The label of the file tab in force: <see cref="MatRibbonDefinition.FileLabel"/>, or the word of MatDesktop in the language of the program (<c>Datei</c>, <c>File</c>).</summary>
    public string FileText => string.IsNullOrWhiteSpace(_definition.FileLabel) ? MatText.Get("ribbon.file") : _definition.FileLabel.Trim();

    /// <summary>The key tip of the file tab in force: <see cref="MatRibbonDefinition.FileKeyTip"/>, or that of MatDesktop (<c>D</c>, <c>F</c>).</summary>
    public string FileKeyTip => string.IsNullOrWhiteSpace(_definition.FileKeyTip) ? MatText.Get("ribbon.fileKeyTip") : _definition.FileKeyTip.Trim().ToUpperInvariant();

    /// <summary>The key that folds and opens the ribbon: Ctrl+F1, on macOS ⌥⌘R.</summary>
    public KeyGesture CollapseGesture { get; set; } = OperatingSystem.IsMacOS() ? new KeyGesture(Key.R, KeyModifiers.Meta | KeyModifiers.Alt) : new KeyGesture(Key.F1, KeyModifiers.Control);

    /// <summary>The words of that key in the tooltips (<c>Strg+F1</c>, <c>Ctrl+F1</c>, <c>⌥⌘R</c>).</summary>
    public string CollapseShortcut { get; set; } = OperatingSystem.IsMacOS() ? "⌥⌘R" : MatText.Get("ribbon.shortcut.collapse");

    /// <summary>The chosen tab, or null when the definition has none.</summary>
    public MatRibbonTab? SelectedTab => _selected;

    /// <summary>True while a folded ribbon shows the commands of a tab over the document.</summary>
    public bool IsPageOpen => _popup.IsOpen;

    /// <summary>Where the key tips stand now.</summary>
    public MatRibbonKeyTipMode KeyTipMode { get; private set; }

    /// <summary>The key tips shown now: the letters and the id of what they reach (<see cref="FileId"/>, a tab, an item, <c>group:&lt;id&gt;</c> for a collapsed group).</summary>
    public IReadOnlyDictionary<string, string> ShownKeyTips => _shownTips;

    /// <summary>The button of the file tab.</summary>
    public Button FileButton => _file;

    /// <summary>The button that folds the ribbon (and pins it again while a folded ribbon shows a tab).</summary>
    public Button CollapseButton => _collapse;

    /// <summary>The button at the end of the strip of tabs that opens a folded ribbon.</summary>
    public Button ExpandButton => _expand;

    /// <summary>The buttons of the tabs, by the id of their tab (those of contextual tabs included, visible or not).</summary>
    public IReadOnlyDictionary<string, RadioButton> TabButtons => _tabButtons.ToDictionary(p => p.Key.Id, p => p.Value, StringComparer.Ordinal);

    /// <summary>The control that draws an item (a button, a toggle button, the frame of a split button, a combo box ...), or null when there is no such item.</summary>
    public Control? ViewOf(string id) => _views.TryGetValue(id, out var view) ? view.Target : null;

    /// <summary>How a group is drawn now, or null when its tab is not the chosen one or there is no such group.</summary>
    public MatRibbonGroupState? GroupState(string groupId) =>
        _selected is not null && _pages.TryGetValue(_selected, out var page) ? page.Groups.FirstOrDefault(g => g.Group.Id == groupId)?.State : null;

    /// <summary>Chooses a tab (it shows, when it is visible).</summary>
    public void SelectTab(string id)
    {
        var tab = _definition.Tabs.FirstOrDefault(t => t.Id == id) ?? throw new KeyNotFoundException($"The ribbon has no tab {id}.");
        Select(tab);
    }

    /// <summary>Folds the ribbon or opens it again.</summary>
    public void ToggleCollapsed() => IsCollapsed = !IsCollapsed;

    /// <summary>Shows the commands of the chosen tab over the document while the ribbon is folded.</summary>
    public void OpenPage()
    {
        if (!IsCollapsed || _selected is null)
        {
            return;
        }

        _popupFrame.Width = Math.Max(200, Bounds.Width - 16);
        _popup.HorizontalOffset = 0;
        _popup.IsOpen = true;
        PageOpenChanged?.Invoke(this, true);
    }

    /// <summary>Shows the key tips of the tabs (what Alt does) and moves the keyboard focus into the ribbon.</summary>
    public void ShowKeyTips()
    {
        _focusBefore ??= _topLevel?.FocusManager?.GetFocusedElement();
        ShowTips(MatRibbonKeyTipMode.Tabs);
        (_selected is not null && _tabButtons.TryGetValue(_selected, out var button) ? button : _file).Focus(NavigationMethod.Tab);
    }

    /// <summary>Hides the key tips.</summary>
    public void HideKeyTips()
    {
        if (KeyTipMode == MatRibbonKeyTipMode.None)
        {
            return;
        }

        ShowTips(MatRibbonKeyTipMode.None);
        RestoreFocus();
    }

    /// <summary>A letter typed while key tips are shown: when it completes a key tip, what it reaches is used. True when it was a letter of a key tip.</summary>
    public bool TypeKeyTip(string letter)
    {
        if (KeyTipMode == MatRibbonKeyTipMode.None || string.IsNullOrEmpty(letter))
        {
            return false;
        }

        var typed = _typed + letter.ToUpperInvariant();
        if (_shownTips.TryGetValue(typed, out var id))
        {
            _typed = string.Empty;
            ActOn(id);
            return true;
        }

        if (_shownTips.Keys.Any(k => k.StartsWith(typed, StringComparison.Ordinal)))
        {
            _typed = typed;
            return true;
        }

        _typed = string.Empty;
        return false;
    }

    /// <summary>Builds the ribbon again from <see cref="Definition"/> (after its structure changed); the chosen tab stays chosen when it is still there.</summary>
    public void Rebuild()
    {
        HideKeyTips();
        foreach (var view in _views.Values)
        {
            view.Detach();
        }

        _views.Clear();
        _groupOf.Clear();
        _pages.Clear();
        _tabButtons.Clear();
        _tabs.Children.Clear();
        _pageHost.Child = null;
        _definition.Reindex();

        _file.Content = FileText;
        AutomationProperties.SetName(_file, FileText);
        ToolTip.SetTip(_file, FileText);
        var contextLineAdded = false;
        foreach (var tab in _definition.Tabs)
        {
            if (tab.Context is not null && !contextLineAdded)
            {
                _tabs.Children.Add(_contextLine);
                contextLineAdded = true;
            }

            var button = new RadioButton { Content = tab.Label, GroupName = _radioGroup, VerticalAlignment = VerticalAlignment.Bottom }.Themed("MatRibbon.Tab");
            if (tab.Context is not null)
            {
                button.Classes.Add("context");
            }

            AutomationProperties.SetName(button, tab.Label);
            button.IsCheckedChanged += (_, _) =>
            {
                if (!_syncingTabs && button.IsChecked == true)
                {
                    Select(tab);
                }
            };
            button.Click += (_, _) => OpenPage();
            button.DoubleTapped += (_, e) =>
            {
                e.Handled = true;
                ToggleCollapsed();
            };
            _tabButtons[tab] = button;
            _tabs.Children.Add(button);

            var groups = tab.Groups.Select(g => new RibbonGroupView(g, this)).ToList();
            foreach (var group in groups)
            {
                foreach (var view in group.AllViews())
                {
                    _views.TryAdd(view.Item.Id, view);
                    _groupOf[view] = group;
                    view.Attach();
                }
            }

            _pages[tab] = new RibbonPagePanel(tab, groups);
        }

        var keep = _selected is not null && _definition.Tabs.Contains(_selected) ? _selected : null;
        _selected = null;
        RefreshTabs(keep);
    }

    /// <summary>Runs what a command does and closes what was open for it (a menu, a flyout, the commands of a folded ribbon).</summary>
    internal void Run(Action action)
    {
        try
        {
            action();
        }
        finally
        {
            _openFlyout?.Hide();
            if (IsCollapsed && _popup.IsOpen)
            {
                _popup.IsOpen = false;
            }
        }
    }

    /// <summary>Shows a menu or a flyout of the ribbon at a control, the one open at a time.</summary>
    internal void Open(FlyoutBase flyout, Control at)
    {
        _openFlyout?.Hide();
        _openFlyout = flyout;
        var button = at as Button;
        button?.Classes.Add("open");
        flyout.Closed += (_, _) =>
        {
            button?.Classes.Remove("open");
            if (_openFlyout == flyout)
            {
                _openFlyout = null;
            }
        };
        flyout.ShowAt(at);
    }

    /// <summary>A label, an icon, a size or the visibility of an item changed: its group measures its widths again.</summary>
    internal void SizesChanged(RibbonItemView view)
    {
        if (_groupOf.TryGetValue(view, out var group))
        {
            group.Widths = null;
            (group.Parent as Control)?.InvalidateMeasure();
        }
    }

    /// <summary>The widths of the groups were measured this many times on the chosen tab (for tests: a change of state measures nothing again).</summary>
    internal int WidthMeasures => _selected is not null && _pages.TryGetValue(_selected, out var page) ? page.WidthMeasures : 0;

    /// <inheritdoc />
    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        _topLevel = TopLevel.GetTopLevel(this);
        if (_topLevel is not null)
        {
            _topLevel.AddHandler(KeyDownEvent, OnWindowKeyDown, RoutingStrategies.Tunnel);
            _topLevel.AddHandler(KeyUpEvent, OnWindowKeyUp, RoutingStrategies.Tunnel);
            _topLevel.AddHandler(PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel, handledEventsToo: true);
        }
    }

    /// <inheritdoc />
    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        if (_topLevel is not null)
        {
            _topLevel.RemoveHandler(KeyDownEvent, OnWindowKeyDown);
            _topLevel.RemoveHandler(KeyUpEvent, OnWindowKeyUp);
            _topLevel.RemoveHandler(PointerPressedEvent, OnWindowPointerPressed);
            _topLevel = null;
        }

        base.OnDetachedFromVisualTree(e);
    }

    private void RequestFile(bool byKeyboard)
    {
        HideKeyTips();
        if (_popup.IsOpen)
        {
            _popup.IsOpen = false;
        }

        FileRequested?.Invoke(this, new MatRibbonFileEventArgs(byKeyboard));
    }

    private void Select(MatRibbonTab tab)
    {
        if (_selected == tab)
        {
            return;
        }

        _selected = tab;
        _syncingTabs = true;
        foreach (var (each, button) in _tabButtons)
        {
            button.IsChecked = each == tab;
        }

        _syncingTabs = false;
        _pageHost.Child = _pages.GetValueOrDefault(tab);
        if (KeyTipMode == MatRibbonKeyTipMode.Items)
        {
            HideKeyTips();
        }

        SelectedTabChanged?.Invoke(this, tab);
    }

    private void OnContextsChanged(object? sender, EventArgs e) => RefreshTabs(_selected);

    /// <summary>Shows the tabs of the active contexts and keeps a tab chosen that is shown.</summary>
    private void RefreshTabs(MatRibbonTab? prefer)
    {
        var visible = _definition.VisibleTabs.ToHashSet();
        foreach (var (tab, button) in _tabButtons)
        {
            button.IsVisible = visible.Contains(tab);
        }

        _contextLine.IsVisible = visible.Any(t => t.Context is not null);
        var choice = prefer is not null && visible.Contains(prefer) ? prefer : _definition.VisibleTabs.FirstOrDefault();
        if (choice is null)
        {
            _selected = null;
            _pageHost.Child = null;
            return;
        }

        if (_selected != choice)
        {
            Select(choice);
        }
    }

    private void OnCollapsedChanged(AvaloniaPropertyChangedEventArgs e)
    {
        var collapsed = IsCollapsed;
        if (collapsed)
        {
            _dock.Children.Remove(_page);
            _popupFrame.Child = _page;
        }
        else
        {
            _popup.IsOpen = false;
            _popupFrame.Child = null;
            _dock.Children.Add(_page);
        }

        RefreshCollapse();
        if (!Equals(e.OldValue, e.NewValue))
        {
            CollapsedChanged?.Invoke(this, collapsed);
        }
    }

    private void RefreshCollapse()
    {
        var collapsed = IsCollapsed;
        _expand.IsVisible = collapsed;
        var pin = MatText.Get("ribbon.pin") + $" ({CollapseShortcut})";
        var fold = MatText.Get("ribbon.collapse") + $" ({CollapseShortcut})";
        _collapse.Content = new MatIcon { Icon = collapsed ? "pin" : "chevron-up", Size = 16 };
        ToolTip.SetTip(_collapse, collapsed ? pin : fold);
        AutomationProperties.SetName(_collapse, MatText.Get(collapsed ? "ribbon.pin" : "ribbon.collapse"));
        ToolTip.SetTip(_expand, pin);
        AutomationProperties.SetName(_expand, MatText.Get("ribbon.pin"));
    }

    private void OnStripWheel(object? sender, PointerWheelEventArgs e)
    {
        if (e.Delta.Y == 0)
        {
            return;
        }

        Step(e.Delta.Y > 0 ? -1 : 1, focus: false);
        e.Handled = true;
    }

    /// <summary>Chooses the tab before or after the chosen one.</summary>
    private void Step(int direction, bool focus)
    {
        var visible = _definition.VisibleTabs.ToList();
        if (visible.Count == 0)
        {
            return;
        }

        var at = _selected is null ? -1 : visible.IndexOf(_selected);
        var next = visible[Math.Clamp(at + direction, 0, visible.Count - 1)];
        Select(next);
        if (focus)
        {
            _tabButtons[next].Focus(NavigationMethod.Directional);
        }
    }

    // ---------------------------------------------------------------------------------------------------- the keyboard

    private void OnWindowKeyDown(object? sender, KeyEventArgs e)
    {
        if (!HandlesWindowKeys)
        {
            return;
        }

        if (HandlesCollapseKey && CollapseGesture.Matches(e))
        {
            ToggleCollapsed();
            e.Handled = true;
            return;
        }

        _altAlone = (e.Key is Key.LeftAlt or Key.RightAlt && (e.KeyModifiers & ~KeyModifiers.Alt) == KeyModifiers.None) || (e.Key == Key.F10 && e.KeyModifiers == KeyModifiers.None);

        // Alt held with the letter of a tab (Alt+R) goes to the tab at once, like Alt and then the letter
        if (KeyTipMode == MatRibbonKeyTipMode.None && e.KeyModifiers == KeyModifiers.Alt && Letter(e, ignoreModifiers: true) is { } direct)
        {
            ShowKeyTips();
            if (TypeKeyTip(direct))
            {
                e.Handled = true;
            }
            else
            {
                HideKeyTips();
                RestoreFocus();
            }

            return;
        }

        if (KeyTipMode != MatRibbonKeyTipMode.None)
        {
            if (e.Key == Key.Escape)
            {
                StepBack();
                e.Handled = true;
                return;
            }

            if (Letter(e) is { } letter)
            {
                TypeKeyTip(letter);
                e.Handled = true;
                return;
            }

            if (e.Key is not (Key.LeftAlt or Key.RightAlt or Key.LeftShift or Key.RightShift))
            {
                HideKeyTips();
            }

            return;
        }

        if (e.Key == Key.Escape && _popup.IsOpen)
        {
            _popup.IsOpen = false;
            e.Handled = true;
        }
    }

    private void OnWindowKeyUp(object? sender, KeyEventArgs e)
    {
        if (!HandlesWindowKeys || !_altAlone || e.Key is not (Key.LeftAlt or Key.RightAlt or Key.F10))
        {
            return;
        }

        _altAlone = false;
        if (KeyTipMode == MatRibbonKeyTipMode.None)
        {
            ShowKeyTips();
        }
        else
        {
            HideKeyTips();
            RestoreFocus();
        }

        e.Handled = true;
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _altAlone = false;
        if (KeyTipMode != MatRibbonKeyTipMode.None)
        {
            HideKeyTips();
        }
    }

    private static string? Letter(KeyEventArgs e, bool ignoreModifiers = false)
    {
        if (!ignoreModifiers && e.KeyModifiers is not (KeyModifiers.None or KeyModifiers.Shift))
        {
            return null;
        }

        if (e.KeySymbol is { Length: 1 } symbol && char.IsLetterOrDigit(symbol[0]))
        {
            return symbol.ToUpperInvariant();
        }

        return e.Key switch
        {
            >= Key.A and <= Key.Z => ((char)('A' + (e.Key - Key.A))).ToString(),
            >= Key.D0 and <= Key.D9 => ((char)('0' + (e.Key - Key.D0))).ToString(),
            >= Key.NumPad0 and <= Key.NumPad9 => ((char)('0' + (e.Key - Key.NumPad0))).ToString(),
            _ => null,
        };
    }

    /// <summary>Escape while key tips show: from the commands back to the tabs, from the tabs out of the ribbon.</summary>
    private void StepBack()
    {
        _typed = string.Empty;
        if (KeyTipMode == MatRibbonKeyTipMode.Items)
        {
            if (_popup.IsOpen)
            {
                _popup.IsOpen = false;
            }

            ShowTips(MatRibbonKeyTipMode.Tabs);
            return;
        }

        HideKeyTips();
        RestoreFocus();
    }

    private void RestoreFocus()
    {
        var before = _focusBefore;
        _focusBefore = null;
        if (before is InputElement { IsEffectivelyVisible: true } element && !this.IsVisualAncestorOf(element))
        {
            element.Focus(NavigationMethod.Tab);
        }
    }

    private void ActOn(string id)
    {
        if (id == FileId)
        {
            ShowTips(MatRibbonKeyTipMode.None);
            _focusBefore = null;
            RequestFile(byKeyboard: true);
            return;
        }

        if (KeyTipMode == MatRibbonKeyTipMode.Tabs && _definition.Tabs.FirstOrDefault(t => t.Id == id) is { } tab)
        {
            Select(tab);
            OpenPage();
            ShowTips(MatRibbonKeyTipMode.Items);
            return;
        }

        ShowTips(MatRibbonKeyTipMode.None);
        if (id.StartsWith("group:", StringComparison.Ordinal) && CurrentPage?.Groups.FirstOrDefault(g => g.Group.Id == id[6..]) is { } group)
        {
            _focusBefore = null;
            group.OpenCollapsed();
            return;
        }

        if (!_views.TryGetValue(id, out var view))
        {
            return;
        }

        view.Act();

        // a command that was used gives the focus back to the document; a field, a menu or a gallery keeps it
        if (view is ToggleView || (view is ButtonView && view is not MenuView))
        {
            RestoreFocus();
        }
        else
        {
            _focusBefore = null;
        }
    }

    private RibbonPagePanel? CurrentPage => _selected is not null ? _pages.GetValueOrDefault(_selected) : null;

    private void ShowTips(MatRibbonKeyTipMode mode)
    {
        foreach (var (target, _) in _badges)
        {
            AdornerLayer.SetAdorner(target, null);
        }

        _badges.Clear();
        _shownTips.Clear();
        _typed = string.Empty;
        KeyTipMode = mode;
        if (mode == MatRibbonKeyTipMode.Tabs)
        {
            var targets = new List<(string Id, string Label, string? KeyTip, Control Target)>
            {
                (FileId, FileText, FileKeyTip, _file),
            };
            targets.AddRange(_definition.VisibleTabs.Select(t => (t.Id, t.Label, t.KeyTip, (Control)_tabButtons[t])));
            Badges(targets, tall: true);
        }
        else if (mode == MatRibbonKeyTipMode.Items && CurrentPage is { } page)
        {
            var targets = new List<(string Id, string Label, string? KeyTip, Control Target)>();
            foreach (var group in page.Groups.Where(g => g.IsVisible))
            {
                if (group.State == MatRibbonGroupState.Collapsed)
                {
                    targets.Add(("group:" + group.Group.Id, group.Group.Label, group.Group.KeyTip, group.CollapsedButton));
                    continue;
                }

                targets.AddRange(group.AllViews()
                    .Where(v => v.TakesKeyTip && v.Item.IsVisible && v.Item.IsEnabled && v.Target.IsEffectivelyVisible)
                    .Select(v => (v.Item.Id, v.Item.Label, v.Item.KeyTip, v.Target)));
            }

            Badges(targets, tall: false);
        }
    }

    private void Badges(List<(string Id, string Label, string? KeyTip, Control Target)> targets, bool tall)
    {
        var tips = MatRibbonKeyTips.Assign(targets.Select(t => (t.Id, t.Label, t.KeyTip)));
        foreach (var (id, _, _, target) in targets)
        {
            if (!tips.TryGetValue(id, out var tip))
            {
                continue;
            }

            _shownTips[tip] = id;
            var large = tall || target.Bounds.Height > RowHeight + 4;
            var badge = new Border
            {
                Child = new TextBlock { Text = tip, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Bottom,
                Margin = new Thickness(0, 0, 0, large ? -8 : -7),
            }.Themed("MatKeyTip");
            AdornerLayer.SetIsClipEnabled(badge, false);
            AdornerLayer.SetAdorner(target, badge);
            _badges.Add((target, badge));
        }
    }

    /// <summary>The arrow keys inside the ribbon: along the tabs, into the commands and along them; Escape goes back to the tab and out of the ribbon.</summary>
    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Handled || KeyTipMode != MatRibbonKeyTipMode.None || e.Source is not Control source)
        {
            return;
        }

        var onTabs = source == _file || _tabs.IsVisualAncestorOf(source);
        if (onTabs)
        {
            switch (e.Key)
            {
                case Key.Left:
                case Key.Right:
                    Step(e.Key == Key.Left ? -1 : 1, focus: true);
                    e.Handled = true;
                    break;
                case Key.Down:
                    OpenPage();
                    Commands().FirstOrDefault()?.Focus(NavigationMethod.Directional);
                    e.Handled = true;
                    break;
                case Key.Escape:
                    RestoreFocus();
                    e.Handled = true;
                    break;
            }

            return;
        }

        if (!_page.IsVisualAncestorOf(source) || source.FindAncestorOfType<ComboBox>(includeSelf: true) is not null || source.FindAncestorOfType<TextBox>(includeSelf: true) is not null)
        {
            if (e.Key == Key.Escape && _page.IsVisualAncestorOf(source))
            {
                FocusSelectedTab();
                e.Handled = true;
            }

            return;
        }

        var commands = Commands();
        var at = commands.IndexOf(source);
        switch (e.Key)
        {
            case Key.Left or Key.Up when at > 0:
                commands[at - 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Right or Key.Down when at >= 0 && at < commands.Count - 1:
                commands[at + 1].Focus(NavigationMethod.Directional);
                e.Handled = true;
                break;
            case Key.Escape:
                FocusSelectedTab();
                e.Handled = true;
                break;
        }
    }

    private void FocusSelectedTab()
    {
        if (_popup.IsOpen)
        {
            _popup.IsOpen = false;
        }

        if (_selected is not null)
        {
            _tabButtons[_selected].Focus(NavigationMethod.Directional);
        }
    }

    /// <summary>The controls of the chosen tab that the keyboard reaches, in their order.</summary>
    private List<Control> Commands()
    {
        if (CurrentPage is not { } page)
        {
            return [];
        }

        return [.. page.GetVisualDescendants().OfType<Control>()
            .Where(c => c is Button or ComboBox or TextBox && c.Focusable && c.IsEffectivelyVisible && c.IsEffectivelyEnabled)
            .Where(c => c.GetVisualAncestors().TakeWhile(a => a != page).All(a => a is not (ComboBox or TextBox)))];
    }
}
