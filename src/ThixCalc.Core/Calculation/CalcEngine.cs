using System.Diagnostics;
using System.Runtime.CompilerServices;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>What a recalculation did.</summary>
public sealed record RecalculationResult(int Computed, TimeSpan Elapsed, bool Canceled, IReadOnlyList<string> CircularReferences);

/// <summary>
/// The calculation engine of a workbook. Every formula cell is a node of the dependency graph; a change marks the formulas
/// that read it (and theirs, transitively) dirty – incremental, never a full recalculation unless asked. Recalculation
/// evaluates the dirty formulas in dependency order (an iterative depth-first walk over their static precedents, so chains
/// of 100,000 formulas need no deep recursion); references only evaluation knows (INDIRECT, OFFSET) are computed on demand.
/// Circular references are detected (their cells show 0 and are listed; with iteration on they iterate). Dynamic arrays
/// spill into empty neighbours or give #SPILL! (#ÜBERLAUF!). Volatile functions recalculate every time. Cancellable.
/// </summary>
public sealed class CalcEngine
{
    private const int MaxPasses = 100;

    private readonly Workbook _workbook;
    private readonly DependencyGraph _graph = new();
    private readonly Dictionary<int, SheetFormulaIndex> _indexes = [];
    private readonly List<FormulaCell> _formulas = [];
    private readonly HashSet<FormulaCell> _volatile = [];
    private readonly HashSet<FormulaCell> _blocked = [];
    private readonly HashSet<FormulaCell> _circular = [];
    private readonly Dictionary<DefinedName, Formula?> _names = [];
    private List<FormulaCell> _dirty = [];
    private int _threeDReferences;
    private int _computed;

    internal CalcEngine(Workbook workbook)
    {
        _workbook = workbook;
    }

    public Workbook Workbook => _workbook;

    /// <summary>The locale functions use for text/number conversions (VALUE, TEXT, DATEVALUE, "x"&amp;1,5).</summary>
    public CalcLocale Locale { get; set; } = CalcLocale.German;

    /// <summary>The clock of NOW and TODAY (tests pin it).</summary>
    public Func<DateTime> Clock { get; set; } = () => DateTime.Now;

    /// <summary>The random source of RAND, RANDBETWEEN, RANDARRAY (tests seed it).</summary>
    public Random Random { get; set; } = new();

    public int FormulaCount => _formulas.Count;

    /// <summary>Formulas wait for a recalculation (dirty or volatile).</summary>
    public bool NeedsRecalculation => _dirty.Count > 0 || _volatile.Count > 0;

    /// <summary>The cells in circular references found by the last recalculation ("Tabelle1!A1").</summary>
    public IReadOnlyList<string> CircularReferences => _circular.Select(cell => cell.ToString()).Order(StringComparer.Ordinal).ToList();

    internal CancellationToken CancellationToken { get; private set; }

    internal int DependencyCount => _graph.CellListenerCount + _graph.RangeNodeCount;

    // ---------------------------------------------------------------- recalculation

    /// <summary>Recalculates what is dirty (and the volatile formulas) in dependency order.</summary>
    public RecalculationResult Recalculate(CancellationToken cancellationToken = default)
    {
        var stopwatch = Stopwatch.StartNew();
        CancellationToken = cancellationToken;
        _computed = 0;
        _circular.Clear();
        foreach (var formula in _volatile.ToArray())
        {
            MarkDirty(formula);
        }

        try
        {
            for (var pass = 0; pass < MaxPasses && _dirty.Count > 0; pass++)
            {
                var batch = _dirty;
                _dirty = [];
                for (var index = 0; index < batch.Count; index++)
                {
                    if ((index & 1023) == 0 && cancellationToken.IsCancellationRequested)
                    {
                        // Keep what is left for the next recalculation.
                        _dirty.AddRange(batch.Skip(index).Where(formula => formula.State == FormulaState.Dirty));
                        cancellationToken.ThrowIfCancellationRequested();
                    }

                    var formula = batch[index];
                    if (formula.State == FormulaState.Dirty && formula.EngineIndex >= 0)
                    {
                        Compute(formula);
                    }
                    else if (formula.State == FormulaState.Dirty)
                    {
                        formula.State = FormulaState.Clean;
                    }
                }
            }

            if (_circular.Count > 0 && _workbook.CalculationSettings.Iterate)
            {
                Iterate();
            }
        }
        catch (OperationCanceledException)
        {
            return new RecalculationResult(_computed, stopwatch.Elapsed, true, CircularReferences);
        }
        finally
        {
            CancellationToken = default;
        }

        return new RecalculationResult(_computed, stopwatch.Elapsed, false, CircularReferences);
    }

    /// <summary>Marks every formula dirty and recalculates (F9 with Ctrl+Alt; after loading a file).</summary>
    public RecalculationResult RecalculateAll(CancellationToken cancellationToken = default)
    {
        foreach (var formula in _formulas)
        {
            if (formula.State == FormulaState.Clean)
            {
                formula.State = FormulaState.Dirty;
                _dirty.Add(formula);
            }
        }

        return Recalculate(cancellationToken);
    }

    /// <summary>
    /// Rebuilds the dependency graph from the sheets (after rows/columns/sheets moved or names changed): positions are read
    /// from the cells again, every formula is registered anew and becomes dirty.
    /// </summary>
    public void Rebuild() => Rebuild(markDirty: true);

    /// <summary>Rebuilds the graph; <paramref name="markDirty"/> false keeps the cached values (after loading a file).</summary>
    internal void Rebuild(bool markDirty)
    {
        _graph.Clear();
        _formulas.Clear();
        _volatile.Clear();
        _blocked.Clear();
        _names.Clear();
        _dirty.Clear();
        _threeDReferences = 0;
        foreach (var index in _indexes.Values)
        {
            index.Clear();
        }

        foreach (var sheet in _workbook.Sheets)
        {
            var store = sheet.Store;
            for (var rowIndex = 0; rowIndex < store.RowCount; rowIndex++)
            {
                var row = store.RowKeyAt(rowIndex);
                var data = store.RowAt(rowIndex);
                for (var position = 0; position < data.Count; position++)
                {
                    if (data.CellAt(position).Payload is FormulaCell formula)
                    {
                        formula.Sheet = sheet;
                        formula.Row = row;
                        formula.Column = data.ColumnAt(position);
                        formula.EngineIndex = -1;
                        formula.State = FormulaState.Clean;
                        Add(formula, markDirty);
                    }
                }
            }
        }
    }

    /// <summary>Evaluates a formula outside any cell's storage (conditional formats, validation, the name manager).</summary>
    public CellValue Evaluate(Formula formula, Worksheet sheet, CellAddress at)
    {
        var context = new EvaluationContext(this, sheet, at.Row, at.Column, arrayMode: false);
        try
        {
            var result = Evaluator.Finish(Evaluator.Evaluate(formula.Root, context), context);
            return result.Kind == OperandKind.Value ? result.Value : Evaluator.ToScalar(result, context);
        }
        catch (CalcErrorException error)
        {
            return CellValue.FromError(error.Error);
        }
    }

    /// <summary>Evaluates a formula and returns all its values (list sources of validation).</summary>
    internal Operand EvaluateOperand(Formula formula, Worksheet sheet, CellAddress at, bool arrayMode)
    {
        var context = new EvaluationContext(this, sheet, at.Row, at.Column, arrayMode);
        try
        {
            return Evaluator.Evaluate(formula.Root, context);
        }
        catch (CalcErrorException error)
        {
            return Operand.Error(error.Error);
        }
    }

    private void Compute(FormulaCell start)
    {
        var stack = new List<Frame>();
        Push(stack, start);
        try
        {
            while (stack.Count > 0)
            {
                var frame = stack[^1];
                frame.Pending ??= DirtyPrecedents(frame.Cell);
                if (frame.Next < frame.Pending.Count)
                {
                    var precedent = frame.Pending[frame.Next++];
                    if (precedent.State == FormulaState.Dirty && precedent.EngineIndex >= 0)
                    {
                        Push(stack, precedent);
                    }
                    else if (precedent.State == FormulaState.Computing)
                    {
                        MarkCycle(stack, precedent);
                    }

                    continue;
                }

                stack.RemoveAt(stack.Count - 1);
                EvaluateCell(frame.Cell);
            }
        }
        catch (OperationCanceledException)
        {
            foreach (var frame in stack)
            {
                frame.Cell.State = FormulaState.Dirty;
                _dirty.Add(frame.Cell);
            }

            throw;
        }
    }

    private static void Push(List<Frame> stack, FormulaCell cell)
    {
        cell.State = FormulaState.Computing;
        stack.Add(new Frame(cell));
    }

    private void MarkCycle(List<Frame> stack, FormulaCell precedent)
    {
        for (var index = stack.Count - 1; index >= 0; index--)
        {
            var cell = stack[index].Cell;
            cell.IsCircular = true;
            _circular.Add(cell);
            if (cell == precedent)
            {
                break;
            }
        }
    }

    /// <summary>Iterative calculation of circular references (Datei › Optionen › Iterative Berechnung).</summary>
    private void Iterate()
    {
        var settings = _workbook.CalculationSettings;
        var cells = _circular.ToList();
        for (var iteration = 0; iteration < settings.MaxIterations; iteration++)
        {
            var change = 0.0;
            foreach (var cell in cells)
            {
                var before = cell.Value.Number;
                cell.State = FormulaState.Computing;
                EvaluateCell(cell, iterating: true);
                change = Math.Max(change, Math.Abs(cell.Value.Number - before));
            }

            if (change < settings.MaxChange)
            {
                break;
            }
        }
    }

    /// <summary>The formulas a formula reads statically that are not clean yet (spill anchors of spilled cells included).</summary>
    private List<FormulaCell> DirtyPrecedents(FormulaCell formula)
    {
        var result = new List<FormulaCell>();
        ForEachPrecedentArea(formula, (sheet, range) =>
        {
            if (range.IsSingleCell)
            {
                var cell = sheet.GetFormulaCell(range.FirstRow, range.FirstColumn);
                if (cell is not null && cell.State != FormulaState.Clean)
                {
                    result.Add(cell);
                }
            }
            else
            {
                IndexOf(sheet).ForEachIn(range, cell =>
                {
                    if (cell.State != FormulaState.Clean)
                    {
                        result.Add(cell);
                    }
                });
            }

            foreach (var anchor in IndexOf(sheet).Anchors)
            {
                if (anchor.State != FormulaState.Clean && anchor.SpillRange is { } spill && spill.Intersects(range) && !result.Contains(anchor))
                {
                    result.Add(anchor);
                }
            }
        });
        return result;
    }

    private void EvaluateCell(FormulaCell cell, bool iterating = false)
    {
        var context = new EvaluationContext(this, cell.Sheet, cell.Row, cell.Column, cell.IsDynamicArray || cell.ArrayRange is not null, cell);
        Operand result;
        try
        {
            result = Evaluator.Finish(Evaluator.Evaluate(cell.Formula.Root, context), context);
        }
        catch (UnavailableFunctionException) when (!cell.Value.IsBlank && !cell.Value.Equals(CellValue.FromError(CellError.GettingData)))
        {
            // Not computable offline: the value the file had stays (and so do its spilled values).
            cell.State = FormulaState.Clean;
            _computed++;
            return;
        }
        catch (CalcErrorException error)
        {
            result = Operand.Error(error.Error);
        }
        catch (InsufficientExecutionStackException)
        {
            result = Operand.Error(CellError.Calc);
        }

        cell.UsedArrayEvaluation = context.UsedArrays;
        cell.State = FormulaState.Clean;
        _computed++;
        if (cell.IsCircular && !iterating && !_workbook.CalculationSettings.Iterate)
        {
            result = CellValue.Zero;
        }

        ApplyResult(cell, result);
    }

    // ---------------------------------------------------------------- results and spills

    private void ApplyResult(FormulaCell cell, Operand result)
    {
        var oldValue = cell.Value;
        var oldSpill = cell.SpillRange;
        if (cell.ArrayRange is { } fixedRange)
        {
            var array = result.Kind == OperandKind.Array ? result.Array : ArrayValue.Scalar(result.Value);
            cell.ArrayResult = array;
            WriteArray(cell, fixedRange, array, spilled: true);
            cell.Value = Normalize(array.Broadcast(0, 0));
            NoteValueChanged(cell, oldValue, null, fixedRange);
            return;
        }

        if (result.Kind == OperandKind.Array && result.Array.Count > 1)
        {
            var array = result.Array;
            cell.ArrayResult = array;
            var lastRow = (long)cell.Row + array.Rows - 1;
            var lastColumn = (long)cell.Column + array.Columns - 1;
            if (lastRow > SheetLimits.LastRow || lastColumn > SheetLimits.LastColumn)
            {
                Block(cell, null);
                NoteValueChanged(cell, oldValue, oldSpill, null);
                return;
            }

            var desired = new CellRange(cell.Row, cell.Column, (int)lastRow, (int)lastColumn);
            if (IsBlocked(cell, desired))
            {
                Block(cell, desired);
                NoteValueChanged(cell, oldValue, oldSpill, null);
                return;
            }

            if (oldSpill is { } previous)
            {
                ClearSpill(cell, previous, except: desired);
            }

            _blocked.Remove(cell);
            cell.BlockedSpill = null;
            cell.SpillRange = desired;
            WriteArray(cell, desired, array, spilled: true);
            cell.Value = Normalize(array[0, 0]);
            AddAnchor(cell);
            NoteValueChanged(cell, oldValue, oldSpill, desired);
            return;
        }

        var value = result.Kind == OperandKind.Array ? result.Array.Values[0] : result.Kind == OperandKind.Value ? result.Value : Evaluator.ToScalar(result, new EvaluationContext(this, cell.Sheet, cell.Row, cell.Column, false));
        cell.ArrayResult = null;
        if (oldSpill is { } old)
        {
            ClearSpill(cell, old, except: null);
            cell.SpillRange = null;
        }

        if (cell.BlockedSpill is not null)
        {
            cell.BlockedSpill = null;
            _blocked.Remove(cell);
        }

        cell.Value = Normalize(value);
        NoteValueChanged(cell, oldValue, oldSpill, null);
    }

    /// <summary>A formula never shows "blank": a reference to an empty cell gives 0.</summary>
    private static CellValue Normalize(CellValue value) => value.IsBlank ? CellValue.Zero : value;

    private void Block(FormulaCell cell, CellRange? desired)
    {
        if (cell.SpillRange is { } previous)
        {
            ClearSpill(cell, previous, except: null);
            cell.SpillRange = null;
        }

        cell.Value = CellValue.FromError(CellError.Spill);
        cell.BlockedSpill = desired ?? CellRange.Cell(cell.Row, cell.Column);
        _blocked.Add(cell);
        AddAnchor(cell);
    }

    private bool IsBlocked(FormulaCell cell, CellRange desired)
    {
        foreach (var (row, column, record) in cell.Sheet.Store.Enumerate(desired))
        {
            if (row == cell.Row && column == cell.Column)
            {
                continue;
            }

            if (record.Kind == CellValueKind.Blank && record.Payload is null)
            {
                continue;
            }

            var ownSpill = (record.Flags & CellFlags.Spilled) != 0 && cell.SpillRange is { } own && own.Contains(row, column);
            if (!ownSpill)
            {
                return true;
            }
        }

        foreach (var merge in cell.Sheet.Merges.Intersecting(desired))
        {
            _ = merge;
            return true;
        }

        return false;
    }

    private void WriteArray(FormulaCell cell, CellRange range, ArrayValue array, bool spilled)
    {
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                if (row == cell.Row && column == cell.Column)
                {
                    continue;
                }

                var value = Normalize(array.Broadcast(row - range.FirstRow, column - range.FirstColumn));
                cell.Sheet.SetSpilledValue(row, column, value, spilled);
            }
        }
    }

    private void ClearSpill(FormulaCell cell, CellRange range, CellRange? except)
    {
        var cleared = false;
        foreach (var (row, column, record) in cell.Sheet.Store.Enumerate(range).ToList())
        {
            if ((row == cell.Row && column == cell.Column) || (except is { } keep && keep.Contains(row, column)))
            {
                continue;
            }

            if ((record.Flags & CellFlags.Spilled) != 0)
            {
                cell.Sheet.SetSpilledValue(row, column, CellValue.Blank, false);
                cleared = true;
            }
        }

        if (cleared)
        {
            Invalidate(cell.Sheet, range);
            WakeBlocked(cell.Sheet, range);
        }

        _workbook.NoteChanged(cell.Sheet, range);
    }

    private void NoteValueChanged(FormulaCell cell, CellValue oldValue, CellRange? oldSpill, CellRange? newSpill)
    {
        _workbook.NoteChanged(cell.Sheet, CellRange.Cell(cell.Row, cell.Column));
        if (newSpill is { } spill)
        {
            _workbook.NoteChanged(cell.Sheet, spill);
            if (oldSpill != newSpill)
            {
                Invalidate(cell.Sheet, spill, except: cell);
            }
            else
            {
                InvalidateClean(cell.Sheet, spill, cell);
            }
        }

        _ = oldValue;
    }

    /// <summary>Spilled values changed after their readers may already be clean: mark those readers dirty again.</summary>
    private void InvalidateClean(Worksheet sheet, CellRange range, FormulaCell anchor)
    {
        _graph.ForEachListener(sheet, range, listener =>
        {
            if (listener != anchor && listener.State == FormulaState.Clean)
            {
                MarkDirty(listener);
            }
        });
    }

    private void AddAnchor(FormulaCell cell)
    {
        var anchors = IndexOf(cell.Sheet).Anchors;
        if (!anchors.Contains(cell))
        {
            anchors.Add(cell);
        }
    }

    private void WakeBlocked(Worksheet sheet, CellRange range)
    {
        foreach (var blocked in _blocked.ToArray())
        {
            if (blocked.Sheet == sheet && blocked.BlockedSpill is { } wanted && wanted.Intersects(range))
            {
                MarkDirty(blocked);
            }
        }
    }

    // ---------------------------------------------------------------- reading values during evaluation

    internal CellValue GetValue(Worksheet sheet, int row, int column)
    {
        if (!sheet.Store.TryGet(row, column, out var record))
        {
            return CellValue.Blank;
        }

        if (record.Payload is FormulaCell formula)
        {
            EnsureComputed(formula);
            return formula.Value;
        }

        if ((record.Flags & CellFlags.Spilled) != 0 && FindAnchor(sheet, row, column) is { State: not FormulaState.Clean } anchor)
        {
            EnsureComputed(anchor);
            return sheet.GetValue(row, column);
        }

        return record.Value;
    }

    private void EnsureComputed(FormulaCell formula)
    {
        if (formula.State == FormulaState.Clean || formula.EngineIndex < 0)
        {
            return;
        }

        if (formula.State == FormulaState.Computing)
        {
            formula.IsCircular = true;
            _circular.Add(formula);
            return;
        }

        RuntimeHelpers.EnsureSufficientExecutionStack();
        Compute(formula);
    }

    /// <summary>Computes the dirty formulas and spill anchors of an area before it is read.</summary>
    private void PrepareArea(SheetArea area)
    {
        var index = IndexOf(area.Sheet);
        List<FormulaCell>? pending = null;
        index.ForEachIn(area.Range, cell =>
        {
            if (cell.State == FormulaState.Dirty)
            {
                (pending ??= []).Add(cell);
            }
        });
        foreach (var anchor in index.Anchors)
        {
            if (anchor.State == FormulaState.Dirty && anchor.SpillRange is { } spill && spill.Intersects(area.Range))
            {
                (pending ??= []).Add(anchor);
            }
        }

        if (pending is null)
        {
            return;
        }

        foreach (var cell in pending)
        {
            EnsureComputed(cell);
        }
    }

    internal Operand Materialize(SheetArea area)
    {
        PrepareArea(area);
        var range = area.Range;
        var array = new ArrayValue(range.RowCount, range.ColumnCount);
        foreach (var (row, column, record) in area.Sheet.Store.Enumerate(range))
        {
            array[row - range.FirstRow, column - range.FirstColumn] = record.Value;
        }

        return array;
    }

    /// <summary>The stored, non-blank values of an area, row-major (blank cells are not visited).</summary>
    internal IEnumerable<(int Row, int Column, CellValue Value)> EnumerateValues(SheetArea area)
    {
        PrepareArea(area);
        foreach (var (row, column, record) in area.Sheet.Store.Enumerate(area.Range))
        {
            var value = record.Value;
            if (!value.IsBlank || record.Payload is FormulaCell)
            {
                yield return (row, column, value);
            }
        }
    }

    internal IReadOnlyList<Worksheet> SheetsOf(SheetReference reference)
    {
        if (reference.IsMissing)
        {
            return [];
        }

        var first = _workbook.GetSheetById(reference.FirstSheetId);
        var last = _workbook.GetSheetById(reference.LastSheetId);
        if (first is null || last is null)
        {
            return [];
        }

        if (first == last)
        {
            return [first];
        }

        var start = _workbook.IndexOf(first);
        var end = _workbook.IndexOf(last);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        return _workbook.Sheets.Skip(start).Take(end - start + 1).ToList();
    }

    internal FormulaNode? ParsedName(DefinedName name) => NameFormula(name)?.Root;

    private Formula? NameFormula(DefinedName name)
    {
        if (_names.TryGetValue(name, out var formula))
        {
            return formula;
        }

        try
        {
            formula = FormulaParser.Parse(name.Formula, _workbook, null, default);
        }
        catch (FormulaParseException)
        {
            formula = null;
        }

        _names[name] = formula;
        return formula;
    }

    /// <summary>The anchor whose spill or array range covers the cell (not the anchor itself), or null.</summary>
    internal FormulaCell? FindAnchor(Worksheet sheet, int row, int column)
    {
        foreach (var anchor in IndexOf(sheet).Anchors)
        {
            var range = anchor.SpillRange ?? anchor.ArrayRange;
            if (range is { } covered && covered.Contains(row, column))
            {
                return anchor;
            }
        }

        return null;
    }

    /// <summary>The array and spill anchors of a sheet whose ranges meet <paramref name="range"/>.</summary>
    internal IEnumerable<FormulaCell> AnchorsIn(Worksheet sheet, CellRange range) =>
        IndexOf(sheet).Anchors.Where(anchor => (anchor.ArrayRange ?? anchor.SpillRange) is { } covered && covered.Intersects(range)).ToList();

    // ---------------------------------------------------------------- changes from the model

    internal void OnFormulaAdded(FormulaCell formula)
    {
        Add(formula);
        MarkDirty(formula);
        Invalidate(formula.Sheet, formula.Row, formula.Column);
        CheckSpillsAt(formula.Sheet, formula.Row, formula.Column, formula);
    }

    internal void OnFormulaRemoved(FormulaCell formula)
    {
        if (formula.EngineIndex < 0)
        {
            return;
        }

        Unregister(formula);
        var last = _formulas[^1];
        _formulas[formula.EngineIndex] = last;
        last.EngineIndex = formula.EngineIndex;
        _formulas.RemoveAt(_formulas.Count - 1);
        formula.EngineIndex = -1;
        _volatile.Remove(formula);
        _blocked.Remove(formula);
        _circular.Remove(formula);
        IndexOf(formula.Sheet).Remove(formula);
        if (formula.SpillRange is { } spill)
        {
            formula.SpillRange = null;
            ClearSpill(formula, spill, except: null);
        }

        if (formula.ArrayRange is { } array)
        {
            ClearSpill(formula, array, except: null);
        }

        formula.State = FormulaState.Clean;
    }

    internal void OnCellChanged(Worksheet sheet, int row, int column)
    {
        Invalidate(sheet, row, column);
        CheckSpillsAt(sheet, row, column, null);
    }

    internal void OnSheetsChanged(string? name)
    {
        _ = name;
        if (_threeDReferences > 0)
        {
            Rebuild();
        }
    }

    /// <summary>A cell got content: a spill over it is blocked now; a blocked spill there may be free now.</summary>
    private void CheckSpillsAt(Worksheet sheet, int row, int column, FormulaCell? self)
    {
        foreach (var anchor in IndexOf(sheet).Anchors.ToArray())
        {
            if (anchor == self)
            {
                continue;
            }

            if ((anchor.SpillRange is { } spill && spill.Contains(row, column)) || (anchor.BlockedSpill is { } wanted && wanted.Contains(row, column)))
            {
                MarkDirty(anchor);
                Invalidate(anchor.Sheet, anchor.Row, anchor.Column);
            }
        }
    }

    private void Add(FormulaCell formula, bool markDirty = true)
    {
        formula.EngineIndex = _formulas.Count;
        _formulas.Add(formula);
        IndexOf(formula.Sheet).Add(formula);
        if (formula.Formula.IsVolatile)
        {
            _volatile.Add(formula);
        }

        if (formula.ArrayRange is not null || formula.SpillRange is not null)
        {
            AddAnchor(formula);
        }

        ForEachPrecedentArea(formula, (sheet, range) => _graph.Add(sheet, range, formula));
        foreach (var precedent in formula.Formula.Precedents)
        {
            if (precedent is ReferenceNode { Sheet.Is3D: true })
            {
                _threeDReferences++;
            }
        }

        if (markDirty || formula.Formula.IsVolatile)
        {
            formula.State = FormulaState.Dirty;
            _dirty.Add(formula);
        }
        else if (formula.SpillRange is not null || formula.ArrayRange is not null)
        {
            AddAnchor(formula);
        }
    }

    private void Unregister(FormulaCell formula)
    {
        ForEachPrecedentArea(formula, (sheet, range) => _graph.Remove(sheet, range, formula));
        foreach (var precedent in formula.Formula.Precedents)
        {
            if (precedent is ReferenceNode { Sheet.Is3D: true })
            {
                _threeDReferences--;
            }
        }
    }

    private void MarkDirty(FormulaCell formula)
    {
        if (formula.State != FormulaState.Clean || formula.EngineIndex < 0)
        {
            return;
        }

        formula.State = FormulaState.Dirty;
        _dirty.Add(formula);
        Invalidate(formula.Sheet, formula.Row, formula.Column);
        if (formula.SpillRange is { } spill)
        {
            Invalidate(formula.Sheet, spill, except: formula);
        }
    }

    /// <summary>Marks the readers of a cell dirty, transitively (iterative).</summary>
    private void Invalidate(Worksheet sheet, int row, int column)
    {
        var stack = new Stack<FormulaCell>();
        _graph.ForEachListener(sheet, row, column, listener => PushDirty(listener, stack));
        Drain(stack);
    }

    private void Invalidate(Worksheet sheet, CellRange range, FormulaCell? except = null)
    {
        var stack = new Stack<FormulaCell>();
        _graph.ForEachListener(sheet, range, listener =>
        {
            if (listener != except)
            {
                PushDirty(listener, stack);
            }
        });
        Drain(stack);
    }

    private void Drain(Stack<FormulaCell> stack)
    {
        while (stack.Count > 0)
        {
            var formula = stack.Pop();
            _graph.ForEachListener(formula.Sheet, formula.Row, formula.Column, listener => PushDirty(listener, stack));
            if (formula.SpillRange is { } spill)
            {
                _graph.ForEachListener(formula.Sheet, spill, listener =>
                {
                    if (listener != formula)
                    {
                        PushDirty(listener, stack);
                    }
                });
            }
        }
    }

    private void PushDirty(FormulaCell formula, Stack<FormulaCell> stack)
    {
        if (formula.State != FormulaState.Clean || formula.EngineIndex < 0)
        {
            return;
        }

        formula.State = FormulaState.Dirty;
        _dirty.Add(formula);
        stack.Push(formula);
    }

    /// <summary>The cells a formula reads statically, as (sheet, range): references, names (recursively) and tables.</summary>
    private void ForEachPrecedentArea(FormulaCell formula, Action<Worksheet, CellRange> visit) =>
        VisitPrecedents(formula.Formula, formula.Sheet, formula.Address, visit, 0);

    private void VisitPrecedents(Formula formula, Worksheet sheet, CellAddress origin, Action<Worksheet, CellRange> visit, int depth)
    {
        foreach (var node in formula.Precedents)
        {
            switch (node)
            {
                case ReferenceNode reference:
                    if (reference.Area.Resolve(origin) is not { } range)
                    {
                        break;
                    }

                    if (reference.Sheet is null)
                    {
                        visit(sheet, range);
                        break;
                    }

                    foreach (var target in SheetsOf(reference.Sheet))
                    {
                        visit(target, range);
                    }

                    break;
                case NameNode name when depth < 16:
                    var scope = name.SheetId is { } id ? _workbook.GetSheetById(id) : sheet;
                    var defined = _workbook.FindName(name.Name, scope);
                    if (defined is not null && NameFormula(defined) is { } nameFormula)
                    {
                        VisitPrecedents(nameFormula, sheet, origin, visit, depth + 1);
                    }
                    else if (StructuredReferences.FindTable(_workbook, name.Name) is { } table)
                    {
                        visit(table.Sheet, table.Table.Range);
                    }

                    break;
                case StructuredReferenceNode structured:
                    var found = structured.TableName is { } tableName
                        ? StructuredReferences.FindTable(_workbook, tableName)
                        : sheet.Tables.Where(candidate => candidate.Range.Contains(origin)).Select(candidate => ((Worksheet, TableDefinition)?)(sheet, candidate)).FirstOrDefault();
                    if (found is { } tableFound)
                    {
                        // Only the cells it names: a total row or a [@Spalte] formula inside the table is no circle.
                        visit(tableFound.Item1, StructuredReferences.ResolveRange(tableFound.Item2, structured.Specifier, origin.Row) ?? tableFound.Item2.Range);
                    }

                    break;
            }
        }
    }

    private SheetFormulaIndex IndexOf(Worksheet sheet)
    {
        if (!_indexes.TryGetValue(sheet.Id, out var index))
        {
            index = new SheetFormulaIndex();
            _indexes[sheet.Id] = index;
        }

        return index;
    }

    private sealed class Frame(FormulaCell cell)
    {
        public FormulaCell Cell { get; } = cell;

        public List<FormulaCell>? Pending { get; set; }

        public int Next { get; set; }
    }
}
