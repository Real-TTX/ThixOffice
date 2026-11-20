using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
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
    public const string TablePicker = "write.tablePicker"; // the grid of the ribbon (a control of its own)
    public const string InsertTable = "write.insertTable";
    public const string InsertPicture = "write.insertPicture";
    public const string Header = "write.header"; // menu entries write.header.edit / .remove
    public const string Footer = "write.footer";
    public const string PageNumber = "write.pageNumber";
    public const string Hyperlink = "write.hyperlink";
    public const string HyperlinkRemove = "write.hyperlink.remove";
    public const string HyperlinkOpen = "write.hyperlink.open";
    public const string Symbol = "write.symbol"; // the menu's symbols are write.symbol.u<hex>
    public const string SymbolMore = "write.symbol.more";

    // Dialogs of Start
    public const string FontDialog = "write.fontDialog";
    public const string ParagraphDialog = "write.paragraphDialog";
    public const string TabsDialog = "write.tabsDialog"; // the places are write.pageNumber.<top|bottom>.<left|center|right>
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

    // Tabellenentwurf / Layout (contextual, while the caret is in a table)
    public const string TableBorders = "write.table.borders"; // the menu entries are write.table.borders.<bottom|top|…>
    public const string TableShading = "write.table.shading"; // value: #rrggbb or null (Keine Farbe)
    public const string TableSelect = "write.table.select";
    public const string TableSelectCell = "write.table.select.cell";
    public const string TableSelectColumn = "write.table.select.column";
    public const string TableSelectRow = "write.table.select.row";
    public const string TableSelectTable = "write.table.select.table";
    public const string TableDelete = "write.table.delete";
    public const string TableDeleteCells = "write.table.delete.cells";
    public const string TableDeleteColumns = "write.table.delete.columns";
    public const string TableDeleteRows = "write.table.delete.rows";
    public const string TableDeleteTable = "write.table.delete.table";
    public const string TableInsertAbove = "write.table.insertAbove";
    public const string TableInsertBelow = "write.table.insertBelow";
    public const string TableInsertLeft = "write.table.insertLeft";
    public const string TableInsertRight = "write.table.insertRight";
    public const string TableMerge = "write.table.merge";
    public const string TableSplit = "write.table.split";
    public const string TableDistributeRows = "write.table.distributeRows";
    public const string TableDistributeColumns = "write.table.distributeColumns";
    public const string TableAlign = "write.table.align"; // the nine are write.table.align.<topLeft|…|bottomRight>
    public const string TableRepeatHeader = "write.table.repeatHeader";

    // Bildformat (contextual, while a picture is selected)
    public const string PictureAltText = "write.picture.altText";
    public const string PictureChange = "write.picture.change";
    public const string PictureResetSize = "write.picture.resetSize";
    public const string PictureAlignLeft = "write.picture.alignLeft";
    public const string PictureAlignCenter = "write.picture.alignCenter";
    public const string PictureAlignRight = "write.picture.alignRight";
    public const string PictureHeight = "write.picture.height"; // value: cm, invariant
    public const string PictureWidth = "write.picture.width"; // value: cm, invariant
    public const string StyleFromSelection = "write.style.fromSelection";
    public const string StyleNew = "write.style.new";
    public const string StyleModify = "write.style.modify";
    public const string StylesPane = "write.stylesPane";
    public const string CoverPage = "write.coverPage"; // menu; entries write.coverPage.<band|sidebar|frame>
    public const string BlankPage = "write.blankPage";
    public const string DropCap = "write.dropCap"; // menu; entries write.dropCap.<none|drop>
    public const string Bookmark = "write.bookmark";
    public const string InsertShape = "write.insertShape"; // value: a preset; entries write.insertShape.<preset>
    public const string InsertTextBox = "write.insertTextBox";
    public const string ShapeFill = "write.shape.fill"; // value: #rrggbb or empty
    public const string ShapeLine = "write.shape.line"; // value: #rrggbb or empty
    public const string ShapeEditText = "write.shape.editText";
    public const string ShapeWrap = "write.shape.wrap"; // menu; entries write.shape.wrap.<choice>
    public const string ShapeHeight = "write.shape.height"; // value: cm
    public const string ShapeWidth = "write.shape.width"; // value: cm
    public const string PictureWrap = "write.picture.wrap"; // menu; entries write.picture.wrap.<inline|square|topBottom|behind|front>

    // Kopf- und Fußzeile (contextual, while one is edited)
    public const string HeaderFooterPage = "write.hf.page";
    public const string HeaderFooterPages = "write.hf.pages";
    public const string HeaderFooterDate = "write.hf.date";
    public const string HeaderFooterFirstPage = "write.hf.firstPage";
    public const string HeaderFooterEvenOdd = "write.hf.evenOdd";
    public const string HeaderFooterClose = "write.hf.close";

    // Referenzen
    public const string Toc = "write.toc"; // the menu: write.toc.insert, write.toc.remove
    public const string TocInsert = "write.toc.insert";
    public const string TocRemove = "write.toc.remove";
    public const string TocUpdate = "write.toc.update";
    public const string InsertFootnote = "write.footnote";
    public const string InsertEndnote = "write.endnote";
    public const string NextFootnote = "write.nextNote"; // the menu's entries are write.nextNote.<footnote|endnote>.<next|previous>

    // Überprüfen
    public const string Spelling = "write.spelling";
    public const string WordCount = "write.wordCount";
    public const string Language = "write.language"; // the menu's entries are write.language.<tag>
    public const string NewComment = "write.comment.new";
    public const string DeleteComment = "write.comment.delete"; // the menu: write.comment.delete.one, write.comment.delete.all
    public const string DeleteCommentOne = "write.comment.delete.one";
    public const string DeleteAllComments = "write.comment.delete.all";
    public const string PreviousComment = "write.comment.previous";
    public const string NextComment = "write.comment.next";
    public const string ShowComments = "write.comment.show";

    // Ansicht
    public const string NavigationPane = "write.navigationPane";
    public const string Ruler = "write.ruler";
    public const string ShowMarksView = "write.showMarks.view";
    public const string ZoomDialog = "write.zoom";
    public const string ZoomPageWidth = "write.zoomPageWidth";
    public const string ZoomWholePage = "write.zoomWholePage";

    // Layout › Spalten, Umbrüche; Entwurf; Groß-/Kleinschreibung; Änderungen; the keys of German Word
    public const string Columns = "write.columns";
    public const string Breaks = "write.breaks";
    public const string ColumnBreak = "write.columnBreak";
    public const string SectionBreak = "write.sectionBreak";
    public const string PageColor = "write.pageColor";
    public const string ChangeCase = "write.changeCase";
    public const string AcceptAllRevisions = "write.acceptAllRevisions";
    public const string RejectAllRevisions = "write.rejectAllRevisions";
    public const string KeyLineSpacing1 = "write.key.lineSpacing1";
    public const string KeyLineSpacing2 = "write.key.lineSpacing2";
    public const string KeyLineSpacing15 = "write.key.lineSpacing15";
    public const string KeyNormal = "write.key.normal";
    public const string KeyHeading1 = "write.key.heading1";
    public const string KeyHeading2 = "write.key.heading2";
    public const string KeyHeading3 = "write.key.heading3";
    public const string KeyIndent = "write.key.indent";
    public const string KeyOutdent = "write.key.outdent";

    // Datei
    public const string ExportPdf = "file.exportPdf";
    public const string Print = "file.print";
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

    /// <summary>
    /// The gallery of a document: Word's quick styles in Word's order and the document's own, each with a sample in its look (size,
    /// bold, italic, a coloured style in the accent).
    /// </summary>
    public static IReadOnlyList<MatRibbonGalleryEntry> StyleEntriesOf(EditingSession session)
    {
        var entries = new List<MatRibbonGalleryEntry>();
        var normal = session.Document.Styles.DefaultParagraphStyle?.Id ?? BuiltInStyles.Normal;
        foreach (var style in session.GalleryStyles())
        {
            var format = style.Type == StyleType.Character
                ? session.Resolver.ResolveCharacter(normal, new CharacterProperties { StyleId = style.Id })
                : session.Resolver.ResolveCharacter(session.Document.Styles.Find(style.Id) is null ? normal : style.Id, CharacterProperties.Empty);
            var size = style.Id switch
            {
                BuiltInStyles.Title => 20,
                _ => Math.Clamp(format.FontSize * 1.05, 11, 16),
            };
            if (session.Document.Styles.Find(style.Id) is null && BuiltInStyles.Definition(style.Id)?.Character.FontSize is { } builtInSize)
            {
                size = Math.Clamp(builtInSize * 1.05, 11, 20);
            }

            var colored = style.Character.Color is { IsAuto: false } color && color.Value is not ("000000" or "404040" or "595959" or "5A5A5A")
                          || style.Id.StartsWith("Heading", StringComparison.Ordinal);
            entries.Add(new MatRibbonGalleryEntry(style.Id, BuiltInStyles.DisplayName(style))
            {
                Sample = style.Id == BuiltInStyles.Title ? "AaBb" : style.Id.StartsWith("Heading", StringComparison.Ordinal) ? "AaBbCc" : "AaBbCcDd",
                SampleSize = size,
                SampleIsBold = format.Bold || style.Character.Bold == true,
                SampleIsItalic = format.Italic || style.Character.Italic == true,
                SampleIsAccent = colored,
            });
        }

        return entries;
    }

    /// <summary>The definition of one window (the colour palettes are those of its document's theme).</summary>
    public static MatRibbonDefinition Create(OfficeWindow window)
    {
        var theme = (window.Document as WordDocument)?.Content.Theme ?? Theme.Office;
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        void Launch(string id)
        {
            if (window.Commands.Find(id) is { } command)
            {
                _ = OfficeWindow.RunCommandAsync(window, command);
            }
        }

        var styles = (window.Document as WordDocument)?.Session is { } session ? StyleEntriesOf(session) : StyleEntries;
        definition.Add(HomeTab(theme, Launch, styles), InsertTab(), DesignTab(theme), LayoutTab(), ReferencesTab(), ReviewTab(), ViewTab(), TableDesignTab(theme), TableLayoutTab(), PictureTab(), ShapeTab(theme), HeaderFooterTab());
        return definition;
    }

    private static MatRibbonTab HomeTab(Theme theme, Action<string> launch, IReadOnlyList<MatRibbonGalleryEntry> styles) =>
        new MatRibbonTab("home", "Start").Add(
            new MatRibbonGroup("undo", "Rückgängig") { Icon = "undo" }.Add(
                Button(CommonCommandIds.Undo, MatRibbonSize.Small),
                Button(CommonCommandIds.Redo, MatRibbonSize.Small)),
            new MatRibbonGroup("clipboard", "Zwischenablage") { Icon = "paste", ShrinkOrder = 1 }.Add(
                new MatRibbonSplitButton(WriteCommandIds.Paste, "", menu: [Button(WriteCommandIds.PasteText)]) { Size = MatRibbonSize.Large, MinSize = MatRibbonSize.Large },
                Button(WriteCommandIds.Cut),
                Button(WriteCommandIds.Copy),
                Toggle(WriteCommandIds.FormatPainter)),
            new MatRibbonGroup("font", "Schriftart") { Icon = "type", ShrinkOrder = 4, Launcher = () => launch(WriteCommandIds.FontDialog) }.Add(
                new MatRibbonStack("font-rows",
                    new MatRibbonRow("font-row-1",
                        ComboBox(WriteCommandIds.FontName, width: 136, isEditable: true),
                        ComboBox(WriteCommandIds.FontSize, width: 64, isEditable: true),
                        Button(WriteCommandIds.GrowFont, MatRibbonSize.Small),
                        Button(WriteCommandIds.ShrinkFont, MatRibbonSize.Small),
                        new MatRibbonMenuButton(WriteCommandIds.ChangeCase, "", null,
                            [.. LayoutCommands.CaseChoices.Select(choice => Button($"{WriteCommandIds.ChangeCase}.{choice.Id}"))]) { Size = MatRibbonSize.Small },
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
            new MatRibbonGroup("paragraph", "Absatz") { Icon = "align-left", ShrinkOrder = 3, Launcher = () => launch(WriteCommandIds.ParagraphDialog) }.Add(
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
                StylesGallery(styles)),
            new MatRibbonGroup("editing", "Bearbeiten") { Icon = "search", ShrinkOrder = 2 }.Add(
                Button(WriteCommandIds.Find),
                Button(WriteCommandIds.Replace),
                Button(WriteCommandIds.SelectAll)));

    private static MatRibbonTab InsertTab() =>
        new MatRibbonTab("insert", "Einfügen").Add(
            new MatRibbonGroup("tables", "Tabellen") { Icon = "table" }.Add(
                new MatRibbonCustom(WriteCommandIds.TablePicker, "Tabelle") { IsTall = true }),
            new MatRibbonGroup("illustrations", "Illustrationen") { Icon = "image" }.Add(
                Button(WriteCommandIds.InsertPicture, MatRibbonSize.Large),
                new MatRibbonMenuButton(WriteCommandIds.InsertShape, "", null,
                    [.. ShapeCommands.Presets.Select(shape => Button($"{WriteCommandIds.InsertShape}.{shape.Preset}"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("pages", "Seiten") { Icon = "page-break" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.CoverPage, "", null,
                    [.. InsertCommands.CoverDesigns.Select(design => Button($"{WriteCommandIds.CoverPage}.{design.Id}"))]) { Size = MatRibbonSize.Large },
                Button(WriteCommandIds.BlankPage, MatRibbonSize.Large),
                Button(WriteCommandIds.PageBreak, MatRibbonSize.Large)),
            new MatRibbonGroup("header-footer", "Kopf- und Fußzeile") { Icon = "header" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.Header, "", null,
                    Button($"{WriteCommandIds.Header}.edit"),
                    Button($"{WriteCommandIds.Header}.remove")) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(WriteCommandIds.Footer, "", null,
                    Button($"{WriteCommandIds.Footer}.edit"),
                    Button($"{WriteCommandIds.Footer}.remove")) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(WriteCommandIds.PageNumber, "", null,
                    [.. HeaderFooterCommands.PageNumberPlaces.Select(place => Button($"{WriteCommandIds.PageNumber}.{place.Id}"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("links", "Links") { Icon = "link" }.Add(
                Button(WriteCommandIds.Hyperlink, MatRibbonSize.Large),
                Button(WriteCommandIds.Bookmark, MatRibbonSize.Large)),
            new MatRibbonGroup("text", "Text") { Icon = "clock" }.Add(
                Button(WriteCommandIds.InsertTextBox, MatRibbonSize.Large),
                new MatRibbonMenuButton(WriteCommandIds.DropCap, "", null,
                    Toggle($"{WriteCommandIds.DropCap}.none", "dropCap"), Toggle($"{WriteCommandIds.DropCap}.drop", "dropCap")) { Size = MatRibbonSize.Large },
                Button(WriteCommandIds.InsertDateTime, MatRibbonSize.Large)),
            new MatRibbonGroup("symbols", "Symbole") { Icon = "symbol" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.Symbol, "", null,
                [
                    .. TextCommands.FrequentSymbols.Select(symbol => Button(TextCommands.SymbolId(symbol), MatRibbonSize.Small)),
                    new MatRibbonSeparator("symbols-separator"),
                    Button(WriteCommandIds.SymbolMore),
                ]) { Size = MatRibbonSize.Large }));

    /// <summary>Entwurf: Seitenhintergrund (Seitenfarbe).</summary>
    private static MatRibbonTab DesignTab(Theme theme) =>
        new MatRibbonTab("design", "Entwurf").Add(
            new MatRibbonGroup("page-background", "Seitenhintergrund") { Icon = "palette" }.Add(
                ColorPicker(WriteCommandIds.PageColor, DocumentPalette.FontColors(theme), "Keine Farbe")));

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
                new MatRibbonMenuButton(WriteCommandIds.Columns, "", null,
                    [.. LayoutCommands.ColumnChoices.Select(choice => Toggle($"{WriteCommandIds.Columns}.{choice.Id}", "columns"))]) { Size = MatRibbonSize.Large },
                new MatRibbonMenuButton(WriteCommandIds.Breaks, "", null,
                [
                    Button($"{WriteCommandIds.PageBreak}.layout"),
                    Button(WriteCommandIds.ColumnBreak),
                    new MatRibbonSeparator("breaks-separator"),
                    .. LayoutCommands.SectionBreaks.Select(entry => Button($"{WriteCommandIds.SectionBreak}.{entry.Id}")),
                ]) { Size = MatRibbonSize.Large },
                Button(WriteCommandIds.PageSetup)),
            new MatRibbonGroup("layout-paragraph", "Absatz") { Icon = "indent" }.Add(
                new MatRibbonStack("indents",
                    Labelled(Number(WriteCommandIds.IndentLeft, 0, 50, 0.25, "cm", 2), "Einzug links"),
                    Labelled(Number(WriteCommandIds.IndentRight, 0, 50, 0.25, "cm", 2), "Einzug rechts")),
                new MatRibbonStack("spacing",
                    Labelled(Number(WriteCommandIds.SpacingBefore, 0, 1584, 6, "Pt."), "Abstand vor"),
                    Labelled(Number(WriteCommandIds.SpacingAfter, 0, 1584, 6, "Pt."), "Abstand nach"))));

    /// <summary>The Formatvorlagen gallery: three styles in the ribbon (Word's width at a normal window), all in its menu.</summary>
    private static MatRibbonGallery StylesGallery(IReadOnlyList<MatRibbonGalleryEntry> styles)
    {
        var gallery = Gallery(WriteCommandIds.Styles, styles);
        gallery.Menu.AddRange([Button(WriteCommandIds.StyleFromSelection), Button(WriteCommandIds.StyleNew), Button(WriteCommandIds.StyleModify), Button(WriteCommandIds.StylesPane)]);
        gallery.InlineCount = 3;
        gallery.EntryWidth = 74;
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

    /// <summary>Tabellenentwurf (contextual): Rahmen and Schattierung of the selected cells.</summary>
    private static MatRibbonTab TableDesignTab(Theme theme) =>
        new MatRibbonTab("tableDesign", "Tabellenentwurf") { Context = TableCommands.Context }.Add(
            new MatRibbonGroup("table-borders", "Rahmen") { Icon = "borders" }.Add(
                ColorPicker(WriteCommandIds.TableShading, DocumentPalette.FontColors(theme), "Keine Farbe"),
                new MatRibbonMenuButton(WriteCommandIds.TableBorders, "", null,
                    [.. TableCommands.BorderChoices.Select(choice => Button($"{WriteCommandIds.TableBorders}.{choice.Id}"))]) { Size = MatRibbonSize.Large }));

    /// <summary>Layout (contextual, Word's second "Layout"): select, delete, insert, merge, size, alignment, heading rows.</summary>
    private static MatRibbonTab TableLayoutTab() =>
        new MatRibbonTab("tableLayout", "Layout") { Context = TableCommands.Context, KeyTip = "T" }.Add(
            new MatRibbonGroup("table-table", "Tabelle") { Icon = "grid" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.TableSelect, "", null,
                    Button(WriteCommandIds.TableSelectCell),
                    Button(WriteCommandIds.TableSelectColumn),
                    Button(WriteCommandIds.TableSelectRow),
                    Button(WriteCommandIds.TableSelectTable)) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("table-rows", "Zeilen und Spalten") { Icon = "row-insert" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.TableDelete, "", null,
                    Button(WriteCommandIds.TableDeleteCells),
                    Button(WriteCommandIds.TableDeleteColumns),
                    Button(WriteCommandIds.TableDeleteRows),
                    Button(WriteCommandIds.TableDeleteTable)) { Size = MatRibbonSize.Large },
                Button(WriteCommandIds.TableInsertAbove, MatRibbonSize.Large),
                new MatRibbonStack("table-insert",
                    Button(WriteCommandIds.TableInsertBelow),
                    Button(WriteCommandIds.TableInsertLeft),
                    Button(WriteCommandIds.TableInsertRight))),
            new MatRibbonGroup("table-merge", "Zusammenführen") { Icon = "merge-cells" }.Add(
                Button(WriteCommandIds.TableMerge),
                Button(WriteCommandIds.TableSplit)),
            new MatRibbonGroup("table-size", "Zellengröße") { Icon = "columns" }.Add(
                Button(WriteCommandIds.TableDistributeRows),
                Button(WriteCommandIds.TableDistributeColumns)),
            new MatRibbonGroup("table-align", "Ausrichtung") { Icon = "align-center" }.Add(
                new MatRibbonStack("table-align-rows",
                    new MatRibbonRow("table-align-top", [.. TableCommands.CellAlignments.Take(3).Select(Align)]),
                    new MatRibbonRow("table-align-center", [.. TableCommands.CellAlignments.Skip(3).Take(3).Select(Align)]),
                    new MatRibbonRow("table-align-bottom", [.. TableCommands.CellAlignments.Skip(6).Select(Align)]))),
            new MatRibbonGroup("table-data", "Daten") { Icon = "header" }.Add(
                Toggle(WriteCommandIds.TableRepeatHeader)));

    /// <summary>Bildformat (contextual): alternative text, change and reset, the alignment of the picture's paragraph, its size.</summary>
    private static MatRibbonTab PictureTab() =>
        new MatRibbonTab("pictureFormat", "Bildformat") { Context = PictureCommands.Context }.Add(
            new MatRibbonGroup("picture-accessibility", "Barrierefreiheit") { Icon = "text-box" }.Add(
                Button(WriteCommandIds.PictureAltText, MatRibbonSize.Large)),
            new MatRibbonGroup("picture-adjust", "Anpassen") { Icon = "refresh" }.Add(
                Button(WriteCommandIds.PictureChange),
                Button(WriteCommandIds.PictureResetSize)),
            new MatRibbonGroup("picture-arrange", "Anordnen") { Icon = "align-center" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.PictureWrap, "", null,
                    [.. PictureCommands.WrapChoices.Select(choice => Toggle($"{WriteCommandIds.PictureWrap}.{choice.Id}", "pictureWrap"))]) { Size = MatRibbonSize.Large },
                new MatRibbonRow("picture-align",
                    Toggle(WriteCommandIds.PictureAlignLeft, "pictureAlign", MatRibbonSize.Small),
                    Toggle(WriteCommandIds.PictureAlignCenter, "pictureAlign", MatRibbonSize.Small),
                    Toggle(WriteCommandIds.PictureAlignRight, "pictureAlign", MatRibbonSize.Small))),
            new MatRibbonGroup("picture-size", "Größe") { Icon = "crop" }.Add(
                new MatRibbonStack("picture-size-fields",
                    Labelled(Number(WriteCommandIds.PictureHeight, 0.1, 55.87, 0.1, "cm", 2), "Höhe"),
                    Labelled(Number(WriteCommandIds.PictureWidth, 0.1, 55.87, 0.1, "cm", 2), "Breite"))));

    /// <summary>Formformat (contextual): fill, outline, text, wrap, size of the selected shape.</summary>
    private static MatRibbonTab ShapeTab(Theme theme) =>
        new MatRibbonTab("shapeFormat", "Formformat") { Context = ShapeCommands.Context }.Add(
            new MatRibbonGroup("shape-styles", "Formenarten") { Icon = "fill" }.Add(
                ColorPicker(WriteCommandIds.ShapeFill, DocumentPalette.FontColors(theme), "Keine Füllung"),
                ColorPicker(WriteCommandIds.ShapeLine, DocumentPalette.FontColors(theme), "Keine Kontur")),
            new MatRibbonGroup("shape-text", "Text") { Icon = "type" }.Add(
                Button(WriteCommandIds.ShapeEditText, MatRibbonSize.Large)),
            new MatRibbonGroup("shape-arrange", "Anordnen") { Icon = "wrap-text" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.ShapeWrap, "", null,
                    [.. PictureCommands.WrapChoices.Select(choice => Toggle($"{WriteCommandIds.ShapeWrap}.{choice.Id}", "shapeWrap"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("shape-size", "Größe") { Icon = "crop" }.Add(
                new MatRibbonStack("shape-size-fields",
                    Labelled(Number(WriteCommandIds.ShapeHeight, 0.1, 55.87, 0.1, "cm", 2), "Höhe"),
                    Labelled(Number(WriteCommandIds.ShapeWidth, 0.1, 55.87, 0.1, "cm", 2), "Breite"))));

    /// <summary>Kopf- und Fußzeile (contextual, while one is edited): fields, options, close.</summary>
    private static MatRibbonTab HeaderFooterTab() =>
        new MatRibbonTab("headerFooterTab", "Kopf- und Fußzeile") { Context = HeaderFooterCommands.Context }.Add(
            new MatRibbonGroup("hf-insert", "Einfügen") { Icon = "page-number" }.Add(
                Button(WriteCommandIds.HeaderFooterPage, MatRibbonSize.Large),
                Button(WriteCommandIds.HeaderFooterPages),
                Button(WriteCommandIds.HeaderFooterDate)),
            new MatRibbonGroup("hf-options", "Optionen") { Icon = "file" }.Add(
                Toggle(WriteCommandIds.HeaderFooterFirstPage),
                Toggle(WriteCommandIds.HeaderFooterEvenOdd)),
            new MatRibbonGroup("hf-close", "Schließen") { Icon = "x" }.Add(
                new MatRibbonButton(WriteCommandIds.HeaderFooterClose, "Schließen") { Size = MatRibbonSize.Large }));

    private static MatRibbonItem Align((string Id, string Label, Core.Model.CellVerticalAlignment Vertical, ParagraphAlignment Horizontal) alignment) =>
        Toggle($"{WriteCommandIds.TableAlign}.{alignment.Id}", "cellAlign", MatRibbonSize.Small);

    /// <summary>Referenzen, like Word's: Inhaltsverzeichnis, Fußnoten.</summary>
    private static MatRibbonTab ReferencesTab() =>
        new MatRibbonTab("references", "Referenzen").Add(
            new MatRibbonGroup("toc", "Inhaltsverzeichnis") { Icon = "toc" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.Toc, "", null,
                    Button(WriteCommandIds.TocInsert),
                    Button(WriteCommandIds.TocRemove)) { Size = MatRibbonSize.Large },
                Button(WriteCommandIds.TocUpdate)),
            new MatRibbonGroup("footnotes", "Fußnoten") { Icon = "footnote" }.Add(
                Button(WriteCommandIds.InsertFootnote, MatRibbonSize.Large),
                new MatRibbonStack("footnote-more",
                    Button(WriteCommandIds.InsertEndnote),
                    new MatRibbonMenuButton(WriteCommandIds.NextFootnote, "", null,
                        [.. ReferenceCommands.NoteSteps.Select(step => Button($"{WriteCommandIds.NextFootnote}.{step.Id}"))]))));

    /// <summary>Überprüfen, like Word's: Kommentare (Dokumentprüfung and Sprache join with the spelling check).</summary>
    private static MatRibbonTab ReviewTab() =>
        new MatRibbonTab("review", "Überprüfen").Add(
            new MatRibbonGroup("proofing", "Dokumentprüfung") { Icon = "spell-check" }.Add(
                Button(WriteCommandIds.Spelling, MatRibbonSize.Large),
                Button(WriteCommandIds.WordCount)),
            new MatRibbonGroup("language", "Sprache") { Icon = "globe" }.Add(
                new MatRibbonMenuButton(WriteCommandIds.Language, "", null,
                    [.. ReviewCommands.LanguageChoices().Select(tag => Toggle($"{WriteCommandIds.Language}.{tag}", "language"))]) { Size = MatRibbonSize.Large }),
            new MatRibbonGroup("comments", "Kommentare") { Icon = "comment" }.Add(
                Button(WriteCommandIds.NewComment, MatRibbonSize.Large),
                new MatRibbonMenuButton(WriteCommandIds.DeleteComment, "", null,
                    Button(WriteCommandIds.DeleteCommentOne),
                    Button(WriteCommandIds.DeleteAllComments)) { Size = MatRibbonSize.Large },
                new MatRibbonStack("comment-steps",
                    Button(WriteCommandIds.PreviousComment),
                    Button(WriteCommandIds.NextComment)),
                Toggle(WriteCommandIds.ShowComments, size: MatRibbonSize.Large)),
            new MatRibbonGroup("changes", "Änderungen") { Icon = "check-circle" }.Add(
                Button(WriteCommandIds.AcceptAllRevisions, MatRibbonSize.Large),
                Button(WriteCommandIds.RejectAllRevisions, MatRibbonSize.Large)));

    private static MatRibbonTab ViewTab() =>
        new MatRibbonTab("view", "Ansicht").Add(
            new MatRibbonGroup("show", "Anzeigen") { Icon = "ruler" }.Add(
                Toggle(WriteCommandIds.Ruler),
                Toggle(WriteCommandIds.NavigationPane),
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
