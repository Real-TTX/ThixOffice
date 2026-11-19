using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Functions;

/// <summary>
/// Day-count conventions of DAYS360, YEARFRAC and the securities functions: basis 0 US (NASD) 30/360, 1 actual/actual,
/// 2 actual/360, 3 actual/365, 4 European 30/360.
/// </summary>
internal static class DayCount
{
    public static double Days360(double start, double end, bool european, bool date1904)
    {
        var (y1, m1, d1) = DateSerial.ToDate(Math.Floor(start), date1904);
        var (y2, m2, d2) = DateSerial.ToDate(Math.Floor(end), date1904);
        if (european)
        {
            d1 = Math.Min(d1, 30);
            d2 = Math.Min(d2, 30);
        }
        else
        {
            var startLastOfFebruary = IsLastDayOfFebruary(y1, m1, d1);
            var endLastOfFebruary = IsLastDayOfFebruary(y2, m2, d2);
            if (startLastOfFebruary && endLastOfFebruary)
            {
                d2 = 30;
            }

            if (startLastOfFebruary)
            {
                d1 = 30;
            }

            if (d2 == 31 && d1 >= 30)
            {
                d2 = 30;
            }

            if (d1 == 31)
            {
                d1 = 30;
            }
        }

        return (y2 - y1) * 360.0 + (m2 - m1) * 30 + (d2 - d1);
    }

    public static double YearFraction(double start, double end, int basis, bool date1904)
    {
        start = Math.Floor(start);
        end = Math.Floor(end);
        if (start > end)
        {
            (start, end) = (end, start);
        }

        switch (basis)
        {
            case 0:
                return Days360(start, end, european: false, date1904) / 360;
            case 1:
                return ActualActual(start, end, date1904);
            case 2:
                return (end - start) / 360;
            case 3:
                return (end - start) / 365;
            case 4:
                return Days360(start, end, european: true, date1904) / 360;
            default:
                throw new CalcErrorException(CellError.Number);
        }
    }

    /// <summary>The days between two dates under a basis (the numerator of <see cref="YearFraction"/>).</summary>
    public static double Days(double start, double end, int basis, bool date1904) => basis switch
    {
        0 => Days360(start, end, european: false, date1904),
        4 => Days360(start, end, european: true, date1904),
        _ => Math.Floor(end) - Math.Floor(start),
    };

    /// <summary>The days of a year under a basis (360, 365 or the actual 365/366).</summary>
    public static double YearLength(int year, int basis) => basis switch
    {
        0 or 2 or 4 => 360,
        3 => 365,
        _ => DateTime.IsLeapYear(year) ? 366 : 365,
    };

    private static double ActualActual(double start, double end, bool date1904)
    {
        var (y1, m1, d1) = DateSerial.ToDate(start, date1904);
        var (y2, m2, d2) = DateSerial.ToDate(end, date1904);
        var days = end - start;
        var withinYear = y1 == y2 || (y2 == y1 + 1 && (m1 > m2 || (m1 == m2 && d1 >= d2)));
        if (withinYear)
        {
            double denominator;
            if (y1 == y2)
            {
                denominator = DateTime.IsLeapYear(y1) ? 366 : 365;
            }
            else
            {
                denominator = ContainsLeapDay(y1, m1, d1, y2, m2, d2) ? 366 : 365;
            }

            return days / denominator;
        }

        var years = y2 - y1 + 1;
        var total = 0.0;
        for (var year = y1; year <= y2; year++)
        {
            total += DateTime.IsLeapYear(year) ? 366 : 365;
        }

        return days / (total / years);
    }

    private static bool ContainsLeapDay(int y1, int m1, int d1, int y2, int m2, int d2)
    {
        if (DateTime.IsLeapYear(y1) && (m1 < 2 || (m1 == 2 && d1 <= 29)))
        {
            return true;
        }

        return DateTime.IsLeapYear(y2) && (m2 > 2 || (m2 == 2 && d2 == 29));
    }

    private static bool IsLastDayOfFebruary(int year, int month, int day) =>
        month == 2 && day == (DateSerial.IsLeapYear(year) ? 29 : 28);
}
