using System.Xml.Linq;

namespace ThixShow.Core.Model;

/// <summary>
/// A piece of the file's XML that ThixShow keeps as it was read (one element with its namespace declarations): unknown
/// properties, effects it does not draw, SmartArt, charts, OLE frames, the full animation timing tree. Equal by text, so a
/// model that holds one still compares by value. Relationship ids inside it are named by <see cref="PartReference"/>s of the
/// element that holds it, so the writer can point them at the right parts in another slide or package.
/// </summary>
public sealed record XmlFragment(string Xml)
{
    private static readonly XNamespace Markup = "http://schemas.openxmlformats.org/markup-compatibility/2006";

    /// <summary>The fragment as an element (a new instance every call).</summary>
    public XElement ToElement() => XElement.Parse(Xml, LoadOptions.PreserveWhitespace);

    /// <summary>The local name of the root element.</summary>
    public string LocalName => ToElement().Name.LocalName;

    /// <summary>
    /// The fragment of an element: a copy whose namespace declarations are normalized – exactly the namespaces it uses (and those
    /// its mc:Ignorable / mc:ProcessContent attributes name), declared on its root with the prefixes they had where it was read.
    /// So the same element reads as the same fragment wherever its declarations stood in the file.
    /// </summary>
    public static XmlFragment From(XElement element)
    {
        var copy = new XElement(element);

        // Declarations below the root (prefixes declared on the element where they are used) and what each mc:Choice requires.
        var declaredBelow = new Dictionary<XNamespace, string>();
        foreach (var declaration in element.Descendants().SelectMany(node => node.Attributes()).Where(attribute => attribute.IsNamespaceDeclaration && attribute.Name.Namespace == XNamespace.Xmlns))
        {
            declaredBelow.TryAdd(XNamespace.Get(declaration.Value), declaration.Name.LocalName);
        }

        var required = new List<(XElement Choice, string Prefix, XNamespace Namespace)>();
        foreach (var choice in copy.DescendantsAndSelf(Markup + "Choice"))
        {
            foreach (var prefix in (choice.Attribute("Requires")?.Value ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                if ((choice.GetNamespaceOfPrefix(prefix) ?? element.GetNamespaceOfPrefix(prefix)) is { } ns)
                {
                    required.Add((choice, prefix, ns));
                }
            }
        }

        foreach (var node in copy.DescendantsAndSelf())
        {
            foreach (var declaration in node.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).ToList())
            {
                declaration.Remove();
            }
        }

        var used = new SortedDictionary<string, XNamespace>(StringComparer.Ordinal);
        void Use(XNamespace ns)
        {
            if (ns == XNamespace.None || ns == XNamespace.Xml || ns == XNamespace.Xmlns || used.ContainsValue(ns))
            {
                return;
            }

            var prefix = element.GetPrefixOfNamespace(ns) ?? declaredBelow.GetValueOrDefault(ns) ?? KnownPrefix(ns) ?? "ns" + used.Count;
            used.TryAdd(prefix, ns);
        }

        foreach (var node in copy.DescendantsAndSelf())
        {
            Use(node.Name.Namespace);
            foreach (var attribute in node.Attributes())
            {
                Use(attribute.Name.Namespace);
                if (attribute.Name.Namespace == Markup && attribute.Name.LocalName is "Ignorable" or "ProcessContent" or "PreserveElements" or "PreserveAttributes")
                {
                    foreach (var prefix in attribute.Value.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                    {
                        var name = prefix.Split(':')[0];
                        if (element.GetNamespaceOfPrefix(name) is { } ns)
                        {
                            used.TryAdd(name, ns);
                        }
                    }
                }
            }
        }

        // The element's own namespace keeps its prefix (or the default namespace when it had none).
        var ownPrefix = element.GetPrefixOfNamespace(element.Name.Namespace);
        foreach (var (prefix, ns) in used)
        {
            if (ns == element.Name.Namespace && ownPrefix is null)
            {
                copy.SetAttributeValue("xmlns", ns.NamespaceName);
                continue;
            }

            copy.SetAttributeValue(XNamespace.Xmlns + prefix, ns.NamespaceName);
        }

        // mc:Choice declares the prefixes its Requires names itself (consumers resolve them there).
        foreach (var (choice, prefix, ns) in required)
        {
            choice.SetAttributeValue(XNamespace.Xmlns + prefix, ns.NamespaceName);
        }

        return new XmlFragment(copy.ToString(SaveOptions.DisableFormatting));
    }

    private static string? KnownPrefix(XNamespace ns) => ns.NamespaceName switch
    {
        "http://schemas.openxmlformats.org/drawingml/2006/main" => "a",
        "http://schemas.openxmlformats.org/presentationml/2006/main" => "p",
        "http://schemas.openxmlformats.org/officeDocument/2006/relationships" => "r",
        "http://schemas.openxmlformats.org/markup-compatibility/2006" => "mc",
        "http://schemas.microsoft.com/office/powerpoint/2010/main" => "p14",
        "http://schemas.microsoft.com/office/powerpoint/2012/main" => "p15",
        "http://schemas.microsoft.com/office/drawing/2010/main" => "a14",
        "http://schemas.openxmlformats.org/drawingml/2006/chart" => "c",
        _ => null,
    };

    public override string ToString() => Xml.Length > 80 ? Xml[..80] + "…" : Xml;
}

/// <summary>
/// What a relationship id inside preserved XML points at: a part of the package (<see cref="PartUri"/>, e.g. "/ppt/charts/chart1.xml")
/// or an external target (<see cref="ExternalTarget"/>, a link). <see cref="Id"/> is the id as it stands in the XML.
/// </summary>
public sealed record PartReference(string Id, string RelationshipType, string? PartUri, string? ExternalTarget);
