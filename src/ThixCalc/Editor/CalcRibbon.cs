using MatDesktop;
using ThixCalc.Core.Documents;
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

        definition.Add(HomeTab(palette, Launch), InsertTab(), PageLayoutTab(), FormulasTab(), DataTab(), ReviewTab(), ViewTab(), ChartDesignTab(), ChartFormatTab());
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
                    new MatRibbonSeparator("paste-separator"), Button(CalcCommandIds.PasteText),
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
                    Button(CalcCommandIds.RenameSheet),
                    new MatRibbonSeparator("format-separator-3"),
                    Button(CalcCommandIds.FormatCells),
                ]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("editing", "Bearbeiten") { Icon = "search", ShrinkOrder = 7 }.Add(
                new MatRibbonStack("editing-left",
                    new MatRibbonSplitButton(CalcCommandIds.AutoSum, "", menu: [.. CalcCommands.AutoSumFunctions.Select(entry => Button($"{CalcCommandIds.AutoSum}.{entry.Function.ToLowerInvariant()}"))]),
                    new MatRibbonMenuButton(CalcCommandIds.Fill, "", null, Button(CalcCommandIds.FillDown), Button(CalcCommandIds.FillRight)),
                    new MatRibbonMenuButton(CalcCommandIds.Clear, "", null,
                        Button(CalcCommandIds.ClearAll), Button(CalcCommandIds.ClearFormats), Button(CalcCommandIds.ClearContents),
                        Button(CalcCommandIds.ClearComments), Button(CalcCommandIds.ClearLinks))),
                new MatRibbonMenuButton(CalcCommandIds.SortFilter, "", null,
                    Button(CalcCommandIds.SortAscending), Button(CalcCommandIds.SortDescending), Button(CalcCommandIds.SortCustom),
                    new MatRibbonSeparator("sort-separator"),
                    Toggle(CalcCommandIds.Filter), Button(CalcCommandIds.FilterClear)) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(CalcCommandIds.FindSelect, "", null,
                    Button(CalcCommandIds.Find), Button(CalcCommandIds.Replace), Button(CalcCommandIds.GoTo), Button(CalcCommandIds.SelectAll)) { Size = MatRibbonSize.Large }));

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
        var library = new MatRibbonGroup("function-library", "Funktionsbibliothek") { Icon = "sum" };
        library.Add(Button(CalcCommandIds.InsertFunction, MatRibbonSize.Large));
        foreach (var (category, functions) in CalcDataCommands.MenuFunctionsOf(Core.Globalization.CalcLocale.German))
        {
            if (functions.Count == 0)
            {
                continue;
            }

            library.Add(new MatRibbonMenuButton($"{CalcCommandIds.FunctionLibrary}.{category.ToString().ToLowerInvariant()}", "", null,
                [.. functions.Select(info => Button(CalcDataCommands.FunctionId(info.Canonical)))]) { Size = MatRibbonSize.Medium });
        }

        return new MatRibbonTab("formulas", "Formeln").Add(
            library,
            new MatRibbonGroup("names", "Definierte Namen") { Icon = "tag" }.Add(
                Button(CalcCommandIds.NameManager, MatRibbonSize.Large),
                Button(CalcCommandIds.DefineName)),
            new MatRibbonGroup("auditing", "Formelüberwachung") { Icon = "code" }.Add(
                Toggle(CalcCommandIds.ShowFormulas, size: MatRibbonSize.Large)),
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
            new MatRibbonGroup("outline", "Gliederung") { Icon = "layers" }.Add(
                Button(CalcCommandIds.Group),
                Button(CalcCommandIds.Ungroup)));

    private static MatRibbonTab ReviewTab() =>
        new MatRibbonTab("review", "Überprüfen").Add(
            new MatRibbonGroup("notes", "Notizen") { Icon = "comment" }.Add(
                Button(CalcCommandIds.NewComment, MatRibbonSize.Large),
                Button(CalcCommandIds.DeleteCommentReview)));

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
