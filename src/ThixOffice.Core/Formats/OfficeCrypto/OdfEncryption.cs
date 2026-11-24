using System.Globalization;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using ThixOffice.Core.Formats.Odf;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// OpenDocument's password encryption (ODF 1.2 Part 3, section 3.4 – what LibreOffice's "Mit Kennwort speichern" writes since 3.4):
/// every part of the package except "mimetype" and META-INF/manifest.xml is deflated, then encrypted with AES-CBC (W3C padding) under
/// a key from PBKDF2-HMAC-SHA1 over the SHA-256 (older: SHA-1) of the UTF-8 password, with a salt and an initialisation vector of its
/// own, and stored; manifest:encryption-data in the manifest describes each part and holds the checksum (SHA-256 or SHA-1 of the first
/// 1024 deflated bytes) that tells a wrong password. Both directions transform a whole package: <see cref="Decrypt"/> gives the plain
/// package the app's reader reads, <see cref="Encrypt"/> turns the plain package the app's writer wrote into an encrypted one – the
/// readers and writers never see the encryption. Blowfish (OpenOffice.org, before LibreOffice 3.4) and LibreOffice 24.2's newer
/// encryption of the whole package (AES-GCM, Argon2id) are recognized and refused in words.
/// </summary>
public static class OdfEncryption
{
    /// <summary>LibreOffice's iteration count since 6.x (PBKDF2-HMAC-SHA1).</summary>
    public const int IterationCount = 100_000;

    internal const string Sha256OneK = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#sha256-1k";
    internal const string Sha1OneK = "SHA1/1K";
    internal const string Sha1OneKUrn = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#sha1-1k";
    internal const string Aes256Cbc = "http://www.w3.org/2001/04/xmlenc#aes256-cbc";
    internal const string Aes192Cbc = "http://www.w3.org/2001/04/xmlenc#aes192-cbc";
    internal const string Aes128Cbc = "http://www.w3.org/2001/04/xmlenc#aes128-cbc";
    internal const string BlowfishCfb = "Blowfish CFB";
    internal const string BlowfishUrn = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#blowfish";
    internal const string StartKeySha256 = "http://www.w3.org/2000/09/xmldsig#sha256";
    internal const string StartKeySha256Enc = "http://www.w3.org/2001/04/xmlenc#sha256";
    internal const string StartKeySha1 = "SHA1";
    internal const string StartKeySha1Dsig = "http://www.w3.org/2000/09/xmldsig#sha1";
    internal const string Pbkdf2 = "PBKDF2";
    internal const string Pbkdf2Urn = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#pbkdf2";

    internal const string BlowfishText = "Diese OpenDocument-Datei ist mit einem älteren Verfahren (Blowfish) verschlüsselt, das nicht unterstützt wird. "
        + "Öffnen Sie sie in LibreOffice und speichern Sie sie dort erneut mit Kennwort – LibreOffice verschlüsselt sie dann mit AES.";

    internal const string ModernText = "Diese OpenDocument-Datei ist mit einem neueren Verfahren von LibreOffice verschlüsselt (AES-GCM, Argon2), "
        + "das noch nicht unterstützt wird. Speichern Sie sie in LibreOffice im Format ODF 1.2 mit Kennwort "
        + "(Extras › Optionen › Laden/Speichern › Allgemein › ODF-Formatversion) oder als .docx, .xlsx oder .pptx mit Kennwort.";

    internal const string OtherText = "Diese OpenDocument-Datei ist mit einem Verfahren verschlüsselt, das nicht unterstützt wird. "
        + "Entfernen Sie das Kennwort in dem Programm, mit dem die Datei erstellt wurde.";

    private const string DamagedText = "Die verschlüsselte OpenDocument-Datei ist beschädigt.";
    private const string ManifestPath = "META-INF/manifest.xml";
    private const string MimeTypePath = "mimetype";
    private const int ChecksumLength = 1024;
    private const int MaxIterations = 10_000_000;
    private const long MaxPartLength = 1L << 30;

    private static readonly XNamespace ManifestNs = OdfXml.ManifestNs;

    /// <summary>
    /// What kind of OpenDocument encryption <paramref name="file"/> carries: <see cref="EncryptedFileKind.None"/> for anything else (no
    /// ZIP, no manifest, no manifest:encryption-data). The position is not kept (the caller does).
    /// </summary>
    internal static EncryptedFileKind KindOf(Stream file)
    {
        file.Position = 0;
        Span<byte> head = stackalloc byte[4];
        if (file.ReadAtLeast(head, 4, throwOnEndOfStream: false) < 4 || head[0] != (byte)'P' || head[1] != (byte)'K')
        {
            return EncryptedFileKind.None;
        }

        file.Position = 0;
        try
        {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            return ManifestOf(zip) is { } manifest ? KindOf(manifest) : EncryptedFileKind.None;
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or NotSupportedException)
        {
            return EncryptedFileKind.None;
        }
    }

    /// <summary>The refusal of an OpenDocument kind that cannot be opened (null for the others).</summary>
    internal static BusinessException? Refusal(EncryptedFileKind kind) => kind switch
    {
        EncryptedFileKind.OdfBlowfish => new BusinessException(BlowfishText),
        EncryptedFileKind.OdfModern => new BusinessException(ModernText),
        EncryptedFileKind.OdfOther => new BusinessException(OtherText),
        _ => null,
    };

    /// <summary>True when <paramref name="package"/> is an OpenDocument package (a ZIP with an OpenDocument "mimetype" and a manifest).</summary>
    public static bool IsOdfPackage(ReadOnlySpan<byte> package)
    {
        if (package.Length < 4 || package[0] != (byte)'P' || package[1] != (byte)'K')
        {
            return false;
        }

        try
        {
            using var zip = new ZipArchive(new MemoryStream(package.ToArray(), writable: false), ZipArchiveMode.Read);
            return MimeTypeOf(zip) is { } type && type.StartsWith("application/vnd.oasis.opendocument.", StringComparison.Ordinal)
                && zip.GetEntry(ManifestPath) is not null;
        }
        catch (InvalidDataException)
        {
            return false;
        }
    }

    /// <summary>
    /// The plain package inside the encrypted OpenDocument <paramref name="file"/>: the encrypted parts decrypted and inflated, the others
    /// as they are, the manifest without its encryption data. A wrong password is a <see cref="WrongPasswordException"/> (the checksum of
    /// the first encrypted part decides); a kind that cannot be opened is a <see cref="BusinessException"/>; a damaged file an
    /// <see cref="InvalidDataException"/>.
    /// </summary>
    public static byte[] Decrypt(Stream file, string password)
    {
        ArgumentNullException.ThrowIfNull(file);
        ArgumentNullException.ThrowIfNull(password);
        file.Position = 0;
        using var zip = OpenZip(file);
        var manifest = ManifestOf(zip) ?? throw new InvalidDataException(DamagedText);
        var kind = KindOf(manifest);
        if (Refusal(kind) is { } refusal)
        {
            throw refusal;
        }

        if (kind != EncryptedFileKind.OdfEncrypted)
        {
            throw new BusinessException(OfficeEncryption.NotEncryptedText);
        }

        var encryption = new Dictionary<string, (XElement Entry, XElement Data)>(StringComparer.Ordinal);
        foreach (var entry in manifest.Root!.Elements(ManifestNs + "file-entry"))
        {
            if (entry.Element(ManifestNs + "encryption-data") is { } data && (string?)entry.Attribute(ManifestNs + "full-path") is { Length: > 0 } path)
            {
                encryption[path] = (entry, data);
            }
        }

        var keys = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var verified = false;
        var parts = new List<(string Path, byte[] Bytes, bool Compress)>();
        try
        {
            foreach (var entry in zip.Entries)
            {
                if (entry.FullName is MimeTypePath or ManifestPath)
                {
                    continue;
                }

                var bytes = ReadAll(entry);
                if (!encryption.TryGetValue(entry.FullName, out var described))
                {
                    parts.Add((entry.FullName, bytes, entry.CompressedLength != entry.Length));
                    continue;
                }

                var size = (long?)described.Entry.Attribute(ManifestNs + "size");
                parts.Add((entry.FullName, DecryptPart(bytes, described.Data, size, password, keys, !verified), true));
                verified = true;
            }
        }
        catch (Exception exception) when (exception is FormatException or OverflowException or CryptographicException)
        {
            throw new InvalidDataException(DamagedText, exception);
        }

        if (!verified)
        {
            throw new InvalidDataException(DamagedText); // the manifest names encrypted parts the package does not have
        }

        foreach (var (entry, data) in encryption.Values)
        {
            data.Remove();
            entry.Attribute(ManifestNs + "size")?.Remove();
        }

        return WritePackage(MimeTypeOf(zip), parts, manifest);
    }

    /// <summary>
    /// Writes the OpenDocument <paramref name="package"/> (the ZIP the app wrote) encrypted with <paramref name="password"/> into
    /// <paramref name="output"/> as LibreOffice does: "mimetype" first, stored and plain; every other part deflated, encrypted with
    /// AES-256-CBC under PBKDF2-HMAC-SHA1 (100,000 iterations, a random 16-byte salt and IV per part) of the password's SHA-256, stored,
    /// with the sha256-1k checksum; the manifest plain and last. A thumbnail is left out – it would show the content.
    /// </summary>
    public static void Encrypt(ReadOnlySpan<byte> package, string password, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(password);
        using var zip = OpenZip(new MemoryStream(package.ToArray(), writable: false));
        var manifest = ManifestOf(zip) ?? throw new ArgumentException("The package has no META-INF/manifest.xml.", nameof(package));
        var root = manifest.Root!;
        var entries = root.Elements(ManifestNs + "file-entry")
            .GroupBy(entry => (string?)entry.Attribute(ManifestNs + "full-path") ?? "", StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.First(), StringComparer.Ordinal);
        foreach (var (path, entry) in entries)
        {
            entry.Element(ManifestNs + "encryption-data")?.Remove();
            if (IsThumbnail(path))
            {
                entry.Remove();
            }
        }

        var startKey = SHA256.HashData(Encoding.UTF8.GetBytes(password));
        var parts = new List<(string Path, byte[] Bytes, bool Compress)>();
        foreach (var entry in zip.Entries)
        {
            if (entry.FullName is MimeTypePath or ManifestPath || IsThumbnail(entry.FullName))
            {
                continue;
            }

            if (entry.FullName.EndsWith('/'))
            {
                parts.Add((entry.FullName, [], false));
                continue;
            }

            var plain = ReadAll(entry);
            var (encrypted, data) = EncryptPart(plain, startKey);
            if (!entries.TryGetValue(entry.FullName, out var described))
            {
                described = new XElement(ManifestNs + "file-entry", new XAttribute(ManifestNs + "full-path", entry.FullName), new XAttribute(ManifestNs + "media-type", ""));
                root.Add(described);
            }

            described.SetAttributeValue(ManifestNs + "size", plain.Length.ToString(CultureInfo.InvariantCulture));
            described.Add(data);
            parts.Add((entry.FullName, encrypted, false));
        }

        output.Write(WritePackage(MimeTypeOf(zip), parts, manifest));
    }

    private static (byte[] Encrypted, XElement Data) EncryptPart(byte[] plain, byte[] startKey)
    {
        var deflated = Deflate(plain);
        var checksum = SHA256.HashData(deflated.AsSpan(0, Math.Min(ChecksumLength, deflated.Length)));
        var salt = RandomNumberGenerator.GetBytes(16);
        var iv = RandomNumberGenerator.GetBytes(16);
        var key = Rfc2898DeriveBytes.Pbkdf2(startKey, salt, IterationCount, HashAlgorithmName.SHA1, 32);
        byte[] encrypted;
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            encrypted = aes.EncryptCbc(deflated, iv, PaddingMode.PKCS7); // PKCS #7 is one form of the W3C padding
        }

        CryptographicOperations.ZeroMemory(key);
        var data = new XElement(ManifestNs + "encryption-data",
            new XAttribute(ManifestNs + "checksum-type", Sha256OneK),
            new XAttribute(ManifestNs + "checksum", Convert.ToBase64String(checksum)),
            new XElement(ManifestNs + "algorithm",
                new XAttribute(ManifestNs + "algorithm-name", Aes256Cbc),
                new XAttribute(ManifestNs + "initialisation-vector", Convert.ToBase64String(iv))),
            new XElement(ManifestNs + "start-key-generation",
                new XAttribute(ManifestNs + "start-key-generation-name", StartKeySha256),
                new XAttribute(ManifestNs + "key-size", "32")),
            new XElement(ManifestNs + "key-derivation",
                new XAttribute(ManifestNs + "key-derivation-name", Pbkdf2),
                new XAttribute(ManifestNs + "key-size", "32"),
                new XAttribute(ManifestNs + "iteration-count", IterationCount.ToString(CultureInfo.InvariantCulture)),
                new XAttribute(ManifestNs + "salt", Convert.ToBase64String(salt))));
        return (encrypted, data);
    }

    private static byte[] DecryptPart(byte[] encrypted, XElement data, long? size, string password, Dictionary<string, byte[]> keys, bool decidesPassword)
    {
        var algorithm = data.Element(ManifestNs + "algorithm") ?? throw new InvalidDataException(DamagedText);
        var derivation = data.Element(ManifestNs + "key-derivation") ?? throw new InvalidDataException(DamagedText);
        var startKeyName = (string?)data.Element(ManifestNs + "start-key-generation")?.Attribute(ManifestNs + "start-key-generation-name") ?? StartKeySha1;
        var iv = Convert.FromBase64String((string?)algorithm.Attribute(ManifestNs + "initialisation-vector") ?? "");
        var salt = Convert.FromBase64String((string?)derivation.Attribute(ManifestNs + "salt") ?? "");
        var iterations = (int?)derivation.Attribute(ManifestNs + "iteration-count") ?? 0;
        var keySize = KeySizeOf((string?)algorithm.Attribute(ManifestNs + "algorithm-name"));
        var declaredKeySize = (int?)derivation.Attribute(ManifestNs + "key-size") ?? keySize;
        if (iv.Length != 16 || salt.Length == 0 || iterations is < 1 or > MaxIterations || declaredKeySize != keySize
            || encrypted.Length == 0 || encrypted.Length % 16 != 0 || size is < 0 or > MaxPartLength)
        {
            throw new InvalidDataException(DamagedText);
        }

        var cacheKey = string.Join('|', startKeyName, Convert.ToBase64String(salt), iterations, keySize);
        if (!keys.TryGetValue(cacheKey, out var key))
        {
            var startKey = IsSha256(startKeyName) ? SHA256.HashData(Encoding.UTF8.GetBytes(password)) : SHA1.HashData(Encoding.UTF8.GetBytes(password));
            key = Rfc2898DeriveBytes.Pbkdf2(startKey, salt, iterations, HashAlgorithmName.SHA1, keySize);
            keys[cacheKey] = key;
        }

        byte[] padded;
        using (var aes = Aes.Create())
        {
            aes.Key = key;
            padded = aes.DecryptCbc(encrypted, iv, PaddingMode.None);
        }

        // W3C padding: the last byte counts the padding bytes (1 to 16); their values are not fixed.
        var padding = padded[^1];
        var checksumType = (string?)data.Attribute(ManifestNs + "checksum-type");
        var expected = (string?)data.Attribute(ManifestNs + "checksum") is { } text ? Convert.FromBase64String(text) : null;
        if (padding is < 1 or > 16 || !Matches(padded.AsSpan(0, padded.Length - padding), checksumType, expected))
        {
            throw decidesPassword ? new WrongPasswordException() : new InvalidDataException(DamagedText);
        }

        var deflated = padded.AsSpan(0, padded.Length - padding).ToArray();
        byte[]? inflated = null;
        try
        {
            inflated = Inflate(deflated, size ?? MaxPartLength);
        }
        catch (InvalidDataException)
        {
            // judged below
        }

        if (inflated is not null && (size is null || inflated.Length == size))
        {
            return inflated;
        }

        if (expected is not null && size == deflated.Length)
        {
            return deflated; // stored without deflate before the encryption (the checksum proved the key): the bytes are the part itself
        }

        // without a checksum, data that does not inflate means the key was wrong
        throw expected is null && decidesPassword ? new WrongPasswordException() : new InvalidDataException(DamagedText);
    }

    private static bool Matches(ReadOnlySpan<byte> deflated, string? checksumType, byte[]? expected)
    {
        if (expected is null)
        {
            return true; // the manifest has no checksum: the inflation decides
        }

        var head = deflated[..Math.Min(ChecksumLength, deflated.Length)];
        byte[] actual = checksumType switch
        {
            Sha256OneK => SHA256.HashData(head),
            Sha1OneK or Sha1OneKUrn or null => SHA1.HashData(head),
            _ => throw new InvalidDataException(DamagedText),
        };
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    private static EncryptedFileKind KindOf(XDocument manifest)
    {
        var result = EncryptedFileKind.None;
        foreach (var entry in manifest.Root?.Elements(ManifestNs + "file-entry") ?? [])
        {
            if ((string?)entry.Attribute(ManifestNs + "full-path") == "encrypted-package")
            {
                return EncryptedFileKind.OdfModern; // LibreOffice 24.2's whole-package encryption
            }

            if (entry.Element(ManifestNs + "encryption-data") is not { } data)
            {
                continue;
            }

            var kind = KindOfData(data);
            if (kind != EncryptedFileKind.OdfEncrypted)
            {
                return kind;
            }

            result = kind;
        }

        return result;
    }

    private static EncryptedFileKind KindOfData(XElement data)
    {
        var algorithm = (string?)data.Element(ManifestNs + "algorithm")?.Attribute(ManifestNs + "algorithm-name") ?? "";
        var derivation = (string?)data.Element(ManifestNs + "key-derivation")?.Attribute(ManifestNs + "key-derivation-name") ?? Pbkdf2;
        var startKey = (string?)data.Element(ManifestNs + "start-key-generation")?.Attribute(ManifestNs + "start-key-generation-name") ?? StartKeySha1;
        var checksum = (string?)data.Attribute(ManifestNs + "checksum-type");
        if (algorithm is BlowfishCfb or BlowfishUrn)
        {
            return EncryptedFileKind.OdfBlowfish;
        }

        if (algorithm.Contains("gcm", StringComparison.OrdinalIgnoreCase) || derivation.Contains("argon2", StringComparison.OrdinalIgnoreCase))
        {
            return EncryptedFileKind.OdfModern;
        }

        var known = KeySizeOf(algorithm) > 0 && derivation is Pbkdf2 or Pbkdf2Urn
            && (IsSha256(startKey) || startKey is StartKeySha1 or StartKeySha1Dsig)
            && checksum is null or Sha256OneK or Sha1OneK or Sha1OneKUrn;
        return known ? EncryptedFileKind.OdfEncrypted : EncryptedFileKind.OdfOther;
    }

    private static int KeySizeOf(string? algorithm) => algorithm switch
    {
        Aes256Cbc => 32,
        Aes192Cbc => 24,
        Aes128Cbc => 16,
        _ => 0,
    };

    private static bool IsSha256(string startKey) => startKey is StartKeySha256 or StartKeySha256Enc;

    private static bool IsThumbnail(string path) => path.StartsWith("Thumbnails/", StringComparison.Ordinal);

    private static ZipArchive OpenZip(Stream stream)
    {
        try
        {
            return new ZipArchive(stream, ZipArchiveMode.Read, leaveOpen: true);
        }
        catch (InvalidDataException exception)
        {
            throw new InvalidDataException(DamagedText, exception);
        }
    }

    private static XDocument? ManifestOf(ZipArchive zip)
    {
        if (zip.GetEntry(ManifestPath) is not { } entry || entry.Length > 16 * 1024 * 1024)
        {
            return null;
        }

        using var input = entry.Open();
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
        return XDocument.Load(reader);
    }

    private static string? MimeTypeOf(ZipArchive zip)
    {
        if (zip.GetEntry(MimeTypePath) is not { Length: < 1024 } entry)
        {
            return null;
        }

        using var reader = new StreamReader(entry.Open(), Encoding.ASCII);
        return reader.ReadToEnd();
    }

    private static byte[] ReadAll(ZipArchiveEntry entry)
    {
        if (entry.Length > MaxPartLength)
        {
            throw new InvalidDataException(DamagedText);
        }

        using var input = entry.Open();
        using var bytes = new MemoryStream((int)Math.Min(entry.Length, int.MaxValue));
        input.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static byte[] Deflate(byte[] plain)
    {
        using var output = new MemoryStream();
        using (var deflate = new DeflateStream(output, CompressionLevel.Optimal, leaveOpen: true))
        {
            deflate.Write(plain);
        }

        return output.ToArray();
    }

    private static byte[] Inflate(byte[] deflated, long limit)
    {
        using var inflate = new DeflateStream(new MemoryStream(deflated, writable: false), CompressionMode.Decompress);
        using var output = new MemoryStream();
        var buffer = new byte[81920];
        int read;
        while ((read = inflate.Read(buffer)) > 0)
        {
            if (output.Length + read > limit)
            {
                throw new InvalidDataException(DamagedText);
            }

            output.Write(buffer, 0, read);
        }

        return output.ToArray();
    }

    /// <summary>The package: "mimetype" first and stored (when there is one), the parts, META-INF/manifest.xml last.</summary>
    private static byte[] WritePackage(string? mimeType, List<(string Path, byte[] Bytes, bool Compress)> parts, XDocument manifest)
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            if (mimeType is not null)
            {
                using var entry = zip.CreateEntry(MimeTypePath, CompressionLevel.NoCompression).Open();
                entry.Write(Encoding.ASCII.GetBytes(mimeType));
            }

            foreach (var (path, bytes, compress) in parts)
            {
                using var entry = zip.CreateEntry(path, compress ? CompressionLevel.Optimal : CompressionLevel.NoCompression).Open();
                entry.Write(bytes);
            }

            using var manifestEntry = zip.CreateEntry(ManifestPath, CompressionLevel.Optimal).Open();
            using var writer = XmlWriter.Create(manifestEntry, new XmlWriterSettings { Encoding = new UTF8Encoding(false), Indent = false });
            manifest.Save(writer);
        }

        return buffer.ToArray();
    }
}
