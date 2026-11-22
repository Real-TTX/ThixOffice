using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;

namespace ThixOffice.Desktop.Pages;

/// <summary>
/// The tab "Cloud" (like ThixCalendar's): not signed in, the sign-in (<see cref="CloudSignInPanel"/>: address, device name, browser
/// first, password second); signed in, the address, the account, this device, the state and the space in ThixDrive, "ThixDrive im
/// Browser öffnen" and – set apart – "Abmelden". Hidden for an app without the cloud; off with a note under the policy DisableCloud.
/// </summary>
public partial class SettingsPage
{
    /// <summary>The sign-in of the tab (tests).</summary>
    public CloudSignInPanel CloudSignInPanel => CloudSignIn;

    /// <summary>True while the tab shows the account (signed in).</summary>
    public bool ShowsCloudAccount => CloudConnected.IsVisible;

    private void AttachCloud()
    {
        CloudSignIn.SignedIn += (_, _) =>
        {
            CloudMessage.Show($"Angemeldet. Ihre Dokumente in ThixDrive öffnen Sie unter Datei › Öffnen › ThixDrive.", InfoSeverity.Success);
            LoadCloud();
        };
        CloudService.StatusChanged += OnCloudStatusChanged;
        DetachedFromVisualTree += (_, _) => CloudService.StatusChanged -= OnCloudStatusChanged;
    }

    private void OnCloudStatusChanged(object? sender, CloudStatus status) => Ui.Post(LoadCloud);

    private void LoadCloud()
    {
        var definition = AppInfo.Definition;
        CloudTabItem.IsVisible = definition.HasCloud;
        if (!definition.HasCloud)
        {
            return;
        }

        CloudIntro.Text = $"Mit ThixCloud öffnen und speichern Sie Ihre Dokumente in ThixDrive – auf jedem Computer, in {definition.Name} und im Browser. " +
                          "Was Sie dort speichern, liegt zusätzlich als Arbeitskopie auf diesem Computer: Ohne Verbindung geht nichts verloren, es wird später hochgeladen.";
        var connected = CloudService.IsConnected;
        CloudSignIn.IsVisible = !connected;
        CloudConnected.IsVisible = connected;
        CloudSignedOut.IsVisible = !connected && CloudService.HasAccount;
        CloudSignedOutText.Text = $"{definition.Name} arbeitet weiter mit den Daten des Kontos {CloudService.Settings.AccountText}. Melden Sie sich oben erneut an, " +
                                  "damit Ihre Änderungen synchronisiert werden – oder melden Sie sich ab, um das Konto auf diesem Computer zu verlassen.";
        if (AccessPolicy.IsCloudDisabled)
        {
            CloudMessage.Show("Von Ihrer Organisation festgelegt: die Verbindung zur ThixCloud ist ausgeschaltet (Richtlinie DisableCloud).", InfoSeverity.Warning);
            CloudConnected.IsEnabled = false;
            return;
        }

        if (!connected)
        {
            return;
        }

        var cloud = CloudService.Settings;
        CloudServerText.Text = cloud.ServerUrl;
        CloudAccountText.Text = cloud.AccountText;
        CloudDeviceText.Text = cloud.DeviceName;
        CloudStateText.Text = CloudService.Status.Message;
        _ = LoadQuotaAsync();
    }

    /// <summary>The space of the account in ThixDrive ("1,2 GB von 5 GB belegt"); hidden when the cloud cannot be asked.</summary>
    private async Task LoadQuotaAsync()
    {
        try
        {
            var quota = await DriveService.QuotaAsync();
            CloudQuotaText.Text = quota.QuotaBytes is { } total
                ? $"{DriveBrowser.FormatSize(quota.UsedBytes)} von {DriveBrowser.FormatSize(total)} belegt"
                : $"{DriveBrowser.FormatSize(quota.UsedBytes)} belegt";
            CloudQuotaLabel.IsVisible = CloudQuotaText.IsVisible = true;
        }
        catch (BusinessException)
        {
            CloudQuotaLabel.IsVisible = CloudQuotaText.IsVisible = false;
        }
    }

    private void OnOpenCloudClick(object? sender, RoutedEventArgs e) => Ui.OpenExternal(CloudService.Settings.ServerUrl + "/Drive");

    private async void OnDisconnectClick(object? sender, RoutedEventArgs e) => await SignOutAsync();

    /// <summary>"Abmelden" (signed in, or the account the cloud signed out): the app asks (<see cref="OfficeApplication.SignOutAsync"/>), then the tab follows.</summary>
    public async Task SignOutAsync()
    {
        if (Ui.WindowOf(this) is not { } owner || !await OfficeApplication.Instance.SignOutAsync(owner))
        {
            return;
        }

        CloudMessage.Show("Abgemeldet.", InfoSeverity.Info);
        LoadCloud();
    }
}
