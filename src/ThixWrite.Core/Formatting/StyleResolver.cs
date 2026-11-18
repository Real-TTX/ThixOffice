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
    int OutlineLevel);

/// <summary>
/// Resolves formatting like Word: document defaults, then the paragraph style and the styles it is based on, then (for lists)
/// the list level's indents, then the paragraph's direct formatting; for text additionally the character style and the run's
/// direct formatting. Caches by style id and property instance; the caches drop themselves when the document's style sheet,
/// list definitions or theme are replaced. One resolver per document; not thread-safe.
/// </summary>
public sealed class StyleResolver(Document document)
{
    private const int CacheLimit = 20_000;

    private readonly Dictionary<string, (ParagraphProperties Paragraph, CharacterProperties Character)> _paragraphStyles = new(StringComparer.Ordinal);
    private readonly Dictionary<string, CharacterProperties> _characterStyles = new(StringComparer.Ordinal);
    private readonly Dictionary<(string ParagraphStyle, CharacterProperties Run), EffectiveCharacterFormat> _runs = [];
    private readonly Dictionary<ParagraphProperties, EffectiveParagraphFormat> _paragraphs = [];
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
        if (_paragraphs.TryGetValue(paragraph.Properties, out var cached))
        {
            return cached;
        }

        var styleId = ParagraphStyleId(paragraph);
        var styleProperties = StyleChain(styleId).Paragraph;
        var merged = Document.Styles.ParagraphDefaults.Merge(styleProperties);

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
            merged.OutlineLevel ?? 9);
        Remember(_paragraphs, paragraph.Properties, result);
        return result;
    }

    /// <summary>The resolved formatting of text with <paramref name="run"/> properties in <paramref name="paragraph"/>.</summary>
    public EffectiveCharacterFormat ResolveCharacter(Paragraph paragraph, CharacterProperties run) =>
        ResolveCharacter(ParagraphStyleId(paragraph), run);

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
        var merged = Document.Styles.CharacterDefaults.Merge(StyleChain(paragraphStyleId).Character);
        if (characterStyleId is not null)
        {
            merged = merged.Merge(CharacterStyle(characterStyleId));
        }

        return merged;
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
        properties.FontTheme is { } slot ? Document.Theme.FontOf(slot) : properties.FontFamily ?? BuiltInStyles.BodyFont;

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
