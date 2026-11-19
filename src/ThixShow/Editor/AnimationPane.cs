using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixOffice.Desktop.Controls;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// Der Animationsbereich at the right of the slide: the slide's effects in their order (number, start, effect, shape), with
/// "Alle wiedergeben" above; below Nach oben, Nach unten and – set apart – Entfernen. Choosing an entry selects its shape.
/// </summary>
internal sealed class AnimationPane : Border
{
    private readonly ShowEditor _editor;
    private bool _loading;

    public AnimationPane(ShowEditor editor)
    {
        _editor = editor;
        Width = 280;
        this.Use(BackgroundProperty, "Mat.Surface");
        this.Use(BorderBrushProperty, "Mat.Border");
        BorderThickness = new Thickness(1, 0, 0, 0);
        List = new ListBox { SelectionMode = SelectionMode.Single };
        Avalonia.Automation.AutomationProperties.SetName(List, "Effekte");
        List.SelectionChanged += (_, _) => OnSelected();

        var heading = new TextBlock { Text = "Animationsbereich", FontWeight = Avalonia.Media.FontWeight.SemiBold, Margin = new Thickness(0, 0, 0, 8) };
        var play = new Button { Content = new IconLabel { Icon = "play", Text = "Alle wiedergeben" } };
        play.Click += (_, _) => _editor.Preview();
        var close = new Button { Content = new MatIcon { Icon = "x", Size = 14 } }.Themed("MatButton.Icon");
        ToolTip.SetTip(close, "Schließen");
        Avalonia.Automation.AutomationProperties.SetName(close, "Animationsbereich schließen");
        close.Click += (_, _) => _editor.ToggleAnimationPane();
        var top = new DockPanel { Children = { close, heading } };
        DockPanel.SetDock(close, Dock.Right);

        Up = new Button { Content = new IconLabel { Icon = "chevron-up", Text = "Nach oben" } };
        Down = new Button { Content = new IconLabel { Icon = "chevron-down", Text = "Nach unten" } };
        Remove = new Button { Content = new IconLabel { Icon = "trash", Text = "Entfernen" } };
        Up.Click += (_, _) => Move(-1);
        Down.Click += (_, _) => Move(1);
        Remove.Click += (_, _) => RemoveSelected();
        var actions = new ActionBar { Children = { Up, Down, Remove } };
        ActionBar.SetSeparated(Remove, true);

        var layout = new Grid { RowDefinitions = new RowDefinitions("Auto,Auto,*,Auto"), Margin = new Thickness(12) };
        Grid.SetRow(play, 1);
        Grid.SetRow(List, 2);
        Grid.SetRow(actions, 3);
        play.Margin = new Thickness(0, 0, 0, 8);
        List.Margin = new Thickness(0, 0, 0, 8);
        layout.Children.Add(top);
        layout.Children.Add(play);
        layout.Children.Add(List);
        layout.Children.Add(actions);
        Child = layout;
    }

    public ListBox List { get; }

    public Button Up { get; }

    public Button Down { get; }

    public Button Remove { get; }

    /// <summary>Shows the current slide's effects (the selected shape's effect chosen).</summary>
    public void Load()
    {
        if (!IsVisible)
        {
            return;
        }

        _loading = true;
        var slide = _editor.Session.CurrentSlide;
        var effects = slide?.Timing?.MainSequence.ToList() ?? [];
        var selected = _editor.Session.Selection.Shapes.ToHashSet();
        var items = new List<string>();
        var click = 0;
        foreach (var effect in effects)
        {
            if (effect.Trigger == AnimationTrigger.OnClick)
            {
                click++;
            }

            var start = effect.Trigger switch
            {
                AnimationTrigger.OnClick => click.ToString(CultureInfo.CurrentCulture),
                AnimationTrigger.WithPrevious => "  ",
                _ => " ↳",
            };
            var name = AnimationPresets.PresetOf(effect) is { } preset ? AnimationPresets.Label(preset) : "Effekt";
            var shape = slide?.FindShape(effect.ShapeId)?.Name ?? $"Form {effect.ShapeId}";
            items.Add($"{start}  {name}: {shape}");
        }

        List.ItemsSource = items;
        var index = effects.FindIndex(effect => selected.Contains(effect.ShapeId));
        List.SelectedIndex = index;
        UpdateButtons();
        _loading = false;
    }

    private void UpdateButtons()
    {
        var index = List.SelectedIndex;
        var count = List.ItemCount;
        Up.IsEnabled = index > 0;
        Down.IsEnabled = index >= 0 && index < count - 1;
        Remove.IsEnabled = index >= 0;
    }

    private void OnSelected()
    {
        UpdateButtons();
        if (_loading || List.SelectedIndex < 0 || _editor.Session.CurrentSlide?.Timing?.MainSequence is not { } effects || List.SelectedIndex >= effects.Count)
        {
            return;
        }

        _editor.Session.SelectShapes([effects[List.SelectedIndex].ShapeId]);
    }

    private void Move(int direction)
    {
        var index = List.SelectedIndex;
        if (index < 0)
        {
            return;
        }

        _ = _editor.RunAsync("Effekt verschieben", () => _editor.Session.MoveAnimation(index, index + direction));
    }

    private void RemoveSelected()
    {
        var index = List.SelectedIndex;
        if (index >= 0)
        {
            _ = _editor.RunAsync("Effekt entfernen", () => _editor.Session.RemoveAnimation(index));
        }
    }
}
