using ThixCalc.Core.Editing;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Core.Models;

namespace ThixCalc.Core.Tests.Editing;

/// <summary>
/// Property test: random sequences of editing commands. Undoing everything gives back the original workbook (values,
/// formulas as text, styles, merges, sizes, sheet names); redoing everything gives the final one; and after every command the
/// incrementally recalculated values equal a full recalculation.
/// </summary>
public class RandomEditTests(Xunit.Abstractions.ITestOutputHelper output)
{
    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 60; seed++)
        {
            data.Add(seed);
        }

        return data;
    }

    [Theory]
    [MemberData(nameof(Seeds))]
    public void Undo_all_restores_the_original_and_redo_all_the_result(int seed)
    {
        var random = new Random(seed);
        var workbook = TestBooks.Create("Tabelle1", "Tabelle2");
        workbook.Calculation.Locale = CalcLocale.German;
        TestBooks.Fill(workbook.Sheets[0], ("A1", 1), ("A2", 2), ("A3", 3), ("B1", "=SUM(A1:A3)"), ("B2", "=A2*2"), ("C3", "Text"));
        TestBooks.Fill(workbook.Sheets[1], ("A1", "=Tabelle1!B1+1"), ("B2", 5));
        workbook.Sheets[0].Merges.Add(CellRange.Parse("E5:F6"));
        workbook.Calculation.Recalculate();
        var session = new EditingSession(workbook);
        var original = Snapshot(workbook);
        var steps = 0;
        for (var index = 0; index < 40; index++)
        {
            try
            {
                var before = Snapshot(workbook);
                var label = session.UndoLabel;
                if (RandomCommand(session, random))
                {
                    steps++;
                }

                if (session.UndoLabel is not null && Snapshot(workbook) is var after && after != before)
                {
                    // Every single command undoes and redoes exactly.
                    session.Undo();
                    Assert.True(before == Snapshot(workbook), $"Undo of {session.RedoLabel}: {Difference(before, Snapshot(workbook))}");
                    session.Redo();
                    Assert.True(after == Snapshot(workbook), $"Redo of {session.UndoLabel}: {Difference(after, Snapshot(workbook))}");
                }

                _ = label;

                output.WriteLine(session.UndoLabel + " @ " + session.ActiveSheet.Name + "!" + session.ActiveCell + " " + session.Selection);
            }
            catch (BusinessException)
            {
                // Refused commands change nothing.
            }

            AssertIncrementalEqualsFull(workbook);
        }

        var final = Snapshot(workbook);
        while (session.CanUndo)
        {
            session.Undo();
        }

        Assert.Equal(original, Snapshot(workbook));
        while (session.CanRedo)
        {
            session.Redo();
        }

        Assert.Equal(final, Snapshot(workbook));
        _ = steps;
    }

    private static bool RandomCommand(EditingSession session, Random random)
    {
        string Cell() => A1.ColumnName(random.Next(5)) + (random.Next(8) + 1);
        var cell = CellAddress.Parse(Cell());
        session.ActivateSheet(random.Next(session.Workbook.Sheets.Count));
        session.Select(cell);
        switch (random.Next(20))
        {
            case 0:
                session.EnterInput(random.Next(100).ToString(System.Globalization.CultureInfo.InvariantCulture));
                return true;
            case 1:
                session.EnterInput($"=SUMME({Cell()}:{Cell()})");
                return true;
            case 2:
                session.EnterInput($"={Cell()}*2");
                return true;
            case 3:
                session.EnterInput("Wort " + random.Next(5));
                return true;
            case 4:
                session.InsertRows(random.Next(8), random.Next(1, 3));
                return true;
            case 5:
                session.DeleteRows(random.Next(8), random.Next(1, 3));
                return true;
            case 6:
                session.InsertColumns(random.Next(5), 1);
                return true;
            case 7:
                session.DeleteColumns(random.Next(5), 1);
                return true;
            case 8:
                session.Select(new CellRange(cell, CellAddress.Parse(Cell())));
                session.SetBold(random.Next(2) == 0);
                return true;
            case 9:
                session.Select(new CellRange(cell, CellAddress.Parse(Cell())));
                session.ClearContents();
                return true;
            case 10:
            {
                session.Select(new CellRange(cell, cell.Offset(random.Next(2), random.Next(2))));
                var content = random.Next(2) == 0 ? session.Copy() : session.Cut();
                session.Select(CellAddress.Parse(Cell()));
                session.Paste(content);
                return true;
            }

            case 11:
                session.Sort(CellRange.Parse("A1:C8"), [new SortKey(random.Next(3), random.Next(2) == 0)], new SortOptions(HasHeader: false));
                return true;
            case 12:
                session.Select(new CellRange(cell, cell.Offset(1, 1)));
                session.Merge();
                return true;
            case 13:
                session.InsertCells(new CellRange(cell, cell.Offset(random.Next(2), 0)), random.Next(2) == 0 ? CellShift.Down : CellShift.Right);
                return true;
            case 14:
                session.AutoFill(CellRange.Cell(cell), new CellRange(cell, cell.Offset(random.Next(1, 4), 0)));
                return true;
            case 15:
                session.Select(CellRange.Parse("A1:C3"));
                session.InsertChart(ThixOffice.Charts.ChartTemplates.All[random.Next(ThixOffice.Charts.ChartTemplates.All.Count)], new ChartAnchor(cell, 3, 4, cell.Offset(10, 5), 0, 0));
                return true;
            case 16 when session.ActiveSheet.Charts.Count > 0:
                session.SetChartAnchor(session.ActiveSheet.Charts[^1].Id, new ChartAnchor(cell, 0, 0, cell.Offset(random.Next(2, 9), random.Next(2, 6)), 1, 1));
                return true;
            case 17 when session.ActiveSheet.Charts.Count > 0:
                session.ChangeChartType(session.ActiveSheet.Charts[0].Id, ThixOffice.Charts.ChartTemplates.All[random.Next(ThixOffice.Charts.ChartTemplates.All.Count)]);
                return true;
            case 18 when session.ActiveSheet.Charts.Count > 0:
                session.DeleteChart(session.ActiveSheet.Charts[random.Next(session.ActiveSheet.Charts.Count)].Id);
                return true;
            default:
                session.RenameSheet(random.Next(session.Workbook.Sheets.Count), "Blatt" + random.Next(100));
                return true;
        }
    }

    private static string Difference(string expected, string actual)
    {
        var index = 0;
        while (index < Math.Min(expected.Length, actual.Length) && expected[index] == actual[index])
        {
            index++;
        }

        var start = Math.Max(0, index - 60);
        return "expected …" + expected[start..Math.Min(expected.Length, index + 60)] + "… got …" + actual[start..Math.Min(actual.Length, index + 60)] + "…";
    }

    private static void AssertIncrementalEqualsFull(Workbook workbook)
    {
        var incremental = Values(workbook);
        workbook.Calculation.Rebuild();
        workbook.Calculation.Recalculate();
        Assert.Equal(Values(workbook), incremental);
    }

    private static Dictionary<string, CellValue> Values(Workbook workbook) =>
        workbook.Sheets.SelectMany(sheet => sheet.GetCells().Select(entry => (Key: sheet.Id + "!" + entry.Address, entry.Cell.Value)))
            .ToDictionary(pair => pair.Key, pair => pair.Value);

    private static string Snapshot(Workbook workbook)
    {
        var builder = new System.Text.StringBuilder();
        foreach (var sheet in workbook.Sheets)
        {
            builder.Append('[').Append(sheet.Name).Append(']');
            foreach (var entry in sheet.GetCells())
            {
                var text = entry.Cell.Formula is { } formula ? "=" + FormulaFormatter.Format(formula, workbook, entry.Address) : entry.Cell.Value.ToString();
                builder.Append(entry.Address).Append(':').Append(text).Append('|').Append(entry.Cell.StyleId).Append(entry.Cell.IsSpilled ? "s" : string.Empty).Append(';');
            }

            builder.Append(" merges ").Append(CellRange.FormatList(sheet.Merges.Ranges));
            builder.Append(" rows ").Append(string.Join(",", sheet.Rows.Entries));
            builder.Append(" columns ").Append(string.Join(",", sheet.Columns.Entries));
            builder.Append(" charts ").Append(string.Join(",", sheet.Charts.Select(chart => chart.Id + "@" + chart.Anchor + ":" + ThixOffice.Charts.ChartTemplates.Of(chart.Chart)?.Id + ":" + string.Join("|", ThixOffice.Charts.ChartEdits.References(chart.Chart)))));
        }

        builder.Append(" names ").Append(string.Join(",", workbook.Names));
        return builder.ToString();
    }
}
