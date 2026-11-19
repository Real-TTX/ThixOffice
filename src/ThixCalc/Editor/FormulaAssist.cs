using MatDesktop;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using ThixOffice.Desktop;

namespace ThixCalc.Editor;

/// <summary>
/// The help under the caret while a formula is typed, like Excel's: the list of functions whose name starts with what was
/// typed (the first one marked; ↑/↓ choose, Tab or a double click takes one; its description beside it), or – inside a
/// function's parentheses – the argument tip "SVERWEIS(Suchkriterium; Matrix; …)" with the current argument in bold.
/// </summary>
internal sealed class FormulaAssist : Border
{
    private readonly CalcEditor _editor;
    private readonly StackPanel _list = new() { Spacing = 0 };
    private readonly TextBlock _description = new() { TextWrapping = TextWrapping.Wrap, MaxWidth = 280, Margin = new Thickness(10, 6), FontSize = 12 };
    private readonly SelectableTextBlock _tip = new() { FontSize = 12, Margin = new Thickness(8, 4) };
    private readonly StackPanel _listRow = new() { Orientation = Orientation.Horizontal };

    public FormulaAssist(CalcEditor editor)
    {
        _editor = editor;
        this.Use(BackgroundProperty, "Mat.Surface");
        this.Use(BorderBrushProperty, "Mat.Border");
        _description.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        BorderThickness = new Thickness(1);
        CornerRadius = new CornerRadius(4);
        IsHitTestVisible = true;
        _listRow.Children.Add(_list);
        _listRow.Children.Add(_description);
    }

    /// <summary>The functions shown (tests).</summary>
    public IReadOnlyList<string> Entries => [.. _list.Children.OfType<Border>().Select(entry => ((TextBlock)entry.Child!).Text ?? string.Empty)];

    /// <summary>The tip's text (tests).</summary>
    public string TipText => _tip.Inlines?.Count > 0 ? string.Concat(_tip.Inlines.OfType<Run>().Select(run => run.Text)) : string.Empty;

    /// <summary>Shows the list, the tip or nothing, under the caret of the active field.</summary>
    public void Update()
    {
        var editing = _editor.Editing;
        if (!editing.IsEditing)
        {
            IsVisible = false;
            return;
        }

        if (editing.Suggestions.Count > 0)
        {
            ShowList(editing);
        }
        else if (editing.ArgumentTip is { } tip)
        {
            ShowTip(tip.Function, tip.Argument);
        }
        else
        {
            IsVisible = false;
            return;
        }

        IsVisible = true;
        Place(editing);
    }

    private void ShowList(CellEditing editing)
    {
        _list.Children.Clear();
        for (var index = 0; index < editing.Suggestions.Count; index++)
        {
            var info = editing.Suggestions[index];
            var entry = new Border
            {
                Child = new TextBlock { Text = info.Name, FontSize = 12, Margin = new Thickness(8, 3) },
                Cursor = new Cursor(StandardCursorType.Hand),
                MinWidth = 160,
            };
            if (index == editing.SuggestionIndex)
            {
                entry.Use(BackgroundProperty, "Mat.AccentSoft");
            }

            var chosen = index;
            entry.PointerPressed += (_, e) =>
            {
                e.Handled = true;
                if (e.ClickCount == 2)
                {
                    _editor.Editing.AcceptSuggestion(chosen);
                }
            };
            _list.Children.Add(entry);
        }

        _description.Text = editing.Suggestions[Math.Clamp(editing.SuggestionIndex, 0, editing.Suggestions.Count - 1)].Description;
        Child = _listRow;
    }

    private void ShowTip(Core.Functions.FunctionInfo function, int argument)
    {
        var locale = _editor.Session.Locale;
        _tip.Inlines = [];
        _tip.Inlines.Add(new Run(function.Name + "("));
        var count = Math.Max(function.Arguments.Count, argument + 1);
        for (var index = 0; index < count; index++)
        {
            if (function.ArgumentName(index) is not { } name)
            {
                break;
            }

            if (index > 0)
            {
                _tip.Inlines.Add(new Run(locale.ListSeparator + " "));
            }

            _tip.Inlines.Add(new Run(name) { FontWeight = index == argument ? FontWeight.Bold : FontWeight.Normal });
        }

        if (function.RepeatsLast)
        {
            _tip.Inlines.Add(new Run(locale.ListSeparator + " …"));
        }

        _tip.Inlines.Add(new Run(")"));
        Child = _tip;
    }

    /// <summary>Below the caret of the field being typed in (inside the grid area).</summary>
    private void Place(CellEditing editing)
    {
        var box = editing.IsInFormulaBar ? editing.BarBox : editing.CellBox;
        var caret = box.CaretRect();
        var host = Parent as Visual;
        if (host is null || box.TranslatePoint(new Point(caret.X, caret.Bottom + 2), host) is not { } point)
        {
            return;
        }

        Canvas.SetLeft(this, Math.Max(0, point.X - 4));
        Canvas.SetTop(this, Math.Max(0, point.Y));
    }
}
