using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using DocumentFormat.OpenXml.Validation;
using ThixCalc.Core.Formats.Xlsx;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using X = DocumentFormat.OpenXml.Spreadsheet;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Formats;

/// <summary>xlsx round trips (model → file → model), validation, preserving what the model does not own, Excel fixtures.</summary>
public class XlsxTests
{
    internal static byte[] Write(Workbook workbook, bool keepMacros = true)
    {
        using var stream = new MemoryStream();
        XlsxWriter.Write(workbook, stream, keepMacros);
        return stream.ToArray();
    }

    internal static Workbook Read(byte[] bytes) => XlsxReader.Read(new MemoryStream(bytes));

    internal static void AssertValid(byte[] bytes)
    {
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var errors = new OpenXmlValidator(FileFormatVersions.Office2019).Validate(package)
            .Where(error => !error.Description.Contains("cm", StringComparison.Ordinal))
            .ToList();
        Assert.True(errors.Count == 0, string.Join("\n", errors.Select(error => $"{error.Part?.Uri} {error.Path?.XPath}: {error.Description}")));
    }

    internal static Workbook RoundTrip(Workbook workbook)
    {
        var bytes = Write(workbook);
        AssertValid(bytes);
        var read = Read(bytes);
        read.Calculation.Clock = () => Now;
        read.Calculation.Locale = workbook.Calculation.Locale;
        return read;
    }

    private static Workbook Sample()
    {
        var workbook = Create("Daten", "Auswertung");
        var data = workbook["Daten"];
        var report = workbook["Auswertung"];
        Fill(data, ("A1", "Artikel"), ("B1", "Menge"), ("C1", "Preis"), ("D1", "Summe"),
            ("A2", "Kabel"), ("B2", 3), ("C2", 4.5),
            ("A3", "Stecker"), ("B3", 10), ("C3", 2.25),
            ("A4", "Lampe"), ("B4", 1), ("C4", 12),
            ("A6", " führende Leerzeichen "), ("B6", true), ("C6", CellError.NotAvailable), ("A7", "Zeile1\nZeile2"), ("A8", "Steuer\u0001zeichen _x0041_"));
        var line = FormulaParser.Parse("B2*C2", workbook, data, new CellAddress(1, 3));
        for (var row = 1; row <= 3; row++)
        {
            data.SetFormula(row, 3, line);
        }

        Fill(report, ("A1", "=SUM(Daten!D2:D4)"), ("A2", "=XLOOKUP(\"Lampe\",Daten!A2:A4,Daten!C2:C4)"), ("A3", "=SEQUENCE(3)"),
            ("B1", "=Gesamt*2"), ("C1", "=TEXT(A1,\"0.00\")"), ("D1", "=Daten!A2&\" \"&Daten!B2"));
        report.SetFormula(5, 0, FormulaParser.Parse("{1,2;3,4}*10", workbook, report, new CellAddress(5, 0)), dynamicArray: false, arrayRange: CellRange.Parse("A6:B7"));
        workbook.Names.Add(new DefinedName("Gesamt", "Auswertung!$A$1"));
        workbook.Names.Add(new DefinedName("Lokal", "Daten!$B$2:$B$4", data.Id));

        var bold = workbook.Styles.Intern(CellStyle.Default with
        {
            Font = CellFont.Default with { Bold = true, Color = CalcColor.FromRgb(0x10, 0x7C, 0x41) },
            Fill = CellFill.Solid(CalcColor.FromTheme(4, 0.4)),
            Borders = CellBorders.All(BorderStyle.Thin, CalcColor.FromIndex(8)),
            Alignment = CellAlignment.Default with { Horizontal = HorizontalAlignment.Center, WrapText = true },
        });
        var money = workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "#,##0.00 \"€\"" });
        var date = workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "m/d/yyyy" });
        foreach (var column in new[] { 0, 1, 2, 3 })
        {
            data.SetStyleId(0, column, bold);
        }

        data.SetStyleId(1, 2, money);
        data.SetValue(9, 0, 45458);
        data.SetStyleId(9, 0, date);
        data.Columns.Set(0, new AxisInfo(Size: 20, CustomSize: true));
        data.Columns.Set(5, new AxisInfo(Hidden: true));
        data.Rows.Set(0, new AxisInfo(Size: 30, CustomSize: true));
        data.Rows.Set(7, new AxisInfo(OutlineLevel: 1, Hidden: true));
        data.Merges.Add(CellRange.Parse("A12:C13"));
        data.View = data.View with { FrozenRows = 1, FrozenColumns = 1, Zoom = 120, ActiveCell = new CellAddress(1, 1), Selection = [CellRange.Parse("B2")] };
        data.TabColor = CalcColor.FromRgb(255, 0, 0);
        data.Validations.Add(new DataValidation { Ranges = [CellRange.Parse("B2:B4")], Type = ValidationType.WholeNumber, Operator = ComparisonOperator.GreaterThan, Formula1 = "0", Error = "Nur positive Mengen" });
        data.Validations.Add(new DataValidation { Ranges = [CellRange.Parse("A2:A4")], Type = ValidationType.List, Formula1 = "\"Kabel,Stecker,Lampe\"" });
        workbook.Styles.DifferentialFormats.Add(new DifferentialFormat { FillColor = CalcColor.FromRgb(255, 199, 206), FontColor = CalcColor.FromRgb(156, 0, 6) });
        data.ConditionalFormats.Add(new ConditionalFormat([CellRange.Parse("D2:D4")], [
            new ConditionalFormatRule { Type = ConditionalFormatType.CellIs, Operator = ComparisonOperator.GreaterThan, Formulas = ["15"], FormatId = 0, Priority = 1 },
            new ConditionalFormatRule { Type = ConditionalFormatType.ColorScale, Priority = 2, Thresholds = [new(ConditionalValueType.Min), new(ConditionalValueType.Max)], Colors = [CalcColor.FromRgb(255, 255, 255), CalcColor.FromRgb(99, 190, 123)] },
            new ConditionalFormatRule { Type = ConditionalFormatType.Expression, Priority = 3, Formulas = ["$B2>5"], FormatId = 0 },
        ]));
        data.AutoFilter = new AutoFilter(CellRange.Parse("A1:D4"), [new FilterColumn { ColumnOffset = 0, Values = ["Kabel", "Lampe"] }]);
        data.Hyperlinks.Add(new Hyperlink(CellRange.Parse("A15"), "https://example.com/artikel", Display: "Katalog", Tooltip: "Öffnet den Katalog"));
        data.Hyperlinks.Add(new Hyperlink(CellRange.Parse("A16"), null, "Auswertung!A1"));
        data.Comments[new CellAddress(1, 0)] = new CellComment("Anna", "Bitte prüfen");
        report.Visibility = SheetVisibility.Hidden;
        workbook.Calculation.Recalculate();
        return workbook;
    }

    [Fact]
    public void A_workbook_round_trips_with_everything_the_model_owns()
    {
        var original = Sample();
        var read = RoundTrip(original);
        var data = read["Daten"];
        var report = read["Auswertung"];

        Assert.Equal(["Daten", "Auswertung"], read.Sheets.Select(sheet => sheet.Name));
        Assert.Equal(" führende Leerzeichen ", data["A6"].Text);
        Assert.Equal("Zeile1\nZeile2", data["A7"].Text);
        Assert.Equal("Steuer\u0001zeichen _x0041_", data["A8"].Text);
        Assert.True(data["B6"].Boolean);
        Assert.Equal(CellError.NotAvailable, data["C6"].Error);
        Assert.Equal(13.5, data["D2"].Number);
        Assert.Same(data.GetFormula(1, 3), data.GetFormula(3, 3));
        Assert.Equal("B4*C4", FormulaFormatter.Format(data.GetFormula(3, 3)!, read, new CellAddress(3, 3)));
        Assert.Equal(48, report["A1"].Number);
        Assert.Equal(12, report["A2"].Number);
        Assert.Equal(3, report["A5"].Number);
        Assert.True(report.GetCell(4, 0).IsSpilled);
        Assert.True(report.GetCell(2, 0).IsDynamicArray);
        Assert.Equal(96, report["B1"].Number);
        Assert.Equal(CellRange.Parse("A6:B7"), report.GetCell(5, 0).ArrayRange);
        Assert.Equal(40, report["B7"].Number);
        Assert.Equal(SheetVisibility.Hidden, report.Visibility);

        // Recalculating the read workbook gives the same values (the cached ones were right).
        read.Calculation.RecalculateAll();
        Assert.Equal(48, report["A1"].Number);
        Assert.Equal("Kabel 3", report["D1"].Text);

        var header = data.GetStyle(0, 0);
        Assert.True(header.Font.Bold);
        Assert.Equal(CalcColor.FromRgb(0x10, 0x7C, 0x41), header.Font.Color);
        Assert.Equal(FillPattern.Solid, header.Fill.Pattern);
        Assert.Equal(CalcColor.FromTheme(4, 0.4), header.Fill.Foreground);
        Assert.Equal(BorderStyle.Thin, header.Borders.Left.Style);
        Assert.Equal(HorizontalAlignment.Center, header.Alignment.Horizontal);
        Assert.True(header.Alignment.WrapText);
        Assert.Equal("#,##0.00 \"€\"", data.GetStyle(1, 2).NumberFormat);
        Assert.Equal("m/d/yyyy", data.GetStyle(9, 0).NumberFormat);

        Assert.Equal(20, data.Columns[0].Size);
        Assert.True(data.Columns[5].Hidden);
        Assert.Equal(30, data.Rows[0].Size);
        Assert.Equal(1, data.Rows[7].OutlineLevel);
        Assert.Equal([CellRange.Parse("A12:C13")], data.Merges.Ranges);
        Assert.Equal(1, data.View.FrozenRows);
        Assert.Equal(1, data.View.FrozenColumns);
        Assert.Equal(120, data.View.Zoom);
        Assert.Equal(new CellAddress(1, 1), data.View.ActiveCell);
        Assert.Equal(CalcColor.FromRgb(255, 0, 0), data.TabColor);
        Assert.Equal(2, data.Validations.Count);
        Assert.Equal(ValidationType.WholeNumber, data.Validations[0].Type);
        Assert.Equal("Nur positive Mengen", data.Validations[0].Error);
        Assert.Equal("\"Kabel,Stecker,Lampe\"", data.Validations[1].Formula1);
        Assert.Equal(3, data.ConditionalFormats[0].Rules.Count);
        Assert.Equal(["$B2>5"], data.ConditionalFormats[0].Rules[2].Formulas);
        Assert.Equal(ConditionalFormatType.ColorScale, data.ConditionalFormats[0].Rules[1].Type);
        Assert.Equal(CalcColor.FromRgb(255, 199, 206), read.Styles.DifferentialFormats[0].FillColor);
        Assert.Equal(CellRange.Parse("A1:D4"), data.AutoFilter!.Range);
        Assert.Equal(["Kabel", "Lampe"], data.AutoFilter.Columns[0].Values);
        Assert.Equal("https://example.com/artikel", data.Hyperlinks[0].Target);
        Assert.Equal("Auswertung!A1", data.Hyperlinks[1].Location);
        Assert.Equal("Bitte prüfen", data.Comments[new CellAddress(1, 0)].Text);
        Assert.Equal("Anna", data.Comments[new CellAddress(1, 0)].Author);
        Assert.Contains(read.Names, name => name.Name == "Lokal" && name.SheetId == data.Id && name.Formula == "Daten!$B$2:$B$4");
    }

    [Fact]
    public void Writing_twice_gives_the_same_model()
    {
        var once = RoundTrip(Sample());
        var twice = RoundTrip(once);
        foreach (var sheet in once.Sheets)
        {
            var other = twice[sheet.Name];
            Assert.Equal(sheet.GetCells().Select(cell => (cell.Address, cell.Cell.Value, cell.Cell.StyleId)), other.GetCells().Select(cell => (cell.Address, cell.Cell.Value, cell.Cell.StyleId)));
        }
    }

    [Fact]
    public void Formulas_use_file_prefixes_and_dynamic_array_metadata()
    {
        var bytes = Write(Sample());
        using var package = SpreadsheetDocument.Open(new MemoryStream(bytes), false);
        var report = package.WorkbookPart!.WorksheetParts.Select(part => part.Worksheet!).First(sheet => sheet.OuterXml.Contains("XLOOKUP", StringComparison.Ordinal));
        Assert.Contains("_xlfn.XLOOKUP(", report.OuterXml, StringComparison.Ordinal);
        Assert.Contains("_xlfn.SEQUENCE(3)", report.OuterXml, StringComparison.Ordinal);
        Assert.NotNull(package.WorkbookPart.CellMetadataPart);
        var data = package.WorkbookPart.WorksheetParts.Select(part => part.Worksheet!).First(sheet => sheet.OuterXml.Contains("Artikel", StringComparison.Ordinal) || sheet.Descendants<X.CellFormula>().Any(formula => formula.FormulaType?.Value == X.CellFormulaValues.Shared));
        Assert.Contains(data.Descendants<X.CellFormula>(), formula => formula.FormulaType?.Value == X.CellFormulaValues.Shared && formula.Reference?.Value == "D2:D4");
    }

    [Fact]
    public void A_new_workbook_is_valid()
    {
        var workbook = Workbook.Create();
        var bytes = Write(workbook);
        AssertValid(bytes);
        Assert.Single(Read(bytes).Sheets);
    }
}
