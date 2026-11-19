using System.Collections.Immutable;
using ThixCalc.Core.Functions;

namespace ThixCalc.Core.Formulas;

/// <summary>
/// A parsed formula, position-independent (relative references are offsets), so cells filled from one formula share one
/// instance. Knows what the dependency graph needs: the static references and names (<see cref="Precedents"/>), whether it
/// is volatile (RAND, NOW, OFFSET, INDIRECT …) and whether it uses functions ThixCalc does not know.
/// The text is made for a position and a locale by <see cref="FormulaFormatter"/>.
/// </summary>
public sealed class Formula
{
    public Formula(FormulaNode root)
    {
        Root = root;
        var precedents = ImmutableArray.CreateBuilder<FormulaNode>();
        Analyze(root, precedents);
        Precedents = precedents.ToImmutable();
    }

    public FormulaNode Root { get; }

    /// <summary>The references, names and structured references the formula reads directly (not through functions like INDIRECT).</summary>
    public ImmutableArray<FormulaNode> Precedents { get; }

    /// <summary>Recalculated whenever anything is recalculated.</summary>
    public bool IsVolatile { get; private set; }

    /// <summary>Has references that only evaluation knows (INDIRECT, OFFSET, a range built with ":" from functions …).</summary>
    public bool HasDynamicReferences { get; private set; }

    /// <summary>Calls a function the registry does not have (#NAME? when recalculated; kept as written).</summary>
    public bool HasUnknownFunctions { get; private set; }

    /// <summary>Uses functions of dynamic arrays (FILTER, SORT, UNIQUE, SEQUENCE …) or LAMBDA.</summary>
    public bool UsesArrayFunctions { get; private set; }

    /// <summary>Is a constant value or a single reference (no functions or operators).</summary>
    public bool IsSimple => Root is NumberNode or TextNode or BooleanNode or ErrorNode or ReferenceNode;

    private void Analyze(FormulaNode node, ImmutableArray<FormulaNode>.Builder precedents)
    {
        switch (node)
        {
            case ReferenceNode or NameNode or StructuredReferenceNode:
                precedents.Add(node);
                break;
            case FunctionNode function:
                if (function.Function is null && function.Callee is null)
                {
                    HasUnknownFunctions = true;
                }
                else if (function.Function is { } definition)
                {
                    IsVolatile |= definition.IsVolatile;
                    HasDynamicReferences |= definition.ReturnsReference && definition.IsVolatile;
                    UsesArrayFunctions |= definition.IsArrayFunction;
                }

                if (function.Callee is not null)
                {
                    UsesArrayFunctions = true;
                    Analyze(function.Callee, precedents);
                }

                foreach (var argument in function.Arguments)
                {
                    Analyze(argument, precedents);
                }

                break;
            case UnaryNode unary:
                Analyze(unary.Operand, precedents);
                break;
            case PercentNode percent:
                Analyze(percent.Operand, precedents);
                break;
            case ParenthesesNode parentheses:
                Analyze(parentheses.Inner, precedents);
                break;
            case BinaryNode binary:
                if (binary.Operator == BinaryOperator.Range && (binary.Left is not ReferenceNode || binary.Right is not ReferenceNode))
                {
                    HasDynamicReferences = true;
                }

                Analyze(binary.Left, precedents);
                Analyze(binary.Right, precedents);
                break;
        }
    }
}
