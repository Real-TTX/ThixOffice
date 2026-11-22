using ThixOffice.Core.Business;

namespace ThixMail.Core.Accounts;

/// <summary>
/// A provider whose IMAP/SMTP take OAuth 2.0 (SASL XOAUTH2): its endpoints, the scopes the mail servers need, the host name of the loopback redirect
/// the registration names, and the extra parameters of its authorization request. The client id is NOT here – it comes from <see cref="OAuthClients"/>.
/// </summary>
public sealed record OAuthProvider(
    string Id,
    string Name,
    string AuthorizationEndpoint,
    string TokenEndpoint,
    string? RevocationEndpoint,
    IReadOnlyList<string> Scopes,
    string RedirectHost,
    IReadOnlyList<KeyValuePair<string, string>> ExtraParameters)
{
    public string Scope => string.Join(' ', Scopes);
}

/// <summary>The OAuth providers ThixMail knows (Google for Gmail/Workspace, Microsoft for Outlook.com and Microsoft 365).</summary>
public static class OAuthProviders
{
    public const string GoogleId = "google";

    public const string MicrosoftId = "microsoft";

    /// <summary>Google: a "Desktop app" client; the loopback redirect on 127.0.0.1 with any port; offline access gives the refresh token.</summary>
    public static OAuthProvider Google { get; } = new(GoogleId, "Google",
        "https://accounts.google.com/o/oauth2/v2/auth",
        "https://oauth2.googleapis.com/token",
        "https://oauth2.googleapis.com/revoke",
        ["https://mail.google.com/"],
        "127.0.0.1",
        [new("access_type", "offline"), new("prompt", "consent")]);

    /// <summary>
    /// Microsoft identity platform (common: personal and work/school accounts): a public client ("Mobile and desktop applications") with the redirect
    /// http://localhost (Microsoft ignores the port of a localhost redirect); offline_access gives the refresh token. Microsoft has no endpoint to
    /// revoke a refresh token of a public client: removing the account forgets the tokens here.
    /// </summary>
    public static OAuthProvider Microsoft { get; } = new(MicrosoftId, "Microsoft",
        "https://login.microsoftonline.com/common/oauth2/v2.0/authorize",
        "https://login.microsoftonline.com/common/oauth2/v2.0/token",
        null,
        ["https://outlook.office.com/IMAP.AccessAsUser.All", "https://outlook.office.com/SMTP.Send", "offline_access"],
        "localhost",
        [new("prompt", "select_account")]);

    public static IReadOnlyList<OAuthProvider> All { get; } = [Google, Microsoft];

    /// <summary>Tests put a provider with the fake server's endpoints in place of the real ones.</summary>
    internal static Func<string, OAuthProvider?>? FindForTests { get; set; }

    public static OAuthProvider? Find(string? id) =>
        id is null ? null : FindForTests?.Invoke(id) ?? All.FirstOrDefault(p => p.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
}

/// <summary>The registration of ThixMail at a provider: the client id (and the "secret" Google gives desktop clients – not secret, but required).</summary>
public sealed record OAuthClient(string ClientId, string? ClientSecret);

/// <summary>
/// THE one place of ThixMail's OAuth client ids. The built-in values are empty until the owner has registered ThixMail at Google and Microsoft
/// (docs/PLAN.md, Phase 7, "OAuth-Registrierungen"); ids of other products are never used. An administrator (policy text values
/// <c>OAuthGoogleClientId</c>, <c>OAuthGoogleClientSecret</c>, <c>OAuthMicrosoftClientId</c>) or a tester (environment variables
/// <c>THIXMAIL_OAUTH_GOOGLE_CLIENT_ID</c>, <c>…_CLIENT_SECRET</c>, <c>THIXMAIL_OAUTH_MICROSOFT_CLIENT_ID</c>) can set their own registration.
/// Without a client id the account page says honestly that the provider needs an app password for now.
/// </summary>
public static class OAuthClients
{
    /// <summary>Built in: filled once the registrations exist. Empty = not available.</summary>
    private static readonly Dictionary<string, OAuthClient> BuiltIn = new(StringComparer.OrdinalIgnoreCase)
    {
        [OAuthProviders.GoogleId] = new("", null),
        [OAuthProviders.MicrosoftId] = new("", null),
    };

    /// <summary>Tests: provider id → client (null = the normal lookup).</summary>
    internal static Func<string, OAuthClient?>? ForTests { get; set; }

    /// <summary>The client of a provider, or null when ThixMail has none for it (yet).</summary>
    public static OAuthClient? For(string providerId)
    {
        if (ForTests is { } test)
        {
            return test(providerId);
        }

        var name = providerId.Length == 0 ? "" : char.ToUpperInvariant(providerId[0]) + providerId[1..].ToLowerInvariant();
        var upper = providerId.ToUpperInvariant();
        var id = AccessPolicy.Text($"OAuth{name}ClientId") ?? Environment.GetEnvironmentVariable($"THIXMAIL_OAUTH_{upper}_CLIENT_ID") ?? BuiltIn.GetValueOrDefault(providerId)?.ClientId;
        if (string.IsNullOrWhiteSpace(id))
        {
            return null;
        }

        var secret = AccessPolicy.Text($"OAuth{name}ClientSecret") ?? Environment.GetEnvironmentVariable($"THIXMAIL_OAUTH_{upper}_CLIENT_SECRET")
                     ?? BuiltIn.GetValueOrDefault(providerId)?.ClientSecret;
        return new OAuthClient(id.Trim(), string.IsNullOrWhiteSpace(secret) ? null : secret.Trim());
    }

    public static bool IsAvailable(string? providerId) => providerId is not null && OAuthProviders.Find(providerId) is not null && For(providerId) is not null;
}
