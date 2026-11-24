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

    /// <summary>An OpenDocument package whose parts are encrypted (manifest:encryption-data); detected only.</summary>
    OdfEncrypted,
}
