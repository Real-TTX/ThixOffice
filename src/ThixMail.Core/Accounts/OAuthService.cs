using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ThixOffice.Core.Infrastructure;

namespace ThixMail.Core.Accounts;

/// <summary>The tokens of an OAuth sign-in (kept only in the TokenStore, through <see cref="AccountService.SetOAuthTokens"/>).</summary>
public sealed record OAuthTokens(string AccessToken, string? RefreshToken, DateTimeOffset ExpiresAt)
{
    /// <summary>Renewed this long before it ends (a connection opened now must not fail half way through its sign-in).</summary>
    public static TimeSpan Margin { get; } = TimeSpan.FromMinutes(2);

    public bool IsFresh(DateTimeOffset now) => ExpiresAt - now > Margin;
}

/// <summary>What went wrong in an OAuth sign-in (the account page and the sync decide by it).</summary>
public enum OAuthErrorKind
{
    /// <summary>ThixMail has no client id for the provider (yet).</summary>
    NotConfigured,

    /// <summary>The person did not allow the access.</summary>
    ConsentRefused,

    /// <summary>The organisation must allow ThixMail first (Microsoft Entra admin consent, a Google Workspace policy).</summary>
    AdminApprovalRequired,

    /// <summary>The refresh token expired or was revoked: sign in again (the mail stays).</summary>
    SignInExpired,

    /// <summary>The provider refused ThixMail's registration (client id unknown, redirect not registered).</summary>
    ClientRejected,

    /// <summary>The provider could not be reached.</summary>
    Network,

    /// <summary>The browser sign-in was not finished (closed, timed out, cancelled).</summary>
    Cancelled,

    Failed,
}

/// <summary>An OAuth failure in German words, with its kind.</summary>
public sealed class OAuthException(OAuthErrorKind kind, string message, Exception? inner = null) : Exception(message, inner)
{
    public OAuthErrorKind Kind { get; } = kind;
}

/// <summary>
/// OAuth 2.0 for IMAP/SMTP (RFC 6749 authorization code flow with PKCE, RFC 7636, for a native app, RFC 8252): the system browser opens the
/// provider's sign-in, the answer comes back to a loopback redirect (a listener on 127.0.0.1 – and ::1 for "localhost" – on a random port, only for
/// this one sign-in), the code is exchanged for tokens; tokens are refreshed (<see cref="RefreshAsync"/>) and revoked where the provider can
/// (<see cref="RevokeAsync"/>). Nothing here stores anything: the caller keeps the tokens in the TokenStore. Errors become <see cref="OAuthException"/>
/// with German messages (consent refused, admin approval, expired sign-in, …).
/// </summary>
public static class OAuthService
{
    /// <summary>How long the browser sign-in may take.</summary>
    public static TimeSpan SignInTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>Tests answer the token and revocation endpoints themselves (a fake provider).</summary>
    internal static Func<HttpMessageHandler>? HandlerForTests { get; set; }

    private static readonly HttpClient SharedClient = new(new SocketsHttpHandler { UseCookies = false }) { Timeout = TimeSpan.FromSeconds(30) };

    /// <summary>
    /// The whole browser sign-in: opens <paramref name="openBrowser"/> with the provider's page (the address as login hint), waits for the redirect,
    /// checks the state, exchanges the code (with the PKCE verifier) – the tokens.
    /// </summary>
    public static async Task<OAuthTokens> AuthorizeAsync(OAuthProvider provider, string loginHint, Func<Uri, Task> openBrowser, CancellationToken cancellationToken)
    {
        var client = OAuthClients.For(provider.Id) ?? throw NotConfigured(provider);
        using var listener = LoopbackListener.Start(provider.RedirectHost);
        var redirect = $"http://{provider.RedirectHost}:{listener.Port.ToString(CultureInfo.InvariantCulture)}/";
        var verifier = Base64Url(RandomNumberGenerator.GetBytes(32));
        var challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        var state = Base64Url(RandomNumberGenerator.GetBytes(16));
        var query = new List<KeyValuePair<string, string>>
        {
            new("response_type", "code"),
            new("client_id", client.ClientId),
            new("redirect_uri", redirect),
            new("scope", provider.Scope),
            new("code_challenge", challenge),
            new("code_challenge_method", "S256"),
            new("state", state),
        };
        if (loginHint.Length > 0)
        {
            query.Add(new("login_hint", loginHint));
        }

        query.AddRange(provider.ExtraParameters);
        var url = new Uri(provider.AuthorizationEndpoint + (provider.AuthorizationEndpoint.Contains('?', StringComparison.Ordinal) ? "&" : "?") + Encode(query));

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(SignInTimeout);
        var waiting = listener.WaitForAnswerAsync(timeout.Token);
        await openBrowser(url);
        IReadOnlyDictionary<string, string> answer;
        try
        {
            answer = await waiting;
        }
        catch (OperationCanceledException exception)
        {
            throw new OAuthException(OAuthErrorKind.Cancelled, cancellationToken.IsCancellationRequested
                ? "Die Anmeldung im Browser wurde abgebrochen."
                : "Die Anmeldung im Browser wurde nicht rechtzeitig abgeschlossen. Bitte versuchen Sie es noch einmal.", exception);
        }

        if (answer.GetValueOrDefault("state") != state)
        {
            throw new OAuthException(OAuthErrorKind.Failed, "Die Antwort aus dem Browser gehört nicht zu dieser Anmeldung. Bitte versuchen Sie es noch einmal.");
        }

        if (answer.TryGetValue("error", out var error))
        {
            throw Explain(provider, error, answer.GetValueOrDefault("error_description") ?? "");
        }

        if (answer.GetValueOrDefault("code") is not { Length: > 0 } code)
        {
            throw new OAuthException(OAuthErrorKind.Failed, $"{provider.Name} hat keine Anmeldung zurückgegeben.");
        }

        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "authorization_code"),
            new("code", code),
            new("redirect_uri", redirect),
            new("client_id", client.ClientId),
            new("code_verifier", verifier),
        };
        if (client.ClientSecret is { } secret)
        {
            form.Add(new("client_secret", secret));
        }

        var tokens = await PostTokenAsync(provider, form, null, cancellationToken);
        return tokens.RefreshToken is null
            ? throw new OAuthException(OAuthErrorKind.Failed, $"{provider.Name} hat keine dauerhafte Anmeldung erteilt (kein Aktualisierungstoken). Bitte versuchen Sie es noch einmal.")
            : tokens;
    }

    /// <summary>A new access token from the refresh token (the provider may hand out a new refresh token; otherwise the old one stays).</summary>
    public static async Task<OAuthTokens> RefreshAsync(OAuthProvider provider, string refreshToken, CancellationToken cancellationToken)
    {
        var client = OAuthClients.For(provider.Id) ?? throw NotConfigured(provider);
        var form = new List<KeyValuePair<string, string>>
        {
            new("grant_type", "refresh_token"),
            new("refresh_token", refreshToken),
            new("client_id", client.ClientId),
        };
        if (provider.Id == OAuthProviders.MicrosoftId)
        {
            // Microsoft wants the scopes again on a refresh (Google keeps those of the grant)
            form.Add(new("scope", provider.Scope));
        }

        if (client.ClientSecret is { } secret)
        {
            form.Add(new("client_secret", secret));
        }

        return await PostTokenAsync(provider, form, refreshToken, cancellationToken);
    }

    /// <summary>
    /// Revokes a token where the provider offers it (Google). Best effort: an unreachable provider is logged, never an error – the account goes anyway.
    /// True when the provider confirmed it.
    /// </summary>
    public static async Task<bool> RevokeAsync(OAuthProvider provider, string token, CancellationToken cancellationToken)
    {
        if (provider.RevocationEndpoint is not { } endpoint)
        {
            return false;
        }

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, endpoint) { Content = new FormUrlEncodedContent([new("token", token)]) };
            using var response = await Send(request, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException)
        {
            Log.Warning($"Revoking an OAuth token failed: {exception.GetType().Name}");
            return false;
        }
    }

    private static async Task<OAuthTokens> PostTokenAsync(OAuthProvider provider, List<KeyValuePair<string, string>> form, string? previousRefresh,
        CancellationToken cancellationToken)
    {
        string body;
        HttpStatusCode status;
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, provider.TokenEndpoint) { Content = new FormUrlEncodedContent(form) };
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await Send(request, cancellationToken);
            status = response.StatusCode;
            body = await response.Content.ReadAsStringAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is HttpRequestException or IOException || (exception is TaskCanceledException && !cancellationToken.IsCancellationRequested))
        {
            throw new OAuthException(OAuthErrorKind.Network, $"{provider.Name} ist nicht erreichbar. Prüfen Sie die Internetverbindung.", exception);
        }

        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(body);
            root = document.RootElement.Clone();
        }
        catch (JsonException exception)
        {
            throw new OAuthException((int)status >= 500 ? OAuthErrorKind.Network : OAuthErrorKind.Failed,
                $"{provider.Name} hat unerwartet geantwortet (HTTP {(int)status}).", exception);
        }

        if (root.ValueKind != JsonValueKind.Object)
        {
            throw new OAuthException(OAuthErrorKind.Failed, $"{provider.Name} hat unerwartet geantwortet (HTTP {(int)status}).");
        }

        if (Text(root, "error") is { } error)
        {
            throw Explain(provider, error, Text(root, "error_description") ?? "");
        }

        if (Text(root, "access_token") is not { } access || (int)status >= 300)
        {
            throw new OAuthException((int)status >= 500 ? OAuthErrorKind.Network : OAuthErrorKind.Failed, $"{provider.Name} hat kein Zugriffstoken ausgegeben (HTTP {(int)status}).");
        }

        var seconds = root.TryGetProperty("expires_in", out var expires) && expires.ValueKind == JsonValueKind.Number ? expires.GetInt32()
            : int.TryParse(Text(root, "expires_in"), NumberStyles.None, CultureInfo.InvariantCulture, out var parsed) ? parsed : 3600;
        return new OAuthTokens(access, Text(root, "refresh_token") ?? previousRefresh, DateTimeOffset.UtcNow.AddSeconds(seconds));
    }

    private static Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (HandlerForTests is { } handler)
        {
            var client = new HttpClient(handler(), disposeHandler: true);
            return SendAndDispose(client, request, cancellationToken);
        }

        return SharedClient.SendAsync(request, cancellationToken);
    }

    private static async Task<HttpResponseMessage> SendAndDispose(HttpClient client, HttpRequestMessage request, CancellationToken cancellationToken)
    {
        using (client)
        {
            var response = await client.SendAsync(request, cancellationToken);
            await response.Content.LoadIntoBufferAsync(cancellationToken);
            return response;
        }
    }

    private static string? Text(JsonElement root, string name) =>
        root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String && value.GetString() is { Length: > 0 } text ? text : null;

    private static OAuthException NotConfigured(OAuthProvider provider) =>
        new(OAuthErrorKind.NotConfigured, $"Die Anmeldung im Browser ist für {provider.Name} in dieser Version von ThixMail noch nicht freigeschaltet.");

    /// <summary>An OAuth error code (RFC 6749 + what Google and Microsoft add) in German.</summary>
    public static OAuthException Explain(OAuthProvider provider, string error, string description)
    {
        var admin = description.Contains("AADSTS65001", StringComparison.Ordinal) && description.Contains("admin", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("AADSTS90094", StringComparison.Ordinal) || description.Contains("AADSTS90095", StringComparison.Ordinal)
                    || description.Contains("AADSTS90099", StringComparison.Ordinal) || error == "admin_policy_enforced"
                    || description.Contains("admin_policy_enforced", StringComparison.OrdinalIgnoreCase)
                    || description.Contains("administrator", StringComparison.OrdinalIgnoreCase) && description.Contains("consent", StringComparison.OrdinalIgnoreCase);
        if (admin)
        {
            return new OAuthException(OAuthErrorKind.AdminApprovalRequired, provider.Id == OAuthProviders.MicrosoftId
                ? "Ihre Organisation muss ThixMail erst zulassen. Bitten Sie Ihre IT-Verwaltung, ThixMail im Microsoft Entra Admin Center die Administratorzustimmung zu erteilen; danach können Sie sich anmelden."
                : $"Die Verwaltung Ihres {provider.Name}-Kontos erlaubt ThixMail den Zugriff nicht. Bitten Sie Ihre IT-Verwaltung, ThixMail zuzulassen.");
        }

        var expired = description.Contains("AADSTS70008", StringComparison.Ordinal) || description.Contains("AADSTS700082", StringComparison.Ordinal)
                      || description.Contains("AADSTS50173", StringComparison.Ordinal) || description.Contains("AADSTS700084", StringComparison.Ordinal);
        return error switch
        {
            "access_denied" or "consent_required" or "interaction_required" when !expired => new OAuthException(OAuthErrorKind.ConsentRefused,
                $"Sie haben ThixMail den Zugriff auf Ihr Postfach bei {provider.Name} nicht erlaubt. Ohne diese Zustimmung kann ThixMail keine Mails abrufen oder senden."),
            "invalid_grant" or "interaction_required" or "login_required" => new OAuthException(OAuthErrorKind.SignInExpired,
                $"Die Anmeldung bei {provider.Name} ist abgelaufen oder wurde widerrufen. Bitte melden Sie sich erneut an – Ihre Nachrichten bleiben erhalten."),
            "invalid_client" or "unauthorized_client" or "invalid_scope" or "redirect_uri_mismatch" => new OAuthException(OAuthErrorKind.ClientRejected,
                $"{provider.Name} lässt die Anmeldung von ThixMail nicht zu (die Registrierung von ThixMail passt nicht: {error})."),
            "temporarily_unavailable" or "server_error" => new OAuthException(OAuthErrorKind.Network,
                $"{provider.Name} kann die Anmeldung gerade nicht bearbeiten. Bitte versuchen Sie es später noch einmal."),
            _ => new OAuthException(OAuthErrorKind.Failed, $"Die Anmeldung bei {provider.Name} ist fehlgeschlagen ({error})."),
        };
    }

    internal static string Base64Url(byte[] bytes) => Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static string Encode(IEnumerable<KeyValuePair<string, string>> pairs) =>
        string.Join('&', pairs.Select(p => $"{Uri.EscapeDataString(p.Key)}={Uri.EscapeDataString(p.Value)}"));

    /// <summary>Splits a query ("a=1&amp;b=x%20y") into its values (later duplicates ignored).</summary>
    internal static Dictionary<string, string> ParseQuery(string query)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var part in query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var equals = part.IndexOf('=');
            var key = Uri.UnescapeDataString((equals < 0 ? part : part[..equals]).Replace('+', ' '));
            var value = equals < 0 ? "" : Uri.UnescapeDataString(part[(equals + 1)..].Replace('+', ' '));
            result.TryAdd(key, value);
        }

        return result;
    }

    /// <summary>
    /// The loopback redirect of one sign-in: listens on 127.0.0.1 (and ::1 when the redirect names "localhost") on a port the system picks, answers
    /// the browser's request with a short German page and hands over the query. Other requests (favicon) get 404. Closed after the sign-in.
    /// </summary>
    private sealed class LoopbackListener : IDisposable
    {
        private readonly TcpListener _v4;
        private readonly TcpListener? _v6;

        private LoopbackListener(TcpListener v4, TcpListener? v6)
        {
            _v4 = v4;
            _v6 = v6;
        }

        public int Port => ((IPEndPoint)_v4.LocalEndpoint).Port;

        public static LoopbackListener Start(string host)
        {
            var v4 = new TcpListener(IPAddress.Loopback, 0);
            v4.Start();
            TcpListener? v6 = null;
            if (host == "localhost" && Socket.OSSupportsIPv6)
            {
                try
                {
                    v6 = new TcpListener(IPAddress.IPv6Loopback, ((IPEndPoint)v4.LocalEndpoint).Port);
                    v6.Start();
                }
                catch (SocketException)
                {
                    v6 = null;
                }
            }

            return new LoopbackListener(v4, v6);
        }

        public async Task<IReadOnlyDictionary<string, string>> WaitForAnswerAsync(CancellationToken cancellationToken)
        {
            using var registration = cancellationToken.Register(Dispose);
            var accepts = new List<Task<TcpClient>> { _v4.AcceptTcpClientAsync(cancellationToken).AsTask() };
            if (_v6 is not null)
            {
                accepts.Add(_v6.AcceptTcpClientAsync(cancellationToken).AsTask());
            }

            while (true)
            {
                Task<TcpClient> done;
                try
                {
                    done = await Task.WhenAny(accepts);
                    cancellationToken.ThrowIfCancellationRequested();
                    var index = accepts.IndexOf(done);
                    using var client = await done;
                    accepts[index] = (index == 0 ? _v4 : _v6!).AcceptTcpClientAsync(cancellationToken).AsTask();
                    if (await AnswerAsync(client, cancellationToken) is { } answer)
                    {
                        return answer;
                    }
                }
                catch (Exception exception) when (exception is SocketException or ObjectDisposedException or IOException or InvalidOperationException)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    throw new OAuthException(OAuthErrorKind.Failed, "Die Antwort aus dem Browser kam nicht an.", exception);
                }
            }
        }

        private static async Task<Dictionary<string, string>?> AnswerAsync(TcpClient client, CancellationToken cancellationToken)
        {
            var stream = client.GetStream();
            var buffer = new byte[16 * 1024];
            var length = 0;
            using var readTimeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            readTimeout.CancelAfter(TimeSpan.FromSeconds(10));
            while (length < buffer.Length)
            {
                var read = await stream.ReadAsync(buffer.AsMemory(length), readTimeout.Token);
                if (read == 0)
                {
                    break;
                }

                length += read;
                if (Encoding.ASCII.GetString(buffer, 0, length).Contains("\r\n\r\n", StringComparison.Ordinal))
                {
                    break;
                }
            }

            var head = Encoding.ASCII.GetString(buffer, 0, length);
            var firstLine = head.Split("\r\n", 2)[0].Split(' ');
            var target = firstLine.Length >= 2 ? firstLine[1] : "";
            var question = target.IndexOf('?');
            var path = question < 0 ? target : target[..question];
            var query = question < 0 ? new Dictionary<string, string>() : ParseQuery(target[(question + 1)..]);
            var isAnswer = firstLine[0] == "GET" && path == "/" && (query.ContainsKey("code") || query.ContainsKey("error"));
            var page = isAnswer
                ? query.ContainsKey("error")
                    ? Page("Anmeldung nicht abgeschlossen", "Die Anmeldung wurde nicht abgeschlossen. Sie können dieses Fenster schließen; ThixMail erklärt, wie es weitergeht.")
                    : Page("Angemeldet", "Sie sind angemeldet. Sie können dieses Fenster schließen und zu ThixMail zurückkehren.")
                : "";
            var bytes = Encoding.UTF8.GetBytes(page);
            var response = (isAnswer ? "HTTP/1.1 200 OK\r\nContent-Type: text/html; charset=utf-8\r\n" : "HTTP/1.1 404 Not Found\r\n")
                           + $"Content-Length: {bytes.Length.ToString(CultureInfo.InvariantCulture)}\r\nCache-Control: no-store\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response), cancellationToken);
            await stream.WriteAsync(bytes, cancellationToken);
            await stream.FlushAsync(cancellationToken);
            return isAnswer ? query : null;
        }

        private static string Page(string title, string text) =>
            $"<!DOCTYPE html><html lang=\"de\"><head><meta charset=\"utf-8\"><title>ThixMail – {title}</title></head>" +
            $"<body style=\"font-family:system-ui,sans-serif;margin:48px\"><h1 style=\"font-weight:600\">ThixMail</h1><p>{text}</p></body></html>";

        public void Dispose()
        {
            _v4.Stop();
            _v6?.Stop();
        }
    }
}
