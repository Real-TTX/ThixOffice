using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Financial functions (Finanzmathematik): annuities, cash flows, depreciation, interest rates, discounted securities.</summary>
internal static class FinancialFunctions
{
    private const int MaxIterations = 100;
    private const double Tolerance = 1e-10;

    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Financial;
        r.Add("PV", 3, 5, call => Result(PresentValue(call.Number(0), call.Number(1), call.Number(2), call.Number(3, 0), Type(call, 4))));
        r.Add("FV", 3, 5, call => Result(FutureValue(call.Number(0), call.Number(1), call.Number(2), call.Number(3, 0), Type(call, 4))));
        r.Add("PMT", 3, 5, call => Result(Payment(call.Number(0), call.Number(1), call.Number(2), call.Number(3, 0), Type(call, 4))));
        r.Add("NPER", 3, 5, call =>
        {
            var rate = call.Number(0);
            var payment = call.Number(1);
            var present = call.Number(2);
            var future = call.Number(3, 0);
            var type = Type(call, 4);
            if (rate == 0)
            {
                Require(payment != 0);
                return Result(-(present + future) / payment);
            }

            var numerator = payment * (1 + rate * type) - future * rate;
            var denominator = payment * (1 + rate * type) + present * rate;
            Require(numerator / denominator > 0);
            return Result(Math.Log(numerator / denominator) / Math.Log(1 + rate));
        });
        r.Add("IPMT", 4, 6, call => Result(InterestPayment(call.Number(0), call.Number(1), call.Number(2), call.Number(3), call.Number(4, 0), Type(call, 5))));
        r.Add("PPMT", 4, 6, call =>
        {
            var rate = call.Number(0);
            var period = call.Number(1);
            var periods = call.Number(2);
            var present = call.Number(3);
            var future = call.Number(4, 0);
            var type = Type(call, 5);
            return Result(Payment(rate, periods, present, future, type) - InterestPayment(rate, period, periods, present, future, type));
        });
        r.Add("RATE", 3, 6, call => Result(Rate(call.Number(0), call.Number(1), call.Number(2), call.Number(3, 0), Type(call, 4), call.Number(5, 0.1))));
        r.Add("NPV", 2, 255, call =>
        {
            var rate = call.Number(0);
            Require(rate != -1, CellError.DivisionByZero);
            var values = Aggregates.Numbers(call, 1);
            var sum = 0.0;
            for (var index = 0; index < values.Count; index++)
            {
                sum += values[index] / Math.Pow(1 + rate, index + 1);
            }

            return Result(sum);
        }, [ArgumentKind.Value, ArgumentKind.Any]);
        r.Add("IRR", 1, 2, call =>
        {
            var values = Aggregates.NumbersOf(call, call[0]);
            Require(values.Any(value => value > 0) && values.Any(value => value < 0));
            return Result(Solve(rate => NetPresentValue(rate, values, 0), call.Number(1, 0.1)));
        }, [ArgumentKind.Array, ArgumentKind.Value]);
        r.Add("MIRR", 3, 3, call =>
        {
            var values = Aggregates.NumbersOf(call, call[0]);
            var finance = call.Number(1);
            var reinvest = call.Number(2);
            Require(values.Any(value => value > 0) && values.Any(value => value < 0), CellError.DivisionByZero);
            var n = values.Count;
            var positive = values.Select((value, index) => value > 0 ? value / Math.Pow(1 + reinvest, index) : 0).Sum();
            var negative = values.Select((value, index) => value < 0 ? value / Math.Pow(1 + finance, index) : 0).Sum();
            return Result(Math.Pow(-positive * Math.Pow(1 + reinvest, n - 1) / negative, 1.0 / (n - 1)) - 1);
        }, [ArgumentKind.Array, ArgumentKind.Value, ArgumentKind.Value]);
        r.Add("XNPV", 3, 3, call =>
        {
            var rate = call.Number(0);
            var (values, dates) = Schedule(call, 1, 2);
            Require(rate > -1);
            return Result(DatedValue(rate, values, dates));
        }, [ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Array]);
        r.Add("XIRR", 2, 3, call =>
        {
            var (values, dates) = Schedule(call, 0, 1);
            Require(values.Any(value => value > 0) && values.Any(value => value < 0));
            return Result(Solve(rate => DatedValue(rate, values, dates), call.Number(2, 0.1)));
        }, [ArgumentKind.Array, ArgumentKind.Array, ArgumentKind.Value]);
        r.Add("SLN", 3, 3, call =>
        {
            var life = call.Number(2);
            Require(life != 0, CellError.DivisionByZero);
            return Result((call.Number(0) - call.Number(1)) / life);
        });
        r.Add("SYD", 4, 4, call =>
        {
            var cost = call.Number(0);
            var salvage = call.Number(1);
            var life = call.Number(2);
            var period = call.Number(3);
            Require(life > 0 && period > 0 && period <= life && salvage >= 0);
            return Result((cost - salvage) * (life - period + 1) * 2 / (life * (life + 1)));
        });
        r.Add("DB", 4, 5, DecliningBalance);
        r.Add("DDB", 4, 5, call =>
        {
            var cost = call.Number(0);
            var salvage = call.Number(1);
            var life = call.Number(2);
            var period = call.Number(3);
            var factor = call.Number(4, 2);
            Require(cost >= 0 && salvage >= 0 && life > 0 && period > 0 && period <= life && factor > 0);
            return Result(DoubleDeclining(cost, salvage, life, period, factor));
        });
        r.Add("VDB", 5, 7, VariableDeclining);
        r.Add("CUMIPMT", 6, 6, call => Cumulative(call, interest: true));
        r.Add("CUMPRINC", 6, 6, call => Cumulative(call, interest: false));
        r.Add("EFFECT", 2, 2, call =>
        {
            var nominal = call.Number(0);
            var periods = Coercion.Truncate(call.Number(1));
            Require(nominal > 0 && periods >= 1);
            return Result(Math.Pow(1 + nominal / periods, periods) - 1);
        });
        r.Add("NOMINAL", 2, 2, call =>
        {
            var effective = call.Number(0);
            var periods = Coercion.Truncate(call.Number(1));
            Require(effective > 0 && periods >= 1);
            return Result(periods * (Math.Pow(1 + effective, 1 / periods) - 1));
        });
        r.Add("FVSCHEDULE", 2, 2, call =>
        {
            var value = call.Number(0);
            foreach (var rate in call.Values(1))
            {
                if (rate.IsError)
                {
                    return rate;
                }

                value *= 1 + (rate.IsBlank ? 0 : Coercion.ToNumber(rate, call.Context.Locale, false));
            }

            return Result(value);
        }, [ArgumentKind.Value, ArgumentKind.Array]);
        r.Add("PDURATION", 3, 3, call =>
        {
            var rate = call.Number(0);
            var present = call.Number(1);
            var future = call.Number(2);
            Require(rate > 0 && present > 0 && future > 0);
            return Result((Math.Log(future) - Math.Log(present)) / Math.Log(1 + rate));
        });
        r.Add("RRI", 3, 3, call =>
        {
            var periods = call.Number(0);
            var present = call.Number(1);
            var future = call.Number(2);
            Require(periods > 0 && present != 0);
            return Result(Math.Pow(future / present, 1 / periods) - 1);
        });
        r.Add("ISPMT", 4, 4, call =>
        {
            var periods = call.Number(2);
            Require(periods != 0, CellError.DivisionByZero);
            return Result(call.Number(3) * call.Number(0) * (call.Number(1) / periods - 1));
        });
        r.Add("DOLLARDE", 2, 2, call => Result(DollarConversion(call.Number(0), call.Number(1), toDecimal: true)));
        r.Add("DOLLARFR", 2, 2, call => Result(DollarConversion(call.Number(0), call.Number(1), toDecimal: false)));
        r.Add("DISC", 4, 5, call =>
        {
            var (fraction, price, redemption) = Security(call);
            return Result((redemption - price) / redemption / fraction);
        });
        r.Add("INTRATE", 4, 5, call =>
        {
            var (fraction, investment, redemption) = Security(call);
            return Result((redemption - investment) / investment / fraction);
        });
        r.Add("RECEIVED", 4, 5, call =>
        {
            var (fraction, investment, discount) = Security(call);
            var denominator = 1 - discount * fraction;
            Require(denominator > 0);
            return Result(investment / denominator);
        });
        r.Add("PRICEDISC", 4, 5, call =>
        {
            var (fraction, discount, redemption) = Security(call);
            return Result(redemption - discount * redemption * fraction);
        });
        r.Add("YIELDDISC", 4, 5, call =>
        {
            var (fraction, price, redemption) = Security(call);
            return Result((redemption - price) / price / fraction);
        });
        r.Add("TBILLPRICE", 3, 3, call =>
        {
            var days = TreasuryDays(call);
            var discount = call.Number(2);
            Require(discount > 0);
            var price = 100 * (1 - discount * days / 360);
            Require(price > 0);
            return Result(price);
        });
        r.Add("TBILLYIELD", 3, 3, call =>
        {
            var days = TreasuryDays(call);
            var price = call.Number(2);
            Require(price > 0);
            return Result((100 - price) / price * 360 / days);
        });
        r.Add("TBILLEQ", 3, 3, call =>
        {
            var days = TreasuryDays(call);
            var discount = call.Number(2);
            Require(discount > 0);
            if (days <= 182)
            {
                return Result(365 * discount / (360 - discount * days));
            }

            var price = 100 * (1 - discount * days / 360);
            Require(price > 0);
            var years = days / 365.0;
            return Result((-years + Math.Sqrt(years * years - (2 * years - 1) * (1 - 100 / price))) / (years - 0.5));
        });
        r.Add("ACCRINT", 6, 8, call =>
        {
            var issue = Math.Floor(call.Number(0));
            var settlement = Math.Floor(call.Number(2));
            var rate = call.Number(3);
            var par = call.Number(4);
            var frequency = Coercion.Truncate(call.Number(5));
            var basis = Basis(call, 6);
            Require(rate > 0 && par > 0 && frequency is 1 or 2 or 4 && issue < settlement);
            return Result(par * rate * DayCount.YearFraction(issue, settlement, basis, call.Context.Date1904));
        });
        r.Add("ACCRINTM", 3, 5, call =>
        {
            var issue = Math.Floor(call.Number(0));
            var settlement = Math.Floor(call.Number(1));
            var rate = call.Number(2);
            var par = call.Number(3, 1000);
            var basis = Basis(call, 4);
            Require(rate > 0 && par > 0 && issue < settlement);
            return Result(par * rate * DayCount.YearFraction(issue, settlement, basis, call.Context.Date1904));
        });
    }

    private static double Type(FunctionCall call, int index) => call.Number(index, 0) != 0 ? 1 : 0;

    private static int Basis(FunctionCall call, int index)
    {
        var basis = (int)Coercion.Truncate(call.Number(index, 0));
        Require(basis is >= 0 and <= 4);
        return basis;
    }

    internal static double PresentValue(double rate, double periods, double payment, double future, double type)
    {
        if (rate == 0)
        {
            return -(future + payment * periods);
        }

        var growth = Math.Pow(1 + rate, periods);
        return -(future + payment * (1 + rate * type) * (growth - 1) / rate) / growth;
    }

    internal static double FutureValue(double rate, double periods, double payment, double present, double type)
    {
        if (rate == 0)
        {
            return -(present + payment * periods);
        }

        var growth = Math.Pow(1 + rate, periods);
        return -(present * growth + payment * (1 + rate * type) * (growth - 1) / rate);
    }

    internal static double Payment(double rate, double periods, double present, double future, double type)
    {
        Require(periods != 0);
        if (rate == 0)
        {
            return -(present + future) / periods;
        }

        var growth = Math.Pow(1 + rate, periods);
        return -(present * growth + future) * rate / ((1 + rate * type) * (growth - 1));
    }

    private static double InterestPayment(double rate, double period, double periods, double present, double future, double type)
    {
        Require(period >= 1 && period <= periods);
        var payment = Payment(rate, periods, present, future, type);
        if (type == 1 && period == 1)
        {
            return 0;
        }

        var interest = FutureValue(rate, period - 1, payment, present, type) * rate;
        return type == 1 ? interest / (1 + rate) : interest;
    }

    private static double Rate(double periods, double payment, double present, double future, double type, double guess)
    {
        double Value(double rate)
        {
            if (Math.Abs(rate) < 1e-12)
            {
                return present + payment * periods + future;
            }

            var growth = Math.Pow(1 + rate, periods);
            return present * growth + payment * (1 + rate * type) * (growth - 1) / rate + future;
        }

        return Solve(Value, guess);
    }

    /// <summary>Newton's method with a numeric derivative from <paramref name="guess"/>; #NUM! without convergence.</summary>
    private static double Solve(Func<double, double> function, double guess)
    {
        var x = guess;
        for (var iteration = 0; iteration < MaxIterations; iteration++)
        {
            var value = function(x);
            if (double.IsNaN(value))
            {
                break;
            }

            if (Math.Abs(value) < Tolerance)
            {
                return x;
            }

            var step = Math.Max(Math.Abs(x) * 1e-7, 1e-10);
            var derivative = (function(x + step) - function(x - step)) / (2 * step);
            if (derivative == 0 || double.IsNaN(derivative))
            {
                break;
            }

            var next = x - value / derivative;
            if (next <= -1)
            {
                next = (x - 1) / 2;
            }

            if (Math.Abs(next - x) < Tolerance * Math.Max(1, Math.Abs(x)))
            {
                return next;
            }

            x = next;
        }

        throw new CalcErrorException(CellError.Number);
    }

    private static double NetPresentValue(double rate, IReadOnlyList<double> values, int firstExponent)
    {
        var sum = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            sum += values[index] / Math.Pow(1 + rate, index + firstExponent);
        }

        return sum;
    }

    private static (List<double> Values, List<double> Dates) Schedule(FunctionCall call, int valuesIndex, int datesIndex)
    {
        var values = call.Array(valuesIndex);
        var dates = call.Array(datesIndex);
        Require(values.Count == dates.Count);
        var numbers = new List<double>();
        var days = new List<double>();
        for (var index = 0; index < values.Count; index++)
        {
            var value = values.Values[index];
            var date = dates.Values[index];
            if (value.IsError)
            {
                throw new CalcErrorException(value.Error);
            }

            Require(value.IsNumber && date.IsNumber, CellError.Value);
            Require(date.Number >= 0 && Math.Floor(date.Number) >= Math.Floor(dates.Values[0].Number));
            numbers.Add(value.Number);
            days.Add(Math.Floor(date.Number));
        }

        return (numbers, days);
    }

    private static double DatedValue(double rate, List<double> values, List<double> dates)
    {
        var sum = 0.0;
        for (var index = 0; index < values.Count; index++)
        {
            sum += values[index] / Math.Pow(1 + rate, (dates[index] - dates[0]) / 365);
        }

        return sum;
    }

    private static Operand DecliningBalance(FunctionCall call)
    {
        var cost = call.Number(0);
        var salvage = call.Number(1);
        var life = call.Number(2);
        var period = Coercion.Truncate(call.Number(3));
        var month = Coercion.Truncate(call.Number(4, 12));
        Require(cost >= 0 && salvage >= 0 && life > 0 && period >= 1 && month is >= 1 and <= 12 && period <= life + (month < 12 ? 1 : 0));
        if (cost == 0)
        {
            return 0;
        }

        var rate = Math.Round(1 - Math.Pow(salvage / cost, 1 / life), 3);
        var total = cost * rate * month / 12;
        if (period == 1)
        {
            return Result(total);
        }

        var depreciation = 0.0;
        for (var current = 2; current <= period; current++)
        {
            depreciation = current == life + 1 ? (cost - total) * rate * (12 - month) / 12 : (cost - total) * rate;
            total += depreciation;
        }

        return Result(depreciation);
    }

    private static double DoubleDeclining(double cost, double salvage, double life, double period, double factor)
    {
        var book = cost;
        var depreciation = 0.0;
        for (var current = 1; current <= Math.Ceiling(period); current++)
        {
            depreciation = Math.Min(book * factor / life, Math.Max(0, book - salvage));
            book -= depreciation;
        }

        return depreciation;
    }

    private static Operand VariableDeclining(FunctionCall call)
    {
        var cost = call.Number(0);
        var salvage = call.Number(1);
        var life = call.Number(2);
        var start = call.Number(3);
        var end = call.Number(4);
        var factor = call.Number(5, 2);
        var noSwitch = call.Boolean(6, false);
        Require(cost >= 0 && salvage >= 0 && life > 0 && start >= 0 && end >= start && end <= life && factor > 0);

        // Depreciate whole periods, switching to straight line when that is larger, then take the slice [start, end].
        var book = cost;
        var total = 0.0;
        var switched = false;
        var periods = (int)Math.Ceiling(life);
        var schedule = new double[periods];
        for (var period = 0; period < periods; period++)
        {
            var declining = Math.Min(book * factor / life, Math.Max(0, book - salvage));
            var straight = (book - salvage) / Math.Max(life - period, 1);
            double depreciation;
            if (!noSwitch && (switched || straight > declining))
            {
                switched = true;
                depreciation = straight;
            }
            else
            {
                depreciation = declining;
            }

            schedule[period] = Math.Max(0, depreciation);
            book -= schedule[period];
        }

        for (var period = 0; period < periods; period++)
        {
            var overlap = Math.Min(end, period + 1) - Math.Max(start, period);
            if (overlap > 0)
            {
                total += schedule[period] * overlap;
            }
        }

        return Result(total);
    }

    private static Operand Cumulative(FunctionCall call, bool interest)
    {
        var rate = call.Number(0);
        var periods = Coercion.Truncate(call.Number(1));
        var present = call.Number(2);
        var start = Coercion.Truncate(call.Number(3));
        var end = Coercion.Truncate(call.Number(4));
        var type = call.Number(5);
        Require(rate > 0 && periods > 0 && present > 0 && start >= 1 && end >= start && end <= periods && type is 0 or 1);
        var sum = 0.0;
        var payment = Payment(rate, periods, present, 0, type);
        for (var period = start; period <= end; period++)
        {
            var interestPart = InterestPayment(rate, period, periods, present, 0, type);
            sum += interest ? interestPart : payment - interestPart;
        }

        return Result(sum);
    }

    private static double DollarConversion(double value, double fraction, bool toDecimal)
    {
        fraction = Math.Truncate(fraction);
        Require(fraction >= 0);
        Require(fraction > 0, CellError.DivisionByZero);
        var whole = Math.Truncate(value);
        var rest = value - whole;
        var digits = Math.Pow(10, Math.Ceiling(Math.Log10(fraction)));
        return toDecimal ? whole + rest * digits / fraction : whole + rest * fraction / digits;
    }

    /// <summary>settlement, maturity, a value, a value, basis → (year fraction, value 1, value 2).</summary>
    private static (double Fraction, double First, double Second) Security(FunctionCall call)
    {
        var settlement = Math.Floor(call.Number(0));
        var maturity = Math.Floor(call.Number(1));
        var first = call.Number(2);
        var second = call.Number(3);
        var basis = Basis(call, 4);
        Require(settlement < maturity && first > 0 && second > 0);
        return (DayCount.YearFraction(settlement, maturity, basis, call.Context.Date1904), first, second);
    }

    private static double TreasuryDays(FunctionCall call)
    {
        var settlement = Math.Floor(call.Number(0));
        var maturity = Math.Floor(call.Number(1));
        var days = maturity - settlement;
        Require(days > 0 && days <= 366);
        return days;
    }
}
