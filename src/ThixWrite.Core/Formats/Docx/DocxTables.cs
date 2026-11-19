using System.Globalization;
using DocumentFormat.OpenXml;
using ThixWrite.Core.Model;
using W = DocumentFormat.OpenXml.Wordprocessing;

namespace ThixWrite.Core.Formats.Docx;

/// <summary>
/// Table, row and cell properties (w:tblPr, w:trPr, w:tcPr) between WordprocessingML and the model; also the w:tblPr of table
/// styles. What the model does not know stays as UnknownXml and is written back at its schema place.
/// </summary>
internal static class DocxTables
{
    // ---------------------------------------------------------------- reading

    public static TableProperties ReadTable(OpenXmlElement? properties)
    {
        if (properties is null)
        {
            return TableProperties.Empty;
        }

        var result = new TableProperties();
        var unknown = new List<OpenXmlElement>();
        foreach (var child in properties.ChildElements)
        {
            var value = DocxValues.AttributeValue(child, "val");
            switch (child.LocalName)
            {
                case "tblStyle" when child.NamespaceUri == DocxValues.WordNamespace:
                    result = result with { StyleId = value };
                    break;
                case "jc" when child.NamespaceUri == DocxValues.WordNamespace:
                    result = result with { Alignment = value switch { "center" => TableAlignment.Center, "right" or "end" => TableAlignment.Right, _ => TableAlignment.Left } };
                    break;
                case "tblInd" when child.NamespaceUri == DocxValues.WordNamespace && ReadWidth(child) is { Kind: WidthKind.Points } indent:
                    result = result with { Indent = indent.Value };
                    break;
                case "tblW" when child.NamespaceUri == DocxValues.WordNamespace:
                    result = result with { Width = ReadWidth(child) };
                    break;
                case "tblLayout" when child.NamespaceUri == DocxValues.WordNamespace:
                    result = result with { Layout = DocxValues.AttributeValue(child, "type") == "fixed" ? TableLayoutKind.Fixed : TableLayoutKind.Auto };
                    break;
                case "tblBorders" when child.NamespaceUri == DocxValues.WordNamespace:
                    result = result with { Borders = ReadBorders(child) };
                    break;
                case "tblCellMar" when child.NamespaceUri == DocxValues.WordNamespace:
                    result = result with { CellMargins = ReadMargins(child) };
                    break;
                case "shd" when child.NamespaceUri == DocxValues.WordNamespace:
                    result = result with { Shading = ReadShading(child) };
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return result with { UnknownXml = XmlFragments.Join(unknown) };
    }

    public static TableRowProperties ReadRow(W.TableRow row)
    {
        var result = new TableRowProperties();
        if (row.GetFirstChild<W.TablePropertyExceptions>() is { } exceptions)
        {
            result = result with { ExceptionsXml = XmlFragments.Canonical(exceptions) };
        }

        if (row.TableRowProperties is not { } properties)
        {
            return result;
        }

        var unknown = new List<OpenXmlElement>();
        foreach (var child in properties.ChildElements)
        {
            switch (child)
            {
                case W.TableRowHeight height:
                    var points = DocxValues.ParseTwips(DocxValues.AttributeValue(height, "val"));
                    var rule = DocxValues.AttributeValue(height, "hRule");
                    if (points is > 0 && rule != "auto")
                    {
                        result = result with { Height = points, ExactHeight = rule == "exact" };
                    }

                    break;
                case W.TableHeader:
                    result = result with { IsHeader = DocxValues.ParseOnOff(child) };
                    break;
                case W.CantSplit:
                    result = result with { CantSplit = DocxValues.ParseOnOff(child) };
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return result with { UnknownXml = XmlFragments.Join(unknown) };
    }

    public static TableCellProperties ReadCell(W.TableCellProperties? properties)
    {
        if (properties is null)
        {
            return TableCellProperties.Empty;
        }

        var result = new TableCellProperties();
        var unknown = new List<OpenXmlElement>();
        foreach (var child in properties.ChildElements)
        {
            var value = DocxValues.AttributeValue(child, "val");
            switch (child)
            {
                case W.TableCellWidth:
                    result = result with { Width = ReadWidth(child) };
                    break;
                case W.GridSpan when DocxValues.ParseInteger(value) is { } span:
                    result = result with { GridSpan = (int)Math.Clamp(span, 1, 63) };
                    break;
                case W.VerticalMerge:
                    result = result with { VerticalMerge = value == "restart" ? VerticalMerge.Restart : VerticalMerge.Continue };
                    break;
                case W.TableCellBorders:
                    result = result with { Borders = ReadBorders(child) };
                    break;
                case W.Shading:
                    result = result with { Shading = ReadShading(child) };
                    break;
                case W.TableCellMargin:
                    result = result with { Margins = ReadMargins(child) };
                    break;
                case W.TableCellVerticalAlignment:
                    result = result with { VerticalAlignment = value switch { "center" => CellVerticalAlignment.Center, "bottom" => CellVerticalAlignment.Bottom, _ => CellVerticalAlignment.Top } };
                    break;
                default:
                    unknown.Add(child);
                    break;
            }
        }

        return result with { UnknownXml = XmlFragments.Join(unknown) };
    }

    /// <summary>The grid's column widths in points (w:tblGrid/w:gridCol).</summary>
    public static EquatableArray<double> ReadGrid(W.TableGrid? grid) =>
        grid is null ? EquatableArray<double>.Empty : new(grid.Elements<W.GridColumn>().Select(column => DocxValues.ParseTwips(DocxValues.AttributeValue(column, "w")) ?? 0));

    private static TableWidth? ReadWidth(OpenXmlElement element)
    {
        var type = DocxValues.AttributeValue(element, "type") ?? "dxa";
        var text = DocxValues.AttributeValue(element, "w");
        switch (type)
        {
            case "auto":
                return TableWidth.Auto;
            case "nil":
                return new TableWidth(WidthKind.Nil, 0);
            case "pct" when text is not null && text.EndsWith('%'):
                return double.TryParse(text.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) ? TableWidth.Percent(percent) : null;
            case "pct":
                return DocxValues.ParseInteger(text) is { } fiftieths ? TableWidth.Percent(fiftieths / 50.0) : null;
            default:
                return DocxValues.ParseTwips(text) is { } points ? TableWidth.Points(points) : null;
        }
    }

    private static BorderSet ReadBorders(OpenXmlElement element)
    {
        var result = BorderSet.Empty;
        foreach (var child in element.ChildElements)
        {
            var line = ReadBorder(child);
            result = child.LocalName switch
            {
                "top" => result with { Top = line },
                "left" or "start" => result with { Left = line },
                "bottom" => result with { Bottom = line },
                "right" or "end" => result with { Right = line },
                "insideH" => result with { InsideHorizontal = line },
                "insideV" => result with { InsideVertical = line },
                _ => result,
            };
        }

        return result;
    }

    private static BorderLine ReadBorder(OpenXmlElement element)
    {
        var style = DocxValues.AttributeValue(element, "val") ?? "single";
        var eighths = DocxValues.ParseInteger(DocxValues.AttributeValue(element, "sz"));
        var width = eighths is { } size ? size / 8.0 : style is "nil" or "none" ? 0 : 0.5;
        return new BorderLine(
            style,
            width,
            DocxValues.AttributeValue(element, "color"),
            DocxValues.ParseInteger(DocxValues.AttributeValue(element, "space")) ?? 0,
            DocxValues.AttributeValue(element, "themeColor"));
    }

    private static CellMargins ReadMargins(OpenXmlElement element)
    {
        var result = new CellMargins();
        foreach (var child in element.ChildElements)
        {
            if (ReadWidth(child) is not { Kind: WidthKind.Points or WidthKind.Nil } width)
            {
                continue;
            }

            var points = width.Value;
            result = child.LocalName switch
            {
                "top" => result with { Top = points },
                "left" or "start" => result with { Left = points },
                "bottom" => result with { Bottom = points },
                "right" or "end" => result with { Right = points },
                _ => result,
            };
        }

        return result;
    }

    private static Shading ReadShading(OpenXmlElement element) => new(
        DocxValues.AttributeValue(element, "fill"),
        DocxValues.AttributeValue(element, "val") ?? "clear",
        DocxValues.AttributeValue(element, "color"),
        DocxValues.AttributeValue(element, "themeFill"),
        DocxValues.AttributeValue(element, "themeFillTint"),
        DocxValues.AttributeValue(element, "themeFillShade"));

    // ---------------------------------------------------------------- writing

    /// <summary>Fills <paramref name="target"/> (w:tblPr of a table or a style); null when it stays empty.</summary>
    public static T? WriteTable<T>(TableProperties properties, T target) where T : OpenXmlCompositeElement
    {
        XmlFragments.AddTo(target, properties.UnknownXml);
        if (properties.StyleId is { } style)
        {
            XmlFragments.Add(target, DocxPropertyWriter.Element<W.TableStyle>(("val", style)));
        }

        if (properties.Width is { } width)
        {
            XmlFragments.Add(target, WriteWidth<W.TableWidth>(width));
        }

        if (properties.Alignment is { } alignment)
        {
            XmlFragments.Add(target, DocxPropertyWriter.Element<W.TableJustification>(("val", alignment switch
            {
                TableAlignment.Center => "center",
                TableAlignment.Right => "right",
                _ => "left",
            })));
        }

        if (properties.Indent is { } indent)
        {
            XmlFragments.Add(target, WriteWidth<W.TableIndentation>(TableWidth.Points(indent)));
        }

        if (properties.Borders is { IsEmpty: false } borders)
        {
            XmlFragments.Add(target, WriteBorders(new W.TableBorders(), borders));
        }

        if (properties.Shading is { } shading)
        {
            XmlFragments.Add(target, WriteShading(shading));
        }

        if (properties.Layout is { } layout)
        {
            XmlFragments.Add(target, DocxPropertyWriter.Element<W.TableLayout>(("type", layout == TableLayoutKind.Fixed ? "fixed" : "autofit")));
        }

        if (properties.CellMargins is { } margins && WriteTableMargins(margins) is { } cellMargins)
        {
            XmlFragments.Add(target, cellMargins);
        }

        return target.HasChildren ? target : null;
    }

    public static W.TableGrid WriteGrid(EquatableArray<double> grid) =>
        new(grid.Select(width => DocxPropertyWriter.Element<W.GridColumn>(("w", DocxPropertyWriter.Twips(width)))));

    /// <summary>The row's w:tblPrEx (kept as read) and w:trPr; nulls when there is nothing to write.</summary>
    public static (OpenXmlElement? Exceptions, W.TableRowProperties? Properties) WriteRow(TableRowProperties properties)
    {
        var exceptions = XmlFragments.Parse<W.TableRow>(properties.ExceptionsXml).FirstOrDefault();
        var target = new W.TableRowProperties();
        XmlFragments.AddTo(target, properties.UnknownXml);
        if (properties.CantSplit)
        {
            XmlFragments.Add(target, new W.CantSplit());
        }

        if (properties.Height is { } height)
        {
            XmlFragments.Add(target, DocxPropertyWriter.Element<W.TableRowHeight>(("val", DocxPropertyWriter.Twips(height)), ("hRule", properties.ExactHeight ? "exact" : null)));
        }

        if (properties.IsHeader)
        {
            XmlFragments.Add(target, new W.TableHeader());
        }

        return (exceptions, target.HasChildren ? target : null);
    }

    public static W.TableCellProperties? WriteCell(TableCellProperties properties)
    {
        var target = new W.TableCellProperties();
        XmlFragments.AddTo(target, properties.UnknownXml);
        if (properties.Width is { } width)
        {
            XmlFragments.Add(target, WriteWidth<W.TableCellWidth>(width));
        }

        if (properties.GridSpan > 1)
        {
            XmlFragments.Add(target, DocxPropertyWriter.Element<W.GridSpan>(("val", DocxPropertyWriter.Integer(properties.GridSpan))));
        }

        if (properties.VerticalMerge != VerticalMerge.None)
        {
            XmlFragments.Add(target, DocxPropertyWriter.Element<W.VerticalMerge>(("val", properties.VerticalMerge == VerticalMerge.Restart ? "restart" : null)));
        }

        if (properties.Borders is { IsEmpty: false } borders)
        {
            XmlFragments.Add(target, WriteBorders(new W.TableCellBorders(), borders));
        }

        if (properties.Shading is { } shading)
        {
            XmlFragments.Add(target, WriteShading(shading));
        }

        if (properties.Margins is { } margins && WriteCellMargins(margins) is { } cellMargins)
        {
            XmlFragments.Add(target, cellMargins);
        }

        if (properties.VerticalAlignment is { } alignment)
        {
            XmlFragments.Add(target, DocxPropertyWriter.Element<W.TableCellVerticalAlignment>(("val", alignment switch
            {
                CellVerticalAlignment.Center => "center",
                CellVerticalAlignment.Bottom => "bottom",
                _ => "top",
            })));
        }

        return target.HasChildren ? target : null;
    }

    private static T WriteWidth<T>(TableWidth width) where T : OpenXmlElement, new() => width.Kind switch
    {
        WidthKind.Auto => DocxPropertyWriter.Element<T>(("w", "0"), ("type", "auto")),
        WidthKind.Nil => DocxPropertyWriter.Element<T>(("w", "0"), ("type", "nil")),
        WidthKind.Percent => DocxPropertyWriter.Element<T>(("w", DocxPropertyWriter.Integer((long)Math.Round(width.Value * 50))), ("type", "pct")),
        _ => DocxPropertyWriter.Element<T>(("w", DocxPropertyWriter.Twips(width.Value)), ("type", "dxa")),
    };

    private static T WriteBorders<T>(T target, BorderSet borders) where T : OpenXmlCompositeElement
    {
        Add<W.TopBorder>(target, borders.Top);
        Add<W.LeftBorder>(target, borders.Left);
        Add<W.BottomBorder>(target, borders.Bottom);
        Add<W.RightBorder>(target, borders.Right);
        Add<W.InsideHorizontalBorder>(target, borders.InsideHorizontal);
        Add<W.InsideVerticalBorder>(target, borders.InsideVertical);
        return target;

        static void Add<TBorder>(OpenXmlCompositeElement parent, BorderLine? line) where TBorder : OpenXmlElement, new()
        {
            if (line is null)
            {
                return;
            }

            parent.AppendChild(DocxPropertyWriter.Element<TBorder>(
                ("val", line.Style),
                ("color", line.Color),
                ("themeColor", line.ThemeColor),
                ("sz", DocxPropertyWriter.Integer((long)Math.Round(line.Width * 8))),
                ("space", DocxPropertyWriter.Integer((long)Math.Round(line.Space)))));
        }
    }

    private static W.Shading WriteShading(Shading shading) => DocxPropertyWriter.Element<W.Shading>(
        ("val", shading.Pattern),
        ("color", shading.Color),
        ("fill", shading.Fill),
        ("themeFill", shading.ThemeFill),
        ("themeFillTint", shading.ThemeFillTint),
        ("themeFillShade", shading.ThemeFillShade));

    private static W.TableCellMarginDefault? WriteTableMargins(CellMargins margins)
    {
        var target = new W.TableCellMarginDefault();
        AddMargin<W.TopMargin>(target, margins.Top);
        AddMargin<W.TableCellLeftMargin>(target, margins.Left);
        AddMargin<W.BottomMargin>(target, margins.Bottom);
        AddMargin<W.TableCellRightMargin>(target, margins.Right);
        return target.HasChildren ? target : null;
    }

    private static W.TableCellMargin? WriteCellMargins(CellMargins margins)
    {
        var target = new W.TableCellMargin();
        AddMargin<W.TopMargin>(target, margins.Top);
        AddMargin<W.LeftMargin>(target, margins.Left);
        AddMargin<W.BottomMargin>(target, margins.Bottom);
        AddMargin<W.RightMargin>(target, margins.Right);
        return target.HasChildren ? target : null;
    }

    private static void AddMargin<T>(OpenXmlCompositeElement target, double? points) where T : OpenXmlElement, new()
    {
        if (points is { } value)
        {
            target.AppendChild(WriteWidth<T>(TableWidth.Points(value)));
        }
    }
}
