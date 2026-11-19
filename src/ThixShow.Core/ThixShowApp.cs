using ThixOffice.Core.Models;

namespace ThixShow.Core;

/// <summary>
/// ThixShow as data: the presentation program of ThixOffice (PowerPoint's place), accent: PowerPoint's orange. Its own format is
/// .pptx (the default type); .pptm keeps its macros (they never run) and stays .pptm. .odp is PLANNED – never registered with the
/// system, never offered – until its reader exists.
/// </summary>
public static class ThixShowApp
{
    public const string Accent = "#C43E1C";

    public static OfficeAppDefinition Definition { get; } = new(
        "thixshow",
        "ThixShow",
        Accent,
        "Präsentationen für Vorträge, Besprechungen und alles, was Sie zeigen.",
        [
            new DocumentType(".pptx", "ThixShow.Presentation", "PowerPoint-Präsentation", DocumentTypeSupport.Supported, IsDefault: true,
                "application/vnd.openxmlformats-officedocument.presentationml.presentation", "org.openxmlformats.presentationml.presentation"),
            new DocumentType(".pptm", "ThixShow.MacroPresentation", "PowerPoint-Präsentation mit Makros", DocumentTypeSupport.Supported,
                MimeType: "application/vnd.ms-powerpoint.presentation.macroEnabled.12", MacTypeIdentifier: "com.microsoft.powerpoint.openxml.presentation.macroenabled"),
            new DocumentType(".odp", "ThixShow.OpenDocumentPresentation", "OpenDocument-Präsentation", DocumentTypeSupport.Planned,
                MimeType: "application/vnd.oasis.opendocument.presentation", MacTypeIdentifier: "org.oasis-open.opendocument.presentation"),
        ])
    {
        UntitledBaseName = "Präsentation",
        BlankDocumentLabel = "Leere Präsentation",
        CloudAppId = MatCloud.Contract.CloudApps.Show, // presentations in ThixDrive (the Drive grants its routes to "show")
    };
}
