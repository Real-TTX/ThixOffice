using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// A TOC field as Word writes it – w:fldChar begin, w:instrText " TOC \o … ", separate in the first paragraph, the entries (with
/// their PAGEREF fields and links to "_Toc…" bookmarks), w:fldChar end in a later paragraph – becomes a <see cref="TocMarkInline"/>
/// pair around ordinary paragraphs. The fields inside the entries stay as they are (updating the table makes them anew) and are
/// not reported. Word's "Inhaltsverzeichnis" content control around it is unwrapped.
/// </summary>
internal static class DocxToc
{
    /// <summary>True for Word's content control around a table of contents (docPartGallery "Table of Contents").</summary>
    public static bool IsTocControl(W.SdtBlock sdt) =>
        sdt.SdtProperties?.Descendants<W.DocPartGallery>().Any(gallery => gallery.Val?.Value is "Table of Contents") == true;

    /// <summary>Finds TOC fields in the body's paragraphs and replaces their characters by marks.</summary>
    public static void Collapse(List<Block> blocks, Action<PreservedKind> uncount)
    {
        for (var block = 0; block < blocks.Count; block++)
        {
            if (blocks[block] is not Paragraph paragraph)
            {
                continue;
            }

            for (var index = 0; index < paragraph.Inlines.Length; index++)
            {
                if (CharType(paragraph.Inlines[index]) != "begin" || Start(paragraph, index) is not { } start)
                {
                    continue;
                }

                if (FindEnd(blocks, block, start.After) is not { } end)
                {
                    return;
                }

                // The marks in: the start replaces begin … separate, the end replaces the end character.
                var inlines = paragraph.Inlines.ToList();
                inlines.RemoveRange(index, start.After - index);
                inlines.Insert(index, new TocMarkInline(false, start.Instruction, paragraph.Inlines[index].Properties));
                var shift = start.After - index - 1;
                if (end.Block == block)
                {
                    var at = end.Index - shift;
                    inlines[at] = new TocMarkInline(true, "", inlines[at].Properties);
                    blocks[block] = paragraph.WithInlines(inlines);
                }
                else
                {
                    blocks[block] = paragraph.WithInlines(inlines);
                    var last = (Paragraph)blocks[end.Block];
                    blocks[end.Block] = last.WithInlines(last.Inlines.SetItem(end.Index, new TocMarkInline(true, "", last.Inlines[end.Index].Properties)));
                }

                // The TOC field and the fields inside it are not "unsupported" any more.
                uncount(PreservedKind.Field);
                for (var counted = 0; counted < end.Nested; counted++)
                {
                    uncount(PreservedKind.Field);
                }

                return; // one table of contents
            }
        }
    }

    /// <summary>The field code of a TOC field starting at inline <paramref name="begin"/> and the index after its separate character, or null.</summary>
    private static (string Instruction, int After)? Start(Paragraph paragraph, int begin)
    {
        var instruction = new System.Text.StringBuilder();
        for (var index = begin + 1; index < paragraph.Inlines.Length; index++)
        {
            var inline = paragraph.Inlines[index];
            if (CharType(inline) == "separate")
            {
                var text = instruction.ToString();
                return TableOfContents.IsToc(text) ? (text, index + 1) : null;
            }

            if (inline is OpaqueInline { Kind: PreservedKind.Field } code && code.Xml.StartsWith("<w:instrText", StringComparison.Ordinal))
            {
                instruction.Append(XmlFragments.Parse<W.Run>(code.Xml).FirstOrDefault()?.InnerText);
                continue;
            }

            if (inline is OpaqueInline { IsMarker: true })
            {
                continue;
            }

            return null;
        }

        return null;
    }

    /// <summary>The end character matching a field whose result starts at inline <paramref name="from"/> of block <paramref name="block"/>; nested fields counted.</summary>
    private static (int Block, int Index, int Nested)? FindEnd(List<Block> blocks, int block, int from)
    {
        var depth = 0;
        var nested = 0;
        for (var current = block; current < blocks.Count; current++)
        {
            if (blocks[current] is not Paragraph paragraph)
            {
                continue;
            }

            for (var index = current == block ? from : 0; index < paragraph.Inlines.Length; index++)
            {
                switch (CharType(paragraph.Inlines[index]))
                {
                    case "begin":
                        depth++;
                        nested++;
                        break;
                    case "end" when depth == 0:
                        return (current, index, nested);
                    case "end":
                        depth--;
                        break;
                }
            }
        }

        return null;
    }

    private static string? CharType(Inline inline)
    {
        if (inline is not OpaqueInline { Kind: PreservedKind.Field } field || !field.Xml.StartsWith("<w:fldChar", StringComparison.Ordinal))
        {
            return null;
        }

        var element = XmlFragments.Parse<W.Run>(field.Xml).FirstOrDefault();
        return element is null ? null : DocxValues.AttributeValue(element, "fldCharType");
    }
}
