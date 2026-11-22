using System.Globalization;
using ThixMail.Core.Store;

namespace ThixMail.Core;

/// <summary>What a row of the message list is.</summary>
public enum RowKind
{
    /// <summary>"Heute", "Gestern", "Letzte Woche" … (only while sorted by date).</summary>
    Group,

    /// <summary>A message, or the newest message of a conversation (with <see cref="ListRow.Count"/> &gt; 1).</summary>
    Message,

    /// <summary>An older message of an expanded conversation (indented).</summary>
    Child,
}

/// <summary>A row of the message list: a date group's heading or a message (its id; the header is read only when the row shows).</summary>
public sealed record ListRow(RowKind Kind, long Id, string Group, int Count, bool IsExpanded, long Thread)
{
    public bool IsMessage => Kind != RowKind.Group;
}

/// <summary>
/// The rows of the message list from the store's entries: Outlook's date groups (Heute, Gestern, the weekdays of this week, Letzte Woche, Vor zwei
/// Wochen, Vor drei Wochen, Letzter Monat, Älter) while sorted by date, and – with conversations on – one row per conversation (its newest message,
/// the number of its messages in the list), expanded ones followed by their other messages. Linear in the number of entries (200,000 in a few ms).
/// </summary>
public static class MessageList
{
    private static readonly CultureInfo German = CultureInfo.GetCultureInfo("de-DE");

    public static IReadOnlyList<ListRow> Build(IReadOnlyList<ListEntry> entries, bool conversations, bool dateGroups, IReadOnlySet<long> expanded, DateTimeOffset now)
    {
        var rows = new List<ListRow>(entries.Count + 16);
        Dictionary<long, List<ListEntry>>? threads = null;
        if (conversations)
        {
            threads = [];
            foreach (var entry in entries)
            {
                if (!threads.TryGetValue(entry.Thread, out var members))
                {
                    threads[entry.Thread] = members = [];
                }

                members.Add(entry);
            }
        }

        var today = now.ToLocalTime().Date;
        string? group = null;
        var emitted = new HashSet<long>();
        foreach (var entry in entries)
        {
            if (threads is not null && !emitted.Add(entry.Thread))
            {
                continue; // a later message of a conversation that already has its row
            }

            if (dateGroups)
            {
                var name = GroupOf(DateTimeOffset.FromUnixTimeSeconds(entry.Date).ToLocalTime().Date, today);
                if (name != group)
                {
                    group = name;
                    rows.Add(new ListRow(RowKind.Group, 0, name, 0, false, 0));
                }
            }

            if (threads is null)
            {
                rows.Add(new ListRow(RowKind.Message, entry.Id, group ?? "", 1, false, entry.Thread));
                continue;
            }

            var members = threads[entry.Thread];
            var isExpanded = members.Count > 1 && expanded.Contains(entry.Thread);
            rows.Add(new ListRow(RowKind.Message, entry.Id, group ?? "", members.Count, isExpanded, entry.Thread));
            if (isExpanded)
            {
                foreach (var member in members.Where(m => m.Id != entry.Id))
                {
                    rows.Add(new ListRow(RowKind.Child, member.Id, group ?? "", 1, false, entry.Thread));
                }
            }
        }

        return rows;
    }

    /// <summary>The date group of a day, seen from <paramref name="today"/> (weeks start on Monday).</summary>
    public static string GroupOf(DateTime day, DateTime today)
    {
        if (day >= today)
        {
            return day > today ? "Zukünftig" : "Heute";
        }

        if (day == today.AddDays(-1))
        {
            return "Gestern";
        }

        var weekStart = today.AddDays(-(((int)today.DayOfWeek + 6) % 7));
        if (day >= weekStart)
        {
            return German.DateTimeFormat.GetDayName(day.DayOfWeek);
        }

        if (day >= weekStart.AddDays(-7))
        {
            return "Letzte Woche";
        }

        if (day >= weekStart.AddDays(-14))
        {
            return "Vor zwei Wochen";
        }

        if (day >= weekStart.AddDays(-21))
        {
            return "Vor drei Wochen";
        }

        var monthStart = new DateTime(today.Year, today.Month, 1);
        if (day >= monthStart)
        {
            return "Anfang dieses Monats";
        }

        return day >= monthStart.AddMonths(-1) ? "Letzter Monat" : "Älter";
    }

    /// <summary>The time a list row shows: the time today, the weekday this week, else the date.</summary>
    public static string ShortDate(DateTimeOffset date, DateTimeOffset now)
    {
        var local = date.ToLocalTime();
        var today = now.ToLocalTime().Date;
        if (local.Date == today)
        {
            return local.ToString("HH:mm", German);
        }

        if (local.Date > today.AddDays(-7))
        {
            return local.ToString("ddd HH:mm", German);
        }

        return local.ToString(local.Year == today.Year ? "dd.MM." : "dd.MM.yyyy", German);
    }
}
