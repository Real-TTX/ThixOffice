using System.Globalization;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Layout;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixOffice.Charts;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// The chart commands: Einfügen › Diagramme (a command per chart type, menus per family), the contextual tabs
/// Diagrammentwurf (Diagrammelement hinzufügen: titles, axes, data labels, gridlines, legend; Farben ändern; Zeile/Spalte
/// wechseln; Daten auswählen; Diagrammtyp ändern) and Format (Höhe, Breite, Anordnen), and Löschen. Every change goes through
/// the session as one undo step.
/// </summary>
public static class CalcChartCommands
{
    /// <summary>The context of the chart tabs (shown while a chart is selected).</summary>
    public const string Context = "chart";

    public const double PointsPerCentimetre = 72 / 2.54;

    public static IReadOnlyList<(string Id, string Label, LegendPosition? Position)> LegendChoices { get; } =
    [
        ("none", "Keine", null), ("right", "Rechts", LegendPosition.Right), ("top", "Oben", LegendPosition.Top),
        ("left", "Links", LegendPosition.Left), ("bottom", "Unten", LegendPosition.Bottom),
    ];

    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        SheetChart? Selected() => Editor() is { Editing.IsEditing: false } editor ? editor.Session.SelectedChart : null;
        bool Editable() => Selected() is { Chart.IsSupported: true };
        Chart? Current() => Selected()?.Chart;
        void Change(string label, Func<Chart, Chart> change)
        {
            if (Editor() is { } editor && Selected() is { } chart)
            {
                _ = editor.RunAsync(label, () => editor.Session.ChangeChart(chart.Id, label, change));
            }
        }

        var commands = window.Commands;

        // ===== Einfügen › Diagramme =====
        foreach (var family in Enum.GetValues<ChartFamily>())
        {
            commands.Add(new OfficeCommand(CalcCommandIds.ChartFamilyPrefix + family.ToString().ToLowerInvariant(), ChartTemplates.FamilyLabel(family), () => { })
            {
                Icon = IconOf(family), CanExecute = Ready, IsSearchable = false,
                Description = $"Fügt ein {ChartTemplates.FamilyLabel(family)}diagramm aus den markierten Daten ein.",
            });
        }

        foreach (var template in ChartTemplates.All)
        {
            var chosen = template;
            commands.Add(new OfficeCommand(CalcCommandIds.InsertChartPrefix + template.Id, template.Label, () => Editor()?.InsertChartAsync(chosen) ?? Task.CompletedTask)
            {
                Icon = IconOf(template.Family), CanExecute = Ready,
                Description = template.Description.Length > 0 ? template.Description : $"Fügt ein Diagramm „{template.Label}“ aus den markierten Daten ein.",
                Keywords = ["diagramm", "chart", "grafik", ChartTemplates.FamilyLabel(template.Family).ToLowerInvariant()],
            });
        }

        // ===== Diagrammentwurf =====
        commands.Add(new OfficeCommand(CalcCommandIds.ChartAddElement, "Diagrammelement hinzufügen", () => { }) { Icon = "plus", CanExecute = Editable, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.ChartTitle, "Diagrammtitel", () => Change("Diagrammtitel", chart => ChartEdits.SetTitle(chart, !HasTitle(chart))))
        {
            Icon = "type", CanExecute = Editable, IsChecked = () => Current() is { } chart && HasTitle(chart), Keywords = ["titel", "überschrift"],
            Description = "Blendet den Titel über dem Diagramm ein oder aus.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ChartAxes, "Achsen", () => { }) { Icon = "chart-line", CanExecute = Editable, IsSearchable = false });
        commands.Add(Toggle(CalcCommandIds.ChartAxisHorizontal, "Primär horizontal", AxisRole.PrimaryHorizontal, axis => axis is { Deleted: false }, (chart, on) => ChartEdits.SetAxisVisible(chart, AxisRole.PrimaryHorizontal, on)));
        commands.Add(Toggle(CalcCommandIds.ChartAxisVertical, "Primär vertikal", AxisRole.PrimaryVertical, axis => axis is { Deleted: false }, (chart, on) => ChartEdits.SetAxisVisible(chart, AxisRole.PrimaryVertical, on)));
        commands.Add(new OfficeCommand(CalcCommandIds.ChartAxisTitles, "Achsentitel", () => { }) { Icon = "type", CanExecute = Editable, IsSearchable = false });
        commands.Add(Toggle(CalcCommandIds.ChartAxisTitleHorizontal, "Primär horizontal", AxisRole.PrimaryHorizontal, axis => axis?.Title is not null, (chart, on) => ChartEdits.SetAxisTitle(chart, AxisRole.PrimaryHorizontal, on)));
        commands.Add(Toggle(CalcCommandIds.ChartAxisTitleVertical, "Primär vertikal", AxisRole.PrimaryVertical, axis => axis?.Title is not null, (chart, on) => ChartEdits.SetAxisTitle(chart, AxisRole.PrimaryVertical, on)));
        commands.Add(new OfficeCommand(CalcCommandIds.ChartDataLabels, "Datenbeschriftungen", () => Change("Datenbeschriftungen", chart => ChartEdits.SetDataLabels(chart, !HasLabels(chart))))
        {
            Icon = "tag", CanExecute = Editable, IsChecked = () => Current() is { } chart && HasLabels(chart), Keywords = ["werte anzeigen", "beschriftung"],
            Description = "Zeigt die Werte an den Datenpunkten.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ChartGridlines, "Gitternetzlinien", () => { }) { Icon = "grid", CanExecute = Editable, IsSearchable = false });
        commands.Add(Toggle(CalcCommandIds.ChartGridlinesMajor, "Primäre horizontale Hauptgitternetzlinien", AxisRole.PrimaryVertical, axis => axis is { MajorGridlines: true }, (chart, on) => ChartEdits.SetGridlines(chart, AxisRole.PrimaryVertical, on, false)));
        commands.Add(Toggle(CalcCommandIds.ChartGridlinesVertical, "Primäre vertikale Hauptgitternetzlinien", AxisRole.PrimaryHorizontal, axis => axis is { MajorGridlines: true }, (chart, on) => ChartEdits.SetGridlines(chart, AxisRole.PrimaryHorizontal, on, false)));
        commands.Add(new OfficeCommand(CalcCommandIds.ChartLegend, "Legende", () => { }) { Icon = "list", CanExecute = Editable, IsSearchable = false });
        foreach (var (id, label, position) in LegendChoices)
        {
            var target = position;
            commands.Add(new OfficeCommand(CalcCommandIds.ChartLegendPrefix + id, label, () => Change("Legende", chart => ChartEdits.SetLegend(chart, target)))
            {
                CanExecute = Editable, IsSearchable = false, IsChecked = () => Current() is { } chart && chart.Legend?.Position == target,
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.ChartColors, "Farben ändern", () => { })
        {
            Icon = "palette", CanExecute = Editable, Keywords = ["farbpalette", "diagrammfarben"],
            Description = "Wählt die Farben der Datenreihen aus den Designfarben.",
        });
        foreach (var set in ChartColors.Sets)
        {
            var id = set.Id;
            commands.Add(new OfficeCommand(CalcCommandIds.ChartColorPrefix + id.ToString(CultureInfo.InvariantCulture), set.Label, () => Change("Farben ändern", chart => ChartEdits.SetColorSet(chart, id)))
            {
                CanExecute = Editable, IsSearchable = false, IsChecked = () => Current()?.ColorSet == id,
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.ChartSwitchRowColumn, "Zeile/Spalte wechseln", () =>
        {
            if (Editor() is { } editor && Selected() is { } chart)
            {
                _ = editor.RunAsync("Zeile/Spalte wechseln", () => editor.Session.SwitchRowColumn(chart.Id));
            }
        })
        {
            Icon = "refresh", CanExecute = Editable, Keywords = ["zeilen und spalten", "reihen tauschen"],
            Description = "Tauscht, was Datenreihen und was Kategorien sind.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ChartSelectData, "Daten auswählen", () => Editor() is { } editor && Selected() is { } chart ? SelectDataDialog.ShowAsync(window, editor, chart) : Task.CompletedTask)
        {
            Icon = "table", CanExecute = Editable, Keywords = ["datenquelle", "bereich", "datenreihen"],
            Description = "Ändert den Datenbereich des Diagramms.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ChartChangeType, "Diagrammtyp ändern", () => Editor() is { } editor && Selected() is { } chart ? ChangeChartTypeDialog.ShowAsync(window, editor, chart) : Task.CompletedTask)
        {
            Icon = "chart-column", CanExecute = Editable, Keywords = ["diagrammtyp", "typ"],
            Description = "Zeigt die Daten als anderen Diagrammtyp.",
        });

        // ===== Format =====
        commands.Add(new OfficeValueCommand(CalcCommandIds.ChartHeight, "Höhe", OfficeValueKind.Number,
            () => Editor() is { } editor && Selected() is { } chart ? Centimetres(editor.ChartSize(chart).Height) : null,
            value => Editor()?.ResizeSelectedChart(value, height: true))
        {
            Icon = "", CanExecute = () => Selected() is not null, IsSearchable = false,
        });
        commands.Add(new OfficeValueCommand(CalcCommandIds.ChartWidth, "Breite", OfficeValueKind.Number,
            () => Editor() is { } editor && Selected() is { } chart ? Centimetres(editor.ChartSize(chart).Width) : null,
            value => Editor()?.ResizeSelectedChart(value, height: false))
        {
            Icon = "", CanExecute = () => Selected() is not null, IsSearchable = false,
        });
        foreach (var (id, label, order) in new (string, string, ChartOrder)[]
                 {
                     (CalcCommandIds.ChartBringForward, "Ebene nach vorne", ChartOrder.BringForward),
                     (CalcCommandIds.ChartBringToFront, "In den Vordergrund", ChartOrder.BringToFront),
                     (CalcCommandIds.ChartSendBackward, "Ebene nach hinten", ChartOrder.SendBackward),
                     (CalcCommandIds.ChartSendToBack, "In den Hintergrund", ChartOrder.SendToBack),
                 })
        {
            var target = order;
            commands.Add(new OfficeCommand(id, label, () =>
            {
                if (Editor() is { } editor && Selected() is { } chart)
                {
                    _ = editor.RunAsync(label, () => editor.Session.OrderChart(chart.Id, target));
                }
            })
            {
                Icon = "layers", CanExecute = () => Selected() is not null && Editor()!.Session.ActiveSheet.Charts.Count > 1, IsSearchable = false,
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.ChartDelete, "Diagramm löschen", () =>
        {
            if (Editor() is { } editor && Selected() is { } chart)
            {
                _ = editor.RunAsync("Diagramm löschen", () => editor.Session.DeleteChart(chart.Id));
            }
        })
        {
            Icon = "trash", CanExecute = () => Selected() is not null, Keywords = ["diagramm entfernen"],
        });

        OfficeCommand Toggle(string id, string label, AxisRole role, Func<ChartAxis?, bool> isOn, Func<Chart, bool, Chart> change) =>
            new(id, label, () =>
            {
                if (Current() is { } chart)
                {
                    var on = isOn(ChartEdits.AxisOf(chart, role));
                    Change(label, current => change(current, !on));
                }
            })
            {
                CanExecute = () => Editable() && ChartEdits.AxisOf(Current()!, role) is not null,
                IsChecked = () => Current() is { } chart && isOn(ChartEdits.AxisOf(chart, role)),
                IsSearchable = false,
            };
    }

    private static bool HasTitle(Chart chart) => chart.Title is not null || (!chart.AutoTitleDeleted && chart.AllSeries.Count() == 1 && chart.AllSeries.First().Name is not null);

    private static bool HasLabels(Chart chart) => chart.Plots.Any(plot => (plot.Labels?.IsVisible ?? false) || plot.Series.Any(series => series.Labels?.IsVisible == true));

    private static string Centimetres(double points) => Math.Round(points / PointsPerCentimetre, 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>The sprite icon of a chart family (stand-ins where the sprite has none yet; see docs/PLAN.md "Icons für MatDesign").</summary>
    public static string IconOf(ChartFamily family) => family switch
    {
        ChartFamily.Column => "chart-column",
        ChartFamily.Bar => "chart-bar",
        ChartFamily.Line => "chart-line",
        ChartFamily.Pie => "chart-pie",
        ChartFamily.Area => "chart-line",
        ChartFamily.Scatter => "bar-chart",
        _ => "chart-column",
    };
}
