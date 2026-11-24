using System.Buffers.Binary;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// Documents protected with "Mit Kennwort verschlüsseln" ([MS-OFFCRYPTO], ECMA-376 Part 2): an OLE compound file holding
/// EncryptionInfo and EncryptedPackage (the encrypted docx/xlsx/pptx). <see cref="Detect"/> tells such files apart from ordinary
/// ones, <see cref="Decrypt"/> opens one with the password the user typed – a wrong password is refused, nothing is guessed –,
/// <see cref="Encrypt"/> writes one the way current Office does (Agile, SHA-512, AES-256). OpenDocument packages encrypted with a
/// password go through <see cref="OdfEncryption"/> behind the same three calls. Synchronous and UI-free; the stream
/// is always the whole file and must be seekable.
/// </summary>
public static class OfficeEncryption
{
    /// <summary>The field of the errors about the password (<see cref="BusinessException.Field"/>).</summary>
    public const string PasswordField = "Password";

    /// <summary>Office's longest password.</summary>
    public const int MaxPasswordLength = 255;

    internal const string EncryptionInfoPath = "EncryptionInfo";
    internal const string EncryptedPackagePath = "EncryptedPackage";
    private const int MaxEncryptionInfoLength = 1024 * 1024;

    internal const string UnsupportedText = "Diese Datei ist mit einem älteren Verfahren verschlüsselt, das nicht unterstützt wird. "
        + "Öffnen Sie sie in dem Programm, mit dem sie erstellt wurde, und speichern Sie sie dort mit einem aktuellen Kennwortschutz oder ohne Kennwort.";

    internal const string CertificateText = "Diese Datei ist mit einem Zertifikat statt mit einem Kennwort verschlüsselt und kann nicht geöffnet werden.";

    internal const string RightsText = "Diese Datei ist durch Information Rights Management (IRM) geschützt. "
        + "Solche Dateien lassen sich nur mit Microsoft Office und einer Berechtigung Ihrer Organisation öffnen.";

    internal const string BinaryText = "Diese Datei ist kein Office Open XML-Dokument, sondern in einem älteren Binärformat (etwa .doc, .xls oder .ppt) "
        + "gespeichert. Dieses Format wird nicht unterstützt.";

    internal const string NotEncryptedText = "Die Datei ist nicht mit einem Kennwort verschlüsselt.";

    internal const string TooLongText = "Das Kennwort darf höchstens 255 Zeichen lang sein.";

    /// <summary>The field of the repeated password ("Kennwort bestätigen").</summary>
    public const string ConfirmationField = "PasswordConfirmation";

    internal const string EmptyText = "Bitte geben Sie ein Kennwort ein.";

    internal const string MismatchText = "Die Kennwörter stimmen nicht überein.";

    /// <summary>
    /// The types an encrypted file can be: the Office Open XML packages (Agile encryption) and the OpenDocument packages (their own
    /// encryption, <see cref="OdfEncryption"/>).
    /// </summary>
    private static readonly HashSet<string> EncryptableExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".docx", ".docm", ".dotx", ".dotm", ".xlsx", ".xlsm", ".xltx", ".xltm", ".pptx", ".pptm", ".potx", ".potm",
        ".odt", ".ott", ".ods", ".ots", ".odp", ".otp",
    };

    /// <summary>True when a file of <paramref name="extension"/> (".docx") can be saved with a password.</summary>
    public static bool CanEncrypt(string extension) => EncryptableExtensions.Contains(extension);

    /// <summary>
    /// Checks a new password of "Dokument verschlüsseln": <paramref name="password"/> typed twice; empty removes the protection of a
    /// document that has one (<paramref name="hasPassword"/>), else it is refused. Returns the password to set, null = none.
    /// </summary>
    public static string? CheckNewPassword(string? password, string? confirmation, bool hasPassword)
    {
        password ??= "";
        confirmation ??= "";
        if (password.Length == 0 && confirmation.Length == 0)
        {
            return hasPassword ? null : throw new BusinessException(EmptyText, PasswordField);
        }

        CheckPassword(password);
        if (!string.Equals(password, confirmation, StringComparison.Ordinal))
        {
            throw new BusinessException(MismatchText, ConfirmationField);
        }

        return password;
    }

    /// <summary>The refusal of a kind that cannot be opened (null for <see cref="EncryptedFileKind.None"/> and the kinds Decrypt opens).</summary>
    internal static BusinessException? Refusal(EncryptedFileKind kind) => kind switch
    {
        EncryptedFileKind.OfficeOther => new BusinessException(UnsupportedText),
        EncryptedFileKind.Rights => new BusinessException(RightsText),
        EncryptedFileKind.CompoundNotEncrypted => new BusinessException(BinaryText),
        _ => OdfEncryption.Refusal(kind),
    };

    /// <summary>
    /// What kind of file <paramref name="file"/> is, from its first bytes and – for a compound file – its directory and
    /// EncryptionInfo version (the position is kept). A compound file whose structure is broken is an <see cref="InvalidDataException"/>.
    /// </summary>
    public static EncryptedFileKind Detect(Stream file)
    {
        CheckStream(file);
        var position = file.Position;
        try
        {
            if (CompoundFile.HasSignature(file))
            {
                return KindOf(CompoundFile.Open(file));
            }

            return OdfEncryption.KindOf(file);
        }
        finally
        {
            file.Position = position;
        }
    }

    /// <summary>
    /// The package (the ZIP of a docx/xlsx/pptx, or the plain package of an encrypted odt/ods/odp) inside the encrypted
    /// <paramref name="file"/>. A wrong password is a <see cref="WrongPasswordException"/>; a kind that cannot be opened (older
    /// encryption, IRM, binary format, Blowfish or LibreOffice's newer OpenDocument encryption, no encryption at all) is a
    /// <see cref="BusinessException"/> saying so; a damaged file – including a failed integrity check – is an <see cref="InvalidDataException"/>.
    /// </summary>
    public static byte[] Decrypt(Stream file, string password)
    {
        CheckStream(file);
        CheckPassword(password);
        var position = file.Position;
        try
        {
            if (!CompoundFile.HasSignature(file))
            {
                return OdfEncryption.KindOf(file) == EncryptedFileKind.None
                    ? throw new BusinessException(NotEncryptedText)
                    : OdfEncryption.Decrypt(file, password);
            }

            var compound = CompoundFile.Open(file);
            return KindOf(compound) switch
            {
                EncryptedFileKind.OfficeAgile => AgileEncryption.Decrypt(EncryptionInfo(compound), compound.ReadStream(EncryptedPackagePath), password),
                EncryptedFileKind.OfficeStandard => StandardEncryption.Decrypt(EncryptionInfo(compound), compound.ReadStream(EncryptedPackagePath), password),
                EncryptedFileKind.Rights => throw new BusinessException(RightsText),
                EncryptedFileKind.CompoundNotEncrypted => throw new BusinessException(BinaryText),
                _ => throw new BusinessException(UnsupportedText),
            };
        }
        finally
        {
            file.Position = position;
        }
    }

    /// <summary>
    /// Writes <paramref name="package"/> encrypted with <paramref name="password"/> into <paramref name="output"/>. The package's own
    /// kind decides how: an OpenDocument package (odt/ods/odp) is encrypted as LibreOffice does it (<see cref="OdfEncryption.Encrypt"/>),
    /// anything else (the ZIP of a docx/xlsx/pptx) as Word, Excel and PowerPoint write it: Agile encryption with SHA-512, AES-256-CBC,
    /// 100,000 spins, random salts and keys, the integrity HMAC, and the data spaces.
    /// </summary>
    public static void Encrypt(ReadOnlySpan<byte> package, string password, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(password);
        CheckPassword(password);
        if (OdfEncryption.IsOdfPackage(package))
        {
            OdfEncryption.Encrypt(package, password, output);
            return;
        }

        var (info, encryptedPackage) = AgileEncryption.Encrypt(package, password);
        var writer = new CompoundFileWriter();
        DataSpaces.AddTo(writer);
        writer.AddStream(EncryptionInfoPath, info);
        writer.AddStream(EncryptedPackagePath, encryptedPackage);
        writer.Save(output);
    }

    private static EncryptedFileKind KindOf(CompoundFile compound)
    {
        if (DataSpaces.IsRightsManaged(compound))
        {
            return EncryptedFileKind.Rights;
        }

        if (compound.Find(EncryptionInfoPath) is not { IsStorage: false })
        {
            return compound.Exists(EncryptedPackagePath) ? EncryptedFileKind.OfficeOther : EncryptedFileKind.CompoundNotEncrypted;
        }

        var info = EncryptionInfo(compound);
        var major = BinaryPrimitives.ReadUInt16LittleEndian(info);
        var minor = BinaryPrimitives.ReadUInt16LittleEndian(info.AsSpan(2));
        var flags = BinaryPrimitives.ReadUInt32LittleEndian(info.AsSpan(4));
        if (major == 4 && minor == 4 && flags == 0x40)
        {
            return EncryptedFileKind.OfficeAgile;
        }

        if (major is >= 2 and <= 4 && minor == 2 && (flags & StandardEncryption.FlagAes) != 0 && (flags & StandardEncryption.FlagExternal) == 0)
        {
            return EncryptedFileKind.OfficeStandard;
        }

        return EncryptedFileKind.OfficeOther;
    }

    private static byte[] EncryptionInfo(CompoundFile compound)
    {
        var entry = compound.Find(EncryptionInfoPath);
        if (entry is null || entry.Size is < 12 or > MaxEncryptionInfoLength)
        {
            throw new InvalidDataException("Die verschlüsselte Datei ist beschädigt.");
        }

        return compound.ReadStream(EncryptionInfoPath);
    }

    private static void CheckStream(Stream file)
    {
        ArgumentNullException.ThrowIfNull(file);
        if (!file.CanSeek || !file.CanRead)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(file));
        }
    }

    private static void CheckPassword(string password)
    {
        ArgumentNullException.ThrowIfNull(password);
        if (password.Length > MaxPasswordLength)
        {
            throw new BusinessException(TooLongText, PasswordField);
        }
    }
}
