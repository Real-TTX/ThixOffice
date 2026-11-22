using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Store;

namespace ThixOffice.Pim.Desktop;

/// <summary>
/// An address field with suggestions from the contacts (ThixMail's "An", "Cc"; here for later): while the person types the last address of the field,
/// the matching contacts show below it (<see cref="PersonItems.Suggest"/>: names and addresses, favourites first); Tab, Enter or a click takes one as
/// "Name &lt;address&gt;", the field goes on after a "; ".
/// </summary>
public sealed class AddressField : UserControl
{
    private readonly ListBox _list = new() { MaxHeight = 220 };
    private readonly Popup _popup;

    public AddressField()
    {
        Box = new TextBox { Watermark = "Name oder E-Mail-Adresse" };
        _popup = new Popup { PlacementTarget = Box, Placement = PlacementMode.BottomEdgeAlignedLeft, Child = new Border { Child = _list, MinWidth = 280 }, IsLightDismissEnabled = true };
        Box.TextChanged += (_, _) => Refresh();
        Box.AddHandler(KeyDownEvent, OnKeyDown, Avalonia.Interactivity.RoutingStrategies.Tunnel);
        _list.DoubleTapped += (_, _) => Take();
        Content = new Panel { Children = { Box, _popup } };
        HorizontalAlignment = HorizontalAlignment.Stretch;
    }

    /// <summary>Where suggestions come from (text → contacts with an address); null: none.</summary>
    public Func<string, IReadOnlyList<(Person Person, string Email)>>? Suggestions { get; set; }

    public TextBox Box { get; }

    /// <summary>The suggestions shown (tests).</summary>
    public IReadOnlyList<string> Shown => _list.Items.OfType<string>().ToList();

    /// <summary>The addresses of the field ("a@x.de; Bert &lt;b@y.de&gt;" → both entries, trimmed).</summary>
    public IReadOnlyList<string> Addresses => (Box.Text ?? "").Split([';', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private string CurrentPart
    {
        get
        {
            var text = Box.Text ?? "";
            var start = text.LastIndexOfAny([';', ',']) + 1;
            return text[start..].Trim();
        }
    }

    /// <summary>
    /// Suggestions as ready entries ("Anna Berger &lt;anna@example.de&gt;") – ThixMail's recent recipients and the contacts of the account; used instead of
    /// <see cref="Suggestions"/> when set.
    /// </summary>
    public Func<string, IReadOnlyList<string>>? EntrySuggestions { get; set; }

    private void Refresh()
    {
        var part = CurrentPart;
        IReadOnlyList<string> entries = part.Length == 0 ? []
            : EntrySuggestions is { } entrySuggestions ? entrySuggestions(part)
            : Suggestions is { } suggestions ? [.. suggestions(part).Select(f => $"{f.Person.Name} <{f.Email}>")]
            : [];
        _list.ItemsSource = entries;
        _list.SelectedIndex = entries.Count > 0 ? 0 : -1;
        _popup.IsOpen = entries.Count > 0;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (!_popup.IsOpen)
        {
            return;
        }

        switch (e.Key)
        {
            case Key.Down:
                _list.SelectedIndex = Math.Min(_list.ItemCount - 1, _list.SelectedIndex + 1);
                e.Handled = true;
                break;
            case Key.Up:
                _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                e.Handled = true;
                break;
            case Key.Enter or Key.Tab:
                e.Handled = Take();
                break;
            case Key.Escape:
                _popup.IsOpen = false;
                e.Handled = true;
                break;
        }
    }

    private bool Take()
    {
        if (_list.SelectedItem is not string chosen)
        {
            return false;
        }

        var text = Box.Text ?? "";
        var start = text.LastIndexOfAny([';', ',']) + 1;
        Box.Text = (start > 0 ? text[..start] + " " : "") + chosen + "; ";
        Box.CaretIndex = Box.Text.Length;
        _popup.IsOpen = false;
        return true;
    }
}
