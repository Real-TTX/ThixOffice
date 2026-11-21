using Avalonia.Threading;
using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using ThixContacts.Core;
using ThixContacts.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Core.Platform;
using ThixOffice.Pim.Rules;
using ThixOffice.Pim.Store;

namespace ThixContacts;

/// <summary>
/// ThixContacts' part of "--self-test" (the published, trimmed program): the main window with the contacts of the store (SQLite with its native library),
/// create, edit, search, undo, a vCard round trip with a picture, a CSV export, the store on disk read again, and the sync against a ThixCloud in memory
/// (two stores of one account, both ways).
/// </summary>
public static class ContactsSelfTest
{
    public static async Task<string> RunAsync(App app)
    {
        var window = app.NewDocumentWindow();
        await Settle();
        var editor = (ContactsEditor)window.Editor;
        var session = editor.Session;

        // create, edit, search, undo
        var id = session.Save(null, new ContactData { GivenName = "Selbst", FamilyName = "Test", Emails = [new ContactValue(ContactLabels.Work, "selbst@test.example")] });
        session.Save(id, session.Find(id)!.Data with { Company = "Prüfstand GmbH" });
        editor.Refresh();
        Ensure(editor.Shown.Any(p => p.Id == id), "the new contact is not in the list");
        Ensure(session.Query(new ContactQuery("prufstand")).Single().Id == id, "the search does not find the contact by its company");
        try
        {
            session.Save(null, new ContactData { Notes = "nur eine Notiz" });
            throw new InvalidOperationException("a contact without a name was saved");
        }
        catch (ThixOffice.Core.Models.BusinessException exception) when (exception.Field == nameof(ContactData.GivenName))
        {
        }

        session.Delete([id]);
        session.Undo();
        Ensure(session.Find(id) is not null, "undo did not bring the contact back");

        // a vCard with a picture, back and forth; CSV
        var png = Convert.FromBase64String("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mNk+M9QDwADhgGAWjR9awAAAABJRU5ErkJggg==");
        var photo = session.AddPhoto(png);
        session.Save(id, session.Find(id)!.Data with { PhotoId = photo });
        var vcard = VCard.Write(session.Export([id]), 3);
        var read = VCard.Read(vcard).Single();
        Ensure(read.Data.Company == "Prüfstand GmbH" && read.Photo?.SequenceEqual(png) == true, "the vCard round trip lost the company or the picture");
        var csv = System.Text.Encoding.UTF8.GetString(PersonCsv.Write([read.Data]));
        Ensure(csv.Contains("Prüfstand GmbH", StringComparison.Ordinal), "the CSV export lost the company");

        // the store on disk
        var path = session.Store.Path;
        using (var again = PimStore.Open(path))
        {
            Ensure(PersonItems.People(again).Any(p => p.Id == id && p.Data.PhotoId == photo), "the store on disk does not have the contact");
        }

        // the sync with a ThixCloud in memory
        var cloud = new SampleCloud { App = CloudApps.Contacts };
        CloudService.HandlerForTests = () => cloud;
        var note = "";
        try
        {
            await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "SELBSTTEST");
            var document = editor.Document;
            await document.ReconnectAsync();
            var report = await document.SyncNowAsync();
            Ensure(cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts).Count == document.Session.Count && cloud.Photo(photo) is not null,
                $"the contacts did not go up ({report.Pushed} pushed)");
            cloud.PushElsewhere(CloudApps.Contacts, ContactsContract.Contacts, "from-elsewhere", ContactsContract.Serialize(new ContactData { GivenName = "Von", FamilyName = "Woanders" }), DateTimeOffset.UtcNow);
            await document.SyncNowAsync();
            Ensure(document.Session.Find("from-elsewhere") is not null, "a contact of another device did not come down");
            note = $"{document.Session.Count} contacts synced, {cloud.Requests.Count} requests";
        }
        finally
        {
            await CloudService.DisconnectAsync();
            CloudService.HandlerForTests = null;
        }

        window.Close();
        await Settle();
        return note;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task Settle()
    {
        for (var round = 0; round < 5; round++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(40);
        }
    }
}
