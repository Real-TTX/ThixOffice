using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Model;

namespace ThixWrite.Editor;

/// <summary>
/// The document of the snapshots (and the README pictures): a short project report with a title, headings, a bulleted and a
/// numbered list, bold, coloured and highlighted text and a quote – built through the editing session like a user would.
/// </summary>
public static class SampleDocument
{
    /// <summary>The block of the first body paragraph (after title, subtitle and the first heading).</summary>
    public const int BodyParagraph = 3;

    public static Document Create(DocumentFonts fonts)
    {
        var document = BlankDocument.Create(fonts);
        var session = new EditingSession(document);

        Paragraph(session, BuiltInStyles.Title, "Projektbericht November");
        Paragraph(session, BuiltInStyles.Subtitle, "Webshop – die ersten zwei Wochen");
        Paragraph(session, BuiltInStyles.Heading1, "Zusammenfassung");

        session.ApplyStyle(BuiltInStyles.Normal);
        session.InsertText("Der neue Webshop ist seit dem 3. November online. In den ersten zwei Wochen kamen ");
        Formatted(session, "412 Bestellungen", s => s.SetBold(true), s => s.SetBold(false));
        session.InsertText(", ein Viertel davon über das Smartphone. Die Rückgabequote liegt mit ");
        Formatted(session, "4,8 %", s => s.SetTextColor(DocumentPalette.StandardColors[5].TrimStart('#')), s => s.SetTextColor(null));
        session.InsertText(" deutlich unter dem Ziel von 8 %");
        Note(session, NoteKind.Footnote, "Ziel aus dem Projektplan vom 12. September.");
        session.InsertText(". ");
        Formatted(session, "Wichtig:", s => s.SetHighlight(HighlightColor.Yellow), s => s.SetHighlight(HighlightColor.None));
        session.InsertText(" Die Versandkosten für Österreich und die Schweiz sind noch offen.");
        session.InsertParagraphBreak();

        Paragraph(session, BuiltInStyles.Heading1, "Nächste Schritte");
        session.ApplyStyle(BuiltInStyles.Normal);
        session.ToggleList(ListKind.Bullet);
        session.InsertText("Zahlungsarten erweitern (Rechnung, PayPal)");
        session.InsertParagraphBreak();
        session.InsertText("Versandkosten für Österreich und die Schweiz klären");
        session.InsertParagraphBreak();
        session.InsertText("Newsletter zum Weihnachtsgeschäft vorbereiten");
        session.InsertParagraphBreak();
        session.RemoveList();

        Paragraph(session, BuiltInStyles.Heading2, "Zeitplan");
        session.ApplyStyle(BuiltInStyles.Normal);
        session.ToggleList(ListKind.Numbered);
        session.InsertText("Zahlungsarten bis zum 30. November");
        session.InsertParagraphBreak();
        session.InsertText("Newsletter am 5. Dezember");
        session.InsertParagraphBreak();
        session.RemoveList();

        Paragraph(session, BuiltInStyles.Heading2, "Kennzahlen");
        session.ApplyStyle(BuiltInStyles.Normal);
        session.InsertTable(4, 3);
        string[] cells = ["Kennzahl", "Ist", "Ziel", "Bestellungen", "412", "350", "Rückgabequote", "4,8 %", "8 %", "Anteil Smartphone", "25 %", "30 %"];
        for (var index = 0; index < cells.Length; index++)
        {
            if (index > 0)
            {
                session.HandleTab(shift: false);
            }

            session.InsertText(cells[index]);
        }

        session.SetCaret(new DocumentPosition(session.CurrentTable!.Start, 0));
        session.SelectTableRow();
        session.SetBold(true);
        session.SetTableCellShading("D9E2F3");
        session.SetRepeatHeaderRows(true);
        session.SetCaret(new DocumentPosition(session.CurrentTable!.End + 1, 0));

        // A chart as a picture, centred, with its alternative text.
        session.SetAlignment(ParagraphAlignment.Center);
        session.InsertImage(ImageData.FromBytes(ChartPng())!);
        session.SetImageSize(300, 150);
        session.SetImageDescription("Bestellungen pro Tag in den ersten zwei Wochen");
        session.SetCaret(session.Selection.End);
        session.InsertParagraphBreak();
        session.SetAlignment(ParagraphAlignment.Left);

        session.ApplyStyle(BuiltInStyles.Quote);
        session.InsertText("„Der Start ist besser gelaufen, als wir erwartet hatten.“");
        session.InsertParagraphBreak();
        session.ApplyStyle(BuiltInStyles.Normal);
        session.InsertText("Anna Berger");

        // A header with the title at the right and the page number in the footer.
        var header = session.NewHeaderFooter(footer: false);
        var title = (Paragraph)header.Blocks[0];
        session.SetHeaderFooter(0, HeaderFooterKind.Default, false, header.WithBlocks([
            title.WithInlines([new TextRun("Webshop – Projektbericht", new CharacterProperties { Color = TextColor.FromHex("7F7F7F") })]).WithProperties(title.Properties with { Alignment = ParagraphAlignment.Right })]));
        session.InsertPageNumber(top: false, ParagraphAlignment.Center);

        session.ClearHistory();
        return document;
    }

    /// <summary>A bar chart of fourteen days (drawn with Skia, as a PNG) – the picture of the sample report.</summary>
    public static byte[] ChartPng()
    {
        int[] orders = [18, 22, 25, 31, 28, 24, 30, 33, 29, 35, 38, 34, 32, 33];
        using var bitmap = new SkiaSharp.SKBitmap(600, 300);
        using (var canvas = new SkiaSharp.SKCanvas(bitmap))
        {
            canvas.Clear(DocumentPalette.PaperSkia);
            using var grid = new SkiaSharp.SKPaint { Color = DocumentPalette.SampleChart[1], StrokeWidth = 2 };
            for (var line = 0; line <= 4; line++)
            {
                canvas.DrawLine(20, 280 - (line * 60), 580, 280 - (line * 60), grid);
            }

            using var bar = new SkiaSharp.SKPaint { Color = DocumentPalette.SampleChart[0], IsAntialias = true };
            for (var day = 0; day < orders.Length; day++)
            {
                var height = orders[day] * 6f;
                canvas.DrawRoundRect(new SkiaSharp.SKRect(32 + (day * 39), 280 - height, 62 + (day * 39), 280), 3, 3, bar);
            }

            using var axis = new SkiaSharp.SKPaint { Color = DocumentPalette.SampleChart[2], StrokeWidth = 3 };
            canvas.DrawLine(20, 280, 580, 280, axis);
        }

        using var data = bitmap.Encode(SkiaSharp.SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static void Paragraph(EditingSession session, string style, string text)
    {
        session.ApplyStyle(style);
        session.InsertText(text);
        session.InsertParagraphBreak();
    }

    /// <summary>A note at the caret with its text, the caret behind its reference.</summary>
    public static void Note(EditingSession session, NoteKind kind, string text)
    {
        var reference = session.InsertNote(kind);
        var content = NoteNumbering.NewContent(kind);
        var paragraph = (Paragraph)content.Blocks[0];
        session.SetNoteContent(reference, content.WithBlocks([paragraph.WithInlines([paragraph.Inlines[0], new TextRun(" " + text, CharacterProperties.Empty)])]));
    }

    private static void Formatted(EditingSession session, string text, Action<EditingSession> on, Action<EditingSession> off)
    {
        on(session);
        session.InsertText(text);
        off(session);
    }
}
