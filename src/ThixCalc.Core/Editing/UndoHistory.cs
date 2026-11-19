using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Editing;

/// <summary>One reversible change inside a command.</summary>
internal abstract class EditStep
{
    public abstract void Undo(Workbook workbook);

    public abstract void Redo(Workbook workbook);

    /// <summary>Whether undoing/redoing it moves rows, columns or sheets (the engine is rebuilt).</summary>
    public virtual bool IsStructural => false;
}

/// <summary>Cells before and after (value or formula, style).</summary>
internal sealed class CellStep(Worksheet sheet, List<(int Row, int Column, Cell Before, Cell After)> cells) : EditStep
{
    public override void Undo(Workbook workbook)
    {
        for (var index = cells.Count - 1; index >= 0; index--)
        {
            var (row, column, before, _) = cells[index];
            sheet.SetCell(row, column, before);
        }
    }

    public override void Redo(Workbook workbook)
    {
        foreach (var (row, column, _, after) in cells)
        {
            sheet.SetCell(row, column, after);
        }
    }
}

/// <summary>A change undone and redone by two actions (sheet state, sizes, names, views).</summary>
internal sealed class ActionStep(Action undo, Action redo, bool structural = false) : EditStep
{
    public override bool IsStructural => structural;

    public override void Undo(Workbook workbook) => undo();

    public override void Redo(Workbook workbook) => redo();
}

/// <summary>
/// Everything a structural change may touch besides the cells it moves: every formula's parsed form, the names, and each
/// sheet's ranges (merges, formats, validation, links, notes, filter, tables, rows and columns). Restoring it after moving the
/// cells back undoes reference rewriting exactly.
/// </summary>
internal sealed class WorkbookSnapshot
{
    private readonly List<(Worksheet Sheet, int Row, int Column, Formulas.Formula Formula, CellRange? ArrayRange)> _formulas = [];
    private readonly List<DefinedName> _names;
    private readonly List<SheetState> _sheets = [];

    public WorkbookSnapshot(Workbook workbook)
    {
        _names = [.. workbook.Names];
        foreach (var sheet in workbook.Sheets)
        {
            _sheets.Add(new SheetState(sheet));
            foreach (var (row, column, record) in sheet.Store.Enumerate(CellRange.Whole))
            {
                if (record.Formula is { } formula)
                {
                    _formulas.Add((sheet, row, column, formula.Formula, formula.ArrayRange));
                }
            }
        }
    }

    public void Restore(Workbook workbook)
    {
        // By position: undoing cell edits recreates formula cells, so identities may differ, positions do not.
        foreach (var (sheet, row, column, formula, arrayRange) in _formulas)
        {
            if (sheet.GetFormulaCell(row, column) is { } cell)
            {
                cell.Formula = formula;
                cell.ArrayRange = arrayRange;
            }
        }

        workbook.Names.Clear();
        workbook.Names.AddRange(_names);
        foreach (var state in _sheets)
        {
            state.Restore();
        }
    }

    /// <summary>The lists, sizes and view of one sheet.</summary>
    internal sealed class SheetState
    {
        private readonly Worksheet _sheet;
        private readonly List<CellRange> _merges;
        private readonly List<ConditionalFormat> _formats;
        private readonly List<DataValidation> _validations;
        private readonly List<Hyperlink> _links;
        private readonly Dictionary<CellAddress, CellComment> _comments;
        private readonly AutoFilter? _filter;
        private readonly SortState? _sort;
        private readonly List<TableDefinition> _tables;
        private readonly List<SheetChart> _charts;
        private readonly List<Model.Pivot.PivotTable> _pivots;
        private readonly (int, AxisInfo)[] _rows;
        private readonly (int, AxisInfo)[] _columns;
        private readonly double _rowSize;
        private readonly double _columnSize;
        private readonly SheetView _view;
        private readonly PageSetup _pageSetup;

        public SheetState(Worksheet sheet)
        {
            _sheet = sheet;
            _merges = [.. sheet.Merges.Ranges];
            _formats = [.. sheet.ConditionalFormats];
            _validations = [.. sheet.Validations];
            _links = [.. sheet.Hyperlinks];
            _comments = new Dictionary<CellAddress, CellComment>(sheet.Comments);
            _filter = sheet.AutoFilter;
            _sort = sheet.SortState;
            _tables = [.. sheet.Tables];
            _charts = [.. sheet.Charts];
            _pivots = [.. sheet.PivotTables];
            _rows = sheet.Rows.Snapshot();
            _columns = sheet.Columns.Snapshot();
            _rowSize = sheet.Rows.DefaultSize;
            _columnSize = sheet.Columns.DefaultSize;
            _view = sheet.View;
            _pageSetup = sheet.PageSetup;
        }

        public void Restore()
        {
            _sheet.PageSetup = _pageSetup;
            _sheet.Merges.Replace(_merges);
            Replace(_sheet.ConditionalFormats, _formats);
            Replace(_sheet.Validations, _validations);
            Replace(_sheet.Hyperlinks, _links);
            _sheet.Comments.Clear();
            foreach (var (address, comment) in _comments)
            {
                _sheet.Comments[address] = comment;
            }

            _sheet.AutoFilter = _filter;
            _sheet.SortState = _sort;
            Replace(_sheet.Tables, _tables);
            Replace(_sheet.Charts, _charts);
            Replace(_sheet.PivotTables, _pivots);
            _sheet.Rows.Restore(_rows, _rowSize);
            _sheet.Columns.Restore(_columns, _columnSize);
            _sheet.View = _view;
        }

        private static void Replace<T>(List<T> target, List<T> source)
        {
            target.Clear();
            target.AddRange(source);
        }
    }
}

/// <summary>An undo step of the user: the steps of one command with a German label ("Eingabe", "Zeilen einfügen").</summary>
internal sealed class EditTransaction(string label, SelectionState before)
{
    public string Label { get; } = label;

    public List<EditStep> Steps { get; } = [];

    public SelectionState Before { get; } = before;

    public SelectionState After { get; set; } = before;

    public bool IsStructural => Steps.Any(step => step.IsStructural);
}

/// <summary>The selection of a sheet as undo/redo restores it.</summary>
internal readonly record struct SelectionState(int SheetIndex, SheetView View);

/// <summary>The undo and redo stacks.</summary>
internal sealed class UndoHistory
{
    public const int Limit = 100;

    private readonly LinkedList<EditTransaction> _undo = new();
    private readonly Stack<EditTransaction> _redo = new();

    public bool CanUndo => _undo.Count > 0;

    public bool CanRedo => _redo.Count > 0;

    public string? UndoLabel => _undo.Last?.Value.Label;

    public string? RedoLabel => _redo.Count > 0 ? _redo.Peek().Label : null;

    public void Push(EditTransaction transaction)
    {
        _undo.AddLast(transaction);
        if (_undo.Count > Limit)
        {
            _undo.RemoveFirst();
        }

        _redo.Clear();
    }

    public EditTransaction? PopUndo()
    {
        if (_undo.Last is not { } last)
        {
            return null;
        }

        _undo.RemoveLast();
        _redo.Push(last.Value);
        return last.Value;
    }

    public EditTransaction? PopRedo()
    {
        if (_redo.Count == 0)
        {
            return null;
        }

        var transaction = _redo.Pop();
        _undo.AddLast(transaction);
        return transaction;
    }

    public void Clear()
    {
        _undo.Clear();
        _redo.Clear();
    }
}
