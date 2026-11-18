using ThixOffice.Core.Models;

namespace ThixWrite.Core;

/// <summary>
/// ThixWrite as data: the word processor of ThixOffice (Word's place), accent: Word's blue. Its own format is .docx; until
/// the docx reader exists (phase 1a) .docx, .odt and .rtf are declared as PLANNED – never registered with the system, never
/// offered – so that a real Word document is never opened into the placeholder editor and overwritten as plain text.
/// Phase 0 works on .txt, which ThixWrite offers under "Öffnen mit" only (the default program of .txt stays the user's).
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
            new DocumentType(".docx", "ThixWrite.Document", "Word-Dokument", DocumentTypeSupport.Planned, IsDefault: true,
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
