using System.Net;
using MatCloud.Contract.Drive;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using static ThixOffice.Core.Tests.CloudTests;

namespace ThixOffice.Core.Tests;

/// <summary>
/// Documents in ThixDrive against a cloud in memory: browsing, the working copy and its sidecar, saving with If-Match, the conflict
/// (overwrite, copy), offline (saved here, uploaded later, never replaced by a download), quota, save as, recent entries, the names.
/// </summary>
public sealed class DriveTests
{
    [Fact]
    public async Task A_folder_lists_folders_first_in_the_chosen_order()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(SampleCloud.WithSampleDrive());

        var listing = await DriveService.ListAsync(null, DriveSort.Modified, descending: true);

        Assert.Equal(["Projekte", "Rechnungen", "Privat"], listing.Items.Take(3).Select(item => item.Name));
        Assert.Equal("Quartalsbericht.docx", listing.Items[3].Name);
        Assert.Equal(10 - 1, listing.Total); // the file in "Projekte" is not at the top
        var projects = listing.Items[0];
        var inside = await DriveService.ListAsync(projects.Id);
        Assert.Equal("Projektplan.docx", Assert.Single(inside.Items).Name);
        Assert.Equal("Projekte", Assert.Single(inside.Ancestors).Name);
        Assert.Contains(cloud.Requests, request => request.StartsWith("GET /api/v1/drive/items", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_type_filter_keeps_folders_and_the_apps_documents()
    {
        using var data = new TestData();
        await SignedInAsync(SampleCloud.WithSampleDrive());

        var listing = await DriveService.ListAsync(null);
        var shown = listing.Items.Where(item => DriveService.Matches(item, [new DocumentType(".docx", "x", "Word")])).Select(item => item.Name).ToList();

        Assert.Contains("Projekte", shown);
        Assert.Contains("Quartalsbericht.docx", shown);
        Assert.DoesNotContain("Budget 2027.xlsx", shown);
        Assert.Equal(listing.Items.Count, listing.Items.Count(item => DriveService.Matches(item, null)));
    }

    [Fact]
    public async Task Opening_downloads_a_working_copy_with_its_sidecar_and_a_recent_entry()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("Hallo aus der Cloud"));

        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));

        Assert.Equal("Hallo aus der Cloud", document.Content);
        Assert.Equal("Brief.tst", document.Title);
        Assert.StartsWith(AppPaths.DriveDirectory, document.FilePath, StringComparison.Ordinal);
        Assert.Equal(file.Id, document.Drive!.ItemId);
        Assert.Equal(file.ETag, document.Drive.ETag);
        Assert.Equal(DriveSyncState.Uploaded, document.DriveState);
        Assert.Equal("ThixDrive: /Brief.tst", document.LocationText);
        var sidecar = DriveService.ReadSidecar(Path.GetDirectoryName(document.FilePath!)!);
        Assert.Equal(file.Id, sidecar!.ItemId);
        Assert.False(sidecar.IsUploadPending);

        var recent = Assert.Single(RecentDocuments.List());
        Assert.True(recent.Document.IsInDrive);
        Assert.Equal(file.Id, recent.Document.Drive!.ItemId);
        Assert.Equal("ThixDrive › /", recent.Document.Folder);
        Assert.True(recent.Exists);
        Assert.DoesNotContain(document.FilePath!, RecentDocuments.RecentFolders().Select(folder => folder + Path.DirectorySeparatorChar + "Brief.tst"));
    }

    [Fact]
    public async Task Saving_uploads_a_new_version_with_the_ETag_it_was_based_on()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));
        document.Type("neu");

        var result = await DriveService.SaveAsync(document);

        Assert.Equal(DriveSaveOutcome.Uploaded, result.Outcome);
        Assert.Equal("neu", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal("neu", File.ReadAllText(document.FilePath!));
        Assert.False(document.IsDirty);
        Assert.Equal(cloud.Find(file.Id)!.ETag, document.Drive!.ETag);
        Assert.NotEqual(file.ETag, document.Drive.ETag);
        Assert.Equal(DriveSyncState.Uploaded, document.DriveState);

        document.Type("noch neuer");
        Assert.True((await DriveService.SaveAsync(document)).IsUploaded); // the new ETag is the base of the next save
    }

    [Fact]
    public async Task A_file_saved_in_between_is_a_conflict_that_overwrite_resolves()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));
        cloud.SaveElsewhere(file.Id, Utf8("von Bert"));
        document.Type("von Anna");

        var result = await DriveService.SaveAsync(document);

        Assert.Equal(DriveSaveOutcome.ChangedMeanwhile, result.Outcome);
        Assert.Contains("LAPTOP-BERT", result.Message, StringComparison.Ordinal);
        Assert.Equal(DriveSyncState.Conflict, document.DriveState);
        Assert.Equal("von Bert", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal("von Anna", File.ReadAllText(document.FilePath!)); // safe here
        Assert.False(document.IsDirty);

        var overwritten = await DriveService.OverwriteAsync(document, result.Current);

        Assert.True(overwritten.IsUploaded);
        Assert.Equal("von Anna", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal(DriveSyncState.Uploaded, document.DriveState);
    }

    [Fact]
    public async Task Save_as_copy_after_a_conflict_makes_a_new_file_and_keeps_the_other_version()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));
        cloud.SaveElsewhere(file.Id, Utf8("von Bert"));
        document.Type("von Anna");
        Assert.Equal(DriveSaveOutcome.ChangedMeanwhile, (await DriveService.SaveAsync(document)).Outcome);

        var copy = await DriveService.SaveCopyAsync(document);

        Assert.True(copy.IsUploaded);
        Assert.Equal("Brief (2).tst", document.Drive!.Name);
        Assert.NotEqual(file.Id, document.Drive.ItemId);
        Assert.Equal("von Anna", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(document.Drive.ItemId)));
        Assert.Equal("von Bert", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal("Brief (2).tst", document.Title);
        Assert.True(File.Exists(document.FilePath));
    }

    [Fact]
    public async Task Offline_the_document_is_saved_here_marked_and_uploaded_later_never_replaced_by_a_download()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));
        document.Type("offline geschrieben");
        cloud.IsOffline = true;

        var result = await DriveService.SaveAsync(document);

        Assert.Equal(DriveSaveOutcome.NotUploaded, result.Outcome);
        Assert.Contains("auf diesem Computer gespeichert", result.Message, StringComparison.Ordinal);
        Assert.Equal(DriveSyncState.NotUploaded, document.DriveState);
        Assert.Equal(CloudState.Offline, CloudService.Status.State);
        Assert.False(document.IsDirty);
        Assert.Equal("offline geschrieben", File.ReadAllText(document.FilePath!));
        Assert.True(DriveService.ReadSidecar(Path.GetDirectoryName(document.FilePath!)!)!.IsUploadPending);
        Assert.Single(DriveService.PendingUploads());

        // opened again while the changes wait: the working copy, not the Drive's older version
        cloud.IsOffline = false;
        var again = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));
        Assert.Equal("offline geschrieben", again.Content);
        Assert.Equal(DriveSyncState.NotUploaded, again.DriveState);

        var retried = await DriveService.UploadAsync(document);

        Assert.True(retried.IsUploaded);
        Assert.Equal("offline geschrieben", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Empty(DriveService.PendingUploads());
        Assert.Equal(CloudState.Online, CloudService.Status.State);
    }

    [Fact]
    public async Task A_recent_ThixDrive_document_opens_from_its_working_copy_when_the_cloud_is_away()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("zuletzt gesehen"));
        await DriveService.OpenAsync(file.Id, name => new TestDocument(name));
        var entry = Assert.Single(RecentDocuments.List()).Document;
        cloud.IsOffline = true;

        var document = await DriveService.OpenRecentAsync(entry, name => new TestDocument(name));

        Assert.Equal("zuletzt gesehen", document.Content);
        Assert.Equal(file.Id, document.Drive!.ItemId);

        await CloudService.DisconnectAsync();
        var signedOut = await DriveService.OpenRecentAsync(entry, name => new TestDocument(name)); // the copy still opens
        Assert.Equal("zuletzt gesehen", signedOut.Content);
    }

    [Fact]
    public async Task A_full_quota_or_a_file_too_large_is_refused_with_its_reason_and_the_document_kept_here()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));
        document.Type("neu");

        cloud.FailWith = HttpStatusCode.InsufficientStorage;
        var full = await Assert.ThrowsAsync<BusinessException>(() => DriveService.SaveAsync(document));
        Assert.Contains("Speicherplatz", full.Message, StringComparison.Ordinal);
        Assert.Equal(DriveSyncState.NotUploaded, document.DriveState);
        Assert.Equal("neu", File.ReadAllText(document.FilePath!));

        cloud.FailWith = HttpStatusCode.RequestEntityTooLarge;
        var large = await Assert.ThrowsAsync<BusinessException>(() => DriveService.UploadAsync(document));
        Assert.Contains("zu groß", large.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Save_as_into_a_folder_adds_the_extension_and_asks_when_the_name_is_taken()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var folder = cloud.AddFolder(null, "Projekte");
        var document = new TestDocument("Test1");
        document.Type("Inhalt");

        var saved = await DriveService.SaveAsAsync(document, folder.Id, "Plan");

        Assert.True(saved.IsUploaded);
        var item = cloud.FindByName(folder.Id, "Plan.tst")!;
        Assert.Equal("Inhalt", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(item.Id)));
        Assert.Equal(item.Id, document.Drive!.ItemId);
        Assert.Equal("/Projekte", document.Drive.Location);
        Assert.False(document.IsDirty);

        var second = new TestDocument("Test2");
        second.Type("Andere Fassung");
        var taken = await DriveService.SaveAsAsync(second, folder.Id, "plan.tst");
        Assert.Equal(DriveSaveOutcome.NameTaken, taken.Outcome);
        Assert.Equal(item.Id, taken.Current!.Id);
        Assert.Null(second.Drive);
        Assert.True(second.IsDirty);

        var replaced = await DriveService.SaveAsAsync(second, folder.Id, "plan.tst", taken.Current);
        Assert.True(replaced.IsUploaded);
        Assert.Equal("Andere Fassung", System.Text.Encoding.UTF8.GetString(cloud.ContentOf(item.Id)));
        Assert.Equal(item.Id, second.Drive!.ItemId);
    }

    [Fact]
    public async Task Save_as_while_offline_saves_nothing_and_keeps_the_document_unsaved()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var document = new TestDocument("Test1");
        document.Type("Inhalt");
        cloud.IsOffline = true;

        await Assert.ThrowsAsync<BusinessException>(() => DriveService.SaveAsAsync(document, null, "Plan"));

        Assert.True(document.IsDirty);
        Assert.Null(document.Drive);
        Assert.Empty(Directory.EnumerateFiles(AppPaths.DriveDirectory, "*", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task Saving_a_ThixDrive_document_on_this_computer_makes_it_a_local_file()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("Hallo"));
        var document = await DriveService.OpenAsync(file.Id, name => new TestDocument(name));

        Assert.Throws<BusinessException>(() => DocumentService.Save(document));
        var local = DocumentService.SaveAs(document, Path.Combine(data.DocumentsFolder, "Brief.tst"));

        Assert.Null(document.Drive);
        Assert.Equal(local, document.FilePath);
        Assert.False(RecentDocuments.Find(local)!.IsInDrive);
    }

    [Fact]
    public async Task Folders_are_made_renamed_and_put_into_the_trash()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(SampleCloud.WithSampleDrive());

        var folder = await DriveService.CreateFolderAsync(null, "Neu");
        var taken = await Assert.ThrowsAsync<BusinessException>(() => DriveService.CreateFolderAsync(null, "neu"));
        Assert.Equal("Name", taken.Field);
        var renamed = await DriveService.RenameAsync(folder.Id, "Archiv");
        await DriveService.TrashAsync(renamed.Id);

        Assert.True(cloud.Find(folder.Id)!.IsTrashed);
        Assert.Equal("Archiv", cloud.Find(folder.Id)!.Name);
        var found = await DriveService.SearchAsync("bericht");
        Assert.Equal("Quartalsbericht.docx", Assert.Single(found.Items).Name);
        Assert.True((await DriveService.QuotaAsync()).QuotaBytes > 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("a/b")]
    [InlineData("Bericht?")]
    [InlineData("Ende.")]
    [InlineData("CON.docx")]
    [InlineData("..")]
    public void Names_the_Drive_refuses_are_refused_here_first(string name) =>
        Assert.Throws<BusinessException>(() => DriveService.CheckName(name));

    [Fact]
    public async Task Without_a_sign_in_the_Drive_is_refused_in_words()
    {
        using var data = new TestData();
        Use(new SampleCloud());

        var refused = await Assert.ThrowsAsync<BusinessException>(() => DriveService.ListAsync(null));

        Assert.Contains("nicht bei ThixCloud angemeldet", refused.Message, StringComparison.Ordinal);
    }
}
