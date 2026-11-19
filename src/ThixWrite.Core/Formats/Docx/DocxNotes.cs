using System.Globalization;
using System.Text.RegularExpressions;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// footnotes.xml and endnotes.xml: the notes of the model are written in the order of their references, each with the id it was
/// read with when that is still free (else a new one); the separators of the original part stay, and so do original notes that
/// preserved content (a reference inside a tracked change …) still points at. A document without such a part gets one with Word's
/// two separators when it has notes.
/// </summary>
internal static partial class DocxNotes
{
    /// <summary>The ids of the notes written into the body, allocated while it is written.</summary>
    public sealed class Allocation(MainDocumentPart main, Document? document)
    {
        private readonly Dictionary<NoteKind, HashSet<long>> _used = new() { [NoteKind.Footnote] = [], [NoteKind.Endnote] = [] };
        private Dictionary<NoteKind, HashSet<long>>? _kept;
        private readonly Dictionary<NoteKind, long> _next = [];

        public List<(NoteKind Kind, long Id, NoteInline Note)> Written { get; } = [];

        /// <summary>The ids of original notes that preserved XML references (they stay in the part).</summary>
        public HashSet<long> Kept(NoteKind kind)
        {
            _kept ??= FindKept(document);
            return _kept[kind];
        }

        public long Allocate(NoteInline note)
        {
            var used = _used[note.Kind];
            var kept = Kept(note.Kind);
            long id;
            if (long.TryParse(note.SourceId, NumberStyles.Integer, CultureInfo.InvariantCulture, out var source) && source > 0 && !used.Contains(source) && !kept.Contains(source))
            {
                id = source;
            }
            else
            {
                if (!_next.TryGetValue(note.Kind, out var next))
                {
                    next = Math.Max(1, ExistingIds(main, note.Kind).Concat(kept).Concat(SourceIds(document, note.Kind)).DefaultIfEmpty(0).Max() + 1);
                }

                while (used.Contains(next) || kept.Contains(next))
                {
                    next++;
                }

                id = next;
                _next[note.Kind] = next + 1;
            }

            used.Add(id);
            Written.Add((note.Kind, id, note));
            return id;
        }
    }

    /// <summary>Writes both parts (only what is needed: a part is created when there are notes of its kind).</summary>
    public static void Write(Document document, MainDocumentPart main, Allocation allocation, Func<IReadOnlyList<Block>, OpenXmlPart, IEnumerable<OpenXmlElement>> writeBlocks)
    {
        foreach (var kind in new[] { NoteKind.Footnote, NoteKind.Endnote })
        {
            var notes = allocation.Written.Where(item => item.Kind == kind).ToList();
            OpenXmlPart? part = kind == NoteKind.Footnote ? main.FootnotesPart : main.EndnotesPart;
            if (part is null && notes.Count == 0)
            {
                continue;
            }

            part ??= kind == NoteKind.Footnote ? main.AddNewPart<FootnotesPart>() : main.AddNewPart<EndnotesPart>();
            OpenXmlCompositeElement? original = part switch
            {
                FootnotesPart footnotes => footnotes.Footnotes,
                EndnotesPart endnotes => endnotes.Endnotes,
                _ => null,
            };
            OpenXmlCompositeElement root = kind == NoteKind.Footnote ? new W.Footnotes() : new W.Endnotes();
            XmlFragments.DeclareBasicNamespaces(root);
            var kept = allocation.Kept(kind);
            var separators = original?.ChildElements.Where(child => DocxValues.AttributeValue(child, "type") is { } type && type != "normal").ToList() ?? [];
            if (separators.Count == 0)
            {
                separators = [Separator(kind, -1, new W.SeparatorMark(), W.FootnoteEndnoteValues.Separator), Separator(kind, 0, new W.ContinuationSeparatorMark(), W.FootnoteEndnoteValues.ContinuationSeparator)];
            }

            foreach (var separator in separators)
            {
                root.AppendChild(separator.CloneNode(true));
            }

            foreach (var child in original?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
            {
                if (DocxValues.AttributeValue(child, "type") is null or "normal" && long.TryParse(DocxValues.AttributeValue(child, "id"), out var id) && kept.Contains(id))
                {
                    root.AppendChild(child.CloneNode(true));
                }
            }

            foreach (var (_, id, note) in notes)
            {
                OpenXmlCompositeElement element = kind == NoteKind.Footnote ? new W.Footnote { Id = id } : new W.Endnote { Id = id };
                foreach (var block in writeBlocks(note.Content.Blocks, part))
                {
                    element.AppendChild(block);
                }

                root.AppendChild(element);
            }

            XmlFragments.RemoveRedundantDeclarations(root);
            if (part is FootnotesPart footnotesPart)
            {
                footnotesPart.Footnotes = (W.Footnotes)root;
                footnotesPart.Footnotes.Save();
            }
            else if (part is EndnotesPart endnotesPart)
            {
                endnotesPart.Endnotes = (W.Endnotes)root;
                endnotesPart.Endnotes.Save();
            }
        }
    }

    private static OpenXmlElement Separator(NoteKind kind, long id, OpenXmlElement mark, W.FootnoteEndnoteValues type)
    {
        var paragraph = new W.Paragraph(
            new W.ParagraphProperties(new W.SpacingBetweenLines { After = "0", Line = "240", LineRule = W.LineSpacingRuleValues.Auto }),
            new W.Run(mark));
        return kind == NoteKind.Footnote
            ? new W.Footnote(paragraph) { Type = type, Id = id }
            : new W.Endnote(paragraph) { Type = type, Id = id };
    }

    private static IEnumerable<long> ExistingIds(MainDocumentPart main, NoteKind kind)
    {
        OpenXmlCompositeElement? root = kind == NoteKind.Footnote ? main.FootnotesPart?.Footnotes : main.EndnotesPart?.Endnotes;
        foreach (var child in root?.ChildElements ?? Enumerable.Empty<OpenXmlElement>())
        {
            if (long.TryParse(DocxValues.AttributeValue(child, "id"), out var id))
            {
                yield return id;
            }
        }
    }

    private static IEnumerable<long> SourceIds(Document? document, NoteKind kind) =>
        document is null ? [] : NoteNumbering.All(document).Where(item => item.Note.Kind == kind)
            .Select(item => long.TryParse(item.Note.SourceId, out var id) ? id : 0);

    private static Dictionary<NoteKind, HashSet<long>> FindKept(Document? document)
    {
        var kept = new Dictionary<NoteKind, HashSet<long>> { [NoteKind.Footnote] = [], [NoteKind.Endnote] = [] };
        if (document is null)
        {
            return kept;
        }

        IEnumerable<string> xml = document.Blocks.OfType<OpaqueBlock>().Select(block => block.Xml)
            .Concat(document.Blocks.OfType<Paragraph>().SelectMany(paragraph => paragraph.Inlines).OfType<OpaqueInline>().Select(inline => inline.Xml));
        foreach (var fragment in xml)
        {
            if (!fragment.Contains("noteReference", StringComparison.Ordinal))
            {
                continue;
            }

            foreach (Match match in ReferencePattern().Matches(fragment))
            {
                var kind = match.Groups[1].Value == "end" ? NoteKind.Endnote : NoteKind.Footnote;
                if (long.TryParse(match.Groups[2].Value, out var id))
                {
                    kept[kind].Add(id);
                }
            }
        }

        return kept;
    }

    [GeneratedRegex("<w:(foot|end)noteReference\\b[^>]*\\bw:id=\"(-?\\d+)\"")]
    private static partial Regex ReferencePattern();
}
