using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MatDesktop;
using ThixMail.Compose;
using ThixMail.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Sync;
using ThixMail.Editor;
using ThixMail.Pages;
using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;

namespace ThixMail;

/// <summary>
/// ThixMail on ThixOffice.Desktop – a workspace app (<see cref="OfficeAppKind.Workspace"/>): the definition (<see cref="ThixMailApp"/>), the one
/// "document" (<see cref="MailDocument"/>: store, accounts, syncs), the editor (<see cref="MailEditor"/>), its commands, ribbon, backstage pages
/// (Konten, Drucken, Als PDF speichern), settings tabs (Lesen, Verfassen, Signaturen, Benachrichtigungen), notifications for new mail, the optional
/// life in the notification area, self test and snapshot scenes. An .eml handed over by the system opens in a message window. Start-up, single
/// instance, setup, updates, the window, Einstellungen (Cloud too) and Info are OfficeApplication's.
/// </summary>
public partial class App : OfficeApplication
{
    public const string ConversationScene = "main-conversation";
    public const string ComposeScene = "compose";
    public const string AccountScene = "backstage-add-account";
    public const string BrowserSignInScene = "backstage-add-account-browser";
    public const string NoClientScene = "backstage-add-account-microsoft";
    public const string AttachmentsScene = "main-attachments";
    public const string OfflineScene = "main-offline";
    public const string SignaturesScene = "backstage-settings-signatures";
    public const string NewsletterScene = "main-newsletter";
    public const string CalendarScene = "main-calendar";
    public const string CalendarMonthScene = "main-calendar-month";
    public const string PeopleScene = "main-people";
    public const string InvitationScene = "main-invitation";
    public const string CalendarSignInScene = "main-calendar-signin";
    public const string AppointmentScene = "dialog-appointment";

    private MatTray? _tray;
    private bool _quitting;

    public override OfficeAppDefinition Definition => ThixMailApp.Definition;

    public override IReadOnlyList<string> SnapshotScenes => [ConversationScene, NewsletterScene, ComposeScene, AccountScene, BrowserSignInScene, NoClientScene, AttachmentsScene, OfflineScene, SignaturesScene, CalendarSignInScene, CalendarScene, CalendarMonthScene, AppointmentScene, PeopleScene, InvitationScene];

    public override MatRibbonDefinition CreateRibbon(OfficeWindow window) => MailRibbon.Create(window);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    public override OfficeDocument CreateDocument(string untitledName) => new MailDocument("E-Mail");

    public override OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document)
    {
        var mail = (MailDocument)document;
        var editor = new MailEditor(window, mail);
        mail.NewMail += (_, e) => Ui.Post(() => Notify(window, editor, e));
        window.Closing += (_, e) =>
        {
            if (!_quitting && SettingsService.AppValue(MailDocument.Settings.KeepRunning) == "true" && !e.IsProgrammatic)
            {
                // keep fetching mail in the notification area
                e.Cancel = true;
                window.Hide();
                ShowTray(window);
            }
        };
        window.Closed += async (_, _) =>
        {
            _tray?.Dispose();
            _tray = null;
            await mail.CloseAsync();
        };
        return editor;
    }

    public override void AddCommands(OfficeWindow window)
    {
        MailCommands.Add(window);
        CalendarCommands.Add(window);
    }

    public override IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) =>
        [AccountsPage.Item(window), MailPrintPage.Item(window), MailPdfPage.Item(window)];

    public override IEnumerable<SettingsTab> CreateSettingsTabs(OfficeWindow window) => MailSettings.Tabs(window);

    /// <summary>An .eml handed over by the system: shown in a message window (read-only; reply and forward work from there).</summary>
    public override async Task ImportFilesAsync(OfficeWindow window, IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            if (MailLinks.IsLink(file))
            {
                OpenLink(window, file);
                continue;
            }

            try
            {
                var raw = EmlFiles.Read(file);
                new MessageWindow(window, MailReader.Read(raw), null, raw).Show(window);
            }
            catch (BusinessException exception)
            {
                await QuestionDialog.ShowMessageAsync(window, "Öffnen nicht möglich", exception.Message);
            }
        }
    }

    public override IEnumerable<(string Name, Func<Task<string>> Check)> SelfTestChecks() =>
        [("ThixMail: store, sync, MIME, sending, safety, the main window", () => MailSelfTest.RunAsync(this))];

    /// <summary>A notification for new unread mail – unless switched off or the window is in front.</summary>
    private static void Notify(OfficeWindow window, MailEditor editor, NewMailEventArgs e)
    {
        // in front = active, shown and not minimized (Windows keeps a minimized window "active" until another one takes the focus)
        var inFront = window.IsActive && window.IsVisible && window.WindowState != WindowState.Minimized;
        if (SettingsService.AppValue(MailDocument.Settings.Notifications) == "false" || inFront || e.Messages.Count == 0)
        {
            Log.Info($"New mail ({e.Messages.Count}): no notification (window in front: {inFront})");
            return;
        }

        var first = e.Messages[0];
        var title = e.Messages.Count == 1 ? first.From.Display : $"{e.Messages.Count} neue Nachrichten";
        var text = e.Messages.Count == 1 ? first.Subject : string.Join(" · ", e.Messages.Take(3).Select(m => m.From.Display));
        MailNotifications.LinkActivated ??= link => Ui.Post(() => OpenLink(window, link));
        MailNotifications.Notifier.Show(new MailNotice(title, text, MailLinks.Message(first.Id), () => OpenMessage(window, editor, first.Id)));
    }

    /// <summary>A link of a notification (<c>thixmail:message?id=…</c>): the window to the front with the message selected.</summary>
    internal static void OpenLink(OfficeWindow window, string link)
    {
        if (window.Editor is not MailEditor editor)
        {
            return;
        }

        if (MailLinks.MessageId(link) is { } id)
        {
            OpenMessage(window, editor, id);
            return;
        }

        window.Show();
        window.BringToFront();
    }

    private static void OpenMessage(OfficeWindow window, MailEditor editor, long messageId)
    {
        window.Show();
        window.BringToFront();
        if (editor.Document.Store.Header(messageId) is { } header)
        {
            editor.ShowView(MailArea.Mail);
            editor.SelectFolder(header.FolderId);
            editor.Select(header.Id);
        }
    }

    private void ShowTray(OfficeWindow window)
    {
        if (_tray is not null)
        {
            return;
        }

        try
        {
            var menu = new MatTrayMenu(Definition.Name, AppInfo.Version, () =>
            {
                _quitting = true;
                window.Close();
            })
            {
                Open = () =>
                {
                    window.Show();
                    window.BringToFront();
                },
                OpenText = "ThixMail öffnen",
            };
            _tray = new MatTray(this, menu, Logo);
        }
        catch (Exception exception) when (exception is InvalidOperationException or PlatformNotSupportedException)
        {
            Log.Warning($"No notification area icon: {exception.GetType().Name}");
            window.Show();
        }
    }

    // ===== snapshots =====

    protected override void FillSampleDocument(OfficeDocument document)
    {
        var mail = (MailDocument)document;
        Task.Run(() => SampleMailbox.FillAsync(mail, SampleMailbox.CreateServer())).GetAwaiter().GetResult();
    }

    public override void EndWorkspaceSample(OfficeWindow window)
    {
    }

    public override TopLevel ShowSnapshotScene(OfficeWindow window, string name)
    {
        var editor = (MailEditor)window.Editor;
        Dispatcher.UIThread.RunJobs();
        switch (name)
        {
            case ConversationScene:
                editor.SelectFolder(null);
                Dispatcher.UIThread.RunJobs();
                var thread = editor.Rows.FirstOrDefault(r => r is { Kind: RowKind.Message, Count: > 1 });
                if (thread is not null)
                {
                    editor.ToggleConversation(thread.Thread, true);
                }

                return window;
            case NewsletterScene:
                SelectSubject(editor, "Neu im November");
                return window;
            case AttachmentsScene:
                SelectSubject(editor, "Präsentation");
                return window;
            case ComposeScene:
                var account = editor.Document.Accounts.Accounts[0];
                var signature = editor.Document.Accounts.Signature(account.NewSignatureId);
                var compose = MailCommands.OpenComposer(window, new Draft
                {
                    AccountId = account.Id,
                    From = account.Sender,
                    To = [new MailAddress("Jonas Weber", "jonas.weber@beispiel.de")],
                    Subject = "Unterlagen für Donnerstag",
                    Html = "<p>Hallo Jonas,</p><p>anbei die <b>Unterlagen</b> für unsere Besprechung:</p><ul><li>Zeitplan</li><li>Budget</li></ul><div><br></div>" + (signature?.Html ?? ""),
                    Attachments = [new DraftAttachment("Zeitplan.pdf", "application/pdf", new byte[48_000])],
                });
                compose.Width = 1100;
                compose.Height = 760;
                return compose;
            case AccountScene:
                window.ShowBackstage(AccountsPage.PageId);
                if (window.Backstage.CurrentPage is AccountsPage page)
                {
                    page.ShowForm(null);
                    page.Form!.SenderBox.Text = "Anna Berger";
                    page.Form.AddressBox.Text = "anna.berger@gmx.de";
                    page.Form.PasswordBox.Text = "geheim";
                    page.Form.ShowFound(ThixMail.Core.Accounts.AutoConfig.FromProvider(ThixMail.Core.Accounts.AutoConfig.ProviderOf("anna.berger@gmx.de")!, "anna.berger@gmx.de"));
                }

                return window;
            case BrowserSignInScene or NoClientScene:
                // the browser sign-in as it looks once ThixMail has a client id (a sample one only for the picture) – and without one (the honest note)
                ThixMail.Core.Accounts.OAuthClients.ForTests = id => name == BrowserSignInScene ? new ThixMail.Core.Accounts.OAuthClient("snapshot", null) : null;
                window.ShowBackstage(AccountsPage.PageId);
                if (window.Backstage.CurrentPage is AccountsPage oauthPage)
                {
                    var address = name == BrowserSignInScene ? "anna.berger@gmail.com" : "anna.berger@outlook.de";
                    oauthPage.ShowForm(null);
                    oauthPage.Form!.SenderBox.Text = "Anna Berger";
                    oauthPage.Form.AddressBox.Text = address;
                    oauthPage.Form.ShowFound(ThixMail.Core.Accounts.AutoConfig.FromProvider(ThixMail.Core.Accounts.AutoConfig.ProviderOf(address)!, address));
                }

                return window;
            case OfflineScene:
                _ = editor.Document.SetWorkingOfflineAsync(true);
                Dispatcher.UIThread.RunJobs();
                editor.Document.Session.Send(new Draft { AccountId = editor.Document.Accounts.Accounts[0].Id, To = [new MailAddress("Lena Koch", "lena.koch@beispiel.de")], Subject = "Rückmeldung", Html = "<p>Danke!</p>" });
                editor.UpdateStatus(window.StatusBar);
                window.ShowMessage("ThixMail arbeitet offline. Gesendete Nachrichten warten im Postausgang, bis Sie wieder online arbeiten.", InfoSeverity.Warning, isClosable: true, "Online arbeiten", () => { });
                return window;
            case SignaturesScene:
                window.ShowSettings(MailSettings.SignaturesTab);
                return window;
            case CalendarSignInScene:
                editor.ShowView(MailArea.Calendar);
                Settle();
                return window;
            case CalendarScene or CalendarMonthScene or PeopleScene or InvitationScene or AppointmentScene:
                SignInForSample(editor);
                if (name == InvitationScene)
                {
                    SelectSubject(editor, "Einladung: Workshop");
                    return window;
                }

                editor.ShowView(name == PeopleScene ? MailArea.People : MailArea.Calendar);
                Settle();
                if (name == CalendarMonthScene)
                {
                    editor.CalendarView!.SetMode(ThixOffice.Pim.Desktop.Calendar.CalendarMode.Month);
                }
                else if (name is CalendarScene or AppointmentScene)
                {
                    editor.CalendarView!.SetMode(ThixOffice.Pim.Desktop.Calendar.CalendarMode.WorkWeek);
                    editor.CalendarView.Select(editor.CalendarView.Shown.FirstOrDefault(a => a.Title.StartsWith("Projektbesprechung", StringComparison.Ordinal)));
                }
                else
                {
                    editor.PeopleView!.Select(editor.Document.Pim.People!.People.FirstOrDefault(p => p.Name == "Lena Koch")?.Id);
                }

                Settle();
                if (name == AppointmentScene && editor.Document.Pim.Calendar is { } book && editor.CalendarView!.Selected is { } selected)
                {
                    var dialog = new ThixOffice.Pim.Desktop.Calendar.AppointmentDialog(book, selected, (value, _) => value, _ => [], asMeeting: true);
                    dialog.Attendees.Box.Text = "Jonas Weber <jonas.weber@beispiel.de>; Lena Koch <lena.koch@beispiel.de>";
                    dialog.Show(window);
                    Settle();
                    return dialog;
                }

                return window;
        }

        return window;
    }

    /// <summary>The sample's account of ThixCloud (the cloud in memory of the snapshots): the views Kalender and Personen with the sample's data.</summary>
    private static void SignInForSample(MailEditor editor)
    {
        if (!CloudService.IsConnected)
        {
            SettingsService.UpdateCloud(_ => new CloudSettings { ServerUrl = SampleCloud.ServerUrl, UserName = SampleCloud.UserName, DeviceName = "ANNA-PC" });
            Task.Run(() => CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC")).GetAwaiter().GetResult();
        }

        Task.Run(editor.Document.Pim.ReconnectAsync).GetAwaiter().GetResult();
        Task.Run(editor.Document.Pim.SyncNowAsync).GetAwaiter().GetResult(); // an own calendar is made only after the first sync
        SampleMailbox.FillPim(editor.Document);
        Settle();
    }

    private static void Settle()
    {
        for (var round = 0; round < 20; round++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
    }

    private static void SelectSubject(MailEditor editor, string subject)
    {
        editor.SelectFolder(null);
        Dispatcher.UIThread.RunJobs();
        var id = editor.Document.Store.Headers([.. editor.Rows.Where(r => r.IsMessage).Select(r => r.Id)]).FirstOrDefault(h => h.Subject.StartsWith(subject, StringComparison.Ordinal))?.Id;
        if (id is { } found)
        {
            editor.Select(found);
        }

        for (var round = 0; round < 20; round++)
        {
            Dispatcher.UIThread.RunJobs();
            Thread.Sleep(20);
        }
    }

    public override void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
        if (shown is ComposeWindow compose)
        {
            compose.CloseDiscarding();
        }

        var editor = (MailEditor)window.Editor;
        switch (name)
        {
            case OfflineScene:
                window.ShowMessage(null);
                foreach (var item in editor.Document.Store.Outbox())
                {
                    editor.Document.Store.RemoveFromOutbox(item.Id);
                }

                _ = editor.Document.SetWorkingOfflineAsync(false);
                break;
            case BrowserSignInScene or NoClientScene:
                ThixMail.Core.Accounts.OAuthClients.ForTests = null;
                window.Backstage.Close();
                break;
            case AccountScene:
            case SignaturesScene:
                window.Backstage.Close();
                break;
            case CalendarScene or CalendarMonthScene or PeopleScene or InvitationScene or AppointmentScene or CalendarSignInScene:
                if (shown is Window dialog && shown != window)
                {
                    dialog.Close();
                }

                editor.ShowView(MailArea.Mail);
                break;
        }

        Dispatcher.UIThread.RunJobs();
    }
}
