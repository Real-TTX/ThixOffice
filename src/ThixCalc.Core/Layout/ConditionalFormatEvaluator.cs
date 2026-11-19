using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Layout;

/// <summary>What conditional formatting changes about a cell: format overrides, a scale colour, a data bar, an icon.</summary>
public sealed record ConditionalLook
{
    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    public bool? Strikethrough { get; init; }

    public bool? Underline { get; init; }

    public CalcColor? FontColor { get; init; }

    public CalcColor? FillColor { get; init; }

    public string? NumberFormat { get; init; }

    /// <summary>The colour of a colour scale.</summary>
    public RgbColor? ScaleColor { get; init; }

    /// <summary>A data bar: its length (0–1 of the cell) and colour.</summary>
    public (double Fraction, RgbColor Color)? DataBar { get; init; }

    /// <summary>An icon of an icon set: the set's name and the icon's index (0 = lowest).</summary>
    public (string Set, int Index)? Icon { get; init; }

    public bool HideValue { get; init; }
}

/// <summary>
/// Evaluates the conditional formats of a sheet for cells (the core part; drawing the look is the grid's): cell value rules,
/// formulas, texts, blanks/errors, top/bottom, above/below average, duplicates, dates, colour scales, data bars, icon sets.
/// Range statistics are computed once per format and kept until <see cref="Invalidate"/>.
/// </summary>
public sealed class ConditionalFormatEvaluator(Worksheet sheet)
{
    private readonly Dictionary<ConditionalFormatRule, List<double>> _numbers = new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<ConditionalFormatRule, Dictionary<string, int>> _counts = new(ReferenceEqualityComparer.Instance);

    public void Invalidate()
    {
        _numbers.Clear();
        _counts.Clear();
    }

    /// <summary>The conditional look of a cell, or null when no rule applies.</summary>
    public ConditionalLook? Evaluate(int row, int column)
    {
        ConditionalLook? look = null;
        var stopped = false;
        foreach (var format in sheet.ConditionalFormats)
        {
            if (!format.Ranges.Any(range => range.Contains(row, column)))
            {
                continue;
            }

            foreach (var rule in format.Rules.OrderBy(rule => rule.Priority))
            {
                if (stopped)
                {
                    break;
                }

                var result = Apply(format, rule, row, column);
                if (result is null)
                {
                    continue;
                }

                look = Merge(look, result);
                stopped = rule.StopIfTrue;
            }
        }

        return look;
    }

    private ConditionalLook? Apply(ConditionalFormat format, ConditionalFormatRule rule, int row, int column)
    {
        var value = sheet.GetValue(row, column);
        switch (rule.Type)
        {
            case ConditionalFormatType.ColorScale:
                return value.IsNumber ? Scale(format, rule, value.Number) : null;
            case ConditionalFormatType.DataBar:
                return value.IsNumber ? Bar(format, rule, value.Number) : null;
            case ConditionalFormatType.IconSet:
                return value.IsNumber ? Icon(format, rule, value.Number) : null;
        }

        return Matches(format, rule, value, row, column) ? Differential(rule) : null;
    }

    private bool Matches(ConditionalFormat format, ConditionalFormatRule rule, CellValue value, int row, int column)
    {
        var text = value.IsText ? value.Text : value.IsBlank ? string.Empty : Coercion.ToText(value.IsError ? CellValue.Blank : value, sheet.Workbook.Calculation.Locale);
        switch (rule.Type)
        {
            case ConditionalFormatType.CellIs:
            {
                if (value.IsBlank && rule.Operator is not (ComparisonOperator.Equal or ComparisonOperator.NotEqual))
                {
                    value = CellValue.Zero;
                }

                var first = Formula(format, rule, 0, row, column);
                var second = Formula(format, rule, 1, row, column);
                if (value.IsError || first.IsError)
                {
                    return false;
                }

                var compare = ValueComparer.Compare(value, first);
                return rule.Operator switch
                {
                    ComparisonOperator.Between => compare >= 0 && ValueComparer.Compare(value, second) <= 0,
                    ComparisonOperator.NotBetween => compare < 0 || ValueComparer.Compare(value, second) > 0,
                    ComparisonOperator.Equal => compare == 0,
                    ComparisonOperator.NotEqual => compare != 0,
                    ComparisonOperator.GreaterThan => compare > 0,
                    ComparisonOperator.LessThan => compare < 0,
                    ComparisonOperator.GreaterThanOrEqual => compare >= 0,
                    _ => compare <= 0,
                };
            }

            case ConditionalFormatType.Expression:
            {
                var result = Formula(format, rule, 0, row, column);
                return result.IsBoolean ? result.Boolean : result.IsNumber && result.Number != 0;
            }

            case ConditionalFormatType.ContainsText:
                return text.Contains(rule.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case ConditionalFormatType.NotContainsText:
                return !text.Contains(rule.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case ConditionalFormatType.BeginsWith:
                return text.StartsWith(rule.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case ConditionalFormatType.EndsWith:
                return text.EndsWith(rule.Text ?? string.Empty, StringComparison.OrdinalIgnoreCase);
            case ConditionalFormatType.ContainsBlanks:
                return value.IsBlank || text.Trim().Length == 0 && value.IsText;
            case ConditionalFormatType.NotContainsBlanks:
                return !(value.IsBlank || text.Trim().Length == 0 && value.IsText);
            case ConditionalFormatType.ContainsErrors:
                return value.IsError;
            case ConditionalFormatType.NotContainsErrors:
                return !value.IsError;
            case ConditionalFormatType.Top10:
            {
                if (!value.IsNumber)
                {
                    return false;
                }

                var numbers = Numbers(format, rule);
                var count = rule.Percent ? (int)Math.Max(1, Math.Floor(numbers.Count * rule.Rank / 100.0)) : rule.Rank;
                var sorted = rule.Bottom ? numbers.OrderBy(number => number).ToList() : numbers.OrderByDescending(number => number).ToList();
                if (sorted.Count == 0)
                {
                    return false;
                }

                var threshold = sorted[Math.Min(count, sorted.Count) - 1];
                return rule.Bottom ? value.Number <= threshold : value.Number >= threshold;
            }

            case ConditionalFormatType.AboveAverage:
            {
                if (!value.IsNumber)
                {
                    return false;
                }

                var numbers = Numbers(format, rule);
                if (numbers.Count == 0)
                {
                    return false;
                }

                var average = numbers.Average();
                var deviation = rule.StdDev > 0 && numbers.Count > 1 ? Math.Sqrt(numbers.Sum(number => (number - average) * (number - average)) / numbers.Count) * rule.StdDev : 0;
                var limit = rule.AboveAverage ? average + deviation : average - deviation;
                return rule.AboveAverage
                    ? rule.EqualAverage ? value.Number >= limit : value.Number > limit
                    : rule.EqualAverage ? value.Number <= limit : value.Number < limit;
            }

            case ConditionalFormatType.DuplicateValues:
            case ConditionalFormatType.UniqueValues:
            {
                if (value.IsBlank)
                {
                    return false;
                }

                var counts = Counts(format, rule);
                var occurrences = counts.TryGetValue(text.ToUpperInvariant(), out var number) ? number : 0;
                return rule.Type == ConditionalFormatType.DuplicateValues ? occurrences > 1 : occurrences == 1;
            }

            case ConditionalFormatType.TimePeriod:
                return value.IsNumber && InPeriod(Math.Floor(value.Number), rule.TimePeriod);
            default:
                return false;
        }
    }

    private bool InPeriod(double serial, string? period)
    {
        var workbook = sheet.Workbook;
        var today = Math.Floor(DateSerial.FromDateTime(workbook.Calculation.Clock(), workbook.Date1904));
        var weekStart = today - DateSerial.DayOfWeek(today, workbook.Date1904);
        var (year, month, _) = DateSerial.ToDate(today, workbook.Date1904);
        var (valueYear, valueMonth, _) = DateSerial.ToDate(serial, workbook.Date1904);
        var monthIndex = valueYear * 12 + valueMonth - (year * 12 + month);
        return period switch
        {
            "today" => serial == today,
            "yesterday" => serial == today - 1,
            "tomorrow" => serial == today + 1,
            "last7Days" => serial > today - 7 && serial <= today,
            "thisWeek" => serial >= weekStart && serial < weekStart + 7,
            "lastWeek" => serial >= weekStart - 7 && serial < weekStart,
            "nextWeek" => serial >= weekStart + 7 && serial < weekStart + 14,
            "thisMonth" => monthIndex == 0,
            "lastMonth" => monthIndex == -1,
            "nextMonth" => monthIndex == 1,
            _ => false,
        };
    }

    private CellValue Formula(ConditionalFormat format, ConditionalFormatRule rule, int index, int row, int column)
    {
        if (index >= rule.Formulas.Count)
        {
            return CellValue.Blank;
        }

        try
        {
            // The formula is relative to the format's first cell; evaluated at this cell it shifts with it.
            var formula = FormulaParser.Parse(rule.Formulas[index], sheet.Workbook, sheet, format.Origin);
            return sheet.Workbook.Calculation.Evaluate(formula, sheet, new CellAddress(row, column));
        }
        catch (FormulaParseException)
        {
            return CellValue.FromError(CellError.Name);
        }
    }

    private ConditionalLook? Differential(ConditionalFormatRule rule)
    {
        if (rule.FormatId is not { } id || id < 0 || id >= sheet.Workbook.Styles.DifferentialFormats.Count)
        {
            return new ConditionalLook();
        }

        var format = sheet.Workbook.Styles.DifferentialFormats[id];
        return new ConditionalLook
        {
            Bold = format.Bold,
            Italic = format.Italic,
            Strikethrough = format.Strikethrough,
            Underline = format.Underline is null ? null : format.Underline != UnderlineKind.None,
            FontColor = format.FontColor,
            FillColor = format.FillColor,
            NumberFormat = format.NumberFormat,
        };
    }

    private List<double> Numbers(ConditionalFormat format, ConditionalFormatRule rule)
    {
        if (_numbers.TryGetValue(rule, out var numbers))
        {
            return numbers;
        }

        numbers = [];
        foreach (var range in format.Ranges)
        {
            foreach (var entry in sheet.GetCells(range))
            {
                if (entry.Cell.Value.IsNumber)
                {
                    numbers.Add(entry.Cell.Value.Number);
                }
            }
        }

        _numbers[rule] = numbers;
        return numbers;
    }

    private Dictionary<string, int> Counts(ConditionalFormat format, ConditionalFormatRule rule)
    {
        if (_counts.TryGetValue(rule, out var counts))
        {
            return counts;
        }

        counts = [];
        foreach (var range in format.Ranges)
        {
            foreach (var entry in sheet.GetCells(range))
            {
                var value = entry.Cell.Value;
                if (value.IsBlank)
                {
                    continue;
                }

                var key = (value.IsText ? value.Text : Coercion.ToText(value.IsError ? CellValue.Blank : value, sheet.Workbook.Calculation.Locale)).ToUpperInvariant();
                counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
            }
        }

        _counts[rule] = counts;
        return counts;
    }

    private double Threshold(ConditionalFormat format, ConditionalFormatRule rule, ConditionalValue threshold, bool minimum)
    {
        var numbers = Numbers(format, rule);
        if (numbers.Count == 0)
        {
            return 0;
        }

        var low = numbers.Min();
        var high = numbers.Max();
        double Parsed() => double.TryParse(threshold.Value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var number) ? number : 0;
        return threshold.Type switch
        {
            ConditionalValueType.Min or ConditionalValueType.AutoMin => low,
            ConditionalValueType.Max or ConditionalValueType.AutoMax => high,
            ConditionalValueType.Percent => low + (high - low) * Parsed() / 100,
            ConditionalValueType.Percentile => Functions.Aggregates.PercentileInclusive(numbers.OrderBy(number => number).ToList(), Math.Clamp(Parsed() / 100, 0, 1)),
            ConditionalValueType.Formula => FormulaThreshold(format, threshold.Value, minimum ? low : high),
            _ => Parsed(),
        };
    }

    private double FormulaThreshold(ConditionalFormat format, string? text, double fallback)
    {
        if (text is null)
        {
            return fallback;
        }

        try
        {
            var value = sheet.Workbook.Calculation.Evaluate(FormulaParser.Parse(text, sheet.Workbook, sheet, format.Origin), sheet, format.Origin);
            return value.IsNumber ? value.Number : fallback;
        }
        catch (FormulaParseException)
        {
            return fallback;
        }
    }

    private ConditionalLook Scale(ConditionalFormat format, ConditionalFormatRule rule, double value)
    {
        var theme = sheet.Workbook.Theme;
        var colors = rule.Colors.Select(color => color.Resolve(theme, RgbColor.White)).ToList();
        var points = rule.Thresholds.Select((threshold, index) => Threshold(format, rule, threshold, index == 0)).ToList();
        if (colors.Count < 2 || points.Count < 2)
        {
            return new ConditionalLook();
        }

        RgbColor color;
        if (value <= points[0])
        {
            color = colors[0];
        }
        else if (value >= points[^1])
        {
            color = colors[^1];
        }
        else
        {
            var segment = 0;
            while (segment < points.Count - 2 && value > points[segment + 1])
            {
                segment++;
            }

            var span = points[segment + 1] - points[segment];
            var fraction = span == 0 ? 0 : (value - points[segment]) / span;
            color = Mix(colors[segment], colors[Math.Min(segment + 1, colors.Count - 1)], fraction);
        }

        return new ConditionalLook { ScaleColor = color };
    }

    private ConditionalLook Bar(ConditionalFormat format, ConditionalFormatRule rule, double value)
    {
        var low = rule.Thresholds.Count > 0 ? Threshold(format, rule, rule.Thresholds[0], true) : Numbers(format, rule).DefaultIfEmpty(0).Min();
        var high = rule.Thresholds.Count > 1 ? Threshold(format, rule, rule.Thresholds[1], false) : Numbers(format, rule).DefaultIfEmpty(0).Max();
        var fraction = high <= low ? 1 : Math.Clamp((value - low) / (high - low), 0, 1);
        var color = rule.Colors.Count > 0 ? rule.Colors[0].Resolve(sheet.Workbook.Theme, RgbColor.FromHex("638EC6")) : RgbColor.FromHex("638EC6");
        return new ConditionalLook { DataBar = (0.1 + 0.9 * fraction, color), HideValue = !rule.ShowValue };
    }

    private ConditionalLook Icon(ConditionalFormat format, ConditionalFormatRule rule, double value)
    {
        var set = rule.IconSet ?? "3TrafficLights1";
        var icons = int.TryParse(set[..1], out var count) ? count : 3;
        var thresholds = rule.Thresholds.Count == icons
            ? rule.Thresholds.Select((threshold, index) => (Value: Threshold(format, rule, threshold, index == 0), threshold.GreaterOrEqual)).ToList()
            : Enumerable.Range(0, icons).Select(index => (Value: Threshold(format, rule, new ConditionalValue(ConditionalValueType.Percent, (index * 100.0 / icons).ToString(System.Globalization.CultureInfo.InvariantCulture)), index == 0), GreaterOrEqual: true)).ToList();
        var index = 0;
        for (var position = 1; position < thresholds.Count; position++)
        {
            var (limit, greaterOrEqual) = thresholds[position];
            if (greaterOrEqual ? value >= limit : value > limit)
            {
                index = position;
            }
        }

        if (rule.Reverse)
        {
            index = icons - 1 - index;
        }

        return new ConditionalLook { Icon = (set, index), HideValue = !rule.ShowValue };
    }

    private static RgbColor Mix(RgbColor first, RgbColor second, double fraction) => new(
        (byte)Math.Round(first.R + (second.R - first.R) * fraction),
        (byte)Math.Round(first.G + (second.G - first.G) * fraction),
        (byte)Math.Round(first.B + (second.B - first.B) * fraction));

    /// <summary>Earlier (higher-priority) settings win; later rules fill what is still open.</summary>
    private static ConditionalLook Merge(ConditionalLook? existing, ConditionalLook added)
    {
        if (existing is null)
        {
            return added;
        }

        return existing with
        {
            Bold = existing.Bold ?? added.Bold,
            Italic = existing.Italic ?? added.Italic,
            Strikethrough = existing.Strikethrough ?? added.Strikethrough,
            Underline = existing.Underline ?? added.Underline,
            FontColor = existing.FontColor ?? added.FontColor,
            FillColor = existing.FillColor ?? added.FillColor,
            NumberFormat = existing.NumberFormat ?? added.NumberFormat,
            ScaleColor = existing.ScaleColor ?? added.ScaleColor,
            DataBar = existing.DataBar ?? added.DataBar,
            Icon = existing.Icon ?? added.Icon,
            HideValue = existing.HideValue || added.HideValue,
        };
    }
}
