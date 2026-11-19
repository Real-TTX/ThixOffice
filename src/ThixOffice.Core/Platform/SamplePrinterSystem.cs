using ThixOffice.Core.Models;

namespace ThixOffice.Core.Platform;

/// <summary>
/// Printers that do not exist: the snapshots show them (no printer of the machine appears in a picture) and the tests print
/// to them (<see cref="Jobs"/> keeps what was sent).
/// </summary>
internal sealed class SamplePrinterSystem : IPrinterSystem
{
    public List<PrinterInfo> Printers { get; } = [new("Büro – Laserdrucker", true), new("Empfang – Farbdrucker", false)];

    public List<PrintJob> Jobs { get; } = [];

    /// <summary>Set to make the next job fail like a printer that is offline.</summary>
    public string? Failure { get; set; }

    public IReadOnlyList<PrinterInfo> GetPrinters() => Printers;

    public void Print(PrintJob job)
    {
        if (Failure is { } failure)
        {
            throw new PrintFailedException(failure);
        }

        Jobs.Add(job);
    }
}
