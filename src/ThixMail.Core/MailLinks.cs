using System.Globalization;

namespace ThixMail.Core;

/// <summary>
/// ThixMail's links (its URL scheme <c>thixmail:</c>, registered by the setup): what a system notification starts when it is clicked –
/// <c>thixmail:message?id=42</c> – and the running instance takes over.
/// </summary>
public static class MailLinks
{
    public const string Scheme = "thixmail";

    public static string Message(long messageId) => $"{Scheme}:message?id={messageId.ToString(CultureInfo.InvariantCulture)}";

    public static bool IsLink(string text) => text.StartsWith(Scheme + ":", StringComparison.OrdinalIgnoreCase);

    /// <summary>The message of a link, or null (another link, a damaged one).</summary>
    public static long? MessageId(string link)
    {
        const string prefix = Scheme + ":message?id=";
        return link.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
               && long.TryParse(link.AsSpan(prefix.Length).TrimEnd('/'), NumberStyles.None, CultureInfo.InvariantCulture, out var id) && id > 0
            ? id
            : null;
    }
}
