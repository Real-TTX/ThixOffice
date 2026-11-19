using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;

namespace ThixOffice.Desktop.Controls;

/// <summary>
/// The shell of every app's Suchen / Ersetzen: a slim, non-modal bar at the top right of the document (the document stays
/// usable): the search text with the number of hits, Zurück / Weiter, Schließen, the app's options below, and – for Ersetzen –
/// the replacement with Ersetzen and Alle ersetzen. Enter finds the next hit, Umschalt+Enter the previous one, Enter in the
/// replacement replaces, Escape closes the bar and gives the keyboard back to the document. The app supplies the search
/// (ThixWrite: text in paragraphs; ThixCalc: cells) by overriding the abstract members.
/// </summary>
public abstract class FindBarBase : Border
{
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 96, Margin = new Thickness(8, 0, 4, 0), FontSize = 12 };
    private readonly Grid _replaceRow;

    protected FindBarBase(string watermark, params Control[] options)
    {
        IsVisible = false;
        Width = 452;
        Padding = new Thickness(12, 10);
        CornerRadius = new CornerRadius(MatTokens.Size.RadiusSm * 2);
        BorderThickness = new Thickness(1);
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 16, Color = ShadowColor() });
        this.Use(BackgroundProperty, "Mat.Surface").Use(BorderBrushProperty, "Mat.Border");
        _count.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");

        SearchBox = new TextBox { Watermark = watermark, MinWidth = 200 };
        AutomationProperties.SetName(SearchBox, "Suchen nach");
        SearchBox.TextChanged += (_, _) => SearchChanged();
        ReplacementBox = new TextBox { Watermark = "Ersetzen durch", MinWidth = 200 };
        AutomationProperties.SetName(ReplacementBox, "Ersetzen durch");

        var previous = IconButton("chevron-up", "Vorheriges Ergebnis (Umschalt+Eingabe)", () => FindNext(forward: false));
        var next = IconButton("chevron-down", "Nächstes Ergebnis (Eingabe)", () => FindNext(forward: true));
        var close = IconButton("x", "Schließen (Esc)", Close);
        var searchRow = Row(SearchBox, previous, next, _count, close);

        var replace = TextButton("Ersetzen", ReplaceOne);
        var replaceAll = TextButton("Alle ersetzen", () => ReplaceAll());
        _replaceRow = Row(ReplacementBox, replace, replaceAll);
        _replaceRow.Margin = new Thickness(0, 8, 0, 0);

        var optionPanel = new WrapPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(2, 8, 0, 0) };
        foreach (var option in options)
        {
            option.Margin = new Thickness(0, 0, 16, 4);
            optionPanel.Children.Add(option);
        }

        Child = new StackPanel { Children = { searchRow, _replaceRow, optionPanel } };
        KeyDown += OnKeyDown;
    }

    /// <summary>The search field (tests type into it).</summary>
    public TextBox SearchBox { get; }

    /// <summary>The replacement field.</summary>
    public TextBox ReplacementBox { get; }

    /// <summary>"Treffer 2 von 5", "Keine Treffer" (tests).</summary>
    public string CountText => _count.Text ?? "";

    public bool IsReplaceVisible => _replaceRow.IsVisible;

    /// <summary>The search text, or null when the field is empty.</summary>
    protected string? SearchText => string.IsNullOrEmpty(SearchBox.Text) ? null : SearchBox.Text;

    protected string ReplacementText => ReplacementBox.Text ?? "";

    /// <summary>Shows the bar (with the replacement for Ersetzen), takes a short selection as the search text, puts the keyboard into the search.</summary>
    public void Open(bool replace)
    {
        IsVisible = true;
        _replaceRow.IsVisible = replace;
        if (SelectedSearchText() is { Length: > 0 and < 80 } selected)
        {
            SearchBox.Text = selected;
        }

        SearchBox.Focus();
        SearchBox.SelectAll();
        UpdateCount();
    }

    /// <summary>Hides the bar and gives the keyboard back to the document.</summary>
    public void Close()
    {
        IsVisible = false;
        FocusDocument();
    }

    /// <summary>Selects the next (or previous) hit; false when there is none.</summary>
    public abstract bool FindNext(bool forward = true);

    /// <summary>Ersetzen: replaces the current hit and selects the next one.</summary>
    public abstract void ReplaceOne();

    /// <summary>Alle ersetzen: one undo step; returns how many (the count shows it).</summary>
    public int ReplaceAll()
    {
        var replaced = ReplaceEverything();
        if (replaced >= 0)
        {
            SetCount(replaced == 1 ? "1 ersetzt" : $"{replaced} ersetzt");
        }

        return Math.Max(0, replaced);
    }

    /// <summary>Replaces every hit; -1 when there is nothing to search for.</summary>
    protected abstract int ReplaceEverything();

    /// <summary>The text of a short selection to search for when the bar opens, or null.</summary>
    protected abstract string? SelectedSearchText();

    /// <summary>The search text or an option changed: show the first hit at once.</summary>
    protected abstract void SearchChanged();

    /// <summary>Writes the number of hits (<see cref="SetCount"/>).</summary>
    protected abstract void UpdateCount();

    protected abstract void FocusDocument();

    protected void SetCount(string text) => _count.Text = text;

    /// <summary>"Treffer 2 von 5", "1 Treffer", "Keine Treffer".</summary>
    protected static string CountOf(int hits, int current) =>
        hits == 0 ? "Keine Treffer" : current >= 0 ? $"Treffer {current + 1} von {hits}" : hits == 1 ? "1 Treffer" : $"{hits} Treffer";

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter when ReplacementBox.IsFocused:
                ReplaceOne();
                e.Handled = true;
                break;
            case Key.Enter:
                FindNext(!e.KeyModifiers.HasFlag(KeyModifiers.Shift));
                e.Handled = true;
                break;
        }
    }

    private static Grid Row(TextBox field, params Control[] controls)
    {
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("*" + string.Concat(Enumerable.Repeat(",Auto", controls.Length))) };
        row.Children.Add(field);
        for (var index = 0; index < controls.Length; index++)
        {
            Grid.SetColumn(controls[index], index + 1);
            controls[index].Margin = controls[index].Margin == default ? new Thickness(6, 0, 0, 0) : controls[index].Margin;
            row.Children.Add(controls[index]);
        }

        return row;
    }

    private static Button IconButton(string icon, string tooltip, Action action)
    {
        var button = new Button { Content = new MatIcon { Icon = icon, Size = 16 }, Padding = new Thickness(7), VerticalAlignment = VerticalAlignment.Center }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(button, tooltip);
        ToolTip.SetTip(button, tooltip);
        button.Click += (_, _) => action();
        return button;
    }

    private static Button TextButton(string text, Action action)
    {
        var button = new Button { Content = text, VerticalAlignment = VerticalAlignment.Center };
        button.Click += (_, _) => action();
        return button;
    }

    private static Color ShadowColor()
    {
        var overlay = Ui.Brush("Mat.Overlay") is ISolidColorBrush solid ? solid.Color : default;
        return new Color((byte)(overlay.A * 0.3), overlay.R, overlay.G, overlay.B);
    }
}
