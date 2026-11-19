using Avalonia.Controls;
using MatDesktop;
using ThixOffice.Core.Commands;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// A context menu made of a window's commands (label, icon, shortcut from the command; null = a separator; a nested list = a
/// submenu): every app's right-click menus. The entries follow the commands' state when the menu opens; entries listed in
/// <c>hiddenWhenDisabled</c> only show when they apply (Link öffnen only on a link). Commands the window lacks are left out.
/// </summary>
public static class OfficeCommandMenu
{
    /// <summary>A submenu entry: its label, icon and the command ids in it.</summary>
    public sealed record Submenu(string Label, string? Icon, IReadOnlyList<string?> Ids);

    public static ContextMenu Create(OfficeWindow window, IEnumerable<object?> entries, IReadOnlyCollection<string>? hiddenWhenDisabled = null)
    {
        var menu = new ContextMenu();
        var items = new List<(MenuItem Item, OfficeCommand Command)>();
        foreach (var entry in entries)
        {
            Add(window, menu.Items, entry, items);
        }

        menu.Opening += (_, _) =>
        {
            foreach (var (item, command) in items)
            {
                item.IsEnabled = command.IsEnabled;
                if (hiddenWhenDisabled?.Contains(command.Id) == true)
                {
                    item.IsVisible = item.IsEnabled;
                }

                if (command.IsCheckable)
                {
                    item.ToggleType = MenuItemToggleType.CheckBox;
                    item.IsChecked = command.Checked;
                }
            }
        };
        return menu;
    }

    private static void Add(OfficeWindow window, ItemCollection target, object? entry, List<(MenuItem, OfficeCommand)> items)
    {
        switch (entry)
        {
            case null:
                target.Add(new Separator());
                return;
            case Submenu submenu:
                var parent = new MenuItem { Header = submenu.Label, Icon = submenu.Icon is null ? null : new MatIcon { Icon = submenu.Icon, Size = 16 } };
                foreach (var id in submenu.Ids)
                {
                    Add(window, parent.Items, id, items);
                }

                if (parent.Items.Count > 0)
                {
                    target.Add(parent);
                }

                return;
            case string id when window.Commands.Find(id) is { } command:
                var item = new MenuItem
                {
                    Header = command.Label,
                    Icon = command.Icon is null ? null : new MatIcon { Icon = command.Icon, Size = 16 },
                    InputGesture = Ui.Gesture(command.CurrentShortcut),
                };
                item.Click += async (_, _) =>
                {
                    await OfficeWindow.RunCommandAsync(window, command);
                    window.FocusDocument();
                };
                items.Add((item, command));
                target.Add(item);
                return;
        }
    }
}
