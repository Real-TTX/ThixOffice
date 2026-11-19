namespace ThixWrite.Core.Model;

/// <summary>Horizontal alignment of a paragraph.</summary>
public enum ParagraphAlignment
{
    Left,
    Center,
    Right,
    Justify,
}

/// <summary>How <see cref="LineSpacing.Value"/> is read.</summary>
public enum LineSpacingRule
{
    /// <summary>A multiple of the single line height (1.0, 1.15, 2.0 …).</summary>
    Multiple,
    /// <summary>At least <see cref="LineSpacing.Value"/> points.</summary>
    AtLeast,
    /// <summary>Exactly <see cref="LineSpacing.Value"/> points.</summary>
    Exact,
}

/// <summary>Line spacing: a multiple of the single line or a height in points.</summary>
public sealed record LineSpacing(LineSpacingRule Rule, double Value)
{
    public static readonly LineSpacing Single = new(LineSpacingRule.Multiple, 1);
}

/// <summary>Alignment of text at a tab stop.</summary>
public enum TabAlignment
{
    Left,
    Center,
    Right,
    Decimal,
    Bar,
    /// <summary>Removes an inherited tab stop at this position (w:tab w:val="clear").</summary>
    Clear,
}

/// <summary>What fills the space up to a tab stop.</summary>
public enum TabLeader
{
    None,
    Dot,
    Hyphen,
    Underscore,
    MiddleDot,
    Heavy,
}

/// <summary>A tab stop at <see cref="Position"/> points from the left indent edge of the text area (page margin).</summary>
public sealed record TabStop(double Position, TabAlignment Alignment = TabAlignment.Left, TabLeader Leader = TabLeader.None);

/// <summary>A paragraph's place in a list: numbering instance (w:numId; 0 = explicitly none) and level 0–8.</summary>
public sealed record NumberingReference(int NumberingId, int Level)
{
    /// <summary>"No list" – overrides the numbering of a style.</summary>
    public static readonly NumberingReference None = new(0, 0);

    public bool IsNone => NumberingId == 0;
}

/// <summary>
/// Paragraph formatting, immutable; null means "inherit" (style, document defaults). Lengths in points.
/// <see cref="FirstLineIndent"/> is signed: positive = first line indent, negative = hanging indent.
/// </summary>
public sealed record ParagraphProperties
{
    public static readonly ParagraphProperties Empty = new();

    /// <summary>The paragraph style (w:pStyle); null = the default paragraph style ("Standard").</summary>
    public string? StyleId { get; init; }

    public ParagraphAlignment? Alignment { get; init; }

    public double? IndentLeft { get; init; }

    public double? IndentRight { get; init; }

    public double? FirstLineIndent { get; init; }

    public double? SpacingBefore { get; init; }

    public double? SpacingAfter { get; init; }

    public LineSpacing? LineSpacing { get; init; }

    /// <summary>"Nicht vom nächsten Absatz trennen".</summary>
    public bool? KeepWithNext { get; init; }

    /// <summary>"Diesen Absatz zusammenhalten".</summary>
    public bool? KeepLinesTogether { get; init; }

    /// <summary>"Seitenumbruch oberhalb".</summary>
    public bool? PageBreakBefore { get; init; }

    /// <summary>"Absatzkontrolle" (no single first / last line of a paragraph alone on a page).</summary>
    public bool? WidowControl { get; init; }

    /// <summary>"Keinen Abstand zwischen Absätzen gleicher Formatierung einfügen".</summary>
    public bool? ContextualSpacing { get; init; }

    public NumberingReference? Numbering { get; init; }

    /// <summary>Tab stops of this level (merged with the style's: a <see cref="TabAlignment.Clear"/> removes one).</summary>
    public EquatableArray<TabStop>? Tabs { get; init; }

    /// <summary>Outline level 0–8 (headings; the table of contents of 1c), 9 = body text.</summary>
    public int? OutlineLevel { get; init; }

    /// <summary>Paragraph properties ThixWrite does not model (borders, shading, frames …), as XML for the round trip.</summary>
    public string? UnknownXml { get; init; }

    public bool IsEmpty => Equals(Empty);

    /// <summary>These properties with every non-null property of <paramref name="other"/> on top (tab stops merged).</summary>
    public ParagraphProperties Merge(ParagraphProperties? other)
    {
        if (other is null || other.IsEmpty)
        {
            return this;
        }

        if (IsEmpty)
        {
            return other;
        }

        return new ParagraphProperties
        {
            StyleId = other.StyleId ?? StyleId,
            Alignment = other.Alignment ?? Alignment,
            IndentLeft = other.IndentLeft ?? IndentLeft,
            IndentRight = other.IndentRight ?? IndentRight,
            FirstLineIndent = other.FirstLineIndent ?? FirstLineIndent,
            SpacingBefore = other.SpacingBefore ?? SpacingBefore,
            SpacingAfter = other.SpacingAfter ?? SpacingAfter,
            LineSpacing = other.LineSpacing ?? LineSpacing,
            KeepWithNext = other.KeepWithNext ?? KeepWithNext,
            KeepLinesTogether = other.KeepLinesTogether ?? KeepLinesTogether,
            PageBreakBefore = other.PageBreakBefore ?? PageBreakBefore,
            WidowControl = other.WidowControl ?? WidowControl,
            ContextualSpacing = other.ContextualSpacing ?? ContextualSpacing,
            Numbering = other.Numbering ?? Numbering,
            Tabs = MergeTabs(Tabs, other.Tabs),
            OutlineLevel = other.OutlineLevel ?? OutlineLevel,
            UnknownXml = other.UnknownXml ?? UnknownXml,
        };
    }

    private static EquatableArray<TabStop>? MergeTabs(EquatableArray<TabStop>? below, EquatableArray<TabStop>? above)
    {
        if (above is null)
        {
            return below;
        }

        if (below is null)
        {
            return above;
        }

        var merged = below.Value.Where(tab => !above.Value.Any(other => Math.Abs(other.Position - tab.Position) < 0.01)).ToList();
        merged.AddRange(above.Value);
        return new EquatableArray<TabStop>(merged.OrderBy(tab => tab.Position));
    }
}
