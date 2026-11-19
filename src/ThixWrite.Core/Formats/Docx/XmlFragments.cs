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
    /// <summary>The concatenated canonical XML of <paramref name="elements"/>, or null when there are none.</summary>
    public static string? Join(IEnumerable<OpenXmlElement> elements)
    {
        var builder = new StringBuilder();
        foreach (var element in elements)
        {
            builder.Append(Canonical(element));
        }

        return builder.Length == 0 ? null : builder.ToString();
    }

    /// <summary>
    /// The self-contained XML of <paramref name="element"/> in one canonical form: every namespace declaration is left to the
    /// serializer, which declares what the fragment uses where it is first used (prefixes only named in markup-compatibility
    /// attributes get an explicit declaration on the top element). So the same content gives the same string whether a file
    /// declared its namespaces at the root (Word) or on the elements (other writers, ThixWrite's own fragments).
    /// </summary>
    public static string Canonical(OpenXmlElement element)
    {
        var clone = element.CloneNode(true);
        var all = clone.Descendants().Prepend(clone).SelectMany(current => current.NamespaceDeclarations).ToList();

        // Prefixes named in markup-compatibility attribute values (mc:Ignorable="w14 wp14", Requires="wps") are not "used" for
        // the serializer; they keep an explicit declaration on the fragment's top element.
        var referenced = ReferencedPrefixes(clone);
        if (all.Count == 0 && referenced.Count == 0)
        {
            return element.OuterXml;
        }

        foreach (var current in clone.Descendants().Prepend(clone))
        {
            foreach (var declaration in current.NamespaceDeclarations.ToList())
            {
                current.RemoveNamespaceDeclaration(declaration.Key);
            }
        }

        foreach (var prefix in referenced.Order(StringComparer.Ordinal))
        {
            var uri = element.LookupNamespace(prefix) ?? all.FirstOrDefault(pair => pair.Key == prefix).Value;
            if (!string.IsNullOrEmpty(uri))
            {
                clone.AddNamespaceDeclaration(prefix, uri);
            }
        }

        return clone.OuterXml;
    }

    private static HashSet<string> ReferencedPrefixes(OpenXmlElement element)
    {
        const string MarkupCompatibility = "http://schemas.openxmlformats.org/markup-compatibility/2006";
        var prefixes = new HashSet<string>(StringComparer.Ordinal);
        foreach (var current in element.Descendants().Prepend(element))
        {
            foreach (var attribute in current.GetAttributes())
            {
                var isReference = (attribute.NamespaceUri == MarkupCompatibility && attribute.LocalName is "Ignorable" or "ProcessContent" or "MustUnderstand" or "PreserveElements" or "PreserveAttributes")
                                  || (attribute.LocalName == "Requires" && current.NamespaceUri == MarkupCompatibility);
                if (!isReference || attribute.Value is null)
                {
                    continue;
                }

                foreach (var token in attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    prefixes.Add(token.Split(':')[0]);
                }
            }

            if (current is AlternateContentChoice { Requires.Value: { } requires })
            {
                foreach (var token in requires.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    prefixes.Add(token);
                }
            }

            if (current.MCAttributes is { } compatibility)
            {
                foreach (var value in new[] { compatibility.Ignorable?.Value, compatibility.ProcessContent?.Value, compatibility.MustUnderstand?.Value })
                {
                    foreach (var token in (value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        prefixes.Add(token.Split(':')[0]);
                    }
                }
            }
        }

        return prefixes;
    }

    private static bool IsDeclared(KeyValuePair<string, string> pair, Dictionary<string, string> declared) =>
        declared.TryGetValue(pair.Key, out var uri) && uri == pair.Value;

    private static void StripDeclarations(OpenXmlElement element, Dictionary<string, string> declared)
    {
        foreach (var current in element.Descendants().Prepend(element))
        {
            foreach (var declaration in current.NamespaceDeclarations.ToList())
            {
                if (IsDeclared(declaration, declared))
                {
                    current.RemoveNamespaceDeclaration(declaration.Key);
                }
            }
        }
    }

    /// <summary>The elements of <paramref name="xml"/>, parsed as children of an element of <typeparamref name="TParent"/>.</summary>
    public static IReadOnlyList<OpenXmlElement> Parse<TParent>(string? xml)
        where TParent : OpenXmlCompositeElement, new() => Parse(xml, new TParent());

    /// <summary>
    /// The elements of <paramref name="xml"/>, parsed as children of <paramref name="holder"/> (an empty element of the parent's
    /// type; no reflection, so the trimmed app parses the same).
    /// </summary>
    private static IReadOnlyList<OpenXmlElement> Parse(string? xml, OpenXmlCompositeElement holder)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return [];
        }

        try
        {

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
        foreach (var element in Parse(xml, (OpenXmlCompositeElement)target.CloneNode(deep: false)))
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
                    builder.Append('\u2011');
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

        foreach (var element in root.ChildElements)
        {
            StripDeclarations(element, declared);
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
