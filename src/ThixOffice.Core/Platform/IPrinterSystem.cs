using ThixOffice.Core.Models;

namespace ThixOffice.Core.Platform;

/// <summary>
/// The system's printing, behind <see cref="Business.PrintService"/> (which checks the policy and the input first):
/// Windows prints through GDI (<see cref="Windows.GdiPrinterSystem"/>), macOS through CUPS
/// (<see cref="MacOS.CupsPrinterSystem"/>). Tests put a fake in its place.
/// </summary>
public interface IPrinterSystem
{
    /// <summary>The printers of the system (empty when there are none or the system cannot print).</summary>
    IReadOnlyList<PrinterInfo> GetPrinters();

    /// <summary>Sends the job to the printer; throws <see cref="PrintFailedException"/> when the system refuses it.</summary>
    void Print(PrintJob job);
}

/// <summary>The system refused a print job (the message names the reason of the system).</summary>
public sealed class PrintFailedException(string message) : Exception(message);

/// <summary>A system without printing support (neither Windows nor macOS).</summary>
internal sealed class NoPrinterSystem : IPrinterSystem
{
    public IReadOnlyList<PrinterInfo> GetPrinters() => [];

    public void Print(PrintJob job) => throw new PrintFailedException("Dieses System bietet kein Drucken an.");
}
