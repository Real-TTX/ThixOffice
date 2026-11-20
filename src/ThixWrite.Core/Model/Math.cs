namespace ThixWrite.Core.Model;

/// <summary>How the text of a formula is set (m:sty, m:nor): variables italic by default, "plain" upright, normal text.</summary>
public enum MathStyle
{
    Italic,
    Plain,
    Bold,
    BoldItalic,
    NormalText,
}

/// <summary>
/// A formula (OMML: m:oMath / m:oMathPara) as ThixWrite SHOWS it – read-only; the formula itself stays as its XML
/// (<see cref="OpaqueInline.Xml"/>) and is written back unchanged. Every node is a box the math layout sets: text, rows, fractions,
/// scripts, radicals, big operators, delimiters, matrices, accents, bars, functions, limits, boxes, equation arrays.
/// </summary>
public abstract record MathNode;

/// <summary>Text of a formula (m:r / m:t).</summary>
public sealed record MathText(string Text, MathStyle Style = MathStyle.Italic) : MathNode;

/// <summary>Boxes side by side (m:oMath, m:e, m:num …).</summary>
public sealed record MathRow(EquatableArray<MathNode> Items) : MathNode
{
    public static readonly MathRow Empty = new(EquatableArray<MathNode>.Empty);
}

/// <summary>A fraction (m:f): "bar" (stacked with a line), "noBar" (stacked), "lin" / "skw" (in a line with a slash).</summary>
public sealed record MathFraction(MathNode Numerator, MathNode Denominator, string Type = "bar") : MathNode;

/// <summary>Sub- and superscripts (m:sSub, m:sSup, m:sSubSup; <see cref="Pre"/>: m:sPre, before the base).</summary>
public sealed record MathScripts(MathNode Base, MathNode? Sub, MathNode? Sup, bool Pre = false) : MathNode;

/// <summary>A radical (m:rad) with an optional degree.</summary>
public sealed record MathRadical(MathNode Base, MathNode? Degree) : MathNode;

/// <summary>A big operator (m:nary: ∑, ∫, ∏ …) with limits under/over it (<see cref="Limits"/>) or as scripts, and its operand.</summary>
public sealed record MathNary(string Operator, MathNode? Sub, MathNode? Sup, MathNode Base, bool Limits) : MathNode;

/// <summary>Delimiters (m:d) around elements separated by <see cref="Separator"/>; an empty string draws nothing.</summary>
public sealed record MathDelimiter(string Begin, string End, string Separator, EquatableArray<MathNode> Items) : MathNode;

/// <summary>A matrix (m:m): rows of cells.</summary>
public sealed record MathMatrix(EquatableArray<MathRow> Rows) : MathNode;

/// <summary>An accent over its base (m:acc: hat, bar, dot, tilde, vector arrow …).</summary>
public sealed record MathAccent(string Accent, MathNode Base) : MathNode;

/// <summary>A line over or under the base (m:bar).</summary>
public sealed record MathBar(MathNode Base, bool Top) : MathNode;

/// <summary>A grouping character (m:groupChr: an under-/overbrace) with its base.</summary>
public sealed record MathGroupChar(string Character, MathNode Base, bool Top) : MathNode;

/// <summary>A function (m:func): its name, upright, and its argument.</summary>
public sealed record MathFunction(MathNode Name, MathNode Argument) : MathNode;

/// <summary>A limit under or over its base (m:limLow / m:limUpp).</summary>
public sealed record MathLimit(MathNode Base, MathNode Limit, bool Upper) : MathNode;

/// <summary>A frame around its base (m:borderBox).</summary>
public sealed record MathBorderBox(MathNode Base) : MathNode;

/// <summary>Equations under each other (m:eqArr).</summary>
public sealed record MathEquationArray(EquatableArray<MathNode> Rows) : MathNode;
