using System.Globalization;
using System.Text;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>Which Word wrote the style sheet: the defaults and built-in styles differ between 2007–2010, 2013–2022 and 2023+.</summary>
internal enum WordVersion
{
    Word2007,
    Word2013,
    Word2023,
}

/// <summary>
/// styles.xml as German Word writes it: docDefaults, latent styles, and the built-in styles a document uses with Word's German style
/// ids ("Standard", "berschrift1" for heading 1, "Titel", "Kopfzeile", "Fuzeile" …), linked character styles ("berschrift1Zchn"),
/// the table styles "Normale Tabelle", "Tabellenraster" and "Gitternetztabelle 4 – Akzent 1" with their conditional formats.
/// </summary>
internal static class WordStyles
{
    public static OfficeTheme ThemeOf(WordVersion version) => version switch
    {
        WordVersion.Word2007 => OfficeTheme.Office2007,
        WordVersion.Word2023 => OfficeTheme.Office2023,
        _ => OfficeTheme.Office2013,
    };

    /// <summary>The style sheet; <paramref name="extra"/> adds a document's own styles (w:style elements).</summary>
    public static string Xml(WordVersion version, string extra = "")
    {
        var theme = ThemeOf(version);
        var accent = theme.Accent1;
        var builder = new StringBuilder($"<w:styles {WordPackage.Namespaces}>");
        var (size, after, line, kern) = version switch
        {
            WordVersion.Word2007 => (22, 200, 276, ""),
            WordVersion.Word2023 => (24, 160, 278, "<w:kern w:val=\"2\"/>"),
            _ => (22, 160, 259, ""),
        };
        builder.Append("<w:docDefaults><w:rPrDefault><w:rPr><w:rFonts w:asciiTheme=\"minorHAnsi\" w:eastAsiaTheme=\"minorHAnsi\" w:hAnsiTheme=\"minorHAnsi\" w:cstheme=\"minorBidi\"/>")
            .Append(kern).Append($"<w:sz w:val=\"{size}\"/><w:szCs w:val=\"{size}\"/><w:lang w:val=\"de-DE\" w:eastAsia=\"en-US\" w:bidi=\"ar-SA\"/>")
            .Append(version == WordVersion.Word2023 ? "<w14:ligatures w14:val=\"standardContextual\"/>" : "")
            .Append($"</w:rPr></w:rPrDefault><w:pPrDefault><w:pPr><w:spacing w:after=\"{after}\" w:line=\"{line}\" w:lineRule=\"auto\"/></w:pPr></w:pPrDefault></w:docDefaults>");
        builder.Append(LatentStyles());

        builder.Append("<w:style w:type=\"paragraph\" w:default=\"1\" w:styleId=\"Standard\"><w:name w:val=\"Normal\"/><w:qFormat/></w:style>");
        builder.Append("<w:style w:type=\"character\" w:default=\"1\" w:styleId=\"Absatz-Standardschriftart\"><w:name w:val=\"Default Paragraph Font\"/><w:uiPriority w:val=\"1\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");
        builder.Append("<w:style w:type=\"table\" w:default=\"1\" w:styleId=\"NormaleTabelle\"><w:name w:val=\"Normal Table\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/>" +
                       "<w:tblPr><w:tblInd w:w=\"0\" w:type=\"dxa\"/><w:tblCellMar><w:top w:w=\"0\" w:type=\"dxa\"/><w:left w:w=\"108\" w:type=\"dxa\"/><w:bottom w:w=\"0\" w:type=\"dxa\"/><w:right w:w=\"108\" w:type=\"dxa\"/></w:tblCellMar></w:tblPr></w:style>");
        builder.Append("<w:style w:type=\"numbering\" w:default=\"1\" w:styleId=\"KeineListe\"><w:name w:val=\"No List\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/></w:style>");

        var headings = version switch
        {
            WordVersion.Word2007 => new[] { (480, 0, 28, Shade(accent, 0.75), "BF", true), (200, 0, 26, accent, (string?)null, true), (200, 0, 22, accent, null, true) },
            WordVersion.Word2023 => [(360, 80, 40, Shade(accent, 0.75), "BF", false), (160, 80, 32, Shade(accent, 0.75), "BF", false), (160, 80, 28, Shade(accent, 0.75), "BF", false)],
            _ => [(240, 0, 32, Shade(accent, 0.75), "BF", false), (40, 0, 26, Shade(accent, 0.75), "BF", false), (40, 0, 24, Shade(accent, 0.5), "7F", false)],
        };
        for (var level = 0; level < 3; level++)
        {
            var (before, afterHeading, headingSize, color, shade, bold) = headings[level];
            var font = version == WordVersion.Word2023 && level == 2 ? "minorHAnsi" : "majorHAnsi";
            var colorXml = shade is null ? $"<w:color w:val=\"{color}\" w:themeColor=\"accent1\"/>" : $"<w:color w:val=\"{color}\" w:themeColor=\"accent1\" w:themeShade=\"{shade}\"/>";
            var runXml = $"<w:rFonts w:asciiTheme=\"{font}\" w:eastAsiaTheme=\"majorEastAsia\" w:hAnsiTheme=\"{font}\" w:cstheme=\"majorBidi\"/>{(bold ? "<w:b/><w:bCs/>" : "")}{colorXml}<w:sz w:val=\"{headingSize}\"/><w:szCs w:val=\"{headingSize}\"/>";
            builder.Append($"<w:style w:type=\"paragraph\" w:styleId=\"berschrift{level + 1}\"><w:name w:val=\"heading {level + 1}\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/>" +
                           $"<w:link w:val=\"berschrift{level + 1}Zchn\"/><w:uiPriority w:val=\"9\"/>{(level > 0 ? "<w:unhideWhenUsed/>" : "")}<w:qFormat/>" +
                           $"<w:pPr><w:keepNext/><w:keepLines/><w:spacing w:before=\"{before}\" w:after=\"{afterHeading}\"/><w:outlineLvl w:val=\"{level}\"/></w:pPr><w:rPr>{runXml}</w:rPr></w:style>");
            builder.Append($"<w:style w:type=\"character\" w:customStyle=\"1\" w:styleId=\"berschrift{level + 1}Zchn\"><w:name w:val=\"Überschrift {level + 1} Zchn\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/>" +
                           $"<w:link w:val=\"berschrift{level + 1}\"/><w:uiPriority w:val=\"9\"/><w:rPr>{runXml}</w:rPr></w:style>");
        }

        var title = version switch
        {
            WordVersion.Word2007 => $"<w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"4\" w:color=\"{accent}\" w:themeColor=\"accent1\"/></w:pBdr><w:spacing w:after=\"300\" w:line=\"240\" w:lineRule=\"auto\"/><w:contextualSpacing/></w:pPr>" +
                                    $"<w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:hAnsiTheme=\"majorHAnsi\" w:cstheme=\"majorBidi\"/><w:color w:val=\"{Shade(theme.Dark2, 0.75)}\" w:themeColor=\"text2\" w:themeShade=\"BF\"/><w:spacing w:val=\"5\"/><w:kern w:val=\"28\"/><w:sz w:val=\"52\"/><w:szCs w:val=\"52\"/></w:rPr>",
            WordVersion.Word2023 => "<w:pPr><w:spacing w:after=\"80\" w:line=\"240\" w:lineRule=\"auto\"/><w:contextualSpacing/></w:pPr><w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:hAnsiTheme=\"majorHAnsi\" w:cstheme=\"majorBidi\"/><w:spacing w:val=\"-10\"/><w:kern w:val=\"28\"/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/></w:rPr>",
            _ => "<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/><w:contextualSpacing/></w:pPr><w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:hAnsiTheme=\"majorHAnsi\" w:cstheme=\"majorBidi\"/><w:spacing w:val=\"-10\"/><w:kern w:val=\"28\"/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/></w:rPr>",
        };
        builder.Append($"<w:style w:type=\"paragraph\" w:styleId=\"Titel\"><w:name w:val=\"Title\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/><w:link w:val=\"TitelZchn\"/><w:uiPriority w:val=\"10\"/><w:qFormat/>{title}</w:style>");
        builder.Append("<w:style w:type=\"character\" w:customStyle=\"1\" w:styleId=\"TitelZchn\"><w:name w:val=\"Titel Zchn\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:link w:val=\"Titel\"/><w:uiPriority w:val=\"10\"/>" +
                       "<w:rPr><w:rFonts w:asciiTheme=\"majorHAnsi\" w:eastAsiaTheme=\"majorEastAsia\" w:hAnsiTheme=\"majorHAnsi\" w:cstheme=\"majorBidi\"/><w:spacing w:val=\"-10\"/><w:kern w:val=\"28\"/><w:sz w:val=\"56\"/><w:szCs w:val=\"56\"/></w:rPr></w:style>");
        builder.Append($"<w:style w:type=\"paragraph\" w:styleId=\"Untertitel\"><w:name w:val=\"Subtitle\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/><w:uiPriority w:val=\"11\"/><w:qFormat/>" +
                       "<w:pPr><w:numPr><w:ilvl w:val=\"1\"/></w:numPr><w:spacing w:after=\"160\"/></w:pPr><w:rPr><w:rFonts w:eastAsiaTheme=\"minorEastAsia\"/><w:color w:val=\"5A5A5A\" w:themeColor=\"text1\" w:themeTint=\"A5\"/><w:spacing w:val=\"15\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"Zitat\"><w:name w:val=\"Quote\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/><w:uiPriority w:val=\"29\"/><w:qFormat/>" +
                       "<w:pPr><w:spacing w:before=\"200\"/><w:ind w:left=\"864\" w:right=\"864\"/><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:i/><w:iCs/><w:color w:val=\"404040\" w:themeColor=\"text1\" w:themeTint=\"BF\"/></w:rPr></w:style>");
        builder.Append($"<w:style w:type=\"paragraph\" w:styleId=\"IntensivesZitat\"><w:name w:val=\"Intense Quote\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/><w:uiPriority w:val=\"30\"/><w:qFormat/>" +
                       $"<w:pPr><w:pBdr><w:top w:val=\"single\" w:sz=\"4\" w:space=\"10\" w:color=\"{accent}\" w:themeColor=\"accent1\"/><w:bottom w:val=\"single\" w:sz=\"4\" w:space=\"10\" w:color=\"{accent}\" w:themeColor=\"accent1\"/></w:pBdr>" +
                       $"<w:spacing w:before=\"360\" w:after=\"360\"/><w:ind w:left=\"864\" w:right=\"864\"/><w:jc w:val=\"center\"/></w:pPr><w:rPr><w:i/><w:iCs/><w:color w:val=\"{accent}\" w:themeColor=\"accent1\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"Listenabsatz\"><w:name w:val=\"List Paragraph\"/><w:basedOn w:val=\"Standard\"/><w:uiPriority w:val=\"34\"/><w:qFormat/><w:pPr><w:ind w:left=\"720\"/><w:contextualSpacing/></w:pPr></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"KeinLeerraum\"><w:name w:val=\"No Spacing\"/><w:uiPriority w:val=\"1\"/><w:qFormat/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"Kopfzeile\"><w:name w:val=\"header\"/><w:basedOn w:val=\"Standard\"/><w:link w:val=\"KopfzeileZchn\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/>" +
                       "<w:pPr><w:tabs><w:tab w:val=\"center\" w:pos=\"4536\"/><w:tab w:val=\"right\" w:pos=\"9072\"/></w:tabs><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:customStyle=\"1\" w:styleId=\"KopfzeileZchn\"><w:name w:val=\"Kopfzeile Zchn\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:link w:val=\"Kopfzeile\"/><w:uiPriority w:val=\"99\"/></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"Fuzeile\"><w:name w:val=\"footer\"/><w:basedOn w:val=\"Standard\"/><w:link w:val=\"FuzeileZchn\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/>" +
                       "<w:pPr><w:tabs><w:tab w:val=\"center\" w:pos=\"4536\"/><w:tab w:val=\"right\" w:pos=\"9072\"/></w:tabs><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:customStyle=\"1\" w:styleId=\"FuzeileZchn\"><w:name w:val=\"Fußzeile Zchn\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:link w:val=\"Fuzeile\"/><w:uiPriority w:val=\"99\"/></w:style>");
        builder.Append($"<w:style w:type=\"character\" w:styleId=\"Hyperlink\"><w:name w:val=\"Hyperlink\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:rPr><w:color w:val=\"{theme.Hyperlink}\" w:themeColor=\"hyperlink\"/><w:u w:val=\"single\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:styleId=\"Fett\"><w:name w:val=\"Strong\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:uiPriority w:val=\"22\"/><w:qFormat/><w:rPr><w:b/><w:bCs/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:styleId=\"Hervorhebung\"><w:name w:val=\"Emphasis\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:uiPriority w:val=\"20\"/><w:qFormat/><w:rPr><w:i/><w:iCs/></w:rPr></w:style>");
        builder.Append($"<w:style w:type=\"paragraph\" w:styleId=\"Beschriftung\"><w:name w:val=\"caption\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/><w:uiPriority w:val=\"35\"/><w:unhideWhenUsed/><w:qFormat/>" +
                       $"<w:pPr><w:spacing w:after=\"200\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:i/><w:iCs/><w:color w:val=\"{theme.Dark2}\" w:themeColor=\"text2\"/><w:sz w:val=\"18\"/><w:szCs w:val=\"18\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"Funotentext\"><w:name w:val=\"footnote text\"/><w:basedOn w:val=\"Standard\"/><w:link w:val=\"FunotentextZchn\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/>" +
                       "<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:customStyle=\"1\" w:styleId=\"FunotentextZchn\"><w:name w:val=\"Fußnotentext Zchn\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:link w:val=\"Funotentext\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:styleId=\"Funotenzeichen\"><w:name w:val=\"footnote reference\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"Endnotentext\"><w:name w:val=\"endnote text\"/><w:basedOn w:val=\"Standard\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:styleId=\"Endnotenzeichen\"><w:name w:val=\"endnote reference\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:rPr><w:vertAlign w:val=\"superscript\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"paragraph\" w:styleId=\"Kommentartext\"><w:name w:val=\"annotation text\"/><w:basedOn w:val=\"Standard\"/><w:uiPriority w:val=\"99\"/><w:unhideWhenUsed/><w:pPr><w:spacing w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:rPr><w:sz w:val=\"20\"/><w:szCs w:val=\"20\"/></w:rPr></w:style>");
        builder.Append("<w:style w:type=\"character\" w:styleId=\"Kommentarzeichen\"><w:name w:val=\"annotation reference\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:unhideWhenUsed/><w:rPr><w:sz w:val=\"16\"/><w:szCs w:val=\"16\"/></w:rPr></w:style>");
        builder.Append($"<w:style w:type=\"paragraph\" w:styleId=\"Inhaltsverzeichnisberschrift\"><w:name w:val=\"TOC Heading\"/><w:basedOn w:val=\"berschrift1\"/><w:next w:val=\"Standard\"/><w:uiPriority w:val=\"39\"/><w:unhideWhenUsed/><w:qFormat/><w:pPr><w:outlineLvl w:val=\"9\"/></w:pPr></w:style>");
        for (var level = 1; level <= 3; level++)
        {
            builder.Append($"<w:style w:type=\"paragraph\" w:styleId=\"Verzeichnis{level}\"><w:name w:val=\"toc {level}\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/><w:autoRedefine/><w:uiPriority w:val=\"39\"/><w:unhideWhenUsed/>" +
                           $"<w:pPr><w:spacing w:after=\"100\"/>{(level > 1 ? $"<w:ind w:left=\"{(level - 1) * 220}\"/>" : "")}</w:pPr></w:style>");
        }

        builder.Append("<w:style w:type=\"table\" w:styleId=\"Tabellenraster\"><w:name w:val=\"Table Grid\"/><w:basedOn w:val=\"NormaleTabelle\"/><w:uiPriority w:val=\"39\"/><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr>" +
                       "<w:tblPr><w:tblBorders>" + string.Concat(new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => $"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/>")) +
                       "</w:tblBorders></w:tblPr></w:style>");
        builder.Append(GridTable4(accent, "Gitternetztabelle4Akzent1", "Grid Table 4 Accent 1", "accent1"));
        var accent5 = theme.Accents[4];
        builder.Append(ListTable3(accent5, "Listentabelle3Akzent5", "List Table 3 Accent 5", "accent5"));
        builder.Append(extra);
        return builder.Append("</w:styles>").ToString();
    }

    /// <summary>"Gitternetztabelle 4 – Akzent n": borders in the accent at 60 %, a header row filled with the accent (white bold text), bands at 20 %.</summary>
    public static string GridTable4(string accent, string id, string name, string themeColor)
    {
        var light = Tint(accent, 0.6);
        var band = Tint(accent, 0.2);
        string Side(string side, string color, string tint) => $"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"{color}\" w:themeColor=\"{themeColor}\"{tint}/>";
        return $"<w:style w:type=\"table\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/><w:basedOn w:val=\"NormaleTabelle\"/><w:uiPriority w:val=\"49\"/>" +
               "<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:tblPr><w:tblStyleRowBandSize w:val=\"1\"/><w:tblStyleColBandSize w:val=\"1\"/><w:tblBorders>" +
               string.Concat(new[] { "top", "left", "bottom", "right", "insideH", "insideV" }.Select(side => Side(side, light, " w:themeTint=\"99\""))) + "</w:tblBorders></w:tblPr>" +
               $"<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/><w:bCs/><w:color w:val=\"FFFFFF\" w:themeColor=\"background1\"/></w:rPr><w:tblPr/><w:tcPr><w:tcBorders>{Side("top", accent, "")}{Side("left", accent, "")}" +
               $"{Side("bottom", accent, "")}{Side("right", accent, "")}<w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tcBorders><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{accent}\" w:themeFill=\"{themeColor}\"/></w:tcPr></w:tblStylePr>" +
               $"<w:tblStylePr w:type=\"lastRow\"><w:rPr><w:b/><w:bCs/></w:rPr><w:tblPr/><w:tcPr><w:tcBorders><w:top w:val=\"double\" w:sz=\"4\" w:space=\"0\" w:color=\"{accent}\" w:themeColor=\"{themeColor}\"/></w:tcBorders></w:tcPr></w:tblStylePr>" +
               "<w:tblStylePr w:type=\"firstCol\"><w:rPr><w:b/><w:bCs/></w:rPr></w:tblStylePr><w:tblStylePr w:type=\"lastCol\"><w:rPr><w:b/><w:bCs/></w:rPr></w:tblStylePr>" +
               $"<w:tblStylePr w:type=\"band1Vert\"><w:tblPr/><w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{band}\" w:themeFill=\"{themeColor}\" w:themeFillTint=\"33\"/></w:tcPr></w:tblStylePr>" +
               $"<w:tblStylePr w:type=\"band1Horz\"><w:tblPr/><w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{band}\" w:themeFill=\"{themeColor}\" w:themeFillTint=\"33\"/></w:tcPr></w:tblStylePr></w:style>";
    }

    /// <summary>"Listentabelle 3 – Akzent n": an outline in the accent, a filled header row, bands as lines.</summary>
    public static string ListTable3(string accent, string id, string name, string themeColor)
    {
        string Side(string side) => $"<w:{side} w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"{accent}\" w:themeColor=\"{themeColor}\"/>";
        return $"<w:style w:type=\"table\" w:styleId=\"{id}\"><w:name w:val=\"{name}\"/><w:basedOn w:val=\"NormaleTabelle\"/><w:uiPriority w:val=\"48\"/>" +
               $"<w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:tblPr><w:tblStyleRowBandSize w:val=\"1\"/><w:tblStyleColBandSize w:val=\"1\"/><w:tblBorders>{Side("top")}{Side("left")}{Side("bottom")}{Side("right")}</w:tblBorders></w:tblPr>" +
               $"<w:tblStylePr w:type=\"firstRow\"><w:rPr><w:b/><w:bCs/><w:color w:val=\"FFFFFF\" w:themeColor=\"background1\"/></w:rPr><w:tblPr/><w:tcPr><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"{accent}\" w:themeFill=\"{themeColor}\"/></w:tcPr></w:tblStylePr>" +
               $"<w:tblStylePr w:type=\"lastRow\"><w:rPr><w:b/><w:bCs/></w:rPr><w:tblPr/><w:tcPr><w:tcBorders><w:top w:val=\"double\" w:sz=\"4\" w:space=\"0\" w:color=\"{accent}\" w:themeColor=\"{themeColor}\"/></w:tcBorders><w:shd w:val=\"clear\" w:color=\"auto\" w:fill=\"FFFFFF\"/></w:tcPr></w:tblStylePr>" +
               "<w:tblStylePr w:type=\"firstCol\"><w:rPr><w:b/><w:bCs/></w:rPr></w:tblStylePr>" +
               $"<w:tblStylePr w:type=\"band1Horz\"><w:tblPr/><w:tcPr><w:tcBorders>{Side("top")}{Side("bottom")}</w:tcBorders></w:tcPr></w:tblStylePr></w:style>";
    }

    /// <summary>Word's tint: the colour moved towards white, <paramref name="amount"/> of the colour kept (0.2 = 80 % lighter).</summary>
    public static string Tint(string hex, double amount)
    {
        var (h, s, l) = ToHsl(hex);
        return FromHsl(h, s, l * amount + (1 - amount));
    }

    /// <summary>Word's shade: the luminance scaled by <paramref name="amount"/> (0.75 = "25 % dunkler").</summary>
    public static string Shade(string hex, double amount)
    {
        var (h, s, l) = ToHsl(hex);
        return FromHsl(h, s, l * amount);
    }

    private static (double H, double S, double L) ToHsl(string hex)
    {
        var r = int.Parse(hex[..2], NumberStyles.HexNumber) / 255.0;
        var g = int.Parse(hex[2..4], NumberStyles.HexNumber) / 255.0;
        var b = int.Parse(hex[4..6], NumberStyles.HexNumber) / 255.0;
        var max = Math.Max(r, Math.Max(g, b));
        var min = Math.Min(r, Math.Min(g, b));
        var l = (max + min) / 2;
        if (max == min)
        {
            return (0, 0, l);
        }

        var d = max - min;
        var s = l > 0.5 ? d / (2 - max - min) : d / (max + min);
        var h = max == r ? (g - b) / d + (g < b ? 6 : 0) : max == g ? (b - r) / d + 2 : (r - g) / d + 4;
        return (h / 6, s, l);
    }

    private static string FromHsl(double h, double s, double l)
    {
        double Channel(double t)
        {
            var q = l < 0.5 ? l * (1 + s) : l + s - l * s;
            var p = 2 * l - q;
            t = t < 0 ? t + 1 : t > 1 ? t - 1 : t;
            return t < 1.0 / 6 ? p + (q - p) * 6 * t : t < 0.5 ? q : t < 2.0 / 3 ? p + (q - p) * (2.0 / 3 - t) * 6 : p;
        }

        var (r, g, b) = s == 0 ? (l, l, l) : (Channel(h + 1.0 / 3), Channel(h), Channel(h - 1.0 / 3));
        return $"{(int)Math.Round(r * 255):X2}{(int)Math.Round(g * 255):X2}{(int)Math.Round(b * 255):X2}";
    }

    private static string LatentStyles()
    {
        var names = new (string Name, int Priority, bool QFormat, bool Hidden)[]
        {
            ("Normal", 0, true, false), ("heading 1", 9, true, false), ("heading 2", 9, true, true), ("heading 3", 9, true, true), ("heading 4", 9, true, true),
            ("heading 5", 9, true, true), ("heading 6", 9, true, true), ("heading 7", 9, true, true), ("heading 8", 9, true, true), ("heading 9", 9, true, true),
            ("index 1", 99, false, true), ("toc 1", 39, false, true), ("toc 2", 39, false, true), ("toc 3", 39, false, true), ("toc 4", 39, false, true),
            ("Normal Indent", 99, false, true), ("footnote text", 99, false, true), ("annotation text", 99, false, true), ("header", 99, false, true), ("footer", 99, false, true),
            ("caption", 35, true, true), ("footnote reference", 99, false, true), ("annotation reference", 99, false, true), ("endnote reference", 99, false, true),
            ("endnote text", 99, false, true), ("List Bullet", 99, false, true), ("List Number", 99, false, true), ("Title", 10, true, false), ("Default Paragraph Font", 1, false, true),
            ("Subtitle", 11, true, false), ("Hyperlink", 99, false, true), ("Strong", 22, true, false), ("Emphasis", 20, true, false), ("Table Grid", 39, false, false),
            ("Placeholder Text", 99, false, true), ("No Spacing", 1, true, false), ("List Paragraph", 34, true, false), ("Quote", 29, true, false), ("Intense Quote", 30, true, false),
            ("Subtle Emphasis", 19, true, false), ("Intense Emphasis", 21, true, false), ("Subtle Reference", 31, true, false), ("Intense Reference", 32, true, false),
            ("Book Title", 33, true, false), ("Bibliography", 37, false, true), ("TOC Heading", 39, true, true), ("Plain Table 1", 41, false, false), ("Grid Table 1 Light", 46, false, false),
            ("Grid Table 4 Accent 1", 49, false, false), ("List Table 3 Accent 5", 48, false, false), ("Mention", 99, false, true), ("Smart Hyperlink", 99, false, true),
            ("Hashtag", 99, false, true), ("Unresolved Mention", 99, false, true), ("Smart Link", 99, false, true),
        };
        var builder = new StringBuilder("<w:latentStyles w:defLockedState=\"0\" w:defUIPriority=\"99\" w:defSemiHidden=\"0\" w:defUnhideWhenUsed=\"0\" w:defQFormat=\"0\" w:count=\"376\">");
        foreach (var (name, priority, qFormat, hidden) in names)
        {
            builder.Append($"<w:lsdException w:name=\"{name}\"{(hidden ? " w:semiHidden=\"1\" w:unhideWhenUsed=\"1\"" : "")} w:uiPriority=\"{priority}\"{(qFormat ? " w:qFormat=\"1\"" : "")}/>");
        }

        return builder.Append("</w:latentStyles>").ToString();
    }
}
