using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using MatDesktop;
using ThixOffice.Desktop.Controls;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>Überprüfen › Wörter zählen: pages, words, characters with and without spaces, paragraphs – of the selection when there is one, as in Word.</summary>
public sealed class WordCountDialog : WriteDialog
{
    public WordCountDialog(WriteEditor editor)
        : base("Wörter zählen", 380)
    {
        var session = editor.Document.Session;
        var selected = !session.Selection.IsCollapsed;
        var statistics = selected ? session.GetSelectionStatistics() : session.GetStatistics(editor.Layout.PageCount);
        Rows =
        [
            ("Seiten", editor.Layout.PageCount),
            ("Wörter", statistics.Words),
            ("Zeichen (ohne Leerzeichen)", statistics.CharactersWithoutSpaces),
            ("Zeichen (mit Leerzeichen)", statistics.Characters),
            ("Absätze", statistics.Paragraphs),
        ];
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto") };
        for (var row = 0; row < Rows.Count; row++)
        {
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));
            var label = new TextBlock { Text = Rows[row].Label, Margin = new Thickness(0, 4, 24, 4) }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            var value = new TextBlock { Text = Rows[row].Value.ToString("N0", CultureInfo.GetCultureInfo("de-DE")), Margin = new Thickness(0, 4) };
            Grid.SetRow(label, row);
            Grid.SetRow(value, row);
            Grid.SetColumn(value, 1);
            grid.Children.Add(label);
            grid.Children.Add(value);
        }

        var scope = new TextBlock { Text = selected ? "In der Markierung:" : "Im ganzen Dokument:", Margin = new Thickness(0, 0, 0, 8) };
        OkButton.Content = new IconLabel { Icon = "check", Text = "Schließen" };
        CancelButton.IsVisible = false;
        SetBody(new StackPanel { Children = { scope, grid } });
    }

    /// <summary>The counts shown (tests).</summary>
    public IReadOnlyList<(string Label, int Value)> Rows { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new WordCountDialog(editor).ShowAsync(owner);

    protected override bool Apply() => true;
}
