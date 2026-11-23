using ThixOffice.Core.Models;

namespace ThixContacts.Core;

/// <summary>
/// ThixContacts as data: the contacts of ThixOffice (the place of Outlook's "Personen"), accent: violet. A workspace app – one main window over
/// the contacts of the local store, no documents –; vCard files (.vcf) are its import type: a double click on one imports its contacts. It works
/// without ThixCloud (only on this computer) and syncs its contacts as the app <c>contacts</c> while signed in.
/// </summary>
public static class ThixContactsApp
{
    public const string Accent = "#8764B8";

    /// <summary>The stem of the store files: contacts-local.db, contacts-&lt;account&gt;.db.</summary>
    public const string StoreName = "contacts";

    public static OfficeAppDefinition Definition { get; } = new(
        "thixcontacts",
        "ThixContacts",
        Accent,
        "Kontakte und Kontaktgruppen – auf allen Geräten gleich.",
        [
            new DocumentType(".vcf", "ThixContacts.VCard", "vCard-Datei", DocumentTypeSupport.Supported, IsDefault: false,
                MimeType: "text/vcard", MacTypeIdentifier: "public.vcard"),
        ])
    {
        Kind = OfficeAppKind.Workspace,
        UntitledBaseName = "Kontakte",
        CloudAppId = MatCloud.Contract.CloudApps.Contacts, // the contacts in the general sync of the account
        CloudPurpose = "Ihre Kontakte auf allen Geräten abzugleichen – mit ThixMail und im Browser",
        CloudIntro = "Mit ThixCloud gleicht ThixContacts Ihre Kontakte und Kontaktgruppen zwischen Ihren Computern ab – dieselben wie in ThixMail und im Browser. " +
                     "Ohne Anmeldung sind die Kontakte nur auf diesem Computer (ThixMail zeigt dieselben).",
        CloudSignedInText = "Angemeldet. Ihre Kontakte werden jetzt abgeglichen – die Kontakte dieses Computers kommen dazu.",
    };
}