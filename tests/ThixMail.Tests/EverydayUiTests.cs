using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixMail.Compose;
using ThixMail.Core.Model;
using ThixMail.Editor;
using static ThixMail.Tests.TestData;

namespace ThixMail.Tests;

/// <summary>Suchordner, Später senden, Vorlagen – through the window.</summary>
public sealed class EverydayUiTests
{
    [AvaloniaFact]
    public async Task The_search_folders_list_unread_and_flagged_mail_of_every_account()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var nodes = editor.Folders.ItemsSource!.Cast<FolderNode>().ToList();
        Assert.Contains(nodes, n => n.Kind == FolderNodeKind.SearchUnread && n.Label == "Ungelesene E-Mail");
        Assert.Contains(nodes, n => n.Kind == FolderNodeKind.SearchFlagged && n.Label == "Zur Nachverfolgung");

        editor.SelectSearchFolder(FolderNodeKind.SearchFlagged);
        await SettleAsync();
        var flagged = editor.Rows.Where(r => r.IsMessage).Select(r => editor.HeaderOf(r.Id)!).ToList();
        Assert.NotEmpty(flagged);
        Assert.All(flagged, h => Assert.True(h.IsFlagged));

        editor.SelectSearchFolder(FolderNodeKind.SearchUnread);
        await SettleAsync();
        await UntilAsync(() => editor.Rows.Any(r => r.IsMessage), "the unread mail");
        var unread = editor.Rows.Where(r => r.IsMessage).Select(r => editor.HeaderOf(r.Id)!).ToList();
        Assert.NotEmpty(unread);
        Assert.True(unread.Count(h => h.IsRead) <= 1); // the one read in this view stays in it until the view changes (as in Outlook)
        Assert.DoesNotContain(unread, h => h.Flags.HasFlag(MessageFlags.Draft));
    }

    [AvaloniaFact]
    public async Task Spaeter_senden_keeps_the_message_in_the_outbox_and_a_template_starts_a_new_one()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        await PressAsync(window, PhysicalKey.N, CommandKey);
        var compose = await OwnedAsync<ComposeWindow>(window);
        compose.To.Box.Text = "lena.koch@beispiel.de";
        compose.SubjectBox.Text = "Wochenbericht";
        compose.Surface.Focus();
        await SettleAsync();
        await TypeAsync(compose, "Hier der Bericht der Woche.");

        // Als Vorlage speichern
        compose.SaveTemplate("Wochenbericht");
        Assert.Equal("Wochenbericht", Assert.Single(editor.Document.Accounts.Templates).Subject);

        // Später senden: a time in the past is refused, tomorrow is taken
        var sending = compose.SendLaterAsync();
        await UntilAsync(() => compose.LastSendLaterDialog is { IsVisible: true }, "the dialog");
        var dialog = compose.LastSendLaterDialog!;
        dialog.DateBox.Text = "01.01.2020";
        dialog.Confirm();
        Assert.True(dialog.IsVisible);
        dialog.DateBox.Text = DateTime.Today.AddDays(1).ToString("dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-DE"));
        dialog.Confirm();
        Assert.False(dialog.IsVisible, dialog.DateBox.Text);
        Assert.True(await sending);
        await UntilAsync(() => !compose.IsVisible, () => $"the window closed ({compose.MessageBar.Text})");
        var waiting = Assert.Single(editor.Document.Store.Outbox());
        Assert.NotNull(waiting.SendAt);
        await Task.Delay(300);
        Assert.Empty(data.Server.Sent);

        // Neue E-Mail aus Vorlage
        Assert.True(window.Commands[MailCommandIds.NewFromTemplate].IsEnabled);
        _ = window.Commands.ExecuteAsync(MailCommandIds.NewFromTemplate);
        var templates = await OwnedAsync<TemplateDialog>(window);
        templates.Confirm();
        var fromTemplate = await OwnedAsync<ComposeWindow>(window);
        Assert.Equal("Wochenbericht", fromTemplate.SubjectBox.Text);
        Assert.Contains("Bericht der Woche", fromTemplate.Surface.Session.Document.GetPlainText(), StringComparison.Ordinal);
        Assert.Equal("", fromTemplate.To.Box.Text ?? "");
        fromTemplate.CloseDiscarding();
    }
}
