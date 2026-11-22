using Avalonia;
using Avalonia.Controls;
using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Security;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixMail.Editor;

/// <summary>
/// A message in a window of its own (a double click in the list, an .eml handed over): read-only, with Antworten, Allen antworten, Weiterleiten,
/// Speichern unter and the same safety as the reading pane (pictures blocked until "Bilder laden", links shown before opening, attachments with a
/// warning for programs).
/// </summary>
public sealed class MessageWindow : MatWindow
{
    private readonly OfficeWindow _owner;
    private readonly ReadMail _mail;
    private readonly MessageHeader? _header;

    public MessageWindow(OfficeWindow owner, ReadMail mail, MessageHeader? header, byte[]? raw = null)
    {
        _owner = owner;
        _mail = mail;
        _header = header;
        Title = $"{(mail.Subject.Length > 0 ? mail.Subject : "(Kein Betreff)")} – Nachricht";
        Width = 860;
        Height = 760;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        View = new MessageView();
        View.Show(mail, header, null);
        View.LoadImagesRequested += async (_, _) =>
        {
            if (View.View is { } view)
            {
                View.Show(mail, header, await RemotePictures.FetchAsync(view.BlockedImages, CancellationToken.None));
            }
        };
        View.LinkRequested += async (_, address) =>
        {
            if (MailHtml.DescribeLink(address) is { } target && await QuestionDialog.ConfirmAsync(this, "Link öffnen?", $"Der Link führt zu:\n{target}", "Öffnen"))
            {
                LinkService.Open(address, Ui.OpenExternal);
            }
        };
        View.AttachmentRequested += async (_, request) =>
        {
            if (!request.Open)
            {
                if (await OfficeApplication.FileDialogs.PickExportTargetAsync(this, request.Attachment.FileName, SettingsService.SaveFolder, "Anlage", Path.GetExtension(request.Attachment.FileName)) is { } target)
                {
                    EmlFiles.Save(target, request.Attachment.Content);
                }

                return;
            }

            if (!AttachmentSafety.IsExecutable(request.Attachment.FileName)
                || await QuestionDialog.ConfirmAsync(this, "Anlage öffnen?", $"„{request.Attachment.FileName}“ kann ein Programm starten. Nur öffnen, wenn Sie dem Absender vertrauen.", "Trotzdem öffnen", isDestructive: true))
            {
                Ui.OpenExternal(EmlFiles.ForOpening(request.Attachment));
            }
        };

        ReplyButton = Action("back", "Antworten", () => Respond(DraftOrigin.Reply));
        ReplyAllButton = Action("users", "Allen antworten", () => Respond(DraftOrigin.ReplyAll));
        ForwardButton = Action("arrow-right", "Weiterleiten", () => Respond(DraftOrigin.Forward));
        var save = Action("save-as", "Speichern unter", async () =>
        {
            if (raw is not null && await OfficeApplication.FileDialogs.PickExportTargetAsync(this, EmlFiles.SuggestedName(mail.Subject), SettingsService.SaveFolder, "E-Mail-Nachricht", ".eml") is { } target)
            {
                EmlFiles.Save(target, raw);
            }
        });
        save.IsVisible = raw is not null;
        var bar = new ActionBar { Margin = new Thickness(16, 12, 16, 0), Children = { ReplyButton, ReplyAllButton, ForwardButton, save } };
        var dock = new DockPanel();
        DockPanel.SetDock(bar, Dock.Top);
        dock.Children.Add(bar);
        dock.Children.Add(View);
        Content = new Border { Child = dock }.Use(Border.BackgroundProperty, "Mat.Bg");
    }

    public MessageView View { get; }

    public Button ReplyButton { get; }

    public Button ReplyAllButton { get; }

    public Button ForwardButton { get; }

    private static Button Action(string icon, string text, Func<Task> run)
    {
        var button = new Button { Content = new IconLabel { Icon = icon, Text = text } };
        button.Click += async (_, _) => await run();
        return button;
    }

    private static Button Action(string icon, string text, Action run) => Action(icon, text, () =>
    {
        run();
        return Task.CompletedTask;
    });

    private void Respond(DraftOrigin origin)
    {
        var editor = (MailEditor)_owner.Editor;
        var account = (_header is not null ? editor.Document.Accounts.Account(_header.AccountId) : null) ?? editor.Document.Accounts.Accounts.FirstOrDefault();
        if (account is null)
        {
            return;
        }

        var draft = origin == DraftOrigin.Forward ? MailComposer.Forward(_mail, account, _header?.Id) : MailComposer.Reply(_mail, account, origin == DraftOrigin.ReplyAll, _header?.Id);
        MailCommands.OpenComposer(_owner, MailCommands.WithSignature(editor.Document, draft, account, reply: true));
        Close();
    }
}
