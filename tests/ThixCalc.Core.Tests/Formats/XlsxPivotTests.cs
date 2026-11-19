using System.Text;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixCalc.Core.Model.Pivot;
using ThixCalc.Core.Tests.Editing;
using ThixOffice.Core.Models;
using X = DocumentFormat.OpenXml.Spreadsheet;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Formats;

/// <summary>
/// PivotTables in xlsx: ThixCalc's pivots written in Excel's shape (valid, read back with the same fields, grouping, filters and the
/// same cells), a pivot in the exact XML Excel writes (read, computed alike, refreshed, written back untouched), unsupported ones kept.
/// </summary>
public class XlsxPivotTests
{
    private static List<string> Grid(Worksheet sheet, PivotTable pivot)
    {
        var range = pivot.Range!.Value;
        var lines = new List<string>();
        for (var row = range.FirstRow; row <= range.LastRow; row++)
        {
            lines.Add(string.Join("|", Enumerable.Range(range.FirstColumn, range.ColumnCount).Select(column => sheet.GetValue(row, column) is var value && value.IsNumber
                ? value.Number.ToString("R", System.Globalization.CultureInfo.InvariantCulture) : value.Text)));
        }

        return lines;
    }

    [Fact]
    public void A_pivot_with_columns_filters_and_date_groups_round_trips()
    {
        var session = PivotTests.Sales();
        var pivot = session.CreatePivotTable("Daten!A1:E11", new PivotDestination(NewSheet: true));
        pivot = session.AddPivotField(pivot, 2, PivotAxis.Row);
        pivot = session.AddPivotField(pivot, 1, PivotAxis.Column);
        pivot = session.AddPivotField(pivot, 0, PivotAxis.Filter);
        pivot = session.AddPivotField(pivot, 4);
        pivot = session.GroupPivotField(pivot, 2, PivotDateGroups.Years | PivotDateGroups.Quarters);
        pivot = session.ChangePivotField(pivot, 1, field => field with { HiddenItems = [PivotItems.KeyOf("Kirschen")] });
        pivot = session.SetPivotOptions(pivot, table => table with { Style = "PivotStyleMedium9", BandedRows = true });
        var sheet = session.ActiveSheet;
        var before = Grid(sheet, pivot);

        var bytes = XlsxTests.Write(session.Workbook);
        XlsxTests.AssertValid(bytes);
        var read = XlsxTests.Read(bytes);
        var readSheet = read[sheet.Name];
        var again = Assert.Single(readSheet.PivotTables);
        Assert.Null(again.Unsupported);
        Assert.Equal(pivot.RowFields.ToArray(), again.RowFields.ToArray());
        Assert.Equal(pivot.ColumnFields.ToArray(), again.ColumnFields.ToArray());
        Assert.Equal(pivot.FilterFields.ToArray(), again.FilterFields.ToArray());
        Assert.Equal(PivotDateGroups.Years | PivotDateGroups.Quarters, again.Fields[2].DateGroups);
        Assert.Contains(PivotItems.KeyOf("Kirschen"), again.Fields[1].HiddenItems);
        Assert.Equal("PivotStyleMedium9", again.Style);
        Assert.True(again.BandedRows);
        Assert.Equal("Summe von Menge", again.DataFields[0].Name);
        Assert.Equal(before, Grid(readSheet, again));

        // Computed again from the read cache, the same cells.
        var cache = read.FindPivotCache(again.CacheId)!;
        Assert.Equal(10, cache.RecordCount);
        var output = PivotEngine.Compute(again, cache)!;
        foreach (var (address, cell) in output.Cells)
        {
            Assert.Equal(cell.Value, readSheet.GetValue(address));
        }

        // Written again without a change: the pivot parts stay as they are.
        var unchanged = XlsxTests.Write(read);
        Assert.Equal(PivotParts(bytes), PivotParts(unchanged));
    }

    [Fact]
    public void Value_filters_sort_layout_and_show_as_survive()
    {
        var session = PivotTests.Sales();
        var pivot = session.CreatePivotTable("Daten!A1:E11", new PivotDestination(NewSheet: true));
        pivot = session.AddPivotField(pivot, 0);
        pivot = session.AddPivotField(pivot, 1, PivotAxis.Row);
        pivot = session.AddPivotField(pivot, 4);
        pivot = session.AddPivotField(pivot, 4);
        pivot = session.ChangePivotDataField(pivot, 1, data => data with { Name = "Anteil", ShowAs = PivotShowAs.PercentOfGrandTotal });
        pivot = session.ChangePivotField(pivot, 0, field => field with { ValueFilter = new PivotFilter(PivotFilterKind.Top, "2", DataField: 0), Sort = new PivotSort(true, 0) });
        pivot = session.ChangePivotField(pivot, 1, field => field with { LabelFilter = new PivotFilter(PivotFilterKind.BeginsWith, "B") });
        pivot = session.SetPivotOptions(pivot, table => table with { Layout = PivotLayout.Tabular, ColumnGrandTotals = false });
        var bytes = XlsxTests.Write(session.Workbook);
        XlsxTests.AssertValid(bytes);
        var again = XlsxTests.Read(bytes)[session.ActiveSheet.Name].PivotTables.Single();
        Assert.Equal(PivotLayout.Tabular, again.Layout);
        Assert.False(again.ColumnGrandTotals);
        Assert.Equal(new PivotFilter(PivotFilterKind.Top, "2", DataField: 0), again.Fields[0].ValueFilter);
        Assert.Equal(new PivotFilter(PivotFilterKind.BeginsWith, "B"), again.Fields[1].LabelFilter);
        Assert.Equal(new PivotSort(true, 0), again.Fields[0].Sort);
        Assert.Equal(PivotShowAs.PercentOfGrandTotal, again.DataFields[1].ShowAs);
        Assert.Equal("Anteil", again.DataFields[1].Name);
    }

    [Fact]
    public void Deleting_a_pivot_removes_its_parts_and_the_cache()
    {
        var session = PivotTests.Sales();
        var pivot = session.CreatePivotTable("Daten!A1:E11", new PivotDestination(NewSheet: true));
        pivot = session.AddPivotField(pivot, 0);
        var read = XlsxTests.Read(XlsxTests.Write(session.Workbook));
        var readSession = new EditingSession(read) { Locale = CalcLocale.German };
        readSession.DeletePivot(readSession.FindPivot(pivot.Name)!.Value.Pivot);
        var bytes = XlsxTests.Write(read);
        XlsxTests.AssertValid(bytes);
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Empty(package.WorkbookPart!.PivotTableCacheDefinitionParts);
        Assert.Null(package.WorkbookPart.Workbook!.PivotCaches);
    }

    /// <summary>The xlsx with a pivot exactly as Excel 365 (German) writes it: inline numbers in the records, rowItems, the style.</summary>
    internal static byte[] ExcelFixture()
    {
        var workbook = Create("Daten", "Auswertung");
        Fill(workbook["Daten"], ("A1", "Region"), ("B1", "Umsatz"), ("A2", "Nord"), ("B2", 10), ("A3", "Süd"), ("B3", 30), ("A4", "Nord"), ("B4", 20), ("A5", "Süd"), ("B5", 40));
        Fill(workbook["Auswertung"], ("A3", "Zeilenbeschriftungen"), ("B3", "Summe von Umsatz"), ("A4", "Nord"), ("B4", 30), ("A5", "Süd"), ("B5", 70), ("A6", "Gesamtergebnis"), ("B6", 100));
        var bytes = XlsxTests.Write(workbook);
        var stream = new MemoryStream();
        stream.Write(bytes);
        using (var package = SpreadsheetDocument.Open(stream, true))
        {
            var workbookPart = package.WorkbookPart!;
            var cachePart = workbookPart.AddNewPart<PivotTableCacheDefinitionPart>("rId9");
            var recordsPart = cachePart.AddNewPart<PivotTableCacheRecordsPart>("rId1");
            Feed(cachePart, """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <pivotCacheDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" r:id="rId1" refreshedBy="Anna" refreshedDate="45614.6" createdVersion="8" refreshedVersion="8" minRefreshableVersion="3" recordCount="4"><cacheSource type="worksheet"><worksheetSource ref="A1:B5" sheet="Daten"/></cacheSource><cacheFields count="2"><cacheField name="Region" numFmtId="0"><sharedItems count="2"><s v="Nord"/><s v="Süd"/></sharedItems></cacheField><cacheField name="Umsatz" numFmtId="0"><sharedItems containsSemiMixedTypes="0" containsString="0" containsNumber="1" containsInteger="1" minValue="10" maxValue="40"/></cacheField></cacheFields></pivotCacheDefinition>
                """);
            Feed(recordsPart, """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <pivotCacheRecords xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" xmlns:r="http://schemas.openxmlformats.org/officeDocument/2006/relationships" count="4"><r><x v="0"/><n v="10"/></r><r><x v="1"/><n v="30"/></r><r><x v="0"/><n v="20"/></r><r><x v="1"/><n v="40"/></r></pivotCacheRecords>
                """);
            workbookPart.Workbook!.PivotCaches = new X.PivotCaches(new X.PivotCache { CacheId = 5, Id = "rId9" });
            workbookPart.Workbook.Save();
            var sheetPart = (WorksheetPart)workbookPart.GetPartById(workbookPart.Workbook.Sheets!.Elements<X.Sheet>().ElementAt(1).Id!.Value!);
            var tablePart = sheetPart.AddNewPart<PivotTablePart>();
            tablePart.AddPart(cachePart);
            Feed(tablePart, """
                <?xml version="1.0" encoding="UTF-8" standalone="yes"?>
                <pivotTableDefinition xmlns="http://schemas.openxmlformats.org/spreadsheetml/2006/main" name="PivotTable1" cacheId="5" applyNumberFormats="0" applyBorderFormats="0" applyFontFormats="0" applyPatternFormats="0" applyAlignmentFormats="0" applyWidthHeightFormats="1" dataCaption="Werte" updatedVersion="8" minRefreshableVersion="3" useAutoFormatting="1" itemPrintTitles="1" createdVersion="8" indent="0" outline="1" outlineData="1" multipleFieldFilters="0"><location ref="A3:B6" firstHeaderRow="1" firstDataRow="1" firstDataCol="1"/><pivotFields count="2"><pivotField axis="axisRow" showAll="0"><items count="3"><item x="0"/><item x="1"/><item t="default"/></items></pivotField><pivotField dataField="1" showAll="0"/></pivotFields><rowFields count="1"><field x="0"/></rowFields><rowItems count="3"><i><x/></i><i><x v="1"/></i><i t="grand"><x/></i></rowItems><colItems count="1"><i/></colItems><dataFields count="1"><dataField name="Summe von Umsatz" fld="1" baseField="0" baseItem="0"/></dataFields><pivotTableStyleInfo name="PivotStyleLight16" showRowHeaders="1" showColHeaders="1" showRowStripes="0" showColStripes="0" showLastColumn="1"/></pivotTableDefinition>
                """);
        }

        return stream.ToArray();
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var data = new MemoryStream(Encoding.UTF8.GetBytes(xml.Trim()));
        part.FeedData(data);
    }

    private static Dictionary<string, string> PivotParts(byte[] bytes)
    {
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var result = new Dictionary<string, string>();
        foreach (var part in package.WorkbookPart!.PivotTableCacheDefinitionParts.Cast<OpenXmlPart>()
            .Concat(package.WorkbookPart.WorksheetParts.SelectMany(sheet => sheet.PivotTableParts)))
        {
            using var reader = new StreamReader(part.GetStream());
            result[part.Uri.ToString()] = reader.ReadToEnd();
        }

        return result;
    }

    [Fact]
    public void An_excel_pivot_is_read_computed_alike_refreshed_and_kept()
    {
        var bytes = ExcelFixture();
        XlsxTests.AssertValid(bytes);
        var workbook = XlsxTests.Read(bytes);
        Assert.DoesNotContain(workbook.UnsupportedContent, note => note.Contains("Pivot", StringComparison.Ordinal));
        var sheet = workbook["Auswertung"];
        var pivot = Assert.Single(sheet.PivotTables);
        Assert.Equal("PivotTable1", pivot.Name);
        Assert.Equal(5, pivot.CacheId);
        Assert.Equal([0], pivot.RowFields.ToArray());
        Assert.Equal(PivotAxis.Row, pivot.Fields[0].Axis);
        Assert.Equal("Summe von Umsatz", Assert.Single(pivot.DataFields).Name);
        Assert.Equal(CellRange.Parse("A3:B6"), pivot.Range);
        var cache = workbook.FindPivotCache(5)!;
        Assert.Equal(4, cache.RecordCount);
        Assert.Equal(40.0, cache.Value(1, 3).Number);

        // ThixCalc computes the same cells Excel wrote.
        var output = PivotEngine.Compute(pivot, cache)!;
        Assert.Equal(CellRange.Parse("A3:B6"), output.Range);
        foreach (var (address, cell) in output.Cells)
        {
            Assert.Equal(sheet.GetValue(address), cell.Value);
        }

        // Unchanged, written back as it was.
        var unchanged = XlsxTests.Write(workbook);
        Assert.Equal(PivotParts(bytes), PivotParts(unchanged));

        // The source changes and Aktualisieren follows; the file is written new and valid.
        var session = new EditingSession(workbook) { Locale = CalcLocale.German };
        workbook["Daten"].SetValue("B2", 15);
        session.ActivateSheet(1);
        pivot = session.RefreshPivot(pivot);
        Assert.Equal(35.0, sheet["B4"].Number);
        var refreshed = XlsxTests.Write(workbook);
        XlsxTests.AssertValid(refreshed);
        Assert.Equal(35.0, XlsxTests.Read(refreshed)["Auswertung"]["B4"].Number);
        Assert.Equal(15.0, XlsxTests.Read(refreshed).FindPivotCache(5)!.Value(1, 0).Number);
    }

    [Fact]
    public void A_pivot_with_a_calculated_field_is_shown_and_kept_but_not_edited()
    {
        var bytes = ExcelFixture();
        using (var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false))
        {
            _ = package;
        }

        var stream = new MemoryStream();
        stream.Write(bytes);
        using (var package = SpreadsheetDocument.Open(stream, true))
        {
            var cachePart = package.WorkbookPart!.PivotTableCacheDefinitionParts.Single();
            string xml;
            using (var reader = new StreamReader(cachePart.GetStream()))
            {
                xml = reader.ReadToEnd();
            }

            Feed(cachePart, xml.Replace("<cacheFields count=\"2\">", "<cacheFields count=\"3\">", StringComparison.Ordinal)
                .Replace("</cacheFields>", "<cacheField name=\"Doppelt\" numFmtId=\"0\" formula=\"Umsatz*2\" databaseField=\"0\"/></cacheFields>", StringComparison.Ordinal));
        }

        var workbook = XlsxTests.Read(stream.ToArray());
        var pivot = workbook["Auswertung"].PivotTables.Single();
        Assert.NotNull(pivot.Unsupported);
        Assert.Contains(workbook.UnsupportedContent, note => note.Contains("PivotTable1", StringComparison.Ordinal));
        var session = new EditingSession(workbook) { Locale = CalcLocale.German };
        Assert.Throws<BusinessException>(() => session.AddPivotField(pivot, 1, PivotAxis.Column));
        Assert.Equal(PivotParts(stream.ToArray()), PivotParts(XlsxTests.Write(workbook)));
    }
}
