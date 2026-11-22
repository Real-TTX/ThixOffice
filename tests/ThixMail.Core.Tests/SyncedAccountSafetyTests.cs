using System.Text.Json;
using MatCloud.Contract;
using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Sync;
using ThixOffice.Core.Business;
using ThixOffice.Core.Platform;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixMail.Core.Tests;

/// <summary>
/// 7d: an account that comes through ThixCloud's sync never inherits what another computer decided – the certificates accepted there stay there, and
/// nothing connects (no password of this computer goes anywhere) until the connection test passed on this computer.
/// </summary>
public sealed class SyncedAccountSafetyTests
{
    [Fact]
    public async Task An_accepted_certificate_stays_on_its_computer_and_never_travels()
    {
        using var data = new TestData();
        data.Server.HasUntrustedCertificate = true;
        var (cloud, document) = await SignedInAsync();
        try
        {
            var trusted = TestData.Account with { TrustedCertificates = [$"imap.example.de|{Transport.SampleMailServer.CertificateThumbprint}"] };
            document.AddAccount(trusted, TestData.Password, null);
            await TestData.UntilAsync(() => document.StatusOf(trusted.Id).Phase == SyncPhase.Idle, "the account with its accepted certificate");
            await TestData.UntilAsync(() => cloud.SyncItems(CloudApps.Mail, AccountStore.AccountsCollection).Count == 1 && document.Accounts.PendingCloudChanges == 0, "the account in ThixCloud");

            var item = cloud.SyncItems(CloudApps.Mail, AccountStore.AccountsCollection).Single();
            Assert.DoesNotContain(Transport.SampleMailServer.CertificateThumbprint, item.Data!, StringComparison.Ordinal);
            Assert.Single(document.Accounts.Account(trusted.Id)!.TrustedCertificates); // here it is still accepted

            // the laptop has the account, but not the acceptance – and an item that names one (an older build, or a forged one) is not believed
            using var laptopStore = PimStore.Open(Path.Combine(data.Folder, "laptop.db"));
            using var laptop = new AccountStore(laptopStore);
            await using var laptopSync = new PimSync(laptopStore, CloudService.CreateClient);
            await laptopSync.SyncOnceAsync();
            Assert.Empty(laptop.Account(trusted.Id)!.TrustedCertificates);
            cloud.PushElsewhere(CloudApps.Mail, AccountStore.AccountsCollection, "fremd",
                JsonSerializer.Serialize(new { id = "fremd", address = "x@example.de", trustedCertificates = new[] { "imap.example.de|ABC" } }), DateTimeOffset.UtcNow);
            await laptopSync.SyncOnceAsync();
            Assert.Empty(laptop.Account("fremd")!.TrustedCertificates);
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task An_account_from_another_computer_connects_only_after_the_connection_test_here()
    {
        using var data = new TestData();
        var (cloud, document) = await SignedInAsync();
        try
        {
            var other = TestData.Account with { Id = "vom-laptop" };
            cloud.PushElsewhere(CloudApps.Mail, AccountStore.AccountsCollection, other.Id, JsonSerializer.Serialize(other, Store.MailJson.Default.MailAccount), DateTimeOffset.UtcNow);
            await TestData.UntilAsync(() => document.Accounts.Account(other.Id) is not null, "the account from the laptop");
            Assert.Equal(SyncPhase.NeedsConfirmation, document.StatusOf(other.Id).Phase);
            Assert.Null(document.SyncOf(other.Id));
            await Task.Delay(200);
            Assert.Equal(0, data.Server.Connections); // nothing connected – not even to try a password

            // the form ran its test (AccountsPage.SaveAsync) and saves: now it syncs like an account added here
            document.UpdateAccount(document.Accounts.Account(other.Id)!, TestData.Password);
            await TestData.UntilAsync(() => document.StatusOf(other.Id).Phase == SyncPhase.Idle, "the confirmed account syncing");
            Assert.False(document.NeedsConfirmation(document.Accounts.Account(other.Id)!));
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task Servers_changed_on_another_computer_stop_the_account_here_and_its_password_is_not_sent_there()
    {
        using var data = new TestData();
        var (cloud, document) = await SignedInAsync();
        try
        {
            document.AddAccount(TestData.Account, TestData.Password, null);
            await TestData.UntilAsync(() => document.StatusOf(TestData.Account.Id).Phase == SyncPhase.Idle && document.Accounts.PendingCloudChanges == 0, "the account syncing and in ThixCloud");
            var mailHere = document.Store.Folders(TestData.Account.Id).Count;

            // only the name changes elsewhere: the running account takes it at once
            var renamed = TestData.Account with { Name = "Büro" };
            cloud.PushElsewhere(CloudApps.Mail, AccountStore.AccountsCollection, renamed.Id, JsonSerializer.Serialize(renamed, Store.MailJson.Default.MailAccount), DateTimeOffset.UtcNow.AddSeconds(1));
            await TestData.UntilAsync(() => document.SyncOf(renamed.Id)?.Account.Name == "Büro", "the new name in the running sync");

            // the IMAP server changes elsewhere: the sync stops, the mail here stays, nothing connects to the new server
            var moved = renamed with { Imap = new ServerSettings("imap.woanders.example", 993, ConnectionSecurity.SslOnConnect) };
            cloud.PushElsewhere(CloudApps.Mail, AccountStore.AccountsCollection, moved.Id, JsonSerializer.Serialize(moved, Store.MailJson.Default.MailAccount), DateTimeOffset.UtcNow.AddSeconds(2));
            await TestData.UntilAsync(() => document.SyncOf(moved.Id) is null && document.StatusOf(moved.Id).Phase == SyncPhase.NeedsConfirmation, "the account stopped for the test");
            var connections = data.Server.Connections;
            await Task.Delay(200);
            Assert.Equal(connections, data.Server.Connections);
            Assert.Equal(mailHere, document.Store.Folders(moved.Id).Count);
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public void A_store_of_an_older_build_keeps_its_accepted_certificates_here_and_sends_the_account_up_without_them()
    {
        using var data = new TestData();
        var path = Path.Combine(data.Folder, "old-settings.db");
        var trust = $"imap.example.de|{Transport.SampleMailServer.CertificateThumbprint}";
        using (var old = PimStore.Open(path))
        {
            // what 7b–7d wrote: the acceptance inside the item
            old.Write([(AccountStore.AccountsCollection, TestData.Account.Id, JsonSerializer.Serialize(TestData.Account with { TrustedCertificates = [trust] }, Store.MailJson.Default.MailAccount))]);
            old.MarkClean(AccountStore.AccountsCollection, TestData.Account.Id, DateTimeOffset.UtcNow, 1);
        }

        using var accounts = AccountStore.Open(path);
        var account = accounts.Account(TestData.Account.Id)!;
        Assert.Equal([trust], account.TrustedCertificates);
        Assert.True(accounts.IsConfirmed(account)); // an account of this computer from before
        Assert.Equal(1, accounts.PendingCloudChanges); // goes up again – without the acceptance
        Assert.DoesNotContain("trustedCertificates\":[\"", PimItem(path), StringComparison.Ordinal);
    }

    private static string PimItem(string path)
    {
        using var store = PimStore.Open(path);
        return store.Get(AccountStore.AccountsCollection, TestData.Account.Id)!.Data!;
    }

    private static async Task<(SampleCloud Cloud, MailDocument Document)> SignedInAsync()
    {
        var cloud = new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => cloud;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        return (cloud, new MailDocument("E-Mail"));
    }
}
