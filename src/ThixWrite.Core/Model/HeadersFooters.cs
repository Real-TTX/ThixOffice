using System.Globalization;

namespace ThixWrite.Core.Model;

/// <summary>Which header or footer of a section a page shows (Word's w:type of w:headerReference).</summary>
public enum HeaderFooterKind
{
    /// <summary>Every page (the odd pages with "Gerade & ungerade Seiten unterschiedlich").</summary>
    Default,
    /// <summary>The first page of the section ("Erste Seite anders").</summary>
    First,
    /// <summary>The even pages ("Gerade & ungerade Seiten unterschiedlich").</summary>
    Even,
}

/// <summary>
/// The text of a header or footer: blocks like the body's (paragraphs; tables and pictures render as there), immutable.
/// <see cref="SourcePart"/> is the relationship id of the part it was read from (written back into the same part).
/// Equal by content.
/// </summary>
public sealed class Story : IEquatable<Story>
{
    public Story(IEnumerable<Block> blocks, string? sourcePart = null)
    {
        var list = blocks.ToList();
        if (list.Count == 0 || list[^1] is not Paragraph { Cell: null })
        {
            list.Add(Paragraph.Empty());
        }

        Blocks = list;
        SourcePart = sourcePart;
    }

    public IReadOnlyList<Block> Blocks { get; }

    public string? SourcePart { get; }

    /// <summary>True when the story has no text (one empty paragraph).</summary>
    public bool IsEmpty => Blocks.All(block => block is Paragraph { Length: 0 });

    /// <summary>True when a field shows the page number or the number of pages (the story looks different on every page).</summary>
    public bool HasPageFields => Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<FieldInline>().Any(inline => inline.Kind is FieldKind.Page or FieldKind.NumPages);

    public Story WithBlocks(IEnumerable<Block> blocks) => new(blocks, SourcePart);

    public bool Equals(Story? other) =>
        other is not null && Blocks.Count == other.Blocks.Count && Blocks.Zip(other.Blocks).All(pair => pair.First.ContentEquals(pair.Second));

    public override bool Equals(object? obj) => obj is Story other && Equals(other);

    public override int GetHashCode() => Blocks.Count;

    public override string ToString() => string.Join(" | ", Blocks);
}

/// <summary>The headers (or footers) of a section by kind; null kinds come from the section before (Word's "Mit vorheriger verknüpfen").</summary>
public sealed record HeaderFooterSet(Story? Default = null, Story? First = null, Story? Even = null)
{
    public static readonly HeaderFooterSet Empty = new();

    public bool IsEmpty => Default is null && First is null && Even is null;

    public Story? this[HeaderFooterKind kind] => kind switch
    {
        HeaderFooterKind.First => First,
        HeaderFooterKind.Even => Even,
        _ => Default,
    };

    public HeaderFooterSet With(HeaderFooterKind kind, Story? story) => kind switch
    {
        HeaderFooterKind.First => this with { First = story },
        HeaderFooterKind.Even => this with { Even = story },
        _ => this with { Default = story },
    };
}

/// <summary>What a field shows.</summary>
public enum FieldKind
{
    /// <summary>PAGE: the number of the page it is on.</summary>
    Page,
    /// <summary>NUMPAGES: the number of pages.</summary>
    NumPages,
    /// <summary>DATE: today, in the format of its \@ switch.</summary>
    Date,
}

/// <summary>
/// A field ThixWrite updates itself (PAGE, NUMPAGES, DATE): one object character in the text; the layout shows its value where it
/// is (the page number of each page in a header). <see cref="Instruction"/> is the field code as written (" PAGE \* MERGEFORMAT ");
/// <see cref="Result"/> the last result (shown in the body, written into the file).
/// </summary>
public sealed record FieldInline(FieldKind Kind, string Instruction, string Result, CharacterProperties Properties, Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";

    public static FieldInline Page(CharacterProperties properties) => new(FieldKind.Page, " PAGE ", "1", properties);

    public static FieldInline NumPages(CharacterProperties properties) => new(FieldKind.NumPages, " NUMPAGES ", "1", properties);

    public static FieldInline Date(CharacterProperties properties, DateTime today) =>
        new(FieldKind.Date, " DATE \\@ \"dd.MM.yyyy\" ", today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture), properties);

    /// <summary>The kind of an instruction ThixWrite updates itself, or null ("PAGE \* Arabic" → Page; "HYPERLINK …" → null).</summary>
    public static FieldKind? KindOf(string instruction)
    {
        var name = instruction.Trim().Split(' ', 2)[0].ToUpperInvariant();
        return name switch
        {
            "PAGE" => FieldKind.Page,
            "NUMPAGES" => FieldKind.NumPages,
            "DATE" => FieldKind.Date,
            _ => null,
        };
    }

    /// <summary>The text of the field for page <paramref name="page"/> of <paramref name="pages"/> (1-based) on <paramref name="today"/>.</summary>
    public string Text(int page, int pages, DateTime today) => Kind switch
    {
        FieldKind.Page => page.ToString(CultureInfo.InvariantCulture),
        FieldKind.NumPages => pages.ToString(CultureInfo.InvariantCulture),
        _ => FormatDate(today),
    };

    /// <summary>The date in the field's \@ format (Word's pattern letters as .NET's: d, M, y, H, m); dd.MM.yyyy without one.</summary>
    private string FormatDate(DateTime today)
    {
        var start = Instruction.IndexOf("\\@", StringComparison.Ordinal);
        var pattern = "dd.MM.yyyy";
        if (start >= 0)
        {
            var rest = Instruction[(start + 2)..].Trim();
            pattern = rest.StartsWith('"') && rest.IndexOf('"', 1) is > 0 and var end ? rest[1..end] : rest.Split(' ')[0];
        }

        try
        {
            return today.ToString(pattern.Replace("am/pm", "tt", StringComparison.OrdinalIgnoreCase), CultureInfo.GetCultureInfo("de-DE"));
        }
        catch (FormatException)
        {
            return today.ToString("dd.MM.yyyy", CultureInfo.InvariantCulture);
        }
    }
}
