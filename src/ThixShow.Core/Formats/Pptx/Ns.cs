using System.Globalization;
using System.Xml.Linq;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>The namespaces of PresentationML/DrawingML and small helpers for reading and writing their XML with LINQ to XML.</summary>
internal static class Ns
{
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace P = "http://schemas.openxmlformats.org/presentationml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace P14 = "http://schemas.microsoft.com/office/powerpoint/2010/main";
    public static readonly XNamespace P15 = "http://schemas.microsoft.com/office/powerpoint/2012/main";
    public static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    public static readonly XNamespace A14 = "http://schemas.microsoft.com/office/drawing/2010/main";
    public static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";

    public const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    public const string TableUri = "http://schemas.openxmlformats.org/drawingml/2006/table";
    public const string DiagramUri = "http://schemas.openxmlformats.org/drawingml/2006/diagram";
    public const string OleUri = "http://schemas.openxmlformats.org/presentationml/2006/ole";
    public const string SectionsExtension = "{521415D9-36F7-43E2-AB2F-B90AF26B5E84}";

    public static string? Attr(this XElement? element, string name) => element?.Attribute(name)?.Value;

    public static int? Int(this XElement? element, XName name) =>
        int.TryParse(element?.Attribute(name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static long? Long(this XElement? element, string name) =>
        long.TryParse(element.Attr(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static int? Int(this XElement? element, string name) =>
        int.TryParse(element.Attr(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static uint? UInt(this XElement? element, string name) =>
        uint.TryParse(element.Attr(name), NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : null;

    public static bool? Bool(this XElement? element, string name) => element.Attr(name) switch
    {
        "1" or "true" or "on" => true,
        "0" or "false" or "off" => false,
        _ => null,
    };

    public static string Text(long value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Text(int value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Text(uint value) => value.ToString(CultureInfo.InvariantCulture);

    public static string Text(bool value) => value ? "1" : "0";

    /// <summary>Sets or removes an attribute (null removes).</summary>
    public static void Set(this XElement element, string name, string? value)
    {
        if (value is null)
        {
            element.Attribute(name)?.Remove();
        }
        else
        {
            element.SetAttributeValue(name, value);
        }
    }

    public static void Set(this XElement element, string name, long? value) => element.Set(name, value is null ? null : Text(value.Value));

    public static void Set(this XElement element, string name, int? value) => element.Set(name, value is null ? null : Text(value.Value));

    public static void Set(this XElement element, string name, bool? value) => element.Set(name, value is null ? null : Text(value.Value));

    /// <summary>The children of <paramref name="element"/> with one of the local names, in any namespace of DrawingML/PresentationML.</summary>
    public static XElement? Child(this XElement? element, XName name) => element?.Element(name);

    /// <summary>
    /// Puts <paramref name="child"/> into <paramref name="parent"/> at its place in <paramref name="order"/> (local names, schema order),
    /// replacing the existing children with any of <paramref name="replaces"/> (default: the child's own name). Null removes them.
    /// </summary>
    public static void Place(this XElement parent, XElement? child, string[] order, params XName[] replaces)
    {
        var names = replaces.Length > 0 ? replaces : child is null ? [] : [child.Name];
        foreach (var existing in parent.Elements().Where(element => names.Contains(element.Name)).ToList())
        {
            existing.Remove();
        }

        if (child is null)
        {
            return;
        }

        // Before the first known child that comes later in the schema; unknown children (rank -1) keep their places.
        var rank = Array.IndexOf(order, child.Name.LocalName);
        var before = parent.Elements().FirstOrDefault(element => Rank(order, element) > rank);
        if (before is not null)
        {
            before.AddBeforeSelf(child);
        }
        else
        {
            parent.Add(child);
        }
    }

    private static int Rank(string[] order, XElement element) => Array.IndexOf(order, element.Name.LocalName);

    /// <summary>A copy of an element without the children whose local names are listed (the rest stays in order).</summary>
    public static XElement Without(this XElement element, params string[] localNames)
    {
        var copy = new XElement(element);
        foreach (var child in copy.Elements().Where(child => localNames.Contains(child.Name.LocalName)).ToList())
        {
            child.Remove();
        }

        return copy;
    }

    /// <summary>True when an element has no attributes and no children.</summary>
    public static bool IsBare(this XElement element) => !element.HasAttributes && !element.HasElements;
}
