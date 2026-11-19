using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Documents;
using ThixShow.Core.Model;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixShow.Editor;

/// <summary>
/// ThixShow's ribbon – Datei | Start | Einfügen | Entwurf | Übergänge | Animationen | Bildschirmpräsentation | Ansicht, like the German
/// PowerPoint, plus the contextual tabs Formformat, Bildformat, Tabellenentwurf and Layout – as a MatDesktop definition. Every item's
/// id is a command id (<see cref="ShowCommandIds"/>); galleries with pictures are controls of their own (<see cref="ShowGalleries"/>).
/// Überprüfen is left out: ThixShow has no spelling yet (no dead buttons).
/// </summary>
public static class ShowRibbon
{
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var presentation = (window.Document as ShowDocument)?.Presentation ?? MasterFactory.CreatePresentation();
        var palette = ShowPalette.Colors(presentation);
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add(HomeTab(palette), InsertTab(), DesignTab(palette), TransitionsTab(), AnimationsTab(), SlideShowTab(), ViewTab(),
            ShapeFormatTab(palette), PictureFormatTab(), TableDesignTab(palette), TableLayoutTab());
        return definition;
    }

    private static MatRibbonTab HomeTab(IReadOnlyList<string> palette) =>
        new MatRibbonTab("home", "Start").Add(
            new MatRibbonGroup("clipboard", "Zwischenablage") { Icon = "paste", ShrinkOrder = 1 }.Add(
                new MatRibbonSplitButton(ShowCommandIds.Paste, "", menu: [Button(ShowCommandIds.PasteText)]) { Size = MatRibbonSize.Large, MinSize = MatRibbonSize.Large },
                Button(ShowCommandIds.Cut),
                Button(ShowCommandIds.Copy),
                Button(ShowCommandIds.Duplicate)),
            new MatRibbonGroup("slides", "Folien") { Icon = "slide-add", ShrinkOrder = 3 }.Add(
                new MatRibbonCustom(ShowGalleries.NewSlideGallery, "Neue Folie") { IsTall = true },
                new MatRibbonStack("slides-rows",
                    new MatRibbonCustom(ShowGalleries.LayoutGallery, "Layout"),
                    Button(ShowCommandIds.ResetSlide, MatRibbonSize.Medium),
                    new MatRibbonMenuButton(ShowCommandIds.Section, "", menu:
                    [
                        Button(ShowCommandIds.AddSection), Button(ShowCommandIds.RenameSection), Button(ShowCommandIds.RemoveSection),
                    ]))),
            new MatRibbonGroup("font", "Schriftart") { Icon = "type", ShrinkOrder = 5 }.Add(
                new MatRibbonStack("font-rows",
                    new MatRibbonRow("font-row-1",
                        ComboBox(ShowCommandIds.FontName, width: 136, isEditable: true),
                        ComboBox(ShowCommandIds.FontSize, width: 60, isEditable: true),
                        Button(ShowCommandIds.GrowFont, MatRibbonSize.Small),
                        Button(ShowCommandIds.ShrinkFont, MatRibbonSize.Small),
                        Button(ShowCommandIds.ClearFormat, MatRibbonSize.Small)),
                    new MatRibbonRow("font-row-2",
                        Toggle(ShowCommandIds.Bold, size: MatRibbonSize.Small),
                        Toggle(ShowCommandIds.Italic, size: MatRibbonSize.Small),
                        Toggle(ShowCommandIds.Underline, size: MatRibbonSize.Small),
                        Toggle(ShowCommandIds.Strikethrough, size: MatRibbonSize.Small),
                        Button(ShowCommandIds.Superscript, MatRibbonSize.Small),
                        Button(ShowCommandIds.Subscript, MatRibbonSize.Small),
                        new MatRibbonSeparator("font-separator"),
                        ColorPicker(ShowCommandIds.Highlight, palette, "Keine Farbe"),
                        ColorPicker(ShowCommandIds.FontColor, palette)))),
            new MatRibbonGroup("paragraph", "Absatz") { Icon = "align-left", ShrinkOrder = 4 }.Add(
                new MatRibbonStack("paragraph-rows",
                    new MatRibbonRow("paragraph-row-1",
                        Toggle(ShowCommandIds.Bullets, size: MatRibbonSize.Small),
                        Toggle(ShowCommandIds.Numbering, size: MatRibbonSize.Small),
                        Button(ShowCommandIds.DecreaseLevel, MatRibbonSize.Small),
                        Button(ShowCommandIds.IncreaseLevel, MatRibbonSize.Small),
                        new MatRibbonMenuButton(ShowCommandIds.LineSpacing, "", menu: [.. ShowCommands.LineSpacings.Select(spacing => Button($"{ShowCommandIds.LineSpacing}.{spacing.Id}"))]) { Size = MatRibbonSize.Small }),
                    new MatRibbonRow("paragraph-row-2",
                        Toggle(ShowCommandIds.AlignLeft, "align", MatRibbonSize.Small),
                        Toggle(ShowCommandIds.AlignCenter, "align", MatRibbonSize.Small),
                        Toggle(ShowCommandIds.AlignRight, "align", MatRibbonSize.Small),
                        Toggle(ShowCommandIds.Justify, "align", MatRibbonSize.Small),
                        new MatRibbonMenuButton(ShowCommandIds.AlignText, "", menu:
                        [
                            Toggle(ShowCommandIds.TextTop, "anchor"), Toggle(ShowCommandIds.TextMiddle, "anchor"), Toggle(ShowCommandIds.TextBottom, "anchor"),
                        ]) { Size = MatRibbonSize.Small }))),
            new MatRibbonGroup("drawing", "Zeichnen") { Icon = "shapes", ShrinkOrder = 2 }.Add(
                new MatRibbonCustom(ShowGalleries.ShapesGallery, "Formen") { IsTall = true },
                ArrangeMenu(),
                Gallery(ShowCommandIds.QuickStyles, [.. ShowDrawingCommands.QuickStyles.Select(style => new MatRibbonGalleryEntry(style.Id, style.Label) { Icon = "palette" })]),
                new MatRibbonStack("drawing-format",
                    ColorPicker(ShowCommandIds.ShapeFill, palette, "Keine Füllung"),
                    ColorPicker(ShowCommandIds.ShapeOutline, palette, "Keine Kontur"),
                    Toggle(ShowCommandIds.ShapeShadow))),
            new MatRibbonGroup("editing", "Bearbeiten") { Icon = "search", ShrinkOrder = 6 }.Add(
                new MatRibbonStack("editing-rows",
                    Button(ShowCommandIds.Find),
                    Button(ShowCommandIds.Replace),
                    Button(ShowCommandIds.SelectAll))));

    private static MatRibbonMenuButton ArrangeMenu() =>
        new(ShowCommandIds.Arrange, "", menu:
        [
            Button(ShowCommandIds.BringToFront), Button(ShowCommandIds.BringForward), Button(ShowCommandIds.SendBackward), Button(ShowCommandIds.SendToBack),
            new MatRibbonSeparator(),
            Button(ShowCommandIds.Group), Button(ShowCommandIds.Ungroup),
            new MatRibbonSeparator(),
            Button(ShowCommandIds.AlignShapesLeft), Button(ShowCommandIds.AlignShapesCenter), Button(ShowCommandIds.AlignShapesRight),
            Button(ShowCommandIds.AlignShapesTop), Button(ShowCommandIds.AlignShapesMiddle), Button(ShowCommandIds.AlignShapesBottom),
            Button(ShowCommandIds.DistributeHorizontally), Button(ShowCommandIds.DistributeVertically),
            new MatRibbonSeparator(),
            Button(ShowCommandIds.RotateRight), Button(ShowCommandIds.RotateLeft), Button(ShowCommandIds.FlipVertical), Button(ShowCommandIds.FlipHorizontal),
        ]) { Size = MatRibbonSize.Large };

    private static MatRibbonTab InsertTab() =>
        new MatRibbonTab("insert", "Einfügen").Add(
            new MatRibbonGroup("insert-slides", "Folien") { Icon = "slide-add" }.Add(
                new MatRibbonCustom(ShowGalleries.NewSlideGallery + ".insert", "Neue Folie") { IsTall = true }),
            new MatRibbonGroup("tables", "Tabellen") { Icon = "table" }.Add(
                new MatRibbonCustom(ShowGalleries.TablePicker, "Tabelle") { IsTall = true }),
            new MatRibbonGroup("images", "Bilder") { Icon = "image" }.Add(
                Button(ShowCommandIds.InsertPicture, MatRibbonSize.Large)),
            new MatRibbonGroup("illustrations", "Illustrationen") { Icon = "shapes" }.Add(
                new MatRibbonCustom(ShowGalleries.ShapesGallery + ".insert", "Formen") { IsTall = true }),
            new MatRibbonGroup("links", "Links") { Icon = "link" }.Add(
                Button(ShowCommandIds.InsertLink, MatRibbonSize.Large)),
            new MatRibbonGroup("text", "Text") { Icon = "text-box" }.Add(
                Button(ShowCommandIds.TextBox, MatRibbonSize.Large)),
            new MatRibbonGroup("symbols", "Symbole") { Icon = "symbol" }.Add(
                new MatRibbonMenuButton(ShowCommandIds.InsertSymbol, "", menu: [.. Enumerable.Range(0, ShowDrawingCommands.Symbols.Count).Select(index => Button($"{ShowCommandIds.InsertSymbol}.{index}"))]) { Size = MatRibbonSize.Large }));

    private static MatRibbonTab DesignTab(IReadOnlyList<string> palette) =>
        new MatRibbonTab("design", "Entwurf").Add(
            new MatRibbonGroup("themes", "Designs") { Icon = "palette" }.Add(
                new MatRibbonCustom(ShowGalleries.DesignGallery, "Designs") { IsTall = true }),
            new MatRibbonGroup("customize", "Anpassen") { Icon = "orientation" }.Add(
                new MatRibbonMenuButton(ShowCommandIds.SlideSize, "", menu:
                [
                    Toggle(ShowCommandIds.SlideSizeStandard, "size"), Toggle(ShowCommandIds.SlideSizeWidescreen, "size"), new MatRibbonSeparator(), Button(ShowCommandIds.SlideSizeCustom),
                ]) { Size = MatRibbonSize.Large },
                ColorPicker(ShowCommandIds.FormatBackground, palette, "Automatisch")));

    private static MatRibbonTab TransitionsTab() =>
        new MatRibbonTab("transitions", "Übergänge").Add(
            new MatRibbonGroup("preview", "Vorschau") { Icon = "play" }.Add(
                Button(ShowCommandIds.PreviewTransition, MatRibbonSize.Large)),
            new MatRibbonGroup("transition", "Übergang zu dieser Folie") { Icon = "transition" }.Add(
                new MatRibbonGallery(ShowCommandIds.Transition, "", "transition", ShowDesignCommands.Transitions.Select(entry => new MatRibbonGalleryEntry(entry.Id, entry.Label) { Icon = entry.Id == "none" ? "x" : "transition" })) { InlineCount = 6 },
                new MatRibbonMenuButton(ShowCommandIds.Transition + ".options", "Effektoptionen", "sliders", [.. ShowDesignCommands.TransitionDirections.Select(direction => Button($"{ShowCommandIds.Transition}.direction.{direction.Id}"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("timing", "Anzeigedauer") { Icon = "clock" }.Add(
                new MatRibbonStack("timing-1",
                    Number(ShowCommandIds.TransitionDuration, 0.01, 59, 0.25, "s", 2),
                    Button(ShowCommandIds.ApplyToAll)),
                new MatRibbonStack("timing-2",
                    Toggle(ShowCommandIds.AdvanceOnClick),
                    Number(ShowCommandIds.AdvanceAfter, 0, 86399, 1, "s", 2))));

    private static MatRibbonTab AnimationsTab() =>
        new MatRibbonTab("animations", "Animationen").Add(
            new MatRibbonGroup("animation-preview", "Vorschau") { Icon = "play" }.Add(
                Button(ShowCommandIds.PreviewAnimation, MatRibbonSize.Large)),
            new MatRibbonGroup("animation", "Animation") { Icon = "animation" }.Add(
                new MatRibbonGallery(ShowCommandIds.Animation, "", "animation",
                    [
                        new MatRibbonGalleryEntry("none", "Keine") { Icon = "x" },
                        .. ShowDesignCommands.AnimationGroups.SelectMany(group => group.Presets.Select(preset => new MatRibbonGalleryEntry(preset.ToString(), AnimationPresets.Label(preset))
                        {
                            Icon = group.Group switch { "Eingang" => "sparkles", "Hervorgehoben" => "star", "Beenden" => "logout", _ => "route" },
                        })),
                    ]) { InlineCount = 6 },
                new MatRibbonMenuButton(ShowCommandIds.Animation + ".options", "Effektoptionen", "sliders", [.. ShowDesignCommands.AnimationDirections.Select(direction => Button($"{ShowCommandIds.Animation}.direction.{direction.Id}"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("advanced", "Erweiterte Animation") { Icon = "list" }.Add(
                Toggle(ShowCommandIds.AnimationPane, size: MatRibbonSize.Large)),
            new MatRibbonGroup("animation-timing", "Anzeigedauer") { Icon = "clock" }.Add(
                new MatRibbonStack("animation-timing-rows",
                    ComboBox(ShowCommandIds.AnimationStart, width: 140),
                    Number(ShowCommandIds.AnimationDuration, 0.01, 59, 0.25, "s", 2),
                    Number(ShowCommandIds.AnimationDelay, 0, 59, 0.25, "s", 2))));

    private static MatRibbonTab SlideShowTab() =>
        new MatRibbonTab("slideShow", "Bildschirmpräsentation").Add(
            new MatRibbonGroup("start", "Bildschirmpräsentation starten") { Icon = "present" }.Add(
                Button(ShowCommandIds.StartShow, MatRibbonSize.Large),
                Button(ShowCommandIds.StartFromCurrent, MatRibbonSize.Large)),
            new MatRibbonGroup("setup", "Einrichten") { Icon = "settings" }.Add(
                new MatRibbonStack("setup-rows",
                    Toggle(ShowCommandIds.LoopShow))),
            new MatRibbonGroup("monitors", "Bildschirme") { Icon = "monitor" }.Add(
                new MatRibbonStack("monitor-rows",
                    ComboBox(ShowCommandIds.ChooseScreen, width: 170),
                    Toggle(ShowCommandIds.PresenterView))));

    private static MatRibbonTab ViewTab() =>
        new MatRibbonTab("view", "Ansicht").Add(
            new MatRibbonGroup("views", "Präsentationsansichten") { Icon = "sidebar" }.Add(
                Toggle(ShowCommandIds.ViewNormal, "view", MatRibbonSize.Large),
                Toggle(ShowCommandIds.ViewSorter, "view", MatRibbonSize.Large),
                Toggle(ShowCommandIds.ViewReading, "view", MatRibbonSize.Large)),
            new MatRibbonGroup("show", "Anzeigen") { Icon = "eye" }.Add(
                new MatRibbonStack("show-rows",
                    Toggle(ShowCommandIds.ToggleNotes),
                    Toggle(ShowCommandIds.SmartGuides),
                    Toggle(ShowCommandIds.Gridlines))),
            new MatRibbonGroup("zoom", "Zoom") { Icon = "zoom-in" }.Add(
                Button(ShowCommandIds.Zoom, MatRibbonSize.Large),
                Button(ShowCommandIds.FitToWindow, MatRibbonSize.Large)));

    private static MatRibbonTab ShapeFormatTab(IReadOnlyList<string> palette) =>
        new MatRibbonTab(ShowContextMenus.ShapeFormatTab, "Formformat") { Context = ShowContextMenus.ShapeContext }.Add(
            new MatRibbonGroup("insert-shapes", "Formen einfügen") { Icon = "shapes" }.Add(
                new MatRibbonCustom(ShowGalleries.ShapesGallery + ".format", "Formen") { IsTall = true },
                new MatRibbonCustom(ShowGalleries.ShapesGallery + ".change", "Form ändern") { IsTall = true },
                Button(ShowCommandIds.TextBox, MatRibbonSize.Large)),
            new MatRibbonGroup("shape-styles", "Formenarten") { Icon = "palette" }.Add(
                Gallery(ShowCommandIds.QuickStyles + ".format", [.. ShowDrawingCommands.QuickStyles.Select(style => new MatRibbonGalleryEntry(style.Id, style.Label) { Icon = "palette" })]),
                new MatRibbonStack("shape-style-rows",
                    ColorPicker(ShowCommandIds.ShapeFill + ".format", palette, "Keine Füllung"),
                    ColorPicker(ShowCommandIds.ShapeOutline + ".format", palette, "Keine Kontur"),
                    Toggle(ShowCommandIds.ShapeShadow + ".format"))),
            new MatRibbonGroup("arrange", "Anordnen") { Icon = "layers" }.Add(
                new MatRibbonStack("arrange-rows",
                    Button(ShowCommandIds.BringToFront + ".format"),
                    Button(ShowCommandIds.SendToBack + ".format"),
                    Button(ShowCommandIds.Group + ".format")),
                new MatRibbonMenuButton(ShowCommandIds.AlignShapes, "", menu:
                [
                    Button(ShowCommandIds.AlignShapesLeft + ".menu"), Button(ShowCommandIds.AlignShapesCenter + ".menu"), Button(ShowCommandIds.AlignShapesRight + ".menu"),
                    Button(ShowCommandIds.AlignShapesTop + ".menu"), Button(ShowCommandIds.AlignShapesMiddle + ".menu"), Button(ShowCommandIds.AlignShapesBottom + ".menu"),
                    Button(ShowCommandIds.DistributeHorizontally + ".menu"), Button(ShowCommandIds.DistributeVertically + ".menu"),
                ]),
                new MatRibbonMenuButton(ShowCommandIds.Rotate, "", menu:
                [
                    Button(ShowCommandIds.RotateRight + ".menu"), Button(ShowCommandIds.RotateLeft + ".menu"), Button(ShowCommandIds.FlipVertical + ".menu"), Button(ShowCommandIds.FlipHorizontal + ".menu"),
                ])),
            new MatRibbonGroup("size", "Größe") { Icon = "orientation" }.Add(
                new MatRibbonStack("size-rows",
                    Number(ShowContextMenus.Height, 0.01, 142, 0.1, "cm", 2),
                    Number(ShowContextMenus.Width, 0.01, 142, 0.1, "cm", 2))));

    private static MatRibbonTab PictureFormatTab() =>
        new MatRibbonTab(ShowContextMenus.PictureFormatTab, "Bildformat") { Context = ShowContextMenus.PictureContext }.Add(
            new MatRibbonGroup("accessibility", "Barrierefreiheit") { Icon = "accessibility" }.Add(
                Button(ShowContextMenus.AltText, MatRibbonSize.Large)),
            new MatRibbonGroup("picture-arrange", "Anordnen") { Icon = "layers" }.Add(
                new MatRibbonStack("picture-arrange-rows",
                    Button(ShowCommandIds.BringToFront + ".picture"),
                    Button(ShowCommandIds.SendToBack + ".picture"),
                    Button(ShowCommandIds.RotateRight + ".picture"))),
            new MatRibbonGroup("picture-size", "Größe") { Icon = "orientation" }.Add(
                new MatRibbonStack("picture-size-rows",
                    Number(ShowContextMenus.Height + ".picture", 0.01, 142, 0.1, "cm", 2),
                    Number(ShowContextMenus.Width + ".picture", 0.01, 142, 0.1, "cm", 2))));

    private static MatRibbonTab TableDesignTab(IReadOnlyList<string> palette) =>
        new MatRibbonTab(ShowContextMenus.TableDesignTab, "Tabellenentwurf") { Context = ShowContextMenus.TableContext }.Add(
            new MatRibbonGroup("table-options", "Optionen für Tabellenformat") { Icon = "check-square" }.Add(
                new MatRibbonStack("table-options-1", Toggle(ShowContextMenus.HeaderRow), Toggle(ShowContextMenus.TotalRow), Toggle(ShowContextMenus.BandedRows)),
                new MatRibbonStack("table-options-2", Toggle(ShowContextMenus.FirstColumn), Toggle(ShowContextMenus.LastColumn), Toggle(ShowContextMenus.BandedColumns))),
            new MatRibbonGroup("table-styles", "Tabellenformatvorlagen") { Icon = "fill" }.Add(
                ColorPicker(ShowContextMenus.Shading, palette, "Keine Füllung")));

    private static MatRibbonTab TableLayoutTab() =>
        new MatRibbonTab(ShowContextMenus.TableLayoutTab, "Layout") { Context = ShowContextMenus.TableContext }.Add(
            new MatRibbonGroup("rows-columns", "Zeilen und Spalten") { Icon = "row-insert" }.Add(
                new MatRibbonMenuButton(ShowContextMenus.DeleteRows + ".menu", "Löschen", "trash", Button(ShowContextMenus.DeleteRows), Button(ShowContextMenus.DeleteColumns), Button(ShowContextMenus.DeleteTable)) { Size = MatRibbonSize.Large },
                Button(ShowContextMenus.RowAbove, MatRibbonSize.Large),
                new MatRibbonStack("insert-rows", Button(ShowContextMenus.RowBelow), Button(ShowContextMenus.ColumnLeft), Button(ShowContextMenus.ColumnRight))),
            new MatRibbonGroup("merge", "Zusammenführen") { Icon = "merge-cells" }.Add(
                Button(ShowContextMenus.MergeCells, MatRibbonSize.Large),
                Button(ShowContextMenus.SplitCell, MatRibbonSize.Large)));
}
