using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>
/// Who reads what: single cells map to the formulas reading them (a dictionary keyed by sheet/row/column), ranges are
/// shared nodes (identical ranges of many formulas are stored once) found through a coarse bucket grid – ranges covering
/// many buckets (whole columns, big blocks) sit in a short list checked directly. So a SUM over A:A of 100k formulas costs
/// one node, and a change finds its readers without scanning every formula.
/// </summary>
internal sealed class DependencyGraph
{
    private readonly Dictionary<long, object> _cellListeners = [];
    private readonly Dictionary<int, RangeIndex> _ranges = [];

    public static long Key(int sheetId, int row, int column) => ((long)sheetId << 34) | ((long)row << 14) | (uint)column;

    public void Clear()
    {
        _cellListeners.Clear();
        _ranges.Clear();
    }

    public void Add(Worksheet sheet, CellRange range, FormulaCell formula)
    {
        if (range.IsSingleCell)
        {
            var key = Key(sheet.Id, range.FirstRow, range.FirstColumn);
            if (!_cellListeners.TryGetValue(key, out var existing))
            {
                _cellListeners[key] = formula;
            }
            else if (existing is FormulaCell single)
            {
                if (single != formula)
                {
                    _cellListeners[key] = new List<FormulaCell>(2) { single, formula };
                }
            }
            else
            {
                ((List<FormulaCell>)existing).Add(formula);
            }

            return;
        }

        if (!_ranges.TryGetValue(sheet.Id, out var index))
        {
            index = new RangeIndex();
            _ranges[sheet.Id] = index;
        }

        index.Add(range, formula);
    }

    public void Remove(Worksheet sheet, CellRange range, FormulaCell formula)
    {
        if (range.IsSingleCell)
        {
            var key = Key(sheet.Id, range.FirstRow, range.FirstColumn);
            if (!_cellListeners.TryGetValue(key, out var existing))
            {
                return;
            }

            if (existing == formula)
            {
                _cellListeners.Remove(key);
            }
            else if (existing is List<FormulaCell> list)
            {
                list.Remove(formula);
                if (list.Count == 1)
                {
                    _cellListeners[key] = list[0];
                }
            }

            return;
        }

        if (_ranges.TryGetValue(sheet.Id, out var index))
        {
            index.Remove(range, formula);
        }
    }

    /// <summary>Every formula that reads the cell (directly or through a range).</summary>
    public void ForEachListener(Worksheet sheet, int row, int column, Action<FormulaCell> action)
    {
        if (_cellListeners.TryGetValue(Key(sheet.Id, row, column), out var listeners))
        {
            if (listeners is FormulaCell single)
            {
                action(single);
            }
            else
            {
                foreach (var formula in ((List<FormulaCell>)listeners).ToArray())
                {
                    action(formula);
                }
            }
        }

        if (_ranges.TryGetValue(sheet.Id, out var index))
        {
            index.ForEachContaining(row, column, action);
        }
    }

    /// <summary>Every formula that reads any cell of <paramref name="range"/>.</summary>
    public void ForEachListener(Worksheet sheet, CellRange range, Action<FormulaCell> action)
    {
        if (range.CellCount <= 4096)
        {
            for (var row = range.FirstRow; row <= range.LastRow; row++)
            {
                for (var column = range.FirstColumn; column <= range.LastColumn; column++)
                {
                    if (_cellListeners.TryGetValue(Key(sheet.Id, row, column), out var listeners))
                    {
                        Visit(listeners, action);
                    }
                }
            }
        }
        else
        {
            foreach (var (key, listeners) in _cellListeners.ToArray())
            {
                var sheetId = (int)(key >> 34);
                var row = (int)((key >> 14) & 0xFFFFF);
                var column = (int)(key & 0x3FFF);
                if (sheetId == sheet.Id && range.Contains(row, column))
                {
                    Visit(listeners, action);
                }
            }
        }

        if (_ranges.TryGetValue(sheet.Id, out var index))
        {
            index.ForEachIntersecting(range, action);
        }
    }

    public int CellListenerCount => _cellListeners.Count;

    public int RangeNodeCount => _ranges.Values.Sum(index => index.Count);

    private static void Visit(object listeners, Action<FormulaCell> action)
    {
        if (listeners is FormulaCell single)
        {
            action(single);
            return;
        }

        foreach (var formula in ((List<FormulaCell>)listeners).ToArray())
        {
            action(formula);
        }
    }

    private sealed class RangeNode(CellRange range)
    {
        public CellRange Range { get; } = range;

        public List<FormulaCell> Listeners { get; } = [];
    }

    /// <summary>The range nodes of one sheet with a bucket grid of 256 rows × 32 columns.</summary>
    private sealed class RangeIndex
    {
        private const int RowShift = 8;
        private const int ColumnShift = 5;
        private const int MaxBuckets = 64;

        private readonly Dictionary<CellRange, RangeNode> _nodes = [];
        private readonly Dictionary<long, List<RangeNode>> _buckets = [];
        private readonly List<RangeNode> _large = [];

        public int Count => _nodes.Count;

        public void Add(CellRange range, FormulaCell formula)
        {
            if (!_nodes.TryGetValue(range, out var node))
            {
                node = new RangeNode(range);
                _nodes[range] = node;
                if (BucketCount(range) > MaxBuckets)
                {
                    _large.Add(node);
                }
                else
                {
                    foreach (var bucket in Buckets(range))
                    {
                        if (!_buckets.TryGetValue(bucket, out var list))
                        {
                            list = [];
                            _buckets[bucket] = list;
                        }

                        list.Add(node);
                    }
                }
            }

            node.Listeners.Add(formula);
        }

        public void Remove(CellRange range, FormulaCell formula)
        {
            if (!_nodes.TryGetValue(range, out var node))
            {
                return;
            }

            node.Listeners.Remove(formula);
            if (node.Listeners.Count > 0)
            {
                return;
            }

            _nodes.Remove(range);
            if (BucketCount(range) > MaxBuckets)
            {
                _large.Remove(node);
                return;
            }

            foreach (var bucket in Buckets(range))
            {
                if (_buckets.TryGetValue(bucket, out var list))
                {
                    list.Remove(node);
                    if (list.Count == 0)
                    {
                        _buckets.Remove(bucket);
                    }
                }
            }
        }

        public void ForEachContaining(int row, int column, Action<FormulaCell> action)
        {
            if (_buckets.TryGetValue(BucketKey(row >> RowShift, column >> ColumnShift), out var list))
            {
                foreach (var node in list.ToArray())
                {
                    if (node.Range.Contains(row, column))
                    {
                        VisitNode(node, action);
                    }
                }
            }

            foreach (var node in _large.ToArray())
            {
                if (node.Range.Contains(row, column))
                {
                    VisitNode(node, action);
                }
            }
        }

        public void ForEachIntersecting(CellRange range, Action<FormulaCell> action)
        {
            foreach (var node in _nodes.Values.ToArray())
            {
                if (node.Range.Intersects(range))
                {
                    VisitNode(node, action);
                }
            }
        }

        private static void VisitNode(RangeNode node, Action<FormulaCell> action)
        {
            foreach (var formula in node.Listeners.ToArray())
            {
                action(formula);
            }
        }

        private static long BucketCount(CellRange range) =>
            (long)((range.LastRow >> RowShift) - (range.FirstRow >> RowShift) + 1) * ((range.LastColumn >> ColumnShift) - (range.FirstColumn >> ColumnShift) + 1);

        private static IEnumerable<long> Buckets(CellRange range)
        {
            for (var row = range.FirstRow >> RowShift; row <= range.LastRow >> RowShift; row++)
            {
                for (var column = range.FirstColumn >> ColumnShift; column <= range.LastColumn >> ColumnShift; column++)
                {
                    yield return BucketKey(row, column);
                }
            }
        }

        private static long BucketKey(int rowBucket, int columnBucket) => ((long)rowBucket << 16) | (uint)columnBucket;
    }
}

/// <summary>The formula cells of one sheet by column (sorted by row on demand) and the array anchors.</summary>
internal sealed class SheetFormulaIndex
{
    private readonly Dictionary<int, List<FormulaCell>> _byColumn = [];
    private readonly HashSet<int> _unsorted = [];

    public List<FormulaCell> Anchors { get; } = [];

    public void Add(FormulaCell formula)
    {
        if (!_byColumn.TryGetValue(formula.Column, out var list))
        {
            list = [];
            _byColumn[formula.Column] = list;
        }

        if (list.Count > 0 && list[^1].Row > formula.Row)
        {
            _unsorted.Add(formula.Column);
        }

        list.Add(formula);
    }

    public void Remove(FormulaCell formula)
    {
        if (_byColumn.TryGetValue(formula.Column, out var list))
        {
            list.Remove(formula);
            if (list.Count == 0)
            {
                _byColumn.Remove(formula.Column);
            }
        }

        Anchors.Remove(formula);
    }

    public void Clear()
    {
        _byColumn.Clear();
        _unsorted.Clear();
        Anchors.Clear();
    }

    /// <summary>The formula cells inside <paramref name="range"/>.</summary>
    public void ForEachIn(CellRange range, Action<FormulaCell> action)
    {
        if (range.ColumnCount <= _byColumn.Count)
        {
            for (var column = range.FirstColumn; column <= range.LastColumn; column++)
            {
                if (_byColumn.TryGetValue(column, out var list))
                {
                    VisitRows(column, list, range, action);
                }
            }

            return;
        }

        foreach (var (column, list) in _byColumn.ToArray())
        {
            if (column >= range.FirstColumn && column <= range.LastColumn)
            {
                VisitRows(column, list, range, action);
            }
        }
    }

    private void VisitRows(int column, List<FormulaCell> list, CellRange range, Action<FormulaCell> action)
    {
        if (_unsorted.Remove(column))
        {
            list.Sort((first, second) => first.Row.CompareTo(second.Row));
        }

        var low = 0;
        var high = list.Count;
        while (low < high)
        {
            var middle = (low + high) / 2;
            if (list[middle].Row < range.FirstRow)
            {
                low = middle + 1;
            }
            else
            {
                high = middle;
            }
        }

        var snapshot = list;
        for (var index = low; index < snapshot.Count && snapshot[index].Row <= range.LastRow; index++)
        {
            action(snapshot[index]);
        }
    }
}
