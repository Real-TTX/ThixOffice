using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formatting;
using ThixCalc.Editor;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>
/// Inhalte einfügen (Strg+Alt+V): what to paste (Alles, Formeln, Werte, Formate, Alles außer Rahmen, Werte und Zahlenformate),
/// Vorgang (Keine, Addieren, Subtrahieren, Multiplizieren, Dividieren), Leerzellen überspringen and Transponieren.
/// </summary>
public sealed class PasteSpecialDialog : OfficeDialog
{
    private static readonly (string Label, PasteMode Mode)[] Modes =
    [
        ("Alles", PasteMode.All), ("Formeln", PasteMode.Formulas), ("Werte", PasteMode.Values), ("Formate", PasteMode.Formats),
        ("Alles außer Rahmen", PasteMode.AllExceptBorders), ("Werte und Zahlenformate", PasteMode.ValuesAndNumberFormats),
    ];

    private static readonly (string Label, PasteOperation Operation)[] Operations =
    [
        ("Keine", PasteOperation.None), ("Addieren", PasteOperation.Add), ("Subtrahieren", PasteOperation.Subtract),
        ("Multiplizieren", PasteOperation.Multiply), ("Dividieren", PasteOperation.Divide),
    ];

    private readonly CalcEditor _editor;
    private readonly List<RadioButton> _modes = [];
    private readonly List<RadioButton> _operations = [];

    private PasteSpecialDialog(CalcEditor editor)
        : base("Inhalte einfügen", 460)
    {
        _editor = editor;
        var modes = new StackPanel { Spacing = 6 };
        foreach (var (label, _) in Modes)
        {
            var button = new RadioButton { Content = label, GroupName = "pasteMode", IsChecked = _modes.Count == 0 };
            _modes.Add(button);
            modes.Children.Add(button);
        }

        var operations = new StackPanel { Spacing = 6 };
        foreach (var (label, _) in Operations)
        {
            var button = new RadioButton { Content = label, GroupName = "pasteOperation", IsChecked = _operations.Count == 0 };
            _operations.Add(button);
            operations.Children.Add(button);
        }

        SkipBlanks = new CheckBox { Content = "Leerzellen überspringen" };
        Transpose = new CheckBox { Content = "Transponieren" };
        var columns = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        var right = Field("Vorgang", operations);
        Grid.SetColumn(right, 2);
        columns.Children.Add(Field("Einfügen", modes));
        columns.Children.Add(right);
        SetBody(new StackPanel { Spacing = 14, Children = { columns, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 24, Children = { SkipBlanks, Transpose } } } });
    }

    public CheckBox SkipBlanks { get; }

    public CheckBox Transpose { get; }

    public static Task<bool> ShowAsync(Avalonia.Controls.Window owner, CalcEditor editor) => Create(editor).ShowAsync(owner);

    public static PasteSpecialDialog Create(CalcEditor editor) => new(editor);

    /// <summary>Chooses a mode and an operation (tests).</summary>
    public void Choose(PasteMode mode, PasteOperation operation)
    {
        _modes[Array.FindIndex(Modes, entry => entry.Mode == mode)].IsChecked = true;
        _operations[Array.FindIndex(Operations, entry => entry.Operation == operation)].IsChecked = true;
    }

    /// <summary>The options the dialog shows now.</summary>
    public PasteOptions Options => new(
        Modes[Math.Max(0, _modes.FindIndex(button => button.IsChecked == true))].Mode,
        Transpose.IsChecked == true,
        SkipBlanks.IsChecked == true,
        Operations[Math.Max(0, _operations.FindIndex(button => button.IsChecked == true))].Operation);

    protected override bool Apply()
    {
        var options = Options;
        _ = _editor.Clipboard.PasteAsync(options);
        return true;
    }
}

/// <summary>
/// Reihe (Start › Ausfüllen › Reihe …): Reihe in Zeilen/Spalten, Typ (Arithmetisch, Geometrisch, Datum), Zeiteinheit (Tag,
/// Wochentag, Monat, Jahr), Inkrement and Endwert – numbers typed as in the cells (German decimals).
/// </summary>
public sealed class SeriesDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly RadioButton _rows = new() { Content = "Zeilen", GroupName = "seriesIn" };
    private readonly RadioButton _columns = new() { Content = "Spalten", GroupName = "seriesIn" };
    private readonly RadioButton _linear = new() { Content = "Arithmetisch", GroupName = "seriesType", IsChecked = true };
    private readonly RadioButton _growth = new() { Content = "Geometrisch", GroupName = "seriesType" };
    private readonly RadioButton _date = new() { Content = "Datum", GroupName = "seriesType" };
    private readonly RadioButton[] _units =
    [
        new() { Content = "Tag", GroupName = "seriesUnit", IsChecked = true }, new() { Content = "Wochentag", GroupName = "seriesUnit" },
        new() { Content = "Monat", GroupName = "seriesUnit" }, new() { Content = "Jahr", GroupName = "seriesUnit" },
    ];

    private readonly MatFormField _stepField;
    private readonly MatFormField _stopField;

    private SeriesDialog(CalcEditor editor)
        : base("Reihe", 520)
    {
        _editor = editor;
        var selection = editor.Session.Selection;
        (selection.ColumnCount > selection.RowCount ? _rows : _columns).IsChecked = true;
        StepBox = new TextBox { Text = "1", MinWidth = 120 };
        StopBox = new TextBox { MinWidth = 120 };
        AutomationProperties.SetName(StepBox, "Inkrement");
        AutomationProperties.SetName(StopBox, "Endwert");
        _stepField = Field("Inkrement", StepBox);
        _stopField = Field("Endwert", StopBox);
        var units = new StackPanel { Spacing = 6 };
        foreach (var unit in _units)
        {
            units.Children.Add(unit);
            unit.IsEnabled = false;
        }

        _date.IsCheckedChanged += (_, _) =>
        {
            foreach (var unit in _units)
            {
                unit.IsEnabled = _date.IsChecked == true;
            }
        };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,16,*,16,*") };
        var type = Field("Typ", new StackPanel { Spacing = 6, Children = { _linear, _growth, _date } });
        var unitField = Field("Zeiteinheit", units);
        Grid.SetColumn(type, 2);
        Grid.SetColumn(unitField, 4);
        grid.Children.Add(Field("Reihe in", new StackPanel { Spacing = 6, Children = { _rows, _columns } }));
        grid.Children.Add(type);
        grid.Children.Add(unitField);
        SetBody(new StackPanel
        {
            Spacing = 14,
            Children = { grid, new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { _stepField, _stopField } } },
        });
    }

    public TextBox StepBox { get; }

    public TextBox StopBox { get; }

    public static Task<bool> ShowAsync(Avalonia.Controls.Window owner, CalcEditor editor) => Create(editor).ShowAsync(owner);

    public static SeriesDialog Create(CalcEditor editor) => new(editor);

    /// <summary>Chooses the date type and a unit (tests).</summary>
    public void ChooseDate(SeriesDateUnit unit)
    {
        _date.IsChecked = true;
        _units[(int)unit].IsChecked = true;
    }

    protected override bool Apply()
    {
        _stepField.Error = null;
        _stopField.Error = null;
        var locale = _editor.Session.Locale;
        if (!TryNumber(StepBox.Text, locale, out var step))
        {
            _stepField.Error = "Geben Sie eine Zahl ein.";
            return false;
        }

        double? stop = null;
        if (!string.IsNullOrWhiteSpace(StopBox.Text))
        {
            if (!TryNumber(StopBox.Text, locale, out var end))
            {
                _stopField.Error = "Geben Sie eine Zahl oder ein Datum ein.";
                return false;
            }

            stop = end;
        }

        var options = new SeriesOptions(
            _rows.IsChecked == true,
            _growth.IsChecked == true ? SeriesType.Growth : _date.IsChecked == true ? SeriesType.Date : SeriesType.Linear,
            step,
            stop,
            (SeriesDateUnit)Math.Max(0, Array.FindIndex(_units, unit => unit.IsChecked == true)));
        try
        {
            _editor.Session.FillSeries(options);
            return true;
        }
        catch (BusinessException exception)
        {
            (exception.Field == "stop" ? _stopField : _stepField).Error = exception.Message;
            return false;
        }
    }

    /// <summary>A number or a date as typed into a cell.</summary>
    private bool TryNumber(string? text, Core.Globalization.CalcLocale locale, out double number)
    {
        number = 0;
        var parsed = ValueParser.ParseInput(text ?? string.Empty, locale, _editor.Session.Workbook.Date1904, () => DateTime.Now);
        if (parsed.Kind == InputKind.Empty || !parsed.Value.IsNumber)
        {
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out number);
        }

        number = parsed.Value.Number;
        return true;
    }
}
