using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixWrite.Core.Formats.Html;
using ThixWrite.Core.Model;
using static ThixMail.Core.Tests.TestData;

namespace ThixMail.Core.Tests;

/// <summary>Reading the 12 fixture mails, composing, replying and forwarding.</summary>
public sealed class MimeTests
{
    [Theory]
    [InlineData("01-plain.eml", "Termin für die Besprechung", "jonas.weber@example.de", false, 0, 0)]
    [InlineData("02-simple-html.eml", "Einladung zum Sommerfest", "lena.koch@example.org", true, 0, 0)]
    [InlineData("03-newsletter.eml", "Neu im November: 20 % auf die Herbstkollektion", "newsletter@news.example.com", true, 0, 0)]
    [InlineData("04-reply-chain.eml", "AW: Termin für die Besprechung", "anna@example.de", true, 0, 0)]
    [InlineData("05-inline-image.eml", "Unser neues Logo", "marketing@example.net", true, 0, 1)]
    [InlineData("06-attachments.eml", "Rechnung November und Unterlagen", "buchhaltung@example.com", false, 3, 0)]
    [InlineData("07-calendar-invite.eml", "Einladung: Projektbesprechung Nordlicht (Do. 26.11.2026 14:00)", "jonas.weber@example.de", true, 1, 0)]
    [InlineData("08-iso-8859-1.eml", "Grüße aus Österreich", "juergen@example.at", false, 0, 0)]
    [InlineData("09-windows-1252-html.eml", "Ihre Bestellung „Premium“", "service@example.shop", true, 0, 0)]
    [InlineData("10-legacy-charsets.eml", "会議の資料", "yuki@example.jp", false, 0, 0)]
    [InlineData("12-outlook-html.eml", "Präsentation für Donnerstag", "maria.schulz@example.com", true, 1, 1)]
    public void Every_fixture_reads_with_its_header_body_and_parts(string file, string subject, string from, bool isHtml, int attachments, int inline)
    {
        var mail = MailReader.Read(Fixture(file));
        Assert.Equal(subject, mail.Subject);
        Assert.Equal(from, mail.From.Address);
        Assert.Equal(isHtml, mail.IsHtml);
        Assert.Equal(attachments, mail.Attachments.Count);
        Assert.Equal(inline, mail.InlineParts.Count);
        Assert.False(string.IsNullOrWhiteSpace(mail.Text));
    }

    [Fact]
    public void Legacy_charsets_show_their_letters()
    {
        Assert.Contains("schöne Grüße aus Österreich", MailReader.Read(Fixture("08-iso-8859-1.eml")).Text, StringComparison.Ordinal);
        Assert.Equal("Jürgen Müller", MailReader.Read(Fixture("08-iso-8859-1.eml")).From.Name);
        var cp1252 = MailReader.Read(Fixture("09-windows-1252-html.eml"));
        Assert.Contains("1.299,00 €", cp1252.Text, StringComparison.Ordinal);
        Assert.Contains("„Premium“ –", cp1252.Text, StringComparison.Ordinal);
        var legacy = MailReader.Read(Fixture("10-legacy-charsets.eml"));
        Assert.Contains("会議の資料を送ります", legacy.Text, StringComparison.Ordinal);
        var message = MailMime.Parse(Fixture("10-legacy-charsets.eml"));
        Assert.Contains(message.BodyParts.OfType<MimeKit.TextPart>(), part => part.Text.Contains("Привет, Анна", StringComparison.Ordinal));
    }

    [Fact]
    public void Attachments_names_and_the_calendar_invitation_are_found()
    {
        var attachments = MailReader.Read(Fixture("06-attachments.eml")).Attachments;
        Assert.Equal(["Rechnung-2026-11.pdf", "Projektdaten.bin", "Einlesen.exe"], attachments.Select(a => a.FileName));
        Assert.Equal(1536 * 1024, attachments[1].Size);
        Assert.Equal("Präsentation Übersicht.pdf", MailReader.Read(Fixture("12-outlook-html.eml")).Attachments.Single().FileName); // RFC 2231
        var invitation = MailReader.Read(Fixture("07-calendar-invite.eml"));
        Assert.NotNull(invitation.Calendar);
        Assert.Equal("REQUEST", invitation.Calendar!.Method);
        Assert.Contains("SUMMARY:Projektbesprechung Nordlicht", invitation.Calendar.Text, StringComparison.Ordinal);
    }

    [Fact]
    public void A_broken_message_still_shows_what_can_be_read()
    {
        var mail = MailReader.Read(Fixture("11-broken-mime.eml"));
        Assert.Equal("Defekte Nachricht", mail.Subject);
        Assert.Contains("Umlaute", mail.Text + mail.Html, StringComparison.Ordinal);
        var summary = MailMime.Summarize(1, Fixture("11-broken-mime.eml"), MessageFlags.None, [], 0);
        Assert.Equal("broken@example.invalid", summary.From.Address);
    }

    [Fact]
    public void A_composed_message_round_trips_with_HTML_text_pictures_attachments_and_non_ASCII_names()
    {
        var picture = Fixture("05-inline-image.eml").Length > 0 ? MailReader.Read(Fixture("05-inline-image.eml")).InlineParts.Values.Single().Bytes : [];
        var draft = new Draft
        {
            AccountId = "anna",
            From = new MailAddress("Anna Berger", Address),
            To = [Jonas],
            Cc = [new MailAddress("Lena Köhler", "lena@example.org")],
            Bcc = [new MailAddress("", "chef@example.de")],
            Subject = "Übersicht für Donnerstag – bitte prüfen",
            Html = "<p>Hallo <b>Jonas</b>,</p><p><img src=\"cid:bild1\"></p>",
            InlineImages = [new DraftAttachment("logo.png", "image/png", picture, "bild1")],
            Attachments = [new DraftAttachment("Präsentation Übersicht.pdf", "application/pdf", [1, 2, 3, 4])],
            InReplyTo = "<vorher@example.de>",
            References = ["<anfang@example.de>", "<vorher@example.de>"],
        };

        var raw = MailComposer.ToBytes(draft);
        var text = System.Text.Encoding.ASCII.GetString(raw);
        Assert.DoesNotContain("chef@example.de", text, StringComparison.Ordinal); // Bcc never in the sent headers
        Assert.Contains("multipart/alternative", text, StringComparison.Ordinal);
        Assert.Contains("multipart/related", text, StringComparison.Ordinal);
        Assert.Contains("X-Mailer: ThixMail", text, StringComparison.Ordinal);

        var read = MailReader.Read(raw);
        Assert.Equal(draft.Subject, read.Subject);
        Assert.Equal("Lena Köhler", read.Cc.Single().Name);
        Assert.Equal("Präsentation Übersicht.pdf", read.Attachments.Single().FileName);
        Assert.Equal([1, 2, 3, 4], read.Attachments.Single().Content);
        Assert.Equal(picture, read.InlineParts["bild1"].Bytes);
        Assert.Contains("Hallo Jonas", read.Text, StringComparison.Ordinal);
        Assert.Equal(["anfang@example.de", "vorher@example.de"], read.References);
        Assert.Contains("chef@example.de", System.Text.Encoding.ASCII.GetString(MailComposer.ToBytes(draft, keepBcc: true)), StringComparison.Ordinal);
    }

    [Fact]
    public void Reply_reply_all_and_forward_address_quote_and_prefix_like_Outlook()
    {
        var original = MailReader.Read(Fixture("04-reply-chain.eml")) with
        {
            Cc = [new MailAddress("Lena", "lena@example.org"), new MailAddress("Ich", "anna@example.de")],
        };
        var account = Account with { Address = "jonas.weber@example.de", SenderName = "Jonas Weber" };

        var reply = MailComposer.Reply(original, account, all: false);
        Assert.Equal("AW: Termin für die Besprechung", reply.Subject); // one prefix, never "AW: AW:"
        Assert.Equal(["anna@example.de"], reply.To.Select(a => a.Address));
        Assert.Empty(reply.Cc);
        Assert.Equal("reply-04@example.de", reply.InReplyTo);
        Assert.Equal(["start-00@example.de", "plain-01@example.de", "reply-04@example.de"], reply.References);
        Assert.Contains("<b>Von:</b> Anna Berger &lt;anna@example.de&gt;", reply.Html, StringComparison.Ordinal);
        Assert.Contains("> Prima, dann machen wir es so.", reply.Text, StringComparison.Ordinal);

        var all = MailComposer.Reply(original, account, all: true);
        Assert.Equal(["lena@example.org"], all.Cc.Select(a => a.Address)); // not the own address, not twice

        var forward = MailComposer.Forward(MailReader.Read(Fixture("06-attachments.eml")), account);
        Assert.Equal("WG: Rechnung November und Unterlagen", forward.Subject);
        Assert.Empty(forward.To);
        Assert.Equal(3, forward.Attachments.Count);
        Assert.Equal("AW: Angebot", MailComposer.Prefixed("AW: ", "Re: RE: Angebot"));
    }

    [Fact]
    public void Plain_text_becomes_HTML_with_links_and_nested_quotes()
    {
        var html = MailReader.TextToHtml("Siehe https://example.de/a?b=1.\n> zitiert\n>> doppelt\nwieder <b>normal</b>");
        Assert.Contains("<a href=\"https://example.de/a?b=1\">", html, StringComparison.Ordinal);
        Assert.Contains("<blockquote>zitiert<br><blockquote>doppelt", html, StringComparison.Ordinal);
        Assert.Contains("&lt;b&gt;normal&lt;/b&gt;", html, StringComparison.Ordinal);
    }

    [Fact]
    public void A_document_exported_as_HTML_reads_back_with_its_formatting()
    {
        var bold = CharacterProperties.Empty with { Bold = true };
        var red = CharacterProperties.Empty with { Color = TextColor.FromHex("C00000") };
        var document = new Document(
        [
            new Paragraph([new TextRun("Hallo ", CharacterProperties.Empty), new TextRun("fett", bold), new TextRun(" und rot", red)]),
            new Paragraph([new TextRun("Link", CharacterProperties.Empty, new Hyperlink("https://example.de"))]),
            new Paragraph([new TextRun("böse", CharacterProperties.Empty, new Hyperlink("javascript:alert(1)"))]),
            new Paragraph("<script>"),
        ]);

        var html = HtmlExport.Export(document, image => "cid:" + image.Key);
        Assert.Contains("<b>fett</b>", html, StringComparison.Ordinal);
        Assert.Contains("color:#C00000", html, StringComparison.Ordinal);
        Assert.Contains("<a href=\"https://example.de\">Link</a>", html, StringComparison.Ordinal);
        Assert.DoesNotContain("javascript", html, StringComparison.Ordinal);
        Assert.Contains("&lt;script&gt;", html, StringComparison.Ordinal);

        var fragment = HtmlImport.Import(html)!;
        var runs = fragment.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<TextRun>().ToList();
        Assert.Contains(runs, r => r.Text == "fett" && r.Properties.Bold == true);
        Assert.Contains(runs, r => r.Text == "Link" && r.Link?.Uri == "https://example.de");
    }
}
