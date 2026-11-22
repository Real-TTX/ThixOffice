using System.Globalization;
using System.Text;

namespace ThixOffice.Pim.Calendar;

/// <summary>One property of an iCalendar component (RFC 5545): its name (upper case), its parameters (names upper case) and its raw value.</summary>
public sealed record ICalProperty(string Name, IReadOnlyDictionary<string, string> Parameters, string Value)
{
    public string? this[string parameter] => Parameters.TryGetValue(parameter, out var value) ? value : null;

    /// <summary>The value as text (TEXT values unescaped: \n, \, \; \\).</summary>
    public string Text => ICalendar.Unescape(Value);
}

/// <summary>A component (VCALENDAR, VEVENT, VTIMEZONE …) with its properties and the components inside it.</summary>
public sealed class ICalComponent(string name)
{
    public string Name { get; } = name.ToUpperInvariant();

    public List<ICalProperty> Properties { get; } = [];

    public List<ICalComponent> Children { get; } = [];

    public ICalProperty? Property(string name) => Properties.FirstOrDefault(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public IEnumerable<ICalProperty> All(string name) => Properties.Where(property => property.Name.Equals(name, StringComparison.OrdinalIgnoreCase));

    public string? Text(string name) => Property(name)?.Text;

    public ICalComponent Add(string name, string value, params (string Name, string Value)[] parameters)
    {
        Properties.Add(new ICalProperty(name.ToUpperInvariant(), parameters.ToDictionary(p => p.Name.ToUpperInvariant(), p => p.Value, StringComparer.OrdinalIgnoreCase), value));
        return this;
    }

    /// <summary>Adds a TEXT property (escaped).</summary>
    public ICalComponent AddText(string name, string text, params (string Name, string Value)[] parameters) => Add(name, ICalendar.Escape(text), parameters);
}

/// <summary>
/// A small reader and writer of iCalendar (RFC 5545) without dependencies: lines unfolded and folded at 75 octets, parameters with quoted values,
/// TEXT escaping, DATE and DATE-TIME values in UTC, with a TZID (Windows and IANA names, else the VTIMEZONE's offsets) or floating (local time).
/// Enough for invitations (iTIP, RFC 5546); what it does not understand it keeps as properties.
/// </summary>
public static class ICalendar
{
    /// <summary>The components of the text (usually one VCALENDAR); broken lines are skipped, never thrown.</summary>
    public static IReadOnlyList<ICalComponent> Parse(string text)
    {
        var roots = new List<ICalComponent>();
        var stack = new Stack<ICalComponent>();
        foreach (var line in Unfold(text))
        {
            if (ParseLine(line) is not { } property)
            {
                continue;
            }

            if (property.Name == "BEGIN")
            {
                var component = new ICalComponent(property.Value.Trim());
                if (stack.Count > 0)
                {
                    stack.Peek().Children.Add(component);
                }
                else
                {
                    roots.Add(component);
                }

                stack.Push(component);
            }
            else if (property.Name == "END")
            {
                if (stack.Count > 0)
                {
                    stack.Pop();
                }
            }
            else if (stack.Count > 0)
            {
                stack.Peek().Properties.Add(property);
            }
        }

        return roots;
    }

    /// <summary>The component as text: CRLF line ends, lines folded at 75 octets (never inside a UTF-8 sequence).</summary>
    public static string Write(ICalComponent component)
    {
        var text = new StringBuilder();
        Write(component, text);
        return text.ToString();
    }

    private static void Write(ICalComponent component, StringBuilder text)
    {
        AppendFolded(text, "BEGIN:" + component.Name);
        foreach (var property in component.Properties)
        {
            var line = new StringBuilder(property.Name);
            foreach (var (name, value) in property.Parameters)
            {
                line.Append(';').Append(name).Append('=');
                line.Append(value.IndexOfAny([':', ';', ',']) >= 0 ? "\"" + value.Replace("\"", "", StringComparison.Ordinal) + "\"" : value);
            }

            line.Append(':').Append(property.Value);
            AppendFolded(text, line.ToString());
        }

        foreach (var child in component.Children)
        {
            Write(child, text);
        }

        AppendFolded(text, "END:" + component.Name);
    }

    private static void AppendFolded(StringBuilder text, string line)
    {
        var octets = 0;
        var first = true;
        foreach (var rune in line.EnumerateRunes())
        {
            var size = rune.Utf8SequenceLength;
            if (octets + size > (first ? 75 : 74))
            {
                text.Append("\r\n ");
                octets = 0;
                first = false;
            }

            text.Append(rune.ToString());
            octets += size;
        }

        text.Append("\r\n");
    }

    private static IEnumerable<string> Unfold(string text)
    {
        var current = new StringBuilder();
        foreach (var raw in text.Replace("\r\n", "\n", StringComparison.Ordinal).Replace('\r', '\n').Split('\n'))
        {
            if (raw.Length > 0 && (raw[0] == ' ' || raw[0] == '\t'))
            {
                current.Append(raw, 1, raw.Length - 1);
                continue;
            }

            if (current.Length > 0)
            {
                yield return current.ToString();
            }

            current.Clear().Append(raw);
        }

        if (current.Length > 0)
        {
            yield return current.ToString();
        }
    }

    private static ICalProperty? ParseLine(string line)
    {
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var index = 0;
        while (index < line.Length && line[index] is not (';' or ':'))
        {
            index++;
        }

        if (index == 0 || index >= line.Length)
        {
            return null;
        }

        var name = line[..index].Trim().ToUpperInvariant();
        while (index < line.Length && line[index] == ';')
        {
            index++;
            var start = index;
            while (index < line.Length && line[index] is not ('=' or ';' or ':'))
            {
                index++;
            }

            var parameter = line[start..index].Trim().ToUpperInvariant();
            var value = "";
            if (index < line.Length && line[index] == '=')
            {
                index++;
                var values = new List<string>();
                while (true)
                {
                    if (index < line.Length && line[index] == '"')
                    {
                        var close = line.IndexOf('"', index + 1);
                        close = close < 0 ? line.Length : close;
                        values.Add(line[(index + 1)..close]);
                        index = Math.Min(line.Length, close + 1);
                    }
                    else
                    {
                        var valueStart = index;
                        while (index < line.Length && line[index] is not (';' or ':' or ','))
                        {
                            index++;
                        }

                        values.Add(line[valueStart..index]);
                    }

                    if (index < line.Length && line[index] == ',')
                    {
                        index++;
                        continue;
                    }

                    break;
                }

                value = string.Join(',', values);
            }

            if (parameter.Length > 0)
            {
                parameters[parameter] = value;
            }
        }

        return index < line.Length && line[index] == ':' ? new ICalProperty(name, parameters, line[(index + 1)..]) : null;
    }

    public static string Escape(string text) =>
        text.Replace("\\", "\\\\", StringComparison.Ordinal).Replace(";", "\\;", StringComparison.Ordinal).Replace(",", "\\,", StringComparison.Ordinal)
            .Replace("\r\n", "\n", StringComparison.Ordinal).Replace("\n", "\\n", StringComparison.Ordinal);

    public static string Unescape(string text)
    {
        if (!text.Contains('\\', StringComparison.Ordinal))
        {
            return text;
        }

        var result = new StringBuilder(text.Length);
        for (var index = 0; index < text.Length; index++)
        {
            if (text[index] == '\\' && index + 1 < text.Length)
            {
                var next = text[++index];
                result.Append(next is 'n' or 'N' ? '\n' : next);
            }
            else
            {
                result.Append(text[index]);
            }
        }

        return result.ToString();
    }

    /// <summary>
    /// The time of a DTSTART/DTEND-like property: DATE (IsDate, at midnight UTC of that date), DATE-TIME in UTC ("Z"), with a TZID (found as a Windows
    /// or IANA time zone, else by the calendar's VTIMEZONE offsets), or floating (local time). Null when the value is no date.
    /// </summary>
    public static (DateTimeOffset Time, bool IsDate)? TimeOf(ICalProperty? property, IReadOnlyList<ICalComponent>? timeZones = null)
    {
        if (property is null)
        {
            return null;
        }

        var value = property.Value.Trim();
        if (string.Equals(property["VALUE"], "DATE", StringComparison.OrdinalIgnoreCase) || (value.Length == 8 && value.All(char.IsAsciiDigit)))
        {
            return DateTime.TryParseExact(value[..Math.Min(8, value.Length)], "yyyyMMdd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date)
                ? (new DateTimeOffset(date.Year, date.Month, date.Day, 0, 0, 0, TimeSpan.Zero), true)
                : null;
        }

        var utc = value.EndsWith('Z');
        var digits = utc ? value[..^1] : value;
        if (!DateTime.TryParseExact(digits, ["yyyyMMdd'T'HHmmss", "yyyyMMdd'T'HHmm"], CultureInfo.InvariantCulture, DateTimeStyles.None, out var wall))
        {
            return null;
        }

        if (utc)
        {
            return (new DateTimeOffset(wall, TimeSpan.Zero), false);
        }

        if (property["TZID"] is { Length: > 0 } tzid)
        {
            if (FindZone(tzid) is { } zone)
            {
                return (new DateTimeOffset(wall, zone.GetUtcOffset(wall)), false);
            }

            if (OffsetFromVTimeZone(tzid, wall, timeZones) is { } offset)
            {
                return (new DateTimeOffset(wall, offset), false);
            }
        }

        return (CalendarAppointment.FromLocal(wall), false);
    }

    /// <summary>A time as a UTC DATE-TIME value ("20261126T130000Z").</summary>
    public static string Utc(DateTimeOffset time) => time.UtcDateTime.ToString("yyyyMMdd'T'HHmmss'Z'", CultureInfo.InvariantCulture);

    /// <summary>A date as a DATE value ("20261126").</summary>
    public static string Date(DateTime date) => date.ToString("yyyyMMdd", CultureInfo.InvariantCulture);

    private static TimeZoneInfo? FindZone(string id)
    {
        var name = id.Trim().Trim('"');
        if (name.StartsWith('/'))
        {
            name = name.TrimStart('/'); // "/Europe/Berlin" of some writers
        }

        foreach (var candidate in new[] { name, Converted(name, toIana: true), Converted(name, toIana: false) })
        {
            if (candidate is null)
            {
                continue;
            }

            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById(candidate);
            }
            catch (Exception exception) when (exception is TimeZoneNotFoundException or InvalidTimeZoneException)
            {
            }
        }

        return null;

        static string? Converted(string name, bool toIana) =>
            toIana ? (TimeZoneInfo.TryConvertWindowsIdToIanaId(name, out var iana) ? iana : null)
                   : (TimeZoneInfo.TryConvertIanaIdToWindowsId(name, out var windows) ? windows : null);
    }

    /// <summary>The offset of a wall time in a VTIMEZONE of the calendar: the STANDARD or DAYLIGHT part whose last start (by its yearly rule) is the latest before it.</summary>
    private static TimeSpan? OffsetFromVTimeZone(string tzid, DateTime wall, IReadOnlyList<ICalComponent>? timeZones)
    {
        var zone = timeZones?.FirstOrDefault(candidate => string.Equals(candidate.Text("TZID"), tzid, StringComparison.OrdinalIgnoreCase));
        if (zone is null)
        {
            return null;
        }

        (DateTime Start, TimeSpan Offset)? best = null;
        foreach (var part in zone.Children.Where(child => child.Name is "STANDARD" or "DAYLIGHT"))
        {
            if (ParseOffset(part.Text("TZOFFSETTO")) is not { } offset || TimeOf(part.Property("DTSTART")) is not { } start)
            {
                continue;
            }

            var startWall = start.Time.DateTime;
            var thisYear = YearlyStart(part.Text("RRULE"), wall.Year) ?? new DateTime(wall.Year, startWall.Month, startWall.Day, startWall.Hour, startWall.Minute, 0);
            foreach (var candidate in new[] { thisYear, YearlyStart(part.Text("RRULE"), wall.Year - 1) ?? thisYear.AddYears(-1) })
            {
                if (candidate <= wall && (best is null || candidate > best.Value.Start))
                {
                    best = (candidate, offset);
                }
            }
        }

        return best?.Offset;

        static TimeSpan? ParseOffset(string? text)
        {
            if (text is null || text.Length < 5)
            {
                return null;
            }

            var sign = text[0] == '-' ? -1 : 1;
            return int.TryParse(text.AsSpan(1, 2), CultureInfo.InvariantCulture, out var hours) && int.TryParse(text.AsSpan(3, 2), CultureInfo.InvariantCulture, out var minutes)
                ? TimeSpan.FromMinutes(sign * ((hours * 60) + minutes))
                : null;
        }
    }

    /// <summary>The start of a yearly rule ("FREQ=YEARLY;BYMONTH=3;BYDAY=-1SU") in a year, at 02:00 or 03:00 as the zone's DTSTART says (approximated: 02:00).</summary>
    private static DateTime? YearlyStart(string? rule, int year)
    {
        if (rule is null)
        {
            return null;
        }

        var parts = rule.Split(';').Select(part => part.Split('=')).Where(pair => pair.Length == 2).ToDictionary(pair => pair[0].ToUpperInvariant(), pair => pair[1].ToUpperInvariant());
        if (!parts.TryGetValue("BYMONTH", out var monthText) || !int.TryParse(monthText, CultureInfo.InvariantCulture, out var month) || !parts.TryGetValue("BYDAY", out var day) || day.Length < 3)
        {
            return null;
        }

        var weekday = day[^2..] switch
        {
            "SU" => DayOfWeek.Sunday,
            "MO" => DayOfWeek.Monday,
            "TU" => DayOfWeek.Tuesday,
            "WE" => DayOfWeek.Wednesday,
            "TH" => DayOfWeek.Thursday,
            "FR" => DayOfWeek.Friday,
            _ => DayOfWeek.Saturday,
        };
        if (!int.TryParse(day[..^2], CultureInfo.InvariantCulture, out var ordinal) || month is < 1 or > 12)
        {
            return null;
        }

        DateTime date;
        if (ordinal < 0)
        {
            date = new DateTime(year, month, DateTime.DaysInMonth(year, month));
            while (date.DayOfWeek != weekday)
            {
                date = date.AddDays(-1);
            }

            date = date.AddDays(7 * (ordinal + 1));
        }
        else
        {
            date = new DateTime(year, month, 1);
            while (date.DayOfWeek != weekday)
            {
                date = date.AddDays(1);
            }

            date = date.AddDays(7 * (ordinal - 1));
        }

        return date.AddHours(2);
    }
}
