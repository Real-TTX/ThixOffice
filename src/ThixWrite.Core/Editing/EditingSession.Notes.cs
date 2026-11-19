using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Footnotes and endnotes (Referenzen › Fußnote einfügen / Endnote einfügen / Nächste Fußnote).</summary>
public sealed partial class EditingSession
{
    /// <summary>
    /// Inserts a footnote or endnote at the caret (over the selection's end, the selection kept as text, like Word): the reference in the
    /// style "Fußnotenzeichen" / "Endnotenzeichen", the note's text a paragraph "Fußnotentext" / "Endnotentext" with its number mark and a
    /// space. Returns the position of the reference (the editor opens the note's text there).
    /// </summary>
    public DocumentPosition InsertNote(NoteKind kind)
    {
        var reference = _selection.End;
        Run(NoteNumbering.Label(kind) + " einfügen", EditKind.Other, null, () =>
        {
            ClearWholeCells();
            EnsureStyle(NoteNumbering.TextStyle(kind));
            EnsureStyle(NoteNumbering.ReferenceStyle(kind));
            var (properties, link) = TypingFormat();
            var note = new NoteInline(kind, NoteNumbering.NewContent(kind), properties with { StyleId = NoteNumbering.ReferenceStyle(kind), VerticalPosition = null }, link);
            var at = _selection.End;
            var map = (ReplaceMap)Apply(ContentEdits.Replace(Document, new DocumentRange(at, at), [new Paragraph([note])]));
            reference = new DocumentPosition(map.EndOfInserted.Block, map.EndOfInserted.Offset - 1);
            SetSelectionQuiet(Selection.Caret(map.EndOfInserted));
        });
        return reference;
    }

    /// <summary>The note referenced at <paramref name="reference"/>, or null.</summary>
    public NoteInline? NoteAt(DocumentPosition reference) =>
        Document.ParagraphAt(reference.Block) is { } paragraph && reference.Offset < paragraph.Length ? paragraph.InlineAt(reference.Offset) as NoteInline : null;

    /// <summary>Replaces the text of the note referenced at <paramref name="reference"/> (one step; nothing when it is the same).</summary>
    public void SetNoteContent(DocumentPosition reference, Story content)
    {
        if (NoteAt(reference) is not { } note || note.Content.Equals(content))
        {
            return;
        }

        Run(NoteNumbering.Label(note.Kind), EditKind.Other, null, () =>
        {
            var paragraph = (Paragraph)Document.Blocks[reference.Block];
            var index = paragraph.InlineIndexAt(reference.Offset);
            var inlines = paragraph.Inlines.SetItem(index, note with { Content = content });
            ApplyOptional(ContentEdits.FormatParagraphs(Document, reference.Block, reference.Block, _ => paragraph.WithInlines(inlines)));
        });
    }

    /// <summary>The reference of the next (or previous) note of a kind after (before) the caret, or null; Nächste Fußnote moves the caret behind it.</summary>
    public DocumentPosition? NextNote(NoteKind kind, bool forward = true)
    {
        var caret = _selection.Active;
        var notes = NoteNumbering.All(Document).Where(item => item.Note.Kind == kind).Select(item => item.Position).ToList();
        DocumentPosition? found = forward
            ? notes.Cast<DocumentPosition?>().FirstOrDefault(position => position!.Value >= caret)
            : notes.Cast<DocumentPosition?>().LastOrDefault(position => position!.Value with { Offset = position.Value.Offset + 1 } < caret);
        if (found is { } position)
        {
            SetCaret(position with { Offset = position.Offset + 1 });
        }

        return found;
    }
}
