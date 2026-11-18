using System.Text.Json;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests;

/// <summary>The channel manifest of an app: reading it, the own channel only, the package per platform, safe file names.</summary>
public sealed class UpdateTests
{
    private const string Manifest = """
        {
          "version": "nightly-57-20261118",
          "channel": "nightly",
          "buildNumber": 57,
          "setup": "ThixTestSetup.exe",
          "sha256": "AA11",
          "publishedAt": "2026-11-18T12:00:00Z",
          "packages": {
            "win-x64": { "file": "ThixTestSetup.exe", "sha256": "AA11" },
            "osx-arm64": { "file": "ThixTest-macOS-arm64.zip", "sha256": "BB22" }
          }
        }
        """;

    [Fact]
    public void The_manifest_is_read_with_a_package_per_platform()
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(Manifest)!;

        Assert.Equal(57, manifest.BuildNumber);
        Assert.Equal("ThixTestSetup.exe", UpdateService.PackageFor(manifest, "win-x64")!.File);
        Assert.Equal("BB22", UpdateService.PackageFor(manifest, "osx-arm64")!.Sha256);
        Assert.Null(UpdateService.PackageFor(manifest, "osx-x64"));
    }

    [Fact]
    public void Windows_falls_back_to_the_setup_of_a_manifest_without_packages()
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(Manifest)! with { Packages = null };
        Assert.Equal("ThixTestSetup.exe", UpdateService.PackageFor(manifest, "win-x64")!.File);
        Assert.Null(UpdateService.PackageFor(manifest, "osx-arm64"));
    }

    [Theory]
    [InlineData("nightly", 56, true)]
    [InlineData("nightly", 57, false)]
    [InlineData("nightly", 80, false)]
    [InlineData("release", 3, false)]
    [InlineData("local", 0, false)]
    public void Only_a_higher_build_of_the_own_channel_is_an_update(string channel, int buildNumber, bool isNewer)
    {
        var manifest = JsonSerializer.Deserialize<UpdateManifest>(Manifest)!;
        Assert.Equal(isNewer, UpdateService.IsNewer(manifest, channel, buildNumber));
    }

    [Theory]
    [InlineData("ThixTestSetup.exe", true)]
    [InlineData("ThixTest-macOS-arm64.zip", true)]
    [InlineData("../evil.exe", false)]
    [InlineData("sub/ThixTestSetup.exe", false)]
    [InlineData("script.sh", false)]
    [InlineData("", false)]
    public void Only_plain_exe_and_zip_names_are_downloaded(string file, bool isSafe) =>
        Assert.Equal(isSafe, UpdateService.IsSafeFileName(file));

    [Fact]
    public void Every_app_reads_its_own_manifest_in_the_shared_channel_releases()
    {
        using var data = new TestData();

        Assert.Equal("thixtest-update.json", UpdateService.ManifestFileName);
        Assert.Equal("https://github.com/Real-TTX/ThixOffice/releases/download/nightly-channel/thixtest-update.json", UpdateService.ManifestUrl("nightly"));
        Assert.Equal("https://github.com/Real-TTX/ThixOffice/releases/tag/release-channel", UpdateService.ReleasePageUrl("release"));
        Assert.Equal(TimeSpan.FromHours(6), UpdateService.CheckInterval);
    }

    [Fact]
    public async Task A_local_build_never_updates_and_a_policy_stops_the_check()
    {
        using var data = new TestData();
        Infrastructure.AppInfo.Pin("local-20261118", "local"); // CI builds the tests as nightly or release
        try
        {
            Assert.False(UpdateService.CanUpdate);
            var result = await UpdateService.CheckAsync();
            Assert.False(result.IsUpdateAvailable);
            Assert.StartsWith("Lokaler Build", result.Message);
        }
        finally
        {
            Infrastructure.AppInfo.Pin(null);
        }


        data.Policies.Add(AccessPolicy.DisableUpdates);
        await Assert.ThrowsAsync<BusinessException>(() => UpdateService.CheckAsync());
    }

    [Fact]
    public void The_Mac_bundle_is_found_from_the_program_inside_it()
    {
        var bundle = Path.Combine(Path.GetTempPath(), "ThixTest.app");
        Assert.Equal(bundle, MacAppBundle.BundleOf(Path.Combine(bundle, "Contents", "MacOS", "ThixTest")));
        Assert.Null(MacAppBundle.BundleOf("/usr/local/bin/ThixTest"));
        Assert.Equal("'it'\\''s'", MacAppBundle.Quote("it's"));
    }
}
