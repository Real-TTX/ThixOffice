using ThixCalc.Core.Formats.Csv;
using ThixCalc.Core.Formats.Ods;
using ThixCalc.Core.Formats.Xlsx;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Formats;

/// <summary>The file formats of ThixCalc.</summary>
public enum WorkbookFileFormat
{
    Xlsx,
    Xlsm,
    Csv,
    Ods,
}

/// <summary>
/// A file format: reads a stream into a workbook and writes one: xlsx/xlsm, csv and ods (OpenDocument Spreadsheet).
/// </summary>
public interface IWorkbookFormat
{
    WorkbookFileFormat Format { get; }

    Workbook Read(Stream stream);

    void Write(Workbook workbook, Stream stream);
}

/// <summary>The formats by file type, and sniffing of what a stream holds.</summary>
public static class WorkbookFormats
{
    public static IWorkbookFormat For(WorkbookFileFormat format, CsvOptions? csv = null) => format switch
    {
        WorkbookFileFormat.Csv => new CsvWorkbookFormat(csv ?? new CsvOptions()),
        WorkbookFileFormat.Xlsm => new XlsxWorkbookFormat(macros: true),
        WorkbookFileFormat.Ods => new OdsWorkbookFormat(),
        _ => new XlsxWorkbookFormat(macros: false),
    };

    /// <summary>The format of a file extension, or null for formats ThixCalc does not write.</summary>
    public static WorkbookFileFormat? FromExtension(string extension) => extension.ToLowerInvariant() switch
    {
        ".xlsx" => WorkbookFileFormat.Xlsx,
        ".xlsm" => WorkbookFileFormat.Xlsm,
        ".csv" or ".tsv" or ".txt" => WorkbookFileFormat.Csv,
        ".ods" => WorkbookFileFormat.Ods,
        _ => null,
    };

    /// <summary>A zip package (xlsx) or text (csv)?</summary>
    public static bool IsZip(ReadOnlySpan<byte> bytes) => bytes.Length >= 4 && bytes[0] == 'P' && bytes[1] == 'K' && bytes[2] == 3 && bytes[3] == 4;

    private sealed class XlsxWorkbookFormat(bool macros) : IWorkbookFormat
    {
        public WorkbookFileFormat Format => macros ? WorkbookFileFormat.Xlsm : WorkbookFileFormat.Xlsx;

        public Workbook Read(Stream stream) => XlsxReader.Read(stream);

        public void Write(Workbook workbook, Stream stream) => XlsxWriter.Write(workbook, stream, keepMacros: macros);
    }

    private sealed class OdsWorkbookFormat : IWorkbookFormat
    {
        public WorkbookFileFormat Format => WorkbookFileFormat.Ods;

        public Workbook Read(Stream stream) => OdsReader.Read(stream);

        public void Write(Workbook workbook, Stream stream) => OdsWriter.Write(workbook, stream);
    }

    private sealed class CsvWorkbookFormat(CsvOptions options) : IWorkbookFormat
    {
        public WorkbookFileFormat Format => WorkbookFileFormat.Csv;

        public Workbook Read(Stream stream) => CsvFormat.Read(stream, options);

        public void Write(Workbook workbook, Stream stream) => CsvFormat.Write(workbook.ActiveSheet ?? workbook.Sheets[0], stream, options);
    }
}
