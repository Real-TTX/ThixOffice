using System.Diagnostics;
using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using ThixContacts.Core;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using ThixOffice.Pim.Rules;
using ThixOffice.Pim.Store;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace ThixContacts.Core.Tests;

/// <summary>A data folder of its own, an in-memory registry, no policies, secrets in memory, a ThixCloud in memory.</summary>
internal sealed class TestData : IDisposable
{
    public TestData()
    {
        AppInfo.Initialize(ThixContactsApp.Definition, typeof(ThixContactsApp).Assembly);
        Folder = Path.Combine(Path.GetTempPath(), "thixcontacts-core-tests", Guid.NewGuid().ToString("N"));
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

    public ContactsSession Session(string name = "contacts") => new(PimStore.Open(Path.Combine(Folder, name + ".db")));

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

/// <summary>The session of ThixContacts on a real store.</summary>
public sealed class SessionTests
{
    private static ContactData Anna => new() { GivenName = "Anna", FamilyName = "Müller", Emails = [new ContactValue(ContactLabels.Work, "anna@x.de")] };

    [Fact]
    public void A_contact_is_checked_kept_and_found_and_every_change_undoes()
    {
        using var data = new TestData();
        using var session = data.Session();

        var refused = Assert.Throws<BusinessException>(() => session.Save(null, new ContactData { Notes = "nur Notiz" }));
        Assert.Equal(nameof(ContactData.GivenName), refused.Field);
        Assert.Equal("Phones[0]", Assert.Throws<BusinessException>(() => session.Save(null, Anna with { Phones = [new ContactValue("mobile", "abc")] })).Field);

        var id = session.Save(null, Anna);
        session.Save(id, session.Find(id)!.Data with { JobTitle = "Chefin" });
        Assert.Equal("Chefin", session.Find(id)!.Data.JobTitle);
        Assert.Equal("Kontakt bearbeiten", session.UndoLabel);
        session.Undo();
        Assert.Equal("", session.Find(id)!.Data.JobTitle);
        session.Redo();
        Assert.Equal("Chefin", session.Find(id)!.Data.JobTitle);

        Assert.Equal(1, session.Delete([id, "nobody"]));
        Assert.Null(session.Find(id));
        session.Undo();
        Assert.NotNull(session.Find(id));
        Assert.Equal([id], session.Query(new ContactQuery("mull")).Select(p => p.Id));
        Assert.Equal(2, session.Store.DirtyCount + 1); // one item (the contact) waits to go up, written three times
    }

    [Fact]
    public void Groups_categories_favourites_and_the_filters_of_the_list()
    {
        using var data = new TestData();
        using var session = data.Session();
        var anna = session.Save(null, Anna);
        var bert = session.Save(null, new ContactData { GivenName = "Bert", Company = "Zeta AG" });
        var clara = session.Save(null, new ContactData { GivenName = "Clara", FamilyName = "Adler" });

        var team = session.CreateGroup("Team");
        Assert.Equal("Name", Assert.Throws<BusinessException>(() => session.CreateGroup(" team ")).Field);
        Assert.Equal(2, session.SetMembers(team, [anna, bert, "gone"], add: true));
        session.Categorize([anna, clara], "Kunde", add: true);
        session.SetFavorite([bert], true);

        Assert.Equal([anna, bert], session.Query(new ContactQuery(Filter: new ContactFilter(ContactFilterKind.Group, team))).Select(p => p.Id));
        Assert.Equal([anna, clara], session.Query(new ContactQuery(Filter: new ContactFilter(ContactFilterKind.Category, "kunde"))).Select(p => p.Id));
        Assert.Equal([bert], session.Query(new ContactQuery(Filter: ContactFilter.Favorites)).Select(p => p.Id));
        Assert.Equal([clara, bert, anna], session.Query(new ContactQuery(Sort: PersonSort.FamilyName)).Select(p => p.Id)); // no family name: the given name sorts
        Assert.Equal(["anna@x.de"], session.GroupAddresses(team));

        session.RenameGroup(team, "Projektteam");
        session.DeleteGroup(team);
        Assert.Empty(session.Groups);
        session.Undo();
        Assert.Equal("Projektteam", session.Groups.Single().Name);
        session.Delete([anna]);
        Assert.Equal([bert], session.Groups.Single().Data.Members); // a member that is gone does not count
    }

    [Fact]
    public void Duplicates_are_merged_field_by_field_and_the_merge_undoes()
    {
        using var data = new TestData();
        using var session = data.Session();
        var first = session.Save(null, Anna);
        var second = session.Save(null, new ContactData { GivenName = "Anna", FamilyName = "Müller", JobTitle = "Chefin", Emails = [new ContactValue(ContactLabels.Home, "anna@privat.de")] });
        var group = session.CreateGroup("Team");
        session.SetMembers(group, [second], add: true);

        var set = Assert.Single(session.Duplicates());
        Assert.Equal(2, set.Count);
        var kept = session.Merge([second, first], new Dictionary<string, string> { ["title"] = second });
        Assert.Equal(second, kept);
        var merged = session.Find(kept)!.Data;
        Assert.Equal(("Chefin", 2), (merged.JobTitle, merged.Emails.Count));
        Assert.Null(session.Find(first));
        Assert.Empty(session.Duplicates());
        session.Undo();
        Assert.NotNull(session.Find(first));
        Assert.Single(session.Duplicates());
    }

    [Fact]
    public void Imports_keep_pictures_skip_duplicates_and_repair_values_and_exports_read_back()
    {
        using var data = new TestData();
        using var session = data.Session();
        session.Save(null, Anna);
        byte[] png = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A, 7, 7];
        var imported = new List<ImportedPerson>
        {
            new(Anna),
            new(new ContactData { GivenName = "Bert", Emails = [new ContactValue("work", "kaputt"), new ContactValue("work", "bert@y.de")] }, png, "image/png"),
            new(new ContactData { Notes = "ohne Namen" }),
        };

        var result = session.Import(imported, skipDuplicates: true);
        Assert.Equal(new ContactImportResult(1, 2), result);
        var bert = session.Query(new ContactQuery("bert")).Single();
        Assert.Equal(["bert@y.de"], bert.Data.Emails.Select(e => e.Value));
        Assert.Equal(png, session.Photo(bert.Data.PhotoId));

        var path = ContactFiles.Write(Path.Combine(data.Folder, "Kontakte"), session.Export(), ContactExportFormat.VCard3);
        Assert.EndsWith(".vcf", path, StringComparison.Ordinal);
        var file = ContactFiles.Read(path);
        Assert.False(file.IsCsv);
        Assert.Equal(2, file.Contacts!.Count);
        Assert.Equal(png, file.Contacts.Single(c => c.Data.GivenName == "Bert").Photo);

        var csv = ContactFiles.Write(Path.Combine(data.Folder, "Kontakte.csv"), session.Export(), ContactExportFormat.Csv);
        var table = ContactFiles.Read(csv).Table!;
        Assert.Equal(["Anna", "Bert"], PersonCsv.Import(table, PersonCsv.AutoMap(table.Columns)).Select(c => c.Data.GivenName).Order());
        Assert.Throws<BusinessException>(() => ContactFiles.Read(Path.Combine(data.Folder, "fehlt.vcf")));
        Assert.Throws<BusinessException>(() => session.AddPhoto("kein Bild"u8.ToArray()));
    }

    [Fact]
    public void Fifty_thousand_contacts_load_query_and_sort_quickly()
    {
        using var data = new TestData();
        using (var store = PimStore.Open(Path.Combine(data.Folder, "big.db")))
        {
            var people = Enumerable.Range(0, 50_000).Select(i => new ThixOffice.Pim.Model.Person(
                $"p{i}", new ContactData { GivenName = $"Vorname{i % 977}", FamilyName = $"Nachname{i}", Company = $"Firma {i % 311}", Emails = [new ContactValue("work", $"p{i}@example.com")] },
                DateTimeOffset.UtcNow));
            PersonItems.Write(store, people);
        }

        var watch = Stopwatch.StartNew();
        using var session = data.Session("big");
        var load = watch.ElapsedMilliseconds;
        Assert.Equal(50_000, session.Count);
        watch.Restart();
        var all = session.Query(new ContactQuery(Sort: PersonSort.FamilyName));
        var sorted = watch.ElapsedMilliseconds;
        watch.Restart();
        var found = session.Query(new ContactQuery("nachname4999"));
        var searched = watch.ElapsedMilliseconds;
        Assert.Equal(50_000, all.Count);
        Assert.Contains(found, p => p.Id == "p4999");
        Assert.True(load < 15_000 && sorted < 5_000 && searched < 3_000, $"load {load} ms, sort {sorted} ms, search {searched} ms");
    }

    [Fact]
    public async Task The_document_works_locally_and_syncs_with_the_account_after_the_sign_in()
    {
        using var data = new TestData();
        var document = new ContactsDocument("Kontakte");
        try
        {
            Assert.False(document.IsSyncing);
            var local = document.Session.Save(null, Anna);
            await Assert.ThrowsAsync<BusinessException>(() => document.SyncNowAsync());

            await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
            await document.ReconnectAsync();
            Assert.True(document.IsSyncing);
            Assert.NotNull(document.Session.Find(local)); // the local contacts went into the account's store
            await document.SyncNowAsync();
            Assert.Single(data.Cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts));

            data.Cloud.PushElsewhere(CloudApps.Contacts, ContactsContract.Contacts, "other", ContactsContract.Serialize(new ContactData { GivenName = "Von", FamilyName = "Woanders" }), DateTimeOffset.UtcNow);
            await document.SyncNowAsync();
            Assert.NotNull(document.Session.Find("other"));

            await CloudService.DisconnectAsync();
            await document.ReconnectAsync();
            Assert.False(document.IsSyncing);
            Assert.Null(document.Session.Find("other")); // back to the contacts of this computer
        }
        finally
        {
            await document.CloseAsync();
        }
    }
}
