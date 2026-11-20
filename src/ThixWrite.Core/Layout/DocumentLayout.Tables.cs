using ThixWrite.Core.Model;

namespace ThixWrite.Core.Layout;

/// <summary>
/// The layout of tables: column widths (the grid, scaled to a percent width), every cell's paragraphs laid out at the cell's
/// text width (cached per paragraph like any other), row heights (content, "Mindestens" / "Genau", vertical merges), and the
/// pagination by rows – a row that does not fit breaks between its lines (unless "Zeilenwechsel auf Seiten zulassen" is off or
/// cells are merged vertically), heading rows repeat at the top of every following page.
/// </summary>
public sealed partial class DocumentLayout
{
    /// <summary>The geometry of a table on its section's text width.</summary>
    private sealed class TableGeometry
    {
        public required TableSpan Span { get; init; }

        public required double[] ColumnX { get; init; }

        public required CellBox[][] Cells { get; init; }

        public required double[] RowHeights { get; init; }

        public int HeaderRows { get; init; }

        public Table Table => Span.Table;
    }

    /// <summary>A cell of the geometry: where it is, its margins, the lines of its paragraphs (offset from the top of its text).</summary>
    private sealed class CellBox
    {
        public int Row { get; init; }

        public int Cell { get; init; }

        public int FirstBlock { get; init; }

        public int LastBlock { get; init; }

        public int RowSpan { get; init; } = 1;

        public bool Covered { get; init; }

        public double Left { get; init; }

        public double Width { get; init; }

        public required CellMargins Margins { get; init; }

        public required BorderLine?[] Borders { get; init; }

        public string? Fill { get; init; }

        public CellVerticalAlignment Alignment { get; init; }

        public List<CellLine> Lines { get; } = [];

        /// <summary>The height the content needs down the row (for turned text: the length of its lines).</summary>
        public double ContentHeight { get; set; }

        /// <summary>"Textrichtung": 90 (top to bottom), 270 (bottom to top) or 0.</summary>
        public int Rotation { get; init; }

        /// <summary>Turned text: the explicit row height its lines are as long as (null: as long as the longest line needs).</summary>
        public double? RowHeight { get; init; }

        /// <summary>Turned text: how much room the lines take across the cell (their heights).</summary>
        public double Thickness { get; set; }

        public double Top => Margins.Top ?? 0;

        public double Bottom => Margins.Bottom ?? 0;

        public double ContentLeft => Left + (Margins.Left ?? 0);
    }

    private readonly record struct CellLine(int Block, int LineIndex, double Offset, LineLayout Line, ParagraphLayout Paragraph);

    /// <summary>Lays out the table that starts at <paramref name="start"/> for a section's text area.</summary>
    private TableGeometry BuildTable(int start, SectionProperties section)
    {
        var span = TableSpan.At(Document, start)!;
        var table = span.Table;
        var properties = _context.Resolver.ResolveTable(table);
        var widths = ColumnWidths(table, properties, section.ContentWidth);
        var firstMargin = (properties.CellMargins ?? CellMargins.WordDefault).Left ?? 0;
        var tableWidth = widths.Sum();
        var left = section.MarginLeft + section.Gutter + (properties.Alignment switch
        {
            TableAlignment.Center => (section.ContentWidth - tableWidth) / 2,
            TableAlignment.Right => section.ContentWidth - tableWidth,
            _ => (properties.Indent ?? 0) - firstMargin, // the text of the first column lines up with the margin, as in Word
        });
        if (properties.Position is { } position)
        {
            left = FloatingTableLeft(position, tableWidth, section);
        }
        var columnX = new double[widths.Length + 1];
        columnX[0] = left;
        for (var index = 0; index < widths.Length; index++)
        {
            columnX[index + 1] = columnX[index] + widths[index];
        }

        var borders = properties.Borders ?? BorderSet.Empty;
        var cells = new CellBox[table.RowCount][];
        for (var row = 0; row < table.RowCount; row++)
        {
            var rowCells = table.Rows[row].Cells;
            cells[row] = new CellBox[rowCells.Count];
            for (var cell = 0; cell < rowCells.Count; cell++)
            {
                var cellProperties = rowCells[cell];
                var column = Math.Min(table.GridColumnOf(row, cell), widths.Length - 1);
                var end = Math.Min(column + Math.Max(1, cellProperties.GridSpan), widths.Length);
                var covered = table.IsCovered(row, cell);
                var rowSpan = covered ? 1 : table.RowSpanOf(row, cell);
                var margins = (properties.CellMargins ?? CellMargins.WordDefault).Merge(cellProperties.Margins);
                var own = cellProperties.Borders ?? BorderSet.Empty;
                var lastRow = row + rowSpan - 1 >= table.RowCount - 1;
                var (first, last) = span.BlocksOf(row, cell);
                var conditional = ConditionalCell(properties.StyleId, table, row, cell, column, end, widths.Length, lastRow);
                cells[row][cell] = new CellBox
                {
                    Row = row,
                    Cell = cell,
                    FirstBlock = first,
                    LastBlock = last,
                    RowSpan = rowSpan,
                    Covered = covered,
                    Left = columnX[column],
                    Width = Math.Max(1, columnX[end] - columnX[column]),
                    Margins = margins,
                    Borders =
                    [
                        own.Top ?? conditional.Borders[0] ?? (row == 0 ? borders.Top : borders.InsideHorizontal),
                        own.Left ?? conditional.Borders[1] ?? (column == 0 || cell == 0 ? borders.Left : borders.InsideVertical), // a row after a gap has its own outer edge
                        own.Bottom ?? conditional.Borders[2] ?? (lastRow ? borders.Bottom : borders.InsideHorizontal),
                        own.Right ?? conditional.Borders[3] ?? (end >= widths.Length || cell == rowCells.Count - 1 ? borders.Right : borders.InsideVertical),
                    ],
                    Fill = (cellProperties.Shading ?? conditional.Shading ?? properties.Shading)?.VisibleFill,
                    Alignment = cellProperties.VerticalAlignment ?? conditional.Alignment ?? CellVerticalAlignment.Top,
                    Rotation = cellProperties.Rotation,
                    RowHeight = rowSpan == 1 ? table.Rows[row].Properties.Height : null,
                };
                LayoutCell(cells[row][cell]);
            }
        }

        return new TableGeometry
        {
            Span = span,
            ColumnX = columnX,
            Cells = cells,
            RowHeights = RowHeights(table, cells),
            HeaderRows = table.HeaderRowCount < table.RowCount ? table.HeaderRowCount : 0,
        };
    }

    /// <summary>The left edge of a floating table (w:tblpPr): its frame (page, margin, or the text = the margin), aligned or at its offset.</summary>
    private static double FloatingTableLeft(TablePosition position, double width, SectionProperties section)
    {
        var (frameX, frameWidth) = position.HorizontalFrame == "page"
            ? (0.0, section.PageWidth)
            : (section.MarginLeft + section.Gutter, section.ContentWidth);
        return position.AlignX switch
        {
            "center" => frameX + (frameWidth - width) / 2,
            "right" or "outside" => frameX + frameWidth - width,
            "left" or "inside" => frameX,
            _ => frameX + position.X,
        };
    }

    /// <summary>
    /// What the conditional formats of the table style give a cell (Überschrift, Bänder, erste Spalte …): its four borders (top, left,
    /// bottom, right – a format's outer lines where the cell is at the edge of that part, its inside lines elsewhere), shading and
    /// vertical alignment, later formats over earlier ones.
    /// </summary>
    private (BorderLine?[] Borders, Shading? Shading, CellVerticalAlignment? Alignment) ConditionalCell(string? styleId, Table table, int row, int cell, int column, int end, int columns, bool lastRow)
    {
        var result = (Borders: new BorderLine?[4], Shading: (Shading?)null, Alignment: (CellVerticalAlignment?)null);
        var resolver = _context.Resolver;
        foreach (var condition in resolver.ConditionsOf(styleId, resolver.ConditionMask(table, row, cell)))
        {
            result.Shading = condition.Shading ?? result.Shading;
            result.Alignment = condition.VerticalAlignment ?? result.Alignment;
            if (condition.Borders is not { } lines)
            {
                continue;
            }

            var kind = condition.Kind;
            var rows = kind is TableConditionKind.FirstRow or TableConditionKind.LastRow or TableConditionKind.Band1Horizontal or TableConditionKind.Band2Horizontal;
            var cols = kind is TableConditionKind.FirstColumn or TableConditionKind.LastColumn or TableConditionKind.Band1Vertical or TableConditionKind.Band2Vertical;
            var corner = kind >= TableConditionKind.TopRightCell;
            BorderLine?[] sides =
            [
                rows || corner ? lines.Top : row == 0 ? lines.Top : lines.InsideHorizontal,
                cols || corner ? lines.Left : column == 0 ? lines.Left : lines.InsideVertical,
                rows || corner ? lines.Bottom : lastRow ? lines.Bottom : lines.InsideHorizontal,
                cols || corner ? lines.Right : end >= columns ? lines.Right : lines.InsideVertical,
            ];
            for (var side = 0; side < 4; side++)
            {
                result.Borders[side] = sides[side] ?? result.Borders[side];
            }
        }

        return result;
    }

    /// <summary>
    /// The width of every grid column: the grid as Word stored it (scaled to a percent width of the table), or – without a usable
    /// grid – the cells' widths, or the text width shared out evenly.
    /// </summary>
    private static double[] ColumnWidths(Table table, TableProperties properties, double available)
    {
        var count = Math.Max(1, table.ColumnCount);
        double[] widths;
        if (table.Grid.Count == count && table.Grid.All(width => width > 0))
        {
            widths = [.. table.Grid];
        }
        else
        {
            widths = Enumerable.Repeat(available / count, count).ToArray();
            var firstRow = table.Rows.Count > 0 ? table.Rows[0].Cells : EquatableArray<TableCellProperties>.Empty;
            if (firstRow.Count == count && firstRow.All(cell => cell.Width is { Kind: WidthKind.Points, Value: > 0 }))
            {
                widths = firstRow.Select(cell => cell.Width!.Value.Value).ToArray();
            }
        }

        if (properties.Width is { Kind: WidthKind.Percent } percent && percent.Resolve(available) is { } target)
        {
            var sum = widths.Sum();
            if (sum > 0)
            {
                widths = widths.Select(width => width * target / sum).ToArray();
            }
        }

        return widths;
    }

    private void LayoutCell(CellBox box)
    {
        if (box.FirstBlock < 0)
        {
            return;
        }

        var width = Math.Max(1, box.Width - (box.Margins.Left ?? 0) - (box.Margins.Right ?? 0));
        if (box.Rotation != 0)
        {
            // Turned text: the lines run down the row – as long as an explicit row height allows, else as the longest line needs.
            width = box.RowHeight is { } height ? Math.Max(1, height - box.Top - box.Bottom) : Math.Max(1, NaturalLength(box));
        }

        var y = 0.0;
        var previousAfter = 0.0;
        string? previousStyle = null;
        var previousContextual = false;
        for (var block = box.FirstBlock; block <= box.LastBlock; block++)
        {
            var layout = EnsureLayout(block, width);
            var format = layout.Format;
            var before = format.SpacingBefore;
            var after = previousAfter;
            if (format.StyleId == previousStyle && format.ContextualSpacing)
            {
                before = 0;
            }

            if (format.StyleId == previousStyle && previousContextual)
            {
                after = 0;
            }

            var (borderTop, borderBottom) = BorderSpace(block, format);
            y += before + after + borderTop;
            for (var index = 0; index < layout.Lines.Count; index++)
            {
                box.Lines.Add(new CellLine(block, index, y, layout.Lines[index], layout));
                y += layout.Lines[index].Height;
            }

            y += borderBottom;

            previousAfter = format.SpacingAfter;
            previousStyle = format.StyleId;
            previousContextual = format.ContextualSpacing;
        }

        box.ContentHeight = y + previousAfter;
        if (box.Rotation != 0)
        {
            box.Thickness = box.ContentHeight;
            box.ContentHeight = width;
        }
    }

    /// <summary>The length of the longest line of a cell's paragraphs when nothing wraps them (turned text in a row of automatic height).</summary>
    private double NaturalLength(CellBox box)
    {
        var length = 0.0;
        for (var block = box.FirstBlock; block <= box.LastBlock; block++)
        {
            if (Document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            var layout = ParagraphLayouter.Layout(paragraph, _context.Resolver.ResolveParagraph(paragraph), null, 2000, _context);
            foreach (var line in layout.Lines)
            {
                length = Math.Max(length, line.Items.Where(item => item is TextItem or ObjectItem).Select(item => item.X + item.Width).DefaultIfEmpty(0).Max());
            }
        }

        return Math.Ceiling(length + 1);
    }

    /// <summary>The height of every row: the tallest cell (text + margins), at least / exactly the row's height; vertically merged cells stretch their last row.</summary>
    private static double[] RowHeights(Table table, CellBox[][] cells)
    {
        var heights = new double[table.RowCount];
        for (var row = 0; row < table.RowCount; row++)
        {
            var natural = 0.0;
            foreach (var box in cells[row])
            {
                if (!box.Covered && box.RowSpan == 1)
                {
                    natural = Math.Max(natural, box.Top + box.ContentHeight + box.Bottom);
                }
            }

            var properties = table.Rows[row].Properties;
            heights[row] = properties.Height is { } height
                ? properties.ExactHeight ? height : Math.Max(height, natural)
                : Math.Max(natural, 1);
        }

        for (var row = 0; row < table.RowCount; row++)
        {
            foreach (var box in cells[row].Where(box => !box.Covered && box.RowSpan > 1))
            {
                var needed = box.Top + box.ContentHeight + box.Bottom;
                var spanned = Enumerable.Range(row, box.RowSpan).Sum(index => heights[index]);
                if (needed > spanned)
                {
                    heights[row + box.RowSpan - 1] += needed - spanned;
                }
            }
        }

        return heights;
    }

    private sealed partial class Flow
    {
        private readonly Dictionary<int, (List<PlacedLine> Lines, List<int> Pages)> _tablePlacements = [];

        /// <summary>Places a table row by row (see the class summary) and records where every line of every cell went.</summary>
        private void PlaceTable(int start, SectionInfo section)
        {
            var geometry = owner.BuildTable(start, section.Properties);
            var table = geometry.Table;
            _tablePlacements.Clear();
            if (table.Properties.Position is { } position)
            {
                if (!TryPlaceFloating(geometry, position) && Page.Lines.Count > 0 && geometry.RowHeights.Sum() <= Bottom - Setup.MarginTop)
                {
                    NextColumn(); // like an object anchored at the foot of a page: it goes on to the next one
                    TryPlaceFloating(geometry, position);
                }

                if (_tablePlacements.Count > 0)
                {
                    Commit(geometry);
                    return;
                }
            }

            if (Page.Lines.Count > 0)
            {
                _y += _pendingAfter;
            }

            _pendingAfter = 0;
            var row = 0;
            while (row < table.RowCount)
            {
                var groupEnd = GroupEnd(geometry, row);
                var height = Enumerable.Range(row, groupEnd - row + 1).Sum(index => geometry.RowHeights[index]);
                var keepTogether = groupEnd > row || table.Rows[row].Properties.CantSplit || table.Rows[row].Properties.ExactHeight
                                   || geometry.Cells[row].Any(box => box.Rotation != 0);
                var fullPage = Bottom - Setup.MarginTop;
                if (_y + height > Bottom + Epsilon && Page.Lines.Count > 0 && (keepTogether || !CanStartRowHere(geometry, row)) && height <= fullPage + Epsilon)
                {
                    NewTablePage(geometry, row);
                    continue;
                }

                if (_y + height > Bottom + Epsilon && !keepTogether)
                {
                    PlaceSplitRow(geometry, row);
                    row++;
                    continue;
                }

                for (var index = row; index <= groupEnd; index++)
                {
                    PlaceRow(geometry, index, _y, repeat: false);
                    _y += geometry.RowHeights[index];
                }

                row = groupEnd + 1;
            }

            Commit(geometry);
            _previousStyle = null;
            _previousContextual = false;
            _afterHardBreak = false;
        }

        /// <summary>
        /// A floating table (w:tblpPr) that fits on the page where its frame puts it: its rows are placed there in one piece, the text
        /// after it goes on where it was and flows around it (the table's area with its distances, like a square-wrapped object).
        /// False when it does not fit: then it is placed in the flow.
        /// </summary>
        private bool TryPlaceFloating(TableGeometry geometry, TablePosition position)
        {
            var height = geometry.RowHeights.Sum();
            var anchorTop = _y + (Page.Lines.Count > 0 ? _pendingAfter : 0);
            var (frameY, frameHeight) = position.VerticalFrame switch
            {
                "page" => (0.0, Setup.PageHeight),
                "margin" => (Setup.MarginTop, Setup.PageHeight - Setup.MarginTop - Setup.MarginBottom),
                _ => (anchorTop, 0.0),
            };
            var top = position.AlignY switch
            {
                "center" => frameY + (frameHeight - height) / 2,
                "bottom" or "outside" => frameY + frameHeight - height,
                "top" or "inside" => frameY,
                _ => frameY + position.Y,
            };
            if (top < 0 || top + height > Bottom + Epsilon)
            {
                return false;
            }

            var y = top;
            for (var row = 0; row < geometry.Table.RowCount; row++)
            {
                PlaceRow(geometry, row, y, repeat: false);
                y += geometry.RowHeights[row];
            }

            var (left, right) = (geometry.ColumnX[0], geometry.ColumnX[^1]);
            var (areaLeft, areaWidth) = ColumnArea;
            var anchor = new DrawingAnchor(DrawingFrame.Column, 0, null, DrawingFrame.Paragraph, 0, null, DrawingWrap.Square, false,
                position.DistanceLeft, position.DistanceTop, position.DistanceRight, position.DistanceBottom);
            Page.MutableFloats.Add(new PlacedFloat(geometry.Span.Start, -1, new TextRun("", CharacterProperties.Empty), anchor, left, top, right - left, height)
            {
                AreaLeft = areaLeft,
                AreaRight = areaLeft + areaWidth,
            });
            return true;
        }

        /// <summary>The last row tied to <paramref name="row"/> by vertically merged cells (they go to a page together).</summary>
        private static int GroupEnd(TableGeometry geometry, int row)
        {
            var end = row;
            for (var index = row; index <= end && index < geometry.Cells.Length; index++)
            {
                foreach (var box in geometry.Cells[index])
                {
                    if (!box.Covered)
                    {
                        end = Math.Max(end, index + box.RowSpan - 1);
                    }
                }
            }

            return Math.Min(end, geometry.Cells.Length - 1);
        }

        /// <summary>True when at least the first line of every cell of the row fits below the current y.</summary>
        private bool CanStartRowHere(TableGeometry geometry, int row) =>
            geometry.Cells[row].Where(box => !box.Covered && box.Lines.Count > 0)
                .All(box => _y + box.Top + box.Lines[0].Offset + box.Lines[0].Line.Height <= Bottom + Epsilon);

        /// <summary>Places a whole row at <paramref name="top"/> (repeat: a heading row again on a new page, drawn only).</summary>
        private void PlaceRow(TableGeometry geometry, int row, double top, bool repeat)
        {
            foreach (var box in geometry.Cells[row])
            {
                if (box.Covered)
                {
                    continue;
                }

                var height = Enumerable.Range(row, box.RowSpan).Sum(index => geometry.RowHeights[index]);
                if (box.Rotation != 0)
                {
                    PlaceTurnedCell(box, top, height, repeat);
                    AddCell(geometry, box, top, height, repeat);
                    continue;
                }

                var free = height - box.Top - box.ContentHeight - box.Bottom;
                var shift = free <= 0 ? 0 : box.Alignment switch
                {
                    CellVerticalAlignment.Center => free / 2,
                    CellVerticalAlignment.Bottom => free,
                    _ => 0,
                };
                foreach (var line in box.Lines)
                {
                    var placed = new PlacedLine(line.Block, line.LineIndex, top + box.Top + shift + line.Offset, box.ContentLeft, line.Line, line.Paragraph);
                    Add(placed, repeat);
                }

                AddCell(geometry, box, top, height, repeat);
            }
        }

        /// <summary>
        /// The lines of a cell with turned text: placed as if across at the cell's top left (where hit testing finds them), drawn through a
        /// transform – top to bottom with the first line at the right (90°), bottom to top with the first line at the left (270°); the
        /// vertical alignment of the cell moves them across it.
        /// </summary>
        private void PlaceTurnedCell(CellBox box, double top, double height, bool repeat)
        {
            var left = box.ContentLeft;
            var right = box.Left + box.Width - (box.Margins.Right ?? 0);
            var free = Math.Max(0, right - left - box.Thickness);
            var shift = box.Alignment switch
            {
                CellVerticalAlignment.Center => free / 2,
                CellVerticalAlignment.Bottom => free,
                _ => 0,
            };
            var contentTop = top + box.Top;
            var contentBottom = top + height - box.Bottom;
            var transform = box.Rotation == 90
                ? new ThixOffice.Text.Drawing.Matrix2D(0, 1, -1, 0, right - shift + contentTop, contentTop - left)
                : new ThixOffice.Text.Drawing.Matrix2D(0, -1, 1, 0, left + shift - contentTop, contentBottom + left);
            foreach (var line in box.Lines)
            {
                Add(new PlacedLine(line.Block, line.LineIndex, contentTop + line.Offset, left, line.Line, line.Paragraph) { Transform = transform }, repeat);
            }
        }

        /// <summary>A row taller than what is left of the page: its lines go on until the page is full, the rest to the next page.</summary>
        private void PlaceSplitRow(TableGeometry geometry, int row)
        {
            var boxes = geometry.Cells[row].Where(box => !box.Covered).ToList();
            var next = boxes.ToDictionary(box => box, _ => 0);
            var consumed = boxes.ToDictionary(box => box, _ => 0.0);
            while (true)
            {
                var fragment = 0.0;
                var placedAny = false;
                foreach (var box in boxes)
                {
                    var available = Bottom - _y - box.Top - box.Bottom;
                    var index = next[box];
                    while (index < box.Lines.Count && box.Lines[index].Offset + box.Lines[index].Line.Height - consumed[box] <= available + Epsilon)
                    {
                        index++;
                    }

                    if (index == next[box] && index < box.Lines.Count && Page.Lines.Count == 0)
                    {
                        index++; // a line taller than a page: it goes here anyway
                    }

                    if (index == next[box])
                    {
                        continue;
                    }

                    placedAny = true;
                    for (var line = next[box]; line < index; line++)
                    {
                        var cellLine = box.Lines[line];
                        Add(new PlacedLine(cellLine.Block, cellLine.LineIndex, _y + box.Top + cellLine.Offset - consumed[box], box.ContentLeft, cellLine.Line, cellLine.Paragraph), repeat: false);
                    }

                    var last = box.Lines[index - 1];
                    fragment = Math.Max(fragment, box.Top + last.Offset + last.Line.Height - consumed[box] + box.Bottom);
                    next[box] = index;
                }

                var done = boxes.All(box => next[box] >= box.Lines.Count);
                if (!placedAny && !done)
                {
                    NewTablePage(geometry, row);
                    continue;
                }

                if (done)
                {
                    var remaining = geometry.RowHeights[row] - consumed.Values.DefaultIfEmpty(0).Max();
                    fragment = Math.Max(fragment, Math.Min(remaining, Bottom - _y));
                }

                foreach (var box in boxes)
                {
                    AddCell(geometry, box, _y, fragment, repeat: false);
                }

                _y += fragment;
                if (done)
                {
                    return;
                }

                foreach (var box in boxes)
                {
                    consumed[box] = next[box] < box.Lines.Count ? box.Lines[next[box]].Offset : box.ContentHeight;
                }

                NewTablePage(geometry, row);
            }
        }

        /// <summary>A new page inside a table: the heading rows are drawn again at its top.</summary>
        private void NewTablePage(TableGeometry geometry, int row)
        {
            NewPage(hard: false);
            if (geometry.HeaderRows == 0 || row < geometry.HeaderRows)
            {
                return;
            }

            for (var header = 0; header < geometry.HeaderRows; header++)
            {
                PlaceRow(geometry, header, _y, repeat: true);
                _y += geometry.RowHeights[header];
            }
        }

        private void Add(PlacedLine line, bool repeat)
        {
            if (repeat)
            {
                Page.MutableRepeatedLines.Add(line);
                return;
            }

            Page.MutableLines.Add(line);
            if (owner.NotesIn(line.Block, line.Line) is { Count: > 0 } notes)
            {
                AddFootnotes(line.Block, line.LineIndex, notes, Math.Max(_y, line.Top + line.Line.Height));
            }

            if (!_tablePlacements.TryGetValue(line.Block, out var placement))
            {
                placement = ([], []);
                _tablePlacements[line.Block] = placement;
            }

            placement.Lines.Add(line);
            placement.Pages.Add(_page);
        }

        private void AddCell(TableGeometry geometry, CellBox box, double top, double height, bool repeat) =>
            Page.MutableCells.Add(new PlacedCell(
                geometry.Span.Start, box.Row, box.Cell, box.Left, top, box.Width, height,
                box.Borders[0], box.Borders[1], box.Borders[2], box.Borders[3], box.Fill, repeat));

        /// <summary>
        /// Records the placements of the table's blocks. The paragraphs of covered cells (vertical merges) are not shown: they get
        /// the place of their merged cell's first line, so that every block has one.
        /// </summary>
        private void Commit(TableGeometry geometry)
        {
            var span = geometry.Span;
            for (var block = span.Start; block <= span.End; block++)
            {
                if (_tablePlacements.TryGetValue(block, out var placement) && placement.Lines.Count == owner._layouts[block]!.Lines.Count)
                {
                    owner._placements[block] = new BlockPlacement([.. placement.Lines], [.. placement.Pages]);
                    continue;
                }

                var layout = owner._layouts[block] ?? owner.EnsureLayout(block, 1);
                var anchor = FindAnchor(geometry, block);
                var lines = layout.Lines.Select((line, index) => new PlacedLine(block, index, anchor.Line.Top, anchor.Line.Left, line, layout)).ToArray();
                owner._placements[block] = new BlockPlacement(lines, Enumerable.Repeat(anchor.Page, lines.Length).ToArray());
            }

            _tablePlacements.Clear();
        }

        /// <summary>Where a hidden paragraph (of a covered cell) is said to be: the first line of the cell that covers it.</summary>
        private (PlacedLine Line, int Page) FindAnchor(TableGeometry geometry, int block)
        {
            var cell = ((Paragraph)owner.Document.Blocks[block]).Cell!.Value;
            var (row, index) = geometry.Table.MergeOrigin(cell.Row, cell.Cell);
            var origin = geometry.Cells[row][index];
            if (origin.FirstBlock >= 0 && _tablePlacements.TryGetValue(origin.FirstBlock, out var placement) && placement.Lines.Count > 0)
            {
                return (placement.Lines[0], placement.Pages[0]);
            }

            var any = _tablePlacements.Values.FirstOrDefault(value => value.Lines.Count > 0);
            return any.Lines is { Count: > 0 } ? (any.Lines[0], any.Pages[0]) : (new PlacedLine(block, 0, _y, Setup.MarginLeft, owner._layouts[block]!.Lines[0], owner._layouts[block]!), _page);
        }
    }
}
