using System.Text.RegularExpressions;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MatCloud.Contract;
using MatCloud.Contract.Contacts;
using ThixContacts.Core;
using ThixContacts.Dialogs;
using ThixContacts.Editor;
using ThixContacts.Pages;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Platform;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Sync;
using static ThixContacts.Tests.TestData;

namespace ThixContacts.Tests;

/// <summary>The real ThixContacts headless, through keys, text and the mouse.</summary>
public sealed class ContactsUiTests
{
    [AvaloniaFact]
    public async Task The_window_is_a_workspace_without_document_flows()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        Assert.Same(window, TestData.App.NewDocumentWindow()); // one main window
        Assert.Single(TestData.App.DocumentWindows);
        Assert.Null(window.Commands.Find(CommonCommandIds.Save));
        Assert.Null(window.Commands.Find(CommonCommandIds.Open));
        Assert.Null(window.Commands.Find(CommonCommandIds.New));
        Assert.Equal(2, window.QuickAccessButtons.Count); // undo, redo
        Assert.Equal([ContactsTransferPage.PageId, ContactsPrintPage.PageId, OfficeExportPage.PageId], window.Backstage.PageIds);

        window.ShowSettings(SettingsPage.SaveTab);
        await SettleAsync();
        Assert.Equal(SettingsPage.GeneralTab, ((SettingsPage)window.Backstage.CurrentPage!).CurrentTab); // no tab Speichern
        window.Backstage.Close();

        Assert.Contains(window.Commands.Search("kontakt"), command => command.Id == ContactsCommandIds.New);
        Assert.Contains(window.Commands.Search("vcard"), command => command.Id == ContactsCommandIds.Import);
        Assert.Equal("12 Kontakte", window.StatusBar.TextOf(ContactsEditor.CountItem));
        Assert.Equal("Nur auf diesem Computer", window.StatusBar.TextOf(ContactsEditor.SyncItem));
    }

    [AvaloniaFact]
    public async Task Ctrl_N_makes_a_contact_through_the_dialog_and_the_list_and_card_show_it()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);

        await PressAsync(window, PhysicalKey.N, CommandKey);
        var dialog = await DialogAsync<ContactDialog>(window);
        dialog.GivenName.Focus();
        await TypeAsync(dialog, "Zora");
        dialog.FamilyName.Focus();
        await TypeAsync(dialog, "Quast");
        dialog.Emails.Rows[0].Box.Focus();
        await TypeAsync(dialog, "zora@quast.example");
        dialog.Phones.Rows[0].Box.Focus();
        await TypeAsync(dialog, "0171 999");
        dialog.Phones.Rows[0].ChooseLabel(ContactLabels.Work);
        await ClickAsync(dialog, dialog.OkButton);
        await SettleAsync();

        Assert.False(dialog.IsVisible);
        Assert.Equal("Zora Quast", editor.Current?.Name);
        Assert.Equal("Zora Quast", editor.Card.NameText);
        Assert.Contains("Telefon", editor.Card.SectionTitles);
        Assert.Equal(ContactLabels.Work, editor.Current!.Data.Phones.Single().Label);
        Assert.Equal("13 Kontakte", window.StatusBar.TextOf(ContactsEditor.CountItem));
    }

    [AvaloniaFact]
    public async Task A_wrong_value_stays_in_the_dialog_at_its_row_and_F2_edits_the_chosen_contact()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);
        editor.Select(editor.Shown.First(p => p.Data.GivenName == "Bert").Id);
        await SettleAsync();

        await PressAsync(window, PhysicalKey.F2);
        var dialog = await DialogAsync<ContactDialog>(window);
        Assert.Equal("Bert", dialog.GivenName.Text);
        var row = dialog.Emails.Add(new ContactValue(ContactLabels.Home, "kaputt"));
        await SettleAsync();
        await ClickAsync(dialog, dialog.OkButton);
        Assert.True(dialog.IsVisible);
        Assert.Contains("keine E-Mail-Adresse", dialog.ErrorText);

        row.Box.Text = "bert@privat.example";
        dialog.JobTitle.Text = "Drucker";
        await ClickAsync(dialog, dialog.OkButton);
        Assert.False(dialog.IsVisible);
        Assert.Equal(("Drucker", 2), (editor.Current!.Data.JobTitle, editor.Current.Data.Emails.Count));

        // undo through the quick access
        await ClickAsync(window, window.QuickAccessButtons[0]);
        Assert.Equal("Inhaber", editor.Session.Find(editor.Current!.Id)!.Data.JobTitle);
    }

    [AvaloniaFact]
    public async Task Search_letters_and_the_folder_pane_narrow_the_list()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);

        await PressAsync(window, PhysicalKey.E, CommandKey);
        Assert.True(editor.SearchBox.IsFocused);
        await TypeAsync(window, "nordlicht");
        await SettleAsync();
        Assert.Equal(5, editor.Shown.Count);
        Assert.Equal("5 von 12 Kontakte", window.StatusBar.TextOf(ContactsEditor.CountItem));
        await PressAsync(window, PhysicalKey.Escape);
        Assert.Equal(12, editor.Shown.Count);

        await PressAsync(window, PhysicalKey.F, CommandKey);
        Assert.True(editor.SearchBox.IsFocused);
        await TypeAsync(window, "4455667");
        Assert.Equal("Frank", editor.Shown.Single().Data.GivenName);
        editor.SearchBox.Text = "";
        await SettleAsync();

        Assert.Contains("S", editor.ActiveLetters); // Stadtwerke Lindenau: no given name, the company
        Assert.DoesNotContain("Z", editor.ActiveLetters);
        editor.SelectLetter("J");
        Assert.Equal("Julia", editor.Current?.Data.GivenName);

        editor.Folders.SelectedItem = editor.Folders.Items.OfType<FolderEntry>().Single(f => f.Label == "Favoriten");
        await SettleAsync();
        Assert.Equal(3, editor.Shown.Count);
        editor.Folders.SelectedItem = editor.Folders.Items.OfType<FolderEntry>().Single(f => f.Label == "Kunde");
        await SettleAsync();
        Assert.Equal(["Clara", "Hanna", ""], editor.Shown.Select(p => p.Data.GivenName));

        await window.Commands.ExecuteAsync(ContactsCommandIds.SortCompany);
        await SettleAsync();
        Assert.True(window.Commands[ContactsCommandIds.SortCompany].Checked);
        Assert.Equal("Bäckerei Schulz", editor.Shown[0].Data.Company);
    }

    [AvaloniaFact]
    public async Task Delete_removes_the_selection_and_the_message_bar_brings_it_back()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);
        editor.Select(editor.Shown.First(p => p.Data.GivenName == "David").Id);
        editor.List.SelectedItems!.Add(editor.Shown.First(p => p.Data.GivenName == "Elif"));
        await SettleAsync();
        Assert.Equal(2, editor.Selected.Count);
        editor.List.Focus();

        await PressAsync(window, PhysicalKey.Delete);
        Assert.Equal(10, editor.Shown.Count);
        Assert.True(window.MessageBar.IsVisible);
        Assert.Equal("2 Kontakte wurden gelöscht.", window.MessageBar.Text);

        await ClickAsync(window, window.MessageBar.ActionButton);
        Assert.Equal(12, editor.Shown.Count);
        Assert.False(window.MessageBar.IsVisible);
    }

    [AvaloniaFact]
    public async Task Groups_are_made_from_the_selection_renamed_and_filled()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);
        editor.Select(editor.Shown.First(p => p.Data.GivenName == "Bert").Id);
        await SettleAsync();

        _ = window.Commands.ExecuteAsync(ContactsCommandIds.NewGroup);
        var ask = await DialogAsync<ThixOffice.Desktop.Dialogs.TextInputDialog>(window);
        await TypeAsync(ask, "Lieferanten");
        await PressAsync(ask, PhysicalKey.Enter);
        await SettleAsync();
        Assert.Equal("Lieferanten", editor.CurrentGroup?.Name);
        Assert.Equal(["Bert"], editor.Shown.Select(p => p.Data.GivenName));
        Assert.True(window.Commands[ContactsCommandIds.RenameGroup].IsEnabled);

        _ = window.Commands.ExecuteAsync(ContactsCommandIds.AddMembers);
        var members = await DialogAsync<MembersDialog>(window);
        members.Search.Text = "zimmer";
        await SettleAsync();
        members.List.SelectedIndex = 0;
        await ClickAsync(members, members.OkButton);
        await SettleAsync();
        Assert.Equal(["Bert", "Frank"], editor.Shown.Select(p => p.Data.GivenName).Order());

        await window.Commands.ExecuteAsync(ContactsCommandIds.EmailGroup);
        Assert.Contains(data.Opened, address => address.StartsWith("mailto:", StringComparison.Ordinal) && address.Contains("praxis@zimmermann-am-park.example", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task A_vcard_handed_over_is_imported_and_a_csv_asks_for_its_columns()
    {
        using var data = new TestData();
        var window = await WindowAsync(sample: false);
        var editor = EditorOf(window);
        var vcf = data.File("Neu.vcf", "BEGIN:VCARD\r\nVERSION:3.0\r\nN:Lang;Lisa;;;\r\nFN:Lisa Lang\r\nEMAIL;TYPE=INTERNET:lisa@lang.example\r\nEND:VCARD\r\n");
        await TestData.App.ImportFilesAsync(window, [vcf]);
        await SettleAsync();
        Assert.Equal("Lisa Lang", editor.Shown.Single().Name);
        Assert.Equal("1 Kontakt importiert.", window.MessageBar.Text);

        data.Dialogs.Files.Enqueue(data.File("Liste.csv", "Vorname;Nachname;Lieblingsfarbe;Mail\nMax;Mut;Blau;max@mut.example\n"));
        _ = window.Commands.ExecuteAsync(ContactsCommandIds.Import);
        var mapping = await DialogAsync<CsvMappingDialog>(window);
        Assert.Equal(["given", "family", PersonCsvIgnore, PersonCsvIgnore], mapping.Mapping);
        mapping.Map(3, "email-1");
        await ClickAsync(mapping, mapping.OkButton);
        await SettleAsync();
        Assert.Equal("max@mut.example", editor.Shown.Single(p => p.Data.GivenName == "Max").Email);
    }

    [AvaloniaFact]
    public async Task Export_writes_the_selection_as_vcard_and_print_has_pages()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);
        editor.Select(editor.Shown.First(p => p.Data.FamilyName == "Neumann").Id);
        await SettleAsync();
        var target = Path.Combine(data.Folder, "Clara");
        data.Dialogs.SaveTargets.Enqueue(target);

        _ = window.Commands.ExecuteAsync(ContactsCommandIds.Export);
        var export = await DialogAsync<ExportDialog>(window);
        Assert.True(export.OnlySelected.IsChecked);
        await ClickAsync(export, export.OkButton);
        await SettleAsync();
        var text = await File.ReadAllTextAsync(target + ".vcf");
        Assert.Single(Regex.Matches(text, "BEGIN:VCARD"));
        Assert.Contains("FN:Clara Neumann", text, StringComparison.Ordinal);

        await PressAsync(window, PhysicalKey.P, CommandKey);
        var print = Assert.IsType<ContactsPrintPage>(window.Backstage.CurrentPage);
        Assert.StartsWith("Seite 1 von", print.PageLabel, StringComparison.Ordinal);
        print.ScopeChoice.SelectedIndex = 1;
        await SettleAsync();
        Assert.True(await print.PrintAsync());
    }

    [AvaloniaFact]
    public async Task Signed_in_the_contacts_sync_and_the_status_bar_says_so()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);
        Assert.False(window.Commands[ContactsCommandIds.Sync].IsEnabled);

        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "ANNA-PC");
        for (var round = 0; round < 50 && !editor.Document.IsSyncing; round++)
        {
            await SettleAsync();
        }

        Assert.True(editor.Document.IsSyncing);
        await window.Commands.ExecuteAsync(ContactsCommandIds.Sync);
        for (var round = 0; round < 50 && window.StatusBar.TextOf(ContactsEditor.SyncItem) != "Mit der ThixCloud synchronisiert"; round++)
        {
            await SettleAsync();
        }

        Assert.Equal(12, data.Cloud.SyncItems(CloudApps.Contacts, ContactsContract.Contacts).Count);
        Assert.Equal("Mit der ThixCloud synchronisiert", window.StatusBar.TextOf(ContactsEditor.SyncItem));

        data.Cloud.RemoveDevices();
        await Assert.ThrowsAsync<ThixOffice.Core.Models.BusinessException>(() => editor.Document.SyncNowAsync());
        for (var round = 0; round < 50 && editor.Document.IsSyncing; round++)
        {
            await SettleAsync();
        }

        await SettleAsync();
        Assert.Equal("Abgemeldet", window.StatusBar.TextOf(ContactsEditor.SyncItem));
        Assert.Contains("Anmeldung beendet", window.MessageBar.Text, StringComparison.Ordinal);
        Assert.Equal(12, editor.Session.Count); // the contacts stay
    }

    [AvaloniaFact]
    public async Task Merging_duplicates_keeps_one_contact()
    {
        using var data = new TestData();
        var window = await WindowAsync();
        var editor = EditorOf(window);

        _ = window.Commands.ExecuteAsync(ContactsCommandIds.Duplicates);
        var duplicates = await DialogAsync<DuplicatesDialog>(window);
        Assert.Single(duplicates.Sets);
        await ClickAsync(duplicates, duplicates.OkButton);
        var merge = await DialogAsync<MergeDialog>(window);
        merge.Choose("title", duplicates.Sets[0].ToList().FindIndex(p => p.Data.JobTitle == "Projektleitung"));
        await ClickAsync(merge, merge.OkButton);
        await SettleAsync();

        Assert.Equal(11, editor.Session.Count);
        Assert.Equal("Projektleitung", editor.Current!.Data.JobTitle);
        Assert.Contains("zusammengeführt", window.MessageBar.Text, StringComparison.Ordinal);
    }

    private const string PersonCsvIgnore = "";
}
