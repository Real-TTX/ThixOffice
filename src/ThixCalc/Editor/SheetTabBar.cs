using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Model;
using ThixOffice.Desktop;

namespace ThixCalc.Editor;

/// <summary>
/// The sheet tabs at the bottom left, like Excel's: scroll buttons, a tab per visible sheet (its colour as a bar, the active one
/// raised in the sheet's white with the accent), "+" for a new sheet. A click activates (while a formula is pointed, the
/// other sheet's cells can be pointed into it), a double click renames in place, dragging reorders, the context menu has
/// Einfügen, Löschen, Umbenennen, Verschieben oder kopieren, Registerfarbe, Ausblenden and Einblenden.
/// </summary>
internal sealed class SheetTabBar : Border
{
    private readonly CalcEditor _editor;
    private readonly StackPanel _tabs = new() { Orientation = Orientation.Horizontal, Spacing = 1 };
    private readonly ScrollViewer _scroller;
    private int? _dragFrom;
    private Point _dragStart;

    public SheetTabBar(CalcEditor editor)
    {
        _editor = editor;
        this.Use(BackgroundProperty, "Mat.Bg");
        _scroller = new ScrollViewer
        {
            Content = _tabs,
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
        };

        var left = ArrowButton("chevron-left", "Vorheriges Blatt");
        left.Click += (_, _) => _scroller.Offset = _scroller.Offset.WithX(Math.Max(0, _scroller.Offset.X - 120));
        var right = ArrowButton("chevron-right", "Nächstes Blatt");
        right.Click += (_, _) => _scroller.Offset = _scroller.Offset.WithX(_scroller.Offset.X + 120);
        AddButton = ArrowButton("plus", "Neues Blatt (Umschalt+F11)");
        AddButton.Click += (_, _) => _ = _editor.RunAsync("Blatt einfügen", () => _editor.Session.AddSheet(index: _editor.Session.Workbook.ActiveSheetIndex + 1));

        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,*,Auto") };
        Grid.SetColumn(right, 1);
        Grid.SetColumn(_scroller, 2);
        Grid.SetColumn(AddButton, 3);
        grid.Children.Add(left);
        grid.Children.Add(right);
        grid.Children.Add(_scroller);
        grid.Children.Add(AddButton);
        Child = grid;
        Height = 30;
        Refresh();
    }

    public Button AddButton { get; }

    /// <summary>The tabs (tests click them).</summary>
    public IReadOnlyList<Control> Tabs => [.. _tabs.Children];

    private static Button ArrowButton(string icon, string label)
    {
        var button = new Button
        {
            Content = new MatIcon { Icon = icon, Size = 14 },
            Width = 28,
            Height = 26,
            Padding = new Thickness(0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Theme = Ui.Theme("MatButton.Ghost"),
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        return button;
    }

    /// <summary>The tabs anew (sheets added, removed, renamed, moved, recoloured, another one active).</summary>
    public void Refresh()
    {
        _tabs.Children.Clear();
        var workbook = _editor.Session.Workbook;
        for (var index = 0; index < workbook.Sheets.Count; index++)
        {
            var sheet = workbook.Sheets[index];
            if (sheet.Visibility != SheetVisibility.Visible)
            {
                continue;
            }

            _tabs.Children.Add(Tab(sheet, index, index == workbook.ActiveSheetIndex));
        }
    }

    private Control Tab(Worksheet sheet, int index, bool active)
    {
        var label = new TextBlock
        {
            Text = sheet.Name,
            FontWeight = active ? FontWeight.SemiBold : FontWeight.Normal,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center,
            Margin = new Thickness(14, 0, 14, 0),
        };
        label.Use(TextBlock.ForegroundProperty, active ? "Mat.Accent" : "Mat.Text");
        var colorBar = new Border { Height = 3, VerticalAlignment = Avalonia.Layout.VerticalAlignment.Bottom, Margin = new Thickness(6, 0, 6, active ? 0 : 1) };
        if (sheet.TabColor is { } color)
        {
            colorBar.Background = new SolidColorBrush(GridPalette.ToColor(color.Resolve(_editor.Session.Workbook.Theme, GridPalette.Ink)));
        }
        else if (active)
        {
            colorBar.Use(Border.BackgroundProperty, "Mat.Accent");
        }

        var tab = new Border
        {
            Child = new Panel { Children = { label, colorBar } },
            Height = 28,
            MinWidth = 64,
            CornerRadius = new CornerRadius(0, 0, 4, 4),
            Cursor = new Cursor(StandardCursorType.Hand),
            Tag = index,
        };
        if (active)
        {
            tab.Use(Border.BackgroundProperty, "Mat.Surface");
        }
        else
        {
            tab.Use(Border.BackgroundProperty, "Mat.Bg");
        }

        AutomationProperties.SetName(tab, sheet.Name);
        ToolTip.SetTip(tab, sheet.Name);
        tab.PointerPressed += (_, e) => OnTabPressed(tab, index, e);
        tab.PointerMoved += (_, e) => OnTabMoved(e);
        tab.PointerReleased += (_, e) => OnTabReleased(e);
        tab.ContextMenu = ContextMenuFor(index);
        return tab;
    }

    private void OnTabPressed(Border tab, int index, PointerPressedEventArgs e)
    {
        var point = e.GetCurrentPoint(tab);
        if (point.Properties.IsRightButtonPressed)
        {
            Activate(index);
            return;
        }

        if (!point.Properties.IsLeftButtonPressed)
        {
            return;
        }

        e.Handled = true;
        if (e.ClickCount == 2)
        {
            BeginRename(tab, index);
            return;
        }

        Activate(index);
        _dragFrom = index;
        _dragStart = e.GetPosition(_tabs);
        e.Pointer.Capture(tab);
    }

    private void OnTabMoved(PointerEventArgs e)
    {
        if (_dragFrom is null)
        {
            return;
        }

        var position = e.GetPosition(_tabs);
        Cursor = Math.Abs(position.X - _dragStart.X) > 8 ? new Cursor(StandardCursorType.DragMove) : Cursor.Default;
    }

    private void OnTabReleased(PointerReleasedEventArgs e)
    {
        if (_dragFrom is not { } from)
        {
            return;
        }

        _dragFrom = null;
        e.Pointer.Capture(null);
        Cursor = Cursor.Default;
        var position = e.GetPosition(_tabs);
        if (Math.Abs(position.X - _dragStart.X) <= 8)
        {
            return;
        }

        var target = TabIndexAt(position.X);
        if (target is { } to && to != from)
        {
            _ = _editor.RunAsync("Blatt verschieben", () => _editor.Session.MoveSheet(from, to));
        }
    }

    /// <summary>The sheet index of the tab at <paramref name="x"/> (the first/last when outside).</summary>
    private int? TabIndexAt(double x)
    {
        var tabs = _tabs.Children.OfType<Border>().ToList();
        if (tabs.Count == 0)
        {
            return null;
        }

        foreach (var tab in tabs)
        {
            if (x < tab.Bounds.Right)
            {
                return (int)tab.Tag!;
            }
        }

        return (int)tabs[^1].Tag!;
    }

    /// <summary>A click on a tab: that sheet shows; while a formula is pointed, the edit goes on (references to that sheet).</summary>
    public void Activate(int index)
    {
        var editing = _editor.Editing;
        if (editing.IsEditing && !editing.CanPoint && !editing.Commit())
        {
            return;
        }

        _editor.Session.ActivateSheet(index);
        if (!editing.IsEditing)
        {
            _editor.FocusDocument();
        }
    }

    /// <summary>Umbenennen in place: a field over the tab, Eingabe takes the name, Esc keeps the old one.</summary>
    public void BeginRename(Control tab, int index)
    {
        var sheet = _editor.Session.Workbook.Sheets[index];
        var box = new TextBox { Text = sheet.Name, MinWidth = Math.Max(80, tab.Bounds.Width), Height = 26, Padding = new Thickness(6, 2), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Center };
        AutomationProperties.SetName(box, "Blattname");
        var position = _tabs.Children.IndexOf(tab);
        _tabs.Children[position] = box;
        box.AttachedToVisualTree += (_, _) =>
        {
            box.Focus();
            box.SelectAll();
        };
        var done = false;
        async Task FinishAsync(bool take)
        {
            if (done)
            {
                return;
            }

            done = true;
            if (take && box.Text is { } name && name != sheet.Name)
            {
                await _editor.RunAsync("Umbenennen", () => _editor.Session.RenameSheet(index, name.Trim()));
            }

            Refresh();
            _editor.FocusDocument();
        }

        box.KeyDown += (_, e) =>
        {
            if (e.Key is Key.Enter or Key.Escape)
            {
                e.Handled = true;
                _ = FinishAsync(e.Key == Key.Enter);
            }
        };
        box.LostFocus += (_, _) => _ = FinishAsync(true);
    }

    /// <summary>Starts renaming the active sheet's tab (Format › Blatt umbenennen, the context menu).</summary>
    public void RenameActive()
    {
        var active = _editor.Session.Workbook.ActiveSheetIndex;
        if (_tabs.Children.OfType<Border>().FirstOrDefault(tab => (int)tab.Tag! == active) is { } tab)
        {
            BeginRename(tab, active);
        }
    }

    private ContextMenu ContextMenuFor(int index)
    {
        var menu = new ContextMenu();
        void Add(string label, string? icon, Func<Task> action, bool enabled = true)
        {
            var item = new MenuItem { Header = label, Icon = icon is null ? null : new MatIcon { Icon = icon, Size = 16 }, IsEnabled = enabled };
            item.Click += async (_, _) => await action();
            menu.Items.Add(item);
        }

        var session = _editor.Session;
        var visibleCount = session.Workbook.Sheets.Count(sheet => sheet.Visibility == SheetVisibility.Visible);
        Add("Einfügen", "plus", () => _editor.RunAsync("Blatt einfügen", () => session.AddSheet(index: index)));
        Add("Löschen", "trash", () => _editor.DeleteSheetAsync(index), visibleCount > 1);
        Add("Umbenennen", "edit", () =>
        {
            RenameActive();
            return Task.CompletedTask;
        });
        Add("Verschieben oder kopieren …", "copy", () => _editor.MoveOrCopySheetAsync(index));
        var colors = new MenuItem { Header = "Registerfarbe", Icon = new MatIcon { Icon = "palette", Size = 16 } };
        var none = new MenuItem { Header = "Keine Farbe" };
        none.Click += (_, _) => _ = _editor.RunAsync("Registerfarbe", () => session.SetTabColor(index, null));
        colors.Items.Add(none);
        foreach (var hex in GridPalette.StandardColors)
        {
            var swatch = new Border { Width = 14, Height = 14, CornerRadius = new CornerRadius(3), Background = GridPalette.Brush(hex) };
            var item = new MenuItem { Header = GridPalette.ColorName(hex), Icon = swatch };
            item.Click += (_, _) => _ = _editor.RunAsync("Registerfarbe", () => session.SetTabColor(index, CalcColor.FromHex(hex)));
            colors.Items.Add(item);
        }

        menu.Items.Add(colors);
        menu.Items.Add(new Separator());
        Add("Ausblenden", "eye-off", () => _editor.RunAsync("Ausblenden", () => session.SetSheetVisibility(index, SheetVisibility.Hidden)), visibleCount > 1);
        var hidden = session.Workbook.Sheets.Select((sheet, position) => (sheet, position)).Where(entry => entry.sheet.Visibility == SheetVisibility.Hidden).ToList();
        var unhide = new MenuItem { Header = "Einblenden", Icon = new MatIcon { Icon = "eye", Size = 16 }, IsEnabled = hidden.Count > 0 };
        foreach (var (sheet, position) in hidden)
        {
            var item = new MenuItem { Header = sheet.Name };
            item.Click += (_, _) => _ = _editor.RunAsync("Einblenden", () => session.SetSheetVisibility(position, SheetVisibility.Visible));
            unhide.Items.Add(item);
        }

        menu.Items.Add(unhide);
        return menu;
    }
}
