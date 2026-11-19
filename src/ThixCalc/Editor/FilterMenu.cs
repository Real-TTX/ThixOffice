using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using MatDesktop;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixOffice.Desktop.Controls;

namespace ThixCalc.Editor;

/// <summary>
/// The list behind an autofilter arrow, like Excel's: sort A–Z / Z–A by this column, "Filter löschen", a search over the values,
/// "(Alles auswählen)" and a check box per value (blanks as "(Leer)"), OK / Abbrechen. OK sets the column's value filter (or
/// removes it when every value is checked).
/// </summary>
internal sealed class FilterMenu : Border
{
    private readonly CalcEditor _editor;
    private readonly int _offset;
    private readonly StackPanel _values = new() { Spacing = 2 };
    private readonly CheckBox _all = new() { Content = "(Alles auswählen)" };
    private readonly TextBox _search = new() { Watermark = "Suchen" };
    private readonly List<(string Value, CheckBox Box)> _entries = [];
    private bool _updating;

    public FilterMenu(CalcEditor editor, int columnOffset, Action close)
    {
        _editor = editor;
        _offset = columnOffset;
        Close = close;
        Padding = new Thickness(8);
        MinWidth = 240;
        var session = editor.Session;
        var filter = session.ActiveFilter ?? new AutoFilter(session.Selection, []);
        var current = filter.Columns.FirstOrDefault(column => column.ColumnOffset == columnOffset);
        foreach (var value in session.FilterValues(columnOffset))
        {
            var box = new CheckBox { Content = value.Length == 0 ? "(Leer)" : value, IsChecked = current?.Values is not { } shown || shown.Contains(value) || value.Length == 0 && current.IncludeBlank };
            box.IsCheckedChanged += (_, _) => SyncAll();
            _entries.Add((value, box));
            _values.Children.Add(box);
        }

        _all.IsCheckedChanged += (_, _) =>
        {
            if (_updating || _all.IsChecked is null)
            {
                return;
            }

            _updating = true;
            foreach (var (_, box) in _entries.Where(entry => entry.Box.IsVisible))
            {
                box.IsChecked = _all.IsChecked;
            }

            _updating = false;
        };
        _search.TextChanged += (_, _) =>
        {
            foreach (var (value, box) in _entries)
            {
                box.IsVisible = string.IsNullOrEmpty(_search.Text) || value.Contains(_search.Text, StringComparison.CurrentCultureIgnoreCase);
            }
        };
        AutomationProperties.SetName(_search, "Werte suchen");
        SyncAll();

        var ascending = MenuButton("sort-asc", "Von A bis Z sortieren", () => Sort(descending: false));
        var descending = MenuButton("sort-desc", "Von Z bis A sortieren", () => Sort(descending: true));
        var clear = MenuButton("x", "Filter löschen", () => Apply(null));
        clear.IsEnabled = current is not null;
        var (kind, kindLabel) = KindOf(session, filter, columnOffset);
        var conditions = MenuButton("filter", kindLabel, () => { });
        conditions.Flyout = ConditionsMenu(kind);
        ConditionsButton = conditions;
        var ok = new Button { Content = new IconLabel { Icon = "check", Text = "OK" }, IsDefault = true }.Themed("MatButton.Primary");
        ok.Click += (_, _) => Apply(Chosen());
        var cancel = new Button { Content = new IconLabel { Icon = "x", Text = "Abbrechen" } };
        cancel.Click += (_, _) => Close();
        Child = new StackPanel
        {
            Spacing = 6,
            Children =
            {
                ascending, descending, clear, conditions,
                _search,
                new Border { Child = new ScrollViewer { Content = new StackPanel { Children = { _all, _values } }, MaxHeight = 240 }, Padding = new Thickness(4) },
                new ActionBar { Children = { ok, cancel } },
            },
        };
    }

    /// <summary>What a filter column holds: dates, numbers or texts (Datumsfilter, Zahlenfilter, Textfilter).</summary>
    private enum ColumnKind : byte
    {
        Text,
        Number,
        Date,
    }

    /// <summary>The button of Zahlenfilter / Textfilter / Datumsfilter (tests).</summary>
    public Button ConditionsButton { get; }

    private static (ColumnKind Kind, string Label) KindOf(EditingSession session, AutoFilter filter, int offset)
    {
        var sheet = session.ActiveSheet;
        var column = filter.Range.FirstColumn + offset;
        int numbers = 0, dates = 0, texts = 0;
        for (var row = filter.Range.FirstRow + 1; row <= Math.Min(filter.Range.LastRow, filter.Range.FirstRow + 2000); row++)
        {
            var value = sheet.GetValue(row, column);
            if (value.IsNumber)
            {
                if (Core.Formatting.NumberFormat.Parse(sheet.GetStyle(row, column).NumberFormat).IsDateTime)
                {
                    dates++;
                }
                else
                {
                    numbers++;
                }
            }
            else if (!value.IsBlank)
            {
                texts++;
            }
        }

        return dates > numbers && dates >= texts ? (ColumnKind.Date, "Datumsfilter")
            : numbers >= texts && numbers > 0 ? (ColumnKind.Number, "Zahlenfilter")
            : (ColumnKind.Text, "Textfilter");
    }

    /// <summary>The conditions of the column's kind; each opens the custom filter preset (Top 10 its own dialog; averages at once).</summary>
    private MenuFlyout ConditionsMenu(ColumnKind kind)
    {
        var menu = new MenuFlyout();
        void Entry(string label, Action action)
        {
            var item = new MenuItem { Header = label };
            item.Click += (_, _) => action();
            menu.Items.Add(item);
        }

        void Custom(string label, int first, int? second = null) => Entry(label, () => ShowCustom(first, second));
        switch (kind)
        {
            case ColumnKind.Number:
                Custom("Ist gleich …", 0);
                Custom("Ist nicht gleich …", 1);
                Custom("Größer als …", 2);
                Custom("Größer oder gleich …", 3);
                Custom("Kleiner als …", 4);
                Custom("Kleiner oder gleich …", 5);
                Custom("Zwischen …", 3, 5);
                Entry("Top 10 …", () =>
                {
                    Close();
                    _ = Dialogs.TopTenDialog.Create(_editor, _offset).ShowAsync(_editor.Window);
                });
                Entry("Über dem Durchschnitt", () => Apply(new FilterColumn { Dynamic = "aboveAverage" }));
                Entry("Unter dem Durchschnitt", () => Apply(new FilterColumn { Dynamic = "belowAverage" }));
                break;
            case ColumnKind.Date:
                Custom("Ist gleich …", 0);
                Custom("Vor …", 4);
                Custom("Nach …", 2);
                Custom("Zwischen …", 3, 5);
                break;
            default:
                Custom("Ist gleich …", 0);
                Custom("Ist nicht gleich …", 1);
                Custom("Beginnt mit …", 6);
                Custom("Endet mit …", 8);
                Custom("Enthält …", 10);
                Custom("Enthält nicht …", 11);
                break;
        }

        menu.Items.Add(new Separator());
        Custom("Benutzerdefinierter Filter …", 0);
        return menu;
    }

    private void ShowCustom(int first, int? second)
    {
        Close();
        var filter = _editor.Session.ActiveFilter;
        var header = filter is null ? string.Empty : _editor.Session.ActiveSheet.GetValue(filter.Range.FirstRow, filter.Range.FirstColumn + _offset).ToString();
        _ = Dialogs.CustomFilterDialog.Create(_editor, _offset, header, first, second).ShowAsync(_editor.Window);
    }

    /// <summary>Closes the flyout around the menu.</summary>
    public Action Close { get; }

    /// <summary>The value check boxes (tests).</summary>
    public IReadOnlyList<CheckBox> Values => [.. _entries.Select(entry => entry.Box)];

    private Button MenuButton(string icon, string label, Action action)
    {
        var button = new Button { Content = new IconLabel { Icon = icon, Text = label }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch, HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Left }.Themed("MatButton.Ghost");
        button.Click += (_, _) => action();
        return button;
    }

    private void SyncAll()
    {
        if (_updating)
        {
            return;
        }

        _updating = true;
        var checkedCount = _entries.Count(entry => entry.Box.IsChecked == true);
        _all.IsChecked = checkedCount == _entries.Count ? true : checkedCount == 0 ? false : null;
        _updating = false;
    }

    /// <summary>The filter the boxes say: null when every value shows.</summary>
    private FilterColumn? Chosen()
    {
        if (_entries.All(entry => entry.Box.IsChecked == true))
        {
            return null;
        }

        return new FilterColumn
        {
            Values = [.. _entries.Where(entry => entry.Box.IsChecked == true && entry.Value.Length > 0).Select(entry => entry.Value)],
            IncludeBlank = _entries.Any(entry => entry.Value.Length == 0 && entry.Box.IsChecked == true),
        };
    }

    public void Apply(FilterColumn? filter)
    {
        Close();
        _ = _editor.RunAsync("Filtern", () => _editor.Session.SetFilter(_offset, filter));
    }

    private void Sort(bool descending)
    {
        Close();
        var session = _editor.Session;
        var range = session.ActiveFilter?.Range ?? session.Selection;
        _ = _editor.RunAsync("Sortieren", () => session.Sort(range, [new SortKey(_offset, descending)], new SortOptions(HasHeader: true)));
    }
}
