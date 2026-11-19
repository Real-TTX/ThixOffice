using System.Globalization;
using System.Xml.Linq;
using ThixCalc.Core.Model;
using ThixOffice.Core.Formats.Odf;
using ThixOffice.Text.Drawing;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixCalc.Core.Formats.Ods;

/// <summary>
/// A cell style (<see cref="CellStyle"/>) ⇄ an ODF table-cell style: font (style:text-properties), fill, borders, wrapping,
/// rotation, vertical alignment, shrinking and protection (style:table-cell-properties), horizontal alignment and indent
/// (style:paragraph-properties). Theme and indexed colours are written as the colours they show.
/// </summary>
internal static class OdsCellStyles
{
    /// <summary>Points of indent per Excel indent level.</summary>
    private const double IndentPoints = 10;

    public static XElement Write(CellStyle style, CellStyle defaults, ThemeColors theme, string name, string? dataStyle, bool isDefault = false)
    {
        var element = isDefault
            ? new XElement(StyleNs + "default-style", new XAttribute(StyleNs + "family", "table-cell"))
            : new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "table-cell"));
        if (!isDefault && name != "Default")
        {
            element.Add(new XAttribute(StyleNs + "parent-style-name", "Default"));
        }

        if (dataStyle is not null)
        {
            element.Add(new XAttribute(StyleNs + "data-style-name", dataStyle));
        }

        var cell = new XElement(StyleNs + "table-cell-properties");
        var fill = style.Fill;
        if (fill.Pattern != FillPattern.None && (fill.Foreground ?? fill.Background) is { } fillColor)
        {
            var color = fill.Pattern == FillPattern.Solid || fill.Foreground is not null ? fill.Foreground ?? fillColor : fillColor;
            cell.Add(new XAttribute(FoNs + "background-color", "#" + color.Resolve(theme, RgbColor.White).ToHex()));
        }
        else if (fill.GradientXml is not null && fill.Foreground is { } first)
        {
            cell.Add(new XAttribute(FoNs + "background-color", "#" + first.Resolve(theme, RgbColor.White).ToHex()));
        }
        else if (!isDefault && defaults.Fill.Pattern != FillPattern.None)
        {
            cell.Add(new XAttribute(FoNs + "background-color", "transparent"));
        }

        var borders = style.Borders;
        AddBorder(cell, "border-left", borders.Left, theme, explicitNone: !isDefault && defaults.Borders.Left.IsVisible);
        AddBorder(cell, "border-right", borders.Right, theme, explicitNone: !isDefault && defaults.Borders.Right.IsVisible);
        AddBorder(cell, "border-top", borders.Top, theme, explicitNone: !isDefault && defaults.Borders.Top.IsVisible);
        AddBorder(cell, "border-bottom", borders.Bottom, theme, explicitNone: !isDefault && defaults.Borders.Bottom.IsVisible);
        if (borders.DiagonalUp)
        {
            AddBorder(cell, "diagonal-bl-tr", borders.Diagonal, theme, StyleNs, false);
        }

        if (borders.DiagonalDown)
        {
            AddBorder(cell, "diagonal-tl-br", borders.Diagonal, theme, StyleNs, false);
        }

        var alignment = style.Alignment;
        if (alignment.WrapText)
        {
            cell.Add(new XAttribute(FoNs + "wrap-option", "wrap"));
        }

        if (alignment.ShrinkToFit)
        {
            cell.Add(new XAttribute(StyleNs + "shrink-to-fit", "true"));
        }

        if (alignment.TextRotation == 255)
        {
            cell.Add(new XAttribute(StyleNs + "direction", "ttb"));
        }
        else if (alignment.TextRotation != 0)
        {
            var angle = alignment.TextRotation <= 90 ? alignment.TextRotation : 360 - (alignment.TextRotation - 90);
            cell.Add(new XAttribute(StyleNs + "rotation-angle", angle.ToString(CultureInfo.InvariantCulture)));
        }

        if (alignment.Vertical != VerticalAlignment.Bottom || isDefault)
        {
            cell.Add(new XAttribute(StyleNs + "vertical-align", alignment.Vertical switch
            {
                VerticalAlignment.Top => "top",
                VerticalAlignment.Center or VerticalAlignment.Justify or VerticalAlignment.Distributed => "middle",
                _ => "bottom",
            }));
        }

        if (alignment.Horizontal != HorizontalAlignment.General)
        {
            cell.Add(new XAttribute(StyleNs + "text-align-source", "fix"));
            if (alignment.Horizontal == HorizontalAlignment.Fill)
            {
                cell.Add(new XAttribute(StyleNs + "repeat-content", "true"));
            }
        }

        var protection = style.Protection;
        if (protection != CellProtection.Default)
        {
            cell.Add(new XAttribute(StyleNs + "cell-protect", (protection.Locked, protection.Hidden) switch
            {
                (true, true) => "protected formula-hidden",
                (false, true) => "formula-hidden",
                (false, false) => "none",
                _ => "protected",
            }));
        }

        if (cell.HasAttributes)
        {
            element.Add(cell);
        }

        if (alignment.Horizontal != HorizontalAlignment.General || alignment.Indent > 0)
        {
            var paragraph = new XElement(StyleNs + "paragraph-properties");
            if (alignment.Horizontal != HorizontalAlignment.General)
            {
                paragraph.Add(new XAttribute(FoNs + "text-align", alignment.Horizontal switch
                {
                    HorizontalAlignment.Center or HorizontalAlignment.CenterContinuous => "center",
                    HorizontalAlignment.Right => "end",
                    HorizontalAlignment.Justify or HorizontalAlignment.Distributed => "justify",
                    _ => "start",
                }));
            }

            if (alignment.Indent > 0)
            {
                paragraph.Add(new XAttribute(FoNs + "margin-left", Length(alignment.Indent * IndentPoints)));
            }

            element.Add(paragraph);
        }

        var font = FontOf(style.Font, theme, isDefault ? null : defaults.Font);
        if (!font.IsEmpty)
        {
            element.Add(font.Write());
        }

        return element;
    }

    /// <summary>The font as ODF attributes; with <paramref name="defaults"/> only what differs from it.</summary>
    public static OdfFont FontOf(CellFont font, ThemeColors theme, CellFont? defaults)
    {
        var color = font.Color is { Kind: not ColorKind.Auto } given ? given.Resolve(theme, RgbColor.Black).ToHex() : null;
        var defaultColor = defaults?.Color is { Kind: not ColorKind.Auto } other ? other.Resolve(theme, RgbColor.Black).ToHex() : null;
        var underline = font.Underline switch
        {
            UnderlineKind.None => "none",
            _ => "solid",
        };
        return new OdfFont
        {
            Family = defaults is null || font.Name != defaults.Name ? font.Name : null,
            Size = defaults is null || font.Size != defaults.Size ? font.Size : null,
            Bold = defaults is null || font.Bold != defaults.Bold ? font.Bold : null,
            Italic = defaults is null || font.Italic != defaults.Italic ? font.Italic : null,
            Underline = defaults is null || font.Underline != defaults.Underline ? underline : null,
            UnderlineDouble = font.Underline is UnderlineKind.Double or UnderlineKind.DoubleAccounting,
            Strikethrough = defaults is null || font.Strikethrough != defaults.Strikethrough ? font.Strikethrough : null,
            Position = defaults is null || font.Script != defaults.Script ? font.Script switch { FontScript.Superscript => 33, FontScript.Subscript => -33, _ => 0 } : null,
            Color = color != defaultColor ? color : null,
        };
    }

    /// <summary>Applies what a table-cell style element sets onto <paramref name="style"/>.</summary>
    public static CellStyle Read(CellStyle style, XElement element)
    {
        if (element.Element(StyleNs + "text-properties") is { } text)
        {
            style = style with { Font = ApplyFont(style.Font, OdfFont.Read(text)) };
        }

        if (element.Element(StyleNs + "table-cell-properties") is { } cell)
        {
            style = ReadCell(style, cell);
        }

        if (element.Element(StyleNs + "paragraph-properties") is { } paragraph)
        {
            var alignment = style.Alignment;
            if ((string?)paragraph.Attribute(FoNs + "text-align") is { } align)
            {
                alignment = alignment with
                {
                    Horizontal = alignment.Horizontal == HorizontalAlignment.Fill ? HorizontalAlignment.Fill : align switch
                    {
                        "center" => HorizontalAlignment.Center,
                        "end" or "right" => HorizontalAlignment.Right,
                        "justify" => HorizontalAlignment.Justify,
                        _ => HorizontalAlignment.Left,
                    },
                };
            }

            if (Points((string?)paragraph.Attribute(FoNs + "margin-left")) is { } margin)
            {
                alignment = alignment with { Indent = Math.Clamp((int)Math.Round(margin / IndentPoints), 0, 250) };
            }

            style = style with { Alignment = alignment };
        }

        return style;
    }

    public static CellFont ApplyFont(CellFont font, OdfFont odf) => font with
    {
        Name = odf.Family ?? font.Name,
        Size = odf.Size ?? font.Size,
        Bold = odf.Bold ?? font.Bold,
        Italic = odf.Italic ?? font.Italic,
        Underline = odf.Underline is null ? font.Underline : odf.Underline == "none" ? UnderlineKind.None : odf.UnderlineDouble ? UnderlineKind.Double : UnderlineKind.Single,
        Strikethrough = odf.Strikethrough ?? font.Strikethrough,
        Script = odf.Position is null ? font.Script : odf.Position > 0 ? FontScript.Superscript : odf.Position < 0 ? FontScript.Subscript : FontScript.Baseline,
        Color = odf.Color is { } color ? CalcColor.FromHex(color) : font.Color,
    };

    private static CellStyle ReadCell(CellStyle style, XElement cell)
    {
        if ((string?)cell.Attribute(FoNs + "background-color") is { } background)
        {
            style = style with { Fill = ColorOf(background) is { } color ? CellFill.Solid(CalcColor.FromHex(color)) : CellFill.None };
        }

        var borders = style.Borders;
        if ((string?)cell.Attribute(FoNs + "border") is { } all)
        {
            var edge = ReadBorder(all);
            borders = borders with { Left = edge, Right = edge, Top = edge, Bottom = edge };
        }

        borders = borders with
        {
            Left = (string?)cell.Attribute(FoNs + "border-left") is { } left ? ReadBorder(left) : borders.Left,
            Right = (string?)cell.Attribute(FoNs + "border-right") is { } right ? ReadBorder(right) : borders.Right,
            Top = (string?)cell.Attribute(FoNs + "border-top") is { } top ? ReadBorder(top) : borders.Top,
            Bottom = (string?)cell.Attribute(FoNs + "border-bottom") is { } bottom ? ReadBorder(bottom) : borders.Bottom,
        };
        if ((string?)cell.Attribute(StyleNs + "diagonal-bl-tr") is { } up && ReadBorder(up) is { IsVisible: true } upEdge)
        {
            borders = borders with { Diagonal = upEdge, DiagonalUp = true };
        }

        if ((string?)cell.Attribute(StyleNs + "diagonal-tl-br") is { } down && ReadBorder(down) is { IsVisible: true } downEdge)
        {
            borders = borders with { Diagonal = downEdge, DiagonalDown = true };
        }

        style = style with { Borders = borders };
        var alignment = style.Alignment;
        if ((string?)cell.Attribute(FoNs + "wrap-option") is { } wrap)
        {
            alignment = alignment with { WrapText = wrap == "wrap" };
        }

        if ((string?)cell.Attribute(StyleNs + "shrink-to-fit") is { } shrink)
        {
            alignment = alignment with { ShrinkToFit = shrink == "true" };
        }

        if ((string?)cell.Attribute(StyleNs + "direction") == "ttb")
        {
            alignment = alignment with { TextRotation = 255 };
        }
        else if (Degrees((string?)cell.Attribute(StyleNs + "rotation-angle")) is { } angle)
        {
            var degrees = (int)Math.Round(((angle % 360) + 360) % 360);
            alignment = alignment with { TextRotation = degrees <= 90 ? degrees : degrees >= 270 ? 90 + (360 - degrees) : 0 };
        }

        if ((string?)cell.Attribute(StyleNs + "vertical-align") is { } vertical)
        {
            alignment = alignment with
            {
                Vertical = vertical switch
                {
                    "top" => VerticalAlignment.Top,
                    "middle" => VerticalAlignment.Center,
                    _ => VerticalAlignment.Bottom,
                },
            };
        }

        if ((string?)cell.Attribute(StyleNs + "text-align-source") == "value-type")
        {
            alignment = alignment with { Horizontal = HorizontalAlignment.General };
        }

        if ((string?)cell.Attribute(StyleNs + "repeat-content") == "true")
        {
            alignment = alignment with { Horizontal = HorizontalAlignment.Fill };
        }

        style = style with { Alignment = alignment };
        if ((string?)cell.Attribute(StyleNs + "cell-protect") is { } protect)
        {
            var words = protect.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            style = style with
            {
                Protection = new CellProtection(words.Contains("protected") || words.Contains("hidden-and-protected"),
                    words.Contains("formula-hidden") || words.Contains("hidden-and-protected")),
            };
        }

        return style;
    }

    // ---------------------------------------------------------------- borders

    private static void AddBorder(XElement cell, string name, BorderEdge edge, ThemeColors theme, XNamespace? ns = null, bool explicitNone = false)
    {
        if (!edge.IsVisible)
        {
            if (explicitNone)
            {
                cell.Add(new XAttribute(FoNs + name, "none"));
            }

            return;
        }

        cell.Add(new XAttribute((ns ?? FoNs) + name, BorderText(edge, theme)));
        if (edge.Style == BorderStyle.Double)
        {
            cell.Add(new XAttribute(StyleNs + (name.StartsWith("border", StringComparison.Ordinal) ? "border-line-width" + name[6..] : name + "-widths"), "0.26pt 0.26pt 0.26pt"));
        }
    }

    private static string BorderText(BorderEdge edge, ThemeColors theme)
    {
        var (width, line) = edge.Style switch
        {
            BorderStyle.Hair => (0.26, "solid"),
            BorderStyle.Medium => (1.76, "solid"),
            BorderStyle.Thick => (2.49, "solid"),
            BorderStyle.Double => (2.01, "double"),
            BorderStyle.Dashed => (0.74, "dashed"),
            BorderStyle.MediumDashed => (1.76, "dashed"),
            BorderStyle.Dotted => (0.74, "dotted"),
            BorderStyle.DashDot => (0.74, "dot-dash"),
            BorderStyle.MediumDashDot or BorderStyle.SlantDashDot => (1.76, "dot-dash"),
            BorderStyle.DashDotDot => (0.74, "dot-dot-dash"),
            BorderStyle.MediumDashDotDot => (1.76, "dot-dot-dash"),
            _ => (0.74, "solid"),
        };
        var color = edge.Color is { Kind: not ColorKind.Auto } given ? given.Resolve(theme, RgbColor.Black).ToHex() : "000000";
        return $"{width.ToString("0.##", CultureInfo.InvariantCulture)}pt {line} #{color}";
    }

    /// <summary>"0.74pt solid #000000" (any order) as a border edge.</summary>
    public static BorderEdge ReadBorder(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0 || parts.Contains("none") || parts.Contains("hidden"))
        {
            return BorderEdge.None;
        }

        var width = parts.Select(Points).FirstOrDefault(value => value is not null) ?? parts switch
        {
            _ when parts.Contains("thick") => 2.5,
            _ when parts.Contains("medium") => 1.75,
            _ => 0.75,
        };
        var color = parts.Select(ColorOf).FirstOrDefault(value => value is not null);
        var line = parts.FirstOrDefault(part => part is "solid" or "dashed" or "dotted" or "double" or "dot-dash" or "dot-dot-dash" or "fine-dashed"
            or "dash-dot" or "dash-dot-dot" or "double-thin" or "groove" or "ridge" or "inset" or "outset") ?? "solid";
        var heavy = width >= 1.2;
        var style = line switch
        {
            "double" or "double-thin" => BorderStyle.Double,
            "dashed" or "fine-dashed" => heavy ? BorderStyle.MediumDashed : BorderStyle.Dashed,
            "dotted" => BorderStyle.Dotted,
            "dot-dash" or "dash-dot" => heavy ? BorderStyle.MediumDashDot : BorderStyle.DashDot,
            "dot-dot-dash" or "dash-dot-dot" => heavy ? BorderStyle.MediumDashDotDot : BorderStyle.DashDotDot,
            _ => width < 0.5 ? BorderStyle.Hair : width < 1.2 ? BorderStyle.Thin : width < 2.2 ? BorderStyle.Medium : BorderStyle.Thick,
        };
        return new BorderEdge(style, color is null || color == "000000" ? null : CalcColor.FromHex(color));
    }
}
