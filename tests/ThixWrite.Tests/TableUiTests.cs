using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using MatDesktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using Xunit;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>Tables in the real window: the grid of Einfügen › Tabelle, the contextual tabs, Tab, dialogs, dragging a column border.</summary>
public sealed class TableUiTests
{
    private static TablePicker PickerOf(OfficeWindow window)
    {
        window.Ribbon.SelectTab("insert");
        Settle();
        return window.GetVisualDescendants().OfType<TablePicker>().Single();
    }

    private static async Task<OfficeWindow> WindowWithTableAsync(int rows, int columns)
    {
        var window = await NewWindowAsync();
        await TypeAsync(window, "Vorher");
        await PressAsync(window, PhysicalKey.Enter);
        EditorOf(window).Session.InsertTable(rows, columns);
        await SettleAsync();
        return window;
    }

    [AvaloniaFact]
    public async Task The_grid_of_Einfuegen_Tabelle_inserts_the_size_chosen_with_the_arrow_keys_and_the_table_tabs_appear()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        Assert.DoesNotContain(TableCommands.Context, window.RibbonDefinition.ActiveContexts);

        var picker = PickerOf(window);
        Assert.True(picker.IsEnabled);
        picker.Open();
        await SettleAsync();
        Assert.True(picker.IsOpen);
        await PressAsync(window, PhysicalKey.ArrowRight);
        await PressAsync(window, PhysicalKey.ArrowRight);
        await PressAsync(window, PhysicalKey.ArrowDown);
        Assert.Equal((2, 3), picker.Chosen);
        await PressAsync(window, PhysicalKey.Enter);
        await SettleAsync();

        var session = EditorOf(window).Session;
        var table = Assert.Single(TableSpan.All(session.Document));
        Assert.Equal((2, 3), (table.Table.RowCount, table.Table.ColumnCount));
        Assert.Contains(TableCommands.Context, window.RibbonDefinition.ActiveContexts);
        Assert.False(picker.IsEnabled); // no table inside a table

        // Typing and Tab through real keys: the last Tab adds a row.
        await TypeAsync(window, "A1");
        for (var cell = 0; cell < 6; cell++)
        {
            await PressAsync(window, PhysicalKey.Tab);
        }

        Assert.Equal(3, TableSpan.All(session.Document).Single().Table.RowCount);
        await TypeAsync(window, "neu");
        Assert.Equal("neu", session.Document.ParagraphAt(session.Selection.Active.Block)!.Text);

        // Out of the table: the tabs go away.
        await PressAsync(window, PhysicalKey.End, CommandKey);
        await SettleAsync();
        Assert.DoesNotContain(TableCommands.Context, window.RibbonDefinition.ActiveContexts);
    }

    [AvaloniaFact]
    public async Task The_Layout_tab_inserts_rows_merges_cells_and_its_dialogs_delete_and_split()
    {
        using var data = new TestData();
        var window = await WindowWithTableAsync(2, 2);
        var session = EditorOf(window).Session;
        window.Ribbon.SelectTab("tableLayout");
        await SettleAsync();

        window.RibbonDefinition.Invoke(window.RibbonDefinition.Get<MatRibbonButton>(WriteCommandIds.TableInsertBelow));
        await SettleAsync();
        Assert.Equal(3, session.CurrentTable!.Table.RowCount);

        // Shift+Right twice from the first cell: two cells selected – Zellen verbinden.
        session.SetCaret(new DocumentPosition(session.CurrentTable.Start, 0));
        await PressAsync(window, PhysicalKey.ArrowRight, RawInputModifiers.Shift);
        Assert.True(window.Commands.Find(WriteCommandIds.TableMerge)!.IsEnabled);
        await window.Commands.ExecuteAsync(WriteCommandIds.TableMerge);
        await SettleAsync();
        Assert.Equal(2, session.CurrentTable!.Table.CellAt(0, 0).GridSpan);

        _ = window.Commands.ExecuteAsync(WriteCommandIds.TableSplit);
        await SettleAsync();
        var split = window.OwnedWindows.OfType<SplitCellsDialog>().Single();
        split.Confirm();
        await SettleAsync();
        Assert.Equal(2, session.CurrentTable!.Table.Rows[0].CellCount);

        _ = window.Commands.ExecuteAsync(WriteCommandIds.TableDeleteCells);
        await SettleAsync();
        var delete = window.OwnedWindows.OfType<DeleteCellsDialog>().Single();
        delete.EntireRow.IsChecked = true;
        delete.Confirm();
        await SettleAsync();
        Assert.Equal(2, session.CurrentTable!.Table.RowCount);

        await window.Commands.ExecuteAsync(WriteCommandIds.TableDeleteTable);
        await SettleAsync();
        Assert.Empty(TableSpan.All(session.Document));
    }

    [AvaloniaFact]
    public async Task Tabelle_einfuegen_asks_for_columns_and_rows()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        _ = window.Commands.ExecuteAsync(WriteCommandIds.InsertTable);
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<InsertTableDialog>().Single();
        Assert.Equal(5, dialog.Columns.Value);
        dialog.Columns.Value = 4;
        dialog.Rows.Value = 3;
        dialog.Confirm();
        await SettleAsync();

        var table = Assert.Single(TableSpan.All(EditorOf(window).Session.Document)).Table;
        Assert.Equal((3, 4), (table.RowCount, table.ColumnCount));
    }

    [AvaloniaFact]
    public async Task Dragging_a_column_border_changes_the_widths_of_its_columns()
    {
        using var data = new TestData();
        var window = await WindowWithTableAsync(2, 3);
        var editor = EditorOf(window);
        var before = editor.Session.CurrentTable!.Table.Grid.ToList();
        var cell = editor.Layout.Pages[0].Cells.First(candidate => candidate.Row == 0 && candidate.Cell == 0);
        var border = editor.Surface.ToSurface(0, cell.X + cell.Width, cell.Y + (cell.Height / 2), 0, 0).TopLeft;
        var start = editor.Surface.TranslatePoint(border, window)!.Value;
        var end = start + new Vector(30, 0);

        window.MouseMove(start);
        window.MouseDown(start, MouseButton.Left);
        window.MouseMove(end, RawInputModifiers.LeftMouseButton);
        window.MouseUp(end, MouseButton.Left);
        await SettleAsync();

        var after = TableSpan.All(editor.Session.Document).Single().Table.Grid.ToList();
        var moved = 30 / editor.Surface.Scale;
        Assert.Equal(before[0] + moved, after[0], 1);
        Assert.Equal(before[1] - moved, after[1], 1);
        Assert.Equal(before[2], after[2], 1);
        Assert.Equal(before.Sum(), after.Sum(), 1);

        editor.Session.Undo();
        Assert.Equal(before, TableSpan.All(editor.Session.Document).Single().Table.Grid.ToList());
    }
}
