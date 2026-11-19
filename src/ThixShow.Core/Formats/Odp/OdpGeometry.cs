using System.Globalization;
using System.Text;
using System.Xml.Linq;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

/// <summary>
/// Shape geometry between the model and ODF's draw:enhanced-geometry. Writing: the <see cref="GeometryEngine"/> evaluates the preset
/// or custom geometry for the shape's size and the paths become draw:enhanced-path (a preset also names itself as draw:type
/// "ooxml-&lt;preset&gt;" with its adjust values as draw:modifiers, the way LibreOffice keeps PowerPoint's shapes). Reading: "ooxml-…",
/// "rectangle" and "ellipse" become presets again; any other enhanced path (its equations, modifiers, arcs and quadrants evaluated)
/// becomes a custom geometry in DrawingML's a:custGeom form; LibreOffice's well-known shape types serve when there is no path.
/// draw:path / draw:polygon / draw:polyline (svg:d, draw:points) become custom geometries too.
/// </summary>
internal static class OdpGeometry
{
    private static readonly XNamespace A = "http://schemas.openxmlformats.org/drawingml/2006/main";

    /// <summary>LibreOffice's shape types whose preset is obvious (used when a shape has no enhanced path).</summary>
    private static readonly Dictionary<string, string> KnownTypes = new(StringComparer.Ordinal)
    {
        ["rectangle"] = "rect",
        ["rect"] = "rect",
        ["ellipse"] = "ellipse",
        ["round-rectangle"] = "roundRect",
        ["isosceles-triangle"] = "triangle",
        ["right-triangle"] = "rtTriangle",
        ["diamond"] = "diamond",
        ["pentagon"] = "pentagon",
        ["hexagon"] = "hexagon",
        ["octagon"] = "octagon",
        ["parallelogram"] = "parallelogram",
        ["trapezoid"] = "trapezoid",
        ["right-arrow"] = "rightArrow",
        ["left-arrow"] = "leftArrow",
        ["up-arrow"] = "upArrow",
        ["down-arrow"] = "downArrow",
        ["left-right-arrow"] = "leftRightArrow",
        ["up-down-arrow"] = "upDownArrow",
        ["star4"] = "star4",
        ["star5"] = "star5",
        ["star6"] = "star6",
        ["star8"] = "star8",
        ["heart"] = "heart",
        ["cloud"] = "cloud",
        ["smiley"] = "smileyFace",
        ["can"] = "can",
        ["cube"] = "cube",
        ["cross"] = "plus",
        ["moon"] = "moon",
        ["sun"] = "sun",
        ["frame"] = "frame",
        ["ring"] = "donut",
        ["block-arc"] = "blockArc",
        ["flowchart-process"] = "flowChartProcess",
        ["flowchart-decision"] = "flowChartDecision",
        ["flowchart-terminator"] = "flowChartTerminator",
        ["flowchart-data"] = "flowChartInputOutput",
    };

    // ================================================================ writing

    /// <summary>draw:enhanced-geometry for a shape of <paramref name="width"/> × <paramref name="height"/> EMU.</summary>
    public static XElement Write(ShapeGeometry? geometry, long width, long height, bool flipHorizontal, bool flipVertical)
    {
        geometry ??= ShapeGeometry.Rectangle;
        var (viewWidth, viewHeight) = ViewBoxOf(geometry, width, height);
        var evaluated = GeometryEngine.Evaluate(geometry, viewWidth, viewHeight);
        var element = new XElement(DrawNs + "enhanced-geometry",
            new XAttribute(SvgNs + "viewBox", $"0 0 {Format(viewWidth)} {Format(viewHeight)}"));
        if (flipHorizontal)
        {
            element.Add(new XAttribute(DrawNs + "mirror-horizontal", "true"));
        }

        if (flipVertical)
        {
            element.Add(new XAttribute(DrawNs + "mirror-vertical", "true"));
        }

        element.Add(new XAttribute(DrawNs + "text-areas", $"{Format(evaluated.TextLeft)} {Format(evaluated.TextTop)} {Format(evaluated.TextRight)} {Format(evaluated.TextBottom)}"));
        if (geometry.Custom is null && geometry.Preset is { } preset)
        {
            element.Add(new XAttribute(DrawNs + "type", preset switch
            {
                "rect" => "rectangle",
                "ellipse" => "ellipse",
                _ => "ooxml-" + preset,
            }));
            if (PresetGeometries.Get(preset) is { Adjusts.Count: > 0 } definition)
            {
                var values = definition.Adjusts.Select(adjust => geometry.AdjustOf(adjust.Name) ?? DefaultOf(adjust));
                element.Add(new XAttribute(DrawNs + "modifiers", string.Join(" ", values.Select(value => value.ToString(CultureInfo.InvariantCulture)))));
            }
        }
        else
        {
            element.Add(new XAttribute(DrawNs + "type", "non-primitive"));
        }

        element.Add(new XAttribute(DrawNs + "enhanced-path", PathText(evaluated.Paths)));
        return element;
    }

    /// <summary>The coordinate space a geometry is written in: a custom geometry's own (all paths alike), else the size in EMU.</summary>
    private static (double Width, double Height) ViewBoxOf(ShapeGeometry geometry, long width, long height)
    {
        if (geometry.Custom is not null && GeometryEngine.DefinitionOf(geometry) is { Paths.Count: > 0 } definition)
        {
            var first = definition.Paths[0];
            if (first.Width > 0 && first.Height > 0 && definition.Paths.All(path => path.Width == first.Width && path.Height == first.Height))
            {
                return (first.Width, first.Height);
            }
        }

        return (Math.Max(1, width), Math.Max(1, height));
    }

    private static long DefaultOf(GeometryGuide adjust)
    {
        var parts = adjust.Formula.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && long.TryParse(parts[1], NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) ? value : 0;
    }

    /// <summary>The enhanced path of evaluated paths: every path a sub-path ended by N, F/S when it is not filled/stroked.</summary>
    public static string PathText(IEnumerable<GeometryPath> paths)
    {
        var builder = new StringBuilder();
        foreach (var path in paths)
        {
            path.Path.Visit((verb, points) =>
            {
                builder.Append(verb switch
                {
                    PathVerb.MoveTo => "M",
                    PathVerb.LineTo => "L",
                    PathVerb.QuadTo => "Q",
                    PathVerb.CubicTo => "C",
                    _ => "Z",
                });
                foreach (var value in points)
                {
                    builder.Append(' ').Append(Format(value));
                }

                builder.Append(' ');
            });
            if (path.Fill == PathFillMode.None)
            {
                builder.Append("F ");
            }

            if (!path.Stroke)
            {
                builder.Append("S ");
            }

            builder.Append("N ");
        }

        return builder.ToString().TrimEnd();
    }

    private static string Format(double value) => Math.Round(value, 2).ToString("0.##", CultureInfo.InvariantCulture);

    // ================================================================ reading

    /// <summary>
    /// The geometry of a draw:custom-shape's draw:enhanced-geometry (null element = rectangle). <paramref name="known"/> is false when
    /// nothing could be made of it (then it is a rectangle).
    /// </summary>
    public static ShapeGeometry Read(XElement? element, double widthPoints, double heightPoints, out bool flipHorizontal, out bool flipVertical, out bool known)
    {
        flipHorizontal = (string?)element?.Attribute(DrawNs + "mirror-horizontal") == "true";
        flipVertical = (string?)element?.Attribute(DrawNs + "mirror-vertical") == "true";
        known = true;
        if (element is null)
        {
            return ShapeGeometry.Rectangle;
        }

        var type = (string?)element.Attribute(DrawNs + "type") ?? "";
        var modifiers = Numbers((string?)element.Attribute(DrawNs + "modifiers"));
        if (type.StartsWith("ooxml-", StringComparison.Ordinal) && PresetGeometries.Get(type[6..]) is { } definition)
        {
            var adjustments = new List<GeometryAdjust>();
            for (var index = 0; index < Math.Min(modifiers.Count, definition.Adjusts.Count); index++)
            {
                var value = (long)Math.Round(modifiers[index]);
                if (value != DefaultOf(definition.Adjusts[index]))
                {
                    adjustments.Add(new GeometryAdjust(definition.Adjusts[index].Name, value));
                }
            }

            return new ShapeGeometry(type[6..], adjustments);
        }

        if (type is "rectangle" or "rect" or "ellipse")
        {
            return ShapeGeometry.Of(KnownTypes[type]);
        }

        if ((string?)element.Attribute(DrawNs + "enhanced-path") is { Length: > 0 } pathText)
        {
            try
            {
                var custom = EnhancedPath(element, pathText, modifiers, widthPoints, heightPoints);
                if (custom is not null)
                {
                    return custom;
                }
            }
            catch (FormatException)
            {
                // an expression ThixShow cannot evaluate: the shape's type is the fallback
            }
        }

        if (KnownTypes.TryGetValue(type, out var preset))
        {
            return ShapeGeometry.Of(preset);
        }

        known = false;
        return ShapeGeometry.Rectangle;
    }

    private static ShapeGeometry? EnhancedPath(XElement element, string pathText, List<double> modifiers, double widthPoints, double heightPoints)
    {
        var box = Numbers((string?)element.Attribute(SvgNs + "viewBox"));
        var (left, top, width, height) = box.Count == 4 ? (box[0], box[1], box[2], box[3]) : (0, 0, 21600, 21600);
        if (width <= 0 || height <= 0)
        {
            (width, height) = (21600, 21600);
        }

        var formulas = new Formulas(element, modifiers, left, top, width, height, widthPoints * 2540 / 72, heightPoints * 2540 / 72);
        var paths = new PathInterpreter(formulas).Run(pathText);
        if (paths.Count == 0)
        {
            return null;
        }

        (double, double, double, double)? textRect = null;
        if ((string?)element.Attribute(DrawNs + "text-areas") is { Length: > 0 } areas)
        {
            var values = Tokens(areas).Take(4).Select(formulas.Parameter).ToList();
            if (values.Count == 4)
            {
                textRect = (values[0], values[1], values[2], values[3]);
            }
        }

        var shifted = paths.Select(path => path with { Path = path.Path.Transform(Matrix2D.Translation(-left, -top)) }).ToList();
        var rect = textRect is { } r ? (r.Item1 - left, r.Item2 - top, r.Item3 - left, r.Item4 - top) : ((double, double, double, double)?)null;
        return CustomGeometry(shifted, width, height, rect);
    }

    /// <summary>A sub-path of a read geometry: its path in view box units, filled, stroked.</summary>
    public sealed record SubPath(VectorPath Path, bool Fill, bool Stroke);

    /// <summary>
    /// A custom geometry (a:custGeom) of paths in a <paramref name="width"/> × <paramref name="height"/> space, coordinates rounded to
    /// whole units; <paramref name="textRect"/> the text rectangle (null: the whole shape).
    /// </summary>
    public static ShapeGeometry CustomGeometry(IReadOnlyList<SubPath> paths, double width, double height, (double Left, double Top, double Right, double Bottom)? textRect = null)
    {
        // Small spaces (an svg:viewBox of a few units) are scaled up so that whole-number coordinates keep the shape.
        var scale = Math.Max(width, height) < 10000 ? 10000 / Math.Max(1e-9, Math.Max(width, height)) : 1;
        var w = Math.Max(1, (long)Math.Round(width * scale));
        var h = Math.Max(1, (long)Math.Round(height * scale));
        string N(double value) => ((long)Math.Round(value * scale)).ToString(CultureInfo.InvariantCulture);

        var pathList = new XElement(A + "pathLst");
        foreach (var subPath in paths)
        {
            var path = new XElement(A + "path", new XAttribute("w", w), new XAttribute("h", h));
            if (!subPath.Fill)
            {
                path.Add(new XAttribute("fill", "none"));
            }

            if (!subPath.Stroke)
            {
                path.Add(new XAttribute("stroke", "0"));
            }

            subPath.Path.Visit((verb, points) =>
            {
                var name = verb switch
                {
                    PathVerb.MoveTo => "moveTo",
                    PathVerb.LineTo => "lnTo",
                    PathVerb.QuadTo => "quadBezTo",
                    PathVerb.CubicTo => "cubicBezTo",
                    _ => "close",
                };
                var command = new XElement(A + name);
                for (var index = 0; index + 1 < points.Length; index += 2)
                {
                    command.Add(new XElement(A + "pt", new XAttribute("x", N(points[index])), new XAttribute("y", N(points[index + 1]))));
                }

                path.Add(command);
            });
            pathList.Add(path);
        }

        var rect = textRect is { } r
            ? new XElement(A + "rect", new XAttribute("l", N(r.Left)), new XAttribute("t", N(r.Top)), new XAttribute("r", N(r.Right)), new XAttribute("b", N(r.Bottom)))
            : new XElement(A + "rect", new XAttribute("l", "l"), new XAttribute("t", "t"), new XAttribute("r", "r"), new XAttribute("b", "b"));
        var custom = new XElement(A + "custGeom", new XAttribute(XNamespace.Xmlns + "a", A),
            new XElement(A + "avLst"), new XElement(A + "gdLst"), new XElement(A + "ahLst"), new XElement(A + "cxnLst"), rect, pathList);
        return new ShapeGeometry(null, Custom: XmlFragment.From(custom));
    }

    /// <summary>The geometry of an svg:d path (draw:path) in its svg:viewBox (default: the shape's size in 1/100 mm).</summary>
    public static ShapeGeometry? SvgPath(string? data, string? viewBox, double widthPoints, double heightPoints)
    {
        if (string.IsNullOrWhiteSpace(data))
        {
            return null;
        }

        var (left, top, width, height) = ViewBox(viewBox, widthPoints, heightPoints);
        var paths = SvgPathParser.Parse(data);
        if (paths.Count == 0)
        {
            return null;
        }

        return CustomGeometry(paths.Select(path => new SubPath(path.Transform(Matrix2D.Translation(-left, -top)), true, true)).ToList(), width, height);
    }

    /// <summary>The geometry of a draw:polygon (closed) or draw:polyline's draw:points.</summary>
    public static ShapeGeometry? Polygon(string? points, string? viewBox, double widthPoints, double heightPoints, bool closed)
    {
        var values = Numbers(points);
        if (values.Count < 4)
        {
            return null;
        }

        var (left, top, width, height) = ViewBox(viewBox, widthPoints, heightPoints);
        var builder = new VectorPathBuilder().MoveTo(values[0] - left, values[1] - top);
        for (var index = 2; index + 1 < values.Count; index += 2)
        {
            builder.LineTo(values[index] - left, values[index + 1] - top);
        }

        if (closed)
        {
            builder.Close();
        }

        return CustomGeometry([new SubPath(builder.Build(), closed, true)], width, height);
    }

    private static (double Left, double Top, double Width, double Height) ViewBox(string? viewBox, double widthPoints, double heightPoints)
    {
        var box = Numbers(viewBox);
        return box.Count == 4 && box[2] > 0 && box[3] > 0
            ? (box[0], box[1], box[2], box[3])
            : (0, 0, Math.Max(1, widthPoints * 2540 / 72), Math.Max(1, heightPoints * 2540 / 72));
    }

    private static List<double> Numbers(string? text)
    {
        var values = new List<double>();
        foreach (var token in (text ?? "").Split([' ', ',', '\t', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries))
        {
            if (double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value))
            {
                values.Add(value);
            }
        }

        return values;
    }

    /// <summary>The tokens of an enhanced path: command letters, numbers, ?equation and $modifier references, named constants.</summary>
    private static List<string> Tokens(string text)
    {
        var tokens = new List<string>();
        var index = 0;
        while (index < text.Length)
        {
            var character = text[index];
            if (char.IsWhiteSpace(character) || character == ',')
            {
                index++;
                continue;
            }

            var start = index;
            if (character is >= 'A' and <= 'Z')
            {
                tokens.Add(character.ToString());
                index++;
                continue;
            }

            if (character is '?' or '$')
            {
                index++;
                while (index < text.Length && (char.IsLetterOrDigit(text[index]) || text[index] == '_'))
                {
                    index++;
                }
            }
            else if (char.IsLetter(character))
            {
                while (index < text.Length && char.IsLetter(text[index]))
                {
                    index++;
                }
            }
            else
            {
                index++;
                while (index < text.Length && (char.IsDigit(text[index]) || text[index] == '.' || ((text[index] == '-' || text[index] == '+') && (text[index - 1] == 'e' || text[index - 1] == 'E')) || text[index] == 'e' || text[index] == 'E'))
                {
                    index++;
                }
            }

            tokens.Add(text[start..index]);
        }

        return tokens;
    }

    /// <summary>The equations, modifiers and constants of an enhanced geometry (ODF 1.3, 19.171 draw:formula).</summary>
    private sealed class Formulas
    {
        private readonly Dictionary<string, string> _equations = new(StringComparer.Ordinal);
        private readonly Dictionary<string, double> _values = new(StringComparer.Ordinal);
        private readonly HashSet<string> _evaluating = new(StringComparer.Ordinal);
        private readonly List<double> _modifiers;
        private readonly Dictionary<string, double> _constants;

        public Formulas(XElement geometry, List<double> modifiers, double left, double top, double width, double height, double logWidth, double logHeight)
        {
            _modifiers = modifiers;
            foreach (var equation in geometry.Elements(DrawNs + "equation"))
            {
                if ((string?)equation.Attribute(DrawNs + "name") is { } name)
                {
                    _equations[name] = (string?)equation.Attribute(DrawNs + "formula") ?? "0";
                }
            }

            _constants = new Dictionary<string, double>(StringComparer.Ordinal)
            {
                ["left"] = left,
                ["top"] = top,
                ["right"] = left + width,
                ["bottom"] = top + height,
                ["width"] = width,
                ["height"] = height,
                ["xstretch"] = 0,
                ["ystretch"] = 0,
                ["hasstroke"] = 1,
                ["hasfill"] = 1,
                ["logwidth"] = logWidth,
                ["logheight"] = logHeight,
                ["pi"] = Math.PI,
            };
        }

        /// <summary>The value of a path parameter: a number, ?equation, $modifier or constant.</summary>
        public double Parameter(string token)
        {
            if (token.StartsWith('?'))
            {
                return Equation(token[1..]);
            }

            if (token.StartsWith('$'))
            {
                return int.TryParse(token[1..], NumberStyles.Integer, CultureInfo.InvariantCulture, out var index) && index >= 0 && index < _modifiers.Count ? _modifiers[index] : 0;
            }

            if (_constants.TryGetValue(token, out var constant))
            {
                return constant;
            }

            return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : throw new FormatException(token);
        }

        private double Equation(string name)
        {
            if (_values.TryGetValue(name, out var value))
            {
                return value;
            }

            if (!_equations.TryGetValue(name, out var formula) || !_evaluating.Add(name))
            {
                throw new FormatException(name);
            }

            value = new Expression(formula, this).Evaluate();
            _evaluating.Remove(name);
            _values[name] = value;
            return value;
        }

        /// <summary>A recursive descent evaluator of draw:formula: + - * /, unary minus, parentheses and ODF's functions.</summary>
        private sealed class Expression(string text, Formulas formulas)
        {
            private int _position;

            public double Evaluate()
            {
                var value = Additive();
                SkipSpace();
                return _position == text.Length ? value : throw new FormatException(text);
            }

            private double Additive()
            {
                var value = Multiplicative();
                while (true)
                {
                    SkipSpace();
                    if (Accept('+'))
                    {
                        value += Multiplicative();
                    }
                    else if (Accept('-'))
                    {
                        value -= Multiplicative();
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double Multiplicative()
            {
                var value = Unary();
                while (true)
                {
                    SkipSpace();
                    if (Accept('*'))
                    {
                        value *= Unary();
                    }
                    else if (Accept('/'))
                    {
                        var divisor = Unary();
                        value = divisor == 0 ? 0 : value / divisor;
                    }
                    else
                    {
                        return value;
                    }
                }
            }

            private double Unary()
            {
                SkipSpace();
                if (Accept('-'))
                {
                    return -Unary();
                }

                if (Accept('+'))
                {
                    return Unary();
                }

                return Primary();
            }

            private double Primary()
            {
                SkipSpace();
                if (Accept('('))
                {
                    var value = Additive();
                    Expect(')');
                    return value;
                }

                if (_position >= text.Length)
                {
                    throw new FormatException(text);
                }

                var character = text[_position];
                if (character is '?' or '$')
                {
                    var start = _position++;
                    while (_position < text.Length && (char.IsLetterOrDigit(text[_position]) || text[_position] == '_'))
                    {
                        _position++;
                    }

                    return formulas.Parameter(text[start.._position]);
                }

                if (char.IsDigit(character) || character == '.')
                {
                    var start = _position;
                    while (_position < text.Length && (char.IsDigit(text[_position]) || text[_position] == '.'))
                    {
                        _position++;
                    }

                    return double.Parse(text[start.._position], NumberStyles.Float, CultureInfo.InvariantCulture);
                }

                if (char.IsLetter(character))
                {
                    var start = _position;
                    while (_position < text.Length && char.IsLetterOrDigit(text[_position]))
                    {
                        _position++;
                    }

                    var name = text[start.._position];
                    SkipSpace();
                    if (!Accept('('))
                    {
                        return formulas.Parameter(name);
                    }

                    var arguments = new List<double> { Additive() };
                    SkipSpace();
                    while (Accept(','))
                    {
                        arguments.Add(Additive());
                        SkipSpace();
                    }

                    Expect(')');
                    return Function(name, arguments);
                }

                throw new FormatException(text);
            }

            private static double Function(string name, List<double> a)
            {
                double Arg(int index) => index < a.Count ? a[index] : 0;
                return name switch
                {
                    "abs" => Math.Abs(Arg(0)),
                    "sqrt" => Math.Sqrt(Math.Max(0, Arg(0))),
                    "sin" => Math.Sin(Arg(0)),
                    "cos" => Math.Cos(Arg(0)),
                    "tan" => Math.Tan(Arg(0)),
                    "atan" => Math.Atan(Arg(0)),
                    "atan2" => Math.Atan2(Arg(0), Arg(1)),
                    "min" => Math.Min(Arg(0), Arg(1)),
                    "max" => Math.Max(Arg(0), Arg(1)),
                    "if" => Arg(0) > 0 ? Arg(1) : Arg(2),
                    _ => throw new FormatException(name),
                };
            }

            private void SkipSpace()
            {
                while (_position < text.Length && char.IsWhiteSpace(text[_position]))
                {
                    _position++;
                }
            }

            private bool Accept(char character)
            {
                SkipSpace();
                if (_position < text.Length && text[_position] == character)
                {
                    _position++;
                    return true;
                }

                return false;
            }

            private void Expect(char character)
            {
                if (!Accept(character))
                {
                    throw new FormatException(text);
                }
            }
        }
    }

    /// <summary>Runs an enhanced path (ODF 1.3, 19.145 draw:enhanced-path) into sub-paths.</summary>
    private sealed class PathInterpreter(Formulas formulas)
    {
        private const double Kappa = 0.5522847498;
        private readonly List<SubPath> _paths = [];
        private VectorPathBuilder _builder = new();
        private bool _fill = true;
        private bool _stroke = true;

        public List<SubPath> Run(string text)
        {
            var tokens = Tokens(text);
            var index = 0;
            var command = 'M';
            while (index < tokens.Count)
            {
                if (tokens[index].Length == 1 && tokens[index][0] is >= 'A' and <= 'Z')
                {
                    command = tokens[index][0];
                    index++;
                    if (Simple(command))
                    {
                        continue;
                    }
                }

                var count = Arguments(command);
                if (count == 0 || index + count > tokens.Count)
                {
                    index++;
                    continue;
                }

                var values = new double[count];
                for (var argument = 0; argument < count; argument++)
                {
                    values[argument] = formulas.Parameter(tokens[index + argument]);
                }

                index += count;
                Draw(command, values);
                if (command == 'M')
                {
                    command = 'L'; // further pairs after a move are lines
                }
                else if (command is 'X' or 'Y')
                {
                    command = command == 'X' ? 'Y' : 'X';
                }
            }

            EndSubPath();
            return _paths;
        }

        private bool Simple(char command)
        {
            switch (command)
            {
                case 'Z':
                    _builder.Close();
                    return true;
                case 'N':
                    EndSubPath();
                    return true;
                case 'F':
                    _fill = false;
                    return true;
                case 'S':
                    _stroke = false;
                    return true;
                default:
                    return false;
            }
        }

        private static int Arguments(char command) => command switch
        {
            'M' or 'L' or 'X' or 'Y' => 2,
            'Q' or 'G' => 4,
            'C' or 'T' or 'U' => 6,
            'A' or 'B' or 'W' or 'V' => 8,
            _ => 0,
        };

        private void EndSubPath()
        {
            if (!_builder.IsEmpty)
            {
                _paths.Add(new SubPath(_builder.Build(), _fill, _stroke));
            }

            _builder = new VectorPathBuilder();
            _fill = true;
            _stroke = true;
        }

        private void Draw(char command, double[] v)
        {
            switch (command)
            {
                case 'M':
                    _builder.MoveTo(v[0], v[1]);
                    break;
                case 'L':
                    _builder.LineTo(v[0], v[1]);
                    break;
                case 'C':
                    _builder.CubicTo(v[0], v[1], v[2], v[3], v[4], v[5]);
                    break;
                case 'Q':
                    _builder.QuadTo(v[0], v[1], v[2], v[3]);
                    break;
                case 'T' or 'U':
                    AngleEllipse(v, move: command == 'U');
                    break;
                case 'A' or 'B' or 'W' or 'V':
                    Arc(v, clockwise: command is 'W' or 'V', move: command is 'B' or 'V');
                    break;
                case 'X':
                    Quadrant(v[0], v[1], horizontalFirst: true);
                    break;
                case 'Y':
                    Quadrant(v[0], v[1], horizontalFirst: false);
                    break;
                case 'G':
                    ArcAngle(v[0], v[1], v[2], v[3]);
                    break;
            }
        }

        /// <summary>T/U: centre, radii, start and end angle in degrees (counter-clockwise, y up).</summary>
        private void AngleEllipse(double[] v, bool move)
        {
            var (centerX, centerY, radiusX, radiusY) = (v[0], v[1], v[2], v[3]);
            var start = v[4];
            var end = v[5];
            var sweep = end - start;
            if (Math.Abs(sweep) < 1e-9 || Math.Abs(sweep) >= 360)
            {
                sweep = 360;
            }
            else if (sweep < 0)
            {
                sweep += 360;
            }

            if (move)
            {
                EndOpenFigure();
            }

            _builder.ArcTo(centerX, centerY, radiusX, radiusY, -start * Math.PI / 180, -sweep * Math.PI / 180);
        }

        /// <summary>A/B/W/V: the ellipse in a box, from the ray through one point to the ray through another.</summary>
        private void Arc(double[] v, bool clockwise, bool move)
        {
            var (x1, y1, x2, y2) = (Math.Min(v[0], v[2]), Math.Min(v[1], v[3]), Math.Max(v[0], v[2]), Math.Max(v[1], v[3]));
            var (centerX, centerY) = ((x1 + x2) / 2, (y1 + y2) / 2);
            var (radiusX, radiusY) = ((x2 - x1) / 2, (y2 - y1) / 2);
            if (radiusX <= 0 || radiusY <= 0)
            {
                _builder.LineTo(v[6], v[7]);
                return;
            }

            var start = Math.Atan2((v[5] - centerY) / radiusY, (v[4] - centerX) / radiusX);
            var end = Math.Atan2((v[7] - centerY) / radiusY, (v[6] - centerX) / radiusX);
            var sweep = end - start;
            if (clockwise)
            {
                while (sweep <= 0)
                {
                    sweep += Math.PI * 2;
                }
            }
            else
            {
                while (sweep >= 0)
                {
                    sweep -= Math.PI * 2;
                }
            }

            if (move)
            {
                EndOpenFigure();
            }

            _builder.ArcTo(centerX, centerY, radiusX, radiusY, start, sweep);
        }

        /// <summary>X/Y: a quarter ellipse to the point, starting horizontally (X) or vertically (Y).</summary>
        private void Quadrant(double x, double y, bool horizontalFirst)
        {
            var (x0, y0) = (_builder.CurrentX, _builder.CurrentY);
            if (horizontalFirst)
            {
                _builder.CubicTo(x0 + (Kappa * (x - x0)), y0, x, y - (Kappa * (y - y0)), x, y);
            }
            else
            {
                _builder.CubicTo(x0, y0 + (Kappa * (y - y0)), x - (Kappa * (x - x0)), y, x, y);
            }
        }

        /// <summary>G: DrawingML's arcTo (radii, visual start angle and sweep in degrees, clockwise positive).</summary>
        private void ArcAngle(double radiusX, double radiusY, double startDegrees, double sweepDegrees)
        {
            static double Parametric(double angle, double rx, double ry) => Math.Atan2(rx * Math.Sin(angle), ry * Math.Cos(angle));
            var start = Parametric(startDegrees * Math.PI / 180, radiusX, radiusY);
            var end = Parametric((startDegrees + sweepDegrees) * Math.PI / 180, radiusX, radiusY);
            var delta = end - start;
            if (sweepDegrees > 0 && delta < 0)
            {
                delta += Math.PI * 2;
            }
            else if (sweepDegrees < 0 && delta > 0)
            {
                delta -= Math.PI * 2;
            }

            if (Math.Abs(sweepDegrees) >= 360)
            {
                delta = Math.Sign(sweepDegrees) * Math.PI * 2;
            }

            var centerX = _builder.CurrentX - (radiusX * Math.Cos(start));
            var centerY = _builder.CurrentY - (radiusY * Math.Sin(start));
            _builder.ArcTo(centerX, centerY, radiusX, radiusY, start, delta);
        }

        /// <summary>A figure that moves starts a new figure: the arc begins with a MoveTo (VectorPathBuilder does it after a Close).</summary>
        private void EndOpenFigure()
        {
            if (!_builder.IsEmpty)
            {
                _builder.Close();
            }
        }
    }

    /// <summary>A small parser of SVG path data (M L H V C S Q T Z, absolute and relative; A as a line to its end).</summary>
    private static class SvgPathParser
    {
        public static List<VectorPath> Parse(string data)
        {
            var paths = new List<VectorPath>();
            var builder = new VectorPathBuilder();
            var tokens = SvgTokens(data);
            var command = 'M';
            var index = 0;
            double x = 0, y = 0, lastControlX = 0, lastControlY = 0;
            while (index < tokens.Count)
            {
                var token = tokens[index];
                if (token.Length == 1 && char.IsLetter(token[0]))
                {
                    command = token[0];
                    index++;
                    if (command is 'Z' or 'z')
                    {
                        builder.Close();
                        (x, y) = (builder.CurrentX, builder.CurrentY);
                    }

                    continue;
                }

                var relative = char.IsLower(command);
                double Next() => double.Parse(tokens[index++], NumberStyles.Float, CultureInfo.InvariantCulture);
                double X(double value) => relative ? x + value : value;
                double Y(double value) => relative ? y + value : value;
                var count = char.ToUpperInvariant(command) switch
                {
                    'M' or 'L' or 'T' => 2,
                    'H' or 'V' => 1,
                    'C' => 6,
                    'S' or 'Q' => 4,
                    'A' => 7,
                    _ => 0,
                };
                if (count == 0 || index + count > tokens.Count || tokens.Skip(index).Take(count).Any(item => item.Length == 1 && char.IsLetter(item[0])))
                {
                    index++;
                    continue;
                }

                switch (char.ToUpperInvariant(command))
                {
                    case 'M':
                        (x, y) = (X(Next()), Y(Next()));
                        builder.MoveTo(x, y);
                        command = relative ? 'l' : 'L';
                        break;
                    case 'L':
                        (x, y) = (X(Next()), Y(Next()));
                        builder.LineTo(x, y);
                        break;
                    case 'H':
                        x = X(Next());
                        builder.LineTo(x, y);
                        break;
                    case 'V':
                        y = relative ? y + Next() : Next();
                        builder.LineTo(x, y);
                        break;
                    case 'C':
                        {
                            var (x1, y1, x2, y2) = (X(Next()), Y(Next()), X(Next()), Y(Next()));
                            var (ex, ey) = (X(Next()), Y(Next()));
                            builder.CubicTo(x1, y1, x2, y2, ex, ey);
                            (lastControlX, lastControlY, x, y) = (x2, y2, ex, ey);
                            break;
                        }

                    case 'S':
                        {
                            var (x1, y1) = (2 * x - lastControlX, 2 * y - lastControlY);
                            var (x2, y2) = (X(Next()), Y(Next()));
                            var (ex, ey) = (X(Next()), Y(Next()));
                            builder.CubicTo(x1, y1, x2, y2, ex, ey);
                            (lastControlX, lastControlY, x, y) = (x2, y2, ex, ey);
                            break;
                        }

                    case 'Q':
                        {
                            var (x1, y1) = (X(Next()), Y(Next()));
                            var (ex, ey) = (X(Next()), Y(Next()));
                            builder.QuadTo(x1, y1, ex, ey);
                            (lastControlX, lastControlY, x, y) = (x1, y1, ex, ey);
                            break;
                        }

                    case 'T':
                        {
                            var (x1, y1) = (2 * x - lastControlX, 2 * y - lastControlY);
                            var (ex, ey) = (X(Next()), Y(Next()));
                            builder.QuadTo(x1, y1, ex, ey);
                            (lastControlX, lastControlY, x, y) = (x1, y1, ex, ey);
                            break;
                        }

                    case 'A':
                        index += 5;
                        (x, y) = (X(Next()), Y(Next()));
                        builder.LineTo(x, y);
                        break;
                }
            }

            if (!builder.IsEmpty)
            {
                paths.Add(builder.Build());
            }

            return paths;
        }

        /// <summary>Command letters one by one, numbers (with signs and exponents) apart.</summary>
        private static List<string> SvgTokens(string text)
        {
            var tokens = new List<string>();
            var index = 0;
            while (index < text.Length)
            {
                var character = text[index];
                if (char.IsWhiteSpace(character) || character == ',')
                {
                    index++;
                    continue;
                }

                if (char.IsLetter(character) && character is not ('e' or 'E'))
                {
                    tokens.Add(character.ToString());
                    index++;
                    continue;
                }

                var start = index++;
                var dot = character == '.';
                while (index < text.Length)
                {
                    var next = text[index];
                    if (char.IsDigit(next))
                    {
                        index++;
                    }
                    else if (next == '.' && !dot)
                    {
                        dot = true;
                        index++;
                    }
                    else if (next is 'e' or 'E')
                    {
                        index++;
                        if (index < text.Length && text[index] is '-' or '+')
                        {
                            index++;
                        }
                    }
                    else
                    {
                        break;
                    }
                }

                tokens.Add(text[start..index]);
            }

            return tokens;
        }
    }
}
