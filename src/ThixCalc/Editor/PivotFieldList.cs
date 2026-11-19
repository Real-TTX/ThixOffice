using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Model.Pivot;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;

namespace ThixCalc.Editor;

/// <summary>
/// "PivotTable-Felder" at the right of the grid, like Excel's: a search over the fields, their check boxes (ticking puts a numeric
/// field into Werte and others into Zeilen; the context menu chooses the area), and the four areas Filter / Spalten / Zeilen / Werte
/// with the fields in them. Fields are dragged from the list into an area, between areas and within one; every chip has a menu
/// with the same moves for the keyboard (Nach oben, In Zeilen verschieben, Feld entfernen, Wertfeldeinstellungen …).
/// </summary>
internal sealed class PivotFieldList : Border
{
    private const double DragThreshold = 4;

    private readonly CalcEditor _editor;
    private readonly TextBox _search = new() { Watermark = "Suchen" };
    private readonly StackPanel _fields = new() { Spacing = 2 };
    private readonly Dictionary<PivotAxis, StackPanel> _areas = [];
    private readonly StackPanel _values = new() { Spacing = 4 };
    private readonly Dictionary<StackPanel, Border> _areaFrames = [];
    private readonly Border _dragGhost = new() { IsVisible = false, IsHitTestVisible = false, Padding = new Thickness(8, 4), CornerRadius = new CornerRadius(4) };
    private PivotTable? _pivot;
    private (string Kind, int Index)? _pressed;
    private Point _pressedAt;
    private bool _dragging;

    public PivotFieldList(CalcEditor editor)
    {
        _editor = editor;
        Width = 300;
        this.Use(BackgroundProperty, "Mat.Bg");
        this.Use(BorderBrushProperty, "Mat.Border");
        BorderThickness = new Thickness(1, 0, 0, 0);
        AutomationProperties.SetName(this, "PivotTable-Felder");
        _search.TextChanged += (_, _) => FillFields();
        AutomationProperties.SetName(_search, "Felder suchen");
        _dragGhost.Use(BackgroundProperty, "Mat.AccentSoft");
        _dragGhost.Use(BorderBrushProperty, "Mat.Accent");
        _dragGhost.BorderThickness = new Thickness(1);

        var close = new Button { Content = new MatIcon { Icon = "x", Size = 14 }, Padding = new Thickness(4) }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(close, "Feldliste schließen");
        ToolTip.SetTip(close, "Feldliste schließen");
        close.Click += (_, _) => _editor.ShowFieldList(false);
        var title = new TextBlock { Text = "PivotTable-Felder", FontWeight = FontWeight.SemiBold, FontSize = 15, VerticalAlignment = VerticalAlignment.Center };
        var header = new DockPanel { Children = { close, title } };
        DockPanel.SetDock(close, Dock.Right);

        var hint = new TextBlock { Text = "Wählen Sie die Felder aus, die Sie dem Bericht hinzufügen möchten:", TextWrapping = TextWrapping.Wrap, FontSize = 12 };
        hint.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var fieldScroller = new ScrollViewer { Content = _fields, MaxHeight = 240 };
        var areasHint = new TextBlock { Text = "Felder zwischen den Bereichen ziehen:", TextWrapping = TextWrapping.Wrap, FontSize = 12, Margin = new Thickness(0, 6, 0, 0) };
        areasHint.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");

        var areas = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*"), RowDefinitions = new RowDefinitions("*,*"), ColumnSpacing = 8, RowSpacing = 8 };
        AddArea(areas, PivotAxis.Filter, "Filter", "filter", 0, 0);
        AddArea(areas, PivotAxis.Column, "Spalten", "columns", 0, 1);
        AddArea(areas, PivotAxis.Row, "Zeilen", "list", 1, 0);
        AddArea(areas, PivotAxis.None, "Werte", "sum", 1, 1);

        var content = new StackPanel { Spacing = 8, Margin = new Thickness(12), Children = { header, hint, _search, fieldScroller, areasHint, areas } };
        Child = new Panel { Children = { new ScrollViewer { Content = content }, new Canvas { Children = { _dragGhost }, IsHitTestVisible = false } } };

        AddHandler(PointerMovedEvent, OnPointerMoved, handledEventsToo: true);
        AddHandler(PointerReleasedEvent, OnPointerReleased, handledEventsToo: true);
    }

    /// <summary>The check boxes of the fields (tests).</summary>
    public IReadOnlyList<CheckBox> FieldBoxes => [.. _fields.Children.OfType<CheckBox>()];

    /// <summary>The names in an area (tests; <see cref="PivotAxis.None"/> = Werte).</summary>
    public IReadOnlyList<string> NamesIn(PivotAxis axis) =>
        [.. (axis == PivotAxis.None ? _values : _areas[axis]).Children.OfType<Border>().Select(chip => ((TextBlock)((DockPanel)chip.Child!).Children[1]).Text ?? string.Empty)];

    /// <summary>The frame of an area (tests drag onto it).</summary>
    public Control AreaOf(PivotAxis axis) => _areaFrames[axis == PivotAxis.None ? _values : _areas[axis]];

    /// <summary>Shows the fields and areas of <paramref name="pivot"/>.</summary>
    public void Show(PivotTable pivot)
    {
        _pivot = pivot;
        FillFields();
        FillAreas();
    }

    private Core.Editing.EditingSession Session => _editor.Session;

    private PivotCache? Cache => _pivot is null ? null : Session.Workbook.FindPivotCache(_pivot.CacheId);

    private void AddArea(Grid grid, PivotAxis axis, string label, string icon, int row, int column)
    {
        var list = axis == PivotAxis.None ? _values : new StackPanel { Spacing = 4 };
        if (axis != PivotAxis.None)
        {
            _areas[axis] = list;
        }

        var frame = new Border
        {
            MinHeight = 110,
            Padding = new Thickness(6),
            CornerRadius = new CornerRadius(4),
            BorderThickness = new Thickness(1),
            Child = new StackPanel { Spacing = 6, Children = { new IconLabel { Icon = icon, Text = label }, list } },
        };
        frame.Use(BackgroundProperty, "Mat.Surface");
        frame.Use(BorderBrushProperty, "Mat.Border");
        AutomationProperties.SetName(frame, label);
        Grid.SetRow(frame, row);
        Grid.SetColumn(frame, column);
        grid.Children.Add(frame);
        _areaFrames[list] = frame;
    }

    private void FillFields()
    {
        _fields.Children.Clear();
        if (_pivot is null || Cache is not { } cache)
        {
            return;
        }

        var filter = _search.Text?.Trim() ?? string.Empty;
        for (var index = 0; index < cache.Fields.Count; index++)
        {
            var name = cache.Fields[index].Name;
            if (filter.Length > 0 && !name.Contains(filter, StringComparison.CurrentCultureIgnoreCase))
            {
                continue;
            }

            var field = index;
            var used = _pivot.Fields[index].Axis != PivotAxis.None || _pivot.DataFields.Any(data => data.Field == index);
            var box = new CheckBox { Content = name, IsChecked = used, Padding = new Thickness(4, 0) };
            AutomationProperties.SetName(box, name);
            box.IsCheckedChanged += (_, _) => Toggle(field, box.IsChecked == true);
            box.ContextMenu = new ContextMenu
            {
                ItemsSource = new[]
                {
                    Item("Zu Berichtsfilter hinzufügen", "filter", () => Run(pivot => Session.AddPivotField(pivot, field, PivotAxis.Filter))),
                    Item("Zu Zeilenbeschriftungen hinzufügen", "list", () => Run(pivot => Session.AddPivotField(pivot, field, PivotAxis.Row))),
                    Item("Zu Spaltenbeschriftungen hinzufügen", "columns", () => Run(pivot => Session.AddPivotField(pivot, field, PivotAxis.Column))),
                    Item("Zu Werten hinzufügen", "sum", () => Run(pivot => Session.AddPivotField(pivot, field, asValue: true))),
                },
            };
            box.AddHandler(PointerPressedEvent, (_, e) => Press("field", field, e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
            _fields.Children.Add(box);
        }
    }

    private void FillAreas()
    {
        foreach (var list in _areas.Values.Append(_values))
        {
            list.Children.Clear();
        }

        if (_pivot is not { } pivot || Cache is not { } cache)
        {
            return;
        }

        foreach (var axis in new[] { PivotAxis.Filter, PivotAxis.Column, PivotAxis.Row })
        {
            var fields = pivot.FieldsOn(axis);
            for (var position = 0; position < fields.Length; position++)
            {
                var field = fields[position];
                if (field == PivotTable.ValuesField && pivot.DataFields.Length < 2)
                {
                    continue;
                }

                _areas[axis].Children.Add(Chip(PivotEngine.Caption(pivot, cache, field), "axis", field, axis, position, fields.Length));
            }
        }

        for (var index = 0; index < pivot.DataFields.Length; index++)
        {
            _values.Children.Add(Chip(pivot.DataFields[index].Name, "data", index, PivotAxis.None, index, pivot.DataFields.Length));
        }
    }

    private Border Chip(string label, string kind, int index, PivotAxis axis, int position, int count)
    {
        var menu = new Button { Content = new MatIcon { Icon = "chevron-down", Size = 12 }, Padding = new Thickness(2) }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(menu, $"{label}: Menü");
        ToolTip.SetTip(menu, "Feldmenü");
        var text = new TextBlock { Text = label, VerticalAlignment = VerticalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis, FontSize = 12 };
        var dock = new DockPanel { Children = { menu, text } };
        DockPanel.SetDock(menu, Dock.Right);
        var chip = new Border { Child = dock, Padding = new Thickness(6, 2, 2, 2), CornerRadius = new CornerRadius(3), BorderThickness = new Thickness(1), Focusable = true };
        chip.Use(BackgroundProperty, "Mat.Bg");
        chip.Use(BorderBrushProperty, "Mat.Border");
        AutomationProperties.SetName(chip, label);

        var flyout = new MenuFlyout();
        void Add(string header, string? icon, Action action, bool enabled = true) =>
            flyout.Items.Add(Item(header, icon, action, enabled));

        if (kind == "data")
        {
            Add("Nach oben", "chevron-up", () => Run(pivot => Session.MovePivotDataField(pivot, index, position - 1)), position > 0);
            Add("Nach unten", "chevron-down", () => Run(pivot => Session.MovePivotDataField(pivot, index, position + 1)), position < count - 1);
            flyout.Items.Add(new Separator());
            Add("Wert entfernen", "trash", () => Run(pivot => Session.RemovePivotDataField(pivot, index)));
            flyout.Items.Add(new Separator());
            Add("Wertfeldeinstellungen …", "settings", () => _ = _editor.ValueFieldSettingsAsync(index));
        }
        else
        {
            Add("Nach oben", "chevron-up", () => Run(pivot => Session.MovePivotField(pivot, index, axis, position - 1)), position > 0);
            Add("Nach unten", "chevron-down", () => Run(pivot => Session.MovePivotField(pivot, index, axis, position + 1)), position < count - 1);
            Add("An den Anfang verschieben", null, () => Run(pivot => Session.MovePivotField(pivot, index, axis, 0)), position > 0);
            Add("An das Ende verschieben", null, () => Run(pivot => Session.MovePivotField(pivot, index, axis, count)), position < count - 1);
            flyout.Items.Add(new Separator());
            if (index != PivotTable.ValuesField)
            {
                Add("In Berichtsfilter verschieben", "filter", () => Run(pivot => Session.MovePivotField(pivot, index, PivotAxis.Filter)), axis != PivotAxis.Filter);
            }

            Add("In Zeilenbeschriftungen verschieben", "list", () => Run(pivot => Session.MovePivotField(pivot, index, PivotAxis.Row)), axis != PivotAxis.Row);
            Add("In Spaltenbeschriftungen verschieben", "columns", () => Run(pivot => Session.MovePivotField(pivot, index, PivotAxis.Column)), axis != PivotAxis.Column);
            if (index != PivotTable.ValuesField)
            {
                Add("In Werte verschieben", "sum", () => Run(pivot => Session.AddPivotField(Session.MovePivotField(pivot, index, PivotAxis.None), index, asValue: true)));
                flyout.Items.Add(new Separator());
                Add("Feld entfernen", "trash", () => Run(pivot => Session.MovePivotField(pivot, index, PivotAxis.None)));
                flyout.Items.Add(new Separator());
                Add("Feldeinstellungen …", "settings", () => _ = _editor.FieldSettingsAsync(index));
            }
        }

        menu.Flyout = flyout;
        chip.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Space or Key.Apps)
            {
                flyout.ShowAt(chip);
                e.Handled = true;
            }
        };
        chip.AddHandler(PointerPressedEvent, (_, e) => Press(kind, kind == "axis" ? index : index, e), Avalonia.Interactivity.RoutingStrategies.Tunnel);
        chip.Tag = (kind, index, axis);
        return chip;
    }

    private static MenuItem Item(string header, string? icon, Action action, bool enabled = true)
    {
        var item = new MenuItem { Header = header, IsEnabled = enabled };
        if (icon is not null)
        {
            item.Icon = new MatIcon { Icon = icon, Size = 14 };
        }

        item.Click += (_, _) => action();
        return item;
    }

    private void Toggle(int field, bool on)
    {
        if (_pivot is not { } pivot)
        {
            return;
        }

        var used = pivot.Fields[field].Axis != PivotAxis.None || pivot.DataFields.Any(data => data.Field == field);
        if (on == used)
        {
            return;
        }

        Run(current => on ? Session.AddPivotField(current, field) : Session.RemovePivotField(current, field));
    }

    /// <summary>Runs a pivot change through the editor (a refusal is shown); the list follows the result.</summary>
    private void Run(Func<PivotTable, PivotTable> change)
    {
        if (_pivot is not { } pivot)
        {
            return;
        }

        _ = _editor.RunAsync("PivotTable", () => _pivot = change(Session.FindPivot(pivot.Name)?.Pivot ?? pivot));
    }

    // ---------------------------------------------------------------- dragging

    private void Press(string kind, int index, PointerPressedEventArgs e)
    {
        if (!e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            return;
        }

        _pressed = (kind, index);
        _pressedAt = e.GetPosition(this);
        _dragging = false;
    }

    private void OnPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_pressed is not { } pressed)
        {
            return;
        }

        var point = e.GetPosition(this);
        if (!_dragging && (Math.Abs(point.X - _pressedAt.X) > DragThreshold || Math.Abs(point.Y - _pressedAt.Y) > DragThreshold))
        {
            _dragging = true;
            _dragGhost.Child = new TextBlock { Text = LabelOf(pressed), FontSize = 12 };
            _dragGhost.IsVisible = true;
            e.Pointer.Capture(this);
        }

        if (_dragging)
        {
            Canvas.SetLeft(_dragGhost, point.X + 8);
            Canvas.SetTop(_dragGhost, point.Y + 8);
            foreach (var (list, frame) in _areaFrames)
            {
                frame.Use(BorderBrushProperty, Contains(frame, e) ? "Mat.Accent" : "Mat.Border");
            }
        }
    }

    private void OnPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        var pressed = _pressed;
        var dragging = _dragging;
        _pressed = null;
        _dragging = false;
        _dragGhost.IsVisible = false;
        foreach (var frame in _areaFrames.Values)
        {
            frame.Use(BorderBrushProperty, "Mat.Border");
        }

        if (!dragging || pressed is not { } source || _pivot is null)
        {
            return;
        }

        e.Pointer.Capture(null);
        var target = _areaFrames.FirstOrDefault(entry => Contains(entry.Value, e));
        if (target.Key is null)
        {
            // Dropped outside every area: the field leaves the table (as in Excel).
            if (source.Kind == "axis")
            {
                Run(pivot => Session.MovePivotField(pivot, source.Index, PivotAxis.None));
            }
            else if (source.Kind == "data")
            {
                Run(pivot => Session.RemovePivotDataField(pivot, source.Index));
            }

            return;
        }

        var position = PositionIn(target.Key, e);
        var axis = _areas.FirstOrDefault(entry => entry.Value == target.Key).Key;
        var toValues = target.Key == _values;
        Run(pivot =>
        {
            switch (source.Kind)
            {
                case "data" when toValues:
                    return Session.MovePivotDataField(pivot, source.Index, position);
                case "data":
                    return pivot;
                case "field" when toValues:
                    return Session.AddPivotField(pivot, source.Index, asValue: true, position: position);
                case "axis" when toValues:
                    return source.Index == PivotTable.ValuesField ? pivot : Session.AddPivotField(Session.MovePivotField(pivot, source.Index, PivotAxis.None), source.Index, asValue: true, position: position);
                default:
                    return Session.MovePivotField(pivot, source.Index, axis, position);
            }
        });
    }

    private string LabelOf((string Kind, int Index) source) => _pivot is not { } pivot || Cache is not { } cache ? string.Empty
        : source.Kind == "data" ? pivot.DataFields[source.Index].Name
        : PivotEngine.Caption(pivot, cache, source.Index);

    private bool Contains(Control control, PointerEventArgs e)
    {
        var point = e.GetPosition(control);
        return point.X >= 0 && point.Y >= 0 && point.X <= control.Bounds.Width && point.Y <= control.Bounds.Height;
    }

    /// <summary>Where in an area's list the pointer is: before the first chip whose middle is below it.</summary>
    private static int PositionIn(StackPanel list, PointerEventArgs e)
    {
        var chips = list.Children.OfType<Border>().ToList();
        for (var index = 0; index < chips.Count; index++)
        {
            if (e.GetPosition(chips[index]).Y < chips[index].Bounds.Height / 2)
            {
                return index;
            }
        }

        return chips.Count;
    }
}
