using ThixCalc.Core.Calculation;

namespace ThixCalc.Core.Model;

[Flags]
internal enum CellFlags : byte
{
    None = 0,

    /// <summary>The value belongs to a dynamic array that spilled into this cell (not the cell's own content).</summary>
    Spilled = 1,
}

/// <summary>
/// One stored cell, 24 bytes: the value (<see cref="Kind"/> + <see cref="Number"/>; a text in <see cref="Payload"/>), or a
/// formula (<see cref="Payload"/> is its <see cref="FormulaCell"/>, which holds the cached value), plus the style id.
/// </summary>
internal struct CellRecord
{
    public double Number;
    public object? Payload;
    public int StyleId;
    public CellValueKind Kind;
    public CellFlags Flags;

    public readonly bool IsEmpty => Kind == CellValueKind.Blank && Payload is null && StyleId == 0;

    public readonly FormulaCell? Formula => Payload as FormulaCell;

    /// <summary>The value (a formula's cached result).</summary>
    public readonly CellValue Value => Payload switch
    {
        FormulaCell formula => formula.Value,
        string text => CellValue.FromText(text),
        _ => Kind switch
        {
            CellValueKind.Number => CellValue.FromNumber(Number),
            CellValueKind.Boolean => CellValue.FromBoolean(Number != 0),
            CellValueKind.Error => CellValue.FromError((CellError)(int)Number),
            _ => CellValue.Blank,
        },
    };

    public void SetValue(CellValue value)
    {
        Payload = value.IsText ? value.Text : null;
        Kind = value.Kind;
        Number = value.Kind switch
        {
            CellValueKind.Number or CellValueKind.Boolean => value.Number,
            CellValueKind.Error => (int)value.Error,
            _ => 0,
        };
    }

    public static CellRecord FromValue(CellValue value, int styleId)
    {
        var record = new CellRecord { StyleId = styleId };
        record.SetValue(value);
        return record;
    }
}

/// <summary>The stored cells of one row: column indices ascending with their records.</summary>
internal sealed class RowData
{
    private int[] _columns = new int[4];
    private CellRecord[] _cells = new CellRecord[4];

    public int Count { get; private set; }

    public int ColumnAt(int index) => _columns[index];

    public ref CellRecord CellAt(int index) => ref _cells[index];

    /// <summary>The index of <paramref name="column"/>, or the bitwise complement of its insertion point.</summary>
    public int IndexOf(int column) => Array.BinarySearch(_columns, 0, Count, column);

    /// <summary>The index of the first column ≥ <paramref name="column"/>.</summary>
    public int LowerBound(int column)
    {
        var index = IndexOf(column);
        return index >= 0 ? index : ~index;
    }

    public void Set(int column, in CellRecord record)
    {
        var index = IndexOf(column);
        if (index >= 0)
        {
            _cells[index] = record;
            return;
        }

        InsertAt(~index, column, record);
    }

    public void RemoveAt(int index)
    {
        Count--;
        Array.Copy(_columns, index + 1, _columns, index, Count - index);
        Array.Copy(_cells, index + 1, _cells, index, Count - index);
        _cells[Count] = default;
    }

    /// <summary>Adds columns ≥ <paramref name="at"/> + <paramref name="count"/> by shifting (negative count deletes).</summary>
    public void ShiftColumns(int at, int count)
    {
        if (count < 0)
        {
            var end = at - count;
            var first = LowerBound(at);
            var last = LowerBound(end);
            var removed = last - first;
            if (removed > 0)
            {
                Array.Copy(_columns, last, _columns, first, Count - last);
                Array.Copy(_cells, last, _cells, first, Count - last);
                Array.Clear(_cells, Count - removed, removed);
                Count -= removed;
            }

            for (var index = first; index < Count; index++)
            {
                _columns[index] += count;
            }

            return;
        }

        var start = LowerBound(at);
        for (var index = start; index < Count; index++)
        {
            _columns[index] += count;
        }

        while (Count > 0 && _columns[Count - 1] > SheetLimits.LastColumn)
        {
            Count--;
            _cells[Count] = default;
        }
    }

    private void InsertAt(int index, int column, in CellRecord record)
    {
        if (Count == _columns.Length)
        {
            var size = _columns.Length * 2;
            Array.Resize(ref _columns, size);
            Array.Resize(ref _cells, size);
        }

        if (index < Count)
        {
            Array.Copy(_columns, index, _columns, index + 1, Count - index);
            Array.Copy(_cells, index, _cells, index + 1, Count - index);
        }

        _columns[index] = column;
        _cells[index] = record;
        Count++;
    }
}

/// <summary>
/// The sparse cells of a worksheet: the rows that hold cells, sorted by row index (arrays + binary search), each with its
/// sorted columns. Appending in row order (loading a file) is O(1); a lookup is two binary searches; enumeration of a range
/// is row-major and touches only stored cells, so a whole column (A:A) costs what it holds, not 1,048,576 cells.
/// Empty records (blank, no formula, style 0) are never stored.
/// </summary>
internal sealed class CellStore
{
    private int[] _rowKeys = new int[16];
    private RowData[] _rows = new RowData[16];
    private int _rowCount;

    public int RowCount => _rowCount;

    public long CellCount
    {
        get
        {
            long count = 0;
            for (var index = 0; index < _rowCount; index++)
            {
                count += _rows[index].Count;
            }

            return count;
        }
    }

    public int RowKeyAt(int index) => _rowKeys[index];

    public RowData RowAt(int index) => _rows[index];

    /// <summary>The index of the first stored row ≥ <paramref name="row"/>.</summary>
    public int RowLowerBound(int row)
    {
        var index = Array.BinarySearch(_rowKeys, 0, _rowCount, row);
        return index >= 0 ? index : ~index;
    }

    public RowData? GetRow(int row)
    {
        if (_rowCount > 0 && _rowKeys[_rowCount - 1] == row)
        {
            return _rows[_rowCount - 1];
        }

        var index = Array.BinarySearch(_rowKeys, 0, _rowCount, row);
        return index >= 0 ? _rows[index] : null;
    }

    public bool TryGet(int row, int column, out CellRecord record)
    {
        var data = GetRow(row);
        if (data is not null)
        {
            var index = data.IndexOf(column);
            if (index >= 0)
            {
                record = data.CellAt(index);
                return true;
            }
        }

        record = default;
        return false;
    }

    public CellRecord Get(int row, int column) => TryGet(row, column, out var record) ? record : default;

    /// <summary>Stores <paramref name="record"/> (removes the cell when the record is empty).</summary>
    public void Set(int row, int column, in CellRecord record)
    {
        if (record.IsEmpty)
        {
            Remove(row, column);
            return;
        }

        GetOrAddRow(row).Set(column, record);
    }

    public void Remove(int row, int column)
    {
        var rowIndex = Array.BinarySearch(_rowKeys, 0, _rowCount, row);
        if (rowIndex < 0)
        {
            return;
        }

        var data = _rows[rowIndex];
        var index = data.IndexOf(column);
        if (index < 0)
        {
            return;
        }

        data.RemoveAt(index);
        if (data.Count == 0)
        {
            RemoveRowAt(rowIndex);
        }
    }

    /// <summary>The cells of <paramref name="range"/> that are stored, row-major.</summary>
    public IEnumerable<(int Row, int Column, CellRecord Record)> Enumerate(CellRange range)
    {
        for (var rowIndex = RowLowerBound(range.FirstRow); rowIndex < _rowCount && _rowKeys[rowIndex] <= range.LastRow; rowIndex++)
        {
            var row = _rowKeys[rowIndex];
            var data = _rows[rowIndex];
            for (var index = data.LowerBound(range.FirstColumn); index < data.Count; index++)
            {
                var column = data.ColumnAt(index);
                if (column > range.LastColumn)
                {
                    break;
                }

                yield return (row, column, data.CellAt(index));
            }
        }
    }

    /// <summary>The smallest range holding every stored cell, or null when the sheet is empty.</summary>
    public CellRange? UsedRange()
    {
        if (_rowCount == 0)
        {
            return null;
        }

        var firstColumn = int.MaxValue;
        var lastColumn = -1;
        for (var index = 0; index < _rowCount; index++)
        {
            var data = _rows[index];
            firstColumn = Math.Min(firstColumn, data.ColumnAt(0));
            lastColumn = Math.Max(lastColumn, data.ColumnAt(data.Count - 1));
        }

        return new CellRange(_rowKeys[0], firstColumn, _rowKeys[_rowCount - 1], lastColumn);
    }

    /// <summary>Moves rows ≥ <paramref name="at"/> down by <paramref name="count"/> (cells pushed past the last row are dropped).</summary>
    public void InsertRows(int at, int count)
    {
        var start = RowLowerBound(at);
        for (var index = start; index < _rowCount; index++)
        {
            _rowKeys[index] += count;
        }

        while (_rowCount > 0 && _rowKeys[_rowCount - 1] > SheetLimits.LastRow)
        {
            _rowCount--;
            _rows[_rowCount] = null!;
        }
    }

    /// <summary>Removes rows [<paramref name="at"/>, at + count) and moves the rows below up.</summary>
    public void DeleteRows(int at, int count)
    {
        var first = RowLowerBound(at);
        var last = RowLowerBound(at + count);
        var removed = last - first;
        if (removed > 0)
        {
            Array.Copy(_rowKeys, last, _rowKeys, first, _rowCount - last);
            Array.Copy(_rows, last, _rows, first, _rowCount - last);
            Array.Clear(_rows, _rowCount - removed, removed);
            _rowCount -= removed;
        }

        for (var index = first; index < _rowCount; index++)
        {
            _rowKeys[index] -= count;
        }
    }

    /// <summary>Moves columns ≥ <paramref name="at"/> right by <paramref name="count"/> in rows [firstRow, lastRow].</summary>
    public void InsertColumns(int at, int count, int firstRow = 0, int lastRow = SheetLimits.LastRow) =>
        ShiftColumns(at, count, firstRow, lastRow);

    /// <summary>Removes columns [<paramref name="at"/>, at + count) in rows [firstRow, lastRow], moving the rest left.</summary>
    public void DeleteColumns(int at, int count, int firstRow = 0, int lastRow = SheetLimits.LastRow) =>
        ShiftColumns(at, -count, firstRow, lastRow);

    /// <summary>Moves the cells of columns [firstColumn, lastColumn] in rows ≥ <paramref name="at"/> down (cell insertion, shift down).</summary>
    public void ShiftCellsDown(int at, int count, int firstColumn, int lastColumn)
    {
        var moved = new List<(int Row, int Column, CellRecord Record)>();
        foreach (var cell in Enumerate(new CellRange(at, firstColumn, SheetLimits.LastRow, lastColumn)))
        {
            moved.Add(cell);
        }

        foreach (var (row, column, _) in moved)
        {
            Remove(row, column);
        }

        foreach (var (row, column, record) in moved)
        {
            var target = row + count;
            if (target >= at && target <= SheetLimits.LastRow)
            {
                Set(target, column, record);
            }
        }
    }

    private void ShiftColumns(int at, int count, int firstRow, int lastRow)
    {
        var rowIndex = RowLowerBound(firstRow);
        while (rowIndex < _rowCount && _rowKeys[rowIndex] <= lastRow)
        {
            var data = _rows[rowIndex];
            data.ShiftColumns(at, count);
            if (data.Count == 0)
            {
                RemoveRowAt(rowIndex);
                continue;
            }

            rowIndex++;
        }
    }

    private RowData GetOrAddRow(int row)
    {
        if (_rowCount > 0 && _rowKeys[_rowCount - 1] == row)
        {
            return _rows[_rowCount - 1];
        }

        if (_rowCount == 0 || _rowKeys[_rowCount - 1] < row)
        {
            return InsertRowAt(_rowCount, row);
        }

        var index = Array.BinarySearch(_rowKeys, 0, _rowCount, row);
        return index >= 0 ? _rows[index] : InsertRowAt(~index, row);
    }

    private RowData InsertRowAt(int index, int row)
    {
        if (_rowCount == _rowKeys.Length)
        {
            var size = _rowKeys.Length * 2;
            Array.Resize(ref _rowKeys, size);
            Array.Resize(ref _rows, size);
        }

        if (index < _rowCount)
        {
            Array.Copy(_rowKeys, index, _rowKeys, index + 1, _rowCount - index);
            Array.Copy(_rows, index, _rows, index + 1, _rowCount - index);
        }

        var data = new RowData();
        _rowKeys[index] = row;
        _rows[index] = data;
        _rowCount++;
        return data;
    }

    private void RemoveRowAt(int index)
    {
        _rowCount--;
        Array.Copy(_rowKeys, index + 1, _rowKeys, index, _rowCount - index);
        Array.Copy(_rows, index + 1, _rows, index, _rowCount - index);
        _rows[_rowCount] = null!;
    }
}
