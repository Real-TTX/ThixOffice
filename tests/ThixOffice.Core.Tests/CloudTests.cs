using System.Net;
using System.Text;
using MatCloud.Contract;
using MatCloud.Contract.Drive;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;

namespace ThixOffice.Core.Tests;

/// <summary>The sign-in at ThixCloud against a cloud in memory: password, browser, launcher hand-over, sign-out, 401/403, the policy.</summary>
public sealed class CloudTests
{
    [Fact]
    public async Task Signing_in_with_a_password_keeps_the_token_protected_and_the_account()
    {
        using var data = new TestData();
        var cloud = Use(new SampleCloud());

        await CloudService.ConnectAsync("cloud.example.com/", SampleCloud.UserName, SampleCloud.Password, "Büro-PC");

        var settings = SettingsService.Get().Cloud;
        Assert.True(CloudService.IsConnected);
        Assert.Equal(SampleCloud.ServerUrl, settings.ServerUrl);
        Assert.Equal("Anna Beispiel (anna)", settings.AccountText);
        Assert.Equal("Büro-PC", cloud.LastDeviceName);
        Assert.Equal("test", settings.ProtectedToken); // the token itself is in the TokenStore, never in the settings
        Assert.DoesNotContain("token-", File.ReadAllText(AppPaths.SettingsFile), StringComparison.Ordinal);
        Assert.DoesNotContain(SampleCloud.Password, File.ReadAllText(AppPaths.SettingsFile), StringComparison.Ordinal);
        Assert.Equal(CloudState.Online, CloudService.Status.State);
        Assert.NotNull(await CloudService.RefreshAccountAsync());
    }

    [Fact]
    public async Task A_wrong_password_or_an_unreachable_address_is_shown_at_its_field()
    {
        using var data = new TestData();
        var cloud = Use(new SampleCloud());

        var wrong = await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, "falsch", ""));
        Assert.Equal("Password", wrong.Field);

        cloud.IsOffline = true;
        var offline = await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, ""));
        Assert.Equal(nameof(CloudSettings.ServerUrl), offline.Field);

        var empty = await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync(" ", SampleCloud.UserName, SampleCloud.Password, ""));
        Assert.Equal(nameof(CloudSettings.ServerUrl), empty.Field);
        Assert.False(CloudService.IsConnected);
        Assert.Equal(CloudState.Off, CloudService.Status.State);
    }

    [Fact]
    public async Task Signing_in_in_the_browser_shows_the_code_opens_the_page_and_waits()
    {
        using var data = new TestData();
        Use(new SampleCloud());
        DeviceLinkStart? shown = null;
        string? opened = null;

        var signedIn = await CloudService.ConnectInBrowserAsync(SampleCloud.ServerUrl, "", start => shown = start, address => opened = address);

        Assert.True(signedIn);
        Assert.Equal("ABCD-EFGH", shown?.Code);
        Assert.StartsWith(SampleCloud.ServerUrl + "/Connect", opened, StringComparison.Ordinal);
        Assert.True(CloudService.IsConnected);
    }

    [Fact]
    public async Task A_browser_sign_in_that_is_cancelled_leaves_the_app_signed_out()
    {
        using var data = new TestData();
        Use(new SampleCloud { ApprovesLinks = false });
        using var cancel = new CancellationTokenSource();

        var signedIn = await CloudService.ConnectInBrowserAsync(SampleCloud.ServerUrl, "", _ => cancel.CancelAfter(300), _ => { }, cancel.Token);

        Assert.False(signedIn);
        Assert.False(CloudService.IsConnected);
        Assert.Equal(CloudState.Off, CloudService.Status.State);
    }

    [Fact]
    public async Task The_hand_over_of_ThixLauncher_signs_in_once_and_is_removed()
    {
        using var data = new TestData();
        var cloud = Use(new SampleCloud());
        CloudHandoff.Write(CloudApps.Write, new DeviceHandoff(SampleCloud.ServerUrl, cloud.IssueHandoff(), SampleCloud.UserName, DateTimeOffset.UtcNow.AddHours(1)));

        Assert.Equal(SampleCloud.DisplayName, await CloudService.ConnectFromLauncherAsync());

        Assert.True(CloudService.IsConnected);
        Assert.False(CloudHandoff.Exists(CloudApps.Write));
        Assert.Null(await CloudService.ConnectFromLauncherAsync()); // nothing left, and signed in anyway
    }

    [Fact]
    public async Task A_hand_over_is_kept_when_the_cloud_cannot_be_reached()
    {
        using var data = new TestData();
        var cloud = Use(new SampleCloud());
        CloudHandoff.Write(CloudApps.Write, new DeviceHandoff(SampleCloud.ServerUrl, cloud.IssueHandoff(), SampleCloud.UserName, DateTimeOffset.UtcNow.AddHours(1)));
        cloud.IsOffline = true;

        Assert.Null(await CloudService.ConnectFromLauncherAsync());

        Assert.True(CloudHandoff.Exists(CloudApps.Write)); // the next start tries again
        Assert.False(CloudService.IsConnected);
    }

    [Fact]
    public async Task Signing_out_ends_the_device_and_keeps_the_address()
    {
        using var data = new TestData();
        var cloud = Use(new SampleCloud());
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "");

        await CloudService.DisconnectAsync();

        Assert.False(CloudService.IsConnected);
        Assert.Contains($"POST {CloudContract.DeviceLogout}", cloud.Requests);
        Assert.Equal(SampleCloud.ServerUrl, SettingsService.Get().Cloud.ServerUrl);
        Assert.Equal("", SettingsService.Get().Cloud.ProtectedToken);
        Assert.Equal(CloudState.Off, CloudService.Status.State);
    }

    [Fact]
    public async Task A_device_removed_in_the_cloud_is_signed_out_and_a_switched_off_drive_says_so()
    {
        using var data = new TestData();
        var cloud = Use(SampleCloud.WithSampleDrive());
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "");

        cloud.FailWith = HttpStatusCode.Forbidden;
        var forbidden = await Assert.ThrowsAsync<BusinessException>(() => DriveService.ListAsync(null));
        Assert.Contains("von der Verwaltung", forbidden.Message, StringComparison.Ordinal);
        Assert.Equal(CloudState.Forbidden, CloudService.Status.State);
        Assert.True(CloudService.IsConnected); // switched off is not signed out

        cloud.FailWith = null;
        cloud.RemoveDevices();
        await Assert.ThrowsAsync<BusinessException>(() => DriveService.ListAsync(null));
        Assert.False(CloudService.IsConnected);
        Assert.Equal(CloudState.SignedOut, CloudService.Status.State);
    }

    [Fact]
    public async Task The_policy_DisableCloud_refuses_every_way_in()
    {
        using var data = new TestData();
        var cloud = Use(new SampleCloud());
        data.Policies.Add(AccessPolicy.DisableCloud);
        CloudHandoff.Write(CloudApps.Write, new DeviceHandoff(SampleCloud.ServerUrl, cloud.IssueHandoff(), SampleCloud.UserName, DateTimeOffset.UtcNow.AddHours(1)));

        Assert.False(CloudService.IsAvailable);
        await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, ""));
        await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectInBrowserAsync(SampleCloud.ServerUrl, "", _ => { }, _ => { }));
        Assert.Null(await CloudService.ConnectFromLauncherAsync());
        Assert.False(CloudHandoff.Exists(CloudApps.Write));
        await Assert.ThrowsAsync<BusinessException>(() => DriveService.ListAsync(null));
        Assert.Empty(cloud.Requests);
    }

    [Fact]
    public async Task An_app_without_a_cloud_id_has_no_cloud()
    {
        using var data = new TestData();
        Use(new SampleCloud());
        AppInfo.Initialize(TestApp.Definition with { CloudAppId = null }, typeof(TestData).Assembly);
        try
        {
            Assert.False(CloudService.IsAvailable);
            await Assert.ThrowsAsync<BusinessException>(() => CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, ""));
        }
        finally
        {
            AppInfo.Initialize(TestApp.Definition, typeof(TestData).Assembly);
        }
    }

    internal static SampleCloud Use(SampleCloud cloud)
    {
        CloudService.HandlerForTests = () => cloud;
        return cloud;
    }

    internal static async Task<SampleCloud> SignedInAsync(SampleCloud cloud)
    {
        Use(cloud);
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "TEST-PC");
        return cloud;
    }

    internal static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);
}
