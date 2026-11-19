using System.Globalization;
using System.Xml.Linq;
using ThixCalc.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixCalc.Core.Formats.Ods;

public static partial class OdsWriter
{
    /// <summary>
    /// What a workbook has that an .ods file of ThixCalc cannot hold (German, for the question before saving), empty when nothing
    /// is lost.
    /// </summary>
    public static IReadOnlyList<string> Losses(Workbook workbook)
    {
        var losses = new List<string>();
        void Note(string text)
        {
            if (!losses.Contains(text))
            {
                losses.Add(text);
            }
        }

        if (workbook.Sheets.Any(sheet => sheet.Charts.Count > 0))
        {
            Note("Diagramme");
        }

        if (workbook.Sheets.Any(sheet => sheet.Tables.Count > 0))
        {
            Note("Tabellen (Name, Tabellenformat und Ergebniszeile – die Zellen und ihre Werte bleiben)");
        }

        if (workbook.Sheets.Any(sheet => sheet.ConditionalFormats.Any(format => format.Rules.Any(rule => !OdsConditionalFormats.CanWrite(rule)))))
        {
            Note("Bedingte Formatierungen mit Farbskalen, Datenbalken, Symbolsätzen oder Regeln, die OpenDocument nicht kennt");
        }

        if (workbook.Sheets.Any(sheet => sheet.Validations.Any(rule => OdsValidations.Write(rule, sheet) is null)))
        {
            Note("Datenüberprüfungen, die OpenDocument nicht ausdrücken kann");
        }

        foreach (var sheet in workbook.Sheets)
        {
            foreach (var entry in sheet.GetCells())
            {
                if (entry.Cell.Formula is { } formula && !entry.Cell.IsSpilled && OdsFormulas.Write(formula, sheet, entry.Address) is null)
                {
                    Note("Formeln, die OpenDocument nicht ausdrücken kann (es bleibt ihr Ergebnis)");
                    break;
                }
            }

            if (sheet.RichText.Count > 0)
            {
                Note("Teilweise formatierter Text in Zellen (einheitlich formatiert gespeichert)");
            }

            if (sheet.PreservedDrawingAnchors.Count > 0)
            {
                Note("Bilder und Formen");
            }

            if (sheet.Rows.Entries.Any(entry => entry.Info.OutlineLevel > 0) || sheet.Columns.Entries.Any(entry => entry.Info.OutlineLevel > 0))
            {
                Note("Gliederungen (gruppierte Zeilen und Spalten; ausgeblendete bleiben ausgeblendet)");
            }

            if (sheet.ProtectionXml is { } protection && (protection.Contains("password", StringComparison.OrdinalIgnoreCase) || protection.Contains("hashValue", StringComparison.OrdinalIgnoreCase)))
            {
                Note("Das Kennwort des Blattschutzes (das Blatt bleibt geschützt)");
            }

            if (sheet.AutoFilter is { } filter && filter.Columns.Any(column => !OdsFilters.CanWrite(column)))
            {
                Note("Dynamische Filter (z. B. „Über dem Durchschnitt“)");
            }
        }

        if (workbook.Styles.All.Any(style => style.Fill.Pattern is not (FillPattern.None or FillPattern.Solid) || style.Fill.GradientXml is not null))
        {
            Note("Musterfüllungen und Farbverläufe (als einfarbige Füllung gespeichert)");
        }

        if (workbook.HasMacros)
        {
            Note("Makros");
        }

        if (workbook.PreservedSheets.Count > 0)
        {
            Note("Diagrammblätter und Makroblätter");
        }

        if (workbook.OriginalPackage is not null)
        {
            foreach (var note in workbook.UnsupportedContent)
            {
                if (!note.StartsWith("Teilweise formatierter Text", StringComparison.Ordinal) && !note.StartsWith("Makros", StringComparison.Ordinal))
                {
                    Note(note);
                }
            }
        }

        return losses;
    }

    private sealed partial class Writer
    {
        private readonly Dictionary<string, (string Name, XElement Layout, XElement Master)> _masters = new(StringComparer.Ordinal);
        private readonly List<XElement> _conditionStyles = [];
        private readonly Dictionary<DifferentialFormat, string> _conditionStyleNames = [];

        // ------------------------------------------------------------ styles.xml

        public XDocument Styles()
        {
            var defaults = OdsCellStyles.Write(Defaults, Defaults, workbook.Theme, "", null, isDefault: true);
            var named = new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", "Default"), new XAttribute(StyleNs + "family", "table-cell"));
            if (DataStyle(Defaults.NumberFormat) is { } defaultData)
            {
                named.Add(new XAttribute(StyleNs + "data-style-name", defaultData));
            }

            if (_masters.Count == 0)
            {
                MasterPage(null);
            }

            var root = new XElement(OfficeNs + "document-styles", AllDeclarations(), new XAttribute(OfficeNs + "version", "1.3"),
                FontFaces(),
                new XElement(OfficeNs + "styles", _dataStyleElements,
                    defaults, named, _conditionStyles),
                new XElement(OfficeNs + "automatic-styles", _masters.Values.Select(entry => entry.Layout)),
                new XElement(OfficeNs + "master-styles", _masters.Values.Select(entry => entry.Master)));
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);
        }

        /// <summary>A named cell style for a conditional format's look (calcext:apply-style-name refers to it).</summary>
        private string NamedConditionStyle(DifferentialFormat format)
        {
            if (_conditionStyleNames.TryGetValue(format, out var name))
            {
                return name;
            }

            name = "ThixCalc_Bedingung_" + (_conditionStyleNames.Count + 1);
            _conditionStyleNames[format] = name;
            var style = new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", "table-cell"),
                new XAttribute(StyleNs + "parent-style-name", "Default"));
            if (format.NumberFormat is { } code && DataStyle(code) is { } data)
            {
                style.Add(new XAttribute(StyleNs + "data-style-name", data));
            }

            var cell = new XElement(StyleNs + "table-cell-properties");
            if (format.FillColor is { } fill)
            {
                cell.Add(new XAttribute(FoNs + "background-color", "#" + fill.Resolve(workbook.Theme, ThixOffice.Text.Drawing.RgbColor.White).ToHex()));
            }

            if (format.BorderStyle is { } border && border != BorderStyle.None)
            {
                var color = format.BorderColor is { } borderColor ? borderColor.Resolve(workbook.Theme, ThixOffice.Text.Drawing.RgbColor.Black).ToHex() : "000000";
                cell.Add(new XAttribute(FoNs + "border", $"0.74pt solid #{color}"));
            }

            if (cell.HasAttributes)
            {
                style.Add(cell);
            }

            var font = new ThixOffice.Core.Formats.Odf.OdfFont
            {
                Bold = format.Bold,
                Italic = format.Italic,
                Strikethrough = format.Strikethrough,
                Underline = format.Underline is null ? null : format.Underline == UnderlineKind.None ? "none" : "solid",
                UnderlineDouble = format.Underline is UnderlineKind.Double or UnderlineKind.DoubleAccounting,
                Color = format.FontColor is { } fontColor ? fontColor.Resolve(workbook.Theme, ThixOffice.Text.Drawing.RgbColor.Black).ToHex() : null,
            };
            if (!font.IsEmpty)
            {
                style.Add(font.Write());
            }

            _conditionStyles.Add(style);
            return name;
        }

        /// <summary>The master page of a sheet's page setup (equal setups share one); null = the default page.</summary>
        private string MasterPage(Worksheet? sheet)
        {
            var setup = sheet?.PageSetup ?? PageSetup.Default;
            var layout = PageLayout(setup);
            var master = new XElement(StyleNs + "master-page");
            if (setup.Header is { Length: > 0 } header)
            {
                master.Add(new XElement(StyleNs + "header", OdsHeaderFooter.Write(header)), new XElement(StyleNs + "header-left", new XAttribute(StyleNs + "display", "false")));
            }
            else
            {
                master.Add(new XElement(StyleNs + "header", new XAttribute(StyleNs + "display", "false")));
            }

            if (setup.Footer is { Length: > 0 } footer)
            {
                master.Add(new XElement(StyleNs + "footer", OdsHeaderFooter.Write(footer)), new XElement(StyleNs + "footer-left", new XAttribute(StyleNs + "display", "false")));
            }
            else
            {
                master.Add(new XElement(StyleNs + "footer", new XAttribute(StyleNs + "display", "false")));
            }

            var key = layout.ToString(SaveOptions.DisableFormatting) + master.ToString(SaveOptions.DisableFormatting);
            if (_masters.TryGetValue(key, out var existing))
            {
                return existing.Name;
            }

            var index = _masters.Count + 1;
            var name = index == 1 ? "Default" : "PageStyle_" + index.ToString(CultureInfo.InvariantCulture);
            var layoutName = "pm" + index.ToString(CultureInfo.InvariantCulture);
            layout.Add(new XAttribute(StyleNs + "name", layoutName));
            master.Add(new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "page-layout-name", layoutName));
            _masters[key] = (name, layout, master);
            return name;
        }

        private static XElement PageLayout(PageSetup setup)
        {
            var (width, height) = setup.PaperPoints;
            var properties = new XElement(StyleNs + "page-layout-properties",
                new XAttribute(FoNs + "page-width", Centimetres(width)),
                new XAttribute(FoNs + "page-height", Centimetres(height)),
                new XAttribute(StyleNs + "print-orientation", setup.Landscape ? "landscape" : "portrait"),
                new XAttribute(FoNs + "margin-left", Inches(setup.LeftMargin)),
                new XAttribute(FoNs + "margin-right", Inches(setup.RightMargin)));
            var hasHeader = setup.Header is { Length: > 0 };
            var hasFooter = setup.Footer is { Length: > 0 };
            properties.Add(new XAttribute(FoNs + "margin-top", Inches(hasHeader ? Math.Min(setup.HeaderMargin, setup.TopMargin) : setup.TopMargin)),
                new XAttribute(FoNs + "margin-bottom", Inches(hasFooter ? Math.Min(setup.FooterMargin, setup.BottomMargin) : setup.BottomMargin)));
            var print = new List<string> { "charts", "drawings", "objects", "zero-values" };
            if (setup.PrintGridLines)
            {
                print.Add("grid");
            }

            if (setup.PrintHeadings)
            {
                print.Add("headers");
            }

            properties.Add(new XAttribute(StyleNs + "print", string.Join(' ', print)),
                new XAttribute(StyleNs + "print-page-order", setup.OverThenDown ? "ltr" : "ttb"),
                new XAttribute(StyleNs + "first-page-number", setup.FirstPageNumber is { } first ? first.ToString(CultureInfo.InvariantCulture) : "continue"),
                new XAttribute(StyleNs + "table-centering", (setup.CenterHorizontally, setup.CenterVertically) switch
                {
                    (true, true) => "both",
                    (true, false) => "horizontal",
                    (false, true) => "vertical",
                    _ => "none",
                }),
                new XAttribute(StyleNs + "writing-mode", "lr-tb"));
            if (setup.FitToPage)
            {
                properties.Add(new XAttribute(StyleNs + "scale-to-X", setup.FitToWidth), new XAttribute(StyleNs + "scale-to-Y", setup.FitToHeight));
            }
            else
            {
                properties.Add(new XAttribute(StyleNs + "scale-to", setup.Scale.ToString(CultureInfo.InvariantCulture) + "%"));
            }

            var layout = new XElement(StyleNs + "page-layout", properties);
            if (hasHeader)
            {
                layout.Add(new XElement(StyleNs + "header-style", new XElement(StyleNs + "header-footer-properties",
                    new XAttribute(FoNs + "min-height", Inches(Math.Max(0, setup.TopMargin - setup.HeaderMargin))), new XAttribute(FoNs + "margin-bottom", "0in"))));
            }

            if (hasFooter)
            {
                layout.Add(new XElement(StyleNs + "footer-style", new XElement(StyleNs + "header-footer-properties",
                    new XAttribute(FoNs + "min-height", Inches(Math.Max(0, setup.BottomMargin - setup.FooterMargin))), new XAttribute(FoNs + "margin-top", "0in"))));
            }

            return layout;
        }

        // ------------------------------------------------------------ settings.xml

        public XDocument Settings()
        {
            var active = workbook.ActiveSheet ?? workbook.Sheets[0];
            var tables = new XElement(ConfigNs + "config-item-map-named", new XAttribute(ConfigNs + "name", "Tables"));
            foreach (var sheet in workbook.Sheets)
            {
                var view = sheet.View;
                var frozen = view.IsFrozen;
                tables.Add(new XElement(ConfigNs + "config-item-map-entry", new XAttribute(ConfigNs + "name", sheet.Name),
                    Item("CursorPositionX", "int", view.ActiveCell.Column),
                    Item("CursorPositionY", "int", view.ActiveCell.Row),
                    Item("HorizontalSplitMode", "short", view.FrozenColumns > 0 ? 2 : 0),
                    Item("VerticalSplitMode", "short", view.FrozenRows > 0 ? 2 : 0),
                    Item("HorizontalSplitPosition", "int", view.FrozenColumns),
                    Item("VerticalSplitPosition", "int", view.FrozenRows),
                    Item("ActiveSplitRange", "short", frozen ? 2 : 0),
                    Item("PositionLeft", "int", 0),
                    Item("PositionRight", "int", frozen ? view.TopLeft.Column : 0),
                    Item("PositionTop", "int", 0),
                    Item("PositionBottom", "int", view.TopLeft.Row),
                    Item("ZoomType", "short", 0),
                    Item("ZoomValue", "int", view.Zoom),
                    Item("PageViewZoomValue", "int", 60),
                    Item("ShowGrid", "boolean", view.ShowGridLines)));
                if (!frozen)
                {
                    // Without frozen panes the scrolled position is the left/top one.
                    var entry = (XElement)tables.LastNode!;
                    SetItem(entry, "PositionLeft", view.TopLeft.Column);
                    SetItem(entry, "PositionBottom", 0);
                    SetItem(entry, "PositionTop", view.TopLeft.Row);
                }
            }

            var view0 = new XElement(ConfigNs + "config-item-map-entry",
                Item("ViewId", "string", "view1"),
                tables,
                Item("ActiveTable", "string", active.Name),
                Item("ShowGrid", "boolean", active.View.ShowGridLines),
                Item("HasColumnRowHeaders", "boolean", active.View.ShowHeaders),
                Item("ShowZeroValues", "boolean", active.View.ShowZeros),
                Item("ShowFormulas", "boolean", active.View.ShowFormulas),
                Item("HasSheetTabs", "boolean", true));
            var root = new XElement(OfficeNs + "document-settings",
                new XAttribute(XNamespace.Xmlns + "office", OfficeNs), new XAttribute(XNamespace.Xmlns + "config", ConfigNs), new XAttribute(OfficeNs + "version", "1.3"),
                new XElement(OfficeNs + "settings",
                    new XElement(ConfigNs + "config-item-set", new XAttribute(ConfigNs + "name", "ooo:view-settings"),
                        new XElement(ConfigNs + "config-item-map-indexed", new XAttribute(ConfigNs + "name", "Views"), view0))));
            return new XDocument(new XDeclaration("1.0", "UTF-8", null), root);

            static XElement Item(string name, string type, object value) => new(ConfigNs + "config-item",
                new XAttribute(ConfigNs + "name", name), new XAttribute(ConfigNs + "type", type),
                value is bool flag ? Boolean(flag) : Convert.ToString(value, CultureInfo.InvariantCulture));

            static void SetItem(XElement entry, string name, int value) =>
                entry.Elements(ConfigNs + "config-item").First(item => (string?)item.Attribute(ConfigNs + "name") == name).Value = value.ToString(CultureInfo.InvariantCulture);
        }
    }
}
