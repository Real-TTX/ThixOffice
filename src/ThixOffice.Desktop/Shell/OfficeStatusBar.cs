using Avalonia.Controls;
using MatDesktop;

namespace ThixOffice.Desktop.Shell;

/// <summary>Where a text of the status bar stands.</summary>
public enum StatusBarSide
{
    Left = 0,
    Right = 1,
}

/// <summary>
/// The status bar at the foot of a document window: MatDesktop's <see cref="MatStatusBar"/> with texts by id ("120 Wörter"
/// left; right before the zoom) and the zoom (minus, slider, plus, percent), which the window ties to
/// <see cref="OfficeEditor.Zoom"/>. The editor writes the texts (<see cref="OfficeEditor.UpdateStatus"/>).
/// </summary>
public sealed class OfficeStatusBar
{
    private readonly Dictionary<string, TextBlock> _items = new(StringComparer.Ordinal);

    public OfficeStatusBar()
    {
        View.MinZoom = OfficeEditor.MinZoom;
        View.MaxZoom = OfficeEditor.MaxZoom;
    }

    /// <summary>The control in the window.</summary>
    public MatStatusBar View { get; } = new();

    /// <summary>Shows or replaces the text with this id (a new id is added at the end of its side).</summary>
    public void SetItem(string id, string text, StatusBarSide side = StatusBarSide.Left)
    {
        if (_items.TryGetValue(id, out var block))
        {
            block.Text = text;
            return;
        }

        block = MatStatusBar.Text(text);
        _items[id] = block;
        (side == StatusBarSide.Right ? View.Right : View.Left).Add(block);
    }

    public void RemoveItem(string id)
    {
        if (_items.Remove(id, out var block))
        {
            View.Left.Remove(block);
            View.Right.Remove(block);
        }
    }

    /// <summary>The text with this id, or null (tests).</summary>
    public string? TextOf(string id) => _items.TryGetValue(id, out var block) ? block.Text : null;
}
