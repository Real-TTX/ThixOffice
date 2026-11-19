using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Functions;

/// <summary>
/// The functions that completed the library: securities, Bessel, complex trigonometry, regular expressions, East Asian text,
/// XML, grouping (GROUPBY/PIVOTBY), exponential smoothing, eta lambdas and the functions that cannot run offline – with the
/// results of Microsoft's function reference.
/// </summary>
public class LibraryCompletionTests
{
    [Theory]
    [InlineData("COUPDAYBS(DATE(2011,1,25),DATE(2011,11,15),2,1)", 71.0)]
    [InlineData("COUPDAYS(DATE(2011,1,25),DATE(2011,11,15),2,1)", 181.0)]
    [InlineData("COUPDAYSNC(DATE(2011,1,25),DATE(2011,11,15),2,1)", 110.0)]
    [InlineData("COUPNCD(DATE(2011,1,25),DATE(2011,11,15),2,1)", 40678.0)]
    [InlineData("COUPNUM(DATE(2007,1,25),DATE(2008,11,15),2,1)", 4.0)]
    [InlineData("COUPPCD(DATE(2011,1,25),DATE(2011,11,15),2,1)", 40497.0)]
    [InlineData("PRICE(DATE(2008,2,15),DATE(2017,11,15),0.0575,0.065,100,2,0)", 94.6343616213221)]
    [InlineData("YIELD(DATE(2008,2,15),DATE(2016,11,15),0.0575,95.04287,100,2,0)", 0.0650000068807314)]
    [InlineData("DURATION(DATE(2018,7,1),DATE(2048,1,1),0.08,0.09,2,1)", 10.9191452815919)]
    [InlineData("MDURATION(DATE(2008,1,1),DATE(2016,1,1),0.08,0.09,2,1)", 5.73566981391884)]
    [InlineData("PRICEMAT(DATE(2008,2,15),DATE(2008,4,13),DATE(2007,11,11),0.061,0.061,0)", 99.9844988755431)]
    [InlineData("YIELDMAT(DATE(2008,3,15),DATE(2008,11,3),DATE(2007,11,8),0.0625,100.0123,0)", 0.0609543336915725)]
    [InlineData("ODDFPRICE(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),0.0785,0.0625,100,2,1)", 113.597717474079)]
    [InlineData("ODDFYIELD(DATE(2008,11,11),DATE(2021,3,1),DATE(2008,10,15),DATE(2009,3,1),0.0575,84.5,100,2,0)", 0.0772455415972989)]
    [InlineData("ODDLPRICE(DATE(2008,2,7),DATE(2008,6,15),DATE(2007,10,15),0.0375,0.0405,100,2,0)", 99.8782860147213)]
    [InlineData("ODDLYIELD(DATE(2008,4,20),DATE(2008,6,15),DATE(2007,12,24),0.0375,99.875,100,2,0)", 0.0451922356291692)]
    [InlineData("AMORDEGRC(2400,DATE(2008,8,19),DATE(2008,12,31),300,1,0.15,1)", 776.0)]
    [InlineData("AMORLINC(2400,DATE(2008,8,19),DATE(2008,12,31),300,1,0.15,1)", 360.0)]
    [InlineData("PRICE(DATE(2008,2,15),DATE(2008,2,15),0.0575,0.065,100,2,0)", CellError.Number)]
    [InlineData("COUPNUM(DATE(2011,1,25),DATE(2011,11,15),3,1)", CellError.Number)]
    public void Securities(string formula, object expected) => AssertValue(expected, Evaluate(formula), 1e-8);

    [Theory]
    [InlineData("BESSELJ(1.9,2)", 0.329925727692387)]
    [InlineData("BESSELI(1.5,1)", 0.981666428477166)]
    [InlineData("BESSELK(1.5,1)", 0.277387800456844)]
    [InlineData("BESSELY(2.5,1)", 0.145918137966786)]
    [InlineData("BESSELJ(10,0)", -0.245935764451348)]
    [InlineData("BESSELY(0.1,0)", -1.53423865135037)]
    [InlineData("BESSELK(-1,1)", CellError.Number)]
    [InlineData("IMREAL(IMTAN(\"4+3i\"))", 0.00490825806749606)]
    [InlineData("IMAGINARY(IMTAN(\"4+3i\"))", 1.00070953606723)]
    [InlineData("IMREAL(IMSINH(\"4+3i\"))", -27.0168132580039)]
    [InlineData("IMREAL(IMCOSH(\"4+3i\"))", -27.0349456030742)]
    [InlineData("IMREAL(IMSEC(\"4+3i\"))", -0.0652940278579471)]
    [InlineData("IMAGINARY(IMCOT(\"4+3i\"))", -0.999266927805902)]
    [InlineData("IMREAL(IMCSCH(\"1+i\"))", 0.303931001628426)]
    [InlineData("IMAGINARY(IMSECH(\"4+3i\"))", -0.00516434460775318)]
    [InlineData("IMREAL(IMCSC(\"4+3i\"))", -0.0754898329158637)]
    public void Engineering(string formula, object expected) => AssertValue(expected, Evaluate(formula), 1e-9);

    [Theory]
    [InlineData("REGEXTEST(\"Hallo Welt\",\"^h\")", false)]
    [InlineData("REGEXTEST(\"Hallo Welt\",\"^h\",1)", true)]
    [InlineData("REGEXEXTRACT(\"Bestellung 4711 vom Mai\",\"[0-9]+\")", "4711")]
    [InlineData("REGEXEXTRACT(\"Bestellung\",\"[0-9]+\")", CellError.NotAvailable)]
    [InlineData("INDEX(REGEXEXTRACT(\"a1 b22 c333\",\"[0-9]+\",1),3)", "333")]
    [InlineData("INDEX(REGEXEXTRACT(\"Max Mustermann\",\"(\\w+) (\\w+)\",2),2)", "Mustermann")]
    [InlineData("REGEXREPLACE(\"a1b2c3\",\"[0-9]\",\"#\")", "a#b#c#")]
    [InlineData("REGEXREPLACE(\"a1b2c3\",\"[0-9]\",\"#\",2)", "a1b#c3")]
    [InlineData("REGEXREPLACE(\"a1b2c3\",\"[0-9]\",\"#\",-1)", "a1b2c#")]
    [InlineData("REGEXREPLACE(\"Mustermann, Max\",\"(\\w+), (\\w+)\",\"$2 $1\")", "Max Mustermann")]
    [InlineData("REGEXTEST(\"x\",\"[\")", CellError.Value)]
    [InlineData("JIS(\"abc 12\")", "ａｂｃ　１２")]
    [InlineData("ASC(\"ＡＢＣ１\")", "ABC1")]
    [InlineData("BAHTTEXT(1234)", "หนึ่งพันสองร้อยสามสิบสี่บาทถ้วน")]
    [InlineData("BAHTTEXT(21.5)", "ยี่สิบเอ็ดบาทห้าสิบสตางค์")]
    [InlineData("BAHTTEXT(0)", "ศูนย์บาทถ้วน")]
    [InlineData("INDEX(FILTERXML(\"<a><b>1</b><b>x</b></a>\",\"//b\"),2)", "x")]
    [InlineData("FILTERXML(\"<a><b>1,5</b></a>\",\"//b\")", "1,5")]
    [InlineData("FILTERXML(\"<a><b>2.5</b></a>\",\"//b\")*2", 5.0)]
    [InlineData("FILTERXML(\"<a>\",\"//b\")", CellError.Value)]
    [InlineData("WEBSERVICE(\"https://example.com\")", CellError.Value)]
    [InlineData("PERCENTOF({1,2},{1,2,3,4})", 0.3)]
    [InlineData("INFO(\"numfile\")", 1.0)]
    [InlineData("INFO(\"release\")", "16.0")]
    [InlineData("SUM(BYROW({1,2;3,4},SUM))", 10.0)]
    [InlineData("INDEX(BYROW({1,2;3,4},MAX),2)", 4.0)]
    [InlineData("REDUCE(0,{1,2,3},LAMBDA(a,b,a+b))", 6.0)]
    public void Text_web_and_lambdas(string formula, object expected) => AssertValue(expected, Evaluate(formula));

    [Fact]
    public void Trimrange_drops_empty_outer_rows_and_columns()
    {
        AssertValue(4.0, Evaluate("ROWS(TRIMRANGE(A1:C10))", ("A2", 1), ("B5", 2)));
        AssertValue(2.0, Evaluate("COLUMNS(TRIMRANGE(A1:C10))", ("A2", 1), ("B5", 2)));
        AssertValue(5.0, Evaluate("ROWS(TRIMRANGE(A1:C10,2))", ("A2", 1), ("B5", 2)));
    }

    [Fact]
    public void Groupby_groups_aggregates_sorts_and_totals()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "Obst"), ("B1", "Menge"), ("A2", "Birne"), ("B2", 2), ("A3", "Apfel"), ("B3", 3), ("A4", "apfel"), ("B4", 5), ("A5", "Kirsche"), ("B5", 1));
        Set(sheet, "D1", "=GROUPBY(A1:A5,B1:B5,SUM)");
        Set(sheet, "G1", "=GROUPBY(A2:A5,B2:B5,LAMBDA(x,MAX(x)),0,0,-2)");
        workbook.Calculation.Recalculate();
        Assert.Equal("Obst", sheet["D1"].Text);
        Assert.Equal("Menge", sheet["E1"].Text);
        Assert.Equal("Apfel", sheet["D2"].Text);
        Assert.Equal(8.0, sheet["E2"].Number);
        Assert.Equal("Birne", sheet["D3"].Text);
        Assert.Equal("Kirsche", sheet["D4"].Text);
        Assert.Equal("Gesamtergebnis", sheet["D5"].Text);
        Assert.Equal(11.0, sheet["E5"].Number);

        // Sorted by the values descending, no totals.
        Assert.Equal("Apfel", sheet["G1"].Text);
        Assert.Equal(5.0, sheet["H1"].Number);
        Assert.Equal("Birne", sheet["G2"].Text);
        Assert.Equal("Kirsche", sheet["G3"].Text);
        Assert.True(sheet["G4"].IsBlank);
    }

    [Fact]
    public void Pivotby_crosses_rows_and_columns()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "Nord"), ("B1", "Q1"), ("C1", 10), ("A2", "Süd"), ("B2", "Q1"), ("C2", 20), ("A3", "Nord"), ("B3", "Q2"), ("C3", 5), ("A4", "Nord"), ("B4", "Q1"), ("C4", 1));
        Set(sheet, "E1", "=PIVOTBY(A1:A4,B1:B4,C1:C4,SUM)");
        workbook.Calculation.Recalculate();
        Assert.Equal("Q1", sheet["F1"].Text);
        Assert.Equal("Q2", sheet["G1"].Text);
        Assert.Equal("Gesamtergebnis", sheet["H1"].Text);
        Assert.Equal("Nord", sheet["E2"].Text);
        Assert.Equal(11.0, sheet["F2"].Number);
        Assert.Equal(5.0, sheet["G2"].Number);
        Assert.Equal(16.0, sheet["H2"].Number);
        Assert.Equal("Süd", sheet["E3"].Text);
        Assert.Equal(string.Empty, sheet["G3"].Text);
        Assert.Equal("Gesamtergebnis", sheet["E4"].Text);
        Assert.Equal(36.0, sheet["H4"].Number);
    }

    [Fact]
    public void Exponential_smoothing_follows_trend_and_season()
    {
        var cells = new List<(string, object?)>();
        for (var index = 1; index <= 24; index++)
        {
            cells.Add(($"A{index}", index));
            cells.Add(($"B{index}", 100 + 2 * index + (index % 4 == 0 ? 10 : 0)));
        }

        var all = cells.ToArray();
        AssertValue(4.0, Evaluate("FORECAST.ETS.SEASONALITY(B1:B24,A1:A24)", all));
        var forecast = Evaluate("FORECAST.ETS(28,B1:B24,A1:A24)", all);
        Assert.InRange(forecast.Number, 164, 168); // 100 + 56 + 10
        var linear = Evaluate("FORECAST.ETS(26,B1:B24,A1:A24,0)", all);
        Assert.InRange(linear.Number, 148, 160);
        Assert.True(Evaluate("FORECAST.ETS.CONFINT(28,B1:B24,A1:A24)", all).Number >= 0);
        AssertValue(1.0, Evaluate("FORECAST.ETS.STAT(B1:B24,A1:A24,8)", all));
        AssertValue(CellError.Number, Evaluate("FORECAST.ETS(10,B1:B24,A1:A24)", all));
    }

    [Fact]
    public void Eta_lambdas_are_written_with_the_file_prefix_and_shown_localized()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        var formula = FormulaParser.Parse("=BYROW(A1:B2,SUM)", workbook, sheet, default);
        Assert.Equal("BYROW(A1:B2,_xleta.SUM)", FormulaFormatter.Format(formula, workbook, default, FormulaSyntax.FileFormat).Replace("_xlfn.", string.Empty, StringComparison.Ordinal));
        Assert.Equal("NACHZEILE(A1:B2;SUMME)", FormulaFormatter.Format(formula, workbook, default, FormulaSyntax.German));
        var read = FormulaParser.Parse("=_xlfn.BYROW(A1:B2,_xleta.SUM)", workbook, sheet, default);
        Assert.Equal("NACHZEILE(A1:B2;SUMME)", FormulaFormatter.Format(read, workbook, default, FormulaSyntax.German));

        // A LET variable named like a function stays a variable.
        AssertValue(3.0, Evaluate("LET(max,2,max+1)"));
    }

    [Fact]
    public void Implicit_intersection_and_spill_references_parse_and_round_trip()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 1), ("A2", 2), ("A3", 3));
        Set(sheet, "B1", "=SEQUENCE(3)");
        Set(sheet, "C2", "=@A1:A3*10");
        Set(sheet, "D1", "=SUM(B1#)");
        workbook.Calculation.Recalculate();
        Assert.Equal(20.0, sheet["C2"].Number);
        Assert.Equal(6.0, sheet["D1"].Number);
        var at = sheet.GetFormula(1, 2)!;
        Assert.Equal("@A1:A3*10", FormulaFormatter.Format(at, workbook, new CellAddress(1, 2), FormulaSyntax.German));
        var spill = sheet.GetFormula(0, 3)!;
        Assert.Equal("SUMME(B1#)", FormulaFormatter.Format(spill, workbook, new CellAddress(0, 3), FormulaSyntax.German));
        Assert.Equal("SUM(_xlfn.ANCHORARRAY(B1))", FormulaFormatter.Format(spill, workbook, new CellAddress(0, 3), FormulaSyntax.FileFormat));
        var read = FormulaParser.Parse("=_xlfn.SINGLE(A1:A3)*10", workbook, sheet, new CellAddress(1, 2));
        Assert.Equal("@A1:A3*10", FormulaFormatter.Format(read, workbook, new CellAddress(1, 2), FormulaSyntax.German));
    }

    [Fact]
    public void Functions_that_need_the_network_keep_the_value_of_their_file()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Set(sheet, "A1", "MSFT");
        var formula = FormulaParser.Parse("=STOCKHISTORY(A1,TODAY())", workbook, sheet, new CellAddress(0, 1));
        sheet.LoadFormula(0, 1, formula, 412.5, 0, dynamicArray: true, arrayRange: null, spillRange: null);
        workbook.Calculation.Rebuild();
        sheet.SetValue("A1", "AAPL");
        workbook.Calculation.Recalculate();
        Assert.Equal(412.5, sheet["B1"].Number);
        Assert.Equal("_xlfn.STOCKHISTORY(A1,_xlfn.TODAY())".Replace("_xlfn.TODAY", "TODAY", StringComparison.Ordinal), FormulaFormatter.Format(formula, workbook, new CellAddress(0, 1), FormulaSyntax.FileFormat));

        // A new one (no value of a file) shows #DATEN_ABRUFEN.
        AssertValue(CellError.GettingData, Evaluate("CUBEVALUE(\"Verbindung\",\"[Measures].[Umsatz]\")"));
    }
}
