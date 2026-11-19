using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Model;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixWrite.Editor;

/// <summary>
/// The ids of ThixWrite's commands. The ribbon names them all; a command the window does not have is left out of the ribbon
/// (no dead buttons): Format übertragen and Lineal come later. Value commands (<see cref="OfficeValueCommand"/>) are marked.
/// </summary>
public static class WriteCommandIds
{
    // Start – Zwischenablage
    public const string Paste = "edit.paste";
    public const string PasteText = "edit.pasteText";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string FormatPainter = "write.formatPainter";

    // Start – Schriftart
    public const string FontName = "write.fontName"; // value: the font family
    public const string FontSize = "write.fontSize"; // value: the size in pt as the German ribbon shows it ("10,5"; "10.5" is read too)
    public const string GrowFont = "write.growFont";
    public const string ShrinkFont = "write.shrinkFont";
    public const string ClearFormatting = "write.clearFormatting";
    public const string Bold = "write.bold";
    public const string Italic = "write.italic";
    public const string Underline = "write.underline";
    public const string Strikethrough = "write.strikethrough";
    public const string Subscript = "write.subscript";
    public const string Superscript = "write.superscript";
    public const string FontColor = "write.fontColor"; // value: #rrggbb or null (Automatisch)
    public const string Highlight = "write.highlight"; // value: #rrggbb of a marker colour or null (none)

    // Start – Absatz
    public const string Bullets = "write.bullets";
    public const string Numbering = "write.numbering";
    public const string DecreaseIndent = "write.decreaseIndent";
    public const string IncreaseIndent = "write.increaseIndent";
    public const string AlignLeft = "write.alignLeft";
    public const string AlignCenter = "write.alignCenter";
    public const string AlignRight = "write.alignRight";
    public const string Justify = "write.justify";
    public const string LineSpacing = "write.lineSpacing"; // value: the multiple ("1.15"); the menu's entries are write.lineSpacing.<multiple>
    public const string ShowMarks = "write.showMarks";

    // Start – Formatvorlagen
    public const string Styles = "write.styles"; // value: the id of the paragraph style

    // Start – Bearbeiten
    public const string Find = "edit.find";
    public const string Replace = "edit.replace";
    public const string SelectAll = "edit.selectAll";

    // Einfügen
    public const string PageBreak = "write.pageBreak";
    public const string InsertDateTime = "write.insertDateTime";

    // Layout – Seite einrichten
    public const string PageSetup = "write.pageSetup";
    public const string PageMargins = "write.pageMargins"; // the presets are write.pageMargins.<normal|narrow|moderate|wide>
    public const string PageMarginsCustom = "write.pageMargins.custom";
    public const string PageOrientation = "write.pageOrientation";
    public const string Portrait = "write.pageOrientation.portrait";
    public const string Landscape = "write.pageOrientation.landscape";
    public const string PageSize = "write.pageSize"; // the sizes are write.pageSize.<a4|a5|a3|letter|legal>

    // Layout – Absatz
    public const string IndentLeft = "write.indentLeft"; // value: cm, invariant
    public const string IndentRight = "write.indentRight"; // value: cm, invariant
    public const string SpacingBefore = "write.spacingBefore"; // value: pt, invariant
    public const string SpacingAfter = "write.spacingAfter"; // value: pt, invariant

    // Ansicht
    public const string Ruler = "write.ruler";
    public const string ShowMarksView = "write.showMarks.view";
    public const string ZoomDialog = "write.zoom";
    public const string ZoomPageWidth = "write.zoomPageWidth";
    public const string ZoomWholePage = "write.zoomWholePage";

    // Datei
    public const string ExportPdf = "file.exportPdf";
}

/// <summary>
/// ThixWrite's ribbon – Datei | Start | Einfügen | Layout | Ansicht, like Word – as a MatDesktop definition (data: the web
/// app can draw the same one). Every item's id is a command id (<see cref="WriteCommands"/>); labels, icons, shortcuts and
/// tooltips come from the commands; what has no command yet (Format übertragen, Lineal) is left out.
/// </summary>
public static class WriteRibbon
{
    /// <summary>The Formatvorlagen gallery: Word's quick styles with a sample of their look.</summary>
    public static IReadOnlyList<MatRibbonGalleryEntry> StyleEntries { get; } =
    [
        new(BuiltInStyles.Normal, "Standard") { Sample = "AaBbCcDd", SampleSize = 12 },
        new(BuiltInStyles.NoSpacing, "Kein Leerraum") { Sample = "AaBbCcDd", SampleSize = 12 },
        new(BuiltInStyles.Heading1, "Überschrift 1") { Sample = "AaBbCc", SampleSize = 15, SampleIsAccent = true },
        new(BuiltInStyles.Heading2, "Überschrift 2") { Sample = "AaBbCcD", SampleSize = 13, SampleIsAccent = true },
        new(BuiltInStyles.Heading3, "Überschrift 3") { Sample = "AaBbCcD", SampleSize = 12, SampleIsAccent = true },
        new(BuiltInStyles.Title, "Titel") { Sample = "AaBb", SampleSize = 20 },
        new(BuiltInStyles.Subtitle, "Untertitel") { Sample = "AaBbCcD", SampleSize = 12 },
        new(BuiltInStyles.Quote, "Zitat") { Sample = "AaBbCcD", SampleSize = 12, SampleIsItalic = true },
        new(BuiltInStyles.IntenseQuote, "Intensives Zitat") { Sample = "AaBbCcD", SampleSize = 12, SampleIsItalic = true, SampleIsAccent = true },
        new(BuiltInStyles.ListParagraph, "Listenabsatz") { Sample = "AaBbCcDd", SampleSize = 12 },
    ];

    /// <summary>The definition of one window (the colour palettes are those of its document's theme).</summary>
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var theme = (window.Document as WordDocument)?.Content.Theme ?? Theme.Office;
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add(HomeTab(theme), InsertTab(), LayoutTab(), ViewTab());
        return definition;
    }

    private static MatRibbonTab HomeTab(Theme theme) =>
        new MatRibbonTab("home", "Start").Add(
            new MatRibbonGroup("undo", "Rückgängig") { Icon = "undo" }.Add(
                Button(CommonCommandIds.Undo),
                Button(CommonCommandIds.Redo)),
            new MatRibbonGroup("clipboard", "Zwischenablage") { Icon = "paste", ShrinkOrder = 1 }.Add(
                new MatRibbonSplitButton(WriteCommandIds.Paste, "", menu: [Button(WriteCommandIds.PasteText)]) { Size = MatRibbonSize.Large },
                Button(WriteCommandIds.Cut),
                Button(WriteCommandIds.Copy),
                Toggle(WriteCommandIds.FormatPainter)),
            new MatRibbonGroup("font", "Schriftart") { Icon = "type", ShrinkOrder = 4 }.Add(
                new MatRibbonStack("font-rows",
                    new MatRibbonRow("font-row-1",
                        ComboBox(WriteCommandIds.FontName, width: 150, isEditable: true),
                        ComboBox(WriteCommandIds.FontSize, width: 64, isEditable: true),
                        Button(WriteCommandIds.GrowFont, MatRibbonSize.Small),
                        Button(WriteCommandIds.ShrinkFont, MatRibbonSize.Small),
                        Button(WriteCommandIds.ClearFormatting, MatRibbonSize.Small)),
                    new MatRibbonRow("font-row-2",
                        Toggle(WriteCommandIds.Bold, size: MatRibbonSize.Small),
                        Toggle(WriteCommandIds.Italic, size: MatRibbonSize.Small),
                        Toggle(WriteCommandIds.Underline, size: MatRibbonSize.Small),
                        Toggle(WriteCommandIds.Strikethrough, size: MatRibbonSize.Small),
                        Toggle(WriteCommandIds.Subscript, size: MatRibbonSize.Small),
                        Toggle(WriteCommandIds.Superscript, size: MatRibbonSize.Small),
                        new MatRibbonSeparator("font-separator"),
                        ColorPicker(WriteCommandIds.Highlight, DocumentPalette.HighlightColors, "Keine Farbe"),
                        ColorPicker(WriteCommandIds.FontColor, DocumentPalette.FontColors(theme))))),
            new MatRibbonGroup("paragraph", "Absatz") { Icon = "align-left", ShrinkOrder = 3 }.Add(
                new MatRibbonStack("paragraph-rows",
                    new MatRibbonRow("paragraph-row-1",
                        Toggle(WriteCommandIds.Bullets, size: MatRibbonSize.Small),
                        Toggle(WriteCommandIds.Numbering, size: MatRibbonSize.Small),
                        Button(WriteCommandIds.DecreaseIndent, MatRibbonSize.Small),
                        Button(WriteCommandIds.IncreaseIndent, MatRibbonSize.Small),
                        Toggle(WriteCommandIds.ShowMarks, size: MatRibbonSize.Small)),
                    new MatRibbonRow("paragraph-row-2",
                        Toggle(WriteCommandIds.AlignLeft, "align", MatRibbonSize.Small),
                        Toggle(WriteCommandIds.AlignCenter, "align", MatRibbonSize.Small),
                        Toggle(WriteCommandIds.AlignRight, "align", MatRibbonSize.Small),
                        Toggle(WriteCommandIds.Justify, "align", MatRibbonSize.Small),
                        new MatRibbonMenuButton(WriteCommandIds.LineSpacing, "", null,
                            [.. WriteCommands.LineSpacings.Select(spacing => Toggle(WriteCommands.LineSpacingId(spacing), "lineSpacing"))]) { Size = MatRibbonSize.Small }))),
            new MatRibbonGroup("styles", "Formatvorlagen") { Icon = "template", ShrinkOrder = 5 }.Add(
                StylesGallery()),
            new MatRibbonGroup("editing", "Bearbeiten") { Icon = "search" }.Add(
                Button(WriteCommandIds.Find),
                Button(WriteCommandIds.Replace),
                Button(WriteCommandIds.SelectAll)));

    private static MatRibbonTab InsertTab() =>
        new MatRibbonTab("insert", "Einfügen").Add(
            new MatRibbonGroup("pages", "Seiten") { Icon = "page-break" }.Add(
                Button(WriteCommandIds.PageBreak, MatRibbonSize.Large)),
            new MatRibbonGroup("text", "Text") { Icon = "clock" }.Add(
                Button(WriteCommandIds.InsertDateTime, MatRibbonSize.Large)));

    private static MatRibbonTab LayoutTab() =>
        new MatRibbonTab("layout", "Layout").Add(
            new MatRibbonGroup("page-setup", "Seite einrichten") { Icon = "margins" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.PageMargins, "", null,
                [
                    .. WriteCommands.MarginPresets.Select(preset => Toggle($"{WriteCommandIds.PageMargins}.{preset.Id}", "margins")),
                    new MatRibbonSeparator("margins-separator"),
                    Button(WriteCommandIds.PageMarginsCustom),
                ]) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(WriteCommandIds.PageOrientation, "", null,
                    Toggle(WriteCommandIds.Portrait, "orientation"),
                    Toggle(WriteCommandIds.Landscape, "orientation")) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(WriteCommandIds.PageSize, "", null,
                    [.. WriteCommands.PaperSizes.Select(size => Toggle($"{WriteCommandIds.PageSize}.{size.Id}", "paper"))]) { Size = MatRibbonSize.Large },
                Button(WriteCommandIds.PageSetup)),
            new MatRibbonGroup("layout-paragraph", "Absatz") { Icon = "indent" }.Add(
                new MatRibbonStack("indents",
                    Labelled(Number(WriteCommandIds.IndentLeft, 0, 50, 0.25, "cm", 2), "Einzug links"),
                    Labelled(Number(WriteCommandIds.IndentRight, 0, 50, 0.25, "cm", 2), "Einzug rechts")),
                new MatRibbonStack("spacing",
                    Labelled(Number(WriteCommandIds.SpacingBefore, 0, 1584, 6, "Pt."), "Abstand vor"),
                    Labelled(Number(WriteCommandIds.SpacingAfter, 0, 1584, 6, "Pt."), "Abstand nach"))));

    /// <summary>The Formatvorlagen gallery: three styles in the ribbon (Word's width at a normal window), all in its menu.</summary>
    private static MatRibbonGallery StylesGallery()
    {
        var gallery = Gallery(WriteCommandIds.Styles, StyleEntries);
        gallery.InlineCount = 3;
        gallery.EntryWidth = 90;
        return gallery;
    }

    /// <summary>A number field with its words at the left (like Word's "Links:" / "Vor:") instead of an icon.</summary>
    private static MatRibbonNumber Labelled(MatRibbonNumber number, string label)
    {
        number.Label = label;
        number.Icon = "";
        number.Width = 92;
        return number;
    }

    private static MatRibbonTab ViewTab() =>
        new MatRibbonTab("view", "Ansicht").Add(
            new MatRibbonGroup("show", "Anzeigen") { Icon = "ruler" }.Add(
                Toggle(WriteCommandIds.Ruler),
                Toggle(WriteCommandIds.ShowMarksView)),
            new MatRibbonGroup("zoom", "Zoom") { Icon = "zoom-in" }.Add(
                Button(WriteCommandIds.ZoomDialog, MatRibbonSize.Large),
                Button(CommonCommandIds.Zoom100, MatRibbonSize.Large),
                new MatRibbonStack("zoom-fit",
                    Button(WriteCommandIds.ZoomWholePage),
                    Button(WriteCommandIds.ZoomPageWidth)),
                new MatRibbonStack("zoom-steps",
                    Button(CommonCommandIds.ZoomIn),
                    Button(CommonCommandIds.ZoomOut))),
            new MatRibbonGroup("ribbon", "Menüband") { Icon = "chevron-up" }.Add(
                Toggle(CommonCommandIds.ToggleRibbon)));
}
