using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using static ThixMail.Core.Tests.TestData;

namespace ThixMail.Core.Tests;

/// <summary>The sync engine against the server in memory.</summary>
public sealed class SyncTests
{
    [Fact]
    public async Task The_first_sync_lists_the_folders_with_their_roles_and_fetches_the_headers()
    {
        using var data = new TestData();
        data.Server.AddFolder(Address, "Projekte");
        data.Server.Deliver(Address, "INBOX", Jonas, "Angebot", "Hier das Angebot.");
        data.Server.Deliver(Address, "INBOX", Jonas, "Rechnung", "Die Rechnung.", flags: MessageFlags.Seen);
        data.Server.Deliver(Address, "Sent", new MailAddress("Anna", Address), "Gesendet", "x", flags: MessageFlags.Seen);
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;

        var report = await sync.SyncOnceAsync();

        Assert.Equal(3, report.NewMessages);
        var folders = store.Folders(Account.Id);
        Assert.Equal(FolderRole.Inbox, folders.Single(f => f.Path == "INBOX").Role);
        Assert.Equal("Gesendete Elemente", folders.Single(f => f.Path == "Sent").DisplayName);
        Assert.Equal(FolderRole.None, folders.Single(f => f.Path == "Projekte").Role);
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        Assert.Equal(2, inbox.Total);
        Assert.Equal(1, inbox.Unread);
        var headers = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]);
        Assert.Contains(headers, h => h.Subject == "Angebot" && h.From.Address == Jonas.Address && h.Preview.StartsWith("Hier das Angebot", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task New_mail_and_flags_changed_at_the_server_arrive_with_and_without_CONDSTORE(bool condStore)
    {
        using var data = new TestData();
        data.Server.SupportsCondStore = condStore;
        var uid = data.Server.Deliver(Address, "INBOX", Jonas, "Erste", "1");
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();

        data.Server.Deliver(Address, "INBOX", Jonas, "Zweite", "2");
        data.Server.SetFlags(Address, "INBOX", uid, MessageFlags.Seen | MessageFlags.Flagged);
        var report = await sync.SyncOnceAsync();

        Assert.Equal(1, report.NewMessages);
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        var first = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]).Single(h => h.Subject == "Erste");
        Assert.True(first.IsRead);
        Assert.True(first.IsFlagged);
    }

    [Fact]
    public async Task Flags_set_here_go_to_the_server_through_the_queue_and_undo_follows()
    {
        using var data = new TestData();
        var uid = data.Server.Deliver(Address, "INBOX", Jonas, "Lesen", "x");
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var id = store.List(new MessageQuery([store.FolderByRole(Account.Id, FolderRole.Inbox)!.Id])).Single().Id;

        session.SetRead([id], true);
        session.SetFlagged([id], true);
        session.ToggleCategory([id], "Projekt");
        Assert.True(store.Header(id)!.IsRead); // at once, before the server knows
        Assert.Equal(3, store.PendingOperations().Count);
        await sync.SyncOnceAsync();

        Assert.Empty(store.PendingOperations());
        Assert.Equal(MessageFlags.Seen | MessageFlags.Flagged, data.Server.FlagsOf(Address, "INBOX", uid));
        Assert.Contains("Projekt", data.Server.KeywordsOf(Address, "INBOX", uid));

        session.Undo(); // the category
        session.Undo(); // the flag
        await sync.SyncOnceAsync();
        Assert.Equal(MessageFlags.Seen, data.Server.FlagsOf(Address, "INBOX", uid));
        Assert.DoesNotContain("Projekt", data.Server.KeywordsOf(Address, "INBOX", uid));
    }

    [Fact]
    public async Task Delete_moves_into_the_trash_and_undo_before_the_sync_takes_it_back()
    {
        using var data = new TestData();
        data.Server.Deliver(Address, "INBOX", Jonas, "Weg damit", "x");
        data.Server.Deliver(Address, "INBOX", Jonas, "Bleibt", "y");
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        var trash = store.FolderByRole(Account.Id, FolderRole.Trash)!;
        var doomed = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]).Single(h => h.Subject == "Weg damit").Id;

        session.Delete([doomed]);
        Assert.Equal(trash.Id, store.Header(doomed)!.FolderId);
        session.Undo();
        Assert.Equal(inbox.Id, store.Header(doomed)!.FolderId);
        Assert.Empty(store.PendingOperations());
        await sync.SyncOnceAsync();
        Assert.Equal(2, data.Server.UidsOf(Address, "INBOX").Count);

        session.Delete([doomed]);
        await sync.SyncOnceAsync();
        Assert.Equal(["Weg damit"], data.Server.SubjectsOf(Address, "Trash"));
        Assert.Single(store.List(new MessageQuery([trash.Id])));
        Assert.True(store.RawUid(doomed) > 0); // the server told the new UID

        // in the trash: for good
        Assert.True(session.DeletesForGood([doomed]));
        session.Delete([doomed]);
        await sync.SyncOnceAsync();
        Assert.Empty(data.Server.UidsOf(Address, "Trash"));
        Assert.Null(store.Header(doomed));
    }

    [Fact]
    public async Task A_move_after_the_sync_is_undone_by_moving_back()
    {
        using var data = new TestData();
        data.Server.AddFolder(Address, "Projekte");
        data.Server.Deliver(Address, "INBOX", Jonas, "Ablegen", "x");
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        var projects = store.FolderByPath(Account.Id, "Projekte")!;
        var id = store.List(new MessageQuery([inbox.Id])).Single().Id;

        session.Move([id], projects.Id);
        await sync.SyncOnceAsync();
        Assert.Equal(["Ablegen"], data.Server.SubjectsOf(Address, "Projekte"));

        session.Undo();
        await sync.SyncOnceAsync();
        Assert.Equal(["Ablegen"], data.Server.SubjectsOf(Address, "INBOX"));
        Assert.Empty(data.Server.UidsOf(Address, "Projekte"));
        Assert.Single(store.List(new MessageQuery([inbox.Id])));
    }

    [Fact]
    public async Task A_message_expunged_elsewhere_leaves_the_store_and_a_new_UIDVALIDITY_fetches_the_folder_again()
    {
        using var data = new TestData();
        var gone = data.Server.Deliver(Address, "INBOX", Jonas, "Gelöscht", "x");
        data.Server.Deliver(Address, "INBOX", Jonas, "Bleibt", "y");
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;

        data.Server.Expunge(Address, "INBOX", gone);
        var report = await sync.SyncOnceAsync();
        Assert.Equal(1, report.Removed);
        Assert.Single(store.List(new MessageQuery([inbox.Id])));

        data.Server.ResetUidValidity(Address, "INBOX");
        await sync.SyncOnceAsync();
        var after = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]);
        Assert.Equal(["Bleibt"], after.Select(h => h.Subject));
        Assert.Equal(data.Server.UidsOf(Address, "INBOX"), store.Uids(inbox.Id));
    }

    [Fact]
    public async Task Offline_actions_wait_in_the_queue_and_are_replayed_in_order()
    {
        using var data = new TestData();
        data.Server.AddFolder(Address, "Projekte");
        var a = data.Server.Deliver(Address, "INBOX", Jonas, "A", "a");
        data.Server.Deliver(Address, "INBOX", Jonas, "B", "b");
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        var headers = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]);
        var idA = headers.Single(h => h.Subject == "A").Id;
        var idB = headers.Single(h => h.Subject == "B").Id;

        data.Server.IsReachable = false;
        session.SetRead([idA], true);
        session.Move([idB], store.FolderByPath(Account.Id, "Projekte")!.Id);
        session.CreateFolder(Account.Id, null, "Neu");
        await Assert.ThrowsAsync<ThixMail.Core.Transport.MailConnectionException>(() => sync.SyncOnceAsync());
        Assert.Equal(3, store.PendingOperations().Count);

        data.Server.IsReachable = true;
        var report = await sync.SyncOnceAsync();
        Assert.Equal(3, report.OperationsReplayed);
        Assert.Equal(MessageFlags.Seen, data.Server.FlagsOf(Address, "INBOX", a));
        Assert.Equal(["B"], data.Server.SubjectsOf(Address, "Projekte"));
        Assert.NotNull(store.FolderByPath(Account.Id, "Neu"));

        // a replay of the same flags (an answer lost) changes nothing
        store.Enqueue(Account.Id, OperationKind.Flags, new OperationData { Folder = "INBOX", Uids = [a], Add = (int)MessageFlags.Seen });
        await sync.SyncOnceAsync();
        Assert.Equal(MessageFlags.Seen, data.Server.FlagsOf(Address, "INBOX", a));
    }

    [Fact]
    public async Task Sending_goes_through_the_outbox_with_a_copy_in_Sent_and_the_original_marked_answered()
    {
        using var data = new TestData();
        var original = data.Server.Deliver(Address, "INBOX", Jonas, "Frage", "Kommst du?", messageId: "<frage@example.de>");
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        var id = store.List(new MessageQuery([inbox.Id])).Single().Id;
        var mail = MailReader.Read((await sync.FetchBodyAsync(id, CancellationToken.None))!);

        var reply = MailComposer.Reply(mail, Account, all: false, id) with { Html = "<p>Ja, gern.</p>" + MailComposer.QuoteHtml(mail), Text = "Ja, gern." };
        session.Send(reply);
        Assert.Single(store.Outbox());
        var report = await sync.SyncOnceAsync();

        Assert.Equal(1, report.Sent);
        Assert.Empty(store.Outbox());
        var (from, to, raw) = Assert.Single(data.Server.Sent);
        Assert.Equal(Address, from.Address);
        Assert.Equal(Jonas.Address, Assert.Single(to).Address);
        var sent = MailReader.Read(raw);
        Assert.Equal("AW: Frage", sent.Subject);
        Assert.Contains("frage@example.de", sent.References);
        await sync.SyncOnceAsync(); // the queued copy into Sent
        Assert.Equal(["AW: Frage"], data.Server.SubjectsOf(Address, "Sent"));
        Assert.True(data.Server.FlagsOf(Address, "INBOX", original).HasFlag(MessageFlags.Answered));
        var sentFolder = store.FolderByRole(Account.Id, FolderRole.Sent)!;
        Assert.Single(store.List(new MessageQuery([sentFolder.Id]))); // the placeholder took the server's UID, no duplicate
        Assert.Contains(store.SuggestRecipients("jon"), r => r.Address == Jonas.Address);
    }

    [Fact]
    public async Task A_message_sent_offline_waits_and_a_refused_recipient_stays_in_the_outbox_without_retries()
    {
        using var data = new TestData();
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();

        data.Server.IsReachable = false;
        session.Send(new Draft { AccountId = Account.Id, To = [Jonas], Subject = "Offline geschrieben", Html = "<p>Hallo</p>" });
        await Assert.ThrowsAsync<ThixMail.Core.Transport.MailConnectionException>(() => sync.SyncOnceAsync());
        Assert.Single(store.Outbox());
        data.Server.IsReachable = true;
        await sync.SyncOnceAsync();
        Assert.Empty(store.Outbox());
        Assert.Single(data.Server.Sent);

        Assert.Throws<ThixOffice.Core.Models.BusinessException>(() => session.Send(new Draft { AccountId = Account.Id, To = [new MailAddress("", "kaputt")], Subject = "x" }));
        Assert.Throws<ThixOffice.Core.Models.BusinessException>(() => session.Send(new Draft { AccountId = Account.Id, Subject = "Ohne Empfänger" }));
    }

    [Fact]
    public async Task Drafts_are_saved_into_Drafts_and_a_newer_save_replaces_the_older()
    {
        using var data = new TestData();
        var (store, accounts, sync, session) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var drafts = store.FolderByRole(Account.Id, FolderRole.Drafts)!;

        var draft = session.SaveDraft(new Draft { AccountId = Account.Id, To = [Jonas], Subject = "Entwurf 1", Html = "<p>eins</p>" });
        draft = session.SaveDraft(draft with { Subject = "Entwurf 2" }); // before the server saw the first
        await sync.SyncOnceAsync();
        Assert.Equal(["Entwurf 2"], data.Server.SubjectsOf(Address, "Drafts"));

        draft = session.SaveDraft(draft with { Subject = "Entwurf 3" });
        await sync.SyncOnceAsync();
        Assert.Equal(["Entwurf 3"], data.Server.SubjectsOf(Address, "Drafts"));
        Assert.Single(store.List(new MessageQuery([drafts.Id])));

        session.Send(draft);
        await sync.SyncOnceAsync();
        await sync.SyncOnceAsync();
        Assert.Empty(data.Server.UidsOf(Address, "Drafts"));
        Assert.Empty(store.List(new MessageQuery([drafts.Id])));
    }

    [Fact]
    public async Task Conversations_follow_References_and_In_Reply_To_and_the_subject_as_fallback()
    {
        using var data = new TestData();
        var day = DateTimeOffset.Now.AddDays(-2);
        data.Server.Deliver(Address, "INBOX", Jonas, "AW: Projekt", "Antwort", day.AddHours(2), messageId: "<b@x>", inReplyTo: "<a@x>"); // before its parent
        data.Server.Deliver(Address, "INBOX", Jonas, "Projekt", "Start", day, messageId: "<a@x>");
        data.Server.Deliver(Address, "INBOX", Jonas, "Re: Projekt", "ohne Verweise", day.AddHours(5), messageId: "<c@x>");
        data.Server.Deliver(Address, "INBOX", Jonas, "Anderes", "x", day, messageId: "<d@x>");
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();

        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        var headers = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]);
        var thread = headers.Single(h => h.MessageId == "a@x").ThreadId;
        Assert.Equal(thread, headers.Single(h => h.MessageId == "b@x").ThreadId);
        Assert.Equal(thread, headers.Single(h => h.MessageId == "c@x").ThreadId);
        Assert.NotEqual(thread, headers.Single(h => h.MessageId == "d@x").ThreadId);
        Assert.Equal(3, store.Conversation(thread).Count);

        var rows = MessageList.Build(store.List(new MessageQuery([inbox.Id])), conversations: true, dateGroups: true, new HashSet<long> { thread }, DateTimeOffset.Now);
        Assert.Equal(RowKind.Group, rows[0].Kind);
        var top = rows.Single(r => r.Kind == RowKind.Message && r.Thread == thread);
        Assert.Equal(3, top.Count);
        Assert.Equal(2, rows.Count(r => r.Kind == RowKind.Child));
    }

    [Fact]
    public async Task The_search_finds_words_in_headers_and_in_fetched_bodies()
    {
        using var data = new TestData();
        data.Server.Deliver(Address, "INBOX", Jonas, "Quartalszahlen", "Die Umsätze steigen.");
        data.Server.Deliver(Address, "INBOX", new MailAddress("Lena Koch", "lena@example.org"), "Sommerfest", "Wir grillen im Hof.");
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(Account.Id, FolderRole.Inbox)!;

        Assert.Single(store.List(new MessageQuery([inbox.Id], Search: "quartal")));
        Assert.Single(store.List(new MessageQuery([inbox.Id], Search: "lena")));
        Assert.Single(store.List(new MessageQuery([inbox.Id], Search: "umsatze"))); // diacritics folded, preview text indexed
        Assert.Empty(store.List(new MessageQuery([inbox.Id], Search: "nirgends")));

        var long1 = data.Server.Deliver(Address, "INBOX", Jonas, "Lang", "Kurzer Anfang. " + new string('x', 400) + " Geheimwort");
        await sync.SyncOnceAsync();
        Assert.Empty(store.List(new MessageQuery([inbox.Id], Search: "geheimwort")));
        var id = store.List(new MessageQuery([inbox.Id], Search: "lang")).Single().Id;
        await sync.FetchBodyAsync(id, CancellationToken.None);
        Assert.Single(store.List(new MessageQuery([inbox.Id], Search: "geheimwort")));
        Assert.True(long1 > 0);
    }

    [Fact]
    public async Task The_loop_waits_in_IDLE_and_wakes_up_for_new_mail_with_a_notification()
    {
        using var data = new TestData();
        data.Server.Deliver(Address, "INBOX", Jonas, "Alt", "x");
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        var notified = new List<NewMailEventArgs>();
        sync.NewMail += (_, e) => notified.Add(e);
        sync.Start();
        await UntilAsync(() => sync.Status.Phase == SyncPhase.Idle, "the first round");

        data.Server.Deliver(Address, "INBOX", Jonas, "Neu!", "frisch");
        await UntilAsync(() => store.MessageCount == 2, "the new message");
        await UntilAsync(() => notified.Count == 1, "the notification");
        Assert.Equal("Neu!", Assert.Single(notified[0].Messages).Subject);
        await sync.StopAsync();
        Assert.Equal(SyncPhase.Off, sync.Status.Phase);
    }

    [Fact]
    public async Task The_loop_reconnects_after_a_broken_connection_and_stops_at_a_wrong_password()
    {
        using var data = new TestData();
        var (store, accounts, sync, _) = data.Open();
        using var _s = store;
        using var _a = accounts;
        data.Server.FailNext(3);
        sync.Start();
        await UntilAsync(() => sync.Status.Phase == SyncPhase.Idle, "a round after the failures");
        Assert.True(data.Server.Connections >= 1);
        await sync.StopAsync();

        ThixMail.Core.Accounts.AccountService.SetPassword(accounts, Account.Id, ThixMail.Core.Accounts.AccountService.ImapSecret, "falsch");
        sync.Start();
        await UntilAsync(() => sync.Status.Phase == SyncPhase.SignInFailed, "the refused sign-in");
        var connections = data.Server.Connections;
        await Task.Delay(300);
        Assert.Equal(connections, data.Server.Connections); // no retries by itself
        await sync.StopAsync();
    }

    [Fact]
    public void Two_hundred_thousand_messages_list_and_group_quickly()
    {
        using var data = new TestData();
        using var store = MailStore.Open(Path.Combine(data.Folder, "big.db"));
        store.SyncFolders("a", [new ThixMail.Core.Transport.RemoteFolder("INBOX", "INBOX", null, '/', FolderRole.Inbox, true)]);
        var inbox = store.FolderByRole("a", FolderRole.Inbox)!;
        var start = DateTimeOffset.Now.AddDays(-400);
        for (var batch = 0; batch < 200; batch++)
        {
            var messages = Enumerable.Range(batch * 1000 + 1, 1000).Select(uid => new ThixMail.Core.Transport.RemoteMessage
            {
                Uid = (uint)uid,
                MessageId = $"m{uid}@x",
                Subject = $"Nachricht {uid}",
                From = new MailAddress("Absender", $"a{uid % 50}@example.de"),
                Date = start.AddMinutes(uid * 3),
                Flags = uid % 3 == 0 ? MessageFlags.Seen : MessageFlags.None,
            }).ToList();
            store.AddMessages(inbox.Id, "a", messages);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        var entries = store.List(new MessageQuery([inbox.Id]));
        var listed = watch.Elapsed;
        watch.Restart();
        var rows = MessageList.Build(entries, conversations: true, dateGroups: true, new HashSet<long>(), DateTimeOffset.Now);
        var built = watch.Elapsed;
        watch.Restart();
        var unread = store.List(new MessageQuery([inbox.Id], ListFilter.Unread));
        var found = store.List(new MessageQuery([inbox.Id], Search: "Nachricht 19999"));
        var filtered = watch.Elapsed;

        Assert.Equal(200_000, entries.Count);
        Assert.True(rows.Count > 200_000);
        Assert.Equal(133_334, unread.Count);
        Assert.NotEmpty(found);
        Assert.True(listed < TimeSpan.FromSeconds(3), $"listing took {listed}");
        Assert.True(built < TimeSpan.FromSeconds(1), $"grouping took {built}");
        Assert.True(filtered < TimeSpan.FromSeconds(4), $"filter and search took {filtered}");
    }
}
