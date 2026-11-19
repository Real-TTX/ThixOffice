using System.Collections.Immutable;
using ThixCalc.Core.Calculation;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Model;
using static ThixCalc.Core.Functions.FunctionRegistrar;

namespace ThixCalc.Core.Functions;

/// <summary>Logical functions (Logik) including LET, LAMBDA and its helpers MAP, REDUCE, SCAN, MAKEARRAY, BYROW, BYCOL.</summary>
internal static class LogicalFunctions
{
    public static void Register(FunctionRegistrar r)
    {
        r.Category = FunctionCategory.Logical;
        r.Add("IF", 2, 3, If, lazy: true);
        r.Add("IFS", 2, 254, Ifs, lazy: true, repeatGroup: 2);
        r.Add("IFERROR", 2, 2, call => ReplaceErrors(call, _ => true), lazy: true);
        r.Add("IFNA", 2, 2, call => ReplaceErrors(call, error => error == CellError.NotAvailable), lazy: true);
        r.Add("SWITCH", 3, 254, Switch, lazy: true);
        r.Add("CHOOSE", 2, 255, Choose, lazy: true, returnsReference: true);
        r.Add("AND", 1, 255, call => Logical(call, (all, value) => all && value, true), [ArgumentKind.Any]);
        r.Add("OR", 1, 255, call => Logical(call, (any, value) => any || value, false), [ArgumentKind.Any]);
        r.Add("XOR", 1, 255, call => Logical(call, (odd, value) => odd ^ value, false), [ArgumentKind.Any]);
        r.Add("NOT", 1, 1, call => !call.Boolean(0));
        r.Add("TRUE", 0, 0, _ => true);
        r.Add("FALSE", 0, 0, _ => false);
        r.Add("LET", 3, 253, Let, lazy: true);
        r.Add("LAMBDA", 1, 254, Lambda, lazy: true);
        r.Add("ISOMITTED", 1, 1, IsOmitted, lazy: true);
        r.Add("MAP", 2, 254, Map, [ArgumentKind.Array, ArgumentKind.Any], array: true);
        r.Add("REDUCE", 2, 3, call => Reduce(call, scan: false), [ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Lambda], array: true);
        r.Add("SCAN", 2, 3, call => Reduce(call, scan: true), [ArgumentKind.Value, ArgumentKind.Array, ArgumentKind.Lambda], array: true);
        r.Add("MAKEARRAY", 3, 3, MakeArray, [ArgumentKind.Value, ArgumentKind.Value, ArgumentKind.Lambda], array: true);
        r.Add("BYROW", 2, 2, call => ByLine(call, rows: true), [ArgumentKind.Array, ArgumentKind.Lambda], array: true);
        r.Add("BYCOL", 2, 2, call => ByLine(call, rows: false), [ArgumentKind.Array, ArgumentKind.Lambda], array: true);
    }

    private static Operand Branch(FunctionCall call, int index, CellValue fallback) =>
        call.Count > index ? (call.IsMissing(index) ? CellValue.Zero : call.Evaluate(index, ArgumentKind.Any)) : fallback;

    private static Operand If(FunctionCall call)
    {
        var condition = call.Evaluate(0, ArgumentKind.Value);
        if (condition.Kind == OperandKind.Array)
        {
            var whenTrue = Values(call, Branch(call, 1, CellValue.True));
            var whenFalse = Values(call, Branch(call, 2, CellValue.False));
            return Elementwise3(condition.Array, whenTrue, whenFalse, (test, yes, no) =>
            {
                if (test.IsError)
                {
                    return test;
                }

                return Truth(test, call) is { } truth ? (truth ? yes : no) : CellValue.FromError(CellError.Value);
            });
        }

        var value = condition.Value;
        if (value.IsError)
        {
            return value;
        }

        var result = Truth(value, call) ?? throw new CalcErrorException(CellError.Value);
        return result ? Branch(call, 1, CellValue.True) : Branch(call, 2, CellValue.False);
    }

    private static bool? Truth(CellValue value, FunctionCall call)
    {
        try
        {
            return Coercion.ToBoolean(value, call.Context.Locale);
        }
        catch (CalcErrorException)
        {
            return null;
        }
    }

    private static ArrayValue Values(FunctionCall call, Operand operand) => operand.Kind switch
    {
        OperandKind.Reference => call.ToArray(operand),
        OperandKind.Array => operand.Array,
        _ => ArrayValue.Scalar(operand.Kind == OperandKind.Value ? operand.Value : CellValue.FromError(CellError.Value)),
    };

    private static ArrayValue Elementwise3(ArrayValue first, ArrayValue second, ArrayValue third, Func<CellValue, CellValue, CellValue, CellValue> apply)
    {
        var rows = Math.Max(first.Rows, Math.Max(second.Rows == 1 ? 1 : second.Rows, third.Rows == 1 ? 1 : third.Rows));
        var columns = Math.Max(first.Columns, Math.Max(second.Columns == 1 ? 1 : second.Columns, third.Columns == 1 ? 1 : third.Columns));
        var result = new ArrayValue(rows, columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                result[row, column] = apply(first.Broadcast(row, column), second.Broadcast(row, column), third.Broadcast(row, column));
            }
        }

        return result;
    }

    private static Operand Ifs(FunctionCall call)
    {
        if (call.Count % 2 != 0)
        {
            return CellError.Value;
        }

        for (var index = 0; index + 1 < call.Count; index += 2)
        {
            var condition = call.Evaluate(index, ArgumentKind.Value);
            var value = condition.Kind == OperandKind.Value ? condition.Value : Evaluator.ToScalar(condition, call.Context);
            if (value.IsError)
            {
                return value;
            }

            if (Truth(value, call) is not { } truth)
            {
                return CellError.Value;
            }

            if (truth)
            {
                return call.Evaluate(index + 1, ArgumentKind.Any);
            }
        }

        return CellError.NotAvailable;
    }

    private static Operand ReplaceErrors(FunctionCall call, Func<CellError, bool> replace)
    {
        var value = call.Evaluate(0, ArgumentKind.Value);
        if (value.Kind == OperandKind.Array)
        {
            var array = value.Array;
            ArrayValue? fallback = null;
            var result = new ArrayValue(array.Rows, array.Columns);
            for (var index = 0; index < array.Count; index++)
            {
                var element = array.Values[index];
                if (element.IsError && replace(element.Error))
                {
                    fallback ??= Values(call, call.Evaluate(1, ArgumentKind.Value));
                    element = fallback.Broadcast(index / array.Columns, index % array.Columns);
                }

                result.Values[index] = element.IsBlank ? CellValue.Zero : element;
            }

            return result;
        }

        if (value.Kind == OperandKind.Value && value.Value.IsError && replace(value.Value.Error))
        {
            return call.IsMissing(1) ? CellValue.Zero : call.Evaluate(1, ArgumentKind.Any);
        }

        return value.Kind == OperandKind.Value && value.Value.IsBlank ? CellValue.Zero : value;
    }

    private static Operand Switch(FunctionCall call)
    {
        var subject = Evaluator.ToScalar(call.Evaluate(0, ArgumentKind.Value), call.Context);
        if (subject.IsError)
        {
            return subject;
        }

        var index = 1;
        for (; index + 1 < call.Count; index += 2)
        {
            var candidate = Evaluator.ToScalar(call.Evaluate(index, ArgumentKind.Value), call.Context);
            if (candidate.IsError)
            {
                return candidate;
            }

            if (ValueComparer.AreEqual(subject, candidate) && subject.Kind == candidate.Kind)
            {
                return call.Evaluate(index + 1, ArgumentKind.Any);
            }
        }

        return index < call.Count ? call.Evaluate(index, ArgumentKind.Any) : CellError.NotAvailable;
    }

    private static Operand Choose(FunctionCall call)
    {
        var selector = call.Evaluate(0, ArgumentKind.Value);
        if (selector.Kind == OperandKind.Array)
        {
            var source = selector.Array;
            var result = new ArrayValue(source.Rows, source.Columns);
            for (var index = 0; index < source.Count; index++)
            {
                var position = Position(source.Values[index], call);
                result.Values[index] = position is { } chosen
                    ? Evaluator.ToScalar(call.Evaluate(chosen, ArgumentKind.Value), call.Context)
                    : CellValue.FromError(CellError.Value);
            }

            return result;
        }

        if (selector.Value.IsError)
        {
            return selector.Value;
        }

        return Position(selector.Value, call) is { } argument ? call.Evaluate(argument, ArgumentKind.Any) : CellError.Value;
    }

    private static int? Position(CellValue value, FunctionCall call)
    {
        if (!Coercion.TryNumber(value, call.Context.Locale, call.Context.Date1904, out var number, out _))
        {
            return null;
        }

        var index = (int)Math.Truncate(number);
        return index >= 1 && index < call.Count ? index : null;
    }

    private static Operand Logical(FunctionCall call, Func<bool, bool, bool> fold, bool seed)
    {
        var result = seed;
        var any = false;
        for (var index = 0; index < call.Count; index++)
        {
            var operand = call[index];
            if (operand.Kind == OperandKind.Value)
            {
                var value = operand.Value;
                if (value.IsError)
                {
                    return value;
                }

                if (value.IsBlank && call.IsMissing(index))
                {
                    result = fold(result, false);
                    any = true;
                    continue;
                }

                if (value.IsBlank)
                {
                    continue;
                }

                result = fold(result, Coercion.ToBoolean(value, call.Context.Locale));
                any = true;
                continue;
            }

            foreach (var value in call.ValuesOf(operand))
            {
                if (value.IsError)
                {
                    return value;
                }

                if (value.IsNumber || value.IsBoolean)
                {
                    result = fold(result, value.Number != 0);
                    any = true;
                }
            }
        }

        return any ? result : CellError.Value;
    }

    /// <summary>A LET/LAMBDA name; LAMBDA's optional parameters are written in brackets ([b]).</summary>
    private static string ParameterName(FunctionCall call, int index) => call.Node?.Arguments[index] switch
    {
        NameNode { SheetId: null } name => name.Name,
        StructuredReferenceNode { TableName: null, Specifier: ['[', .., ']'] specifier } => specifier[1..^1],
        _ => throw new CalcErrorException(CellError.Value),
    };

    private static Operand Let(FunctionCall call)
    {
        if (call.Count % 2 == 0)
        {
            return CellError.Value;
        }

        var context = call.Context;
        var saved = context.Scope;
        try
        {
            for (var index = 0; index + 1 < call.Count; index += 2)
            {
                var name = ParameterName(call, index);
                var value = Evaluator.Evaluate(call.Node!.Arguments[index + 1], context);
                context.Scope = new Scope(name, value, context.Scope);
            }

            return Evaluator.Evaluate(call.Node!.Arguments[^1], context);
        }
        finally
        {
            context.Scope = saved;
        }
    }

    private static Operand Lambda(FunctionCall call)
    {
        var names = ImmutableArray.CreateBuilder<string>();
        for (var index = 0; index + 1 < call.Count; index++)
        {
            names.Add(ParameterName(call, index));
        }

        return Operand.From(new LambdaValue(names.ToImmutable(), call.Node!.Arguments[^1], call.Context.Scope));
    }

    private static Operand IsOmitted(FunctionCall call)
    {
        var name = ParameterName(call, 0);
        return call.Context.Scope is { } scope && scope.TryFind("\u0001omitted:" + name, out _);
    }

    private static CellValue Invoke(FunctionCall call, LambdaValue lambda, params Operand[] arguments)
    {
        var result = Evaluator.CallLambda(lambda, arguments, call.Context);
        if (result.Kind == OperandKind.Reference)
        {
            result = Evaluator.Finish(result, call.Context);
        }

        if (result.Kind == OperandKind.Array)
        {
            return result.Array.Count == 1 ? result.Array.Values[0] : CellValue.FromError(CellError.Calc);
        }

        return result.Kind == OperandKind.Value ? result.Value : CellValue.FromError(CellError.Calc);
    }

    private static Operand Map(FunctionCall call)
    {
        var lambda = call.Lambda(call.Count - 1);
        var arrays = Enumerable.Range(0, call.Count - 1).Select(call.Array).ToList();
        var first = arrays[0];
        var result = new ArrayValue(first.Rows, first.Columns);
        for (var row = 0; row < first.Rows; row++)
        {
            for (var column = 0; column < first.Columns; column++)
            {
                var arguments = arrays.Select(array => (Operand)array.Broadcast(row, column)).ToArray();
                result[row, column] = Invoke(call, lambda, arguments);
            }
        }

        return result;
    }

    private static Operand Reduce(FunctionCall call, bool scan)
    {
        var hasInitial = call.Count == 3;
        var accumulator = hasInitial && call.Has(0) ? call.Value(0) : CellValue.Blank;
        var array = call.Array(hasInitial ? 1 : 0);
        var lambda = call.Lambda(call.Count - 1);
        var result = scan ? new ArrayValue(array.Rows, array.Columns) : null;
        for (var index = 0; index < array.Count; index++)
        {
            accumulator = Invoke(call, lambda, accumulator, array.Values[index]);
            if (result is not null)
            {
                result.Values[index] = accumulator;
            }
        }

        return result is not null ? result : accumulator;
    }

    private static Operand MakeArray(FunctionCall call)
    {
        var rows = call.Integer(0);
        var columns = call.Integer(1);
        Require(rows >= 1 && columns >= 1, CellError.Value);
        var lambda = call.Lambda(2);
        var result = new ArrayValue(rows, columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                result[row, column] = Invoke(call, lambda, row + 1.0, column + 1.0);
            }
        }

        return result;
    }

    private static Operand ByLine(FunctionCall call, bool rows)
    {
        var array = call.Array(0);
        var lambda = call.Lambda(1);
        var count = rows ? array.Rows : array.Columns;
        var result = rows ? new ArrayValue(count, 1) : new ArrayValue(1, count);
        for (var line = 0; line < count; line++)
        {
            var slice = rows ? new ArrayValue(1, array.Columns) : new ArrayValue(array.Rows, 1);
            for (var index = 0; index < slice.Count; index++)
            {
                slice.Values[index] = rows ? array[line, index] : array[index, line];
            }

            result.Values[line] = Invoke(call, lambda, slice);
        }

        return result;
    }
}
