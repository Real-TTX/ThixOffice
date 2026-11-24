using System.Buffers.Binary;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests.OfficeCrypto;

/// <summary>
/// Password-encrypted Office files: round trips, the independent reference implementation in both directions, wrong passwords,
/// integrity, the data spaces and the detection of every kind of file.
/// </summary>
public sealed class OfficeEncryptionTests
{
    private const string Password = "Geheim!Ä€ 123";

    private static byte[] Package(int length)
    {
        var bytes = new byte[length];
        new Random(length).NextBytes(bytes);
        return bytes;
    }

    private static byte[] Encrypted(byte[] package, string password = Password)
    {
        using var output = new MemoryStream();
        OfficeEncryption.Encrypt(package, password, output);
        return output.ToArray();
    }

    private static byte[] Container(params (string Path, byte[] Data)[] streams)
    {
        var writer = new CompoundFileWriter();
        foreach (var (path, data) in streams)
        {
            writer.AddStream(path, data);
        }

        using var output = new MemoryStream();
        writer.Save(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(4095)]
    [InlineData(4096)]
    [InlineData(4097)]
    [InlineData(3 * 4096 + 17)]
    [InlineData(5 * 1024 * 1024 + 5)]
    public void Encrypted_packages_decrypt_to_the_same_bytes(int length)
    {
        var package = Package(length);
        var file = Encrypted(package);

        Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(new MemoryStream(file)));
        Assert.Equal(package, OfficeEncryption.Decrypt(new MemoryStream(file), Password));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(4097)]
    [InlineData(70_000)]
    public void The_reference_implementation_decrypts_what_the_product_encrypts(int length)
    {
        var package = Package(length);
        var file = CompoundFile.Open(new MemoryStream(Encrypted(package)));

        var decrypted = ReferenceCrypto.AgileDecrypt(file.ReadStream("EncryptionInfo"), file.ReadStream("EncryptedPackage"), Password);

        Assert.Equal(package, decrypted);
        Assert.Throws<CryptographicException>(() =>
            ReferenceCrypto.AgileDecrypt(file.ReadStream("EncryptionInfo"), file.ReadStream("EncryptedPackage"), Password + "x"));
    }

    [Theory]
    [InlineData("SHA1", 128, 100_000, true)]
    [InlineData("SHA256", 192, 5_000, true)]
    [InlineData("SHA384", 256, 0, false)]
    [InlineData("SHA512", 256, 100_000, true)]
    [InlineData("SHA-1", 256, 10, false)]
    public void The_product_decrypts_what_the_reference_implementation_encrypts(string hash, int keyBits, int spinCount, bool integrity)
    {
        var package = Package(9_000);
        var (info, encrypted) = ReferenceCrypto.AgileEncrypt(package, Password, hash, keyBits, spinCount, integrity);
        var file = Container(("EncryptionInfo", info), ("EncryptedPackage", encrypted));

        Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(new MemoryStream(file)));
        Assert.Equal(package, OfficeEncryption.Decrypt(new MemoryStream(file), Password));
        Assert.Throws<WrongPasswordException>(() => OfficeEncryption.Decrypt(new MemoryStream(file), "geheim!Ä€ 123"));
    }

    [Fact]
    public void Both_implementations_derive_the_same_password_hash()
    {
        var salt = Enumerable.Range(1, 16).Select(index => (byte)index).ToArray();
        foreach (var (name, algorithm) in new[] { ("SHA1", HashAlgorithmName.SHA1), ("SHA512", HashAlgorithmName.SHA512) })
        {
            Assert.Equal(ReferenceCrypto.AgilePasswordHash(name, salt, Password, 1000),
                AgileEncryption.HashPassword(algorithm, salt, Password, 1000));
        }

        Assert.Equal(ReferenceCrypto.StandardKey(salt, Password, 256), StandardEncryption.DeriveKey(salt, Password, 32));
    }

    [Theory]
    [InlineData(128, 3)]
    [InlineData(192, 4)]
    [InlineData(256, 3)]
    public void Standard_encrypted_files_of_Office_2007_open(int keyBits, ushort major)
    {
        var package = Package(10_000);
        var (info, encrypted) = ReferenceCrypto.StandardEncrypt(package, Password, keyBits, major);
        var file = Container(("EncryptionInfo", info), ("EncryptedPackage", encrypted));

        Assert.Equal(EncryptedFileKind.OfficeStandard, OfficeEncryption.Detect(new MemoryStream(file)));
        Assert.Equal(package, OfficeEncryption.Decrypt(new MemoryStream(file), Password));
        var wrong = Assert.Throws<WrongPasswordException>(() => OfficeEncryption.Decrypt(new MemoryStream(file), "Falsch"));
        Assert.Equal(OfficeEncryption.PasswordField, wrong.Field);
    }

    [Fact]
    public void A_wrong_password_is_refused_with_the_German_message()
    {
        var file = Encrypted(Package(100));

        var exception = Assert.Throws<WrongPasswordException>(() => OfficeEncryption.Decrypt(new MemoryStream(file), "geheim!Ä€ 123"));

        Assert.IsAssignableFrom<BusinessException>(exception);
        Assert.Equal("Das Kennwort ist falsch. Achten Sie auf Groß- und Kleinschreibung.", exception.Message);
        Assert.Equal("Password", exception.Field);
        Assert.Throws<WrongPasswordException>(() => OfficeEncryption.Decrypt(new MemoryStream(file), ""));
    }

    [Fact]
    public void A_changed_byte_in_the_encrypted_package_fails_the_integrity_check()
    {
        var file = CompoundFile.Open(new MemoryStream(Encrypted(Package(20_000))));
        var package = file.ReadStream("EncryptedPackage");
        package[5000] ^= 0x01;
        var damaged = Container(("EncryptionInfo", file.ReadStream("EncryptionInfo")), ("EncryptedPackage", package));

        Assert.Throws<InvalidDataException>(() => OfficeEncryption.Decrypt(new MemoryStream(damaged), Password));
    }

    [Fact]
    public void Passwords_follow_Offices_limits()
    {
        Assert.Throws<ArgumentException>(() => Encrypted(Package(10), ""));
        var tooLong = Assert.Throws<BusinessException>(() => Encrypted(Package(10), new string('a', 256)));
        Assert.Equal(OfficeEncryption.PasswordField, tooLong.Field);

        var longest = new string('ä', 255);
        Assert.Equal(Package(10), OfficeEncryption.Decrypt(new MemoryStream(Encrypted(Package(10), longest)), longest));
    }

    [Fact]
    public void Encrypted_files_are_written_like_Office_writes_them()
    {
        var bytes = Encrypted(Package(5000));
        var file = CompoundFile.Open(new MemoryStream(bytes));

        Assert.Equal(3, file.MajorVersion);
        Assert.Equal(
            ["\u0006DataSpaces", "EncryptionInfo", "EncryptedPackage", "\u0006DataSpaces/Version", "\u0006DataSpaces/DataSpaceMap",
                "\u0006DataSpaces/DataSpaceInfo", "\u0006DataSpaces/TransformInfo", "\u0006DataSpaces/DataSpaceInfo/StrongEncryptionDataSpace",
                "\u0006DataSpaces/TransformInfo/StrongEncryptionTransform", "\u0006DataSpaces/TransformInfo/StrongEncryptionTransform/\u0006Primary"],
            file.Entries.Select(entry => entry.Path));

        var info = file.ReadStream("EncryptionInfo");
        Assert.Equal(new byte[] { 4, 0, 4, 0, 0x40, 0, 0, 0 }, info[..8]);
        var xml = XDocument.Parse(Encoding.UTF8.GetString(info, 8, info.Length - 8));
        XNamespace e = "http://schemas.microsoft.com/office/2006/encryption";
        XNamespace p = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";
        var keyData = xml.Root!.Element(e + "keyData")!;
        var encryptedKey = xml.Root.Descendants(p + "encryptedKey").Single();
        Assert.Equal("SHA512", (string?)keyData.Attribute("hashAlgorithm"));
        Assert.Equal("256", (string?)keyData.Attribute("keyBits"));
        Assert.Equal("AES", (string?)keyData.Attribute("cipherAlgorithm"));
        Assert.Equal("ChainingModeCBC", (string?)keyData.Attribute("cipherChaining"));
        Assert.Equal(16, Convert.FromBase64String((string)keyData.Attribute("saltValue")!).Length);
        Assert.Equal("100000", (string?)encryptedKey.Attribute("spinCount"));
        Assert.NotNull(xml.Root.Element(e + "dataIntegrity"));
        Assert.Equal(5000UL, BinaryPrimitives.ReadUInt64LittleEndian(file.ReadStream("EncryptedPackage")));

        // [MS-OFFCRYPTO] 2.1.5/2.1.6/2.2.6: the data spaces' structures.
        var version = file.ReadStream("\u0006DataSpaces/Version");
        Assert.Equal(76, version.Length);
        Assert.Equal("Microsoft.Container.DataSpaces", Encoding.Unicode.GetString(version, 4, 60));
        var map = file.ReadStream("\u0006DataSpaces/DataSpaceMap");
        Assert.Equal(8 + 104, map.Length);
        Assert.Equal(104u, BinaryPrimitives.ReadUInt32LittleEndian(map.AsSpan(8)));
        var primary = file.ReadStream("\u0006DataSpaces/TransformInfo/StrongEncryptionTransform/\u0006Primary");
        Assert.Equal(0x58u, BinaryPrimitives.ReadUInt32LittleEndian(primary));
        Assert.Equal("{FF9A3F03-56EF-4613-BDD5-5A41C1D07246}", Encoding.Unicode.GetString(primary, 12, 76));
        Assert.Equal(4u, BinaryPrimitives.ReadUInt32LittleEndian(primary.AsSpan(primary.Length - 4)));
    }

    [Fact]
    public void Every_encryption_has_its_own_salts_and_keys()
    {
        var package = Package(100);
        var first = CompoundFile.Open(new MemoryStream(Encrypted(package))).ReadStream("EncryptedPackage");
        var second = CompoundFile.Open(new MemoryStream(Encrypted(package))).ReadStream("EncryptedPackage");
        Assert.NotEqual(first, second);
    }

    private static byte[] Zip(params (string Path, string Text)[] entries)
    {
        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (path, text) in entries)
            {
                using var writer = new StreamWriter(zip.CreateEntry(path).Open());
                writer.Write(text);
            }
        }

        return output.ToArray();
    }

    private const string Manifest = """
        <?xml version="1.0" encoding="UTF-8"?>
        <manifest:manifest xmlns:manifest="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0" manifest:version="1.3">
          <manifest:file-entry manifest:full-path="/" manifest:media-type="application/vnd.oasis.opendocument.text"/>
          <manifest:file-entry manifest:full-path="content.xml" manifest:media-type="text/xml" manifest:size="1234">
            {0}
          </manifest:file-entry>
        </manifest:manifest>
        """;

    private const string EncryptionData = """
        <manifest:encryption-data manifest:checksum-type="urn:oasis:names:tc:opendocument:xmlns:manifest:1.0#sha256-1k" manifest:checksum="AAAA">
          <manifest:algorithm manifest:algorithm-name="http://www.w3.org/2001/04/xmlenc#aes256-cbc" manifest:initialisation-vector="AAAA"/>
          <manifest:key-derivation manifest:key-derivation-name="PBKDF2" manifest:key-size="32" manifest:iteration-count="100000" manifest:salt="AAAA"/>
        </manifest:encryption-data>
        """;

    private static (string Path, byte[] Data) EncryptionInfoOf(ushort major, ushort minor, uint flags) =>
        ("EncryptionInfo", [(byte)major, (byte)(major >> 8), (byte)minor, (byte)(minor >> 8), (byte)flags, (byte)(flags >> 8), (byte)(flags >> 16), (byte)(flags >> 24),
            .. new byte[200]]);

    public static TheoryData<string, EncryptedFileKind> Kinds => new()
    {
        { "docx", EncryptedFileKind.None },
        { "odt", EncryptedFileKind.None },
        { "odt encrypted", EncryptedFileKind.OdfEncrypted },
        { "doc", EncryptedFileKind.CompoundNotEncrypted },
        { "irm", EncryptedFileKind.Rights },
        { "rc4 cryptoapi", EncryptedFileKind.OfficeOther },
        { "extensible", EncryptedFileKind.OfficeOther },
        { "rc4", EncryptedFileKind.OfficeOther },
        { "garbage", EncryptedFileKind.None },
        { "empty", EncryptedFileKind.None },
        { "text", EncryptedFileKind.None },
    };

    private static byte[] FileOf(string kind) => kind switch
    {
        "docx" => Zip(("[Content_Types].xml", "<Types/>"), ("word/document.xml", "<w:document/>")),
        "odt" => Zip(("mimetype", "application/vnd.oasis.opendocument.text"), ("META-INF/manifest.xml", string.Format(Manifest, ""))),
        "odt encrypted" => Zip(("mimetype", "application/vnd.oasis.opendocument.text"), ("META-INF/manifest.xml", string.Format(Manifest, EncryptionData))),
        "doc" => Container(("WordDocument", Package(5000)), ("1Table", Package(300)), ("\u0005SummaryInformation", Package(100))),
        "irm" => Container(("EncryptedPackage", Package(5000)), ("\u0006DataSpaces/DataSpaceMap", Package(100)),
            ("\u0006DataSpaces/DataSpaceInfo/DRMEncryptedDataSpace", Package(50)), ("\u0006DataSpaces/TransformInfo/DRMEncryptedTransform/\u0006Primary", Package(50))),
        "rc4 cryptoapi" => Container(EncryptionInfoOf(4, 2, 0x04), ("EncryptedPackage", Package(5000))),
        "extensible" => Container(EncryptionInfoOf(4, 3, 0x10), ("EncryptedPackage", Package(5000))),
        "rc4" => Container(EncryptionInfoOf(1, 1, 0), ("EncryptedPackage", Package(5000))),
        "garbage" => Package(3000),
        "empty" => [],
        _ => Encoding.UTF8.GetBytes("Name;Betrag\nAnna;12\n"),
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Every_kind_of_file_is_detected_and_the_position_kept(string kind, EncryptedFileKind expected)
    {
        var stream = new MemoryStream(FileOf(kind));
        stream.Position = stream.Length / 2;
        var position = stream.Position;

        Assert.Equal(expected, OfficeEncryption.Detect(stream));
        Assert.Equal(position, stream.Position);
    }

    [Theory]
    [InlineData("docx", OfficeEncryption.NotEncryptedText)]
    [InlineData("odt encrypted", OfficeEncryption.OdfText)]
    [InlineData("doc", OfficeEncryption.BinaryText)]
    [InlineData("irm", OfficeEncryption.RightsText)]
    [InlineData("rc4 cryptoapi", OfficeEncryption.UnsupportedText)]
    [InlineData("extensible", OfficeEncryption.UnsupportedText)]
    public void What_cannot_be_decrypted_is_explained(string kind, string message)
    {
        var exception = Assert.Throws<BusinessException>(() => OfficeEncryption.Decrypt(new MemoryStream(FileOf(kind)), Password));
        Assert.Equal(message, exception.Message);
    }

    [Fact]
    public void Office_files_encrypted_with_a_certificate_only_are_explained()
    {
        var (info, encrypted) = ReferenceCrypto.AgileEncrypt(Package(100), Password, "SHA512", 256, 10, integrity: false);
        var text = Encoding.UTF8.GetString(info, 8, info.Length - 8)
            .Replace("uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/password\"", "uri=\"http://schemas.microsoft.com/office/2006/keyEncryptor/certificate\"");
        var file = Container(("EncryptionInfo", [.. info[..8], .. Encoding.UTF8.GetBytes(text)]), ("EncryptedPackage", encrypted));

        var exception = Assert.Throws<BusinessException>(() => OfficeEncryption.Decrypt(new MemoryStream(file), Password));
        Assert.Equal(OfficeEncryption.CertificateText, exception.Message);
    }

    [Fact]
    public void Broken_encryption_info_is_damage_not_a_wrong_password()
    {
        var (info, encrypted) = ReferenceCrypto.AgileEncrypt(Package(100), Password, "SHA512", 256, 10, integrity: false);
        var broken = Container(("EncryptionInfo", info[..(info.Length / 2)]), ("EncryptedPackage", encrypted));
        Assert.Throws<InvalidDataException>(() => OfficeEncryption.Decrypt(new MemoryStream(broken), Password));

        var hugeSpin = Encoding.UTF8.GetString(info, 8, info.Length - 8).Replace("spinCount=\"10\"", "spinCount=\"2000000000\"");
        var slow = Container(("EncryptionInfo", [.. info[..8], .. Encoding.UTF8.GetBytes(hugeSpin)]), ("EncryptedPackage", encrypted));
        Assert.Throws<InvalidDataException>(() => OfficeEncryption.Decrypt(new MemoryStream(slow), Password));
    }
}
