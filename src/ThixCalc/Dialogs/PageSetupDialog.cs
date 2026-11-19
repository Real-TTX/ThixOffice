using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixCalc.Editor;

namespace ThixCalc.Dialogs;

/// <summary>
/// Seite einrichten of a sheet, like Excel's: tabs Seite (Ausrichtung, Skalierung in % or An N Seite(n) breit und M hoch,
/// Papierformat, erste Seitenzahl) | Seitenränder (cm, zentrieren) | Kopfzeile/Fußzeile (Excel's presets or text with &amp;P …)
/// | Blatt (Druckbereich, Wiederholungszeilen oben, Gitternetzlinien, Zeilen- und Spaltenüberschriften, Seitenreihenfolge).
/// One undo step for the page setup, one for the print area and titles when they changed.
/// </summary>
public sealed class PageSetupDialog : CalcDialog
{
    public const int SheetTab = 3;
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Excel's header/footer presets: label and code.</summary>
    public static IReadOnlyList<(string Label, string? Code)> HeaderFooterPresets { get; } =
    [
        ("(keine)", null),
        ("Seite 1", "&CSeite &P"),
        ("Seite 1 von ?", "&CSeite &P von &N"),
        ("Blattname", "&C&A"),
        ("Dateiname", "&C&F"),
        ("Blattname, Seite 1", "&L&A&RSeite &P"),
        ("Datum und Seite", "&L&D&RSeite &P von &N"),
    ];

    private readonly CalcEditor _editor;
    private readonly RadioButton _portrait = new() { Content = "Hochformat", GroupName = "orientation" };
    private readonly RadioButton _landscape = new() { Content = "Querformat", GroupName = "orientation" };
    private readonly RadioButton _scaleChoice = new() { Content = "Verkleinern/Vergrößern auf", GroupName = "scale" };
    private readonly RadioButton _fitChoice = new() { Content = "Anpassen an", GroupName = "scale" };
    private readonly NumericUpDown _scale = Number(10, 400, 5, "0");
    private readonly NumericUpDown _wide = Number(0, 100, 1, "0");
    private readonly NumericUpDown _tall = Number(0, 100, 1, "0");
    private readonly ComboBox _paper = new() { ItemsSource = SheetPaper.All.Select(paper => $"{paper.Label} ({paper.WidthMillimetres / 10:0.##} × {paper.HeightMillimetres / 10:0.##} cm)").ToList(), MinWidth = 260 };
    private readonly TextBox _firstPage = new() { Watermark = "Automatisch", Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    private readonly NumericUpDown _top = Number(0, 20, 0.1m, "0.0#");
    private readonly NumericUpDown _bottom = Number(0, 20, 0.1m, "0.0#");
    private readonly NumericUpDown _left = Number(0, 20, 0.1m, "0.0#");
    private readonly NumericUpDown _right = Number(0, 20, 0.1m, "0.0#");
    private readonly NumericUpDown _header = Number(0, 20, 0.1m, "0.0#");
    private readonly NumericUpDown _footer = Number(0, 20, 0.1m, "0.0#");
    private readonly CheckBox _centerHorizontally = new() { Content = "Horizontal" };
    private readonly CheckBox _centerVertically = new() { Content = "Vertikal" };
    private readonly ComboBox _headerChoice = PresetBox();
    private readonly ComboBox _footerChoice = PresetBox();
    private readonly TextBox _headerText = new();
    private readonly TextBox _footerText = new();
    private readonly TextBox _printArea = new() { Watermark = "z. B. A1:H40" };
    private readonly TextBox _titles = new() { Watermark = "z. B. $1:$2" };
    private readonly CheckBox _gridLines = new() { Content = "Gitternetzlinien" };
    private readonly CheckBox _headings = new() { Content = "Zeilen- und Spaltenüberschriften" };
    private readonly RadioButton _downThenOver = new() { Content = "Seiten nach unten, dann nach rechts", GroupName = "order" };
    private readonly RadioButton _overThenDown = new() { Content = "Seiten nach rechts, dann nach unten", GroupName = "order" };
    private readonly MatDesktop.MatFormField _areaField;
    private readonly MatDesktop.MatFormField _titlesField;

    private PageSetupDialog(CalcEditor editor, int tab)
        : base("Seite einrichten", 600)
    {
        _editor = editor;
        _areaField = Field("Druckbereich", _printArea);
        _titlesField = Field("Wiederholungszeilen oben", _titles);
        AutomationProperties.SetName(_paper, "Papierformat");
        AutomationProperties.SetName(_headerText, "Kopfzeile");
        AutomationProperties.SetName(_footerText, "Fußzeile");
        _headerChoice.SelectionChanged += (_, _) => Preset(_headerChoice, _headerText);
        _footerChoice.SelectionChanged += (_, _) => Preset(_footerChoice, _footerText);

        var page = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                Field("Ausrichtung", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { _portrait, _landscape } }),
                Field("Skalierung", new StackPanel
                {
                    Spacing = 8,
                    Children =
                    {
                        Row(_scaleChoice, _scale, new TextBlock { Text = "% normaler Größe", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }),
                        Row(_fitChoice, _wide, new TextBlock { Text = "Seite(n) breit und", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, _tall, new TextBlock { Text = "hoch", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }),
                    },
                }),
                Field("Papierformat", _paper),
                Field("Erste Seitenzahl", _firstPage),
            },
        };
        var margins = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*,16,*"), RowDefinitions = new RowDefinitions("Auto,Auto,Auto,Auto"), Margin = new Thickness(0, 12, 0, 0) };
        Place(margins, Field("Oben (cm)", _top), 0, 2);
        Place(margins, Field("Kopfzeile (cm)", _header), 0, 4);
        Place(margins, Field("Links (cm)", _left), 1, 0);
        Place(margins, Field("Rechts (cm)", _right), 1, 4);
        Place(margins, Field("Unten (cm)", _bottom), 2, 2);
        Place(margins, Field("Fußzeile (cm)", _footer), 2, 4);
        Place(margins, Field("Auf der Seite zentrieren", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { _centerHorizontally, _centerVertically } }), 3, 0, 5);
        var headerFooter = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                Field("Kopfzeile", _headerChoice), _headerText,
                Field("Fußzeile", _footerChoice), _footerText,
                new TextBlock { Text = "Codes: &P Seite, &N Seitenzahl, &D Datum, &T Uhrzeit, &A Blatt, &F Datei; &L &C &R links, mittig, rechts.", TextWrapping = Avalonia.Media.TextWrapping.Wrap }
                    .Use(TextBlock.ForegroundProperty, "Mat.TextMuted"),
            },
        };
        var sheet = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(0, 12, 0, 0),
            Children =
            {
                _areaField, _titlesField,
                Field("Drucken", new StackPanel { Spacing = 6, Children = { _gridLines, _headings } }),
                Field("Seitenreihenfolge", new StackPanel { Spacing = 6, Children = { _downThenOver, _overThenDown } }),
            },
        };
        Tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Seite", Content = page },
                new TabItem { Header = "Seitenränder", Content = margins },
                new TabItem { Header = "Kopfzeile/Fußzeile", Content = headerFooter },
                new TabItem { Header = "Blatt", Content = sheet },
            },
            SelectedIndex = tab,
        };
        SetBody(Tabs);
        Load();
    }

    public TabControl Tabs { get; }

    public RadioButton LandscapeChoice => _landscape;

    public RadioButton FitChoice => _fitChoice;

    public NumericUpDown FitWide => _wide;

    public NumericUpDown FitTall => _tall;

    public TextBox Titles => _titles;

    public ComboBox FooterChoice => _footerChoice;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, int tab = 0) => new PageSetupDialog(editor, tab).ShowAsync(owner);

    internal static PageSetupDialog Create(CalcEditor editor, int tab = 0) => new(editor, tab);

    private static NumericUpDown Number(decimal minimum, decimal maximum, decimal increment, string format) =>
        new() { Minimum = minimum, Maximum = maximum, Increment = increment, FormatString = format, Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };

    private static ComboBox PresetBox() => new() { ItemsSource = HeaderFooterPresets.Select(preset => preset.Label).ToList(), MinWidth = 260 };

    private static StackPanel Row(params Control[] controls)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8 };
        foreach (var control in controls)
        {
            row.Children.Add(control);
        }

        return row;
    }

    private static void Place(Grid grid, Control control, int row, int column, int span = 1)
    {
        Grid.SetRow(control, row);
        Grid.SetColumn(control, column);
        Grid.SetColumnSpan(control, span);
        control.Margin = new Thickness(0, 0, 0, 10);
        grid.Children.Add(control);
    }

    private static void Preset(ComboBox choice, TextBox text)
    {
        if (choice.SelectedIndex >= 0)
        {
            text.Text = HeaderFooterPresets[choice.SelectedIndex].Code ?? string.Empty;
        }
    }

    private static decimal Centimetres(double inches) => Math.Round((decimal)(inches * 2.54), 2);

    private static double Inches(decimal? centimetres) => (double)(centimetres ?? 0) / 2.54;

    private void Load()
    {
        var sheet = _editor.Session.ActiveSheet;
        var setup = sheet.PageSetup;
        _portrait.IsChecked = !setup.Landscape;
        _landscape.IsChecked = setup.Landscape;
        _scaleChoice.IsChecked = !setup.FitToPage;
        _fitChoice.IsChecked = setup.FitToPage;
        _scale.Value = setup.Scale;
        _wide.Value = setup.FitToWidth;
        _tall.Value = setup.FitToHeight;
        _paper.SelectedIndex = Math.Max(0, SheetPaper.All.ToList().FindIndex(paper => paper.Code == setup.PaperSize));
        _firstPage.Text = setup.FirstPageNumber?.ToString(CultureInfo.InvariantCulture);
        _top.Value = Centimetres(setup.TopMargin);
        _bottom.Value = Centimetres(setup.BottomMargin);
        _left.Value = Centimetres(setup.LeftMargin);
        _right.Value = Centimetres(setup.RightMargin);
        _header.Value = Centimetres(setup.HeaderMargin);
        _footer.Value = Centimetres(setup.FooterMargin);
        _centerHorizontally.IsChecked = setup.CenterHorizontally;
        _centerVertically.IsChecked = setup.CenterVertically;
        _headerText.Text = setup.Header;
        _footerText.Text = setup.Footer;
        _headerChoice.SelectedIndex = HeaderFooterPresets.ToList().FindIndex(preset => preset.Code == setup.Header);
        _footerChoice.SelectedIndex = HeaderFooterPresets.ToList().FindIndex(preset => preset.Code == setup.Footer);
        _headerText.Text = setup.Header;
        _footerText.Text = setup.Footer;
        _gridLines.IsChecked = setup.PrintGridLines;
        _headings.IsChecked = setup.PrintHeadings;
        _downThenOver.IsChecked = !setup.OverThenDown;
        _overThenDown.IsChecked = setup.OverThenDown;
        _printArea.Text = EditingSession.PrintAreaOf(sheet)?.ToString();
        _titles.Text = EditingSession.PrintTitlesOf(sheet) is { } titles ? $"${titles.FirstRow + 1}:${titles.LastRow + 1}" : null;
    }

    protected override bool Apply()
    {
        var session = _editor.Session;
        var sheet = session.ActiveSheet;
        CellRange? area = null;
        if (!string.IsNullOrWhiteSpace(_printArea.Text))
        {
            area = CellRange.TryParse(_printArea.Text.Replace("$", "", StringComparison.Ordinal).Trim());
            if (area is null)
            {
                _areaField.Error = "Geben Sie einen Bereich wie A1:H40 ein.";
                Tabs.SelectedIndex = SheetTab;
                return false;
            }
        }

        CellRange? titles = null;
        if (!string.IsNullOrWhiteSpace(_titles.Text))
        {
            var token = FormulaTokens.Scan(_titles.Text.Trim(), CalcLocale.Invariant).FirstOrDefault(candidate => candidate.Kind == FormulaTokenKind.Reference);
            if (token?.Range is not { } range)
            {
                _titlesField.Error = "Geben Sie Zeilen wie $1:$2 ein.";
                Tabs.SelectedIndex = SheetTab;
                return false;
            }

            titles = range;
        }

        int? firstPage = int.TryParse(_firstPage.Text, NumberStyles.Integer, German, out var number) && number > 0 ? number : null;
        var updated = sheet.PageSetup with
        {
            Landscape = _landscape.IsChecked == true,
            FitToPage = _fitChoice.IsChecked == true,
            Scale = (int)(_scale.Value ?? 100),
            FitToWidth = (int)(_wide.Value ?? 1),
            FitToHeight = (int)(_tall.Value ?? 1),
            PaperSize = SheetPaper.All[Math.Max(0, _paper.SelectedIndex)].Code,
            FirstPageNumber = firstPage,
            TopMargin = Inches(_top.Value),
            BottomMargin = Inches(_bottom.Value),
            LeftMargin = Inches(_left.Value),
            RightMargin = Inches(_right.Value),
            HeaderMargin = Inches(_header.Value),
            FooterMargin = Inches(_footer.Value),
            CenterHorizontally = _centerHorizontally.IsChecked == true,
            CenterVertically = _centerVertically.IsChecked == true,
            Header = string.IsNullOrEmpty(_headerText.Text) ? null : _headerText.Text,
            Footer = string.IsNullOrEmpty(_footerText.Text) ? null : _footerText.Text,
            PrintGridLines = _gridLines.IsChecked == true,
            PrintHeadings = _headings.IsChecked == true,
            OverThenDown = _overThenDown.IsChecked == true,
        };

        return Run(() =>
        {
            if (updated != sheet.PageSetup)
            {
                session.SetPageSetup(_ => updated);
            }

            if (area != EditingSession.PrintAreaOf(sheet))
            {
                session.SetPrintArea(area);
            }

            if (titles != EditingSession.PrintTitlesOf(sheet))
            {
                session.SetPrintTitles(titles?.FirstRow, titles?.LastRow);
            }
        });
    }
}
