using System.Globalization;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Globalization;

namespace ThixCalc.Core.Model.Pivot;

/// <summary>What a cell of a PivotTable is (the style draws by it; edits are refused on all of them).</summary>
public enum PivotCellKind : byte
{
    FilterLabel,
    FilterValue,
    DataCaption,
    RowHeader,
    ColumnHeader,
    ColumnItem,
    RowItem,
    Value,
    SubtotalLabel,
    SubtotalValue,
    GrandTotalLabel,
    GrandTotalValue,
}

/// <summary>A cell of the drawn PivotTable: its value, its kind, the indent of a compact row label, its number format.</summary>
public readonly record struct PivotCell(CellValue Value, PivotCellKind Kind, int Indent = 0, string? NumberFormat = null, bool TotalRow = false);

/// <summary>A condition of a value cell: the level (field and date group), the item's key and label.</summary>
public readonly record struct PivotCondition(int Field, PivotDateGroups Group, string Name, string Key, CellValue Label);

/// <summary>A value cell for PIVOTDATENZUORDNEN and the drill-down: which value field, under which items.</summary>
public sealed record PivotValueEntry(CellAddress Cell, int DataField, IReadOnlyList<PivotCondition> Conditions, CellValue Value);

/// <summary>The PivotTable computed for drawing: every cell with its kind, the range, the value cells.</summary>
public sealed class PivotOutput
{
    public required CellRange Range { get; init; }

    /// <summary>The top left cell of the body (the header rows start here).</summary>
    public required CellAddress Body { get; init; }

    public required int HeaderRows { get; init; }

    public required int LabelColumns { get; init; }

    public required Dictionary<CellAddress, PivotCell> Cells { get; init; }

    public required List<PivotValueEntry> Values { get; init; }

    /// <summary>The header cells of the row label columns that get a filter arrow (row → field index).</summary>
    public required List<(CellAddress Cell, int Field)> FilterButtons { get; init; }

    public PivotValueEntry? EntryAt(CellAddress cell) => Values.FirstOrDefault(entry => entry.Cell == cell);
}

/// <summary>
/// Computes a PivotTable from its cache: filters (filter fields, hidden items, label filters, then value filters), groups the
/// records into row and column trees (date and number groups as their own levels), aggregates every value field for every pair
/// of nodes in one pass (100 000 records × a few levels in well under a second), sorts, applies Werte anzeigen als and lays the
/// cells out in Kurzformat, Gliederungsformat or Tabellenformat as Excel does (German captions).
/// </summary>
public static class PivotEngine
{
    public const string RowLabels = "Zeilenbeschriftungen";
    public const string ColumnLabels = "Spaltenbeschriftungen";
    public const string GrandTotal = "Gesamtergebnis";
    public const string ValuesCaption = "Werte";
    public const string AllItems = "(Alle)";
    public const string MultipleItems = "(Mehrere Elemente)";

    private static readonly string[] Months = ["Jan", "Feb", "Mrz", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez"];

    /// <summary>The name a function gives a value field: "Summe von Umsatz".</summary>
    public static string FunctionLabel(PivotFunction function) => function switch
    {
        PivotFunction.Sum => "Summe",
        PivotFunction.Count => "Anzahl",
        PivotFunction.Average => "Mittelwert",
        PivotFunction.Max => "Max.",
        PivotFunction.Min => "Min.",
        PivotFunction.Product => "Produkt",
        PivotFunction.CountNumbers => "Anzahl Zahlen",
        PivotFunction.StdDev => "StdAbw",
        PivotFunction.StdDevP => "StdAbwN",
        PivotFunction.Var => "Varianz",
        _ => "Varianzen",
    };

    public static string DataFieldName(PivotFunction function, string field) => $"{FunctionLabel(function)} von {field}";

    /// <summary>The caption of a field in a pivot (its custom name, else the source column's).</summary>
    public static string Caption(PivotTable pivot, PivotCache cache, int field) =>
        field == PivotTable.ValuesField ? ValuesCaption : pivot.Fields[field].Caption ?? cache.Fields[field].Name;

    // ---------------------------------------------------------------- levels

    /// <summary>A level of an axis: a field (or one of its date groups) or Σ Werte.</summary>
    internal sealed class Level
    {
        public required int Field { get; init; }

        public PivotDateGroups Group { get; init; }

        public required string Name { get; init; }

        public bool IsValues => Field == PivotTable.ValuesField;

        /// <summary>Per cache item the level's item id.</summary>
        public int[] ItemOf { get; init; } = [];

        public List<(string Key, CellValue Label, double Order, bool Numeric, string? Format)> Items { get; } = [];
    }

    internal static List<Level> LevelsOf(PivotTable pivot, PivotCache cache, IEnumerable<int> fields)
    {
        var levels = new List<Level>();
        foreach (var field in fields)
        {
            if (field == PivotTable.ValuesField)
            {
                if (pivot.DataFields.Length > 1)
                {
                    levels.Add(new Level { Field = field, Name = ValuesCaption });
                }

                continue;
            }

            var settings = pivot.Fields[field];
            var name = Caption(pivot, cache, field);
            if (settings.DateGroups != PivotDateGroups.None)
            {
                // Coarse to fine; the field keeps its name on its finest level, the others are Jahre/Quartale/Monate/Tage.
                var groups = new[] { PivotDateGroups.Years, PivotDateGroups.Quarters, PivotDateGroups.Months, PivotDateGroups.Days }
                    .Where(group => settings.DateGroups.HasFlag(group)).ToList();
                foreach (var group in groups)
                {
                    levels.Add(Build(cache, field, group, null, group == groups[^1] ? name : GroupName(group)));
                }
            }
            else
            {
                levels.Add(Build(cache, field, PivotDateGroups.None, settings.NumberGroup, name));
            }
        }

        return levels;
    }

    public static string GroupName(PivotDateGroups group) => group switch
    {
        PivotDateGroups.Years => "Jahre",
        PivotDateGroups.Quarters => "Quartale",
        PivotDateGroups.Months => "Monate",
        _ => "Tage",
    };

    private static Level Build(PivotCache cache, int field, PivotDateGroups group, PivotNumberGroup? numbers, string name)
    {
        var source = cache.Fields[field];
        var itemOf = new int[source.Items.Count];
        var level = new Level { Field = field, Group = group, Name = name, ItemOf = itemOf };
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        for (var index = 0; index < source.Items.Count; index++)
        {
            var (key, label, order, numeric, format) = ItemFor(source.Items[index], group, numbers, source.NumberFormat);
            if (!ids.TryGetValue(key, out var id))
            {
                id = level.Items.Count;
                ids[key] = id;
                level.Items.Add((key, label, order, numeric, format));
            }

            itemOf[index] = id;
        }

        return level;
    }

    /// <summary>The level item of a value: plain, a date part (Jahre "2024", Quartale "Qrtl1", Monate "Jan", Tage "01. Jan"), a number band.</summary>
    internal static (string Key, CellValue Label, double Order, bool Numeric, string? Format) ItemFor(CellValue value, PivotDateGroups group, PivotNumberGroup? numbers, string? format)
    {
        if (group != PivotDateGroups.None && value.IsNumber && DateSerial.ToDateTime(value.Number) is { } date)
        {
            return group switch
            {
                PivotDateGroups.Years => ("y" + date.Year, date.Year.ToString(CultureInfo.InvariantCulture), date.Year, false, null),
                PivotDateGroups.Quarters => ("q" + ((date.Month + 2) / 3), "Qrtl" + ((date.Month + 2) / 3), (date.Month + 2) / 3, false, null),
                PivotDateGroups.Months => ("m" + date.Month, Months[date.Month - 1], date.Month, false, null),
                _ => ("d" + date.Month + "-" + date.Day, $"{date.Day:00}. {Months[date.Month - 1]}", date.Month * 100 + date.Day, false, null),
            };
        }

        if (numbers is not null && value.IsNumber)
        {
            var number = value.Number;
            if (number < numbers.Start)
            {
                return ("<", "<" + Text(numbers.Start), double.MinValue, false, null);
            }

            if (number > numbers.End)
            {
                return (">", ">" + Text(numbers.End), double.MaxValue, false, null);
            }

            var start = numbers.Start + Math.Floor((number - numbers.Start) / numbers.Interval) * numbers.Interval;
            var whole = numbers.Interval == Math.Floor(numbers.Interval) && numbers.Start == Math.Floor(numbers.Start);
            var end = Math.Min(numbers.End, whole ? start + numbers.Interval - 1 : start + numbers.Interval);
            return ("g" + start.ToString("R", CultureInfo.InvariantCulture), Text(start) + "-" + Text(end), start, false, null);
        }

        if (group != PivotDateGroups.None || numbers is not null)
        {
            // Not a date/number in a grouped field: Excel puts it before the groups ("<01.01.2024" …), here by its text.
            return ("x" + PivotItems.KeyOf(value), value.IsBlank ? PivotItems.BlankLabel : value, double.MinValue, false, null);
        }

        return value.Kind switch
        {
            CellValueKind.Blank => (PivotItems.KeyOf(value), PivotItems.BlankLabel, double.MaxValue, false, null),
            CellValueKind.Number => (PivotItems.KeyOf(value), value, value.Number, true, format),
            _ => (PivotItems.KeyOf(value), value, 0, false, null),
        };

        static string Text(double number) => number.ToString("0.###############", CultureInfo.GetCultureInfo("de-DE"));
    }

    // ---------------------------------------------------------------- aggregation

    /// <summary>The running aggregate of one value field for one pair of nodes.</summary>
    private struct Accumulator
    {
        public int Count;
        public int Numbers;
        public double Sum;
        public double Product;
        public double Min;
        public double Max;
        public double Mean;
        public double Squares;
        public CellError? Error;

        public void Add(CellValue value)
        {
            if (value.IsBlank)
            {
                return;
            }

            Count++;
            if (value.IsError)
            {
                Error ??= value.Error;
                return;
            }

            if (!value.IsNumber)
            {
                return;
            }

            var number = value.Number;
            if (Numbers == 0)
            {
                Product = 1;
                Min = number;
                Max = number;
            }

            Numbers++;
            Sum += number;
            Product *= number;
            Min = Math.Min(Min, number);
            Max = Math.Max(Max, number);
            var delta = number - Mean;
            Mean += delta / Numbers;
            Squares += delta * (number - Mean);
        }

        public readonly CellValue Result(PivotFunction function)
        {
            if (Count == 0)
            {
                return CellValue.Blank;
            }

            if (function is PivotFunction.Count)
            {
                return Count;
            }

            if (Error is { } error)
            {
                return error;
            }

            return function switch
            {
                PivotFunction.Sum => Sum,
                PivotFunction.CountNumbers => Numbers,
                PivotFunction.Average => Numbers == 0 ? CellError.DivisionByZero : Sum / Numbers,
                PivotFunction.Max => Numbers == 0 ? 0 : Max,
                PivotFunction.Min => Numbers == 0 ? 0 : Min,
                PivotFunction.Product => Numbers == 0 ? 0 : Product,
                PivotFunction.StdDev => Numbers < 2 ? CellError.DivisionByZero : Math.Sqrt(Squares / (Numbers - 1)),
                PivotFunction.StdDevP => Numbers < 1 ? CellError.DivisionByZero : Math.Sqrt(Squares / Numbers),
                PivotFunction.Var => Numbers < 2 ? CellError.DivisionByZero : Squares / (Numbers - 1),
                _ => Numbers < 1 ? CellError.DivisionByZero : Squares / Numbers,
            };
        }
    }

    /// <summary>A node of a row or column tree (0 = the root = the totals).</summary>
    private sealed class Node
    {
        public int Id;
        public int Parent = -1;
        public int Depth;
        public int Item = -1;
        public List<int> Children = [];
        public Dictionary<int, int> ByItem = [];
    }

    private sealed class Tree
    {
        public List<Node> Nodes { get; } = [new Node()];

        public int Child(int parent, int item, int depth)
        {
            var node = Nodes[parent];
            if (node.ByItem.TryGetValue(item, out var child))
            {
                return child;
            }

            child = Nodes.Count;
            Nodes.Add(new Node { Id = child, Parent = parent, Depth = depth, Item = item });
            node.ByItem[item] = child;
            node.Children.Add(child);
            return child;
        }
    }

    /// <summary>Everything one computation needs: levels, trees, aggregates.</summary>
    private sealed class Computation
    {
        public required PivotTable Pivot { get; init; }

        public required PivotCache Cache { get; init; }

        public required List<Level> RowLevels { get; init; }

        public required List<Level> ColumnLevels { get; init; }

        /// <summary>The field levels only (no Σ Werte), per axis.</summary>
        public required List<Level> RowFieldLevels { get; init; }

        public required List<Level> ColumnFieldLevels { get; init; }

        public Tree Rows { get; set; } = new();

        public Tree Columns { get; set; } = new();

        public Dictionary<long, Accumulator[]> Sums { get; set; } = [];

        public CellValue Value(int row, int column, int data)
        {
            if (!Sums.TryGetValue(((long)row << 32) | (uint)column, out var sums))
            {
                return CellValue.Blank;
            }

            return sums[data].Result(Pivot.DataFields[data].Function);
        }

        public double Number(int row, int column, int data) => Value(row, column, data) is { IsNumber: true } value ? value.Number : 0;
    }

    /// <summary>Which records are shown: filter fields, hidden items and label filters (value filters come later).</summary>
    internal static bool[] VisibleRecords(PivotTable pivot, PivotCache cache, CalcLocale? locale = null)
    {
        var visible = new bool[cache.RecordCount];
        Array.Fill(visible, true);
        for (var field = 0; field < pivot.Fields.Length && field < cache.Fields.Count; field++)
        {
            var settings = pivot.Fields[field];
            var inPivot = settings.Axis != PivotAxis.None;
            if (!inPivot || (settings.HiddenItems.Count == 0 && settings.SelectedItem is null && settings.LabelFilter is null))
            {
                continue;
            }

            var source = cache.Fields[field];
            var finest = settings.DateGroups == PivotDateGroups.None ? PivotDateGroups.None
                : new[] { PivotDateGroups.Days, PivotDateGroups.Months, PivotDateGroups.Quarters, PivotDateGroups.Years }.First(group => settings.DateGroups.HasFlag(group));
            var allowed = new bool[source.Items.Count];
            for (var item = 0; item < source.Items.Count; item++)
            {
                var value = source.Items[item];
                var (key, label, _, _, _) = ItemFor(value, finest, settings.NumberGroup, source.NumberFormat);
                var show = !settings.HiddenItems.Contains(key);
                if (settings.Axis == PivotAxis.Filter && settings.SelectedItem is { } selected)
                {
                    show &= PivotItems.KeyOf(value) == selected || key == selected;
                }

                if (show && settings.LabelFilter is { ByValue: false } filter)
                {
                    show = Matches(filter, label, locale ?? CalcLocale.German);
                }

                allowed[item] = show;
            }

            var records = cache.Records[field];
            for (var record = 0; record < visible.Length; record++)
            {
                if (visible[record] && !allowed[records[record]])
                {
                    visible[record] = false;
                }
            }
        }

        return visible;
    }

    /// <summary>A label filter's test of an item label.</summary>
    internal static bool Matches(PivotFilter filter, CellValue label, CalcLocale locale)
    {
        var text = LabelText(label, locale);
        var numeric = label.IsNumber;
        double.TryParse(filter.Value.Replace(locale.DecimalSeparator, '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number);
        double.TryParse((filter.Value2 ?? string.Empty).Replace(locale.DecimalSeparator, '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number2);
        int Compare() => numeric && double.TryParse(filter.Value.Replace(locale.DecimalSeparator, '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            ? label.Number.CompareTo(number)
            : string.Compare(text, filter.Value, CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);

        return filter.Kind switch
        {
            PivotFilterKind.Equals => Compare() == 0,
            PivotFilterKind.NotEquals => Compare() != 0,
            PivotFilterKind.GreaterThan => Compare() > 0,
            PivotFilterKind.GreaterOrEqual => Compare() >= 0,
            PivotFilterKind.LessThan => Compare() < 0,
            PivotFilterKind.LessOrEqual => Compare() <= 0,
            PivotFilterKind.BeginsWith => text.StartsWith(filter.Value, StringComparison.CurrentCultureIgnoreCase),
            PivotFilterKind.EndsWith => text.EndsWith(filter.Value, StringComparison.CurrentCultureIgnoreCase),
            PivotFilterKind.Contains => text.Contains(filter.Value, StringComparison.CurrentCultureIgnoreCase),
            PivotFilterKind.Between => numeric ? label.Number >= number && label.Number <= number2
                : string.Compare(text, filter.Value, StringComparison.CurrentCultureIgnoreCase) >= 0 && string.Compare(text, filter.Value2, StringComparison.CurrentCultureIgnoreCase) <= 0,
            _ => true,
        };
    }

    private static void Aggregate(Computation computation, bool[] visible)
    {
        var cache = computation.Cache;
        var pivot = computation.Pivot;
        var rows = new Tree();
        var columns = new Tree();
        var sums = new Dictionary<long, Accumulator[]>();
        var dataCount = pivot.DataFields.Length;
        var rowLevels = computation.RowFieldLevels;
        var columnLevels = computation.ColumnFieldLevels;
        var rowPath = new int[rowLevels.Count + 1];
        var columnPath = new int[columnLevels.Count + 1];
        var values = new CellValue[dataCount];
        for (var record = 0; record < visible.Length; record++)
        {
            if (!visible[record])
            {
                continue;
            }

            for (var depth = 0; depth < rowLevels.Count; depth++)
            {
                var level = rowLevels[depth];
                rowPath[depth + 1] = rows.Child(rowPath[depth], level.ItemOf[cache.Records[level.Field][record]], depth + 1);
            }

            for (var depth = 0; depth < columnLevels.Count; depth++)
            {
                var level = columnLevels[depth];
                columnPath[depth + 1] = columns.Child(columnPath[depth], level.ItemOf[cache.Records[level.Field][record]], depth + 1);
            }

            for (var data = 0; data < dataCount; data++)
            {
                values[data] = cache.Value(pivot.DataFields[data].Field, record);
            }

            for (var row = 0; row < rowPath.Length; row++)
            {
                for (var column = 0; column < columnPath.Length; column++)
                {
                    var key = ((long)rowPath[row] << 32) | (uint)columnPath[column];
                    if (!sums.TryGetValue(key, out var accumulators))
                    {
                        sums[key] = accumulators = new Accumulator[dataCount];
                    }

                    for (var data = 0; data < dataCount; data++)
                    {
                        accumulators[data].Add(values[data]);
                    }
                }
            }
        }

        computation.Rows = rows;
        computation.Columns = columns;
        computation.Sums = sums;
    }

    /// <summary>Value filters (Top 10, greater than …): the records under the items that fail them are left out.</summary>
    private static bool ApplyValueFilters(Computation computation, bool[] visible)
    {
        var changed = false;
        foreach (var (tree, levels, isRow) in new[] { (computation.Rows, computation.RowFieldLevels, true), (computation.Columns, computation.ColumnFieldLevels, false) })
        {
            for (var depth = 0; depth < levels.Count; depth++)
            {
                var level = levels[depth];
                if (computation.Pivot.Fields[level.Field].ValueFilter is not { ByValue: true } filter || filter.DataField >= computation.Pivot.DataFields.Length)
                {
                    continue;
                }

                var dropped = new HashSet<int>();
                foreach (var parent in tree.Nodes.Where(node => node.Depth == depth))
                {
                    double Total(int node) => isRow ? computation.Number(node, 0, filter.DataField) : computation.Number(0, node, filter.DataField);
                    var children = parent.Children;
                    if (filter.Kind is PivotFilterKind.Top or PivotFilterKind.Bottom)
                    {
                        var count = (int)Math.Max(0, double.TryParse(filter.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var n) ? n : 10);
                        var ordered = filter.Kind == PivotFilterKind.Top ? children.OrderByDescending(Total) : children.OrderBy(Total);
                        foreach (var child in ordered.Skip(count))
                        {
                            dropped.Add(tree.Nodes[child].Item);
                        }
                    }
                    else
                    {
                        foreach (var child in children)
                        {
                            if (!Matches(filter, Total(child), CalcLocale.Invariant))
                            {
                                dropped.Add(tree.Nodes[child].Item);
                            }
                        }
                    }
                }

                if (dropped.Count == 0)
                {
                    continue;
                }

                var records = computation.Cache.Records[level.Field];
                for (var record = 0; record < visible.Length; record++)
                {
                    if (visible[record] && dropped.Contains(level.ItemOf[records[record]]))
                    {
                        visible[record] = false;
                        changed = true;
                    }
                }
            }
        }

        return changed;
    }

    private static void SortTree(Computation computation, Tree tree, List<Level> levels, bool isRow)
    {
        foreach (var node in tree.Nodes)
        {
            if (node.Children.Count < 2 || node.Depth >= levels.Count)
            {
                continue;
            }

            var level = levels[node.Depth];
            var sort = computation.Pivot.Fields[level.Field].Sort;
            Comparison<int> compare;
            if (sort.DataField >= 0 && sort.DataField < computation.Pivot.DataFields.Length)
            {
                double Total(int child) => isRow ? computation.Number(child, 0, sort.DataField) : computation.Number(0, child, sort.DataField);
                compare = (left, right) => Total(left).CompareTo(Total(right));
            }
            else
            {
                compare = (left, right) => CompareItems(level, tree.Nodes[left].Item, tree.Nodes[right].Item);
            }

            node.Children.Sort(sort.Descending ? (left, right) => compare(right, left) : compare);
        }
    }

    /// <summary>Excel's item order: numbers (and date groups) by value, then texts alphabetically, (Leer) last.</summary>
    private static int CompareItems(Level level, int left, int right)
    {
        var a = level.Items[left];
        var b = level.Items[right];
        int Rank((string Key, CellValue Label, double Order, bool Numeric, string? Format) item) =>
            item.Label is { IsText: true } label && label.Text == PivotItems.BlankLabel && item.Order == double.MaxValue ? 3
            : item.Numeric || level.Group != PivotDateGroups.None || item.Key.StartsWith('g') || item.Key is "<" or ">" ? 0
            : item.Label.IsText ? 1 : 2;

        var rank = Rank(a).CompareTo(Rank(b));
        if (rank != 0)
        {
            return rank;
        }

        return Rank(a) == 0 ? a.Order.CompareTo(b.Order) : string.Compare(LabelText(a.Label, CalcLocale.German), LabelText(b.Label, CalcLocale.German), CultureInfo.CurrentCulture, CompareOptions.IgnoreCase);
    }

    // ---------------------------------------------------------------- layout

    private sealed record Slot(int Node, int Data, int Depth, SlotKind Kind);

    private enum SlotKind : byte
    {
        Item,
        Subtotal,
        GrandTotal,
        DataName,
    }

    /// <summary>Computes the PivotTable (null when the cache has no records yet: the file's values stay).</summary>
    public static PivotOutput? Compute(PivotTable pivot, PivotCache cache, CalcLocale? locale = null)
    {
        if (!cache.HasRecords || pivot.Unsupported is not null)
        {
            return null;
        }

        locale ??= CalcLocale.German;
        var dataCount = pivot.DataFields.Length;
        var rowFields = pivot.RowFields.ToList();
        var columnFields = pivot.ColumnFields.ToList();
        if (dataCount > 1 && !rowFields.Contains(PivotTable.ValuesField) && !columnFields.Contains(PivotTable.ValuesField))
        {
            columnFields.Add(PivotTable.ValuesField);
        }

        var rowLevels = LevelsOf(pivot, cache, rowFields);
        var columnLevels = LevelsOf(pivot, cache, columnFields);
        var computation = new Computation
        {
            Pivot = pivot,
            Cache = cache,
            RowLevels = rowLevels,
            ColumnLevels = columnLevels,
            RowFieldLevels = rowLevels.Where(level => !level.IsValues).ToList(),
            ColumnFieldLevels = columnLevels.Where(level => !level.IsValues).ToList(),
        };
        var visible = VisibleRecords(pivot, cache, locale);
        Aggregate(computation, visible);
        if (ApplyValueFilters(computation, visible))
        {
            Aggregate(computation, visible);
        }

        SortTree(computation, computation.Rows, computation.RowFieldLevels, isRow: true);
        SortTree(computation, computation.Columns, computation.ColumnFieldLevels, isRow: false);
        return Layout(computation, locale);
    }

    private static List<Slot> Expand(Computation computation, List<Level> levels, Tree tree, bool isRow)
    {
        var pivot = computation.Pivot;
        var slots = new List<Slot>();
        var tabular = isRow && (pivot.Layout == PivotLayout.Tabular || !pivot.SubtotalsAtTop);
        var dataCount = pivot.DataFields.Length;

        void Walk(int levelIndex, int node, int data, int depth)
        {
            if (levelIndex >= levels.Count)
            {
                slots.Add(new Slot(node, Math.Max(data, dataCount == 1 ? 0 : data), depth, SlotKind.Item));
                return;
            }

            var level = levels[levelIndex];
            if (level.IsValues)
            {
                for (var index = 0; index < dataCount; index++)
                {
                    if (isRow && levelIndex == levels.Count - 1)
                    {
                        slots.Add(new Slot(node, index, depth, SlotKind.DataName));
                    }
                    else
                    {
                        Walk(levelIndex + 1, node, index, depth + 1);
                    }
                }

                return;
            }

            var deeper = levels.Skip(levelIndex + 1).Any(other => !other.IsValues);
            var valuesBelow = levels.Skip(levelIndex + 1).Any(other => other.IsValues);
            var subtotals = pivot.Fields[level.Field].Subtotals && deeper;
            foreach (var child in tree.Nodes[node].Children)
            {
                if (!isRow)
                {
                    Walk(levelIndex + 1, child, data, depth + 1);
                    if (subtotals)
                    {
                        AddSubtotals(child, data, depth + 1, valuesBelow);
                    }

                    continue;
                }

                if (!deeper && !valuesBelow)
                {
                    slots.Add(new Slot(child, data, depth + 1, SlotKind.Item));
                    continue;
                }

                if (tabular)
                {
                    Walk(levelIndex + 1, child, data, depth + 1);
                    if (subtotals)
                    {
                        AddSubtotals(child, data, depth + 1, valuesBelow);
                    }
                }
                else
                {
                    // Kurzformat/Gliederung: the item's own row carries its subtotal.
                    slots.Add(new Slot(child, subtotals && !valuesBelow ? data : -2, depth + 1, SlotKind.Item));
                    Walk(levelIndex + 1, child, data, depth + 1);
                }
            }

            void AddSubtotals(int child, int fixedData, int childDepth, bool perData)
            {
                if (perData && fixedData < 0)
                {
                    for (var index = 0; index < dataCount; index++)
                    {
                        slots.Add(new Slot(child, index, childDepth, SlotKind.Subtotal));
                    }
                }
                else
                {
                    slots.Add(new Slot(child, fixedData, childDepth, SlotKind.Subtotal));
                }
            }
        }

        Walk(0, 0, -1, 0);
        var hasFieldLevels = levels.Any(level => !level.IsValues);
        var grand = isRow ? pivot.ColumnGrandTotals : pivot.RowGrandTotals;
        if (hasFieldLevels && grand)
        {
            if (levels.Any(level => level.IsValues) && !isRow)
            {
                for (var index = 0; index < dataCount; index++)
                {
                    slots.Add(new Slot(0, index, 0, SlotKind.GrandTotal));
                }
            }
            else
            {
                slots.Add(new Slot(0, -1, 0, SlotKind.GrandTotal));
            }
        }

        if (slots.Count == 0)
        {
            slots.Add(new Slot(0, -1, 0, SlotKind.Item));
        }

        return slots;
    }

    private static PivotOutput Layout(Computation computation, CalcLocale locale)
    {
        var pivot = computation.Pivot;
        var cache = computation.Cache;
        var dataCount = pivot.DataFields.Length;
        var cells = new Dictionary<CellAddress, PivotCell>();
        var entries = new List<PivotValueEntry>();
        var buttons = new List<(CellAddress, int)>();
        var rowSlots = Expand(computation, computation.RowLevels, computation.Rows, isRow: true);
        var columnSlots = Expand(computation, computation.ColumnLevels, computation.Columns, isRow: false);
        var compact = pivot.Layout == PivotLayout.Compact;
        var rowFieldLevels = computation.RowLevels;
        var labelColumns = compact || rowFieldLevels.Count == 0 ? 1 : rowFieldLevels.Count;
        var columnLevelCount = computation.ColumnFieldLevels.Count > 0 ? computation.ColumnLevels.Count : 0;
        var headerRows = columnLevelCount > 0 ? 1 + columnLevelCount : 1;

        // Filter fields above the body.
        var filters = pivot.FilterFields;
        var bodyRow = Math.Max(pivot.Location.Row, filters.Length > 0 ? filters.Length + 1 : 0);
        var top = bodyRow;
        var left = pivot.Location.Column;
        for (var index = 0; index < filters.Length; index++)
        {
            var field = filters[index];
            var row = bodyRow - filters.Length - 1 + index;
            var settings = pivot.Fields[field];
            cells[new CellAddress(row, left)] = new PivotCell(Caption(pivot, cache, field), PivotCellKind.FilterLabel);
            CellValue selection = settings.SelectedItem is { } selected
                ? cache.Fields[field].Items.FirstOrDefault(item => PivotItems.KeyOf(item) == selected) is var item && !item.IsBlank ? item : (CellValue)PivotItems.BlankLabel
                : settings.HiddenItems.Count > 0 ? MultipleItems : AllItems;
            cells[new CellAddress(row, left + 1)] = new PivotCell(selection, PivotCellKind.FilterValue, NumberFormat: cache.Fields[field].NumberFormat);
            buttons.Add((new CellAddress(row, left + 1), field));
        }

        var dataCaption = dataCount == 1 ? (CellValue)pivot.DataFields[0].Name : CellValue.EmptyText;
        if (columnLevelCount > 0)
        {
            cells[new CellAddress(top, left)] = new PivotCell(dataCaption, PivotCellKind.DataCaption);
            var columnHeader = compact ? ColumnLabels : computation.ColumnLevels[0].Name;
            if (pivot.ColumnHeaders)
            {
                cells[new CellAddress(top, left + labelColumns)] = new PivotCell(columnHeader, PivotCellKind.ColumnHeader);
                if (computation.ColumnLevels.Any(level => !level.IsValues))
                {
                    buttons.Add((new CellAddress(top, left + labelColumns), computation.ColumnLevels.First(level => !level.IsValues).Field));
                }
            }
        }

        // Row label headers in the last header row.
        var labelHeaderRow = top + headerRows - 1;
        if (pivot.RowHeaders)
        {
            if (rowFieldLevels.Count > 0)
            {
                for (var column = 0; column < labelColumns; column++)
                {
                    var text = compact ? RowLabels : rowFieldLevels[column].Name;
                    cells[new CellAddress(labelHeaderRow, left + column)] = new PivotCell(text, PivotCellKind.RowHeader);
                    var firstField = compact ? rowFieldLevels.FirstOrDefault(level => !level.IsValues) : rowFieldLevels[column].IsValues ? null : rowFieldLevels[column];
                    if (firstField is not null)
                    {
                        buttons.Add((new CellAddress(labelHeaderRow, left + column), firstField.Field));
                    }
                }
            }
            else if (columnLevelCount == 0 && dataCount > 0)
            {
                cells[new CellAddress(labelHeaderRow, left)] = new PivotCell(CellValue.EmptyText, PivotCellKind.RowHeader);
            }
        }

        // Column headers: one row per column level, then (without column levels) the value field names.
        var columnStart = left + labelColumns;
        for (var index = 0; index < columnSlots.Count; index++)
        {
            var slot = columnSlots[index];
            var column = columnStart + index;
            if (columnLevelCount == 0)
            {
                if (dataCount > 0 && rowFieldLevels.Count > 0 && !rowFieldLevels.Any(level => level.IsValues))
                {
                    cells[new CellAddress(labelHeaderRow, column)] = new PivotCell(pivot.DataFields[Math.Max(0, slot.Data)].Name, PivotCellKind.ColumnItem);
                }

                continue;
            }

            switch (slot.Kind)
            {
                case SlotKind.GrandTotal:
                    cells[new CellAddress(top + 1, column)] = new PivotCell(slot.Data >= 0 && dataCount > 1 ? $"Gesamt: {pivot.DataFields[slot.Data].Name}" : GrandTotal, PivotCellKind.GrandTotalLabel);
                    break;
                case SlotKind.Subtotal:
                    var subtotalLabel = LabelOf(computation, computation.Columns, computation.ColumnFieldLevels, slot.Node, locale);
                    cells[new CellAddress(top + 1 + LevelRow(computation.ColumnLevels, slot.Depth - 1), column)] = new PivotCell(
                        $"{subtotalLabel} Ergebnis" + (slot.Data >= 0 && dataCount > 1 && computation.ColumnLevels.Any(level => level.IsValues) ? $" - {pivot.DataFields[slot.Data].Name}" : string.Empty),
                        PivotCellKind.SubtotalLabel);
                    break;
                default:
                    // The labels of every level this column starts (the first column of a group shows the group).
                    var path = PathOf(computation.Columns, slot.Node);
                    var previous = index > 0 && columnSlots[index - 1].Kind == SlotKind.Item ? columnSlots[index - 1] : null;
                    var previousPath = previous is null ? [] : PathOf(computation.Columns, previous.Node);
                    var fieldDepth = 0;
                    for (var levelIndex = 0; levelIndex < computation.ColumnLevels.Count; levelIndex++)
                    {
                        var level = computation.ColumnLevels[levelIndex];
                        var row = top + 1 + levelIndex;
                        if (level.IsValues)
                        {
                            cells[new CellAddress(row, column)] = new PivotCell(pivot.DataFields[Math.Max(0, slot.Data)].Name, PivotCellKind.ColumnItem);
                            continue;
                        }

                        if (fieldDepth < path.Count)
                        {
                            var node = path[fieldDepth];
                            var same = fieldDepth < previousPath.Count && previousPath[fieldDepth] == node;
                            if (!same || pivot.RepeatLabels)
                            {
                                var item = level.Items[computation.Columns.Nodes[node].Item];
                                cells[new CellAddress(row, column)] = new PivotCell(item.Label, PivotCellKind.ColumnItem, NumberFormat: item.Format);
                            }
                        }

                        fieldDepth++;
                    }

                    break;
            }
        }

        // Body rows.
        var bodyTop = top + headerRows;
        for (var index = 0; index < rowSlots.Count; index++)
        {
            var slot = rowSlots[index];
            var row = bodyTop + index;
            var isGrand = slot.Kind == SlotKind.GrandTotal;
            var isSubtotalRow = slot.Kind == SlotKind.Subtotal || (slot.Kind == SlotKind.Item && slot.Depth < computation.RowFieldLevels.Count && compact && computation.Rows.Nodes[slot.Node].Children.Count > 0);
            switch (slot.Kind)
            {
                case SlotKind.GrandTotal:
                    cells[new CellAddress(row, left)] = new PivotCell(GrandTotal, PivotCellKind.GrandTotalLabel, TotalRow: true);
                    break;
                case SlotKind.Subtotal:
                    var label = LabelOf(computation, computation.Rows, computation.RowFieldLevels, slot.Node, locale);
                    var subtotalColumn = compact ? 0 : Math.Min(labelColumns - 1, LevelRow(rowFieldLevels, slot.Depth - 1));
                    cells[new CellAddress(row, left + subtotalColumn)] = new PivotCell($"{label} Ergebnis", PivotCellKind.SubtotalLabel, compact ? slot.Depth - 1 : 0, TotalRow: true);
                    break;
                case SlotKind.DataName:
                    var dataColumn = compact ? 0 : labelColumns - 1;
                    cells[new CellAddress(row, left + dataColumn)] = new PivotCell(pivot.DataFields[slot.Data].Name, PivotCellKind.RowItem, compact ? Math.Max(0, slot.Depth) : 0);
                    break;
                default:
                    if (computation.RowFieldLevels.Count == 0)
                    {
                        if (dataCount > 0)
                        {
                            cells[new CellAddress(row, left)] = new PivotCell(dataCount == 1 ? pivot.DataFields[0].Name : CellValue.EmptyText, PivotCellKind.RowItem);
                        }

                        break;
                    }

                    var path = PathOf(computation.Rows, slot.Node);
                    if (compact)
                    {
                        var level = computation.RowFieldLevels[slot.Depth - 1];
                        var item = level.Items[computation.Rows.Nodes[slot.Node].Item];
                        cells[new CellAddress(row, left)] = new PivotCell(item.Label, isSubtotalRow ? PivotCellKind.SubtotalLabel : PivotCellKind.RowItem, slot.Depth - 1, item.Format);
                    }
                    else
                    {
                        // Gliederung/Tabelle: each level in its column; a parent's label on its first row (or every row with Wiederholen).
                        var previous = index > 0 ? rowSlots[index - 1] : null;
                        var previousPath = previous is { Kind: SlotKind.Item or SlotKind.DataName } ? PathOf(computation.Rows, previous.Node) : [];
                        var fieldColumn = 0;
                        for (var levelIndex = 0; levelIndex < rowFieldLevels.Count && fieldColumn < path.Count; levelIndex++)
                        {
                            var level = rowFieldLevels[levelIndex];
                            if (level.IsValues)
                            {
                                continue;
                            }

                            var node = path[fieldColumn];
                            var isOwn = fieldColumn == path.Count - 1;
                            var shown = isOwn || pivot.RepeatLabels || pivot.Layout == PivotLayout.Tabular && !(fieldColumn < previousPath.Count && previousPath[fieldColumn] == node);
                            if (shown)
                            {
                                var item = level.Items[computation.Rows.Nodes[node].Item];
                                cells[new CellAddress(row, left + levelIndex)] = new PivotCell(item.Label, isOwn && isSubtotalRow ? PivotCellKind.SubtotalLabel : PivotCellKind.RowItem, NumberFormat: item.Format);
                            }

                            fieldColumn++;
                        }
                    }

                    break;
            }

            if (slot.Data == -2)
            {
                continue; // a group row without values (subtotals off)
            }

            for (var column = 0; column < columnSlots.Count; column++)
            {
                var columnSlot = columnSlots[column];
                var data = slot.Data >= 0 ? slot.Data : columnSlot.Data >= 0 ? columnSlot.Data : 0;
                if (dataCount == 0 || data >= dataCount)
                {
                    continue;
                }

                var address = new CellAddress(row, columnStart + column);
                var raw = computation.Value(slot.Node, columnSlot.Node, data);
                var shown = ShowAs(computation, slot.Node, columnSlot.Node, data, raw);
                var field = pivot.DataFields[data];
                var format = field.NumberFormat ?? (field.ShowAs is PivotShowAs.PercentOfGrandTotal or PivotShowAs.PercentOfColumnTotal or PivotShowAs.PercentOfRowTotal or PivotShowAs.PercentDifference ? "0.00%" : null);
                var kind = isGrand || columnSlot.Kind == SlotKind.GrandTotal ? PivotCellKind.GrandTotalValue
                    : isSubtotalRow || slot.Kind == SlotKind.Subtotal || columnSlot.Kind == SlotKind.Subtotal ? PivotCellKind.SubtotalValue : PivotCellKind.Value;
                cells[address] = new PivotCell(shown, kind, NumberFormat: format, TotalRow: isGrand || slot.Kind == SlotKind.Subtotal);
                entries.Add(new PivotValueEntry(address, data, Conditions(computation, slot.Node, columnSlot.Node), raw));
            }
        }

        var right = columnStart + Math.Max(columnSlots.Count, dataCount == 0 ? 0 : 1) - 1;
        right = Math.Max(right, left + labelColumns - 1);
        if (filters.Length > 0)
        {
            right = Math.Max(right, left + 1);
        }

        var bottom = bodyTop + rowSlots.Count - 1;
        var first = filters.Length > 0 ? bodyRow - filters.Length - 1 : top;
        return new PivotOutput
        {
            Range = new CellRange(first, left, Math.Max(bottom, top), right),
            Body = new CellAddress(top, left),
            HeaderRows = headerRows,
            LabelColumns = labelColumns,
            Cells = cells,
            Values = entries,
            FilterButtons = buttons,
        };
    }

    /// <summary>The row (offset) of a field depth among the levels (Σ Werte counts as a level).</summary>
    private static int LevelRow(List<Level> levels, int fieldDepth)
    {
        var seen = -1;
        for (var index = 0; index < levels.Count; index++)
        {
            if (!levels[index].IsValues && ++seen == fieldDepth)
            {
                return index;
            }
        }

        return 0;
    }

    private static List<int> PathOf(Tree tree, int node)
    {
        var path = new List<int>();
        for (var current = node; current > 0; current = tree.Nodes[current].Parent)
        {
            path.Insert(0, current);
        }

        return path;
    }

    private static string LabelOf(Computation computation, Tree tree, List<Level> levels, int node, CalcLocale locale)
    {
        var current = tree.Nodes[node];
        return LabelText(levels[current.Depth - 1].Items[current.Item].Label, locale);
    }

    /// <summary>An item label as text (numbers as the formula bar shows them).</summary>
    internal static string LabelText(CellValue label, CalcLocale locale) => label.Kind switch
    {
        CellValueKind.Text => label.Text,
        CellValueKind.Number => NumberText.ToText(label.Number, locale),
        CellValueKind.Boolean => locale.BooleanName(label.Boolean),
        CellValueKind.Error => locale.ErrorName(label.Error),
        _ => PivotItems.BlankLabel,
    };

    private static List<PivotCondition> Conditions(Computation computation, int rowNode, int columnNode)
    {
        var conditions = new List<PivotCondition>();
        foreach (var (tree, levels, node) in new[] { (computation.Rows, computation.RowFieldLevels, rowNode), (computation.Columns, computation.ColumnFieldLevels, columnNode) })
        {
            foreach (var step in PathOf(tree, node))
            {
                var current = tree.Nodes[step];
                var level = levels[current.Depth - 1];
                var item = level.Items[current.Item];
                conditions.Add(new PivotCondition(level.Field, level.Group, level.Name, item.Key, item.Label));
            }
        }

        // Filter fields with one selected item are conditions too.
        foreach (var field in computation.Pivot.FilterFields)
        {
            if (computation.Pivot.Fields[field].SelectedItem is { } selected)
            {
                var value = computation.Cache.Fields[field].Items.FirstOrDefault(item => PivotItems.KeyOf(item) == selected);
                conditions.Add(new PivotCondition(field, PivotDateGroups.None, Caption(computation.Pivot, computation.Cache, field), selected, value));
            }
        }

        return conditions;
    }

    // ---------------------------------------------------------------- Werte anzeigen als

    private static CellValue ShowAs(Computation computation, int row, int column, int data, CellValue value)
    {
        var field = computation.Pivot.DataFields[data];
        if (field.ShowAs == PivotShowAs.Normal || !value.IsNumber && !value.IsBlank)
        {
            return value;
        }

        var number = value.IsNumber ? value.Number : 0;
        CellValue Ratio(double total) => total == 0 ? CellError.DivisionByZero : number / total;
        switch (field.ShowAs)
        {
            case PivotShowAs.PercentOfGrandTotal:
                return value.IsBlank ? value : Ratio(computation.Number(0, 0, data));
            case PivotShowAs.PercentOfColumnTotal:
                return value.IsBlank ? value : Ratio(computation.Number(0, column, data));
            case PivotShowAs.PercentOfRowTotal:
                return value.IsBlank ? value : Ratio(computation.Number(row, 0, data));
        }

        // Relative to the base field: find the base field's level on an axis.
        var onRows = computation.RowFieldLevels.FindIndex(level => level.Field == field.BaseField);
        var onColumns = computation.ColumnFieldLevels.FindIndex(level => level.Field == field.BaseField);
        if (onRows < 0 && onColumns < 0)
        {
            return CellError.NotAvailable;
        }

        var isRow = onRows >= 0;
        var tree = isRow ? computation.Rows : computation.Columns;
        var depth = (isRow ? onRows : onColumns) + 1;
        var node = isRow ? row : column;
        var path = PathOf(tree, node);
        if (path.Count < depth)
        {
            return field.ShowAs == PivotShowAs.RunningTotal ? value : CellValue.EmptyText;
        }

        var anchor = path[depth - 1];
        var parent = tree.Nodes[tree.Nodes[anchor].Parent];
        var siblings = parent.Children;
        var position = siblings.IndexOf(anchor);

        // The same sub-path under another sibling.
        int? Under(int sibling)
        {
            var current = sibling;
            for (var step = depth; step < path.Count; step++)
            {
                if (!tree.Nodes[current].ByItem.TryGetValue(tree.Nodes[path[step]].Item, out current))
                {
                    return null;
                }
            }

            return current;
        }

        CellValue At(int other) => isRow ? computation.Value(other, column, data) : computation.Value(row, other, data);

        if (field.ShowAs == PivotShowAs.RunningTotal)
        {
            var total = 0.0;
            for (var index = 0; index <= position; index++)
            {
                if (Under(siblings[index]) is { } match && At(match) is { IsNumber: true } part)
                {
                    total += part.Number;
                }
            }

            return total;
        }

        int baseIndex;
        if (field.BaseItem == PivotDataField.PreviousItem)
        {
            baseIndex = position - 1;
        }
        else if (field.BaseItem == PivotDataField.NextItem)
        {
            baseIndex = position + 1;
        }
        else
        {
            var level = (isRow ? computation.RowFieldLevels : computation.ColumnFieldLevels)[depth - 1];
            baseIndex = siblings.FindIndex(sibling => level.Items[tree.Nodes[sibling].Item].Key == field.BaseItem);
        }

        if (baseIndex == position)
        {
            return CellValue.EmptyText;
        }

        if (baseIndex < 0 || baseIndex >= siblings.Count)
        {
            return field.BaseItem is PivotDataField.PreviousItem or PivotDataField.NextItem ? CellValue.EmptyText : CellError.NotAvailable;
        }

        var baseValue = Under(siblings[baseIndex]) is { } baseNode ? At(baseNode) : CellValue.Blank;
        var baseNumber = baseValue.IsNumber ? baseValue.Number : 0;
        return field.ShowAs == PivotShowAs.Difference ? number - baseNumber
            : baseNumber == 0 ? CellError.Null : (number - baseNumber) / baseNumber;
    }

    // ---------------------------------------------------------------- drill-down and PIVOTDATENZUORDNEN

    /// <summary>The records behind a value cell (Details anzeigen): the visible records that meet its conditions.</summary>
    public static IReadOnlyList<int> RecordsOf(PivotTable pivot, PivotCache cache, PivotValueEntry entry)
    {
        var visible = VisibleRecords(pivot, cache);
        var result = new List<int>();
        for (var record = 0; record < cache.RecordCount; record++)
        {
            if (!visible[record])
            {
                continue;
            }

            var matches = true;
            foreach (var condition in entry.Conditions)
            {
                var source = cache.Fields[condition.Field];
                var settings = pivot.Fields[condition.Field];
                var group = condition.Group;
                var key = ItemFor(cache.Value(condition.Field, record), group, group == PivotDateGroups.None ? settings.NumberGroup : null, source.NumberFormat).Key;
                if (key != condition.Key && PivotItems.KeyOf(cache.Value(condition.Field, record)) != condition.Key)
                {
                    matches = false;
                    break;
                }
            }

            if (matches)
            {
                result.Add(record);
            }
        }

        return result;
    }

    /// <summary>
    /// PIVOTDATENZUORDNEN: the value of <paramref name="dataName"/> ("Umsatz" or "Summe von Umsatz") where every given field has the
    /// given item (and no other field is fixed): a total when fewer fields are given. #BEZUG! when there is no such cell.
    /// </summary>
    public static CellValue Lookup(PivotTable pivot, PivotCache cache, PivotOutput output, string dataName, IReadOnlyList<(string Field, CellValue Item)> pairs, CalcLocale locale)
    {
        var data = -1;
        for (var index = 0; index < pivot.DataFields.Length; index++)
        {
            var field = pivot.DataFields[index];
            if (string.Equals(field.Name, dataName, StringComparison.CurrentCultureIgnoreCase)
                || string.Equals(cache.Fields[field.Field].Name, dataName, StringComparison.CurrentCultureIgnoreCase))
            {
                data = index;
                break;
            }
        }

        if (data < 0)
        {
            return CellError.Reference;
        }

        foreach (var entry in output.Values)
        {
            if (entry.DataField != data)
            {
                continue;
            }

            var conditions = entry.Conditions.Where(condition => !pivot.FilterFields.Contains(condition.Field) || pairs.Any(pair => string.Equals(pair.Field, condition.Name, StringComparison.CurrentCultureIgnoreCase))).ToList();
            if (conditions.Count != pairs.Count)
            {
                continue;
            }

            var all = pairs.All(pair => conditions.Any(condition =>
                string.Equals(condition.Name, pair.Field, StringComparison.CurrentCultureIgnoreCase)
                && (condition.Key == PivotItems.KeyOf(pair.Item) || string.Equals(LabelText(condition.Label, locale), LabelText(pair.Item, locale), StringComparison.CurrentCultureIgnoreCase))));
            if (all)
            {
                var cell = output.Cells[entry.Cell];
                return cell.Value.IsBlank || cell.Value is { IsText: true, Text.Length: 0 } ? CellError.Reference : cell.Value;
            }
        }

        return CellError.Reference;
    }
}
