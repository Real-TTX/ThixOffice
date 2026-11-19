using System.Collections.Immutable;

namespace ThixWrite.Core.Model;

/// <summary>What a style formats.</summary>
public enum StyleType
{
    Paragraph,
    Character,
    Table,
    Numbering,
}

/// <summary>
/// A named style ("Formatvorlage"). <see cref="Name"/> is the name in the file – Word stores built-in styles under their English
/// canonical names ("heading 1") whatever the UI language; <see cref="BuiltInStyles.DisplayName(Style)"/> gives the German name.
/// A paragraph style carries paragraph and character properties, a character style character properties only.
/// </summary>
public sealed record Style
{
    public required string Id { get; init; }

    public required string Name { get; init; }

    public StyleType Type { get; init; } = StyleType.Paragraph;

    public string? BasedOn { get; init; }

    /// <summary>The style of the paragraph after Enter.</summary>
    public string? Next { get; init; }

    /// <summary>The linked character / paragraph style (Word's "verknüpfte Formatvorlage").</summary>
    public string? Link { get; init; }

    /// <summary>The default style of its type ("Standard", "Absatz-Standardschriftart").</summary>
    public bool IsDefault { get; init; }

    /// <summary>A style the user (not the application) made.</summary>
    public bool IsCustom { get; init; }

    /// <summary>Shown in the style gallery of the ribbon.</summary>
    public bool QuickFormat { get; init; }

    public int? UiPriority { get; init; }

    public bool SemiHidden { get; init; }

    public bool UnhideWhenUsed { get; init; }

    public ParagraphProperties Paragraph { get; init; } = ParagraphProperties.Empty;

    public CharacterProperties Character { get; init; } = CharacterProperties.Empty;

    /// <summary>The table properties of a table style (borders, cell margins …; w:tblPr), or null.</summary>
    public TableProperties? Table { get; init; }

    /// <summary>The conditional formats of a table style (Überschrift, Bänder …), read from its w:tblStylePr (which stay in <see cref="UnknownXml"/>).</summary>
    public EquatableArray<TableStyleCondition> Conditions { get; init; } = EquatableArray<TableStyleCondition>.Empty;

    /// <summary>Style children ThixWrite does not model (conditional table formats w:tblStylePr, w:trPr, w:tcPr, w:rsid …), as XML.</summary>
    public string? UnknownXml { get; init; }
}

/// <summary>
/// The styles of a document, immutable: the document defaults (w:docDefaults), the styles by id in file order and the latent
/// style list (kept as XML). Changes return a new sheet (an undoable step swaps the whole sheet).
/// </summary>
public sealed class StyleSheet
{
    private readonly ImmutableDictionary<string, Style> _byId;

    public StyleSheet(
        IEnumerable<Style> styles,
        ParagraphProperties? paragraphDefaults = null,
        CharacterProperties? characterDefaults = null,
        string? latentStylesXml = null)
    {
        var list = styles.ToImmutableArray();
        Styles = list;
        _byId = list.GroupBy(style => style.Id, StringComparer.Ordinal)
            .ToImmutableDictionary(group => group.Key, group => group.Last(), StringComparer.Ordinal);
        ParagraphDefaults = paragraphDefaults ?? ParagraphProperties.Empty;
        CharacterDefaults = characterDefaults ?? CharacterProperties.Empty;
        LatentStylesXml = latentStylesXml;
    }

    public static StyleSheet Empty { get; } = new([]);

    public ImmutableArray<Style> Styles { get; }

    public ParagraphProperties ParagraphDefaults { get; }

    public CharacterProperties CharacterDefaults { get; }

    public string? LatentStylesXml { get; }

    /// <summary>The default paragraph style ("Standard").</summary>
    public Style? DefaultParagraphStyle => Styles.FirstOrDefault(style => style.Type == StyleType.Paragraph && style.IsDefault);

    /// <summary>The default table style ("Normale Tabelle").</summary>
    public Style? DefaultTableStyle => Styles.FirstOrDefault(style => style.Type == StyleType.Table && style.IsDefault);

    /// <summary>The default character style ("Absatz-Standardschriftart").</summary>
    public Style? DefaultCharacterStyle => Styles.FirstOrDefault(style => style.Type == StyleType.Character && style.IsDefault);

    public Style? Find(string? id) => id is not null && _byId.TryGetValue(id, out var style) ? style : null;

    /// <summary>The style named <paramref name="name"/> (file name, case-insensitive) or with that German display name.</summary>
    public Style? FindByName(string name) =>
        Styles.FirstOrDefault(style => string.Equals(style.Name, name, StringComparison.OrdinalIgnoreCase))
        ?? Styles.FirstOrDefault(style => string.Equals(BuiltInStyles.DisplayName(style), name, StringComparison.OrdinalIgnoreCase));

    /// <summary>The sheet with <paramref name="style"/> added or replacing the style of the same id.</summary>
    public StyleSheet With(Style style)
    {
        var styles = Styles.Any(existing => existing.Id == style.Id)
            ? Styles.Select(existing => existing.Id == style.Id ? style : existing)
            : Styles.Append(style);
        return new StyleSheet(styles, ParagraphDefaults, CharacterDefaults, LatentStylesXml);
    }

    public StyleSheet WithDefaults(ParagraphProperties paragraphDefaults, CharacterProperties characterDefaults) =>
        new(Styles, paragraphDefaults, characterDefaults, LatentStylesXml);

    /// <summary>The chain of a style and the styles it is based on, the style itself first (cycles are cut).</summary>
    public IReadOnlyList<Style> Chain(string? id)
    {
        var chain = new List<Style>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var current = Find(id);
        while (current is not null && seen.Add(current.Id))
        {
            chain.Add(current);
            current = Find(current.BasedOn);
        }

        return chain;
    }

    public bool ContentEquals(StyleSheet other) =>
        ParagraphDefaults == other.ParagraphDefaults
        && CharacterDefaults == other.CharacterDefaults
        && LatentStylesXml == other.LatentStylesXml
        && Styles.SequenceEqual(other.Styles);
}
