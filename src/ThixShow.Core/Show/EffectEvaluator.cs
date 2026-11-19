using System.Globalization;
using ThixShow.Core.Model;

namespace ThixShow.Core.Show;

/// <summary>Which side a wipe reveals from (or towards).</summary>
public enum WipeSide
{
    Left,
    Right,
    Top,
    Bottom,
    CenterHorizontal,
    CenterVertical,
}

/// <summary>
/// The state of a shape at a moment of the show: visible, opacity, offset (points), scale and rotation about its centre, and a
/// reveal (wipe) – the fraction of its box shown from a side.
/// </summary>
public readonly record struct ShapeState(
    bool Visible,
    double Opacity,
    double OffsetX,
    double OffsetY,
    double ScaleX,
    double ScaleY,
    double Rotation,
    WipeSide? RevealSide,
    double Revealed)
{
    public static ShapeState Shown { get; } = new(true, 1, 0, 0, 1, 1, 0, null, 1);

    public static ShapeState HiddenState { get; } = Shown with { Visible = false };

    public bool IsIdentity => Visible && Opacity >= 1 && OffsetX == 0 && OffsetY == 0 && ScaleX == 1 && ScaleY == 1 && Rotation == 0 && RevealSide is null;
}

/// <summary>A shape's box on the slide (points) – what the formulas #ppt_x, #ppt_y, #ppt_w, #ppt_h are relative to.</summary>
public readonly record struct ShapeBox(double X, double Y, double Width, double Height, double SlideWidth, double SlideHeight)
{
    public double CenterXFraction => (X + (Width / 2)) / SlideWidth;

    public double CenterYFraction => (Y + (Height / 2)) / SlideHeight;

    public double WidthFraction => Width / SlideWidth;

    public double HeightFraction => Height / SlideHeight;
}

/// <summary>
/// Plays the behaviours of effects (ECMA-376 timing behaviours) onto a shape's state: visibility sets, filter transitions (fade;
/// wipe from a side; barn/split from the centre; anything else falls back to fade), property animations of ppt_x/ppt_y/ppt_w/ppt_h,
/// rotation and opacity with keyframe formulas, scale, rotation and motion paths, with acceleration/deceleration and auto-reverse.
/// Deterministic and UI-free.
/// </summary>
public static class EffectEvaluator
{
    /// <summary>Applies <paramref name="effect"/> at <paramref name="milliseconds"/> after its start (after its end: its end state).</summary>
    public static ShapeState Apply(ShapeState state, AnimationEffect effect, double milliseconds, ShapeBox box)
    {
        foreach (var behavior in effect.Behaviors)
        {
            var local = milliseconds - behavior.DelayMs;
            if (local < 0)
            {
                continue;
            }

            var progress = Progress(behavior, local);
            state = ApplyBehavior(state, behavior, progress, box, local);
        }

        return state;
    }

    private static double Progress(AnimationBehavior behavior, double local)
    {
        if (behavior.DurationMs <= 1)
        {
            return 1;
        }

        double raw;
        if (behavior.AutoReverse)
        {
            var total = local / behavior.DurationMs;
            raw = total >= 2 ? 0 : total <= 1 ? total : 2 - total;
        }
        else
        {
            raw = Math.Clamp(local / behavior.DurationMs, 0, 1);
        }

        return Ease(raw, behavior.Acceleration / 100000.0, behavior.Deceleration / 100000.0);
    }

    /// <summary>SMIL's acceleration/deceleration: start slowly over the first <paramref name="accelerate"/>, end slowly over the last <paramref name="decelerate"/>.</summary>
    public static double Ease(double progress, double accelerate, double decelerate)
    {
        if (accelerate <= 0 && decelerate <= 0)
        {
            return progress;
        }

        if (accelerate + decelerate > 1)
        {
            var scale = 1 / (accelerate + decelerate);
            accelerate *= scale;
            decelerate *= scale;
        }

        var rate = 1 / (1 - (accelerate / 2) - (decelerate / 2));
        if (progress < accelerate)
        {
            return rate * progress * progress / (2 * accelerate);
        }

        if (progress <= 1 - decelerate)
        {
            return rate * (progress - (accelerate / 2));
        }

        var tail = progress - (1 - decelerate);
        return rate * (progress - (accelerate / 2) - (tail * tail / (2 * decelerate)));
    }

    private static ShapeState ApplyBehavior(ShapeState state, AnimationBehavior behavior, double progress, ShapeBox box, double local)
    {
        switch (behavior)
        {
            case VisibilityBehavior visibility:
                return state with { Visible = visibility.Visible };
            case FilterBehavior filter:
                return Filter(state, filter, progress);
            case PropertyBehavior property:
                return Property(state, property, progress, box);
            case ScaleBehavior scale:
                {
                    var fromX = (scale.FromX ?? 100000) / 100000.0;
                    var fromY = (scale.FromY ?? 100000) / 100000.0;
                    var toX = scale.ToX is { } tx ? tx / 100000.0 : fromX * ((scale.ByX ?? 100000) / 100000.0);
                    var toY = scale.ToY is { } ty ? ty / 100000.0 : fromY * ((scale.ByY ?? 100000) / 100000.0);
                    return state with { ScaleX = state.ScaleX * Lerp(fromX, toX, progress), ScaleY = state.ScaleY * Lerp(fromY, toY, progress) };
                }

            case RotateBehavior rotate:
                return state with { Rotation = state.Rotation + (rotate.By / 60000.0 * progress) };
            case MotionBehavior motion:
                {
                    var (x, y) = MotionPoint(motion.Path, progress);
                    return state with { OffsetX = state.OffsetX + (x * box.SlideWidth), OffsetY = state.OffsetY + (y * box.SlideHeight) };
                }

            default:
                return state;
        }
    }

    private static ShapeState Filter(ShapeState state, FilterBehavior filter, double progress)
    {
        var amount = filter.In ? progress : 1 - progress;
        var name = filter.Filter;
        var side = name switch
        {
            "wipe(down)" => WipeSide.Bottom,
            "wipe(up)" => WipeSide.Top,
            "wipe(left)" => WipeSide.Left,
            "wipe(right)" => WipeSide.Right,
            _ when name.StartsWith("barn(", StringComparison.Ordinal) && name.Contains("Vertical", StringComparison.Ordinal) => WipeSide.CenterVertical,
            _ when name.StartsWith("barn(", StringComparison.Ordinal) => WipeSide.CenterHorizontal,
            _ => (WipeSide?)null,
        };
        if (side is { } reveal)
        {
            return state with { RevealSide = reveal, Revealed = Math.Clamp(amount, 0, 1), Visible = state.Visible || filter.In };
        }

        // fade, and every filter ThixShow does not draw (wheel, circle, dissolve …), as a fade.
        return state with { Opacity = Math.Clamp(amount, 0, 1), Visible = state.Visible || filter.In };
    }

    private static ShapeState Property(ShapeState state, PropertyBehavior property, double progress, ShapeBox box)
    {
        var variables = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["#ppt_x"] = box.CenterXFraction,
            ["#ppt_y"] = box.CenterYFraction,
            ["#ppt_w"] = box.WidthFraction,
            ["#ppt_h"] = box.HeightFraction,
            ["ppt_x"] = box.CenterXFraction,
            ["ppt_y"] = box.CenterYFraction,
            ["ppt_w"] = box.WidthFraction,
            ["ppt_h"] = box.HeightFraction,
            ["$"] = progress,
        };
        double? value;
        if (property.CalcMode == "set")
        {
            value = Formula(property.To ?? "0", variables);
        }
        else if (!property.Keys.IsEmpty)
        {
            value = KeyValue(property.Keys, progress, variables);
        }
        else
        {
            var from = property.From is { } start ? Formula(start, variables) : Base(property.Attribute, box);
            var to = property.To is { } end ? Formula(end, variables) : property.By is { } by ? from + Formula(by, variables) : from;
            value = Lerp(from, to, progress);
        }

        if (value is not { } result)
        {
            return state;
        }

        return property.Attribute switch
        {
            "ppt_x" => state with { OffsetX = state.OffsetX + ((result - box.CenterXFraction) * box.SlideWidth) },
            "ppt_y" => state with { OffsetY = state.OffsetY + ((result - box.CenterYFraction) * box.SlideHeight) },
            "ppt_w" => state with { ScaleX = state.ScaleX * (box.WidthFraction > 0 ? result / box.WidthFraction : 1) },
            "ppt_h" => state with { ScaleY = state.ScaleY * (box.HeightFraction > 0 ? result / box.HeightFraction : 1) },
            "style.rotation" or "r" => state with { Rotation = state.Rotation + result },
            "style.opacity" => state with { Opacity = Math.Clamp(result, 0, 1) },
            _ => state,
        };
    }

    private static double Base(string attribute, ShapeBox box) => attribute switch
    {
        "ppt_x" => box.CenterXFraction,
        "ppt_y" => box.CenterYFraction,
        "ppt_w" => box.WidthFraction,
        "ppt_h" => box.HeightFraction,
        "style.opacity" => 1,
        _ => 0,
    };

    private static double KeyValue(ValueList<AnimationKey> keys, double progress, Dictionary<string, double> variables)
    {
        var time = progress * 100000;
        var after = keys.FirstOrDefault(key => key.Time >= time) ?? keys[^1];
        var index = keys.IndexOf(after);
        if (index <= 0 || after.Time <= time)
        {
            return KeyFormula(after, variables);
        }

        var before = keys[index - 1];
        var span = Math.Max(1, after.Time - before.Time);
        return Lerp(KeyFormula(before, variables), KeyFormula(after, variables), (time - before.Time) / span);
    }

    private static double KeyFormula(AnimationKey key, Dictionary<string, double> variables)
    {
        var value = Formula(key.Value, variables);
        if (key.Formula is { } formula)
        {
            variables["$"] = value;
            value = Formula(formula, variables);
        }

        return value;
    }

    private static double Lerp(double from, double to, double progress) => from + ((to - from) * progress);

    /// <summary>
    /// Evaluates an animation value formula ("0-#ppt_w/2", "#ppt_y+.1", "1+#ppt_h/2", "sin(6.28*$)"): numbers, the shape variables,
    /// + − * / ^, parentheses and the functions abs, sin, cos, tan, sqrt, min, max, rand (0).
    /// </summary>
    public static double Formula(string text, IReadOnlyDictionary<string, double> variables)
    {
        var parser = new FormulaParser(text, variables);
        return parser.Parse();
    }

    private sealed class FormulaParser(string text, IReadOnlyDictionary<string, double> variables)
    {
        private int _position;

        public double Parse()
        {
            var value = Sum();
            return double.IsFinite(value) ? value : 0;
        }

        private void Skip()
        {
            while (_position < text.Length && char.IsWhiteSpace(text[_position]))
            {
                _position++;
            }
        }

        private double Sum()
        {
            var value = Product();
            while (true)
            {
                Skip();
                if (_position >= text.Length || (text[_position] != '+' && text[_position] != '-'))
                {
                    return value;
                }

                var add = text[_position++] == '+';
                var right = Product();
                value = add ? value + right : value - right;
            }
        }

        private double Product()
        {
            var value = Power();
            while (true)
            {
                Skip();
                if (_position >= text.Length || (text[_position] != '*' && text[_position] != '/'))
                {
                    return value;
                }

                var multiply = text[_position++] == '*';
                var right = Power();
                value = multiply ? value * right : right == 0 ? 0 : value / right;
            }
        }

        private double Power()
        {
            var value = Unary();
            Skip();
            if (_position < text.Length && text[_position] == '^')
            {
                _position++;
                return Math.Pow(value, Unary());
            }

            return value;
        }

        private double Unary()
        {
            Skip();
            if (_position < text.Length && text[_position] == '-')
            {
                _position++;
                return -Unary();
            }

            if (_position < text.Length && text[_position] == '+')
            {
                _position++;
                return Unary();
            }

            return Atom();
        }

        private double Atom()
        {
            Skip();
            if (_position >= text.Length)
            {
                return 0;
            }

            var character = text[_position];
            if (character == '(')
            {
                _position++;
                var value = Sum();
                Skip();
                if (_position < text.Length && text[_position] == ')')
                {
                    _position++;
                }

                return value;
            }

            if (char.IsDigit(character) || character == '.')
            {
                var start = _position;
                while (_position < text.Length && (char.IsDigit(text[_position]) || text[_position] == '.' || text[_position] == 'e' || text[_position] == 'E'))
                {
                    _position++;
                }

                return double.TryParse(text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
            }

            var nameStart = _position;
            while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] is '#' or '_' or '$' or '.'))
            {
                _position++;
            }

            var name = text[nameStart.._position];
            if (name.Length == 0)
            {
                _position++;
                return 0;
            }

            Skip();
            if (_position < text.Length && text[_position] == '(')
            {
                _position++;
                var arguments = new List<double> { Sum() };
                Skip();
                while (_position < text.Length && text[_position] == ',')
                {
                    _position++;
                    arguments.Add(Sum());
                    Skip();
                }

                if (_position < text.Length && text[_position] == ')')
                {
                    _position++;
                }

                return name switch
                {
                    "abs" => Math.Abs(arguments[0]),
                    "sin" => Math.Sin(arguments[0]),
                    "cos" => Math.Cos(arguments[0]),
                    "tan" => Math.Tan(arguments[0]),
                    "sqrt" => Math.Sqrt(Math.Max(0, arguments[0])),
                    "ln" => arguments[0] > 0 ? Math.Log(arguments[0]) : 0,
                    "max" => arguments.Max(),
                    "min" => arguments.Min(),
                    _ => 0,
                };
            }

            return name switch
            {
                "pi" => Math.PI,
                "e" => Math.E,
                "rand" => 0,
                _ => variables.TryGetValue(name, out var variable) ? variable : 0,
            };
        }
    }

    /// <summary>
    /// The point of a motion path ("M 0 0 L 0.25 0.1 C … Z E", fractions of the slide relative to the shape's position) at
    /// <paramref name="progress"/> of its length.
    /// </summary>
    public static (double X, double Y) MotionPoint(string path, double progress)
    {
        var points = Flatten(path);
        if (points.Count == 0)
        {
            return (0, 0);
        }

        if (points.Count == 1)
        {
            return points[0];
        }

        var lengths = new double[points.Count];
        for (var index = 1; index < points.Count; index++)
        {
            var (dx, dy) = (points[index].X - points[index - 1].X, points[index].Y - points[index - 1].Y);
            lengths[index] = lengths[index - 1] + Math.Sqrt((dx * dx) + (dy * dy));
        }

        var target = lengths[^1] * Math.Clamp(progress, 0, 1);
        for (var index = 1; index < points.Count; index++)
        {
            if (lengths[index] >= target)
            {
                var span = lengths[index] - lengths[index - 1];
                var t = span <= 0 ? 1 : (target - lengths[index - 1]) / span;
                return (Lerp(points[index - 1].X, points[index].X, t), Lerp(points[index - 1].Y, points[index].Y, t));
            }
        }

        return points[^1];
    }

    private static List<(double X, double Y)> Flatten(string path)
    {
        var tokens = path.Split([' ', ','], StringSplitOptions.RemoveEmptyEntries);
        var points = new List<(double X, double Y)>();
        var command = 'M';
        var index = 0;
        (double X, double Y) current = (0, 0);
        (double X, double Y) start = (0, 0);
        double Number() => index < tokens.Length && double.TryParse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : 0;
        while (index < tokens.Length)
        {
            var token = tokens[index];
            if (token.Length == 1 && char.IsLetter(token[0]))
            {
                command = token[0];
                index++;
                if (command is 'Z' or 'z')
                {
                    current = start;
                    points.Add(current);
                    continue;
                }

                if (command is 'E' or 'e')
                {
                    break;
                }
            }

            var relative = char.IsLower(command);
            (double X, double Y) Point()
            {
                var (x, y) = (Number(), Number());
                return relative ? (current.X + x, current.Y + y) : (x, y);
            }

            switch (char.ToUpperInvariant(command))
            {
                case 'M':
                    current = start = Point();
                    points.Add(current);
                    break;
                case 'L':
                    current = Point();
                    points.Add(current);
                    break;
                case 'C':
                    {
                        var (c1, c2, end) = (Point(), Point(), Point());
                        var from = current;
                        for (var step = 1; step <= 16; step++)
                        {
                            var t = step / 16.0;
                            var u = 1 - t;
                            points.Add((
                                (u * u * u * from.X) + (3 * u * u * t * c1.X) + (3 * u * t * t * c2.X) + (t * t * t * end.X),
                                (u * u * u * from.Y) + (3 * u * u * t * c1.Y) + (3 * u * t * t * c2.Y) + (t * t * t * end.Y)));
                        }

                        current = end;
                        break;
                    }

                default:
                    index++;
                    break;
            }
        }

        return points;
    }
}
