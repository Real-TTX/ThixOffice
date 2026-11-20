using System.Globalization;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// Einfügen › Formen (the presets of the shared geometry, drawn by dragging) and Textfeld, and the contextual tab Formformat (shown while
/// a shape is selected): Fülleffekt, Formkontur, Text bearbeiten, Textumbruch, Höhe and Breite.
/// </summary>
public static class ShapeCommands
{
    /// <summary>The context of the contextual tab.</summary>
    public const string Context = "shape";

    private const double PointsPerCm = 72 / 2.54;

    /// <summary>The shapes Einfügen › Formen offers (preset of the shared geometry, German name), in Word's order.</summary>
    public static readonly IReadOnlyList<(string Preset, string Label)> Presets =
    [
        ("rect", "Rechteck"), ("roundRect", "Abgerundetes Rechteck"), ("ellipse", "Ellipse"), ("triangle", "Gleichschenkliges Dreieck"),
        ("rtTriangle", "Rechtwinkliges Dreieck"), ("diamond", "Raute"), ("pentagon", "Fünfeck"), ("hexagon", "Sechseck"), ("octagon", "Achteck"),
        ("parallelogram", "Parallelogramm"), ("trapezoid", "Trapez"), ("can", "Zylinder"), ("cube", "Würfel"), ("heart", "Herz"), ("smileyFace", "Smiley"),
        ("rightArrow", "Pfeil: nach rechts"), ("leftArrow", "Pfeil: nach links"), ("upArrow", "Pfeil: nach oben"), ("downArrow", "Pfeil: nach unten"),
        ("leftRightArrow", "Pfeil: nach links und rechts"), ("chevron", "Pfeil: Chevron"), ("star5", "Stern: 5 Zacken"), ("star8", "Stern: 8 Zacken"),
        ("wedgeRectCallout", "Sprechblase: rechteckig"), ("wedgeEllipseCallout", "Sprechblase: oval"),
        ("flowChartProcess", "Flussdiagramm: Prozess"), ("flowChartDecision", "Flussdiagramm: Verzweigung"), ("flowChartTerminator", "Flussdiagramm: Grenzstelle"),
    ];

    public static void Add(OfficeWindow window)
    {
        WriteEditor? Editor() => window.Editor as WriteEditor;
        bool HasEditor() => Editor() is not null;
        bool Selected() => Editor()?.Session.SelectedShape is not null;
        ShapeInline? Shape() => Editor()?.Session.SelectedShape;
        var commands = window.Commands;

        commands.Add(new OfficeValueCommand(WriteCommandIds.InsertShape, "Formen", OfficeValueKind.Choice, () => null, value =>
        {
            if (!string.IsNullOrEmpty(value))
            {
                Editor()?.BeginDrawShape(value, textBox: false);
            }
        })
        {
            Icon = "shapes", CanExecute = HasEditor, Options = [.. Presets.Select(preset => preset.Preset)],
            Description = "Zeichnet eine Form (Rechteck, Kreis, Pfeil, Stern …): wählen und auf der Seite aufziehen.", Keywords = ["form", "rechteck", "kreis", "pfeil", "stern", "zeichnen"],
        });
        foreach (var (preset, label) in Presets)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.InsertShape}.{preset}", label, () => Editor()?.BeginDrawShape(preset, textBox: false))
            {
                Icon = IconOf(preset), CanExecute = HasEditor, IsSearchable = false,
            });
        }

        commands.Add(new OfficeCommand(WriteCommandIds.InsertTextBox, "Textfeld", () => Editor()?.BeginDrawShape("rect", textBox: true))
        {
            Icon = "text-box", CanExecute = HasEditor,
            Description = "Zeichnet ein Textfeld, das frei auf der Seite steht.", Keywords = ["textfeld", "kasten", "textbox"],
        });

        commands.Add(new OfficeValueCommand(WriteCommandIds.ShapeFill, "Fülleffekt", OfficeValueKind.Color,
            () => Shape()?.Shape.Fill?.Color?.Rgb is { } rgb ? "#" + rgb : null,
            value => Editor()?.Session.SetShapeFill(string.IsNullOrEmpty(value) ? null : new DrawingFill(DrawingColor.Of(value.TrimStart('#').ToUpperInvariant()))))
        {
            Icon = "fill", CanExecute = Selected,
            Description = "Füllt die Form mit einer Farbe – oder lässt sie leer.", Keywords = ["füllfarbe", "farbe", "form"],
        });
        commands.Add(new OfficeValueCommand(WriteCommandIds.ShapeLine, "Formkontur", OfficeValueKind.Color,
            () => Shape()?.Shape.Line?.Color.Rgb is { } rgb ? "#" + rgb : null,
            value => Editor()?.Session.SetShapeLine(string.IsNullOrEmpty(value) ? null
                : new DrawingLine(DrawingColor.Of(value.TrimStart('#').ToUpperInvariant()), Shape()?.Shape.Line?.Width ?? 1)))
        {
            Icon = "edit", CanExecute = Selected,
            Description = "Die Farbe der Linie um die Form – oder keine Linie.", Keywords = ["kontur", "rahmen", "linie", "form"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ShapeEditText, "Text bearbeiten", () =>
        {
            if (Editor() is { } editor && editor.Session.SelectedObject is { } selected)
            {
                editor.EditShapeText(selected.Position);
            }
        })
        {
            Icon = "type", CanExecute = Selected,
            Description = "Schreibt Text in die Form (auch: Doppelklick auf die Form).", Keywords = ["text", "beschriften", "form"],
        });
        commands.Add(new OfficeCommand(WriteCommandIds.ShapeWrap, "Textumbruch", () => Task.CompletedTask)
        {
            Icon = "wrap-text", CanExecute = Selected,
            Description = "Legt fest, wie der Text um die Form fließt.", Keywords = ["umbruch", "layoutoptionen", "hinter den text", "vor den text"],
        });
        foreach (var (choiceId, label, icon, wrap) in PictureCommands.WrapChoices)
        {
            commands.Add(new OfficeCommand($"{WriteCommandIds.ShapeWrap}.{choiceId}", label, () =>
            {
                if (Editor() is { } editor && editor.Session.SelectedObject is { } selected)
                {
                    var at = EditingSession.Frame(selected.Object).Anchor is null ? editor.Layout.InlineOffsetsOf(selected.Position) : null;
                    editor.Session.SetObjectWrap(wrap, at);
                }
            })
            {
                Icon = icon, CanExecute = Selected, IsSearchable = false,
                IsChecked = () => Editor()?.Session.SelectedObjectWrap == wrap && Selected(),
            });
        }

        commands.Add(new OfficeValueCommand(WriteCommandIds.ShapeHeight, "Höhe", OfficeValueKind.Number,
            () => Shape() is { } shape ? Centimetres(shape.Height) : null,
            value => Resize(Editor(), value, byWidth: false))
        {
            Icon = "", CanExecute = Selected, IsSearchable = false,
        });
        commands.Add(new OfficeValueCommand(WriteCommandIds.ShapeWidth, "Breite", OfficeValueKind.Number,
            () => Shape() is { } shape ? Centimetres(shape.Width) : null,
            value => Resize(Editor(), value, byWidth: true))
        {
            Icon = "", CanExecute = Selected, IsSearchable = false,
        });
    }

    /// <summary>The nearest icon of the sprite for a preset (the sprite has no picture of every shape).</summary>
    private static string IconOf(string preset) => preset switch
    {
        "rect" or "roundRect" or "flowChartProcess" or "flowChartTerminator" or "cube" => "box",
        "ellipse" or "smileyFace" => "circle",
        "star5" or "star8" => "star",
        "heart" => "heart",
        "rightArrow" or "chevron" => "arrow-right",
        "leftArrow" => "back",
        "upArrow" => "arrow-up",
        "downArrow" => "arrow-down",
        "wedgeRectCallout" or "wedgeEllipseCallout" => "message-square",
        _ => "shapes",
    };

    private static string Centimetres(double points) => Math.Round(points / PointsPerCm, 2).ToString(CultureInfo.InvariantCulture);

    /// <summary>A shape's height or width alone (a shape has no fixed proportions, as in Word).</summary>
    private static void Resize(WriteEditor? editor, string? value, bool byWidth)
    {
        if (editor?.Session.SelectedShape is not { } shape
            || !double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out var centimetres) || centimetres <= 0)
        {
            return;
        }

        var points = centimetres * PointsPerCm;
        editor.Session.SetObjectSize(byWidth ? points : shape.Width, byWidth ? shape.Height : points);
    }
}
