using ThixOffice.Text.Unicode;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>Überprüfen › Kommentare: new, edit, delete, previous / next.</summary>
public sealed partial class EditingSession
{
    /// <summary>The comments with their ranges, in document order.</summary>
    public IReadOnlyList<PlacedComment> Comments() => Model.Comments.All(Document);

    /// <summary>The comment whose text holds the caret (the innermost, the last one starting before it), or null.</summary>
    public PlacedComment? CommentAtCaret
    {
        get
        {
            var caret = _selection.Active;
            return Comments().Where(placed => placed.Range.Contains(caret)).OrderByDescending(placed => placed.Range.Start).FirstOrDefault();
        }
    }

    /// <summary>
    /// Neuer Kommentar: a comment on the selection (on the word at the caret when nothing is selected) by <paramref name="author"/>, with
    /// <paramref name="text"/> (often empty: the user types it in the comment's card). One step; returns the comment's reference.
    /// </summary>
    public DocumentPosition AddComment(string author, string initials, string text = "", DateTime? date = null)
    {
        var reference = _selection.End;
        Run("Neuer Kommentar", EditKind.Other, null, () =>
        {
            EnsureStyle(BuiltInStyles.CommentText);
            var range = _selection.Range;
            if (range.IsEmpty && Document.ParagraphAt(range.Start.Block) is { Length: > 0 } paragraph)
            {
                var (start, end) = TextBoundaries.WordAt(paragraph.Text, Math.Min(range.Start.Offset, paragraph.Length));
                while (end > start && char.IsWhiteSpace(paragraph.Text[end - 1]))
                {
                    end--;
                }

                range = new DocumentRange(range.Start with { Offset = start }, range.Start with { Offset = end });
            }

            var id = Model.Comments.NewId(Document);
            var comment = new Comment(id, author, initials, date ?? _clock(), new Story([])).WithText(text);
            var properties = new CharacterProperties { StyleId = BuiltInStyles.CommentReference };

            // The end first (positions before it stay), then the start.
            var endMap = (ReplaceMap)Apply(ContentEdits.Replace(Document, DocumentRange.At(range.End), [new Paragraph([new CommentInline(comment, properties)])]));
            reference = new DocumentPosition(endMap.EndOfInserted.Block, endMap.EndOfInserted.Offset - 1);
            Apply(ContentEdits.Replace(Document, DocumentRange.At(range.Start), [new Paragraph([new CommentAnchorInline(id, CharacterProperties.Empty)])]));
            reference = reference.Block == range.Start.Block ? reference with { Offset = reference.Offset + 1 } : reference;
            SetSelectionQuiet(Selection.Caret(reference with { Offset = reference.Offset + 1 }));
        });
        return reference;
    }

    /// <summary>The comment at its reference, or null.</summary>
    public Comment? CommentAt(DocumentPosition reference) =>
        Document.ParagraphAt(reference.Block) is { } paragraph && reference.Offset < paragraph.Length && paragraph.InlineAt(reference.Offset) is CommentInline inline ? inline.Comment : null;

    /// <summary>Changes the text of the comment at <paramref name="reference"/> (plain text, one paragraph per line); one step, nothing when it is the same.</summary>
    public void SetCommentText(DocumentPosition reference, string text)
    {
        if (CommentAt(reference) is not { } comment || comment.Text == text.Replace("\r\n", "\n"))
        {
            return;
        }

        Run("Kommentar bearbeiten", EditKind.Other, null, () => ReplaceComment(reference, comment.WithText(text)));
    }

    /// <summary>Marks the comment as done (Word's "Thread auflösen") or open again.</summary>
    public void SetCommentDone(DocumentPosition reference, bool done)
    {
        if (CommentAt(reference) is not { } comment || comment.Done == done)
        {
            return;
        }

        Run(done ? "Kommentar erledigt" : "Kommentar wieder öffnen", EditKind.Other, null, () => ReplaceComment(reference, comment with { Done = done }));
    }

    /// <summary>Löschen: the comment at <paramref name="reference"/> (its anchor too; replies to it stay as they are in Word: they go with it).</summary>
    public void DeleteComment(DocumentPosition reference)
    {
        var placed = Comments().FirstOrDefault(item => item.Reference == reference);
        if (placed is null)
        {
            return;
        }

        Run("Kommentar löschen", EditKind.Other, null, () => RemoveComments([placed, .. RepliesTo(placed.Comment)]));
    }

    /// <summary>Alle Kommentare im Dokument löschen.</summary>
    public void DeleteAllComments()
    {
        var all = Comments();
        if (all.Count == 0)
        {
            return;
        }

        Run("Alle Kommentare löschen", EditKind.Other, null, () => RemoveComments(all));
    }

    /// <summary>Vorheriger / Nächster: selects the text of the comment before / after the caret; returns it (null: none).</summary>
    public PlacedComment? NextComment(bool forward)
    {
        var caret = forward ? _selection.End : _selection.Start;
        var all = Comments().Where(placed => !placed.Comment.IsReply).ToList();
        var found = forward
            ? all.FirstOrDefault(placed => placed.Range.Start > caret || (placed.Range.Start == caret && _selection.IsCollapsed))
            : all.LastOrDefault(placed => placed.Range.Start < caret);
        if (found is not null)
        {
            Select(found.Range.Start, found.Range.End);
        }

        return found;
    }

    /// <summary>The replies to a comment (by its paragraph id).</summary>
    public IReadOnlyList<PlacedComment> RepliesTo(Comment comment) =>
        comment.ParaId is null ? [] : Comments().Where(placed => placed.Comment.ParentParaId == comment.ParaId).ToList();

    private void ReplaceComment(DocumentPosition reference, Comment comment)
    {
        var paragraph = (Paragraph)Document.Blocks[reference.Block];
        var index = paragraph.InlineIndexAt(reference.Offset);
        var inline = (CommentInline)paragraph.Inlines[index];
        var inlines = paragraph.Inlines.SetItem(index, inline with { Comment = comment });
        ApplyOptional(ContentEdits.FormatParagraphs(Document, reference.Block, reference.Block, _ => paragraph.WithInlines(inlines)));
    }

    /// <summary>Removes the references and anchors of comments, from the end of the document backwards (positions before stay).</summary>
    private void RemoveComments(IEnumerable<PlacedComment> comments)
    {
        var positions = new SortedSet<DocumentPosition>();
        foreach (var placed in comments)
        {
            positions.Add(placed.Reference);
            if (placed.Range.Start != placed.Reference && Document.ParagraphAt(placed.Range.Start.Block)?.InlineAt(placed.Range.Start.Offset) is CommentAnchorInline)
            {
                positions.Add(placed.Range.Start);
            }
        }

        foreach (var position in positions.Reverse())
        {
            Apply(ContentEdits.Delete(Document, new DocumentRange(position, position with { Offset = position.Offset + 1 })));
        }
    }
}
