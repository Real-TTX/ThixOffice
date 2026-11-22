using System.Net;
using System.Security.Cryptography;
using System.Text;
using ThixMail.Core.Accounts;

namespace ThixMail.Core.Tests;

/// <summary>
/// A small OAuth 2.0 provider for the tests: its "browser" (<see cref="BrowseAsync"/>, handed to the sign-in as the browser) checks the authorization
/// request like Google/Microsoft do and answers the loopback redirect for real over HTTP (consent given, refused, or blocked by the organisation); its
/// token and revocation endpoints are an <see cref="HttpMessageHandler"/> that checks the PKCE verifier, issues, refreshes and revokes tokens.
/// Issued access tokens are handed to <see cref="Issued"/> (the tests let the SampleMailServer accept them through XOAUTH2).
/// </summary>
internal sealed class FakeOAuthServer : HttpMessageHandler
{
    public const string ClientId = "thixmail-test-client";
    public const string Endpoint = "https://oauth.test";

    private readonly object _gate = new();
    private readonly Dictionary<string, (string Challenge, string Redirect)> _codes = new(StringComparer.Ordinal);
    private readonly HashSet<string> _refreshTokens = new(StringComparer.Ordinal);
    private int _counter;

    /// <summary>How the person answers the consent screen.</summary>
    public FakeConsent Consent { get; set; } = FakeConsent.Grant;

    /// <summary>Lifetime of issued access tokens in seconds.</summary>
    public int AccessLifetime { get; set; } = 3600;

    /// <summary>The provider of a test (id "google", the fake endpoints, the loopback on 127.0.0.1).</summary>
    public static OAuthProvider Provider(string id = OAuthProviders.GoogleId) =>
        (id == OAuthProviders.MicrosoftId ? OAuthProviders.Microsoft : OAuthProviders.Google) with
        {
            AuthorizationEndpoint = $"{Endpoint}/authorize",
            TokenEndpoint = $"{Endpoint}/token",
            RevocationEndpoint = $"{Endpoint}/revoke",
            RedirectHost = "127.0.0.1",
        };

    /// <summary>Plugs this provider in for "google" and "microsoft" (client id set); <see cref="Unplug"/> restores the real lookups.</summary>
    public FakeOAuthServer Plug()
    {
        OAuthProviders.FindForTests = id => id is OAuthProviders.GoogleId or OAuthProviders.MicrosoftId ? Provider(id) : null;
        OAuthClients.ForTests = _ => new OAuthClient(ClientId, null);
        OAuthService.HandlerForTests = () => new Forwarder(this);
        return this;
    }

    public static void Unplug()
    {
        OAuthProviders.FindForTests = null;
        OAuthClients.ForTests = null;
        OAuthService.HandlerForTests = null;
    }

    public Action<string>? Issued { get; set; }

    /// <summary>The query of the last authorization request.</summary>
    public IReadOnlyDictionary<string, string> LastAuthorization { get; private set; } = new Dictionary<string, string>();

    public int Refreshes { get; private set; }

    public List<string> Revoked { get; } = [];

    /// <summary>Every refresh token stops working (expired, revoked by the person at the provider).</summary>
    public void ExpireRefreshTokens()
    {
        lock (_gate)
        {
            _refreshTokens.Clear();
        }
    }

    /// <summary>The browser: shows the "consent screen" and lets it answer the redirect.</summary>
    public async Task BrowseAsync(Uri url)
    {
        var query = OAuthService.ParseQuery(url.Query);
        LastAuthorization = query;
        Assert.Equal($"{Endpoint}/authorize", url.GetLeftPart(UriPartial.Path));
        Assert.Equal(ClientId, query["client_id"]);
        Assert.Equal("code", query["response_type"]);
        Assert.Equal("S256", query["code_challenge_method"]);
        var redirect = query["redirect_uri"];
        Assert.StartsWith("http://127.0.0.1:", redirect, StringComparison.Ordinal);
        var state = Uri.EscapeDataString(query["state"]);
        string target;
        switch (Consent)
        {
            case FakeConsent.Refuse:
                target = $"{redirect}?error=access_denied&error_description=The+user+denied+access&state={state}";
                break;
            case FakeConsent.AdminRequired:
                target = $"{redirect}?error=access_denied&error_description={Uri.EscapeDataString("AADSTS90094: Admin consent is required for the permissions requested.")}&state={state}";
                break;
            default:
                var code = "code-" + Interlocked.Increment(ref _counter);
                lock (_gate)
                {
                    _codes[code] = (query["code_challenge"], redirect);
                }

                target = $"{redirect}?code={code}&state={state}&scope={Uri.EscapeDataString(query["scope"])}";
                break;
        }

        using var browser = new HttpClient();
        var page = await browser.GetStringAsync(target);
        Assert.Contains("ThixMail", page, StringComparison.Ordinal);
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var form = OAuthService.ParseQuery(await request.Content!.ReadAsStringAsync(cancellationToken));
        var path = request.RequestUri!.AbsolutePath;
        if (path == "/revoke")
        {
            lock (_gate)
            {
                Revoked.Add(form["token"]);
                _refreshTokens.Remove(form["token"]);
            }

            return new HttpResponseMessage(HttpStatusCode.OK);
        }

        if (form.GetValueOrDefault("client_id") != ClientId)
        {
            return Error("invalid_client");
        }

        lock (_gate)
        {
            switch (form.GetValueOrDefault("grant_type"))
            {
                case "authorization_code":
                    if (!_codes.Remove(form.GetValueOrDefault("code") ?? "", out var issued) || issued.Redirect != form.GetValueOrDefault("redirect_uri"))
                    {
                        return Error("invalid_grant");
                    }

                    var challenge = OAuthService.Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(form.GetValueOrDefault("code_verifier") ?? "")));
                    if (challenge != issued.Challenge)
                    {
                        return Error("invalid_grant", "PKCE verification failed");
                    }

                    return Tokens(withRefresh: true);
                case "refresh_token":
                    if (!_refreshTokens.Contains(form.GetValueOrDefault("refresh_token") ?? ""))
                    {
                        return Error("invalid_grant", "Token has been expired or revoked.");
                    }

                    Refreshes++;
                    return Tokens(withRefresh: false);
                default:
                    return Error("unsupported_grant_type");
            }
        }
    }

    private HttpResponseMessage Tokens(bool withRefresh)
    {
        var number = Interlocked.Increment(ref _counter);
        var access = $"access-{number}";
        var refresh = $"refresh-{number}";
        if (withRefresh)
        {
            _refreshTokens.Add(refresh);
        }

        Issued?.Invoke(access);
        var json = $"{{\"access_token\":\"{access}\",\"expires_in\":{AccessLifetime},\"token_type\":\"Bearer\"{(withRefresh ? $",\"refresh_token\":\"{refresh}\"" : "")}}}";
        return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
    }

    private static HttpResponseMessage Error(string error, string description = "") =>
        new(HttpStatusCode.BadRequest) { Content = new StringContent($"{{\"error\":\"{error}\",\"error_description\":\"{description}\"}}", Encoding.UTF8, "application/json") };

    /// <summary>A handler per HttpClient (the service disposes it) that forwards to the one server.</summary>
    private sealed class Forwarder(FakeOAuthServer server) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            server.SendAsync(request, cancellationToken);
    }
}

internal enum FakeConsent
{
    Grant,
    Refuse,
    AdminRequired,
}
