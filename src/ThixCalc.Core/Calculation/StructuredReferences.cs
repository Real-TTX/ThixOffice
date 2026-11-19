using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>
/// Resolves structured references of tables read from files: Tabelle1[Umsatz], Tabelle1[[#Kopfzeilen],[Name]],
/// Tabelle1[@Menge] / [[#This Row],[Menge]], Tabelle1[#All], Tabelle1[[Jan]:[Mär]] (English and German item names).
/// </summary>
internal static class StructuredReferences
{
    public static (Worksheet Sheet, TableDefinition Table)? FindTable(Workbook workbook, string name)
    {
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var table in sheet.Tables)
            {
                if (string.Equals(table.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return (sheet, table);
                }
            }
        }

        return null;
    }

    public static Operand Resolve(StructuredReferenceNode node, EvaluationContext context)
    {
        var found = node.TableName is { } name ? FindTable(context.Workbook, name) : TableAt(context.Sheet, context.Row, context.Column);
        if (found is null)
        {
            return Operand.Error(CellError.Reference);
        }

        var range = ResolveRange(found.Value.Table, node.Specifier, context.Row);
        return range is { } value ? new ReferenceValue(found.Value.Sheet, value) : Operand.Error(CellError.Reference);
    }

    /// <summary>The range a specifier names in <paramref name="table"/> for a formula in <paramref name="row"/>, or null.</summary>
    public static CellRange? ResolveRange(TableDefinition table, string specifier, int row)
    {
        var inner = specifier.Length >= 2 && specifier[0] == '[' && specifier[^1] == ']' ? specifier[1..^1] : specifier;
        var items = SplitItems(inner);
        var firstRow = table.DataRange.FirstRow;
        var lastRow = table.DataRange.LastRow;
        var firstColumn = table.Range.FirstColumn;
        var lastColumn = table.Range.LastColumn;
        var rowsSet = false;
        var columnSet = false;
        foreach (var raw in items)
        {
            var item = raw.Trim();
            if (item.Length > 2 && item[0] == '[' && item[^1] == ']' && item[1] is '#' or '@')
            {
                // [[#Headers],[Name]]: the special item in its own brackets.
                item = item[1..^1];
            }

            if (item.StartsWith('@'))
            {
                (firstRow, lastRow) = (row, row);
                rowsSet = true;
                item = item[1..].Trim();
                if (item.Length == 0)
                {
                    continue;
                }
            }

            if (item.StartsWith('#'))
            {
                var special = Special(table, item, row);
                if (special is null)
                {
                    return null;
                }

                if (!rowsSet)
                {
                    (firstRow, lastRow) = special.Value;
                    rowsSet = true;
                }
                else
                {
                    firstRow = Math.Min(firstRow, special.Value.First);
                    lastRow = Math.Max(lastRow, special.Value.Last);
                }

                continue;
            }

            var colon = FindColon(item);
            var startName = Unbracket(colon < 0 ? item : item[..colon]);
            var endName = Unbracket(colon < 0 ? item : item[(colon + 1)..]);
            var start = ColumnIndex(table, startName);
            var end = ColumnIndex(table, endName);
            if (start < 0 || end < 0)
            {
                return null;
            }

            firstColumn = table.Range.FirstColumn + Math.Min(start, end);
            lastColumn = table.Range.FirstColumn + Math.Max(start, end);
            columnSet = true;
        }

        _ = columnSet;
        return firstRow > lastRow ? null : new CellRange(firstRow, firstColumn, lastRow, lastColumn);
    }

    private static (int First, int Last)? Special(TableDefinition table, string item, int row) => item.ToUpperInvariant() switch
    {
        "#ALL" or "#ALLE" => (table.Range.FirstRow, table.Range.LastRow),
        "#DATA" or "#DATEN" => (table.DataRange.FirstRow, table.DataRange.LastRow),
        "#HEADERS" or "#KOPFZEILEN" => table.HeaderRowCount > 0 ? (table.Range.FirstRow, table.Range.FirstRow) : null,
        "#TOTALS" or "#ERGEBNISSE" => table.TotalsRowCount > 0 ? (table.Range.LastRow, table.Range.LastRow) : null,
        "#THIS ROW" or "#DIESE ZEILE" => (row, row),
        _ => null,
    };

    private static int ColumnIndex(TableDefinition table, string name)
    {
        for (var index = 0; index < table.Columns.Count; index++)
        {
            if (string.Equals(table.Columns[index], name, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        return -1;
    }

    private static string Unbracket(string text)
    {
        text = text.Trim();
        if (text.StartsWith('[') && text.EndsWith(']'))
        {
            text = text[1..^1];
        }

        // ' escapes [ ] # ' in column names.
        var builder = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\'' && index + 1 < text.Length)
            {
                index++;
            }

            builder.Append(text[index]);
        }

        return builder.ToString();
    }

    private static int FindColon(string item)
    {
        var depth = 0;
        for (var index = 0; index < item.Length; index++)
        {
            switch (item[index])
            {
                case '\'':
                    index++;
                    break;
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    break;
                case ':' when depth == 0:
                    return index;
            }
        }

        return -1;
    }

    /// <summary>Top-level items of "[#Headers],[Name]" (or the single item "Name").</summary>
    private static List<string> SplitItems(string inner)
    {
        if (!inner.Contains('['))
        {
            return inner.Length == 0 ? [] : [inner];
        }

        var items = new List<string>();
        var depth = 0;
        var start = 0;
        for (var index = 0; index < inner.Length; index++)
        {
            switch (inner[index])
            {
                case '\'':
                    index++;
                    break;
                case '[':
                    depth++;
                    break;
                case ']':
                    depth--;
                    break;
                case ',' or ';' when depth == 0:
                    items.Add(inner[start..index]);
                    start = index + 1;
                    break;
            }
        }

        items.Add(inner[start..]);
        return items.Select(item => item.Trim()).Where(item => item.Length > 0).ToList();
    }

    private static (Worksheet Sheet, TableDefinition Table)? TableAt(Worksheet sheet, int row, int column)
    {
        foreach (var table in sheet.Tables)
        {
            if (table.Range.Contains(row, column))
            {
                return (sheet, table);
            }
        }

        return null;
    }
}
