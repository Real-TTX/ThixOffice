using ThixCalc.Core.Formatting;
using ThixCalc.Core.Globalization;
using ThixCalc.Core.Model;
using ThixOffice.Text;

namespace ThixCalc.Core.Layout;

/// <summary>
/// The measuring of a workbook's grid: the widest digit of the default font (Excel's unit of column widths), the conversion
/// of column widths (characters) and row heights (points) to pixels at 100 % (96 dpi: 1 pt = 4/3 px), and text widths of
/// displayed values. Everything goes through <see cref="ITextMeasurer"/>, so a web version brings its own measurer.
/// </summary>
public sealed class GridMetrics
{
    private const double PixelsPerPoint = 96.0 / 72;
    private readonly Dictionary<(string Text, FontSpec Font), double> _widths = [];

    public GridMetrics(ITextMeasurer measurer, Workbook workbook, CalcLocale locale)
    {
        Measurer = measurer;
        Workbook = workbook;
        Locale = locale;
        var font = FontOf(workbook.Styles.Default.Font);
        var widest = 0.0;
        for (var digit = '0'; digit <= '9'; digit++)
        {
            widest = Math.Max(widest, measurer.Shape(digit.ToString(), font).Width);
        }

        MaxDigitWidthPixels = Math.Max(1, Math.Round(widest * PixelsPerPoint));
    }

    public ITextMeasurer Measurer { get; }

    public Workbook Workbook { get; }

    public CalcLocale Locale { get; set; }

    /// <summary>The widest digit of the default font in pixels at 100 % (7 for Calibri 11).</summary>
    public double MaxDigitWidthPixels { get; }

    /// <summary>The padding Excel adds to a column width (2 px each side + 1 px grid line).</summary>
    public double ColumnPaddingPixels => Math.Round(MaxDigitWidthPixels / 4) * 2 + 1;

    /// <summary>A column width in characters as pixels at 100 % (Excel: 8.43 → 64 px for Calibri 11).</summary>
    public double ColumnPixels(double characters)
    {
        if (characters <= 0)
        {
            return 0;
        }

        var mdw = MaxDigitWidthPixels;
        return Math.Truncate((256 * characters + Math.Truncate(128 / mdw)) / 256 * mdw + ColumnPaddingPixels);
    }

    /// <summary>A width in pixels at 100 % as the column width in characters that shows it.</summary>
    public double WidthInCharactersFromPixels(double pixels)
    {
        var mdw = MaxDigitWidthPixels;
        return Math.Truncate((pixels - ColumnPaddingPixels) / mdw * 100 + 0.5) / 100;
    }

    /// <summary>A text width in points (with the cell's padding) as a column width in characters.</summary>
    public double WidthInCharacters(double points) => WidthInCharactersFromPixels(Math.Ceiling(points * PixelsPerPoint) + ColumnPaddingPixels);

    public static double RowPixels(double points) => points * PixelsPerPoint;

    public FontSpec FontOf(CellFont font) => new(font.Name, font.Size, font.Bold, font.Italic);

    /// <summary>The width (points) of a text in a font (cached).</summary>
    public double Measure(string text, FontSpec font)
    {
        if (text.Length == 0)
        {
            return 0;
        }

        if (_widths.TryGetValue((text, font), out var width))
        {
            return width;
        }

        width = Measurer.Shape(text, font).Width;
        if (_widths.Count > 50_000)
        {
            _widths.Clear();
        }

        _widths[(text, font)] = width;
        return width;
    }

    /// <summary>The width (points) of a cell's displayed value (the longest line of wrapped text).</summary>
    public double TextWidth(Worksheet sheet, int row, int column)
    {
        var style = sheet.GetStyle(row, column);
        var text = NumberFormatter.Format(sheet.GetValue(row, column), style.NumberFormat, Locale, Workbook.Date1904).Text;
        var font = FontOf(style.Font);
        return text.Split('\n').Max(line => Measure(line, font)) + style.Alignment.Indent * MaxDigitWidthPixels * 3 / PixelsPerPoint;
    }
}
