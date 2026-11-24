using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop.Calendar;
using ThixOffice.Pim.Desktop.People;
using ThixOffice.Pim.Sync;

namespace ThixMail.Editor;

/// <summary>The views of ThixMail's window, as Outlook's navigation bar switches them.</summary>
public enum MailArea
{
    Mail,
    Calendar,
    People,
}

/// <summary>
/// The modules of ThixMail's window like Outlook's: the navigation bar switches between E-Mail, Kalender and Personen; the two others show
/// ThixOffice.Pim.Desktop's <see cref="CalendarView"/> and <see cref="PeopleView"/> on the data of this computer or of the ThixCloud account
/// (<see cref="ThixMail.Core.MailPim"/>) – they always work; without ThixCloud a slim bar offers the sign-in (it can be closed for good). The ribbon shows
/// the tabs of the active module (<see cref="MailRibbon.ModuleOf"/>); the status bar speaks of it.
/// </summary>
public sealed partial class MailEditor
{
    public const string ViewCountItem = "mail.view.count";

    /// <summary>The app value that remembers that the person closed the offer to sign in to ThixCloud.</summary>
    public const string CloudOfferDismissed = "mail.cloudOffer.dismissed";

    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly Border _viewHost = new() { IsVisible = false };
    private readonly DockPanel _viewDock = new();
    private readonly Border _viewContent = new();
    private PeopleView? _people;
    private CalendarView? _calendar;
    private Border? _cloudOffer;
    private bool _wasAccountData;

    /// <summary>The view shown: E-Mail, Kalender or Personen.</summary>
    public MailArea ActiveView { get; private set; } = MailArea.Mail;

    /// <summary>The reminders of the calendar while ThixMail runs (set by the app).</summary>
    public MailReminders? Reminders { get; internal set; }

    /// <summary>The calendar view (made when Kalender is shown the first time), or null.</summary>
    public CalendarView? CalendarView => _calendar;

    /// <summary>The people view (made when Personen is shown the first time), or null.</summary>
    public PeopleView? PeopleView => _people;

    /// <summary>The bar that offers ThixCloud above Kalender and Personen (visible while ThixMail has no account and the person did not close it).</summary>
    public Border CloudOffer => _cloudOffer ??= BuildCloudOffer();

    /// <summary>"Anmelden" of the offer (tests).</summary>
    public Button? CloudOfferSignIn { get; private set; }

    /// <summary>"Nicht mehr anzeigen" of the offer (tests).</summary>
    public Button? CloudOfferClose { get; private set; }

    /// <summary>True when the offer to sign in belongs above the view: the app has the cloud, no account, no policy against it, not closed by the person.</summary>
    public bool OffersCloud => AppInfo.Definition.HasCloud && !AccessPolicy.IsCloudDisabled && !CloudService.HasAccount
                               && SettingsService.AppValue(CloudOfferDismissed) != "true";

    /// <summary>The window bound the ribbon: the tabs of the module shown.</summary>
    public override void RibbonBound(OfficeWindow window) =>
        window.RibbonBinding.ShowTabs(tab => MailRibbon.ModuleOf(tab.Id) == ActiveView, MailRibbon.HomeTabOf(ActiveView));

    /// <summary>Shows E-Mail, Kalender or Personen – with its ribbon tabs, like Outlook.</summary>
    public void ShowView(MailArea view)
    {
        if (Gone)
        {
            return;
        }

        var changed = ActiveView != view;
        ActiveView = view;
        Document.ViewName = ViewNameOf(view); // the title bar names the view (the window refreshes on the document's change)
        for (var i = 0; i < NavigationButtons.Count; i++)
        {
            MarkNavButton(NavigationButtons[i], (MailArea)i == view);
        }

        var mail = view == MailArea.Mail;
        _folderPane.IsVisible = mail && ShowsFolderPane;
        _mainArea.IsVisible = mail;
        _viewHost.IsVisible = !mail;
        if (!mail)
        {
            _viewContent.Child = !Document.Pim.IsAvailable ? Unavailable() : view == MailArea.Calendar ? EnsureCalendar() : EnsurePeople();
            if (view == MailArea.Calendar)
            {
                _calendar?.Refresh();
            }
            else
            {
                _people?.Refresh();
            }
        }

        UpdateCloudOffer();
        ShowSearch();

        // Outlook swaps the ribbon with the module; its Start tab comes up (staying in the same module keeps the chosen tab)
        _window.RibbonBinding.ShowTabs(tab => MailRibbon.ModuleOf(tab.Id) == view, changed ? MailRibbon.HomeTabOf(view) : null);
        _window.Commands.Invalidate();
        UpdateStatus(_window.StatusBar);
        Ui.Post(FocusDocument);
    }

    /// <summary>The name of a view as the navigation bar and the title bar say it.</summary>
    public static string ViewNameOf(MailArea view) => view switch
    {
        MailArea.Calendar => "Kalender",
        MailArea.People => "Personen",
        _ => "E-Mail",
    };

    /// <summary>A module of the navigation bar: its icon with its name under it (Outlook's modules at a glance), the key in the tooltip.</summary>
    private Button NavButton(string icon, string name, string shortcut, MailArea view, Thickness margin)
    {
        var label = new TextBlock { Text = name, FontSize = MatTokens.Type.FsXs, HorizontalAlignment = HorizontalAlignment.Center, TextTrimming = TextTrimming.CharacterEllipsis };
        var content = new StackPanel { Spacing = 3, HorizontalAlignment = HorizontalAlignment.Center, Children = { new MatIcon { Icon = icon, Size = 20, HorizontalAlignment = HorizontalAlignment.Center }, label } };
        var button = new Button
        {
            Content = content,
            Padding = new Thickness(2, 8, 2, 6),
            Margin = margin,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            HorizontalContentAlignment = HorizontalAlignment.Center,
        }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, Ui.Tooltip(name, shortcut));
        button.Click += (_, _) => ShowView(view);
        MarkNavButton(button, view == MailArea.Mail);
        return button;
    }

    /// <summary>The active module: the accent's soft tint behind it, its icon and name in the accent.</summary>
    private static void MarkNavButton(Button button, bool active)
    {
        var content = (StackPanel)button.Content!;
        var icon = (MatIcon)content.Children[0];
        var label = (TextBlock)content.Children[1];
        if (active)
        {
            button.Use(Button.BackgroundProperty, "Mat.AccentSoft");
            icon.Use(MatIcon.ForegroundProperty, "Mat.Accent");
            label.Use(TextBlock.ForegroundProperty, "Mat.Accent");
            label.FontWeight = FontWeight.SemiBold;
        }
        else
        {
            button.ClearValue(Button.BackgroundProperty);
            icon.Use(MatIcon.ForegroundProperty, "Mat.TextMuted");
            label.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            label.FontWeight = FontWeight.Normal;
        }
    }

    private CalendarView EnsureCalendar()
    {
        if (_calendar is null)
        {
            _calendar = new CalendarView(() => Document.Pim.Calendar, "mail.calendar");
            _calendar.StateChanged += (_, _) => ScheduleViewState();
            _calendar.OpenRequested += async (_, appointment) => await CalendarCommands.OpenAsync(_window, this, appointment);
            _calendar.NewRequested += async (_, request) => await CalendarCommands.NewAsync(_window, this, request.Start, request.AllDay, meeting: false);
            _calendar.ChangeRequested += (_, change) =>
            {
                try
                {
                    var saved = Document.ChangeAppointment(change.Appointment, change.Label);
                    _calendar.Refresh();
                    _calendar.Select(Document.Pim.Calendar?.Find(saved.Key));
                }
                catch (ThixOffice.Core.Models.BusinessException exception)
                {
                    _window.ShowMessage(exception.Message, InfoSeverity.Warning, isClosable: true);
                }
            };
        }

        return _calendar;
    }

    private PeopleView EnsurePeople()
    {
        if (_people is null)
        {
            _people = new PeopleView(_window, () => Document.Pim.People!, "mail.people");
            _people.StateChanged += (_, _) => UpdateStatus(_window.StatusBar);
        }

        return _people;
    }

    /// <summary>
    /// The offer above Kalender and Personen while ThixMail has no ThixCloud account: one slim line – what it brings, "Anmelden" (Einstellungen › Cloud)
    /// and a close button that hides it for good (an app value; Einstellungen › Cloud stays).
    /// </summary>
    private Border BuildCloudOffer()
    {
        var text = new TextBlock
        {
            Text = "Mit ThixCloud auf allen Geräten synchronisieren – Kalender und Kontakte sind jetzt nur auf diesem Computer.",
            TextWrapping = TextWrapping.Wrap,
            VerticalAlignment = VerticalAlignment.Center,
        };
        CloudOfferSignIn = new Button { Content = new IconLabel { Icon = "login", Text = "Anmelden" }, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(12, 0, 4, 0) };
        CloudOfferSignIn.Click += (_, _) => _window.ShowSettings(ThixOffice.Desktop.Pages.SettingsPage.CloudTab);
        CloudOfferClose = new Button { Content = new MatIcon { Icon = "x", Size = 14 }, Padding = new Thickness(6), VerticalAlignment = VerticalAlignment.Center }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(CloudOfferClose, "Nicht mehr anzeigen");
        ToolTip.SetTip(CloudOfferClose, "Nicht mehr anzeigen (Anmelden bleibt unter Datei › Einstellungen › Cloud)");
        CloudOfferClose.Click += (_, _) =>
        {
            SettingsService.RememberAppValue(CloudOfferDismissed, "true");
            UpdateCloudOffer();
            FocusView();
        };
        var icon = new MatIcon { Icon = "cloud", Size = 18, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(0, 0, 10, 0) };
        icon.Use(MatIcon.ForegroundProperty, "Mat.Accent");
        var row = new DockPanel();
        DockPanel.SetDock(icon, Dock.Left);
        DockPanel.SetDock(CloudOfferClose, Dock.Right);
        DockPanel.SetDock(CloudOfferSignIn, Dock.Right);
        row.Children.Add(icon);
        row.Children.Add(CloudOfferClose);
        row.Children.Add(CloudOfferSignIn);
        row.Children.Add(text);
        var bar = new Border { Child = row, Padding = new Thickness(14, 6, 8, 6), BorderThickness = new Thickness(0, 0, 0, 1), IsVisible = false };
        bar.Use(Border.BackgroundProperty, "Mat.AccentSoft");
        bar.Use(Border.BorderBrushProperty, "Mat.Border");
        AutomationProperties.SetName(bar, "Mit ThixCloud synchronisieren");
        return bar;
    }

    private void UpdateCloudOffer() => CloudOffer.IsVisible = OffersCloud && ActiveView != MailArea.Mail;

    /// <summary>The disk refused the stores of the calendar and the people (never the case normally): say so instead of an empty view.</summary>
    private static Control Unavailable()
    {
        var text = new TextBlock
        {
            Text = "Kalender und Kontakte konnten auf diesem Computer nicht geöffnet werden. Bitte starten Sie ThixMail neu.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 420,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
        };
        text.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        return text;
    }

    private void FocusView()
    {
        if (ActiveView == MailArea.Calendar && _calendar is { IsVisible: true })
        {
            _calendar.Focus();
        }
        else if (ActiveView == MailArea.People && _people is not null)
        {
            _people.FocusList();
        }
    }

    private bool _viewStateQueued;

    private void ScheduleViewState()
    {
        if (_viewStateQueued)
        {
            return;
        }

        _viewStateQueued = true;
        Ui.Post(() =>
        {
            _viewStateQueued = false;
            if (!Gone)
            {
                _window.Commands.Invalidate();
                UpdateStatus(_window.StatusBar);
            }
        });
    }

    private void UpdateViewStatus(OfficeStatusBar statusBar)
    {
        if (!Document.Pim.IsAvailable)
        {
            statusBar.SetItem(CountItem, "");
            statusBar.SetItem(SyncItem, "", StatusBarSide.Right);
            return;
        }

        if (ActiveView == MailArea.People && _people is not null)
        {
            statusBar.SetItem(CountItem, _people.CountText());
            statusBar.SetItem(SyncItem, PimSyncText(Document.Pim.PeopleSyncStatus), StatusBarSide.Right);
            return;
        }

        if (_calendar is not null)
        {
            var count = _calendar.Shown.Count;
            var text = count == 0 ? "Keine Termine" : count == 1 ? "1 Termin" : $"{count.ToString("N0", GermanCulture)} Termine";
            if (_calendar.Selected is { } selected)
            {
                text += $" · {selected.Title}";
            }

            statusBar.SetItem(CountItem, text);
            statusBar.SetItem(SyncItem, PimSyncText(Document.Pim.CalendarSyncStatus), StatusBarSide.Right);
        }
    }

    /// <summary>The words of the status bar for the sync of the calendar or the people.</summary>
    public static string PimSyncText(SyncStatus status) => status.State switch
    {
        SyncState.Off => CloudService.Status.State == CloudState.SignedOut ? "Abgemeldet – Änderungen warten" : "Nur auf diesem Computer",
        SyncState.Syncing => "Wird synchronisiert …",
        SyncState.Idle when status.Pending > 0 => $"{status.Pending} Änderungen warten",
        SyncState.Idle => "Mit der ThixCloud synchronisiert",
        SyncState.Offline => status.Pending > 0 ? $"Offline – {status.Pending} Änderungen warten" : "Offline",
        SyncState.SignedOut => "Abgemeldet",
        SyncState.Forbidden => status.Message.Length > 0 ? status.Message : "In der ThixCloud abgeschaltet",
        _ => status.Message,
    };

    private void AttachViews()
    {
        DockPanel.SetDock(CloudOffer, Dock.Top);
        _viewDock.Children.Add(CloudOffer);
        _viewDock.Children.Add(_viewContent);
        _viewHost.Child = _viewDock;
        _wasAccountData = Document.Pim.IsAccountData;
        Document.Pim.Changed += OnPimChanged;
        Document.Pim.SyncStatusChanged += OnPimSyncStatusChanged;
        CloudService.StatusChanged += OnCloudChanged;
    }

    private void DetachViews()
    {
        Document.Pim.Changed -= OnPimChanged;
        Document.Pim.SyncStatusChanged -= OnPimSyncStatusChanged;
        CloudService.StatusChanged -= OnCloudChanged;
    }

    /// <summary>The calendar or the people changed (here, by the sync, by ThixContacts, or another account came in).</summary>
    private void OnPimChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        if (Gone)
        {
            return;
        }

        if (Document.Pim.IsAccountData != _wasAccountData)
        {
            _wasAccountData = Document.Pim.IsAccountData;
            if (ActiveView != MailArea.Mail)
            {
                ShowView(ActiveView);
            }

            return;
        }

        if (ActiveView == MailArea.Calendar)
        {
            _calendar?.Refresh();
        }
        else if (ActiveView == MailArea.People)
        {
            _people?.QueueRefresh();
        }
    });

    /// <summary>A sync's state changed: the status bar of Kalender / Personen says so (once per round of the dispatcher).</summary>
    private void OnPimSyncStatusChanged(object? sender, EventArgs e)
    {
        if (ActiveView != MailArea.Mail)
        {
            ScheduleViewState();
        }
    }

    /// <summary>Signed in or out: the account's calendar and people come in (or this computer's again).</summary>
    private void OnCloudChanged(object? sender, CloudStatus status) => Ui.Post(async () =>
    {
        if (Gone || status.State == CloudState.Connecting)
        {
            return;
        }

        try
        {
            if (status.State == CloudState.SignedOut)
            {
                await Document.Pim.StopSyncsAsync(); // the data stays, changes wait for the next sign-in
            }
            else
            {
                await Document.Pim.ReconnectAsync();
            }

            await Document.ReconnectSettingsAsync();
        }
        catch (Exception exception) when (exception is ThixOffice.Core.Models.BusinessException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }

        if (!Gone)
        {
            _wasAccountData = Document.Pim.IsAccountData;
            if (ActiveView != MailArea.Mail)
            {
                ShowView(ActiveView);
            }
            else
            {
                UpdateCloudOffer();
            }
        }
    });

    // ===== invitations in the reading pane =====

    /// <summary>The bar of an invitation for the message shown (its state in the calendar).</summary>
    private void ShowInvitationOf(ThixMail.Core.Mime.ReadMail mail, ThixMail.Core.Model.MessageHeader header) =>
        Reader.ShowInvitation(ThixMail.Core.MailInvitations.View(Document, mail, header.AccountId));

    /// <summary>Zusagen / Mit Vorbehalt / Absagen: the calendar follows, the answer goes to the organizer through the outbox.</summary>
    private void AnswerInvitation(ThixOffice.Pim.Calendar.InvitationAnswer answer)
    {
        if (Reader.Invitation is not { } invitation || Reader.Mail is not { } mail || Reader.Header is not { } header)
        {
            return;
        }

        try
        {
            ThixMail.Core.MailInvitations.Answer(Document, invitation, answer);
            var words = answer switch
            {
                ThixOffice.Pim.Calendar.InvitationAnswer.Accept => "Zugesagt",
                ThixOffice.Pim.Calendar.InvitationAnswer.Tentative => "Mit Vorbehalt angenommen",
                _ => "Abgesagt",
            };
            var where = answer == ThixOffice.Pim.Calendar.InvitationAnswer.Decline || !Document.Pim.IsAvailable ? "" : " – der Termin steht in Ihrem Kalender";
            _window.ShowMessage($"{words}{where}. Die Antwort an {invitation.Invitation.Organizer.Email} liegt im Postausgang.", InfoSeverity.Success, isClosable: true);
            ShowInvitationOf(mail, header);
            _calendar?.Refresh();
        }
        catch (ThixOffice.Core.Models.BusinessException exception)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }
    }

    private void RemoveCancelledInvitation()
    {
        if (Reader.Invitation is { } invitation && Reader.Mail is { } mail && Reader.Header is { } header && ThixMail.Core.MailInvitations.RemoveCancelled(Document, invitation))
        {
            _window.ShowMessage($"„{invitation.Invitation.Summary}“ wurde aus Ihrem Kalender entfernt.", InfoSeverity.Success, isClosable: true);
            ShowInvitationOf(mail, header);
            _calendar?.Refresh();
        }
    }
}
