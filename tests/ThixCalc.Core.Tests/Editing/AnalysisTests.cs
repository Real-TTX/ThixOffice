using ThixCalc.Core.Editing;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixCalc.Core.Tests.Formats;
using ThixOffice.Core.Models;
using static ThixCalc.Core.Tests.TestBooks;

namespace ThixCalc.Core.Tests.Editing;

/// <summary>
/// Teilergebnis and the outline, Zielwertsuche, Formelüberwachung, Namen aus Auswahl, Inhalte auswählen, Blatt- und
/// Arbeitsmappenschutz (enforced in the session, passwords as Excel hashes them, through xlsx).
/// </summary>
public class AnalysisTests
{
    private static EditingSession Session(params (string Address, object? Value)[] cells)
    {
        var workbook = Create("Daten");
        Fill(workbook["Daten"], cells);
        workbook.Calculation.Recalculate();
        return new EditingSession(workbook) { Locale = CalcLocale.German };
    }

    private static EditingSession Sales() => Session(
        ("A1", "Region"), ("B1", "Umsatz"),
        ("A2", "Nord"), ("B2", 10), ("A3", "Nord"), ("B3", 20),
        ("A4", "Süd"), ("B4", 5),
        ("A5", "West"), ("B5", 7), ("A6", "West"), ("B6", 3));

    [Fact]
    public void Subtotals_insert_rows_with_teilergebnis_and_an_outline()
    {
        var session = Sales();
        var sheet = session.ActiveSheet;
        var range = session.AddSubtotals(CellRange.Parse("A1:B6"), 0, SubtotalFunction.Sum, [1]);
        Assert.Equal(CellRange.Parse("A1:B10"), range);
        Assert.Equal("Nord Ergebnis", sheet["A4"].Text);
        Assert.Equal(30.0, sheet["B4"].Number);
        Assert.Equal("Süd Ergebnis", sheet["A6"].Text);
        Assert.Equal(5.0, sheet["B6"].Number);
        Assert.Equal("West Ergebnis", sheet["A9"].Text);
        Assert.Equal(10.0, sheet["B9"].Number);
        Assert.Equal("Gesamtergebnis", sheet["A10"].Text);
        Assert.Equal(45.0, sheet["B10"].Number); // TEILERGEBNIS skips the subtotals
        Assert.Equal(2, sheet.Rows[1].OutlineLevel);
        Assert.Equal(1, sheet.Rows[3].OutlineLevel);
        Assert.True(sheet.GetStyle(9, 0).Font.Bold);

        // Detail ausblenden of Nord hides its rows; level 1 shows only the totals.
        session.ShowDetail(rows: true, 1, show: false);
        Assert.True(sheet.Rows.IsHidden(1) && sheet.Rows.IsHidden(2));
        session.ShowOutlineLevel(rows: true, 2); // Ebene 2: the subtotals, no details
        Assert.True(sheet.Rows.IsHidden(1));
        Assert.False(sheet.Rows.IsHidden(3));
        session.ShowOutlineLevel(rows: true, 3); // everything
        Assert.False(sheet.Rows.IsHidden(1));

        // Remove all, then undo everything.
        session.RemoveSubtotals(range);
        Assert.Equal("Süd", sheet["A4"].Text);
        Assert.Equal(0, sheet.Rows[1].OutlineLevel);
        while (session.CanUndo)
        {
            session.Undo();
        }

        Assert.Equal("Süd", sheet["A4"].Text);
        Assert.True(sheet["A7"].IsBlank);
    }

    [Fact]
    public void Goal_seek_finds_the_input_and_is_one_undo_step()
    {
        var session = Session(("A1", 1), ("B1", "=A1*A1+3"));
        var (found, input, result) = session.GoalSeek(new CellAddress(0, 1), 39, new CellAddress(0, 0));
        Assert.True(found);
        Assert.Equal(6, input, 2);
        Assert.Equal(39, result, 2);
        session.Undo();
        Assert.Equal(1.0, session.ActiveSheet["A1"].Number);
        Assert.Throws<BusinessException>(() => session.GoalSeek(new CellAddress(0, 0), 3, new CellAddress(0, 1)));
        var (impossible, _, _) = session.GoalSeek(new CellAddress(0, 1), 1, new CellAddress(0, 0));
        Assert.False(impossible);
        Assert.Equal(1.0, session.ActiveSheet["A1"].Number);
    }

    [Fact]
    public void Trace_precedents_dependents_errors_and_evaluation()
    {
        var session = Session(("A1", 2), ("A2", 3), ("B1", "=SUM(A1:A2)*A1"), ("C1", "=B1+1"), ("D1", "=1/0"));
        session.Select(new CellAddress(0, 1));
        Assert.Equal(2, session.TracePrecedents());
        Assert.Contains(session.TraceArrows, arrow => arrow.From == CellRange.Parse("A1:A2") && arrow.To == new CellAddress(0, 1));
        Assert.Equal(1, session.TraceDependents());
        Assert.Contains(session.TraceArrows, arrow => arrow.To == new CellAddress(0, 2));
        session.RemoveArrows();
        Assert.Empty(session.TraceArrows);

        Assert.Equal(new CellAddress(0, 3), session.NextError());
        Assert.Contains("Division durch 0", EditingSession.ExplainError(session.ActiveSheet["D1"].Error), StringComparison.Ordinal);

        var steps = session.EvaluateFormulaSteps(new CellAddress(0, 1));
        Assert.Equal("=SUMME(A1:A2)*A1", steps[0]);
        Assert.Contains("=5*A1", steps);
        Assert.Contains("=5*2", steps);
        Assert.Equal("10", steps[^1]);
    }

    [Fact]
    public void Names_come_from_the_labels_of_the_selection()
    {
        var session = Session(("A1", "Monat"), ("B1", "Umsatz Netto"), ("A2", "Jan"), ("B2", 5), ("A3", "Feb"), ("B3", 7));
        session.Select(CellRange.Parse("A1:B3"));
        var names = session.CreateNamesFromSelection(top: true, left: false);
        Assert.Equal(["Monat", "Umsatz_Netto"], names);
        session.Select(new CellAddress(5, 0));
        session.EnterInput("=SUMME(Umsatz_Netto)");
        Assert.Equal(12.0, session.ActiveSheet["A6"].Number);
    }

    [Fact]
    public void Special_cells_are_selected()
    {
        var session = Session(("A1", 1), ("A3", "x"), ("B1", "=A1*2"), ("B3", "=1/0"));
        session.Select(new CellAddress(0, 0));
        Assert.Equal([CellRange.Parse("A2:B2")], session.SelectSpecial(SpecialCells.Blanks));
        session.Select(new CellAddress(0, 0));
        Assert.Equal([CellRange.Parse("B1"), CellRange.Parse("B3")], session.SelectSpecial(SpecialCells.Formulas).OrderBy(range => range.FirstRow).ToList());
        session.Select(new CellAddress(0, 0));
        Assert.Equal([CellRange.Parse("B3")], session.SelectSpecial(SpecialCells.Errors));
        Assert.Equal(CellRange.Parse("B3"), session.SelectSpecial(SpecialCells.LastCell)[0]);
        session.HideRows(1, 1);
        session.Select(CellRange.Parse("A1:B3"));
        Assert.Equal([CellRange.Parse("A1:B1"), CellRange.Parse("A3:B3")], session.SelectSpecial(SpecialCells.VisibleCells));
    }

    [Fact]
    public void A_protected_sheet_keeps_locked_cells_and_its_password()
    {
        var session = Session(("A1", 1), ("B1", 2));
        session.Select(new CellAddress(0, 1));
        session.ApplyStyle(style => style with { Protection = style.Protection with { Locked = false } }, "Zellschutz");
        session.ProtectSheet(new SheetProtection { FormatColumns = true }, "geheim");
        Assert.True(session.IsSheetProtected);

        session.Select(new CellAddress(0, 0));
        Assert.Throws<BusinessException>(() => session.EnterInput("5"));
        Assert.Throws<BusinessException>(() => session.ClearContents());
        Assert.Throws<BusinessException>(() => session.SetBold(true));
        Assert.Throws<BusinessException>(() => session.InsertRows(0, 1));
        Assert.Throws<BusinessException>(() => session.HideRows(0, 0));
        session.SetColumnWidth(0, 0, 20); // allowed: Spalten formatieren
        session.Select(new CellAddress(0, 1));
        session.EnterInput("7"); // an unlocked cell
        Assert.Equal(7.0, session.ActiveSheet["B1"].Number);

        Assert.Throws<BusinessException>(() => session.UnprotectSheet("falsch"));
        Assert.True(session.IsSheetProtected);

        // Through xlsx: the hash is Excel's SHA-512; the password still opens it.
        var read = XlsxTests.RoundTrip(session.Workbook);
        var again = new EditingSession(read) { Locale = CalcLocale.German };
        Assert.True(again.IsSheetProtected);
        Assert.True(again.ActiveSheet.Protection!.HasPassword && again.ActiveSheet.Protection.FormatColumns);
        again.Select(new CellAddress(0, 0));
        Assert.Throws<BusinessException>(() => again.EnterInput("5"));
        Assert.True(again.UnprotectSheet("geheim"));
        again.EnterInput("5");
        Assert.Equal(5.0, again.ActiveSheet["A1"].Number);
    }

    [Fact]
    public void Excel_password_hashes_are_verified()
    {
        Assert.Equal("83AF", Protection.LegacyHash("password"));
        var legacy = $"<sheetProtection xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" password=\"83AF\" sheet=\"1\"/>";
        Assert.True(Protection.Verify(legacy, "password"));
        Assert.False(Protection.Verify(legacy, "Password"));
        Assert.NotNull(Protection.Parse(legacy));
    }

    [Fact]
    public void Icon_sets_are_evaluated_and_survive_xlsx()
    {
        var session = Session(("A1", 1), ("A2", 5), ("A3", 10));
        session.Select(CellRange.Parse("A1:A3"));
        session.AddIconSet("3Arrows");
        var rule = session.ActiveSheet.ConditionalFormats.Single().Rules.Single();
        Assert.Equal(ConditionalFormatType.IconSet, rule.Type);
        var evaluator = new Core.Layout.ConditionalFormatEvaluator(session.ActiveSheet);
        Assert.Equal(("3Arrows", 0), evaluator.Evaluate(0, 0)!.Icon);
        Assert.Equal(("3Arrows", 2), evaluator.Evaluate(2, 0)!.Icon);
        var read = XlsxTests.RoundTrip(session.Workbook);
        Assert.Equal("3Arrows", read.Sheets[0].ConditionalFormats.Single().Rules.Single().IconSet);
    }

    [Fact]
    public void A_protected_workbook_keeps_its_sheets()
    {
        var session = Session(("A1", 1));
        session.ProtectWorkbook("abc");
        Assert.Throws<BusinessException>(() => session.AddSheet());
        Assert.Throws<BusinessException>(() => session.RenameSheet(0, "Neu"));
        Assert.Throws<BusinessException>(() => session.DeleteSheet(0));
        var read = XlsxTests.RoundTrip(session.Workbook);
        Assert.True(read.IsStructureProtected);
        var again = new EditingSession(read) { Locale = CalcLocale.German };
        Assert.Throws<BusinessException>(() => again.UnprotectWorkbook("xyz"));
        again.UnprotectWorkbook("abc");
        again.AddSheet();
        Assert.Equal(2, read.Sheets.Count);
    }
}
