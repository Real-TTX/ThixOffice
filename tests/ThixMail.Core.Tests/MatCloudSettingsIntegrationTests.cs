using MatCloud.Contract;
using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Sync;
using ThixOffice.Core.Business;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;
using static ThixMail.Core.Tests.MatCloudEmbeddingIntegrationTests;

namespace ThixMail.Core.Tests;

/// <summary>
/// ThixMail's settings (accounts without passwords, signatures, categories, rules, templates) through the general sync of a REAL MatCloud as the app
/// <c>mail</c> – env-gated like <see cref="MatCloudEmbeddingIntegrationTests"/> (a throwaway instance, see CLAUDE.md). Two computers: this one is a whole
/// <see cref="MailDocument"/> signed in through CloudService, the laptop is a settings store with its own device token and its own <see cref="PimSync"/>.
/// Every test removes what it added (the account of the instance is shared by all runs).
/// </summary>
public sealed class MatCloudSettingsIntegrationTests
{
    [Fact]
    public async Task Two_computers_share_accounts_without_passwords_signatures_rules_templates_and_removals()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        await SignInAsMailAsync("IT-MAILSET-PC");
        var document = new MailDocument("E-Mail");
        var run = Guid.NewGuid().ToString("N")[..6];
        var account = TestData.Account with { Id = "it-" + run, Name = "Arbeit " + run };
        using var laptopStore = PimStore.Open(Path.Combine(data.Folder, "laptop-mailsettings.db"));
        using var laptop = new AccountStore(laptopStore);
        var laptopToken = await LoginAsync("IT-MAILSET-LAPTOP", CloudApps.Mail);
        await using var laptopSync = new PimSync(laptopStore, () => Client(laptopToken));
        try
        {
            document.AddAccount(account, TestData.Password, null);
            document.Accounts.Save(new MailSignature("sig-" + run, "Arbeit " + run, "<p>Viele Grüße</p>", "Viele Grüße"));
            document.Accounts.Save(new MailRule { Id = "rule-" + run, Name = "Shop " + run, FromContains = "shop", MarkRead = true });
            var template = document.Accounts.SaveTemplate("Angebot " + run, "Ihr Angebot", "<p>Hallo</p>", "Hallo");
            document.Accounts.Save(new MailCategory("Projekt " + run, 1));
            await TestData.UntilAsync(() => document.Accounts.PendingCloudChanges == 0 && document.SettingsSyncStatus.State == SyncState.Idle, "the settings in MatCloud");

            // what MatCloud holds for the app mail: the settings, never a password or a protected secret
            using (var reader = Client(laptopToken))
            {
                var held = await AllChangesAsync(reader, CloudApps.Mail);
                var ours = held.Where(i => !i.IsDeleted && (i.Data ?? "").Contains(run, StringComparison.Ordinal)).ToList();
                Assert.Equal(
                    [AccountStore.AccountsCollection, AccountStore.CategoriesCollection, AccountStore.RulesCollection, AccountStore.SignaturesCollection, AccountStore.TemplatesCollection],
                    ours.Select(i => i.Collection).Distinct().Order(StringComparer.Ordinal));
                var everything = string.Join("\n", held.Select(i => i.Data));
                Assert.DoesNotContain(TestData.Password, everything, StringComparison.Ordinal);
                Assert.DoesNotContain("secret:", everything, StringComparison.Ordinal);
            }

            // the laptop takes them – the account without its password
            await laptopSync.SyncOnceAsync();
            var arrived = Assert.Single(laptop.Accounts, a => a.Id == account.Id);
            Assert.Equal((account.Address, account.Imap.Host), (arrived.Address, arrived.Imap.Host));
            Assert.Null(AccountService.Password(laptop, arrived, AccountService.ImapSecret));
            Assert.Contains(laptop.Signatures, s => s.Id == "sig-" + run);
            Assert.Contains(laptop.Rules, r => r.Id == "rule-" + run);
            Assert.Contains(laptop.Templates, t => t.Id == template.Id);
            Assert.Contains(laptop.Categories, c => c.Name == "Projekt " + run);

            // the laptop adds a signature and an account: both arrive here, the account waits for its password (passwords never travel)
            laptop.Save(new MailSignature("sig2-" + run, "Privat " + run, "<p>LG</p>", "LG"));
            var privat = TestData.Account with { Id = "itp-" + run, Name = "Privat " + run, Address = $"privat.{run}@example.de", ImapUser = $"privat.{run}@example.de" };
            laptop.Save(privat);
            await laptopSync.SyncOnceAsync();
            await TestData.UntilAsync(() => document.Accounts.Signature("sig2-" + run) is not null, "the laptop's signature");
            await TestData.UntilAsync(() => document.Accounts.Account(privat.Id) is not null && document.SyncOf(privat.Id) is null && document.StatusOf(privat.Id).Phase == SyncPhase.NeedsConfirmation, "the laptop's account waiting for the connection test here");
            Assert.Null(AccountService.Password(document.Accounts, privat, AccountService.ImapSecret));

            // removed on the laptop: gone here with its mail; a rule changed here reaches the laptop
            laptop.Remove(privat.Id);
            await laptopSync.SyncOnceAsync();
            await TestData.UntilAsync(() => document.SyncOf(privat.Id) is null && document.Accounts.Account(privat.Id) is null, "the account removed on the laptop");
            document.Accounts.Save(document.Accounts.Rules.Single(r => r.Id == "rule-" + run) with { Name = "Shop geändert " + run });
            await TestData.UntilAsync(() => document.Accounts.PendingCloudChanges == 0, "the changed rule in MatCloud");
            await laptopSync.SyncOnceAsync();
            Assert.Equal("Shop geändert " + run, laptop.Rules.Single(r => r.Id == "rule-" + run).Name);
        }
        finally
        {
            await RemoveRunAsync(document, run);
            await laptopSync.SyncOnceAsync();
            Assert.DoesNotContain(laptop.Accounts, a => a.Id.EndsWith(run, StringComparison.Ordinal));
            await document.CloseAsync();
            await CloudService.DisconnectAsync();
        }
    }

    [Fact]
    public async Task When_MatCloud_ends_the_sign_in_the_settings_stay_their_changes_wait_and_go_up_after_the_next_sign_in()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        await SignInAsMailAsync("IT-MAILSET-401");
        var document = new MailDocument("E-Mail");
        var run = Guid.NewGuid().ToString("N")[..6];
        try
        {
            document.Accounts.Save(new MailSignature("sig-" + run, "Vorher " + run, "<p>A</p>", "A"));
            await TestData.UntilAsync(() => document.Accounts.PendingCloudChanges == 0, "the first signature in MatCloud");

            // the device is removed in MatCloud (its token ends): the settings' sync stops at the 401
            using (var client = CloudService.CreateClient())
            {
                await client.LogoutAsync(CancellationToken.None);
            }

            document.Accounts.Save(new MailSignature("sig2-" + run, "Abgemeldet " + run, "<p>B</p>", "B"));
            await TestData.UntilAsync(() => document.SettingsSyncStatus.State == SyncState.SignedOut, "the settings' sync to stop at the 401");
            Assert.Equal(1, document.Accounts.PendingCloudChanges); // waits
            Assert.Equal(2, document.Accounts.Signatures.Count(s => s.Id.EndsWith(run, StringComparison.Ordinal))); // nothing lost

            // signed in again (the same ThixCloud account, a new device): the waiting change goes up
            await SignInAsMailAsync("IT-MAILSET-401B");
            await document.ReconnectSettingsAsync();
            await TestData.UntilAsync(() => document.Accounts.PendingCloudChanges == 0, "the waiting change after the new sign-in");
            using var reader = Client(await LoginAsync("IT-MAILSET-READER", CloudApps.Mail));
            Assert.Contains(await AllChangesAsync(reader, CloudApps.Mail), i => i.Key == "sig2-" + run && !i.IsDeleted);
        }
        finally
        {
            await RemoveRunAsync(document, run);
            await document.CloseAsync();
            await CloudService.DisconnectAsync();
        }
    }

    [Fact]
    public async Task ThixMail_switched_off_in_MatCloud_keeps_its_settings_and_sends_them_when_it_is_on_again()
    {
        if (!Configured)
        {
            return;
        }

        using var data = new TestData();
        await SignInAsMailAsync("IT-MAILSET-403");
        var document = new MailDocument("E-Mail");
        var run = Guid.NewGuid().ToString("N")[..6];
        try
        {
            await TestData.UntilAsync(() => document.SettingsSyncStatus.State == SyncState.Idle, "the settings' sync");
            await SetAppEnabledAsync(CloudApps.Mail, false);
            try
            {
                document.Accounts.Save(new MailRule { Id = "rule-" + run, Name = "Abgeschaltet " + run, SubjectContains = "x", MarkRead = true });
                await TestData.UntilAsync(() => document.SettingsSyncStatus.State == SyncState.Forbidden, "403 for the settings' sync");
                Assert.Equal(1, document.Accounts.PendingCloudChanges);
                Assert.Single(document.Accounts.Rules, r => r.Id == "rule-" + run);
            }
            finally
            {
                await SetAppEnabledAsync(CloudApps.Mail, true);
            }

            // on again: the next round (a change here triggers it) sends what waited
            document.Accounts.Save(new MailCategory("Wieder an " + run, 2));
            await TestData.UntilAsync(() => document.Accounts.PendingCloudChanges == 0, "the waiting settings after ThixMail is on again");
        }
        finally
        {
            await RemoveRunAsync(document, run);
            await document.CloseAsync();
            await CloudService.DisconnectAsync();
        }
    }

    /// <summary>Removes what a run added (the instance's account is shared by all runs) and waits until MatCloud has the removals.</summary>
    private static async Task RemoveRunAsync(MailDocument document, string run)
    {
        foreach (var account in document.Accounts.Accounts.Where(a => a.Id.EndsWith(run, StringComparison.Ordinal)))
        {
            await document.RemoveAccountAsync(account.Id);
        }

        foreach (var signature in document.Accounts.Signatures.Where(s => s.Id.EndsWith(run, StringComparison.Ordinal)))
        {
            document.Accounts.RemoveSignature(signature.Id);
        }

        foreach (var rule in document.Accounts.Rules.Where(r => r.Id.EndsWith(run, StringComparison.Ordinal)))
        {
            document.Accounts.RemoveRule(rule.Id);
        }

        foreach (var template in document.Accounts.Templates.Where(t => t.Name.EndsWith(run, StringComparison.Ordinal)))
        {
            document.Accounts.RemoveTemplate(template.Id);
        }

        foreach (var category in document.Accounts.Categories.Where(c => c.Name.EndsWith(run, StringComparison.Ordinal)))
        {
            document.Accounts.RemoveCategory(category.Name);
        }

        if (CloudService.IsConnected)
        {
            await TestData.UntilAsync(() => document.Accounts.PendingCloudChanges == 0, "the removals in MatCloud");
        }
    }
}
