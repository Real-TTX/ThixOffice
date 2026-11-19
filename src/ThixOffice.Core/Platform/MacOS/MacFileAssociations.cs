using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Platform.MacOS;

/// <summary>
/// macOS (SETUP-08): the bundle declares its document types in Info.plist (CFBundleDocumentTypes from
/// scripts/macos-document-types/&lt;id&gt;.plist, rank Alternate), so it is offered in "Öffnen mit"; <see cref="MakeDefault"/>
/// makes it the app that opens the types the user chose (Launch Services, per user) – "&lt;App&gt; --configure --file-types=…",
/// which ThixLauncher calls after it copied the bundle.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class MacFileAssociations
{
    private const string CoreServices = "/System/Library/Frameworks/CoreServices.framework/CoreServices";
    private const string CoreFoundation = "/System/Library/Frameworks/CoreFoundation.framework/CoreFoundation";
    private const uint Utf8 = 0x08000100;
    private const uint AllRoles = 0xFFFFFFFF;

    /// <summary>The app <paramref name="bundleId"/> opens these document types (by their UTI, else by the extension).</summary>
    public static void MakeDefault(string bundleId, IEnumerable<DocumentType> types)
    {
        var handler = CFStringCreateWithCString(IntPtr.Zero, bundleId, Utf8);
        var tagClass = CFStringCreateWithCString(IntPtr.Zero, "public.filename-extension", Utf8);
        try
        {
            foreach (var type in types)
            {
                var contentType = ContentTypeOf(type, tagClass);
                if (contentType == IntPtr.Zero)
                {
                    Log.Warning($"Launch Services knows no type for {type.Extension}");
                    continue;
                }

                var status = LSSetDefaultRoleHandlerForContentType(contentType, AllRoles, handler);
                if (status != 0)
                {
                    Log.Warning($"Launch Services did not take {bundleId} for {type.Extension} ({status})");
                }

                CFRelease(contentType);
            }
        }
        finally
        {
            CFRelease(tagClass);
            CFRelease(handler);
        }
    }

    /// <summary>The UTI of the type as a CFString the caller releases.</summary>
    private static IntPtr ContentTypeOf(DocumentType type, IntPtr tagClass)
    {
        if (type.MacTypeIdentifier is { Length: > 0 } identifier)
        {
            return CFStringCreateWithCString(IntPtr.Zero, identifier, Utf8);
        }

        var tag = CFStringCreateWithCString(IntPtr.Zero, type.Name, Utf8);
        try
        {
            return UTTypeCreatePreferredIdentifierForTag(tagClass, tag, IntPtr.Zero);
        }
        finally
        {
            CFRelease(tag);
        }
    }

    [LibraryImport(CoreFoundation, StringMarshalling = StringMarshalling.Utf8)]
    private static partial IntPtr CFStringCreateWithCString(IntPtr allocator, string text, uint encoding);

    [LibraryImport(CoreFoundation)]
    private static partial void CFRelease(IntPtr value);

    [LibraryImport(CoreServices)]
    private static partial IntPtr UTTypeCreatePreferredIdentifierForTag(IntPtr tagClass, IntPtr tag, IntPtr conformingTo);

    [LibraryImport(CoreServices)]
    private static partial int LSSetDefaultRoleHandlerForContentType(IntPtr contentType, uint role, IntPtr handlerBundleId);
}
