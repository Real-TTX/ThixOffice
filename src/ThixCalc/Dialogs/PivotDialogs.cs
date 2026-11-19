using System.Globalization;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Core.Model.Pivot;
using ThixCalc.Editor;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;

namespace ThixCalc.Dialogs;

/// <summary>
/// PivotTable erstellen (Einfügen › PivotTable): "Tabelle/Bereich" (proposed: the active cell's table or region) and where the
/// table goes – Neues Arbeitsblatt (default) or Vorhandenes Arbeitsblatt with a cell.
/// </summary>
public sealed class InsertPivotDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly MatFormField _sourceField;
    private readonly MatFormField _targetField;

    private InsertPivotDialog(CalcEditor editor)
        : base("PivotTable erstellen", 460)
    {
        _editor = editor;
        SourceBox = new TextBox { Text = editor.Session.ProposedPivotSource(), MinWidth = 300 };
        AutomationProperties.SetName(SourceBox, "Tabelle/Bereich");
        NewSheet = new RadioButton { Content = "Neues Arbeitsblatt", GroupName = "pivotTarget", IsChecked = true };
        ExistingSheet = new RadioButton { Content = "Vorhandenes Arbeitsblatt", GroupName = "pivotTarget" };
        TargetBox = new TextBox { Text = $"{editor.Session.ActiveSheet.Name}!{editor.Session.ActiveCell}", MinWidth = 300, IsEnabled = false };
        AutomationProperties.SetName(TargetBox, "Zielbereich");
        ExistingSheet.IsCheckedChanged += (_, _) => TargetBox.IsEnabled = ExistingSheet.IsChecked == true;
        _sourceField = Field("Tabelle/Bereich", SourceBox);
        _targetField = Field("Zielbereich", TargetBox);
        var hint = new TextBlock { Text = "Wählen Sie die Daten aus, die analysiert werden sollen, und wo die PivotTable stehen soll.", TextWrapping = Avalonia.Media.TextWrapping.Wrap };
        hint.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        SetBody(new StackPanel { Spacing = 12, Children = { hint, _sourceField, NewSheet, ExistingSheet, _targetField } });
    }

    public TextBox SourceBox { get; }

    public RadioButton NewSheet { get; }

    public RadioButton ExistingSheet { get; }

    public TextBox TargetBox { get; }

    public static async Task ShowAsync(Window owner, CalcEditor editor)
    {
        if (editor.Editing.Commit() && await Create(editor).ShowAsync(owner))
        {
            editor.ShowFieldList(true);
        }
    }

    public static InsertPivotDialog Create(CalcEditor editor) => new(editor);

    protected override bool Apply()
    {
        _sourceField.Error = null;
        _targetField.Error = null;
        PivotDestination destination;
        if (ExistingSheet.IsChecked == true)
        {
            var text = (TargetBox.Text ?? string.Empty).Trim().TrimStart('=').Replace("$", string.Empty, StringComparison.Ordinal);
            var bang = text.LastIndexOf('!');
            var sheet = bang > 0 ? text[..bang].Trim('\'') : _editor.Session.ActiveSheet.Name;
            if (CellRange.TryParse(bang > 0 ? text[(bang + 1)..] : text) is not { } target || _editor.Session.Workbook.FindSheet(sheet) is null)
            {
                _targetField.Error = "Der Zielbezug ist ungültig.";
                return false;
            }

            destination = new PivotDestination(false, sheet, target.First);
        }
        else
        {
            destination = new PivotDestination(true);
        }

        try
        {
            _editor.Session.CreatePivotTable(SourceBox.Text ?? string.Empty, destination);
            return true;
        }
        catch (BusinessException exception)
        {
            (exception.Field == "destination" ? _targetField : _sourceField).Error = exception.Message;
            return false;
        }
    }
}

/// <summary>
/// Wertfeldeinstellungen: Benutzerdefinierter Name, "Werte zusammenfassen nach" (Summe, Anzahl, Mittelwert …) and "Werte anzeigen
/// als" (with Basisfeld/Basiselement where it needs them), and the number format of the values.
/// </summary>
public sealed class ValueFieldSettingsDialog : OfficeDialog
{
    private static readonly (PivotFunction Function, string Label)[] Functions =
    [
        (PivotFunction.Sum, "Summe"), (PivotFunction.Count, "Anzahl"), (PivotFunction.Average, "Mittelwert"), (PivotFunction.Max, "Max"),
        (PivotFunction.Min, "Min"), (PivotFunction.Product, "Produkt"), (PivotFunction.CountNumbers, "Anzahl Zahlen"), (PivotFunction.StdDev, "StdAbw (Stichprobe)"),
        (PivotFunction.StdDevP, "StdAbwN (Grundgesamtheit)"), (PivotFunction.Var, "Varianz (Stichprobe)"), (PivotFunction.VarP, "Varianzen (Grundgesamtheit)"),
    ];

    private static readonly (PivotShowAs ShowAs, string Label)[] ShowAsOptions =
    [
        (PivotShowAs.Normal, "Keine Berechnung"), (PivotShowAs.PercentOfGrandTotal, "% des Gesamtergebnisses"),
        (PivotShowAs.PercentOfColumnTotal, "% des Spaltengesamtergebnisses"), (PivotShowAs.PercentOfRowTotal, "% des Zeilengesamtergebnisses"),
        (PivotShowAs.Difference, "Differenz von"), (PivotShowAs.PercentDifference, "% Differenz von"), (PivotShowAs.RunningTotal, "Laufende Summe in"),
    ];

    private static readonly (string? Code, string Label)[] Formats =
    [
        (null, "Standard"), ("#,##0", "Zahl (1.234)"), ("#,##0.00", "Zahl (1.234,56)"), ("#,##0.00 \"€\"", "Währung (1.234,56 €)"), ("0%", "Prozent (12%)"), ("0.00%", "Prozent (12,34%)"),
    ];

    private readonly CalcEditor _editor;
    private readonly PivotTable _pivot;
    private readonly int _index;
    private readonly PivotCache _cache;
    private readonly List<int> _baseFields;
    private string _autoName;

    private ValueFieldSettingsDialog(CalcEditor editor, PivotTable pivot, int index)
        : base("Wertfeldeinstellungen", 460)
    {
        _editor = editor;
        _pivot = pivot;
        _index = index;
        _cache = editor.Session.Workbook.FindPivotCache(pivot.CacheId)!;
        var data = pivot.DataFields[index];
        _autoName = data.Name;
        NameBox = new TextBox { Text = data.Name };
        AutomationProperties.SetName(NameBox, "Benutzerdefinierter Name");
        FunctionList = new ListBox { ItemsSource = Functions.Select(entry => entry.Label).ToList(), SelectedIndex = Array.FindIndex(Functions, entry => entry.Function == data.Function), Height = 170 };
        AutomationProperties.SetName(FunctionList, "Wertfeld zusammenfassen nach");
        FunctionList.SelectionChanged += (_, _) => Rename();
        ShowAsBox = new ComboBox { ItemsSource = ShowAsOptions.Select(entry => entry.Label).ToList(), SelectedIndex = Array.FindIndex(ShowAsOptions, entry => entry.ShowAs == data.ShowAs), MinWidth = 260 };
        AutomationProperties.SetName(ShowAsBox, "Werte anzeigen als");
        _baseFields = [.. pivot.RowFields.Concat(pivot.ColumnFields).Where(field => field >= 0)];
        BaseFieldBox = new ComboBox { ItemsSource = _baseFields.Select(field => PivotEngine.Caption(pivot, _cache, field)).ToList(), SelectedIndex = Math.Max(0, _baseFields.IndexOf(data.BaseField)), MinWidth = 200 };
        AutomationProperties.SetName(BaseFieldBox, "Basisfeld");
        BaseItemBox = new ComboBox { MinWidth = 200 };
        AutomationProperties.SetName(BaseItemBox, "Basiselement");
        BaseFieldBox.SelectionChanged += (_, _) => FillBaseItems(null);
        ShowAsBox.SelectionChanged += (_, _) => UpdateBase();
        FormatBox = new ComboBox { ItemsSource = Formats.Select(entry => entry.Label).ToList(), SelectedIndex = Math.Max(0, Array.FindIndex(Formats, entry => entry.Code == data.NumberFormat)), MinWidth = 200 };
        AutomationProperties.SetName(FormatBox, "Zahlenformat");
        FillBaseItems(data.BaseItem);
        UpdateBase();
        var source = new TextBlock { Text = $"Quellname: {_cache.Fields[data.Field].Name}" };
        source.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var tabs = new TabControl
        {
            ItemsSource = new[]
            {
                new TabItem { Header = "Werte zusammenfassen nach", Content = Field("Wertfeld zusammenfassen nach", FunctionList) },
                new TabItem
                {
                    Header = "Werte anzeigen als",
                    Content = new StackPanel { Spacing = 10, Children = { Field("Werte anzeigen als", ShowAsBox), Field("Basisfeld", BaseFieldBox), Field("Basiselement", BaseItemBox) } },
                },
            },
        };
        SetBody(new StackPanel { Spacing = 12, Children = { source, Field("Benutzerdefinierter Name", NameBox), tabs, Field("Zahlenformat", FormatBox) } });
    }

    public TextBox NameBox { get; }

    public ListBox FunctionList { get; }

    public ComboBox ShowAsBox { get; }

    public ComboBox BaseFieldBox { get; }

    public ComboBox BaseItemBox { get; }

    public ComboBox FormatBox { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, PivotTable pivot, int index) => Create(editor, pivot, index).ShowAsync(owner);

    public static ValueFieldSettingsDialog Create(CalcEditor editor, PivotTable pivot, int index) => new(editor, pivot, index);

    /// <summary>The automatic name follows the function ("Summe von Umsatz" → "Mittelwert von Umsatz") until the user typed one.</summary>
    private void Rename()
    {
        if (FunctionList.SelectedIndex < 0 || NameBox.Text != _autoName)
        {
            return;
        }

        _autoName = PivotEngine.DataFieldName(Functions[FunctionList.SelectedIndex].Function, _cache.Fields[_pivot.DataFields[_index].Field].Name);
        NameBox.Text = _autoName;
    }

    private void UpdateBase()
    {
        var showAs = ShowAsOptions[Math.Max(0, ShowAsBox.SelectedIndex)].ShowAs;
        BaseFieldBox.IsEnabled = showAs is PivotShowAs.Difference or PivotShowAs.PercentDifference or PivotShowAs.RunningTotal && _baseFields.Count > 0;
        BaseItemBox.IsEnabled = showAs is PivotShowAs.Difference or PivotShowAs.PercentDifference && _baseFields.Count > 0;
    }

    private List<string?> _baseItemKeys = [];

    private void FillBaseItems(string? selected)
    {
        _baseItemKeys = [PivotDataField.PreviousItem, PivotDataField.NextItem];
        var labels = new List<string> { "(Vorheriger)", "(Nächster)" };
        if (BaseFieldBox.SelectedIndex >= 0 && BaseFieldBox.SelectedIndex < _baseFields.Count)
        {
            foreach (var item in _cache.Fields[_baseFields[BaseFieldBox.SelectedIndex]].Items)
            {
                _baseItemKeys.Add(PivotItems.KeyOf(item));
                labels.Add(PivotEngine.LabelText(item, _editor.Session.Locale));
            }
        }

        BaseItemBox.ItemsSource = labels;
        BaseItemBox.SelectedIndex = Math.Max(0, _baseItemKeys.IndexOf(selected));
    }

    protected override bool Apply()
    {
        var name = (NameBox.Text ?? string.Empty).Trim();
        if (name.Length == 0)
        {
            return false;
        }

        var showAs = ShowAsOptions[Math.Max(0, ShowAsBox.SelectedIndex)].ShowAs;
        var function = Functions[Math.Max(0, FunctionList.SelectedIndex)].Function;
        var baseField = BaseFieldBox.IsEnabled && BaseFieldBox.SelectedIndex >= 0 ? _baseFields[BaseFieldBox.SelectedIndex] : -1;
        var baseItem = BaseItemBox.IsEnabled && BaseItemBox.SelectedIndex >= 0 ? _baseItemKeys[BaseItemBox.SelectedIndex] : null;
        var format = Formats[Math.Max(0, FormatBox.SelectedIndex)].Code;
        _ = _editor.RunAsync("Wertfeldeinstellungen", () => _editor.Session.ChangePivotDataField(_pivot, _index, data => data with
        {
            Name = name,
            Function = function,
            ShowAs = showAs,
            BaseField = baseField,
            BaseItem = baseItem,
            NumberFormat = format,
        }));
        return true;
    }
}

/// <summary>Feldeinstellungen: Benutzerdefinierter Name, Teilergebnisse (Automatisch / Keine) and the sort order of the items.</summary>
public sealed class FieldSettingsDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly PivotTable _pivot;
    private readonly int _field;

    private FieldSettingsDialog(CalcEditor editor, PivotTable pivot, int field)
        : base("Feldeinstellungen", 420)
    {
        _editor = editor;
        _pivot = pivot;
        _field = field;
        var cache = editor.Session.Workbook.FindPivotCache(pivot.CacheId)!;
        var settings = pivot.Fields[field];
        NameBox = new TextBox { Text = settings.Caption ?? cache.Fields[field].Name };
        AutomationProperties.SetName(NameBox, "Benutzerdefinierter Name");
        Subtotals = new CheckBox { Content = "Teilergebnisse anzeigen (Automatisch)", IsChecked = settings.Subtotals };
        SortBox = new ComboBox { ItemsSource = new[] { "Aufsteigend (A bis Z)", "Absteigend (Z bis A)" }, SelectedIndex = settings.Sort.Descending ? 1 : 0, MinWidth = 220 };
        AutomationProperties.SetName(SortBox, "Sortieren");
        var source = new TextBlock { Text = $"Quellname: {cache.Fields[field].Name}" };
        source.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        SetBody(new StackPanel { Spacing = 12, Children = { source, Field("Benutzerdefinierter Name", NameBox), Subtotals, Field("Sortieren", SortBox) } });
    }

    public TextBox NameBox { get; }

    public CheckBox Subtotals { get; }

    public ComboBox SortBox { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, PivotTable pivot, int field) => Create(editor, pivot, field).ShowAsync(owner);

    public static FieldSettingsDialog Create(CalcEditor editor, PivotTable pivot, int field) => new(editor, pivot, field);

    protected override bool Apply()
    {
        var cache = _editor.Session.Workbook.FindPivotCache(_pivot.CacheId)!;
        var name = (NameBox.Text ?? string.Empty).Trim();
        var caption = name.Length == 0 || name == cache.Fields[_field].Name ? null : name;
        var subtotals = Subtotals.IsChecked == true;
        var descending = SortBox.SelectedIndex == 1;
        _ = _editor.RunAsync("Feldeinstellungen", () => _editor.Session.ChangePivotField(_pivot, _field, field => field with
        {
            Caption = caption,
            Subtotals = subtotals,
            Sort = field.Sort with { Descending = descending },
        }));
        return true;
    }
}

/// <summary>Gruppieren: dates by Jahre/Quartale/Monate/Tage, numbers by Starten/Beenden/Nach (like Excel's dialog).</summary>
public sealed class PivotGroupDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly PivotTable _pivot;
    private readonly int _field;
    private readonly bool _dates;
    private readonly MatFormField _intervalField;

    private PivotGroupDialog(CalcEditor editor, PivotTable pivot, int field)
        : base("Gruppierung", 360)
    {
        _editor = editor;
        _pivot = pivot;
        _field = field;
        var cache = editor.Session.Workbook.FindPivotCache(pivot.CacheId)!;
        var source = cache.Fields[field];
        _dates = source.IsDate;
        var numbers = source.Items.Where(item => item.IsNumber).Select(item => item.Number).DefaultIfEmpty(0).ToList();
        var current = pivot.Fields[field];
        StartBox = new TextBox { Text = (current.NumberGroup?.Start ?? numbers.Min()).ToString(CultureInfo.GetCultureInfo("de-DE")) };
        EndBox = new TextBox { Text = (current.NumberGroup?.End ?? numbers.Max()).ToString(CultureInfo.GetCultureInfo("de-DE")) };
        IntervalBox = new TextBox { Text = (current.NumberGroup?.Interval ?? 10).ToString(CultureInfo.GetCultureInfo("de-DE")) };
        AutomationProperties.SetName(StartBox, "Starten");
        AutomationProperties.SetName(EndBox, "Beenden");
        AutomationProperties.SetName(IntervalBox, "Nach");
        Levels = [];
        var body = new StackPanel { Spacing = 10 };
        _intervalField = Field("Nach", IntervalBox);
        if (_dates)
        {
            var groups = current.DateGroups == PivotDateGroups.None ? PivotDateGroups.Months : current.DateGroups;
            foreach (var (group, label) in new[] { (PivotDateGroups.Days, "Tage"), (PivotDateGroups.Months, "Monate"), (PivotDateGroups.Quarters, "Quartale"), (PivotDateGroups.Years, "Jahre") })
            {
                var box = new CheckBox { Content = label, IsChecked = groups.HasFlag(group), Tag = group };
                Levels.Add(box);
            }

            var list = new StackPanel { Spacing = 2 };
            foreach (var box in Levels)
            {
                list.Children.Add(box);
            }

            body.Children.Add(Field("Nach", list));
        }
        else
        {
            body.Children.Add(Field("Starten", StartBox));
            body.Children.Add(Field("Beenden", EndBox));
            body.Children.Add(_intervalField);
        }

        SetBody(body);
    }

    public List<CheckBox> Levels { get; }

    public TextBox StartBox { get; }

    public TextBox EndBox { get; }

    public TextBox IntervalBox { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, PivotTable pivot, int field) => Create(editor, pivot, field).ShowAsync(owner);

    public static PivotGroupDialog Create(CalcEditor editor, PivotTable pivot, int field) => new(editor, pivot, field);

    protected override bool Apply()
    {
        _intervalField.Error = null;
        var session = _editor.Session;
        if (_dates)
        {
            var groups = Levels.Where(box => box.IsChecked == true).Aggregate(PivotDateGroups.None, (all, box) => all | (PivotDateGroups)box.Tag!);
            if (groups == PivotDateGroups.None)
            {
                return false;
            }

            _ = _editor.RunAsync("Gruppieren", () => session.GroupPivotField(_pivot, _field, groups));
            return true;
        }

        var german = CultureInfo.GetCultureInfo("de-DE");
        if (!double.TryParse(StartBox.Text, NumberStyles.Float, german, out var start) || !double.TryParse(EndBox.Text, NumberStyles.Float, german, out var end)
            || !double.TryParse(IntervalBox.Text, NumberStyles.Float, german, out var interval) || interval <= 0 || end < start)
        {
            _intervalField.Error = "Geben Sie Zahlen ein: Starten ≤ Beenden, Nach größer als 0.";
            return false;
        }

        _ = _editor.RunAsync("Gruppieren", () => session.GroupPivotField(_pivot, _field, PivotDateGroups.None, new PivotNumberGroup(start, end, interval)));
        return true;
    }
}

/// <summary>Datenquelle ändern / PivotTable verschieben: one reference field.</summary>
public sealed class PivotReferenceDialog : OfficeDialog
{
    private readonly Func<string, Task<bool>> _apply;
    private readonly MatFormField _field;

    private PivotReferenceDialog(string title, string label, string text, Func<string, Task<bool>> apply)
        : base(title, 420)
    {
        _apply = apply;
        ReferenceBox = new TextBox { Text = text, MinWidth = 300 };
        AutomationProperties.SetName(ReferenceBox, label);
        _field = Field(label, ReferenceBox);
        SetBody(new StackPanel { Spacing = 12, Children = { _field } });
    }

    public TextBox ReferenceBox { get; }

    public static Task<bool> SourceAsync(Window owner, CalcEditor editor, PivotTable pivot) =>
        new PivotReferenceDialog("PivotTable-Datenquelle ändern", "Tabelle/Bereich", editor.Session.PivotSourceText(pivot),
            text => editor.RunAsync("Datenquelle ändern", () => editor.Session.ChangePivotSource(pivot, text))).ShowAsync(owner);

    public static Task<bool> MoveAsync(Window owner, CalcEditor editor, PivotTable pivot) =>
        new PivotReferenceDialog("PivotTable verschieben", "Position", $"{editor.Session.ActiveSheet.Name}!{pivot.Location}", text =>
        {
            var clean = text.Trim().TrimStart('=').Replace("$", string.Empty, StringComparison.Ordinal);
            var bang = clean.LastIndexOf('!');
            var sheet = bang > 0 ? clean[..bang].Trim('\'') : editor.Session.ActiveSheet.Name;
            if (CellRange.TryParse(bang > 0 ? clean[(bang + 1)..] : clean) is not { } target)
            {
                throw new BusinessException("Der Zielbezug ist ungültig.");
            }

            return editor.RunAsync("PivotTable verschieben", () => editor.Session.MovePivot(pivot, sheet, target.First));
        }).ShowAsync(owner);

    protected override bool Apply()
    {
        _field.Error = null;
        try
        {
            _ = _apply(ReferenceBox.Text ?? string.Empty);
            return true;
        }
        catch (BusinessException exception)
        {
            _field.Error = exception.Message;
            return false;
        }
    }
}
