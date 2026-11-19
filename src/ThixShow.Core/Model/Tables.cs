namespace ThixShow.Core.Model;

/// <summary>
/// a:tcPr: cell margins (EMU), anchoring, borders (left, right, top, bottom; diagonals kept in <see cref="Extra"/>), fill.
/// Null = from the table style.
/// </summary>
public sealed record TableCellProperties(
    long? LeftMargin = null,
    long? RightMargin = null,
    long? TopMargin = null,
    long? BottomMargin = null,
    TextAnchor? Anchor = null,
    string? Vertical = null,
    Outline? LeftBorder = null,
    Outline? RightBorder = null,
    Outline? TopBorder = null,
    Outline? BottomBorder = null,
    Fill? Fill = null,
    XmlFragment? Extra = null)
{
    public static TableCellProperties Empty { get; } = new();
}

/// <summary>
/// a:tc: the cell's text and properties; <see cref="GridSpan"/>/<see cref="RowSpan"/> on the cell that starts a merge,
/// <see cref="HorizontalMerge"/>/<see cref="VerticalMerge"/> on the cells it covers.
/// </summary>
public sealed record TableCell(
    TextBody Text,
    TableCellProperties Properties,
    int GridSpan = 1,
    int RowSpan = 1,
    bool HorizontalMerge = false,
    bool VerticalMerge = false)
{
    public static TableCell Empty { get; } = new(TextBody.Empty, TableCellProperties.Empty);

    public bool IsCovered => HorizontalMerge || VerticalMerge;
}

/// <summary>a:tr: height in EMU and the cells (one per grid column, covered cells included).</summary>
public sealed record TableRow(long Height, ValueList<TableCell> Cells, XmlFragment? Extra = null);

/// <summary>
/// a:tblPr: the table style (a GUID of tableStyles.xml or of PowerPoint's built-in styles) and which of its parts apply (header
/// row, total row, first/last column, banded rows/columns).
/// </summary>
public sealed record TableProperties(
    string? StyleId = null,
    bool FirstRow = false,
    bool LastRow = false,
    bool FirstColumn = false,
    bool LastColumn = false,
    bool BandRows = false,
    bool BandColumns = false,
    bool RightToLeft = false,
    Fill? Fill = null,
    XmlFragment? Extra = null);

/// <summary>a:tbl: a DrawingML table – properties, the grid's column widths (EMU) and the rows.</summary>
public sealed record Table(TableProperties Properties, ValueList<long> ColumnWidths, ValueList<TableRow> Rows)
{
    public int RowCount => Rows.Count;

    public int ColumnCount => ColumnWidths.Count;

    public TableCell CellAt(int row, int column) => Rows[row].Cells[column];

    public Table WithCell(int row, int column, TableCell cell) =>
        this with { Rows = Rows.SetItem(row, Rows[row] with { Cells = Rows[row].Cells.SetItem(column, cell) }) };

    /// <summary>A table of <paramref name="rows"/> × <paramref name="columns"/> empty cells sized to fill <paramref name="width"/> × <paramref name="height"/>.</summary>
    public static Table Create(int rows, int columns, long width, long rowHeight, string? styleId = null)
    {
        var columnWidth = width / Math.Max(1, columns);
        var cells = Enumerable.Range(0, columns).Select(_ => TableCell.Empty).ToList();
        return new Table(
            new TableProperties(styleId, FirstRow: true, BandRows: true),
            Enumerable.Repeat(columnWidth, columns).ToList(),
            Enumerable.Range(0, rows).Select(_ => new TableRow(rowHeight, cells)).ToList());
    }
}

/// <summary>The parts of a table style (a:wholeTbl, a:band1H, a:firstRow …).</summary>
public enum TableStylePart
{
    WholeTable,
    Band1Horizontal,
    Band2Horizontal,
    Band1Vertical,
    Band2Vertical,
    LastColumn,
    FirstColumn,
    LastRow,
    FirstRow,
    SouthEastCell,
    SouthWestCell,
    NorthEastCell,
    NorthWestCell,
}

/// <summary>
/// The look of one part of a table style: text (bold, italic, font reference colour) and cell (fill, borders: left, right, top,
/// bottom, inside horizontal, inside vertical).
/// </summary>
public sealed record TableStyleFormat(
    bool? Bold = null,
    bool? Italic = null,
    ColorSpec? TextColor = null,
    string? FontReference = null,
    Fill? Fill = null,
    Outline? LeftBorder = null,
    Outline? RightBorder = null,
    Outline? TopBorder = null,
    Outline? BottomBorder = null,
    Outline? InsideHorizontalBorder = null,
    Outline? InsideVerticalBorder = null);

/// <summary>a:tblStyle of tableStyles.xml: its id (GUID), name and the formats of its parts.</summary>
public sealed record TableStyle(string Id, string Name, ValueList<(TableStylePart Part, TableStyleFormat Format)> Parts)
{
    public TableStyleFormat? FormatOf(TableStylePart part) =>
        Parts.FirstOrDefault(entry => entry.Part == part) is { Format: { } format } ? format : null;
}
