using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Transport;
using ThixMail.Editor;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixMail.Pages;

/// <summary>
/// Datei › Konten: the accounts of this computer as a list (address, servers, state) with Hinzufügen · Bearbeiten · gap · Entfernen below it, and the
/// account form in its place: first name, address and password ("Weiter" finds the settings – provider table, autoconfig, ISPDB, MX, SRV – and tests
/// the connection), then the servers to check or change; saving always tests first. A wrong password, an app password the provider wants, an
/// unreachable server are said in German at the form; a certificate the system does not trust is SHOWN (subject, issuer, validity, SHA-256) and
/// accepted only by an explicit "Diesem Zertifikat vertrauen". Passwords go to the TokenStore only.
/// </summary>
public sealed class AccountsPage : UserControl
{
    public const string PageId = "accounts";

    private readonly OfficeWindow _window;
    private readonly MailDocument _document;
    private readonly ContentControl _host = new();
    private readonly InfoBar _message = new();

    public AccountsPage(OfficeWindow window)
    {
        _window = window;
        _document = ((MailEditor)window.Editor).Document;
        var title = new TextBlock { Text = "Konten" }.Themed("MatText.PageTitle");
        Content = new ScrollViewer
        {
            Content = new StackPanel { Margin = new Thickness(32, 26, 32, 16), MaxWidth = 820, HorizontalAlignment = HorizontalAlignment.Left, Spacing = 10, Children = { title, _message, _host } },
        };
        ShowList();
        if (_document.Accounts.Accounts.Count == 0)
        {
            ShowForm(null);
        }
    }

    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Konten", "user", () => new AccountsPage(window));

    public InfoBar Message => _message;

    /// <summary>The form while adding or editing (tests), else null.</summary>
    public AccountForm? Form => _host.Content as AccountForm;

    public ListBox? List { get; private set; }

    public Button? AddButton { get; private set; }

    public Button? EditButton { get; private set; }

    public Button? RemoveButton { get; private set; }

    public void ShowList()
    {
        var accounts = _document.Accounts.Accounts;
        List = new ListBox
        {
            ItemsSource = accounts.Select(a => new AccountRow(a, _document.StatusOf(a.Id).Message)).ToList(),
            SelectedIndex = accounts.Count > 0 ? 0 : -1,
            MaxHeight = 360,
        };
        AutomationProperties.SetName(List, "Konten");
        AddButton = new Button { Content = new IconLabel { Icon = "plus", Text = "Konto hinzufügen" } }.Themed("MatButton.Primary");
        AddButton.Click += (_, _) => ShowForm(null);
        EditButton = new Button { Content = new IconLabel { Icon = "edit", Text = "Bearbeiten" }, IsEnabled = accounts.Count > 0 };
        EditButton.Click += (_, _) =>
        {
            if (List.SelectedIndex >= 0)
            {
                ShowForm(accounts[List.SelectedIndex]);
            }
        };
        RemoveButton = new Button { Content = new IconLabel { Icon = "trash", Text = "Entfernen" }, IsEnabled = accounts.Count > 0 }.Themed("MatButton.Danger");
        ActionBar.SetSeparated(RemoveButton, true);
        RemoveButton.Click += async (_, _) =>
        {
            if (List.SelectedIndex < 0)
            {
                return;
            }

            var account = accounts[List.SelectedIndex];
            if (await QuestionDialog.ConfirmAsync(_window, "Konto entfernen?",
                    $"„{account.DisplayName}“ wird von diesem Computer entfernt – mit den hier gespeicherten Nachrichten und dem Kennwort. Auf dem Mailserver bleibt alles erhalten.",
                    "Entfernen", isDestructive: true))
            {
                await _document.RemoveAccountAsync(account.Id);
                _message.Show($"„{account.DisplayName}“ wurde entfernt.", InfoSeverity.Info, isClosable: true);
                ShowList();
            }
        };
        var intro = new TextBlock
        {
            Text = accounts.Count == 0 ? "Noch kein Konto eingerichtet." : "ThixMail ruft diese Konten über IMAP ab und sendet über SMTP. Die Nachrichten bleiben auf dem Mailserver und auf diesem Computer – nie in der ThixCloud.",
            TextWrapping = TextWrapping.Wrap,
        };
        intro.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _host.Content = new StackPanel { Spacing = 12, Children = { intro, List, new ActionBar { Children = { AddButton, EditButton, RemoveButton } } } };
    }

    public void ShowForm(MailAccount? account)
    {
        _message.Hide();
        var form = new AccountForm(_window, _document, account);
        form.Finished += (_, saved) =>
        {
            ShowList();
            if (saved is not null)
            {
                _message.Show($"„{saved.DisplayName}“ ist eingerichtet. Die Ordner und Nachrichten werden jetzt geholt.", InfoSeverity.Success, isClosable: true);
            }
        };
        _host.Content = form;
    }

    private sealed class AccountRow : Grid
    {
        public AccountRow(MailAccount account, string state)
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*");
            Margin = new Thickness(0, 4);
            var icon = new MatIcon { Icon = "mail", Size = 20, Margin = new Thickness(0, 0, 12, 0), VerticalAlignment = VerticalAlignment.Center };
            var name = new TextBlock { Text = account.DisplayName == account.Address ? account.Address : $"{account.DisplayName} – {account.Address}", FontWeight = FontWeight.SemiBold };
            var detail = new TextBlock { Text = $"IMAP {account.Imap.Display} · SMTP {account.Smtp.Display}{(state.Length > 0 ? " · " + state : "")}", FontSize = MatTokens.Type.FsSm, TextTrimming = TextTrimming.CharacterEllipsis };
            detail.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            var texts = new StackPanel { Children = { name, detail } };
            Grid.SetColumn(texts, 1);
            Children.Add(icon);
            Children.Add(texts);
            AutomationProperties.SetName(this, name.Text);
        }
    }
}

/// <summary>The form of one account (add or edit) – see <see cref="AccountsPage"/>.</summary>
public sealed class AccountForm : UserControl
{
    private static readonly string[] Securities = ["SSL/TLS", "STARTTLS", "Keine (unverschlüsselt)"];

    private readonly OfficeWindow _window;
    private readonly MailDocument _document;
    private readonly MailAccount? _existing;
    private readonly StackPanel _details = new() { Spacing = 10, IsVisible = false };
    private readonly InfoBar _result = new();
    private readonly Border _certificate = new() { IsVisible = false, CornerRadius = new CornerRadius(8), Padding = new Thickness(14, 10), BorderThickness = new Thickness(1) };
    private readonly TextBlock _certificateText = new() { TextWrapping = TextWrapping.Wrap, FontSize = MatTokens.Type.FsSm };
    private readonly StackPanel _signIn = new() { Spacing = 8, IsVisible = false };
    private readonly MatFormField _passwordField;
    private readonly MatFormField _choiceField;
    private CertificateInfo? _shownCertificate;
    private MailAccount? _trusting;
    private bool _busy;
    private string? _oauthProvider;
    private OAuthTokens? _tokens;
    private CancellationTokenSource? _signInCancel;

    /// <summary>Tests play the browser (the sign-in page of a fake provider); null = the system browser.</summary>
    internal static Func<Uri, Task>? BrowserForTests { get; set; }

    public AccountForm(OfficeWindow window, MailDocument document, MailAccount? existing)
    {
        _window = window;
        _document = document;
        _existing = existing;
        var heading = new TextBlock { Text = existing is null ? "Konto hinzufügen" : $"„{existing.DisplayName}“ bearbeiten" }.Themed("MatText.SectionTitle");

        SenderBox = Box("Ihr Name", existing?.SenderName ?? SettingsDefaults.UserName);
        AddressBox = Box("E-Mail-Adresse", existing?.Address ?? "");
        PasswordBox = new TextBox { PasswordChar = '•', Watermark = existing is null ? "" : "Unverändert lassen" };
        AutomationProperties.SetName(PasswordBox, "Kennwort");
        ImapHost = Box("Posteingangsserver (IMAP)", existing?.Imap.Host ?? "");
        ImapPort = Box("Port", existing is null ? "993" : existing.Imap.Port.ToString(CultureInfo.InvariantCulture));
        ImapSecurity = Choice("Verschlüsselung (IMAP)", existing?.Imap.Security ?? ConnectionSecurity.SslOnConnect);
        ImapUser = Box("Benutzername (IMAP)", existing?.ImapUser ?? "");
        SmtpHost = Box("Postausgangsserver (SMTP)", existing?.Smtp.Host ?? "");
        SmtpPort = Box("Port", existing is null ? "587" : existing.Smtp.Port.ToString(CultureInfo.InvariantCulture));
        SmtpSecurity = Choice("Verschlüsselung (SMTP)", existing?.Smtp.Security ?? ConnectionSecurity.StartTls);
        SmtpUser = Box("Benutzername (SMTP)", existing?.SmtpUser ?? "");
        SmtpOwnPassword = new CheckBox { Content = "Der Postausgangsserver hat ein eigenes Kennwort", IsChecked = existing is { SmtpUsesImapPassword: false } };
        SmtpPasswordBox = new TextBox { PasswordChar = '•', IsVisible = existing is { SmtpUsesImapPassword: false } };
        AutomationProperties.SetName(SmtpPasswordBox, "Kennwort (SMTP)");
        SmtpOwnPassword.IsCheckedChanged += (_, _) => SmtpPasswordBox.IsVisible = SmtpOwnPassword.IsChecked == true;
        NameBox = Box("Kontoname (im Ordnerbereich)", existing?.Name ?? "");
        SignInChoice = new ComboBox { ItemsSource = new[] { "Anmeldung im Browser (empfohlen)", "Kennwort oder App-Passwort" }, SelectedIndex = existing is { UsesOAuth: false } ? 1 : 0, MinWidth = 260 };
        AutomationProperties.SetName(SignInChoice, "Anmeldung");
        SignInChoice.SelectionChanged += (_, _) => UpdateSignIn();
        BrowserButton = new Button { Content = new IconLabel { Icon = "login", Text = existing is { UsesOAuth: true } ? "Erneut im Browser anmelden" : "Im Browser anmelden" } }.Themed("MatButton.Primary");
        BrowserButton.Click += async (_, _) => await SignInInBrowserAsync();
        SignInText = new TextBlock { TextWrapping = TextWrapping.Wrap, FontSize = MatTokens.Type.FsSm };
        SignInText.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        _choiceField = new MatFormField { Label = "Anmeldung", Content = SignInChoice };
        _signIn.Children.Add(_choiceField);
        _signIn.Children.Add(SignInText);
        _signIn.Children.Add(new ActionBar { Children = { BrowserButton } });

        _details.Children.Add(new TextBlock { Text = "Server" }.Themed("MatText.SectionTitle"));
        _details.Children.Add(Pair(Field("Posteingangsserver (IMAP)", ImapHost, nameof(MailAccount.Imap)), Field("Port", ImapPort, null), Field("Verschlüsselung", ImapSecurity, null)));
        _details.Children.Add(Field("Benutzername (IMAP)", ImapUser, nameof(MailAccount.ImapUser)));
        _details.Children.Add(Pair(Field("Postausgangsserver (SMTP)", SmtpHost, nameof(MailAccount.Smtp)), Field("Port", SmtpPort, null), Field("Verschlüsselung", SmtpSecurity, null)));
        _details.Children.Add(Field("Benutzername (SMTP)", SmtpUser, null));
        _details.Children.Add(SmtpOwnPassword);
        _details.Children.Add(SmtpPasswordBox);
        _details.Children.Add(Field("Kontoname", NameBox, nameof(MailAccount.Name)));

        _certificate.Use(Border.BackgroundProperty, "Mat.WarningBg");
        _certificate.Use(Border.BorderBrushProperty, "Mat.Border");
        TrustButton = new Button { Content = new IconLabel { Icon = "shield", Text = "Diesem Zertifikat vertrauen" } }.Themed("MatButton.Danger");
        TrustButton.Click += async (_, _) => await TrustAsync();
        _certificate.Child = new StackPanel { Spacing = 8, Children = { new TextBlock { Text = "Zertifikat des Servers", FontWeight = FontWeight.SemiBold }, _certificateText, TrustButton } };

        NextButton = new Button { Content = new IconLabel { Icon = "arrow-right", Text = "Weiter" } }.Themed("MatButton.Primary");
        NextButton.Click += async (_, _) => await DiscoverAsync();
        ManualButton = new Button { Content = new IconLabel { Icon = "settings", Text = "Manuell einrichten" } };
        ManualButton.Click += (_, _) => ShowDetails(null);
        TestButton = new Button { Content = new IconLabel { Icon = "plug", Text = "Verbindung testen" } };
        TestButton.Click += async (_, _) => await TestAsync();
        SaveButton = new Button { Content = new IconLabel { Icon = "save", Text = existing is null ? "Konto hinzufügen" : "Speichern" } }.Themed("MatButton.Primary");
        SaveButton.Click += async (_, _) => await SaveAsync();
        var cancel = new Button { Content = new IconLabel { Icon = "x", Text = "Abbrechen" } };
        cancel.Click += (_, _) =>
        {
            _signInCancel?.Cancel();
            Finished?.Invoke(this, null);
        };
        CancelButton = cancel;
        var start = new ActionBar { Children = { NextButton, ManualButton, CancelButton } };
        _passwordField = Field("Kennwort", PasswordBox, "Password");
        var save = new ActionBar { Children = { SaveButton, TestButton } };
        save.IsVisible = false;
        _details.Children.Add(save);
        StartActions = start;

        Content = new StackPanel
        {
            Spacing = 10,
            MaxWidth = 640,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children =
            {
                heading,
                Field("Ihr Name", SenderBox, nameof(MailAccount.SenderName)),
                Field("E-Mail-Adresse", AddressBox, nameof(MailAccount.Address)),
                _passwordField,
                _signIn,
                start,
                _result,
                _certificate,
                _details,
            },
        };
        if (existing is not null)
        {
            ShowDetails(null);
        }

        AddressBox.TextChanged += (_, _) => DecideSignIn(null);
        DecideSignIn(null);
    }

    /// <summary>Raised when the form is done: the saved account, or null when cancelled.</summary>
    public event EventHandler<MailAccount?>? Finished;

    public TextBox SenderBox { get; }
    public TextBox AddressBox { get; }
    public TextBox PasswordBox { get; }
    public TextBox ImapHost { get; }
    public TextBox ImapPort { get; }
    public ComboBox ImapSecurity { get; }
    public TextBox ImapUser { get; }
    public TextBox SmtpHost { get; }
    public TextBox SmtpPort { get; }
    public ComboBox SmtpSecurity { get; }
    public TextBox SmtpUser { get; }
    public CheckBox SmtpOwnPassword { get; }
    public TextBox SmtpPasswordBox { get; }
    public TextBox NameBox { get; }
    public Button NextButton { get; }
    public Button ManualButton { get; }
    public Button TestButton { get; }
    public Button SaveButton { get; }
    public Button CancelButton { get; }
    public Button TrustButton { get; }
    public ActionBar StartActions { get; }
    public InfoBar Result => _result;
    public ComboBox SignInChoice { get; }
    public Button BrowserButton { get; }
    public TextBlock SignInText { get; }

    /// <summary>The account signs in in the browser (the provider offers it, ThixMail has a client id for it, the person chose it).</summary>
    public bool UsesBrowserSignIn => _oauthProvider is not null && OAuthClients.IsAvailable(_oauthProvider) && SignInChoice.SelectedIndex == 0;

    /// <summary>The choice of sign-in shows (the provider offers OAuth).</summary>
    public bool ShowsSignInChoice => _signIn.IsVisible;

    /// <summary>The choice "Anmeldung im Browser" / "Kennwort" shows (ThixMail has a client id for the provider).</summary>
    public bool OffersBrowserSignIn => _signIn.IsVisible && _choiceField.IsVisible;

    /// <summary>Signed in in the browser during this form (tests).</summary>
    public bool HasBrowserSignIn => _tokens is not null;
    public bool ShowsDetails => _details.IsVisible;
    public bool ShowsCertificate => _certificate.IsVisible;

    /// <summary>
    /// Which sign-in the account uses: for a provider that signs in in the browser (Gmail, Outlook.com, Microsoft 365 – by the address, the discovered
    /// settings or the IMAP host) the choice shows; without a client id for that provider it says honestly that an app password is needed for now.
    /// </summary>
    private void DecideSignIn(AutoConfigResult? found)
    {
        var provider = found?.OAuthProvider ?? AccountService.OAuthProviderFor(AddressBox.Text ?? "", ImapHost.Text ?? "") ?? _existing?.OAuthProvider;
        if (provider != _oauthProvider)
        {
            _tokens = null;
        }

        _oauthProvider = provider;
        UpdateSignIn();
    }

    private void UpdateSignIn()
    {
        var provider = OAuthProviders.Find(_oauthProvider);
        if (provider is null)
        {
            _signIn.IsVisible = false;
            _passwordField.IsVisible = true;
            SmtpOwnPassword.IsVisible = true;
            return;
        }

        _signIn.IsVisible = true;
        var available = OAuthClients.IsAvailable(provider.Id);
        _choiceField.IsVisible = available;
        BrowserButton.IsVisible = available && SignInChoice.SelectedIndex == 0;
        var browser = UsesBrowserSignIn;
        _passwordField.IsVisible = !browser;
        SmtpOwnPassword.IsVisible = !browser;
        SmtpPasswordBox.IsVisible = !browser && SmtpOwnPassword.IsChecked == true;
        SignInText.Text = !available
            ? provider.Id == OAuthProviders.MicrosoftId
                ? "Microsoft lässt IMAP und SMTP für Outlook.com und die meisten Microsoft-365-Konten nur mit der Anmeldung im Browser zu. Diese Anmeldung ist in " +
                  "dieser Version von ThixMail noch nicht freigeschaltet – bis dahin lässt sich ein solches Konto nur einrichten, wenn Ihr Postfach ein App-Passwort " +
                  "oder die Anmeldung mit Kennwort erlaubt."
                : $"Die Anmeldung im Browser ist für {provider.Name} in dieser Version von ThixMail noch nicht freigeschaltet. Bis dahin brauchen Sie ein App-Passwort " +
                  "(Google-Konto › Sicherheit › Bestätigung in zwei Schritten › App-Passwörter)."
            : browser
                ? _tokens is not null
                    ? $"Sie sind bei {provider.Name} angemeldet. ThixMail bewahrt nur die Anmeldung auf, nie Ihr Kennwort."
                    : $"Ihr Browser öffnet die Anmeldeseite von {provider.Name}. Sie geben Ihr Kennwort dort ein, nicht in ThixMail; ThixMail bekommt nur die Erlaubnis, Ihre Mails abzurufen und zu senden."
                : $"{provider.Name} verlangt dafür ein App-Passwort statt Ihres normalen Kennworts.";
    }

    /// <summary>"Im Browser anmelden": the provider's sign-in in the system browser; the tokens are kept for the test and the save.</summary>
    public async Task<bool> SignInInBrowserAsync()
    {
        if (_busy || OAuthProviders.Find(_oauthProvider) is not { } provider)
        {
            return false;
        }

        FormErrors.ShowError(this, null, "");
        _busy = true;
        _signInCancel = new CancellationTokenSource();
        _result.Show($"Bitte melden Sie sich im Browser bei {provider.Name} an …", InfoSeverity.Info);
        try
        {
            var open = BrowserForTests ?? (uri =>
            {
                Ui.OpenExternal(uri.AbsoluteUri);
                return Task.CompletedTask;
            });
            _tokens = await OAuthService.AuthorizeAsync(provider, (AddressBox.Text ?? "").Trim(), open, _signInCancel.Token);
            _window.Activate();
        }
        catch (OAuthException exception)
        {
            _result.Show(exception.Message, exception.Kind is OAuthErrorKind.Cancelled ? InfoSeverity.Warning : InfoSeverity.Error);
            return false;
        }
        finally
        {
            _busy = false;
            _signInCancel = null;
            UpdateSignIn();
        }

        if (!ShowsDetails)
        {
            ShowDetails(null);
        }

        return await TestAsync();
    }

    private static TextBox Box(string name, string text)
    {
        var box = new TextBox { Text = text };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static ComboBox Choice(string name, ConnectionSecurity security)
    {
        var box = new ComboBox { ItemsSource = Securities, SelectedIndex = (int)security, MinWidth = 150 };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private static MatFormField Field(string label, Control control, string? field)
    {
        var formField = new MatFormField { Label = label, Content = control };
        if (field is not null)
        {
            FormErrors.SetField(formField, field);
        }

        return formField;
    }

    private static Grid Pair(Control host, Control port, Control security)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,90,170") };
        port.Margin = new Thickness(10, 0, 0, 0);
        security.Margin = new Thickness(10, 0, 0, 0);
        Grid.SetColumn(port, 1);
        Grid.SetColumn(security, 2);
        grid.Children.Add(host);
        grid.Children.Add(port);
        grid.Children.Add(security);
        return grid;
    }

    /// <summary>"Manuell einrichten": the server fields without searching.</summary>
    public void ShowManual() => ShowDetails(null);

    /// <summary>What the auto-configuration found, shown as after "Weiter" (snapshots).</summary>
    public void ShowFound(AutoConfigResult found)
    {
        ShowDetails(found);
        DecideSignIn(found);
        if (found.Hint is { } hint && !_signIn.IsVisible)
        {
            _result.Show(hint, found.NeedsAppPassword ? InfoSeverity.Warning : InfoSeverity.Info);
        }
    }

    private void ShowDetails(AutoConfigResult? found)
    {
        if (found is not null)
        {
            ImapHost.Text = found.Imap.Host;
            ImapPort.Text = found.Imap.Port.ToString(CultureInfo.InvariantCulture);
            ImapSecurity.SelectedIndex = (int)found.Imap.Security;
            ImapUser.Text = found.ImapUser;
            SmtpHost.Text = found.Smtp.Host;
            SmtpPort.Text = found.Smtp.Port.ToString(CultureInfo.InvariantCulture);
            SmtpSecurity.SelectedIndex = (int)found.Smtp.Security;
            SmtpUser.Text = found.SmtpUser;
        }
        else if ((ImapUser.Text ?? "").Length == 0)
        {
            ImapUser.Text = AddressBox.Text;
            SmtpUser.Text = AddressBox.Text;
        }

        _details.IsVisible = true;
        StartActions.IsVisible = false;
        _details.Children.OfType<ActionBar>().Last().IsVisible = true;
    }

    /// <summary>The account as the form describes it now (not validated).</summary>
    public MailAccount AccountFromForm()
    {
        var provider = AutoConfig.ProviderOf(AddressBox.Text ?? "");
        var basis = _trusting ?? _existing ?? new MailAccount();
        return basis with
        {
            SenderName = SenderBox.Text ?? "",
            Address = (AddressBox.Text ?? "").Trim(),
            Name = NameBox.Text ?? "",
            Imap = new ServerSettings(ImapHost.Text ?? "", Port(ImapPort.Text), (ConnectionSecurity)Math.Max(0, ImapSecurity.SelectedIndex)),
            ImapUser = ImapUser.Text ?? "",
            Smtp = new ServerSettings(SmtpHost.Text ?? "", Port(SmtpPort.Text), (ConnectionSecurity)Math.Max(0, SmtpSecurity.SelectedIndex)),
            SmtpUser = SmtpUser.Text ?? "",
            SmtpUsesImapPassword = SmtpOwnPassword.IsChecked != true,
            ServerSavesSent = _existing?.ServerSavesSent ?? provider?.ServerSavesSent ?? false,
            Authentication = UsesBrowserSignIn ? MailAuthentication.OAuth : MailAuthentication.Password,
            OAuthProvider = UsesBrowserSignIn ? _oauthProvider : null,
        };
    }

    private static int Port(string? text) => int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var port) ? port : 0;

    /// <summary>"Weiter": the settings for the address, then the connection test.</summary>
    public async Task DiscoverAsync()
    {
        if (_busy)
        {
            return;
        }

        FormErrors.ShowError(this, null, "");
        var address = (AddressBox.Text ?? "").Trim();
        if (!AccountService.IsAddress(address))
        {
            FormErrors.ShowError(this, nameof(MailAccount.Address), "Bitte geben Sie eine gültige E-Mail-Adresse an (z. B. anna@example.de).");
            return;
        }

        DecideSignIn(null);
        if (!UsesBrowserSignIn && (PasswordBox.Text ?? "").Length == 0 && (_oauthProvider is null || !OAuthClients.IsAvailable(_oauthProvider)))
        {
            FormErrors.ShowError(this, "Password", "Bitte geben Sie das Kennwort des Postfachs an.");
            return;
        }

        _busy = true;
        _result.Show("Die Einstellungen werden gesucht …", InfoSeverity.Info);
        try
        {
            var found = await AutoConfig.DiscoverAsync(address, CancellationToken.None);
            ShowDetails(found);
            DecideSignIn(found);
            if (found.Hint is { } hint && !_signIn.IsVisible)
            {
                _result.Show(hint, found.NeedsAppPassword ? InfoSeverity.Warning : InfoSeverity.Info);
            }
            else
            {
                _result.Hide();
            }
        }
        finally
        {
            _busy = false;
        }

        if (UsesBrowserSignIn)
        {
            await SignInInBrowserAsync();
            return;
        }

        if ((PasswordBox.Text ?? "").Length == 0)
        {
            FormErrors.ShowError(this, "Password", "Bitte geben Sie das Kennwort des Postfachs an.");
            return;
        }

        await TestAsync();
    }

    /// <summary>"Verbindung testen": signs in at both servers; a failure explained, a certificate shown.</summary>
    public async Task<bool> TestAsync()
    {
        if (_busy)
        {
            return false;
        }

        FormErrors.ShowError(this, null, "");
        MailAccount account;
        try
        {
            account = AccountService.Validate(AccountFromForm());
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                _result.Show(exception.Message, InfoSeverity.Error);
            }

            return false;
        }

        var password = PasswordBox.Text ?? "";
        if (account.UsesOAuth)
        {
            password = _tokens?.AccessToken ?? "";
            if (password.Length == 0 && _existing is { UsesOAuth: true } && !_document.NeedsConfirmation(_existing))
            {
                try
                {
                    password = await AccountService.CredentialAsync(_document.Accounts, _existing, AccountService.ImapSecret, false, CancellationToken.None) ?? "";
                }
                catch (MailConnectionException exception)
                {
                    _result.Show(exception.Message, InfoSeverity.Error);
                    return false;
                }
            }

            if (password.Length == 0)
            {
                _result.Show("Bitte melden Sie sich zuerst im Browser an.", InfoSeverity.Warning);
                return false;
            }
        }
        else if (password.Length == 0 && _existing is not null && !_document.NeedsConfirmation(_existing)) // never this computer's password to servers another computer named
        {
            password = AccountService.Password(_document.Accounts, _existing, AccountService.ImapSecret) ?? "";
        }

        var smtpPassword = !account.UsesOAuth && SmtpOwnPassword.IsChecked == true ? SmtpPasswordBox.Text ?? "" : password;
        _busy = true;
        _result.Show("Die Verbindung wird getestet …", InfoSeverity.Info);
        _certificate.IsVisible = false;
        ConnectionTestResult result;
        try
        {
            result = await AccountService.TestAsync(MailDocument.Transport, account, password, smtpPassword, CancellationToken.None);
        }
        finally
        {
            _busy = false;
        }

        if (result.Succeeded)
        {
            _result.Show(result.Message, InfoSeverity.Success);
            return true;
        }

        _result.Show(result.Message, InfoSeverity.Error);
        if (result.Kind is MailErrorKind.Authentication or MailErrorKind.AppPasswordRequired)
        {
            FormErrors.ShowError(this, "Password", result.Kind == MailErrorKind.AppPasswordRequired ? "Hier gehört das App-Passwort des Anbieters hin." : "Das Kennwort wurde abgelehnt.");
        }

        if (result.Certificate is { } certificate)
        {
            _shownCertificate = certificate;
            _certificateText.Text = $"Server: {certificate.Host}\nAusgestellt für: {certificate.Subject}\nAusgestellt von: {certificate.Issuer}\n" +
                                    $"Gültig: {certificate.NotBefore.LocalDateTime:d} bis {certificate.NotAfter.LocalDateTime:d}\nSHA-256: {certificate.Thumbprint}\n\n{certificate.Problem}\n" +
                                    "Vertrauen Sie diesem Zertifikat nur, wenn Sie wissen, dass es zu Ihrem Mailserver gehört (z. B. vom Administrator bestätigt).";
            _certificate.IsVisible = true;
        }

        return false;
    }

    private async Task TrustAsync()
    {
        if (_shownCertificate is null)
        {
            return;
        }

        _trusting = AccountService.TrustCertificate(AccountFromForm(), _shownCertificate);
        _certificate.IsVisible = false;
        await TestAsync();
    }

    /// <summary>Speichern / Konto hinzufügen: the test must pass, then the account and its passwords are kept and its sync starts.</summary>
    public async Task<bool> SaveAsync()
    {
        if (!await TestAsync())
        {
            return false;
        }

        try
        {
            var account = AccountFromForm();
            var password = PasswordBox.Text ?? "";
            var smtpPassword = SmtpOwnPassword.IsChecked == true ? SmtpPasswordBox.Text : null;
            MailAccount saved;
            if (_existing is null)
            {
                saved = _document.AddAccount(account, password, smtpPassword, _tokens);
            }
            else
            {
                saved = _document.UpdateAccount(account, password.Length > 0 ? password : null, smtpPassword is { Length: > 0 } ? smtpPassword : null, _tokens);
            }

            Finished?.Invoke(this, saved);
            return true;
        }
        catch (BusinessException exception)
        {
            if (!FormErrors.ShowError(this, exception.Field, exception.Message))
            {
                _result.Show(exception.Message, InfoSeverity.Error);
            }

            return false;
        }
    }

    private static class SettingsDefaults
    {
        public static string UserName => ThixOffice.Core.Business.SettingsService.AuthorName;
    }
}
