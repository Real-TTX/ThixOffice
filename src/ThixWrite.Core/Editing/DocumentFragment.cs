using System.Text;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// A piece of a document (the clipboard's rich content): blocks whose first and last are paragraphs. One paragraph = inline
/// content (text without a paragraph mark); several = the first is the end of a paragraph, the last the start of one, with
/// whole blocks in between. It carries the styles and list definitions of its source so that pasting into another document
/// can bring missing styles along and give lists their own instances.
/// </summary>
public sealed class DocumentFragment
{
    public DocumentFragment(IReadOnlyList<Block> blocks, StyleSheet? styles = null, NumberingDefinitions? numbering = null, Guid? sourceDocumentId = null)
    {
        if (blocks.Count == 0 || blocks[0] is not Paragraph || blocks[^1] is not Paragraph)
        {
            throw new ArgumentException("A fragment starts and ends with a paragraph.", nameof(blocks));
        }

        Blocks = blocks;
        Styles = styles;
        Numbering = numbering;
        SourceDocumentId = sourceDocumentId;
    }

    public IReadOnlyList<Block> Blocks { get; }

    public StyleSheet? Styles { get; }

    public NumberingDefinitions? Numbering { get; }

    /// <summary>The <see cref="Document.Id"/> the fragment was copied from (null for plain text and foreign sources).</summary>
    public Guid? SourceDocumentId { get; }

    /// <summary>True for inline content only (no paragraph break inside).</summary>
    public bool IsInline => Blocks.Count == 1;

    public bool IsEmpty => Blocks.Count == 1 && Blocks[0].Length == 0;

    /// <summary>The text for the clipboard's plain-text format: paragraphs separated by CR LF, objects dropped.</summary>
    public string PlainText
    {
        get
        {
            var text = new StringBuilder();
            for (var index = 0; index < Blocks.Count; index++)
            {
                if (index > 0)
                {
                    text.Append("\r\n");
                }

                switch (Blocks[index])
                {
                    case Paragraph paragraph:
                        text.Append(paragraph.Text.Replace(SpecialCharacters.Object.ToString(), "").Replace(SpecialCharacters.LineBreak, '\n'));
                        break;
                    case OpaqueBlock opaque:
                        text.Append(string.Join("\r\n", opaque.Lines));
                        break;
                }
            }

            return text.ToString();
        }
    }

    /// <summary>A fragment of plain text: one paragraph per line (CR LF, LF or CR), no formatting.</summary>
    public static DocumentFragment FromPlainText(string text)
    {
        var lines = text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        return new DocumentFragment(lines.Select(line => (Block)new Paragraph(CleanLine(line))).ToList());
    }

    /// <summary>The content of <paramref name="range"/> of <paramref name="document"/>.</summary>
    public static DocumentFragment Copy(Document document, DocumentRange range)
    {
        var blocks = new List<Block>();
        var start = range.Start;
        var end = range.End;
        for (var index = start.Block; index <= end.Block; index++)
        {
            var block = document.Blocks[index];
            if (block is not Paragraph paragraph)
            {
                blocks.Add(block);
                continue;
            }

            var from = index == start.Block ? start.Offset : 0;
            var to = index == end.Block ? end.Offset : paragraph.Length;
            var sectionBreak = index < end.Block ? paragraph.SectionBreak : null;
            blocks.Add(new Paragraph(paragraph.Slice(from, to), paragraph.Properties, paragraph.MarkProperties, sectionBreak));
        }

        if (blocks[0] is not Paragraph)
        {
            blocks.Insert(0, Paragraph.Empty());
        }

        if (blocks[^1] is not Paragraph)
        {
            blocks.Add(Paragraph.Empty());
        }

        return new DocumentFragment(blocks, document.Styles, document.Numbering, document.Id);
    }

    private static string CleanLine(string line)
    {
        var builder = new StringBuilder(line.Length);
        foreach (var character in line)
        {
            if (character == SpecialCharacters.Tab || character == SpecialCharacters.PageBreak || !char.IsControl(character))
            {
                builder.Append(character == SpecialCharacters.Object ? ' ' : character);
            }
        }

        return builder.ToString();
    }
}
