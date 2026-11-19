using System.Text;
using ThixCalc.Core.Formats.Csv;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Tests.Formats;

/// <summary>CSV import and export: separators, quoting, encodings, German number and date recognition.</summary>
public class CsvTests
{
    private static Workbook Read(string text, Encoding? encoding = null, CsvOptions? options = null) =>
        CsvFormat.Read(new MemoryStream((encoding ?? new UTF8Encoding(true)).GetPreamble().Concat((encoding ?? Encoding.UTF8).GetBytes(text)).ToArray()), options);

    [Fact]
    public void German_csv_with_semicolons_decimal_commas_and_dates()
    {
        var sheet = Read("Name;Betrag;Datum\r\nMüller;1.234,56;31.12.2024\r\n\"Meier; Söhne\";-7,5;01.02.2023\r\n").Sheets[0];
        Assert.Equal("Name", sheet["A1"].Text);
        Assert.Equal(1234.56, sheet["B2"].Number);
        Assert.Equal(45657, sheet["C2"].Number);
        Assert.Equal("dd.mm.yyyy", sheet.GetStyle(1, 2).NumberFormat);
        Assert.Equal("Meier; Söhne", sheet["A3"].Text);
        Assert.Equal(-7.5, sheet["B3"].Number);
    }

    [Fact]
    public void Quotes_line_breaks_and_empty_fields()
    {
        var sheet = Read("a,\"b \"\"c\"\"\",,d\n\"zwei\nZeilen\",x\n", options: new CsvOptions { Locale = CalcLocale.English }).Sheets[0];
        Assert.Equal("b \"c\"", sheet["B1"].Text);
        Assert.True(sheet["C1"].IsBlank);
        Assert.Equal("d", sheet["D1"].Text);
        Assert.Equal("zwei\nZeilen", sheet["A2"].Text);
        Assert.Equal("x", sheet["B2"].Text);
    }

    [Theory]
    [InlineData("a;b;c\n1;2;3", ';')]
    [InlineData("a,b,c\n1,2,3", ',')]
    [InlineData("a\tb\tc\n1\t2\t3", '\t')]
    [InlineData("a|b|c\n1|2|3", '|')]
    [InlineData("\"x,y\";z\n1;2", ';')]
    public void The_separator_is_detected(string text, char expected) => Assert.Equal(expected, CsvFormat.DetectDelimiter(text, CalcLocale.German));

    [Fact]
    public void Windows_1252_files_are_recognized()
    {
        var bytes = Encoding.Latin1.GetBytes("Größe;Maß\r\nÄ;ü\r\n");
        var sheet = CsvFormat.Read(new MemoryStream(bytes)).Sheets[0];
        Assert.Equal("Größe", sheet["A1"].Text);
        Assert.Equal("ü", sheet["B2"].Text);
    }

    [Fact]
    public void Export_writes_displayed_values_with_the_locale_separator()
    {
        var workbook = TestBooks.Create();
        var sheet = workbook.Sheets[0];
        TestBooks.Fill(sheet, ("A1", "Text; mit Semikolon"), ("B1", 1234.5), ("C1", "=B1*2"), ("A2", "Zitat \"x\""), ("C2", 45657));
        sheet.SetStyleId(1, 2, workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "dd.mm.yyyy" }));
        workbook.Calculation.Recalculate();
        using var stream = new MemoryStream();
        CsvFormat.Write(sheet, stream, new CsvOptions { Locale = CalcLocale.German });
        var text = new UTF8Encoding(true).GetString(stream.ToArray()).TrimStart('﻿');
        Assert.Equal("\"Text; mit Semikolon\";1234,5;2469\r\n\"Zitat \"\"x\"\"\";;31.12.2024\r\n", text);

        var back = CsvFormat.Read(new MemoryStream(stream.ToArray())).Sheets[0];
        Assert.Equal("Text; mit Semikolon", back["A1"].Text);
        Assert.Equal(2469, back["C1"].Number);
    }
}
