using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Model;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>
/// Table parts (x:table): read into <see cref="TableDefinition"/>s (range, columns, header and total rows, total functions and
/// labels, style and its options, the filter), written back into their parts – what the model does not own (column formats,
/// calculated column formulas, extensions) stays – new tables get new parts with workbook-unique ids, deleted ones lose theirs.
/// </summary>
internal static class XlsxTables
{
    public static void Read(Worksheet sheet, WorksheetPart part)
    {
        foreach (var tablePart in part.TableDefinitionParts)
        {
            var table = tablePart.Table;
            if (table?.Reference?.Value is not { } reference || CellRange.TryParse(reference) is not { } range)
            {
                continue;
            }

            var columns = table.TableColumns?.Elements<X.TableColumn>().ToList() ?? [];
            var style = table.TableStyleInfo;
            sheet.Tables.Add(new TableDefinition(table.DisplayName?.Value ?? table.Name?.Value ?? "Tabelle", range, columns.Select(column => column.Name?.Value ?? string.Empty).ToList(),
                (int)(table.HeaderRowCount?.Value ?? 1), (int)(table.TotalsRowCount?.Value ?? 0))
            {
                PartUri = tablePart.Uri.ToString(),
                FileId = table.Id?.Value ?? 0,
                Style = style is null ? null : style.Name?.Value,
                BandedRows = style?.ShowRowStripes?.Value ?? false,
                BandedColumns = style?.ShowColumnStripes?.Value ?? false,
                FirstColumn = style?.ShowFirstColumn?.Value ?? false,
                LastColumn = style?.ShowLastColumn?.Value ?? false,
                ShowFilterButton = table.AutoFilter is not null,
                Filter = table.AutoFilter is { } filter ? XlsxFeatures.ReadAutoFilter(filter) : null,
                TotalsFunctions = columns.Select(column => column.TotalsRowFunction?.Value is { } function && function != X.TotalsRowFunctionValues.None ? FunctionName(function) : null).ToList(),
                TotalsLabels = columns.Select(column => column.TotalsRowLabel?.Value).ToList(),
            });
        }
    }

    /// <summary>Brings the sheet's table parts in line with its tables (before the worksheet's tableParts are written).</summary>
    public static void Write(Worksheet sheet, WorksheetPart part, WriteContext context)
    {
        var parts = part.TableDefinitionParts.ToList();
        foreach (var orphan in parts.Where(tablePart => !sheet.Tables.Any(table => table.PartUri == tablePart.Uri.ToString())).ToList())
        {
            part.DeletePart(orphan);
            parts.Remove(orphan);
        }

        foreach (var table in sheet.Tables)
        {
            var tablePart = parts.FirstOrDefault(candidate => candidate.Uri.ToString() == table.PartUri);
            var element = tablePart?.Table;
            if (tablePart is null || element is null)
            {
                tablePart = part.AddNewPart<TableDefinitionPart>();
                element = new X.Table { Id = context.NextTableId(table.FileId) };
                tablePart.Table = element;
            }

            Fill(element, table);
            element.Save();
        }
    }

    private static void Fill(X.Table element, TableDefinition table)
    {
        element.Name = table.Name;
        element.DisplayName = table.Name;
        element.Reference = table.Range.ToString();
        element.HeaderRowCount = table.HeaderRowCount == 1 ? null : (uint)table.HeaderRowCount;
        element.TotalsRowCount = table.TotalsRowCount > 0 ? (uint)table.TotalsRowCount : null;
        element.TotalsRowShown = table.TotalsRowCount > 0 ? null : false;

        // autoFilter, (sortState), tableColumns, tableStyleInfo, extLst – in schema order.
        element.AutoFilter = table.HasHeader && table.ShowFilterButton ? Filter(table) : null;
        var oldColumns = element.TableColumns?.Elements<X.TableColumn>().ToList() ?? [];
        var nextId = oldColumns.Select(column => column.Id?.Value ?? 0).DefaultIfEmpty(0u).Max() + 1;
        var columns = new X.TableColumns { Count = (uint)table.Columns.Count };
        for (var index = 0; index < table.Columns.Count; index++)
        {
            var name = table.Columns[index];
            var column = index < oldColumns.Count && oldColumns[index].Name?.Value == name
                ? (X.TableColumn)oldColumns[index].CloneNode(true)
                : new X.TableColumn { Id = nextId++, Name = name };
            var function = index < table.TotalsFunctions.Count ? table.TotalsFunctions[index] : null;
            var label = index < table.TotalsLabels.Count ? table.TotalsLabels[index] : null;
            column.TotalsRowFunction = function is null ? null : FunctionValue(function);
            column.TotalsRowLabel = label;
            columns.Append(column);
        }

        element.TableColumns = columns;
        element.TableStyleInfo = table.Style is null && !table.BandedRows && !table.BandedColumns
            ? null
            : new X.TableStyleInfo
            {
                Name = table.Style,
                ShowFirstColumn = table.FirstColumn,
                ShowLastColumn = table.LastColumn,
                ShowRowStripes = table.BandedRows,
                ShowColumnStripes = table.BandedColumns,
            };
    }

    private static X.AutoFilter Filter(TableDefinition table)
    {
        var filter = (table.Filter ?? new AutoFilter(table.FilterRange, [])) with { Range = table.FilterRange };
        var builder = new StringBuilder();
        using (var writer = XmlWriter.Create(builder, new XmlWriterSettings { OmitXmlDeclaration = true }))
        {
            XlsxFeatures.WriteAutoFilter(writer, filter);
        }

        return new X.AutoFilter(builder.ToString());
    }

    private static string FunctionName(X.TotalsRowFunctionValues function) =>
        function == X.TotalsRowFunctionValues.Sum ? "sum"
        : function == X.TotalsRowFunctionValues.Average ? "average"
        : function == X.TotalsRowFunctionValues.Count ? "count"
        : function == X.TotalsRowFunctionValues.CountNumbers ? "countNums"
        : function == X.TotalsRowFunctionValues.Maximum ? "max"
        : function == X.TotalsRowFunctionValues.Minimum ? "min"
        : function == X.TotalsRowFunctionValues.StandardDeviation ? "stdDev"
        : function == X.TotalsRowFunctionValues.Variance ? "var"
        : "custom";

    private static X.TotalsRowFunctionValues FunctionValue(string name) => name switch
    {
        "sum" => X.TotalsRowFunctionValues.Sum,
        "average" => X.TotalsRowFunctionValues.Average,
        "count" => X.TotalsRowFunctionValues.Count,
        "countNums" => X.TotalsRowFunctionValues.CountNumbers,
        "max" => X.TotalsRowFunctionValues.Maximum,
        "min" => X.TotalsRowFunctionValues.Minimum,
        "stdDev" => X.TotalsRowFunctionValues.StandardDeviation,
        "var" => X.TotalsRowFunctionValues.Variance,
        _ => X.TotalsRowFunctionValues.Custom,
    };
}
