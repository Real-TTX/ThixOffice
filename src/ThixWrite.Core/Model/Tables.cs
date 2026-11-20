namespace ThixWrite.Core.Model;

/// <summary>How a table sits between the margins (w:jc of the table).</summary>
public enum TableAlignment
{
    Left,
    Center,
    Right,
}

/// <summary>"Automatisch anpassen" (column widths follow the content) or fixed column widths (w:tblLayout).</summary>
public enum TableLayoutKind
{
    Auto,
    Fixed,
}

/// <summary>The unit of a table or cell width (w:tblW / w:tcW type).</summary>
public enum WidthKind
{
    Auto,
    Points,
    Percent,
    Nil,
}

/// <summary>A preferred width: automatic, in points, or in percent (0–100) of the available width.</summary>
public readonly record struct TableWidth(WidthKind Kind, double Value)
{
    public static TableWidth Auto => new(WidthKind.Auto, 0);

    public static TableWidth Points(double points) => new(WidthKind.Points, points);

    public static TableWidth Percent(double percent) => new(WidthKind.Percent, percent);

    /// <summary>The width in points for <paramref name="available"/> points, or null when automatic.</summary>
    public double? Resolve(double available) => Kind switch
    {
        WidthKind.Points when Value > 0 => Value,
        WidthKind.Percent when Value > 0 => available * Value / 100,
        _ => null,
    };
}

/// <summary>Where a cell is in a vertical merge (w:vMerge): not merged, the first cell, or a cell covered by the one above.</summary>
public enum VerticalMerge
{
    None,
    Restart,
    Continue,
}

/// <summary>"Ausrichtung" of the text in a cell (w:vAlign).</summary>
public enum CellVerticalAlignment
{
    Top,
    Center,
    Bottom,
}

/// <summary>
/// One border line (w:top, w:insideH …): its style as Word writes it (<c>single</c>, <c>double</c>, <c>dotted</c>, <c>dashed</c>,
/// <c>thick</c>, <c>nil</c>/<c>none</c> = no line …), width and space in points, colour "RRGGBB" or "auto", theme colour.
/// </summary>
public sealed record BorderLine(string Style, double Width = 0.5, string? Color = "auto", double Space = 0, string? ThemeColor = null)
{
    /// <summary>"Kein Rahmen" – a border set to nothing (overrides the table's).</summary>
    public static readonly BorderLine None = new("nil", 0, null);

    /// <summary>Word's default grid line: single, ½ pt, automatic colour.</summary>
    public static readonly BorderLine Single = new("single", 0.5, "auto");

    public bool IsVisible => Style is not ("nil" or "none") && Width > 0;
}

/// <summary>The borders of a table or a cell; null sides are not set (they come from the table, the style or nothing).</summary>
public sealed record BorderSet(
    BorderLine? Top = null,
    BorderLine? Left = null,
    BorderLine? Bottom = null,
    BorderLine? Right = null,
    BorderLine? InsideHorizontal = null,
    BorderLine? InsideVertical = null)
{
    public static readonly BorderSet Empty = new();

    /// <summary>Every line of a grid ("Alle Rahmenlinien").</summary>
    public static BorderSet All(BorderLine line) => new(line, line, line, line, line, line);

    /// <summary>This set with the sides that are set in <paramref name="other"/> taken from it.</summary>
    public BorderSet Merge(BorderSet? other) => other is null ? this : new(
        other.Top ?? Top,
        other.Left ?? Left,
        other.Bottom ?? Bottom,
        other.Right ?? Right,
        other.InsideHorizontal ?? InsideHorizontal,
        other.InsideVertical ?? InsideVertical);

    public bool IsEmpty => this == Empty;
}

/// <summary>The distances between a cell's edge and its text, in points; null sides are not set.</summary>
public sealed record CellMargins(double? Top = null, double? Left = null, double? Bottom = null, double? Right = null)
{
    /// <summary>Word's defaults ("Normale Tabelle"): 0 above and below, 5.4 pt left and right.</summary>
    public static readonly CellMargins WordDefault = new(0, 5.4, 0, 5.4);

    public CellMargins Merge(CellMargins? other) => other is null ? this : new(
        other.Top ?? Top,
        other.Left ?? Left,
        other.Bottom ?? Bottom,
        other.Right ?? Right);
}

/// <summary>The shading of a table or cell (w:shd): the fill "RRGGBB" (or "auto"), the pattern and its colour, a theme fill.</summary>
public sealed record Shading(string? Fill, string Pattern = "clear", string? Color = "auto", string? ThemeFill = null, string? ThemeFillTint = null, string? ThemeFillShade = null)
{
    /// <summary>The fill as "RRGGBB", or null when it shows nothing (auto, no fill).</summary>
    public string? VisibleFill => Fill is { Length: 6 } fill && !string.Equals(fill, "auto", StringComparison.OrdinalIgnoreCase) ? fill : null;
}

/// <summary>
/// The properties of a table (w:tblPr): style, alignment, indent, preferred width, layout, borders, default cell margins,
/// shading, Tabellenformatoptionen (w:tblLook), band sizes of a style; everything else (w:tblCaption, floating position …) is kept as <see cref="UnknownXml"/>.
/// </summary>
public sealed record TableProperties
{
    public static readonly TableProperties Empty = new();

    public string? StyleId { get; init; }

    public TableAlignment? Alignment { get; init; }

    /// <summary>The indent of the table from the left margin (w:tblInd), points.</summary>
    public double? Indent { get; init; }

    public TableWidth? Width { get; init; }

    public TableLayoutKind? Layout { get; init; }

    public BorderSet? Borders { get; init; }

    public CellMargins? CellMargins { get; init; }

    public Shading? Shading { get; init; }

    /// <summary>Which conditional formats of the table style apply (w:tblLook), null = Word's default.</summary>
    public TableLook? Look { get; init; }

    /// <summary>Rows per band of a table style (w:tblStyleRowBandSize), null = 1.</summary>
    public int? RowBandSize { get; init; }

    /// <summary>Columns per band of a table style (w:tblStyleColBandSize), null = 1.</summary>
    public int? ColumnBandSize { get; init; }

    public string? UnknownXml { get; init; }

    /// <summary>
    /// Where a floating table sits ("Umgebender Text", w:tblpPr). Read for the display only: the element itself stays in
    /// <see cref="UnknownXml"/> and is written back from there.
    /// </summary>
    public TablePosition? Position { get; init; }

    /// <summary>These properties with the ones set in <paramref name="other"/> taken from it (style chain, direct formatting).</summary>
    public TableProperties Merge(TableProperties? other) => other is null ? this : new TableProperties
    {
        StyleId = other.StyleId ?? StyleId,
        Alignment = other.Alignment ?? Alignment,
        Indent = other.Indent ?? Indent,
        Width = other.Width ?? Width,
        Layout = other.Layout ?? Layout,
        Borders = (Borders ?? BorderSet.Empty).Merge(other.Borders),
        CellMargins = (CellMargins ?? new CellMargins()).Merge(other.CellMargins),
        Shading = other.Shading ?? Shading,
        Look = other.Look ?? Look,
        RowBandSize = other.RowBandSize ?? RowBandSize,
        ColumnBandSize = other.ColumnBandSize ?? ColumnBandSize,
        UnknownXml = other.UnknownXml ?? UnknownXml,
        Position = other.Position ?? Position,
    };
}

/// <summary>
/// The position of a floating table (w:tblpPr): horizontal and vertical frame ("text", "margin", "page"), an offset in points or an
/// alignment ("left", "center", "right", "inside", "outside"; "top", "center", "bottom"), and the distances the text keeps.
/// </summary>
public sealed record TablePosition(
    string HorizontalFrame,
    double X,
    string? AlignX,
    string VerticalFrame,
    double Y,
    string? AlignY,
    double DistanceLeft,
    double DistanceTop,
    double DistanceRight,
    double DistanceBottom);

/// <summary>
/// The properties of a row (w:trPr): height (at least or exact), "Überschrift wiederholen" (w:tblHeader), "Zeilenwechsel auf
/// Seiten zulassen" off (w:cantSplit); other children as <see cref="UnknownXml"/>, the row's table exceptions (w:tblPrEx) as
/// <see cref="ExceptionsXml"/>.
/// </summary>
public sealed record TableRowProperties
{
    public static readonly TableRowProperties Empty = new();

    public double? Height { get; init; }

    public bool ExactHeight { get; init; }

    public bool IsHeader { get; init; }

    public bool CantSplit { get; init; }

    /// <summary>Grid columns left empty before the row's first cell (w:gridBefore): the row starts with a gap.</summary>
    public int GridBefore { get; init; }

    /// <summary>Grid columns left empty after the row's last cell (w:gridAfter).</summary>
    public int GridAfter { get; init; }

    public string? UnknownXml { get; init; }

    public string? ExceptionsXml { get; init; }
}

/// <summary>
/// The properties of a cell (w:tcPr): preferred width, the grid columns it spans (w:gridSpan), its place in a vertical merge,
/// borders, shading, margins, vertical alignment; other children (w:noWrap, w:textDirection, w:hideMark …) as
/// <see cref="UnknownXml"/>.
/// </summary>
public sealed record TableCellProperties
{
    public static readonly TableCellProperties Empty = new();

    public TableWidth? Width { get; init; }

    public int GridSpan { get; init; } = 1;

    public VerticalMerge VerticalMerge { get; init; }

    public BorderSet? Borders { get; init; }

    public Shading? Shading { get; init; }

    public CellMargins? Margins { get; init; }

    public CellVerticalAlignment? VerticalAlignment { get; init; }

    /// <summary>"Textrichtung" (w:textDirection) as Word writes it: <c>btLr</c> (bottom to top), <c>tbRl</c> (top to bottom), null = across.</summary>
    public string? TextDirection { get; init; }

    /// <summary>How far the text of the cell is turned: 90 (top to bottom), 270 (bottom to top) or 0.</summary>
    public int Rotation => TextDirection switch
    {
        "tbRl" or "tbRlV" or "rl" or "tbLrV" => 90,
        "btLr" or "lr" => 270,
        _ => 0,
    };

    public string? UnknownXml { get; init; }
}

/// <summary>A row: its properties and the properties of its cells in order (the cells' paragraphs are in the document's story).</summary>
public sealed record TableRow(TableRowProperties Properties, EquatableArray<TableCellProperties> Cells)
{
    public int CellCount => Cells.Count;
}

/// <summary>
/// The structure of a table, IMMUTABLE: properties, the grid (column widths in points, w:tblGrid) and the rows with their
/// cells. The TEXT of the table is not here: every cell's paragraphs are ordinary paragraphs in the document's list of blocks,
/// in reading order (row by row, cell by cell), each with a <see cref="CellRef"/> to this instance. A table is the run of
/// consecutive blocks whose cell names the same instance (<see cref="TableSpan"/>); a change of the structure replaces those
/// blocks with paragraphs naming a new instance – one undoable step like every other edit.
/// </summary>
public sealed record Table(TableProperties Properties, EquatableArray<double> Grid, EquatableArray<TableRow> Rows)
{
    public int RowCount => Rows.Count;

    /// <summary>The number of grid columns (the widest row's span when the grid is missing).</summary>
    public int ColumnCount => Grid.Count > 0
        ? Grid.Count
        : Rows.Select(row => row.Properties.GridBefore + row.Cells.Sum(cell => Math.Max(1, cell.GridSpan)) + row.Properties.GridAfter).DefaultIfEmpty(0).Max();

    public TableCellProperties CellAt(int row, int cell) => Rows[row].Cells[cell];

    /// <summary>The first grid column of cell <paramref name="cell"/> of row <paramref name="row"/> (after the row's w:gridBefore gap).</summary>
    public int GridColumnOf(int row, int cell)
    {
        var column = Rows[row].Properties.GridBefore;
        var cells = Rows[row].Cells;
        for (var index = 0; index < cell && index < cells.Count; index++)
        {
            column += Math.Max(1, cells[index].GridSpan);
        }

        return column;
    }

    /// <summary>The cell of row <paramref name="row"/> that covers grid column <paramref name="column"/>, or -1.</summary>
    public int CellAtColumn(int row, int column)
    {
        var start = Rows[row].Properties.GridBefore;
        var cells = Rows[row].Cells;
        for (var index = 0; index < cells.Count; index++)
        {
            var span = Math.Max(1, cells[index].GridSpan);
            if (column >= start && column < start + span)
            {
                return index;
            }

            start += span;
        }

        return -1;
    }

    /// <summary>True when the cell is covered by a vertically merged cell above (its paragraphs are hidden).</summary>
    public bool IsCovered(int row, int cell) => row < Rows.Count && cell < Rows[row].Cells.Count && Rows[row].Cells[cell].VerticalMerge == VerticalMerge.Continue;

    /// <summary>The number of rows a cell spans downwards (1 unless it starts a vertical merge).</summary>
    public int RowSpanOf(int row, int cell)
    {
        if (Rows[row].Cells[cell].VerticalMerge != VerticalMerge.Restart)
        {
            return 1;
        }

        var column = GridColumnOf(row, cell);
        var span = 1;
        while (row + span < Rows.Count)
        {
            var below = CellAtColumn(row + span, column);
            if (below < 0 || Rows[row + span].Cells[below].VerticalMerge != VerticalMerge.Continue || GridColumnOf(row + span, below) != column)
            {
                break;
            }

            span++;
        }

        return span;
    }

    /// <summary>The cell that shows the content of a covered cell (the start of its vertical merge), or the cell itself.</summary>
    public (int Row, int Cell) MergeOrigin(int row, int cell)
    {
        var column = GridColumnOf(row, cell);
        while (row > 0 && IsCovered(row, cell))
        {
            var above = CellAtColumn(row - 1, column);
            if (above < 0)
            {
                break;
            }

            row--;
            cell = above;
        }

        return (row, cell);
    }

    /// <summary>The number of rows at the top that repeat on every page ("Überschriften wiederholen").</summary>
    public int HeaderRowCount
    {
        get
        {
            var count = 0;
            while (count < Rows.Count && Rows[count].Properties.IsHeader)
            {
                count++;
            }

            return count;
        }
    }
}

/// <summary>Which cell of which table a paragraph belongs to (<see cref="Paragraph.Cell"/>).</summary>
public readonly record struct CellRef(Table Table, int Row, int Cell)
{
    /// <summary>True when the paragraph is in a cell covered by a vertical merge (it is kept, not shown, the caret skips it).</summary>
    public bool IsCovered => Table.IsCovered(Row, Cell);

    /// <summary>The same cell of the same table (compares the table by instance).</summary>
    public bool IsSameCell(CellRef? other) => other is { } cell && ReferenceEquals(cell.Table, Table) && cell.Row == Row && cell.Cell == Cell;

    public override string ToString() => $"R{Row + 1}C{Cell + 1}";
}

/// <summary>The parts of a table a conditional format of a table style (w:tblStylePr) formats, in the order Word applies them (later wins).</summary>
public enum TableConditionKind
{
    WholeTable,
    Band1Vertical,
    Band2Vertical,
    Band1Horizontal,
    Band2Horizontal,
    FirstColumn,
    LastColumn,
    FirstRow,
    LastRow,
    TopRightCell,
    TopLeftCell,
    BottomRightCell,
    BottomLeftCell,
}

/// <summary>
/// "Tabellenformatoptionen" (w:tblLook): which conditional formats of the style a table shows – Überschrift, Ergebniszeile, erste und
/// letzte Spalte, gebänderte Zeilen und Spalten. <see cref="Default"/> is what Word gives a new table (04A0).
/// </summary>
public sealed record TableLook(bool FirstRow, bool LastRow, bool FirstColumn, bool LastColumn, bool BandedRows, bool BandedColumns)
{
    public static readonly TableLook Default = new(true, false, true, false, true, false);
}

/// <summary>
/// One conditional format of a table style (w:tblStylePr): paragraph and character properties for the text, cell borders, shading
/// and alignment for the cells of that part. Read for the display; the XML stays in <see cref="Style.UnknownXml"/> for the round trip.
/// </summary>
public sealed record TableStyleCondition(
    TableConditionKind Kind,
    ParagraphProperties Paragraph,
    CharacterProperties Character,
    BorderSet? Borders = null,
    Shading? Shading = null,
    CellVerticalAlignment? VerticalAlignment = null);
