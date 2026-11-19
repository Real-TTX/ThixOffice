using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>A sort key: a column of the range (offset from its first column, or a row with <see cref="SortOptions.ByColumns"/>).</summary>
public sealed record SortKey(int Offset, bool Descending = false);

public sealed record SortOptions(bool HasHeader = true, bool CaseSensitive = false, bool ByColumns = false);

/// <summary>Suchen und Ersetzen options.</summary>
public sealed record FindOptions(bool MatchCase = false, bool WholeCell = false, bool InFormulas = true, bool AllSheets = false, bool ByColumns = false);

public sealed partial class EditingSession
{
    // ---------------------------------------------------------------- sort

    /// <summary>
    /// Sortieren: the rows of <paramref name="range"/> (the header row stays) by several keys, stable; numbers before texts
    /// before booleans before errors, blanks always last. Cells move with their formats; formulas keep their relative
    /// references (they move with the row, as in Excel).
    /// </summary>
    public void Sort(CellRange range, IReadOnlyList<SortKey> keys, SortOptions? options = null)
    {
        options ??= new SortOptions();
        var sheet = ActiveSheet;
        if (keys.Count == 0)
        {
            return;
        }

        range = sheet.UsedRange?.Intersect(range) ?? range;
        if (sheet.Merges.Intersecting(range).Any(merge => !range.Contains(merge) || merge.RowCount > 1 && !options.ByColumns))
        {
            throw new BusinessException("Dazu müssen alle verbundenen Zellen dieselbe Größe haben.");
        }

        var body = options.ByColumns
            ? new CellRange(range.FirstRow, range.FirstColumn + (options.HasHeader ? 1 : 0), range.LastRow, range.LastColumn)
            : new CellRange(range.FirstRow + (options.HasHeader ? 1 : 0), range.FirstColumn, range.LastRow, range.LastColumn);
        if (body.FirstRow > body.LastRow || body.FirstColumn > body.LastColumn)
        {
            return;
        }

        var lines = options.ByColumns ? body.ColumnCount : body.RowCount;
        var order = Enumerable.Range(0, lines).ToList();
        CellValue KeyValue(int line, SortKey key) => options.ByColumns
            ? sheet.GetValue(body.FirstRow + key.Offset, body.FirstColumn + line)
            : sheet.GetValue(body.FirstRow + line, body.FirstColumn + key.Offset);
        order = order.OrderBy(line => line, Comparer<int>.Create((first, second) =>
        {
            foreach (var key in keys)
            {
                var left = KeyValue(first, key);
                var right = KeyValue(second, key);
                if (left.IsBlank || right.IsBlank)
                {
                    if (left.IsBlank != right.IsBlank)
                    {
                        return left.IsBlank ? 1 : -1;
                    }

                    continue;
                }

                var comparison = ArrayFunctions.CompareForSort(left, right);
                if (comparison == 0 && options.CaseSensitive && left.IsText && right.IsText)
                {
                    comparison = ValueComparer.CompareText(left.Text, right.Text, caseSensitive: true);
                }

                if (comparison != 0)
                {
                    return key.Descending ? -comparison : comparison;
                }
            }

            return 0;
        })).ToList();

        Execute("Sortieren", () =>
        {
            GuardArrays(sheet, body);
            // Lines move by position (never by remembered cell objects: undo may have recreated them).
            var forward = new int[lines];
            for (var position = 0; position < lines; position++)
            {
                forward[order[position]] = position;
            }

            var backward = order.ToArray();
            void Apply(int[] map)
            {
                var records = sheet.Store.Enumerate(body).ToList();
                foreach (var (row, column, _) in records)
                {
                    sheet.Store.Remove(row, column);
                }

                foreach (var (row, column, record) in records)
                {
                    if (options.ByColumns)
                    {
                        sheet.Store.Set(row, body.FirstColumn + map[column - body.FirstColumn], record);
                    }
                    else
                    {
                        sheet.Store.Set(body.FirstRow + map[row - body.FirstRow], column, record);
                    }
                }

                Workbook.Calculation.Rebuild();
                Workbook.NoteChanged(sheet, body);
            }

            Apply(forward);
            Record(new ActionStep(() => Apply(backward), () => Apply(forward), structural: true));
            var state = new SortState(range, keys.Select(key => new SortCondition(options.ByColumns
                ? new CellRange(body.FirstRow + key.Offset, body.FirstColumn, body.FirstRow + key.Offset, body.LastColumn)
                : new CellRange(body.FirstRow, body.FirstColumn + key.Offset, body.LastRow, body.FirstColumn + key.Offset), key.Descending)).ToList(), options.CaseSensitive, options.ByColumns);
            ChangeSheetState(sheet, () =>
            {
                if (sheet.AutoFilter is { } filter && filter.Range.Contains(range))
                {
                    sheet.AutoFilter = filter with { Sort = state };
                }
                else
                {
                    sheet.SortState = state;
                }
            });
        });
    }

    // ---------------------------------------------------------------- autofilter

    /// <summary>Filtern on/off: an autofilter over <paramref name="range"/> (the current region when null), or none.</summary>
    public void ToggleAutoFilter(CellRange? range = null)
    {
        var sheet = ActiveSheet;
        if (range is null && ActiveTable is { } table)
        {
            // In a table, Filtern switches its arrows.
            SetTableOptions(table.Name, current => current with { ShowFilterButton = !current.ShowFilterButton }, "Filtern");
            return;
        }

        Execute("Filtern", () =>
        {
            if (sheet.AutoFilter is { } existing)
            {
                ShowAllRows(sheet, existing.Range);
                ChangeSheetState(sheet, () => sheet.AutoFilter = null);
                return;
            }

            var area = range ?? Navigation.CurrentRegion(sheet, ActiveCell);
            ChangeSheetState(sheet, () => sheet.AutoFilter = new AutoFilter(area, []));
        });
    }

    /// <summary>Sets the filter of one column of the autofilter (null removes it) and hides the rows that do not match.</summary>
    public void SetFilter(int columnOffset, FilterColumn? filter)
    {
        var sheet = ActiveSheet;
        if (FilterTarget() is not { } target)
        {
            throw new BusinessException("Auf diesem Blatt ist kein Filter aktiv.");
        }

        var autoFilter = target.Filter;
        Execute("Filtern", () =>
        {
            var columns = autoFilter.Columns.Where(column => column.ColumnOffset != columnOffset).ToList();
            if (filter is not null)
            {
                columns.Add(filter with { ColumnOffset = columnOffset, Xml = null });
            }

            var updated = autoFilter with { Columns = columns.OrderBy(column => column.ColumnOffset).ToList() };
            ChangeSheetState(sheet, () => target.Set(updated));
            ApplyFilter(sheet, updated);
        });
    }

    /// <summary>Alle anzeigen: removes every column filter.</summary>
    public void ClearFilters()
    {
        var sheet = ActiveSheet;
        if (FilterTarget() is not { } target)
        {
            return;
        }

        var autoFilter = target.Filter;
        Execute("Filter löschen", () =>
        {
            ChangeSheetState(sheet, () => target.Set(autoFilter with { Columns = [] }));
            ShowAllRows(sheet, autoFilter.Range);
        });
    }

    /// <summary>The distinct displayed values of a filter column (the filter list), sorted, blank as "".</summary>
    public IReadOnlyList<string> FilterValues(int columnOffset)
    {
        var sheet = ActiveSheet;
        if (ActiveFilter is not { } filter)
        {
            return [];
        }

        var column = filter.Range.FirstColumn + columnOffset;
        var values = new SortedSet<string>(StringComparer.CurrentCultureIgnoreCase);
        for (var row = filter.Range.FirstRow + 1; row <= filter.Range.LastRow; row++)
        {
            values.Add(DisplayText(sheet, row, column));
        }

        return values.ToList();
    }

    /// <summary>
    /// The filter the arrows and the filter commands act on: the table's around the active cell (its range: header and data
    /// rows), else the sheet's autofilter; null when there is none.
    /// </summary>
    public AutoFilter? ActiveFilter => FilterTarget()?.Filter;

    private (AutoFilter Filter, Action<AutoFilter> Set)? FilterTarget()
    {
        var sheet = ActiveSheet;
        if (ActiveTable is { HasHeader: true, ShowFilterButton: true } table)
        {
            var name = table.Name;
            var filter = (table.Filter ?? new AutoFilter(table.FilterRange, [])) with { Range = table.FilterRange };
            return (filter, updated =>
            {
                var index = sheet.Tables.FindIndex(entry => entry.Name == name);
                if (index >= 0)
                {
                    sheet.Tables[index] = sheet.Tables[index] with { Filter = updated };
                }
            });
        }

        return sheet.AutoFilter is { } autoFilter ? (autoFilter, updated => sheet.AutoFilter = updated) : null;
    }

    private string DisplayText(Worksheet sheet, int row, int column) =>
        NumberFormatter.Format(sheet.GetValue(row, column), sheet.GetStyle(row, column).NumberFormat, Locale, Workbook.Date1904).Text;

    private void ApplyFilter(Worksheet sheet, AutoFilter filter)
    {
        var range = filter.Range;
        var axis = sheet.Rows;
        var before = axis.Snapshot();
        var size = axis.DefaultSize;
        var visible = new bool[range.RowCount - 1];
        Array.Fill(visible, true);
        foreach (var column in filter.Columns)
        {
            var values = Enumerable.Range(range.FirstRow + 1, range.RowCount - 1)
                .Select(row => (Row: row, Value: sheet.GetValue(row, range.FirstColumn + column.ColumnOffset))).ToList();
            var matcher = FilterMatcher(sheet, column, values.Select(entry => entry.Value).ToList(), range.FirstColumn + column.ColumnOffset);
            for (var index = 0; index < values.Count; index++)
            {
                visible[index] &= matcher(values[index].Row, values[index].Value);
            }
        }

        axis.Update(range.FirstRow + 1, range.LastRow, info => info);
        for (var index = 0; index < visible.Length; index++)
        {
            var row = range.FirstRow + 1 + index;
            axis.Set(row, axis[row] with { Hidden = !visible[index] });
        }

        var after = axis.Snapshot();
        Record(new ActionStep(() => axis.Restore(before, size), () => axis.Restore(after, size)));
        Workbook.NoteStructureChanged();
    }

    private Func<int, CellValue, bool> FilterMatcher(Worksheet sheet, FilterColumn column, List<CellValue> values, int sheetColumn)
    {
        if (column.Values is { } list)
        {
            var allowed = new HashSet<string>(list, StringComparer.CurrentCultureIgnoreCase);
            return (row, value) => value.IsBlank ? column.IncludeBlank || allowed.Contains(string.Empty) : allowed.Contains(DisplayText(sheet, row, sheetColumn));
        }

        if (column.Custom.Count > 0)
        {
            var criteria = column.Custom.Select(condition => Criterion.Parse(CellValue.FromText(condition.Operator switch
            {
                FilterOperator.NotEqual => "<>",
                FilterOperator.GreaterThan => ">",
                FilterOperator.GreaterThanOrEqual => ">=",
                FilterOperator.LessThan => "<",
                FilterOperator.LessThanOrEqual => "<=",
                _ => "=",
            } + condition.Value), Locale, Workbook.Date1904)).ToList();
            return (_, value) => column.CustomAnd ? criteria.All(criterion => criterion.Matches(value)) : criteria.Any(criterion => criterion.Matches(value));
        }

        if (column.Top is { } top)
        {
            var numbers = values.Where(value => value.IsNumber).Select(value => value.Number).OrderByDescending(number => number).ToList();
            if (column.Bottom)
            {
                numbers.Reverse();
            }

            var count = column.TopPercent ? (int)Math.Ceiling(numbers.Count * top / 100) : (int)top;
            var kept = numbers.Take(Math.Max(0, count)).ToHashSet();
            return (_, value) => value.IsNumber && kept.Contains(value.Number);
        }

        if (column.Dynamic is "aboveAverage" or "belowAverage")
        {
            var numbers = values.Where(value => value.IsNumber).Select(value => value.Number).ToList();
            var average = numbers.Count > 0 ? numbers.Average() : 0;
            return (_, value) => value.IsNumber && (column.Dynamic == "aboveAverage" ? value.Number > average : value.Number < average);
        }

        return (_, _) => true;
    }

    private void ShowAllRows(Worksheet sheet, CellRange range)
    {
        var axis = sheet.Rows;
        var before = axis.Snapshot();
        var size = axis.DefaultSize;
        axis.Update(range.FirstRow + 1, range.LastRow, info => info with { Hidden = false });
        var after = axis.Snapshot();
        Record(new ActionStep(() => axis.Restore(before, size), () => axis.Restore(after, size)));
        Workbook.NoteStructureChanged();
    }

    // ---------------------------------------------------------------- find and replace

    /// <summary>All cells that contain <paramref name="text"/> (* and ? wildcards, ~ escapes) in sheet order.</summary>
    public IReadOnlyList<(Worksheet Sheet, CellAddress Cell)> FindAll(string text, FindOptions? options = null)
    {
        options ??= new FindOptions();
        var results = new List<(Worksheet, CellAddress)>();
        var sheets = options.AllSheets ? Workbook.Sheets : [ActiveSheet];
        foreach (var sheet in sheets)
        {
            var cells = sheet.GetCells().Where(entry => !entry.Cell.IsEmpty);
            if (options.ByColumns)
            {
                cells = cells.OrderBy(entry => entry.Column).ThenBy(entry => entry.Row);
            }

            foreach (var entry in cells)
            {
                if (Matches(SearchText(sheet, entry, options), text, options))
                {
                    results.Add((sheet, entry.Address));
                }
            }
        }

        return results;
    }

    /// <summary>Weitersuchen: selects the next match after the active cell (wrapping); null when there is none.</summary>
    public CellAddress? FindNext(string text, FindOptions? options = null, bool backwards = false)
    {
        var all = FindAll(text, options);
        if (all.Count == 0)
        {
            return null;
        }

        var sheet = ActiveSheet;
        var active = ActiveCell;
        var sheetIndex = Workbook.IndexOf(sheet);
        (Worksheet Sheet, CellAddress Cell) next;
        if (!backwards)
        {
            next = all.FirstOrDefault(entry => Workbook.IndexOf(entry.Sheet) > sheetIndex
                || entry.Sheet == sheet && Compare(entry.Cell, active, options?.ByColumns ?? false) > 0);
            if (next.Sheet is null)
            {
                next = all[0];
            }
        }
        else
        {
            next = all.LastOrDefault(entry => Workbook.IndexOf(entry.Sheet) < sheetIndex
                || entry.Sheet == sheet && Compare(entry.Cell, active, options?.ByColumns ?? false) < 0);
            if (next.Sheet is null)
            {
                next = all[^1];
            }
        }

        ActivateSheet(Workbook.IndexOf(next.Sheet));
        Select(next.Cell);
        return next.Cell;
    }

    private static int Compare(CellAddress first, CellAddress second, bool byColumns) => byColumns
        ? (first.Column != second.Column ? first.Column.CompareTo(second.Column) : first.Row.CompareTo(second.Row))
        : first.CompareTo(second);

    /// <summary>Alle ersetzen: in constants and (with InFormulas) in formula texts; returns how many cells changed.</summary>
    public int ReplaceAll(string text, string replacement, FindOptions? options = null)
    {
        options ??= new FindOptions();
        var matches = FindAll(text, options);
        var count = 0;
        Execute("Ersetzen", () =>
        {
            foreach (var (sheet, cell) in matches)
            {
                if (ReplaceIn(sheet, cell, text, replacement, options))
                {
                    count++;
                }
            }
        });
        return count;
    }

    /// <summary>Ersetzen: the active cell when it matches, then the next match is selected.</summary>
    public bool Replace(string text, string replacement, FindOptions? options = null)
    {
        options ??= new FindOptions();
        var sheet = ActiveSheet;
        var cell = ActiveCell;
        var replaced = false;
        if (Matches(SearchText(sheet, new CellEntry(cell.Row, cell.Column, sheet.GetCell(cell)), options), text, options))
        {
            Execute("Ersetzen", () => replaced = ReplaceIn(sheet, cell, text, replacement, options));
        }

        FindNext(text, options);
        return replaced;
    }

    private bool ReplaceIn(Worksheet sheet, CellAddress cell, string text, string replacement, FindOptions options)
    {
        var content = sheet.GetCell(cell);
        if (content.IsSpilled)
        {
            return false;
        }

        var source = SearchText(sheet, new CellEntry(cell.Row, cell.Column, content), options);
        var result = options.WholeCell ? replacement : ReplaceText(source, text, replacement, options.MatchCase);
        if (result == source)
        {
            return false;
        }

        EnterInto(sheet, cell, content.Formula is null && content.Value.IsText && !result.StartsWith('=') ? (ValueParser.ParseInput(result, Locale).Kind == InputKind.Text ? result : "'" + result) : result);
        return true;
    }

    private string SearchText(Worksheet sheet, CellEntry entry, FindOptions options)
    {
        if (options.InFormulas && entry.Cell.Formula is { } formula)
        {
            return "=" + FormulaFormatter.Format(formula, Workbook, entry.Address, Syntax);
        }

        if (options.InFormulas && entry.Cell.Formula is null && !entry.Cell.IsSpilled)
        {
            return EditText(entry.Cell.Value, sheet.GetStyle(entry.Row, entry.Column)).TrimStart('\'');
        }

        return DisplayText(sheet, entry.Row, entry.Column);
    }

    private static bool Matches(string candidate, string pattern, FindOptions options)
    {
        if (pattern.Length == 0)
        {
            return false;
        }

        var comparison = options.MatchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;
        if (!Wildcard.HasWildcards(pattern))
        {
            return options.WholeCell ? string.Equals(candidate, pattern, comparison) : candidate.Contains(pattern, comparison);
        }

        var full = options.WholeCell ? pattern : "*" + pattern + "*";
        return Wildcard.IsMatch(full, candidate) && (!options.MatchCase || candidate.Contains(pattern.Replace("*", string.Empty, StringComparison.Ordinal).Replace("?", string.Empty, StringComparison.Ordinal), StringComparison.Ordinal));
    }

    private static string ReplaceText(string source, string pattern, string replacement, bool matchCase)
    {
        if (!Wildcard.HasWildcards(pattern))
        {
            return source.Replace(pattern, replacement, matchCase ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        // Wildcards: replace the shortest match at each position, left to right.
        var builder = new System.Text.StringBuilder();
        var index = 0;
        while (index < source.Length)
        {
            var matched = -1;
            for (var end = index + 1; end <= source.Length; end++)
            {
                if (Wildcard.IsMatch(pattern, source[index..end]))
                {
                    matched = end;
                    break;
                }
            }

            if (matched < 0)
            {
                builder.Append(source[index]);
                index++;
                continue;
            }

            builder.Append(replacement);
            index = matched;
        }

        return builder.ToString();
    }

    // ---------------------------------------------------------------- notes and links

    public void SetComment(CellAddress cell, CellComment? comment)
    {
        var sheet = ActiveSheet;
        Execute(comment is null ? "Notiz löschen" : "Notiz", () => ChangeSheetState(sheet, () =>
        {
            if (comment is null)
            {
                sheet.Comments.Remove(cell);
            }
            else
            {
                sheet.Comments[cell] = comment;
            }
        }));
    }

    public void SetHyperlink(CellRange range, Hyperlink? hyperlink)
    {
        var sheet = ActiveSheet;
        Execute(hyperlink is null ? "Link entfernen" : "Link", () => ChangeSheetState(sheet, () =>
        {
            sheet.Hyperlinks.RemoveAll(existing => existing.Range.Intersects(range));
            if (hyperlink is not null)
            {
                sheet.Hyperlinks.Add(hyperlink with { Range = range });
            }
        }));
    }

    public void AddValidation(DataValidation validation)
    {
        var sheet = ActiveSheet;
        Execute("Datenüberprüfung", () => ChangeSheetState(sheet, () =>
        {
            foreach (var range in validation.Ranges)
            {
                for (var index = sheet.Validations.Count - 1; index >= 0; index--)
                {
                    if (sheet.Validations[index].Ranges.Any(existing => range.Contains(existing)))
                    {
                        sheet.Validations.RemoveAt(index);
                    }
                }
            }

            sheet.Validations.Add(validation);
        }));
    }

    public void AddConditionalFormat(ConditionalFormat format)
    {
        var sheet = ActiveSheet;
        Execute("Bedingte Formatierung", () => ChangeSheetState(sheet, () => sheet.ConditionalFormats.Add(format)));
    }
}
