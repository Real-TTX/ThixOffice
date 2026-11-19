using System.Text;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Editing;

/// <summary>
/// Previews without changing anything (Funktionsargumente): what an argument or a whole formula would give at the active cell,
/// as text the way Excel's dialog shows it – "= 42", "= "Text"", "= {10;20;30}", "= #NV".
/// </summary>
public sealed partial class EditingSession
{
    private const int PreviewValues = 12;

    /// <summary>The value of an expression (an argument as typed, without "=") at the active cell, or null when it does not parse.</summary>
    public string? PreviewExpression(string expression)
    {
        if (string.IsNullOrWhiteSpace(expression))
        {
            return null;
        }

        var formula = FormulaParser.TryParse("=" + expression, Workbook, ActiveSheet, ActiveCell, Syntax, out _);
        if (formula is null)
        {
            return null;
        }

        var operand = Workbook.Calculation.EvaluateOperand(formula, ActiveSheet, ActiveCell, arrayMode: true);
        return Describe(operand, Locale);
    }

    /// <summary>The result of a whole formula ("=…") at the active cell, or null when it does not parse.</summary>
    public string? PreviewFormula(string formulaText)
    {
        var text = formulaText.TrimStart();
        return text.StartsWith('=') ? PreviewExpression(text[1..]) : null;
    }

    private static string Describe(Operand operand, CalcLocale locale)
    {
        switch (operand.Kind)
        {
            case OperandKind.Value:
                return Describe(operand.Value, locale);
            case OperandKind.Array:
                return Describe(operand.Array.Rows, operand.Array.Columns, (row, column) => operand.Array[row, column], locale);
            case OperandKind.Reference:
                var reference = operand.Reference;
                if (!reference.IsSingleArea)
                {
                    return locale.ErrorName(CellError.Value);
                }

                var area = reference.First;
                var range = area.Range;
                return range.IsSingleCell
                    ? Describe(area.Sheet.GetValue(range.FirstRow, range.FirstColumn), locale)
                    : Describe(Math.Min(range.RowCount, PreviewValues), Math.Min(range.ColumnCount, PreviewValues),
                        (row, column) => area.Sheet.GetValue(range.FirstRow + row, range.FirstColumn + column), locale,
                        range.CellCount > PreviewValues);
            default:
                return string.Empty;
        }
    }

    private static string Describe(int rows, int columns, Func<int, int, CellValue> valueAt, CalcLocale locale, bool more = false)
    {
        var builder = new StringBuilder("{");
        var shown = 0;
        for (var row = 0; row < rows && shown < PreviewValues; row++)
        {
            if (row > 0)
            {
                builder.Append(locale.ArrayRowSeparator);
            }

            for (var column = 0; column < columns && shown < PreviewValues; column++, shown++)
            {
                if (column > 0)
                {
                    builder.Append(locale.ArrayColumnSeparator);
                }

                builder.Append(Describe(valueAt(row, column), locale));
            }
        }

        if (more || shown < rows * columns)
        {
            builder.Append('…');
        }

        return builder.Append('}').ToString();
    }

    private static string Describe(CellValue value, CalcLocale locale) => value.Kind switch
    {
        CellValueKind.Number => NumberText.ToText(value.Number, locale),
        CellValueKind.Text => "\"" + value.Text + "\"",
        CellValueKind.Boolean => locale.BooleanName(value.Boolean),
        CellValueKind.Error => locale.ErrorName(value.Error),
        _ => "0",
    };
}
