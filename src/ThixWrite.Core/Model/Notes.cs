using System.Globalization;
using System.Text;

namespace ThixWrite.Core.Model;

/// <summary>Footnote (at the foot of its page) or endnote (at the end of the document).</summary>
public enum NoteKind
{
    Footnote,
    Endnote,
}

/// <summary>
/// The reference of a footnote or endnote in the text (w:footnoteReference / w:endnoteReference): one object character that shows
/// the note's number (counted in document order, per kind), and the note's text itself as a <see cref="Story"/>. The text travels
/// with its reference: deleting the reference deletes the note, copying copies it, undo restores it – no second list to keep in step.
/// <see cref="SourceId"/> is the id the note had in the file (reused when written back; not part of the equality).
/// </summary>
public sealed record NoteInline(NoteKind Kind, Story Content, CharacterProperties Properties, Hyperlink? Link = null, string? SourceId = null)
    : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";

    /// <summary>The note's text as plain text (paragraphs separated by "\n", its own number mark left out).</summary>
    public string PlainText => string.Join("\n", Content.Blocks.OfType<Paragraph>().Select(paragraph =>
        string.Concat(paragraph.Inlines.Where(inline => inline is TextRun).Select(inline => inline.Characters))));

    public bool Equals(NoteInline? other) =>
        other is not null && Kind == other.Kind && Content.Equals(other.Content) && Properties == other.Properties && Link == other.Link;

    public override int GetHashCode() => HashCode.Combine(Kind, Content.Blocks.Count, Properties);
}

/// <summary>
/// The number mark at the start of a note's text (w:footnoteRef / w:endnoteRef): one object character that shows the number of the
/// note it belongs to.
/// </summary>
public sealed record NoteMarkInline(NoteKind Kind, CharacterProperties Properties, Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";
}

/// <summary>How notes are numbered (Word's defaults: footnotes 1, 2, 3 …; endnotes i, ii, iii …) and the styles of new notes.</summary>
public static class NoteNumbering
{
    public const string FootnoteText = "FootnoteText";
    public const string FootnoteReference = "FootnoteReference";
    public const string EndnoteText = "EndnoteText";
    public const string EndnoteReference = "EndnoteReference";

    /// <summary>The label of note <paramref name="number"/> (1-based).</summary>
    public static string Format(NoteKind kind, int number) =>
        kind == NoteKind.Endnote ? Roman(number) : number.ToString(CultureInfo.InvariantCulture);

    public static string TextStyle(NoteKind kind) => kind == NoteKind.Endnote ? EndnoteText : FootnoteText;

    public static string ReferenceStyle(NoteKind kind) => kind == NoteKind.Endnote ? EndnoteReference : FootnoteReference;

    /// <summary>German: "Fußnote" / "Endnote".</summary>
    public static string Label(NoteKind kind) => kind == NoteKind.Endnote ? "Endnote" : "Fußnote";

    /// <summary>A new note's text: its number mark, a space, in the note text style.</summary>
    public static Story NewContent(NoteKind kind) => new(
    [
        new Paragraph(
            [new NoteMarkInline(kind, new CharacterProperties { StyleId = ReferenceStyle(kind) }), new TextRun(" ", CharacterProperties.Empty)],
            new ParagraphProperties { StyleId = TextStyle(kind) }),
    ]);

    /// <summary>The notes of a document in order: (block, offset, note).</summary>
    public static IEnumerable<(DocumentPosition Position, NoteInline Note)> All(Document document)
    {
        for (var block = 0; block < document.Blocks.Count; block++)
        {
            if (document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            for (var index = 0; index < paragraph.Inlines.Length; index++)
            {
                if (paragraph.Inlines[index] is NoteInline note)
                {
                    yield return (new DocumentPosition(block, paragraph.StartOf(index)), note);
                }
            }
        }
    }

    private static string Roman(int number)
    {
        if (number <= 0)
        {
            return number.ToString(CultureInfo.InvariantCulture);
        }

        (int Value, string Text)[] digits = [(1000, "m"), (900, "cm"), (500, "d"), (400, "cd"), (100, "c"), (90, "xc"), (50, "l"), (40, "xl"), (10, "x"), (9, "ix"), (5, "v"), (4, "iv"), (1, "i")];
        var builder = new StringBuilder();
        foreach (var (value, text) in digits)
        {
            while (number >= value)
            {
                builder.Append(text);
                number -= value;
            }
        }

        return builder.ToString();
    }
}
