using System.Globalization;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Globalization;

/// <summary>
/// How formulas, values and number formats look to a user of one language: function names, argument and array separators,
/// decimal and group symbols, TRUE/FALSE, error names, R1C1 letters, month and day names and the default formats.
/// Formulas are stored canonically (<see cref="Invariant"/>: English names, "," between arguments, "." as decimal point –
/// the file format); <see cref="Formulas.FormulaFormatter"/> and <see cref="Formulas.FormulaParser"/> translate.
/// </summary>
public sealed class CalcLocale
{
    private readonly IReadOnlyDictionary<string, string> _functionNames;
    private readonly Dictionary<string, string> _canonicalNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<CellError, string> _errorNames;

    private CalcLocale(
        string name,
        CultureInfo culture,
        IReadOnlyDictionary<string, string> functionNames,
        Dictionary<CellError, string> errorNames)
    {
        Name = name;
        Culture = culture;
        _functionNames = functionNames;
        _errorNames = errorNames;
        foreach (var (canonical, local) in functionNames)
        {
            _canonicalNames[local] = canonical;
        }
    }

    /// <summary>German (Germany): SUMME, ";" between arguments, decimal comma, #WERT!, Z1S1, TT.MM.JJJJ.</summary>
    public static CalcLocale German { get; } = new("de-DE", CultureInfo.GetCultureInfo("de-DE"), GermanFunctionNames.Names, new()
    {
        [CellError.Null] = "#NULL!",
        [CellError.DivisionByZero] = "#DIV/0!",
        [CellError.Value] = "#WERT!",
        [CellError.Reference] = "#BEZUG!",
        [CellError.Name] = "#NAME?",
        [CellError.Number] = "#ZAHL!",
        [CellError.NotAvailable] = "#NV",
        [CellError.GettingData] = "#DATEN_ABRUFEN",
        [CellError.Spill] = "#ÜBERLAUF!",
        [CellError.Calc] = "#KALK!",
    })
    {
        DecimalSeparator = ',',
        GroupSeparator = '.',
        ListSeparator = ';',
        ArrayColumnSeparator = '.',
        ArrayRowSeparator = ';',
        TrueName = "WAHR",
        FalseName = "FALSCH",
        RowLetter = 'Z',
        ColumnLetter = 'S',
        GeneralName = "Standard",
        ShortDateFormat = "dd.mm.yyyy",
        LongDateFormat = "[$-407]dddd, d. mmmm yyyy",
        ShortTimeFormat = "hh:mm",
        LongTimeFormat = "hh:mm:ss",
        DateTimeFormat = "dd.mm.yyyy hh:mm",
        CurrencySymbol = "€",
        CurrencyFormat = "#,##0.00 \"€\"",
        CurrencyNegativeRedFormat = "#,##0.00 \"€\";[Red]-#,##0.00 \"€\"",
        AccountingFormat = "_-* #,##0.00 \"€\"_-;-* #,##0.00 \"€\"_-;_-* \"-\"?? \"€\"_-;_-@_-",
        DateSeparator = '.',
        YearLetter = 'J',
        DayLetter = 'T',
        SheetBaseName = "Tabelle",
        TrueFalseInput = ["WAHR", "FALSCH"],
    };

    /// <summary>English (United States): SUM, "," between arguments, decimal point, #VALUE!, R1C1, m/d/yyyy.</summary>
    public static CalcLocale English { get; } = new("en-US", CultureInfo.GetCultureInfo("en-US"), new Dictionary<string, string>(), DefaultErrors())
    {
        CurrencySymbol = "$",
        CurrencyFormat = "\"$\"#,##0.00",
        CurrencyNegativeRedFormat = "\"$\"#,##0.00;[Red]-\"$\"#,##0.00",
        AccountingFormat = "_(\"$\"* #,##0.00_);_(\"$\"* \\(#,##0.00\\);_(\"$\"* \"-\"??_);_(@_)",
        LongDateFormat = "[$-409]dddd, mmmm d, yyyy",
        ShortTimeFormat = "h:mm",
        LongTimeFormat = "h:mm:ss AM/PM",
        DateTimeFormat = "m/d/yyyy h:mm",
        SheetBaseName = "Sheet",
    };

    /// <summary>The file format's form (English names, "." decimal point, "," between arguments) – how formulas are stored.</summary>
    public static CalcLocale Invariant { get; } = new("", CultureInfo.InvariantCulture, new Dictionary<string, string>(), DefaultErrors())
    {
        SheetBaseName = "Sheet",
    };

    /// <summary>de-DE / en-US by culture name (German for any "de", English otherwise).</summary>
    public static CalcLocale For(string cultureName) =>
        cultureName.StartsWith("de", StringComparison.OrdinalIgnoreCase) ? German : English;

    public string Name { get; }

    public CultureInfo Culture { get; }

    public char DecimalSeparator { get; private init; } = '.';

    public char GroupSeparator { get; private init; } = ',';

    /// <summary>Between function arguments (and the union operator).</summary>
    public char ListSeparator { get; private init; } = ',';

    public char ArrayColumnSeparator { get; private init; } = ',';

    public char ArrayRowSeparator { get; private init; } = ';';

    public string TrueName { get; private init; } = "TRUE";

    public string FalseName { get; private init; } = "FALSE";

    /// <summary>Further words typed as booleans.</summary>
    public IReadOnlyList<string> TrueFalseInput { get; private init; } = ["TRUE", "FALSE"];

    /// <summary>R1C1: "R" / German Z1S1: "Z".</summary>
    public char RowLetter { get; private init; } = 'R';

    public char ColumnLetter { get; private init; } = 'C';

    /// <summary>What the General format is called ("Standard" in German).</summary>
    public string GeneralName { get; private init; } = "General";

    /// <summary>Canonical format codes this locale shows for built-in ids (14 = short date …).</summary>
    public string ShortDateFormat { get; private init; } = "m/d/yyyy";

    public string LongDateFormat { get; private init; } = "dddd, mmmm d, yyyy";

    public string ShortTimeFormat { get; private init; } = "h:mm";

    public string LongTimeFormat { get; private init; } = "h:mm:ss";

    public string DateTimeFormat { get; private init; } = "m/d/yyyy h:mm";

    public string CurrencySymbol { get; private init; } = "¤";

    public string CurrencyFormat { get; private init; } = "#,##0.00";

    public string CurrencyNegativeRedFormat { get; private init; } = "#,##0.00;[Red]-#,##0.00";

    public string AccountingFormat { get; private init; } = "#,##0.00";

    public char DateSeparator { get; private init; } = '/';

    /// <summary>The year letter of localized format codes ("J" in German: TT.MM.JJJJ).</summary>
    public char YearLetter { get; private init; } = 'y';

    /// <summary>The day letter of localized format codes ("T" in German).</summary>
    public char DayLetter { get; private init; } = 'd';

    /// <summary>"Tabelle" / "Sheet" (new sheets are called Tabelle1, Tabelle2 …).</summary>
    public string SheetBaseName { get; private init; } = "Sheet";

    /// <summary>Month names as Excel shows them (fixed tables: ICU and Windows differ, e.g. "Mär" / "Mrz").</summary>
    public string[] MonthNames => IsGerman
        ? ["Januar", "Februar", "März", "April", "Mai", "Juni", "Juli", "August", "September", "Oktober", "November", "Dezember"]
        : ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    public string[] MonthAbbreviations => IsGerman
        ? ["Jan", "Feb", "Mrz", "Apr", "Mai", "Jun", "Jul", "Aug", "Sep", "Okt", "Nov", "Dez"]
        : ["Jan", "Feb", "Mar", "Apr", "May", "Jun", "Jul", "Aug", "Sep", "Oct", "Nov", "Dec"];

    /// <summary>Day names from Sunday.</summary>
    public string[] DayNames => IsGerman
        ? ["Sonntag", "Montag", "Dienstag", "Mittwoch", "Donnerstag", "Freitag", "Samstag"]
        : ["Sunday", "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday"];

    public string[] DayAbbreviations => IsGerman
        ? ["So", "Mo", "Di", "Mi", "Do", "Fr", "Sa"]
        : ["Sun", "Mon", "Tue", "Wed", "Thu", "Fri", "Sat"];

    private bool IsGerman => Name.StartsWith("de", StringComparison.Ordinal);

    public bool IsInvariant => Name.Length == 0;

    /// <summary>The name a user of this locale sees for a canonical function name (unchanged when it has no translation).</summary>
    public string FunctionName(string canonical) => _functionNames.TryGetValue(canonical, out var local) ? local : canonical;

    /// <summary>The canonical name of a function name typed in this locale (English names are accepted too).</summary>
    public string CanonicalFunctionName(string local) => _canonicalNames.TryGetValue(local, out var canonical) ? canonical : local.ToUpperInvariant();

    public string ErrorName(CellError error) => _errorNames.TryGetValue(error, out var name) ? name : CellErrors.NameOf(error);

    /// <summary>The error a localized (or canonical) error name stands for, or null.</summary>
    public CellError? ParseError(string text)
    {
        foreach (var (error, name) in _errorNames)
        {
            if (string.Equals(name, text, StringComparison.OrdinalIgnoreCase))
            {
                return error;
            }
        }

        return CellErrors.Parse(text);
    }

    /// <summary>The longest error name in this locale that starts at <paramref name="index"/> of <paramref name="text"/>, or null.</summary>
    public (CellError Error, int Length)? MatchError(string text, int index)
    {
        (CellError, int)? best = null;
        foreach (var (error, name) in _errorNames.Concat(DefaultErrors()))
        {
            if (string.Compare(text, index, name, 0, name.Length, StringComparison.OrdinalIgnoreCase) == 0 && (best is null || name.Length > best.Value.Item2))
            {
                best = (error, name.Length);
            }
        }

        return best;
    }

    public string BooleanName(bool value) => value ? TrueName : FalseName;

    /// <summary>A number as a formula writes it in this locale (15 significant digits, decimal separator).</summary>
    public string FormatNumberLiteral(double value)
    {
        var text = NumberText.ToRoundTrip(value);
        return DecimalSeparator == '.' ? text : text.Replace('.', DecimalSeparator);
    }

    public override string ToString() => IsInvariant ? "(invariant)" : Name;

    private static Dictionary<CellError, string> DefaultErrors() => CellErrors.All.ToDictionary(error => error, CellErrors.NameOf);
}

/// <summary>Number to text conversions Excel uses outside number formats.</summary>
public static class NumberText
{
    /// <summary>
    /// The shortest text that reads back as the same double, in invariant form with an exponent only when Excel would use one
    /// (formula literals and the file format).
    /// </summary>
    public static string ToRoundTrip(double value)
    {
        var text = value.ToString("R", CultureInfo.InvariantCulture);
        var exponentAt = text.IndexOf('E', StringComparison.Ordinal);
        var magnitude = Math.Abs(value);
        if (exponentAt < 0 || magnitude >= 1e15 || magnitude < 1e-20)
        {
            return text;
        }

        // 1E-06 → 0.000001: move the decimal point of the mantissa.
        var negative = text.StartsWith('-');
        var mantissa = text[(negative ? 1 : 0)..exponentAt].Replace(".", string.Empty, StringComparison.Ordinal);
        var pointAt = (text[(negative ? 1 : 0)..exponentAt].IndexOf('.') is var dot && dot >= 0 ? dot : exponentAt - (negative ? 1 : 0))
            + int.Parse(text[(exponentAt + 1)..], CultureInfo.InvariantCulture);
        string result;
        if (pointAt <= 0)
        {
            result = "0." + new string('0', -pointAt) + mantissa;
        }
        else if (pointAt >= mantissa.Length)
        {
            result = mantissa + new string('0', pointAt - mantissa.Length);
        }
        else
        {
            result = mantissa[..pointAt] + "." + mantissa[pointAt..];
        }

        return negative ? "-" + result : result;
    }

    /// <summary>
    /// A number as Excel converts it to text (="x"&amp;A1, TEXT-less concatenation, the formula bar): rounded to 15 significant
    /// digits, no trailing zeros, scientific outside 1E-9 … 1E15 like General would, in the locale's decimal separator.
    /// </summary>
    public static string ToText(double value, CalcLocale locale)
    {
        if (value == 0)
        {
            return "0";
        }

        var rounded = RoundToSignificant(value, 15);
        var magnitude = Math.Abs(rounded);
        string text;
        if (magnitude >= 1e15 || magnitude < 1e-9)
        {
            text = rounded.ToString("0.##############E+00", CultureInfo.InvariantCulture);
        }
        else
        {
            text = rounded.ToString("0.###############", CultureInfo.InvariantCulture);
            if (text.Replace("-", string.Empty, StringComparison.Ordinal).Replace(".", string.Empty, StringComparison.Ordinal).TrimStart('0').Length > 15)
            {
                text = rounded.ToString("G15", CultureInfo.InvariantCulture);
            }
        }

        return locale.DecimalSeparator == '.' ? text : text.Replace('.', locale.DecimalSeparator);
    }

    /// <summary>Rounds to <paramref name="digits"/> significant digits (Excel keeps 15).</summary>
    public static double RoundToSignificant(double value, int digits)
    {
        if (value == 0 || double.IsNaN(value) || double.IsInfinity(value))
        {
            return value;
        }

        var text = value.ToString("E" + (digits - 1).ToString(CultureInfo.InvariantCulture), CultureInfo.InvariantCulture);
        return double.Parse(text, CultureInfo.InvariantCulture);
    }
}
