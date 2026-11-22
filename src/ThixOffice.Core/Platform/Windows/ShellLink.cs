using System.Runtime.InteropServices;
using System.Runtime.InteropServices.Marshalling;
using System.Runtime.Versioning;

namespace ThixOffice.Core.Platform.Windows;

/// <summary>
/// Writes a Windows shortcut (.lnk) through the shell's ShellLink object, with source-generated COM interop: no
/// "dynamic", no built-in COM, so it keeps working in the trimmed single-file build.
/// </summary>
[SupportedOSPlatform("windows")]
internal static partial class ShellLink
{
    private static readonly Guid ShellLinkClass = new("00021401-0000-0000-C000-000000000046");
    private static readonly StrategyBasedComWrappers Wrappers = new();

    /// <summary>PKEY_AppUserModel_ID: the AppUserModelID of a shortcut (toasts of an unpackaged app find the app through it).</summary>
    private static readonly PropertyKey AppUserModelIdKey = new(new Guid("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3"), 5);

    public static void Write(string path, string target, string workingDirectory, string description, string iconPath, string? appUserModelId = null)
    {
        var interfaceId = typeof(IShellLinkW).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ShellLinkClass, IntPtr.Zero, 1 /* CLSCTX_INPROC_SERVER */, interfaceId, out var instance));
        try
        {
            var link = (IShellLinkW)Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
            link.SetPath(target);
            link.SetWorkingDirectory(workingDirectory);
            link.SetDescription(description);
            link.SetIconLocation(iconPath, 0);
            if (appUserModelId is not null)
            {
                var store = (IPropertyStore)link;
                var value = new PropVariant { Type = 31 /* VT_LPWSTR */, Pointer = Marshal.StringToCoTaskMemUni(appUserModelId) };
                try
                {
                    store.SetValue(AppUserModelIdKey, value);
                    store.Commit();
                }
                finally
                {
                    PropVariantClear(ref value);
                }
            }

            ((IPersistFile)link).Save(path, true);
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    /// <summary>The AppUserModelID a shortcut carries (null: none) – tests and the self test.</summary>
    public static string? ReadAppUserModelId(string path)
    {
        var interfaceId = typeof(IShellLinkW).GUID;
        Marshal.ThrowExceptionForHR(CoCreateInstance(ShellLinkClass, IntPtr.Zero, 1, interfaceId, out var instance));
        try
        {
            var link = (IShellLinkW)Wrappers.GetOrCreateObjectForComInstance(instance, CreateObjectFlags.UniqueInstance);
            ((IPersistFile)link).Load(path, 0);
            var value = new PropVariant();
            ((IPropertyStore)link).GetValue(AppUserModelIdKey, out value);
            try
            {
                return value.Type == 31 ? Marshal.PtrToStringUni(value.Pointer) : null;
            }
            finally
            {
                PropVariantClear(ref value);
            }
        }
        finally
        {
            Marshal.Release(instance);
        }
    }

    [LibraryImport("ole32.dll")]
    private static partial int PropVariantClear(ref PropVariant value);

    [LibraryImport("ole32.dll")]
    private static partial int CoCreateInstance(in Guid classId, IntPtr outer, uint context, in Guid interfaceId, out IntPtr instance);

    /// <summary>The methods in the order of the vtable; only the setters are used, the rest keep their place.</summary>
    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal partial interface IShellLinkW
    {
        void GetPath(IntPtr file, int maxPath, IntPtr findData, uint flags);

        void GetIDList(out IntPtr idList);

        void SetIDList(IntPtr idList);

        void GetDescription(IntPtr name, int maxName);

        void SetDescription(string name);

        void GetWorkingDirectory(IntPtr directory, int maxPath);

        void SetWorkingDirectory(string directory);

        void GetArguments(IntPtr arguments, int maxPath);

        void SetArguments(string arguments);

        void GetHotkey(out short hotkey);

        void SetHotkey(short hotkey);

        void GetShowCmd(out int showCommand);

        void SetShowCmd(int showCommand);

        void GetIconLocation(IntPtr iconPath, int maxPath, out int iconIndex);

        void SetIconLocation(string iconPath, int iconIndex);

        void SetRelativePath(string relativePath, uint reserved);

        void Resolve(IntPtr window, uint flags);

        void SetPath(string file);
    }

    [StructLayout(LayoutKind.Sequential)]
    internal readonly record struct PropertyKey(Guid FormatId, uint PropertyId);

    /// <summary>A PROPVARIANT as far as a string needs it (the union is 16 bytes on 64-bit, two pointers).</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct PropVariant
    {
        public ushort Type;
        public ushort Reserved1;
        public ushort Reserved2;
        public ushort Reserved3;
        public IntPtr Pointer;
        public IntPtr Rest;
    }

    [GeneratedComInterface]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    internal partial interface IPropertyStore
    {
        void GetCount(out uint count);

        void GetAt(uint index, out PropertyKey key);

        void GetValue(in PropertyKey key, out PropVariant value);

        void SetValue(in PropertyKey key, in PropVariant value);

        void Commit();
    }

    [GeneratedComInterface(StringMarshalling = StringMarshalling.Utf16)]
    [Guid("0000010B-0000-0000-C000-000000000046")]
    internal partial interface IPersistFile
    {
        void GetClassID(out Guid classId);

        [PreserveSig]
        int IsDirty();

        void Load(string fileName, uint mode);

        void Save(string fileName, [MarshalAs(UnmanagedType.Bool)] bool remember);

        void SaveCompleted(string fileName);

        void GetCurFile(out IntPtr fileName);
    }
}
