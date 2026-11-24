using System.Buffers.Binary;
using System.IO.Compression;
using System.Text;
using System.Xml.Linq;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests.OfficeCrypto;

/// <summary>
/// OpenDocument's password encryption: round trips, the shape LibreOffice writes, the independent reference implementation in both
/// directions (also the older SHA1/1K variants), wrong passwords, damaged parts and the kinds that are refused in words.
/// </summary>
public sealed class OdfEncryptionTests
{
    private const string Password = "Geheim!Ä€ 123";
    private const string TextMime = "application/vnd.oasis.opendocument.text";
    private static readonly XNamespace M = ReferenceOdfCrypto.ManifestUri;

    /// <summary>A plain odt package as the apps write it, plus a thumbnail and a picture.</summary>
    private static byte[] PlainPackage(int pictureLength = 3000, int textLength = 2000)
    {
        var parts = Parts(pictureLength, textLength);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "mimetype", Encoding.ASCII.GetBytes(TextMime), CompressionLevel.NoCompression);
            var manifest = new StringBuilder($"<?xml version=\"1.0\" encoding=\"UTF-8\"?><manifest:manifest xmlns:manifest=\"{ReferenceOdfCrypto.ManifestUri}\" manifest:version=\"1.3\">"
                + $"<manifest:file-entry manifest:full-path=\"/\" manifest:version=\"1.3\" manifest:media-type=\"{TextMime}\"/>");
            foreach (var (path, mediaType, bytes) in parts)
            {
                Add(zip, path, bytes, CompressionLevel.Optimal);
                manifest.Append($"<manifest:file-entry manifest:full-path=\"{path}\" manifest:media-type=\"{mediaType}\"/>");
            }

            Add(zip, "Thumbnails/thumbnail.png", [1, 2, 3, 4], CompressionLevel.NoCompression);
            manifest.Append("<manifest:file-entry manifest:full-path=\"Thumbnails/thumbnail.png\" manifest:media-type=\"image/png\"/>");
            manifest.Append("</manifest:manifest>");
            Add(zip, "META-INF/manifest.xml", Encoding.UTF8.GetBytes(manifest.ToString()), CompressionLevel.Optimal);
        }

        return output.ToArray();
    }

    private static List<(string Path, string MediaType, byte[] Bytes)> Parts(int pictureLength = 3000, int textLength = 2000)
    {
        var picture = new byte[pictureLength];
        new Random(pictureLength).NextBytes(picture);
        var text = new StringBuilder("<office:document-content xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"><office:body><office:text>");
        for (var i = 0; text.Length < textLength; i++)
        {
            text.Append($"<p>Absatz {i}: Übermorgen größere Maße – €</p>");
        }

        text.Append("</office:text></office:body></office:document-content>");
        return
        [
            ("content.xml", "text/xml", Encoding.UTF8.GetBytes(text.ToString())),
            ("styles.xml", "text/xml", Encoding.UTF8.GetBytes("<office:document-styles xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"/>")),
            ("meta.xml", "text/xml", Encoding.UTF8.GetBytes("<office:document-meta xmlns:office=\"urn:oasis:names:tc:opendocument:xmlns:office:1.0\"/>")),
            ("settings.xml", "text/xml", []),
            ("Pictures/bild.png", "image/png", picture),
        ];
    }

    private static void Add(ZipArchive zip, string path, byte[] bytes, CompressionLevel level)
    {
        using var output = zip.CreateEntry(path, level).Open();
        output.Write(bytes);
    }

    private static Dictionary<string, byte[]> Entries(byte[] package)
    {
        using var zip = new ZipArchive(new MemoryStream(package), ZipArchiveMode.Read);
        return zip.Entries.ToDictionary(entry => entry.FullName, entry =>
        {
            using var input = entry.Open();
            using var bytes = new MemoryStream();
            input.CopyTo(bytes);
            return bytes.ToArray();
        });
    }

    private static byte[] Encrypted(byte[] package, string password = Password)
    {
        using var output = new MemoryStream();
        OfficeEncryption.Encrypt(package, password, output);
        return output.ToArray();
    }

    private static byte[] Decrypted(byte[] file, string password = Password) => OfficeEncryption.Decrypt(new MemoryStream(file), password);

    private static XDocument ManifestOf(byte[] package) => XDocument.Parse(Encoding.UTF8.GetString(Entries(package)["META-INF/manifest.xml"]));

    [Fact]
    public void The_reference_PBKDF2_meets_the_RFC_6070_vectors()
    {
        var password = Encoding.ASCII.GetBytes("password");
        var salt = Encoding.ASCII.GetBytes("salt");
        Assert.Equal("0C60C80F961F0E71F3A9B524AF6012062FE037A6", Convert.ToHexString(ReferenceOdfCrypto.Pbkdf2HmacSha1(password, salt, 1, 20)));
        Assert.Equal("EA6C014DC72D6F8CCD1ED92ACE1D41F0D8DE8957", Convert.ToHexString(ReferenceOdfCrypto.Pbkdf2HmacSha1(password, salt, 2, 20)));
        Assert.Equal("4B007901B765489ABEAD49D926F721D065A429C1", Convert.ToHexString(ReferenceOdfCrypto.Pbkdf2HmacSha1(password, salt, 4096, 20)));
    }

    [Theory]
    [InlineData(3000, 2000)]
    [InlineData(3 * 1024 * 1024, 2 * 1024 * 1024)]
    public void Every_part_comes_back_as_it_was_and_the_thumbnail_is_left_out(int pictureLength, int textLength)
    {
        var plain = PlainPackage(pictureLength, textLength);
        var file = Encrypted(plain);

        Assert.Equal(EncryptedFileKind.OdfEncrypted, OfficeEncryption.Detect(new MemoryStream(file)));
        var decrypted = Entries(Decrypted(file));
        var expected = Entries(plain);
        foreach (var (path, bytes) in expected.Where(entry => entry.Key != "META-INF/manifest.xml" && !entry.Key.StartsWith("Thumbnails/", StringComparison.Ordinal)))
        {
            Assert.Equal(bytes, decrypted[path]);
        }

        Assert.DoesNotContain(decrypted.Keys, path => path.StartsWith("Thumbnails/", StringComparison.Ordinal));
        var manifest = XDocument.Parse(Encoding.UTF8.GetString(decrypted["META-INF/manifest.xml"]));
        Assert.Empty(manifest.Descendants(M + "encryption-data"));
        Assert.DoesNotContain(manifest.Descendants(M + "file-entry"), entry => ((string?)entry.Attribute(M + "full-path"))!.StartsWith("Thumbnails/", StringComparison.Ordinal));
        Assert.Equal(TextMime, Encoding.ASCII.GetString(decrypted["mimetype"]));
    }

    [Fact]
    public void The_package_has_the_shape_LibreOffice_writes()
    {
        var plain = PlainPackage();
        var file = Encrypted(plain);

        // "mimetype" first, stored, plain: a sniffer still finds the type at offset 38
        Assert.Equal(0x04034B50u, BinaryPrimitives.ReadUInt32LittleEndian(file));
        Assert.Equal(0, BinaryPrimitives.ReadUInt16LittleEndian(file.AsSpan(8)));
        Assert.Equal("mimetype" + TextMime, Encoding.ASCII.GetString(file, 30, 8 + TextMime.Length));

        using var zip = new ZipArchive(new MemoryStream(file), ZipArchiveMode.Read);
        Assert.Equal("mimetype", zip.Entries[0].FullName);
        Assert.Equal("META-INF/manifest.xml", zip.Entries[^1].FullName);
        var manifest = ManifestOf(file);
        var plainParts = Parts().ToDictionary(part => part.Path, part => part.Bytes);
        var salts = new HashSet<string>();
        var vectors = new HashSet<string>();
        foreach (var entry in zip.Entries.Where(entry => entry.FullName is not ("mimetype" or "META-INF/manifest.xml")))
        {
            Assert.Equal(entry.Length, entry.CompressedLength); // stored: the bytes are already deflated and encrypted
            Assert.Equal(0, entry.Length % 16);
            var described = manifest.Root!.Elements(M + "file-entry").Single(element => (string?)element.Attribute(M + "full-path") == entry.FullName);
            Assert.Equal(plainParts[entry.FullName].Length, (int)described.Attribute(M + "size")!);
            var data = described.Element(M + "encryption-data")!;
            Assert.Equal("urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#sha256-1k", (string?)data.Attribute(M + "checksum-type"));
            Assert.Equal(32, Convert.FromBase64String((string)data.Attribute(M + "checksum")!).Length);
            var algorithm = data.Element(M + "algorithm")!;
            Assert.Equal("http://www.w3.org/2001/04/xmlenc#aes256-cbc", (string?)algorithm.Attribute(M + "algorithm-name"));
            var iv = (string)algorithm.Attribute(M + "initialisation-vector")!;
            Assert.Equal(16, Convert.FromBase64String(iv).Length);
            var startKey = data.Element(M + "start-key-generation")!;
            Assert.Equal("http://www.w3.org/2000/09/xmldsig#sha256", (string?)startKey.Attribute(M + "start-key-generation-name"));
            Assert.Equal("32", (string?)startKey.Attribute(M + "key-size"));
            var derivation = data.Element(M + "key-derivation")!;
            Assert.Equal("PBKDF2", (string?)derivation.Attribute(M + "key-derivation-name"));
            Assert.Equal("32", (string?)derivation.Attribute(M + "key-size"));
            Assert.Equal("100000", (string?)derivation.Attribute(M + "iteration-count"));
            var salt = (string)derivation.Attribute(M + "salt")!;
            Assert.Equal(16, Convert.FromBase64String(salt).Length);
            Assert.Equal(["algorithm", "start-key-generation", "key-derivation"], data.Elements().Select(element => element.Name.LocalName));
            Assert.True(salts.Add(salt));
            Assert.True(vectors.Add(iv));
        }

        Assert.Equal(plainParts.Count, salts.Count);
        Assert.Null(manifest.Root!.Elements(M + "file-entry").Single(element => (string?)element.Attribute(M + "full-path") == "/").Element(M + "encryption-data"));
        Assert.DoesNotContain(Encoding.UTF8.GetString(file), "Absatz", StringComparison.Ordinal);
    }

    [Fact]
    public void The_reference_decrypts_what_the_product_writes()
    {
        var file = Encrypted(PlainPackage());

        var parts = ReferenceOdfCrypto.Decrypt(file, Password);

        foreach (var (path, _, bytes) in Parts())
        {
            Assert.Equal(bytes, parts[path]);
        }

        Assert.Throws<System.Security.Cryptography.CryptographicException>(() => ReferenceOdfCrypto.Decrypt(file, "falsch"));
    }

    public static TheoryData<string> Variants => ["LibreOffice", "SHA1/1K and SHA-1 start key", "SHA1/1K, no start key element", "sha1-1k urn", "AES-128", "xmlenc sha256 start key", "picture left plain", "no checksum"];

    private static ReferenceOdfCrypto.Options OptionsOf(string variant) => variant switch
    {
        "SHA1/1K and SHA-1 start key" => new() { ChecksumType = "SHA1/1K", StartKey = "SHA1" },
        "SHA1/1K, no start key element" => new() { ChecksumType = "SHA1/1K", StartKey = null },
        "sha1-1k urn" => new() { ChecksumType = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#sha1-1k", StartKey = "http://www.w3.org/2000/09/xmldsig#sha1" },
        "AES-128" => new() { Algorithm = "http://www.w3.org/2001/04/xmlenc#aes128-cbc", KeySize = 16 },
        "xmlenc sha256 start key" => new() { StartKey = "http://www.w3.org/2001/04/xmlenc#sha256", KeyDerivation = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#pbkdf2" },
        "picture left plain" => new() { Plain = ["Pictures/bild.png"] },
        "no checksum" => new() { WithoutChecksum = true },
        _ => new() { Iterations = 100_000 },
    };

    [Theory]
    [MemberData(nameof(Variants))]
    public void The_product_opens_what_the_reference_encrypts(string variant)
    {
        var parts = Parts();
        var file = ReferenceOdfCrypto.Encrypt(TextMime, parts, Password, OptionsOf(variant));

        Assert.Equal(EncryptedFileKind.OdfEncrypted, OfficeEncryption.Detect(new MemoryStream(file)));
        var decrypted = Entries(Decrypted(file));
        foreach (var (path, _, bytes) in parts)
        {
            Assert.Equal(bytes, decrypted[path]);
        }

        Assert.Equal(TextMime, Encoding.ASCII.GetString(decrypted["mimetype"]));
        Assert.Throws<WrongPasswordException>(() => Decrypted(file, "geheim!Ä€ 123"));
    }

    [Fact]
    public void A_wrong_password_is_refused_at_the_field()
    {
        var file = Encrypted(PlainPackage());

        var wrong = Assert.Throws<WrongPasswordException>(() => Decrypted(file, "falsch"));
        Assert.Equal(OfficeEncryption.PasswordField, wrong.Field);
        Assert.Throws<WrongPasswordException>(() => Decrypted(file, ""));
    }

    [Fact]
    public void A_damaged_part_after_the_first_is_damage_and_not_a_wrong_password()
    {
        var parts = Parts();
        var file = ReferenceOdfCrypto.Encrypt(TextMime, parts, Password, new ReferenceOdfCrypto.Options());
        var damaged = Rewrite(file, "Pictures/bild.png", bytes => bytes[40] ^= 0xFF);

        Assert.Throws<InvalidDataException>(() => Decrypted(damaged));
    }

    public static TheoryData<string, EncryptedFileKind, string> Refused => new()
    {
        { "Blowfish CFB", EncryptedFileKind.OdfBlowfish, OdfEncryption.BlowfishText },
        { "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#blowfish", EncryptedFileKind.OdfBlowfish, OdfEncryption.BlowfishText },
        { "http://www.w3.org/2009/xmlenc11#aes256-gcm", EncryptedFileKind.OdfModern, OdfEncryption.ModernText },
        { "argon2id", EncryptedFileKind.OdfModern, OdfEncryption.ModernText },
        { "encrypted-package", EncryptedFileKind.OdfModern, OdfEncryption.ModernText },
        { "http://example.org/rot13", EncryptedFileKind.OdfOther, OdfEncryption.OtherText },
    };

    [Theory]
    [MemberData(nameof(Refused))]
    public void Other_encryptions_are_recognized_and_refused_in_words(string variant, EncryptedFileKind kind, string message)
    {
        var options = variant switch
        {
            "argon2id" => new ReferenceOdfCrypto.Options { KeyDerivation = "urn:org:documentfoundation:names:experimental:office:manifest:argon2id" },
            "encrypted-package" => new ReferenceOdfCrypto.Options(),
            _ => new ReferenceOdfCrypto.Options { Algorithm = variant },
        };
        List<(string Path, string MediaType, byte[] Bytes)> parts = variant == "encrypted-package"
            ? [("encrypted-package", "application/octet-stream", new byte[100])]
            : Parts();
        var file = ReferenceOdfCrypto.Encrypt(TextMime, parts, Password, options);

        Assert.Equal(kind, OfficeEncryption.Detect(new MemoryStream(file)));
        var refusal = Assert.Throws<BusinessException>(() => Decrypted(file));
        Assert.Equal(message, refusal.Message);
        Assert.IsNotType<WrongPasswordException>(refusal);
    }

    [Fact]
    public void Office_Open_XML_packages_keep_the_Agile_encryption()
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Add(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes("<Types/>"), CompressionLevel.Optimal);
        }

        Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(new MemoryStream(Encrypted(output.ToArray()))));
        Assert.False(OdfEncryption.IsOdfPackage(output.ToArray()));
        Assert.True(OdfEncryption.IsOdfPackage(PlainPackage()));
    }

    [Fact]
    public void The_OpenDocument_types_can_carry_a_password()
    {
        foreach (var extension in new[] { ".odt", ".ods", ".odp", ".ODT" })
        {
            Assert.True(OfficeEncryption.CanEncrypt(extension), extension);
        }

        Assert.False(OfficeEncryption.CanEncrypt(".fodt"));
    }

    private static byte[] Rewrite(byte[] file, string path, Action<byte[]> change)
    {
        var entries = Entries(file);
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, bytes) in entries)
            {
                if (name == path)
                {
                    change(bytes);
                }

                Add(zip, name, bytes, name == "mimetype" ? CompressionLevel.NoCompression : CompressionLevel.Optimal);
            }
        }

        return output.ToArray();
    }
}
