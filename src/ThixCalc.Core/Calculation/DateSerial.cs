namespace ThixCalc.Core.Calculation;

/// <summary>
/// Excel's date serials. In the 1900 system serial 1 is 1900-01-01 and serial 60 is the non-existent 1900-02-29 (Lotus'
/// leap-year bug, kept for compatibility), so dates from 1900-03-01 on are days since 1899-12-30; serial 0 shows as
/// 1900-01-00. In the 1904 system serial 0 is 1904-01-01. The fraction is the time of day. Valid serials end at 9999-12-31.
/// </summary>
public static class DateSerial
{
    /// <summary>The serial of 9999-12-31 in the 1900 system.</summary>
    public const double MaxSerial1900 = 2958465;

    /// <summary>The serial of 9999-12-31 in the 1904 system.</summary>
    public const double MaxSerial1904 = 2957003;

    private static readonly DateTime Epoch1900 = new(1899, 12, 30);
    private static readonly DateTime Epoch1904 = new(1904, 1, 1);

    public static double MaxSerial(bool date1904) => date1904 ? MaxSerial1904 : MaxSerial1900;

    /// <summary>The serial of a real calendar date (no time), or null outside the system's range.</summary>
    public static double? FromDate(int year, int month, int day, bool date1904 = false)
    {
        if (year < 1 || year > 9999 || month is < 1 or > 12 || day < 1 || day > DateTime.DaysInMonth(year, month))
        {
            return null;
        }

        var date = new DateTime(year, month, day);
        if (date1904)
        {
            var days = (date - Epoch1904).TotalDays;
            return days < 0 ? null : days;
        }

        var serial = (date - Epoch1900).TotalDays;
        if (serial < 61)
        {
            // Before 1900-03-01 the fake leap day is not yet counted.
            serial -= 1;
        }

        return serial < 1 ? null : serial;
    }

    /// <summary>
    /// DATE(year; month; day) as Excel computes it: years 0–1899 add 1900, months and days overflow into the next/previous
    /// year/month (DATE(2024;14;1) = 2025-02-01, DATE(2024;3;0) = 2024-02-29), 1900-02-29 exists. Null when out of range.
    /// </summary>
    public static double? FromParts(double year, double month, double day, bool date1904 = false)
    {
        var y = (long)Math.Truncate(year);
        var m = (long)Math.Truncate(month);
        var d = (long)Math.Truncate(day);
        if (y < 0 || y >= 10000)
        {
            return null;
        }

        if (y < 1900)
        {
            y += 1900;
        }

        // Normalize the month into the year.
        var totalMonths = y * 12 + (m - 1);
        var normalizedYear = Math.DivRem(totalMonths, 12, out var monthIndex);
        if (monthIndex < 0)
        {
            monthIndex += 12;
            normalizedYear--;
        }

        if (normalizedYear < 1 || normalizedYear > 9999)
        {
            return null;
        }

        var first = FromDate((int)normalizedYear, (int)monthIndex + 1, 1, date1904);
        double serial;
        if (first is null)
        {
            // The 1900 system's January 1900 and earlier: count from day 1.
            if (date1904 || normalizedYear != 1900 || monthIndex != 0)
            {
                return null;
            }

            serial = d;
        }
        else
        {
            serial = first.Value + d - 1;
            if (!date1904 && first.Value < 60 && serial >= 60)
            {
                // Crossing the fake 1900-02-29 from January/February 1900.
                serial += 0;
            }
        }

        if (serial < 0 || serial > MaxSerial(date1904))
        {
            return null;
        }

        return serial;
    }

    /// <summary>The calendar date of a serial (1900-02-29 for serial 60; 1900-01-00 is reported as day 0 of January).</summary>
    public static (int Year, int Month, int Day) ToDate(double serial, bool date1904 = false)
    {
        var days = (long)Math.Floor(serial);
        if (date1904)
        {
            var date = Epoch1904.AddDays(days);
            return (date.Year, date.Month, date.Day);
        }

        if (days == 0)
        {
            return (1900, 1, 0);
        }

        if (days == 60)
        {
            return (1900, 2, 29);
        }

        var value = days < 60 ? Epoch1900.AddDays(days + 1) : Epoch1900.AddDays(days);
        return (value.Year, value.Month, value.Day);
    }

    /// <summary>The serial as a DateTime (1900-02-29 becomes 1900-03-01), or null when out of range.</summary>
    public static DateTime? ToDateTime(double serial, bool date1904 = false)
    {
        if (serial < 0 || serial >= MaxSerial(date1904) + 1)
        {
            return null;
        }

        var (year, month, day) = ToDate(serial, date1904);
        if (day == 0)
        {
            day = 1;
            month = 1;
        }

        if (month == 2 && day == 29 && year == 1900)
        {
            month = 3;
            day = 1;
        }

        var fraction = serial - Math.Floor(serial);
        return new DateTime(year, month, day).AddMilliseconds(Math.Round(fraction * 86_400_000));
    }

    public static double FromDateTime(DateTime value, bool date1904 = false)
    {
        var day = FromDate(value.Year, value.Month, value.Day, date1904) ?? 0;
        return day + value.TimeOfDay.TotalDays;
    }

    /// <summary>The weekday of a serial, 0 = Sunday (serial 1 = 1900-01-01 was a Sunday in Excel's calendar).</summary>
    public static int DayOfWeek(double serial, bool date1904 = false)
    {
        var days = (long)Math.Floor(serial);
        if (date1904)
        {
            days += 1462;
        }

        return (int)(((days - 1) % 7 + 7) % 7);
    }

    /// <summary>Hours, minutes, seconds and milliseconds of the time part (rounded to milliseconds like Excel).</summary>
    public static (int Hour, int Minute, int Second, int Millisecond) TimeOf(double serial)
    {
        var fraction = serial - Math.Floor(serial);
        var milliseconds = (long)Math.Round(fraction * 86_400_000);
        if (milliseconds >= 86_400_000)
        {
            milliseconds = 86_399_999;
        }

        return ((int)(milliseconds / 3_600_000), (int)(milliseconds / 60_000 % 60), (int)(milliseconds / 1000 % 60), (int)(milliseconds % 1000));
    }

    public static bool IsLeapYear(int year) => year == 1900 || DateTime.IsLeapYear(year);
}
