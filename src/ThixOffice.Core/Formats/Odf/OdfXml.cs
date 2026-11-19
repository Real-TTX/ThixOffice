using System.Globalization;
using System.Xml.Linq;

namespace ThixOffice.Core.Formats.Odf;

/// <summary>
/// What every OpenDocument format of the suite shares (odt, ods, odp): the namespaces, lengths ("2.5cm" ⇄ points), percentages,
/// colours ("#RRGGBB"), ISO durations and the attributes of a font (<see cref="OdfFont"/>). UI-free, BCL only.
/// </summary>
public static class OdfXml
{
    public static readonly XNamespace OfficeNs = "urn:oasis:names:tc:opendocument:xmlns:office:1.0";
    public static readonly XNamespace StyleNs = "urn:oasis:names:tc:opendocument:xmlns:style:1.0";
    public static readonly XNamespace TextNs = "urn:oasis:names:tc:opendocument:xmlns:text:1.0";
    public static readonly XNamespace TableNs = "urn:oasis:names:tc:opendocument:xmlns:table:1.0";
    public static readonly XNamespace DrawNs = "urn:oasis:names:tc:opendocument:xmlns:drawing:1.0";
    public static readonly XNamespace FoNs = "urn:oasis:names:tc:opendocument:xmlns:xsl-fo-compatible:1.0";
    public static readonly XNamespace XLinkNs = "http://www.w3.org/1999/xlink";
    public static readonly XNamespace DcNs = "http://purl.org/dc/elements/1.1/";
    public static readonly XNamespace MetaNs = "urn:oasis:names:tc:opendocument:xmlns:meta:1.0";
    public static readonly XNamespace SvgNs = "urn:oasis:names:tc:opendocument:xmlns:svg-compatible:1.0";
    public static readonly XNamespace ManifestNs = "urn:oasis:names:tc:opendocument:xmlns:manifest:1.0";
    public static readonly XNamespace NumberNs = "urn:oasis:names:tc:opendocument:xmlns:datastyle:1.0";
    public static readonly XNamespace ChartNs = "urn:oasis:names:tc:opendocument:xmlns:chart:1.0";
    public static readonly XNamespace PresentationNs = "urn:oasis:names:tc:opendocument:xmlns:presentation:1.0";
    public static readonly XNamespace ConfigNs = "urn:oasis:names:tc:opendocument:xmlns:config:1.0";
    public static readonly XNamespace SmilNs = "urn:oasis:names:tc:opendocument:xmlns:smil-compatible:1.0";
    public static readonly XNamespace AnimNs = "urn:oasis:names:tc:opendocument:xmlns:animation:1.0";
    public static readonly XNamespace OfNs = "urn:oasis:names:tc:opendocument:xmlns:of:1.2";

    /// <summary>LibreOffice's extensions (tab colours, conditional formats, value types of errors, extended layouts).</summary>
    public static readonly XNamespace TableOooNs = "http://openoffice.org/2009/table";
    public static readonly XNamespace CalcExtNs = "urn:org:documentfoundation:names:experimental:calc:xmlns:calcext:1.0";
    public static readonly XNamespace LoExtNs = "urn:org:documentfoundation:names:experimental:office:xmlns:loext:1.0";
    public static readonly XNamespace OooNs = "http://openoffice.org/2004/office";
    public static readonly XNamespace DrawOooNs = "http://openoffice.org/2010/draw";

    /// <summary>The namespace declarations of a root element (office, style, text, table, draw, fo, xlink, dc, meta, svg).</summary>
    public static IEnumerable<XAttribute> Declarations() =>
    [
        new(XNamespace.Xmlns + "office", OfficeNs),
        new(XNamespace.Xmlns + "style", StyleNs),
        new(XNamespace.Xmlns + "text", TextNs),
        new(XNamespace.Xmlns + "table", TableNs),
        new(XNamespace.Xmlns + "draw", DrawNs),
        new(XNamespace.Xmlns + "fo", FoNs),
        new(XNamespace.Xmlns + "xlink", XLinkNs),
        new(XNamespace.Xmlns + "dc", DcNs),
        new(XNamespace.Xmlns + "meta", MetaNs),
        new(XNamespace.Xmlns + "svg", SvgNs),
    ];

    /// <summary>The common declarations plus the ones a spreadsheet or presentation needs (number, chart, presentation, …).</summary>
    public static IEnumerable<XAttribute> AllDeclarations() => Declarations().Concat(
    [
        new(XNamespace.Xmlns + "number", NumberNs),
        new(XNamespace.Xmlns + "presentation", PresentationNs),
        new(XNamespace.Xmlns + "smil", SmilNs),
        new(XNamespace.Xmlns + "anim", AnimNs),
        new(XNamespace.Xmlns + "of", OfNs),
        new(XNamespace.Xmlns + "config", ConfigNs),
        new(XNamespace.Xmlns + "tableooo", TableOooNs),
        new(XNamespace.Xmlns + "calcext", CalcExtNs),
        new(XNamespace.Xmlns + "loext", LoExtNs),
    ]);

    // ---------------------------------------------------------------- lengths

    /// <summary>"12pt", "2.5cm", "10mm", "1in", "0.5pc", "96px" in points; null for anything else.</summary>
    public static double? Points(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        var unitStart = value.Length;
        while (unitStart > 0 && char.IsLetter(value[unitStart - 1]))
        {
            unitStart--;
        }

        if (!double.TryParse(value[..unitStart], NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
        {
            return null;
        }

        return value[unitStart..].ToLowerInvariant() switch
        {
            "pt" or "" => number,
            "cm" => number * 72 / 2.54,
            "mm" => number * 72 / 25.4,
            "in" or "inch" => number * 72,
            "pc" => number * 12,
            "px" => number * 0.75,
            _ => null,
        };
    }

    /// <summary>A length in points as ODF writes it ("12.5pt").</summary>
    public static string Length(double points) => Math.Round(points, 3).ToString("0.###", CultureInfo.InvariantCulture) + "pt";

    /// <summary>A length in centimetres ("2.501cm") – the page and the columns, as LibreOffice writes them.</summary>
    public static string Centimetres(double points) => Math.Round(points * 2.54 / 72, 4).ToString("0.####", CultureInfo.InvariantCulture) + "cm";

    /// <summary>A length in inches ("0.75in"), exact for margins kept in inches.</summary>
    public static string Inches(double inches) => Math.Round(inches, 5).ToString("0.#####", CultureInfo.InvariantCulture) + "in";

    public static string Percent(double value) => Math.Round(value * 100, 2).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>"50%" as 0.5, or null.</summary>
    public static double? PercentOf(string? value) =>
        value is not null && value.Trim().EndsWith('%') && double.TryParse(value.Trim().TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
            ? percent / 100
            : null;

    /// <summary>A number as ODF writes it (invariant, shortest round-trip form).</summary>
    public static string Number(double value) => value.ToString("R", CultureInfo.InvariantCulture);

    public static double? NumberOf(string? value) =>
        double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    /// <summary>"true"/"false".</summary>
    public static string Boolean(bool value) => value ? "true" : "false";

    /// <summary>An angle as ODF gives it ("45", "45deg", "0.785rad", "50grad") in degrees, or null.</summary>
    public static double? Degrees(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        value = value.Trim();
        var (text, factor) = value.EndsWith("deg", StringComparison.OrdinalIgnoreCase) ? (value[..^3], 1.0)
            : value.EndsWith("grad", StringComparison.OrdinalIgnoreCase) ? (value[..^4], 0.9)
            : value.EndsWith("rad", StringComparison.OrdinalIgnoreCase) ? (value[..^3], 180 / Math.PI)
            : (value, 1.0);
        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number * factor : null;
    }

    // ---------------------------------------------------------------- colours

    /// <summary>"#RRGGBB" of a colour given as "RRGGBB" (or "AARRGGBB": the alpha is dropped).</summary>
    public static string Hex(string rrggbb) => "#" + (rrggbb.Length == 8 ? rrggbb[2..] : rrggbb).ToUpperInvariant();

    /// <summary>"RRGGBB" of "#rrggbb", or null.</summary>
    public static string? ColorOf(string? value) =>
        value is { Length: 7 } && value[0] == '#' && int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? value[1..].ToUpperInvariant() : null;

    // ---------------------------------------------------------------- dates and durations

    /// <summary>An ISO 8601 duration ("PT12H30M15.5S", "P1DT2H") in days, or null.</summary>
    public static double? DurationDays(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        try
        {
            var negative = value.StartsWith('-');
            var span = System.Xml.XmlConvert.ToTimeSpan(negative ? value[1..] : value);
            var days = span.Ticks / (double)TimeSpan.TicksPerDay;
            return negative ? -days : days;
        }
        catch (FormatException)
        {
            return null;
        }
    }

    /// <summary>A number of days as "PT36H00M00.5S" (hours may pass 24, as LibreOffice writes elapsed times).</summary>
    public static string Duration(double days)
    {
        var negative = days < 0;
        var ticks = (long)Math.Round(Math.Abs(days) * TimeSpan.TicksPerDay);
        var hours = ticks / TimeSpan.TicksPerHour;
        ticks -= hours * TimeSpan.TicksPerHour;
        var minutes = ticks / TimeSpan.TicksPerMinute;
        ticks -= minutes * TimeSpan.TicksPerMinute;
        var seconds = ticks / (double)TimeSpan.TicksPerSecond;
        return (negative ? "-" : "") + "PT" + hours.ToString(CultureInfo.InvariantCulture) + "H" + minutes.ToString("00", CultureInfo.InvariantCulture) + "M"
            + seconds.ToString("00.#######", CultureInfo.InvariantCulture) + "S";
    }

    /// <summary>"2026-11-19" or "2026-11-19T10:30:00.5" as a DateTime (unspecified kind), or null.</summary>
    public static DateTime? DateOf(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var text = value.Trim();
        var zone = text.IndexOfAny(['Z', '+'], 10);
        if (zone > 0)
        {
            text = text[..zone];
        }

        return DateTime.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.NoCurrentDateDefault, out var date) ? date : null;
    }

    /// <summary>A DateTime as ODF writes a date ("2026-11-19") or a date with time ("2026-11-19T10:30:00.5").</summary>
    public static string DateText(DateTime value) =>
        value.TimeOfDay == TimeSpan.Zero
            ? value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
            : value.ToString("yyyy-MM-ddTHH:mm:ss.FFFFFFF", CultureInfo.InvariantCulture);

    // ---------------------------------------------------------------- text

    /// <summary>
    /// The plain text of a text:p / text:h / text:span: text:s (spaces), text:tab, text:line-break and nested spans and links; notes
    /// and annotations inside are left out.
    /// </summary>
    public static string PlainText(XElement element)
    {
        var builder = new System.Text.StringBuilder();
        Append(element, builder);
        return builder.ToString();

        static void Append(XElement parent, System.Text.StringBuilder builder)
        {
            foreach (var node in parent.Nodes())
            {
                switch (node)
                {
                    case XText text:
                        builder.Append(text.Value);
                        break;
                    case XElement child when child.Name == TextNs + "s":
                        builder.Append(' ', Math.Clamp((int?)child.Attribute(TextNs + "c") ?? 1, 1, 1000));
                        break;
                    case XElement child when child.Name == TextNs + "tab":
                        builder.Append('\t');
                        break;
                    case XElement child when child.Name == TextNs + "line-break":
                        builder.Append('\n');
                        break;
                    case XElement child when child.Name == TextNs + "note" || child.Name == OfficeNs + "annotation" || child.Name == OfficeNs + "annotation-end":
                        break;
                    case XElement child:
                        Append(child, builder);
                        break;
                }
            }
        }
    }

    /// <summary>
    /// Text content for a text:p as ODF wants it: runs of spaces after the first as text:s, tabs as text:tab, line breaks as
    /// text:line-break (a leading space is a text:s too, as white space at the start collapses).
    /// </summary>
    public static IEnumerable<object> TextContent(string text)
    {
        var parts = new List<object>();
        var run = new System.Text.StringBuilder();
        for (var index = 0; index < text.Length; index++)
        {
            var character = text[index];
            if (character == '\t' || character == '\n')
            {
                Flush();
                parts.Add(new XElement(TextNs + (character == '\t' ? "tab" : "line-break")));
                continue;
            }

            if (character == '\r')
            {
                continue;
            }

            if (character == ' ' && (index == 0 || index == text.Length - 1 || text[index - 1] == ' ' || text[index - 1] == '\t' || text[index - 1] == '\n'
                || (index + 1 < text.Length && text[index + 1] == ' ')))
            {
                var count = 1;
                while (index + 1 < text.Length && text[index + 1] == ' ')
                {
                    count++;
                    index++;
                }

                Flush();
                parts.Add(count == 1 ? new XElement(TextNs + "s") : new XElement(TextNs + "s", new XAttribute(TextNs + "c", count)));
                continue;
            }

            run.Append(character);
        }

        Flush();
        return parts;

        void Flush()
        {
            if (run.Length > 0)
            {
                parts.Add(new XText(run.ToString()));
                run.Clear();
            }
        }
    }
}
