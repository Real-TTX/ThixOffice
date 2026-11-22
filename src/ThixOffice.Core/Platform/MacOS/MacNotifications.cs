using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Core.Platform.MacOS;

/// <summary>
/// macOS notifications with click-through, without changes to the bundle: NSUserNotificationCenter (deprecated since macOS 11, still delivered by the
/// system and – unlike UNUserNotificationCenter – usable without blocks or an entitlement) with a delegate class made at run time through the
/// Objective-C runtime: a click hands the notification's link to <see cref="Activated"/>, and notifications show even while the app is in front only
/// when the app asks. Only inside an app bundle (the center is nil otherwise, e.g. "dotnet run"): <see cref="Show"/> returns false and the caller falls back.
/// Call on the main thread.
/// </summary>
[SupportedOSPlatform("macos")]
internal static unsafe partial class MacNotifications
{
    private const string Runtime = "/usr/lib/libobjc.A.dylib";
    private const string LinkKey = "link";

    private static nint _delegate;

    /// <summary>A click on one of the app's notifications, with its link (main thread).</summary>
    public static Action<string>? Activated { get; set; }

    public static bool Show(string title, string subtitle, string text, string link)
    {
        try
        {
            var center = ObjectiveC.Send(ObjectiveC.objc_getClass("NSUserNotificationCenter"), ObjectiveC.Selector("defaultUserNotificationCenter"));
            if (center == 0)
            {
                return false;
            }

            EnsureDelegate(center);
            var notification = ObjectiveC.Send(ObjectiveC.Send(ObjectiveC.objc_getClass("NSUserNotification"), ObjectiveC.Selector("alloc")), ObjectiveC.Selector("init"));
            ObjectiveC.Send(notification, ObjectiveC.Selector("setTitle:"), ObjectiveC.String(title));
            if (subtitle.Length > 0)
            {
                ObjectiveC.Send(notification, ObjectiveC.Selector("setSubtitle:"), ObjectiveC.String(subtitle));
            }

            ObjectiveC.Send(notification, ObjectiveC.Selector("setInformativeText:"), ObjectiveC.String(text));
            var info = ObjectiveC.Send(ObjectiveC.objc_getClass("NSDictionary"), ObjectiveC.Selector("dictionaryWithObject:forKey:"), ObjectiveC.String(link), ObjectiveC.String(LinkKey));
            ObjectiveC.Send(notification, ObjectiveC.Selector("setUserInfo:"), info);
            ObjectiveC.Send(center, ObjectiveC.Selector("deliverNotification:"), notification);
            ObjectiveC.Send(notification, ObjectiveC.Selector("release"));
            return true;
        }
        catch (Exception exception) when (exception is DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Info($"No macOS notification center: {exception.GetType().Name}");
            return false;
        }
    }

    /// <summary>The delegate (a run-time class "ThixNotificationDelegate" with the two methods), made and set once; kept alive for the process.</summary>
    private static void EnsureDelegate(nint center)
    {
        if (_delegate != 0)
        {
            return;
        }

        var type = ObjectiveC.objc_getClass("ThixNotificationDelegate");
        if (type == 0)
        {
            type = objc_allocateClassPair(ObjectiveC.objc_getClass("NSObject"), "ThixNotificationDelegate", 0);
            class_addMethod(type, ObjectiveC.Selector("userNotificationCenter:didActivateNotification:"),
                (nint)(delegate* unmanaged<nint, nint, nint, nint, void>)&DidActivate, "v@:@@");
            class_addMethod(type, ObjectiveC.Selector("userNotificationCenter:shouldPresentNotification:"),
                (nint)(delegate* unmanaged<nint, nint, nint, nint, byte>)&ShouldPresent, "c@:@@");
            objc_registerClassPair(type);
        }

        _delegate = ObjectiveC.Send(ObjectiveC.Send(type, ObjectiveC.Selector("alloc")), ObjectiveC.Selector("init"));
        ObjectiveC.Send(center, ObjectiveC.Selector("setDelegate:"), _delegate);
    }

    [UnmanagedCallersOnly]
    private static void DidActivate(nint self, nint selector, nint center, nint notification)
    {
        try
        {
            var info = ObjectiveC.Send(notification, ObjectiveC.Selector("userInfo"));
            var link = info == 0 ? null : ObjectiveC.Text(ObjectiveC.Send(info, ObjectiveC.Selector("objectForKey:"), ObjectiveC.String(LinkKey)));
            ObjectiveC.Send(center, ObjectiveC.Selector("removeDeliveredNotification:"), notification);
            if (link is not null)
            {
                Activated?.Invoke(link);
            }
        }
        catch (Exception exception)
        {
            // never let an exception cross into the Objective-C runtime
            Log.Warning($"A notification click failed: {exception.GetType().Name}");
        }
    }

    /// <summary>Shown even while the app is in front (the app itself decides when to notify).</summary>
    [UnmanagedCallersOnly]
    private static byte ShouldPresent(nint self, nint selector, nint center, nint notification) => 1;

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    private static partial nint objc_allocateClassPair(nint superclass, string name, nint extraBytes);

    [LibraryImport(Runtime)]
    private static partial void objc_registerClassPair(nint type);

    [LibraryImport(Runtime, StringMarshalling = StringMarshalling.Utf8)]
    [return: MarshalAs(UnmanagedType.I1)]
    private static partial bool class_addMethod(nint type, nint selector, nint implementation, string types);
}
