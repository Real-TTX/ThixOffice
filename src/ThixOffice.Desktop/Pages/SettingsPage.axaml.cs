using Avalonia.Controls;
using Avalonia.Interactivity;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Pages;

/// <summary>
/// The settings of the app on this computer, in tabs. Every control saves through <see cref="SettingsService"/> (or the
/// autostart / file type services); a refusal of the business layer is shown at its field or above the tabs. A
/// configuration locked by a policy shows its note and the controls stay off.
/// </summary>
public partial class SettingsPage : UserControl
{
    public const string GeneralTab = "general";
    public const string SaveTab = "save";
    public const string CloudTab = "cloud";
    public const string UpdatesTab = "updates";
    public const string AdvancedTab = "advanced";

    private readonly OfficeWindow? _window;
    private bool _isLoading;

    /// <summary>For the XAML previewer.</summary>
    public SettingsPage()
    {
        InitializeComponent();
    }

    public SettingsPage(OfficeWindow window)
        : this()
    {
        _window = window;
        RecoverChoice.ItemsSource = SettingsService.AutoRecoverChoices.Select(minutes => minutes switch
        {
            0 => "Nie",
            1 => "Jede Minute",
            _ => $"Alle {minutes} Minuten",
        }).ToList();
        RecentChoice.ItemsSource = SettingsService.RecentListSizeChoices.Select(count => $"{count} Dokumente").ToList();

        ThemeChoice.SelectionChanged += (_, _) => Change(settings => settings with { Theme = (ThemeChoice)Math.Max(0, ThemeChoice.SelectedIndex) });
        RibbonSwitch.IsCheckedChanged += (_, _) => OnRibbonChanged();
        AutostartSwitch.IsCheckedChanged += (_, _) => OnAutostartChanged();
        BackupSwitch.IsCheckedChanged += (_, _) => Change(settings => settings with { KeepBackupCopy = BackupSwitch.IsChecked == true });
        RecoverChoice.SelectionChanged += (_, _) => Change(settings => settings with { AutoRecoverMinutes = SettingsService.AutoRecoverChoices[Math.Max(0, RecoverChoice.SelectedIndex)] });
        RecentChoice.SelectionChanged += (_, _) => Change(settings => settings with { RecentListSize = SettingsService.RecentListSizeChoices[Math.Max(0, RecentChoice.SelectedIndex)] });
        UserNameBox.LostFocus += (_, _) => ChangeText(UserNameBox, settings => settings.UserName, (settings, value) => settings with { UserName = value });
        UserInitialsBox.LostFocus += (_, _) => ChangeText(UserInitialsBox, settings => settings.UserInitials, (settings, value) => settings with { UserInitials = value });
        SpellingSwitch.IsCheckedChanged += (_, _) => Change(settings => settings with { CheckSpellingAsYouType = SpellingSwitch.IsChecked == true });
        CheckUpdatesSwitch.IsCheckedChanged += (_, _) => Change(settings => settings with { CheckForUpdates = CheckUpdatesSwitch.IsChecked == true });
        AutoInstallSwitch.IsCheckedChanged += (_, _) => Change(settings => settings with { InstallUpdatesAutomatically = AutoInstallSwitch.IsChecked == true });

        UpdateCoordinator.Changed += OnUpdateChanged;
        DetachedFromVisualTree += (_, _) => UpdateCoordinator.Changed -= OnUpdateChanged;
        AttachCloud();
        Load();
    }

    /// <summary>Shows a tab (<see cref="GeneralTab"/> …).</summary>
    public void ShowTab(string tab) => Tabs.SelectedItem = tab switch
    {
        SaveTab when SaveTabItem.IsVisible => SaveTabItem,
        CloudTab when CloudTabItem.IsVisible => CloudTabItem,
        UpdatesTab => UpdatesTabItem,
        AdvancedTab => AdvancedTabItem,
        _ => GeneralTabItem,
    };

    /// <summary>The tab shown (tests).</summary>
    public string CurrentTab => Tabs.SelectedItem == SaveTabItem ? SaveTab : Tabs.SelectedItem == CloudTabItem ? CloudTab : Tabs.SelectedItem == UpdatesTabItem ? UpdatesTab : Tabs.SelectedItem == AdvancedTabItem ? AdvancedTab : GeneralTab;

    private void Load()
    {
        _isLoading = true;
        var settings = SettingsService.Get();
        var definition = AppInfo.Definition;
        var locked = AccessPolicy.IsConfigurationLocked;

        if (locked)
        {
            PolicyNote.Show("Von Ihrer Organisation festgelegt: die Einstellungen sind gesperrt (Richtlinie LockConfiguration).", InfoSeverity.Warning);
        }
        else
        {
            PolicyNote.Hide();
        }

        ThemeChoice.SelectedIndex = (int)settings.Theme;
        UserNameBox.Text = settings.UserName;
        UserNameBox.Watermark = UserIdentity.DisplayName;
        UserInitialsBox.Text = settings.UserInitials;
        UserInitialsBox.Watermark = SettingsService.InitialsOf(settings.UserName is { Length: > 0 } name ? name : UserIdentity.DisplayName);
        SpellingRow.IsVisible = OfficeApplication.Instance?.ChecksSpelling == true;
        SpellingSwitch.IsChecked = settings.CheckSpellingAsYouType;
        RibbonSwitch.IsChecked = settings.IsRibbonCollapsed;
        AutostartRow.IsVisible = AutostartService.IsSupported;
        AutostartSwitch.IsChecked = AutostartService.IsSupported && AutostartService.IsEnabled();

        // a workspace app saves nothing in files: no tab "Speichern"
        SaveTabItem.IsVisible = !definition.IsWorkspace;
        SaveFolderBox.Text = SettingsService.SaveFolder;
        ResetFolderButton.IsVisible = settings.DefaultSaveFolder.Length > 0;
        BackupSwitch.IsChecked = settings.KeepBackupCopy;
        RecoverChoice.SelectedIndex = Math.Max(0, SettingsService.AutoRecoverChoices.ToList().IndexOf(settings.AutoRecoverMinutes));
        RecentChoice.SelectedIndex = IndexOfNearest(SettingsService.RecentListSizeChoices, settings.RecentListSize);

        // File types: Windows only (macOS reads them from the app bundle); hidden when an administrator turned them off.
        FileTypesRow.IsVisible = OperatingSystem.IsWindows() && !AccessPolicy.AreFileAssociationsDisabled && definition.SupportedTypes.Count > 0;
        var types = string.Join(", ", definition.SupportedTypes.Select(type => type.Extension));
        FileTypesRow.Description = FileAssociations.IsRegisteredForThisApp
            ? $"{definition.Name} steht unter „Öffnen mit“ für {types}. Welches Programm ein Typ standardmäßig öffnet, legen Sie in den Standard-Apps von Windows fest."
            : $"{definition.Name} ist für {types} noch nicht eingetragen (es wurde nicht installiert). „Erneut eintragen“ holt das nach.";

        foreach (var control in new Control[] { ThemeChoice, UserNameBox, UserInitialsBox, SpellingSwitch, AutostartSwitch, ChooseFolderButton, ResetFolderButton, BackupSwitch, RecoverChoice, RecentChoice, CheckUpdatesSwitch, AutoInstallSwitch })
        {
            control.IsEnabled = !locked;
        }

        CheckUpdatesSwitch.IsChecked = settings.CheckForUpdates;
        AutoInstallSwitch.IsChecked = settings.InstallUpdatesAutomatically;
        UpdateSwitches.IsVisible = !AppInfo.IsLocalBuild && !AccessPolicy.AreUpdatesDisabled;
        InstalledText.Text = AppInfo.Version;
        ChannelText.Text = ChannelName(AppInfo.Channel);

        DataFolderRow.Description = AppPaths.DataDirectory;
        PoliciesRow.Description = $"{AccessPolicy.Location} – {string.Join(", ", AccessPolicy.Names)} (1 = an).";
        LoadCloud();
        RefreshUpdate();
        _isLoading = false;
    }

    private static int IndexOfNearest(IReadOnlyList<int> choices, int value) =>
        choices.Select((choice, index) => (distance: Math.Abs(choice - value), index)).MinBy(pair => pair.distance).index;

    internal static string ChannelName(string channel) => channel switch
    {
        "release" => "Release",
        "nightly" => "Nightly",
        _ => "Lokaler Build",
    };

    private void Change(Func<AppSettings, AppSettings> change)
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            Message.Hide();
            FormErrors.ShowError(this, null, "");
            SettingsService.Update(change);
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                Message.Show(exception.Message, InfoSeverity.Error);
            }

            Load();
        }
    }

    /// <summary>A text setting: saved when the field is left and its text differs.</summary>
    private void ChangeText(TextBox box, Func<AppSettings, string> current, Func<AppSettings, string, AppSettings> change)
    {
        var value = box.Text?.Trim() ?? "";
        if (_isLoading || value == current(SettingsService.Get()))
        {
            return;
        }

        Change(settings => change(settings, value));
        UserInitialsBox.Watermark = SettingsService.InitialsOf(SettingsService.AuthorName);
    }

    private void OnRibbonChanged()
    {
        if (_isLoading || _window is null)
        {
            return;
        }

        var collapsed = RibbonSwitch.IsChecked == true;
        SettingsService.Remember(settings => settings with { IsRibbonCollapsed = collapsed });
        foreach (var window in OfficeApplication.Instance.DocumentWindows)
        {
            window.Ribbon.IsCollapsed = collapsed;
        }
    }

    private void OnAutostartChanged()
    {
        if (_isLoading)
        {
            return;
        }

        try
        {
            if (AutostartSwitch.IsChecked == true)
            {
                AutostartService.Enable(OfficeApplication.Instance.ExecutablePath);
            }
            else
            {
                AutostartService.Disable();
            }
        }
        catch (BusinessException exception)
        {
            Message.Show(exception.Message, InfoSeverity.Error);
            Load();
        }
    }

    private async void OnChooseFolderClick(object? sender, RoutedEventArgs e)
    {
        if (_window is null)
        {
            return;
        }

        var folder = await OfficeApplication.FileDialogs.PickFolderAsync(_window, SettingsService.SaveFolder);
        if (folder is not null)
        {
            Change(settings => settings with { DefaultSaveFolder = folder });
            Load();
        }
    }

    private void OnResetFolderClick(object? sender, RoutedEventArgs e)
    {
        Change(settings => settings with { DefaultSaveFolder = "" });
        Load();
    }

    private void OnRepairTypesClick(object? sender, RoutedEventArgs e)
    {
        try
        {
            FileAssociations.RepairForThisApp(OfficeApplication.Instance.ExecutablePath);
            Message.Show("Die Dateitypen wurden erneut eingetragen.", InfoSeverity.Success);
            Load();
        }
        catch (BusinessException exception)
        {
            Message.Show(exception.Message, InfoSeverity.Error);
        }
    }

    private void OnDefaultAppsClick(object? sender, RoutedEventArgs e) =>
        Ui.OpenExternal($"ms-settings:defaultapps?registeredAppUser={AppInfo.ProductName}");

    // ===== Updates =====

    private void OnUpdateChanged(object? sender, EventArgs e) => Ui.Post(RefreshUpdate);

    private void RefreshUpdate()
    {
        LastCheckText.Text = UpdateService.LastCheck is { } last ? Ui.FormatDateTime(last) : "–";
        var result = UpdateService.LastResult;
        InstallUpdateButton.IsVisible = result is { IsUpdateAvailable: true } && UpdateService.CanUpdate;
        CheckUpdateButton.IsVisible = UpdateService.CanUpdate;
        if (AppInfo.IsLocalBuild)
        {
            UpdateMessage.Show("Lokaler Build – er bekommt keine Updates. Installierte Versionen aus GitHub aktualisieren sich selbst.", InfoSeverity.Info);
        }
        else if (AccessPolicy.AreUpdatesDisabled)
        {
            UpdateMessage.Show("Von Ihrer Organisation festgelegt: Updates sind deaktiviert (Richtlinie DisableUpdates).", InfoSeverity.Warning);
        }
        else if (result is not null)
        {
            UpdateMessage.Show(UpdateService.Downloaded is { } downloaded && result.IsUpdateAvailable
                ? $"Version {downloaded.Manifest.Version} ist geladen und wird beim Schließen installiert."
                : result.Message, result.IsUpdateAvailable ? InfoSeverity.Success : InfoSeverity.Info);
        }
        else
        {
            UpdateMessage.Hide();
        }
    }

    private async void OnCheckUpdateClick(object? sender, RoutedEventArgs e)
    {
        CheckUpdateButton.IsEnabled = false;
        try
        {
            await UpdateCoordinator.CheckAsync();
        }
        catch (BusinessException exception)
        {
            UpdateMessage.Show(exception.Message, InfoSeverity.Error);
        }
        finally
        {
            CheckUpdateButton.IsEnabled = true;
            RefreshUpdate();
        }
    }

    private async void OnInstallUpdateClick(object? sender, RoutedEventArgs e)
    {
        if (UpdateService.LastResult is not { Manifest: { } manifest })
        {
            return;
        }

        InstallUpdateButton.IsEnabled = false;
        UpdateProgress.IsVisible = true;
        try
        {
            await UpdateCoordinator.InstallNowAsync(manifest, new Progress<double>(value => UpdateProgress.Value = value));
        }
        catch (Exception exception) when (exception is BusinessException or HttpRequestException or IOException)
        {
            UpdateMessage.Show(exception.Message, InfoSeverity.Error);
        }
        finally
        {
            InstallUpdateButton.IsEnabled = true;
            UpdateProgress.IsVisible = false;
        }
    }

    private void OnReleasePageClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(UpdateService.ReleasePageUrl(AppInfo.Channel == "local" ? "release" : AppInfo.Channel));

    private void OnOpenDataFolderClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(AppPaths.DataDirectory);

    private void OnOpenLogFolderClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(AppPaths.LogDirectory);
}
