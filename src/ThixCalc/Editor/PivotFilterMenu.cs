using System.Collections.Immutable;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Model.Pivot;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;

namespace ThixCalc.Editor;

/// <summary>
/// The list behind a PivotTable field's arrow, like Excel's: sorting (row and column fields), a search, the items with check boxes
/// ("(Alle auswählen)") – for a filter field one item or (Alle) – "Filter löschen", OK and Abbrechen.
/// </summary>
internal sealed class PivotFilterMenu : Border
{
    private readonly CalcEditor _editor;
    private readonly PivotTable _pivot;
    private readonly int _field;
    private readonly Action _close;
    private readonly bool _single;
    private readonly List<(string Key, string Label)> _items;
    private readonly HashSet<string> _hidden;
    private readonly StackPanel _list = new() { Spacing = 2 };
    private readonly TextBox _search = new() { Watermark = "Suchen" };
    private string? _selected;

    public PivotFilterMenu(CalcEditor editor, PivotTable pivot, int field, Action close)
    {
        _editor = editor;
        _pivot = pivot;
        _field = field;
        _close = close;
        var cache = editor.Session.Workbook.FindPivotCache(pivot.CacheId)!;
        var settings = pivot.Fields[field];
        _single = settings.Axis == PivotAxis.Filter;
        _hidden = [.. settings.HiddenItems];
        _selected = settings.SelectedItem;
        var level = PivotEngine.LevelsOf(pivot, cache, [field]).Last();
        _items = [.. level.Items
            .Select(item => (item.Key, Label: PivotEngine.LabelText(item.Label, editor.Session.Locale), item.Order, item.Numeric))
            .OrderBy(item => item.Numeric || level.Group != PivotDateGroups.None ? 0 : 1).ThenBy(item => item.Order).ThenBy(item => item.Label, StringComparer.CurrentCultureIgnoreCase)
            .Select(item => (item.Key, item.Label))];
        if (_single)
        {
            // A filter field filters by the plain item.
            _items = [.. cache.Fields[field].Items.Select(item => (PivotItems.KeyOf(item), PivotEngine.LabelText(item, editor.Session.Locale))).OrderBy(item => item.Item2, StringComparer.CurrentCultureIgnoreCase)];
        }

        this.Use(BackgroundProperty, "Mat.Surface");
        Padding = new Thickness(10);
        Width = 260;
        AutomationProperties.SetName(this, PivotEngine.Caption(pivot, cache, field));
        _search.TextChanged += (_, _) => Fill();
        AutomationProperties.SetName(_search, "Suchen");

        var content = new StackPanel { Spacing = 8 };
        if (!_single)
        {
            content.Children.Add(MenuButton("Von A bis Z sortieren", "sort-asc", () => Apply(current => current with { Sort = new PivotSort(false) })));
            content.Children.Add(MenuButton("Von Z bis A sortieren", "sort-desc", () => Apply(current => current with { Sort = new PivotSort(true) })));
        }

        content.Children.Add(MenuButton($"Filter löschen aus „{PivotEngine.Caption(pivot, cache, field)}“", "x", () =>
            Apply(current => current with { HiddenItems = [], SelectedItem = null, LabelFilter = null, ValueFilter = null })));
        content.Children.Add(_search);
        content.Children.Add(new ScrollViewer { Content = _list, MaxHeight = 240 });
        var ok = new Button { Content = new IconLabel { Icon = "check", Text = "OK" } }.Themed("MatButton.Primary");
        var cancel = new Button { Content = new IconLabel { Icon = "x", Text = "Abbrechen" } };
        ok.Click += (_, _) => Confirm();
        cancel.Click += (_, _) => _close();
        content.Children.Add(new ActionBar { Children = { ok, cancel } });
        Child = content;
        Fill();
    }

    /// <summary>The item check boxes (or radio buttons of a filter field) as shown (tests).</summary>
    public IReadOnlyList<ToggleButton> Items => [.. _list.Children.OfType<ToggleButton>()];

    private Button MenuButton(string text, string icon, Action action)
    {
        var button = new Button { Content = new IconLabel { Icon = icon, Text = text }, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch }.Themed("MatButton.Ghost");
        button.Click += (_, _) => action();
        return button;
    }

    private void Fill()
    {
        _list.Children.Clear();
        var filter = _search.Text?.Trim() ?? string.Empty;
        if (_single)
        {
            var all = new RadioButton { Content = PivotEngine.AllItems, GroupName = "pivotFilterItem", IsChecked = _selected is null };
            all.IsCheckedChanged += (_, _) =>
            {
                if (all.IsChecked == true)
                {
                    _selected = null;
                }
            };
            _list.Children.Add(all);
        }
        else if (filter.Length == 0)
        {
            var all = new CheckBox { Content = "(Alle auswählen)", IsChecked = _hidden.Count == 0 ? true : _hidden.Count == _items.Count ? false : null, IsThreeState = false };
            all.Click += (_, _) =>
            {
                if (_hidden.Count == 0)
                {
                    _hidden.UnionWith(_items.Select(item => item.Key));
                }
                else
                {
                    _hidden.Clear();
                }

                Fill();
            };
            _list.Children.Add(all);
        }

        foreach (var (key, label) in _items.Where(item => filter.Length == 0 || item.Label.Contains(filter, StringComparison.CurrentCultureIgnoreCase)))
        {
            var itemKey = key;
            if (_single)
            {
                var radio = new RadioButton { Content = label, GroupName = "pivotFilterItem", IsChecked = _selected == key };
                radio.IsCheckedChanged += (_, _) =>
                {
                    if (radio.IsChecked == true)
                    {
                        _selected = itemKey;
                    }
                };
                _list.Children.Add(radio);
                continue;
            }

            var box = new CheckBox { Content = label, IsChecked = !_hidden.Contains(key) };
            box.IsCheckedChanged += (_, _) =>
            {
                if (box.IsChecked == true)
                {
                    _hidden.Remove(itemKey);
                }
                else
                {
                    _hidden.Add(itemKey);
                }
            };
            _list.Children.Add(box);
        }
    }

    /// <summary>OK: the hidden items (at least one item stays, as in Excel) or the filter field's item.</summary>
    public void Confirm()
    {
        if (!_single && _hidden.Count >= _items.Count)
        {
            return;
        }

        var hidden = _hidden.ToImmutableHashSet();
        var selected = _selected;
        Apply(current => _single ? current with { SelectedItem = selected } : current with { HiddenItems = hidden });
    }

    private void Apply(Func<PivotField, PivotField> change)
    {
        _close();
        _ = _editor.RunAsync("Filtern", () => _editor.Session.ChangePivotField(_pivot, _field, change, "Filtern"));
    }

    /// <summary>Ticks or unticks an item by its label (tests).</summary>
    public void Set(string label, bool shown)
    {
        foreach (var toggle in Items)
        {
            if (toggle.Content as string == label)
            {
                toggle.IsChecked = shown;
            }
        }
    }
}
