using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MatDesktop;
using ThixContacts.Core;
using ThixContacts.Editor;
using ThixContacts.Pages;
using ThixOffice.Pim.Desktop.People;
using ThixOffice.Core.Business;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Rules;

namespace ThixContacts;

/// <summary>
/// ThixContacts on ThixOffice.Desktop – a workspace app (<see cref="OfficeAppKind.Workspace"/>): the definition (<see cref="ThixContactsApp"/>), the one
/// "document" (<see cref="ContactsDocument"/>: the contacts of the store, synced while signed in), the editor (<see cref="ContactsEditor"/>), its commands,
/// ribbon, backstage pages (Importieren/Exportieren, Drucken, Als PDF speichern), self test and snapshot scenes. A .vcf handed over by the system is
/// imported. Start-up, single instance, setup, updates, the window, Einstellungen (Cloud too) and Info are OfficeApplication's.
/// </summary>
public partial class App : OfficeApplication
{
    public const string EditScene = "dialog-edit";
    public const string GroupsScene = "main-groups";
    public const string DuplicatesScene = "dialog-merge";
    public const string MappingScene = "dialog-import-mapping";
    public const string SignedOutScene = "main-signed-out";
    public const string PrintScene = "backstage-print";

    public override OfficeAppDefinition Definition => ThixContactsApp.Definition;

    public override IReadOnlyList<string> SnapshotScenes => [EditScene, GroupsScene, DuplicatesScene, MappingScene, SignedOutScene, PrintScene];

    public override MatRibbonDefinition CreateRibbon(OfficeWindow window) => PeopleRibbon.Create(window);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    public override OfficeDocument CreateDocument(string untitledName) => new ContactsDocument("Kontakte");

    public override OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document)
    {
        var editor = new ContactsEditor(window, (ContactsDocument)document);
        window.Closed += async (_, _) => await ((ContactsDocument)document).CloseAsync();
        return editor;
    }

    public override void AddCommands(OfficeWindow window) => ContactsCommands.Add(window);

    public override IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) =>
        [ContactsTransferPage.Item(window), ContactsPrintPage.Item(window), ContactsPdfPage.Item(window)];

    /// <summary>A .vcf (or .csv) handed over by the system: imported into the contacts, the result in the message bar.</summary>
    public override async Task ImportFilesAsync(OfficeWindow window, IReadOnlyList<string> files)
    {
        foreach (var file in files)
        {
            try
            {
                await ContactsCommands.ImportFileAsync(window, (ContactsEditor)window.Editor, file);
            }
            catch (BusinessException exception)
            {
                await QuestionDialog.ShowMessageAsync(window, "Importieren nicht möglich", exception.Message);
            }
        }
    }

    /// <summary>
    /// "Abmelden" leaves the account's contacts: asks whether a copy of them stays on this computer (the local contacts become that copy) or not (the
    /// contacts of this computer show as before the sign-in).
    /// </summary>
    public override async Task<bool> SignOutAsync(Window owner)
    {
        var choice = await QuestionDialog.AskAsync(owner, "Von der ThixCloud abmelden?",
            $"ThixContacts zeigt danach die Kontakte dieses Computers statt der Kontakte des Kontos {CloudService.Settings.AccountText}. " +
            "Möchten Sie eine Kopie der Kontakte des Kontos auf diesem Computer behalten?",
            new DialogChoice("Kopie behalten", "save", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Abbrechen", "x", IsCancel: true),
            new DialogChoice("Ohne Kopie abmelden", "logout", "MatButton.Danger", IsSeparated: true));
        if (choice is not (0 or 2) || DocumentWindows.FirstOrDefault()?.Document is not ContactsDocument document)
        {
            return false;
        }

        try
        {
            await document.SignOutAsync(keepLocalCopy: choice == 0);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or Microsoft.Data.Sqlite.SqliteException)
        {
            await QuestionDialog.ShowMessageAsync(owner, "Abmelden nicht möglich", $"Die Kopie der Kontakte konnte nicht angelegt werden: {exception.Message}");
            return false;
        }

        return true;
    }

    public override IEnumerable<(string Name, Func<Task<string>> Check)> SelfTestChecks() => [("ThixContacts: contacts, store, vCard, sync", () => ContactsSelfTest.RunAsync(this))];

    /// <summary>The sample contacts of the snapshots (in the store of the snapshot's temporary data folder).</summary>
    protected override void FillSampleDocument(OfficeDocument document)
    {
        var session = ((ContactsDocument)document).Session;
        if (session.Count == 0)
        {
            SampleContacts.Fill(session);
        }
    }

    public override void EndWorkspaceSample(OfficeWindow window)
    {
    }

    public override TopLevel ShowSnapshotScene(OfficeWindow window, string name)
    {
        var editor = (ContactsEditor)window.Editor;
        switch (name)
        {
            case EditScene:
                editor.Select(editor.Shown.First(p => p.Data.FamilyName == "Berger").Id);
                var edit = new ContactDialog(editor.Session, editor.Current);
                _ = edit.ShowAsync(window);
                return edit;
            case GroupsScene:
                var group = editor.Session.Groups.First(g => g.Name == "Projekt Nordlicht");
                editor.SetFilter(new ContactFilter(ContactFilterKind.Group, group.Id));
                editor.Select(editor.Shown.FirstOrDefault()?.Id);
                window.Ribbon.SelectTab("folder");
                return window;
            case DuplicatesScene:
                var set = editor.Session.Duplicates().First();
                var merge = new MergeDialog(set);
                _ = merge.ShowAsync(window);
                return merge;
            case MappingScene:
                var table = PersonCsv.Read("Vorname;Nachname;Firma;E-Mail-Adresse;Mobiltelefon;Geburtstag;Lieblingsfarbe\nKlaus;Wagner;Wagner & Söhne;klaus@wagner.example;0172 1112223;24.12.1965;Blau\nLena;Koch;;lena.koch@example.de;;;Grün\n");
                var mapping = new CsvMappingDialog(table);
                _ = mapping.ShowAsync(window);
                return mapping;
            case SignedOutScene:
                window.ShowMessage("Die ThixCloud hat die Anmeldung beendet. Ihre Kontakte bleiben auf diesem Computer; melden Sie sich erneut an, um sie wieder abzugleichen.",
                    InfoSeverity.Warning, isClosable: true, "Anmelden", () => { });
                window.StatusBar.SetItem(ContactsEditor.SyncItem, "Abgemeldet", StatusBarSide.Right);
                return window;
            case PrintScene:
                editor.SetFilter(ContactFilter.All);
                window.ShowBackstage(ContactsPrintPage.PageId);
                if (window.Backstage.CurrentPage is ContactsPrintPage print)
                {
                    print.ScopeChoice.SelectedIndex = 1;
                }

                return window;
        }

        return window;
    }

    public override void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
        if (shown is Window dialog && !ReferenceEquals(dialog, window))
        {
            dialog.Close();
        }

        var editor = (ContactsEditor)window.Editor;
        switch (name)
        {
            case GroupsScene:
                editor.SetFilter(ContactFilter.All);
                window.Ribbon.SelectTab("home");
                break;
            case SignedOutScene:
                window.ShowMessage(null);
                editor.UpdateStatus(window.StatusBar);
                break;
            case PrintScene:
                window.Backstage.Close();
                break;
        }

        Dispatcher.UIThread.RunJobs();
    }
}
