using System.Net;
using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixOffice.Pim.Tests;

/// <summary>A PIM app for the tests: a workspace app with the cloud id of ThixContacts.</summary>
internal sealed class PimTestData : IDisposable
{
    public static OfficeAppDefinition Definition { get; } = new("thixpimtest", "ThixPimTest", "#8764B8", "Ein Testprogramm.",
        [new DocumentType(".vcf", "ThixPimTest.VCard", "vCard")])
    {
        Kind = OfficeAppKind.Workspace,
        CloudAppId = CloudApps.Contacts,
    };

    public PimTestData()
    {
        AppInfo.Initialize(Definition, typeof(PimTestData).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixoffice-pim-tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Folder);
        AppPaths.UseDirectoryForTests(Path.Combine(Folder, "data"));
        UserRegistry.UseForTests(new MemoryUserRegistry());
        AccessPolicy.UsePolicyReaderForTests(_ => false);
        TokenStore.UseMemoryForTests(true);
        SettingsService.ResetCache();
        CloudService.ResetForTests();
        Cloud = new SampleCloud { App = CloudApps.Contacts };
        CloudService.HandlerForTests = () => Cloud;
    }

    public string Folder { get; }

    public SampleCloud Cloud { get; }

    public string StorePath(string name) => Path.Combine(Folder, name + ".db");

    public async Task SignInAsync() => await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");

    public void Dispose()
    {
        CloudService.ResetForTests();
        TokenStore.UseMemoryForTests(false);
        AccessPolicy.UsePolicyReaderForTests(null);
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

/// <summary>The local store (SQLite, migrations, dirty items, pictures) and the sync client against the ThixCloud in memory.</summary>
public sealed class StoreAndSyncTests
{
    private static Person Anna(string id = "a1", string given = "Anna") => new(id, new ContactData { GivenName = given, FamilyName = "Müller", Emails = [new ContactValue("work", "anna@x.de")] }, DateTimeOffset.UtcNow);

    private static PimSync SyncOf(PimStore store) => new(store, CloudService.CreateClient);

    [Fact]
    public void A_new_store_has_every_migration_and_keeps_items_tombstones_and_pictures_on_disk()
    {
        using var data = new PimTestData();
        var path = data.StorePath("contacts");
        using (var store = PimStore.Open(path))
        {
            Assert.Equal(PimStore.CurrentVersion, store.SchemaVersion);
            PersonItems.Write(store, [Anna(), Anna("a2", "Bert")], deletedPeople: ["gone"]);
            store.PutPhoto("ab", [1, 2, 3], isUploaded: false);
            store.Cursor = 42;
        }

        using var again = PimStore.Open(path);
        Assert.Equal(["Anna", "Bert"], PersonItems.People(again).Select(p => p.Data.GivenName).Order());
        Assert.True(again.Get(ContactsContract.Contacts, "gone")!.IsDeleted);
        Assert.Equal(3, again.DirtyCount);
        Assert.Equal([1, 2, 3], again.Photo("ab"));
        Assert.Equal(["ab"], again.PhotosToUpload());
        Assert.Equal(42, again.Cursor);
    }

    [Fact]
    public void An_old_store_is_migrated_and_a_newer_one_is_refused()
    {
        using var data = new PimTestData();
        var path = data.StorePath("old");
        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = PimStore.Migrations[0] + "PRAGMA user_version = 1; INSERT INTO Item (Collection, Key, Data, UpdatedAt) VALUES ('contacts', 'k', '{\"givenName\":\"Alt\"}', 1);";
            command.ExecuteNonQuery();
        }

        using (var store = PimStore.Open(path))
        {
            Assert.Equal(PimStore.CurrentVersion, store.SchemaVersion);
            Assert.Equal("Alt", PersonItems.People(store).Single().Data.GivenName);
            Assert.NotNull(store.GetMeta("created"));
        }

        using (var connection = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={path};Pooling=False"))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version = 99";
            command.ExecuteNonQuery();
        }

        Assert.Throws<BusinessException>(() => PimStore.Open(path));
    }

    [Fact]
    public async Task Two_computers_sync_both_ways_with_the_last_writer_winning_and_deletions_travelling()
    {
        using var data = new PimTestData();
        await data.SignInAsync();
        using var pc = PimStore.Open(data.StorePath("pc"));
        using var mac = PimStore.Open(data.StorePath("mac"));
        await using var pcSync = SyncOf(pc);
        await using var macSync = SyncOf(mac);

        PersonItems.Write(pc, [Anna()]);
        var first = await pcSync.SyncOnceAsync();
        Assert.Equal(1, first.Pushed);
        Assert.Equal(0, pc.DirtyCount);
        await macSync.SyncOnceAsync();
        Assert.Equal("Anna", PersonItems.People(mac).Single().Data.GivenName);

        // both change it; the later change wins on both
        var older = DateTimeOffset.UtcNow.AddMinutes(-1);
        pc.Write([(ContactsContract.Contacts, "a1", ContactsContract.Serialize(Anna().Data with { JobTitle = "PC" }))], older);
        mac.Write([(ContactsContract.Contacts, "a1", ContactsContract.Serialize(Anna().Data with { JobTitle = "Mac" }))]);
        await macSync.SyncOnceAsync();
        var lost = await pcSync.SyncOnceAsync();
        Assert.Equal(1, lost.Conflicts);
        Assert.Equal("Mac", PersonItems.People(pc).Single().Data.JobTitle);
        Assert.Equal(0, pc.DirtyCount);

        // a change of another device while this one has a newer local change: the local one stays and goes up
        data.Cloud.PushElsewhere(CloudApps.Contacts, ContactsContract.Contacts, "a1", ContactsContract.Serialize(Anna().Data with { JobTitle = "Alt" }), DateTimeOffset.UtcNow.AddMinutes(-5));
        pc.Write([(ContactsContract.Contacts, "a1", ContactsContract.Serialize(Anna().Data with { JobTitle = "Neu" }))]);
        await pcSync.SyncOnceAsync();
        Assert.Equal("Neu", PersonItems.People(pc).Single().Data.JobTitle);
        Assert.Contains("Neu", data.Cloud.SyncItem(CloudApps.Contacts, ContactsContract.Contacts, "a1")!.Data);

        // a delete travels as a tombstone
        PersonItems.Write(mac, deletedPeople: ["a1"]);
        await macSync.SyncOnceAsync();
        await pcSync.SyncOnceAsync();
        Assert.Empty(PersonItems.People(pc));
        Assert.True(data.Cloud.SyncItem(CloudApps.Contacts, ContactsContract.Contacts, "a1")!.IsDeleted);
    }

    [Fact]
    public async Task Offline_changes_wait_in_the_store_and_go_up_later_and_a_removed_device_is_signed_out()
    {
        using var data = new PimTestData();
        await data.SignInAsync();
        using var store = PimStore.Open(data.StorePath("pc"));
        await using var sync = SyncOf(store);

        data.Cloud.IsOffline = true;
        PersonItems.Write(store, [Anna(), Anna("a2", "Bert")]);
        await Assert.ThrowsAnyAsync<Exception>(() => sync.SyncOnceAsync());
        Assert.Equal((SyncState.Offline, 2), (sync.Status.State, sync.Status.Pending));
        Assert.Equal(CloudState.Offline, CloudService.Status.State);

        data.Cloud.IsOffline = false;
        Assert.Equal(2, (await sync.SyncOnceAsync()).Pushed);
        Assert.Equal((SyncState.Idle, 0), (sync.Status.State, sync.Status.Pending));
        Assert.Equal(2, data.Cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts).Count);

        data.Cloud.SyncFailsWith = HttpStatusCode.Forbidden;
        await Assert.ThrowsAsync<CloudException>(() => sync.SyncOnceAsync());
        Assert.Equal(SyncState.Forbidden, sync.Status.State);

        data.Cloud.SyncFailsWith = null;
        data.Cloud.RemoveDevices();
        await Assert.ThrowsAsync<CloudException>(() => sync.SyncOnceAsync());
        Assert.Equal(SyncState.SignedOut, sync.Status.State);
        Assert.False(CloudService.IsConnected);
    }

    [Fact]
    public async Task Pictures_go_up_before_their_contact_and_come_down_on_the_other_computer()
    {
        using var data = new PimTestData();
        await data.SignInAsync();
        using var pc = PimStore.Open(data.StorePath("pc"));
        using var mac = PimStore.Open(data.StorePath("mac"));
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 1, 2, 3];
        var id = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png));
        pc.PutPhoto(id, png, isUploaded: false);
        PersonItems.Write(pc, [Anna() with { Data = Anna().Data with { PhotoId = id } }]);

        var report = await SyncOf(pc).SyncOnceAsync();
        Assert.Equal(1, report.Photos);
        Assert.Equal(png, data.Cloud.Photo(id));
        Assert.Empty(pc.PhotosToUpload());

        Assert.Equal(1, (await SyncOf(mac).SyncOnceAsync()).Photos);
        Assert.Equal(png, mac.Photo(id));
    }

    [Fact]
    public async Task The_loop_syncs_a_local_change_at_once_and_stops()
    {
        using var data = new PimTestData();
        await data.SignInAsync();
        using var store = PimStore.Open(data.StorePath("pc"));
        await using var sync = SyncOf(store);
        sync.Start();
        PersonItems.Write(store, [Anna()]);
        sync.Trigger();

        var until = DateTime.UtcNow.AddSeconds(10);
        while (data.Cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts).Count == 0 && DateTime.UtcNow < until)
        {
            await Task.Delay(50);
        }

        Assert.Single(data.Cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts));
        await sync.StopAsync();
        Assert.False(sync.IsRunning);
    }

    [Fact]
    public async Task The_local_contacts_go_into_the_first_account_store_and_up()
    {
        using var data = new PimTestData();
        using (var local = PimStores.Open("contacts"))
        {
            Assert.EndsWith("contacts-local.db", local.Path, StringComparison.Ordinal);
            PersonItems.Write(local, [Anna()]);
        }

        await data.SignInAsync();
        using var account = PimStores.Open("contacts");
        Assert.DoesNotContain("local", account.Path, StringComparison.Ordinal);
        Assert.Equal(1, account.DirtyCount);
        await SyncOf(account).SyncOnceAsync();
        Assert.Single(data.Cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts));
    }

    [Fact]
    public void Suggestions_find_names_and_addresses_favourites_first()
    {
        using var data = new PimTestData();
        using var store = PimStore.Open(data.StorePath("s"));
        PersonItems.Write(store,
        [
            Anna(),
            new Person("b", new ContactData { GivenName = "Andreas", FamilyName = "Brandt", IsFavorite = true, Emails = [new ContactValue("work", "andreas@y.de")] }, DateTimeOffset.UtcNow),
            new Person("c", new ContactData { GivenName = "Carla", Emails = [new ContactValue("work", "an.carla@z.de")] }, DateTimeOffset.UtcNow),
        ]);

        Assert.Equal(["andreas@y.de", "anna@x.de", "an.carla@z.de"], PersonItems.Suggest(store, "an").Select(s => s.Email));
        Assert.Equal(["anna@x.de"], PersonItems.Suggest(store, "mül").Select(s => s.Email));
    }
}
