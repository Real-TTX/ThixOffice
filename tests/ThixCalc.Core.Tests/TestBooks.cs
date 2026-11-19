using System.Globalization;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Tests;

/// <summary>Workbooks for the tests: a fixed clock, a seeded random source, English function names in formulas.</summary>
internal static class TestBooks
{
    public static readonly DateTime Now = new(2024, 6, 15, 10, 30, 0);

    public static Workbook Create(params string[] sheetNames)
    {
        var workbook = new Workbook();
        foreach (var name in sheetNames.Length == 0 ? ["Tabelle1"] : sheetNames)
        {
            workbook.AddSheet(name);
        }

        workbook.Calculation.Clock = () => Now;
        workbook.Calculation.Random = new Random(42);
        workbook.Calculation.Locale = CalcLocale.English;
        return workbook;
    }

    /// <summary>Fills cells: values are numbers, texts, booleans, errors; strings starting with "=" are formulas (canonical).</summary>
    public static void Fill(Worksheet sheet, params (string Address, object? Value)[] cells)
    {
        foreach (var (address, value) in cells)
        {
            Set(sheet, address, value);
        }
    }

    public static void Set(Worksheet sheet, string address, object? value)
    {
        var cell = CellAddress.Parse(address);
        switch (value)
        {
            case null:
                sheet.SetValue(cell.Row, cell.Column, CellValue.Blank);
                break;
            case string text when text.StartsWith('=') && text.Length > 1:
                sheet.SetFormula(cell.Row, cell.Column, FormulaParser.Parse(text, sheet.Workbook, sheet, cell));
                break;
            default:
                sheet.SetValue(cell.Row, cell.Column, ToValue(value));
                break;
        }
    }

    public static CellValue ToValue(object? value) => value switch
    {
        null => CellValue.Blank,
        CellValue cellValue => cellValue,
        double number => number,
        int number => number,
        long number => number,
        decimal number => (double)number,
        bool boolean => boolean,
        CellError error => error,
        string text => text,
        DateTime date => ThixCalc.Core.Calculation.DateSerial.FromDateTime(date),
        _ => throw new ArgumentException($"Unsupported test value {value}"),
    };

    /// <summary>Evaluates one formula on a sheet filled with <paramref name="cells"/> (the formula goes to ZZ1000).</summary>
    public static CellValue Evaluate(string formula, params (string Address, object? Value)[] cells)
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, cells);
        Set(sheet, "ZZ1000", formula.StartsWith('=') ? formula : "=" + formula);
        workbook.Calculation.Recalculate();
        return sheet["ZZ1000"];
    }

    /// <summary>Asserts a value: numbers with a relative tolerance, everything else exactly.</summary>
    public static void AssertValue(object? expected, CellValue actual, double tolerance = 1e-9)
    {
        var expectedValue = ToValue(expected);
        if (expectedValue.IsNumber && actual.IsNumber)
        {
            var scale = Math.Max(1, Math.Abs(expectedValue.Number));
            Assert.True(Math.Abs(expectedValue.Number - actual.Number) <= tolerance * scale,
                $"Expected {expectedValue.Number.ToString("R", CultureInfo.InvariantCulture)}, got {actual.Number.ToString("R", CultureInfo.InvariantCulture)}");
            return;
        }

        Assert.Equal(expectedValue, actual);
    }
}
