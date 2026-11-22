using System.Globalization;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Business;
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
/// The views Kalender and Personen of ThixMail's window (7c): the navigation bar switches between E-Mail, Kalender and Personen; the two others show
/// ThixOffice.Pim.Desktop's <see cref="CalendarView"/> and <see cref="PeopleView"/> on the account's data (<see cref="ThixMail.Core.MailPim"/>, through
/// ThixCloud's sync) – or, without a sign-in, why they need ThixCloud and the sign-in itself. The ribbon shows their contextual tabs (contexts
/// <see cref="CalendarContext"/> and <see cref="PeopleContext"/>) while they are active; the status bar speaks of them.
/// </summary>
public sealed partial class MailEditor
{
    public const string CalendarContext = "calendar";
    public const string PeopleContext = "people";
    public const string ViewCountItem = "mail.view.count";

    private static readonly CultureInfo GermanCulture = CultureInfo.GetCultureInfo("de-DE");
    private readonly Border _viewHost = new() { IsVisible = false };
    private PeopleView? _people;
    private CalendarView? _calendar;
    private bool _wasAvailable;

    /// <summary>The view shown: E-Mail, Kalender or Personen.</summary>
    public MailArea ActiveView { get; private set; } = MailArea.Mail;

    /// <summary>The calendar view (made when Kalender is shown the first time with ThixCloud), or null.</summary>
    public CalendarView? CalendarView => _calendar;

    /// <summary>The people view (made when Personen is shown the first time with ThixCloud), or null.</summary>
    public PeopleView? PeopleView => _people;

    /// <summary>The sign-in shown instead of a view while ThixMail has no ThixCloud account, or null.</summary>
    public CloudSignInPanel? SignInPanel { get; private set; }

    /// <summary>Shows E-Mail, Kalender or Personen.</summary>
    public void ShowView(MailArea view)
    {
        if (Gone)
        {
            return;
        }

        ActiveView = view;
        Document.ViewName = ViewNameOf(view); // the title bar names the view (the window refreshes on the document's change)
        for (var i = 0; i < NavigationButtons.Count; i++)
        {
            var button = NavigationButtons[i];
            if ((MailArea)i == view)
            {
                button.Use(Button.BackgroundProperty, "Mat.AccentSoft");
            }
            else
            {
                button.ClearValue(Button.BackgroundProperty);
            }
        }

        var mail = view == MailArea.Mail;
        _folderPane.IsVisible = mail && ShowsFolderPane;
        _mainArea.IsVisible = mail;
        _viewHost.IsVisible = !mail;
        var ribbon = _window.RibbonDefinition;
        ribbon.HideContext(CalendarContext);
        ribbon.HideContext(PeopleContext);
        if (!mail)
        {
            var available = Document.Pim.IsAvailable;
            _viewHost.Child = !available ? SignInFor(view) : view == MailArea.Calendar ? EnsureCalendar() : EnsurePeople();
            if (available)
            {
                var context = view == MailArea.Calendar ? CalendarContext : PeopleContext;
                ribbon.ShowContext(context);
                _window.Ribbon.SelectTab($"{context}.home");
                if (view == MailArea.Calendar)
                {
                    _calendar!.Refresh();
                }
                else
                {
                    _people!.Refresh();
                }
            }
        }
        else
        {
            _window.Ribbon.SelectTab("home");
        }

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

    private Button NavButton(string icon, string name, string shortcut, MailArea view, Thickness margin)
    {
        var button = new Button { Content = new MatIcon { Icon = icon, Size = 20 }, Padding = new Thickness(10), Margin = margin }.Themed("MatButton.Ghost");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, Ui.Tooltip(name, shortcut));
        button.Click += (_, _) => ShowView(view);
        if (view == MailArea.Mail)
        {
            button.Use(Button.BackgroundProperty, "Mat.AccentSoft");
        }

        return button;
    }

    private CalendarView EnsureCalendar()
    {
        if (_calendar is null)
        {
            _calendar = new CalendarView(() => Document.Pim.Calendar, "mail.calendar");
            _calendar.StateChanged += (_, _) => ScheduleViewState();
            _calendar.OpenRequested += async (_, appointment) => await CalendarCommands.OpenAsync(_window, this, appointment);
            _calendar.NewRequested += async (_, request) => await CalendarCommands.NewAsync(_window, this, request.Start, request.AllDay, meeting: false);
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

    /// <summary>Why the view needs ThixCloud, and the sign-in (the same panel as Einstellungen › Cloud).</summary>
    private Control SignInFor(MailArea view)
    {
        var calendar = view == MailArea.Calendar;
        SignInPanel = new CloudSignInPanel { IsCompact = true, MaxWidth = 460 };
        var title = new TextBlock { Text = calendar ? "Der Kalender kommt aus der ThixCloud" : "Die Personen kommen aus der ThixCloud", HorizontalAlignment = HorizontalAlignment.Center }.Themed("MatText.SectionTitle");
        var text = new TextBlock
        {
            Text = calendar
                ? "ThixMail zeigt den Kalender Ihres ThixCloud-Kontos – dieselben Termine wie ThixCalendar auf allen Ihren Geräten. Melden Sie sich an, um ihn zu sehen und Einladungen zu beantworten. E-Mail funktioniert auch ohne."
                : "ThixMail zeigt die Kontakte Ihres ThixCloud-Kontos – dieselben wie ThixContacts auf allen Ihren Geräten. Melden Sie sich an, um sie zu sehen und zu bearbeiten. E-Mail funktioniert auch ohne.",
            TextWrapping = TextWrapping.Wrap,
            TextAlignment = TextAlignment.Center,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Center,
        };
        text.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var panel = new StackPanel
        {
            Spacing = 12,
            Margin = new Thickness(24),
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Children = { new MatIcon { Icon = calendar ? "calendar" : "users", Size = 40, HorizontalAlignment = HorizontalAlignment.Center }, title, text, SignInPanel },
        };
        var host = new ScrollViewer { Content = panel };
        host.Use(ScrollViewer.BackgroundProperty, "Mat.Bg");
        return host;
    }

    private void FocusView()
    {
        if (ActiveView == MailArea.Calendar && _calendar is { IsVisible: true })
        {
            _calendar.Focus();
        }
        else if (ActiveView == MailArea.People && _people is not null && Document.Pim.IsAvailable)
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
            statusBar.SetItem(CountItem, "Nicht bei der ThixCloud angemeldet");
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
            var text = count == 1 ? "1 Termin" : $"{count.ToString("N0", GermanCulture)} Termine";
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
        SyncState.Off => CloudService.Status.State == CloudState.SignedOut ? "Abgemeldet – Änderungen warten" : "Nicht verbunden",
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
        _wasAvailable = Document.Pim.IsAvailable;
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

    /// <summary>The calendar or the people changed (here, by the sync, or another account came in).</summary>
    private void OnPimChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        if (Gone)
        {
            return;
        }

        var available = Document.Pim.IsAvailable;
        if (available != _wasAvailable)
        {
            _wasAvailable = available;
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

    /// <summary>Signed in or out: the account's calendar and people come in (or go).</summary>
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
        }
        catch (Exception exception) when (exception is ThixOffice.Core.Models.BusinessException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }

        if (!Gone && Document.Pim.IsAvailable != _wasAvailable)
        {
            _wasAvailable = Document.Pim.IsAvailable;
            if (ActiveView != MailArea.Mail)
            {
                ShowView(ActiveView);
            }
        }
    });

    // ===== invitations in the reading pane =====

    /// <summary>The bar of an invitation for the message shown (its state in the calendar of the ThixCloud account).</summary>
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
