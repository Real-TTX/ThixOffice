using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixMail.Core.Transport;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Platform;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixMail.Core.Tests;

/// <summary>
/// Every test: a data folder of its own, an in-memory registry, no policies, secrets in memory, a mail server in memory with Anna's mailbox, the
/// stores of ThixMail in that folder. Nothing real is touched.
/// </summary>
internal sealed class TestData : IDisposable
{
    public const string Address = "anna@example.de";
    public const string Password = "geheim";

    public TestData()
    {
        AppInfo.Initialize(ThixMailApp.Definition, typeof(ThixMailApp).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixmail-core-tests", Guid.NewGuid().ToString("N"));
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        UserRegistry.UseForTests(new MemoryUserRegistry());
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        AccessPolicy.UseTextReaderForTests(_ => null);
        TokenStore.UseMemoryForTests(true);
        SettingsService.ResetCache();
        CloudService.ResetForTests();
        Server = new SampleMailServer();
        Server.AddMailbox(Address, Password);
        MailDocument.Transport = Server;
        MailDocument.ConfigureSyncForTests = sync =>
        {
            sync.Backoff = TimeSpan.FromMilliseconds(50);
            sync.MaximumBackoff = TimeSpan.FromMilliseconds(200);
            sync.IdleTime = TimeSpan.FromSeconds(30);
        };
    }

    public string Folder { get; }

    public SampleMailServer Server { get; }

    public static MailAccount Account => new()
    {
        Id = "anna",
        Name = "Arbeit",
        SenderName = "Anna Berger",
        Address = Address,
        Imap = new ServerSettings("imap.example.de", 993, ConnectionSecurity.SslOnConnect),
        ImapUser = Address,
        Smtp = new ServerSettings("smtp.example.de", 587, ConnectionSecurity.StartTls),
        SmtpUser = Address,
    };

    /// <summary>A store, the settings store with Anna's account and her password, and a sync on them (not started).</summary>
    public (MailStore Store, AccountStore Accounts, MailSync Sync, MailSession Session) Open()
    {
        var store = MailStore.Open(Path.Combine(Folder, "mail.db"));
        var accounts = AccountStore.Open(Path.Combine(Folder, "settings.db"));
        accounts.Save(Account);
        AccountService.SetPassword(accounts, Account.Id, AccountService.ImapSecret, Password);
        var sync = new MailSync(store, Account, Server, (account, kind) => AccountService.Password(accounts, account, kind))
        {
            Backoff = TimeSpan.FromMilliseconds(50),
            MaximumBackoff = TimeSpan.FromMilliseconds(200),
            IdleTime = TimeSpan.FromSeconds(30),
        };
        var session = new MailSession(store, accounts, _ => { });
        return (store, accounts, sync, session);
    }

    public static MailAddress Jonas => new("Jonas Weber", "jonas.weber@example.de");

    /// <summary>Waits (up to 10 s) until <paramref name="condition"/> holds.</summary>
    public static async Task UntilAsync(Func<bool> condition, string what)
    {
        for (var round = 0; round < 400 && !condition(); round++)
        {
            await Task.Delay(25);
        }

        Assert.True(condition(), $"Timed out waiting for {what}");
    }

    public static string FixturePath(string name) => Path.Combine(AppContext.BaseDirectory, "Fixtures", name);

    public static byte[] Fixture(string name) => File.ReadAllBytes(FixturePath(name));

    public void Dispose()
    {
        MailDocument.Transport = MailKitTransport.Instance;
        MailDocument.ConfigureSyncForTests = null;
        FakeOAuthServer.Unplug();
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(false);
        AccessPolicy.UsePolicyReaderForTests(null);
        AccessPolicy.UseTextReaderForTests(null);
        ThixMail.Core.Accounts.OAuthProviders.FindForTests = null;
        ThixMail.Core.Accounts.OAuthClients.ForTests = null;
        ThixMail.Core.Accounts.OAuthService.HandlerForTests = null;
        UserRegistry.UseForTests(null);
        SettingsService.ResetCache();
        AppPaths.UseDirectoryForTests(null);
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        try
        {
            Directory.Delete(Folder, recursive: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
        }
    }
}
