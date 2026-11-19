using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Core.Tests.Formats;
using ThixOffice.Core.Models;
using ThixOffice.Text.Drawing;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Editing;

/// <summary>Inhalte einfügen with a Vorgang, Ausfüllen › Reihe, filters by condition, rich text kept through xlsx, rotated and shrunk text.</summary>
public class PasteAndSeriesTests
{
    private static EditingSession Session(params (string Address, object? Value)[] cells)
    {
        var workbook = Create("T");
        Fill(workbook["T"], cells);
        workbook.Calculation.Recalculate();
        return new EditingSession(workbook) { Locale = CalcLocale.German };
    }

    [Theory]
    [InlineData(PasteOperation.Add, 12)]
    [InlineData(PasteOperation.Subtract, 8)]
    [InlineData(PasteOperation.Multiply, 20)]
    [InlineData(PasteOperation.Divide, 5)]
    public void A_paste_operation_combines_the_numbers(PasteOperation operation, double expected)
    {
        var session = Session(("A1", 2), ("C1", 10), ("C2", "Text"));
        session.Select(CellAddress.Parse("A1"));
        var content = session.Copy();

        session.Select(CellRange.Parse("C1:C2"));
        session.Paste(content, new PasteOptions(PasteMode.Values, Operation: operation));

        Assert.Equal(expected, session.ActiveSheet["C1"].Number);
        Assert.Equal(expected, session.ActiveSheet["C1"].Number);
        session.Undo();
        Assert.Equal(10, session.ActiveSheet["C1"].Number);
    }

    [Fact]
    public void Skip_blanks_keeps_what_is_there()
    {
        var session = Session(("A1", 1), ("A3", 3), ("C1", 9), ("C2", 9), ("C3", 9));
        session.Select(CellRange.Parse("A1:A3"));
        var content = session.Copy();
        session.Select(CellAddress.Parse("C1"));

        session.Paste(content, new PasteOptions(SkipBlanks: true));

        Assert.Equal([1, 9, 3], new[] { "C1", "C2", "C3" }.Select(address => session.ActiveSheet[address].Number));
    }

    [Fact]
    public void A_linear_series_fills_the_selection()
    {
        var session = Session(("A1", 5));
        session.Select(CellRange.Parse("A1:A5"));

        session.FillSeries(new SeriesOptions(InRows: false, SeriesType.Linear, Step: 2.5));

        Assert.Equal([5, 7.5, 10, 12.5, 15], Enumerable.Range(1, 5).Select(row => session.ActiveSheet["A" + row].Number));
    }

    [Fact]
    public void A_growth_series_stops_at_its_end_value()
    {
        var session = Session(("A1", 1));
        session.Select(CellAddress.Parse("A1"));

        session.FillSeries(new SeriesOptions(InRows: true, SeriesType.Growth, Step: 3, Stop: 100));

        Assert.Equal([1, 3, 9, 27, 81], new[] { "A1", "B1", "C1", "D1", "E1" }.Select(address => session.ActiveSheet[address].Number));
        Assert.True(session.ActiveSheet["F1"].IsBlank);
    }

    [Fact]
    public void A_date_series_steps_by_months_and_weekdays()
    {
        // 2026-01-31 = 46053; then 28.02., 31.03.
        var session = Session(("A1", 46053), ("B1", 46055));
        session.Select(CellRange.Parse("A1:A3"));
        session.FillSeries(new SeriesOptions(InRows: false, SeriesType.Date, 1, null, SeriesDateUnit.Month));
        Assert.Equal([46053, 46081, 46112], new[] { "A1", "A2", "A3" }.Select(address => session.ActiveSheet[address].Number));

        // 2026-02-02 is a Monday; five weekdays later is Monday the 9th.
        session.Select(CellRange.Parse("B1:B6"));
        session.FillSeries(new SeriesOptions(InRows: false, SeriesType.Date, 1, null, SeriesDateUnit.Weekday));
        Assert.Equal(46062, session.ActiveSheet["B6"].Number);
    }

    [Fact]
    public void A_single_cell_without_end_value_is_refused()
    {
        var session = Session(("A1", 1));
        session.Select(CellAddress.Parse("A1"));

        var error = Assert.Throws<BusinessException>(() => session.FillSeries(new SeriesOptions(false, SeriesType.Linear)));

        Assert.Equal("stop", error.Field);
    }

    [Fact]
    public void Custom_and_top_filters_hide_the_other_rows()
    {
        var session = Session(("A1", "Ort"), ("B1", "Wert"), ("A2", "Berlin"), ("B2", 5), ("A3", "Bonn"), ("B3", 50), ("A4", "Köln"), ("B4", 500), ("A5", "Bremen"), ("B5", 40));
        session.Select(CellAddress.Parse("A1"));
        session.ToggleAutoFilter();
        var rows = session.ActiveSheet.Rows;

        session.SetFilter(0, new FilterColumn { Custom = [new CustomFilter(FilterOperator.Equal, "B*")] });
        Assert.True(rows.IsHidden(3));
        Assert.False(rows.IsHidden(4));

        session.SetFilter(0, null);
        session.SetFilter(1, new FilterColumn { Custom = [new CustomFilter(FilterOperator.GreaterThanOrEqual, "40"), new CustomFilter(FilterOperator.LessThanOrEqual, "100")], CustomAnd = true });
        Assert.Equal([true, false, true, false], Enumerable.Range(1, 4).Select(rows.IsHidden));

        session.SetFilter(1, new FilterColumn { Top = 2 });
        Assert.Equal([true, false, false, true], Enumerable.Range(1, 4).Select(rows.IsHidden));

        session.SetFilter(1, new FilterColumn { Dynamic = "belowAverage" });
        Assert.Equal([false, false, true, false], Enumerable.Range(1, 4).Select(rows.IsHidden));
    }

    [Fact]
    public void Rich_text_survives_the_round_trip_while_the_text_is_the_same()
    {
        var workbook = Create("T");
        Fill(workbook["T"], ("A1", "Hallo Welt"), ("A2", "Hallo Welt"));
        var bytes = XlsxTests.Write(workbook);
        using (var stream = new MemoryStream())
        {
            stream.Write(bytes);
            using (var package = SpreadsheetDocument.Open(stream, true))
            {
                var part = package.WorkbookPart!.SharedStringTablePart!;
                const string xml = "<sst xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" count=\"2\" uniqueCount=\"1\"><si><r><rPr><b/></rPr><t xml:space=\"preserve\">Hallo </t></r><r><t>Welt</t></r></si></sst>";
                using var feed = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(xml));
                part.FeedData(feed);
            }

            bytes = stream.ToArray();
        }

        var read = XlsxTests.Read(bytes);
        Assert.Equal("Hallo Welt", read["T"]["A1"].Text);
        new EditingSession(read).SetValue(CellAddress.Parse("A2"), CellValue.FromText("Anders"));

        var written = XlsxTests.Write(read);
        XlsxTests.AssertValid(written);
        using var check = SpreadsheetDocument.Open(new MemoryStream(written), false);
        using var reader = new StreamReader(check.WorkbookPart!.SharedStringTablePart!.GetStream());
        var strings = reader.ReadToEnd();
        Assert.Matches(@"<(\w+:)?b\s*/>", strings);
        Assert.Contains("Anders", strings, StringComparison.Ordinal);
    }

    [Fact]
    public void Rotated_and_shrunk_texts_are_drawn()
    {
        var workbook = Create("T");
        Fill(workbook["T"], ("A1", "Gedreht"), ("A2", "Ein sehr langer Text in einer schmalen Zelle"));
        var rotated = workbook.Styles.Intern(CellStyle.Default with { Alignment = CellAlignment.Default with { TextRotation = 45 } });
        var shrunk = workbook.Styles.Intern(CellStyle.Default with { Alignment = CellAlignment.Default with { ShrinkToFit = true } });
        workbook["T"].SetStyleId(0, 0, rotated);
        workbook["T"].SetStyleId(1, 0, shrunk);
        var metrics = new GridMetrics(new ThixOffice.Text.FixedWidthTextMeasurer(), workbook, CalcLocale.German);

        var items = GridRenderer.Render(new SheetLayout(workbook["T"], metrics).Viewport(400, 200, 1), metrics).Items;

        Assert.Contains(items, item => item is TransformBeginItem { Rotation: -45 });
        var shrunkRun = items.OfType<GlyphRunItem>().Single(run => run.Text.StartsWith("Ein sehr", StringComparison.Ordinal));
        Assert.True(shrunkRun.Font.Size < 11 * 96 / 72.0, "the font shrank");
    }
}
