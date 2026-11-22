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
            await TestData.UntilAsync(() => document.SyncOf("privat") is { Status.Phase: SyncPhase.SignInFailed }, "the laptop's account asking for its password");

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
}
