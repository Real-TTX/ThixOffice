using System.Globalization;
using System.Xml.Linq;

namespace ThixOffice.Charts.Formats;

/// <summary>The namespaces of DrawingML charts.</summary>
public static class ChartNamespaces
{
    public static readonly XNamespace C = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    public static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";
    public static readonly XNamespace R = "http://schemas.openxmlformats.org/officeDocument/2006/relationships";
    public static readonly XNamespace Mc = "http://schemas.openxmlformats.org/markup-compatibility/2006";
    public static readonly XNamespace C16R2 = "http://schemas.microsoft.com/office/drawing/2015/06/chart";

    /// <summary>The content type of a chart part.</summary>
    public const string ChartContentType = "application/vnd.openxmlformats-officedocument.drawingml.chart+xml";
}

/// <summary>
/// Reads a DrawingML chart part (c:chartSpace) into a <see cref="Chart"/>: column/bar, line, pie, doughnut, area and scatter
/// plots with their series (references and cached values), axes (category, value, date), title, legend, data labels, series
/// colours (theme accents or RGB) and the options that change the drawing. Chart types ThixOffice does not draw make an
/// unsupported chart (<see cref="Chart.UnsupportedType"/>) that keeps its part. The XML of elements the model does not own
/// stays in the model (SourceXml / ShapeXml / ExtraXml), so <see cref="ChartXmlWriter"/> can write it back.
/// </summary>
public static class ChartXml
{
    private static readonly XNamespace C = ChartNamespaces.C;
    private static readonly XNamespace A = ChartNamespaces.A;

    private static readonly Dictionary<string, string> UnsupportedNames = new(StringComparer.Ordinal)
    {
        ["bar3DChart"] = "3D-Säulen oder -Balken",
        ["line3DChart"] = "3D-Linie",
        ["pie3DChart"] = "3D-Kreis",
        ["area3DChart"] = "3D-Fläche",
        ["radarChart"] = "Netz",
        ["stockChart"] = "Kurs",
        ["surfaceChart"] = "Oberfläche",
        ["surface3DChart"] = "3D-Oberfläche",
        ["bubbleChart"] = "Blase",
        ["ofPieChart"] = "Kreis aus Kreis",
    };

    /// <summary>Reads chart XML; a document that is no chart becomes an unsupported chart that keeps it.</summary>
    public static Chart Read(string xml)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        }
        catch (System.Xml.XmlException)
        {
            return new Chart { UnsupportedType = "Unbekannt", SourceXml = xml, Legend = null };
        }

        var space = document.Root;
        var chartElement = space?.Element(C + "chart");
        var plotArea = chartElement?.Element(C + "plotArea");
        if (space is null || space.Name != C + "chartSpace" || chartElement is null || plotArea is null)
        {
            return new Chart { UnsupportedType = "Unbekannt", SourceXml = xml, Legend = null };
        }

        var plots = new List<ChartPlot>();
        string? unsupported = null;
        foreach (var element in plotArea.Elements())
        {
            if (element.Name.Namespace != C)
            {
                continue;
            }

            var name = element.Name.LocalName;
            if (UnsupportedNames.TryGetValue(name, out var german))
            {
                unsupported ??= german;
                continue;
            }

            if (ReadPlot(element) is { } plot)
            {
                plots.Add(plot);
            }
        }

        var axes = plotArea.Elements().Where(element => element.Name == C + "catAx" || element.Name == C + "valAx" || element.Name == C + "dateAx" || element.Name == C + "serAx")
            .Select(ReadAxis).Where(axis => axis is not null).Select(axis => axis!).ToList();
        if (plotArea.Element(C + "serAx") is not null)
        {
            unsupported ??= "3D-Diagramm";
        }

        var title = chartElement.Element(C + "title") is { } titleElement ? ReadTitle(titleElement) : null;
        var legend = chartElement.Element(C + "legend") is { } legendElement ? ReadLegend(legendElement) : null;
        return new Chart
        {
            Title = title,
            AutoTitleDeleted = Bool(chartElement.Element(C + "autoTitleDeleted"), false),
            Plots = plots,
            Axes = axes,
            Legend = legend,
            PlotVisibleOnly = Bool(chartElement.Element(C + "plotVisOnly"), true),
            DisplayBlanksAs = Val(chartElement.Element(C + "dispBlanksAs")) switch
            {
                "zero" => BlankCells.Zero,
                "span" => BlankCells.Span,
                _ => BlankCells.Gap,
            },
            RoundedCorners = Bool(space.Element(C + "roundedCorners"), false),
            UnsupportedType = unsupported ?? (plots.Count == 0 ? "Unbekannt" : null),
            SourceXml = xml,
        };
    }

    // ===== Plots and series =====

    private static ChartPlot? ReadPlot(XElement element)
    {
        var name = element.Name.LocalName;
        ChartType type;
        switch (name)
        {
            case "barChart":
                type = Val(element.Element(C + "barDir")) == "bar" ? ChartType.Bar : ChartType.Column;
                break;
            case "lineChart":
                type = ChartType.Line;
                break;
            case "pieChart":
                type = ChartType.Pie;
                break;
            case "doughnutChart":
                type = ChartType.Doughnut;
                break;
            case "areaChart":
                type = ChartType.Area;
                break;
            case "scatterChart":
                type = ChartType.Scatter;
                break;
            default:
                return null;
        }

        var grouping = Val(element.Element(C + "grouping")) switch
        {
            "stacked" => ChartGrouping.Stacked,
            "percentStacked" => ChartGrouping.PercentStacked,
            _ => ChartGrouping.Clustered,
        };
        var scatterStyle = Val(element.Element(C + "scatterStyle"));
        var series = element.Elements(C + "ser").Select(ser => ReadSeries(ser, type, scatterStyle)).OrderBy(entry => entry.Order).ToList();
        var owned = new HashSet<string>(StringComparer.Ordinal)
        {
            "barDir", "grouping", "varyColors", "ser", "dLbls", "gapWidth", "overlap", "axId", "marker", "smooth", "firstSliceAng", "holeSize", "scatterStyle",
        };
        return new ChartPlot(type, grouping, series)
        {
            GapWidth = Int(element.Element(C + "gapWidth"), 150),
            Overlap = Int(element.Element(C + "overlap"), grouping == ChartGrouping.Clustered ? 0 : 100),
            VaryColors = Bool(element.Element(C + "varyColors"), type is ChartType.Pie or ChartType.Doughnut),
            HoleSize = Int(element.Element(C + "holeSize"), 50),
            FirstSliceAngle = Int(element.Element(C + "firstSliceAng"), 0),
            ShowMarkers = Bool(element.Element(C + "marker"), true),
            Labels = element.Element(C + "dLbls") is { } labels ? ReadLabels(labels) : null,
            AxisIds = element.Elements(C + "axId").Select(id => Int(id, 0)).ToList(),
            ExtraXml = element.Elements().Where(child => !(child.Name.Namespace == C && owned.Contains(child.Name.LocalName))).Select(child => child.ToString(SaveOptions.DisableFormatting)).ToList(),
        };
    }

    private static readonly HashSet<string> OwnedSeriesElements = new(StringComparer.Ordinal)
    {
        "idx", "order", "tx", "spPr", "marker", "dPt", "dLbls", "cat", "val", "xVal", "yVal", "smooth", "explosion", "invertIfNegative",
    };

    private static ChartSeries ReadSeries(XElement element, ChartType type, string? scatterStyle)
    {
        var shape = element.Element(C + "spPr");
        var (fill, line, width, noLine, noFill) = ReadShape(shape);
        var markerElement = element.Element(C + "marker");
        var marker = MarkerSymbol.Auto;
        double? markerSize = null;
        if (markerElement is not null)
        {
            marker = Val(markerElement.Element(C + "symbol")) switch
            {
                "none" => MarkerSymbol.None,
                "circle" => MarkerSymbol.Circle,
                "square" => MarkerSymbol.Square,
                "diamond" => MarkerSymbol.Diamond,
                "triangle" => MarkerSymbol.Triangle,
                "x" => MarkerSymbol.X,
                "plus" => MarkerSymbol.Plus,
                "dash" => MarkerSymbol.Dash,
                "star" => MarkerSymbol.Star,
                _ => MarkerSymbol.Auto,
            };
            markerSize = markerElement.Element(C + "size") is { } size ? Int(size, 5) : null;
        }

        if (type == ChartType.Scatter && scatterStyle is "none" or "marker" && marker == MarkerSymbol.Auto)
        {
            noLine = true;
        }

        if (type == ChartType.Scatter && scatterStyle is "line" or "smooth" && markerElement is null)
        {
            marker = MarkerSymbol.None;
        }

        var points = new Dictionary<int, ChartColor>();
        foreach (var point in element.Elements(C + "dPt"))
        {
            var (pointFill, pointLine, _, _, _) = ReadShape(point.Element(C + "spPr"));
            if ((type is ChartType.Line or ChartType.Scatter ? pointLine ?? pointFill : pointFill ?? pointLine) is { } color)
            {
                points[Int(point.Element(C + "idx"), 0)] = color;
            }
        }

        var categories = element.Element(C + (type == ChartType.Scatter ? "xVal" : "cat")) ?? element.Element(C + "cat");
        var values = element.Element(C + (type == ChartType.Scatter ? "yVal" : "val")) ?? element.Element(C + "val");
        if (noFill && type is not (ChartType.Line or ChartType.Scatter))
        {
            fill = ChartColor.FromRgb(new ThixOffice.Text.Drawing.RgbColor(255, 255, 255, 0));
        }

        return new ChartSeries(Int(element.Element(C + "idx"), 0), ReadData(values) ?? new ChartDataRef(null, []))
        {
            Order = Int(element.Element(C + "order"), Int(element.Element(C + "idx"), 0)),
            Name = ReadText(element.Element(C + "tx")),
            Categories = ReadData(categories),
            Fill = type is ChartType.Line or ChartType.Scatter ? null : fill,
            Line = type is ChartType.Line or ChartType.Scatter ? line : null,
            LineWidth = type is ChartType.Line or ChartType.Scatter ? width : null,
            NoLine = type is ChartType.Line or ChartType.Scatter && noLine,
            Marker = marker,
            MarkerSize = markerSize,
            Smooth = Bool(element.Element(C + "smooth"), false),
            Labels = element.Element(C + "dLbls") is { } labels ? ReadLabels(labels) : null,
            Explosion = Int(element.Element(C + "explosion"), 0),
            PointColors = points.Count > 0 ? points : null,
            ShapeXml = shape?.ToString(SaveOptions.DisableFormatting),
            ExtraXml = element.Elements().Where(child => !(child.Name.Namespace == C && OwnedSeriesElements.Contains(child.Name.LocalName))).Select(child => child.ToString(SaveOptions.DisableFormatting)).ToList(),
        };
    }

    /// <summary>The fill and line of a c:spPr: (fill, line colour, line width in points, no line, no fill).</summary>
    internal static (ChartColor? Fill, ChartColor? Line, double? Width, bool NoLine, bool NoFill) ReadShape(XElement? shape)
    {
        if (shape is null)
        {
            return (null, null, null, false, false);
        }

        var fill = ReadColor(shape.Element(A + "solidFill"));
        var noFill = shape.Element(A + "noFill") is not null;
        var line = shape.Element(A + "ln");
        ChartColor? lineColor = null;
        double? width = null;
        var noLine = false;
        if (line is not null)
        {
            lineColor = ReadColor(line.Element(A + "solidFill"));
            noLine = line.Element(A + "noFill") is not null;
            width = line.Attribute("w") is { } w && double.TryParse(w.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var emu) ? emu / 12700 : null;
        }

        return (fill, lineColor, width, noLine, noFill);
    }

    /// <summary>A colour of a:solidFill: a scheme accent with lumMod/lumOff, or an RGB colour (system colours by their last value).</summary>
    internal static ChartColor? ReadColor(XElement? solidFill)
    {
        if (solidFill is null)
        {
            return null;
        }

        if (solidFill.Element(A + "srgbClr") is { } rgb && rgb.Attribute("val")?.Value is { Length: 6 } hex)
        {
            return ChartColor.FromRgb(ThixOffice.Text.Drawing.RgbColor.FromHex(hex));
        }

        if (solidFill.Element(A + "sysClr") is { } system && system.Attribute("lastClr")?.Value is { Length: 6 } last)
        {
            return ChartColor.FromRgb(ThixOffice.Text.Drawing.RgbColor.FromHex(last));
        }

        if (solidFill.Element(A + "schemeClr") is { } scheme)
        {
            var name = scheme.Attribute("val")?.Value ?? string.Empty;
            var lumMod = Percent(scheme.Element(A + "lumMod"), 1);
            var lumOff = Percent(scheme.Element(A + "lumOff"), 0);
            if (name.StartsWith("accent", StringComparison.Ordinal) && int.TryParse(name.AsSpan(6), out var accent) && accent is >= 1 and <= 6)
            {
                return ChartColor.FromAccent(accent, lumMod, lumOff);
            }

            var basic = name switch
            {
                "tx1" or "dk1" => ThixOffice.Text.Drawing.RgbColor.Black,
                "bg1" or "lt1" => ThixOffice.Text.Drawing.RgbColor.White,
                "tx2" or "dk2" => ThixOffice.Text.Drawing.RgbColor.FromHex("44546A"),
                "bg2" or "lt2" => ThixOffice.Text.Drawing.RgbColor.FromHex("E7E6E6"),
                _ => ThixOffice.Text.Drawing.RgbColor.FromHex("808080"),
            };
            return ChartColor.FromRgb(ChartColors.ApplyLuminance(basic, lumMod, lumOff));
        }

        return null;
    }

    // ===== Data and texts =====

    private static ChartDataRef? ReadData(XElement? element)
    {
        if (element is null)
        {
            return null;
        }

        if (element.Element(C + "numRef") is { } numRef)
        {
            var cache = numRef.Element(C + "numCache");
            return new ChartDataRef(numRef.Element(C + "f")?.Value, ReadPoints(cache, numeric: true), cache?.Element(C + "formatCode")?.Value);
        }

        if (element.Element(C + "strRef") is { } strRef)
        {
            return new ChartDataRef(strRef.Element(C + "f")?.Value, ReadPoints(strRef.Element(C + "strCache"), numeric: false)) { IsNumeric = false };
        }

        if (element.Element(C + "numLit") is { } numLit)
        {
            return new ChartDataRef(null, ReadPoints(numLit, numeric: true), numLit.Element(C + "formatCode")?.Value);
        }

        if (element.Element(C + "strLit") is { } strLit)
        {
            return new ChartDataRef(null, ReadPoints(strLit, numeric: false)) { IsNumeric = false };
        }

        if (element.Element(C + "multiLvlStrRef") is { } multi)
        {
            // Several levels of categories: the innermost level's texts; the reference is written back as read.
            var level = multi.Element(C + "multiLvlStrCache")?.Elements(C + "lvl").FirstOrDefault();
            var count = Int(multi.Element(C + "multiLvlStrCache")?.Element(C + "ptCount"), 0);
            var points = new ChartValue[count];
            foreach (var point in level?.Elements(C + "pt") ?? [])
            {
                var index = (int)(point.Attribute("idx") is { } idx ? long.Parse(idx.Value, CultureInfo.InvariantCulture) : 0);
                if (index >= 0 && index < count)
                {
                    points[index] = new ChartValue(null, point.Element(C + "v")?.Value);
                }
            }

            return new ChartDataRef(multi.Element(C + "f")?.Value, points) { IsNumeric = false, MultiLevelXml = element.ToString(SaveOptions.DisableFormatting) };
        }

        return null;
    }

    private static List<ChartValue> ReadPoints(XElement? cache, bool numeric)
    {
        if (cache is null)
        {
            return [];
        }

        var count = Math.Clamp(Int(cache.Element(C + "ptCount"), 0), 0, 1_000_000);
        var points = new ChartValue[count];
        foreach (var point in cache.Elements(C + "pt"))
        {
            if (!int.TryParse(point.Attribute("idx")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) || index < 0)
            {
                continue;
            }

            if (index >= points.Length)
            {
                Array.Resize(ref points, index + 1);
            }

            var text = point.Element(C + "v")?.Value;
            var format = point.Attribute("formatCode")?.Value;
            if (numeric && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
            {
                points[index] = new ChartValue(number, null, format);
            }
            else
            {
                points[index] = new ChartValue(null, text, format);
            }
        }

        return [.. points];
    }

    /// <summary>A c:tx: a reference (c:strRef) or a literal (c:v).</summary>
    private static ChartText? ReadText(XElement? tx)
    {
        if (tx is null)
        {
            return null;
        }

        if (tx.Element(C + "strRef") is { } strRef)
        {
            var cached = strRef.Element(C + "strCache")?.Elements(C + "pt").Select(point => point.Element(C + "v")?.Value).FirstOrDefault();
            return ChartText.Ref(strRef.Element(C + "f")?.Value ?? string.Empty, cached);
        }

        if (tx.Element(C + "v") is { } literal)
        {
            return ChartText.Of(literal.Value);
        }

        if (tx.Element(C + "rich") is { } rich)
        {
            var paragraphs = rich.Elements(A + "p").Select(paragraph => string.Concat(paragraph.Descendants(A + "t").Select(text => text.Value)));
            return ChartText.Of(string.Join("\n", paragraphs));
        }

        return null;
    }

    private static ChartTitle ReadTitle(XElement element)
    {
        var text = ReadText(element.Element(C + "tx"));
        var (size, bold) = FontOf(element.Element(C + "tx")?.Element(C + "rich")) ?? FontOf(element.Element(C + "txPr")) ?? (null, false);
        return new ChartTitle(text, Bool(element.Element(C + "overlay"), false), size, bold) { SourceXml = element.ToString(SaveOptions.DisableFormatting) };
    }

    /// <summary>The size (points) and weight of the first run or the default run properties of a text body.</summary>
    private static (double? Size, bool Bold)? FontOf(XElement? body)
    {
        if (body is null)
        {
            return null;
        }

        var properties = body.Descendants(A + "rPr").FirstOrDefault() ?? body.Descendants(A + "defRPr").FirstOrDefault();
        if (properties is null)
        {
            return null;
        }

        double? size = properties.Attribute("sz") is { } sz && double.TryParse(sz.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var hundredths) ? hundredths / 100 : null;
        var bold = properties.Attribute("b")?.Value is "1" or "true";
        return (size, bold);
    }

    private static ChartLegend ReadLegend(XElement element)
    {
        var position = Val(element.Element(C + "legendPos")) switch
        {
            "t" => LegendPosition.Top,
            "l" => LegendPosition.Left,
            "b" => LegendPosition.Bottom,
            "tr" => LegendPosition.TopRight,
            _ => LegendPosition.Right,
        };
        return new ChartLegend(position, Bool(element.Element(C + "overlay"), false), FontOf(element.Element(C + "txPr"))?.Size) { SourceXml = element.ToString(SaveOptions.DisableFormatting) };
    }

    private static DataLabels ReadLabels(XElement element)
    {
        if (Bool(element.Element(C + "delete"), false))
        {
            return new DataLabels(false) { SourceXml = element.ToString(SaveOptions.DisableFormatting) };
        }

        var position = Val(element.Element(C + "dLblPos")) switch
        {
            "ctr" => DataLabelPosition.Center,
            "inEnd" => DataLabelPosition.InsideEnd,
            "inBase" => DataLabelPosition.InsideBase,
            "outEnd" => DataLabelPosition.OutsideEnd,
            "t" => DataLabelPosition.Above,
            "b" => DataLabelPosition.Below,
            "l" => DataLabelPosition.Left,
            "r" => DataLabelPosition.Right,
            "bestFit" => DataLabelPosition.BestFit,
            _ => DataLabelPosition.Auto,
        };
        var format = element.Element(C + "numFmt") is { } numFmt && !Bool(numFmt, "sourceLinked", false) ? numFmt.Attribute("formatCode")?.Value : null;
        return new DataLabels(
            Bool(element.Element(C + "showVal"), false),
            Bool(element.Element(C + "showCatName"), false),
            Bool(element.Element(C + "showSerName"), false),
            Bool(element.Element(C + "showPercent"), false),
            position,
            format) { SourceXml = element.ToString(SaveOptions.DisableFormatting) };
    }

    // ===== Axes =====

    private static ChartAxis? ReadAxis(XElement element)
    {
        var kind = element.Name.LocalName switch
        {
            "valAx" => AxisKind.Value,
            "dateAx" => AxisKind.Date,
            _ => AxisKind.Category,
        };
        var position = Val(element.Element(C + "axPos")) switch
        {
            "l" => AxisPosition.Left,
            "r" => AxisPosition.Right,
            "t" => AxisPosition.Top,
            _ => AxisPosition.Bottom,
        };
        var scaling = element.Element(C + "scaling");
        var numFmt = element.Element(C + "numFmt");
        var crosses = Val(element.Element(C + "crosses"));
        var rotation = element.Element(C + "txPr")?.Element(A + "bodyPr")?.Attribute("rot") is { } rot && int.TryParse(rot.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var angle) && angle != -60000000
            ? angle / 60000.0
            : (double?)null;
        return new ChartAxis(Int(element.Element(C + "axId"), 0), kind, position, Int(element.Element(C + "crossAx"), 0))
        {
            Deleted = Bool(element.Element(C + "delete"), false),
            Title = element.Element(C + "title") is { } title ? ReadTitle(title) : null,
            MajorGridlines = element.Element(C + "majorGridlines") is not null,
            MinorGridlines = element.Element(C + "minorGridlines") is not null,
            NumberFormat = numFmt?.Attribute("formatCode")?.Value,
            SourceLinked = numFmt is null || Bool(numFmt, "sourceLinked", false),
            Minimum = Double(scaling?.Element(C + "min")),
            Maximum = Double(scaling?.Element(C + "max")),
            MajorUnit = Double(element.Element(C + "majorUnit")),
            MinorUnit = Double(element.Element(C + "minorUnit")),
            Reverse = Val(scaling?.Element(C + "orientation")) == "maxMin",
            Crosses = element.Element(C + "crossesAt") is not null ? AxisCrossing.Value : crosses switch
            {
                "min" => AxisCrossing.Minimum,
                "max" => AxisCrossing.Maximum,
                _ => AxisCrossing.AutoZero,
            },
            CrossesAt = Double(element.Element(C + "crossesAt")),
            CrossBetween = Val(element.Element(C + "crossBetween")) != "midCat",
            HideLabels = Val(element.Element(C + "tickLblPos")) == "none",
            FontSize = FontOf(element.Element(C + "txPr"))?.Size,
            LabelRotation = rotation,
            ShapeXml = element.Element(C + "spPr")?.ToString(SaveOptions.DisableFormatting),
            TextXml = element.Element(C + "txPr")?.ToString(SaveOptions.DisableFormatting),
        };
    }

    // ===== Values =====

    private static string? Val(XElement? element) => element?.Attribute("val")?.Value;

    /// <summary>A CT_Boolean: an element without val means true.</summary>
    private static bool Bool(XElement? element, bool missing)
    {
        if (element is null)
        {
            return missing;
        }

        return Bool(element, "val", true);
    }

    private static bool Bool(XElement element, string attribute, bool missing) =>
        element.Attribute(attribute)?.Value is { } value ? value is "1" or "true" : missing;

    private static int Int(XElement? element, int missing) =>
        element?.Attribute("val")?.Value is { } value && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number) ? number : missing;

    private static double? Double(XElement? element) =>
        element?.Attribute("val")?.Value is { } value && double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : null;

    private static double Percent(XElement? element, double missing) =>
        element?.Attribute("val")?.Value is { } value && double.TryParse(value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out var number)
            ? (value.EndsWith('%') ? number / 100 : number / 100_000)
            : missing;
}
