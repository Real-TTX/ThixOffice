using System.Net;
using System.Text.Json;
using MatCloud.Contract;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>What the connection to ThixCloud is right now.</summary>
public enum CloudState
{
    /// <summary>Not signed in.</summary>
    Off,

    /// <summary>An administrator switched the cloud off for this app (policy DisableCloud).</summary>
    Disabled,

    /// <summary>A sign-in is running.</summary>
    Connecting,

    /// <summary>Signed in, the cloud answered last time.</summary>
    Online,

    /// <summary>Signed in, but the cloud could not be reached last time (documents wait in their working copies).</summary>
    Offline,

    /// <summary>The cloud ended the sign-in (the device was removed, 401): sign in again.</summary>
    SignedOut,

    /// <summary>The administration of the cloud switched the app or ThixDrive off (403).</summary>
    Forbidden,
}

/// <summary>The state of the connection and its words for people (the Cloud tab, the title bar).</summary>
public sealed record CloudStatus(CloudState State, string Message);

/// <summary>
/// The connection of an office app to ThixCloud (MatCloud), the same for every app of the suite: the app signs in as its
/// <see cref="OfficeAppDefinition.CloudAppId"/> (ThixWrite = <c>write</c>) and reaches the Drive of its account through
/// <see cref="DriveService"/>. Signing in – in the browser with a code (first), with user name and password (second), or with the
/// sign-in ThixLauncher left when it installed the app (redeemed silently at the start) – and out, the account, the state.
/// A device the cloud removed (401) is signed out here too; an app or Drive the administration switched off (403) is shown as
/// such. The device token is kept protected (<see cref="TokenStore"/>, one entry per app); a password is used once and never
/// stored. Nothing here logs a token or a password. Every entry checks the policy DisableCloud first.
/// </summary>
public static class CloudService
{
    private const string NotConnected = "Nicht angemeldet";

    private static CloudStatus? _status;

    /// <summary>Raised when <see cref="Status"/> changes (any thread).</summary>
    public static event EventHandler<CloudStatus>? StatusChanged;

    /// <summary>The state of the connection; at the start Online when signed in (the first call tells).</summary>
    public static CloudStatus Status => _status ??= InitialStatus();

    /// <summary>The app has a cloud and no policy switches it off: the places, the Cloud tab and the sign-in are offered.</summary>
    public static bool IsAvailable => AppInfo.Definition.HasCloud && !AccessPolicy.IsCloudDisabled;

    /// <summary>True when this app is signed in at ThixCloud.</summary>
    public static bool IsConnected => AppInfo.Definition.HasCloud && Settings.IsConnected;

    /// <summary>The sign-in as it is stored (address, account, device).</summary>
    public static CloudSettings Settings => SettingsService.Get().Cloud;

    /// <summary>How the app introduces itself to the cloud: "ThixWrite/0.1.42-20261119 (Windows)".</summary>
    public static string UserAgent => $"{AppInfo.ProductName}/{AppInfo.Version} ({AppInfo.PlatformName})";

    /// <summary>Tests (and the self test, the snapshots) talk to a cloud in memory.</summary>
    internal static Func<HttpMessageHandler>? HandlerForTests { get; set; }

    /// <summary>True when this app is signed in at <paramref name="serverUrl"/> as <paramref name="userName"/> (a recent ThixDrive document can open).</summary>
    public static bool IsSignedInTo(string serverUrl, string userName)
    {
        if (!AppInfo.IsInitialized || !IsConnected)
        {
            return false;
        }

        var cloud = Settings;
        return string.Equals(cloud.ServerUrl, serverUrl, StringComparison.OrdinalIgnoreCase) && string.Equals(cloud.UserName, userName, StringComparison.OrdinalIgnoreCase);
    }

    // ===== Signing in and out =====

    /// <summary>
    /// The sign-in in the browser (every account, also one of MatIdentity): the cloud gives a code, the UI shows it
    /// (<paramref name="onCode"/>) and opens the page (<paramref name="openAddress"/>); the app waits until the person confirms it
    /// there. False when it was cancelled.
    /// </summary>
    public static async Task<bool> ConnectInBrowserAsync(string serverUrl, string deviceName, Action<DeviceLinkStart> onCode, Action<string> openAddress,
        CancellationToken cancellationToken = default)
    {
        var app = EnsureAllowed();
        var url = NormalizeServerUrl(serverUrl);
        var device = CheckDeviceName(deviceName);
        SetStatus(new CloudStatus(CloudState.Connecting, "Warte auf die Bestätigung im Browser …"));
        DeviceLoginResponse login;
        try
        {
            using var client = CreateClient(url, null);
            var start = await client.StartLinkAsync(new DeviceLinkRequest(device, AppInfo.PlatformName, AppInfo.Version, app), cancellationToken);
            onCode(start);
            openAddress(start.VerificationUrl);
            login = await client.WaitForLinkAsync(start, cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            SetStatus(InitialStatus());
            return false;
        }
        catch (Exception exception) when (IsCloudFailure(exception, cancellationToken))
        {
            SetStatus(InitialStatus());
            throw ExplainSignIn(exception, url);
        }

        await DisconnectIfConnectedAsync();
        SignedIn(url, login);
        return true;
    }

    /// <summary>Signs in with the user name and password of a local account of the cloud (the password is used once).</summary>
    public static async Task ConnectAsync(string serverUrl, string userName, string password, string deviceName, CancellationToken cancellationToken = default)
    {
        var app = EnsureAllowed();
        var url = NormalizeServerUrl(serverUrl);
        var device = CheckDeviceName(deviceName);
        if (string.IsNullOrWhiteSpace(userName))
        {
            throw new BusinessException("Bitte geben Sie Ihren Benutzernamen an.", nameof(CloudSettings.UserName));
        }

        if (string.IsNullOrEmpty(password))
        {
            throw new BusinessException("Bitte geben Sie Ihr Passwort an.", "Password");
        }

        SetStatus(new CloudStatus(CloudState.Connecting, "Verbinde …"));
        DeviceLoginResponse login;
        try
        {
            using var client = CreateClient(url, null);
            login = await client.LoginAsync(new DeviceLoginRequest(userName.Trim(), password, device, AppInfo.PlatformName, AppInfo.Version, app), cancellationToken);
        }
        catch (Exception exception) when (IsCloudFailure(exception, cancellationToken))
        {
            SetStatus(InitialStatus());
            throw exception is CloudException { IsUnauthorized: true }
                ? new BusinessException("Benutzername oder Passwort stimmen nicht.", "Password")
                : ExplainSignIn(exception, url);
        }
        catch (OperationCanceledException)
        {
            SetStatus(InitialStatus());
            throw;
        }

        await DisconnectIfConnectedAsync();
        SignedIn(url, login);
    }

    /// <summary>
    /// At the start: the sign-in ThixLauncher left when it installed the app (<see cref="CloudHandoff"/>). While the app is not signed
    /// in, it fetches its own token with it – no browser. The hand-over is removed when it was used or had expired, and kept when
    /// the cloud did not answer (the next start tries again). Returns the account it signed in with, or null. Never throws.
    /// </summary>
    public static async Task<string?> ConnectFromLauncherAsync(CancellationToken cancellationToken = default)
    {
        if (AppInfo.Definition.CloudAppId is not { } app)
        {
            return null;
        }

        if (IsConnected || AccessPolicy.IsCloudDisabled)
        {
            CloudHandoff.Remove(app);
            return null;
        }

        if (CloudHandoff.Find(app) is not { } handoff)
        {
            return null;
        }

        string url;
        DeviceLoginResponse? login;
        try
        {
            url = NormalizeServerUrl(handoff.ServerUrl);
            using var client = CreateClient(url, null);
            login = await client.RedeemHandoffAsync(handoff, cancellationToken);
        }
        catch (BusinessException)
        {
            CloudHandoff.Remove(app);
            return null;
        }
        catch (Exception exception) when (exception is CloudException or HttpRequestException or TaskCanceledException or JsonException)
        {
            Log.Warning($"The sign-in of ThixLauncher could not be fetched ({exception.GetType().Name}); trying again at the next start");
            return null;
        }

        CloudHandoff.Remove(app);
        if (login is null || IsConnected)
        {
            Log.Info("The sign-in of ThixLauncher was used or had expired");
            return null;
        }

        SignedIn(url, login);
        Log.Info("Connected with the sign-in of ThixLauncher");
        return login.Account.DisplayName.Length > 0 ? login.Account.DisplayName : login.Account.UserName;
    }

    /// <summary>Signs this app out (the cloud forgets the device); the documents and their working copies stay on this computer.</summary>
    public static async Task DisconnectAsync()
    {
        var cloud = Settings;
        if (cloud.IsConnected && ReadToken(cloud) is { } token)
        {
            try
            {
                using var client = CreateClient(cloud.ServerUrl, token);
                await client.LogoutAsync(CancellationToken.None);
            }
            catch (Exception exception) when (IsCloudFailure(exception, CancellationToken.None))
            {
                Log.Warning($"Logout at the cloud failed ({exception.GetType().Name}); signing out locally");
            }
        }

        Forget(cloud);
        SetStatus(InitialStatus());
    }

    /// <summary>The account as the cloud knows it now (and the state: Online, Offline, SignedOut, Forbidden). Null when it cannot be asked.</summary>
    public static async Task<AccountInfo?> RefreshAccountAsync(CancellationToken cancellationToken = default)
    {
        if (!IsConnected || AccessPolicy.IsCloudDisabled)
        {
            return null;
        }

        try
        {
            using var client = CreateClient();
            var account = await client.GetAccountAsync(cancellationToken);
            ReportOnline();
            if (account.DisplayName != Settings.DisplayName || account.DeviceName != Settings.DeviceName)
            {
                SettingsService.UpdateCloud(current => current with { DisplayName = account.DisplayName, DeviceName = account.DeviceName });
            }

            return account;
        }
        catch (Exception exception) when (IsCloudFailure(exception, cancellationToken))
        {
            _ = Explain(exception);
            return null;
        }
    }

    /// <summary>The address as it is stored: http(s), "https://" added when missing, no trailing slash.</summary>
    public static string NormalizeServerUrl(string? serverUrl)
    {
        if (string.IsNullOrWhiteSpace(serverUrl))
        {
            throw new BusinessException("Bitte geben Sie die Adresse der ThixCloud an.", nameof(CloudSettings.ServerUrl));
        }

        try
        {
            return MatCloudClient.NormalizeServerUrl(serverUrl);
        }
        catch (UriFormatException)
        {
            throw new BusinessException("Das ist keine gültige Adresse (z. B. https://cloud.example.com).", nameof(CloudSettings.ServerUrl));
        }
    }

    // ===== For DriveService =====

    /// <summary>A client with the device token; refused (policy, not signed in) as a <see cref="BusinessException"/>.</summary>
    internal static MatCloudClient CreateClient()
    {
        EnsureAllowed();
        var cloud = Settings;
        if (!cloud.IsConnected || ReadToken(cloud) is not { } token)
        {
            throw new BusinessException($"{AppInfo.ProductName} ist nicht bei ThixCloud angemeldet. Bitte melden Sie sich an (Datei › Einstellungen › Cloud).");
        }

        return CreateClient(cloud.ServerUrl, token);
    }

    /// <summary>The cloud answered: the state is Online again.</summary>
    internal static void ReportOnline()
    {
        if (Status.State != CloudState.Online && IsConnected)
        {
            SetStatus(new CloudStatus(CloudState.Online, OnlineText()));
        }
    }

    /// <summary>
    /// A failure of the cloud in the words of the person, and what it means for the state: 401 signs out, 403 is "switched off by
    /// the administration", no answer is Offline. 404, 409, 413, 507 and a bad value keep the state.
    /// </summary>
    internal static BusinessException Explain(Exception exception)
    {
        switch (exception)
        {
            case BusinessException business:
                return business;
            case CloudException { IsUnauthorized: true }:
                SignedOutByCloud();
                return new BusinessException("Die ThixCloud hat die Anmeldung beendet (das Gerät wurde dort entfernt). Bitte melden Sie sich erneut an.");
            case CloudException { IsForbidden: true }:
                var forbidden = AppInfo.Definition.IsWorkspace
                    ? $"{AppInfo.ProductName} ist von der Verwaltung der ThixCloud abgeschaltet."
                    : $"ThixDrive ist für {AppInfo.ProductName} von der Verwaltung der ThixCloud abgeschaltet.";
                SetStatus(new CloudStatus(CloudState.Forbidden, forbidden));
                return new BusinessException(forbidden);
            case CloudException { StatusCode: HttpStatusCode.NotFound }:
                ReportOnline();
                return new BusinessException("Diese Datei oder diesen Ordner gibt es in ThixDrive nicht mehr (gelöscht oder im Papierkorb).");
            case CloudException { StatusCode: HttpStatusCode.RequestEntityTooLarge }:
                ReportOnline();
                return new BusinessException("Die Datei ist zu groß für ThixDrive.");
            case CloudException { StatusCode: HttpStatusCode.InsufficientStorage }:
                ReportOnline();
                return new BusinessException("Ihr Speicherplatz in ThixDrive ist voll. Leeren Sie den Papierkorb oder löschen Sie Dateien, die Sie nicht mehr brauchen.");
            case CloudException { StatusCode: HttpStatusCode.BadRequest } cloud:
                ReportOnline();
                return new BusinessException(cloud.Message, cloud.Field?.ToLowerInvariant() == "name" ? "FileName" : cloud.Field);
            case CloudException { StatusCode: HttpStatusCode.TooManyRequests } cloud:
                return new BusinessException(cloud.Message);
            case CloudException { StatusCode: >= HttpStatusCode.InternalServerError }:
                SetOffline();
                return new BusinessException("Die ThixCloud hat einen Fehler gemeldet. Bitte versuchen Sie es später noch einmal.");
            case CloudException cloud:
                return new BusinessException(cloud.Message, cloud.Field);
            default:
                SetOffline();
                return new BusinessException("Die ThixCloud ist nicht erreichbar. Prüfen Sie die Internetverbindung und versuchen Sie es noch einmal.");
        }
    }

    /// <summary>The cloud answered with an error, could not be reached or took too long (and nobody cancelled on purpose).</summary>
    internal static bool IsCloudFailure(Exception exception, CancellationToken cancellationToken) =>
        exception is CloudException or HttpRequestException or JsonException or UriFormatException or IOException
        || (exception is OperationCanceledException && !cancellationToken.IsCancellationRequested);

    /// <summary>The failure leaves a save waiting (no network, a server error, signed out, switched off) rather than refusing it for good.</summary>
    internal static bool IsTransient(Exception exception) =>
        exception is HttpRequestException or IOException or OperationCanceledException or JsonException
        || exception is CloudException { StatusCode: HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden or HttpStatusCode.TooManyRequests or (>= HttpStatusCode.InternalServerError and not HttpStatusCode.InsufficientStorage) };

    /// <summary>Snapshots show a state; null forgets it.</summary>
    internal static void ShowStatusForSnapshot(CloudStatus? status) => SetStatus(status ?? InitialStatus());

    /// <summary>Tests start without a remembered state.</summary>
    internal static void ResetForTests()
    {
        _status = null;
        HandlerForTests = null;
    }

    // ===== Helpers =====

    private static string EnsureAllowed()
    {
        var app = AppInfo.Definition.CloudAppId ?? throw new BusinessException($"{AppInfo.ProductName} verbindet sich nicht mit der ThixCloud.");
        AccessPolicy.EnsureCloudAllowed();
        return app;
    }

    private static CloudStatus InitialStatus()
    {
        if (!AppInfo.Definition.HasCloud || AccessPolicy.IsCloudDisabled)
        {
            return new CloudStatus(CloudState.Disabled, "Durch Richtlinie deaktiviert");
        }

        return Settings.IsConnected ? new CloudStatus(CloudState.Online, OnlineText()) : new CloudStatus(CloudState.Off, NotConnected);
    }

    private static string OnlineText() => $"Angemeldet als {Settings.AccountText}";

    private static void SetOffline()
    {
        if (IsConnected)
        {
            SetStatus(new CloudStatus(CloudState.Offline, "Offline – die ThixCloud ist nicht erreichbar"));
        }
    }

    private static void SignedIn(string url, DeviceLoginResponse login)
    {
        var cloud = new CloudSettings
        {
            ServerUrl = url,
            UserName = login.Account.UserName,
            DisplayName = login.Account.DisplayName,
            DeviceName = login.Account.DeviceName,
            DeviceId = login.DeviceId,
        };
        var protectedToken = TokenStore.Save(AccountKey(cloud), login.Token);
        SettingsService.UpdateCloud(_ => cloud with { ProtectedToken = protectedToken });
        Log.Info($"Connected to ThixCloud as device {login.DeviceId}");
        SetStatus(new CloudStatus(CloudState.Online, OnlineText()));
    }

    private static void SignedOutByCloud()
    {
        var cloud = Settings;
        if (!cloud.IsConnected)
        {
            return;
        }

        Log.Warning("ThixCloud ended the sign-in (401)");
        Forget(cloud);
        SetStatus(new CloudStatus(CloudState.SignedOut, "Abgemeldet – die ThixCloud hat die Anmeldung beendet. Bitte melden Sie sich erneut an."));
    }

    private static async Task DisconnectIfConnectedAsync()
    {
        if (IsConnected)
        {
            await DisconnectAsync();
        }
    }

    private static string CheckDeviceName(string? deviceName)
    {
        var name = string.IsNullOrWhiteSpace(deviceName) ? AppInfo.MachineName : deviceName.Trim();
        if (name.Length > CloudContract.MaxDeviceNameLength)
        {
            throw new BusinessException($"Der Gerätename ist zu lang (höchstens {CloudContract.MaxDeviceNameLength} Zeichen).", nameof(CloudSettings.DeviceName));
        }

        return name;
    }

    /// <summary>The message of the cloud at its field (user name, password, device name), or "not reachable" at the address.</summary>
    private static BusinessException ExplainSignIn(Exception exception, string url)
    {
        if (exception is not CloudException cloud)
        {
            return new BusinessException($"Die ThixCloud ist unter {url} nicht erreichbar.", nameof(CloudSettings.ServerUrl));
        }

        var field = cloud.Field?.ToLowerInvariant() switch
        {
            "username" => nameof(CloudSettings.UserName),
            "password" => "Password",
            "devicename" => nameof(CloudSettings.DeviceName),
            _ => cloud.StatusCode == HttpStatusCode.NotFound ? nameof(CloudSettings.ServerUrl) : null,
        };
        return new BusinessException(cloud.Message, field);
    }

    private static MatCloudClient CreateClient(string serverUrl, string? token) => new(serverUrl, token, UserAgent, HandlerForTests?.Invoke());

    /// <summary>Forgets the token; the address, the user name and the device name stay for the next sign-in.</summary>
    private static void Forget(CloudSettings cloud)
    {
        if (cloud.ServerUrl.Length > 0)
        {
            TokenStore.Delete(AccountKey(cloud));
        }

        SettingsService.UpdateCloud(current => new CloudSettings { ServerUrl = current.ServerUrl, UserName = current.UserName, DeviceName = current.DeviceName });
    }

    private static string? ReadToken(CloudSettings cloud) => cloud.IsConnected ? TokenStore.Read(AccountKey(cloud), cloud.ProtectedToken) : null;

    /// <summary>cloud-user@server: the Keychain entry of the app (the service is the app's bundle id, so every app has its own).</summary>
    private static string AccountKey(CloudSettings cloud) => $"cloud-{cloud.UserName}@{cloud.ServerUrl}";

    private static void SetStatus(CloudStatus status)
    {
        if (status == _status)
        {
            return;
        }

        _status = status;
        StatusChanged?.Invoke(null, status);
    }
}
