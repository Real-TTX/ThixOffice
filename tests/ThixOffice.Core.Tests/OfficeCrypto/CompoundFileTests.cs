using System.Buffers.Binary;
using System.Diagnostics;
using System.Text;
using ThixOffice.Core.Formats.OfficeCrypto;

namespace ThixOffice.Core.Tests.OfficeCrypto;

/// <summary>The compound file reader and writer (MS-CFB): round trips, a hand-built version 4 file, damaged files.</summary>
public sealed class CompoundFileTests
{
    private static byte[] Bytes(int length, int seed) => Enumerable.Range(0, length).Select(index => (byte)(index * 31 + seed)).ToArray();

    private static byte[] Save(CompoundFileWriter writer)
    {
        using var output = new MemoryStream();
        writer.Save(output);
        return output.ToArray();
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    public void The_reader_reads_what_the_writer_wrote_with_storages_and_mini_streams(int version)
    {
        var streams = new Dictionary<string, byte[]>
        {
            ["EncryptionInfo"] = Bytes(700, 1),
            ["EncryptedPackage"] = Bytes(20_000, 2),
            ["Empty"] = [],
            ["Exactly4096"] = Bytes(4096, 3),
            ["Just4095"] = Bytes(4095, 4),
            ["\u0006DataSpaces/Version"] = Bytes(76, 5),
            ["\u0006DataSpaces/DataSpaceMap"] = Bytes(112, 6),
            ["\u0006DataSpaces/TransformInfo/StrongEncryptionTransform/\u0006Primary"] = Bytes(200, 7),
            ["A/B/C/D/Deep"] = Bytes(65, 8),
        };
        var writer = new CompoundFileWriter(version);
        foreach (var (path, data) in streams)
        {
            writer.AddStream(path, data);
        }

        writer.AddStorage("Leer");
        var file = CompoundFile.Open(new MemoryStream(Save(writer)));

        Assert.Equal(version, file.MajorVersion);
        foreach (var (path, data) in streams)
        {
            Assert.Equal(data, file.ReadStream(path));
            Assert.Equal(data.Length, file.Find(path)!.Size);
        }

        Assert.True(file.Find("Leer")!.IsStorage);
        Assert.True(file.Find("\u0006DataSpaces/TransformInfo")!.IsStorage);
        Assert.True(file.Exists("encryptioninfo"));
        Assert.Null(file.TryReadStream("Fehlt"));
        Assert.Null(file.TryReadStream("\u0006DataSpaces"));
        Assert.Throws<InvalidDataException>(() => file.ReadStream("Fehlt"));
        Assert.Equal(streams.Count + 8, file.Entries.Count);
    }

    [Fact]
    public void A_file_with_more_than_109_FAT_sectors_uses_DIFAT_sectors()
    {
        var big = Bytes(9 * 1024 * 1024 + 3, 9);
        var writer = new CompoundFileWriter();
        writer.AddStream("Big", big);
        writer.AddStream("Small", Bytes(10, 1));
        var bytes = Save(writer);

        Assert.True(BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(72)) > 0, "DIFAT sectors");
        Assert.Equal(0, bytes.Length % 512);
        var file = CompoundFile.Open(new MemoryStream(bytes));
        Assert.Equal(big, file.ReadStream("Big"));
        Assert.Equal(Bytes(10, 1), file.ReadStream("Small"));
    }

    [Fact]
    public void Every_storage_is_a_valid_red_black_tree_in_the_order_of_the_format()
    {
        var writer = new CompoundFileWriter();
        var names = Enumerable.Range(0, 37).Select(index => new string((char)('a' + index % 26), 1 + index % 5) + index).ToList();
        foreach (var name in names)
        {
            writer.AddStream(name, Bytes(5, 0));
            writer.AddStream("Sub/" + name, Bytes(5, 0));
        }

        var directory = RawDirectory(Save(writer));
        foreach (var storage in directory.Where(entry => entry.Type is 1 or 5))
        {
            if (storage.Child == uint.MaxValue)
            {
                continue;
            }

            Assert.Equal(1, directory[(int)storage.Child].Color);
            var inOrder = new List<string>();
            BlackHeight(directory, storage.Child, inOrder);
            Assert.Equal(inOrder.OrderBy(name => name.Length).ThenBy(name => name.ToUpperInvariant(), StringComparer.Ordinal), inOrder);
        }
    }

    /// <summary>Checks no red node has a red child and returns the black height; collects names in order.</summary>
    private static int BlackHeight(List<RawEntry> directory, uint id, List<string> inOrder)
    {
        if (id == uint.MaxValue)
        {
            return 1;
        }

        var entry = directory[(int)id];
        var left = BlackHeight(directory, entry.Left, inOrder);
        inOrder.Add(entry.Name);
        var right = BlackHeight(directory, entry.Right, inOrder);
        Assert.Equal(left, right);
        if (entry.Color == 0)
        {
            Assert.True(entry.Left == uint.MaxValue || directory[(int)entry.Left].Color == 1);
            Assert.True(entry.Right == uint.MaxValue || directory[(int)entry.Right].Color == 1);
        }

        return left + (entry.Color == 1 ? 1 : 0);
    }

    private sealed record RawEntry(string Name, byte Type, byte Color, uint Left, uint Right, uint Child);

    /// <summary>The directory of a version 3 file read by hand (the FAT sectors named in the header only).</summary>
    private static List<RawEntry> RawDirectory(byte[] file)
    {
        var fatCount = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(44));
        var fat = new List<uint>();
        for (var index = 0; index < Math.Min(fatCount, 109); index++)
        {
            var sector = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(76 + index * 4));
            for (var entry = 0; entry < 128; entry++)
            {
                fat.Add(BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan((int)(sector + 1) * 512 + entry * 4)));
            }
        }

        var result = new List<RawEntry>();
        for (var sector = BinaryPrimitives.ReadUInt32LittleEndian(file.AsSpan(48)); sector != 0xFFFFFFFE; sector = fat[(int)sector])
        {
            for (var index = 0; index < 4; index++)
            {
                var span = file.AsSpan((int)(sector + 1) * 512 + index * 128, 128);
                var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[64..]);
                result.Add(new RawEntry(Encoding.Unicode.GetString(span[..Math.Max(0, nameLength - 2)]), span[66], span[67],
                    BinaryPrimitives.ReadUInt32LittleEndian(span[68..]), BinaryPrimitives.ReadUInt32LittleEndian(span[72..]),
                    BinaryPrimitives.ReadUInt32LittleEndian(span[76..])));
            }
        }

        return result;
    }

    [Fact]
    public void A_version_4_file_built_by_hand_is_read()
    {
        var file = CompoundFile.Open(new MemoryStream(HandBuiltCompoundFile.Build()));

        Assert.Equal(4, file.MajorVersion);
        Assert.Equal(["Big", "Small", "Store", "Store/Inner"], file.Entries.Select(entry => entry.Path));
        Assert.Equal(HandBuiltCompoundFile.Big, file.ReadStream("Big"));
        Assert.Equal(HandBuiltCompoundFile.Small, file.ReadStream("Small"));
        Assert.Equal(HandBuiltCompoundFile.Inner, file.ReadStream("Store/Inner"));
    }

    [Fact]
    public void Signatures_are_recognized_and_the_position_is_kept()
    {
        var stream = new MemoryStream(HandBuiltCompoundFile.Build()) { Position = 17 };
        Assert.True(CompoundFile.HasSignature(stream));
        Assert.Equal(17, stream.Position);
        Assert.False(CompoundFile.HasSignature(new MemoryStream(Encoding.ASCII.GetBytes("PK\u0003\u0004 not a compound file"))));
        Assert.False(CompoundFile.HasSignature(new MemoryStream()));
    }

    public static TheoryData<string> DamagedFiles => ["truncated", "cut header", "FAT cycle", "directory cycle", "tree cycle", "sector outside",
        "huge size", "huge FAT count", "unknown version", "unused entry in tree"];

    [Theory]
    [MemberData(nameof(DamagedFiles))]
    public void Damaged_files_are_refused_quickly(string damage)
    {
        var bytes = damage switch
        {
            "truncated" => Truncated(),
            "cut header" => HandBuiltCompoundFile.Build()[..300],
            "FAT cycle" => HandBuiltCompoundFile.Build(fat => fat[5] = 4),
            "directory cycle" => HandBuiltCompoundFile.Build(fat => fat[1] = 1),
            "tree cycle" => HandBuiltCompoundFile.Build(directory: entries => BinaryPrimitives.WriteUInt32LittleEndian(entries.AsSpan(128 + 68), 2)),
            "sector outside" => HandBuiltCompoundFile.Build(fat => fat[5] = 4000),
            "huge size" => HandBuiltCompoundFile.Build(directory: entries => BinaryPrimitives.WriteUInt64LittleEndian(entries.AsSpan(128 + 120), long.MaxValue / 2)),
            "huge FAT count" => With(HandBuiltCompoundFile.Build(), bytes => BinaryPrimitives.WriteUInt32LittleEndian(bytes.AsSpan(44), 0x7FFFFFFF)),
            "unknown version" => With(HandBuiltCompoundFile.Build(), bytes => BinaryPrimitives.WriteUInt16LittleEndian(bytes.AsSpan(26), 5)),
            _ => HandBuiltCompoundFile.Build(directory: entries => entries[3 * 128 + 66] = 0),
        };

        var watch = Stopwatch.StartNew();
        Assert.Throws<InvalidDataException>(() =>
        {
            var file = CompoundFile.Open(new MemoryStream(bytes));
            foreach (var entry in file.Entries.Where(entry => !entry.IsStorage))
            {
                file.ReadStream(entry.Path);
            }
        });
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(2), $"{damage} took {watch.Elapsed}");
    }

    private static byte[] Truncated()
    {
        var writer = new CompoundFileWriter();
        writer.AddStream("Big", Bytes(100_000, 1));
        var bytes = Save(writer);
        return bytes[..(bytes.Length / 2)];
    }

    private static byte[] With(byte[] bytes, Action<byte[]> change)
    {
        change(bytes);
        return bytes;
    }

    [Fact]
    public void The_writer_refuses_bad_names_and_duplicates()
    {
        var writer = new CompoundFileWriter();
        writer.AddStream("A/B", new byte[] { 1 });
        Assert.Throws<ArgumentException>(() => writer.AddStream("a/b", new byte[] { 2 }));
        Assert.Throws<ArgumentException>(() => writer.AddStream("A/B/C", new byte[] { 2 }));
        Assert.Throws<ArgumentException>(() => writer.AddStream(new string('x', 32), new byte[] { 2 }));
        Assert.Throws<ArgumentException>(() => writer.AddStream("Bad:Name", new byte[] { 2 }));
        Assert.Throws<ArgumentException>(() => writer.AddStream("A//B", new byte[] { 2 }));
        Assert.Throws<ArgumentOutOfRangeException>(() => new CompoundFileWriter(2));
    }
}
