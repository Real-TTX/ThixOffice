using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Tests.Formulas;

/// <summary>Parsing and writing formulas: canonical round trips, the German locale, R1C1, precedence, errors.</summary>
public class ParserTests
{
    private static readonly CellAddress Origin = new(4, 3); // D5

    private static Workbook Book() => TestBooks.Create("Tabelle1", "Mein Blatt", "Daten", "2024");

    private static string RoundTrip(string text, FormulaSyntax? input = null, FormulaSyntax? output = null)
    {
        var workbook = Book();
        var formula = FormulaParser.Parse(text, workbook, workbook.Sheets[0], Origin, input);
        return FormulaFormatter.Format(formula, workbook, Origin, output ?? input);
    }

    [Theory]
    [InlineData("1+2*3")]
    [InlineData("(1+2)*3")]
    [InlineData("-2^2")]
    [InlineData("2^3^2")]
    [InlineData("A1:B3")]
    [InlineData("$A$1+A$1+$A1+A1")]
    [InlineData("SUM(A1:A10,B:B,3:3)")]
    [InlineData("'Mein Blatt'!A1*2")]
    [InlineData("Daten!$B$2:$C$9")]
    [InlineData("'2024'!A1")]
    [InlineData("SUM(Tabelle1:Daten!A1)")]
    [InlineData("IF(A1>=10,\"groß\",\"klein\")")]
    [InlineData("\"Er sagte \"\"Hallo\"\"\"")]
    [InlineData("{1,2,3;4,5,6}")]
    [InlineData("{\"a\",TRUE;#N/A,-1.5}")]
    [InlineData("A1 B1")]
    [InlineData("SUM((A1,B2))")]
    [InlineData("50%")]
    [InlineData("A1<>B1")]
    [InlineData("A1&\" \"&B1")]
    [InlineData("@A1:A10")]
    [InlineData("A1:INDEX(B:B,5)")]
    [InlineData("IF(A1,,2)")]
    [InlineData("TRUE()")]
    [InlineData("#REF!+1")]
    [InlineData("MyName*2")]
    [InlineData("Tabelle1[Umsatz]")]
    [InlineData("SUM(Tabelle1[[#Headers],[Name]])")]
    [InlineData("LET(x,2,y,x*3,x+y)")]
    [InlineData("LAMBDA(a,b,a+b)(1,2)")]
    [InlineData("UNKNOWNFUNC(1)")]
    [InlineData("[1]Sheet1!A1")]
    [InlineData("1E+20")]
    [InlineData("0.000001")]
    public void Canonical_text_round_trips(string text) => Assert.Equal(text, RoundTrip(text));

    [Theory]
    [InlineData("SUMME(A1:A3;2)", "SUM(A1:A3,2)")]
    [InlineData("WENN(A1>1,5;\"ja\";\"nein\")", "IF(A1>1.5,\"ja\",\"nein\")")]
    [InlineData("SVERWEIS(A1;B:C;2;FALSCH)", "VLOOKUP(A1,B:C,2,FALSE)")]
    [InlineData("XVERWEIS(1;A:A;B:B)", "XLOOKUP(1,A:A,B:B)")]
    [InlineData("ZÄHLENWENN(A1:A9;\">5\")", "COUNTIF(A1:A9,\">5\")")]
    [InlineData("{1.2;3.4}", "{1,2;3,4}")]
    [InlineData("{1,5.2,5}", "{1.5,2.5}")]
    [InlineData("WENNFEHLER(1/0;#NV)", "IFERROR(1/0,#N/A)")]
    [InlineData("ISTFEHLER(#WERT!)", "ISERROR(#VALUE!)")]
    [InlineData("SUMME((A1;B2))", "SUM((A1,B2))")]
    [InlineData("WAHR", "TRUE")]
    [InlineData("SUM(A1)", "SUM(A1)")]
    public void German_input_is_stored_canonically_and_written_back(string german, string canonical)
    {
        Assert.Equal(canonical, RoundTrip(german, FormulaSyntax.German, FormulaSyntax.Canonical));
        if (german != "SUM(A1)")
        {
            Assert.Equal(german, RoundTrip(canonical, FormulaSyntax.Canonical, FormulaSyntax.German));
        }
    }

    [Theory]
    [InlineData("$A$1", "R1C1")]
    [InlineData("D5", "RC")]
    [InlineData("C4", "R[-1]C[-1]")]
    [InlineData("$A$1:B6", "R1C1:R[1]C[-2]")]
    [InlineData("A:A", "C[-3]")]
    [InlineData("$A:$B", "C1:C2")]
    [InlineData("5:7", "R:R[2]")]
    public void R1C1_round_trips(string a1, string r1c1)
    {
        var r1c1Syntax = FormulaSyntax.Canonical with { Style = ReferenceStyle.R1C1 };
        Assert.Equal(r1c1, RoundTrip(a1, FormulaSyntax.Canonical, r1c1Syntax));
        Assert.Equal(a1.Replace("$A$1", "$A$1"), RoundTrip(r1c1, r1c1Syntax, FormulaSyntax.Canonical));
    }

    [Fact]
    public void German_R1C1_uses_Z_and_S()
    {
        var syntax = FormulaSyntax.German with { Style = ReferenceStyle.R1C1 };
        Assert.Equal("SUMME(Z1S1:Z[1]S)", RoundTrip("SUM($A$1:D6)", FormulaSyntax.Canonical, syntax));
    }

    [Fact]
    public void Precedence_follows_Excel()
    {
        var workbook = Book();
        var node = FormulaParser.ParseNode("1+2*3^2", workbook, Origin);
        var add = Assert.IsType<BinaryNode>(node);
        Assert.Equal(BinaryOperator.Add, add.Operator);
        var multiply = Assert.IsType<BinaryNode>(add.Right);
        Assert.Equal(BinaryOperator.Multiply, multiply.Operator);

        // Negation binds tighter than ^: -2^2 = (-2)^2.
        var power = Assert.IsType<BinaryNode>(FormulaParser.ParseNode("-2^2", workbook, Origin));
        Assert.Equal(BinaryOperator.Power, power.Operator);
        Assert.IsType<UnaryNode>(power.Left);

        // Comparison is lowest, & is below +.
        var compare = Assert.IsType<BinaryNode>(FormulaParser.ParseNode("1&2=\"12\"", workbook, Origin));
        Assert.Equal(BinaryOperator.Equal, compare.Operator);
    }

    [Fact]
    public void Relative_references_are_stored_as_offsets_so_filled_formulas_share_one_instance()
    {
        var workbook = Book();
        var first = FormulaParser.ParseNode("A1*2", workbook, new CellAddress(0, 1));
        var second = FormulaParser.ParseNode("A2*2", workbook, new CellAddress(1, 1));
        Assert.Equal(first, second);
        Assert.Equal("A7*2", FormulaFormatter.Format(first, workbook, new CellAddress(6, 1)));
    }

    [Fact]
    public void Function_names_with_file_prefixes_are_read_and_written()
    {
        var workbook = Book();
        var formula = FormulaParser.Parse("_xlfn.XLOOKUP(1,A:A,B:B)+_xlfn._xlws.FILTER(A1:A3,B1:B3)", workbook, null, Origin);
        Assert.Equal("XLOOKUP(1,A:A,B:B)+FILTER(A1:A3,B1:B3)", FormulaFormatter.Format(formula, workbook, Origin));
        Assert.Equal("_xlfn.XLOOKUP(1,A:A,B:B)+_xlfn._xlws.FILTER(A1:A3,B1:B3)", FormulaFormatter.Format(formula, workbook, Origin, FormulaSyntax.FileFormat));

        var let = FormulaParser.Parse("_xlfn.LET(_xlpm.x,1,_xlpm.x+1)", workbook, null, Origin);
        Assert.Equal("LET(x,1,x+1)", FormulaFormatter.Format(let, workbook, Origin));
        Assert.Equal("_xlfn.LET(_xlpm.x,1,_xlpm.x+1)", FormulaFormatter.Format(let, workbook, Origin, FormulaSyntax.FileFormat));
    }

    [Fact]
    public void Unknown_functions_keep_their_name_and_prefix()
    {
        var workbook = Book();
        var formula = FormulaParser.Parse("_xlfn.FUTUREFUNC(A1)+_xll.ADDIN(2)", workbook, null, Origin);
        Assert.True(formula.HasUnknownFunctions);
        Assert.Equal("_xlfn.FUTUREFUNC(A1)+_xll.ADDIN(2)", FormulaFormatter.Format(formula, workbook, Origin, FormulaSyntax.FileFormat));
    }

    [Fact]
    public void Renaming_a_sheet_changes_the_text_of_formulas_referring_to_it()
    {
        var workbook = Book();
        var formula = FormulaParser.Parse("Daten!A1", workbook, null, Origin);
        workbook.RenameSheet(workbook["Daten"], "Neue Daten");
        Assert.Equal("'Neue Daten'!A1", FormulaFormatter.Format(formula, workbook, Origin));
    }

    [Theory]
    [InlineData("SUM(1,2")]
    [InlineData("1+")]
    [InlineData("\"offen")]
    [InlineData("{1,2;3}")]
    [InlineData("A1)")]
    [InlineData("")]
    [InlineData("=")]
    public void Invalid_formulas_report_an_error(string text)
    {
        var workbook = Book();
        Assert.Throws<FormulaParseException>(() => FormulaParser.Parse(text, workbook, null, Origin));
    }

    [Fact]
    public void Volatile_and_precedents_are_known()
    {
        var workbook = Book();
        var formula = FormulaParser.Parse("SUM(A1:B2)+RAND()+Name1+Daten!C3", workbook, null, Origin);
        Assert.True(formula.IsVolatile);
        Assert.Equal(3, formula.Precedents.Length);
    }
}
