using System.Diagnostics;
using MatDesktop;

namespace ThixMail;

/// <summary>How ThixMail tells about new mail while its window is not in front (a small platform seam; tests record instead).</summary>
public interface IMailNotifier
{
    void Show(string title, string text, Action onClick);
}

/// <summary>
/// The notifier of the platform: on Windows MatDesktop's notification (a toast at the bottom right in the Thix look; a click brings the message), on
/// macOS the system's notification centre through osascript (no click-through there). <see cref="Notifier"/> can be replaced by tests.
/// </summary>
public static class MailNotifications
{
    public static IMailNotifier Notifier { get; set; } = OperatingSystem.IsMacOS() ? new MacNotifier() : new ToastNotifier();

    private sealed class ToastNotifier : IMailNotifier
    {
        public void Show(string title, string text, Action onClick) => MatNotification.Show(title, text, isWarning: false, onClick);
    }

    private sealed class MacNotifier : IMailNotifier
    {
        public void Show(string title, string text, Action onClick)
        {
            try
            {
                var start = new ProcessStartInfo("osascript") { UseShellExecute = false, CreateNoWindow = true };
                start.ArgumentList.Add("-e");
                start.ArgumentList.Add($"display notification \"{Escape(text)}\" with title \"ThixMail\" subtitle \"{Escape(title)}\"");
                Process.Start(start)?.Dispose();
            }
            catch (Exception exception) when (exception is System.ComponentModel.Win32Exception or InvalidOperationException)
            {
                ThixOffice.Core.Infrastructure.Log.Warning($"A notification could not be shown: {exception.GetType().Name}");
            }
        }

        private static string Escape(string text) => text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace("\"", "\\\"", StringComparison.Ordinal).Replace('\n', ' ');
    }
}
