using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Security;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Core.Platform.Windows;

/// <summary>
/// Windows toast notifications for an UNPACKAGED app, without a WinRT projection (no extra assembly, nothing for the trimmer to lose): the WinRT
/// classes are activated through combase (RoActivateInstance / RoGetActivationFactory with HSTRINGs) and called through their vtables –
/// XmlDocument.LoadXml, ToastNotification's factory, ToastNotificationManager.CreateToastNotifierWithId(AUMID), ToastNotifier.Setting and Show.
/// Windows shows the toast only when a Start-menu shortcut carries the AUMID (the setup writes it: <see cref="ShellLink.Write"/>); otherwise
/// <see cref="Show"/> returns false and the caller falls back to its own notification. A click starts the toast's <c>launch</c> link
/// (activationType "protocol": the app's URL scheme, registered by the setup).
/// </summary>
internal static unsafe partial class WindowsToasts
{
    private static readonly Guid XmlDocumentIo = new("6CD0E74E-EE65-4489-9EBF-CA43E87BA637");
    private static readonly Guid XmlDocument = new("F7F3A506-1E87-42D6-BCFB-B8C809FA5494");
    private static readonly Guid ToastNotificationFactory = new("04124B20-82C6-4229-B109-FD9ED4662B53");
    private static readonly Guid ToastNotificationManagerStatics = new("50AC103F-D235-4598-BBEF-98FE4D1A3AD4");

    // vtable slots after IUnknown (3) and IInspectable (3)
    private const int LoadXmlSlot = 6;              // IXmlDocumentIO.LoadXml(HSTRING)
    private const int CreateToastSlot = 6;          // IToastNotificationFactory.CreateToastNotification(IXmlDocument*, IToastNotification**)
    private const int CreateNotifierWithIdSlot = 7; // IToastNotificationManagerStatics.CreateToastNotifierWithId(HSTRING, IToastNotifier**)
    private const int ShowSlot = 6;                 // IToastNotifier.Show(IToastNotification*)
    private const int SettingSlot = 8;              // IToastNotifier.get_Setting(NotificationSetting*)

    /// <summary>The toast XML of a title, a text and the link a click starts (all escaped).</summary>
    public static string ToastXml(string title, string text, string? launch) =>
        $"<toast{(launch is null ? "" : $" launch=\"{SecurityElement.Escape(launch)}\" activationType=\"protocol\"")}>" +
        $"<visual><binding template=\"ToastGeneric\"><text>{SecurityElement.Escape(title)}</text><text>{SecurityElement.Escape(text)}</text></binding></visual></toast>";

    /// <summary>
    /// Shows a toast as the app <paramref name="appUserModelId"/>. False (nothing shown) when Windows does not know the AUMID (no shortcut – a
    /// development build), notifications are off for the app or the system, or the system lacks the API; never throws.
    /// </summary>
    [SupportedOSPlatform("windows10.0.10240")]
    public static bool Show(string appUserModelId, string xml)
    {
        if (!OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240))
        {
            return false;
        }

        nint document = 0, documentIo = 0, xmlDocument = 0, factory = 0, toast = 0, statics = 0, notifier = 0;
        try
        {
            using (var name = new HString("Windows.Data.Xml.Dom.XmlDocument"))
            {
                Check(RoActivateInstance(name.Handle, out document));
            }

            Check(Marshal.QueryInterface(document, XmlDocumentIo, out documentIo));
            using (var content = new HString(xml))
            {
                Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(documentIo, LoadXmlSlot))(documentIo, content.Handle));
            }

            Check(Marshal.QueryInterface(document, XmlDocument, out xmlDocument));
            using (var name = new HString("Windows.UI.Notifications.ToastNotification"))
            {
                Check(RoGetActivationFactory(name.Handle, ToastNotificationFactory, out factory));
            }

            Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(factory, CreateToastSlot))(factory, xmlDocument, &toast));
            using (var name = new HString("Windows.UI.Notifications.ToastNotificationManager"))
            {
                Check(RoGetActivationFactory(name.Handle, ToastNotificationManagerStatics, out statics));
            }

            using (var id = new HString(appUserModelId))
            {
                Check(((delegate* unmanaged[Stdcall]<nint, nint, nint*, int>)Slot(statics, CreateNotifierWithIdSlot))(statics, id.Handle, &notifier));
            }

            var setting = -1;
            var settingResult = ((delegate* unmanaged[Stdcall]<nint, int*, int>)Slot(notifier, SettingSlot))(notifier, &setting);
            if (settingResult >= 0 && setting != 0)
            {
                LastFailure = $"setting {setting}";
                Log.Info($"Toasts are not available for the app (setting {setting}): using the app's own notification");
                return false;
            }

            Check(((delegate* unmanaged[Stdcall]<nint, nint, int>)Slot(notifier, ShowSlot))(notifier, toast));
            return true;
        }
        catch (Exception exception) when (exception is COMException or DllNotFoundException or EntryPointNotFoundException or InvalidCastException)
        {
            LastFailure = $"{exception.GetType().Name} 0x{exception.HResult:X8} {exception.StackTrace}";
            Log.Info($"A toast could not be shown ({exception.GetType().Name} 0x{exception.HResult:X8}): using the app's own notification");
            return false;
        }
        finally
        {
            foreach (var pointer in new[] { notifier, statics, toast, factory, xmlDocument, documentIo, document })
            {
                if (pointer != 0)
                {
                    Marshal.Release(pointer);
                }
            }
        }
    }

    /// <summary>The process takes the app's AUMID (its windows group with the Start-menu shortcut that carries it, the toasts are its).</summary>
    [SupportedOSPlatform("windows")]
    public static void UseAppUserModelId(string appUserModelId)
    {
        var result = SetCurrentProcessExplicitAppUserModelID(appUserModelId);
        if (result < 0)
        {
            Log.Warning($"The AppUserModelID could not be set (0x{result:X8})");
        }
    }

    [LibraryImport("shell32.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SetCurrentProcessExplicitAppUserModelID(string appUserModelId);

    /// <summary>Why the last toast was not shown (tests, the self test).</summary>
    internal static string? LastFailure { get; private set; }

    private static nint Slot(nint instance, int slot) => (*(nint**)instance)[slot];

    private static void Check(int result)
    {
        if (result < 0)
        {
            throw new COMException("WinRT call failed", result);
        }
    }

    [LibraryImport("combase.dll")]
    private static partial int RoActivateInstance(nint activatableClassId, out nint instance);

    [LibraryImport("combase.dll")]
    private static partial int RoGetActivationFactory(nint activatableClassId, in Guid interfaceId, out nint factory);

    [LibraryImport("combase.dll", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int WindowsCreateString(string source, int length, out nint handle);

    [LibraryImport("combase.dll")]
    private static partial int WindowsDeleteString(nint handle);

    /// <summary>An HSTRING for the length of a call.</summary>
    private readonly struct HString : IDisposable
    {
        public HString(string text) => Check(WindowsCreateString(text, text.Length, out Handle));

        public readonly nint Handle;

        public void Dispose()
        {
            if (Handle != 0)
            {
                WindowsDeleteString(Handle);
            }
        }
    }
}
