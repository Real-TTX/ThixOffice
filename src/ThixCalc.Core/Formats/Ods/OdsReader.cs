using System.Globalization;
using System.Xml.Linq;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Core.Formats.Odf;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixCalc.Core.Formats.Ods;

/// <summary>
/// Reads OpenDocument Spreadsheet (.ods – LibreOffice, OpenOffice, Google Sheets, Excel's export) into a workbook: sheets (order,
/// hidden, tab colour, protection), cells (numbers, percentages, currencies, dates, times, booleans, text, errors), OpenFormula
/// formulas with their cached results (no recalculation unless a result is missing), matrix formulas, merges, notes, links, cell
/// styles with their data styles, column widths, row heights, hidden and grouped rows/columns, validations, defined names, print
/// ranges and title rows, the autofilter, conditional formats of the kinds ThixCalc knows, page layouts with header and footer,
/// frozen panes, zoom and the cursor (settings.xml). Runs of repeated rows and cells are not materialized when empty. What the file
/// has that ThixCalc does not take over (charts, pictures, pivot tables, macros …) is named in
/// <see cref="Workbook.UnsupportedContent"/>; it is not written back.
/// </summary>
public static class OdsReader
{
    public const string NotAnOdsMessage = "Die Datei ist keine gültige OpenDocument-Tabelle (.ods).";

    /// <summary>Repeated rows with content beyond this are not materialized (a file could claim a million copies).</summary>
    private const int MaxMaterializedRows = 65536;

    /// <summary>Style-only cells repeated more often than this are not stored (whole formatted rows come as row styles).</summary>
    private const int MaxStyledRepeat = 256;

    public static bool IsOds(Stream stream) => OdfPackage.HasMimeType(stream, OdsWriter.MimeType, "application/vnd.oasis.opendocument.spreadsheet-template");

    public static Workbook Read(Stream stream) => OdfPackage.Read(stream, NotAnOdsMessage, package => new Reader(package).Read());

    private sealed class Reader
    {
        private readonly OdfPackage _package;
        private readonly XDocument _content;
        private readonly XDocument? _styles;
        private readonly Dictionary<string, XElement> _named = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _automatic = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _dataStyles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _pageLayouts = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _masterPages = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _styleIds = new(StringComparer.Ordinal);
        private readonly Dictionary<string, List<(Worksheet Sheet, CellRange Range)>> _validationRanges = new(StringComparer.Ordinal);
        private readonly List<string> _notes = [];
        private Workbook _workbook = null!;
        private CellStyle _defaultStyle = CellStyle.Default;
        private bool _missingResults;

        public Reader(OdfPackage package)
        {
            _package = package;
            _content = package.Xml("content.xml")!;
            _styles = package.Xml("styles.xml");
            foreach (var root in new[] { _styles?.Root, _content.Root })
            {
                if (root is null)
                {
                    continue;
                }

                foreach (var container in new[] { root.Element(OfficeNs + "styles"), root.Element(OfficeNs + "automatic-styles") })
                {
                    foreach (var element in container?.Elements() ?? [])
                    {
                        if ((string?)element.Attribute(StyleNs + "name") is not { } name)
                        {
                            continue;
                        }

                        if (element.Name.Namespace == NumberNs)
                        {
                            _dataStyles[name] = element;
                        }
                        else if (element.Name == StyleNs + "style")
                        {
                            (container!.Name.LocalName == "styles" ? _named : _automatic)[name] = element;
                        }
                        else if (element.Name == StyleNs + "page-layout")
                        {
                            _pageLayouts[name] = element;
                        }
                    }
                }

                foreach (var master in root.Element(OfficeNs + "master-styles")?.Elements(StyleNs + "master-page") ?? [])
                {
                    _masterPages[(string?)master.Attribute(StyleNs + "name") ?? ""] = master;
                }
            }
        }

        public Workbook Read()
        {
            _defaultStyle = DefaultStyle();
            _workbook = new Workbook(_defaultStyle);
            var spreadsheet = _content.Root?.Element(OfficeNs + "body")?.Element(OfficeNs + "spreadsheet")
                ?? throw new InvalidDataException(OdsReader.NotAnOdsMessage);
            ReadCalculationSettings(spreadsheet.Element(TableNs + "calculation-settings"));

            var tables = spreadsheet.Elements(TableNs + "table").ToList();
            var sheets = new List<(Worksheet Sheet, XElement Table)>();
            foreach (var table in tables)
            {
                var name = UniqueName(((string?)table.Attribute(TableNs + "name"))?.Trim() is { Length: > 0 } given ? given : _workbook.NextSheetName());
                sheets.Add((_workbook.AddSheet(name), table));
            }

            if (sheets.Count == 0)
            {
                _workbook.AddSheet();
            }

            // Names before cells: formulas may use them.
            ReadNames(spreadsheet.Element(TableNs + "named-expressions"), null);
            foreach (var (sheet, table) in sheets)
            {
                ReadNames(table.Element(TableNs + "named-expressions"), sheet);
            }

            foreach (var (sheet, table) in sheets)
            {
                ReadSheet(sheet, table);
            }

            ReadValidations(spreadsheet.Element(TableNs + "content-validations"));
            ReadDatabaseRanges(spreadsheet.Element(TableNs + "database-ranges"));
            ReadSettings();
            DetectUnsupported(spreadsheet);

            _workbook.UnsupportedContent = _notes;
            _workbook.Calculation.Rebuild(markDirty: _missingResults);
            if (_missingResults)
            {
                _workbook.Calculation.Recalculate();
            }

            _workbook.ForgetChanges();
            return _workbook;
        }

        private void Note(string text)
        {
            if (!_notes.Contains(text))
            {
                _notes.Add(text);
            }
        }

        private string UniqueName(string name)
        {
            name = new string(name.Select(character => character is ':' or '\\' or '/' or '?' or '*' or '[' or ']' ? '_' : character).ToArray()).Trim('\'');
            if (name.Length > 31)
            {
                name = name[..31];
            }

            if (name.Length == 0 || name.Equals("Verlauf", StringComparison.OrdinalIgnoreCase) || name.Equals("History", StringComparison.OrdinalIgnoreCase))
            {
                name = _workbook.NextSheetName();
            }

            var candidate = name;
            for (var number = 2; _workbook.FindSheet(candidate) is not null; number++)
            {
                var suffix = $" ({number})";
                candidate = (name.Length + suffix.Length > 31 ? name[..(31 - suffix.Length)] : name) + suffix;
            }

            return candidate;
        }

        // ------------------------------------------------------------ styles

        private CellStyle DefaultStyle()
        {
            var style = CellStyle.Default;
            var defaults = _styles?.Root?.Element(OfficeNs + "styles")?.Elements(StyleNs + "default-style")
                .FirstOrDefault(element => (string?)element.Attribute(StyleNs + "family") == "table-cell");
            if (defaults is not null)
            {
                style = OdsCellStyles.Read(style, defaults);
            }

            if (_named.TryGetValue("Default", out var named))
            {
                style = OdsCellStyles.Read(style, named);
                if (DataStyleCode(named) is { } code)
                {
                    style = style with { NumberFormat = code };
                }
            }

            return style;
        }

        private string? DataStyleCode(XElement style) =>
            (string?)style.Attribute(StyleNs + "data-style-name") is { } name && _dataStyles.TryGetValue(name, out var data) ? OdsNumberFormats.Read(data, _dataStyles) : null;

        /// <summary>The style id of a cell style name (0 for none or "Default").</summary>
        private int StyleId(string? name)
        {
            if (string.IsNullOrEmpty(name) || name == "Default")
            {
                return 0;
            }

            if (_styleIds.TryGetValue(name, out var id))
            {
                return id;
            }

            var style = Resolve(name, 0);
            id = style == _defaultStyle ? 0 : _workbook.Styles.Intern(style);
            _styleIds[name] = id;
            return id;
        }

        private CellStyle Resolve(string name, int depth)
        {
            var element = _automatic.GetValueOrDefault(name) ?? _named.GetValueOrDefault(name);
            if (element is null || depth > 16)
            {
                return _defaultStyle;
            }

            if (element.Elements(StyleNs + "map").Any())
            {
                Note("Bedingte Formatierungen im älteren OpenDocument-Format (werden nicht übernommen)");
            }

            var parentName = (string?)element.Attribute(StyleNs + "parent-style-name");
            var style = parentName is null || parentName == "Default" ? _defaultStyle : Resolve(parentName, depth + 1);
            style = OdsCellStyles.Read(style, element);
            if (DataStyleCode(element) is { } code)
            {
                style = style with { NumberFormat = code };
            }

            return style;
        }

        private XElement? StyleProperties(string? name, string properties) =>
            name is null ? null : (_automatic.GetValueOrDefault(name) ?? _named.GetValueOrDefault(name))?.Element(StyleNs + properties);

        // ------------------------------------------------------------ a sheet

        private void ReadSheet(Worksheet sheet, XElement table)
        {
            var tableProperties = StyleProperties((string?)table.Attribute(TableNs + "style-name"), "table-properties");
            if ((string?)tableProperties?.Attribute(TableNs + "display") == "false")
            {
                sheet.Visibility = SheetVisibility.Hidden;
            }

            if (ColorOf((string?)tableProperties?.Attribute(TableOooNs + "tab-color")) is { } tab)
            {
                sheet.TabColor = CalcColor.FromHex(tab);
            }

            if ((string?)table.Attribute(TableNs + "protected") == "true")
            {
                sheet.ProtectionXml = "<sheetProtection xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" sheet=\"1\" objects=\"1\" scenarios=\"1\" />";
            }

            var tableStyle = (string?)table.Attribute(TableNs + "style-name") is { } styleName ? _automatic.GetValueOrDefault(styleName) ?? _named.GetValueOrDefault(styleName) : null;
            sheet.PageSetup = ReadPageSetup((string?)tableStyle?.Attribute(StyleNs + "master-page-name"));

            var titleColumns = ReadColumns(sheet, table);
            var titleRows = ReadRows(sheet, table);
            PrintNames(sheet, (string?)table.Attribute(TableNs + "print-ranges"), titleRows, titleColumns);
            ReadConditionalFormats(sheet, table.Element(CalcExtNs + "conditional-formats"));
            if (table.Element(TableNs + "shapes") is { } shapes)
            {
                NoteShapes(shapes);
            }
        }

        // ------------------------------------------------------------ columns

        private (int First, int Last)? ReadColumns(Worksheet sheet, XElement table)
        {
            var columns = new List<(int Index, int Count, double? Width, bool Optimal, bool Hidden, int? Style, int Level)>();
            (int First, int Last)? titles = null;
            var index = 0;
            Walk(table, 0, false);

            // The width most columns have (LibreOffice's filler columns) is the sheet's default.
            var widest = columns.Where(column => column.Width is not null).OrderByDescending(column => column.Count).FirstOrDefault();
            if (widest.Count >= 100 && widest.Width is { } common)
            {
                sheet.Columns.DefaultSize = OdsWriter.ColumnCharacters(common);
            }

            foreach (var column in columns)
            {
                var characters = column.Width is { } width ? OdsWriter.ColumnCharacters(width) : (double?)null;
                var size = characters is { } value && Math.Abs(value - sheet.Columns.DefaultSize) > 1e-6 ? value : (double?)null;
                var info = new AxisInfo(size, column.Hidden, column.Level, false, column.Style, CustomSize: size is not null && !column.Optimal, BestFit: column.Optimal && size is not null);
                if (info.IsDefault || column.Count > 4096)
                {
                    continue;
                }

                var last = Math.Min(column.Index + column.Count - 1, SheetLimits.LastColumn);
                sheet.Columns.Update(column.Index, last, _ => info);
            }

            return titles;

            void Walk(XElement parent, int level, bool header)
            {
                foreach (var child in parent.Elements())
                {
                    if (index >= SheetLimits.MaxColumns)
                    {
                        return;
                    }

                    switch (child.Name.LocalName)
                    {
                        case "table-column" when child.Name.Namespace == TableNs:
                            var count = Math.Clamp((int?)child.Attribute(TableNs + "number-columns-repeated") ?? 1, 1, SheetLimits.MaxColumns - index);
                            var properties = StyleProperties((string?)child.Attribute(TableNs + "style-name"), "table-column-properties");
                            var style = StyleId((string?)child.Attribute(TableNs + "default-cell-style-name"));
                            columns.Add((index, count, Points((string?)properties?.Attribute(StyleNs + "column-width")),
                                (string?)properties?.Attribute(StyleNs + "use-optimal-column-width") == "true",
                                (string?)child.Attribute(TableNs + "visibility") is "collapse" or "filter",
                                style == 0 ? null : style, Math.Min(level, 7)));
                            if (header)
                            {
                                titles = (titles?.First ?? index, index + count - 1);
                            }

                            index += count;
                            break;
                        case "table-column-group" when child.Name.Namespace == TableNs:
                            Walk(child, level + 1, header);
                            break;
                        case "table-header-columns" when child.Name.Namespace == TableNs:
                            Walk(child, level, true);
                            break;
                        case "table-columns" when child.Name.Namespace == TableNs:
                            Walk(child, level, header);
                            break;
                    }
                }
            }
        }

        // ------------------------------------------------------------ rows

        private (int First, int Last)? ReadRows(Worksheet sheet, XElement table)
        {
            var rows = new List<(XElement Row, int Level, bool Header)>();
            Collect(table, 0, false);

            // The height most optimal rows have is the sheet's default row height.
            var heights = new Dictionary<double, long>();
            foreach (var (row, _, _) in rows)
            {
                var properties = StyleProperties((string?)row.Attribute(TableNs + "style-name"), "table-row-properties");
                if ((string?)properties?.Attribute(StyleNs + "use-optimal-row-height") != "false" && Points((string?)properties?.Attribute(StyleNs + "row-height")) is { } height)
                {
                    heights[Math.Round(height, 6)] = heights.GetValueOrDefault(Math.Round(height, 6)) + Math.Max(1, (int?)row.Attribute(TableNs + "number-rows-repeated") ?? 1);
                }
            }

            if (heights.Count > 0)
            {
                sheet.Rows.DefaultSize = heights.MaxBy(pair => pair.Value).Key;
            }

            (int First, int Last)? titles = null;
            var index = 0;
            var context = new CellContext(sheet);
            foreach (var (row, level, header) in rows)
            {
                if (index >= SheetLimits.MaxRows)
                {
                    break;
                }

                var count = Math.Clamp((int?)row.Attribute(TableNs + "number-rows-repeated") ?? 1, 1, SheetLimits.MaxRows - index);
                if (header)
                {
                    titles = (titles?.First ?? index, index + count - 1);
                }

                var properties = StyleProperties((string?)row.Attribute(TableNs + "style-name"), "table-row-properties");
                var height = Points((string?)properties?.Attribute(StyleNs + "row-height"));
                var custom = (string?)properties?.Attribute(StyleNs + "use-optimal-row-height") == "false";
                var size = height is { } value && (custom || Math.Abs(value - sheet.Rows.DefaultSize) > 1e-6) ? Math.Round(value, 6) : (double?)null;
                var style = StyleId((string?)row.Attribute(TableNs + "default-cell-style-name"));
                var info = new AxisInfo(size, (string?)row.Attribute(TableNs + "visibility") is "collapse" or "filter", Math.Min(level, 7), false,
                    style == 0 ? null : style, CustomSize: custom && size is not null);
                if (!info.IsDefault && count <= 100_000)
                {
                    sheet.Rows.Update(index, index + count - 1, _ => info);
                }

                if (HasContent(row))
                {
                    var copies = Math.Min(count, MaxMaterializedRows);
                    if (copies < count)
                    {
                        Note("Sehr oft wiederholte Zeilen (nur die ersten 65.536 Wiederholungen übernommen)");
                    }

                    ReadCells(sheet, row, index, copies, context);
                }
                else if (count > 1)
                {
                    ReadAttributesOnly(sheet, row, index, count);
                }
                else
                {
                    ReadCells(sheet, row, index, 1, context);
                }

                index += count;
            }

            foreach (var (row, column, range) in context.Anchors)
            {
                sheet.MarkSpilled(range, row, column);
            }

            return titles;

            void Collect(XElement parent, int level, bool header)
            {
                foreach (var child in parent.Elements())
                {
                    if (child.Name.Namespace != TableNs)
                    {
                        continue;
                    }

                    switch (child.Name.LocalName)
                    {
                        case "table-row":
                            rows.Add((child, level, header));
                            break;
                        case "table-row-group":
                            Collect(child, level + 1, header);
                            break;
                        case "table-header-rows":
                            Collect(child, level, true);
                            break;
                        case "table-rows":
                            Collect(child, level, header);
                            break;
                    }
                }
            }
        }

        /// <summary>A row with a value, formula, note, merge or styled cells that are not repeated very often.</summary>
        private static bool HasContent(XElement row) => row.Elements().Any(cell =>
            cell.Attribute(OfficeNs + "value-type") is not null || cell.Attribute(TableNs + "formula") is not null || cell.HasElements
            || cell.Attribute(TableNs + "number-columns-spanned") is not null
            || (cell.Attribute(TableNs + "style-name") is not null && ((int?)cell.Attribute(TableNs + "number-columns-repeated") ?? 1) <= MaxStyledRepeat));

        /// <summary>A repeated row without content: only its cells' validations count.</summary>
        private void ReadAttributesOnly(Worksheet sheet, XElement row, int rowIndex, int count)
        {
            var column = 0;
            foreach (var cell in row.Elements())
            {
                var repeat = Math.Clamp((int?)cell.Attribute(TableNs + "number-columns-repeated") ?? 1, 1, Math.Max(1, SheetLimits.MaxColumns - column));
                if ((string?)cell.Attribute(TableNs + "content-validation-name") is { } validation && column < SheetLimits.MaxColumns)
                {
                    AddValidationRange(validation, sheet, new CellRange(rowIndex, column, Math.Min(rowIndex + count - 1, SheetLimits.LastRow), Math.Min(column + repeat - 1, SheetLimits.LastColumn)));
                }

                column += repeat;
            }
        }

        private void AddValidationRange(string name, Worksheet sheet, CellRange range)
        {
            if (!_validationRanges.TryGetValue(name, out var ranges))
            {
                ranges = [];
                _validationRanges[name] = ranges;
            }

            // Rows below each other with the same columns become one range.
            if (ranges.Count > 0 && ranges[^1].Sheet == sheet && ranges[^1].Range.FirstColumn == range.FirstColumn && ranges[^1].Range.LastColumn == range.LastColumn
                && ranges[^1].Range.LastRow + 1 == range.FirstRow)
            {
                ranges[^1] = (sheet, ranges[^1].Range.Union(range));
                return;
            }

            var match = ranges.FindIndex(entry => entry.Sheet == sheet && entry.Range.FirstColumn == range.FirstColumn && entry.Range.LastColumn == range.LastColumn
                && entry.Range.LastRow + 1 == range.FirstRow);
            if (match >= 0)
            {
                ranges[match] = (sheet, ranges[match].Range.Union(range));
                return;
            }

            ranges.Add((sheet, range));
        }

        private sealed class CellContext(Worksheet sheet)
        {
            public Worksheet Sheet { get; } = sheet;

            public List<(int Row, int Column, CellRange Range)> Anchors { get; } = [];
        }

        // ------------------------------------------------------------ cells

        private void ReadCells(Worksheet sheet, XElement row, int rowIndex, int copies, CellContext context)
        {
            var column = 0;
            foreach (var cell in row.Elements())
            {
                if (cell.Name.Namespace != TableNs || cell.Name.LocalName is not ("table-cell" or "covered-table-cell"))
                {
                    continue;
                }

                if (column >= SheetLimits.MaxColumns)
                {
                    break;
                }

                var repeat = Math.Clamp((int?)cell.Attribute(TableNs + "number-columns-repeated") ?? 1, 1, SheetLimits.MaxColumns - column);
                ReadCell(sheet, cell, rowIndex, column, repeat, copies, context);
                column += repeat;
            }
        }

        private void ReadCell(Worksheet sheet, XElement cell, int rowIndex, int column, int repeat, int copies, CellContext context)
        {
            if ((string?)cell.Attribute(TableNs + "content-validation-name") is { } validation)
            {
                AddValidationRange(validation, sheet, new CellRange(rowIndex, column, Math.Min(rowIndex + copies - 1, SheetLimits.LastRow), column + repeat - 1));
            }

            var covered = cell.Name.LocalName == "covered-table-cell";
            var spannedColumns = (int?)cell.Attribute(TableNs + "number-columns-spanned") ?? 1;
            var spannedRows = (int?)cell.Attribute(TableNs + "number-rows-spanned") ?? 1;
            if (!covered && (spannedColumns > 1 || spannedRows > 1))
            {
                for (var copy = 0; copy < copies; copy++)
                {
                    for (var offset = 0; offset < repeat; offset++)
                    {
                        var first = new CellAddress(rowIndex + copy, column + offset);
                        var last = new CellAddress(Math.Min(first.Row + Math.Max(1, spannedRows) - 1, SheetLimits.LastRow), Math.Min(first.Column + Math.Max(1, spannedColumns) - 1, SheetLimits.LastColumn));
                        sheet.Merges.Add(new CellRange(first, last));
                    }
                }
            }

            var styleId = StyleId((string?)cell.Attribute(TableNs + "style-name"));
            var value = ReadValue(cell, out var hasRichText);
            var formulaText = (string?)cell.Attribute(TableNs + "formula");
            var annotation = cell.Element(OfficeNs + "annotation");
            var link = cell.Elements(TextNs + "p").Descendants(TextNs + "a").FirstOrDefault();
            if (value.IsBlank && formulaText is null && annotation is null && (styleId == 0 || repeat * copies > MaxStyledRepeat * 4))
            {
                return;
            }

            if (hasRichText)
            {
                Note("Teilweise formatierter Text in Zellen (wird einheitlich angezeigt)");
            }

            Formula? formula = null;
            var origin = new CellAddress(rowIndex, column);
            if (formulaText is not null)
            {
                formula = ParseFormula(sheet, formulaText, origin);
            }

            var matrixColumns = (int?)cell.Attribute(TableNs + "number-matrix-columns-spanned") ?? 1;
            var matrixRows = (int?)cell.Attribute(TableNs + "number-matrix-rows-spanned") ?? 1;
            for (var copy = 0; copy < copies; copy++)
            {
                for (var offset = 0; offset < repeat; offset++)
                {
                    var row = rowIndex + copy;
                    var target = column + offset;
                    if (formula is not null)
                    {
                        CellRange? matrix = matrixColumns > 1 || matrixRows > 1
                            ? new CellRange(row, target, Math.Min(row + matrixRows - 1, SheetLimits.LastRow), Math.Min(target + matrixColumns - 1, SheetLimits.LastColumn))
                            : null;
                        var dynamic = matrix is not null && formula.UsesArrayFunctions;
                        if (value.IsBlank)
                        {
                            _missingResults = true;
                        }

                        sheet.LoadFormula(row, target, formula, value.IsBlank ? CellValue.Zero : value, styleId, dynamic, dynamic ? null : matrix, dynamic ? matrix : null);
                        if (matrix is { } covers)
                        {
                            context.Anchors.Add((row, target, covers));
                        }
                    }
                    else
                    {
                        sheet.LoadValue(row, target, value, styleId);
                    }

                    var address = new CellAddress(row, target);
                    if (annotation is not null)
                    {
                        sheet.Comments[address] = new CellComment(
                            annotation.Element(DcNs + "creator")?.Value ?? "",
                            string.Join('\n', annotation.Elements(TextNs + "p").Select(PlainText)),
                            (string?)annotation.Attribute(OfficeNs + "display") == "true");
                    }

                    if (link is not null && (string?)link.Attribute(XLinkNs + "href") is { Length: > 0 } href)
                    {
                        sheet.Hyperlinks.Add(LinkOf(CellRange.Cell(address), href));
                    }
                }
            }
        }

        private Hyperlink LinkOf(CellRange range, string href)
        {
            if (!href.StartsWith('#'))
            {
                var hash = href.IndexOf('#', StringComparison.Ordinal);
                return hash > 0 && !href.StartsWith("http", StringComparison.OrdinalIgnoreCase)
                    ? new Hyperlink(range, href[..hash], href[(hash + 1)..])
                    : new Hyperlink(range, href);
            }

            // #Tabelle2.A1 / #'Mein Blatt'.A1 → Tabelle2!A1
            var place = href[1..];
            if (OdsFormulas.ToCanonical("[" + (place.Contains('.', StringComparison.Ordinal) ? "$" + place : "." + place) + "]") is { } canonical
                && !canonical.Contains('[', StringComparison.Ordinal))
            {
                return new Hyperlink(range, null, canonical.Replace("$", "", StringComparison.Ordinal));
            }

            return new Hyperlink(range, null, place);
        }

        private Formula? ParseFormula(Worksheet sheet, string text, CellAddress origin)
        {
            if (OdsFormulas.ToCanonical(text) is not { } canonical)
            {
                Note("Formeln, die ThixCalc nicht übernehmen kann (ihr Ergebnis bleibt als Wert)");
                return null;
            }

            try
            {
                return FormulaParser.Parse(canonical, _workbook, sheet, origin);
            }
            catch (FormulaParseException)
            {
                Note("Formeln, die ThixCalc nicht übernehmen kann (ihr Ergebnis bleibt als Wert)");
                return null;
            }
        }

        private CellValue ReadValue(XElement cell, out bool hasRichText)
        {
            hasRichText = false;
            var type = (string?)cell.Attribute(OfficeNs + "value-type");
            var extended = (string?)cell.Attribute(CalcExtNs + "value-type");
            var paragraphs = cell.Elements(TextNs + "p").ToList();
            if (extended == "error")
            {
                var text = string.Join('\n', paragraphs.Select(PlainText)).Trim();
                return CellValue.FromError(CellErrors.Parse(text) ?? CellErrors.Parse(text.Replace("!", "?", StringComparison.Ordinal)) ?? CellError.Value);
            }

            switch (type)
            {
                case "float" or "percentage" or "currency":
                    return NumberOf((string?)cell.Attribute(OfficeNs + "value")) is { } number ? CellValue.FromNumber(number) : CellValue.Blank;
                case "date":
                    return DateOf((string?)cell.Attribute(OfficeNs + "date-value")) is { } date
                        ? CellValue.FromNumber(DateSerial.FromDateTime(date, _workbook.Date1904))
                        : CellValue.Blank;
                case "time":
                    return DurationDays((string?)cell.Attribute(OfficeNs + "time-value")) is { } days ? CellValue.FromNumber(days) : CellValue.Blank;
                case "boolean":
                    var flag = (string?)cell.Attribute(OfficeNs + "boolean-value");
                    return CellValue.FromBoolean(flag is "true" or "1");
                case "string":
                    if ((string?)cell.Attribute(OfficeNs + "string-value") is { } given)
                    {
                        return CellValue.FromText(given);
                    }

                    hasRichText = paragraphs.Descendants(TextNs + "span").Any(span => span.Attribute(TextNs + "style-name") is not null);
                    var text = string.Join('\n', paragraphs.Select(PlainText));
                    return cell.Attribute(TableNs + "formula") is not null && CellErrors.Parse(text) is { } error ? CellValue.FromError(error) : CellValue.FromText(text);
                default:
                    return CellValue.Blank;
            }
        }

        // ------------------------------------------------------------ names, print ranges, validations, filters

        private void ReadNames(XElement? names, Worksheet? scope)
        {
            foreach (var element in names?.Elements() ?? [])
            {
                var name = (string?)element.Attribute(TableNs + "name");
                if (string.IsNullOrWhiteSpace(name))
                {
                    continue;
                }

                var text = element.Name.LocalName switch
                {
                    "named-range" => (string?)element.Attribute(TableNs + "cell-range-address") is { } address ? "[" + address + "]" : null,
                    "named-expression" => (string?)element.Attribute(TableNs + "expression"),
                    _ => null,
                };
                if (OdsFormulas.ToCanonical(text) is not { } canonical)
                {
                    Note("Namen, die ThixCalc nicht übernehmen kann");
                    continue;
                }

                try
                {
                    canonical = FormulaFormatter.Format(FormulaParser.ParseNode(canonical, _workbook, default), _workbook, default);
                }
                catch (FormulaParseException)
                {
                    // Kept as written.
                }

                if (_workbook.Names.Any(existing => string.Equals(existing.Name, name, StringComparison.OrdinalIgnoreCase) && existing.SheetId == scope?.Id))
                {
                    continue;
                }

                _workbook.Names.Add(new DefinedName(name, canonical, scope?.Id));
            }
        }

        private void PrintNames(Worksheet sheet, string? printRanges, (int First, int Last)? rows, (int First, int Last)? columns)
        {
            if (!string.IsNullOrWhiteSpace(printRanges))
            {
                var ranges = SplitAddresses(printRanges).Select(RangeAddress).Where(entry => entry is not null).Select(entry => entry!.Value).ToList();
                if (ranges.Count > 0)
                {
                    _workbook.Names.Add(new DefinedName("_xlnm.Print_Area", string.Join(',', ranges.Select(entry => FormulaFormatter.FormatRange(entry.Sheet ?? sheet, entry.Range))), sheet.Id));
                }
            }

            var titles = new List<string>();
            if (columns is { } titleColumns)
            {
                titles.Add(FormulaFormatter.FormatRange(sheet, CellRange.Columns(titleColumns.First, titleColumns.Last)));
            }

            if (rows is { } titleRows)
            {
                titles.Add(FormulaFormatter.FormatRange(sheet, CellRange.Rows(titleRows.First, titleRows.Last)));
            }

            if (titles.Count > 0)
            {
                _workbook.Names.Add(new DefinedName("_xlnm.Print_Titles", string.Join(',', titles), sheet.Id));
            }
        }

        /// <summary>A range address list ("Tabelle1.A1:Tabelle1.B2 'Mein Blatt'.C3") split at spaces outside quotes.</summary>
        private static List<string> SplitAddresses(string list)
        {
            var parts = new List<string>();
            var quoted = false;
            var start = 0;
            for (var index = 0; index <= list.Length; index++)
            {
                if (index < list.Length && list[index] == '\'')
                {
                    quoted = !quoted;
                }

                if (index == list.Length || (list[index] == ' ' && !quoted))
                {
                    if (index > start)
                    {
                        parts.Add(list[start..index]);
                    }

                    start = index + 1;
                }
            }

            return parts;
        }

        /// <summary>"Tabelle1.A1:Tabelle1.D10" (or "$Tabelle1.$A$1") as a sheet and a range.</summary>
        private (Worksheet? Sheet, CellRange Range)? RangeAddress(string address)
        {
            address = address.Trim();
            var bracketed = "[" + (address.StartsWith('.') || address.StartsWith('$') ? address : "$" + address) + "]";
            if (OdsFormulas.ToCanonical(bracketed) is not { } canonical)
            {
                return null;
            }

            try
            {
                if (FormulaParser.ParseNode(canonical, _workbook, default) is ReferenceNode reference && reference.Area.Resolve(default) is { } range)
                {
                    var sheet = reference.Sheet is { IsMissing: false } sheetReference ? _workbook.GetSheetById(sheetReference.FirstSheetId) : null;
                    return (sheet, range);
                }
            }
            catch (FormulaParseException)
            {
                // Not a range.
            }

            return null;
        }

        private void ReadValidations(XElement? validations)
        {
            foreach (var element in validations?.Elements(TableNs + "content-validation") ?? [])
            {
                var name = (string?)element.Attribute(TableNs + "name") ?? "";
                if (!_validationRanges.TryGetValue(name, out var ranges) || ranges.Count == 0)
                {
                    continue;
                }

                var rule = OdsValidations.Read((string?)element.Attribute(TableNs + "condition"));
                if (rule is null)
                {
                    Note("Datenüberprüfungen, die ThixCalc nicht kennt");
                    continue;
                }

                var help = element.Element(TableNs + "help-message");
                var error = element.Element(TableNs + "error-message");
                rule = rule with
                {
                    AllowBlank = (string?)element.Attribute(TableNs + "allow-empty-cell") != "false",
                    ShowDropDown = (string?)element.Attribute(TableNs + "display-list") != "none",
                    ShowInputMessage = help is not null && (string?)help.Attribute(TableNs + "display") != "false",
                    PromptTitle = (string?)help?.Attribute(TableNs + "title"),
                    Prompt = Text(help),
                    ShowErrorMessage = error is null || (string?)error.Attribute(TableNs + "display") != "false",
                    ErrorTitle = (string?)error?.Attribute(TableNs + "title"),
                    Error = Text(error),
                    ErrorStyle = (string?)error?.Attribute(TableNs + "message-type") switch
                    {
                        "warning" => ValidationErrorStyle.Warning,
                        "information" => ValidationErrorStyle.Information,
                        _ => ValidationErrorStyle.Stop,
                    },
                };
                foreach (var group in ranges.GroupBy(entry => entry.Sheet))
                {
                    group.Key.Validations.Add(rule with { Ranges = group.Select(entry => entry.Range).ToList() });
                }
            }

            static string? Text(XElement? message)
            {
                var lines = message?.Elements(TextNs + "p").Select(PlainText).ToList();
                return lines is null || lines.Count == 0 ? null : string.Join('\n', lines);
            }
        }

        private void ReadDatabaseRanges(XElement? ranges)
        {
            foreach (var element in ranges?.Elements(TableNs + "database-range") ?? [])
            {
                var buttons = (string?)element.Attribute(TableNs + "display-filter-buttons") == "true";
                if (!buttons || (string?)element.Attribute(TableNs + "target-range-address") is not { } address || RangeAddress(address) is not { Sheet: { } sheet } target)
                {
                    continue;
                }

                if (sheet.AutoFilter is not null)
                {
                    continue;
                }

                sheet.AutoFilter = new AutoFilter(target.Range, OdsFilters.Read(element.Element(TableNs + "filter")));
            }
        }

        private void ReadConditionalFormats(Worksheet sheet, XElement? formats)
        {
            foreach (var format in formats?.Elements(CalcExtNs + "conditional-format") ?? [])
            {
                var ranges = SplitAddresses((string?)format.Attribute(CalcExtNs + "target-range-address") ?? "")
                    .Select(RangeAddress).Where(entry => entry is not null).Select(entry => entry!.Value.Range).ToList();
                if (ranges.Count == 0)
                {
                    continue;
                }

                var rules = new List<ConditionalFormatRule>();
                foreach (var child in format.Elements())
                {
                    if (child.Name != CalcExtNs + "condition")
                    {
                        Note("Farbskalen, Datenbalken und Symbolsätze (werden nicht übernommen)");
                        continue;
                    }

                    var rule = OdsConditionalFormats.Read((string?)child.Attribute(CalcExtNs + "value") ?? "", rules.Count + 1);
                    var styleName = (string?)child.Attribute(CalcExtNs + "apply-style-name");
                    var style = styleName is null ? null : _named.GetValueOrDefault(styleName) ?? _automatic.GetValueOrDefault(styleName);
                    if (rule is null || style is null)
                    {
                        Note("Bedingte Formatierungen, die ThixCalc nicht kennt (werden nicht übernommen)");
                        continue;
                    }

                    var look = OdsConditionalFormats.ReadFormat(style, DataStyleCode);
                    var index = _workbook.Styles.DifferentialFormats.IndexOf(look);
                    if (index < 0)
                    {
                        index = _workbook.Styles.DifferentialFormats.Count;
                        _workbook.Styles.DifferentialFormats.Add(look);
                    }

                    rules.Add(rule with { FormatId = index });
                }

                if (rules.Count > 0)
                {
                    sheet.ConditionalFormats.Add(new ConditionalFormat(ranges, rules));
                }
            }
        }

        // ------------------------------------------------------------ page setup

        private PageSetup ReadPageSetup(string? masterName)
        {
            var master = masterName is null ? _masterPages.GetValueOrDefault("Default") : _masterPages.GetValueOrDefault(masterName);
            if (master is null || (string?)master.Attribute(StyleNs + "page-layout-name") is not { } layoutName || !_pageLayouts.TryGetValue(layoutName, out var layout))
            {
                return PageSetup.Default;
            }

            var properties = layout.Element(StyleNs + "page-layout-properties");
            if (properties is null)
            {
                return PageSetup.Default;
            }

            var setup = PageSetup.Default;
            var width = Points((string?)properties.Attribute(FoNs + "page-width"));
            var height = Points((string?)properties.Attribute(FoNs + "page-height"));
            var landscape = (string?)properties.Attribute(StyleNs + "print-orientation") == "landscape" || (width > height && (string?)properties.Attribute(StyleNs + "print-orientation") is null);
            if (width is { } w && height is { } h)
            {
                var (shortSide, longSide) = (Math.Min(w, h) * 25.4 / 72, Math.Max(w, h) * 25.4 / 72);
                if (SheetPaper.All.FirstOrDefault(paper => Math.Abs(paper.WidthMillimetres - shortSide) < 2 && Math.Abs(paper.HeightMillimetres - longSide) < 2) is { } paper)
                {
                    setup = setup with { PaperSize = paper.Code };
                }
            }

            double Inches(string name, double fallback) => Points((string?)properties.Attribute(FoNs + name)) is { } points ? Math.Round(points / 72, 6) : fallback;
            setup = setup with
            {
                Landscape = landscape,
                LeftMargin = Inches("margin-left", setup.LeftMargin),
                RightMargin = Inches("margin-right", setup.RightMargin),
                TopMargin = Inches("margin-top", setup.TopMargin),
                BottomMargin = Inches("margin-bottom", setup.BottomMargin),
            };
            if (layout.Element(StyleNs + "header-style")?.Element(StyleNs + "header-footer-properties") is { } headerStyle && OdsHeaderFooter.Read(master.Element(StyleNs + "header")) is not null)
            {
                var extra = (Points((string?)headerStyle.Attribute(FoNs + "min-height")) ?? Points((string?)headerStyle.Attribute(SvgNs + "height")) ?? 0)
                    + (Points((string?)headerStyle.Attribute(FoNs + "margin-bottom")) ?? 0);
                setup = setup with { HeaderMargin = setup.TopMargin, TopMargin = Math.Round(setup.TopMargin + extra / 72, 6) };
            }

            if (layout.Element(StyleNs + "footer-style")?.Element(StyleNs + "header-footer-properties") is { } footerStyle && OdsHeaderFooter.Read(master.Element(StyleNs + "footer")) is not null)
            {
                var extra = (Points((string?)footerStyle.Attribute(FoNs + "min-height")) ?? Points((string?)footerStyle.Attribute(SvgNs + "height")) ?? 0)
                    + (Points((string?)footerStyle.Attribute(FoNs + "margin-top")) ?? 0);
                setup = setup with { FooterMargin = setup.BottomMargin, BottomMargin = Math.Round(setup.BottomMargin + extra / 72, 6) };
            }

            var print = ((string?)properties.Attribute(StyleNs + "print") ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var centering = (string?)properties.Attribute(StyleNs + "table-centering");
            setup = setup with
            {
                PrintGridLines = print.Contains("grid"),
                PrintHeadings = print.Contains("headers"),
                OverThenDown = (string?)properties.Attribute(StyleNs + "print-page-order") == "ltr",
                CenterHorizontally = centering is "horizontal" or "both",
                CenterVertically = centering is "vertical" or "both",
                FirstPageNumber = int.TryParse((string?)properties.Attribute(StyleNs + "first-page-number"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var first) ? first : null,
                Header = OdsHeaderFooter.Read(master.Element(StyleNs + "header")),
                Footer = OdsHeaderFooter.Read(master.Element(StyleNs + "footer")),
            };
            if (PercentOf((string?)properties.Attribute(StyleNs + "scale-to")) is { } scale)
            {
                setup = setup with { Scale = Math.Clamp((int)Math.Round(scale * 100), 10, 400), FitToPage = false };
            }
            else if (properties.Attribute(StyleNs + "scale-to-X") is not null || properties.Attribute(StyleNs + "scale-to-Y") is not null)
            {
                setup = setup with
                {
                    FitToPage = true,
                    FitToWidth = (int?)properties.Attribute(StyleNs + "scale-to-X") ?? 0,
                    FitToHeight = (int?)properties.Attribute(StyleNs + "scale-to-Y") ?? 0,
                };
            }
            else if ((int?)properties.Attribute(StyleNs + "scale-to-pages") is { } pages)
            {
                setup = setup with { FitToPage = true, FitToWidth = pages, FitToHeight = pages };
            }

            return setup;
        }

        // ------------------------------------------------------------ settings.xml, calculation

        private void ReadCalculationSettings(XElement? settings)
        {
            if (settings is null)
            {
                return;
            }

            if (DateOf((string?)settings.Element(TableNs + "null-date")?.Attribute(TableNs + "date-value")) is { } nullDate)
            {
                _workbook.Date1904 = nullDate.Year == 1904;
                if (nullDate != new DateTime(1899, 12, 30) && nullDate.Year != 1904)
                {
                    Note("Ein anderes Basisdatum als 30.12.1899 (Datumswerte werden auf 1900 bezogen)");
                }
            }

            if (settings.Element(TableNs + "iteration") is { } iteration)
            {
                _workbook.CalculationSettings = _workbook.CalculationSettings with
                {
                    Iterate = (string?)iteration.Attribute(TableNs + "status") == "enable",
                    MaxIterations = (int?)iteration.Attribute(TableNs + "steps") ?? 100,
                    MaxChange = NumberOf((string?)iteration.Attribute(TableNs + "minimum-difference")) ?? 0.001,
                };
            }
        }

        private void ReadSettings()
        {
            var settings = _package.Xml("settings.xml");
            var view = settings?.Root?.Descendants(ConfigNs + "config-item-set")
                .FirstOrDefault(set => (string?)set.Attribute(ConfigNs + "name") == "ooo:view-settings")
                ?.Descendants(ConfigNs + "config-item-map-indexed").FirstOrDefault(map => (string?)map.Attribute(ConfigNs + "name") == "Views")
                ?.Elements(ConfigNs + "config-item-map-entry").FirstOrDefault();
            if (view is null)
            {
                return;
            }

            string? Item(XElement parent, string name) =>
                parent.Elements(ConfigNs + "config-item").FirstOrDefault(item => (string?)item.Attribute(ConfigNs + "name") == name)?.Value;
            int IntItem(XElement parent, string name, int fallback) => int.TryParse(Item(parent, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : fallback;

            if (Item(view, "ActiveTable") is { } active && _workbook.FindSheet(active) is { } activeSheet)
            {
                _workbook.ActiveSheetIndex = activeSheet.Index;
            }

            var showGrid = Item(view, "ShowGrid") != "false";
            var headers = Item(view, "HasColumnRowHeaders") != "false";
            var zeros = Item(view, "ShowZeroValues") != "false";
            var formulas = Item(view, "ShowFormulas") == "true";
            var tables = view.Elements(ConfigNs + "config-item-map-named").FirstOrDefault(map => (string?)map.Attribute(ConfigNs + "name") == "Tables");
            foreach (var sheet in _workbook.Sheets)
            {
                var entry = tables?.Elements(ConfigNs + "config-item-map-entry").FirstOrDefault(candidate => (string?)candidate.Attribute(ConfigNs + "name") == sheet.Name);
                var result = sheet.View with { ShowGridLines = showGrid, ShowHeaders = headers, ShowZeros = zeros, ShowFormulas = formulas, TabSelected = sheet.Index == _workbook.ActiveSheetIndex };
                if (entry is not null)
                {
                    var frozenColumns = IntItem(entry, "HorizontalSplitMode", 0) == 2 ? Math.Clamp(IntItem(entry, "HorizontalSplitPosition", 0), 0, SheetLimits.LastColumn) : 0;
                    var frozenRows = IntItem(entry, "VerticalSplitMode", 0) == 2 ? Math.Clamp(IntItem(entry, "VerticalSplitPosition", 0), 0, SheetLimits.LastRow) : 0;
                    var cursor = new CellAddress(Math.Clamp(IntItem(entry, "CursorPositionY", 0), 0, SheetLimits.LastRow), Math.Clamp(IntItem(entry, "CursorPositionX", 0), 0, SheetLimits.LastColumn));
                    var frozen = frozenRows > 0 || frozenColumns > 0;
                    var topLeft = frozen
                        ? new CellAddress(frozenRows > 0 ? Math.Max(frozenRows, IntItem(entry, "PositionBottom", frozenRows)) : IntItem(entry, "PositionTop", 0),
                            frozenColumns > 0 ? Math.Max(frozenColumns, IntItem(entry, "PositionRight", frozenColumns)) : IntItem(entry, "PositionLeft", 0))
                        : new CellAddress(Math.Max(0, IntItem(entry, "PositionTop", 0)), Math.Max(0, IntItem(entry, "PositionLeft", 0)));
                    result = result with
                    {
                        FrozenColumns = frozenColumns,
                        FrozenRows = frozenRows,
                        TopLeft = new CellAddress(Math.Clamp(topLeft.Row, 0, SheetLimits.LastRow), Math.Clamp(topLeft.Column, 0, SheetLimits.LastColumn)),
                        Zoom = Math.Clamp(IntItem(entry, "ZoomValue", 100), 10, 400),
                        ActiveCell = cursor,
                        Selection = [CellRange.Cell(cursor)],
                        ShowGridLines = Item(entry, "ShowGrid") is { } own ? own != "false" : showGrid,
                    };
                }

                sheet.View = result;
            }
        }

        // ------------------------------------------------------------ what is not taken over

        private void NoteShapes(XElement shapes)
        {
            foreach (var shape in shapes.Elements())
            {
                if (shape.Name == DrawNs + "frame" && shape.Element(DrawNs + "object") is not null)
                {
                    Note("Diagramme und eingebettete Objekte");
                }
                else if (shape.Name == DrawNs + "frame" && shape.Element(DrawNs + "image") is not null)
                {
                    Note("Bilder");
                }
                else
                {
                    Note("Formen und Zeichnungen");
                }
            }
        }

        private void DetectUnsupported(XElement spreadsheet)
        {
            foreach (var table in spreadsheet.Elements(TableNs + "table"))
            {
                foreach (var cell in table.Descendants(TableNs + "table-cell"))
                {
                    foreach (var frame in cell.Elements(DrawNs + "frame").Concat(cell.Elements().Where(element => element.Name.Namespace == DrawNs && element.Name.LocalName != "frame")))
                    {
                        NoteShapes(new XElement("shapes", frame));
                    }
                }

                if (table.Element(TableNs + "scenario") is not null)
                {
                    Note("Szenarien");
                }

                if (table.Element(TableNs + "table-source") is not null)
                {
                    Note("Verknüpfungen mit anderen Dateien (die Werte bleiben)");
                }
            }

            if (spreadsheet.Element(TableNs + "data-pilot-tables") is not null)
            {
                Note("Pivot-Tabellen (Datenpilot; die Werte bleiben)");
            }

            if (spreadsheet.Element(TableNs + "tracked-changes") is not null)
            {
                Note("Nachverfolgte Änderungen");
            }

            if (_package.Paths.Any(path => path.StartsWith("Basic/", StringComparison.Ordinal) || path.StartsWith("Scripts/", StringComparison.Ordinal)))
            {
                Note("Makros (werden nicht übernommen und nie ausgeführt)");
            }
        }
    }
}
