using System.Globalization;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formatting;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Date and time (Datum und Uhrzeit) on Excel's serials (1900 with its leap-year bug, or 1904).</summary>
internal static class DateTimeFunctions
{
    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.DateTime;
        r.Add("DATE", 3, 3, call => DateSerial.FromParts(call.Number(0), call.Number(1), call.Number(2), call.Context.Date1904) is { } serial ? serial : CellError.Number);
        r.Add("TIME", 3, 3, call =>
        {
            var seconds = Math.Truncate(call.Number(0)) * 3600 + Math.Truncate(call.Number(1)) * 60 + Math.Truncate(call.Number(2));
            Require(seconds >= 0 && call.Number(0) < 32768);
            return Result(seconds % 86400 / 86400);
        });
        r.Add("YEAR", 1, 1, call => DateSerial.ToDate(Serial(call, 0), call.Context.Date1904).Year);
        r.Add("MONTH", 1, 1, call => DateSerial.ToDate(Serial(call, 0), call.Context.Date1904).Month);
        r.Add("DAY", 1, 1, call => DateSerial.ToDate(Serial(call, 0), call.Context.Date1904).Day);
        r.Add("HOUR", 1, 1, call => DateSerial.TimeOf(Serial(call, 0)).Hour);
        r.Add("MINUTE", 1, 1, call => DateSerial.TimeOf(Serial(call, 0)).Minute);
        r.Add("SECOND", 1, 1, call => DateSerial.TimeOf(Serial(call, 0)).Second);
        r.Add("WEEKDAY", 1, 2, Weekday);
        r.Add("WEEKNUM", 1, 2, WeekNumber);
        r.Add("ISOWEEKNUM", 1, 1, call => IsoWeek(Serial(call, 0), call.Context.Date1904));
        r.Add("TODAY", 0, 0, call => Math.Floor(DateSerial.FromDateTime(call.Context.Engine.Clock(), call.Context.Date1904)), isVolatile: true);
        r.Add("NOW", 0, 0, call => DateSerial.FromDateTime(call.Context.Engine.Clock(), call.Context.Date1904), isVolatile: true);
        r.Add("EDATE", 2, 2, call => AddMonths(call, endOfMonth: false));
        r.Add("EOMONTH", 2, 2, call => AddMonths(call, endOfMonth: true));
        r.Add("DATEDIF", 3, 3, DateDifference);
        r.Add("DAYS", 2, 2, call => Math.Floor(Serial(call, 0)) - Math.Floor(Serial(call, 1)));
        r.Add("DAYS360", 2, 3, call => DayCount.Days360(Serial(call, 0), Serial(call, 1), call.Boolean(2, false), call.Context.Date1904));
        r.Add("YEARFRAC", 2, 3, call =>
        {
            var basis = (int)Math.Truncate(call.Number(2, 0));
            Require(basis is >= 0 and <= 4);
            return Result(DayCount.YearFraction(Serial(call, 0), Serial(call, 1), basis, call.Context.Date1904));
        });
        r.Add("NETWORKDAYS", 2, 3, call => NetworkDays(call, Weekend(1), 2), [ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Any]);
        r.Add("NETWORKDAYS.INTL", 2, 4, call => NetworkDays(call, call.Has(2) ? Weekend(call.Value(2)) : Weekend(1), 3),
            [ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Any]);
        r.Add("WORKDAY", 2, 3, call => Workday(call, Weekend(1), 2), [ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Any]);
        r.Add("WORKDAY.INTL", 2, 4, call => Workday(call, call.Has(2) ? Weekend(call.Value(2)) : Weekend(1), 3),
            [ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Any]);
        r.Add("DATEVALUE", 1, 1, call =>
        {
            var text = call.Text(0).Trim();
            var context = call.Context;
            DateTime Today() => context.Engine.Clock();
            if (ValueParser.TryParseDate(text, context.Locale, context.Date1904, Today, out var date, out _))
            {
                return date;
            }

            var space = text.LastIndexOf(' ');
            if (space > 0 && ValueParser.TryParseDate(text[..space], context.Locale, context.Date1904, Today, out date, out _))
            {
                return date;
            }

            return CellError.Value;
        });
        r.Add("TIMEVALUE", 1, 1, call =>
        {
            var text = call.Text(0).Trim();
            if (ValueParser.TryParseTime(text, out var time, out _))
            {
                return time - Math.Floor(time);
            }

            if (ValueParser.TryParseNumber(text, call.Context.Locale, call.Context.Date1904, out var value))
            {
                return value - Math.Floor(value);
            }

            return CellError.Value;
        });
    }

    /// <summary>A date argument (numbers, date texts); negative or past 9999-12-31 → #NUM!.</summary>
    internal static double Serial(FunctionCall call, int index)
    {
        var value = call.Number(index);
        Require(value >= 0 && value < DateSerial.MaxSerial(call.Context.Date1904) + 1);
        return value;
    }

    private static Operand Weekday(FunctionCall call)
    {
        var dayOfWeek = DateSerial.DayOfWeek(Serial(call, 0), call.Context.Date1904);
        var type = (int)Math.Truncate(call.Number(1, 1));
        return type switch
        {
            1 or 17 => dayOfWeek + 1,
            2 or 11 => (dayOfWeek + 6) % 7 + 1,
            3 => (dayOfWeek + 6) % 7,
            >= 12 and <= 16 => (dayOfWeek - (type - 10) + 7) % 7 + 1,
            _ => CellError.Number,
        };
    }

    private static Operand WeekNumber(FunctionCall call)
    {
        var serial = Math.Floor(Serial(call, 0));
        var type = (int)Math.Truncate(call.Number(1, 1));
        var date1904 = call.Context.Date1904;
        if (type == 21)
        {
            return IsoWeek(serial, date1904);
        }

        var start = type switch
        {
            1 or 17 => 0,
            2 or 11 => 1,
            >= 12 and <= 16 => type - 10,
            _ => -1,
        };
        Require(start >= 0);
        var (year, _, _) = DateSerial.ToDate(serial, date1904);
        var january1 = DateSerial.FromDate(year, 1, 1, date1904) ?? (date1904 ? 0 : 1);
        var offset = (DateSerial.DayOfWeek(january1, date1904) - start + 7) % 7;
        return Math.Floor((serial - january1 + offset) / 7) + 1;
    }

    private static int IsoWeek(double serial, bool date1904)
    {
        var date = DateSerial.ToDateTime(Math.Floor(serial), date1904) ?? throw new CalcErrorException(CellError.Number);
        return ISOWeek.GetWeekOfYear(date);
    }

    private static Operand AddMonths(FunctionCall call, bool endOfMonth)
    {
        var date1904 = call.Context.Date1904;
        var (year, month, day) = DateSerial.ToDate(Serial(call, 0), date1904);
        var months = (int)Math.Truncate(call.Number(1));
        var total = year * 12 + (month - 1) + months;
        var newYear = total / 12;
        var newMonth = total % 12 + 1;
        Require(newYear is >= 1900 and <= 9999);
        var last = DateTime.DaysInMonth(newYear, newMonth);
        var newDay = endOfMonth ? last : Math.Min(Math.Max(day, 1), last);
        return DateSerial.FromDate(newYear, newMonth, newDay, date1904) is { } serial ? serial : CellError.Number;
    }

    private static Operand DateDifference(FunctionCall call)
    {
        var date1904 = call.Context.Date1904;
        var start = Math.Floor(Serial(call, 0));
        var end = Math.Floor(Serial(call, 1));
        Require(start <= end);
        var unit = call.Text(2).Trim().ToUpperInvariant();
        var (y1, m1, d1) = DateSerial.ToDate(start, date1904);
        var (y2, m2, d2) = DateSerial.ToDate(end, date1904);
        var months = (y2 - y1) * 12 + m2 - m1 - (d2 < d1 ? 1 : 0);
        switch (unit)
        {
            case "D":
                return end - start;
            case "M":
                return months;
            case "Y":
                return Math.Floor(months / 12.0);
            case "YM":
                return months % 12;
            case "MD":
            {
                if (d2 >= d1)
                {
                    return d2 - d1;
                }

                var previousMonth = m2 == 1 ? 12 : m2 - 1;
                var previousYear = m2 == 1 ? y2 - 1 : y2;
                return DateTime.DaysInMonth(previousYear, previousMonth) - d1 + d2;
            }

            case "YD":
            {
                var targetYear = m2 > m1 || (m2 == m1 && d2 >= d1) ? y2 : y2 - 1;
                var anniversary = DateSerial.FromDate(targetYear, m1, Math.Min(d1, DateTime.DaysInMonth(targetYear, m1)), date1904) ?? start;
                return end - anniversary;
            }

            default:
                return CellError.Number;
        }
    }

    /// <summary>The weekend mask (index 0 = Sunday) of a weekend number (1–7, 11–17) or a 7-character string from Monday.</summary>
    private static bool[] Weekend(CellValue value)
    {
        if (value.IsText)
        {
            var text = value.Text;
            Require(text.Length == 7 && text.All(character => character is '0' or '1') && text != "1111111", CellError.Value);
            var mask = new bool[7];
            for (var index = 0; index < 7; index++)
            {
                mask[(index + 1) % 7] = text[index] == '1';
            }

            return mask;
        }

        Require(value.IsNumber || value.IsBlank, CellError.Value);
        return Weekend((int)value.Number);
    }

    private static bool[] Weekend(int code)
    {
        var mask = new bool[7];
        switch (code)
        {
            case >= 1 and <= 7:
                // 1 = Saturday+Sunday, 2 = Sunday+Monday … 7 = Friday+Saturday.
                mask[(code + 5) % 7] = true;
                mask[(code + 6) % 7] = true;
                break;
            case >= 11 and <= 17:
                mask[(code - 11) % 7] = true;
                break;
            default:
                throw new CalcErrorException(CellError.Number);
        }

        return mask;
    }

    private static HashSet<double> Holidays(FunctionCall call, int index)
    {
        var holidays = new HashSet<double>();
        if (!call.Has(index))
        {
            return holidays;
        }

        foreach (var value in call.Values(index))
        {
            if (value.IsError)
            {
                throw new CalcErrorException(value.Error);
            }

            if (value.IsNumber)
            {
                holidays.Add(Math.Floor(value.Number));
            }
        }

        return holidays;
    }

    private static Operand NetworkDays(FunctionCall call, bool[] weekend, int holidayIndex)
    {
        var date1904 = call.Context.Date1904;
        var start = Math.Floor(Serial(call, 0));
        var end = Math.Floor(Serial(call, 1));
        var holidays = Holidays(call, holidayIndex);
        var sign = 1;
        if (start > end)
        {
            (start, end) = (end, start);
            sign = -1;
        }

        // Whole weeks first, then the rest day by day.
        var workdaysPerWeek = weekend.Count(day => !day);
        var weeks = Math.Floor((end - start + 1) / 7);
        var count = weeks * workdaysPerWeek;
        for (var serial = start + weeks * 7; serial <= end; serial++)
        {
            if (!weekend[DateSerial.DayOfWeek(serial, date1904)])
            {
                count++;
            }
        }

        foreach (var holiday in holidays)
        {
            if (holiday >= start && holiday <= end && !weekend[DateSerial.DayOfWeek(holiday, date1904)])
            {
                count--;
            }
        }

        return sign * count;
    }

    private static Operand Workday(FunctionCall call, bool[] weekend, int holidayIndex)
    {
        var date1904 = call.Context.Date1904;
        var serial = Math.Floor(Serial(call, 0));
        var days = (long)Math.Truncate(call.Number(1));
        var holidays = Holidays(call, holidayIndex);
        var step = days >= 0 ? 1 : -1;
        var remaining = Math.Abs(days);
        var maximum = DateSerial.MaxSerial(date1904);
        while (remaining > 0)
        {
            serial += step;
            Require(serial >= 0 && serial <= maximum);
            if (!weekend[DateSerial.DayOfWeek(serial, date1904)] && !holidays.Contains(serial))
            {
                remaining--;
            }
        }

        return serial;
    }
}
