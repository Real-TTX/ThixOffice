using System.IO.Compression;
using System.Text;

namespace ThixWrite.Core.Tests.RealWorld;

/// <summary>
/// Writes a .docx part by part the way Word writes it: [Content_Types].xml, the package and part relationships, docProps, the main
/// document with styles, settings, font table, web settings and theme, plus whatever a document adds (numbering, headers, footers,
/// notes, comments, pictures, charts, SmartArt). The XML of every part is given as Word would write it – the corpus is faithful to
/// Word's output, not to what ThixWrite reads.
/// </summary>
internal sealed class WordPackage
{
    public const string Namespaces =
        "xmlns:wpc=\"http://schemas.microsoft.com/office/word/2010/wordprocessingCanvas\" " +
        "xmlns:cx=\"http://schemas.microsoft.com/office/drawing/2014/chartex\" " +
        "xmlns:mc=\"http://schemas.openxmlformats.org/markup-compatibility/2006\" " +
        "xmlns:o=\"urn:schemas-microsoft-com:office:office\" " +
        "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\" " +
        "xmlns:m=\"http://schemas.openxmlformats.org/officeDocument/2006/math\" " +
        "xmlns:v=\"urn:schemas-microsoft-com:vml\" " +
        "xmlns:wp14=\"http://schemas.microsoft.com/office/word/2010/wordprocessingDrawing\" " +
        "xmlns:wp=\"http://schemas.openxmlformats.org/drawingml/2006/wordprocessingDrawing\" " +
        "xmlns:w10=\"urn:schemas-microsoft-com:office:word\" " +
        "xmlns:w=\"http://schemas.openxmlformats.org/wordprocessingml/2006/main\" " +
        "xmlns:w14=\"http://schemas.microsoft.com/office/word/2010/wordml\" " +
        "xmlns:w15=\"http://schemas.microsoft.com/office/word/2012/wordml\" " +
        "xmlns:wpg=\"http://schemas.microsoft.com/office/word/2010/wordprocessingGroup\" " +
        "xmlns:wpi=\"http://schemas.microsoft.com/office/word/2010/wordprocessingInk\" " +
        "xmlns:wne=\"http://schemas.microsoft.com/office/word/2006/wordml\" " +
        "xmlns:wps=\"http://schemas.microsoft.com/office/word/2010/wordprocessingShape\" " +
        "mc:Ignorable=\"w14 w15 wp14\"";

    private const string Rel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    private const string XmlHeader = "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>\r\n";

    private readonly List<(string Path, byte[] Content)> _parts = [];
    private readonly List<(string Id, string Type, string Target, bool External)> _documentRels = [];
    private readonly Dictionary<string, string> _overrides = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _defaults = new(StringComparer.OrdinalIgnoreCase)
    {
        ["rels"] = "application/vnd.openxmlformats-package.relationships+xml",
        ["xml"] = "application/xml",
    };

    private int _nextRel = 1;

    public WordPackage(OfficeTheme theme)
    {
        Theme = theme;
    }

    public OfficeTheme Theme { get; }

    /// <summary>The settings' compatibility mode (15 = Word 2013+, 14 = 2010, 12 = 2007).</summary>
    public int CompatibilityMode { get; init; } = 15;

    /// <summary>Extra children of w:settings (before w:compat), as Word orders them: w:displayBackgroundShape, w:evenAndOddHeaders …</summary>
    public string SettingsExtra { get; init; } = "";

    public string Title { get; init; } = "";

    /// <summary>w:evenAndOddHeaders ("Gerade/ungerade anders").</summary>
    public bool EvenAndOddHeaders { get; init; }

    /// <summary>A relationship of the main document; returns its id ("rId7").</summary>
    public string AddDocumentRelationship(string type, string target, bool external = false)
    {
        var id = "rId" + _nextRel++;
        _documentRels.Add((id, type, target, external));
        return id;
    }

    public void AddPart(string path, string contentType, string xml, IEnumerable<(string Id, string Type, string Target)>? relationships = null)
    {
        _overrides["/" + path] = contentType;
        _parts.Add((path, Encoding.UTF8.GetBytes(XmlHeader + xml)));
        if (relationships is not null && relationships.Any())
        {
            var folder = Path.GetDirectoryName(path)!.Replace('\\', '/');
            var name = Path.GetFileName(path);
            _parts.Add(($"{folder}/_rels/{name}.rels", Encoding.UTF8.GetBytes(Relationships(relationships.Select(r => (r.Id, r.Type, r.Target, false))))));
        }
    }

    public string AddHyperlink(string uri) => AddDocumentRelationship(Rel + "/hyperlink", uri, external: true);

    /// <summary>A picture in word/media; returns the relationship id from the main document.</summary>
    public string AddImage(string name, byte[] bytes)
    {
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        _defaults[extension] = extension switch { "png" => "image/png", "jpeg" or "jpg" => "image/jpeg", "gif" => "image/gif", _ => "image/" + extension };
        _parts.Add(("word/media/" + name, bytes));
        return AddDocumentRelationship(Rel + "/image", "media/" + name);
    }

    /// <summary>A picture for another part (header): the bytes go to word/media, the caller relates them.</summary>
    public void AddMedia(string name, byte[] bytes)
    {
        var extension = Path.GetExtension(name).TrimStart('.').ToLowerInvariant();
        _defaults[extension] = extension == "png" ? "image/png" : "image/jpeg";
        _parts.Add(("word/media/" + name, bytes));
    }

    public string AddHeader(int number, string bodyXml, IEnumerable<(string Id, string Type, string Target)>? relationships = null)
    {
        AddPart($"word/header{number}.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.header+xml",
            $"<w:hdr {Namespaces}>{bodyXml}</w:hdr>", relationships);
        return AddDocumentRelationship(Rel + "/header", $"header{number}.xml");
    }

    public string AddFooter(int number, string bodyXml, IEnumerable<(string Id, string Type, string Target)>? relationships = null)
    {
        AddPart($"word/footer{number}.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footer+xml",
            $"<w:ftr {Namespaces}>{bodyXml}</w:ftr>", relationships);
        return AddDocumentRelationship(Rel + "/footer", $"footer{number}.xml");
    }

    public void AddNumbering(string xml)
    {
        AddPart("word/numbering.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.numbering+xml", $"<w:numbering {Namespaces}>{xml}</w:numbering>");
        AddDocumentRelationship(Rel + "/numbering", "numbering.xml");
    }

    /// <summary>Footnotes (with Word's separator notes -1/0 added) – <paramref name="notesXml"/> holds the w:footnote elements from id 1.</summary>
    public void AddFootnotes(string notesXml)
    {
        AddPart("word/footnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.footnotes+xml",
            $"<w:footnotes {Namespaces}>{Separators("footnote")}{notesXml}</w:footnotes>");
        AddDocumentRelationship(Rel + "/footnotes", "footnotes.xml");
    }

    public void AddEndnotes(string notesXml)
    {
        AddPart("word/endnotes.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.endnotes+xml",
            $"<w:endnotes {Namespaces}>{Separators("endnote")}{notesXml}</w:endnotes>");
        AddDocumentRelationship(Rel + "/endnotes", "endnotes.xml");
    }

    public void AddComments(string commentsXml, string? extendedXml = null)
    {
        AddPart("word/comments.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.comments+xml", $"<w:comments {Namespaces}>{commentsXml}</w:comments>");
        AddDocumentRelationship(Rel + "/comments", "comments.xml");
        if (extendedXml is not null)
        {
            AddPart("word/commentsExtended.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.commentsExtended+xml",
                $"<w15:commentsEx {Namespaces}>{extendedXml}</w15:commentsEx>");
            AddDocumentRelationship("http://schemas.microsoft.com/office/2011/relationships/commentsExtended", "commentsExtended.xml");
        }
    }

    /// <summary>A chart part (c:chartSpace as Word writes it, with cached values); returns the relationship id.</summary>
    public string AddChart(int number, string chartSpaceXml)
    {
        AddPart($"word/charts/chart{number}.xml", "application/vnd.openxmlformats-officedocument.drawingml.chart+xml", chartSpaceXml);
        return AddDocumentRelationship(Rel + "/chart", $"charts/chart{number}.xml");
    }

    /// <summary>The five parts of a SmartArt (data, layout, quick style, colours, drawing); returns the ids for dgm:relIds (dm, lo, qs, cs).</summary>
    public (string Data, string Layout, string Style, string Colors) AddDiagram(int number, string dataXml, string layoutXml, string styleXml, string colorsXml, string drawingXml)
    {
        const string Ns = "application/vnd.openxmlformats-officedocument.drawingml.";
        var data = AddDocumentRelationship(Rel + "/diagramData", $"diagrams/data{number}.xml");
        var layout = AddDocumentRelationship(Rel + "/diagramLayout", $"diagrams/layout{number}.xml");
        var style = AddDocumentRelationship(Rel + "/diagramQuickStyle", $"diagrams/quickStyle{number}.xml");
        var colors = AddDocumentRelationship(Rel + "/diagramColors", $"diagrams/colors{number}.xml");
        var drawing = AddDocumentRelationship("http://schemas.microsoft.com/office/2007/relationships/diagramDrawing", $"diagrams/drawing{number}.xml");
        AddPart($"word/diagrams/data{number}.xml", Ns + "diagramData+xml", dataXml.Replace("rIdSmartArtDrawing", drawing, StringComparison.Ordinal));
        AddPart($"word/diagrams/layout{number}.xml", Ns + "diagramLayout+xml", layoutXml);
        AddPart($"word/diagrams/quickStyle{number}.xml", Ns + "diagramStyle+xml", styleXml);
        AddPart($"word/diagrams/colors{number}.xml", Ns + "diagramColors+xml", colorsXml);
        AddPart($"word/diagrams/drawing{number}.xml", "application/vnd.ms-office.drawingml.diagramDrawing+xml", drawingXml);
        return (data, layout, style, colors);
    }

    /// <summary>Writes the package with <paramref name="bodyXml"/> (the children of w:body including the final w:sectPr) and the style sheet.</summary>
    public byte[] Build(string bodyXml, string stylesXml, string? backgroundXml = null)
    {
        AddPart("word/document.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml",
            $"<w:document {Namespaces}>{backgroundXml}<w:body>{bodyXml}</w:body></w:document>");
        AddDocumentRelationship(Rel + "/styles", "styles.xml");
        AddPart("word/styles.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml", stylesXml);
        AddDocumentRelationship(Rel + "/settings", "settings.xml");
        AddPart("word/settings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.settings+xml", Settings());
        AddDocumentRelationship(Rel + "/webSettings", "webSettings.xml");
        AddPart("word/webSettings.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.webSettings+xml",
            $"<w:webSettings {Namespaces}><w:optimizeForBrowser/><w:allowPNG/></w:webSettings>");
        AddDocumentRelationship(Rel + "/fontTable", "fontTable.xml");
        AddPart("word/fontTable.xml", "application/vnd.openxmlformats-officedocument.wordprocessingml.fontTable+xml", FontTable());
        AddDocumentRelationship(Rel + "/theme", "theme/theme1.xml");
        AddPart("word/theme/theme1.xml", "application/vnd.openxmlformats-officedocument.theme+xml", Theme.Xml);
        AddPart("docProps/core.xml", "application/vnd.openxmlformats-package.core-properties+xml",
            "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" xmlns:dc=\"http://purl.org/dc/elements/1.1/\" " +
            "xmlns:dcterms=\"http://purl.org/dc/terms/\" xmlns:dcmitype=\"http://purl.org/dc/dcmitype/\" xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
            $"<dc:title>{Title}</dc:title><dc:creator>Anna Berger</dc:creator><cp:lastModifiedBy>Anna Berger</cp:lastModifiedBy><cp:revision>3</cp:revision>" +
            "<dcterms:created xsi:type=\"dcterms:W3CDTF\">2026-11-02T08:14:00Z</dcterms:created><dcterms:modified xsi:type=\"dcterms:W3CDTF\">2026-11-12T16:40:00Z</dcterms:modified></cp:coreProperties>");
        AddPart("docProps/app.xml", "application/vnd.openxmlformats-officedocument.extended-properties+xml",
            "<Properties xmlns=\"http://schemas.openxmlformats.org/officeDocument/2006/extended-properties\" xmlns:vt=\"http://schemas.openxmlformats.org/officeDocument/2006/docPropsVTypes\">" +
            "<Template>Normal.dotm</Template><TotalTime>42</TotalTime><Pages>1</Pages><Application>Microsoft Office Word</Application><DocSecurity>0</DocSecurity>" +
            "<ScaleCrop>false</ScaleCrop><Company></Company><LinksUpToDate>false</LinksUpToDate><SharedDoc>false</SharedDoc><HyperlinksChanged>false</HyperlinksChanged><AppVersion>16.0000</AppVersion></Properties>");

        using var output = new MemoryStream();
        using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
        {
            Entry(zip, "[Content_Types].xml", Encoding.UTF8.GetBytes(ContentTypes()));
            Entry(zip, "_rels/.rels", Encoding.UTF8.GetBytes(Relationships(
            [
                ("rId3", "http://schemas.openxmlformats.org/officeDocument/2006/relationships/extended-properties", "docProps/app.xml", false),
                ("rId2", "http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties", "docProps/core.xml", false),
                ("rId1", Rel + "/officeDocument", "word/document.xml", false),
            ])));
            Entry(zip, "word/_rels/document.xml.rels", Encoding.UTF8.GetBytes(Relationships(_documentRels)));
            foreach (var (path, content) in _parts)
            {
                Entry(zip, path, content);
            }
        }

        return output.ToArray();
    }

    private static void Entry(ZipArchive zip, string path, byte[] content)
    {
        var entry = zip.CreateEntry(path, CompressionLevel.Optimal);
        using var stream = entry.Open();
        stream.Write(content);
    }

    private string ContentTypes()
    {
        var builder = new StringBuilder(XmlHeader + "<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">");
        foreach (var (extension, type) in _defaults)
        {
            builder.Append($"<Default Extension=\"{extension}\" ContentType=\"{type}\"/>");
        }

        foreach (var (part, type) in _overrides)
        {
            builder.Append($"<Override PartName=\"{part}\" ContentType=\"{type}\"/>");
        }

        return builder.Append("</Types>").ToString();
    }

    private static string Relationships(IEnumerable<(string Id, string Type, string Target, bool External)> relationships)
    {
        var builder = new StringBuilder(XmlHeader + "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">");
        foreach (var (id, type, target, external) in relationships)
        {
            builder.Append($"<Relationship Id=\"{id}\" Type=\"{type}\" Target=\"{System.Security.SecurityElement.Escape(target)}\"{(external ? " TargetMode=\"External\"" : "")}/>");
        }

        return builder.Append("</Relationships>").ToString();
    }

    private static string Separators(string kind) =>
        $"<w:{kind} w:type=\"separator\" w:id=\"-1\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:separator/></w:r></w:p></w:{kind}>" +
        $"<w:{kind} w:type=\"continuationSeparator\" w:id=\"0\"><w:p><w:pPr><w:spacing w:after=\"0\" w:line=\"240\" w:lineRule=\"auto\"/></w:pPr><w:r><w:continuationSeparator/></w:r></w:p></w:{kind}>";

    private string Settings() =>
        $"<w:settings {Namespaces}><w:zoom w:percent=\"100\"/>{SettingsExtra}<w:proofState w:spelling=\"clean\" w:grammar=\"clean\"/><w:defaultTabStop w:val=\"708\"/>" +
        "<w:hyphenationZone w:val=\"425\"/>" + (EvenAndOddHeaders ? "<w:evenAndOddHeaders/>" : "") + "<w:characterSpacingControl w:val=\"doNotCompress\"/>" +
        (_parts.Any(part => part.Path == "word/footnotes.xml") ? "<w:footnotePr><w:footnote w:id=\"-1\"/><w:footnote w:id=\"0\"/></w:footnotePr>" : "") +
        (_parts.Any(part => part.Path == "word/endnotes.xml") ? "<w:endnotePr><w:endnote w:id=\"-1\"/><w:endnote w:id=\"0\"/></w:endnotePr>" : "") +
        $"<w:compat><w:compatSetting w:name=\"compatibilityMode\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"{CompatibilityMode}\"/>" +
        "<w:compatSetting w:name=\"overrideTableStyleFontSizeAndJustification\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"enableOpenTypeFeatures\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"doNotFlipMirrorIndents\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/>" +
        "<w:compatSetting w:name=\"differentiateMultirowTableHeaders\" w:uri=\"http://schemas.microsoft.com/office/word\" w:val=\"1\"/></w:compat>" +
        "<w:rsids><w:rsidRoot w:val=\"00A21C4E\"/><w:rsid w:val=\"00A21C4E\"/><w:rsid w:val=\"00C3F2B1\"/></w:rsids>" +
        "<m:mathPr><m:mathFont m:val=\"Cambria Math\"/><m:brkBin m:val=\"before\"/><m:brkBinSub m:val=\"--\"/><m:smallFrac m:val=\"0\"/><m:dispDef/><m:lMargin m:val=\"0\"/>" +
        "<m:rMargin m:val=\"0\"/><m:defJc m:val=\"centerGroup\"/><m:wrapIndent m:val=\"1440\"/><m:intLim m:val=\"subSup\"/><m:naryLim m:val=\"undOvr\"/></m:mathPr>" +
        "<w:themeFontLang w:val=\"de-DE\"/><w:clrSchemeMapping w:bg1=\"light1\" w:t1=\"dark1\" w:bg2=\"light2\" w:t2=\"dark2\" w:accent1=\"accent1\" w:accent2=\"accent2\" " +
        "w:accent3=\"accent3\" w:accent4=\"accent4\" w:accent5=\"accent5\" w:accent6=\"accent6\" w:hyperlink=\"hyperlink\" w:followedHyperlink=\"followedHyperlink\"/>" +
        "<w:shapeDefaults><o:shapedefaults v:ext=\"edit\" spidmax=\"2049\"/><o:shapelayout v:ext=\"edit\"><o:idmap v:ext=\"edit\" data=\"1\"/></o:shapelayout></w:shapeDefaults>" +
        "<w:decimalSymbol w:val=\",\"/><w:listSeparator w:val=\";\"/></w:settings>";

    private string FontTable()
    {
        var builder = new StringBuilder($"<w:fonts {Namespaces}>");
        foreach (var (name, panose, family, pitch) in Theme.FontTableEntries)
        {
            builder.Append($"<w:font w:name=\"{name}\"><w:panose1 w:val=\"{panose}\"/><w:charset w:val=\"00\"/><w:family w:val=\"{family}\"/><w:pitch w:val=\"{pitch}\"/></w:font>");
        }

        return builder.Append("</w:fonts>").ToString();
    }
}
