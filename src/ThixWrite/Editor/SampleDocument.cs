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
        session.InsertText(" deutlich unter dem Ziel von 8 %. ");
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

        session.ApplyStyle(BuiltInStyles.Quote);
        session.InsertText("„Der Start ist besser gelaufen, als wir erwartet hatten.“");
        session.InsertParagraphBreak();
        session.ApplyStyle(BuiltInStyles.Normal);
        session.InsertText("Anna Berger");

        session.ClearHistory();
        return document;
    }

    private static void Paragraph(EditingSession session, string style, string text)
    {
        session.ApplyStyle(style);
        session.InsertText(text);
        session.InsertParagraphBreak();
    }

    private static void Formatted(EditingSession session, string text, Action<EditingSession> on, Action<EditingSession> off)
    {
        on(session);
        session.InsertText(text);
        off(session);
    }
}
