using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ThixShow.Core.Model;

/// <summary>The picture formats of a presentation (EMF/WMF/SVG/TIFF are kept and drawn as placeholders).</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    Bmp,
    Tiff,
    Emf,
    Wmf,
    Svg,
    Unknown,
}

/// <summary>
/// The bytes of a picture as stored in the package, its format and pixel size (read from the header – the core decodes
/// nothing; drawing is the renderer's). Immutable; equal by content (<see cref="Key"/> is the SHA-256 of the bytes), so a
/// presentation writes each picture once. Duplicates ThixWrite.Core's ImageData on purpose (more formats here; see the
/// report's consolidation note).
/// </summary>
public sealed class ImageData : IEquatable<ImageData>
{
    private readonly byte[] _bytes;

    private ImageData(byte[] bytes, ImageFormat format, int pixelWidth, int pixelHeight, string? contentType)
    {
        _bytes = bytes;
        Format = format;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        Key = Convert.ToHexString(SHA256.HashData(bytes));
        ContentType = contentType ?? DefaultContentType(format);
    }

    public ImageFormat Format { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    /// <summary>The SHA-256 of the bytes (hex): identity, and the key of decoded pictures in the renderer.</summary>
    public string Key { get; }

    /// <summary>The bytes (the same array every time: do not change it).</summary>
    public byte[] Bytes => _bytes;

    public string ContentType { get; }

    /// <summary>True for formats a renderer can decode (PNG, JPEG, GIF, BMP).</summary>
    public bool IsDrawable => Format is ImageFormat.Png or ImageFormat.Jpeg or ImageFormat.Gif or ImageFormat.Bmp;

    /// <summary>The file extension of the part ("png").</summary>
    public string Extension => Format switch
    {
        ImageFormat.Jpeg => "jpeg",
        ImageFormat.Gif => "gif",
        ImageFormat.Bmp => "bmp",
        ImageFormat.Tiff => "tiff",
        ImageFormat.Emf => "emf",
        ImageFormat.Wmf => "wmf",
        ImageFormat.Svg => "svg",
        ImageFormat.Png => "png",
        _ => ContentType switch
        {
            "image/x-wdp" or "image/vnd.ms-photo" => "wdp",
            _ => "bin",
        },
    };

    /// <summary>Reads the format and size from <paramref name="bytes"/>; <paramref name="contentType"/> is kept for unknown formats.</summary>
    public static ImageData From(byte[] bytes, string? contentType = null)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var (format, width, height) = Sniff(bytes);
        return new ImageData(bytes, format, width, height, format == ImageFormat.Unknown ? contentType ?? "application/octet-stream" : null);
    }

    public bool Equals(ImageData? other) => other is not null && Key == other.Key;

    public override bool Equals(object? obj) => obj is ImageData other && Equals(other);

    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => $"{Format} {PixelWidth}×{PixelHeight} ({_bytes.Length} bytes)";

    private static string DefaultContentType(ImageFormat format) => format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Gif => "image/gif",
        ImageFormat.Bmp => "image/bmp",
        ImageFormat.Tiff => "image/tiff",
        ImageFormat.Emf => "image/x-emf",
        ImageFormat.Wmf => "image/x-wmf",
        ImageFormat.Svg => "image/svg+xml",
        _ => "image/png",
    };

    private static (ImageFormat Format, int Width, int Height) Sniff(byte[] bytes)
    {
        ReadOnlySpan<byte> data = bytes;
        if (data.Length >= 24 && data[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return (ImageFormat.Png, BinaryPrimitives.ReadInt32BigEndian(data[16..]), BinaryPrimitives.ReadInt32BigEndian(data[20..]));
        }

        if (data.Length >= 4 && data[0] == 0xFF && data[1] == 0xD8)
        {
            var (width, height) = JpegSize(data);
            return (ImageFormat.Jpeg, width, height);
        }

        if (data.Length >= 10 && data[0] == 'G' && data[1] == 'I' && data[2] == 'F')
        {
            return (ImageFormat.Gif, BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]));
        }

        if (data.Length >= 26 && data[0] == 'B' && data[1] == 'M')
        {
            return (ImageFormat.Bmp, BinaryPrimitives.ReadInt32LittleEndian(data[18..]), Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..])));
        }

        if (data.Length >= 4 && ((data[0] == 'I' && data[1] == 'I' && data[2] == 42) || (data[0] == 'M' && data[1] == 'M' && data[3] == 42)))
        {
            return (ImageFormat.Tiff, 0, 0);
        }

        if (data.Length >= 44 && BinaryPrimitives.ReadUInt32LittleEndian(data) == 1 && BinaryPrimitives.ReadUInt32LittleEndian(data[40..]) == 0x464D4520)
        {
            return (ImageFormat.Emf, 0, 0);
        }

        if (data.Length >= 4 && (BinaryPrimitives.ReadUInt32LittleEndian(data) == 0x9AC6CDD7 || (data[0] == 1 && data[1] == 0 && data[2] == 9 && data[3] == 0)))
        {
            return (ImageFormat.Wmf, 0, 0);
        }

        var head = System.Text.Encoding.ASCII.GetString(bytes, 0, Math.Min(bytes.Length, 256));
        if (head.Contains("<svg", StringComparison.Ordinal))
        {
            return (ImageFormat.Svg, 0, 0);
        }

        return (ImageFormat.Unknown, 0, 0);
    }

    private static (int Width, int Height) JpegSize(ReadOnlySpan<byte> data)
    {
        var index = 2;
        while (index + 9 < data.Length)
        {
            if (data[index] != 0xFF)
            {
                index++;
                continue;
            }

            var marker = data[index + 1];
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7))
            {
                index += 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(index + 2)..]);
            if (marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC)
            {
                return (BinaryPrimitives.ReadUInt16BigEndian(data[(index + 7)..]), BinaryPrimitives.ReadUInt16BigEndian(data[(index + 5)..]));
            }

            index += 2 + length;
        }

        return (0, 0);
    }
}
