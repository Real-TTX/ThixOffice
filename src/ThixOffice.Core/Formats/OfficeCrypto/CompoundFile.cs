using System.Buffers.Binary;
using System.Collections;
using System.Text;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>A storage or stream of a compound file: its path below the root ("\u0006DataSpaces/Version"), name and size.</summary>
public sealed record CompoundFileEntry(string Path, string Name, bool IsStorage, long Size);

/// <summary>
/// A compound file being read (MS-CFB, versions 3 and 4): the header, the DIFAT, the FAT, the mini FAT and mini stream, and the
/// directory tree with nested storages. The stream is the whole file and must stay open and seekable while this is used. Every
/// inconsistency – sectors outside the file, cycles in a chain, sizes larger than the file – is an <see cref="InvalidDataException"/>;
/// nothing is allocated beyond what the file can hold.
/// </summary>
public sealed class CompoundFile
{
    internal const uint MaxRegularSector = 0xFFFFFFFA;
    internal const uint DifatSector = 0xFFFFFFFC;
    internal const uint FatSector = 0xFFFFFFFD;
    internal const uint EndOfChain = 0xFFFFFFFE;
    internal const uint FreeSector = 0xFFFFFFFF;
    internal const uint NoStream = 0xFFFFFFFF;
    internal const int MiniStreamCutoff = 4096;
    internal const int MiniSectorSize = 64;
    internal const int DirectoryEntrySize = 128;
    internal const int HeaderDifatCount = 109;

    private static readonly byte[] Signature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    private readonly Stream _stream;
    private readonly int _sectorSize;
    private readonly long _sectorCount;
    private readonly uint[] _fat;
    private readonly uint[] _miniFat;
    private readonly Dictionary<string, DirectoryEntry> _byPath = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<CompoundFileEntry> _entries = [];
    private readonly DirectoryEntry _root;
    private byte[]? _miniStream;

    private CompoundFile(Stream stream)
    {
        _stream = stream;
        var header = new byte[512];
        stream.Position = 0;
        if (stream.Length < header.Length || stream.ReadAtLeast(header, header.Length, throwOnEndOfStream: false) < header.Length)
        {
            throw Invalid("the header is incomplete");
        }

        if (!header.AsSpan(0, 8).SequenceEqual(Signature))
        {
            throw Invalid("the signature is missing");
        }

        MajorVersion = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(26));
        var sectorShift = BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(30));
        if ((MajorVersion, sectorShift) is not ((3, 9) or (4, 12)) || BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(28)) != 0xFFFE)
        {
            throw Invalid("unknown version or sector size");
        }

        if (BinaryPrimitives.ReadUInt16LittleEndian(header.AsSpan(32)) != 6 || BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(56)) != MiniStreamCutoff)
        {
            throw Invalid("unknown mini sector size or cutoff");
        }

        _sectorSize = 1 << sectorShift;
        _sectorCount = Math.Max(0, (stream.Length - _sectorSize + _sectorSize - 1) / _sectorSize);
        _fat = ReadFat(header);

        var directory = ReadChain(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(48)), _fat, _sectorCount, _sectorSize, ReadSector);
        var entries = ParseDirectory(directory);
        _root = entries.Length > 0 && entries[0].Type == 5 ? entries[0] : throw Invalid("the root entry is missing");

        var miniFatStart = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(60));
        var miniFatCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(64));
        if (miniFatCount > _sectorCount)
        {
            throw Invalid("too many mini FAT sectors");
        }

        _miniFat = miniFatCount == 0 || miniFatStart == EndOfChain ? [] : ToEntries(ReadChain(miniFatStart, _fat, _sectorCount, _sectorSize, ReadSector));
        Walk(entries);
    }

    /// <summary>The major version: 3 (512-byte sectors) or 4 (4096-byte sectors).</summary>
    public int MajorVersion { get; }

    /// <summary>Every storage and stream below the root, storages before their contents.</summary>
    public IReadOnlyList<CompoundFileEntry> Entries => _entries;

    /// <summary>True when the stream starts with the compound file signature (the position is kept).</summary>
    public static bool HasSignature(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        var position = stream.Position;
        try
        {
            stream.Position = 0;
            Span<byte> head = stackalloc byte[8];
            return stream.ReadAtLeast(head, 8, throwOnEndOfStream: false) == 8 && head.SequenceEqual(Signature);
        }
        finally
        {
            stream.Position = position;
        }
    }

    /// <summary>Reads the structure of the compound file in <paramref name="stream"/> (the whole, seekable stream).</summary>
    public static CompoundFile Open(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanSeek || !stream.CanRead)
        {
            throw new ArgumentException("The stream must be readable and seekable.", nameof(stream));
        }

        return new CompoundFile(stream);
    }

    /// <summary>The entry at <paramref name="path"/> (names separated by '/', compared without case like MS-CFB), or null.</summary>
    public CompoundFileEntry? Find(string path) => _byPath.TryGetValue(path, out var entry) ? entry.Public : null;

    /// <summary>True when a storage or stream exists at <paramref name="path"/>.</summary>
    public bool Exists(string path) => _byPath.ContainsKey(path);

    /// <summary>The bytes of the stream at <paramref name="path"/>, or null when there is none.</summary>
    public byte[]? TryReadStream(string path)
    {
        if (!_byPath.TryGetValue(path, out var entry) || entry.Type != 2)
        {
            return null;
        }

        return ReadEntry(entry);
    }

    /// <summary>The bytes of the stream at <paramref name="path"/>; a missing stream is an <see cref="InvalidDataException"/>.</summary>
    public byte[] ReadStream(string path) => TryReadStream(path) ?? throw Invalid($"the stream {path} is missing");

    private byte[] ReadEntry(DirectoryEntry entry)
    {
        if (entry.Size == 0)
        {
            return [];
        }

        if (entry.Size >= MiniStreamCutoff)
        {
            return ReadStreamData(entry.Start, entry.Size, _fat, _sectorSize, _sectorCount, ReadSector);
        }

        _miniStream ??= ReadMiniStream();
        var miniSectors = _miniStream.Length / MiniSectorSize;
        return ReadStreamData(entry.Start, entry.Size, _miniFat, MiniSectorSize, miniSectors, (sector, buffer) =>
            _miniStream.AsSpan((int)sector * MiniSectorSize, MiniSectorSize).CopyTo(buffer));
    }

    private byte[] ReadMiniStream()
    {
        if (_root.Size == 0)
        {
            return [];
        }

        var data = ReadStreamData(_root.Start, _root.Size, _fat, _sectorSize, _sectorCount, ReadSector);
        var whole = data.Length / MiniSectorSize * MiniSectorSize;
        return whole == data.Length ? data : data[..whole];
    }

    private static byte[] ReadStreamData(uint start, long size, uint[] fat, int sectorSize, long sectorCount, Action<uint, byte[]> read)
    {
        var needed = (size + sectorSize - 1) / sectorSize;
        if (size > Array.MaxLength || needed > sectorCount || needed > fat.Length)
        {
            throw Invalid("a stream is larger than the file");
        }

        var result = new byte[size];
        var buffer = new byte[sectorSize];
        var visited = new BitArray(fat.Length);
        var sector = start;
        long offset = 0;
        for (long index = 0; index < needed; index++)
        {
            CheckSector(sector, fat.Length, sectorCount);
            if (visited[(int)sector])
            {
                throw Invalid("a sector chain has a cycle");
            }

            visited[(int)sector] = true;
            read(sector, buffer);
            var count = (int)Math.Min(sectorSize, size - offset);
            buffer.AsSpan(0, count).CopyTo(result.AsSpan((int)offset));
            offset += count;
            sector = fat[sector];
        }

        return result;
    }

    /// <summary>The sectors of a chain whose length the FAT alone decides (directory, mini FAT), concatenated.</summary>
    private static byte[] ReadChain(uint start, uint[] fat, long sectorCount, int sectorSize, Action<uint, byte[]> read)
    {
        using var result = new MemoryStream();
        var buffer = new byte[sectorSize];
        var visited = new BitArray(fat.Length);
        for (var sector = start; sector != EndOfChain; sector = fat[sector])
        {
            CheckSector(sector, fat.Length, sectorCount);
            if (visited[(int)sector])
            {
                throw Invalid("a sector chain has a cycle");
            }

            visited[(int)sector] = true;
            read(sector, buffer);
            result.Write(buffer);
        }

        return result.ToArray();
    }

    private static void CheckSector(uint sector, int fatLength, long sectorCount)
    {
        if (sector > MaxRegularSector || sector >= fatLength || sector >= sectorCount)
        {
            throw Invalid("a sector lies outside the file");
        }
    }

    private void ReadSector(uint sector, byte[] buffer)
    {
        var offset = (sector + 1L) * _sectorSize;
        if (offset >= _stream.Length)
        {
            throw Invalid("a sector lies outside the file");
        }

        _stream.Position = offset;
        var read = _stream.ReadAtLeast(buffer, buffer.Length, throwOnEndOfStream: false);
        buffer.AsSpan(read).Clear();
    }

    private uint[] ReadFat(byte[] header)
    {
        var fatCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(44));
        var difatStart = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(68));
        var difatCount = BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(72));
        if (fatCount > _sectorCount || difatCount > _sectorCount)
        {
            throw Invalid("more FAT sectors than the file has");
        }

        var fatSectors = new List<uint>((int)fatCount);
        for (var index = 0; index < HeaderDifatCount && fatSectors.Count < fatCount; index++)
        {
            fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(header.AsSpan(76 + index * 4)));
        }

        var perSector = _sectorSize / 4;
        var buffer = new byte[_sectorSize];
        var seen = new HashSet<uint>();
        var difat = difatStart;
        for (var count = 0; fatSectors.Count < fatCount; count++)
        {
            if (count >= difatCount || difat > MaxRegularSector || difat >= _sectorCount || !seen.Add(difat))
            {
                throw Invalid("the DIFAT is broken");
            }

            ReadSector(difat, buffer);
            for (var index = 0; index < perSector - 1 && fatSectors.Count < fatCount; index++)
            {
                fatSectors.Add(BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(index * 4)));
            }

            difat = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(_sectorSize - 4));
        }

        var fat = new uint[fatSectors.Count * perSector];
        for (var index = 0; index < fatSectors.Count; index++)
        {
            if (fatSectors[index] > MaxRegularSector || fatSectors[index] >= _sectorCount)
            {
                throw Invalid("a FAT sector lies outside the file");
            }

            ReadSector(fatSectors[index], buffer);
            for (var entry = 0; entry < perSector; entry++)
            {
                fat[index * perSector + entry] = BinaryPrimitives.ReadUInt32LittleEndian(buffer.AsSpan(entry * 4));
            }
        }

        return fat;
    }

    private static uint[] ToEntries(byte[] bytes)
    {
        var result = new uint[bytes.Length / 4];
        for (var index = 0; index < result.Length; index++)
        {
            result[index] = BinaryPrimitives.ReadUInt32LittleEndian(bytes.AsSpan(index * 4));
        }

        return result;
    }

    private DirectoryEntry[] ParseDirectory(byte[] directory)
    {
        var entries = new DirectoryEntry[directory.Length / DirectoryEntrySize];
        for (var index = 0; index < entries.Length; index++)
        {
            var span = directory.AsSpan(index * DirectoryEntrySize, DirectoryEntrySize);
            var nameLength = BinaryPrimitives.ReadUInt16LittleEndian(span[64..]);
            var chars = Math.Clamp(nameLength / 2 - 1, 0, 31);
            var size = (long)BinaryPrimitives.ReadUInt64LittleEndian(span[120..]);
            if (MajorVersion == 3)
            {
                size &= 0xFFFFFFFF;
            }

            entries[index] = new DirectoryEntry
            {
                Name = Encoding.Unicode.GetString(span[..(chars * 2)]),
                Type = span[66],
                Left = BinaryPrimitives.ReadUInt32LittleEndian(span[68..]),
                Right = BinaryPrimitives.ReadUInt32LittleEndian(span[72..]),
                Child = BinaryPrimitives.ReadUInt32LittleEndian(span[76..]),
                Start = BinaryPrimitives.ReadUInt32LittleEndian(span[116..]),
                Size = size < 0 ? throw Invalid("a stream size is negative") : size,
            };
        }

        return entries;
    }

    private void Walk(DirectoryEntry[] entries)
    {
        var visited = new bool[entries.Length];
        visited[0] = true;
        var storages = new Queue<(uint Child, string Prefix)>();
        storages.Enqueue((_root.Child, ""));
        while (storages.Count > 0)
        {
            var (child, prefix) = storages.Dequeue();
            var pending = new Stack<uint>();
            if (child != NoStream)
            {
                pending.Push(child);
            }

            var siblings = new List<DirectoryEntry>();
            while (pending.Count > 0)
            {
                var id = pending.Pop();
                if (id >= entries.Length || visited[id])
                {
                    throw Invalid("the directory tree is broken");
                }

                visited[id] = true;
                var entry = entries[id];
                if (entry.Type is not (1 or 2))
                {
                    throw Invalid("the directory tree names an unused entry");
                }

                siblings.Add(entry);
                foreach (var next in new[] { entry.Right, entry.Left })
                {
                    if (next != NoStream)
                    {
                        pending.Push(next);
                    }
                }
            }

            foreach (var entry in siblings.OrderBy(entry => entry.Name, CompoundNames.Comparer))
            {
                var path = prefix + entry.Name;
                entry.Public = new CompoundFileEntry(path, entry.Name, entry.Type == 1, entry.Type == 1 ? 0 : entry.Size);
                _byPath.TryAdd(path, entry);
                _entries.Add(entry.Public);
                if (entry.Type == 1)
                {
                    storages.Enqueue((entry.Child, path + "/"));
                }
            }
        }
    }

    private static InvalidDataException Invalid(string reason) => new($"Die Datei ist beschädigt (Verbunddatei: {reason}).");

    private sealed class DirectoryEntry
    {
        public required string Name { get; init; }

        public required byte Type { get; init; }

        public required uint Left { get; init; }

        public required uint Right { get; init; }

        public required uint Child { get; init; }

        public required uint Start { get; init; }

        public required long Size { get; init; }

        public CompoundFileEntry? Public { get; set; }
    }
}

/// <summary>The order of names in a storage of a compound file (MS-CFB 2.6.4): shorter first, then by the upper-case letters.</summary>
internal sealed class CompoundNames : IComparer<string>
{
    public static readonly CompoundNames Comparer = new();

    public int Compare(string? x, string? y)
    {
        x ??= "";
        y ??= "";
        if (x.Length != y.Length)
        {
            return x.Length.CompareTo(y.Length);
        }

        for (var index = 0; index < x.Length; index++)
        {
            var difference = char.ToUpperInvariant(x[index]).CompareTo(char.ToUpperInvariant(y[index]));
            if (difference != 0)
            {
                return difference;
            }
        }

        return 0;
    }
}
