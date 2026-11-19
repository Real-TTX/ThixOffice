using ThixOffice.Text;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Editing;

/// <summary>
/// Property tests: random sequences of every editing command on random selections. After each command the model invariants
/// hold; undoing everything gives back the original blocks (the same instances), redoing everything the final ones; the
/// incrementally updated layout equals a layout computed from scratch.
/// </summary>
public class RandomEditTests
{
    private static readonly string[] Snippets = ["a", "Wort ", "über", " ", "\U0001F600", "e\u0301", "Größe", "\t", "x y z", "1.000,50 €", "„Zitat“"];

    public static TheoryData<int> Seeds()
    {
        var data = new TheoryData<int>();
        for (var seed = 1; seed <= 100; seed++)
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
        var table = new OpaqueBlock("<w:tbl xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" />", PreservedKind.Table, ["A\tB", "C\tD"]);
        var cells = TableEdits.CreateTable(2, 3, 400, Paragraph.Empty(), null)
            .Cast<Paragraph>()
            .Select((paragraph, index) => index % 2 == 0 ? paragraph.WithInlines([new TextRun($"Zelle {index}", CharacterProperties.Empty)]) : paragraph)
            .ToList();
        var document = Create([
            new Paragraph("Erster Absatz mit etwas Text."),
            .. cells,
            new Paragraph([new TextRun("fett ", new CharacterProperties { Bold = true }), new TextRun("und normal", CharacterProperties.Empty)]),
            table,
            new Paragraph("Ein dritter Absatz, der etwas länger ist, damit er umbricht und mehrere Zeilen hat."),
            new Paragraph(""),
            new Paragraph("Ende"),
        ]);
        var session = new EditingSession(document, () => new DateTime(2026, 1, 1));
        using var layout = new DocumentLayout(document, new FixedWidthTextMeasurer(), resolver: session.Resolver);
        session.Navigator = layout;

        var originalBlocks = document.Blocks.ToList();
        var originalStyles = document.Styles;
        var originalNumbering = document.Numbering;
        var originalSection = document.FinalSection;
        DocumentFragment? clipboard = null;

        for (var step = 0; step < 80; step++)
        {
            RandomSelection(session, random);
            var before = session.Selection;
            var command = random.Next(45);
            clipboard = RandomCommand(session, random, clipboard, command);
            try
            {
                AssertInvariants(session);
                layout.Update();
                AssertSameLayout(layout, session.Document);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                throw new InvalidOperationException($"Step {step}: command {command} on {before} broke the document: {exception.Message}", exception);
            }

            layout.Update();
        }

        AssertSameLayout(layout, session.Document);

        var finalBlocks = session.Document.Blocks.ToList();
        var finalStyles = session.Document.Styles;
        var finalNumbering = session.Document.Numbering;

        // Redo exactly as many steps as were undone (steps undone by the sequence itself stay undone).
        var undone = 0;
        while (session.CanUndo)
        {
            session.Undo();
            undone++;
            AssertInvariants(session);
        }

        Assert.Equal(originalBlocks, session.Document.Blocks);
        Assert.Same(originalStyles, session.Document.Styles);
        Assert.Same(originalNumbering, session.Document.Numbering);
        Assert.Equal(originalSection, session.Document.FinalSection);
        AssertSameLayout(layout, session.Document);

        for (var step = 0; step < undone; step++)
        {
            session.Redo();
            AssertInvariants(session);
        }

        Assert.Equal(finalBlocks, session.Document.Blocks);
        Assert.Same(finalStyles, session.Document.Styles);
        Assert.Same(finalNumbering, session.Document.Numbering);
        AssertSameLayout(layout, session.Document);
    }

    private static void RandomSelection(EditingSession session, Random random)
    {
        if (random.Next(3) == 0)
        {
            return;
        }

        var anchor = RandomPosition(session.Document, random);
        var active = random.Next(2) == 0 ? anchor : RandomPosition(session.Document, random);
        session.Select(anchor, active);
    }

    private static DocumentPosition RandomPosition(Document document, Random random)
    {
        var block = random.Next(document.Blocks.Count);
        return new DocumentPosition(block, random.Next(document.Blocks[block].Length + 1));
    }

    private static DocumentFragment? RandomCommand(EditingSession session, Random random, DocumentFragment? clipboard, int command)
    {
        switch (command)
        {
            case 0 or 1 or 2 or 3:
                session.InsertText(Snippets[random.Next(Snippets.Length)]);
                break;
            case 4:
                session.InsertParagraphBreak();
                break;
            case 5:
                session.InsertLineBreak();
                break;
            case 6:
                session.InsertPageBreak();
                break;
            case 7 or 8:
                session.DeleteBackward(random.Next(3) == 0 ? DeleteUnit.Word : DeleteUnit.Character);
                break;
            case 9:
                session.DeleteForward(random.Next(3) == 0 ? DeleteUnit.Word : DeleteUnit.Character);
                break;
            case 10:
                session.DeleteSelection();
                break;
            case 11:
                session.ToggleBold();
                break;
            case 12:
                session.ToggleItalic();
                break;
            case 13:
                session.SetFontSize(8 + random.Next(20));
                break;
            case 14:
                session.SetAlignment((ParagraphAlignment)random.Next(4));
                break;
            case 15:
                string[] styles = [BuiltInStyles.Normal, BuiltInStyles.Heading1, BuiltInStyles.Heading2, BuiltInStyles.Quote, BuiltInStyles.Title, BuiltInStyles.Strong];
                session.ApplyStyle(styles[random.Next(styles.Length)]);
                break;
            case 16:
                session.ToggleList(random.Next(2) == 0 ? ListKind.Bullet : ListKind.Numbered);
                break;
            case 17:
                if (random.Next(2) == 0)
                {
                    session.IncreaseIndent();
                }
                else
                {
                    session.DecreaseIndent();
                }

                break;
            case 18:
                return session.Cut() ?? clipboard;
            case 19:
                return session.Copy() ?? clipboard;
            case 20:
                if (clipboard is not null)
                {
                    session.Paste(clipboard);
                }

                break;
            case 21:
                session.PasteText("eins\r\nzwei\r\n");
                break;
            case 22:
                session.ReplaceAll(new FindOptions("e"), random.Next(2) == 0 ? "E" : "");
                break;
            case 23:
                session.ClearFormatting();
                break;
            case 24:
                session.SetPageSetup(setup => setup with { MarginLeft = 30 + random.Next(60) });
                break;
            case 25:
                session.RestartNumbering();
                break;
            case 26:
                session.SetHighlight(HighlightColor.Yellow);
                break;
            case 27:
                session.HandleTab(random.Next(2) == 0);
                break;
            case 28:
                session.Move((CaretMovement)random.Next(14), random.Next(2) == 0);
                session.InsertText("m");
                break;
            case 29:
                session.InsertTable(1 + random.Next(3), 1 + random.Next(3));
                break;
            case 30:
                session.InsertTableRows(random.Next(2) == 0);
                break;
            case 31:
                session.InsertTableColumns(random.Next(2) == 0);
                break;
            case 32:
                session.DeleteTableRows();
                break;
            case 33:
                session.DeleteTableColumns();
                break;
            case 34:
                session.MergeTableCells();
                break;
            case 35:
                session.SplitTableCell(1 + random.Next(3));
                break;
            case 36:
                session.SetTableCellShading(random.Next(2) == 0 ? null : "FFC000");
                break;
            case 37:
                session.SetTableBorders((BorderChoice)random.Next(10));
                break;
            case 38:
                session.MoveToCell(random.Next(2) == 0);
                break;
            case 39:
                session.SetRepeatHeaderRows(random.Next(2) == 0);
                break;
            case 40:
                session.DistributeTableColumns();
                break;
            case 41:
                session.DeleteTableCells((CellDeletion)random.Next(3));
                break;
            case 42:
                if (random.Next(4) == 0)
                {
                    session.DeleteTable();
                }
                else
                {
                    session.SetTableCellAlignment((CellVerticalAlignment)random.Next(3), (ParagraphAlignment)random.Next(3));
                }

                break;
            case 43:
                session.SelectTableColumn();
                break;
            default:
                if (session.CanUndo && random.Next(2) == 0)
                {
                    session.Undo();
                }
                else
                {
                    session.Redo();
                }

                break;
        }

        return clipboard;
    }

    /// <summary>The incrementally updated layout must equal one computed from scratch.</summary>
    internal static void AssertSameLayout(DocumentLayout layout, Document document)
    {
        using var fresh = new DocumentLayout(document, new FixedWidthTextMeasurer());
        Assert.Equal(fresh.PageCount, layout.PageCount);
        for (var page = 0; page < fresh.PageCount; page++)
        {
            var expected = fresh.Pages[page].Lines.Select(line => (line.Block, line.Line.Start, line.Line.End, Math.Round(line.Top, 3))).ToList();
            var actual = layout.Pages[page].Lines.Select(line => (line.Block, line.Line.Start, line.Line.End, Math.Round(line.Top, 3))).ToList();
            Assert.Equal(expected, actual);
            var expectedCells = fresh.Pages[page].Cells.Select(Cell).ToList();
            var actualCells = layout.Pages[page].Cells.Select(Cell).ToList();
            Assert.Equal(expectedCells, actualCells);
        }

        static (int, int, int, double, double, double, double, bool) Cell(PlacedCell cell) =>
            (cell.TableStart, cell.Row, cell.Cell, Math.Round(cell.X, 3), Math.Round(cell.Y, 3), Math.Round(cell.Width, 3), Math.Round(cell.Height, 3), cell.IsRepeat);
    }
}
