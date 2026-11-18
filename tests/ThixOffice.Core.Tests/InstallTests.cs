using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;

namespace ThixOffice.Core.Tests;

/// <summary>
/// The rules of the setup in an in-memory registry (on every system): the uninstall entry exactly as ThixLauncher reads it,
/// the document types ("Öffnen mit", never taking a type from another program, only supported types), autostart. On
/// Windows the whole install and uninstall runs in a temporary folder.
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
        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, @"C:\icons\doc.ico", makeDefault: false);

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
    public void Set_as_default_takes_only_a_type_no_other_program_has()
    {
        using var data = new TestData();
        data.Registry.SetValue(@"Software\Classes\.tst", "", "OtherEditor.Text");

        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, makeDefault: true);
        Assert.Equal("OtherEditor.Text", data.Registry.GetValue(@"Software\Classes\.tst", ""));
        Assert.False(FileAssociations.IsDefaultFor(data.Registry, TestApp.Definition.SaveType));

        data.Registry.DeleteValue(@"Software\Classes\.tst", "");
        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, makeDefault: true);
        Assert.True(FileAssociations.IsDefaultFor(data.Registry, TestApp.Definition.SaveType));
        Assert.Equal("text/plain", data.Registry.GetValue(@"Software\Classes\.tst", "Content Type"));
        Assert.Equal($"\"{Executable}\",0", data.Registry.GetValue(@"Software\Classes\ThixTest.Text\DefaultIcon", ""));
    }

    [Fact]
    public void Unregistering_removes_the_apps_keys_and_leaves_another_programs_default()
    {
        using var data = new TestData();
        data.Registry.SetValue(@"Software\Classes\.tst", "", "OtherEditor.Text");
        data.Registry.SetValue(@"Software\Classes\.tst\OpenWithProgids", "OtherEditor.Text", "", RegistryValueType.None);
        FileAssociations.Register(data.Registry, TestApp.Definition, Executable, null, makeDefault: false);

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

        InstallService.Install(source, new InstallOptions(DesktopShortcut: true, InstallUpdatesAutomatically: false), steps.Add);

        Assert.True(InstallService.IsInstalled);
        Assert.True(File.Exists(Path.Combine(programFolder, "ThixTest.exe")));
        Assert.True(File.Exists(InstallService.StartMenuShortcut));
        Assert.True(File.Exists(InstallService.DesktopShortcut));
        Assert.Equal(AppInfo.Version, InstallService.InstalledVersion);
        Assert.Equal(programFolder, data.Registry.GetValue(TestApp.Definition.UninstallKey, "InstallLocation"));
        Assert.True(FileAssociations.IsRegistered(data.Registry, TestApp.Definition));
        Assert.False(SettingsService.Get().InstallUpdatesAutomatically);
        Assert.Contains("Installation abgeschlossen", steps);

        // An update keeps the shortcuts and choices.
        InstallService.Install(source, null, steps.Add);
        Assert.True(File.Exists(InstallService.DesktopShortcut));
        Assert.Contains("Update abgeschlossen", steps);

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
}
