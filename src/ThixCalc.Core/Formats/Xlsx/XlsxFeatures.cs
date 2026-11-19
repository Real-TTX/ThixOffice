using System.Globalization;
using System.Xml;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ValidationType = ThixCalc.Core.Model.ValidationType;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>Conditional formats, data validation, autofilter and sort state ↔ SpreadsheetML.</summary>
internal static class XlsxFeatures
{
    private static readonly string[] Operators = ["between", "notBetween", "equal", "notEqual", "greaterThan", "lessThan", "greaterThanOrEqual", "lessThanOrEqual"];

    // ---------------------------------------------------------------- reading

    public static ConditionalFormat? ReadConditionalFormat(X.ConditionalFormatting formatting, ReadContext context)
    {
        var ranges = CellRange.ParseList(formatting.SequenceOfReferences?.InnerText ?? string.Empty);
        if (ranges.Count == 0)
        {
            return null;
        }

        var origin = ranges[0].First;
        var rules = new List<ConditionalFormatRule>();
        foreach (var rule in formatting.Elements<X.ConditionalFormattingRule>())
        {
            var type = rule.Type?.InnerText ?? "expression";
            var model = new ConditionalFormatRule
            {
                Type = ParseType(type),
                Priority = rule.Priority?.Value ?? 1,
                StopIfTrue = rule.StopIfTrue?.Value ?? false,
                Operator = ParseOperator(rule.Operator?.InnerText),
                Formulas = rule.Elements<X.Formula>().Select(formula => Canonical(formula.Text, context.Workbook, origin)).ToList(),
                FormatId = rule.FormatId?.Value is { } id ? (int)id : null,
                Text = rule.Text?.Value,
                TimePeriod = rule.TimePeriod?.InnerText,
                Rank = (int)(rule.Rank?.Value ?? 10),
                Percent = rule.Percent?.Value ?? false,
                Bottom = rule.Bottom?.Value ?? false,
                AboveAverage = rule.AboveAverage?.Value ?? true,
                EqualAverage = rule.EqualAverage?.Value ?? false,
                StdDev = rule.StdDev?.Value ?? 0,
                Xml = rule.OuterXml,
            };
            if (rule.GetFirstChild<X.ColorScale>() is { } scale)
            {
                model = model with
                {
                    Thresholds = scale.Elements<X.ConditionalFormatValueObject>().Select(ReadThreshold).ToList(),
                    Colors = scale.Elements<X.Color>().Select(color => XlsxStyles.ReadColor(color) ?? CalcColor.Automatic).ToList(),
                };
            }
            else if (rule.GetFirstChild<X.DataBar>() is { } bar)
            {
                model = model with
                {
                    Thresholds = bar.Elements<X.ConditionalFormatValueObject>().Select(ReadThreshold).ToList(),
                    Colors = bar.Elements<X.Color>().Select(color => XlsxStyles.ReadColor(color) ?? CalcColor.Automatic).ToList(),
                    ShowValue = bar.ShowValue?.Value ?? true,
                };
            }
            else if (rule.GetFirstChild<X.IconSet>() is { } icons)
            {
                model = model with
                {
                    Thresholds = icons.Elements<X.ConditionalFormatValueObject>().Select(ReadThreshold).ToList(),
                    IconSet = icons.IconSetValue?.InnerText ?? "3TrafficLights1",
                    ShowValue = icons.ShowValue?.Value ?? true,
                    Reverse = icons.Reverse?.Value ?? false,
                };
            }

            rules.Add(model);
        }

        return new ConditionalFormat(ranges, rules);
    }

    private static ConditionalValue ReadThreshold(X.ConditionalFormatValueObject value) => new(
        value.Type?.InnerText switch
        {
            "percent" => ConditionalValueType.Percent,
            "max" => ConditionalValueType.Max,
            "min" => ConditionalValueType.Min,
            "formula" => ConditionalValueType.Formula,
            "percentile" => ConditionalValueType.Percentile,
            "autoMin" => ConditionalValueType.AutoMin,
            "autoMax" => ConditionalValueType.AutoMax,
            _ => ConditionalValueType.Number,
        },
        value.Val?.Value,
        value.GreaterThanOrEqual?.Value ?? true);

    private static ConditionalFormatType ParseType(string type) => type switch
    {
        "cellIs" => ConditionalFormatType.CellIs,
        "colorScale" => ConditionalFormatType.ColorScale,
        "dataBar" => ConditionalFormatType.DataBar,
        "iconSet" => ConditionalFormatType.IconSet,
        "top10" => ConditionalFormatType.Top10,
        "uniqueValues" => ConditionalFormatType.UniqueValues,
        "duplicateValues" => ConditionalFormatType.DuplicateValues,
        "containsText" => ConditionalFormatType.ContainsText,
        "notContainsText" => ConditionalFormatType.NotContainsText,
        "beginsWith" => ConditionalFormatType.BeginsWith,
        "endsWith" => ConditionalFormatType.EndsWith,
        "containsBlanks" => ConditionalFormatType.ContainsBlanks,
        "notContainsBlanks" => ConditionalFormatType.NotContainsBlanks,
        "containsErrors" => ConditionalFormatType.ContainsErrors,
        "notContainsErrors" => ConditionalFormatType.NotContainsErrors,
        "timePeriod" => ConditionalFormatType.TimePeriod,
        "aboveAverage" => ConditionalFormatType.AboveAverage,
        _ => ConditionalFormatType.Expression,
    };

    private static string TypeName(ConditionalFormatType type)
    {
        var name = type.ToString();
        return type == ConditionalFormatType.Top10 ? "top10" : char.ToLowerInvariant(name[0]) + name[1..];
    }

    private static ComparisonOperator ParseOperator(string? name)
    {
        var index = Array.IndexOf(Operators, name);
        return index < 0 ? ComparisonOperator.Between : (ComparisonOperator)index;
    }

    /// <summary>A file formula (maybe with _xlfn.) as canonical text for <paramref name="origin"/>.</summary>
    internal static string Canonical(string text, Workbook workbook, CellAddress origin)
    {
        try
        {
            return FormulaFormatter.Format(FormulaParser.ParseNode(text, workbook, origin), workbook, origin);
        }
        catch (FormulaParseException)
        {
            return text;
        }
    }

    /// <summary>Canonical text as the file writes it (_xlfn. prefixes).</summary>
    internal static string FileText(string text, Workbook workbook, CellAddress origin)
    {
        try
        {
            return FormulaFormatter.Format(FormulaParser.ParseNode(text, workbook, origin), workbook, origin, FormulaSyntax.FileFormat);
        }
        catch (FormulaParseException)
        {
            return text;
        }
    }

    public static DataValidation ReadValidation(X.DataValidation validation, Worksheet sheet)
    {
        var ranges = CellRange.ParseList(validation.SequenceOfReferences?.InnerText ?? string.Empty);
        var origin = ranges.Count > 0 ? ranges[0].First : default;
        return new DataValidation
        {
            Ranges = ranges,
            Type = validation.Type?.InnerText switch
            {
                "whole" => ValidationType.WholeNumber,
                "decimal" => ValidationType.Decimal,
                "list" => ValidationType.List,
                "date" => ValidationType.Date,
                "time" => ValidationType.Time,
                "textLength" => ValidationType.TextLength,
                "custom" => ValidationType.Custom,
                _ => ValidationType.Any,
            },
            Operator = ParseOperator(validation.Operator?.InnerText),
            Formula1 = validation.Formula1?.Text is { } first ? Canonical(first, sheet.Workbook, origin) : null,
            Formula2 = validation.Formula2?.Text is { } second ? Canonical(second, sheet.Workbook, origin) : null,
            AllowBlank = validation.AllowBlank?.Value ?? false,
            ShowDropDown = !(validation.ShowDropDown?.Value ?? false),
            ShowInputMessage = validation.ShowInputMessage?.Value ?? false,
            ShowErrorMessage = validation.ShowErrorMessage?.Value ?? false,
            ErrorStyle = validation.ErrorStyle?.InnerText switch
            {
                "warning" => ValidationErrorStyle.Warning,
                "information" => ValidationErrorStyle.Information,
                _ => ValidationErrorStyle.Stop,
            },
            ErrorTitle = validation.ErrorTitle?.Value,
            Error = validation.Error?.Value,
            PromptTitle = validation.PromptTitle?.Value,
            Prompt = validation.Prompt?.Value,
        };
    }

    public static AutoFilter? ReadAutoFilter(X.AutoFilter filter)
    {
        if (filter.Reference?.Value is not { } reference || CellRange.TryParse(reference) is not { } range)
        {
            return null;
        }

        var columns = new List<FilterColumn>();
        foreach (var column in filter.Elements<X.FilterColumn>())
        {
            var model = new FilterColumn
            {
                ColumnOffset = (int)(column.ColumnId?.Value ?? 0),
                HiddenButton = column.HiddenButton?.Value ?? false,
                Xml = column.OuterXml,
            };
            if (column.GetFirstChild<X.Filters>() is { } filters)
            {
                model = model with
                {
                    Values = filters.Elements<X.Filter>().Select(value => value.Val?.Value ?? string.Empty).ToList(),
                    IncludeBlank = filters.Blank?.Value ?? false,
                };
            }

            if (column.GetFirstChild<X.CustomFilters>() is { } custom)
            {
                model = model with
                {
                    CustomAnd = custom.And?.Value ?? false,
                    Custom = custom.Elements<X.CustomFilter>().Select(condition => new CustomFilter(
                        condition.Operator?.InnerText switch
                        {
                            "notEqual" => FilterOperator.NotEqual,
                            "greaterThan" => FilterOperator.GreaterThan,
                            "greaterThanOrEqual" => FilterOperator.GreaterThanOrEqual,
                            "lessThan" => FilterOperator.LessThan,
                            "lessThanOrEqual" => FilterOperator.LessThanOrEqual,
                            _ => FilterOperator.Equal,
                        },
                        condition.Val?.Value ?? string.Empty)).ToList(),
                };
            }

            if (column.GetFirstChild<X.Top10>() is { } top)
            {
                model = model with { Top = top.Val?.Value ?? 10, TopPercent = top.Percent?.Value ?? false, Bottom = !(top.Top?.Value ?? true) };
            }

            if (column.GetFirstChild<X.DynamicFilter>() is { } dynamic)
            {
                model = model with { Dynamic = dynamic.Type?.InnerText };
            }

            columns.Add(model);
        }

        return new AutoFilter(range, columns) { Sort = filter.GetFirstChild<X.SortState>() is { } sort ? ReadSortState(sort) : null };
    }

    public static SortState? ReadSortState(X.SortState sort)
    {
        if (sort.Reference?.Value is not { } reference || CellRange.TryParse(reference) is not { } range)
        {
            return null;
        }

        var conditions = sort.Elements<X.SortCondition>()
            .Where(condition => condition.Reference?.Value is not null && CellRange.TryParse(condition.Reference.Value) is not null)
            .Select(condition => new SortCondition(CellRange.Parse(condition.Reference!.Value!), condition.Descending?.Value ?? false, condition.CustomList?.Value))
            .ToList();
        return new SortState(range, conditions, sort.CaseSensitive?.Value ?? false, sort.ColumnSort?.Value ?? false);
    }

    // ---------------------------------------------------------------- writing

    public static void WriteConditionalFormats(XmlWriter writer, Worksheet sheet)
    {
        foreach (var format in sheet.ConditionalFormats)
        {
            writer.WriteStartElement("conditionalFormatting", XlsxNamespaces.Main);
            writer.WriteAttributeString("sqref", CellRange.FormatList(format.Ranges));
            foreach (var rule in format.Rules)
            {
                if (rule.Xml is { } xml)
                {
                    writer.WriteRaw(xml);
                    continue;
                }

                WriteRule(writer, rule, sheet.Workbook, format.Origin);
            }

            writer.WriteEndElement();
        }
    }

    private static void WriteRule(XmlWriter writer, ConditionalFormatRule rule, Workbook workbook, CellAddress origin)
    {
        writer.WriteStartElement("cfRule", XlsxNamespaces.Main);
        writer.WriteAttributeString("type", TypeName(rule.Type));
        if (rule.FormatId is { } id && rule.Type is not (ConditionalFormatType.ColorScale or ConditionalFormatType.DataBar or ConditionalFormatType.IconSet))
        {
            writer.WriteAttributeString("dxfId", id.ToString(CultureInfo.InvariantCulture));
        }

        writer.WriteAttributeString("priority", rule.Priority.ToString(CultureInfo.InvariantCulture));
        if (rule.StopIfTrue)
        {
            writer.WriteAttributeString("stopIfTrue", "1");
        }

        switch (rule.Type)
        {
            case ConditionalFormatType.CellIs:
                writer.WriteAttributeString("operator", Operators[(int)rule.Operator]);
                break;
            case ConditionalFormatType.ContainsText or ConditionalFormatType.NotContainsText or ConditionalFormatType.BeginsWith or ConditionalFormatType.EndsWith:
                writer.WriteAttributeString("operator", rule.Type switch
                {
                    ConditionalFormatType.ContainsText => "containsText",
                    ConditionalFormatType.NotContainsText => "notContains",
                    ConditionalFormatType.BeginsWith => "beginsWith",
                    _ => "endsWith",
                });
                writer.WriteAttributeString("text", rule.Text ?? string.Empty);
                break;
            case ConditionalFormatType.TimePeriod:
                writer.WriteAttributeString("timePeriod", rule.TimePeriod ?? "today");
                break;
            case ConditionalFormatType.Top10:
                if (rule.Percent)
                {
                    writer.WriteAttributeString("percent", "1");
                }

                if (rule.Bottom)
                {
                    writer.WriteAttributeString("bottom", "1");
                }

                writer.WriteAttributeString("rank", rule.Rank.ToString(CultureInfo.InvariantCulture));
                break;
            case ConditionalFormatType.AboveAverage:
                if (!rule.AboveAverage)
                {
                    writer.WriteAttributeString("aboveAverage", "0");
                }

                if (rule.EqualAverage)
                {
                    writer.WriteAttributeString("equalAverage", "1");
                }

                if (rule.StdDev != 0)
                {
                    writer.WriteAttributeString("stdDev", rule.StdDev.ToString(CultureInfo.InvariantCulture));
                }

                break;
        }

        switch (rule.Type)
        {
            case ConditionalFormatType.ColorScale:
                writer.WriteStartElement("colorScale", XlsxNamespaces.Main);
                WriteThresholds(writer, rule.Thresholds);
                WriteColors(writer, rule.Colors);
                writer.WriteEndElement();
                break;
            case ConditionalFormatType.DataBar:
                writer.WriteStartElement("dataBar", XlsxNamespaces.Main);
                if (!rule.ShowValue)
                {
                    writer.WriteAttributeString("showValue", "0");
                }

                WriteThresholds(writer, rule.Thresholds);
                WriteColors(writer, rule.Colors.Take(1).ToList());
                writer.WriteEndElement();
                break;
            case ConditionalFormatType.IconSet:
                writer.WriteStartElement("iconSet", XlsxNamespaces.Main);
                writer.WriteAttributeString("iconSet", rule.IconSet ?? "3TrafficLights1");
                if (!rule.ShowValue)
                {
                    writer.WriteAttributeString("showValue", "0");
                }

                if (rule.Reverse)
                {
                    writer.WriteAttributeString("reverse", "1");
                }

                WriteThresholds(writer, rule.Thresholds);
                writer.WriteEndElement();
                break;
        }

        foreach (var formula in rule.Formulas.Take(3))
        {
            writer.WriteElementString("formula", XlsxNamespaces.Main, FileText(formula, workbook, origin));
        }

        writer.WriteEndElement();
    }

    private static void WriteThresholds(XmlWriter writer, IReadOnlyList<ConditionalValue> thresholds)
    {
        foreach (var threshold in thresholds)
        {
            writer.WriteStartElement("cfvo", XlsxNamespaces.Main);
            var name = threshold.Type.ToString();
            writer.WriteAttributeString("type", char.ToLowerInvariant(name[0]) + name[1..]);
            if (threshold.Value is { } value)
            {
                writer.WriteAttributeString("val", value);
            }

            if (!threshold.GreaterOrEqual)
            {
                writer.WriteAttributeString("gte", "0");
            }

            writer.WriteEndElement();
        }
    }

    private static void WriteColors(XmlWriter writer, IReadOnlyList<CalcColor> colors)
    {
        foreach (var color in colors)
        {
            WriteColor(writer, "color", color);
        }
    }

    internal static void WriteColor(XmlWriter writer, string element, CalcColor color)
    {
        writer.WriteStartElement(element, XlsxNamespaces.Main);
        switch (color.Kind)
        {
            case ColorKind.Rgb:
                writer.WriteAttributeString("rgb", color.ToHex());
                break;
            case ColorKind.Theme:
                writer.WriteAttributeString("theme", color.Index.ToString(CultureInfo.InvariantCulture));
                break;
            case ColorKind.Indexed:
                writer.WriteAttributeString("indexed", color.Index.ToString(CultureInfo.InvariantCulture));
                break;
            default:
                writer.WriteAttributeString("auto", "1");
                break;
        }

        if (color.Tint != 0)
        {
            writer.WriteAttributeString("tint", color.Tint.ToString("R", CultureInfo.InvariantCulture));
        }

        writer.WriteEndElement();
    }

    public static void WriteValidations(XmlWriter writer, Worksheet sheet)
    {
        if (sheet.Validations.Count == 0)
        {
            return;
        }

        writer.WriteStartElement("dataValidations", XlsxNamespaces.Main);
        writer.WriteAttributeString("count", sheet.Validations.Count.ToString(CultureInfo.InvariantCulture));
        foreach (var validation in sheet.Validations)
        {
            writer.WriteStartElement("dataValidation", XlsxNamespaces.Main);
            if (validation.Type != ValidationType.Any)
            {
                writer.WriteAttributeString("type", validation.Type switch
                {
                    ValidationType.WholeNumber => "whole",
                    ValidationType.TextLength => "textLength",
                    _ => validation.Type.ToString().ToLowerInvariant(),
                });
            }

            if (validation.ErrorStyle != ValidationErrorStyle.Stop)
            {
                writer.WriteAttributeString("errorStyle", validation.ErrorStyle.ToString().ToLowerInvariant());
            }

            if (validation.Operator != ComparisonOperator.Between && validation.Type is not (ValidationType.List or ValidationType.Custom or ValidationType.Any))
            {
                writer.WriteAttributeString("operator", Operators[(int)validation.Operator]);
            }

            WriteFlag(writer, "allowBlank", validation.AllowBlank);
            WriteFlag(writer, "showDropDown", !validation.ShowDropDown);
            WriteFlag(writer, "showInputMessage", validation.ShowInputMessage);
            WriteFlag(writer, "showErrorMessage", validation.ShowErrorMessage);
            WriteOptional(writer, "errorTitle", validation.ErrorTitle);
            WriteOptional(writer, "error", validation.Error);
            WriteOptional(writer, "promptTitle", validation.PromptTitle);
            WriteOptional(writer, "prompt", validation.Prompt);
            writer.WriteAttributeString("sqref", CellRange.FormatList(validation.Ranges));
            if (validation.Formula1 is { } first)
            {
                writer.WriteElementString("formula1", XlsxNamespaces.Main, FileText(first, sheet.Workbook, validation.Origin));
            }

            if (validation.Formula2 is { } second)
            {
                writer.WriteElementString("formula2", XlsxNamespaces.Main, FileText(second, sheet.Workbook, validation.Origin));
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    public static void WriteAutoFilter(XmlWriter writer, AutoFilter filter)
    {
        writer.WriteStartElement("autoFilter", XlsxNamespaces.Main);
        writer.WriteAttributeString("ref", filter.Range.ToString());
        foreach (var column in filter.Columns)
        {
            if (column.Xml is { } xml)
            {
                writer.WriteRaw(xml);
                continue;
            }

            writer.WriteStartElement("filterColumn", XlsxNamespaces.Main);
            writer.WriteAttributeString("colId", column.ColumnOffset.ToString(CultureInfo.InvariantCulture));
            if (column.HiddenButton)
            {
                writer.WriteAttributeString("hiddenButton", "1");
            }

            if (column.Values is { } values)
            {
                writer.WriteStartElement("filters", XlsxNamespaces.Main);
                if (column.IncludeBlank)
                {
                    writer.WriteAttributeString("blank", "1");
                }

                foreach (var value in values)
                {
                    writer.WriteStartElement("filter", XlsxNamespaces.Main);
                    writer.WriteAttributeString("val", value);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
            else if (column.Custom.Count > 0)
            {
                writer.WriteStartElement("customFilters", XlsxNamespaces.Main);
                if (column.CustomAnd)
                {
                    writer.WriteAttributeString("and", "1");
                }

                foreach (var condition in column.Custom)
                {
                    writer.WriteStartElement("customFilter", XlsxNamespaces.Main);
                    if (condition.Operator != FilterOperator.Equal)
                    {
                        var name = condition.Operator.ToString();
                        writer.WriteAttributeString("operator", char.ToLowerInvariant(name[0]) + name[1..]);
                    }

                    writer.WriteAttributeString("val", condition.Value);
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }
            else if (column.Top is { } top)
            {
                writer.WriteStartElement("top10", XlsxNamespaces.Main);
                if (column.Bottom)
                {
                    writer.WriteAttributeString("top", "0");
                }

                if (column.TopPercent)
                {
                    writer.WriteAttributeString("percent", "1");
                }

                writer.WriteAttributeString("val", top.ToString("R", CultureInfo.InvariantCulture));
                writer.WriteEndElement();
            }
            else if (column.Dynamic is { } dynamic)
            {
                writer.WriteStartElement("dynamicFilter", XlsxNamespaces.Main);
                writer.WriteAttributeString("type", dynamic);
                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        if (filter.Sort is { } sort)
        {
            WriteSortState(writer, sort);
        }

        writer.WriteEndElement();
    }

    public static void WriteSortState(XmlWriter writer, SortState sort)
    {
        writer.WriteStartElement("sortState", XlsxNamespaces.Main);
        if (sort.ByColumns)
        {
            writer.WriteAttributeString("columnSort", "1");
        }

        if (sort.CaseSensitive)
        {
            writer.WriteAttributeString("caseSensitive", "1");
        }

        writer.WriteAttributeString("ref", sort.Range.ToString());
        foreach (var condition in sort.Conditions)
        {
            writer.WriteStartElement("sortCondition", XlsxNamespaces.Main);
            if (condition.Descending)
            {
                writer.WriteAttributeString("descending", "1");
            }

            writer.WriteAttributeString("ref", condition.Range.ToString());
            WriteOptional(writer, "customList", condition.CustomList);
            writer.WriteEndElement();
        }

        writer.WriteEndElement();
    }

    internal static void WriteFlag(XmlWriter writer, string name, bool value)
    {
        if (value)
        {
            writer.WriteAttributeString(name, "1");
        }
    }

    internal static void WriteOptional(XmlWriter writer, string name, string? value)
    {
        if (value is not null)
        {
            writer.WriteAttributeString(name, value);
        }
    }
}

internal static class XlsxNamespaces
{
    public const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";
    public const string Relationships = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public const string MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
}
