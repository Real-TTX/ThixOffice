using System.Net;
using System.Text;
using System.Text.RegularExpressions;
using MimeKit;
using ThixMail.Core.Model;

namespace ThixMail.Core.Mime;

/// <summary>An attachment of a message as the reading pane lists it (the bytes come along; the store bounds the size of what it fetches).</summary>
public sealed record MailAttachment(int Index, string FileName, string ContentType, byte[] Content)
{
    public long Size => Content.LongLength;
}

/// <summary>A calendar invitation (text/calendar) found in a message: kept for ThixMail's calendar (7c); now only shown as a note.</summary>
public sealed record CalendarPart(string Method, string Text);

/// <summary>
/// A message read for showing: the header, the HTML to show (a plain-text message is turned into HTML: links, quotes), the plain text (replies,
/// search), the pictures it embeds by Content-ID, its attachments, a calendar invitation, and notes about what could not be read.
/// </summary>
public sealed record ReadMail
{
    public string Subject { get; init; } = "";

    public MailAddress From { get; init; } = new("", "");

    public IReadOnlyList<MailAddress> To { get; init; } = [];

    public IReadOnlyList<MailAddress> Cc { get; init; } = [];

    public IReadOnlyList<MailAddress> ReplyTo { get; init; } = [];

    public DateTimeOffset Date { get; init; }

    public string MessageId { get; init; } = "";

    public IReadOnlyList<string> References { get; init; } = [];

    /// <summary>The HTML the reading pane shows (raw – <see cref="Security.MailHtml"/> makes it safe).</summary>
    public string Html { get; init; } = "";

    /// <summary>True when the message itself had an HTML part (false: <see cref="Html"/> was made from its text).</summary>
    public bool IsHtml { get; init; }

    public string Text { get; init; } = "";

    /// <summary>Pictures by Content-ID (without &lt;&gt;), for cid: references of the HTML.</summary>
    public IReadOnlyDictionary<string, (string ContentType, byte[] Bytes)> InlineParts { get; init; } = new Dictionary<string, (string, byte[])>();

    public IReadOnlyList<MailAttachment> Attachments { get; init; } = [];

    public CalendarPart? Calendar { get; init; }

    public IReadOnlyList<string> Problems { get; init; } = [];
}

/// <summary>Reads a message for the reading pane, the reply and the print (MimeKit underneath; tolerant of broken MIME).</summary>
public static partial class MailReader
{
    public static ReadMail Read(byte[] raw) => Read(MailMime.Parse(raw));

    public static ReadMail Read(MimeMessage message)
    {
        var problems = new List<string>();
        string? html = null;
        string? text = null;
        try
        {
            html = message.HtmlBody;
            text = message.TextBody;
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException or NotSupportedException)
        {
            problems.Add("Ein Teil der Nachricht konnte nicht gelesen werden.");
        }

        var inline = new Dictionary<string, (string, byte[])>(StringComparer.OrdinalIgnoreCase);
        var attachments = new List<MailAttachment>();
        CalendarPart? calendar = null;
        var index = 0;
        foreach (var entity in message.BodyParts)
        {
            switch (entity)
            {
                case TextPart textPart when textPart.ContentType.IsMimeType("text", "calendar"):
                    calendar ??= new CalendarPart(textPart.ContentType.Parameters["method"] ?? "", SafeText(textPart));
                    if (textPart.IsAttachment)
                    {
                        attachments.Add(new MailAttachment(index, FileNameOf(textPart, index, ".ics"), textPart.ContentType.MimeType, Decode(textPart)));
                    }

                    break;
                case TextPart textPart when !textPart.IsAttachment:
                    break; // a body part (HtmlBody / TextBody chose among them)
                case MimePart part when part.ContentId is { Length: > 0 } contentId && !part.IsAttachment && part.ContentType.MediaType.Equals("image", StringComparison.OrdinalIgnoreCase):
                    inline[contentId.Trim('<', '>')] = (part.ContentType.MimeType, Decode(part));
                    break;
                case MimePart part:
                    attachments.Add(new MailAttachment(index, FileNameOf(part, index, ""), part.ContentType.MimeType, Decode(part)));
                    break;
                case MessagePart nested:
                    using (var stream = new MemoryStream())
                    {
                        nested.Message?.WriteTo(stream);
                        var name = (nested.Message?.Subject is { Length: > 0 } subject ? Sanitize(subject) : "Nachricht") + ".eml";
                        attachments.Add(new MailAttachment(index, name, "message/rfc822", stream.ToArray()));
                    }

                    break;
            }

            index++;
        }

        // inline pictures the HTML never names are attachments to the reader
        if (html is not null)
        {
            foreach (var unused in inline.Keys.Where(cid => !html.Contains(cid, StringComparison.OrdinalIgnoreCase)).ToList())
            {
                var (type, bytes) = inline[unused];
                attachments.Add(new MailAttachment(index++, $"Bild {attachments.Count + 1}{ExtensionOf(type)}", type, bytes));
                inline.Remove(unused);
            }
        }

        var isHtml = !string.IsNullOrWhiteSpace(html);
        return new ReadMail
        {
            Subject = message.Subject ?? "",
            From = message.From.Mailboxes.Select(MailMime.ToAddress).FirstOrDefault() ?? new MailAddress("", ""),
            To = MailMime.ToAddresses(message.To),
            Cc = MailMime.ToAddresses(message.Cc),
            ReplyTo = MailMime.ToAddresses(message.ReplyTo),
            Date = message.Date,
            MessageId = message.MessageId ?? "",
            References = [.. message.References],
            Html = isHtml ? html! : TextToHtml(text ?? ""),
            IsHtml = isHtml,
            Text = !string.IsNullOrWhiteSpace(text) ? text! : isHtml ? ThixWrite.Core.Formats.Html.HtmlImport.PlainText(html!) : "",
            InlineParts = inline,
            Attachments = attachments,
            Calendar = calendar,
            Problems = problems,
        };
    }

    /// <summary>
    /// A plain-text message as HTML: paragraphs by blank lines, line breaks kept, web addresses as links, quoted lines ("&gt; …") as quotes (nested
    /// by the number of &gt;), a signature ("-- ") muted by nothing but kept as text.
    /// </summary>
    public static string TextToHtml(string text)
    {
        var html = new StringBuilder("<div>");
        var depth = 0;
        foreach (var rawLine in text.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n'))
        {
            var line = rawLine;
            var level = 0;
            while (line.StartsWith('>'))
            {
                level++;
                line = line[1..].TrimStart(' ');
            }

            while (depth < level)
            {
                html.Append("<blockquote>");
                depth++;
            }

            while (depth > level)
            {
                html.Append("</blockquote>");
                depth--;
            }

            html.Append(Linkify(WebUtility.HtmlEncode(line))).Append("<br>");
        }

        while (depth-- > 0)
        {
            html.Append("</blockquote>");
        }

        return html.Append("</div>").ToString();
    }

    private static string Linkify(string encoded) =>
        UrlPattern().Replace(encoded, match => $"<a href=\"{match.Value}\">{match.Value}</a>");

    [GeneratedRegex(@"\bhttps?://[^\s<>""']+[^\s<>""'.,;:!?)\]]", RegexOptions.IgnoreCase)]
    private static partial Regex UrlPattern();

    private static byte[] Decode(MimePart part)
    {
        using var stream = new MemoryStream();
        try
        {
            part.Content?.DecodeTo(stream);
        }
        catch (FormatException)
        {
        }

        return stream.ToArray();
    }

    private static string SafeText(TextPart part)
    {
        try
        {
            return part.Text ?? "";
        }
        catch (Exception exception) when (exception is FormatException or ArgumentException)
        {
            return "";
        }
    }

    private static string FileNameOf(MimePart part, int index, string fallbackExtension)
    {
        var name = part.FileName;
        if (string.IsNullOrWhiteSpace(name))
        {
            name = $"Anlage {index + 1}{(fallbackExtension.Length > 0 ? fallbackExtension : ExtensionOf(part.ContentType.MimeType))}";
        }

        return Sanitize(name);
    }

    /// <summary>A file name without path parts or characters a file system refuses (an attachment never names a folder).</summary>
    public static string Sanitize(string name)
    {
        var leaf = name.Replace('\\', '/').Split('/').Last().Trim();
        var invalid = Path.GetInvalidFileNameChars().Concat([':', '*', '?', '"', '<', '>', '|']).ToHashSet();
        var clean = new string([.. leaf.Select(character => invalid.Contains(character) || char.IsControl(character) ? '_' : character)]).Trim('.', ' ');
        return clean.Length == 0 ? "Anlage" : clean.Length > 120 ? clean[..120] : clean;
    }

    private static string ExtensionOf(string contentType) => contentType.ToLowerInvariant() switch
    {
        "image/png" => ".png",
        "image/jpeg" or "image/jpg" => ".jpg",
        "image/gif" => ".gif",
        "application/pdf" => ".pdf",
        "text/plain" => ".txt",
        "text/calendar" => ".ics",
        _ => ".bin",
    };
}
