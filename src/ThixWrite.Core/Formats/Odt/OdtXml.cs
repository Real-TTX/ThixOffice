using System.Globalization;
using System.Xml.Linq;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;

namespace ThixWrite.Core.Formats.Odt;

/// <summary>The namespaces of OpenDocument, its lengths ("2.5cm" ⇄ points) and the formatting properties both ways.</summary>
internal static class OdtXml
{
    public const string MimeType = "application/vnd.oasis.opendocument.text";

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

    public static string Percent(double value) => Math.Round(value * 100, 2).ToString("0.##", CultureInfo.InvariantCulture) + "%";

    /// <summary>"#RRGGBB" of a model colour ("RRGGBB").</summary>
    public static string Hex(string rrggbb) => "#" + rrggbb.ToUpperInvariant();

    /// <summary>"RRGGBB" of "#rrggbb", or null.</summary>
    public static string? ColorOf(string? value) =>
        value is { Length: 7 } && value[0] == '#' && int.TryParse(value.AsSpan(1), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out _) ? value[1..].ToUpperInvariant() : null;

    // ---------------------------------------------------------------- character properties

    /// <summary>The style:text-properties of character properties (null when there is nothing to write).</summary>
    public static XElement? TextProperties(CharacterProperties properties, Theme theme)
    {
        var element = new XElement(StyleNs + "text-properties");
        var family = properties.FontFamily ?? (properties.FontTheme is { } slot ? theme.FontOf(slot) : null);
        if (family is not null)
        {
            element.Add(new XAttribute(FoNs + "font-family", family.Contains(' ') ? $"'{family}'" : family), new XAttribute(StyleNs + "font-name", family));
        }

        if (properties.FontSize is { } size)
        {
            element.Add(new XAttribute(FoNs + "font-size", Length(size)));
        }

        if (properties.Bold is { } bold)
        {
            element.Add(new XAttribute(FoNs + "font-weight", bold ? "bold" : "normal"));
        }

        if (properties.Italic is { } italic)
        {
            element.Add(new XAttribute(FoNs + "font-style", italic ? "italic" : "normal"));
        }

        if (properties.Underline is { } underline)
        {
            element.Add(new XAttribute(StyleNs + "text-underline-style", UnderlineStyleOf(underline)));
            if (underline != UnderlineStyle.None)
            {
                element.Add(new XAttribute(StyleNs + "text-underline-width", underline is UnderlineStyle.Thick or UnderlineStyle.DottedHeavy or UnderlineStyle.DashedHeavy or UnderlineStyle.WavyHeavy ? "bold" : "auto"),
                    new XAttribute(StyleNs + "text-underline-color", "font-color"));
                if (underline is UnderlineStyle.Double or UnderlineStyle.WavyDouble)
                {
                    element.Add(new XAttribute(StyleNs + "text-underline-type", "double"));
                }
            }
        }

        if (properties.Strikethrough is { } strike)
        {
            element.Add(new XAttribute(StyleNs + "text-line-through-style", strike ? "solid" : "none"));
        }
        else if (properties.DoubleStrikethrough == true)
        {
            element.Add(new XAttribute(StyleNs + "text-line-through-style", "solid"), new XAttribute(StyleNs + "text-line-through-type", "double"));
        }

        if (properties.VerticalPosition is { } position)
        {
            element.Add(new XAttribute(StyleNs + "text-position", position switch
            {
                VerticalPosition.Superscript => "super 58%",
                VerticalPosition.Subscript => "sub 58%",
                _ => "0% 100%",
            }));
        }

        if (properties.Color is { IsAuto: false } color)
        {
            element.Add(new XAttribute(FoNs + "color", Hex(theme.ColorOf(color.ThemeColor ?? "") ?? color.Value)));
        }

        if (properties.Highlight is { } highlight)
        {
            element.Add(new XAttribute(FoNs + "background-color", highlight == HighlightColor.None ? "transparent" : "#" + DisplayListBuilder.ColorOf(highlight).ToHex()));
        }

        return element.HasAttributes ? element : null;
    }

    /// <summary>Character properties of a style:text-properties element (what ThixWrite models of it).</summary>
    public static CharacterProperties ReadText(XElement? element)
    {
        if (element is null)
        {
            return CharacterProperties.Empty;
        }

        var properties = CharacterProperties.Empty;
        var family = (string?)element.Attribute(StyleNs + "font-name") ?? ((string?)element.Attribute(FoNs + "font-family"))?.Trim('\'', '"');
        if (!string.IsNullOrWhiteSpace(family))
        {
            properties = properties.WithFont(family);
        }

        if (Points((string?)element.Attribute(FoNs + "font-size")) is { } size && size > 0)
        {
            properties = properties with { FontSize = Math.Round(size * 2) / 2 };
        }

        if ((string?)element.Attribute(FoNs + "font-weight") is { } weight)
        {
            properties = properties with { Bold = weight is "bold" or "600" or "700" or "800" or "900" };
        }

        if ((string?)element.Attribute(FoNs + "font-style") is { } style)
        {
            properties = properties with { Italic = style is "italic" or "oblique" };
        }

        if ((string?)element.Attribute(StyleNs + "text-underline-style") is { } underline)
        {
            var isDouble = (string?)element.Attribute(StyleNs + "text-underline-type") == "double";
            properties = properties with { Underline = underline == "none" ? UnderlineStyle.None : UnderlineOf(underline, isDouble) };
        }

        if ((string?)element.Attribute(StyleNs + "text-line-through-style") is { } through)
        {
            var isDouble = (string?)element.Attribute(StyleNs + "text-line-through-type") == "double";
            properties = isDouble && through != "none"
                ? properties with { DoubleStrikethrough = true }
                : properties with { Strikethrough = through != "none" };
        }

        if ((string?)element.Attribute(StyleNs + "text-position") is { } position)
        {
            var first = position.Split(' ')[0];
            properties = properties with
            {
                VerticalPosition = first == "super" || (first.EndsWith('%') && double.TryParse(first.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var up) && up > 0) ? VerticalPosition.Superscript
                    : first == "sub" || first.StartsWith('-') ? VerticalPosition.Subscript
                    : VerticalPosition.Baseline,
            };
        }

        if (ColorOf((string?)element.Attribute(FoNs + "color")) is { } color)
        {
            properties = properties with { Color = TextColor.FromHex(color) };
        }

        if ((string?)element.Attribute(FoNs + "background-color") is { } background)
        {
            properties = properties with { Highlight = background == "transparent" ? HighlightColor.None : HighlightOf(ColorOf(background)) };
        }

        return properties;
    }

    // ---------------------------------------------------------------- paragraph properties

    /// <summary>The style:paragraph-properties of paragraph properties (null when there is nothing to write).</summary>
    public static XElement? ParagraphProperties(ParagraphProperties properties)
    {
        var element = new XElement(StyleNs + "paragraph-properties");
        if (properties.Alignment is { } alignment)
        {
            element.Add(new XAttribute(FoNs + "text-align", alignment switch
            {
                ParagraphAlignment.Center => "center",
                ParagraphAlignment.Right => "end",
                ParagraphAlignment.Justify => "justify",
                _ => "start",
            }));
        }

        Add(element, FoNs + "margin-left", properties.IndentLeft);
        Add(element, FoNs + "margin-right", properties.IndentRight);
        Add(element, FoNs + "text-indent", properties.FirstLineIndent);
        Add(element, FoNs + "margin-top", properties.SpacingBefore);
        Add(element, FoNs + "margin-bottom", properties.SpacingAfter);
        if (properties.LineSpacing is { } spacing)
        {
            element.Add(spacing.Rule switch
            {
                LineSpacingRule.AtLeast => new XAttribute(StyleNs + "line-height-at-least", Length(spacing.Value)),
                LineSpacingRule.Exact => new XAttribute(FoNs + "line-height", Length(spacing.Value)),
                _ => new XAttribute(FoNs + "line-height", Percent(spacing.Value)),
            });
        }

        if (properties.KeepWithNext is { } keepWithNext)
        {
            element.Add(new XAttribute(FoNs + "keep-with-next", keepWithNext ? "always" : "auto"));
        }

        if (properties.KeepLinesTogether is { } together)
        {
            element.Add(new XAttribute(FoNs + "keep-together", together ? "always" : "auto"));
        }

        if (properties.PageBreakBefore == true)
        {
            element.Add(new XAttribute(FoNs + "break-before", "page"));
        }

        if (properties.WidowControl is { } widows)
        {
            element.Add(new XAttribute(FoNs + "widows", widows ? "2" : "0"), new XAttribute(FoNs + "orphans", widows ? "2" : "0"));
        }

        if (properties.ContextualSpacing is { } contextual)
        {
            element.Add(new XAttribute(StyleNs + "contextual-spacing", contextual ? "true" : "false"));
        }

        if (properties.Tabs is { Count: > 0 } tabs)
        {
            element.Add(new XElement(StyleNs + "tab-stops", tabs.Where(tab => tab.Alignment != TabAlignment.Clear).Select(tab => new XElement(StyleNs + "tab-stop",
                new XAttribute(StyleNs + "position", Length(tab.Position)),
                tab.Alignment switch
                {
                    TabAlignment.Center => new XAttribute(StyleNs + "type", "center"),
                    TabAlignment.Right => new XAttribute(StyleNs + "type", "right"),
                    TabAlignment.Decimal => new XAttribute(StyleNs + "type", "char"),
                    _ => null,
                },
                tab.Leader == TabLeader.None ? null : new XAttribute(StyleNs + "leader-text", tab.Leader switch
                {
                    TabLeader.Hyphen => "-",
                    TabLeader.Underscore or TabLeader.Heavy => "_",
                    TabLeader.MiddleDot => "·",
                    _ => ".",
                })))));
        }

        return element.HasAttributes || element.HasElements ? element : null;
    }

    /// <summary>Paragraph properties of a style:paragraph-properties element.</summary>
    public static ParagraphProperties ReadParagraph(XElement? element)
    {
        if (element is null)
        {
            return Model.ParagraphProperties.Empty;
        }

        var properties = Model.ParagraphProperties.Empty;
        if ((string?)element.Attribute(FoNs + "text-align") is { } align)
        {
            properties = properties with
            {
                Alignment = align switch
                {
                    "center" => ParagraphAlignment.Center,
                    "end" or "right" => ParagraphAlignment.Right,
                    "justify" => ParagraphAlignment.Justify,
                    _ => ParagraphAlignment.Left,
                },
            };
        }

        properties = properties with
        {
            IndentLeft = Points((string?)element.Attribute(FoNs + "margin-left")) ?? properties.IndentLeft,
            IndentRight = Points((string?)element.Attribute(FoNs + "margin-right")) ?? properties.IndentRight,
            FirstLineIndent = Points((string?)element.Attribute(FoNs + "text-indent")) ?? properties.FirstLineIndent,
            SpacingBefore = Points((string?)element.Attribute(FoNs + "margin-top")) ?? properties.SpacingBefore,
            SpacingAfter = Points((string?)element.Attribute(FoNs + "margin-bottom")) ?? properties.SpacingAfter,
        };
        if ((string?)element.Attribute(FoNs + "line-height") is { } height)
        {
            properties = height.EndsWith('%') && double.TryParse(height.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent)
                ? properties with { LineSpacing = new LineSpacing(LineSpacingRule.Multiple, Math.Round(percent / 100, 4)) }
                : Points(height) is { } exact ? properties with { LineSpacing = new LineSpacing(LineSpacingRule.Exact, exact) } : properties;
        }
        else if (Points((string?)element.Attribute(StyleNs + "line-height-at-least")) is { } least)
        {
            properties = properties with { LineSpacing = new LineSpacing(LineSpacingRule.AtLeast, least) };
        }

        if ((string?)element.Attribute(FoNs + "keep-with-next") is { } keep)
        {
            properties = properties with { KeepWithNext = keep == "always" };
        }

        if ((string?)element.Attribute(FoNs + "keep-together") is { } together)
        {
            properties = properties with { KeepLinesTogether = together == "always" };
        }

        if ((string?)element.Attribute(FoNs + "break-before") == "page")
        {
            properties = properties with { PageBreakBefore = true };
        }

        if ((string?)element.Attribute(FoNs + "widows") is { } widows)
        {
            properties = properties with { WidowControl = widows != "0" };
        }

        if ((string?)element.Attribute(StyleNs + "contextual-spacing") is { } contextual)
        {
            properties = properties with { ContextualSpacing = contextual == "true" };
        }

        if (element.Element(StyleNs + "tab-stops") is { } stops)
        {
            var tabs = stops.Elements(StyleNs + "tab-stop").Select(stop => new TabStop(
                Points((string?)stop.Attribute(StyleNs + "position")) ?? 0,
                (string?)stop.Attribute(StyleNs + "type") switch
                {
                    "center" => TabAlignment.Center,
                    "right" => TabAlignment.Right,
                    "char" => TabAlignment.Decimal,
                    _ => TabAlignment.Left,
                },
                ((string?)stop.Attribute(StyleNs + "leader-text") ?? ((string?)stop.Attribute(StyleNs + "leader-style") is { } leader && leader != "none" ? "." : null)) switch
                {
                    "." => TabLeader.Dot,
                    "-" => TabLeader.Hyphen,
                    "_" => TabLeader.Underscore,
                    "·" => TabLeader.MiddleDot,
                    _ => TabLeader.None,
                })).ToList();
            properties = properties with { Tabs = new EquatableArray<TabStop>(tabs) };
        }

        return properties;
    }

    private static void Add(XElement element, XName name, double? points)
    {
        if (points is { } value)
        {
            element.Add(new XAttribute(name, Length(value)));
        }
    }

    private static string UnderlineStyleOf(UnderlineStyle underline) => underline switch
    {
        UnderlineStyle.None => "none",
        UnderlineStyle.Dotted or UnderlineStyle.DottedHeavy => "dotted",
        UnderlineStyle.Dash or UnderlineStyle.DashedHeavy => "dash",
        UnderlineStyle.DashLong or UnderlineStyle.DashLongHeavy => "long-dash",
        UnderlineStyle.DotDash or UnderlineStyle.DashDotHeavy => "dot-dash",
        UnderlineStyle.DotDotDash or UnderlineStyle.DashDotDotHeavy => "dot-dot-dash",
        UnderlineStyle.Wave or UnderlineStyle.WavyHeavy or UnderlineStyle.WavyDouble => "wave",
        _ => "solid",
    };

    private static UnderlineStyle UnderlineOf(string style, bool isDouble) => style switch
    {
        "dotted" => UnderlineStyle.Dotted,
        "dash" => UnderlineStyle.Dash,
        "long-dash" => UnderlineStyle.DashLong,
        "dot-dash" => UnderlineStyle.DotDash,
        "dot-dot-dash" => UnderlineStyle.DotDotDash,
        "wave" => isDouble ? UnderlineStyle.WavyDouble : UnderlineStyle.Wave,
        _ => isDouble ? UnderlineStyle.Double : UnderlineStyle.Single,
    };

    private static HighlightColor HighlightOf(string? hex) =>
        hex is null ? HighlightColor.None : Enum.GetValues<HighlightColor>().FirstOrDefault(highlight => highlight != HighlightColor.None
            && string.Equals(DisplayListBuilder.ColorOf(highlight).ToHex(), hex, StringComparison.OrdinalIgnoreCase), HighlightColor.Yellow);
}
