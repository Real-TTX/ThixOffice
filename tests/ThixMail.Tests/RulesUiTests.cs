using Avalonia.Headless.XUnit;
using ThixMail.Core;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Editor;
using ThixMail.Pages;
using ThixOffice.Desktop.Pages;
using static ThixMail.Tests.TestData;

namespace ThixMail.Tests;

/// <summary>Regeln: "Regel erstellen" from a message, Einstellungen › Regeln (list, order, Regeln jetzt anwenden), the ribbon, Rückgängig.</summary>
public sealed class RulesUiTests
{
    [AvaloniaFact]
    public async Task A_rule_made_from_a_message_moves_the_senders_mail_and_is_undone_in_one_step()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        var message = Header(editor, "Neu im November");
        editor.Select(message.Id);
        await SettleAsync();
        Assert.True(window.Commands[MailCommandIds.CreateRule].IsEnabled);
        Assert.False(window.Commands[MailCommandIds.RunRules].IsEnabled); // no rules yet

        // Start › Regel erstellen: the dialog comes with the sender
        _ = window.Commands.ExecuteAsync(MailCommandIds.CreateRule);
        var dialog = await OwnedAsync<RuleDialog>(window);
        Assert.Equal(message.From.Address, dialog.FromBox.Text);
        dialog.OkButton.Focus();
        await ClickAsync(dialog, dialog.OkButton);
        Assert.Contains("Aktion", dialog.ErrorText, StringComparison.Ordinal); // no action chosen yet
        await ClickAsync(dialog, dialog.MoveBox);
        var archiveIndex = ((IEnumerable<string>)dialog.FolderBox.ItemsSource!).ToList().IndexOf("Archiv");
        dialog.FolderBox.SelectedIndex = archiveIndex;
        await ClickAsync(dialog, dialog.ReadBox);
        await ClickAsync(dialog, dialog.OkButton);
        await UntilAsync(() => !dialog.IsVisible, "the dialog closed");
        var rule = Assert.Single(editor.Document.Accounts.Rules);
        Assert.True(rule.MarkRead);
        Assert.Contains(rule.Name, window.MessageBar.Text, StringComparison.Ordinal);

        // Regeln jetzt anwenden on the Posteingang: one step, Strg+Z takes it back
        await window.Commands.ExecuteAsync(MailCommandIds.RunRules);
        await SettleAsync();
        var archive = editor.Document.Store.FolderByRole(SampleMailbox.Account.Id, FolderRole.Archive)!;
        Assert.Contains(editor.Document.Store.List(new MessageQuery([archive.Id])), e => e.Id == message.Id);
        Assert.True(editor.Document.Store.Header(message.Id)!.IsRead);
        await PressAsync(window, Avalonia.Input.PhysicalKey.Z, CommandKey);
        await SettleAsync();
        Assert.NotEqual(archive.Id, editor.Document.Store.Header(message.Id)!.FolderId);
    }

    [AvaloniaFact]
    public async Task Einstellungen_Regeln_lists_orders_and_deletes_rules()
    {
        using var data = new TestData();
        var window = await data.WindowAsync();
        var editor = EditorOf(window);
        editor.Document.Accounts.Save(new MailRule { Name = "Erste", Order = 0, SubjectContains = "Rechnung", Flag = true });
        editor.Document.Accounts.Save(new MailRule { Name = "Zweite", Order = 1, FromContains = "news", MarkRead = true, IsEnabled = false });
        await window.Commands.ExecuteAsync(MailCommandIds.ManageRules);
        await SettleAsync();
        var settings = Assert.IsType<SettingsPage>(window.Backstage.CurrentPage);
        Assert.Equal(MailSettings.RulesTab, settings.CurrentTab);
        var panel = Assert.IsType<RulesPanel>(settings.AppTab(MailSettings.RulesTab));
        Assert.Equal(2, panel.List.ItemCount);
        Assert.False(panel.UpButton.IsEnabled);

        panel.List.SelectedIndex = 1;
        await ClickAsync(window, panel.UpButton);
        Assert.Equal(["Zweite", "Erste"], editor.Document.Accounts.Rules.Select(r => r.Name));
        Assert.Contains("ausgeschaltet", ((Avalonia.Controls.StackPanel)panel.List.Items[0]!).Children.OfType<Avalonia.Controls.TextBlock>().First().Text, StringComparison.Ordinal);

        panel.List.SelectedIndex = 1;
        await ClickAsync(window, panel.DeleteButton);
        var question = await OwnedAsync<ThixOffice.Desktop.Dialogs.QuestionDialog>(window);
        question.Buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent)); // Löschen
        await UntilAsync(() => editor.Document.Accounts.Rules.Count == 1, "the deleted rule");
        Assert.Equal("Zweite", editor.Document.Accounts.Rules.Single().Name);
    }

}
