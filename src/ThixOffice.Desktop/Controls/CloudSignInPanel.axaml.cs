using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using MatCloud.Contract;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Desktop.Controls;

/// <summary>
/// The sign-in at ThixCloud (one code base for Einstellungen › Cloud and the place ThixDrive): the address (and in the settings the
/// device name), then "Im Browser anmelden" first or "Mit Benutzername und Passwort" second. Everything goes through
/// <see cref="CloudService"/> (policy, validation, the token); a refusal shows at its field. <see cref="SignedIn"/> tells the page.
/// </summary>
public partial class CloudSignInPanel : UserControl
{
    private CancellationTokenSource? _browserSignIn;
    private string? _browserAddress;

    public CloudSignInPanel()
    {
        InitializeComponent();
        var app = AppInfo.IsInitialized ? AppInfo.ProductName : "Die App";
        HeadingText.Text = $"Melden Sie sich an, um {(AppInfo.IsInitialized ? AppInfo.Definition.CloudPurpose : "Ihre Dokumente in ThixDrive zu öffnen und zu speichern – auf jedem Computer und im Browser")}.";
        BrowserText.Text = $"Die Anmeldung öffnet sich im Browser: Melden Sie sich dort an – mit Ihrem Konto der ThixCloud oder über MatIdentity – und bestätigen Sie den Code, den {app} Ihnen zeigt.";
        BrowserCodeHint.Text = $"Melden Sie sich auf der Seite der ThixCloud an und prüfen Sie, dass dort derselbe Code steht. {app} verbindet sich, sobald Sie bestätigt haben.";
        Load();
    }

    /// <summary>Raised on the UI thread after a successful sign-in.</summary>
    public event EventHandler? SignedIn;

    /// <summary>Compact (the place ThixDrive): a heading, no device name. Off in the settings.</summary>
    public bool IsCompact
    {
        get => Heading.IsVisible;
        set
        {
            Heading.IsVisible = value;
            DeviceNameField.IsVisible = !value;
        }
    }

    /// <summary>A sign-in in the browser is waiting for its confirmation (tests).</summary>
    public bool IsSigningInInBrowser => _browserSignIn is not null;

    /// <summary>The fields (tests).</summary>
    public TextBox ServerUrlField => ServerUrlBox;

    public TextBox UserNameField => UserNameBox;

    public TextBox PasswordField => PasswordBox;

    public InfoBar MessageBar => Message;

    /// <summary>The way to sign in: in the browser (first) or with user name and password (second).</summary>
    public void ShowSignInWay(bool password)
    {
        BrowserCode.IsVisible = false;
        BrowserSignIn.IsVisible = !password;
        PasswordSignIn.IsVisible = password;
    }

    /// <summary>The code of a started sign-in in the browser, big, with the hint to confirm it there.</summary>
    public void ShowBrowserCode(DeviceLinkStart start)
    {
        _browserAddress = start.VerificationUrl;
        BrowserCodeText.Text = start.Code;
        BrowserSignIn.IsVisible = PasswordSignIn.IsVisible = false;
        BrowserCode.IsVisible = true;
        FocusAfterLayout(CancelLinkButton);
    }

    /// <summary>"Anmelden" with the user name and password typed (tests run it like the button).</summary>
    public async Task ConnectWithPasswordAsync()
    {
        ConnectButton.IsEnabled = false;
        Message.Hide();
        try
        {
            FormErrors.ShowError(this, null, "");
            await CloudService.ConnectAsync(ServerUrlBox.Text ?? "", UserNameBox.Text ?? "", PasswordBox.Text ?? "", DeviceNameBox.Text ?? "");
            PasswordBox.Text = "";
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
        catch (BusinessException exception)
        {
            ShowError(exception);
        }
        finally
        {
            ConnectButton.IsEnabled = true;
        }
    }

    private void Load()
    {
        if (!AppInfo.IsInitialized)
        {
            return;
        }

        var cloud = CloudService.Settings;
        ServerUrlBox.Text = cloud.ServerUrl;
        UserNameBox.Text = cloud.UserName;
        DeviceNameBox.Text = cloud.DeviceName.Length > 0 ? cloud.DeviceName : AppInfo.MachineName;
        IsEnabled = !AccessPolicy.IsCloudDisabled;
        if (CloudService.Status is { State: CloudState.SignedOut } signedOut)
        {
            Message.Show(signedOut.Message, InfoSeverity.Warning);
        }
    }

    /// <summary>The first way: the cloud shows a code, the browser opens, the person confirms it there; the app waits (Abbrechen ends it).</summary>
    private async void OnBrowserConnectClick(object? sender, RoutedEventArgs e)
    {
        if (_browserSignIn is not null)
        {
            return;
        }

        Message.Hide();
        FormErrors.ShowError(this, null, "");
        _browserSignIn = new CancellationTokenSource();
        ServerUrlBox.IsEnabled = DeviceNameBox.IsEnabled = BrowserConnectButton.IsEnabled = ShowPasswordSignInButton.IsEnabled = false;
        var signedIn = false;
        try
        {
            signedIn = await CloudService.ConnectInBrowserAsync(ServerUrlBox.Text ?? "", DeviceNameBox.Text ?? "", start => Ui.Post(() => ShowBrowserCode(start)),
                Ui.OpenExternal, _browserSignIn.Token);
        }
        catch (BusinessException exception)
        {
            ShowError(exception);
        }
        finally
        {
            _browserSignIn.Dispose();
            _browserSignIn = null;
            ServerUrlBox.IsEnabled = DeviceNameBox.IsEnabled = BrowserConnectButton.IsEnabled = ShowPasswordSignInButton.IsEnabled = true;
            ShowSignInWay(password: false);
        }

        if (signedIn)
        {
            SignedIn?.Invoke(this, EventArgs.Empty);
        }
    }

    private void ShowError(BusinessException exception)
    {
        if (!FormErrors.ShowError(this, exception.Field, exception.Message))
        {
            Message.Show(exception.Message, InfoSeverity.Error);
        }
    }

    private void OnShowPasswordSignInClick(object? sender, RoutedEventArgs e)
    {
        ShowSignInWay(password: true);
        FocusAfterLayout(string.IsNullOrEmpty(UserNameBox.Text) ? UserNameBox : PasswordBox);
    }

    private void OnShowBrowserSignInClick(object? sender, RoutedEventArgs e)
    {
        ShowSignInWay(password: false);
        FocusAfterLayout(BrowserConnectButton);
    }

    /// <summary>A control that has just become visible takes the focus once it is laid out (before that it cannot).</summary>
    private static void FocusAfterLayout(Control control) =>
        Dispatcher.UIThread.Post(() => control.Focus(NavigationMethod.Tab), DispatcherPriority.Loaded);

    private void OnOpenLinkClick(object? sender, RoutedEventArgs e)
    {
        if (_browserAddress is not null)
        {
            Ui.OpenExternal(_browserAddress);
        }
    }

    private void OnCancelLinkClick(object? sender, RoutedEventArgs e) => _browserSignIn?.Cancel();

    private async void OnConnectClick(object? sender, RoutedEventArgs e) => await ConnectWithPasswordAsync();
}
