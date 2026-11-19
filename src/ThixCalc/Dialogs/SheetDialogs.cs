using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Editor;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Dialogs;

/// <summary>Ansicht › Zoom (and a click on the status bar's percentage): 200, 100, 75, 50, 25 %, An Markierung anpassen, a value of its own.</summary>
public sealed class ZoomDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly List<(RadioButton Button, double? Percent)> _choices = [];

    public ZoomDialog(CalcEditor editor)
        : base("Zoom", 420)
    {
        _editor = editor;
        Percent = new NumericUpDown { Value = (decimal)editor.Zoom, Minimum = 10, Maximum = 400, Increment = 10, FormatString = "0", MinWidth = 140, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        AutomationProperties.SetName(Percent, "Prozent");
        var presets = new StackPanel { Spacing = 6 };
        foreach (var (label, percent) in new (string, double?)[] { ("200 %", 200), ("100 %", 100), ("75 %", 75), ("50 %", 50), ("25 %", 25), ("An Markierung anpassen", null) })
        {
            var button = new RadioButton { Content = label, GroupName = "zoom", IsChecked = percent == editor.Zoom };
            var value = percent;
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true && value is { } chosen)
                {
                    Percent.Value = (decimal)chosen;
                }
            };
            _choices.Add((button, percent));
            presets.Children.Add(button);
        }

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        var custom = Field("Benutzerdefiniert (%)", Percent);
        Grid.SetColumn(custom, 2);
        body.Children.Add(Field("Vergrößerung", presets));
        body.Children.Add(custom);
        SetBody(body);
    }

    public NumericUpDown Percent { get; }

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor) => new ZoomDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        if (_choices.Any(choice => choice.Button.IsChecked == true && choice.Percent is null))
        {
            _editor.ZoomToSelection();
            return true;
        }

        if (Percent.Value is not { } percent)
        {
            return false;
        }

        _editor.Zoom = (double)percent;
        return true;
    }
}

/// <summary>Blatt verschieben oder kopieren: before which sheet (or at the end), and whether as a copy.</summary>
public sealed class MoveSheetDialog : OfficeDialog
{
    private readonly ListBox _before;
    private readonly CheckBox _copy = new() { Content = "Kopie erstellen" };
    private (int Before, bool Copy)? _result;

    private MoveSheetDialog(CalcEditor editor, int index)
        : base("Verschieben oder kopieren", 420)
    {
        var names = editor.Session.Workbook.Sheets.Select(sheet => sheet.Name).Append("(ans Ende stellen)").ToList();
        _before = new ListBox { ItemsSource = names, Height = 200, SelectedIndex = Math.Min(index + 1, names.Count - 1) };
        AutomationProperties.SetName(_before, "Einfügen vor");
        SetBody(new StackPanel
        {
            Spacing = 12,
            Children = { new TextBlock { Text = $"„{editor.Session.Workbook.Sheets[index].Name}“" }, Field("Einfügen vor Blatt", _before), _copy },
        });
    }

    public static async Task<(int Before, bool Copy)?> ShowAsync(Window owner, CalcEditor editor, int index)
    {
        var dialog = new MoveSheetDialog(editor, index);
        return await dialog.ShowAsync(owner) ? dialog._result : null;
    }

    protected override bool Apply()
    {
        _result = (Math.Max(0, _before.SelectedIndex), _copy.IsChecked == true);
        return true;
    }
}

/// <summary>Zellen einfügen / löschen: where the other cells go (or whole rows / columns), like Excel's small dialog.</summary>
public sealed class InsertDeleteCellsDialog : OfficeDialog
{
    private readonly CalcEditor _editor;
    private readonly bool _insert;
    private readonly RadioButton[] _choices;

    private InsertDeleteCellsDialog(CalcEditor editor, bool insert)
        : base(insert ? "Zellen einfügen" : "Löschen", 360)
    {
        _editor = editor;
        _insert = insert;
        _choices = insert
            ? [Choice("Zellen nach rechts verschieben"), Choice("Zellen nach unten verschieben", true), Choice("Ganze Zeile"), Choice("Ganze Spalte")]
            : [Choice("Zellen nach links verschieben"), Choice("Zellen nach oben verschieben", true), Choice("Ganze Zeile"), Choice("Ganze Spalte")];
        var list = new StackPanel { Spacing = 6 };
        foreach (var choice in _choices)
        {
            list.Children.Add(choice);
        }

        SetBody(list);
    }

    private static RadioButton Choice(string label, bool isChecked = false) => new() { Content = label, GroupName = "cells", IsChecked = isChecked };

    public static Task<bool> ShowAsync(Window owner, CalcEditor editor, bool insert) => new InsertDeleteCellsDialog(editor, insert).ShowAsync(owner);

    protected override bool Apply()
    {
        var session = _editor.Session;
        var range = session.Selection;
        var chosen = Array.FindIndex(_choices, choice => choice.IsChecked == true);
        return Run(() =>
        {
            switch (chosen)
            {
                case 2 when _insert:
                    session.InsertRows(range.FirstRow, range.RowCount);
                    break;
                case 2:
                    session.DeleteRows(range.FirstRow, range.RowCount);
                    break;
                case 3 when _insert:
                    session.InsertColumns(range.FirstColumn, range.ColumnCount);
                    break;
                case 3:
                    session.DeleteColumns(range.FirstColumn, range.ColumnCount);
                    break;
                case 0 when _insert:
                    session.InsertCells(range, CellShift.Right);
                    break;
                case 0:
                    session.DeleteCells(range, CellShift.Left);
                    break;
                case 1 when _insert:
                    session.InsertCells(range, CellShift.Down);
                    break;
                default:
                    session.DeleteCells(range, CellShift.Up);
                    break;
            }
        });
    }

    private bool Run(Action action)
    {
        try
        {
            action();
            return true;
        }
        catch (ThixOffice.Core.Models.BusinessException exception)
        {
            _ = QuestionDialog.ShowMessageAsync(this, Title ?? "", exception.Message);
            return false;
        }
    }
}
