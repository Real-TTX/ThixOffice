using ThixCalc.Core.Globalization;

namespace ThixCalc.Core.Formulas;

/// <summary>A1 (B3, $C$4) or R1C1 (Z3S2 in German) references.</summary>
public enum ReferenceStyle : byte
{
    A1,
    R1C1,
}

/// <summary>
/// How a formula text is written: the locale (function names, separators, decimal symbol) and the reference style.
/// <see cref="FileFormat"/> also writes the _xlfn./_xlpm. prefixes of the file format.
/// </summary>
public sealed record FormulaSyntax(CalcLocale Locale, ReferenceStyle Style = ReferenceStyle.A1, bool IsFileFormat = false)
{
    /// <summary>The stored form: English, A1, without file prefixes.</summary>
    public static FormulaSyntax Canonical { get; } = new(CalcLocale.Invariant);

    /// <summary>The form of xlsx files: English, A1, with _xlfn. prefixes for newer functions.</summary>
    public static FormulaSyntax FileFormat { get; } = new(CalcLocale.Invariant, ReferenceStyle.A1, true);

    public static FormulaSyntax German { get; } = new(CalcLocale.German);

    public static FormulaSyntax English { get; } = new(CalcLocale.English);
}

/// <summary>A formula text that cannot be parsed; <see cref="Position"/> is where the problem is.</summary>
public sealed class FormulaParseException(string message, int position) : Exception(message)
{
    public int Position { get; } = position;
}
