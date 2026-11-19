using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ThixCalc.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixCalc.Core.Formats.Ods;

/// <summary>Excel's header/footer codes (&amp;L &amp;C &amp;R, &amp;P, &amp;N, &amp;D, &amp;T, &amp;A, &amp;F) ⇄ ODF regions with text fields.</summary>
internal static class OdsHeaderFooter
{
    public static IEnumerable<XElement> Write(string text)
    {
        var sections = new Dictionary<char, StringBuilder> { ['L'] = new(), ['C'] = new(), ['R'] = new() };
        var current = 'C';
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] != '&' || index + 1 >= text.Length)
            {
                sections[current].Append(text[index]);
                continue;
            }

            var code = text[index + 1];
            switch (char.ToUpperInvariant(code))
            {
                case 'L' or 'C' or 'R':
                    current = char.ToUpperInvariant(code);
                    index++;
                    break;
                case '&':
                    sections[current].Append('&').Append('&');
                    index++;
                    break;
                case 'P' or 'N' or 'D' or 'T' or 'A' or 'F' or 'Z':
                    sections[current].Append('&').Append(char.ToUpperInvariant(code));
                    index++;
                    break;
                case '"':
                    var end = text.IndexOf('"', index + 2);
                    index = end < 0 ? text.Length : end;
                    break;
                case 'K':
                    index += 7;
                    break;
                default:
                    index++;
                    while (index + 1 < text.Length && char.IsAsciiDigit(text[index + 1]) && char.IsAsciiDigit(code))
                    {
                        index++;
                    }

                    break;
            }
        }

        foreach (var (key, name) in new[] { ('L', "region-left"), ('C', "region-center"), ('R', "region-right") })
        {
            if (sections[key].Length > 0)
            {
                yield return new XElement(StyleNs + name, sections[key].ToString().Split('\n').Select(Paragraph));
            }
        }
    }

    private static XElement Paragraph(string line)
    {
        var paragraph = new XElement(TextNs + "p");
        var run = new StringBuilder();
        for (var index = 0; index < line.Length; index++)
        {
            if (line[index] != '&' || index + 1 >= line.Length)
            {
                run.Append(line[index]);
                continue;
            }

            var code = line[++index];
            if (code == '&')
            {
                run.Append('&');
                continue;
            }

            if (run.Length > 0)
            {
                paragraph.Add(TextContent(run.ToString()));
                run.Clear();
            }

            paragraph.Add(code switch
            {
                'P' => new XElement(TextNs + "page-number", "1"),
                'N' => new XElement(TextNs + "page-count", "1"),
                'D' => new XElement(TextNs + "date"),
                'T' => new XElement(TextNs + "time"),
                'A' => new XElement(TextNs + "sheet-name", "???"),
                'Z' => new XElement(TextNs + "file-name", new XAttribute(TextNs + "display", "path")),
                _ => new XElement(TextNs + "file-name", new XAttribute(TextNs + "display", "name-and-extension"), "???"),
            });
        }

        if (run.Length > 0)
        {
            paragraph.Add(TextContent(run.ToString()));
        }

        return paragraph;
    }

    /// <summary>The Excel text of a style:header / style:footer (null when it shows nothing).</summary>
    public static string? Read(XElement? element)
    {
        if (element is null || (string?)element.Attribute(StyleNs + "display") == "false")
        {
            return null;
        }

        var builder = new StringBuilder();
        var regions = element.Elements().Where(child => child.Name.Namespace == StyleNs && child.Name.LocalName.StartsWith("region-", StringComparison.Ordinal)).ToList();
        if (regions.Count == 0)
        {
            var text = Text(element);
            return text.Length == 0 ? null : "&C" + text;
        }

        foreach (var (name, code) in new[] { ("region-left", "&L"), ("region-center", "&C"), ("region-right", "&R") })
        {
            if (element.Element(StyleNs + name) is { } region && Text(region) is { Length: > 0 } text)
            {
                builder.Append(code).Append(text);
            }
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    private static string Text(XElement container)
    {
        var lines = new List<string>();
        foreach (var paragraph in container.Elements(TextNs + "p"))
        {
            var builder = new StringBuilder();
            Append(paragraph, builder);
            lines.Add(builder.ToString());
        }

        while (lines.Count > 0 && lines[^1].Length == 0)
        {
            lines.RemoveAt(lines.Count - 1);
        }

        return string.Join('\n', lines);

        static void Append(XElement parent, StringBuilder builder)
        {
            foreach (var node in parent.Nodes())
            {
                switch (node)
                {
                    case XText text:
                        builder.Append(text.Value.Replace("&", "&&", StringComparison.Ordinal));
                        break;
                    case XElement child when child.Name.Namespace == TextNs:
                        switch (child.Name.LocalName)
                        {
                            case "page-number":
                                builder.Append("&P");
                                break;
                            case "page-count":
                                builder.Append("&N");
                                break;
                            case "date":
                                builder.Append("&D");
                                break;
                            case "time":
                                builder.Append("&T");
                                break;
                            case "sheet-name":
                                builder.Append("&A");
                                break;
                            case "file-name" or "title":
                                builder.Append((string?)child.Attribute(TextNs + "display") is "path" or "full" ? "&Z&F" : "&F");
                                break;
                            case "s":
                                builder.Append(' ', Math.Clamp((int?)child.Attribute(TextNs + "c") ?? 1, 1, 100));
                                break;
                            case "tab":
                                builder.Append('\t');
                                break;
                            case "line-break":
                                builder.Append('\n');
                                break;
                            default:
                                Append(child, builder);
                                break;
                        }

                        break;
                }
            }
        }
    }
}

/// <summary>Data validation rules ⇄ ODF content-validation conditions ("of:cell-content-is-whole-number() and cell-content-is-between(1;10)").</summary>
internal static class OdsValidations
{
    /// <summary>The condition of a rule ("" for any value), or null when ODF cannot express it.</summary>
    public static string? Write(DataValidation rule, Worksheet sheet)
    {
        var workbook = sheet.Workbook;
        var origin = rule.Origin;
        string? Expression(string? canonical) => canonical is null ? null : OdsFormulas.Expression(canonical, workbook, sheet, origin);
        string? Comparison(string subject, string between)
        {
            var first = Expression(rule.Formula1);
            if (first is null)
            {
                return null;
            }

            if (rule.Operator is ComparisonOperator.Between or ComparisonOperator.NotBetween)
            {
                return Expression(rule.Formula2) is { } second
                    ? $"{between}{(rule.Operator == ComparisonOperator.NotBetween ? "-is-not-between" : "-is-between")}({first};{second})"
                    : null;
            }

            return subject + Operator(rule.Operator) + first;
        }

        var condition = rule.Type switch
        {
            ValidationType.Any => "",
            ValidationType.WholeNumber => Comparison("cell-content()", "cell-content") is { } whole ? "cell-content-is-whole-number() and " + whole : null,
            ValidationType.Decimal => Comparison("cell-content()", "cell-content") is { } number ? "cell-content-is-decimal-number() and " + number : null,
            ValidationType.Date => Comparison("cell-content()", "cell-content") is { } date ? "cell-content-is-date() and " + date : null,
            ValidationType.Time => Comparison("cell-content()", "cell-content") is { } time ? "cell-content-is-time() and " + time : null,
            ValidationType.TextLength => Comparison("cell-content-text-length()", "cell-content-text-length"),
            ValidationType.List => List(rule.Formula1, Expression),
            ValidationType.Custom => Expression(rule.Formula1) is { } custom ? $"is-true-formula({custom})" : null,
            _ => null,
        };
        return condition is null ? null : condition.Length == 0 ? "" : "of:" + condition;
    }

    private static string? List(string? formula, Func<string?, string?> expression)
    {
        if (formula is null)
        {
            return null;
        }

        if (formula.Length >= 2 && formula[0] == '"' && formula[^1] == '"')
        {
            var items = formula[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal).Split(',');
            return "cell-content-is-in-list(" + string.Join(';', items.Select(item => "\"" + item.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"")) + ")";
        }

        return expression(formula) is { } range ? $"cell-content-is-in-list({range})" : null;
    }

    private static string Operator(ComparisonOperator comparison) => comparison switch
    {
        ComparisonOperator.Equal => "=",
        ComparisonOperator.NotEqual => "!=",
        ComparisonOperator.GreaterThan => ">",
        ComparisonOperator.LessThan => "<",
        ComparisonOperator.GreaterThanOrEqual => ">=",
        _ => "<=",
    };

    /// <summary>A rule of a condition (without ranges and messages), or null when ThixCalc does not know it.</summary>
    public static DataValidation? Read(string? condition)
    {
        if (string.IsNullOrWhiteSpace(condition))
        {
            return new DataValidation { Type = ValidationType.Any };
        }

        var colon = condition.IndexOf(':', StringComparison.Ordinal);
        if (colon > 0 && colon < 6 && condition[..colon] is "of" or "oooc" or "ooow")
        {
            condition = condition[(colon + 1)..];
        }

        var parts = SplitAnd(condition.Trim());
        var type = ValidationType.Any;
        DataValidation? result = null;
        foreach (var raw in parts)
        {
            var part = raw.Trim();
            switch (part.Replace(" ", "", StringComparison.Ordinal))
            {
                case "cell-content-is-whole-number()":
                    type = ValidationType.WholeNumber;
                    continue;
                case "cell-content-is-decimal-number()":
                    type = ValidationType.Decimal;
                    continue;
                case "cell-content-is-date()":
                    type = ValidationType.Date;
                    continue;
                case "cell-content-is-time()":
                    type = ValidationType.Time;
                    continue;
            }

            if (Call(part, "cell-content-is-in-list") is { } list)
            {
                var items = SplitArguments(list);
                if (items.All(item => item.Trim().StartsWith('"')))
                {
                    var values = items.Select(item => item.Trim()[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal));
                    return new DataValidation { Type = ValidationType.List, Formula1 = "\"" + string.Join(',', values) + "\"" };
                }

                return OdsFormulas.ToCanonical(list) is { } range ? new DataValidation { Type = ValidationType.List, Formula1 = range } : null;
            }

            if (Call(part, "is-true-formula") is { } formula)
            {
                return OdsFormulas.ToCanonical(formula) is { } custom ? new DataValidation { Type = ValidationType.Custom, Formula1 = custom } : null;
            }

            var length = part.StartsWith("cell-content-text-length", StringComparison.Ordinal);
            if (length)
            {
                type = ValidationType.TextLength;
            }

            result = Comparison(part, type);
            if (result is null)
            {
                return null;
            }
        }

        return result ?? (type == ValidationType.Any ? new DataValidation { Type = type } : null);
    }

    private static DataValidation? Comparison(string part, ValidationType type)
    {
        foreach (var (name, comparison) in new[] { ("is-not-between", ComparisonOperator.NotBetween), ("is-between", ComparisonOperator.Between) })
        {
            foreach (var subject in new[] { "cell-content-text-length-", "cell-content-" })
            {
                if (Call(part, subject + name) is { } arguments)
                {
                    var values = SplitArguments(arguments);
                    if (values.Count != 2 || OdsFormulas.ToCanonical(values[0]) is not { } first || OdsFormulas.ToCanonical(values[1]) is not { } second)
                    {
                        return null;
                    }

                    return new DataValidation { Type = type, Operator = comparison, Formula1 = first, Formula2 = second };
                }
            }
        }

        foreach (var subject in new[] { "cell-content-text-length()", "cell-content()" })
        {
            if (!part.StartsWith(subject, StringComparison.Ordinal))
            {
                continue;
            }

            var rest = part[subject.Length..].TrimStart();
            foreach (var (text, comparison) in new[] { ("!=", ComparisonOperator.NotEqual), ("<>", ComparisonOperator.NotEqual), (">=", ComparisonOperator.GreaterThanOrEqual),
                ("<=", ComparisonOperator.LessThanOrEqual), (">", ComparisonOperator.GreaterThan), ("<", ComparisonOperator.LessThan), ("=", ComparisonOperator.Equal) })
            {
                if (rest.StartsWith(text, StringComparison.Ordinal))
                {
                    return OdsFormulas.ToCanonical(rest[text.Length..]) is { } value ? new DataValidation { Type = type, Operator = comparison, Formula1 = value } : null;
                }
            }
        }

        return null;
    }

    /// <summary>The arguments text of "name(…)" when <paramref name="part"/> is that call, else null.</summary>
    internal static string? Call(string part, string name)
    {
        part = part.Trim();
        if (!part.StartsWith(name + "(", StringComparison.Ordinal) || !part.EndsWith(')'))
        {
            return null;
        }

        return part[(name.Length + 1)..^1];
    }

    private static List<string> SplitAnd(string condition)
    {
        var parts = new List<string>();
        var depth = 0;
        var quoted = false;
        var start = 0;
        for (var index = 0; index < condition.Length; index++)
        {
            var character = condition[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && character is '(' or '[')
            {
                depth++;
            }
            else if (!quoted && character is ')' or ']')
            {
                depth--;
            }
            else if (!quoted && depth == 0 && string.Compare(condition, index, " and ", 0, 5, StringComparison.Ordinal) == 0)
            {
                parts.Add(condition[start..index]);
                start = index + 5;
                index += 4;
            }
        }

        parts.Add(condition[start..]);
        return parts;
    }

    /// <summary>Top-level arguments separated by ";" (or "," as some writers use).</summary>
    internal static List<string> SplitArguments(string arguments)
    {
        var separator = arguments.Contains(';', StringComparison.Ordinal) ? ';' : ',';
        var parts = new List<string>();
        var depth = 0;
        var quoted = false;
        var start = 0;
        for (var index = 0; index < arguments.Length; index++)
        {
            var character = arguments[index];
            if (character == '"')
            {
                quoted = !quoted;
            }
            else if (!quoted && character is '(' or '[' or '{')
            {
                depth++;
            }
            else if (!quoted && character is ')' or ']' or '}')
            {
                depth--;
            }
            else if (!quoted && depth == 0 && character == separator)
            {
                parts.Add(arguments[start..index]);
                start = index + 1;
            }
        }

        parts.Add(arguments[start..]);
        return parts;
    }
}

/// <summary>The autofilter's conditions ⇄ table:filter (value lists as filter-set-item, custom conditions, top/bottom N).</summary>
internal static class OdsFilters
{
    public static bool CanWrite(FilterColumn column) => column.Dynamic is null;

    public static XElement? Write(AutoFilter filter)
    {
        var conditions = new List<XElement>();
        foreach (var column in filter.Columns)
        {
            if (Column(column) is { } condition)
            {
                conditions.Add(condition);
            }
        }

        return conditions.Count switch
        {
            0 => null,
            1 => new XElement(TableNs + "filter", conditions[0]),
            _ => new XElement(TableNs + "filter", new XElement(TableNs + "filter-and", conditions)),
        };
    }

    private static XElement? Column(FilterColumn column)
    {
        var field = column.ColumnOffset.ToString(CultureInfo.InvariantCulture);
        if (column.Values is { } values)
        {
            var items = values.ToList();
            if (column.IncludeBlank)
            {
                items.Add("");
            }

            return new XElement(TableNs + "filter-condition",
                new XAttribute(TableNs + "field-number", field),
                new XAttribute(TableNs + "value", items.FirstOrDefault() ?? ""),
                new XAttribute(TableNs + "operator", "="),
                items.Select(item => new XElement(TableNs + "filter-set-item", new XAttribute(TableNs + "value", item))));
        }

        if (column.Top is { } top)
        {
            return new XElement(TableNs + "filter-condition",
                new XAttribute(TableNs + "field-number", field),
                new XAttribute(TableNs + "value", Number(top)),
                new XAttribute(TableNs + "data-type", "number"),
                new XAttribute(TableNs + "operator", (column.Bottom ? "bottom " : "top ") + (column.TopPercent ? "percent" : "values")));
        }

        if (column.Custom.Count == 0)
        {
            return null;
        }

        var conditions = column.Custom.Select(custom =>
        {
            var (op, value) = Custom(custom);
            var isNumber = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out _);
            return new XElement(TableNs + "filter-condition",
                new XAttribute(TableNs + "field-number", field),
                new XAttribute(TableNs + "value", value),
                isNumber ? new XAttribute(TableNs + "data-type", "number") : null,
                new XAttribute(TableNs + "operator", op));
        }).ToList();
        return conditions.Count == 1 ? conditions[0] : new XElement(TableNs + (column.CustomAnd ? "filter-and" : "filter-or"), conditions);
    }

    private static (string Operator, string Value) Custom(CustomFilter filter)
    {
        var value = filter.Value;
        var negate = filter.Operator == FilterOperator.NotEqual;
        if (filter.Operator is FilterOperator.Equal or FilterOperator.NotEqual && value.Length > 1)
        {
            if (value.StartsWith('*') && value.EndsWith('*') && value.Length > 2)
            {
                return (negate ? "!contains" : "contains", value[1..^1]);
            }

            if (value.EndsWith('*'))
            {
                return (negate ? "!begins" : "begins", value[..^1]);
            }

            if (value.StartsWith('*'))
            {
                return (negate ? "!ends" : "ends", value[1..]);
            }
        }

        return (filter.Operator switch
        {
            FilterOperator.NotEqual => "!=",
            FilterOperator.GreaterThan => ">",
            FilterOperator.GreaterThanOrEqual => ">=",
            FilterOperator.LessThan => "<",
            FilterOperator.LessThanOrEqual => "<=",
            _ => "=",
        }, value);
    }

    /// <summary>The filtered columns of a table:filter (what ThixCalc models of it).</summary>
    public static List<FilterColumn> Read(XElement? filter)
    {
        var columns = new Dictionary<int, FilterColumn>();
        if (filter is null)
        {
            return [];
        }

        Collect(filter, and: true);
        return columns.Values.OrderBy(column => column.ColumnOffset).ToList();

        void Collect(XElement element, bool and)
        {
            foreach (var child in element.Elements())
            {
                switch (child.Name.LocalName)
                {
                    case "filter-and":
                        Collect(child, true);
                        break;
                    case "filter-or":
                        Collect(child, false);
                        break;
                    case "filter-condition":
                        Condition(child, and);
                        break;
                }
            }
        }

        void Condition(XElement condition, bool and)
        {
            if ((int?)condition.Attribute(TableNs + "field-number") is not { } field || field < 0)
            {
                return;
            }

            var op = (string?)condition.Attribute(TableNs + "operator") ?? "=";
            var value = (string?)condition.Attribute(TableNs + "value") ?? "";
            var items = condition.Elements(TableNs + "filter-set-item").Select(item => (string?)item.Attribute(TableNs + "value") ?? "").ToList();
            columns.TryGetValue(field, out var column);
            column ??= new FilterColumn { ColumnOffset = field };
            if (items.Count > 0)
            {
                columns[field] = column with { Values = items.Where(item => item.Length > 0).ToList(), IncludeBlank = items.Contains("") };
                return;
            }

            if (op.StartsWith("top ", StringComparison.Ordinal) || op.StartsWith("bottom ", StringComparison.Ordinal))
            {
                columns[field] = column with
                {
                    Top = double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var count) ? count : 10,
                    Bottom = op.StartsWith("bottom", StringComparison.Ordinal),
                    TopPercent = op.EndsWith("percent", StringComparison.Ordinal),
                };
                return;
            }

            var custom = op switch
            {
                "contains" => new CustomFilter(FilterOperator.Equal, "*" + value + "*"),
                "!contains" => new CustomFilter(FilterOperator.NotEqual, "*" + value + "*"),
                "begins" => new CustomFilter(FilterOperator.Equal, value + "*"),
                "!begins" => new CustomFilter(FilterOperator.NotEqual, value + "*"),
                "ends" => new CustomFilter(FilterOperator.Equal, "*" + value),
                "!ends" => new CustomFilter(FilterOperator.NotEqual, "*" + value),
                "!=" => new CustomFilter(FilterOperator.NotEqual, value),
                ">" => new CustomFilter(FilterOperator.GreaterThan, value),
                ">=" => new CustomFilter(FilterOperator.GreaterThanOrEqual, value),
                "<" => new CustomFilter(FilterOperator.LessThan, value),
                "<=" => new CustomFilter(FilterOperator.LessThanOrEqual, value),
                _ => new CustomFilter(FilterOperator.Equal, value),
            };
            columns[field] = column with { Custom = column.Custom.Append(custom).ToList(), CustomAnd = and && column.Custom.Count > 0 };
        }
    }
}

/// <summary>
/// Conditional formats ⇄ LibreOffice's calcext:conditional-format (cell value comparisons, formulas, text, duplicates, top/bottom,
/// averages, errors), the look as a named cell style. Colour scales, data bars and icon sets are not written (the save names them).
/// </summary>
internal static class OdsConditionalFormats
{
    public static bool CanWrite(ConditionalFormatRule rule) => rule.FormatId is not null && rule.Type is ConditionalFormatType.CellIs or ConditionalFormatType.Expression
        or ConditionalFormatType.ContainsText or ConditionalFormatType.NotContainsText or ConditionalFormatType.BeginsWith or ConditionalFormatType.EndsWith
        or ConditionalFormatType.DuplicateValues or ConditionalFormatType.UniqueValues or ConditionalFormatType.ContainsErrors or ConditionalFormatType.NotContainsErrors
        or ConditionalFormatType.Top10 or ConditionalFormatType.AboveAverage;

    public static XElement? Write(ConditionalFormat format, Worksheet sheet, Func<DifferentialFormat, string> styleName)
    {
        var workbook = sheet.Workbook;
        var origin = format.Origin;
        var element = new XElement(CalcExtNs + "conditional-format",
            new XAttribute(CalcExtNs + "target-range-address", string.Join(' ', format.Ranges.Select(range => OdsFormulas.RangeAddress(sheet, range)))));
        foreach (var rule in format.Rules.OrderBy(rule => rule.Priority))
        {
            if (!CanWrite(rule) || rule.FormatId is not { } formatId || formatId < 0 || formatId >= workbook.Styles.DifferentialFormats.Count
                || Value(rule, workbook, sheet, origin) is not { } value)
            {
                continue;
            }

            element.Add(new XElement(CalcExtNs + "condition",
                new XAttribute(CalcExtNs + "apply-style-name", styleName(workbook.Styles.DifferentialFormats[formatId])),
                new XAttribute(CalcExtNs + "value", value),
                new XAttribute(CalcExtNs + "base-cell-address", OdsFormulas.RangeAddress(sheet, CellRange.Cell(origin)))));
        }

        return element.HasElements ? element : null;
    }

    private static string? Value(ConditionalFormatRule rule, Workbook workbook, Worksheet sheet, CellAddress origin)
    {
        string? Formula(int index) => index < rule.Formulas.Count ? OdsFormulas.Expression(rule.Formulas[index], workbook, sheet, origin) : null;
        string Quoted(string? text) => "\"" + (text ?? "").Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        return rule.Type switch
        {
            ConditionalFormatType.CellIs => rule.Operator switch
            {
                ComparisonOperator.Between => Formula(0) is { } low && Formula(1) is { } high ? $"between({low},{high})" : null,
                ComparisonOperator.NotBetween => Formula(0) is { } low && Formula(1) is { } high ? $"not-between({low},{high})" : null,
                _ => Formula(0) is { } value ? rule.Operator switch
                {
                    ComparisonOperator.Equal => "=",
                    ComparisonOperator.NotEqual => "!=",
                    ComparisonOperator.GreaterThan => ">",
                    ComparisonOperator.LessThan => "<",
                    ComparisonOperator.GreaterThanOrEqual => ">=",
                    _ => "<=",
                } + value : null,
            },
            ConditionalFormatType.Expression => Formula(0) is { } expression ? $"formula-is({expression})" : null,
            ConditionalFormatType.ContainsText => $"contains-text({Quoted(rule.Text)})",
            ConditionalFormatType.NotContainsText => $"not-contains-text({Quoted(rule.Text)})",
            ConditionalFormatType.BeginsWith => $"begins-with({Quoted(rule.Text)})",
            ConditionalFormatType.EndsWith => $"ends-with({Quoted(rule.Text)})",
            ConditionalFormatType.DuplicateValues => "duplicate",
            ConditionalFormatType.UniqueValues => "unique",
            ConditionalFormatType.ContainsErrors => "is-error",
            ConditionalFormatType.NotContainsErrors => "is-no-error",
            ConditionalFormatType.Top10 => $"{(rule.Bottom ? "bottom" : "top")}-{(rule.Percent ? "percent" : "elements")}({rule.Rank.ToString(CultureInfo.InvariantCulture)})",
            ConditionalFormatType.AboveAverage => (rule.AboveAverage ? "above" : "below") + (rule.EqualAverage ? "-equal" : "") + "-average",
            _ => null,
        };
    }

    /// <summary>A rule of a calcext:condition value (its format id set by the caller), or null.</summary>
    public static ConditionalFormatRule? Read(string value, int priority)
    {
        value = value.Trim();
        var rule = new ConditionalFormatRule { Priority = priority };
        string? Text(string name) => OdsValidations.Call(value, name) is { } argument && argument.Trim() is { Length: >= 2 } quoted && quoted[0] == '"' && quoted[^1] == '"'
            ? quoted[1..^1].Replace("\"\"", "\"", StringComparison.Ordinal)
            : null;
        List<string>? Formulas(string arguments)
        {
            var parts = OdsValidations.SplitArguments(arguments).Select(OdsFormulas.ToCanonical).ToList();
            return parts.Any(part => part is null) ? null : parts.Select(part => part!).ToList();
        }

        foreach (var (name, comparison) in new[] { ("not-between", ComparisonOperator.NotBetween), ("between", ComparisonOperator.Between) })
        {
            if (OdsValidations.Call(value, name) is { } arguments)
            {
                return Formulas(arguments) is { Count: 2 } bounds ? rule with { Type = ConditionalFormatType.CellIs, Operator = comparison, Formulas = bounds } : null;
            }
        }

        if (OdsValidations.Call(value, "formula-is") is { } formula)
        {
            return OdsFormulas.ToCanonical(formula) is { } expression ? rule with { Type = ConditionalFormatType.Expression, Formulas = [expression] } : null;
        }

        foreach (var (name, type) in new[] { ("not-contains-text", ConditionalFormatType.NotContainsText), ("contains-text", ConditionalFormatType.ContainsText),
            ("begins-with", ConditionalFormatType.BeginsWith), ("ends-with", ConditionalFormatType.EndsWith) })
        {
            if (Text(name) is { } text)
            {
                return rule with { Type = type, Text = text };
            }
        }

        foreach (var (name, bottom, percent) in new[] { ("top-elements", false, false), ("bottom-elements", true, false), ("top-percent", false, true), ("bottom-percent", true, true) })
        {
            if (OdsValidations.Call(value, name) is { } count)
            {
                return rule with { Type = ConditionalFormatType.Top10, Bottom = bottom, Percent = percent, Rank = int.TryParse(count.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out var rank) ? rank : 10 };
            }
        }

        switch (value)
        {
            case "duplicate":
                return rule with { Type = ConditionalFormatType.DuplicateValues };
            case "unique":
                return rule with { Type = ConditionalFormatType.UniqueValues };
            case "is-error":
                return rule with { Type = ConditionalFormatType.ContainsErrors };
            case "is-no-error":
                return rule with { Type = ConditionalFormatType.NotContainsErrors };
            case "above-average" or "below-average" or "above-equal-average" or "below-equal-average":
                return rule with { Type = ConditionalFormatType.AboveAverage, AboveAverage = value.StartsWith("above", StringComparison.Ordinal), EqualAverage = value.Contains("equal", StringComparison.Ordinal) };
        }

        foreach (var (text, comparison) in new[] { ("!=", ComparisonOperator.NotEqual), ("<>", ComparisonOperator.NotEqual), (">=", ComparisonOperator.GreaterThanOrEqual),
            ("<=", ComparisonOperator.LessThanOrEqual), (">", ComparisonOperator.GreaterThan), ("<", ComparisonOperator.LessThan), ("=", ComparisonOperator.Equal) })
        {
            if (value.StartsWith(text, StringComparison.Ordinal))
            {
                return OdsFormulas.ToCanonical(value[text.Length..]) is { } operand ? rule with { Type = ConditionalFormatType.CellIs, Operator = comparison, Formulas = [operand] } : null;
            }
        }

        return null;
    }

    /// <summary>The look of a named cell style as a differential format.</summary>
    public static DifferentialFormat ReadFormat(XElement style, Func<XElement, string?> dataStyleCode)
    {
        var font = ThixOffice.Core.Formats.Odf.OdfFont.Read(style.Element(StyleNs + "text-properties"));
        var cell = style.Element(StyleNs + "table-cell-properties");
        var border = (string?)cell?.Attribute(FoNs + "border") ?? (string?)cell?.Attribute(FoNs + "border-bottom");
        var edge = border is null ? (BorderEdge?)null : OdsCellStyles.ReadBorder(border);
        return new DifferentialFormat
        {
            NumberFormat = dataStyleCode(style),
            Bold = font.Bold,
            Italic = font.Italic,
            Strikethrough = font.Strikethrough,
            Underline = font.Underline is null ? null : font.Underline == "none" ? UnderlineKind.None : font.UnderlineDouble ? UnderlineKind.Double : UnderlineKind.Single,
            FontColor = font.Color is { } color ? CalcColor.FromHex(color) : null,
            FillColor = ColorOf((string?)cell?.Attribute(FoNs + "background-color")) is { } fill ? CalcColor.FromHex(fill) : null,
            BorderStyle = edge is { IsVisible: true } visible ? visible.Style : null,
            BorderColor = edge is { IsVisible: true } colored ? colored.Color ?? CalcColor.FromHex("000000") : null,
        };
    }
}
