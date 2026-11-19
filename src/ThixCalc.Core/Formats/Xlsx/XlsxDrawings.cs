using System.Globalization;
using System.Text;
using System.Xml.Linq;
using DocumentFormat.OpenXml.Packaging;
using ThixCalc.Core.Charts;
using ThixCalc.Core.Model;
using ThixOffice.Charts;
using ThixOffice.Charts.Formats;

namespace ThixCalc.Core.Formats.Xlsx;

/// <summary>
/// A worksheet's drawing part (xdr:wsDr): its chart frames become <see cref="SheetChart"/>s (anchor + the chart part read by
/// <see cref="ChartXml"/>); pictures, shapes and charts of the newer kinds (chartex) are kept as XML. Writing leaves the part
/// untouched while the charts are as read; otherwise the drawing is written anew – kept anchors first, then the charts in
/// their order – unchanged charts keep their parts, changed ones get their XML replaced, new ones new parts, deleted ones
/// lose theirs.
/// </summary>
internal static class XlsxDrawings
{
    private static readonly XNamespace Xdr = "http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing";
    private static readonly XNamespace A = ChartNamespaces.A;
    private static readonly XNamespace C = ChartNamespaces.C;
    private static readonly XNamespace R = ChartNamespaces.R;
    private static readonly XNamespace Mc = ChartNamespaces.Mc;
    private const string ChartUri = "http://schemas.openxmlformats.org/drawingml/2006/chart";
    private const double EmuPerPoint = 12700;

    public static void Read(Worksheet sheet, WorksheetPart part, ReadContext context)
    {
        if (part.DrawingsPart is not { } drawings)
        {
            return;
        }

        XDocument document;
        try
        {
            using var stream = drawings.GetStream();
            document = XDocument.Load(stream);
        }
        catch (System.Xml.XmlException)
        {
            return;
        }

        if (document.Root is not { } root)
        {
            return;
        }

        sheet.DrawingNamespaces.AddRange(root.Attributes().Where(attribute => attribute.IsNamespaceDeclaration).Select(attribute => (attribute.Name.LocalName, attribute.Value)));
        foreach (var element in root.Elements())
        {
            if (ReadChart(sheet, drawings, element, context) is { } chart)
            {
                sheet.Charts.Add(chart);
                continue;
            }

            sheet.PreservedDrawingAnchors.Add(element.ToString(SaveOptions.DisableFormatting));
        }

        sheet.LoadedCharts = [.. sheet.Charts];
    }

    private static SheetChart? ReadChart(Worksheet sheet, DrawingsPart drawings, XElement element, ReadContext context)
    {
        // A chart of the newer kinds (waterfall, treemap …) sits in mc:AlternateContent with cx:chart: kept, shown as a frame.
        if (element.Name == Mc + "AlternateContent")
        {
            var inner = element.Elements(Mc + "Choice").Elements().FirstOrDefault();
            var frame = inner?.Element(Xdr + "graphicFrame");
            if (inner is null || frame is null || AnchorOf(sheet, inner) is not { } exAnchor)
            {
                return null;
            }

            var unsupported = new Chart { UnsupportedType = "Neuer Diagrammtyp (Wasserfall, Treemap …)", Legend = null };
            context.Note("Diagrammtypen, die ThixCalc nicht anzeigt (erscheinen als Rahmen)");
            return new SheetChart(context.Workbook.NextChartId(), exAnchor, unsupported)
            {
                Name = NameOf(frame) ?? "Diagramm",
                Loaded = unsupported,
                AnchorXml = element.ToString(SaveOptions.DisableFormatting),
                DrawingId = IdOf(frame),
            };
        }

        var graphicFrame = element.Element(Xdr + "graphicFrame");
        var reference = graphicFrame?.Element(A + "graphic")?.Element(A + "graphicData") is { } data && data.Attribute("uri")?.Value == ChartUri
            ? data.Element(C + "chart")?.Attribute(R + "id")?.Value
            : null;
        if (graphicFrame is null || reference is null || AnchorOf(sheet, element) is not { } anchor)
        {
            return null;
        }

        if (!drawings.TryGetPartById(reference, out var chartPart) || chartPart is not ChartPart)
        {
            return null;
        }

        string xml;
        using (var stream = chartPart.GetStream())
        using (var reader = new StreamReader(stream, Encoding.UTF8))
        {
            xml = reader.ReadToEnd();
        }

        var chart = ChartXml.Read(xml);
        if (!chart.IsSupported)
        {
            context.Note("Diagrammtypen, die ThixCalc nicht anzeigt (erscheinen als Rahmen)");
        }

        return new SheetChart(context.Workbook.NextChartId(), anchor, chart)
        {
            Name = NameOf(graphicFrame) ?? "Diagramm",
            PartUri = reference,
            Loaded = chart,
            AnchorXml = element.ToString(SaveOptions.DisableFormatting),
            DrawingId = IdOf(graphicFrame),
        };
    }

    private static string? NameOf(XElement frame) => frame.Element(Xdr + "nvGraphicFramePr")?.Element(Xdr + "cNvPr")?.Attribute("name")?.Value;

    private static int IdOf(XElement frame) =>
        int.TryParse(frame.Element(Xdr + "nvGraphicFramePr")?.Element(Xdr + "cNvPr")?.Attribute("id")?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var id) ? id : 0;

    /// <summary>The anchor of a two-cell, one-cell or absolute anchor (the last two by the sheet's column widths and row heights).</summary>
    private static ChartAnchor? AnchorOf(Worksheet sheet, XElement element)
    {
        var from = element.Element(Xdr + "from");
        switch (element.Name.LocalName)
        {
            case "twoCellAnchor" when from is not null && element.Element(Xdr + "to") is { } to:
                var (fromCell, fromX, fromY) = Marker(from);
                var (toCell, toX, toY) = Marker(to);
                return new ChartAnchor(fromCell, fromX, fromY, toCell, toX, toY);
            case "oneCellAnchor" when from is not null && element.Element(Xdr + "ext") is { } extent:
                var (start, startX, startY) = Marker(from);
                return Extend(sheet, start, startX, startY, Emu(extent.Attribute("cx")?.Value), Emu(extent.Attribute("cy")?.Value));
            case "absoluteAnchor" when element.Element(Xdr + "pos") is { } position && element.Element(Xdr + "ext") is { } size:
                return Extend(sheet, new CellAddress(0, 0), Emu(position.Attribute("x")?.Value), Emu(position.Attribute("y")?.Value), Emu(size.Attribute("cx")?.Value), Emu(size.Attribute("cy")?.Value));
            default:
                return null;
        }
    }

    private static (CellAddress Cell, double X, double Y) Marker(XElement marker)
    {
        int Number(string name) => int.TryParse(marker.Element(Xdr + name)?.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
        var row = Math.Clamp(Number("row"), 0, SheetLimits.LastRow);
        var column = Math.Clamp(Number("col"), 0, SheetLimits.LastColumn);
        return (new CellAddress(row, column), Emu(marker.Element(Xdr + "colOff")?.Value), Emu(marker.Element(Xdr + "rowOff")?.Value));
    }

    private static double Emu(string? text) => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var emu) ? emu / EmuPerPoint : 0;

    /// <summary>The two-cell anchor of a size given in points, walking the columns (Calibri 11's 7-pixel digit) and rows.</summary>
    private static ChartAnchor Extend(Worksheet sheet, CellAddress start, double startX, double startY, double width, double height)
    {
        static double ColumnPoints(double characters) => (Math.Truncate((characters * 7 + 5) / 7 * 256) / 256 * 7) * 0.75;
        var column = start.Column;
        var x = startX + width;
        while (column < SheetLimits.LastColumn && x > ColumnPoints(sheet.Columns[column].Size ?? sheet.Columns.DefaultSize))
        {
            x -= ColumnPoints(sheet.Columns[column].Size ?? sheet.Columns.DefaultSize);
            column++;
        }

        var row = start.Row;
        var y = startY + height;
        while (row < SheetLimits.LastRow && y > (sheet.Rows[row].Size ?? sheet.Rows.DefaultSize))
        {
            y -= sheet.Rows[row].Size ?? sheet.Rows.DefaultSize;
            row++;
        }

        return new ChartAnchor(start, startX, startY, new CellAddress(row, column), Math.Max(0, x), Math.Max(0, y));
    }

    // ===== Writing =====

    /// <summary>
    /// Writes the drawing of a changed sheet. Returns whether the sheet's &lt;drawing&gt; element is decided here (true) and the
    /// relationship id to write (null: no drawing); false keeps the element as read.
    /// </summary>
    public static (bool Handled, string? RelationshipId) Write(Worksheet sheet, WorksheetPart part, WriteContext context)
    {
        var unchanged = sheet.Charts.Count == sheet.LoadedCharts.Count
            && sheet.Charts.Zip(sheet.LoadedCharts).All(pair => ReferenceEquals(pair.First, pair.Second));
        if (unchanged)
        {
            return (false, null);
        }

        if (sheet.Charts.Count == 0 && sheet.PreservedDrawingAnchors.Count == 0)
        {
            // The last chart went: so does the drawing.
            if (part.DrawingsPart is { } stale)
            {
                part.DeletePart(stale);
            }

            return (true, null);
        }

        var drawings = part.DrawingsPart ?? part.AddNewPart<DrawingsPart>();
        var source = new WorkbookChartSource(context.Workbook, context.Workbook.Calculation.Locale);
        var kept = new HashSet<string>(StringComparer.Ordinal);
        var anchors = new List<XElement>();
        var usedIds = new HashSet<int>();
        foreach (var xml in sheet.PreservedDrawingAnchors)
        {
            var element = XElement.Parse(xml);
            foreach (var id in element.Descendants(Xdr + "cNvPr").Select(cNvPr => cNvPr.Attribute("id")?.Value))
            {
                if (int.TryParse(id, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number))
                {
                    usedIds.Add(number);
                }
            }

            anchors.Add(element);
        }

        var nextId = Math.Max(2, usedIds.DefaultIfEmpty(1).Max() + 1);
        foreach (var chart in sheet.Charts)
        {
            if (!chart.Chart.IsSupported && chart.PartUri is null && chart.AnchorXml is { } exXml)
            {
                anchors.Add(XElement.Parse(exXml));
                continue;
            }

            string relationship;
            if (chart.PartUri is { } existing && drawings.TryGetPartById(existing, out var chartPart) && chartPart is ChartPart && !kept.Contains(existing))
            {
                if (!ReferenceEquals(chart.Chart, chart.Loaded))
                {
                    Feed(chartPart, ChartXmlWriter.Write(chart.Chart, source));
                }

                relationship = existing;
            }
            else
            {
                var added = drawings.AddNewPart<ChartPart>();
                Feed(added, ChartXmlWriter.Write(WithoutRelationships(chart.Chart), source));
                relationship = drawings.GetIdOfPart(added);
            }

            kept.Add(relationship);
            var drawingId = chart.DrawingId > 0 && usedIds.Add(chart.DrawingId) ? chart.DrawingId : NextFree(usedIds, ref nextId);
            anchors.Add(AnchorElement(chart, relationship, drawingId));
        }

        foreach (var orphan in drawings.ChartParts.ToList())
        {
            if (!kept.Contains(drawings.GetIdOfPart(orphan)))
            {
                drawings.DeletePart(orphan);
            }
        }

        var root = new XElement(Xdr + "wsDr",
            new XAttribute(XNamespace.Xmlns + "xdr", Xdr.NamespaceName),
            new XAttribute(XNamespace.Xmlns + "a", A.NamespaceName),
            anchors);
        using (var stream = new MemoryStream())
        {
            using (var writer = System.Xml.XmlWriter.Create(stream, new System.Xml.XmlWriterSettings { Encoding = new UTF8Encoding(false) }))
            {
                new XDocument(new XDeclaration("1.0", "UTF-8", "yes"), root).Save(writer);
            }

            stream.Position = 0;
            drawings.FeedData(stream);
        }

        return (true, part.GetIdOfPart(drawings));
    }

    private static int NextFree(HashSet<int> used, ref int next)
    {
        while (!used.Add(next))
        {
            next++;
        }

        return next++;
    }

    /// <summary>A chart going into a new part loses the elements that point to the old part's relationships (its embedded workbook).</summary>
    private static Chart WithoutRelationships(Chart chart)
    {
        if (chart.SourceXml is not { } xml)
        {
            return chart;
        }

        try
        {
            var document = XDocument.Parse(xml);
            document.Root?.Elements().Where(element => element.Name.LocalName is "externalData" or "userShapes").Remove();
            return chart with { SourceXml = document.ToString(SaveOptions.DisableFormatting) };
        }
        catch (System.Xml.XmlException)
        {
            return chart with { SourceXml = null };
        }
    }

    private static void Feed(OpenXmlPart part, string xml)
    {
        using var stream = new MemoryStream(new UTF8Encoding(false).GetBytes(xml));
        part.FeedData(stream);
    }

    private static XElement AnchorElement(SheetChart chart, string relationship, int drawingId)
    {
        var anchor = chart.Anchor;
        static XElement Marker(string name, CellAddress cell, double x, double y) => new(Xdr + name,
            new XElement(Xdr + "col", cell.Column.ToString(CultureInfo.InvariantCulture)),
            new XElement(Xdr + "colOff", ((long)Math.Round(x * EmuPerPoint)).ToString(CultureInfo.InvariantCulture)),
            new XElement(Xdr + "row", cell.Row.ToString(CultureInfo.InvariantCulture)),
            new XElement(Xdr + "rowOff", ((long)Math.Round(y * EmuPerPoint)).ToString(CultureInfo.InvariantCulture)));

        return new XElement(Xdr + "twoCellAnchor",
            Marker("from", anchor.From, anchor.FromX, anchor.FromY),
            Marker("to", anchor.To, anchor.ToX, anchor.ToY),
            new XElement(Xdr + "graphicFrame", new XAttribute("macro", string.Empty),
                new XElement(Xdr + "nvGraphicFramePr",
                    new XElement(Xdr + "cNvPr", new XAttribute("id", drawingId.ToString(CultureInfo.InvariantCulture)), new XAttribute("name", chart.Name)),
                    new XElement(Xdr + "cNvGraphicFramePr")),
                new XElement(Xdr + "xfrm",
                    new XElement(A + "off", new XAttribute("x", "0"), new XAttribute("y", "0")),
                    new XElement(A + "ext", new XAttribute("cx", "0"), new XAttribute("cy", "0"))),
                new XElement(A + "graphic",
                    new XElement(A + "graphicData", new XAttribute("uri", ChartUri),
                        new XElement(C + "chart",
                            new XAttribute(XNamespace.Xmlns + "c", C.NamespaceName),
                            new XAttribute(XNamespace.Xmlns + "r", R.NamespaceName),
                            new XAttribute(R + "id", relationship))))),
            new XElement(Xdr + "clientData"));
    }
}
