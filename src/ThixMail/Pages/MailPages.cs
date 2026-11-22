using System.Net;
using ThixMail.Core.Mime;
using ThixMail.Core.Security;
using ThixMail.Editor;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Desktop.Shell;
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

/// <summary>Datei › Drucken of ThixMail: the message shown in the reading pane, in Outlook's memo style.</summary>
public sealed class MailPrintPage(OfficeWindow window, MailEditor editor) : OfficePrintPage(window, new MailPrintSource(editor))
{
    public static BackstageItem Item(OfficeWindow window) =>
        new(PageId, "Drucken", "print", () => new MailPrintPage(window, (MailEditor)window.Editor), Shortcut: window.Commands.Find(MailCommandIds.Print)?.CurrentShortcut);

    private sealed class MailPrintSource(MailEditor editor) : IPrintSource
    {
        public IReadOnlyList<string> Scopes => ["Memoformat"];

        public int Scope { get; set; }

        public int CurrentPage => 0;

        public Func<Task<bool>>? PageSetup => null;

        public IReadOnlyList<string> Variants => [];

        public string VariantLabel => "";

        public int Variant { get; set; }

        public PrintablePages Snapshot()
        {
            var mail = editor.Reader.Mail ?? new ReadMail { Subject = "Keine Nachricht ausgewählt" };
            return new PrintablePages(MailPrinting.Pages(mail, editor.LoadedImagesOfShown), OfficeFonts.Skia, mail.Subject, DocumentPalette.PaperSkia);
        }
    }
}

/// <summary>Datei › Als PDF speichern: the message shown, as on paper.</summary>
public sealed class MailPdfPage(OfficeWindow window) : OfficeExportPage(window,
    () => MailPrinting.Pages(((MailEditor)window.Editor).Reader.Mail ?? new ReadMail { Subject = "Keine Nachricht ausgewählt" }, ((MailEditor)window.Editor).LoadedImagesOfShown),
    OfficeFonts.Skia, "Die ausgewählte Nachricht wie gedruckt – Kopf, Text und Bilder")
{
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Als PDF speichern", "export", () => new MailPdfPage(window));
}
