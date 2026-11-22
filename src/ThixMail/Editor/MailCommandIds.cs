namespace ThixMail.Editor;

/// <summary>The ids of ThixMail's commands (<c>mail.&lt;name&gt;</c>): ribbon items, keys, context menus and the search use them.</summary>
public static class MailCommandIds
{
    public const string NewFromTemplate = "mail.newFromTemplate";
    public const string CreateRule = "mail.rules.create";
    public const string ManageRules = "mail.rules.manage";
    public const string RunRules = "mail.rules.run";

    // Start
    public const string NewMail = "mail.new";
    public const string Delete = "mail.delete";
    public const string Archive = "mail.archive";
    public const string Reply = "mail.reply";
    public const string ReplyAll = "mail.replyAll";
    public const string Forward = "mail.forward";
    public const string Move = "mail.move";
    public const string MarkRead = "mail.markRead";
    public const string MarkUnread = "mail.markUnread";
    public const string ToggleRead = "mail.toggleRead";
    public const string Categorize = "mail.categorize";
    public const string Flag = "mail.flag";
    public const string Search = "mail.search";
    public const string Copy = "mail.copy";
    public const string SelectAll = "mail.selectAll";
    public const string OpenMessage = "mail.open";
    public const string SaveAs = "mail.saveAs";
    public const string Print = "mail.print";

    // Senden/Empfangen
    public const string SendReceive = "mail.sendReceive";
    public const string WorkOffline = "mail.workOffline";
    public const string ShowOutbox = "mail.outbox";

    // Ordner
    public const string NewFolder = "mail.folder.new";
    public const string RenameFolder = "mail.folder.rename";
    public const string DeleteFolder = "mail.folder.delete";
    public const string MarkAllRead = "mail.folder.markAllRead";
    public const string EmptyFolder = "mail.folder.empty";

    // Ansicht
    public const string PaneRight = "mail.view.paneRight";
    public const string PaneBottom = "mail.view.paneBottom";
    public const string PaneOff = "mail.view.paneOff";
    public const string Conversations = "mail.view.conversations";
    public const string DensityCompact = "mail.view.compact";
    public const string DensityComfortable = "mail.view.comfortable";
    public const string FolderPane = "mail.view.folderPane";

    // the reading pane
    public const string LoadImages = "mail.loadImages";
    public const string AddAccount = "mail.addAccount";
    public const string AddSenderToContacts = "mail.addSenderToContacts";
}
