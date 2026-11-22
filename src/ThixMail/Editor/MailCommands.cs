using ThixMail.Compose;
using ThixMail.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Pages;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;

namespace ThixMail.Editor;

/// <summary>
/// ThixMail's commands – what the ribbon, "Was möchten Sie tun?", the keys and the context menus run. Keys of the German Outlook: Strg+N neue E-Mail,
/// Strg+R Antworten, Strg+Umschalt+R Allen antworten, Strg+F Weiterleiten (in the German Outlook Strg+F forwards; searching is Strg+E or F3), Entf
/// löschen (with "Rückgängig"), Rücktaste archivieren, Strg+Q gelesen, Strg+U ungelesen, Einfg zur Nachverfolgung, Strg+Umschalt+V verschieben, F9
/// senden/empfangen, Strg+P drucken, F12 speichern unter. The Mac keeps ⌘ for Strg and uses Outlook for Mac's keys where they differ (⌘J forward,
/// ⌘T/⇧⌘T read/unread, ⇧⌘M move, ⌘K send/receive, ⌥⌘F search).
/// </summary>
public static class MailCommands
{
    public static void Add(OfficeWindow window)
    {
        MailEditor Editor() => (MailEditor)window.Editor;
        bool Open() => window.Editor is MailEditor { Document.IsClosed: false };
        bool HasOne() => Open() && window.Editor is MailEditor { Current: not null };
        bool HasSome() => Open() && window.Editor is MailEditor editor && editor.SelectedIds.Count > 0;
        bool HasAccount() => Open() && window.Editor is MailEditor editor && editor.Document.Accounts.Accounts.Count > 0;
        bool HasFolder() => Open() && window.Editor is MailEditor { CurrentFolder: not null };
        bool HasOwnFolder() => Open() && window.Editor is MailEditor { CurrentFolder.Role: FolderRole.None };
        var commands = new OfficeCommandRegistry(); // gathered here, then registered on the window for the view "E-Mail" (see the end)

        commands.Add(new OfficeCommand(MailCommandIds.NewMail, "Neue E-Mail", () => NewMailAsync(window))
        {
            Icon = "mail",
            Shortcut = "Ctrl+N",
            Description = "Schreibt eine neue E-Mail in einem eigenen Fenster.",
            Keywords = ["neu", "schreiben", "verfassen", "nachricht", "mail"],
            CanExecute = HasAccount,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Reply, "Antworten", () => ReplyAsync(window, DraftOrigin.Reply))
        {
            Icon = "back",
            Shortcut = "Ctrl+R",
            Description = "Antwortet dem Absender der ausgewählten Nachricht.",
            Keywords = ["antwort", "reply", "zurückschreiben"],
            CanExecute = HasOne,
        });
        commands.Add(new OfficeCommand(MailCommandIds.ReplyAll, "Allen antworten", () => ReplyAsync(window, DraftOrigin.ReplyAll))
        {
            Icon = "users",
            Shortcut = "Ctrl+Shift+R",
            Description = "Antwortet dem Absender und allen anderen Empfängern.",
            Keywords = ["antwort", "alle", "reply all"],
            CanExecute = HasOne,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Forward, "Weiterleiten", () => ReplyAsync(window, DraftOrigin.Forward))
        {
            Icon = "arrow-right",
            Shortcut = "Ctrl+F",
            MacShortcut = "Ctrl+J",
            Description = "Leitet die ausgewählte Nachricht mit ihren Anlagen weiter.",
            Keywords = ["weiterleiten", "forward", "senden an"],
            CanExecute = HasOne,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Delete, "Löschen", () => DeleteAsync(window, Editor(), Editor().SelectedIds))
        {
            Icon = "trash",
            Shortcut = "Delete",
            MacShortcut = "Ctrl+Back",
            Description = "Verschiebt die ausgewählten Nachrichten nach „Gelöschte Elemente“ – „Rückgängig“ holt sie zurück.",
            Keywords = ["entfernen", "papierkorb", "weg"],
            CanExecute = HasSome,
            YieldsToTextField = true,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Archive, "Archivieren", () => Editor().Run(() =>
        {
            var editor = Editor();
            var ids = editor.SelectedIds;
            editor.Session.Archive(ids);
            window.ShowMessage(ids.Count == 1 ? "1 Element wurde archiviert." : $"{ids.Count} Elemente wurden archiviert.", InfoSeverity.Info, isClosable: true, "Rückgängig", () => window.Document.Undo());
        }))
        {
            Icon = "archive",
            Shortcut = "Back",
            Description = "Verschiebt die ausgewählten Nachrichten in den Ordner „Archiv“ des Kontos.",
            Keywords = ["archiv", "ablegen", "aufräumen"],
            CanExecute = HasSome,
            YieldsToTextField = true,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Move, "Verschieben", () => MoveAsync(window, Editor()))
        {
            Icon = "folder",
            Shortcut = "Ctrl+Shift+V",
            MacShortcut = "Ctrl+Shift+M",
            Description = "Verschiebt die ausgewählten Nachrichten in einen anderen Ordner (auch per Ziehen auf den Ordner).",
            Keywords = ["verschieben", "ordner", "ablegen"],
            CanExecute = HasSome,
        });
        commands.Add(new OfficeCommand(MailCommandIds.MarkRead, "Als gelesen markieren", () => Editor().Run(() => Editor().Session.SetRead(Editor().SelectedIds, true)))
        {
            Icon = "check",
            Shortcut = "Ctrl+Q",
            MacShortcut = "Ctrl+T",
            Description = "Markiert die ausgewählten Nachrichten als gelesen.",
            Keywords = ["gelesen", "markieren"],
            CanExecute = HasSome,
        });
        commands.Add(new OfficeCommand(MailCommandIds.MarkUnread, "Als ungelesen markieren", () => Editor().Run(() => Editor().Session.SetRead(Editor().SelectedIds, false)))
        {
            Icon = "mail",
            Shortcut = "Ctrl+U",
            MacShortcut = "Ctrl+Shift+T",
            Description = "Markiert die ausgewählten Nachrichten als ungelesen.",
            Keywords = ["ungelesen", "neu", "markieren"],
            CanExecute = HasSome,
        });
        commands.Add(new OfficeCommand(MailCommandIds.ToggleRead, "Gelesen/Ungelesen", () => Editor().Run(() =>
        {
            var editor = Editor();
            var headers = editor.Document.Store.Headers(editor.SelectedIds);
            editor.Session.SetRead(editor.SelectedIds, headers.Any(h => !h.IsRead));
        }))
        {
            Icon = "mail",
            Description = "Markiert die ausgewählten Nachrichten als gelesen oder ungelesen.",
            Keywords = ["gelesen", "ungelesen"],
            CanExecute = HasSome,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Flag, "Zur Nachverfolgung", () => Editor().Run(() =>
        {
            var editor = Editor();
            var headers = editor.Document.Store.Headers(editor.SelectedIds);
            editor.Session.SetFlagged(editor.SelectedIds, headers.Any(h => !h.IsFlagged));
        }))
        {
            Icon = "pin",
            Shortcut = "Insert",
            MacShortcut = "Ctrl+Shift+G",
            Description = "Kennzeichnet die ausgewählten Nachrichten zur Nachverfolgung – oder nimmt die Kennzeichnung weg.",
            Keywords = ["kennzeichnen", "flagge", "markieren", "wichtig", "nachverfolgung"],
            CanExecute = HasSome,
            IsChecked = () => window.Editor is MailEditor { Current.IsFlagged: true },
        });
        commands.Add(new OfficeCommand(MailCommandIds.Categorize, "Kategorisieren", () => CategorizeAsync(window, Editor()))
        {
            Icon = "tag",
            Description = "Gibt den ausgewählten Nachrichten eine Kategorie – oder nimmt sie wieder weg.",
            Keywords = ["kategorie", "farbe", "markieren", "schlagwort"],
            CanExecute = HasSome,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Search, "Suchen", () => Editor().FocusSearch())
        {
            Icon = "search",
            Shortcut = "Ctrl+E",
            MacShortcut = "Ctrl+Alt+F",
            Description = "Sucht in Betreff, Absender, Empfängern und Text – im Ordner oder in allen Ordnern.",
            Keywords = ["suchen", "finden", "filter"],
        });
        commands.Add(new OfficeCommand("mail.searchF3", "Suchen (F3)", () => Editor().FocusSearch()) { Icon = "search", Shortcut = "F3", IsSearchable = false });
        commands.Add(new OfficeCommand(MailCommandIds.Copy, "Kopieren", () => CopyAsync(window, Editor()))
        {
            Icon = "copy",
            Shortcut = "Ctrl+C",
            Description = "Kopiert den markierten Text der Nachricht.",
            YieldsToTextField = true,
            IsSearchable = false,
        });
        commands.Add(new OfficeCommand(MailCommandIds.SelectAll, "Alle auswählen", () => Editor().List.SelectAll())
        {
            Icon = "check-square",
            Shortcut = "Ctrl+A",
            YieldsToTextField = true,
            IsSearchable = false,
        });
        commands.Add(new OfficeCommand(MailCommandIds.OpenMessage, "Öffnen", () => OpenMessageAsync(window, Editor()))
        {
            Icon = "external",
            Shortcut = "Ctrl+O",
            Description = "Öffnet die ausgewählte Nachricht in einem eigenen Fenster (ein Entwurf öffnet sich zum Bearbeiten).",
            CanExecute = HasOne,
        });
        commands.Add(new OfficeCommand(MailCommandIds.SaveAs, "Speichern unter", () => SaveAsAsync(window, Editor()))
        {
            Icon = "save-as",
            Shortcut = "F12",
            MacShortcut = "Ctrl+Shift+S",
            Description = "Speichert die ausgewählte Nachricht als .eml-Datei.",
            Keywords = ["speichern", "eml", "datei", "exportieren"],
            CanExecute = HasOne,
        });
        commands.Add(new OfficeCommand(MailCommandIds.Print, "Drucken", () => window.ShowBackstage(MailPrintPage.PageId))
        {
            Icon = "print",
            Shortcut = "Ctrl+P",
            Description = "Druckt die ausgewählte Nachricht – in der Ansicht Kalender den Kalender (Tages-, Wochen- oder Monatsformat).",
            Keywords = ["drucken", "pdf", "papier", "kalender"],
            CanExecute = () => HasOne() || window.Editor is MailEditor { ActiveView: MailArea.Calendar, CalendarView: not null },
        });

        // Senden/Empfangen
        commands.Add(new OfficeCommand(MailCommandIds.SendReceive, "Alle Ordner senden/empfangen", () => Editor().Run(() => Editor().Document.SendReceiveAll()))
        {
            Icon = "refresh",
            Shortcut = "F9",
            MacShortcut = "Ctrl+K",
            Description = "Sendet den Postausgang und holt neue Nachrichten in allen Ordnern aller Konten.",
            Keywords = ["senden", "empfangen", "abrufen", "aktualisieren", "synchronisieren"],
            CanExecute = () => HasAccount() && !Editor().Document.IsWorkingOffline,
        });
        commands.Add(new OfficeCommand(MailCommandIds.WorkOffline, "Offline arbeiten", () => Editor().Document.SetWorkingOfflineAsync(!Editor().Document.IsWorkingOffline))
        {
            Icon = "power",
            Description = "Trennt alle Verbindungen. Was Sie tun und senden, wartet, bis Sie wieder online arbeiten.",
            Keywords = ["offline", "online", "verbindung", "trennen"],
            IsChecked = () => window.Editor is MailEditor { Document.IsWorkingOffline: true },
        });

        // Ordner
        commands.Add(new OfficeCommand(MailCommandIds.NewFolder, "Neuer Ordner", () => NewFolderAsync(window, Editor()))
        {
            Icon = "folder-open",
            Description = "Legt einen Ordner im gewählten Ordner (oder im Konto) an.",
            Keywords = ["ordner", "neu", "anlegen"],
            CanExecute = () => Open() && window.Editor is MailEditor { CurrentNode.AccountId: not null },
        });
        commands.Add(new OfficeCommand(MailCommandIds.RenameFolder, "Ordner umbenennen", () => RenameFolderAsync(window, Editor()))
        {
            Icon = "edit",
            Description = "Gibt dem gewählten Ordner einen neuen Namen.",
            Keywords = ["ordner", "umbenennen", "name"],
            CanExecute = HasOwnFolder,
        });
        commands.Add(new OfficeCommand(MailCommandIds.DeleteFolder, "Ordner löschen", () => DeleteFolderAsync(window, Editor()))
        {
            Icon = "trash",
            Description = "Löscht den gewählten Ordner mit seinen Nachrichten.",
            Keywords = ["ordner", "löschen", "entfernen"],
            CanExecute = HasOwnFolder,
        });
        commands.Add(new OfficeCommand(MailCommandIds.MarkAllRead, "Alle als gelesen markieren", () => Editor().Run(() => Editor().Session.MarkAllRead(Editor().CurrentFolder!.Id)))
        {
            Icon = "check-circle",
            Description = "Markiert alle Nachrichten des Ordners als gelesen.",
            Keywords = ["alle", "gelesen", "ordner"],
            CanExecute = HasFolder,
        });
        commands.Add(new OfficeCommand(MailCommandIds.EmptyFolder, "Ordner leeren", () => EmptyFolderAsync(window, Editor()))
        {
            Icon = "broom",
            Description = "Löscht alle Nachrichten des Ordners endgültig (etwa „Gelöschte Elemente“).",
            Keywords = ["leeren", "papierkorb", "endgültig"],
            CanExecute = HasFolder,
        });

        // Ansicht
        commands.Add(new OfficeCommand(MailCommandIds.PaneRight, "Rechts", () => Editor().Pane = ReadingPanePosition.Right)
        {
            Icon = "columns",
            Description = "Zeigt den Lesebereich rechts neben der Liste.",
            Keywords = ["lesebereich", "rechts", "vorschau"],
            IsChecked = () => window.Editor is MailEditor { Pane: ReadingPanePosition.Right },
        });
        commands.Add(new OfficeCommand(MailCommandIds.PaneBottom, "Unten", () => Editor().Pane = ReadingPanePosition.Bottom)
        {
            Icon = "layers",
            Description = "Zeigt den Lesebereich unter der Liste.",
            Keywords = ["lesebereich", "unten", "vorschau"],
            IsChecked = () => window.Editor is MailEditor { Pane: ReadingPanePosition.Bottom },
        });
        commands.Add(new OfficeCommand(MailCommandIds.PaneOff, "Aus", () => Editor().Pane = ReadingPanePosition.Off)
        {
            Icon = "eye-off",
            Description = "Blendet den Lesebereich aus (Nachrichten öffnen sich mit Doppelklick).",
            Keywords = ["lesebereich", "aus", "ausblenden"],
            IsChecked = () => window.Editor is MailEditor { Pane: ReadingPanePosition.Off },
        });
        commands.Add(new OfficeCommand(MailCommandIds.Conversations, "Als Unterhaltungen anzeigen", () => Editor().SetConversations(!Editor().ShowsConversations))
        {
            Icon = "message-square",
            Description = "Fasst Nachrichten mit demselben Gesprächsverlauf zu einer Zeile zusammen.",
            Keywords = ["unterhaltung", "konversation", "thread", "gruppieren"],
            IsChecked = () => window.Editor is MailEditor { ShowsConversations: true },
        });
        commands.Add(new OfficeCommand(MailCommandIds.DensityCompact, "Kompakt", () => Editor().SetCompact(true))
        {
            Icon = "list",
            Description = "Zeigt mehr Nachrichten auf einmal (ohne Vorschautext).",
            Keywords = ["dichte", "kompakt", "eng"],
            IsChecked = () => window.Editor is MailEditor { IsCompact: true },
        });
        commands.Add(new OfficeCommand(MailCommandIds.DensityComfortable, "Geräumig", () => Editor().SetCompact(false))
        {
            Icon = "list-checks",
            Description = "Zeigt zu jeder Nachricht die ersten Worte des Textes.",
            Keywords = ["dichte", "geräumig", "vorschau"],
            IsChecked = () => window.Editor is MailEditor { IsCompact: false },
        });
        commands.Add(new OfficeCommand(MailCommandIds.FolderPane, "Ordnerbereich", () => Editor().SetFolderPane(!Editor().ShowsFolderPane))
        {
            Icon = "sidebar",
            Description = "Blendet den Ordnerbereich ein oder aus.",
            Keywords = ["ordnerbereich", "navigation", "seitenleiste"],
            IsChecked = () => window.Editor is MailEditor { ShowsFolderPane: true },
        });
        commands.Add(new OfficeCommand(MailCommandIds.LoadImages, "Bilder laden", () => Editor().LoadImagesAsync(always: false))
        {
            Icon = "image",
            Description = "Lädt die blockierten Bilder dieser Nachricht aus dem Internet.",
            Keywords = ["bilder", "laden", "blockiert", "extern"],
            CanExecute = () => window.Editor is MailEditor { Reader.ShowsImagesBar: true },
        });
        commands.Add(new OfficeCommand(MailCommandIds.AddSenderToContacts, "Zu Kontakten hinzufügen", () => AddSenderAsync(window, Editor()))
        {
            Icon = "user",
            Description = "Legt den Absender der ausgewählten Nachricht als Kontakt an (Personen in der ThixCloud).",
            Keywords = ["kontakt", "absender", "adressbuch", "person", "speichern"],
            CanExecute = () => HasOne() && Editor().Document.Pim.IsAvailable,
        });
        commands.Add(new OfficeCommand(MailCommandIds.NewFromTemplate, "Neue E-Mail aus Vorlage", () => NewFromTemplateAsync(window, Editor()))
        {
            Icon = "template",
            Description = "Beginnt eine neue Nachricht mit Betreff und Text einer Vorlage (Vorlagen speichern Sie im Nachrichtenfenster).",
            Keywords = ["vorlage", "neu", "muster", "template"],
            CanExecute = () => HasAccount() && Editor().Document.Accounts.Templates.Count > 0,
        });
        commands.Add(new OfficeCommand(MailCommandIds.CreateRule, "Regel erstellen", () => CreateRuleAsync(window, Editor()))
        {
            Icon = "filter",
            Description = "Legt eine Regel nach dem Absender und dem Betreff der ausgewählten Nachricht an.",
            Keywords = ["regel", "erstellen", "filter", "automatisch", "sortieren"],
            CanExecute = HasOne,
        });
        commands.Add(new OfficeCommand(MailCommandIds.ManageRules, "Regeln verwalten", () => window.ShowSettings(MailSettings.RulesTab))
        {
            Icon = "list-checks",
            Description = "Zeigt die Regeln: anlegen, ändern, ordnen, ein- und ausschalten.",
            Keywords = ["regeln", "verwalten", "filter", "benachrichtigungen", "automatisch"],
            CanExecute = Open,
        });
        commands.Add(new OfficeCommand(MailCommandIds.RunRules, "Regeln jetzt anwenden", () => Editor().Run(() =>
        {
            // the chosen folder, or – in Alle Posteingänge / a search folder – every Posteingang
            var folders = Editor().CurrentFolder is { } current ? [current.Id]
                : Editor().Document.Accounts.Accounts.Select(a => Editor().Document.Store.FolderByRole(a.Id, FolderRole.Inbox)?.Id).OfType<long>().ToList();
            var changed = folders.Sum(id => Editor().Session.ApplyRulesNow(id));
            window.ShowMessage(changed == 0 ? "Keine Nachricht in diesem Ordner passt zu einer Regel." : changed == 1 ? "Eine Nachricht wurde nach den Regeln geändert."
                : $"{changed} Nachrichten wurden nach den Regeln geändert.", InfoSeverity.Success, isClosable: true);
        }))
        {
            Icon = "play",
            Description = "Wendet die eingeschalteten Regeln auf alle Nachrichten des aktuellen Ordners an (rückgängig zu machen).",
            Keywords = ["regeln", "anwenden", "ausführen", "jetzt"],
            CanExecute = () => HasAccount() && Editor().Document.Accounts.Rules.Any(r => r.IsEnabled),
        });
        commands.Add(new OfficeCommand(MailCommandIds.AddAccount, "Konto hinzufügen", () => window.ShowBackstage(AccountsPage.PageId))
        {
            Icon = "user",
            Description = "Richtet ein E-Mail-Konto ein (IMAP und SMTP; die Einstellungen werden meist selbst gefunden).",
            Keywords = ["konto", "einrichten", "imap", "smtp", "hinzufügen", "postfach"],
        });

        // The mail commands act while the view "E-Mail" is shown (Kalender and Personen have their own Strg+N, Entf, Strg+E …); accounts and offline
        // work everywhere, Drucken also in the calendar (it prints the calendar there).
        var everywhere = new HashSet<string>(StringComparer.Ordinal) { MailCommandIds.AddAccount, MailCommandIds.WorkOffline, MailCommandIds.Print };
        bool InMail() => window.Editor is MailEditor { ActiveView: MailArea.Mail };
        foreach (var command in commands.All)
        {
            var always = everywhere.Contains(command.Id);
            window.Commands.Add(new OfficeCommand(command.Id, command.Label, () => command.ExecuteAsync())
            {
                Icon = command.Icon,
                Shortcut = command.Shortcut,
                MacShortcut = command.MacShortcut,
                Description = command.Description,
                Keywords = command.Keywords,
                CanExecute = () => (always || InMail()) && command.IsEnabled,
                IsChecked = command.IsChecked,
                IsSearchable = command.IsSearchable,
                SearchPenalty = command.SearchPenalty,
                YieldsToTextField = command.YieldsToTextField,
            });
        }
    }

    /// <summary>"Zu Kontakten hinzufügen": the contact dialog with the sender's name and address (an existing contact of that address opens instead).</summary>
    private static async Task AddSenderAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Current is not { } header || editor.Document.Pim.People is not { } people)
        {
            return;
        }

        var address = header.From.Address;
        var existing = people.People.FirstOrDefault(p => p.Data.Emails.Any(e => e.Value.Equals(address, StringComparison.OrdinalIgnoreCase)));
        var dialog = new ThixOffice.Pim.Desktop.People.ContactDialog(people, existing);
        if (existing is null)
        {
            var name = header.From.Name.Trim();
            var comma = name.IndexOf(',');
            var space = name.LastIndexOf(' ');
            (dialog.GivenName.Text, dialog.FamilyName.Text) = comma > 0 ? (name[(comma + 1)..].Trim(), name[..comma].Trim()) // "Schulz, Maria"
                : space > 0 ? (name[..space], name[(space + 1)..]) : (name, "");
            dialog.Emails.Rows[0].Box.Text = address;
        }

        if (await dialog.ShowAsync(window))
        {
            window.ShowMessage(existing is null ? $"„{header.From.Display}“ ist jetzt in Ihren Kontakten." : "Der Kontakt wurde gespeichert.", InfoSeverity.Success, isClosable: true);
        }
    }

    /// <summary>A new message to <paramref name="addresses"/> (Personen › E-Mail senden), from the first account.</summary>
    public static Task ComposeToAddressesAsync(OfficeWindow window, IReadOnlyList<string> addresses)
    {
        var editor = (MailEditor)window.Editor;
        var account = editor.Document.Accounts.Accounts.FirstOrDefault();
        if (account is null)
        {
            window.ShowBackstage(AccountsPage.PageId);
            return Task.CompletedTask;
        }

        var to = addresses.Select(address => new MailAddress("", address)).ToList();
        OpenComposer(window, WithSignature(editor.Document, new Draft { AccountId = account.Id, From = account.Sender, To = to }, account, reply: false));
        return Task.CompletedTask;
    }

    // ===== composing =====

    /// <summary>Opens a compose window for <paramref name="draft"/> (owned by the main window).</summary>
    public static ComposeWindow OpenComposer(OfficeWindow window, Draft draft)
    {
        var editor = (MailEditor)window.Editor;
        var compose = new ComposeWindow(editor.Document, draft, OfficeApplication.Instance.Logo);
        compose.Show(window);
        return compose;
    }

    private static Task NewMailAsync(OfficeWindow window)
    {
        var editor = (MailEditor)window.Editor;
        var account = editor.CurrentFolder is { } folder ? editor.Document.Accounts.Account(folder.AccountId) : null;
        account ??= editor.Document.Accounts.Accounts.FirstOrDefault();
        if (account is null)
        {
            window.ShowBackstage(AccountsPage.PageId);
            return Task.CompletedTask;
        }

        OpenComposer(window, WithSignature(editor.Document, new Draft { AccountId = account.Id, From = account.Sender }, account, reply: false));
        return Task.CompletedTask;
    }

    /// <summary>A mailto: link of a message: a new message to its address (and subject).</summary>
    public static Task ComposeToAsync(OfficeWindow window, string mailto)
    {
        var editor = (MailEditor)window.Editor;
        var account = editor.Document.Accounts.Accounts.FirstOrDefault();
        if (account is null)
        {
            return Task.CompletedTask;
        }

        var rest = mailto[7..];
        var query = rest.IndexOf('?');
        var to = Uri.UnescapeDataString(query < 0 ? rest : rest[..query]);
        var subject = query < 0 ? "" : rest[(query + 1)..].Split('&').Select(p => p.Split('=', 2)).Where(p => p.Length == 2 && p[0].Equals("subject", StringComparison.OrdinalIgnoreCase))
            .Select(p => Uri.UnescapeDataString(p[1])).FirstOrDefault() ?? "";
        OpenComposer(window, WithSignature(editor.Document, new Draft { AccountId = account.Id, From = account.Sender, To = [new MailAddress("", to)], Subject = subject }, account, reply: false));
        return Task.CompletedTask;
    }

    /// <summary>The account's signature (new or reply) into the draft: below an empty line, above a quote.</summary>
    internal static Draft WithSignature(MailDocument document, Draft draft, MailAccount account, bool reply)
    {
        var signature = document.Accounts.Signature(reply ? account.ReplySignatureId : account.NewSignatureId);
        var block = signature is null ? "" : $"<div><br></div><div>{signature.Html}</div>";
        return draft with { Html = "<div><br></div>" + block + draft.Html };
    }

    private static async Task ReplyAsync(OfficeWindow window, DraftOrigin origin)
    {
        var editor = (MailEditor)window.Editor;
        if (editor.Current is not { } header || editor.Document.Accounts.Account(header.AccountId) is not { } account)
        {
            return;
        }

        var raw = await editor.Document.BodyAsync(header.Id, CancellationToken.None);
        var mail = raw is null
            ? new ReadMail { Subject = header.Subject, From = header.From, To = header.To, Cc = header.Cc, ReplyTo = header.ReplyTo, Date = header.Date, MessageId = header.MessageId, References = header.References, Html = "", Text = header.Preview }
            : MailReader.Read(raw);
        var draft = origin == DraftOrigin.Forward ? MailComposer.Forward(mail, account, header.Id) : MailComposer.Reply(mail, account, origin == DraftOrigin.ReplyAll, header.Id);
        OpenComposer(window, WithSignature(editor.Document, draft, account, reply: true));
    }

    private static async Task OpenMessageAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Current is not { } header)
        {
            return;
        }

        var raw = await editor.Document.BodyAsync(header.Id, CancellationToken.None);
        if (raw is null)
        {
            window.ShowMessage("Der Text dieser Nachricht ist nicht auf diesem Computer und der Mailserver ist nicht erreichbar.", InfoSeverity.Warning, isClosable: true);
            return;
        }

        var folder = editor.Document.Store.Folder(header.FolderId);
        if (folder?.Role == FolderRole.Drafts || header.Flags.HasFlag(MessageFlags.Draft))
        {
            var message = MailMime.Parse(raw);
            OpenComposer(window, MailComposer.FromMessage(MailReader.Read(message), message, header.AccountId) with { SavedDraft = header.Id });
            return;
        }

        new MessageWindow(window, MailReader.Read(raw), header).Show(window);
    }

    // ===== actions =====

    /// <summary>Entf: into Gelöschte Elemente with "Rückgängig" in the message bar; for good only after asking.</summary>
    public static async Task DeleteAsync(OfficeWindow window, MailEditor editor, IReadOnlyList<long> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }

        if (editor.Session.DeletesForGood(ids)
            && !await QuestionDialog.ConfirmAsync(window, "Endgültig löschen?",
                ids.Count == 1 ? "Die Nachricht wird endgültig gelöscht und lässt sich nicht wiederherstellen." : $"{ids.Count} Nachrichten werden endgültig gelöscht und lassen sich nicht wiederherstellen.",
                "Endgültig löschen", isDestructive: true))
        {
            return;
        }

        var forGood = 0;
        editor.Run(() => forGood = editor.Session.Delete(ids));
        var moved = ids.Count - forGood;
        if (moved > 0)
        {
            window.ShowMessage(moved == 1 ? "1 Element wurde gelöscht." : $"{moved} Elemente wurden gelöscht.", InfoSeverity.Info, isClosable: true, "Rückgängig", () => window.Document.Undo());
        }
    }

    private static async Task MoveAsync(OfficeWindow window, MailEditor editor)
    {
        var ids = editor.SelectedIds;
        var header = editor.Document.Store.Header(ids.FirstOrDefault());
        if (header is null)
        {
            return;
        }

        // the account's own folders first, then those of the other accounts (a move there copies and deletes)
        var accounts = editor.Document.Accounts.Accounts.OrderBy(a => a.Id == header.AccountId ? 0 : 1).ThenBy(a => a.Order).ToList();
        var folders = accounts.SelectMany(a => editor.Document.Store.Folders(a.Id).Where(f => f.IsSelectable && f.Id != header.FolderId)
            .OrderBy(f => f.RoleOrder).ThenBy(f => f.Path, StringComparer.CurrentCultureIgnoreCase)).ToList();
        var dialog = new MoveDialog(folders, accounts.Count > 1 ? accounts.ToDictionary(a => a.Id, a => a.DisplayName) : null);
        if (await dialog.ShowAsync(window) && dialog.Chosen is { } target)
        {
            await editor.MoveAsync(ids, target.Id);
        }
    }

    private static async Task NewFromTemplateAsync(OfficeWindow window, MailEditor editor)
    {
        var dialog = new TemplateDialog(editor.Document);
        if (!await dialog.ShowAsync(window) || dialog.Chosen is not { } template)
        {
            return;
        }

        var account = editor.CurrentFolder is { } folder ? editor.Document.Accounts.Account(folder.AccountId) : null;
        account ??= editor.Document.Accounts.Accounts[0];
        OpenComposer(window, new Draft { AccountId = account.Id, From = account.Sender, Subject = template.Subject, Html = template.Html, Text = template.Text });
    }

    /// <summary>"Regel erstellen": the rule dialog with the sender and the subject of the message (Outlook's "Regel erstellen …").</summary>
    private static async Task CreateRuleAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Current is not { } message)
        {
            return;
        }

        var rules = editor.Document.Accounts.Rules;
        var template = new MailRule
        {
            Name = $"Von {message.From.Display}",
            AccountId = message.AccountId,
            FromContains = message.From.Address,
            Order = rules.Count == 0 ? 0 : rules.Max(r => r.Order) + 1,
        };
        var dialog = new RuleDialog(editor.Document, template, isNew: true);
        if (await dialog.ShowAsync(window) && dialog.Saved is { } saved)
        {
            window.ShowMessage($"Die Regel „{saved.Name}“ gilt ab jetzt für neue Nachrichten.", InfoSeverity.Success, isClosable: true, "Jetzt anwenden",
                () => _ = window.Commands.ExecuteAsync(MailCommandIds.RunRules));
        }
    }

    private static async Task CategorizeAsync(OfficeWindow window, MailEditor editor)
    {
        var known = editor.Document.Accounts.Categories.Select(c => c.Name)
            .Union(editor.Document.Store.Headers(editor.SelectedIds).SelectMany(h => h.Categories), StringComparer.OrdinalIgnoreCase).ToList();
        var hint = known.Count > 0 ? $"Vorhanden: {string.Join(", ", known)}" : "z. B. Projekt, Rechnung, Privat";
        var name = await TextInputDialog.AskAsync(window, "Kategorisieren", $"Kategorie ({hint})", known.FirstOrDefault() ?? "", "Zuweisen/Entfernen");
        if (string.IsNullOrWhiteSpace(name))
        {
            return;
        }

        editor.Run(() =>
        {
            editor.Session.ToggleCategory(editor.SelectedIds, name.Trim());
            if (!editor.Document.Accounts.Categories.Any(c => c.Name.Equals(name.Trim(), StringComparison.OrdinalIgnoreCase)))
            {
                editor.Document.Accounts.Save(new MailCategory(name.Trim(), editor.Document.Accounts.Categories.Count));
            }
        });
    }

    private static async Task CopyAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Reader.SelectedText() is { Length: > 0 } text && window.Clipboard is { } clipboard)
        {
            await clipboard.SetTextAsync(text);
        }
    }

    private static async Task SaveAsAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.Current is not { } header)
        {
            return;
        }

        var raw = await editor.Document.BodyAsync(header.Id, CancellationToken.None);
        if (raw is null)
        {
            window.ShowMessage("Der Text dieser Nachricht ist nicht auf diesem Computer und der Mailserver ist nicht erreichbar.", InfoSeverity.Warning, isClosable: true);
            return;
        }

        var target = await OfficeApplication.FileDialogs.PickExportTargetAsync(window, EmlFiles.SuggestedName(header.Subject), SettingsService.SaveFolder, "E-Mail-Nachricht", ".eml");
        if (target is null)
        {
            return;
        }

        try
        {
            EmlFiles.Save(target, raw);
            window.ShowMessage($"Die Nachricht wurde als „{Path.GetFileName(target)}“ gespeichert.", InfoSeverity.Success, isClosable: true);
        }
        catch (BusinessException exception)
        {
            window.ShowMessage(exception.Message, InfoSeverity.Error, isClosable: true);
        }
    }

    private static async Task NewFolderAsync(OfficeWindow window, MailEditor editor)
    {
        var node = editor.CurrentNode;
        if (node?.AccountId is not { } accountId)
        {
            return;
        }

        string? error = null;
        while (true)
        {
            var name = await TextInputDialog.AskAsync(window, "Neuer Ordner", "Name des Ordners", "", "Anlegen", error);
            if (string.IsNullOrWhiteSpace(name))
            {
                return;
            }

            try
            {
                var id = editor.Session.CreateFolder(accountId, node.FolderId, name);
                editor.SelectFolder(id);
                return;
            }
            catch (BusinessException exception)
            {
                error = exception.Message;
            }
        }
    }

    private static async Task RenameFolderAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.CurrentFolder is not { } folder)
        {
            return;
        }

        string? error = null;
        while (true)
        {
            var name = await TextInputDialog.AskAsync(window, "Ordner umbenennen", "Neuer Name", folder.Name, "Umbenennen", error);
            if (string.IsNullOrWhiteSpace(name) || name == folder.Name)
            {
                return;
            }

            try
            {
                editor.Session.RenameFolder(folder.Id, name);
                return;
            }
            catch (BusinessException exception)
            {
                error = exception.Message;
            }
        }
    }

    private static async Task DeleteFolderAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.CurrentFolder is not { } folder
            || !await QuestionDialog.ConfirmAsync(window, "Ordner löschen?", $"„{folder.DisplayName}“ wird mit allen Nachrichten und Unterordnern gelöscht – auch auf dem Mailserver.", "Ordner löschen", isDestructive: true))
        {
            return;
        }

        editor.Run(() => editor.Session.DeleteFolder(folder.Id));
        editor.SelectFolder(null);
    }

    private static async Task EmptyFolderAsync(OfficeWindow window, MailEditor editor)
    {
        if (editor.CurrentFolder is not { } folder
            || !await QuestionDialog.ConfirmAsync(window, "Ordner leeren?", $"Alle Nachrichten in „{folder.DisplayName}“ werden endgültig gelöscht.", "Ordner leeren", isDestructive: true))
        {
            return;
        }

        editor.Run(() => editor.Session.EmptyFolder(folder.Id));
    }
}

/// <summary>"Verschieben": the folders of the account, one is chosen.</summary>
public sealed class MoveDialog : OfficeDialog
{
    private readonly IReadOnlyList<MailFolder> _folders;
    private readonly Avalonia.Controls.ListBox _list;

    public MoveDialog(IReadOnlyList<MailFolder> folders, IReadOnlyDictionary<string, string>? accountNames = null)
        : base("Elemente verschieben", 420)
    {
        _folders = folders;
        string Label(MailFolder f) => (f.Path.Count(c => c == f.Delimiter) > 0 ? $"  {f.DisplayName}  ({f.Path})" : f.DisplayName)
                                      + (accountNames?.GetValueOrDefault(f.AccountId) is { } account ? $" – {account}" : "");
        _list = new Avalonia.Controls.ListBox { ItemsSource = folders.Select(Label).ToList(), MaxHeight = 320, SelectedIndex = folders.Count > 0 ? 0 : -1 };
        Avalonia.Automation.AutomationProperties.SetName(_list, "Zielordner");
        _list.DoubleTapped += (_, _) => Confirm();
        SetBody(Field("In den Ordner", _list));
    }

    public MailFolder? Chosen { get; private set; }

    public void Choose(string displayName) => _list.SelectedIndex = _folders.ToList().FindIndex(f => f.DisplayName == displayName);

    protected override bool Apply()
    {
        Chosen = _list.SelectedIndex >= 0 ? _folders[_list.SelectedIndex] : null;
        return Chosen is not null;
    }
}
