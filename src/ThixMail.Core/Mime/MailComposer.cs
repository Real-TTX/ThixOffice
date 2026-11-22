using System.Globalization;
using System.Net;
using MimeKit;
using MimeKit.Utils;
using ThixMail.Core.Model;

namespace ThixMail.Core.Mime;

/// <summary>A file attached to a message being written (or a picture embedded in its HTML by <see cref="ContentId"/>).</summary>
public sealed record DraftAttachment(string FileName, string ContentType, byte[] Content, string? ContentId = null)
{
    public long Size => Content.LongLength;
}

/// <summary>What a reply or a forward refers to (the message in the store; its Answered/$Forwarded flag is set after sending).</summary>
public enum DraftOrigin
{
    New,
    Reply,
    ReplyAll,
    Forward,
}

/// <summary>A message being written: sender account, recipients, subject, the HTML and the plain text of the body, attachments and embedded pictures, the
/// headers that keep the conversation together, and where it came from.</summary>
public sealed record Draft
{
    public string AccountId { get; init; } = "";

    public MailAddress From { get; init; } = new("", "");

    public IReadOnlyList<MailAddress> To { get; init; } = [];

    public IReadOnlyList<MailAddress> Cc { get; init; } = [];

    public IReadOnlyList<MailAddress> Bcc { get; init; } = [];

    public string Subject { get; init; } = "";

    public string Html { get; init; } = "";

    public string Text { get; init; } = "";

    public IReadOnlyList<DraftAttachment> Attachments { get; init; } = [];

    /// <summary>Pictures the HTML shows by cid: (each with its <see cref="DraftAttachment.ContentId"/>).</summary>
    public IReadOnlyList<DraftAttachment> InlineImages { get; init; } = [];

    public string InReplyTo { get; init; } = "";

    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>The Message-ID (kept across saves of the same draft, so the server's copy is replaced, not duplicated).</summary>
    public string MessageId { get; init; } = MimeUtils.GenerateMessageId("thixmail.local");

    public DraftOrigin Origin { get; init; }

    /// <summary>The store id of the message replied to / forwarded (its flags change after sending).</summary>
    public long? OriginalMessage { get; init; }

    /// <summary>The store id of the draft's own copy in Entwürfe (replaced on the next save, removed after sending).</summary>
    public long? SavedDraft { get; init; }

    /// <summary>An invitation or an answer to one (iMIP, RFC 6047): sent as a text/calendar part with its method beside the text and the HTML, and as invite.ics.</summary>
    public CalendarPart? Calendar { get; init; }

    public IEnumerable<MailAddress> Recipients => To.Concat(Cc).Concat(Bcc);

    public long AttachmentSize => Attachments.Sum(attachment => attachment.Size);
}

/// <summary>
/// Composing (MimeKit's BodyBuilder underneath): a draft as a MIME message – multipart/alternative of plain text and HTML, the HTML's pictures in a
/// multipart/related, attachments in multipart/mixed, non-ASCII names encoded (RFC 2047 in headers, RFC 2231 in parameters), Message-ID, In-Reply-To
/// and References –, and the replies and forwards Outlook writes: "AW:"/"WG:", the right recipients, the original below an Outlook header block.
/// </summary>
public static class MailComposer
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    /// <summary>The draft as a MIME message (Bcc stays in the envelope only – never in the headers that are sent; drafts keep it, see <paramref name="keepBcc"/>).</summary>
    public static MimeMessage Build(Draft draft, bool keepBcc = false)
    {
        var message = new MimeMessage();
        message.From.Add(MailMime.ToMailbox(draft.From));
        message.To.AddRange(draft.To.Select(MailMime.ToMailbox));
        message.Cc.AddRange(draft.Cc.Select(MailMime.ToMailbox));
        if (keepBcc)
        {
            message.Bcc.AddRange(draft.Bcc.Select(MailMime.ToMailbox));
        }

        message.Subject = draft.Subject;
        message.Date = DateTimeOffset.Now;
        message.MessageId = draft.MessageId.Trim('<', '>');
        if (draft.InReplyTo.Length > 0)
        {
            message.InReplyTo = draft.InReplyTo.Trim('<', '>');
        }

        foreach (var reference in draft.References)
        {
            message.References.Add(reference.Trim('<', '>'));
        }

        message.Headers.Add("X-Mailer", "ThixMail");
        var builder = new BodyBuilder { TextBody = draft.Text.Length > 0 ? draft.Text : ThixWrite.Core.Formats.Html.HtmlImport.PlainText(draft.Html) };
        if (draft.Html.Length > 0)
        {
            builder.HtmlBody = draft.Html;
            foreach (var picture in draft.InlineImages)
            {
                var resource = builder.LinkedResources.Add(picture.FileName, picture.Content, ContentType.Parse(picture.ContentType));
                resource.ContentId = picture.ContentId ?? MimeUtils.GenerateMessageId();
                resource.ContentDisposition = new ContentDisposition(ContentDisposition.Inline) { FileName = picture.FileName };
            }
        }

        foreach (var attachment in draft.Attachments)
        {
            builder.Attachments.Add(attachment.FileName, attachment.Content, ContentType.Parse(attachment.ContentType));
        }

        message.Body = draft.Calendar is { } calendar ? WithCalendar(builder, draft, calendar) : builder.ToMessageBody();
        return message;
    }

    /// <summary>
    /// The body of an invitation as Outlook and Gmail send it: multipart/mixed of a multipart/alternative (text, HTML, text/calendar with its method) and
    /// the same calendar as invite.ics (application/ics) for programs that only look at attachments.
    /// </summary>
    private static MimeEntity WithCalendar(BodyBuilder builder, Draft draft, CalendarPart calendar)
    {
        var alternative = new MultipartAlternative();
        var plain = new TextPart("plain");
        plain.SetText("utf-8", builder.TextBody ?? "");
        alternative.Add(plain);
        if (draft.Html.Length > 0)
        {
            var html = new TextPart("html");
            html.SetText("utf-8", draft.Html);
            alternative.Add(html);
        }

        var invitation = new TextPart("calendar") { ContentTransferEncoding = ContentEncoding.Base64 };
        invitation.SetText("utf-8", calendar.Text);
        invitation.ContentType.Parameters.Add("method", calendar.Method);
        alternative.Add(invitation);

        var file = new MimePart("application", "ics")
        {
            Content = new MimeContent(new MemoryStream(System.Text.Encoding.UTF8.GetBytes(calendar.Text))),
            ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = "invite.ics" },
            ContentTransferEncoding = ContentEncoding.Base64,
        };
        var mixed = new Multipart("mixed") { alternative, file };
        foreach (var attachment in draft.Attachments)
        {
            mixed.Add(new MimePart(ContentType.Parse(attachment.ContentType))
            {
                Content = new MimeContent(new MemoryStream(attachment.Content)),
                ContentDisposition = new ContentDisposition(ContentDisposition.Attachment) { FileName = attachment.FileName },
                ContentTransferEncoding = ContentEncoding.Base64,
            });
        }

        return mixed;
    }

    /// <summary>The draft as bytes (for SMTP, the outbox and APPEND).</summary>
    public static byte[] ToBytes(Draft draft, bool keepBcc = false) => MailMime.ToBytes(Build(draft, keepBcc));

    /// <summary>A saved draft (from Entwürfe or an .eml) back into a draft to edit.</summary>
    public static Draft FromMessage(ReadMail mail, MimeMessage message, string accountId) => new()
    {
        AccountId = accountId,
        From = mail.From,
        To = mail.To,
        Cc = mail.Cc,
        Bcc = MailMime.ToAddresses(message.Bcc),
        Subject = mail.Subject,
        Html = mail.Html,
        Text = mail.Text,
        Attachments = [.. mail.Attachments.Select(a => new DraftAttachment(a.FileName, a.ContentType, a.Content))],
        InlineImages = [.. mail.InlineParts.Select(p => new DraftAttachment(p.Key, p.Value.ContentType, p.Value.Bytes, p.Key))],
        InReplyTo = message.InReplyTo ?? "",
        References = [.. message.References],
        MessageId = mail.MessageId.Length > 0 ? mail.MessageId : MimeUtils.GenerateMessageId("thixmail.local"),
    };

    /// <summary>
    /// "Antworten" / "Allen antworten": to the Reply-To (else the sender); for all also everyone in To and Cc except the own address; "AW: " before
    /// the subject (once); In-Reply-To and References; the original below an Outlook header block (quoted with "&gt; " in the plain text).
    /// </summary>
    public static Draft Reply(ReadMail original, MailAccount account, bool all, long? originalId = null)
    {
        var to = original.ReplyTo.Count > 0 ? original.ReplyTo : [original.From];
        var cc = new List<MailAddress>();
        if (all)
        {
            var own = account.Address;
            var seen = new HashSet<string>(to.Select(a => a.Address), StringComparer.OrdinalIgnoreCase) { own };
            foreach (var address in original.To.Concat(original.Cc))
            {
                if (address.Address.Length > 0 && seen.Add(address.Address))
                {
                    cc.Add(address);
                }
            }
        }

        var references = original.References.ToList();
        if (original.MessageId.Length > 0)
        {
            references.Add(original.MessageId);
        }

        return new Draft
        {
            AccountId = account.Id,
            From = account.Sender,
            To = [.. to.Where(a => !a.Address.Equals(account.Address, StringComparison.OrdinalIgnoreCase) || to.Count == 1)],
            Cc = cc,
            Subject = Prefixed("AW: ", original.Subject),
            Html = QuoteHtml(original),
            Text = QuoteText(original),
            InReplyTo = original.MessageId,
            References = references,
            Origin = all ? DraftOrigin.ReplyAll : DraftOrigin.Reply,
            OriginalMessage = originalId,
        };
    }

    /// <summary>"Weiterleiten": "WG: " before the subject, no recipients, the original's header block and body, its attachments and pictures along.</summary>
    public static Draft Forward(ReadMail original, MailAccount account, long? originalId = null) => new()
    {
        AccountId = account.Id,
        From = account.Sender,
        Subject = Prefixed("WG: ", original.Subject),
        Html = QuoteHtml(original),
        Text = QuoteText(original, quote: false),
        Attachments = [.. original.Attachments.Select(a => new DraftAttachment(a.FileName, a.ContentType, a.Content))],
        InlineImages = [.. original.InlineParts.Select(p => new DraftAttachment(p.Key, p.Value.ContentType, p.Value.Bytes, p.Key))],
        References = original.MessageId.Length > 0 ? [.. original.References, original.MessageId] : original.References,
        Origin = DraftOrigin.Forward,
        OriginalMessage = originalId,
    };

    /// <summary>"AW: Angebot" stays "AW: Angebot"; "Re: Angebot" becomes "AW: Angebot" (one prefix, Outlook's).</summary>
    public static string Prefixed(string prefix, string subject)
    {
        var bare = MailMime.NormalizeSubject(subject);
        return prefix + bare;
    }

    /// <summary>Outlook's header block above the original ("Von: … Gesendet: … An: … Cc: … Betreff: …") with the original's HTML below a line.</summary>
    public static string QuoteHtml(ReadMail original)
    {
        var header = $"<p style=\"margin:0\"><b>Von:</b> {Encode(original.From.Full)}<br><b>Gesendet:</b> {Encode(DateText(original.Date))}<br>" +
                     $"<b>An:</b> {Encode(string.Join("; ", original.To.Select(a => a.Full)))}<br>" +
                     (original.Cc.Count > 0 ? $"<b>Cc:</b> {Encode(string.Join("; ", original.Cc.Select(a => a.Full)))}<br>" : "") +
                     $"<b>Betreff:</b> {Encode(original.Subject)}</p>";
        return $"<div><p><br></p><hr>{header}<p><br></p>{BodyOf(original.Html)}</div>";
    }

    /// <summary>The plain-text form: the header block and every line of the original after "&gt; " (a forward does not quote).</summary>
    public static string QuoteText(ReadMail original, bool quote = true)
    {
        var lines = original.Text.Replace("\r\n", "\n").Split('\n').Select(line => quote ? "> " + line : line);
        return "\n\n-----Ursprüngliche Nachricht-----\n" +
               $"Von: {original.From.Full}\nGesendet: {DateText(original.Date)}\nAn: {string.Join("; ", original.To.Select(a => a.Full))}\n" +
               (original.Cc.Count > 0 ? $"Cc: {string.Join("; ", original.Cc.Select(a => a.Full))}\n" : "") +
               $"Betreff: {original.Subject}\n\n" + string.Join("\n", lines);
    }

    /// <summary>"Montag, 23. November 2026 09:15" (Outlook's German date of a header block).</summary>
    public static string DateText(DateTimeOffset date) => date.ToLocalTime().ToString("dddd, d. MMMM yyyy HH:mm", German);

    /// <summary>The content of the body element of an HTML document (or the HTML itself).</summary>
    private static string BodyOf(string html)
    {
        var start = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
        if (start < 0)
        {
            return html;
        }

        var open = html.IndexOf('>', start);
        var end = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
        return open < 0 ? html : end > open ? html[(open + 1)..end] : html[(open + 1)..];
    }

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
