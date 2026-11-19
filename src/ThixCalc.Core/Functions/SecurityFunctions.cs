using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>
/// Securities (Finanzmathematik): the coupon schedule (ZINSTERMTAGVA, ZINSTERMTAGE, ZINSTERMTAGNZ, ZINSTERMNZ, ZINSTERMZAHL,
/// ZINSTERMVZ), KURS/RENDITE, KURSFÄLLIG/RENDITEFÄLL, DURATION/MDURATION, the odd first and last periods (UNREGER.*,
/// UNREGLE.*) and the French depreciation AMORDEGRK/AMORLINEARK – the formulas of Excel's function reference; the coupon
/// dates step back from the maturity in months, keeping the last day of a month when the maturity is one.
/// </summary>
internal static class SecurityFunctions
{
    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Financial;
        r.Add("COUPPCD", 3, 4, call => Result(Schedule(call).Previous));
        r.Add("COUPNCD", 3, 4, call => Result(Schedule(call).Next));
        r.Add("COUPNUM", 3, 4, call => Result(Schedule(call).Count));
        r.Add("COUPDAYBS", 3, 4, call => Result(Schedule(call).DaysBefore));
        r.Add("COUPDAYS", 3, 4, call => Result(Schedule(call).Days));
        r.Add("COUPDAYSNC", 3, 4, call => Result(Schedule(call).DaysAfter));

        r.Add("PRICE", 6, 7, call =>
        {
            var (settlement, maturity) = Dates(call);
            var rate = call.Number(2);
            var yield = call.Number(3);
            var redemption = call.Number(4);
            var bond = Bond.Of(settlement, maturity, Frequency(call, 5), Basis(call, 6), call.Context.Date1904);
            Require(rate >= 0 && yield >= 0 && redemption > 0);
            return Result(bond.Price(rate, yield, redemption));
        });
        r.Add("YIELD", 6, 7, call =>
        {
            var (settlement, maturity) = Dates(call);
            var rate = call.Number(2);
            var price = call.Number(3);
            var redemption = call.Number(4);
            var bond = Bond.Of(settlement, maturity, Frequency(call, 5), Basis(call, 6), call.Context.Date1904);
            Require(rate >= 0 && price > 0 && redemption > 0);
            return Result(SolveYield(yield => bond.Price(rate, yield, redemption), price, bond.Frequency));
        });
        r.Add("DURATION", 5, 6, call => Result(Duration(call)));
        r.Add("MDURATION", 5, 6, call => Result(Duration(call) / (1 + call.Number(3) / Frequency(call, 4))));

        r.Add("PRICEMAT", 5, 6, call =>
        {
            var (settlement, maturity) = Dates(call);
            var issue = Math.Floor(call.Number(2));
            var rate = call.Number(3);
            var yield = call.Number(4);
            var basis = Basis(call, 5);
            var date1904 = call.Context.Date1904;
            Require(rate >= 0 && yield >= 0 && issue < settlement);
            var issueToMaturity = DayCount.YearFraction(issue, maturity, basis, date1904);
            var settlementToMaturity = DayCount.YearFraction(settlement, maturity, basis, date1904);
            var issueToSettlement = DayCount.YearFraction(issue, settlement, basis, date1904);
            return Result((100 + issueToMaturity * rate * 100) / (1 + settlementToMaturity * yield) - issueToSettlement * rate * 100);
        });
        r.Add("YIELDMAT", 5, 6, call =>
        {
            var (settlement, maturity) = Dates(call);
            var issue = Math.Floor(call.Number(2));
            var rate = call.Number(3);
            var price = call.Number(4);
            var basis = Basis(call, 5);
            var date1904 = call.Context.Date1904;
            Require(rate >= 0 && price > 0 && issue < settlement);
            var issueToMaturity = DayCount.YearFraction(issue, maturity, basis, date1904);
            var settlementToMaturity = DayCount.YearFraction(settlement, maturity, basis, date1904);
            var issueToSettlement = DayCount.YearFraction(issue, settlement, basis, date1904);
            var paid = price / 100 + issueToSettlement * rate;
            return Result((1 + issueToMaturity * rate - paid) / paid / settlementToMaturity);
        });

        r.Add("ODDFPRICE", 8, 9, call =>
        {
            var odd = OddFirst(call);
            Require(call.Number(5) >= 0);
            return Result(odd.Price(call.Number(5)));
        });
        r.Add("ODDFYIELD", 8, 9, call =>
        {
            var odd = OddFirst(call);
            var price = call.Number(5);
            Require(price > 0);
            return Result(SolveYield(odd.Price, price, odd.Frequency));
        });
        r.Add("ODDLPRICE", 7, 8, call =>
        {
            var (fromLast, toMaturity, settlementToMaturity, rate, redemption, frequency) = OddLast(call);
            var yield = call.Number(4);
            Require(yield >= 0);
            var coupon = 100 * rate / frequency;
            return Result((redemption + toMaturity * coupon) / (settlementToMaturity * yield / frequency + 1) - fromLast * coupon);
        });
        r.Add("ODDLYIELD", 7, 8, call =>
        {
            var (fromLast, toMaturity, settlementToMaturity, rate, redemption, frequency) = OddLast(call);
            var price = call.Number(4);
            Require(price > 0);
            var coupon = 100 * rate / frequency;
            return Result(((redemption + toMaturity * coupon) / (price + fromLast * coupon) - 1) * frequency / settlementToMaturity);
        });

        r.Add("AMORLINC", 6, 7, call =>
        {
            var (cost, purchased, firstPeriod, salvage, period, rate, basis) = Depreciation(call);
            var yearly = cost * rate;
            var first = DayCount.YearFraction(purchased, firstPeriod, basis, call.Context.Date1904) * rate * cost;
            var fullPeriods = Math.Floor((cost - salvage - first) / yearly);
            if (period == 0)
            {
                return Result(first);
            }

            if (period <= fullPeriods)
            {
                return Result(yearly);
            }

            return Result(period == fullPeriods + 1 ? cost - salvage - yearly * fullPeriods - first : 0);
        });
        r.Add("AMORDEGRC", 6, 7, call =>
        {
            var (cost, purchased, firstPeriod, salvage, period, rate, basis) = Depreciation(call);
            var life = 1 / rate;
            var coefficient = life < 3 ? 1 : life < 5 ? 1.5 : life <= 6 ? 2 : 2.5;
            rate *= coefficient;
            var amount = Math.Round(DayCount.YearFraction(purchased, firstPeriod, basis, call.Context.Date1904) * rate * cost, MidpointRounding.AwayFromZero);
            cost -= amount;
            var rest = cost - salvage;
            for (var step = 0; step < period; step++)
            {
                amount = Math.Round(rate * cost, MidpointRounding.AwayFromZero);
                rest -= amount;
                if (rest < 0)
                {
                    return Result(period - step is 0 or 1 ? Math.Round(cost * 0.5, MidpointRounding.AwayFromZero) : 0);
                }

                cost -= amount;
            }

            return Result(amount);
        });
    }

    private static (double Settlement, double Maturity) Dates(FunctionCall call)
    {
        var settlement = Math.Floor(call.Number(0));
        var maturity = Math.Floor(call.Number(1));
        Require(settlement < maturity);
        return (settlement, maturity);
    }

    private static int Frequency(FunctionCall call, int index)
    {
        var frequency = (int)Coercion.Truncate(call.Number(index));
        Require(frequency is 1 or 2 or 4);
        return frequency;
    }

    private static int Basis(FunctionCall call, int index)
    {
        var basis = (int)Coercion.Truncate(call.Number(index, 0));
        Require(basis is >= 0 and <= 4);
        return basis;
    }

    private static Bond Schedule(FunctionCall call)
    {
        var (settlement, maturity) = Dates(call);
        return Bond.Of(settlement, maturity, Frequency(call, 2), Basis(call, 3), call.Context.Date1904);
    }

    private static double Duration(FunctionCall call)
    {
        var (settlement, maturity) = Dates(call);
        var coupon = call.Number(2);
        var yield = call.Number(3);
        var frequency = Frequency(call, 4);
        var basis = Basis(call, 5);
        Require(coupon >= 0 && yield >= 0);
        var bond = Bond.Of(settlement, maturity, frequency, basis, call.Context.Date1904);
        var count = bond.Count;
        var difference = bond.DaysAfter / bond.Days - 1;
        var payment = coupon * 100 / frequency;
        var factor = 1 + yield / frequency;
        double weighted = 0, present = 0;
        for (var period = 1; period < count; period++)
        {
            var discounted = payment / Math.Pow(factor, period + difference);
            weighted += (period + difference) * discounted;
            present += discounted;
        }

        var last = (payment + 100) / Math.Pow(factor, count + difference);
        weighted += (count + difference) * last;
        present += last;
        return weighted / present / frequency;
    }

    /// <summary>The yield at which <paramref name="price"/> of the yield is <paramref name="target"/> (prices fall as yields rise).</summary>
    private static double SolveYield(Func<double, double> price, double target, int frequency)
    {
        var low = -frequency * 0.999999;
        var high = 1.0;
        while (price(high) > target)
        {
            high *= 2;
            Require(high < 1e6);
        }

        for (var step = 0; step < 300 && high - low > 1e-15; step++)
        {
            var middle = (low + high) / 2;
            if (price(middle) > target)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (low + high) / 2;
    }

    private static (double FromLast, double ToMaturity, double SettlementToMaturity, double Rate, double Redemption, int Frequency) OddLast(FunctionCall call)
    {
        var (settlement, maturity) = Dates(call);
        var lastInterest = Math.Floor(call.Number(2));
        var rate = call.Number(3);
        var redemption = call.Number(5);
        var frequency = Frequency(call, 6);
        var basis = Basis(call, 7);
        var date1904 = call.Context.Date1904;
        Require(rate >= 0 && redemption > 0 && lastInterest < settlement);
        return (
            DayCount.YearFraction(lastInterest, settlement, basis, date1904) * frequency,
            DayCount.YearFraction(lastInterest, maturity, basis, date1904) * frequency,
            DayCount.YearFraction(settlement, maturity, basis, date1904) * frequency,
            rate,
            redemption,
            frequency);
    }

    private static OddFirstBond OddFirst(FunctionCall call)
    {
        var (settlement, maturity) = Dates(call);
        var issue = Math.Floor(call.Number(2));
        var firstCoupon = Math.Floor(call.Number(3));
        var rate = call.Number(4);
        var redemption = call.Number(6);
        var frequency = Frequency(call, 7);
        var basis = Basis(call, 8);
        Require(rate >= 0 && redemption > 0 && issue < settlement && settlement < firstCoupon && firstCoupon < maturity);
        var date1904 = call.Context.Date1904;
        var bond = Bond.Of(settlement, maturity, frequency, basis, date1904);
        return new OddFirstBond(
            Bond.DaysBetween(issue, settlement, basis, date1904),
            Bond.DaysBetween(settlement, firstCoupon, basis, date1904),
            Bond.DaysBetween(issue, firstCoupon, basis, date1904),
            bond.Days,
            bond.Count,
            rate,
            redemption,
            frequency);
    }

    private static (double Cost, double Purchased, double FirstPeriod, double Salvage, double Period, double Rate, int Basis) Depreciation(FunctionCall call)
    {
        var cost = call.Number(0);
        var purchased = Math.Floor(call.Number(1));
        var firstPeriod = Math.Floor(call.Number(2));
        var salvage = call.Number(3);
        var period = Math.Floor(call.Number(4));
        var rate = call.Number(5);
        var basis = Basis(call, 6);
        Require(cost > 0 && salvage >= 0 && salvage <= cost && period >= 0 && rate > 0 && purchased <= firstPeriod && basis != 2);
        return (cost, purchased, firstPeriod, salvage, period, rate, basis);
    }

    /// <summary>A bond with an odd (short) first period: Excel's ODDFPRICE formula.</summary>
    private sealed record OddFirstBond(double IssueToSettlement, double SettlementToFirst, double IssueToFirst, double Days, int Count, double Rate, double Redemption, int Frequency)
    {
        public double Price(double yield)
        {
            var factor = 1 + yield / Frequency;
            var scale = 100 * Rate / Frequency;
            var offset = SettlementToFirst / Days;
            var price = Redemption / Math.Pow(factor, Count - 1 + offset) + scale * IssueToFirst / Days / Math.Pow(factor, offset);
            for (var period = 1; period < Count; period++)
            {
                price += scale / Math.Pow(factor, period + offset);
            }

            return price - scale * IssueToSettlement / Days;
        }
    }

    /// <summary>The coupon schedule of a bond around its settlement date.</summary>
    private sealed record Bond(double Previous, double Next, int Count, double DaysBefore, double Days, double DaysAfter, int Frequency, int Basis)
    {
        public static Bond Of(double settlement, double maturity, int frequency, int basis, bool date1904)
        {
            var (maturityYear, maturityMonth, maturityDay) = DateSerial.ToDate(maturity, date1904);
            var (settlementYear, _, _) = DateSerial.ToDate(settlement, date1904);
            var endOfMonth = maturityDay == DateTime.DaysInMonth(maturityYear, maturityMonth);
            var step = 12 / frequency;

            // Months from the maturity: the last coupon date on or before the settlement.
            var months = (settlementYear - maturityYear) * 12;
            if (Serial(maturityYear, maturityMonth + months, maturityDay, endOfMonth, date1904) < settlement)
            {
                months += 12;
            }

            while (Serial(maturityYear, maturityMonth + months, maturityDay, endOfMonth, date1904) > settlement)
            {
                months -= step;
            }

            var previous = Serial(maturityYear, maturityMonth + months, maturityDay, endOfMonth, date1904);
            var next = Serial(maturityYear, maturityMonth + months + step, maturityDay, endOfMonth, date1904);
            var count = -months / step;
            var days = basis switch
            {
                1 => next - previous,
                3 => 365.0 / frequency,
                _ => 360.0 / frequency,
            };
            var before = DaysBetween(previous, settlement, basis, date1904);
            var after = basis is 0 or 4 ? days - before : next - settlement;
            return new Bond(previous, next, count, before, days, after, frequency, basis);
        }

        /// <summary>Excel's PRICE: the discounted coupons and redemption minus the accrued interest.</summary>
        public double Price(double rate, double yield, double redemption)
        {
            var factor = 1 + yield / Frequency;
            var offset = DaysAfter / Days;
            var coupon = 100 * rate / Frequency;
            var price = redemption / Math.Pow(factor, Count - 1 + offset) - coupon * DaysBefore / Days;
            for (var period = 0; period < Count; period++)
            {
                price += coupon / Math.Pow(factor, period + offset);
            }

            return price;
        }

        public static double DaysBetween(double from, double to, int basis, bool date1904) => basis switch
        {
            0 => DayCount.Days360(from, to, european: false, date1904),
            4 => DayCount.Days360(from, to, european: true, date1904),
            _ => to - from,
        };

        private static double Serial(int year, int month, int day, bool endOfMonth, bool date1904)
        {
            var total = year * 12 + (month - 1);
            var actualYear = Math.DivRem(total, 12, out var monthIndex);
            if (monthIndex < 0)
            {
                monthIndex += 12;
                actualYear--;
            }

            var length = DateTime.DaysInMonth(actualYear, monthIndex + 1);
            return DateSerial.FromDate(actualYear, monthIndex + 1, endOfMonth ? length : Math.Min(day, length), date1904) ?? throw new CalcErrorException(CellError.Number);
        }
    }
}
