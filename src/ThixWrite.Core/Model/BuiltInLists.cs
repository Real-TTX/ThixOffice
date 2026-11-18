namespace ThixWrite.Core.Model;

/// <summary>The kinds of list the ribbon switches on.</summary>
public enum ListKind
{
    /// <summary>"Aufzählungszeichen": •, o, ▪ repeating over the levels.</summary>
    Bullet,
    /// <summary>"Nummerierung": 1., a., i. repeating over the levels.</summary>
    Numbered,
}

/// <summary>Word's default list definitions (multi-level, 9 levels, 36 pt per level, labels hanging 18 pt).</summary>
public static class BuiltInLists
{
    public const double LevelIndent = 36;
    public const double Hanging = 18;

    /// <summary>A list definition of <paramref name="kind"/> with id <paramref name="id"/>.</summary>
    public static AbstractNumbering Create(ListKind kind, int id) => new()
    {
        Id = id,
        MultiLevelType = "hybridMultilevel",
        Levels = new EquatableArray<NumberingLevel>(Enumerable.Range(0, 9).Select(level => kind == ListKind.Bullet ? BulletLevel(level) : NumberLevel(level))),
    };

    /// <summary>The kind of list a definition is (bullets when its first level is a bullet).</summary>
    public static ListKind KindOf(AbstractNumbering definition) =>
        definition.LevelAt(0)?.Format == NumberFormat.Bullet ? ListKind.Bullet : ListKind.Numbered;

    private static NumberingLevel BulletLevel(int level)
    {
        var (text, font) = (level % 3) switch
        {
            0 => ("", "Symbol"),
            1 => ("o", "Courier New"),
            _ => ("", "Wingdings"),
        };
        return new NumberingLevel
        {
            Level = level,
            Start = 1,
            Format = NumberFormat.Bullet,
            Text = text,
            IndentLeft = LevelIndent * (level + 1),
            FirstLineIndent = -Hanging,
            LabelProperties = new CharacterProperties { FontFamily = font },
        };
    }

    private static NumberingLevel NumberLevel(int level)
    {
        var format = (level % 3) switch
        {
            0 => NumberFormat.Decimal,
            1 => NumberFormat.LowerLetter,
            _ => NumberFormat.LowerRoman,
        };
        return new NumberingLevel
        {
            Level = level,
            Start = 1,
            Format = format,
            Text = $"%{level + 1}.",
            Alignment = format == NumberFormat.LowerRoman ? ParagraphAlignment.Right : ParagraphAlignment.Left,
            IndentLeft = LevelIndent * (level + 1),
            FirstLineIndent = format == NumberFormat.LowerRoman ? -9 : -Hanging,
        };
    }
}
