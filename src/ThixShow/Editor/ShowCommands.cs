using System.Globalization;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;
using ThixShow.Dialogs;
using ThixShow.Show;

namespace ThixShow.Editor;

/// <summary>
/// ThixShow's commands of the Start tab, the clipboard, the slides, the text and the arrangement (the shell's – Neu, Speichern,
/// Rückgängig, Zoom … – are there already). Each acts on the window's <see cref="ShowEditor"/> when it runs and reads its state from
/// the session (the formatting of the selection, cached by the core).
/// <para>
/// Shortcuts of the German PowerPoint: Strg+C/X/V, Strg+D duplizieren, Strg+M neue Folie, Strg+Umschalt+F / K / U fett / kursiv /
/// unterstrichen (also Strg+B, Strg+I, Strg+U), Strg+L / E / R / J ausrichten, Strg+Umschalt+&gt; / &lt; Schrift größer / kleiner,
/// Strg+Leertaste Zeichenformat löschen, Strg+G gruppieren, Strg+Umschalt+G Gruppierung aufheben, Strg+A alles markieren,
/// Strg+F Suchen, Strg+H Ersetzen, Strg+K Link, F5 / Umschalt+F5 Bildschirmpräsentation, Alt+Umschalt+→/← Ebene, Entf löschen.
/// The Mac keeps ⌘ for Strg and uses ⇧⌘F5 / ⌘↩ for the show.
/// </para>
/// </summary>
public static class ShowCommands
{
    /// <summary>The line spacings of Start › Zeilenabstand (100 000 = single).</summary>
    public static IReadOnlyList<(string Id, string Label, int Percent)> LineSpacings { get; } =
    [
        ("1.0", "1,0", 100000), ("1.5", "1,5", 150000), ("2.0", "2,0", 200000), ("2.5", "2,5", 250000), ("3.0", "3,0", 300000),
    ];

    public static IReadOnlyList<string> FontSizes { get; } =
        ["8", "9", "10", "10,5", "11", "12", "14", "16", "18", "20", "24", "28", "32", "36", "40", "44", "48", "54", "60", "66", "72", "80", "88", "96"];

    public static void Add(OfficeWindow window)
    {
        ShowEditor? Editor() => window.Editor as ShowEditor;
        bool HasEditor() => Editor() is not null;
        bool HasSlide() => Editor()?.Session.CurrentSlide is not null;
        bool HasShapes() => Editor() is { } editor && !editor.Session.Selection.Shapes.IsEmpty;
        bool HasText() => Editor() is { } editor && (editor.Session.Selection.Text is not null || !editor.Session.Selection.Shapes.IsEmpty);
        SelectionFormatting? Format() => Editor() is { } editor && HasText() ? editor.Session.GetFormatting() : null;
        void Run(string label, Action<ShowEditor> action)
        {
            if (Editor() is { } editor)
            {
                _ = editor.RunAsync(label, () => action(editor));
            }
        }

        var commands = window.Commands;

        // ===== Zwischenablage =====
        commands.Add(new OfficeCommand(ShowCommandIds.Paste, "Einfügen", () => Editor()?.Clipboard.PasteAsync() ?? Task.CompletedTask)
        {
            Icon = "paste", Shortcut = "Ctrl+V", YieldsToTextField = true, CanExecute = HasEditor,
            Description = "Fügt den Inhalt der Zwischenablage ein: Folien, Formen, Text oder ein Bild.", Keywords = ["zwischenablage", "einsetzen"],
        });
        commands.Add(new OfficeCommand(ShowCommandIds.PasteText, "Nur den Text übernehmen", () => Editor()?.Clipboard.PasteAsync(textOnly: true) ?? Task.CompletedTask)
        {
            Icon = "file-text", Shortcut = "Ctrl+Shift+V", CanExecute = HasEditor, IsSearchable = true,
            Description = "Fügt nur den Text der Zwischenablage ein, ohne Formatierung.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Cut, "Ausschneiden", () => Editor()?.Clipboard.CopyAsync(cut: true) ?? Task.CompletedTask)
        {
            Icon = "cut", Shortcut = "Ctrl+X", YieldsToTextField = true, CanExecute = HasEditor,
            Description = "Verschiebt die Markierung an die Stelle, an der Sie sie einfügen.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Copy, "Kopieren", () => Editor()?.Clipboard.CopyAsync(cut: false) ?? Task.CompletedTask)
        {
            Icon = "copy", Shortcut = "Ctrl+C", YieldsToTextField = true, CanExecute = HasEditor,
            Description = "Legt die Markierung in die Zwischenablage – den Text auch für andere Programme.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Duplicate, "Duplizieren", () => Run("Duplizieren", editor =>
        {
            if (editor.FocusedArea == EditorArea.Slides || editor.Session.Selection.Shapes.IsEmpty)
            {
                editor.Session.DuplicateSlides(editor.SelectedSlideIds);
            }
            else
            {
                editor.Session.DuplicateShapes();
            }
        }))
        {
            Icon = "copy", Shortcut = "Ctrl+D", CanExecute = HasSlide,
            Description = "Legt eine Kopie der markierten Formen (oder Folien) gleich daneben an.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.SelectAll, "Alle auswählen", () => Run("Markieren", editor =>
        {
            if (editor.Session.Selection.Text is not null)
            {
                editor.Session.SelectAllText();
            }
            else if (editor.FocusedArea == EditorArea.Slides || editor.CurrentView == ShowView.Sorter)
            {
                editor.Session.SelectSlides(editor.Session.Presentation.Slides.Select(slide => slide.Id));
            }
            else
            {
                editor.Session.SelectAllShapes();
            }
        }))
        {
            Icon = "list-checks", Shortcut = "Ctrl+A", YieldsToTextField = true, CanExecute = HasSlide,
            Description = "Markiert alle Formen der Folie (im Text: den ganzen Text; in den Miniaturen: alle Folien).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Delete, "Löschen", () => Editor() is { } editor && editor.FocusedArea == EditorArea.Slides ? editor.DeleteSlidesAsync() : RunTask(Editor(), "Löschen", editor => editor.Session.DeleteShapes()))
        {
            Icon = "trash", CanExecute = () => HasShapes() || Editor()?.FocusedArea == EditorArea.Slides, IsSearchable = false,
            Description = "Löscht die markierten Formen oder Folien.",
        });

        // ===== Folien =====
        commands.Add(new OfficeCommand(ShowCommandIds.NewSlide, "Neue Folie", () => Run("Neue Folie", editor => editor.Session.AddSlide()))
        {
            Icon = "slide-add", Shortcut = "Ctrl+M", CanExecute = HasEditor,
            Description = "Fügt nach der aktuellen Folie eine neue Folie mit dem passenden Layout ein.", Keywords = ["folie hinzufügen"],
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.NewSlideLayout, "Neue Folie mit Layout", OfficeValueKind.Choice, () => null,
            value => Run("Neue Folie", editor => editor.Session.AddSlide(uint.TryParse(value, CultureInfo.InvariantCulture, out var id) ? id : null)))
        {
            Icon = "slide-add", CanExecute = HasEditor, IsSearchable = false,
            Description = "Fügt eine neue Folie mit dem gewählten Layout ein.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.Layout, "Layout", OfficeValueKind.Choice,
            () => Editor()?.Session.CurrentSlide?.LayoutId.ToString(CultureInfo.InvariantCulture),
            value => Run("Layout", editor =>
            {
                if (uint.TryParse(value, CultureInfo.InvariantCulture, out var id))
                {
                    editor.Session.ChangeLayout(editor.SelectedSlideIds, id);
                }
            }))
        {
            Icon = "template", CanExecute = HasSlide,
            Description = "Gibt den markierten Folien ein anderes Layout; ihr Inhalt wandert in die passenden Platzhalter.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.ResetSlide, "Zurücksetzen", () => Run("Zurücksetzen", editor => editor.Session.ResetSlides(editor.SelectedSlideIds)))
        {
            Icon = "restore", CanExecute = HasSlide,
            Description = "Setzt Position, Größe und Formatierung der Platzhalter auf die des Layouts zurück.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.DuplicateSlide, "Folie duplizieren", () => Run("Folie duplizieren", editor => editor.Session.DuplicateSlides(editor.SelectedSlideIds)))
        {
            Icon = "copy", CanExecute = HasSlide, Description = "Legt eine Kopie der markierten Folien direkt dahinter an.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.DeleteSlide, "Folie löschen", () => Editor()?.DeleteSlidesAsync() ?? Task.CompletedTask)
        {
            Icon = "trash", CanExecute = HasSlide, Description = "Löscht die markierten Folien.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.HideSlide, "Folie ausblenden", () => Run("Folie ausblenden", editor =>
        {
            var ids = editor.SelectedSlideIds;
            var hide = !ids.All(id => editor.Session.Presentation.FindSlide(id)?.Hidden == true);
            editor.Session.SetSlidesHidden(ids, hide);
        }))
        {
            Icon = "eye-off", CanExecute = HasSlide,
            IsChecked = () => Editor() is { } editor && editor.SelectedSlideIds.Count > 0 && editor.SelectedSlideIds.All(id => editor.Session.Presentation.FindSlide(id)?.Hidden == true),
            Description = "Blendet die markierten Folien in der Bildschirmpräsentation aus (sie bleiben in der Datei).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.AddSection, "Abschnitt hinzufügen", () => SectionDialogs.AddAsync(window, Editor()))
        {
            Icon = "list", CanExecute = HasSlide, Description = "Beginnt vor der aktuellen Folie einen neuen Abschnitt.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.RenameSection, "Abschnitt umbenennen", () => SectionDialogs.RenameAsync(window, Editor()))
        {
            Icon = "edit", CanExecute = () => Editor() is { } editor && editor.Session.Selection.CurrentSlide is { } id && editor.Session.Presentation.SectionOf(id) is not null,
            Description = "Gibt dem Abschnitt der aktuellen Folie einen neuen Namen.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.RemoveSection, "Abschnitt entfernen", () => Run("Abschnitt entfernen", editor =>
        {
            if (editor.Session.Selection.CurrentSlide is { } id && editor.Session.Presentation.SectionOf(id) is { } section)
            {
                editor.Session.RemoveSection(section.Id);
            }
        }))
        {
            Icon = "x", CanExecute = () => Editor() is { } editor && editor.Session.Selection.CurrentSlide is { } id && editor.Session.Presentation.SectionOf(id) is not null,
            Description = "Entfernt den Abschnitt der aktuellen Folie (die Folien bleiben).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Section, "Abschnitt", () => SectionDialogs.AddAsync(window, Editor()))
        {
            Icon = "list", CanExecute = HasSlide, IsSearchable = false, Description = "Abschnitte gliedern die Folien.",
        });

        // ===== Schriftart =====
        commands.Add(new OfficeValueCommand(ShowCommandIds.FontName, "Schriftart", OfficeValueKind.Text,
            () => Format()?.FontFamily is { IsMixed: false, Value: { } family } ? family : null,
            value => Run("Schriftart", editor =>
            {
                if (!string.IsNullOrWhiteSpace(value))
                {
                    editor.Session.SetFontFamily(value.Trim());
                }
            }))
        {
            Icon = "type", Options = OfficeFonts.Families, CanExecute = HasText, Shortcut = "Ctrl+Shift+A",
            Description = "Wählt eine Schriftart für den markierten Text.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.FontSize, "Schriftgrad", OfficeValueKind.Text,
            () => Format()?.FontSize is { IsMixed: false, Value: var size and > 0 } ? size.ToString("0.#", CultureInfo.GetCultureInfo("de-DE")) : null,
            value => Run("Schriftgrad", editor =>
            {
                if (double.TryParse(value?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var points) && points > 0)
                {
                    editor.Session.SetFontSize(points);
                }
            }))
        {
            Icon = "type", Options = FontSizes, CanExecute = HasText, Shortcut = "Ctrl+Shift+P",
            Description = "Ändert den Schriftgrad des markierten Texts.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.GrowFont, "Schriftgrad vergrößern", () => Run("Schriftgrad", editor => editor.Session.GrowFont(true)))
        {
            Icon = "font-grow", Shortcut = "Ctrl+Shift+OemPeriod", CanExecute = HasText, Description = "Vergrößert den Text um einen Schritt.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.ShrinkFont, "Schriftgrad verkleinern", () => Run("Schriftgrad", editor => editor.Session.GrowFont(false)))
        {
            Icon = "font-shrink", Shortcut = "Ctrl+Shift+OemComma", CanExecute = HasText, Description = "Verkleinert den Text um einen Schritt.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Bold, "Fett", () => Run("Fett", editor => editor.Session.ToggleBold()))
        {
            Icon = "bold", Shortcut = "Ctrl+Shift+F", CanExecute = HasText, IsChecked = () => Format()?.Bold.Is(true) == true,
            Description = "Formatiert den Text fett (auch Strg+B).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Italic, "Kursiv", () => Run("Kursiv", editor => editor.Session.ToggleItalic()))
        {
            Icon = "italic", Shortcut = "Ctrl+Shift+K", CanExecute = HasText, IsChecked = () => Format()?.Italic.Is(true) == true,
            Description = "Formatiert den Text kursiv (auch Strg+I).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Underline, "Unterstrichen", () => Run("Unterstrichen", editor => editor.Session.ToggleUnderline()))
        {
            Icon = "underline", Shortcut = "Ctrl+U", CanExecute = HasText, IsChecked = () => Format()?.Underline.Is(true) == true,
            Description = "Unterstreicht den Text.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Strikethrough, "Durchgestrichen", () => Run("Durchgestrichen", editor => editor.Session.ToggleStrikethrough()))
        {
            Icon = "strikethrough", CanExecute = HasText, IsChecked = () => Format()?.Strikethrough.Is(true) == true,
            Description = "Streicht den Text durch.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Superscript, "Hochgestellt", () => Run("Hochgestellt", editor => editor.Session.SetBaseline(30000)))
        {
            Icon = "superscript", Shortcut = "Ctrl+OemPlus", CanExecute = HasText, Description = "Stellt den Text klein über die Zeile.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Subscript, "Tiefgestellt", () => Run("Tiefgestellt", editor => editor.Session.SetBaseline(-25000)))
        {
            Icon = "subscript", Shortcut = "Ctrl+Shift+OemPlus", CanExecute = HasText, Description = "Stellt den Text klein unter die Zeile.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.FontColor, "Schriftfarbe", OfficeValueKind.Color, () => Editor()?.FontColor,
            value => Run("Schriftfarbe", editor =>
            {
                editor.FontColor = value;
                editor.Session.SetTextColor(ShowPalette.ToColorSpec(value));
            }))
        {
            Icon = "font-colour", CanExecute = HasText, Description = "Ändert die Farbe des Texts.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.Highlight, "Texthervorhebungsfarbe", OfficeValueKind.Color, () => Editor()?.HighlightColor,
            value => Run("Texthervorhebungsfarbe", editor =>
            {
                editor.HighlightColor = value;
                editor.Session.SetHighlight(ShowPalette.ToColorSpec(value));
            }))
        {
            Icon = "highlight", CanExecute = HasText, Description = "Hinterlegt den Text farbig wie mit einem Textmarker.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.ClearFormat, "Alle Formatierungen löschen", () => Run("Formatierung löschen", editor => editor.Session.ClearCharacterFormatting()))
        {
            Icon = "clear-format", Shortcut = "Ctrl+Space", CanExecute = HasText, Description = "Entfernt die Zeichenformatierung des markierten Texts.",
        });

        // ===== Absatz =====
        commands.Add(new OfficeCommand(ShowCommandIds.Bullets, "Aufzählungszeichen", () => Run("Aufzählungszeichen", editor => editor.Session.ToggleBullets()))
        {
            Icon = "list", CanExecute = HasText, IsChecked = () => Format()?.BulletKind.Is("bullet") == true,
            Description = "Beginnt die Absätze mit Aufzählungszeichen.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Numbering, "Nummerierung", () => Run("Nummerierung", editor => editor.Session.ToggleNumbering()))
        {
            Icon = "list-ordered", CanExecute = HasText, IsChecked = () => Format()?.BulletKind.Is("number") == true,
            Description = "Nummeriert die Absätze.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.DecreaseLevel, "Listenebene verringern", () => Run("Ebene", editor => editor.Session.HandleTab(shift: true)))
        {
            Icon = "outdent", Shortcut = "Alt+Shift+Left", CanExecute = () => Editor()?.Session.Selection.Text is not null,
            Description = "Rückt die Absätze eine Ebene nach links (wie Umschalt+Tab am Zeilenanfang).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.IncreaseLevel, "Listenebene erhöhen", () => Run("Ebene", editor => editor.Session.HandleTab(shift: false)))
        {
            Icon = "indent", Shortcut = "Alt+Shift+Right", CanExecute = () => Editor()?.Session.Selection.Text is not null,
            Description = "Rückt die Absätze eine Ebene nach rechts (wie Tab am Zeilenanfang).",
        });
        foreach (var (id, label, icon, alignment, shortcut) in new (string, string, string, TextAlignment, string)[]
                 {
                     (ShowCommandIds.AlignLeft, "Linksbündig", "align-left", TextAlignment.Left, "Ctrl+L"),
                     (ShowCommandIds.AlignCenter, "Zentriert", "align-center", TextAlignment.Center, "Ctrl+E"),
                     (ShowCommandIds.AlignRight, "Rechtsbündig", "align-right", TextAlignment.Right, "Ctrl+R"),
                     (ShowCommandIds.Justify, "Blocksatz", "align-justify", TextAlignment.Justify, "Ctrl+J"),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => Run(label, editor => editor.Session.SetAlignment(alignment)))
            {
                Icon = icon, Shortcut = shortcut, CanExecute = HasText, IsChecked = () => Format()?.Alignment.Is(alignment) == true,
                Description = $"Richtet die Absätze {label.ToLowerInvariant()} aus.",
            });
        }

        commands.Add(new OfficeCommand(ShowCommandIds.LineSpacing, "Zeilenabstand", () => Task.CompletedTask)
        {
            Icon = "line-spacing", CanExecute = HasText, IsSearchable = false, Description = "Legt den Abstand zwischen den Zeilen fest.",
        });
        foreach (var (spacingId, label, percent) in LineSpacings)
        {
            commands.Add(new OfficeCommand($"{ShowCommandIds.LineSpacing}.{spacingId}", label, () => Run("Zeilenabstand", editor => editor.Session.SetLineSpacing(TextSpacing.Percent(percent))))
            {
                CanExecute = HasText, IsSearchable = false, Description = $"Zeilenabstand {label}.",
            });
        }

        foreach (var (id, label, icon, anchor) in new (string, string, string, TextAnchor)[]
                 {
                     (ShowCommandIds.TextTop, "Oben", "arrow-up", TextAnchor.Top),
                     (ShowCommandIds.TextMiddle, "Mitte", "minus", TextAnchor.Center),
                     (ShowCommandIds.TextBottom, "Unten", "arrow-down", TextAnchor.Bottom),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => Run("Text ausrichten", editor => editor.Session.SetBodyProperties("Text ausrichten", body => body with { Anchor = anchor })))
            {
                Icon = icon, CanExecute = HasText, IsChecked = () => Format()?.Anchor.Is(anchor) == true, IsSearchable = false,
                Description = $"Setzt den Text {label.ToLowerInvariant()} in die Form.",
            });
        }

        commands.Add(new OfficeCommand(ShowCommandIds.AlignText, "Text ausrichten", () => Task.CompletedTask)
        {
            Icon = "align-center", CanExecute = HasText, Description = "Setzt den Text oben, in die Mitte oder unten in die Form.",
        });

        // ===== Bearbeiten =====
        commands.Add(new OfficeCommand(ShowCommandIds.Find, "Suchen", () => Editor()?.OpenFind(replace: false))
        {
            Icon = "search", Shortcut = "Ctrl+F", CanExecute = HasEditor, Description = "Sucht Text auf allen Folien.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Replace, "Ersetzen", () => Editor()?.OpenFind(replace: true))
        {
            Icon = "replace", Shortcut = "Ctrl+H", MacShortcut = "Ctrl+Alt+F", CanExecute = HasEditor, Description = "Ersetzt Text auf allen Folien.",
        });

        ShowDrawingCommands.Add(window, Editor, Run);
        ShowDesignCommands.Add(window, Editor, Run);
        ShowContextMenus.AddCommands(window, Editor, Run);
        ShowChartCommands.Add(window, Editor, Run);

        // ===== Bildschirmpräsentation =====
        commands.Add(new OfficeCommand(ShowCommandIds.StartShow, "Von Beginn an", () => Editor() is { } editor ? SlideShowWindow.StartAsync(window, editor, fromCurrent: false) : Task.CompletedTask)
        {
            Icon = "present", Shortcut = "F5", MacShortcut = "Ctrl+Shift+Enter", CanExecute = () => Editor()?.Session.Presentation.SlideCount > 0,
            Description = "Startet die Bildschirmpräsentation mit der ersten Folie.", Keywords = ["präsentieren", "vorführen"],
        });
        commands.Add(new OfficeCommand(ShowCommandIds.StartFromCurrent, "Ab aktueller Folie", () => Editor() is { } editor ? SlideShowWindow.StartAsync(window, editor, fromCurrent: true) : Task.CompletedTask)
        {
            Icon = "play", Shortcut = "Shift+F5", MacShortcut = "Ctrl+Enter", CanExecute = HasSlide,
            Description = "Startet die Bildschirmpräsentation mit der aktuellen Folie.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.PresenterView, "Referentenansicht verwenden", () => ShowSettings.UsePresenterView = !ShowSettings.UsePresenterView)
        {
            Icon = "monitor", IsChecked = () => ShowSettings.UsePresenterView,
            Description = "Zeigt auf dem zweiten Bildschirm die Referentenansicht mit Notizen, nächster Folie und Zeitgeber.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.LoopShow, "Wiederholen, bis „Esc“ gedrückt wird", () => ShowSettings.Loop = !ShowSettings.Loop)
        {
            Icon = "refresh", IsChecked = () => ShowSettings.Loop, Description = "Beginnt nach der letzten Folie wieder mit der ersten.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.ChooseScreen, "Bildschirm", OfficeValueKind.Choice,
            () => ShowSettings.AudienceScreen?.ToString(CultureInfo.InvariantCulture) ?? "auto",
            value => ShowSettings.AudienceScreen = int.TryParse(value, CultureInfo.InvariantCulture, out var index) ? index : null)
        {
            Icon = "tv", Description = "Wählt den Bildschirm, auf dem die Präsentation läuft (Automatisch: der zweite, wenn vorhanden).",
        });

        // ===== Ansicht =====
        foreach (var (id, label, icon, view) in new (string, string, string, ShowView)[]
                 {
                     (ShowCommandIds.ViewNormal, "Normal", "sidebar", ShowView.Normal),
                     (ShowCommandIds.ViewSorter, "Foliensortierung", "grid", ShowView.Sorter),
                     (ShowCommandIds.ViewReading, "Leseansicht", "book", ShowView.Reading),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => Editor()?.SetView(view))
            {
                Icon = icon, CanExecute = HasEditor, IsChecked = () => Editor()?.CurrentView == view, Description = $"Zeigt die Präsentation in der Ansicht „{label}“.",
            });
        }

        commands.Add(new OfficeCommand(ShowCommandIds.ToggleNotes, "Notizen", () =>
        {
            if (Editor() is { } editor)
            {
                editor.SetNotesVisible(!editor.NotesVisible);
            }
        })
        {
            Icon = "comment", CanExecute = HasEditor, IsChecked = () => Editor()?.NotesVisible == true, Description = "Blendet den Notizenbereich unter der Folie ein oder aus.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.SmartGuides, "Intelligente Führungslinien", () =>
        {
            if (Editor() is { } editor)
            {
                editor.SmartGuides = !editor.SmartGuides;
            }
        })
        {
            Icon = "ruler", CanExecute = HasEditor, IsChecked = () => Editor()?.SmartGuides == true,
            Description = "Zeigt beim Verschieben Linien, an denen Formen ausgerichtet einrasten.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Gridlines, "Am Raster ausrichten", () =>
        {
            if (Editor() is { } editor)
            {
                editor.SnapToGrid = !editor.SnapToGrid;
            }
        })
        {
            Icon = "grid", CanExecute = HasEditor, IsChecked = () => Editor()?.SnapToGrid == true,
            Description = "Lässt Formen beim Verschieben an einem Raster von 0,2 cm einrasten.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.FitToWindow, "An Fenster anpassen", () => Editor()?.FitToWindow())
        {
            Icon = "zoom-in", CanExecute = HasEditor, Description = "Zeigt die ganze Folie im Fenster.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Zoom, "Zoom", () => Editor()?.RequestZoomLevel())
        {
            Icon = "zoom-in", CanExecute = HasEditor, Description = "Wählt die Vergrößerung der Folie.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Print, "Drucken", () => window.ShowBackstage(Pages.ShowPrintPage.PageId))
        {
            Icon = "print", Shortcut = "Ctrl+P", CanExecute = HasEditor,
            Description = "Druckt Folien, Notizenseiten oder Handzettel.",
        });
    }

    /// <summary>
    /// The second places of commands in the ribbon (ids are unique in a definition): each alias runs, reads and enables like its
    /// command; Effektoptionen's entries apply one value of the direction commands. Called after every command exists.
    /// </summary>
    public static void AddAliases(OfficeWindow window)
    {
        var commands = window.Commands;
        foreach (var (alias, id) in new (string, string)[]
                 {
                     (ShowCommandIds.QuickStyles + ".format", ShowCommandIds.QuickStyles),
                     (ShowCommandIds.ShapeFill + ".format", ShowCommandIds.ShapeFill),
                     (ShowCommandIds.ShapeOutline + ".format", ShowCommandIds.ShapeOutline),
                     (ShowCommandIds.ShapeShadow + ".format", ShowCommandIds.ShapeShadow),
                     (ShowCommandIds.BringToFront + ".format", ShowCommandIds.BringToFront),
                     (ShowCommandIds.SendToBack + ".format", ShowCommandIds.SendToBack),
                     (ShowCommandIds.Group + ".format", ShowCommandIds.Group),
                     (ShowCommandIds.BringToFront + ".picture", ShowCommandIds.BringToFront),
                     (ShowCommandIds.SendToBack + ".picture", ShowCommandIds.SendToBack),
                     (ShowCommandIds.RotateRight + ".picture", ShowCommandIds.RotateRight),
                     (ShowCommandIds.BringToFront + ".chart", ShowCommandIds.BringToFront),
                     (ShowCommandIds.SendToBack + ".chart", ShowCommandIds.SendToBack),
                     (ShowContextMenus.Height + ".picture", ShowContextMenus.Height),
                     (ShowContextMenus.Width + ".picture", ShowContextMenus.Width),
                     (ShowCommandIds.AlignShapesLeft + ".menu", ShowCommandIds.AlignShapesLeft),
                     (ShowCommandIds.AlignShapesCenter + ".menu", ShowCommandIds.AlignShapesCenter),
                     (ShowCommandIds.AlignShapesRight + ".menu", ShowCommandIds.AlignShapesRight),
                     (ShowCommandIds.AlignShapesTop + ".menu", ShowCommandIds.AlignShapesTop),
                     (ShowCommandIds.AlignShapesMiddle + ".menu", ShowCommandIds.AlignShapesMiddle),
                     (ShowCommandIds.AlignShapesBottom + ".menu", ShowCommandIds.AlignShapesBottom),
                     (ShowCommandIds.DistributeHorizontally + ".menu", ShowCommandIds.DistributeHorizontally),
                     (ShowCommandIds.DistributeVertically + ".menu", ShowCommandIds.DistributeVertically),
                     (ShowCommandIds.RotateRight + ".menu", ShowCommandIds.RotateRight),
                     (ShowCommandIds.RotateLeft + ".menu", ShowCommandIds.RotateLeft),
                     (ShowCommandIds.FlipVertical + ".menu", ShowCommandIds.FlipVertical),
                     (ShowCommandIds.FlipHorizontal + ".menu", ShowCommandIds.FlipHorizontal),
                     (ShowContextMenus.DeleteRows + ".menu", ShowContextMenus.DeleteRows),
                     (ShowGalleries.NewSlideGallery, ShowCommandIds.NewSlide),
                     (ShowGalleries.NewSlideGallery + ".insert", ShowCommandIds.NewSlide),
                     (ShowGalleries.LayoutGallery, ShowCommandIds.Layout),
                     (ShowGalleries.ShapesGallery, ShowCommandIds.Shapes),
                     (ShowGalleries.ShapesGallery + ".insert", ShowCommandIds.Shapes),
                     (ShowGalleries.ShapesGallery + ".format", ShowCommandIds.Shapes),
                     (ShowGalleries.ShapesGallery + ".change", ShowCommandIds.ChangeShape),
                     (ShowGalleries.DesignGallery, ShowCommandIds.Design),
                     (ShowGalleries.TablePicker, ShowCommandIds.InsertTable),
                 })
        {
            if (commands.Find(id) is { } command)
            {
                commands.Add(Alias(alias, command));
            }
        }

        foreach (var (prefix, options) in new (string, IReadOnlyList<(string Id, string Label)>)[]
                 {
                     (ShowCommandIds.Transition, ShowDesignCommands.TransitionDirections),
                     (ShowCommandIds.Animation, [.. ShowDesignCommands.AnimationDirections.Select(entry => (entry.Id, entry.Label))]),
                 })
        {
            if (commands.Find(prefix + ".direction") is not OfficeValueCommand direction)
            {
                continue;
            }

            commands.Add(new OfficeCommand(prefix + ".options", "Effektoptionen", () => Task.CompletedTask)
            {
                Icon = "sliders", CanExecute = () => direction.IsEnabled || commands.Find(prefix + ".byParagraph")?.IsEnabled == true, IsSearchable = false, Description = direction.Description,
            });
            foreach (var (value, label) in options)
            {
                commands.Add(new OfficeCommand($"{prefix}.direction.{value}", label, () => direction.ApplyAsync(value))
                {
                    CanExecute = () => direction.IsEnabled, IsChecked = () => direction.Value == value, IsSearchable = false, Description = $"Effektoptionen: {label}.",
                });
            }
        }
    }

    private static OfficeCommand Alias(string id, OfficeCommand command) => command switch
    {
        OfficeValueCommand value => new OfficeValueCommand(id, value.Label, value.Kind, () => value.Value, chosen => value.ApplyAsync(chosen))
        {
            Icon = value.Icon, CanExecute = () => value.IsEnabled, Options = value.Options, Description = value.Description, IsSearchable = false,
        },
        _ => new OfficeCommand(id, command.Label, () => command.ExecuteAsync())
        {
            Icon = command.Icon, CanExecute = () => command.IsEnabled, IsChecked = command.IsChecked, Description = command.Description, IsSearchable = false,
        },
    };

    /// <summary>Runs a session command and returns its task (for commands whose execution is awaited).</summary>
    internal static Task RunTask(ShowEditor? editor, string label, Action<ShowEditor> action) =>
        editor is null ? Task.CompletedTask : editor.RunAsync(label, () => action(editor));
}
