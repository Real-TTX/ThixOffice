using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using ThixOffice.Core.Business;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixOffice.Pim.Tests;

/// <summary>
/// One address book for ThixMail and ThixContacts on the same computer: two connections on one store file (as two processes have them) see each other's
/// changes, write at the same time without failing, only one sync per file talks to the cloud (the lease), and the transitions local → account and
/// account → local ("Kopie behalten") neither lose nor double anything.
/// </summary>
public sealed class SharedStoreTests
{
    private static Person Person(string id, string given, DateTimeOffset? at = null) =>
        new(id, new ContactData { GivenName = given, FamilyName = "Müller", Emails = [new ContactValue("work", given.ToLowerInvariant() + "@x.de")] }, at ?? DateTimeOffset.UtcNow);

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        var until = DateTime.UtcNow.AddSeconds(10);
        while (!condition())
        {
            Assert.True(DateTime.UtcNow < until, "timed out waiting for " + what);
            await Task.Delay(25);
        }
    }

    [Fact]
    public void A_second_connection_sees_the_changes_of_the_first_and_never_its_own()
    {
        using var data = new PimTestData();
        var path = data.StorePath("shared");
        using var mail = PimStore.Open(path);
        using var contacts = PimStore.Open(path);
        using var session = new ThixContactsLike(contacts);

        Assert.False(contacts.CheckForOtherChanges());
        PersonItems.Write(mail, [Person("a1", "Anna")]);
        Assert.False(mail.CheckForOtherChanges()); // its own write
        Assert.True(contacts.CheckForOtherChanges());
        Assert.Equal(1, session.Reloads);
        Assert.Equal("Anna", Assert.Single(PersonItems.People(contacts)).Data.GivenName);
        Assert.False(contacts.CheckForOtherChanges()); // seen
    }

    [Fact]
    public async Task The_watch_reports_a_change_of_the_other_connection_by_itself()
    {
        using var data = new PimTestData();
        var path = data.StorePath("shared");
        using var mail = PimStore.Open(path);
        using var contacts = PimStore.Open(path);
        var changed = 0;
        contacts.RemoteChanged += (_, _) => Interlocked.Increment(ref changed);
        contacts.WatchOtherProcesses(TimeSpan.FromMilliseconds(50));

        PersonItems.Write(mail, [Person("a1", "Anna")]);
        await UntilAsync(() => Volatile.Read(ref changed) > 0, "the watch");
    }

    [Fact]
    public async Task Two_connections_writing_at_the_same_time_lose_nothing()
    {
        using var data = new PimTestData();
        var path = data.StorePath("shared");
        using var one = PimStore.Open(path);
        using var two = PimStore.Open(path);
        await Task.WhenAll(
            Task.Run(() =>
            {
                for (var i = 0; i < 150; i++)
                {
                    PersonItems.Write(one, [Person("one-" + i, "Eins")]);
                }
            }),
            Task.Run(() =>
            {
                for (var i = 0; i < 150; i++)
                {
                    PersonItems.Write(two, [Person("two-" + i, "Zwei")]);
                }
            }));

        Assert.Equal(300, PersonItems.People(one).Count);
        Assert.Equal(300, PersonItems.People(two).Count);
    }

    [Fact]
    public async Task Only_one_sync_per_file_talks_to_the_cloud_and_takes_up_the_changes_of_the_other_app_and_the_other_takes_over_when_it_stops()
    {
        using var data = new PimTestData();
        await data.SignInAsync();
        var previous = PimSync.LeaseRetry;
        PimSync.LeaseRetry = TimeSpan.FromMilliseconds(100);
        var path = data.StorePath("contacts-account");
        using var contactsStore = PimStore.Open(path);
        using var mailStore = PimStore.Open(path);
        contactsStore.WatchOtherProcesses(TimeSpan.FromMilliseconds(50));
        mailStore.WatchOtherProcesses(TimeSpan.FromMilliseconds(50));
        var contactsSync = new PimSync(contactsStore, CloudService.CreateClient);
        await using var mailSync = new PimSync(mailStore, CloudService.CreateClient, app: CloudApps.Contacts);
        try
        {
            contactsSync.Start();
            await UntilAsync(() => contactsSync.HoldsLease, "the first sync to take the lease");
            mailSync.Start();
            await UntilAsync(() => mailSync.Status.Message.Contains("anderen App", StringComparison.Ordinal), "the second sync to wait");
            Assert.False(mailSync.HoldsLease);

            // ThixMail writes: ThixContacts' sync (the holder) sees it through the file and takes it up
            PersonItems.Write(mailStore, [Person("m1", "Mara")]);
            await UntilAsync(() => data.Cloud.SyncItem(CloudApps.Contacts, ContactsContract.Contacts, "m1") is not null, "the holder to push the other app's change");

            // ThixContacts ends: ThixMail's sync takes over
            await contactsSync.DisposeAsync();
            await UntilAsync(() => mailSync.HoldsLease, "the second sync to take over");
            PersonItems.Write(mailStore, [Person("m2", "Mia")]);
            mailSync.Trigger();
            await UntilAsync(() => data.Cloud.SyncItem(CloudApps.Contacts, ContactsContract.Contacts, "m2") is not null, "the new holder to push");
        }
        finally
        {
            PimSync.LeaseRetry = previous;
            await contactsSync.DisposeAsync();
        }
    }

    [Fact]
    public async Task Local_items_go_into_an_account_once_newer_local_changes_follow_and_what_the_account_deleted_stays_deleted()
    {
        using var data = new PimTestData();
        var old = DateTimeOffset.UtcNow.AddDays(-2);
        using (var local = PimStores.Open("contacts"))
        {
            PersonItems.Write(local, [Person("a1", "Anna"), Person("b1", "Bert")]);
            local.Import(local.AllItems().Select(item => item with { UpdatedAt = old })); // made two days ago
        }

        await data.SignInAsync();
        using (var account = PimStores.Open("contacts"))
        {
            Assert.Equal(2, account.DirtyCount);
            Assert.All(account.Dirty(), item => Assert.True(item.UpdatedAt < DateTimeOffset.UtcNow.AddDays(-1))); // the local times stay
            await new PimSync(account, CloudService.CreateClient).SyncOnceAsync();
            PersonItems.Write(account, deletedPeople: ["b1"]); // deleted in the account
            await new PimSync(account, CloudService.CreateClient).SyncOnceAsync();
        }

        using (var account = PimStores.Open("contacts")) // opened again (the other app, a restart): nothing doubles, nothing comes back
        {
            Assert.Equal(0, account.DirtyCount);
            Assert.Equal(["a1"], PersonItems.People(account).Select(p => p.Id));
        }

        await CloudService.DisconnectAsync();
        using (var local = PimStores.Open("contacts")) // signed out: a contact made on this computer
        {
            PersonItems.Write(local, [Person("c1", "Clara")]);
        }

        await data.SignInAsync();
        using (var account = PimStores.Open("contacts"))
        {
            Assert.Equal(["a1", "c1"], PersonItems.People(account).Select(p => p.Id).Order());
            Assert.Equal(1, account.DirtyCount);
        }
    }

    [Fact]
    public async Task Keeping_a_copy_takes_what_only_the_local_store_had_into_the_account_and_replaces_the_open_local_file_in_place()
    {
        using var data = new PimTestData();
        await data.SignInAsync();
        using var account = PimStores.Open("contacts");
        PersonItems.Write(account, [Person("a1", "Anna"), Person("b1", "Bert")]);
        PersonItems.Write(account, deletedPeople: ["b1"]);

        // the other app, signed out, shows the local file and adds a contact there meanwhile
        using var otherApp = PimStore.Open(PimStores.LocalPath("contacts"));
        PersonItems.Write(otherApp, [Person("x1", "Xaver")]);

        PimStores.KeepLocalCopy("contacts", account);
        Assert.Equal(["a1", "x1"], PersonItems.People(account).Select(p => p.Id).Order()); // Xaver went into the account
        Assert.True(otherApp.CheckForOtherChanges());
        Assert.Equal(["a1", "x1"], PersonItems.People(otherApp).Select(p => p.Id).Order()); // the open file now holds the copy

        // signing in again later: nothing is doubled or uploaded again
        account.Dispose();
        using var again = PimStores.Open("contacts");
        Assert.Equal(["a1", "x1"], PersonItems.People(again).Select(p => p.Id).Order());
    }

    /// <summary>Counts the reloads a session would do (ThixContacts' session reloads on <see cref="PimStore.RemoteChanged"/>).</summary>
    private sealed class ThixContactsLike : IDisposable
    {
        private readonly PimStore _store;

        public ThixContactsLike(PimStore store)
        {
            _store = store;
            _store.RemoteChanged += OnRemoteChanged;
        }

        public int Reloads { get; private set; }

        public void Dispose() => _store.RemoteChanged -= OnRemoteChanged;

        private void OnRemoteChanged(object? sender, IReadOnlyCollection<string> collections) => Reloads++;
    }
}
