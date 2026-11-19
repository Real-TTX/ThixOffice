using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixOffice.Core.Formats.Odf;

/// <summary>
/// An OpenDocument package being read: a ZIP with "mimetype", content.xml, styles.xml, settings.xml, meta.xml and pictures. The
/// XML is read without DTDs or resolvers; a part that is missing is null. A file that is no ZIP or whose XML is broken becomes an
/// <see cref="InvalidDataException"/> with the format's German message (<see cref="Read{T}"/>).
/// </summary>
public sealed class OdfPackage : IDisposable
{
    private readonly ZipArchive _zip;

    private OdfPackage(ZipArchive zip)
    {
        _zip = zip;
        MimeType = ReadMimeType(zip);
    }

    /// <summary>The package's mimetype entry (trimmed), or null.</summary>
    public string? MimeType { get; }

    /// <summary>True when the stream is a ZIP whose mimetype entry is one of <paramref name="mimeTypes"/> (the position is kept).</summary>
    public static bool HasMimeType(Stream stream, params string[] mimeTypes)
    {
        var position = stream.Position;
        try
        {
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
            return ReadMimeType(zip) is { } type && mimeTypes.Contains(type, StringComparer.Ordinal);
        }
        catch (InvalidDataException)
        {
            return false;
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>
    /// Opens the package and runs <paramref name="read"/> on it; every failure of the ZIP or the XML (and a missing content.xml)
    /// becomes an <see cref="InvalidDataException"/> with <paramref name="invalidMessage"/>.
    /// </summary>
    public static T Read<T>(Stream stream, string invalidMessage, Func<OdfPackage, T> read)
    {
        try
        {
            using var package = new OdfPackage(new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true));
            if (package._zip.GetEntry("content.xml") is null)
            {
                throw new InvalidDataException(invalidMessage);
            }

            return read(package);
        }
        catch (Exception exception) when (exception is XmlException or IOException or InvalidOperationException or FormatException
            or ArgumentException or OverflowException or IndexOutOfRangeException or NullReferenceException && exception is not InvalidDataException)
        {
            throw new InvalidDataException(invalidMessage, exception);
        }
    }

    /// <summary>The XML of a part, or null when the package lacks it.</summary>
    public XDocument? Xml(string path)
    {
        if (Entry(path) is not { } entry)
        {
            return null;
        }

        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    /// <summary>The bytes of a part ("Pictures/a.png", "./Pictures/a.png"), or null (also for a part over 256 MB).</summary>
    public byte[]? Bytes(string path)
    {
        if (Entry(path) is not { } entry || entry.Length > 256L * 1024 * 1024)
        {
            return null;
        }

        using var input = entry.Open();
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        return bytes.ToArray();
    }

    public bool Contains(string path) => Entry(path) is not null;

    /// <summary>The paths of every part (sub-documents of embedded objects, pictures …).</summary>
    public IEnumerable<string> Paths => _zip.Entries.Select(entry => entry.FullName);

    public void Dispose() => _zip.Dispose();

    private ZipArchiveEntry? Entry(string path) =>
        string.IsNullOrEmpty(path) || path.Contains("://", StringComparison.Ordinal) ? null : _zip.GetEntry(path.TrimStart('.', '/'));

    private static string? ReadMimeType(ZipArchive zip)
    {
        if (zip.GetEntry("mimetype") is not { } entry)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.ASCII);
        return reader.ReadToEnd().Trim();
    }
}

/// <summary>
/// Writes an OpenDocument package: "mimetype" first and stored (a sniffer reads it at offset 38), the XML parts (UTF-8, no BOM),
/// pictures stored as they are, META-INF/manifest.xml last listing every part. <see cref="AddPicture"/> stores equal bytes once.
/// </summary>
public sealed class OdfPackageWriter(string mimeType)
{
    private readonly List<(string Path, object Content, string MediaType)> _parts = [];
    private readonly Dictionary<string, string> _pictures = new(StringComparer.Ordinal);

    public string MimeType { get; } = mimeType;

    /// <summary>The paths written so far (tests, the manifest).</summary>
    public IEnumerable<string> PartPaths => _parts.Select(part => part.Path);

    public void AddXml(string path, XDocument xml) => _parts.Add((path, xml, "text/xml"));

    public void AddBytes(string path, byte[] bytes, string mediaType) => _parts.Add((path, bytes, mediaType));

    /// <summary>Stores a picture once per content (Pictures/&lt;hash&gt;.&lt;extension&gt;) and returns its path.</summary>
    public string AddPicture(byte[] bytes, string extension)
    {
        var hash = Convert.ToHexString(SHA256.HashData(bytes))[..32];
        if (_pictures.TryGetValue(hash, out var existing))
        {
            return existing;
        }

        extension = extension.TrimStart('.').ToLowerInvariant();
        var path = $"Pictures/{hash}.{(extension.Length == 0 ? "png" : extension)}";
        _pictures[hash] = path;
        AddBytes(path, bytes, MediaTypeOf(extension));
        return path;
    }

    public void WriteTo(Stream stream)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            var mime = zip.CreateEntry("mimetype", CompressionLevel.NoCompression);
            using (var entry = mime.Open())
            {
                entry.Write(Encoding.ASCII.GetBytes(MimeType));
            }

            foreach (var (path, content, _) in _parts)
            {
                switch (content)
                {
                    case XDocument xml:
                        WriteXml(zip, path, xml);
                        break;
                    case byte[] bytes:
                        var part = zip.CreateEntry(path, IsCompressed(path) ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
                        using (var output = part.Open())
                        {
                            output.Write(bytes);
                        }

                        break;
                }
            }

            WriteXml(zip, "META-INF/manifest.xml", Manifest());
        }

        buffer.Position = 0;
        buffer.CopyTo(stream);
    }

    /// <summary>meta.xml with the generator and the date.</summary>
    public static XDocument Meta(string generator) => new(new XDeclaration("1.0", "UTF-8", null),
        new XElement(OfficeNs + "document-meta", Declarations(), new XAttribute(OfficeNs + "version", "1.3"),
            new XElement(OfficeNs + "meta",
                new XElement(MetaNs + "generator", generator),
                new XElement(DcNs + "date", DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture)))));

    /// <summary>The media type of a picture's extension (png when unknown).</summary>
    public static string MediaTypeOf(string extension) => extension.TrimStart('.').ToLowerInvariant() switch
    {
        "jpg" or "jpeg" => "image/jpeg",
        "gif" => "image/gif",
        "bmp" => "image/bmp",
        "svg" => "image/svg+xml",
        "tif" or "tiff" => "image/tiff",
        "wmf" => "image/x-wmf",
        "emf" => "image/x-emf",
        _ => "image/png",
    };

    private XDocument Manifest()
    {
        var root = new XElement(ManifestNs + "manifest", new XAttribute(XNamespace.Xmlns + "manifest", ManifestNs), new XAttribute(ManifestNs + "version", "1.3"),
            new XElement(ManifestNs + "file-entry", new XAttribute(ManifestNs + "full-path", "/"), new XAttribute(ManifestNs + "version", "1.3"), new XAttribute(ManifestNs + "media-type", MimeType)));
        foreach (var (path, _, mediaType) in _parts)
        {
            root.Add(new XElement(ManifestNs + "file-entry", new XAttribute(ManifestNs + "full-path", path), new XAttribute(ManifestNs + "media-type", mediaType)));
        }

        return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
    }

    private static bool IsCompressed(string path) =>
        Path.GetExtension(path).ToLowerInvariant() is ".png" or ".jpg" or ".jpeg" or ".gif";

    private static void WriteXml(ZipArchive zip, string path, XDocument xml)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var output = entry.Open();
        using var writer = XmlWriter.Create(output, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
        xml.Save(writer);
    }
}
