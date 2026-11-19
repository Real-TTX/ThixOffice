using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Platform.Windows;

/// <summary>
/// Printing on Windows through GDI (winspool + gdi32, source-generated P/Invoke: no System.Drawing, no WPF/XPS, nothing that
/// needs the Windows target framework or reflection – it works in the trimmed single-file build). Every page is drawn by the
/// app at the printer's resolution (at most 600 dpi) in bands of <see cref="BandHeight"/> rows and handed to the printer with
/// StretchDIBits, placed on the sheet so that the document's margins land where the layout put them (the printer's
/// unprintable edge is subtracted). Paper and orientation come from each page (A4/Letter … by their DEVMODE codes, other
/// sizes as a custom size); the copies are made here, collated, so that every driver does the same.
/// </summary>
[SupportedOSPlatform("windows")]
internal sealed unsafe partial class GdiPrinterSystem : IPrinterSystem
{
    /// <summary>Rows per band handed to the printer (keeps a 600 dpi A4 page at ~20 MB per band instead of 140 MB).</summary>
    internal const int BandHeight = 1024;

    private const double MaxDotsPerInch = 600;
    private const uint PrinterEnumLocal = 0x2;
    private const uint PrinterEnumConnections = 0x4;
    private const int DmOutBuffer = 2;
    private const int LogPixelsX = 88;
    private const int LogPixelsY = 90;
    private const int PhysicalOffsetX = 112;
    private const int PhysicalOffsetY = 113;
    private const uint SourceCopy = 0x00CC0020;

    // DEVMODEW: the offsets of the fields set here (dmDeviceName is 32 WCHARs).
    private const int DevModeSize = 68;
    private const int DevModeFields = 72;
    private const int DevModeOrientation = 76;
    private const int DevModePaperSize = 78;
    private const int DevModePaperLength = 80;
    private const int DevModePaperWidth = 82;
    private const int DevModeCopies = 86;
    private const int FieldOrientation = 0x1;
    private const int FieldPaperSize = 0x2;
    private const int FieldPaperLength = 0x4;
    private const int FieldPaperWidth = 0x8;
    private const int FieldCopies = 0x100;

    public IReadOnlyList<PrinterInfo> GetPrinters()
    {
        const uint flags = PrinterEnumLocal | PrinterEnumConnections;
        EnumPrinters(flags, null, 4, IntPtr.Zero, 0, out var needed, out _);
        if (needed == 0)
        {
            return [];
        }

        var buffer = Marshal.AllocHGlobal((int)needed);
        try
        {
            if (!EnumPrinters(flags, null, 4, buffer, needed, out _, out var count))
            {
                throw new PrintFailedException($"Die Drucker konnten nicht gelesen werden (Fehler {Marshal.GetLastPInvokeError()}).");
            }

            var defaultName = DefaultPrinterName();
            var printers = new List<PrinterInfo>();
            var entries = (PrinterInfo4*)buffer;
            for (var index = 0; index < count; index++)
            {
                var name = Marshal.PtrToStringUni(entries[index].Name);
                if (!string.IsNullOrEmpty(name))
                {
                    printers.Add(new PrinterInfo(name, string.Equals(name, defaultName, StringComparison.OrdinalIgnoreCase)));
                }
            }

            return printers;
        }
        finally
        {
            Marshal.FreeHGlobal(buffer);
        }
    }

    public void Print(PrintJob job)
    {
        var devMode = DefaultDevMode(job.PrinterName);
        var title = Marshal.StringToHGlobalUni(job.Title);
        var output = job.OutputFile is null ? IntPtr.Zero : Marshal.StringToHGlobalUni(job.OutputFile);
        var dc = IntPtr.Zero;
        try
        {
            var (firstWidth, firstHeight) = job.Source.SizeOf(job.Pages[0]);
            ApplyPage(devMode, firstWidth, firstHeight);
            dc = CreateDC(null, job.PrinterName, null, devMode);
            if (dc == IntPtr.Zero)
            {
                throw new PrintFailedException($"Der Drucker „{job.PrinterName}“ ist nicht bereit.");
            }

            var info = new DocInfo { Size = sizeof(DocInfo), DocumentName = title, Output = output };
            if (StartDoc(dc, in info) <= 0)
            {
                throw new PrintFailedException($"Der Drucker „{job.PrinterName}“ hat den Auftrag abgelehnt (Fehler {Marshal.GetLastPInvokeError()}).");
            }

            try
            {
                PrintPages(dc, devMode, job);
                if (EndDoc(dc) <= 0)
                {
                    throw new PrintFailedException($"Der Auftrag an „{job.PrinterName}“ konnte nicht abgeschlossen werden.");
                }
            }
            catch
            {
                AbortDoc(dc);
                throw;
            }
        }
        finally
        {
            if (dc != IntPtr.Zero)
            {
                DeleteDC(dc);
            }

            Marshal.FreeHGlobal(devMode);
            Marshal.FreeHGlobal(title);
            if (output != IntPtr.Zero)
            {
                Marshal.FreeHGlobal(output);
            }
        }
    }

    private static void PrintPages(IntPtr dc, IntPtr devMode, PrintJob job)
    {
        for (var copy = 0; copy < job.Copies; copy++)
        {
            foreach (var index in job.Pages)
            {
                var (width, height) = job.Source.SizeOf(index);
                if (ApplyPage(devMode, width, height))
                {
                    ResetDC(dc, devMode); // another paper or orientation from this page on (a section in landscape)
                }

                if (StartPage(dc) <= 0)
                {
                    throw new PrintFailedException("Der Drucker hat eine Seite abgelehnt.");
                }

                DrawPage(dc, job.Source, index, width, height);
                if (EndPage(dc) <= 0)
                {
                    throw new PrintFailedException("Der Drucker hat eine Seite abgelehnt.");
                }
            }
        }
    }

    /// <summary>Draws one page band by band at the printer's resolution (at most 600 dpi; above, GDI stretches).</summary>
    private static void DrawPage(IntPtr dc, IPrintablePages source, int index, double width, double height)
    {
        double deviceX = GetDeviceCaps(dc, LogPixelsX);
        double deviceY = GetDeviceCaps(dc, LogPixelsY);
        var offsetX = GetDeviceCaps(dc, PhysicalOffsetX);
        var offsetY = GetDeviceCaps(dc, PhysicalOffsetY);
        var dpi = Math.Min(Math.Max(deviceX, deviceY), MaxDotsPerInch);
        var pixelHeight = (int)Math.Ceiling(height / 72 * dpi);
        var targetWidth = (int)Math.Round(width / 72 * deviceX);
        for (var top = 0; top < pixelHeight; top += BandHeight)
        {
            var band = source.Render(index, dpi, top, Math.Min(BandHeight, pixelHeight - top));
            var targetTop = (int)Math.Round(top * deviceY / dpi);
            var targetBottom = (int)Math.Round((top + band.Height) * deviceY / dpi);
            var header = new BitmapInfoHeader
            {
                Size = sizeof(BitmapInfoHeader),
                Width = band.Width,
                Height = -band.Height, // rows top-down
                Planes = 1,
                BitCount = 32,
            };
            fixed (byte* pixels = band.Pixels)
            {
                StretchDIBits(dc, -offsetX, targetTop - offsetY, targetWidth, targetBottom - targetTop, 0, 0, band.Width, band.Height, pixels, &header, 0, SourceCopy);
            }
        }
    }

    /// <summary>Sets paper and orientation of a page into the DEVMODE; true when they changed.</summary>
    private static bool ApplyPage(IntPtr devMode, double width, double height)
    {
        var before = Read(devMode);
        var fields = Marshal.ReadInt32(devMode, DevModeFields) | FieldOrientation | FieldCopies;
        Marshal.WriteInt16(devMode, DevModeOrientation, (short)(width > height ? 2 : 1));
        Marshal.WriteInt16(devMode, DevModeCopies, 1);
        if (PaperSize.Find(width, height) is { } paper)
        {
            fields = (fields | FieldPaperSize) & ~(FieldPaperLength | FieldPaperWidth);
            Marshal.WriteInt16(devMode, DevModePaperSize, paper.WindowsCode);
        }
        else
        {
            // A size of its own, in tenths of a millimetre, portrait.
            fields = (fields | FieldPaperLength | FieldPaperWidth) & ~FieldPaperSize;
            Marshal.WriteInt16(devMode, DevModePaperSize, 0);
            Marshal.WriteInt16(devMode, DevModePaperLength, TenthsOfMillimetre(Math.Max(width, height)));
            Marshal.WriteInt16(devMode, DevModePaperWidth, TenthsOfMillimetre(Math.Min(width, height)));
        }

        Marshal.WriteInt32(devMode, DevModeFields, fields);
        return Read(devMode) != before;

        static (int, short, short, short, short) Read(IntPtr mode) => (
            Marshal.ReadInt32(mode, DevModeFields),
            Marshal.ReadInt16(mode, DevModeOrientation),
            Marshal.ReadInt16(mode, DevModePaperSize),
            Marshal.ReadInt16(mode, DevModePaperLength),
            Marshal.ReadInt16(mode, DevModePaperWidth));
    }

    private static short TenthsOfMillimetre(double points) => (short)Math.Round(points / 72 * 254);

    /// <summary>The printer's default DEVMODE in unmanaged memory (the caller frees it).</summary>
    private static IntPtr DefaultDevMode(string printer)
    {
        if (!OpenPrinter(printer, out var handle, IntPtr.Zero))
        {
            throw new PrintFailedException($"Der Drucker „{printer}“ wurde nicht gefunden.");
        }

        try
        {
            var size = DocumentProperties(IntPtr.Zero, handle, printer, IntPtr.Zero, IntPtr.Zero, 0);
            if (size <= 0)
            {
                throw new PrintFailedException($"Die Einstellungen des Druckers „{printer}“ konnten nicht gelesen werden.");
            }

            var devMode = Marshal.AllocHGlobal(size);
            if (DocumentProperties(IntPtr.Zero, handle, printer, devMode, IntPtr.Zero, DmOutBuffer) < 0 || Marshal.ReadInt16(devMode, DevModeSize) < DevModeCopies + 2)
            {
                Marshal.FreeHGlobal(devMode);
                throw new PrintFailedException($"Die Einstellungen des Druckers „{printer}“ konnten nicht gelesen werden.");
            }

            return devMode;
        }
        finally
        {
            ClosePrinter(handle);
        }
    }

    private static string? DefaultPrinterName()
    {
        uint size = 0;
        GetDefaultPrinter(null, ref size);
        if (size == 0)
        {
            return null;
        }

        var buffer = new char[size];
        fixed (char* chars = buffer)
        {
            return GetDefaultPrinter(chars, ref size) ? new string(chars) : null;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PrinterInfo4
    {
        public IntPtr Name;
        public IntPtr Server;
        public uint Attributes;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DocInfo
    {
        public int Size;
        public IntPtr DocumentName;
        public IntPtr Output;
        public IntPtr DataType;
        public uint Type;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct BitmapInfoHeader
    {
        public int Size;
        public int Width;
        public int Height;
        public short Planes;
        public short BitCount;
        public int Compression;
        public int SizeImage;
        public int XPelsPerMeter;
        public int YPelsPerMeter;
        public int ClrUsed;
        public int ClrImportant;
    }

    [LibraryImport("winspool.drv", EntryPoint = "EnumPrintersW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool EnumPrinters(uint flags, string? name, uint level, IntPtr buffer, uint size, out uint needed, out uint returned);

    [LibraryImport("winspool.drv", EntryPoint = "GetDefaultPrinterW", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetDefaultPrinter(char* buffer, ref uint size);

    [LibraryImport("winspool.drv", EntryPoint = "OpenPrinterW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool OpenPrinter(string name, out IntPtr handle, IntPtr defaults);

    [LibraryImport("winspool.drv", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClosePrinter(IntPtr handle);

    [LibraryImport("winspool.drv", EntryPoint = "DocumentPropertiesW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial int DocumentProperties(IntPtr window, IntPtr printer, string name, IntPtr output, IntPtr input, int mode);

    [LibraryImport("gdi32.dll", EntryPoint = "CreateDCW", SetLastError = true, StringMarshalling = StringMarshalling.Utf16)]
    private static partial IntPtr CreateDC(string? driver, string device, string? output, IntPtr devMode);

    [LibraryImport("gdi32.dll", EntryPoint = "ResetDCW")]
    private static partial IntPtr ResetDC(IntPtr dc, IntPtr devMode);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool DeleteDC(IntPtr dc);

    [LibraryImport("gdi32.dll", EntryPoint = "StartDocW", SetLastError = true)]
    private static partial int StartDoc(IntPtr dc, in DocInfo info);

    [LibraryImport("gdi32.dll")]
    private static partial int EndDoc(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int AbortDoc(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int StartPage(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int EndPage(IntPtr dc);

    [LibraryImport("gdi32.dll")]
    private static partial int GetDeviceCaps(IntPtr dc, int index);

    [LibraryImport("gdi32.dll")]
    private static partial int StretchDIBits(IntPtr dc, int x, int y, int width, int height, int sourceX, int sourceY, int sourceWidth, int sourceHeight, void* bits, BitmapInfoHeader* info, uint usage, uint operation);
}
