using System.Text;
using SkiaSharp;
using static ThixWrite.Core.Tests.RealWorld.Wml;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// The generated documents of the real-world corpus: what Word demo and template documents contain, written the way Word writes
/// them. Each method returns the file name and the bytes; <see cref="All"/> lists them.
/// </summary>
internal static class CorpusDocuments
{
    private const string Lorem = "Die Ergebnisse des dritten Quartals zeigen ein stabiles Wachstum in allen Regionen. Besonders der Online-Handel hat sich " +
                                 "deutlich besser entwickelt als geplant, während die Kosten für Logistik und Energie leicht gesunken sind.";

    private const string Lorem2 = "Für das kommende Jahr planen wir, die Zusammenarbeit mit unseren Partnern auszubauen und neue Märkte in Skandinavien zu " +
                                  "erschließen. Dafür stellen wir ein eigenes Team zusammen, das von der Geschäftsführung begleitet wird.";

    public static IEnumerable<(string Name, Func<byte[]> Build)> All =>
    [
        ("01-demo-cover-toc.docx", CoverAndToc),
        ("02-demo-floating.docx", Floating),
        ("03-demo-chart-smartart.docx", ChartAndSmartArt),
        ("04-demo-columns-sections.docx", ColumnsAndSections),
        ("05-demo-typography.docx", Typography),
        ("06-demo-lists.docx", Lists),
        ("07-demo-tables.docx", Tables),
        ("08-demo-review.docx", Review),
        ("09-letter-din5008.docx", Letter),
        ("10-invoice.docx", Invoice),
        ("11-cv-two-columns.docx", CurriculumVitae),
        ("12-report-landscape.docx", LandscapeReport),
        ("13-office2007-theme.docx", Office2007),
        ("14-newsletter.docx", Newsletter),
    ];

    private static readonly SKColor Blue = new(0x44, 0x72, 0xC4);
    private static readonly SKColor Teal = new(0x1F, 0x9E, 0x99);

    private static string Header(WordPackage package, int number, string text, string? logoId = null) =>
        package.AddHeader(number, P("Kopfzeile", (logoId is null ? "" : InlinePicture(logoId, 3, 0.75, "Logo")) + Tab() + Tab() + R(text, Color("7F7F7F", "text1", tint: "80"))));

    private static string PageXofY(WordPackage package, int number) =>
        package.AddFooter(number, P("Fuzeile", R("Seite ") + Field("PAGE   \\* MERGEFORMAT", "1") + R(" von ") + Field("NUMPAGES   \\* MERGEFORMAT", "3"), "<w:jc w:val=\"center\"/>"));

    private static string Paragraphs(int count) => string.Concat(Enumerable.Range(0, count).Select(index => Para(index % 2 == 0 ? Lorem : Lorem2)));

    /// <summary>A cover page in a "Deckblätter" content control (a filled shape over the page and a text box with title, subtitle, author), a TOC field, numbered headings.</summary>
    private static byte[] CoverAndToc()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Jahresbericht 2026" };
        package.AddNumbering(CorpusParts.Numbering());
        var footer = PageXofY(package, 1);
        var headingNumber = "<w:numPr><w:numId w:val=\"2\"/></w:numPr>";
        var titleBox = P(null, R("JAHRESBERICHT 2026", Font("Calibri Light") + Size(36) + Color("FFFFFF")), "<w:spacing w:after=\"240\"/>") +
                       P(null, R("Nordlicht Reisen GmbH – Zahlen, Projekte, Ausblick", Size(14) + Color("FFFFFF")));
        var cover = "<w:sdt><w:sdtPr><w:id w:val=\"-1430394893\"/><w:docPartObj><w:docPartGallery w:val=\"Cover Pages\"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtEndPr><w:rPr><w:color w:val=\"auto\"/></w:rPr></w:sdtEndPr><w:sdtContent>" +
                    P(null,
                        Shape("rect", 21, 29.7, new Position("page", 0, null, "page", 0), "none", SolidFill("4472C4", "accent1"), NoLine, behind: true, name: "Rechteck") +
                        Shape("rect", 15, 6, new Position("margin", 0, null, "page", 9), "square", NoFill, NoLine, titleBox, isTextBox: true, name: "Textfeld", anchorText: "b")) +
                    P(null, "<w:r><w:br w:type=\"page\"/></w:r>") + "</w:sdtContent></w:sdt>";
        var toc = "<w:sdt><w:sdtPr><w:id w:val=\"1867411\"/><w:docPartObj><w:docPartGallery w:val=\"Table of Contents\"/><w:docPartUnique/></w:docPartObj></w:sdtPr><w:sdtEndPr><w:rPr><w:b/><w:bCs/></w:rPr></w:sdtEndPr><w:sdtContent>" +
                  P("Inhaltsverzeichnisberschrift", R("Inhalt")) +
                  P("Verzeichnis1", "<w:r><w:fldChar w:fldCharType=\"begin\"/></w:r><w:r><w:instrText xml:space=\"preserve\"> TOC \\o \"1-3\" \\h \\z \\u </w:instrText></w:r><w:r><w:fldChar w:fldCharType=\"separate\"/></w:r>" +
                                    "<w:hyperlink w:anchor=\"_Toc1\" w:history=\"1\">" + R("1") + Tab() + R("Überblick") + Tab() + R("2") + "</w:hyperlink>",
                        "<w:tabs><w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>") +
                  P("Verzeichnis2", "<w:hyperlink w:anchor=\"_Toc2\" w:history=\"1\">" + R("1.1") + Tab() + R("Umsatz nach Regionen") + Tab() + R("2") + "</w:hyperlink>",
                        "<w:tabs><w:tab w:val=\"left\" w:pos=\"880\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>") +
                  P("Verzeichnis1", "<w:hyperlink w:anchor=\"_Toc3\" w:history=\"1\">" + R("2") + Tab() + R("Ausblick") + Tab() + R("3") + "</w:hyperlink><w:r><w:fldChar w:fldCharType=\"end\"/></w:r>",
                        "<w:tabs><w:tab w:val=\"left\" w:pos=\"440\"/><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>") +
                  "</w:sdtContent></w:sdt>";
        var body = cover + toc + P(null, Br("page")) +
                   P("berschrift1", "<w:bookmarkStart w:id=\"0\" w:name=\"_Toc1\"/>" + R("Überblick") + "<w:bookmarkEnd w:id=\"0\"/>", headingNumber) + Paragraphs(2) +
                   P("berschrift2", "<w:bookmarkStart w:id=\"1\" w:name=\"_Toc2\"/>" + R("Umsatz nach Regionen") + "<w:bookmarkEnd w:id=\"1\"/>", "<w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"2\"/></w:numPr>") + Paragraphs(2) +
                   P("berschrift3", R("Norddeutschland"), "<w:numPr><w:ilvl w:val=\"2\"/><w:numId w:val=\"2\"/></w:numPr>") + Paragraphs(1) +
                   P("berschrift1", "<w:bookmarkStart w:id=\"2\" w:name=\"_Toc3\"/>" + R("Ausblick") + "<w:bookmarkEnd w:id=\"2\"/>", headingNumber) + Paragraphs(3) +
                   P("IntensivesZitat", R("„Wir wachsen dort, wo unsere Gäste uns hinführen.“")) +
                   Section(references: $"<w:footerReference w:type=\"default\" r:id=\"{footer}\"/>", titlePage: true);
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>Anchored pictures with every wrap kind and frame, a sidebar text box, preset shapes, a group, a VML-fallback text box.</summary>
    private static byte[] Floating()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Bilder und Formen" };
        var photo = package.AddImage("image1.png", Picture(640, 420, Blue, Teal));
        var small = package.AddImage("image2.png", Picture(300, 300, new SKColor(0xED, 0x7D, 0x31), new SKColor(0xFF, 0xC0, 0x00)));
        var sidebar = P(null, R("Gut zu wissen", Bold + Color("4472C4", "accent1")), "<w:spacing w:after=\"120\"/>") +
                      Para("Seit 2019 verdoppelte sich die Zahl der Buchungen über die App. Jede dritte Reise wird heute mobil gebucht.");
        var body =
            P("Titel", R("Bilder und Formen im Text")) +
            P(null, FloatingPicture(photo, 7, 4.6, new Position("column", null, "right", "paragraph", 0.1), "square", name: "Bild") + R(Lorem + " " + Lorem2)) +
            Para(Lorem) +
            P(null, FloatingPicture(small, 3, 3, new Position("margin", 0, null, "paragraph", 0), "tight", name: "Bild") + R(Lorem2 + " " + Lorem)) +
            P(null, FloatingPicture(photo, 12, 4, new Position("margin", null, "center", "paragraph", 0.2), "topAndBottom", name: "Bild")) +
            Para(Lorem2) +
            P(null, Shape("rect", 5.5, 5, new Position("page", 14.5, null, "page", 19), "square", SolidFill("DEEAF6"), Line("4472C4", 0.75), sidebar, isTextBox: true, name: "Textfeld") + R(Lorem)) +
            Para(Lorem2) +
            P(null, R("Formen: ") +
                    Shape("roundRect", 3, 1.5, new Position("column", 0, null, "paragraph", 0.8), "topAndBottom", SolidFill("4472C4", "accent1"), Line("2F528F"), P(null, R("Start", Color("FFFFFF")), "<w:jc w:val=\"center\"/>"), name: "Rechteck: abgerundete Ecken", anchorText: "ctr") +
                    Shape("rightArrow", 2, 1, new Position("column", 3.5, null, "paragraph", 1.05), "none", SolidFill("ED7D31", "accent2"), NoLine, name: "Pfeil: nach rechts") +
                    Shape("ellipse", 3, 1.5, new Position("column", 6, null, "paragraph", 0.8), "none", SolidFill("70AD47", "accent6"), Line("507E32"), P(null, R("Ziel", Color("FFFFFF")), "<w:jc w:val=\"center\"/>"), name: "Ellipse", anchorText: "ctr") +
                    Shape("star5", 1.8, 1.8, new Position("column", 10, null, "paragraph", 0.6), "none", SolidFill("FFC000", "accent4"), NoLine, name: "Stern: 5 Zacken")) +
            P(null, "") + P(null, "") + P(null, "") +
            P(null, Group(8, 2.5, new Position("column", 0, null, "paragraph", 0.2), "topAndBottom",
                ("rect", 0, 0, 3.5, 2.5, SolidFill("5B9BD5", "accent5"), NoLine, P(null, R("Planung", Color("FFFFFF")), "<w:jc w:val=\"center\"/>")),
                ("rect", 4.5, 0, 3.5, 2.5, SolidFill("A5A5A5", "accent3"), NoLine, P(null, R("Umsetzung", Color("FFFFFF")), "<w:jc w:val=\"center\"/>")),
                ("rightArrow", 3.6, 0.9, 0.8, 0.7, SolidFill("404040"), NoLine, ""))) +
            Para("Das Logo im Hintergrund liegt hinter dem Text:") +
            P(null, FloatingPicture(small, 4, 4, new Position("margin", null, "center", "paragraph", 0), "none", behind: true, name: "Wasserzeichen") + R(Lorem + " " + Lorem2)) +
            Section();
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>A column chart with cached values and a SmartArt process with its drawing part.</summary>
    private static byte[] ChartAndSmartArt()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Diagramme" };
        var chart = package.AddChart(1, CorpusParts.ColumnChart("Umsatz nach Quartal (Mio. €)", ["Q1", "Q2", "Q3", "Q4"],
            ("2024", [4.3, 2.5, 3.5, 4.5]), ("2025", [2.4, 4.4, 1.8, 2.8]), ("2026", [2, 2, 3, 5])));
        var (data, layout, style, colors, drawing) = CorpusParts.SmartArtProcess(["Anfrage", "Angebot", "Buchung"], 15.9, 4);
        var smartArt = package.AddDiagram(1, data, layout, style, colors, drawing);
        var body =
            P("berschrift1", R("Umsatzentwicklung")) + Para(Lorem) +
            P(null, InlineChart(chart, 15.9, 8), "<w:jc w:val=\"center\"/>") +
            P("Beschriftung", R("Abbildung ") + Field("SEQ Abbildung \\* ARABIC", "1") + R(": Umsatz je Quartal")) +
            P("berschrift1", R("Ablauf einer Buchung")) + Para(Lorem2) +
            P(null, InlineSmartArt(smartArt, 15.9, 4)) +
            Para("Nach der Buchung erhalten die Gäste ihre Unterlagen per E-Mail.") +
            Section();
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>Continuous two- and three-column sections with a column break, a landscape section, an A5 section, odd-page start.</summary>
    private static byte[] ColumnsAndSections()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Spalten und Abschnitte" };
        var body =
            P("Titel", R("Spalten und Abschnitte")) + Para("Dieser Absatz steht über die ganze Breite. Darunter beginnt ein Abschnitt mit zwei Spalten.") +
            P(null, "", Section()) +
            Paragraphs(3) + P(null, R("Nach diesem Satz folgt ein Spaltenumbruch.") + Br("column")) + Paragraphs(2) +
            P(null, "", Section(type: "continuous", columns: "<w:cols w:num=\"2\" w:space=\"708\"/>")) +
            P("berschrift1", R("Drei Spalten mit Linie")) + Paragraphs(4) +
            P(null, "", Section(type: "continuous", columns: "<w:cols w:num=\"3\" w:sep=\"1\" w:space=\"454\"/>")) +
            P("berschrift1", R("Querformat")) + Para("Dieser Abschnitt steht im Querformat – für breite Tabellen und Diagramme.") + Paragraphs(2) +
            P(null, "", Section(16838, 11906, landscape: true, type: "nextPage")) +
            P("berschrift1", R("A5-Seite")) + Paragraphs(2) +
            P(null, "", Section(8391, 11906, type: "oddPage", top: 850, right: 850, bottom: 850, left: 850)) +
            P("berschrift1", R("Wieder A4 hoch")) + Paragraphs(2) +
            Section();
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>Character formats, paragraph borders and shading, tab leaders, line spacing rules, a drop cap, page colour, line numbers.</summary>
    private static byte[] Typography()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2023) { Title = "Typografie", SettingsExtra = "<w:displayBackgroundShape/>" };
        var dropCap = P(null, R("D", Size(44) + Color("156082", "accent1")), "<w:keepNext/><w:framePr w:dropCap=\"drop\" w:lines=\"3\" w:wrap=\"around\" w:vAnchor=\"text\" w:hAnchor=\"text\"/><w:spacing w:after=\"0\" w:line=\"1069\" w:lineRule=\"exact\"/><w:textAlignment w:val=\"baseline\"/>", Size(44)) +
                      Para("ie Initiale über drei Zeilen ist ein Rahmen (w:framePr dropCap), der Text fließt rechts daneben weiter. " + Lorem + " " + Lorem2);
        var body =
            P("Titel", R("Zeichen und Absätze")) +
            P(null, R("Fett", Bold) + R(", ") + R("kursiv", Italic) + R(", ") + R("unterstrichen", "<w:u w:val=\"single\"/>") + R(", ") + R("doppelt", "<w:u w:val=\"double\"/>") + R(", ") +
                    R("gepunktet", "<w:u w:val=\"dotted\"/>") + R(", ") + R("Welle", "<w:u w:val=\"wave\" w:color=\"FF0000\"/>") + R(", ") + R("durchgestrichen", "<w:strike/>") + R(", ") +
                    R("doppelt durchgestrichen", "<w:dstrike/>") + R(", H") + R("2", "<w:vertAlign w:val=\"subscript\"/>") + R("O, m") + R("2", "<w:vertAlign w:val=\"superscript\"/>") + R(".")) +
            P(null, R("Kapitälchen", "<w:smallCaps/>") + R(", ") + R("Großbuchstaben", "<w:caps/>") + R(", ") + R("gesperrt", "<w:spacing w:val=\"60\"/>") + R(", ") +
                    R("schmal", "<w:spacing w:val=\"-15\"/>") + R(", ") + R("150 % breit", "<w:w w:val=\"150\"/>") + R(", ") + R("80 % breit", "<w:w w:val=\"80\"/>") + R(", ") +
                    R("hervorgehoben", "<w:highlight w:val=\"yellow\"/>") + R(", ") + R("schattiert", Shd("D9D9D9")) + R(", ") + R("Unterschneidung AV Wa", "<w:kern w:val=\"16\"/>" + Size(16)) + R(", ") +
                    R("hochgestellt 3 pt", "<w:position w:val=\"6\"/>") + R(", ") + R("verborgen", "<w:vanish/>") + R("Ende.")) +
            P(null, R("Ein Absatz mit Rahmen und Schattierung, wie ihn Vorlagen für Hinweise nutzen.")
                , "<w:pBdr><w:top w:val=\"single\" w:sz=\"12\" w:space=\"4\" w:color=\"156082\" w:themeColor=\"accent1\"/><w:left w:val=\"single\" w:sz=\"12\" w:space=\"4\" w:color=\"156082\"/><w:bottom w:val=\"single\" w:sz=\"12\" w:space=\"4\" w:color=\"156082\"/><w:right w:val=\"single\" w:sz=\"12\" w:space=\"4\" w:color=\"156082\"/></w:pBdr>" + Shd("E8F1F8")) +
            P(null, R("Nur eine Linie unten, wie unter einer Überschrift."), "<w:pBdr><w:bottom w:val=\"single\" w:sz=\"6\" w:space=\"1\" w:color=\"auto\"/></w:pBdr>") +
            P("berschrift2", R("Tabstopps mit Füllzeichen")) +
            P(null, R("Vorspeise") + Tab() + R("8,50 €"), "<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>") +
            P(null, R("Hauptgang mit Beilage") + Tab() + R("21,00 €"), "<w:tabs><w:tab w:val=\"right\" w:leader=\"dot\" w:pos=\"9062\"/></w:tabs>") +
            P(null, R("Links") + Tab() + R("Mitte") + Tab() + R("1.234,56") + Tab() + R("Rechts"),
                "<w:tabs><w:tab w:val=\"center\" w:pos=\"3000\"/><w:tab w:val=\"decimal\" w:pos=\"6000\"/><w:tab w:val=\"right\" w:leader=\"hyphen\" w:pos=\"9062\"/></w:tabs>") +
            P(null, R("Standard") + Tab() + R("Tab") + Tab() + R("stopps") + Tab() + R("alle 1,25 cm")) +
            P(null, R("Unterstrich-Füllzeichen für ein Formular: Name") + Tab(), "<w:tabs><w:tab w:val=\"right\" w:leader=\"underscore\" w:pos=\"9062\"/></w:tabs>") +
            P("berschrift2", R("Zeilenabstand")) +
            P(null, R("Genau 12 pt: " + Lorem), "<w:spacing w:line=\"240\" w:lineRule=\"exact\"/>") +
            P(null, R("Mindestens 24 pt: " + Lorem), "<w:spacing w:line=\"480\" w:lineRule=\"atLeast\"/>") +
            P(null, R("Doppelt: " + Lorem), "<w:spacing w:line=\"480\" w:lineRule=\"auto\"/>") +
            P(null, R("Automatischer Abstand vor und nach (wie in HTML-Importen): " + Lorem2), "<w:spacing w:before=\"100\" w:beforeAutospacing=\"1\" w:after=\"100\" w:afterAutospacing=\"1\"/>") +
            P("berschrift2", R("Initiale")) + dropCap +
            P(null, R("Blocksatz: " + Lorem + " " + Lorem2), "<w:jc w:val=\"both\"/>") +
            P(null, R("Zentriert und mit Einzug 2 cm links und rechts."), "<w:ind w:left=\"1134\" w:right=\"1134\"/><w:jc w:val=\"center\"/>") +
            P(null, R("Hängender Einzug: " + Lorem2), "<w:ind w:left=\"1134\" w:hanging=\"1134\"/>") +
            Section(extra: "<w:lnNumType w:countBy=\"5\" w:restart=\"continuous\"/>");
        return package.Build(body, WordStyles.Xml(WordVersion.Word2023), "<w:background w:color=\"FBF8EF\"/>");
    }

    /// <summary>Bullets with Symbol / Courier New / Wingdings, a multi-level numbered list with restarts, an outline-numbered heading list.</summary>
    private static byte[] Lists()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Listen" };
        package.AddNumbering(CorpusParts.Numbering());
        string Item(string text, int numId, int level = 0) => P("Listenabsatz", R(text), $"<w:numPr><w:ilvl w:val=\"{level}\"/><w:numId w:val=\"{numId}\"/></w:numPr>");
        var body =
            P("Titel", R("Listen")) +
            P("berschrift1", R("Aufzählung"), "<w:numPr><w:numId w:val=\"2\"/></w:numPr>") +
            Item("Erster Punkt mit dem Symbol-Punkt", 1) + Item("Zweite Ebene mit Courier-o", 1, 1) + Item("Dritte Ebene mit Wingdings-Quadrat", 1, 2) + Item("Wieder oben", 1) +
            P("berschrift1", R("Nummerierung"), "<w:numPr><w:numId w:val=\"2\"/></w:numPr>") +
            P("berschrift2", R("Mehrere Ebenen"), "<w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"2\"/></w:numPr>") +
            Item("Anreise planen", 3) + Item("Zug oder Flug", 3, 1) + Item("Mietwagen", 3, 1) + Item("Versicherung prüfen", 3, 2) + Item("Unterkunft buchen", 3) + Item("Packliste", 3) +
            P("berschrift2", R("Neu beginnen"), "<w:numPr><w:ilvl w:val=\"1\"/><w:numId w:val=\"2\"/></w:numPr>") +
            Item("Diese Liste beginnt wieder bei 1", 5) + Item("und zählt weiter", 5) +
            P("berschrift1", R("Symbole aus Wingdings"), "<w:numPr><w:numId w:val=\"2\"/></w:numPr>") +
            Item("Pfeil", 4) + Item("Häkchen", 4, 1) + Item("Quadrat", 4, 2) +
            Para("Ein Absatz nach den Listen.") + Section();
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>Table styles with banding, autofit and fixed, percent and auto widths, nested tables, row heights, header rows, alignment, text direction, a floating table.</summary>
    private static byte[] Tables()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Tabellen" };
        string C(int width, string text, string tcPr = "", string pPr = "") => Cell(width, P(null, R(text), pPr), tcPr);
        var header = Row(C(2830, "Region") + C(2116, "2024") + C(2116, "2025") + C(2000, "Veränderung"), "<w:tblHeader/>");
        var rows = new[] { ("Nord", "4,3", "4,8", "+12 %"), ("Süd", "3,9", "4,1", "+5 %"), ("Ost", "2,1", "2,6", "+24 %"), ("West", "5,0", "5,2", "+4 %") }
            .Select(row => Row(C(2830, row.Item1) + C(2116, row.Item2, pPr: "<w:jc w:val=\"right\"/>") + C(2116, row.Item3, pPr: "<w:jc w:val=\"right\"/>") + C(2000, row.Item4, pPr: "<w:jc w:val=\"right\"/>")));
        var styled = Table("<w:tblStyle w:val=\"Gitternetztabelle4Akzent1\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/>",
            [2830, 2116, 2116, 2000], [header, .. rows, Row(C(2830, "Summe") + C(2116, "15,3") + C(2116, "16,7") + C(2000, "+9 %"))]);
        var list = Table("<w:tblStyle w:val=\"Listentabelle3Akzent5\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLook w:val=\"0420\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"0\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/>",
            [3020, 3021, 3021],
            Row(C(3020, "Produkt") + C(3021, "Menge") + C(3021, "Preis")), Row(C(3020, "Rucksack") + C(3021, "2") + C(3021, "89,00 €")),
            Row(C(3020, "Schlafsack") + C(3021, "1") + C(3021, "129,00 €")), Row(C(3020, "Zelt") + C(3021, "1") + C(3021, "349,00 €")));
        var nested = Table("<w:tblStyle w:val=\"Tabellenraster\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\"/>", [4531, 4531],
            Row(C(4531, "Außen links") + Cell(4531, P(null, R("Außen rechts mit innerer Tabelle:")) +
                Table("<w:tblStyle w:val=\"Tabellenraster\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\"/>", [2000, 2000], Row(C(2000, "innen 1") + C(2000, "innen 2"))) + P(null, ""))));
        var heights = Table("<w:tblStyle w:val=\"Tabellenraster\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:jc w:val=\"center\"/><w:tblLook w:val=\"04A0\"/>", [1500, 3000, 1500],
            Row(C(1500, "genau 1 cm", "<w:vAlign w:val=\"center\"/>") + C(3000, "Mitte unten", "<w:vAlign w:val=\"bottom\"/>", "<w:jc w:val=\"center\"/>") + C(1500, "gedreht", "<w:textDirection w:val=\"btLr\"/>"), "<w:trHeight w:val=\"567\" w:hRule=\"exact\"/>"),
            Row(C(1500, "mind. 2 cm") + Cell(3000, P(null, R("verbunden über zwei Zeilen")), "<w:vMerge w:val=\"restart\"/><w:vAlign w:val=\"center\"/>" + Shd("FFF2CC")) + C(1500, "x"), "<w:trHeight w:val=\"1134\"/>"),
            Row(C(1500, "frei") + Cell(3000, P(null, ""), "<w:vMerge/>") + C(1500, "y")));
        var fixedTable = Table("<w:tblW w:w=\"6000\" w:type=\"dxa\"/><w:tblInd w:w=\"1134\" w:type=\"dxa\"/><w:tblBorders><w:top w:val=\"double\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:bottom w:val=\"double\" w:sz=\"4\" w:space=\"0\" w:color=\"auto\"/><w:insideH w:val=\"dashed\" w:sz=\"4\" w:space=\"0\" w:color=\"A5A5A5\"/></w:tblBorders><w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"57\" w:type=\"dxa\"/><w:right w:w=\"57\" w:type=\"dxa\"/></w:tblCellMar><w:tblLook w:val=\"04A0\"/>",
            [2000, 2000, 2000], Row(C(2000, "fest 3,5 cm") + C(2000, "eingerückt 2 cm") + C(2000, "schmale Ränder")), Row(C(2000, "ein sehr langes Wort: Donaudampfschifffahrt") + C(2000, "b") + C(2000, "c")));
        var floating = Table("<w:tblpPr w:leftFromText=\"141\" w:rightFromText=\"141\" w:vertAnchor=\"text\" w:horzAnchor=\"margin\" w:tblpXSpec=\"right\" w:tblpY=\"85\"/><w:tblW w:w=\"3000\" w:type=\"dxa\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"ED7D31\"/><w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"ED7D31\"/></w:tblBorders><w:tblLook w:val=\"04A0\"/>",
            [3000], Row(C(3000, "Schwebende Tabelle rechts")), Row(C(3000, "mit Text daneben")));
        var gridBefore = Table("<w:tblStyle w:val=\"Tabellenraster\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\"/>", [2000, 2000, 2000],
            Row(C(2000, "a") + C(2000, "b") + C(2000, "c")), Row(C(4000, "nach Lücke", "<w:gridSpan w:val=\"2\"/>"), "<w:gridBefore w:val=\"1\"/><w:wBefore w:w=\"2000\" w:type=\"dxa\"/>"));
        var body = P("Titel", R("Tabellen")) +
                   P("berschrift1", R("Gitternetztabelle 4 – Akzent 1")) + styled + Para("") +
                   P("berschrift1", R("Listentabelle 3 – Akzent 5, 100 % breit")) + list + Para("") +
                   P("berschrift1", R("Verschachtelt")) + nested + Para("") +
                   P("berschrift1", R("Zeilenhöhe, Ausrichtung, Textrichtung, verbunden")) + heights + Para("") +
                   P("berschrift1", R("Feste Breite, eingerückt, eigene Rahmen")) + fixedTable + Para("") +
                   floating + Para(Lorem + " " + Lorem2) +
                   P("berschrift1", R("Zeile mit Lücke davor")) + gridBefore + Para("") +
                   P("berschrift1", R("Lange Tabelle mit wiederholter Kopfzeile")) +
                   Table("<w:tblStyle w:val=\"Gitternetztabelle4Akzent1\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"0\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/>",
                       [1500, 7562], [Row(C(1500, "Nr.") + C(7562, "Eintrag"), "<w:tblHeader/>"), .. Enumerable.Range(1, 45).Select(index => Row(C(1500, index.ToString()) + C(7562, $"Position {index}: Leistung laut Angebot")))]) +
                   Para("") + Section();
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>Comments with a reply, tracked insertions / deletions / format changes / moves, content controls of every kind, fields, links, bookmarks.</summary>
    private static byte[] Review()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Überarbeitung" };
        var link = package.AddHyperlink("https://www.example.com/reisen");
        package.AddComments(
            "<w:comment w:id=\"0\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:12:00Z\" w:initials=\"AB\">" + P("Kommentartext", "<w:r><w:rPr><w:rStyle w:val=\"Kommentarzeichen\"/></w:rPr><w:annotationRef/></w:r>" + R("Bitte die Zahl prüfen.")) + "</w:comment>" +
            "<w:comment w:id=\"1\" w:author=\"Jonas Keller\" w:date=\"2026-11-10T10:30:00Z\" w:initials=\"JK\">" + P("Kommentartext", "<w:r><w:rPr><w:rStyle w:val=\"Kommentarzeichen\"/></w:rPr><w:annotationRef/></w:r>" + R("Geprüft, stimmt.")) + "</w:comment>");
        var body =
            P("Titel", R("Überarbeitung")) +
            P(null, R("Ein Satz mit ") + "<w:ins w:id=\"10\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:00:00Z\">" + R("eingefügtem ") + "</w:ins>" + R("und ") +
                    "<w:del w:id=\"11\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:01:00Z\"><w:r><w:delText>gelöschtem </w:delText></w:r></w:del>" + R("Text. ") +
                    "<w:r><w:rPr><w:b/><w:rPrChange w:id=\"12\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:02:00Z\"><w:rPr/></w:rPrChange></w:rPr><w:t>Fett gemacht.</w:t></w:r>") +
            P(null, "<w:commentRangeStart w:id=\"0\"/>" + R("Der Umsatz stieg um 12 Prozent.") + "<w:commentRangeEnd w:id=\"0\"/><w:r><w:rPr><w:rStyle w:val=\"Kommentarzeichen\"/></w:rPr><w:commentReference w:id=\"0\"/></w:r>" +
                    "<w:r><w:rPr><w:rStyle w:val=\"Kommentarzeichen\"/></w:rPr><w:commentReference w:id=\"1\"/></w:r>") +
            "<w:p><w:pPr><w:rPr><w:ins w:id=\"13\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:03:00Z\"/></w:rPr></w:pPr><w:ins w:id=\"14\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:03:00Z\">" + R("Ein ganz neuer Absatz.") + "</w:ins></w:p>" +
            P(null, "<w:moveFromRangeStart w:id=\"15\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:04:00Z\" w:name=\"move1\"/><w:moveFrom w:id=\"16\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:04:00Z\">" + R("Verschoben von hier. ") + "</w:moveFrom><w:moveFromRangeEnd w:id=\"15\"/>" + R("Fester Text.")) +
            P(null, R("Fester Text. ") + "<w:moveToRangeStart w:id=\"17\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:04:00Z\" w:name=\"move1\"/><w:moveTo w:id=\"18\" w:author=\"Anna Berger\" w:date=\"2026-11-10T09:04:00Z\">" + R("Verschoben von hier.") + "</w:moveTo><w:moveToRangeEnd w:id=\"17\"/>") +
            P("berschrift1", R("Inhaltssteuerelemente")) +
            P(null, R("Name: ") + "<w:sdt><w:sdtPr><w:alias w:val=\"Name\"/><w:tag w:val=\"name\"/><w:id w:val=\"100\"/><w:text/></w:sdtPr><w:sdtContent>" + R("Anna Berger", Bold) + "</w:sdtContent></w:sdt>") +
            P(null, R("Datum: ") + "<w:sdt><w:sdtPr><w:id w:val=\"101\"/><w:date w:fullDate=\"2026-11-12T00:00:00Z\"><w:dateFormat w:val=\"dd.MM.yyyy\"/><w:lid w:val=\"de-DE\"/><w:storeMappedDataAs w:val=\"dateTime\"/><w:calendar w:val=\"gregorian\"/></w:date></w:sdtPr><w:sdtContent>" + R("12.11.2026") + "</w:sdtContent></w:sdt>") +
            P(null, R("Erledigt: ") + "<w:sdt><w:sdtPr><w:id w:val=\"102\"/><w14:checkbox><w14:checked w14:val=\"1\"/><w14:checkedState w14:val=\"2612\" w14:font=\"MS Gothic\"/><w14:uncheckedState w14:val=\"2610\" w14:font=\"MS Gothic\"/></w14:checkbox></w:sdtPr><w:sdtContent>" +
                    R("☒", "<w:rFonts w:ascii=\"MS Gothic\" w:eastAsia=\"MS Gothic\" w:hAnsi=\"MS Gothic\" w:hint=\"eastAsia\"/>") + "</w:sdtContent></w:sdt>") +
            P(null, R("Auswahl: ") + "<w:sdt><w:sdtPr><w:id w:val=\"103\"/><w:dropDownList><w:listItem w:displayText=\"Bahn\" w:value=\"Bahn\"/><w:listItem w:displayText=\"Flug\" w:value=\"Flug\"/></w:dropDownList></w:sdtPr><w:sdtContent>" + R("Bahn") + "</w:sdtContent></w:sdt>") +
            P(null, R("Leer: ") + "<w:sdt><w:sdtPr><w:id w:val=\"104\"/><w:showingPlcHdr/><w:text/></w:sdtPr><w:sdtContent>" + R("Klicken oder tippen Sie hier, um Text einzugeben.", "<w:rStyle w:val=\"Platzhaltertext\"/>") + "</w:sdtContent></w:sdt>") +
            "<w:sdt><w:sdtPr><w:alias w:val=\"Hinweis\"/><w:id w:val=\"105\"/></w:sdtPr><w:sdtContent>" + P("Zitat", R("Ein ganzer Absatz in einem Rich-Text-Steuerelement.")) + Para("Und ein zweiter darin.") + "</w:sdtContent></w:sdt>" +
            P("berschrift1", R("Felder und Links")) +
            P(null, R("Autor: ") + Field("AUTHOR  \\* MERGEFORMAT", "Anna Berger") + R(", gespeichert: ") + Field("SAVEDATE  \\@ \"dd.MM.yyyy\"  \\* MERGEFORMAT", "12.11.2026") + R(", Dateiname: ") +
                    "<w:fldSimple w:instr=\" FILENAME   \\* MERGEFORMAT \">" + R("Überarbeitung.docx") + "</w:fldSimple>" + R(".")) +
            P(null, R("Mehr unter ") + Hyperlink(link, "www.example.com/reisen") + R(" und ") + "<w:hyperlink w:anchor=\"Ziel\" w:history=\"1\">" + R("im Dokument", "<w:rStyle w:val=\"Hyperlink\"/>") + "</w:hyperlink>" + R(".")) +
            P(null, "<w:bookmarkStart w:id=\"20\" w:name=\"Ziel\"/>" + R("Hier ist das Ziel des Links.") + "<w:bookmarkEnd w:id=\"20\"/>") +
            P(null, R("Gleichung: ") + "<m:oMath><m:r><m:t>a²+b²=c²</m:t></m:r></m:oMath>") +
            Section();
        var placeholder = "<w:style w:type=\"character\" w:styleId=\"Platzhaltertext\"><w:name w:val=\"Placeholder Text\"/><w:basedOn w:val=\"Absatz-Standardschriftart\"/><w:uiPriority w:val=\"99\"/><w:semiHidden/><w:rPr><w:color w:val=\"666666\"/></w:rPr></w:style>";
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013, placeholder));
    }

    /// <summary>A DIN 5008 letter (Form B): the address field as a text box at its fixed place, the info block, fold and punch marks, a footer table.</summary>
    private static byte[] Letter()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Brief" };
        var logo = "rIdLogo";
        package.AddMedia("image1.png", Picture(600, 150, Blue, new SKColor(0x1F, 0x38, 0x64), logo: true));
        var header = package.AddHeader(1, P("Kopfzeile", R(""), "<w:jc w:val=\"right\"/>") +
                                          P("Kopfzeile", FloatingPicture(logo, 5, 1.25, new Position("page", 14, null, "page", 1.2), "none", name: "Logo")),
            [("rIdLogo", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/image", "media/image1.png")]);
        string FooterCell(string lines) => Cell(3020, string.Concat(lines.Split('|').Select(line => P("Fuzeile", R(line, Size(8) + Color("595959"))))));
        var footer = package.AddFooter(1,
            Table("<w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders><w:top w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"A6A6A6\"/></w:tblBorders><w:tblLook w:val=\"04A0\"/>", [3020, 3021, 3021],
                Row(FooterCell("Nordlicht Reisen GmbH|Hafenstraße 12|20457 Hamburg") + FooterCell("Telefon 040 123456-0|info@nordlicht.example|www.nordlicht.example") + FooterCell("Hamburger Sparkasse|IBAN DE12 2005 0550 1234 5678 90|BIC HASPDEHHXXX"))) +
            P("Fuzeile", R("Seite ", Size(8)) + Field("PAGE", "1", Size(8)) + R(" von ", Size(8)) + Field("NUMPAGES", "1", Size(8)), "<w:jc w:val=\"right\"/>"));
        var address = P(null, R("Nordlicht Reisen GmbH · Hafenstraße 12 · 20457 Hamburg", Size(7) + "<w:u w:val=\"single\"/>"), "<w:spacing w:after=\"120\" w:line=\"240\" w:lineRule=\"auto\"/>") +
                      string.Concat(new[] { "Frau", "Dr. Clara Hoffmann", "Wiesenweg 7", "", "80331 München" }.Select(line => P("KeinLeerraum", R(line))));
        var info = string.Concat(new[] { ("Ihr Zeichen:", "CH 11/26"), ("Unser Zeichen:", "AB-4711"), ("Name:", "Anna Berger"), ("Telefon:", "040 123456-21"), ("Datum:", "12.11.2026") }
            .Select(entry => P("KeinLeerraum", R(entry.Item1, Size(9) + Color("595959")) + Tab() + R(entry.Item2, Size(9)), "<w:tabs><w:tab w:val=\"left\" w:pos=\"1418\"/></w:tabs>")));
        var marks =
            Shape("line", 0.5, 0, new Position("page", 0.5, null, "page", 10.5), "none", NoFill, Line("808080", 0.5), name: "Falzmarke") +
            Shape("line", 0.8, 0, new Position("page", 0.5, null, "page", 14.85), "none", NoFill, Line("808080", 0.5), name: "Lochmarke") +
            Shape("line", 0.5, 0, new Position("page", 0.5, null, "page", 21), "none", NoFill, Line("808080", 0.5), name: "Falzmarke");
        var body =
            P("KeinLeerraum", marks +
                Shape("rect", 8.5, 4.5, new Position("page", 2.5, null, "page", 4.5), "none", NoFill, NoLine, address, isTextBox: true, name: "Anschriftfeld") +
                Shape("rect", 7.5, 4.5, new Position("page", 12.5, null, "page", 5), "none", NoFill, NoLine, info, isTextBox: true, name: "Informationsblock")) +
            P("KeinLeerraum", "", "<w:spacing w:before=\"5600\"/>") +
            P(null, R("Ihre Anfrage vom 3. November 2026 – Gruppenreise nach Tromsø", Bold), "<w:spacing w:before=\"480\" w:after=\"480\"/>") +
            Para("Sehr geehrte Frau Dr. Hoffmann,") +
            P(null, R("vielen Dank für Ihre Anfrage. Gerne bieten wir Ihnen für Ihre Gruppe von zwölf Personen die folgende Reise an: sieben Nächte in Tromsø mit Halbpension, " +
                      "zwei geführte Ausflüge zu den Nordlichtern und eine Fahrt mit dem Hurtigruten-Schiff nach Hammerfest."), "<w:jc w:val=\"both\"/>") +
            P(null, R("Der Preis beträgt 1.890,00 € pro Person im Doppelzimmer. Bitte geben Sie uns bis zum 30. November 2026 Bescheid, damit wir die Zimmer für Sie reservieren können."), "<w:jc w:val=\"both\"/>") +
            Para("Für Fragen bin ich jederzeit für Sie da.") +
            P(null, R("Mit freundlichen Grüßen"), "<w:spacing w:after=\"720\"/>") +
            Para("Anna Berger") + P(null, R("Reiseberatung", Color("595959"))) +
            P(null, R("Anlage", Bold) + Br() + R("Reiseverlauf (2 Seiten)")) +
            Section(top: 1701, bottom: 1985, left: 1418, right: 1134, references: $"<w:headerReference w:type=\"default\" r:id=\"{header}\"/><w:footerReference w:type=\"default\" r:id=\"{footer}\"/>");
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>An invoice: a header table with logo and sender, positions with decimal tabs and shading, totals.</summary>
    private static byte[] Invoice()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2023) { Title = "Rechnung" };
        var logo = package.AddImage("image1.png", Picture(480, 120, new SKColor(0x15, 0x60, 0x82), new SKColor(0xE9, 0x71, 0x32), logo: true));
        var footer = PageXofY(package, 1);
        string C(int width, string content, string tcPr = "") => Cell(width, content, tcPr);
        var top = Table("<w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/><w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders><w:tblLook w:val=\"04A0\"/>",
            [4531, 4531],
            Row(C(4531, P(null, InlinePicture(logo, 5, 1.25, "Logo"))) +
                C(4531, P(null, R("Fjord & Feld Ausrüstung", Bold), "<w:jc w:val=\"right\"/><w:spacing w:after=\"0\"/>") + P(null, R("Lindenallee 3 · 50667 Köln"), "<w:jc w:val=\"right\"/><w:spacing w:after=\"0\"/>") +
                             P(null, R("USt-IdNr. DE123456789"), "<w:jc w:val=\"right\"/>"))));
        var positions = new[] { ("1", "Trekkingrucksack 45 l", "2", "89,00", "178,00"), ("2", "Daunenschlafsack bis −10 °C", "1", "229,00", "229,00"), ("3", "Kuppelzelt für 2 Personen", "1", "349,90", "349,90"), ("4", "Versand", "1", "4,95", "4,95") };
        string Text(string text, string jc = "left", string rPr = "") => P(null, R(text, rPr), $"<w:spacing w:after=\"0\"/><w:jc w:val=\"{jc}\"/>");
        var table = Table("<w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders><w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"0\" w:color=\"156082\"/><w:insideH w:val=\"single\" w:sz=\"4\" w:space=\"0\" w:color=\"D9D9D9\"/></w:tblBorders><w:tblLayout w:type=\"fixed\"/><w:tblLook w:val=\"04A0\"/>",
            [567, 4536, 851, 1559, 1549],
            [
                Row(C(567, Text("Pos.", rPr: Bold + Color("FFFFFF")), Shd("156082")) + C(4536, Text("Bezeichnung", rPr: Bold + Color("FFFFFF")), Shd("156082")) + C(851, Text("Menge", "right", Bold + Color("FFFFFF")), Shd("156082")) +
                    C(1559, Text("Einzelpreis €", "right", Bold + Color("FFFFFF")), Shd("156082")) + C(1549, Text("Gesamt €", "right", Bold + Color("FFFFFF")), Shd("156082")), "<w:tblHeader/>"),
                .. positions.Select((row, index) => Row(C(567, Text(row.Item1), index % 2 == 1 ? Shd("F2F2F2") : "") + C(4536, Text(row.Item2), index % 2 == 1 ? Shd("F2F2F2") : "") +
                                                        C(851, Text(row.Item3, "right"), index % 2 == 1 ? Shd("F2F2F2") : "") + C(1559, Text(row.Item4, "right"), index % 2 == 1 ? Shd("F2F2F2") : "") +
                                                        C(1549, Text(row.Item5, "right"), index % 2 == 1 ? Shd("F2F2F2") : ""))),
            ]);
        string Total(string label, string value, string rPr = "") =>
            P(null, Tab() + R(label, rPr) + Tab() + R(value, rPr), "<w:tabs><w:tab w:val=\"left\" w:pos=\"5103\"/><w:tab w:val=\"decimal\" w:pos=\"8789\"/></w:tabs><w:spacing w:after=\"0\"/>");
        var body = top + Para("") +
                   P("KeinLeerraum", R("Herrn")) + P("KeinLeerraum", R("Jonas Keller")) + P("KeinLeerraum", R("Bergstraße 21")) + P(null, R("69117 Heidelberg")) +
                   P("Titel", R("Rechnung Nr. 2026-0815", Size(20))) +
                   P(null, R("Rechnungsdatum:") + Tab() + R("12.11.2026") + Br() + R("Lieferdatum:") + Tab() + R("10.11.2026") + Br() + R("Kundennummer:") + Tab() + R("K-1042"),
                       "<w:tabs><w:tab w:val=\"left\" w:pos=\"2268\"/></w:tabs>") +
                   table + Para("") +
                   Total("Zwischensumme", "761,85") + Total("zzgl. 19 % MwSt.", "144,75") +
                   P(null, Tab() + R("Rechnungsbetrag", Bold) + Tab() + R("906,60 €", Bold), "<w:pBdr><w:top w:val=\"single\" w:sz=\"4\" w:space=\"1\" w:color=\"auto\"/></w:pBdr><w:tabs><w:tab w:val=\"left\" w:pos=\"5103\"/><w:tab w:val=\"decimal\" w:pos=\"8789\"/></w:tabs>") +
                   Para("Bitte überweisen Sie den Betrag innerhalb von 14 Tagen ohne Abzug. Vielen Dank für Ihren Einkauf!") +
                   Section(references: $"<w:footerReference w:type=\"default\" r:id=\"{footer}\"/>");
        return package.Build(body, WordStyles.Xml(WordVersion.Word2023));
    }

    /// <summary>A CV built from a two-column table without borders (shaded left column with photo), headings with a bottom border, skill bars as shapes.</summary>
    private static byte[] CurriculumVitae()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Lebenslauf" };
        var photo = package.AddImage("image1.png", Picture(300, 380, new SKColor(0xB4, 0xC7, 0xE7), new SKColor(0x2F, 0x54, 0x96)));
        var heading = "<w:style w:type=\"paragraph\" w:customStyle=\"1\" w:styleId=\"Abschnitt\"><w:name w:val=\"Abschnitt\"/><w:basedOn w:val=\"Standard\"/><w:next w:val=\"Standard\"/><w:qFormat/>" +
                      "<w:pPr><w:pBdr><w:bottom w:val=\"single\" w:sz=\"8\" w:space=\"1\" w:color=\"2F5496\" w:themeColor=\"accent1\" w:themeShade=\"BF\"/></w:pBdr><w:spacing w:before=\"360\" w:after=\"120\"/></w:pPr>" +
                      "<w:rPr><w:caps/><w:color w:val=\"2F5496\" w:themeColor=\"accent1\" w:themeShade=\"BF\"/><w:spacing w:val=\"20\"/><w:sz w:val=\"26\"/></w:rPr></w:style>";
        string Skill(string name, double level) => P(null, R(name, Size(9) + Color("FFFFFF")) +
            Shape("rect", 4.5 * level, 0.25, new Position("column", 0, null, "line", 0.45), "topAndBottom", SolidFill("FFFFFF"), NoLine, name: "Balken"), "<w:spacing w:after=\"240\"/>");
        var left = P(null, InlinePicture(photo, 4.5, 5.7, "Foto", "Porträt"), "<w:jc w:val=\"center\"/>") +
                   P(null, R("KONTAKT", Bold + Color("FFFFFF") + "<w:spacing w:val=\"20\"/>"), "<w:spacing w:before=\"240\"/>") +
                   P(null, R("clara.hoffmann@example.com", Size(9) + Color("FFFFFF"))) + P(null, R("+49 170 1234567", Size(9) + Color("FFFFFF"))) + P(null, R("München", Size(9) + Color("FFFFFF"))) +
                   P(null, R("KENNTNISSE", Bold + Color("FFFFFF") + "<w:spacing w:val=\"20\"/>"), "<w:spacing w:before=\"240\"/>") +
                   Skill("Projektleitung", 0.9) + Skill("Norwegisch", 0.6) + Skill("Kalkulation", 0.75);
        string Station(string years, string title, string place, string text) =>
            P(null, R(years, Bold + Color("2F5496", "accent1", "BF")) + Tab() + R(title, Bold), "<w:tabs><w:tab w:val=\"left\" w:pos=\"1701\"/></w:tabs><w:spacing w:after=\"0\"/>") +
            P(null, Tab() + R(place, Italic + Color("595959")), "<w:tabs><w:tab w:val=\"left\" w:pos=\"1701\"/></w:tabs><w:spacing w:after=\"0\"/>") +
            P(null, R(text), "<w:ind w:left=\"1701\"/>");
        var right = P("Titel", R("Dr. Clara Hoffmann")) + P("Untertitel", R("Reiseleiterin und Projektmanagerin")) +
                    P("Abschnitt", R("Profil")) + Para("Seit zwölf Jahren plane und begleite ich Gruppenreisen in Skandinavien – mit Leidenschaft für Natur, Organisation und gute Geschichten.") +
                    P("Abschnitt", R("Berufserfahrung")) +
                    Station("seit 2019", "Leitung Gruppenreisen", "Nordlicht Reisen GmbH, Hamburg", "Verantwortung für 40 Reisen im Jahr, Budget 2,4 Mio. €, Team von sechs Personen.") +
                    Station("2014–2019", "Reiseleiterin", "Fjordtours AS, Bergen", "Touren in Norwegen und Schweden, Ausbildung neuer Reiseleiter.") +
                    P("Abschnitt", R("Ausbildung")) +
                    Station("2010–2014", "Promotion Geografie", "Universität Hamburg", "Thema: Nachhaltiger Tourismus in arktischen Regionen.");
        var body = Table("<w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblBorders><w:top w:val=\"nil\"/><w:left w:val=\"nil\"/><w:bottom w:val=\"nil\"/><w:right w:val=\"nil\"/><w:insideH w:val=\"nil\"/><w:insideV w:val=\"nil\"/></w:tblBorders>" +
                         "<w:tblLayout w:type=\"fixed\"/><w:tblCellMar><w:left w:w=\"227\" w:type=\"dxa\"/><w:right w:w=\"227\" w:type=\"dxa\"/></w:tblCellMar><w:tblLook w:val=\"0600\"/>",
                       [3402, 7088], Row(Cell(3402, left, Shd("2F5496", "accent1", null) + "<w:tcMar><w:top w:w=\"340\" w:type=\"dxa\"/></w:tcMar>") + Cell(7088, right, "<w:tcMar><w:top w:w=\"340\" w:type=\"dxa\"/><w:left w:w=\"454\" w:type=\"dxa\"/></w:tcMar>"), "<w:trHeight w:val=\"15300\"/>")) +
                   P("KeinLeerraum", "") + Section(top: 567, right: 567, bottom: 567, left: 567);
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013, heading));
    }

    /// <summary>A report: different first page, even/odd headers, footnotes and endnotes, a landscape section with a wide table and a chart.</summary>
    private static byte[] LandscapeReport()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2013) { Title = "Bericht", EvenAndOddHeaders = true };
        package.AddFootnotes(
            "<w:footnote w:id=\"1\">" + P("Funotentext", "<w:r><w:rPr><w:rStyle w:val=\"Funotenzeichen\"/></w:rPr><w:footnoteRef/></w:r>" + R(" Quelle: Statistisches Bundesamt, Tourismus 2026.")) + "</w:footnote>" +
            "<w:footnote w:id=\"2\">" + P("Funotentext", "<w:r><w:rPr><w:rStyle w:val=\"Funotenzeichen\"/></w:rPr><w:footnoteRef/></w:r>" + R(" Ohne Geschäftsreisen.")) + "</w:footnote>");
        package.AddEndnotes("<w:endnote w:id=\"1\">" + P("Endnotentext", "<w:r><w:rPr><w:rStyle w:val=\"Endnotenzeichen\"/></w:rPr><w:endnoteRef/></w:r>" + R(" Alle Zahlen vorläufig.")) + "</w:endnote>");
        var odd = Header(package, 1, "Marktbericht Skandinavien – ungerade Seite");
        var even = package.AddHeader(2, P("Kopfzeile", R("Gerade Seite – Marktbericht", Color("7F7F7F"))));
        var first = package.AddHeader(3, P("Kopfzeile", ""));
        var footer = PageXofY(package, 1);
        var chart = package.AddChart(1, CorpusParts.ColumnChart("Übernachtungen (Tsd.)", ["Norwegen", "Schweden", "Finnland", "Dänemark"], ("2025", [820, 640, 410, 390]), ("2026", [905, 700, 470, 395])));
        var references = $"<w:headerReference w:type=\"even\" r:id=\"{even}\"/><w:headerReference w:type=\"default\" r:id=\"{odd}\"/><w:headerReference w:type=\"first\" r:id=\"{first}\"/><w:footerReference w:type=\"default\" r:id=\"{footer}\"/>";
        string C(int width, string text, bool bold = false) => Cell(width, P(null, R(text, bold ? Bold : ""), "<w:spacing w:after=\"0\"/>"));
        var wide = Table("<w:tblStyle w:val=\"Gitternetztabelle4Akzent1\"/><w:tblW w:w=\"5000\" w:type=\"pct\"/><w:tblLook w:val=\"04A0\" w:firstRow=\"1\" w:lastRow=\"0\" w:firstColumn=\"1\" w:lastColumn=\"0\" w:noHBand=\"0\" w:noVBand=\"1\"/>",
            [2200, 1500, 1500, 1500, 1500, 1500, 1500, 1500, 1300],
            [Row(string.Concat(new[] { "Land", "Jan", "Feb", "Mär", "Apr", "Mai", "Jun", "Jul", "Summe" }.Select((text, index) => C(index == 0 ? 2200 : index == 8 ? 1300 : 1500, text))), "<w:tblHeader/>"),
             .. new[] { "Norwegen", "Schweden", "Finnland", "Dänemark", "Island" }.Select((land, row) => Row(C(2200, land) + string.Concat(Enumerable.Range(0, 7).Select(month => C(1500, ((row + 2) * 13 + month * 7).ToString()))) + C(1300, ((row + 2) * 91 + 147).ToString())))]);
        var body =
            P("Titel", R("Marktbericht Skandinavien")) + P("Untertitel", R("Stand November 2026")) +
            P(null, R("Die Zahl der Übernachtungen deutscher Gäste in Skandinavien stieg 2026 um 9 Prozent.") + "<w:r><w:rPr><w:rStyle w:val=\"Funotenzeichen\"/></w:rPr><w:footnoteReference w:id=\"1\"/></w:r>" +
                    R(" Norwegen bleibt das beliebteste Ziel.") + "<w:r><w:rPr><w:rStyle w:val=\"Funotenzeichen\"/></w:rPr><w:footnoteReference w:id=\"2\"/></w:r>" + R(" " + Lorem)) +
            Paragraphs(6) + P(null, R("Einzelheiten zeigt die Tabelle auf der nächsten Seite.") + "<w:r><w:rPr><w:rStyle w:val=\"Endnotenzeichen\"/></w:rPr><w:endnoteReference w:id=\"1\"/></w:r>") +
            P(null, "", Section(references: references, titlePage: true)) +
            P("berschrift1", R("Übernachtungen nach Monat")) + wide + Para("") + P(null, InlineChart(chart, 20, 7), "<w:jc w:val=\"center\"/>") +
            P(null, "", Section(16838, 11906, landscape: true, type: "nextPage", references: "")) +
            P("berschrift1", R("Fazit")) + Paragraphs(4) +
            Section(references: references);
        return package.Build(body, WordStyles.Xml(WordVersion.Word2013));
    }

    /// <summary>A document in Word 2007 compatibility mode: Cambria / Calibri, the old heading and title styles (title with a bottom border).</summary>
    private static byte[] Office2007()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2007) { Title = "Protokoll", CompatibilityMode = 12 };
        var body = P("Titel", R("Protokoll der Teamsitzung")) +
                   P("berschrift1", R("Teilnehmer")) + Para("Anna Berger, Jonas Keller, Clara Hoffmann, Malte Sørensen") +
                   P("berschrift1", R("Tagesordnung")) + P("berschrift2", R("1. Rückblick")) + Para(Lorem) + P("berschrift2", R("2. Planung")) + Para(Lorem2) +
                   P("berschrift3", R("Offene Punkte")) + Para("Die Kalkulation für Island liegt bis Freitag vor.") +
                   Table("<w:tblStyle w:val=\"Tabellenraster\"/><w:tblW w:w=\"0\" w:type=\"auto\"/><w:tblLook w:val=\"04A0\"/>", [3070, 3071, 3071],
                       Row(Cell(3070, Para("Aufgabe")) + Cell(3071, Para("Wer")) + Cell(3071, Para("Bis"))), Row(Cell(3070, Para("Angebot Tromsø")) + Cell(3071, Para("AB")) + Cell(3071, Para("30.11.")))) +
                   Para("") + Section();
        return package.Build(body, WordStyles.Xml(WordVersion.Word2007));
    }

    /// <summary>A newsletter: a title banner shape across the page, three columns, a drop cap, a pull-quote text box, a picture with tight wrap.</summary>
    private static byte[] Newsletter()
    {
        Reset();
        var package = new WordPackage(OfficeTheme.Office2023) { Title = "Newsletter" };
        var photo = package.AddImage("image1.png", Picture(500, 330, new SKColor(0x0F, 0x9E, 0xD5), new SKColor(0x15, 0x60, 0x82)));
        var banner = P(null, R("NORDLICHT NEWS", Font("Aptos Display") + Size(32) + Bold + Color("FFFFFF")), "<w:jc w:val=\"center\"/><w:spacing w:after=\"0\"/>") +
                     P(null, R("Ausgabe 11 · November 2026", Size(11) + Color("FFFFFF")), "<w:jc w:val=\"center\"/>");
        var quote = P("Zitat", R("„Die beste Reisezeit für Nordlichter beginnt jetzt.“", Size(14) + Color("156082", "accent1")));
        var body =
            P(null, Shape("rect", 19, 3, new Position("page", null, "center", "page", 1), "topAndBottom", SolidFill("156082", "accent1"), NoLine, banner, name: "Banner", anchorText: "ctr")) +
            P(null, "", Section(type: "", columns: "<w:cols w:space=\"708\"/>", top: 2268, bottom: 1134, left: 1134, right: 1134)) +
            P("berschrift1", R("Polarnächte erleben")) +
            P(null, R("W", Size(40) + Color("156082", "accent1")), "<w:keepNext/><w:framePr w:dropCap=\"drop\" w:lines=\"2\" w:wrap=\"around\" w:vAnchor=\"text\" w:hAnchor=\"text\"/><w:spacing w:after=\"0\" w:line=\"640\" w:lineRule=\"exact\"/><w:textAlignment w:val=\"baseline\"/>") +
            Para("enn die Sonne im Dezember nicht mehr aufgeht, beginnt in Nordnorwegen die schönste Zeit des Jahres. " + Lorem) +
            P(null, FloatingPicture(photo, 5, 3.3, new Position("column", null, "center", "paragraph", 0.2), "tight", name: "Bild") + R(Lorem2)) +
            Paragraphs(2) +
            P(null, Shape("rect", 5.2, 2.6, new Position("column", 0, null, "paragraph", 0.2), "topAndBottom", NoFill, "<a:ln w=\"19050\"><a:solidFill><a:schemeClr val=\"accent2\"/></a:solidFill></a:ln>", quote, isTextBox: true, name: "Zitat")) +
            P("berschrift1", R("Neue Reisen 2027")) + Paragraphs(3) +
            P("berschrift1", R("Termine")) +
            P(null, R("4. Dezember") + Tab() + R("Vortrag in Hamburg"), "<w:tabs><w:tab w:val=\"left\" w:pos=\"1418\"/></w:tabs>") +
            P(null, R("11. Dezember") + Tab() + R("Messe in Köln"), "<w:tabs><w:tab w:val=\"left\" w:pos=\"1418\"/></w:tabs>") +
            Paragraphs(2) +
            Section(type: "continuous", columns: "<w:cols w:num=\"3\" w:space=\"425\"/>", top: 2268, bottom: 1134, left: 1134, right: 1134);
        return package.Build(body, WordStyles.Xml(WordVersion.Word2023));
    }
}
