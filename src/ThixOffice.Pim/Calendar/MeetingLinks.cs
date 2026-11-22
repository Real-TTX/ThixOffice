using System.Text.RegularExpressions;

namespace ThixOffice.Pim.Calendar;

/// <summary>
/// The address to join an online meeting (Teams, Meet, Zoom, Webex, …) found in an appointment's location or notes – ThixCalendar's rule (its
/// MeetingLinks): an event without a meeting address gets the first one of its texts when it is saved. ThixMail applies the same rule, so an appointment
/// it writes is exactly what ThixCalendar would store for it (otherwise ThixCalendar writes it back with the address on its next save).
/// </summary>
public static partial class MeetingLinks
{
    [GeneratedRegex(
        @"https://(?:teams\.microsoft\.com/l/meetup-join/|teams\.live\.com/meet/|meet\.google\.com/[a-z]{3}-[a-z]{4}-[a-z]{3}|(?:[\w-]+\.)?zoom\.us/(?:j|my|w)/|[\w-]+\.webex\.com/|whereby\.com/|meet\.jit\.si/|[\w-]+\.gotomeeting\.com/|app\.chime\.aws/)[^\s""'<>)\]]*",
        RegexOptions.IgnoreCase)]
    private static partial Regex MeetingPattern();

    /// <summary>The first meeting address in the texts, or "".</summary>
    public static string Find(params string?[] texts)
    {
        foreach (var text in texts)
        {
            if (!string.IsNullOrEmpty(text) && MeetingPattern().Match(text) is { Success: true } match)
            {
                return match.Value.TrimEnd('.', ',', ';');
            }
        }

        return "";
    }

    /// <summary>True for an address ThixCalendar accepts as a meeting address: absolute, http or https.</summary>
    public static bool IsWebAddress(string address) => Uri.TryCreate(address, UriKind.Absolute, out var uri) && uri.Scheme is "https" or "http";
}
