using System.Globalization;
using Avalonia.Input;
using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Commands;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// Items of a ribbon that stand for a command of the window (<see cref="OfficeCommandRegistry"/>): the item's id is the
/// command id; label, icon, shortcut and tooltip come from the command when the binding is made
/// (<see cref="OfficeRibbonBinding"/>). Use them in <see cref="OfficeApplication.CreateRibbon"/>; an item of MatDesktop made
/// by hand with a command id as its id is bound just the same (its own texts win). Do not give a bound item a callback of
/// its own (<c>Command</c>, <c>Toggled</c> …): the binding runs the command.
/// </summary>
public static class OfficeRibbonItems
{
    /// <summary>A button that runs the command (Large for the main command of a group).</summary>
    public static MatRibbonButton Button(string commandId, MatRibbonSize size = MatRibbonSize.Medium) => new(commandId, "") { Size = size };

    /// <summary>A toggle that shows <see cref="OfficeCommand.IsChecked"/>; toggles with one <paramref name="groupName"/> are one choice (Linksbündig, Zentriert …).</summary>
    public static MatRibbonToggle Toggle(string commandId, string? groupName = null, MatRibbonSize size = MatRibbonSize.Medium) =>
        new(commandId, "") { Size = size, GroupName = groupName };

    /// <summary>A combo box of an <see cref="OfficeValueCommand"/> (Schriftart, Schriftgrad); no options: the command's <see cref="OfficeValueCommand.Options"/>.</summary>
    public static MatRibbonComboBox ComboBox(string commandId, double width = 120, bool isEditable = false, IEnumerable<string>? options = null) =>
        new(commandId, "", options ?? []) { Width = width, IsEditable = isEditable };

    /// <summary>A number field of an <see cref="OfficeValueCommand"/> of the kind Number.</summary>
    public static MatRibbonNumber Number(string commandId, double minimum, double maximum, double step = 1, string? unit = null, int decimals = 0) =>
        new(commandId, "") { Minimum = minimum, Maximum = maximum, Step = step, Unit = unit, Decimals = decimals };

    /// <summary>A colour button of an <see cref="OfficeValueCommand"/> of the kind Color; the palette is the document's (<c>#rrggbb</c>, from the app's core).</summary>
    public static MatRibbonColorPicker ColorPicker(string commandId, IEnumerable<string> palette, string? automaticLabel = "Automatisch") =>
        new(commandId, "", null, palette) { AutomaticLabel = automaticLabel };

    /// <summary>A gallery of an <see cref="OfficeValueCommand"/> of the kind Choice (Formatvorlagen): the value is the id of the chosen entry.</summary>
    public static MatRibbonGallery Gallery(string commandId, IEnumerable<MatRibbonGalleryEntry> entries) => new(commandId, "", null, entries);
}

/// <summary>
/// Connects a <see cref="MatRibbonDefinition"/> (an app's ribbon, made per window) with the window's commands by id, so that
/// the ribbon, the quick access, the search box and the keyboard are one code path:
/// <list type="bullet">
/// <item>An item whose id is no command of the window and that has no callback of its own is LEFT OUT (no dead buttons):
/// an app may describe commands its editor brings later; <see cref="MissingCommandIds"/> lists them. Groups and tabs that
/// end up empty go too.</item>
/// <item>Label, icon, shortcut (as the system writes it: Strg+S, ⌘S) and tooltip default to the command's.</item>
/// <item>Using an item runs its command through <see cref="OfficeWindow.RunCommandAsync"/>; a combo box, a number, a colour
/// or a gallery hands its value to the <see cref="OfficeValueCommand"/>.</item>
/// <item>On <see cref="OfficeCommandRegistry.StateChanged"/> (and after every use) enabled, checked and the values are
/// pushed into the items; MatRibbon touches only the controls whose state changed.</item>
/// </list>
/// </summary>
public sealed class OfficeRibbonBinding
{
    private readonly MatRibbonDefinition _definition;
    private readonly OfficeCommandRegistry _commands;
    private readonly MatRibbon _ribbon;
    private readonly List<(MatRibbonItem Item, OfficeCommand Command)> _bound = [];
    private readonly List<string> _missing = [];

    private OfficeRibbonBinding(MatRibbonDefinition definition, OfficeCommandRegistry commands, MatRibbon ribbon)
    {
        _definition = definition;
        _commands = commands;
        _ribbon = ribbon;
    }

    /// <summary>The ids of the items that were left out because the window has no such command (yet).</summary>
    public IReadOnlyList<string> MissingCommandIds => _missing;

    /// <summary>
    /// Binds <paramref name="definition"/> (and <paramref name="extraItems"/> outside its tabs, e.g. the quick access) to
    /// <paramref name="commands"/>, leaves out what has no command, and sets it into <paramref name="ribbon"/>.
    /// </summary>
    public static OfficeRibbonBinding Bind(MatRibbonDefinition definition, OfficeCommandRegistry commands, MatRibbon ribbon, params MatRibbonItem[] extraItems)
    {
        var binding = new OfficeRibbonBinding(definition, commands, ribbon);
        binding.Prune();
        foreach (var item in definition.AllItems().Concat(extraItems.SelectMany(Walk)))
        {
            binding.Attach(item);
        }

        definition.Reindex();
        definition.Invoked += binding.OnInvoked;
        commands.StateChanged += (_, _) =>
        {
            if (Dispatcher.UIThread.CheckAccess())
            {
                binding.Sync();
            }
            else
            {
                Dispatcher.UIThread.Post(binding.Sync);
            }
        };
        binding.Sync();
        ribbon.Definition = definition;
        return binding;
    }

    /// <summary>The command an item is bound to, or null (an item of the app's own, a stack, a row).</summary>
    public OfficeCommand? CommandOf(MatRibbonItem item) => _bound.FirstOrDefault(pair => ReferenceEquals(pair.Item, item)).Command;

    /// <summary>
    /// Shows the item of a command in the ribbon and puts the keyboard into it (the search found a value command: the user
    /// types the font size there). False when the ribbon has no visible item of it.
    /// </summary>
    public bool Reveal(string commandId)
    {
        var tab = _definition.VisibleTabs.FirstOrDefault(candidate => candidate.Groups.SelectMany(group => group.Items).SelectMany(Walk).Any(item => item.Id == commandId));
        if (tab is null)
        {
            return false;
        }

        _ribbon.SelectTab(tab.Id);
        if (_ribbon.IsCollapsed)
        {
            _ribbon.OpenPage();
        }

        _ribbon.UpdateLayout();
        return _ribbon.ViewOf(commandId) is { IsEffectivelyVisible: true } view && view.Focus(NavigationMethod.Tab);
    }

    /// <summary>Pushes enabled, checked and the values of the commands into their items.</summary>
    public void Sync()
    {
        foreach (var (item, command) in _bound)
        {
            item.IsEnabled = command.IsEnabled;
            if (item is MatRibbonToggle toggle)
            {
                toggle.IsChecked = command.Checked;
            }

            if (command is not OfficeValueCommand valueCommand)
            {
                continue;
            }

            var value = valueCommand.Value;
            switch (item)
            {
                case MatRibbonComboBox combo:
                    combo.Text = value;
                    break;
                case MatRibbonColorPicker picker:
                    picker.Color = value;
                    break;
                case MatRibbonGallery gallery:
                    gallery.SelectedId = value;
                    break;
                case MatRibbonNumber number when double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed):
                    number.Value = parsed;
                    break;
            }
        }
    }

    private async void OnInvoked(object? sender, MatRibbonItem item)
    {
        var command = CommandOf(item);
        if (command is null)
        {
            return;
        }

        if (command is OfficeValueCommand valueCommand && ValueOf(item) is { } value)
        {
            await OfficeWindow.RunAsync(_ribbon, command.Label, () => valueCommand.ApplyAsync(value.Text));
        }
        else
        {
            await OfficeWindow.RunCommandAsync(_ribbon, command);
        }

        Sync(); // a toggle the command did not turn on, a value it refused: the item shows the truth again
    }

    /// <summary>The value an item hands to its value command, or null for an item without one (a plain button).</summary>
    private static ItemValue? ValueOf(MatRibbonItem item) => item switch
    {
        MatRibbonComboBox combo => new ItemValue(combo.Text),
        MatRibbonColorPicker picker => new ItemValue(picker.Color),
        MatRibbonGallery gallery => new ItemValue(gallery.SelectedId),
        MatRibbonNumber number => new ItemValue(number.Value.ToString(CultureInfo.InvariantCulture)),
        _ => null,
    };

    private void Attach(MatRibbonItem item)
    {
        if (item is MatRibbonStack or MatRibbonRow or MatRibbonSeparator || _commands.Find(item.Id) is not { } command)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(item.Label))
        {
            item.Label = command.Label;
        }

        item.Icon ??= command.Icon;
        item.ToolTip ??= command.Description;
        if (item.Shortcut is null && command.CurrentShortcut is { } shortcut)
        {
            item.Shortcut = Ui.ShortcutText(shortcut);
        }

        if (item is MatRibbonComboBox { Options.Count: 0 } combo && command is OfficeValueCommand valueCommand)
        {
            combo.Options.AddRange(valueCommand.Options);
        }

        _bound.Add((item, command));
    }

    // ===== Leaving out what has no command =====

    private void Prune()
    {
        foreach (var tab in _definition.Tabs)
        {
            foreach (var group in tab.Groups)
            {
                Filter(group.Items);
            }

            tab.Groups.RemoveAll(group => !group.Items.Any(item => item is not MatRibbonSeparator));
        }

        _definition.Tabs.RemoveAll(tab => tab.Groups.Count == 0);
    }

    /// <summary>Removes the items to leave out and the separators that no longer separate anything.</summary>
    private void Filter(List<MatRibbonItem> items)
    {
        items.RemoveAll(item => !Keep(item));
        for (var index = items.Count - 1; index >= 0; index--)
        {
            var isSeparator = items[index] is MatRibbonSeparator;
            var atEdge = index == 0 || index == items.Count - 1;
            if (isSeparator && (atEdge || items[index - 1] is MatRibbonSeparator))
            {
                items.RemoveAt(index);
            }
        }
    }

    private bool Keep(MatRibbonItem item)
    {
        switch (item)
        {
            case MatRibbonSeparator or MatRibbonCustom:
                return true;
            case MatRibbonStack stack:
                Filter(stack.Items);
                return stack.Items.Any(child => child is not MatRibbonSeparator);
            case MatRibbonRow row:
                Filter(row.Items);
                return row.Items.Any(child => child is not MatRibbonSeparator);
            case MatRibbonMenuButton menu:
                Filter(menu.Menu);
                break;
            case MatRibbonGallery gallery:
                Filter(gallery.Menu);
                break;
        }

        if (_commands.Find(item.Id) is not null || HasActionOfItsOwn(item))
        {
            return true;
        }

        _missing.Add(item.Id);
        return false;
    }

    private static bool HasActionOfItsOwn(MatRibbonItem item) => item switch
    {
        MatRibbonColorPicker picker => picker.ColorChosen is not null,
        MatRibbonMenuButton menu => menu.Command is not null || menu.Menu.Count > 0,
        MatRibbonToggle toggle => toggle.Toggled is not null,
        MatRibbonButton button => button.Command is not null,
        MatRibbonComboBox combo => combo.Committed is not null,
        MatRibbonNumber number => number.Changed is not null,
        MatRibbonGallery gallery => gallery.Chosen is not null,
        _ => false,
    };

    private static IEnumerable<MatRibbonItem> Walk(MatRibbonItem item) => item.Children.SelectMany(Walk).Prepend(item);

    private sealed record ItemValue(string? Text);
}
