using ThixOffice.Core.Business;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests;

/// <summary>Settings: defaults, validation at the field, the configuration lock, what the app remembers by itself.</summary>
public sealed class SettingsAndPolicyTests
{
    [Fact]
    public void The_defaults_suit_an_editor()
    {
        using var data = new TestData();
        var settings = SettingsService.Get();

        Assert.Equal(ThemeChoice.System, settings.Theme);
        Assert.True(settings.CheckForUpdates);
        Assert.True(settings.InstallUpdatesAutomatically);
        Assert.Equal(25, settings.RecentListSize);
        Assert.Equal(5, settings.AutoRecoverMinutes);
        Assert.False(settings.KeepBackupCopy);
        Assert.False(settings.IsRibbonCollapsed);
        Assert.Equal("", settings.DefaultSaveFolder);
        Assert.False(AutostartService.IsEnabled()); // the autostart is off unless the user turns it on
    }

    [Fact]
    public void A_change_is_saved_and_read_again()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { Theme = ThemeChoice.Dark, RecentListSize = 10, DefaultSaveFolder = data.DocumentsFolder });
        SettingsService.ResetCache();

        var read = SettingsService.Get();
        Assert.Equal(ThemeChoice.Dark, read.Theme);
        Assert.Equal(10, read.RecentListSize);
        Assert.Equal(data.DocumentsFolder, SettingsService.SaveFolder);
    }

    [Theory]
    [InlineData(0, nameof(AppSettings.RecentListSize))]
    [InlineData(101, nameof(AppSettings.RecentListSize))]
    public void A_wrong_value_is_refused_at_its_field(int size, string field)
    {
        using var data = new TestData();
        var exception = Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { RecentListSize = size }));
        Assert.Equal(field, exception.Field);
        Assert.Equal(25, SettingsService.Get().RecentListSize);
    }

    [Fact]
    public void A_save_folder_that_does_not_exist_is_refused()
    {
        using var data = new TestData();
        var exception = Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { DefaultSaveFolder = Path.Combine(data.Folder, "gibt-es-nicht") }));
        Assert.Equal(nameof(AppSettings.DefaultSaveFolder), exception.Field);
        Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { AutoRecoverMinutes = -1 }));
    }

    [Fact]
    public void A_locked_configuration_refuses_changes_but_the_app_still_remembers_its_window()
    {
        using var data = new TestData();
        data.Policies.Add(AccessPolicy.LockConfiguration);

        var exception = Assert.Throws<BusinessException>(() => SettingsService.Update(settings => settings with { Theme = ThemeChoice.Dark }));
        Assert.Contains("LockConfiguration", exception.Message);
        Assert.Throws<BusinessException>(() => AutostartService.Enable("ThixTest.exe"));

        SettingsService.Remember(settings => settings with { IsRibbonCollapsed = true, WindowWidth = 10 });
        Assert.True(SettingsService.Get().IsRibbonCollapsed);
        Assert.Equal(760, SettingsService.Get().WindowWidth); // clamped

        // An app's own small choices are remembered under the lock too, and survive a new session.
        SettingsService.RememberAppValue("show.loop", "true");
        SettingsService.ResetCache();
        Assert.Equal("true", SettingsService.AppValue("show.loop"));
        SettingsService.RememberAppValue("show.loop", null);
        Assert.Null(SettingsService.AppValue("show.loop"));
    }

    [Fact]
    public void Every_policy_is_checked_in_the_business_layer()
    {
        using var data = new TestData();
        data.Policies.UnionWith(AccessPolicy.Names);

        Assert.Throws<BusinessException>(AccessPolicy.EnsureConfigurationEditable);
        Assert.Throws<BusinessException>(AccessPolicy.EnsureUpdatesAllowed);
        Assert.Throws<BusinessException>(AccessPolicy.EnsureCloudAllowed);
        Assert.Throws<BusinessException>(AccessPolicy.EnsureFileAssociationsAllowed);
        Assert.Throws<BusinessException>(() => FileAssociations.RepairForThisApp("ThixTest.exe"));
        Assert.False(UpdateService.CanUpdate);
        Assert.Contains(@"HKLM\SOFTWARE\Policies\ThixTest", AccessPolicy.Location);
    }

    [Fact]
    public void A_broken_settings_file_is_kept_aside_and_the_defaults_apply()
    {
        using var data = new TestData();
        Directory.CreateDirectory(Path.GetDirectoryName(Infrastructure.AppPaths.SettingsFile)!);
        File.WriteAllText(Infrastructure.AppPaths.SettingsFile, "{ kaputt");
        SettingsService.ResetCache();

        Assert.Equal(25, SettingsService.Get().RecentListSize);
        Assert.Single(Directory.GetFiles(Path.GetDirectoryName(Infrastructure.AppPaths.SettingsFile)!, "settings.json.broken-*"));
    }
}
