using System.Globalization;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;
using ThixWrite.Dialogs;

namespace ThixWrite.Editor;

/// <summary>
/// ThixWrite's commands of a window (the shell's – Neu, Speichern, Rückgängig, Zoom … – are there already). Each acts on the
/// window's <see cref="WriteEditor"/> at the time it runs (opening a file into the window brings a new editor) and reads its
/// state from the session's cached <see cref="EditingSession.GetFormatting"/>; mixed values show as unchecked / empty.
/// <para>
/// Shortcuts, as in the German Word where they do not collide: Strg+Umschalt+F/K/U fett/kursiv/unterstrichen with Strg+B/I/U as
/// aliases (Strg+B is therefore not Blocksatz: Blocksatz is Strg+J), Strg+L/E/R links/zentriert/rechts, Strg+F Suchen, Strg+H
/// Ersetzen (⌥⌘F on the Mac, where ⌘H hides the app), Strg+A, Strg+Eingabe Seitenumbruch, Strg+Leertaste Zeichenformat
/// löschen, Strg+Umschalt+V Nur Text einfügen, Alt+Umschalt+D Datum und Uhrzeit.
/// </para>
/// </summary>
public static class WriteCommands
{
    private const double PointsPerCm = 72 / 2.54;
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The line spacings of the menu (multiples), as Word offers them.</summary>
    public static IReadOnlyList<double> LineSpacings { get; } = [1, 1.15, 1.5, 2, 2.5, 3];

    /// <summary>The margin presets of Layout › Seitenränder (cm: top, bottom, left, right), as the German Word names them.</summary>
    public static IReadOnlyList<(string Id, string Label, double Top, double Bottom, double Left, double Right)> MarginPresets { get; } =
    [
        ("normal", "Normal", 2.5, 2, 2.5, 2.5),
        ("narrow", "Schmal", 1.27, 1.27, 1.27, 1.27),
        ("moderate", "Mittel", 2.54, 2.54, 1.91, 1.91),
        ("wide", "Breit", 2.54, 2.54, 5.08, 5.08),
    ];

    /// <summary>The paper sizes of Layout › Format (points, portrait).</summary>
    public static IReadOnlyList<(string Id, string Label, double Width, double Height)> PaperSizes { get; } =
    [
        ("a4", "A4 (21 × 29,7 cm)", 595.3, 841.9),
        ("a5", "A5 (14,8 × 21 cm)", 419.55, 595.3),
        ("a3", "A3 (29,7 × 42 cm)", 841.9, 1190.55),
        ("letter", "Letter (21,59 × 27,94 cm)", 612, 792),
        ("legal", "Legal (21,59 × 35,56 cm)", 612, 1008),
    ];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        SelectionFormatting? Format() => Editor()?.Session.GetFormatting();
        bool HasEditor() => Editor() is not null;
        void With(Action<EditingSession> action)
        {
            if (Editor() is { } editor)
            {
                action(editor.Session);
            }
        }

        var commands = window.Commands;

        // Zwischenablage
        commands.Add(new OfficeCommand(WriteCommandIds.Paste, "Einfügen", () => Editor()?.PasteAsync() ?? Task.CompletedTask)
        {
            Icon = "paste", Shortcut = "Ctrl+V", YieldsToTextField = true,
            Description = "Fügt den Inhalt der Zwischenablage an der Einfügemarke ein.", Keywords = ["zwischenablage", "einsetzen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PasteText, "Nur Text einfügen", () => Editor()?.PasteAsync(textOnly: true) ?? Task.CompletedTask)
        {
            Icon = "file-text", Shortcut = "Ctrl+Shift+V", YieldsToTextField = true,
            Description = "Fügt den Inhalt der Zwischenablage ohne Formatierung ein.", Keywords = ["unformatiert", "zwischenablage"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Cut, "Ausschneiden", () => Editor()?.CutAsync() ?? Task.CompletedTask)
        {
            Icon = "cut", Shortcut = "Ctrl+X", YieldsToTextField = true, CanExecute = () => Editor()?.Session.Selection.IsCollapsed == false,
            Description = "Entfernt die Auswahl und legt sie in die Zwischenablage.",
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Copy, "Kopieren", () => Editor()?.CopyAsync() ?? Task.CompletedTask)
        {
            Icon = "copy", Shortcut = "Ctrl+C", YieldsToTextField = true, CanExecute = () => Editor()?.Session.Selection.IsCollapsed == false,
            Description = "Legt die Auswahl in die Zwischenablage.",
        });

        // Schriftart
        commands.Add(new OfficeValueCommand(WriteCommandIds.FontName, "Schriftart", OfficeValueKind.Text,
            () => Format()?.FontFamily is { IsMixed: false } family ? family.Value : null,
            value =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    With(session => session.SetFontFamily(value.Trim()));
                }
            })
        {
            Icon = "type", Options = WriteFonts.Families, CanExecute = HasEditor,
            Description = "Wählt die Schriftart der Auswahl.", Keywords = ["font", "schrift"],
        });
        commands.Add(new OfficeValueCommand(WriteCommandIds.FontSize, "Schriftgrad", OfficeValueKind.Text,
            () => Format()?.FontSize is { IsMixed: false } size ? FormatSize(size.Value) : null,
            value => With(session => session.SetFontSize(ParseSize(value))))
        {
            Icon = "type", Options = [.. FontSizes.Standard.Select(FormatSize)], CanExecute = HasEditor,
            Description = "Legt die Größe der Schrift in Punkt fest.", Keywords = ["größe", "schriftgröße", "punkt"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.GrowFont, "Schriftgrad vergrößern", () => With(session => session.GrowFont()))
        {
            Icon = "font-grow", CanExecute = HasEditor,
            Description = "Macht die Schrift eine Stufe größer.", Keywords = ["größer", "schrift"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ShrinkFont, "Schriftgrad verkleinern", () => With(session => session.GrowFont(false)))
        {
            Icon = "font-shrink", CanExecute = HasEditor,
            Description = "Macht die Schrift eine Stufe kleiner.", Keywords = ["kleiner", "schrift"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ClearFormatting, "Formatierung löschen", () => With(session => session.ClearFormatting()))
        {
            Icon = "clear-format", Shortcut = "Ctrl+Space", CanExecute = HasEditor,
            Description = "Entfernt die Formatierung der Auswahl: Standardtext ohne Zeichenformat.", Keywords = ["zurücksetzen", "standard", "entfernen"],
        });
        AddToggle(commands, WriteCommandIds.Bold, "Fett", "bold", "Ctrl+Shift+F", "Ctrl+B", () => With(session => session.ToggleBold()), () => Format()?.Bold.Is(true) == true, HasEditor, "Formatiert die Auswahl fett.");
        AddToggle(commands, WriteCommandIds.Italic, "Kursiv", "italic", "Ctrl+Shift+K", "Ctrl+I", () => With(session => session.ToggleItalic()), () => Format()?.Italic.Is(true) == true, HasEditor, "Formatiert die Auswahl kursiv.");
        AddToggle(commands, WriteCommandIds.Underline, "Unterstrichen", "underline", "Ctrl+Shift+U", "Ctrl+U", () => With(session => session.ToggleUnderline()),
            () => Format()?.Underline is { IsMixed: false } underline && underline.Value != UnderlineStyle.None, HasEditor, "Unterstreicht die Auswahl.");
        AddToggle(commands, WriteCommandIds.Strikethrough, "Durchgestrichen", "strikethrough", null, null, () => With(session => session.ToggleStrikethrough()), () => Format()?.Strikethrough.Is(true) == true, HasEditor, "Streicht die Auswahl durch.");
        AddToggle(commands, WriteCommandIds.Subscript, "Tiefgestellt", "subscript", null, null, () => With(session => session.ToggleSubscript()), () => Format()?.Subscript.Is(true) == true, HasEditor, "Setzt die Auswahl klein unter die Grundlinie.");
        AddToggle(commands, WriteCommandIds.Superscript, "Hochgestellt", "superscript", null, null, () => With(session => session.ToggleSuperscript()), () => Format()?.Superscript.Is(true) == true, HasEditor, "Setzt die Auswahl klein über die Grundlinie.");
        commands.Add(new OfficeValueCommand(WriteCommandIds.FontColor, "Schriftfarbe", OfficeValueKind.Color,
            () => Editor()?.FontColor,
            value =>
            {
                if (Editor() is not { } editor)
                {
                    return;
                }

                editor.FontColor = value;
                editor.Session.SetTextColor(value?.TrimStart('#'));
            })
        {
            Icon = "font-colour", CanExecute = HasEditor,
            Description = "Färbt den Text der Auswahl.", Keywords = ["farbe", "textfarbe", "rot", "blau"],
        });
        commands.Add(new OfficeValueCommand(WriteCommandIds.Highlight, "Texthervorhebungsfarbe", OfficeValueKind.Color,
            () => Editor()?.HighlightColor,
            value =>
            {
                if (Editor() is not { } editor)
                {
                    return;
                }

                editor.HighlightColor = value;
                editor.Session.SetHighlight(DocumentPalette.HighlightOf(value));
            })
        {
            Icon = "highlight", CanExecute = HasEditor,
            Description = "Hebt die Auswahl wie mit einem Textmarker hervor.", Keywords = ["markieren", "textmarker", "gelb"],
        });

        // Absatz
        AddToggle(commands, WriteCommandIds.Bullets, "Aufzählungszeichen", "list", null, null, () => With(session => session.ToggleList(ListKind.Bullet)),
            () => Format()?.List is { IsMixed: false, Value: ListKind.Bullet }, HasEditor, "Macht die Absätze zu einer Aufzählung mit Punkten.", ["liste", "punkte", "aufzählung"]);
        AddToggle(commands, WriteCommandIds.Numbering, "Nummerierung", "list-ordered", null, null, () => With(session => session.ToggleList(ListKind.Numbered)),
            () => Format()?.List is { IsMixed: false, Value: ListKind.Numbered }, HasEditor, "Nummeriert die Absätze (1., 2., 3.).", ["liste", "nummern", "zahlen"]);
        commands.Add(new OfficeCommand(WriteCommandIds.DecreaseIndent, "Einzug verkleinern", () => With(session => session.DecreaseIndent()))
        {
            Icon = "outdent", CanExecute = HasEditor, Description = "Rückt die Absätze weniger weit ein (in Listen: eine Ebene höher).", Keywords = ["einzug", "links"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.IncreaseIndent, "Einzug vergrößern", () => With(session => session.IncreaseIndent()))
        {
            Icon = "indent", CanExecute = HasEditor, Description = "Rückt die Absätze weiter ein (in Listen: eine Ebene tiefer).", Keywords = ["einzug", "einrücken"],
        });
        AddToggle(commands, WriteCommandIds.AlignLeft, "Linksbündig", "align-left", "Ctrl+L", null, () => With(session => session.SetAlignment(ParagraphAlignment.Left)),
            () => Format()?.Alignment.Is(ParagraphAlignment.Left) == true, HasEditor, "Richtet die Absätze am linken Rand aus.", ["ausrichtung", "links"]);
        AddToggle(commands, WriteCommandIds.AlignCenter, "Zentriert", "align-center", "Ctrl+E", null, () => With(session => session.SetAlignment(ParagraphAlignment.Center)),
            () => Format()?.Alignment.Is(ParagraphAlignment.Center) == true, HasEditor, "Zentriert die Absätze.", ["ausrichtung", "mitte"]);
        AddToggle(commands, WriteCommandIds.AlignRight, "Rechtsbündig", "align-right", "Ctrl+R", null, () => With(session => session.SetAlignment(ParagraphAlignment.Right)),
            () => Format()?.Alignment.Is(ParagraphAlignment.Right) == true, HasEditor, "Richtet die Absätze am rechten Rand aus.", ["ausrichtung", "rechts"]);
        AddToggle(commands, WriteCommandIds.Justify, "Blocksatz", "align-justify", "Ctrl+J", null, () => With(session => session.SetAlignment(ParagraphAlignment.Justify)),
            () => Format()?.Alignment.Is(ParagraphAlignment.Justify) == true, HasEditor, "Verteilt den Text gleichmäßig zwischen den Rändern.", ["ausrichtung", "block"]);
        commands.Add(new OfficeValueCommand(WriteCommandIds.LineSpacing, "Zeilenabstand", OfficeValueKind.Choice,
            () => Format()?.LineSpacing is { IsMixed: false, Value: { Rule: LineSpacingRule.Multiple } spacing } ? spacing.Value.ToString(CultureInfo.InvariantCulture) : null,
            value =>
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var multiple))
                {
                    With(session => session.SetLineSpacing(new LineSpacing(LineSpacingRule.Multiple, multiple)));
                }
            })
        {
            Icon = "line-spacing", CanExecute = HasEditor, Options = [.. LineSpacings.Select(spacing => spacing.ToString(CultureInfo.InvariantCulture))],
            Description = "Legt den Abstand zwischen den Zeilen fest.", Keywords = ["zeilen", "abstand", "doppelt"],
        });
        foreach (var spacing in LineSpacings)
        {
            var value = spacing;
            commands.Add(new OfficeCommand(LineSpacingId(value), value.ToString("0.0#", German), () => With(session => session.SetLineSpacing(new LineSpacing(LineSpacingRule.Multiple, value))))
            {
                IsSearchable = false, CanExecute = HasEditor,
                IsChecked = () => Format()?.LineSpacing is { IsMixed: false, Value: { Rule: LineSpacingRule.Multiple } current } && Math.Abs(current.Value - value) < 0.01,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.ShowMarks, "Formatierungszeichen", () => ToggleMarks(Editor()))
        {
            Icon = "pilcrow", CanExecute = HasEditor, IsChecked = () => Editor()?.ShowMarks == true,
            Description = "Zeigt Absatzmarken, Leerzeichen und Tabstopps an – oder blendet sie aus.", Keywords = ["absatzmarke", "pilcrow", "steuerzeichen", "¶"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ShowMarksView, "Formatierungszeichen", () => ToggleMarks(Editor()))
        {
            Icon = "pilcrow", CanExecute = HasEditor, IsChecked = () => Editor()?.ShowMarks == true, IsSearchable = false,
        });

        // Formatvorlagen
        commands.Add(new OfficeValueCommand(WriteCommandIds.Styles, "Formatvorlagen", OfficeValueKind.Choice,
            () => Format()?.ParagraphStyleId is { IsMixed: false } style ? style.Value : null,
            value =>
            {
                if (!string.IsNullOrEmpty(value))
                {
                    With(session => session.ApplyStyle(value));
                }
            })
        {
            Icon = "template", CanExecute = HasEditor, Options = [.. WriteRibbon.StyleEntries.Select(entry => entry.Id)],
            Description = "Formatiert die Absätze mit einer Formatvorlage (Überschrift 1, Titel, Zitat …).", Keywords = ["überschrift", "titel", "vorlage", "stil"],
        });

        // Bearbeiten
        commands.Add(new OfficeCommand(WriteCommandIds.Find, "Suchen", () => Editor()?.FindBar.Open(replace: false))
        {
            Icon = "search", Shortcut = "Ctrl+F", CanExecute = HasEditor,
            Description = "Sucht Text im Dokument.", Keywords = ["finden", "text"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Replace, "Ersetzen", () => Editor()?.FindBar.Open(replace: true))
        {
            Icon = "replace", Shortcut = "Ctrl+H", MacShortcut = "Ctrl+Alt+F", CanExecute = HasEditor,
            Description = "Sucht Text und ersetzt ihn durch anderen.", Keywords = ["austauschen", "suchen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.SelectAll, "Alles markieren", () => With(session => session.SelectAll()))
        {
            Icon = "text-box", Shortcut = "Ctrl+A", YieldsToTextField = true, CanExecute = HasEditor,
            Description = "Markiert das ganze Dokument.", Keywords = ["auswahl", "alles", "markieren"],
        });

        // Einfügen
        commands.Add(new OfficeCommand(WriteCommandIds.PageBreak, "Seitenumbruch", () => With(session => session.InsertPageBreak()))
        {
            Icon = "page-break", Shortcut = "Ctrl+Enter", CanExecute = HasEditor,
            Description = "Beginnt an der Einfügemarke eine neue Seite.", Keywords = ["umbruch", "neue seite"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.InsertDateTime, "Datum und Uhrzeit", () =>
            With(session => session.PasteText(DateTime.Now.ToString("dd.MM.yyyy HH:mm", German))))
        {
            Icon = "clock", Shortcut = "Alt+Shift+D", CanExecute = HasEditor,
            Description = "Fügt das heutige Datum und die Uhrzeit an der Einfügemarke ein.", Keywords = ["datum", "uhrzeit", "heute", "einfügen", "zeit"],
        });

        // Layout
        AddPageSetupCommands(window, Editor, HasEditor);
        AddNumber(commands, WriteCommandIds.IndentLeft, "Einzug links", "indent", "Rückt die Absätze links ein (cm).",
            () => Format()?.IndentLeft, points => With(session => session.SetIndents(points, null, null)), PointsPerCm, HasEditor);
        AddNumber(commands, WriteCommandIds.IndentRight, "Einzug rechts", "outdent", "Rückt die Absätze rechts ein (cm).",
            () => Format()?.IndentRight, points => With(session => session.SetIndents(null, points, null)), PointsPerCm, HasEditor);
        AddNumber(commands, WriteCommandIds.SpacingBefore, "Abstand vor", "arrow-up", "Abstand über den Absätzen (Punkt).",
            () => Format()?.SpacingBefore, points => With(session => session.SetSpacing(points, null)), 1, HasEditor);
        AddNumber(commands, WriteCommandIds.SpacingAfter, "Abstand nach", "arrow-down", "Abstand unter den Absätzen (Punkt).",
            () => Format()?.SpacingAfter, points => With(session => session.SetSpacing(null, points)), 1, HasEditor);

        // Ansicht
        commands.Add(new OfficeCommand(WriteCommandIds.ZoomDialog, "Zoom", () => Editor() is { } editor ? ZoomDialog.ShowAsync(window, editor) : Task.CompletedTask)
        {
            Icon = "zoom-in", CanExecute = HasEditor, Description = "Wählt die Vergrößerung des Dokuments.", Keywords = ["vergrößerung", "prozent", "lupe"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ZoomPageWidth, "Seitenbreite", () => Editor()?.ZoomToPageWidth())
        {
            Icon = "columns", CanExecute = HasEditor, Description = "Zeigt die Seite so groß, dass sie die Fensterbreite füllt.", Keywords = ["zoom", "breite", "anpassen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ZoomWholePage, "Ganze Seite", () => Editor()?.ZoomToWholePage())
        {
            Icon = "file", CanExecute = HasEditor, Description = "Zeigt eine ganze Seite im Fenster.", Keywords = ["zoom", "seite", "anpassen"],
        });
    }

    /// <summary>The id of the command of one line spacing of the menu.</summary>
    public static string LineSpacingId(double spacing) => $"{WriteCommandIds.LineSpacing}.{spacing.ToString(CultureInfo.InvariantCulture)}";

    /// <summary>A font size as the German ribbon shows it ("10,5").</summary>
    public static string FormatSize(double size) => size.ToString("0.#", German);

    /// <summary>A typed font size ("10,5", "10.5", "12 pt"), held to Word's 1–1638 pt; refused with a message otherwise.</summary>
    public static double ParseSize(string? text)
    {
        var cleaned = (text ?? "").Replace("pt", "", StringComparison.OrdinalIgnoreCase).Trim().Replace(',', '.');
        if (!double.TryParse(cleaned, NumberStyles.Float, CultureInfo.InvariantCulture, out var size) || size < 1 || size > 1638)
        {
            throw new BusinessException("Bitte geben Sie einen Schriftgrad zwischen 1 und 1638 Punkt ein.", "FontSize");
        }

        return Math.Round(size * 2) / 2;
    }

    private static void ToggleMarks(WriteEditor? editor)
    {
        if (editor is not null)
        {
            editor.ShowMarks = !editor.ShowMarks;
        }
    }

    private static void AddPageSetupCommands(OfficeWindow window, Func<WriteEditor?> editor, Func<bool> hasEditor)
    {
        var commands = window.Commands;
        SectionProperties? Setup() => editor()?.Document.Content.FinalSection;
        void Change(Func<SectionProperties, SectionProperties> change) => editor()?.Session.SetPageSetup(change);

        commands.Add(new OfficeCommand(WriteCommandIds.PageSetup, "Seite einrichten", () => editor() is { } current ? PageSetupDialog.ShowAsync(window, current) : Task.CompletedTask)
        {
            Icon = "settings", CanExecute = hasEditor,
            Description = "Seitenränder, Ausrichtung und Papierformat des Dokuments.", Keywords = ["ränder", "papier", "format", "seite"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PageMargins, "Seitenränder", () => editor() is { } current ? PageSetupDialog.ShowAsync(window, current) : Task.CompletedTask)
        {
            Icon = "margins", CanExecute = hasEditor,
            Description = "Wählt die Ränder der Seiten.", Keywords = ["ränder", "rand", "seite"],
        });
        foreach (var preset in MarginPresets)
        {
            var (id, label, top, bottom, left, right) = preset;
            commands.Add(new OfficeCommand($"{WriteCommandIds.PageMargins}.{id}", label, () => Change(setup => setup with
            {
                MarginTop = top * PointsPerCm,
                MarginBottom = bottom * PointsPerCm,
                MarginLeft = left * PointsPerCm,
                MarginRight = right * PointsPerCm,
            }))
            {
                Icon = "margins", CanExecute = hasEditor, IsSearchable = false,
                Description = $"Oben {Cm(top)}, unten {Cm(bottom)}, links {Cm(left)}, rechts {Cm(right)}",
                IsChecked = () => Setup() is { } setup && Near(setup.MarginTop, top) && Near(setup.MarginBottom, bottom) && Near(setup.MarginLeft, left) && Near(setup.MarginRight, right),
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.PageMarginsCustom, "Benutzerdefinierte Seitenränder …", () => editor() is { } current ? PageSetupDialog.ShowAsync(window, current) : Task.CompletedTask)
        {
            Icon = "sliders", CanExecute = hasEditor, IsSearchable = false,
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PageOrientation, "Ausrichtung", () => Change(setup => setup.WithOrientation(setup.Orientation == PageOrientation.Portrait ? PageOrientation.Landscape : PageOrientation.Portrait)))
        {
            Icon = "orientation", CanExecute = hasEditor,
            Description = "Hochformat oder Querformat.", Keywords = ["hochformat", "querformat", "drehen"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Portrait, "Hochformat", () => Change(setup => setup.WithOrientation(PageOrientation.Portrait)))
        {
            Icon = "file", CanExecute = hasEditor, IsSearchable = false, IsChecked = () => Setup()?.Orientation == PageOrientation.Portrait,
        });
        commands.Add(new OfficeCommand(WriteCommandIds.Landscape, "Querformat", () => Change(setup => setup.WithOrientation(PageOrientation.Landscape)))
        {
            Icon = "orientation", CanExecute = hasEditor, IsSearchable = false, IsChecked = () => Setup()?.Orientation == PageOrientation.Landscape,
        });
        commands.Add(new OfficeCommand(WriteCommandIds.PageSize, "Format", () => editor() is { } current ? PageSetupDialog.ShowAsync(window, current, PageSetupDialog.PaperTab) : Task.CompletedTask)
        {
            Icon = "file-text", CanExecute = hasEditor,
            Description = "Wählt das Papierformat (A4, A5, Letter …).", Keywords = ["papier", "papierformat", "größe", "a4"],
        });
        foreach (var (id, label, width, height) in PaperSizes)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.PageSize}.{id}", label, () => Change(setup => WithPaper(setup, width, height)))
            {
                Icon = "file-text", CanExecute = hasEditor, IsSearchable = false,
                IsChecked = () => Setup() is { } setup && IsPaper(setup, width, height),
            });
        }
    }

    /// <summary>The page setup with a paper of <paramref name="width"/> × <paramref name="height"/> (portrait points) in its orientation.</summary>
    public static SectionProperties WithPaper(SectionProperties setup, double width, double height) =>
        setup.Orientation == PageOrientation.Landscape ? setup with { PageWidth = height, PageHeight = width } : setup with { PageWidth = width, PageHeight = height };

    /// <summary>Whether the page setup has that paper (either orientation).</summary>
    public static bool IsPaper(SectionProperties setup, double width, double height) =>
        Math.Abs(Math.Min(setup.PageWidth, setup.PageHeight) - width) < 2 && Math.Abs(Math.Max(setup.PageWidth, setup.PageHeight) - height) < 2;

    private static bool Near(double points, double centimetres) => Math.Abs(points - (centimetres * PointsPerCm)) < 1;

    private static string Cm(double centimetres) => $"{centimetres.ToString("0.##", German)} cm";

    private static void AddToggle(OfficeCommandRegistry commands, string id, string label, string icon, string? shortcut, string? alias,
        Action run, Func<bool> isChecked, Func<bool> canExecute, string description, IReadOnlyList<string>? keywords = null)
    {
        commands.Add(new OfficeCommand(id, label, run)
        {
            Icon = icon,
            Shortcut = shortcut,
            MacShortcut = alias,
            CanExecute = canExecute,
            IsChecked = isChecked,
            Description = description,
            Keywords = keywords ?? [],
        });
        if (alias is not null)
        {
            // The other key of the same command (Strg+B beside Strg+Umschalt+F; ⇧⌘F beside ⌘B on the Mac): not offered by the search.
            commands.Add(new OfficeCommand(id + ".alias", label, run) { Shortcut = alias, MacShortcut = shortcut, CanExecute = canExecute, IsSearchable = false });
        }
    }

    private static void AddNumber(OfficeCommandRegistry commands, string id, string label, string icon, string description,
        Func<FormatState<double>?> read, Action<double> apply, double pointsPerUnit, Func<bool> canExecute)
    {
        commands.Add(new OfficeValueCommand(id, label, OfficeValueKind.Number,
            () => read() is { IsMixed: false } state ? Math.Round(state.Value / pointsPerUnit, 2).ToString(CultureInfo.InvariantCulture) : null,
            value =>
            {
                if (double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var number))
                {
                    apply(Math.Max(0, number) * pointsPerUnit);
                }
            })
        {
            Icon = icon, CanExecute = canExecute, Description = description, Keywords = ["absatz", "einzug", "abstand"],
        });
    }
}
