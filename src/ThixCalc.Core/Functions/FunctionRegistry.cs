using ThixCalc.Core.Calculation;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Functions;

/// <summary>
/// The function library: every function by canonical name. The thematic files (MathFunctions, StatisticalFunctions …)
/// register theirs once. Localized names come from the locale (<see cref="Globalization.CalcLocale.FunctionName"/>).
/// </summary>
public static class FunctionRegistry
{
    private static readonly Lazy<Dictionary<string, FunctionDefinition>> Functions = new(Build);

    /// <summary>Functions newer than Excel 2007: the file format writes them with "_xlfn.".</summary>
    private static readonly HashSet<string> FutureFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "ACOT", "ACOTH", "AGGREGATE", "ARABIC", "BASE", "BETA.DIST", "BETA.INV", "BINOM.DIST", "BINOM.INV", "BITAND", "BITLSHIFT",
        "BITOR", "BITRSHIFT", "BITXOR", "CEILING.MATH", "CEILING.PRECISE", "CHISQ.DIST", "CHISQ.DIST.RT", "CHISQ.INV", "CHISQ.INV.RT",
        "CHISQ.TEST", "COMBINA", "CONCAT", "CONFIDENCE.NORM", "CONFIDENCE.T", "COT", "COTH", "COVARIANCE.P", "COVARIANCE.S", "CSC",
        "CSCH", "DAYS", "DECIMAL", "ENCODEURL", "ERF.PRECISE", "ERFC.PRECISE", "EXPON.DIST", "F.DIST", "F.DIST.RT", "F.INV", "F.INV.RT",
        "F.TEST", "FLOOR.MATH", "FLOOR.PRECISE", "FORECAST.LINEAR", "FORMULATEXT", "GAMMA", "GAMMA.DIST", "GAMMA.INV",
        "GAMMALN.PRECISE", "GAUSS", "HYPGEOM.DIST", "IFNA", "IFS", "ISFORMULA", "ISO.CEILING", "ISOWEEKNUM", "LOGNORM.DIST",
        "LOGNORM.INV", "MAXIFS", "MINIFS", "MODE.MULT", "MODE.SNGL", "MUNIT", "NEGBINOM.DIST", "NETWORKDAYS.INTL", "NORM.DIST",
        "NORM.INV", "NORM.S.DIST", "NORM.S.INV", "NUMBERVALUE", "PDURATION", "PERCENTILE.EXC", "PERCENTILE.INC", "PERCENTRANK.EXC",
        "PERCENTRANK.INC", "PERMUTATIONA", "PHI", "POISSON.DIST", "QUARTILE.EXC", "QUARTILE.INC", "RANK.AVG", "RANK.EQ", "RRI", "SEC",
        "SECH", "SHEET", "SHEETS", "SKEW.P", "STDEV.P", "STDEV.S", "SWITCH", "T.DIST", "T.DIST.2T", "T.DIST.RT", "T.INV", "T.INV.2T",
        "T.TEST", "TEXTJOIN", "UNICHAR", "UNICODE", "VAR.P", "VAR.S", "WEIBULL.DIST", "WORKDAY.INTL", "XOR", "Z.TEST",
        "SEQUENCE", "RANDARRAY", "SORTBY", "UNIQUE", "XLOOKUP", "XMATCH", "LET", "LAMBDA", "MAP", "REDUCE", "SCAN", "MAKEARRAY",
        "BYROW", "BYCOL", "ISOMITTED", "CHOOSECOLS", "CHOOSEROWS", "DROP", "EXPAND", "HSTACK", "VSTACK", "TAKE", "TOCOL", "TOROW",
        "WRAPCOLS", "WRAPROWS", "BINOM.DIST.RANGE", "ANCHORARRAY", "TEXTAFTER", "TEXTBEFORE", "TEXTSPLIT", "VALUETOTEXT", "ARRAYTOTEXT",
        "STOCKHISTORY", "FIELDVALUE", "IMAGE", "TRANSLATE", "DETECTLANGUAGE", "COPILOT", "GROUPBY", "PIVOTBY", "PERCENTOF", "TRIMRANGE",
        "REGEXTEST", "REGEXEXTRACT", "REGEXREPLACE", "FORECAST.ETS", "FORECAST.ETS.CONFINT", "FORECAST.ETS.SEASONALITY", "FORECAST.ETS.STAT",
        "IMCOSH", "IMCOT", "IMCSC", "IMCSCH", "IMSEC", "IMSECH", "IMSINH", "IMTAN", "FILTERXML", "WEBSERVICE", "SINGLE",
    };

    /// <summary>Functions written as "_xlfn._xlws." (worksheet-only dynamic array functions).</summary>
    private static readonly HashSet<string> WorksheetFunctions = new(StringComparer.OrdinalIgnoreCase) { "FILTER", "SORT" };

    /// <summary>Whether the function is one ThixCalc names but cannot compute offline (STOCKHISTORY, CUBE…, RTD …).</summary>
    public static bool IsUnsupported(string name) => Find(name)?.IsUnsupported == true;

    public static FunctionDefinition? Find(string name) => Functions.Value.TryGetValue(name, out var definition) ? definition : null;

    public static IReadOnlyCollection<FunctionDefinition> All => Functions.Value.Values;

    public static int Count => Functions.Value.Count;

    private static Dictionary<string, FunctionDefinition> Build()
    {
        var registrar = new FunctionRegistrar();
        MathFunctions.Register(registrar);
        StatisticalFunctions.Register(registrar);
        LogicalFunctions.Register(registrar);
        LookupFunctions.Register(registrar);
        ArrayFunctions.Register(registrar);
        TextFunctions.Register(registrar);
        DateTimeFunctions.Register(registrar);
        InformationFunctions.Register(registrar);
        FinancialFunctions.Register(registrar);
        DatabaseFunctions.Register(registrar);
        EngineeringFunctions.Register(registrar);
        DistributionFunctions.Register(registrar);
        SecurityFunctions.Register(registrar);
        ModernFunctions.Register(registrar);
        AnalysisFunctions.Register(registrar);
        UnsupportedFunctions.Register(registrar);
        foreach (var definition in registrar.Functions.Values)
        {
            if (CompatibilityFunctions.Contains(definition.Name))
            {
                definition.Category = FunctionCategory.Compatibility;
            }
            else if (WebFunctions.Contains(definition.Name))
            {
                definition.Category = FunctionCategory.Web;
            }
            else if (definition.Name.StartsWith("CUBE", StringComparison.Ordinal))
            {
                definition.Category = FunctionCategory.Cube;
            }
        }

        return registrar.Functions;
    }

    /// <summary>Excel's category Kompatibilität: the old names newer functions replaced (they keep working).</summary>
    private static readonly HashSet<string> CompatibilityFunctions = new(StringComparer.OrdinalIgnoreCase)
    {
        "BETADIST", "BETAINV", "BINOMDIST", "CEILING", "CHIDIST", "CHIINV", "CHITEST", "CONCATENATE", "CONFIDENCE", "COVAR",
        "CRITBINOM", "EXPONDIST", "FDIST", "FINV", "FLOOR", "FORECAST", "FTEST", "GAMMADIST", "GAMMAINV", "HYPGEOMDIST", "LOGINV",
        "LOGNORMDIST", "MODE", "NEGBINOMDIST", "NORMDIST", "NORMINV", "NORMSDIST", "NORMSINV", "PERCENTILE", "PERCENTRANK", "POISSON",
        "QUARTILE", "RANK", "STDEV", "STDEVP", "TDIST", "TINV", "TTEST", "VAR", "VARP", "WEIBULL", "ZTEST",
    };

    private static readonly HashSet<string> WebFunctions = new(StringComparer.OrdinalIgnoreCase) { "ENCODEURL", "FILTERXML", "WEBSERVICE" };

    internal static string? PrefixOf(string name) =>
        WorksheetFunctions.Contains(name) ? "_xlfn._xlws." : FutureFunctions.Contains(name) ? "_xlfn." : null;
}

/// <summary>Registers functions (used by the thematic files).</summary>
internal sealed class FunctionRegistrar
{
    public Dictionary<string, FunctionDefinition> Functions { get; } = new(StringComparer.OrdinalIgnoreCase);

    public FunctionCategory Category { get; set; }

    public FunctionDefinition Add(
        string name,
        int min,
        int max,
        FunctionBody body,
        ArgumentKind[]? kinds = null,
        bool isVolatile = false,
        bool returnsReference = false,
        bool lazy = false,
        bool array = false,
        int repeatGroup = 1,
        bool unsupported = false)
    {
        var definition = new FunctionDefinition(name, min, max, kinds ?? [ArgumentKind.Value], body)
        {
            IsVolatile = isVolatile,
            ReturnsReference = returnsReference,
            IsLazy = lazy,
            IsArrayFunction = array,
            FilePrefix = FunctionRegistry.PrefixOf(name),
            Category = Category,
            RepeatGroup = repeatGroup,
            IsUnsupported = unsupported,
        };
        Functions[name] = definition;
        return definition;
    }

    /// <summary>A function of one number.</summary>
    public void Number(string name, Func<double, double> function) =>
        Add(name, 1, 1, call => Result(function(call.Number(0))));

    /// <summary>A function of two numbers.</summary>
    public void Number(string name, Func<double, double, double> function) =>
        Add(name, 2, 2, call => Result(function(call.Number(0), call.Number(1))));

    /// <summary>A number result: NaN and infinities become #NUM!.</summary>
    public static Operand Result(double value) => CellValue.FromNumber(value);

    /// <summary>Raises #NUM! unless the condition holds.</summary>
    public static void Require(bool condition, CellError error = CellError.Number)
    {
        if (!condition)
        {
            throw new CalcErrorException(error);
        }
    }

    /// <summary>Another name for an existing function (STDEV.S = STDEV …).</summary>
    public void Alias(string name, string existing)
    {
        var source = Functions[existing];
        Add(name, source.MinArguments, source.MaxArguments, source.Body, source.Kinds.ToArray(), source.IsVolatile, source.ReturnsReference,
            source.IsLazy, source.IsArrayFunction, source.RepeatGroup);
    }
}
