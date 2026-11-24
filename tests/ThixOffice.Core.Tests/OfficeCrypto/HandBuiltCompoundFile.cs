using System.Buffers.Binary;
using System.Text;

namespace ThixOffice.Core.Tests.OfficeCrypto;

/// <summary>
/// A version 4 compound file (4096-byte sectors) laid out by hand, byte for byte, without the product's writer:
/// sector 0 = FAT, 1 = directory, 2 = mini FAT, 3 = mini stream, 4–6 = the stream "Big" (10,000 bytes).
/// Root → "Big", "Small" (100 bytes, mini sectors 0–1), storage "Store" → "Inner" (50 bytes, mini sector 2).
/// </summary>
internal static class HandBuiltCompoundFile
{
    public const int SectorSize = 4096;
    public const uint End = 0xFFFFFFFE;
    public const uint Free = 0xFFFFFFFF;

    public static byte[] Big { get; } = Enumerable.Range(0, 10_000).Select(index => (byte)(index * 7)).ToArray();

    public static byte[] Small { get; } = Enumerable.Range(0, 100).Select(index => (byte)(255 - index)).ToArray();

    public static byte[] Inner { get; } = Encoding.ASCII.GetBytes("Fifty bytes of a stream in a nested storage!!!!!!!");

    /// <summary>The file; <paramref name="fat"/> may change FAT entries before it is written (corruption tests).</summary>
    public static byte[] Build(Action<uint[]>? fat = null, Action<byte[]>? directory = null)
    {
        var file = new byte[SectorSize * 8];
        var header = file.AsSpan(0, 512);
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(header);
        BinaryPrimitives.WriteUInt16LittleEndian(header[24..], 0x3E);
        BinaryPrimitives.WriteUInt16LittleEndian(header[26..], 4);
        BinaryPrimitives.WriteUInt16LittleEndian(header[28..], 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(header[30..], 12);
        BinaryPrimitives.WriteUInt16LittleEndian(header[32..], 6);
        BinaryPrimitives.WriteUInt32LittleEndian(header[40..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[44..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[48..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[56..], 4096);
        BinaryPrimitives.WriteUInt32LittleEndian(header[60..], 2);
        BinaryPrimitives.WriteUInt32LittleEndian(header[64..], 1);
        BinaryPrimitives.WriteUInt32LittleEndian(header[68..], End);
        BinaryPrimitives.WriteUInt32LittleEndian(header[72..], 0);
        for (var index = 0; index < 109; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(header[(76 + index * 4)..], index == 0 ? 0 : Free);
        }

        var fatEntries = Enumerable.Repeat(Free, SectorSize / 4).ToArray();
        fatEntries[0] = 0xFFFFFFFD;
        fatEntries[1] = End;
        fatEntries[2] = End;
        fatEntries[3] = End;
        fatEntries[4] = 5;
        fatEntries[5] = 6;
        fatEntries[6] = End;
        fat?.Invoke(fatEntries);
        WriteEntries(Sector(file, 0), fatEntries);

        var miniFat = Enumerable.Repeat(Free, SectorSize / 4).ToArray();
        miniFat[0] = 1;
        miniFat[1] = End;
        miniFat[2] = End;
        WriteEntries(Sector(file, 2), miniFat);

        var miniStream = Sector(file, 3);
        Small.CopyTo(miniStream);
        Inner.CopyTo(miniStream[128..]);
        Big.CopyTo(file.AsSpan((4 + 1) * SectorSize));

        var entries = new byte[SectorSize];
        for (var index = 0; index < SectorSize / 128; index++)
        {
            Entry(entries, index, "", 0, Free, Free, Free, 0, 0);
        }

        Entry(entries, 0, "Root Entry", 5, Free, Free, 2, 3, 192);
        Entry(entries, 1, "Big", 2, Free, Free, Free, 4, Big.Length);
        Entry(entries, 2, "Small", 2, 1, 3, Free, 0, Small.Length);
        Entry(entries, 3, "Store", 1, Free, Free, 4, 0, 0);
        Entry(entries, 4, "Inner", 2, Free, Free, Free, 2, Inner.Length);
        directory?.Invoke(entries);
        entries.CopyTo(Sector(file, 1));
        return file;
    }

    private static Span<byte> Sector(byte[] file, int sector) => file.AsSpan((sector + 1) * SectorSize, SectorSize);

    private static void WriteEntries(Span<byte> sector, uint[] entries)
    {
        for (var index = 0; index < entries.Length; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(sector[(index * 4)..], entries[index]);
        }
    }

    public static void Entry(byte[] directory, int index, string name, byte type, uint left, uint right, uint child, uint start, long size)
    {
        var span = directory.AsSpan(index * 128, 128);
        span.Clear();
        var nameBytes = Encoding.Unicode.GetBytes(name);
        nameBytes.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[64..], (ushort)(name.Length == 0 ? 0 : nameBytes.Length + 2));
        span[66] = type;
        span[67] = 1;
        BinaryPrimitives.WriteUInt32LittleEndian(span[68..], left);
        BinaryPrimitives.WriteUInt32LittleEndian(span[72..], right);
        BinaryPrimitives.WriteUInt32LittleEndian(span[76..], child);
        BinaryPrimitives.WriteUInt32LittleEndian(span[116..], start);
        BinaryPrimitives.WriteUInt64LittleEndian(span[120..], (ulong)size);
    }
}
