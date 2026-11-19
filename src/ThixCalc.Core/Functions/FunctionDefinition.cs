using ThixCalc.Core.Calculation;

namespace ThixCalc.Core.Functions;

/// <summary>What a function argument expects (decides implicit intersection, lifting over arrays, and dereferencing).</summary>
public enum ArgumentKind : byte
{
    /// <summary>A single value: a range gives the intersection with the formula's row/column, or – in array evaluation – the function is applied element by element.</summary>
    Value,

    /// <summary>A reference kept as a reference (ROW, OFFSET, SUMIF's range, INDEX's area …).</summary>
    Reference,

    /// <summary>An array: expressions inside are evaluated element by element (SUMPRODUCT, MMULT, LOOKUP's arrays …).</summary>
    Array,

    /// <summary>Values, ranges or arrays, passed as they are (SUM, COUNT, MAX …).</summary>
    Any,

    /// <summary>A LAMBDA.</summary>
    Lambda,
}

/// <summary>The category the function wizard groups by.</summary>
public enum FunctionCategory : byte
{
    Math,
    Statistical,
    Logical,
    Lookup,
    Text,
    DateTime,
    Information,
    Financial,
    Database,
    Engineering,
}

internal delegate Operand FunctionBody(FunctionCall call);

/// <summary>
/// A function of the library: canonical name, arity, argument kinds (the last one repeats), volatility, whether it returns a
/// reference or needs lazy arguments (IF, IFERROR, CHOOSE …), the file prefix of newer functions (_xlfn.) and its category.
/// </summary>
public sealed class FunctionDefinition
{
    internal FunctionDefinition(string name, int minArguments, int maxArguments, ArgumentKind[] kinds, FunctionBody body)
    {
        Name = name;
        MinArguments = minArguments;
        MaxArguments = maxArguments;
        Kinds = kinds;
        Body = body;
    }

    public string Name { get; }

    public int MinArguments { get; }

    public int MaxArguments { get; }

    public IReadOnlyList<ArgumentKind> Kinds { get; }

    public bool IsVolatile { get; internal init; }

    public bool ReturnsReference { get; internal init; }

    /// <summary>Arguments are evaluated by the function itself, when needed (IF evaluates one branch).</summary>
    public bool IsLazy { get; internal init; }

    /// <summary>A function of dynamic arrays (FILTER, SORT, SEQUENCE …).</summary>
    public bool IsArrayFunction { get; internal init; }

    /// <summary>"_xlfn." (or "_xlfn._xlws.") for functions newer than Excel 2007, as the file format writes them.</summary>
    public string? FilePrefix { get; internal init; }

    public FunctionCategory Category { get; internal init; }

    /// <summary>Repeating groups: arguments after the fixed ones come in groups of this size (SUMIFS: 2).</summary>
    public int RepeatGroup { get; internal init; } = 1;

    internal FunctionBody Body { get; }

    public ArgumentKind KindOf(int index)
    {
        if (Kinds.Count == 0)
        {
            return ArgumentKind.Value;
        }

        if (index < Kinds.Count)
        {
            return Kinds[index];
        }

        // Repeat the last group.
        var group = Math.Min(RepeatGroup, Kinds.Count);
        var start = Kinds.Count - group;
        return Kinds[start + (index - start) % group];
    }

    public override string ToString() => Name;
}
