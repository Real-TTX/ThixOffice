using System.Buffers.Binary;
using System.Text;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// The "\u0006DataSpaces" storage of an encrypted package ([MS-OFFCRYPTO] 2.1 and 2.2): the version, the map naming
/// EncryptedPackage's data space, the data space's transform and the transform's description – what Office writes beside
/// EncryptionInfo and EncryptedPackage. Also recognizes the data spaces of Information Rights Management.
/// </summary>
internal static class DataSpaces
{
    public const string Storage = "\u0006DataSpaces";
    public const string VersionPath = Storage + "/Version";
    public const string MapPath = Storage + "/DataSpaceMap";
    public const string DefinitionPath = Storage + "/DataSpaceInfo/StrongEncryptionDataSpace";
    public const string TransformPath = Storage + "/TransformInfo/StrongEncryptionTransform/\u0006Primary";

    private const string EncryptionTransformId = "{FF9A3F03-56EF-4613-BDD5-5A41C1D07246}";
    private const string EncryptionTransformName = "Microsoft.Container.EncryptionTransform";

    /// <summary>Adds the four streams of the data spaces of a password-encrypted package.</summary>
    public static void AddTo(CompoundFileWriter writer)
    {
        writer.AddStream(VersionPath, Version());
        writer.AddStream(MapPath, Map());
        writer.AddStream(DefinitionPath, Definition());
        writer.AddStream(TransformPath, Transform());
    }

    /// <summary>
    /// True when the file is protected by Information Rights Management: its data spaces name a DRM transform or data space
    /// ("DRMEncryptedDataSpace", "DRMEncryptedTransform", "\u0009DRMDataSpace" …), or it holds "\u0009DRMContent".
    /// </summary>
    public static bool IsRightsManaged(CompoundFile file) =>
        file.Entries.Any(entry => (entry.Path.StartsWith(Storage + "/", StringComparison.OrdinalIgnoreCase) && entry.Name.Contains("DRM", StringComparison.OrdinalIgnoreCase))
            || entry.Path.Equals("\u0009DRMContent", StringComparison.OrdinalIgnoreCase));

    /// <summary>DataSpaceVersionInfo: "Microsoft.Container.DataSpaces", reader/updater/writer version 1.0.</summary>
    internal static byte[] Version()
    {
        using var output = new MemoryStream();
        WriteString(output, "Microsoft.Container.DataSpaces");
        WriteVersions(output);
        return output.ToArray();
    }

    /// <summary>DataSpaceMap: one entry mapping the stream EncryptedPackage to StrongEncryptionDataSpace.</summary>
    internal static byte[] Map()
    {
        using var entry = new MemoryStream();
        WriteUInt32(entry, 1);
        WriteUInt32(entry, 0);
        WriteString(entry, "EncryptedPackage");
        WriteString(entry, "StrongEncryptionDataSpace");

        using var output = new MemoryStream();
        WriteUInt32(output, 8);
        WriteUInt32(output, 1);
        WriteUInt32(output, (uint)entry.Length + 4);
        entry.WriteTo(output);
        return output.ToArray();
    }

    /// <summary>DataSpaceDefinition: the transform StrongEncryptionTransform.</summary>
    internal static byte[] Definition()
    {
        using var output = new MemoryStream();
        WriteUInt32(output, 8);
        WriteUInt32(output, 1);
        WriteString(output, "StrongEncryptionTransform");
        return output.ToArray();
    }

    /// <summary>The transform's TransformInfoHeader and EncryptionTransformInfo (no name, block size 0, cipher mode 0, reserved 4).</summary>
    internal static byte[] Transform()
    {
        using var output = new MemoryStream();
        WriteUInt32(output, (uint)(4 + 4 + 4 + Encoding.Unicode.GetByteCount(EncryptionTransformId)));
        WriteUInt32(output, 1);
        WriteString(output, EncryptionTransformId);
        WriteString(output, EncryptionTransformName);
        WriteVersions(output);
        WriteUInt32(output, 0);
        WriteUInt32(output, 0);
        WriteUInt32(output, 0);
        WriteUInt32(output, 4);
        return output.ToArray();
    }

    private static void WriteVersions(Stream output)
    {
        for (var index = 0; index < 3; index++)
        {
            Span<byte> version = [1, 0, 0, 0];
            output.Write(version);
        }
    }

    /// <summary>UNICODE-LP-P4: the length in bytes, the UTF-16LE characters, padding to four bytes.</summary>
    private static void WriteString(Stream output, string text)
    {
        var bytes = Encoding.Unicode.GetBytes(text);
        WriteUInt32(output, (uint)bytes.Length);
        output.Write(bytes);
        for (var index = bytes.Length; index % 4 != 0; index++)
        {
            output.WriteByte(0);
        }
    }

    private static void WriteUInt32(Stream output, uint value)
    {
        Span<byte> bytes = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32LittleEndian(bytes, value);
        output.Write(bytes);
    }
}
