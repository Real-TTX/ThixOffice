using System.Diagnostics;
using MatDesktop;
using ThixOffice.Core.Infrastructure;

namespace ThixMail;

/// <summary>What a notification about new mail says, the link a system notification starts (<c>thixmail:message?id=…</c>) and what a click does here.</summary>
public sealed record MailNotice(string Title, string Text, string Link, Action Open);

/// <summary>How ThixMail tells about new mail while its window is not in front (a small platform seam; tests record instead).</summary>
public interface IMailNotifier
{
    void Show(MailNotice notice);
}

/// <summary>
/// The notifier of the platform. Windows: a system toast (Action Center) as the app's AppUserModelID – shown only when the installed Start-menu shortcut
/// carries it; a click starts <c>thixmail:message?id=…</c>, which the running instance takes over (or which starts ThixMail and opens the message) –,
/// else MatDesktop's own notification at the bottom right (a development build, notifications switched off for the app). macOS: the notification centre
/// with click-through (NSUserNotificationCenter inside the app bundle), else osascript without click-through. <see cref="Notifier"/> can be replaced by tests.
/// </summary>
public static class MailNotifications
{
    public static IMailNotifier Notifier { get; set; } = OperatingSystem.IsMacOS() ? new MacNotifier() : new WindowsNotifier();

    /// <summary>A click on a system notification of this process came back as its link (macOS; Windows comes through the URL scheme).</summary>
    public static Action<string>? LinkActivated { get; set; }

    private sealed class WindowsNotifier : IMailNotifier
    {
        public void Show(MailNotice notice)
        {
            // only the installed program has the Start-menu shortcut with the AUMID (without it Windows drops a toast silently)
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 10240) && Environment.ProcessPath is { } program
                && ThixOffice.Core.Business.InstallService.IsInstalledExecutable(program) && File.Exists(ThixOffice.Core.Business.InstallService.StartMenuShortcut)
                && ThixOffice.Core.Platform.Windows.WindowsToasts.Show(ThixMail.Core.ThixMailApp.Definition.AppUserModelId,
                    ThixOffice.Core.Platform.Windows.WindowsToasts.ToastXml(notice.Title, notice.Text, notice.Link)))
            {
                Log.Info("New mail: system notification shown");
                return;
            }

            Log.Info("New mail: the app's own notification (no installed program with its Start-menu shortcut, or toasts unavailable)");
            MatNotification.Show(notice.Title, notice.Text, isWarning: false, notice.Open);
        }
    }

    private sealed class MacNotifier : IMailNotifier
    {
        public void Show(MailNotice notice)
        {
            if (OperatingSystem.IsMacOS())
            {
                ThixOffice.Core.Platform.MacOS.MacNotifications.Activated ??= link => LinkActivated?.Invoke(link);
                if (ThixOffice.Core.Platform.MacOS.MacNotifications.Show("ThixMail", notice.Title, notice.Text, notice.Link))
                {
                    return;
                }
            }

            try
            {
                var start = new ProcessStartInfo("osascript") { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add($"display notification \"{Escape(notice.Text)}\" with title \"ThixMail\" subtitle \"{Escape(notice.Title)}\"");
                Process.Start(start)?.Dispose();
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                Log.Warning($"A notification could not be shown: {exception.GetType().Name}");
            }
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace('\n', ' ');
    }
}
