using ThixOffice.Charts;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// The sample presentation of the snapshots, the README pictures and the self test: "Nordlicht Reisen – Jahresrückblick 2026" in
/// ThixShow's design – a title slide, a list with levels and an animation, a chart, a picture beside a list, a table, a section
/// header; transitions and speaker notes. Built through the editing session like a user would.
/// </summary>
public static class SampleDeck
{
    public const string Title = "Nordlicht Reisen";

    public static Presentation Create(ThemeDesign? design = null, SlideSize? size = null)
    {
        var session = new EditingSession(MasterFactory.CreatePresentation(design ?? BuiltInThemes.ThixShow, size));
        var presentation = session.Presentation;
        session.GoToSlide(presentation.Slides[0].Id);
        var layouts = presentation.Masters[0].Layouts;
        uint LayoutOf(string type) => layouts.FirstOrDefault(layout => layout.Type == type)?.Id ?? layouts[0].Id;

        // 1 Titelfolie
        Fill(session, ["Nordlicht Reisen"], ["Jahresrückblick 2026 · Vertriebsmeeting"]);
        session.SetNotesText(session.Presentation.Slides[0].Id, "Begrüßung, kurz vorstellen. Ziel des Termins: Rückblick und Ausblick in 20 Minuten.");

        // 2 Titel und Inhalt: a list with a second level, appearing on clicks
        session.AddSlide(LayoutOf("obj"));
        Fill(session, ["Was 2026 gut lief"],
        [
            "Buchungen +18 % gegenüber dem Vorjahr", "\tStärkstes Quartal: Sommer", "Neue Ziele in Skandinavien", "\tLofoten, Abisko, Tromsø",
            "Kundenzufriedenheit 4,7 von 5",
        ]);
        AnimateBody(session);
        session.SetTransition([session.Selection.CurrentSlide!.Value], new SlideTransition(TransitionKind.Fade));
        session.SetNotesText(session.Selection.CurrentSlide!.Value, "Die Zahlen stammen aus dem Buchungssystem, Stand 30. November.");

        // 3 Diagramm
        session.AddSlide(LayoutOf("titleOnly"));
        Fill(session, ["Buchungen nach Quartal"], null);
        var quarters = new[] { "Q1", "Q2", "Q3", "Q4" };
        var chart = ChartFactory.Create(ChartTemplates.Find("column")!,
        [
            new SeriesSpec(ChartText.Of("2025"), ChartDataRef.LiteralText(quarters), ChartDataRef.Literal([820, 1140, 1610, 930])),
            new SeriesSpec(ChartText.Of("2026"), ChartDataRef.LiteralText(quarters), ChartDataRef.Literal([905, 1320, 1985, 1090])),
        ]);
        var size2 = session.Presentation.SlideSize;
        session.InsertChart(chart, new EmuRect(size2.Width / 12, size2.Height * 26 / 100, size2.Width * 10 / 12, size2.Height * 64 / 100));
        session.SetTransition([session.Selection.CurrentSlide!.Value], new SlideTransition(TransitionKind.Push, "u"));

        // 4 Zwei Inhalte: picture and list
        session.AddSlide(LayoutOf("twoObj"));
        Fill(session, ["Neue Ziele im Norden"], ["Hüttenwanderungen mit Polarlicht", "Kleine Gruppen bis 12 Personen", "Anreise mit dem Nachtzug"], second: true);
        var picture = ImageData.From(ShowPalette.SamplePicture());
        var slide = session.CurrentSlide!;
        var left = slide.Shapes.OfType<AutoShape>().Where(shape => shape.Placeholder is { IsTitle: false }).OrderBy(shape => shape.Id).FirstOrDefault();
        if (left is not null)
        {
            session.SelectShapes([left.Id]);
        }

        session.InsertPicture(picture, description: "Berglandschaft unter Polarlicht");

        // 5 Tabelle
        session.AddSlide(LayoutOf("titleOnly"));
        Fill(session, ["Ausblick 2027"], null);
        var table = session.InsertTable(4, 3);
        string[][] cells = [["Ziel", "Termin", "Verantwortlich"], ["Island im Winter", "Februar", "Anna Berg"], ["Fahrradreisen Dänemark", "Mai", "Jonas Weber"], ["Buchungsportal neu", "September", "Team Digital"]];
        for (var row = 0; row < cells.Length; row++)
        {
            for (var column = 0; column < cells[row].Length; column++)
            {
                var target = new TextTarget(table, row, column);
                session.SelectText(target, new TextPosition(0, 0), new TextPosition(0, 0));
                session.InsertText(cells[row][column]);
                session.CloseOpenStep();
            }
        }

        session.EndTextEditing();

        // 6 Abschnittsüberschrift
        session.AddSlide(LayoutOf("secHead"));
        Fill(session, ["Fragen und Diskussion"], ["Vielen Dank!"]);
        session.SetTransition([session.Selection.CurrentSlide!.Value], new SlideTransition(TransitionKind.Fade, ThroughBlack: true));

        session.GoToSlide(session.Presentation.Slides[0].Id);
        return session.Presentation with { Title = "Jahresrückblick 2026", Author = "Anna Berg" };
    }

    /// <summary>Types the title and the body (or the second content placeholder) of the current slide; "\t" starts a level deeper.</summary>
    private static void Fill(EditingSession session, string[] title, string[]? body, bool second = false)
    {
        var slide = session.CurrentSlide!;
        var placeholders = slide.Shapes.OfType<AutoShape>().Where(shape => shape.Placeholder is not null).OrderBy(shape => shape.Id).ToList();
        var titleShape = placeholders.FirstOrDefault(shape => shape.Placeholder!.IsTitle);
        var bodies = placeholders.Where(shape => !shape.Placeholder!.IsTitle).ToList();
        if (titleShape is not null)
        {
            Type(session, titleShape.Id, title);
        }

        if (body is not null && bodies.Count > 0)
        {
            Type(session, (second && bodies.Count > 1 ? bodies[1] : bodies[0]).Id, body);
        }

        session.EndTextEditing();
    }

    private static void Type(EditingSession session, int shapeId, string[] lines)
    {
        var target = new TextTarget(shapeId);
        session.SelectText(target, new TextPosition(0, 0), new TextPosition(0, 0));
        for (var index = 0; index < lines.Length; index++)
        {
            if (index > 0)
            {
                session.InsertParagraphBreak();
            }

            var line = lines[index];
            if (line.StartsWith('\t'))
            {
                session.HandleTab(shift: false);
                line = line[1..];
            }
            else if (index > 0 && session.GetFormatting().Level.Value > 0)
            {
                session.HandleTab(shift: true);
            }

            session.InsertText(line);
        }

        session.CloseOpenStep();
    }

    /// <summary>The body of the current slide flies in from the bottom on a click.</summary>
    private static void AnimateBody(EditingSession session)
    {
        var body = session.CurrentSlide!.Shapes.OfType<AutoShape>().FirstOrDefault(shape => shape.Placeholder is { IsTitle: false });
        if (body is null)
        {
            return;
        }

        session.SelectShapes([body.Id]);
        session.AddAnimation(AnimationPreset.FlyIn, AnimationDirection.Bottom);
        session.SelectShapes([]);
    }
}
