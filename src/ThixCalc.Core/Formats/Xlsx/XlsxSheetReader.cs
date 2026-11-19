using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>Reads one worksheet part (streaming) into a <see cref="Worksheet"/>.</summary>
internal static class XlsxSheetReader
{
    /// <summary>The worksheet children the model owns (everything else is kept as XML).</summary>
    internal static readonly HashSet<string> OwnedElements =
    [
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "sheetProtection", "autoFilter", "sortState",
        "mergeCells", "conditionalFormatting", "dataValidations", "hyperlinks", "legacyDrawing", "tableParts",
    ];

    public static void Read(Worksheet sheet, WorksheetPart part, ReadContext context)
    {
        var anchors = new List<(int Row, int Column, CellRange Range)>();
        var sharedFormulas = new Dictionary<uint, Formula>();
        using (var reader = OpenXmlReader.Create(part))
        {
            while (reader.Read())
            {
                if (!reader.IsStartElement)
                {
                    continue;
                }

                if (reader.ElementType == typeof(X.Worksheet))
                {
                    sheet.RootNamespaces.AddRange(reader.NamespaceDeclarations.Select(declaration => (declaration.Key, declaration.Value)));
                    sheet.RootAttributes.AddRange(reader.Attributes.Select(attribute => (attribute.Prefix, attribute.LocalName, attribute.NamespaceUri, attribute.Value ?? string.Empty)));
                    continue;
                }

                if (reader.Depth != 1)
                {
                    continue;
                }

                if (reader.ElementType == typeof(X.SheetData))
                {
                    ReadSheetData(reader, sheet, context, sharedFormulas, anchors);
                    continue;
                }

                var element = reader.LoadCurrentElement();
                if (element is not null)
                {
                    ReadElement(sheet, part, element, context);
                }
            }
        }

        foreach (var (row, column, range) in anchors)
        {
            sheet.MarkSpilled(range, row, column);
        }

        ReadComments(sheet, part);
        ReadTables(sheet, part);
    }

    private static void ReadSheetData(OpenXmlReader reader, Worksheet sheet, ReadContext context, Dictionary<uint, Formula> sharedFormulas,
        List<(int Row, int Column, CellRange Range)> anchors)
    {
        var nextRow = 0;
        while (reader.Read())
        {
            if (reader.ElementType == typeof(X.SheetData) && reader.IsEndElement)
            {
                return;
            }

            if (reader.ElementType != typeof(X.Row) || !reader.IsStartElement)
            {
                continue;
            }

            var row = (X.Row)reader.LoadCurrentElement()!;
            var rowIndex = row.RowIndex?.Value is { } index ? (int)index - 1 : nextRow;
            nextRow = rowIndex + 1;
            ReadRowInfo(sheet, row, rowIndex, context);
            var nextColumn = 0;
            foreach (var cell in row.Elements<X.Cell>())
            {
                var address = cell.CellReference?.Value is { } reference && CellAddress.TryParse(reference) is { } parsed
                    ? parsed
                    : new CellAddress(rowIndex, nextColumn);
                nextColumn = address.Column + 1;
                ReadCell(sheet, cell, address, context, sharedFormulas, anchors);
            }
        }
    }

    private static void ReadRowInfo(Worksheet sheet, X.Row row, int rowIndex, ReadContext context)
    {
        var height = row.Height?.Value;
        var info = new AxisInfo(
            Size: height is { } value && value != sheet.Rows.DefaultSize ? value : null,
            Hidden: row.Hidden?.Value ?? false,
            OutlineLevel: row.OutlineLevel?.Value ?? 0,
            Collapsed: row.Collapsed?.Value ?? false,
            StyleId: row.CustomFormat?.Value == true ? context.Style(row.StyleIndex?.Value) : null,
            CustomSize: row.CustomHeight?.Value ?? false);
        if (!info.IsDefault)
        {
            sheet.Rows.Set(rowIndex, info);
        }
    }

    private static void ReadCell(Worksheet sheet, X.Cell cell, CellAddress address, ReadContext context, Dictionary<uint, Formula> sharedFormulas,
        List<(int Row, int Column, CellRange Range)> anchors)
    {
        var style = context.Style(cell.StyleIndex?.Value);
        var value = ReadValue(cell, context);
        var formulaElement = cell.CellFormula;
        if (formulaElement is null)
        {
            if (!value.IsBlank || style != 0)
            {
                sheet.LoadValue(address.Row, address.Column, value, style);
            }

            return;
        }

        var type = formulaElement.FormulaType?.Value;
        var text = formulaElement.Text;
        Formula? formula = null;
        if (type == X.CellFormulaValues.Shared && formulaElement.SharedIndex?.Value is { } sharedIndex)
        {
            if (!string.IsNullOrEmpty(text))
            {
                formula = Parse(text, sheet, address, context);
                if (formula is not null)
                {
                    sharedFormulas[sharedIndex] = formula;
                }
            }
            else
            {
                sharedFormulas.TryGetValue(sharedIndex, out formula);
            }
        }
        else if (!string.IsNullOrEmpty(text) && type != X.CellFormulaValues.DataTable)
        {
            formula = Parse(text, sheet, address, context);
        }

        if (formula is null)
        {
            if (type == X.CellFormulaValues.DataTable)
            {
                context.Note("Datentabellen (Was-wäre-wenn-Analyse; Werte bleiben erhalten)");
            }

            sheet.LoadValue(address.Row, address.Column, value, style);
            return;
        }

        var isArray = type == X.CellFormulaValues.Array;
        var isDynamic = isArray && cell.CellMetaIndex?.Value is { } metadata && context.DynamicArrayMetadata.Contains(metadata);
        var range = isArray && formulaElement.Reference?.Value is { } reference ? CellRange.TryParse(reference) : null;
        CellRange? arrayRange = isArray && !isDynamic ? range : null;
        CellRange? spillRange = isDynamic && range is { IsSingleCell: false } ? range : null;
        if (isDynamic || !isArray)
        {
            // Formulas without the dynamic-array flag are legacy: implicit intersection.
        }

        var cached = value.IsBlank ? CellValue.Zero : value;
        if (isDynamic && spillRange is null && value.IsError && value.Error == CellError.Spill)
        {
            cached = value;
        }

        sheet.LoadFormula(address.Row, address.Column, formula, cached, style, isDynamic, arrayRange, spillRange);
        if ((arrayRange ?? spillRange) is { } covered && !covered.IsSingleCell)
        {
            anchors.Add((address.Row, address.Column, covered));
        }
    }

    private static Formula? Parse(string text, Worksheet sheet, CellAddress address, ReadContext context)
    {
        try
        {
            return FormulaParser.Parse(text, sheet.Workbook, sheet, address);
        }
        catch (FormulaParseException)
        {
            context.Note("Formeln, die ThixCalc nicht lesen kann (ihre Werte bleiben erhalten)");
            return null;
        }
    }

    private static CellValue ReadValue(X.Cell cell, ReadContext context)
    {
        var type = cell.DataType?.Value;
        if (type == X.CellValues.InlineString)
        {
            return cell.InlineString is { } inline ? CellValue.FromText(XlsxReader.TextOf(inline)) : CellValue.EmptyText;
        }

        var text = cell.CellValue?.Text;
        if (text is null)
        {
            return CellValue.Blank;
        }

        if (type == X.CellValues.SharedString)
        {
            return int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < context.SharedStrings.Length
                ? CellValue.FromText(context.SharedStrings[index])
                : CellValue.EmptyText;
        }

        if (type == X.CellValues.String)
        {
            return CellValue.FromText(XlsxReader.Unescape(text));
        }

        if (type == X.CellValues.Boolean)
        {
            return CellValue.FromBoolean(text == "1" || string.Equals(text, "true", StringComparison.OrdinalIgnoreCase));
        }

        if (type == X.CellValues.Error)
        {
            return CellValue.FromError(CellErrors.Parse(text) ?? CellError.Value);
        }

        if (type == X.CellValues.Date)
        {
            return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out var date)
                ? CellValue.FromNumber(DateSerial.FromDateTime(date, context.Workbook.Date1904))
                : CellValue.FromText(text);
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? CellValue.FromNumber(number) : CellValue.FromText(text);
    }

    // ---------------------------------------------------------------- the other worksheet elements

    private static void ReadElement(Worksheet sheet, WorksheetPart part, OpenXmlElement element, ReadContext context)
    {
        switch (element)
        {
            case X.SheetProperties properties:
                sheet.TabColor = XlsxStyles.ReadColor(properties.TabColor);
                sheet.SheetPropertiesXml = properties.OuterXml;
                break;
            case X.SheetDimension:
                break;
            case X.SheetViews views:
                ReadView(sheet, views);
                break;
            case X.SheetFormatProperties format:
                sheet.Rows.DefaultSize = format.DefaultRowHeight?.Value ?? 15;
                sheet.CustomDefaultRowHeight = format.CustomHeight?.Value ?? false;
                sheet.ZeroHeightRows = format.ZeroHeight?.Value ?? false;
                sheet.Columns.DefaultSize = format.DefaultColumnWidth?.Value ?? (format.BaseColumnWidth?.Value is { } baseWidth ? baseWidth + 0.43 : 8.43);
                break;
            case X.Columns columns:
                foreach (var column in columns.Elements<X.Column>())
                {
                    var first = (int)(column.Min?.Value ?? 1) - 1;
                    var last = Math.Min((int)(column.Max?.Value ?? 1) - 1, SheetLimits.LastColumn);
                    var info = new AxisInfo(
                        Size: column.CustomWidth?.Value == true || column.Width is not null ? column.Width?.Value : null,
                        Hidden: column.Hidden?.Value ?? false,
                        OutlineLevel: column.OutlineLevel?.Value ?? 0,
                        Collapsed: column.Collapsed?.Value ?? false,
                        StyleId: column.Style?.Value is { } style && style != 0 ? context.Style(style) : null,
                        CustomSize: column.CustomWidth?.Value ?? false,
                        BestFit: column.BestFit?.Value ?? false);
                    if (last - first > 1000 && info is { Size: null, Hidden: false, OutlineLevel: 0 } && info.StyleId is null)
                    {
                        continue;
                    }

                    sheet.Columns.Update(first, last, _ => info);
                }

                break;
            case X.SheetProtection protection:
                sheet.ProtectionXml = protection.OuterXml;
                break;
            case X.AutoFilter filter:
                sheet.AutoFilter = XlsxFeatures.ReadAutoFilter(filter);
                break;
            case X.SortState sort:
                sheet.SortState = XlsxFeatures.ReadSortState(sort);
                break;
            case X.MergeCells merges:
                foreach (var merge in merges.Elements<X.MergeCell>())
                {
                    if (merge.Reference?.Value is { } reference && CellRange.TryParse(reference) is { } range)
                    {
                        sheet.Merges.Add(range);
                    }
                }

                break;
            case X.ConditionalFormatting formatting:
                if (XlsxFeatures.ReadConditionalFormat(formatting, context) is { } conditional)
                {
                    sheet.ConditionalFormats.Add(conditional);
                }

                break;
            case X.DataValidations validations:
                foreach (var validation in validations.Elements<X.DataValidation>())
                {
                    sheet.Validations.Add(XlsxFeatures.ReadValidation(validation, sheet));
                }

                break;
            case X.Hyperlinks hyperlinks:
                foreach (var hyperlink in hyperlinks.Elements<X.Hyperlink>())
                {
                    if (hyperlink.Reference?.Value is not { } reference || CellRange.TryParse(reference) is not { } range)
                    {
                        continue;
                    }

                    string? target = null;
                    if (hyperlink.Id?.Value is { } id)
                    {
                        target = part.HyperlinkRelationships.FirstOrDefault(relationship => relationship.Id == id)?.Uri.OriginalString;
                    }

                    sheet.Hyperlinks.Add(new Hyperlink(range, target, hyperlink.Location?.Value, hyperlink.Display?.Value, hyperlink.Tooltip?.Value));
                }

                break;
            case X.LegacyDrawing legacy:
                sheet.LegacyDrawingId = legacy.Id?.Value;
                break;
            case X.TableParts:
                break;
            default:
                sheet.PreservedElements.Add((element.LocalName, element.OuterXml));
                if (element is X.WorksheetExtensionList extensions && extensions.OuterXml.Contains("sparkline", StringComparison.OrdinalIgnoreCase))
                {
                    context.Note("Sparklines");
                }

                break;
        }
    }

    private static void ReadView(Worksheet sheet, X.SheetViews views)
    {
        var view = views.Elements<X.SheetView>().FirstOrDefault();
        if (view is null)
        {
            return;
        }

        var model = new SheetView
        {
            TabSelected = view.TabSelected?.Value ?? false,
            ShowGridLines = view.ShowGridLines?.Value ?? true,
            ShowHeaders = view.ShowRowColHeaders?.Value ?? true,
            ShowZeros = view.ShowZeros?.Value ?? true,
            ShowFormulas = view.ShowFormulas?.Value ?? false,
            RightToLeft = view.RightToLeft?.Value ?? false,
            Zoom = (int)(view.ZoomScale?.Value ?? 100),
            Kind = view.View?.Value == X.SheetViewValues.PageLayout ? SheetViewKind.PageLayout
                : view.View?.Value == X.SheetViewValues.PageBreakPreview ? SheetViewKind.PageBreakPreview : SheetViewKind.Normal,
            TopLeft = view.TopLeftCell?.Value is { } topLeft && CellAddress.TryParse(topLeft) is { } address ? address : default,
        };
        if (view.Pane is { } pane)
        {
            var frozen = pane.State?.Value == X.PaneStateValues.Frozen || pane.State?.Value == X.PaneStateValues.FrozenSplit;
            if (frozen)
            {
                model = model with
                {
                    FrozenColumns = (int)(pane.HorizontalSplit?.Value ?? 0),
                    FrozenRows = (int)(pane.VerticalSplit?.Value ?? 0),
                    TopLeft = pane.TopLeftCell?.Value is { } paneTopLeft && CellAddress.TryParse(paneTopLeft) is { } paneAddress ? paneAddress : model.TopLeft,
                };
            }
            else
            {
                model = model with { SplitX = pane.HorizontalSplit?.Value ?? 0, SplitY = pane.VerticalSplit?.Value ?? 0 };
            }
        }

        var selection = view.Elements<X.Selection>().LastOrDefault();
        if (selection is not null)
        {
            var active = selection.ActiveCell?.Value is { } activeCell && CellAddress.TryParse(activeCell) is { } activeAddress ? activeAddress : default;
            var ranges = selection.SequenceOfReferences?.InnerText is { } sqref ? CellRange.ParseList(sqref) : [];
            model = model with { ActiveCell = active, Selection = ranges.Count > 0 ? ranges : [CellRange.Cell(active)] };
        }

        sheet.View = model;
    }

    private static void ReadComments(Worksheet sheet, WorksheetPart part)
    {
        var comments = part.WorksheetCommentsPart?.Comments;
        if (comments is null)
        {
            return;
        }

        var authors = comments.Authors?.Elements<X.Author>().Select(author => author.Text).ToList() ?? [];
        foreach (var comment in comments.CommentList?.Elements<X.Comment>() ?? [])
        {
            if (comment.Reference?.Value is not { } reference || CellAddress.TryParse(reference) is not { } address)
            {
                continue;
            }

            var author = comment.AuthorId?.Value is { } id && id < authors.Count ? authors[(int)id] : string.Empty;
            var text = comment.CommentText is { } commentText ? XlsxReader.TextOf(commentText) : string.Empty;
            sheet.Comments[address] = new CellComment(author, text);
        }

        sheet.OriginalCommentAddresses = sheet.Comments.Keys.ToHashSet();
    }

    private static void ReadTables(Worksheet sheet, WorksheetPart part)
    {
        foreach (var tablePart in part.TableDefinitionParts)
        {
            var table = tablePart.Table;
            if (table?.Reference?.Value is not { } reference || CellRange.TryParse(reference) is not { } range)
            {
                continue;
            }

            var columns = table.TableColumns?.Elements<X.TableColumn>().Select(column => column.Name?.Value ?? string.Empty).ToList() ?? [];
            sheet.Tables.Add(new TableDefinition(table.DisplayName?.Value ?? table.Name?.Value ?? "Tabelle", range, columns,
                (int)(table.HeaderRowCount?.Value ?? 1), (int)(table.TotalsRowCount?.Value ?? 0))
            {
                PartUri = tablePart.Uri.ToString(),
            });
        }
    }
}
