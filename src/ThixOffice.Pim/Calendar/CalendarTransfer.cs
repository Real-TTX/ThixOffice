using System.Globalization;
using System.Text.Json;
using MatCloud.Contract;
using MatCloud.Contract.Calendar;
using ThixOffice.Pim.Store;

namespace ThixOffice.Pim.Calendar;

/// <summary>
/// The calendar of this computer and the calendar of a ThixCloud account (ThixMail's "Kalender" works without ThixCloud): the own calendars move between
/// the two stores without doubling anything. Into an account (<see cref="MergeInto"/>, after the account's first sync, so its own calendars are known):
/// a local calendar goes into the account's "Eigene Kalender" – into the calendar of the same name when there is one, else as a new calendar there (the
/// local "Eigene Kalender" becomes the account's when it has none) –, its appointments follow with their keys and times; what the account already has
/// (same key, not older) stays. Out of an account ("Abmelden" with a copy, <see cref="KeepLocalCopy"/>): the local store first gives what only it had to
/// the account, then becomes a copy of the account's own calendars (calendars MatCloud keeps for online accounts stay in the cloud).
/// </summary>
public static class CalendarTransfer
{
    /// <summary>Meta values of an appointment that stay with it on this computer: a meeting sent from here, the answer to its reminder.</summary>
    private static readonly string[] AppointmentMeta = ["meeting:", "reminder:"];

    /// <summary>Puts the local own calendars and their appointments into <paramref name="account"/>; answers how many items went in.</summary>
    public static int MergeInto(PimStore local, PimStore account)
    {
        var mark = "merged:" + Path.GetFileNameWithoutExtension(account.Path);
        var since = long.TryParse(local.GetMeta(mark), NumberStyles.Integer, CultureInfo.InvariantCulture, out var ms) ? DateTimeOffset.FromUnixTimeMilliseconds(ms) : DateTimeOffset.MinValue;
        var localItems = local.AllItems();
        var localOwn = CalendarItems.Accounts(local).FirstOrDefault(entry => entry.IsOwn);
        if (localOwn is null || localItems.Count == 0)
        {
            return 0;
        }

        var known = account.AllItems(withDeleted: true).ToDictionary(item => (item.Collection, item.Key), item => item.UpdatedAt);
        bool Wanted(StoredItem item) => item.UpdatedAt > since && (!known.TryGetValue((item.Collection, item.Key), out var there) || there < item.UpdatedAt);

        var changes = new List<StoredItem>();
        var accountOwn = CalendarItems.Accounts(account).FirstOrDefault(entry => entry.IsOwn);
        var target = accountOwn?.SyncId ?? localOwn.SyncId;
        if (accountOwn is null && local.Get(CalendarContract.Accounts, localOwn.SyncId) is { } ownItem && Wanted(ownItem))
        {
            changes.Add(ownItem); // the account has no own calendars yet: this computer's "Eigene Kalender" becomes its
        }

        var existing = CalendarItems.Calendars(account).Where(calendar => calendar.IsOwn && calendar.AccountSyncId == target).ToList();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var item in local.Items(CalendarContract.Calendars).Where(item => CalendarItems.AccountOf(item.Key) == localOwn.SyncId))
        {
            if (CalendarItems.Read<CalendarCalendarItem>(item.Data) is not { } data)
            {
                continue;
            }

            if (existing.FirstOrDefault(calendar => string.Equals(calendar.Name.Trim(), (data.Name ?? "").Trim(), StringComparison.CurrentCultureIgnoreCase)) is { } same)
            {
                map[item.Key] = same.Key; // "Kalender" here and "Kalender" in the account: one calendar
                continue;
            }

            var key = CalendarContract.CalendarKey(target, data.RemoteId);
            map[item.Key] = key;
            var moved = item with { Key = key };
            if (Wanted(moved) || !known.ContainsKey((CalendarContract.Calendars, key)))
            {
                changes.Add(moved with { UpdatedAt = item.UpdatedAt });
            }
        }

        var events = new List<string>();
        foreach (var item in local.Items(CalendarContract.Events))
        {
            if (!Wanted(item) || CalendarItems.Read<CalendarEventItem>(item.Data) is not { } data || !map.TryGetValue(data.Calendar ?? "", out var calendar))
            {
                continue;
            }

            changes.Add(item with { Data = JsonSerializer.Serialize(data with { Calendar = calendar }, CloudContract.Json) });
            events.Add(item.Key);
        }

        if (changes.Count > 0)
        {
            account.Import(changes);
            CopyMeta(local, account, events);
        }

        local.SetMeta(mark, localItems.Max(item => item.UpdatedAt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        return changes.Count;
    }

    /// <summary>
    /// "Abmelden" with a copy: what only this computer had goes into the account first (<see cref="MergeInto"/>), then the local store at
    /// <paramref name="localPath"/> becomes a copy of the account's own calendars and their appointments (with their meetings and reminder answers).
    /// </summary>
    public static void KeepLocalCopy(PimStore account, string localPath)
    {
        using var local = PimStore.Open(localPath);
        MergeInto(local, account);
        var own = CalendarItems.Accounts(account).Where(entry => entry.IsOwn).Select(entry => entry.SyncId).ToHashSet(StringComparer.Ordinal);
        var calendars = account.Items(CalendarContract.Calendars).Where(item => own.Contains(CalendarItems.AccountOf(item.Key))).Select(item => item.Key).ToHashSet(StringComparer.Ordinal);
        var items = account.AllItems().Where(item => item.Collection switch
        {
            CalendarContract.Accounts => own.Contains(item.Key),
            CalendarContract.Calendars => calendars.Contains(item.Key),
            CalendarContract.Events => CalendarItems.Read<CalendarEventItem>(item.Data) is { } data && calendars.Contains(data.Calendar ?? ""),
            _ => false,
        }).ToList();
        local.ReplaceContent(items, []);
        CopyMeta(account, local, items.Where(item => item.Collection == CalendarContract.Events).Select(item => item.Key));
        if (items.Count > 0)
        {
            local.SetMeta("merged:" + Path.GetFileNameWithoutExtension(account.Path), items.Max(item => item.UpdatedAt).ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void CopyMeta(PimStore from, PimStore to, IEnumerable<string> eventKeys)
    {
        foreach (var key in eventKeys)
        {
            foreach (var prefix in AppointmentMeta)
            {
                if (from.GetMeta(prefix + key) is { Length: > 0 } value)
                {
                    to.SetMeta(prefix + key, value);
                }
            }
        }
    }
}
