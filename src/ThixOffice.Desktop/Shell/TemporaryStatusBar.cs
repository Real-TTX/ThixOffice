using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;

namespace ThixOffice.Desktop.Shell;

/// <summary>
/// PLACEHOLDER until MatStatusBar (MatDesktop 0.3.0) is copied in: one slim line of texts, left and right (no zoom slider,
/// no clickable fields).
/// </summary>
public sealed class TemporaryStatusBar : Border, IOfficeStatusBar
{
    private readonly StackPanel _left = new() { Orientation = Orientation.Horizontal, Spacing = 22 };
    private readonly StackPanel _right = new() { Orientation = Orientation.Horizontal, Spacing = 22 };
    private readonly Dictionary<string, TextBlock> _items = new(StringComparer.Ordinal);

    public TemporaryStatusBar()
    {
        Height = 28;
        Padding = new Thickness(16, 0);
        BorderThickness = new Thickness(0, 1, 0, 0);
        this.Use(BackgroundProperty, "Mat.Surface");
        this.Use(BorderBrushProperty, "Mat.Border");
        var dock = new DockPanel { VerticalAlignment = VerticalAlignment.Center };
        DockPanel.SetDock(_right, Dock.Right);
        dock.Children.Add(_right);
        dock.Children.Add(_left);
        Child = dock;
    }

    public Control View => this;

    public void SetItem(string id, string text, StatusBarSide side = StatusBarSide.Left)
    {
        if (!_items.TryGetValue(id, out var block))
        {
            block = new TextBlock { FontSize = MatTokens.Type.FsSm, VerticalAlignment = VerticalAlignment.Center }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            _items[id] = block;
            (side == StatusBarSide.Right ? _right : _left).Children.Add(block);
        }

        block.Text = text;
    }

    public void RemoveItem(string id)
    {
        if (_items.Remove(id, out var block))
        {
            _left.Children.Remove(block);
            _right.Children.Remove(block);
        }
    }

    public string? TextOf(string id) => _items.TryGetValue(id, out var block) ? block.Text : null;
}
