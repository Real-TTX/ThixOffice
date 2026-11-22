using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixMail.Core.Transport;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;

namespace ThixMail.Core.Tests;

/// <summary>OAuth 2.0 for IMAP/SMTP against <see cref="FakeOAuthServer"/> (a real loopback redirect) and the SampleMailServer taking XOAUTH2.</summary>
public sealed class OAuthTests : IDisposable
{
    private const string Gmail = "anna.berger@gmail.com";

    private readonly TestData _data = new();
    private readonly FakeOAuthServer _provider = new FakeOAuthServer().Plug();

    public OAuthTests()
    {
        _data.Server.AddMailbox(Gmail, "unbenutzt");
        _provider.Issued = token => _data.Server.AcceptAccessToken(Gmail, token);
    }

    public void Dispose()
    {
        FakeOAuthServer.Unplug();
        AccessPolicy.UseTextReaderForTests(null);
        _data.Dispose();
    }

    private static MailAccount GmailAccount => new()
    {
        Id = "gmail",
        SenderName = "Anna Berger",
        Address = Gmail,
        Imap = new ServerSettings("imap.gmail.com", 993, ConnectionSecurity.SslOnConnect),
        ImapUser = Gmail,
        Smtp = new ServerSettings("smtp.gmail.com", 465, ConnectionSecurity.SslOnConnect),
        SmtpUser = Gmail,
        Authentication = MailAuthentication.OAuth,
        OAuthProvider = OAuthProviders.GoogleId,
    };

    private Task<OAuthTokens> SignInAsync() =>
        OAuthService.AuthorizeAsync(OAuthProviders.Find(OAuthProviders.GoogleId)!, Gmail, _provider.BrowseAsync, CancellationToken.None);

    [Fact]
    public async Task The_browser_sign_in_uses_PKCE_and_a_loopback_redirect_and_returns_tokens()
    {
        var tokens = await SignInAsync();

        Assert.StartsWith("access-", tokens.AccessToken, StringComparison.Ordinal);
        Assert.StartsWith("refresh-", tokens.RefreshToken, StringComparison.Ordinal);
        Assert.True(tokens.IsFresh(DateTimeOffset.UtcNow));
        var request = _provider.LastAuthorization;
        Assert.Equal(Gmail, request["login_hint"]);
        Assert.Equal("https://mail.google.com/", request["scope"]);
        Assert.Equal("offline", request["access_type"]);
        Assert.Equal(43, request["code_challenge"].Length); // SHA-256, base64url without padding
        Assert.True(request["state"].Length >= 20);
    }

    [Fact]
    public async Task Refused_consent_and_missing_admin_approval_are_explained_in_German()
    {
        _provider.Consent = FakeConsent.Refuse;
        var refused = await Assert.ThrowsAsync<OAuthException>(SignInAsync);
        Assert.Equal(OAuthErrorKind.ConsentRefused, refused.Kind);
        Assert.Contains("nicht erlaubt", refused.Message, StringComparison.Ordinal);

        _provider.Consent = FakeConsent.AdminRequired;
        var admin = await Assert.ThrowsAsync<OAuthException>(() =>
            OAuthService.AuthorizeAsync(OAuthProviders.Find(OAuthProviders.MicrosoftId)!, "anna@firma.de", _provider.BrowseAsync, CancellationToken.None));
        Assert.Equal(OAuthErrorKind.AdminApprovalRequired, admin.Kind);
        Assert.Contains("Administratorzustimmung", admin.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_sign_in_nobody_finishes_ends_after_the_timeout()
    {
        var previous = OAuthService.SignInTimeout;
        OAuthService.SignInTimeout = TimeSpan.FromMilliseconds(300);
        try
        {
            var cancelled = await Assert.ThrowsAsync<OAuthException>(() =>
                OAuthService.AuthorizeAsync(OAuthProviders.Find(OAuthProviders.GoogleId)!, Gmail, _ => Task.CompletedTask, CancellationToken.None));
            Assert.Equal(OAuthErrorKind.Cancelled, cancelled.Kind);
        }
        finally
        {
            OAuthService.SignInTimeout = previous;
        }
    }

    [Fact]
    public void Without_a_client_id_the_provider_is_not_available_and_ids_come_from_policy_or_environment()
    {
        FakeOAuthServer.Unplug();
        AccessPolicy.UseTextReaderForTests(_ => null);
        Environment.SetEnvironmentVariable("THIXMAIL_OAUTH_GOOGLE_CLIENT_ID", null);
        Assert.Null(OAuthClients.For(OAuthProviders.GoogleId));
        Assert.False(OAuthClients.IsAvailable(OAuthProviders.MicrosoftId));

        Environment.SetEnvironmentVariable("THIXMAIL_OAUTH_GOOGLE_CLIENT_ID", "from-env");
        try
        {
            Assert.Equal("from-env", OAuthClients.For(OAuthProviders.GoogleId)?.ClientId);
            AccessPolicy.UseTextReaderForTests(name => name == "OAuthGoogleClientId" ? "from-policy" : name == "OAuthGoogleClientSecret" ? "s" : null);
            Assert.Equal(new OAuthClient("from-policy", "s"), OAuthClients.For(OAuthProviders.GoogleId));
        }
        finally
        {
            Environment.SetEnvironmentVariable("THIXMAIL_OAUTH_GOOGLE_CLIENT_ID", null);
        }
    }

    [Fact]
    public void Auto_configuration_knows_which_providers_sign_in_in_the_browser()
    {
        Assert.Equal(OAuthProviders.GoogleId, AutoConfig.ProviderOf("a@gmail.com")?.OAuthProvider);
        Assert.Equal(OAuthProviders.MicrosoftId, AutoConfig.ProviderOf("a@outlook.de")?.OAuthProvider);
        Assert.Null(AutoConfig.ProviderOf("a@posteo.de")?.OAuthProvider);
        Assert.Equal(OAuthProviders.MicrosoftId, AccountService.OAuthProviderFor("anna@firma.de", "outlook.office365.com"));
        Assert.Equal(OAuthProviders.GoogleId, AccountService.OAuthProviderFor("anna@firma.de", "imap.gmail.com"));
        var parsed = AutoConfig.Parse("""
            <clientConfig version="1.1"><emailProvider id="firma.de">
              <incomingServer type="imap"><hostname>outlook.office365.com</hostname><port>993</port><socketType>SSL</socketType><authentication>OAuth2</authentication></incomingServer>
              <outgoingServer type="smtp"><hostname>smtp.office365.com</hostname><port>587</port><socketType>STARTTLS</socketType></outgoingServer>
            </emailProvider></clientConfig>
            """, "anna@firma.de", ConfigSource.Autoconfig);
        Assert.Equal(OAuthProviders.MicrosoftId, parsed?.OAuthProvider);
    }

    [Fact]
    public async Task An_OAuth_account_syncs_through_XOAUTH2_and_refreshes_its_token_before_it_ends()
    {
        _data.Server.Deliver(Gmail, "INBOX", TestData.Jonas, "Hallo über OAuth", "Text");
        _provider.AccessLifetime = 60; // ends within the margin: renewed before connecting
        using var store = MailStore.Open(Path.Combine(_data.Folder, "mail.db"));
        using var accounts = AccountStore.Open(Path.Combine(_data.Folder, "settings.db"));
        accounts.Save(GmailAccount);
        AccountService.SetOAuthTokens(accounts, GmailAccount.Id, await SignInAsync());
        await using var sync = Sync(store, accounts);

        await sync.SyncOnceAsync();

        Assert.Equal(1, _provider.Refreshes);
        Assert.True(_data.Server.XOAuth2SignIns > 0);
        Assert.Contains(store.Headers(store.List(new MessageQuery([store.FolderByRole(GmailAccount.Id, FolderRole.Inbox)!.Id], ListFilter.All)).Select(e => e.Id).ToList()),
            h => h.Subject == "Hallo über OAuth");
        var kept = AccountService.OAuthTokensOf(accounts, GmailAccount.Id)!;
        Assert.NotNull(kept.RefreshToken); // Google sends no new refresh token: the old one stays
    }

    [Fact]
    public async Task A_token_the_server_refuses_is_renewed_once_and_the_sync_goes_on()
    {
        using var store = MailStore.Open(Path.Combine(_data.Folder, "mail.db"));
        using var accounts = AccountStore.Open(Path.Combine(_data.Folder, "settings.db"));
        accounts.Save(GmailAccount);
        AccountService.SetOAuthTokens(accounts, GmailAccount.Id, await SignInAsync());
        _data.Server.RevokeAccessTokens(Gmail); // still "fresh" here, refused there
        await using var sync = Sync(store, accounts);

        await sync.SyncOnceAsync();

        Assert.Equal(1, _provider.Refreshes);
    }

    [Fact]
    public async Task An_ended_sign_in_asks_to_sign_in_again_and_keeps_the_mail()
    {
        _data.Server.Deliver(Gmail, "INBOX", TestData.Jonas, "Bleibt da", "Text");
        using var store = MailStore.Open(Path.Combine(_data.Folder, "mail.db"));
        using var accounts = AccountStore.Open(Path.Combine(_data.Folder, "settings.db"));
        accounts.Save(GmailAccount);
        AccountService.SetOAuthTokens(accounts, GmailAccount.Id, await SignInAsync());
        await using var sync = Sync(store, accounts);
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(GmailAccount.Id, FolderRole.Inbox)!;

        _provider.ExpireRefreshTokens();
        _data.Server.RevokeAccessTokens(Gmail);
        sync.Start();
        await TestData.UntilAsync(() => sync.Status.Phase == SyncPhase.SignInFailed, "the sign-in to fail");

        Assert.Contains("erneut an", sync.Status.Message, StringComparison.Ordinal);
        Assert.Single(store.List(new MessageQuery([inbox.Id], ListFilter.All)));

        // signing in again: the sync goes on with the new tokens
        AccountService.SetOAuthTokens(accounts, GmailAccount.Id, await SignInAsync());
        sync.Update(GmailAccount);
        await TestData.UntilAsync(() => sync.Status.Phase == SyncPhase.Idle, "the sync to run again");
    }

    [Fact]
    public async Task The_connection_test_and_sending_use_the_token()
    {
        var tokens = await SignInAsync();
        Assert.True((await AccountService.TestAsync(_data.Server, GmailAccount, tokens.AccessToken, tokens.AccessToken, CancellationToken.None)).Succeeded);
        var refused = await AccountService.TestAsync(_data.Server, GmailAccount, "access-falsch", "access-falsch", CancellationToken.None);
        Assert.Equal(MailErrorKind.Authentication, refused.Kind);
        Assert.Contains("Anmeldung im Browser", refused.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Removing_an_OAuth_account_revokes_its_sign_in_and_forgets_the_tokens()
    {
        var document = new MailDocument("E-Mail");
        try
        {
            var added = document.AddAccount(GmailAccount, "", null, await SignInAsync());
            Assert.NotNull(AccountService.OAuthTokensOf(document.Accounts, added.Id));
            Assert.Null(AccountService.Password(document.Accounts, added, AccountService.ImapSecret));

            await document.RemoveAccountAsync(added.Id);

            Assert.Single(_provider.Revoked);
            Assert.StartsWith("refresh-", _provider.Revoked[0], StringComparison.Ordinal);
            Assert.Null(AccountService.OAuthTokensOf(document.Accounts, added.Id));
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public void An_OAuth_account_needs_a_known_provider()
    {
        var exception = Assert.Throws<BusinessException>(() => AccountService.Validate(GmailAccount with { OAuthProvider = "unbekannt" }));
        Assert.Equal("Password", exception.Field);
    }

    private MailSync Sync(MailStore store, AccountStore accounts) =>
        new(store, GmailAccount, _data.Server, (account, kind, renew, cancellationToken) => AccountService.CredentialAsync(accounts, account, kind, renew, cancellationToken))
        {
            Backoff = TimeSpan.FromMilliseconds(50),
            MaximumBackoff = TimeSpan.FromMilliseconds(200),
            IdleTime = TimeSpan.FromSeconds(30),
        };
}
