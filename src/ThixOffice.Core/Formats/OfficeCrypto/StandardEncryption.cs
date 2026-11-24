using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// ECMA-376 Standard Encryption ([MS-OFFCRYPTO] 2.3.4.5–2.3.4.9), Office 2007's password encryption: a binary EncryptionInfo
/// (header + verifier), SHA-1 with 50,000 spins, the key derived through the 0x36/0x5C buffers, AES in ECB mode over the package.
/// </summary>
internal static class StandardEncryption
{
    internal const uint FlagCryptoApi = 0x04;
    internal const uint FlagExternal = 0x10;
    internal const uint FlagAes = 0x20;
    private const int SpinCount = 50_000;

    public static byte[] Decrypt(byte[] encryptionInfo, byte[] encryptedPackage, string password)
    {
        var info = encryptionInfo.AsSpan();
        if (info.Length < 12)
        {
            throw Invalid();
        }

        var headerSize = BinaryPrimitives.ReadUInt32LittleEndian(info[8..]);
        if (headerSize < 32 || headerSize > info.Length - 12)
        {
            throw Invalid();
        }

        var header = info.Slice(12, (int)headerSize);
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(header);
        var algorithm = BinaryPrimitives.ReadUInt32LittleEndian(header[8..]);
        var hashAlgorithm = BinaryPrimitives.ReadUInt32LittleEndian(header[12..]);
        var keyBits = (int)BinaryPrimitives.ReadUInt32LittleEndian(header[16..]);
        if ((flags & FlagAes) == 0 || (flags & FlagExternal) != 0 || hashAlgorithm is not (0 or 0x8004))
        {
            throw new Models.BusinessException(OfficeEncryption.UnsupportedText, OfficeEncryption.PasswordField);
        }

        var expectedBits = algorithm switch
        {
            0 or 0x660E => 128,
            0x660F => 192,
            0x6610 => 256,
            _ => throw new Models.BusinessException(OfficeEncryption.UnsupportedText, OfficeEncryption.PasswordField),
        };
        if (keyBits == 0)
        {
            keyBits = expectedBits;
        }

        if (keyBits != expectedBits)
        {
            throw Invalid();
        }

        var verifier = info[(12 + (int)headerSize)..];
        if (verifier.Length < 4 + 16 + 16 + 4 + 32 || BinaryPrimitives.ReadUInt32LittleEndian(verifier) != 16)
        {
            throw Invalid();
        }

        var salt = verifier.Slice(4, 16).ToArray();
        var encryptedVerifier = verifier.Slice(20, 16).ToArray();
        var verifierHashSize = BinaryPrimitives.ReadUInt32LittleEndian(verifier[36..]);
        var encryptedVerifierHash = verifier.Slice(40, 32).ToArray();
        if (verifierHashSize != 20)
        {
            throw Invalid();
        }

        var key = DeriveKey(salt, password, keyBits / 8);
        using var aes = Aes.Create();
        aes.Key = key;
        var plainVerifier = aes.DecryptEcb(encryptedVerifier, PaddingMode.None);
        var plainHash = aes.DecryptEcb(encryptedVerifierHash, PaddingMode.None);
        if (!CryptographicOperations.FixedTimeEquals(SHA1.HashData(plainVerifier), plainHash.AsSpan(0, 20)))
        {
            throw new WrongPasswordException();
        }

        if (encryptedPackage.Length < 8)
        {
            throw Invalid();
        }

        var size = BinaryPrimitives.ReadUInt64LittleEndian(encryptedPackage);
        var data = encryptedPackage.AsSpan(8);
        var whole = data.Length / 16 * 16;
        if (size > (ulong)whole)
        {
            throw Invalid();
        }

        var needed = ((int)size + 15) / 16 * 16;
        var plain = new byte[needed];
        aes.DecryptEcb(data[..needed], plain, PaddingMode.None);
        return needed == (int)size ? plain : plain[..(int)size];
    }

    /// <summary>[MS-OFFCRYPTO] 2.3.4.7: the spun SHA-1 of salt and password, H(Hn + block 0), then X1 + X2 cut to the key's length.</summary>
    internal static byte[] DeriveKey(byte[] salt, string password, int keyLength)
    {
        var hash = SHA1.HashData([.. salt, .. Encoding.Unicode.GetBytes(password)]);
        var buffer = new byte[24];
        for (var iterator = 0; iterator < SpinCount; iterator++)
        {
            BinaryPrimitives.WriteInt32LittleEndian(buffer, iterator);
            hash.CopyTo(buffer, 4);
            hash = SHA1.HashData(buffer);
        }

        var final = SHA1.HashData([.. hash, 0, 0, 0, 0]);
        var first = new byte[64];
        var second = new byte[64];
        Array.Fill(first, (byte)0x36);
        Array.Fill(second, (byte)0x5C);
        for (var index = 0; index < final.Length; index++)
        {
            first[index] ^= final[index];
            second[index] ^= final[index];
        }

        byte[] derived = [.. SHA1.HashData(first), .. SHA1.HashData(second)];
        return derived[..keyLength];
    }

    private static InvalidDataException Invalid() => new("Die verschlüsselte Datei ist beschädigt.");
}
