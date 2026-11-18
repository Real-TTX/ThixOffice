using System.IO.Packaging;
using System.Text;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using M = DocumentFormat.OpenXml.Math;
using W = DocumentFormat.OpenXml.Wordprocessing;
using Wp = DocumentFormat.OpenXml.Drawing.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Reads a .docx (Word 2007+, LibreOffice, …) into the model. Tolerant: odd values are ignored, missing parts give defaults;
/// only a stream that is not a Word package is refused (<see cref="InvalidDataException"/>).
/// <para>
/// Preserve-unknown: content the model does not support is kept as its original XML in the model – inline elements as
/// <see cref="OpaqueInline"/> (one character in the text, shown by its visible text or as a placeholder), body elements as
/// <see cref="OpaqueBlock"/> – and the original package is kept in <see cref="Document.Package"/>, so that
/// <see cref="DocxWriter"/> writes everything back with its parts and relationships. <see cref="Document.UnsupportedContent"/>
/// lists what the UI should warn about.
/// </para>
/// </summary>
public static class DocxReader
{
    /// <summary>The message when a file is not a Word document.</summary>
    public const string NotADocxMessage = "Die Datei ist kein gültiges Word-Dokument (.docx).";

    public static Document Read(Stream stream)
    {
        var bytes = ReadAll(stream);
        try
        {
            using var package = WordprocessingDocument.Open(new MemoryStream(bytes, writable: false), false);
            return Read(package, bytes);
        }
        catch (Exception exception) when (exception is OpenXmlPackageException or FileFormatException or InvalidDataException
                                              or IOException or System.Xml.XmlException or NotSupportedException)
        {
            throw new InvalidDataException(NotADocxMessage, exception);
        }
    }

    private static Document Read(WordprocessingDocument package, byte[] bytes)
    {
        var main = package.MainDocumentPart ?? throw new InvalidDataException(NotADocxMessage);
        var body = main.Document?.Body;
        var context = new ReadContext(main);
        var blocks = new List<Block>();
        SectionProperties? finalSection = null;
        if (body is not null)
        {
            foreach (var child in body.ChildElements)
            {
                ReadBodyChild(child, blocks, context, ref finalSection);
            }
        }

        if (context.PendingMarkers.Count > 0)
        {
            blocks.Add(new Paragraph(context.TakeMarkers()));
        }

        return new Document(
            blocks,
            DocxStyles.ReadStyles(main.StyleDefinitionsPart?.Styles),
            DocxStyles.ReadNumbering(main.NumberingDefinitionsPart?.Numbering),
            finalSection ?? SectionProperties.Default,
            DocxStyles.ReadTheme(main.ThemePart),
            DocxStyles.ReadSettings(main.DocumentSettingsPart?.Settings))
        {
            UnsupportedContent = context.Notes(),
            Package = new PreservedPackage(bytes),
        };
    }

    private static void ReadBodyChild(OpenXmlElement child, List<Block> blocks, ReadContext context, ref SectionProperties? finalSection)
    {
        switch (child)
        {
            case W.Paragraph paragraph:
                blocks.Add(ReadParagraph(paragraph, context));
                return;
            case W.SectionProperties section:
                finalSection = DocxPropertyReader.ReadSection(section);
                return;
            case W.BookmarkStart or W.BookmarkEnd or W.CommentRangeStart or W.CommentRangeEnd or W.PermStart or W.PermEnd
                or W.MoveFromRangeStart or W.MoveFromRangeEnd or W.MoveToRangeStart or W.MoveToRangeEnd or W.ProofError:
                // Range markers between paragraphs move to the start of the next paragraph.
                if (child is not W.ProofError)
                {
                    context.PendingMarkers.Add(Marker(child, KindOfMarker(child)));
                }

                return;
        }

        var kind = child switch
        {
            W.Table => PreservedKind.Table,
            W.SdtBlock => PreservedKind.ContentControl,
            W.AltChunk => PreservedKind.EmbeddedObject,
            W.CustomXmlBlock => PreservedKind.Markup,
            M.Paragraph => PreservedKind.Math,
            _ => PreservedKind.Other,
        };
        context.Count(kind);
        blocks.Add(new OpaqueBlock(child.OuterXml, kind, LinesOf(child)));
    }

    private static IReadOnlyList<string> LinesOf(OpenXmlElement element)
    {
        if (element is W.Table table)
        {
            return table.Elements<W.TableRow>()
                .Select(row => string.Join("\t", row.Elements<W.TableCell>().Select(cell =>
                    string.Join(" ", cell.Elements<W.Paragraph>().Select(XmlFragments.VisibleText)))))
                .ToList();
        }

        var paragraphs = element.Descendants<W.Paragraph>().Where(paragraph => !paragraph.Ancestors<W.Paragraph>().Any()).ToList();
        if (paragraphs.Count == 0)
        {
            var text = XmlFragments.VisibleText(element);
            return text.Length == 0 ? [] : [text];
        }

        return paragraphs.Select(XmlFragments.VisibleText).ToList();
    }

    private static Paragraph ReadParagraph(W.Paragraph element, ReadContext context)
    {
        var properties = element.ParagraphProperties;
        var inlines = new List<Inline>(context.TakeMarkers());
        foreach (var child in element.ChildElements)
        {
            if (child is W.ParagraphProperties)
            {
                continue;
            }

            ReadParagraphChild(child, inlines, null, context);
        }

        return new Paragraph(
            inlines,
            DocxPropertyReader.ReadParagraph(properties),
            DocxPropertyReader.ReadCharacter(properties?.ParagraphMarkRunProperties),
            properties?.SectionProperties is { } section ? DocxPropertyReader.ReadSection(section) : null);
    }

    private static void ReadParagraphChild(OpenXmlElement child, List<Inline> inlines, Hyperlink? link, ReadContext context)
    {
        switch (child)
        {
            case W.Run run:
                ReadRun(run, inlines, link, context);
                return;
            case W.Hyperlink hyperlink:
                var target = ReadHyperlink(hyperlink, context);
                foreach (var linked in hyperlink.ChildElements)
                {
                    ReadParagraphChild(linked, inlines, target, context);
                }

                return;
            case W.ProofError:
                return;
            case W.BookmarkStart or W.BookmarkEnd or W.CommentRangeStart or W.CommentRangeEnd or W.PermStart or W.PermEnd
                or W.MoveFromRangeStart or W.MoveFromRangeEnd or W.MoveToRangeStart or W.MoveToRangeEnd:
                inlines.Add(Marker(child, KindOfMarker(child)) with { Link = link });
                return;
        }

        var (kind, visible) = child switch
        {
            W.InsertedRun or W.MoveToRun => (PreservedKind.TrackedChange, true),
            W.DeletedRun or W.MoveFromRun => (PreservedKind.TrackedChange, false),
            W.SimpleField => (PreservedKind.Field, true),
            W.SdtRun => (PreservedKind.ContentControl, true),
            W.CustomXmlRun => (PreservedKind.Markup, true),
            _ when child.LocalName == "smartTag" => (PreservedKind.Markup, true),
            M.OfficeMath or M.Paragraph => (PreservedKind.Math, false),
            _ => (PreservedKind.Other, false),
        };
        context.Count(kind);
        var properties = DocxPropertyReader.ReadCharacter(child.Descendants<W.RunProperties>().FirstOrDefault());
        var text = visible ? XmlFragments.VisibleText(child) : "";
        inlines.Add(new OpaqueInline(child.OuterXml, kind, false, properties, text, Link: link));
    }

    private static void ReadRun(W.Run run, List<Inline> inlines, Hyperlink? link, ReadContext context)
    {
        var properties = DocxPropertyReader.ReadCharacter(run.RunProperties);
        var text = new StringBuilder();

        void Flush()
        {
            if (text.Length > 0)
            {
                inlines.Add(new TextRun(text.ToString(), properties, link));
                text.Clear();
            }
        }

        void AddOpaque(OpenXmlElement element, PreservedKind kind, double width = 0, double height = 0)
        {
            Flush();
            inlines.Add(new OpaqueInline(element.OuterXml, kind, true, properties, "", width, height, link));
        }

        foreach (var child in run.ChildElements)
        {
            switch (child)
            {
                case W.RunProperties or W.LastRenderedPageBreak:
                    break;
                case W.Text value:
                    text.Append(Clean(value.Text));
                    break;
                case W.TabChar:
                    text.Append(SpecialCharacters.Tab);
                    break;
                case W.Break when DocxValues.AttributeValue(child, "type") == "page":
                    text.Append(SpecialCharacters.PageBreak);
                    break;
                case W.Break when DocxValues.AttributeValue(child, "type") == "column":
                    context.Count(PreservedKind.ColumnBreak);
                    AddOpaque(child, PreservedKind.ColumnBreak);
                    break;
                case W.Break or W.CarriageReturn:
                    text.Append(SpecialCharacters.LineBreak);
                    break;
                case W.SoftHyphen:
                    text.Append(SpecialCharacters.SoftHyphen);
                    break;
                case W.NoBreakHyphen:
                    text.Append(SpecialCharacters.NonBreakingHyphen);
                    break;
                case W.SymbolChar symbol when ReadSymbol(symbol) is { } character:
                    Flush();
                    var font = DocxValues.AttributeValue(symbol, "font");
                    inlines.Add(new TextRun(character, font is null ? properties : properties.WithFont(font), link));
                    break;
                case W.FieldChar:
                    if (DocxValues.AttributeValue(child, "fldCharType") == "begin")
                    {
                        context.Count(PreservedKind.Field);
                    }

                    AddOpaque(child, PreservedKind.Field);
                    break;
                case W.FieldCode:
                    AddOpaque(child, PreservedKind.Field);
                    break;
                case W.Drawing or W.Picture or AlternateContent:
                    context.Count(PreservedKind.Drawing);
                    var (width, height) = InlineSize(child);
                    AddOpaque(child, PreservedKind.Drawing, width, height);
                    break;
                case W.EmbeddedObject:
                    context.Count(PreservedKind.EmbeddedObject);
                    AddOpaque(child, PreservedKind.EmbeddedObject);
                    break;
                case W.FootnoteReference:
                    context.Count(PreservedKind.Footnote);
                    AddOpaque(child, PreservedKind.Footnote);
                    break;
                case W.EndnoteReference:
                    context.Count(PreservedKind.Endnote);
                    AddOpaque(child, PreservedKind.Endnote);
                    break;
                case W.CommentReference:
                    context.Count(PreservedKind.Comment);
                    AddOpaque(child, PreservedKind.Comment);
                    break;
                case W.PositionalTab:
                    context.Count(PreservedKind.Markup);
                    AddOpaque(child, PreservedKind.Markup);
                    break;
                default:
                    context.Count(PreservedKind.Other);
                    AddOpaque(child, PreservedKind.Other);
                    break;
            }
        }

        Flush();
    }

    private static Hyperlink ReadHyperlink(W.Hyperlink hyperlink, ReadContext context)
    {
        var id = hyperlink.Id?.Value;
        string? uri = null;
        if (id is not null)
        {
            uri = context.Main.HyperlinkRelationships.FirstOrDefault(relationship => relationship.Id == id)?.Uri.OriginalString;
        }

        var history = DocxValues.AttributeValue(hyperlink, "history") is { } value && value.Trim() is "1" or "true" or "on";
        return new Hyperlink(uri, DocxValues.AttributeValue(hyperlink, "anchor"), DocxValues.AttributeValue(hyperlink, "tooltip"), history);
    }

    private static string? ReadSymbol(W.SymbolChar symbol)
    {
        var code = DocxValues.AttributeValue(symbol, "char");
        if (code is null || !int.TryParse(code, System.Globalization.NumberStyles.HexNumber, null, out var value) || value is <= 0 or > 0xFFFF)
        {
            return null;
        }

        var character = (char)value;
        return char.IsSurrogate(character) || char.IsControl(character) ? null : character.ToString();
    }

    private static (double Width, double Height) InlineSize(OpenXmlElement element)
    {
        var inline = element.Descendants<Wp.Inline>().FirstOrDefault();
        var extent = inline?.Extent;
        if (extent is null)
        {
            return (0, 0);
        }

        var width = DocxValues.ParseInteger(extent.Cx?.InnerText) ?? 0;
        var height = DocxValues.ParseInteger(extent.Cy?.InnerText) ?? 0;
        return (DocxValues.EmuToPoints(Math.Max(0, width)), DocxValues.EmuToPoints(Math.Max(0, height)));
    }

    private static OpaqueInline Marker(OpenXmlElement element, PreservedKind kind) =>
        new(element.OuterXml, kind, false, CharacterProperties.Empty);

    private static PreservedKind KindOfMarker(OpenXmlElement element) => element switch
    {
        W.BookmarkStart or W.BookmarkEnd => PreservedKind.Bookmark,
        W.CommentRangeStart or W.CommentRangeEnd => PreservedKind.Comment,
        W.MoveFromRangeStart or W.MoveFromRangeEnd or W.MoveToRangeStart or W.MoveToRangeEnd => PreservedKind.TrackedChange,
        _ => PreservedKind.Markup,
    };

    /// <summary>Removes characters the model gives a meaning (and XML-invalid ones) from text read from w:t.</summary>
    private static string Clean(string text)
    {
        if (!text.Any(character => character < ' ' || character == SpecialCharacters.Object))
        {
            return text;
        }

        var builder = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            if (character == '\t')
            {
                builder.Append(character);
            }
            else if (character is '\n' or '\r')
            {
                builder.Append(' ');
            }
            else if (character >= ' ' && character != SpecialCharacters.Object)
            {
                builder.Append(character);
            }
        }

        return builder.ToString();
    }

    private static byte[] ReadAll(Stream stream)
    {
        if (stream is MemoryStream memory && memory.Position == 0)
        {
            return memory.ToArray();
        }

        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        return copy.ToArray();
    }

    private sealed class ReadContext(MainDocumentPart main)
    {
        private readonly Dictionary<PreservedKind, int> _counts = [];

        public MainDocumentPart Main { get; } = main;

        public List<Inline> PendingMarkers { get; } = [];

        public void Count(PreservedKind kind)
        {
            if (kind == PreservedKind.Bookmark)
            {
                return;
            }

            _counts[kind] = _counts.GetValueOrDefault(kind) + 1;
        }

        public IReadOnlyList<Inline> TakeMarkers()
        {
            var markers = PendingMarkers.ToList();
            PendingMarkers.Clear();
            return markers;
        }

        public IReadOnlyList<UnsupportedContent> Notes() =>
            _counts.OrderBy(pair => pair.Key).Select(pair => new UnsupportedContent(pair.Key, pair.Value)).ToList();
    }
}
