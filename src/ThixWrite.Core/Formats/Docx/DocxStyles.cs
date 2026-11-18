using System.Collections.Immutable;
using DocumentFormat.OpenXml;
using DocumentFormat.OpenXml.Packaging;
using ThixWrite.Core.Model;
using A = DocumentFormat.OpenXml.Drawing;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>styles.xml, numbering.xml, the theme and the settings: read into the model and written back.</summary>
internal static class DocxStyles
{
    public static StyleSheet ReadStyles(W.Styles? styles)
    {
        if (styles is null)
        {
            return StyleSheet.Empty;
        }

        var defaults = styles.DocDefaults;
        var characterDefaults = DocxPropertyReader.ReadCharacter(defaults?.RunPropertiesDefault?.RunPropertiesBaseStyle);
        var paragraphDefaults = DocxPropertyReader.ReadParagraph(defaults?.ParagraphPropertiesDefault?.ParagraphPropertiesBaseStyle);
        var list = styles.Elements<W.Style>().Select(ReadStyle).OfType<Style>().ToList();
        return new StyleSheet(list, paragraphDefaults, characterDefaults, styles.LatentStyles?.OuterXml);
    }

    public static W.Styles WriteStyles(StyleSheet sheet, W.Styles target)
    {
        target.RemoveAllChildren();
        var characterDefaults = DocxPropertyWriter.WriteCharacter(sheet.CharacterDefaults, new W.RunPropertiesBaseStyle());
        var paragraphDefaults = DocxPropertyWriter.WriteParagraph(sheet.ParagraphDefaults, new W.ParagraphPropertiesBaseStyle());
        target.AppendChild(new W.DocDefaults(
            new W.RunPropertiesDefault(characterDefaults ?? new W.RunPropertiesBaseStyle()),
            new W.ParagraphPropertiesDefault(paragraphDefaults ?? new W.ParagraphPropertiesBaseStyle())));
        foreach (var latent in XmlFragments.Parse(sheet.LatentStylesXml, typeof(W.Styles)))
        {
            target.AppendChild(latent);
        }

        foreach (var style in sheet.Styles)
        {
            target.AppendChild(WriteStyle(style));
        }

        return target;
    }

    public static NumberingDefinitions ReadNumbering(W.Numbering? numbering)
    {
        if (numbering is null)
        {
            return NumberingDefinitions.Empty;
        }

        var definitions = new List<AbstractNumbering>();
        var instances = new List<NumberingInstance>();
        var unknown = new List<OpenXmlElement>();
        foreach (var child in numbering.ChildElements)
        {
            switch (child)
            {
                case W.AbstractNum abstractNum when DocxValues.ParseInteger(DocxValues.AttributeValue(abstractNum, "abstractNumId")) is { } id:
                    definitions.Add(ReadDefinition(abstractNum, (int)id));
                    break;
                case W.NumberingInstance instance when ReadInstance(instance) is { } read:
                    instances.Add(read);
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return new NumberingDefinitions(definitions, instances, XmlFragments.Join(unknown));
    }

    public static W.Numbering WriteNumbering(NumberingDefinitions definitions, W.Numbering target)
    {
        target.RemoveAllChildren();
        foreach (var element in XmlFragments.Parse(definitions.UnknownXml, typeof(W.Numbering)))
        {
            XmlFragments.Add(target, element);
        }

        foreach (var definition in definitions.Definitions)
        {
            XmlFragments.Add(target, WriteDefinition(definition));
        }

        foreach (var instance in definitions.Instances)
        {
            XmlFragments.Add(target, WriteInstance(instance));
        }

        return target;
    }

    public static Theme ReadTheme(ThemePart? part)
    {
        var elements = part?.Theme?.ThemeElements;
        if (elements is null)
        {
            return Theme.Office;
        }

        var major = elements.FontScheme?.MajorFont?.LatinFont?.Typeface?.Value;
        var minor = elements.FontScheme?.MinorFont?.LatinFont?.Typeface?.Value;
        var colors = ImmutableDictionary.CreateBuilder<string, string>();
        if (elements.ColorScheme is { } scheme)
        {
            foreach (var color in scheme.ChildElements)
            {
                var value = color.GetFirstChild<A.RgbColorModelHex>()?.Val?.Value
                            ?? color.GetFirstChild<A.SystemColor>()?.LastColor?.Value;
                if (value is not null)
                {
                    colors[color.LocalName] = value;
                }
            }
        }

        return new Theme(
            string.IsNullOrEmpty(major) ? Theme.Office.MajorFont : major,
            string.IsNullOrEmpty(minor) ? Theme.Office.MinorFont : minor,
            colors.Count == 0 ? Theme.Office.Colors : colors.ToImmutable());
    }

    public static DocumentSettings ReadSettings(W.Settings? settings)
    {
        var tab = settings?.GetFirstChild<W.DefaultTabStop>();
        var value = tab is null ? null : DocxValues.ParseTwips(DocxValues.AttributeValue(tab, "val"));
        return value is > 0 ? new DocumentSettings { DefaultTabStop = value.Value } : DocumentSettings.Default;
    }

    /// <summary>Sets the default tab stop in <paramref name="settings"/>, keeping everything else.</summary>
    public static void WriteSettings(DocumentSettings model, W.Settings settings)
    {
        settings.RemoveAllChildren<W.DefaultTabStop>();
        XmlFragments.Add(settings, DocxPropertyWriter.Element<W.DefaultTabStop>(("val", DocxPropertyWriter.Twips(model.DefaultTabStop))));
    }

    /// <summary>The settings of a new document: default tab stop, Word 2013+ compatibility.</summary>
    public static W.Settings CreateSettings(DocumentSettings model)
    {
        var settings = new W.Settings();
        WriteSettings(model, settings);
        XmlFragments.Add(settings, new W.Compatibility(new W.CompatibilitySetting
        {
            Name = W.CompatSettingNameValues.CompatibilityMode,
            Uri = "http://schemas.microsoft.com/office/word",
            Val = "15",
        }));
        return settings;
    }

    private static Style? ReadStyle(W.Style element)
    {
        var id = DocxValues.AttributeValue(element, "styleId");
        if (string.IsNullOrEmpty(id))
        {
            return null;
        }

        var type = DocxValues.AttributeValue(element, "type") switch
        {
            "character" => StyleType.Character,
            "table" => StyleType.Table,
            "numbering" => StyleType.Numbering,
            _ => StyleType.Paragraph,
        };
        var isDefault = DocxValues.AttributeValue(element, "default") is { } defaultValue && IsOn(defaultValue);
        var isCustom = DocxValues.AttributeValue(element, "customStyle") is { } customValue && IsOn(customValue);
        var style = new Style { Id = id, Name = id, Type = type, IsDefault = isDefault, IsCustom = isCustom };
        var unknown = new List<OpenXmlElement>();
        foreach (var child in element.ChildElements)
        {
            var value = DocxValues.AttributeValue(child, "val");
            switch (child)
            {
                case W.StyleName:
                    style = style with { Name = value ?? id };
                    break;
                case W.BasedOn:
                    style = style with { BasedOn = value };
                    break;
                case W.NextParagraphStyle:
                    style = style with { Next = value };
                    break;
                case W.LinkedStyle:
                    style = style with { Link = value };
                    break;
                case W.UIPriority when DocxValues.ParseInteger(value) is { } priority:
                    style = style with { UiPriority = (int)priority };
                    break;
                case W.SemiHidden:
                    style = style with { SemiHidden = DocxValues.ParseOnOff(child) };
                    break;
                case W.UnhideWhenUsed:
                    style = style with { UnhideWhenUsed = DocxValues.ParseOnOff(child) };
                    break;
                case W.PrimaryStyle:
                    style = style with { QuickFormat = DocxValues.ParseOnOff(child) };
                    break;
                case W.StyleParagraphProperties paragraph:
                    style = style with { Paragraph = DocxPropertyReader.ReadParagraph(paragraph) };
                    break;
                case W.StyleRunProperties run:
                    style = style with { Character = DocxPropertyReader.ReadCharacter(run) };
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return style with { UnknownXml = XmlFragments.Join(unknown) };
    }

    private static W.Style WriteStyle(Style style)
    {
        var typeValue = style.Type switch
        {
            StyleType.Character => "character",
            StyleType.Table => "table",
            StyleType.Numbering => "numbering",
            _ => "paragraph",
        };
        var element = DocxPropertyWriter.Element<W.Style>(
            ("type", typeValue),
            ("default", style.IsDefault ? "1" : null),
            ("customStyle", style.IsCustom ? "1" : null),
            ("styleId", style.Id));
        XmlFragments.AddTo(element, style.UnknownXml);
        DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.StyleName>(("val", style.Name)));
        AddValue<W.BasedOn>(element, style.BasedOn);
        AddValue<W.NextParagraphStyle>(element, style.Next);
        AddValue<W.LinkedStyle>(element, style.Link);
        if (style.UiPriority is { } priority)
        {
            DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.UIPriority>(("val", DocxPropertyWriter.Integer(priority))));
        }

        AddFlag<W.SemiHidden>(element, style.SemiHidden);
        AddFlag<W.UnhideWhenUsed>(element, style.UnhideWhenUsed);
        AddFlag<W.PrimaryStyle>(element, style.QuickFormat);
        if (!style.Paragraph.IsEmpty && DocxPropertyWriter.WriteParagraph(style.Paragraph, new W.StyleParagraphProperties()) is { } paragraph)
        {
            DocxPropertyWriter.Add(element, paragraph);
        }

        if (DocxPropertyWriter.WriteCharacter(style.Character, new W.StyleRunProperties()) is { } run)
        {
            DocxPropertyWriter.Add(element, run);
        }

        return element;
    }

    private static AbstractNumbering ReadDefinition(W.AbstractNum element, int id)
    {
        var levels = new List<NumberingLevel>();
        var unknown = new List<OpenXmlElement>();
        string? multiLevelType = null;
        foreach (var child in element.ChildElements)
        {
            switch (child)
            {
                case W.Level level:
                    levels.Add(ReadLevel(level));
                    break;
                case W.MultiLevelType:
                    multiLevelType = DocxValues.AttributeValue(child, "val");
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return new AbstractNumbering
        {
            Id = id,
            Levels = new EquatableArray<NumberingLevel>(levels),
            MultiLevelType = multiLevelType,
            UnknownXml = XmlFragments.Join(unknown),
        };
    }

    private static W.AbstractNum WriteDefinition(AbstractNumbering definition)
    {
        var element = DocxPropertyWriter.Element<W.AbstractNum>(("abstractNumId", DocxPropertyWriter.Integer(definition.Id)));
        XmlFragments.AddTo(element, definition.UnknownXml);
        if (definition.MultiLevelType is not null)
        {
            DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.MultiLevelType>(("val", definition.MultiLevelType)));
        }

        foreach (var level in definition.Levels)
        {
            DocxPropertyWriter.Add(element, WriteLevel(level));
        }

        return element;
    }

    private static NumberingLevel ReadLevel(W.Level element)
    {
        var level = new NumberingLevel
        {
            Level = (int)Math.Clamp(DocxValues.ParseInteger(DocxValues.AttributeValue(element, "ilvl")) ?? 0, 0, 8),
            Text = "",
        };
        var attributes = element.GetAttributes()
            .Where(attribute => attribute.NamespaceUri == DocxValues.WordNamespace && attribute.LocalName != "ilvl")
            .Select(attribute => $"{attribute.LocalName}={attribute.Value}")
            .ToList();
        var unknown = new List<OpenXmlElement>();
        foreach (var child in element.ChildElements)
        {
            var value = DocxValues.AttributeValue(child, "val");
            switch (child)
            {
                case W.StartNumberingValue when DocxValues.ParseInteger(value) is { } start:
                    level = level with { Start = (int)start };
                    break;
                case W.NumberingFormat:
                    level = ReadFormat(level, value, child);
                    if (child.GetAttributes().Any(attribute => attribute.LocalName != "val"))
                    {
                        unknown.Add(child);
                    }

                    break;
                case W.LevelText:
                    level = level with { Text = value ?? "" };
                    break;
                case W.LevelJustification when DocxPropertyReader.ParseAlignment(value) is { } alignment:
                    level = level with { Alignment = alignment };
                    break;
                case W.LevelSuffix:
                    level = level with { Suffix = value switch { "space" => LabelSuffix.Space, "nothing" => LabelSuffix.Nothing, _ => LabelSuffix.Tab } };
                    break;
                case W.LevelRestart when DocxValues.ParseInteger(value) is { } restart:
                    level = level with { RestartAfterLevel = (int)restart };
                    break;
                case W.IsLegalNumberingStyle:
                    level = level with { IsLegal = DocxValues.ParseOnOff(child) };
                    break;
                case W.ParagraphStyleIdInLevel:
                    level = level with { ParagraphStyleId = value };
                    break;
                case W.NumberingSymbolRunProperties run:
                    level = level with { LabelProperties = DocxPropertyReader.ReadCharacter(run) };
                    break;
                case W.PreviousParagraphProperties paragraph:
                    level = ReadLevelParagraph(level, paragraph, unknown);
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return level with
        {
            UnknownXml = XmlFragments.Join(unknown),
            UnknownAttributes = attributes.Count == 0 ? null : string.Join("\n", attributes),
        };
    }

    private static NumberingLevel ReadFormat(NumberingLevel level, string? value, OpenXmlElement element)
    {
        _ = element;
        var format = value switch
        {
            "none" => NumberFormat.None,
            "bullet" => NumberFormat.Bullet,
            "decimal" => NumberFormat.Decimal,
            "decimalZero" => NumberFormat.DecimalZero,
            "lowerLetter" => NumberFormat.LowerLetter,
            "upperLetter" => NumberFormat.UpperLetter,
            "lowerRoman" => NumberFormat.LowerRoman,
            "upperRoman" => NumberFormat.UpperRoman,
            "ordinal" => NumberFormat.Ordinal,
            _ => NumberFormat.Other,
        };
        return level with { Format = format, FormatName = format == NumberFormat.Other ? value : null };
    }

    private static NumberingLevel ReadLevelParagraph(NumberingLevel level, W.PreviousParagraphProperties paragraph, List<OpenXmlElement> unknown)
    {
        var properties = DocxPropertyReader.ReadParagraph(paragraph);
        level = level with
        {
            IndentLeft = properties.IndentLeft ?? 0,
            FirstLineIndent = properties.FirstLineIndent ?? 0,
        };
        var rest = (W.PreviousParagraphProperties)paragraph.CloneNode(true);
        rest.RemoveAllChildren<W.Indentation>();
        if (rest.HasChildren)
        {
            unknown.Add(rest);
        }

        return level;
    }

    private static W.Level WriteLevel(NumberingLevel level)
    {
        var element = DocxPropertyWriter.Element<W.Level>(("ilvl", DocxPropertyWriter.Integer(level.Level)));
        foreach (var pair in (level.UnknownAttributes ?? "").Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var separator = pair.IndexOf('=');
            if (separator > 0)
            {
                element.SetAttribute(new OpenXmlAttribute("w", pair[..separator], DocxValues.WordNamespace, pair[(separator + 1)..]));
            }
        }

        XmlFragments.AddTo(element, level.UnknownXml);
        DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.StartNumberingValue>(("val", DocxPropertyWriter.Integer(level.Start))));
        if (element.GetFirstChild<W.NumberingFormat>() is null)
        {
            DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.NumberingFormat>(("val", FormatValue(level))));
        }

        if (level.RestartAfterLevel is { } restart)
        {
            DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.LevelRestart>(("val", DocxPropertyWriter.Integer(restart))));
        }

        AddValue<W.ParagraphStyleIdInLevel>(element, level.ParagraphStyleId);
        AddFlag<W.IsLegalNumberingStyle>(element, level.IsLegal);
        if (level.Suffix != LabelSuffix.Tab)
        {
            DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.LevelSuffix>(("val", level.Suffix == LabelSuffix.Space ? "space" : "nothing")));
        }

        DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.LevelText>(("val", level.Text)));
        DocxPropertyWriter.Add(element, DocxPropertyWriter.Element<W.LevelJustification>(("val", DocxPropertyWriter.AlignmentValue(level.Alignment))));
        if (level.IndentLeft != 0 || level.FirstLineIndent != 0)
        {
            var paragraph = element.GetFirstChild<W.PreviousParagraphProperties>();
            if (paragraph is null)
            {
                paragraph = new W.PreviousParagraphProperties();
                DocxPropertyWriter.Add(element, paragraph);
            }

            DocxPropertyWriter.Add(paragraph, DocxPropertyWriter.WriteIndentation(level.IndentLeft, null, level.FirstLineIndent)!);
        }

        if (DocxPropertyWriter.WriteCharacter(level.LabelProperties, new W.NumberingSymbolRunProperties()) is { } run)
        {
            DocxPropertyWriter.Add(element, run);
        }

        return element;
    }

    private static string FormatValue(NumberingLevel level) => level.Format switch
    {
        NumberFormat.None => "none",
        NumberFormat.Bullet => "bullet",
        NumberFormat.DecimalZero => "decimalZero",
        NumberFormat.LowerLetter => "lowerLetter",
        NumberFormat.UpperLetter => "upperLetter",
        NumberFormat.LowerRoman => "lowerRoman",
        NumberFormat.UpperRoman => "upperRoman",
        NumberFormat.Ordinal => "ordinal",
        NumberFormat.Other => level.FormatName ?? "decimal",
        _ => "decimal",
    };

    private static NumberingInstance? ReadInstance(W.NumberingInstance element)
    {
        var id = DocxValues.ParseInteger(DocxValues.AttributeValue(element, "numId"));
        var abstractId = element.AbstractNumId is { } reference ? DocxValues.ParseInteger(DocxValues.AttributeValue(reference, "val")) : null;
        if (id is null || abstractId is null)
        {
            return null;
        }

        var overrides = new List<LevelOverride>();
        foreach (var levelOverride in element.Elements<W.LevelOverride>())
        {
            var level = (int)Math.Clamp(DocxValues.ParseInteger(DocxValues.AttributeValue(levelOverride, "ilvl")) ?? 0, 0, 8);
            var start = levelOverride.StartOverrideNumberingValue is { } startOverride
                ? DocxValues.ParseInteger(DocxValues.AttributeValue(startOverride, "val"))
                : null;
            var replacement = levelOverride.Level is { } replacementLevel ? ReadLevel(replacementLevel) : null;
            overrides.Add(new LevelOverride(level, start is null ? null : (int)start, replacement));
        }

        return new NumberingInstance { Id = (int)id, AbstractId = (int)abstractId, Overrides = new EquatableArray<LevelOverride>(overrides) };
    }

    private static W.NumberingInstance WriteInstance(NumberingInstance instance)
    {
        var element = DocxPropertyWriter.Element<W.NumberingInstance>(("numId", DocxPropertyWriter.Integer(instance.Id)));
        element.AppendChild(DocxPropertyWriter.Element<W.AbstractNumId>(("val", DocxPropertyWriter.Integer(instance.AbstractId))));
        foreach (var levelOverride in instance.Overrides)
        {
            var overrideElement = DocxPropertyWriter.Element<W.LevelOverride>(("ilvl", DocxPropertyWriter.Integer(levelOverride.Level)));
            if (levelOverride.StartOverride is { } start)
            {
                overrideElement.AppendChild(DocxPropertyWriter.Element<W.StartOverrideNumberingValue>(("val", DocxPropertyWriter.Integer(start))));
            }

            if (levelOverride.Replacement is { } replacement)
            {
                overrideElement.AppendChild(WriteLevel(replacement));
            }

            element.AppendChild(overrideElement);
        }

        return element;
    }

    private static void AddValue<T>(OpenXmlCompositeElement target, string? value) where T : OpenXmlElement, new()
    {
        if (value is not null)
        {
            DocxPropertyWriter.Add(target, DocxPropertyWriter.Element<T>(("val", value)));
        }
    }

    private static void AddFlag<T>(OpenXmlCompositeElement target, bool value) where T : OpenXmlElement, new()
    {
        if (value)
        {
            DocxPropertyWriter.Add(target, new T());
        }
    }

    private static bool IsOn(string value) => value.Trim().ToLowerInvariant() is "1" or "true" or "on";
}
