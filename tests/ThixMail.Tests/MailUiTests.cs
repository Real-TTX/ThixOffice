using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixMail.Compose;
using ThixMail.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Editor;
using ThixMail.Pages;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using static ThixMail.Tests.TestData;

namespace ThixMail.Tests;

/// <summary>The real ThixMail headless, through keys, text and the mouse, against a mail server in memory.</summary>
public sealed class MailUiTests
{
    [AvaloniaFact]
    public async Task The_window_is_a_workspace_and_without_an_account_it_offers_to_add_one()
    {
        using var data = new TestData();
        var window = await data.WindowAsync(sample: false);
        var editor = EditorOf(window);
        Assert.Same(window, TestData.App.NewDocumentWindow());
        Assert.Null(window.Commands.Find(CommonCommandIds.Save));
        Assert.Equal([AccountsPage.PageId, MailPrintPage.PageId, OfficeExportPage.PageId], window.Backstage.PageIds);
        Assert.Equal([true, true, true], editor.NavigationButtons.Select(b => b.IsVisible)); // E-Mail, Kalender, Personen
        Assert.Empty(editor.Rows);
        Assert.Equal("Kein Konto eingerichtet", window.StatusBar.TextOf(MailEditor.SyncItem));
        Assert.False(window.Commands[MailCommandIds.NewMail].IsEnabled);
        Assert.Contains(window.Commands.Search("weiterleiten"), c => c.Id == MailCommandIds.Forward);
        Assert.Contains(window.Commands.Search("imap"), c => c.Id == MailCommandIds.AddAccount);
    }

    [AvaloniaFact]
    public async Task An_account_is_added_on_the_Konten_page_after_the_connection_test()
    {
        using var data = new TestData();
        var window = await data.WindowAsync(sample: false);
        await window.Commands.ExecuteAsync(MailCommandIds.AddAccount);
        await SettleAsync();
        var page = Assert.IsType<AccountsPage>(window.Backstage.CurrentPage);
        var form = Assert.IsType<AccountForm>(page.Form);

        form.SenderBox.Text = "Anna Berger";
        form.AddressBox.Text = SampleMailbox.Address;
        form.PasswordBox.Text = "falsch";
        await ClickAsync(window, form.NextButton);
        await UntilAsync(() => form.ShowsDetails && form.Result.Text.Contains("Kennwort", StringComparison.Ordinal), () => $"the refused password (details {form.ShowsDetails}, result: {form.Result.Text})");
        Assert.Equal("imap.beispiel.de", form.ImapHost.Text); // guessed (no autoconfig answered)
        Assert.Equal("smtp.beispiel.de", form.SmtpHost.Text);

        form.PasswordBox.Text = SampleMailbox.Password;
        await ClickAsync(window, form.TestButton);
        await UntilAsync(() => form.Result.Text.StartsWith("Verbindung hergestellt", StringComparison.Ordinal), "the connection test");
        await ClickAsync(window, form.SaveButton);
        await UntilAsync(() => page.Form is null, "the saved account");
        Assert.Contains("eingerichtet", page.Message.Text, StringComparison.Ordinal);

        var editor = EditorOf(window);
        window.Backstage.Close();
        await UntilAsync(() => editor.Rows.Any(r => r.IsMessage), "the messages of the new account");
        Assert.Contains(editor.Folders.ItemsSource!.Cast<FolderNode>(), n => n.Label == "Gesendete Elemente");
        Assert.DoesNotContain(SampleMailbox.Password, System.IO.File.ReadAllText(Path.Combine(data.Folder, "data", "settings.json")), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_certificate_the_system_does_not_trust_is_shown_and_accepted_only_on_purpose()
    {
        using var data = new TestData();
        data.Server.HasUntrustedCertificate = true;
        var window = await data.WindowAsync(sample: false);
        await window.Commands.ExecuteAsync(MailCommandIds.AddAccount);
        await SettleAsync();
        var form = ((AccountsPage)window.Backstage.CurrentPage!).Form!;
        form.AddressBox.Text = SampleMailbox.Address;
        form.PasswordBox.Text = SampleMailbox.Password;
        await ClickAsync(window, form.NextButton);
        await UntilAsync(() => form.ShowsCertificate, "the certificate");
        Assert.False(await form.SaveAsync()); // never silently
        await ClickAsync(window, form.TrustButton);
        await UntilAsync(() => form.Result.Text.StartsWith("Verbindung hergestellt", StringComparison.Ordinal), "the test with the accepted certificate");
        Assert.True(await form.SaveAsync());
        Assert.Single(EditorOf(window).Document.Accounts.Accounts.Single().TrustedCertificates);
    }

    [AvaloniaFact]
    public async Task Reading_a_message_marks_it_read_and_remote_pictures_wait_for_Bilder_laden()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var newsletter = Header(editor, "Neu im November");
        Assert.False(newsletter.IsRead);

        editor.Select(newsletter.Id);
        await UntilAsync(() => editor.Reader.Mail is not null, "the newsletter in the reading pane");
        Assert.Equal("Neu im November: 20 % auf die Herbstkollektion", editor.Reader.SubjectText);
        Assert.True(editor.Reader.ShowsImagesBar);
        Assert.True(editor.Document.Store.Header(newsletter.Id)!.IsRead);
        Assert.DoesNotContain(editor.Reader.Surface!.Session.Document.Blocks.OfType<ThixWrite.Core.Model.Paragraph>().SelectMany(p => p.Inlines), i => i is ThixWrite.Core.Model.ImageInline);

        await ClickAsync(window, editor.Reader.LoadImagesButton);
        await UntilAsync(() => !editor.Reader.ShowsImagesBar, () => $"the loaded pictures (loaded: {editor.LoadedImagesOfShown?.Count.ToString() ?? "none"}, blocked: {editor.Reader.View?.BlockedImages.Count})");
        Assert.Contains(editor.Reader.Surface!.Session.Document.Blocks.OfType<ThixWrite.Core.Model.Paragraph>().SelectMany(p => p.Inlines), i => i is ThixWrite.Core.Model.ImageInline);
    }

    [AvaloniaFact]
    public async Task Reply_writes_in_its_own_window_and_Ctrl_Enter_sends_through_the_outbox()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var question = Header(editor, "Präsentation für Donnerstag");
        editor.Select(question.Id);
        await UntilAsync(() => editor.Reader.Mail is not null, "the message in the reading pane");

        await PressAsync(window, PhysicalKey.R, CommandKey);
        var compose = await OwnedAsync<ComposeWindow>(window);
        Assert.Equal("AW: Präsentation für Donnerstag", compose.SubjectBox.Text);
        Assert.Contains("maria.schulz@kunde.example", compose.To.Box.Text, StringComparison.Ordinal);
        compose.Surface.Focus();
        await SettleAsync();
        await TypeAsync(compose, "Vielen Dank, ich sehe es mir an.");
        Assert.Contains("Vielen Dank", compose.Surface.Session.Document.GetPlainText(), StringComparison.Ordinal);

        await PressAsync(compose, PhysicalKey.Enter, CommandKey);
        await UntilAsync(() => data.Server.Sent.Count == 1, "the message at the server");
        var sent = ThixMail.Core.Mime.MailReader.Read(data.Server.Sent[0].Raw);
        Assert.Equal("AW: Präsentation für Donnerstag", sent.Subject);
        Assert.Contains("Vielen Dank, ich sehe es mir an.", sent.Text, StringComparison.Ordinal);
        Assert.Contains("Anna Berger", sent.Text, StringComparison.Ordinal); // the reply signature
        Assert.Contains("Von:", sent.Html, StringComparison.Ordinal); // the quote's header block
        await UntilAsync(() => editor.Document.Store.Header(question.Id)!.Flags.HasFlag(MessageFlags.Answered), "the answered flag");
    }

    [AvaloniaFact]
    public async Task The_composer_marks_unknown_words_while_typing_and_pastes_HTML_of_other_programs()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        await PressAsync(window, PhysicalKey.N, CommandKey);
        var compose = await OwnedAsync<ComposeWindow>(window);
        compose.Surface.Focus();
        await SettleAsync();
        await TypeAsync(compose, "Ein Fehlr hier ");
        await UntilAsync(() => compose.Surface.UnderlinedWords.Count == 1, "the wavy line under the unknown word");

        var suggestion = compose.Spelling.MenuItems(compose.Surface.UnderlinedWords[0].Start).OfType<MenuItem>().First(item => (string?)item.Header == "Fehler");
        suggestion.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        Assert.StartsWith("Ein Fehler hier", compose.Surface.Session.Document.GetPlainText(), StringComparison.Ordinal);
        await UntilAsync(() => compose.Surface.UnderlinedWords.Count == 0, "no wavy line after the suggestion");

        Assert.True(compose.PasteHtml("Version:0.9\r\nStartHTML:-1\r\nEndHTML:-1\r\n<html><body><!--StartFragment--><p>Von <b>außen</b><script>alert(1)</script> <a href=\"javascript:alert(1)\">böse</a></p><!--EndFragment--></body></html>"));
        var text = compose.Surface.Session.Document.GetPlainText();
        Assert.Contains("Von außen", text, StringComparison.Ordinal);
        Assert.DoesNotContain("alert", text, StringComparison.Ordinal);
        Assert.Contains(compose.Surface.Session.Document.Blocks.OfType<ThixWrite.Core.Model.Paragraph>().SelectMany(p => p.Inlines),
            inline => inline is ThixWrite.Core.Model.TextRun { Text: "außen", Properties.Bold: true });
        Assert.DoesNotContain(compose.Surface.Session.Document.Blocks.OfType<ThixWrite.Core.Model.Paragraph>().SelectMany(p => p.Inlines), inline => inline.Link is not null);
        Assert.Contains("<b>au", compose.CurrentDraft().Html, StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task A_new_message_is_checked_before_sending_and_attachments_are_listed()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        await PressAsync(window, PhysicalKey.N, CommandKey);
        var compose = await OwnedAsync<ComposeWindow>(window);
        Assert.False(await compose.SendAsync());
        Assert.Contains("Empfänger", compose.MessageBar.Text, StringComparison.Ordinal);

        compose.To.Box.Text = "kaputt";
        Assert.False(await compose.SendAsync());
        Assert.Contains("keine gültige E-Mail-Adresse", compose.MessageBar.Text, StringComparison.Ordinal);

        var file = data.File("Bericht Übersicht.pdf", new byte[2048]);
        await compose.AttachFilesAsync([file]);
        Assert.Single(compose.Attachments);
        compose.To.Box.Text = "Lena Koch <lena.koch@beispiel.de>";
        compose.SubjectBox.Text = "Bericht";
        Assert.True(await compose.SendAsync());
        await UntilAsync(() => data.Server.Sent.Count == 1, "the message at the server");
        Assert.Equal("Bericht Übersicht.pdf", ThixMail.Core.Mime.MailReader.Read(data.Server.Sent[0].Raw).Attachments.Single().FileName);
        Assert.Contains(EditorOf(window).Document.SuggestAddresses("lena"), a => a.Address == "lena.koch@beispiel.de");
    }

    /// <summary>
    /// The race behind a flaky run: a text change of an address field is raised by Avalonia in a posted job; when it ran after the main window had closed
    /// the document, the suggestions queried the closed store ("ExecuteReader can only be called when the connection is open"). Here the order is forced:
    /// the store is closed first, then the field's text changes and the posted jobs run.
    /// </summary>
    [AvaloniaFact]
    public async Task A_compose_window_keeps_its_draft_and_never_touches_the_store_after_the_main_window_closed()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var document = EditorOf(window).Document;
        await PressAsync(window, PhysicalKey.N, CommandKey);
        var compose = await OwnedAsync<ComposeWindow>(window);
        compose.SubjectBox.Text = "Noch nicht fertig";
        await SettleAsync();

        // the document closes (the end of the process) with the compose window open: the draft is kept and the window lets go of the store
        await document.CloseAsync();
        Assert.True(document.Store.IsDisposed);
        Assert.False(compose.IsVisible); // closed with the document, without a question
        using (var reopened = MailStore.Open(document.Store.Path))
        {
            var drafts = reopened.FolderByRole(SampleMailbox.Account.Id, FolderRole.Drafts)!;
            Assert.Contains(reopened.Headers([.. reopened.List(new MessageQuery([drafts.Id])).Select(e => e.Id)]), h => h.Subject == "Noch nicht fertig"); // kept in "Entwürfe"
        }

        compose.To.Box.Text = "len"; // a late text change (posted by Avalonia) after the store closed
        Settle();
        Assert.Empty(document.SuggestAddresses("len"));
    }

    [AvaloniaFact]
    public async Task Delete_moves_into_Geloeschte_Elemente_and_Rueckgaengig_brings_it_back()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var message = Header(editor, "Wartung am Wochenende");
        editor.Select(message.Id);
        await SettleAsync();
        editor.List.Focus();

        // Entf; on the Mac ⌘⌫ like Outlook for Mac
        await PressAsync(window, OperatingSystem.IsMacOS() ? PhysicalKey.Backspace : PhysicalKey.Delete, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta : RawInputModifiers.None);
        var trash = editor.Document.Store.FolderByRole(SampleMailbox.Account.Id, FolderRole.Trash)!;
        Assert.Equal(trash.Id, editor.Document.Store.Header(message.Id)!.FolderId);
        Assert.Equal("1 Element wurde gelöscht.", window.MessageBar.Text);
        await ClickAsync(window, window.MessageBar.ActionButton);
        Assert.NotEqual(trash.Id, editor.Document.Store.Header(message.Id)!.FolderId);
        await editor.Document.SyncOf(SampleMailbox.Account.Id)!.SyncOnceAsync();
        Assert.Empty(data.Server.UidsOf(SampleMailbox.Address, "Trash"));
    }

    [AvaloniaFact]
    public async Task Messages_dragged_onto_a_folder_move_there_and_the_server_follows()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var message = Header(editor, "Reisekostenabrechnung");
        editor.Select(message.Id);
        await SettleAsync();
        var row = editor.List.GetRealizedContainers().OfType<ListBoxItem>().First(item => item.DataContext is ListRow r && r.Id == message.Id);
        var target = editor.Folders.GetRealizedContainers().OfType<ListBoxItem>().First(item => item.DataContext is FolderNode { Label: "Projekte" });
        var from = row.TranslatePoint(new Point(40, row.Bounds.Height / 2), window)!.Value;
        var to = target.TranslatePoint(new Point(40, target.Bounds.Height / 2), window)!.Value;

        window.MouseDown(from, MouseButton.Left);
        window.MouseMove(new Point(from.X - 30, from.Y), RawInputModifiers.LeftMouseButton);
        window.MouseMove(to, RawInputModifiers.LeftMouseButton);
        window.MouseUp(to, MouseButton.Left);
        await SettleAsync();

        var projects = editor.Document.Store.FolderByPath(SampleMailbox.Account.Id, "Projekte")!;
        Assert.Equal(projects.Id, editor.Document.Store.Header(message.Id)!.FolderId);
        await editor.Document.SyncOf(SampleMailbox.Account.Id)!.SyncOnceAsync();
        Assert.Contains("Reisekostenabrechnung Oktober", data.Server.SubjectsOf(SampleMailbox.Address, "Projekte"));
    }

    [AvaloniaFact]
    public async Task Ctrl_E_searches_and_Esc_ends_the_search()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var all = editor.Rows.Count(r => r.IsMessage);

        // Strg+E; on the Mac ⌥⌘F like Outlook for Mac
        await PressAsync(window, OperatingSystem.IsMacOS() ? PhysicalKey.F : PhysicalKey.E, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta | RawInputModifiers.Alt : RawInputModifiers.Control);
        Assert.True(editor.SearchBox.IsFocused);
        await TypeAsync(window, "sommerfest");
        await UntilAsync(() => editor.Rows.Count(r => r.IsMessage) == 1, "the search result");
        Assert.Equal("Einladung zum Sommerfest", editor.HeaderOf(editor.Rows.First(r => r.IsMessage).Id)!.Subject);

        await PressAsync(window, PhysicalKey.Escape);
        await UntilAsync(() => editor.Rows.Count(r => r.IsMessage) == all, "the whole list again");
    }

    [AvaloniaFact]
    public async Task The_list_toolbar_works_like_Outlooks_one_search_field_the_views_and_the_order_with_its_direction()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var all = editor.Rows.Count(r => r.IsMessage);

        // one search field: the scope sits in it, ✕ only while searching and it ends the search
        Assert.Equal("Suchen", editor.SearchBox.Watermark);
        Assert.Equal("Aktueller Ordner", editor.ScopeChoice.SelectedItem);
        Assert.False(editor.ClearSearchButton.IsVisible);
        editor.SearchBox.Text = "sommerfest";
        await UntilAsync(() => editor.Rows.Count(r => r.IsMessage) == 1, "the search result");
        Assert.True(editor.ClearSearchButton.IsVisible);
        await ClickAsync(window, editor.ClearSearchButton);
        await UntilAsync(() => editor.Rows.Count(r => r.IsMessage) == all, "the whole list again");
        Assert.False(editor.ClearSearchButton.IsVisible);

        // "Ungelesen" over the list, "Alle" back
        await ClickAsync(window, editor.UnreadViewButton);
        await UntilAsync(() => editor.Rows.Count(r => r.IsMessage) < all, "only the unread");
        Assert.Equal(1, editor.FilterChoice.SelectedIndex);
        await ClickAsync(window, editor.AllViewButton);
        await UntilAsync(() => editor.Rows.Count(r => r.IsMessage) == all, "every message again");

        // "Nach Datum" newest first; the arrow turns it round
        Assert.Equal("Nach Datum", editor.SortChoice.SelectedItem);
        Assert.True(editor.SortDescending);
        DateTimeOffset First() => editor.HeaderOf(editor.Rows.First(r => r.Kind == RowKind.Message).Id)!.Date;
        DateTimeOffset Last() => editor.HeaderOf(editor.Rows.Last(r => r.Kind == RowKind.Message).Id)!.Date;
        Assert.True(First() >= Last());
        await ClickAsync(window, editor.SortDirectionButton);
        Assert.False(editor.SortDescending);
        await UntilAsync(() => First() < Last(), "the oldest first");

        // another order starts with its own direction: A to Z for the sender
        editor.SortChoice.SelectedIndex = 1;
        Assert.False(editor.SortDescending);
        editor.SortChoice.SelectedIndex = 0;
        Assert.True(editor.SortDescending);
    }

    [AvaloniaFact]
    public async Task A_conversation_opens_with_the_right_arrow_and_shows_its_messages()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var conversation = editor.Rows.First(r => r is { Kind: RowKind.Message, Count: 3 });
        editor.List.SelectedItem = conversation;
        await SettleAsync();
        editor.FocusDocument();
        await SettleAsync();

        await PressAsync(window, PhysicalKey.ArrowRight);
        Assert.Equal(2, editor.Rows.Count(r => r.Kind == RowKind.Child && r.Thread == conversation.Thread));
        await PressAsync(window, PhysicalKey.ArrowLeft);
        Assert.DoesNotContain(editor.Rows, r => r.Kind == RowKind.Child);

        await window.Commands.ExecuteAsync(MailCommandIds.Conversations); // off: every message a row of its own
        Assert.DoesNotContain(editor.Rows, r => r.Count > 1);
    }

    [AvaloniaFact]
    public async Task Working_offline_keeps_sent_mail_in_the_outbox_until_online_again()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(MailCommandIds.WorkOffline);
        await SettleAsync();
        Assert.True(editor.Document.IsWorkingOffline);
        editor.Session.Send(new Draft { AccountId = SampleMailbox.Account.Id, To = [new MailAddress("", "lena.koch@beispiel.de")], Subject = "Offline", Html = "<p>x</p>" });
        await SettleAsync();
        Assert.Equal("Offline arbeiten · Postausgang: 1", window.StatusBar.TextOf(MailEditor.SyncItem));
        Assert.False(window.Commands[MailCommandIds.SendReceive].IsEnabled);
        await Task.Delay(200);
        Assert.Empty(data.Server.Sent);

        await window.Commands.ExecuteAsync(MailCommandIds.WorkOffline);
        await UntilAsync(() => data.Server.Sent.Count == 1, "the message sent after going online");
        await UntilAsync(() => window.StatusBar.TextOf(MailEditor.SyncItem) == "Alle Ordner sind auf dem neuesten Stand.", "the state");
    }

    [AvaloniaFact]
    public async Task New_mail_while_the_window_is_in_the_background_shows_a_notification()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var sync = editor.Document.SyncOf(SampleMailbox.Account.Id)!;
        await UntilAsync(() => sync.Status.Phase == ThixMail.Core.Sync.SyncPhase.Idle, "the loop idling");
        window.Hide();
        data.Server.Deliver(SampleMailbox.Address, "INBOX", new MailAddress("Jonas Weber", "jonas.weber@beispiel.de"), "Kurze Frage", "Hast du kurz Zeit?");
        await UntilAsync(() => data.Notifier.Shown.Count == 1, "the notification");
        Assert.Equal(("Jonas Weber", "Kurze Frage"), (data.Notifier.Shown[0].Title, data.Notifier.Shown[0].Text));
        data.Notifier.Shown[0].Click();
        await SettleAsync();
        Assert.True(window.IsVisible);
        Assert.Equal("Kurze Frage", editor.Current?.Subject);
    }

    /// <summary>Found with the published ThixMail on Windows: a minimized window stays "active" until another window takes the focus – it must notify.</summary>
    [AvaloniaFact]
    public async Task New_mail_while_the_window_is_minimized_shows_a_notification_although_it_is_still_active()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var sync = EditorOf(window).Document.SyncOf(SampleMailbox.Account.Id)!;
        await UntilAsync(() => sync.Status.Phase == ThixMail.Core.Sync.SyncPhase.Idle, "the loop idling");
        window.Activate();
        window.WindowState = WindowState.Minimized;
        await SettleAsync();
        data.Server.Deliver(SampleMailbox.Address, "INBOX", new MailAddress("Jonas Weber", "jonas.weber@beispiel.de"), "Noch eine Frage", "Kurz?");
        await UntilAsync(() => data.Notifier.Shown.Count == 1, "the notification of the minimized window");
        window.WindowState = WindowState.Normal;

        // in front (active, shown, not minimized): no notification
        window.Activate();
        await SettleAsync();
        if (window.IsActive)
        {
            data.Server.Deliver(SampleMailbox.Address, "INBOX", new MailAddress("Jonas Weber", "jonas.weber@beispiel.de"), "Und noch eine", "Kurz?");
            await sync.SyncOnceAsync();
            await SettleAsync();
            Assert.Single(data.Notifier.Shown);
        }
    }

    [AvaloniaFact]
    public async Task A_click_on_a_system_notification_comes_back_as_a_link_and_opens_the_message()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var sync = editor.Document.SyncOf(SampleMailbox.Account.Id)!;
        await UntilAsync(() => sync.Status.Phase == ThixMail.Core.Sync.SyncPhase.Idle, "the loop idling");
        window.Hide();
        data.Server.Deliver(SampleMailbox.Address, "INBOX", new MailAddress("Lena Koch", "lena.koch@beispiel.de"), "Per Toast", "Hallo");
        await UntilAsync(() => data.Notifier.Links.Count == 1, "the notification");
        var link = data.Notifier.Links[0];
        Assert.StartsWith("thixmail:message?id=", link, StringComparison.Ordinal);

        // Windows starts "ThixMail.exe thixmail:message?id=…"; the second start hands the arguments to this instance
        var handedOver = ThixOffice.Desktop.OfficeApplication.DocumentFilesIn(["--quiet-not-a-file", link, "kein-link:x"]);
        Assert.Equal([link], handedOver);
        TestData.App.HandleActivation(handedOver);
        await SettleAsync();
        Assert.True(window.IsVisible);
        Assert.Equal("Per Toast", editor.Current?.Subject);
        Assert.Null(MailLinks.MessageId("thixmail:message?id=abc"));
        Assert.Null(MailLinks.MessageId("thixmail:anderes"));
    }

    [AvaloniaFact]
    public async Task An_eml_handed_over_opens_in_a_message_window_and_Speichern_unter_writes_one()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        await TestData.App.ImportFilesAsync(window, [FixturePath("02-simple-html.eml")]);
        var message = await OwnedAsync<MessageWindow>(window);
        Assert.Equal("Einladung zum Sommerfest", message.View.SubjectText);
        message.Close();

        var editor = EditorOf(window);
        editor.Select(Header(editor, "Einladung zum Sommerfest").Id);
        await SettleAsync();
        var target = Path.Combine(data.Folder, "Sommerfest.eml");
        data.Dialogs.SaveTargets.Enqueue(target);
        await window.Commands.ExecuteAsync(MailCommandIds.SaveAs);
        Assert.Equal("Einladung zum Sommerfest", ThixMail.Core.Mime.MailReader.Read(System.IO.File.ReadAllBytes(target)).Subject);
    }

    [AvaloniaFact]
    public async Task The_settings_have_the_mail_tabs_and_signatures_are_made_there()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        window.ShowSettings(MailSettings.SignaturesTab);
        await SettleAsync();
        var settings = Assert.IsType<SettingsPage>(window.Backstage.CurrentPage);
        Assert.Equal(MailSettings.SignaturesTab, settings.CurrentTab);
        foreach (var tab in new[] { MailSettings.ReadingTab, MailSettings.ComposeTab, MailSettings.NotificationsTab })
        {
            Assert.NotNull(settings.AppTab(tab));
        }

        var panel = Assert.IsType<SignaturesPanel>(settings.AppTab(MailSettings.SignaturesTab));
        await ClickAsync(window, panel.NewButton);
        Assert.Equal(2, EditorOf(window).Document.Accounts.Signatures.Count);
        await SettleAsync();
        panel.Editor.Text = "Beste Grüße\nAnna";
        panel.SaveText();
        Assert.Contains(EditorOf(window).Document.Accounts.Signatures, s => s.Text == "Beste Grüße\nAnna" && s.Html.Contains("Beste Gr", StringComparison.Ordinal));
    }

    [AvaloniaFact]
    public async Task The_list_of_two_hundred_thousand_messages_stays_quick()
    {
        using var data = new TestData(sampleServer: false);
        var window = await data.WindowAsync(sample: false);
        var editor = EditorOf(window);
        var store = editor.Document.Store;
        editor.Document.Accounts.Save(SampleMailbox.Account);
        store.SyncFolders(SampleMailbox.Account.Id, [new ThixMail.Core.Transport.RemoteFolder("INBOX", "INBOX", null, '/', FolderRole.Inbox, true)]);
        var inbox = store.FolderByRole(SampleMailbox.Account.Id, FolderRole.Inbox)!;
        var start = DateTimeOffset.Now.AddDays(-500);
        for (var batch = 0; batch < 200; batch++)
        {
            store.AddMessages(inbox.Id, SampleMailbox.Account.Id, [.. Enumerable.Range((batch * 1000) + 1, 1000).Select(uid => new ThixMail.Core.Transport.RemoteMessage
            {
                Uid = (uint)uid,
                MessageId = $"m{uid}@x",
                Subject = $"Nachricht {uid}",
                From = new MailAddress("Absender", $"a{uid % 40}@beispiel.de"),
                Date = start.AddMinutes(uid * 3),
            })]);
        }

        var watch = System.Diagnostics.Stopwatch.StartNew();
        editor.SelectFolder(inbox.Id);
        editor.Refresh();
        await SettleAsync();
        watch.Stop();
        Assert.True(editor.Rows.Count > 200_000);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(6), $"showing 200,000 messages took {watch.Elapsed}");
        Assert.True(editor.List.GetRealizedContainers().Count() < 200, "the list is not virtualized");
        editor.List.SelectedItem = editor.Rows.First(r => r.IsMessage);
        await PressAsync(window, PhysicalKey.End);
        await SettleAsync();
    }
}
