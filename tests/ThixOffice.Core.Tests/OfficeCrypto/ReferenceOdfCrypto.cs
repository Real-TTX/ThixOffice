using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace ThixOffice.Core.Tests.OfficeCrypto;

/// <summary>
/// A second, deliberately plain implementation of OpenDocument's package encryption (ODF 1.2 Part 3, section 3.4), written from the
/// specification and LibreOffice's behaviour without sharing any code with ThixOffice.Core: the manifest through XmlDocument and
/// XPath, PBKDF2 spelled out with HMACSHA1, the cipher through ICryptoTransform, W3C padding with random filler bytes. It decrypts
/// what the product writes and builds encrypted packages – also the older variants (SHA1/1K checksum, SHA-1 start key, AES-128) –
/// for the product to open.
/// </summary>
internal static class ReferenceOdfCrypto
{
    public const string ManifestUri = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";

    /// <summary>How <see cref="Encrypt"/> encrypts. <see cref="StartKey"/> null leaves start-key-generation out (the default is SHA1).</summary>
    public sealed record Options
    {
        public string ChecksumType { get; init; } = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#sha256-1k";

        public string? StartKey { get; init; } = "http://www.w3.org/2000/09/xmldsig#sha256";

        public string Algorithm { get; init; } = "http://www.w3.org/2001/04/xmlenc#aes256-cbc";

        public string KeyDerivation { get; init; } = "PBKDF2";

        public int KeySize { get; init; } = 32;

        public int Iterations { get; init; } = 1000;

        /// <summary>Parts written as they are (no encryption-data).</summary>
        public HashSet<string> Plain { get; init; } = [];

        /// <summary>Leaves the checksum out.</summary>
        public bool WithoutChecksum { get; init; }
    }

    /// <summary>Every part of the encrypted package <paramref name="file"/> decrypted (the manifest and mimetype as they are).</summary>
    public static Dictionary<string, byte[]> Decrypt(byte[] file, string password)
    {
        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        var manifest = new XmlDocument();
        using (var input = zip.GetEntry("META-INF/manifest.xml")!.Open())
        {
            manifest.Load(input);
        }

        var names = new XmlNamespaceManager(manifest.NameTable);
        names.AddNamespace("m", ManifestUri);
        var parts = new Dictionary<string, byte[]>();
        foreach (var entry in zip.Entries)
        {
            var raw = ReadEntry(entry);
            var data = manifest.SelectSingleNode($"/m:manifest/m:file-entry[@m:full-path='{entry.FullName}']/m:encryption-data", names);
            if (data is null)
            {
                parts[entry.FullName] = raw;
                continue;
            }

            var algorithm = data.SelectSingleNode("m:algorithm", names)!;
            var derivation = data.SelectSingleNode("m:key-derivation", names)!;
            var startKeyName = data.SelectSingleNode("m:start-key-generation/@m:start-key-generation-name", names)?.Value ?? "SHA1";
            var passwordBytes = Encoding.UTF8.GetBytes(password);
            var startKey = startKeyName.EndsWith("sha256", StringComparison.Ordinal) ? SHA256.HashData(passwordBytes) : SHA1.HashData(passwordBytes);
            var keySize = int.Parse(derivation.Attributes!["key-size", ManifestUri]!.Value);
            var key = Pbkdf2HmacSha1(startKey, Convert.FromBase64String(derivation.Attributes["salt", ManifestUri]!.Value),
                int.Parse(derivation.Attributes["iteration-count", ManifestUri]!.Value), keySize);
            var iv = Convert.FromBase64String(algorithm.Attributes!["initialisation-vector", ManifestUri]!.Value);
            var decrypted = Transform(raw, key, iv, encrypt: false);
            var deflated = decrypted[..^decrypted[^1]];
            var head = deflated[..Math.Min(1024, deflated.Length)];
            var checksumType = data.Attributes!["checksum-type", ManifestUri]!.Value;
            var checksum = checksumType.EndsWith("sha256-1k", StringComparison.Ordinal) ? SHA256.HashData(head) : SHA1.HashData(head);
            if (Convert.ToBase64String(checksum) != data.Attributes["checksum", ManifestUri]!.Value)
            {
                throw new CryptographicException("checksum mismatch: " + entry.FullName);
            }

            using var inflate = new DeflateStream(new MemoryStream(deflated), CompressionMode.Decompress);
            using var plain = new MemoryStream();
            inflate.CopyTo(plain);
            var size = long.Parse(data.ParentNode!.Attributes!["size", ManifestUri]!.Value);
            if (plain.Length != size)
            {
                throw new CryptographicException("size mismatch: " + entry.FullName);
            }

            parts[entry.FullName] = plain.ToArray();
        }

        return parts;
    }

    /// <summary>An encrypted package of <paramref name="parts"/> (path, media type, bytes) after "mimetype".</summary>
    public static byte[] Encrypt(string mimeType, IEnumerable<(string Path, string MediaType, byte[] Bytes)> parts, string password, Options options)
    {
        var manifest = new StringBuilder();
        manifest.Append("<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n");
        manifest.Append($"<manifest:manifest xmlns:manifest=\"{ManifestUri}\" manifest:version=\"1.2\">\n");
        manifest.Append($" <manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.2\" manifest:media-type=\"{mimeType}\"/>\n");
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Write(zip, "mimetype", Encoding.ASCII.GetBytes(mimeType), CompressionLevel.NoCompression);
            foreach (var (path, mediaType, bytes) in parts)
            {
                if (options.Plain.Contains(path))
                {
                    Write(zip, path, bytes, CompressionLevel.Optimal);
                    manifest.Append($" <manifest:file-entry manifest:full-path=\"{path}\" manifest:media-type=\"{mediaType}\"/>\n");
                    continue;
                }

                using var deflatedStream = new MemoryStream();
                using (var deflate = new DeflateStream(deflatedStream, CompressionLevel.SmallestSize, leaveOpen: true))
                {
                    deflate.Write(bytes, 0, bytes.Length);
                }

                var deflated = deflatedStream.ToArray();
                var head = deflated[..Math.Min(1024, deflated.Length)];
                var checksum = options.ChecksumType.EndsWith("sha256-1k", StringComparison.Ordinal) ? SHA256.HashData(head) : SHA1.HashData(head);
                var salt = RandomNumberGenerator.GetBytes(16);
                var iv = RandomNumberGenerator.GetBytes(16);
                var passwordBytes = Encoding.UTF8.GetBytes(password);
                var startKey = options.StartKey?.EndsWith("sha256", StringComparison.Ordinal) == true ? SHA256.HashData(passwordBytes) : SHA1.HashData(passwordBytes);
                var key = Pbkdf2HmacSha1(startKey, salt, options.Iterations, options.KeySize);

                // W3C padding: 1 to 16 bytes, random filler, the last byte counts them
                var count = 16 - deflated.Length % 16;
                var padding = RandomNumberGenerator.GetBytes(count);
                padding[^1] = (byte)count;
                Write(zip, path, Transform([.. deflated, .. padding], key, iv, encrypt: true), CompressionLevel.NoCompression);

                manifest.Append($" <manifest:file-entry manifest:full-path=\"{path}\" manifest:media-type=\"{mediaType}\" manifest:size=\"{bytes.Length}\">\n");
                manifest.Append(options.WithoutChecksum
                    ? "  <manifest:encryption-data>\n"
                    : $"  <manifest:encryption-data manifest:checksum-type=\"{options.ChecksumType}\" manifest:checksum=\"{Convert.ToBase64String(checksum)}\">\n");
                manifest.Append($"   <manifest:algorithm manifest:algorithm-name=\"{options.Algorithm}\" manifest:initialisation-vector=\"{Convert.ToBase64String(iv)}\"/>\n");
                if (options.StartKey is not null)
                {
                    manifest.Append($"   <manifest:start-key-generation manifest:start-key-generation-name=\"{options.StartKey}\" manifest:key-size=\"{startKey.Length}\"/>\n");
                }

                manifest.Append($"   <manifest:key-derivation manifest:key-derivation-name=\"{options.KeyDerivation}\" manifest:key-size=\"{options.KeySize}\" "
                    + $"manifest:iteration-count=\"{options.Iterations}\" manifest:salt=\"{Convert.ToBase64String(salt)}\"/>\n");
                manifest.Append("  </manifest:encryption-data>\n </manifest:file-entry>\n");
            }

            manifest.Append("</manifest:manifest>\n");
            Write(zip, "META-INF/manifest.xml", Encoding.UTF8.GetBytes(manifest.ToString()), CompressionLevel.Optimal);
        }

        return output.ToArray();
    }

    /// <summary>PBKDF2 (RFC 8018) with HMAC-SHA1, block by block.</summary>
    public static byte[] Pbkdf2HmacSha1(byte[] password, byte[] salt, int iterations, int length)
    {
        using var hmac = new HMACSHA1(password);
        var result = new List<byte>();
        for (var block = 1; result.Count < length; block++)
        {
            byte[] u = hmac.ComputeHash([.. salt, (byte)(block >> 24), (byte)(block >> 16), (byte)(block >> 8), (byte)block]);
            var t = (byte[])u.Clone();
            for (var round = 1; round < iterations; round++)
            {
                u = hmac.ComputeHash(u);
                for (var i = 0; i < t.Length; i++)
                {
                    t[i] ^= u[i];
                }
            }

            result.AddRange(t);
        }

        return [.. result.Take(length)];
    }

    private static byte[] Transform(byte[] data, byte[] key, byte[] iv, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var transform = encrypt ? aes.CreateEncryptor(key, iv) : aes.CreateDecryptor(key, iv);
        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var input = entry.Open();
        using var bytes = new MemoryStream();
        input.CopyTo(bytes);
        return bytes.ToArray();
    }

    private static void Write(ZipArchive zip, string path, byte[] bytes, CompressionLevel level)
    {
        using var output = zip.CreateEntry(path, level).Open();
        output.Write(bytes, 0, bytes.Length);
    }
}
