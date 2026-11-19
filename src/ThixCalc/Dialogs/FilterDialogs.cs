using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>A condition of the custom filter as the user reads it: label, operator, and the wildcards around the value.</summary>
public sealed record FilterCondition(string Label, FilterOperator Operator, string Prefix = "", string Suffix = "");

/// <summary>
/// Benutzerdefinierter AutoFilter: "Zeilen anzeigen: Spalte" with two conditions (ist gleich, ist größer als, beginnt mit,
/// enthält …) joined by Und / Oder – also what the entries of Zahlenfilter, Textfilter and Datumsfilter open, preset.
/// </summary>
public sealed class CustomFilterDialog : OfficeDialog
{
    public static IReadOnlyList<FilterCondition> Conditions { get; } =
    [
        new("ist gleich", FilterOperator.Equal), new("ist nicht gleich", FilterOperator.NotEqual),
        new("ist größer als", FilterOperator.GreaterThan), new("ist größer oder gleich", FilterOperator.GreaterThanOrEqual),
        new("ist kleiner als", FilterOperator.LessThan), new("ist kleiner oder gleich", FilterOperator.LessThanOrEqual),
        new("beginnt mit", FilterOperator.Equal, Suffix: "*"), new("beginnt nicht mit", FilterOperator.NotEqual, Suffix: "*"),
        new("endet mit", FilterOperator.Equal, Prefix: "*"), new("endet nicht mit", FilterOperator.NotEqual, Prefix: "*"),
        new("enthält", FilterOperator.Equal, "*", "*"), new("enthält nicht", FilterOperator.NotEqual, "*", "*"),
    ];

    private readonly CalcEditor _editor;
    private readonly int _offset;

    private CustomFilterDialog(CalcEditor editor, int offset, string column, int first, int? second, bool and)
        : base("Benutzerdefinierter AutoFilter", 520)
    {
        _editor = editor;
        _offset = offset;
        FirstOperator = Operators(first);
        SecondOperator = Operators(second ?? -1);
        FirstValue = new TextBox { MinWidth = 220 };
        SecondValue = new TextBox { MinWidth = 220 };
        AutomationProperties.SetName(FirstValue, "Erster Wert");
        AutomationProperties.SetName(SecondValue, "Zweiter Wert");
        And = new RadioButton { Content = "Und", GroupName = "filterJoin", IsChecked = and };
        Or = new RadioButton { Content = "Oder", GroupName = "filterJoin", IsChecked = !and };
        var hint = new TextBlock { Text = "? steht für ein einzelnes Zeichen, * für eine beliebige Zeichenfolge.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        hint.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        SetBody(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                new TextBlock { Text = $"Zeilen anzeigen: {column}" },
                Row(FirstOperator, FirstValue),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 20, Children = { And, Or } },
                Row(SecondOperator, SecondValue),
                hint,
            },
        });
    }

    public ComboBox FirstOperator { get; }

    public ComboBox SecondOperator { get; }

    public TextBox FirstValue { get; }

    public TextBox SecondValue { get; }

    public RadioButton And { get; }

    public RadioButton Or { get; }

    /// <summary>The dialog for filter column <paramref name="offset"/>, preset with condition <paramref name="first"/> (and <paramref name="second"/>: "zwischen").</summary>
    public static CustomFilterDialog Create(CalcEditor editor, int offset, string column, int first = 0, int? second = null, bool and = true) =>
        new(editor, offset, column, first, second, and);

    private static ComboBox Operators(int selected)
    {
        var box = new ComboBox { ItemsSource = new[] { "(leer)" }.Concat(Conditions.Select(condition => condition.Label)).ToList(), SelectedIndex = selected + 1, MinWidth = 190 };
        AutomationProperties.SetName(box, "Bedingung");
        return box;
    }

    private static StackPanel Row(ComboBox condition, TextBox value) => new() { Orientation = Orientation.Horizontal, Spacing = 10, Children = { condition, value } };

    /// <summary>The filter the dialog says, or null when no condition is filled in.</summary>
    public FilterColumn? Filter()
    {
        var conditions = new List<CustomFilter>();
        foreach (var (box, value) in new[] { (FirstOperator, FirstValue), (SecondOperator, SecondValue) })
        {
            if (box.SelectedIndex <= 0 || string.IsNullOrEmpty(value.Text))
            {
                continue;
            }

            var condition = Conditions[box.SelectedIndex - 1];
            conditions.Add(new CustomFilter(condition.Operator, condition.Prefix + value.Text + condition.Suffix));
        }

        return conditions.Count == 0 ? null : new FilterColumn { Custom = conditions, CustomAnd = And.IsChecked == true };
    }

    protected override bool Apply()
    {
        var filter = Filter();
        _ = _editor.RunAsync("Filtern", () => _editor.Session.SetFilter(_offset, filter));
        return true;
    }
}

/// <summary>Top 10 AutoFilter: show the [Obersten|Untersten] [10] [Elemente|Prozent].</summary>
public sealed class TopTenDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly int _offset;

    private TopTenDialog(CalcEditor editor, int offset)
        : base("Top 10 AutoFilter", 460)
    {
        _editor = editor;
        _offset = offset;
        Direction = new ComboBox { ItemsSource = new[] { "Obersten", "Untersten" }, SelectedIndex = 0, MinWidth = 120 };
        Count = new NumericUpDown { Value = 10, Minimum = 1, Maximum = 500, Increment = 1, FormatString = "0", MinWidth = 110 };
        Unit = new ComboBox { ItemsSource = new[] { "Elemente", "Prozent" }, SelectedIndex = 0, MinWidth = 120 };
        AutomationProperties.SetName(Direction, "Oberste oder unterste");
        AutomationProperties.SetName(Count, "Anzahl");
        AutomationProperties.SetName(Unit, "Elemente oder Prozent");
        SetBody(Field("Anzeigen", new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { Direction, Count, Unit } }));
    }

    public ComboBox Direction { get; }

    public NumericUpDown Count { get; }

    public ComboBox Unit { get; }

    public static TopTenDialog Create(CalcEditor editor, int offset) => new(editor, offset);

    protected override bool Apply()
    {
        var filter = new FilterColumn { Top = (double)(Count.Value ?? 10), TopPercent = Unit.SelectedIndex == 1, Bottom = Direction.SelectedIndex == 1 };
        _ = _editor.RunAsync("Filtern", () => _editor.Session.SetFilter(_offset, filter));
        return true;
    }
}
