using System.Globalization;
using System.Text;
using System.Xml;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>
/// Streams one worksheet as XML in schema order: the elements the model owns from the model, the others back from the XML
/// they were read as. Formulas filled down a column (one shared instance) are written as shared formulas.
/// </summary>
internal static class XlsxSheetWriter
{
    private static readonly string[] SchemaOrder =
    [
        "sheetPr", "dimension", "sheetViews", "sheetFormatPr", "cols", "sheetData", "sheetCalcPr", "sheetProtection", "protectedRanges",
        "scenarios", "autoFilter", "sortState", "dataConsolidate", "customSheetViews", "mergeCells", "phoneticPr", "conditionalFormatting",
        "dataValidations", "hyperlinks", "printOptions", "pageMargins", "pageSetup", "headerFooter", "rowBreaks", "colBreaks",
        "customProperties", "cellWatches", "ignoredErrors", "smartTags", "drawing", "legacyDrawing", "legacyDrawingHF", "drawingHF",
        "picture", "oleObjects", "controls", "webPublishItems", "tableParts", "extLst",
    ];

    public static void Write(Worksheet sheet, WorksheetPart part, WriteContext context)
    {
        var hyperlinkIds = WriteHyperlinkRelationships(sheet, part);
        var legacyDrawingId = WriteComments(sheet, part);
        var drawing = XlsxDrawings.Write(sheet, part, context);
        UpdateTables(sheet, part);
        var shared = SharedFormulaGroups(sheet, context);

        using var stream = new MemoryStream();
        var settings = new XmlWriterSettings { Encoding = new UTF8Encoding(false) };
        using (var writer = XmlWriter.Create(stream, settings))
        {
            writer.WriteStartDocument(true);
            writer.WriteStartElement("worksheet", XlsxNamespaces.Main);
            WriteRootAttributes(writer, sheet);
            foreach (var name in SchemaOrder)
            {
                switch (name)
                {
                    case "sheetPr":
                        WriteSheetProperties(writer, sheet);
                        break;
                    case "dimension":
                        writer.WriteStartElement("dimension", XlsxNamespaces.Main);
                        writer.WriteAttributeString("ref", (sheet.UsedRange ?? CellRange.Cell(0, 0)).ToString());
                        writer.WriteEndElement();
                        break;
                    case "sheetViews":
                        WriteView(writer, sheet);
                        break;
                    case "sheetFormatPr":
                        WriteFormat(writer, sheet);
                        break;
                    case "cols":
                        WriteColumns(writer, sheet);
                        break;
                    case "sheetData":
                        WriteSheetData(writer, sheet, context, shared);
                        break;
                    case "sheetProtection" when sheet.ProtectionXml is { } protection:
                        writer.WriteRaw(protection);
                        break;
                    case "autoFilter" when sheet.AutoFilter is { } filter:
                        XlsxFeatures.WriteAutoFilter(writer, filter);
                        break;
                    case "sortState" when sheet.SortState is { } sort:
                        XlsxFeatures.WriteSortState(writer, sort);
                        break;
                    case "mergeCells" when sheet.Merges.Count > 0:
                        writer.WriteStartElement("mergeCells", XlsxNamespaces.Main);
                        writer.WriteAttributeString("count", sheet.Merges.Count.ToString(CultureInfo.InvariantCulture));
                        foreach (var merge in sheet.Merges.Ranges)
                        {
                            writer.WriteStartElement("mergeCell", XlsxNamespaces.Main);
                            writer.WriteAttributeString("ref", merge.ToString());
                            writer.WriteEndElement();
                        }

                        writer.WriteEndElement();
                        break;
                    case "conditionalFormatting":
                        XlsxFeatures.WriteConditionalFormats(writer, sheet);
                        break;
                    case "dataValidations":
                        XlsxFeatures.WriteValidations(writer, sheet);
                        break;
                    case "hyperlinks" when sheet.Hyperlinks.Count > 0:
                        WriteHyperlinks(writer, sheet, hyperlinkIds);
                        break;
                    case "printOptions" or "pageMargins" or "pageSetup" or "headerFooter":
                        WritePageSetup(writer, sheet, name);
                        break;
                    case "drawing" when drawing.Handled:
                        if (drawing.RelationshipId is { } drawingId)
                        {
                            writer.WriteStartElement("drawing", XlsxNamespaces.Main);
                            writer.WriteAttributeString("r", "id", XlsxNamespaces.Relationships, drawingId);
                            writer.WriteEndElement();
                        }

                        break;
                    case "legacyDrawing" when legacyDrawingId is not null:
                        writer.WriteStartElement("legacyDrawing", XlsxNamespaces.Main);
                        writer.WriteAttributeString("r", "id", XlsxNamespaces.Relationships, legacyDrawingId);
                        writer.WriteEndElement();
                        break;
                    case "tableParts":
                        WriteTableParts(writer, part);
                        break;
                    default:
                        if (!XlsxSheetReader.OwnedElements.Contains(name))
                        {
                            foreach (var (preservedName, xml) in sheet.PreservedElements)
                            {
                                if (preservedName == name)
                                {
                                    writer.WriteRaw(xml);
                                }
                            }
                        }

                        break;
                }
            }

            writer.WriteEndElement();
        }

        stream.Position = 0;
        part.FeedData(stream);
    }

    private static void WriteRootAttributes(XmlWriter writer, Worksheet sheet)
    {
        var declared = new HashSet<string> { string.Empty };
        foreach (var (prefix, uri) in sheet.RootNamespaces)
        {
            if (prefix.Length > 0 && declared.Add(prefix))
            {
                writer.WriteAttributeString("xmlns", prefix, null, uri);
            }
        }

        if (declared.Add("r"))
        {
            writer.WriteAttributeString("xmlns", "r", null, XlsxNamespaces.Relationships);
        }

        foreach (var (prefix, localName, ns, value) in sheet.RootAttributes)
        {
            if (prefix.Length > 0 && declared.Contains(prefix))
            {
                writer.WriteAttributeString(prefix, localName, ns, value);
            }
        }
    }

    /// <summary>Seitenlayout: the four elements from the model; attributes of pageSetup / headerFooter it does not own stay as read.</summary>
    private static void WritePageSetup(XmlWriter writer, Worksheet sheet, string name)
    {
        var setup = sheet.PageSetup;
        string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);
        switch (name)
        {
            case "printOptions" when setup.PrintGridLines || setup.PrintHeadings || setup.CenterHorizontally || setup.CenterVertically:
                writer.WriteStartElement("printOptions", XlsxNamespaces.Main);
                XlsxFeatures.WriteFlag(writer, "horizontalCentered", setup.CenterHorizontally);
                XlsxFeatures.WriteFlag(writer, "verticalCentered", setup.CenterVertically);
                XlsxFeatures.WriteFlag(writer, "headings", setup.PrintHeadings);
                XlsxFeatures.WriteFlag(writer, "gridLines", setup.PrintGridLines);
                writer.WriteEndElement();
                break;
            case "pageMargins":
                writer.WriteStartElement("pageMargins", XlsxNamespaces.Main);
                writer.WriteAttributeString("left", Number(setup.LeftMargin));
                writer.WriteAttributeString("right", Number(setup.RightMargin));
                writer.WriteAttributeString("top", Number(setup.TopMargin));
                writer.WriteAttributeString("bottom", Number(setup.BottomMargin));
                writer.WriteAttributeString("header", Number(setup.HeaderMargin));
                writer.WriteAttributeString("footer", Number(setup.FooterMargin));
                writer.WriteEndElement();
                break;
            case "pageSetup" when sheet.PageSetupXml is not null || setup with { LeftMargin = 0.7, RightMargin = 0.7, TopMargin = 0.75, BottomMargin = 0.75, HeaderMargin = 0.3, FooterMargin = 0.3, PrintGridLines = false, PrintHeadings = false, CenterHorizontally = false, CenterVertically = false, Header = null, Footer = null } != PageSetup.Default:
                var element = sheet.PageSetupXml is { } xml ? ParsePageSetup(xml) : new X.PageSetup();
                element.PaperSize = (uint)setup.PaperSize;
                element.Orientation = setup.Landscape ? X.OrientationValues.Landscape : X.OrientationValues.Portrait;
                element.Scale = setup.Scale == 100 ? null : (uint)Math.Clamp(setup.Scale, 10, 400);
                element.FitToWidth = setup.FitToWidth == 1 ? null : (uint)Math.Max(0, setup.FitToWidth);
                element.FitToHeight = setup.FitToHeight == 1 ? null : (uint)Math.Max(0, setup.FitToHeight);
                element.PageOrder = setup.OverThenDown ? X.PageOrderValues.OverThenDown : null;
                element.UseFirstPageNumber = setup.FirstPageNumber is null ? null : true;
                element.FirstPageNumber = setup.FirstPageNumber is { } first ? (uint)first : null;
                writer.WriteRaw(element.OuterXml);
                break;
            case "headerFooter" when sheet.HeaderFooterXml is not null || setup.Header is not null || setup.Footer is not null:
                var headerFooter = sheet.HeaderFooterXml is { } original ? new X.HeaderFooter(original) : new X.HeaderFooter();
                headerFooter.OddHeader = setup.Header is { Length: > 0 } header ? new X.OddHeader(header) : null;
                headerFooter.OddFooter = setup.Footer is { Length: > 0 } footer ? new X.OddFooter(footer) : null;
                if (headerFooter.HasChildren || headerFooter.HasAttributes)
                {
                    writer.WriteRaw(headerFooter.OuterXml);
                }

                break;
        }
    }

    private static void WriteSheetProperties(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.SheetPropertiesXml is { } xml)
        {
            var properties = new X.SheetProperties(xml);
            properties.TabColor = sheet.TabColor is { } color ? XlsxStyles.WriteColor<X.TabColor>(color) : null;
            ApplyFitToPage(properties, sheet);
            writer.WriteRaw(properties.OuterXml);
            return;
        }

        if (sheet.TabColor is not null || sheet.PageSetup.FitToPage)
        {
            var created = new X.SheetProperties { TabColor = sheet.TabColor is { } tab ? XlsxStyles.WriteColor<X.TabColor>(tab) : null };
            ApplyFitToPage(created, sheet);
            writer.WriteRaw(created.OuterXml);
        }
    }

    private static X.PageSetup ParsePageSetup(string xml)
    {
        var holder = new X.Worksheet { InnerXml = xml };
        return holder.GetFirstChild<X.PageSetup>() is { } parsed ? (X.PageSetup)parsed.CloneNode(true) : new X.PageSetup();
    }

    /// <summary>sheetPr/pageSetUpPr fitToPage follows the model (Seitenlayout › An Format anpassen).</summary>
    private static void ApplyFitToPage(X.SheetProperties properties, Worksheet sheet)
    {
        if (sheet.PageSetup.FitToPage)
        {
            properties.PageSetupProperties ??= new X.PageSetupProperties();
            properties.PageSetupProperties.FitToPage = true;
        }
        else if (properties.PageSetupProperties is { } existing)
        {
            existing.FitToPage = null;
        }
    }

    private static void WriteView(XmlWriter writer, Worksheet sheet)
    {
        var view = sheet.View;
        var active = sheet.Workbook.ActiveSheet == sheet;
        writer.WriteStartElement("sheetViews", XlsxNamespaces.Main);
        writer.WriteStartElement("sheetView", XlsxNamespaces.Main);
        XlsxFeatures.WriteFlag(writer, "tabSelected", active);
        if (!view.ShowGridLines)
        {
            writer.WriteAttributeString("showGridLines", "0");
        }

        if (!view.ShowHeaders)
        {
            writer.WriteAttributeString("showRowColHeaders", "0");
        }

        if (!view.ShowZeros)
        {
            writer.WriteAttributeString("showZeros", "0");
        }

        XlsxFeatures.WriteFlag(writer, "showFormulas", view.ShowFormulas);
        XlsxFeatures.WriteFlag(writer, "rightToLeft", view.RightToLeft);
        if (view.Kind != SheetViewKind.Normal)
        {
            writer.WriteAttributeString("view", view.Kind == SheetViewKind.PageLayout ? "pageLayout" : "pageBreakPreview");
        }

        if (!view.IsFrozen && view.TopLeft != default)
        {
            writer.WriteAttributeString("topLeftCell", view.TopLeft.ToString());
        }

        if (view.Zoom != 100)
        {
            writer.WriteAttributeString("zoomScale", view.Zoom.ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("zoomScaleNormal", view.Zoom.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteAttributeString("workbookViewId", "0");
        string? pane = null;
        if (view.IsFrozen)
        {
            pane = view.FrozenRows > 0 && view.FrozenColumns > 0 ? "bottomRight" : view.FrozenRows > 0 ? "bottomLeft" : "topRight";
            var topLeft = new CellAddress(Math.Max(view.TopLeft.Row, view.FrozenRows), Math.Max(view.TopLeft.Column, view.FrozenColumns));
            writer.WriteStartElement("pane", XlsxNamespaces.Main);
            if (view.FrozenColumns > 0)
            {
                writer.WriteAttributeString("xSplit", view.FrozenColumns.ToString(CultureInfo.InvariantCulture));
            }

            if (view.FrozenRows > 0)
            {
                writer.WriteAttributeString("ySplit", view.FrozenRows.ToString(CultureInfo.InvariantCulture));
            }

            writer.WriteAttributeString("topLeftCell", topLeft.ToString());
            writer.WriteAttributeString("activePane", pane);
            writer.WriteAttributeString("state", "frozen");
            writer.WriteEndElement();
        }
        else if (view.SplitX > 0 || view.SplitY > 0)
        {
            pane = view.SplitX > 0 && view.SplitY > 0 ? "bottomRight" : view.SplitY > 0 ? "bottomLeft" : "topRight";
            writer.WriteStartElement("pane", XlsxNamespaces.Main);
            if (view.SplitX > 0)
            {
                writer.WriteAttributeString("xSplit", view.SplitX.ToString("R", CultureInfo.InvariantCulture));
            }

            if (view.SplitY > 0)
            {
                writer.WriteAttributeString("ySplit", view.SplitY.ToString("R", CultureInfo.InvariantCulture));
            }

            writer.WriteAttributeString("activePane", pane);
            writer.WriteEndElement();
        }

        writer.WriteStartElement("selection", XlsxNamespaces.Main);
        if (pane is not null)
        {
            writer.WriteAttributeString("pane", pane);
        }

        writer.WriteAttributeString("activeCell", view.ActiveCell.ToString());
        writer.WriteAttributeString("sqref", CellRange.FormatList(view.Selection));
        writer.WriteEndElement();
        writer.WriteEndElement();
        writer.WriteEndElement();
    }

    private static void WriteFormat(XmlWriter writer, Worksheet sheet)
    {
        writer.WriteStartElement("sheetFormatPr", XlsxNamespaces.Main);
        if (Math.Abs(sheet.Columns.DefaultSize - 8.43) > 1e-9)
        {
            writer.WriteAttributeString("defaultColWidth", sheet.Columns.DefaultSize.ToString("R", CultureInfo.InvariantCulture));
        }

        writer.WriteAttributeString("defaultRowHeight", sheet.Rows.DefaultSize.ToString("R", CultureInfo.InvariantCulture));
        XlsxFeatures.WriteFlag(writer, "customHeight", sheet.CustomDefaultRowHeight);
        XlsxFeatures.WriteFlag(writer, "zeroHeight", sheet.ZeroHeightRows);
        var rowOutline = sheet.Rows.Entries.Select(entry => entry.Info.OutlineLevel).DefaultIfEmpty(0).Max();
        var columnOutline = sheet.Columns.Entries.Select(entry => entry.Info.OutlineLevel).DefaultIfEmpty(0).Max();
        if (rowOutline > 0)
        {
            writer.WriteAttributeString("outlineLevelRow", rowOutline.ToString(CultureInfo.InvariantCulture));
        }

        if (columnOutline > 0)
        {
            writer.WriteAttributeString("outlineLevelCol", columnOutline.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteEndElement();
    }

    private static void WriteColumns(XmlWriter writer, Worksheet sheet)
    {
        var entries = sheet.Columns.Entries.ToList();
        if (entries.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("cols", XlsxNamespaces.Main);
        var index = 0;
        while (index < entries.Count)
        {
            var (first, info) = entries[index];
            var last = first;
            while (index + 1 < entries.Count && entries[index + 1].Index == last + 1 && entries[index + 1].Info == info)
            {
                index++;
                last++;
            }

            writer.WriteStartElement("col", XlsxNamespaces.Main);
            writer.WriteAttributeString("min", (first + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("max", (last + 1).ToString(CultureInfo.InvariantCulture));
            writer.WriteAttributeString("width", (info.Size ?? sheet.Columns.DefaultSize).ToString("R", CultureInfo.InvariantCulture));
            if (info.StyleId is { } style)
            {
                writer.WriteAttributeString("style", style.ToString(CultureInfo.InvariantCulture));
            }

            XlsxFeatures.WriteFlag(writer, "hidden", info.Hidden);
            XlsxFeatures.WriteFlag(writer, "bestFit", info.BestFit);
            XlsxFeatures.WriteFlag(writer, "customWidth", info.CustomSize || info.Size is not null);
            if (info.OutlineLevel > 0)
            {
                writer.WriteAttributeString("outlineLevel", info.OutlineLevel.ToString(CultureInfo.InvariantCulture));
            }

            XlsxFeatures.WriteFlag(writer, "collapsed", info.Collapsed);
            writer.WriteEndElement();
            index++;
        }

        writer.WriteEndElement();
    }

    /// <summary>Runs of ≥ 2 vertically adjacent cells holding the same formula instance become shared formulas.</summary>
    private static Dictionary<CellAddress, (int Index, CellRange? Range)> SharedFormulaGroups(Worksheet sheet, WriteContext context)
    {
        var result = new Dictionary<CellAddress, (int, CellRange?)>();
        var byColumn = new Dictionary<int, List<(int Row, FormulaCell Cell)>>();
        foreach (var (row, column, record) in sheet.Store.Enumerate(CellRange.Whole))
        {
            if (record.Formula is { ArrayRange: null, SpillRange: null } formula && !IsDynamic(formula, context))
            {
                if (!byColumn.TryGetValue(column, out var list))
                {
                    list = [];
                    byColumn[column] = list;
                }

                list.Add((row, formula));
            }
        }

        var next = 0;
        foreach (var (column, cells) in byColumn)
        {
            var start = 0;
            while (start < cells.Count)
            {
                var end = start;
                while (end + 1 < cells.Count && cells[end + 1].Row == cells[end].Row + 1 && ReferenceEquals(cells[end + 1].Cell.Formula, cells[start].Cell.Formula))
                {
                    end++;
                }

                if (end > start && cells[start].Cell.Formula.Root is not ReferenceNode)
                {
                    var range = new CellRange(cells[start].Row, column, cells[end].Row, column);
                    var index = next++;
                    result[new CellAddress(cells[start].Row, column)] = (index, range);
                    for (var member = start + 1; member <= end; member++)
                    {
                        result[new CellAddress(cells[member].Row, column)] = (index, null);
                    }
                }

                start = end + 1;
            }
        }

        return result;
    }

    private static void WriteSheetData(XmlWriter writer, Worksheet sheet, WriteContext context, Dictionary<CellAddress, (int Index, CellRange? Range)> shared)
    {
        writer.WriteStartElement("sheetData", XlsxNamespaces.Main);
        var store = sheet.Store;
        var rowInfos = sheet.Rows.Entries.GetEnumerator();
        var hasInfo = rowInfos.MoveNext();
        var rowIndex = 0;
        while (rowIndex < store.RowCount || hasInfo)
        {
            var storedRow = rowIndex < store.RowCount ? store.RowKeyAt(rowIndex) : int.MaxValue;
            var infoRow = hasInfo ? rowInfos.Current.Index : int.MaxValue;
            var row = Math.Min(storedRow, infoRow);
            var info = infoRow == row ? rowInfos.Current.Info : default;
            writer.WriteStartElement("row", XlsxNamespaces.Main);
            writer.WriteAttributeString("r", (row + 1).ToString(CultureInfo.InvariantCulture));
            if (info.StyleId is { } style)
            {
                writer.WriteAttributeString("s", style.ToString(CultureInfo.InvariantCulture));
                writer.WriteAttributeString("customFormat", "1");
            }

            if (info.Size is { } height)
            {
                writer.WriteAttributeString("ht", height.ToString("R", CultureInfo.InvariantCulture));
            }

            XlsxFeatures.WriteFlag(writer, "hidden", info.Hidden);
            XlsxFeatures.WriteFlag(writer, "customHeight", info.CustomSize || (info.Size is not null && info.CustomSize));
            if (info.OutlineLevel > 0)
            {
                writer.WriteAttributeString("outlineLevel", info.OutlineLevel.ToString(CultureInfo.InvariantCulture));
            }

            XlsxFeatures.WriteFlag(writer, "collapsed", info.Collapsed);
            if (storedRow == row)
            {
                var data = store.RowAt(rowIndex);
                for (var position = 0; position < data.Count; position++)
                {
                    WriteCell(writer, sheet, row, data.ColumnAt(position), data.CellAt(position), context, shared);
                }

                rowIndex++;
            }

            if (infoRow == row)
            {
                hasInfo = rowInfos.MoveNext();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteCell(XmlWriter writer, Worksheet sheet, int row, int column, in CellRecord record, WriteContext context,
        Dictionary<CellAddress, (int Index, CellRange? Range)> shared)
    {
        var address = new CellAddress(row, column);
        var formula = record.Formula;
        var value = record.Value;
        writer.WriteStartElement("c", XlsxNamespaces.Main);
        writer.WriteAttributeString("r", address.ToString());
        if (record.StyleId != 0)
        {
            writer.WriteAttributeString("s", record.StyleId.ToString(CultureInfo.InvariantCulture));
        }

        var dynamic = formula is not null && IsDynamic(formula, context);
        switch (value.Kind)
        {
            case CellValueKind.Text when formula is null:
                writer.WriteAttributeString("t", "s");
                break;
            case CellValueKind.Text:
                writer.WriteAttributeString("t", "str");
                break;
            case CellValueKind.Boolean:
                writer.WriteAttributeString("t", "b");
                break;
            case CellValueKind.Error:
                writer.WriteAttributeString("t", "e");
                break;
        }

        if (dynamic)
        {
            context.UsesDynamicArrays = true;
            writer.WriteAttributeString("cm", (context.DynamicArrayIndex ?? 1).ToString(CultureInfo.InvariantCulture));
        }

        if (formula is not null)
        {
            WriteFormula(writer, sheet, address, formula, dynamic, shared);
        }

        switch (value.Kind)
        {
            case CellValueKind.Number:
                writer.WriteElementString("v", XlsxNamespaces.Main, value.Number.ToString("R", CultureInfo.InvariantCulture));
                break;
            case CellValueKind.Text when formula is null:
                writer.WriteElementString("v", XlsxNamespaces.Main, context.StringIndex(value.Text).ToString(CultureInfo.InvariantCulture));
                break;
            case CellValueKind.Text:
                writer.WriteElementString("v", XlsxNamespaces.Main, XlsxWriter.Escape(value.Text));
                break;
            case CellValueKind.Boolean:
                writer.WriteElementString("v", XlsxNamespaces.Main, value.Boolean ? "1" : "0");
                break;
            case CellValueKind.Error:
                writer.WriteElementString("v", XlsxNamespaces.Main, CellErrors.NameOf(value.Error));
                break;
        }

        writer.WriteEndElement();
    }

    /// <summary>A dynamic-array formula that needs array semantics is written as an array formula with dynamic-array metadata.</summary>
    private static bool IsDynamic(FormulaCell formula, WriteContext context) =>
        formula.IsDynamicArray && (formula.SpillRange is not null || formula.UsedArrayEvaluation || formula.Formula.UsesArrayFunctions)
        && context.DynamicArrayIndex != 0;

    private static void WriteFormula(XmlWriter writer, Worksheet sheet, CellAddress address, FormulaCell formula, bool dynamic,
        Dictionary<CellAddress, (int Index, CellRange? Range)> shared)
    {
        writer.WriteStartElement("f", XlsxNamespaces.Main);
        string? text = null;
        if (formula.ArrayRange is { } array)
        {
            writer.WriteAttributeString("t", "array");
            writer.WriteAttributeString("ref", array.ToString());
        }
        else if (dynamic)
        {
            writer.WriteAttributeString("t", "array");
            writer.WriteAttributeString("ref", (formula.SpillRange ?? CellRange.Cell(address)).ToString());
        }
        else if (shared.TryGetValue(address, out var group))
        {
            writer.WriteAttributeString("t", "shared");
            if (group.Range is { } range)
            {
                writer.WriteAttributeString("ref", range.ToString());
            }

            writer.WriteAttributeString("si", group.Index.ToString(CultureInfo.InvariantCulture));
            if (group.Range is null)
            {
                writer.WriteEndElement();
                return;
            }
        }

        text ??= FormulaFormatter.Format(formula.Formula, sheet.Workbook, address, FormulaSyntax.FileFormat);
        writer.WriteString(XlsxWriter.Escape(text));
        writer.WriteEndElement();
    }

    private static Dictionary<Hyperlink, string> WriteHyperlinkRelationships(Worksheet sheet, WorksheetPart part)
    {
        foreach (var relationship in part.HyperlinkRelationships.ToList())
        {
            part.DeleteReferenceRelationship(relationship);
        }

        var ids = new Dictionary<Hyperlink, string>(ReferenceEqualityComparer.Instance);
        foreach (var hyperlink in sheet.Hyperlinks)
        {
            if (hyperlink.Target is { Length: > 0 } target && Uri.TryCreate(target, UriKind.RelativeOrAbsolute, out var uri))
            {
                ids[hyperlink] = part.AddHyperlinkRelationship(uri, true).Id;
            }
        }

        return ids;
    }

    private static void WriteHyperlinks(XmlWriter writer, Worksheet sheet, Dictionary<Hyperlink, string> ids)
    {
        writer.WriteStartElement("hyperlinks", XlsxNamespaces.Main);
        foreach (var hyperlink in sheet.Hyperlinks)
        {
            writer.WriteStartElement("hyperlink", XlsxNamespaces.Main);
            writer.WriteAttributeString("ref", hyperlink.Range.ToString());
            if (ids.TryGetValue(hyperlink, out var id))
            {
                writer.WriteAttributeString("r", "id", XlsxNamespaces.Relationships, id);
            }

            XlsxFeatures.WriteOptional(writer, "location", hyperlink.Location);
            XlsxFeatures.WriteOptional(writer, "tooltip", hyperlink.Tooltip);
            XlsxFeatures.WriteOptional(writer, "display", hyperlink.Display);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    private static void WriteTableParts(XmlWriter writer, WorksheetPart part)
    {
        var tables = part.TableDefinitionParts.ToList();
        if (tables.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("tableParts", XlsxNamespaces.Main);
        writer.WriteAttributeString("count", tables.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var table in tables)
        {
            writer.WriteStartElement("tablePart", XlsxNamespaces.Main);
            writer.WriteAttributeString("r", "id", XlsxNamespaces.Relationships, part.GetIdOfPart(table));
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    /// <summary>Tables whose range moved (rows inserted/deleted) get their new ref.</summary>
    private static void UpdateTables(Worksheet sheet, WorksheetPart part)
    {
        foreach (var tablePart in part.TableDefinitionParts)
        {
            var model = sheet.Tables.FirstOrDefault(table => table.PartUri == tablePart.Uri.ToString());
            var table = tablePart.Table;
            if (model is null || table is null)
            {
                continue;
            }

            var reference = model.Range.ToString();
            if (table.Reference?.Value == reference)
            {
                continue;
            }

            table.Reference = reference;
            if (table.AutoFilter is { } filter)
            {
                filter.Reference = new CellRange(model.Range.FirstRow, model.Range.FirstColumn, model.Range.LastRow - model.TotalsRowCount, model.Range.LastColumn).ToString();
            }

            table.Save();
        }
    }

    /// <summary>Writes the notes part (and VML shapes when the noted cells changed); returns the legacyDrawing relationship id.</summary>
    private static string? WriteComments(Worksheet sheet, WorksheetPart part)
    {
        if (sheet.Comments.Count == 0)
        {
            if (part.WorksheetCommentsPart is { } commentsPart)
            {
                part.DeletePart(commentsPart);
                if (sheet.LegacyDrawingId is { } id && part.GetPartById(id) is VmlDrawingPart vml)
                {
                    part.DeletePart(vml);
                }

                sheet.LegacyDrawingId = null;
            }

            return sheet.LegacyDrawingId;
        }

        var authors = sheet.Comments.Values.Select(comment => comment.Author).Distinct().ToList();
        var comments = new X.Comments(
            new X.Authors(authors.Select(author => new X.Author(author))),
            new X.CommentList(sheet.Comments.OrderBy(entry => entry.Key).Select(entry => new X.Comment(
                new X.CommentText(new X.Run(new X.Text(entry.Value.Text) { Space = DocumentFormat.OpenXml.SpaceProcessingModeValues.Preserve })))
            {
                Reference = entry.Key.ToString(),
                AuthorId = (uint)authors.IndexOf(entry.Value.Author),
            })));
        var target = part.WorksheetCommentsPart ?? part.AddNewPart<WorksheetCommentsPart>();
        target.Comments = comments;
        target.Comments.Save();

        var unchanged = sheet.LegacyDrawingId is not null && sheet.OriginalCommentAddresses.SetEquals(sheet.Comments.Keys);
        if (unchanged)
        {
            return sheet.LegacyDrawingId;
        }

        if (sheet.LegacyDrawingId is { } oldId && part.GetPartById(oldId) is VmlDrawingPart old)
        {
            part.DeletePart(old);
        }

        var drawing = part.AddNewPart<VmlDrawingPart>();
        using (var stream = new MemoryStream(Encoding.UTF8.GetBytes(NotesVml(sheet))))
        {
            drawing.FeedData(stream);
        }

        sheet.LegacyDrawingId = part.GetIdOfPart(drawing);
        sheet.OriginalCommentAddresses = sheet.Comments.Keys.ToHashSet();
        return sheet.LegacyDrawingId;
    }

    /// <summary>The VML shapes Excel needs to show notes (one hidden yellow box per note).</summary>
    private static string NotesVml(Worksheet sheet)
    {
        var builder = new StringBuilder();
        builder.Append("<xml xmlns:v=\"urn:schemas-microsoft-com:vml\" xmlns:o=\"urn:schemas-microsoft-com:office:office\" xmlns:x=\"urn:schemas-microsoft-com:office:excel\">");
        builder.Append("<o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"1\"/></o:shapelayout>");
        builder.Append("<v:shapetype id=\"_x0000_t202\" coordsize=\"21600,21600\" o:spt=\"202\" path=\"m,l,21600r21600,l21600,xe\">");
        builder.Append("<v:stroke joinstyle=\"miter\"/><v:path gradientshapeok=\"t\" o:connecttype=\"rect\"/></v:shapetype>");
        var shape = 1025;
        foreach (var (address, comment) in sheet.Comments.OrderBy(entry => entry.Key))
        {
            var visibility = comment.Visible ? "visible" : "hidden";
            builder.Append(CultureInfo.InvariantCulture, $"<v:shape id=\"_x0000_s{shape++}\" type=\"#_x0000_t202\" style=\"position:absolute;margin-left:59.25pt;margin-top:1.5pt;width:108pt;height:59.25pt;z-index:{shape - 1025};visibility:{visibility}\" fillcolor=\"#ffffe1\" o:insetmode=\"auto\">");
            builder.Append("<v:fill color2=\"#ffffe1\"/><v:shadow on=\"t\" color=\"black\" obscured=\"t\"/><v:path o:connecttype=\"none\"/>");
            builder.Append("<v:textbox style=\"mso-direction-alt:auto\"><div style=\"text-align:left\"></div></v:textbox>");
            builder.Append("<x:ClientData ObjectType=\"Note\"><x:MoveWithCells/><x:SizeWithCells/>");
            builder.Append(CultureInfo.InvariantCulture, $"<x:Anchor>{address.Column + 1}, 15, {Math.Max(0, address.Row - 1)}, 10, {address.Column + 3}, 15, {address.Row + 3}, 4</x:Anchor>");
            builder.Append("<x:AutoFill>False</x:AutoFill>");
            builder.Append(CultureInfo.InvariantCulture, $"<x:Row>{address.Row}</x:Row><x:Column>{address.Column}</x:Column>");
            if (comment.Visible)
            {
                builder.Append("<x:Visible/>");
            }

            builder.Append("</x:ClientData></v:shape>");
        }

        builder.Append("</xml>");
        return builder.ToString();
    }
}
