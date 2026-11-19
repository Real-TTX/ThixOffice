namespace ThixCalc.Core.Model;

/// <summary>
/// What a row or column has of its own: a size (rows: height in points; columns: width in characters of the default font's
/// widest digit, as SpreadsheetML counts), hidden, outline level and collapsed state, a style for empty cells.
/// </summary>
public readonly record struct AxisInfo(
    double? Size = null,
    bool Hidden = false,
    int OutlineLevel = 0,
    bool Collapsed = false,
    int? StyleId = null,
    bool CustomSize = false,
    bool BestFit = false)
{
    public bool IsDefault => Size is null && !Hidden && OutlineLevel == 0 && !Collapsed && StyleId is null && !BestFit;
}

/// <summary>
/// The rows or the columns of a worksheet: a default size and the entries that differ, sorted, with lazily rebuilt prefix
/// sums so the offset of any index and the index at any offset are O(log n) even with a million custom row heights.
/// </summary>
public sealed class AxisCollection
{
    private readonly int _limit;
    private int[] _keys = new int[8];
    private AxisInfo[] _infos = new AxisInfo[8];
    private int _count;
    private double[]? _prefix;
    private double _defaultSize;

    public AxisCollection(double defaultSize, int limit)
    {
        _defaultSize = defaultSize;
        _limit = limit;
    }

    /// <summary>Raised after any change (sizes, hidden, outline).</summary>
    public event EventHandler? Changed;

    /// <summary>The number of indices (1,048,576 rows or 16,384 columns).</summary>
    public int Limit => _limit;

    public double DefaultSize
    {
        get => _defaultSize;
        set
        {
            _defaultSize = value;
            Invalidate();
        }
    }

    public int Count => _count;

    public AxisInfo this[int index]
    {
        get
        {
            var position = Array.BinarySearch(_keys, 0, _count, index);
            return position >= 0 ? _infos[position] : default;
        }

        set => Set(index, value);
    }

    /// <summary>The entries that differ from the default, ascending.</summary>
    public IEnumerable<(int Index, AxisInfo Info)> Entries
    {
        get
        {
            for (var position = 0; position < _count; position++)
            {
                yield return (_keys[position], _infos[position]);
            }
        }
    }

    /// <summary>The size an index takes (0 when hidden).</summary>
    public double SizeOf(int index)
    {
        var info = this[index];
        return info.Hidden ? 0 : info.Size ?? _defaultSize;
    }

    public bool IsHidden(int index) => this[index].Hidden;

    public void Set(int index, AxisInfo info)
    {
        var position = Array.BinarySearch(_keys, 0, _count, index);
        if (info.IsDefault)
        {
            if (position >= 0)
            {
                RemoveAt(position);
                Invalidate();
            }

            return;
        }

        if (position >= 0)
        {
            _infos[position] = info;
        }
        else
        {
            InsertAt(~position, index, info);
        }

        Invalidate();
    }

    /// <summary>Changes every index of [<paramref name="first"/>, <paramref name="last"/>].</summary>
    public void Update(int first, int last, Func<AxisInfo, AxisInfo> change)
    {
        for (var index = first; index <= last; index++)
        {
            var position = Array.BinarySearch(_keys, 0, _count, index);
            var info = change(position >= 0 ? _infos[position] : default);
            if (info.IsDefault)
            {
                if (position >= 0)
                {
                    RemoveAt(position);
                }

                continue;
            }

            if (position >= 0)
            {
                _infos[position] = info;
            }
            else
            {
                InsertAt(~position, index, info);
            }
        }

        Invalidate();
    }

    /// <summary>The distance from index 0 to the start of <paramref name="index"/>.</summary>
    public double OffsetOf(int index)
    {
        var prefix = Prefix();
        var position = Array.BinarySearch(_keys, 0, _count, index);
        var before = position >= 0 ? position : ~position;
        return index * _defaultSize + (before == 0 ? 0 : prefix[before - 1]);
    }

    /// <summary>The index whose span contains <paramref name="offset"/> (the last index for offsets past the end).</summary>
    public int IndexAt(double offset)
    {
        if (offset <= 0)
        {
            return FirstVisibleFrom(0);
        }

        var low = 0;
        var high = _limit - 1;
        while (low < high)
        {
            var middle = low + (high - low + 1) / 2;
            if (OffsetOf(middle) <= offset)
            {
                low = middle;
            }
            else
            {
                high = middle - 1;
            }
        }

        // A hidden index has no span: take the next visible one.
        return SizeOf(low) == 0 ? FirstVisibleFrom(low) : low;
    }

    /// <summary>The first index ≥ <paramref name="index"/> that is not hidden (or the limit − 1).</summary>
    public int FirstVisibleFrom(int index)
    {
        while (index < _limit - 1 && SizeOf(index) == 0)
        {
            index++;
        }

        return index;
    }

    /// <summary>The total extent of all indices.</summary>
    public double TotalSize => OffsetOf(_limit - 1) + SizeOf(_limit - 1);

    /// <summary>Moves entries ≥ <paramref name="at"/> by <paramref name="count"/> (insert); entries pushed past the limit are dropped.</summary>
    public void Insert(int at, int count)
    {
        var start = LowerBound(at);
        for (var position = start; position < _count; position++)
        {
            _keys[position] += count;
        }

        while (_count > 0 && _keys[_count - 1] >= _limit)
        {
            _count--;
        }

        Invalidate();
    }

    /// <summary>Removes entries in [<paramref name="at"/>, at + count) and moves the following ones back.</summary>
    public void Delete(int at, int count)
    {
        var first = LowerBound(at);
        var last = LowerBound(at + count);
        var removed = last - first;
        if (removed > 0)
        {
            Array.Copy(_keys, last, _keys, first, _count - last);
            Array.Copy(_infos, last, _infos, first, _count - last);
            _count -= removed;
        }

        for (var position = first; position < _count; position++)
        {
            _keys[position] -= count;
        }

        Invalidate();
    }

    /// <summary>A copy of the entries (undo of structural edits).</summary>
    public (int Index, AxisInfo Info)[] Snapshot() => Entries.ToArray();

    public void Restore((int Index, AxisInfo Info)[] entries, double defaultSize)
    {
        _count = 0;
        _defaultSize = defaultSize;
        foreach (var (index, info) in entries)
        {
            InsertAt(_count, index, info);
        }

        Invalidate();
    }

    private int LowerBound(int index)
    {
        var position = Array.BinarySearch(_keys, 0, _count, index);
        return position >= 0 ? position : ~position;
    }

    private double[] Prefix()
    {
        if (_prefix is not null)
        {
            return _prefix;
        }

        var prefix = new double[_count];
        var sum = 0.0;
        for (var position = 0; position < _count; position++)
        {
            var info = _infos[position];
            sum += (info.Hidden ? 0 : info.Size ?? _defaultSize) - _defaultSize;
            prefix[position] = sum;
        }

        _prefix = prefix;
        return prefix;
    }

    private void Invalidate()
    {
        _prefix = null;
        Changed?.Invoke(this, EventArgs.Empty);
    }

    private void InsertAt(int position, int index, AxisInfo info)
    {
        if (_count == _keys.Length)
        {
            Array.Resize(ref _keys, _keys.Length * 2);
            Array.Resize(ref _infos, _infos.Length * 2);
        }

        Array.Copy(_keys, position, _keys, position + 1, _count - position);
        Array.Copy(_infos, position, _infos, position + 1, _count - position);
        _keys[position] = index;
        _infos[position] = info;
        _count++;
    }

    private void RemoveAt(int position)
    {
        _count--;
        Array.Copy(_keys, position + 1, _keys, position, _count - position);
        Array.Copy(_infos, position + 1, _infos, position, _count - position);
    }
}
