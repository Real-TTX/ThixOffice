using System.Xml.Linq;
using ThixShow.Core.Model;
using static ThixShow.Core.Formats.Pptx.Ns;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>Reads and writes a:theme (colour, font and format schemes; object defaults, extra colour schemes and extensions kept).</summary>
internal static class ThemeXml
{
    private sealed class NoLinks : IReadLinks, IWriteLinks
    {
        public static NoLinks Instance { get; } = new();

        public ImageData? ImageOf(string relationshipId) => null;

        public (string? Url, uint? SlideId) TargetOf(string relationshipId) => (null, null);

        public string? PartUriOf(string relationshipId) => null;

        public string ImageId(ImageData image) => "";

        public string LinkId(Hyperlink link) => "";
    }

    public static Theme Read(XElement theme, IReadLinks? links = null)
    {
        links ??= NoLinks.Instance;
        var elements = theme.Element(A + "themeElements");
        var colors = elements?.Element(A + "clrScheme");
        ColorSpec Color(string slot) => DrawingXml.ReadColor(colors?.Element(A + slot)) ?? ColorSpec.Rgb("000000");
        var scheme = new ColorScheme(colors.Attr("name") ?? "", Color("dk1"), Color("lt1"), Color("dk2"), Color("lt2"), Color("accent1"), Color("accent2"),
            Color("accent3"), Color("accent4"), Color("accent5"), Color("accent6"), Color("hlink"), Color("folHlink"));
        var fonts = elements?.Element(A + "fontScheme");
        FontCollection Fonts(string name)
        {
            var collection = fonts?.Element(A + name);
            var supplemental = collection?.Without("latin", "ea", "cs");
            return new FontCollection(
                collection?.Element(A + "latin").Attr("typeface") ?? "Calibri",
                collection?.Element(A + "ea").Attr("typeface") ?? "",
                collection?.Element(A + "cs").Attr("typeface") ?? "",
                supplemental is { HasElements: true } ? XmlFragment.From(supplemental) : null);
        }

        var formats = elements?.Element(A + "fmtScheme");
        var fills = formats?.Element(A + "fillStyleLst")?.Elements().Select(element => DrawingXml.ReadFillElement(element, links)).OfType<Fill>().ToList() ?? [];
        var lines = formats?.Element(A + "lnStyleLst")?.Elements(A + "ln").Select(element => DrawingXml.ReadOutline(element, links)!).ToList() ?? [];
        var effects = formats?.Element(A + "effectStyleLst")?.Elements(A + "effectStyle").Select(element => DrawingXml.ReadEffects(element) ?? ShapeEffects.Empty).ToList() ?? [];
        var backgrounds = formats?.Element(A + "bgFillStyleLst")?.Elements().Select(element => DrawingXml.ReadFillElement(element, links)).OfType<Fill>().ToList() ?? [];
        return new Theme(
            theme.Attr("name") ?? "",
            scheme,
            new FontScheme(fonts.Attr("name") ?? "", Fonts("majorFont"), Fonts("minorFont")),
            new FormatScheme(formats.Attr("name") ?? "", fills, lines, effects, backgrounds),
            XmlFragment.From(theme));
    }

    /// <summary>The theme's XML: the kept element (or a new one) with the three schemes written from the model.</summary>
    public static XElement Write(Theme theme)
    {
        var element = theme.Source?.ToElement() ?? new XElement(A + "theme",
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XElement(A + "themeElements"),
            new XElement(A + "objectDefaults"),
            new XElement(A + "extraClrSchemeLst"));
        element.SetAttributeValue("name", theme.Name);
        var elements = element.Element(A + "themeElements") ?? new XElement(A + "themeElements");
        if (elements.Parent is null)
        {
            element.AddFirst(elements);
        }

        string[] order = ["clrScheme", "fontScheme", "fmtScheme", "extLst"];
        elements.Place(WriteColors(theme.Colors), order);
        elements.Place(WriteFonts(theme.Fonts), order);
        elements.Place(WriteFormats(theme.Formats), order);
        return element;
    }

    private static XElement WriteColors(ColorScheme scheme)
    {
        var element = new XElement(A + "clrScheme", new XAttribute("name", scheme.Name));
        foreach (var slot in ColorScheme.SlotNames)
        {
            element.Add(new XElement(A + slot, DrawingXml.WriteColor(scheme[slot])));
        }

        return element;
    }

    private static XElement WriteFonts(FontScheme fonts)
    {
        XElement Collection(string name, FontCollection collection)
        {
            var element = new XElement(A + name,
                new XElement(A + "latin", new XAttribute("typeface", collection.Latin)),
                new XElement(A + "ea", new XAttribute("typeface", collection.EastAsian)),
                new XElement(A + "cs", new XAttribute("typeface", collection.ComplexScript)));
            if (collection.Supplemental is { } supplemental)
            {
                element.Add(supplemental.ToElement().Elements());
            }

            return element;
        }

        return new XElement(A + "fontScheme", new XAttribute("name", fonts.Name), Collection("majorFont", fonts.Major), Collection("minorFont", fonts.Minor));
    }

    private static XElement WriteFormats(FormatScheme formats)
    {
        var links = NoLinks.Instance;
        return new XElement(A + "fmtScheme", new XAttribute("name", formats.Name),
            new XElement(A + "fillStyleLst", formats.FillStyles.Select(fill => DrawingXml.WriteFill(fill, links))),
            new XElement(A + "lnStyleLst", formats.LineStyles.Select(line => DrawingXml.WriteOutline(line, links))),
            new XElement(A + "effectStyleLst", formats.EffectStyles.Select(effects => new XElement(A + "effectStyle", DrawingXml.WriteEffects(effects)))),
            new XElement(A + "bgFillStyleLst", formats.BackgroundFillStyles.Select(fill => DrawingXml.WriteFill(fill, links))));
    }
}
