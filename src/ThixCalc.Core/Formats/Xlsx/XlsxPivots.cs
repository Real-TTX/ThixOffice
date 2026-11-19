using System.Collections.Immutable;
using System.Globalization;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using ThixCalc.Core.Model.Pivot;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>
/// PivotTables in xlsx: pivotCacheDefinition (+ pivotCacheRecords) and pivotTableDefinition parts. Read into the model (fields,
/// shared items, records, areas, value fields, hidden items, filters, sort, subtotals, grouping of dates and numbers, layout,
/// style); what it cannot hold (OLAP, calculated fields/items, data model, scenarios) marks the table <see cref="PivotTable.Unsupported"/>
/// – shown and kept, not edited. Unchanged tables and caches stay as read (their parts are not touched); changed and new ones are
/// written in Excel's shape with rowItems/colItems from the computed layout, the cache marked refreshOnLoad.
/// </summary>
internal static class XlsxPivots
{
    private static readonly XNamespace Main = XlsxNamespaces.Main;
    private static readonly XNamespace Relationships = XlsxNamespaces.Relationships;
    private const string Previous = "1048828";
    private const string Next = "1048829";

    // ---------------------------------------------------------------- reading

    public static void Read(Workbook workbook, WorkbookPart workbookPart, IReadOnlyList<(Worksheet Sheet, WorksheetPart Part)> sheets, ReadContext context)
    {
        // cacheId by the workbook's relationship to the cache definition.
        var ids = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var entry in workbookPart.Workbook?.PivotCaches?.Elements<X.PivotCache>() ?? [])
        {
            if (entry.Id?.Value is { } relationship && workbookPart.GetPartById(relationship) is PivotTableCacheDefinitionPart part)
            {
                ids[part.Uri.ToString()] = (int)(entry.CacheId?.Value ?? 0);
            }
        }

        var caches = new Dictionary<string, (PivotCache Cache, CacheFile File)?>(StringComparer.Ordinal);
        foreach (var (sheet, worksheetPart) in sheets)
        {
            foreach (var tablePart in worksheetPart.PivotTableParts)
            {
                var cachePart = tablePart.PivotTableCacheDefinitionPart;
                if (cachePart is null)
                {
                    continue;
                }

                var uri = cachePart.Uri.ToString();
                if (!caches.TryGetValue(uri, out var cache))
                {
                    cache = ReadCache(workbook, cachePart, ids.GetValueOrDefault(uri, caches.Count + 1));
                    caches[uri] = cache;
                    if (cache is { } read)
                    {
                        workbook.PivotCaches.Add(read.Cache);
                    }
                }

                if (cache is not { } found)
                {
                    context.Note("PivotTables, die ThixCalc nicht lesen kann (bleiben erhalten)");
                    continue;
                }

                try
                {
                    var pivot = ReadTable(tablePart, found.Cache, found.File);
                    sheet.PivotTables.Add(pivot);
                    if (pivot.Unsupported is { } reason)
                    {
                        context.Note($"PivotTable „{pivot.Name}“ mit {reason} (wird angezeigt und erhalten, nicht bearbeitet)");
                    }
                }
                catch (Exception exception) when (exception is XmlException or FormatException or InvalidOperationException or IndexOutOfRangeException or ArgumentException)
                {
                    context.Note("PivotTables, die ThixCalc nicht lesen kann (bleiben erhalten)");
                }
            }
        }
    }

    /// <summary>What the file's cache has beyond the model: its field count incl. grouping fields, and per file field the model field.</summary>
    private sealed record CacheFile(int FileFieldCount, int[] ModelField, PivotDateGroups[] Groups, PivotNumberGroup?[] NumberGroups, int[] BaseOf, List<List<string>> GroupItems, string? Unsupported);

    private static (PivotCache, CacheFile)? ReadCache(Workbook workbook, PivotTableCacheDefinitionPart part, int id)
    {
        XDocument document;
        using (var stream = part.GetStream())
        {
            document = XDocument.Load(stream);
        }

        var root = document.Root!;
        string? unsupported = null;
        var source = root.Element(Main + "cacheSource");
        var worksheetSource = source?.Element(Main + "worksheetSource");
        if (source?.Attribute("type")?.Value is "external" or "consolidation" or "scenario" || worksheetSource is null)
        {
            unsupported = "einer externen oder OLAP-Datenquelle";
        }

        var table = worksheetSource?.Attribute("name")?.Value;
        var sheetName = worksheetSource?.Attribute("sheet")?.Value ?? workbook.Sheets[0].Name;
        var range = CellRange.TryParse(worksheetSource?.Attribute("ref")?.Value?.Replace("$", string.Empty, StringComparison.Ordinal) ?? string.Empty) ?? new CellRange(0, 0, 0, 0);
        if (table is not null)
        {
            foreach (var sheet in workbook.Sheets)
            {
                if (sheet.Tables.FirstOrDefault(candidate => string.Equals(candidate.Name, table, StringComparison.OrdinalIgnoreCase)) is { } found)
                {
                    sheetName = sheet.Name;
                    range = found.Range;
                }
            }
        }

        var cacheFields = root.Element(Main + "cacheFields")?.Elements(Main + "cacheField").ToList() ?? [];
        var modelField = new int[cacheFields.Count];
        var groups = new PivotDateGroups[cacheFields.Count];
        var numberGroups = new PivotNumberGroup?[cacheFields.Count];
        var baseOf = new int[cacheFields.Count];
        var groupItems = new List<List<string>>();
        var fields = new List<PivotCacheField>();
        var sharedItems = new List<List<CellValue>>();
        for (var index = 0; index < cacheFields.Count; index++)
        {
            var field = cacheFields[index];
            baseOf[index] = index;
            if (field.Attribute("formula") is not null)
            {
                unsupported ??= "berechneten Feldern";
            }

            var group = field.Element(Main + "fieldGroup");
            var range2 = group?.Element(Main + "rangePr");
            groupItems.Add(group?.Element(Main + "groupItems")?.Elements().Select(item => item.Attribute("v")?.Value ?? string.Empty).ToList() ?? []);
            if (group is not null)
            {
                baseOf[index] = int.TryParse(group.Attribute("base")?.Value, out var baseField) ? baseField : index;
                if (group.Element(Main + "discretePr") is not null)
                {
                    unsupported ??= "manuell gruppierten Elementen";
                }

                var by = range2?.Attribute("groupBy")?.Value ?? "range";
                groups[index] = by switch
                {
                    "years" => PivotDateGroups.Years,
                    "quarters" => PivotDateGroups.Quarters,
                    "months" => PivotDateGroups.Months,
                    "days" => PivotDateGroups.Days,
                    _ => PivotDateGroups.None,
                };
                if (by is "hours" or "minutes" or "seconds")
                {
                    unsupported ??= "nach Uhrzeit gruppierten Feldern";
                }

                if (by == "range" && range2 is not null)
                {
                    numberGroups[index] = new PivotNumberGroup(Number(range2, "startNum", 0), Number(range2, "endNum", 0), Number(range2, "groupInterval", 1));
                }
            }

            if (field.Attribute("databaseField")?.Value is "0")
            {
                modelField[index] = -1;
                sharedItems.Add([]);
                continue;
            }

            modelField[index] = fields.Count;
            var items = field.Element(Main + "sharedItems")?.Elements().Select(Value).ToList() ?? [];
            sharedItems.Add(items);
            var isDate = field.Element(Main + "sharedItems")?.Attribute("containsDate")?.Value is "1" or "true";
            var format = isDate ? "dd.mm.yyyy" : int.TryParse(field.Attribute("numFmtId")?.Value, out var formatId) && formatId != 0 ? BuiltInFormats.Code(formatId) : null;
            fields.Add(new PivotCacheField(field.Attribute("name")?.Value ?? $"Feld{index + 1}", items, format));
        }

        // Records: shared item indices (x) or inline values.
        var records = Enumerable.Range(0, fields.Count).Select(_ => new List<int>()).ToArray();
        var lookups = sharedItems.Select(list => list.Select((value, position) => (PivotItems.KeyOf(value), position)).GroupBy(pair => pair.Item1).ToDictionary(group => group.Key, group => group.First().position)).ToList();
        var count = 0;
        if (part.PivotTableCacheRecordsPart is { } recordsPart)
        {
            using var stream = recordsPart.GetStream();
            using var reader = XmlReader.Create(stream);
            reader.MoveToContent();
            while (!reader.EOF)
            {
                if (reader.NodeType != XmlNodeType.Element || reader.LocalName != "r" || XNode.ReadFrom(reader) is not XElement record)
                {
                    reader.Read();
                    continue;
                }

                var fileField = 0;
                foreach (var cell in record.Elements())
                {
                    while (fileField < modelField.Length && modelField[fileField] < 0)
                    {
                        fileField++;
                    }

                    if (fileField >= modelField.Length)
                    {
                        break;
                    }

                    var target = modelField[fileField];
                    int item;
                    if (cell.Name.LocalName == "x")
                    {
                        item = int.Parse(cell.Attribute("v")?.Value ?? "0", CultureInfo.InvariantCulture);
                    }
                    else
                    {
                        var value = Value(cell);
                        var key = PivotItems.KeyOf(value);
                        if (!lookups[fileField].TryGetValue(key, out item))
                        {
                            item = sharedItems[fileField].Count;
                            sharedItems[fileField].Add(value);
                            lookups[fileField][key] = item;
                        }
                    }

                    records[target].Add(item);
                    fileField++;
                }

                count++;
            }
        }

        // Fields whose items grew with inline record values.
        for (var index = 0; index < cacheFields.Count; index++)
        {
            if (modelField[index] >= 0 && sharedItems[index].Count != fields[modelField[index]].Items.Count)
            {
                var old = fields[modelField[index]];
                fields[modelField[index]] = new PivotCacheField(old.Name, sharedItems[index], old.NumberFormat);
            }
        }

        if (records.Any(list => list.Count != count))
        {
            count = 0;
        }

        var cache = PivotCache.FromFile(id, sheetName, range, table, fields, [.. records.Select(list => list.ToArray())], count);
        var file = new CacheFile(cacheFields.Count, modelField, groups, numberGroups, baseOf, groupItems, unsupported);
        return (cache.WithFile(part.Uri.ToString()), file);
    }

    private static double Number(XElement element, string attribute, double fallback) =>
        double.TryParse(element.Attribute(attribute)?.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static CellValue Value(XElement element)
    {
        var text = element.Attribute("v")?.Value;
        return element.Name.LocalName switch
        {
            "n" => double.Parse(text ?? "0", NumberStyles.Float, CultureInfo.InvariantCulture),
            "s" => text ?? string.Empty,
            "b" => text is "1" or "true",
            "e" => CellErrors.Parse(text ?? "#N/A") ?? CellError.NotAvailable,
            "d" => DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeLocal, out var date) ? DateSerial.FromDateTime(date) : CellValue.Blank,
            _ => CellValue.Blank,
        };
    }

    private static PivotTable ReadTable(PivotTablePart part, PivotCache cache, CacheFile file)
    {
        XDocument document;
        using (var stream = part.GetStream())
        {
            document = XDocument.Load(stream);
        }

        var root = document.Root!;
        var name = root.Attribute("name")?.Value ?? "PivotTable";
        var location = root.Element(Main + "location");
        var reference = CellRange.TryParse(location?.Attribute("ref")?.Value ?? "A1") ?? new CellRange(0, 0, 0, 0);
        var unsupported = file.Unsupported;
        if (root.Element(Main + "calculatedItems") is not null)
        {
            unsupported ??= "berechneten Elementen";
        }

        int ModelOf(int fileField) => fileField == PivotTable.ValuesField ? fileField : file.ModelField[file.BaseOf[fileField]];

        var fileFields = root.Element(Main + "pivotFields")?.Elements(Main + "pivotField").ToList() ?? [];
        var fields = Enumerable.Range(0, cache.Fields.Count).Select(index => new PivotField(index)).ToArray();
        var layout = PivotLayout.Compact;
        for (var index = 0; index < fileFields.Count && index < file.FileFieldCount; index++)
        {
            var element = fileFields[index];
            var model = ModelOf(index);
            if (model < 0 || model >= fields.Length)
            {
                continue;
            }

            var axis = element.Attribute("axis")?.Value switch
            {
                "axisRow" => PivotAxis.Row,
                "axisCol" => PivotAxis.Column,
                "axisPage" => PivotAxis.Filter,
                _ => PivotAxis.None,
            };
            var field = fields[model];
            if (axis != PivotAxis.None)
            {
                field = field with { Axis = axis };
            }

            if (file.Groups[index] != PivotDateGroups.None)
            {
                field = field with { DateGroups = field.DateGroups | file.Groups[index] };
            }

            if (file.NumberGroups[index] is { } numbers && file.BaseOf[index] == index)
            {
                field = field with { NumberGroup = numbers };
            }

            if (index == file.BaseOf[index])
            {
                if (element.Attribute("outline")?.Value is "0" && axis == PivotAxis.Row)
                {
                    layout = PivotLayout.Tabular;
                }
                else if (element.Attribute("compact")?.Value is "0" && axis == PivotAxis.Row && layout == PivotLayout.Compact)
                {
                    layout = PivotLayout.Outline;
                }

                var hidden = ImmutableHashSet.CreateBuilder<string>();
                foreach (var item in element.Element(Main + "items")?.Elements(Main + "item") ?? [])
                {
                    if (item.Attribute("h")?.Value is "1" or "true" && int.TryParse(item.Attribute("x")?.Value, out var x))
                    {
                        if (file.Groups[index] == PivotDateGroups.None && file.NumberGroups[index] is null && x < cache.Fields[model].Items.Count)
                        {
                            hidden.Add(PivotItems.KeyOf(cache.Fields[model].Items[x]));
                        }
                        else if (x < file.GroupItems[index].Count)
                        {
                            hidden.Add(GroupKey(file, index, x));
                        }
                    }
                }

                var descending = element.Attribute("sortType")?.Value == "descending";
                var sortData = -1;
                if (element.Element(Main + "autoSortScope")?.Descendants(Main + "reference").FirstOrDefault(reference => reference.Attribute("field")?.Value == "4294967294") is { } scope
                    && int.TryParse(scope.Element(Main + "x")?.Attribute("v")?.Value ?? "0", out var data))
                {
                    sortData = data;
                }

                field = field with
                {
                    HiddenItems = hidden.ToImmutable(),
                    Subtotals = element.Attribute("defaultSubtotal")?.Value is not ("0" or "false"),
                    Sort = new PivotSort(descending, sortData),
                    Caption = element.Attribute("name")?.Value,
                };
            }

            fields[model] = field;
        }

        ImmutableArray<int> Axis(string element) =>
        [
            .. (root.Element(Main + element)?.Elements(Main + "field") ?? [])
                .Select(field => int.Parse(field.Attribute("x")?.Value ?? "0", CultureInfo.InvariantCulture))
                .Select(ModelOf)
                .Distinct(),
        ];

        var pages = root.Element(Main + "pageFields")?.Elements(Main + "pageField").ToList() ?? [];
        foreach (var page in pages)
        {
            var field = ModelOf(int.Parse(page.Attribute("fld")?.Value ?? "0", CultureInfo.InvariantCulture));
            if (field >= 0 && int.TryParse(page.Attribute("item")?.Value, out var item) && item < cache.Fields[field].Items.Count)
            {
                fields[field] = fields[field] with { SelectedItem = PivotItems.KeyOf(cache.Fields[field].Items[item]) };
            }
        }

        var dataFields = (root.Element(Main + "dataFields")?.Elements(Main + "dataField") ?? []).Select(element =>
        {
            var field = ModelOf(int.Parse(element.Attribute("fld")?.Value ?? "0", CultureInfo.InvariantCulture));
            var function = element.Attribute("subtotal")?.Value switch
            {
                "count" => PivotFunction.Count,
                "average" => PivotFunction.Average,
                "max" => PivotFunction.Max,
                "min" => PivotFunction.Min,
                "product" => PivotFunction.Product,
                "countNums" => PivotFunction.CountNumbers,
                "stdDev" => PivotFunction.StdDev,
                "stdDevp" => PivotFunction.StdDevP,
                "var" => PivotFunction.Var,
                "varp" => PivotFunction.VarP,
                _ => PivotFunction.Sum,
            };
            var showAs = element.Attribute("showDataAs")?.Value switch
            {
                "percentOfTotal" => PivotShowAs.PercentOfGrandTotal,
                "percentOfCol" => PivotShowAs.PercentOfColumnTotal,
                "percentOfRow" => PivotShowAs.PercentOfRowTotal,
                "difference" => PivotShowAs.Difference,
                "percentDiff" => PivotShowAs.PercentDifference,
                "runTotal" => PivotShowAs.RunningTotal,
                null or "normal" => PivotShowAs.Normal,
                _ => (PivotShowAs?)null,
            };
            if (showAs is null)
            {
                unsupported ??= "„Werte anzeigen als“-Einstellungen, die ThixCalc nicht kennt";
            }

            var baseField = int.TryParse(element.Attribute("baseField")?.Value, out var fileBase) ? ModelOf(fileBase) : -1;
            var baseItem = element.Attribute("baseItem")?.Value switch
            {
                Previous => PivotDataField.PreviousItem,
                Next => PivotDataField.NextItem,
                { } text when baseField >= 0 && int.TryParse(text, out var item) && item < cache.Fields[baseField].Items.Count => PivotItems.KeyOf(cache.Fields[baseField].Items[item]),
                _ => null,
            };
            var format = int.TryParse(element.Attribute("numFmtId")?.Value, out var formatId) && formatId != 0 ? BuiltInFormats.Code(formatId) : null;
            return new PivotDataField(field, element.Attribute("name")?.Value ?? PivotEngine.DataFieldName(function, cache.Fields[field].Name), function)
            {
                ShowAs = showAs ?? PivotShowAs.Normal,
                BaseField = showAs is PivotShowAs.Normal or PivotShowAs.PercentOfGrandTotal or PivotShowAs.PercentOfColumnTotal or PivotShowAs.PercentOfRowTotal ? -1 : baseField,
                BaseItem = baseItem,
                NumberFormat = format,
            };
        }).ToImmutableArray();

        var style = root.Element(Main + "pivotTableStyleInfo");
        var filterRows = pages.Count;
        var bodyTop = reference.FirstRow;
        var range = filterRows > 0 ? new CellRange(Math.Max(0, bodyTop - filterRows - 1), reference.FirstColumn, reference.LastRow, Math.Max(reference.LastColumn, reference.FirstColumn + 1)) : reference;
        var filters = (root.Element(Main + "filters")?.Elements(Main + "filter") ?? []).ToList();
        foreach (var filter in filters)
        {
            var field = ModelOf(int.Parse(filter.Attribute("fld")?.Value ?? "0", CultureInfo.InvariantCulture));
            if (field < 0 || ReadFilter(filter) is not { } read)
            {
                unsupported ??= "Filtern, die ThixCalc nicht kennt";
                continue;
            }

            fields[field] = read.ByValue ? fields[field] with { ValueFilter = read } : fields[field] with { LabelFilter = read };
        }

        var tableCompact = root.Attribute("compact")?.Value is not ("0" or "false");
        if (!tableCompact && layout == PivotLayout.Compact)
        {
            layout = PivotLayout.Outline;
        }

        var rowFields = Axis("rowFields");
        var columnFields = Axis("colFields");
        return new PivotTable(name, cache.Id, new CellAddress(bodyTop, reference.FirstColumn))
        {
            Fields = [.. fields],
            RowFields = rowFields,
            ColumnFields = columnFields,
            FilterFields = [.. pages.Select(page => ModelOf(int.Parse(page.Attribute("fld")?.Value ?? "0", CultureInfo.InvariantCulture))).Where(field => field >= 0).Distinct()],
            DataFields = dataFields,
            Layout = layout,
            RowGrandTotals = root.Attribute("rowGrandTotals")?.Value is not ("0" or "false"),
            ColumnGrandTotals = root.Attribute("colGrandTotals")?.Value is not ("0" or "false"),
            SubtotalsAtTop = fileFields.All(field => field.Attribute("subtotalTop")?.Value is not ("0" or "false")),
            Style = style?.Attribute("name")?.Value ?? PivotStyles.DefaultStyle,
            RowHeaders = style?.Attribute("showRowHeaders")?.Value is not ("0" or "false"),
            ColumnHeaders = style?.Attribute("showColHeaders")?.Value is not ("0" or "false"),
            BandedRows = style?.Attribute("showRowStripes")?.Value is "1" or "true",
            BandedColumns = style?.Attribute("showColStripes")?.Value is "1" or "true",
            Range = range,
            PartUri = part.Uri.ToString(),
            SourceXml = root.ToString(SaveOptions.DisableFormatting),
            Unsupported = unsupported,
        };
    }

    /// <summary>The model's key of a group item of the file (by its index in groupItems).</summary>
    private static string GroupKey(CacheFile file, int field, int index)
    {
        var text = file.GroupItems[field][index];
        if (index == 0 && text.StartsWith('<'))
        {
            return file.NumberGroups[field] is not null ? "<" : "x<";
        }

        if (text.StartsWith('>') && index == file.GroupItems[field].Count - 1)
        {
            return file.NumberGroups[field] is not null ? ">" : "x>";
        }

        return file.Groups[field] switch
        {
            PivotDateGroups.Years => "y" + text,
            PivotDateGroups.Quarters => "q" + index,
            PivotDateGroups.Months => "m" + index,
            PivotDateGroups.Days => DayKey(index),
            _ => file.NumberGroups[field] is { } numbers ? "g" + (numbers.Start + (index - 1) * numbers.Interval).ToString("R", CultureInfo.InvariantCulture) : text,
        };
    }

    private static string DayKey(int dayOfLeapYear)
    {
        var date = new DateTime(2000, 1, 1).AddDays(dayOfLeapYear - 1);
        return "d" + date.Month + "-" + date.Day;
    }

    private static PivotFilter? ReadFilter(XElement filter)
    {
        var type = filter.Attribute("type")?.Value ?? string.Empty;
        var value = filter.Attribute("stringValue1")?.Value ?? string.Empty;
        var value2 = filter.Attribute("stringValue2")?.Value;
        var data = int.TryParse(filter.Attribute("iMeasureFld")?.Value, out var measure) ? measure : 0;
        if (type is "count" or "percent" or "sum")
        {
            var top = filter.Descendants(Main + "top10").FirstOrDefault();
            var bottom = top?.Attribute("top")?.Value is "0" or "false";
            return type == "count" ? new PivotFilter(bottom ? PivotFilterKind.Bottom : PivotFilterKind.Top, top?.Attribute("val")?.Value ?? "10", DataField: data) : null;
        }

        var byValue = type.StartsWith("value", StringComparison.Ordinal);
        var kind = (byValue ? type[5..] : type.StartsWith("caption", StringComparison.Ordinal) ? type[7..] : string.Empty) switch
        {
            "Equal" => PivotFilterKind.Equals,
            "NotEqual" => PivotFilterKind.NotEquals,
            "GreaterThan" => PivotFilterKind.GreaterThan,
            "GreaterThanOrEqual" => PivotFilterKind.GreaterOrEqual,
            "LessThan" => PivotFilterKind.LessThan,
            "LessThanOrEqual" => PivotFilterKind.LessOrEqual,
            "BeginsWith" => PivotFilterKind.BeginsWith,
            "EndsWith" => PivotFilterKind.EndsWith,
            "Contains" => PivotFilterKind.Contains,
            "Between" => PivotFilterKind.Between,
            _ => (PivotFilterKind?)null,
        };
        if (kind is not { } known)
        {
            return null;
        }

        if (byValue)
        {
            value = Number(filter, "stringValue1", 0) is var number && filter.Attribute("stringValue1") is not null ? value
                : filter.Descendants(Main + "customFilter").FirstOrDefault()?.Attribute("val")?.Value ?? value;
        }

        return new PivotFilter(known, value, value2, byValue ? data : -1);
    }

    // ---------------------------------------------------------------- writing

    /// <summary>Writes the pivot parts of every sheet and the workbook's pivotCaches (after the worksheets were written).</summary>
    public static void Write(Workbook workbook, WorkbookPart workbookPart, IReadOnlyList<(Worksheet Sheet, WorksheetPart Part)> sheets)
    {
        var pivots = sheets.SelectMany(entry => entry.Sheet.PivotTables.Select(pivot => (entry.Sheet, entry.Part, Pivot: pivot))).ToList();
        if (pivots.Count == 0 && !workbookPart.PivotTableCacheDefinitionParts.Any())
        {
            return;
        }

        // Caches: unchanged ones keep their part; new/refreshed ones are written (into their old part when they had one).
        var cacheParts = workbookPart.PivotTableCacheDefinitionParts.ToDictionary(part => part.Uri.ToString(), StringComparer.Ordinal);
        var used = pivots.Select(entry => entry.Pivot.CacheId).ToHashSet();
        var written = new Dictionary<int, PivotTableCacheDefinitionPart>();
        var layouts = new Dictionary<int, CacheLayout>();
        foreach (var cache in workbook.PivotCaches.Where(cache => used.Contains(cache.Id)))
        {
            if (cache.PartUri is { } uri && cacheParts.TryGetValue(uri, out var existing) && cache.IsAsRead)
            {
                written[cache.Id] = existing;
                continue;
            }

            var part = cache.PartUri is { } old && cacheParts.TryGetValue(old, out var reuse) ? reuse : workbookPart.AddNewPart<PivotTableCacheDefinitionPart>();
            var tables = pivots.Where(entry => entry.Pivot.CacheId == cache.Id).Select(entry => entry.Pivot).ToList();
            var cacheLayout = CacheLayout.Of(cache, tables);
            layouts[cache.Id] = cacheLayout;
            WriteCache(part, cache, cacheLayout, workbook);
            written[cache.Id] = part;
        }

        // Tables: unchanged ones stay; changed and new ones are written; removed ones lose their part.
        foreach (var (sheet, worksheetPart) in sheets)
        {
            var parts = worksheetPart.PivotTableParts.ToList();
            foreach (var orphan in parts.Where(part => !sheet.PivotTables.Any(pivot => pivot.PartUri == part.Uri.ToString())).ToList())
            {
                worksheetPart.DeletePart(orphan);
            }

            for (var index = 0; index < sheet.PivotTables.Count; index++)
            {
                var pivot = sheet.PivotTables[index];
                var cacheChanged = layouts.ContainsKey(pivot.CacheId);
                var part = parts.FirstOrDefault(candidate => candidate.Uri.ToString() == pivot.PartUri);
                if (part is not null && pivot.SourceXml is not null && !cacheChanged)
                {
                    continue;
                }

                if (part is null)
                {
                    part = worksheetPart.AddNewPart<PivotTablePart>();
                    part.AddPart(written[pivot.CacheId]);
                }
                else if (part.PivotTableCacheDefinitionPart is { } linked && linked.Uri != written[pivot.CacheId].Uri)
                {
                    part.DeletePart(linked);
                    part.AddPart(written[pivot.CacheId]);
                }

                var cache = workbook.FindPivotCache(pivot.CacheId)!;
                var layout = layouts.GetValueOrDefault(pivot.CacheId) ?? CacheLayout.Of(cache, [pivot]);
                if (pivot.Unsupported is null)
                {
                    WriteTable(part, pivot, cache, layout, sheet);
                }

                sheet.PivotTables[index] = pivot with { PartUri = part.Uri.ToString() };
            }
        }

        // Caches no table uses any more go.
        foreach (var (uri, part) in cacheParts)
        {
            if (!written.Values.Any(kept => kept.Uri.ToString() == uri))
            {
                workbookPart.DeletePart(part);
            }
        }

        var root = workbookPart.Workbook!;
        if (written.Count == 0)
        {
            root.PivotCaches = null;
            return;
        }

        var entries = new X.PivotCaches();
        foreach (var (id, part) in written.OrderBy(entry => entry.Key))
        {
            entries.Append(new X.PivotCache { CacheId = (uint)id, Id = workbookPart.GetIdOfPart(part) });
        }

        root.PivotCaches = entries;
        foreach (var cache in workbook.PivotCaches)
        {
            if (written.TryGetValue(cache.Id, out var part))
            {
                cache.WithFile(part.Uri.ToString());
            }
        }
    }

    /// <summary>The file fields of a cache: the source fields, then a grouping field per extra date level the tables use.</summary>
    private sealed class CacheLayout
    {
        public List<(int Base, PivotDateGroups Group)> Extra { get; } = [];

        /// <summary>Per source field its grouping in the file (the finest level on the field itself).</summary>
        public Dictionary<int, (PivotDateGroups Finest, PivotNumberGroup? Numbers)> Groups { get; } = [];

        public int FieldCount(PivotCache cache) => cache.Fields.Count + Extra.Count;

        /// <summary>The file field of a level: the source field or its extra grouping field.</summary>
        public int FileField(int field, PivotDateGroups group)
        {
            if (group == PivotDateGroups.None || !Groups.TryGetValue(field, out var grouping) || grouping.Finest == group)
            {
                return field;
            }

            var extra = Extra.FindIndex(entry => entry.Base == field && entry.Group == group);
            return extra < 0 ? field : -1 - extra;
        }

        public static CacheLayout Of(PivotCache cache, IReadOnlyList<PivotTable> tables)
        {
            var layout = new CacheLayout();
            foreach (var table in tables)
            {
                foreach (var field in table.Fields.Where(field => field.IsGrouped && field.Index < cache.Fields.Count))
                {
                    if (layout.Groups.ContainsKey(field.Index))
                    {
                        continue;
                    }

                    var levels = new[] { PivotDateGroups.Years, PivotDateGroups.Quarters, PivotDateGroups.Months, PivotDateGroups.Days }.Where(group => field.DateGroups.HasFlag(group)).ToList();
                    layout.Groups[field.Index] = (levels.Count > 0 ? levels[^1] : PivotDateGroups.None, field.NumberGroup);
                    foreach (var group in levels.Take(Math.Max(0, levels.Count - 1)).Reverse())
                    {
                        layout.Extra.Add((field.Index, group));
                    }
                }
            }

            return layout;
        }
    }

    private static string FileName(int extraIndex, CacheLayout layout, PivotCache cache) => PivotEngine.GroupName(layout.Extra[extraIndex].Group);

    private static void WriteCache(PivotTableCacheDefinitionPart part, PivotCache cache, CacheLayout layout, Workbook workbook)
    {
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings { OmitXmlDeclaration = false, Encoding = Encoding.UTF8 }))
        {
            writer.WriteStartDocument(true);
            writer.WriteStartElement("pivotCacheDefinition", XlsxNamespaces.Main);
            writer.WriteAttributeString("xmlns", "r", null, XlsxNamespaces.Relationships);
            var recordsPart = part.PivotTableCacheRecordsPart ?? part.AddNewPart<PivotTableCacheRecordsPart>();
            writer.WriteAttributeString("id", XlsxNamespaces.Relationships, part.GetIdOfPart(recordsPart));
            writer.WriteAttributeString("refreshOnLoad", "1");
            writer.WriteAttributeString("refreshedBy", "ThixCalc");
            writer.WriteAttributeString("refreshedDate", DateSerial.FromDateTime(DateTime.Now).ToString("R", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("createdVersion", "8");
            writer.WriteAttributeString("refreshedVersion", "8");
            writer.WriteAttributeString("minRefreshableVersion", "3");
            writer.WriteAttributeString("recordCount", cache.RecordCount.ToString(CultureInfo.InvariantCulture));
            writer.WriteStartElement("cacheSource");
            writer.WriteAttributeString("type", "worksheet");
            writer.WriteStartElement("worksheetSource");
            if (cache.SourceTable is { } table)
            {
                writer.WriteAttributeString("name", table);
            }
            else
            {
                writer.WriteAttributeString("ref", cache.SourceRange.ToString());
                writer.WriteAttributeString("sheet", cache.SourceSheet);
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteStartElement("cacheFields");
            writer.WriteAttributeString("count", layout.FieldCount(cache).ToString(CultureInfo.InvariantCulture));
            for (var index = 0; index < cache.Fields.Count; index++)
            {
                var field = cache.Fields[index];
                writer.WriteStartElement("cacheField");
                writer.WriteAttributeString("name", field.Name);
                writer.WriteAttributeString("numFmtId", field.IsDate ? "14" : "0");
                WriteSharedItems(writer, field);
                if (layout.Groups.TryGetValue(index, out var grouping))
                {
                    var parent = layout.Extra.FindLastIndex(entry => entry.Base == index);
                    WriteFieldGroup(writer, field, index, grouping.Finest, grouping.Numbers, parent < 0 ? null : cache.Fields.Count + parent, workbook);
                }

                writer.WriteEndElement();
            }

            for (var extra = 0; extra < layout.Extra.Count; extra++)
            {
                var (baseField, group) = layout.Extra[extra];
                writer.WriteStartElement("cacheField");
                writer.WriteAttributeString("name", FileName(extra, layout, cache));
                writer.WriteAttributeString("numFmtId", "0");
                writer.WriteAttributeString("databaseField", "0");
                var coarser = extra > 0 && layout.Extra[extra - 1].Base == baseField ? cache.Fields.Count + extra - 1 : (int?)null;
                WriteFieldGroup(writer, cache.Fields[baseField], baseField, group, null, coarser, workbook);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        Feed(part, builder.ToString());

        var records = new StringBuilder();
        using (var writer = XmlWriter.Create(records, new XmlWriterSettings { Encoding = Encoding.UTF8 }))
        {
            writer.WriteStartDocument(true);
            writer.WriteStartElement("pivotCacheRecords", XlsxNamespaces.Main);
            writer.WriteAttributeString("xmlns", "r", null, XlsxNamespaces.Relationships);
            writer.WriteAttributeString("count", cache.RecordCount.ToString(CultureInfo.InvariantCulture));
            for (var record = 0; record < cache.RecordCount; record++)
            {
                writer.WriteStartElement("r");
                for (var field = 0; field < cache.Fields.Count; field++)
                {
                    writer.WriteStartElement("x");
                    writer.WriteAttributeString("v", cache.Records[field][record].ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        Feed(part.PivotTableCacheRecordsPart!, records.ToString());
    }

    private static void WriteSharedItems(XmlWriter writer, PivotCacheField field)
    {
        var items = field.Items;
        var hasText = items.Any(item => item.IsText);
        var hasBlank = items.Any(item => item.IsBlank);
        var hasNumber = items.Any(item => item.IsNumber);
        var hasBoolean = items.Any(item => item.IsBoolean);
        var hasError = items.Any(item => item.IsError);
        var isDate = field.IsDate;
        writer.WriteStartElement("sharedItems");
        if (!hasText && !hasBoolean && !hasError)
        {
            writer.WriteAttributeString("containsSemiMixedTypes", "0");
        }

        if (isDate)
        {
            writer.WriteAttributeString("containsNonDate", "0");
            writer.WriteAttributeString("containsDate", "1");
        }

        if (!hasText)
        {
            writer.WriteAttributeString("containsString", "0");
        }

        if (hasBlank)
        {
            writer.WriteAttributeString("containsBlank", "1");
        }

        var kinds = (hasText ? 1 : 0) + (hasNumber ? 1 : 0) + (hasBoolean ? 1 : 0) + (hasError ? 1 : 0);
        if (kinds > 1)
        {
            writer.WriteAttributeString("containsMixedTypes", "1");
        }

        var numbers = items.Where(item => item.IsNumber).Select(item => item.Number).ToList();
        if (hasNumber && !isDate)
        {
            writer.WriteAttributeString("containsNumber", "1");
            if (numbers.All(number => number == Math.Floor(number)))
            {
                writer.WriteAttributeString("containsInteger", "1");
            }

            writer.WriteAttributeString("minValue", numbers.Min().ToString("R", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("maxValue", numbers.Max().ToString("R", CultureInfo.InvariantCulture));
        }

        if (isDate && numbers.Count > 0)
        {
            writer.WriteAttributeString("minDate", DateText(numbers.Min()));
            writer.WriteAttributeString("maxDate", DateText(numbers.Max() + 1));
        }

        writer.WriteAttributeString("count", items.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var item in items)
        {
            switch (item.Kind)
            {
                case CellValueKind.Text:
                    writer.WriteStartElement("s");
                    writer.WriteAttributeString("v", item.Text);
                    break;
                case CellValueKind.Number when isDate:
                    writer.WriteStartElement("d");
                    writer.WriteAttributeString("v", DateText(item.Number));
                    break;
                case CellValueKind.Number:
                    writer.WriteStartElement("n");
                    writer.WriteAttributeString("v", item.Number.ToString("R", CultureInfo.InvariantCulture));
                    break;
                case CellValueKind.Boolean:
                    writer.WriteStartElement("b");
                    writer.WriteAttributeString("v", item.Boolean ? "1" : "0");
                    break;
                case CellValueKind.Error:
                    writer.WriteStartElement("e");
                    writer.WriteAttributeString("v", CellErrors.NameOf(item.Error));
                    break;
                default:
                    writer.WriteStartElement("m");
                    break;
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static string DateText(double serial) =>
        (DateSerial.ToDateTime(serial) ?? new DateTime(1900, 1, 1)).ToString("yyyy-MM-dd'T'HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>The group items of a level in the file's order (the model's level item key of each).</summary>
    private static List<(string Key, string Text)> GroupItemsOf(PivotCacheField field, PivotDateGroups group, PivotNumberGroup? numbers)
    {
        var list = new List<(string, string)>();
        var german = CultureInfo.GetCultureInfo("de-DE");
        if (numbers is not null)
        {
            list.Add(("<", "<" + numbers.Start.ToString(german)));
            for (var start = numbers.Start; start <= numbers.End; start += numbers.Interval)
            {
                var item = PivotEngine.ItemFor(start, PivotDateGroups.None, numbers, null);
                list.Add((item.Key, item.Label.Text));
            }

            list.Add((">", ">" + numbers.End.ToString(german)));
            return list;
        }

        var dates = field.Items.Where(item => item.IsNumber).Select(item => item.Number).DefaultIfEmpty(0).ToList();
        var first = DateSerial.ToDateTime(dates.Min()) ?? new DateTime(1900, 1, 1);
        var last = DateSerial.ToDateTime(dates.Max()) ?? first;
        list.Add(("x<", "<" + first.ToString("dd.MM.yyyy", german)));
        switch (group)
        {
            case PivotDateGroups.Years:
                for (var year = first.Year; year <= last.Year; year++)
                {
                    list.Add(("y" + year, year.ToString(CultureInfo.InvariantCulture)));
                }

                break;
            case PivotDateGroups.Quarters:
                for (var quarter = 1; quarter <= 4; quarter++)
                {
                    list.Add(("q" + quarter, "Qrtl" + quarter));
                }

                break;
            case PivotDateGroups.Months:
                for (var month = 1; month <= 12; month++)
                {
                    list.Add(("m" + month, PivotEngine.ItemFor(DateSerial.FromDate(2000, month, 1) ?? 0, PivotDateGroups.Months, null, null).Label.Text));
                }

                break;
            default:
                for (var day = 1; day <= 366; day++)
                {
                    var date = new DateTime(2000, 1, 1).AddDays(day - 1);
                    list.Add((DayKey(day), PivotEngine.ItemFor(DateSerial.FromDateTime(date), PivotDateGroups.Days, null, null).Label.Text));
                }

                break;
        }

        list.Add(("x>", ">" + last.AddDays(1).ToString("dd.MM.yyyy", german)));
        return list;
    }

    private static void WriteFieldGroup(XmlWriter writer, PivotCacheField field, int baseField, PivotDateGroups group, PivotNumberGroup? numbers, int? parent, Workbook workbook)
    {
        _ = workbook;
        writer.WriteStartElement("fieldGroup");
        if (parent is { } par)
        {
            writer.WriteAttributeString("par", par.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteAttributeString("base", baseField.ToString(CultureInfo.InvariantCulture));
        writer.WriteStartElement("rangePr");
        if (numbers is not null)
        {
            writer.WriteAttributeString("autoStart", "0");
            writer.WriteAttributeString("autoEnd", "0");
            writer.WriteAttributeString("startNum", numbers.Start.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("endNum", numbers.End.ToString("R", CultureInfo.InvariantCulture));
            writer.WriteAttributeString("groupInterval", numbers.Interval.ToString("R", CultureInfo.InvariantCulture));
        }
        else
        {
            var dates = field.Items.Where(item => item.IsNumber).Select(item => item.Number).DefaultIfEmpty(0).ToList();
            writer.WriteAttributeString("groupBy", group switch
            {
                PivotDateGroups.Years => "years",
                PivotDateGroups.Quarters => "quarters",
                PivotDateGroups.Months => "months",
                _ => "days",
            });
            writer.WriteAttributeString("startDate", DateText(dates.Min()));
            writer.WriteAttributeString("endDate", DateText(dates.Max() + 1));
        }

        writer.WriteEndElement();
        var items = GroupItemsOf(field, group, numbers);
        writer.WriteStartElement("groupItems");
        writer.WriteAttributeString("count", items.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var (_, text) in items)
        {
            writer.WriteStartElement("s");
            writer.WriteAttributeString("v", text);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteTable(PivotTablePart part, PivotTable pivot, PivotCache cache, CacheLayout layout, Worksheet sheet)
    {
        var output = sheet.OutputOf(pivot);
        var rowLevels = PivotEngine.LevelsOf(pivot, cache, pivot.RowFields.ToList());
        var columnFields = pivot.ColumnFields.ToList();
        if (pivot.DataFields.Length > 1 && !pivot.RowFields.Contains(PivotTable.ValuesField) && !columnFields.Contains(PivotTable.ValuesField))
        {
            columnFields.Add(PivotTable.ValuesField);
        }

        var columnLevels = PivotEngine.LevelsOf(pivot, cache, columnFields);
        var fileFieldCount = layout.FieldCount(cache);

        // The pivotField items of each file field: positions by the model's level item key.
        var itemLists = new Dictionary<int, List<(string Key, int X)>>();
        List<(string Key, int X)> ItemsOf(int fileField)
        {
            if (itemLists.TryGetValue(fileField, out var known))
            {
                return known;
            }

            var (field, group) = fileField < cache.Fields.Count
                ? (fileField, layout.Groups.TryGetValue(fileField, out var grouping) ? grouping.Finest : PivotDateGroups.None)
                : (layout.Extra[fileField - cache.Fields.Count].Base, layout.Extra[fileField - cache.Fields.Count].Group);
            var numbers = fileField < cache.Fields.Count && layout.Groups.TryGetValue(fileField, out var numberGrouping) ? numberGrouping.Numbers : null;
            List<(string, int)> list = group == PivotDateGroups.None && numbers is null
                ? [.. cache.Fields[field].Items.Select((item, index) => (PivotItems.KeyOf(item), index))]
                : [.. GroupItemsOf(cache.Fields[field], group, numbers).Select((item, index) => (item.Key, index))];
            itemLists[fileField] = list;
            return list;
        }

        int Position(PivotEngine.Level level, int itemId)
        {
            var fileField = FileFieldOf(level);
            var key = level.Items[itemId].Key;
            var index = ItemsOf(fileField).FindIndex(entry => entry.Key == key);
            return Math.Max(0, index);
        }

        int FileFieldOf(PivotEngine.Level level)
        {
            var file = layout.FileField(level.Field, level.Group);
            return file >= 0 ? file : cache.Fields.Count + (-1 - file);
        }

        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings { Encoding = Encoding.UTF8 }))
        {
            writer.WriteStartDocument(true);
            writer.WriteStartElement("pivotTableDefinition", XlsxNamespaces.Main);
            writer.WriteAttributeString("name", pivot.Name);
            writer.WriteAttributeString("cacheId", pivot.CacheId.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("applyNumberFormats", "0");
            writer.WriteAttributeString("applyBorderFormats", "0");
            writer.WriteAttributeString("applyFontFormats", "0");
            writer.WriteAttributeString("applyPatternFormats", "0");
            writer.WriteAttributeString("applyAlignmentFormats", "0");
            writer.WriteAttributeString("applyWidthHeightFormats", "1");
            writer.WriteAttributeString("dataCaption", PivotEngine.ValuesCaption);
            if (pivot.RowFields.Contains(PivotTable.ValuesField))
            {
                writer.WriteAttributeString("dataOnRows", "1");
            }

            var valuesAxis = pivot.RowFields.Contains(PivotTable.ValuesField) ? pivot.RowFields : columnFields.ToImmutableArray();
            if (pivot.DataFields.Length > 1 && valuesAxis.IndexOf(PivotTable.ValuesField) is var position && position >= 0 && position < valuesAxis.Length - 1)
            {
                writer.WriteAttributeString("dataPosition", position.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteAttributeString("updatedVersion", "8");
            writer.WriteAttributeString("minRefreshableVersion", "3");
            writer.WriteAttributeString("useAutoFormatting", "1");
            if (!pivot.RowGrandTotals)
            {
                writer.WriteAttributeString("rowGrandTotals", "0");
            }

            if (!pivot.ColumnGrandTotals)
            {
                writer.WriteAttributeString("colGrandTotals", "0");
            }

            writer.WriteAttributeString("itemPrintTitles", "1");
            writer.WriteAttributeString("createdVersion", "8");
            writer.WriteAttributeString("indent", "0");
            if (pivot.Layout != PivotLayout.Compact)
            {
                writer.WriteAttributeString("compact", "0");
                writer.WriteAttributeString("compactData", "0");
            }

            writer.WriteAttributeString("outline", "1");
            writer.WriteAttributeString("outlineData", "1");
            if (pivot.Layout != PivotLayout.Compact)
            {
                writer.WriteAttributeString("gridDropZones", pivot.Layout == PivotLayout.Tabular ? "0" : "0");
            }

            writer.WriteAttributeString("multipleFieldFilters", "0");

            // location: the body; the filter fields above it.
            var body = output is null
                ? new CellRange(pivot.Location.Row, pivot.Location.Column, pivot.Location.Row + 17, pivot.Location.Column + 2)
                : new CellRange(output.Body.Row, output.Body.Column, output.Range.LastRow, output.Range.LastColumn);
            writer.WriteStartElement("location");
            writer.WriteAttributeString("ref", body.ToString());
            writer.WriteAttributeString("firstHeaderRow", "1");
            writer.WriteAttributeString("firstDataRow", (output is null ? 1 : columnLevels.Any(level => !level.IsValues) ? output.HeaderRows : 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("firstDataCol", (output?.LabelColumns ?? 0).ToString(CultureInfo.InvariantCulture));
            if (pivot.FilterFields.Length > 0)
            {
                writer.WriteAttributeString("rowPageCount", pivot.FilterFields.Length.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("colPageCount", "1");
            }

            writer.WriteEndElement();

            writer.WriteStartElement("pivotFields");
            writer.WriteAttributeString("count", fileFieldCount.ToString(CultureInfo.InvariantCulture));
            for (var fileField = 0; fileField < fileFieldCount; fileField++)
            {
                var field = fileField < cache.Fields.Count ? fileField : layout.Extra[fileField - cache.Fields.Count].Base;
                var settings = field < pivot.Fields.Length ? pivot.Fields[field] : new PivotField(field);
                writer.WriteStartElement("pivotField");
                if (fileField < cache.Fields.Count && settings.Caption is { } caption)
                {
                    writer.WriteAttributeString("name", caption);
                }

                var axis = settings.Axis switch
                {
                    PivotAxis.Row => "axisRow",
                    PivotAxis.Column => "axisCol",
                    PivotAxis.Filter => "axisPage",
                    _ => null,
                };
                if (axis is not null)
                {
                    writer.WriteAttributeString("axis", axis);
                }

                if (fileField < cache.Fields.Count && pivot.DataFields.Any(data => data.Field == field))
                {
                    writer.WriteAttributeString("dataField", "1");
                }

                if (pivot.Layout != PivotLayout.Compact)
                {
                    writer.WriteAttributeString("compact", "0");
                }

                if (pivot.Layout == PivotLayout.Tabular)
                {
                    writer.WriteAttributeString("outline", "0");
                }

                writer.WriteAttributeString("showAll", "0");
                if (settings.Sort.Descending || settings.Sort.DataField >= 0)
                {
                    writer.WriteAttributeString("sortType", settings.Sort.Descending ? "descending" : "ascending");
                }

                if (!settings.Subtotals)
                {
                    writer.WriteAttributeString("defaultSubtotal", "0");
                }

                if (!pivot.SubtotalsAtTop)
                {
                    writer.WriteAttributeString("subtotalTop", "0");
                }

                if (axis is not null)
                {
                    var items = ItemsOf(fileField);
                    writer.WriteStartElement("items");
                    writer.WriteAttributeString("count", (items.Count + (settings.Subtotals ? 1 : 0)).ToString(CultureInfo.InvariantCulture));
                    foreach (var (key, x) in items)
                    {
                        writer.WriteStartElement("item");
                        if (settings.HiddenItems.Contains(key))
                        {
                            writer.WriteAttributeString("h", "1");
                        }

                        writer.WriteAttributeString("x", x.ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    if (settings.Subtotals)
                    {
                        writer.WriteStartElement("item");
                        writer.WriteAttributeString("t", "default");
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                if (settings.Sort.DataField >= 0 && fileField < cache.Fields.Count)
                {
                    writer.WriteStartElement("autoSortScope");
                    writer.WriteStartElement("pivotArea");
                    writer.WriteAttributeString("dataOnly", "0");
                    writer.WriteAttributeString("outline", "0");
                    writer.WriteAttributeString("fieldPosition", "0");
                    writer.WriteStartElement("references");
                    writer.WriteAttributeString("count", "1");
                    writer.WriteStartElement("reference");
                    writer.WriteAttributeString("field", "4294967294");
                    writer.WriteAttributeString("count", "1");
                    writer.WriteAttributeString("selected", "0");
                    writer.WriteStartElement("x");
                    writer.WriteAttributeString("v", settings.Sort.DataField.ToString(CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();

            void WriteAxis(string fieldsName, string itemsName, List<PivotEngine.Level> levels, IReadOnlyList<PivotLayoutItem>? items)
            {
                if (levels.Count > 0)
                {
                    writer.WriteStartElement(fieldsName);
                    writer.WriteAttributeString("count", levels.Count.ToString(CultureInfo.InvariantCulture));
                    foreach (var level in levels)
                    {
                        writer.WriteStartElement("field");
                        writer.WriteAttributeString("x", (level.IsValues ? PivotTable.ValuesField : FileFieldOf(level)).ToString(CultureInfo.InvariantCulture));
                        writer.WriteEndElement();
                    }

                    writer.WriteEndElement();
                }

                writer.WriteStartElement(itemsName);
                var list = items is { Count: > 0 } && levels.Count > 0 ? items : null;
                writer.WriteAttributeString("count", (list?.Count ?? 1).ToString(CultureInfo.InvariantCulture));
                if (list is null)
                {
                    writer.WriteStartElement("i");
                    writer.WriteEndElement();
                }
                else
                {
                    IReadOnlyList<int> previous = [];
                    foreach (var item in list)
                    {
                        writer.WriteStartElement("i");
                        if (item.Kind == 'g')
                        {
                            writer.WriteAttributeString("t", "grand");
                            if (item.Data > 0)
                            {
                                writer.WriteAttributeString("i", item.Data.ToString(CultureInfo.InvariantCulture));
                            }

                            writer.WriteStartElement("x");
                            writer.WriteEndElement();
                            writer.WriteEndElement();
                            previous = [];
                            continue;
                        }

                        var positions = new List<int>();
                        for (var index = 0; index < item.Path.Count; index++)
                        {
                            positions.Add(levels[index].IsValues ? item.Path[index] : Position(levels[index], item.Path[index]));
                        }

                        var repeat = 0;
                        if (item.Kind != 's')
                        {
                            while (repeat < positions.Count - 1 && repeat < previous.Count && previous[repeat] == positions[repeat])
                            {
                                repeat++;
                            }
                        }

                        if (item.Kind == 's')
                        {
                            writer.WriteAttributeString("t", "default");
                        }

                        if (repeat > 0)
                        {
                            writer.WriteAttributeString("r", repeat.ToString(CultureInfo.InvariantCulture));
                        }

                        if (item.Data > 0 && levels.Any(level => level.IsValues))
                        {
                            writer.WriteAttributeString("i", item.Data.ToString(CultureInfo.InvariantCulture));
                        }

                        for (var index = repeat; index < positions.Count; index++)
                        {
                            writer.WriteStartElement("x");
                            if (positions[index] != 0)
                            {
                                writer.WriteAttributeString("v", positions[index].ToString(CultureInfo.InvariantCulture));
                            }

                            writer.WriteEndElement();
                        }

                        if (positions.Count == 0)
                        {
                            writer.WriteStartElement("x");
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        previous = positions;
                    }
                }

                writer.WriteEndElement();
            }

            WriteAxis("rowFields", "rowItems", rowLevels, output?.RowItems);
            WriteAxis("colFields", "colItems", columnLevels, output?.ColumnItems);

            if (pivot.FilterFields.Length > 0)
            {
                writer.WriteStartElement("pageFields");
                writer.WriteAttributeString("count", pivot.FilterFields.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var field in pivot.FilterFields)
                {
                    writer.WriteStartElement("pageField");
                    writer.WriteAttributeString("fld", field.ToString(CultureInfo.InvariantCulture));
                    if (pivot.Fields[field].SelectedItem is { } selected && ItemsOf(field).FindIndex(entry => entry.Key == selected) is var item && item >= 0)
                    {
                        writer.WriteAttributeString("item", item.ToString(CultureInfo.InvariantCulture));
                    }

                    writer.WriteAttributeString("hier", "-1");
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            if (pivot.DataFields.Length > 0)
            {
                writer.WriteStartElement("dataFields");
                writer.WriteAttributeString("count", pivot.DataFields.Length.ToString(CultureInfo.InvariantCulture));
                foreach (var data in pivot.DataFields)
                {
                    writer.WriteStartElement("dataField");
                    writer.WriteAttributeString("name", data.Name);
                    writer.WriteAttributeString("fld", data.Field.ToString(CultureInfo.InvariantCulture));
                    if (data.Function != PivotFunction.Sum)
                    {
                        writer.WriteAttributeString("subtotal", data.Function switch
                        {
                            PivotFunction.Count => "count",
                            PivotFunction.Average => "average",
                            PivotFunction.Max => "max",
                            PivotFunction.Min => "min",
                            PivotFunction.Product => "product",
                            PivotFunction.CountNumbers => "countNums",
                            PivotFunction.StdDev => "stdDev",
                            PivotFunction.StdDevP => "stdDevp",
                            PivotFunction.Var => "var",
                            _ => "varp",
                        });
                    }

                    if (data.ShowAs != PivotShowAs.Normal)
                    {
                        writer.WriteAttributeString("showDataAs", data.ShowAs switch
                        {
                            PivotShowAs.PercentOfGrandTotal => "percentOfTotal",
                            PivotShowAs.PercentOfColumnTotal => "percentOfCol",
                            PivotShowAs.PercentOfRowTotal => "percentOfRow",
                            PivotShowAs.Difference => "difference",
                            PivotShowAs.PercentDifference => "percentDiff",
                            _ => "runTotal",
                        });
                    }

                    writer.WriteAttributeString("baseField", Math.Max(0, data.BaseField).ToString(CultureInfo.InvariantCulture));
                    var baseItem = data.BaseItem switch
                    {
                        PivotDataField.PreviousItem => Previous,
                        PivotDataField.NextItem => Next,
                        { } key when data.BaseField >= 0 => Math.Max(0, ItemsOf(data.BaseField).FindIndex(entry => entry.Key == key)).ToString(CultureInfo.InvariantCulture),
                        _ => "0",
                    };
                    writer.WriteAttributeString("baseItem", baseItem);
                    if (data.NumberFormat is { } format && BuiltInFormats.IdOf(format) is { } id)
                    {
                        writer.WriteAttributeString("numFmtId", id.ToString(CultureInfo.InvariantCulture));
                    }
                    else if (data.ShowAs is PivotShowAs.PercentOfGrandTotal or PivotShowAs.PercentOfColumnTotal or PivotShowAs.PercentOfRowTotal or PivotShowAs.PercentDifference)
                    {
                        writer.WriteAttributeString("numFmtId", "10");
                    }

                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteStartElement("pivotTableStyleInfo");
            writer.WriteAttributeString("name", pivot.Style);
            writer.WriteAttributeString("showRowHeaders", pivot.RowHeaders ? "1" : "0");
            writer.WriteAttributeString("showColHeaders", pivot.ColumnHeaders ? "1" : "0");
            writer.WriteAttributeString("showRowStripes", pivot.BandedRows ? "1" : "0");
            writer.WriteAttributeString("showColStripes", pivot.BandedColumns ? "1" : "0");
            writer.WriteAttributeString("showLastColumn", "1");
            writer.WriteEndElement();

            // filters (label and value filters).
            var filters = pivot.Fields.SelectMany(field => new[] { (field.Index, Filter: field.LabelFilter), (field.Index, Filter: field.ValueFilter) }).Where(entry => entry.Filter is not null).ToList();
            if (filters.Count > 0)
            {
                writer.WriteStartElement("filters");
                writer.WriteAttributeString("count", filters.Count.ToString(CultureInfo.InvariantCulture));
                var id = 1;
                foreach (var (field, filter) in filters)
                {
                    WriteFilter(writer, field, filter!, id++);
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
            writer.WriteEndDocument();
        }

        Feed(part, builder.ToString());
    }

    private static void WriteFilter(XmlWriter writer, int field, PivotFilter filter, int id)
    {
        writer.WriteStartElement("filter");
        writer.WriteAttributeString("fld", field.ToString(CultureInfo.InvariantCulture));
        if (filter.ByValue)
        {
            writer.WriteAttributeString("type", filter.Kind switch
            {
                PivotFilterKind.Top or PivotFilterKind.Bottom => "count",
                PivotFilterKind.Equals => "valueEqual",
                PivotFilterKind.NotEquals => "valueNotEqual",
                PivotFilterKind.GreaterThan => "valueGreaterThan",
                PivotFilterKind.GreaterOrEqual => "valueGreaterThanOrEqual",
                PivotFilterKind.LessThan => "valueLessThan",
                PivotFilterKind.LessOrEqual => "valueLessThanOrEqual",
                _ => "valueBetween",
            });
            writer.WriteAttributeString("evalOrder", "-1");
            writer.WriteAttributeString("id", id.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("iMeasureFld", filter.DataField.ToString(CultureInfo.InvariantCulture));
        }
        else
        {
            writer.WriteAttributeString("type", "caption" + filter.Kind switch
            {
                PivotFilterKind.Equals => "Equal",
                PivotFilterKind.NotEquals => "NotEqual",
                PivotFilterKind.GreaterThan => "GreaterThan",
                PivotFilterKind.GreaterOrEqual => "GreaterThanOrEqual",
                PivotFilterKind.LessThan => "LessThan",
                PivotFilterKind.LessOrEqual => "LessThanOrEqual",
                PivotFilterKind.BeginsWith => "BeginsWith",
                PivotFilterKind.EndsWith => "EndsWith",
                PivotFilterKind.Contains => "Contains",
                _ => "Between",
            });
            writer.WriteAttributeString("evalOrder", "-1");
            writer.WriteAttributeString("id", id.ToString(CultureInfo.InvariantCulture));
        }

        if (filter.Kind is not (PivotFilterKind.Top or PivotFilterKind.Bottom))
        {
            writer.WriteAttributeString("stringValue1", filter.Value);
            if (filter.Value2 is { } second)
            {
                writer.WriteAttributeString("stringValue2", second);
            }
        }

        writer.WriteStartElement("autoFilter");
        writer.WriteAttributeString("ref", "A1");
        writer.WriteStartElement("filterColumn");
        writer.WriteAttributeString("colId", "0");
        if (filter.Kind is PivotFilterKind.Top or PivotFilterKind.Bottom)
        {
            writer.WriteStartElement("top10");
            if (filter.Kind == PivotFilterKind.Bottom)
            {
                writer.WriteAttributeString("top", "0");
            }

            writer.WriteAttributeString("val", filter.Value);
            writer.WriteAttributeString("filterVal", filter.Value);
            writer.WriteEndElement();
        }
        else
        {
            writer.WriteStartElement("customFilters");
            writer.WriteStartElement("customFilter");
            var (op, value) = filter.Kind switch
            {
                PivotFilterKind.BeginsWith => (null, filter.Value + "*"),
                PivotFilterKind.EndsWith => (null, "*" + filter.Value),
                PivotFilterKind.Contains => (null, "*" + filter.Value + "*"),
                PivotFilterKind.NotEquals => ("notEqual", filter.Value),
                PivotFilterKind.GreaterThan => ("greaterThan", filter.Value),
                PivotFilterKind.GreaterOrEqual => ("greaterThanOrEqual", filter.Value),
                PivotFilterKind.LessThan => ("lessThan", filter.Value),
                PivotFilterKind.LessOrEqual => ("lessThanOrEqual", filter.Value),
                _ => ((string?)null, filter.Value),
            };
            if (op is not null)
            {
                writer.WriteAttributeString("operator", op);
            }

            writer.WriteAttributeString("val", value);
            writer.WriteEndElement();
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var stream = new MemoryStream(Encoding.UTF8.GetBytes(xml.Replace("encoding=\"utf-16\"", "encoding=\"UTF-8\"", StringComparison.Ordinal)));
        part.FeedData(stream);
    }
}
