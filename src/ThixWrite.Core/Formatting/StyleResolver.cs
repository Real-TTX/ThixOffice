using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formatting;

/// <summary>The formatting a run of text really has, every value resolved (styles, defaults, theme).</summary>
public sealed record EffectiveCharacterFormat(
    string FontFamily,
    double FontSize,
    bool Bold,
    bool Italic,
    UnderlineStyle Underline,
    bool Strikethrough,
    bool DoubleStrikethrough,
    VerticalPosition VerticalPosition,
    string Color,
    HighlightColor Highlight,
    string? CharacterStyleId)
{
    /// <summary>The colour as "RRGGBB"; automatic colour is black.</summary>
    public bool IsAutoColor => string.Equals(Color, "auto", StringComparison.OrdinalIgnoreCase);
}

/// <summary>The formatting a paragraph really has, every value resolved.</summary>
public sealed record EffectiveParagraphFormat(
    string StyleId,
    ParagraphAlignment Alignment,
    double IndentLeft,
    double IndentRight,
    double FirstLineIndent,
    double SpacingBefore,
    double SpacingAfter,
    LineSpacing LineSpacing,
    bool KeepWithNext,
    bool KeepLinesTogether,
    bool PageBreakBefore,
    bool WidowControl,
    bool ContextualSpacing,
    NumberingReference? Numbering,
    EquatableArray<TabStop> Tabs,
    int OutlineLevel)
{
    /// <summary>
    /// The key the text of this paragraph is resolved under (<see cref="StyleResolver.ResolveCharacter(string, CharacterProperties)"/>):
    /// the paragraph style, and in a table cell also the table style.
    /// </summary>
    public string StyleKey { get; init; } = StyleId;
}

/// <summary>
/// Resolves formatting like Word: document defaults, then (in a table cell) the table style, then the paragraph style and the
/// styles it is based on, then (for lists)
/// the list level's indents, then the paragraph's direct formatting; for text additionally the character style and the run's
/// direct formatting. Caches by style id and property instance; the caches drop themselves when the document's style sheet,
/// list definitions or theme are replaced. One resolver per document; not thread-safe.
/// </summary>
public sealed class StyleResolver(Document document)
{
    /// <summary>Word's font when neither a style nor the document defaults name one (with 10 pt).</summary>
    public const string ImplicitFont = "Times New Roman";

    private const int CacheLimit = 20_000;
    private const char TableKeySeparator = '\u0001';

    private const char ConditionSeparator = '\u0002';

    private readonly Dictionary<string, (ParagraphProperties Paragraph, CharacterProperties Character)> _paragraphStyles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CharacterProperties> _characterStyles = new(StringComparer.Ordinal);
    private readonly Dictionary<(string ParagraphStyle, CharacterProperties Run), EffectiveCharacterFormat> _runs = [];
    private readonly Dictionary<(ParagraphProperties Properties, string? TableStyle), EffectiveParagraphFormat> _paragraphs = [];
    private System.Runtime.CompilerServices.ConditionalWeakTable<Table, Dictionary<int, int>> _masks = new();

    private StyleSheet? _styles;
    private NumberingDefinitions? _numbering;
    private Theme? _theme;

    public Document Document { get; } = document;

    /// <summary>The id of the paragraph style of <paramref name="paragraph"/> (the default style when it names none or an unknown one).</summary>
    public string ParagraphStyleId(Paragraph paragraph)
    {
        Refresh();
        var id = paragraph.Properties.StyleId;
        if (id is not null && Document.Styles.Find(id) is { Type: StyleType.Paragraph })
        {
            return id;
        }

        return Document.Styles.DefaultParagraphStyle?.Id ?? "";
    }

    /// <summary>The resolved formatting of <paramref name="paragraph"/>.</summary>
    public EffectiveParagraphFormat ResolveParagraph(Paragraph paragraph)
    {
        Refresh();
        var tableStyle = TableKey(paragraph);
        if (_paragraphs.TryGetValue((paragraph.Properties, tableStyle), out var cached))
        {
            return cached;
        }

        var styleId = ParagraphStyleId(paragraph);
        var styleProperties = StyleChain(styleId).Paragraph;
        var merged = Document.Styles.ParagraphDefaults;
        if (tableStyle is not null)
        {
            var (tableStyleId, mask) = SplitTableKey(tableStyle);
            merged = merged.Merge(StyleChain(tableStyleId).Paragraph);
            foreach (var condition in ConditionsOf(tableStyleId, mask))
            {
                merged = merged.Merge(condition.Paragraph);
            }
        }

        merged = merged.Merge(styleProperties);

        var numbering = paragraph.Properties.Numbering ?? merged.Numbering;
        if (numbering is { IsNone: false } && Document.Numbering.ResolveLevel(numbering.NumberingId, numbering.Level) is { } level)
        {
            merged = merged.Merge(new ParagraphProperties { IndentLeft = level.IndentLeft, FirstLineIndent = level.FirstLineIndent });
        }
        else
        {
            numbering = null;
        }

        merged = merged.Merge(paragraph.Properties);
        var tabs = merged.Tabs is { } list
            ? new EquatableArray<TabStop>(list.Where(tab => tab.Alignment != TabAlignment.Clear).OrderBy(tab => tab.Position))
            : EquatableArray<TabStop>.Empty;

        var result = new EffectiveParagraphFormat(
            styleId,
            merged.Alignment ?? ParagraphAlignment.Left,
            merged.IndentLeft ?? 0,
            merged.IndentRight ?? 0,
            merged.FirstLineIndent ?? 0,
            merged.SpacingBefore ?? 0,
            merged.SpacingAfter ?? 0,
            merged.LineSpacing ?? LineSpacing.Single,
            merged.KeepWithNext ?? false,
            merged.KeepLinesTogether ?? false,
            merged.PageBreakBefore ?? false,
            merged.WidowControl ?? false,
            merged.ContextualSpacing ?? false,
            numbering,
            tabs,
            merged.OutlineLevel ?? 9)
        {
            StyleKey = tableStyle is null ? styleId : styleId + TableKeySeparator + tableStyle,
        };
        Remember(_paragraphs, (paragraph.Properties, tableStyle), result);
        return result;
    }

    /// <summary>The resolved formatting of text with <paramref name="run"/> properties in <paramref name="paragraph"/>.</summary>
    public EffectiveCharacterFormat ResolveCharacter(Paragraph paragraph, CharacterProperties run) =>
        ResolveCharacter(StyleKeyOf(paragraph), run);

    /// <summary>The key of <paramref name="paragraph"/>'s text: its style, in a table cell with the table style.</summary>
    public string StyleKeyOf(Paragraph paragraph) =>
        TableKey(paragraph) is { } table ? ParagraphStyleId(paragraph) + TableKeySeparator + table : ParagraphStyleId(paragraph);

    /// <summary>The table style a cell paragraph gets its defaults from (the table's or the default table style), null outside tables.</summary>
    public string? TableStyleId(Paragraph paragraph)
    {
        if (paragraph.Cell is not { } cell)
        {
            return null;
        }

        var id = cell.Table.Properties.StyleId;
        return id is not null && Document.Styles.Find(id) is { Type: StyleType.Table } ? id : Document.Styles.DefaultTableStyle?.Id;
    }

    /// <summary>
    /// The effective table properties of <paramref name="table"/>: the default table style, the table's style chain, then its
    /// direct properties; cell margins fall back to Word's 5.4 pt.
    /// </summary>
    public TableProperties ResolveTable(Table table)
    {
        Refresh();
        var result = new TableProperties { CellMargins = CellMargins.WordDefault, Borders = BorderSet.Empty };
        var styleId = table.Properties.StyleId is { } id && Document.Styles.Find(id) is { Type: StyleType.Table } ? id : Document.Styles.DefaultTableStyle?.Id;
        foreach (var style in Document.Styles.Chain(styleId).Reverse())
        {
            result = result.Merge(style.Table);
        }

        return result.Merge(table.Properties with { StyleId = styleId });
    }

    /// <summary>The resolved formatting of text with <paramref name="run"/> properties in a paragraph of style <paramref name="paragraphStyleId"/>.</summary>
    public EffectiveCharacterFormat ResolveCharacter(string paragraphStyleId, CharacterProperties run)
    {
        Refresh();
        var key = (paragraphStyleId, run);
        if (_runs.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var merged = InheritedCharacter(paragraphStyleId, run.StyleId).Merge(run);
        var result = ToEffective(merged);
        Remember(_runs, key, result);
        return result;
    }

    /// <summary>
    /// The character properties text would have without <paramref name="direct"/>: defaults, paragraph style, character style.
    /// Used to keep direct formatting minimal (a "bold" that the style already gives is not written again).
    /// </summary>
    public CharacterProperties InheritedCharacter(string paragraphStyleId, string? characterStyleId)
    {
        Refresh();
        var separator = paragraphStyleId.IndexOf(TableKeySeparator);
        var merged = Document.Styles.CharacterDefaults;
        if (separator >= 0)
        {
            var (tableStyleId, mask) = SplitTableKey(paragraphStyleId[(separator + 1)..]);
            merged = merged.Merge(StyleChain(tableStyleId).Character);
            foreach (var condition in ConditionsOf(tableStyleId, mask))
            {
                merged = merged.Merge(condition.Character);
            }

            paragraphStyleId = paragraphStyleId[..separator];
        }

        merged = merged.Merge(StyleChain(paragraphStyleId).Character);
        if (characterStyleId is not null)
        {
            merged = merged.Merge(CharacterStyle(characterStyleId));
        }

        return merged;
    }

    /// <summary>The table style and the conditional formats that apply to a cell paragraph: "style" or "style + mask"; null outside tables.</summary>
    private string? TableKey(Paragraph paragraph)
    {
        if (TableStyleId(paragraph) is not { } style || paragraph.Cell is not { } cell)
        {
            return null;
        }

        var mask = ConditionMask(cell.Table, cell.Row, cell.Cell);
        return mask == 0 ? style : style + ConditionSeparator + mask.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private static (string Style, int Mask) SplitTableKey(string key)
    {
        var separator = key.IndexOf(ConditionSeparator);
        return separator < 0 ? (key, 0) : (key[..separator], int.Parse(key[(separator + 1)..], System.Globalization.CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// The conditional formats of a table style that format cell <paramref name="cell"/> of row <paramref name="row"/>, as a bit mask
    /// of <see cref="TableConditionKind"/>: the whole table, row and column bands (not in a heading or total row/column), first/last
    /// row and column as the table's "Tabellenformatoptionen" (w:tblLook) switch them on, and the corner cells. 0 when the table's
    /// style has no conditional formats.
    /// </summary>
    public int ConditionMask(Table table, int row, int cell)
    {
        Refresh();
        var masks = _masks.GetOrCreateValue(table);
        var key = (row << 16) | cell;
        if (masks.TryGetValue(key, out var known))
        {
            return known;
        }

        var computed = ComputeConditionMask(table, row, cell);
        masks[key] = computed;
        return computed;
    }

    private int ComputeConditionMask(Table table, int row, int cell)
    {
        var properties = ResolveTable(table);
        if (properties.StyleId is not { } styleId || Document.Styles.Chain(styleId).All(link => link.Conditions.Count == 0))
        {
            return 0;
        }

        var look = properties.Look ?? TableLook.Default;
        var columns = Math.Max(1, table.ColumnCount);
        var firstColumn = table.GridColumnOf(row, cell);
        var lastColumn = firstColumn + Math.Max(1, table.CellAt(row, cell).GridSpan) - 1;
        var isFirstRow = look.FirstRow && row == 0;
        var isLastRow = look.LastRow && row == table.RowCount - 1;
        var isFirstColumn = look.FirstColumn && firstColumn == 0;
        var isLastColumn = look.LastColumn && lastColumn >= columns - 1;
        var mask = 1 << (int)TableConditionKind.WholeTable;
        if (look.BandedColumns && !isFirstColumn && !isLastColumn)
        {
            var band = (firstColumn - (look.FirstColumn ? 1 : 0)) / Math.Max(1, properties.ColumnBandSize ?? 1);
            mask |= 1 << (int)(band % 2 == 0 ? TableConditionKind.Band1Vertical : TableConditionKind.Band2Vertical);
        }

        if (look.BandedRows && !isFirstRow && !isLastRow)
        {
            var band = (row - (look.FirstRow ? 1 : 0)) / Math.Max(1, properties.RowBandSize ?? 1);
            mask |= 1 << (int)(band % 2 == 0 ? TableConditionKind.Band1Horizontal : TableConditionKind.Band2Horizontal);
        }

        void Set(bool on, TableConditionKind kind)
        {
            if (on)
            {
                mask |= 1 << (int)kind;
            }
        }

        Set(isFirstColumn, TableConditionKind.FirstColumn);
        Set(isLastColumn, TableConditionKind.LastColumn);
        Set(isFirstRow, TableConditionKind.FirstRow);
        Set(isLastRow, TableConditionKind.LastRow);
        Set(isFirstRow && isLastColumn, TableConditionKind.TopRightCell);
        Set(isFirstRow && isFirstColumn, TableConditionKind.TopLeftCell);
        Set(isLastRow && isLastColumn, TableConditionKind.BottomRightCell);
        Set(isLastRow && isFirstColumn, TableConditionKind.BottomLeftCell);
        return mask;
    }

    /// <summary>The conditional formats of <paramref name="styleId"/>'s chain that <paramref name="mask"/> selects, in Word's order (the base style's before the style's own).</summary>
    public IReadOnlyList<TableStyleCondition> ConditionsOf(string? styleId, int mask)
    {
        if (mask == 0)
        {
            return [];
        }

        var chain = Document.Styles.Chain(styleId).Reverse().ToList();
        var result = new List<TableStyleCondition>();
        foreach (var kind in Enum.GetValues<TableConditionKind>())
        {
            if ((mask & (1 << (int)kind)) == 0)
            {
                continue;
            }

            foreach (var style in chain)
            {
                result.AddRange(style.Conditions.Where(condition => condition.Kind == kind));
            }
        }

        return result;
    }

    /// <summary>The list level a paragraph shows (direct or from its style), or null when it is not in a list.</summary>
    public (NumberingReference Reference, NumberingLevel Level)? ListLevel(Paragraph paragraph)
    {
        var format = ResolveParagraph(paragraph);
        if (format.Numbering is not { } reference)
        {
            return null;
        }

        var level = Document.Numbering.ResolveLevel(reference.NumberingId, reference.Level);
        return level is null ? null : (reference, level);
    }

    /// <summary>The resolved family name of a font choice (theme fonts through the document's theme).</summary>
    public string FamilyOf(CharacterProperties properties) =>
        properties.FontTheme is { } slot ? Document.Theme.FontOf(slot) : properties.FontFamily ?? ImplicitFont;

    private EffectiveCharacterFormat ToEffective(CharacterProperties merged)
    {
        // Word writes the computed colour next to a theme colour; only an "auto" value falls back to the theme.
        var color = merged.Color switch
        {
            null => "auto",
            { IsAuto: true, ThemeColor: { } theme } when Document.Theme.ColorOf(theme) is { } themed => themed,
            _ => merged.Color.Value,
        };
        return new EffectiveCharacterFormat(
            FamilyOf(merged),
            merged.FontSize ?? 10,
            merged.Bold ?? false,
            merged.Italic ?? false,
            merged.Underline ?? UnderlineStyle.None,
            merged.Strikethrough ?? false,
            merged.DoubleStrikethrough ?? false,
            merged.VerticalPosition ?? VerticalPosition.Baseline,
            color,
            merged.Highlight ?? HighlightColor.None,
            merged.StyleId);
    }

    private (ParagraphProperties Paragraph, CharacterProperties Character) StyleChain(string styleId)
    {
        if (_paragraphStyles.TryGetValue(styleId, out var cached))
        {
            return cached;
        }

        var paragraph = ParagraphProperties.Empty;
        var character = CharacterProperties.Empty;
        foreach (var style in Document.Styles.Chain(styleId).Reverse())
        {
            paragraph = paragraph.Merge(style.Paragraph with { StyleId = null });
            character = character.Merge(style.Character with { StyleId = null });
        }

        var result = (paragraph, character);
        _paragraphStyles[styleId] = result;
        return result;
    }

    private CharacterProperties CharacterStyle(string styleId)
    {
        if (_characterStyles.TryGetValue(styleId, out var cached))
        {
            return cached;
        }

        var character = CharacterProperties.Empty;
        foreach (var style in Document.Styles.Chain(styleId).Reverse())
        {
            character = character.Merge(style.Character with { StyleId = null });
        }

        _characterStyles[styleId] = character;
        return character;
    }

    private void Refresh()
    {
        if (ReferenceEquals(_styles, Document.Styles) && ReferenceEquals(_numbering, Document.Numbering) && ReferenceEquals(_theme, Document.Theme))
        {
            return;
        }

        _styles = Document.Styles;
        _numbering = Document.Numbering;
        _theme = Document.Theme;
        _paragraphStyles.Clear();
        _characterStyles.Clear();
        _runs.Clear();
        _paragraphs.Clear();
        _masks = new();
    }

    private static void Remember<TKey, TValue>(Dictionary<TKey, TValue> cache, TKey key, TValue value)
        where TKey : notnull
    {
        if (cache.Count >= CacheLimit)
        {
            cache.Clear();
        }

        cache[key] = value;
    }
}
