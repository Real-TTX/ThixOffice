using System.Globalization;
using System.Text;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;

namespace ThixCalc.Core.Layout;

/// <summary>
/// A printed page of a sheet: the cells on it, the rows repeated above them (Drucktitel), the paper (points) and the scale.
/// </summary>
public sealed record PrintedPage(Worksheet Sheet, CellRange Cells, CellRange? TitleRows, double Width, double Height, double Scale);

/// <summary>
/// The grid cut into pages, UI-free (the desktop prints and exports these display lists; a web version can too): the print area
/// (or the used range, or a given range such as the selection) is broken into page columns and rows that fit between the
/// margins at the page setup's scale – or at the scale that fits it onto N × M pages – in Excel's order (down, then over),
/// with the title rows repeated. Each page renders through <see cref="GridRenderer"/> in points (the cells' look, merged cells,
/// overflowing text, borders, conditional formats; grid lines and headings when the setup asks), plus the header and footer.
/// </summary>
public static class SheetPagination
{
    private const double PointsPerPixel = 0.75;

    /// <summary>The range a sheet prints: its print area, or what it uses (cells with content or format, merges).</summary>
    public static CellRange? PrintedRange(Worksheet sheet)
    {
        if (EditingSession.PrintAreaOf(sheet) is { } area)
        {
            return area;
        }

        // Charts print too: the used range grows to cover them.
        var range = sheet.UsedRange;
        foreach (var chart in sheet.Charts)
        {
            range = range is { } existing ? existing.Union(chart.Anchor.Cells) : chart.Anchor.Cells;
        }

        return range;
    }

    /// <summary>The pages of <paramref name="sheet"/> (of <paramref name="range"/> when given); empty when nothing is to print.</summary>
    public static IReadOnlyList<PrintedPage> Paginate(Worksheet sheet, GridMetrics metrics, CellRange? range = null)
    {
        if ((range ?? PrintedRange(sheet)) is not { } area)
        {
            return [];
        }

        var used = sheet.UsedRange ?? CellRange.Cell(0, 0);
        area = area.Intersect(new CellRange(0, 0, Math.Max(used.LastRow, area.IsWholeColumns ? 0 : area.LastRow), Math.Max(used.LastColumn, area.IsWholeRows ? 0 : area.LastColumn))) ?? CellRange.Cell(area.First);
        var setup = sheet.PageSetup;
        var layout = new SheetLayout(sheet, metrics);
        var (paperWidth, paperHeight) = setup.PaperPoints;
        var availableWidth = Math.Max(36, paperWidth - (setup.LeftMargin + setup.RightMargin) * 72 - (setup.PrintHeadings ? HeadingWidth(metrics, area) : 0));
        var availableHeight = Math.Max(36, paperHeight - (setup.TopMargin + setup.BottomMargin) * 72 - (setup.PrintHeadings ? HeadingHeight(sheet) : 0));
        double Width(int column) => layout.ColumnWidth(column, 1) * PointsPerPixel;
        double Height(int row) => layout.RowHeight(row, 1) * PointsPerPixel;

        var titles = EditingSession.PrintTitlesOf(sheet) is { } rows ? new CellRange(rows.FirstRow, area.FirstColumn, rows.LastRow, area.LastColumn) : (CellRange?)null;
        var titleHeight = titles is { } title ? Enumerable.Range(title.FirstRow, title.RowCount).Sum(Height) : 0;

        var scale = setup.Scale / 100.0;
        if (setup.FitToPage)
        {
            var totalWidth = Enumerable.Range(area.FirstColumn, area.ColumnCount).Sum(Width);
            var totalHeight = Enumerable.Range(area.FirstRow, Math.Min(area.RowCount, 200_000)).Sum(Height);
            scale = 1;
            if (setup.FitToWidth > 0 && totalWidth > 0)
            {
                scale = Math.Min(scale, availableWidth * setup.FitToWidth / totalWidth);
            }

            if (setup.FitToHeight > 0 && totalHeight > 0)
            {
                scale = Math.Min(scale, availableHeight * setup.FitToHeight / (totalHeight + titleHeight * Math.Max(0, setup.FitToHeight - 1)));
            }
        }

        scale = Math.Clamp(scale, 0.1, 4);
        var columnBands = Bands(area.FirstColumn, area.LastColumn, column => Width(column) * scale, availableWidth);
        var pages = new List<PrintedPage>();
        var rowBands = new List<(int First, int Last)>();
        var row = area.FirstRow;
        while (row <= area.LastRow)
        {
            var repeat = titles is { } repeatedTitle && row > repeatedTitle.LastRow ? titleHeight * scale : 0;
            var band = Bands(row, area.LastRow, index => Height(index) * scale, availableHeight - repeat, single: true)[0];
            rowBands.Add(band);
            row = band.Last + 1;
        }

        IEnumerable<(int Row, int Column)> order = setup.OverThenDown
            ? rowBands.SelectMany((_, rowIndex) => columnBands.Select((_, columnIndex) => (rowIndex, columnIndex)))
            : columnBands.SelectMany((_, columnIndex) => rowBands.Select((_, rowIndex) => (rowIndex, columnIndex)));
        foreach (var (rowIndex, columnIndex) in order)
        {
            var (firstRow, lastRow) = rowBands[rowIndex];
            var (firstColumn, lastColumn) = columnBands[columnIndex];
            var repeated = titles is { } pageTitle && firstRow > pageTitle.LastRow ? new CellRange(pageTitle.FirstRow, firstColumn, pageTitle.LastRow, lastColumn) : (CellRange?)null;
            pages.Add(new PrintedPage(sheet, new CellRange(firstRow, firstColumn, lastRow, lastColumn), repeated, paperWidth, paperHeight, scale));
        }

        return pages;
    }

    /// <summary>Greedy bands of indexes whose sizes fit <paramref name="available"/> (at least one per band; hidden ones are free).</summary>
    private static List<(int First, int Last)> Bands(int first, int last, Func<int, double> size, double available, bool single = false)
    {
        var bands = new List<(int, int)>();
        var start = first;
        var used = 0.0;
        for (var index = first; index <= last; index++)
        {
            var next = size(index);
            if (used + next > available + 0.01 && index > start)
            {
                bands.Add((start, index - 1));
                if (single)
                {
                    return bands;
                }

                start = index;
                used = 0;
            }

            used += next;
        }

        bands.Add((start, last));
        return bands;
    }

    private static double HeadingWidth(GridMetrics metrics, CellRange area) =>
        (Math.Max(3, (area.LastRow + 1).ToString(CultureInfo.InvariantCulture).Length) * metrics.MaxDigitWidthPixels + 10) * PointsPerPixel;

    private static double HeadingHeight(Worksheet sheet) => GridMetrics.RowPixels(sheet.Rows.DefaultSize) * PointsPerPixel;

    /// <summary>
    /// A page as a display list in points: the cells (and title rows) at the page's scale between the margins (centred when the
    /// setup says), then the header and the footer with page <paramref name="number"/> of <paramref name="count"/>.
    /// </summary>
    public static PageDisplayList Render(PrintedPage page, GridMetrics metrics, int index, int number, int count, string fileName, DateTime now, RgbColor? headingBackground = null)
    {
        var sheet = page.Sheet;
        var setup = sheet.PageSetup;
        var layout = new SheetLayout(sheet, metrics);
        var zoom = page.Scale * PointsPerPixel;
        var headerWidth = setup.PrintHeadings ? Math.Round(HeadingWidth(metrics, page.Cells) * page.Scale) : 0;
        var headerHeight = setup.PrintHeadings ? Math.Round(HeadingHeight(sheet) * page.Scale) : 0;

        var titleRows = new List<AxisSegment>();
        var y = headerHeight;
        if (page.TitleRows is { } titles)
        {
            for (var row = titles.FirstRow; row <= titles.LastRow; row++)
            {
                var size = layout.RowHeight(row, zoom);
                if (size > 0)
                {
                    titleRows.Add(new AxisSegment(row, y, size));
                }

                y += size;
            }
        }

        var scrollTop = y;
        var rowOrigin = layout.RowTop(page.Cells.FirstRow, zoom) - y;
        var rows = new List<AxisSegment>();
        for (var row = page.Cells.FirstRow; row <= page.Cells.LastRow; row++)
        {
            var size = layout.RowHeight(row, zoom);
            if (size > 0)
            {
                rows.Add(new AxisSegment(row, layout.RowTop(row, zoom) - rowOrigin, size));
            }
        }

        var columnOrigin = layout.ColumnLeft(page.Cells.FirstColumn, zoom) - headerWidth;
        var columns = new List<AxisSegment>();
        for (var column = page.Cells.FirstColumn; column <= page.Cells.LastColumn; column++)
        {
            var size = layout.ColumnWidth(column, zoom);
            if (size > 0)
            {
                columns.Add(new AxisSegment(column, layout.ColumnLeft(column, zoom) - columnOrigin, size));
            }
        }

        var width = columns.Count > 0 ? columns[^1].End : headerWidth;
        var height = rows.Count > 0 ? rows[^1].End : scrollTop;
        var viewport = new GridViewport(layout, width + 1, height + 1, zoom, headerWidth, headerHeight, titleRows, rows, [], columns, rowOrigin, columnOrigin)
        {
            ScrollTop = scrollTop,
            ScrollLeft = headerWidth,
            FrozenRowLimit = page.TitleRows is { } title ? title.LastRow + 1 : 0,
            FrozenRowShift = page.TitleRows is { } shifted ? layout.RowTop(shifted.FirstRow, zoom) : 0,
            FrozenColumnLimit = 0,
        };
        var options = new GridRenderOptions
        {
            GridLines = setup.PrintGridLines,
            DrawHeaders = setup.PrintHeadings,
            HeaderBackground = headingBackground ?? RgbColor.FromHex("F2F2F2"),
            HeaderHighlight = RgbColor.FromHex("F2F2F2"),
            HeaderLine = RgbColor.FromHex("BFBFBF"),
            HeaderText = RgbColor.FromHex("404040"),
            FreezeLine = RgbColor.FromHex("BFBFBF"),
            ChartPixelSize = 0,
        };
        var grid = GridRenderer.Render(viewport, metrics, options);

        var left = setup.LeftMargin * 72;
        var top = setup.TopMargin * 72;
        if (setup.CenterHorizontally)
        {
            left += Math.Max(0, (page.Width - (setup.LeftMargin + setup.RightMargin) * 72 - width) / 2);
        }

        if (setup.CenterVertically)
        {
            top += Math.Max(0, (page.Height - (setup.TopMargin + setup.BottomMargin) * 72 - height) / 2);
        }

        // The grid's own background stays out: the paper is white, and the grid's last line is drawn.
        // Items inside a transform are in its coordinates: only the transform itself moves.
        var items = new List<DisplayItem>();
        var depth = 0;
        foreach (var item in grid.Items.Skip(1))
        {
            items.Add(depth == 0 ? Translate(item, left, top) : item);
            depth += item switch
            {
                TransformBeginItem => 1,
                TransformEndItem when depth > 0 => -1,
                _ => 0,
            };
        }

        var context = new HeaderContext(number, count, sheet.Name, fileName, now);
        AddHeaderFooter(items, metrics.Measurer, setup.Header, page.Width, setup.HeaderMargin * 72, top: true, setup, context);
        AddHeaderFooter(items, metrics.Measurer, setup.Footer, page.Width, page.Height - setup.FooterMargin * 72, top: false, setup, context);
        return new PageDisplayList(index, page.Width, page.Height, items);
    }

    /// <summary>A display item moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    private static DisplayItem Translate(DisplayItem item, double dx, double dy) => item switch
    {
        GlyphRunItem run => run with { X = run.X + dx, Y = run.Y + dy },
        RectangleItem rectangle => rectangle with { X = rectangle.X + dx, Y = rectangle.Y + dy },
        LineItem line => line with { X1 = line.X1 + dx, Y1 = line.Y1 + dy, X2 = line.X2 + dx, Y2 = line.Y2 + dy },
        ClipBeginItem clip => clip with { X = clip.X + dx, Y = clip.Y + dy },
        ImageItem image => image with { X = image.X + dx, Y = image.Y + dy },
        TransformBeginItem transform => transform.Translated(dx, dy),
        VectorPathItem path => path.Translated(dx, dy),
        ClipPathBeginItem clipPath => clipPath with { Path = clipPath.Path.Transform(Matrix2D.Translation(dx, dy)) },
        _ => item,
    };

    private sealed record HeaderContext(int Page, int Pages, string Sheet, string File, DateTime Now);

    /// <summary>The three sections (&amp;L, &amp;C, &amp;R) of a header or footer text with its codes resolved (formatting codes dropped).</summary>
    public static (string Left, string Center, string Right) Sections(string? text, int page, int pages, string sheet, string file, DateTime now)
    {
        if (string.IsNullOrEmpty(text))
        {
            return (string.Empty, string.Empty, string.Empty);
        }

        var sections = new[] { new StringBuilder(), new StringBuilder(), new StringBuilder() };
        var current = 1;
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character != '&' || index + 1 >= text.Length)
            {
                sections[current].Append(character);
                continue;
            }

            var code = text[++index];
            switch (char.ToUpperInvariant(code))
            {
                case 'L':
                    current = 0;
                    break;
                case 'C':
                    current = 1;
                    break;
                case 'R':
                    current = 2;
                    break;
                case 'P':
                    sections[current].Append(page.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'N':
                    sections[current].Append(pages.ToString(CultureInfo.InvariantCulture));
                    break;
                case 'D':
                    sections[current].Append(now.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture));
                    break;
                case 'T':
                    sections[current].Append(now.ToString("HH:mm", CultureInfo.InvariantCulture));
                    break;
                case 'A':
                    sections[current].Append(sheet);
                    break;
                case 'F':
                    sections[current].Append(file);
                    break;
                case '&':
                    sections[current].Append('&');
                    break;
                case '"':
                    index = text.IndexOf('"', index + 1) is var end and >= 0 ? end : text.Length;
                    break;
                default:
                    while (index + 1 < text.Length && char.IsDigit(text[index + 1]) && char.IsDigit(code))
                    {
                        index++;
                    }

                    break;
            }
        }

        return (sections[0].ToString(), sections[1].ToString(), sections[2].ToString());
    }

    private static void AddHeaderFooter(List<DisplayItem> items, ITextMeasurer measurer, string? text, double pageWidth, double y, bool top, PageSetup setup, HeaderContext context)
    {
        var (left, center, right) = Sections(text, context.Page, context.Pages, context.Sheet, context.File, context.Now);
        var font = new FontSpec("Calibri", 11, false, false);
        var metrics = measurer.GetMetrics(font);
        var baseline = top ? y + metrics.Ascent : y - metrics.Descent;
        void Add(string part, Func<double, double> x)
        {
            if (part.Length == 0)
            {
                return;
            }

            var shaped = measurer.Shape(part.Replace('\n', ' '), font);
            GridRenderer.AddRun(items, x(shaped.Width), baseline, shaped, RgbColor.Black);
        }

        Add(left, _ => setup.LeftMargin * 72);
        Add(center, width => (pageWidth - width) / 2);
        Add(right, width => pageWidth - setup.RightMargin * 72 - width);
    }
}
