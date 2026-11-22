using System.Globalization;
using System.Net;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixOffice.Core.Models;
using ThixOffice.Pim.Calendar;

namespace ThixMail.Core;

/// <summary>What an invitation in a message means for the person who reads it: the meeting, its state in the calendar, the overlaps.</summary>
public sealed record InvitationView(
    Invitation Invitation, MailAccount? Account, CalendarAppointment? Entered, IReadOnlyList<CalendarAppointment> Conflicts, bool CanAnswer, bool HasCalendar)
{
    /// <summary>The meeting was called off by its organizer.</summary>
    public bool IsCancelled => Invitation.Method == InvitationMethod.Cancel;

    /// <summary>An answer of a participant to a meeting of the reader.</summary>
    public bool IsReply => Invitation.Method == InvitationMethod.Reply;
}

/// <summary>
/// Invitations by mail in ThixMail's business layer: what a message with a text/calendar part shows (the meeting, conflicts with the calendar of the
/// ThixCloud account), the answer (Zusagen | Mit Vorbehalt | Absagen: the appointment written into an own calendar – or removed – and the iTIP REPLY to the
/// organizer through the outbox), a cancelled meeting taken out of the calendar, and meetings made in the calendar view (REQUEST to the participants,
/// CANCEL when deleted). The calendar's events carry no participants (ThixCalendar's contract): the meetings this computer sent are remembered locally.
/// </summary>
public static class MailInvitations
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The invitation of a message and its state, or null when the message has none (or an unreadable one).</summary>
    public static InvitationView? View(MailDocument document, ReadMail mail, string? accountId)
    {
        if (Invitations.Read(mail.Calendar?.Text) is not { } invitation)
        {
            return null;
        }

        var account = accountId is null ? null : document.Accounts.Account(accountId);
        var book = document.Pim.Calendar;
        var key = CalendarBook.KeyOfInvitation(invitation.Uid);
        var entered = book?.Find(key);
        var conflicts = book is null || invitation.Method != InvitationMethod.Request ? [] : book.Conflicts(invitation.Start, invitation.End, key);
        var canAnswer = invitation.Method == InvitationMethod.Request && account is not null && invitation.Organizer.Email.Length > 0
                        && !invitation.Organizer.Email.Equals(account.Address, StringComparison.OrdinalIgnoreCase);
        return new InvitationView(invitation, account, entered, conflicts, canAnswer, book is not null);
    }

    /// <summary>
    /// Answers an invitation: Zusagen / Mit Vorbehalt write the appointment into the first own calendar (made when there is none), Absagen removes it; the
    /// REPLY goes to the organizer (through the outbox, also offline). Without ThixCloud only the answer is sent.
    /// </summary>
    public static CalendarAppointment? Answer(MailDocument document, InvitationView view, InvitationAnswer answer)
    {
        if (!view.CanAnswer || view.Account is not { } account)
        {
            throw new BusinessException("Auf diese Einladung kann von diesem Konto nicht geantwortet werden.");
        }

        var invitation = view.Invitation;
        CalendarAppointment? entered = null;
        if (document.Pim.Calendar is { } book)
        {
            var key = CalendarBook.KeyOfInvitation(invitation.Uid);
            if (answer == InvitationAnswer.Decline)
            {
                if (book.Find(key) is { IsReadOnly: false })
                {
                    book.Delete(key);
                }
            }
            else
            {
                var calendar = book.Find(key)?.CalendarKey is { } existing && book.Calendar(existing) is { IsOwn: true } ? existing : book.DefaultCalendar().Key;
                entered = book.Save(Invitations.AppointmentOf(invitation, answer, calendar));
            }
        }

        var me = account.Sender;
        var words = answer switch
        {
            InvitationAnswer.Accept => "hat die Einladung angenommen",
            InvitationAnswer.Tentative => "hat die Einladung mit Vorbehalt angenommen",
            _ => "hat die Einladung abgelehnt",
        };
        var text = $"{me.Display} {words}.\n\n{invitation.Summary}\n{When(invitation)}" + (invitation.Location.Length > 0 ? $"\n{invitation.Location}" : "");
        document.Session.Send(new Draft
        {
            AccountId = account.Id,
            From = me,
            To = [new MailAddress(invitation.Organizer.Name, invitation.Organizer.Email)],
            Subject = Invitations.ReplySubject(invitation, answer),
            Text = text,
            Html = MailHtmlText(text),
            Calendar = new CalendarPart("REPLY", Invitations.Reply(invitation, me.Address, me.Name, answer)),
        });
        return entered;
    }

    /// <summary>A cancelled meeting: its appointment leaves the calendar ("Aus Kalender entfernen"). False when it was not there.</summary>
    public static bool RemoveCancelled(MailDocument document, InvitationView view)
        => document.Pim.Calendar is { } book && book.Find(CalendarBook.KeyOfInvitation(view.Invitation.Uid)) is { IsReadOnly: false } && book.Delete(CalendarBook.KeyOfInvitation(view.Invitation.Uid));

    /// <summary>
    /// A meeting made in the calendar view: the appointment is saved, the invitation (REQUEST) goes to <paramref name="attendees"/> from
    /// <paramref name="account"/>, and the meeting is remembered here (an update or a cancellation reaches the same people). An update raises the sequence.
    /// </summary>
    public static CalendarAppointment SendMeeting(MailDocument document, CalendarAppointment appointment, IReadOnlyList<MailAddress> attendees, MailAccount account)
    {
        var book = document.Pim.Calendar ?? throw new BusinessException("Der Kalender braucht eine Anmeldung bei der ThixCloud (Datei › Einstellungen › Cloud).");
        if (attendees.Count == 0)
        {
            throw new BusinessException("Bitte geben Sie mindestens einen Teilnehmer an.", "Attendees");
        }

        foreach (var attendee in attendees)
        {
            if (!MimeKit.MailboxAddress.TryParse(attendee.Address, out _) || !attendee.Address.Contains('@', StringComparison.Ordinal))
            {
                throw new BusinessException($"„{attendee.Address}“ ist keine gültige E-Mail-Adresse.", "Attendees");
            }
        }

        var saved = book.Save(appointment);
        var previous = book.MeetingOf(saved.Key);
        var meeting = new Invitation
        {
            Method = InvitationMethod.Request,
            Uid = previous?.Uid ?? $"{saved.Key}@thixmail",
            Sequence = previous is null ? 0 : previous.Sequence + 1,
            Summary = saved.Title,
            Start = saved.Start,
            End = saved.End,
            IsAllDay = saved.IsAllDay,
            Location = saved.Location,
            Description = saved.Description,
            Organizer = new InvitationAttendee(account.Address, account.Sender.Name),
            Attendees = [.. attendees.Select(a => new InvitationAttendee(a.Address, a.Name))],
        };
        var text = $"{saved.Title}\n{When(meeting)}" + (saved.Location.Length > 0 ? $"\n{saved.Location}" : "") + (saved.Description.Length > 0 ? $"\n\n{saved.Description}" : "");
        document.Session.Send(new Draft
        {
            AccountId = account.Id,
            From = account.Sender,
            To = attendees,
            Subject = (previous is null ? "Einladung: " : "Aktualisiert: ") + saved.Title,
            Text = text,
            Html = MailHtmlText(text),
            Calendar = new CalendarPart("REQUEST", Invitations.Request(meeting)),
        });
        book.RememberMeeting(saved.Key, meeting);
        return saved;
    }

    /// <summary>Deletes an appointment; a meeting this computer sent is called off first (CANCEL to its participants, from <paramref name="account"/>).</summary>
    public static void DeleteAppointment(MailDocument document, string key, MailAccount? account)
    {
        var book = document.Pim.Calendar ?? throw new BusinessException("Der Kalender braucht eine Anmeldung bei der ThixCloud.");
        if (book.MeetingOf(key) is { Attendees.Count: > 0 } meeting && account is not null)
        {
            var text = $"Abgesagt: {meeting.Summary}\n{When(meeting)}";
            document.Session.Send(new Draft
            {
                AccountId = account.Id,
                From = account.Sender,
                To = [.. meeting.Attendees.Select(a => new MailAddress(a.Name, a.Email))],
                Subject = "Abgesagt: " + meeting.Summary,
                Text = text,
                Html = MailHtmlText(text),
                Calendar = new CalendarPart("CANCEL", Invitations.Cancel(meeting)),
            });
            book.ForgetMeeting(key);
        }

        book.Delete(key);
    }

    /// <summary>"Do., 26.11.2026, 14:00–15:00" (all-day: the dates) in local time.</summary>
    public static string When(Invitation invitation)
    {
        if (invitation.IsAllDay)
        {
            var first = invitation.Start.UtcDateTime.Date;
            var last = invitation.End.UtcDateTime.Date.AddDays(-1);
            return last <= first ? first.ToString("ddd., dd.MM.yyyy", German) + " (ganztägig)" : $"{first.ToString("dd.MM.", German)}–{last.ToString("dd.MM.yyyy", German)} (ganztägig)";
        }

        var start = invitation.Start.ToLocalTime();
        var end = invitation.End.ToLocalTime();
        return start.Date == end.Date
            ? $"{start.ToString("ddd., dd.MM.yyyy, HH:mm", German)}–{end.ToString("HH:mm", German)}"
            : $"{start.ToString("ddd., dd.MM.yyyy, HH:mm", German)} – {end.ToString("ddd., dd.MM.yyyy, HH:mm", German)}";
    }

    private static string MailHtmlText(string text) => "<html><body>" + string.Concat(text.Split('\n').Select(line => "<div>" + (line.Length == 0 ? "<br>" : WebUtility.HtmlEncode(line)) + "</div>")) + "</body></html>";
}
