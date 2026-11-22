using System.Text;
using MimeKit;
using ThixMail.Core.Model;
using ThixMail.Core.Transport;
using ThixWrite.Core.Formats.Html;

namespace ThixMail.Core.Mime;

/// <summary>
/// The MIME helpers every part of ThixMail uses (MimeKit): parse bytes tolerantly (a broken message still gives what can be read), write a message as
/// bytes, the header summary of a message as the store keeps it (addresses, references, attachments, a preview), and the conversion of MimeKit's
/// addresses. The legacy code pages (ISO-8859-x, Windows-125x, KOI8-R …) are registered once so old mails show their umlauts.
/// </summary>
public static class MailMime
{
    /// <summary>
    /// Registers the legacy code pages as soon as ThixMail.Core loads – before MimeKit builds its table of charsets (it does that once, from what
    /// .NET knows at that moment; registered later, ISO-2022-JP and friends would show as escape codes).
    /// </summary>
    [System.Runtime.CompilerServices.ModuleInitializer]
    [System.Diagnostics.CodeAnalysis.SuppressMessage("Usage", "CA2255", Justification = "Must run before MimeKit's first use anywhere in the process.")]
    internal static void RegisterCodePages() => Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);

    /// <summary>Parses a message; a message MimeKit cannot read at all becomes one with the bytes as text (nothing is lost, nothing throws).</summary>
    public static MimeMessage Parse(byte[] raw)
    {
        try
        {
            using var stream = new MemoryStream(raw, writable: false);
            return MimeMessage.Load(new ParserOptions { AllowAddressesWithoutDomain = true, AllowUnquotedCommasInAddresses = true }, stream);
        }
        catch (FormatException)
        {
            var fallback = new MimeMessage { Subject = "(Nachricht nicht lesbar)" };
            fallback.Body = new TextPart("plain") { Text = Encoding.UTF8.GetString(raw) };
            return fallback;
        }
    }

    /// <summary>The message with its Date header set to <paramref name="date"/> ("Später senden": the moment it goes out, not when it was written).</summary>
    public static byte[] WithDate(byte[] raw, DateTimeOffset date)
    {
        var message = Parse(raw);
        message.Date = date;
        return ToBytes(message);
    }

    /// <summary>The message as RFC 5322 bytes (CRLF).</summary>
    public static byte[] ToBytes(MimeMessage message)
    {
        using var stream = new MemoryStream();
        message.WriteTo(FormatOptions.Default, stream);
        return stream.ToArray();
    }

    public static MailAddress ToAddress(MailboxAddress mailbox) => new(mailbox.Name ?? "", mailbox.Address ?? "");

    public static IReadOnlyList<MailAddress> ToAddresses(InternetAddressList list) => [.. list.Mailboxes.Select(ToAddress)];

    public static MailboxAddress ToMailbox(MailAddress address) => new(address.Name, address.Address);

    /// <summary>The header summary of raw bytes (the sample server and the .eml import use it; IMAP gets the same from FETCH).</summary>
    public static RemoteMessage Summarize(uint uid, byte[] raw, MessageFlags flags, IReadOnlyList<string> keywords, ulong modSeq)
    {
        var message = Parse(raw);
        return new RemoteMessage
        {
            Uid = uid,
            MessageId = message.MessageId ?? "",
            InReplyTo = message.InReplyTo ?? "",
            References = [.. message.References],
            Subject = message.Subject ?? "",
            From = message.From.Mailboxes.Select(ToAddress).FirstOrDefault() ?? new MailAddress("", ""),
            To = ToAddresses(message.To),
            Cc = ToAddresses(message.Cc),
            ReplyTo = ToAddresses(message.ReplyTo),
            Date = message.Date == DateTimeOffset.MinValue ? DateTimeOffset.Now : message.Date,
            Size = raw.Length,
            Flags = flags,
            Keywords = keywords,
            HasAttachments = message.Attachments.Any(),
            Preview = Preview(message),
            ModSeq = modSeq,
        };
    }

    /// <summary>The first words of the text of a message (the list's preview line): the plain part, or the HTML part's text.</summary>
    public static string Preview(MimeMessage message)
    {
        var text = message.TextBody;
        if (string.IsNullOrWhiteSpace(text) && message.HtmlBody is { } html)
        {
            text = HtmlImport.PlainText(html);
        }

        return Shorten(text ?? "", 200);
    }

    /// <summary>Whitespace collapsed, quoted lines ("> …") left out, at most <paramref name="length"/> characters.</summary>
    public static string Shorten(string text, int length)
    {
        var lines = text.Replace("\r", "").Split('\n').Where(line => !line.TrimStart().StartsWith('>'));
        var flat = string.Join(' ', string.Join(' ', lines).Split([' ', '\t', ' '], StringSplitOptions.RemoveEmptyEntries));
        return flat.Length <= length ? flat : flat[..length].TrimEnd() + "…";
    }

    /// <summary>"AW: AW: Re: Angebot" → "Angebot": the subject of a conversation (the fallback of threading).</summary>
    public static string NormalizeSubject(string subject)
    {
        var text = subject.Trim();
        while (true)
        {
            var colon = text.IndexOf(':');
            if (colon is <= 0 or > 6)
            {
                return text;
            }

            var prefix = text[..colon].Trim().TrimEnd(']').Split('[')[0].ToLowerInvariant();
            if (prefix is not ("re" or "aw" or "wg" or "fw" or "fwd" or "antw" or "sv" or "vs" or "tr" or "rif"))
            {
                return text;
            }

            text = text[(colon + 1)..].Trim();
        }
    }

    /// <summary>True when the subject starts like a reply or a forward ("AW:", "Re:", "WG:", "Fwd:").</summary>
    public static bool IsReplySubject(string subject) => !string.Equals(NormalizeSubject(subject), subject.Trim(), StringComparison.Ordinal);
}
