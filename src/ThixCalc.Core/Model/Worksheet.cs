using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;

namespace ThixCalc.Core.Model;

/// <summary>
/// What a cell holds, as a snapshot: its value (a formula's cached result), the formula (null for a constant), the style id,
/// and for array formulas the range of a legacy array formula or whether the formula is a dynamic array. A cell inside a
/// spill or array range that is not the anchor is <see cref="IsSpilled"/> (its value belongs to the anchor's formula).
/// </summary>
public readonly record struct Cell(
    CellValue Value,
    Formula? Formula = null,
    int StyleId = 0,
    CellRange? ArrayRange = null,
    bool IsDynamicArray = false,
    bool IsSpilled = false)
{
    public static readonly Cell Empty = default;

    public bool HasFormula => Formula is not null;

    /// <summary>No value, no formula, no style of its own.</summary>
    public bool IsEmpty => Value.IsBlank && Formula is null && StyleId == 0;
}

/// <summary>A stored cell with its position (enumeration).</summary>
public readonly record struct CellEntry(int Row, int Column, Cell Cell)
{
    public CellAddress Address => new(Row, Column);
}

/// <summary>The merged ranges of a worksheet.</summary>
public sealed class MergedCells
{
    private readonly List<CellRange> _ranges = [];

    public IReadOnlyList<CellRange> Ranges => _ranges;

    public int Count => _ranges.Count;

    /// <summary>The merged range containing the cell, or null.</summary>
    public CellRange? Find(int row, int column)
    {
        foreach (var range in _ranges)
        {
            if (range.Contains(row, column))
            {
                return range;
            }
        }

        return null;
    }

    public IEnumerable<CellRange> Intersecting(CellRange area) => _ranges.Where(range => range.Intersects(area));

    /// <summary>Adds a merge (overlapping merges are removed first).</summary>
    public void Add(CellRange range)
    {
        _ranges.RemoveAll(existing => existing.Intersects(range));
        if (!range.IsSingleCell)
        {
            _ranges.Add(range);
        }
    }

    public void Remove(CellRange range) => _ranges.RemoveAll(existing => existing.Intersects(range));

    internal void Replace(IEnumerable<CellRange> ranges)
    {
        _ranges.Clear();
        _ranges.AddRange(ranges);
    }

    /// <summary>The range grown so that no merge is cut (selection, sorting).</summary>
    public CellRange Expand(CellRange range)
    {
        var result = range;
        bool grown;
        do
        {
            grown = false;
            foreach (var merge in _ranges)
            {
                if (merge.Intersects(result) && !result.Contains(merge))
                {
                    result = result.Union(merge);
                    grown = true;
                }
            }
        }
        while (grown);

        return result;
    }
}

/// <summary>
/// A worksheet: the sparse cells, rows and columns, merges, the view, and what the sheet carries besides (validation,
/// conditional formats, autofilter, hyperlinks, notes, tables, preserved XML). Changing cells through these methods keeps the
/// calculation engine informed (dirty marking) and the change list of the workbook; they record no undo – the editing
/// session does that.
/// </summary>
public sealed class Worksheet
{
    internal Worksheet(Workbook workbook, int id, string name)
    {
        Workbook = workbook;
        Id = id;
        Name = name;
        Rows = new AxisCollection(15, SheetLimits.MaxRows);
        Columns = new AxisCollection(8.43, SheetLimits.MaxColumns);
    }

    public Workbook Workbook { get; }

    /// <summary>A stable id (formulas refer to sheets by it; renaming and moving keep it).</summary>
    public int Id { get; }

    public string Name { get; internal set; }

    public int Index => Workbook.IndexOf(this);

    public SheetVisibility Visibility { get; set; }

    public CalcColor? TabColor { get; set; }

    public AxisCollection Rows { get; }

    public AxisCollection Columns { get; }

    public MergedCells Merges { get; } = new();

    public SheetView View { get; set; } = SheetView.Default;

    public List<DataValidation> Validations { get; } = [];

    public List<ConditionalFormat> ConditionalFormats { get; } = [];

    public AutoFilter? AutoFilter { get; set; }

    public SortState? SortState { get; set; }

    public List<Hyperlink> Hyperlinks { get; } = [];

    public Dictionary<CellAddress, CellComment> Comments { get; } = [];

    public List<TableDefinition> Tables { get; } = [];

    /// <summary>Default row height has been set explicitly (sheetFormatPr customHeight).</summary>
    public bool CustomDefaultRowHeight { get; set; }

    /// <summary>Rows hidden unless they have data (sheetFormatPr zeroHeight).</summary>
    public bool ZeroHeightRows { get; set; }

    /// <summary>The sheet's protection (&lt;sheetProtection&gt; as XML), or null when the sheet is not protected.</summary>
    public string? ProtectionXml { get; set; }

    internal CellStore Store { get; } = new();

    /// <summary>The worksheet elements the model does not own (drawing, pageSetup, tableParts, extLst …) as XML, by element name.</summary>
    internal List<(string Name, string Xml)> PreservedElements { get; } = [];

    /// <summary>The namespace declarations and attributes of the original worksheet root (mc:Ignorable …).</summary>
    internal List<(string Prefix, string Uri)> RootNamespaces { get; } = [];

    internal List<(string Prefix, string LocalName, string Namespace, string Value)> RootAttributes { get; } = [];

    /// <summary>The original &lt;sheetPr&gt; (the writer patches the tab colour into it).</summary>
    internal string? SheetPropertiesXml { get; set; }

    /// <summary>The relationship id of the notes' VML drawing in the original package.</summary>
    internal string? LegacyDrawingId { get; set; }

    /// <summary>The cells that had notes when the file was read (unchanged notes keep their VML shapes).</summary>
    internal HashSet<CellAddress> OriginalCommentAddresses { get; set; } = [];

    /// <summary>The sheetId of the file (kept on write).</summary>
    internal uint FileSheetId { get; set; }

    /// <summary>The worksheet part this sheet was read from (the writer keeps its relationships).</summary>
    internal string? PartUri { get; set; }

    /// <summary>The number of stored cells (values, formulas or styles).</summary>
    public long CellCount => Store.CellCount;

    /// <summary>The range of all stored cells, or null for an empty sheet.</summary>
    public CellRange? UsedRange => Store.UsedRange();

    /// <summary>The range of cells with a value or formula (styles alone do not count), or null.</summary>
    public CellRange? DataRange
    {
        get
        {
            CellRange? result = null;
            for (var index = 0; index < Store.RowCount; index++)
            {
                var data = Store.RowAt(index);
                var row = Store.RowKeyAt(index);
                for (var position = 0; position < data.Count; position++)
                {
                    ref var record = ref data.CellAt(position);
                    if (record.Kind == CellValueKind.Blank && record.Payload is null)
                    {
                        continue;
                    }

                    var cell = CellRange.Cell(row, data.ColumnAt(position));
                    result = result is { } existing ? existing.Union(cell) : cell;
                }
            }

            return result;
        }
    }

    // ---------------------------------------------------------------- reading

    public CellValue GetValue(int row, int column) => Store.TryGet(row, column, out var record) ? record.Value : CellValue.Blank;

    public CellValue GetValue(CellAddress address) => GetValue(address.Row, address.Column);

    public CellValue this[string address] => GetValue(CellAddress.Parse(address));

    public Cell GetCell(int row, int column) => Store.TryGet(row, column, out var record) ? ToCell(record) : Cell.Empty;

    public Cell GetCell(CellAddress address) => GetCell(address.Row, address.Column);

    public Formula? GetFormula(int row, int column) => Store.TryGet(row, column, out var record) ? record.Formula?.Formula : null;

    /// <summary>The style the cell shows: its own, else its row's, else its column's, else the default (0).</summary>
    public int GetStyleId(int row, int column)
    {
        if (Store.TryGet(row, column, out var record) && record.StyleId != 0)
        {
            return record.StyleId;
        }

        return Rows[row].StyleId ?? Columns[column].StyleId ?? 0;
    }

    public CellStyle GetStyle(int row, int column) => Workbook.Styles[GetStyleId(row, column)];

    /// <summary>The cells stored in <paramref name="range"/> (all when null), row-major.</summary>
    public IEnumerable<CellEntry> GetCells(CellRange? range = null)
    {
        foreach (var (row, column, record) in Store.Enumerate(range ?? CellRange.Whole))
        {
            yield return new CellEntry(row, column, ToCell(record));
        }
    }

    /// <summary>Whether the cell has a value or formula (a spilled value counts; a style alone does not).</summary>
    public bool HasContent(int row, int column) =>
        Store.TryGet(row, column, out var record) && (record.Kind != CellValueKind.Blank || record.Payload is not null);

    /// <summary>The anchor of the spill / array range that covers the cell, or null.</summary>
    public CellAddress? FindArrayAnchor(int row, int column)
    {
        if (Store.TryGet(row, column, out var own) && own.Formula is { } formula && (formula.SpillRange is not null || formula.ArrayRange is not null))
        {
            return new CellAddress(row, column);
        }

        return Workbook.Calculation.FindAnchor(this, row, column)?.Address;
    }

    // ---------------------------------------------------------------- writing (no undo)

    /// <summary>Puts a constant into the cell (a formula there is removed), keeping its style.</summary>
    public void SetValue(int row, int column, CellValue value)
    {
        var record = Store.Get(row, column);
        RemoveFormula(ref record);
        record.Flags = CellFlags.None;
        record.SetValue(value);
        Store.Set(row, column, record);
        Workbook.Calculation.OnCellChanged(this, row, column);
        Workbook.NoteChanged(this, CellRange.Cell(row, column));
    }

    public void SetValue(string address, CellValue value)
    {
        var cell = CellAddress.Parse(address);
        SetValue(cell.Row, cell.Column, value);
    }

    /// <summary>Puts a formula into the cell (dirty until the next recalculation), keeping its style.</summary>
    public void SetFormula(int row, int column, Formula formula, bool dynamicArray = true, CellRange? arrayRange = null)
    {
        var record = Store.Get(row, column);
        RemoveFormula(ref record);
        var formulaCell = new FormulaCell(this, row, column, formula, dynamicArray) { ArrayRange = arrayRange };
        record.Payload = formulaCell;
        record.Kind = CellValueKind.Blank;
        record.Number = 0;
        record.Flags = CellFlags.None;
        Store.Set(row, column, record);
        Workbook.Calculation.OnFormulaAdded(formulaCell);
        Workbook.NoteChanged(this, CellRange.Cell(row, column));
    }

    /// <summary>Parses <paramref name="formula"/> (canonical, with or without "=") and puts it into the cell.</summary>
    public void SetFormula(string address, string formula)
    {
        var cell = CellAddress.Parse(address);
        SetFormula(cell.Row, cell.Column, FormulaParser.Parse(formula, Workbook, this, cell));
    }

    public void SetStyleId(int row, int column, int styleId)
    {
        var record = Store.Get(row, column);
        if (record.StyleId == styleId)
        {
            return;
        }

        record.StyleId = styleId;
        Store.Set(row, column, record);
        Workbook.NoteChanged(this, CellRange.Cell(row, column), styleOnly: true);
    }

    /// <summary>Restores a snapshot (undo, paste): value or formula and style.</summary>
    public void SetCell(int row, int column, Cell cell)
    {
        if (cell.Formula is not null && !cell.IsSpilled)
        {
            SetFormula(row, column, cell.Formula, cell.IsDynamicArray, cell.ArrayRange);
        }
        else
        {
            SetValue(row, column, cell.IsSpilled ? CellValue.Blank : cell.Value);
        }

        SetStyleId(row, column, cell.StyleId);
    }

    /// <summary>Removes values and formulas in <paramref name="range"/> (styles stay).</summary>
    public void ClearContents(CellRange range)
    {
        foreach (var (row, column, record) in Store.Enumerate(range).ToList())
        {
            if (record.Kind != CellValueKind.Blank || record.Payload is not null)
            {
                SetValue(row, column, CellValue.Blank);
            }
        }
    }

    /// <summary>Resets the styles in <paramref name="range"/> to the default.</summary>
    public void ClearFormats(CellRange range)
    {
        foreach (var (row, column, _) in Store.Enumerate(range).ToList())
        {
            SetStyleId(row, column, 0);
        }
    }

    public override string ToString() => Name;

    internal static Cell ToCell(in CellRecord record)
    {
        if (record.Formula is { } formula)
        {
            return new Cell(formula.Value, formula.Formula, record.StyleId, formula.ArrayRange, formula.IsDynamicArray);
        }

        return new Cell(record.Value, null, record.StyleId, IsSpilled: (record.Flags & CellFlags.Spilled) != 0);
    }

    /// <summary>Loading: stores a value without telling the engine (it is rebuilt once at the end).</summary>
    internal void LoadValue(int row, int column, CellValue value, int styleId, bool spilled = false)
    {
        var record = CellRecord.FromValue(value, styleId);
        record.Flags = spilled ? CellFlags.Spilled : CellFlags.None;
        Store.Set(row, column, record);
    }

    /// <summary>Loading: stores a formula with its cached value, clean (no recalculation needed).</summary>
    internal FormulaCell LoadFormula(int row, int column, Formula formula, CellValue cached, int styleId, bool dynamicArray, CellRange? arrayRange, CellRange? spillRange)
    {
        var formulaCell = new FormulaCell(this, row, column, formula, dynamicArray)
        {
            Value = cached,
            ArrayRange = arrayRange,
            SpillRange = spillRange,
            State = FormulaState.Clean,
        };
        Store.Set(row, column, new CellRecord { Payload = formulaCell, StyleId = styleId });
        return formulaCell;
    }

    /// <summary>Marks the cells of a loaded array/spill range (not the anchor) as belonging to it.</summary>
    internal void MarkSpilled(CellRange range, int anchorRow, int anchorColumn)
    {
        foreach (var (row, column, record) in Store.Enumerate(range).ToList())
        {
            if ((row != anchorRow || column != anchorColumn) && record.Payload is null)
            {
                var copy = record;
                copy.Flags = CellFlags.Spilled;
                Store.Set(row, column, copy);
            }
        }
    }

    internal FormulaCell? GetFormulaCell(int row, int column) => Store.TryGet(row, column, out var record) ? record.Formula : null;

    /// <summary>Writes a value computed by the engine (formula result in a spill range) without touching formulas.</summary>
    internal void SetSpilledValue(int row, int column, CellValue value, bool spilled)
    {
        var record = Store.Get(row, column);
        if (record.Formula is not null)
        {
            return;
        }

        record.SetValue(value);
        record.Flags = spilled ? CellFlags.Spilled : CellFlags.None;
        Store.Set(row, column, record);
    }

    internal bool IsSpilledCell(int row, int column) => Store.TryGet(row, column, out var record) && (record.Flags & CellFlags.Spilled) != 0;

    private void RemoveFormula(ref CellRecord record)
    {
        if (record.Formula is { } old)
        {
            Workbook.Calculation.OnFormulaRemoved(old);
            record.Payload = null;
        }
    }
}
