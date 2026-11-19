using System.Globalization;
using System.Numerics;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Engineering functions (Technisch): number bases, bit operations, erf, CONVERT, complex numbers.</summary>
internal static class EngineeringFunctions
{
    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Engineering;
        foreach (var (from, fromRadix, fromBits) in new[] { ("BIN", 2, 10), ("OCT", 8, 30), ("DEC", 10, 0), ("HEX", 16, 40) })
        {
            foreach (var (to, toRadix, toBits) in new[] { ("BIN", 2, 10), ("OCT", 8, 30), ("DEC", 10, 0), ("HEX", 16, 40) })
            {
                if (from == to)
                {
                    continue;
                }

                var name = $"{from}2{to}";
                var (sourceRadix, sourceBits, targetRadix, targetBits) = (fromRadix, fromBits, toRadix, toBits);
                r.Add(name, 1, to == "DEC" ? 1 : 2, call =>
                {
                    var value = sourceRadix == 10 ? Coercion.Truncate(call.Number(0)) : Parse(call, sourceRadix, sourceBits);
                    if (targetRadix == 10)
                    {
                        return value;
                    }

                    return Format(value, targetRadix, targetBits, call.Has(1) ? call.Number(1) : null);
                });
            }
        }

        r.Add("BITAND", 2, 2, call => Bits(call, (a, b) => a & b));
        r.Add("BITOR", 2, 2, call => Bits(call, (a, b) => a | b));
        r.Add("BITXOR", 2, 2, call => Bits(call, (a, b) => a ^ b));
        r.Add("BITLSHIFT", 2, 2, call => Shift(call, left: true));
        r.Add("BITRSHIFT", 2, 2, call => Shift(call, left: false));
        r.Add("DELTA", 1, 2, call => call.Number(0) == call.Number(1, 0) ? 1 : 0);
        r.Add("GESTEP", 1, 2, call => call.Number(0) >= call.Number(1, 0) ? 1 : 0);
        r.Add("ERF", 1, 2, call => Result(call.Has(1)
            ? SpecialFunctions.Erf(call.Number(1)) - SpecialFunctions.Erf(call.Number(0))
            : SpecialFunctions.Erf(call.Number(0))));
        r.Number("ERF.PRECISE", SpecialFunctions.Erf);
        r.Number("ERFC", SpecialFunctions.Erfc);
        r.Alias("ERFC.PRECISE", "ERFC");
        r.Add("CONVERT", 3, 3, call => Result(Units.Convert(call.Number(0), call.Text(1), call.Text(2))));
        r.Add("COMPLEX", 2, 3, call =>
        {
            var suffix = call.Has(2) ? call.Text(2) : "i";
            Require(suffix is "i" or "j", CellError.Value);
            return FormatComplex(new Complex(call.Number(0), call.Number(1)), suffix);
        });
        r.Add("IMREAL", 1, 1, call => Result(ParseComplex(call, 0).Real));
        r.Add("IMAGINARY", 1, 1, call => Result(ParseComplex(call, 0).Imaginary));
        r.Add("IMABS", 1, 1, call => Result(Complex.Abs(ParseComplex(call, 0))));
        r.Add("IMARGUMENT", 1, 1, call =>
        {
            var value = ParseComplex(call, 0);
            Require(value != Complex.Zero, CellError.DivisionByZero);
            return Result(value.Phase);
        });
        r.Add("IMCONJUGATE", 1, 1, call => FormatComplex(Complex.Conjugate(ParseComplex(call, 0))));
        r.Add("IMSUM", 1, 255, call => FormatComplex(ComplexValues(call).Aggregate(Complex.Zero, (sum, value) => sum + value)), [ArgumentKind.Any]);
        r.Add("IMPRODUCT", 1, 255, call => FormatComplex(ComplexValues(call).Aggregate(Complex.One, (product, value) => product * value)), [ArgumentKind.Any]);
        r.Add("IMSUB", 2, 2, call => FormatComplex(ParseComplex(call, 0) - ParseComplex(call, 1)));
        r.Add("IMDIV", 2, 2, call =>
        {
            var divisor = ParseComplex(call, 1);
            Require(divisor != Complex.Zero);
            return FormatComplex(ParseComplex(call, 0) / divisor);
        });
        r.Add("IMPOWER", 2, 2, call =>
        {
            var value = ParseComplex(call, 0);
            var power = call.Number(1);
            if (power == Math.Floor(power) && Math.Abs(power) <= 64)
            {
                // Whole powers by multiplication stay exact for whole numbers ((2+3i)^3 = -46+9i).
                var result = Complex.One;
                for (var step = 0; step < Math.Abs(power); step++)
                {
                    result *= value;
                }

                return FormatComplex(power < 0 ? Complex.One / result : result);
            }

            return FormatComplex(Complex.Pow(value, power));
        });
        r.Add("IMSQRT", 1, 1, call => FormatComplex(Complex.Sqrt(ParseComplex(call, 0))));
        r.Add("IMEXP", 1, 1, call => FormatComplex(Complex.Exp(ParseComplex(call, 0))));
        r.Add("IMLN", 1, 1, call => FormatComplex(Complex.Log(NonZero(ParseComplex(call, 0)))));
        r.Add("IMLOG10", 1, 1, call => FormatComplex(Complex.Log10(NonZero(ParseComplex(call, 0)))));
        r.Add("IMLOG2", 1, 1, call => FormatComplex(Complex.Log(NonZero(ParseComplex(call, 0))) / Math.Log(2)));
        r.Add("IMSIN", 1, 1, call => FormatComplex(Complex.Sin(ParseComplex(call, 0))));
        r.Add("IMCOS", 1, 1, call => FormatComplex(Complex.Cos(ParseComplex(call, 0))));
        r.Add("IMTAN", 1, 1, call => FormatComplex(Complex.Tan(ParseComplex(call, 0))));
        r.Add("IMCOT", 1, 1, call => FormatComplex(Complex.One / NonZero(Complex.Tan(ParseComplex(call, 0)))));
        r.Add("IMSEC", 1, 1, call => FormatComplex(Complex.One / NonZero(Complex.Cos(ParseComplex(call, 0)))));
        r.Add("IMCSC", 1, 1, call => FormatComplex(Complex.One / NonZero(Complex.Sin(ParseComplex(call, 0)))));
        r.Add("IMSINH", 1, 1, call => FormatComplex(Complex.Sinh(ParseComplex(call, 0))));
        r.Add("IMCOSH", 1, 1, call => FormatComplex(Complex.Cosh(ParseComplex(call, 0))));
        r.Add("IMSECH", 1, 1, call => FormatComplex(Complex.One / NonZero(Complex.Cosh(ParseComplex(call, 0)))));
        r.Add("IMCSCH", 1, 1, call => FormatComplex(Complex.One / NonZero(Complex.Sinh(ParseComplex(call, 0)))));

        // Bessel functions of whole order n ≥ 0.
        r.Add("BESSELJ", 2, 2, call => Result(Bessel.J(BesselOrder(call), call.Number(0))));
        r.Add("BESSELI", 2, 2, call => Result(Bessel.I(BesselOrder(call), call.Number(0))));
        r.Add("BESSELK", 2, 2, call => Result(Bessel.K(BesselOrder(call), Positive(call.Number(0)))));
        r.Add("BESSELY", 2, 2, call => Result(Bessel.Y(BesselOrder(call), Positive(call.Number(0)))));
    }

    private static int BesselOrder(FunctionCall call)
    {
        var order = Math.Truncate(call.Number(1));
        Require(order >= 0 && order <= 1000);
        return (int)order;
    }

    private static double Positive(double x)
    {
        Require(x > 0);
        return x;
    }

    // ---------------------------------------------------------------- number bases

    private static double Parse(FunctionCall call, int radix, int bits)
    {
        var value = call.Value(0);
        var text = value.IsNumber ? ((long)value.Number).ToString(CultureInfo.InvariantCulture) : call.Text(0).Trim();
        Require(text.Length <= 10);
        long number = 0;
        foreach (var character in text.ToUpperInvariant())
        {
            var digit = char.IsAsciiDigit(character) ? character - '0' : character is >= 'A' and <= 'F' ? character - 'A' + 10 : 99;
            Require(digit < radix);
            number = number * radix + digit;
        }

        if (text.Length == 10 && number >= 1L << (bits - 1))
        {
            number -= 1L << bits;
        }

        return number;
    }

    private static string Format(double value, int radix, int bits, double? places)
    {
        var limit = 1L << (bits - 1);
        Require(value >= -limit && value < limit);
        var number = (long)value;
        string text;
        if (number < 0)
        {
            text = MathFunctions.ToBase(number + (1L << bits), radix);
            return text.PadLeft(10, radix == 16 ? 'F' : radix == 8 ? '7' : '1');
        }

        text = MathFunctions.ToBase(number, radix);
        if (places is { } count)
        {
            var length = Coercion.Truncate(count);
            Require(length > 0 && length <= 10 && length >= text.Length);
            text = text.PadLeft((int)length, '0');
        }

        return text;
    }

    private static Operand Bits(FunctionCall call, Func<long, long, long> operation)
    {
        var a = BitOperand(call.Number(0));
        var b = BitOperand(call.Number(1));
        return operation(a, b);
    }

    private static long BitOperand(double value)
    {
        Require(value >= 0 && value < 281474976710656 && value == Math.Floor(value));
        return (long)value;
    }

    private static Operand Shift(FunctionCall call, bool left)
    {
        var number = BitOperand(call.Number(0));
        var amount = (int)Coercion.Truncate(call.Number(1));
        Require(Math.Abs(amount) <= 53);
        if (!left)
        {
            amount = -amount;
        }

        var result = amount >= 0 ? (double)number * Math.Pow(2, amount) : number >> -amount;
        Require(result < 281474976710656);
        return result;
    }

    // ---------------------------------------------------------------- complex numbers

    private static Complex NonZero(Complex value)
    {
        Require(value != Complex.Zero);
        return value;
    }

    private static IEnumerable<Complex> ComplexValues(FunctionCall call)
    {
        for (var index = 0; index < call.Count; index++)
        {
            foreach (var value in call.Values(index))
            {
                if (value.IsError)
                {
                    throw new CalcErrorException(value.Error);
                }

                if (!value.IsBlank)
                {
                    yield return ParseComplexText(Coercion.ToText(value, Globalization.CalcLocale.Invariant));
                }
            }
        }
    }

    private static Complex ParseComplex(FunctionCall call, int index)
    {
        var value = call.ValueOrThrow(index);
        return value.IsNumber ? new Complex(value.Number, 0) : ParseComplexText(call.Text(index));
    }

    /// <summary>"3+4i", "-2.5j", "i", "5", "1E-3-2i".</summary>
    internal static Complex ParseComplexText(string text)
    {
        text = text.Trim();
        if (text.Length == 0)
        {
            return Complex.Zero;
        }

        if (text[^1] is not ('i' or 'j'))
        {
            Require(double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real), CellError.Number);
            return new Complex(real, 0);
        }

        var body = text[..^1];
        // The split between real and imaginary part: the last + or − not part of an exponent.
        var split = -1;
        for (var index = body.Length - 1; index > 0; index--)
        {
            if (body[index] is '+' or '-' && body[index - 1] is not ('E' or 'e'))
            {
                split = index;
                break;
            }
        }

        var realText = split > 0 ? body[..split] : "0";
        var imaginaryText = split > 0 ? body[split..] : body;
        imaginaryText = imaginaryText switch
        {
            "" or "+" => "1",
            "-" => "-1",
            _ => imaginaryText,
        };
        var validReal = double.TryParse(realText, NumberStyles.Float, CultureInfo.InvariantCulture, out var realPart);
        var validImaginary = double.TryParse(imaginaryText, NumberStyles.Float, CultureInfo.InvariantCulture, out var imaginaryPart);
        Require(validReal && validImaginary, CellError.Number);
        return new Complex(realPart, imaginaryPart);
    }

    internal static string FormatComplex(Complex value, string suffix = "i")
    {
        static string Number(double number) => Globalization.NumberText.RoundToSignificant(number, 15).ToString("G15", CultureInfo.InvariantCulture);

        var real = Math.Abs(value.Real) < 1e-15 ? 0 : value.Real;
        var imaginary = Math.Abs(value.Imaginary) < 1e-15 ? 0 : value.Imaginary;
        if (imaginary == 0)
        {
            return Number(real);
        }

        var imaginaryText = imaginary == 1 ? string.Empty : imaginary == -1 ? "-" : Number(imaginary);
        if (real == 0)
        {
            return imaginaryText + suffix;
        }

        return Number(real) + (imaginary > 0 ? "+" : string.Empty) + imaginaryText + suffix;
    }

    /// <summary>The units of CONVERT: factors to a base unit per group, metric and binary prefixes, temperatures.</summary>
    private static class Units
    {
        private static readonly Dictionary<string, (string Group, double Factor, bool Prefixable)> Table = Build();

        private static readonly Dictionary<string, double> Prefixes = new(StringComparer.Ordinal)
        {
            ["Y"] = 1e24, ["Z"] = 1e21, ["E"] = 1e18, ["P"] = 1e15, ["T"] = 1e12, ["G"] = 1e9, ["M"] = 1e6, ["k"] = 1e3, ["h"] = 1e2,
            ["da"] = 1e1, ["e"] = 1e1, ["d"] = 1e-1, ["c"] = 1e-2, ["m"] = 1e-3, ["u"] = 1e-6, ["n"] = 1e-9, ["p"] = 1e-12, ["f"] = 1e-15,
            ["a"] = 1e-18, ["z"] = 1e-21, ["y"] = 1e-24, ["ki"] = 1024, ["Mi"] = 1048576, ["Gi"] = 1073741824, ["Ti"] = 1099511627776,
            ["Pi"] = 1125899906842624, ["Ei"] = 1152921504606846976,
        };

        private static Dictionary<string, (string, double, bool)> Build()
        {
            var table = new Dictionary<string, (string, double, bool)>(StringComparer.Ordinal);
            void Add(string group, double factor, bool prefixable, params string[] names)
            {
                foreach (var name in names)
                {
                    table[name] = (group, factor, prefixable);
                }
            }

            Add("mass", 1, true, "g");
            Add("mass", 14593.9029372064, false, "sg");
            Add("mass", 453.59237, false, "lbm");
            Add("mass", 1.66053906660e-24, true, "u");
            Add("mass", 28.349523125, false, "ozm");
            Add("mass", 0.06479891, false, "grain");
            Add("mass", 45359.237, false, "cwt", "shweight");
            Add("mass", 50802.34544, false, "uk_cwt", "lcwt", "hweight");
            Add("mass", 6350.29318, false, "stone");
            Add("mass", 907184.74, false, "ton");
            Add("mass", 1016046.9088, false, "uk_ton", "LTON", "brton");
            Add("distance", 1, true, "m");
            Add("distance", 1609.344, false, "mi");
            Add("distance", 1852, false, "Nmi");
            Add("distance", 0.0254, false, "in");
            Add("distance", 0.3048, false, "ft");
            Add("distance", 0.9144, false, "yd");
            Add("distance", 1e-10, true, "ang");
            Add("distance", 1.143, false, "ell");
            Add("distance", 9.4607304725808e15, true, "ly");
            Add("distance", 3.08567758149137e16, true, "parsec", "pc");
            Add("distance", 0.0254 / 72, false, "Picapt", "Pica");
            Add("distance", 0.0254 / 6, false, "pica");
            Add("distance", 1609.34721869444, false, "survey_mi");
            Add("time", 365.25 * 86400, true, "yr");
            Add("time", 86400, true, "day", "d");
            Add("time", 3600, true, "hr");
            Add("time", 60, true, "mn", "min");
            Add("time", 1, true, "sec", "s");
            Add("pressure", 1, true, "Pa", "p");
            Add("pressure", 101325, true, "atm", "at");
            Add("pressure", 133.322368421053, true, "mmHg");
            Add("pressure", 6894.75729316836, true, "psi");
            Add("pressure", 133.322368421053, true, "Torr");
            Add("force", 1, true, "N");
            Add("force", 1e-5, true, "dyn", "dy");
            Add("force", 4.4482216152605, false, "lbf");
            Add("force", 0.00980665, true, "pond");
            Add("energy", 1, true, "J");
            Add("energy", 1e-7, true, "e");
            Add("energy", 4.184, true, "c");
            Add("energy", 4.1868, true, "cal");
            Add("energy", 1.602176634e-19, true, "eV", "ev");
            Add("energy", 2684519.53769617, false, "HPh", "hh");
            Add("energy", 3600, true, "Wh", "wh");
            Add("energy", 1.3558179483314, false, "flb");
            Add("energy", 1055.05585262, false, "BTU", "btu");
            Add("power", 745.69987158227, false, "HP", "h");
            Add("power", 735.49875, false, "PS");
            Add("power", 1, true, "W", "w");
            Add("magnetism", 1, true, "T");
            Add("magnetism", 1e-4, true, "ga");
            Add("volume", 1, true, "l", "L", "lt");
            Add("volume", 0.00492892159375, false, "tsp");
            Add("volume", 0.01478676478125, false, "tbs");
            Add("volume", 0.0295735295625, false, "oz");
            Add("volume", 0.2365882365, false, "cup");
            Add("volume", 0.473176473, false, "pt", "us_pt");
            Add("volume", 0.56826125, false, "uk_pt");
            Add("volume", 0.946352946, false, "qt");
            Add("volume", 1.1365225, false, "uk_qt");
            Add("volume", 3.785411784, false, "gal");
            Add("volume", 4.54609, false, "uk_gal");
            Add("volume", 1000, true, "m3", "m^3");
            Add("volume", 764.554857984, false, "yd3", "yd^3");
            Add("volume", 28.316846592, false, "ft3", "ft^3");
            Add("volume", 0.016387064, false, "in3", "in^3");
            Add("volume", 158.987294928, false, "barrel");
            Add("volume", 35.23907016688, false, "bushel");
            Add("area", 1, true, "m2", "m^2");
            Add("area", 2589988.110336, false, "mi2", "mi^2");
            Add("area", 0.00064516, false, "in2", "in^2");
            Add("area", 0.09290304, false, "ft2", "ft^2");
            Add("area", 0.83612736, false, "yd2", "yd^2");
            Add("area", 100, false, "ar");
            Add("area", 2500, false, "Morgen");
            Add("area", 4046.8564224, false, "uk_acre");
            Add("area", 4046.87260987425, false, "us_acre");
            Add("area", 10000, false, "ha");
            Add("information", 1, true, "bit");
            Add("information", 8, true, "byte");
            Add("speed", 1, true, "m/s", "m/sec");
            Add("speed", 1.0 / 3600, true, "m/h", "m/hr");
            Add("speed", 0.44704, false, "mph");
            Add("speed", 0.514444444444444, false, "kn");
            Add("speed", 0.514773333333333, false, "admkn");
            foreach (var temperature in new[] { "C", "cel", "F", "fah", "K", "kel", "Rank", "Reau" })
            {
                Add("temperature", 1, temperature is "K" or "kel", temperature);
            }

            return table;
        }

        public static double Convert(double value, string from, string to)
        {
            var (fromGroup, fromFactor, fromUnit) = Find(from);
            var (toGroup, toFactor, toUnit) = Find(to);
            if (fromGroup != toGroup)
            {
                throw new CalcErrorException(CellError.NotAvailable);
            }

            if (fromGroup == "temperature")
            {
                var kelvin = ToKelvin(value * fromFactor, fromUnit);
                return FromKelvin(kelvin, toUnit) / toFactor;
            }

            return value * fromFactor / toFactor;
        }

        private static (string Group, double Factor, string Unit) Find(string unit)
        {
            if (Table.TryGetValue(unit, out var exact))
            {
                return (exact.Group, exact.Factor, unit);
            }

            foreach (var (prefix, factor) in Prefixes.OrderByDescending(entry => entry.Key.Length))
            {
                if (unit.StartsWith(prefix, StringComparison.Ordinal) && Table.TryGetValue(unit[prefix.Length..], out var entry) && entry.Prefixable)
                {
                    var binary = prefix.EndsWith('i');
                    if (binary && entry.Group != "information")
                    {
                        continue;
                    }

                    var scale = entry.Group switch
                    {
                        "area" when !unit.Contains("^", StringComparison.Ordinal) && unit.EndsWith('2') => factor * factor,
                        "volume" when unit.EndsWith('3') => factor * factor * factor,
                        _ => factor,
                    };
                    return (entry.Group, entry.Factor * scale, unit[prefix.Length..]);
                }
            }

            throw new CalcErrorException(CellError.NotAvailable);
        }

        private static double ToKelvin(double value, string unit) => unit switch
        {
            "C" or "cel" => value + 273.15,
            "F" or "fah" => (value - 32) * 5 / 9 + 273.15,
            "Rank" => value * 5 / 9,
            "Reau" => value * 1.25 + 273.15,
            _ => value,
        };

        private static double FromKelvin(double value, string unit) => unit switch
        {
            "C" or "cel" => value - 273.15,
            "F" or "fah" => (value - 273.15) * 9 / 5 + 32,
            "Rank" => value * 9 / 5,
            "Reau" => (value - 273.15) * 0.8,
            _ => value,
        };
    }
}
