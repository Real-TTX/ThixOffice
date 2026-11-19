using System.Globalization;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using ThixOffice.Core.Platform.MacOS;
using ThixOffice.Core.Platform.Windows;

namespace ThixOffice.Core.Business;

/// <summary>
/// "Drucken": the printers of the system, which pages a choice means, and the job itself – after the policy check
/// (<see cref="AccessPolicy.DisablePrinting"/>) and the validation of the input. The system's part is an
/// <see cref="IPrinterSystem"/> (GDI on Windows, CUPS on macOS). Fields of the errors: <see cref="PrinterField"/>,
/// <see cref="CopiesField"/>, <see cref="PagesField"/>.
/// </summary>
public static class PrintService
{
    public const string PrinterField = "printer";
    public const string CopiesField = "copies";
    public const string PagesField = "pages";
    public const int MaxCopies = 999;

    private static IPrinterSystem? _system;

    /// <summary>The system's printing (tests put a fake here; null: the platform's again).</summary>
    internal static IPrinterSystem System
    {
        get => _system ??= CreatePlatformSystem();
        set => _system = value!;
    }

    /// <summary>True when an administrator turned printing off.</summary>
    public static bool IsDisabled => AccessPolicy.IsPrintingDisabled;

    /// <summary>The printers, the default one first; empty when printing is off or the system has none.</summary>
    public static IReadOnlyList<PrinterInfo> GetPrinters()
    {
        if (IsDisabled)
        {
            return [];
        }

        try
        {
            return [.. System.GetPrinters().OrderByDescending(printer => printer.IsDefault).ThenBy(printer => printer.Name, StringComparer.CurrentCultureIgnoreCase)];
        }
        catch (Exception exception) when (exception is PrintFailedException or DllNotFoundException or EntryPointNotFoundException)
        {
            Log.Warning($"The printers could not be read: {exception.Message}");
            return [];
        }
    }

    /// <summary>
    /// The pages (0-based, in the order given) of a choice: all, the current one, or the text of "Seiten" – numbers and ranges
    /// separated by commas or semicolons ("1-3, 5, 8–10"; a range may run backwards; pages beyond the end are an error).
    /// </summary>
    public static IReadOnlyList<int> PagesOf(PrintRange range, string? custom, int currentPage, int pageCount)
    {
        if (pageCount <= 0)
        {
            throw new BusinessException("Das Dokument hat keine Seiten.", PagesField);
        }

        return range switch
        {
            PrintRange.All => [.. Enumerable.Range(0, pageCount)],
            PrintRange.Current => [Math.Clamp(currentPage, 0, pageCount - 1)],
            _ => ParsePages(custom, pageCount),
        };
    }

    /// <summary>Checks the job and hands it to the system; a refusal of the system becomes a <see cref="BusinessException"/>.</summary>
    public static void Print(PrintJob job)
    {
        ArgumentNullException.ThrowIfNull(job);
        AccessPolicy.EnsurePrintingAllowed();
        if (string.IsNullOrWhiteSpace(job.PrinterName))
        {
            throw new BusinessException("Bitte wählen Sie einen Drucker.", PrinterField);
        }

        if (job.Copies is < 1 or > MaxCopies)
        {
            throw new BusinessException($"Die Anzahl der Exemplare muss zwischen 1 und {MaxCopies} liegen.", CopiesField);
        }

        if (job.Pages.Count == 0 || job.Pages.Any(page => page < 0 || page >= job.Source.Count))
        {
            throw new BusinessException("Diese Seiten gibt es im Dokument nicht.", PagesField);
        }

        try
        {
            System.Print(job);
            Log.Info($"Printed {job.Pages.Count} page(s) × {job.Copies}");
        }
        catch (Exception exception) when (exception is PrintFailedException or DllNotFoundException or EntryPointNotFoundException or IOException or UnauthorizedAccessException)
        {
            Log.Warning($"Printing failed: {exception.Message}");
            throw new BusinessException(exception is PrintFailedException ? exception.Message : $"Der Druck ist fehlgeschlagen: {exception.Message}", PrinterField);
        }
    }

    private static List<int> ParsePages(string? text, int pageCount)
    {
        var pages = new List<int>();
        var parts = (text ?? "").Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (parts.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie die Seiten an, z. B. 1-3, 5.", PagesField);
        }

        foreach (var part in parts)
        {
            var bounds = part.Split(['-', '–'], StringSplitOptions.TrimEntries);
            if (bounds.Length > 2 || !TryPage(bounds[0], out var first) || !TryPage(bounds[^1], out var last))
            {
                throw new BusinessException($"„{part}“ ist keine Seitenangabe. Beispiel: 1-3, 5.", PagesField);
            }

            if (Math.Max(first, last) > pageCount)
            {
                throw new BusinessException($"Das Dokument hat nur {pageCount} {(pageCount == 1 ? "Seite" : "Seiten")}.", PagesField);
            }

            var step = last >= first ? 1 : -1;
            for (var page = first; page != last + step; page += step)
            {
                pages.Add(page - 1);
            }
        }

        return pages;
    }

    private static bool TryPage(string text, out int page) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out page) && page >= 1;

    private static IPrinterSystem CreatePlatformSystem()
    {
        if (OperatingSystem.IsWindows())
        {
            return new GdiPrinterSystem();
        }

        return OperatingSystem.IsMacOS() ? new CupsPrinterSystem() : new NoPrinterSystem();
    }
}
