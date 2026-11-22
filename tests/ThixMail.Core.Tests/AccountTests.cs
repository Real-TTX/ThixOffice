using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Transport;
using ThixOffice.Core.Models;
using static ThixMail.Core.Tests.TestData;

namespace ThixMail.Core.Tests;

/// <summary>Accounts: validation, passwords only in the TokenStore, the connection test, auto-configuration.</summary>
public sealed class AccountTests
{
    private const string Mozilla = """
        <?xml version="1.0"?>
        <clientConfig version="1.1">
          <emailProvider id="example.de">
            <domain>example.de</domain>
            <displayName>Example Mail</displayName>
            <incomingServer type="pop3"><hostname>pop.example.de</hostname><port>995</port><socketType>SSL</socketType><username>%EMAILADDRESS%</username></incomingServer>
            <incomingServer type="imap"><hostname>imap.example.de</hostname><port>143</port><socketType>STARTTLS</socketType><username>%EMAILLOCALPART%</username><authentication>password-cleartext</authentication></incomingServer>
            <outgoingServer type="smtp"><hostname>smtp.%EMAILDOMAIN%</hostname><port>465</port><socketType>SSL</socketType><username>%EMAILADDRESS%</username></outgoingServer>
          </emailProvider>
        </clientConfig>
        """;

    [Fact]
    public void A_Mozilla_autoconfig_document_is_read_with_its_placeholders()
    {
        var result = AutoConfig.Parse(Mozilla, "anna@example.de", ConfigSource.Autoconfig)!;
        Assert.Equal(new ServerSettings("imap.example.de", 143, ConnectionSecurity.StartTls), result.Imap);
        Assert.Equal("anna", result.ImapUser);
        Assert.Equal(new ServerSettings("smtp.example.de", 465, ConnectionSecurity.SslOnConnect), result.Smtp);
        Assert.Equal("anna@example.de", result.SmtpUser);
        Assert.Equal("Example Mail", result.ProviderName);
        Assert.Null(AutoConfig.Parse("<html>404</html>", "anna@example.de", ConfigSource.Autoconfig));
        Assert.Null(AutoConfig.Parse("kein xml", "anna@example.de", ConfigSource.Autoconfig));
    }

    [Fact]
    public async Task Discovery_asks_the_provider_table_autoconfig_ISPDB_MX_SRV_and_last_guesses()
    {
        var requested = new List<string>();
        AutoConfig.FetchForTests = url =>
        {
            requested.Add(url);
            return Task.FromResult(url.StartsWith("https://autoconfig.thunderbird.net/v1.1/hosted.example", StringComparison.Ordinal) ? Mozilla : null);
        };
        DnsLookup.ResolverForTests = (name, type) => (name, type) switch
        {
            ("firma.example", "MX") => ["10 mx01.hosted.example."],
            ("srv.example", "MX") => [],
            ("_imaps._tcp.srv.example", "SRV") => ["0 1 993 mail.srv.example."],
            ("_submission._tcp.srv.example", "SRV") => ["0 1 587 mail.srv.example."],
            ("google-hosted.example", "MX") => ["1 aspmx.l.google.com."],
            _ => [],
        };
        try
        {
            var gmail = await AutoConfig.DiscoverAsync("anna@gmail.com", CancellationToken.None);
            Assert.Equal(ConfigSource.Provider, gmail.Source);
            Assert.True(gmail.NeedsAppPassword);
            Assert.Empty(requested);

            var hosted = await AutoConfig.DiscoverAsync("anna@firma.example", CancellationToken.None);
            Assert.Equal(ConfigSource.MxIspdb, hosted.Source);
            Assert.Equal("imap.example.de", hosted.Imap.Host);
            Assert.Contains("https://autoconfig.firma.example/mail/config-v1.1.xml?emailaddress=anna%40firma.example", requested);

            var google = await AutoConfig.DiscoverAsync("anna@google-hosted.example", CancellationToken.None);
            Assert.Equal("imap.gmail.com", google.Imap.Host);

            var srv = await AutoConfig.DiscoverAsync("anna@srv.example", CancellationToken.None);
            Assert.Equal(ConfigSource.Srv, srv.Source);
            Assert.Equal(new ServerSettings("mail.srv.example", 993, ConnectionSecurity.SslOnConnect), srv.Imap);
            Assert.Equal(new ServerSettings("mail.srv.example", 587, ConnectionSecurity.StartTls), srv.Smtp);

            var guess = await AutoConfig.DiscoverAsync("anna@unbekannt.example", CancellationToken.None);
            Assert.Equal(ConfigSource.Guess, guess.Source);
            Assert.Equal("imap.unbekannt.example", guess.Imap.Host);
            Assert.NotNull(guess.Hint);
        }
        finally
        {
            AutoConfig.FetchForTests = null;
            DnsLookup.ResolverForTests = null;
        }
    }

    [Theory]
    [InlineData("", "Address")]
    [InlineData("kein-at", "Address")]
    [InlineData("a@b", "Address")]
    [InlineData("an na@example.de", "Address")]
    public void A_wrong_address_is_refused_at_its_field(string address, string field)
    {
        var error = Assert.Throws<BusinessException>(() => AccountService.Validate(Account with { Address = address }));
        Assert.Equal(field, error.Field);
    }

    [Fact]
    public void Servers_and_ports_are_checked()
    {
        Assert.Equal(nameof(MailAccount.Imap), Assert.Throws<BusinessException>(() => AccountService.Validate(Account with { Imap = Account.Imap with { Host = "" } })).Field);
        Assert.Equal(nameof(MailAccount.Smtp), Assert.Throws<BusinessException>(() => AccountService.Validate(Account with { Smtp = Account.Smtp with { Port = 70000 } })).Field);
        Assert.Equal(nameof(MailAccount.Imap), Assert.Throws<BusinessException>(() => AccountService.Validate(Account with { Imap = Account.Imap with { Host = "bad host!" } })).Field);
        var valid = AccountService.Validate(Account with { SmtpUser = "", Imap = Account.Imap with { Host = " IMAP.Example.DE " } });
        Assert.Equal("imap.example.de", valid.Imap.Host);
        Assert.Equal(Address, valid.SmtpUser);
    }

    [Fact]
    public void Passwords_live_only_in_the_TokenStore_never_in_the_account_items()
    {
        using var data = new TestData();
        var (store, accounts, _, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        AccountService.SetPassword(accounts, Account.Id, AccountService.ImapSecret, "s3cr3t-imap");

        Assert.Equal("s3cr3t-imap", AccountService.Password(accounts, Account, AccountService.ImapSecret));
        Assert.Equal("s3cr3t-imap", AccountService.Password(accounts, Account, AccountService.SmtpSecret)); // SMTP uses the IMAP one
        using var raw = ThixOffice.Pim.Store.PimStore.Open(Path.Combine(data.Folder, "settings.db"));
        var items = string.Join("\n", raw.Items(AccountStore.AccountsCollection).Select(i => i.Data));
        Assert.Contains(Address, items, StringComparison.Ordinal);
        Assert.DoesNotContain("s3cr3t", items, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_connection_test_explains_a_wrong_password_an_app_password_and_a_certificate()
    {
        using var data = new TestData();
        Assert.True((await AccountService.TestAsync(data.Server, Account, Password, Password, CancellationToken.None)).Succeeded);

        var wrong = await AccountService.TestAsync(data.Server, Account, "falsch", "falsch", CancellationToken.None);
        Assert.Equal(MailErrorKind.Authentication, wrong.Kind);
        Assert.Contains("Benutzername oder Kennwort", wrong.Message, StringComparison.Ordinal);

        var gmail = Account with { Address = "anna@gmail.com", ImapUser = "anna@gmail.com" };
        var appPassword = await AccountService.TestAsync(data.Server, gmail, "x", "x", CancellationToken.None);
        Assert.Equal(MailErrorKind.AppPasswordRequired, appPassword.Kind);
        Assert.Contains("App-Passwort", appPassword.Message, StringComparison.Ordinal);

        data.Server.IsReachable = false;
        Assert.Equal(MailErrorKind.Network, (await AccountService.TestAsync(data.Server, Account, Password, Password, CancellationToken.None)).Kind);
        data.Server.IsReachable = true;

        data.Server.HasUntrustedCertificate = true;
        var certificate = await AccountService.TestAsync(data.Server, Account, Password, Password, CancellationToken.None);
        Assert.Equal(MailErrorKind.Certificate, certificate.Kind);
        Assert.NotNull(certificate.Certificate);
        var trusting = AccountService.TrustCertificate(Account, certificate.Certificate!);
        Assert.True((await AccountService.TestAsync(data.Server, trusting, Password, Password, CancellationToken.None)).Succeeded);
    }

    [Fact]
    public async Task An_account_is_added_once_and_removed_with_its_mail_and_passwords()
    {
        using var data = new TestData();
        var document = new MailDocument("E-Mail");
        try
        {
            var added = document.AddAccount(Account with { Id = "x" }, Password, null);
            Assert.Throws<BusinessException>(() => document.AddAccount(Account with { Id = "y" }, Password, null));
            Assert.Single(document.Accounts.Accounts);
            Assert.Equal(Password, AccountService.Password(document.Accounts, added, AccountService.ImapSecret));
            await document.RemoveAccountAsync(added.Id);
            Assert.Empty(document.Accounts.Accounts);
            Assert.Null(AccountService.Password(document.Accounts, added, AccountService.ImapSecret));
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public void Folder_roles_come_from_special_use_or_the_name()
    {
        var roles = ThixMail.Core.Store.MailStore.AssignRoles(
        [
            new RemoteFolder("INBOX", "INBOX", null, '/', FolderRole.Inbox, true),
            new RemoteFolder("Gesendete Elemente", "Gesendete Elemente", null, '/', FolderRole.None, true),
            new RemoteFolder("Papierkorb", "Papierkorb", null, '/', FolderRole.None, true),
            new RemoteFolder("INBOX.Spam", "Spam", "INBOX", '.', FolderRole.None, true),
            new RemoteFolder("Archiv", "Archiv", null, '/', FolderRole.Archive, true),
            new RemoteFolder("Archive", "Archive", null, '/', FolderRole.None, true),
        ]);
        Assert.Equal([FolderRole.Inbox, FolderRole.Sent, FolderRole.Trash, FolderRole.Junk, FolderRole.Archive, FolderRole.None], roles.Select(r => r.Role));
    }
}
