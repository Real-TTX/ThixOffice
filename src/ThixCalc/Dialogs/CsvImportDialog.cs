using System.Text;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Formats.Csv;

namespace ThixCalc.Dialogs;

/// <summary>
/// Textimport of a CSV file whose separator or encoding was a guess (like Excel's "Daten aus Text"): Trennzeichen and
/// Dateiursprung with a live preview of the first rows; OK reads the file again with the choice (no unsaved change).
/// </summary>
public sealed class CsvImportDialog : CalcDialog
{
    private static readonly (string Label, char Delimiter)[] Delimiters = [("Semikolon", ';'), ("Komma", ','), ("Tabstopp", '\t'), ("Senkrechter Strich", '|')];

    private readonly CalcDocument _document;
    private readonly Encoding[] _encodings;
    private readonly ComboBox _delimiter = new() { ItemsSource = Delimiters.Select(entry => entry.Label).ToList(), MinWidth = 220 };
    private readonly ComboBox _encoding = new() { ItemsSource = new[] { "Unicode (UTF-8)", "Westeuropäisch (Windows-1252)", "Unicode (UTF-16)" }, MinWidth = 260 };
    private readonly UniformGrid _preview = new();

    private CsvImportDialog(CalcDocument document)
        : base("Textimport", 620)
    {
        _document = document;
        _encodings = [new UTF8Encoding(false), Encoding.Latin1, Encoding.Unicode];
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            _encodings[1] = Encoding.GetEncoding(1252);
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException)
        {
        }

        var detection = document.CsvDetection;
        _delimiter.SelectedIndex = Math.Max(0, Array.FindIndex(Delimiters, entry => entry.Delimiter == detection?.Delimiter));
        _encoding.SelectedIndex = detection?.Encoding is UTF8Encoding or null ? 0 : detection.Encoding.CodePage == 1200 ? 2 : 1;
        AutomationProperties.SetName(_delimiter, "Trennzeichen");
        AutomationProperties.SetName(_encoding, "Dateiursprung");
        _delimiter.SelectionChanged += (_, _) => ShowPreview();
        _encoding.SelectionChanged += (_, _) => ShowPreview();
        var frame = new Border { Child = new ScrollViewer { Content = _preview, MaxHeight = 200, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto }, BorderThickness = new Thickness(1), Padding = new Thickness(6) };
        frame.Use(Border.BorderBrushProperty, "Mat.Border");
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new TextBlock { Text = "Trennzeichen oder Zeichensatz dieser Datei sind nicht eindeutig. Prüfen Sie die Vorschau.", TextWrapping = TextWrapping.Wrap },
                Field("Trennzeichen", _delimiter),
                Field("Dateiursprung", _encoding),
                Field("Vorschau", frame),
            },
        });
        ShowPreview();
    }

    public ComboBox Delimiter => _delimiter;

    public static Task<bool> ShowAsync(Window owner, CalcDocument document) => new CsvImportDialog(document).ShowAsync(owner);

    internal static CsvImportDialog Create(CalcDocument document) => new(document);

    private CsvOptions Options() => _document.CsvOptions with
    {
        Delimiter = Delimiters[Math.Max(0, _delimiter.SelectedIndex)].Delimiter,
        Encoding = _encodings[Math.Max(0, _encoding.SelectedIndex)],
    };

    private void ShowPreview()
    {
        _preview.Children.Clear();
        if (_document.CsvSource is not { } source)
        {
            return;
        }

        var options = Options();
        var text = options.Encoding!.GetString(source, 0, Math.Min(source.Length, 16 * 1024)).TrimStart('﻿');
        var rows = CsvFormat.PreviewRows(text, options.Delimiter!.Value);
        var columns = Math.Clamp(rows.Count == 0 ? 1 : rows.Max(row => row.Count), 1, 12);
        _preview.Columns = columns;
        foreach (var row in rows)
        {
            for (var column = 0; column < columns; column++)
            {
                _preview.Children.Add(new TextBlock { Text = column < row.Count ? row[column] : string.Empty, Margin = new Thickness(0, 0, 12, 2), TextTrimming = TextTrimming.CharacterEllipsis, MaxWidth = 160 });
            }
        }
    }

    protected override bool Apply()
    {
        _document.ReloadCsv(Options());
        return true;
    }
}
