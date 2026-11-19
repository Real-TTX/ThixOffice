using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>Einfügen › Link (Strg+K), Link entfernen, Link öffnen (also Strg+Klick on a link).</summary>
public static class LinkCommands
{
    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;
        bool OnLink() => Editor()?.Session.LinkAtCaret is not null;

        var commands = window.Commands;
        commands.Add(new OfficeCommand(WriteCommandIds.Hyperlink, "Link", () => Editor() is { } editor ? HyperlinkDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "link", Shortcut = "Ctrl+K", CanExecute = HasEditor,
            Description = "Verknüpft den markierten Text mit einer Webseite oder einer E-Mail-Adresse.", Keywords = ["link", "hyperlink", "verknüpfung", "url", "adresse"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.HyperlinkRemove, "Link entfernen", () => Editor()?.Session.RemoveHyperlink())
        {
            Icon = "x", CanExecute = OnLink, Description = "Macht aus dem Link wieder gewöhnlichen Text.",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.HyperlinkOpen, "Link öffnen", () => Open(window, Editor()?.Session.LinkAtCaret?.Uri))
        {
            Icon = "external", CanExecute = OnLink, Description = "Öffnet die Adresse des Links im Browser oder E-Mail-Programm.",
        });
    }

    /// <summary>Opens a link's address through the business layer (only web pages and e-mail); a refusal shows a message.</summary>
    public static Task Open(OfficeWindow window, string? address) =>
        OfficeWindow.RunAsync(window, "Link öffnen", () =>
        {
            LinkService.Open(address, Ui.OpenExternal);
            return Task.FromResult(true);
        });
}
