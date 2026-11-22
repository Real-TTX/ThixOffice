using ThixMail.Core.Accounts;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixMail.Core.Transport;
using ThixOffice.Core.Models;

namespace ThixMail.Core.Tests;

/// <summary>Später senden, Vorlagen, Verschieben zwischen Konten.</summary>
public sealed class EverydayTests
{
    [Fact]
    public async Task A_message_sent_later_waits_in_the_outbox_until_its_time()
    {
        using var data = new TestData();
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await using var _y = sync;
        await sync.SyncOnceAsync();
        var draft = new Draft { AccountId = TestData.Account.Id, To = [TestData.Jonas], Subject = "Morgen früh", Html = "<p>Guten Morgen</p>" };

        Assert.Equal("SendAt", Assert.Throws<BusinessException>(() => session.Send(draft, DateTimeOffset.Now.AddMinutes(-5))).Field);
        var id = session.Send(draft, DateTimeOffset.Now.AddHours(10));
        await sync.SyncOnceAsync();
        Assert.Empty(data.Server.Sent);
        var waiting = Assert.Single(store.Outbox());
        Assert.True(waiting.IsWaitingForItsTime(DateTimeOffset.Now));
        Assert.True(waiting.IsWaitingForItsTime(DateTimeOffset.Now.AddHours(9)));
        Assert.False(waiting.IsWaitingForItsTime(DateTimeOffset.Now.AddHours(11)));

        // a message sent now goes, the one for later stays
        session.Send(draft with { Subject = "Jetzt", MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId("example.de") });
        await sync.SyncOnceAsync();
        Assert.Single(data.Server.Sent);
        Assert.Equal(id, Assert.Single(store.Outbox()).Id);
    }

    [Fact]
    public void Templates_are_kept_by_name_in_their_own_collection()
    {
        using var data = new TestData();
        var (store, accounts, _, _) = data.Open();
        using var _s = store;
        using var _a = accounts;

        var first = accounts.SaveTemplate(" Angebot ", "Ihr Angebot", "<p>Sehr geehrte Damen und Herren,</p>", "Sehr geehrte Damen und Herren,");
        var again = accounts.SaveTemplate("angebot", "Ihr Angebot (neu)", "<p>x</p>", "x");
        Assert.Equal(first.Id, again.Id); // the same name replaces it
        Assert.Equal("Ihr Angebot (neu)", Assert.Single(accounts.Templates).Subject);
        Assert.Throws<BusinessException>(() => accounts.SaveTemplate(" ", "", "", ""));
        using var raw = ThixOffice.Pim.Store.PimStore.Open(Path.Combine(data.Folder, "settings.db"));
        Assert.Single(raw.Items(AccountStore.TemplatesCollection));
        accounts.RemoveTemplate(first.Id);
        Assert.Empty(accounts.Templates);
    }

    [Fact]
    public async Task Moving_to_another_account_copies_there_and_puts_the_original_into_its_trash()
    {
        using var data = new TestData();
        const string other = "anna.privat@example.de";
        data.Server.AddMailbox(other, "privat");
        data.Server.Deliver(TestData.Address, "INBOX", TestData.Jonas, "Urlaubsfotos", "Anbei die Fotos", flags: MessageFlags.Seen | MessageFlags.Flagged);
        var document = new MailDocument("E-Mail");
        try
        {
            document.AddAccount(TestData.Account, TestData.Password, null);
            var second = document.AddAccount(TestData.Account with { Id = "privat", Name = "Privat", Address = other, ImapUser = other, SmtpUser = other }, "privat", null);
            await TestData.UntilAsync(() => document.Syncs.All(s => s.Status.Phase == SyncPhase.Idle), "both accounts synced");
            var inbox = document.Store.FolderByRole(TestData.Account.Id, FolderRole.Inbox)!;
            var message = document.Store.List(new MessageQuery([inbox.Id])).Single().Id;
            var target = document.Store.FolderByRole(second.Id, FolderRole.Archive)!;

            Assert.Throws<BusinessException>(() => document.Session.Move([message], target.Id)); // the undoable move stays within an account
            Assert.Equal(1, await document.Session.MoveToAccountAsync([message], target.Id, document.BodyAsync, CancellationToken.None));

            var copy = document.Store.Headers([.. document.Store.List(new MessageQuery([target.Id])).Select(e => e.Id)]).Single();
            Assert.Equal("Urlaubsfotos", copy.Subject);
            Assert.True(copy.IsRead && copy.IsFlagged);
            Assert.Contains("Anbei die Fotos", MailReader.Read(document.Store.Raw(copy.Id)!).Text, StringComparison.Ordinal);
            var trash = document.Store.FolderByRole(TestData.Account.Id, FolderRole.Trash)!;
            Assert.Equal(trash.Id, document.Store.Header(message)!.FolderId);
            await TestData.UntilAsync(() => data.Server.UidsOf(other, "Archive").Count == 1 && data.Server.UidsOf(TestData.Address, "Trash").Count == 1, "both servers");
            Assert.Equal(MessageFlags.Seen | MessageFlags.Flagged, data.Server.FlagsOf(other, "Archive", data.Server.UidsOf(other, "Archive")[0]) & (MessageFlags.Seen | MessageFlags.Flagged));
        }
        finally
        {
            await document.CloseAsync();
        }
    }
}
