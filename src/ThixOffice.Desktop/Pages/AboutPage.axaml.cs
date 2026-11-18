using System.Runtime.InteropServices;
using Avalonia.Controls;
using Avalonia.Interactivity;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Pages;

/// <summary>Datei › Info: the app, its version, channel and build, the document types, the system and where its data lives.</summary>
public partial class AboutPage : UserControl
{
    private readonly OfficeWindow? _window;

    /// <summary>For the XAML previewer.</summary>
    public AboutPage()
    {
        InitializeComponent();
    }

    public AboutPage(OfficeWindow window)
        : this()
    {
        _window = window;
        var definition = AppInfo.Definition;
        TitleText.Text = $"Über {definition.Name}";
        LogoImage.Source = OfficeApplication.Instance.Logo;
        WordMarkHost.Content = Ui.WordMark(definition.Suffix, 28);
        TaglineText.Text = definition.Description;
        VersionText.Text = MatVersion.Format(AppInfo.Version, AppInfo.Channel);
        ChannelText.Text = SettingsPage.ChannelName(AppInfo.Channel);
        BuildText.Text = AppInfo.BuildNumber > 0 ? $"#{AppInfo.BuildNumber}" : "–";
        TypesText.Text = string.Join(", ", definition.DocumentTypes.Select(type => type.IsSupported ? $"{type.Extension}" : $"{type.Extension} (geplant)"));
        SystemText.Text = $"{RuntimeInformation.OSDescription} ({UpdateService.PlatformKey}), .NET {Environment.Version}";
        DataText.Text = AppPaths.DataDirectory;
        UpdateCoordinator.Changed += OnUpdateChanged;
        DetachedFromVisualTree += (_, _) => UpdateCoordinator.Changed -= OnUpdateChanged;
        RefreshUpdate();
    }

    /// <summary>The texts that name this machine (snapshots replace them with a typical installation).</summary>
    internal void ShowMachine(string system, string dataFolder)
    {
        SystemText.Text = system;
        DataText.Text = dataFolder;
    }

    private void OnUpdateChanged(object? sender, EventArgs e) => Ui.Post(RefreshUpdate);

    /// <summary>What the last update check found; the button leads to Einstellungen › Updates.</summary>
    private void RefreshUpdate()
    {
        var result = UpdateService.LastResult;
        var available = result is { IsUpdateAvailable: true, Manifest: not null };
        UpdateTitle.Text = available ? $"Version {result!.Manifest!.Version} ist verfügbar" : AppInfo.IsLocalBuild ? "Lokaler Build" : "Updates";
        UpdateText.Text = available
            ? "Sie wird beim Schließen installiert, wenn automatische Updates an sind."
            : AppInfo.IsLocalBuild
                ? "Ein lokaler Build bekommt keine Updates."
                : result is null
                    ? "Noch nicht geprüft."
                    : $"{result.Message} Zuletzt geprüft: {(UpdateService.LastCheck is { } last ? Ui.FormatDateTime(last) : "–")}.";
        UpdateLabel.Text = available ? "Installieren …" : "Updates …";
        UpdateIcon.Use(MatIcon.ForegroundProperty, available ? "Mat.Accent" : "Mat.TextMuted");
    }

    private void OnUpdateClick(object? sender, RoutedEventArgs e) => _window?.ShowSettings(SettingsPage.UpdatesTab);

    private void OnGitHubClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal($"https://github.com/{AppInfo.Repository}");

    private void OnBackClick(object? sender, RoutedEventArgs e) => _window?.Backstage.Close();
}
