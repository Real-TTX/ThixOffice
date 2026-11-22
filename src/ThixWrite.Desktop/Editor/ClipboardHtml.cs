using Avalonia.Input;
using Avalonia.Input.Platform;

namespace ThixWrite.Editor;

/// <summary>The HTML another program put on the clipboard (Windows "HTML Format", macOS public.html) – ThixWrite's editor and ThixMail's composer paste it.</summary>
internal static class ClipboardHtml
{
    /// <summary>The HTML on the clipboard (with Windows' header; <c>HtmlImport.FromClipboardFormat</c> reads it), or null.</summary>
    public static async Task<string?> TryGetAsync(IClipboard clipboard)
    {
        var format = DataFormat.CreateBytesPlatformFormat(OperatingSystem.IsMacOS() ? "public.html" : "HTML Format");
        try
        {
            return await clipboard.TryGetValueAsync(format) is { Length: > 0 } bytes ? System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0', ' ') : null;
        }
        catch (Exception exception) when (exception is InvalidOperationException or NotSupportedException or System.Runtime.InteropServices.COMException)
        {
            return null;
        }
    }
}
