using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Controls;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// PLACEHOLDER until MatRibbon (MatDesktop 0.3.0) is copied in – do not grow it into a ribbon. A row of tab headers
/// ("Datei" opens the backstage) and, below it, the commands of the chosen tab as plain buttons, group by group with the
/// group's name under them. It reads the same <see cref="RibbonLayout"/> and runs the same commands the ribbon will.
/// </summary>
public sealed class TemporaryCommandBar : Border, IOfficeRibbon
{
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 2, Margin = new Thickness(10, 4, 10, 0) };
    private readonly StackPanel _groups = new() { Orientation = Orientation.Horizontal, Margin = new Thickness(10, 4, 10, 6) };
    private readonly Button _collapse;
    private readonly List<(OfficeCommand Command, Button Button)> _bound = [];
    private RibbonLayout _layout = new([]);
    private OfficeCommandRegistry? _commands;
    private string? _selectedTabId;
    private bool _isCollapsed;

    public TemporaryCommandBar()
    {
        BorderThickness = new Thickness(0, 0, 0, 1);
        this.Use(BackgroundProperty, "Mat.Surface");
        this.Use(BorderBrushProperty, "Mat.Border");

        _collapse = new Button { Theme = Ui.Theme("MatButton.Icon"), VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 4, 10, 0) };
        _collapse.Click += (_, _) =>
        {
            IsCollapsed = !IsCollapsed;
            CollapsedChanged?.Invoke(this, IsCollapsed);
        };

        var header = new DockPanel();
        DockPanel.SetDock(_collapse, Dock.Right);
        header.Children.Add(_collapse);
        header.Children.Add(_tabs);

        var groupsScroller = new ScrollViewer
        {
            Content = _groups,
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
        };
        var panel = new DockPanel();
        DockPanel.SetDock(header, Dock.Top);
        panel.Children.Add(header);
        panel.Children.Add(groupsScroller);
        Child = panel;
        Refresh();
    }

    public event EventHandler? FileTabInvoked;

    public event EventHandler<bool>? CollapsedChanged;

    public Control View => this;

    public string? SelectedTabId
    {
        get => _selectedTabId;
        set
        {
            _selectedTabId = value;
            Refresh();
        }
    }

    public bool IsCollapsed
    {
        get => _isCollapsed;
        set
        {
            _isCollapsed = value;
            Refresh();
        }
    }

    /// <summary>The buttons of the tab shown, by command id (tests).</summary>
    public IReadOnlyDictionary<string, Button> CommandButtons => _bound.ToDictionary(pair => pair.Command.Id, pair => pair.Button);

    public void Bind(RibbonLayout layout, OfficeCommandRegistry commands)
    {
        if (_commands is not null)
        {
            _commands.StateChanged -= OnStateChanged;
        }

        _layout = layout;
        _commands = commands;
        _commands.StateChanged += OnStateChanged;
        _selectedTabId ??= layout.Tabs.FirstOrDefault()?.Id;
        Refresh();
    }

    private void OnStateChanged(object? sender, EventArgs e) => Ui.Post(UpdateStates);

    private void Refresh()
    {
        _tabs.Children.Clear();
        var file = TabButton("Datei", isSelected: false, isFile: true);
        file.Click += (_, _) => FileTabInvoked?.Invoke(this, EventArgs.Empty);
        _tabs.Children.Add(file);
        foreach (var tab in _layout.Tabs)
        {
            var button = TabButton(tab.Header, tab.Id == _selectedTabId && !_isCollapsed, isFile: false);
            button.Click += (_, _) =>
            {
                _selectedTabId = tab.Id;
                if (_isCollapsed)
                {
                    _isCollapsed = false;
                    CollapsedChanged?.Invoke(this, false);
                }

                Refresh();
            };
            _tabs.Children.Add(button);
        }

        _collapse.Content = new MatIcon { Icon = _isCollapsed ? "chevron-down" : "chevron-up", Size = 16 };
        var collapseText = Ui.Tooltip(_isCollapsed ? "Menüband anzeigen" : "Menüband reduzieren", "Ctrl+F1");
        ToolTip.SetTip(_collapse, collapseText);
        AutomationProperties.SetName(_collapse, collapseText);

        _groups.Children.Clear();
        _bound.Clear();
        _groups.IsVisible = !_isCollapsed;
        var selected = _layout.Tabs.FirstOrDefault(tab => tab.Id == _selectedTabId);
        if (selected is null || _commands is null)
        {
            return;
        }

        foreach (var group in selected.Groups)
        {
            // A large item is a column of its own; small ones stack in columns of up to three (like a ribbon group).
            var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 4 };
            StackPanel? column = null;
            foreach (var item in group.Items)
            {
                if (_commands.Find(item.CommandId) is not { } command)
                {
                    continue;
                }

                var button = CommandButton(command, item.Size);
                _bound.Add((command, button));
                if (item.Size == RibbonItemSize.Large)
                {
                    buttons.Children.Add(button);
                    column = null;
                    continue;
                }

                if (column is null || column.Children.Count == 3)
                {
                    column = new StackPanel { Spacing = 2 };
                    buttons.Children.Add(column);
                }

                button.HorizontalAlignment = HorizontalAlignment.Stretch;
                button.HorizontalContentAlignment = HorizontalAlignment.Left;
                column.Children.Add(button);
            }

            var caption = new TextBlock { Text = group.Header, FontSize = MatTokens.Type.FsXs, HorizontalAlignment = HorizontalAlignment.Center, Margin = new Thickness(0, 4, 0, 0) }
                .Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            var groupPanel = new DockPanel { Margin = new Thickness(6, 0, 10, 0) };
            DockPanel.SetDock(caption, Dock.Bottom);
            groupPanel.Children.Add(caption);
            groupPanel.Children.Add(buttons);
            if (_groups.Children.Count > 0)
            {
                _groups.Children.Add(new Border { Width = 1, Margin = new Thickness(0, 4, 0, 4) }.Use(BackgroundProperty, "Mat.Border"));
            }

            _groups.Children.Add(groupPanel);
        }

        UpdateStates();
    }

    private void UpdateStates()
    {
        foreach (var (command, button) in _bound)
        {
            button.IsEnabled = command.IsEnabled;
            if (command.IsCheckable)
            {
                button.Classes.Set("checked", command.Checked);
                button.Use(BackgroundProperty, command.Checked ? "Mat.AccentSoft" : "Mat.Surface");
            }
        }
    }

    private Button CommandButton(OfficeCommand command, RibbonItemSize size)
    {
        var tooltip = Ui.Tooltip(command.Label, command.Shortcut);
        Control content = size == RibbonItemSize.Large
            ? new StackPanel
            {
                Spacing = 4,
                Children =
                {
                    new MatIcon { Icon = command.Icon, Size = 22, HorizontalAlignment = HorizontalAlignment.Center },
                    new TextBlock { Text = command.Label, FontSize = MatTokens.Type.FsSm, HorizontalAlignment = HorizontalAlignment.Center },
                },
            }
            : new IconLabel { Icon = command.Icon, Text = command.Label };
        var button = new Button
        {
            Content = content,
            Theme = Ui.Theme("MatButton.Ghost"),
            MinHeight = size == RibbonItemSize.Large ? 62 : 0,
            VerticalAlignment = VerticalAlignment.Top,
        };
        ToolTip.SetTip(button, command.Description is null ? tooltip : $"{tooltip}\n{command.Description}");
        AutomationProperties.SetName(button, command.Label);
        button.Click += async (_, _) => await OfficeWindow.RunCommandAsync(button, command);
        return button;
    }

    private static Button TabButton(string header, bool isSelected, bool isFile)
    {
        var text = new TextBlock { Text = header, FontWeight = isSelected || isFile ? FontWeight.SemiBold : FontWeight.Normal };
        if (isFile || isSelected)
        {
            text.Use(TextBlock.ForegroundProperty, "Mat.AccentText");
        }

        var button = new Button { Content = text, Theme = Ui.Theme("MatButton.Ghost"), Padding = new Thickness(12, 4) };
        if (isSelected)
        {
            button.Use(BackgroundProperty, "Mat.AccentSoft");
        }

        AutomationProperties.SetName(button, header);
        return button;
    }
}
