using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Tests.Formatting;

/// <summary>Number format codes in en-US and de-DE: numbers, sections, colours, percent, scientific, fractions, dates, text.</summary>
public class NumberFormatTests
{
    private static string English(object value, string code) => NumberFormatter.Format(TestBooks.ToValue(value), code, CalcLocale.English).Text;

    private static string German(object value, string code) => NumberFormatter.Format(TestBooks.ToValue(value), code, CalcLocale.German).Text;

    [Theory]
    [InlineData(1234.567, "0", "1235")]
    [InlineData(1234.567, "0.00", "1234.57")]
    [InlineData(1234.567, "#,##0.00", "1,234.57")]
    [InlineData(1234567.891, "#,##0", "1,234,568")]
    [InlineData(0.5, "#.##", ".5")]
    [InlineData(0.5, "0.##", "0.5")]
    [InlineData(12, "000", "012")]
    [InlineData(-12.5, "0.0", "-12.5")]
    [InlineData(-12.5, "0.0;(0.0)", "(12.5)")]
    [InlineData(0, "0.0;-0.0;\"zero\"", "zero")]
    [InlineData(0.256, "0%", "26%")]
    [InlineData(0.256, "0.0%", "25.6%")]
    [InlineData(12345.678, "0.00E+00", "1.23E+04")]
    [InlineData(0.000123, "0.00E+00", "1.23E-04")]
    [InlineData(12345, "##0.0E+0", "12.3E+3")]
    [InlineData(1.5, "# ?/?", "1 1/2")]
    [InlineData(0.75, "# ?/?", " 3/4")]
    [InlineData(2.3125, "# ??/16", "2  5/16")]
    [InlineData(5, "# ?/?", "5    ")]
    [InlineData(1234567, "#,##0,", "1,235")]
    [InlineData(1234567, "0.0,,\" Mio\"", "1.2 Mio")]
    [InlineData(5551234567, "(###) ###-####", "(555) 123-4567")]
    [InlineData(1.005, "0.00", "1.01")]
    [InlineData(-0.001, "0.00", "0.00")]
    [InlineData(1234.5, "\"$\"#,##0.00", "$1,234.50")]
    [InlineData(150, "[>100]\"groß\";\"klein\"", "groß")]
    [InlineData(50, "[>100]\"groß\";\"klein\"", "klein")]
    [InlineData(1, "General", "1")]
    [InlineData(0.1, "General", "0.1")]
    [InlineData(1234567890123, "General", "1.23457E+12")]
    [InlineData(123456.789012345, "General", "123456.789")]
    [InlineData(0.000000000123, "General", "1.23E-10")]
    public void Numbers_in_English(double value, string code, string expected) => Assert.Equal(expected, English(value, code));

    [Theory]
    [InlineData(1234.567, "#,##0.00", "1.234,57")]
    [InlineData(0.256, "0.0%", "25,6%")]
    [InlineData(1234.5, "#,##0.00 \"€\"", "1.234,50 €")]
    [InlineData(123456.789012345, "General", "123456,789")]
    [InlineData(12345.678, "0.00E+00", "1,23E+04")]
    public void Numbers_in_German(double value, string code, string expected) => Assert.Equal(expected, German(value, code));

    [Theory]
    [InlineData(45458.5, "yyyy-mm-dd", "2024-06-15")]
    [InlineData(45458.5, "d/m/yy", "15/6/24")]
    [InlineData(45458.5, "dddd, mmmm d, yyyy", "Saturday, June 15, 2024")]
    [InlineData(45458.5, "ddd mmm", "Sat Jun")]
    [InlineData(45458.5, "mmmmm", "J")]
    [InlineData(45458.5, "h:mm AM/PM", "12:00 PM")]
    [InlineData(45458.75, "hh:mm:ss", "18:00:00")]
    [InlineData(1.5, "[h]:mm", "36:00")]
    [InlineData(0.0104166666666667, "[mm]:ss", "15:00")]
    [InlineData(0.000011574074, "ss.00", "01.00")]
    [InlineData(45458.5, "m/d/yyyy h:mm", "6/15/2024 12:00")]
    [InlineData(60, "yyyy-mm-dd", "1900-02-29")]
    [InlineData(0, "yyyy-mm-dd", "1900-01-00")]
    [InlineData(45458.999999, "hh:mm", "00:00")]
    public void Dates_and_times_in_English(double value, string code, string expected) => Assert.Equal(expected, English(value, code));

    [Theory]
    [InlineData(45458.5, "dddd, d. mmmm yyyy", "Samstag, 15. Juni 2024")]
    [InlineData(45366, "d. mmm yyyy", "15. Mrz 2024")]
    [InlineData(45458.5, "m/d/yyyy", "15.06.2024")]
    [InlineData(45458.5, "[$-409]mmmm", "June")]
    public void Dates_in_German(double value, string code, string expected) => Assert.Equal(expected, German(value, code));

    [Fact]
    public void Negative_dates_cannot_be_shown()
    {
        var result = NumberFormatter.Format(-1, "dd.mm.yyyy", CalcLocale.German);
        Assert.True(result.IsOverflow);
    }

    [Fact]
    public void Colours_text_sections_and_fill()
    {
        var red = NumberFormatter.Format(-5, "0;[Red]-0", CalcLocale.English);
        Assert.Equal("-5", red.Text);
        Assert.Equal("#FF0000", red.Color.ToString());
        Assert.Equal("#FF0000", NumberFormatter.Format(-5, "0;[Rot]-0", CalcLocale.German).Color.ToString());

        Assert.Equal("Name: Anna", NumberFormatter.Format("Anna", "0;0;0;\"Name: \"@", CalcLocale.English).Text);
        Assert.Equal("Anna", NumberFormatter.Format("Anna", "0.00", CalcLocale.English).Text);

        var fill = NumberFormatter.Format(12, "\"$\"* 0", CalcLocale.English);
        Assert.Equal("$12", fill.Text);
        Assert.Equal(1, fill.FillIndex);
        Assert.Equal(' ', fill.FillCharacter);
        Assert.Equal(" 12 ", NumberFormatter.Format(12, "_(0_)", CalcLocale.English).Text);
    }

    [Theory]
    [InlineData("TT.MM.JJJJ", "dd.mm.yyyy")]
    [InlineData("#.##0,00 €", "#,##0.00 €")]
    [InlineData("Standard", "General")]
    [InlineData("[Rot]#.##0;[Blau]-#.##0", "[Red]#,##0;[Blue]-#,##0")]
    [InlineData("TTTT, T. MMMM JJJJ", "dddd, d. mmmm yyyy")]
    [InlineData("hh:mm:ss", "hh:mm:ss")]
    [InlineData("0,0%", "0.0%")]
    public void German_codes_translate_to_canonical_and_back(string german, string canonical)
    {
        Assert.Equal(canonical, FormatCodes.ToCanonical(german, CalcLocale.German));
        Assert.Equal(german.Replace("MMMM", "mmmm", StringComparison.Ordinal).Replace(".MM.", ".mm.", StringComparison.Ordinal),
            FormatCodes.ToLocal(canonical, CalcLocale.German));
    }

    [Fact]
    public void Built_in_ids_depend_on_the_locale()
    {
        Assert.Equal("m/d/yyyy", BuiltInFormats.Code(14));
        Assert.Equal(14, BuiltInFormats.IdOf("m/d/yyyy"));
        Assert.Equal("dd.mm.yyyy", BuiltInFormats.Localize("m/d/yyyy", CalcLocale.German));
        Assert.Equal("6/15/2024", NumberFormatter.Format(45458, "m/d/yyyy", CalcLocale.English).Text);
        Assert.Equal("1.234 €", NumberFormatter.Format(1234, BuiltInFormats.Code(5)!, CalcLocale.German).Text);
    }

    [Fact]
    public void Booleans_errors_and_blanks()
    {
        Assert.Equal("WAHR", NumberFormatter.Format(true, "General", CalcLocale.German).Text);
        Assert.Equal("#WERT!", NumberFormatter.Format(CellError.Value, "General", CalcLocale.German).Text);
        Assert.Equal("#ÜBERLAUF!", NumberFormatter.Format(CellError.Spill, "0", CalcLocale.German).Text);
        Assert.Equal(string.Empty, NumberFormatter.Format(CellValue.Blank, "0.00", CalcLocale.German).Text);
    }
}
