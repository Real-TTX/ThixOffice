using ThixOffice.Core.Models;

namespace ThixCalc.Core;

/// <summary>
/// ThixCalc as data: the spreadsheet of ThixOffice (Excel's place), accent: Excel's green. Its own format is .xlsx (the
/// default type); .xlsm keeps its macros (they never run) and stays .xlsm; .csv is read and written as text. .ods is PLANNED –
/// never registered with the system, never offered – until its reader exists.
/// </summary>
public static class ThixCalcApp
{
    public const string Accent = "#107C41";

    public static OfficeAppDefinition Definition { get; } = new(
        "thixcalc",
        "ThixCalc",
        Accent,
        "Tabellenkalkulation für Zahlen, Listen und Auswertungen.",
        [
            new DocumentType(".xlsx", "ThixCalc.Workbook", "Excel-Arbeitsmappe", DocumentTypeSupport.Supported, IsDefault: true,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "org.openxmlformats.spreadsheetml.sheet"),
            new DocumentType(".xlsm", "ThixCalc.MacroWorkbook", "Excel-Arbeitsmappe mit Makros", DocumentTypeSupport.Supported,
                MimeType: "application/vnd.ms-excel.sheet.macroEnabled.12", MacTypeIdentifier: "com.microsoft.excel.sheet.macroenabled"),
            new DocumentType(".csv", "ThixCalc.Csv", "CSV-Datei (Trennzeichen-getrennt)", DocumentTypeSupport.Supported,
                MimeType: "text/csv", MacTypeIdentifier: "public.comma-separated-values-text"),
            new DocumentType(".ods", "ThixCalc.OpenDocumentSpreadsheet", "OpenDocument-Tabelle", DocumentTypeSupport.Planned,
                MimeType: "application/vnd.oasis.opendocument.spreadsheet", MacTypeIdentifier: "org.oasis-open.opendocument.spreadsheet"),
        ])
    {
        UntitledBaseName = "Mappe",
        BlankDocumentLabel = "Leere Arbeitsmappe",
        CloudAppId = MatCloud.Contract.CloudApps.Calc, // workbooks in ThixDrive (the Drive grants its routes to "calc")
    };
}
