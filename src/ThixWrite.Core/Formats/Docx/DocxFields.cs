using System.Text;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// The fields ThixWrite updates itself (PAGE, NUMPAGES, DATE): a w:fldSimple, or a complex field (w:fldChar begin, w:instrText,
/// separate, the result runs, end) inside one paragraph, become one <see cref="FieldInline"/>. Every other field stays preserved
/// with its result shown. Written back as w:fldSimple with the last result.
/// </summary>
internal static class DocxFields
{
    /// <summary>Replaces the known fields among a paragraph's inlines (as the reader made them) by <see cref="FieldInline"/>s.</summary>
    public static void Collapse(List<Inline> inlines, Action<PreservedKind> uncount)
    {
        for (var index = 0; index < inlines.Count; index++)
        {
            if (inlines[index] is not OpaqueInline { Kind: PreservedKind.Field } field)
            {
                continue;
            }

            if (!field.IsRunContent && field.Xml.StartsWith("<w:fldSimple", StringComparison.Ordinal) && Simple(field) is { } simple)
            {
                inlines[index] = simple;
                uncount(PreservedKind.Field);
                continue;
            }

            if (CharType(field) == "begin" && Complex(inlines, index) is { } complex)
            {
                inlines.RemoveRange(index, complex.Length);
                inlines.Insert(index, complex.Field);
                uncount(PreservedKind.Field);
            }
        }
    }

    /// <summary>The w:fldSimple of a field.</summary>
    public static W.SimpleField Write(FieldInline field, W.Run result) => new(result) { Instruction = field.Instruction };

    private static FieldInline? Simple(OpaqueInline field)
    {
        var instruction = DocxValues.AttributeValue(XmlFragments.Parse<W.Paragraph>(field.Xml).FirstOrDefault() ?? new W.SimpleField(), "instr");
        return instruction is not null && FieldInline.KindOf(instruction) is { } kind
            ? new FieldInline(kind, instruction, field.DisplayText, field.Properties, field.Link)
            : null;
    }

    /// <summary>A complex field from its begin at <paramref name="begin"/>: the field and the number of inlines it takes, or null.</summary>
    private static (FieldInline Field, int Length)? Complex(List<Inline> inlines, int begin)
    {
        var instruction = new StringBuilder();
        var result = new StringBuilder();
        CharacterProperties? resultProperties = null;
        var inResult = false;
        for (var index = begin + 1; index < inlines.Count; index++)
        {
            switch (inlines[index])
            {
                case OpaqueInline { Kind: PreservedKind.Field } part when CharType(part) is { } type:
                    if (type == "begin")
                    {
                        return null; // a nested field: kept as it is
                    }

                    if (type == "separate")
                    {
                        inResult = true;
                        continue;
                    }

                    var text = instruction.ToString();
                    if (FieldInline.KindOf(text) is not { } kind)
                    {
                        return null;
                    }

                    var properties = resultProperties ?? inlines[begin].Properties;
                    return (new FieldInline(kind, text, result.ToString(), properties, inlines[begin].Link), index - begin + 1);
                case OpaqueInline { Kind: PreservedKind.Field } code when !inResult:
                    instruction.Append(XmlFragments.Parse<W.Run>(code.Xml).FirstOrDefault()?.InnerText);
                    break;
                case TextRun run when inResult:
                    result.Append(run.Text);
                    resultProperties ??= run.Properties;
                    break;
                case OpaqueInline { IsMarker: true }:
                    break;
                default:
                    return null; // anything else inside a field (a picture, a tracked change): kept as it is
            }
        }

        return null;
    }

    private static string? CharType(OpaqueInline inline)
    {
        if (!inline.Xml.StartsWith("<w:fldChar", StringComparison.Ordinal))
        {
            return null;
        }

        var element = XmlFragments.Parse<W.Run>(inline.Xml).FirstOrDefault();
        return element is null ? null : DocxValues.AttributeValue(element, "fldCharType");
    }
}
