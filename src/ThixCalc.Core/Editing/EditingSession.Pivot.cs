using System.Collections.Immutable;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixCalc.Core.Model.Pivot;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>Where Einfügen › PivotTable puts the new table: a new sheet (at A3) or a cell of an existing sheet.</summary>
public sealed record PivotDestination(bool NewSheet, string? Sheet = null, CellAddress Cell = default);

/// <summary>
/// PivotTables (Einfügen › PivotTable, the field list, PivotTable-Analyse, Entwurf): creating one from a range or a table, every
/// change of fields, areas, value fields, filters, grouping, sorting, layout and style, Aktualisieren, Datenquelle ändern,
/// Verschieben, Löschen and Details anzeigen. Each is one undo step: the PivotTable record (immutable) is swapped in the sheet's
/// list and its cells are drawn anew – ordinary cells (formulas may refer to them, PIVOTDATENZUORDNEN reads them) that the
/// user cannot edit one by one.
/// </summary>
public sealed partial class EditingSession
{
    private bool _writingPivot;

    /// <summary>The PivotTable around the active cell, or null (the field list and the PivotTable tabs show while there is one).</summary>
    public PivotTable? ActivePivot => ActiveSheet.PivotTables.Count == 0 ? null : ActiveSheet.PivotAt(ActiveCell.Row, ActiveCell.Column);

    /// <summary>The source PivotTable erstellen proposes: the active cell's table, the selection, else the current region.</summary>
    public string ProposedPivotSource()
    {
        if (ActiveTable is { } table)
        {
            return table.Name;
        }

        var range = Selection.IsSingleCell ? Navigation.CurrentRegion(ActiveSheet, ActiveCell) : Selection;
        return $"{SheetPrefix(ActiveSheet.Name)}!{range.ToAbsoluteString()}";
    }

    /// <summary>Reads "Tabelle1!$A$1:$D$20", "A1:D20" (the active sheet) or a table's name into a source.</summary>
    public (string Sheet, CellRange Range, string? Table) ParsePivotSource(string text)
    {
        text = text.Trim().TrimStart('=');
        foreach (var sheet in Workbook.Sheets)
        {
            if (sheet.Tables.FirstOrDefault(table => string.Equals(table.Name, text, StringComparison.OrdinalIgnoreCase)) is { } table)
            {
                var range = table.HasTotals ? new CellRange(table.Range.FirstRow, table.Range.FirstColumn, table.Range.LastRow - 1, table.Range.LastColumn) : table.Range;
                return (sheet.Name, range, table.Name);
            }
        }

        var sheetName = ActiveSheet.Name;
        var reference = text;
        var bang = text.LastIndexOf('!');
        if (bang > 0)
        {
            sheetName = text[..bang].Trim('\'').Replace("''", "'", StringComparison.Ordinal);
            reference = text[(bang + 1)..];
        }

        if (Workbook.FindSheet(sheetName) is null || CellRange.TryParse(reference.Replace("$", string.Empty, StringComparison.Ordinal)) is not { } parsed)
        {
            throw new BusinessException("Der Bezug der Datenquelle ist ungültig.", "source");
        }

        if (parsed.IsWholeColumns || parsed.IsWholeRows)
        {
            var used = Workbook.FindSheet(sheetName)!.DataRange ?? parsed;
            parsed = new CellRange(Math.Max(parsed.FirstRow, used.FirstRow), parsed.FirstColumn, Math.Min(parsed.LastRow, used.LastRow), Math.Min(parsed.LastColumn, used.LastColumn));
        }

        return (Workbook.FindSheet(sheetName)!.Name, parsed, null);
    }

    /// <summary>Einfügen › PivotTable: an empty PivotTable over the source (its fields wait in the field list).</summary>
    public PivotTable CreatePivotTable(string source, PivotDestination destination)
    {
        var (sourceSheet, range, table) = ParsePivotSource(source);
        if (destination.NewSheet)
        {
            GuardStructure();
        }

        var cache = PivotCache.Build(Workbook, NextPivotCacheId(), sourceSheet, range, table);
        PivotTable? created = null;
        Execute("PivotTable einfügen", () =>
        {
            Worksheet sheet;
            CellAddress location;
            if (destination.NewSheet)
            {
                var position = Workbook.ActiveSheetIndex;
                sheet = Workbook.AddSheet(Workbook.NextSheetName(Locale.SheetBaseName), position);
                var added = sheet;
                Record(new ActionStep(() => RemoveSheetRaw(added), () => Workbook.InsertExistingSheet(added, position), structural: true));
                location = new CellAddress(2, 0);
            }
            else
            {
                sheet = (destination.Sheet is { } name ? Workbook.FindSheet(name) : ActiveSheet) ?? throw new BusinessException("Der Zielbezug ist ungültig.", "destination");
                location = destination.Cell;
            }

            AddCache(cache);
            var fields = Enumerable.Range(0, cache.Fields.Count).Select(index => new PivotField(index)).ToImmutableArray();
            created = new PivotTable(NextPivotName(), cache.Id, location) { Fields = fields };
            Place(sheet, null, created);
            Workbook.ActiveSheetIndex = Workbook.IndexOf(sheet);
            sheet.View = sheet.View with { ActiveCell = location, Selection = [CellRange.Cell(location)] };
        });
        return FindPivot(created!.Name)!.Value.Pivot;
    }

    /// <summary>Changes a PivotTable (one undo step labelled <paramref name="label"/>) and draws it anew.</summary>
    public PivotTable ChangePivot(PivotTable pivot, Func<PivotTable, PivotTable> change, string label = "PivotTable ändern")
    {
        var (sheet, current) = FindPivot(pivot.Name) ?? throw new BusinessException("Die PivotTable gibt es nicht mehr.");
        RefuseUnsupported(current);
        var changed = change(current) with { SourceXml = null };
        Execute(label, () => Place(sheet, current, changed));
        return FindPivot(pivot.Name)!.Value.Pivot;
    }

    /// <summary>
    /// The field list's check box: a numeric field goes to Werte (Summe), others to Zeilen – like Excel – or to <paramref name="axis"/>.
    /// </summary>
    public PivotTable AddPivotField(PivotTable pivot, int field, PivotAxis? axis = null, bool asValue = false, int? position = null)
    {
        var cache = CacheOf(pivot);
        var numeric = cache.Fields[field].IsNumeric && !cache.Fields[field].IsDate;
        if (asValue || (axis is null && numeric))
        {
            return ChangePivot(pivot, table => table with { DataFields = Insert(table.DataFields, NewDataField(table, cache, field, numeric), position) }, "Wertfeld hinzufügen");
        }

        var target = axis ?? PivotAxis.Row;
        return ChangePivot(pivot, table => MoveField(table, field, target, position), "Feld hinzufügen");
    }

    /// <summary>Moves a field to an area (or out of the pivot with <see cref="PivotAxis.None"/>) at a position.</summary>
    public PivotTable MovePivotField(PivotTable pivot, int field, PivotAxis axis, int? position = null) =>
        ChangePivot(pivot, table => MoveField(table, field, axis, position), axis == PivotAxis.None ? "Feld entfernen" : "Feld verschieben");

    /// <summary>Unticks a field in the field list: off every area and out of the value fields.</summary>
    public PivotTable RemovePivotField(PivotTable pivot, int field) =>
        ChangePivot(pivot, table => MoveField(table, field, PivotAxis.None, null) with { DataFields = [.. table.DataFields.Where(data => data.Field != field)] }, "Feld entfernen");

    public PivotTable ChangePivotDataField(PivotTable pivot, int index, Func<PivotDataField, PivotDataField> change) =>
        ChangePivot(pivot, table => table with { DataFields = table.DataFields.SetItem(index, change(table.DataFields[index])) }, "Wertfeldeinstellungen");

    public PivotTable RemovePivotDataField(PivotTable pivot, int index) =>
        ChangePivot(pivot, table =>
        {
            var data = table.DataFields.RemoveAt(index);
            return data.Length > 1 ? table with { DataFields = data } : table with
            {
                DataFields = data,
                RowFields = [.. table.RowFields.Where(field => field != PivotTable.ValuesField)],
                ColumnFields = [.. table.ColumnFields.Where(field => field != PivotTable.ValuesField)],
            };
        }, "Wertfeld entfernen");

    public PivotTable MovePivotDataField(PivotTable pivot, int index, int position) =>
        ChangePivot(pivot, table =>
        {
            var field = table.DataFields[index];
            var list = table.DataFields.RemoveAt(index);
            return table with { DataFields = list.Insert(Math.Clamp(position, 0, list.Length), field) };
        }, "Wertfeld verschieben");

    public PivotTable ChangePivotField(PivotTable pivot, int field, Func<PivotField, PivotField> change, string label = "Feldeinstellungen") =>
        ChangePivot(pivot, table => table.WithField(field, change), label);

    /// <summary>Gruppieren: dates by Jahre/Quartale/Monate/Tage, numbers by Starten/Beenden/Nach.</summary>
    public PivotTable GroupPivotField(PivotTable pivot, int field, PivotDateGroups dates, PivotNumberGroup? numbers = null)
    {
        var cache = CacheOf(pivot);
        if (dates != PivotDateGroups.None && !cache.Fields[field].IsDate)
        {
            throw new BusinessException("Diese Auswahl kann nicht gruppiert werden: das Feld enthält keine Datumswerte.");
        }

        if (numbers is not null && (numbers.Interval <= 0 || numbers.End < numbers.Start || !cache.Fields[field].IsNumeric))
        {
            throw new BusinessException(numbers.Interval <= 0 ? "Die Schrittweite muss größer als 0 sein." : "Diese Auswahl kann nicht gruppiert werden.");
        }

        return ChangePivotField(pivot, field, settings => settings with { DateGroups = dates, NumberGroup = numbers, HiddenItems = [] }, "Gruppieren");
    }

    public PivotTable UngroupPivotField(PivotTable pivot, int field) =>
        ChangePivotField(pivot, field, settings => settings with { DateGroups = PivotDateGroups.None, NumberGroup = null, HiddenItems = [] }, "Gruppierung aufheben");

    /// <summary>Aktualisieren: reads the source again; fields are matched by name (new ones are added, gone ones leave the table).</summary>
    public PivotTable RefreshPivot(PivotTable pivot)
    {
        var (sheet, current) = FindPivot(pivot.Name) ?? throw new BusinessException("Die PivotTable gibt es nicht mehr.");
        RefuseUnsupported(current);
        var old = CacheOf(current);
        var fresh = PivotCache.Build(Workbook, old.Id, old.CurrentSheet(Workbook), old.CurrentRange(Workbook), old.SourceTable);
        Execute("Aktualisieren", () => Rebind(old, fresh));
        return FindPivot(pivot.Name)!.Value.Pivot;
    }

    /// <summary>Alle aktualisieren: every PivotTable of the workbook (one step).</summary>
    public void RefreshAllPivots()
    {
        var caches = Workbook.PivotCaches.Where(cache => Workbook.Sheets.SelectMany(sheet => sheet.PivotTables).Any(pivot => pivot.CacheId == cache.Id && pivot.Unsupported is null)).ToList();
        if (caches.Count == 0)
        {
            return;
        }

        var fresh = caches.Select(cache => (cache, PivotCache.Build(Workbook, cache.Id, cache.CurrentSheet(Workbook), cache.CurrentRange(Workbook), cache.SourceTable))).ToList();
        Execute("Alle aktualisieren", () =>
        {
            foreach (var (old, refreshed) in fresh)
            {
                Rebind(old, refreshed);
            }
        });
    }

    /// <summary>Datenquelle ändern: a new source for the table's cache (other tables on the same cache follow).</summary>
    public PivotTable ChangePivotSource(PivotTable pivot, string source)
    {
        var (_, current) = FindPivot(pivot.Name) ?? throw new BusinessException("Die PivotTable gibt es nicht mehr.");
        RefuseUnsupported(current);
        var (sheet, range, table) = ParsePivotSource(source);
        var old = CacheOf(current);
        var fresh = PivotCache.Build(Workbook, old.Id, sheet, range, table);
        Execute("Datenquelle ändern", () => Rebind(old, fresh));
        return FindPivot(pivot.Name)!.Value.Pivot;
    }

    /// <summary>Verschieben: to another cell (or sheet).</summary>
    public PivotTable MovePivot(PivotTable pivot, string sheetName, CellAddress cell)
    {
        var (sheet, current) = FindPivot(pivot.Name) ?? throw new BusinessException("Die PivotTable gibt es nicht mehr.");
        var target = Workbook.FindSheet(sheetName) ?? throw new BusinessException("Der Zielbezug ist ungültig.", "destination");
        Execute("PivotTable verschieben", () =>
        {
            Clear(sheet, current);
            ChangeSheetState(sheet, () => sheet.PivotTables.Remove(current));
            var moved = current with { Location = cell, SourceXml = null, PartUri = ReferenceEquals(sheet, target) ? current.PartUri : null };
            Place(target, null, moved);
        });
        return FindPivot(pivot.Name)!.Value.Pivot;
    }

    /// <summary>Löschen: the table and its cells go (the cache too when no other table uses it).</summary>
    public void DeletePivot(PivotTable pivot)
    {
        var (sheet, current) = FindPivot(pivot.Name) ?? throw new BusinessException("Die PivotTable gibt es nicht mehr.");
        Execute("PivotTable löschen", () =>
        {
            Clear(sheet, current);
            ChangeSheetState(sheet, () => sheet.PivotTables.Remove(current));
            if (!Workbook.Sheets.SelectMany(other => other.PivotTables).Any(other => other.CacheId == current.CacheId) && Workbook.FindPivotCache(current.CacheId) is { } cache)
            {
                var caches = Workbook.PivotCaches;
                Record(new ActionStep(() => caches.Add(cache), () => caches.Remove(cache)));
                caches.Remove(cache);
            }
        });
    }

    /// <summary>
    /// Details anzeigen (double click on a value): a new sheet with the records behind the value, as a table – Excel's drill-down.
    /// </summary>
    public Worksheet ShowPivotDetails(CellAddress cell)
    {
        var sheet = ActiveSheet;
        var pivot = sheet.PivotAt(cell.Row, cell.Column) ?? throw new BusinessException("Die Zelle gehört zu keiner PivotTable.");
        var output = sheet.OutputOf(pivot) ?? throw new BusinessException("Aktualisieren Sie die PivotTable zuerst (ihre Daten sind nicht gespeichert).");
        var entry = output.EntryAt(cell) ?? throw new BusinessException("Details gibt es nur zu den Werten einer PivotTable.");
        GuardStructure();
        var cache = CacheOf(pivot);
        var records = PivotEngine.RecordsOf(pivot, cache, entry);
        Worksheet? created = null;
        Execute("Details anzeigen", () =>
        {
            var position = Workbook.ActiveSheetIndex;
            created = Workbook.AddSheet(Workbook.NextSheetName(Locale.SheetBaseName), position);
            var added = created;
            Record(new ActionStep(() => RemoveSheetRaw(added), () => Workbook.InsertExistingSheet(added, position), structural: true));
            for (var field = 0; field < cache.Fields.Count; field++)
            {
                SetCell(created, 0, field, new Cell(CellValue.FromText(cache.Fields[field].Name)));
                var format = cache.Fields[field].NumberFormat;
                var style = format is null ? 0 : Workbook.Styles.Intern(Workbook.Styles[0] with { NumberFormat = format });
                for (var index = 0; index < records.Count; index++)
                {
                    SetCell(created, index + 1, field, new Cell(cache.Value(field, records[index]), null, style));
                }
            }

            Workbook.ActiveSheetIndex = Workbook.IndexOf(created);
            created.View = created.View with { ActiveCell = default, Selection = [CellRange.Cell(default)] };
            if (records.Count > 0 && cache.Fields.Count > 0)
            {
                var range = new CellRange(0, 0, records.Count, cache.Fields.Count - 1);
                var table = new TableDefinition(NextTableName(), range, [.. cache.Fields.Select(field => field.Name)]) { Style = TableStyles.DefaultStyle };
                ChangeSheetState(created, () => created.Tables.Add(table));
                TablesChanged();
            }
        });
        return created!;
    }

    /// <summary>Formatvorlage, Berichtslayout, Teil-/Gesamtergebnisse, Überschriften, gebänderte Zeilen: the table's options.</summary>
    public PivotTable SetPivotOptions(PivotTable pivot, Func<PivotTable, PivotTable> change, string label = "PivotTable-Optionen") => ChangePivot(pivot, change, label);

    // ---------------------------------------------------------------- helpers

    internal PivotCache CacheOf(PivotTable pivot) =>
        Workbook.FindPivotCache(pivot.CacheId) ?? throw new BusinessException("Der PivotCache der PivotTable fehlt.");

    /// <summary>The sheet and current record of a PivotTable by name.</summary>
    public (Worksheet Sheet, PivotTable Pivot)? FindPivot(string name)
    {
        foreach (var sheet in Workbook.Sheets)
        {
            if (sheet.PivotTables.FirstOrDefault(pivot => string.Equals(pivot.Name, name, StringComparison.OrdinalIgnoreCase)) is { } pivot)
            {
                return (sheet, pivot);
            }
        }

        return null;
    }

    private static void RefuseUnsupported(PivotTable pivot)
    {
        if (pivot.Unsupported is { } reason)
        {
            throw new BusinessException($"Diese PivotTable kann ThixCalc nur anzeigen und erhalten, nicht ändern ({reason}).");
        }
    }

    private int NextPivotCacheId() => Workbook.PivotCaches.Select(cache => cache.Id).DefaultIfEmpty(0).Max() + 1;

    private string NextPivotName()
    {
        var names = Workbook.Sheets.SelectMany(sheet => sheet.PivotTables).Select(pivot => pivot.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var number = 1; ; number++)
        {
            if (!names.Contains($"PivotTable{number}"))
            {
                return $"PivotTable{number}";
            }
        }
    }

    private static string SheetPrefix(string name) =>
        name.All(character => char.IsLetterOrDigit(character) || character == '_') && !char.IsDigit(name[0]) ? name : "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";

    private void AddCache(PivotCache cache)
    {
        var caches = Workbook.PivotCaches;
        caches.Add(cache);
        Record(new ActionStep(() => caches.Remove(cache), () => caches.Add(cache)));
    }

    /// <summary>A refreshed cache replaces the old one; every table on it is mapped to the new fields by name and drawn anew.</summary>
    private void Rebind(PivotCache old, PivotCache fresh)
    {
        var caches = Workbook.PivotCaches;
        var index = caches.IndexOf(old);
        caches[index] = fresh;
        Record(new ActionStep(() => caches[caches.IndexOf(fresh)] = old, () => caches[caches.IndexOf(old)] = fresh));
        var map = Enumerable.Range(0, old.Fields.Count).ToDictionary(field => field, field => fresh.IndexOf(old.Fields[field].Name));
        foreach (var sheet in Workbook.Sheets)
        {
            foreach (var pivot in sheet.PivotTables.Where(pivot => pivot.CacheId == old.Id).ToList())
            {
                Place(sheet, pivot, Remap(pivot, fresh, map));
            }
        }
    }

    private static PivotTable Remap(PivotTable pivot, PivotCache fresh, Dictionary<int, int> map)
    {
        int To(int field) => field == PivotTable.ValuesField ? field : map.GetValueOrDefault(field, -1);
        var fields = Enumerable.Range(0, fresh.Fields.Count).Select(index =>
        {
            var source = map.FirstOrDefault(entry => entry.Value == index);
            return map.ContainsValue(index) && source.Key < pivot.Fields.Length ? pivot.Fields[source.Key] with { Index = index } : new PivotField(index);
        }).ToImmutableArray();
        var dates = fields.Select(field => field.DateGroups != PivotDateGroups.None && !fresh.Fields[field.Index].IsDate ? field with { DateGroups = PivotDateGroups.None } : field).ToImmutableArray();
        return pivot with
        {
            Fields = dates,
            RowFields = [.. pivot.RowFields.Select(To).Where(field => field != -1)],
            ColumnFields = [.. pivot.ColumnFields.Select(To).Where(field => field != -1)],
            FilterFields = [.. pivot.FilterFields.Select(To).Where(field => field != -1)],
            DataFields = [.. pivot.DataFields.Where(data => To(data.Field) != -1).Select(data => data with { Field = To(data.Field), BaseField = data.BaseField < 0 ? -1 : To(data.BaseField) })],
            SourceXml = null,
        };
    }

    private static PivotDataField NewDataField(PivotTable table, PivotCache cache, int field, bool numeric)
    {
        var function = numeric ? PivotFunction.Sum : PivotFunction.Count;
        var name = PivotEngine.DataFieldName(function, cache.Fields[field].Name);
        var taken = table.DataFields.Select(data => data.Name).ToHashSet(StringComparer.CurrentCultureIgnoreCase);
        var unique = name;
        for (var number = 2; taken.Contains(unique); number++)
        {
            unique = $"{name}{number}";
        }

        return new PivotDataField(field, unique, function);
    }

    private static ImmutableArray<T> Insert<T>(ImmutableArray<T> list, T item, int? position) =>
        list.Insert(Math.Clamp(position ?? list.Length, 0, list.Length), item);

    /// <summary>A field leaves its area and enters another (Σ Werte only between Zeilen and Spalten).</summary>
    private static PivotTable MoveField(PivotTable table, int field, PivotAxis axis, int? position)
    {
        var rows = table.RowFields.Remove(field);
        var columns = table.ColumnFields.Remove(field);
        var filters = table.FilterFields.Remove(field);
        if (field == PivotTable.ValuesField && axis is PivotAxis.Filter or PivotAxis.None)
        {
            return table;
        }

        switch (axis)
        {
            case PivotAxis.Row:
                rows = Insert(rows, field, position);
                break;
            case PivotAxis.Column:
                columns = Insert(columns, field, position);
                break;
            case PivotAxis.Filter:
                filters = Insert(filters, field, position);
                break;
        }

        var result = table with { RowFields = rows, ColumnFields = columns, FilterFields = filters };
        return field == PivotTable.ValuesField ? result : result.WithField(field, settings => settings with
        {
            Axis = axis,
            SelectedItem = axis == PivotAxis.Filter ? settings.SelectedItem : null,
        });
    }

    /// <summary>Draws <paramref name="pivot"/> into <paramref name="sheet"/> in place of <paramref name="old"/> (null: new), inside the open command.</summary>
    private void Place(Worksheet sheet, PivotTable? old, PivotTable pivot)
    {
        var cache = CacheOf(pivot);
        var output = PivotEngine.Compute(pivot, cache, Locale);
        var range = output?.Range ?? EmptyRange(pivot);
        if (range.LastRow >= SheetLimits.MaxRows || range.LastColumn >= SheetLimits.MaxColumns)
        {
            throw new BusinessException("Die PivotTable passt nicht mehr auf das Blatt.");
        }

        foreach (var other in sheet.PivotTables)
        {
            if (!ReferenceEquals(other, old) && other.Range is { } taken && taken.Intersects(range))
            {
                throw new BusinessException("Eine PivotTable kann keine andere PivotTable überlappen.");
            }
        }

        if (sheet.Tables.Any(table => table.Range.Intersects(range)))
        {
            throw new BusinessException("Eine PivotTable kann keine Tabelle überlappen. Verschieben Sie die PivotTable oder die Tabelle.");
        }

        if (sheet.Merges.Intersecting(range).Any())
        {
            throw new BusinessException("Eine PivotTable kann keine verbundenen Zellen überlappen.");
        }

        if (old is not null)
        {
            Clear(sheet, old, keep: output?.Cells.Keys.ToHashSet());
        }

        _writingPivot = true;
        try
        {
            if (output is not null)
            {
                foreach (var (address, cell) in output.Cells)
                {
                    var current = sheet.GetCell(address.Row, address.Column);
                    var style = sheet.GetStyleId(address.Row, address.Column);
                    var numberFormat = cell.NumberFormat ?? "General";
                    if (Workbook.Styles[style].NumberFormat != numberFormat && (cell.NumberFormat is not null || Workbook.Styles[style].NumberFormat != "General" && current.Value.IsNumber))
                    {
                        style = Workbook.Styles.Intern(Workbook.Styles[style] with { NumberFormat = numberFormat });
                    }

                    var value = cell.Value;
                    if (current.Formula is not null || !current.Value.Equals(value) || current.StyleId != style)
                    {
                        SetCell(sheet, address.Row, address.Column, new Cell(value, null, style));
                    }
                }
            }
        }
        finally
        {
            _writingPivot = false;
        }

        var placed = pivot with { Range = output?.Range ?? range };
        ChangeSheetState(sheet, () =>
        {
            var index = old is null ? -1 : sheet.PivotTables.IndexOf(old);
            if (index >= 0)
            {
                sheet.PivotTables[index] = placed;
            }
            else
            {
                sheet.PivotTables.Add(placed);
            }
        });
    }

    /// <summary>An empty PivotTable holds a place of 3 × 18 cells (Excel draws its hint there).</summary>
    private static CellRange EmptyRange(PivotTable pivot) =>
        new(pivot.Location.Row, pivot.Location.Column, pivot.Location.Row + 17, pivot.Location.Column + 2);

    /// <summary>Removes a table's values (not the cells in <paramref name="keep"/>, which are written again).</summary>
    private void Clear(Worksheet sheet, PivotTable pivot, HashSet<CellAddress>? keep = null)
    {
        if (pivot.Range is not { } range)
        {
            return;
        }

        _writingPivot = true;
        try
        {
            foreach (var entry in sheet.GetCells(range).ToList())
            {
                if (keep?.Contains(entry.Address) == true || entry.Cell.Value.IsBlank && entry.Cell.Formula is null)
                {
                    continue;
                }

                SetCell(sheet, entry.Row, entry.Column, Cell.Empty with { StyleId = entry.Cell.StyleId });
            }
        }
        finally
        {
            _writingPivot = false;
        }
    }

    /// <summary>Refuses to change a value inside a PivotTable (only the table itself writes there).</summary>
    private void GuardPivot(Worksheet sheet, int row, int column, Cell before, Cell after)
    {
        if (_writingPivot || sheet.PivotTables.Count == 0 || (before.Value.Equals(after.Value) && before.Formula == after.Formula))
        {
            return;
        }

        if (sheet.PivotAt(row, column) is not null)
        {
            throw new BusinessException("Diese Änderung ist nicht möglich, weil sie eine PivotTable ändern würde. Ändern Sie die PivotTable über die Feldliste, oder verschieben Sie sie.");
        }
    }

    /// <summary>The text of a source for the dialogs ("Daten!$A$1:$D$20" or the table's name).</summary>
    public string PivotSourceText(PivotTable pivot) => CacheOf(pivot).SourceTable ?? $"{SheetPrefix(CacheOf(pivot).SourceSheet)}!{CacheOf(pivot).SourceRange.ToAbsoluteString()}";
}
