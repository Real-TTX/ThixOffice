using System.Globalization;
using Avalonia.Controls;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// The commands of the contextual tabs: Formformat / Bildformat (Höhe, Breite in cm, Alternativtext) and Tabellenentwurf / Layout
/// (rows and columns, merge and split, Schattierung, the style options), plus the context menus of the slide and the thumbnails and
/// which contextual tabs show for the selection.
/// </summary>
internal static class ShowContextMenus
{
    public const string ShapeContext = "shape";
    public const string PictureContext = "picture";
    public const string TableContext = "table";
    public const string TextContext = "text";

    public const string ShapeFormatTab = "shapeFormat";
    public const string PictureFormatTab = "pictureFormat";
    public const string TableDesignTab = "tableDesign";
    public const string TableLayoutTab = "tableLayout";

    public const string Height = "show.size.height";
    public const string Width = "show.size.width";
    public const string AltText = "show.altText";
    public const string RowAbove = "show.table.rowAbove";
    public const string RowBelow = "show.table.rowBelow";
    public const string ColumnLeft = "show.table.columnLeft";
    public const string ColumnRight = "show.table.columnRight";
    public const string DeleteRows = "show.table.deleteRows";
    public const string DeleteColumns = "show.table.deleteColumns";
    public const string DeleteTable = "show.table.delete";
    public const string MergeCells = "show.table.merge";
    public const string SplitCell = "show.table.split";
    public const string Shading = "show.table.shading";
    public const string HeaderRow = "show.table.headerRow";
    public const string TotalRow = "show.table.totalRow";
    public const string BandedRows = "show.table.bandedRows";
    public const string FirstColumn = "show.table.firstColumn";
    public const string LastColumn = "show.table.lastColumn";
    public const string BandedColumns = "show.table.bandedColumns";

    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>Which contextual tabs show: Formformat for shapes, Bildformat for pictures, Tabellenentwurf and Layout in a table.</summary>
    public static void UpdateContextTabs(OfficeWindow window, ShowEditor editor)
    {
        var definition = window.RibbonDefinition;
        var slide = editor.Session.CurrentSlide;
        var shapes = slide is null ? [] : editor.Session.Selection.Shapes.Select(id => slide.FindShape(id)).OfType<SlideElement>().ToList();
        void Show(string context, bool shown)
        {
            if (shown)
            {
                definition.ShowContext(context);
            }
            else
            {
                definition.HideContext(context);
            }
        }

        var normal = editor.CurrentView == ShowView.Normal;
        Show(ShapeContext, normal && shapes.Any(shape => shape is AutoShape or ConnectorShape or GroupShape));
        Show(PictureContext, normal && shapes.Count > 0 && shapes.All(shape => shape is PictureShape));
        Show(TableContext, normal && shapes.Count == 1 && shapes[0] is GraphicFrame { Table: not null });
    }

    /// <summary>The menu of the slide area: clipboard, then what applies to the selection (order, group, text), the slide's layout.</summary>
    public static ContextMenu ForSlideCanvas(OfficeWindow window, EditingSession session)
    {
        if (session.Selection.Text is not null)
        {
            return OfficeCommandMenu.Create(window,
            [
                ShowCommandIds.Cut, ShowCommandIds.Copy, ShowCommandIds.Paste, ShowCommandIds.PasteText, null,
                ShowCommandIds.Bullets, ShowCommandIds.Numbering, ShowCommandIds.InsertLink,
            ]);
        }

        if (!session.Selection.Shapes.IsEmpty)
        {
            return OfficeCommandMenu.Create(window,
            [
                ShowCommandIds.Cut, ShowCommandIds.Copy, ShowCommandIds.Paste, null,
                ShowCommandIds.Group, ShowCommandIds.Ungroup, ShowCommandIds.BringToFront, ShowCommandIds.SendToBack, null,
                AltText, ShowCommandIds.Delete,
            ],
            [ShowCommandIds.Group, ShowCommandIds.Ungroup]);
        }

        return OfficeCommandMenu.Create(window,
        [
            ShowCommandIds.Paste, null,
            ShowCommandIds.NewSlide, ShowCommandIds.ResetSlide, ShowCommandIds.FormatBackground, null,
            ShowCommandIds.SmartGuides, ShowCommandIds.Gridlines,
        ]);
    }

    /// <summary>The menu of the thumbnails and the sorter: PowerPoint's slide menu.</summary>
    public static ContextMenu ForSlides(OfficeWindow window) => OfficeCommandMenu.Create(window,
    [
        ShowCommandIds.Cut, ShowCommandIds.Copy, ShowCommandIds.Paste, null,
        ShowCommandIds.NewSlide, ShowCommandIds.DuplicateSlide, ShowCommandIds.DeleteSlide, null,
        ShowCommandIds.AddSection, ShowCommandIds.RenameSection, ShowCommandIds.RemoveSection, null,
        ShowCommandIds.ResetSlide, ShowCommandIds.HideSlide,
    ],
    [ShowCommandIds.RenameSection, ShowCommandIds.RemoveSection]);

    public static void AddCommands(OfficeWindow window, Func<ShowEditor?> editorOf, Action<string, Action<ShowEditor>> run)
    {
        var commands = window.Commands;
        bool SingleShape() => editorOf() is { } editor && editor.Session.Selection.Shapes.Count == 1;
        (int ShapeId, int Row, int Column, Table Table)? Cell()
        {
            if (editorOf() is not { } editor || editor.Session.CurrentSlide is not { } slide || editor.Session.Selection.Shapes.Count != 1
                || slide.FindShape(editor.Session.Selection.Shapes[0]) is not GraphicFrame { Table: { } table } frame)
            {
                return null;
            }

            var target = editor.Session.Selection.Text?.Target;
            return (frame.Id, target?.Row ?? 0, target?.Column ?? 0, table);
        }

        bool InTable() => Cell() is not null;
        Transform2D? Box() => editorOf() is { } editor && SingleShape() && editor.Session.CurrentSlide?.FindShape(editor.Session.Selection.Shapes[0])?.Transform is { } transform ? transform : null;

        // ===== Größe =====
        commands.Add(new OfficeValueCommand(Height, "Höhe", OfficeValueKind.Number,
            () => Box() is { } box ? Units.EmuToCentimeters(box.Height).ToString("0.##", CultureInfo.InvariantCulture) : null,
            value => run("Größe", editor => Resize(editor, value, height: true)))
        {
            Icon = "line-spacing", CanExecute = () => Box() is not null, Description = "Die Höhe der markierten Form (cm).",
        });
        commands.Add(new OfficeValueCommand(Width, "Breite", OfficeValueKind.Number,
            () => Box() is { } box ? Units.EmuToCentimeters(box.Width).ToString("0.##", CultureInfo.InvariantCulture) : null,
            value => run("Größe", editor => Resize(editor, value, height: false)))
        {
            Icon = "columns", CanExecute = () => Box() is not null, Description = "Die Breite der markierten Form (cm).",
        });
        commands.Add(new OfficeCommand(AltText, "Alternativtext", async () =>
        {
            if (editorOf() is not { } editor || editor.Session.CurrentSlide?.FindShape(editor.Session.Selection.Shapes[0]) is not { } shape)
            {
                return;
            }

            var text = await TextInputDialog.AskAsync(window, "Alternativtext", "Beschreibung für Personen, die die Folie nicht sehen", shape.Description ?? "", "OK");
            if (text is not null)
            {
                await editor.RunAsync("Alternativtext", () => editor.Session.SetAlternativeText(shape.Id, text.Trim()));
            }
        })
        {
            Icon = "accessibility", CanExecute = SingleShape, Description = "Beschreibt die Form für Bildschirmsprachausgaben.",
        });

        // ===== Tabelle: Layout =====
        commands.Add(new OfficeCommand(RowAbove, "Oberhalb einfügen", () => run("Zeile einfügen", editor => { if (Cell() is { } c) { editor.Session.InsertTableRow(c.ShapeId, c.Row, below: false); } }))
        {
            Icon = "row-insert", CanExecute = InTable, Description = "Fügt über der Zeile mit dem Cursor eine Zeile ein.",
        });
        commands.Add(new OfficeCommand(RowBelow, "Unterhalb einfügen", () => run("Zeile einfügen", editor => { if (Cell() is { } c) { editor.Session.InsertTableRow(c.ShapeId, c.Row, below: true); } }))
        {
            Icon = "row-insert", CanExecute = InTable, Description = "Fügt unter der Zeile mit dem Cursor eine Zeile ein.",
        });
        commands.Add(new OfficeCommand(ColumnLeft, "Links einfügen", () => run("Spalte einfügen", editor => { if (Cell() is { } c) { editor.Session.InsertTableColumn(c.ShapeId, c.Column, right: false); } }))
        {
            Icon = "column-insert", CanExecute = InTable, Description = "Fügt links der Spalte mit dem Cursor eine Spalte ein.",
        });
        commands.Add(new OfficeCommand(ColumnRight, "Rechts einfügen", () => run("Spalte einfügen", editor => { if (Cell() is { } c) { editor.Session.InsertTableColumn(c.ShapeId, c.Column, right: true); } }))
        {
            Icon = "column-insert", CanExecute = InTable, Description = "Fügt rechts der Spalte mit dem Cursor eine Spalte ein.",
        });
        commands.Add(new OfficeCommand(DeleteRows, "Zeilen löschen", () => run("Zeilen löschen", editor => { if (Cell() is { } c) { editor.Session.DeleteTableRows(c.ShapeId, c.Row, 1); } }))
        {
            Icon = "trash", CanExecute = () => Cell() is { Table.RowCount: > 1 }, Description = "Löscht die Zeile mit dem Cursor.",
        });
        commands.Add(new OfficeCommand(DeleteColumns, "Spalten löschen", () => run("Spalten löschen", editor => { if (Cell() is { } c) { editor.Session.DeleteTableColumns(c.ShapeId, c.Column, 1); } }))
        {
            Icon = "trash", CanExecute = () => Cell() is { Table.ColumnCount: > 1 }, Description = "Löscht die Spalte mit dem Cursor.",
        });
        commands.Add(new OfficeCommand(DeleteTable, "Tabelle löschen", () => run("Tabelle löschen", editor =>
        {
            if (Cell() is { } c)
            {
                editor.Session.EndTextEditing();
                editor.Session.DeleteShapes([c.ShapeId]);
            }
        }))
        {
            Icon = "trash", CanExecute = InTable, Description = "Löscht die ganze Tabelle.",
        });
        commands.Add(new OfficeCommand(MergeCells, "Zellen verbinden", () => run("Zellen verbinden", editor =>
        {
            if (Cell() is { } c && editor.Session.Selection.Text is { } text)
            {
                var (row, column) = (c.Row, c.Column);
                editor.Session.MergeTableCells(c.ShapeId, row, column, Math.Min(row + 1, c.Table.RowCount - 1), column);
            }
        }))
        {
            Icon = "merge-cells", CanExecute = () => Cell() is { } c && c.Row < c.Table.RowCount - 1, Description = "Verbindet die Zelle mit der darunter.",
        });
        commands.Add(new OfficeCommand(SplitCell, "Zellen teilen", () => run("Zellen teilen", editor => { if (Cell() is { } c) { editor.Session.SplitTableCell(c.ShapeId, c.Row, c.Column); } }))
        {
            Icon = "split-cells", CanExecute = () => Cell() is { } c && c.Table.CellAt(c.Row, c.Column) is { } cell && (cell.GridSpan > 1 || cell.RowSpan > 1),
            Description = "Teilt eine verbundene Zelle wieder auf.",
        });

        // ===== Tabelle: Entwurf =====
        commands.Add(new OfficeValueCommand(Shading, "Schattierung", OfficeValueKind.Color, () => editorOf()?.FillColor, value => run("Schattierung", editor =>
        {
            if (Cell() is { } c)
            {
                editor.FillColor = value;
                editor.Session.SetTableCellFill(c.ShapeId, [(c.Row, c.Column)], ShowPalette.ToColorSpec(value) is { } color ? new SolidFill(color) : NoFill.Instance);
            }
        }))
        {
            Icon = "fill", CanExecute = InTable, Description = "Füllt die Zelle mit dem Cursor mit einer Farbe.",
        });
        foreach (var (id, label, read, write) in new (string, string, Func<TableProperties, bool>, Func<TableProperties, bool, TableProperties>)[]
                 {
                     (HeaderRow, "Kopfzeile", p => p.FirstRow, (p, v) => p with { FirstRow = v }),
                     (TotalRow, "Ergebniszeile", p => p.LastRow, (p, v) => p with { LastRow = v }),
                     (BandedRows, "Gebänderte Zeilen", p => p.BandRows, (p, v) => p with { BandRows = v }),
                     (FirstColumn, "Erste Spalte", p => p.FirstColumn, (p, v) => p with { FirstColumn = v }),
                     (LastColumn, "Letzte Spalte", p => p.LastColumn, (p, v) => p with { LastColumn = v }),
                     (BandedColumns, "Gebänderte Spalten", p => p.BandColumns, (p, v) => p with { BandColumns = v }),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => run(label, editor =>
            {
                if (Cell() is { } c)
                {
                    editor.Session.SetTableStyleOptions(c.ShapeId, properties => write(properties, !read(properties)));
                }
            }))
            {
                Icon = "check-square", CanExecute = InTable, IsChecked = () => Cell() is { } c && read(c.Table.Properties), Description = $"Formatvorlagenoption „{label}“ der Tabelle.",
            });
        }
    }

    /// <summary>Höhe / Breite (cm): the box keeps its top-left corner.</summary>
    private static void Resize(ShowEditor editor, string? value, bool height)
    {
        if (!double.TryParse(value?.Replace(',', '.'), NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres) || centimetres <= 0
            || editor.Session.Selection.Shapes.Count != 1 || editor.Session.CurrentSlide?.FindShape(editor.Session.Selection.Shapes[0]) is not { Transform: { } box } shape)
        {
            return;
        }

        var size = Units.CentimetersToEmu(Math.Min(centimetres, 142));
        editor.Session.SetBounds(shape.Id, new EmuRect(box.X, box.Y, height ? box.Width : size, height ? size : box.Height));
        _ = German;
    }
}
