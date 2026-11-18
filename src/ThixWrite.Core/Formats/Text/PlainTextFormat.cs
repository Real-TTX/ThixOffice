using System.Text;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formats.Text;

/// <summary>
/// Plain text (.txt) import and export. Reading detects the encoding: a byte order mark (UTF-8, UTF-16 LE/BE, UTF-32 LE/BE),
/// else UTF-8 when the bytes are valid UTF-8, else Windows-1252 (the encoding of old Windows text files). Every line becomes a
/// paragraph (CRLF, LF and CR all end a line); a form feed becomes a page break. Writing joins paragraphs with CRLF, turns
/// manual line breaks into line ends, keeps page breaks as form feeds and drops embedded objects.
/// </summary>
public static class PlainTextFormat
{
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    static PlainTextFormat() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Windows-1252 ("ANSI" of western European Windows).</summary>
    public static Encoding Windows1252 => Encoding.GetEncoding(1252);

    public static Document Read(Stream stream) => Read(stream, out _);

    public static Document Read(Stream stream, out Encoding detected)
    {
        using var copy = new MemoryStream();
        stream.CopyTo(copy);
        var text = Decode(copy.ToArray(), out detected);
        return FromText(text);
    }

    /// <summary>The text of <paramref name="bytes"/> in the detected encoding (BOM removed).</summary>
    public static string Decode(byte[] bytes, out Encoding detected)
    {
        var (encoding, bomLength) = DetectBom(bytes);
        if (encoding is not null)
        {
            detected = encoding;
            return encoding.GetString(bytes, bomLength, bytes.Length - bomLength);
        }

        try
        {
            var strict = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);
            var text = strict.GetString(bytes);
            detected = Utf8WithoutBom;
            return text;
        }
        catch (DecoderFallbackException)
        {
            detected = Windows1252;
            return Windows1252.GetString(bytes);
        }
    }

    /// <summary>A document with one paragraph per line of <paramref name="text"/>, form feeds as page breaks.</summary>
    public static Document FromText(string text)
    {
        var lines = text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n');
        return new Document(lines.Select(line => new Paragraph(Clean(line))));
    }

    public static void Write(Document document, Stream stream, Encoding? encoding = null)
    {
        var bytes = (encoding ?? Utf8WithoutBom).GetPreamble().Concat((encoding ?? Utf8WithoutBom).GetBytes(ToText(document))).ToArray();
        stream.Write(bytes);
    }

    /// <summary>The text of the document as written to a file (CRLF between paragraphs).</summary>
    public static string ToText(Document document)
    {
        var lines = new List<string>();
        foreach (var block in document.Blocks)
        {
            switch (block)
            {
                case Paragraph paragraph:
                    lines.Add(paragraph.Text
                        .Replace(SpecialCharacters.Object.ToString(), "", StringComparison.Ordinal)
                        .Replace(SpecialCharacters.LineBreak.ToString(), "\r\n", StringComparison.Ordinal)
                        .Replace(SpecialCharacters.SoftHyphen.ToString(), "", StringComparison.Ordinal));
                    break;
                case OpaqueBlock opaque:
                    lines.AddRange(opaque.Lines);
                    break;
            }
        }

        return string.Join("\r\n", lines);
    }

    private static (Encoding? Encoding, int Length) DetectBom(byte[] bytes)
    {
        if (bytes.Length >= 4 && bytes[0] == 0xFF && bytes[1] == 0xFE && bytes[2] == 0 && bytes[3] == 0)
        {
            return (new UTF32Encoding(bigEndian: false, byteOrderMark: true), 4);
        }

        if (bytes.Length >= 4 && bytes[0] == 0 && bytes[1] == 0 && bytes[2] == 0xFE && bytes[3] == 0xFF)
        {
            return (new UTF32Encoding(bigEndian: true, byteOrderMark: true), 4);
        }

        if (bytes.Length >= 3 && bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF)
        {
            return (new UTF8Encoding(encoderShouldEmitUTF8Identifier: true), 3);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFF && bytes[1] == 0xFE)
        {
            return (new UnicodeEncoding(bigEndian: false, byteOrderMark: true), 2);
        }

        if (bytes.Length >= 2 && bytes[0] == 0xFE && bytes[1] == 0xFF)
        {
            return (new UnicodeEncoding(bigEndian: true, byteOrderMark: true), 2);
        }

        return (null, 0);
    }

    /// <summary>Keeps tabs and form feeds (page breaks); other control characters and object characters go.</summary>
    private static string Clean(string line)
    {
        if (!line.Any(character => (character < ' ' && character is not ('\t' or '\f')) || character == SpecialCharacters.Object))
        {
            return line;
        }

        return new string(line.Where(character => (character >= ' ' || character is '\t' or '\f') && character != SpecialCharacters.Object).ToArray());
    }
}
