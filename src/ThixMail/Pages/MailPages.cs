using System.Net;
using ThixMail.Core.Mime;
using ThixMail.Core.Security;
using ThixMail.Editor;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
using ThixOffice.Pim.Desktop.Calendar;
using ThixOffice.Text.Drawing;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Layout;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixMail.Pages;

/// <summary>
/// A message on paper (A4, Outlook's "Memoformat"): the header block (Von, Gesendet, An, Cc, Betreff, Anlagen) and the body laid out by ThixWrite's
/// engine – the same pages for the printer, the preview and the PDF. Remote pictures are printed only when they were loaded for the message.
/// </summary>
public static class MailPrinting
{
    public static IReadOnlyList<PageDisplayList> Pages(ReadMail mail, IReadOnlyDictionary<string, byte[]>? remote)
    {
        const double margin = 56.7;
        var a4 = SectionProperties.Default;
        var width = a4.PageWidth - (2 * margin);
        var header = "<p style=\"font-size:15pt\"><b>" + WebUtility.HtmlEncode(mail.Subject) + "</b></p>"
                     + "<p><b>Von:</b> " + WebUtility.HtmlEncode(mail.From.Full) + "<br><b>Gesendet:</b> " + WebUtility.HtmlEncode(MailComposer.DateText(mail.Date))
                     + "<br><b>An:</b> " + WebUtility.HtmlEncode(string.Join("; ", mail.To.Select(a => a.Full)))
                     + (mail.Cc.Count > 0 ? "<br><b>Cc:</b> " + WebUtility.HtmlEncode(string.Join("; ", mail.Cc.Select(a => a.Full))) : "")
                     + (mail.Attachments.Count > 0 ? "<br><b>Anlagen:</b> " + WebUtility.HtmlEncode(string.Join("; ", mail.Attachments.Select(a => $"{a.FileName} ({MessageView.Size(a.Size)})"))) : "")
                     + "</p><hr>";
        var view = MailHtml.ToView(mail with { Html = header + mail.Html }, width, remote, WriteFonts.Default);
        var document = view.Document;
        document.SetFinalSection(document.FinalSection with
        {
            PageWidth = a4.PageWidth,
            PageHeight = a4.PageHeight,
            MarginLeft = margin,
            MarginRight = margin,
            MarginTop = margin,
            MarginBottom = margin,
        });
        var session = new EditingSession(document);
        var layout = new DocumentLayout(document, WriteFonts.Measurer, LayoutOptions.Default, session.Resolver);
        layout.Update();
        return [.. layout.GetDisplayLists()];
    }
}

/// <summary>
/// Datei › Drucken of ThixMail: in the view E-Mail the message shown in the reading pane (Outlook's Memoformat); in the view Kalender the calendar in
/// Tagesformat, Wochenformat or Monatsformat (the arrangement shown is chosen first) around the chosen day.
/// </summary>
public sealed class MailPrintPage(OfficeWindow window, MailEditor editor) : OfficePrintPage(window, new MailPrintSource(editor))
{
    public static BackstageItem Item(OfficeWindow window) =>
        new(PageId, "Drucken", "print", () => new MailPrintPage(window, (MailEditor)window.Editor), Shortcut: window.Commands.Find(MailCommandIds.Print)?.CurrentShortcut);

    /// <summary>The calendar's print styles as the page offers them.</summary>
    public static IReadOnlyList<string> CalendarScopes { get; } = ["Tagesformat", "Wochenformat", "Monatsformat"];

    private sealed class MailPrintSource(MailEditor editor) : IPrintSource
    {
        private readonly CalendarView? _calendar = editor is { ActiveView: MailArea.Calendar, CalendarView: { } view } && editor.Document.Pim.IsAvailable ? view : null;
        private int _scope = editor is { ActiveView: MailArea.Calendar, CalendarView: { } shown } ? (int)shown.PrintStyle : 0;

        public IReadOnlyList<string> Scopes => _calendar is null ? ["Memoformat"] : CalendarScopes;

        public int Scope
        {
            get => _calendar is null ? 0 : _scope;
            set => _scope = value;
        }

        public int CurrentPage => 0;

        public Func<Task<bool>>? PageSetup => null;

        public IReadOnlyList<string> Variants => [];

        public string VariantLabel => "";

        public int Variant { get; set; }

        public PrintablePages Snapshot()
        {
            if (_calendar is { } calendar)
            {
                var style = (CalendarPrintStyle)Math.Clamp(_scope, 0, 2);
                return new PrintablePages(calendar.PrintPages(style, OfficeFonts.Skia), OfficeFonts.Skia, "Kalender", CalendarPalette.PrintPaper);
            }

            var mail = editor.Reader.Mail ?? new ReadMail { Subject = "Keine Nachricht ausgewählt" };
            return new PrintablePages(MailPrinting.Pages(mail, editor.LoadedImagesOfShown), OfficeFonts.Skia, mail.Subject, DocumentPalette.PaperSkia);
        }
    }
}

/// <summary>Datei › Als PDF speichern: the message shown, as on paper – or, in the view Kalender, the calendar as shown (its print style).</summary>
public sealed class MailPdfPage(OfficeWindow window) : OfficeExportPage(window, () => PagesOf((MailEditor)window.Editor), OfficeFonts.Skia,
    "Die ausgewählte Nachricht wie gedruckt – Kopf, Text und Bilder; in der Ansicht Kalender der gezeigte Zeitraum")
{
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Als PDF speichern", "export", () => new MailPdfPage(window));

    private static IReadOnlyList<PageDisplayList> PagesOf(MailEditor editor) =>
        editor is { ActiveView: MailArea.Calendar, CalendarView: { } calendar } && editor.Document.Pim.IsAvailable
            ? calendar.PrintPages(calendar.PrintStyle, OfficeFonts.Skia)
            : MailPrinting.Pages(editor.Reader.Mail ?? new ReadMail { Subject = "Keine Nachricht ausgewählt" }, editor.LoadedImagesOfShown);
}
