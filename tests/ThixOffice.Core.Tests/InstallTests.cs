using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests;

/// <summary>
/// The rules of the setup in an in-memory registry (on every system): the uninstall entry exactly as ThixLauncher reads it,
/// the document types ("Öffnen mit" for every supported type, the default only for the chosen ones), autostart, the install
/// options of SETUP-08 (installer.json, the command line). On Windows the whole install and uninstall runs in a temporary folder.
/// </summary>
public sealed class InstallTests
{
    private const string Executable = @"C:\Users\Anna\AppData\Local\Programs\ThixTest\ThixTest.exe";
    private const string ProgramFolder = @"C:\Users\Anna\AppData\Local\Programs\ThixTest";

    [Fact]
    public void The_uninstall_entry_has_what_ThixLauncher_reads()
    {
        using var data = new TestData();
        UninstallEntry.Write(data.Registry, TestApp.Definition, Executable, ProgramFolder, "0.1.42-20261118", 40 * 1024 * 1024);

        const string key = @"Software\Microsoft\Windows\CurrentVersion\Uninstall\ThixTest";
        Assert.Equal("ThixTest", data.Registry.GetValue(key, "DisplayName"));
        Assert.Equal("0.1.42-20261118", data.Registry.GetValue(key, "DisplayVersion"));
        Assert.Equal(ProgramFolder, data.Registry.GetValue(key, "InstallLocation"));
        Assert.Equal(Executable + ",0", data.Registry.GetValue(key, "DisplayIcon"));
        Assert.Equal($"\"{Executable}\" --uninstall", data.Registry.GetValue(key, "UninstallString"));
        Assert.Equal($"\"{Executable}\" --uninstall --quiet", data.Registry.GetValue(key, "QuietUninstallString"));
        Assert.Equal("Matthias Schmoldt", data.Registry.GetValue(key, "Publisher"));
        Assert.Equal(40 * 1024, data.Registry.GetValue(key, "EstimatedSize"));
        Assert.Equal("0.1.42-20261118", UninstallEntry.InstalledVersion(data.Registry, TestApp.Definition));

        UninstallEntry.Remove(data.Registry, TestApp.Definition);
        Assert.False(UninstallEntry.Exists(data.Registry, TestApp.Definition));
    }

    [Fact]
    public void Only_supported_types_are_registered_and_only_under_Open_with()
    {
        using var data = new TestData();
        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, @"C:\icons\doc.ico", defaults: null);

        Assert.Equal("Testtext", data.Registry.GetValue(@"Software\Classes\ThixTest.Text", ""));
        Assert.Equal("\"C:\\icons\\doc.ico\"", data.Registry.GetValue(@"Software\Classes\ThixTest.Text\DefaultIcon", ""));
        Assert.Equal($"\"{Executable}\" \"%1\"", data.Registry.GetValue(@"Software\Classes\ThixTest.Text\shell\open\command", ""));
        Assert.Contains("ThixTest.Text", data.Registry.ValueNames(@"Software\Classes\.tst\OpenWithProgids"));
        Assert.Null(data.Registry.GetValue(@"Software\Classes\.tst", "")); // not taken as default
        Assert.Contains(".tst", data.Registry.ValueNames(@"Software\Classes\Applications\ThixTest.exe\SupportedTypes"));
        Assert.Equal(@"Software\ThixTest\Capabilities", data.Registry.GetValue(@"Software\RegisteredApplications", "ThixTest"));
        Assert.Equal("ThixTest.Text", data.Registry.GetValue(@"Software\ThixTest\Capabilities\FileAssociations", ".tst"));

        // The planned format is never registered.
        Assert.False(data.Registry.KeyExists(@"Software\Classes\ThixTest.Document"));
        Assert.False(data.Registry.KeyExists(@"Software\Classes\.tsx"));
        Assert.True(FileAssociations.IsRegistered(data.Registry, TestApp.Definition));
    }

    [Fact]
    public void A_chosen_type_gets_the_app_as_default_and_a_type_no_longer_chosen_is_released()
    {
        using var data = new TestData();
        data.Registry.SetValue(@"Software\Classes\.tst", "", "OtherEditor.Text");

        // Repairing ("Erneut eintragen") never changes a default.
        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, defaults: null);
        Assert.Equal("OtherEditor.Text", data.Registry.GetValue(@"Software\Classes\.tst", ""));
        Assert.Empty(FileAssociations.Defaults(data.Registry, TestApp.Definition));

        // The user chose the type in the setup (or ThixLauncher passed --file-types=.tst).
        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, defaults: [".TST"]);
        Assert.True(FileAssociations.IsDefaultFor(data.Registry, TestApp.Definition.SaveType));
        Assert.Equal([".tst"], FileAssociations.Defaults(data.Registry, TestApp.Definition));
        Assert.Equal("text/plain", data.Registry.GetValue(@"Software\Classes\.tst", "Content Type"));
        Assert.Equal($"\"{Executable}\",0", data.Registry.GetValue(@"Software\Classes\ThixTest.Text\DefaultIcon", ""));

        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, defaults: null);
        Assert.True(FileAssociations.IsDefaultFor(data.Registry, TestApp.Definition.SaveType));

        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, defaults: []);
        Assert.Null(data.Registry.GetValue(@"Software\Classes\.tst", ""));
        Assert.Contains("ThixTest.Text", data.Registry.ValueNames(@"Software\Classes\.tst\OpenWithProgids")); // still under "Öffnen mit"
    }

    [Fact]
    public void Unregistering_removes_the_apps_keys_and_leaves_another_programs_default()
    {
        using var data = new TestData();
        data.Registry.SetValue(@"Software\Classes\.tst", "", "OtherEditor.Text");
        data.Registry.SetValue(@"Software\Classes\.tst\OpenWithProgids", "OtherEditor.Text", "", RegistryValueType.None);
        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, defaults: null);

        FileAssociations.Unregister(data.Registry, TestApp.Definition);

        Assert.Equal("OtherEditor.Text", data.Registry.GetValue(@"Software\Classes\.tst", ""));
        Assert.Equal(["OtherEditor.Text"], data.Registry.ValueNames(@"Software\Classes\.tst\OpenWithProgids"));
        Assert.False(data.Registry.KeyExists(@"Software\Classes\ThixTest.Text"));
        Assert.False(data.Registry.KeyExists(@"Software\Classes\Applications\ThixTest.exe"));
        Assert.False(data.Registry.KeyExists(@"Software\ThixTest"));
        Assert.Null(data.Registry.GetValue(@"Software\RegisteredApplications", "ThixTest"));
        Assert.False(FileAssociations.IsRegistered(data.Registry, TestApp.Definition));
    }

    [Fact]
    public void Autostart_is_the_Run_value_or_the_LaunchAgent()
    {
        using var data = new TestData();
        if (!OperatingSystem.IsMacOS())
        {
            AutostartService.Enable(Executable);
            Assert.Equal($"\"{Executable}\" --autostart", data.Registry.GetValue(@"Software\Microsoft\Windows\CurrentVersion\Run", "ThixTest"));
            Assert.True(AutostartService.IsEnabled());
            AutostartService.Disable();
            Assert.False(AutostartService.IsEnabled());
        }

        AutostartService.UseLaunchAgentFolderForTests(Path.Combine(data.Folder, "LaunchAgents"));
        AutostartService.Enable("/Applications/ThixTest.app/Contents/MacOS/ThixTest");
        Assert.True(AutostartService.IsEnabled());
        var plist = File.ReadAllText(AutostartService.LaunchAgentFile);
        Assert.Contains("<string>info.schmoldt.thixtest</string>", plist);
        Assert.Contains("<string>--autostart</string>", plist);
        Assert.EndsWith("info.schmoldt.thixtest.plist", AutostartService.LaunchAgentFile);
        AutostartService.Disable();
        Assert.False(AutostartService.IsEnabled());
    }

    [Fact]
    public void The_whole_setup_installs_and_uninstalls_in_test_places()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // the self-installing program (shortcuts through COM) exists on Windows only
        }

        using var data = new TestData();
        var programFolder = Path.Combine(data.Folder, "Programs", "ThixTest");
        InstallService.UseLocationsForTests(programFolder);
        var source = Path.Combine(data.Folder, "Downloads", "ThixTestSetup.exe");
        Directory.CreateDirectory(Path.GetDirectoryName(source)!);
        File.WriteAllBytes(source, new byte[2048]);
        var steps = new List<string>();

        InstallService.Install(source, new InstallChoices(DesktopShortcut: true, AutomaticUpdates: false), steps.Add);

        Assert.True(InstallService.IsInstalled);
        Assert.True(File.Exists(Path.Combine(programFolder, "ThixTest.exe")));
        Assert.True(File.Exists(InstallService.StartMenuShortcut));
        Assert.True(File.Exists(InstallService.DesktopShortcut));
        Assert.Equal(AppInfo.Version, InstallService.InstalledVersion);
        Assert.Equal(programFolder, data.Registry.GetValue(TestApp.Definition.UninstallKey, "InstallLocation"));
        Assert.True(FileAssociations.IsRegistered(data.Registry, TestApp.Definition));
        Assert.False(SettingsService.Get().InstallUpdatesAutomatically);
        Assert.Equal([".tst"], InstallerOptions.Current().FileTypes); // the default of installer.json
        Assert.Equal($"\"{Path.Combine(programFolder, "ThixTest.exe")}\" \"%1\"", data.Registry.GetValue(@"Software\Classes\ThixTest.Text\shell\open\command", ""));
        Assert.Contains("Installation abgeschlossen", steps);

        // An update keeps the shortcuts and choices.
        InstallService.Install(source, null, steps.Add);
        Assert.True(File.Exists(InstallService.DesktopShortcut));
        Assert.Equal([".tst"], InstallerOptions.Current().FileTypes);
        Assert.False(SettingsService.Get().InstallUpdatesAutomatically);
        Assert.Contains("Update abgeschlossen", steps);

        // A given option changes what the installed app has; the others stay.
        InstallService.Install(source, new InstallChoices(DesktopShortcut: false, FileTypes: []), steps.Add);
        Assert.False(File.Exists(InstallService.DesktopShortcut));
        Assert.Empty(InstallerOptions.Current().FileTypes!);
        Assert.True(FileAssociations.IsRegistered(data.Registry, TestApp.Definition)); // still under "Öffnen mit"
        Assert.False(SettingsService.Get().InstallUpdatesAutomatically);

        var userFile = Path.Combine(data.DocumentsFolder, "bleibt.tst");
        File.WriteAllText(userFile, "Dokument");
        Assert.False(InstallService.Uninstall(deleteUserData: true, steps.Add));
        Assert.False(InstallService.IsInstalled);
        Assert.False(Directory.Exists(programFolder));
        Assert.False(File.Exists(InstallService.StartMenuShortcut));
        Assert.False(FileAssociations.IsRegistered(data.Registry, TestApp.Definition));
        Assert.False(UninstallEntry.Exists(data.Registry, TestApp.Definition));
        Assert.True(File.Exists(userFile)); // documents are never touched
    }

    [Fact]
    public void The_command_line_carries_the_install_options_and_refuses_what_it_does_not_know()
    {
        using var data = new TestData();
        InstallerOptions.UseConfigForTests(InstallerOptions.Config with
        {
            FileTypes = [new InstallerFileType { Extension = ".tst", Name = "Testtext", Default = true }, new InstallerFileType { Extension = ".txt", Name = "Text" }],
        });
        var request = InstallerOptions.Parse(["--quiet", "--desktop-shortcut=yes", "--automatic-updates=no", "--file-types=tst,.TXT", "--start"]);
        Assert.Equal(SetupAction.Install, request.Action);
        Assert.True(request.Start);
        Assert.True(request.Quiet);
        Assert.Equal((true, false), (request.Choices.DesktopShortcut, request.Choices.AutomaticUpdates));
        Assert.Null(request.Choices.Autostart);
        Assert.Equal([".tst", ".txt"], request.Choices.FileTypes);
        Assert.Equal(["--desktop-shortcut=yes", "--automatic-updates=no", "--file-types=.tst,.txt"], InstallerOptions.ToArguments(request.Choices));
        Assert.Equal([".tst"], InstallerOptions.Parse(["--update", "--file-types=default"]).Choices.FileTypes);
        Assert.Empty(InstallerOptions.Parse(["--configure", "--file-types=none"]).Choices.FileTypes!);
        Assert.Equal(SetupAction.Configure, InstallerOptions.Parse(["--configure", "--desktop-shortcut=no"]).Action);
        Assert.True(InstallerOptions.Parse(["--update", "--restart"]).Start);
        Assert.True(InstallerOptions.Parse(["--uninstall", "--quiet"]) is { Action: SetupAction.Uninstall, Quiet: true });

        // Not setup arguments: the start at sign-in (a bare --autostart) and a document.
        Assert.Equal(SetupAction.None, InstallerOptions.Parse(["--autostart"]).Action);
        Assert.Equal(SetupAction.None, InstallerOptions.Parse([@"C:\Briefe\a=b.tst"]).Action);

        Assert.Throws<BusinessException>(() => InstallerOptions.Parse(["--quiet", "--desktop-shortcut=vielleicht"]));
        Assert.Throws<BusinessException>(() => InstallerOptions.Parse(["--quiet", "--file-types=.docx"]));
        Assert.Throws<BusinessException>(() => InstallerOptions.Parse(["--quiet", "--color=blue"]));
        Assert.Throws<BusinessException>(() => InstallerOptions.Parse(["--quiet", "--autostart=yes"])); // the Office apps do not offer it
        Assert.Throws<BusinessException>(() => InstallerOptions.Parse(["--desktop-shortcut=yes"])); // options without --quiet/--update/--configure
        Assert.Throws<BusinessException>(() => InstallerOptions.Parse(["--uninstall", "--file-types=none"]));
    }

    [Fact]
    public void The_embedded_installer_json_is_read_and_gives_the_defaults()
    {
        using var data = new TestData();
        var config = InstallerOptions.Config;
        Assert.NotNull(config.DesktopShortcut);
        Assert.NotNull(config.AutomaticUpdates);
        Assert.Null(config.Autostart);
        Assert.Equal(".tst", Assert.Single(config.FileTypes).Extension);
        Assert.Equal(new InstallChoices(false, false, true, null), InstallerOptions.Defaults with { FileTypes = null });
        Assert.Equal([".tst"], InstallerOptions.Defaults.FileTypes);
    }

    [Fact]
    public void Applying_the_options_sets_the_file_types_unless_an_administrator_turned_them_off()
    {
        if (!OperatingSystem.IsWindows())
        {
            return; // macOS asks Launch Services, which a test must not change
        }

        using var data = new TestData();
        var steps = new List<string>();
        data.Policies.Add(AccessPolicy.DisableFileAssociations);
        InstallerOptions.Apply(new InstallChoices(FileTypes: [".tst"]), Executable, steps.Add);
        Assert.False(data.Registry.KeyExists(@"Software\Classes\ThixTest.Text"));
        Assert.Contains("Dateitypen: von einem Administrator deaktiviert", steps);

        data.Policies.Clear();
        InstallerOptions.Apply(new InstallChoices(AutomaticUpdates: false, FileTypes: [".tst"]), Executable, steps.Add);
        Assert.True(FileAssociations.IsDefaultFor(data.Registry, TestApp.Definition.SaveType));
        Assert.False(SettingsService.Get().InstallUpdatesAutomatically);

        // Not given: stays.
        InstallerOptions.Apply(new InstallChoices(), Executable, steps.Add);
        Assert.True(FileAssociations.IsDefaultFor(data.Registry, TestApp.Definition.SaveType));
    }
}
