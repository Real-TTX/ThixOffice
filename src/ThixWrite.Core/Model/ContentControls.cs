namespace ThixWrite.Core.Model;

/// <summary>
/// The start or the end of a block-level content control (w:sdt around paragraphs – Word's cover pages, rich-text controls, the
/// boxes of templates): invisible marks at the very start of its first and the very end of its last paragraph, so the paragraphs
/// between are ordinary, editable paragraphs. The start keeps the control's properties as read (w:sdtPr, w:sdtEndPr); the writer puts
/// the paragraphs (and tables) between the marks back into a w:sdt. An end without a start is dropped, a start without an end is
/// closed at the end of its story.
/// </summary>
public sealed record ContentControlMarkInline(bool IsEnd, string? PropertiesXml, CharacterProperties Properties) : Inline(Properties, null)
{
    /// <summary>
    /// The mark of a content control INSIDE a paragraph (w:sdt around runs: text fields, dates, drop-downs, check boxes): its text between
    /// the marks is ordinary, formatted, editable text; the writer puts it back into a w:sdt in the paragraph.
    /// </summary>
    public bool IsInline { get; init; }

    public override int Length => 1;

    public override string Characters => "￼";

    public override bool IsMarker => true;
}
