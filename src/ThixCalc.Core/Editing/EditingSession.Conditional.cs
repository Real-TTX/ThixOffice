using ThixCalc.Core.Model;

namespace ThixCalc.Core.Editing;

/// <summary>The quick rules of Start › Bedingte Formatierung (Excel's "Regeln zum Hervorheben von Zellen" and "Obere/untere Regeln").</summary>
public enum HighlightRule : byte
{
    GreaterThan,
    LessThan,
    Between,
    EqualTo,
    TextContains,
    Duplicates,
    Top10,
    Bottom10,
    AboveAverage,
    BelowAverage,
}

public sealed partial class EditingSession
{
    /// <summary>
    /// A quick highlight rule on the selection with its look (fill and font colour): Größer als 100, Zwischen 1 und 5, Textinhalt
    /// „offen“, Doppelte Werte, Obere 10 … <paramref name="first"/>/<paramref name="second"/> are values as typed (numbers in the
    /// locale, a text for Textinhalt) or a reference "=$B$1". One undo step.
    /// </summary>
    public void AddHighlightRule(HighlightRule kind, string? first, string? second, CalcColor fill, CalcColor font)
    {
        string Operand(string? text)
        {
            text = (text ?? string.Empty).Trim();
            if (text.StartsWith('='))
            {
                return text[1..];
            }

            return Formatting.ValueParser.TryParseNumber(text, Locale, Workbook.Date1904, out var number)
                ? number.ToString("R", System.Globalization.CultureInfo.InvariantCulture)
                : "\"" + text.Replace("\"", "\"\"", StringComparison.Ordinal) + "\"";
        }

        var origin = Selection.First;
        var rule = kind switch
        {
            HighlightRule.GreaterThan => new ConditionalFormatRule { Type = ConditionalFormatType.CellIs, Operator = ComparisonOperator.GreaterThan, Formulas = [Operand(first)] },
            HighlightRule.LessThan => new ConditionalFormatRule { Type = ConditionalFormatType.CellIs, Operator = ComparisonOperator.LessThan, Formulas = [Operand(first)] },
            HighlightRule.Between => new ConditionalFormatRule { Type = ConditionalFormatType.CellIs, Operator = ComparisonOperator.Between, Formulas = [Operand(first), Operand(second)] },
            HighlightRule.EqualTo => new ConditionalFormatRule { Type = ConditionalFormatType.CellIs, Operator = ComparisonOperator.Equal, Formulas = [Operand(first)] },
            HighlightRule.TextContains => new ConditionalFormatRule
            {
                Type = ConditionalFormatType.ContainsText,
                Text = first ?? string.Empty,
                Formulas = [$"NOT(ISERROR(SEARCH(\"{(first ?? string.Empty).Replace("\"", "\"\"", StringComparison.Ordinal)}\",{Model.A1.ColumnName(origin.Column)}{origin.Row + 1})))"],
            },
            HighlightRule.Duplicates => new ConditionalFormatRule { Type = ConditionalFormatType.DuplicateValues },
            HighlightRule.Top10 => new ConditionalFormatRule { Type = ConditionalFormatType.Top10, Rank = ParseRank(first) },
            HighlightRule.Bottom10 => new ConditionalFormatRule { Type = ConditionalFormatType.Top10, Rank = ParseRank(first), Bottom = true },
            HighlightRule.AboveAverage => new ConditionalFormatRule { Type = ConditionalFormatType.AboveAverage, AboveAverage = true },
            _ => new ConditionalFormatRule { Type = ConditionalFormatType.AboveAverage, AboveAverage = false },
        };

        var format = new DifferentialFormat { FillColor = fill, FontColor = font };
        AddRule(rule, format, "Bedingte Formatierung");
    }

    private static int ParseRank(string? text) => int.TryParse(text, out var rank) && rank is > 0 and <= 1000 ? rank : 10;

    /// <summary>Datenbalken in one colour over the selection (from the smallest to the largest value).</summary>
    public void AddDataBar(CalcColor color) => AddRule(new ConditionalFormatRule
    {
        Type = ConditionalFormatType.DataBar,
        Thresholds = [new ConditionalValue(ConditionalValueType.Min), new ConditionalValue(ConditionalValueType.Max)],
        Colors = [color],
    }, null, "Datenbalken");

    /// <summary>Farbskala of two or three colours (low, middle at the 50th percentile, high).</summary>
    public void AddColorScale(CalcColor low, CalcColor? middle, CalcColor high) => AddRule(new ConditionalFormatRule
    {
        Type = ConditionalFormatType.ColorScale,
        Thresholds = middle is null
            ? [new ConditionalValue(ConditionalValueType.Min), new ConditionalValue(ConditionalValueType.Max)]
            : [new ConditionalValue(ConditionalValueType.Min), new ConditionalValue(ConditionalValueType.Percentile, "50"), new ConditionalValue(ConditionalValueType.Max)],
        Colors = middle is { } center ? [low, center, high] : [low, high],
    }, null, "Farbskala");

    private void AddRule(ConditionalFormatRule rule, DifferentialFormat? format, string label)
    {
        var sheet = ActiveSheet;
        var ranges = SelectedRanges.ToList();
        var priority = sheet.ConditionalFormats.SelectMany(existing => existing.Rules).Select(existing => existing.Priority).DefaultIfEmpty(0).Max() + 1;
        Execute(label, () => ChangeSheetState(sheet, () =>
        {
            int? formatId = null;
            if (format is not null)
            {
                var formats = Workbook.Styles.DifferentialFormats;
                var index = formats.IndexOf(format);
                if (index < 0)
                {
                    formats.Add(format);
                    index = formats.Count - 1;
                }

                formatId = index;
            }

            sheet.ConditionalFormats.Add(new ConditionalFormat(ranges, [rule with { Priority = priority, FormatId = formatId }]));
        }));
    }

    /// <summary>Regeln löschen: of the selected cells (rules whose ranges meet the selection) or of the whole sheet.</summary>
    public void ClearConditionalFormats(bool wholeSheet)
    {
        var sheet = ActiveSheet;
        var ranges = SelectedRanges.ToList();
        Execute("Regeln löschen", () => ChangeSheetState(sheet, () =>
        {
            if (wholeSheet)
            {
                sheet.ConditionalFormats.Clear();
                return;
            }

            sheet.ConditionalFormats.RemoveAll(format => format.Ranges.Any(range => ranges.Any(range.Intersects)));
        }));
    }

    /// <summary>Regeln verwalten › Regel löschen: one conditional format of the sheet by its index.</summary>
    public void RemoveConditionalFormat(int index)
    {
        var sheet = ActiveSheet;
        if (index < 0 || index >= sheet.ConditionalFormats.Count)
        {
            return;
        }

        Execute("Regel löschen", () => ChangeSheetState(sheet, () => sheet.ConditionalFormats.RemoveAt(index)));
    }

    /// <summary>A rule as the rules manager lists it ("Zellwert > 100", "Datenbalken", "Doppelte Werte" …).</summary>
    public string DescribeRule(ConditionalFormatRule rule)
    {
        string Value(int index) => index < rule.Formulas.Count ? "=" + rule.Formulas[index] : string.Empty;
        return rule.Type switch
        {
            ConditionalFormatType.CellIs => rule.Operator switch
            {
                ComparisonOperator.GreaterThan => $"Zellwert > {Value(0)}",
                ComparisonOperator.LessThan => $"Zellwert < {Value(0)}",
                ComparisonOperator.Equal => $"Zellwert = {Value(0)}",
                ComparisonOperator.NotEqual => $"Zellwert <> {Value(0)}",
                ComparisonOperator.GreaterThanOrEqual => $"Zellwert >= {Value(0)}",
                ComparisonOperator.LessThanOrEqual => $"Zellwert <= {Value(0)}",
                ComparisonOperator.NotBetween => $"Zellwert nicht zwischen {Value(0)} und {Value(1)}",
                _ => $"Zellwert zwischen {Value(0)} und {Value(1)}",
            },
            ConditionalFormatType.Expression => $"Formel: {Value(0)}",
            ConditionalFormatType.ColorScale => rule.Colors.Count == 3 ? "Farbskala (3 Farben)" : "Farbskala (2 Farben)",
            ConditionalFormatType.DataBar => "Datenbalken",
            ConditionalFormatType.IconSet => "Symbolsatz",
            ConditionalFormatType.Top10 => $"{(rule.Bottom ? "Untere" : "Obere")} {rule.Rank}{(rule.Percent ? " %" : string.Empty)}",
            ConditionalFormatType.DuplicateValues => "Doppelte Werte",
            ConditionalFormatType.UniqueValues => "Eindeutige Werte",
            ConditionalFormatType.ContainsText => $"Zelle enthält „{rule.Text}“",
            ConditionalFormatType.NotContainsText => $"Zelle enthält nicht „{rule.Text}“",
            ConditionalFormatType.BeginsWith => $"Beginnt mit „{rule.Text}“",
            ConditionalFormatType.EndsWith => $"Endet mit „{rule.Text}“",
            ConditionalFormatType.AboveAverage => rule.AboveAverage ? "Über dem Durchschnitt" : "Unter dem Durchschnitt",
            ConditionalFormatType.ContainsBlanks => "Leere Zellen",
            ConditionalFormatType.ContainsErrors => "Fehler",
            ConditionalFormatType.TimePeriod => "Datum",
            _ => rule.Type.ToString(),
        };
    }
}
