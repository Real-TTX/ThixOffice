using System.Globalization;
using ThixCalc.Core.Calculation;

namespace ThixCalc.Core.Model.Pivot;

/// <summary>One column of the source as the cache holds it: its name, its distinct items, the number format of its cells.</summary>
public sealed class PivotCacheField(string name, IReadOnlyList<CellValue> items, string? numberFormat)
{
    public string Name { get; } = name;

    /// <summary>The distinct values in the order they were first met (the records refer to them by index).</summary>
    public IReadOnlyList<CellValue> Items { get; } = items;

    /// <summary>The number format of the source column's first value (dates show as dates).</summary>
    public string? NumberFormat { get; } = numberFormat;

    public bool IsNumeric => Items.Count > 0 && Items.All(item => item.IsNumber || item.IsBlank);

    public bool IsDate => IsNumeric && NumberFormat is { } format && Formatting.NumberFormat.Parse(format).IsDateTime;
}

/// <summary>
/// The PivotCache: a copy of the source (a range or a table) – the fields and, per record, the index of each field's item. Pivot
/// tables compute from it; Aktualisieren builds a new one from the source (the old one stays for undo). Immutable.
/// </summary>
public sealed class PivotCache
{
    private PivotCache(int id, string sourceSheet, CellRange sourceRange, string? sourceTable, IReadOnlyList<PivotCacheField> fields, int[][] records, int count)
    {
        Id = id;
        SourceSheet = sourceSheet;
        SourceRange = sourceRange;
        SourceTable = sourceTable;
        Fields = fields;
        Records = records;
        RecordCount = count;
    }

    public int Id { get; }

    /// <summary>The source: a sheet and range ("Daten!A1:F500"), or a table by name (then the range is the table's).</summary>
    public string SourceSheet { get; }

    public CellRange SourceRange { get; }

    public string? SourceTable { get; }

    public IReadOnlyList<PivotCacheField> Fields { get; }

    /// <summary>Per field, per record the index of its item in <see cref="PivotCacheField.Items"/>.</summary>
    internal int[][] Records { get; }

    public int RecordCount { get; }

    /// <summary>Whether records were read (a file may hold the definition only; Aktualisieren reads the source).</summary>
    public bool HasRecords => RecordCount > 0 || Fields.Count == 0;

    /// <summary>The part the cache definition was read from or written to.</summary>
    public string? PartUri { get; internal set; }

    /// <summary>Whether the cache part in the package holds this cache (read or saved; a refresh makes a new cache).</summary>
    internal bool IsAsRead { get; set; }

    /// <summary>Marks the cache as the one in a package part.</summary>
    internal PivotCache WithFile(string uri)
    {
        PartUri = uri;
        IsAsRead = true;
        return this;
    }

    /// <summary>The cache definition and records as read (written back unchanged until the cache is refreshed).</summary>
    public string? SourceXml { get; init; }

    public string? RecordsXml { get; init; }

    /// <summary>The source as a text: "Daten!$A$1:$F$500" or the table's name.</summary>
    public string SourceText => SourceTable ?? $"{FormatSheet(SourceSheet)}!{SourceRange.ToAbsoluteString()}";

    public CellValue Value(int field, int record) => Fields[field].Items[Records[field][record]];

    /// <summary>The field by its name (case does not matter), or −1.</summary>
    public int IndexOf(string name)
    {
        for (var index = 0; index < Fields.Count; index++)
        {
            if (string.Equals(Fields[index].Name, name, StringComparison.CurrentCultureIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    /// <summary>Reads the source: the first row are the field names (empty or double names get numbers), the rest the records.</summary>
    public static PivotCache Build(Workbook workbook, int id, string sheetName, CellRange range, string? table = null)
    {
        var sheet = workbook.FindSheet(sheetName) ?? throw new ThixOffice.Core.Models.BusinessException("Der Bezug der Datenquelle ist ungültig.", "source");
        if (range.RowCount < 2)
        {
            throw new ThixOffice.Core.Models.BusinessException("Für eine PivotTable braucht die Datenquelle eine Überschriftenzeile und mindestens eine Datenzeile.", "source");
        }

        var columns = range.ColumnCount;
        var names = new List<string>();
        for (var column = 0; column < columns; column++)
        {
            var header = sheet.GetValue(range.FirstRow, range.FirstColumn + column);
            var name = header.IsBlank ? string.Empty : Coercion.ToText(header, Globalization.CalcLocale.German).Trim();
            if (name.Length == 0)
            {
                throw new ThixOffice.Core.Models.BusinessException("Der Name des PivotTable-Felds ist ungültig. Jede Spalte der Datenquelle braucht eine Überschrift.", "source");
            }

            var unique = name;
            for (var number = 2; names.Contains(unique, StringComparer.CurrentCultureIgnoreCase); number++)
            {
                unique = name + number.ToString(CultureInfo.InvariantCulture);
            }

            names.Add(unique);
        }

        var count = range.RowCount - 1;
        var records = new int[columns][];
        var fields = new List<PivotCacheField>();
        for (var column = 0; column < columns; column++)
        {
            var items = new List<CellValue>();
            var index = new Dictionary<string, int>(StringComparer.Ordinal);
            var values = records[column] = new int[count];
            string? format = null;
            for (var record = 0; record < count; record++)
            {
                var row = range.FirstRow + 1 + record;
                var value = sheet.GetValue(row, range.FirstColumn + column);
                var key = PivotItems.KeyOf(value);
                if (!index.TryGetValue(key, out var item))
                {
                    item = items.Count;
                    index[key] = item;
                    items.Add(value);
                    if (format is null && value.IsNumber)
                    {
                        format = sheet.GetStyle(row, range.FirstColumn + column).NumberFormat;
                    }
                }

                values[record] = item;
            }

            fields.Add(new PivotCacheField(names[column], items, format));
        }

        return new PivotCache(id, sheet.Name, range, table, fields, records, count);
    }

    /// <summary>A cache read from a file: the fields with their shared items and the records (item indices; may be none).</summary>
    internal static PivotCache FromFile(int id, string sheet, CellRange range, string? table, IReadOnlyList<PivotCacheField> fields, int[][] records, int count) =>
        new(id, sheet, range, table, fields, records, count);

    /// <summary>The source range now: a table's current range, else the stored range.</summary>
    public CellRange CurrentRange(Workbook workbook)
    {
        if (SourceTable is { } name)
        {
            foreach (var sheet in workbook.Sheets)
            {
                if (sheet.Tables.FirstOrDefault(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase)) is { } table)
                {
                    return table.HasTotals ? new CellRange(table.Range.FirstRow, table.Range.FirstColumn, table.Range.LastRow - 1, table.Range.LastColumn) : table.Range;
                }
            }
        }

        return SourceRange;
    }

    /// <summary>The sheet of a table source now (tables may move with their sheet), else the stored one.</summary>
    public string CurrentSheet(Workbook workbook)
    {
        if (SourceTable is { } name)
        {
            foreach (var sheet in workbook.Sheets)
            {
                if (sheet.Tables.Any(table => string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase)))
                {
                    return sheet.Name;
                }
            }
        }

        return SourceSheet;
    }

    private static string FormatSheet(string name) =>
        name.All(character => char.IsLetterOrDigit(character) || character == '_') && !char.IsDigit(name[0]) ? name : "'" + name.Replace("'", "''", StringComparison.Ordinal) + "'";
}

/// <summary>Labels and keys of pivot items.</summary>
public static class PivotItems
{
    /// <summary>"(Leer)": the item of empty cells.</summary>
    public const string BlankLabel = "(Leer)";

    /// <summary>The key of a value: texts without case, numbers by value (the same item for "Nord" and "nord").</summary>
    public static string KeyOf(CellValue value) => value.Kind switch
    {
        CellValueKind.Text => "t" + value.Text.ToUpperInvariant(),
        CellValueKind.Number => "n" + value.Number.ToString("R", CultureInfo.InvariantCulture),
        CellValueKind.Boolean => value.Boolean ? "bTRUE" : "bFALSE",
        CellValueKind.Error => "e" + (int)value.Error,
        _ => "_",
    };
}
