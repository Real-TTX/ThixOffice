using System.Xml.Linq;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixOffice.Core.Formats.Odf;

/// <summary>
/// The character attributes of a style:text-properties element that the spreadsheet and the presentation share: family, size,
/// bold, italic, underline, strike-through, raised/lowered text and colour (each null = not given, so styles can inherit).
/// <see cref="Read"/> and <see cref="Write"/> are inverse for every value this record can hold.
/// </summary>
public sealed record OdfFont
{
    public static OdfFont Empty { get; } = new();

    public string? Family { get; init; }

    public double? Size { get; init; }

    public bool? Bold { get; init; }

    public bool? Italic { get; init; }

    /// <summary>"none", "solid", "dotted", "dash", "long-dash", "dot-dash", "dot-dot-dash", "wave".</summary>
    public string? Underline { get; init; }

    public bool UnderlineDouble { get; init; }

    public bool? Strikethrough { get; init; }

    /// <summary>Raised (&gt; 0) or lowered (&lt; 0) text in percent of the font size; 0 = baseline.</summary>
    public double? Position { get; init; }

    /// <summary>"RRGGBB".</summary>
    public string? Color { get; init; }

    public bool IsEmpty => this == Empty;

    /// <summary>The attributes of a style:text-properties element (missing ones stay null).</summary>
    public static OdfFont Read(XElement? element)
    {
        if (element is null)
        {
            return Empty;
        }

        var family = (string?)element.Attribute(FoNs + "font-family") ?? (string?)element.Attribute(StyleNs + "font-name");
        var underline = (string?)element.Attribute(StyleNs + "text-underline-style");
        var through = (string?)element.Attribute(StyleNs + "text-line-through-style");
        double? position = null;
        if ((string?)element.Attribute(StyleNs + "text-position") is { } text)
        {
            var first = text.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "0%";
            position = first switch
            {
                "super" => 33,
                "sub" => -33,
                _ => PercentOf(first) * 100 ?? 0,
            };
        }

        var weight = (string?)element.Attribute(FoNs + "font-weight");
        var style = (string?)element.Attribute(FoNs + "font-style");
        return new OdfFont
        {
            Family = string.IsNullOrWhiteSpace(family) ? null : family.Trim().Trim('\'', '"'),
            Size = Points((string?)element.Attribute(FoNs + "font-size")) is { } size && size > 0 ? Math.Round(size, 2) : null,
            Bold = weight is null ? null : weight is "bold" or "600" or "700" or "800" or "900",
            Italic = style is null ? null : style is "italic" or "oblique",
            Underline = underline,
            UnderlineDouble = (string?)element.Attribute(StyleNs + "text-underline-type") == "double",
            Strikethrough = through is null ? null : through != "none",
            Position = position,
            Color = ColorOf((string?)element.Attribute(FoNs + "color")),
        };
    }

    /// <summary>The attributes of this font on a style:text-properties element (also the Asian and complex script sizes/weights).</summary>
    public XElement Write(XElement? element = null)
    {
        element ??= new XElement(StyleNs + "text-properties");
        if (Family is { } family)
        {
            element.Add(new XAttribute(StyleNs + "font-name", family), new XAttribute(FoNs + "font-family", family.Contains(' ') ? $"'{family}'" : family));
        }

        if (Size is { } size)
        {
            element.Add(new XAttribute(FoNs + "font-size", Length(size)), new XAttribute(StyleNs + "font-size-asian", Length(size)), new XAttribute(StyleNs + "font-size-complex", Length(size)));
        }

        if (Bold is { } bold)
        {
            var weight = bold ? "bold" : "normal";
            element.Add(new XAttribute(FoNs + "font-weight", weight), new XAttribute(StyleNs + "font-weight-asian", weight), new XAttribute(StyleNs + "font-weight-complex", weight));
        }

        if (Italic is { } italic)
        {
            var style = italic ? "italic" : "normal";
            element.Add(new XAttribute(FoNs + "font-style", style), new XAttribute(StyleNs + "font-style-asian", style), new XAttribute(StyleNs + "font-style-complex", style));
        }

        if (Underline is { } underline)
        {
            element.Add(new XAttribute(StyleNs + "text-underline-style", underline));
            if (underline != "none")
            {
                element.Add(new XAttribute(StyleNs + "text-underline-width", "auto"), new XAttribute(StyleNs + "text-underline-color", "font-color"));
                if (UnderlineDouble)
                {
                    element.Add(new XAttribute(StyleNs + "text-underline-type", "double"));
                }
            }
        }

        if (Strikethrough is { } strike)
        {
            element.Add(new XAttribute(StyleNs + "text-line-through-style", strike ? "solid" : "none"));
        }

        if (Position is { } position)
        {
            element.Add(new XAttribute(StyleNs + "text-position", position == 0 ? "0% 100%" : $"{OdfXml.Number(Math.Round(position, 2))}% 58%"));
        }

        if (Color is { } color)
        {
            element.Add(new XAttribute(FoNs + "color", Hex(color)));
        }

        return element;
    }
}
