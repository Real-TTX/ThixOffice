using ThixCalc.Core.Model;

namespace ThixCalc.Core.Tests.Model;

/// <summary>The sparse cell storage and the basic model types.</summary>
public class CellStoreTests
{
    [Fact]
    public void Values_are_stored_sparsely_and_read_back()
    {
        var workbook = TestBooks.Create();
        var sheet = workbook.Sheets[0];
        sheet.SetValue(0, 0, 1.5);
        sheet.SetValue(1_048_575, 16_383, "Ende");
        sheet.SetValue(5, 3, true);
        sheet.SetValue(5, 2, CellError.NotAvailable);

        Assert.Equal(1.5, sheet.GetValue(0, 0).Number);
        Assert.Equal("Ende", sheet["XFD1048576"].Text);
        Assert.True(sheet["D6"].Boolean);
        Assert.Equal(CellError.NotAvailable, sheet["C6"].Error);
        Assert.True(sheet["B2"].IsBlank);
        Assert.Equal(4, sheet.CellCount);
        Assert.Equal(new CellRange(0, 0, 1_048_575, 16_383), sheet.UsedRange);
    }

    [Fact]
    public void Blank_unstyled_cells_are_removed()
    {
        var sheet = TestBooks.Create().Sheets[0];
        sheet.SetValue(2, 2, 7);
        sheet.SetValue(2, 2, CellValue.Blank);
        Assert.Equal(0, sheet.CellCount);
        Assert.Null(sheet.UsedRange);

        sheet.SetStyleId(2, 2, 3);
        Assert.Equal(1, sheet.CellCount);
    }

    [Fact]
    public void Enumeration_is_row_major_and_limited_to_the_range()
    {
        var sheet = TestBooks.Create().Sheets[0];
        TestBooks.Fill(sheet, ("C3", 1), ("A1", 2), ("B2", 3), ("A3", 4), ("D3", 5));
        var cells = sheet.GetCells(CellRange.Parse("A2:C3")).Select(cell => cell.Address.ToString()).ToList();
        Assert.Equal(["B2", "A3", "C3"], cells);
    }

    [Fact]
    public void Rows_and_columns_insert_and_delete_shift_the_cells()
    {
        var sheet = TestBooks.Create().Sheets[0];
        TestBooks.Fill(sheet, ("A1", 1), ("A2", 2), ("A3", 3), ("C1", 4));
        sheet.Store.InsertRows(1, 2);
        Assert.Equal(2, sheet["A4"].Number);
        Assert.Equal(3, sheet["A5"].Number);
        sheet.Store.DeleteRows(0, 4);
        Assert.Equal(3, sheet["A1"].Number);
        Assert.True(sheet["C1"].IsBlank);

        sheet.Store.InsertColumns(0, 1);
        Assert.Equal(3, sheet["B1"].Number);
        sheet.Store.DeleteColumns(0, 1);
        Assert.Equal(3, sheet["A1"].Number);
    }

    [Theory]
    [InlineData(0, "A")]
    [InlineData(25, "Z")]
    [InlineData(26, "AA")]
    [InlineData(701, "ZZ")]
    [InlineData(702, "AAA")]
    [InlineData(16383, "XFD")]
    public void Column_names_round_trip(int column, string name)
    {
        Assert.Equal(name, A1.ColumnName(column));
        Assert.True(A1.TryParseColumn(name, out var parsed));
        Assert.Equal(column, parsed);
    }

    [Theory]
    [InlineData("A1:B3", "A1:B3")]
    [InlineData("$B$2", "B2")]
    [InlineData("C:E", "C:E")]
    [InlineData("2:4", "2:4")]
    [InlineData("b3:a1", "A1:B3")]
    public void Ranges_parse_and_format(string text, string expected) => Assert.Equal(expected, CellRange.Parse(text).ToString());

    [Fact]
    public void Invalid_addresses_are_rejected()
    {
        Assert.Null(CellAddress.TryParse("XFE1"));
        Assert.Null(CellAddress.TryParse("A0"));
        Assert.Null(CellAddress.TryParse("A1048577"));
        Assert.Null(CellAddress.TryParse("1A"));
    }

    [Fact]
    public void Axis_offsets_follow_custom_sizes_and_hidden_rows()
    {
        var rows = new AxisCollection(15, SheetLimits.MaxRows);
        rows.Set(2, new AxisInfo(Size: 30, CustomSize: true));
        rows.Set(4, new AxisInfo(Hidden: true));
        Assert.Equal(0, rows.OffsetOf(0));
        Assert.Equal(30, rows.OffsetOf(2));
        Assert.Equal(60, rows.OffsetOf(3));
        Assert.Equal(75, rows.OffsetOf(4));
        Assert.Equal(75, rows.OffsetOf(5));
        Assert.Equal(2, rows.IndexAt(31));
        Assert.Equal(5, rows.IndexAt(75));
        Assert.Equal(SheetLimits.MaxRows * 15.0 + 15 - 15, rows.TotalSize, 6);
    }

    [Fact]
    public void Styles_are_interned()
    {
        var workbook = TestBooks.Create();
        var bold = CellStyle.Default with { Font = CellFont.Default with { Bold = true } };
        var first = workbook.Styles.Intern(bold);
        var second = workbook.Styles.Intern(CellStyle.Default with { Font = CellFont.Default with { Bold = true } });
        Assert.Equal(first, second);
        Assert.Equal(0, workbook.Styles.Intern(CellStyle.Default));
    }

    [Fact]
    public void Sheet_names_are_checked_like_Excel()
    {
        var workbook = TestBooks.Create();
        Assert.Throws<ThixOffice.Core.Models.BusinessException>(() => workbook.AddSheet("Tabelle1"));
        Assert.Throws<ThixOffice.Core.Models.BusinessException>(() => workbook.AddSheet("a/b"));
        Assert.Throws<ThixOffice.Core.Models.BusinessException>(() => workbook.AddSheet(new string('x', 32)));
        Assert.Equal("Tabelle2", workbook.NextSheetName());
    }
}
