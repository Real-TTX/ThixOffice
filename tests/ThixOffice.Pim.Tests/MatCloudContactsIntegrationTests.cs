using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Store;
using ThixOffice.Pim.Sync;

namespace ThixOffice.Pim.Tests;

/// <summary>
/// The sync of contacts against a REAL MatCloud (a throwaway instance, see CLAUDE.md): THIXOFFICE_MATCLOUD_URL, THIXOFFICE_MATCLOUD_USER and
/// THIXOFFICE_MATCLOUD_PASSWORD name it; without them the tests do nothing. Two computers (two devices of ThixContacts with their own stores) sync
/// both ways – changes, a conflict, a deletion, a picture –, and a device of ThixMail reads and writes the contacts through the grant.
/// </summary>
public sealed class MatCloudContactsIntegrationTests
{
    private static string? Url => Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_URL");

    private static string? User => Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_USER");

    private static string? Password => Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_PASSWORD");

    [Fact]
    public async Task Two_computers_and_ThixMail_sync_contacts_through_a_real_MatCloud()
    {
        if (Url is null || User is null || Password is null)
        {
            return;
        }

        using var data = new PimTestData();
        var pcToken = await SignInAsync("IT-PC", CloudApps.Contacts);
        var macToken = await SignInAsync("IT-MAC", CloudApps.Contacts);
        var mailToken = await SignInAsync("IT-MAIL", CloudApps.Mail);
        using var pc = PimStore.Open(data.StorePath("pc"));
        using var mac = PimStore.Open(data.StorePath("mac"));
        await using var pcSync = new PimSync(pc, () => Client(pcToken));
        await using var macSync = new PimSync(mac, () => Client(macToken));

        var key = "it-" + Guid.NewGuid().ToString("N")[..8];
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var photo = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(png));
        pc.PutPhoto(photo, png, isUploaded: false);
        PersonItems.Write(pc, [new Person(key, new ContactData { GivenName = "Inge", FamilyName = "Test", PhotoId = photo, Emails = [new ContactValue("work", "inge@it.example")] }, DateTimeOffset.UtcNow)]);
        await pcSync.SyncOnceAsync();
        await macSync.SyncOnceAsync();
        var arrived = PersonItems.People(mac).Single(p => p.Id == key);
        Assert.Equal("Inge", arrived.Data.GivenName);
        Assert.Equal(png, mac.Photo(photo));

        // the Mac changes it later than the PC: the Mac wins on both
        pc.Write([(ContactsContract.Contacts, key, ContactsContract.Serialize(arrived.Data with { JobTitle = "PC" }))], DateTimeOffset.UtcNow.AddMinutes(-1));
        mac.Write([(ContactsContract.Contacts, key, ContactsContract.Serialize(arrived.Data with { JobTitle = "Mac" }))]);
        await macSync.SyncOnceAsync();
        Assert.Equal(1, (await pcSync.SyncOnceAsync()).Conflicts);
        Assert.Equal("Mac", PersonItems.People(pc).Single(p => p.Id == key).Data.JobTitle);

        // ThixMail reads it and writes one of its own (the grant), ThixContacts sees it
        using (var mail = Client(mailToken))
        {
            var changes = await mail.GetChangesAsync(CloudApps.Contacts, 0, 200, CancellationToken.None);
            Assert.Contains(changes.Items, item => item.Key == key);
            await mail.PushAsync(CloudApps.Contacts, new SyncPushRequest([new SyncItemDto
            {
                Collection = ContactsContract.Contacts, Key = key + "-mail", UpdatedAt = DateTimeOffset.UtcNow,
                Data = ContactsContract.Serialize(new ContactData { GivenName = "Aus", FamilyName = "ThixMail" }),
            }]), CancellationToken.None);
        }

        await pcSync.SyncOnceAsync();
        Assert.Contains(PersonItems.People(pc), p => p.Id == key + "-mail");

        // deletions travel
        PersonItems.Write(pc, deletedPeople: [key, key + "-mail"]);
        await pcSync.SyncOnceAsync();
        await macSync.SyncOnceAsync();
        Assert.DoesNotContain(PersonItems.People(mac), p => p.Id == key);

        // the app's own way: CloudService signs in with the password, the account's store syncs
        ThixOffice.Core.Business.CloudService.HandlerForTests = null;
        await ThixOffice.Core.Business.CloudService.ConnectAsync(Url, User, Password, "IT-APP");
        PersonItems.Write(mac, [new Person(key + "-app", new ContactData { GivenName = "Über", FamilyName = "Anmeldung" }, DateTimeOffset.UtcNow)]);
        await macSync.SyncOnceAsync();
        using var account = PimStores.Open("contacts");
        await using var accountSync = new PimSync(account, ThixOffice.Core.Business.CloudService.CreateClient);
        await accountSync.SyncOnceAsync();
        Assert.Contains(PersonItems.People(account), p => p.Id == key + "-app");
        PersonItems.Write(account, deletedPeople: [key + "-app"]);
        await accountSync.SyncOnceAsync();
        await ThixOffice.Core.Business.CloudService.DisconnectAsync();
    }

    private static async Task<string> SignInAsync(string device, string app)
    {
        using var client = new MatCloudClient(Url!, null, "ThixContacts/1.0.0 (windows)");
        var login = await client.LoginAsync(new DeviceLoginRequest(User!, Password!, device, "windows", "1.0.0", app), CancellationToken.None);
        return login.Token;
    }

    private static MatCloudClient Client(string token) => new(Url!, token, "ThixContacts/1.0.0 (windows)");
}
