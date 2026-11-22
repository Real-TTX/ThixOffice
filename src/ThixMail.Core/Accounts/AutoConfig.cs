using System.Globalization;
using System.Xml;
using System.Xml.Linq;
using ThixMail.Core.Model;

namespace ThixMail.Core.Accounts;

/// <summary>Where the settings of an account were found (the account page names it).</summary>
public enum ConfigSource
{
    Provider,
    Autoconfig,
    WellKnown,
    Ispdb,
    MxIspdb,
    Srv,
    Guess,
}

/// <summary>What the auto-configuration found for an address: the servers, the user names, and a hint for the person (an app password, OAuth).</summary>
public sealed record AutoConfigResult(ServerSettings Imap, string ImapUser, ServerSettings Smtp, string SmtpUser, ConfigSource Source, string? ProviderName = null,
    string? Hint = null, bool NeedsAppPassword = false, bool ServerSavesSent = false, string? OAuthProvider = null);

/// <summary>A provider ThixMail knows by heart (the common German and international ones).</summary>
public sealed record MailProvider(string Name, IReadOnlyList<string> Domains, ServerSettings Imap, ServerSettings Smtp, bool NeedsAppPassword = false,
    bool ServerSavesSent = false, string? Hint = null, string? OAuthProvider = null);

/// <summary>
/// The auto-configuration of an account from its address, in the order Thunderbird asks (and quietly, without sending the password anywhere): the
/// table of common providers, the provider's own autoconfig (https://autoconfig.&lt;domain&gt;/mail/config-v1.1.xml and /.well-known/…), Mozilla's
/// ISPDB, the ISPDB of the domain's MX host (a domain hosted at Google or Microsoft), SRV records (RFC 6186), and last a guess (imap./smtp.). It only
/// proposes: the account page always runs a connection test before saving. HTTP and DNS can be answered by tests.
/// </summary>
public static class AutoConfig
{
    public static IReadOnlyList<MailProvider> Providers { get; } =
    [
        new("Gmail", ["gmail.com", "googlemail.com"], new("imap.gmail.com", 993, ConnectionSecurity.SslOnConnect), new("smtp.gmail.com", 465, ConnectionSecurity.SslOnConnect),
            NeedsAppPassword: true, ServerSavesSent: true, Hint: "Google verlangt ein App-Passwort (Google-Konto › Sicherheit › Bestätigung in zwei Schritten › App-Passwörter).",
            OAuthProvider: OAuthProviders.GoogleId),
        new("Outlook.com", ["outlook.com", "outlook.de", "hotmail.com", "hotmail.de", "live.com", "live.de", "msn.com"],
            new("outlook.office365.com", 993, ConnectionSecurity.SslOnConnect), new("smtp-mail.outlook.com", 587, ConnectionSecurity.StartTls), ServerSavesSent: true,
            Hint: "Microsoft lässt IMAP und SMTP für Outlook.com und die meisten Microsoft-365-Konten nur mit der Anmeldung im Browser zu.",
            OAuthProvider: OAuthProviders.MicrosoftId),
        new("Yahoo", ["yahoo.com", "yahoo.de", "ymail.com"], new("imap.mail.yahoo.com", 993, ConnectionSecurity.SslOnConnect), new("smtp.mail.yahoo.com", 465, ConnectionSecurity.SslOnConnect),
            NeedsAppPassword: true, Hint: "Yahoo verlangt ein App-Passwort (Kontosicherheit › App-Passwort erstellen)."),
        new("AOL", ["aol.com", "aol.de"], new("imap.aol.com", 993, ConnectionSecurity.SslOnConnect), new("smtp.aol.com", 465, ConnectionSecurity.SslOnConnect),
            NeedsAppPassword: true, Hint: "AOL verlangt ein App-Passwort."),
        new("iCloud", ["icloud.com", "me.com", "mac.com"], new("imap.mail.me.com", 993, ConnectionSecurity.SslOnConnect), new("smtp.mail.me.com", 587, ConnectionSecurity.StartTls),
            NeedsAppPassword: true, Hint: "Apple verlangt ein app-spezifisches Passwort (account.apple.com › Anmeldung und Sicherheit)."),
        new("GMX", ["gmx.de", "gmx.net", "gmx.at", "gmx.ch"], new("imap.gmx.net", 993, ConnectionSecurity.SslOnConnect), new("mail.gmx.net", 587, ConnectionSecurity.StartTls),
            Hint: "Bei GMX muss der Zugriff über POP3/IMAP in den Einstellungen des Postfachs eingeschaltet sein."),
        new("WEB.DE", ["web.de"], new("imap.web.de", 993, ConnectionSecurity.SslOnConnect), new("smtp.web.de", 587, ConnectionSecurity.StartTls),
            Hint: "Bei WEB.DE muss der Zugriff über POP3/IMAP in den Einstellungen des Postfachs eingeschaltet sein."),
        new("Telekom", ["t-online.de", "magenta.de"], new("secureimap.t-online.de", 993, ConnectionSecurity.SslOnConnect), new("securesmtp.t-online.de", 465, ConnectionSecurity.SslOnConnect),
            Hint: "Die Telekom verlangt ein eigenes E-Mail-Passwort (Telekom Login › E-Mail-Passwort)."),
        new("Posteo", ["posteo.de", "posteo.net", "posteo.org", "posteo.eu"], new("posteo.de", 993, ConnectionSecurity.SslOnConnect), new("posteo.de", 587, ConnectionSecurity.StartTls)),
        new("mailbox.org", ["mailbox.org"], new("imap.mailbox.org", 993, ConnectionSecurity.SslOnConnect), new("smtp.mailbox.org", 465, ConnectionSecurity.SslOnConnect)),
        new("freenet", ["freenet.de"], new("mx.freenet.de", 993, ConnectionSecurity.SslOnConnect), new("mx.freenet.de", 587, ConnectionSecurity.StartTls)),
        new("IONOS", ["ionos.de", "online.de", "onlinehome.de"], new("imap.ionos.de", 993, ConnectionSecurity.SslOnConnect), new("smtp.ionos.de", 587, ConnectionSecurity.StartTls)),
        new("Strato", ["strato.de"], new("imap.strato.de", 993, ConnectionSecurity.SslOnConnect), new("smtp.strato.de", 465, ConnectionSecurity.SslOnConnect)),
        new("Vodafone", ["vodafone.de", "arcor.de", "vodafonemail.de"], new("imap.vodafonemail.de", 993, ConnectionSecurity.SslOnConnect), new("smtp.vodafonemail.de", 587, ConnectionSecurity.StartTls)),
    ];

    /// <summary>Tests: URL → body (null = not found).</summary>
    internal static Func<string, Task<string?>>? FetchForTests { get; set; }

    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseCookies = false, AllowAutoRedirect = true }) { Timeout = TimeSpan.FromSeconds(8) };

    /// <summary>The settings for <paramref name="address"/> (always something: the guess comes last).</summary>
    public static async Task<AutoConfigResult> DiscoverAsync(string address, CancellationToken cancellationToken)
    {
        var at = address.LastIndexOf('@');
        var domain = at < 0 ? address : address[(at + 1)..].Trim().ToLowerInvariant();

        if (Providers.FirstOrDefault(p => p.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase)) is { } provider)
        {
            return FromProvider(provider, address);
        }

        foreach (var (url, source) in new[]
                 {
                     ($"https://autoconfig.{domain}/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(address)}", ConfigSource.Autoconfig),
                     ($"https://{domain}/.well-known/autoconfig/mail/config-v1.1.xml?emailaddress={Uri.EscapeDataString(address)}", ConfigSource.WellKnown),
                     ($"https://autoconfig.thunderbird.net/v1.1/{domain}", ConfigSource.Ispdb),
                 })
        {
            if (await FetchAsync(url, cancellationToken) is { } xml && Parse(xml, address, source) is { } found)
            {
                return found;
            }
        }

        var mx = await DnsLookup.MxAsync(domain, cancellationToken);
        if (mx.Count > 0)
        {
            var mxDomain = BaseDomain(mx[0]);
            if (mxDomain.EndsWith("google.com", StringComparison.OrdinalIgnoreCase) || mxDomain.EndsWith("googlemail.com", StringComparison.OrdinalIgnoreCase))
            {
                return FromProvider(Providers[0], address) with { Source = ConfigSource.MxIspdb };
            }

            if (mxDomain.EndsWith("outlook.com", StringComparison.OrdinalIgnoreCase))
            {
                return FromProvider(Providers[1], address) with { Source = ConfigSource.MxIspdb, ProviderName = "Microsoft 365" };
            }

            if (!string.Equals(mxDomain, domain, StringComparison.OrdinalIgnoreCase)
                && await FetchAsync($"https://autoconfig.thunderbird.net/v1.1/{mxDomain}", cancellationToken) is { } xml
                && Parse(xml, address, ConfigSource.MxIspdb) is { } byMx)
            {
                return byMx;
            }
        }

        var imap = await SrvServerAsync([$"_imaps._tcp.{domain}", $"_imap._tcp.{domain}"], cancellationToken);
        var smtp = await SrvServerAsync([$"_submissions._tcp.{domain}", $"_submission._tcp.{domain}"], cancellationToken);
        if (imap is not null && smtp is not null)
        {
            return new AutoConfigResult(imap, address, smtp, address, ConfigSource.Srv);
        }

        return new AutoConfigResult(new ServerSettings($"imap.{domain}", 993, ConnectionSecurity.SslOnConnect), address,
            new ServerSettings($"smtp.{domain}", 587, ConnectionSecurity.StartTls), address, ConfigSource.Guess,
            Hint: "Die Einstellungen wurden geraten. Prüfen Sie sie mit den Angaben Ihres Anbieters.");
    }

    /// <summary>The settings of a known provider for an address.</summary>
    public static AutoConfigResult FromProvider(MailProvider provider, string address) =>
        new(provider.Imap, address, provider.Smtp, address, ConfigSource.Provider, provider.Name, provider.Hint, provider.NeedsAppPassword, provider.ServerSavesSent,
            provider.OAuthProvider);

    /// <summary>The provider of an address, when ThixMail knows it.</summary>
    public static MailProvider? ProviderOf(string address)
    {
        var at = address.LastIndexOf('@');
        var domain = at < 0 ? "" : address[(at + 1)..].Trim();
        return Providers.FirstOrDefault(p => p.Domains.Contains(domain, StringComparer.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Reads a Mozilla autoconfig document (clientConfig 1.1): the first IMAP incoming server and the first SMTP outgoing server, the placeholders
    /// %EMAILADDRESS%, %EMAILLOCALPART%, %EMAILDOMAIN% filled. Null when it has neither or is not that format.
    /// </summary>
    public static AutoConfigResult? Parse(string xml, string address, ConfigSource source)
    {
        XDocument document;
        try
        {
            document = XDocument.Parse(xml, LoadOptions.None);
        }
        catch (XmlException)
        {
            return null;
        }

        var provider = document.Root?.Element("emailProvider");
        if (provider is null)
        {
            return null;
        }

        var incoming = provider.Elements("incomingServer").FirstOrDefault(e => (string?)e.Attribute("type") == "imap");
        var outgoing = provider.Elements("outgoingServer").FirstOrDefault(e => (string?)e.Attribute("type") == "smtp");
        if (incoming is null || outgoing is null)
        {
            return null;
        }

        var (imap, imapUser) = ReadServer(incoming, address, 993);
        var (smtp, smtpUser) = ReadServer(outgoing, address, 587);
        if (imap.Host.Length == 0 || smtp.Host.Length == 0)
        {
            return null;
        }

        var oauthOnly = incoming.Elements("authentication").All(a => a.Value.Trim().Equals("OAuth2", StringComparison.OrdinalIgnoreCase));
        return new AutoConfigResult(imap, imapUser, smtp, smtpUser, source, (string?)provider.Element("displayName") ?? (string?)provider.Element("displayShortName"),
            oauthOnly ? "Dieser Anbieter lässt die Anmeldung nur im Browser (OAuth) zu." : null,
            OAuthProvider: AccountService.OAuthProviderFor(address, imap.Host));
    }

    private static (ServerSettings Server, string User) ReadServer(XElement element, string address, int defaultPort)
    {
        var host = Fill((string?)element.Element("hostname") ?? "", address);
        var port = int.TryParse((string?)element.Element("port"), NumberStyles.None, CultureInfo.InvariantCulture, out var value) ? value : defaultPort;
        var security = ((string?)element.Element("socketType") ?? "SSL").Trim().ToUpperInvariant() switch
        {
            "STARTTLS" => ConnectionSecurity.StartTls,
            "PLAIN" => ConnectionSecurity.None,
            _ => ConnectionSecurity.SslOnConnect,
        };
        var user = Fill((string?)element.Element("username") ?? "%EMAILADDRESS%", address);
        return (new ServerSettings(host.Trim(), port, security), user.Trim());
    }

    private static string Fill(string text, string address)
    {
        var at = address.LastIndexOf('@');
        var local = at < 0 ? address : address[..at];
        var domain = at < 0 ? "" : address[(at + 1)..];
        return text.Replace("%EMAILADDRESS%", address, StringComparison.Ordinal).Replace("%EMAILLOCALPART%", local, StringComparison.Ordinal)
            .Replace("%EMAILDOMAIN%", domain, StringComparison.Ordinal);
    }

    private static async Task<ServerSettings?> SrvServerAsync(string[] names, CancellationToken cancellationToken)
    {
        foreach (var name in names)
        {
            var records = await DnsLookup.SrvAsync(name, cancellationToken);
            if (records.FirstOrDefault() is { } record)
            {
                var implicitTls = name.StartsWith("_imaps", StringComparison.Ordinal) || name.StartsWith("_submissions", StringComparison.Ordinal);
                return new ServerSettings(record.Target, record.Port, implicitTls ? ConnectionSecurity.SslOnConnect : ConnectionSecurity.StartTls);
            }
        }

        return null;
    }

    /// <summary>"aspmx.l.google.com" → "google.com" (the last two labels; three for co.uk-like domains).</summary>
    internal static string BaseDomain(string host)
    {
        var labels = host.TrimEnd('.').Split('.');
        if (labels.Length <= 2)
        {
            return host.TrimEnd('.');
        }

        var take = labels[^2].Length <= 3 && labels[^1].Length == 2 ? 3 : 2;
        return string.Join('.', labels[^take..]);
    }

    private static async Task<string?> FetchAsync(string url, CancellationToken cancellationToken)
    {
        if (FetchForTests is { } fetch)
        {
            return await fetch(url);
        }

        try
        {
            using var response = await Client.GetAsync(url, cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadAsStringAsync(cancellationToken) : null;
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or IOException or InvalidOperationException)
        {
            return cancellationToken.IsCancellationRequested ? throw new OperationCanceledException(cancellationToken) : null;
        }
    }
}
