namespace ThixCalc.Editor;

/// <summary>
/// The ids of ThixCalc's commands. The ribbon names them; a command the window does not have is left out (no dead buttons).
/// Value commands (<see cref="ThixOffice.Core.Commands.OfficeValueCommand"/>) note their value. Menu entries are commands of
/// their own ("calc.borders.bottom" …).
/// </summary>
public static class CalcCommandIds
{
    // Start – Zwischenablage
    public const string Paste = "edit.paste";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string PasteValues = "calc.paste.values";
    public const string PasteFormulas = "calc.paste.formulas";
    public const string PasteFormats = "calc.paste.formats";
    public const string PasteValuesAndFormats = "calc.paste.valuesNumberFormats";
    public const string PasteNoBorders = "calc.paste.noBorders";
    public const string PasteTranspose = "calc.paste.transpose";
    public const string PasteText = "calc.paste.text";
    public const string FormatPainter = "calc.formatPainter";

    // Start – Schriftart
    public const string FontName = "calc.fontName"; // value: the family
    public const string FontSize = "calc.fontSize"; // value: pt as the German ribbon shows it ("10,5")
    public const string GrowFont = "calc.growFont";
    public const string ShrinkFont = "calc.shrinkFont";
    public const string Bold = "calc.bold";
    public const string Italic = "calc.italic";
    public const string Underline = "calc.underline";
    public const string Strikethrough = "calc.strikethrough";
    public const string Borders = "calc.borders"; // the menu's entries are calc.borders.<preset>
    public const string FillColor = "calc.fillColor"; // value: #rrggbb or null (Keine Füllung)
    public const string FontColor = "calc.fontColor"; // value: #rrggbb or null (Automatisch)

    // Start – Ausrichtung
    public const string AlignTop = "calc.alignTop";
    public const string AlignMiddle = "calc.alignMiddle";
    public const string AlignBottom = "calc.alignBottom";
    public const string AlignLeft = "calc.alignLeft";
    public const string AlignCenter = "calc.alignCenter";
    public const string AlignRight = "calc.alignRight";
    public const string WrapText = "calc.wrapText";
    public const string Merge = "calc.merge"; // menu: calc.merge.center / .across / .cells / .unmerge
    public const string MergeCenter = "calc.merge.center";
    public const string MergeAcross = "calc.merge.across";
    public const string MergeCells = "calc.merge.cells";
    public const string Unmerge = "calc.merge.unmerge";
    public const string IndentDecrease = "calc.indentDecrease";
    public const string IndentIncrease = "calc.indentIncrease";

    // Start – Zahl
    public const string NumberFormat = "calc.numberFormat"; // value: the label of the format (Standard, Zahl, Währung …)
    public const string Accounting = "calc.accounting";
    public const string Percent = "calc.percent";
    public const string Thousands = "calc.thousands";
    public const string DecimalsMore = "calc.decimalsMore";
    public const string DecimalsLess = "calc.decimalsLess";

    // Start – Formatvorlagen
    public const string ConditionalFormatting = "calc.cf"; // menu: calc.cf.<rule>
    public const string CellStyles = "calc.cellStyles"; // value: the id of the cell style

    // Start – Zellen
    public const string Insert = "calc.insert"; // menu: calc.insert.cells / .rows / .columns / .sheet
    public const string InsertCells = "calc.insert.cells";
    public const string InsertRows = "calc.insert.rows";
    public const string InsertColumns = "calc.insert.columns";
    public const string InsertSheet = "calc.insert.sheet";
    public const string Delete = "calc.delete"; // menu: calc.delete.cells / .rows / .columns / .sheet
    public const string DeleteCells = "calc.delete.cells";
    public const string DeleteRows = "calc.delete.rows";
    public const string DeleteColumns = "calc.delete.columns";
    public const string DeleteSheet = "calc.delete.sheet";
    public const string Format = "calc.format"; // menu: calc.format.<entry>
    public const string RowHeight = "calc.format.rowHeight";
    public const string AutoFitRows = "calc.format.autoFitRows";
    public const string ColumnWidth = "calc.format.columnWidth";
    public const string AutoFitColumns = "calc.format.autoFitColumns";
    public const string HideRows = "calc.format.hideRows";
    public const string HideColumns = "calc.format.hideColumns";
    public const string UnhideRows = "calc.format.unhideRows";
    public const string UnhideColumns = "calc.format.unhideColumns";
    public const string RenameSheet = "calc.format.renameSheet";
    public const string FormatCells = "calc.formatCells";

    // Start – Bearbeiten
    public const string AutoSum = "calc.autoSum"; // split: calc.autoSum.<function>
    public const string Fill = "calc.fill"; // menu: calc.fill.down / .right
    public const string FillDown = "calc.fill.down";
    public const string FillRight = "calc.fill.right";
    public const string Clear = "calc.clear"; // menu: calc.clear.all / .formats / .contents / .comments / .links
    public const string ClearAll = "calc.clear.all";
    public const string ClearFormats = "calc.clear.formats";
    public const string ClearContents = "calc.clear.contents";
    public const string ClearComments = "calc.clear.comments";
    public const string ClearLinks = "calc.clear.links";
    public const string SortFilter = "calc.sortFilter"; // menu
    public const string SortAscending = "calc.sort.asc";
    public const string SortDescending = "calc.sort.desc";
    public const string SortCustom = "calc.sort.custom";
    public const string Filter = "calc.filter";
    public const string FilterClear = "calc.filter.clear";
    public const string FindSelect = "calc.findSelect"; // menu
    public const string Find = "edit.find";
    public const string Replace = "edit.replace";
    public const string GoTo = "calc.goTo";
    public const string SelectAll = "edit.selectAll";

    // Einfügen
    public const string Hyperlink = "calc.hyperlink";
    public const string Comment = "calc.comment";
    public const string DeleteComment = "calc.comment.delete";
    public const string Symbol = "calc.symbol"; // the menu's symbols are calc.symbol.u<hex>

    // Seitenlayout
    public const string Margins = "calc.page.margins"; // presets calc.page.margins.<id>
    public const string Orientation = "calc.page.orientation";
    public const string Portrait = "calc.page.orientation.portrait";
    public const string Landscape = "calc.page.orientation.landscape";
    public const string PaperSize = "calc.page.size"; // sizes calc.page.size.<id>
    public const string PrintArea = "calc.page.printArea";
    public const string SetPrintArea = "calc.page.printArea.set";
    public const string ClearPrintArea = "calc.page.printArea.clear";
    public const string PrintTitles = "calc.page.printTitles";
    public const string PrintGridLines = "calc.page.gridLines";
    public const string PrintHeadings = "calc.page.headings";
    public const string PageSetup = "calc.page.setup";
    public const string FitToPage = "calc.page.fit"; // fits calc.page.fit.<id>

    // Formeln
    public const string InsertFunction = "calc.insertFunction";
    public const string FunctionLibrary = "calc.functions"; // per category calc.functions.<category> (+ .insert), entries calc.function.<NAME>
    public const string MoreFunctions = "calc.functions.more";
    public const string NameManager = "calc.nameManager";
    public const string DefineName = "calc.defineName";
    public const string ShowFormulas = "calc.showFormulas";
    public const string CalculateNow = "calc.calculateNow";
    public const string CalculateSheet = "calc.calculateSheet";
    public const string CalculationOptions = "calc.calculationOptions";
    public const string CalculationAutomatic = "calc.calculationOptions.automatic";
    public const string CalculationManual = "calc.calculationOptions.manual";

    // Daten
    public const string DataSortAscending = "calc.data.sortAsc";
    public const string DataSortDescending = "calc.data.sortDesc";
    public const string DataSort = "calc.data.sort";
    public const string DataFilter = "calc.data.filter";
    public const string DataFilterClear = "calc.data.filterClear";
    public const string TextToColumns = "calc.data.textToColumns";
    public const string RemoveDuplicates = "calc.data.removeDuplicates";
    public const string Validation = "calc.data.validation";
    public const string Group = "calc.data.group";
    public const string Ungroup = "calc.data.ungroup";

    // Überprüfen
    public const string NewComment = "calc.review.newComment";
    public const string DeleteCommentReview = "calc.review.deleteComment";

    // Ansicht
    public const string Freeze = "calc.freeze"; // menu
    public const string FreezePanes = "calc.freeze.panes";
    public const string FreezeTopRow = "calc.freeze.topRow";
    public const string FreezeFirstColumn = "calc.freeze.firstColumn";
    public const string Unfreeze = "calc.freeze.off";
    public const string ShowGridLines = "calc.view.gridLines";
    public const string ShowHeadings = "calc.view.headings";
    public const string ShowFormulaBar = "calc.view.formulaBar";
    public const string ZoomDialog = "calc.zoom";
    public const string ZoomSelection = "calc.zoomSelection";

    // Inhalte einfügen …, Reihe …, Strg+Plus / Strg+Minus
    public const string PasteSpecial = "calc.paste.special";
    public const string FillSeries = "calc.fill.series";
    public const string InsertByKey = "calc.cells.insertByKey";
    public const string InsertByKeyPad = "calc.cells.insertByKeyPad";
    public const string DeleteByKey = "calc.cells.deleteByKey";
    public const string DeleteByKeyPad = "calc.cells.deleteByKeyPad";

    // Einfügen – Tabellen; Start – Als Tabelle formatieren (value: the style name)
    public const string InsertTable = "calc.table.insert";

    // PivotTable (Einfügen, contextual tabs PivotTable-Analyse and Entwurf)
    public const string InsertPivot = "calc.pivot.insert";

    // Block D: outline, what-if, auditing, names, select special, protection
    public const string Subtotals = "calc.data.subtotals";
    public const string ShowDetail = "calc.data.showDetail";
    public const string HideDetail = "calc.data.hideDetail";
    public const string WhatIf = "calc.data.whatIf";
    public const string GoalSeek = "calc.data.goalSeek";
    public const string TracePrecedents = "calc.audit.precedents";
    public const string TraceDependents = "calc.audit.dependents";
    public const string RemoveArrows = "calc.audit.removeArrows";
    public const string ErrorChecking = "calc.audit.errorChecking";
    public const string EvaluateFormula = "calc.audit.evaluate";
    public const string UseInFormula = "calc.names.useInFormula";
    public const string CreateNames = "calc.names.createFromSelection";
    public const string SelectSpecial = "calc.selectSpecial";
    public const string SelectVisible = "calc.selectVisible";
    public const string ProtectSheet = "calc.review.protectSheet";
    public const string ProtectWorkbook = "calc.review.protectWorkbook";
    public const string LockCell = "calc.format.lockCell";
    public const string PivotName = "calc.pivot.name"; // value: the name
    public const string PivotRefresh = "calc.pivot.refresh";
    public const string PivotRefreshAll = "calc.pivot.refreshAll";
    public const string PivotChangeSource = "calc.pivot.source";
    public const string PivotFieldSettings = "calc.pivot.fieldSettings";
    public const string PivotValueSettings = "calc.pivot.valueSettings";
    public const string PivotGroup = "calc.pivot.group";
    public const string PivotUngroup = "calc.pivot.ungroup";
    public const string PivotDelete = "calc.pivot.delete";
    public const string PivotMove = "calc.pivot.move";
    public const string PivotFieldList = "calc.pivot.fieldList";
    public const string PivotDetails = "calc.pivot.details";
    public const string PivotSortAscending = "calc.pivot.sortAscending";
    public const string PivotSortDescending = "calc.pivot.sortDescending";
    public const string PivotSubtotals = "calc.pivot.subtotals"; // menu: .none .bottom .top
    public const string PivotGrandTotals = "calc.pivot.grandTotals"; // menu: .off .both .rows .columns
    public const string PivotLayout = "calc.pivot.layout"; // menu: .compact .outline .tabular .repeat .norepeat
    public const string PivotRowHeaders = "calc.pivot.rowHeaders";
    public const string PivotColumnHeaders = "calc.pivot.columnHeaders";
    public const string PivotBandedRows = "calc.pivot.bandedRows";
    public const string PivotBandedColumns = "calc.pivot.bandedColumns";
    public const string PivotStyles = "calc.pivot.styles"; // value: a PivotStyle name
    public const string InsertTableAlias = "calc.table.insertAlias";
    public const string FormatAsTable = "calc.table.formatAs";

    // Tabellenentwurf (context "table")
    public const string TableName = "calc.table.name"; // value: the name
    public const string TableResize = "calc.table.resize";
    public const string TableConvert = "calc.table.convert";
    public const string TableHeaderRow = "calc.table.headerRow";
    public const string TableTotalRow = "calc.table.totalRow";
    public const string TableBandedRows = "calc.table.bandedRows";
    public const string TableFirstColumn = "calc.table.firstColumn";
    public const string TableLastColumn = "calc.table.lastColumn";
    public const string TableBandedColumns = "calc.table.bandedColumns";
    public const string TableFilterButton = "calc.table.filterButton";
    public const string TableStyles = "calc.table.styles"; // value: the style name

    // Einfügen – Diagramme ("calc.chart.insert.<template>", menus "calc.chart.family.<family>")
    public const string InsertChartPrefix = "calc.chart.insert.";
    public const string ChartFamilyPrefix = "calc.chart.family.";

    // Diagrammentwurf (context "chart")
    public const string ChartAddElement = "calc.chart.addElement"; // menu
    public const string ChartTitle = "calc.chart.element.title";
    public const string ChartAxes = "calc.chart.element.axes"; // menu
    public const string ChartAxisHorizontal = "calc.chart.element.axisHorizontal";
    public const string ChartAxisVertical = "calc.chart.element.axisVertical";
    public const string ChartAxisTitles = "calc.chart.element.axisTitles"; // menu
    public const string ChartAxisTitleHorizontal = "calc.chart.element.axisTitleHorizontal";
    public const string ChartAxisTitleVertical = "calc.chart.element.axisTitleVertical";
    public const string ChartDataLabels = "calc.chart.element.dataLabels";
    public const string ChartGridlines = "calc.chart.element.gridlines"; // menu
    public const string ChartGridlinesMajor = "calc.chart.element.gridlinesMajor";
    public const string ChartGridlinesVertical = "calc.chart.element.gridlinesVertical";
    public const string ChartLegend = "calc.chart.element.legend"; // menu
    public const string ChartLegendPrefix = "calc.chart.element.legend."; // none, right, top, left, bottom
    public const string ChartColors = "calc.chart.colors"; // menu
    public const string ChartColorPrefix = "calc.chart.colors.";
    public const string ChartSwitchRowColumn = "calc.chart.switchRowColumn";
    public const string ChartSelectData = "calc.chart.selectData";
    public const string ChartChangeType = "calc.chart.changeType";

    // Format (context "chart")
    public const string ChartHeight = "calc.chart.height"; // value: cm
    public const string ChartWidth = "calc.chart.width"; // value: cm
    public const string ChartBringForward = "calc.chart.bringForward";
    public const string ChartBringToFront = "calc.chart.bringToFront";
    public const string ChartSendBackward = "calc.chart.sendBackward";
    public const string ChartSendToBack = "calc.chart.sendToBack";
    public const string ChartDelete = "calc.chart.delete";

    // Blätter
    public const string NextSheet = "calc.sheet.next";
    public const string PreviousSheet = "calc.sheet.previous";

    // Datei
    public const string ExportPdf = "file.exportPdf";
    public const string Print = "file.print";
}
