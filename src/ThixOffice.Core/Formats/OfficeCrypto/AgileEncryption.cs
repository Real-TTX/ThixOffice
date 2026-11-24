using System.Buffers.Binary;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Xml;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// ECMA-376 Agile Encryption ([MS-OFFCRYPTO] 2.3.4.10–2.3.4.15): the EncryptionInfo XML, the password key encryptor (salted
/// hash with a spin count, verifier, encrypted intermediate key), the package in 4096-byte segments with an IV per segment,
/// and the dataIntegrity HMAC over the whole EncryptedPackage stream.
/// </summary>
internal static class AgileEncryption
{
    internal const string EncryptionNs = "http://schemas.microsoft.com/office/2006/encryption";
    internal const string PasswordNs = "http://schemas.microsoft.com/office/2006/keyEncryptor/password";
    internal const string CertificateNs = "http://schemas.microsoft.com/office/2006/keyEncryptor/certificate";
    internal const int SegmentLength = 4096;
    internal const int MaxSpinCount = 10_000_000;

    internal static readonly byte[] VerifierInputBlock = [0xFE, 0xA7, 0xD2, 0x76, 0x3B, 0x4B, 0x9E, 0x79];
    internal static readonly byte[] VerifierValueBlock = [0xD7, 0xAA, 0x0F, 0x6D, 0x30, 0x61, 0x34, 0x4E];
    internal static readonly byte[] KeyValueBlock = [0x14, 0x6E, 0x0B, 0xE7, 0xAB, 0xAC, 0xD0, 0xD6];
    internal static readonly byte[] HmacKeyBlock = [0x5F, 0xB2, 0xAD, 0x01, 0x0C, 0xB9, 0xE1, 0xF6];
    internal static readonly byte[] HmacValueBlock = [0xA0, 0x67, 0x7F, 0x02, 0xB2, 0x2C, 0x84, 0x33];

    /// <summary>The package's bytes, or <see cref="WrongPasswordException"/> when the password's verifier does not match.</summary>
    public static byte[] Decrypt(byte[] encryptionInfo, byte[] encryptedPackage, string password)
    {
        var info = Parse(encryptionInfo);
        var encryptor = info.Password ?? throw new Models.BusinessException(OfficeEncryption.CertificateText, OfficeEncryption.PasswordField);
        CheckSupported(info.KeyData);
        CheckSupported(encryptor);

        var passwordHash = HashPassword(encryptor.Hash, encryptor.Salt, password, encryptor.SpinCount);
        var verifierInput = DecryptWith(encryptor, passwordHash, VerifierInputBlock, encryptor.EncryptedVerifierHashInput);
        var verifierValue = DecryptWith(encryptor, passwordHash, VerifierValueBlock, encryptor.EncryptedVerifierHashValue);
        var hashSize = HashSize(encryptor.Hash);
        if (verifierInput.Length < encryptor.Salt.Length || verifierValue.Length < hashSize)
        {
            throw Invalid();
        }

        var expected = CryptographicOperations.HashData(encryptor.Hash, verifierInput.AsSpan(0, encryptor.Salt.Length));
        if (!CryptographicOperations.FixedTimeEquals(expected, verifierValue.AsSpan(0, hashSize)))
        {
            throw new WrongPasswordException();
        }

        var keyLength = info.KeyData.KeyBits / 8;
        var secretKey = DecryptWith(encryptor, passwordHash, KeyValueBlock, encryptor.EncryptedKeyValue);
        if (secretKey.Length < keyLength)
        {
            throw Invalid();
        }

        secretKey = secretKey[..keyLength];
        if (info.EncryptedHmacKey is not null && info.EncryptedHmacValue is not null)
        {
            CheckIntegrity(info, secretKey, encryptedPackage);
        }

        return DecryptPackage(info.KeyData, secretKey, encryptedPackage);
    }

    /// <summary>The EncryptionInfo and EncryptedPackage streams for <paramref name="package"/>, the way current Office writes them.</summary>
    public static (byte[] EncryptionInfo, byte[] EncryptedPackage) Encrypt(ReadOnlySpan<byte> package, string password)
    {
        var hash = HashAlgorithmName.SHA512;
        const int keyBits = 256;
        const int blockSize = 16;
        const int spinCount = 100_000;
        var hashSize = HashSize(hash);

        var keyDataSalt = RandomNumberGenerator.GetBytes(16);
        var passwordSalt = RandomNumberGenerator.GetBytes(16);
        var secretKey = RandomNumberGenerator.GetBytes(keyBits / 8);
        var verifier = RandomNumberGenerator.GetBytes(16);
        var hmacKey = RandomNumberGenerator.GetBytes(hashSize);

        var keyData = new KeyParameters(16, blockSize, keyBits, hashSize, "AES", "ChainingModeCBC", hash, keyDataSalt);
        var encryptor = new PasswordParameters(keyData with { Salt = passwordSalt }, spinCount, [], [], []);
        var encryptedPackage = EncryptPackage(keyData, secretKey, package);

        var passwordHash = HashPassword(hash, passwordSalt, password, spinCount);
        var verifierHash = CryptographicOperations.HashData(hash, verifier);
        var encryptedVerifierInput = EncryptWith(encryptor, passwordHash, VerifierInputBlock, verifier);
        var encryptedVerifierValue = EncryptWith(encryptor, passwordHash, VerifierValueBlock, verifierHash);
        var encryptedKeyValue = EncryptWith(encryptor, passwordHash, KeyValueBlock, secretKey);

        var hmacValue = CryptographicOperations.HmacData(hash, hmacKey, encryptedPackage);
        var encryptedHmacKey = Cbc(secretKey, BlockIv(keyData, HmacKeyBlock), Pad(hmacKey, blockSize), encrypt: true);
        var encryptedHmacValue = Cbc(secretKey, BlockIv(keyData, HmacValueBlock), Pad(hmacValue, blockSize), encrypt: true);

        var parameters = $"saltSize=\"16\" blockSize=\"{blockSize}\" keyBits=\"{keyBits}\" hashSize=\"{hashSize}\" cipherAlgorithm=\"AES\" "
            + "cipherChaining=\"ChainingModeCBC\" hashAlgorithm=\"SHA512\"";
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n"
            + $"<encryption xmlns=\"{EncryptionNs}\" xmlns:p=\"{PasswordNs}\" xmlns:c=\"{CertificateNs}\">"
            + $"<keyData {parameters} saltValue=\"{Convert.ToBase64String(keyDataSalt)}\"/>"
            + $"<dataIntegrity encryptedHmacKey=\"{Convert.ToBase64String(encryptedHmacKey)}\" encryptedHmacValue=\"{Convert.ToBase64String(encryptedHmacValue)}\"/>"
            + $"<keyEncryptors><keyEncryptor uri=\"{PasswordNs}\"><p:encryptedKey spinCount=\"{spinCount}\" {parameters} "
            + $"saltValue=\"{Convert.ToBase64String(passwordSalt)}\" encryptedVerifierHashInput=\"{Convert.ToBase64String(encryptedVerifierInput)}\" "
            + $"encryptedVerifierHashValue=\"{Convert.ToBase64String(encryptedVerifierValue)}\" encryptedKeyValue=\"{Convert.ToBase64String(encryptedKeyValue)}\"/>"
            + "</keyEncryptor></keyEncryptors></encryption>";

        var xmlBytes = Encoding.UTF8.GetBytes(xml);
        var info = new byte[8 + xmlBytes.Length];
        BinaryPrimitives.WriteUInt16LittleEndian(info, 4);
        BinaryPrimitives.WriteUInt16LittleEndian(info.AsSpan(2), 4);
        BinaryPrimitives.WriteUInt32LittleEndian(info.AsSpan(4), 0x40);
        xmlBytes.CopyTo(info, 8);
        return (info, encryptedPackage);
    }

    /// <summary>H0 = H(salt + password), Hn = H(iterator + Hn-1) for every spin ([MS-OFFCRYPTO] 2.3.4.11).</summary>
    internal static byte[] HashPassword(HashAlgorithmName hash, ReadOnlySpan<byte> salt, string password, int spinCount)
    {
        var size = HashSize(hash);
        var passwordBytes = Encoding.Unicode.GetBytes(password);
        var first = new byte[salt.Length + passwordBytes.Length];
        salt.CopyTo(first);
        passwordBytes.CopyTo(first, salt.Length);

        Span<byte> buffer = stackalloc byte[4 + 64];
        Span<byte> result = stackalloc byte[64];
        CryptographicOperations.HashData(hash, first, buffer.Slice(4, size));
        for (var iterator = 0; iterator < spinCount; iterator++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, iterator);
            CryptographicOperations.HashData(hash, buffer[..(4 + size)], result);
            result[..size].CopyTo(buffer[4..]);
        }

        return buffer.Slice(4, size).ToArray();
    }

    /// <summary>The key for a block key: H(Hn + blockKey) cut to the key's length or filled with 0x36.</summary>
    internal static byte[] DeriveKey(HashAlgorithmName hash, byte[] passwordHash, byte[] blockKey, int keyBits)
    {
        var final = CryptographicOperations.HashData(hash, [.. passwordHash, .. blockKey]);
        return Fit(final, keyBits / 8);
    }

    private static byte[] DecryptWith(PasswordParameters encryptor, byte[] passwordHash, byte[] blockKey, byte[] data)
    {
        if (data.Length == 0 || data.Length % encryptor.Key.BlockSize != 0)
        {
            throw Invalid();
        }

        var key = DeriveKey(encryptor.Hash, passwordHash, blockKey, encryptor.Key.KeyBits);
        return Cbc(key, Fit(encryptor.Salt, encryptor.Key.BlockSize), data, encrypt: false);
    }

    private static byte[] EncryptWith(PasswordParameters encryptor, byte[] passwordHash, byte[] blockKey, byte[] data)
    {
        var key = DeriveKey(encryptor.Hash, passwordHash, blockKey, encryptor.Key.KeyBits);
        return Cbc(key, Fit(encryptor.Salt, encryptor.Key.BlockSize), Pad(data, encryptor.Key.BlockSize), encrypt: true);
    }

    private static void CheckIntegrity(EncryptionInfo info, byte[] secretKey, byte[] encryptedPackage)
    {
        var keyData = info.KeyData;
        var hashSize = HashSize(keyData.Hash);
        if (info.EncryptedHmacKey!.Length % keyData.BlockSize != 0 || info.EncryptedHmacValue!.Length % keyData.BlockSize != 0)
        {
            throw Invalid();
        }

        var hmacKey = Cbc(secretKey, BlockIv(keyData, HmacKeyBlock), info.EncryptedHmacKey, encrypt: false);
        var hmacValue = Cbc(secretKey, BlockIv(keyData, HmacValueBlock), info.EncryptedHmacValue, encrypt: false);
        if (hmacKey.Length < hashSize || hmacValue.Length < hashSize)
        {
            throw Invalid();
        }

        var actual = CryptographicOperations.HmacData(keyData.Hash, hmacKey.AsSpan(0, hashSize), encryptedPackage);
        if (!CryptographicOperations.FixedTimeEquals(actual, hmacValue.AsSpan(0, hashSize)))
        {
            throw new InvalidDataException("Die verschlüsselte Datei ist beschädigt: ihre Prüfsumme stimmt nicht.");
        }
    }

    private static byte[] DecryptPackage(KeyParameters keyData, byte[] secretKey, byte[] encryptedPackage)
    {
        if (encryptedPackage.Length < 8)
        {
            throw Invalid();
        }

        var size = BinaryPrimitives.ReadUInt64LittleEndian(encryptedPackage);
        var data = encryptedPackage.AsSpan(8);
        if (size > (ulong)data.Length)
        {
            throw Invalid();
        }

        var result = new byte[size];
        using var aes = Aes.Create();
        aes.Key = secretKey;
        var segment = new byte[SegmentLength];
        var blockSize = keyData.BlockSize;
        var offset = 0;
        for (uint index = 0; offset < result.Length; index++)
        {
            var plain = Math.Min(SegmentLength, result.Length - offset);
            var cipher = Math.Min(SegmentLength, data.Length - offset) / blockSize * blockSize;
            if (cipher < plain)
            {
                throw Invalid();
            }

            aes.DecryptCbc(data.Slice(offset, cipher), SegmentIv(keyData, index), segment, PaddingMode.None);
            segment.AsSpan(0, plain).CopyTo(result.AsSpan(offset));
            offset += plain;
        }

        return result;
    }

    private static byte[] EncryptPackage(KeyParameters keyData, byte[] secretKey, ReadOnlySpan<byte> package)
    {
        var blockSize = keyData.BlockSize;
        var fullSegments = package.Length / SegmentLength;
        var rest = package.Length % SegmentLength;
        var encryptedLength = (long)fullSegments * SegmentLength + (rest + blockSize - 1) / blockSize * blockSize;
        var result = new byte[8 + encryptedLength];
        BinaryPrimitives.WriteUInt64LittleEndian(result, (ulong)package.Length);

        using var aes = Aes.Create();
        aes.Key = secretKey;
        var last = new byte[SegmentLength];
        var offset = 0;
        for (uint index = 0; offset < package.Length; index++)
        {
            var count = Math.Min(SegmentLength, package.Length - offset);
            var source = package.Slice(offset, count);
            if (count % blockSize != 0)
            {
                var padded = (count + blockSize - 1) / blockSize * blockSize;
                Array.Clear(last);
                source.CopyTo(last);
                source = last.AsSpan(0, padded);
            }

            aes.EncryptCbc(source, SegmentIv(keyData, index), result.AsSpan(8 + offset), PaddingMode.None);
            offset += count;
        }

        return result;
    }

    private static byte[] SegmentIv(KeyParameters keyData, uint index)
    {
        var block = new byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(block, index);
        return BlockIv(keyData, block);
    }

    /// <summary>H(keyData salt + block) cut to the block size or filled with 0x36.</summary>
    private static byte[] BlockIv(KeyParameters keyData, byte[] block) =>
        Fit(CryptographicOperations.HashData(keyData.Hash, [.. keyData.Salt, .. block]), keyData.BlockSize);

    private static byte[] Cbc(byte[] key, byte[] iv, byte[] data, bool encrypt)
    {
        using var aes = Aes.Create();
        aes.Key = key;
        return encrypt ? aes.EncryptCbc(data, iv, PaddingMode.None) : aes.DecryptCbc(data, iv, PaddingMode.None);
    }

    private static byte[] Fit(byte[] bytes, int length)
    {
        if (bytes.Length == length)
        {
            return bytes;
        }

        var result = new byte[length];
        result.AsSpan().Fill(0x36);
        bytes.AsSpan(0, Math.Min(length, bytes.Length)).CopyTo(result);
        return result;
    }

    private static byte[] Pad(byte[] bytes, int blockSize)
    {
        var length = (bytes.Length + blockSize - 1) / blockSize * blockSize;
        return length == bytes.Length ? bytes : [.. bytes, .. new byte[length - bytes.Length]];
    }

    internal static int HashSize(HashAlgorithmName hash) => hash.Name switch
    {
        "SHA1" => 20,
        "SHA256" => 32,
        "SHA384" => 48,
        "SHA512" => 64,
        _ => throw new ArgumentOutOfRangeException(nameof(hash)),
    };

    private static void CheckSupported(KeyParameters parameters)
    {
        if (parameters.CipherAlgorithm != "AES" || parameters.CipherChaining != "ChainingModeCBC" || parameters.BlockSize != 16
            || parameters.KeyBits is not (128 or 192 or 256) || parameters.HashSize != HashSize(parameters.Hash) || parameters.Salt.Length == 0)
        {
            throw new Models.BusinessException(OfficeEncryption.UnsupportedText, OfficeEncryption.PasswordField);
        }
    }

    private static void CheckSupported(PasswordParameters encryptor)
    {
        CheckSupported(encryptor.Key);
        if (encryptor.SpinCount is < 0 or > MaxSpinCount)
        {
            throw Invalid();
        }
    }

    private static EncryptionInfo Parse(byte[] encryptionInfo)
    {
        var length = encryptionInfo.Length;
        while (length > 8 && encryptionInfo[length - 1] == 0)
        {
            length--;
        }

        KeyParameters? keyData = null;
        PasswordParameters? password = null;
        byte[]? hmacKey = null;
        byte[]? hmacValue = null;
        var hasCertificate = false;
        try
        {
            using var input = new MemoryStream(encryptionInfo, 8, length - 8, writable: false);
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            string? encryptorUri = null;
            while (reader.Read())
            {
                if (reader.NodeType != XmlNodeType.Element)
                {
                    continue;
                }

                switch (reader.NamespaceURI, reader.LocalName)
                {
                    case (EncryptionNs, "keyData"):
                        keyData ??= ReadKey(reader);
                        break;
                    case (EncryptionNs, "dataIntegrity"):
                        hmacKey = Base64(reader, "encryptedHmacKey");
                        hmacValue = Base64(reader, "encryptedHmacValue");
                        break;
                    case (EncryptionNs, "keyEncryptor"):
                        encryptorUri = reader.GetAttribute("uri");
                        hasCertificate |= encryptorUri == CertificateNs;
                        break;
                    case (PasswordNs, "encryptedKey") when encryptorUri == PasswordNs:
                        password ??= new PasswordParameters(ReadKey(reader), Int(reader, "spinCount"), Base64(reader, "encryptedVerifierHashInput"),
                            Base64(reader, "encryptedVerifierHashValue"), Base64(reader, "encryptedKeyValue"));
                        break;
                }
            }
        }
        catch (Exception exception) when (exception is XmlException or FormatException or OverflowException)
        {
            throw new InvalidDataException(InvalidText, exception);
        }

        if (keyData is null || password is null && !hasCertificate)
        {
            throw Invalid();
        }

        return new EncryptionInfo(keyData, password, hmacKey, hmacValue);
    }

    private static KeyParameters ReadKey(XmlReader reader)
    {
        var salt = Base64(reader, "saltValue");
        if (Int(reader, "saltSize") != salt.Length)
        {
            throw Invalid();
        }

        var hash = reader.GetAttribute("hashAlgorithm") switch
        {
            "SHA1" or "SHA-1" => HashAlgorithmName.SHA1,
            "SHA256" => HashAlgorithmName.SHA256,
            "SHA384" => HashAlgorithmName.SHA384,
            "SHA512" => HashAlgorithmName.SHA512,
            _ => throw new Models.BusinessException(OfficeEncryption.UnsupportedText, OfficeEncryption.PasswordField),
        };

        return new KeyParameters(salt.Length, Int(reader, "blockSize"), Int(reader, "keyBits"), Int(reader, "hashSize"),
            reader.GetAttribute("cipherAlgorithm") ?? "", reader.GetAttribute("cipherChaining") ?? "", hash, salt);
    }

    private static int Int(XmlReader reader, string name) =>
        int.Parse(reader.GetAttribute(name) ?? throw Invalid(), NumberStyles.None, CultureInfo.InvariantCulture);

    private static byte[] Base64(XmlReader reader, string name) => Convert.FromBase64String(reader.GetAttribute(name) ?? throw Invalid());

    private const string InvalidText = "Die verschlüsselte Datei ist beschädigt.";

    private static InvalidDataException Invalid() => new(InvalidText);

    internal sealed record KeyParameters(int SaltSize, int BlockSize, int KeyBits, int HashSize, string CipherAlgorithm, string CipherChaining,
        HashAlgorithmName Hash, byte[] Salt);

    private sealed record PasswordParameters(KeyParameters Key, int SpinCount, byte[] EncryptedVerifierHashInput, byte[] EncryptedVerifierHashValue,
        byte[] EncryptedKeyValue)
    {
        public HashAlgorithmName Hash => Key.Hash;

        public byte[] Salt => Key.Salt;
    }

    private sealed record EncryptionInfo(KeyParameters KeyData, PasswordParameters? Password, byte[]? EncryptedHmacKey, byte[]? EncryptedHmacValue);
}
