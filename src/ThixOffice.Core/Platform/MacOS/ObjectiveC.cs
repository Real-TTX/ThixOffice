using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace ThixOffice.Core.Platform.MacOS;

/// <summary>
/// The few Objective-C runtime calls ThixOffice needs on macOS (the user's full name, the system spell checker): classes, selectors
/// and objc_msgSend in the shapes used, NSString in and out, an autorelease pool around a call. No reflection, trimming-safe.
/// </summary>
[SupportedOSPlatform("macos")]
internal static partial class ObjectiveC
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string Foundation = "/System/Library/Frameworks/Foundation.framework/Foundation";

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr objc_getClass(string name);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr sel_registerName(string name);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr first, IntPtr second);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    public static partial IntPtr Send(IntPtr receiver, IntPtr selector, NSRange range, IntPtr first, IntPtr second, IntPtr third);

    /// <summary>-checkSpellingOfString:startingAt:language:wrap:inSpellDocumentWithTag:wordCount: (returns an NSRange).</summary>
    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    public static partial NSRange SendRange(IntPtr receiver, IntPtr selector, IntPtr text, nint start, IntPtr language, [MarshalAs(UnmanagedType.I1)] bool wrap, nint tag, IntPtr wordCount);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    public static partial nint SendInteger(IntPtr receiver, IntPtr selector);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    [return: MarshalAs(UnmanagedType.I1)]
    public static partial bool SendBool(IntPtr receiver, IntPtr selector, IntPtr argument);

    [LibraryImport(Runtime, EntryPoint = "objc_msgSend")]
    public static partial IntPtr SendIndex(IntPtr receiver, IntPtr selector, nint index);

    [LibraryImport(Foundation)]
    public static partial IntPtr NSFullUserName();

    /// <summary>An NSRange (location, length) as Foundation lays it out.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public readonly struct NSRange(nint location, nint length)
    {
        public nint Location { get; } = location;

        public nint Length { get; } = length;
    }

    public static IntPtr Selector(string name) => sel_registerName(name);

    /// <summary>A new autoreleased NSString of <paramref name="text"/>.</summary>
    public static IntPtr String(string text)
    {
        var bytes = Marshal.StringToCoTaskMemUTF8(text);
        try
        {
            return Send(objc_getClass("NSString"), Selector("stringWithUTF8String:"), bytes);
        }
        finally
        {
            Marshal.FreeCoTaskMem(bytes);
        }
    }

    /// <summary>The text of an NSString (null for nil).</summary>
    public static string? Text(IntPtr nsString) =>
        nsString == IntPtr.Zero ? null : Marshal.PtrToStringUTF8(Send(nsString, Selector("UTF8String")));

    /// <summary>Runs <paramref name="action"/> inside an autorelease pool (calls from a worker thread need one).</summary>
    public static T InPool<T>(Func<T> action)
    {
        var pool = Send(Send(objc_getClass("NSAutoreleasePool"), Selector("alloc")), Selector("init"));
        try
        {
            return action();
        }
        finally
        {
            Send(pool, Selector("drain"));
        }
    }
}
