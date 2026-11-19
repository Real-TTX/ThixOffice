using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Desktop;

namespace ThixCalc.Editor;

/// <summary>
/// Excel's Bearbeitungsleiste above the grid: the Namenfeld (the active cell or a name that covers the selection; typing a
/// cell, a range, "Tabelle2!B4" or a defined name and Eingabe goes there, a new name defines it for the selection; its list
/// holds the defined names), Abbrechen / Eingeben (while a cell is edited), fx (Funktion einfügen) and the content field –
/// the second <see cref="FormulaTextBox"/> of the cell editing, wrapped, one line high or expanded (the chevron).
/// </summary>
internal sealed class FormulaBar : Border
{
    private const double LineHeight = 22;
    private readonly CalcEditor _editor;
    private readonly Button _cancel;
    private readonly Button _enter;
    private readonly Button _expand;
    private readonly MatIcon _expandIcon = new() { Icon = "chevron-down", Size = 14 };
    private bool _expanded;

    public FormulaBar(CalcEditor editor, FormulaTextBox field)
    {
        _editor = editor;
        Field = field;
        this.Use(BackgroundProperty, "Mat.Surface");
        this.Use(BorderBrushProperty, "Mat.Border");
        BorderThickness = new Thickness(0, 0, 0, 1);
        Padding = new Thickness(6, 4);

        NameBox = new TextBox
        {
            Width = 110,
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            MinHeight = 26,
            Padding = new Thickness(6, 3),
            FontSize = 13,
        };
        AutomationProperties.SetName(NameBox, "Namenfeld");
        ToolTip.SetTip(NameBox, "Namenfeld – Zelle, Bereich oder Name eingeben und Eingabe drücken");
        NameBox.KeyDown += OnNameBoxKey;
        NameBox.LostFocus += (_, _) => Refresh();

        var names = IconButton("chevron-down", "Namen", 20);
        names.Click += (_, _) => ShowNames(names);

        _cancel = IconButton("x", "Abbrechen (Esc)", 28);
        _cancel.Click += (_, _) => _editor.Editing.Cancel();
        _enter = IconButton("check", "Eingeben (Eingabe)", 28);
        _enter.Click += (_, _) => _editor.Editing.Commit();
        var function = new Button
        {
            Content = new TextBlock { Text = "fx", FontStyle = FontStyle.Italic, FontWeight = FontWeight.SemiBold, FontSize = 14 },
            Width = 30,
            Height = 26,
            Padding = new Thickness(0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Theme = Ui.Theme("MatButton.Ghost"),
        };
        AutomationProperties.SetName(function, "Funktion einfügen");
        ToolTip.SetTip(function, Ui.Tooltip("Funktion einfügen", OperatingSystem.IsMacOS() ? null : "Shift+F3"));
        function.Click += (_, _) => _ = _editor.InsertFunctionAsync();

        field.Wrap = true;
        field.Padding = new Thickness(6, 3);
        field.Height = LineHeight + 4;
        field.VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top;
        field.Use(FormulaTextBox.ForegroundBrushProperty, "Mat.Text");
        AutomationProperties.SetName(field, "Bearbeitungsleiste");
        var fieldFrame = new Border { Child = field, BorderThickness = new Thickness(1), CornerRadius = new CornerRadius(4) };
        fieldFrame.Use(Border.BorderBrushProperty, "Mat.Border");
        fieldFrame.Use(Border.BackgroundProperty, "Mat.Surface");

        _expand = IconButton("chevron-down", "Bearbeitungsleiste erweitern", 26);
        _expand.Content = _expandIcon;
        _expand.Click += (_, _) => Expanded = !Expanded;

        var separator = new Border { Width = 1, Margin = new Thickness(6, 3), VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch };
        separator.Use(Border.BackgroundProperty, "Mat.Border");
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,Auto,Auto,Auto,Auto,Auto,*,Auto") };
        var items = new Control[] { NameBox, names, separator, _cancel, _enter, function, fieldFrame, _expand };
        for (var index = 0; index < items.Length; index++)
        {
            Grid.SetColumn(items[index], index);
            grid.Children.Add(items[index]);
        }

        fieldFrame.Margin = new Thickness(4, 0, 4, 0);
        Child = grid;
        Refresh();
    }

    /// <summary>The Namenfeld.</summary>
    public TextBox NameBox { get; }

    /// <summary>The content field.</summary>
    public FormulaTextBox Field { get; }

    /// <summary>The field shows several lines (the chevron), like Excel's expanded formula bar.</summary>
    public bool Expanded
    {
        get => _expanded;
        set
        {
            _expanded = value;
            Field.Height = (value ? 4 * LineHeight : LineHeight) + 4;
            _expandIcon.Icon = value ? "chevron-up" : "chevron-down";
            ToolTip.SetTip(_expand, value ? "Bearbeitungsleiste reduzieren" : "Bearbeitungsleiste erweitern");
        }
    }

    private static Button IconButton(string icon, string label, double width)
    {
        var button = new Button
        {
            Content = new MatIcon { Icon = icon, Size = 14 },
            Width = width,
            Height = 26,
            Padding = new Thickness(0),
            VerticalAlignment = Avalonia.Layout.VerticalAlignment.Top,
            HorizontalContentAlignment = Avalonia.Layout.HorizontalAlignment.Center,
            Theme = Ui.Theme("MatButton.Ghost"),
        };
        AutomationProperties.SetName(button, label);
        ToolTip.SetTip(button, label);
        return button;
    }

    /// <summary>Shows the active cell (or name) and the content; Abbrechen/Eingeben only while a cell is edited.</summary>
    public void Refresh()
    {
        var editing = _editor.Editing.IsEditing;
        _cancel.IsEnabled = editing;
        _enter.IsEnabled = editing;
        if (!NameBox.IsFocused)
        {
            NameBox.Text = SelectionName();
        }
    }

    /// <summary>"B3", a defined name, or "4Z x 3S" while a range is being dragged (Excel's way of counting).</summary>
    private string SelectionName()
    {
        var session = _editor.Session;
        return session.NameBoxText;
    }

    private void OnNameBoxKey(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                e.Handled = true;
                _ = _editor.RunAsync("Gehe zu", () => _editor.GoTo(NameBox.Text ?? string.Empty));
                _editor.FocusDocument();
                break;
            case Key.Escape:
                e.Handled = true;
                NameBox.Text = SelectionName();
                _editor.FocusDocument();
                break;
        }
    }

    private void ShowNames(Control anchor)
    {
        var flyout = new MenuFlyout { Placement = PlacementMode.BottomEdgeAlignedLeft };
        var names = _editor.Session.Workbook.Names.Where(name => !name.IsBuiltIn && !name.Hidden).OrderBy(name => name.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
        if (names.Count == 0)
        {
            flyout.Items.Add(new MenuItem { Header = "Keine Namen definiert", IsEnabled = false });
        }

        foreach (var name in names)
        {
            var item = new MenuItem { Header = name.Name };
            item.Click += (_, _) => _ = _editor.RunAsync("Gehe zu", () => _editor.GoTo(name.Name));
            flyout.Items.Add(item);
        }

        flyout.ShowAt(anchor);
    }
}
