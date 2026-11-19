using System.Globalization;
using SortKey = ThixCalc.Core.Editing.SortKey;
using ThixCalc.Core.Editing;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using ThixCalc.Dialogs;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using HorizontalAlignment = ThixCalc.Core.Model.HorizontalAlignment;
using VerticalAlignment = ThixCalc.Core.Model.VerticalAlignment;

namespace ThixCalc.Editor;

/// <summary>
/// ThixCalc's commands of the Start tab, the clipboard, the view and the sheets (the shell's – Neu, Speichern, Rückgängig,
/// Zoom … – are there already). Each acts on the window's <see cref="CalcEditor"/> when it runs and reads its state from the
/// active cell's style (Excel's ribbon does the same). While a cell is being edited, commands that change cells first end the
/// input (as clicking the ribbon does in Excel).
/// <para>
/// Shortcuts of the German Excel: Strg+C/X/V, Strg+1 Zellen formatieren, Strg+Umschalt+F / K / U fett / kursiv /
/// unterstrichen (Strg+2 / 3 / 4 as Excel's aliases), Strg+5 durchgestrichen, Strg+U nach unten ausfüllen, Strg+R nach rechts
/// ausfüllen, Strg+Umschalt+4 Währung, Strg+Umschalt+5 Prozent, Strg+Umschalt+1 Zahl (#.##0,00), Strg+Umschalt+6
/// Wissenschaft, Strg+F Suchen, Strg+H Ersetzen (⌥⌘F on the Mac), F5 / Strg+G Gehe zu, Strg+A alles markieren,
/// Alt+Umschalt+0 AutoSumme, Strg+Bild ab / auf nächstes / vorheriges Blatt, Umschalt+F11 neues Blatt, Strg+9 / Strg+0 Zeilen
/// / Spalten ausblenden. In the grid: F2 bearbeiten, F4 $ in Bezügen, Entf Inhalte löschen, Strg+Leertaste / Umschalt+
/// Leertaste Spalte / Zeile markieren.
/// </para>
/// </summary>
public static class CalcCommands
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The formats of Start › Zahl's list, as German Excel names them (codes in the German notation).</summary>
    public static IReadOnlyList<(string Label, string? LocalCode)> NumberFormats { get; } =
    [
        ("Standard", null),
        ("Zahl", "0,00"),
        ("Währung", "#.##0,00 €"),
        ("Buchhaltung", "_-* #.##0,00 €_-;-* #.##0,00 €_-;_-* \"-\"?? €_-;_-@_-"),
        ("Datum, kurz", "TT.MM.JJJJ"),
        ("Datum, lang", "TTTT, T. MMMM JJJJ"),
        ("Uhrzeit", "hh:mm:ss"),
        ("Prozent", "0,00%"),
        ("Bruch", "# ?/?"),
        ("Wissenschaft", "0,00E+00"),
        ("Text", "@"),
    ];

    /// <summary>The entries of Start › Rahmen, in Excel's order.</summary>
    public static IReadOnlyList<(string Id, string Label, BorderPreset Preset)> BorderChoices { get; } =
    [
        ("bottom", "Rahmenlinie unten", BorderPreset.Bottom),
        ("top", "Rahmenlinie oben", BorderPreset.Top),
        ("left", "Rahmenlinie links", BorderPreset.Left),
        ("right", "Rahmenlinie rechts", BorderPreset.Right),
        ("none", "Kein Rahmen", BorderPreset.None),
        ("all", "Alle Rahmenlinien", BorderPreset.All),
        ("outside", "Rahmenlinien außen", BorderPreset.Outside),
        ("thickOutside", "Dicke Rahmenlinien außen", BorderPreset.ThickOutside),
        ("doubleBottom", "Doppelte Rahmenlinie unten", BorderPreset.DoubleBottom),
        ("thickBottom", "Dicke Rahmenlinie unten", BorderPreset.ThickBottom),
        ("topBottom", "Rahmenlinie oben und unten", BorderPreset.TopAndBottom),
        ("topThickBottom", "Rahmenlinie oben und dicke Rahmenlinie unten", BorderPreset.TopAndThickBottom),
        ("topDoubleBottom", "Rahmenlinie oben und doppelte Rahmenlinie unten", BorderPreset.TopAndDoubleBottom),
        ("inside", "Rahmenlinien innen", BorderPreset.Inside),
    ];

    /// <summary>The functions of AutoSumme's menu: canonical name and German label.</summary>
    public static IReadOnlyList<(string Function, string Label)> AutoSumFunctions { get; } =
    [
        ("SUM", "Summe"), ("AVERAGE", "Mittelwert"), ("COUNT", "Anzahl"), ("MAX", "Max"), ("MIN", "Min"),
    ];

    public static void Add(OfficeWindow window)
    {
        CalcEditor? Editor() => window.Editor as CalcEditor;
        CellStyle? Style() => Editor()?.Session.ActiveStyle;
        bool Ready() => Editor() is { Editing.IsEditing: false };
        bool HasEditor() => Editor() is not null;

        // Runs a session command after ending an input (a refused input keeps the cell open and the command does not run).
        void With(Action<EditingSession> action)
        {
            if (Editor() is not { } editor || !editor.Editing.Commit())
            {
                return;
            }

            action(editor.Session);
        }

        var commands = window.Commands;

        // ===== Zwischenablage =====
        commands.Add(new OfficeCommand(CalcCommandIds.Paste, "Einfügen", () => Editor()?.Clipboard.PasteAsync() ?? Task.CompletedTask)
        {
            Icon = "paste", Shortcut = "Ctrl+V", YieldsToTextField = true, CanExecute = HasEditor,
            Description = "Fügt den Inhalt der Zwischenablage in die markierten Zellen ein.", Keywords = ["zwischenablage", "einsetzen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Cut, "Ausschneiden", () => Editor()?.Clipboard.CopyAsync(cut: true) ?? Task.CompletedTask)
        {
            Icon = "cut", Shortcut = "Ctrl+X", YieldsToTextField = true, CanExecute = Ready,
            Description = "Verschiebt die markierten Zellen an die Stelle, an der Sie sie einfügen.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Copy, "Kopieren", () => Editor()?.Clipboard.CopyAsync(cut: false) ?? Task.CompletedTask)
        {
            Icon = "copy", Shortcut = "Ctrl+C", YieldsToTextField = true, CanExecute = Ready,
            Description = "Legt die markierten Zellen in die Zwischenablage – auch für andere Programme (Text und HTML-Tabelle).",
        });
        foreach (var (id, label, icon, mode, transpose) in new (string, string, string, PasteMode, bool)[]
                 {
                     (CalcCommandIds.PasteValues, "Werte einfügen", "file-text", PasteMode.Values, false),
                     (CalcCommandIds.PasteFormulas, "Formeln einfügen", "code", PasteMode.Formulas, false),
                     (CalcCommandIds.PasteFormats, "Formatierung einfügen", "format-painter", PasteMode.Formats, false),
                     (CalcCommandIds.PasteValuesAndFormats, "Werte und Zahlenformate einfügen", "percent", PasteMode.ValuesAndNumberFormats, false),
                     (CalcCommandIds.PasteNoBorders, "Ohne Rahmen einfügen", "borders", PasteMode.AllExceptBorders, false),
                     (CalcCommandIds.PasteTranspose, "Transponieren", "refresh", PasteMode.All, true),
                 })
        {
            var options = new PasteOptions(mode, transpose);
            commands.Add(new OfficeCommand(id, label, () => Editor()?.Clipboard.PasteAsync(options) ?? Task.CompletedTask)
            {
                Icon = icon, CanExecute = () => Ready() && Editor()!.Clipboard.HasOwnContent, IsSearchable = true,
                Description = $"Inhalte einfügen: {label}.", Keywords = ["inhalte einfügen", "einfügen", "werte"],
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.PasteText, "Nur Text einfügen", () => Editor()?.Clipboard.PasteAsync(textOnly: true) ?? Task.CompletedTask)
        {
            Icon = "file-text", Shortcut = "Ctrl+Shift+V", CanExecute = Ready,
            Description = "Fügt den Text der Zwischenablage ein – jede Zeile in eine Zeile, Tabulatoren trennen die Spalten.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FormatPainter, "Format übertragen", () => Editor()?.ToggleFormatPainter())
        {
            Icon = "format-painter", CanExecute = Ready, IsChecked = () => Editor()?.IsPainting == true,
            Description = "Übernimmt das Format der markierten Zelle; die nächste Markierung erhält es.", Keywords = ["pinsel", "format kopieren"],
        });

        // ===== Schriftart =====
        commands.Add(new OfficeValueCommand(CalcCommandIds.FontName, "Schriftart", OfficeValueKind.Text,
            () => Style()?.Font.Name,
            value =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    With(session => session.SetFont(value.Trim(), session.ActiveStyle.Font.Size));
                }
            })
        {
            Icon = "type", Options = CalcFonts.Families, CanExecute = Ready,
            Description = "Wählt die Schriftart der markierten Zellen.", Keywords = ["font", "schrift"],
        });
        commands.Add(new OfficeValueCommand(CalcCommandIds.FontSize, "Schriftgrad", OfficeValueKind.Text,
            () => Style() is { } style ? style.Font.Size.ToString("0.#", German) : null,
            value =>
            {
                if (double.TryParse(value?.Replace('.', ','), NumberStyles.Float, German, out var size) && size is >= 1 and <= 409)
                {
                    With(session => session.SetFont(session.ActiveStyle.Font.Name, size));
                }
            })
        {
            Icon = "type", Options = [.. EditingSession.FontSizeSteps.Select(size => size.ToString("0.#", German))], CanExecute = Ready,
            Description = "Legt die Größe der Schrift in Punkt fest.", Keywords = ["größe", "schriftgröße", "punkt"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.GrowFont, "Schriftgrad vergrößern", () => With(session => session.ChangeFontSize(grow: true)))
        {
            Icon = "font-grow", CanExecute = Ready, Keywords = ["größer"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ShrinkFont, "Schriftgrad verkleinern", () => With(session => session.ChangeFontSize(grow: false)))
        {
            Icon = "font-shrink", CanExecute = Ready, Keywords = ["kleiner"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Bold, "Fett", () => With(session => session.SetBold(!session.ActiveStyle.Font.Bold)))
        {
            Icon = "bold", Shortcut = "Ctrl+Shift+F", MacShortcut = "Ctrl+B", CanExecute = Ready, IsChecked = () => Style()?.Font.Bold == true,
            Description = "Formatiert die markierten Zellen fett.", Keywords = ["dick", "hervorheben"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Italic, "Kursiv", () => With(session => session.SetItalic(!session.ActiveStyle.Font.Italic)))
        {
            Icon = "italic", Shortcut = "Ctrl+Shift+K", MacShortcut = "Ctrl+I", CanExecute = Ready, IsChecked = () => Style()?.Font.Italic == true,
            Description = "Formatiert die markierten Zellen kursiv.", Keywords = ["schräg"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Underline, "Unterstrichen",
            () => With(session => session.SetUnderline(session.ActiveStyle.Font.Underline == UnderlineKind.None ? UnderlineKind.Single : UnderlineKind.None)))
        {
            Icon = "underline", Shortcut = "Ctrl+Shift+U", MacShortcut = "Ctrl+U", CanExecute = Ready, IsChecked = () => Style() is { } style && style.Font.Underline != UnderlineKind.None,
            Description = "Unterstreicht den Inhalt der markierten Zellen.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Strikethrough, "Durchgestrichen", () => With(session => session.SetStrikethrough(!session.ActiveStyle.Font.Strikethrough)))
        {
            Icon = "strikethrough", Shortcut = "Ctrl+5", CanExecute = Ready, IsChecked = () => Style()?.Font.Strikethrough == true,
            Description = "Streicht den Inhalt der markierten Zellen durch.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Borders, "Rahmen", () => With(session => session.SetBorderPreset(BorderPreset.Bottom)))
        {
            Icon = "borders", CanExecute = Ready,
            Description = "Zeichnet Rahmenlinien um die markierten Zellen.", Keywords = ["linie", "umrandung", "gitter"],
        });
        foreach (var (id, label, preset) in BorderChoices)
        {
            commands.Add(new OfficeCommand($"{CalcCommandIds.Borders}.{id}", label, () => With(session => session.SetBorderPreset(preset)))
            {
                Icon = preset == BorderPreset.None ? "x" : "borders", CanExecute = Ready, Keywords = ["rahmen", "linie"],
            });
        }

        commands.Add(new OfficeValueCommand(CalcCommandIds.FillColor, "Füllfarbe", OfficeValueKind.Color,
            () => Editor()?.FillColor,
            value =>
            {
                if (Editor() is { } editor)
                {
                    editor.FillColor = value;
                    With(session => session.SetFill(GridPalette.ToCalc(value)));
                }
            })
        {
            Icon = "fill", CanExecute = Ready, Description = "Färbt den Hintergrund der markierten Zellen.", Keywords = ["hintergrund", "farbe", "schattierung"],
        });
        commands.Add(new OfficeValueCommand(CalcCommandIds.FontColor, "Schriftfarbe", OfficeValueKind.Color,
            () => Editor()?.FontColor,
            value =>
            {
                if (Editor() is { } editor)
                {
                    editor.FontColor = value;
                    With(session => session.SetFontColor(GridPalette.ToCalc(value)));
                }
            })
        {
            Icon = "font-colour", CanExecute = Ready, Description = "Färbt den Text der markierten Zellen.", Keywords = ["farbe", "textfarbe"],
        });

        // ===== Ausrichtung =====
        void Vertical(string id, string label, string icon, VerticalAlignment alignment) =>
            commands.Add(new OfficeCommand(id, label, () => With(session => session.SetVerticalAlignment(alignment)))
            {
                Icon = icon, CanExecute = Ready, IsChecked = () => Style()?.Alignment.Vertical == alignment, Keywords = ["ausrichtung", "vertikal"],
            });
        Vertical(CalcCommandIds.AlignTop, "Oben ausrichten", "arrow-up", VerticalAlignment.Top);
        Vertical(CalcCommandIds.AlignMiddle, "Zentriert ausrichten", "minus", VerticalAlignment.Center);
        Vertical(CalcCommandIds.AlignBottom, "Unten ausrichten", "arrow-down", VerticalAlignment.Bottom);

        void Horizontal(string id, string label, string icon, HorizontalAlignment alignment) =>
            commands.Add(new OfficeCommand(id, label, () => With(session => session.SetHorizontalAlignment(Style()?.Alignment.Horizontal == alignment ? HorizontalAlignment.General : alignment)))
            {
                Icon = icon, CanExecute = Ready, IsChecked = () => Style()?.Alignment.Horizontal == alignment, Keywords = ["ausrichtung", "bündig"],
            });
        Horizontal(CalcCommandIds.AlignLeft, "Linksbündig", "align-left", HorizontalAlignment.Left);
        Horizontal(CalcCommandIds.AlignCenter, "Zentriert", "align-center", HorizontalAlignment.Center);
        Horizontal(CalcCommandIds.AlignRight, "Rechtsbündig", "align-right", HorizontalAlignment.Right);

        commands.Add(new OfficeCommand(CalcCommandIds.WrapText, "Textumbruch", () => With(session => session.SetWrapText(!session.ActiveStyle.Alignment.WrapText)))
        {
            Icon = "wrap-text", CanExecute = Ready, IsChecked = () => Style()?.Alignment.WrapText == true,
            Description = "Zeigt langen Text in mehreren Zeilen der Zelle.", Keywords = ["umbrechen", "mehrzeilig", "zeilenumbruch"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Merge, "Verbinden und zentrieren", () => With(session => session.Merge(center: true)))
        {
            Icon = "merge-cells", CanExecute = Ready, Keywords = ["verbinden", "zusammenführen", "zellen verbinden"],
            Description = "Verbindet die markierten Zellen zu einer und zentriert ihren Inhalt.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.MergeCenter, "Verbinden und zentrieren", () => With(session => session.Merge(center: true)))
        {
            Icon = "merge-cells", CanExecute = Ready, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.MergeAcross, "Verbinden über", () => With(session => session.Merge(across: true)))
        {
            Icon = "merge-cells", CanExecute = Ready, Keywords = ["zeilenweise verbinden"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.MergeCells, "Zellen verbinden", () => With(session => session.Merge()))
        {
            Icon = "merge-cells", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Unmerge, "Zellverbund aufheben", () => With(session => session.Unmerge()))
        {
            Icon = "split-cells", CanExecute = () => Ready() && Editor()!.Session.SelectedRanges.Any(range => Editor()!.Session.ActiveSheet.Merges.Intersecting(range).Any()),
            Keywords = ["trennen", "teilen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.IndentDecrease, "Einzug verkleinern", () => With(session => session.ChangeIndent(-1)))
        {
            Icon = "outdent", CanExecute = () => Ready() && Style()?.Alignment.Indent > 0,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.IndentIncrease, "Einzug vergrößern", () => With(session => session.ChangeIndent(1)))
        {
            Icon = "indent", CanExecute = Ready,
        });

        // ===== Zahl =====
        commands.Add(new OfficeValueCommand(CalcCommandIds.NumberFormat, "Zahlenformat", OfficeValueKind.Choice,
            () => Editor() is { } editor ? NumberFormatLabel(editor.Session) : null,
            value =>
            {
                var choice = NumberFormats.FirstOrDefault(format => format.Label == value);
                if (choice.Label is null)
                {
                    return;
                }

                With(session =>
                {
                    if (choice.LocalCode is null)
                    {
                        session.SetNumberFormat("General");
                    }
                    else
                    {
                        session.SetLocalNumberFormat(choice.LocalCode);
                    }
                });
            })
        {
            Icon = "percent", Options = [.. NumberFormats.Select(format => format.Label)], CanExecute = Ready,
            Description = "Wählt, wie Zahlen in den markierten Zellen erscheinen: Zahl, Währung, Datum, Prozent …", Keywords = ["format", "zahl", "datum", "währung"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Accounting, "Buchhaltungszahlenformat", () => With(session => session.SetLocalNumberFormat(NumberFormats[3].LocalCode!)))
        {
            Icon = "currency", Shortcut = "Ctrl+Shift+4", CanExecute = Ready, Keywords = ["währung", "euro", "geld"],
            Description = "Zeigt Zahlen als Beträge in Euro.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Percent, "Prozentformat", () => With(session => session.SetNumberFormat("0%")))
        {
            Icon = "percent", Shortcut = "Ctrl+Shift+5", CanExecute = Ready, Keywords = ["prozent", "anteil"],
            Description = "Zeigt Zahlen als Prozent (0,25 als 25 %).",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Thousands, "1.000er-Trennzeichen", () => With(session => session.SetNumberFormat("#,##0.00")))
        {
            Icon = "decimals-more", Shortcut = "Ctrl+Shift+1", CanExecute = Ready, Keywords = ["tausender", "punkt", "zahl"],
            Description = "Zeigt Zahlen mit Tausenderpunkt und zwei Dezimalstellen.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DecimalsMore, "Dezimalstelle hinzufügen", () => With(session => session.ChangeDecimals(1)))
        {
            Icon = "decimals-more", CanExecute = Ready, Keywords = ["nachkommastellen", "genauer"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DecimalsLess, "Dezimalstelle löschen", () => With(session => session.ChangeDecimals(-1)))
        {
            Icon = "decimals-less", CanExecute = Ready, Keywords = ["nachkommastellen", "runden"],
        });

        // ===== Zellen formatieren (Strg+1) =====
        commands.Add(new OfficeCommand(CalcCommandIds.FormatCells, "Zellen formatieren …", () => Editor() is { } editor && editor.Editing.Commit() ? FormatCellsDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "settings", Shortcut = "Ctrl+1", CanExecute = HasEditor,
            Description = "Zahlenformat, Ausrichtung, Schrift, Rahmen und Ausfüllen der markierten Zellen.", Keywords = ["format", "formatieren", "zahlenformat", "rahmen"],
        });

        // ===== Zellen =====
        commands.Add(new OfficeCommand(CalcCommandIds.Insert, "Einfügen", () => With(InsertBySelection)) { Icon = "row-insert", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.InsertCells, "Zellen einfügen …", () => Editor() is { } editor && editor.Editing.Commit() ? InsertDeleteCellsDialog.ShowAsync(window, editor, insert: true) : Task.CompletedTask)
        {
            Icon = "row-insert", CanExecute = Ready, Keywords = ["einfügen", "verschieben"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.InsertRows, "Blattzeilen einfügen", () => With(session => session.InsertRows(session.Selection.FirstRow, session.Selection.RowCount)))
        {
            Icon = "row-insert", CanExecute = Ready, Keywords = ["zeile einfügen", "neue zeile"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.InsertColumns, "Blattspalten einfügen", () => With(session => session.InsertColumns(session.Selection.FirstColumn, session.Selection.ColumnCount)))
        {
            Icon = "column-insert", CanExecute = Ready, Keywords = ["spalte einfügen", "neue spalte"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.InsertSheet, "Blatt einfügen", () => With(session => session.AddSheet(index: session.Workbook.ActiveSheetIndex)))
        {
            Icon = "file-new", Shortcut = "Shift+F11", CanExecute = Ready, Keywords = ["tabelle", "arbeitsblatt", "neues blatt"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Delete, "Löschen", () => With(DeleteBySelection)) { Icon = "trash", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.DeleteCells, "Zellen löschen …", () => Editor() is { } editor && editor.Editing.Commit() ? InsertDeleteCellsDialog.ShowAsync(window, editor, insert: false) : Task.CompletedTask)
        {
            Icon = "trash", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DeleteRows, "Blattzeilen löschen", () => With(session => session.DeleteRows(session.Selection.FirstRow, session.Selection.RowCount)))
        {
            Icon = "trash", CanExecute = Ready, Keywords = ["zeile löschen", "zeile entfernen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DeleteColumns, "Blattspalten löschen", () => With(session => session.DeleteColumns(session.Selection.FirstColumn, session.Selection.ColumnCount)))
        {
            Icon = "trash", CanExecute = Ready, Keywords = ["spalte löschen", "spalte entfernen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.DeleteSheet, "Blatt löschen", () => Editor() is { } editor ? editor.DeleteSheetAsync(editor.Session.Workbook.ActiveSheetIndex) : Task.CompletedTask)
        {
            Icon = "trash", CanExecute = () => Ready() && Editor()!.Session.Workbook.Sheets.Count(sheet => sheet.Visibility == SheetVisibility.Visible) > 1,
            Keywords = ["tabelle löschen", "arbeitsblatt entfernen"],
        });

        commands.Add(new OfficeCommand(CalcCommandIds.Format, "Format", () => { }) { Icon = "settings", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.RowHeight, "Zeilenhöhe …", () => AskSizeAsync(window, rows: true)) { Icon = "row-insert", CanExecute = Ready, Keywords = ["höhe"] });
        commands.Add(new OfficeCommand(CalcCommandIds.AutoFitRows, "Zeilenhöhe automatisch anpassen", () => With(session => session.AutoFitRows(session.Selection.FirstRow, session.Selection.LastRow, Editor()!.Metrics)))
        {
            Icon = "row-insert", CanExecute = Ready, Keywords = ["optimale höhe"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ColumnWidth, "Spaltenbreite …", () => AskSizeAsync(window, rows: false)) { Icon = "columns", CanExecute = Ready, Keywords = ["breite"] });
        commands.Add(new OfficeCommand(CalcCommandIds.AutoFitColumns, "Spaltenbreite automatisch anpassen", () => With(session => session.AutoFitColumns(session.Selection.FirstColumn, session.Selection.LastColumn, Editor()!.Metrics)))
        {
            Icon = "columns", CanExecute = Ready, Keywords = ["optimale breite", "anpassen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.HideRows, "Zeilen ausblenden", () => With(session => session.HideRows(session.Selection.FirstRow, session.Selection.LastRow)))
        {
            Icon = "eye-off", Shortcut = "Ctrl+9", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.HideColumns, "Spalten ausblenden", () => With(session => session.HideColumns(session.Selection.FirstColumn, session.Selection.LastColumn)))
        {
            Icon = "eye-off", Shortcut = "Ctrl+0", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.UnhideRows, "Zeilen einblenden", () => With(session => session.HideRows(Math.Max(0, session.Selection.FirstRow - 1), session.Selection.LastRow + 1, hidden: false)))
        {
            Icon = "eye", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.UnhideColumns, "Spalten einblenden", () => With(session => session.HideColumns(Math.Max(0, session.Selection.FirstColumn - 1), session.Selection.LastColumn + 1, hidden: false)))
        {
            Icon = "eye", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.RenameSheet, "Blatt umbenennen", () => Editor()?.Tabs.RenameActive())
        {
            Icon = "edit", CanExecute = Ready, Keywords = ["umbenennen", "name"],
        });

        // ===== Bearbeiten =====
        commands.Add(new OfficeCommand(CalcCommandIds.AutoSum, "AutoSumme", () => Editor()?.AutoSum("SUM"))
        {
            Icon = "sum", Shortcut = "Alt+Shift+D0", CanExecute = Ready,
            Description = "Summiert die Zahlen über oder links der Zelle – die Formel erscheint zum Bestätigen.", Keywords = ["summe", "addieren", "summieren"],
        });
        foreach (var (function, label) in AutoSumFunctions)
        {
            var name = function;
            commands.Add(new OfficeCommand($"{CalcCommandIds.AutoSum}.{function.ToLowerInvariant()}", label, () => Editor()?.AutoSum(name))
            {
                Icon = "sum", CanExecute = Ready, IsSearchable = false,
            });
        }

        commands.Add(new OfficeCommand(CalcCommandIds.Fill, "Füllbereich", () => With(session => session.FillDown())) { Icon = "arrow-down", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.FillDown, "Unten", () => With(session => session.FillDown()))
        {
            Icon = "arrow-down", Shortcut = "Ctrl+U", MacShortcut = "Ctrl+D", CanExecute = Ready, Keywords = ["ausfüllen", "nach unten", "kopieren"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FillRight, "Rechts", () => With(session => session.FillRight()))
        {
            Icon = "arrow-right", Shortcut = "Ctrl+R", CanExecute = Ready, Keywords = ["ausfüllen", "nach rechts"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Clear, "Löschen", () => With(session => session.ClearAll())) { Icon = "broom", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.ClearAll, "Alle löschen", () => With(session => session.ClearAll())) { Icon = "broom", CanExecute = Ready, Keywords = ["leeren", "entfernen"] });
        commands.Add(new OfficeCommand(CalcCommandIds.ClearFormats, "Formate löschen", () => With(session => session.ClearFormats())) { Icon = "clear-format", CanExecute = Ready, Keywords = ["formatierung entfernen"] });
        commands.Add(new OfficeCommand(CalcCommandIds.ClearContents, "Inhalte löschen", () => With(session => session.ClearContents())) { Icon = "trash", CanExecute = Ready, Keywords = ["leeren", "werte löschen"] });
        commands.Add(new OfficeCommand(CalcCommandIds.ClearComments, "Notizen löschen", () => With(session => ClearComments(session))) { Icon = "comment", CanExecute = Ready });
        commands.Add(new OfficeCommand(CalcCommandIds.ClearLinks, "Links entfernen", () => With(session => session.SetHyperlink(session.Selection, null))) { Icon = "link", CanExecute = Ready });

        commands.Add(new OfficeCommand(CalcCommandIds.SortFilter, "Sortieren und Filtern", () => With(session => SortSelection(session, descending: false))) { Icon = "sort", CanExecute = Ready, IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.SortAscending, "Von A bis Z sortieren", () => With(session => SortSelection(session, descending: false)))
        {
            Icon = "sort-asc", CanExecute = Ready, Keywords = ["aufsteigend", "sortieren"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.SortDescending, "Von Z bis A sortieren", () => With(session => SortSelection(session, descending: true)))
        {
            Icon = "sort-desc", CanExecute = Ready, Keywords = ["absteigend", "sortieren"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.SortCustom, "Benutzerdefiniertes Sortieren …", () => Editor() is { } editor && editor.Editing.Commit() ? SortDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "sort", CanExecute = Ready, Keywords = ["sortieren", "mehrere spalten"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Filter, "Filtern", () => With(session => session.ToggleAutoFilter()))
        {
            Icon = "filter", Shortcut = "Ctrl+Shift+L", CanExecute = Ready, IsChecked = () => Editor()?.Session.ActiveSheet.AutoFilter is not null,
            Description = "Schaltet die Filterpfeile in den Spaltenköpfen der Liste ein oder aus.", Keywords = ["autofilter", "filter"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FilterClear, "Filter löschen", () => With(session => session.ClearFilters()))
        {
            Icon = "x", CanExecute = () => Ready() && Editor()!.Session.ActiveSheet.AutoFilter is { Columns.Count: > 0 },
        });

        commands.Add(new OfficeCommand(CalcCommandIds.FindSelect, "Suchen und Auswählen", () => Editor()?.FindBar.Open(replace: false)) { Icon = "search", IsSearchable = false });
        commands.Add(new OfficeCommand(CalcCommandIds.Find, "Suchen", () => Editor()?.OpenFind(replace: false))
        {
            Icon = "search", Shortcut = "Ctrl+F", CanExecute = HasEditor, Keywords = ["finden", "suche"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Replace, "Ersetzen", () => Editor()?.OpenFind(replace: true))
        {
            Icon = "replace", Shortcut = "Ctrl+H", MacShortcut = "Ctrl+Alt+F", CanExecute = HasEditor, Keywords = ["austauschen"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.GoTo, "Gehe zu …", () => Editor()?.FocusNameBox())
        {
            Icon = "arrow-right", Shortcut = "F5", CanExecute = HasEditor, Keywords = ["springen", "zelle", "namenfeld"],
            Description = "Springt zu einer Zelle, einem Bereich oder einem Namen (über das Namenfeld).",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.SelectAll, "Alles markieren", () => With(session => session.SelectAll()))
        {
            Icon = "grid", Shortcut = "Ctrl+A", YieldsToTextField = true, CanExecute = HasEditor,
        });

        // ===== Ansicht =====
        commands.Add(new OfficeCommand(CalcCommandIds.Freeze, "Fenster fixieren", () => With(session => session.FreezePanes(FreezeTarget(session))))
        {
            Icon = "freeze", CanExecute = Ready, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FreezePanes, "Fenster fixieren", () => With(session => session.FreezePanes(FreezeTarget(session))))
        {
            Icon = "freeze", CanExecute = Ready, Keywords = ["fixieren", "einfrieren", "kopfzeile festhalten"],
            Description = "Hält die Zeilen über und die Spalten links der markierten Zelle beim Scrollen fest.",
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FreezeTopRow, "Oberste Zeile fixieren", () => With(session => session.FreezePanes(new CellAddress(1, 0))))
        {
            Icon = "freeze", CanExecute = Ready, Keywords = ["überschrift fixieren", "erste zeile"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.FreezeFirstColumn, "Erste Spalte fixieren", () => With(session => session.FreezePanes(new CellAddress(0, 1))))
        {
            Icon = "freeze", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.Unfreeze, "Fixierung aufheben", () => With(session => session.Unfreeze()))
        {
            Icon = "x", CanExecute = () => Ready() && Editor()!.Session.ActiveSheet.View.IsFrozen,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ShowGridLines, "Gitternetzlinien", () => Editor()?.SetView(view => view with { ShowGridLines = !view.ShowGridLines }))
        {
            Icon = "grid", CanExecute = HasEditor, IsChecked = () => Editor()?.Session.ActiveSheet.View.ShowGridLines == true, Keywords = ["gitter", "linien"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ShowHeadings, "Überschriften", () => Editor()?.SetView(view => view with { ShowHeaders = !view.ShowHeaders }))
        {
            Icon = "header", CanExecute = HasEditor, IsChecked = () => Editor()?.Session.ActiveSheet.View.ShowHeaders == true, Keywords = ["spaltenköpfe", "zeilennummern"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ShowFormulaBar, "Bearbeitungsleiste", () => Editor()?.ToggleFormulaBar())
        {
            Icon = "text-box", CanExecute = HasEditor, IsChecked = () => Editor()?.FormulaBar.IsVisible == true, Keywords = ["formelleiste"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ZoomDialog, "Zoom", () => Editor() is { } editor ? ZoomDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "zoom-in", CanExecute = HasEditor, Keywords = ["vergrößern", "verkleinern", "prozent"],
        });
        commands.Add(new OfficeCommand(CalcCommandIds.ZoomSelection, "Auswahl vergrößern", () => Editor()?.ZoomToSelection())
        {
            Icon = "zoom-in", CanExecute = HasEditor, Keywords = ["zoom auf auswahl"],
        });

        // ===== Blätter =====
        commands.Add(new OfficeCommand(CalcCommandIds.NextSheet, "Nächstes Blatt", () => With(session => session.ActivateSheet(NextVisible(session, 1))))
        {
            Icon = "chevron-right", Shortcut = "Ctrl+PageDown", MacShortcut = "Alt+Right", CanExecute = Ready,
        });
        commands.Add(new OfficeCommand(CalcCommandIds.PreviousSheet, "Vorheriges Blatt", () => With(session => session.ActivateSheet(NextVisible(session, -1))))
        {
            Icon = "chevron-left", Shortcut = "Ctrl+PageUp", MacShortcut = "Alt+Left", CanExecute = Ready,
        });

        CalcDataCommands.Add(window);
    }

    /// <summary>The label of the active cell's format in Start › Zahl's list (Benutzerdefiniert when it is none of them).</summary>
    internal static string NumberFormatLabel(EditingSession session)
    {
        var code = session.ActiveStyle.NumberFormat;
        if (NumberFormat.Parse(code).IsGeneral)
        {
            return "Standard";
        }

        foreach (var (label, local) in NumberFormats)
        {
            if (local is not null && string.Equals(FormatCodes.ToCanonical(local, session.Locale), code, StringComparison.Ordinal))
            {
                return label;
            }
        }

        var format = NumberFormat.Parse(code);
        return format.IsDateTime ? "Datum, kurz" : format.IsPercent ? "Prozent" : format.IsText ? "Text" : "Benutzerdefiniert";
    }

    /// <summary>Einfügen (the button): whole rows/columns when they are selected, else cells shifted down.</summary>
    private static void InsertBySelection(EditingSession session)
    {
        var range = session.Selection;
        if (range.IsWholeRows)
        {
            session.InsertRows(range.FirstRow, range.RowCount);
        }
        else if (range.IsWholeColumns)
        {
            session.InsertColumns(range.FirstColumn, range.ColumnCount);
        }
        else
        {
            session.InsertCells(range, CellShift.Down);
        }
    }

    private static void DeleteBySelection(EditingSession session)
    {
        var range = session.Selection;
        if (range.IsWholeRows)
        {
            session.DeleteRows(range.FirstRow, range.RowCount);
        }
        else if (range.IsWholeColumns)
        {
            session.DeleteColumns(range.FirstColumn, range.ColumnCount);
        }
        else
        {
            session.DeleteCells(range, CellShift.Up);
        }
    }

    private static void ClearComments(EditingSession session)
    {
        foreach (var cell in session.ActiveSheet.Comments.Keys.Where(cell => session.SelectedRanges.Any(range => range.Contains(cell))).ToList())
        {
            session.SetComment(cell, null);
        }
    }

    /// <summary>A–Z / Z–A: the current region (or the selection) by the active cell's column; a first row of texts over numbers is the header.</summary>
    internal static void SortSelection(EditingSession session, bool descending)
    {
        var range = session.Selection.IsSingleCell ? Navigation.CurrentRegion(session.ActiveSheet, session.ActiveCell) : session.Selection;
        var hasHeader = SortDialog.LooksLikeHeader(session.ActiveSheet, range);
        session.Sort(range, [new SortKey(session.ActiveCell.Column - range.FirstColumn, descending)], new SortOptions(hasHeader));
    }

    /// <summary>Fenster fixieren: at the active cell (A1 would freeze nothing: the middle of the view then, like Excel).</summary>
    private static CellAddress FreezeTarget(EditingSession session)
    {
        var cell = session.ActiveCell;
        var view = session.ActiveSheet.View;
        if (view.IsFrozen)
        {
            return default;
        }

        return cell == view.TopLeft ? new CellAddress(view.TopLeft.Row + 1, view.TopLeft.Column) : cell;
    }

    private static int NextVisible(EditingSession session, int step)
    {
        var sheets = session.Workbook.Sheets;
        var index = session.Workbook.ActiveSheetIndex;
        for (var count = 0; count < sheets.Count; count++)
        {
            index = (index + step + sheets.Count) % sheets.Count;
            if (sheets[index].Visibility == SheetVisibility.Visible)
            {
                return index;
            }
        }

        return session.Workbook.ActiveSheetIndex;
    }

    /// <summary>Zeilenhöhe / Spaltenbreite: asks for the value (pt or characters, German decimals).</summary>
    private static async Task AskSizeAsync(OfficeWindow window, bool rows)
    {
        if (window.Editor is not CalcEditor editor || !editor.Editing.Commit())
        {
            return;
        }

        var session = editor.Session;
        var sheet = session.ActiveSheet;
        var range = session.Selection;
        var current = rows ? sheet.Rows[range.FirstRow].Size ?? sheet.Rows.DefaultSize : sheet.Columns[range.FirstColumn].Size ?? sheet.Columns.DefaultSize;
        string? error = null;
        while (true)
        {
            var answer = await TextInputDialog.AskAsync(window, rows ? "Zeilenhöhe" : "Spaltenbreite", rows ? "Zeilenhöhe (Punkt)" : "Spaltenbreite (Zeichen)", current.ToString("0.##", German), error: error);
            if (answer is null)
            {
                return;
            }

            if (double.TryParse(answer.Replace('.', ','), NumberStyles.Float, German, out var value) && value >= 0 && value <= (rows ? 409 : 255))
            {
                await editor.RunAsync(rows ? "Zeilenhöhe" : "Spaltenbreite", () =>
                {
                    if (rows)
                    {
                        session.SetRowHeight(range.FirstRow, range.LastRow, value);
                    }
                    else
                    {
                        session.SetColumnWidth(range.FirstColumn, range.LastColumn, value);
                    }
                });
                return;
            }

            error = rows ? "Geben Sie eine Zahl zwischen 0 und 409 ein." : "Geben Sie eine Zahl zwischen 0 und 255 ein.";
        }
    }
}
