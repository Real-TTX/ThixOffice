using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using ThixWrite.Core.Editing;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>Einfügen › Tabelle › "Tabelle einfügen …": the number of columns and rows (Word: 5 × 2 at first).</summary>
public sealed class InsertTableDialog : WriteDialog
{
    private readonly WriteEditor _editor;

    public InsertTableDialog(WriteEditor editor)
        : base("Tabelle einfügen", 360)
    {
        _editor = editor;
        Columns = Count(5, EditingSession.MaxTableColumns, "Spaltenanzahl");
        Rows = Count(2, EditingSession.MaxTableRows, "Zeilenanzahl");
        SetBody(new StackPanel { Spacing = 12, Children = { Field("Spaltenanzahl", Columns), Field("Zeilenanzahl", Rows) } });
    }

    public NumericUpDown Columns { get; }

    public NumericUpDown Rows { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new InsertTableDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        if (Columns.Value is not { } columns || Rows.Value is not { } rows)
        {
            return false;
        }

        _editor.Session.InsertTable((int)rows, (int)columns);
        return true;
    }

    internal static NumericUpDown Count(int value, int maximum, string name)
    {
        var field = new NumericUpDown { Value = value, Minimum = 1, Maximum = maximum, Increment = 1, FormatString = "0", Width = 160, HorizontalAlignment = HorizontalAlignment.Left };
        AutomationProperties.SetName(field, name);
        return field;
    }
}

/// <summary>Layout › Zellen teilen …: the number of columns the cell is split into, and whether a vertical merge ends.</summary>
public sealed class SplitCellsDialog : WriteDialog
{
    private readonly WriteEditor _editor;

    public SplitCellsDialog(WriteEditor editor)
        : base("Zellen teilen", 360)
    {
        _editor = editor;
        Columns = InsertTableDialog.Count(2, EditingSession.MaxTableColumns, "Spaltenanzahl");
        Unmerge = new CheckBox { Content = "Verbundene Zeilen trennen", IsChecked = true };
        SetBody(new StackPanel { Spacing = 12, Children = { Field("Spaltenanzahl", Columns), Unmerge } });
    }

    public NumericUpDown Columns { get; }

    public CheckBox Unmerge { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new SplitCellsDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        if (Columns.Value is not { } columns)
        {
            return false;
        }

        _editor.Session.SplitTableCell((int)columns, Unmerge.IsChecked == true);
        return true;
    }
}

/// <summary>Layout › Löschen › Zellen löschen …: Word's three choices.</summary>
public sealed class DeleteCellsDialog : WriteDialog
{
    private readonly WriteEditor _editor;
    private readonly RadioButton _shiftLeft = new() { Content = "Zellen nach links verschieben", GroupName = "deletion", IsChecked = true };
    private readonly RadioButton _row = new() { Content = "Ganze Zeile löschen", GroupName = "deletion" };
    private readonly RadioButton _column = new() { Content = "Ganze Spalte löschen", GroupName = "deletion" };

    public DeleteCellsDialog(WriteEditor editor)
        : base("Zellen löschen", 360)
    {
        _editor = editor;
        SetBody(new StackPanel { Spacing = 8, Children = { _shiftLeft, _row, _column } });
    }

    public RadioButton EntireRow => _row;

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new DeleteCellsDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        var deletion = _row.IsChecked == true ? CellDeletion.EntireRow : _column.IsChecked == true ? CellDeletion.EntireColumn : CellDeletion.ShiftLeft;
        _editor.Session.DeleteTableCells(deletion);
        return true;
    }
}
