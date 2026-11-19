using System.Globalization;
using System.Xml.Linq;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Core.Formats.Odf;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixCalc.Core.Formats.Ods;

/// <summary>
/// Writes a workbook as OpenDocument Spreadsheet (.ods): content.xml (sheets with columns, rows and cells – values, OpenFormula
/// formulas with their cached results, merges, matrix formulas, notes, links –, automatic cell/column/row/table styles and data
/// styles, validations, defined names, the autofilter, conditional formats of the kinds ODF holds), styles.xml (the default cell
/// style, page layouts and master pages with header and footer), settings.xml (active sheet, frozen panes, zoom, cursor, grid) and
/// meta.xml. Runs of equal empty cells and rows are written once with number-columns/rows-repeated. What the format cannot hold
/// is listed by <see cref="Losses"/> (the save asks first).
/// </summary>
public static partial class OdsWriter
{
    public const string MimeType = "application/vnd.oasis.opendocument.spreadsheet";

    /// <summary>The rows and columns LibreOffice's sheets have (the trailing filler rows/columns reach them).</summary>
    private const int FillerRows = SheetLimits.MaxRows;

    private const int FillerColumns = 1024;

    public static void Write(Workbook workbook, Stream stream)
    {
        var writer = new Writer(workbook);
        var package = new OdfPackageWriter(MimeType);
        package.AddXml("content.xml", writer.Content());
        package.AddXml("styles.xml", writer.Styles());
        package.AddXml("settings.xml", writer.Settings());
        package.AddXml("meta.xml", OdfPackageWriter.Meta("ThixCalc"));
        package.WriteTo(stream);
    }

    /// <summary>A column width in characters (the model's unit) in points (Excel's rule for Calibri 11: 7 px per character + 5 px).</summary>
    internal static double ColumnPoints(double characters) => characters <= 0 ? 0 : (characters * 7 + 5) * 0.75;

    internal static double ColumnCharacters(double points) => Math.Round(Math.Max(0, (points / 0.75 - 5) / 7), 8);

    internal static string Exact(double points) => Math.Round(points, 6).ToString("0.######", CultureInfo.InvariantCulture) + "pt";

    private sealed partial class Writer(Workbook workbook)
    {
        private readonly Dictionary<int, string> _cellStyles = [];
        private readonly Dictionary<string, string> _dataStyles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _columnStyles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _rowStyles = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _tableStyles = new(StringComparer.Ordinal);
        private readonly List<XElement> _automatic = [];
        private readonly List<XElement> _dataStyleElements = [];
        private readonly HashSet<string> _fonts = new(StringComparer.Ordinal);
        private readonly List<XElement> _validations = [];
        private readonly List<XElement> _databaseRanges = [];
        private readonly Formatting.NumberFormat _general = Formatting.NumberFormat.General;

        private Globalization.CalcLocale Locale => workbook.Calculation.Locale;

        private CellStyle Defaults => workbook.Styles.Default;

        // ------------------------------------------------------------ content.xml

        public XDocument Content()
        {
            var spreadsheet = new XElement(OfficeNs + "spreadsheet");
            var settings = CalculationSettings();
            if (settings is not null)
            {
                spreadsheet.Add(settings);
            }

            var validations = new XElement(TableNs + "content-validations");
            spreadsheet.Add(validations);
            var tables = workbook.Sheets.Select(WriteSheet).ToList();
            validations.Add(_validations);
            if (_validations.Count == 0)
            {
                validations.Remove();
            }

            spreadsheet.Add(tables);
            if (NamedExpressions(null) is { } names)
            {
                spreadsheet.Add(names);
            }

            if (_databaseRanges.Count > 0)
            {
                spreadsheet.Add(new XElement(TableNs + "database-ranges", _databaseRanges));
            }

            var root = new XElement(OfficeNs + "document-content", AllDeclarations(), new XAttribute(OfficeNs + "version", "1.3"),
                FontFaces(),
                new XElement(OfficeNs + "automatic-styles", _automatic),
                new XElement(OfficeNs + "body", spreadsheet));
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        }

        private XElement? CalculationSettings()
        {
            var calculation = workbook.CalculationSettings;
            if (!workbook.Date1904 && !calculation.Iterate)
            {
                return null;
            }

            var element = new XElement(TableNs + "calculation-settings");
            if (workbook.Date1904)
            {
                element.Add(new XElement(TableNs + "null-date", new XAttribute(TableNs + "date-value", "1904-01-01")));
            }

            if (calculation.Iterate)
            {
                element.Add(new XElement(TableNs + "iteration", new XAttribute(TableNs + "status", "enable"),
                    new XAttribute(TableNs + "steps", calculation.MaxIterations),
                    new XAttribute(TableNs + "minimum-difference", Number(calculation.MaxChange))));
            }

            return element;
        }

        private XElement FontFaces()
        {
            _fonts.Add(Defaults.Font.Name);
            return new XElement(OfficeNs + "font-face-decls", _fonts.Order(StringComparer.Ordinal).Select(name => new XElement(StyleNs + "font-face",
                new XAttribute(StyleNs + "name", name),
                new XAttribute(SvgNs + "font-family", name.Contains(' ') ? $"'{name}'" : name))));
        }

        // ------------------------------------------------------------ one sheet

        private XElement WriteSheet(Worksheet sheet)
        {
            var table = new XElement(TableNs + "table",
                new XAttribute(TableNs + "name", sheet.Name),
                new XAttribute(TableNs + "style-name", TableStyle(sheet)));
            if (sheet.ProtectionXml is not null)
            {
                table.Add(new XAttribute(TableNs + "protected", "true"));
            }

            var titles = PrintTitles(sheet);
            if (PrintRanges(sheet) is { } printRanges)
            {
                table.Add(new XAttribute(TableNs + "print-ranges", printRanges));
            }

            var validationNames = Validations(sheet);
            var layout = new SheetLayout(sheet, validationNames, titles);
            WriteColumns(sheet, table, layout);
            WriteRows(sheet, table, layout);
            if (NamedExpressions(sheet) is { } names)
            {
                table.Add(names);
            }

            if (ConditionalFormats(sheet) is { } formats)
            {
                table.Add(formats);
            }

            AutoFilter(sheet);
            return table;
        }

        private string TableStyle(Worksheet sheet)
        {
            var master = MasterPage(sheet);
            var hidden = sheet.Visibility != SheetVisibility.Visible;
            var tab = sheet.TabColor is { } color ? "#" + color.Resolve(workbook.Theme, ThixOffice.Text.Drawing.RgbColor.White).ToHex() : null;
            var key = $"{master}|{hidden}|{tab}";
            if (_tableStyles.TryGetValue(key, out var name))
            {
                return name;
            }

            name = "ta" + (_tableStyles.Count + 1);
            _tableStyles[key] = name;
            var properties = new XElement(StyleNs + "table-properties", new XAttribute(TableNs + "display", Boolean(!hidden)), new XAttribute(StyleNs + "writing-mode", "lr-tb"));
            if (tab is not null)
            {
                properties.Add(new XAttribute(TableOooNs + "tab-color", tab));
            }

            _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "table"),
                new XAttribute(StyleNs + "master-page-name", master), properties));
            return name;
        }

        /// <summary>The automatic style of a cell style id ("Default" for 0), with its data style.</summary>
        private string CellStyle(int id)
        {
            if (id == 0)
            {
                return "Default";
            }

            if (_cellStyles.TryGetValue(id, out var name))
            {
                return name;
            }

            var style = workbook.Styles[id];
            name = "ce" + (_cellStyles.Count + 1);
            _cellStyles[id] = name;
            if (style.Font.Name != Defaults.Font.Name)
            {
                _fonts.Add(style.Font.Name);
            }

            _automatic.Add(OdsCellStyles.Write(style, Defaults, workbook.Theme, name, DataStyle(style.NumberFormat)));
            return name;
        }

        /// <summary>The data style of a format code (null for General).</summary>
        private string? DataStyle(string code)
        {
            if (string.IsNullOrEmpty(code) || Formatting.NumberFormat.Parse(code).IsGeneral)
            {
                return null;
            }

            if (_dataStyles.TryGetValue(code, out var name))
            {
                return name;
            }

            name = "N" + (_dataStyles.Count + 100);
            var elements = OdsNumberFormats.Write(code, name);
            if (elements.Count == 0)
            {
                return null;
            }

            _dataStyles[code] = name;
            _dataStyleElements.AddRange(elements);
            return name;
        }

        private string ColumnStyle(double width, bool optimal)
        {
            var text = Exact(ColumnPoints(width));
            var key = text + (optimal ? "|o" : "");
            if (_columnStyles.TryGetValue(key, out var name))
            {
                return name;
            }

            name = "co" + (_columnStyles.Count + 1);
            _columnStyles[key] = name;
            var properties = new XElement(StyleNs + "table-column-properties", new XAttribute(FoNs + "break-before", "auto"), new XAttribute(StyleNs + "column-width", text));
            if (optimal)
            {
                properties.Add(new XAttribute(StyleNs + "use-optimal-column-width", "true"));
            }

            _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "table-column"), properties));
            return name;
        }

        private string RowStyle(double height, bool optimal)
        {
            var text = Exact(height);
            var key = text + (optimal ? "|o" : "");
            if (_rowStyles.TryGetValue(key, out var name))
            {
                return name;
            }

            name = "ro" + (_rowStyles.Count + 1);
            _rowStyles[key] = name;
            _automatic.Add(new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "table-row"),
                new XElement(StyleNs + "table-row-properties", new XAttribute(StyleNs + "row-height", text), new XAttribute(FoNs + "break-before", "auto"),
                    new XAttribute(StyleNs + "use-optimal-row-height", Boolean(optimal)))));
            return name;
        }

        // ------------------------------------------------------------ columns

        private void WriteColumns(Worksheet sheet, XElement table, SheetLayout layout)
        {
            var last = layout.LastColumn;
            var columns = sheet.Columns;
            var boundaries = new SortedSet<int> { 0, last + 1 };
            foreach (var (index, _) in columns.Entries)
            {
                if (index <= last)
                {
                    boundaries.Add(index);
                    boundaries.Add(index + 1);
                }
            }

            if (layout.TitleColumns is { } titleColumns)
            {
                boundaries.Add(titleColumns.First);
                boundaries.Add(titleColumns.Last + 1);
            }

            XElement? header = null;
            var points = boundaries.Where(boundary => boundary <= last + 1).ToList();
            for (var position = 0; position + 1 < points.Count; position++)
            {
                var start = points[position];
                var count = points[position + 1] - start;
                var element = ColumnElement(columns, columns[start], count);
                if (layout.TitleColumns is { } titles && start >= titles.First && start <= titles.Last)
                {
                    header ??= new XElement(TableNs + "table-header-columns");
                    if (header.Parent is null)
                    {
                        table.Add(header);
                    }

                    header.Add(element);
                    continue;
                }

                table.Add(element);
            }

            if (last + 1 < FillerColumns)
            {
                table.Add(ColumnElement(columns, default, FillerColumns - last - 1));
            }
        }

        private XElement ColumnElement(AxisCollection columns, AxisInfo info, int count)
        {
            var element = new XElement(TableNs + "table-column",
                new XAttribute(TableNs + "style-name", ColumnStyle(info.Size ?? columns.DefaultSize, info.BestFit)));
            if (count > 1)
            {
                element.Add(new XAttribute(TableNs + "number-columns-repeated", count));
            }

            if (info.Hidden)
            {
                element.Add(new XAttribute(TableNs + "visibility", "collapse"));
            }

            element.Add(new XAttribute(TableNs + "default-cell-style-name", CellStyle(info.StyleId ?? 0)));
            return element;
        }

        // ------------------------------------------------------------ rows and cells

        private void WriteRows(Worksheet sheet, XElement table, SheetLayout layout)
        {
            XElement? header = null;
            var rows = sheet.Rows;
            for (var position = 0; position + 1 < layout.RowBoundaries.Count; position++)
            {
                var start = layout.RowBoundaries[position];
                var count = layout.RowBoundaries[position + 1] - start;
                var info = rows[start];
                var row = new XElement(TableNs + "table-row",
                    new XAttribute(TableNs + "style-name", RowStyle(info.Size ?? rows.DefaultSize, !info.CustomSize)));
                if (count > 1)
                {
                    row.Add(new XAttribute(TableNs + "number-rows-repeated", count));
                }

                if (info.Hidden)
                {
                    row.Add(new XAttribute(TableNs + "visibility", sheet.AutoFilter is { } filter && filter.Range.Contains(start, filter.Range.FirstColumn) ? "filter" : "collapse"));
                }

                if (info.StyleId is { } rowStyle)
                {
                    row.Add(new XAttribute(TableNs + "default-cell-style-name", CellStyle(rowStyle)));
                }

                WriteCells(sheet, row, start, layout);
                if (layout.TitleRows is { } titles && start >= titles.FirstRow && start <= titles.LastRow)
                {
                    header ??= new XElement(TableNs + "table-header-rows");
                    if (header.Parent is null)
                    {
                        table.Add(header);
                    }

                    header.Add(row);
                    continue;
                }

                table.Add(row);
            }

            var end = layout.RowBoundaries[^1];
            if (end < FillerRows)
            {
                table.Add(new XElement(TableNs + "table-row",
                    new XAttribute(TableNs + "style-name", RowStyle(rows.DefaultSize, true)),
                    new XAttribute(TableNs + "number-rows-repeated", FillerRows - end),
                    new XElement(TableNs + "table-cell")));
            }
        }

        private void WriteCells(Worksheet sheet, XElement row, int rowIndex, SheetLayout layout)
        {
            var cells = layout.CellsOf(rowIndex);
            var boundaries = new SortedSet<int> { 0 };
            foreach (var (column, _) in cells)
            {
                boundaries.Add(column);
                boundaries.Add(column + 1);
            }

            foreach (var address in sheet.Comments.Keys)
            {
                if (address.Row == rowIndex)
                {
                    boundaries.Add(address.Column);
                    boundaries.Add(address.Column + 1);
                }
            }

            var limit = Math.Max(layout.LastColumn + 1, FillerColumns);
            foreach (var range in layout.RangesOn(rowIndex))
            {
                boundaries.Add(Math.Min(range.FirstColumn, limit));
                boundaries.Add(Math.Min(range.FirstColumn + 1, limit));
                boundaries.Add(Math.Min(range.LastColumn + 1, limit));
            }

            var points = boundaries.ToList();
            if (points.Count == 1)
            {
                row.Add(new XElement(TableNs + "table-cell"));
                return;
            }

            for (var position = 0; position + 1 < points.Count; position++)
            {
                var start = points[position];
                var count = points[position + 1] - start;
                var merge = layout.MergeAt(rowIndex, start);
                var covered = merge is { } range && (range.FirstRow != rowIndex || range.FirstColumn != start);
                var element = new XElement(TableNs + (covered ? "covered-table-cell" : "table-cell"));
                if (count > 1)
                {
                    element.Add(new XAttribute(TableNs + "number-columns-repeated", count));
                }

                if (layout.ValidationAt(rowIndex, start) is { } validation)
                {
                    element.Add(new XAttribute(TableNs + "content-validation-name", validation));
                }

                if (!covered && merge is { } anchor)
                {
                    element.Add(new XAttribute(TableNs + "number-columns-spanned", anchor.ColumnCount), new XAttribute(TableNs + "number-rows-spanned", anchor.RowCount));
                }

                if (cells.TryGetValue(start, out var cell))
                {
                    WriteCell(sheet, element, new CellAddress(rowIndex, start), cell);
                }
                else if (sheet.Comments.TryGetValue(new CellAddress(rowIndex, start), out var lonely))
                {
                    element.Add(Annotation(lonely));
                }

                row.Add(element);
            }
        }

        private void WriteCell(Worksheet sheet, XElement element, CellAddress address, Cell cell)
        {
            if (cell.StyleId != 0)
            {
                element.Add(new XAttribute(TableNs + "style-name", CellStyle(cell.StyleId)));
            }

            if (cell.Formula is { } formula && !cell.IsSpilled)
            {
                if (OdsFormulas.Write(formula, sheet, address) is { } text)
                {
                    element.Add(new XAttribute(TableNs + "formula", text));
                    var formulaCell = sheet.GetFormulaCell(address.Row, address.Column);
                    if ((cell.ArrayRange ?? formulaCell?.SpillRange) is { IsSingleCell: false } matrix)
                    {
                        element.Add(new XAttribute(TableNs + "number-matrix-columns-spanned", matrix.ColumnCount),
                            new XAttribute(TableNs + "number-matrix-rows-spanned", matrix.RowCount));
                    }
                }
            }

            var style = workbook.Styles[sheet.GetStyleId(address.Row, address.Column)];
            WriteValue(element, cell.Value, style.NumberFormat, cell.Formula is not null && !cell.IsSpilled);
            if (sheet.Comments.TryGetValue(address, out var comment))
            {
                element.AddFirst(Annotation(comment));
            }

            if (cell.Value.IsBlank)
            {
                return;
            }

            var link = sheet.Hyperlinks.FirstOrDefault(candidate => candidate.Range.Contains(address));
            var shown = DisplayText(cell.Value, style.NumberFormat);
            var lines = shown.Split('\n');
            for (var index = 0; index < lines.Length; index++)
            {
                var paragraph = new XElement(TextNs + "p");
                if (index == 0 && link is not null && LinkHref(link) is { } href)
                {
                    paragraph.Add(new XElement(TextNs + "a", new XAttribute(XLinkNs + "href", href), new XAttribute(XLinkNs + "type", "simple"), TextContent(lines[index])));
                }
                else
                {
                    paragraph.Add(TextContent(lines[index]));
                }

                element.Add(paragraph);
            }
        }

        private static string? LinkHref(Hyperlink link)
        {
            if (link.Target is { Length: > 0 } target)
            {
                return link.Location is { Length: > 0 } location ? target + "#" + location : target;
            }

            if (link.Location is not { Length: > 0 } place)
            {
                return null;
            }

            var bang = place.LastIndexOf('!');
            if (bang < 0)
            {
                return "#" + place;
            }

            var sheetName = place[..bang].Trim('\'').Replace("''", "'", StringComparison.Ordinal);
            return "#" + OdsFormulas.QuoteSheet(sheetName) + "." + place[(bang + 1)..].Replace("$", "", StringComparison.Ordinal);
        }

        private void WriteValue(XElement element, CellValue value, string code, bool isFormula)
        {
            var format = Formatting.NumberFormat.Parse(code);
            switch (value.Kind)
            {
                case CellValueKind.Number:
                    if (format.IsDateTime && DateKind(code) is { } dateKind)
                    {
                        if (dateKind == "time")
                        {
                            element.Add(new XAttribute(OfficeNs + "value-type", "time"), new XAttribute(CalcExtNs + "value-type", "time"),
                                new XAttribute(OfficeNs + "time-value", Duration(value.Number)));
                            return;
                        }

                        if (Calculation.DateSerial.ToDateTime(value.Number, workbook.Date1904) is { } date)
                        {
                            element.Add(new XAttribute(OfficeNs + "value-type", "date"), new XAttribute(CalcExtNs + "value-type", "date"),
                                new XAttribute(OfficeNs + "date-value", DateText(date)));
                            return;
                        }
                    }

                    var type = format.IsPercent ? "percentage" : CurrencyOf(code) is not null ? "currency" : "float";
                    element.Add(new XAttribute(OfficeNs + "value-type", type), new XAttribute(CalcExtNs + "value-type", type));
                    if (type == "currency")
                    {
                        element.Add(new XAttribute(OfficeNs + "currency", CurrencyOf(code)!));
                    }

                    element.Add(new XAttribute(OfficeNs + "value", Number(value.Number)));
                    return;
                case CellValueKind.Boolean:
                    element.Add(new XAttribute(OfficeNs + "value-type", "boolean"), new XAttribute(CalcExtNs + "value-type", "boolean"),
                        new XAttribute(OfficeNs + "boolean-value", Boolean(value.Boolean)));
                    return;
                case CellValueKind.Text:
                    element.Add(new XAttribute(OfficeNs + "value-type", "string"), new XAttribute(CalcExtNs + "value-type", "string"));
                    if (isFormula)
                    {
                        element.Add(new XAttribute(OfficeNs + "string-value", value.Text));
                    }

                    return;
                case CellValueKind.Error:
                    element.Add(new XAttribute(OfficeNs + "value-type", "string"), new XAttribute(OfficeNs + "string-value", ""),
                        new XAttribute(CalcExtNs + "value-type", "error"));
                    return;
            }
        }

        /// <summary>"date" when the format shows a date (with or without time), "time" when only a time.</summary>
        private static string? DateKind(string code)
        {
            var section = code.Split(';')[0];
            var builder = new System.Text.StringBuilder();
            var inQuote = false;
            for (var index = 0; index < section.Length; index++)
            {
                var character = section[index];
                if (character == '"')
                {
                    inQuote = !inQuote;
                    continue;
                }

                if (inQuote)
                {
                    continue;
                }

                if (character is '\\' or '_' or '*')
                {
                    index++;
                    continue;
                }

                if (character == '[')
                {
                    var end = section.IndexOf(']', index);
                    if (end < 0)
                    {
                        break;
                    }

                    var content = section[(index + 1)..end].ToLowerInvariant();
                    if (content.Length > 0 && content.All(letter => letter is 'h' or 'm' or 's'))
                    {
                        builder.Append('h');
                    }

                    index = end;
                    continue;
                }

                builder.Append(char.ToLowerInvariant(character));
            }

            var letters = builder.ToString();
            if (letters.Contains('y') || letters.Contains('d') || letters.Contains("mmm", StringComparison.Ordinal) || letters.Contains('e'))
            {
                return "date";
            }

            if (letters.Contains('m') && !letters.Contains('h') && !letters.Contains('s'))
            {
                return "date";
            }

            return letters.Contains('h') || letters.Contains('s') ? "time" : null;
        }

        /// <summary>The ISO currency of a format with a currency symbol (€ → EUR …), or null.</summary>
        private static string? CurrencyOf(string code)
        {
            var start = code.IndexOf("[$", StringComparison.Ordinal);
            if (start < 0)
            {
                return null;
            }

            var end = code.IndexOfAny(['-', ']'], start + 2);
            var symbol = end < 0 ? "" : code[(start + 2)..end];
            return symbol switch
            {
                "€" or "EUR" => "EUR",
                "$" or "USD" => "USD",
                "£" or "GBP" => "GBP",
                "¥" or "JPY" => "JPY",
                "CHF" or "Fr." => "CHF",
                _ => null,
            };
        }

        private string DisplayText(CellValue value, string code)
        {
            if (value.IsText)
            {
                return value.Text;
            }

            if (value.IsError)
            {
                return CellErrors.NameOf(value.Error);
            }

            if (value.IsBoolean)
            {
                return Locale.BooleanName(value.Boolean);
            }

            var format = string.IsNullOrEmpty(code) ? _general : Formatting.NumberFormat.Parse(code);
            return NumberFormatter.Format(value, format, Locale, workbook.Date1904).Text;
        }

        private static XElement Annotation(CellComment comment)
        {
            var annotation = new XElement(OfficeNs + "annotation");
            if (comment.Visible)
            {
                annotation.Add(new XAttribute(OfficeNs + "display", "true"));
            }

            if (comment.Author.Length > 0)
            {
                annotation.Add(new XElement(DcNs + "creator", comment.Author));
            }

            foreach (var line in comment.Text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
            {
                annotation.Add(new XElement(TextNs + "p", TextContent(line)));
            }

            return annotation;
        }

        // ------------------------------------------------------------ validations

        /// <summary>The content-validation names of a sheet's rules (written into the shared list).</summary>
        private List<(DataValidation Rule, string Name)> Validations(Worksheet sheet)
        {
            var result = new List<(DataValidation, string)>();
            foreach (var rule in sheet.Validations)
            {
                if (rule.Ranges.Count == 0 || OdsValidations.Write(rule, sheet) is not { } condition)
                {
                    continue;
                }

                var name = "val" + (_validations.Count + 1);
                var origin = rule.Origin;
                var element = new XElement(TableNs + "content-validation",
                    new XAttribute(TableNs + "name", name),
                    condition.Length == 0 ? null : new XAttribute(TableNs + "condition", condition),
                    new XAttribute(TableNs + "allow-empty-cell", Boolean(rule.AllowBlank)),
                    new XAttribute(TableNs + "base-cell-address", OdsFormulas.RangeAddress(sheet, CellRange.Cell(origin))));
                if (rule.Type == ValidationType.List)
                {
                    element.Add(new XAttribute(TableNs + "display-list", rule.ShowDropDown ? "unsorted" : "none"));
                }

                element.Add(new XElement(TableNs + "help-message", new XAttribute(TableNs + "display", Boolean(rule.ShowInputMessage)),
                    rule.PromptTitle is null ? null : new XAttribute(TableNs + "title", rule.PromptTitle),
                    Lines(rule.Prompt)));
                element.Add(new XElement(TableNs + "error-message", new XAttribute(TableNs + "display", Boolean(rule.ShowErrorMessage)),
                    new XAttribute(TableNs + "message-type", rule.ErrorStyle switch
                    {
                        ValidationErrorStyle.Warning => "warning",
                        ValidationErrorStyle.Information => "information",
                        _ => "stop",
                    }),
                    rule.ErrorTitle is null ? null : new XAttribute(TableNs + "title", rule.ErrorTitle),
                    Lines(rule.Error)));
                _validations.Add(element);
                result.Add((rule, name));
            }

            return result;

            static IEnumerable<XElement> Lines(string? text) =>
                text is null ? [] : text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => new XElement(TextNs + "p", TextContent(line)));
        }

        // ------------------------------------------------------------ names, print ranges, filters

        private XElement? NamedExpressions(Worksheet? scope)
        {
            var element = new XElement(TableNs + "named-expressions");
            foreach (var name in workbook.Names)
            {
                if (name.IsBuiltIn || name.SheetId != scope?.Id)
                {
                    continue;
                }

                var sheet = scope ?? workbook.Sheets[0];
                Formulas.FormulaNode node;
                try
                {
                    node = FormulaParser.ParseNode(name.Formula, workbook, default);
                }
                catch (FormulaParseException)
                {
                    continue;
                }

                var baseCell = "$" + OdsFormulas.QuoteSheet(sheet.Name) + ".$A$1";
                if (node is ReferenceNode { Sheet: { Is3D: false, IsMissing: false } reference } && workbook.GetSheetById(reference.FirstSheetId) is { } target
                    && OdsFormulas.Expression(node, workbook, target, default) is { } rangeText)
                {
                    element.Add(new XElement(TableNs + "named-range",
                        new XAttribute(TableNs + "name", name.Name),
                        new XAttribute(TableNs + "base-cell-address", baseCell),
                        new XAttribute(TableNs + "cell-range-address", rangeText.Trim('[', ']'))));
                    continue;
                }

                if (OdsFormulas.Expression(node, workbook, sheet, default) is { } expression)
                {
                    element.Add(new XElement(TableNs + "named-expression",
                        new XAttribute(TableNs + "name", name.Name),
                        new XAttribute(TableNs + "base-cell-address", baseCell),
                        new XAttribute(TableNs + "expression", "of:=" + expression)));
                }
            }

            return element.HasElements ? element : null;
        }

        /// <summary>The built-in name of a sheet (_xlnm.Print_Area …) parsed into its ranges.</summary>
        private List<(CellRange Range, AreaKind Kind)> BuiltInRanges(Worksheet sheet, string builtIn)
        {
            var result = new List<(CellRange, AreaKind)>();
            var defined = workbook.Names.FirstOrDefault(name => name.SheetId == sheet.Id && string.Equals(name.Name, builtIn, StringComparison.OrdinalIgnoreCase));
            if (defined is null)
            {
                return result;
            }

            try
            {
                Collect(FormulaParser.ParseNode(defined.Formula, workbook, default));
            }
            catch (FormulaParseException)
            {
                // Not a range: no print area.
            }

            return result;

            void Collect(Formulas.FormulaNode node)
            {
                switch (node)
                {
                    case ReferenceNode reference when reference.Area.Resolve(default) is { } range:
                        result.Add((range, reference.Area.Kind));
                        break;
                    case BinaryNode { Operator: BinaryOperator.Union } union:
                        Collect(union.Left);
                        Collect(union.Right);
                        break;
                    case ParenthesesNode parentheses:
                        Collect(parentheses.Inner);
                        break;
                }
            }
        }

        private string? PrintRanges(Worksheet sheet)
        {
            var ranges = BuiltInRanges(sheet, "_xlnm.Print_Area");
            return ranges.Count == 0 ? null : string.Join(' ', ranges.Select(entry => OdsFormulas.RangeAddress(sheet, entry.Range)));
        }

        private (CellRange? Rows, CellRange? Columns) PrintTitles(Worksheet sheet)
        {
            CellRange? rows = null;
            CellRange? columns = null;
            foreach (var (range, kind) in BuiltInRanges(sheet, "_xlnm.Print_Titles"))
            {
                if (kind == AreaKind.Rows || range.IsWholeRows)
                {
                    rows = range;
                }
                else if (kind == AreaKind.Columns || range.IsWholeColumns)
                {
                    columns = range;
                }
            }

            return (rows, columns);
        }

        private void AutoFilter(Worksheet sheet)
        {
            if (sheet.AutoFilter is not { } filter)
            {
                return;
            }

            var element = new XElement(TableNs + "database-range",
                new XAttribute(TableNs + "name", "__Anonymous_Sheet_DB__" + sheet.Index.ToString(CultureInfo.InvariantCulture)),
                new XAttribute(TableNs + "target-range-address", OdsFormulas.RangeAddress(sheet, filter.Range)),
                new XAttribute(TableNs + "display-filter-buttons", "true"));
            if (OdsFilters.Write(filter) is { } conditions)
            {
                element.Add(conditions);
            }

            _databaseRanges.Add(element);
        }

        private XElement? ConditionalFormats(Worksheet sheet)
        {
            var element = new XElement(CalcExtNs + "conditional-formats");
            foreach (var format in sheet.ConditionalFormats)
            {
                if (OdsConditionalFormats.Write(format, sheet, NamedConditionStyle) is { } written)
                {
                    element.Add(written);
                }
            }

            return element.HasElements ? element : null;
        }

        // ------------------------------------------------------------ the layout of a sheet's grid

        /// <summary>
        /// The structure of a sheet's rows: boundaries where anything changes (cells, row infos, merges, validations, notes, title
        /// rows), so every run between two boundaries is written once with number-rows-repeated; the cells by row; the ranges that
        /// give cells attributes.
        /// </summary>
        private sealed class SheetLayout
        {
            private readonly Dictionary<int, SortedList<int, Cell>> _cells = [];
            private readonly List<(CellRange Range, string Name)> _validations = [];
            private readonly IReadOnlyList<CellRange> _merges;

            public SheetLayout(Worksheet sheet, List<(DataValidation Rule, string Name)> validations, (CellRange? Rows, CellRange? Columns) titles)
            {
                _merges = sheet.Merges.Ranges;
                TitleRows = titles.Rows;
                TitleColumns = titles.Columns is { } columns ? (columns.FirstColumn, columns.LastColumn) : null;
                var rows = new SortedSet<int> { 0 };
                var lastColumn = -1;
                foreach (var entry in sheet.GetCells())
                {
                    if (!_cells.TryGetValue(entry.Row, out var row))
                    {
                        row = [];
                        _cells[entry.Row] = row;
                        rows.Add(entry.Row);
                        rows.Add(entry.Row + 1);
                    }

                    row[entry.Column] = entry.Cell;
                    lastColumn = Math.Max(lastColumn, entry.Column);
                }

                foreach (var (index, _) in sheet.Rows.Entries)
                {
                    rows.Add(index);
                    rows.Add(index + 1);
                }

                foreach (var merge in _merges)
                {
                    rows.Add(merge.FirstRow);
                    rows.Add(merge.FirstRow + 1);
                    rows.Add(merge.LastRow + 1);
                    lastColumn = Math.Max(lastColumn, merge.LastColumn);
                }

                foreach (var (rule, name) in validations)
                {
                    foreach (var range in rule.Ranges)
                    {
                        _validations.Add((range, name));
                        rows.Add(range.FirstRow);
                        rows.Add(Math.Min(range.LastRow + 1, SheetLimits.MaxRows));
                        lastColumn = Math.Max(lastColumn, Math.Min(range.LastColumn, FillerColumns - 1));
                    }
                }

                foreach (var address in sheet.Comments.Keys)
                {
                    rows.Add(address.Row);
                    rows.Add(address.Row + 1);
                    lastColumn = Math.Max(lastColumn, address.Column);
                    if (!_cells.ContainsKey(address.Row))
                    {
                        _cells[address.Row] = [];
                    }
                }

                if (TitleRows is { } title)
                {
                    rows.Add(title.FirstRow);
                    rows.Add(title.LastRow + 1);
                }

                LastColumn = Math.Max(0, Math.Max(lastColumn, sheet.Columns.Entries.Select(entry => entry.Index).DefaultIfEmpty(0).Max()));
                if (TitleColumns is { } titleColumns)
                {
                    LastColumn = Math.Max(LastColumn, titleColumns.Last);
                }

                LastColumn = Math.Min(LastColumn, SheetLimits.LastColumn);
                RowBoundaries = rows.Where(row => row <= SheetLimits.MaxRows).ToList();
            }

            public IReadOnlyList<int> RowBoundaries { get; }

            public int LastColumn { get; }

            public CellRange? TitleRows { get; }

            public (int First, int Last)? TitleColumns { get; }

            public IReadOnlyDictionary<int, Cell> CellsOf(int row) =>
                _cells.TryGetValue(row, out var cells) ? cells : (IReadOnlyDictionary<int, Cell>)new Dictionary<int, Cell>();

            public IEnumerable<CellRange> RangesOn(int row) =>
                _validations.Select(entry => entry.Range).Concat(_merges).Where(range => range.FirstRow <= row && range.LastRow >= row);

            public CellRange? MergeAt(int row, int column) => _merges.FirstOrDefault(range => range.Contains(row, column)) is { RowCount: > 0 } merge ? merge : null;

            public string? ValidationAt(int row, int column) => _validations.FirstOrDefault(entry => entry.Range.Contains(row, column)).Name;
        }
    }
}
