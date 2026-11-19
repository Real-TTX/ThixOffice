using System.Collections.Immutable;

namespace ThixCalc.Core.Model.Pivot;

/// <summary>Where a field of a PivotTable is: not used, Zeilen, Spalten or Filter (Werte are the data fields).</summary>
public enum PivotAxis : byte
{
    None,
    Row,
    Column,
    Filter,
}

/// <summary>How a value field summarizes (Wertfeldeinstellungen › Werte zusammenfassen nach).</summary>
public enum PivotFunction : byte
{
    Sum,
    Count,
    Average,
    Max,
    Min,
    Product,
    CountNumbers,
    StdDev,
    StdDevP,
    Var,
    VarP,
}

/// <summary>Werte anzeigen als.</summary>
public enum PivotShowAs : byte
{
    Normal,
    PercentOfGrandTotal,
    PercentOfColumnTotal,
    PercentOfRowTotal,
    Difference,
    PercentDifference,
    RunningTotal,
}

/// <summary>Berichtslayout: Kurzformat (one indented column), Gliederungsformat, Tabellenformat.</summary>
public enum PivotLayout : byte
{
    Compact,
    Outline,
    Tabular,
}

/// <summary>The date levels a date field is grouped by (Gruppieren › Jahre, Quartale, Monate, Tage).</summary>
[Flags]
public enum PivotDateGroups : byte
{
    None = 0,
    Days = 1,
    Months = 2,
    Quarters = 4,
    Years = 8,
}

/// <summary>Numbers grouped from <see cref="Start"/> to <see cref="End"/> in steps of <see cref="Interval"/> (Gruppieren › Starten/Beenden/Nach).</summary>
public sealed record PivotNumberGroup(double Start, double End, double Interval);

/// <summary>A label filter (Beschriftungsfilter) or value filter (Wertefilter) of a row or column field.</summary>
public enum PivotFilterKind : byte
{
    Equals,
    NotEquals,
    GreaterThan,
    GreaterOrEqual,
    LessThan,
    LessOrEqual,
    BeginsWith,
    EndsWith,
    Contains,
    Between,
    Top,
    Bottom,
}

/// <summary>
/// A filter on a field's items: by label (text/number comparison of the item) or by value (the item's total of
/// <see cref="DataField"/>; Top/Bottom keep <see cref="Value"/> items).
/// </summary>
public sealed record PivotFilter(PivotFilterKind Kind, string Value, string? Value2 = null, int DataField = -1)
{
    public bool ByValue => DataField >= 0;
}

/// <summary>Sorting of a field's items: by label, or by the items' totals of a value field.</summary>
public sealed record PivotSort(bool Descending = false, int DataField = -1);

/// <summary>
/// The settings of one source field inside a PivotTable (the field list's check box and its area, Feldeinstellungen): where it is,
/// its hidden items (manual selection, by the item's label key), filters, sort, subtotals and grouping.
/// </summary>
public sealed record PivotField(int Index)
{
    public PivotAxis Axis { get; init; }

    /// <summary>The labels (<see cref="PivotItems.KeyOf"/>) of the items that are not shown.</summary>
    public ImmutableHashSet<string> HiddenItems { get; init; } = [];

    /// <summary>For a filter field: the one item shown (null = Alle).</summary>
    public string? SelectedItem { get; init; }

    public PivotFilter? LabelFilter { get; init; }

    public PivotFilter? ValueFilter { get; init; }

    public PivotSort Sort { get; init; } = new();

    public bool Subtotals { get; init; } = true;

    public PivotDateGroups DateGroups { get; init; }

    public PivotNumberGroup? NumberGroup { get; init; }

    /// <summary>The name shown instead of the source column's (Benutzerdefinierter Name).</summary>
    public string? Caption { get; init; }

    public bool IsGrouped => DateGroups != PivotDateGroups.None || NumberGroup is not null;
}

/// <summary>A value field: the source field, its name ("Summe von Umsatz"), the function, Werte anzeigen als, the number format.</summary>
public sealed record PivotDataField(int Field, string Name, PivotFunction Function = PivotFunction.Sum)
{
    public PivotShowAs ShowAs { get; init; }

    /// <summary>The base field of Differenz von / laufende Summe in (a source field index), −1 for none.</summary>
    public int BaseField { get; init; } = -1;

    /// <summary>The base item: its label key, or <see cref="PreviousItem"/> / <see cref="NextItem"/>.</summary>
    public string? BaseItem { get; init; }

    public string? NumberFormat { get; init; }

    public const string PreviousItem = "\u0001previous";

    public const string NextItem = "\u0001next";
}

/// <summary>
/// A PivotTable: where it is (the top left cell of its body; filter fields stand two rows above it), its cache, the fields and
/// areas, the value fields, and the layout and style. Immutable – every change makes a new one (undo keeps both).
/// </summary>
public sealed record PivotTable(string Name, int CacheId, CellAddress Location)
{
    public ImmutableArray<PivotField> Fields { get; init; } = [];

    /// <summary>The row fields in order; <see cref="ValuesField"/> stands for Σ Werte.</summary>
    public ImmutableArray<int> RowFields { get; init; } = [];

    public ImmutableArray<int> ColumnFields { get; init; } = [];

    public ImmutableArray<int> FilterFields { get; init; } = [];

    public ImmutableArray<PivotDataField> DataFields { get; init; } = [];

    public PivotLayout Layout { get; init; }

    public bool RepeatLabels { get; init; }

    /// <summary>Gesamtergebnisse für Zeilen (a total column at the right).</summary>
    public bool RowGrandTotals { get; init; } = true;

    /// <summary>Gesamtergebnisse für Spalten (a total row at the bottom).</summary>
    public bool ColumnGrandTotals { get; init; } = true;

    /// <summary>Teilergebnisse oben in der Gruppe (Kurzformat/Gliederung); unten otherwise.</summary>
    public bool SubtotalsAtTop { get; init; } = true;

    public string Style { get; init; } = PivotStyles.DefaultStyle;

    public bool RowHeaders { get; init; } = true;

    public bool ColumnHeaders { get; init; } = true;

    public bool BandedRows { get; init; }

    public bool BandedColumns { get; init; }

    /// <summary>The cells the PivotTable occupies (filters, headers, body), set when it is drawn into the sheet.</summary>
    public CellRange? Range { get; init; }

    /// <summary>The part it was read from (written back as read while <see cref="SourceXml"/> is set).</summary>
    public string? PartUri { get; init; }

    /// <summary>The pivotTableDefinition as read; cleared by every change (the writer then writes it new).</summary>
    public string? SourceXml { get; init; }

    /// <summary>What the model could not hold (calculated fields, OLAP …): the table is shown and kept, not edited.</summary>
    public string? Unsupported { get; init; }

    /// <summary>Σ Werte in <see cref="RowFields"/> / <see cref="ColumnFields"/> (Excel's field −2).</summary>
    public const int ValuesField = -2;

    public PivotField Field(int index) => Fields[index];

    public PivotTable WithField(int index, Func<PivotField, PivotField> change) =>
        this with { Fields = Fields.SetItem(index, change(Fields[index])), SourceXml = null };

    /// <summary>The fields on an axis in order (Σ Werte included).</summary>
    public ImmutableArray<int> FieldsOn(PivotAxis axis) => axis switch
    {
        PivotAxis.Row => RowFields,
        PivotAxis.Column => ColumnFields,
        PivotAxis.Filter => FilterFields,
        _ => [],
    };
}
