using System.Runtime.InteropServices;
using ThixOffice.Core.Platform.MacOS;

namespace ThixOffice.Core.Infrastructure;

/// <summary>
/// Who is working: the display name of the signed-in user (Windows: GetUserNameEx NameDisplay – "Anna Berger"; macOS:
/// NSFullUserName), else the login name. The default author of comments; Einstellungen › Allgemein can set another. Snapshots pin it.
/// </summary>
public static partial class UserIdentity
{
    private static string? _displayName;
    private static string? _pinned;

    /// <summary>The user's display name (never empty).</summary>
    public static string DisplayName => _pinned ?? (_displayName ??= ReadDisplayName());

    /// <summary>Pins the name (snapshots, tests; null: the real one again).</summary>
    internal static void UseForSnapshots(string? name) => _pinned = name;

    private static string ReadDisplayName()
    {
        try
        {
            if (OperatingSystem.IsWindows() && WindowsDisplayName() is { Length: > 0 } windows)
            {
                return windows;
            }

            if (OperatingSystem.IsMacOS() && MacDisplayName() is { Length: > 0 } mac)
            {
                return mac;
            }
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException or MarshalDirectiveException)
        {
            Log.Warning($"The user's display name is not available: {exception.GetType().Name}");
        }

        return Environment.UserName is { Length: > 0 } login ? login : "Benutzer";
    }

    [System.Runtime.Versioning.SupportedOSPlatform("macos")]
    private static string? MacDisplayName() => ObjectiveC.InPool(() => ObjectiveC.Text(ObjectiveC.NSFullUserName()));

    private static unsafe string? WindowsDisplayName()
    {
        const int NameDisplay = 3;
        var size = 256u;
        var buffer = stackalloc char[256];
        if (!GetUserNameEx(NameDisplay, buffer, ref size) || size == 0)
        {
            return null;
        }

        return new string(buffer, 0, (int)Math.Min(size, 256u)).TrimEnd('\0').Trim();
    }

    [LibraryImport("secur32.dll", EntryPoint = "GetUserNameExW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static unsafe partial bool GetUserNameEx(int format, char* buffer, ref uint size);
}
