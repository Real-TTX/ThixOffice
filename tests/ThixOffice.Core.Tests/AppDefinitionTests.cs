using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests;

/// <summary>One definition names everything of an app; the version comes from the app's assembly (Directory.Build.props).</summary>
public sealed class AppDefinitionTests
{
    [Fact]
    public void Every_name_of_an_app_follows_from_its_definition()
    {
        var definition = TestApp.Definition;

        Assert.Equal("Test", definition.Suffix);
        Assert.Equal("ThixTest.exe", definition.ExecutableName);
        Assert.Equal("ThixTestSetup.exe", definition.SetupFileName);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ThixTest", definition.UninstallKey);
        Assert.Equal(@"SOFTWARE\Policies\ThixTest", definition.PolicyKey);
        Assert.Equal("info.schmoldt.thixtest", definition.MacBundleId);
        Assert.Equal("thixtest-update.json", definition.ManifestFileName);
        Assert.Equal("ThixTest-macOS-arm64.zip", definition.MacPackageFileName("arm64"));
        Assert.Equal("THIXTEST_DATA_DIR", definition.DataDirectoryVariable);
        Assert.Equal("THIXTEST_SCREENSHOTS", definition.ScreenshotsVariable);
        Assert.Equal("ThixTest-Document.ico", definition.DocumentIconFileName);
    }

    [Fact]
    public void A_planned_default_type_is_never_the_save_type()
    {
        var definition = TestApp.Definition;

        Assert.Equal(".tsx", definition.DefaultType.Extension);
        Assert.False(definition.DefaultType.IsSupported);
        Assert.Equal(".tst", definition.SaveType.Extension);
        Assert.Equal([".tst"], definition.SupportedTypes.Select(type => type.Extension));
        Assert.NotNull(definition.SupportedTypeOf(@"C:\a\b.TST"));
        Assert.Null(definition.SupportedTypeOf(@"C:\a\b.tsx"));
        Assert.Null(definition.SupportedTypeOf(@"C:\a\b"));
    }

    [Fact]
    public void An_app_is_called_Thix_something_and_its_id_is_its_name_in_lower_case()
    {
        Assert.Throws<ArgumentException>(() => new OfficeAppDefinition("write", "Write", "#185ABD", "x", []));
        Assert.Throws<ArgumentException>(() => new OfficeAppDefinition("thixwriter", "ThixWrite", "#185ABD", "x", []));
    }

    [Fact]
    public void The_version_comes_from_the_assembly_of_the_app()
    {
        using var data = new TestData();

        // A local build: CI sets Channel, BuildNumber and BuildDate (Directory.Build.props).
        Assert.Equal("local", AppInfo.Channel);
        Assert.True(AppInfo.IsLocalBuild);
        Assert.Equal(0, AppInfo.BuildNumber);
        Assert.Matches(@"^local-\d{8}$", AppInfo.Version);
        Assert.Equal("Real-TTX/ThixOffice", AppInfo.Repository);
        Assert.Equal("thixoffice", AppInfo.BuiltAppId);
        Assert.Equal("ThixTest", AppInfo.ProductName);
    }

    [Fact]
    public void A_pinned_version_is_what_snapshots_show()
    {
        using var data = new TestData();
        AppInfo.Pin("0.1.0", "release", 7, "ANNA-PC");
        try
        {
            Assert.Equal("0.1.0", AppInfo.Version);
            Assert.Equal("release", AppInfo.Channel);
            Assert.Equal(7, AppInfo.BuildNumber);
            Assert.False(AppInfo.IsLocalBuild);
            Assert.Equal("ANNA-PC", AppInfo.MachineName);
        }
        finally
        {
            AppInfo.Pin(null);
        }
    }

    [Fact]
    public void The_data_folder_is_the_apps_own_and_a_variable_makes_it_another()
    {
        AppInfo.Initialize(TestApp.Definition, typeof(TestData).Assembly);
        AppPaths.UseDirectoryForTests(null);
        var folder = Path.Combine(Path.GetTempPath(), "thixtest-override");
        Environment.SetEnvironmentVariable("THIXTEST_DATA_DIR", folder);
        try
        {
            Assert.Equal(folder, AppPaths.DataDirectory);
            Assert.True(AppPaths.HasOverride);
            Assert.Equal(Path.Combine(folder, "recovery"), AppPaths.RecoveryDirectory);
        }
        finally
        {
            Environment.SetEnvironmentVariable("THIXTEST_DATA_DIR", null);
        }

        Assert.EndsWith("ThixTest", AppPaths.DataDirectory);
        Assert.False(AppPaths.HasOverride);
    }
}
