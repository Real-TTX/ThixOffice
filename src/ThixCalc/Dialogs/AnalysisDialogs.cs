using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>Teilergebnisse: Gruppieren nach, Unter Verwendung von, Teilergebnis addieren zu, Vorhandene ersetzen, Alle entfernen.</summary>
public sealed class SubtotalDialog : OfficeDialog
{
    private static readonly (SubtotalFunction Function, string Label)[] Functions =
    [
        (SubtotalFunction.Sum, "Summe"), (SubtotalFunction.CountA, "Anzahl"), (SubtotalFunction.Average, "Mittelwert"), (SubtotalFunction.Max, "Max"),
        (SubtotalFunction.Min, "Min"), (SubtotalFunction.Product, "Produkt"), (SubtotalFunction.Count, "Anzahl Zahlen"), (SubtotalFunction.StdDev, "StdAbw (Stichprobe)"),
        (SubtotalFunction.StdDevP, "StdAbwN (Grundgesamtheit)"), (SubtotalFunction.Var, "Varianz (Stichprobe)"), (SubtotalFunction.VarP, "Varianz (Grundgesamtheit)"),
    ];

    private readonly CalcEditor _editor;
    private readonly CellRange _list;
    private readonly MatFormField _columnsField;

    private SubtotalDialog(CalcEditor editor)
        : base("Teilergebnisse", 400)
    {
        _editor = editor;
        var session = editor.Session;
        _list = session.Selection.IsSingleCell ? Navigation.CurrentRegion(session.ActiveSheet, session.ActiveCell) : session.Selection;
        var headers = Enumerable.Range(_list.FirstColumn, _list.ColumnCount).Select(column => session.ActiveSheet.GetValue(_list.FirstRow, column) is var value && value.IsText ? value.Text : $"Spalte {A1Name(column)}").ToList();
        GroupBox = new ComboBox { ItemsSource = headers, SelectedIndex = 0, MinWidth = 240 };
        AutomationProperties.SetName(GroupBox, "Gruppieren nach");
        FunctionBox = new ComboBox { ItemsSource = Functions.Select(entry => entry.Label).ToList(), SelectedIndex = 0, MinWidth = 240 };
        AutomationProperties.SetName(FunctionBox, "Unter Verwendung von");
        Columns = [.. headers.Select((header, index) => new CheckBox { Content = header, IsChecked = index == headers.Count - 1 })];
        var list = new StackPanel { Spacing = 2 };
        foreach (var box in Columns)
        {
            list.Children.Add(box);
        }

        Replace = new CheckBox { Content = "Vorhandene Teilergebnisse ersetzen", IsChecked = true };
        var removeAll = new Button { Content = "Alle entfernen" };
        removeAll.Click += (_, _) =>
        {
            _ = _editor.RunAsync("Teilergebnisse entfernen", () => _editor.Session.RemoveSubtotals(_list));
            Close();
        };
        _columnsField = Field("Teilergebnis addieren zu", new ScrollViewer { Content = list, MaxHeight = 160 });
        SetBody(new StackPanel { Spacing = 12, Children = { Field("Gruppieren nach", GroupBox), Field("Unter Verwendung von", FunctionBox), _columnsField, Replace, removeAll } });
    }

    public ComboBox GroupBox { get; }

    public ComboBox FunctionBox { get; }

    public List<CheckBox> Columns { get; }

    public CheckBox Replace { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => editor.Editing.Commit() ? Create(editor).ShowAsync(owner) : Task.FromResult(false);

    public static SubtotalDialog Create(CalcEditor editor) => new(editor);

    private static string A1Name(int column) => A1.ColumnName(column);

    protected override bool Apply()
    {
        _columnsField.Error = null;
        var columns = Columns.Select((box, index) => (box, index)).Where(entry => entry.box.IsChecked == true).Select(entry => entry.index).ToList();
        try
        {
            _editor.Session.AddSubtotals(_list, Math.Max(0, GroupBox.SelectedIndex), Functions[Math.Max(0, FunctionBox.SelectedIndex)].Function, columns, Replace.IsChecked == true);
            return true;
        }
        catch (BusinessException exception)
        {
            _columnsField.Error = exception.Message;
            return false;
        }
    }
}

/// <summary>Zielwertsuche: Zielzelle, Zielwert, Veränderbare Zelle; the result is reported like Excel's status window.</summary>
public sealed class GoalSeekDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly MatFormField _targetField;
    private readonly MatFormField _valueField;
    private readonly MatFormField _changingField;

    private GoalSeekDialog(CalcEditor editor)
        : base("Zielwertsuche", 380)
    {
        _editor = editor;
        TargetBox = new TextBox { Text = editor.Session.ActiveCell.ToString() };
        ValueBox = new TextBox();
        ChangingBox = new TextBox();
        AutomationProperties.SetName(TargetBox, "Zielzelle");
        AutomationProperties.SetName(ValueBox, "Zielwert");
        AutomationProperties.SetName(ChangingBox, "Veränderbare Zelle");
        _targetField = Field("Zielzelle", TargetBox);
        _valueField = Field("Zielwert", ValueBox);
        _changingField = Field("Veränderbare Zelle", ChangingBox);
        SetBody(new StackPanel { Spacing = 12, Children = { _targetField, _valueField, _changingField } });
    }

    public TextBox TargetBox { get; }

    public TextBox ValueBox { get; }

    public TextBox ChangingBox { get; }

    /// <summary>Excel's report after the search (null before).</summary>
    public string? Report { get; private set; }

    public static async Task ShowAsync(Window owner, CalcEditor editor)
    {
        var dialog = Create(editor);
        if (editor.Editing.Commit() && await dialog.ShowAsync(owner) && dialog.Report is { } report)
        {
            await QuestionDialog.ShowMessageAsync(owner, "Status der Zielwertsuche", report);
        }
    }

    public static GoalSeekDialog Create(CalcEditor editor) => new(editor);

    protected override bool Apply()
    {
        _targetField.Error = _valueField.Error = _changingField.Error = null;
        var target = CellRange.TryParse((TargetBox.Text ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal).Trim());
        var changing = CellRange.TryParse((ChangingBox.Text ?? string.Empty).Replace("$", string.Empty, StringComparison.Ordinal).Trim());
        if (target is not { IsSingleCell: true } targetCell)
        {
            _targetField.Error = "Geben Sie eine Zelle an, z. B. B5.";
            return false;
        }

        if (!double.TryParse(ValueBox.Text, NumberStyles.Float, CultureInfo.GetCultureInfo("de-DE"), out var value))
        {
            _valueField.Error = "Geben Sie eine Zahl ein.";
            return false;
        }

        if (changing is not { IsSingleCell: true } changingCell)
        {
            _changingField.Error = "Geben Sie eine Zelle an, z. B. B1.";
            return false;
        }

        try
        {
            var (found, input, result) = _editor.Session.GoalSeek(targetCell.First, value, changingCell.First);
            var locale = _editor.Session.Locale;
            Report = found
                ? $"Die Zielwertsuche hat für die Zelle {targetCell.First} eine Lösung gefunden.{Environment.NewLine}Zielwert: {Core.Globalization.NumberText.ToText(value, locale)}{Environment.NewLine}Aktueller Wert: {Core.Globalization.NumberText.ToText(result, locale)}"
                : $"Die Zielwertsuche hat für die Zelle {targetCell.First} keine Lösung gefunden.";
            _ = input;
            return true;
        }
        catch (BusinessException exception)
        {
            (exception.Field == "changing" ? _changingField : _targetField).Error = exception.Message;
            return false;
        }
    }
}

/// <summary>Namen aus Auswahl erstellen (Strg+Umschalt+F3): Oberster Zeile, Linker Spalte, Unterster Zeile, Rechter Spalte.</summary>
public sealed class CreateNamesDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };

    private CreateNamesDialog(CalcEditor editor)
        : base("Namen aus Auswahl erstellen", 360)
    {
        _editor = editor;
        var sheet = editor.Session.ActiveSheet;
        var range = editor.Session.Selection;
        Top = new CheckBox { Content = "Oberster Zeile", IsChecked = sheet.GetValue(range.FirstRow, range.LastColumn).IsText };
        Left = new CheckBox { Content = "Linker Spalte", IsChecked = sheet.GetValue(range.LastRow, range.FirstColumn).IsText && range.ColumnCount > 1 };
        Bottom = new CheckBox { Content = "Unterster Zeile" };
        Right = new CheckBox { Content = "Rechter Spalte" };
        _error.Use(TextBlock.ForegroundProperty, "Mat.Danger");
        SetBody(new StackPanel { Spacing = 6, Children = { new TextBlock { Text = "Namen erstellen aus den Werten in:" }, Top, Left, Bottom, Right, _error } });
    }

    public CheckBox Top { get; }

    public CheckBox Left { get; }

    public CheckBox Bottom { get; }

    public CheckBox Right { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => editor.Editing.Commit() ? Create(editor).ShowAsync(owner) : Task.FromResult(false);

    public static CreateNamesDialog Create(CalcEditor editor) => new(editor);

    protected override bool Apply()
    {
        try
        {
            _editor.Session.CreateNamesFromSelection(Top.IsChecked == true, Left.IsChecked == true, Bottom.IsChecked == true, Right.IsChecked == true);
            return true;
        }
        catch (BusinessException exception)
        {
            _error.Text = exception.Message;
            return false;
        }
    }
}

/// <summary>Inhalte auswählen: Leerzellen, Konstanten, Formeln, Fehler, Aktueller Bereich, Letzte Zelle, Nur sichtbare Zellen, Notizen.</summary>
public sealed class SelectSpecialDialog : OfficeDialog
{
    private static readonly (SpecialCells Kind, string Label)[] Kinds =
    [
        (SpecialCells.Comments, "Notizen"), (SpecialCells.Constants, "Konstanten"), (SpecialCells.Formulas, "Formeln"), (SpecialCells.Errors, "Fehler"),
        (SpecialCells.Blanks, "Leerzellen"), (SpecialCells.CurrentRegion, "Aktueller Bereich"), (SpecialCells.LastCell, "Letzte Zelle"),
        (SpecialCells.VisibleCells, "Nur sichtbare Zellen"),
    ];

    private readonly CalcEditor _editor;
    private readonly TextBlock _error = new() { TextWrapping = TextWrapping.Wrap };

    private SelectSpecialDialog(CalcEditor editor)
        : base("Inhalte auswählen", 340)
    {
        _editor = editor;
        Options = [.. Kinds.Select((kind, index) => new RadioButton { Content = kind.Label, GroupName = "selectSpecial", IsChecked = index == 4 })];
        _error.Use(TextBlock.ForegroundProperty, "Mat.Danger");
        var list = new StackPanel { Spacing = 4 };
        foreach (var option in Options)
        {
            list.Children.Add(option);
        }

        list.Children.Add(_error);
        SetBody(list);
    }

    public List<RadioButton> Options { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => editor.Editing.Commit() ? Create(editor).ShowAsync(owner) : Task.FromResult(false);

    public static SelectSpecialDialog Create(CalcEditor editor) => new(editor);

    protected override bool Apply()
    {
        var index = Options.FindIndex(option => option.IsChecked == true);
        try
        {
            _editor.Session.SelectSpecial(Kinds[Math.Max(0, index)].Kind);
            return true;
        }
        catch (BusinessException exception)
        {
            _error.Text = exception.Message;
            return false;
        }
    }
}

/// <summary>Blatt schützen: an optional password (repeated) and what users of the sheet may still do.</summary>
public sealed class ProtectSheetDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly MatFormField _passwordField;

    private ProtectSheetDialog(CalcEditor editor)
        : base("Blatt schützen", 400)
    {
        _editor = editor;
        PasswordBox = new TextBox { PasswordChar = '•' };
        RepeatBox = new TextBox { PasswordChar = '•' };
        AutomationProperties.SetName(PasswordBox, "Kennwort zum Aufheben des Blattschutzes");
        AutomationProperties.SetName(RepeatBox, "Kennwort bestätigen");
        SelectLocked = new CheckBox { Content = "Gesperrte Zellen auswählen", IsChecked = true };
        SelectUnlocked = new CheckBox { Content = "Nicht gesperrte Zellen auswählen", IsChecked = true };
        FormatCells = new CheckBox { Content = "Zellen formatieren" };
        FormatColumns = new CheckBox { Content = "Spalten formatieren" };
        FormatRows = new CheckBox { Content = "Zeilen formatieren" };
        InsertColumns = new CheckBox { Content = "Spalten einfügen" };
        InsertRows = new CheckBox { Content = "Zeilen einfügen" };
        DeleteColumns = new CheckBox { Content = "Spalten löschen" };
        DeleteRows = new CheckBox { Content = "Zeilen löschen" };
        Sort = new CheckBox { Content = "Sortieren" };
        AutoFilter = new CheckBox { Content = "AutoFilter verwenden" };
        _passwordField = Field("Kennwort zum Aufheben des Blattschutzes", PasswordBox);
        var options = new StackPanel { Spacing = 2, Children = { SelectLocked, SelectUnlocked, FormatCells, FormatColumns, FormatRows, InsertColumns, InsertRows, DeleteColumns, DeleteRows, Sort, AutoFilter } };
        SetBody(new StackPanel { Spacing = 10, Children = { _passwordField, Field("Kennwort bestätigen", RepeatBox), new TextBlock { Text = "Alle Benutzer dieses Arbeitsblatts dürfen:" }, options } });
    }

    public TextBox PasswordBox { get; }

    public TextBox RepeatBox { get; }

    public CheckBox SelectLocked { get; }

    public CheckBox SelectUnlocked { get; }

    public CheckBox FormatCells { get; }

    public CheckBox FormatColumns { get; }

    public CheckBox FormatRows { get; }

    public CheckBox InsertColumns { get; }

    public CheckBox InsertRows { get; }

    public CheckBox DeleteColumns { get; }

    public CheckBox DeleteRows { get; }

    public CheckBox Sort { get; }

    public CheckBox AutoFilter { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => editor.Editing.Commit() ? Create(editor).ShowAsync(owner) : Task.FromResult(false);

    public static ProtectSheetDialog Create(CalcEditor editor) => new(editor);

    protected override bool Apply()
    {
        _passwordField.Error = null;
        if ((PasswordBox.Text ?? string.Empty) != (RepeatBox.Text ?? string.Empty))
        {
            _passwordField.Error = "Die Kennwörter stimmen nicht überein.";
            return false;
        }

        var allowed = new SheetProtection
        {
            SelectLockedCells = SelectLocked.IsChecked == true,
            SelectUnlockedCells = SelectUnlocked.IsChecked == true,
            FormatCells = FormatCells.IsChecked == true,
            FormatColumns = FormatColumns.IsChecked == true,
            FormatRows = FormatRows.IsChecked == true,
            InsertColumns = InsertColumns.IsChecked == true,
            InsertRows = InsertRows.IsChecked == true,
            DeleteColumns = DeleteColumns.IsChecked == true,
            DeleteRows = DeleteRows.IsChecked == true,
            Sort = Sort.IsChecked == true,
            AutoFilter = AutoFilter.IsChecked == true,
        };
        var password = string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text;
        _ = _editor.RunAsync("Blatt schützen", () => _editor.Session.ProtectSheet(allowed, password));
        return true;
    }
}

/// <summary>One password: Blattschutz aufheben, Arbeitsmappe schützen / Arbeitsmappenschutz aufheben.</summary>
public sealed class PasswordDialog : OfficeDialog
{
    private readonly Action<string?> _apply;
    private readonly MatFormField _field;

    private PasswordDialog(string title, string label, Action<string?> apply)
        : base(title, 360)
    {
        _apply = apply;
        PasswordBox = new TextBox { PasswordChar = '•' };
        AutomationProperties.SetName(PasswordBox, label);
        _field = Field(label, PasswordBox);
        SetBody(new StackPanel { Spacing = 12, Children = { _field } });
    }

    public TextBox PasswordBox { get; }

    public static Task<bool> UnprotectSheetAsync(Window owner, CalcEditor editor) =>
        new PasswordDialog("Blattschutz aufheben", "Kennwort", password => editor.Session.UnprotectSheet(password)).ShowAsync(owner);

    public static Task<bool> ProtectWorkbookAsync(Window owner, CalcEditor editor) =>
        new PasswordDialog("Arbeitsmappe schützen", "Kennwort (optional)", editor.Session.ProtectWorkbook).ShowAsync(owner);

    public static Task<bool> UnprotectWorkbookAsync(Window owner, CalcEditor editor) =>
        new PasswordDialog("Arbeitsmappenschutz aufheben", "Kennwort", editor.Session.UnprotectWorkbook).ShowAsync(owner);

    protected override bool Apply()
    {
        _field.Error = null;
        try
        {
            _apply(string.IsNullOrEmpty(PasswordBox.Text) ? null : PasswordBox.Text);
            return true;
        }
        catch (BusinessException exception)
        {
            _field.Error = exception.Message;
            return false;
        }
    }
}

/// <summary>Formel auswerten: the formula's steps from the references to the result.</summary>
public sealed class EvaluateFormulaDialog : OfficeDialog
{
    private EvaluateFormulaDialog(IReadOnlyList<string> steps, string cell)
        : base("Formel auswerten", 520)
    {
        Steps = steps;
        var list = new StackPanel { Spacing = 6 };
        list.Children.Add(new TextBlock { Text = $"Bezug: {cell}", FontWeight = FontWeight.SemiBold });
        for (var index = 0; index < steps.Count; index++)
        {
            var step = new TextBlock { Text = $"{index + 1}. {steps[index]}", TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Consolas, Menlo, monospace") };
            list.Children.Add(step);
        }

        SetBody(new ScrollViewer { Content = list, MaxHeight = 360 });
        CancelButton.IsVisible = false;
    }

    public IReadOnlyList<string> Steps { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor)
    {
        var steps = editor.Session.EvaluateFormulaSteps(editor.Session.ActiveCell);
        return new EvaluateFormulaDialog(steps, editor.Session.ActiveCell.ToString()).ShowAsync(owner);
    }

    protected override bool Apply() => true;
}

/// <summary>Namen einfügen (F3, Formeln › In Formel verwenden): the defined names; OK puts the chosen one into the cell.</summary>
public sealed class PasteNameDialog : OfficeDialog
{
    private readonly CalcEditor _editor;

    private PasteNameDialog(CalcEditor editor)
        : base("Namen einfügen", 340)
    {
        _editor = editor;
        Names = new ListBox { ItemsSource = editor.Session.Workbook.Names.Where(name => !name.Hidden).Select(name => name.Name).OrderBy(name => name, StringComparer.CurrentCultureIgnoreCase).ToList(), Height = 200 };
        Names.SelectedIndex = 0;
        AutomationProperties.SetName(Names, "Namen einfügen");
        Names.DoubleTapped += (_, _) => Confirm();
        SetBody(Field("Namen einfügen", Names));
    }

    public ListBox Names { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new PasteNameDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        if (Names.SelectedItem is not string name)
        {
            return false;
        }

        _editor.InsertIntoFormula(name);
        return true;
    }
}
