using System.Globalization;
using System.Xml.Linq;
using ThixOffice.Text.Drawing;

namespace ThixOffice.Charts.Formats;

/// <summary>
/// Writes a <see cref="Chart"/> as a DrawingML chart part (c:chartSpace) in schema order, with the look of Office 2016+
/// charts (text sizes and greys, light gridlines, series in theme accents). A chart read from a file starts from its
/// original XML: what the model does not own (3-D settings, the chart style, the embedded workbook's relationship, print
/// settings, extensions, unknown children of plots and series) is copied back, and parts the user did not edit keep their
/// XML (title, legend, data labels, series shapes, axis text). References are written with fresh caches from the data source
/// (other programs show the chart without recalculating). A chart of a type that is not drawn is written exactly as read.
/// </summary>
public static class ChartXmlWriter
{
    private static readonly XNamespace C = ChartNamespaces.C;
    private static readonly XNamespace A = ChartNamespaces.A;
    private static readonly XNamespace R = ChartNamespaces.R;

    private static readonly string[] PlotOrderBar = ["barDir", "grouping", "varyColors", "ser", "dLbls", "gapWidth", "overlap", "serLines", "axId", "extLst"];
    private static readonly string[] PlotOrderLine = ["grouping", "varyColors", "ser", "dLbls", "dropLines", "hiLowLines", "upDownBars", "marker", "smooth", "axId", "extLst"];
    private static readonly string[] PlotOrderPie = ["varyColors", "ser", "dLbls", "firstSliceAng", "extLst"];
    private static readonly string[] PlotOrderDoughnut = ["varyColors", "ser", "dLbls", "firstSliceAng", "holeSize", "extLst"];
    private static readonly string[] PlotOrderArea = ["grouping", "varyColors", "ser", "dLbls", "dropLines", "axId", "extLst"];
    private static readonly string[] PlotOrderScatter = ["scatterStyle", "varyColors", "ser", "dLbls", "axId", "extLst"];

    private static readonly string[] SeriesOrderBar = ["idx", "order", "tx", "spPr", "invertIfNegative", "pictureOptions", "dPt", "dLbls", "trendline", "errBars", "cat", "val", "shape", "extLst"];
    private static readonly string[] SeriesOrderLine = ["idx", "order", "tx", "spPr", "marker", "dPt", "dLbls", "trendline", "errBars", "cat", "val", "smooth", "extLst"];
    private static readonly string[] SeriesOrderPie = ["idx", "order", "tx", "spPr", "explosion", "dPt", "dLbls", "cat", "val", "extLst"];
    private static readonly string[] SeriesOrderArea = ["idx", "order", "tx", "spPr", "pictureOptions", "dPt", "dLbls", "trendline", "errBars", "cat", "val", "extLst"];
    private static readonly string[] SeriesOrderScatter = ["idx", "order", "tx", "spPr", "marker", "dPt", "dLbls", "trendline", "errBars", "xVal", "yVal", "smooth", "extLst"];

    private static readonly string[] SpaceKeptBeforeChart = ["AlternateContent", "clrMapOvr", "pivotSource", "protection"];
    private static readonly string[] SpaceKeptAfterChart = ["externalData", "printSettings", "userShapes", "extLst"];
    private static readonly string[] ChartKeptBeforePlotArea = ["pivotFmts", "view3D", "floor", "sideWall", "backWall"];

    /// <summary>The chart part's XML.</summary>
    public static string Write(Chart chart, IChartDataSource? source = null)
    {
        if (!chart.IsSupported && chart.SourceXml is { } original)
        {
            return original;
        }

        var sourceSpace = Parse(chart.SourceXml);
        var sourceChart = sourceSpace?.Element(C + "chart");
        var space = new XElement(C + "chartSpace",
            new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName));
        space.Add(Kept(sourceSpace, "date1904") ?? Value("date1904", "0"));
        space.Add(Kept(sourceSpace, "lang") ?? Value("lang", "de-DE"));
        space.Add(Value("roundedCorners", chart.RoundedCorners ? "1" : "0"));
        foreach (var name in SpaceKeptBeforeChart)
        {
            space.Add(KeptAll(sourceSpace, name));
        }

        space.Add(ChartElement(chart, sourceChart, source));
        space.Add(Kept(sourceSpace, "spPr") ?? new XElement(C + "spPr", SolidFill(Scheme("bg1")), Line(9525, Grey(15000, 85000)), new XElement(A + "effectLst")));
        space.Add(Kept(sourceSpace, "txPr") ?? new XElement(C + "txPr", new XElement(A + "bodyPr"), new XElement(A + "lstStyle"),
            new XElement(A + "p", new XElement(A + "pPr", new XElement(A + "defRPr")), new XElement(A + "endParaRPr", new XAttribute("lang", "de-DE")))));
        foreach (var name in SpaceKeptAfterChart)
        {
            space.Add(KeptAll(sourceSpace, name));
        }

        var document = new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), space);
        return document.Declaration + document.ToString(SaveOptions.DisableFormatting);
    }

    private static XElement ChartElement(Chart chart, XElement? sourceChart, IChartDataSource? source)
    {
        var element = new XElement(C + "chart");
        if (chart.Title is { } title)
        {
            element.Add(TitleElement(title, 1400, source));
        }

        element.Add(Value("autoTitleDeleted", chart.AutoTitleDeleted ? "1" : "0"));
        foreach (var name in ChartKeptBeforePlotArea)
        {
            element.Add(KeptAll(sourceChart, name));
        }

        element.Add(PlotAreaElement(chart, sourceChart?.Element(C + "plotArea"), source));
        if (chart.Legend is { } legend)
        {
            element.Add(Parse(legend.SourceXml) ?? new XElement(C + "legend",
                Value("legendPos", legend.Position switch
                {
                    LegendPosition.Top => "t",
                    LegendPosition.Left => "l",
                    LegendPosition.Right => "r",
                    LegendPosition.TopRight => "tr",
                    _ => "b",
                }),
                Value("overlay", legend.Overlay ? "1" : "0"),
                NoShape(),
                TextProperties((legend.FontSize ?? 9) * 100, Grey(65000, 35000))));
        }

        element.Add(Value("plotVisOnly", chart.PlotVisibleOnly ? "1" : "0"));
        element.Add(Value("dispBlanksAs", chart.DisplayBlanksAs switch
        {
            BlankCells.Zero => "zero",
            BlankCells.Span => "span",
            _ => "gap",
        }));
        element.Add(KeptAll(sourceChart, "extLst"));
        return element;
    }

    private static XElement PlotAreaElement(Chart chart, XElement? sourcePlotArea, IChartDataSource? source)
    {
        var element = new XElement(C + "plotArea");
        element.Add(Kept(sourcePlotArea, "layout") ?? new XElement(C + "layout"));
        var count = chart.AllSeries.Count();
        var position = 0;
        foreach (var plot in chart.Plots)
        {
            element.Add(PlotElement(chart, plot, ref position, count, source));
        }

        foreach (var axis in chart.Axes)
        {
            element.Add(AxisElement(axis, chart, source));
        }

        element.Add(KeptAll(sourcePlotArea, "dTable"));
        element.Add(Kept(sourcePlotArea, "spPr") ?? NoShape());
        element.Add(KeptAll(sourcePlotArea, "extLst"));
        return element;
    }

    // ===== Plots =====

    private static XElement PlotElement(Chart chart, ChartPlot plot, ref int position, int count, IChartDataSource? source)
    {
        var (name, order) = plot.Type switch
        {
            ChartType.Column or ChartType.Bar => ("barChart", PlotOrderBar),
            ChartType.Line => ("lineChart", PlotOrderLine),
            ChartType.Pie => ("pieChart", PlotOrderPie),
            ChartType.Doughnut => ("doughnutChart", PlotOrderDoughnut),
            ChartType.Area => ("areaChart", PlotOrderArea),
            _ => ("scatterChart", PlotOrderScatter),
        };
        var start = position;
        position += plot.Series.Count;
        var grouping = plot.Grouping switch
        {
            ChartGrouping.Stacked => "stacked",
            ChartGrouping.PercentStacked => "percentStacked",
            _ => plot.Type is ChartType.Column or ChartType.Bar ? "clustered" : "standard",
        };
        IEnumerable<XElement> Owned(string child)
        {
            switch (child)
            {
                case "barDir":
                    yield return Value("barDir", plot.Type == ChartType.Bar ? "bar" : "col");
                    break;
                case "grouping":
                    yield return Value("grouping", grouping);
                    break;
                case "varyColors":
                    yield return Value("varyColors", plot.VaryColors ? "1" : "0");
                    break;
                case "scatterStyle":
                    yield return Value("scatterStyle", plot.Series.Any(series => series.Smooth) ? "smoothMarker" : "lineMarker");
                    break;
                case "ser":
                    for (var index = 0; index < plot.Series.Count; index++)
                    {
                        yield return SeriesElement(chart, plot, plot.Series[index], start + index, count, source);
                    }

                    break;
                case "dLbls" when plot.Labels is { } labels:
                    yield return LabelsElement(labels, plot);
                    break;
                case "gapWidth":
                    yield return Value("gapWidth", plot.GapWidth.ToString(CultureInfo.InvariantCulture));
                    break;
                case "overlap" when plot.Overlap != 0 || plot.Grouping != ChartGrouping.Clustered:
                    yield return Value("overlap", Math.Clamp(plot.Overlap, -100, 100).ToString(CultureInfo.InvariantCulture));
                    break;
                case "marker":
                    yield return Value("marker", plot.ShowMarkers ? "1" : "0");
                    break;
                case "firstSliceAng":
                    yield return Value("firstSliceAng", Math.Clamp(plot.FirstSliceAngle, 0, 360).ToString(CultureInfo.InvariantCulture));
                    break;
                case "holeSize":
                    yield return Value("holeSize", Math.Clamp(plot.HoleSize, 10, 90).ToString(CultureInfo.InvariantCulture));
                    break;
                case "axId":
                    foreach (var id in plot.AxisIds)
                    {
                        yield return Value("axId", id.ToString(CultureInfo.InvariantCulture));
                    }

                    break;
            }
        }

        return InOrder(new XElement(C + name), order, Owned, plot.ExtraXml);
    }

    private static XElement SeriesElement(Chart chart, ChartPlot plot, ChartSeries series, int position, int count, IChartDataSource? source)
    {
        var order = plot.Type switch
        {
            ChartType.Column or ChartType.Bar => SeriesOrderBar,
            ChartType.Line => SeriesOrderLine,
            ChartType.Pie or ChartType.Doughnut => SeriesOrderPie,
            ChartType.Area => SeriesOrderArea,
            _ => SeriesOrderScatter,
        };
        var lineLike = plot.Type is ChartType.Line or ChartType.Scatter;
        var color = series.Fill ?? series.Line ?? ChartColors.SeriesColor(chart.ColorSet, position, count);
        IEnumerable<XElement> Owned(string child)
        {
            switch (child)
            {
                case "idx":
                    yield return Value("idx", series.Index.ToString(CultureInfo.InvariantCulture));
                    break;
                case "order":
                    yield return Value("order", series.Order.ToString(CultureInfo.InvariantCulture));
                    break;
                case "tx" when series.Name is { } name:
                    yield return new XElement(C + "tx", TextReference(name, source));
                    break;
                case "spPr":
                    yield return Parse(series.ShapeXml) ?? SeriesShape(plot, series, color);
                    break;
                case "invertIfNegative":
                    yield return Value("invertIfNegative", "0");
                    break;
                case "marker" when lineLike:
                    yield return MarkerElement(plot, series, series.Line ?? color);
                    break;
                case "explosion" when series.Explosion > 0:
                    yield return Value("explosion", series.Explosion.ToString(CultureInfo.InvariantCulture));
                    break;
                case "dPt" when series.PointColors is { } points:
                    foreach (var (index, pointColor) in points.OrderBy(entry => entry.Key))
                    {
                        var point = new XElement(C + "dPt", Value("idx", index.ToString(CultureInfo.InvariantCulture)));
                        if (plot.Type is ChartType.Column or ChartType.Bar)
                        {
                            point.Add(Value("invertIfNegative", "0"));
                        }

                        point.Add(Value("bubble3D", "0"));
                        point.Add(lineLike
                            ? new XElement(C + "spPr", new XElement(A + "ln", new XAttribute("w", "28575"), new XAttribute("cap", "rnd"), SolidFill(ColorElement(pointColor)), new XElement(A + "round")))
                            : new XElement(C + "spPr", SolidFill(ColorElement(pointColor)), plot.IsRound ? Line(19050, Scheme("lt1")) : new XElement(A + "ln", new XElement(A + "noFill"))));
                        yield return point;
                    }

                    break;
                case "dLbls" when series.Labels is { } labels:
                    yield return LabelsElement(labels, plot);
                    break;
                case "cat" when series.Categories is not null && plot.Type != ChartType.Scatter:
                    yield return DataElement("cat", series.Categories!, source, numericWrapper: false);
                    break;
                case "val":
                    yield return DataElement("val", series.Values, source, numericWrapper: true);
                    break;
                case "xVal" when series.Categories is { } xValues:
                    yield return DataElement("xVal", xValues, source, numericWrapper: false);
                    break;
                case "yVal":
                    yield return DataElement("yVal", series.Values, source, numericWrapper: true);
                    break;
                case "smooth" when lineLike:
                    yield return Value("smooth", series.Smooth ? "1" : "0");
                    break;
            }
        }

        return InOrder(new XElement(C + "ser"), order, Owned, series.ExtraXml);
    }

    private static XElement SeriesShape(ChartPlot plot, ChartSeries series, ChartColor color)
    {
        if (plot.Type is ChartType.Line or ChartType.Scatter)
        {
            var width = (long)Math.Round((series.LineWidth ?? 2.25) * 12700);
            var line = series.NoLine
                ? new XElement(A + "ln", new XAttribute("w", "25400"), new XAttribute("cap", "rnd"), new XElement(A + "noFill"), new XElement(A + "round"))
                : new XElement(A + "ln", new XAttribute("w", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("cap", "rnd"), SolidFill(ColorElement(series.Line ?? color)), new XElement(A + "round"));
            return new XElement(C + "spPr", line, new XElement(A + "effectLst"));
        }

        var fill = series.Fill?.Rgb is { A: 0 } ? new XElement(A + "noFill") : SolidFill(ColorElement(series.Fill ?? color));
        var outline = plot.IsRound ? Line(19050, Scheme("lt1")) : new XElement(A + "ln", new XElement(A + "noFill"));
        return new XElement(C + "spPr", fill, outline, new XElement(A + "effectLst"));
    }

    private static XElement MarkerElement(ChartPlot plot, ChartSeries series, ChartColor color)
    {
        var symbol = series.Marker switch
        {
            MarkerSymbol.None => "none",
            MarkerSymbol.Square => "square",
            MarkerSymbol.Diamond => "diamond",
            MarkerSymbol.Triangle => "triangle",
            MarkerSymbol.X => "x",
            MarkerSymbol.Plus => "plus",
            MarkerSymbol.Dash => "dash",
            MarkerSymbol.Star => "star",
            MarkerSymbol.Circle => "circle",
            _ => plot.Type == ChartType.Line && !plot.ShowMarkers ? "none" : "circle",
        };
        if (symbol == "none")
        {
            return new XElement(C + "marker", Value("symbol", "none"));
        }

        var size = (int)Math.Clamp(Math.Round(series.MarkerSize ?? 5), 2, 72);
        return new XElement(C + "marker",
            Value("symbol", symbol),
            Value("size", size.ToString(CultureInfo.InvariantCulture)),
            new XElement(C + "spPr", SolidFill(ColorElement(color)), new XElement(A + "ln", new XAttribute("w", "9525"), SolidFill(ColorElement(color))), new XElement(A + "effectLst")));
    }

    private static XElement LabelsElement(DataLabels labels, ChartPlot plot)
    {
        if (Parse(labels.SourceXml) is { } kept)
        {
            return kept;
        }

        var element = new XElement(C + "dLbls");
        if (labels.NumberFormat is { } format)
        {
            element.Add(new XElement(C + "numFmt", new XAttribute("formatCode", format), new XAttribute("sourceLinked", "0")));
        }

        element.Add(NoShape());
        element.Add(TextProperties(900, Grey(75000, 25000)));
        if (PositionValue(labels.Position, plot) is { } position)
        {
            element.Add(Value("dLblPos", position));
        }

        element.Add(Value("showLegendKey", "0"));
        element.Add(Value("showVal", labels.ShowValue ? "1" : "0"));
        element.Add(Value("showCatName", labels.ShowCategory ? "1" : "0"));
        element.Add(Value("showSerName", labels.ShowSeriesName ? "1" : "0"));
        element.Add(Value("showPercent", labels.ShowPercent ? "1" : "0"));
        element.Add(Value("showBubbleSize", "0"));
        if (plot.IsRound)
        {
            element.Add(Value("showLeaderLines", "1"));
        }

        return element;
    }

    /// <summary>dLblPos only where the chart type allows it (Excel refuses a file with, say, "t" on columns).</summary>
    private static string? PositionValue(DataLabelPosition position, ChartPlot plot) => (plot.Type, position) switch
    {
        (_, DataLabelPosition.Auto) => null,
        (ChartType.Area or ChartType.Doughnut, _) => null,
        (ChartType.Column or ChartType.Bar, DataLabelPosition.Center) => "ctr",
        (ChartType.Column or ChartType.Bar, DataLabelPosition.InsideEnd) => "inEnd",
        (ChartType.Column or ChartType.Bar, DataLabelPosition.InsideBase) => "inBase",
        (ChartType.Column or ChartType.Bar, DataLabelPosition.OutsideEnd) when plot.Grouping == ChartGrouping.Clustered => "outEnd",
        (ChartType.Line or ChartType.Scatter, DataLabelPosition.Center) => "ctr",
        (ChartType.Line or ChartType.Scatter, DataLabelPosition.Above) => "t",
        (ChartType.Line or ChartType.Scatter, DataLabelPosition.Below) => "b",
        (ChartType.Line or ChartType.Scatter, DataLabelPosition.Left) => "l",
        (ChartType.Line or ChartType.Scatter, DataLabelPosition.Right) => "r",
        (ChartType.Pie, DataLabelPosition.Center) => "ctr",
        (ChartType.Pie, DataLabelPosition.InsideEnd) => "inEnd",
        (ChartType.Pie, DataLabelPosition.OutsideEnd) => "outEnd",
        (ChartType.Pie, DataLabelPosition.BestFit) => "bestFit",
        _ => null,
    };

    // ===== Data =====

    private static XElement DataElement(string wrapper, ChartDataRef reference, IChartDataSource? source, bool numericWrapper)
    {
        if (reference.MultiLevelXml is { } multi && Parse(multi) is { } kept)
        {
            return new XElement(C + wrapper, kept.Elements());
        }

        var values = reference.Formula is { Length: > 0 } formula && source?.Resolve(formula) is { } resolved ? resolved : reference.Cache ?? [];
        var numeric = numericWrapper || (reference.IsNumeric && values.Any(value => value.Number is not null) && values.All(value => value.IsBlank || value.Number is not null));
        var format = reference.FormatCode ?? values.Select(value => value.FormatCode).FirstOrDefault(code => code is not null) ?? "General";
        XElement Points(bool asNumbers)
        {
            var cache = new List<XElement>();
            if (asNumbers)
            {
                cache.Add(new XElement(C + "formatCode", format));
            }

            cache.Add(Value("ptCount", values.Count.ToString(CultureInfo.InvariantCulture)));
            for (var index = 0; index < values.Count; index++)
            {
                var value = values[index];
                string? text = asNumbers
                    ? value.Number?.ToString("R", CultureInfo.InvariantCulture)
                    : value.Text ?? value.Number?.ToString(CultureInfo.InvariantCulture);
                if (text is null)
                {
                    continue;
                }

                cache.Add(new XElement(C + "pt", new XAttribute("idx", index.ToString(CultureInfo.InvariantCulture)), new XElement(C + "v", text)));
            }

            return new XElement(C + (asNumbers ? "numCache" : "strCache"), cache);
        }

        if (reference.Formula is { Length: > 0 } text)
        {
            return numeric
                ? new XElement(C + wrapper, new XElement(C + "numRef", new XElement(C + "f", text), Points(true)))
                : new XElement(C + wrapper, new XElement(C + "strRef", new XElement(C + "f", text), Points(false)));
        }

        var literal = Points(numeric);
        literal.Name = C + (numeric ? "numLit" : "strLit");
        return new XElement(C + wrapper, literal);
    }

    private static XElement TextReference(ChartText text, IChartDataSource? source)
    {
        if (text.Reference is { Formula: { Length: > 0 } formula } reference)
        {
            var cached = source?.Resolve(formula) is { Count: > 0 } values
                ? string.Join(" ", values.Select(value => value.Text ?? value.Number?.ToString(CultureInfo.InvariantCulture) ?? string.Empty))
                : reference.Cache?.FirstOrDefault().Text ?? string.Empty;
            return new XElement(C + "strRef", new XElement(C + "f", formula),
                new XElement(C + "strCache", Value("ptCount", "1"), new XElement(C + "pt", new XAttribute("idx", "0"), new XElement(C + "v", cached))));
        }

        return new XElement(C + "v", text.Literal ?? string.Empty);
    }

    // ===== Titles and axes =====

    private static XElement TitleElement(ChartTitle title, int defaultSize, IChartDataSource? source)
    {
        if (Parse(title.SourceXml) is { } kept)
        {
            return kept;
        }

        var size = (int)Math.Round((title.FontSize ?? defaultSize / 100.0) * 100);
        var element = new XElement(C + "title");
        var color = Grey(65000, 35000);
        if (title.Text is { Reference: not null } reference)
        {
            element.Add(new XElement(C + "tx", TextReference(reference, source)));
        }
        else if (title.Text is { Literal: { } literal })
        {
            var rich = new XElement(C + "rich", BodyProperties(), new XElement(A + "lstStyle"));
            foreach (var line in literal.Replace("\r", string.Empty, StringComparison.Ordinal).Split('\n'))
            {
                rich.Add(new XElement(A + "p",
                    new XElement(A + "pPr", RunProperties("defRPr", size, title.Bold, color)),
                    new XElement(A + "r", new XElement(A + "rPr", new XAttribute("lang", "de-DE"), title.Bold ? new XAttribute("b", "1") : null), new XElement(A + "t", line))));
            }

            element.Add(new XElement(C + "tx", rich));
        }

        element.Add(Value("overlay", title.Overlay ? "1" : "0"));
        element.Add(NoShape());
        if (title.Text is not { Literal: not null })
        {
            element.Add(TextProperties(size, color, title.Bold));
        }

        return element;
    }

    private static XElement AxisElement(ChartAxis axis, Chart chart, IChartDataSource? source)
    {
        var name = axis.Kind switch
        {
            AxisKind.Value => "valAx",
            AxisKind.Date => "dateAx",
            _ => "catAx",
        };

        // Scatter x axes are value axes in the file.
        if (axis.Kind == AxisKind.Category && chart.Plots.Any(plot => plot.Type == ChartType.Scatter && plot.AxisIds.Count > 0 && plot.AxisIds[0] == axis.Id))
        {
            name = "valAx";
        }

        var element = new XElement(C + name, Value("axId", axis.Id.ToString(CultureInfo.InvariantCulture)));
        var scaling = new XElement(C + "scaling", Value("orientation", axis.Reverse ? "maxMin" : "minMax"));
        if (axis.Maximum is { } maximum)
        {
            scaling.Add(Value("max", maximum.ToString("R", CultureInfo.InvariantCulture)));
        }

        if (axis.Minimum is { } minimum)
        {
            scaling.Add(Value("min", minimum.ToString("R", CultureInfo.InvariantCulture)));
        }

        element.Add(scaling);
        element.Add(Value("delete", axis.Deleted ? "1" : "0"));
        element.Add(Value("axPos", axis.Position switch
        {
            AxisPosition.Left => "l",
            AxisPosition.Right => "r",
            AxisPosition.Top => "t",
            _ => "b",
        }));
        if (axis.MajorGridlines)
        {
            element.Add(new XElement(C + "majorGridlines", new XElement(C + "spPr", Line(9525, Grey(15000, 85000)), new XElement(A + "effectLst"))));
        }

        if (axis.MinorGridlines)
        {
            element.Add(new XElement(C + "minorGridlines", new XElement(C + "spPr", Line(9525, Grey(5000, 95000)), new XElement(A + "effectLst"))));
        }

        if (axis.Title is { } title)
        {
            element.Add(TitleElement(title, 1000, source) is var titleElement && title.SourceXml is null && axis.IsVertical && title.Text is not { Literal: not null }
                ? RotateTitle(titleElement)
                : titleElement);
        }

        element.Add(new XElement(C + "numFmt", new XAttribute("formatCode", axis.NumberFormat ?? "General"), new XAttribute("sourceLinked", axis.SourceLinked ? "1" : "0")));
        element.Add(Value("majorTickMark", "none"));
        element.Add(Value("minorTickMark", "none"));
        element.Add(Value("tickLblPos", axis.HideLabels ? "none" : "nextTo"));
        element.Add(Parse(axis.ShapeXml) ?? (axis.Kind == AxisKind.Value && name == "valAx" && axis.IsVertical
            ? new XElement(C + "spPr", new XElement(A + "noFill"), new XElement(A + "ln", new XElement(A + "noFill")), new XElement(A + "effectLst"))
            : new XElement(C + "spPr", new XElement(A + "noFill"), Line(9525, Grey(15000, 85000)), new XElement(A + "effectLst"))));
        element.Add(Parse(axis.TextXml) ?? TextProperties((int)Math.Round((axis.FontSize ?? 9) * 100), Grey(65000, 35000), rotation: axis.LabelRotation));
        element.Add(Value("crossAx", axis.CrossAxisId.ToString(CultureInfo.InvariantCulture)));
        if (axis.Crosses == AxisCrossing.Value && axis.CrossesAt is { } at)
        {
            element.Add(Value("crossesAt", at.ToString("R", CultureInfo.InvariantCulture)));
        }
        else
        {
            element.Add(Value("crosses", axis.Crosses switch
            {
                AxisCrossing.Minimum => "min",
                AxisCrossing.Maximum => "max",
                _ => "autoZero",
            }));
        }

        switch (name)
        {
            case "valAx":
                element.Add(Value("crossBetween", axis.CrossBetween ? "between" : "midCat"));
                if (axis.MajorUnit is { } major)
                {
                    element.Add(Value("majorUnit", major.ToString("R", CultureInfo.InvariantCulture)));
                }

                if (axis.MinorUnit is { } minor)
                {
                    element.Add(Value("minorUnit", minor.ToString("R", CultureInfo.InvariantCulture)));
                }

                break;
            case "catAx":
                element.Add(Value("auto", "1"));
                element.Add(Value("lblAlgn", "ctr"));
                element.Add(Value("lblOffset", "100"));
                element.Add(Value("noMultiLvlLbl", "0"));
                break;
            default:
                element.Add(Value("auto", "1"));
                element.Add(Value("lblOffset", "100"));
                element.Add(Value("baseTimeUnit", "days"));
                if (axis.MajorUnit is { } dateMajor)
                {
                    element.Add(Value("majorUnit", dateMajor.ToString("R", CultureInfo.InvariantCulture)));
                }

                break;
        }

        return element;
    }

    /// <summary>A vertical axis' title reads upwards.</summary>
    private static XElement RotateTitle(XElement title)
    {
        foreach (var body in title.Descendants(A + "bodyPr"))
        {
            body.SetAttributeValue("rot", "-5400000");
            body.SetAttributeValue("vert", "horz");
        }

        return title;
    }

    // ===== Building blocks =====

    private static XElement InOrder(XElement parent, string[] order, Func<string, IEnumerable<XElement>> owned, IReadOnlyList<string> extras)
    {
        var extraElements = extras.Select(Parse).Where(element => element is not null).Select(element => element!).ToList();
        foreach (var name in order)
        {
            parent.Add(owned(name));
            parent.Add(extraElements.Where(element => element.Name.Namespace == C && element.Name.LocalName == name));
        }

        return parent;
    }

    private static XElement Value(string name, string value) => new(C + name, new XAttribute("val", value));

    private static XElement? Kept(XElement? parent, string name) => parent?.Elements().FirstOrDefault(element => element.Name.LocalName == name && element.Name.Namespace == C) is { } element ? new XElement(element) : null;

    private static IEnumerable<XElement> KeptAll(XElement? parent, string name) =>
        parent?.Elements().Where(element => element.Name.LocalName == name).Select(element => new XElement(element)).ToList() ?? [];

    private static XElement? Parse(string? xml)
    {
        if (string.IsNullOrEmpty(xml))
        {
            return null;
        }

        try
        {
            return XDocument.Parse(xml).Root is { } root ? new XElement(root) : null;
        }
        catch (System.Xml.XmlException)
        {
            return null;
        }
    }

    private static XElement NoShape() => new(C + "spPr", new XElement(A + "noFill"), new XElement(A + "ln", new XElement(A + "noFill")), new XElement(A + "effectLst"));

    private static XElement Scheme(string name) => new(A + "schemeClr", new XAttribute("val", name));

    /// <summary>tx1 at a luminance (Office's greys of chart text and lines).</summary>
    private static XElement Grey(int lumMod, int lumOff) => new(A + "schemeClr", new XAttribute("val", "tx1"),
        new XElement(A + "lumMod", new XAttribute("val", lumMod.ToString(CultureInfo.InvariantCulture))),
        new XElement(A + "lumOff", new XAttribute("val", lumOff.ToString(CultureInfo.InvariantCulture))));

    private static XElement SolidFill(XElement color) => new(A + "solidFill", color);

    private static XElement Line(int width, XElement color) =>
        new(A + "ln", new XAttribute("w", width.ToString(CultureInfo.InvariantCulture)), new XAttribute("cap", "flat"), new XAttribute("cmpd", "sng"), new XAttribute("algn", "ctr"), SolidFill(color), new XElement(A + "round"));

    /// <summary>A chart colour: a scheme accent with luminance, or an RGB value.</summary>
    internal static XElement ColorElement(ChartColor color)
    {
        if (color.Rgb is { } rgb || color.Accent is < 1 or > 6)
        {
            var value = color.Rgb ?? RgbColor.Black;
            return new XElement(A + "srgbClr", new XAttribute("val", value.ToHex()));
        }

        var scheme = new XElement(A + "schemeClr", new XAttribute("val", "accent" + color.Accent.ToString(CultureInfo.InvariantCulture)));
        if (Math.Abs(color.LumMod - 1) > 1e-9)
        {
            scheme.Add(new XElement(A + "lumMod", new XAttribute("val", ((int)Math.Round(color.LumMod * 100_000)).ToString(CultureInfo.InvariantCulture))));
        }

        if (Math.Abs(color.LumOff) > 1e-9)
        {
            scheme.Add(new XElement(A + "lumOff", new XAttribute("val", ((int)Math.Round(color.LumOff * 100_000)).ToString(CultureInfo.InvariantCulture))));
        }

        return scheme;
    }

    private static XElement BodyProperties(double? rotation = null) => new(A + "bodyPr",
        new XAttribute("rot", rotation is { } degrees ? ((int)Math.Round(degrees * 60000)).ToString(CultureInfo.InvariantCulture) : "-60000000"),
        new XAttribute("spcFirstLastPara", "1"), new XAttribute("vertOverflow", "ellipsis"), new XAttribute("vert", "horz"),
        new XAttribute("wrap", "square"), new XAttribute("anchor", "ctr"), new XAttribute("anchorCtr", "1"));

    private static XElement RunProperties(string name, int size, bool bold, XElement color) => new(A + name,
        new XAttribute("sz", size.ToString(CultureInfo.InvariantCulture)), new XAttribute("b", bold ? "1" : "0"), new XAttribute("i", "0"),
        new XAttribute("u", "none"), new XAttribute("strike", "noStrike"), new XAttribute("kern", "1200"), new XAttribute("baseline", "0"),
        SolidFill(new XElement(color)),
        new XElement(A + "latin", new XAttribute("typeface", "+mn-lt")),
        new XElement(A + "ea", new XAttribute("typeface", "+mn-ea")),
        new XElement(A + "cs", new XAttribute("typeface", "+mn-cs")));

    private static XElement TextProperties(double size, XElement color, bool bold = false, double? rotation = null) => new(C + "txPr",
        BodyProperties(rotation),
        new XElement(A + "lstStyle"),
        new XElement(A + "p", new XElement(A + "pPr", RunProperties("defRPr", (int)Math.Round(size), bold, color)), new XElement(A + "endParaRPr", new XAttribute("lang", "de-DE"))));
}
