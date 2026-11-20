using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Start › Sortieren: the selected paragraphs – or, with the caret in a table, its rows by a column – by text, number or date, up or down.
/// </summary>
public sealed class SortDialog : WriteDialog
{
    private readonly WriteEditor _editor;

    public SortDialog(WriteEditor editor)
        : base("Text sortieren", 440)
    {
        _editor = editor;
        var table = editor.Session.CurrentTable;
        Column = new ComboBox
        {
            ItemsSource = table is null ? ["Absätze"] : Enumerable.Range(1, Math.Max(1, table.Table.ColumnCount)).Select(column => $"Spalte {column}").ToList(),
            SelectedIndex = 0,
            Width = 200,
        };
        Kind = new ComboBox { ItemsSource = new[] { "Text", "Zahl", "Datum" }, SelectedIndex = 0, Width = 140 };
        Descending = new CheckBox { Content = "Absteigend (Z bis A, 9 bis 1)" };
        AutomationProperties.SetName(Column, "Sortieren nach");
        AutomationProperties.SetName(Kind, "Typ");
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Field("Sortieren nach", Column), Field("Typ", Kind) } },
                Descending,
            },
        });
    }

    public ComboBox Column { get; }

    public ComboBox Kind { get; }

    public CheckBox Descending { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new SortDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        _editor.Session.Sort((SortKind)Math.Max(0, Kind.SelectedIndex), Descending.IsChecked == true, Math.Max(0, Column.SelectedIndex));
        return true;
    }
}

/// <summary>
/// "Rahmen und Schattierung": Rahmen of the selected paragraphs, Seitenrand of the section, Schattierung of the paragraphs – Word's
/// dialog in short (setting, line, colour, width; fill).
/// </summary>
public sealed class BordersDialog : WriteDialog
{
    public const int BordersTab = 0;
    public const int PageTab = 1;
    public const int ShadingTab = 2;

    private static readonly string[] Settings = ["Ohne", "Kasten", "Nur unten", "Nur oben"];
    private static readonly (string Label, string Style)[] LineStyles = [("Einfach", "single"), ("Doppelt", "double"), ("Punktiert", "dotted"), ("Gestrichelt", "dashed")];
    private static readonly double[] Widths = [0.5, 1, 1.5, 2.25, 3];

    private readonly WriteEditor _editor;
    private readonly (ComboBox Setting, ComboBox Style, ComboBox Color, ComboBox Width) _paragraph;
    private readonly (ComboBox Setting, ComboBox Style, ComboBox Color, ComboBox Width) _page;
    private readonly int _paragraphStart;
    private readonly int _pageStart;
    private readonly string? _shadingStart;

    public BordersDialog(WriteEditor editor, int tab = BordersTab)
        : base("Rahmen und Schattierung", 520)
    {
        _editor = editor;
        var session = editor.Session;
        var colors = new[] { "Automatisch" }.Concat(DocumentPalette.FontColors(session.Document.Theme)).Distinct().ToList();
        var paragraph = session.Document.ParagraphAt(session.Selection.Start.Block) ?? Paragraph.Empty();
        var format = session.Resolver.ResolveParagraph(paragraph);
        _paragraphStart = SettingOf(format.Borders);
        _paragraph = Line(colors, _paragraphStart, format.Borders);
        var section = session.Document.SectionOf(session.Selection.Start.Block).Properties;
        _pageStart = SettingOf(section.PageBorders);
        _page = Line(colors, _pageStart, section.PageBorders);
        _shadingStart = format.ShadingFill is { } fill ? "#" + fill : null;
        Fill = new ComboBox { ItemsSource = new[] { "Keine Farbe" }.Concat(DocumentPalette.FontColors(session.Document.Theme)).Distinct().ToList(), Width = 200, ItemTemplate = Swatches() };
        Fill.SelectedItem = _shadingStart ?? "Keine Farbe";
        AutomationProperties.SetName(Fill, "Füllung");
        Tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Rahmen", Content = Page(_paragraph, "Übernehmen für: Absatz") },
                new TabItem { Header = "Seitenrand", Content = Page(_page, "Übernehmen für: diesen Abschnitt (gemessen vom Seitenrand)") },
                new TabItem { Header = "Schattierung", Content = new StackPanel { Margin = new Thickness(0, 14, 0, 0), Children = { Field("Füllung", Fill) } } },
            },
        };
        Tabs.SelectedIndex = tab;
        SetBody(Tabs);
    }

    public TabControl Tabs { get; }

    /// <summary>"Einstellung" of the paragraph borders (Ohne, Kasten, Nur unten, Nur oben).</summary>
    public ComboBox ParagraphSetting => _paragraph.Setting;

    public ComboBox PageSetting => _page.Setting;

    public ComboBox Fill { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor, int tab = BordersTab) => new BordersDialog(editor, tab).ShowAsync(owner);

    protected override bool Apply()
    {
        var session = _editor.Session;
        if (_paragraph.Setting.SelectedIndex != _paragraphStart || _paragraph.Setting.SelectedIndex > 0)
        {
            session.SetParagraphBorders(Borders(_paragraph, space: 1));
        }

        if (_page.Setting.SelectedIndex != _pageStart || _page.Setting.SelectedIndex > 0)
        {
            session.SetPageBorders(Borders(_page, space: 24));
        }

        var fill = Fill.SelectedItem as string is { } chosen && chosen.StartsWith('#') ? chosen : null;
        if (fill != _shadingStart)
        {
            session.SetParagraphShading(fill);
        }

        return true;
    }

    private static int SettingOf(BorderSet? borders) => borders switch
    {
        null => 0,
        { Top.IsVisible: true, Bottom.IsVisible: true, Left.IsVisible: true } => 1,
        { Bottom.IsVisible: true } => 2,
        { Top.IsVisible: true } => 3,
        _ => 0,
    };

    private static BorderSet? Borders((ComboBox Setting, ComboBox Style, ComboBox Color, ComboBox Width) choice, double space)
    {
        var style = LineStyles[Math.Max(0, choice.Style.SelectedIndex)].Style;
        var color = choice.Color.SelectedItem as string is { } hex && hex.StartsWith('#') ? hex.TrimStart('#').ToUpperInvariant() : "auto";
        var line = new BorderLine(style, Widths[Math.Max(0, choice.Width.SelectedIndex)], color, space);
        return choice.Setting.SelectedIndex switch
        {
            1 => new BorderSet(line, line, line, line),
            2 => new BorderSet(Bottom: line),
            3 => new BorderSet(Top: line),
            _ => null,
        };
    }

    private static (ComboBox, ComboBox, ComboBox, ComboBox) Line(List<string> colors, int setting, BorderSet? borders)
    {
        var current = borders?.Top ?? borders?.Bottom ?? borders?.Left;
        var settingBox = new ComboBox { ItemsSource = Settings, SelectedIndex = setting, Width = 160 };
        var styleBox = new ComboBox { ItemsSource = LineStyles.Select(item => item.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(LineStyles, item => item.Style == current?.Style)), Width = 160 };
        var colorBox = new ComboBox { ItemsSource = colors, Width = 160, ItemTemplate = Swatches() };
        colorBox.SelectedItem = current?.Color is { Length: 6 } hex && colors.Contains("#" + hex.ToUpperInvariant()) ? "#" + hex.ToUpperInvariant() : "Automatisch";
        var widthBox = new ComboBox { ItemsSource = Widths.Select(width => $"{width.ToString("0.##", System.Globalization.CultureInfo.GetCultureInfo("de-DE"))} pt").ToList(), Width = 120 };
        widthBox.SelectedIndex = current is null ? 0 : Math.Max(0, Array.FindIndex(Widths, width => Math.Abs(width - current.Width) < 0.01));
        AutomationProperties.SetName(settingBox, "Einstellung");
        AutomationProperties.SetName(styleBox, "Formatvorlage");
        AutomationProperties.SetName(colorBox, "Farbe");
        AutomationProperties.SetName(widthBox, "Breite");
        return (settingBox, styleBox, colorBox, widthBox);
    }

    private static StackPanel Page((ComboBox Setting, ComboBox Style, ComboBox Color, ComboBox Width) choice, string note) => new()
    {
        Spacing = 12,
        Margin = new Thickness(0, 14, 0, 0),
        Children =
        {
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Field("Einstellung", choice.Setting), Field("Formatvorlage", choice.Style) } },
            new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Field("Farbe", choice.Color), Field("Breite", choice.Width) } },
            new TextBlock { Text = note, TextWrapping = TextWrapping.Wrap }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted"),
        },
    };

    internal static Avalonia.Controls.Templates.FuncDataTemplate<string> Swatches() => new((value, _) =>
    {
        var text = new TextBlock { Text = value, VerticalAlignment = VerticalAlignment.Center };
        if (value is null || !value.StartsWith('#'))
        {
            return text;
        }

        var swatch = new Border { Width = 14, Height = 14, BorderThickness = new Thickness(1), Background = DocumentPalette.Swatch(value) }.Use(Border.BorderBrushProperty, "Mat.Border");
        return new StackPanel { Orientation = Orientation.Horizontal, Spacing = 6, Children = { swatch, text } };
    });
}
