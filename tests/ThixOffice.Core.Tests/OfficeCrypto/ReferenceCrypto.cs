using System.Security.Cryptography;
using System.Text;
using System.Xml.Linq;

namespace ThixOffice.Core.Tests.OfficeCrypto;

/// <summary>
/// A second, deliberately plain implementation of [MS-OFFCRYPTO]'s Agile and Standard encryption, written from the
/// specification without sharing any code with ThixOffice.Core: byte arrays concatenated step by step, hash and cipher objects
/// (ICryptoTransform) instead of the one-shot APIs, the XML through LINQ to XML. The tests hold the product to it both ways.
/// </summary>
internal static class ReferenceCrypto
{
    private static readonly XNamespace E = "http://schemas.microsoft.com/office/2006/encryption";
    private static readonly XNamespace P = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";

    private static readonly byte[] BlockVerifierInput = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    private static readonly byte[] BlockVerifierValue = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    private static readonly byte[] BlockKeyValue = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    private static readonly byte[] BlockHmacKey = [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    private static readonly byte[] BlockHmacValue = [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];

    public static HashAlgorithm NewHash(string name) => name switch
    {
        "SHA1" or "SHA-1" => SHA1.Create(),
        "SHA256" => SHA256.Create(),
        "SHA384" => SHA384.Create(),
        "SHA512" => SHA512.Create(),
        _ => throw new NotSupportedException(name),
    };

    private static HMAC NewHmac(string name, byte[] key) => name switch
    {
        "SHA1" or "SHA-1" => new HMACSHA1(key),
        "SHA256" => new HMACSHA256(key),
        "SHA384" => new HMACSHA384(key),
        "SHA512" => new HMACSHA512(key),
        _ => throw new NotSupportedException(name),
    };

    private static byte[] H(string hash, params byte[][] parts)
    {
        using var algorithm = NewHash(hash);
        return algorithm.ComputeHash(parts.SelectMany(part => part).ToArray());
    }

    private static byte[] Le32(int value) => [(byte)value, (byte)(value >> 8), (byte)(value >> 16), (byte)(value >> 24)];

    private static byte[] Le64(long value) => Enumerable.Range(0, 8).Select(index => (byte)(value >> (8 * index))).ToArray();

    /// <summary>Truncates or fills with 0x36 ([MS-OFFCRYPTO] 2.3.4.11/2.3.4.12).</summary>
    private static byte[] Fix(byte[] bytes, int length) => bytes.Length >= length
        ? bytes.Take(length).ToArray()
        : bytes.Concat(Enumerable.Repeat((byte)0x36, length - bytes.Length)).ToArray();

    private static byte[] ZeroPad(byte[] bytes, int block) =>
        bytes.Concat(new byte[(block - bytes.Length % block) % block]).ToArray();

    private static byte[] Aes(byte[] key, byte[]? iv, byte[] data, bool encrypt)
    {
        using var aes = System.Security.Cryptography.Aes.Create();
        aes.Mode = iv is null ? CipherMode.ECB : CipherMode.CBC;
        aes.Padding = PaddingMode.None;
        using var transform = encrypt ? aes.CreateEncryptor(key, iv ?? new byte[16]) : aes.CreateDecryptor(key, iv ?? new byte[16]);
        return transform.TransformFinalBlock(data, 0, data.Length);
    }

    public static byte[] AgilePasswordHash(string hash, byte[] salt, string password, int spinCount)
    {
        var h = H(hash, salt, Encoding.Unicode.GetBytes(password));
        for (var i = 0; i < spinCount; i++)
        {
            h = H(hash, Le32(i), h);
        }

        return h;
    }

    private static byte[] AgileKey(string hash, byte[] passwordHash, byte[] block, int keyBits) => Fix(H(hash, passwordHash, block), keyBits / 8);

    /// <summary>Decrypts an Agile file's streams; checks the verifier and, when present, the HMAC (throws on either).</summary>
    public static byte[] AgileDecrypt(byte[] encryptionInfo, byte[] encryptedPackage, string password)
    {
        var xml = XDocument.Parse(Encoding.UTF8.GetString(encryptionInfo, 8, encryptionInfo.Length - 8)).Root!;
        var keyData = xml.Element(E + "keyData")!;
        var encryptedKey = xml.Descendants(P + "encryptedKey").Single();

        var hash = (string)encryptedKey.Attribute("hashAlgorithm")!;
        var salt = Convert.FromBase64String((string)encryptedKey.Attribute("saltValue")!);
        var keyBits = (int)encryptedKey.Attribute("keyBits")!;
        var passwordHash = AgilePasswordHash(hash, salt, password, (int)encryptedKey.Attribute("spinCount")!);
        var iv = Fix(salt, 16);

        var verifier = Aes(AgileKey(hash, passwordHash, BlockVerifierInput, keyBits), iv,
            Convert.FromBase64String((string)encryptedKey.Attribute("encryptedVerifierHashInput")!), encrypt: false).Take(salt.Length).ToArray();
        var verifierHash = Aes(AgileKey(hash, passwordHash, BlockVerifierValue, keyBits), iv,
            Convert.FromBase64String((string)encryptedKey.Attribute("encryptedVerifierHashValue")!), encrypt: false);
        var computed = H(hash, verifier);
        if (!verifierHash.Take(computed.Length).SequenceEqual(computed))
        {
            throw new CryptographicException("wrong password");
        }

        var dataHash = (string)keyData.Attribute("hashAlgorithm")!;
        var dataSalt = Convert.FromBase64String((string)keyData.Attribute("saltValue")!);
        var secretKey = Aes(AgileKey(hash, passwordHash, BlockKeyValue, keyBits), iv,
            Convert.FromBase64String((string)encryptedKey.Attribute("encryptedKeyValue")!), encrypt: false)
            .Take((int)keyData.Attribute("keyBits")! / 8).ToArray();

        if (xml.Element(E + "dataIntegrity") is { } integrity)
        {
            var hashSize = H(dataHash).Length;
            var hmacKey = Aes(secretKey, Fix(H(dataHash, dataSalt, BlockHmacKey), 16),
                Convert.FromBase64String((string)integrity.Attribute("encryptedHmacKey")!), encrypt: false).Take(hashSize).ToArray();
            var hmacValue = Aes(secretKey, Fix(H(dataHash, dataSalt, BlockHmacValue), 16),
                Convert.FromBase64String((string)integrity.Attribute("encryptedHmacValue")!), encrypt: false).Take(hashSize).ToArray();
            using var hmac = NewHmac(dataHash, hmacKey);
            if (!hmac.ComputeHash(encryptedPackage).SequenceEqual(hmacValue))
            {
                throw new CryptographicException("integrity");
            }
        }

        var size = BitConverter.ToInt64(encryptedPackage, 0);
        var output = new List<byte>();
        for (var segment = 0; output.Count < size; segment++)
        {
            var offset = 8 + segment * 4096;
            var length = Math.Min(4096, encryptedPackage.Length - offset);
            var chunk = encryptedPackage.Skip(offset).Take(length).ToArray();
            output.AddRange(Aes(secretKey, Fix(H(dataHash, dataSalt, Le32(segment)), 16), chunk, encrypt: false));
        }

        return output.Take((int)size).ToArray();
    }

    /// <summary>Agile encryption with chosen parameters (EncryptionInfo, EncryptedPackage).</summary>
    public static (byte[] Info, byte[] Package) AgileEncrypt(byte[] package, string password, string hash, int keyBits, int spinCount, bool integrity)
    {
        var dataSalt = RandomNumberGenerator.GetBytes(16);
        var keySalt = RandomNumberGenerator.GetBytes(16);
        var secretKey = RandomNumberGenerator.GetBytes(keyBits / 8);
        var verifier = RandomNumberGenerator.GetBytes(16);
        var hashSize = H(hash).Length;

        var encrypted = new List<byte>(Le64(package.Length));
        for (var segment = 0; segment * 4096 < package.Length; segment++)
        {
            var chunk = ZeroPad(package.Skip(segment * 4096).Take(4096).ToArray(), 16);
            encrypted.AddRange(Aes(secretKey, Fix(H(hash, dataSalt, Le32(segment)), 16), chunk, encrypt: true));
        }

        var encryptedPackage = encrypted.ToArray();
        var passwordHash = AgilePasswordHash(hash, keySalt, password, spinCount);
        var iv = keySalt;

        XAttribute[] Parameters(byte[] salt) =>
        [
            new("saltSize", 16), new("blockSize", 16), new("keyBits", keyBits), new("hashSize", hashSize), new("cipherAlgorithm", "AES"),
            new("cipherChaining", "ChainingModeCBC"), new("hashAlgorithm", hash), new("saltValue", Convert.ToBase64String(salt)),
        ];

        var root = new XElement(E + "encryption", new XAttribute(XNamespace.Xmlns + "p", P), new XElement(E + "keyData", Parameters(dataSalt)));
        if (integrity)
        {
            var hmacKey = RandomNumberGenerator.GetBytes(hashSize);
            using var hmac = NewHmac(hash, hmacKey);
            var hmacValue = hmac.ComputeHash(encryptedPackage);
            root.Add(new XElement(E + "dataIntegrity",
                new XAttribute("encryptedHmacKey", Convert.ToBase64String(Aes(secretKey, Fix(H(hash, dataSalt, BlockHmacKey), 16), ZeroPad(hmacKey, 16), true))),
                new XAttribute("encryptedHmacValue", Convert.ToBase64String(Aes(secretKey, Fix(H(hash, dataSalt, BlockHmacValue), 16), ZeroPad(hmacValue, 16), true)))));
        }

        root.Add(new XElement(E + "keyEncryptors", new XElement(E + "keyEncryptor", new XAttribute("uri", P.NamespaceName),
            new XElement(P + "encryptedKey", new XAttribute("spinCount", spinCount), Parameters(keySalt),
                new XAttribute("encryptedVerifierHashInput", Convert.ToBase64String(Aes(AgileKey(hash, passwordHash, BlockVerifierInput, keyBits), iv, verifier, true))),
                new XAttribute("encryptedVerifierHashValue", Convert.ToBase64String(Aes(AgileKey(hash, passwordHash, BlockVerifierValue, keyBits), iv,
                    ZeroPad(H(hash, verifier), 16), true))),
                new XAttribute("encryptedKeyValue", Convert.ToBase64String(Aes(AgileKey(hash, passwordHash, BlockKeyValue, keyBits), iv, ZeroPad(secretKey, 16), true)))))));

        var xml = Encoding.UTF8.GetBytes("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n" + root.ToString(SaveOptions.DisableFormatting));
        byte[] info = [4, 0, 4, 0, 0x40, 0, 0, 0, .. xml];
        return (info, encryptedPackage);
    }

    /// <summary>The Standard encryption key ([MS-OFFCRYPTO] 2.3.4.7).</summary>
    public static byte[] StandardKey(byte[] salt, string password, int keyBits)
    {
        var h = H("SHA1", salt, Encoding.Unicode.GetBytes(password));
        for (var i = 0; i < 50000; i++)
        {
            h = H("SHA1", Le32(i), h);
        }

        h = H("SHA1", h, Le32(0));
        var x1 = H("SHA1", Enumerable.Range(0, 64).Select(i => (byte)(0x36 ^ (i < h.Length ? h[i] : 0))).ToArray());
        var x2 = H("SHA1", Enumerable.Range(0, 64).Select(i => (byte)(0x5C ^ (i < h.Length ? h[i] : 0))).ToArray());
        return x1.Concat(x2).Take(keyBits / 8).ToArray();
    }

    /// <summary>Standard encryption as Office 2007 writes it (EncryptionInfo 3.2 or 4.2, flags fCryptoAPI | fAES).</summary>
    public static (byte[] Info, byte[] Package) StandardEncrypt(byte[] package, string password, int keyBits, ushort major = 3)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var key = StandardKey(salt, password, keyBits);
        var verifier = RandomNumberGenerator.GetBytes(16);
        var algorithm = keyBits switch { 128 => 0x660E, 192 => 0x660F, _ => 0x6610 };

        var csp = Encoding.Unicode.GetBytes("Microsoft Enhanced RSA and AES Cryptographic Provider\0");
        byte[] header = [.. Le32(0x24), .. Le32(0), .. Le32(algorithm), .. Le32(0x8004), .. Le32(keyBits), .. Le32(0x18), .. Le32(0), .. Le32(0), .. csp];
        byte[] verifierBlock =
        [
            .. Le32(16), .. salt, .. Aes(key, null, verifier, true), .. Le32(20), .. Aes(key, null, ZeroPad(H("SHA1", verifier), 16), true),
        ];
        byte[] info = [(byte)major, 0, 2, 0, .. Le32(0x24), .. Le32(header.Length), .. header, .. verifierBlock];
        byte[] encryptedPackage = [.. Le64(package.Length), .. Aes(key, null, ZeroPad(package, 16), true)];
        return (info, encryptedPackage);
    }
}
