using System.Xml.Linq;
using ThixShow.Core.Model;
using static ThixShow.Core.Formats.Pptx.Ns;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>Reads tableStyles.xml (a:tblStyleLst) and writes ThixShow's default table style for new presentations.</summary>
internal static class TableStyleXml
{
    private static readonly (string Name, TableStylePart Part)[] Parts =
    [
        ("wholeTbl", TableStylePart.WholeTable), ("band1H", TableStylePart.Band1Horizontal), ("band2H", TableStylePart.Band2Horizontal),
        ("band1V", TableStylePart.Band1Vertical), ("band2V", TableStylePart.Band2Vertical), ("lastCol", TableStylePart.LastColumn),
        ("firstCol", TableStylePart.FirstColumn), ("lastRow", TableStylePart.LastRow), ("seCell", TableStylePart.SouthEastCell),
        ("swCell", TableStylePart.SouthWestCell), ("firstRow", TableStylePart.FirstRow), ("neCell", TableStylePart.NorthEastCell),
        ("nwCell", TableStylePart.NorthWestCell),
    ];

    private sealed class NoLinks : IReadLinks, IWriteLinks
    {
        public static NoLinks Instance { get; } = new();

        public ImageData? ImageOf(string relationshipId) => null;

        public (string? Url, uint? SlideId) TargetOf(string relationshipId) => (null, null);

        public string? PartUriOf(string relationshipId) => null;

        public string ImageId(ImageData image) => "";

        public string LinkId(Hyperlink link) => "";
    }

    public static (ValueList<TableStyle> Styles, string? Default) Read(XElement list)
    {
        var styles = list.Elements(A + "tblStyle").Select(style =>
        {
            var parts = new List<(TableStylePart, TableStyleFormat)>();
            foreach (var (name, part) in Parts)
            {
                if (style.Element(A + name) is { } element)
                {
                    parts.Add((part, ReadFormat(element)));
                }
            }

            return new TableStyle(style.Attr("styleId") ?? "", style.Attr("styleName") ?? "", parts);
        }).ToList();
        return (styles, list.Attr("def"));
    }

    private static TableStyleFormat ReadFormat(XElement element)
    {
        var text = element.Element(A + "tcTxStyle");
        var cell = element.Element(A + "tcStyle");
        var borders = cell?.Element(A + "tcBdr");
        Outline? Border(string name) => borders?.Element(A + name)?.Element(A + "ln") is { } line ? DrawingXml.ReadOutline(line, NoLinks.Instance) : null;
        var fontReference = text?.Element(A + "fontRef");
        return new TableStyleFormat(
            text.Attr("b") switch
            {
                "on" => true,
                "off" => false,
                _ => null,
            },
            text.Attr("i") switch
            {
                "on" => true,
                "off" => false,
                _ => null,
            },
            DrawingXml.ReadColor(text) ?? DrawingXml.ReadColor(fontReference),
            fontReference.Attr("idx"),
            DrawingXml.ReadFill(cell?.Element(A + "fill"), NoLinks.Instance),
            Border("left"),
            Border("right"),
            Border("top"),
            Border("bottom"),
            Border("insideH"),
            Border("insideV"));
    }

    public static XElement Write(IEnumerable<TableStyle> styles, string defaultId)
    {
        var list = new XElement(A + "tblStyleLst", new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName), new XAttribute("def", defaultId));
        foreach (var style in styles)
        {
            var element = new XElement(A + "tblStyle", new XAttribute("styleId", style.Id), new XAttribute("styleName", style.Name));
            foreach (var (name, part) in Parts)
            {
                if (style.FormatOf(part) is { } format)
                {
                    element.Add(WriteFormat(name, format));
                }
            }

            list.Add(element);
        }

        return list;
    }

    private static XElement WriteFormat(string name, TableStyleFormat format)
    {
        var element = new XElement(A + name);
        if (format.Bold is not null || format.Italic is not null || format.TextColor is not null || format.FontReference is not null)
        {
            var text = new XElement(A + "tcTxStyle");
            text.Set("b", format.Bold switch
            {
                true => "on",
                false => "off",
                _ => null,
            });
            text.Set("i", format.Italic switch
            {
                true => "on",
                false => "off",
                _ => null,
            });
            if (format.FontReference is not null)
            {
                text.Add(new XElement(A + "fontRef", new XAttribute("idx", format.FontReference)));
            }

            if (format.TextColor is not null)
            {
                text.Add(DrawingXml.WriteColor(format.TextColor));
            }

            element.Add(text);
        }

        var cell = new XElement(A + "tcStyle");
        var borders = new XElement(A + "tcBdr");
        foreach (var (border, borderName) in new[] { (format.LeftBorder, "left"), (format.RightBorder, "right"), (format.TopBorder, "top"), (format.BottomBorder, "bottom"), (format.InsideHorizontalBorder, "insideH"), (format.InsideVerticalBorder, "insideV") })
        {
            if (border is not null)
            {
                borders.Add(new XElement(A + borderName, DrawingXml.WriteOutline(border, NoLinks.Instance)));
            }
        }

        cell.Add(borders);
        if (DrawingXml.WriteFill(format.Fill, NoLinks.Instance) is { } fill)
        {
            cell.Add(new XElement(A + "fill", fill));
        }

        element.Add(cell);
        return element;
    }
}
