using System.Globalization;
using DocumentFormat.OpenXml;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Units and tolerant value parsing for WordprocessingML: lengths in twips (1/20 pt), font sizes in half points, drawing
/// sizes in EMU. Every parser accepts odd input (returns null) instead of throwing – real-world files are not always valid.
/// </summary>
internal static class DocxValues
{
    public const string WordNamespace = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    public const string RelationshipNamespace = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";

    public static double TwipsToPoints(long twips) => twips / 20.0;

    public static int PointsToTwips(double points) => (int)Math.Round(points * 20, MidpointRounding.AwayFromZero);

    public static double EmuToPoints(long emu) => emu / 12700.0;

    /// <summary>The integer in <paramref name="text"/> (also "720.0" or "12pt" in twips-free form), or null.</summary>
    public static long? ParseInteger(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value))
        {
            return value;
        }

        return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? (long)Math.Round(number) : null;
    }

    /// <summary>A twips value as points, or null.</summary>
    public static double? ParseTwips(string? text) => ParseInteger(text) is { } twips ? TwipsToPoints(twips) : null;

    /// <summary>An on/off value: absent attribute = on; "0", "false", "off" = off.</summary>
    public static bool ParseOnOff(OpenXmlElement element)
    {
        var value = AttributeValue(element, "val");
        return value is null || value.Trim().ToLowerInvariant() is not ("0" or "false" or "off");
    }

    /// <summary>The value of the w: attribute <paramref name="localName"/> (any namespace as a fallback), or null.</summary>
    public static string? AttributeValue(OpenXmlElement element, string localName)
    {
        string? fallback = null;
        foreach (var attribute in element.GetAttributes())
        {
            if (attribute.LocalName != localName)
            {
                continue;
            }

            if (attribute.NamespaceUri == WordNamespace)
            {
                return attribute.Value;
            }

            fallback ??= attribute.Value;
        }

        return fallback;
    }

    public static string Format(double value) => value.ToString("0.####", CultureInfo.InvariantCulture);
}
