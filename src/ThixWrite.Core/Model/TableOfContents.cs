using System.Globalization;
using System.Text.RegularExpressions;

namespace ThixWrite.Core.Model;

/// <summary>
/// The start (with the field code, " TOC \o "1-3" \h \z \u ") or the end of a table of contents: Word's TOC field, whose result –
/// the entries – are ordinary paragraphs between the two marks. Invisible; written back as the field's characters (begin,
/// instruction, separate … end), so Word sees a real table of contents it can update itself.
/// </summary>
public sealed record TocMarkInline(bool IsEnd, string Instruction, CharacterProperties Properties, Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";

    public override bool IsMarker => true;
}

/// <summary>What ThixWrite knows about tables of contents: the field code, the levels it takes, the styles of its entries.</summary>
public static partial class TableOfContents
{
    /// <summary>Word's default: headings 1–3, entries as links, no page numbers in web view, outline levels too.</summary>
    public const string DefaultInstruction = " TOC \\o \"1-3\" \\h \\z \\u ";

    /// <summary>The heading over a new table of contents ("Inhalt", like Word's built-in "Automatisches Verzeichnis 1").</summary>
    public const string Heading = "Inhalt";

    /// <summary>What an empty table of contents says (Word's words).</summary>
    public const string NoEntries = "Keine Einträge für das Inhaltsverzeichnis gefunden.";

    /// <summary>True for a field code that is a table of contents.</summary>
    public static bool IsToc(string instruction) => instruction.TrimStart().StartsWith("TOC", StringComparison.OrdinalIgnoreCase)
        && (instruction.TrimStart().Length == 3 || !char.IsLetter(instruction.TrimStart()[3]));

    /// <summary>The deepest heading level of a field code (\o "1-3" → 3; 3 without the switch).</summary>
    public static int LevelsOf(string instruction)
    {
        var match = LevelPattern().Match(instruction);
        return match.Success && int.TryParse(match.Groups[2].Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var last) ? Math.Clamp(last, 1, 9) : 3;
    }

    /// <summary>The field code for headings 1 to <paramref name="levels"/>.</summary>
    public static string InstructionFor(int levels) => $" TOC \\o \"1-{Math.Clamp(levels, 1, 9)}\" \\h \\z \\u ";

    /// <summary>The style of an entry of a level (1-based): "Verzeichnis 1" …</summary>
    public static string EntryStyle(int level) => $"TOC{Math.Clamp(level, 1, 9)}";

    [GeneratedRegex("\\\\o\\s*\"?(\\d)\\s*-\\s*(\\d)")]
    private static partial Regex LevelPattern();
}
