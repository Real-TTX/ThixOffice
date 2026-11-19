using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Functions;

/// <summary>
/// The arguments of one function call as the function sees them: evaluated by kind (lazily for lazy functions), with
/// helpers that coerce to number / text / boolean / integer (an error argument or a failed conversion raises the error as
/// the function's result), flatten values of ranges and arrays, and materialize arrays.
/// </summary>
internal sealed class FunctionCall
{
    private readonly Operand?[] _arguments;
    private readonly bool[] _missing;

    public FunctionCall(FunctionDefinition definition, FunctionNode? node, EvaluationContext context)
    {
        Definition = definition;
        Node = node;
        Context = context;
        var count = node?.Arguments.Length ?? 0;
        _arguments = new Operand?[count];
        _missing = new bool[count];
        for (var index = 0; index < count; index++)
        {
            _missing[index] = node!.Arguments[index] is MissingArgumentNode;
        }
    }

    private FunctionCall(FunctionDefinition definition, EvaluationContext context, Operand?[] arguments, bool[] missing)
    {
        Definition = definition;
        Context = context;
        _arguments = arguments;
        _missing = missing;
    }

    public FunctionDefinition Definition { get; }

    public FunctionNode? Node { get; }

    public EvaluationContext Context { get; }

    public int Count => _arguments.Length;

    public bool IsMissing(int index) => index >= Count || _missing[index];

    public bool Has(int index) => !IsMissing(index);

    /// <summary>A call of the same function with given operands (lifting, LAMBDA helpers).</summary>
    public static FunctionCall WithOperands(FunctionDefinition definition, EvaluationContext context, params Operand[] operands) =>
        new(definition, context, operands.Select(operand => (Operand?)operand).ToArray(), new bool[operands.Length]);

    internal void SetArgument(int index, Operand value) => _arguments[index] = value;

    internal Operand? RawArgument(int index) => _arguments[index];

    /// <summary>The call for one element when lifting: array arguments of Value parameters replaced by their element.</summary>
    internal FunctionCall ElementCall(int row, int column)
    {
        var arguments = new Operand?[Count];
        for (var index = 0; index < Count; index++)
        {
            var value = _arguments[index];
            if (value is { Kind: OperandKind.Array } array && Definition.KindOf(index) == ArgumentKind.Value)
            {
                arguments[index] = array.Array.Broadcast(row, column);
            }
            else
            {
                arguments[index] = value;
            }
        }

        return new FunctionCall(Definition, Context, arguments, _missing);
    }

    /// <summary>The argument evaluated for its kind (lazy functions evaluate on first access).</summary>
    public Operand this[int index]
    {
        get
        {
            if (index >= Count)
            {
                return CellValue.Blank;
            }

            if (_arguments[index] is { } value)
            {
                return value;
            }

            var evaluated = Node is null
                ? CellValue.Blank
                : Evaluator.EvaluateArgument(Node.Arguments[index], Definition.KindOf(index), Context);
            _arguments[index] = evaluated;
            return evaluated;
        }
    }

    /// <summary>Evaluates an argument with another kind than the definition says (lazy functions decide).</summary>
    public Operand Evaluate(int index, ArgumentKind kind)
    {
        if (Node is null || index >= Count)
        {
            return this[index];
        }

        return Evaluator.EvaluateArgument(Node.Arguments[index], kind, Context);
    }

    // ---------------------------------------------------------------- single values

    /// <summary>The argument as one value (implicit intersection / first element); errors are returned, not raised.</summary>
    public CellValue Value(int index)
    {
        var operand = this[index];
        return operand.Kind == OperandKind.Value ? operand.Value : Evaluator.ToScalar(operand, Context);
    }

    /// <summary>The argument as one value; an error is raised.</summary>
    public CellValue ValueOrThrow(int index)
    {
        var value = Value(index);
        if (value.IsError)
        {
            throw new CalcErrorException(value.Error);
        }

        return value;
    }

    public double Number(int index) => Coercion.ToNumber(Value(index), Context.Locale, Context.Date1904);

    public double Number(int index, double fallback) => IsMissing(index) ? fallback : Number(index);

    public int Integer(int index) => Coercion.ToInt(Number(index));

    public int Integer(int index, int fallback) => IsMissing(index) ? fallback : Integer(index);

    public string Text(int index) => Coercion.ToText(Value(index), Context.Locale);

    public string Text(int index, string fallback) => IsMissing(index) ? fallback : Text(index);

    public bool Boolean(int index) => Coercion.ToBoolean(Value(index), Context.Locale);

    public bool Boolean(int index, bool fallback) => IsMissing(index) ? fallback : Boolean(index);

    // ---------------------------------------------------------------- ranges and arrays

    /// <summary>The argument as a reference, or null when it is a value or array.</summary>
    public ReferenceValue? Reference(int index)
    {
        var operand = this[index];
        if (operand.IsError)
        {
            throw new CalcErrorException(operand.Value.Error);
        }

        return operand.Kind == OperandKind.Reference ? operand.Reference : null;
    }

    /// <summary>The argument as an array (a reference materialized, a value as 1×1).</summary>
    public ArrayValue Array(int index) => ToArray(this[index]);

    public ArrayValue ToArray(Operand operand)
    {
        switch (operand.Kind)
        {
            case OperandKind.Array:
                return operand.Array;
            case OperandKind.Reference:
                var materialized = Evaluator.Materialize(operand.Reference, Context);
                if (materialized.Kind == OperandKind.Array)
                {
                    return materialized.Array;
                }

                if (materialized.IsError)
                {
                    throw new CalcErrorException(materialized.Value.Error);
                }

                return ArrayValue.Scalar(materialized.Value);
            case OperandKind.Lambda:
                throw new CalcErrorException(CellError.Value);
            default:
                return ArrayValue.Scalar(operand.Value);
        }
    }

    /// <summary>
    /// The values of the argument in row-major order: a value itself, every element of an array, the stored cells of a
    /// reference (blank cells are skipped – they are not stored).
    /// </summary>
    public IEnumerable<CellValue> Values(int index) => ValuesOf(this[index]);

    public IEnumerable<CellValue> ValuesOf(Operand operand)
    {
        switch (operand.Kind)
        {
            case OperandKind.Value:
                yield return operand.Value;
                break;
            case OperandKind.Array:
                foreach (var value in operand.Array.Values)
                {
                    yield return value;
                }

                break;
            case OperandKind.Reference:
                foreach (var area in operand.Reference.Areas)
                {
                    foreach (var value in Context.Engine.EnumerateValues(area))
                    {
                        yield return value.Value;
                    }
                }

                break;
        }
    }

    /// <summary>Whether the argument came from a range or array (aggregates ignore texts there, but convert direct texts).</summary>
    public bool IsFromRangeOrArray(int index) => this[index].Kind is OperandKind.Array or OperandKind.Reference;

    public LambdaValue Lambda(int index)
    {
        var operand = this[index];
        if (operand.Kind != OperandKind.Lambda)
        {
            throw new CalcErrorException(operand.IsError ? operand.Value.Error : CellError.Value);
        }

        return operand.Lambda;
    }
}
