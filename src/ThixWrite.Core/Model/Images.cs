using System.Buffers.Binary;
using System.Security.Cryptography;

namespace ThixWrite.Core.Model;

/// <summary>The picture formats ThixWrite shows and writes.</summary>
public enum ImageFormat
{
    Png,
    Jpeg,
    Gif,
    Bmp,
}

/// <summary>
/// The bytes of a picture as stored in the file (PNG, JPEG, GIF, BMP), its format and pixel size (read from the header – the core
/// decodes nothing; drawing is Skia's in the renderer) and its resolution when the file names one. Immutable; two pictures with
/// the same bytes are equal (<see cref="Key"/> is their SHA-256), so a document stores each once.
/// </summary>
public sealed class ImageData : IEquatable<ImageData>
{
    private readonly byte[] _bytes;

    private ImageData(byte[] bytes, ImageFormat format, int pixelWidth, int pixelHeight, double dpiX, double dpiY)
    {
        _bytes = bytes;
        Format = format;
        PixelWidth = pixelWidth;
        PixelHeight = pixelHeight;
        DpiX = dpiX;
        DpiY = dpiY;
        Key = Convert.ToHexString(SHA256.HashData(bytes));
    }

    public ImageFormat Format { get; }

    public int PixelWidth { get; }

    public int PixelHeight { get; }

    /// <summary>The resolution the file names (96 when it names none).</summary>
    public double DpiX { get; }

    public double DpiY { get; }

    /// <summary>The SHA-256 of the bytes (hex): identity, and the key of decoded pictures in the renderer.</summary>
    public string Key { get; }

    /// <summary>The bytes (the same array every time: do not change it).</summary>
    public byte[] Bytes => _bytes;

    public int Length => _bytes.Length;

    public string ContentType => Format switch
    {
        ImageFormat.Jpeg => "image/jpeg",
        ImageFormat.Gif => "image/gif",
        ImageFormat.Bmp => "image/bmp",
        _ => "image/png",
    };

    public string Extension => Format switch
    {
        ImageFormat.Jpeg => ".jpeg",
        ImageFormat.Gif => ".gif",
        ImageFormat.Bmp => ".bmp",
        _ => ".png",
    };

    /// <summary>The size the picture has in a document at first: its pixels at its resolution, in points.</summary>
    public (double Width, double Height) NaturalSize => (PixelWidth * 72 / DpiX, PixelHeight * 72 / DpiY);

    /// <summary>The picture of <paramref name="bytes"/>, or null when they are no PNG, JPEG, GIF or BMP with a readable size.</summary>
    public static ImageData? FromBytes(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);
        var copy = bytes.ToArray();
        var span = copy.AsSpan();
        var info = ReadPng(span) ?? ReadGif(span) ?? ReadBmp(span) ?? ReadJpeg(span);
        if (info is not { } found || found.Width <= 0 || found.Height <= 0 || found.Width > 100_000 || found.Height > 100_000)
        {
            return null;
        }

        var dpiX = found.DpiX is > 1 and < 10_000 ? found.DpiX : 96;
        var dpiY = found.DpiY is > 1 and < 10_000 ? found.DpiY : 96;
        return new ImageData(copy, found.Format, found.Width, found.Height, dpiX, dpiY);
    }

    public bool Equals(ImageData? other) => other is not null && other.Key == Key;

    public override bool Equals(object? obj) => obj is ImageData other && Equals(other);

    public override int GetHashCode() => Key.GetHashCode(StringComparison.Ordinal);

    public override string ToString() => $"{Format} {PixelWidth}×{PixelHeight}";

    private readonly record struct HeaderInfo(ImageFormat Format, int Width, int Height, double DpiX = 0, double DpiY = 0);

    private static HeaderInfo? ReadPng(ReadOnlySpan<byte> data)
    {
        ReadOnlySpan<byte> signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        if (data.Length < 24 || !data[..8].SequenceEqual(signature))
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32BigEndian(data[16..]);
        var height = BinaryPrimitives.ReadInt32BigEndian(data[20..]);
        double dpiX = 0, dpiY = 0;
        var position = 8;
        while (position + 12 <= data.Length)
        {
            var length = BinaryPrimitives.ReadInt32BigEndian(data[position..]);
            var type = data.Slice(position + 4, 4);
            if (length < 0 || position + 12 + length > data.Length)
            {
                break;
            }

            if (type.SequenceEqual("pHYs"u8) && length >= 9 && data[position + 16] == 1)
            {
                dpiX = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 8)..]) * 0.0254;
                dpiY = BinaryPrimitives.ReadUInt32BigEndian(data[(position + 12)..]) * 0.0254;
                break;
            }

            if (type.SequenceEqual("IDAT"u8))
            {
                break;
            }

            position += 12 + length;
        }

        return new HeaderInfo(ImageFormat.Png, width, height, dpiX, dpiY);
    }

    private static HeaderInfo? ReadGif(ReadOnlySpan<byte> data) =>
        data.Length >= 10 && (data[..6].SequenceEqual("GIF87a"u8) || data[..6].SequenceEqual("GIF89a"u8))
            ? new HeaderInfo(ImageFormat.Gif, BinaryPrimitives.ReadUInt16LittleEndian(data[6..]), BinaryPrimitives.ReadUInt16LittleEndian(data[8..]))
            : null;

    private static HeaderInfo? ReadBmp(ReadOnlySpan<byte> data)
    {
        if (data.Length < 26 || data[0] != (byte)'B' || data[1] != (byte)'M')
        {
            return null;
        }

        var width = BinaryPrimitives.ReadInt32LittleEndian(data[18..]);
        var height = Math.Abs(BinaryPrimitives.ReadInt32LittleEndian(data[22..]));
        double dpiX = 0, dpiY = 0;
        if (data.Length >= 46)
        {
            dpiX = BinaryPrimitives.ReadInt32LittleEndian(data[38..]) * 0.0254;
            dpiY = BinaryPrimitives.ReadInt32LittleEndian(data[42..]) * 0.0254;
        }

        return new HeaderInfo(ImageFormat.Bmp, width, height, dpiX, dpiY);
    }

    /// <summary>JPEG: the size from the first start-of-frame marker, the resolution from a JFIF header.</summary>
    private static HeaderInfo? ReadJpeg(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data[0] != 0xFF || data[1] != 0xD8)
        {
            return null;
        }

        double dpiX = 0, dpiY = 0;
        var position = 2;
        while (position + 4 <= data.Length)
        {
            if (data[position] != 0xFF)
            {
                position++;
                continue;
            }

            var marker = data[position + 1];
            if (marker is 0xD8 or 0x01 or (>= 0xD0 and <= 0xD7) or 0xFF)
            {
                position += marker == 0xFF ? 1 : 2;
                continue;
            }

            var length = BinaryPrimitives.ReadUInt16BigEndian(data[(position + 2)..]);
            if (length < 2 || position + 2 + length > data.Length)
            {
                return null;
            }

            var segment = data.Slice(position + 4, length - 2);
            if (marker == 0xE0 && segment.Length >= 12 && segment[..5].SequenceEqual("JFIF\0"u8))
            {
                var units = segment[7];
                var x = BinaryPrimitives.ReadUInt16BigEndian(segment[8..]);
                var y = BinaryPrimitives.ReadUInt16BigEndian(segment[10..]);
                (dpiX, dpiY) = units switch { 1 => (x, y), 2 => (x * 2.54, y * 2.54), _ => (0.0, 0.0) };
            }

            var isFrame = marker is >= 0xC0 and <= 0xCF and not 0xC4 and not 0xC8 and not 0xCC;
            if (isFrame && segment.Length >= 5)
            {
                var height = BinaryPrimitives.ReadUInt16BigEndian(segment[1..]);
                var width = BinaryPrimitives.ReadUInt16BigEndian(segment[3..]);
                return new HeaderInfo(ImageFormat.Jpeg, width, height, dpiX, dpiY);
            }

            position += 2 + length;
        }

        return null;
    }
}

/// <summary>
/// An inline picture: one character in the text (<see cref="SpecialCharacters.Object"/>), shown at <see cref="Width"/> ×
/// <see cref="Height"/> points on the line like a big letter. <see cref="Description"/> is the alternative text.
/// <see cref="SourceXml"/>/<see cref="SourcePart"/>: the w:drawing as read and its media relationship – written back unchanged
/// while the picture is (effects, cropping); <see cref="IsFloating"/>: Word positioned it freely (wp:anchor), ThixWrite shows
/// it in the text flow.
/// </summary>
public sealed record ImageInline(
    ImageData Image,
    double Width,
    double Height,
    CharacterProperties Properties,
    string? Description = null,
    Hyperlink? Link = null,
    string? SourceXml = null,
    string? SourcePart = null,
    bool IsFloating = false) : Inline(Properties, Link)
{
    public override int Length => 1;

    public override string Characters => "￼";

    /// <summary>Where a floating picture (wp:anchor) sits and how text flows around it; null for a picture in the line.</summary>
    public DrawingAnchor? Anchor { get; init; }

    /// <summary>The same picture at another size (no longer the drawing as read).</summary>
    public ImageInline WithSize(double width, double height) => this with { Width = Math.Max(1, width), Height = Math.Max(1, height), SourceXml = null };

    /// <summary>The same picture, size (to a hundredth of a point), formatting, text and link – where it came from does not count.</summary>
    public bool Equals(ImageInline? other) =>
        other is not null
        && Image.Equals(other.Image)
        && Math.Abs(Width - other.Width) < 0.01
        && Math.Abs(Height - other.Height) < 0.01
        && Properties == other.Properties
        && Description == other.Description
        && Link == other.Link
        && IsFloating == other.IsFloating
        && Anchor == other.Anchor;

    public override int GetHashCode() => HashCode.Combine(Image, Math.Round(Width), Math.Round(Height), Description, IsFloating);
}
