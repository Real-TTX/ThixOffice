using ThixCalc.Core.Editing;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixCalc.Pages;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;

namespace ThixCalc.Editor;

/// <summary>
/// Seitenlayout's commands (Seitenränder, Ausrichtung, Format, Druckbereich, Drucktitel, Blattoptionen, An Format anpassen,
/// Seite einrichten) and Datei › Drucken (Strg+P) / Exportieren. They act on the active sheet's page setup, one undo step each.
/// </summary>
public static partial class CalcPageCommands
{
    private const double CentimetresPerInch = 2.54;

    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        PageSetup? Setup() => Editor()?.Session.ActiveSheet.PageSetup;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        void Change(Func<PageSetup, PageSetup> change, string label = "Seite einrichten")
        {
            if (Editor() is { } editor && editor.Editing.Commit())
            {
                editor.Session.SetPageSetup(change, label);
            }
        }

        var commands = window.Commands;
        commands.Add(new OfficeCommand(CalcCommandIds.Margins, "Seitenränder", () => { }) { Icon = "margins", CanExecute = Ready, IsSearchable = false });
        foreach (var preset in MarginPresets)
        {
            var (top, bottom, left, right, header, footer) = (preset.Top / CentimetresPerInch, preset.Bottom / CentimetresPerInch, preset.Left / CentimetresPerInch,
                preset.Right / CentimetresPerInch, preset.Header / CentimetresPerInch, preset.Footer / CentimetresPerInch);
            commands.Add(new OfficeCommand($"{CalcCommandIds.Margins}.{preset.Id}", preset.Label, () => Change(setup => setup with
            {
                TopMargin = top, BottomMargin = bottom, LeftMargin = left, RightMargin = right, HeaderMargin = header, FooterMargin = footer,
            }, "Seitenränder"))
            {
                Icon = "margins", CanExecute = Ready, IsChecked = () => Setup() is { } setup && Math.Abs(setup.TopMargin - top) < 0.01 && Math.Abs(setup.LeftMargin - left) < 0.01,
                Keywords = ["seitenränder", "rand"],
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.Orientation, "Ausrichtung", () => { }) { Icon = "orientation", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.Portrait, "Hochformat", () => Change(setup => setup with { Landscape = false }, "Ausrichtung"))
        {
            Icon = "file", CanExecute = Ready, IsChecked = () => Setup()?.Landscape == false, Keywords = ["hochformat", "ausrichtung"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Landscape, "Querformat", () => Change(setup => setup with { Landscape = true }, "Ausrichtung"))
        {
            Icon = "orientation", CanExecute = Ready, IsChecked = () => Setup()?.Landscape == true, Keywords = ["querformat", "ausrichtung", "quer drucken"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PaperSize, "Format", () => { }) { Icon = "file-text", CanExecute = Ready, IsSearchable = false });
        foreach (var size in PaperSizes)
        {
            var code = SheetPaper.All.First(paper => string.Equals(paper.Label, size.Id, StringComparison.OrdinalIgnoreCase)).Code;
            commands.Add(new OfficeCommand($"{CalcCommandIds.PaperSize}.{size.Id}", size.Label, () => Change(setup => setup with { PaperSize = code }, "Format"))
            {
                Icon = "file-text", CanExecute = Ready, IsChecked = () => Setup()?.PaperSize == code, Keywords = ["papier", "format", size.Id],
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.PrintArea, "Druckbereich", () => { }) { Icon = "print", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.SetPrintArea, "Druckbereich festlegen", () =>
        {
            if (Editor() is { } editor && editor.Editing.Commit())
            {
                editor.Session.SetPrintArea(editor.Session.Selection);
            }
        })
        {
            Icon = "print", CanExecute = Ready, Keywords = ["druckbereich", "nur auswahl drucken"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ClearPrintArea, "Druckbereich aufheben", () =>
        {
            if (Editor() is { } editor && editor.Editing.Commit())
            {
                editor.Session.SetPrintArea(null);
            }
        })
        {
            Icon = "x", CanExecute = () => Ready() && EditingSession.PrintAreaOf(Editor()!.Session.ActiveSheet) is not null,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PrintTitles, "Drucktitel", () => Editor() is { } editor && editor.Editing.Commit() ? PageSetupDialog.ShowAsync(window, editor, PageSetupDialog.SheetTab) : Task.CompletedTask)
        {
            Icon = "header", CanExecute = Ready, Keywords = ["wiederholungszeilen", "überschrift auf jeder seite", "titel"],
            Description = "Legt Zeilen fest, die auf jeder gedruckten Seite oben wiederholt werden.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PrintGridLines, "Gitternetzlinien drucken", () => Change(setup => setup with { PrintGridLines = !setup.PrintGridLines }, "Gitternetzlinien drucken"))
        {
            Icon = "grid", CanExecute = Ready, IsChecked = () => Setup()?.PrintGridLines == true,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PrintHeadings, "Überschriften drucken", () => Change(setup => setup with { PrintHeadings = !setup.PrintHeadings }, "Überschriften drucken"))
        {
            Icon = "header", CanExecute = Ready, IsChecked = () => Setup()?.PrintHeadings == true,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FitToPage, "Skalierung", () => { }) { Icon = "zoom-out", CanExecute = Ready, IsSearchable = false });
        foreach (var fit in FitChoices)
        {
            var (wide, tall) = (fit.Wide, fit.Tall);
            var none = fit.Id == "none";
            commands.Add(new OfficeCommand($"{CalcCommandIds.FitToPage}.{fit.Id}", fit.Label, () => Change(setup => none
                ? setup with { FitToPage = false, Scale = 100 }
                : setup with { FitToPage = true, FitToWidth = wide, FitToHeight = tall }, "Skalierung"))
            {
                Icon = "zoom-out", CanExecute = Ready, Keywords = ["auf eine seite", "anpassen", "skalierung"],
                IsChecked = () => Setup() is { } setup && (none ? !setup.FitToPage : setup.FitToPage && setup.FitToWidth == wide && setup.FitToHeight == tall),
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.PageSetup, "Seite einrichten …", () => Editor() is { } editor && editor.Editing.Commit() ? PageSetupDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "settings", CanExecute = Ready, Keywords = ["kopfzeile", "fußzeile", "ränder", "papier", "seitenlayout"],
            Description = "Papier, Ausrichtung, Ränder, Skalierung, Kopf- und Fußzeile und Druckoptionen des Blatts.",
        });

        commands.Add(new OfficeCommand(CalcCommandIds.Print, "Drucken", () =>
        {
            if (Editor()?.Editing.Commit() != false)
            {
                window.ShowBackstage(CalcPrintPage.PageId);
            }
        })
        {
            Icon = "print", Shortcut = "Ctrl+P",
            Description = "Druckt das Blatt oder die Mappe – mit Vorschau, Druckerwahl und Seitenauswahl.", Keywords = ["drucken", "ausdrucken", "drucker", "vorschau"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ExportPdf, "Als PDF exportieren", () => window.ShowBackstage(CalcExportPage.PageId))
        {
            Icon = "export", Description = "Speichert die Seiten des Blatts als PDF-Datei.", Keywords = ["pdf", "exportieren", "weitergeben"],
        });
    }
}
