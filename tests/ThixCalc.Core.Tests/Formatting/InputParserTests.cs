using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Tests.Formatting;

/// <summary>Type detection of what the user types, in German and English.</summary>
public class InputParserTests
{
    private static readonly Func<DateTime> Today = () => new DateTime(2024, 6, 15);

    [Theory]
    [InlineData("42", 42.0, null)]
    [InlineData("1,5", 1.5, null)]
    [InlineData("-1.234,56", -1234.56, "#,##0.00")]
    [InlineData("1.234", 1234.0, "#,##0")]
    [InlineData("(12)", -12.0, null)]
    [InlineData("12%", 0.12, "0%")]
    [InlineData("12,5 %", 0.125, "0.0%")]
    [InlineData("12,50 €", 12.5, "#,##0.00 \"€\"")]
    [InlineData("€ 3", 3.0, "#,##0.00 \"€\"")]
    [InlineData("1E3", 1000.0, "0.00E+00")]
    [InlineData("1 1/2", 1.5, "# ?/?")]
    [InlineData("31.12.2024", 45657.0, "dd.mm.yyyy")]
    [InlineData("1.5", 45413.0, "dd. mmm")]
    [InlineData("31.12.24", 45657.0, "dd.mm.yyyy")]
    [InlineData("2024-12-31", 45657.0, "yyyy-mm-dd")]
    [InlineData("31. Dez 2024", 45657.0, "dd. mmm yyyy")]
    [InlineData("14:30", 0.6041666666666666, "hh:mm")]
    [InlineData("25:00", 1.0416666666666667, "[h]:mm")]
    [InlineData("31.12.2024 14:30", 45657.604166666664, "dd.mm.yyyy hh:mm")]
    public void German_numbers_dates_and_times(string input, double expected, string? format)
    {
        var parsed = ValueParser.ParseInput(input, CalcLocale.German, today: Today);
        Assert.Equal(InputKind.Number, parsed.Kind);
        Assert.Equal(expected, parsed.Value.Number, 9);
        Assert.Equal(format, parsed.NumberFormat);
    }

    [Theory]
    [InlineData("1,234.5", 1234.5)]
    [InlineData("12/31/2024", 45657.0)]
    [InlineData("2:30 PM", 0.6041666666666666)]
    [InlineData("$1,000", 1000.0)]
    [InlineData("Dec 31, 2024", 45657.0)]
    public void English_numbers_and_dates(string input, double expected)
    {
        var parsed = ValueParser.ParseInput(input, CalcLocale.English, today: Today);
        Assert.Equal(InputKind.Number, parsed.Kind);
        Assert.Equal(expected, parsed.Value.Number, 9);
    }

    [Theory]
    [InlineData("Hallo", InputKind.Text)]
    [InlineData("'123", InputKind.Text)]
    [InlineData("=SUMME(A1:A3)", InputKind.Formula)]
    [InlineData("+A1*2", InputKind.Formula)]
    [InlineData("WAHR", InputKind.Boolean)]
    [InlineData("#NV", InputKind.Error)]
    [InlineData("1,2,3", InputKind.Text)]
    [InlineData("12.34.56", InputKind.Text)]
    [InlineData("", InputKind.Empty)]
    public void Other_German_input(string input, InputKind kind) => Assert.Equal(kind, ValueParser.ParseInput(input, CalcLocale.German, today: Today).Kind);

    [Fact]
    public void Apostrophe_keeps_the_text()
    {
        var parsed = ValueParser.ParseInput("'0815", CalcLocale.German);
        Assert.Equal("0815", parsed.Value.Text);
        Assert.Equal(CellError.NotAvailable, ValueParser.ParseInput("#NV", CalcLocale.German).Value.Error);
    }
}
