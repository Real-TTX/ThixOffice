using MatDesktop;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using static ThixOffice.Desktop.Shell.OfficeRibbonItems;

namespace ThixWrite.Editor;

/// <summary>
/// The ids of ThixWrite's commands. The ribbon names them all; a command the window does not have yet is left out of the
/// ribbon (no dead buttons), so the editor of phase 1a only registers its commands (<see cref="WriteRibbon.AddCommands"/>)
/// and they appear in their places. Value commands (<see cref="OfficeValueCommand"/>) are marked.
/// </summary>
public static class WriteCommandIds
{
    // Now (phase 0)
    public const string InsertDateTime = "write.insertDateTime";

    // Start – Zwischenablage
    public const string Paste = "edit.paste";
    public const string Cut = "edit.cut";
    public const string Copy = "edit.copy";
    public const string FormatPainter = "write.formatPainter";

    // Start – Schriftart
    public const string FontName = "write.fontName"; // value: the font family
    public const string FontSize = "write.fontSize"; // value: the size in pt, invariant ("10.5")
    public const string GrowFont = "write.growFont";
    public const string ShrinkFont = "write.shrinkFont";
    public const string ClearFormatting = "write.clearFormatting";
    public const string Bold = "write.bold";
    public const string Italic = "write.italic";
    public const string Underline = "write.underline";
    public const string Strikethrough = "write.strikethrough";
    public const string Subscript = "write.subscript";
    public const string Superscript = "write.superscript";

    // Start – Absatz
    public const string Bullets = "write.bullets";
    public const string Numbering = "write.numbering";
    public const string DecreaseIndent = "write.decreaseIndent";
    public const string IncreaseIndent = "write.increaseIndent";
    public const string AlignLeft = "write.alignLeft";
    public const string AlignCenter = "write.alignCenter";
    public const string AlignRight = "write.alignRight";
    public const string Justify = "write.justify";
    public const string LineSpacing = "write.lineSpacing";
    public const string ShowMarks = "write.showMarks";

    // Start – Formatvorlagen
    public const string Styles = "write.styles"; // value: the id of the paragraph style

    // Start – Bearbeiten
    public const string Find = "edit.find";
    public const string Replace = "edit.replace";

    // Layout – Seite einrichten
    public const string PageMargins = "write.pageMargins";
    public const string PageOrientation = "write.pageOrientation";
    public const string PageSize = "write.pageSize";

    // Ansicht
    public const string Ruler = "write.ruler";
}

/// <summary>
/// ThixWrite's ribbon – Datei | Start | Einfügen | Layout | Ansicht, like Word – as a MatDesktop definition (data: the web
/// app can draw the same one) and ThixWrite's own commands. Every item's id is a command id; labels, icons, shortcuts and
/// tooltips come from the commands. Today only the shell's commands and "Datum und Uhrzeit" exist, so the binding leaves
/// most of it out: Start shows Rückgängig, Einfügen "Datum und Uhrzeit", Ansicht Zoom and Menüband; Layout appears with the
/// editor's commands (phase 1a).
/// </summary>
public static class WriteRibbon
{
    /// <summary>The definition of one window.</summary>
    public static MatRibbonDefinition Create()
    {
        var definition = new MatRibbonDefinition { FileLabel = "Datei" };
        definition.Add(
            new MatRibbonTab("home", "Start").Add(
                new MatRibbonGroup("undo", "Rückgängig") { Icon = "undo" }.Add(
                    Button(CommonCommandIds.Undo),
                    Button(CommonCommandIds.Redo)),
                new MatRibbonGroup("clipboard", "Zwischenablage") { Icon = "paste" }.Add(
                    Button(WriteCommandIds.Paste, MatRibbonSize.Large),
                    Button(WriteCommandIds.Cut),
                    Button(WriteCommandIds.Copy),
                    Toggle(WriteCommandIds.FormatPainter)),
                new MatRibbonGroup("font", "Schriftart") { Icon = "type", ShrinkOrder = 3 }.Add(
                    new MatRibbonStack("font-rows",
                        new MatRibbonRow("font-row-1",
                            ComboBox(WriteCommandIds.FontName, width: 140, isEditable: true),
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
                            Toggle(WriteCommandIds.Superscript, size: MatRibbonSize.Small)))),
                new MatRibbonGroup("paragraph", "Absatz") { Icon = "align-left", ShrinkOrder = 2 }.Add(
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
                            Button(WriteCommandIds.LineSpacing, MatRibbonSize.Small)))),
                new MatRibbonGroup("styles", "Formatvorlagen") { Icon = "template", ShrinkOrder = 1 }.Add(
                    Gallery(WriteCommandIds.Styles, [])),
                new MatRibbonGroup("editing", "Bearbeiten") { Icon = "search" }.Add(
                    Button(WriteCommandIds.Find),
                    Button(WriteCommandIds.Replace))),
            new MatRibbonTab("insert", "Einfügen").Add(
                new MatRibbonGroup("text", "Text") { Icon = "clock" }.Add(
                    Button(WriteCommandIds.InsertDateTime, MatRibbonSize.Large))),
            new MatRibbonTab("layout", "Layout").Add(
                new MatRibbonGroup("page-setup", "Seite einrichten") { Icon = "margins" }.Add(
                    Button(WriteCommandIds.PageMargins, MatRibbonSize.Large),
                    Button(WriteCommandIds.PageOrientation, MatRibbonSize.Large),
                    Button(WriteCommandIds.PageSize, MatRibbonSize.Large))),
            new MatRibbonTab("view", "Ansicht").Add(
                new MatRibbonGroup("show", "Anzeigen") { Icon = "ruler" }.Add(
                    Toggle(WriteCommandIds.Ruler)),
                new MatRibbonGroup("zoom", "Zoom") { Icon = "zoom-in" }.Add(
                    Button(CommonCommandIds.Zoom100, MatRibbonSize.Large),
                    Button(CommonCommandIds.ZoomIn),
                    Button(CommonCommandIds.ZoomOut)),
                new MatRibbonGroup("ribbon", "Menüband") { Icon = "chevron-up" }.Add(
                    Toggle(CommonCommandIds.ToggleRibbon))));
        return definition;
    }

    /// <summary>ThixWrite's commands of a window (the shell's are there already).</summary>
    public static void AddCommands(OfficeWindow window)
    {
        window.Commands.Add(new OfficeCommand(WriteCommandIds.InsertDateTime, "Datum und Uhrzeit", () =>
        {
            if (window.Editor is PlaceholderEditor editor)
            {
                editor.InsertAtCaret(DateTime.Now.ToString("dd.MM.yyyy HH:mm", System.Globalization.CultureInfo.GetCultureInfo("de-DE")));
            }
        })
        {
            Icon = "clock",
            Shortcut = "Alt+Shift+D",
            Description = "Fügt das heutige Datum und die Uhrzeit an der Einfügemarke ein.",
            Keywords = ["datum", "uhrzeit", "heute", "einfügen", "zeit"],
        });
    }
}
