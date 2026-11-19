using ThixCalc.Core.Editing;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Core.Tests.Formats;
using ThixOffice.Core.Models;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Editing;

/// <summary>
/// Tables: created over a range (with or without headers), styled, with a total row, structured references in both languages
/// (parsing, display, rewriting on renames), growth by typing below and beside, filters of their own, converting back to a
/// range, undo of each, and the xlsx round trip (valid, new parts, kept parts).
/// </summary>
public class TableTests
{
    private static EditingSession Sales()
    {
        var workbook = Create("Umsatz");
        Fill(workbook["Umsatz"],
            ("A1", "Region"), ("B1", "Menge"), ("C1", "Preis"),
            ("A2", "Nord"), ("B2", 10), ("C2", 2.5),
            ("A3", "Süd"), ("B3", 4), ("C3", 3),
            ("A4", "Ost"), ("B4", 7), ("C4", 1.5));
        workbook.Calculation.Recalculate();
        return new EditingSession(workbook) { Locale = CalcLocale.German };
    }

    [Fact]
    public void A_range_with_headers_becomes_a_table()
    {
        var session = Sales();
        session.Select(new CellAddress(1, 1));

        var table = session.CreateTable(session.ProposedTableRange(), hasHeaders: true);

        Assert.Equal("Tabelle1", table.Name);
        Assert.Equal(CellRange.Parse("A1:C4"), table.Range);
        Assert.Equal(["Region", "Menge", "Preis"], table.Columns);
        Assert.Equal(TableStyles.DefaultStyle, table.Style);
        Assert.Equal("Tabelle1", session.ActiveTable?.Name);
        session.Undo();
        Assert.Empty(session.ActiveSheet.Tables);
    }

    [Fact]
    public void Without_headers_a_header_row_goes_above()
    {
        var workbook = Create("T");
        Fill(workbook["T"], ("A1", 1), ("B1", 2), ("A2", 3), ("B2", 4), ("A3", "darunter"));
        var session = new EditingSession(workbook) { Locale = CalcLocale.German };

        var table = session.CreateTable(CellRange.Parse("A1:B2"), hasHeaders: false);

        Assert.Equal(CellRange.Parse("A1:B3"), table.Range);
        Assert.Equal(["Spalte1", "Spalte2"], table.Columns);
        Assert.Equal("Spalte1", workbook["T"]["A1"].Text);
        Assert.Equal(1, workbook["T"]["A2"].Number);
        Assert.Equal("darunter", workbook["T"]["A4"].Text);
    }

    [Fact]
    public void Overlapping_tables_and_merged_cells_are_refused()
    {
        var session = Sales();
        session.CreateTable(CellRange.Parse("A1:C4"), true);

        Assert.Throws<BusinessException>(() => session.CreateTable(CellRange.Parse("C2:D5"), true));
        session.ActiveSheet.Merges.Add(CellRange.Parse("F1:G1"));
        Assert.Throws<BusinessException>(() => session.CreateTable(CellRange.Parse("F1:G3"), true));
    }

    [Fact]
    public void The_total_row_sums_the_visible_rows()
    {
        var session = Sales();
        var table = session.CreateTable(CellRange.Parse("A1:C4"), true);

        session.SetTotalsRow(table.Name, true);

        var sheet = session.ActiveSheet;
        var updated = sheet.Tables[0];
        Assert.Equal(CellRange.Parse("A1:C5"), updated.Range);
        Assert.Equal("Ergebnis", sheet["A5"].Text);
        Assert.Equal(7, sheet["C5"].Number, 9);
        session.Select(CellAddress.Parse("C5"));
        Assert.Equal("=TEILERGEBNIS(109;[Preis])", session.FormulaBarText);
        Assert.Equal("sum", updated.TotalsFunctions[2]);

        session.SetTotalsRow(table.Name, false);
        Assert.Equal(CellRange.Parse("A1:C4"), sheet.Tables[0].Range);
        Assert.True(sheet["C5"].IsBlank);
    }

    [Fact]
    public void Structured_references_are_shown_in_German_and_stored_in_English()
    {
        var session = Sales();
        session.CreateTable(CellRange.Parse("A1:C4"), true);
        session.Select(CellAddress.Parse("E1"));

        session.EnterInput("=SUMME(Tabelle1[Menge])");
        session.Select(CellAddress.Parse("E2"));
        session.EnterInput("=ANZAHL2(Tabelle1[[#Kopfzeilen];[Region]:[Preis]])");
        session.Select(CellAddress.Parse("D2"));
        session.EnterInput("=[@Menge]*[@Preis]");

        var sheet = session.ActiveSheet;
        Assert.Equal(21, sheet["E1"].Number);
        Assert.Equal(3, sheet["E2"].Number);
        Assert.Equal(25, sheet["D2"].Number);
        Assert.Equal("COUNTA(Tabelle1[[#Headers],[Region]:[Preis]])", FormulaFormatter.Format(sheet.GetFormula(1, 4)!, session.Workbook, new CellAddress(1, 4)));
        session.Select(CellAddress.Parse("E2"));
        Assert.Equal("=ANZAHL2(Tabelle1[[#Kopfzeilen];[Region]:[Preis]])", session.FormulaBarText);
    }

    [Theory]
    [InlineData("[[#Alle];[Menge]]", "[[#All],[Menge]]")]
    [InlineData("[#Ergebnisse]", "[#Totals]")]
    [InlineData("[[#Diese Zeile];[Preis]]", "[[#This Row],[Preis]]")]
    [InlineData("[[#Daten],[Spalte '#1]]", "[[#Data],[Spalte '#1]]")]
    [InlineData("[@Menge]", "[@Menge]")]
    public void Specifiers_translate_both_ways(string german, string english)
    {
        Assert.Equal(english, StructuredSpecifiers.ToCanonical(german));
        Assert.Equal(german.Replace(",", ";", StringComparison.Ordinal), StructuredSpecifiers.ToLocal(english, ';'));
    }

    [Fact]
    public void Typing_below_or_beside_extends_the_table()
    {
        var session = Sales();
        session.CreateTable(CellRange.Parse("A1:C4"), true);
        session.Select(CellAddress.Parse("F1"));
        session.EnterInput("=SUMME(Tabelle1[Menge])");

        session.Select(CellAddress.Parse("A5"));
        session.EnterInput("West");
        session.Select(CellAddress.Parse("B5"));
        session.EnterInput("5");
        Assert.Equal(26, session.ActiveSheet["F1"].Number);
        session.Select(CellAddress.Parse("D1"));
        session.EnterInput("Rabatt");

        var table = session.ActiveSheet.Tables[0];
        Assert.Equal(CellRange.Parse("A1:D5"), table.Range);
        Assert.Equal(["Region", "Menge", "Preis", "Rabatt"], table.Columns);
        session.Undo();
        Assert.Equal(CellRange.Parse("A1:C5"), session.ActiveSheet.Tables[0].Range);
        session.Undo();
        session.Undo();
        Assert.Equal(CellRange.Parse("A1:C4"), session.ActiveSheet.Tables[0].Range);
        Assert.Equal(21, session.ActiveSheet["F1"].Number);
    }

    [Fact]
    public void Renaming_a_table_or_a_column_rewrites_the_formulas()
    {
        var session = Sales();
        session.CreateTable(CellRange.Parse("A1:C4"), true);
        session.Select(CellAddress.Parse("E1"));
        session.EnterInput("=SUMME(Tabelle1[Menge])");

        session.RenameTable("Tabelle1", "Verkauf");
        session.Select(CellAddress.Parse("B1"));
        session.EnterInput("Stück");

        session.Select(CellAddress.Parse("E1"));
        Assert.Equal("=SUMME(Verkauf[Stück])", session.FormulaBarText);
        Assert.Equal(21, session.ActiveSheet["E1"].Number);
        Assert.Throws<BusinessException>(() => session.RenameTable("Verkauf", "A1"));
        Assert.Throws<BusinessException>(() => session.RenameTable("Verkauf", "mit Leerzeichen"));
    }

    [Fact]
    public void A_table_filters_its_own_rows()
    {
        var session = Sales();
        session.CreateTable(CellRange.Parse("A1:C4"), true);
        session.Select(CellAddress.Parse("A1"));

        Assert.Equal(CellRange.Parse("A1:C4"), session.ActiveFilter?.Range);
        Assert.Equal(["Nord", "Ost", "Süd"], session.FilterValues(0));
        session.SetFilter(0, new FilterColumn { Values = ["Nord"] });

        Assert.True(session.ActiveSheet.Rows.IsHidden(2));
        Assert.False(session.ActiveSheet.Rows.IsHidden(1));
        Assert.Null(session.ActiveSheet.AutoFilter);
        Assert.Single(session.ActiveSheet.Tables[0].Filter!.Columns);
        session.ClearFilters();
        Assert.False(session.ActiveSheet.Rows.IsHidden(2));
    }

    [Fact]
    public void Converting_to_a_range_keeps_the_look_and_the_formulas_work()
    {
        var session = Sales();
        session.CreateTable(CellRange.Parse("A1:C4"), true);
        session.Select(CellAddress.Parse("E1"));
        session.EnterInput("=SUMME(Tabelle1[Menge])");

        session.ConvertTableToRange("Tabelle1");

        var sheet = session.ActiveSheet;
        Assert.Empty(sheet.Tables);
        Assert.True(sheet.GetStyle(0, 0).Font.Bold);
        Assert.NotEqual(FillPattern.None, sheet.GetStyle(0, 0).Fill.Pattern);
        Assert.Equal(21, sheet["E1"].Number);
        session.Select(CellAddress.Parse("E1"));
        Assert.Equal("=SUMME(Umsatz!$B$2:$B$4)", session.FormulaBarText);
        session.Undo();
        Assert.Single(sheet.Tables);
    }

    [Fact]
    public void The_grid_draws_header_bands_and_arrows()
    {
        var session = Sales();
        session.CreateTable(CellRange.Parse("A1:C4"), true);
        var metrics = new GridMetrics(new ThixOffice.Text.FixedWidthTextMeasurer(), session.Workbook, CalcLocale.German);
        var viewport = new SheetLayout(session.ActiveSheet, metrics).Viewport(600, 300, 1);

        var header = GridRenderer.Present(session.ActiveSheet, 0, 0, metrics);
        var band = GridRenderer.Present(session.ActiveSheet, 1, 0, metrics);
        var plain = GridRenderer.Present(session.ActiveSheet, 2, 0, metrics);

        Assert.Equal(session.Workbook.Theme[4], header.Fill);
        Assert.True(header.Style.Font.Bold);
        Assert.Equal(ThixOffice.Text.Drawing.RgbColor.White, header.TextColor);
        Assert.NotNull(band.Fill);
        Assert.Null(plain.Fill);
        Assert.NotNull(GridRenderer.FilterButtonRect(viewport, new CellAddress(0, 2)));
        Assert.Null(GridRenderer.FilterButtonRect(viewport, new CellAddress(1, 2)));
    }

    [Fact]
    public void Tables_round_trip_through_xlsx()
    {
        var session = Sales();
        var table = session.CreateTable(CellRange.Parse("A1:C4"), true, "TableStyleMedium4");
        session.SetTotalsRow(table.Name, true);
        session.SetTableOptions(table.Name, current => current with { BandedRows = false, FirstColumn = true });
        session.Select(CellAddress.Parse("E1"));
        session.EnterInput("=SUMME(Tabelle1[Menge])");

        var bytes = XlsxTests.Write(session.Workbook);
        XlsxTests.AssertValid(bytes);
        var read = XlsxTests.Read(bytes);

        var back = Assert.Single(read["Umsatz"].Tables);
        Assert.Equal("Tabelle1", back.Name);
        Assert.Equal(CellRange.Parse("A1:C5"), back.Range);
        Assert.Equal(1, back.TotalsRowCount);
        Assert.Equal("TableStyleMedium4", back.Style);
        Assert.False(back.BandedRows);
        Assert.True(back.FirstColumn);
        Assert.Equal("Ergebnis", back.TotalsLabels[0]);
        Assert.Equal("sum", back.TotalsFunctions[2]);
        Assert.Equal("SUM(Tabelle1[Menge])", FormulaFormatter.Format(read["Umsatz"].GetFormula(0, 4)!, read, new CellAddress(0, 4)));

        // Changed again: the same part, still valid; deleted: the part goes.
        var again = new EditingSession(read) { Locale = CalcLocale.German };
        again.RenameTable("Tabelle1", "Verkauf");
        var renamed = XlsxTests.Write(read);
        XlsxTests.AssertValid(renamed);
        Assert.Equal("Verkauf", XlsxTests.Read(renamed)["Umsatz"].Tables[0].Name);
        again.ConvertTableToRange("Verkauf");
        var removed = XlsxTests.Write(read);
        XlsxTests.AssertValid(removed);
        Assert.Empty(XlsxTests.Read(removed)["Umsatz"].Tables);
    }
}
