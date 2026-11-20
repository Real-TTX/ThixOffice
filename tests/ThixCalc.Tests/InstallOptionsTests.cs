using Avalonia.Headless.XUnit;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixCalc.Core;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>
/// ThixCalc's install options (SETUP-08: its installer.json, what the setup assistant and ThixLauncher ask) and the way a
/// document reaches it: as the first argument of a start, and from a second start through the running instance.
/// </summary>
public sealed class InstallOptionsTests
{
    [AvaloniaFact]
    public void The_installer_json_offers_every_type_ThixCalc_opens_and_its_own_format_by_default()
    {
        using var data = new TestData();
        var config = InstallerOptions.Config;

        Assert.NotNull(config.DesktopShortcut);
        Assert.False(config.DesktopShortcut.Default);
        Assert.True(config.AutomaticUpdates?.Default);
        Assert.Null(config.Autostart); // an editor does not start with the computer
        Assert.Equal(
            ThixCalcApp.Definition.SupportedTypes.Select(type => (type.Extension, type.Description)).Order(),
            config.FileTypes.Select(type => (type.Extension, type.Name)).Order());
        Assert.Equal([".xlsx"], InstallerOptions.Defaults.FileTypes);
        Assert.Equal(".xlsx", ThixCalcApp.Definition.DefaultType.Extension);

        if (!OperatingSystem.IsWindows())
        {
            return; // the setup assistant exists on Windows only
        }

        // The setup assistant asks the same: a switch per type, each as its default.
        var setup = OfficeSetup.Options(ThixCalcApp.Definition).ToList();
        Assert.Contains(setup, option => option is { Key: "type:.xlsx", IsOn: true });
        Assert.Contains(setup, option => option is { Key: "type:.ods", IsOn: false });
        var answers = OfficeSetup.Choices(setup.ToDictionary(option => option.Key, option => option.IsOn));
        Assert.Equal(((bool?)false, (bool?)null, (bool?)true), (answers.DesktopShortcut, answers.Autostart, answers.AutomaticUpdates));
        Assert.Equal([".xlsx"], answers.FileTypes);
    }

    [AvaloniaFact]
    public async Task A_document_on_the_command_line_opens_and_a_second_start_hands_its_document_to_the_running_one()
    {
        using var data = new TestData();
        var first = Path.Combine(data.DocumentsFolder, "Erster Start.xlsx");
        var second = Path.Combine(data.DocumentsFolder, "Zweiter Start.xlsx");
        DocumentService.SaveAs(TestData.App.CreateDocument("Erster Start"), first);
        DocumentService.SaveAs(TestData.App.CreateDocument("Zweiter Start"), second);

        // The first start: "ThixCalc.exe <file>" (Explorer's open command, Finder) – what OfficeApplication.Start opens.
        var scope = Path.Combine(data.Folder, "instance");
        using var running = OfficeApplication.InstanceOf(TestData.App.Definition, scope);
        var files = OfficeApplication.DocumentFilesIn([first]);
        Assert.True(TestData.App.ClaimInstance(running, files));
        await TestData.App.OpenFilesAsync(files);
        await SettleAsync();
        Assert.Equal(first, Assert.Single(TestData.App.DocumentWindows).Document.FilePath);

        // A second start with a document hands it over through the pipe and ends; the running one opens it.
        using var secondStart = OfficeApplication.InstanceOf(TestData.App.Definition, scope);
        Assert.False(TestData.App.ClaimInstance(secondStart, OfficeApplication.DocumentFilesIn([second, "--autostart"])));
        for (var wait = 0; wait < 100 && TestData.App.DocumentWindows.Count < 2; wait++)
        {
            await Task.Delay(50);
            await SettleAsync();
        }

        Assert.Equal([first, second], TestData.App.DocumentWindows.Select(window => window.Document.FilePath));
    }
}
