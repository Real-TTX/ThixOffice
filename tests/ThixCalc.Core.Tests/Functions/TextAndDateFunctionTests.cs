using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Functions;

/// <summary>Text and date/time functions with the results Excel documents.</summary>
public class TextAndDateFunctionTests
{
    [Theory]
    [InlineData("LEN(\"Phoenix, AZ\")", 11.0)]
    [InlineData("LEFT(\"Sale Price\",4)", "Sale")]
    [InlineData("LEFT(\"Sweden\")", "S")]
    [InlineData("RIGHT(\"Stock Number\",6)", "Number")]
    [InlineData("MID(\"Fluid Flow\",7,20)", "Flow")]
    [InlineData("MID(\"Fluid Flow\",20,5)", "")]
    [InlineData("UPPER(\"total\")", "TOTAL")]
    [InlineData("LOWER(\"E. E. Cummings\")", "e. e. cummings")]
    [InlineData("PROPER(\"this is a TITLE\")", "This Is A Title")]
    [InlineData("PROPER(\"76BudGet\")", "76Budget")]
    [InlineData("TRIM(\" First Quarter    Earnings \")", "First Quarter Earnings")]
    [InlineData("FIND(\"M\",\"Miriam McGovern\")", 1.0)]
    [InlineData("FIND(\"m\",\"Miriam McGovern\")", 6.0)]
    [InlineData("FIND(\"M\",\"Miriam McGovern\",3)", 8.0)]
    [InlineData("SEARCH(\"e\",\"Statements\",6)", 7.0)]
    [InlineData("SEARCH(\"m?r\",\"Miriam\")", 1.0)]
    [InlineData("SEARCH(\"*am\",\"Miriam\")", 1.0)]
    [InlineData("SUBSTITUTE(\"Sales Data\",\"Sales\",\"Cost\")", "Cost Data")]
    [InlineData("SUBSTITUTE(\"Quarter 1, 2008\",\"1\",\"2\",1)", "Quarter 2, 2008")]
    [InlineData("SUBSTITUTE(\"Quarter 1, 2011\",\"1\",\"2\",3)", "Quarter 1, 2012")]
    [InlineData("REPLACE(\"abcdefghijk\",6,5,\"*\")", "abcde*k")]
    [InlineData("REPLACE(\"2009\",3,2,\"10\")", "2010")]
    [InlineData("REPT(\"*-\",3)", "*-*-*-")]
    [InlineData("CHAR(65)", "A")]
    [InlineData("CHAR(128)", "€")]
    [InlineData("CODE(\"A\")", 65.0)]
    [InlineData("UNICODE(\"€\")", 8364.0)]
    [InlineData("UNICHAR(66)", "B")]
    [InlineData("EXACT(\"word\",\"Word\")", false)]
    [InlineData("CONCATENATE(\"a\",1,TRUE)", "a1TRUE")]
    [InlineData("CONCAT(\"x\",A1:B1)", "x12")]
    [InlineData("TEXTJOIN(\", \",TRUE,\"a\",\"\",\"b\")", "a, b")]
    [InlineData("TEXTJOIN(\"-\",FALSE,A1:B1,\"\")", "1-2-")]
    [InlineData("T(\"Rain\")", "Rain")]
    [InlineData("T(19)", "")]
    [InlineData("VALUE(\"$1,000\")", 1000.0)]
    [InlineData("VALUE(\"16:48:00\")-VALUE(\"12:00:00\")", 0.2)]
    [InlineData("NUMBERVALUE(\"2.500,27\",\",\",\".\")", 2500.27)]
    [InlineData("NUMBERVALUE(\"3.5%\")", 0.035)]
    [InlineData("FIXED(1234.567,1)", "1,234.6")]
    [InlineData("FIXED(1234.567,-1)", "1,230")]
    [InlineData("FIXED(-1234.567,-1,TRUE)", "-1230")]
    [InlineData("DOLLAR(1234.567,2)", "$1,234.57")]
    [InlineData("TEXT(1234.567,\"$#,##0.00\")", "$1,234.57")]
    [InlineData("TEXT(0.285,\"0.0%\")", "28.5%")]
    [InlineData("TEXTBEFORE(\"Red riding hood's, red hood\",\"hood\")", "Red riding ")]
    [InlineData("TEXTBEFORE(\"Little red riding hood's red hood\",\"red\",2)", "Little red riding hood's ")]
    [InlineData("TEXTAFTER(\"Little red riding hood's red hood\",\"hood\")", "'s red hood")]
    [InlineData("TEXTAFTER(\"Little red riding hood's red hood\",\"red\",-1)", " hood")]
    [InlineData("TEXTAFTER(\"abc\",\"x\")", CellError.NotAvailable)]
    [InlineData("MID(\"abc\",0,1)", CellError.Value)]
    [InlineData("FIND(\"z\",\"abc\")", CellError.Value)]
    [InlineData("REPT(\"ab\",-1)", CellError.Value)]
    public void Text_functions(string formula, object expected) => AssertValue(expected, Evaluate(formula, ("A1", 1), ("B1", 2)));

    [Fact]
    public void Text_splits_into_an_array()
    {
        var workbook = Create();
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "=TEXTSPLIT(\"Dakota Lennon Sanchez\",\" \")"), ("A3", "=TEXTSPLIT(\"1,2;3\",\",\",\";\",FALSE,0,\"-\")"));
        workbook.Calculation.Recalculate();
        Assert.Equal("Lennon", sheet["B1"].Text);
        Assert.Equal("Sanchez", sheet["C1"].Text);
        Assert.Equal("3", sheet["A4"].Text);
        Assert.Equal("-", sheet["B4"].Text);
    }

    [Fact]
    public void Text_follows_the_German_locale()
    {
        var workbook = Create();
        workbook.Calculation.Locale = CalcLocale.German;
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 45458.75), ("B1", "=TEXT(A1,\"TT.MM.JJJJ hh:mm\")"), ("B2", "=TEXT(1234.5,\"#.##0,00 €\")"), ("B3", "=\"Wert: \"&1.5"),
            ("B4", "=VALUE(\"1,5\")"), ("B5", "=TEXT(A1,\"TTTT\")"), ("B6", "=DATEVALUE(\"31.12.2024\")"), ("B7", "=FIXED(1234.5,1)"));
        workbook.Calculation.Recalculate();
        Assert.Equal("15.06.2024 18:00", sheet["B1"].Text);
        Assert.Equal("1.234,50 €", sheet["B2"].Text);
        Assert.Equal("Wert: 1,5", sheet["B3"].Text);
        Assert.Equal(1.5, sheet["B4"].Number);
        Assert.Equal("Samstag", sheet["B5"].Text);
        Assert.Equal(45657, sheet["B6"].Number);
        Assert.Equal("1.234,5", sheet["B7"].Text);
    }

    [Theory]
    [InlineData("DATE(2008,1,2)", 39449.0)]
    [InlineData("DATE(108,1,2)", 39449.0)]
    [InlineData("DATE(2008,14,2)", 39846.0)]
    [InlineData("DATE(2008,-3,2)", 39327.0)]
    [InlineData("DATE(2008,1,35)", 39482.0)]
    [InlineData("DATE(1900,2,29)", 60.0)]
    [InlineData("DATE(1900,3,1)", 61.0)]
    [InlineData("DATE(10000,1,1)", CellError.Number)]
    [InlineData("TIME(12,0,0)", 0.5)]
    [InlineData("TIME(16,48,10)", 0.700115740740741)]
    [InlineData("TIME(27,0,0)", 0.125)]
    [InlineData("YEAR(45458.5)", 2024.0)]
    [InlineData("MONTH(\"15.06.2024\")", 6.0)]
    [InlineData("MONTH(45458)", 6.0)]
    [InlineData("DAY(60)", 29.0)]
    [InlineData("HOUR(0.75)", 18.0)]
    [InlineData("MINUTE(\"12:45:00 PM\")", 45.0)]
    [InlineData("SECOND(0.700115740740741)", 10.0)]
    [InlineData("WEEKDAY(DATE(2008,2,14))", 5.0)]
    [InlineData("WEEKDAY(DATE(2008,2,14),2)", 4.0)]
    [InlineData("WEEKDAY(DATE(2008,2,14),3)", 3.0)]
    [InlineData("WEEKDAY(1)", 1.0)]
    [InlineData("WEEKNUM(DATE(2012,3,9))", 10.0)]
    [InlineData("WEEKNUM(DATE(2012,3,9),2)", 11.0)]
    [InlineData("ISOWEEKNUM(42443)", 11.0)]
    [InlineData("ISOWEEKNUM(DATE(2021,1,1))", 53.0)]
    [InlineData("EDATE(DATE(2011,1,15),1)", 40589.0)]
    [InlineData("EDATE(DATE(2011,1,31),1)", 40602.0)]
    [InlineData("EOMONTH(DATE(2011,1,1),1)", 40602.0)]
    [InlineData("EOMONTH(DATE(2011,1,1),-3)", 40482.0)]
    [InlineData("DATEDIF(DATE(2001,1,1),DATE(2003,1,1),\"Y\")", 2.0)]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"D\")", 440.0)]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"YD\")", 75.0)]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"MD\")", 14.0)]
    [InlineData("DATEDIF(DATE(2001,6,1),DATE(2002,8,15),\"YM\")", 2.0)]
    [InlineData("DATEDIF(DATE(2003,1,1),DATE(2001,1,1),\"Y\")", CellError.Number)]
    [InlineData("DAYS(DATE(2021,3,15),DATE(2021,2,1))", 42.0)]
    [InlineData("DAYS360(DATE(2011,1,30),DATE(2011,12,31))", 330.0)]
    [InlineData("DAYS360(DATE(2011,2,28),DATE(2011,3,31))", 30.0)]
    [InlineData("DAYS360(DATE(2011,1,31),DATE(2011,2,28),TRUE)", 28.0)]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30))", 0.580555555555556)]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30),1)", 0.576502732240437)]
    [InlineData("YEARFRAC(DATE(2012,1,1),DATE(2012,7,30),3)", 0.578082191780822)]
    [InlineData("NETWORKDAYS(DATE(2012,10,1),DATE(2013,3,1))", 110.0)]
    [InlineData("NETWORKDAYS(DATE(2012,10,1),DATE(2013,3,1),DATE(2012,11,22))", 109.0)]
    [InlineData("NETWORKDAYS.INTL(DATE(2006,1,1),DATE(2006,1,31))", 22.0)]
    [InlineData("NETWORKDAYS.INTL(DATE(2006,1,1),DATE(2006,2,1),7)", 24.0)]
    [InlineData("NETWORKDAYS.INTL(DATE(2006,1,1),DATE(2006,2,1),\"0010001\")", 22.0)]
    [InlineData("WORKDAY(DATE(2008,10,1),151)", 39933.0)]
    [InlineData("WORKDAY.INTL(DATE(2012,1,1),30,0)", CellError.Number)]
    [InlineData("WORKDAY.INTL(DATE(2012,1,1),90,11)", 41013.0)]
    [InlineData("DATEVALUE(\"8/22/2011\")", 40777.0)]
    [InlineData("TIMEVALUE(\"2:24 AM\")", 0.1)]
    [InlineData("TODAY()", 45458.0)]
    [InlineData("NOW()", 45458.4375)]
    public void Date_functions(string formula, object expected) => AssertValue(expected, Evaluate(formula));

    [Fact]
    public void Date_1904_system()
    {
        var workbook = Create();
        workbook.Date1904 = true;
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "=DATE(1904,1,2)"), ("A2", "=YEAR(0)"), ("A3", "=WEEKDAY(0)"));
        workbook.Calculation.Recalculate();
        Assert.Equal(1, sheet["A1"].Number);
        Assert.Equal(1904, sheet["A2"].Number);
        Assert.Equal(6, sheet["A3"].Number);
    }
}
