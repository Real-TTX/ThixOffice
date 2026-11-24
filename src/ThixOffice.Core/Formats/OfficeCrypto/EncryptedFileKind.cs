namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>What <see cref="OfficeEncryption.Detect"/> found in a file.</summary>
public enum EncryptedFileKind
{
    /// <summary>Neither a compound file nor an encrypted OpenDocument file: read it as usual.</summary>
    None,

    /// <summary>Office's password encryption since Office 2010 (ECMA-376 Agile Encryption): <see cref="OfficeEncryption.Decrypt"/> opens it.</summary>
    OfficeAgile,

    /// <summary>Office 2007's password encryption (ECMA-376 Standard Encryption, AES): <see cref="OfficeEncryption.Decrypt"/> opens it.</summary>
    OfficeStandard,

    /// <summary>An encryption this code does not open (RC4, RC4 CryptoAPI, extensible encryption).</summary>
    OfficeOther,

    /// <summary>Information Rights Management (IRM/DRM): only Microsoft Office with a licence of the organization opens it.</summary>
    Rights,

    /// <summary>A compound file without encryption: an old binary document (.doc, .xls, .ppt), perhaps renamed.</summary>
    CompoundNotEncrypted,

    /// <summary>
    /// An OpenDocument package whose parts are encrypted with AES-CBC and PBKDF2 (ODF 1.2, LibreOffice's "Mit Kennwort speichern"):
    /// <see cref="OfficeEncryption.Decrypt"/> opens it (<see cref="OdfEncryption"/>).
    /// </summary>
    OdfEncrypted,

    /// <summary>An OpenDocument package encrypted with Blowfish (OpenOffice.org, LibreOffice before 3.4): refused in words.</summary>
    OdfBlowfish,

    /// <summary>LibreOffice's newer encryption (AES-GCM, Argon2id, the whole package encrypted, since 24.2): refused in words.</summary>
    OdfModern,

    /// <summary>An OpenDocument package encrypted with an algorithm none of the above names: refused in words.</summary>
    OdfOther,
}
