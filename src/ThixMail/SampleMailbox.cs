using System.Text;
using MimeKit;
using ThixMail.Core;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Transport;
using MatCloud.Contract.Contacts;
using ThixOffice.Pim.Calendar;

namespace ThixMail;

/// <summary>
/// The sample mailbox of the snapshots and the self test: Anna Berger's account at a server in memory (<see cref="SampleMailServer"/>) with a
/// realistic inbox – a conversation, a newsletter with blocked pictures, a message with attachments, an invitation, unread and flagged ones, a
/// category – plus Projekte, drafts and sent items. No real server, no real address.
/// </summary>
public static class SampleMailbox
{
    public const string Address = "anna.berger@beispiel.de";
    public const string Password = "beispiel";

    public static MailAccount Account => new()
    {
        Id = "sample",
        Name = "Beispiel GmbH",
        SenderName = "Anna Berger",
        Address = Address,
        Imap = new ServerSettings("imap.beispiel.de", 993, ConnectionSecurity.SslOnConnect),
        ImapUser = Address,
        Smtp = new ServerSettings("smtp.beispiel.de", 587, ConnectionSecurity.StartTls),
        SmtpUser = Address,
    };

    /// <summary>A server with the sample messages (the dates relative to now, so the date groups show).</summary>
    public static SampleMailServer CreateServer()
    {
        var server = new SampleMailServer();
        server.AddMailbox(Address, Password);
        server.AddFolder(Address, "Projekte");
        server.AddFolder(Address, "Projekte/Nordlicht");
        var now = DateTimeOffset.Now;
        var jonas = new MailAddress("Jonas Weber", "jonas.weber@beispiel.de");
        var lena = new MailAddress("Lena Koch", "lena.koch@beispiel.de");
        var maria = new MailAddress("Maria Schulz", "maria.schulz@kunde.example");

        Html(server, "INBOX", maria, "Präsentation für Donnerstag",
            "<p>Hallo Frau Berger,</p><p>anbei die <b>Präsentation</b> für Donnerstag. Die wichtigsten Punkte:</p><ol><li>Zeitplan bis März</li><li>Budget</li><li>Nächste Schritte</li></ol><p>Mit freundlichen Grüßen<br>Maria Schulz</p>",
            now.AddMinutes(-25), MessageFlags.None, attachment: ("Präsentation Nordlicht.pdf", "application/pdf", Pdf()));
        server.Deliver(Address, "INBOX", jonas, "Projekt Nordlicht – Termin", "Hallo Anna,\n\nwann passt dir die Besprechung zum Projekt Nordlicht?\n\nJonas", now.AddHours(-3), MessageFlags.Seen, "<n1@beispiel.de>");
        server.Deliver(Address, "INBOX", new MailAddress("Anna Berger", Address), "AW: Projekt Nordlicht – Termin", "Donnerstag um 14 Uhr?\n\n> wann passt dir die Besprechung zum Projekt Nordlicht?", now.AddHours(-2), MessageFlags.Seen, "<n2@beispiel.de>", "<n1@beispiel.de>");
        server.Deliver(Address, "INBOX", jonas, "AW: AW: Projekt Nordlicht – Termin", "Passt, ich bringe den Beamer mit.\n\nJonas\n\n> Donnerstag um 14 Uhr?", now.AddMinutes(-70), MessageFlags.None, "<n3@beispiel.de>", "<n2@beispiel.de>");
        server.Deliver(Address, "INBOX", SampleMailServerRaw(Fixture03(now.AddHours(-5))), MessageFlags.None);
        Html(server, "INBOX", lena, "Einladung zum Sommerfest",
            "<p>Liebe Kolleginnen und Kollegen,</p><p>am <b>Freitag</b> feiern wir unser Sommerfest im Hof. Ab 17 Uhr wird gegrillt.</p><p>Viele Grüße<br><i>Lena</i></p>",
            now.AddDays(-1).AddHours(-2), MessageFlags.Seen | MessageFlags.Flagged);
        server.Deliver(Address, "INBOX", new MailAddress("Buchhaltung", "buchhaltung@beispiel.de"), "Reisekostenabrechnung Oktober", "Guten Tag Frau Berger,\n\nIhre Abrechnung wurde geprüft und angewiesen.\n\nIhre Buchhaltung", now.AddDays(-2), MessageFlags.Seen);
        server.Deliver(Address, "INBOX", new MailAddress("IT-Service", "it@beispiel.de"), "Wartung am Wochenende", "Am Samstag zwischen 8 und 12 Uhr ist das Netz nicht erreichbar.", now.AddDays(-6), MessageFlags.Seen);
        server.Deliver(Address, "INBOX", new MailAddress("Tanja Roth", "tanja.roth@kunde.example"), "Angebot 2026-118", "Sehr geehrte Frau Berger,\n\nvielen Dank für das Angebot. Wir melden uns bis Ende der Woche.\n\nTanja Roth", now.AddDays(-9), MessageFlags.Seen);
        server.Deliver(Address, "Sent", new MailAddress("Anna Berger", Address), "Angebot 2026-118", "Sehr geehrte Frau Roth,\n\nanbei unser Angebot.\n\nAnna Berger", now.AddDays(-10), MessageFlags.Seen);
        server.Deliver(Address, "Projekte/Nordlicht", jonas, "Protokoll Auftakt", "Das Protokoll des Auftakts liegt im Projektordner.", now.AddDays(-14), MessageFlags.Seen);
        var workshop = WorkshopStart();
        var invitation = new Invitation
        {
            Method = InvitationMethod.Request,
            Uid = "workshop-nordlicht@beispiel.de",
            Summary = "Workshop Nordlicht",
            Start = workshop,
            End = workshop.AddHours(2),
            Location = "Raum 2.14",
            Description = "Wir planen die nächsten Schritte im Projekt.",
            Organizer = new InvitationAttendee(jonas.Address, jonas.Name),
            Attendees = [new InvitationAttendee(Address, "Anna Berger"), new InvitationAttendee(lena.Address, lena.Name)],
        };
        server.Deliver(Address, "INBOX", MailComposer.ToBytes(new Draft
        {
            From = jonas,
            To = [new MailAddress("Anna Berger", Address), lena],
            Subject = "Einladung: Workshop Nordlicht",
            Text = "Hallo Anna, hallo Lena,\n\nich lade euch zum Workshop ein.\n\nJonas",
            Html = "<p>Hallo Anna, hallo Lena,</p><p>ich lade euch zum <b>Workshop Nordlicht</b> ein.</p><p>Jonas</p>",
            MessageId = "<workshop@beispiel.de>",
            Calendar = new CalendarPart("REQUEST", Invitations.Request(invitation)),
        }), MessageFlags.None);
        server.Deliver(Address, "Drafts", new MailAddress("Anna Berger", Address), "Urlaubsvertretung", "Liebe Lena, kannst du mich vom 2. bis 6. vertreten?", now.AddHours(-20), MessageFlags.Seen | MessageFlags.Draft);
        return server;
    }

    /// <summary>The coming Thursday at 14:00 (the workshop of the sample invitation; a week later when it is Thursday today).</summary>
    public static DateTimeOffset WorkshopStart()
    {
        var today = DateTime.Today;
        var thursday = today.AddDays((((int)DayOfWeek.Thursday - (int)today.DayOfWeek) + 7) % 7);
        if (thursday == today)
        {
            thursday = thursday.AddDays(7);
        }

        return CalendarAppointment.FromLocal(thursday.AddHours(14));
    }

    /// <summary>
    /// The calendar and the people of the sample (the views Kalender and Personen of the snapshots): a calendar "Kalender" with a week of appointments
    /// around today (one overlapping the workshop of the invitation), a calendar "Privat", and Anna's colleagues and customers as contacts.
    /// </summary>
    public static void FillPim(MailDocument document)
    {
        if (document.Pim.Calendar is not { } book || document.Pim.People is not { } people)
        {
            return;
        }

        if (book.Appointments.Count == 0)
        {
            var work = book.DefaultCalendar();
            var monday = DateTime.Today.AddDays(-(((int)DateTime.Today.DayOfWeek + 6) % 7));
            var workshopDay = WorkshopStart().ToLocalTime().Date;
            void Add(string calendar, string title, DateTime start, double hours, string location = "", bool allDay = false, int days = 1) => book.Save(new CalendarAppointment
            {
                CalendarKey = calendar,
                Title = title,
                Start = allDay ? CalendarAppointment.AllDayDate(start.Date) : CalendarAppointment.FromLocal(start),
                End = allDay ? CalendarAppointment.AllDayDate(start.Date.AddDays(days)) : CalendarAppointment.FromLocal(start.AddHours(hours)),
                IsAllDay = allDay,
                Location = location,
                ReminderMinutes = allDay ? null : 15,
            });
            Add(work.Key, "Jour fixe", monday.AddHours(9), 0.5, "Teams");
            Add(work.Key, "Projektbesprechung Nordlicht", monday.AddDays(1).AddHours(14), 1.5, "Raum 2.14");
            Add(work.Key, "Messe Köln", monday.AddDays(2), 0, "Koelnmesse, Halle 7", allDay: true, days: 2);
            Add(work.Key, "Mittagessen mit Lena", monday.AddDays(3).AddHours(12), 1, "Kantine");
            Add(work.Key, "Telefonat Kunde", monday.AddDays(3).AddHours(12.5), 0.5);
            Add(work.Key, "Angebot fertigstellen", workshopDay.AddHours(14.5), 1);
            Add(work.Key, "Zahnarzt", monday.AddDays(4).AddHours(10), 1, "Praxis Dr. Lang");
            var privateKey = book.AddOwnCalendar("Privat", "#16A34A").Key;
            Add(privateKey, "Sport", monday.AddDays(1).AddHours(18), 1.5, "Turnhalle");
            Add(privateKey, "Geburtstag Mama", monday.AddDays(5), 0, allDay: true);
        }

        if (people.Count == 0)
        {
            foreach (var (given, family, company, email, phone, favorite) in new[]
                     {
                         ("Jonas", "Weber", "Beispiel GmbH", "jonas.weber@beispiel.de", "+49 221 555 0101", true),
                         ("Lena", "Koch", "Beispiel GmbH", "lena.koch@beispiel.de", "+49 221 555 0102", true),
                         ("Maria", "Schulz", "Kunde AG", "maria.schulz@kunde.example", "+49 30 987654", false),
                         ("Tanja", "Roth", "Kunde AG", "tanja.roth@kunde.example", "+49 30 987655", false),
                         ("Tobias", "Krüger", "Beispiel GmbH", "tobias.krueger@beispiel.de", "+49 221 555 0142", false),
                         ("Yuki", "Tanaka", "Partner KK", "yuki@partner.example", "", false),
                         ("Jürgen", "Müller", "", "juergen@example.at", "+43 1 234567", false),
                     })
            {
                people.Save(null, new ContactData
                {
                    GivenName = given,
                    FamilyName = family,
                    Company = company,
                    Emails = [new ContactValue("work", email)],
                    Phones = phone.Length > 0 ? [new ContactValue("work", phone)] : [],
                    IsFavorite = favorite,
                    Categories = company == "Kunde AG" ? ["Kunde"] : [],
                });
            }
        }
    }

    private static byte[] SampleMailServerRaw(MimeMessage message) => MailMime.ToBytes(message);

    private static void Html(SampleMailServer server, string folder, MailAddress from, string subject, string html, DateTimeOffset date, MessageFlags flags,
        (string Name, string Type, byte[] Content)? attachment = null)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress(from.Name, from.Address));
        message.To.Add(new MailboxAddress("Anna Berger", Address));
        message.Subject = subject;
        message.Date = date;
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId("beispiel.de");
        var builder = new BodyBuilder { HtmlBody = html, TextBody = ThixWrite.Core.Formats.Html.HtmlImport.PlainText(html) };
        if (attachment is { } file)
        {
            builder.Attachments.Add(file.Name, file.Content, ContentType.Parse(file.Type));
        }

        message.Body = builder.ToMessageBody();
        server.Deliver(Address, folder, MailMime.ToBytes(message), flags);
    }

    /// <summary>A newsletter like fixture 03: layout tables, colours, remote pictures (blocked), a hidden preheader.</summary>
    private static MimeMessage Fixture03(DateTimeOffset date)
    {
        var message = new MimeMessage();
        message.From.Add(new MailboxAddress("Beispiel Shop", "newsletter@shop.example"));
        message.To.Add(new MailboxAddress("", Address));
        message.Subject = "Neu im November: 20 % auf die Herbstkollektion";
        message.Date = date;
        message.MessageId = MimeKit.Utils.MimeUtils.GenerateMessageId("shop.example");
        message.Body = new TextPart("html")
        {
            Text = """
                <div style="display:none">Die Neuheiten im November</div>
                <table width="100%" bgcolor="#EEEEEE"><tr><td align="center">
                <table width="600" bgcolor="#FFFFFF">
                <tr><td bgcolor="#1F3B57" style="padding:20px"><img src="https://shop.example/logo.png" width="160" height="40" alt="Beispiel Shop"></td></tr>
                <tr><td style="padding:24px"><h1 style="color:#1F3B57">Neu im November</h1><p>Entdecken Sie unsere Herbstkollektion – nur für kurze Zeit <b>20 % günstiger</b>.</p>
                <table width="100%"><tr><td width="50%"><img src="https://shop.example/jacke.jpg" width="260" height="180" alt="Jacke"><p><b>Wetterjacke</b><br>89,00 €</p></td>
                <td width="50%"><img src="https://shop.example/schal.jpg" width="260" height="180" alt="Schal"><p><b>Wollschal</b><br>29,00 €</p></td></tr></table>
                <p><a href="https://shop.example/herbst">Jetzt entdecken</a></p></td></tr>
                <tr><td bgcolor="#F4F4F4" style="font-size:11px;color:#777777">Sie erhalten diesen Newsletter, weil Sie ihn bestellt haben. <a href="https://shop.example/abmelden">Abmelden</a></td></tr>
                </table></td></tr></table>
                """,
        };
        return message;
    }

    private static byte[] Pdf() => Encoding.ASCII.GetBytes("%PDF-1.4\n1 0 obj<</Type/Catalog/Pages 2 0 R>>endobj 2 0 obj<</Type/Pages/Kids[]/Count 0>>endobj\ntrailer<</Root 1 0 R>>\n%%EOF\n");

    /// <summary>The sample account in <paramref name="document"/>, synced once (and the bodies of the inbox fetched), on <paramref name="server"/>.</summary>
    public static async Task FillAsync(MailDocument document, SampleMailServer server)
    {
        MailDocument.Transport = server;
        if (document.Accounts.Account(Account.Id) is null)
        {
            document.AddAccount(Account, Password, null);
        }

        var sync = document.SyncOf(Account.Id)!;
        await sync.SyncOnceAsync();
        foreach (var folder in document.Store.Folders(Account.Id))
        {
            foreach (var entry in document.Store.List(new Core.Store.MessageQuery([folder.Id])))
            {
                await sync.FetchBodyAsync(entry.Id, CancellationToken.None);
            }
        }

        var inbox = document.Store.FolderByRole(Account.Id, FolderRole.Inbox)!;
        var tagged = document.Store.Headers([.. document.Store.List(new Core.Store.MessageQuery([inbox.Id])).Select(e => e.Id)]).FirstOrDefault(h => h.Subject.StartsWith("Angebot", StringComparison.Ordinal));
        if (tagged is not null && tagged.Categories.Count == 0)
        {
            document.Session.ToggleCategory([tagged.Id], "Kunde");
            document.Session.ClearUndoForSamples();
        }

        if (document.Accounts.Signatures.Count == 0)
        {
            var text = "Viele Grüße\nAnna Berger\nBeispiel GmbH · Projektleitung";
            var signature = new MailSignature("standard", "Standard", Core.Security.MailHtml.TextAsHtml(text), text);
            document.Accounts.Save(signature);
            document.Accounts.Save(document.Accounts.Account(Account.Id)! with { NewSignatureId = signature.Id, ReplySignatureId = signature.Id });
        }
    }
}
