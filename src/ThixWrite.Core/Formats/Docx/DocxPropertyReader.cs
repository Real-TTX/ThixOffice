using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Reads run, paragraph and section properties into the model. The readers take the children of any properties element
/// (w:rPr of runs, paragraph marks, styles, defaults and list levels; w:pPr of paragraphs, styles and defaults), so one
/// mapping serves every place. Children the model does not know go into UnknownXml, unchanged.
/// </summary>
internal static class DocxPropertyReader
{
    public static CharacterProperties ReadCharacter(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return CharacterProperties.Empty;
        }

        var result = new CharacterProperties();
        var unknown = new List<OpenXmlElement>();
        foreach (var child in properties.ChildElements)
        {
            switch (child)
            {
                case W.RunStyle style:
                    result = result with { StyleId = style.Val?.Value };
                    break;
                case W.RunFonts fonts:
                    result = ReadFonts(result, fonts);
                    break;
                case W.Bold:
                    result = result with { Bold = DocxValues.ParseOnOff(child) };
                    break;
                case W.Italic:
                    result = result with { Italic = DocxValues.ParseOnOff(child) };
                    break;
                case W.Strike:
                    result = result with { Strikethrough = DocxValues.ParseOnOff(child) };
                    break;
                case W.DoubleStrike:
                    result = result with { DoubleStrikethrough = DocxValues.ParseOnOff(child) };
                    break;
                case W.Color:
                    result = result with
                    {
                        Color = new TextColor(
                            DocxValues.AttributeValue(child, "val") ?? "auto",
                            DocxValues.AttributeValue(child, "themeColor"),
                            DocxValues.AttributeValue(child, "themeTint"),
                            DocxValues.AttributeValue(child, "themeShade")),
                    };
                    break;
                case W.FontSize when DocxValues.ParseInteger(DocxValues.AttributeValue(child, "val")) is { } halfPoints:
                    result = result with { FontSize = halfPoints / 2.0 };
                    break;
                case W.Highlight when ParseHighlight(DocxValues.AttributeValue(child, "val")) is { } highlight:
                    result = result with { Highlight = highlight };
                    break;
                case W.Underline:
                    result = result with { Underline = ParseUnderline(DocxValues.AttributeValue(child, "val")) };
                    break;
                case W.VerticalTextAlignment when ParseVerticalPosition(DocxValues.AttributeValue(child, "val")) is { } position:
                    result = result with { VerticalPosition = position };
                    break;
                case W.FontSize or W.Highlight or W.VerticalTextAlignment:
                    // A modelled property with an invalid value: dropped (writing it back would make the file invalid).
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return result with { UnknownXml = XmlFragments.Join(unknown) };
    }

    public static ParagraphProperties ReadParagraph(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return ParagraphProperties.Empty;
        }

        var result = new ParagraphProperties();
        var unknown = new List<OpenXmlElement>();
        foreach (var child in properties.ChildElements)
        {
            switch (child)
            {
                case W.ParagraphStyleId style:
                    result = result with { StyleId = style.Val?.Value };
                    break;
                case W.KeepNext:
                    result = result with { KeepWithNext = DocxValues.ParseOnOff(child) };
                    break;
                case W.KeepLines:
                    result = result with { KeepLinesTogether = DocxValues.ParseOnOff(child) };
                    break;
                case W.PageBreakBefore:
                    result = result with { PageBreakBefore = DocxValues.ParseOnOff(child) };
                    break;
                case W.WidowControl:
                    result = result with { WidowControl = DocxValues.ParseOnOff(child) };
                    break;
                case W.ContextualSpacing:
                    result = result with { ContextualSpacing = DocxValues.ParseOnOff(child) };
                    break;
                case W.NumberingProperties numbering when ReadNumbering(numbering) is { } reference:
                    result = result with { Numbering = reference };
                    break;
                case W.Tabs tabs:
                    result = result with { Tabs = new EquatableArray<TabStop>(tabs.Elements<W.TabStop>().Select(ReadTab).OfType<TabStop>()) };
                    break;
                case W.SpacingBetweenLines spacing:
                    result = ReadSpacing(result, spacing);
                    break;
                case W.Indentation indentation:
                    result = ReadIndentation(result, indentation);
                    break;
                case W.Justification when ParseAlignment(DocxValues.AttributeValue(child, "val")) is { } alignment:
                    result = result with { Alignment = alignment };
                    break;
                case W.OutlineLevel when DocxValues.ParseInteger(DocxValues.AttributeValue(child, "val")) is { } level:
                    result = result with { OutlineLevel = (int)Math.Clamp(level, 0, 9) };
                    break;
                case W.SectionProperties or W.ParagraphMarkRunProperties:
                    // Read by the paragraph reader (section break, paragraph mark).
                    break;
                case W.Justification or W.OutlineLevel:
                    // A modelled property with an invalid value: dropped (writing it back would make the file invalid).
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return result with { UnknownXml = XmlFragments.Join(unknown) };
    }

    /// <summary>
    /// A section's properties; <paramref name="readStory"/> reads the header or footer of a w:headerReference / w:footerReference
    /// (relationship id, footer?) – a reference it cannot read stays as unknown XML.
    /// </summary>
    public static SectionProperties ReadSection(W.SectionProperties? properties, Func<string, bool, Story?>? readStory = null)
    {
        if (properties is null)
        {
            return SectionProperties.Default;
        }

        var result = new SectionProperties();
        var unknown = new List<OpenXmlElement>();
        foreach (var child in properties.ChildElements)
        {
            switch (child)
            {
                case W.PageSize size:
                    result = result with
                    {
                        PageWidth = DocxValues.ParseTwips(DocxValues.AttributeValue(size, "w")) ?? result.PageWidth,
                        PageHeight = DocxValues.ParseTwips(DocxValues.AttributeValue(size, "h")) ?? result.PageHeight,
                        Orientation = DocxValues.AttributeValue(size, "orient") == "landscape" ? PageOrientation.Landscape : PageOrientation.Portrait,
                    };
                    break;
                case W.PageMargin margin:
                    result = result with
                    {
                        MarginTop = DocxValues.ParseTwips(DocxValues.AttributeValue(margin, "top")) ?? result.MarginTop,
                        MarginBottom = DocxValues.ParseTwips(DocxValues.AttributeValue(margin, "bottom")) ?? result.MarginBottom,
                        MarginLeft = DocxValues.ParseTwips(DocxValues.AttributeValue(margin, "left")) ?? result.MarginLeft,
                        MarginRight = DocxValues.ParseTwips(DocxValues.AttributeValue(margin, "right")) ?? result.MarginRight,
                        HeaderDistance = DocxValues.ParseTwips(DocxValues.AttributeValue(margin, "header")) ?? result.HeaderDistance,
                        FooterDistance = DocxValues.ParseTwips(DocxValues.AttributeValue(margin, "footer")) ?? result.FooterDistance,
                        Gutter = DocxValues.ParseTwips(DocxValues.AttributeValue(margin, "gutter")) ?? 0,
                    };
                    break;
                case W.Columns columns when IsSimpleColumns(columns):
                    result = result with
                    {
                        ColumnCount = (int)Math.Max(1, DocxValues.ParseInteger(DocxValues.AttributeValue(columns, "num")) ?? 1),
                        ColumnSpacing = DocxValues.ParseTwips(DocxValues.AttributeValue(columns, "space")) ?? result.ColumnSpacing,
                    };
                    break;
                case W.SectionType type:
                    result = result with { Start = ParseSectionStart(DocxValues.AttributeValue(type, "val")) };
                    break;
                case W.TitlePage:
                    result = result with { TitlePage = DocxValues.ParseOnOff(child) };
                    break;
                case W.HeaderReference or W.FooterReference when readStory is not null && ReadReference(child, readStory) is { } reference:
                    result = reference.Footer
                        ? result with { Footers = result.Footers.With(reference.Kind, reference.Story) }
                        : result with { Headers = result.Headers.With(reference.Kind, reference.Story) };
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return result with { UnknownXml = XmlFragments.Join(unknown) };
    }

    private static (HeaderFooterKind Kind, bool Footer, Story Story)? ReadReference(OpenXmlElement reference, Func<string, bool, Story?> readStory)
    {
        var footer = reference is W.FooterReference;
        var id = DocxValues.AttributeValue(reference, "id");
        if (id is null || readStory(id, footer) is not { } story)
        {
            return null;
        }

        var kind = DocxValues.AttributeValue(reference, "type") switch
        {
            "first" => HeaderFooterKind.First,
            "even" => HeaderFooterKind.Even,
            _ => HeaderFooterKind.Default,
        };
        return (kind, footer, story);
    }

    /// <summary>The (family, theme) pair a w:rFonts says, as the reader and the writer understand it.</summary>
    public static (string? Family, ThemeFontSlot? Theme) InterpretFonts(W.RunFonts fonts)
    {
        var family = DocxValues.AttributeValue(fonts, "ascii") ?? DocxValues.AttributeValue(fonts, "hAnsi");
        var theme = DocxValues.AttributeValue(fonts, "asciiTheme") ?? DocxValues.AttributeValue(fonts, "hAnsiTheme");
        ThemeFontSlot? slot = theme switch
        {
            null => null,
            _ when theme.StartsWith("major", StringComparison.Ordinal) => ThemeFontSlot.Major,
            _ when theme.StartsWith("minor", StringComparison.Ordinal) => ThemeFontSlot.Minor,
            _ => null,
        };
        return (family, slot);
    }

    private static CharacterProperties ReadFonts(CharacterProperties result, W.RunFonts fonts)
    {
        var (family, theme) = InterpretFonts(fonts);
        var canonical = DocxPropertyWriter.CreateFonts(family, theme);
        var isCanonical = canonical is not null && SameAttributes(canonical, fonts);
        return result with { FontFamily = family, FontTheme = theme, FontsXml = isCanonical ? null : XmlFragments.Canonical(fonts) };
    }

    private static bool SameAttributes(OpenXmlElement first, OpenXmlElement second)
    {
        static HashSet<string> Set(OpenXmlElement element) =>
            element.GetAttributes().Select(attribute => $"{attribute.NamespaceUri}|{attribute.LocalName}={attribute.Value}").ToHashSet();

        return Set(first).SetEquals(Set(second));
    }

    private static NumberingReference? ReadNumbering(W.NumberingProperties numbering)
    {
        var id = numbering.NumberingId is { } numberingId ? DocxValues.ParseInteger(DocxValues.AttributeValue(numberingId, "val")) : null;
        if (id is null)
        {
            return null;
        }

        var level = numbering.NumberingLevelReference is { } levelReference
            ? DocxValues.ParseInteger(DocxValues.AttributeValue(levelReference, "val")) ?? 0
            : 0;
        return new NumberingReference((int)id, (int)Math.Clamp(level, 0, 8));
    }

    private static TabStop? ReadTab(W.TabStop tab)
    {
        if (DocxValues.ParseTwips(DocxValues.AttributeValue(tab, "pos")) is not { } position)
        {
            return null;
        }

        var alignment = DocxValues.AttributeValue(tab, "val") switch
        {
            "center" => TabAlignment.Center,
            "right" or "end" => TabAlignment.Right,
            "decimal" => TabAlignment.Decimal,
            "bar" => TabAlignment.Bar,
            "clear" => TabAlignment.Clear,
            _ => TabAlignment.Left,
        };
        var leader = DocxValues.AttributeValue(tab, "leader") switch
        {
            "dot" => TabLeader.Dot,
            "hyphen" => TabLeader.Hyphen,
            "underscore" => TabLeader.Underscore,
            "middleDot" => TabLeader.MiddleDot,
            "heavy" => TabLeader.Heavy,
            _ => TabLeader.None,
        };
        return new TabStop(position, alignment, leader);
    }

    private static ParagraphProperties ReadSpacing(ParagraphProperties result, W.SpacingBetweenLines spacing)
    {
        result = result with
        {
            SpacingBefore = DocxValues.ParseTwips(DocxValues.AttributeValue(spacing, "before")) ?? result.SpacingBefore,
            SpacingAfter = DocxValues.ParseTwips(DocxValues.AttributeValue(spacing, "after")) ?? result.SpacingAfter,
        };
        if (DocxValues.ParseInteger(DocxValues.AttributeValue(spacing, "line")) is not { } line)
        {
            return result;
        }

        var lineSpacing = DocxValues.AttributeValue(spacing, "lineRule") switch
        {
            "exact" => new LineSpacing(LineSpacingRule.Exact, DocxValues.TwipsToPoints(Math.Abs(line))),
            "atLeast" => new LineSpacing(LineSpacingRule.AtLeast, DocxValues.TwipsToPoints(line)),
            _ => new LineSpacing(LineSpacingRule.Multiple, line / 240.0),
        };
        return result with { LineSpacing = lineSpacing };
    }

    private static ParagraphProperties ReadIndentation(ParagraphProperties result, W.Indentation indentation)
    {
        var left = DocxValues.ParseTwips(DocxValues.AttributeValue(indentation, "left") ?? DocxValues.AttributeValue(indentation, "start"));
        var right = DocxValues.ParseTwips(DocxValues.AttributeValue(indentation, "right") ?? DocxValues.AttributeValue(indentation, "end"));
        var hanging = DocxValues.ParseTwips(DocxValues.AttributeValue(indentation, "hanging"));
        var firstLine = DocxValues.ParseTwips(DocxValues.AttributeValue(indentation, "firstLine"));
        return result with
        {
            IndentLeft = left ?? result.IndentLeft,
            IndentRight = right ?? result.IndentRight,
            FirstLineIndent = hanging is { } value ? -value : firstLine ?? result.FirstLineIndent,
        };
    }

    private static bool IsSimpleColumns(W.Columns columns) =>
        !columns.HasChildren && columns.GetAttributes().All(attribute => attribute.LocalName is "num" or "space");

    public static ParagraphAlignment? ParseAlignment(string? value) => value switch
    {
        "left" or "start" => ParagraphAlignment.Left,
        "center" => ParagraphAlignment.Center,
        "right" or "end" => ParagraphAlignment.Right,
        "both" or "distribute" or "mediumKashida" or "highKashida" or "lowKashida" or "thaiDistribute" => ParagraphAlignment.Justify,
        _ => null,
    };

    private static SectionStart ParseSectionStart(string? value) => value switch
    {
        "continuous" => SectionStart.Continuous,
        "evenPage" => SectionStart.EvenPage,
        "oddPage" => SectionStart.OddPage,
        "nextColumn" => SectionStart.NextColumn,
        _ => SectionStart.NextPage,
    };

    private static VerticalPosition? ParseVerticalPosition(string? value) => value switch
    {
        "superscript" => VerticalPosition.Superscript,
        "subscript" => VerticalPosition.Subscript,
        "baseline" => VerticalPosition.Baseline,
        _ => null,
    };

    private static HighlightColor? ParseHighlight(string? value) => value switch
    {
        "none" => HighlightColor.None,
        "yellow" => HighlightColor.Yellow,
        "green" => HighlightColor.BrightGreen,
        "cyan" => HighlightColor.Cyan,
        "magenta" => HighlightColor.Magenta,
        "blue" => HighlightColor.Blue,
        "red" => HighlightColor.Red,
        "darkBlue" => HighlightColor.DarkBlue,
        "darkCyan" => HighlightColor.DarkCyan,
        "darkGreen" => HighlightColor.DarkGreen,
        "darkMagenta" => HighlightColor.DarkMagenta,
        "darkRed" => HighlightColor.DarkRed,
        "darkYellow" => HighlightColor.DarkYellow,
        "darkGray" => HighlightColor.DarkGray,
        "lightGray" => HighlightColor.LightGray,
        "black" => HighlightColor.Black,
        "white" => HighlightColor.White,
        _ => null,
    };

    private static UnderlineStyle ParseUnderline(string? value) => value switch
    {
        null => UnderlineStyle.Single,
        "none" => UnderlineStyle.None,
        "words" => UnderlineStyle.Words,
        "double" => UnderlineStyle.Double,
        "thick" => UnderlineStyle.Thick,
        "dotted" => UnderlineStyle.Dotted,
        "dottedHeavy" => UnderlineStyle.DottedHeavy,
        "dash" => UnderlineStyle.Dash,
        "dashedHeavy" => UnderlineStyle.DashedHeavy,
        "dashLong" => UnderlineStyle.DashLong,
        "dashLongHeavy" => UnderlineStyle.DashLongHeavy,
        "dotDash" => UnderlineStyle.DotDash,
        "dashDotHeavy" => UnderlineStyle.DashDotHeavy,
        "dotDotDash" => UnderlineStyle.DotDotDash,
        "dashDotDotHeavy" => UnderlineStyle.DashDotDotHeavy,
        "wave" => UnderlineStyle.Wave,
        "wavyHeavy" => UnderlineStyle.WavyHeavy,
        "wavyDouble" => UnderlineStyle.WavyDouble,
        _ => UnderlineStyle.Single,
    };

    /// <summary>The w:u value of an underline style (inverse of the reader's mapping).</summary>
    public static string UnderlineValue(UnderlineStyle style) => style switch
    {
        UnderlineStyle.None => "none",
        _ => char.ToLowerInvariant(style.ToString()[0]) + style.ToString()[1..],
    };

    /// <summary>The w:highlight value of a highlight colour.</summary>
    public static string HighlightValue(HighlightColor color) => color switch
    {
        HighlightColor.BrightGreen => "green",
        _ => char.ToLowerInvariant(color.ToString()[0]) + color.ToString()[1..],
    };
}
