using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Writes run, paragraph and section properties into any properties element of the right kind; every child goes to its
/// schema position (AddChild), the preserved UnknownXml children too.
/// </summary>
internal static class DocxPropertyWriter
{
    /// <summary>Fills <paramref name="target"/> (w:rPr of any kind); returns it, or null when nothing was written.</summary>
    public static T? WriteCharacter<T>(CharacterProperties properties, T target) where T : OpenXmlCompositeElement
    {
        if (properties.IsEmpty)
        {
            return null;
        }

        XmlFragments.AddTo(target, properties.UnknownXml);
        if (properties.StyleId is not null)
        {
            Add(target, Element<W.RunStyle>(("val", properties.StyleId)));
        }

        if (WriteFonts(properties) is { } fonts)
        {
            Add(target, fonts);
        }

        AddOnOff<W.Bold>(target, properties.Bold);
        AddOnOff<W.Italic>(target, properties.Italic);
        AddOnOff<W.Strike>(target, properties.Strikethrough);
        AddOnOff<W.DoubleStrike>(target, properties.DoubleStrikethrough);
        if (properties.Color is { } color)
        {
            Add(target, Element<W.Color>(("val", color.Value), ("themeColor", color.ThemeColor), ("themeTint", color.ThemeTint),
                ("themeShade", color.ThemeShade)));
        }

        if (properties.FontSize is { } size)
        {
            Add(target, Element<W.FontSize>(("val", Math.Round(size * 2).ToString(System.Globalization.CultureInfo.InvariantCulture))));
        }

        if (properties.Highlight is { } highlight)
        {
            Add(target, Element<W.Highlight>(("val", DocxPropertyReader.HighlightValue(highlight))));
        }

        if (properties.Underline is { } underline)
        {
            Add(target, Element<W.Underline>(("val", DocxPropertyReader.UnderlineValue(underline))));
        }

        if (properties.VerticalPosition is { } position)
        {
            var value = position switch
            {
                VerticalPosition.Superscript => "superscript",
                VerticalPosition.Subscript => "subscript",
                _ => "baseline",
            };
            Add(target, Element<W.VerticalTextAlignment>(("val", value)));
        }

        return target.HasChildren ? target : null;
    }

    /// <summary>
    /// Fills <paramref name="target"/> (w:pPr of any kind) with the paragraph properties, the paragraph mark's formatting and
    /// the section break; returns it, or null when nothing was written.
    /// </summary>
    public static T? WriteParagraph<T>(
        ParagraphProperties properties,
        T target,
        CharacterProperties? markProperties = null,
        SectionProperties? sectionBreak = null) where T : OpenXmlCompositeElement
    {
        XmlFragments.AddTo(target, properties.UnknownXml);
        if (properties.StyleId is not null)
        {
            Add(target, Element<W.ParagraphStyleId>(("val", properties.StyleId)));
        }

        AddOnOff<W.KeepNext>(target, properties.KeepWithNext);
        AddOnOff<W.KeepLines>(target, properties.KeepLinesTogether);
        AddOnOff<W.PageBreakBefore>(target, properties.PageBreakBefore);
        AddOnOff<W.WidowControl>(target, properties.WidowControl);
        if (properties.Numbering is { } numbering)
        {
            Add(target, new W.NumberingProperties(
                Element<W.NumberingLevelReference>(("val", Integer(numbering.Level))),
                Element<W.NumberingId>(("val", Integer(numbering.NumberingId)))));
        }

        if (properties.Tabs is { } tabs)
        {
            Add(target, new W.Tabs(tabs.Select(WriteTab)));
        }

        if (WriteSpacing(properties) is { } spacing)
        {
            Add(target, spacing);
        }

        if (WriteIndentation(properties.IndentLeft, properties.IndentRight, properties.FirstLineIndent) is { } indentation)
        {
            Add(target, indentation);
        }

        AddOnOff<W.ContextualSpacing>(target, properties.ContextualSpacing);
        if (properties.Alignment is { } alignment)
        {
            Add(target, Element<W.Justification>(("val", AlignmentValue(alignment))));
        }

        if (properties.OutlineLevel is { } level)
        {
            Add(target, Element<W.OutlineLevel>(("val", Integer(level))));
        }

        if (markProperties is not null && WriteCharacter(markProperties, new W.ParagraphMarkRunProperties()) is { } mark)
        {
            Add(target, mark);
        }

        if (sectionBreak is not null)
        {
            Add(target, WriteSection(sectionBreak));
        }

        return target.HasChildren ? target : null;
    }

    public static W.SectionProperties WriteSection(SectionProperties section)
    {
        var target = new W.SectionProperties();
        XmlFragments.AddTo(target, section.UnknownXml);
        if (section.Start != SectionStart.NextPage)
        {
            var value = section.Start switch
            {
                SectionStart.Continuous => "continuous",
                SectionStart.EvenPage => "evenPage",
                SectionStart.OddPage => "oddPage",
                _ => "nextColumn",
            };
            Add(target, Element<W.SectionType>(("val", value)));
        }

        Add(target, Element<W.PageSize>(
            ("w", Twips(section.PageWidth)),
            ("h", Twips(section.PageHeight)),
            ("orient", section.Orientation == PageOrientation.Landscape ? "landscape" : null)));
        Add(target, Element<W.PageMargin>(
            ("top", Twips(section.MarginTop)),
            ("right", Twips(section.MarginRight)),
            ("bottom", Twips(section.MarginBottom)),
            ("left", Twips(section.MarginLeft)),
            ("header", Twips(section.HeaderDistance)),
            ("footer", Twips(section.FooterDistance)),
            ("gutter", Twips(section.Gutter))));
        if (target.GetFirstChild<W.Columns>() is null)
        {
            Add(target, Element<W.Columns>(("space", Twips(section.ColumnSpacing)), ("num", section.ColumnCount > 1 ? Integer(section.ColumnCount) : null)));
        }

        if (section.TitlePage)
        {
            Add(target, new W.TitlePage());
        }

        return target;
    }

    /// <summary>The w:rFonts ThixWrite writes for a family / theme font (null when both are null).</summary>
    public static W.RunFonts? CreateFonts(string? family, ThemeFontSlot? theme)
    {
        if (family is null && theme is null)
        {
            return null;
        }

        if (theme is { } slot)
        {
            var themeValue = slot == ThemeFontSlot.Major ? "majorHAnsi" : "minorHAnsi";
            return Element<W.RunFonts>(("ascii", family), ("hAnsi", family), ("asciiTheme", themeValue), ("hAnsiTheme", themeValue));
        }

        return Element<W.RunFonts>(("ascii", family), ("hAnsi", family), ("cs", family));
    }

    public static W.Indentation? WriteIndentation(double? left, double? right, double? firstLine)
    {
        if (left is null && right is null && firstLine is null)
        {
            return null;
        }

        string? hanging = firstLine is < 0 ? Twips(-firstLine.Value) : null;
        string? first = firstLine is >= 0 ? Twips(firstLine.Value) : null;
        return Element<W.Indentation>(
            ("left", left is { } leftValue ? Twips(leftValue) : null),
            ("right", right is { } rightValue ? Twips(rightValue) : null),
            ("firstLine", first),
            ("hanging", hanging));
    }

    public static string AlignmentValue(ParagraphAlignment alignment) => alignment switch
    {
        ParagraphAlignment.Center => "center",
        ParagraphAlignment.Right => "right",
        ParagraphAlignment.Justify => "both",
        _ => "left",
    };

    /// <summary>A typed element with w: attributes (null values are left out).</summary>
    public static T Element<T>(params (string Name, string? Value)[] attributes) where T : OpenXmlElement, new()
    {
        var element = new T();
        foreach (var (name, value) in attributes)
        {
            if (value is not null)
            {
                element.SetAttribute(new OpenXmlAttribute("w", name, DocxValues.WordNamespace, value));
            }
        }

        return element;
    }

    public static string Twips(double points) => Integer(DocxValues.PointsToTwips(points));

    public static string Integer(long value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    public static void Add(OpenXmlCompositeElement target, OpenXmlElement element) => XmlFragments.Add(target, element);

    private static W.RunFonts? WriteFonts(CharacterProperties properties)
    {
        if (properties.FontsXml is not null
            && XmlFragments.Parse<W.RunProperties>(properties.FontsXml).FirstOrDefault() is W.RunFonts original
            && DocxPropertyReader.InterpretFonts(original) == (properties.FontFamily, properties.FontTheme))
        {
            return original;
        }

        return CreateFonts(properties.FontFamily, properties.FontTheme);
    }

    private static void AddOnOff<T>(OpenXmlCompositeElement target, bool? value) where T : OpenXmlElement, new()
    {
        if (value is null)
        {
            return;
        }

        Add(target, value.Value ? new T() : Element<T>(("val", "0")));
    }

    private static W.TabStop WriteTab(TabStop tab)
    {
        var alignment = tab.Alignment switch
        {
            TabAlignment.Center => "center",
            TabAlignment.Right => "right",
            TabAlignment.Decimal => "decimal",
            TabAlignment.Bar => "bar",
            TabAlignment.Clear => "clear",
            _ => "left",
        };
        var leader = tab.Leader switch
        {
            TabLeader.Dot => "dot",
            TabLeader.Hyphen => "hyphen",
            TabLeader.Underscore => "underscore",
            TabLeader.MiddleDot => "middleDot",
            TabLeader.Heavy => "heavy",
            _ => null,
        };
        return Element<W.TabStop>(("val", alignment), ("leader", leader), ("pos", Twips(tab.Position)));
    }

    private static W.SpacingBetweenLines? WriteSpacing(ParagraphProperties properties)
    {
        if (properties.SpacingBefore is null && properties.SpacingAfter is null && properties.LineSpacing is null)
        {
            return null;
        }

        string? line = null;
        string? rule = null;
        if (properties.LineSpacing is { } spacing)
        {
            (line, rule) = spacing.Rule switch
            {
                LineSpacingRule.Exact => (Twips(spacing.Value), "exact"),
                LineSpacingRule.AtLeast => (Twips(spacing.Value), "atLeast"),
                _ => (Integer((long)Math.Round(spacing.Value * 240)), "auto"),
            };
        }

        return Element<W.SpacingBetweenLines>(
            ("before", properties.SpacingBefore is { } before ? Twips(before) : null),
            ("after", properties.SpacingAfter is { } after ? Twips(after) : null),
            ("line", line),
            ("lineRule", rule));
    }
}
