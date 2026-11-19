using System.Globalization;
using DocumentFormat.OpenXml;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using X = DocumentFormat.OpenXml.Spreadsheet;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>
/// styles.xml ↔ the style table: cellXfs become interned <see cref="CellStyle"/>s (the reader maps xf index → style id),
/// the writer emits one xf per style id (so ids and xf indices agree) with deduplicated fonts, fills, borders and number
/// formats; named styles, differential formats and parts the model does not show are kept.
/// </summary>
internal static class XlsxStyles
{
    private const string MainNamespace = "http://schemas.openxmlformats.org/spreadsheetml/2006/main";

    /// <summary>Reads the stylesheet into <paramref name="styles"/>; returns the style id of every cellXfs index.</summary>
    public static int[] Read(X.Stylesheet? stylesheet, StyleTable styles)
    {
        if (stylesheet is null)
        {
            return [0];
        }

        var formats = new Dictionary<uint, string>();
        foreach (var format in stylesheet.NumberingFormats?.Elements<X.NumberingFormat>() ?? [])
        {
            if (format.NumberFormatId?.Value is { } id && format.FormatCode?.Value is { } code)
            {
                formats[id] = code;
            }
        }

        var fonts = stylesheet.Fonts?.Elements<X.Font>().Select(ReadFont).ToList() ?? [];
        var fills = stylesheet.Fills?.Elements<X.Fill>().Select(ReadFill).ToList() ?? [];
        var borders = stylesheet.Borders?.Elements<X.Border>().Select(ReadBorders).ToList() ?? [];

        CellStyle ReadXf(X.CellFormat xf)
        {
            var numberFormatId = xf.NumberFormatId?.Value ?? 0;
            var code = formats.TryGetValue(numberFormatId, out var custom) ? custom : BuiltInFormats.Code((int)numberFormatId) ?? "General";
            var style = new CellStyle
            {
                NumberFormat = code,
                Font = Pick(fonts, xf.FontId?.Value, CellFont.Default),
                Fill = Pick(fills, xf.FillId?.Value, CellFill.None),
                Borders = Pick(borders, xf.BorderId?.Value, CellBorders.None),
                ParentStyle = (int)(xf.FormatId?.Value ?? 0),
                QuotePrefix = xf.QuotePrefix?.Value ?? false,
            };
            if (xf.Alignment is { } alignment)
            {
                style = style with { Alignment = ReadAlignment(alignment) };
            }

            if (xf.Protection is { } protection)
            {
                style = style with { Protection = new CellProtection(protection.Locked?.Value ?? true, protection.Hidden?.Value ?? false) };
            }

            return style;
        }

        foreach (var xf in stylesheet.CellStyleFormats?.Elements<X.CellFormat>() ?? [])
        {
            styles.NamedStyleFormats.Add(ReadXf(xf) with { ParentStyle = 0 });
        }

        foreach (var named in stylesheet.CellStyles?.Elements<X.CellStyle>() ?? [])
        {
            styles.NamedStyles.Add(new NamedCellStyle(named.Name?.Value ?? "Normal", (int)(named.FormatId?.Value ?? 0), named.BuiltinId?.Value is { } builtIn ? (int)builtIn : null, named.OuterXml));
        }

        foreach (var dxf in stylesheet.DifferentialFormats?.Elements<X.DifferentialFormat>() ?? [])
        {
            styles.DifferentialFormats.Add(ReadDifferential(dxf, formats));
        }

        foreach (var element in stylesheet.ChildElements)
        {
            if (element is X.TableStyles or X.Colors or X.StylesheetExtensionList)
            {
                styles.PreservedXml.Add(element.OuterXml);
            }
        }

        var cellXfs = stylesheet.CellFormats?.Elements<X.CellFormat>().ToList() ?? [];
        var map = new int[Math.Max(1, cellXfs.Count)];
        for (var index = 0; index < cellXfs.Count; index++)
        {
            var style = ReadXf(cellXfs[index]);
            map[index] = index == 0 ? ReplaceDefault(styles, style) : styles.Intern(style);
        }

        return map;
    }

    private static int ReplaceDefault(StyleTable styles, CellStyle style)
    {
        // The first xf is the workbook's default: it becomes style 0.
        if (!Equals(styles.Default, style))
        {
            styles.ReplaceDefault(style);
        }

        return 0;
    }

    private static T Pick<T>(List<T> items, uint? index, T fallback) => index is { } value && value < items.Count ? items[(int)value] : fallback;

    internal static CalcColor? ReadColor(X.ColorType? color)
    {
        if (color is null)
        {
            return null;
        }

        var tint = color.Tint?.Value ?? 0;
        if (color.Rgb?.Value is { } rgb && rgb.Length >= 6)
        {
            return CalcColor.FromHex(rgb) with { Tint = tint };
        }

        if (color.Theme?.Value is { } theme)
        {
            return CalcColor.FromTheme((int)theme, tint);
        }

        if (color.Indexed?.Value is { } indexed)
        {
            return CalcColor.FromIndex((int)indexed) with { Tint = tint };
        }

        return color.Auto?.Value == true ? CalcColor.Automatic : null;
    }

    private static CellFont ReadFont(X.Font font) => new()
    {
        Name = font.FontName?.Val?.Value ?? "Calibri",
        Size = font.FontSize?.Val?.Value ?? 11,
        Bold = font.Bold is { } bold && (bold.Val?.Value ?? true),
        Italic = font.Italic is { } italic && (italic.Val?.Value ?? true),
        Strikethrough = font.Strike is { } strike && (strike.Val?.Value ?? true),
        Outline = font.Outline is { } outline && (outline.Val?.Value ?? true),
        Shadow = font.Shadow is { } shadow && (shadow.Val?.Value ?? true),
        Underline = font.Underline is null ? UnderlineKind.None : (font.Underline.Val?.Value.ToString() ?? "single") switch
        {
            "double" => UnderlineKind.Double,
            "singleAccounting" => UnderlineKind.SingleAccounting,
            "doubleAccounting" => UnderlineKind.DoubleAccounting,
            "none" => UnderlineKind.None,
            _ => UnderlineKind.Single,
        },
        Script = font.VerticalTextAlignment?.Val?.Value.ToString() switch
        {
            "superscript" => FontScript.Superscript,
            "subscript" => FontScript.Subscript,
            _ => FontScript.Baseline,
        },
        Color = ReadColor(font.Color),
        Family = font.FontFamilyNumbering?.Val?.Value,
        Scheme = font.FontScheme?.Val?.Value.ToString(),
        Charset = font.FontCharSet?.Val?.Value,
    };

    private static CellFill ReadFill(X.Fill fill)
    {
        if (fill.GradientFill is { } gradient)
        {
            var first = gradient.Elements<X.GradientStop>().FirstOrDefault()?.Color;
            return new CellFill(FillPattern.Solid, ReadColor(first), null, gradient.OuterXml);
        }

        var pattern = fill.PatternFill;
        if (pattern is null)
        {
            return CellFill.None;
        }

        return new CellFill(ParsePattern(pattern.PatternType?.InnerText), ReadColor(pattern.ForegroundColor), ReadColor(pattern.BackgroundColor));
    }

    private static FillPattern ParsePattern(string? name) => name switch
    {
        null or "none" => FillPattern.None,
        "solid" => FillPattern.Solid,
        "mediumGray" => FillPattern.MediumGray,
        "darkGray" => FillPattern.DarkGray,
        "lightGray" => FillPattern.LightGray,
        "darkHorizontal" => FillPattern.DarkHorizontal,
        "darkVertical" => FillPattern.DarkVertical,
        "darkDown" => FillPattern.DarkDown,
        "darkUp" => FillPattern.DarkUp,
        "darkGrid" => FillPattern.DarkGrid,
        "darkTrellis" => FillPattern.DarkTrellis,
        "lightHorizontal" => FillPattern.LightHorizontal,
        "lightVertical" => FillPattern.LightVertical,
        "lightDown" => FillPattern.LightDown,
        "lightUp" => FillPattern.LightUp,
        "lightGrid" => FillPattern.LightGrid,
        "lightTrellis" => FillPattern.LightTrellis,
        "gray125" => FillPattern.Gray125,
        "gray0625" => FillPattern.Gray0625,
        _ => FillPattern.None,
    };

    private static string PatternName(FillPattern pattern) => pattern switch
    {
        FillPattern.None => "none",
        FillPattern.Gray125 => "gray125",
        FillPattern.Gray0625 => "gray0625",
        _ => char.ToLowerInvariant(pattern.ToString()[0]) + pattern.ToString()[1..],
    };

    private static CellBorders ReadBorders(X.Border border) => new()
    {
        Left = ReadEdge(border.LeftBorder),
        Right = ReadEdge(border.RightBorder),
        Top = ReadEdge(border.TopBorder),
        Bottom = ReadEdge(border.BottomBorder),
        Diagonal = ReadEdge(border.DiagonalBorder),
        DiagonalUp = border.DiagonalUp?.Value ?? false,
        DiagonalDown = border.DiagonalDown?.Value ?? false,
    };

    private static BorderEdge ReadEdge(X.BorderPropertiesType? edge)
    {
        if (edge?.Style?.InnerText is not { Length: > 0 } style || style == "none")
        {
            return BorderEdge.None;
        }

        var parsed = Enum.TryParse<BorderStyle>(style, ignoreCase: true, out var value) ? value : BorderStyle.Thin;
        return new BorderEdge(parsed, ReadColor(edge.Color));
    }

    private static CellAlignment ReadAlignment(X.Alignment alignment) => new()
    {
        Horizontal = alignment.Horizontal?.InnerText switch
        {
            "left" => HorizontalAlignment.Left,
            "center" => HorizontalAlignment.Center,
            "right" => HorizontalAlignment.Right,
            "fill" => HorizontalAlignment.Fill,
            "justify" => HorizontalAlignment.Justify,
            "centerContinuous" => HorizontalAlignment.CenterContinuous,
            "distributed" => HorizontalAlignment.Distributed,
            _ => HorizontalAlignment.General,
        },
        Vertical = alignment.Vertical?.InnerText switch
        {
            "top" => VerticalAlignment.Top,
            "center" => VerticalAlignment.Center,
            "justify" => VerticalAlignment.Justify,
            "distributed" => VerticalAlignment.Distributed,
            _ => VerticalAlignment.Bottom,
        },
        WrapText = alignment.WrapText?.Value ?? false,
        Indent = (int)(alignment.Indent?.Value ?? 0),
        TextRotation = (int)(alignment.TextRotation?.Value ?? 0),
        ShrinkToFit = alignment.ShrinkToFit?.Value ?? false,
        ReadingOrder = (int)(alignment.ReadingOrder?.Value ?? 0),
    };

    private static DifferentialFormat ReadDifferential(X.DifferentialFormat dxf, Dictionary<uint, string> formats)
    {
        var font = dxf.Font;
        var fill = dxf.Fill?.PatternFill;
        var border = dxf.Border;
        return new DifferentialFormat
        {
            NumberFormat = dxf.NumberingFormat?.FormatCode?.Value,
            Bold = font?.Bold is { } bold ? bold.Val?.Value ?? true : null,
            Italic = font?.Italic is { } italic ? italic.Val?.Value ?? true : null,
            Strikethrough = font?.Strike is { } strike ? strike.Val?.Value ?? true : null,
            Underline = font?.Underline is not null ? UnderlineKind.Single : null,
            FontColor = ReadColor(font?.Color),
            FillColor = ReadColor(fill?.BackgroundColor) ?? ReadColor(fill?.ForegroundColor),
            BorderColor = ReadColor(border?.LeftBorder?.Color ?? border?.BottomBorder?.Color),
            Xml = dxf.OuterXml,
        };
    }

    // ---------------------------------------------------------------- writing

    /// <summary>Writes the stylesheet of <paramref name="styles"/> (xf index = style id).</summary>
    public static X.Stylesheet Write(StyleTable styles)
    {
        var numberFormats = new List<(int Id, string Code)>();
        var fonts = new List<CellFont>();
        var fills = new List<CellFill> { CellFill.None, CellFill.Gray125 };
        var borders = new List<CellBorders> { CellBorders.None };

        int FormatId(string code)
        {
            if (BuiltInFormats.IdOf(code) is { } builtIn)
            {
                return builtIn;
            }

            var existing = numberFormats.FindIndex(format => format.Code == code);
            if (existing >= 0)
            {
                return numberFormats[existing].Id;
            }

            var id = 164 + numberFormats.Count;
            numberFormats.Add((id, code));
            return id;
        }

        int IndexOf<T>(List<T> list, T item)
        {
            var index = list.IndexOf(item);
            if (index >= 0)
            {
                return index;
            }

            list.Add(item);
            return list.Count - 1;
        }

        X.CellFormat Xf(CellStyle style, bool named)
        {
            var xf = new X.CellFormat
            {
                NumberFormatId = (uint)FormatId(style.NumberFormat),
                FontId = (uint)IndexOf(fonts, style.Font),
                FillId = (uint)IndexOf(fills, style.Fill),
                BorderId = (uint)IndexOf(borders, style.Borders),
            };
            if (!named)
            {
                xf.FormatId = (uint)style.ParentStyle;
                if (xf.NumberFormatId.Value != 0)
                {
                    xf.ApplyNumberFormat = true;
                }

                if (xf.FontId.Value != 0)
                {
                    xf.ApplyFont = true;
                }

                if (xf.FillId.Value != 0)
                {
                    xf.ApplyFill = true;
                }

                if (xf.BorderId.Value != 0)
                {
                    xf.ApplyBorder = true;
                }
            }

            if (style.QuotePrefix)
            {
                xf.QuotePrefix = true;
            }

            if (style.Alignment != CellAlignment.Default)
            {
                xf.Alignment = WriteAlignment(style.Alignment);
                if (!named)
                {
                    xf.ApplyAlignment = true;
                }
            }

            if (style.Protection != CellProtection.Default)
            {
                xf.Protection = new X.Protection { Locked = style.Protection.Locked, Hidden = style.Protection.Hidden };
                if (!named)
                {
                    xf.ApplyProtection = true;
                }
            }

            return xf;
        }

        fonts.Add(styles.Default.Font);
        var namedFormats = styles.NamedStyleFormats.Count > 0 ? styles.NamedStyleFormats : [CellStyle.Default with { Font = styles.Default.Font }];
        var cellStyleXfs = new X.CellStyleFormats(namedFormats.Select(style => Xf(style, named: true)));
        var cellXfs = new X.CellFormats(styles.All.Select(style => Xf(style with { ParentStyle = Math.Min(style.ParentStyle, namedFormats.Count - 1) }, named: false)));

        var stylesheet = new X.Stylesheet();
        if (numberFormats.Count > 0)
        {
            stylesheet.NumberingFormats = new X.NumberingFormats(numberFormats.Select(format =>
                new X.NumberingFormat { NumberFormatId = (uint)format.Id, FormatCode = format.Code }))
            { Count = (uint)numberFormats.Count };
        }

        stylesheet.Fonts = new X.Fonts(fonts.Select(WriteFont)) { Count = (uint)fonts.Count };
        stylesheet.Fills = new X.Fills(fills.Select(WriteFill)) { Count = (uint)fills.Count };
        stylesheet.Borders = new X.Borders(borders.Select(WriteBorders)) { Count = (uint)borders.Count };
        cellStyleXfs.Count = (uint)cellStyleXfs.ChildElements.Count;
        stylesheet.CellStyleFormats = cellStyleXfs;
        cellXfs.Count = (uint)cellXfs.ChildElements.Count;
        stylesheet.CellFormats = cellXfs;

        var cellStyles = new X.CellStyles();
        if (styles.NamedStyles.Count > 0)
        {
            foreach (var named in styles.NamedStyles)
            {
                var element = new X.CellStyle { Name = named.Name, FormatId = (uint)Math.Min(named.FormatIndex, namedFormats.Count - 1) };
                if (named.BuiltInId is { } builtIn)
                {
                    element.BuiltinId = (uint)builtIn;
                }

                cellStyles.Append(element);
            }
        }
        else
        {
            cellStyles.Append(new X.CellStyle { Name = "Standard", FormatId = 0, BuiltinId = 0 });
        }

        cellStyles.Count = (uint)cellStyles.ChildElements.Count;
        stylesheet.CellStyles = cellStyles;

        var differential = new X.DifferentialFormats();
        foreach (var dxf in styles.DifferentialFormats)
        {
            differential.Append(dxf.Xml is { } xml ? new X.DifferentialFormat(xml) : WriteDifferential(dxf));
        }

        differential.Count = (uint)differential.ChildElements.Count;
        stylesheet.DifferentialFormats = differential;
        foreach (var xml in styles.PreservedXml)
        {
            stylesheet.Append(Parse(xml));
        }

        return stylesheet;
    }

    private static OpenXmlElement Parse(string xml)
    {
        var name = xml.TrimStart('<').Split([' ', '>', '/'], 2)[0];
        var local = name.Contains(':') ? name[(name.IndexOf(':') + 1)..] : name;
        return local switch
        {
            "tableStyles" => new X.TableStyles(xml),
            "colors" => new X.Colors(xml),
            "extLst" => new X.StylesheetExtensionList(xml),
            _ => new OpenXmlUnknownElement(xml),
        };
    }

    internal static T WriteColor<T>(CalcColor color)
        where T : X.ColorType, new()
    {
        var element = new T();
        switch (color.Kind)
        {
            case ColorKind.Auto:
                element.Auto = true;
                break;
            case ColorKind.Rgb:
                element.Rgb = color.ToHex();
                break;
            case ColorKind.Theme:
                element.Theme = (uint)color.Index;
                break;
            case ColorKind.Indexed:
                element.Indexed = (uint)color.Index;
                break;
        }

        if (color.Tint != 0)
        {
            element.Tint = color.Tint;
        }

        return element;
    }

    private static X.Font WriteFont(CellFont font)
    {
        var element = new X.Font();
        if (font.Bold)
        {
            element.Append(new X.Bold());
        }

        if (font.Italic)
        {
            element.Append(new X.Italic());
        }

        if (font.Strikethrough)
        {
            element.Append(new X.Strike());
        }

        if (font.Outline)
        {
            element.Append(new X.Outline());
        }

        if (font.Shadow)
        {
            element.Append(new X.Shadow());
        }

        if (font.Underline != UnderlineKind.None)
        {
            element.Append(new X.Underline
            {
                Val = font.Underline switch
                {
                    UnderlineKind.Double => X.UnderlineValues.Double,
                    UnderlineKind.SingleAccounting => X.UnderlineValues.SingleAccounting,
                    UnderlineKind.DoubleAccounting => X.UnderlineValues.DoubleAccounting,
                    _ => X.UnderlineValues.Single,
                },
            });
        }

        if (font.Script != FontScript.Baseline)
        {
            element.Append(new X.VerticalTextAlignment
            {
                Val = font.Script == FontScript.Superscript ? X.VerticalAlignmentRunValues.Superscript : X.VerticalAlignmentRunValues.Subscript,
            });
        }

        element.Append(new X.FontSize { Val = font.Size });
        if (font.Color is { } color)
        {
            element.Append(WriteColor<X.Color>(color));
        }

        element.Append(new X.FontName { Val = font.Name });
        if (font.Family is { } family)
        {
            element.Append(new X.FontFamilyNumbering { Val = family });
        }

        if (font.Charset is { } charset)
        {
            element.Append(new X.FontCharSet { Val = charset });
        }

        if (font.Scheme is { } scheme)
        {
            element.Append(new X.FontScheme { Val = scheme == "major" ? X.FontSchemeValues.Major : scheme == "minor" ? X.FontSchemeValues.Minor : X.FontSchemeValues.None });
        }

        return element;
    }

    private static X.Fill WriteFill(CellFill fill)
    {
        if (fill.GradientXml is { } gradient)
        {
            return new X.Fill(new X.GradientFill(gradient));
        }

        var pattern = new X.PatternFill { PatternType = new EnumValue<X.PatternValues>(new X.PatternValues(PatternName(fill.Pattern))) };
        if (fill.Foreground is { } foreground)
        {
            pattern.ForegroundColor = WriteColor<X.ForegroundColor>(foreground);
        }

        if (fill.Background is { } background)
        {
            pattern.BackgroundColor = WriteColor<X.BackgroundColor>(background);
        }

        return new X.Fill(pattern);
    }

    private static X.Border WriteBorders(CellBorders borders)
    {
        var border = new X.Border();
        if (borders.DiagonalUp)
        {
            border.DiagonalUp = true;
        }

        if (borders.DiagonalDown)
        {
            border.DiagonalDown = true;
        }

        border.Append(WriteEdge(new X.LeftBorder(), borders.Left));
        border.Append(WriteEdge(new X.RightBorder(), borders.Right));
        border.Append(WriteEdge(new X.TopBorder(), borders.Top));
        border.Append(WriteEdge(new X.BottomBorder(), borders.Bottom));
        border.Append(WriteEdge(new X.DiagonalBorder(), borders.Diagonal));
        return border;
    }

    private static X.BorderPropertiesType WriteEdge(X.BorderPropertiesType element, BorderEdge edge)
    {
        if (!edge.IsVisible)
        {
            return element;
        }

        var name = edge.Style.ToString();
        element.Style = new EnumValue<X.BorderStyleValues>(new X.BorderStyleValues(char.ToLowerInvariant(name[0]) + name[1..]));
        if (edge.Color is { } color)
        {
            element.Color = WriteColor<X.Color>(color);
        }

        return element;
    }

    private static X.Alignment WriteAlignment(CellAlignment alignment)
    {
        var element = new X.Alignment();
        if (alignment.Horizontal != HorizontalAlignment.General)
        {
            var name = alignment.Horizontal.ToString();
            element.Horizontal = new EnumValue<X.HorizontalAlignmentValues>(new X.HorizontalAlignmentValues(char.ToLowerInvariant(name[0]) + name[1..]));
        }

        if (alignment.Vertical != VerticalAlignment.Bottom)
        {
            var name = alignment.Vertical.ToString();
            element.Vertical = new EnumValue<X.VerticalAlignmentValues>(new X.VerticalAlignmentValues(char.ToLowerInvariant(name[0]) + name[1..]));
        }

        if (alignment.WrapText)
        {
            element.WrapText = true;
        }

        if (alignment.Indent != 0)
        {
            element.Indent = (uint)alignment.Indent;
        }

        if (alignment.TextRotation != 0)
        {
            element.TextRotation = (uint)alignment.TextRotation;
        }

        if (alignment.ShrinkToFit)
        {
            element.ShrinkToFit = true;
        }

        if (alignment.ReadingOrder != 0)
        {
            element.ReadingOrder = (uint)alignment.ReadingOrder;
        }

        return element;
    }

    private static X.DifferentialFormat WriteDifferential(DifferentialFormat format)
    {
        var dxf = new X.DifferentialFormat();
        if (format.Bold is not null || format.Italic is not null || format.Strikethrough is not null || format.Underline is not null || format.FontColor is not null)
        {
            var font = new X.Font();
            if (format.Bold is { } bold)
            {
                font.Append(new X.Bold { Val = bold });
            }

            if (format.Italic is { } italic)
            {
                font.Append(new X.Italic { Val = italic });
            }

            if (format.Strikethrough is { } strike)
            {
                font.Append(new X.Strike { Val = strike });
            }

            if (format.Underline is not null)
            {
                font.Append(new X.Underline());
            }

            if (format.FontColor is { } color)
            {
                font.Append(WriteColor<X.Color>(color));
            }

            dxf.Append(font);
        }

        if (format.NumberFormat is { } code)
        {
            dxf.Append(new X.NumberingFormat { NumberFormatId = 300, FormatCode = code });
        }

        if (format.FillColor is { } fill)
        {
            dxf.Append(new X.Fill(new X.PatternFill { BackgroundColor = WriteColor<X.BackgroundColor>(fill) }));
        }

        if (format.BorderColor is { } borderColor)
        {
            var style = format.BorderStyle ?? BorderStyle.Thin;
            var edge = new BorderEdge(style, borderColor);
            dxf.Append(WriteBorders(CellBorders.All(edge.Style, borderColor)));
        }

        _ = MainNamespace;
        _ = CultureInfo.InvariantCulture;
        return dxf;
    }
}
