using ThixCalc.Core.Model;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Calculation;

/// <summary>Operators, coercion, comparison, error propagation, references, implicit intersection and spills.</summary>
public class EvaluationTests
{
    [Theory]
    [InlineData("1+2*3", 7.0)]
    [InlineData("-2^2", 4.0)]
    [InlineData("2^3^2", 64.0)]
    [InlineData("10/4", 2.5)]
    [InlineData("50%", 0.5)]
    [InlineData("200%%", 0.02)]
    [InlineData("\"3\"+4", 7.0)]
    [InlineData("TRUE+TRUE", 2.0)]
    [InlineData("\"1.5\"*2", 3.0)]
    [InlineData("-8^(1/3)", -2.0)]
    [InlineData("0.1+0.2=0.3", true)]
    [InlineData("\"a\"=\"A\"", true)]
    [InlineData("\"a\"<\"B\"", true)]
    [InlineData("\"1\">1", true)]
    [InlineData("TRUE>\"zzz\"", true)]
    [InlineData("1&2", "12")]
    [InlineData("\"x\"&TRUE", "xTRUE")]
    [InlineData("1.5&\"\"", "1.5")]
    [InlineData("(1/3)&\"\"", "0.333333333333333")]
    [InlineData("123456789012345678&\"\"", "1.23456789012346E+17")]
    public void Operators_follow_Excel(string formula, object expected) => AssertValue(expected, Evaluate(formula));

    [Theory]
    [InlineData("1/0", CellError.DivisionByZero)]
    [InlineData("\"abc\"+1", CellError.Value)]
    [InlineData("#N/A+1/0", CellError.NotAvailable)]
    [InlineData("1/0+#N/A", CellError.DivisionByZero)]
    [InlineData("0^0", CellError.Number)]
    [InlineData("(-8)^0.5", CellError.Number)]
    [InlineData("A1:A2 C1:C2", CellError.Null)]
    [InlineData("UNKNOWNFUNC(1)", CellError.Name)]
    [InlineData("NotDefinedName", CellError.Name)]
    [InlineData("#REF!", CellError.Reference)]
    [InlineData("'Fehlt'!A1", CellError.Reference)]
    public void Errors_propagate(string formula, CellError expected) => AssertValue(expected, Evaluate(formula));

    [Fact]
    public void References_read_cells_and_blank_is_zero()
    {
        AssertValue(5, Evaluate("A1+B1", ("A1", 2), ("B1", 3)));
        AssertValue(0, Evaluate("A1", ("B1", 3)));
        AssertValue("", Evaluate("A1&\"\""));
        AssertValue(6, Evaluate("SUM(A1:B2)", ("A1", 1), ("B1", 2), ("A2", 3)));
        AssertValue(3, Evaluate("SUM(A1:B2 B1:C3)", ("A1", 1), ("B1", 2), ("B2", 1)));
        AssertValue(4, Evaluate("SUM((A1,B2:B3))", ("A1", 1), ("B2", 1), ("B3", 2)));
    }

    [Fact]
    public void Legacy_formulas_intersect_ranges_with_their_row()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 10), ("A2", 20), ("A3", 30));
        sheet.SetFormula(1, 2, ThixCalc.Core.Formulas.FormulaParser.Parse("A1:A3*2", workbook, sheet, new CellAddress(1, 2)), dynamicArray: false);
        workbook.Calculation.Recalculate();
        Assert.Equal(40, sheet["C2"].Number);
        Assert.True(sheet["C3"].IsBlank);
    }

    [Fact]
    public void Dynamic_arrays_spill_and_report_spill_errors()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 1), ("A2", 2), ("A3", 3), ("C1", "=A1:A3*10"));
        workbook.Calculation.Recalculate();
        Assert.Equal(10, sheet["C1"].Number);
        Assert.Equal(20, sheet["C2"].Number);
        Assert.Equal(30, sheet["C3"].Number);
        Assert.True(sheet.GetCell(1, 2).IsSpilled);
        Assert.Equal(new CellAddress(0, 2), sheet.FindArrayAnchor(2, 2));

        // A value in the way: #SPILL!, the other cells are cleared.
        Set(sheet, "C3", "im Weg");
        workbook.Calculation.Recalculate();
        Assert.Equal(CellError.Spill, sheet["C1"].Error);
        Assert.True(sheet["C2"].IsBlank);

        // Removing it lets the array spill again.
        Set(sheet, "C3", null);
        workbook.Calculation.Recalculate();
        Assert.Equal(30, sheet["C3"].Number);

        // A formula reading a spilled cell follows the array.
        Set(sheet, "E1", "=C3+1");
        workbook.Calculation.Recalculate();
        Assert.Equal(31, sheet["E1"].Number);
        Set(sheet, "A3", 5);
        workbook.Calculation.Recalculate();
        Assert.Equal(51, sheet["E1"].Number);
    }

    [Fact]
    public void Shrinking_spills_clear_their_old_cells()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 3), ("B1", "=SEQUENCE(A1)"));
        workbook.Calculation.Recalculate();
        Assert.Equal(3, sheet["B3"].Number);
        Set(sheet, "A1", 1);
        workbook.Calculation.Recalculate();
        Assert.True(sheet["B2"].IsBlank);
        Assert.True(sheet["B3"].IsBlank);
        Assert.Equal(1, sheet["B1"].Number);
    }

    [Fact]
    public void Functions_lift_over_arrays_in_dynamic_formulas()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", -2), ("A2", -4), ("B1", "=ABS(A1:A2)"));
        workbook.Calculation.Recalculate();
        Assert.Equal(2, sheet["B1"].Number);
        Assert.Equal(4, sheet["B2"].Number);
    }

    [Fact]
    public void Implicit_intersection_operator()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 1), ("A2", 2), ("A3", 3), ("B2", "=@A1:A3"));
        workbook.Calculation.Recalculate();
        Assert.Equal(2, sheet["B2"].Number);
    }

    [Fact]
    public void Defined_names_and_lambdas()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        workbook.Names.Add(new DefinedName("Satz", "0.19"));
        workbook.Names.Add(new DefinedName("Werte", "Tabelle1!$A$1:$A$3"));
        workbook.Names.Add(new DefinedName("Brutto", "LAMBDA(x,x*(1+Satz))"));
        Fill(sheet, ("A1", 1), ("A2", 2), ("A3", 3), ("B1", "=SUM(Werte)*Satz"), ("B2", "=Brutto(100)"), ("B3", "=LET(a,2,b,a*a,b+1)"));
        workbook.Calculation.Rebuild();
        workbook.Calculation.Recalculate();
        Assert.Equal(6 * 0.19, sheet["B1"].Number, 12);
        Assert.Equal(119, sheet["B2"].Number, 12);
        Assert.Equal(5, sheet["B3"].Number);

        Set(sheet, "A1", 11);
        workbook.Calculation.Recalculate();
        Assert.Equal(16 * 0.19, sheet["B1"].Number, 12);
    }

    [Fact]
    public void Three_dimensional_references_span_sheets()
    {
        var workbook = Create("Jan", "Feb", "Mär", "Summe");
        foreach (var (name, value) in new[] { ("Jan", 1.0), ("Feb", 2.0), ("Mär", 4.0) })
        {
            Set(workbook[name], "B2", value);
        }

        Set(workbook["Summe"], "A1", "=SUM(Jan:Mär!B2)");
        workbook.Calculation.Recalculate();
        Assert.Equal(7, workbook["Summe"]["A1"].Number);

        Set(workbook["Feb"], "B2", 10);
        workbook.Calculation.Recalculate();
        Assert.Equal(15, workbook["Summe"]["A1"].Number);
    }
}
