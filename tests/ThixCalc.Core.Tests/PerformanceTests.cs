using System.Diagnostics;
using ThixCalc.Core.Formats.Xlsx;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using Xunit.Abstractions;

namespace ThixCalc.Core.Tests;

/// <summary>
/// Performance with stated numbers (see the test output): a sheet with 1,000,000 filled cells (half values, half formulas
/// filled down), its full recalculation, an incremental one, saving and loading the xlsx. The limits are generous (CI
/// machines vary); the numbers in the output are what matters.
/// </summary>
public class PerformanceTests(ITestOutputHelper output)
{
    [Fact]
    public void A_million_cells_build_recalculate_save_and_load()
    {
        const int rows = 100_000;
        const int valueColumns = 5;
        var before = GC.GetTotalMemory(forceFullCollection: true);
        var stopwatch = Stopwatch.StartNew();
        var workbook = TestBooks.Create();
        var sheet = workbook.Sheets[0];
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < valueColumns; column++)
            {
                sheet.SetValue(row, column, row * 10 + column);
            }
        }

        // Five formula columns, each filled down (one shared formula instance per column).
        var formulas = new[] { "A1*2", "B1+C1", "IF(D1>100,E1,0)", "F1+G1", "SUM(A1:E1)" }
            .Select((text, index) => FormulaParser.Parse(text, workbook, sheet, new CellAddress(0, valueColumns + index))).ToArray();
        for (var row = 0; row < rows; row++)
        {
            for (var index = 0; index < formulas.Length; index++)
            {
                var column = valueColumns + index;
                sheet.SetFormula(row, column, formulas[index]);
            }
        }

        var build = stopwatch.Elapsed;
        stopwatch.Restart();
        var full = workbook.Calculation.Recalculate();
        var recalculation = stopwatch.Elapsed;
        var memory = GC.GetTotalMemory(forceFullCollection: true) - before;
        Assert.Equal(1_000_000, sheet.CellCount);
        Assert.Equal(500_000, full.Computed);
        Assert.Equal(999_990 * 2 + 999_991 + 999_992, sheet.GetValue(rows - 1, 8).Number);

        stopwatch.Restart();
        sheet.SetValue(5, 0, -1);
        var incremental = workbook.Calculation.Recalculate();
        var change = stopwatch.Elapsed;

        stopwatch.Restart();
        using var file = new MemoryStream();
        XlsxWriter.Write(workbook, file);
        var save = stopwatch.Elapsed;
        stopwatch.Restart();
        file.Position = 0;
        var loaded = XlsxReader.Read(file);
        var load = stopwatch.Elapsed;
        Assert.Equal(sheet.GetValue(rows - 1, 9).Number, loaded.Sheets[0].GetValue(rows - 1, 9).Number);

        output.WriteLine($"1,000,000 cells (500,000 formulas): build {build.TotalSeconds:F2} s, full recalculation {recalculation.TotalSeconds:F2} s, memory {memory / 1024.0 / 1024:F0} MB");
        output.WriteLine($"incremental change: {incremental.Computed} formulas in {change.TotalMilliseconds:F1} ms");
        output.WriteLine($"xlsx: save {save.TotalSeconds:F2} s ({file.Length / 1024.0 / 1024:F1} MB), load {load.TotalSeconds:F2} s");
        Assert.True(incremental.Computed <= 5);
        Assert.True(recalculation < TimeSpan.FromSeconds(60));
    }
}
