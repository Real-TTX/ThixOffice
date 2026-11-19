using System.Collections.Concurrent;
using System.Globalization;
namespace ThixOffice.Text.Drawing.Geometry;

/// <summary>A path of an evaluated geometry in the shape's own coordinates (0,0 = top-left of its box).</summary>
public sealed record GeometryPath(VectorPath Path, PathFillMode Fill, bool Stroke);

/// <summary>An adjust handle's position in the shape's coordinates and the adjust values it moves.</summary>
public sealed record HandlePosition(int Index, double X, double Y, AdjustHandle Handle);

/// <summary>The result of evaluating a geometry for a size: paths, text rectangle, handles, connection sites.</summary>
public sealed record EvaluatedGeometry(
    IReadOnlyList<GeometryPath> Paths,
    double TextLeft,
    double TextTop,
    double TextRight,
    double TextBottom,
    IReadOnlyList<HandlePosition> Handles,
    IReadOnlyList<(double X, double Y, double Angle)> ConnectionSites,
    bool IsKnown);

/// <summary>An adjust value of a geometry (a:gd in a:avLst): its name and value.</summary>
public readonly record struct GeometryAdjustment(string Name, long Value);

/// <summary>
/// DrawingML's shape geometry engine (ECMA-376 Part 1, 20.1.9), shared by every app (ThixShow's shapes, ThixWrite's shapes and text
/// boxes): evaluates the guide formulas of a preset or custom geometry for a shape size and produces its paths (arcs as Béziers), text
/// rectangle, adjust handles and connection sites. Units are whatever the size is given in (the renderers use points). Unknown presets
/// fall back to the rectangle (<see cref="EvaluatedGeometry.IsKnown"/> false).
/// </summary>
public static class GeometryEvaluator
{
    private static readonly ConcurrentDictionary<string, GeometryDefinition> CustomCache = new(StringComparer.Ordinal);

    /// <summary>The definition of a preset (null when unknown) or of a custom geometry (a:custGeom as XML).</summary>
    public static GeometryDefinition? DefinitionOf(string? preset, string? customXml = null) =>
        customXml is not null
            ? CustomCache.GetOrAdd(customXml, xml => GeometryDefinition.FromCustomGeometry(System.Xml.Linq.XElement.Parse(xml)))
            : PresetGeometries.Get(preset ?? "rect");

    /// <summary>Evaluates a preset or custom geometry for a box of <paramref name="width"/> × <paramref name="height"/> (the rectangle when unknown).</summary>
    public static EvaluatedGeometry Evaluate(string? preset, IReadOnlyList<GeometryAdjustment>? adjustments, double width, double height, string? customXml = null)
    {
        var definition = DefinitionOf(preset, customXml);
        var known = definition is not null;
        definition ??= PresetGeometries.Get("rect")!;
        return Evaluate(definition, adjustments ?? [], width, height, known);
    }

    /// <summary>Evaluates a definition with adjust values overriding its defaults.</summary>
    public static EvaluatedGeometry Evaluate(GeometryDefinition definition, IReadOnlyList<GeometryAdjustment> adjustments, double width, double height, bool known = true)
    {
        var variables = Variables(definition, adjustments, width, height);
        var paths = definition.Paths.Select(path => BuildPath(path, variables, width, height)).ToList();
        double left = 0, top = 0, right = width, bottom = height;
        if (definition.TextRect is { } rect)
        {
            (left, top, right, bottom) = (Value(rect.Left, variables), Value(rect.Top, variables), Value(rect.Right, variables), Value(rect.Bottom, variables));
        }

        var handles = definition.Handles
            .Select((handle, index) => new HandlePosition(index, Value(handle.PositionX, variables), Value(handle.PositionY, variables), handle))
            .ToList();
        var sites = definition.ConnectionSites
            .Select(site => (Value(site.X, variables), Value(site.Y, variables), Value(site.Angle, variables) / 60000.0))
            .ToList();
        return new EvaluatedGeometry(paths, left, top, right, bottom, handles, sites, known);
    }

    /// <summary>
    /// The adjust value that puts handle <paramref name="handleIndex"/> nearest to (<paramref name="x"/>, <paramref name="y"/>) –
    /// numerically (bisection on the handle's position, which is monotonic in its adjust value for the presets), so it works for
    /// every formula. Returns the adjust names with their new values.
    /// </summary>
    public static IReadOnlyList<GeometryAdjustment> AdjustForHandle(GeometryDefinition? definition, IReadOnlyList<GeometryAdjustment> adjustments, int handleIndex, double width, double height, double x, double y)
    {
        if (definition is null || handleIndex < 0 || handleIndex >= definition.Handles.Count)
        {
            return [];
        }

        var handle = definition.Handles[handleIndex];
        var result = new List<GeometryAdjustment>();
        var current = adjustments;
        if (handle.IsPolar)
        {
            return result;
        }

        if (handle.ReferenceX is { } referenceX)
        {
            var value = Solve(definition, current, referenceX, handle.MinimumX, handle.MaximumX, width, height, x, handle.PositionX);
            result.Add(new GeometryAdjustment(referenceX, value));
            current = With(current, referenceX, value);
        }

        if (handle.ReferenceY is { } referenceY)
        {
            var value = Solve(definition, current, referenceY, handle.MinimumY, handle.MaximumY, width, height, y, handle.PositionY);
            result.Add(new GeometryAdjustment(referenceY, value));
        }

        return result;
    }

    private static IReadOnlyList<GeometryAdjustment> With(IReadOnlyList<GeometryAdjustment> adjustments, string name, long value)
    {
        var list = adjustments.Where(adjust => adjust.Name != name).ToList();
        list.Add(new GeometryAdjustment(name, value));
        return list;
    }

    private static long Solve(GeometryDefinition definition, IReadOnlyList<GeometryAdjustment> adjustments, string name, string? minimum, string? maximum, double width, double height, double target, string position)
    {
        var baseline = Variables(definition, adjustments, width, height);
        var low = minimum is null ? -100000.0 : Value(minimum, baseline);
        var high = maximum is null ? 100000.0 : Value(maximum, baseline);
        if (low > high)
        {
            (low, high) = (high, low);
        }

        double PositionAt(double value) => Value(position, Variables(definition, With(adjustments, name, (long)Math.Round(value)), width, height));

        var increasing = PositionAt(high) >= PositionAt(low);
        for (var iteration = 0; iteration < 60 && high - low > 0.5; iteration++)
        {
            var middle = (low + high) / 2;
            if ((PositionAt(middle) < target) == increasing)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return (long)Math.Round((low + high) / 2);
    }

    private static Dictionary<string, double> Variables(GeometryDefinition definition, IReadOnlyList<GeometryAdjustment> adjustments, double width, double height)
    {
        var variables = BuiltIns(width, height);
        foreach (var adjust in definition.Adjusts)
        {
            var index = FindIndex(adjustments, adjust.Name);
            variables[adjust.Name] = index >= 0 ? adjustments[index].Value : Formula(adjust.Formula, variables);
        }

        foreach (var guide in definition.Guides)
        {
            variables[guide.Name] = Formula(guide.Formula, variables);
        }

        return variables;
    }

    private static int FindIndex(IReadOnlyList<GeometryAdjustment> adjustments, string name)
    {
        for (var index = 0; index < adjustments.Count; index++)
        {
            if (adjustments[index].Name == name)
            {
                return index;
            }
        }

        return -1;
    }

    private static Dictionary<string, double> BuiltIns(double w, double h)
    {
        var ss = Math.Min(w, h);
        var ls = Math.Max(w, h);
        return new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["w"] = w,
            ["h"] = h,
            ["l"] = 0,
            ["t"] = 0,
            ["r"] = w,
            ["b"] = h,
            ["hc"] = w / 2,
            ["vc"] = h / 2,
            ["ss"] = ss,
            ["ls"] = ls,
            ["wd2"] = w / 2,
            ["wd3"] = w / 3,
            ["wd4"] = w / 4,
            ["wd5"] = w / 5,
            ["wd6"] = w / 6,
            ["wd8"] = w / 8,
            ["wd10"] = w / 10,
            ["wd12"] = w / 12,
            ["wd32"] = w / 32,
            ["hd2"] = h / 2,
            ["hd3"] = h / 3,
            ["hd4"] = h / 4,
            ["hd5"] = h / 5,
            ["hd6"] = h / 6,
            ["hd8"] = h / 8,
            ["hd10"] = h / 10,
            ["hd12"] = h / 12,
            ["hd32"] = h / 32,
            ["ssd2"] = ss / 2,
            ["ssd4"] = ss / 4,
            ["ssd6"] = ss / 6,
            ["ssd8"] = ss / 8,
            ["ssd16"] = ss / 16,
            ["ssd32"] = ss / 32,
            ["cd2"] = 10800000,
            ["cd4"] = 5400000,
            ["cd8"] = 2700000,
            ["3cd4"] = 16200000,
            ["3cd8"] = 8100000,
            ["5cd8"] = 13500000,
            ["7cd8"] = 18900000,
        };
    }

    /// <summary>A guide name or a number.</summary>
    private static double Value(string token, IReadOnlyDictionary<string, double> variables)
    {
        if (variables.TryGetValue(token, out var value))
        {
            return value;
        }

        return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var number) ? number : 0;
    }

    private static double Value(string token, Dictionary<string, double> variables) => Value(token, (IReadOnlyDictionary<string, double>)variables);

    private const double AngleToRadians = Math.PI / 180 / 60000;

    /// <summary>Evaluates one formula (ECMA-376 20.1.9.11).</summary>
    public static double Formula(string formula, IReadOnlyDictionary<string, double> variables)
    {
        var parts = formula.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length == 0)
        {
            return 0;
        }

        double Arg(int index) => index < parts.Length ? Value(parts[index], variables) : 0;

        switch (parts[0])
        {
            case "val":
                return Arg(1);
            case "*/":
                return Arg(3) == 0 ? 0 : Arg(1) * Arg(2) / Arg(3);
            case "+-":
                return Arg(1) + Arg(2) - Arg(3);
            case "+/":
                return Arg(3) == 0 ? 0 : (Arg(1) + Arg(2)) / Arg(3);
            case "?:":
                return Arg(1) > 0 ? Arg(2) : Arg(3);
            case "abs":
                return Math.Abs(Arg(1));
            case "at2":
                return Math.Atan2(Arg(2), Arg(1)) / AngleToRadians;
            case "cat2":
                return Arg(1) * Math.Cos(Math.Atan2(Arg(3), Arg(2)));
            case "sat2":
                return Arg(1) * Math.Sin(Math.Atan2(Arg(3), Arg(2)));
            case "cos":
                return Arg(1) * Math.Cos(Arg(2) * AngleToRadians);
            case "sin":
                return Arg(1) * Math.Sin(Arg(2) * AngleToRadians);
            case "tan":
                return Arg(1) * Math.Tan(Arg(2) * AngleToRadians);
            case "max":
                return Math.Max(Arg(1), Arg(2));
            case "min":
                return Math.Min(Arg(1), Arg(2));
            case "mod":
                return Math.Sqrt((Arg(1) * Arg(1)) + (Arg(2) * Arg(2)) + (Arg(3) * Arg(3)));
            case "pin":
                {
                    var (low, value, high) = (Arg(1), Arg(2), Arg(3));
                    return value < low ? low : value > high ? high : value;
                }

            case "sqrt":
                return Math.Sqrt(Math.Max(0, Arg(1)));
            default:
                return 0;
        }
    }

    private static GeometryPath BuildPath(GeometryPathDefinition definition, Dictionary<string, double> variables, double width, double height)
    {
        var scaleX = definition.Width > 0 ? width / definition.Width : 1;
        var scaleY = definition.Height > 0 ? height / definition.Height : 1;
        var builder = new VectorPathBuilder();
        double X(string token) => Value(token, variables) * scaleX;
        double Y(string token) => Value(token, variables) * scaleY;

        foreach (var command in definition.Commands)
        {
            var arguments = command.Arguments;
            switch (command.Kind)
            {
                case 'M':
                    builder.MoveTo(X(arguments[0]), Y(arguments[1]));
                    break;
                case 'L':
                    builder.LineTo(X(arguments[0]), Y(arguments[1]));
                    break;
                case 'Q':
                    builder.QuadTo(X(arguments[0]), Y(arguments[1]), X(arguments[2]), Y(arguments[3]));
                    break;
                case 'C':
                    builder.CubicTo(X(arguments[0]), Y(arguments[1]), X(arguments[2]), Y(arguments[3]), X(arguments[4]), Y(arguments[5]));
                    break;
                case 'A':
                    ArcTo(builder, X(arguments[0]), Y(arguments[1]), Value(arguments[2], variables), Value(arguments[3], variables));
                    break;
                case 'Z':
                    builder.Close();
                    break;
            }
        }

        return new GeometryPath(builder.Build(), definition.Fill, definition.Stroke);
    }

    /// <summary>
    /// a:arcTo: the current point lies on the ellipse (radii <paramref name="radiusX"/>, <paramref name="radiusY"/>) at the visual
    /// angle <paramref name="start"/>; the arc sweeps <paramref name="sweep"/> (60000ths of a degree, clockwise positive). Visual
    /// angles become parametric ones (tan t = tan a · rX / rY), the way the preset formulas' cat2/sat2 place points.
    /// </summary>
    private static void ArcTo(VectorPathBuilder builder, double radiusX, double radiusY, double start, double sweep)
    {
        var startRadians = start * AngleToRadians;
        var endRadians = (start + sweep) * AngleToRadians;
        var startParameter = Parametric(startRadians, radiusX, radiusY);
        var endParameter = Parametric(endRadians, radiusX, radiusY);
        var delta = endParameter - startParameter;
        if (sweep > 0 && delta < 0)
        {
            delta += Math.PI * 2;
        }
        else if (sweep < 0 && delta > 0)
        {
            delta -= Math.PI * 2;
        }

        if (Math.Abs(sweep) >= 21600000 - 1)
        {
            delta = Math.Sign(sweep) * Math.PI * 2;
        }

        var centerX = builder.CurrentX - (radiusX * Math.Cos(startParameter));
        var centerY = builder.CurrentY - (radiusY * Math.Sin(startParameter));
        if (radiusX <= 0 || radiusY <= 0)
        {
            builder.LineTo(centerX + (radiusX * Math.Cos(startParameter + delta)), centerY + (radiusY * Math.Sin(startParameter + delta)));
            return;
        }

        builder.ArcTo(centerX, centerY, radiusX, radiusY, startParameter, delta);
    }

    private static double Parametric(double angle, double radiusX, double radiusY) =>
        Math.Atan2(radiusX * Math.Sin(angle), radiusY * Math.Cos(angle));
}
