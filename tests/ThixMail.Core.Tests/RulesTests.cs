using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixOffice.Core.Models;

namespace ThixMail.Core.Tests;

/// <summary>The client-side rules: validation, matching, the plan, "Regeln jetzt anwenden" as one undo step, and the rules on arrival before the notification.</summary>
public sealed class RulesTests
{
    private static MessageHeader Message(string from, string subject, bool attachment = false, string to = TestData.Address) => new()
    {
        Id = 1,
        FolderId = 10,
        AccountId = TestData.Account.Id,
        From = new MailAddress(from.Split('@')[0], from),
        To = [new MailAddress("", to)],
        Subject = subject,
        HasAttachments = attachment,
    };

    [Fact]
    public async Task A_rule_needs_a_name_a_condition_an_action_and_a_folder_of_its_account()
    {
        using var data = new TestData();
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await using (sync)
        {
            await sync.SyncOnceAsync();
        }

        var valid = new MailRule { Name = "Shop", FromContains = "shop", MarkRead = true };

        Assert.Equal(nameof(MailRule.Name), Assert.Throws<BusinessException>(() => MailRules.Validate(valid with { Name = " " }, store)).Field);
        Assert.Equal("Conditions", Assert.Throws<BusinessException>(() => MailRules.Validate(valid with { FromContains = "  " }, store)).Field);
        Assert.Equal("Actions", Assert.Throws<BusinessException>(() => MailRules.Validate(valid with { MarkRead = false }, store)).Field);
        Assert.Equal(nameof(MailRule.AccountId), Assert.Throws<BusinessException>(() => MailRules.Validate(valid with { MoveToFolder = "Archive" }, store)).Field);
        Assert.Equal(nameof(MailRule.MoveToFolder), Assert.Throws<BusinessException>(() => MailRules.Validate(valid with { AccountId = TestData.Account.Id, MoveToFolder = "Gibt es nicht" }, store)).Field);
        Assert.Equal("Actions", Assert.Throws<BusinessException>(() => MailRules.Validate(valid with { AccountId = TestData.Account.Id, MoveToFolder = "Archive", Delete = true }, store)).Field);
        Assert.Equal(nameof(MailRule.Category), Assert.Throws<BusinessException>(() => MailRules.Validate(valid with { Category = "zwei Worte" }, store)).Field);
        var normalized = MailRules.Validate(valid with { Name = " Shop ", SubjectContains = " " }, store);
        Assert.Equal("Shop", normalized.Name);
        Assert.Null(normalized.SubjectContains);

        accounts.Save(normalized);
        using var raw = ThixOffice.Pim.Store.PimStore.Open(Path.Combine(data.Folder, "settings.db"));
        Assert.Single(raw.Items(AccountStore.RulesCollection)); // a collection like the signatures, ready for the general sync
    }

    [Fact]
    public void All_set_conditions_must_hold_and_texts_match_without_case()
    {
        var rule = new MailRule { Name = "R", FromContains = "SHOP", SubjectContains = "rechnung", HasAttachment = true, Flag = true };
        Assert.True(MailRules.Matches(rule, Message("info@shop.example", "Ihre Rechnung 42", attachment: true)));
        Assert.False(MailRules.Matches(rule, Message("info@shop.example", "Ihre Rechnung 42", attachment: false)));
        Assert.False(MailRules.Matches(rule, Message("info@laden.example", "Ihre Rechnung 42", attachment: true)));
        Assert.False(MailRules.Matches(rule with { IsEnabled = false }, Message("info@shop.example", "Ihre Rechnung 42", attachment: true)));
        Assert.False(MailRules.Matches(rule with { AccountId = "anderes" }, Message("info@shop.example", "Ihre Rechnung 42", attachment: true)));
        Assert.True(MailRules.Matches(new MailRule { Name = "An", ToContains = "team@", Flag = true }, Message("a@b.example", "x", to: "team@firma.example")));
    }

    [Fact]
    public async Task Regeln_jetzt_anwenden_is_one_undo_step_and_the_first_move_ends_the_processing()
    {
        using var data = new TestData();
        data.Server.Deliver(TestData.Address, "INBOX", new MailAddress("Shop", "info@shop.example"), "Ihre Rechnung", "x");
        data.Server.Deliver(TestData.Address, "INBOX", new MailAddress("Shop", "info@shop.example"), "Newsletter", "x");
        data.Server.Deliver(TestData.Address, "INBOX", TestData.Jonas, "Treffen", "x");
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await using var _y = sync;
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(TestData.Account.Id, FolderRole.Inbox)!;
        var archive = store.FolderByRole(TestData.Account.Id, FolderRole.Archive)!;
        accounts.Save(new MailRule { Name = "Kategorie", Order = 0, FromContains = "shop", Category = "Einkauf" });
        accounts.Save(new MailRule { Name = "Rechnungen", Order = 1, AccountId = TestData.Account.Id, SubjectContains = "Rechnung", MoveToFolder = archive.Path, MarkRead = true });
        accounts.Save(new MailRule { Name = "Nie erreicht", Order = 2, SubjectContains = "Rechnung", Flag = true });
        accounts.Save(new MailRule { Name = "Aus", Order = 3, IsEnabled = false, FromContains = "jonas", Delete = true });

        Assert.Equal(2, session.ApplyRulesNow(inbox.Id));

        var moved = store.Headers([.. store.List(new MessageQuery([archive.Id])).Select(e => e.Id)]).Single();
        Assert.Equal("Ihre Rechnung", moved.Subject);
        Assert.True(moved.IsRead);
        Assert.False(moved.IsFlagged); // the move ended the processing
        Assert.Equal(["Einkauf"], moved.Categories);
        var left = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]);
        Assert.Equal(["Einkauf"], left.Single(h => h.Subject == "Newsletter").Categories);
        Assert.Empty(left.Single(h => h.Subject == "Treffen").Categories);
        Assert.Equal("Regeln anwenden", session.UndoLabel);

        session.Undo();
        Assert.Empty(store.List(new MessageQuery([archive.Id])));
        Assert.All(store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]), h => Assert.Empty(h.Categories));
        Assert.False(store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]).Single(h => h.Subject == "Ihre Rechnung").IsRead);
        Assert.False(session.CanUndo);

        // the server gets the result after the queue is replayed
        session.Redo();
        await sync.SyncOnceAsync();
        Assert.Single(data.Server.UidsOf(TestData.Address, "Archive"));
    }

    [Fact]
    public async Task New_mail_runs_through_the_rules_before_the_notification()
    {
        using var data = new TestData();
        var document = new MailDocument("E-Mail");
        try
        {
            var notified = new List<string>();
            document.NewMail += (_, e) =>
            {
                lock (notified)
                {
                    notified.AddRange(e.Messages.Select(m => m.Subject));
                }
            };
            document.AddAccount(TestData.Account, TestData.Password, null);
            var sync = document.SyncOf(TestData.Account.Id)!;
            await TestData.UntilAsync(() => sync.Status.Phase == SyncPhase.Idle, "the first round");
            var archive = document.Store.FolderByRole(TestData.Account.Id, FolderRole.Archive)!;
            document.Accounts.Save(MailRules.Validate(new MailRule { Name = "Newsletter", AccountId = TestData.Account.Id, FromContains = "news@", MoveToFolder = archive.Path, MarkRead = true }, document.Store));
            document.Accounts.Save(MailRules.Validate(new MailRule { Name = "Chef", FromContains = "jonas", Flag = true, Category = "Wichtig" }, document.Store));

            data.Server.Deliver(TestData.Address, "INBOX", new MailAddress("Newsletter", "news@laden.example"), "Angebote der Woche", "x");
            data.Server.Deliver(TestData.Address, "INBOX", TestData.Jonas, "Bitte bis Freitag", "x");
            sync.Trigger();

            await TestData.UntilAsync(() => document.Store.List(new MessageQuery([archive.Id])).Count == 1, "the newsletter moved by its rule");
            await TestData.UntilAsync(() => { lock (notified) { return notified.Count == 1; } }, "the notification");
            Assert.Equal(["Bitte bis Freitag"], notified); // the moved, read newsletter is not announced
            var inbox = document.Store.FolderByRole(TestData.Account.Id, FolderRole.Inbox)!;
            var flagged = document.Store.Headers([.. document.Store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]).Single(h => h.Subject == "Bitte bis Freitag");
            Assert.True(flagged.IsFlagged);
            Assert.Equal(["Wichtig"], flagged.Categories);
            Assert.False(document.Session.CanUndo); // rules on arrival are not the person's steps
            await TestData.UntilAsync(() => data.Server.UidsOf(TestData.Address, "Archive").Count == 1, "the move at the server");
        }
        finally
        {
            await document.CloseAsync();
        }
    }
}
