using System.Diagnostics;
using System.Net.Http.Json;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json.Serialization;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>One downloadable file of a version (Windows setup, zipped Mac app).</summary>
public sealed record UpdatePackage(
    [property: JsonPropertyName("file")] string File,
    [property: JsonPropertyName("sha256")] string Sha256);

/// <summary>
/// The manifest CI publishes next to the packages in the rolling release of a channel (SETUP-06).
/// <see cref="Setup"/>/<see cref="Sha256"/> are the Windows setup; <see cref="Packages"/> holds one package per platform
/// ("win-x64", "osx-arm64", "osx-x64").
/// </summary>
public sealed record UpdateManifest(
    [property: JsonPropertyName("version")] string Version,
    [property: JsonPropertyName("channel")] string Channel,
    [property: JsonPropertyName("buildNumber")] int BuildNumber,
    [property: JsonPropertyName("setup")] string Setup,
    [property: JsonPropertyName("sha256")] string Sha256,
    [property: JsonPropertyName("publishedAt")] DateTimeOffset PublishedAt,
    [property: JsonPropertyName("packages")] Dictionary<string, UpdatePackage>? Packages = null);

public sealed record UpdateCheckResult(bool IsUpdateAvailable, string Message, UpdateManifest? Manifest = null);

/// <summary>A downloaded update whose checksum is right: it waits for the app to close (or for "Jetzt installieren").</summary>
public sealed record DownloadedUpdate(UpdateManifest Manifest, string PackagePath);

/// <summary>
/// Over-the-air updates from GitHub releases. Each channel has one rolling release whose files every build replaces:
/// "release-channel" (branch main) and "nightly-channel" (branch dev). A build only looks at its own channel; local builds
/// never update themselves.
///
/// An editor never replaces itself while somebody writes. An update is downloaded and checked (SHA-256)
/// first; it is installed when the app closes (automatic updates) or right away when the user asks for it – then the app
/// starts again ("--restart").
///
/// Windows: the package is the new ThixWrite.exe (ThixCalc.exe …) itself, started with "--update" (InstallService): it ends a running
/// instance, replaces the program file and, with "--restart", starts the app again.
/// macOS: the package is the zipped app bundle; a small script replaces the bundle once the app has ended and, on
/// request, opens it again. Documents and settings stay as they are on both.
/// </summary>
public static class UpdateService
{
    /// <summary>The manifest of this app in the rolling releases (thixwrite-update.json): every app of the suite has its own.</summary>
    public static string ManifestFileName => AppInfo.Definition.ManifestFileName;

    /// <summary>The Windows program started by an update installs itself silently (InstallService).</summary>
    public const string WindowsUpdateArgument = "--update";

    /// <summary>With the update: start the app again afterwards.</summary>
    public const string RestartArgument = "--restart";

    public static readonly TimeSpan CheckInterval = TimeSpan.FromHours(6);

    private static readonly HttpClient Http = CreateHttpClient();

    public static DateTimeOffset? LastCheck { get; private set; }

    public static UpdateCheckResult? LastResult { get; private set; }

    /// <summary>The update that is downloaded and waits to be installed (null: none).</summary>
    public static DownloadedUpdate? Downloaded { get; private set; }

    /// <summary>The package name of this platform: win-x64, osx-arm64 or osx-x64.</summary>
    public static string PlatformKey { get; } =
        (OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux") + "-" +
        (RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x64");

    public static string ReleaseTag(string channel) => $"{channel}-channel";

    public static string ManifestUrl(string channel) =>
        $"https://github.com/{AppInfo.Repository}/releases/download/{ReleaseTag(channel)}/{ManifestFileName}";

    public static string ReleasePageUrl(string channel) =>
        $"https://github.com/{AppInfo.Repository}/releases/tag/{ReleaseTag(channel)}";

    /// <summary>A Mac app only updates itself as an app bundle (not when started with "dotnet run").</summary>
    public static bool CanUpdate => !AppInfo.IsLocalBuild && !AccessPolicy.AreUpdatesDisabled &&
                                    (OperatingSystem.IsWindows() || (OperatingSystem.IsMacOS() && MacAppBundle.Current is not null));

    public static async Task<UpdateCheckResult> CheckAsync(CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureUpdatesAllowed();
        if (AppInfo.IsLocalBuild)
        {
            return Remember(new UpdateCheckResult(false, "Lokaler Build – keine automatischen Updates."));
        }

        try
        {
            var manifest = await Http.GetFromJsonAsync<UpdateManifest>(ManifestUrl(AppInfo.Channel), cancellationToken);
            if (manifest is null)
            {
                return Remember(new UpdateCheckResult(false, "Update-Information ist leer."));
            }

            if (!IsNewer(manifest, AppInfo.Channel, AppInfo.BuildNumber))
            {
                return Remember(new UpdateCheckResult(false, $"{AppInfo.ProductName} ist aktuell."));
            }

            return Remember(PackageFor(manifest, PlatformKey) is null
                ? new UpdateCheckResult(false, $"Version {manifest.Version} gibt es noch nicht für dieses System ({PlatformKey}).")
                : new UpdateCheckResult(true, $"Version {manifest.Version} ist verfügbar.", manifest));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException)
        {
            Log.Warning($"Update check failed: {exception.Message}");
            return Remember(new UpdateCheckResult(false, "Update-Prüfung fehlgeschlagen – keine Verbindung zu GitHub?"));
        }
    }

    /// <summary>Same channel and a higher build number (build numbers only grow).</summary>
    public static bool IsNewer(UpdateManifest manifest, string channel, int buildNumber) =>
        string.Equals(manifest.Channel, channel, StringComparison.OrdinalIgnoreCase) && manifest.BuildNumber > buildNumber;

    /// <summary>The package of a platform; Windows falls back to the setup fields of the manifest.</summary>
    public static UpdatePackage? PackageFor(UpdateManifest manifest, string platformKey)
    {
        if (manifest.Packages?.TryGetValue(platformKey, out var package) == true)
        {
            return package;
        }

        return platformKey.StartsWith("win-", StringComparison.Ordinal) && manifest.Setup.Length > 0
            ? new UpdatePackage(manifest.Setup, manifest.Sha256)
            : null;
    }

    /// <summary>Downloads the package of this platform and checks its SHA-256; it then waits in <see cref="Downloaded"/>.</summary>
    public static async Task<DownloadedUpdate> DownloadAsync(UpdateManifest manifest, IProgress<double>? progress = null,
        CancellationToken cancellationToken = default)
    {
        AccessPolicy.EnsureUpdatesAllowed();
        if (Downloaded is { } waiting && waiting.Manifest.BuildNumber == manifest.BuildNumber && File.Exists(waiting.PackagePath))
        {
            return waiting;
        }

        var package = PackageFor(manifest, PlatformKey);
        if (package is null || !IsSafeFileName(package.File))
        {
            throw new BusinessException("Die Update-Information ist ungültig.");
        }

        Directory.CreateDirectory(AppPaths.UpdateDirectory);
        var packagePath = Path.Combine(AppPaths.UpdateDirectory, package.File);
        var url = $"https://github.com/{AppInfo.Repository}/releases/download/{ReleaseTag(manifest.Channel)}/{package.File}";

        using (var response = await Http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken))
        {
            response.EnsureSuccessStatusCode();
            var total = response.Content.Headers.ContentLength ?? 0;
            await using var source = await response.Content.ReadAsStreamAsync(cancellationToken);
            await using var target = File.Create(packagePath);
            var buffer = new byte[81920];
            long received = 0;
            int read;
            while ((read = await source.ReadAsync(buffer, cancellationToken)) > 0)
            {
                await target.WriteAsync(buffer.AsMemory(0, read), cancellationToken);
                received += read;
                if (total > 0)
                {
                    progress?.Report((double)received / total);
                }
            }
        }

        // Hashing runs off the calling (UI) thread.
        var hash = await Task.Run(() => Convert.ToHexString(SHA256.HashData(File.ReadAllBytes(packagePath))), cancellationToken);
        if (!string.Equals(hash, package.Sha256, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(packagePath);
            throw new BusinessException("Das heruntergeladene Update ist beschädigt (Prüfsumme stimmt nicht).");
        }

        Log.Info($"Update {manifest.Version} downloaded and checked");
        return Downloaded = new DownloadedUpdate(manifest, packagePath);
    }

    /// <summary>
    /// Starts the installation of a downloaded update. The caller ends the app right afterwards: the setup waits for it
    /// (Windows) or the bundle is replaced once it has ended (macOS). <paramref name="restart"/>: the app opens again.
    /// </summary>
    public static async Task StartInstallerAsync(DownloadedUpdate update, bool restart)
    {
        AccessPolicy.EnsureUpdatesAllowed();
        if (!File.Exists(update.PackagePath))
        {
            Downloaded = null;
            throw new BusinessException("Das Update ist nicht mehr da. Bitte erneut nach Updates suchen.");
        }

        Log.Info($"Starting update to {update.Manifest.Version} (restart: {restart})");
        if (OperatingSystem.IsMacOS())
        {
            await Task.Run(() =>
            {
                if (OperatingSystem.IsMacOS())
                {
                    MacAppBundle.StartReplacement(update.PackagePath, reopen: restart); // unpacking takes a moment
                }
            });
            return;
        }

        // Windows checks a freshly downloaded, unsigned program (Defender / SmartScreen) inside CreateProcess: off the UI thread.
        var started = Stopwatch.StartNew();
        var arguments = restart ? $"{WindowsUpdateArgument} {RestartArgument}" : WindowsUpdateArgument;
        await Task.Run(() => Process.Start(new ProcessStartInfo(update.PackagePath, arguments)
        {
            UseShellExecute = false,
            WorkingDirectory = AppPaths.UpdateDirectory,
        })?.Dispose());
        if (started.Elapsed > TimeSpan.FromSeconds(2))
        {
            Log.Info($"Starting the update took {started.Elapsed.TotalSeconds:0} s (virus scan of the new program)");
        }
    }

    /// <summary>Removes packages of earlier updates (at start: the new version runs, its setup is no longer needed).</summary>
    public static void RemoveOldPackages()
    {
        try
        {
            if (Directory.Exists(AppPaths.UpdateDirectory))
            {
                Directory.Delete(AppPaths.UpdateDirectory, recursive: true);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Old update packages could not be removed: {exception.Message}");
        }
    }

    internal static bool IsSafeFileName(string fileName) =>
        fileName.Length > 0 &&
        (fileName.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || fileName.EndsWith(".zip", StringComparison.OrdinalIgnoreCase)) &&
        fileName.IndexOfAny(Path.GetInvalidFileNameChars()) < 0 && !fileName.Contains("..") && !fileName.Contains('/') && !fileName.Contains('\\');

    /// <summary>Tests and snapshots set what the last check found.</summary>
    internal static void SetStateForTests(UpdateCheckResult? result, DateTimeOffset? lastCheck = null, DownloadedUpdate? downloaded = null)
    {
        LastResult = result;
        LastCheck = lastCheck;
        Downloaded = downloaded;
    }

    private static UpdateCheckResult Remember(UpdateCheckResult result)
    {
        LastCheck = DateTimeOffset.Now;
        LastResult = result;
        return result;
    }

    private static HttpClient CreateHttpClient()
    {
        var client = new HttpClient { Timeout = TimeSpan.FromMinutes(5) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd($"{AppInfo.ProductName}/{AppInfo.BuildNumber}");
        return client;
    }
}
