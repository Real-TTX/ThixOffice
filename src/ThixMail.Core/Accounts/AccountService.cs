using ThixMail.Core.Model;
using ThixMail.Core.Transport;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixMail.Core.Accounts;

/// <summary>The outcome of a connection test: fine, or a German explanation with its kind (and the certificate to show when that is the problem).</summary>
public sealed record ConnectionTestResult(bool Succeeded, string Message, MailErrorKind? Kind = null, CertificateInfo? Certificate = null, int Folders = 0)
{
    public static ConnectionTestResult Success(int folders) =>
        new(true, $"Verbindung hergestellt: Posteingang und Postausgang funktionieren ({folders} Ordner).", Folders: folders);
}

/// <summary>
/// The business rules of accounts (static, like every service of the suite): validation of an account's fields (errors at their field), the passwords
/// – ONLY in the TokenStore (DPAPI on Windows, the Keychain on macOS; the store keeps the protected value locally, never in the settings that may go to
/// ThixCloud) –, the connection test that must pass before an account is saved (IMAP: sign in, list the folders; SMTP: sign in), with clear German
/// errors (wrong password, an app password needed, a certificate problem – shown, never accepted silently), and accepting a shown certificate.
/// </summary>
public static class AccountService
{
    public const string ImapSecret = "imap";

    public const string SmtpSecret = "smtp";

    /// <summary>Checks the fields of an account; the first problem as a <see cref="BusinessException"/> at its field.</summary>
    public static MailAccount Validate(MailAccount account)
    {
        var address = account.Address.Trim();
        if (address.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie die E-Mail-Adresse an.", nameof(MailAccount.Address));
        }

        if (!IsAddress(address))
        {
            throw new BusinessException("Das ist keine gültige E-Mail-Adresse (z. B. anna@example.de).", nameof(MailAccount.Address));
        }

        CheckServer(account.Imap, "Posteingangsserver (IMAP)", nameof(MailAccount.Imap));
        CheckServer(account.Smtp, "Postausgangsserver (SMTP)", nameof(MailAccount.Smtp));
        if (account.ImapUser.Trim().Length == 0)
        {
            throw new BusinessException("Bitte geben Sie den Benutzernamen für den Posteingangsserver an.", nameof(MailAccount.ImapUser));
        }

        if (account.UsesOAuth && OAuthProviders.Find(account.OAuthProvider) is null)
        {
            throw new BusinessException("Für dieses Konto ist keine Anmeldung im Browser möglich.", "Password");
        }

        return account with
        {
            Address = address,
            Name = account.Name.Trim(),
            SenderName = account.SenderName.Trim(),
            ImapUser = account.ImapUser.Trim(),
            SmtpUser = account.SmtpUser.Trim().Length > 0 ? account.SmtpUser.Trim() : account.ImapUser.Trim(),
            Imap = account.Imap with { Host = account.Imap.Host.Trim().ToLowerInvariant() },
            Smtp = account.Smtp with { Host = account.Smtp.Host.Trim().ToLowerInvariant() },
        };
    }

    /// <summary>A plausible address: one @, a local part, a domain with a dot, no spaces.</summary>
    public static bool IsAddress(string address)
    {
        var at = address.IndexOf('@');
        return at > 0 && at == address.LastIndexOf('@') && at < address.Length - 3 && address[(at + 1)..].Contains('.', StringComparison.Ordinal)
               && !address.Any(char.IsWhiteSpace) && !address.EndsWith('.');
    }

    private static void CheckServer(ServerSettings server, string label, string field)
    {
        if (server.Host.Trim().Length == 0)
        {
            throw new BusinessException($"Bitte geben Sie den {label} an.", field);
        }

        if (Uri.CheckHostName(server.Host.Trim()) == UriHostNameType.Unknown)
        {
            throw new BusinessException($"„{server.Host}“ ist kein gültiger Servername.", field);
        }

        if (server.Port is < 1 or > 65535)
        {
            throw new BusinessException($"Der Port des {label}s muss zwischen 1 und 65535 liegen.", field);
        }
    }

    // ===== passwords =====

    /// <summary>Keeps a password of the account in the TokenStore (empty = forget it).</summary>
    public static void SetPassword(AccountStore store, string accountId, string kind, string password)
    {
        var key = TokenKey(accountId, kind);
        if (password.Length == 0)
        {
            TokenStore.Delete(key);
            store.SetProtectedSecret(accountId, kind, "");
            return;
        }

        store.SetProtectedSecret(accountId, kind, TokenStore.Save(key, password));
    }

    /// <summary>The password (the SMTP one falls back to the IMAP one when the account says so); null when none is stored.</summary>
    public static string? Password(AccountStore store, MailAccount account, string kind)
    {
        if (kind == SmtpSecret && account.SmtpUsesImapPassword)
        {
            kind = ImapSecret;
        }

        return store.ProtectedSecret(account.Id, kind) is { } value ? TokenStore.Read(TokenKey(account.Id, kind), value) : null;
    }

    /// <summary>The account's passwords and OAuth tokens go (the account was removed).</summary>
    public static void ForgetPasswords(AccountStore store, string accountId)
    {
        SetPassword(store, accountId, ImapSecret, "");
        SetPassword(store, accountId, SmtpSecret, "");
        SetOAuthTokens(store, accountId, null);
    }

    private static string TokenKey(string accountId, string kind) => $"mail-{accountId}-{kind}";

    // ===== OAuth =====

    public const string OAuthSecret = "oauth";

    private static readonly Dictionary<string, SemaphoreSlim> RefreshGates = new(StringComparer.Ordinal);

    /// <summary>Keeps the OAuth tokens of an account in the TokenStore (null = forget them).</summary>
    public static void SetOAuthTokens(AccountStore store, string accountId, OAuthTokens? tokens)
    {
        if (tokens is null)
        {
            SetPassword(store, accountId, OAuthSecret, "");
            return;
        }

        using var buffer = new MemoryStream();
        using (var writer = new System.Text.Json.Utf8JsonWriter(buffer))
        {
            writer.WriteStartObject();
            writer.WriteString("access", tokens.AccessToken);
            if (tokens.RefreshToken is { } refresh)
            {
                writer.WriteString("refresh", refresh);
            }

            writer.WriteNumber("expires", tokens.ExpiresAt.ToUnixTimeSeconds());
            writer.WriteEndObject();
        }

        SetPassword(store, accountId, OAuthSecret, System.Text.Encoding.UTF8.GetString(buffer.ToArray()));
    }

    /// <summary>The OAuth tokens of an account, or null.</summary>
    public static OAuthTokens? OAuthTokensOf(AccountStore store, string accountId)
    {
        if (store.ProtectedSecret(accountId, OAuthSecret) is not { } value || TokenStore.Read(TokenKey(accountId, OAuthSecret), value) is not { } json)
        {
            return null;
        }

        try
        {
            using var document = System.Text.Json.JsonDocument.Parse(json);
            var root = document.RootElement;
            return new OAuthTokens(root.GetProperty("access").GetString() ?? "",
                root.TryGetProperty("refresh", out var refresh) ? refresh.GetString() : null,
                DateTimeOffset.FromUnixTimeSeconds(root.GetProperty("expires").GetInt64()));
        }
        catch (Exception exception) when (exception is System.Text.Json.JsonException or KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// What the account signs in with at <paramref name="kind"/>'s server: the password, or for an OAuth account a valid access token – refreshed when
    /// it ends within <see cref="OAuthTokens.Margin"/> or when <paramref name="renew"/> (the server refused the last one). An ended sign-in (refresh
    /// token expired or revoked) becomes <see cref="MailErrorKind.SignInRequired"/>: the sync waits, the account asks to sign in again, the mail stays.
    /// </summary>
    public static async Task<string?> CredentialAsync(AccountStore store, MailAccount account, string kind, bool renew, CancellationToken cancellationToken)
    {
        if (!account.UsesOAuth)
        {
            return Password(store, account, kind);
        }

        var provider = OAuthProviders.Find(account.OAuthProvider);
        var name = provider?.Name ?? "dem Anbieter";
        SemaphoreSlim gate;
        lock (RefreshGates)
        {
            gate = RefreshGates.TryGetValue(account.Id, out var existing) ? existing : RefreshGates[account.Id] = new SemaphoreSlim(1, 1);
        }

        var asked = OAuthTokensOf(store, account.Id);
        await gate.WaitAsync(cancellationToken);
        try
        {
            var tokens = OAuthTokensOf(store, account.Id)
                         ?? throw new MailConnectionException(MailErrorKind.SignInRequired, $"Bitte melden Sie sich bei {name} an (Datei › Konten).");
            var renewedMeanwhile = asked is not null && tokens.AccessToken != asked.AccessToken;
            if (tokens.IsFresh(DateTimeOffset.UtcNow) && (!renew || renewedMeanwhile))
            {
                return tokens.AccessToken;
            }

            if (tokens.RefreshToken is not { } refresh || provider is null)
            {
                throw new MailConnectionException(MailErrorKind.SignInRequired, $"Die Anmeldung bei {name} ist abgelaufen. Bitte melden Sie sich erneut an (Datei › Konten).");
            }

            try
            {
                var fresh = await OAuthService.RefreshAsync(provider, refresh, cancellationToken);
                SetOAuthTokens(store, account.Id, fresh);
                return fresh.AccessToken;
            }
            catch (OAuthException exception) when (exception.Kind == OAuthErrorKind.Network)
            {
                throw new MailConnectionException(MailErrorKind.Network, exception.Message, inner: exception);
            }
            catch (OAuthException exception)
            {
                throw new MailConnectionException(MailErrorKind.SignInRequired, exception.Message, inner: exception);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    /// <summary>Removing an account: revokes its OAuth sign-in where the provider can (best effort), then everything secret of it goes.</summary>
    public static async Task ForgetAsync(AccountStore store, MailAccount account, CancellationToken cancellationToken)
    {
        if (account.UsesOAuth && OAuthProviders.Find(account.OAuthProvider) is { } provider && OAuthTokensOf(store, account.Id) is { } tokens)
        {
            await OAuthService.RevokeAsync(provider, tokens.RefreshToken ?? tokens.AccessToken, cancellationToken);
        }

        ForgetPasswords(store, account.Id);
    }

    /// <summary>The OAuth provider an account of this address/server would sign in with (Gmail, Google Workspace, Outlook.com, Microsoft 365), or null.</summary>
    public static string? OAuthProviderFor(string address, string imapHost)
    {
        if (AutoConfig.ProviderOf(address)?.OAuthProvider is { } byDomain)
        {
            return byDomain;
        }

        var host = imapHost.Trim().ToLowerInvariant();
        return host switch
        {
            "imap.gmail.com" or "imap.googlemail.com" => OAuthProviders.GoogleId,
            "outlook.office365.com" or "imap-mail.outlook.com" or "outlook.office.com" => OAuthProviders.MicrosoftId,
            _ => null,
        };
    }

    // ===== the connection test =====

    /// <summary>Signs in at both servers (nothing is sent, nothing changed) and explains a failure.</summary>
    public static async Task<ConnectionTestResult> TestAsync(IMailTransport transport, MailAccount account, string imapPassword, string smtpPassword,
        CancellationToken cancellationToken)
    {
        int folders;
        try
        {
            await using var imap = await transport.ConnectImapAsync(account, imapPassword, cancellationToken);
            folders = (await imap.ListFoldersAsync(cancellationToken)).Count;
        }
        catch (MailConnectionException exception)
        {
            return Explain(exception, account, "Posteingangsserver");
        }

        try
        {
            await using var smtp = await transport.ConnectSmtpAsync(account, smtpPassword, cancellationToken);
        }
        catch (MailConnectionException exception)
        {
            return Explain(exception, account, "Postausgangsserver");
        }

        return ConnectionTestResult.Success(folders);
    }

    /// <summary>A failure in the words of the account page; for a provider that needs an app password the wrong-password message says so.</summary>
    public static ConnectionTestResult Explain(MailConnectionException exception, MailAccount account, string server)
    {
        var provider = AutoConfig.ProviderOf(account.Address);
        return exception.Kind switch
        {
            MailErrorKind.Authentication when account.UsesOAuth =>
                new(false, $"{server}: Der Server hat die Anmeldung im Browser nicht angenommen. Ist IMAP/SMTP für dieses Postfach freigeschaltet?", MailErrorKind.Authentication),
            MailErrorKind.Authentication when provider is { NeedsAppPassword: true } =>
                new(false, $"{server}: Die Anmeldung wurde abgelehnt. {provider.Name} verlangt ein App-Passwort statt Ihres normalen Kennworts. {provider.Hint}", MailErrorKind.AppPasswordRequired),
            MailErrorKind.Authentication => new(false, $"{server}: Benutzername oder Kennwort stimmen nicht.", MailErrorKind.Authentication),
            MailErrorKind.SignInRequired => new(false, $"{server}: {exception.Message}", MailErrorKind.SignInRequired),
            MailErrorKind.AppPasswordRequired => new(false, $"{server}: {exception.Message} {provider?.Hint}".Trim(), MailErrorKind.AppPasswordRequired),
            MailErrorKind.Certificate => new(false, $"{server}: Das Zertifikat des Servers ist nicht vertrauenswürdig. {exception.Certificate?.Problem}".Trim(),
                MailErrorKind.Certificate, exception.Certificate),
            MailErrorKind.Network => new(false, $"{server}: {exception.Message} Prüfen Sie den Servernamen, den Port und die Internetverbindung.", MailErrorKind.Network),
            _ => new(false, $"{server}: {exception.Message}", exception.Kind),
        };
    }

    /// <summary>The person looked at the certificate and accepts it for this host (stored as host + SHA-256; another certificate is asked again).</summary>
    public static MailAccount TrustCertificate(MailAccount account, CertificateInfo certificate)
    {
        var entry = $"{certificate.Host.ToLowerInvariant()}|{certificate.Thumbprint.ToUpperInvariant()}";
        return account.TrustedCertificates.Contains(entry, StringComparer.Ordinal) ? account : account with { TrustedCertificates = [.. account.TrustedCertificates, entry] };
    }
}
