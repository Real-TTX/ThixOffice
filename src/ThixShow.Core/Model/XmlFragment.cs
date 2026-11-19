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
    /// <summary>The fragment as an element (a new instance every call).</summary>
    public XElement ToElement() => XElement.Parse(Xml, LoadOptions.PreserveWhitespace);

    /// <summary>The local name of the root element.</summary>
    public string LocalName => ToElement().Name.LocalName;

    public static XmlFragment From(XElement element) => new(element.ToString(SaveOptions.DisableFormatting | SaveOptions.OmitDuplicateNamespaces));

    public override string ToString() => Xml.Length > 80 ? Xml[..80] + "…" : Xml;
}

/// <summary>
/// What a relationship id inside preserved XML points at: a part of the package (<see cref="PartUri"/>, e.g. "/ppt/charts/chart1.xml")
/// or an external target (<see cref="ExternalTarget"/>, a link). <see cref="Id"/> is the id as it stands in the XML.
/// </summary>
public sealed record PartReference(string Id, string RelationshipType, string? PartUri, string? ExternalTarget);
