using System.Globalization;
using Avalonia.Controls;
using Avalonia.Media;
using ThixOffice.Charts;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// Charts on slides through the shared chart component: Einfügen › Diagramm (a type with PowerPoint's sample data – three series
/// over four categories, edited afterwards in "Daten bearbeiten"), the contextual tab Diagrammentwurf with Diagrammtyp ändern and
/// Daten bearbeiten. The data are the chart's own values (no embedded workbook yet): the dialog edits them as a small table.
/// </summary>
internal static class ShowChartCommands
{
    public const string Context = "chart";
    public const string DesignTab = "chartDesign";
    public const string Insert = "show.chart.insert";
    public const string ChangeType = "show.chart.type";
    public const string EditData = "show.chart.data";

    /// <summary>The types of Einfügen › Diagramm and Diagrammtyp ändern (PowerPoint's main ones).</summary>
    public static IReadOnlyList<(string Id, string Label, string Icon)> Types { get; } =
    [
        ("column", "Säule", "chart-column"), ("column-stacked", "Gestapelte Säulen", "chart-column"), ("bar", "Balken", "chart-bar"),
        ("line-markers", "Linie", "chart-line"), ("pie", "Kreis", "chart-pie"), ("doughnut", "Ring", "chart-pie"), ("area", "Fläche", "chart-line"),
        ("scatter", "Punkt (XY)", "bar-chart"), ("combo", "Verbund", "chart-column"),
    ];

    /// <summary>PowerPoint's sample data of a new chart.</summary>
    public static IReadOnlyList<SeriesSpec> SampleData()
    {
        string[] categories = ["Kategorie 1", "Kategorie 2", "Kategorie 3", "Kategorie 4"];
        return
        [
            new SeriesSpec(ChartText.Of("Datenreihe 1"), ChartDataRef.LiteralText(categories), ChartDataRef.Literal([4.3, 2.5, 3.5, 4.5])),
            new SeriesSpec(ChartText.Of("Datenreihe 2"), ChartDataRef.LiteralText(categories), ChartDataRef.Literal([2.4, 4.4, 1.8, 2.8])),
            new SeriesSpec(ChartText.Of("Datenreihe 3"), ChartDataRef.LiteralText(categories), ChartDataRef.Literal([2, 2, 3, 5])),
        ];
    }

    public static void Add(OfficeWindow window, Func<ShowEditor?> editorOf, Action<string, Action<ShowEditor>> run)
    {
        var commands = window.Commands;
        bool HasSlide() => editorOf()?.Session.CurrentSlide is not null;
        bool ChartSelected() => SelectedChart(editorOf()) is not null;

        commands.Add(new OfficeCommand(Insert, "Diagramm", () => Task.CompletedTask)
        {
            Icon = "chart-column", CanExecute = HasSlide, IsSearchable = false, Description = "Fügt ein Diagramm mit Beispieldaten ein (ändern Sie sie mit „Daten bearbeiten“).",
        });
        foreach (var (id, label, icon) in Types)
        {
            commands.Add(new OfficeCommand($"{Insert}.{id}", label, () => run("Diagramm einfügen", editor =>
            {
                var template = ChartTemplates.Find(id)!;
                editor.Session.InsertChart(ChartFactory.Create(template, SampleData()));
            }))
            {
                Icon = icon, CanExecute = HasSlide, Description = $"Fügt ein Diagramm „{label}“ mit Beispieldaten ein.", Keywords = ["diagramm", "chart"],
            });
            commands.Add(new OfficeCommand($"{ChangeType}.{id}", label, () => run("Diagrammtyp ändern", editor =>
            {
                if (SelectedChart(editor) is { } selected)
                {
                    editor.Session.SetChart(selected.Frame.Id, ChartFactory.WithType(selected.Chart, ChartTemplates.Find(id)!));
                }
            }))
            {
                Icon = icon, CanExecute = ChartSelected, IsSearchable = false, Description = $"Macht das Diagramm zu „{label}“.",
            });
        }

        commands.Add(new OfficeCommand(ChangeType, "Diagrammtyp ändern", () => Task.CompletedTask)
        {
            Icon = "chart-column", CanExecute = ChartSelected, Description = "Zeigt die Daten des Diagramms als anderen Diagrammtyp.",
        });
        commands.Add(new OfficeCommand(EditData, "Daten bearbeiten", () => ChartDataDialog.ShowAsync(window, editorOf()))
        {
            Icon = "table", CanExecute = ChartSelected, Description = "Ändert die Zahlen, Kategorien und Datenreihen des Diagramms.",
        });
    }

    /// <summary>The selected chart frame and its chart (from its own model or its part).</summary>
    public static (GraphicFrame Frame, Chart Chart)? SelectedChart(ShowEditor? editor)
    {
        if (editor?.Session.CurrentSlide is not { } slide || editor.Session.Selection.Shapes.Count != 1
            || slide.FindShape(editor.Session.Selection.Shapes[0]) is not GraphicFrame { Kind: GraphicKind.Chart } frame
            || SharedChartRenderer.ChartOf(frame, editor.Session.Presentation) is not { IsSupported: true } chart)
        {
            return null;
        }

        return (frame, chart);
    }
}

/// <summary>
/// Daten bearbeiten: the chart's values as a small table – the first row the series' names, the first column the categories, the
/// rest numbers with a decimal comma, columns separated by tabs (pasted from ThixCalc or Excel as they come).
/// </summary>
internal sealed class ChartDataDialog : OfficeDialog
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");
    private readonly ShowEditor _editor;
    private readonly GraphicFrame _frame;
    private readonly Chart _chart;

    private ChartDataDialog(ShowEditor editor, GraphicFrame frame, Chart chart)
        : base("Daten bearbeiten", 620)
    {
        _editor = editor;
        _frame = frame;
        _chart = chart;
        Table = new TextBox
        {
            AcceptsReturn = true,
            AcceptsTab = true,
            MinHeight = 220,
            FontFamily = new FontFamily("Consolas, Menlo, monospace"),
            Text = ToTable(chart),
        };
        Avalonia.Automation.AutomationProperties.SetName(Table, "Diagrammdaten");
        var hint = new TextBlock { Text = "Erste Zeile: Namen der Datenreihen · erste Spalte: Kategorien · Spalten durch Tabulator getrennt.", TextWrapping = TextWrapping.Wrap, Margin = new Avalonia.Thickness(0, 6, 0, 0) };
        hint.Classes.Add("muted");
        SetBody(new StackPanel { Children = { Field("Daten", Table), hint } });
    }

    public TextBox Table { get; }

    public static async Task ShowAsync(OfficeWindow window, ShowEditor? editor)
    {
        if (ShowChartCommands.SelectedChart(editor) is { } selected && editor is not null)
        {
            await new ChartDataDialog(editor, selected.Frame, selected.Chart).ShowAsync(window);
        }
    }

    /// <summary>The chart's series as a tab-separated table (categories down, series across).</summary>
    public static string ToTable(Chart chart)
    {
        var series = chart.AllSeries.ToList();
        var categories = series.FirstOrDefault()?.Categories?.Cache?.Select(value => value.Text ?? value.Number?.ToString(German) ?? "").ToList() ?? [];
        var rows = Math.Max(categories.Count, series.Select(entry => entry.Values.Cache?.Count ?? 0).DefaultIfEmpty(0).Max());
        var lines = new List<string> { string.Join('\t', [string.Empty, .. series.Select(entry => entry.Name?.Literal ?? (entry.Name?.Reference?.Cache is { Count: > 0 } names ? names[0].Text : null) ?? "")]) };
        for (var row = 0; row < rows; row++)
        {
            var cells = new List<string> { row < categories.Count ? categories[row] : "" };
            cells.AddRange(series.Select(entry => entry.Values.Cache is { } cache && row < cache.Count && cache[row].Number is { } number ? number.ToString(German) : ""));
            lines.Add(string.Join('\t', cells));
        }

        return string.Join(Environment.NewLine, lines);
    }

    /// <summary>A table back into series (empty cells are gaps); null when it holds no numbers.</summary>
    public static IReadOnlyList<SeriesSpec>? FromTable(string text)
    {
        var rows = text.Replace("\r", "", StringComparison.Ordinal).Split('\n').Where(line => line.Trim().Length > 0).Select(line => line.Split('\t')).ToList();
        if (rows.Count < 2 || rows[0].Length < 2)
        {
            return null;
        }

        var categories = rows.Skip(1).Select(row => row[0].Trim()).ToList();
        var specs = new List<SeriesSpec>();
        for (var column = 1; column < rows[0].Length; column++)
        {
            var values = rows.Skip(1).Select(row => column < row.Length && double.TryParse(row[column].Trim(), NumberStyles.Float, German, out var value) ? value : (double?)null).ToList();
            specs.Add(new SeriesSpec(ChartText.Of(rows[0][column].Trim()), ChartDataRef.LiteralText(categories), ChartDataRef.Literal(values)));
        }

        return specs.Any(spec => spec.Values.Cache!.Any(value => value.Number is not null)) ? specs : null;
    }

    protected override bool Apply()
    {
        if (FromTable(Table.Text ?? "") is not { } specs)
        {
            return false;
        }

        var template = ChartTemplates.Of(_chart) ?? ChartTemplates.Find("column")!;
        var chart = ChartFactory.Create(template, specs) with { Title = _chart.Title };
        _ = _editor.RunAsync("Daten bearbeiten", () => _editor.Session.SetChart(_frame.Id, chart));
        return true;
    }
}
