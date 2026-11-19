namespace ThixOffice.Core.Models;

/// <summary>A printer of the system; <see cref="IsDefault"/> is the one the system prints to by default.</summary>
public sealed record PrinterInfo(string Name, bool IsDefault);

/// <summary>Which pages "Drucken" prints (Word's choice: Alle Seiten, Aktuelle Seite, Benutzerdefinierter Druck).</summary>
public enum PrintRange
{
    All,
    Current,
    Custom,
}

/// <summary>
/// A paper size by its name, in points (portrait), with the names the systems use for it: the Windows DEVMODE paper code
/// (DMPAPER_*) and the CUPS media name.
/// </summary>
public sealed record PaperSize(string Id, string Label, double Width, double Height, short WindowsCode, string CupsName)
{
    /// <summary>The paper sizes of the apps (Layout › Format) and of printing.</summary>
    public static IReadOnlyList<PaperSize> All { get; } =
    [
        new("a4", "A4 (21 × 29,7 cm)", 595.3, 841.9, 9, "A4"),
        new("a5", "A5 (14,8 × 21 cm)", 419.55, 595.3, 11, "A5"),
        new("a3", "A3 (29,7 × 42 cm)", 841.9, 1190.55, 8, "A3"),
        new("letter", "Letter (21,59 × 27,94 cm)", 612, 792, 1, "Letter"),
        new("legal", "Legal (21,59 × 35,56 cm)", 612, 1008, 5, "Legal"),
    ];

    /// <summary>The named size of a page of <paramref name="width"/> × <paramref name="height"/> points in either orientation, or null.</summary>
    public static PaperSize? Find(double width, double height)
    {
        var (shorter, longer) = (Math.Min(width, height), Math.Max(width, height));
        return All.FirstOrDefault(size => Math.Abs(size.Width - shorter) < 2 && Math.Abs(size.Height - longer) < 2);
    }

    /// <summary>The CUPS media name of a page: A4, Letter …, or a size of its own in points ("Custom.500x700").</summary>
    public static string CupsMedia(double width, double height)
    {
        if (Find(width, height) is { } paper)
        {
            return paper.CupsName;
        }

        var (shorter, longer) = (Math.Min(width, height), Math.Max(width, height));
        return string.Create(System.Globalization.CultureInfo.InvariantCulture, $"Custom.{shorter:0.##}x{longer:0.##}");
    }

    /// <summary>"A4 (21 × 29,7 cm)" or "Benutzerdefiniert (12 × 18 cm)" for a page of that size.</summary>
    public static string Describe(double width, double height)
    {
        if (Find(width, height) is { } size)
        {
            return size.Label;
        }

        var culture = System.Globalization.CultureInfo.GetCultureInfo("de-DE");
        static double Centimetres(double points) => Math.Round(points / 72 * 2.54, 2);
        return $"Benutzerdefiniert ({Centimetres(Math.Min(width, height)).ToString(culture)} × {Centimetres(Math.Max(width, height)).ToString(culture)} cm)";
    }
}

/// <summary>A band of a page as pixels: <see cref="Width"/> × <see cref="Height"/>, 32-bit BGRA (premultiplied, opaque), rows top-down.</summary>
public sealed record PrintRaster(int Width, int Height, byte[] Pixels);

/// <summary>
/// The pages of a document as a printer needs them – given by the app (ThixWrite: the laid-out pages drawn with Skia); the
/// core knows only sizes, pixels and PDF bytes. Implementations are called from a worker thread and must not touch the UI.
/// </summary>
public interface IPrintablePages
{
    /// <summary>Number of pages.</summary>
    int Count { get; }

    /// <summary>The size of a page in points (wider than tall: landscape).</summary>
    (double Width, double Height) SizeOf(int index);

    /// <summary>
    /// Draws rows <paramref name="top"/> … <paramref name="top"/> + <paramref name="height"/> of page <paramref name="index"/>
    /// at <paramref name="dotsPerInch"/> on white (the width is the whole page).
    /// </summary>
    PrintRaster Render(int index, double dotsPerInch, int top, int height);

    /// <summary>Writes the pages <paramref name="indexes"/> (in that order) as a PDF.</summary>
    void WritePdf(IReadOnlyList<int> indexes, Stream output);
}

/// <summary>
/// What to print: <paramref name="Pages"/> (0-based, in order) of <paramref name="Source"/> on <paramref name="PrinterName"/>,
/// <paramref name="Copies"/> times (collated). <paramref name="OutputFile"/>: print to a file instead of the device (the
/// self test with "Microsoft Print to PDF").
/// </summary>
public sealed record PrintJob(string Title, string PrinterName, int Copies, IReadOnlyList<int> Pages, IPrintablePages Source, string? OutputFile = null);
