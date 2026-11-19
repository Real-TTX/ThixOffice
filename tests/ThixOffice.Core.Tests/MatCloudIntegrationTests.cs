using System.Text;
using MatCloud.Contract;
using MatCloud.Contract.Drive;
using ThixOffice.Core.Business;

namespace ThixOffice.Core.Tests;

/// <summary>
/// One run against a REAL MatCloud (the build of the MatCloud repository), only when THIXOFFICE_MATCLOUD_URL, _USER and _PASSWORD name
/// a throwaway installation (CLAUDE.md: "Integration run"); without them the test does nothing. Sign-in by password, the account, a
/// folder, save as (and a taken name), open, save with If-Match, a version saved in between by a second device, save as copy, list,
/// search, recent, quota, trash, sign-out.
/// </summary>
public sealed class MatCloudIntegrationTests
{
    [Fact]
    public async Task Against_a_real_MatCloud_when_THIXOFFICE_MATCLOUD_URL_is_set()
    {
        var url = Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_URL");
        var user = Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_USER");
        var password = Environment.GetEnvironmentVariable("THIXOFFICE_MATCLOUD_PASSWORD");
        if (string.IsNullOrEmpty(url) || string.IsNullOrEmpty(user) || string.IsNullOrEmpty(password))
        {
            return; // only in an integration run
        }

        using var data = new TestData();
        await CloudService.ConnectAsync(url, user, password, "THIXOFFICE-IT");
        Assert.Equal(user, (await CloudService.RefreshAccountAsync())!.UserName);
        var folder = await DriveService.CreateFolderAsync(null, $"ThixOffice-IT-{Guid.NewGuid():N}"[..24]);
        try
        {
            var document = new TestDocument("Test1");
            document.Type("Erste Fassung");
            Assert.True((await DriveService.SaveAsAsync(document, folder.Id, "Bericht")).IsUploaded);
            var id = document.Drive!.ItemId;
            Assert.Equal("Bericht.tst", document.Drive.Name);

            var second = new TestDocument("Test2");
            second.Type("Andere");
            var taken = await DriveService.SaveAsAsync(second, folder.Id, "bericht.tst");
            Assert.Equal(DriveSaveOutcome.NameTaken, taken.Outcome);
            Assert.Equal(id, taken.Current!.Id);

            var opened = await DriveService.OpenAsync(id, name => new TestDocument(name));
            Assert.Equal("Erste Fassung", opened.Content);
            opened.Type("Zweite Fassung");
            Assert.True((await DriveService.SaveAsync(opened)).IsUploaded);

            // a second device (another computer of the same account) saves in between
            var login = await new MatCloudClient(url, null, "ThixOffice-IT/1.0").LoginAsync(
                new DeviceLoginRequest(user, password, "THIXOFFICE-IT-2", "Windows", "1.0", CloudApps.Write), CancellationToken.None);
            using (var other = new MatCloudClient(url, login.Token, "ThixOffice-IT/1.0"))
            {
                var current = await other.GetDriveItemAsync(id, CancellationToken.None);
                using var bert = new MemoryStream(Encoding.UTF8.GetBytes("von Bert"));
                await other.SaveDriveFileAsync(id, bert, current.ETag!, CancellationToken.None);
                await other.LogoutAsync(CancellationToken.None);
            }

            opened.Type("von Anna");
            var conflict = await DriveService.SaveAsync(opened);
            Assert.Equal(DriveSaveOutcome.ChangedMeanwhile, conflict.Outcome);
            Assert.NotNull(conflict.Current?.ETag);
            Assert.True((await DriveService.SaveCopyAsync(opened)).IsUploaded);
            Assert.NotEqual(id, opened.Drive!.ItemId);

            var listing = await DriveService.ListAsync(folder.Id);
            Assert.Equal(2, listing.Items.Count(item => item.Kind == DriveItemKind.File));
            Assert.Contains((await DriveService.SearchAsync("Bericht")).Items, item => item.Id == id);
            Assert.NotEmpty((await DriveService.RecentAsync()).Items);
            Assert.True((await DriveService.QuotaAsync()).UsedBytes > 0);

            var original = await DriveService.OpenAsync(id, name => new TestDocument(name));
            Assert.Equal("von Bert", original.Content);
        }
        finally
        {
            await DriveService.TrashAsync(folder.Id);
            await CloudService.DisconnectAsync();
        }

        // ThixLauncher signs in once and hands a sign-in to the app it installs: redeemed without a browser
        var launcher = await new MatCloudClient(url, null, "ThixLauncher-IT/1.0").LoginAsync(
            new DeviceLoginRequest(user, password, "THIXOFFICE-IT-LAUNCHER", "Windows", "1.0", CloudApps.Launcher), CancellationToken.None);
        using (var client = new MatCloudClient(url, launcher.Token, "ThixLauncher-IT/1.0"))
        {
            var handoff = await client.HandOffAsync(new DeviceHandoffRequest(CloudApps.Write, "1.0"), CancellationToken.None);
            CloudHandoff.Write(CloudApps.Write, new DeviceHandoff(url, handoff.PollToken, user, DateTimeOffset.UtcNow.AddSeconds(handoff.ExpiresInSeconds)));
            Assert.NotNull(await CloudService.ConnectFromLauncherAsync());
            Assert.True(CloudService.IsConnected);
            Assert.False(CloudHandoff.Exists(CloudApps.Write));
            Assert.NotNull((await DriveService.ListAsync(null)).Items);
            await CloudService.DisconnectAsync();
            await client.LogoutAsync(CancellationToken.None);
        }
    }
}
