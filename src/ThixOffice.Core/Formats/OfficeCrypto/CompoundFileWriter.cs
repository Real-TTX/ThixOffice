using System.Buffers.Binary;
using System.Text;
using static ThixOffice.Core.Formats.OfficeCrypto.CompoundFile;

namespace ThixOffice.Core.Formats.OfficeCrypto;

/// <summary>
/// Writes a compound file (MS-CFB): storages and streams by path ("\u0006DataSpaces/Version"), streams smaller than 4096 bytes
/// in the mini stream as the format requires, every storage's children as a red-black tree in the format's order, and the FAT
/// with DIFAT sectors for files of any size. Version 3 (512-byte sectors) by default; version 4 (4096-byte sectors) on request.
/// The streams' bytes are written straight from the memory given, in one pass.
/// </summary>
public sealed class CompoundFileWriter
{
    private readonly Node _root = new("Root Entry", isStorage: true, ReadOnlyMemory<byte>.Empty);
    private readonly int _majorVersion;
    private readonly int _sectorSize;

    public CompoundFileWriter(int majorVersion = 3)
    {
        if (majorVersion is not (3 or 4))
        {
            throw new ArgumentOutOfRangeException(nameof(majorVersion), "Only versions 3 and 4 exist.");
        }

        _majorVersion = majorVersion;
        _sectorSize = majorVersion == 3 ? 512 : 4096;
    }

    /// <summary>Adds an (empty) storage; storages named in stream paths are added on their own.</summary>
    public void AddStorage(string path) => Add(path, isStorage: true, ReadOnlyMemory<byte>.Empty);

    /// <summary>Adds a stream with <paramref name="data"/> (kept by reference until <see cref="Save"/>).</summary>
    public void AddStream(string path, ReadOnlyMemory<byte> data) => Add(path, isStorage: false, data);

    private void Add(string path, bool isStorage, ReadOnlyMemory<byte> data)
    {
        ArgumentException.ThrowIfNullOrEmpty(path);
        var names = path.Split('/');
        var parent = _root;
        for (var index = 0; index < names.Length; index++)
        {
            var name = names[index];
            if (name.Length is 0 or > 31 || name.IndexOfAny(['\\', ':', '!']) >= 0)
            {
                throw new ArgumentException($"\"{name}\" is no valid compound file name.", nameof(path));
            }

            var existing = parent.Children.FirstOrDefault(child => CompoundNames.Comparer.Compare(child.Name, name) == 0);
            var last = index == names.Length - 1;
            if (last)
            {
                if (existing is not null)
                {
                    if (isStorage && existing.IsStorage)
                    {
                        return;
                    }

                    throw new ArgumentException($"\"{path}\" exists already.", nameof(path));
                }

                parent.Children.Add(new Node(name, isStorage, data));
                return;
            }

            if (existing is { IsStorage: false })
            {
                throw new ArgumentException($"\"{name}\" is a stream, not a storage.", nameof(path));
            }

            if (existing is null)
            {
                existing = new Node(name, isStorage: true, ReadOnlyMemory<byte>.Empty);
                parent.Children.Add(existing);
            }

            parent = existing;
        }
    }

    /// <summary>Writes the compound file to <paramref name="output"/> (from its current position, sequentially).</summary>
    public void Save(Stream output)
    {
        ArgumentNullException.ThrowIfNull(output);
        var nodes = new List<Node>();
        Number(_root, nodes);

        // The mini stream: every stream below the cutoff, in 64-byte mini sectors.
        var miniFat = new List<uint>();
        var smallStreams = nodes.Where(node => !node.IsStorage && node.Data.Length is > 0 and < MiniStreamCutoff).ToList();
        foreach (var node in smallStreams)
        {
            var count = (node.Data.Length + MiniSectorSize - 1) / MiniSectorSize;
            node.Start = (uint)miniFat.Count;
            for (var index = 0; index < count; index++)
            {
                miniFat.Add(index == count - 1 ? EndOfChain : (uint)(miniFat.Count + 1));
            }
        }

        var miniStreamLength = (long)miniFat.Count * MiniSectorSize;
        var perSector = _sectorSize / 4;
        long Sectors(long bytes) => (bytes + _sectorSize - 1) / _sectorSize;

        // The order of the sectors: mini stream, large streams, mini FAT, directory, FAT, DIFAT.
        var chains = new List<(long Start, long Count)>();
        long next = 0;
        long Place(long count)
        {
            var start = next;
            next += count;
            chains.Add((start, count));
            return start;
        }

        var miniStreamStart = Place(Sectors(miniStreamLength));
        var largeStreams = nodes.Where(node => !node.IsStorage && node.Data.Length >= MiniStreamCutoff).ToList();
        foreach (var node in largeStreams)
        {
            node.Start = (uint)Place(Sectors(node.Data.Length));
        }

        var miniFatSectors = Sectors(miniFat.Count * 4L);
        var miniFatStart = Place(miniFatSectors);
        var directorySectors = Sectors((long)nodes.Count * DirectoryEntrySize);
        var directoryStart = Place(directorySectors);

        var dataSectors = next;
        long fatSectors = (dataSectors + perSector - 1) / perSector;
        long difatSectors;
        while (true)
        {
            difatSectors = fatSectors <= HeaderDifatCount ? 0 : (fatSectors - HeaderDifatCount + perSector - 2) / (perSector - 1);
            var needed = (dataSectors + fatSectors + difatSectors + perSector - 1) / perSector;
            if (needed <= fatSectors)
            {
                break;
            }

            fatSectors = needed;
        }

        if (dataSectors + fatSectors + difatSectors >= MaxRegularSector)
        {
            throw new InvalidOperationException("The compound file would be too large.");
        }

        var fatStart = dataSectors;
        var difatStart = fatStart + fatSectors;
        var fat = new uint[fatSectors * perSector];
        Array.Fill(fat, FreeSector);
        foreach (var (start, count) in chains)
        {
            for (var index = 0L; index < count; index++)
            {
                fat[start + index] = index == count - 1 ? EndOfChain : (uint)(start + index + 1);
            }
        }

        for (var index = 0L; index < fatSectors; index++)
        {
            fat[fatStart + index] = FatSector;
        }

        for (var index = 0L; index < difatSectors; index++)
        {
            fat[difatStart + index] = DifatSector;
        }

        _root.Start = miniStreamLength == 0 ? EndOfChain : (uint)miniStreamStart;
        _root.Size = miniStreamLength;

        var writer = new SectorWriter(output, _sectorSize);
        writer.WriteHeader(Header(fatSectors, directorySectors, (uint)directoryStart, miniFat.Count == 0 ? EndOfChain : (uint)miniFatStart,
            (uint)miniFatSectors, difatSectors == 0 ? EndOfChain : (uint)difatStart, (uint)difatSectors, fatStart));

        foreach (var node in smallStreams)
        {
            writer.Write(node.Data.Span);
            writer.PadTo(MiniSectorSize);
        }

        writer.PadTo(_sectorSize);
        foreach (var node in largeStreams)
        {
            writer.Write(node.Data.Span);
            writer.PadTo(_sectorSize);
        }

        var buffer = new byte[4];
        foreach (var entry in miniFat)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, entry);
            writer.Write(buffer);
        }

        writer.PadTo(_sectorSize, fill: 0xFF);
        foreach (var node in nodes)
        {
            writer.Write(DirectoryEntry(node));
        }

        var unused = UnusedEntry();
        for (var index = nodes.Count; index < directorySectors * _sectorSize / DirectoryEntrySize; index++)
        {
            writer.Write(unused);
        }

        foreach (var entry in fat)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(buffer, entry);
            writer.Write(buffer);
        }

        for (var index = 0L; index < difatSectors; index++)
        {
            for (var slot = 0; slot < perSector - 1; slot++)
            {
                var fatIndex = HeaderDifatCount + index * (perSector - 1) + slot;
                BinaryPrimitives.WriteUInt32LittleEndian(buffer, fatIndex < fatSectors ? (uint)(fatStart + fatIndex) : FreeSector);
                writer.Write(buffer);
            }

            BinaryPrimitives.WriteUInt32LittleEndian(buffer, index == difatSectors - 1 ? EndOfChain : (uint)(difatStart + index + 1));
            writer.Write(buffer);
        }

        writer.Flush();
    }

    private byte[] Header(long fatSectors, long directorySectors, uint directoryStart, uint miniFatStart, uint miniFatSectors, uint difatStart,
        uint difatSectors, long fatStart)
    {
        var header = new byte[512];
        var span = header.AsSpan();
        new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 }.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[24..], 0x3E);
        BinaryPrimitives.WriteUInt16LittleEndian(span[26..], (ushort)_majorVersion);
        BinaryPrimitives.WriteUInt16LittleEndian(span[28..], 0xFFFE);
        BinaryPrimitives.WriteUInt16LittleEndian(span[30..], (ushort)(_majorVersion == 3 ? 9 : 12));
        BinaryPrimitives.WriteUInt16LittleEndian(span[32..], 6);
        BinaryPrimitives.WriteUInt32LittleEndian(span[40..], _majorVersion == 3 ? 0 : (uint)directorySectors);
        BinaryPrimitives.WriteUInt32LittleEndian(span[44..], (uint)fatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(span[48..], directoryStart);
        BinaryPrimitives.WriteUInt32LittleEndian(span[56..], MiniStreamCutoff);
        BinaryPrimitives.WriteUInt32LittleEndian(span[60..], miniFatStart);
        BinaryPrimitives.WriteUInt32LittleEndian(span[64..], miniFatSectors);
        BinaryPrimitives.WriteUInt32LittleEndian(span[68..], difatStart);
        BinaryPrimitives.WriteUInt32LittleEndian(span[72..], difatSectors);
        for (var index = 0; index < HeaderDifatCount; index++)
        {
            BinaryPrimitives.WriteUInt32LittleEndian(span[(76 + index * 4)..], index < fatSectors ? (uint)(fatStart + index) : FreeSector);
        }

        return header;
    }

    /// <summary>Gives every node its directory id (the root 0) and links each storage's children as a red-black tree.</summary>
    private static void Number(Node storage, List<Node> nodes)
    {
        if (nodes.Count == 0)
        {
            storage.Id = 0;
            nodes.Add(storage);
        }

        var sorted = storage.Children.OrderBy(child => child.Name, CompoundNames.Comparer).ToList();
        foreach (var child in sorted)
        {
            child.Id = (uint)nodes.Count;
            nodes.Add(child);
        }

        storage.Child = sorted.Count == 0 ? NoStream : Build(sorted, 0, sorted.Count - 1, 0, Depth(sorted.Count));
        foreach (var child in sorted.Where(child => child.IsStorage))
        {
            Number(child, nodes);
        }
    }

    /// <summary>The deepest level (root 0) of a tree built by halving <paramref name="count"/> sorted nodes.</summary>
    private static int Depth(int count)
    {
        var depth = -1;
        for (var size = count; size > 0; size /= 2)
        {
            depth++;
        }

        return depth;
    }

    /// <summary>
    /// A balanced tree of the sorted nodes: every level black except the deepest, which is red when it is not the root. All paths
    /// then pass the same number of black nodes, and no red node has a red child – a valid red-black tree.
    /// </summary>
    private static uint Build(List<Node> sorted, int low, int high, int depth, int deepest)
    {
        if (low > high)
        {
            return NoStream;
        }

        var middle = (low + high + 1) / 2;
        var node = sorted[middle];
        node.IsRed = depth == deepest && depth > 0;
        node.Left = Build(sorted, low, middle - 1, depth + 1, deepest);
        node.Right = Build(sorted, middle + 1, high, depth + 1, deepest);
        return node.Id;
    }

    private byte[] DirectoryEntry(Node node)
    {
        var entry = new byte[DirectoryEntrySize];
        var span = entry.AsSpan();
        var nameBytes = Encoding.Unicode.GetBytes(node.Name);
        nameBytes.CopyTo(span);
        BinaryPrimitives.WriteUInt16LittleEndian(span[64..], (ushort)(nameBytes.Length + 2));
        span[66] = node == _root ? (byte)5 : node.IsStorage ? (byte)1 : (byte)2;
        span[67] = node.IsRed ? (byte)0 : (byte)1;
        BinaryPrimitives.WriteUInt32LittleEndian(span[68..], node.Left);
        BinaryPrimitives.WriteUInt32LittleEndian(span[72..], node.Right);
        BinaryPrimitives.WriteUInt32LittleEndian(span[76..], node.IsStorage ? node.Child : NoStream);
        var size = node == _root ? node.Size : node.IsStorage ? 0 : node.Data.Length;
        var start = node == _root ? node.Start : node.IsStorage || size == 0 ? EndOfChain : node.Start;
        if (node.IsStorage && node != _root)
        {
            start = 0;
        }

        BinaryPrimitives.WriteUInt32LittleEndian(span[116..], start);
        BinaryPrimitives.WriteUInt64LittleEndian(span[120..], (ulong)size);
        return entry;
    }

    private static byte[] UnusedEntry()
    {
        var entry = new byte[DirectoryEntrySize];
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(68), NoStream);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(72), NoStream);
        BinaryPrimitives.WriteUInt32LittleEndian(entry.AsSpan(76), NoStream);
        return entry;
    }

    private sealed class Node(string name, bool isStorage, ReadOnlyMemory<byte> data)
    {
        public string Name { get; } = name;

        public bool IsStorage { get; } = isStorage;

        public ReadOnlyMemory<byte> Data { get; } = data;

        public List<Node> Children { get; } = [];

        public uint Id { get; set; }

        public uint Left { get; set; } = NoStream;

        public uint Right { get; set; } = NoStream;

        public uint Child { get; set; } = NoStream;

        public bool IsRed { get; set; }

        public uint Start { get; set; } = EndOfChain;

        public long Size { get; set; }
    }

    /// <summary>Writes sequentially and pads to sector boundaries (counting from the first sector after the header).</summary>
    private sealed class SectorWriter(Stream output, int sectorSize)
    {
        private readonly byte[] _padding = new byte[4096];
        private readonly byte[] _ones = Enumerable.Repeat((byte)0xFF, 4096).ToArray();
        private long _written;

        public void WriteHeader(byte[] header)
        {
            output.Write(header);
            output.Write(_padding.AsSpan(0, sectorSize - header.Length));
        }

        public void Write(ReadOnlySpan<byte> bytes)
        {
            output.Write(bytes);
            _written += bytes.Length;
        }

        public void PadTo(int boundary, byte fill = 0)
        {
            var missing = (int)((boundary - _written % boundary) % boundary);
            var source = fill == 0 ? _padding : _ones;
            while (missing > 0)
            {
                var count = Math.Min(missing, source.Length);
                Write(source.AsSpan(0, count));
                missing -= count;
            }
        }

        public void Flush() => output.Flush();
    }
}
