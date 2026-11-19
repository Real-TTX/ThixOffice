namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// The "Office" themes Word's Normal.dotm carries, as theme1.xml: 2007–2010 (Cambria / Calibri, accent 4F81BD), 2013–2022 (Calibri
/// Light / Calibri, accent 4472C4) and 2023+ (Aptos Display / Aptos, accent 156082) – colours, fonts with their script fonts and
/// the format scheme (fills, lines, effects, background fills) as Word writes them.
/// </summary>
internal sealed record OfficeTheme(
    string Name,
    string MajorFont,
    string MinorFont,
    string Dark2,
    string Light2,
    string[] Accents,
    string Hyperlink,
    string FollowedHyperlink,
    IReadOnlyList<(string Name, string Panose, string Family, string Pitch)> FontTableEntries)
{
    public static readonly OfficeTheme Office2007 = new(
        "Office", "Cambria", "Calibri", "1F497D", "EEECE1",
        ["4F81BD", "C0504D", "9BBB59", "8064A2", "4BACC6", "F79646"], "0000FF", "800080",
        [("Calibri", "020F0502020204030204", "swiss", "variable"), ("Times New Roman", "02020603050405020304", "roman", "variable"),
         ("Cambria", "02040503050406030204", "roman", "variable"), ("Symbol", "05050102010706020507", "roman", "variable"),
         ("Wingdings", "05000000000000000000", "auto", "variable")]);

    public static readonly OfficeTheme Office2013 = new(
        "Office", "Calibri Light", "Calibri", "44546A", "E7E6E6",
        ["4472C4", "ED7D31", "A5A5A5", "FFC000", "5B9BD5", "70AD47"], "0563C1", "954F72",
        [("Calibri", "020F0502020204030204", "swiss", "variable"), ("Times New Roman", "02020603050405020304", "roman", "variable"),
         ("Calibri Light", "020F0302020204030204", "swiss", "variable"), ("Symbol", "05050102010706020507", "roman", "variable"),
         ("Wingdings", "05000000000000000000", "auto", "variable"), ("Courier New", "02070309020205020404", "modern", "fixed")]);

    public static readonly OfficeTheme Office2023 = new(
        "Office", "Aptos Display", "Aptos", "0E2841", "E8E8E8",
        ["156082", "E97132", "196B24", "0F9ED5", "A02B93", "4EA72E"], "467886", "96607D",
        [("Aptos", "020B0004020202020204", "swiss", "variable"), ("Times New Roman", "02020603050405020304", "roman", "variable"),
         ("Aptos Display", "020B0004020202020204", "swiss", "variable"), ("Symbol", "05050102010706020507", "roman", "variable"),
         ("Wingdings", "05000000000000000000", "auto", "variable")]);

    public string Accent1 => Accents[0];

    public string Xml =>
        $"<a:theme xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" name=\"{Name}\"><a:themeElements>" +
        $"<a:clrScheme name=\"{Name}\"><a:dk1><a:sysClr val=\"windowText\" lastClr=\"000000\"/></a:dk1><a:lt1><a:sysClr val=\"window\" lastClr=\"FFFFFF\"/></a:lt1>" +
        $"<a:dk2><a:srgbClr val=\"{Dark2}\"/></a:dk2><a:lt2><a:srgbClr val=\"{Light2}\"/></a:lt2>" +
        string.Concat(Accents.Select((accent, index) => $"<a:accent{index + 1}><a:srgbClr val=\"{accent}\"/></a:accent{index + 1}>")) +
        $"<a:hlink><a:srgbClr val=\"{Hyperlink}\"/></a:hlink><a:folHlink><a:srgbClr val=\"{FollowedHyperlink}\"/></a:folHlink></a:clrScheme>" +
        $"<a:fontScheme name=\"{Name}\"><a:majorFont>{Fonts(MajorFont, "MS Gothic", "Times New Roman")}</a:majorFont>" +
        $"<a:minorFont>{Fonts(MinorFont, "MS Mincho", "Arial")}</a:minorFont></a:fontScheme>" +
        $"<a:fmtScheme name=\"{Name}\"><a:fillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"110000\"/><a:satMod val=\"105000\"/><a:tint val=\"67000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"105000\"/><a:satMod val=\"103000\"/><a:tint val=\"73000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"105000\"/><a:satMod val=\"109000\"/><a:tint val=\"81000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:satMod val=\"103000\"/><a:lumMod val=\"102000\"/><a:tint val=\"94000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:satMod val=\"110000\"/><a:lumMod val=\"100000\"/><a:shade val=\"100000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:lumMod val=\"99000\"/><a:satMod val=\"120000\"/><a:shade val=\"78000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill></a:fillStyleLst>" +
        "<a:lnStyleLst><a:ln w=\"6350\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "<a:ln w=\"12700\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln>" +
        "<a:ln w=\"19050\" cap=\"flat\" cmpd=\"sng\" algn=\"ctr\"><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:prstDash val=\"solid\"/><a:miter lim=\"800000\"/></a:ln></a:lnStyleLst>" +
        "<a:effectStyleLst><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst/></a:effectStyle><a:effectStyle><a:effectLst><a:outerShdw blurRad=\"57150\" dist=\"19050\" dir=\"5400000\" algn=\"ctr\" rotWithShape=\"0\">" +
        "<a:srgbClr val=\"000000\"><a:alpha val=\"63000\"/></a:srgbClr></a:outerShdw></a:effectLst></a:effectStyle></a:effectStyleLst>" +
        "<a:bgFillStyleLst><a:solidFill><a:schemeClr val=\"phClr\"/></a:solidFill><a:solidFill><a:schemeClr val=\"phClr\"><a:tint val=\"95000\"/><a:satMod val=\"170000\"/></a:schemeClr></a:solidFill>" +
        "<a:gradFill rotWithShape=\"1\"><a:gsLst><a:gs pos=\"0\"><a:schemeClr val=\"phClr\"><a:tint val=\"93000\"/><a:satMod val=\"150000\"/><a:shade val=\"98000\"/><a:lumMod val=\"102000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"50000\"><a:schemeClr val=\"phClr\"><a:tint val=\"98000\"/><a:satMod val=\"130000\"/><a:shade val=\"90000\"/><a:lumMod val=\"103000\"/></a:schemeClr></a:gs>" +
        "<a:gs pos=\"100000\"><a:schemeClr val=\"phClr\"><a:shade val=\"63000\"/><a:satMod val=\"120000\"/></a:schemeClr></a:gs></a:gsLst><a:lin ang=\"5400000\" scaled=\"0\"/></a:gradFill></a:bgFillStyleLst></a:fmtScheme>" +
        "</a:themeElements><a:objectDefaults/><a:extraClrSchemeLst/></a:theme>";

    private static string Fonts(string latin, string japanese, string arabic) =>
        $"<a:latin typeface=\"{latin}\" panose=\"020F0302020204030204\"/><a:ea typeface=\"\"/><a:cs typeface=\"\"/>" +
        $"<a:font script=\"Jpan\" typeface=\"{japanese}\"/><a:font script=\"Hang\" typeface=\"맑은 고딕\"/><a:font script=\"Hans\" typeface=\"等线\"/>" +
        $"<a:font script=\"Hant\" typeface=\"新細明體\"/><a:font script=\"Arab\" typeface=\"{arabic}\"/><a:font script=\"Hebr\" typeface=\"{arabic}\"/>" +
        "<a:font script=\"Thai\" typeface=\"Angsana New\"/><a:font script=\"Ethi\" typeface=\"Nyala\"/><a:font script=\"Beng\" typeface=\"Vrinda\"/>" +
        "<a:font script=\"Grek\" typeface=\"" + arabic + "\"/><a:font script=\"Cyrl\" typeface=\"" + arabic + "\"/>";
}
