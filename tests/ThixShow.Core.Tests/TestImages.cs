using SkiaSharp;

namespace ThixShow.Core.Tests;

/// <summary>Small pictures made with Skia for the tests.</summary>
internal static class TestImages
{
    /// <summary>A PNG of <paramref name="width"/> × <paramref name="height"/> pixels in one colour.</summary>
    public static byte[] Png(int width, int height, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(width, height);
        bitmap.Erase(color ?? SKColors.SteelBlue);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }
}
