using ThixOffice.Text.Drawing;

namespace ThixOffice.Charts;

/// <summary>The kind of a plot: what draws its series.</summary>
public enum ChartType : byte
{
    Column,
    Bar,
    Line,
    Pie,
    Doughnut,
    Area,
    Scatter,
}

/// <summary>How the series of a plot share a category: side by side, stacked, or stacked to 100 %.</summary>
public enum ChartGrouping : byte
{
    Clustered,
    Stacked,
    PercentStacked,
}

/// <summary>Where the legend sits.</summary>
public enum LegendPosition : byte
{
    Right,
    Top,
    Bottom,
    Left,
    TopRight,
}

/// <summary>What an axis shows: categories (text), values, or dates on a time scale.</summary>
public enum AxisKind : byte
{
    Category,
    Value,
    Date,
}

/// <summary>The side of the plot area an axis is drawn at.</summary>
public enum AxisPosition : byte
{
    Bottom,
    Left,
    Top,
    Right,
}

/// <summary>Empty cells in a line or scatter series: a gap, zero, or a line across (DrawingML dispBlanksAs).</summary>
public enum BlankCells : byte
{
    Gap,
    Zero,
    Span,
}

/// <summary>Where a data label sits relative to its point.</summary>
public enum DataLabelPosition : byte
{
    Auto,
    Center,
    InsideEnd,
    InsideBase,
    OutsideEnd,
    Above,
    Below,
    Left,
    Right,
    BestFit,
}

/// <summary>The marker of a line or scatter point.</summary>
public enum MarkerSymbol : byte
{
    Auto,
    None,
    Circle,
    Square,
    Diamond,
    Triangle,
    X,
    Plus,
    Dash,
    Star,
}

/// <summary>Where an axis crosses the other: at zero (or the minimum), at its minimum, at its maximum, or at a value.</summary>
public enum AxisCrossing : byte
{
    AutoZero,
    Minimum,
    Maximum,
    Value,
}

/// <summary>
/// A colour of a chart: a theme accent (1–6) with a luminance change (DrawingML lumMod / lumOff) or a fixed RGB colour. The
/// accent stays an accent in the file, so a chart follows the document's theme.
/// </summary>
public sealed record ChartColor(int Accent, double LumMod = 1, double LumOff = 0, RgbColor? Rgb = null)
{
    public static ChartColor FromRgb(RgbColor color) => new(0, Rgb: color);

    public static ChartColor FromAccent(int accent, double lumMod = 1, double lumOff = 0) => new(accent, lumMod, lumOff);

    /// <summary>The colour with the theme's six accents.</summary>
    public RgbColor Resolve(IReadOnlyList<RgbColor> accents)
    {
        if (Rgb is { } rgb)
        {
            return rgb;
        }

        var baseColor = Accent >= 1 && Accent <= accents.Count ? accents[Accent - 1] : RgbColor.Black;
        return ChartColors.ApplyLuminance(baseColor, LumMod, LumOff);
    }
}

/// <summary>
/// A reference to data (a series' values, its categories, its name, a title): <see cref="Formula"/> as the file writes it
/// ("Tabelle1!$B$2:$B$13"), and the values the file cached (shown when the formula cannot be resolved). Either may be null.
/// </summary>
public sealed record ChartDataRef(string? Formula, IReadOnlyList<ChartValue>? Cache = null, string? FormatCode = null)
{
    /// <summary>Whether the cache holds numbers (c:numRef) rather than text (c:strRef).</summary>
    public bool IsNumeric { get; init; } = true;

    /// <summary>Categories of several levels (c:multiLvlStrRef) as read: written back unchanged while the formula is.</summary>
    public string? MultiLevelXml { get; init; }

    /// <summary>A reference with literal values only (no formula).</summary>
    public static ChartDataRef Literal(IEnumerable<double?> values) => new(null, values.Select(value => new ChartValue(value, null)).ToList());

    public static ChartDataRef LiteralText(IEnumerable<string> texts) => new(null, texts.Select(text => new ChartValue(null, text)).ToList()) { IsNumeric = false };
}

/// <summary>One value of chart data: a number, a text (a category label, a name), or both (a number shown as formatted text).</summary>
public readonly record struct ChartValue(double? Number, string? Text, string? FormatCode = null, bool IsHidden = false)
{
    public static ChartValue Blank => default;

    public bool IsBlank => Number is null && string.IsNullOrEmpty(Text);
}

/// <summary>A text of a chart (title, series name): a literal or a reference to a cell; null parts mean "automatic".</summary>
public sealed record ChartText(string? Literal, ChartDataRef? Reference = null)
{
    public static ChartText Of(string text) => new(text);

    public static ChartText Ref(string formula, string? cached = null) =>
        new(null, new ChartDataRef(formula, cached is null ? null : [new ChartValue(null, cached)]) { IsNumeric = false });
}

/// <summary>
/// A title (the chart's or an axis'): its text (null = automatic: "Diagrammtitel", or the only series' name), whether it lies
/// over the plot, its font size (null = the default), and the original XML of what the model does not own (rich text runs
/// stay in the file while the text is unchanged).
/// </summary>
public sealed record ChartTitle(ChartText? Text = null, bool Overlay = false, double? FontSize = null, bool Bold = false)
{
    /// <summary>The c:title element as read; written back while <see cref="Text"/> is unchanged.</summary>
    public string? SourceXml { get; init; }
}

/// <summary>Which parts of a data label show, where it sits, and its number format (null = the values').</summary>
public sealed record DataLabels(
    bool ShowValue = true,
    bool ShowCategory = false,
    bool ShowSeriesName = false,
    bool ShowPercent = false,
    DataLabelPosition Position = DataLabelPosition.Auto,
    string? NumberFormat = null)
{
    public bool IsVisible => ShowValue || ShowCategory || ShowSeriesName || ShowPercent;

    /// <summary>The c:dLbls element as read (written back while the labels are unchanged).</summary>
    public string? SourceXml { get; init; }
}

/// <summary>
/// A data series: its name, categories (or the x values of a scatter), values, and look. Look fields that are null are
/// automatic (the colour set's colour of <see cref="Index"/>). <see cref="ShapeXml"/> / <see cref="ExtraXml"/> keep what the
/// model does not own (gradients, trend lines, error bars, single points) for the file.
/// </summary>
public sealed record ChartSeries(int Index, ChartDataRef Values)
{
    public int Order { get; init; }

    public ChartText? Name { get; init; }

    /// <summary>The categories, or for a scatter the x values; null = 1, 2, 3 …</summary>
    public ChartDataRef? Categories { get; init; }

    public ChartColor? Fill { get; init; }

    /// <summary>The line of a line/scatter series (or the outline of a bar); null = automatic.</summary>
    public ChartColor? Line { get; init; }

    /// <summary>The line width in points (null = 2.25 for lines).</summary>
    public double? LineWidth { get; init; }

    /// <summary>No line (a scatter with markers only, a line series without its line).</summary>
    public bool NoLine { get; init; }

    public MarkerSymbol Marker { get; init; } = MarkerSymbol.Auto;

    public double? MarkerSize { get; init; }

    public bool Smooth { get; init; }

    public DataLabels? Labels { get; init; }

    /// <summary>How far pie slices move out (percent of the radius).</summary>
    public int Explosion { get; init; }

    /// <summary>Colours of single points (pie slices, highlighted bars) by point index.</summary>
    public IReadOnlyDictionary<int, ChartColor>? PointColors { get; init; }

    /// <summary>The c:spPr element as read (written back while <see cref="Fill"/>/<see cref="Line"/> are unchanged).</summary>
    public string? ShapeXml { get; init; }

    /// <summary>Other children as read (c:dPt, c:trendline, c:errBars, c:extLst …), in their order.</summary>
    public IReadOnlyList<string> ExtraXml { get; init; } = [];
}

/// <summary>
/// An axis: kind, position, title, gridlines, number format, scale (null = automatic), orientation, where it crosses its
/// partner (<see cref="CrossAxisId"/>). <see cref="Deleted"/> hides it but keeps its scale.
/// </summary>
public sealed record ChartAxis(int Id, AxisKind Kind, AxisPosition Position, int CrossAxisId)
{
    public bool Deleted { get; init; }

    public ChartTitle? Title { get; init; }

    public bool MajorGridlines { get; init; }

    public bool MinorGridlines { get; init; }

    /// <summary>The number format of the labels (null or <see cref="SourceLinked"/>: the data's).</summary>
    public string? NumberFormat { get; init; }

    public bool SourceLinked { get; init; } = true;

    public double? Minimum { get; init; }

    public double? Maximum { get; init; }

    public double? MajorUnit { get; init; }

    public double? MinorUnit { get; init; }

    public bool Reverse { get; init; }

    public AxisCrossing Crosses { get; init; } = AxisCrossing.AutoZero;

    public double? CrossesAt { get; init; }

    /// <summary>Values between the categories (columns) rather than on them (lines may sit on the tick marks).</summary>
    public bool CrossBetween { get; init; } = true;

    /// <summary>No tick labels (tickLblPos none).</summary>
    public bool HideLabels { get; init; }

    /// <summary>The label font size in points (null = 9).</summary>
    public double? FontSize { get; init; }

    /// <summary>The label rotation in degrees from the file (null = automatic).</summary>
    public double? LabelRotation { get; init; }

    /// <summary>The c:spPr / c:txPr of the axis as read (line and text look the model does not own).</summary>
    public string? ShapeXml { get; init; }

    public string? TextXml { get; init; }

    public bool IsVertical => Position is AxisPosition.Left or AxisPosition.Right;
}

/// <summary>The legend: position, whether it overlaps the plot, the font size (null = 9).</summary>
public sealed record ChartLegend(LegendPosition Position = LegendPosition.Bottom, bool Overlay = false, double? FontSize = null)
{
    /// <summary>The c:legend element as read (entries hidden by the file, its look), written back while position and overlay are unchanged.</summary>
    public string? SourceXml { get; init; }
}

/// <summary>
/// A plot of a chart: one chart type over a pair of axes (a combo chart has several). Column/bar: gap width and overlap
/// (percent); pie/doughnut: first slice angle, hole size, varied colours; line: markers. <see cref="ExtraXml"/> keeps children
/// the model does not own (drop lines, up/down bars …).
/// </summary>
public sealed record ChartPlot(ChartType Type, ChartGrouping Grouping, IReadOnlyList<ChartSeries> Series)
{
    public int GapWidth { get; init; } = 150;

    public int Overlap { get; init; }

    public bool VaryColors { get; init; }

    public int HoleSize { get; init; } = 75;

    public int FirstSliceAngle { get; init; }

    /// <summary>Line plots: whether points get markers (c:marker val).</summary>
    public bool ShowMarkers { get; init; } = true;

    public DataLabels? Labels { get; init; }

    /// <summary>The ids of the plot's axes (category or x first, then value or y); empty for pie and doughnut.</summary>
    public IReadOnlyList<int> AxisIds { get; init; } = [];

    public IReadOnlyList<string> ExtraXml { get; init; } = [];

    public bool IsHorizontal => Type == ChartType.Bar;

    public bool IsRound => Type is ChartType.Pie or ChartType.Doughnut;
}

/// <summary>
/// A chart, independent of where it lives (a sheet, a slide, a page): its plots and axes, title, legend, and how blanks and
/// hidden cells count. A chart of a type ThixOffice does not draw (3-D, radar, stock, bubble, surface …) keeps its file part
/// unchanged and shows a labelled frame: <see cref="UnsupportedType"/> names it. <see cref="SourceXml"/> is the c:chartSpace
/// as read: the writer starts from it, so what the model does not own stays.
/// </summary>
public sealed record Chart
{
    public ChartTitle? Title { get; init; }

    /// <summary>The automatic title is switched off (c:autoTitleDeleted).</summary>
    public bool AutoTitleDeleted { get; init; }

    public IReadOnlyList<ChartPlot> Plots { get; init; } = [];

    public IReadOnlyList<ChartAxis> Axes { get; init; } = [];

    public ChartLegend? Legend { get; init; } = new();

    /// <summary>Only visible cells count (c:plotVisOnly).</summary>
    public bool PlotVisibleOnly { get; init; } = true;

    public BlankCells DisplayBlanksAs { get; init; } = BlankCells.Gap;

    /// <summary>The colour set (Farben ändern): 0 = Farbig 1 (the accents in order); see <see cref="ChartColors.Sets"/>.</summary>
    public int ColorSet { get; init; }

    public bool RoundedCorners { get; init; }

    /// <summary>The German name of a chart type that is kept but not drawn (null for a chart ThixOffice draws).</summary>
    public string? UnsupportedType { get; init; }

    /// <summary>The original c:chartSpace XML (null for a new chart).</summary>
    public string? SourceXml { get; init; }

    public bool IsSupported => UnsupportedType is null && Plots.Count > 0;

    /// <summary>Every series of every plot, in drawing order.</summary>
    public IEnumerable<ChartSeries> AllSeries => Plots.SelectMany(plot => plot.Series);

    public ChartAxis? Axis(int id) => Axes.FirstOrDefault(axis => axis.Id == id);

    /// <summary>The plot's category (x) and value (y) axes, or nulls for a pie.</summary>
    public (ChartAxis? Category, ChartAxis? Value) AxesOf(ChartPlot plot)
    {
        if (plot.AxisIds.Count < 2)
        {
            return (null, null);
        }

        return (Axis(plot.AxisIds[0]), Axis(plot.AxisIds[1]));
    }
}
