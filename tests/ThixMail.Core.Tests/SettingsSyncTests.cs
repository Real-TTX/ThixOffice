using MatCloud.Contract;
using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Sync;
using ThixOffice.Core.Business;
using ThixOffice.Core.Platform;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixMail.Core.Tests;

/// <summary>7d: ThixMail's settings through ThixCloud's general sync as the app mail – never passwords, tokens or mail.</summary>
public sealed class SettingsSyncTests
{
    [Fact]
    public async Task Accounts_signatures_rules_and_templates_travel_without_secrets_and_a_second_computer_takes_them()
    {
        using var data = new TestData();
        var cloud = new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => cloud;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        var document = new MailDocument("E-Mail");
        try
        {
            document.AddAccount(TestData.Account, TestData.Password, null);
            document.Accounts.Save(new MailSignature("s1", "Arbeit", "<p>Viele Grüße</p>", "Viele Grüße"));
            document.Accounts.Save(new MailRule { Name = "Shop", FromContains = "shop", MarkRead = true });
            document.Accounts.SaveTemplate("Angebot", "Ihr Angebot", "<p>Hallo</p>", "Hallo");
            await TestData.UntilAsync(() => cloud.SyncItems(CloudApps.Mail, AccountStore.AccountsCollection).Count == 1
                                            && cloud.SyncItems(CloudApps.Mail, AccountStore.SignaturesCollection).Count == 1
                                            && cloud.SyncItems(CloudApps.Mail, AccountStore.RulesCollection).Count == 1
                                            && cloud.SyncItems(CloudApps.Mail, AccountStore.TemplatesCollection).Count == 1, "the settings in ThixCloud");
            var everything = string.Join("\n", new[] { AccountStore.AccountsCollection, AccountStore.SignaturesCollection, AccountStore.RulesCollection, AccountStore.TemplatesCollection }
                .SelectMany(c => cloud.SyncItems(CloudApps.Mail, c)).Select(i => i.Data));
            Assert.Contains(TestData.Address, everything, StringComparison.Ordinal);
            Assert.DoesNotContain(TestData.Password, everything, StringComparison.Ordinal); // never a password
            Assert.DoesNotContain("secret:", everything, StringComparison.Ordinal);

            // a second computer (its own settings store) gets them, and what it adds comes back here
            using var laptopStore = PimStore.Open(Path.Combine(data.Folder, "laptop-mailsettings.db"));
            using var laptop = new AccountStore(laptopStore);
            await using var laptopSync = new PimSync(laptopStore, CloudService.CreateClient);
            await laptopSync.SyncOnceAsync();
            Assert.Equal(TestData.Address, Assert.Single(laptop.Accounts).Address);
            Assert.Null(AccountService.Password(laptop, laptop.Accounts[0], AccountService.ImapSecret)); // the password stayed on the first computer
            Assert.Equal("Shop", Assert.Single(laptop.Rules).Name);

            laptop.Save(new MailSignature("s2", "Privat", "<p>LG</p>", "LG"));
            var privat = TestData.Account with { Id = "privat", Name = "Privat", Address = "anna.privat@example.de", ImapUser = "anna.privat@example.de" };
            laptop.Save(privat);
            await laptopSync.SyncOnceAsync();
            document.SyncOf(TestData.Account.Id)!.Trigger();
            await TestData.UntilAsync(() => document.Accounts.Signatures.Count == 2, "the laptop's signature");
            // the account from the laptop starts here too – and asks for its password (passwords never travel)
            await TestData.UntilAsync(() => document.SyncOf("privat") is null && document.StatusOf("privat").Phase == SyncPhase.NeedsConfirmation, "the laptop's account waiting for the connection test here");

            // removed on the laptop: gone here with its mail
            laptop.Remove("privat");
            await laptopSync.SyncOnceAsync();
            await TestData.UntilAsync(() => document.SyncOf("privat") is null && document.Accounts.Account("privat") is null, "the account removed from the laptop");
        }
        finally
        {
            await document.CloseAsync();
        }
    }

    [Fact]
    public async Task Switching_the_ThixCloud_account_takes_this_computers_settings_into_the_other_account_and_joins_its_own()
    {
        using var data = new TestData();
        var first = new SampleCloud { App = CloudApps.Mail };
        CloudService.HandlerForTests = () => first;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        var document = new MailDocument("E-Mail");
        try
        {
            document.AddAccount(TestData.Account, TestData.Password, null);
            document.Accounts.Save(new MailSignature("s1", "Arbeit", "<p>Viele Grüße</p>", "Viele Grüße"));
            await TestData.UntilAsync(() => first.SyncItems(CloudApps.Mail, AccountStore.AccountsCollection).Count == 1
                                            && first.SyncItems(CloudApps.Mail, AccountStore.SignaturesCollection).Count == 1, "the settings in the first account");

            // another ThixCloud account (another server) that has a signature of its own
            var second = new SampleCloud { App = CloudApps.Mail };
            second.PushElsewhere(CloudApps.Mail, AccountStore.SignaturesCollection, "s9", """{"id":"s9","name":"Verein","html":"<p>Sportliche Grüße</p>","text":"Sportliche Grüße"}""", DateTimeOffset.UtcNow);
            await CloudService.DisconnectAsync();
            await document.ReconnectSettingsAsync();
            CloudService.HandlerForTests = () => second;
            await CloudService.ConnectAsync("https://zweite-cloud.example.com", SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
            await document.ReconnectSettingsAsync();

            await TestData.UntilAsync(() => second.SyncItems(CloudApps.Mail, AccountStore.AccountsCollection).Count == 1
                                            && second.SyncItems(CloudApps.Mail, AccountStore.SignaturesCollection).Count == 2
                                            && document.Accounts.Signatures.Count == 2, "this computer's settings in the second account and its signature here");
            Assert.Equal(TestData.Address, document.Accounts.Accounts.Single().Address); // nothing of this computer was lost
            Assert.NotNull(document.SyncOf(TestData.Account.Id)); // and the account keeps running
            Assert.Equal(0, document.Accounts.PendingCloudChanges);
        }
        finally
        {
            await document.CloseAsync();
        }
    }
}
