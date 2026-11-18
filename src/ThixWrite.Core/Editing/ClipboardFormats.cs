using System.Text;
using ThixWrite.Core.Formats.Docx;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Editing;

/// <summary>
/// Reads clipboard data of a foreign format into a fragment (the seam for HTML and RTF from other programs, which come later).
/// </summary>
public interface IClipboardImporter
{
    /// <summary>The clipboard format it reads (<see cref="ClipboardFormats.Html"/>, <see cref="ClipboardFormats.Rtf"/> …).</summary>
    string Format { get; }

    /// <summary>The content as a fragment, or null when the data cannot be read.</summary>
    DocumentFragment? Import(byte[] data);
}

/// <summary>
/// What ThixWrite puts on the clipboard and takes from it, UI-free: the editor control copies these byte arrays to and from
/// the system clipboard. ThixWrite's own rich format is a small docx package of the fragment (styles and lists included), so
/// it is lossless between ThixWrite windows and processes; plain text goes along for every other program.
/// </summary>
public static class ClipboardFormats
{
    /// <summary>ThixWrite's rich fragment (a docx package).</summary>
    public const string Fragment = "application/x-thixwrite-fragment+docx";

    /// <summary>Plain text, UTF-8.</summary>
    public const string PlainText = "text/plain";

    public const string Html = "text/html";

    public const string Rtf = "text/rtf";

    private static readonly List<IClipboardImporter> Importers = [];

    /// <summary>Adds a reader for a foreign format (HTML, RTF); the last one registered for a format wins.</summary>
    public static void Register(IClipboardImporter importer)
    {
        lock (Importers)
        {
            Importers.RemoveAll(existing => existing.Format == importer.Format);
            Importers.Add(importer);
        }
    }

    /// <summary>The formats for copying <paramref name="fragment"/>: the rich fragment and plain text.</summary>
    public static IReadOnlyDictionary<string, byte[]> Export(DocumentFragment fragment) => new Dictionary<string, byte[]>
    {
        [Fragment] = Serialize(fragment),
        [PlainText] = Encoding.UTF8.GetBytes(fragment.PlainText),
    };

    /// <summary>
    /// The best content of clipboard data: ThixWrite's own fragment, else a registered importer's format (HTML, RTF), else plain
    /// text; null when there is nothing ThixWrite can paste.
    /// </summary>
    public static DocumentFragment? Import(IReadOnlyDictionary<string, byte[]> data)
    {
        if (data.TryGetValue(Fragment, out var rich) && TryDeserialize(rich) is { } fragment)
        {
            return fragment;
        }

        List<IClipboardImporter> importers;
        lock (Importers)
        {
            importers = [.. Importers];
        }

        foreach (var importer in importers)
        {
            if (data.TryGetValue(importer.Format, out var bytes) && importer.Import(bytes) is { } imported)
            {
                return imported;
            }
        }

        return data.TryGetValue(PlainText, out var text) ? DocumentFragment.FromPlainText(Encoding.UTF8.GetString(text)) : null;
    }

    /// <summary>The fragment as a docx package.</summary>
    public static byte[] Serialize(DocumentFragment fragment)
    {
        var document = new Document(fragment.Blocks, fragment.Styles ?? BuiltInStyles.CreateDefault(), fragment.Numbering);
        using var stream = new MemoryStream();
        DocxWriter.Write(document, stream);
        return stream.ToArray();
    }

    /// <summary>A fragment from a docx package written by <see cref="Serialize"/> (foreign: pasting adopts styles and lists).</summary>
    public static DocumentFragment? TryDeserialize(byte[] data)
    {
        try
        {
            var document = DocxReader.Read(new MemoryStream(data));
            return new DocumentFragment(document.Blocks.ToList(), document.Styles, document.Numbering);
        }
        catch (Exception exception) when (exception is InvalidDataException or IOException or FormatException or InvalidOperationException)
        {
            return null;
        }
    }
}
