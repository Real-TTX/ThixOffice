using DocumentFormat.OpenXml.Packaging;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Core.Tests.Formats;
using static ThixWrite.Core.Tests.TestDocuments;

namespace ThixWrite.Core.Tests.Tables;

/// <summary>Tables: the model, editing (typing, Tab, delete rules, structure), layout and pagination, docx.</summary>
public class TableTests
{
    /// <summary>"Vorher", a 3 × 2 table (caret in its first cell), "Nachher".</summary>
    private static (EditingSession Session, DocumentLayout Layout) WithTable(int rows = 3, int columns = 2)
    {
        var (session, layout) = Editor(Create("Vorher", "Nachher"));
        session.SetCaret(At(1, 0));
        session.InsertTable(rows, columns);
        return (session, layout);
    }

    private static TableSpan Span(EditingSession session) => TableSpan.At(session.Document, 1)!;

    [Fact]
    public void Inserting_a_table_puts_empty_cells_with_Tabellenraster_before_the_paragraph_and_the_caret_into_the_first_cell()
    {
        var (session, _) = WithTable();

        var span = Span(session);
        Assert.Equal((1, 6), (span.Start, span.End));
        Assert.Equal(3, span.Table.RowCount);
        Assert.Equal(2, span.Table.ColumnCount);
        Assert.Equal(BuiltInStyles.TableGrid, span.Table.Properties.StyleId);
        Assert.NotNull(session.Document.Styles.Find(BuiltInStyles.TableGrid));
        Assert.Empty(span.Problems(session.Document));
        Assert.Equal("Nachher", session.Document.ParagraphAt(7)!.Text);
        Assert.Equal(Selection.Caret(At(1, 0)), session.Selection);

        session.Undo();
        Assert.Equal("Vorher|Nachher", Texts(session.Document));
    }

    [Fact]
    public void Typing_Tab_and_Shift_Tab_move_through_the_cells_and_Tab_in_the_last_cell_adds_a_row()
    {
        var (session, _) = WithTable(2, 2);

        session.InsertText("A1");
        session.HandleTab(shift: false);
        session.InsertText("B1");
        session.HandleTab(shift: false);
        session.HandleTab(shift: true);
        Assert.Equal(new DocumentRange(At(2, 0), At(2, 2)), session.Selection.Range); // the previous cell's text is selected
        session.SetCaret(At(4, 0));
        session.InsertText("B2");
        session.HandleTab(shift: false);

        var span = Span(session);
        Assert.Equal(3, span.Table.RowCount);
        Assert.Equal(span.BlocksOf(2, 0).First, session.Selection.Active.Block);
        Assert.Equal(["A1", "B1", "", "B2", "", ""], Enumerable.Range(span.Start, span.BlockCount).Select(block => session.Document.ParagraphAt(block)!.Text));

        session.Undo();
        Assert.Equal(2, Span(session).Table.RowCount);
    }

    [Fact]
    public void Enter_stays_in_the_cell_and_Backspace_and_Delete_never_leave_it()
    {
        var (session, _) = WithTable(2, 2);
        session.InsertText("Eins");
        session.InsertParagraphBreak();
        session.InsertText("Zwei");
        var span = Span(session);
        Assert.Equal((1, 2), span.BlocksOf(0, 0));

        session.SetCaret(At(3, 0)); // B1
        session.DeleteBackward();
        Assert.Equal("Zwei", session.Document.ParagraphAt(2)!.Text);
        Assert.Equal(At(3, 0), session.Selection.Active);

        session.SetCaret(At(2, 4));
        session.DeleteForward();
        Assert.Equal(5, Span(session).BlockCount);

        session.SetCaret(At(2, 0));
        session.DeleteBackward(); // inside the cell: the two paragraphs join
        Assert.Equal("EinsZwei", session.Document.ParagraphAt(1)!.Text);
        Assert.Empty(Span(session).Problems(session.Document));
    }

    [Fact]
    public void Backspace_after_a_table_goes_into_its_last_cell_and_Delete_before_it_removes_an_empty_paragraph()
    {
        var (session, _) = WithTable(1, 2);
        session.SetCaret(At(3, 0)); // "Nachher"
        session.DeleteBackward();
        Assert.Equal(At(2, 0), session.Selection.Active);

        session.SetCaret(At(0, 6));
        session.InsertParagraphBreak(); // an empty paragraph before the table
        session.DeleteForward();
        Assert.Equal("Vorher", session.Document.ParagraphAt(0)!.Text);
        Assert.NotNull(session.Document.ParagraphAt(1)!.Cell);
    }

    [Fact]
    public void Whole_cells_are_cleared_by_Delete_and_a_table_inside_a_selection_goes_as_a_whole()
    {
        var (session, layout) = WithTable(2, 2);
        session.InsertText("A1");
        session.SetCaret(At(2, 0));
        session.InsertText("B1");
        session.SetCaret(At(3, 0));
        session.InsertText("A2");

        session.Select(At(1, 1), At(3, 1)); // A1 to A2: the first column
        var cells = Assert.Single(TableSelection.WholeCells(session.Document, session.Selection));
        Assert.Equal((0, 1, 0, 0), (cells.FirstRow, cells.LastRow, cells.FirstColumn, cells.LastColumn));
        Assert.Equal(2, layout.GetSelectionRects(session.Selection.Range).Count); // two cells highlighted, not lines

        session.ToggleBold();
        Assert.True(session.Document.ParagraphAt(1)!.Inlines[0].Properties.Bold);
        Assert.Null(session.Document.ParagraphAt(2)!.Inlines[0].Properties.Bold); // B1 is not in the column

        session.DeleteSelection();
        Assert.Equal(["", "B1", "", ""], Enumerable.Range(1, 4).Select(block => session.Document.ParagraphAt(block)!.Text));

        session.SelectAll();
        session.DeleteSelection();
        Assert.Empty(TableSpan.All(session.Document));
        Assert.Equal("", Texts(session.Document));
    }

    [Fact]
    public void Copying_cells_gives_a_table_of_them_and_tab_separated_text()
    {
        var (session, _) = WithTable(2, 3);
        string[] texts = ["a", "b", "c", "d", "e", "f"];
        for (var index = 0; index < 6; index++)
        {
            session.SetCaret(At(1 + index, 0));
            session.InsertText(texts[index]);
        }

        session.Select(At(2, 0), At(6, 1)); // b … f: columns 2–3 of both rows
        var fragment = session.Copy()!;
        Assert.Equal("\r\nb\tc\r\ne\tf\r\n", fragment.PlainText);
        var copied = TableSpan.At(new Document(fragment.Blocks), 1)!;
        Assert.Equal((2, 2), (copied.Table.RowCount, copied.Table.ColumnCount));

        session.SetCaret(At(8, 7)); // the end of "Nachher"
        session.Paste(fragment);
        Assert.Equal(2, TableSpan.All(session.Document).Count());
        AssertInvariants(session);
    }

    [Fact]
    public void Rows_and_columns_are_inserted_and_deleted_and_the_table_keeps_its_width()
    {
        var (session, _) = WithTable(2, 2);
        var width = Span(session).Table.Grid.Sum();

        session.InsertTableRows(below: true);
        Assert.Equal(3, Span(session).Table.RowCount);
        session.InsertTableColumns(right: false);
        var table = Span(session).Table;
        Assert.Equal(3, table.ColumnCount);
        Assert.Equal(width, table.Grid.Sum(), 1);
        Assert.All(table.Rows, row => Assert.Equal(3, row.CellCount));

        session.DeleteTableColumns();
        session.DeleteTableRows();
        Assert.Equal((2, 2), (Span(session).Table.RowCount, Span(session).Table.ColumnCount));

        session.DeleteTable();
        Assert.Empty(TableSpan.All(session.Document));
        Assert.Equal("Vorher|Nachher", Texts(session.Document));
        AssertInvariants(session);
    }

    [Fact]
    public void Cells_merge_horizontally_and_vertically_and_split_again()
    {
        var (session, layout) = WithTable(3, 3);
        session.InsertText("x");
        session.Select(At(1, 0), At(5, 0)); // A1 … B2
        Assert.True(session.CanMergeTableCells);
        session.MergeTableCells();

        var table = Span(session).Table;
        Assert.Equal(2, table.CellAt(0, 0).GridSpan);
        Assert.Equal(VerticalMerge.Restart, table.CellAt(0, 0).VerticalMerge);
        Assert.Equal(VerticalMerge.Continue, table.CellAt(1, 0).VerticalMerge);
        Assert.Equal(2, table.RowSpanOf(0, 0));
        Assert.Equal("x", session.Document.ParagraphAt(1)!.Text);
        AssertInvariants(session);

        // The merged cell is one rectangle over two rows and two columns; the covered paragraph is no caret place.
        var merged = layout.Pages[0].Cells.Single(cell => cell.Row == 0 && cell.Cell == 0);
        var single = layout.Pages[0].Cells.Single(cell => cell.Row == 0 && cell.Cell == 1);
        Assert.True(merged.Width > single.Width * 1.9);
        Assert.True(merged.Height > single.Height * 1.9);
        var covered = Span(session).BlocksOf(1, 0).First;
        Assert.NotEqual(covered, CaretStops.Snap(session.Document, At(covered, 0)).Block);

        session.SetCaret(At(1, 0));
        session.SplitTableCell(2);
        table = Span(session).Table;
        Assert.Equal(VerticalMerge.None, table.CellAt(0, 0).VerticalMerge);
        Assert.Equal(1, table.CellAt(0, 0).GridSpan);
        Assert.Equal(1, table.CellAt(0, 1).GridSpan);
        AssertInvariants(session);
    }

    [Fact]
    public void Cells_are_laid_out_side_by_side_with_borders_and_shading_on_the_page()
    {
        var (session, layout) = WithTable(2, 2);
        session.InsertText("links");
        session.SetCaret(At(2, 0));
        session.InsertText("rechts");
        session.SetTableCellShading("FFC000");

        var page = layout.Pages[0];
        var left = page.Lines.Single(line => line.Block == 1);
        var right = page.Lines.Single(line => line.Block == 2);
        Assert.Equal(left.Top, right.Top, 3);
        Assert.True(right.Left > left.Left + 100);
        Assert.Equal(4, page.Cells.Count);
        var setup = session.Document.FinalSection;
        Assert.Equal(setup.MarginLeft, left.Left, 1); // the text of the first column lines up with the margin

        var after = page.Lines.Single(line => line.Block == 5);
        Assert.True(after.Top >= page.Cells.Max(cell => cell.Y + cell.Height) - 0.01);

        var items = layout.GetDisplayList(0).Items;
        Assert.Contains(items, item => item is RectangleItem { Fill: { } fill } && fill == RgbColor.FromHex("FFC000"));
        Assert.True(items.OfType<LineItem>().Count() >= 16); // four sides of four cells (Tabellenraster)

        // Down from the left cell of the first row: the left cell of the second row, then the paragraph after the table.
        var down = layout.MoveByLine(new CaretPlace(At(1, 2)), 1, left.Left + 2)!.Value;
        Assert.Equal(3, down.Position.Block);
        Assert.Equal(5, layout.MoveByLine(down, 1, left.Left + 2)!.Value.Position.Block);

        // A click right of the right cell's text lands in it.
        var hit = layout.HitTest(0, right.Left + 150, right.Top + 2);
        Assert.Equal(2, hit.Position.Block);
    }

    [Fact]
    public void A_long_table_repeats_its_heading_row_and_a_tall_row_breaks_across_pages()
    {
        var (session, layout) = WithTable(40, 2);
        session.SetRepeatHeaderRows(true);
        for (var row = 1; row < 40; row++)
        {
            var block = Span(session).BlocksOf(row, 0).First;
            session.SetCaret(At(block, 0));
            session.InsertText($"Zeile {row} " + Words(row == 30 ? 1400 : 3, row));
        }

        Assert.True(layout.PageCount >= 3);
        var second = layout.Pages[1];
        Assert.NotEmpty(second.RepeatedLines);
        Assert.Contains(second.Cells, cell => cell.IsRepeat && cell.Row == 0);
        var heading = second.Cells.Where(cell => cell.IsRepeat).Max(cell => cell.Y + cell.Height);
        Assert.All(second.Lines, line => Assert.True(line.Top >= heading - 0.01));

        // Row 30 (1400 words) is taller than a page: its lines go on over two pages.
        var tall = Span(session).BlocksOf(30, 0).First;
        var pages = layout.Pages.Select((page, index) => (page, index)).Where(item => item.page.Lines.Any(line => line.Block == tall)).Select(item => item.index).ToList();
        Assert.True(pages.Count >= 2);
        Assert.Equal(layout.GetBlockLayout(tall).Lines.Count, layout.Pages.Sum(page => page.Lines.Count(line => line.Block == tall)));
        Editing.RandomEditTests.AssertSameLayout(layout, session.Document);

        foreach (var page in layout.Pages)
        {
            Assert.All(page.Lines, line => Assert.True(line.Top + line.Line.Height <= page.Height - page.Section.Properties.MarginBottom + 0.01 || line.Line.Height > 600));
        }
    }

    [Fact]
    public void A_table_with_merges_borders_shading_and_unknown_properties_survives_docx()
    {
        var (session, _) = WithTable(3, 3);
        session.InsertText("Kopf");
        session.SetRepeatHeaderRows(true);
        session.Select(At(4, 0), At(8, 0)); // A2 … B3
        session.MergeTableCells();
        session.SetCaret(At(2, 0));
        session.SetTableCellShading("D9E2F3");
        session.SetTableBorders(BorderChoice.Outside, new BorderLine("double", 1.5, "1F3864"));
        session.SetTableCellAlignment(CellVerticalAlignment.Center, ParagraphAlignment.Right);
        session.SetTableAlignment(TableAlignment.Center);

        var bytes = DocxTestSupport.Write(session.Document);
        DocxTestSupport.AssertValid(bytes);
        var read = DocxTestSupport.Read(bytes);
        var written = TableSpan.At(session.Document, 1)!.Table;
        var reread = TableSpan.At(read, 1)!.Table;
        Assert.Equal(written.Properties, reread.Properties);
        Assert.Equal(written.Grid, reread.Grid);
        Assert.Equal(written.Rows, reread.Rows);
        DocxTestSupport.AssertSameContent(session.Document, read);
        var xml = DocxTestSupport.DocumentXml(bytes);
        Assert.Contains("w:vMerge w:val=\"restart\"", xml);
        Assert.Contains("w:gridSpan w:val=\"2\"", xml);
        Assert.Contains("w:tblHeader", xml);
        Assert.Contains("w:tblLook", xml);
        Assert.Contains("w:fill=\"D9E2F3\"", xml);
    }

    [Fact]
    public void Word_tables_open_as_tables_with_markers_moved_into_the_text_and_nested_tables_kept_whole()
    {
        const string W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
        var cell = "<w:tc><w:tcPr><w:tcW w:w=\"2000\" w:type=\"dxa\"/><w:noWrap/></w:tcPr><w:p><w:r><w:t>{0}</w:t></w:r></w:p></w:tc>";
        var table = "<w:tbl><w:tblPr><w:tblStyle w:val=\"TableGrid\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\"/></w:tblPr>"
                    + "<w:tblGrid><w:gridCol w:w=\"2000\"/><w:gridCol w:w=\"2000\"/></w:tblGrid>"
                    + "<w:bookmarkStart w:id=\"1\" w:name=\"Tabelle\"/>"
                    + "<w:tr><w:trPr><w:cantSplit/></w:trPr>" + string.Format(cell, "A1") + string.Format(cell, "B1") + "</w:tr>"
                    + "<w:tr>" + string.Format(cell, "A2") + string.Format(cell, "B2") + "</w:tr>"
                    + "<w:bookmarkEnd w:id=\"1\"/></w:tbl>";
        var nested = "<w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"4000\"/></w:tblGrid><w:tr><w:tc><w:tbl><w:tblPr/><w:tblGrid><w:gridCol w:w=\"2000\"/></w:tblGrid>"
                     + "<w:tr><w:tc><w:p/></w:tc></w:tr></w:tbl><w:p/></w:tc></w:tr></w:tbl>";
        var bytes = DocxFixtures.CreateRaw($"<w:document xmlns:w=\"{W}\"><w:body>{table}<w:p/>{nested}<w:p/></w:body></w:document>");

        var document = DocxTestSupport.Read(bytes);
        var span = TableSpan.At(document, 0)!;
        Assert.Equal(4, span.BlockCount);
        Assert.True(span.Table.Rows[0].Properties.CantSplit);
        Assert.Contains("tblLook", span.Table.Properties.UnknownXml);
        Assert.Contains("noWrap", span.Table.CellAt(0, 0).UnknownXml);
        Assert.Contains(document.ParagraphAt(0)!.Inlines, inline => inline is OpaqueInline { Kind: PreservedKind.Bookmark });
        Assert.Contains(document.Blocks, block => block is OpaqueBlock { Kind: PreservedKind.Table });

        var again = DocxTestSupport.RoundTrip(document);
        DocxTestSupport.AssertSameContent(document, again);
    }

    [Fact]
    public void The_table_style_gives_the_cells_their_paragraph_spacing()
    {
        var (session, layout) = WithTable(1, 1);
        var format = session.Resolver.ResolveParagraph(session.Document.ParagraphAt(1)!);
        Assert.Equal(0, format.SpacingAfter); // Tabellenraster: no space after, single line spacing
        Assert.Equal(LineSpacing.Single, format.LineSpacing);
        Assert.Equal(8, session.Resolver.ResolveParagraph(session.Document.ParagraphAt(0)!).SpacingAfter);
        _ = layout;
    }
}
