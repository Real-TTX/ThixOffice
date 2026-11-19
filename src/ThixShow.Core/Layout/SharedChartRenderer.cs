using System.Collections.Concurrent;
using System.IO.Compression;
using System.Runtime.CompilerServices;
using ThixOffice.Charts;
using ThixOffice.Charts.Formats;
using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;

namespace ThixShow.Core.Layout;

/// <summary>
/// The chart seam filled by the shared chart component (ThixOffice.Charts): a chart frame's part is read from the presentation's
/// kept package (<see cref="ChartXml.Read"/>, once per package and part), resolved from the file's cached values
/// (<see cref="ChartDataResolver.Resolve"/> without a data source) and drawn by <see cref="ChartRenderer.Render"/> in the slide
/// theme's accents and body font. The part itself stays untouched, so an unchanged chart is written back byte for byte. Without a
/// readable part (a new or pasted frame) it returns null and the slide shows the placeholder.
/// </summary>
public sealed class SharedChartRenderer : IChartRenderer
{
    private static readonly ConditionalWeakTable<PreservedPackage, ConcurrentDictionary<string, Chart?>> Charts = new();
    private static readonly string[] AccentNames = ["accent1", "accent2", "accent3", "accent4", "accent5", "accent6"];

    public static SharedChartRenderer Instance { get; } = new();

    public IReadOnlyList<DisplayItem>? Render(GraphicFrame frame, double width, double height, SlideContext context, ITextMeasurer measurer)
    {
        if (ChartOf(frame, context.Presentation) is not { } chart)
        {
            return null;
        }

        var data = ChartDataResolver.Resolve(chart, null);
        return ChartRenderer.Render(data, new ChartRenderOptions
        {
            Measurer = measurer,
            Accents = AccentNames.Select(name => ColorResolver.Resolve(ColorSpec.Scheme(name), context.Colors)).ToList(),
            FontFamily = context.Theme.Fonts.Minor.Latin,
            Width = width,
            Height = height,
        });
    }

    /// <summary>The chart a frame shows: its own model, else the one of its part in the kept package (null when there is none).</summary>
    public static Chart? ChartOf(GraphicFrame frame, Presentation presentation) =>
        frame.Chart ?? (frame.ChartPartUri is { } uri && presentation.Package is { } package ? ChartOf(package, uri) : null);

    /// <summary>The chart of a part of the package (null when the part is missing or cannot be read).</summary>
    internal static Chart? ChartOf(PreservedPackage package, string partUri) =>
        Charts.GetOrCreateValue(package).GetOrAdd(partUri, uri => Read(package, uri));

    private static Chart? Read(PreservedPackage package, string partUri)
    {
        try
        {
            using var stream = package.Open();
            using var zip = new ZipArchive(stream, ZipArchiveMode.Read);
            var entry = zip.GetEntry(Uri.UnescapeDataString(partUri.TrimStart('/')));
            if (entry is null)
            {
                return null;
            }

            using var reader = new StreamReader(entry.Open());
            return ChartXml.Read(reader.ReadToEnd());
        }
        catch (Exception exception) when (exception is InvalidDataException or System.Xml.XmlException or IOException)
        {
            return null;
        }
    }
}
