using ThixOffice.Core.Models;

namespace ThixWrite.Core;

/// <summary>
/// ThixWrite as data: the word processor of ThixOffice (Word's place), accent: Word's blue. Its own format is .docx (the
/// default type: "Speichern unter" offers it first, the install flow registers it with the document icon); .txt is read and
/// written as plain text. .odt and .rtf are PLANNED – never registered with the system, never offered – until their readers
/// exist. ThixWrite never takes .docx or .txt from another program silently: it offers itself under "Öffnen mit".
/// </summary>
public static class ThixWriteApp
{
    public const string Accent = "#185ABD";

    public static OfficeAppDefinition Definition { get; } = new(
        "thixwrite",
        "ThixWrite",
        Accent,
        "Textverarbeitung für Briefe, Berichte und alles, was Sie schreiben.",
        [
            new DocumentType(".docx", "ThixWrite.Document", "Word-Dokument", DocumentTypeSupport.Supported, IsDefault: true,
                "application/vnd.openxmlformats-officedocument.wordprocessingml.document", "org.openxmlformats.wordprocessingml.document"),
            new DocumentType(".odt", "ThixWrite.OpenDocumentText", "OpenDocument-Text", DocumentTypeSupport.Planned,
                MimeType: "application/vnd.oasis.opendocument.text", MacTypeIdentifier: "org.oasis-open.opendocument.text"),
            new DocumentType(".rtf", "ThixWrite.RichText", "Rich-Text-Dokument", DocumentTypeSupport.Planned,
                MimeType: "application/rtf", MacTypeIdentifier: "public.rtf"),
            new DocumentType(".txt", "ThixWrite.Text", "Textdokument", DocumentTypeSupport.Supported,
                MimeType: "text/plain", MacTypeIdentifier: "public.plain-text"),
        ])
    {
        UntitledBaseName = "Dokument",
        BlankDocumentLabel = "Leeres Dokument",
    };
}
