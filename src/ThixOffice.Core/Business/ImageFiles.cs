using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// Reading a picture file for a document (Einfügen › Bilder, a file dropped on the page, Bild ändern): only the picture types
/// the apps show (PNG, JPEG, GIF, BMP), at most <see cref="MaxBytes"/>; the app checks the content itself.
/// </summary>
public static class ImageFiles
{
    public const long MaxBytes = 50L * 1024 * 1024;

    /// <summary>The extensions of pictures the apps read (lower case, with the dot).</summary>
    public static IReadOnlyList<string> Extensions { get; } = [".png", ".jpg", ".jpeg", ".jpe", ".gif", ".bmp"];

    /// <summary>True when <paramref name="path"/> names a picture by its extension.</summary>
    public static bool IsPicture(string path) => Extensions.Contains(Path.GetExtension(path).ToLowerInvariant());

    /// <summary>The bytes of a picture file; a <see cref="BusinessException"/> for another type, a missing or too large file.</summary>
    public static byte[] Read(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !IsPicture(path))
        {
            throw new BusinessException("Dieses Bildformat wird nicht unterstützt. Möglich sind PNG, JPEG, GIF und BMP.");
        }

        try
        {
            var info = new FileInfo(path);
            if (!info.Exists)
            {
                throw new BusinessException($"Das Bild „{Path.GetFileName(path)}“ wurde nicht gefunden.");
            }

            if (info.Length > MaxBytes)
            {
                throw new BusinessException($"Das Bild „{info.Name}“ ist größer als {MaxBytes / 1024 / 1024} MB.");
            }

            return File.ReadAllBytes(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"A picture could not be read: {exception.Message}");
            throw new BusinessException($"Das Bild „{Path.GetFileName(path)}“ konnte nicht gelesen werden: {exception.Message}");
        }
    }
}
