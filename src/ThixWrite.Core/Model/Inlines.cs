namespace ThixWrite.Core.Model;

/// <summary>Characters with a meaning inside paragraph text.</summary>
public static class SpecialCharacters
{
    public const char Tab = '\t';

    /// <summary>Manual line break (Shift+Enter, w:br / w:cr).</summary>
    public const char LineBreak = '\v';

    /// <summary>Page break (Ctrl+Enter, w:br w:type="page").</summary>
    public const char PageBreak = '\f';

    /// <summary>The single character an inline object (<see cref="OpaqueInline"/>, later images, fields …) takes in the text.</summary>
    public const char Object = '\uFFFC';

    public const char NonBreakingSpace = '\u00A0';

    public const char SoftHyphen = '\u00AD';

    public const char NonBreakingHyphen = '\u2011';

    /// <summary>True for characters that end a line by themselves.</summary>
    public static bool IsBreak(char character) => character is LineBreak or PageBreak;
}

/// <summary>A link on a run of text (w:hyperlink): an address or a bookmark in the document.</summary>
public sealed record Hyperlink(string? Uri, string? Anchor = null, string? Tooltip = null, bool History = true);

/// <summary>
/// The content of a paragraph: runs of text and inline objects, each with character formatting and an optional link.
/// New kinds (images, fields, footnote references, comments – 1b/1c) derive from <see cref="Inline"/>; anything that is not a
/// text run takes exactly one character (<see cref="SpecialCharacters.Object"/>) of the paragraph text.
/// </summary>
public abstract record Inline(CharacterProperties Properties, Hyperlink? Link)
{
    /// <summary>The characters this inline takes in the paragraph text.</summary>
    public abstract int Length { get; }

    /// <summary>The characters as the paragraph text has them.</summary>
    public abstract string Characters { get; }

    /// <summary>Invisible and without size (a bookmark, a field character, the start of a comment …): it sticks to the character after it.</summary>
    public virtual bool IsMarker => false;

    /// <summary>A tracked change this content belongs to ("Änderungen nachverfolgen": inserted or deleted text, moves), or null.</summary>
    public Revision? Revision { get; init; }
}

/// <summary>What a tracked change did (w:ins, w:del, w:moveTo, w:moveFrom).</summary>
public enum RevisionKind
{
    Insertion,
    Deletion,
    MoveTo,
    MoveFrom,
}

/// <summary>A tracked change: what, who (Word's author name), when, and the id it had in the file.</summary>
public sealed record Revision(RevisionKind Kind, string? Author, DateTime? Date, string? Id = null)
{
    /// <summary>Text that is gone in the final document (a deletion or the old place of a move).</summary>
    public bool IsRemoval => Kind is RevisionKind.Deletion or RevisionKind.MoveFrom;
}

/// <summary>Formatted text. May contain tabs and line / page breaks (<see cref="SpecialCharacters"/>).</summary>
public sealed record TextRun(string Text, CharacterProperties Properties, Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override string Characters => Text;

    public override int Length => Text.Length;

    /// <summary>True when the two runs can be one (same formatting, link and tracked change).</summary>
    public bool CanJoin(TextRun other) => Properties == other.Properties && Link == other.Link && Revision == other.Revision;
}

/// <summary>What a preserved element is (for the display and the "unsupported content" notes).</summary>
public enum PreservedKind
{
    Other,
    /// <summary>An image or drawing (w:drawing, w:pict).</summary>
    Drawing,
    /// <summary>A field (w:fldChar / w:instrText / w:fldSimple).</summary>
    Field,
    /// <summary>A tracked change (w:ins, w:del, w:moveFrom, w:moveTo).</summary>
    TrackedChange,
    /// <summary>A bookmark (invisible, never reported).</summary>
    Bookmark,
    Comment,
    Footnote,
    Endnote,
    Math,
    ContentControl,
    Table,
    EmbeddedObject,
    /// <summary>A column break (w:br w:type="column") – columns come with 1b.</summary>
    ColumnBreak,
    /// <summary>Positional tab, smart tag, custom XML …</summary>
    Markup,
    /// <summary>A picture Word positions freely (wp:anchor): ThixWrite shows it in the text flow and keeps its position.</summary>
    FloatingDrawing,
}

/// <summary>
/// Inline content ThixWrite does not model yet, kept as the original XML so that it is written back unchanged
/// (preserve-unknown). It takes one character in the text; <see cref="DisplayText"/> (field results, inserted text …) is
/// shown but not editable, an inline image keeps its size (<see cref="Width"/> × <see cref="Height"/>) as a placeholder box.
/// <see cref="IsRunContent"/>: the XML is a child of a w:r (drawing, field char …) rather than of the paragraph
/// (bookmark, tracked change, content control …).
/// </summary>
public sealed record OpaqueInline(
    string Xml,
    PreservedKind Kind,
    bool IsRunContent,
    CharacterProperties Properties,
    string DisplayText = "",
    double Width = 0,
    double Height = 0,
    Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override int Length => 1;

    /// <summary>What the drawing shows (shapes, text boxes, groups, charts, SmartArt) when this is a w:drawing ThixWrite can draw; null otherwise.</summary>
    public DrawingGraphic? Graphic { get; init; }

    /// <summary>
    /// A table nested in a table cell (<see cref="Kind"/> = <see cref="PreservedKind.Table"/>, <see cref="Xml"/> = its w:tbl, written back at
    /// block level where it was): what it shows, read-only \u2013 a story of its own laid out at the width of the line it is on.
    /// </summary>
    public Story? Nested { get; init; }

    /// <summary>A formula (m:oMath, m:oMathPara) as it shows – read-only; null for anything else.</summary>
    public MathNode? Math { get; init; }

    /// <summary>A display formula (m:oMathPara): set on a line of its own, centred.</summary>
    public bool IsDisplayMath { get; init; }

    /// <summary>A block-level element carried by a paragraph (a nested table): the writer puts the XML between paragraphs, not into one.</summary>
    public bool IsBlockContent => Kind == PreservedKind.Table;

    public override string Characters => ObjectText;

    private const string ObjectText = "\uFFFC";

    /// <summary>Invisible and without size: a marker such as a bookmark or a field character.</summary>
    public override bool IsMarker => Nested is null && Math is null && DisplayText.Length == 0 && Width <= 0;
}
