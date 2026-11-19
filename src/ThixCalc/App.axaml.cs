using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using MatDesktop;
using ThixCalc.Core;
using ThixCalc.Core.Documents;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
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
    public const string FindScene = "main-find";
    public const string FormatCellsScene = "dialog-format-cells";
    public const string FunctionScene = "dialog-function";
    public const string ViewScene = "main-view";

    public override OfficeAppDefinition Definition => ThixCalcApp.Definition;

    public override IReadOnlyList<string> SnapshotScenes => [FormulaScene, ViewScene, FindScene, FormatCellsScene, FunctionScene];

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
    }

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
            default:
                return window;
        }
    }

    public override void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
        var editor = (CalcEditor)window.Editor;
        switch (name)
        {
            case FormulaScene:
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
            case FormatCellsScene or FunctionScene when shown is Window dialog:
                dialog.Close();
                editor.Session.Select(SampleWorkbook.StartCell);
                break;
        }
    }
}
