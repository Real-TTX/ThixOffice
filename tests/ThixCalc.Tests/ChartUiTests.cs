using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using MatDesktop;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Editor;
using ThixOffice.Charts;
using ThixOffice.Desktop.Shell;
using static ThixCalc.Tests.TestData;

namespace ThixCalc.Tests;

/// <summary>
/// Charts in the real ThixCalc, driven by real input: inserting from the ribbon command, selecting by a click (the chart
/// tabs show), moving by dragging, sizing by a handle, the elements of Diagrammentwurf, the dialogs, copy/paste, deleting
/// with Entf and undoing each of it.
/// </summary>
public sealed class ChartUiTests
{
    private static async Task<OfficeWindow> BudgetAsync()
    {
        var window = await NewWindowAsync();
        foreach (var row in new[] { new[] { "Monat", "Einnahmen", "Ausgaben" }, ["Januar", "2400", "1900"], ["Februar", "2500", "2100"], ["März", "2600", "1750"] })
        {
            foreach (var value in row)
            {
                await TypeAsync(window, value);
                await PressAsync(window, PhysicalKey.Tab);
            }

            await PressAsync(window, PhysicalKey.Enter);
            EditorOf(window).Session.Select(new CellAddress(EditorOf(window).Session.ActiveCell.Row, 0));
        }

        EditorOf(window).Session.Select(new CellAddress(0, 0));
        await SettleAsync();
        return window;
    }

    /// <summary>The middle of the chart in the window's coordinates.</summary>
    private static Point CenterOf(OfficeWindow window, SheetChart chart) => PointIn(window, chart, 0.5, 0.5);

    private static Point PointIn(OfficeWindow window, SheetChart chart, double fx, double fy)
    {
        var editor = EditorOf(window);
        var rect = ChartLayer.ViewRect(editor.Surface.Viewport(), chart.Anchor);
        var scaling = editor.Surface.Scale / (editor.Zoom / 100);
        return SurfacePoint(window, (rect.X + rect.Width * fx) / scaling, (rect.Y + rect.Height * fy) / scaling);
    }

    [AvaloniaFact]
    public async Task Inserting_a_chart_selects_it_and_shows_the_design_tab()
    {
        using var data = new TestData();
        var window = await BudgetAsync();
        var editor = EditorOf(window);

        await window.Commands.ExecuteAsync(CalcCommandIds.InsertChartPrefix + "column");
        await SettleAsync();

        var chart = Assert.Single(editor.Session.ActiveSheet.Charts);
        Assert.Equal(chart.Id, editor.Session.SelectedChart?.Id);
        Assert.Contains(CalcChartCommands.Context, window.RibbonDefinition.ActiveContexts);
        Assert.Equal(2, chart.Chart.AllSeries.Count());
        Assert.Equal("Diagramm 1", editor.FormulaBar.NameBox.Text);

        // A click into the cells goes back to them; the tabs go.
        await ClickAsync(window, PointOf(window, new CellAddress(1, 1)));
        Assert.Null(editor.Session.SelectedChart);
        Assert.DoesNotContain(CalcChartCommands.Context, window.RibbonDefinition.ActiveContexts);

        // A click on the chart selects it again.
        await ClickAsync(window, CenterOf(window, chart));
        Assert.Equal(chart.Id, editor.Session.SelectedChart?.Id);
    }

    [AvaloniaFact]
    public async Task Dragging_moves_and_a_handle_sizes_the_chart()
    {
        using var data = new TestData();
        var window = await BudgetAsync();
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(CalcCommandIds.InsertChartPrefix + "line");
        var chart = editor.Session.ActiveSheet.Charts[0];
        var before = ChartLayer.ViewRect(editor.Surface.Viewport(), chart.Anchor);

        var start = CenterOf(window, chart);
        await DragAsync(window, start, start + new Point(60, 80));
        var moved = editor.Session.ActiveSheet.Charts[0];
        var after = ChartLayer.ViewRect(editor.Surface.Viewport(), moved.Anchor);
        var scaling = editor.Surface.Scale / (editor.Zoom / 100);
        Assert.Equal(before.X + 60 * scaling, after.X, 1.5);
        Assert.Equal(before.Y + 80 * scaling, after.Y, 1.5);
        Assert.Equal(before.Width, after.Width, 1.5);

        // The bottom-right handle makes it bigger.
        var corner = PointIn(window, moved, 1, 1);
        await DragAsync(window, corner, corner + new Point(40, 30));
        var sized = ChartLayer.ViewRect(editor.Surface.Viewport(), editor.Session.ActiveSheet.Charts[0].Anchor);
        Assert.Equal(after.Width + 40 * scaling, sized.Width, 1.5);
        Assert.Equal(after.Height + 30 * scaling, sized.Height, 1.5);

        // Each was one step.
        editor.Session.Undo();
        Assert.Equal(moved.Anchor, editor.Session.ActiveSheet.Charts[0].Anchor);
        editor.Session.Undo();
        Assert.Equal(chart.Anchor, editor.Session.ActiveSheet.Charts[0].Anchor);
    }

    [AvaloniaFact]
    public async Task Entf_deletes_the_selected_chart_and_undo_brings_it_back()
    {
        using var data = new TestData();
        var window = await BudgetAsync();
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(CalcCommandIds.InsertChartPrefix + "pie");
        editor.FocusDocument();

        await PressAsync(window, PhysicalKey.Delete);

        Assert.Empty(editor.Session.ActiveSheet.Charts);
        Assert.Equal("Mona", ValueAt(window, "A1").Text[..4]);
        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Single(editor.Session.ActiveSheet.Charts);
    }

    [AvaloniaFact]
    public async Task The_elements_of_Diagrammentwurf_change_the_chart()
    {
        using var data = new TestData();
        var window = await BudgetAsync();
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(CalcCommandIds.InsertChartPrefix + "column");
        Chart Current() => editor.Session.ActiveSheet.Charts[0].Chart;

        await window.Commands.ExecuteAsync(CalcCommandIds.ChartDataLabels);
        await SettleAsync();
        Assert.True(Current().Plots[0].Labels?.ShowValue);
        Assert.True(window.RibbonDefinition.FindAll(CalcCommandIds.ChartDataLabels).OfType<MatRibbonToggle>().All(toggle => toggle.IsChecked));

        await window.Commands.ExecuteAsync(CalcCommandIds.ChartLegendPrefix + "right");
        await window.Commands.ExecuteAsync(CalcCommandIds.ChartAxisTitleVertical);
        await window.Commands.ExecuteAsync(CalcCommandIds.ChartColorPrefix + "4");
        await window.Commands.ExecuteAsync(CalcCommandIds.ChartTitle);
        await SettleAsync();
        Assert.Equal(LegendPosition.Right, Current().Legend?.Position);
        Assert.NotNull(ChartEdits.AxisOf(Current(), AxisRole.PrimaryVertical)?.Title);
        Assert.Equal(4, Current().ColorSet);
        Assert.Null(Current().Title);

        await window.Commands.ExecuteAsync(CalcCommandIds.ChartSwitchRowColumn);
        await SettleAsync();
        Assert.Equal(3, Current().AllSeries.Count());

        for (var step = 0; step < 6; step++)
        {
            editor.Session.Undo();
        }

        Assert.Equal(2, Current().AllSeries.Count());
        Assert.Null(Current().Plots[0].Labels);
    }

    [AvaloniaFact]
    public async Task The_dialogs_change_type_and_data()
    {
        using var data = new TestData();
        var window = await BudgetAsync();
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(CalcCommandIds.InsertChartPrefix + "column");
        var chart = editor.Session.SelectedChart!;

        var typeDialog = ChangeChartTypeDialog.Create(editor, chart);
        typeDialog.Choose(ChartTemplates.Find("line-markers")!);
        typeDialog.Confirm();
        await SettleAsync();
        Assert.Equal(ChartType.Line, editor.Session.ActiveSheet.Charts[0].Chart.Plots[0].Type);

        var dataDialog = SelectDataDialog.Create(editor, editor.Session.ActiveSheet.Charts[0]);
        Assert.Equal("=Tabelle1!$A$1:$C$4", dataDialog.RangeBox.Text);
        dataDialog.RangeBox.Text = "=Tabelle1!$A$1:$B$4";
        dataDialog.Confirm();
        await SettleAsync();
        Assert.Single(editor.Session.ActiveSheet.Charts[0].Chart.AllSeries);
        Assert.Equal(ChartType.Line, editor.Session.ActiveSheet.Charts[0].Chart.Plots[0].Type);

        var wrong = SelectDataDialog.Create(editor, editor.Session.ActiveSheet.Charts[0]);
        wrong.RangeBox.Text = "=Nirgendwo";
        wrong.Confirm();
        Assert.True(wrong.IsVisible || !wrong.IsActive);
        Assert.Single(editor.Session.ActiveSheet.Charts[0].Chart.AllSeries);
        wrong.Close();
    }

    [AvaloniaFact]
    public async Task Copy_and_paste_make_a_second_chart()
    {
        using var data = new TestData();
        var window = await BudgetAsync();
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(CalcCommandIds.InsertChartPrefix + "bar");
        editor.FocusDocument();

        await PressAsync(window, PhysicalKey.C, CommandKey);
        await ClickAsync(window, PointOf(window, new CellAddress(20, 1)));
        await PressAsync(window, PhysicalKey.V, CommandKey);

        var charts = editor.Session.ActiveSheet.Charts;
        Assert.Equal(2, charts.Count);
        Assert.Equal(new CellAddress(20, 1), charts[1].Anchor.From);
        Assert.Equal(charts[1].Id, editor.Session.SelectedChart?.Id);
        Assert.Equal(ChartType.Bar, charts[1].Chart.Plots[0].Type);
    }

    [AvaloniaFact]
    public async Task The_height_field_of_Format_sizes_the_chart()
    {
        using var data = new TestData();
        var window = await BudgetAsync();
        var editor = EditorOf(window);
        await window.Commands.ExecuteAsync(CalcCommandIds.InsertChartPrefix + "area");
        var command = (ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[CalcCommandIds.ChartHeight];
        Assert.Equal("7.62", command.Value);

        await command.ApplyAsync("10");
        await SettleAsync();

        Assert.Equal(10, editor.ChartSize(editor.Session.ActiveSheet.Charts[0]).Height / CalcChartCommands.PointsPerCentimetre, 1);
    }
}
