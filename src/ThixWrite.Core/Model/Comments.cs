namespace ThixWrite.Core.Model;

/// <summary>
/// A comment (w:comment): who wrote it and when, its text (a <see cref="Story"/> of paragraphs), and what ties it to Word's threads:
/// <see cref="ParaId"/> (the w14:paraId of its last paragraph), <see cref="ParentParaId"/> (the comment it answers, from
/// commentsExtended.xml) and <see cref="Done"/>. <see cref="SourceXml"/> / <see cref="SourceContent"/>: the element as read and its
/// text then – written back unchanged while the text is the same. Equal by what the user sees (not by the source).
/// </summary>
public sealed record Comment(string Id, string Author, string Initials, DateTime? Date, Story Content)
{
    public string? ParaId { get; init; }

    public string? ParentParaId { get; init; }

    public bool Done { get; init; }

    public string? SourceXml { get; init; }

    public Story? SourceContent { get; init; }

    /// <summary>True for an answer to another comment (a reply in Word's thread).</summary>
    public bool IsReply => ParentParaId is not null;

    /// <summary>The text, paragraphs separated by "\n" (objects left out).</summary>
    public string Text => string.Join("\n", Content.Blocks.OfType<Paragraph>().Select(paragraph =>
        string.Concat(paragraph.Inlines.OfType<TextRun>().Select(run => run.Text))));

    /// <summary>The comment with plain text (one paragraph per line, style "Kommentartext").</summary>
    public Comment WithText(string text) => this with
    {
        Content = new Story(text.Replace("\r\n", "\n").Split('\n').Select(line =>
            new Paragraph(line, new ParagraphProperties { StyleId = BuiltInStyles.CommentText }))),
    };

    /// <summary>"AB" from "Anna Berger" (the first letters of the first two names).</summary>
    public static string InitialsOf(string author) =>
        string.Concat(author.Split(' ', StringSplitOptions.RemoveEmptyEntries).Take(2).Select(part => char.ToUpperInvariant(part[0])));

    public bool Equals(Comment? other) =>
        other is not null && Id == other.Id && Author == other.Author && Initials == other.Initials && Date == other.Date
        && Content.Equals(other.Content) && ParentParaId == other.ParentParaId && Done == other.Done;

    public override int GetHashCode() => HashCode.Combine(Id, Author, Date);
}

/// <summary>The start of the text a comment is about (w:commentRangeStart): an invisible marker naming the comment.</summary>
public sealed record CommentAnchorInline(string Id, CharacterProperties Properties, Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";

    public override bool IsMarker => true;
}

/// <summary>
/// The end of a comment's text and the comment itself (w:commentRangeEnd + w:commentReference): an invisible marker that carries the
/// comment, so that deleting, copying and undoing the text does the same to the comment.
/// </summary>
public sealed record CommentInline(Comment Comment, CharacterProperties Properties, Hyperlink? Link = null) : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";

    public override bool IsMarker => true;
}

/// <summary>A comment of a document with where it is: the text it is about (anchor to reference) and its reference.</summary>
public sealed record PlacedComment(Comment Comment, DocumentRange Range, DocumentPosition Reference);

/// <summary>The comments of a document.</summary>
public static class Comments
{
    /// <summary>
    /// Every comment in document order with its range: from its anchor (the nearest one before it with the same id; none: the
    /// reference itself) to its reference.
    /// </summary>
    public static IReadOnlyList<PlacedComment> All(Document document)
    {
        var result = new List<PlacedComment>();
        var open = new Dictionary<string, Stack<DocumentPosition>>(StringComparer.Ordinal);
        for (var block = 0; block < document.Blocks.Count; block++)
        {
            if (document.Blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            for (var index = 0; index < paragraph.Inlines.Length; index++)
            {
                var position = new DocumentPosition(block, paragraph.StartOf(index));
                switch (paragraph.Inlines[index])
                {
                    case CommentAnchorInline anchor:
                        if (!open.TryGetValue(anchor.Id, out var stack))
                        {
                            stack = new Stack<DocumentPosition>();
                            open[anchor.Id] = stack;
                        }

                        stack.Push(position);
                        break;
                    case CommentInline comment:
                        var start = open.TryGetValue(comment.Comment.Id, out var starts) && starts.Count > 0 ? starts.Pop() : position;
                        result.Add(new PlacedComment(comment.Comment, new DocumentRange(start, position), position));
                        break;
                }
            }
        }

        return result;
    }

    /// <summary>An id no comment of the document has ("1", "2" …).</summary>
    public static string NewId(Document document)
    {
        var used = All(document).Select(placed => placed.Comment.Id).ToHashSet(StringComparer.Ordinal);
        var next = used.Select(id => int.TryParse(id, out var number) ? number : 0).DefaultIfEmpty(0).Max() + 1;
        while (used.Contains(next.ToString(System.Globalization.CultureInfo.InvariantCulture)))
        {
            next++;
        }

        return next.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
}
