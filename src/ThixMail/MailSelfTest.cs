using Avalonia.Threading;
using ThixMail.Core;
using ThixMail.Core.Accounts;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Security;
using ThixMail.Core.Store;
using ThixMail.Core.Transport;
using ThixMail.Editor;
using ThixMail.Pages;
using ThixOffice.Desktop.Rendering;
using ThixOffice.Text.Skia;

namespace ThixMail;

/// <summary>
/// ThixMail's part of "--self-test" (the published, trimmed program): the main window over the store (SQLite with FTS5 from its native library),
/// an account on a mail server in memory (connection test, sync, IDLE-free round), reading every kind of MIME (legacy charsets through the code pages,
/// the HTML made safe and laid out by ThixWrite's engine and drawn with Skia), sending through the outbox, a draft, the full-text search, MailKit's
/// types alive after trimming (a MIME message parsed and written by MimeKit, an IMAP and SMTP client made), and a PDF of a message.
/// </summary>
public static class MailSelfTest
{
    public static async Task<string> RunAsync(App app)
    {
        Editor.MailReminders.CalendarRunsForTests ??= () => true; // no reminder window of the sample during the self test
        var window = app.NewDocumentWindow();
        await Settle();
        var editor = (MailEditor)window.Editor;
        var document = editor.Document;
        var previous = MailDocument.Transport;
        var server = SampleMailbox.CreateServer();
        try
        {
            // the connection test and the account
            var test = await AccountService.TestAsync(server, SampleMailbox.Account, SampleMailbox.Password, SampleMailbox.Password, CancellationToken.None);
            Ensure(test.Succeeded, $"the connection test failed: {test.Message}");
            var wrong = await AccountService.TestAsync(server, SampleMailbox.Account, "falsch", "falsch", CancellationToken.None);
            Ensure(wrong.Kind == MailErrorKind.Authentication, "a wrong password was not recognized");
            await SampleMailbox.FillAsync(document, server);
            await Settle();
            var inbox = document.Store.FolderByRole(SampleMailbox.Account.Id, FolderRole.Inbox)!;
            var count = document.Store.List(new MessageQuery([inbox.Id])).Count;
            Ensure(count >= 8, $"the inbox has {count} messages after the sync");
            Ensure(editor.Rows.Count > 0, "the list shows nothing");

            // the search (FTS5) and reading with the safe HTML through ThixWrite's layout
            Ensure(document.Store.List(new MessageQuery([inbox.Id], Search: "herbstkollektion")).Count == 1, "the full-text search did not find the newsletter");
            var newsletter = document.Store.List(new MessageQuery([inbox.Id], Search: "herbstkollektion")).Single().Id;
            var raw = document.Store.Raw(newsletter) ?? throw new InvalidOperationException("the newsletter's body was not cached");
            var mail = MailReader.Read(raw);
            var view = MailHtml.ToView(mail, 500, null, ThixWrite.Editor.WriteFonts.Default);
            Ensure(view.BlockedImages.Count >= 3 && view.Document.GetPlainText().Contains("Herbstkollektion", StringComparison.Ordinal), "the newsletter did not render safely");
            var png = SkiaPageRenderer.RenderPng(new ThixWrite.Core.Layout.DocumentLayout(view.Document, ThixWrite.Editor.WriteFonts.Measurer).Also(l => l.Update()).GetDisplayList(0), 600, ThixWrite.Editor.WriteFonts.Measurer);
            Ensure(png.Length > 1000, "the body could not be drawn with Skia");
            editor.SelectFolder(null);
            await Settle();
            editor.Select(newsletter);
            await Settle(10);

            // legacy charsets through the code pages, MimeKit after trimming
            var latin = "From: =?ISO-8859-1?Q?J=FCrgen?= <j@example.at>\r\nSubject: =?ISO-8859-1?Q?Gr=FC=DFe?=\r\nContent-Type: text/plain; charset=ISO-8859-1\r\n\r\n"u8.ToArray()
                .Concat(System.Text.Encoding.Latin1.GetBytes("Schöne Grüße")).ToArray();
            var latinMail = MailReader.Read(latin);
            Ensure(latinMail.Subject == "Grüße" && latinMail.Text.Contains("Schöne Grüße", StringComparison.Ordinal), "ISO-8859-1 was not decoded");
            var jis = MailReader.Read("Subject: =?ISO-2022-JP?B?GyRCMnE1RCROO3FOQRsoQg==?=\r\n\r\nx"u8.ToArray());
            Ensure(jis.Subject == "会議の資料", $"ISO-2022-JP was not decoded ({jis.Subject})");
            using (var imap = new MailKit.Net.Imap.ImapClient())
            using (var smtp = new MailKit.Net.Smtp.SmtpClient())
            {
                Ensure(!imap.IsConnected && !smtp.IsConnected, "MailKit's clients are not usable");
            }

            // sending through the outbox, a draft
            var account = document.Accounts.Account(SampleMailbox.Account.Id)!;
            var reply = MailComposer.Reply(mail, account, all: false, newsletter) with { Html = "<p>Danke!</p>", Text = "Danke!" };
            document.Session.Send(reply);
            await document.SyncOf(account.Id)!.SyncOnceAsync();
            Ensure(server.Sent.Count == 1 && document.Store.Outbox().Count == 0, "the reply was not sent");
            var draft = document.Session.SaveDraft(new Draft { AccountId = account.Id, To = [new MailAddress("", "lena.koch@beispiel.de")], Subject = "Selbsttest-Entwurf", Html = "<p>Entwurf</p>" });
            await document.SyncOf(account.Id)!.SyncOnceAsync();
            Ensure(server.SubjectsOf(SampleMailbox.Address, "Drafts").Contains("Selbsttest-Entwurf"), "the draft did not reach the server");
            Ensure(draft.SavedDraft is not null, "the draft has no copy in the store");

            // a message as PDF
            var pages = MailPrinting.Pages(mail, null);
            using var pdf = new MemoryStream();
            PdfExport.Export(pages, pdf, null, ThixWrite.Editor.WriteFonts.Measurer);
            Ensure(pdf.Length > 1000, "the PDF of a message is empty");

            // 7c: invitations (iCalendar both ways) and ThixCalendar's items (the contract's JSON, trimmed publish included)
            var invitation = ThixOffice.Pim.Calendar.Invitations.Read(ThixOffice.Pim.Calendar.Invitations.Request(new ThixOffice.Pim.Calendar.Invitation
            {
                Uid = "selbsttest@thixmail",
                Summary = "Selbsttest",
                Start = DateTimeOffset.UtcNow,
                End = DateTimeOffset.UtcNow.AddHours(1),
                Organizer = new ThixOffice.Pim.Calendar.InvitationAttendee("a@example.de", "A"),
                Attendees = [new ThixOffice.Pim.Calendar.InvitationAttendee("b@example.de", "B")],
            }));
            Ensure(invitation is { Summary: "Selbsttest", Attendees.Count: 1 }, "an invitation does not survive iCalendar");
            var item = ThixOffice.Pim.Calendar.CalendarItems.Serialize(new ThixOffice.Pim.Calendar.CalendarAppointment { CalendarKey = "a/b", Title = "x", Start = DateTimeOffset.UnixEpoch, End = DateTimeOffset.UnixEpoch });
            Ensure(item.StartsWith("{\"calendar\":\"a/b\",\"title\":\"x\",", StringComparison.Ordinal) && item.Contains("\"status\":\"Confirmed\"", StringComparison.Ordinal), "the calendar's items are not ThixCalendar's");
            editor.ShowView(Editor.MailArea.Calendar);
            await Settle();
            Ensure(editor.SignInPanel is not null || editor.CalendarView is not null, "the view Kalender does not show");
            editor.ShowView(Editor.MailArea.Mail);

            // 7d: the calendar on paper (Wochen- and Monatsformat through Skia into a PDF)
            var sample = new ThixOffice.Pim.Calendar.CalendarAppointment { Key = "s", CalendarKey = "a/b", Title = "Selbsttest", Start = ThixOffice.Pim.Calendar.CalendarAppointment.FromLocal(DateTime.Today.AddHours(10)), End = ThixOffice.Pim.Calendar.CalendarAppointment.FromLocal(DateTime.Today.AddHours(11)) };
            var week = ThixOffice.Pim.Desktop.Calendar.CalendarPrinting.Pages(ThixOffice.Pim.Desktop.Calendar.CalendarPrintStyle.Week, DateTime.Today, false, [sample], _ => null, OfficeFonts.Skia);
            var month = ThixOffice.Pim.Desktop.Calendar.CalendarPrinting.Pages(ThixOffice.Pim.Desktop.Calendar.CalendarPrintStyle.Month, DateTime.Today, false, [sample], _ => null, OfficeFonts.Skia);
            using var calendarPdf = new MemoryStream();
            PdfExport.Export([.. week, .. month], calendarPdf, null, OfficeFonts.Skia);
            Ensure(calendarPdf.Length > 1000 && week[0].Items.OfType<ThixOffice.Text.Drawing.GlyphRunItem>().Any(g => g.Text.StartsWith("Selbsttest", StringComparison.Ordinal)), "the calendar does not print");

            return $"{count} messages synced, {server.Sent.Count} sent, PDF {pdf.Length / 1024} KB, {document.Store.CachedBytes / 1024} KB cached";
        }
        finally
        {
            MailDocument.Transport = previous;
            window.Close();
            await Settle();
        }
    }

    private static T Also<T>(this T value, Action<T> action)
    {
        action(value);
        return value;
    }

    private static void Ensure(bool condition, string message)
    {
        if (!condition)
        {
            throw new InvalidOperationException(message);
        }
    }

    private static async Task Settle(int rounds = 5)
    {
        for (var round = 0; round < rounds; round++)
        {
            await Dispatcher.UIThread.InvokeAsync(() => { }, DispatcherPriority.Background);
            await Task.Delay(40);
        }
    }
}
