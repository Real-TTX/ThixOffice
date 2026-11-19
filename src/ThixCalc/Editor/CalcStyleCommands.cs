using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// Start › Formatvorlagen: Bedingte Formatierung (Regeln zum Hervorheben von Zellen – Größer als, Kleiner als, Zwischen, Gleich,
/// Textinhalt, Doppelte Werte –, Obere/untere Regeln, Datenbalken, Farbskalen, Regeln löschen, Regeln verwalten) and the
/// Zellenformatvorlagen gallery (Gut, Schlecht, Neutral, Überschriften, Ergebnis, Eingabe, Berechnung, Notiz, Akzent).
/// </summary>
public static class CalcStyleCommands
{
    /// <summary>The quick rules of the menu: id, label, kind, whether it asks for a value (and a second one).</summary>
    public static IReadOnlyList<(string Id, string Label, HighlightRule Rule, int Values)> Rules { get; } =
    [
        ("greater", "Größer als …", HighlightRule.GreaterThan, 1),
        ("less", "Kleiner als …", HighlightRule.LessThan, 1),
        ("between", "Zwischen …", HighlightRule.Between, 2),
        ("equal", "Gleich …", HighlightRule.EqualTo, 1),
        ("text", "Textinhalt …", HighlightRule.TextContains, 1),
        ("duplicates", "Doppelte Werte …", HighlightRule.Duplicates, 0),
        ("top10", "Obere 10 Elemente …", HighlightRule.Top10, 1),
        ("bottom10", "Untere 10 Elemente …", HighlightRule.Bottom10, 1),
        ("aboveAverage", "Über dem Durchschnitt …", HighlightRule.AboveAverage, 0),
        ("belowAverage", "Unter dem Durchschnitt …", HighlightRule.BelowAverage, 0),
    ];

    public static string RuleId(string id) => $"{CalcCommandIds.ConditionalFormatting}.{id}";

    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        void With(Action<EditingSession> action)
        {
            if (Editor() is { } editor && editor.Editing.Commit())
            {
                action(editor.Session);
            }
        }

        var commands = window.Commands;
        commands.Add(new OfficeCommand(CalcCommandIds.ConditionalFormatting, "Bedingte Formatierung", () => { })
        {
            Icon = "palette", CanExecute = Ready, IsSearchable = false,
        });
        foreach (var (id, label, rule, values) in Rules)
        {
            var kind = rule;
            var count = values;
            var title = label.TrimEnd(' ', '…');
            commands.Add(new OfficeCommand(RuleId(id), label, () => Editor() is { } editor && editor.Editing.Commit() ? ConditionalRuleDialog.ShowAsync(window, editor, kind, title, count) : Task.CompletedTask)
            {
                Icon = "palette", CanExecute = Ready, Keywords = ["bedingte formatierung", "hervorheben", "regel"],
                Description = $"Bedingte Formatierung: hebt Zellen hervor – {title}.",
            });
        }

        foreach (var (id, label, color) in GridPalette.DataBars)
        {
            var bar = CalcColor.FromHex(color);
            commands.Add(new OfficeCommand(RuleId("databar." + id), $"Datenbalken {label}", () => With(session => session.AddDataBar(bar)))
            {
                Icon = "bar-chart", CanExecute = Ready, Keywords = ["datenbalken", "balken", "bedingte formatierung"],
            });
        }

        foreach (var (id, label, low, middle, high) in GridPalette.ColorScales)
        {
            var (lowColor, middleColor, highColor) = (CalcColor.FromHex(low), middle is null ? (CalcColor?)null : CalcColor.FromHex(middle), CalcColor.FromHex(high));
            commands.Add(new OfficeCommand(RuleId("scale." + id), $"Farbskala {label}", () => With(session => session.AddColorScale(lowColor, middleColor, highColor)))
            {
                Icon = "palette", CanExecute = Ready, Keywords = ["farbskala", "heatmap", "bedingte formatierung"],
            });
        }

        commands.Add(new OfficeCommand(RuleId("clearSelection"), "Regeln in ausgewählten Zellen löschen", () => With(session => session.ClearConditionalFormats(wholeSheet: false)))
        {
            Icon = "x", CanExecute = () => Ready() && Editor()!.Session.ActiveSheet.ConditionalFormats.Count > 0,
        });
        commands.Add(new OfficeCommand(RuleId("clearSheet"), "Regeln im gesamten Blatt löschen", () => With(session => session.ClearConditionalFormats(wholeSheet: true)))
        {
            Icon = "trash", CanExecute = () => Ready() && Editor()!.Session.ActiveSheet.ConditionalFormats.Count > 0,
        });
        commands.Add(new OfficeCommand(RuleId("manage"), "Regeln verwalten …", () => Editor() is { } editor && editor.Editing.Commit() ? RulesManagerDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "list", CanExecute = Ready, Keywords = ["bedingte formatierung", "regeln"],
        });

        commands.Add(new OfficeValueCommand(CalcCommandIds.CellStyles, "Zellenformatvorlagen", OfficeValueKind.Choice, () => null, value =>
        {
            var style = GridPalette.CellStyles.FirstOrDefault(entry => entry.Id == value);
            if (style.Id is null)
            {
                return;
            }

            With(session => session.ApplyStyle(current => ApplyCellStyle(current, style), "Formatvorlage"));
        })
        {
            Icon = "template", Options = [.. GridPalette.CellStyles.Select(style => style.Id)], CanExecute = Ready,
            Description = "Gibt den Zellen ein fertiges Aussehen: Gut, Schlecht, Neutral, Überschrift, Ergebnis …", Keywords = ["formatvorlage", "stil", "zellenformat"],
        });
    }

    private static CellStyle ApplyCellStyle(CellStyle current, (string Id, string Label, string? Fill, string? Font, bool Bold, double? Size, string? Border) style)
    {
        if (style.Id == "normal")
        {
            return current with { Font = CellFont.Default, Fill = CellFill.None, Borders = CellBorders.None };
        }

        var result = current with
        {
            Fill = style.Fill is { } fill ? CellFill.Solid(CalcColor.FromHex(fill)) : CellFill.None,
            Font = current.Font with { Color = style.Font is { } font ? CalcColor.FromHex(font) : null, Bold = style.Bold, Size = style.Size ?? CellFont.Default.Size },
        };
        if (style.Border is { } border)
        {
            var edge = new BorderEdge(style.Id is "heading1" ? BorderStyle.Thick : style.Id is "heading2" ? BorderStyle.Medium : BorderStyle.Thin, CalcColor.FromHex(border));
            result = style.Id is "heading1" or "heading2" or "total"
                ? result with { Borders = CellBorders.None with { Bottom = style.Id == "total" ? new BorderEdge(BorderStyle.Double, CalcColor.FromHex(border)) : edge, Top = style.Id == "total" ? new BorderEdge(BorderStyle.Thin, CalcColor.FromHex(border)) : default } }
                : result with { Borders = new CellBorders { Top = edge, Bottom = edge, Left = edge, Right = edge } };
        }

        return result;
    }
}
