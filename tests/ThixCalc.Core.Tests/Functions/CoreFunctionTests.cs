using ThixCalc.Core.Functions;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Functions;

/// <summary>Math, statistics, logic, lookup and dynamic array functions, table-driven with Excel's documented results.</summary>
public class CoreFunctionTests
{
    private static readonly (string, object?)[] Numbers =
    [
        ("A1", 10), ("A2", 7), ("A3", 9), ("A4", 27), ("A5", 2), ("A6", "Text"), ("A7", true),
        ("B1", 1), ("B2", 2), ("B3", 3), ("B4", 4), ("B5", 5),
        ("C1", "Apfel"), ("C2", "Birne"), ("C3", "Apfel"), ("C4", "Kirsche"), ("C5", "apfel"),
    ];

    [Theory]
    [InlineData("SUM(A1:A7)", 55.0)]
    [InlineData("SUM(1,\"2\",TRUE)", 4.0)]
    [InlineData("SUM(A1:A7,\"x\")", CellError.Value)]
    [InlineData("SUMIF(C1:C5,\"Apfel\",B1:B5)", 9.0)]
    [InlineData("SUMIF(A1:A5,\">8\")", 46.0)]
    [InlineData("SUMIF(C1:C5,\"A*\",B1:B5)", 9.0)]
    [InlineData("SUMIF(C1:C5,\"?irne\",B1:B5)", 2.0)]
    [InlineData("SUMIFS(B1:B5,C1:C5,\"Apfel\",A1:A5,\">9\")", 1.0)]
    [InlineData("SUMPRODUCT(A1:A5,B1:B5)", 169.0)]
    [InlineData("SUMPRODUCT((C1:C5=\"Apfel\")*B1:B5)", 9.0)]
    [InlineData("SUMSQ(3,4)", 25.0)]
    [InlineData("SUMX2MY2({2,3},{1,1})", 11.0)]
    [InlineData("PRODUCT(B1:B5)", 120.0)]
    [InlineData("ROUND(2.15,1)", 2.2)]
    [InlineData("ROUND(2.149,1)", 2.1)]
    [InlineData("ROUND(-1.475,2)", -1.48)]
    [InlineData("ROUND(21.5,-1)", 20.0)]
    [InlineData("ROUND(626.3,-3)", 1000.0)]
    [InlineData("ROUND(2.675,2)", 2.68)]
    [InlineData("ROUNDUP(3.2,0)", 4.0)]
    [InlineData("ROUNDUP(-3.14159,1)", -3.2)]
    [InlineData("ROUNDUP(31415.92654,-2)", 31500.0)]
    [InlineData("ROUNDDOWN(3.14159,3)", 3.141)]
    [InlineData("ROUNDDOWN(-3.14159,1)", -3.1)]
    [InlineData("TRUNC(-8.9)", -8.0)]
    [InlineData("INT(-8.9)", -9.0)]
    [InlineData("MOD(3,2)", 1.0)]
    [InlineData("MOD(-3,2)", 1.0)]
    [InlineData("MOD(3,-2)", -1.0)]
    [InlineData("MOD(3,0)", CellError.DivisionByZero)]
    [InlineData("QUOTIENT(-10,3)", -3.0)]
    [InlineData("ABS(-4)", 4.0)]
    [InlineData("SIGN(-0.00001)", -1.0)]
    [InlineData("POWER(98.6,3.2)", 2401077.22206958)]
    [InlineData("SQRT(16)", 4.0)]
    [InlineData("SQRT(-16)", CellError.Number)]
    [InlineData("EXP(1)", 2.71828182845905)]
    [InlineData("LN(86)", 4.45434729625351)]
    [InlineData("LOG(8,2)", 3.0)]
    [InlineData("LOG10(86)", 1.93449845124357)]
    [InlineData("CEILING(2.5,1)", 3.0)]
    [InlineData("CEILING(-2.5,-2)", -4.0)]
    [InlineData("CEILING(-2.5,2)", -2.0)]
    [InlineData("CEILING(1.5,0.1)", 1.5)]
    [InlineData("CEILING(0.234,0.01)", 0.24)]
    [InlineData("CEILING(2.5,-2)", CellError.Number)]
    [InlineData("FLOOR(3.7,2)", 2.0)]
    [InlineData("FLOOR(-2.5,-2)", -2.0)]
    [InlineData("FLOOR(2.5,-2)", CellError.Number)]
    [InlineData("FLOOR(1.58,0.1)", 1.5)]
    [InlineData("CEILING.MATH(-5.5,2,-1)", -6.0)]
    [InlineData("CEILING.MATH(-8.1,2)", -8.0)]
    [InlineData("FLOOR.MATH(-5.5,2,-1)", -4.0)]
    [InlineData("FLOOR.MATH(-3.2,-1)", -4.0)]
    [InlineData("CEILING.PRECISE(-4.3)", -4.0)]
    [InlineData("FLOOR.PRECISE(-3.2,-1)", -4.0)]
    [InlineData("MROUND(10,3)", 9.0)]
    [InlineData("MROUND(-10,-3)", -9.0)]
    [InlineData("MROUND(1.3,0.2)", 1.4)]
    [InlineData("MROUND(5,-2)", CellError.Number)]
    [InlineData("EVEN(1.5)", 2.0)]
    [InlineData("EVEN(-1)", -2.0)]
    [InlineData("ODD(1.5)", 3.0)]
    [InlineData("ODD(2)", 3.0)]
    [InlineData("FACT(5)", 120.0)]
    [InlineData("FACT(-1)", CellError.Number)]
    [InlineData("FACTDOUBLE(7)", 105.0)]
    [InlineData("COMBIN(8,2)", 28.0)]
    [InlineData("COMBINA(4,3)", 20.0)]
    [InlineData("GCD(24,36)", 12.0)]
    [InlineData("LCM(24,36)", 72.0)]
    [InlineData("MULTINOMIAL(2,3,4)", 1260.0)]
    [InlineData("PI()", 3.14159265358979)]
    [InlineData("DEGREES(PI())", 180.0)]
    [InlineData("RADIANS(270)", 4.71238898038469)]
    [InlineData("SIN(PI()/2)", 1.0)]
    [InlineData("COS(1.047)", 0.50017107459707)]
    [InlineData("TAN(0.785)", 0.99920399010655)]
    [InlineData("ATAN2(1,1)", 0.785398163397448)]
    [InlineData("ACOS(-0.5)", 2.0943951023932)]
    [InlineData("ASINH(-2.5)", -1.6472311463711)]
    [InlineData("ROMAN(499)", "CDXCIX")]
    [InlineData("ARABIC(\"LVII\")", 57.0)]
    [InlineData("BASE(7,2)", "111")]
    [InlineData("BASE(15,2,10)", "0000001111")]
    [InlineData("DECIMAL(\"FF\",16)", 255.0)]
    [InlineData("SUBTOTAL(9,A1:A5)", 55.0)]
    [InlineData("SUBTOTAL(1,B1:B5)", 3.0)]
    [InlineData("AGGREGATE(14,6,A1:A5,2)", 10.0)]
    [InlineData("AGGREGATE(4,6,{1,#DIV/0!,5})", 5.0)]
    [InlineData("MDETERM({3,6,1;1,1,0;3,10,2})", 1.0)]
    [InlineData("SUM(MMULT({1,2;3,4},{1;1}))", 10.0)]
    [InlineData("SUM(MINVERSE({4,-1;2,0}))", 1.5)]
    [InlineData("SERIESSUM(PI()/4,0,2,{1,-0.5,0.0416666666666667,-0.00138888888888889})", 0.707103214822846)]
    public void Math(string formula, object expected) => AssertValue(expected, Evaluate(formula, Numbers), 1e-9);

    [Theory]
    [InlineData("COUNT(A1:A7)", 5.0)]
    [InlineData("COUNT(1,\"2\",\"x\",TRUE)", 3.0)]
    [InlineData("COUNTA(A1:A7,C1:C5)", 12.0)]
    [InlineData("COUNTBLANK(A1:A10)", 3.0)]
    [InlineData("COUNTIF(C1:C5,\"apfel\")", 3.0)]
    [InlineData("COUNTIF(A1:A5,\">=9\")", 3.0)]
    [InlineData("COUNTIF(A1:A10,\"\")", 3.0)]
    [InlineData("COUNTIF(A1:A10,\"<>\")", 7.0)]
    [InlineData("COUNTIF(A:A,\"<>Text\")", 1048575.0)]
    [InlineData("COUNTIFS(C1:C5,\"Apfel\",B1:B5,\">1\")", 2.0)]
    [InlineData("AVERAGE(A1:A5)", 11.0)]
    [InlineData("AVERAGE(A6)", CellError.DivisionByZero)]
    [InlineData("AVERAGEA(A1:A7)", 8.0)]
    [InlineData("AVERAGEIF(C1:C5,\"Apfel\",B1:B5)", 3.0)]
    [InlineData("AVERAGEIFS(B1:B5,C1:C5,\"Apfel\",A1:A5,\"<5\")", 5.0)]
    [InlineData("MAX(A1:A7)", 27.0)]
    [InlineData("MAX(A6)", 0.0)]
    [InlineData("MAXA(A6:A7)", 1.0)]
    [InlineData("MIN(A1:A5,1)", 1.0)]
    [InlineData("MAXIFS(A1:A5,C1:C5,\"Apfel\")", 10.0)]
    [InlineData("MINIFS(A1:A5,C1:C5,\"Apfel\")", 2.0)]
    [InlineData("MEDIAN(1,2,3,4,5,6)", 3.5)]
    [InlineData("MODE.SNGL(5.6,4,4,3,2,4)", 4.0)]
    [InlineData("MODE(1,2,3)", CellError.NotAvailable)]
    [InlineData("STDEV.S(1345,1301,1368,1322,1310,1370,1318,1350,1303,1299)", 27.4639157198435)]
    [InlineData("STDEV.P(1345,1301,1368,1322,1310,1370,1318,1350,1303,1299)", 26.0545581424825)]
    [InlineData("VAR.S(1345,1301,1368,1322,1310,1370,1318,1350,1303,1299)", 754.266666666667)]
    [InlineData("VAR.P(1345,1301,1368,1322,1310,1370,1318,1350,1303,1299)", 678.84)]
    [InlineData("DEVSQ(4,5,8,7,11,4,3)", 48.0)]
    [InlineData("AVEDEV(4,5,6,7,5,4,3)", 1.02040816326531)]
    [InlineData("GEOMEAN(4,5,8,7,11,4,3)", 5.47698696965696)]
    [InlineData("HARMEAN(4,5,8,7,11,4,3)", 5.02837596206418)]
    [InlineData("TRIMMEAN({4,5,6,7,2,3,4,5,1,2,3},0.2)", 3.77777777777778)]
    [InlineData("KURT(3,4,5,2,3,4,5,6,4,7)", -0.151799637208351)]
    [InlineData("SKEW(3,4,5,2,3,4,5,6,4,7)", 0.359543071407364)]
    [InlineData("SKEW.P(3,4,5,2,3,4,5,6,4,7)", 0.303193339354144)]
    [InlineData("LARGE({3,5,3,5,4},3)", 4.0)]
    [InlineData("SMALL({3,4,5,2,3,4,6,4,7},4)", 4.0)]
    [InlineData("LARGE({1,2},3)", CellError.Number)]
    [InlineData("RANK.EQ(7,A1:A5)", 4.0)]
    [InlineData("RANK.EQ(7,A1:A5,1)", 2.0)]
    [InlineData("RANK.AVG(94,{89,88,92,101,94,97,95})", 4.0)]
    [InlineData("RANK(3,A1:A5)", CellError.NotAvailable)]
    [InlineData("PERCENTILE.INC({1,3,2,4},0.3)", 1.9)]
    [InlineData("PERCENTILE.EXC({1,2,3,6,6,6,7,8,9},0.25)", 2.5)]
    [InlineData("QUARTILE.INC({1,2,4,7,8,9,10,12},1)", 3.5)]
    [InlineData("QUARTILE.EXC({6,7,15,36,39,40,41,42,43,47,49},1)", 15.0)]
    [InlineData("PERCENTRANK.INC({13,12,11,8,4,3,2,1,1,1},2)", 0.333)]
    [InlineData("PERCENTRANK.INC({13,12,11,8,4,3,2,1,1,1},8)", 0.666)]
    [InlineData("PERCENTRANK.EXC({1,2,3,6,6,6,7,8,9},7)", 0.7)]
    [InlineData("CORREL({3,2,4,5,6},{9,7,12,15,17})", 0.997054485501581)]
    [InlineData("RSQ({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", 0.0579501915708812)]
    [InlineData("SLOPE({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", 0.305555555555556)]
    [InlineData("INTERCEPT({2,3,9,1,8},{6,5,11,7,5})", 0.0483870967741935)]
    [InlineData("STEYX({2,3,9,1,8,7,5},{6,5,11,7,5,4,4})", 3.30571895021004)]
    [InlineData("FORECAST.LINEAR(30,{6,7,9,15,21},{20,28,31,38,40})", 10.6072530864198)]
    [InlineData("COVARIANCE.P({3,2,4,5,6},{9,7,12,15,17})", 5.2)]
    [InlineData("COVARIANCE.S({2,4,8},{5,11,12})", 9.66666666666667)]
    [InlineData("STANDARDIZE(42,40,1.5)", 1.33333333333333)]
    [InlineData("FISHER(0.75)", 0.972955074527657)]
    [InlineData("FISHERINV(0.972955)", 0.749999967185204)]
    [InlineData("PERMUT(100,3)", 970200.0)]
    [InlineData("PERMUTATIONA(3,2)", 9.0)]
    [InlineData("PROB({0,1,2,3},{0.2,0.3,0.1,0.4},2)", 0.1)]
    [InlineData("PROB({0,1,2,3},{0.2,0.3,0.1,0.4},1,3)", 0.8)]
    [InlineData("SUM(FREQUENCY({79,85,78,85,50,81,95,88,97},{70,79,89}))", 9.0)]
    [InlineData("INDEX(FREQUENCY({79,85,78,85,50,81,95,88,97},{70,79,89}),3)", 4.0)]
    [InlineData("INDEX(LINEST({1,9,5,7},{0,4,2,3}),1)", 2.0)]
    [InlineData("INDEX(TREND({1,9,5,7},{0,4,2,3},{5}),1)", 11.0)]
    public void Statistics(string formula, object expected) => AssertValue(expected, Evaluate(formula, Numbers), 1e-9);

    [Theory]
    [InlineData("IF(A1>5,\"groß\",\"klein\")", "groß")]
    [InlineData("IF(A1<5,\"groß\")", false)]
    [InlineData("IF(A1<5,,1)", 1.0)]
    [InlineData("IF(A1>5,,1)", 0.0)]
    [InlineData("IF(\"x\",1,2)", CellError.Value)]
    [InlineData("IF(TRUE,1,1/0)", 1.0)]
    [InlineData("IFS(A1>20,\"a\",A1>5,\"b\",TRUE,\"c\")", "b")]
    [InlineData("IFS(A1>20,\"a\")", CellError.NotAvailable)]
    [InlineData("IFERROR(1/0,\"Fehler\")", "Fehler")]
    [InlineData("IFERROR(A6,\"x\")", "Text")]
    [InlineData("IFNA(#N/A,0)", 0.0)]
    [InlineData("IFNA(1/0,0)", CellError.DivisionByZero)]
    [InlineData("AND(TRUE,A1>5,B1=1)", true)]
    [InlineData("AND(A6:A7)", true)]
    [InlineData("OR(FALSE,0)", false)]
    [InlineData("OR(A6)", CellError.Value)]
    [InlineData("XOR(TRUE,TRUE,TRUE)", true)]
    [InlineData("NOT(1)", false)]
    [InlineData("SWITCH(B2,1,\"eins\",2,\"zwei\",\"sonst\")", "zwei")]
    [InlineData("SWITCH(9,1,\"eins\")", CellError.NotAvailable)]
    [InlineData("CHOOSE(2,\"a\",\"b\",\"c\")", "b")]
    [InlineData("SUM(CHOOSE(2,A1:A2,B1:B2))", 3.0)]
    [InlineData("CHOOSE(4,1,2)", CellError.Value)]
    [InlineData("LET(x,5,y,x*2,x+y)", 15.0)]
    [InlineData("LAMBDA(x,y,x*y)(3,4)", 12.0)]
    [InlineData("REDUCE(0,B1:B5,LAMBDA(a,v,a+v*v))", 55.0)]
    [InlineData("SUM(MAP(B1:B3,LAMBDA(v,v*10)))", 60.0)]
    [InlineData("INDEX(SCAN(0,B1:B5,LAMBDA(a,v,a+v)),5)", 15.0)]
    [InlineData("SUM(MAKEARRAY(2,3,LAMBDA(r,c,r*c)))", 18.0)]
    [InlineData("INDEX(BYROW({1,2;3,4},LAMBDA(r,SUM(r))),2)", 7.0)]
    [InlineData("INDEX(BYCOL({1,2;3,4},LAMBDA(c,MAX(c))),2)", 4.0)]
    [InlineData("LAMBDA(a,[b],IF(ISOMITTED(b),a,a+b))(1)", 1.0)]
    [InlineData("ISBLANK(A9)", true)]
    [InlineData("ISNUMBER(A1)", true)]
    [InlineData("ISTEXT(C1)", true)]
    [InlineData("ISLOGICAL(A7)", true)]
    [InlineData("ISERR(#N/A)", false)]
    [InlineData("ISERROR(#N/A)", true)]
    [InlineData("ISNA(#N/A)", true)]
    [InlineData("ISEVEN(-1)", false)]
    [InlineData("ISODD(5)", true)]
    [InlineData("ISREF(A1)", true)]
    [InlineData("ISREF(1)", false)]
    [InlineData("ISNONTEXT(A1)", true)]
    [InlineData("N(TRUE)", 1.0)]
    [InlineData("N(\"7\")", 0.0)]
    [InlineData("TYPE(\"x\")", 2.0)]
    [InlineData("TYPE({1,2})", 64.0)]
    [InlineData("ERROR.TYPE(#N/A)", 7.0)]
    [InlineData("ERROR.TYPE(1)", CellError.NotAvailable)]
    [InlineData("SHEET()", 1.0)]
    [InlineData("SHEETS()", 1.0)]
    [InlineData("CELL(\"address\",B2)", "$B$2")]
    [InlineData("CELL(\"row\",B7)", 7.0)]
    [InlineData("CELL(\"type\",C1)", "l")]
    public void Logic_and_information(string formula, object expected) => AssertValue(expected, Evaluate(formula, Numbers), 1e-9);

    private static readonly (string, object?)[] Table =
    [
        ("E1", "Nr"), ("F1", "Name"), ("G1", "Preis"),
        ("E2", 101), ("F2", "Kabel"), ("G2", 4.5),
        ("E3", 102), ("F3", "Stecker"), ("G3", 2.25),
        ("E4", 104), ("F4", "Adapter"), ("G4", 7),
        ("E5", 107), ("F5", "Lampe"), ("G5", 12),
    ];

    [Theory]
    [InlineData("VLOOKUP(102,E2:G5,2,FALSE)", "Stecker")]
    [InlineData("VLOOKUP(103,E2:G5,3)", 2.25)]
    [InlineData("VLOOKUP(103,E2:G5,3,FALSE)", CellError.NotAvailable)]
    [InlineData("VLOOKUP(\"La*\",F2:G5,2,FALSE)", 12.0)]
    [InlineData("VLOOKUP(100,E2:G5,2)", CellError.NotAvailable)]
    [InlineData("VLOOKUP(102,E2:G5,4,FALSE)", CellError.Reference)]
    [InlineData("VLOOKUP(102,E2:G5,0,FALSE)", CellError.Value)]
    [InlineData("VLOOKUP(102,E:G,2,FALSE)", "Stecker")]
    [InlineData("HLOOKUP(\"Preis\",E1:G5,3,FALSE)", 2.25)]
    [InlineData("LOOKUP(105,E2:E5,F2:F5)", "Adapter")]
    [InlineData("LOOKUP(105,E2:G5)", 7.0)]
    [InlineData("MATCH(104,E2:E5,0)", 3.0)]
    [InlineData("MATCH(106,E2:E5)", 3.0)]
    [InlineData("MATCH(\"lampe\",F2:F5,0)", 4.0)]
    [InlineData("MATCH(5,{9,7,5,3},-1)", 3.0)]
    [InlineData("MATCH(1,E2:E5,0)", CellError.NotAvailable)]
    [InlineData("XMATCH(104,E2:E5)", 3.0)]
    [InlineData("XMATCH(105,E2:E5,1)", 4.0)]
    [InlineData("XMATCH(105,E2:E5,-1)", 3.0)]
    [InlineData("XMATCH(\"*pe\",F2:F5,2)", 4.0)]
    [InlineData("XMATCH(104,E2:E5,0,2)", 3.0)]
    [InlineData("XLOOKUP(104,E2:E5,F2:F5)", "Adapter")]
    [InlineData("XLOOKUP(999,E2:E5,F2:F5,\"fehlt\")", "fehlt")]
    [InlineData("XLOOKUP(105,E2:E5,G2:G5,,1)", 12.0)]
    [InlineData("XLOOKUP(\"Lampe\",F2:F5,E2:E5,,0,-1)", 107.0)]
    [InlineData("SUM(XLOOKUP(102,E2:E5,F2:G5))", 2.25)]
    [InlineData("INDEX(E2:G5,2,3)", 2.25)]
    [InlineData("INDEX({1,2;3,4},2,1)", 3.0)]
    [InlineData("SUM(INDEX(E2:G5,0,3))", 25.75)]
    [InlineData("INDEX(E2:G5,5,1)", CellError.Reference)]
    [InlineData("SUM(OFFSET(E1,1,2,4,1))", 25.75)]
    [InlineData("OFFSET(E1,-1,0)", CellError.Reference)]
    [InlineData("INDIRECT(\"G3\")", 2.25)]
    [InlineData("SUM(INDIRECT(\"G2:G5\"))", 25.75)]
    [InlineData("INDIRECT(\"R3C7\",FALSE)", 2.25)]
    [InlineData("INDIRECT(\"kein Bezug\")", CellError.Reference)]
    [InlineData("ROW(E3)", 3.0)]
    [InlineData("COLUMN(G1)", 7.0)]
    [InlineData("ROWS(E2:G5)", 4.0)]
    [InlineData("COLUMNS({1,2,3})", 3.0)]
    [InlineData("AREAS((E1:E2,G1))", 2.0)]
    [InlineData("ADDRESS(2,3)", "$C$2")]
    [InlineData("ADDRESS(2,3,2)", "C$2")]
    [InlineData("ADDRESS(2,3,1,FALSE,\"Mein Blatt\")", "'Mein Blatt'!R2C3")]
    [InlineData("ADDRESS(2,3,4,FALSE)", "R[2]C[3]")]
    [InlineData("SUM(TRANSPOSE({1,2,3}))", 6.0)]
    [InlineData("HYPERLINK(\"https://example.com\",\"Beispiel\")", "Beispiel")]
    public void Lookup(string formula, object expected) => AssertValue(expected, Evaluate(formula, Table), 1e-9);

    [Fact]
    public void Dynamic_array_functions_spill()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, Table);
        Fill(sheet, ("A1", "=FILTER(F2:F5,G2:G5>5)"), ("A4", "=SORT(G2:G5,1,-1)"), ("B1", "=UNIQUE({1;2;1;3})"),
            ("C1", "=SORTBY(F2:F5,G2:G5)"), ("D1", "=TAKE(E2:E5,-2)"), ("H1", "=VSTACK({1,2},{3})"), ("J1", "=SEQUENCE(2,3,10,5)"),
            ("A10", "=CHOOSECOLS(E2:G3,3,1)"), ("A12", "=TOCOL({1,2;3,4})"), ("C20", "=WRAPROWS({1,2,3,4,5},2,0)"),
            ("D10", "=FILTER(F2:F5,G2:G5>100,\"keine\")"), ("D11", "=FILTER(F2:F5,G2:G5>100)"), ("E10", "=DROP({1,2;3,4},1)"),
            ("E12", "=EXPAND({1},2,2,0)"), ("G10", "=HSTACK({1;2},{3})"));
        workbook.Calculation.Recalculate();
        Assert.Equal("Adapter", sheet["A1"].Text);
        Assert.Equal("Lampe", sheet["A2"].Text);
        Assert.Equal(new[] { 12.0, 7, 4.5, 2.25 }, new[] { "A4", "A5", "A6", "A7" }.Select(cell => sheet[cell].Number));
        Assert.Equal(new[] { 1.0, 2, 3 }, new[] { "B1", "B2", "B3" }.Select(cell => sheet[cell].Number));
        Assert.Equal("Stecker", sheet["C1"].Text);
        Assert.Equal("Lampe", sheet["C4"].Text);
        Assert.Equal(104, sheet["D1"].Number);
        Assert.Equal(107, sheet["D2"].Number);
        Assert.Equal(CellError.NotAvailable, sheet["I2"].Error);
        Assert.Equal(35, sheet["L2"].Number);
        Assert.Equal(4.5, sheet["A10"].Number);
        Assert.Equal(101, sheet["B10"].Number);
        Assert.Equal(5, sheet["C22"].Number);
        Assert.Equal(0, sheet["D22"].Number);
        Assert.Equal(4, sheet["A15"].Number);
        Assert.Equal("keine", sheet["D10"].Text);
        Assert.Equal(CellError.Calc, sheet["D11"].Error);
        Assert.Equal(3, sheet["E10"].Number);
        Assert.Equal(0, sheet["F13"].Number);
        Assert.Equal(CellError.NotAvailable, sheet["H11"].Error);
    }

    [Fact]
    public void The_library_is_large_and_every_function_has_a_German_name_or_keeps_its_own()
    {
        Assert.True(FunctionRegistry.Count >= 300, $"Only {FunctionRegistry.Count} functions");
        foreach (var function in FunctionRegistry.All)
        {
            var german = CalcLocale.German.FunctionName(function.Name);
            Assert.Equal(function.Name, CalcLocale.German.CanonicalFunctionName(german));
        }

        Assert.Equal("SUMME", CalcLocale.German.FunctionName("SUM"));
        Assert.Equal("SVERWEIS", CalcLocale.German.FunctionName("VLOOKUP"));
        Assert.Equal("XVERWEIS", CalcLocale.German.FunctionName("XLOOKUP"));
        Assert.Equal("ZÄHLENWENN", CalcLocale.German.FunctionName("COUNTIF"));
    }
}
