using Avalonia.Controls;
using ThixContacts.Core;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop;
using ThixOffice.Pim.Desktop.People;
using ThixOffice.Pim.Model;
using ThixOffice.Pim.Rules;
using ThixOffice.Pim.Sync;

namespace ThixContacts.Editor;

/// <summary>
/// ThixContacts' part of the window: the shared view of Outlook's "Personen" (<see cref="PeopleView"/> of ThixOffice.Pim.Desktop – ThixMail shows the
/// same) on the document's <see cref="ContactsSession"/>, plus what belongs to the app: the status bar (how many contacts, how the sync stands), the
/// store of another account after a sign-in, and the message while the cloud has ended the sign-in.
/// </summary>
public sealed class ContactsEditor : OfficeEditor
{
    public const string CountItem = "contacts.count";
    public const string SyncItem = "contacts.sync";

    /// <summary>The words of the message bar while the cloud has ended the sign-in (the account's contacts stay, changes wait).</summary>
    public const string SignedOutMessage = "Bei ThixCloud abgemeldet – Änderungen werden nach der Anmeldung synchronisiert";

    private readonly OfficeWindow _window;
    private bool _showsSignedOut;

    public ContactsEditor(OfficeWindow window, ContactsDocument document)
    {
        _window = window;
        Document = document;
        _window.StatusBar.View.ShowZoom = false;
        People = new PeopleView(window, () => Document.Session);
        People.StateChanged += (_, _) => UpdateStatus(_window.StatusBar);
        Document.SessionChanged += OnSessionChanged;
        Document.Changed += OnDocumentChanged;
        Document.SyncStatusChanged += OnSyncStatusChanged;
        CloudService.StatusChanged += OnCloudStatusChanged;
        Ui.Post(ShowSignedOutMessage);
    }

    public ContactsDocument Document { get; }

    public ContactsSession Session => Document.Session;

    /// <summary>The shared view (folders, list, card).</summary>
    public PeopleView People { get; }

    public override Control View => People;

    public ListBox Folders => People.Folders;

    public TextBox SearchBox => People.SearchBox;

    public ListBox List => People.List;

    public PersonCard Card => People.Card;

    public ContactFilter Filter => People.Filter;

    public PersonSort Sort => People.Sort;

    public bool ShowsCard => People.ShowsCard;

    public bool ShowsIndex => People.ShowsIndex;

    public bool ShowsFolders => People.ShowsFolders;

    public IReadOnlyList<Person> Shown => People.Shown;

    public IReadOnlyList<Person> Selected => People.Selected;

    public Person? Current => People.Current;

    public PersonGroup? CurrentGroup => People.CurrentGroup;

    public IReadOnlyList<string> ActiveLetters => People.ActiveLetters;

    public void SetFilter(ContactFilter filter) => People.SetFilter(filter);

    public void Select(string? id) => People.Select(id);

    public void SelectLetter(string letter) => People.SelectLetter(letter);

    public void Refresh() => People.Refresh();

    public Task RunAsync(string id) => People.RunAsync(id);

    public override void FocusDocument() => People.FocusList();

    public override void UpdateStatus(OfficeStatusBar statusBar)
    {
        statusBar.SetItem(CountItem, People.CountText());
        statusBar.SetItem(SyncItem, CloudService.Status.State == CloudState.SignedOut ? "Abgemeldet" : SyncText(Document.SyncStatus), StatusBarSide.Right);
    }

    /// <summary>The words of the status bar for the sync.</summary>
    public static string SyncText(SyncStatus status) => status.State switch
    {
        SyncState.Off => "Nur auf diesem Computer",
        SyncState.Syncing => "Wird synchronisiert …",
        SyncState.Idle when status.Pending > 0 => $"{status.Pending} Änderungen warten",
        SyncState.Idle => "Mit der ThixCloud synchronisiert",
        SyncState.Offline => status.Pending > 0 ? $"Offline – {status.Pending} Änderungen warten" : "Offline",
        SyncState.SignedOut => "Abgemeldet",
        SyncState.Forbidden => "In der ThixCloud abgeschaltet",
        _ => status.Message,
    };

    private void OnDocumentChanged(object? sender, EventArgs e) => Ui.Post(People.QueueRefresh);

    private void OnSessionChanged(object? sender, EventArgs e) => Ui.Post(() =>
    {
        People.SetFilter(ContactFilter.All);
    });

    private void OnSyncStatusChanged(object? sender, SyncStatus status) => Ui.Post(() =>
    {
        UpdateStatus(_window.StatusBar);
        if (status.State == SyncState.SignedOut)
        {
            ShowSignedOutMessage(force: true);
        }
    });

    /// <summary>
    /// The cloud ended the sign-in (now or before a restart): the account's contacts stay in the window and can be changed – the changes wait – and the
    /// message bar offers "Anmelden" (Einstellungen › Cloud).
    /// </summary>
    private void ShowSignedOutMessage() => ShowSignedOutMessage(force: false);

    private void ShowSignedOutMessage(bool force)
    {
        if (!force && CloudService.Status.State != CloudState.SignedOut)
        {
            return;
        }

        _showsSignedOut = true;
        _window.ShowMessage(SignedOutMessage, InfoSeverity.Warning, isClosable: true, "Anmelden",
            () => _window.ShowSettings(ThixOffice.Desktop.Pages.SettingsPage.CloudTab));
        UpdateStatus(_window.StatusBar);
    }

    /// <summary>Signed in or out: the store of the account (or of this computer) comes in.</summary>
    private void OnCloudStatusChanged(object? sender, CloudStatus status) => Ui.Post(() =>
    {
        if (status.State == CloudState.SignedOut)
        {
            // the cloud ended the sign-in: the account's contacts stay in the window (offline) until the person signs in again or signs out
            _ = Document.StopSyncAsync();
            ShowSignedOutMessage(force: true);
            return;
        }

        if (_showsSignedOut && status.State != CloudState.Connecting)
        {
            _showsSignedOut = false;
            _window.ShowMessage(null);
        }

        _ = ReconnectAsync();
    });

    private async Task ReconnectAsync()
    {
        try
        {
            await Document.ReconnectAsync();
        }
        catch (Exception exception) when (exception is ThixOffice.Core.Models.BusinessException or IOException or Microsoft.Data.Sqlite.SqliteException)
        {
            _window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }

        UpdateStatus(_window.StatusBar);
    }

    public override void Detach()
    {
        Document.SessionChanged -= OnSessionChanged;
        Document.Changed -= OnDocumentChanged;
        Document.SyncStatusChanged -= OnSyncStatusChanged;
        CloudService.StatusChanged -= OnCloudStatusChanged;
    }
}
