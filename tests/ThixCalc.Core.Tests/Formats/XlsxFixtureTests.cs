using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Tests.Formats;

/// <summary>
/// A package as Excel writes it, generated with the SDK: shared formulas, a dynamic array with its metadata, a function of
/// the future with a cached value, a table, a picture, page setup, an extension list and a VBA project. ThixCalc reads the
/// values without recalculating, names what it does not show, and writes everything back.
/// </summary>
public class XlsxFixtureTests
{
    private const string Main = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    internal static byte[] ExcelLikeWorkbook()
    {
        using var stream = new MemoryStream();
        using (var package = SpreadsheetDocument.Create(stream, SpreadsheetDocumentType.MacroEnabledWorkbook))
        {
            var workbookPart = package.AddWorkbookPart();
            var sheetPart = workbookPart.AddNewPart<WorksheetPart>("rId1");
            var stringsPart = workbookPart.AddNewPart<SharedStringTablePart>("rId3");
            stringsPart.SharedStringTable = new X.SharedStringTable(
                new X.SharedStringItem(new X.Text("Name")),
                new X.SharedStringItem(new X.Text("Wert")),
                new X.SharedStringItem(new X.Run(new X.RunProperties(new X.Bold()), new X.Text("fett")), new X.Run(new X.Text(" normal"))));
            var stylesPart = workbookPart.AddNewPart<WorkbookStylesPart>("rId4");
            stylesPart.Stylesheet = new X.Stylesheet(
                new X.Fonts(new X.Font(new X.FontSize { Val = 11 }, new X.FontName { Val = "Calibri" })) { Count = 1 },
                new X.Fills(new X.Fill(new X.PatternFill { PatternType = X.PatternValues.None }), new X.Fill(new X.PatternFill { PatternType = X.PatternValues.Gray125 })) { Count = 2 },
                new X.Borders(new X.Border()) { Count = 1 },
                new X.CellStyleFormats(new X.CellFormat()) { Count = 1 },
                new X.CellFormats(new X.CellFormat(), new X.CellFormat { NumberFormatId = 14, ApplyNumberFormat = true }) { Count = 2 },
                new X.CellStyles(new X.CellStyle { Name = "Standard", FormatId = 0, BuiltinId = 0 }) { Count = 1 });
            var metadataPart = workbookPart.AddNewPart<CellMetadataPart>("rId5");
            using (var metadata = new MemoryStream(System.Text.Encoding.UTF8.GetBytes(
                "<metadata xmlns=\"" + Main + "\" xmlns:xda=\"http://schemas.microsoft.com/office/spreadsheetml/2017/dynamicarray\">"
                + "<metadataTypes count=\"1\"><metadataType name=\"XLDAPR\" minSupportedVersion=\"120000\" copy=\"1\" pasteAll=\"1\" pasteValues=\"1\" merge=\"1\" splitFirst=\"1\" rowColShift=\"1\" clearFormats=\"1\" clearComments=\"1\" assign=\"1\" coerce=\"1\" cellMeta=\"1\"/></metadataTypes>"
                + "<futureMetadata name=\"XLDAPR\" count=\"1\"><bk><extLst><ext uri=\"{bdbb8cdc-fa1e-496e-a857-3c3f30c029c3}\"><xda:dynamicArrayProperties fDynamic=\"1\" fCollapsed=\"0\"/></ext></extLst></bk></futureMetadata>"
                + "<cellMetadata count=\"1\"><bk><rc t=\"1\" v=\"0\"/></bk></cellMetadata></metadata>")))
            {
                metadataPart.FeedData(metadata);
            }

            var vba = workbookPart.AddNewPart<VbaProjectPart>("rId6");
            using (var bytes = new MemoryStream([1, 2, 3, 4]))
            {
                vba.FeedData(bytes);
            }

            var table = sheetPart.AddNewPart<TableDefinitionPart>("rId2");
            table.Table = new X.Table(
                new X.AutoFilter { Reference = "A1:B3" },
                new X.TableColumns(new X.TableColumn { Id = 1, Name = "Name" }, new X.TableColumn { Id = 2, Name = "Wert" }) { Count = 2 },
                new X.TableStyleInfo { Name = "TableStyleMedium2", ShowRowStripes = true })
            { Id = 1, Name = "Tabelle1", DisplayName = "Tabelle1", Reference = "A1:B3" };

            var drawing = sheetPart.AddNewPart<DrawingsPart>("rId3");
            var image = drawing.AddImagePart(ImagePartType.Png, "rId1");
            using (var png = new MemoryStream([0x89, 0x50, 0x4E, 0x47]))
            {
                image.FeedData(png);
            }

            drawing.WorksheetDrawing = new DocumentFormat.OpenXml.Drawing.Spreadsheet.WorksheetDrawing();

            var sheetData = new X.SheetData(
                new X.Row(
                    new X.Cell { CellReference = "A1", DataType = X.CellValues.SharedString, CellValue = new X.CellValue("0") },
                    new X.Cell { CellReference = "B1", DataType = X.CellValues.SharedString, CellValue = new X.CellValue("1") },
                    new X.Cell { CellReference = "D1", CellFormula = new X.CellFormula("_xlfn.FUTUREFN(B2)"), CellValue = new X.CellValue("42") },
                    new X.Cell { CellReference = "E1", CellMetaIndex = 1, CellFormula = new X.CellFormula("_xlfn.SEQUENCE(2)") { FormulaType = X.CellFormulaValues.Array, Reference = "E1:E2" }, CellValue = new X.CellValue("1") })
                { RowIndex = 1 },
                new X.Row(
                    new X.Cell { CellReference = "A2", DataType = X.CellValues.SharedString, CellValue = new X.CellValue("2") },
                    new X.Cell { CellReference = "B2", CellValue = new X.CellValue("10") },
                    new X.Cell { CellReference = "C2", CellFormula = new X.CellFormula("B2*2") { FormulaType = X.CellFormulaValues.Shared, Reference = "C2:C3", SharedIndex = 0 }, CellValue = new X.CellValue("20") },
                    new X.Cell { CellReference = "E2", CellValue = new X.CellValue("2") },
                    new X.Cell { CellReference = "F2", StyleIndex = 1, CellValue = new X.CellValue("45458") })
                { RowIndex = 2 },
                new X.Row(
                    new X.Cell { CellReference = "A3", DataType = X.CellValues.InlineString, InlineString = new X.InlineString(new X.Text("inline")) },
                    new X.Cell { CellReference = "B3", CellValue = new X.CellValue("5") },
                    new X.Cell { CellReference = "C3", CellFormula = new X.CellFormula { FormulaType = X.CellFormulaValues.Shared, SharedIndex = 0 }, CellValue = new X.CellValue("10") })
                { RowIndex = 3 });
            var worksheet = new X.Worksheet(
                new X.SheetDimension { Reference = "A1:F3" },
                new X.SheetViews(new X.SheetView { WorkbookViewId = 0, TabSelected = true }),
                new X.SheetFormatProperties { DefaultRowHeight = 15, DyDescent = 0.25 },
                sheetData,
                new X.PageMargins { Left = 0.7, Right = 0.7, Top = 0.78740157499999996, Bottom = 0.78740157499999996, Header = 0.3, Footer = 0.3 },
                new X.PageSetup { PaperSize = 9, Orientation = X.OrientationValues.Landscape },
                new X.HeaderFooter(new X.OddHeader("&CKopfzeile")),
                new X.Drawing { Id = "rId3" },
                new X.TableParts(new X.TablePart { Id = "rId2" }) { Count = 1 },
                new X.WorksheetExtensionList(new X.WorksheetExtension(new DocumentFormat.OpenXml.Office2010.Excel.SparklineGroups(new DocumentFormat.OpenXml.Office2010.Excel.SparklineGroup(new DocumentFormat.OpenXml.Office2010.Excel.Sparklines(new DocumentFormat.OpenXml.Office2010.Excel.Sparkline(new DocumentFormat.OpenXml.Office.Excel.Formula("'Übersicht'!B2:B3"), new DocumentFormat.OpenXml.Office.Excel.ReferenceSequence("G2"))))))
                    { Uri = "{05C60535-1F16-4fd2-B633-F4F36F0B64E0}" }));
            worksheet.AddNamespaceDeclaration("r", "http://schemas.openxmlformats.org/officeDocument/2006/relationships");
            worksheet.AddNamespaceDeclaration("mc", "http://schemas.openxmlformats.org/markup-compatibility/2006");
            worksheet.AddNamespaceDeclaration("x14ac", "http://schemas.microsoft.com/office/spreadsheetml/2009/9/ac");
            worksheet.MCAttributes = new MarkupCompatibilityAttributes { Ignorable = "x14ac" };
            sheetPart.Worksheet = worksheet;

            workbookPart.Workbook = new X.Workbook(
                new X.BookViews(new X.WorkbookView()),
                new X.Sheets(new X.Sheet { Name = "Übersicht", SheetId = 1, Id = "rId1" }),
                new X.DefinedNames(new X.DefinedName("'Übersicht'!$A$1:$B$3") { Name = "_xlnm._FilterDatabase", LocalSheetId = 0, Hidden = true }),
                new X.CalculationProperties { CalculationId = 191029 });
        }

        return stream.ToArray();
    }

    [Fact]
    public void An_Excel_package_is_read_with_its_cached_values()
    {
        var workbook = XlsxTests.Read(ExcelLikeWorkbook());
        var sheet = workbook["Übersicht"];
        Assert.Equal("Name", sheet["A1"].Text);
        Assert.Equal("fett normal", sheet["A2"].Text);
        Assert.Equal("inline", sheet["A3"].Text);
        Assert.Equal(42, sheet["D1"].Number);
        Assert.True(sheet.GetFormula(0, 3)!.HasUnknownFunctions);
        Assert.Equal(10, sheet["C3"].Number);
        Assert.Same(sheet.GetFormula(1, 2), sheet.GetFormula(2, 2));
        Assert.True(sheet.GetCell(0, 4).IsDynamicArray);
        Assert.True(sheet.GetCell(1, 4).IsSpilled);
        Assert.Equal("m/d/yyyy", sheet.GetStyle(1, 5).NumberFormat);
        Assert.Single(sheet.Tables);
        Assert.Equal(["Name", "Wert"], sheet.Tables[0].Columns);
        Assert.True(workbook.HasMacros);
        Assert.Contains("Bilder", workbook.UnsupportedContent);
        Assert.Contains("Makros (werden beibehalten, aber nie ausgeführt)", workbook.UnsupportedContent);
        Assert.Contains("Sparklines", workbook.UnsupportedContent);
        Assert.Contains("Formatierter Text in Zellen (wird als einfacher Text gespeichert)", workbook.UnsupportedContent);

        // Nothing was recalculated on load: the unknown function keeps its cached value until a recalculation.
        Assert.False(workbook.Calculation.NeedsRecalculation);
        workbook.Calculation.RecalculateAll();
        Assert.Equal(CellError.Name, sheet["D1"].Error);
        Assert.Equal(20, sheet["C2"].Number);
    }

    [Fact]
    public void Writing_keeps_what_the_model_does_not_own()
    {
        var workbook = XlsxTests.Read(ExcelLikeWorkbook());
        var sheet = workbook["Übersicht"];
        sheet.SetValue(1, 1, 7);
        workbook.Calculation.Recalculate();
        Assert.Equal(CellError.Name, sheet["D1"].Error);
        var bytes = XlsxTests.Write(workbook);
        XlsxTests.AssertValid(bytes);

        using (var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false))
        {
            Assert.Equal(SpreadsheetDocumentType.MacroEnabledWorkbook, package.DocumentType);
            var workbookPart = package.WorkbookPart!;
            Assert.NotNull(workbookPart.VbaProjectPart);
            var part = workbookPart.WorksheetParts.Single();
            Assert.Single(part.TableDefinitionParts);
            Assert.Single(part.DrawingsPart!.ImageParts);
            var xml = part.Worksheet!.OuterXml;
            Assert.Contains("pageSetup", xml, StringComparison.Ordinal);
            Assert.Contains("Kopfzeile", xml, StringComparison.Ordinal);
            Assert.Contains("sparklineGroups", xml, StringComparison.Ordinal);
            Assert.Contains("_xlfn.FUTUREFN(B2)", xml, StringComparison.Ordinal);
            Assert.Contains("tablePart", xml, StringComparison.Ordinal);
            Assert.Contains("mc:Ignorable=\"x14ac\"", xml, StringComparison.Ordinal);
        }

        var reread = XlsxTests.Read(bytes);
        Assert.Equal(14, reread["Übersicht"]["C2"].Number);
        Assert.Equal(CellError.Name, reread["Übersicht"]["D1"].Error);
        Assert.Contains(reread.Names, name => name.Name == "_xlnm._FilterDatabase" && name.Hidden);
    }

    [Fact]
    public void Saving_a_macro_workbook_as_xlsx_drops_the_macros()
    {
        var workbook = XlsxTests.Read(ExcelLikeWorkbook());
        var bytes = XlsxTests.Write(workbook, keepMacros: false);
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Equal(SpreadsheetDocumentType.Workbook, package.DocumentType);
        Assert.Null(package.WorkbookPart!.VbaProjectPart);
    }

    [Fact]
    public void Sheet_formulas_survive_renaming_in_the_file()
    {
        var workbook = XlsxTests.Read(ExcelLikeWorkbook());
        var other = workbook.AddSheet("Bericht");
        TestBooks.Set(other, "A1", "='Übersicht'!B2+1");
        workbook.RenameSheet(workbook["Übersicht"], "Basis Daten");
        workbook.Calculation.Recalculate();
        var reread = XlsxTests.RoundTrip(workbook);
        Assert.Equal("'Basis Daten'!B2+1", FormulaFormatter.Format(reread["Bericht"].GetFormula(0, 0)!, reread, default));
        Assert.Equal(11, reread["Bericht"]["A1"].Number);
    }
[Fact]
    public void Inserted_rows_move_tables_and_structured_references_evaluate()
    {
        var workbook = XlsxTests.Read(ExcelLikeWorkbook());
        var session = new ThixCalc.Core.Editing.EditingSession(workbook);
        var sheet = workbook["Übersicht"];
        TestBooks.Set(sheet, "H1", "=SUM(Tabelle1[Wert])");
        workbook.Calculation.Recalculate();
        Assert.Equal(15, sheet["H1"].Number);
        session.InsertRows(0, 2);
        Assert.Equal(CellRange.Parse("A3:B5"), sheet.Tables[0].Range);
        Assert.Equal(15, sheet["H3"].Number);
        var bytes = XlsxTests.Write(workbook);
        XlsxTests.AssertValid(bytes);
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        Assert.Equal("A3:B5", package.WorkbookPart!.WorksheetParts.Single().TableDefinitionParts.Single().Table!.Reference!.Value);
    }
}
