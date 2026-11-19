using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Desktop;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// Suchen / Ersetzen: a slim, non-modal bar at the top right of the pages (the document stays usable): the search text with
/// the number of hits, Zurück / Weiter, Groß-/Kleinschreibung, Nur ganze Wörter, and – for Ersetzen – the replacement with
/// Ersetzen and Alle ersetzen. The current hit is the document's selection (highlighted, scrolled into view). Enter finds the
/// next hit, Umschalt+Enter the previous one, Escape closes the bar and gives the keyboard back to the document.
/// </summary>
public sealed class FindBar : Border
{
    private readonly WriteEditor _editor;
    private readonly TextBox _search;
    private readonly TextBox _replacement;
    private readonly CheckBox _matchCase = new() { Content = "Groß-/Kleinschreibung" };
    private readonly CheckBox _wholeWord = new() { Content = "Nur ganze Wörter" };
    private readonly TextBlock _count = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 96, Margin = new Thickness(8, 0, 4, 0), FontSize = 12 };
    private readonly Grid _replaceRow;

    public FindBar(WriteEditor editor)
    {
        _editor = editor;
        IsVisible = false;
        Width = 452;
        Padding = new Thickness(12, 10);
        CornerRadius = new CornerRadius(MatTokens.Size.RadiusSm * 2);
        BorderThickness = new Thickness(1);
        BoxShadow = new BoxShadows(new BoxShadow { OffsetY = 4, Blur = 16, Color = ShadowColor() });
        this.Use(BackgroundProperty, "Mat.Surface").Use(BorderBrushProperty, "Mat.Border");
        _count.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");

        _search = new TextBox { Watermark = "Im Dokument suchen", MinWidth = 200 };
        AutomationProperties.SetName(_search, "Suchen nach");
        _search.TextChanged += (_, _) => OnSearchChanged();
        _replacement = new TextBox { Watermark = "Ersetzen durch", MinWidth = 200 };
        AutomationProperties.SetName(_replacement, "Ersetzen durch");
        _matchCase.IsCheckedChanged += (_, _) => OnSearchChanged();
        _wholeWord.IsCheckedChanged += (_, _) => OnSearchChanged();

        var previous = IconButton("chevron-up", "Vorheriges Ergebnis (Umschalt+Eingabe)", () => FindNext(forward: false));
        var next = IconButton("chevron-down", "Nächstes Ergebnis (Eingabe)", () => FindNext(forward: true));
        var close = IconButton("x", "Schließen (Esc)", Close);
        var searchRow = Row(_search, previous, next, _count, close);

        var replace = TextButton("Ersetzen", ReplaceOne);
        var replaceAll = TextButton("Alle ersetzen", () => ReplaceAll());
        _replaceRow = Row(_replacement, replace, replaceAll);
        _replaceRow.Margin = new Thickness(0, 8, 0, 0);

        var options = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Margin = new Thickness(2, 8, 0, 0), Children = { _matchCase, _wholeWord } };
        Child = new StackPanel { Children = { searchRow, _replaceRow, options } };
        KeyDown += OnKeyDown;
    }

    /// <summary>The search field (tests type into it).</summary>
    public TextBox SearchBox => _search;

    /// <summary>The replacement field.</summary>
    public TextBox ReplacementBox => _replacement;

    public CheckBox MatchCase => _matchCase;

    public CheckBox WholeWord => _wholeWord;

    /// <summary>"Treffer 2 von 5", "Keine Treffer" (tests).</summary>
    public string CountText => _count.Text ?? "";

    public bool IsReplaceVisible => _replaceRow.IsVisible;

    /// <summary>Shows the bar (with the replacement for Ersetzen), takes a short selection as the search text, puts the keyboard into the search.</summary>
    public void Open(bool replace)
    {
        IsVisible = true;
        _replaceRow.IsVisible = replace;
        var selection = _editor.Session.Selection;
        if (!selection.IsCollapsed && selection.Range is { IsMultiBlock: false } range &&
            _editor.Session.Document.ParagraphAt(range.Start.Block) is { } paragraph && range.End.Offset - range.Start.Offset is > 0 and < 80)
        {
            _search.Text = paragraph.Text[range.Start.Offset..range.End.Offset];
        }

        _search.Focus();
        _search.SelectAll();
        UpdateCount();
    }

    /// <summary>Hides the bar and gives the keyboard back to the document.</summary>
    public void Close()
    {
        IsVisible = false;
        _editor.FocusDocument();
    }

    /// <summary>Selects the next (or previous) hit; false when there is none.</summary>
    public bool FindNext(bool forward = true)
    {
        if (Options() is not { } options)
        {
            return false;
        }

        var found = _editor.Session.FindNext(options, forward);
        UpdateCount();
        return found;
    }

    /// <summary>Ersetzen: replaces the current hit and selects the next one.</summary>
    public void ReplaceOne()
    {
        if (Options() is { } options)
        {
            _editor.Session.Replace(options, _replacement.Text ?? "");
            UpdateCount();
        }
    }

    /// <summary>Alle ersetzen: one undo step; the count says how many.</summary>
    public int ReplaceAll()
    {
        if (Options() is not { } options)
        {
            return 0;
        }

        var replaced = _editor.Session.ReplaceAll(options, _replacement.Text ?? "");
        _count.Text = replaced == 1 ? "1 ersetzt" : $"{replaced} ersetzt";
        return replaced;
    }

    private FindOptions? Options() =>
        string.IsNullOrEmpty(_search.Text) ? null : new FindOptions(_search.Text, _matchCase.IsChecked == true, _wholeWord.IsChecked == true);

    /// <summary>Typing in the search field: the first hit from where the selection starts shows at once.</summary>
    private void OnSearchChanged()
    {
        if (!IsVisible || Options() is not { } options)
        {
            UpdateCount();
            return;
        }

        var session = _editor.Session;
        var start = session.Selection.Start;
        if (TextSearch.IsMatch(session.Document, session.Selection.Range, options))
        {
            UpdateCount();
            return;
        }

        session.SetCaret(start);
        session.FindNext(options);
        UpdateCount();
    }

    private void UpdateCount()
    {
        if (Options() is not { } options)
        {
            _count.Text = "";
            return;
        }

        var session = _editor.Session;
        var hits = TextSearch.FindAll(session.Document, options);
        if (hits.Count == 0)
        {
            _count.Text = "Keine Treffer";
            return;
        }

        var current = hits.ToList().FindIndex(hit => hit.Start == session.Selection.Start && hit.End == session.Selection.End);
        _count.Text = current >= 0 ? $"Treffer {current + 1} von {hits.Count}" : hits.Count == 1 ? "1 Treffer" : $"{hits.Count} Treffer";
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Escape:
                Close();
                e.Handled = true;
                break;
            case Key.Enter when _replacement.IsFocused:
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
