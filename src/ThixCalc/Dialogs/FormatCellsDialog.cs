using Avalonia.Controls.Primitives;
using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;
using CellHorizontal = ThixCalc.Core.Model.HorizontalAlignment;
using CellVertical = ThixCalc.Core.Model.VerticalAlignment;

namespace ThixCalc.Dialogs;

/// <summary>
/// Zellen formatieren (Strg+1), like Excel's: tabs Zahlen | Ausrichtung | Schrift | Rahmen | Ausfüllen for the selection,
/// opened on the active cell's values. Only what the user changes is applied (a mixed selection keeps the rest), in ONE undo
/// step for the format and one for the borders. Zahlen shows an example of the active cell's value in the chosen format.
/// </summary>
public sealed class FormatCellsDialog : OfficeDialog
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private static readonly string[] Categories = ["Standard", "Zahl", "Währung", "Buchhaltung", "Datum", "Uhrzeit", "Prozent", "Bruch", "Wissenschaft", "Text", "Benutzerdefiniert"];
    private static readonly (string Label, string Code)[] DateFormats =
    [
        ("14.03.2025", "TT.MM.JJJJ"), ("14.03.25", "TT.MM.JJ"), ("14. Mrz", "T. MMM"), ("14. März 2025", "T. MMMM JJJJ"),
        ("Freitag, 14. März 2025", "TTTT, T. MMMM JJJJ"), ("März 25", "MMMM JJ"), ("2025-03-14", "JJJJ-MM-TT"),
    ];
    private static readonly (string Label, string Code)[] TimeFormats =
    [
        ("13:30:55", "hh:mm:ss"), ("13:30", "hh:mm"), ("1:30 PM", "h:mm AM/PM"), ("14.03.25 13:30", "TT.MM.JJ hh:mm"), ("37:30:55", "[h]:mm:ss"),
    ];

    private readonly CalcEditor _editor;
    private readonly CellStyle _start;

    // Zahlen
    private readonly ListBox _category = new() { ItemsSource = Categories, Height = 330, Width = 170 };
    private readonly NumericUpDown _decimals = new() { Minimum = 0, Maximum = 30, Value = 2, Increment = 1, FormatString = "0", Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    private readonly CheckBox _thousands = new() { Content = "1.000er-Trennzeichen verwenden (.)" };
    private readonly ComboBox _type = new() { MinWidth = 240 };
    private readonly TextBox _custom = new() { MinWidth = 240 };
    private readonly TextBlock _sample = new() { FontSize = 14 };
    private readonly StackPanel _numberOptions = new() { Spacing = 10 };
    private bool _numberChanged;

    // Ausrichtung
    private readonly ComboBox _horizontal = Combo("Standard", "Links (Einzug)", "Zentriert", "Rechts (Einzug)", "Ausfüllen", "Blocksatz", "Über Auswahl zentrieren", "Verteilt (Einzug)");
    private readonly ComboBox _vertical = Combo("Unten", "Oben", "Zentriert", "Blocksatz", "Verteilt");
    private readonly NumericUpDown _indent = new() { Minimum = 0, Maximum = 250, Increment = 1, FormatString = "0", Width = 120, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
    private readonly CheckBox _wrap = new() { Content = "Zeilenumbruch" };
    private readonly CheckBox _shrink = new() { Content = "An Zellgröße anpassen" };
    private readonly CheckBox _merge = new() { Content = "Zellen verbinden" };
    private readonly NumericUpDown _rotation = new() { Minimum = -90, Maximum = 90, Increment = 15, FormatString = "0", Width = 120 };

    // Schrift
    private readonly ComboBox _fontName = new() { MinWidth = 220, IsTextSearchEnabled = true };
    private readonly ComboBox _fontStyle = Combo("Standard", "Kursiv", "Fett", "Fett Kursiv");
    private readonly ComboBox _fontSize = new() { MinWidth = 90 };
    private readonly ComboBox _underline = Combo("Keine", "Einfach", "Doppelt", "Einfach (Buchhaltung)", "Doppelt (Buchhaltung)");
    private readonly ColorChoice _fontColor;
    private readonly CheckBox _strike = new() { Content = "Durchgestrichen" };
    private readonly CheckBox _superscript = new() { Content = "Hochgestellt" };
    private readonly CheckBox _subscript = new() { Content = "Tiefgestellt" };

    // Rahmen
    private readonly ComboBox _lineStyle = Combo("Dünn", "Mittel", "Dick", "Doppelt", "Gestrichelt", "Gepunktet");
    private readonly ColorChoice _lineColor;
    private readonly CheckBox _top = new() { Content = "Oben" };
    private readonly CheckBox _bottom = new() { Content = "Unten" };
    private readonly CheckBox _left = new() { Content = "Links" };
    private readonly CheckBox _right = new() { Content = "Rechts" };
    private readonly CheckBox _insideHorizontal = new() { Content = "Innen waagrecht" };
    private readonly CheckBox _insideVertical = new() { Content = "Innen senkrecht" };
    private bool _bordersChanged;

    // Ausfüllen
    private readonly ColorChoice _fill;

    public FormatCellsDialog(CalcEditor editor)
        : base("Zellen formatieren", 640)
    {
        _editor = editor;
        var session = editor.Session;
        _start = session.ActiveStyle;
        var palette = GridPalette.Colors(session.Workbook.Theme);
        _fontColor = new ColorChoice(palette, "Automatisch");
        _lineColor = new ColorChoice(palette, "Automatisch");
        _fill = new ColorChoice(palette, "Keine Farbe");

        Tabs = new TabControl
        {
            Items =
            {
                new TabItem { Header = "Zahlen", Content = NumberTab() },
                new TabItem { Header = "Ausrichtung", Content = AlignmentTab() },
                new TabItem { Header = "Schrift", Content = FontTab() },
                new TabItem { Header = "Rahmen", Content = BorderTab() },
                new TabItem { Header = "Ausfüllen", Content = FillTab() },
            },
        };
        SetBody(Tabs);
        Load();
    }

    public TabControl Tabs { get; }

    /// <summary>The Zahlen category list (tests choose in it).</summary>
    public ListBox Category => _category;

    public NumericUpDown Decimals => _decimals;

    public CheckBox Thousands => _thousands;

    public CheckBox WrapText => _wrap;

    public ComboBox Horizontal => _horizontal;

    public ComboBox FontStyleChoice => _fontStyle;

    public CheckBox BorderBottom => _bottom;

    public ColorChoice FillChoice => _fill;

    /// <summary>The example of the Zahlen tab (tests).</summary>
    public string SampleText => _sample.Text ?? "";

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new FormatCellsDialog(editor).ShowAsync(owner);

    private static ComboBox Combo(params string[] items) => new() { ItemsSource = items, SelectedIndex = 0, MinWidth = 220 };

    // ===== Zahlen =====

    private Control NumberTab()
    {
        AutomationProperties.SetName(_category, "Kategorie");
        _category.SelectionChanged += (_, _) => OnNumberChanged(categoryChanged: true);
        _decimals.ValueChanged += (_, _) => OnNumberChanged();
        _thousands.IsCheckedChanged += (_, _) => OnNumberChanged();
        _type.SelectionChanged += (_, _) => OnNumberChanged();
        _custom.TextChanged += (_, _) => OnNumberChanged();
        var sampleBox = new Border { Padding = new Thickness(10, 6), CornerRadius = new CornerRadius(4), BorderThickness = new Thickness(1), Child = _sample };
        sampleBox.Use(Border.BorderBrushProperty, "Mat.Border");
        var right = new StackPanel { Spacing = 12, Children = { Field("Beispiel", sampleBox), _numberOptions } };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,20,*"), Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetColumn(right, 2);
        grid.Children.Add(Field("Kategorie", _category));
        grid.Children.Add(right);
        return grid;
    }

    private void OnNumberChanged(bool categoryChanged = false)
    {
        if (_loading)
        {
            return;
        }

        _numberChanged = true;
        if (categoryChanged)
        {
            ShowNumberOptions();
        }

        UpdateSample();
    }

    private void ShowNumberOptions()
    {
        _numberOptions.Children.Clear();
        var category = Categories[Math.Max(0, _category.SelectedIndex)];
        switch (category)
        {
            case "Zahl":
                _numberOptions.Children.Add(Field("Dezimalstellen", _decimals));
                _numberOptions.Children.Add(_thousands);
                break;
            case "Währung" or "Buchhaltung" or "Prozent" or "Wissenschaft":
                _numberOptions.Children.Add(Field("Dezimalstellen", _decimals));
                break;
            case "Datum":
                Fill(_type, DateFormats);
                _numberOptions.Children.Add(Field("Typ", _type));
                break;
            case "Uhrzeit":
                Fill(_type, TimeFormats);
                _numberOptions.Children.Add(Field("Typ", _type));
                break;
            case "Benutzerdefiniert":
                _numberOptions.Children.Add(Field("Typ (Formatcode)", _custom));
                break;
        }

        _numberOptions.Children.Add(new TextBlock { Text = Explanation(category), TextWrapping = TextWrapping.Wrap, MaxWidth = 380 }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted"));
    }

    private static void Fill(ComboBox box, (string Label, string Code)[] entries)
    {
        if (box.ItemsSource is IEnumerable<string> current && current.SequenceEqual(entries.Select(entry => entry.Label)))
        {
            return;
        }

        box.ItemsSource = entries.Select(entry => entry.Label).ToList();
        box.SelectedIndex = 0;
    }

    private static string Explanation(string category) => category switch
    {
        "Standard" => "Zellen im Standardformat haben kein bestimmtes Zahlenformat.",
        "Zahl" => "Für die allgemeine Darstellung von Zahlen.",
        "Währung" => "Für Geldbeträge mit Währungssymbol.",
        "Buchhaltung" => "Richtet Währungssymbole und Dezimalkommas in einer Spalte untereinander aus.",
        "Datum" or "Uhrzeit" => "Zeigt fortlaufende Zahlen als Datum bzw. Uhrzeit.",
        "Prozent" => "Multipliziert den Wert mit 100 und zeigt ihn mit Prozentzeichen.",
        "Text" => "Zellen im Textformat zeigen genau das, was eingegeben wurde – auch Zahlen.",
        "Benutzerdefiniert" => "Ein Formatcode wie #.##0,00 €;[Rot]-#.##0,00 € oder TT.MM.JJJJ.",
        _ => string.Empty,
    };

    /// <summary>The chosen format as a code in the German notation (null: Standard).</summary>
    private string? LocalCode()
    {
        var decimals = (int)(_decimals.Value ?? 2);
        var fraction = decimals > 0 ? "," + new string('0', decimals) : string.Empty;
        return Categories[Math.Max(0, _category.SelectedIndex)] switch
        {
            "Zahl" => (_thousands.IsChecked == true ? "#.##0" : "0") + fraction,
            "Währung" => $"#.##0{fraction} €",
            "Buchhaltung" => $"_-* #.##0{fraction} €_-;-* #.##0{fraction} €_-;_-* \"-\"?? €_-;_-@_-",
            "Datum" => DateFormats[Math.Max(0, _type.SelectedIndex)].Code,
            "Uhrzeit" => TimeFormats[Math.Max(0, Math.Min(_type.SelectedIndex, TimeFormats.Length - 1))].Code,
            "Prozent" => "0" + fraction + "%",
            "Bruch" => "# ?/?",
            "Wissenschaft" => "0" + fraction + "E+00",
            "Text" => "@",
            "Benutzerdefiniert" => string.IsNullOrWhiteSpace(_custom.Text) ? null : _custom.Text,
            _ => null,
        };
    }

    private void UpdateSample()
    {
        var session = _editor.Session;
        var value = session.ActiveSheet.GetValue(session.ActiveCell);
        if (value.IsBlank)
        {
            value = CellValue.FromNumber(1234.5);
        }

        try
        {
            var code = LocalCode() is { } local ? FormatCodes.ToCanonical(local, session.Locale) : "General";
            _sample.Text = NumberFormatter.Format(value, code, session.Locale, session.Workbook.Date1904).Text;
        }
        catch (FormatException)
        {
            _sample.Text = "Ungültiger Formatcode";
        }
    }

    // ===== Ausrichtung =====

    private Control AlignmentTab()
    {
        foreach (var box in new Control[] { _horizontal, _vertical, _indent, _wrap, _shrink, _merge, _rotation })
        {
            Changed(box);
        }

        AutomationProperties.SetName(_horizontal, "Horizontal");
        AutomationProperties.SetName(_vertical, "Vertikal");
        var left = new StackPanel { Spacing = 10, Children = { Field("Horizontal", _horizontal), Field("Vertikal", _vertical), Field("Einzug", _indent) } };
        var right = new StackPanel { Spacing = 10, Children = { Heading("Textsteuerung"), _wrap, _shrink, _merge, Field("Ausrichtung (Grad)", _rotation) } };
        return TwoColumns(left, right);
    }

    // ===== Schrift =====

    private Control FontTab()
    {
        _fontName.ItemsSource = CalcFonts.Families;
        _fontSize.ItemsSource = Core.Editing.EditingSession.FontSizeSteps.Select(size => size.ToString("0.#", German)).ToList();
        foreach (var box in new Control[] { _fontName, _fontStyle, _fontSize, _underline, _strike, _superscript, _subscript })
        {
            Changed(box);
        }

        _fontColor.Changed += (_, _) => _fontTouched |= !_loading;
        AutomationProperties.SetName(_fontName, "Schriftart");
        AutomationProperties.SetName(_fontStyle, "Schriftschnitt");
        var left = new StackPanel { Spacing = 10, Children = { Field("Schriftart", _fontName), Field("Schriftschnitt", _fontStyle), Field("Schriftgrad", _fontSize) } };
        var right = new StackPanel { Spacing = 10, Children = { Field("Unterstreichung", _underline), Field("Farbe", _fontColor), Heading("Effekte"), _strike, _superscript, _subscript } };
        return TwoColumns(left, right);
    }

    private bool _fontTouched;
    private bool _alignmentTouched;
    private bool _loading;

    private void Changed(Control control)
    {
        void Touch()
        {
            if (_loading)
            {
                return;
            }

            if (control == _fontName || control == _fontStyle || control == _fontSize || control == _underline || control == _strike || control == _superscript || control == _subscript)
            {
                _fontTouched = true;
            }
            else
            {
                _alignmentTouched = true;
            }
        }

        switch (control)
        {
            case ComboBox combo:
                combo.SelectionChanged += (_, _) => Touch();
                break;
            case CheckBox check:
                check.IsCheckedChanged += (_, _) => Touch();
                break;
            case NumericUpDown number:
                number.ValueChanged += (_, _) => Touch();
                break;
        }
    }

    // ===== Rahmen =====

    private Control BorderTab()
    {
        foreach (var check in new[] { _top, _bottom, _left, _right, _insideHorizontal, _insideVertical })
        {
            check.IsCheckedChanged += (_, _) => _bordersChanged |= !_loading;
        }

        _lineStyle.SelectionChanged += (_, _) => _bordersChanged |= !_loading;
        _lineColor.Changed += (_, _) => _bordersChanged |= !_loading;
        var none = new Button { Content = new ThixOffice.Desktop.Controls.IconLabel { Icon = "x", Text = "Keine" } };
        none.Click += (_, _) => SetEdges(false, false);
        var outline = new Button { Content = new ThixOffice.Desktop.Controls.IconLabel { Icon = "borders", Text = "Außen" } };
        outline.Click += (_, _) => SetEdges(true, null);
        var inside = new Button { Content = new ThixOffice.Desktop.Controls.IconLabel { Icon = "grid", Text = "Innen" } };
        inside.Click += (_, _) => SetEdges(null, true);
        var presets = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { none, outline, inside } };
        var edges = new UniformGrid { Columns = 2, Children = { _top, _bottom, _left, _right, _insideHorizontal, _insideVertical } };
        var left = new StackPanel { Spacing = 10, Children = { Field("Linienart", _lineStyle), Field("Farbe", _lineColor) } };
        var right = new StackPanel { Spacing = 10, Children = { Field("Voreinstellungen", presets), Field("Rahmen", edges) } };
        return TwoColumns(left, right);
    }

    private void SetEdges(bool? outline, bool? inside)
    {
        if (outline is { } outer)
        {
            _top.IsChecked = _bottom.IsChecked = _left.IsChecked = _right.IsChecked = outer;
        }

        if (inside is { } inner)
        {
            _insideHorizontal.IsChecked = _insideVertical.IsChecked = inner;
        }

        if (outline == false && inside == false)
        {
            _insideHorizontal.IsChecked = _insideVertical.IsChecked = false;
        }

        _bordersChanged = true;
    }

    // ===== Ausfüllen =====

    private Control FillTab()
    {
        _fill.Changed += (_, _) => _fillTouched |= !_loading;
        return new StackPanel { Margin = new Thickness(0, 12, 0, 0), Spacing = 10, Children = { Field("Hintergrundfarbe", _fill) } };
    }

    private bool _fillTouched;

    private static TextBlock Heading(string text) => new() { Text = text, FontWeight = FontWeight.SemiBold, Margin = new Thickness(0, 4, 0, 0) };

    private static Grid TwoColumns(Control left, Control right)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*"), Margin = new Thickness(0, 12, 0, 0) };
        Grid.SetColumn(right, 2);
        grid.Children.Add(left);
        grid.Children.Add(right);
        return grid;
    }

    // ===== Load and apply =====

    private void Load()
    {
        _loading = true;
        var style = _start;
        var format = NumberFormat.Parse(style.NumberFormat);
        var label = CalcCommands.NumberFormatLabel(_editor.Session);
        var local = FormatCodes.ToLocal(style.NumberFormat, _editor.Session.Locale);
        _category.SelectedIndex = label switch
        {
            "Standard" => 0,
            "Zahl" => 1,
            "Währung" => 2,
            "Buchhaltung" => 3,
            "Datum, kurz" or "Datum, lang" => 4,
            "Uhrzeit" => 5,
            "Prozent" => 6,
            "Bruch" => 7,
            "Wissenschaft" => 8,
            "Text" => 9,
            _ => format.IsDateTime ? 4 : 10,
        };
        var comma = local.IndexOf(',');
        _decimals.Value = comma >= 0 ? local[(comma + 1)..].TakeWhile(character => character is '0' or '#').Count() : format.IsGeneral ? 2 : 0;
        _thousands.IsChecked = local.Contains("#.##", StringComparison.Ordinal);
        _custom.Text = format.IsGeneral ? "Standard" : local;
        ShowNumberOptions();
        UpdateSample();

        _horizontal.SelectedIndex = style.Alignment.Horizontal switch
        {
            CellHorizontal.Left => 1,
            CellHorizontal.Center => 2,
            CellHorizontal.Right => 3,
            CellHorizontal.Fill => 4,
            CellHorizontal.Justify => 5,
            CellHorizontal.CenterContinuous => 6,
            CellHorizontal.Distributed => 7,
            _ => 0,
        };
        _vertical.SelectedIndex = style.Alignment.Vertical switch
        {
            CellVertical.Top => 1,
            CellVertical.Center => 2,
            CellVertical.Justify => 3,
            CellVertical.Distributed => 4,
            _ => 0,
        };
        _indent.Value = style.Alignment.Indent;
        _wrap.IsChecked = style.Alignment.WrapText;
        _shrink.IsChecked = style.Alignment.ShrinkToFit;
        _rotation.Value = style.Alignment.TextRotation is > 90 and <= 180 ? 90 - style.Alignment.TextRotation : style.Alignment.TextRotation;
        var session = _editor.Session;
        _merge.IsChecked = session.ActiveSheet.Merges.Find(session.ActiveCell.Row, session.ActiveCell.Column) is not null;

        _fontName.SelectedItem = CalcFonts.Families.FirstOrDefault(family => string.Equals(family, style.Font.Name, StringComparison.OrdinalIgnoreCase));
        _fontStyle.SelectedIndex = (style.Font.Bold ? 2 : 0) + (style.Font.Italic ? 1 : 0);
        var sizes = (IList<string>)_fontSize.ItemsSource!;
        var sizeText = style.Font.Size.ToString("0.#", German);
        if (!sizes.Contains(sizeText))
        {
            sizes.Add(sizeText);
        }

        _fontSize.SelectedItem = sizeText;
        _underline.SelectedIndex = (int)style.Font.Underline;
        _strike.IsChecked = style.Font.Strikethrough;
        _superscript.IsChecked = style.Font.Script == FontScript.Superscript;
        _subscript.IsChecked = style.Font.Script == FontScript.Subscript;
        _fontColor.Value = style.Font.Color is { } color ? GridPalette.Hex(color.Resolve(session.Workbook.Theme, GridPalette.Ink)) : null;

        var borders = style.Borders;
        _top.IsChecked = borders.Top.IsVisible;
        _bottom.IsChecked = borders.Bottom.IsVisible;
        _left.IsChecked = borders.Left.IsVisible;
        _right.IsChecked = borders.Right.IsVisible;

        _fill.Value = style.Fill.Pattern == FillPattern.Solid && (style.Fill.Foreground ?? style.Fill.Background) is { } fill
            ? GridPalette.Hex(fill.Resolve(session.Workbook.Theme, GridPalette.Sheet))
            : null;
        _loading = false;
    }

    protected override bool Apply()
    {
        var session = _editor.Session;
        string? canonical = null;
        if (_numberChanged)
        {
            var local = LocalCode();
            canonical = local is null || local == "Standard" ? "General" : FormatCodes.ToCanonical(local, session.Locale);
            try
            {
                NumberFormat.Parse(canonical);
            }
            catch (FormatException)
            {
                Tabs.SelectedIndex = 0;
                return false;
            }
        }

        var horizontal = _horizontal.SelectedIndex switch
        {
            1 => CellHorizontal.Left,
            2 => CellHorizontal.Center,
            3 => CellHorizontal.Right,
            4 => CellHorizontal.Fill,
            5 => CellHorizontal.Justify,
            6 => CellHorizontal.CenterContinuous,
            7 => CellHorizontal.Distributed,
            _ => CellHorizontal.General,
        };
        var vertical = _vertical.SelectedIndex switch
        {
            1 => CellVertical.Top,
            2 => CellVertical.Center,
            3 => CellVertical.Justify,
            4 => CellVertical.Distributed,
            _ => CellVertical.Bottom,
        };
        var rotation = (int)(_rotation.Value ?? 0);
        var size = double.TryParse(_fontSize.SelectedItem as string, NumberStyles.Float, German, out var parsed) ? parsed : _start.Font.Size;
        var fontName = _fontName.SelectedItem as string ?? _start.Font.Name;
        var fontColor = GridPalette.ToCalc(_fontColor.Value);
        var fill = GridPalette.ToCalc(_fill.Value);
        var numberChanged = _numberChanged;
        var alignmentTouched = _alignmentTouched;
        var fontTouched = _fontTouched;
        var fillTouched = _fillTouched;
        var wrap = _wrap.IsChecked == true;
        var shrink = _shrink.IsChecked == true;
        var indent = (int)(_indent.Value ?? 0);
        var bold = _fontStyle.SelectedIndex >= 2;
        var italic = _fontStyle.SelectedIndex % 2 == 1;
        var underline = (UnderlineKind)Math.Max(0, _underline.SelectedIndex);
        var strike = _strike.IsChecked == true;
        var script = _superscript.IsChecked == true ? FontScript.Superscript : _subscript.IsChecked == true ? FontScript.Subscript : FontScript.Baseline;

        var applied = true;
        if (numberChanged || alignmentTouched || fontTouched || fillTouched)
        {
            applied &= Run(() => session.ApplyStyle(style =>
            {
                if (numberChanged)
                {
                    style = style with { NumberFormat = canonical! };
                }

                if (alignmentTouched)
                {
                    style = style with
                    {
                        Alignment = style.Alignment with
                        {
                            Horizontal = horizontal, Vertical = vertical, Indent = indent, WrapText = wrap, ShrinkToFit = shrink,
                            TextRotation = rotation < 0 ? 90 - rotation : rotation,
                        },
                    };
                }

                if (fontTouched)
                {
                    style = style with
                    {
                        Font = style.Font with
                        {
                            Name = fontName, Size = size, Bold = bold, Italic = italic, Underline = underline, Strikethrough = strike, Script = script, Color = fontColor,
                        },
                    };
                }

                if (fillTouched)
                {
                    style = style with { Fill = fill is { } color ? CellFill.Solid(color) : CellFill.None };
                }

                return style;
            }, "Zellen formatieren"));
        }

        if (alignmentTouched && (_merge.IsChecked == true) != (session.ActiveSheet.Merges.Find(session.ActiveCell.Row, session.ActiveCell.Column) is not null))
        {
            applied &= Run(() =>
            {
                if (_merge.IsChecked == true)
                {
                    session.Merge();
                }
                else
                {
                    session.Unmerge();
                }
            });
        }

        if (_bordersChanged)
        {
            var edgeStyle = _lineStyle.SelectedIndex switch
            {
                1 => BorderStyle.Medium,
                2 => BorderStyle.Thick,
                3 => BorderStyle.Double,
                4 => BorderStyle.Dashed,
                5 => BorderStyle.Dotted,
                _ => BorderStyle.Thin,
            };
            var line = new BorderEdge(edgeStyle, GridPalette.ToCalc(_lineColor.Value));
            BorderEdge Edge(CheckBox check) => check.IsChecked == true ? line : default;
            applied &= Run(() => session.SetBorderEdges(Edge(_top), Edge(_bottom), Edge(_left), Edge(_right), Edge(_insideHorizontal), Edge(_insideVertical)));
        }

        return applied;
    }

    private bool Run(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (ThixOffice.Core.Models.BusinessException exception)
        {
            _ = QuestionDialog.ShowMessageAsync(this, "Zellen formatieren", exception.Message);
            return false;
        }
    }
}

/// <summary>A colour choice of a dialog: a button with the colour that opens the palette (theme colours, standard colours) and "none".</summary>
public sealed class ColorChoice : Button
{
    private readonly Border _swatch = new() { Width = 46, Height = 16, CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1) };
    private readonly TextBlock _label = new() { VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
    private readonly string _noneLabel;
    private string? _value;

    public ColorChoice(IReadOnlyList<string> palette, string noneLabel)
    {
        _noneLabel = noneLabel;
        _swatch.Use(Border.BorderBrushProperty, "Mat.Border");
        Content = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { _swatch, _label, new MatIcon { Icon = "chevron-down", Size = 12 } } };
        MinWidth = 180;
        var grid = new UniformGrid { Columns = 10 };
        foreach (var hex in palette)
        {
            var color = hex;
            var swatch = new Button { Width = 20, Height = 20, Padding = new Thickness(0), Margin = new Thickness(1), Background = GridPalette.Brush(hex) };
            ToolTip.SetTip(swatch, hex);
            swatch.Click += (_, _) =>
            {
                Value = color;
                Flyout?.Hide();
            };
            grid.Children.Add(swatch);
        }

        var none = new Button { Content = noneLabel, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, Margin = new Thickness(0, 0, 0, 6) };
        none.Click += (_, _) =>
        {
            Value = null;
            Flyout?.Hide();
        };
        Flyout = new Flyout { Content = new StackPanel { Margin = new Thickness(6), Children = { none, grid } } };
        AutomationProperties.SetName(this, "Farbe");
        Show();
    }

    public event EventHandler? Changed;

    /// <summary>"#rrggbb" or null (Automatisch / Keine Farbe).</summary>
    public string? Value
    {
        get => _value;
        set
        {
            _value = value;
            Show();
            Changed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Show()
    {
        _label.Text = _value is null ? _noneLabel : GridPalette.ColorName(_value);
        if (_value is null)
        {
            _swatch.Background = null;
        }
        else
        {
            _swatch.Background = GridPalette.Brush(_value);
        }
    }
}
