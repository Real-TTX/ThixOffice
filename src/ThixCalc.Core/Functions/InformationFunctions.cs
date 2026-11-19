using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Functions;

/// <summary>Information functions (Information): the IS… family, TYPE, ERROR.TYPE, N, NA, SHEET(S), CELL.</summary>
internal static class InformationFunctions
{
    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Information;
        r.Add("ISBLANK", 1, 1, call => call.Value(0).IsBlank);
        r.Add("ISERR", 1, 1, call => call.Value(0) is { IsError: true } value && value.Error != CellError.NotAvailable);
        r.Add("ISERROR", 1, 1, call => call.Value(0).IsError);
        r.Add("ISNA", 1, 1, call => call.Value(0) is { IsError: true, Error: CellError.NotAvailable });
        r.Add("ISLOGICAL", 1, 1, call => call.Value(0).IsBoolean);
        r.Add("ISNUMBER", 1, 1, call => call.Value(0).IsNumber);
        r.Add("ISTEXT", 1, 1, call => call.Value(0).IsText);
        r.Add("ISNONTEXT", 1, 1, call => !call.Value(0).IsText);
        r.Add("ISEVEN", 1, 1, call => Math.Floor(Math.Abs(call.Number(0))) % 2 == 0);
        r.Add("ISODD", 1, 1, call => Math.Floor(Math.Abs(call.Number(0))) % 2 == 1);
        r.Add("ISREF", 1, 1, call => call.Evaluate(0, ArgumentKind.Reference).Kind == OperandKind.Reference, lazy: true);
        r.Add("ISFORMULA", 1, 1, call =>
        {
            var reference = call.Reference(0) ?? throw new CalcErrorException(CellError.Value);
            var area = reference.First;
            return area.Sheet.GetFormula(area.Range.FirstRow, area.Range.FirstColumn) is not null;
        }, [ArgumentKind.Reference]);
        r.Add("N", 1, 1, call => call.Value(0) switch
        {
            { IsError: true } error => error,
            { IsNumber: true } number => number,
            { IsBoolean: true } boolean => boolean.Number,
            _ => CellValue.Zero,
        });
        r.Add("NA", 0, 0, _ => CellError.NotAvailable);
        r.Add("TYPE", 1, 1, call =>
        {
            var operand = call[0];
            if (operand.Kind == OperandKind.Array || (operand.Kind == OperandKind.Reference && !operand.Reference.IsSingleCell))
            {
                return 64;
            }

            return Evaluator.ToScalar(operand, call.Context).Kind switch
            {
                CellValueKind.Text => 2,
                CellValueKind.Boolean => 4,
                CellValueKind.Error => 16,
                _ => 1,
            };
        }, [ArgumentKind.Any]);
        r.Add("ERROR.TYPE", 1, 1, call => call.Value(0) is { IsError: true } value ? (int)value.Error : CellError.NotAvailable);
        r.Add("SHEET", 0, 1, call =>
        {
            if (call.IsMissing(0))
            {
                return call.Context.Sheet.Index + 1;
            }

            var operand = call[0];
            if (operand.Kind == OperandKind.Reference)
            {
                return operand.Reference.First.Sheet.Index + 1;
            }

            var name = Coercion.ToText(Evaluator.ToScalar(operand, call.Context), call.Context.Locale);
            return call.Context.Workbook.FindSheet(name) is { } sheet ? sheet.Index + 1 : CellError.NotAvailable;
        }, [ArgumentKind.Reference]);
        r.Add("SHEETS", 0, 1, call =>
        {
            if (call.IsMissing(0))
            {
                return call.Context.Workbook.Sheets.Count;
            }

            var reference = call.Reference(0) ?? throw new CalcErrorException(CellError.Value);
            return reference.Areas.Select(area => area.Sheet).Distinct().Count();
        }, [ArgumentKind.Reference]);
        r.Add("CELL", 1, 2, Cell, [ArgumentKind.Value, ArgumentKind.Reference], isVolatile: true);
    }

    private static Operand Cell(FunctionCall call)
    {
        var info = call.Text(0).Trim().ToLowerInvariant();
        var context = call.Context;
        var sheet = context.Sheet;
        var row = context.Row;
        var column = context.Column;
        if (call.Has(1) && call.Reference(1) is { } reference)
        {
            sheet = reference.First.Sheet;
            row = reference.First.Range.FirstRow;
            column = reference.First.Range.FirstColumn;
        }

        var value = context.Engine.GetValue(sheet, row, column);
        return info switch
        {
            "address" or "adresse" => FormulaFormatter.Format(
                new ReferenceNode(sheet == context.Sheet ? null : SheetReference.Single(sheet.Id), AreaReference.Cell(CellReference.Absolute(row, column))),
                context.Workbook, default, new FormulaSyntax(context.Locale)),
            "col" or "spalte" => column + 1,
            "row" or "zeile" => row + 1,
            "contents" or "inhalt" => value,
            "type" or "typ" => value.IsBlank ? "b" : value.IsText ? "l" : "v",
            "filename" or "dateiname" => string.Empty,
            "protect" or "schutz" => sheet.GetStyle(row, column).Protection.Locked ? 1 : 0,
            "prefix" or "präfix" => value.IsText ? sheet.GetStyle(row, column).Alignment.Horizontal switch
            {
                HorizontalAlignment.Right => "\"",
                HorizontalAlignment.Center => "^",
                HorizontalAlignment.Fill => "\\",
                _ => "'",
            } : string.Empty,
            "width" or "breite" => Math.Round(sheet.Columns.SizeOf(column)),
            "format" => CellFormatCode(sheet.GetStyle(row, column).NumberFormat),
            _ => CellError.Value,
        };
    }

    /// <summary>The CELL("format") code: G, F0, ,2, C2, P0, D1 … (a simplified mapping).</summary>
    private static string CellFormatCode(string format)
    {
        var code = format.ToLowerInvariant();
        if (code == "general")
        {
            return "G";
        }

        var decimals = code.Contains('.') ? code[(code.IndexOf('.') + 1)..].TakeWhile(character => character == '0').Count() : 0;
        if (code.Contains('%'))
        {
            return "P" + decimals;
        }

        if (code.Contains('y') || code.Contains('d'))
        {
            return "D1";
        }

        if (code.Contains('h') || code.Contains('s'))
        {
            return "D9";
        }

        if (code.Contains("e+"))
        {
            return "S" + decimals;
        }

        if (code.Contains('$') || code.Contains('€'))
        {
            return "C" + decimals;
        }

        return (code.Contains(',') ? "," : "F") + decimals;
    }
}
