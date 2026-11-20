using MatDesktop;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Model;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixCalc.Editor;

/// <summary>
/// ThixCalc's ribbon – Datei | Start | Einfügen | Seitenlayout | Formeln | Daten | Überprüfen | Ansicht, like the German Excel – as
/// a MatDesktop definition (data: a web version can draw the same one). Every item's id is a command id
/// (<see cref="CalcCommandIds"/>); labels, icons, shortcuts and tooltips come from the commands; what has no command is left out.
/// </summary>
public static class CalcRibbon
{
    /// <summary>The definition of one window (the colour palettes are its workbook's theme).</summary>
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var theme = (window.Document as CalcDocument)?.Workbook.Theme ?? ThemeColors.Office;
        var palette = GridPalette.Colors(theme);
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        void Launch(string id)
        {
            if (window.Commands.Find(id) is { } command)
            {
                _ = OfficeWindow.RunCommandAsync(window, command);
            }
        }

        definition.Add(HomeTab(palette, Launch), InsertTab(), PageLayoutTab(), FormulasTab(), DataTab(), ReviewTab(), ViewTab(), TableDesignTab(), PivotAnalyzeTab(), PivotDesignTab(), ChartDesignTab(), ChartFormatTab());
        return definition;
    }

    private static MatRibbonTab HomeTab(IReadOnlyList<string> palette, Action<string> launch) =>
        new MatRibbonTab("home", "Start").Add(
            new MatRibbonGroup("undo", "Rückgängig") { Icon = "undo" }.Add(
                Button(CommonCommandIds.Undo, MatRibbonSize.Small),
                Button(CommonCommandIds.Redo, MatRibbonSize.Small)),
            new MatRibbonGroup("clipboard", "Zwischenablage") { Icon = "paste", ShrinkOrder = 1 }.Add(
                new MatRibbonSplitButton(CalcCommandIds.Paste, "", menu:
                [
                    Button(CalcCommandIds.PasteValues), Button(CalcCommandIds.PasteFormulas), Button(CalcCommandIds.PasteFormats),
                    Button(CalcCommandIds.PasteValuesAndFormats), Button(CalcCommandIds.PasteNoBorders), Button(CalcCommandIds.PasteTranspose),
                    new MatRibbonSeparator("paste-separator"), Button(CalcCommandIds.PasteText), Button(CalcCommandIds.PasteSpecial),
                ]) { Size = MatRibbonSize.Large, MinSize = MatRibbonSize.Large },
                Button(CalcCommandIds.Cut),
                Button(CalcCommandIds.Copy),
                Toggle(CalcCommandIds.FormatPainter)),
            new MatRibbonGroup("font", "Schriftart") { Icon = "type", ShrinkOrder = 5, Launcher = () => launch(CalcCommandIds.FormatCells) }.Add(
                new MatRibbonStack("font-rows",
                    new MatRibbonRow("font-row-1",
                        ComboBox(CalcCommandIds.FontName, width: 136, isEditable: true),
                        ComboBox(CalcCommandIds.FontSize, width: 60, isEditable: true),
                        Button(CalcCommandIds.GrowFont, MatRibbonSize.Small),
                        Button(CalcCommandIds.ShrinkFont, MatRibbonSize.Small)),
                    new MatRibbonRow("font-row-2",
                        Toggle(CalcCommandIds.Bold, size: MatRibbonSize.Small),
                        Toggle(CalcCommandIds.Italic, size: MatRibbonSize.Small),
                        Toggle(CalcCommandIds.Underline, size: MatRibbonSize.Small),
                        new MatRibbonSplitButton(CalcCommandIds.Borders, "", menu: [.. CalcCommands.BorderChoices.Select(choice => Button($"{CalcCommandIds.Borders}.{choice.Id}"))]) { Size = MatRibbonSize.Small },
                        new MatRibbonSeparator("font-separator"),
                        ColorPicker(CalcCommandIds.FillColor, palette, "Keine Füllung"),
                        ColorPicker(CalcCommandIds.FontColor, palette)))),
            new MatRibbonGroup("alignment", "Ausrichtung") { Icon = "align-center", ShrinkOrder = 4, Launcher = () => launch(CalcCommandIds.FormatCells) }.Add(
                new MatRibbonStack("alignment-rows",
                    new MatRibbonRow("alignment-row-1",
                        Toggle(CalcCommandIds.AlignTop, "vertical", MatRibbonSize.Small),
                        Toggle(CalcCommandIds.AlignMiddle, "vertical", MatRibbonSize.Small),
                        Toggle(CalcCommandIds.AlignBottom, "vertical", MatRibbonSize.Small),
                        Toggle(CalcCommandIds.WrapText, size: MatRibbonSize.Small)),
                    new MatRibbonRow("alignment-row-2",
                        Toggle(CalcCommandIds.AlignLeft, "horizontal", MatRibbonSize.Small),
                        Toggle(CalcCommandIds.AlignCenter, "horizontal", MatRibbonSize.Small),
                        Toggle(CalcCommandIds.AlignRight, "horizontal", MatRibbonSize.Small),
                        Button(CalcCommandIds.IndentDecrease, MatRibbonSize.Small),
                        Button(CalcCommandIds.IndentIncrease, MatRibbonSize.Small),
                        new MatRibbonSplitButton(CalcCommandIds.Merge, "", menu:
                        [
                            Button(CalcCommandIds.MergeCenter), Button(CalcCommandIds.MergeAcross), Button(CalcCommandIds.MergeCells), Button(CalcCommandIds.Unmerge),
                        ]) { Size = MatRibbonSize.Small }))),
            new MatRibbonGroup("number", "Zahl") { Icon = "percent", ShrinkOrder = 3, Launcher = () => launch(CalcCommandIds.FormatCells) }.Add(
                new MatRibbonStack("number-rows",
                    ComboBox(CalcCommandIds.NumberFormat, width: 150),
                    new MatRibbonRow("number-row-2",
                        Button(CalcCommandIds.Accounting, MatRibbonSize.Small),
                        Button(CalcCommandIds.Percent, MatRibbonSize.Small),
                        Button(CalcCommandIds.Thousands, MatRibbonSize.Small),
                        Button(CalcCommandIds.DecimalsMore, MatRibbonSize.Small),
                        Button(CalcCommandIds.DecimalsLess, MatRibbonSize.Small)))),
            new MatRibbonGroup("styles", "Formatvorlagen") { Icon = "template", ShrinkOrder = 6 }.Add(
                new MatRibbonMenuButton(CalcCommandIds.ConditionalFormatting, "", null,
                [
                    .. CalcStyleCommands.Rules.Take(6).Select(rule => Button(CalcStyleCommands.RuleId(rule.Id))),
                    new MatRibbonSeparator("cf-separator-1"),
                    .. CalcStyleCommands.Rules.Skip(6).Select(rule => Button(CalcStyleCommands.RuleId(rule.Id))),
                    new MatRibbonSeparator("cf-separator-2"),
                    .. GridPalette.DataBars.Select(bar => Button(CalcStyleCommands.RuleId("databar." + bar.Id))),
                    new MatRibbonSeparator("cf-separator-3"),
                    .. GridPalette.ColorScales.Select(scale => Button(CalcStyleCommands.RuleId("scale." + scale.Id))),
                    new MatRibbonSeparator("cf-separator-4"),
                    Button(CalcStyleCommands.RuleId("clearSelection")),
                    Button(CalcStyleCommands.RuleId("clearSheet")),
                    Button(CalcStyleCommands.RuleId("manage")),
                ]) { Size = MatRibbonSize.Large },
                TableStyleMenu(CalcCommandIds.FormatAsTable),
                CellStylesGallery()),
            new MatRibbonGroup("cells", "Zellen") { Icon = "row-insert", ShrinkOrder = 2 }.Add(
                new MatRibbonSplitButton(CalcCommandIds.Insert, "", menu:
                [
                    Button(CalcCommandIds.InsertCells), Button(CalcCommandIds.InsertRows), Button(CalcCommandIds.InsertColumns), Button(CalcCommandIds.InsertSheet),
                ]) { Size = MatRibbonSize.Large },
                new MatRibbonSplitButton(CalcCommandIds.Delete, "", menu:
                [
                    Button(CalcCommandIds.DeleteCells), Button(CalcCommandIds.DeleteRows), Button(CalcCommandIds.DeleteColumns), Button(CalcCommandIds.DeleteSheet),
                ]) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.Format, "", null,
                [
                    Button(CalcCommandIds.RowHeight), Button(CalcCommandIds.AutoFitRows), Button(CalcCommandIds.ColumnWidth), Button(CalcCommandIds.AutoFitColumns),
                    new MatRibbonSeparator("format-separator-1"),
                    Button(CalcCommandIds.HideRows), Button(CalcCommandIds.HideColumns), Button(CalcCommandIds.UnhideRows), Button(CalcCommandIds.UnhideColumns),
                    new MatRibbonSeparator("format-separator-2"),
                    Button(CalcCommandIds.RenameSheet), Toggle(CalcCommandIds.LockCell),
                    new MatRibbonSeparator("format-separator-3"),
                    Button(CalcCommandIds.FormatCells),
                ]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("editing", "Bearbeiten") { Icon = "search", ShrinkOrder = 7 }.Add(
                new MatRibbonStack("editing-left",
                    new MatRibbonSplitButton(CalcCommandIds.AutoSum, "", menu: [.. CalcCommands.AutoSumFunctions.Select(entry => Button($"{CalcCommandIds.AutoSum}.{entry.Function.ToLowerInvariant()}"))]),
                    new MatRibbonMenuButton(CalcCommandIds.Fill, "", null, Button(CalcCommandIds.FillDown), Button(CalcCommandIds.FillRight), Button(CalcCommandIds.FillSeries)),
                    new MatRibbonMenuButton(CalcCommandIds.Clear, "", null,
                        Button(CalcCommandIds.ClearAll), Button(CalcCommandIds.ClearFormats), Button(CalcCommandIds.ClearContents),
                        Button(CalcCommandIds.ClearComments), Button(CalcCommandIds.ClearLinks))),
                new MatRibbonMenuButton(CalcCommandIds.SortFilter, "", null,
                    Button(CalcCommandIds.SortAscending), Button(CalcCommandIds.SortDescending), Button(CalcCommandIds.SortCustom),
                    new MatRibbonSeparator("sort-separator"),
                    Toggle(CalcCommandIds.Filter), Button(CalcCommandIds.FilterClear)) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.FindSelect, "", null,
                    Button(CalcCommandIds.Find), Button(CalcCommandIds.Replace), Button(CalcCommandIds.GoTo), Button(CalcCommandIds.SelectSpecial), Button(CalcCommandIds.SelectVisible), Button(CalcCommandIds.SelectAll)) { Size = MatRibbonSize.Large }));

    /// <summary>Zellenformatvorlagen: the styles with a sample of their look, a few in the ribbon, all in its menu.</summary>
    private static MatRibbonGallery CellStylesGallery()
    {
        var gallery = Gallery(CalcCommandIds.CellStyles, GridPalette.CellStyles.Select(style => new MatRibbonGalleryEntry(style.Id, style.Label) { Sample = style.Label, SampleSize = 11 }));
        gallery.InlineCount = 3;
        gallery.EntryWidth = 82;
        return gallery;
    }

    private static MatRibbonTab InsertTab() =>
        new MatRibbonTab("insert", "Einfügen").Add(
            new MatRibbonGroup("tables", "Tabellen") { Icon = "table" }.Add(
                Button(CalcCommandIds.InsertPivot, MatRibbonSize.Large),
                Button(CalcCommandIds.InsertTable, MatRibbonSize.Large)),
            new MatRibbonGroup("charts", "Diagramme") { Icon = "chart-column" }.Add(
                new MatRibbonStack("charts-left", ChartFamily(ThixOffice.Charts.ChartFamily.Column), ChartFamily(ThixOffice.Charts.ChartFamily.Bar), ChartFamily(ThixOffice.Charts.ChartFamily.Line)),
                new MatRibbonStack("charts-middle", ChartFamily(ThixOffice.Charts.ChartFamily.Pie), ChartFamily(ThixOffice.Charts.ChartFamily.Area), ChartFamily(ThixOffice.Charts.ChartFamily.Scatter)),
                new MatRibbonStack("charts-right", ChartFamily(ThixOffice.Charts.ChartFamily.Combo))),
            new MatRibbonGroup("links", "Links") { Icon = "link" }.Add(
                Button(CalcCommandIds.Hyperlink, MatRibbonSize.Large)),
            new MatRibbonGroup("comments", "Kommentare") { Icon = "comment" }.Add(
                Button(CalcCommandIds.Comment, MatRibbonSize.Large)));

    /// <summary>A family of the Diagramme group: a menu of its chart types.</summary>
    private static MatRibbonMenuButton ChartFamily(ThixOffice.Charts.ChartFamily family) =>
        new(CalcCommandIds.ChartFamilyPrefix + family.ToString().ToLowerInvariant(), "", null,
            [.. ThixOffice.Charts.ChartTemplates.All.Where(template => template.Family == family).Select(template => Button(CalcCommandIds.InsertChartPrefix + template.Id))]) { Size = MatRibbonSize.Medium };

    /// <summary>The table styles as a gallery (labels: Hell 1, Mittel 2 …).</summary>
    private static MatRibbonGallery TableStyleMenu(string commandId)
    {
        var gallery = Gallery(commandId, TableStyles.Gallery.Select(style => new MatRibbonGalleryEntry(style.Id, style.Label) { Icon = "table" }));
        gallery.InlineCount = 3;
        gallery.EntryWidth = 74;
        return gallery;
    }

    /// <summary>Tabellenentwurf (contextual, while the active cell is in a table).</summary>
    private static MatRibbonTab TableDesignTab() =>
        new MatRibbonTab("tableDesign", "Tabellenentwurf") { Context = CalcTableCommands.Context }.Add(
            new MatRibbonGroup("table-properties", "Eigenschaften") { Icon = "tag" }.Add(
                new MatRibbonStack("table-properties-rows",
                    ComboBox(CalcCommandIds.TableName, width: 130, isEditable: true),
                    Button(CalcCommandIds.TableResize, MatRibbonSize.Medium))),
            new MatRibbonGroup("table-tools", "Tools") { Icon = "grid" }.Add(
                Button(CalcCommandIds.TableConvert, MatRibbonSize.Large)),
            new MatRibbonGroup("table-options", "Tabellenformatoptionen") { Icon = "check-square" }.Add(
                new MatRibbonStack("table-options-1", Toggle(CalcCommandIds.TableHeaderRow), Toggle(CalcCommandIds.TableTotalRow), Toggle(CalcCommandIds.TableBandedRows)),
                new MatRibbonStack("table-options-2", Toggle(CalcCommandIds.TableFirstColumn), Toggle(CalcCommandIds.TableLastColumn), Toggle(CalcCommandIds.TableBandedColumns)),
                new MatRibbonStack("table-options-3", Toggle(CalcCommandIds.TableFilterButton))),
            new MatRibbonGroup("table-styles", "Tabellenformatvorlagen") { Icon = "template" }.Add(
                TableStyleMenu(CalcCommandIds.TableStyles)));

    /// <summary>PivotTable-Analyse (contextual, while the active cell is in a PivotTable).</summary>
    private static MatRibbonTab PivotAnalyzeTab() =>
        new MatRibbonTab(CalcEditor.PivotAnalyzeTab, "PivotTable-Analyse") { Context = CalcPivotCommands.Context }.Add(
            new MatRibbonGroup("pivot-table", "PivotTable") { Icon = "table" }.Add(
                new MatRibbonStack("pivot-name", ComboBox(CalcCommandIds.PivotName, width: 130, isEditable: true))),
            new MatRibbonGroup("pivot-field", "Aktives Feld") { Icon = "settings" }.Add(
                new MatRibbonStack("pivot-field-rows", Button(CalcCommandIds.PivotFieldSettings, MatRibbonSize.Medium), Button(CalcCommandIds.PivotValueSettings, MatRibbonSize.Medium))),
            new MatRibbonGroup("pivot-group", "Gruppieren") { Icon = "layers" }.Add(
                new MatRibbonStack("pivot-group-rows", Button(CalcCommandIds.PivotGroup, MatRibbonSize.Medium), Button(CalcCommandIds.PivotUngroup, MatRibbonSize.Medium))),
            new MatRibbonGroup("pivot-sort", "Sortieren") { Icon = "sort" }.Add(
                new MatRibbonStack("pivot-sort-rows", Button(CalcCommandIds.PivotSortAscending, MatRibbonSize.Small), Button(CalcCommandIds.PivotSortDescending, MatRibbonSize.Small))),
            new MatRibbonGroup("pivot-data", "Daten") { Icon = "refresh" }.Add(
                new MatRibbonSplitButton(CalcCommandIds.PivotRefresh, "", menu: [Button(CalcCommandIds.PivotRefreshAll)]) { Size = MatRibbonSize.Large },
                Button(CalcCommandIds.PivotChangeSource, MatRibbonSize.Large)),
            new MatRibbonGroup("pivot-actions", "Aktionen") { Icon = "external" }.Add(
                new MatRibbonStack("pivot-actions-rows", Button(CalcCommandIds.PivotDelete, MatRibbonSize.Medium), Button(CalcCommandIds.PivotMove, MatRibbonSize.Medium), Button(CalcCommandIds.PivotDetails, MatRibbonSize.Medium))),
            new MatRibbonGroup("pivot-show", "Anzeigen") { Icon = "sidebar" }.Add(
                Toggle(CalcCommandIds.PivotFieldList, size: MatRibbonSize.Large)));

    /// <summary>Entwurf (contextual, while the active cell is in a PivotTable).</summary>
    private static MatRibbonTab PivotDesignTab() =>
        new MatRibbonTab(CalcEditor.PivotDesignTab, "Entwurf") { Context = CalcPivotCommands.Context }.Add(
            new MatRibbonGroup("pivot-layout", "Layout") { Icon = "grid" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.PivotSubtotals, "", null,
                    Toggle($"{CalcCommandIds.PivotSubtotals}.none", "pivot-subtotals"), Toggle($"{CalcCommandIds.PivotSubtotals}.bottom", "pivot-subtotals"), Toggle($"{CalcCommandIds.PivotSubtotals}.top", "pivot-subtotals")) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.PivotGrandTotals, "", null,
                    Toggle($"{CalcCommandIds.PivotGrandTotals}.off", "pivot-grand"), Toggle($"{CalcCommandIds.PivotGrandTotals}.both", "pivot-grand"),
                    Toggle($"{CalcCommandIds.PivotGrandTotals}.rows", "pivot-grand"), Toggle($"{CalcCommandIds.PivotGrandTotals}.columns", "pivot-grand")) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.PivotLayout, "", null,
                    Toggle($"{CalcCommandIds.PivotLayout}.compact", "pivot-layout"), Toggle($"{CalcCommandIds.PivotLayout}.outline", "pivot-layout"), Toggle($"{CalcCommandIds.PivotLayout}.tabular", "pivot-layout"),
                    new MatRibbonSeparator("pivot-layout-separator"),
                    Toggle($"{CalcCommandIds.PivotLayout}.repeat", "pivot-repeat"), Toggle($"{CalcCommandIds.PivotLayout}.norepeat", "pivot-repeat")) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("pivot-options", "Optionen für PivotTable-Formate") { Icon = "check-square" }.Add(
                new MatRibbonStack("pivot-options-1", Toggle(CalcCommandIds.PivotRowHeaders), Toggle(CalcCommandIds.PivotColumnHeaders)),
                new MatRibbonStack("pivot-options-2", Toggle(CalcCommandIds.PivotBandedRows), Toggle(CalcCommandIds.PivotBandedColumns))),
            new MatRibbonGroup("pivot-styles", "PivotTable-Formatvorlagen") { Icon = "template" }.Add(
                PivotStyleGallery()));

    private static MatRibbonGallery PivotStyleGallery()
    {
        var gallery = Gallery(CalcCommandIds.PivotStyles, Core.Model.Pivot.PivotStyles.Gallery.Select(style => new MatRibbonGalleryEntry(style.Id, style.Label) { Icon = "table" }));
        gallery.InlineCount = 4;
        gallery.EntryWidth = 74;
        return gallery;
    }

    /// <summary>Diagrammentwurf (contextual, while a chart is selected).</summary>
    private static MatRibbonTab ChartDesignTab() =>
        new MatRibbonTab(CalcEditor.ChartDesignTab, "Diagrammentwurf") { Context = CalcChartCommands.Context }.Add(
            new MatRibbonGroup("chart-layouts", "Diagrammlayouts") { Icon = "plus" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.ChartAddElement, "", null,
                [
                    new MatRibbonMenuButton(CalcCommandIds.ChartAxes, "", null, Toggle(CalcCommandIds.ChartAxisHorizontal), Toggle(CalcCommandIds.ChartAxisVertical)),
                    new MatRibbonMenuButton(CalcCommandIds.ChartAxisTitles, "", null, Toggle(CalcCommandIds.ChartAxisTitleHorizontal), Toggle(CalcCommandIds.ChartAxisTitleVertical)),
                    Toggle(CalcCommandIds.ChartTitle),
                    Toggle(CalcCommandIds.ChartDataLabels),
                    new MatRibbonMenuButton(CalcCommandIds.ChartGridlines, "", null, Toggle(CalcCommandIds.ChartGridlinesMajor), Toggle(CalcCommandIds.ChartGridlinesVertical)),
                    new MatRibbonMenuButton(CalcCommandIds.ChartLegend, "", null, [.. CalcChartCommands.LegendChoices.Select(choice => Toggle(CalcCommandIds.ChartLegendPrefix + choice.Id, "chartLegend"))]),
                ]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("chart-styles", "Diagrammformatvorlagen") { Icon = "palette" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.ChartColors, "", null,
                    [.. ThixOffice.Charts.ChartColors.Sets.Select(set => Toggle(CalcCommandIds.ChartColorPrefix + set.Id.ToString(System.Globalization.CultureInfo.InvariantCulture), "chartColors"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("chart-data", "Daten") { Icon = "table" }.Add(
                Button(CalcCommandIds.ChartSwitchRowColumn, MatRibbonSize.Large),
                Button(CalcCommandIds.ChartSelectData, MatRibbonSize.Large)),
            new MatRibbonGroup("chart-type", "Typ") { Icon = "chart-column" }.Add(
                Button(CalcCommandIds.ChartChangeType, MatRibbonSize.Large)));

    /// <summary>Format (contextual, while a chart is selected): order and size.</summary>
    private static MatRibbonTab ChartFormatTab() =>
        new MatRibbonTab("chartFormat", "Format") { Context = CalcChartCommands.Context }.Add(
            new MatRibbonGroup("chart-arrange", "Anordnen") { Icon = "layers" }.Add(
                new MatRibbonStack("chart-arrange-forward", Button(CalcCommandIds.ChartBringForward, MatRibbonSize.Medium), Button(CalcCommandIds.ChartBringToFront, MatRibbonSize.Medium)),
                new MatRibbonStack("chart-arrange-backward", Button(CalcCommandIds.ChartSendBackward, MatRibbonSize.Medium), Button(CalcCommandIds.ChartSendToBack, MatRibbonSize.Medium))),
            new MatRibbonGroup("chart-size", "Größe") { Icon = "crop" }.Add(
                new MatRibbonStack("chart-size-fields",
                    Labelled(Number(CalcCommandIds.ChartHeight, 0.5, 500, 0.1, "cm", 2), "Höhe"),
                    Labelled(Number(CalcCommandIds.ChartWidth, 0.5, 500, 0.1, "cm", 2), "Breite"))),
            new MatRibbonGroup("chart-delete", "Löschen") { Icon = "trash" }.Add(
                Button(CalcCommandIds.ChartDelete, MatRibbonSize.Large)));

    private static MatRibbonNumber Labelled(MatRibbonNumber number, string label)
    {
        number.Label = label;
        number.Icon = "";
        number.Width = 92;
        return number;
    }

    private static MatRibbonTab PageLayoutTab() =>
        new MatRibbonTab("pageLayout", "Seitenlayout").Add(
            new MatRibbonGroup("page-setup", "Seite einrichten") { Icon = "margins" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.Margins, "", null,
                    [.. CalcPageCommands.MarginPresets.Select(preset => Toggle($"{CalcCommandIds.Margins}.{preset.Id}", "margins"))]) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.Orientation, "", null,
                    Toggle(CalcCommandIds.Portrait, "orientation"),
                    Toggle(CalcCommandIds.Landscape, "orientation")) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.PaperSize, "", null,
                    [.. CalcPageCommands.PaperSizes.Select(size => Toggle($"{CalcCommandIds.PaperSize}.{size.Id}", "paper"))]) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.PrintArea, "", null,
                    Button(CalcCommandIds.SetPrintArea), Button(CalcCommandIds.ClearPrintArea)) { Size = MatRibbonSize.Large },
                Button(CalcCommandIds.PrintTitles, MatRibbonSize.Large),
                Button(CalcCommandIds.PageSetup)),
            new MatRibbonGroup("scale", "An Format anpassen") { Icon = "zoom-out" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.FitToPage, "", null,
                    [.. CalcPageCommands.FitChoices.Select(fit => Toggle($"{CalcCommandIds.FitToPage}.{fit.Id}", "fit"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("sheet-options", "Blattoptionen") { Icon = "grid" }.Add(
                Toggle(CalcCommandIds.PrintGridLines),
                Toggle(CalcCommandIds.PrintHeadings)));

    private static MatRibbonTab FormulasTab()
    {
        // Like Excel: Funktion einfügen, then the main categories, the others under Weitere Funktionen; every menu lists ALL
        // functions of its category and ends with Funktion einfügen ….
        var library = new MatRibbonGroup("function-library", "Funktionsbibliothek") { Icon = "sum" };
        library.Add(Button(CalcCommandIds.InsertFunction, MatRibbonSize.Large));
        var menus = CalcDataCommands.MenuFunctionsOf(Core.Globalization.CalcLocale.German).Where(entry => entry.Functions.Count > 0).ToList();
        MatRibbonMenuButton Menu((FunctionCategory Category, IReadOnlyList<FunctionInfo> Functions) entry) =>
            new(CalcDataCommands.CategoryId(entry.Category), "", null,
                [.. entry.Functions.Select(info => Button(CalcDataCommands.FunctionId(info.Canonical))), new MatRibbonSeparator(),
                    Button(CalcDataCommands.CategoryId(entry.Category) + ".insert")]) { Size = MatRibbonSize.Medium };
        FunctionCategory[] main = [FunctionCategory.Financial, FunctionCategory.Logical, FunctionCategory.Text, FunctionCategory.DateTime, FunctionCategory.Lookup, FunctionCategory.Math];
        foreach (var category in main)
        {
            if (menus.FirstOrDefault(entry => entry.Category == category) is { Functions: not null } entry)
            {
                library.Add(Menu(entry));
            }
        }

        library.Add(new MatRibbonMenuButton(CalcCommandIds.MoreFunctions, "", null,
            [.. menus.Where(entry => !main.Contains(entry.Category)).Select(Menu)]) { Size = MatRibbonSize.Medium });

        return new MatRibbonTab("formulas", "Formeln").Add(
            library,
            new MatRibbonGroup("names", "Definierte Namen") { Icon = "tag" }.Add(
                Button(CalcCommandIds.NameManager, MatRibbonSize.Large),
                new MatRibbonStack("names-rows", Button(CalcCommandIds.DefineName), Button(CalcCommandIds.UseInFormula), Button(CalcCommandIds.CreateNames))),
            new MatRibbonGroup("auditing", "Formelüberwachung") { Icon = "code" }.Add(
                new MatRibbonStack("auditing-trace", Button(CalcCommandIds.TracePrecedents), Button(CalcCommandIds.TraceDependents), Button(CalcCommandIds.RemoveArrows)),
                new MatRibbonStack("auditing-check", Toggle(CalcCommandIds.ShowFormulas), Button(CalcCommandIds.ErrorChecking), Button(CalcCommandIds.EvaluateFormula))),
            new MatRibbonGroup("calculation", "Berechnung") { Icon = "refresh" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.CalculationOptions, "", null,
                    Toggle(CalcCommandIds.CalculationAutomatic, "calculation"),
                    Toggle(CalcCommandIds.CalculationManual, "calculation")) { Size = MatRibbonSize.Large },
                Button(CalcCommandIds.CalculateNow),
                Button(CalcCommandIds.CalculateSheet)));
    }

    private static MatRibbonTab DataTab() =>
        new MatRibbonTab("data", "Daten").Add(
            new MatRibbonGroup("sort-filter", "Sortieren und Filtern") { Icon = "sort" }.Add(
                new MatRibbonStack("sort-quick",
                    Button(CalcCommandIds.DataSortAscending, MatRibbonSize.Small),
                    Button(CalcCommandIds.DataSortDescending, MatRibbonSize.Small)),
                Button(CalcCommandIds.DataSort, MatRibbonSize.Large),
                Toggle(CalcCommandIds.DataFilter, size: MatRibbonSize.Large),
                Button(CalcCommandIds.DataFilterClear)),
            new MatRibbonGroup("data-tools", "Datentools") { Icon = "wrench" }.Add(
                Button(CalcCommandIds.TextToColumns, MatRibbonSize.Large),
                Button(CalcCommandIds.RemoveDuplicates),
                Button(CalcCommandIds.Validation)),
            new MatRibbonGroup("forecast", "Prognose") { Icon = "help-circle" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.WhatIf, "", null, Button(CalcCommandIds.GoalSeek)) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("outline", "Gliederung") { Icon = "layers" }.Add(
                new MatRibbonStack("outline-group", Button(CalcCommandIds.Group), Button(CalcCommandIds.Ungroup), Button(CalcCommandIds.Subtotals)),
                new MatRibbonStack("outline-detail", Button(CalcCommandIds.ShowDetail, MatRibbonSize.Small), Button(CalcCommandIds.HideDetail, MatRibbonSize.Small))));

    private static MatRibbonTab ReviewTab() =>
        new MatRibbonTab("review", "Überprüfen").Add(
            new MatRibbonGroup("notes", "Notizen") { Icon = "comment" }.Add(
                Button(CalcCommandIds.NewComment, MatRibbonSize.Large),
                Button(CalcCommandIds.DeleteCommentReview)),
            new MatRibbonGroup("protect", "Schützen") { Icon = "lock" }.Add(
                Toggle(CalcCommandIds.ProtectSheet, size: MatRibbonSize.Large),
                Toggle(CalcCommandIds.ProtectWorkbook, size: MatRibbonSize.Large)));

    private static MatRibbonTab ViewTab() =>
        new MatRibbonTab("view", "Ansicht").Add(
            new MatRibbonGroup("show", "Anzeigen") { Icon = "eye" }.Add(
                Toggle(CalcCommandIds.ShowGridLines),
                Toggle(CalcCommandIds.ShowHeadings),
                Toggle(CalcCommandIds.ShowFormulaBar)),
            new MatRibbonGroup("zoom", "Zoom") { Icon = "zoom-in" }.Add(
                Button(CalcCommandIds.ZoomDialog, MatRibbonSize.Large),
                Button(CommonCommandIds.Zoom100, MatRibbonSize.Large),
                Button(CalcCommandIds.ZoomSelection, MatRibbonSize.Large)),
            new MatRibbonGroup("window", "Fenster") { Icon = "freeze" }.Add(
                new MatRibbonMenuButton(CalcCommandIds.Freeze, "", null,
                    Button(CalcCommandIds.FreezePanes), Button(CalcCommandIds.FreezeTopRow), Button(CalcCommandIds.FreezeFirstColumn),
                    new MatRibbonSeparator("freeze-separator"), Button(CalcCommandIds.Unfreeze)) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("ribbon", "Menüband") { Icon = "chevron-up" }.Add(
                Toggle(CommonCommandIds.ToggleRibbon)));
}
