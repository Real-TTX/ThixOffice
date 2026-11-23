using ThixOffice.Core.Models;

namespace ThixMail.Core;

/// <summary>
/// ThixMail as data: the mail of ThixOffice (Outlook's place), accent: blue. A workspace app – one main window over the mail accounts of this computer,
/// no documents –; a saved message (.eml) is the type it opens: a double click on one shows it in a window of its own (read-only, "Antworten" and
/// "Weiterleiten" work from there). Accounts are IMAP/SMTP; the messages live in the offline store of this computer and on the mail server, NEVER in
/// ThixCloud. Signed in to ThixCloud as the app <c>mail</c>, it reads the contacts of the account for address suggestions (later also settings without
/// passwords, signatures and categories as collections of the general sync).
/// </summary>
public static class ThixMailApp
{
    public const string Accent = "#0F6CBD";

    /// <summary>The file of the message store in the data folder.</summary>
    public const string StoreFileName = "mail.db";

    /// <summary>The stem of the settings store (accounts without passwords, signatures, categories): mailsettings-local.db.</summary>
    public const string SettingsStoreName = "mailsettings";

    public static OfficeAppDefinition Definition { get; } = new(
        "thixmail",
        "ThixMail",
        Accent,
        "E-Mail mit IMAP und SMTP – offline lesen, schreiben und ordnen.",
        [
            new DocumentType(".eml", "ThixMail.Message", "E-Mail-Nachricht", DocumentTypeSupport.Supported, IsDefault: false,
                MimeType: "message/rfc822", MacTypeIdentifier: "com.apple.mail.email"),
        ])
    {
        Kind = OfficeAppKind.Workspace,
        ShowsNotifications = true,
        UntitledBaseName = "E-Mail",
        CloudAppId = MatCloud.Contract.CloudApps.Mail,
        CloudPurpose = "Ihren Kalender, Ihre Kontakte und Ihre E-Mail-Einstellungen auf allen Geräten abzugleichen – mit ThixCalendar und ThixContacts",
        CloudIntro = "Mit ThixCloud gleicht ThixMail Ihren Kalender, Ihre Kontakte und Ihre E-Mail-Einstellungen (Konten ohne Kennwörter, Signaturen, Regeln, " +
                     "Vorlagen, Kategorien) zwischen Ihren Computern ab – dieselben Termine wie ThixCalendar, dieselben Kontakte wie ThixContacts. Ohne Anmeldung " +
                     "arbeitet ThixMail nur auf diesem Computer; Ihre E-Mails liegen nie in der ThixCloud.",
        CloudSignedInText = "Angemeldet. Kalender, Kontakte und Einstellungen werden jetzt abgeglichen – was bisher nur auf diesem Computer war, kommt dazu.",
    };
}
