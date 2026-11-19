using System.Globalization;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;
using ThixShow.Dialogs;

namespace ThixShow.Editor;

/// <summary>
/// The commands of Start › Zeichnen, Einfügen and the contextual tab Formformat: shapes drawn by drag, text boxes, pictures, tables,
/// links and symbols; Anordnen (order, group, align, distribute, rotate, flip); Schnellformatvorlagen, Fülleffekt, Formkontur,
/// Formeffekte (shadow), Form ändern. Strg+G / Strg+Umschalt+G group and ungroup like PowerPoint.
/// </summary>
internal static class ShowDrawingCommands
{
    /// <summary>The symbols of Einfügen › Symbol (the frequent ones; they go in as text).</summary>
    public static IReadOnlyList<string> Symbols { get; } = ["€", "©", "®", "™", "°", "±", "×", "÷", "→", "←", "✓", "•", "…", "§", "¶", "∞", "≈", "≠", "≤", "≥"];

    /// <summary>The quick styles (Schnellformatvorlagen): an accent fill with a darker outline, or the accent as outline only.</summary>
    public static IReadOnlyList<(string Id, string Label)> QuickStyles { get; } =
    [
        .. Enumerable.Range(1, 6).Select(index => ($"fill{index}", $"Farbige Füllung – Akzent {index}")),
        .. Enumerable.Range(1, 6).Select(index => ($"outline{index}", $"Farbige Kontur – Akzent {index}")),
    ];

    /// <summary>The effects of a shape that can have them (shapes, lines, pictures, groups).</summary>
    private static ShapeEffects? EffectsOf(SlideElement? shape) => shape switch
    {
        AutoShape auto => auto.Effects,
        ConnectorShape connector => connector.Effects,
        PictureShape picture => picture.Effects,
        GroupShape group => group.Effects,
        _ => null,
    };

    public static void Add(OfficeWindow window, Func<ShowEditor?> editorOf, Action<string, Action<ShowEditor>> run)
    {
        var commands = window.Commands;
        bool HasSlide() => editorOf()?.Session.CurrentSlide is not null;
        bool HasShapes() => editorOf() is { } editor && !editor.Session.Selection.Shapes.IsEmpty;
        bool HasSeveral() => editorOf() is { } editor && editor.Session.Selection.Shapes.Count > 1;

        // ===== Zeichnen / Einfügen =====
        commands.Add(new OfficeValueCommand(ShowCommandIds.Shapes, "Formen", OfficeValueKind.Choice, () => null, value =>
        {
            if (editorOf() is { } editor && !string.IsNullOrEmpty(value))
            {
                editor.Canvas.BeginDraw(value);
            }
        })
        {
            Icon = "shapes", CanExecute = HasSlide, Description = "Zeichnet eine Form: wählen Sie sie aus und ziehen Sie auf der Folie.", Keywords = ["rechteck", "kreis", "pfeil", "linie"],
        });
        commands.Add(new OfficeCommand(ShowCommandIds.TextBox, "Textfeld", () => editorOf()?.Canvas.BeginDraw("textBox"))
        {
            Icon = "text-box", CanExecute = HasSlide, Description = "Zeichnet ein Textfeld: ziehen Sie auf der Folie oder klicken Sie.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.InsertPicture, "Bilder", async () =>
        {
            if (editorOf() is not { } editor || await OfficeApplication.FileDialogs.PickPictureAsync(window, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures)) is not { } path)
            {
                return;
            }

            await editor.InsertPicturesAsync([path]);
        })
        {
            Icon = "image", CanExecute = HasSlide, Description = "Fügt ein Bild aus einer Datei ein (PNG, JPEG, GIF, BMP).", Keywords = ["foto", "grafik"],
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.InsertTable, "Tabelle", OfficeValueKind.Choice, () => null, value => run("Tabelle einfügen", editor =>
        {
            var parts = (value ?? "").Split('x', StringSplitOptions.TrimEntries);
            if (parts.Length == 2 && int.TryParse(parts[0], CultureInfo.InvariantCulture, out var rows) && int.TryParse(parts[1], CultureInfo.InvariantCulture, out var columns))
            {
                editor.Session.InsertTable(Math.Clamp(rows, 1, 75), Math.Clamp(columns, 1, 75));
            }
        }))
        {
            Icon = "table", CanExecute = HasSlide, Description = "Fügt eine Tabelle ein: wählen Sie Zeilen und Spalten im Raster.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.InsertLink, "Link", () => LinkDialog.ShowAsync(window, editorOf()))
        {
            Icon = "link", Shortcut = "Ctrl+K", CanExecute = () => editorOf()?.Session.Selection.Text is { IsCollapsed: false },
            Description = "Macht den markierten Text zu einem Link auf eine Webseite oder E-Mail-Adresse.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.InsertSymbol, "Symbol", () => Task.CompletedTask)
        {
            Icon = "symbol", CanExecute = () => editorOf()?.Session.Selection.Text is not null, IsSearchable = false,
            Description = "Fügt ein Symbol in den Text ein.",
        });
        for (var index = 0; index < Symbols.Count; index++)
        {
            var symbol = Symbols[index];
            commands.Add(new OfficeCommand($"{ShowCommandIds.InsertSymbol}.{index}", symbol, () => run("Symbol", editor => editor.Session.InsertText(symbol)))
            {
                CanExecute = () => editorOf()?.Session.Selection.Text is not null, IsSearchable = false, Description = $"Fügt „{symbol}“ ein.",
            });
        }

        // ===== Anordnen =====
        commands.Add(new OfficeCommand(ShowCommandIds.Arrange, "Anordnen", () => Task.CompletedTask)
        {
            Icon = "layers", CanExecute = HasShapes, IsSearchable = false, Description = "Reihenfolge, Gruppierung, Ausrichtung und Drehung der Formen.",
        });
        foreach (var (id, label, move, shortcut) in new (string, string, ZOrderMove, string?)[]
                 {
                     (ShowCommandIds.BringToFront, "In den Vordergrund", ZOrderMove.BringToFront, "Ctrl+Shift+OemPlus"),
                     (ShowCommandIds.BringForward, "Ebene nach vorne", ZOrderMove.BringForward, null),
                     (ShowCommandIds.SendBackward, "Ebene nach hinten", ZOrderMove.SendBackward, null),
                     (ShowCommandIds.SendToBack, "In den Hintergrund", ZOrderMove.SendToBack, "Ctrl+Shift+OemMinus"),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => run(label, editor => editor.Session.Arrange(move)))
            {
                Icon = "layers", CanExecute = HasShapes, Description = $"{label}: ändert die Reihenfolge der markierten Formen.",
            });
        }

        commands.Add(new OfficeCommand(ShowCommandIds.Group, "Gruppieren", () => run("Gruppieren", editor => editor.Session.GroupShapes()))
        {
            Icon = "layers", Shortcut = "Ctrl+G", CanExecute = HasSeveral, Description = "Fasst die markierten Formen zu einer Gruppe zusammen.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Ungroup, "Gruppierung aufheben", () => run("Gruppierung aufheben", editor => editor.Session.Ungroup()))
        {
            Icon = "layers", Shortcut = "Ctrl+Shift+G",
            CanExecute = () => editorOf() is { } editor && editor.Session.CurrentSlide is { } slide && editor.Session.Selection.Shapes.Any(id => slide.FindShape(id) is GroupShape),
            Description = "Löst die markierte Gruppe in ihre Formen auf.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.AlignShapes, "Ausrichten", () => Task.CompletedTask)
        {
            Icon = "align-left", CanExecute = HasShapes, IsSearchable = false, Description = "Richtet die markierten Formen aneinander oder an der Folie aus.",
        });
        foreach (var (id, label, icon, kind) in new (string, string, string, AlignKind)[]
                 {
                     (ShowCommandIds.AlignShapesLeft, "Linksbündig ausrichten", "align-left", AlignKind.Left),
                     (ShowCommandIds.AlignShapesCenter, "Horizontal zentrieren", "align-center", AlignKind.Center),
                     (ShowCommandIds.AlignShapesRight, "Rechtsbündig ausrichten", "align-right", AlignKind.Right),
                     (ShowCommandIds.AlignShapesTop, "Oben ausrichten", "arrow-up", AlignKind.Top),
                     (ShowCommandIds.AlignShapesMiddle, "Vertikal zentrieren", "minus", AlignKind.Middle),
                     (ShowCommandIds.AlignShapesBottom, "Unten ausrichten", "arrow-down", AlignKind.Bottom),
                 })
        {
            commands.Add(new OfficeCommand(id, label, () => run(label, editor => editor.Session.Align(kind, toSlide: editor.Session.Selection.Shapes.Count == 1)))
            {
                Icon = icon, CanExecute = HasShapes, Description = $"{label} (eine einzelne Form an der Folie).",
            });
        }

        commands.Add(new OfficeCommand(ShowCommandIds.DistributeHorizontally, "Horizontal verteilen", () => run("Verteilen", editor => editor.Session.Distribute(horizontal: true, toSlide: editor.Session.Selection.Shapes.Count < 3)))
        {
            Icon = "columns", CanExecute = HasSeveral, Description = "Verteilt die markierten Formen in gleichen Abständen nebeneinander.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.DistributeVertically, "Vertikal verteilen", () => run("Verteilen", editor => editor.Session.Distribute(horizontal: false, toSlide: editor.Session.Selection.Shapes.Count < 3)))
        {
            Icon = "line-spacing", CanExecute = HasSeveral, Description = "Verteilt die markierten Formen in gleichen Abständen untereinander.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.Rotate, "Drehen", () => Task.CompletedTask)
        {
            Icon = "refresh", CanExecute = HasShapes, IsSearchable = false, Description = "Dreht oder spiegelt die markierten Formen.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.RotateRight, "Rechtsdrehung 90°", () => run("Drehen", editor => editor.Session.Rotate(90, relative: true)))
        {
            Icon = "refresh", CanExecute = HasShapes, Description = "Dreht die markierten Formen um 90° im Uhrzeigersinn.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.RotateLeft, "Linksdrehung 90°", () => run("Drehen", editor => editor.Session.Rotate(-90, relative: true)))
        {
            Icon = "undo", CanExecute = HasShapes, Description = "Dreht die markierten Formen um 90° gegen den Uhrzeigersinn.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.FlipHorizontal, "Horizontal spiegeln", () => run("Spiegeln", editor => editor.Session.Flip(horizontal: true)))
        {
            Icon = "columns", CanExecute = HasShapes, Description = "Spiegelt die markierten Formen von links nach rechts.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.FlipVertical, "Vertikal spiegeln", () => run("Spiegeln", editor => editor.Session.Flip(horizontal: false)))
        {
            Icon = "line-spacing", CanExecute = HasShapes, Description = "Spiegelt die markierten Formen von oben nach unten.",
        });

        // ===== Formformat =====
        commands.Add(new OfficeValueCommand(ShowCommandIds.QuickStyles, "Schnellformatvorlagen", OfficeValueKind.Choice, () => null, value => run("Schnellformatvorlage", editor =>
        {
            if (value is not { Length: > 0 } || !int.TryParse(value[^1..], CultureInfo.InvariantCulture, out var accent))
            {
                return;
            }

            var color = ColorSpec.Scheme($"accent{accent}");
            editor.Session.Group("Schnellformatvorlage", () =>
            {
                if (value.StartsWith("fill", StringComparison.Ordinal))
                {
                    editor.Session.SetFill(new SolidFill(color));
                    editor.Session.SetOutline(outline => outline with { Fill = new SolidFill(color.LumMod(50000)), Width = 12700 });
                }
                else
                {
                    editor.Session.SetFill(NoFill.Instance);
                    editor.Session.SetOutline(outline => outline with { Fill = new SolidFill(color), Width = 19050 });
                }
            });
        }))
        {
            Icon = "palette", CanExecute = HasShapes, Description = "Gibt den Formen Füllung und Kontur in einer Akzentfarbe des Designs.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.ShapeFill, "Fülleffekt", OfficeValueKind.Color, () => editorOf()?.FillColor, value => run("Fülleffekt", editor =>
        {
            editor.FillColor = value;
            editor.Session.SetFill(ShowPalette.ToColorSpec(value) is { } color ? new SolidFill(color) : NoFill.Instance);
        }))
        {
            Icon = "fill", CanExecute = HasShapes, Description = "Füllt die markierten Formen mit einer Farbe.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.ShapeOutline, "Formkontur", OfficeValueKind.Color, () => editorOf()?.OutlineColor, value => run("Formkontur", editor =>
        {
            editor.OutlineColor = value;
            editor.Session.SetOutline(outline => outline with { Fill = ShowPalette.ToColorSpec(value) is { } color ? new SolidFill(color) : NoFill.Instance });
        }))
        {
            Icon = "edit", CanExecute = HasShapes, Description = "Gibt den markierten Formen eine Kontur in einer Farbe.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.ShapeShadow, "Schatten", () => run("Formeffekte", editor =>
        {
            var hasShadow = editor.Session.CurrentSlide is { } slide && editor.Session.Selection.Shapes.Any(id => EffectsOf(slide.FindShape(id))?.Shadow is not null);
            editor.Session.SetShadow(hasShadow ? null : new OuterShadow(50800, 38100, 2700000, ColorSpec.Rgb("000000").Alpha(40000), "tl", false));
        }))
        {
            Icon = "layers", CanExecute = HasShapes,
            IsChecked = () => editorOf() is { } editor && editor.Session.CurrentSlide is { } slide && editor.Session.Selection.Shapes.Any(id => EffectsOf(slide.FindShape(id))?.Shadow is not null),
            Description = "Formeffekte: ein weicher Schatten unter den markierten Formen.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.ChangeShape, "Form ändern", OfficeValueKind.Choice, () => null, value => run("Form ändern", editor =>
        {
            if (!string.IsNullOrEmpty(value) && !ShapeCatalog.IsLine(value))
            {
                editor.Session.SetGeometry(value);
            }
        }))
        {
            Icon = "shapes", CanExecute = () => editorOf() is { } editor && editor.Session.CurrentSlide is { } slide && editor.Session.Selection.Shapes.Any(id => slide.FindShape(id) is AutoShape),
            Description = "Gibt den markierten Formen eine andere Form; Text und Formatierung bleiben.",
        });
    }
}
