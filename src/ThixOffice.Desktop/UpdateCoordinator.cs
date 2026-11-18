using Avalonia.Threading;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Pages;

namespace ThixOffice.Desktop;

/// <summary>
/// When to look for updates and what to do with one (like ThixNote): check shortly after start and every 6 hours (when
/// enabled), only in the app's own channel. With automatic updates the new version is downloaded at once and installed
/// when the app closes – an editor never restarts under the user's hands. Otherwise it is only offered (Einstellungen ›
/// Updates, the about page).
/// </summary>
public static class UpdateCoordinator
{
    private static readonly DispatcherTimer Timer = new() { Interval = UpdateService.CheckInterval };
    private static int _notifiedBuild;
    private static bool _started;

    /// <summary>Raised on the UI thread after a check or a download.</summary>
    public static event EventHandler? Changed;

    /// <summary>The version that is available (downloaded or not), or null.</summary>
    public static string? AvailableVersion => UpdateService.LastResult is { IsUpdateAvailable: true, Manifest: { } manifest } ? manifest.Version : null;

    public static void Start()
    {
        if (_started || !UpdateService.CanUpdate)
        {
            return;
        }

        _started = true;
        Timer.Tick += async (_, _) => await CheckInBackgroundAsync();
        Timer.Start();

        // The first check waits a bit: the start has more important things to do.
        DispatcherTimer.RunOnce(() => _ = CheckInBackgroundAsync(), TimeSpan.FromSeconds(45));
    }

    public static async Task<UpdateCheckResult> CheckAsync()
    {
        var result = await UpdateService.CheckAsync();
        Changed?.Invoke(null, EventArgs.Empty);
        return result;
    }

    /// <summary>"Jetzt installieren": downloads (if needed), then every window closes (asking about its changes) and the app opens again in the new version.</summary>
    public static async Task InstallNowAsync(UpdateManifest manifest, IProgress<double>? progress = null)
    {
        var update = await UpdateService.DownloadAsync(manifest, progress);
        Changed?.Invoke(null, EventArgs.Empty);
        await OfficeApplication.Instance.InstallUpdateNowAsync(update);
    }

    private static async Task CheckInBackgroundAsync()
    {
        var settings = SettingsService.Get();
        if (!settings.CheckForUpdates)
        {
            return;
        }

        try
        {
            var result = await CheckAsync();
            if (result is not { IsUpdateAvailable: true, Manifest: { } manifest })
            {
                return;
            }

            if (settings.InstallUpdatesAutomatically)
            {
                await UpdateService.DownloadAsync(manifest);
                Changed?.Invoke(null, EventArgs.Empty);
                Notify(manifest, $"Version {manifest.Version} wird installiert, wenn Sie {AppInfo.ProductName} schließen. Ihre Dokumente bleiben, wie sie sind.");
            }
            else
            {
                Notify(manifest, $"Version {manifest.Version} kann installiert werden (Datei › Einstellungen › Updates).");
            }
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException or TaskCanceledException or BusinessException)
        {
            Log.Warning($"Automatic update failed: {exception.Message}");
        }
    }

    /// <summary>Once per version (TRAY-04: an update that is ready is something the user did not cause).</summary>
    private static void Notify(UpdateManifest manifest, string text)
    {
        if (_notifiedBuild == manifest.BuildNumber)
        {
            return;
        }

        _notifiedBuild = manifest.BuildNumber;
        MatNotification.Show("Update verfügbar", text, onClick: () => OfficeApplication.Instance.DocumentWindows.LastOrDefault()?.ShowSettings(SettingsPage.UpdatesTab));
    }
}
