namespace ThixCalc.Core.Model;

public enum SheetVisibility : byte
{
    Visible,
    Hidden,
    VeryHidden,
}

public enum SheetViewKind : byte
{
    Normal,
    PageBreakPreview,
    PageLayout,
}

/// <summary>
/// How a worksheet is shown (sheetView): frozen panes, the scroll position of the scrolling pane, zoom, grid lines, headers,
/// the active cell and the selection. Immutable; the editing session replaces it.
/// </summary>
public sealed record SheetView
{
    public static SheetView Default { get; } = new();

    /// <summary>Rows frozen at the top (0 = none).</summary>
    public int FrozenRows { get; init; }

    /// <summary>Columns frozen at the left (0 = none).</summary>
    public int FrozenColumns { get; init; }

    /// <summary>The first cell of the scrolling pane (the top-left visible cell below/right of the frozen panes).</summary>
    public CellAddress TopLeft { get; init; }

    /// <summary>Zoom in percent (10–400).</summary>
    public int Zoom { get; init; } = 100;

    public bool ShowGridLines { get; init; } = true;

    public bool ShowHeaders { get; init; } = true;

    public bool ShowZeros { get; init; } = true;

    public bool ShowFormulas { get; init; }

    public bool RightToLeft { get; init; }

    public bool TabSelected { get; init; }

    public SheetViewKind Kind { get; init; }

    public CellAddress ActiveCell { get; init; }

    public IReadOnlyList<CellRange> Selection { get; init; } = [CellRange.Cell(0, 0)];

    /// <summary>A split that is not frozen (in twips), kept for the round trip.</summary>
    public double SplitX { get; init; }

    public double SplitY { get; init; }

    public bool IsFrozen => FrozenRows > 0 || FrozenColumns > 0;
}

public enum ValidationType : byte
{
    Any,
    WholeNumber,
    Decimal,
    List,
    Date,
    Time,
    TextLength,
    Custom,
}

public enum ComparisonOperator : byte
{
    Between,
    NotBetween,
    Equal,
    NotEqual,
    GreaterThan,
    LessThan,
    GreaterThanOrEqual,
    LessThanOrEqual,
}

public enum ValidationErrorStyle : byte
{
    Stop,
    Warning,
    Information,
}

/// <summary>
/// A data validation rule (Daten › Datenüberprüfung). <see cref="Formula1"/>/<see cref="Formula2"/> are canonical formula
/// texts without "=" relative to the top-left cell of the first range (a list may be a quoted literal "a,b,c" or a range).
/// </summary>
public sealed record DataValidation
{
    public IReadOnlyList<CellRange> Ranges { get; init; } = [];

    public ValidationType Type { get; init; }

    public ComparisonOperator Operator { get; init; }

    public string? Formula1 { get; init; }

    public string? Formula2 { get; init; }

    public bool AllowBlank { get; init; } = true;

    /// <summary>Shows the drop-down of a list (the file's showDropDown attribute means the opposite).</summary>
    public bool ShowDropDown { get; init; } = true;

    public bool ShowInputMessage { get; init; } = true;

    public bool ShowErrorMessage { get; init; } = true;

    public ValidationErrorStyle ErrorStyle { get; init; }

    public string? ErrorTitle { get; init; }

    public string? Error { get; init; }

    public string? PromptTitle { get; init; }

    public string? Prompt { get; init; }

    public CellAddress Origin => Ranges.Count > 0 ? Ranges[0].First : default;
}

public enum ConditionalFormatType : byte
{
    CellIs,
    Expression,
    ColorScale,
    DataBar,
    IconSet,
    Top10,
    UniqueValues,
    DuplicateValues,
    ContainsText,
    NotContainsText,
    BeginsWith,
    EndsWith,
    ContainsBlanks,
    NotContainsBlanks,
    ContainsErrors,
    NotContainsErrors,
    TimePeriod,
    AboveAverage,
}

public enum ConditionalValueType : byte
{
    Number,
    Percent,
    Max,
    Min,
    Formula,
    Percentile,
    AutoMin,
    AutoMax,
}

/// <summary>A threshold of a colour scale, data bar or icon set (cfvo).</summary>
public sealed record ConditionalValue(ConditionalValueType Type, string? Value = null, bool GreaterOrEqual = true);

/// <summary>
/// One rule of a conditional format. Formulas are canonical texts relative to the top-left cell of the format's first range.
/// <see cref="Xml"/> keeps the original &lt;cfRule&gt; (written back while the rule is unchanged, so extensions survive).
/// </summary>
public sealed record ConditionalFormatRule
{
    public ConditionalFormatType Type { get; init; }

    public int Priority { get; init; } = 1;

    public bool StopIfTrue { get; init; }

    public ComparisonOperator Operator { get; init; }

    public IReadOnlyList<string> Formulas { get; init; } = [];

    /// <summary>The differential format (index into <see cref="StyleTable.DifferentialFormats"/>).</summary>
    public int? FormatId { get; init; }

    /// <summary>The text of contains/begins/ends rules.</summary>
    public string? Text { get; init; }

    /// <summary>today, yesterday, last7Days, thisMonth … (ST_TimePeriod).</summary>
    public string? TimePeriod { get; init; }

    public int Rank { get; init; } = 10;

    public bool Percent { get; init; }

    public bool Bottom { get; init; }

    public bool AboveAverage { get; init; } = true;

    public bool EqualAverage { get; init; }

    public int StdDev { get; init; }

    public IReadOnlyList<ConditionalValue> Thresholds { get; init; } = [];

    public IReadOnlyList<CalcColor> Colors { get; init; } = [];

    /// <summary>3Arrows, 3TrafficLights1, 4Rating, 5Quarters … (icon sets).</summary>
    public string? IconSet { get; init; }

    public bool ShowValue { get; init; } = true;

    public bool Reverse { get; init; }

    public string? Xml { get; init; }
}

/// <summary>A conditional format: ranges and their rules (in priority order).</summary>
public sealed record ConditionalFormat(IReadOnlyList<CellRange> Ranges, IReadOnlyList<ConditionalFormatRule> Rules)
{
    public CellAddress Origin => Ranges.Count > 0 ? Ranges[0].First : default;
}

public enum FilterOperator : byte
{
    Equal,
    NotEqual,
    GreaterThan,
    GreaterThanOrEqual,
    LessThan,
    LessThanOrEqual,
}

/// <summary>A custom filter condition: operator and a value (may contain * and ? wildcards for text).</summary>
public sealed record CustomFilter(FilterOperator Operator, string Value);

/// <summary>
/// The filter of one column of an autofilter (offset from the filter's first column): a value list (with blanks), up to two
/// custom conditions (and/or), top/bottom N, or a dynamic filter (aboveAverage, today …). <see cref="Xml"/> keeps the original.
/// </summary>
public sealed record FilterColumn
{
    public int ColumnOffset { get; init; }

    /// <summary>The values to show (display texts), or null when this is no value list.</summary>
    public IReadOnlyList<string>? Values { get; init; }

    public bool IncludeBlank { get; init; }

    public IReadOnlyList<CustomFilter> Custom { get; init; } = [];

    public bool CustomAnd { get; init; }

    public double? Top { get; init; }

    public bool TopPercent { get; init; }

    public bool Bottom { get; init; }

    public string? Dynamic { get; init; }

    public bool HiddenButton { get; init; }

    public string? Xml { get; init; }
}

public sealed record SortCondition(CellRange Range, bool Descending = false, string? CustomList = null);

public sealed record SortState(CellRange Range, IReadOnlyList<SortCondition> Conditions, bool CaseSensitive = false, bool ByColumns = false);

/// <summary>The autofilter of a worksheet: the range (header row first) and the filtered columns.</summary>
public sealed record AutoFilter(CellRange Range, IReadOnlyList<FilterColumn> Columns)
{
    public SortState? Sort { get; init; }
}

/// <summary>A hyperlink on cells: an external target (URL, file, mailto) or a location in the workbook ("Tabelle2!A1").</summary>
public sealed record Hyperlink(CellRange Range, string? Target, string? Location = null, string? Display = null, string? Tooltip = null);

/// <summary>A note (Notiz; legacy comment) on a cell.</summary>
public sealed record CellComment(string Author, string Text, bool Visible = false);

/// <summary>
/// A table (Einfügen › Tabelle) read from a file: the part stays as it is; the model knows its name, range and columns, so
/// structured references (Tabelle1[Umsatz]) evaluate and its range follows inserted and deleted rows.
/// </summary>
public sealed record TableDefinition(string Name, CellRange Range, IReadOnlyList<string> Columns, int HeaderRowCount = 1, int TotalsRowCount = 0)
{
    /// <summary>The part of the table in the package (for the writer).</summary>
    internal string? PartUri { get; init; }

    public CellRange DataRange => new(Range.FirstRow + HeaderRowCount, Range.FirstColumn, Range.LastRow - TotalsRowCount, Range.LastColumn);
}

/// <summary>
/// A defined name (Formeln › Namens-Manager): workbook-wide or local to a sheet (<see cref="SheetId"/>), its formula in
/// canonical text without "=" ("Tabelle1!$A$1:$B$10"). Built-in names keep their _xlnm. prefix (Print_Area …).
/// </summary>
public sealed record DefinedName(string Name, string Formula, int? SheetId = null, bool Hidden = false, string? Comment = null)
{
    public bool IsBuiltIn => Name.StartsWith("_xlnm.", StringComparison.OrdinalIgnoreCase);
}

/// <summary>When the workbook recalculates and how it treats circular references.</summary>
public sealed record CalculationSettings
{
    public bool Manual { get; init; }

    public bool Iterate { get; init; }

    public int MaxIterations { get; init; } = 100;

    public double MaxChange { get; init; } = 0.001;

    public bool FullCalculationOnLoad { get; init; }
}
