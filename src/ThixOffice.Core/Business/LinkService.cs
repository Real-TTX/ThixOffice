using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Business;

/// <summary>
/// Links in documents: what an address may be (only web pages and e-mail – http, https, mailto; never a file, a program or a
/// script) and opening one (Strg+Klick). "www.example.de" becomes https://, "name@example.de" mailto:. The system's browser or
/// mail program is started by the caller's <c>open</c> (the UI's), after this check.
/// </summary>
public static class LinkService
{
    public const string AddressField = "address";

    /// <summary>The schemes a link may have.</summary>
    public static IReadOnlyList<string> AllowedSchemes { get; } = ["http", "https", "mailto"];

    /// <summary>The address as it is stored and opened; a <see cref="BusinessException"/> at <see cref="AddressField"/> when it is not allowed.</summary>
    public static string Normalize(string? address)
    {
        var text = (address ?? "").Trim();
        if (text.Length == 0)
        {
            throw new BusinessException("Bitte geben Sie eine Adresse ein.", AddressField);
        }

        if (!text.Contains(':', StringComparison.Ordinal))
        {
            text = text.Contains('@', StringComparison.Ordinal) && !text.Contains('/', StringComparison.Ordinal) ? "mailto:" + text : "https://" + text;
        }

        if (!Uri.TryCreate(text, UriKind.Absolute, out var uri) || !AllowedSchemes.Contains(uri.Scheme, StringComparer.OrdinalIgnoreCase)
            || (uri.Scheme != Uri.UriSchemeMailto && string.IsNullOrEmpty(uri.Host)))
        {
            throw new BusinessException("Diese Adresse kann kein Link sein. Möglich sind Webseiten (https://…) und E-Mail-Adressen.", AddressField);
        }

        return text;
    }

    /// <summary>Opens a link with <paramref name="open"/> (the system's browser or mail program) after the check.</summary>
    public static void Open(string? address, Action<string> open)
    {
        ArgumentNullException.ThrowIfNull(open);
        var checkedAddress = Normalize(address);
        Log.Info($"A link is opened ({new Uri(checkedAddress).Scheme})");
        open(checkedAddress);
    }
}
