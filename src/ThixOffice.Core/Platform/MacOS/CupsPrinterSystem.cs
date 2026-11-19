using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Platform.MacOS;

/// <summary>
/// Printing on macOS through CUPS, the printing system of macOS (libcups, source-generated P/Invoke: no process, no output of
/// a command line tool to parse in the user's language). The printers are CUPS's destinations (the ones of System Settings ›
/// Drucker); a job is the chosen pages as a PDF (drawn by the app exactly as exported) handed to <c>cupsPrintFile</c> with
/// the copies (collated) and the paper of the first page as options. CUPS reads the file before it returns; the temporary
/// PDF is deleted right after.
/// </summary>
[SupportedOSPlatform("macos")]
internal sealed unsafe partial class CupsPrinterSystem : IPrinterSystem
{
    private const string Library = "/usr/lib/libcups.2.dylib";

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        CupsDestination* destinations = null;
        var count = cupsGetDests(&destinations);
        try
        {
            var printers = new List<PrinterInfo>();
            for (var index = 0; index < count; index++)
            {
                var destination = destinations[index];
                if (destination.Instance != IntPtr.Zero)
                {
                    continue; // an instance (lpoptions) of a printer listed anyway
                }

                if (Marshal.PtrToStringUTF8(destination.Name) is { Length: > 0 } name)
                {
                    printers.Add(new PrinterInfo(name, destination.IsDefault != 0));
                }
            }

            return printers;
        }
        finally
        {
            if (destinations is not null)
            {
                cupsFreeDests(count, destinations);
            }
        }
    }

    public void Print(PrintJob job)
    {
        var file = job.OutputFile ?? Path.Combine(Path.GetTempPath(), $"thixoffice-print-{Guid.NewGuid():N}.pdf");
        using (var stream = File.Create(file))
        {
            job.Source.WritePdf(ExpandCopies(job), stream);
        }

        if (job.OutputFile is not null)
        {
            return; // printing to a file: the PDF is the result
        }

        CupsOption* options = null;
        var optionCount = 0;
        try
        {
            var (width, height) = job.Source.SizeOf(job.Pages[0]);
            optionCount = cupsAddOption("media", PaperSize.CupsMedia(width, height), optionCount, &options);
            var id = cupsPrintFile(job.PrinterName, file, job.Title, optionCount, options);
            if (id == 0)
            {
                throw new PrintFailedException($"Der Drucker „{job.PrinterName}“ hat den Auftrag abgelehnt: {Marshal.PtrToStringUTF8(cupsLastErrorString())}");
            }
        }
        finally
        {
            if (options is not null)
            {
                cupsFreeOptions(optionCount, options);
            }

            File.Delete(file);
        }
    }

    /// <summary>
    /// The pages of every copy in order (collated): the copies are in the PDF itself, so that every printer (and "Als PDF
    /// sichern") does the same.
    /// </summary>
    internal static IReadOnlyList<int> ExpandCopies(PrintJob job) => [.. Enumerable.Range(0, job.Copies).SelectMany(_ => job.Pages)];

    [StructLayout(LayoutKind.Sequential)]
    private struct CupsDestination
    {
        public IntPtr Name;
        public IntPtr Instance;
        public int IsDefault;
        public int OptionCount;
        public IntPtr Options;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct CupsOption
    {
        public IntPtr Name;
        public IntPtr Value;
    }

#pragma warning disable SA1300, IDE1006 // the C names of libcups
    [LibraryImport(Library)]
    private static partial int cupsGetDests(CupsDestination** destinations);

    [LibraryImport(Library)]
    private static partial void cupsFreeDests(int count, CupsDestination* destinations);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int cupsAddOption(string name, string value, int count, CupsOption** options);

    [LibraryImport(Library)]
    private static partial void cupsFreeOptions(int count, CupsOption* options);

    [LibraryImport(Library, StringMarshalling = StringMarshalling.Utf8)]
    private static partial int cupsPrintFile(string printer, string file, string title, int optionCount, CupsOption* options);

    [LibraryImport(Library)]
    private static partial IntPtr cupsLastErrorString();
#pragma warning restore SA1300, IDE1006
}
