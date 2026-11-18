// MatDesktop 0.3.0 – a copy from MatDesign, do not edit here
using System.ComponentModel;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;

namespace MatDesktop;

/// <summary>
/// The quick access of a document program: small icon buttons for the title bar (Speichern, Rückgängig, Wiederholen; <c>MatButton.Quick</c>), made from items of the ribbon's definition, so
/// that they share their state with the ribbon (Rückgängig turned off is off in both) and act through <see cref="MatRibbonDefinition.Invoke"/>. Put it into the title bar
/// (<see cref="MatTitleBar.Center"/>, beside the name of the document). Each button has the tooltip and the name of its item (<c>Rückgängig (Strg+Z)</c>, DESK-07).
/// </summary>
public sealed class MatQuickAccess : StackPanel
{
    private readonly MatRibbonDefinition _definition;
    private readonly Dictionary<MatRibbonButton, Button> _buttons = [];

    /// <summary>The quick access of the buttons <paramref name="ids"/> of <paramref name="definition"/>, in that order.</summary>
    public MatQuickAccess(MatRibbonDefinition definition, params string[] ids)
    {
        ArgumentNullException.ThrowIfNull(definition);
        _definition = definition;
        Orientation = Orientation.Horizontal;
        Spacing = 2;
        VerticalAlignment = VerticalAlignment.Center;
        foreach (var id in ids)
        {
            Add(definition.Get<MatRibbonButton>(id));
        }
    }

    /// <summary>The buttons, by the id of their item.</summary>
    public IReadOnlyDictionary<string, Button> Buttons => _buttons.ToDictionary(p => p.Key.Id, p => p.Value, StringComparer.Ordinal);

    /// <summary>Adds the button of an item at the end.</summary>
    public void Add(MatRibbonButton item)
    {
        ArgumentNullException.ThrowIfNull(item);
        var button = new Button { Content = new MatIcon { Icon = item.Icon, Size = 16 } }.Themed("MatButton.Quick");
        button.Click += (_, _) => _definition.Invoke(item);
        _buttons[item] = button;
        Children.Add(button);
        Sync(item, button);
        item.PropertyChanged += OnItemChanged;
    }

    private void OnItemChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (sender is MatRibbonButton item && _buttons.TryGetValue(item, out var button))
        {
            Sync(item, button);
        }
    }

    private static void Sync(MatRibbonButton item, Button button)
    {
        button.IsEnabled = item.IsEnabled;
        button.IsVisible = item.IsVisible;
        ((MatIcon)button.Content!).Icon = item.Icon;
        AutomationProperties.SetName(button, item.Label);
        ToolTip.SetTip(button, item.ToolTipTitle);
    }
}
