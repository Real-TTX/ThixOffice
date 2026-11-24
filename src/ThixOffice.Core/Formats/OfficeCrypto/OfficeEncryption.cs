using System.Buffers.Binary;
using System.IO.Compression;
using System.Xml;
using ThixOffice.Core.Formats.Odf;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// Documents protected with "Mit Kennwort verschlüsseln" ([MS-OFFCRYPTO], ECMA-376 Part 2): an OLE compound file holding
/// EncryptionInfo and EncryptedPackage (the encrypted docx/xlsx/pptx). <see cref="Detect"/> tells such files apart from ordinary
/// ones, <see cref="Decrypt"/> opens one with the password the user typed – a wrong password is refused, nothing is guessed –,
/// <see cref="Encrypt"/> writes one the way current Office does (Agile, SHA-512, AES-256). Synchronous and UI-free; the stream
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

    internal const string OdfText = "Verschlüsselte OpenDocument-Dateien werden noch nicht unterstützt. "
        + "Entfernen Sie das Kennwort in dem Programm, mit dem die Datei erstellt wurde.";

    internal const string NotEncryptedText = "Die Datei ist nicht mit einem Kennwort verschlüsselt.";

    internal const string TooLongText = "Das Kennwort darf höchstens 255 Zeichen lang sein.";

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

            return IsEncryptedOdf(file) ? EncryptedFileKind.OdfEncrypted : EncryptedFileKind.None;
        }
        finally
        {
            file.Position = position;
        }
    }

    /// <summary>
    /// The package (the ZIP of a docx/xlsx/pptx) inside the encrypted <paramref name="file"/>. A wrong password is a
    /// <see cref="WrongPasswordException"/>; a kind that cannot be opened (older encryption, IRM, binary format, encrypted
    /// OpenDocument, no encryption at all) is a <see cref="BusinessException"/> saying so; a damaged file – including a failed
    /// integrity check – is an <see cref="InvalidDataException"/>.
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
                throw new BusinessException(IsEncryptedOdf(file) ? OdfText : NotEncryptedText);
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
    /// Writes <paramref name="package"/> (the ZIP of a docx/xlsx/pptx) encrypted with <paramref name="password"/> into
    /// <paramref name="output"/> as Word, Excel and PowerPoint write it: Agile encryption with SHA-512, AES-256-CBC, 100,000 spins,
    /// random salts and keys, the integrity HMAC, and the data spaces.
    /// </summary>
    public static void Encrypt(ReadOnlySpan<byte> package, string password, Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        ArgumentException.ThrowIfNullOrEmpty(password);
        CheckPassword(password);
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

    /// <summary>A ZIP whose META-INF/manifest.xml describes encrypted parts (manifest:encryption-data).</summary>
    private static bool IsEncryptedOdf(Stream file)
    {
        file.Position = 0;
        Span<byte> head = stackalloc byte[4];
        if (file.ReadAtLeast(head, 4, throwOnEndOfStream: false) < 4 || head[0] != (byte)'P' || head[1] != (byte)'K')
        {
            return false;
        }

        file.Position = 0;
        try
        {
            using var zip = new ZipArchive(file, ZipArchiveMode.Read, leaveOpen: true);
            if (zip.GetEntry("META-INF/manifest.xml") is not { } manifest || manifest.Length > 16 * 1024 * 1024)
            {
                return false;
            }

            using var input = manifest.Open();
            using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null });
            var manifestNs = OdfXml.ManifestNs.NamespaceName;
            while (reader.Read())
            {
                if (reader.NodeType == XmlNodeType.Element && reader.LocalName == "encryption-data" && reader.NamespaceURI == manifestNs)
                {
                    return true;
                }
            }

            return false;
        }
        catch (Exception exception) when (exception is InvalidDataException or XmlException or IOException or NotSupportedException)
        {
            return false;
        }
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
