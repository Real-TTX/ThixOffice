using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MatDesktop;
using ThixCalc.Core;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using ThixCalc.Pages;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;

namespace ThixCalc;

/// <summary>
/// ThixCalc on ThixOffice.Desktop: the definition (ThixCalcApp), the document (<see cref="CalcDocument"/>: xlsx, xlsm, csv), the
/// editor (<see cref="CalcEditor"/>), ThixCalc's commands and ribbon, its backstage pages, self test and snapshot scenes.
/// Start-up, single instance, setup, updates, the window, the backstage and the settings are OfficeApplication's.
/// </summary>
public partial class App : OfficeApplication
{
    public const string FormulaScene = "main-formula";
    public const string EditScene = "main-edit";
    public const string FindScene = "main-find";
    public const string FormatCellsScene = "dialog-format-cells";
    public const string FunctionScene = "dialog-function";
    public const string ViewScene = "main-view";
    public const string PrintScene = "backstage-print";
    public const string PageSetupScene = "dialog-page-setup";
    public const string ChartScene = "main-chart";
    public const string ChartDesignScene = "main-chart-design";
    public const string TableScene = "main-table";
    public const string FilterScene = "dialog-filter";
    public const string FunctionArgumentsScene = "dialog-function-arguments";
    public const string PivotScene = "main-pivot";
    public const string PivotDesignScene = "main-pivot-design";

    public override OfficeAppDefinition Definition => ThixCalcApp.Definition;

    public override IReadOnlyList<string> SnapshotScenes => [FormulaScene, EditScene, ViewScene, FindScene, FormatCellsScene, FunctionScene, FunctionArgumentsScene, PageSetupScene, PrintScene, ChartScene, ChartDesignScene, TableScene, FilterScene, PivotScene, PivotDesignScene];

    public override string SampleDocumentName => "Haushaltsbudget 2026";

    public override MatRibbonDefinition CreateRibbon(OfficeWindow window) => CalcRibbon.Create(window);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    public override OfficeDocument CreateDocument(string untitledName) => new CalcDocument(untitledName);

    public override OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document) => new CalcEditor(window, (CalcDocument)document);

    public override void AddCommands(OfficeWindow window)
    {
        CalcCommands.Add(window);
        CalcStyleCommands.Add(window);
        CalcPageCommands.Add(window);
        CalcChartCommands.Add(window);
        CalcTableCommands.Add(window);
        CalcPivotCommands.Add(window);
        CalcAnalysisCommands.Add(window);
        CalcEditCommands.Add(window);
    }

    public override IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) => [CalcPrintPage.Item(window), CalcExportPage.Item(window)];

    public override IEnumerable<(string Name, Func<Task<string>> Check)> SelfTestChecks() => [("ThixCalc grid, formulas, xlsx", () => CalcSelfTest.RunAsync(this))];

    /// <summary>The household budget, the active cell on the first expense of January.</summary>
    protected override void FillSampleDocument(OfficeDocument document)
    {
        var calc = (CalcDocument)document;
        calc.Load(SampleWorkbook.Create());
    }

    public override TopLevel ShowSnapshotScene(OfficeWindow window, string name)
    {
        var editor = (CalcEditor)window.Editor;
        switch (name)
        {
            case FormulaScene:
                // A formula being typed: references in colour, the list of functions under the caret.
                editor.Session.Select(new CellAddress(17, 7));
                Dispatcher.UIThread.RunJobs();
                editor.Editing.Begin(EditStart.Typing);
                editor.Editing.CellBox.InsertText("=SUMME(H7:H13)/H4+SUMMEW");
                Dispatcher.UIThread.RunJobs();
                return window;
            case EditScene:
                // F2 on a text cell: the text stays where the grid drew it, the active cell's border around the field.
                editor.Session.Select(new CellAddress(8, 0));
                Dispatcher.UIThread.RunJobs();
                editor.Editing.Begin(EditStart.Edit);
                Dispatcher.UIThread.RunJobs();
                return window;
            case ViewScene:
                window.Ribbon.SelectTab("formulas");
                editor.Session.Select(new CellRange(6, 1, 12, 6));
                return window;
            case FindScene:
                editor.FindBar.Open(replace: true);
                editor.FindBar.SearchBox.Text = "Miete";
                editor.FindBar.ReplacementBox.Text = "Wohnung";
                return window;
            case FormatCellsScene:
                editor.Session.Select(new CellRange(6, 1, 12, 6));
                var dialog = new FormatCellsDialog(editor);
                _ = dialog.ShowAsync(window);
                return dialog;
            case FunctionScene:
                var function = InsertFunctionDialog.Create(editor.Session.Locale);
                function.SearchBox.Text = "verweis";
                _ = function.ShowAsync(window);
                return function;
            case PrintScene:
                editor.Session.SetPageSetup(setup => setup with { Landscape = true, FitToPage = true, FitToWidth = 1, FitToHeight = 0, Footer = "&CSeite &P von &N" });
                window.ShowBackstage(CalcPrintPage.PageId);
                return window;
            case ChartScene or ChartDesignScene:
                // The budget's charts below the table: columns per month and the expenses as a pie.
                var (columns, _) = SampleWorkbook.AddCharts(editor.Session, editor.LayoutOf(editor.Session.ActiveSheet));
                editor.Session.Select(new CellAddress(16, 1));
                editor.Session.ScrollTo(new CellAddress(12, 1));
                editor.Surface.ResetSheet();
                window.Ribbon.SelectTab("insert");
                if (name == ChartDesignScene)
                {
                    editor.Session.SelectChart(columns.Id);
                    Dispatcher.UIThread.RunJobs();
                    window.Ribbon.SelectTab(CalcEditor.ChartDesignTab);
                }

                Dispatcher.UIThread.RunJobs();
                return window;
            case FilterScene:
                // Zahlenfilter › Größer als … on the budget's January column.
                editor.Session.ToggleAutoFilter(new CellRange(1, 0, 12, 9));
                var filterDialog = CustomFilterDialog.Create(editor, 1, "Januar", 2);
                filterDialog.FirstValue.Text = "500";
                _ = filterDialog.ShowAsync(window);
                return filterDialog;
            case TableScene:
                // A formatted table with a total row on the second sheet, the tab Tabellenentwurf open.
                _tableSceneSteps = SampleWorkbook.AddTable(editor.Session);
                Dispatcher.UIThread.RunJobs();
                window.Ribbon.SelectTab("tableDesign");
                Dispatcher.UIThread.RunJobs();
                return window;
            case FunctionArgumentsScene:
                // SVERWEIS over the budget: each argument's value beside its field, the result below.
                editor.Session.Select(new CellAddress(17, 7));
                var vlookup = Core.Functions.FunctionHelp.Find("SVERWEIS", editor.Session.Locale)!;
                var arguments = FunctionArgumentsDialog.Create(editor.Session, vlookup, ["\"Miete\"", "A7:C13", "2", "FALSCH"]);
                _ = arguments.ShowAsync(window);
                return arguments;
            case PivotScene or PivotDesignScene:
                // The expenses of the half year as a PivotTable: groups and categories by month, the field list beside it.
                _pivotSceneSheets = editor.Session.Workbook.Sheets.Count;
                SampleWorkbook.AddPivot(editor.Session);
                editor.ShowFieldList(true);
                Dispatcher.UIThread.RunJobs();
                window.Ribbon.SelectTab(name == PivotScene ? CalcEditor.PivotAnalyzeTab : CalcEditor.PivotDesignTab);
                Dispatcher.UIThread.RunJobs();
                return window;
            case PageSetupScene:
                var setupDialog = PageSetupDialog.Create(editor);
                _ = setupDialog.ShowAsync(window);
                return setupDialog;
            default:
                return window;
        }
    }

    private int _tableSceneSteps;
    private int _pivotSceneSheets;

    public override void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
        var editor = (CalcEditor)window.Editor;
        switch (name)
        {
            case FormulaScene or EditScene:
                editor.Editing.Cancel();
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
            case ViewScene:
                window.Ribbon.SelectTab("home");
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
            case FindScene:
                editor.FindBar.Close();
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
            case ChartScene or ChartDesignScene:
                window.Ribbon.SelectTab("home");
                editor.Session.Undo();
                editor.Session.Undo();
                editor.Session.ScrollTo(new CellAddress(2, 1));
                editor.Surface.ResetSheet();
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
            case FilterScene:
                if (shown is Window filterWindow)
                {
                    filterWindow.Close();
                }

                editor.Session.Undo();
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
            case TableScene:
                window.Ribbon.SelectTab("home");
                for (var step = 0; step < _tableSceneSteps; step++)
                {
                    editor.Session.Undo();
                }

                editor.Session.ActivateSheet(0);
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
            case PivotScene or PivotDesignScene:
                window.Ribbon.SelectTab("home");
                while (editor.Session.CanUndo && editor.Session.Workbook.Sheets.Count > _pivotSceneSheets)
                {
                    editor.Session.Undo();
                }

                editor.Session.ActivateSheet(0);
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
            case PrintScene:
                window.Backstage.Close();
                editor.Session.Undo();
                break;
            case FormatCellsScene or FunctionScene or PageSetupScene when shown is Window dialog:
                dialog.Close();
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
        }
    }
}
