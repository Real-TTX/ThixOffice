using System.Text;
using DocumentFormat.OpenXml;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// The preserved XML of the model: self-contained OuterXml strings (each with its namespace declarations), concatenated.
/// Reading collects them, writing parses them again inside the parent type they came from and inserts every element at its
/// schema position (OpenXmlCompositeElement.AddChild knows the order); elements the schema does not know are appended.
/// </summary>
internal static class XmlFragments
{
    /// <summary>The concatenated OuterXml of <paramref name="elements"/>, or null when there are none.</summary>
    public static string? Join(IEnumerable<OpenXmlElement> elements)
    {
        var builder = new StringBuilder();
        foreach (var element in elements)
        {
            builder.Append(element.OuterXml);
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>The elements of <paramref name="xml"/>, parsed as children of an element of <paramref name="parentType"/>.</summary>
    public static IReadOnlyList<OpenXmlElement> Parse(string? xml, Type parentType)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return [];
        }

        try
        {
            var holder = (OpenXmlCompositeElement)Activator.CreateInstance(parentType)!;
            holder.InnerXml = xml;
            var children = holder.ChildElements.ToList();
            foreach (var child in children)
            {
                child.Remove();
            }

            return children;
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException or ArgumentException)
        {
            return [];
        }
    }

    /// <summary>Adds the elements of <paramref name="xml"/> to <paramref name="target"/>, each at its schema position.</summary>
    public static void AddTo(OpenXmlCompositeElement target, string? xml)
    {
        foreach (var element in Parse(xml, target.GetType()))
        {
            Add(target, element);
        }
    }

    /// <summary>
    /// Adds <paramref name="element"/> at its schema position among the children already there (after the children of the same
    /// kind, so repeated elements keep their order). Elements of other namespaces (w14, w15 …) go where the schema allows
    /// extensions; parents without an order table get the element appended.
    /// </summary>
    public static void Add(OpenXmlCompositeElement target, OpenXmlElement element)
    {
        if (!Orders.TryGetValue(target.LocalName, out var order) || target.NamespaceUri != DocxValues.WordNamespace)
        {
            target.AppendChild(element);
            return;
        }

        var rank = RankOf(order, element);
        var before = target.ChildElements.FirstOrDefault(child => RankOf(order, child) > rank);
        if (before is null)
        {
            target.AppendChild(element);
            return;
        }

        target.InsertBefore(element, before);
    }

    private static int RankOf(Dictionary<string, int> order, OpenXmlElement element)
    {
        var key = element.NamespaceUri == DocxValues.WordNamespace ? element.LocalName : "*";
        return order.TryGetValue(key, out var rank) ? rank : order["*"];
    }

    /// <summary>The child order of the WordprocessingML elements whose children the writer combines (ECMA-376 + Office extensions; "*" = other namespaces / unknown).</summary>
    private static readonly Dictionary<string, Dictionary<string, int>> Orders = new()
    {
        ["rPr"] = Ranks("ins del moveFrom moveTo rStyle rFonts b bCs i iCs caps smallCaps strike dstrike outline shadow emboss imprint noProof snapToGrid vanish webHidden color spacing w kern position sz szCs highlight u effect bdr shd fitText vertAlign rtl cs em lang eastAsianLayout specVanish oMath * rPrChange"),
        ["pPr"] = Ranks("pStyle keepNext keepLines pageBreakBefore framePr widowControl numPr suppressLineNumbers pBdr shd tabs suppressAutoHyphens kinsoku wordWrap overflowPunct topLinePunct autoSpaceDE autoSpaceDN bidi adjustRightInd snapToGrid spacing ind contextualSpacing mirrorIndents suppressOverlap jc textDirection textAlignment textboxTightWrap outlineLvl divId cnfStyle rPr sectPr * pPrChange"),
        ["sectPr"] = Ranks("headerReference|footerReference footnotePr endnotePr type pgSz pgMar paperSrc pgBorders lnNumType pgNumType cols formProt vAlign noEndnote titlePg textDirection bidi rtlGutter docGrid printerSettings * sectPrChange"),
        ["style"] = Ranks("name aliases basedOn next link autoRedefine hidden uiPriority semiHidden unhideWhenUsed qFormat locked personal personalCompose personalReply rsid pPr rPr tblPr trPr tcPr tblStylePr *"),
        ["lvl"] = Ranks("start numFmt lvlRestart pStyle isLgl suff lvlText lvlPicBulletId legacy lvlJc pPr rPr *"),
        ["abstractNum"] = Ranks("nsid multiLevelType tmpl name styleLink numStyleLink lvl *"),
        ["numbering"] = Ranks("numPicBullet abstractNum num numIdMacAtCleanup *"),
        ["settings"] = Ranks("writeProtection view zoom removePersonalInformation removeDateAndTime doNotDisplayPageBoundaries displayBackgroundShape printPostScriptOverText printFractionalCharacterWidth printFormsData embedTrueTypeFonts embedSystemFonts saveSubsetFonts saveFormsData mirrorMargins alignBordersAndEdges bordersDoNotSurroundHeader bordersDoNotSurroundFooter gutterAtTop hideSpellingErrors hideGrammaticalErrors activeWritingStyle proofState formsDesign attachedTemplate linkStyles stylePaneFormatFilter stylePaneSortMethod documentType mailMerge revisionView trackRevisions doNotTrackMoves doNotTrackFormatting documentProtection autoFormatOverride styleLockTheme styleLockQFSet defaultTabStop *"),
    };

    private static Dictionary<string, int> Ranks(string names)
    {
        var ranks = new Dictionary<string, int>(StringComparer.Ordinal);
        var rank = 0;
        foreach (var group in names.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var name in group.Split('|'))
            {
                ranks[name] = rank;
            }

            rank++;
        }

        return ranks;
    }

    /// <summary>
    /// The visible text of preserved content: w:t texts, tabs as tabs, breaks as spaces (deleted text, field codes and
    /// properties excluded).
    /// </summary>
    public static string VisibleText(OpenXmlElement element)
    {
        var builder = new StringBuilder();
        foreach (var descendant in element.Descendants())
        {
            switch (descendant)
            {
                case W.Text text:
                    builder.Append(text.Text);
                    break;
                case W.TabChar:
                    builder.Append('\t');
                    break;
                case W.Break or W.CarriageReturn:
                    builder.Append(' ');
                    break;
                case W.NoBreakHyphen:
                    builder.Append('‑');
                    break;
            }
        }

        return builder.ToString();
    }

    /// <summary>
    /// Removes namespace declarations inside <paramref name="root"/> that repeat a declaration of the root itself (preserved
    /// fragments carry their own declarations; once in the part they are redundant).
    /// </summary>
    public static void RemoveRedundantDeclarations(OpenXmlElement root)
    {
        var declared = root.NamespaceDeclarations.ToDictionary(pair => pair.Key, pair => pair.Value);
        if (declared.Count == 0)
        {
            return;
        }

        foreach (var element in root.Descendants())
        {
            foreach (var declaration in element.NamespaceDeclarations.ToList())
            {
                if (declared.TryGetValue(declaration.Key, out var uri) && uri == declaration.Value)
                {
                    element.RemoveNamespaceDeclaration(declaration.Key);
                }
            }
        }
    }

    /// <summary>Makes sure <paramref name="root"/> declares w: and r: (so that the redundant declarations can go).</summary>
    public static void DeclareBasicNamespaces(OpenXmlElement root)
    {
        var declared = root.NamespaceDeclarations.Select(pair => pair.Key).ToHashSet();
        if (!declared.Contains("w"))
        {
            root.AddNamespaceDeclaration("w", DocxValues.WordNamespace);
        }

        if (!declared.Contains("r"))
        {
            root.AddNamespaceDeclaration("r", DocxValues.RelationshipNamespace);
        }
    }
}
