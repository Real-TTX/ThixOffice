using System.Globalization;
using System.Net;
using System.Text;
using ThixMail.Core.Mime;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Editing;
using ThixWrite.Core.Formats.Html;
using ThixWrite.Core.Model;

namespace ThixMail.Core.Security;

/// <summary>What showing a mail found: the remote pictures it left out (until "Bilder laden") and its links with their real targets.</summary>
public sealed record MailView(Document Document, IReadOnlyList<string> BlockedImages, IReadOnlyList<string> Links)
{
    public bool HasBlockedImages => BlockedImages.Count > 0;
}

/// <summary>
/// The safety of a shown mail, in the business layer: its HTML becomes a ThixWrite document through <see cref="HtmlImport"/> in the mail mode – so
/// nothing of it can run: scripts, styles, frames, objects, forms, SVG and event handlers are dropped by the import, links are only http, https and
/// mailto, and a picture comes only from the message itself (cid:) or – after "Bilder laden" for this message or "immer für diesen Absender" – from
/// the web (fetched by <see cref="RemotePictures"/> with limits; until then every remote request is blocked, so a tracking pixel learns nothing).
/// </summary>
public static class MailHtml
{
    /// <summary>
    /// The document the reading pane shows for <paramref name="mail"/>, <paramref name="width"/> points wide. <paramref name="remote"/>: the remote
    /// pictures already fetched (empty while blocked).
    /// </summary>
    public static MailView ToView(ReadMail mail, double width, IReadOnlyDictionary<string, byte[]>? remote, DocumentFonts fonts)
    {
        var blocked = new List<string>();
        ImageData? Resolve(string source)
        {
            if (source.StartsWith("cid:", StringComparison.OrdinalIgnoreCase))
            {
                var id = WebUtility.UrlDecode(source[4..]).Trim('<', '>');
                return mail.InlineParts.TryGetValue(id, out var part) ? ImageData.FromBytes(part.Bytes) : null;
            }

            if (!IsRemote(source))
            {
                return null; // file:, javascript:, relative paths … never
            }

            if (remote is not null && remote.TryGetValue(source, out var bytes))
            {
                return ImageData.FromBytes(bytes);
            }

            if (!blocked.Contains(source, StringComparer.Ordinal))
            {
                blocked.Add(source);
            }

            return null;
        }

        var fragment = HtmlImport.Import(mail.Html, HtmlImportOptions.Mail(Resolve, width));
        var document = Build(fragment, width, fonts);
        var links = document.Blocks.OfType<Paragraph>().SelectMany(p => p.Inlines).Select(i => i.Link?.Uri).Where(u => u is not null).Select(u => u!)
            .Distinct(StringComparer.Ordinal).ToList();
        return new MailView(document, blocked, links);
    }

    /// <summary>The remote pictures of a mail (http/https src of img), for "Bilder laden".</summary>
    public static IReadOnlyList<string> RemoteImages(ReadMail mail, DocumentFonts fonts) => ToView(mail, 400, null, fonts).BlockedImages;

    /// <summary>True for an address that would be a request to the web.</summary>
    public static bool IsRemote(string source) =>
        Uri.TryCreate(source.Trim(), UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https";

    /// <summary>
    /// The target of a link as the person should read it before opening: the scheme and the host in its ASCII form (an internationalized host that
    /// imitates another one shows as xn--…), then the path. Null for a link that may not be opened.
    /// </summary>
    public static string? DescribeLink(string? address)
    {
        if (address is null || !Uri.TryCreate(address.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https" or "mailto"))
        {
            return null;
        }

        if (uri.Scheme == "mailto")
        {
            return "E-Mail an " + WebUtility.UrlDecode(uri.OriginalString[7..].Split('?')[0]);
        }

        var host = uri.IdnHost;
        return $"{uri.Scheme}://{host}{(uri.IsDefaultPort ? "" : ":" + uri.Port.ToString(CultureInfo.InvariantCulture))}{uri.PathAndQuery}";
    }

    /// <summary>The document of a fragment: mail styles (no spacing after a paragraph, the machine's body font, 11 pt), one page as wide as the pane.</summary>
    public static Document Build(DocumentFragment? fragment, double width, DocumentFonts fonts)
    {
        var blank = BlankDocument.Create(fonts);
        var styles = blank.Styles;
        foreach (var id in fragment?.Styles?.Styles.Select(s => s.Id) ?? [])
        {
            styles = BuiltInStyles.Ensure(styles, id);
        }

        styles = styles.WithDefaults(
            styles.ParagraphDefaults with { SpacingAfter = 0, SpacingBefore = 0, LineSpacing = LineSpacing.Single },
            styles.CharacterDefaults with { FontSize = 11, FontFamily = fonts.Body });
        if (styles.Find(BuiltInStyles.Normal) is { } normal)
        {
            styles = styles.With(normal with { Paragraph = normal.Paragraph with { SpacingAfter = 0, SpacingBefore = 0, LineSpacing = LineSpacing.Single } });
        }

        var section = SectionProperties.Default with
        {
            PageWidth = Math.Max(120, width),
            PageHeight = 72 * 2000,
            MarginLeft = 0,
            MarginRight = 0,
            MarginTop = 0,
            MarginBottom = 0,
        };
        var blocks = fragment?.Blocks ?? [Paragraph.Empty()];
        return new Document(blocks, styles, fragment?.Numbering, section, blank.Theme);
    }

    /// <summary>A plain text as a document (the composer's start, a text-only signature).</summary>
    public static string TextAsHtml(string text)
    {
        var html = new StringBuilder();
        foreach (var line in text.Replace("\r\n", "\n").Split('\n'))
        {
            html.Append("<div>").Append(line.Length == 0 ? "<br>" : WebUtility.HtmlEncode(line)).Append("</div>");
        }

        return html.ToString();
    }
}

/// <summary>
/// Remote pictures of a mail, fetched only after consent: http/https only, no cookies, no referrer, at most 40 pictures of 5 MB each, 10 s each,
/// and only what really is a picture (PNG, JPEG, GIF, BMP – checked by its header, not by the server's word).
/// </summary>
public static class RemotePictures
{
    public const int MaximumCount = 40;

    public const int MaximumBytes = 5 * 1024 * 1024;

    private static readonly HttpClient Client = CreateClient();

    /// <summary>Tests answer the requests themselves.</summary>
    internal static Func<string, CancellationToken, Task<byte[]?>>? FetchForTests { get; set; }

    public static async Task<IReadOnlyDictionary<string, byte[]>> FetchAsync(IEnumerable<string> sources, CancellationToken cancellationToken)
    {
        var result = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var tasks = sources.Where(MailHtml.IsRemote).Distinct(StringComparer.Ordinal).Take(MaximumCount)
            .Select(async source => (source, bytes: await FetchOneAsync(source, cancellationToken))).ToList();
        foreach (var (source, bytes) in await Task.WhenAll(tasks))
        {
            if (bytes is not null && ImageData.FromBytes(bytes) is not null)
            {
                result[source] = bytes;
            }
        }

        return result;
    }

    private static async Task<byte[]?> FetchOneAsync(string source, CancellationToken cancellationToken)
    {
        if (FetchForTests is { } fetch)
        {
            return await fetch(source, cancellationToken);
        }

        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(10));
            using var response = await Client.GetAsync(source, HttpCompletionOption.ResponseHeadersRead, timeout.Token);
            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumBytes)
            {
                return null;
            }

            await using var stream = await response.Content.ReadAsStreamAsync(timeout.Token);
            using var buffer = new MemoryStream();
            var chunk = new byte[81920];
            int read;
            while ((read = await stream.ReadAsync(chunk, timeout.Token)) > 0)
            {
                buffer.Write(chunk, 0, read);
                if (buffer.Length > MaximumBytes)
                {
                    return null;
                }
            }

            return buffer.ToArray();
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static HttpClient CreateClient()
    {
        var handler = new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = true, MaxAutomaticRedirections = 3 };
        var client = new HttpClient(handler) { Timeout = TimeSpan.FromSeconds(15) };
        client.DefaultRequestHeaders.UserAgent.ParseAdd("ThixMail");
        return client;
    }
}

/// <summary>What may happen with an attachment: saved always; opened only with a warning when its type can run code on this computer.</summary>
public static class AttachmentSafety
{
    private static readonly HashSet<string> Executable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".exe", ".com", ".bat", ".cmd", ".msi", ".msp", ".msc", ".scr", ".pif", ".cpl", ".dll", ".sys", ".vbs", ".vbe", ".js", ".jse", ".wsf", ".wsh",
        ".ps1", ".psm1", ".psd1", ".hta", ".jar", ".lnk", ".reg", ".inf", ".application", ".appref-ms", ".gadget", ".scf", ".url", ".iso", ".img",
        ".vhd", ".vhdx", ".app", ".command", ".pkg", ".dmg", ".sh", ".csh", ".ksh", ".bash", ".zsh", ".py", ".pyw", ".pl", ".rb", ".action",
        ".workflow", ".terminal", ".docm", ".dotm", ".xlsm", ".xltm", ".xlam", ".pptm", ".potm", ".ppam", ".ppsm", ".sldm", ".chm", ".library-ms",
        ".settingcontent-ms", ".appx", ".msix", ".msixbundle",
    };

    /// <summary>True when opening the file could run a program or macros (the UI warns and never opens it without asking).</summary>
    public static bool IsExecutable(string fileName)
    {
        var name = fileName.Trim().TrimEnd('.', ' ');
        return Executable.Contains(Path.GetExtension(name));
    }
}
