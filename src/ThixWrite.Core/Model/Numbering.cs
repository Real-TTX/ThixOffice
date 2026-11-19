using System.Collections.Immutable;

namespace ThixWrite.Core.Model;

/// <summary>How a list level counts (w:numFmt).</summary>
public enum NumberFormat
{
    None,
    Bullet,
    Decimal,
    DecimalZero,
    LowerLetter,
    UpperLetter,
    LowerRoman,
    UpperRoman,
    Ordinal,
    /// <summary>A format ThixWrite does not count in (shown as decimal); <see cref="NumberingLevel.FormatName"/> keeps it.</summary>
    Other,
}

/// <summary>What follows the label of a list item.</summary>
public enum LabelSuffix
{
    Tab,
    Space,
    Nothing,
}

/// <summary>
/// One level of a list definition (w:lvl): format, start, the label pattern ("%1.%2." – %n is level n's number; a bullet's
/// text is the bullet), alignment of the label, the indents the paragraph gets, the label's font.
/// </summary>
public sealed record NumberingLevel
{
    public int Level { get; init; }

    public int Start { get; init; } = 1;

    public NumberFormat Format { get; init; } = NumberFormat.Decimal;

    /// <summary>The w:numFmt value as in the file (for <see cref="NumberFormat.Other"/>).</summary>
    public string? FormatName { get; init; }

    public string Text { get; init; } = "%1.";

    public ParagraphAlignment Alignment { get; init; } = ParagraphAlignment.Left;

    public double IndentLeft { get; init; }

    /// <summary>Signed like <see cref="ParagraphProperties.FirstLineIndent"/>: lists use a negative value (hanging).</summary>
    public double FirstLineIndent { get; init; }

    public LabelSuffix Suffix { get; init; } = LabelSuffix.Tab;

    /// <summary>The level whose new item restarts this one (w:lvlRestart); null = the next higher level (default).</summary>
    public int? RestartAfterLevel { get; init; }

    /// <summary>Show all numbers of the label as decimal ("Rechtliche Formatvorlage").</summary>
    public bool IsLegal { get; init; }

    /// <summary>The paragraph style tied to this level (w:pStyle).</summary>
    public string? ParagraphStyleId { get; init; }

    /// <summary>The formatting of the label (bullet font, size …).</summary>
    public CharacterProperties LabelProperties { get; init; } = CharacterProperties.Empty;

    /// <summary>Level children ThixWrite does not model (w:lvlPicBulletId, w:legacy …), as XML.</summary>
    public string? UnknownXml { get; init; }

    /// <summary>The attributes of w:lvl besides w:ilvl (w:tplc, w:tentative), as "name=value" pairs, kept for the round trip.</summary>
    public string? UnknownAttributes { get; init; }
}

/// <summary>A list definition (w:abstractNum) with its levels 0–8.</summary>
public sealed record AbstractNumbering
{
    public required int Id { get; init; }

    public EquatableArray<NumberingLevel> Levels { get; init; } = EquatableArray<NumberingLevel>.Empty;

    /// <summary>singleLevel / multilevel / hybridMultilevel.</summary>
    public string? MultiLevelType { get; init; }

    /// <summary>Children ThixWrite does not model (w:nsid, w:tmpl, w:name, w:styleLink …), as XML.</summary>
    public string? UnknownXml { get; init; }

    public NumberingLevel? LevelAt(int level) => Levels.FirstOrDefault(item => item.Level == level);
}

/// <summary>An override of one level in a list instance: a new start (restart) or a whole level.</summary>
public sealed record LevelOverride(int Level, int? StartOverride, NumberingLevel? Replacement = null);

/// <summary>A list instance (w:num) that paragraphs refer to: a definition plus overrides.</summary>
public sealed record NumberingInstance
{
    public required int Id { get; init; }

    public required int AbstractId { get; init; }

    public EquatableArray<LevelOverride> Overrides { get; init; } = EquatableArray<LevelOverride>.Empty;

    public LevelOverride? OverrideAt(int level) => Overrides.FirstOrDefault(item => item.Level == level);
}

/// <summary>All list definitions and instances of a document, immutable (an undoable step swaps the whole set).</summary>
public sealed class NumberingDefinitions
{
    public NumberingDefinitions(
        IEnumerable<AbstractNumbering> definitions,
        IEnumerable<NumberingInstance> instances,
        string? unknownXml = null)
    {
        Definitions = [.. definitions];
        Instances = [.. instances];
        UnknownXml = unknownXml;
    }

    public static NumberingDefinitions Empty { get; } = new([], []);

    public ImmutableArray<AbstractNumbering> Definitions { get; }

    public ImmutableArray<NumberingInstance> Instances { get; }

    /// <summary>Picture bullets (w:numPicBullet) and other children ThixWrite does not model, as XML.</summary>
    public string? UnknownXml { get; }

    public bool IsEmpty => Definitions.IsEmpty && Instances.IsEmpty;

    public NumberingInstance? Instance(int id) => Instances.FirstOrDefault(instance => instance.Id == id);

    public AbstractNumbering? Definition(int id) => Definitions.FirstOrDefault(definition => definition.Id == id);

    /// <summary>The effective level of list instance <paramref name="numberingId"/> (overrides applied), or null.</summary>
    public NumberingLevel? ResolveLevel(int numberingId, int level)
    {
        var instance = Instance(numberingId);
        if (instance is null)
        {
            return null;
        }

        var replacement = instance.OverrideAt(level)?.Replacement;
        if (replacement is not null)
        {
            return replacement;
        }

        return Definition(instance.AbstractId)?.LevelAt(level);
    }

    public int NextDefinitionId() => Definitions.IsEmpty ? 0 : Definitions.Max(definition => definition.Id) + 1;

    public int NextInstanceId() => Instances.IsEmpty ? 1 : Instances.Max(instance => instance.Id) + 1;

    public NumberingDefinitions WithDefinition(AbstractNumbering definition) =>
        new(Definitions.Where(existing => existing.Id != definition.Id).Append(definition), Instances, UnknownXml);

    public NumberingDefinitions WithInstance(NumberingInstance instance) =>
        new(Definitions, Instances.Where(existing => existing.Id != instance.Id).Append(instance), UnknownXml);

    public bool ContentEquals(NumberingDefinitions other) =>
        UnknownXml == other.UnknownXml && Definitions.SequenceEqual(other.Definitions) && Instances.SequenceEqual(other.Instances);
}
