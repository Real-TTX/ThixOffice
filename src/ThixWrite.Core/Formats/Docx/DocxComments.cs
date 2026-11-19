using System.Globalization;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// comments.xml: comments are read with their author, initials, date and text, and their place in Word's threads (commentsExtended:
/// the comment they answer, done). Written: the comments of the body in order with ids 0, 1, 2 …; a comment whose text did not
/// change is written as it was read (its paragraph ids stay, so Word's threads, reactions and done flags stay with it); the entries of
/// commentsExtended / commentsIds / commentsExtensible for comments that are gone are removed, done flags are updated.
/// </summary>
internal static class DocxComments
{
    /// <summary>The comments of the package by id; <paramref name="readStory"/> reads a comment's paragraphs (with the comments part for its links).</summary>
    public static Dictionary<string, Comment> Read(MainDocumentPart main, Func<OpenXmlElement, OpenXmlPart, Story> readStory)
    {
        var result = new Dictionary<string, Comment>(StringComparer.Ordinal);
        if (main.WordprocessingCommentsPart is not { Comments: { } comments } part)
        {
            return result;
        }

        var threads = new Dictionary<string, (string? Parent, bool Done)>(StringComparer.OrdinalIgnoreCase);
        if (main.WordprocessingCommentsExPart?.RootElement is { } extended)
        {
            foreach (var entry in extended.ChildElements)
            {
                if (Attribute(entry, "paraId") is { } paraId)
                {
                    threads[paraId] = (Attribute(entry, "paraIdParent"), Attribute(entry, "done") is "1" or "true");
                }
            }
        }

        foreach (var element in comments.Elements<W.Comment>())
        {
            if (element.Id?.Value is not { } id || result.ContainsKey(id))
            {
                continue;
            }

            var content = readStory(element, part);
            var paraId = element.Elements<W.Paragraph>().LastOrDefault()?.ParagraphId?.Value;
            var thread = paraId is not null && threads.TryGetValue(paraId, out var found) ? found : (null, false);
            var author = element.Author?.Value ?? "";
            result[id] = new Comment(id, author, element.Initials?.Value ?? Comment.InitialsOf(author), element.Date?.Value, content)
            {
                ParaId = paraId,
                ParentParaId = thread.Parent,
                Done = thread.Done,
                SourceXml = XmlFragments.Canonical(element),
                SourceContent = content,
            };
        }

        return result;
    }

    /// <summary>The ids of the comments written into the body: anchors and references paired in document order.</summary>
    public sealed class Allocation
    {
        private readonly Queue<long?> _anchors = new();
        private readonly Queue<long?> _references = new();

        public List<(long Id, Comment Comment)> Written { get; } = [];

        /// <summary>Pairs the anchors and references of the body (blocks in writing order) and gives each pair an id.</summary>
        public void Prepare(Document document)
        {
            var anchors = new List<long?>();
            var open = new Dictionary<string, Stack<int>>(StringComparer.Ordinal);
            var used = new HashSet<long>();
            var all = document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<CommentInline>()
                .Select(inline => long.TryParse(inline.Comment.Id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : -1).ToList();
            var free = all.DefaultIfEmpty(-1).Max() + 1;

            // The model's id when it is a number not taken yet (a document read stays as it was), else the next free one.
            long Allocate(string id)
            {
                if (long.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value >= 0 && used.Add(value))
                {
                    return value;
                }

                while (!used.Add(free))
                {
                    free++;
                }

                return free;
            }

            foreach (var paragraph in document.Blocks.OfType<Paragraph>())
            {
                foreach (var inline in paragraph.Inlines)
                {
                    switch (inline)
                    {
                        case CommentAnchorInline anchor:
                            if (!open.TryGetValue(anchor.Id, out var stack))
                            {
                                stack = new Stack<int>();
                                open[anchor.Id] = stack;
                            }

                            stack.Push(anchors.Count);
                            anchors.Add(null);
                            break;
                        case CommentInline comment:
                            var id = Allocate(comment.Comment.Id);
                            if (open.TryGetValue(comment.Comment.Id, out var starts) && starts.Count > 0)
                            {
                                anchors[starts.Pop()] = id;
                            }

                            _references.Enqueue(id);
                            Written.Add((id, comment.Comment));
                            break;
                    }
                }
            }

            foreach (var anchor in anchors)
            {
                _anchors.Enqueue(anchor);
            }
        }

        /// <summary>The id of the next anchor written (null: a start without its comment – left out).</summary>
        public long? NextAnchor() => _anchors.Count > 0 ? _anchors.Dequeue() : null;

        /// <summary>The id of the next reference written (null outside the body: left out).</summary>
        public long? NextReference() => _references.Count > 0 ? _references.Dequeue() : null;
    }

    /// <summary>Writes comments.xml (created when there are comments) and tidies Word's thread parts.</summary>
    public static void Write(MainDocumentPart main, Allocation allocation, Func<IReadOnlyList<Block>, OpenXmlPart, IEnumerable<OpenXmlElement>> writeBlocks)
    {
        var part = main.WordprocessingCommentsPart;
        if (part is null && allocation.Written.Count == 0)
        {
            return;
        }

        part ??= main.AddNewPart<WordprocessingCommentsPart>();
        var root = new W.Comments();
        XmlFragments.DeclareBasicNamespaces(root);
        var paraIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var done = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        foreach (var (id, comment) in allocation.Written)
        {
            var paraId = comment.ParaId is { } existing && paraIds.Add(existing) ? existing : null;
            W.Comment element;
            if (paraId is not null && comment.SourceXml is { } source && comment.SourceContent is { } original && original.Equals(comment.Content)
                && XmlFragments.Parse<W.Comments>(source).FirstOrDefault() is W.Comment kept)
            {
                element = kept;
            }
            else
            {
                element = new W.Comment { Author = comment.Author, Initials = comment.Initials };
                if (comment.Date is { } date)
                {
                    element.Date = new DateTimeValue(DateTime.SpecifyKind(date, DateTimeKind.Unspecified));
                }

                foreach (var block in writeBlocks(comment.Content.Blocks, part))
                {
                    element.AppendChild(block);
                }

                if (element.Elements<W.Paragraph>().FirstOrDefault() is { } first)
                {
                    // Word's mark at the start of a comment's text (the reference in the comment's own style).
                    var mark = new W.Run(new W.RunProperties(new W.RunStyle { Val = BuiltInStyles.CommentReference }), new W.AnnotationReferenceMark());
                    first.InsertAfter(mark, first.ParagraphProperties);
                }

                if (paraId is not null && element.Elements<W.Paragraph>().LastOrDefault() is { } last)
                {
                    last.ParagraphId = new HexBinaryValue(paraId);
                }
            }

            element.Id = id.ToString(CultureInfo.InvariantCulture);
            if (paraId is not null)
            {
                done[paraId] = comment.Done;
            }

            root.AppendChild(element);
        }

        XmlFragments.RemoveRedundantDeclarations(root);
        part.Comments = root;
        part.Comments.Save();
        TidyThreads(main, paraIds, done);
    }

    /// <summary>Removes the thread entries of comments that are gone and writes the done flags.</summary>
    private static void TidyThreads(MainDocumentPart main, HashSet<string> paraIds, Dictionary<string, bool> done)
    {
        if (main.WordprocessingCommentsExPart?.RootElement is { } extended)
        {
            foreach (var entry in extended.ChildElements.ToList())
            {
                var paraId = Attribute(entry, "paraId");
                if (paraId is null || !paraIds.Contains(paraId))
                {
                    entry.Remove();
                    continue;
                }

                var attribute = entry.GetAttributes().FirstOrDefault(item => item.LocalName == "done");
                if (done.TryGetValue(paraId, out var isDone) && attribute.LocalName is not null)
                {
                    entry.SetAttribute(new OpenXmlAttribute(attribute.Prefix, "done", attribute.NamespaceUri, isDone ? "1" : "0"));
                }
            }

            extended.Save();
        }

        var durable = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (main.WordprocessingCommentsIdsPart?.RootElement is { } ids)
        {
            foreach (var entry in ids.ChildElements.ToList())
            {
                if (Attribute(entry, "paraId") is not { } paraId || !paraIds.Contains(paraId))
                {
                    entry.Remove();
                }
                else if (Attribute(entry, "durableId") is { } durableId)
                {
                    durable.Add(durableId);
                }
            }

            ids.Save();
        }

        if (main.WordCommentsExtensiblePart?.RootElement is { } extensible && main.WordprocessingCommentsIdsPart is not null)
        {
            foreach (var entry in extensible.ChildElements.ToList())
            {
                if (Attribute(entry, "durableId") is not { } durableId || !durable.Contains(durableId))
                {
                    entry.Remove();
                }
            }

            extensible.Save();
        }
    }

    private static string? Attribute(OpenXmlElement element, string localName) =>
        element.GetAttributes().FirstOrDefault(attribute => attribute.LocalName == localName).Value;
}
