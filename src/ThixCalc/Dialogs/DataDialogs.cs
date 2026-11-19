using MatDesktop;
using System.Globalization;
using SortKey = ThixCalc.Core.Editing.SortKey;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>A dialog of ThixCalc that runs session commands: a refusal of the core shows over the dialog and keeps it open.</summary>
public abstract class CalcDialog(string title, double width) : OfficeDialog(title, width)
{
    protected bool Run(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (BusinessException exception)
        {
            _ = QuestionDialog.ShowMessageAsync(this, Title ?? "", exception.Message);
            return false;
        }
    }

    /// <summary>The column names of a range: its header row's texts, or "Spalte B" when it has none.</summary>
    protected static IReadOnlyList<string> ColumnNames(Worksheet sheet, CellRange range, bool hasHeader, CalcLocale? locale = null) =>
        Enumerable.Range(range.FirstColumn, range.ColumnCount)
            .Select(column => hasHeader && sheet.GetValue(range.FirstRow, column) is { IsBlank: false } value
                ? NumberFormatter.Format(value, "General", locale ?? CalcLocale.German).Text
                : $"Spalte {A1.ColumnName(column)}")
            .ToList();
}

/// <summary>
/// Daten › Sortieren: the list (the current region of the active cell or the selection) by up to several keys, each
/// ascending or descending, with or without a header row; Ebene hinzufügen / löschen like Excel.
/// </summary>
public sealed class SortDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly CellRange _range;
    private readonly CheckBox _header = new() { Content = "Daten haben Überschriften" };
    private readonly CheckBox _caseSensitive = new() { Content = "Groß-/Kleinschreibung beachten" };
    private readonly StackPanel _levels = new() { Spacing = 8 };

    private SortDialog(CalcEditor editor)
        : base("Sortieren", 560)
    {
        _editor = editor;
        var session = editor.Session;
        _range = session.Selection.IsSingleCell ? Navigation.CurrentRegion(session.ActiveSheet, session.ActiveCell) : session.Selection;
        _header.IsChecked = LooksLikeHeader(session.ActiveSheet, _range);
        _header.IsCheckedChanged += (_, _) => RefreshNames();
        AddLevel(session.ActiveCell.Column - _range.FirstColumn);
        var add = new Button { Content = new IconLabel { Icon = "plus", Text = "Ebene hinzufügen" } };
        add.Click += (_, _) => AddLevel(0);
        var remove = new Button { Content = new IconLabel { Icon = "trash", Text = "Ebene löschen" } };
        remove.Click += (_, _) =>
        {
            if (_levels.Children.Count > 1)
            {
                _levels.Children.RemoveAt(_levels.Children.Count - 1);
            }
        };
        var toolbar = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { add, remove } };
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children = { new TextBlock { Text = $"Bereich {_range}" }.Use(TextBlock.ForegroundProperty, "Mat.TextMuted"), toolbar, _levels, _header, _caseSensitive },
        });
    }

    /// <summary>The sort levels (column, order) – tests choose in them.</summary>
    public IReadOnlyList<(ComboBox Column, ComboBox Order)> Levels => [.. _levels.Children.OfType<Grid>().Select(row => ((ComboBox)row.Children[1], (ComboBox)row.Children[2]))];

    public CheckBox HasHeader => _header;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new SortDialog(editor).ShowAsync(owner);

    internal static SortDialog Create(CalcEditor editor) => new(editor);

    /// <summary>A first row of texts over a row with a number or a date below: a header row (Excel's guess).</summary>
    public static bool LooksLikeHeader(Worksheet sheet, CellRange range)
    {
        if (range.RowCount < 2)
        {
            return false;
        }

        var first = Enumerable.Range(range.FirstColumn, range.ColumnCount).Select(column => sheet.GetValue(range.FirstRow, column)).ToList();
        var second = Enumerable.Range(range.FirstColumn, range.ColumnCount).Select(column => sheet.GetValue(range.FirstRow + 1, column)).ToList();
        var styleDiffers = Enumerable.Range(range.FirstColumn, range.ColumnCount).Any(column => sheet.GetStyle(range.FirstRow, column).Font.Bold && !sheet.GetStyle(range.FirstRow + 1, column).Font.Bold);
        return first.All(value => value.IsText || value.IsBlank) && first.Any(value => value.IsText) && (styleDiffers || second.Any(value => value.IsNumber));
    }

    private void AddLevel(int column)
    {
        var names = ColumnNames(_editor.Session.ActiveSheet, _range, _header.IsChecked == true, _editor.Session.Locale);
        var label = new TextBlock { Text = _levels.Children.Count == 0 ? "Sortieren nach" : "Dann nach", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center, Width = 110 };
        var columns = new ComboBox { ItemsSource = names, SelectedIndex = Math.Clamp(column, 0, names.Count - 1), MinWidth = 200 };
        AutomationProperties.SetName(columns, "Spalte");
        var order = new ComboBox { ItemsSource = new[] { "A bis Z (aufsteigend)", "Z bis A (absteigend)" }, SelectedIndex = 0, MinWidth = 190 };
        AutomationProperties.SetName(order, "Reihenfolge");
        var row = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*,Auto"), ColumnSpacing = 8 };
        Grid.SetColumn(columns, 1);
        Grid.SetColumn(order, 2);
        row.Children.Add(label);
        row.Children.Add(columns);
        row.Children.Add(order);
        _levels.Children.Add(row);
    }

    private void RefreshNames()
    {
        var names = ColumnNames(_editor.Session.ActiveSheet, _range, _header.IsChecked == true, _editor.Session.Locale);
        foreach (var (column, _) in Levels)
        {
            var selected = column.SelectedIndex;
            column.ItemsSource = names;
            column.SelectedIndex = selected;
        }
    }

    protected override bool Apply()
    {
        var keys = Levels.Select(level => new SortKey(Math.Max(0, level.Column.SelectedIndex), level.Order.SelectedIndex == 1)).ToList();
        var options = new SortOptions(_header.IsChecked == true, _caseSensitive.IsChecked == true);
        return Run(() => _editor.Session.Sort(_range, keys, options));
    }
}

/// <summary>Daten › Text in Spalten: the separators (Tabstopp, Semikolon, Komma, Leerzeichen, Andere) of the selection's first column.</summary>
public sealed class TextToColumnsDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly CheckBox _tab = new() { Content = "Tabstopp" };
    private readonly CheckBox _semicolon = new() { Content = "Semikolon", IsChecked = true };
    private readonly CheckBox _comma = new() { Content = "Komma" };
    private readonly CheckBox _space = new() { Content = "Leerzeichen" };
    private readonly TextBox _other = new() { Width = 60, MaxLength = 1 };
    private readonly CheckBox _consecutive = new() { Content = "Aufeinanderfolgende Trennzeichen als ein Zeichen behandeln" };

    private TextToColumnsDialog(CalcEditor editor)
        : base("Text in Spalten", 480)
    {
        _editor = editor;
        var other = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, Children = { new TextBlock { Text = "Andere:", VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center }, _other } };
        SetBody(new StackPanel
        {
            Spacing = 10,
            Children = { Field("Trennzeichen", new StackPanel { Spacing = 6, Children = { _tab, _semicolon, _comma, _space, other } }), _consecutive },
        });
    }

    public CheckBox Semicolon => _semicolon;

    public CheckBox Comma => _comma;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new TextToColumnsDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        var separators = new List<char>();
        if (_tab.IsChecked == true)
        {
            separators.Add('\t');
        }

        if (_semicolon.IsChecked == true)
        {
            separators.Add(';');
        }

        if (_comma.IsChecked == true)
        {
            separators.Add(',');
        }

        if (_space.IsChecked == true)
        {
            separators.Add(' ');
        }

        if (_other.Text is { Length: 1 } other)
        {
            separators.Add(other[0]);
        }

        var session = _editor.Session;
        return Run(() => session.TextToColumns(session.Selection, separators, _consecutive.IsChecked == true));
    }
}

/// <summary>Daten › Duplikate entfernen: which columns decide; the result says how many rows were removed.</summary>
public sealed class RemoveDuplicatesDialog : CalcDialog
{
    private readonly CalcEditor _editor;
    private readonly CellRange _range;
    private readonly CheckBox _header = new() { Content = "Daten haben Überschriften" };
    private readonly StackPanel _columns = new() { Spacing = 4 };

    private RemoveDuplicatesDialog(CalcEditor editor)
        : base("Duplikate entfernen", 460)
    {
        _editor = editor;
        var session = editor.Session;
        _range = session.Selection.IsSingleCell ? Navigation.CurrentRegion(session.ActiveSheet, session.ActiveCell) : session.Selection;
        _header.IsChecked = SortDialog.LooksLikeHeader(session.ActiveSheet, _range);
        _header.IsCheckedChanged += (_, _) => Fill();
        Fill();
        SetBody(new StackPanel { Spacing = 10, Children = { _header, Field("Spalten", new ScrollViewer { Content = _columns, MaxHeight = 220 }) } });
    }

    /// <summary>How many rows the last OK removed.</summary>
    public int Removed { get; private set; }

    public static async Task ShowAsync(Window owner, CalcEditor editor)
    {
        var dialog = new RemoveDuplicatesDialog(editor);
        if (await dialog.ShowAsync(owner))
        {
            var removed = dialog.Removed;
            await QuestionDialog.ShowMessageAsync(owner, "Duplikate entfernen", removed == 0
                ? "Es wurden keine doppelten Werte gefunden."
                : removed == 1 ? "1 doppelter Wert wurde entfernt." : $"{removed} doppelte Werte wurden entfernt.");
        }
    }

    private void Fill()
    {
        var names = ColumnNames(_editor.Session.ActiveSheet, _range, _header.IsChecked == true, _editor.Session.Locale);
        _columns.Children.Clear();
        foreach (var name in names)
        {
            _columns.Children.Add(new CheckBox { Content = name, IsChecked = true });
        }
    }

    protected override bool Apply()
    {
        var columns = _columns.Children.OfType<CheckBox>().Select((check, index) => (check, index)).Where(entry => entry.check.IsChecked == true).Select(entry => entry.index).ToList();
        if (columns.Count == 0)
        {
            return false;
        }

        return Run(() => Removed = _editor.Session.RemoveDuplicates(_range, columns, _header.IsChecked == true));
    }
}

/// <summary>
/// Daten › Datenüberprüfung: Zulassen (Jeder Wert, Ganze Zahl, Dezimal, Liste, Datum, Textlänge), Daten (zwischen, gleich,
/// größer …), Minimum/Maximum or the list's entries ("Ja;Nein" or a range), and the error message; tabs Einstellungen |
/// Eingabemeldung | Fehlermeldung like Excel.
/// </summary>
public sealed class ValidationDialog : CalcDialog
{
    private static readonly (string Label, ValidationType Type)[] Types =
    [
        ("Jeden Wert", ValidationType.Any), ("Ganze Zahl", ValidationType.WholeNumber), ("Dezimal", ValidationType.Decimal),
        ("Liste", ValidationType.List), ("Datum", ValidationType.Date), ("Zeit", ValidationType.Time), ("Textlänge", ValidationType.TextLength),
    ];

    private static readonly (string Label, ComparisonOperator Operator)[] Operators =
    [
        ("zwischen", ComparisonOperator.Between), ("nicht zwischen", ComparisonOperator.NotBetween), ("gleich", ComparisonOperator.Equal),
        ("ungleich", ComparisonOperator.NotEqual), ("größer als", ComparisonOperator.GreaterThan), ("kleiner als", ComparisonOperator.LessThan),
        ("größer oder gleich", ComparisonOperator.GreaterThanOrEqual), ("kleiner oder gleich", ComparisonOperator.LessThanOrEqual),
    ];

    private readonly CalcEditor _editor;
    private readonly ComboBox _type = new() { ItemsSource = Types.Select(type => type.Label).ToList(), SelectedIndex = 0, MinWidth = 220 };
    private readonly ComboBox _operator = new() { ItemsSource = Operators.Select(entry => entry.Label).ToList(), SelectedIndex = 0, MinWidth = 220 };
    private readonly TextBox _first = new();
    private readonly TextBox _second = new();
    private readonly CheckBox _ignoreBlank = new() { Content = "Leere Zellen ignorieren", IsChecked = true };
    private readonly TextBox _promptTitle = new();
    private readonly TextBox _prompt = new() { AcceptsReturn = true, Height = 70, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly ComboBox _errorStyle = new() { ItemsSource = new[] { "Stopp", "Warnung", "Information" }, SelectedIndex = 0, MinWidth = 160 };
    private readonly TextBox _errorTitle = new();
    private readonly TextBox _error = new() { AcceptsReturn = true, Height = 70, TextWrapping = Avalonia.Media.TextWrapping.Wrap };
    private readonly MatDesktop.MatFormField _firstField;
    private readonly MatDesktop.MatFormField _secondField;
    private readonly MatDesktop.MatFormField _operatorField;

    private ValidationDialog(CalcEditor editor)
        : base("Datenüberprüfung", 520)
    {
        _editor = editor;
        _firstField = Field("Minimum", _first);
        _secondField = Field("Maximum", _second);
        _operatorField = Field("Daten", _operator);
        AutomationProperties.SetName(_type, "Zulassen");
        _type.SelectionChanged += (_, _) => UpdateFields();
        _operator.SelectionChanged += (_, _) => UpdateFields();
        var settings = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0), Children = { Field("Zulassen", _type), _operatorField, _firstField, _secondField, _ignoreBlank } };
        var input = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0), Children = { Field("Titel", _promptTitle), Field("Eingabemeldung", _prompt) } };
        var error = new StackPanel { Spacing = 10, Margin = new Thickness(0, 12, 0, 0), Children = { Field("Typ", _errorStyle), Field("Titel", _errorTitle), Field("Fehlermeldung", _error) } };
        SetBody(new TabControl
        {
            Items =
            {
                new TabItem { Header = "Einstellungen", Content = settings },
                new TabItem { Header = "Eingabemeldung", Content = input },
                new TabItem { Header = "Fehlermeldung", Content = error },
            },
        });
        Load();
        UpdateFields();
    }

    public ComboBox Allow => _type;

    public TextBox Source => _first;

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new ValidationDialog(editor).ShowAsync(owner);

    private void Load()
    {
        var session = _editor.Session;
        if (Validation.RuleAt(session.ActiveSheet, session.ActiveCell) is not { } rule)
        {
            return;
        }

        _type.SelectedIndex = Math.Max(0, Array.FindIndex(Types, type => type.Type == rule.Type));
        _operator.SelectedIndex = Math.Max(0, Array.FindIndex(Operators, entry => entry.Operator == rule.Operator));
        _first.Text = rule.Type == ValidationType.List ? ListText(rule.Formula1) : rule.Formula1;
        _second.Text = rule.Formula2;
        _ignoreBlank.IsChecked = rule.AllowBlank;
        _promptTitle.Text = rule.PromptTitle;
        _prompt.Text = rule.Prompt;
        _errorStyle.SelectedIndex = (int)rule.ErrorStyle;
        _errorTitle.Text = rule.ErrorTitle;
        _error.Text = rule.Error;
    }

    /// <summary>A literal list "a,b,c" (canonical) as the German user types it: a;b;c.</summary>
    private static string? ListText(string? formula) =>
        formula is { Length: > 1 } text && text.StartsWith('"') && text.EndsWith('"') ? text[1..^1].Replace(',', ';') : formula;

    private void UpdateFields()
    {
        var type = Types[Math.Max(0, _type.SelectedIndex)].Type;
        var op = Operators[Math.Max(0, _operator.SelectedIndex)].Operator;
        var between = op is ComparisonOperator.Between or ComparisonOperator.NotBetween;
        _operatorField.IsVisible = type is not (ValidationType.Any or ValidationType.List);
        _firstField.IsVisible = type != ValidationType.Any;
        _secondField.IsVisible = _operatorField.IsVisible && between;
        _firstField.Label = type == ValidationType.List ? "Quelle (z. B. Ja;Nein oder =$A$1:$A$5)" : between ? "Minimum" : "Wert";
    }

    protected override bool Apply()
    {
        var session = _editor.Session;
        var type = Types[Math.Max(0, _type.SelectedIndex)].Type;
        string? Canonical(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
            {
                return null;
            }

            text = text.Trim();
            if (type == ValidationType.List && !text.StartsWith('='))
            {
                return "\"" + string.Join(',', text.Split(';', StringSplitOptions.TrimEntries)) + "\"";
            }

            if (text.StartsWith('='))
            {
                return text[1..];
            }

            return ValueParser.TryParseNumber(text, session.Locale, session.Workbook.Date1904, out var number)
                ? number.ToString("R", CultureInfo.InvariantCulture)
                : text;
        }

        var rule = new DataValidation
        {
            Ranges = session.SelectedRanges,
            Type = type,
            Operator = Operators[Math.Max(0, _operator.SelectedIndex)].Operator,
            Formula1 = Canonical(_first.Text),
            Formula2 = Canonical(_second.Text),
            AllowBlank = _ignoreBlank.IsChecked == true,
            PromptTitle = _promptTitle.Text,
            Prompt = _prompt.Text,
            ErrorStyle = (ValidationErrorStyle)Math.Max(0, _errorStyle.SelectedIndex),
            ErrorTitle = _errorTitle.Text,
            Error = _error.Text,
        };
        if (type != ValidationType.Any && rule.Formula1 is null)
        {
            _firstField.Error = "Bitte geben Sie einen Wert ein.";
            return false;
        }

        return Run(() => session.AddValidation(rule));
    }
}
