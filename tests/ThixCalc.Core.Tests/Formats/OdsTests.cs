using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ThixCalc.Core.Formats;
using ThixCalc.Core.Formats.Ods;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using ThixOffice.Core.Formats.Odf;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Formats;

/// <summary>OpenDocument Spreadsheet: round trips (model → ods → model), formulas, number formats, the package's structure, robustness.</summary>
public class OdsTests
{
    internal static byte[] Write(Workbook workbook)
    {
        using var stream = new MemoryStream();
        OdsWriter.Write(workbook, stream);
        return stream.ToArray();
    }

    internal static Workbook Read(byte[] bytes) => OdsReader.Read(new MemoryStream(bytes));

    internal static Workbook RoundTrip(Workbook workbook)
    {
        var bytes = Write(workbook);
        AssertPackage(bytes);
        var read = Read(bytes);
        read.Calculation.Clock = () => Now;
        read.Calculation.Locale = workbook.Calculation.Locale;
        return read;
    }

    /// <summary>mimetype first and stored, every part in the manifest, every XML part well-formed with ODF's namespaces.</summary>
    internal static void AssertPackage(byte[] bytes)
    {
        Assert.Equal((byte)'P', bytes[0]);
        Assert.Equal(0, BitConverter.ToUInt16(bytes, 8)); // stored
        Assert.Equal("mimetype", Encoding.ASCII.GetString(bytes, 30, 8));
        Assert.Equal(OdsWriter.MimeType, Encoding.ASCII.GetString(bytes, 38, OdsWriter.MimeType.Length));

        using var zip = new ZipArchive(new MemoryStream(bytes));
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        var manifest = Load(zip, "META-INF/manifest.xml");
        var listed = manifest.Root!.Elements(OdfXml.ManifestNs + "file-entry").Select(entry => (string)entry.Attribute(OdfXml.ManifestNs + "full-path")!).ToHashSet();
        Assert.Contains("/", listed);
        foreach (var entry in zip.Entries.Where(entry => entry.FullName is not ("mimetype" or "META-INF/manifest.xml")))
        {
            Assert.Contains(entry.FullName, listed);
        }

        foreach (var path in new[] { "content.xml", "styles.xml", "settings.xml", "meta.xml" })
        {
            var xml = Load(zip, path);
            Assert.Equal(OdfXml.OfficeNs, xml.Root!.Name.Namespace);
        }

        var content = Load(zip, "content.xml");
        Assert.Equal("document-content", content.Root!.Name.LocalName);
        Assert.NotNull(content.Root.Element(OdfXml.OfficeNs + "body")?.Element(OdfXml.OfficeNs + "spreadsheet"));
        foreach (var element in content.Descendants())
        {
            Assert.NotEqual(XNamespace.None, element.Name.Namespace);
        }
    }

    internal static XDocument Load(ZipArchive zip, string path)
    {
        using var stream = zip.GetEntry(path)!.Open();
        return XDocument.Load(stream);
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
            ("A6", " führende  Leerzeichen "), ("B6", true), ("C6", CellError.NotAvailable), ("A7", "Zeile1\nZeile2\tmit Tab"),
            ("A15", "Katalog"), ("A16", "Zur Auswertung"));
        var line = FormulaParser.Parse("B2*C2", workbook, data, new CellAddress(1, 3));
        for (var row = 1; row <= 3; row++)
        {
            data.SetFormula(row, 3, line);
        }

        Fill(report, ("A1", "=SUM(Daten!D2:D4)"), ("A2", "=XLOOKUP(\"Lampe\",Daten!A2:A4,Daten!C2:C4)"), ("A3", "=SEQUENCE(3)"),
            ("B1", "=Gesamt*2"), ("C1", "=TEXT(A1,\"0.00\")"), ("D1", "=Daten!A2&\" \"&Daten!B2"), ("E1", "=IF(A1>40,TRUE,FALSE)"),
            ("F1", "=SUM(Daten!B:B)"), ("G1", "=1/0"), ("H1", "=STDEV.S(Daten!B2:B4)"), ("I1", "=-A1^2%"));
        report.SetFormula(5, 0, FormulaParser.Parse("{1,2;3,4}*10", workbook, report, new CellAddress(5, 0)), dynamicArray: false, arrayRange: CellRange.Parse("A6:B7"));
        workbook.Names.Add(new DefinedName("Gesamt", "Auswertung!$A$1"));
        workbook.Names.Add(new DefinedName("Lokal", "Daten!$B$2:$B$4", data.Id));
        workbook.Names.Add(new DefinedName("_xlnm.Print_Area", "Daten!$A$1:$D$16", data.Id));
        workbook.Names.Add(new DefinedName("_xlnm.Print_Titles", "Daten!$1:$1", data.Id));

        var bold = workbook.Styles.Intern(CellStyle.Default with
        {
            Font = CellFont.Default with { Bold = true, Italic = true, Size = 12, Name = "Arial", Underline = UnderlineKind.Single, Color = CalcColor.FromRgb(0x10, 0x7C, 0x41) },
            Fill = CellFill.Solid(CalcColor.FromRgb(0xDD, 0xEB, 0xF7)),
            Borders = CellBorders.All(BorderStyle.Thin, CalcColor.FromRgb(1, 2, 3)) with { Bottom = new BorderEdge(BorderStyle.Double, CalcColor.FromRgb(200, 0, 0)) },
            Alignment = CellAlignment.Default with { Horizontal = HorizontalAlignment.Center, Vertical = VerticalAlignment.Top, WrapText = true },
        });
        var money = workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "#,##0.00 \"€\"" });
        var date = workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "dd.mm.yyyy" });
        var time = workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "hh:mm:ss" });
        var percent = workbook.Styles.Intern(CellStyle.Default with { NumberFormat = "0.0%" });
        var rotated = workbook.Styles.Intern(CellStyle.Default with
        {
            Alignment = CellAlignment.Default with { TextRotation = 45, Horizontal = HorizontalAlignment.Right, Indent = 2 },
            Protection = new CellProtection(Locked: false),
            Font = CellFont.Default with { Strikethrough = true, Script = FontScript.Superscript },
        });
        foreach (var column in new[] { 0, 1, 2, 3 })
        {
            data.SetStyleId(0, column, bold);
        }

        data.SetStyleId(1, 2, money);
        data.SetValue(9, 0, 45458);
        data.SetStyleId(9, 0, date);
        data.SetValue(9, 1, 0.75);
        data.SetStyleId(9, 1, time);
        data.SetValue(9, 2, 0.125);
        data.SetStyleId(9, 2, percent);
        data.SetValue(9, 3, "schräg");
        data.SetStyleId(9, 3, rotated);
        data.Columns.Set(0, new AxisInfo(Size: 20, CustomSize: true));
        data.Columns.Set(5, new AxisInfo(Hidden: true));
        data.Columns.Set(6, new AxisInfo(StyleId: money));
        data.Rows.Set(0, new AxisInfo(Size: 30, CustomSize: true));
        data.Rows.Set(7, new AxisInfo(Hidden: true));
        data.Merges.Add(CellRange.Parse("A12:C13"));
        data.View = data.View with { FrozenRows = 1, FrozenColumns = 1, Zoom = 120, ActiveCell = new CellAddress(1, 1), Selection = [CellRange.Parse("B2")] };
        data.TabColor = CalcColor.FromRgb(255, 0, 0);
        data.Validations.Add(new DataValidation { Ranges = [CellRange.Parse("B2:B4")], Type = ValidationType.WholeNumber, Operator = ComparisonOperator.GreaterThan, Formula1 = "0", Error = "Nur positive Mengen", ErrorTitle = "Menge" });
        data.Validations.Add(new DataValidation { Ranges = [CellRange.Parse("A2:A4")], Type = ValidationType.List, Formula1 = "\"Kabel,Stecker,Lampe\"" });
        data.Validations.Add(new DataValidation { Ranges = [CellRange.Parse("C2:C100")], Type = ValidationType.Decimal, Operator = ComparisonOperator.Between, Formula1 = "0", Formula2 = "1000", ErrorStyle = ValidationErrorStyle.Warning });
        workbook.Styles.DifferentialFormats.Add(new DifferentialFormat { FillColor = CalcColor.FromRgb(255, 199, 206), FontColor = CalcColor.FromRgb(156, 0, 6), Bold = true });
        data.ConditionalFormats.Add(new ConditionalFormat([CellRange.Parse("D2:D4")], [
            new ConditionalFormatRule { Type = ConditionalFormatType.CellIs, Operator = ComparisonOperator.GreaterThan, Formulas = ["15"], FormatId = 0, Priority = 1 },
            new ConditionalFormatRule { Type = ConditionalFormatType.ColorScale, Priority = 2, Thresholds = [new(ConditionalValueType.Min), new(ConditionalValueType.Max)], Colors = [CalcColor.FromRgb(255, 255, 255), CalcColor.FromRgb(99, 190, 123)] },
            new ConditionalFormatRule { Type = ConditionalFormatType.Expression, Priority = 3, Formulas = ["$B2>5"], FormatId = 0 },
        ]));
        data.AutoFilter = new AutoFilter(CellRange.Parse("A1:D4"), [new FilterColumn { ColumnOffset = 0, Values = ["Kabel", "Lampe"] }]);
        data.Hyperlinks.Add(new Hyperlink(CellRange.Parse("A15"), "https://example.com/artikel"));
        data.Hyperlinks.Add(new Hyperlink(CellRange.Parse("A16"), null, "Auswertung!A1"));
        data.Comments[new CellAddress(1, 0)] = new CellComment("Anna", "Bitte prüfen\nzweite Zeile");
        data.Comments[new CellAddress(20, 4)] = new CellComment("Ben", "Notiz an leerer Zelle", Visible: true);
        data.PageSetup = PageSetup.Default with
        {
            Landscape = true, PaperSize = 8, Scale = 85, PrintGridLines = true, CenterHorizontally = true, OverThenDown = true,
            Header = "&LFirma&CSeite &P von &N&R&D", Footer = "&C&A – &F", LeftMargin = 0.5, TopMargin = 1, HeaderMargin = 0.4, FirstPageNumber = 3,
        };
        report.Visibility = SheetVisibility.Hidden;
        report.PageSetup = PageSetup.Default with { FitToPage = true, FitToWidth = 1, FitToHeight = 0 };
        workbook.ActiveSheetIndex = 0;
        workbook.Calculation.Recalculate();
        return workbook;
    }

    [Fact]
    public void A_workbook_round_trips_with_everything_ods_holds()
    {
        var original = Sample();
        var read = RoundTrip(original);
        var data = read["Daten"];
        var report = read["Auswertung"];

        Assert.Equal(["Daten", "Auswertung"], read.Sheets.Select(sheet => sheet.Name));
        Assert.Equal(" führende  Leerzeichen ", data["A6"].Text);
        Assert.Equal("Zeile1\nZeile2\tmit Tab", data["A7"].Text);
        Assert.True(data["B6"].Boolean);
        Assert.Equal(CellError.NotAvailable, data["C6"].Error);
        Assert.Equal(13.5, data["D2"].Number);
        Assert.Equal("B4*C4", FormulaFormatter.Format(data.GetFormula(3, 3)!, read, new CellAddress(3, 3)));
        Assert.Equal(48, report["A1"].Number);
        Assert.Equal(12, report["A2"].Number);
        Assert.Equal(3, report["A5"].Number);
        Assert.True(report.GetCell(4, 0).IsSpilled);
        Assert.True(report.GetCell(2, 0).IsDynamicArray);
        Assert.Equal(96, report["B1"].Number);
        Assert.Equal(CellRange.Parse("A6:B7"), report.GetCell(5, 0).ArrayRange);
        Assert.Equal(40, report["B7"].Number);
        Assert.Equal(CellError.DivisionByZero, report["G1"].Error);
        Assert.True(report["E1"].Boolean);
        Assert.Equal(SheetVisibility.Hidden, report.Visibility);
        foreach (var address in new[] { "A1", "A2", "B1", "C1", "D1", "E1", "F1", "G1", "H1", "I1" })
        {
            var cell = CellAddress.Parse(address);
            Assert.Equal(FormulaFormatter.Format(original["Auswertung"].GetFormula(cell.Row, cell.Column)!, original, cell),
                FormulaFormatter.Format(report.GetFormula(cell.Row, cell.Column)!, read, cell));
        }

        // Recalculating gives the same values (the cached ones were right).
        read.Calculation.RecalculateAll();
        Assert.Equal(48, report["A1"].Number);
        Assert.Equal("Kabel 3", report["D1"].Text);
        Assert.Equal(14.75, report["F1"].Number);

        // Styles.
        for (var column = 0; column < 4; column++)
        {
            Assert.Equal(original["Daten"].GetStyle(0, column), data.GetStyle(0, column));
        }

        foreach (var address in new[] { "C2", "A10", "B10", "C10", "D10" })
        {
            var cell = CellAddress.Parse(address);
            Assert.Equal(original["Daten"].GetStyle(cell.Row, cell.Column), data.GetStyle(cell.Row, cell.Column));
        }

        Assert.Equal(45458, data["A10"].Number);
        Assert.Equal(0.75, data["B10"].Number, 9);
        Assert.Equal(0.125, data["C10"].Number);

        // Rows, columns, view.
        Assert.Equal(20, data.Columns[0].Size);
        Assert.True(data.Columns[0].CustomSize);
        Assert.True(data.Columns[5].Hidden);
        Assert.Equal("#,##0.00 \"€\"", read.Styles[data.Columns[6].StyleId!.Value].NumberFormat);
        Assert.Equal(8.43, data.Columns.DefaultSize, 6);
        Assert.Equal(30, data.Rows[0].Size);
        Assert.True(data.Rows[7].Hidden);
        Assert.Equal(15, data.Rows.DefaultSize);
        Assert.Equal([CellRange.Parse("A12:C13")], data.Merges.Ranges);
        Assert.Equal(1, data.View.FrozenRows);
        Assert.Equal(1, data.View.FrozenColumns);
        Assert.Equal(120, data.View.Zoom);
        Assert.Equal(new CellAddress(1, 1), data.View.ActiveCell);
        Assert.Equal(0, read.ActiveSheetIndex);
        Assert.Equal(CalcColor.FromRgb(255, 0, 0), data.TabColor);

        // Validations, conditional formats, filter, links, notes, names.
        Assert.Equal(3, data.Validations.Count);
        Assert.Equal(original["Daten"].Validations[0], data.Validations[0] with { Ranges = original["Daten"].Validations[0].Ranges });
        Assert.Equal(original["Daten"].Validations[0].Ranges, data.Validations[0].Ranges);
        Assert.Equal(original["Daten"].Validations[1], data.Validations[1] with { Ranges = original["Daten"].Validations[1].Ranges });
        Assert.Equal(original["Daten"].Validations[1].Ranges, data.Validations[1].Ranges);
        Assert.Equal(original["Daten"].Validations[2], data.Validations[2] with { Ranges = original["Daten"].Validations[2].Ranges });
        Assert.Equal(original["Daten"].Validations[2].Ranges, data.Validations[2].Ranges);
        Assert.Equal(2, data.ConditionalFormats[0].Rules.Count);
        Assert.Equal(["15"], data.ConditionalFormats[0].Rules[0].Formulas);
        Assert.Equal(["$B2>5"], data.ConditionalFormats[0].Rules[1].Formulas);
        Assert.Equal(ConditionalFormatType.Expression, data.ConditionalFormats[0].Rules[1].Type);
        Assert.Equal(original.Styles.DifferentialFormats[0], read.Styles.DifferentialFormats[0]);
        Assert.Equal(CellRange.Parse("A1:D4"), data.AutoFilter!.Range);
        Assert.Equal(["Kabel", "Lampe"], data.AutoFilter.Columns[0].Values);
        Assert.Equal("https://example.com/artikel", data.Hyperlinks.Single(link => link.Range == CellRange.Parse("A15")).Target);
        Assert.Equal("Auswertung!A1", data.Hyperlinks.Single(link => link.Range == CellRange.Parse("A16")).Location);
        Assert.Equal(new CellComment("Anna", "Bitte prüfen\nzweite Zeile"), data.Comments[new CellAddress(1, 0)]);
        Assert.Equal(new CellComment("Ben", "Notiz an leerer Zelle", true), data.Comments[new CellAddress(20, 4)]);
        Assert.Contains(read.Names, name => name.Name == "Lokal" && name.SheetId == data.Id && name.Formula == "Daten!$B$2:$B$4");
        Assert.Contains(read.Names, name => name.Name == "Gesamt" && name.SheetId is null && name.Formula == "Auswertung!$A$1");
        Assert.Contains(read.Names, name => name.Name == "_xlnm.Print_Area" && name.SheetId == data.Id && name.Formula == "Daten!$A$1:$D$16");
        Assert.Contains(read.Names, name => name.Name == "_xlnm.Print_Titles" && name.SheetId == data.Id && name.Formula == "Daten!$1:$1");

        // Page setup.
        Assert.Equal(original["Daten"].PageSetup, data.PageSetup);
        Assert.Equal(original["Auswertung"].PageSetup, report.PageSetup);
    }

    [Fact]
    public void Writing_twice_gives_the_same_package_content()
    {
        var once = RoundTrip(Sample());
        var first = Write(once);
        var twice = RoundTrip(once);
        var second = Write(twice);
        using var a = new ZipArchive(new MemoryStream(first));
        using var b = new ZipArchive(new MemoryStream(second));
        Assert.Equal(Load(a, "content.xml").ToString(), Load(b, "content.xml").ToString());
        Assert.Equal(Load(a, "styles.xml").ToString(), Load(b, "styles.xml").ToString());
    }

    [Fact]
    public void The_losses_name_what_ods_cannot_hold()
    {
        var workbook = Sample();
        Assert.Contains(OdsWriter.Losses(workbook), loss => loss.StartsWith("Bedingte Formatierungen", StringComparison.Ordinal));
        var plain = Create("Tabelle1");
        Fill(plain.Sheets[0], ("A1", 1), ("A2", "=A1*2"));
        Assert.Empty(OdsWriter.Losses(plain));
    }

    [Fact]
    public void Repeated_empty_rows_and_cells_are_written_once_and_never_materialized()
    {
        var workbook = Create("Tabelle1");
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", 1), ("Z100000", 2));
        sheet.Validations.Add(new DataValidation { Ranges = [CellRange.Parse("C:C")], Type = ValidationType.WholeNumber, Operator = ComparisonOperator.GreaterThan, Formula1 = "0" });
        var bytes = Write(workbook);
        Assert.True(bytes.Length < 20_000, $"{bytes.Length} bytes");
        using (var zip = new ZipArchive(new MemoryStream(bytes)))
        {
            var rows = Load(zip, "content.xml").Descendants(OdfXml.TableNs + "table-row").ToList();
            Assert.True(rows.Count < 10, $"{rows.Count} row elements");
        }

        var read = Read(bytes);
        var copy = read.Sheets[0];
        Assert.Equal(2, copy.CellCount);
        Assert.Equal(2, copy["Z100000"].Number);
        Assert.Equal([CellRange.Parse("C1:C1048576")], copy.Validations[0].Ranges);
    }

    [Theory]
    [InlineData("0")]
    [InlineData("0.00")]
    [InlineData("#,##0")]
    [InlineData("#,##0.00")]
    [InlineData("0%")]
    [InlineData("0.00%")]
    [InlineData("0.00E+00")]
    [InlineData("# ?/?")]
    [InlineData("# ?/8")]
    [InlineData("@")]
    [InlineData("dd.mm.yyyy")]
    [InlineData("m/d/yyyy")]
    [InlineData("dd.mm.yyyy hh:mm")]
    [InlineData("hh:mm:ss")]
    [InlineData("[h]:mm:ss")]
    [InlineData("mmmm yyyy")]
    [InlineData("dddd, d. mmmm yyyy")]
    [InlineData("hh:mm AM/PM")]
    [InlineData("mm:ss.00")]
    [InlineData("#,##0.00 \"€\"")]
    [InlineData("#,##0.00 [$€-407]")]
    [InlineData("[$$-409]#,##0.00")]
    [InlineData("#,##0.00;[Red]-#,##0.00")]
    [InlineData("#,##0;-#,##0;\"–\"")]
    [InlineData("0.00;-0.00;0.00;@")]
    [InlineData("[>=1000]#,##0;0.00")]
    [InlineData("#,##0,")]
    [InlineData("0.0 \"kg\"")]
    public void Number_formats_round_trip_through_data_styles(string code)
    {
        var elements = OdsNumberFormats.Write(code, "N1");
        Assert.NotEmpty(elements);
        var styles = elements.ToDictionary(element => (string)element.Attribute(OdfXml.StyleNs + "name")!);
        Assert.Equal(code, OdsNumberFormats.Read(elements[^1], styles));
    }

    [Theory]
    [InlineData("SUM(A1:B2)", "of:=SUM([.A1:.B2])")]
    [InlineData("SUM(Tabelle2!$A$1:B3)", "of:=SUM([$Tabelle2.$A$1:.B3])")]
    [InlineData("'Mein Blatt'!A1+1", "of:=[$'Mein Blatt'.A1]+1")]
    [InlineData("SUM(A:A)", "of:=SUM([.A:.A])")]
    [InlineData("SUM($1:$3)", "of:=SUM([.$1:.$3])")]
    [InlineData("IF(A1>0,\"ja \"\"x\"\"\",FALSE)", "of:=IF([.A1]>0;\"ja \"\"x\"\"\";FALSE())")]
    [InlineData("{1,2;3,4}", "of:={1;2|3;4}")]
    [InlineData("STDEV.S(A1:A3)", "of:=COM.MICROSOFT.STDEV.S([.A1:.A3])")]
    [InlineData("IFNA(A1,0)", "of:=IFNA([.A1];0)")]
    [InlineData("NORMSDIST(1)", "of:=LEGACY.NORMSDIST(1)")]
    [InlineData("A1:B2 B1:C3", "of:=[.A1:.B2]![.B1:.C3]")]
    [InlineData("-A1^2%", "of:=-[.A1]^2%")]
    [InlineData("Tabelle2:Tabelle3!A1", "of:=[$Tabelle2.A1:$Tabelle3.A1]")]
    [InlineData("#N/A", "of:=#N/A")]
    public void Formulas_translate_both_ways(string canonical, string odf)
    {
        var workbook = Create("Tabelle1", "Tabelle2", "Tabelle3", "Mein Blatt");
        var sheet = workbook.Sheets[0];
        var formula = FormulaParser.Parse(canonical, workbook, sheet, new CellAddress(4, 4));
        Assert.Equal(odf, OdsFormulas.Write(formula, sheet, new CellAddress(4, 4)));
        var back = OdsFormulas.ToCanonical(odf);
        Assert.NotNull(back);
        Assert.Equal(FormulaFormatter.Format(formula, workbook, new CellAddress(4, 4)),
            FormulaFormatter.Format(FormulaParser.Parse(back, workbook, sheet, new CellAddress(4, 4)), workbook, new CellAddress(4, 4)));
    }

    [Theory]
    [InlineData("of:=ORG.OPENOFFICE.STYLE(\"x\")")]
    [InlineData("of:=['file:///C:/a.ods'#$Tabelle1.A1]")]
    [InlineData("of:=SUM([.A1]~[.B1])")]
    [InlineData("of:=MULTIPLE.OPERATIONS([.A1];[.B1];[.C1])")]
    public void Formulas_without_a_counterpart_are_not_translated(string odf) => Assert.Null(OdsFormulas.ToCanonical(odf));

    [Fact]
    public void A_structured_reference_is_written_as_its_range()
    {
        var workbook = Create("Tabelle1");
        var sheet = workbook.Sheets[0];
        Fill(sheet, ("A1", "Name"), ("B1", "Wert"), ("A2", "a"), ("B2", 1), ("A3", "b"), ("B3", 2));
        sheet.Tables.Add(new TableDefinition("Daten", CellRange.Parse("A1:B3"), ["Name", "Wert"]));
        var formula = FormulaParser.Parse("SUM(Daten[Wert])", workbook, sheet, new CellAddress(5, 0));
        Assert.Equal("of:=SUM([.$B$2:.$B$3])", OdsFormulas.Write(formula, sheet, new CellAddress(5, 0)));
    }

    [Fact]
    public void Broken_files_give_a_German_message()
    {
        var notZip = Assert.Throws<InvalidDataException>(() => Read(Encoding.UTF8.GetBytes("keine Tabelle")));
        Assert.Equal(OdsReader.NotAnOdsMessage, notZip.Message);

        var broken = OdsFixtures.Package("<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"><office:body>");
        Assert.Equal(OdsReader.NotAnOdsMessage, Assert.Throws<InvalidDataException>(() => Read(broken)).Message);

        var noSpreadsheet = OdsFixtures.Package("<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"><office:body><office:text/></office:body></office:document-content>");
        Assert.Equal(OdsReader.NotAnOdsMessage, Assert.Throws<InvalidDataException>(() => Read(noSpreadsheet)).Message);
    }

    [Fact]
    public void Sniffing_tells_ods_from_xlsx()
    {
        Assert.True(OdsReader.IsOds(new MemoryStream(Write(Create("A")))));
        Assert.False(OdsReader.IsOds(new MemoryStream(XlsxTests.Write(Create("A")))));
        Assert.Equal(WorkbookFileFormat.Ods, WorkbookFormats.FromExtension(".ODS"));
    }
}
