using System.Diagnostics;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using Xunit.Abstractions;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Calculation;

/// <summary>
/// The dependency graph and recalculation: incremental updates, ranges, cycles, volatile functions, cancellation, and the
/// property that an incremental recalculation after random edits equals a full recalculation of the same workbook.
/// </summary>
public class RecalculationTests(ITestOutputHelper output)
{
    [Fact]
    public void Only_dependents_are_recalculated()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 1), ("B1", "=A1*2"), ("C1", "=B1+1"), ("D1", "=7"), ("E1", "=SUM(A1:A10)"));
        var first = workbook.Calculation.Recalculate();
        Assert.Equal(4, first.Computed);

        Set(sheet, "A1", 5);
        var second = workbook.Calculation.Recalculate();
        Assert.Equal(3, second.Computed);
        Assert.Equal(11, sheet["C1"].Number);
        Assert.Equal(5, sheet["E1"].Number);

        Set(sheet, "A7", 5);
        Assert.Equal(1, workbook.Calculation.Recalculate().Computed);
        Assert.Equal(10, sheet["E1"].Number);
    }

    [Fact]
    public void Long_chains_need_no_deep_recursion()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        sheet.SetValue(0, 0, 1);
        var formula = FormulaParser.Parse("A1+1", workbook, sheet, new CellAddress(1, 0));
        const int length = 100_000;
        for (var row = 1; row < length; row++)
        {
            sheet.SetFormula(row, 0, formula);
        }

        var stopwatch = Stopwatch.StartNew();
        var result = workbook.Calculation.Recalculate();
        output.WriteLine($"Chain of {length:N0} formulas: first calculation {stopwatch.ElapsedMilliseconds} ms");
        Assert.Equal(length - 1, result.Computed);
        Assert.Equal(length, sheet.GetValue(length - 1, 0).Number);

        stopwatch.Restart();
        sheet.SetValue(0, 0, 2);
        workbook.Calculation.Recalculate();
        output.WriteLine($"Change at the head of the chain: {stopwatch.ElapsedMilliseconds} ms");
        Assert.Equal(length + 1, sheet.GetValue(length - 1, 0).Number);
    }

    [Fact]
    public void Circular_references_show_zero_and_are_reported()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "=B1+1"), ("B1", "=A1+1"), ("C1", "=5"));
        var result = workbook.Calculation.Recalculate();
        Assert.Contains("Tabelle1!A1", result.CircularReferences);
        Assert.Equal(0, sheet["A1"].Number);
        Assert.Equal(5, sheet["C1"].Number);
    }

    [Fact]
    public void Iteration_solves_circular_references_when_enabled()
    {
        var workbook = Create();
        workbook.CalculationSettings = workbook.CalculationSettings with { Iterate = true, MaxIterations = 100, MaxChange = 1e-9 };
        var sheet = workbook.Sheets[0];
        // x = 1 + x/2 converges to 2.
        Fill(sheet, ("A1", "=1+A1/2"));
        workbook.Calculation.Recalculate();
        Assert.Equal(2, sheet["A1"].Number, 6);
    }

    [Fact]
    public void Volatile_functions_recalculate_every_time()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "=RAND()"), ("B1", "=A1*1"), ("C1", "=1+1"));
        workbook.Calculation.Recalculate();
        var first = sheet["B1"].Number;
        var result = workbook.Calculation.Recalculate();
        Assert.Equal(2, result.Computed);
        Assert.NotEqual(first, sheet["B1"].Number);
    }

    [Fact]
    public void Indirect_references_are_computed_on_demand()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "=B1*2"), ("B1", 21), ("C1", "=INDIRECT(\"A\"&1)+0"));
        workbook.Calculation.Recalculate();
        Assert.Equal(42, sheet["C1"].Number);
        Set(sheet, "B1", 1);
        workbook.Calculation.Recalculate();
        Assert.Equal(2, sheet["C1"].Number);
    }

    [Fact]
    public void Cancellation_leaves_the_rest_dirty_and_continues_later()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        for (var row = 0; row < 5000; row++)
        {
            Set(sheet, A1.ColumnName(0) + (row + 1), $"={row}*2");
        }

        using var source = new CancellationTokenSource();
        source.Cancel();
        var canceled = workbook.Calculation.Recalculate(source.Token);
        Assert.True(canceled.Canceled);
        Assert.True(workbook.Calculation.NeedsRecalculation);
        var finished = workbook.Calculation.Recalculate();
        Assert.False(finished.Canceled);
        Assert.Equal(9998, sheet["A5000"].Number);
    }

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 40; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    /// <summary>Random formulas and edits: after every edit the incremental result equals a fresh full recalculation.</summary>
    [Theory]
    [MemberData(nameof(Seeds))]
    public void Incremental_recalculation_equals_full_recalculation(int seed)
    {
        var random = new Random(seed);
        var workbook = Create("Tabelle1", "Tabelle2");
        var cells = new List<(Worksheet Sheet, string Address)>();
        foreach (var sheet in workbook.Sheets)
        {
            for (var row = 1; row <= 6; row++)
            {
                for (var column = 0; column < 4; column++)
                {
                    cells.Add((sheet, A1.ColumnName(column) + row));
                }
            }
        }

        for (var step = 0; step < 60; step++)
        {
            var (sheet, address) = cells[random.Next(cells.Count)];
            Set(sheet, address, RandomContent(random, sheet, CellAddress.Parse(address).Row));
            if (random.Next(3) == 0)
            {
                workbook.Calculation.Recalculate();
                AssertSameAsFullRecalculation(workbook);
            }
        }

        workbook.Calculation.Recalculate();
        AssertSameAsFullRecalculation(workbook);
    }

    /// <summary>
    /// Random content for a cell in <paramref name="row"/>: formulas only read rows above (and Tabelle2 reads Tabelle1), so the
    /// graph stays acyclic and the result does not depend on the order of evaluation; arrays spill downward only.
    /// </summary>
    private static object? RandomContent(Random random, Worksheet sheet, int row)
    {
        if (row == 0 && sheet.Name == "Tabelle1")
        {
            return random.Next(3) == 0 ? "Text" + random.Next(3) : random.Next(100);
        }

        var crossSheet = sheet.Name == "Tabelle2";
        string Cell() => crossSheet && (row == 0 || random.Next(2) == 0)
            ? "Tabelle1!" + A1.ColumnName(random.Next(4)) + (random.Next(6) + 1)
            : A1.ColumnName(random.Next(4)) + (random.Next(row) + 1);
        string Other() => crossSheet ? "Tabelle1!" + A1.ColumnName(random.Next(4)) + (random.Next(6) + 1) : Cell();
        string Range()
        {
            if (crossSheet && (row == 0 || random.Next(2) == 0))
            {
                return $"Tabelle1!A{random.Next(6) + 1}:D{random.Next(6) + 1}";
            }

            return $"{A1.ColumnName(random.Next(4))}{random.Next(row) + 1}:{A1.ColumnName(random.Next(4))}{random.Next(row) + 1}";
        }

        return random.Next(12) switch
        {
            0 => null,
            1 => random.Next(100),
            2 => "Text" + random.Next(3),
            3 => $"={Cell()}+{random.Next(10)}",
            4 => $"=SUM({Range()})",
            5 => $"={Other()}*2",
            6 => $"=IF({Cell()}>50,{Cell()},{random.Next(9)})",
            7 => $"=COUNTIF({Range()},\">10\")",
            8 => $"=IFERROR({Cell()}/{Cell()},-1)",
            9 => $"=SEQUENCE({random.Next(1, 3)},1,{random.Next(5)})",
            10 => $"=INDEX({Range()},1,1)+{Cell()}",
            _ => $"=MAX({Cell()},{Other()})",
        };
    }

    private static void AssertSameAsFullRecalculation(Workbook workbook)
    {
        var incremental = Snapshot(workbook);
        workbook.Calculation.Rebuild();
        workbook.Calculation.Recalculate();
        var full = Snapshot(workbook);
        Assert.Equal(full, incremental);
    }

    private static Dictionary<string, CellValue> Snapshot(Workbook workbook)
    {
        var values = new Dictionary<string, CellValue>();
        foreach (var sheet in workbook.Sheets)
        {
            foreach (var entry in sheet.GetCells())
            {
                values[sheet.Name + "!" + entry.Address] = entry.Cell.Value;
            }
        }

        return values;
    }
}
