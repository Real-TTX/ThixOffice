using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Editing;

/// <summary>
/// Datenüberprüfung: whether a value fits a cell's validation rule (whole/decimal numbers, lists, dates, times, text length,
/// custom formulas), and the list entries of a list rule for the drop-down.
/// </summary>
public static class Validation
{
    public static DataValidation? RuleAt(Worksheet sheet, CellAddress cell) =>
        sheet.Validations.LastOrDefault(validation => validation.Ranges.Any(range => range.Contains(cell)));

    /// <summary>Throws the rule's error message when the value is refused (style Stopp).</summary>
    public static void Check(Worksheet sheet, CellAddress cell, CellValue value, CalcLocale locale)
    {
        if (RuleAt(sheet, cell) is not { ShowErrorMessage: true, ErrorStyle: ValidationErrorStyle.Stop } rule || IsValid(sheet, cell, value, rule))
        {
            return;
        }

        throw new BusinessException(rule.Error ?? "Dieser Wert entspricht nicht den für diese Zelle definierten Einschränkungen zur Datenüberprüfung.", "value");
    }

    public static bool IsValid(Worksheet sheet, CellAddress cell, CellValue value, DataValidation rule)
    {
        if (value.IsBlank || (value.IsText && value.Text.Length == 0))
        {
            return rule.AllowBlank;
        }

        var engine = sheet.Workbook.Calculation;
        CellValue Evaluate(string? formula)
        {
            if (formula is null)
            {
                return CellValue.Blank;
            }

            // Relative references of the rule are relative to its first cell; evaluated at this cell they shift with it.
            var parsed = FormulaParser.Parse(formula, sheet.Workbook, sheet, rule.Origin);
            return engine.Evaluate(parsed, sheet, cell);
        }

        switch (rule.Type)
        {
            case ValidationType.Any:
                return true;
            case ValidationType.List:
                return ListEntries(sheet, cell, rule).Any(entry => ValueComparer.AreEqual(entry, value) && entry.Kind == value.Kind
                    || entry.IsText && Coercion.ToText(value, CalcLocale.Invariant) == entry.Text);
            case ValidationType.Custom:
                var result = Evaluate(rule.Formula1);
                return result.IsBoolean ? result.Boolean : result.IsNumber && result.Number != 0;
            case ValidationType.TextLength:
                return Compare(Coercion.ToText(value, sheet.Workbook.Calculation.Locale).Length, rule, Evaluate);
            default:
                if (!value.IsNumber)
                {
                    return false;
                }

                if (rule.Type == ValidationType.WholeNumber && value.Number != Math.Floor(value.Number))
                {
                    return false;
                }

                if (rule.Type == ValidationType.Time && (value.Number < 0 || value.Number >= 1))
                {
                    return false;
                }

                return Compare(value.Number, rule, Evaluate);
        }
    }

    private static bool Compare(double value, DataValidation rule, Func<string?, CellValue> evaluate)
    {
        var first = evaluate(rule.Formula1);
        var second = evaluate(rule.Formula2);
        if (!first.IsNumber)
        {
            return true;
        }

        var low = first.Number;
        var high = second.IsNumber ? second.Number : low;
        return rule.Operator switch
        {
            ComparisonOperator.Between => value >= Math.Min(low, high) && value <= Math.Max(low, high),
            ComparisonOperator.NotBetween => value < Math.Min(low, high) || value > Math.Max(low, high),
            ComparisonOperator.Equal => value == low,
            ComparisonOperator.NotEqual => value != low,
            ComparisonOperator.GreaterThan => value > low,
            ComparisonOperator.LessThan => value < low,
            ComparisonOperator.GreaterThanOrEqual => value >= low,
            _ => value <= low,
        };
    }

    /// <summary>The entries of a list rule: a literal list ("a,b,c" – ";" in German) or the values of a range.</summary>
    public static IReadOnlyList<CellValue> ListEntries(Worksheet sheet, CellAddress cell, DataValidation rule)
    {
        if (rule.Type != ValidationType.List || rule.Formula1 is not { } formula)
        {
            return [];
        }

        if (formula.StartsWith('"') && formula.EndsWith('"') && formula.Length >= 2)
        {
            return formula[1..^1].Split(',').Select(entry => CellValue.FromText(entry.Trim())).ToList();
        }

        try
        {
            var node = FormulaParser.ParseNode(formula, sheet.Workbook, rule.Origin);
            var operand = sheet.Workbook.Calculation.EvaluateOperand(new Formula(node), sheet, rule.Origin, arrayMode: true);
            if (operand.Kind == OperandKind.Reference)
            {
                var area = operand.Reference.First;
                return sheet.Workbook.Calculation.EnumerateValues(area).Select(entry => entry.Value).ToList();
            }

            if (operand.Kind == OperandKind.Array)
            {
                return operand.Array.Values.Where(value => !value.IsBlank).ToList();
            }

            return operand.IsValue ? [operand.Value] : [];
        }
        catch (FormulaParseException)
        {
            return [];
        }
        finally
        {
            _ = cell;
        }
    }
}
