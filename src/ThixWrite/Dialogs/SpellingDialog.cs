using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Spelling;
using ThixOffice.Desktop.Controls;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Überprüfen › Rechtschreibung (F7): steps through the unknown words from the caret to the end of the document – each one selected
/// in the text, shown with its sentence and the suggestions. Ändern (the suggestion chosen), Ignorieren (this one), Alle ignorieren,
/// Zum Wörterbuch hinzufügen; at the end it says the check is done.
/// </summary>
public sealed class SpellingDialog : WriteDialog
{
    private readonly WriteEditor _editor;
    private readonly TextBlock _word = new() { FontWeight = FontWeight.SemiBold, FontSize = 15 };
    private readonly TextBlock _context = new() { TextWrapping = TextWrapping.Wrap };
    private readonly ListBox _suggestions = new() { Height = 150 };
    private readonly StackPanel _found;
    private readonly TextBlock _done = new() { Text = "Die Rechtschreibprüfung ist abgeschlossen.", TextWrapping = TextWrapping.Wrap, IsVisible = false };
    private DocumentRange _range;
    private string _current = "";

    public SpellingDialog(WriteEditor editor)
        : base("Rechtschreibung", 460)
    {
        _editor = editor;
        OkButton.Content = new IconLabel { Icon = "check", Text = "Ändern" };
        CancelButton.Content = new IconLabel { Icon = "x", Text = "Schließen" };
        AutomationProperties.SetName(_suggestions, "Vorschläge");
        var ignore = new Button { Content = new IconLabel { Icon = "arrow-right", Text = "Ignorieren" } };
        var ignoreAll = new Button { Content = new IconLabel { Icon = "eye-off", Text = "Alle ignorieren" } };
        var add = new Button { Content = new IconLabel { Icon = "book", Text = "Zum Wörterbuch hinzufügen" } };
        ignore.Click += (_, _) => Next(_range.End);
        ignoreAll.Click += (_, _) =>
        {
            SpellingService.Ignore(_current);
            Next(_range.End);
        };
        add.Click += (_, _) =>
        {
            if (_editor.Spelling.Checker is { } checker)
            {
                SpellingService.AddToDictionary(checker, _current);
            }

            Next(_range.End);
        };
        _suggestions.DoubleTapped += (_, _) => Confirm();
        var label = new TextBlock { Text = "Nicht im Wörterbuch:" }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _found = new StackPanel
        {
            Spacing = 10,
            Children =
            {
                label,
                _word,
                _context,
                Field("Vorschläge", _suggestions),
                new WrapPanel { Children = { ignore, ignoreAll, add }, ItemSpacing = 8, LineSpacing = 8 },
            },
        };
        SetBody(new StackPanel { Children = { _found, _done } });
        Next(editor.Document.Session.Selection.Start);
    }

    /// <summary>The unknown word shown now (tests), or null when the check is done.</summary>
    public string? CurrentWord => _done.IsVisible ? null : _current;

    /// <summary>The suggestions shown (tests).</summary>
    public IReadOnlyList<string> Suggestions => _suggestions.ItemsSource as IReadOnlyList<string> ?? [];

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new SpellingDialog(editor).ShowAsync(owner);

    /// <summary>Ändern: the chosen suggestion (the first when none is chosen) replaces the word; then the next one.</summary>
    protected override bool Apply()
    {
        if (_done.IsVisible)
        {
            return true;
        }

        if ((_suggestions.SelectedItem as string ?? Suggestions.FirstOrDefault()) is { } replacement)
        {
            _editor.Document.Session.ReplaceWord(_range, replacement);
            Next(_editor.Document.Session.Selection.End);
        }
        else
        {
            Next(_range.End);
        }

        return false; // the dialog stays for the next word; Schließen ends it
    }

    /// <summary>Ignorieren (tests).</summary>
    public void IgnoreOnce() => Next(_range.End);

    private void Next(DocumentPosition from)
    {
        if (_editor.Spelling.NextError(from) is not { } error)
        {
            _found.IsVisible = false;
            _done.IsVisible = true;
            OkButton.IsVisible = false;
            CancelButton.Content = new IconLabel { Icon = "check", Text = "Schließen" };
            return;
        }

        _range = error.Range;
        _current = error.Word;
        _editor.Document.Session.Select(error.Range.Start, error.Range.End);
        _word.Text = error.Word;
        var paragraph = _editor.Document.Content.ParagraphAt(error.Range.Start.Block)!;
        var text = paragraph.Text;
        var start = Math.Max(0, error.Range.Start.Offset - 60);
        var end = Math.Min(text.Length, error.Range.End.Offset + 60);
        _context.Text = (start > 0 ? "…" : "") + text[start..end].Replace("￼", "", StringComparison.Ordinal).Trim() + (end < text.Length ? "…" : "");
        var suggestions = _editor.Spelling.Checker?.Suggest(error.Word) ?? [];
        _suggestions.ItemsSource = suggestions.ToList();
        _suggestions.SelectedIndex = suggestions.Count > 0 ? 0 : -1;
        OkButton.IsEnabled = suggestions.Count > 0;
    }
}
