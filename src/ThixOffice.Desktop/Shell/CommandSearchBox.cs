using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Commands;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// "Was möchten Sie tun?" in the middle of the title bar (Alt+Q): typing lists the matching commands of the window's
/// registry (<see cref="OfficeCommandRegistry.Search"/>) below the box – icon, label, shortcut; a disabled one is shown
/// but cannot run. ↑↓ choose, Enter runs, Escape closes and gives the focus back to the document. Later it searches the
/// document too (plan, section 4); the ribbon runs the same commands.
/// </summary>
public sealed class CommandSearchBox : Grid
{
    public const string Placeholder = "Was möchten Sie tun?";

    private readonly OfficeCommandRegistry _commands;
    private readonly TextBox _box;
    private readonly ListBox _list;
    private readonly Popup _popup;
    private readonly TextBlock _empty;

    public CommandSearchBox(OfficeCommandRegistry commands)
    {
        _commands = commands;
        _box = new TextBox
        {
            Theme = Ui.Theme("MatSearchBox"),
            Watermark = Placeholder,
            MaxWidth = 460,
            MinWidth = 220,
            HorizontalAlignment = HorizontalAlignment.Stretch,
        };
        AutomationProperties.SetName(_box, Placeholder);
        ToolTip.SetTip(_box, $"Befehle suchen ({(OperatingSystem.IsMacOS() ? "⌥Q" : "Alt+Q")})");

        _list = new ListBox { Theme = Ui.Theme("MatList"), MaxHeight = 380, ItemTemplate = new Avalonia.Controls.Templates.FuncDataTemplate<OfficeCommand?>((command, _) => command is null ? new TextBlock() : Row(command)) };
        _list.DoubleTapped += async (_, _) => await RunSelectedAsync();
        _empty = new TextBlock { Text = "Kein Befehl gefunden.", Margin = new Thickness(14, 10), IsVisible = false }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");

        var card = new Border
        {
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(MatTokens.Size.RadiusLg),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Children = { _list, _empty } },
        }.Use(Border.BackgroundProperty, "Mat.Surface").Use(Border.BorderBrushProperty, "Mat.Border");
        _popup = new Popup
        {
            PlacementTarget = _box,
            Placement = PlacementMode.Bottom,
            VerticalOffset = 6,
            IsLightDismissEnabled = true,
            Child = card,
        };
        _popup.Opened += (_, _) => card.Width = Math.Max(_box.Bounds.Width, 360);

        Children.Add(_box);
        Children.Add(_popup);

        _box.TextChanged += (_, _) => Search();
        _box.AddHandler(KeyDownEvent, OnBoxKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
    }

    /// <summary>The search was left with Escape: the window gives the focus to the document.</summary>
    public event EventHandler? Finished;

    /// <summary>How a chosen command runs (the window: a value command shows its field in the ribbon, any other runs).</summary>
    public Func<OfficeCommand, Task<bool>>? Run { get; init; }

    /// <summary>The text box (tests type into it).</summary>
    public TextBox Box => _box;

    /// <summary>The commands found for the current text, best first.</summary>
    public IReadOnlyList<OfficeCommand> Results { get; private set; } = [];

    public bool IsOpen => _popup.IsOpen;

    public void FocusSearch()
    {
        _box.Focus();
        _box.SelectAll();
    }

    /// <summary>Runs the chosen command (the first one when none is chosen); false when there is none or it is disabled.</summary>
    public async Task<bool> RunSelectedAsync()
    {
        var command = _list.SelectedItem as OfficeCommand ?? Results.FirstOrDefault();
        if (command is null || !command.IsEnabled)
        {
            return false;
        }

        Reset();
        if (Run is { } run)
        {
            return await run(command);
        }

        Finished?.Invoke(this, EventArgs.Empty);
        return await OfficeWindow.RunCommandAsync(this, command);
    }

    private void Search()
    {
        var text = _box.Text ?? "";
        Results = _commands.Search(text);
        _list.ItemsSource = Results;
        _list.SelectedIndex = Results.Count > 0 ? 0 : -1;
        _list.IsVisible = Results.Count > 0;
        _empty.IsVisible = Results.Count == 0;
        _popup.IsOpen = text.Trim().Length > 0;
    }

    private void Reset()
    {
        _popup.IsOpen = false;
        _box.Text = "";
        Results = [];
    }

    private async void OnBoxKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Down when Results.Count > 0:
                _list.SelectedIndex = Math.Min(Results.Count - 1, _list.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Up when Results.Count > 0:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Enter:
                e.Handled = true;
                await RunSelectedAsync();
                break;
            case Key.Escape:
                e.Handled = true;
                Reset();
                Finished?.Invoke(this, EventArgs.Empty);
                break;
        }
    }

    private static Control Row(OfficeCommand command)
    {
        var icon = new MatIcon { Icon = command.Icon ?? "chevron-right", Size = 16, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        var label = new TextBlock { Text = command.Label, VerticalAlignment = VerticalAlignment.Center };
        var shortcut = new TextBlock
        {
            Text = command.CurrentShortcut is { } keys ? Ui.ShortcutText(keys) : "",
            FontSize = MatTokens.Type.FsSm,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(16, 0, 4, 0),
        }.Use(TextBlock.ForegroundProperty, "Mat.TextFaint");
        var row = new DockPanel { Opacity = command.IsEnabled ? 1 : 0.5 };
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(shortcut, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(shortcut);
        row.Children.Add(label);
        ToolTip.SetTip(row, command.Description);
        return row;
    }
}
