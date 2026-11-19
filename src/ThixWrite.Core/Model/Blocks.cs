using System.Collections.Immutable;
using System.Text;

namespace ThixWrite.Core.Model;

/// <summary>
/// A block of the document body. Blocks are IMMUTABLE: an edit replaces a block with a new instance, so undo keeps the old
/// instances and the layout caches by instance (an unchanged block keeps its layout). 1a: <see cref="Paragraph"/> and
/// <see cref="OpaqueBlock"/> (preserved content). A table is not a block of its own: its cells' paragraphs are paragraphs with a
/// <see cref="Paragraph.Cell"/> (see <see cref="Table"/>).
/// </summary>
public abstract class Block
{
    /// <summary>The number of positions inside the block's text (a paragraph's text length; 0 for blocks without text).</summary>
    public abstract int Length { get; }

    /// <summary>True when both blocks have the same content and formatting (not the same instance).</summary>
    public abstract bool ContentEquals(Block other);
}

/// <summary>
/// A paragraph: inlines (normalized – adjacent runs with the same formatting joined, empty runs dropped), its properties, the
/// formatting of its paragraph mark (decides the height of an empty paragraph) and, at the end of a section, that section's
/// page setup (Word keeps w:sectPr in the last paragraph of a section). A paragraph in a table cell names its cell
/// (<see cref="Cell"/>); every edit keeps it.
/// </summary>
public sealed class Paragraph : Block
{
    private readonly int[] _starts;

    public Paragraph(
        IEnumerable<Inline> inlines,
        ParagraphProperties? properties = null,
        CharacterProperties? markProperties = null,
        SectionProperties? sectionBreak = null,
        CellRef? cell = null)
    {
        Inlines = Normalize(inlines);
        Cell = cell;
        Properties = properties ?? ParagraphProperties.Empty;
        MarkProperties = markProperties ?? CharacterProperties.Empty;
        SectionBreak = sectionBreak;

        _starts = new int[Inlines.Length + 1];
        var text = new StringBuilder();
        for (var index = 0; index < Inlines.Length; index++)
        {
            _starts[index] = text.Length;
            text.Append(Inlines[index].Characters);
        }

        _starts[Inlines.Length] = text.Length;
        Text = text.ToString();
    }

    /// <summary>A paragraph with plain text in one run.</summary>
    public Paragraph(string text, ParagraphProperties? properties = null, CharacterProperties? runProperties = null)
        : this(text.Length == 0 ? [] : [new TextRun(text, runProperties ?? CharacterProperties.Empty)], properties)
    {
    }

    public ImmutableArray<Inline> Inlines { get; }

    public ParagraphProperties Properties { get; }

    public CharacterProperties MarkProperties { get; }

    public SectionProperties? SectionBreak { get; }

    /// <summary>The table cell this paragraph is in, or null outside tables.</summary>
    public CellRef? Cell { get; }

    /// <summary>The text: runs' characters, one <see cref="SpecialCharacters.Object"/> per inline object.</summary>
    public string Text { get; }

    public override int Length => Text.Length;

    public static Paragraph Empty(ParagraphProperties? properties = null, CharacterProperties? markProperties = null, CellRef? cell = null) =>
        new([], properties, markProperties, null, cell);

    public Paragraph WithProperties(ParagraphProperties properties) => new(Inlines, properties, MarkProperties, SectionBreak, Cell);

    public Paragraph WithMarkProperties(CharacterProperties markProperties) => new(Inlines, Properties, markProperties, SectionBreak, Cell);

    public Paragraph WithSectionBreak(SectionProperties? sectionBreak) => new(Inlines, Properties, MarkProperties, sectionBreak, Cell);

    public Paragraph WithInlines(IEnumerable<Inline> inlines) => new(inlines, Properties, MarkProperties, SectionBreak, Cell);

    /// <summary>The same paragraph in another cell (or outside tables with null).</summary>
    public Paragraph WithCell(CellRef? cell) => Nullable.Equals(cell, Cell) && ReferenceEquals(cell?.Table, Cell?.Table) ? this : new(Inlines, Properties, MarkProperties, SectionBreak, cell);

    /// <summary>The offset in the text where inline <paramref name="index"/> starts.</summary>
    public int StartOf(int index) => _starts[index];

    /// <summary>The inline holding the character at <paramref name="offset"/> (-1 at or after the end).</summary>
    public int InlineIndexAt(int offset)
    {
        if (offset < 0 || offset >= Length)
        {
            return -1;
        }

        var index = Array.BinarySearch(_starts, 0, Inlines.Length, offset);
        if (index < 0)
        {
            return ~index - 1;
        }

        // Several inlines cannot start at the same offset (empty runs are dropped), so this is the one.
        return index;
    }

    /// <summary>The inline holding the character at <paramref name="offset"/>, or null.</summary>
    public Inline? InlineAt(int offset)
    {
        var index = InlineIndexAt(offset);
        return index < 0 ? null : Inlines[index];
    }

    /// <summary>
    /// The formatting typing at <paramref name="offset"/> takes: that of the character before it (of the first character at the
    /// start, of the paragraph mark in an empty paragraph). Inline objects pass on their formatting; links do not continue.
    /// </summary>
    public CharacterProperties TypingPropertiesAt(int offset)
    {
        if (Length == 0)
        {
            return MarkProperties;
        }

        var inline = InlineAt(Math.Clamp(offset - 1, 0, Length - 1));
        return inline switch
        {
            NoteInline note => note.Properties with { StyleId = null }, // text after a note's number is not raised
            null => MarkProperties,
            _ => inline.Properties,
        };
    }

    /// <summary>The inlines of [<paramref name="start"/>, <paramref name="end"/>), text runs cut at the edges.</summary>
    public IReadOnlyList<Inline> Slice(int start, int end)
    {
        start = Math.Clamp(start, 0, Length);
        end = Math.Clamp(end, start, Length);
        var result = new List<Inline>();
        if (start == end)
        {
            return result;
        }

        for (var index = 0; index < Inlines.Length; index++)
        {
            var inlineStart = _starts[index];
            var inlineEnd = _starts[index + 1];
            if (inlineEnd <= start || inlineStart >= end)
            {
                continue;
            }

            var inline = Inlines[index];
            if (inline is TextRun run && (inlineStart < start || inlineEnd > end))
            {
                var from = Math.Max(start, inlineStart) - inlineStart;
                var to = Math.Min(end, inlineEnd) - inlineStart;
                result.Add(run with { Text = run.Text[from..to] });
                continue;
            }

            result.Add(inline);
        }

        return result;
    }

    public override bool ContentEquals(Block other) =>
        other is Paragraph paragraph
        && Properties == paragraph.Properties
        && MarkProperties == paragraph.MarkProperties
        && SectionBreak == paragraph.SectionBreak
        && Nullable.Equals(Cell, paragraph.Cell)
        && Inlines.SequenceEqual(paragraph.Inlines);

    public override string ToString() => Text.Replace(SpecialCharacters.Object, '¤');

    private static ImmutableArray<Inline> Normalize(IEnumerable<Inline> inlines)
    {
        var builder = ImmutableArray.CreateBuilder<Inline>();
        foreach (var inline in inlines)
        {
            if (inline is TextRun { Length: 0 })
            {
                continue;
            }

            if (inline is TextRun run && builder.Count > 0 && builder[^1] is TextRun previous && previous.CanJoin(run))
            {
                builder[^1] = previous with { Text = previous.Text + run.Text };
                continue;
            }

            builder.Add(inline);
        }

        return builder.ToImmutable();
    }
}

/// <summary>
/// A block ThixWrite does not model yet (a table in 1a, a block-level content control, …), kept as its original XML and
/// written back unchanged. It is shown as a frame with <see cref="Lines"/> (the text of its paragraphs / table rows) and can be
/// deleted as a whole, not edited.
/// </summary>
public sealed class OpaqueBlock(string xml, PreservedKind kind, IReadOnlyList<string> lines) : Block
{
    public string Xml { get; } = xml;

    public PreservedKind Kind { get; } = kind;

    /// <summary>The visible text, one entry per paragraph (table rows: the cells separated by tabs).</summary>
    public IReadOnlyList<string> Lines { get; } = lines;

    public override int Length => 0;

    public override bool ContentEquals(Block other) =>
        other is OpaqueBlock block && block.Xml == Xml && block.Kind == Kind && block.Lines.SequenceEqual(Lines);

    public override string ToString() => $"[{Kind}: {string.Join(" / ", Lines)}]";
}
