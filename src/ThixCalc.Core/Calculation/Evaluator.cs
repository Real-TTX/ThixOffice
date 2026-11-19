using System.Collections.Immutable;
using ThixCalc.Core.Formulas;
using ThixCalc.Core.Functions;
using ThixCalc.Core.Model;

namespace ThixCalc.Core.Calculation;

/// <summary>
/// Evaluates formula ASTs with Excel's semantics: references stay references until a value is needed; operators coerce
/// (text to number, number to text), propagate errors left first and work element by element on arrays with broadcasting;
/// in legacy formulas a range where one value is expected is intersected with the formula's row/column; functions get
/// their arguments by kind and are lifted over arrays in array mode.
/// </summary>
internal static class Evaluator
{
    private const int MaxLambdaDepth = 1000;
    private const long MaxArrayCells = 50_000_000;

    public static Operand Evaluate(FormulaNode node, EvaluationContext context)
    {
        switch (node)
        {
            case NumberNode number:
                return CellValue.FromNumber(number.Value);
            case TextNode text:
                return CellValue.FromText(text.Value);
            case BooleanNode boolean:
                return CellValue.FromBoolean(boolean.Value);
            case ErrorNode error:
                return CellValue.FromError(error.Error);
            case MissingArgumentNode:
                return CellValue.Blank;
            case ReferenceNode reference:
                return ResolveReference(reference, context);
            case ReferenceErrorNode:
            case ExternalReferenceNode:
                return Operand.Error(CellError.Reference);
            case NameNode name:
                return EvaluateName(name, context);
            case FunctionNode function:
                return InvokeFunction(function, context);
            case UnaryNode unary:
                return EvaluateUnary(unary, context);
            case PercentNode percent:
                return MapNumbers(Evaluate(percent.Operand, context), context, value => value / 100);
            case BinaryNode binary:
                return EvaluateBinary(binary, context);
            case ParenthesesNode parentheses:
                return Evaluate(parentheses.Inner, context);
            case ArrayNode array:
                return ArrayValue.FromNode(array);
            case StructuredReferenceNode structured:
                return StructuredReferences.Resolve(structured, context);
            default:
                return Operand.Error(CellError.Value);
        }
    }

    // ---------------------------------------------------------------- references and names

    public static Operand ResolveReference(ReferenceNode reference, EvaluationContext context)
    {
        var range = reference.Area.Resolve(context.Origin);
        if (range is null)
        {
            return Operand.Error(CellError.Reference);
        }

        if (reference.Sheet is null)
        {
            return new ReferenceValue(context.Sheet, range.Value);
        }

        var sheets = context.Workbook.Calculation.SheetsOf(reference.Sheet);
        if (sheets.Count == 0)
        {
            return Operand.Error(CellError.Reference);
        }

        if (sheets.Count == 1)
        {
            return new ReferenceValue(sheets[0], range.Value);
        }

        return new ReferenceValue(sheets.Select(sheet => new SheetArea(sheet, range.Value)).ToArray());
    }

    /// <summary>A function used as a LAMBDA (SUMME in =BYROW(A1:C3; SUMME)): as many parameters as it requires (at least one).</summary>
    private static Operand EtaLambda(FunctionDefinition function)
    {
        var count = Math.Clamp(function.MinArguments, 1, 3);
        var parameters = Enumerable.Range(1, count).Select(index => "eta" + index).ToImmutableArray();
        var body = new FunctionNode(function.Name, function, [.. parameters.Select(parameter => (FormulaNode)new NameNode(parameter))]);
        return Operand.From(new LambdaValue(parameters, body, null));
    }

    private static Operand EvaluateName(NameNode name, EvaluationContext context)
    {
        if (name.SheetId is null && context.Scope is { } scope && scope.TryFind(name.Name, out var variable))
        {
            return variable;
        }

        if (name.EtaFunction is { } eta && FunctionRegistry.Find(eta) is { } function && context.Workbook.FindName(name.Name, context.Sheet) is null)
        {
            return EtaLambda(function);
        }

        var sheet = name.SheetId is { } id ? context.Workbook.GetSheetById(id) : context.Sheet;
        var defined = context.Workbook.FindName(name.Name, sheet);
        if (defined is not null)
        {
            var node = context.Engine.ParsedName(defined);
            if (node is null)
            {
                return Operand.Error(CellError.Name);
            }

            if (context.Depth > MaxLambdaDepth)
            {
                return Operand.Error(CellError.Calc);
            }

            context.Depth++;
            var savedScope = context.Scope;
            context.Scope = null;
            try
            {
                return Evaluate(node, context);
            }
            finally
            {
                context.Scope = savedScope;
                context.Depth--;
            }
        }

        if (name.SheetId is null && StructuredReferences.FindTable(context.Workbook, name.Name) is { } table)
        {
            return new ReferenceValue(table.Sheet, table.Table.DataRange);
        }

        return Operand.Error(CellError.Name);
    }

    // ---------------------------------------------------------------- dereferencing

    /// <summary>The value of a reference/array where one value is expected (implicit intersection, or the top-left element).</summary>
    public static CellValue ToScalar(Operand operand, EvaluationContext context)
    {
        switch (operand.Kind)
        {
            case OperandKind.Value:
                return operand.Value;
            case OperandKind.Array:
                return operand.Array.Count > 0 ? operand.Array.Values[0] : CellValue.FromError(CellError.Value);
            case OperandKind.Reference:
                return ImplicitIntersection(operand.Reference, context);
            default:
                return CellValue.FromError(CellError.Calc);
        }
    }

    /// <summary>The cell of a reference in the formula's row or column (the @ operator), or #VALUE!.</summary>
    public static CellValue ImplicitIntersection(ReferenceValue reference, EvaluationContext context)
    {
        if (!reference.IsSingleArea)
        {
            return CellValue.FromError(CellError.Value);
        }

        var (sheet, range) = reference.First;
        if (range.IsSingleCell)
        {
            return context.Engine.GetValue(sheet, range.FirstRow, range.FirstColumn);
        }

        var row = range.RowCount == 1 ? range.FirstRow : context.Row;
        var column = range.ColumnCount == 1 ? range.FirstColumn : context.Column;
        if (!range.Contains(row, column))
        {
            return CellValue.FromError(CellError.Value);
        }

        return context.Engine.GetValue(sheet, row, column);
    }

    /// <summary>A reference as an array of its values (single area; several areas → #VALUE!).</summary>
    public static Operand Materialize(ReferenceValue reference, EvaluationContext context)
    {
        if (!reference.IsSingleArea)
        {
            return Operand.Error(CellError.Value);
        }

        var area = reference.First;
        if (area.Range.CellCount > MaxArrayCells)
        {
            return Operand.Error(CellError.Number);
        }

        return context.Engine.Materialize(area);
    }

    /// <summary>For operators: a single cell becomes its value, a range an array (array mode) or its intersection (legacy).</summary>
    private static Operand Prepare(Operand operand, EvaluationContext context)
    {
        switch (operand.Kind)
        {
            case OperandKind.Reference:
                var reference = operand.Reference;
                if (reference.IsSingleCell)
                {
                    var area = reference.First;
                    return context.Engine.GetValue(area.Sheet, area.Range.FirstRow, area.Range.FirstColumn);
                }

                if (context.ArrayMode)
                {
                    context.UsedArrays = true;
                    return Materialize(reference, context);
                }

                return ImplicitIntersection(reference, context);
            case OperandKind.Lambda:
                return Operand.Error(CellError.Calc);
            case OperandKind.Array:
                if (operand.Array.Count == 1)
                {
                    return operand.Array.Values[0];
                }

                context.UsedArrays = true;
                return operand;
            default:
                return operand;
        }
    }

    // ---------------------------------------------------------------- operators

    private static Operand EvaluateUnary(UnaryNode unary, EvaluationContext context)
    {
        var operand = Evaluate(unary.Operand, context);
        return unary.Operator switch
        {
            UnaryOperator.Minus => MapNumbers(operand, context, value => -value),
            UnaryOperator.Plus => operand.Kind == OperandKind.Reference ? Prepare(operand, context) : operand,
            _ => operand.Kind switch
            {
                OperandKind.Reference => ImplicitIntersection(operand.Reference, context),
                OperandKind.Array => operand.Array.Count > 0 ? operand.Array.Values[0] : CellValue.FromError(CellError.Value),
                _ => operand,
            },
        };
    }

    private static Operand MapNumbers(Operand operand, EvaluationContext context, Func<double, double> map)
    {
        operand = Prepare(operand, context);
        if (operand.Kind == OperandKind.Value)
        {
            return MapNumber(operand.Value, context, map);
        }

        var source = operand.Array;
        var result = new ArrayValue(source.Rows, source.Columns);
        for (var index = 0; index < source.Count; index++)
        {
            result.Values[index] = MapNumber(source.Values[index], context, map);
        }

        return result;
    }

    private static CellValue MapNumber(CellValue value, EvaluationContext context, Func<double, double> map)
    {
        if (!Coercion.TryNumber(value, context.Locale, context.Date1904, out var number, out var error))
        {
            return CellValue.FromError(error);
        }

        return CellValue.FromNumber(map(number));
    }

    private static Operand EvaluateBinary(BinaryNode binary, EvaluationContext context)
    {
        switch (binary.Operator)
        {
            case BinaryOperator.Range:
                return ReferenceOperators.Range(Evaluate(binary.Left, context), Evaluate(binary.Right, context));
            case BinaryOperator.Intersect:
                return ReferenceOperators.Intersect(Evaluate(binary.Left, context), Evaluate(binary.Right, context));
            case BinaryOperator.Union:
                return ReferenceOperators.Union(Evaluate(binary.Left, context), Evaluate(binary.Right, context));
        }

        var left = Prepare(Evaluate(binary.Left, context), context);
        var right = Prepare(Evaluate(binary.Right, context), context);
        if (left.Kind == OperandKind.Value && right.Kind == OperandKind.Value)
        {
            return Apply(binary.Operator, left.Value, right.Value, context);
        }

        return Elementwise(left, right, context, (a, b) => Apply(binary.Operator, a, b, context));
    }

    /// <summary>Applies a scalar function over two operands with broadcasting (one row/column/value repeats; outside → #N/A).</summary>
    public static Operand Elementwise(Operand left, Operand right, EvaluationContext context, Func<CellValue, CellValue, CellValue> apply)
    {
        if (left.IsError && right.Kind != OperandKind.Value)
        {
            // An error with an array still maps element by element.
        }

        var leftArray = left.Kind == OperandKind.Array ? left.Array : ArrayValue.Scalar(left.Kind == OperandKind.Value ? left.Value : CellValue.FromError(CellError.Value));
        var rightArray = right.Kind == OperandKind.Array ? right.Array : ArrayValue.Scalar(right.Kind == OperandKind.Value ? right.Value : CellValue.FromError(CellError.Value));
        var rows = Dimension(leftArray.Rows, rightArray.Rows);
        var columns = Dimension(leftArray.Columns, rightArray.Columns);
        var result = new ArrayValue(rows, columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                result[row, column] = apply(leftArray.Broadcast(row, column), rightArray.Broadcast(row, column));
            }
        }

        context.UsedArrays = true;
        return result;
    }

    private static int Dimension(int first, int second) => first == 1 ? second : second == 1 ? first : Math.Max(first, second);

    public static CellValue Apply(BinaryOperator op, CellValue left, CellValue right, EvaluationContext context)
    {
        switch (op)
        {
            case BinaryOperator.Concatenate:
                if (left.IsError)
                {
                    return left;
                }

                if (right.IsError)
                {
                    return right;
                }

                var text = Coercion.ToText(left, context.Locale) + Coercion.ToText(right, context.Locale);
                return text.Length > 32767 ? CellValue.FromError(CellError.Value) : CellValue.FromText(text);
            case BinaryOperator.Equal:
            case BinaryOperator.NotEqual:
            case BinaryOperator.Less:
            case BinaryOperator.LessOrEqual:
            case BinaryOperator.Greater:
            case BinaryOperator.GreaterOrEqual:
                if (left.IsError)
                {
                    return left;
                }

                if (right.IsError)
                {
                    return right;
                }

                var comparison = ValueComparer.Compare(left, right);
                return CellValue.FromBoolean(op switch
                {
                    BinaryOperator.Equal => comparison == 0,
                    BinaryOperator.NotEqual => comparison != 0,
                    BinaryOperator.Less => comparison < 0,
                    BinaryOperator.LessOrEqual => comparison <= 0,
                    BinaryOperator.Greater => comparison > 0,
                    _ => comparison >= 0,
                });
        }

        if (!Coercion.TryNumber(left, context.Locale, context.Date1904, out var a, out var leftError))
        {
            return CellValue.FromError(leftError);
        }

        if (!Coercion.TryNumber(right, context.Locale, context.Date1904, out var b, out var rightError))
        {
            return CellValue.FromError(rightError);
        }

        return op switch
        {
            BinaryOperator.Add => CellValue.FromNumber(a + b),
            BinaryOperator.Subtract => CellValue.FromNumber(a - b),
            BinaryOperator.Multiply => CellValue.FromNumber(a * b),
            BinaryOperator.Divide => b == 0 ? CellValue.FromError(CellError.DivisionByZero) : CellValue.FromNumber(a / b),
            BinaryOperator.Power => Power(a, b),
            _ => CellValue.FromError(CellError.Value),
        };
    }

    public static CellValue Power(double a, double b)
    {
        if (a == 0 && b == 0)
        {
            return CellValue.FromError(CellError.Number);
        }

        if (a == 0 && b < 0)
        {
            return CellValue.FromError(CellError.DivisionByZero);
        }

        if (a < 0 && b != Math.Floor(b))
        {
            // Odd roots of negative numbers (=-8^(1/3)) are defined in Excel.
            var inverse = 1 / b;
            if (Math.Abs(inverse - Math.Round(inverse)) < 1e-10 && ((long)Math.Round(inverse)) % 2 != 0)
            {
                return CellValue.FromNumber(-Math.Pow(-a, b));
            }

            return CellValue.FromError(CellError.Number);
        }

        return CellValue.FromNumber(Math.Pow(a, b));
    }

    // ---------------------------------------------------------------- functions

    public static Operand InvokeFunction(FunctionNode node, EvaluationContext context)
    {
        if (node.Callee is not null)
        {
            var callee = Evaluate(node.Callee, context);
            return callee.Kind == OperandKind.Lambda ? CallLambda(callee.Lambda, node.Arguments, context) : Operand.Error(CellError.Value);
        }

        var definition = node.Function;
        if (definition is null)
        {
            // A defined name holding a LAMBDA can be called like a function.
            var name = context.Workbook.FindName(node.Name, context.Sheet);
            if (name is not null && Evaluate(new NameNode(name.Name), context) is { Kind: OperandKind.Lambda } lambda)
            {
                return CallLambda(lambda.Lambda, node.Arguments, context);
            }

            if (context.Scope is { } scope && scope.TryFind(node.Name, out var variable) && variable.Kind == OperandKind.Lambda)
            {
                return CallLambda(variable.Lambda, node.Arguments, context);
            }

            return Operand.Error(CellError.Name);
        }

        if (node.Arguments.Length < definition.MinArguments || node.Arguments.Length > definition.MaxArguments)
        {
            return Operand.Error(CellError.Value);
        }

        context.Engine.CancellationToken.ThrowIfCancellationRequested();
        var call = new FunctionCall(definition, node, context);
        if (definition.IsLazy)
        {
            return Run(call);
        }

        var lift = false;
        for (var index = 0; index < node.Arguments.Length; index++)
        {
            var kind = definition.KindOf(index);
            var value = EvaluateArgument(node.Arguments[index], kind, context);
            call.SetArgument(index, value);
            if (kind == ArgumentKind.Value && value.Kind == OperandKind.Array)
            {
                lift = true;
            }
        }

        return lift ? Lift(call, context) : Run(call);
    }

    /// <summary>Evaluates an argument for a parameter of <paramref name="kind"/>.</summary>
    public static Operand EvaluateArgument(FormulaNode node, ArgumentKind kind, EvaluationContext context)
    {
        if (node is MissingArgumentNode)
        {
            return CellValue.Blank;
        }

        switch (kind)
        {
            case ArgumentKind.Value:
            {
                var value = Evaluate(node, context);
                if (value.Kind == OperandKind.Reference)
                {
                    var reference = value.Reference;
                    if (reference.IsSingleCell)
                    {
                        return context.Engine.GetValue(reference.First.Sheet, reference.First.Range.FirstRow, reference.First.Range.FirstColumn);
                    }

                    if (context.ArrayMode)
                    {
                        context.UsedArrays = true;
                        return Materialize(reference, context);
                    }

                    return ImplicitIntersection(reference, context);
                }

                if (value.Kind == OperandKind.Array)
                {
                    if (value.Array.Count == 1)
                    {
                        return value.Array.Values[0];
                    }

                    if (!context.ArrayMode)
                    {
                        return value.Array.Values[0];
                    }
                }

                return value;
            }

            case ArgumentKind.Array:
            {
                var saved = context.ArrayMode;
                context.ArrayMode = true;
                try
                {
                    return Evaluate(node, context);
                }
                finally
                {
                    context.ArrayMode = saved;
                }
            }

            default:
                return Evaluate(node, context);
        }
    }

    private static Operand Run(FunctionCall call)
    {
        try
        {
            return call.Definition.Body(call);
        }
        catch (CalcErrorException error) when (error is not UnavailableFunctionException)
        {
            return Operand.Error(error.Error);
        }
        catch (ArgumentOutOfRangeException)
        {
            return Operand.Error(CellError.Value);
        }
        catch (OverflowException)
        {
            return Operand.Error(CellError.Number);
        }
    }

    /// <summary>Applies a function element by element where single-value parameters got arrays (=LEN(A1:A3)).</summary>
    private static Operand Lift(FunctionCall call, EvaluationContext context)
    {
        context.UsedArrays = true;
        var rows = 1;
        var columns = 1;
        for (var index = 0; index < call.Count; index++)
        {
            if (call.Definition.KindOf(index) == ArgumentKind.Value && call.RawArgument(index) is { Kind: OperandKind.Array } array)
            {
                rows = Dimension(rows, array.Array.Rows);
                columns = Dimension(columns, array.Array.Columns);
            }
        }

        var result = new ArrayValue(rows, columns);
        for (var row = 0; row < rows; row++)
        {
            for (var column = 0; column < columns; column++)
            {
                var element = call.ElementCall(row, column);
                var value = Run(element);
                result[row, column] = ToScalar(value.Kind == OperandKind.Reference ? Prepare(value, context) : value, context);
            }
        }

        return result;
    }

    /// <summary>Calls a LAMBDA with argument expressions (evaluated here, in the caller's scope).</summary>
    public static Operand CallLambda(LambdaValue lambda, ImmutableArray<FormulaNode> arguments, EvaluationContext context)
    {
        var values = new Operand[arguments.Length];
        for (var index = 0; index < arguments.Length; index++)
        {
            values[index] = arguments[index] is MissingArgumentNode ? Operand.From(CellValue.Blank) : Evaluate(arguments[index], context);
        }

        return CallLambda(lambda, values, context, arguments.Select(argument => argument is MissingArgumentNode).ToArray());
    }

    public static Operand CallLambda(LambdaValue lambda, IReadOnlyList<Operand> values, EvaluationContext context, bool[]? missing = null)
    {
        if (values.Count > lambda.Parameters.Length)
        {
            return Operand.Error(CellError.Value);
        }

        if (context.Depth > MaxLambdaDepth)
        {
            return Operand.Error(CellError.Calc);
        }

        var scope = lambda.Scope;
        for (var index = 0; index < lambda.Parameters.Length; index++)
        {
            var isMissing = index >= values.Count || (missing is not null && index < missing.Length && missing[index]);
            var value = isMissing ? Operand.From(CellValue.Blank) : values[index];
            scope = new Scope(lambda.Parameters[index], value, scope);
            if (isMissing)
            {
                scope = new Scope("\u0001omitted:" + lambda.Parameters[index], CellValue.True, scope);
            }
        }

        var savedScope = context.Scope;
        context.Scope = scope;
        context.Depth++;
        try
        {
            return Evaluate(lambda.Body, context);
        }
        finally
        {
            context.Depth--;
            context.Scope = savedScope;
        }
    }

    /// <summary>The final value of a formula cell's evaluation: a reference becomes its value(s).</summary>
    public static Operand Finish(Operand result, EvaluationContext context)
    {
        switch (result.Kind)
        {
            case OperandKind.Reference:
                var reference = result.Reference;
                if (reference.IsSingleCell)
                {
                    var area = reference.First;
                    return context.Engine.GetValue(area.Sheet, area.Range.FirstRow, area.Range.FirstColumn);
                }

                if (context.ArrayMode)
                {
                    return Materialize(reference, context);
                }

                return ImplicitIntersection(reference, context);
            case OperandKind.Lambda:
                return Operand.Error(CellError.Calc);
            case OperandKind.Array when !context.ArrayMode:
                return result.Array.Count > 0 ? result.Array.Values[0] : CellValue.FromError(CellError.Value);
            default:
                return result;
        }
    }
}

/// <summary>The reference operators: range (:), intersection (space) and union (, / ;).</summary>
internal static class ReferenceOperators
{
    public static Operand Range(Operand left, Operand right)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (left.Kind != OperandKind.Reference || right.Kind != OperandKind.Reference)
        {
            return Operand.Error(CellError.Value);
        }

        var all = left.Reference.Areas.Concat(right.Reference.Areas).ToList();
        var sheet = all[0].Sheet;
        if (all.Any(area => area.Sheet != sheet))
        {
            return Operand.Error(CellError.Value);
        }

        var bounds = all.Skip(1).Aggregate(all[0].Range, (range, area) => range.Union(area.Range));
        return new ReferenceValue(sheet, bounds);
    }

    public static Operand Intersect(Operand left, Operand right)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (left.Kind != OperandKind.Reference || right.Kind != OperandKind.Reference)
        {
            return Operand.Error(CellError.Value);
        }

        var areas = new List<SheetArea>();
        foreach (var first in left.Reference.Areas)
        {
            foreach (var second in right.Reference.Areas)
            {
                if (first.Sheet == second.Sheet && first.Range.Intersect(second.Range) is { } common)
                {
                    areas.Add(new SheetArea(first.Sheet, common));
                }
            }
        }

        return areas.Count == 0 ? Operand.Error(CellError.Null) : new ReferenceValue(areas);
    }

    public static Operand Union(Operand left, Operand right)
    {
        if (left.IsError)
        {
            return left;
        }

        if (right.IsError)
        {
            return right;
        }

        if (left.Kind != OperandKind.Reference || right.Kind != OperandKind.Reference)
        {
            return Operand.Error(CellError.Value);
        }

        return new ReferenceValue(left.Reference.Areas.Concat(right.Reference.Areas).ToArray());
    }
}
