using System.Globalization;
using System.Xml.Linq;

namespace ThixOffice.Text.Drawing.Geometry;

/// <summary>How a path of a geometry is filled (a:path fill): normally, not at all, or with a lightened/darkened fill (3-D looks).</summary>
public enum PathFillMode
{
    Normal,
    None,
    Lighten,
    LightenLess,
    Darken,
    DarkenLess,
}

/// <summary>A guide: a name and its formula ("*/ ss a 100000"), evaluated in order (ECMA-376 20.1.9.11).</summary>
public sealed record GeometryGuide(string Name, string Formula);

/// <summary>A path command with its arguments (guide names or numbers).</summary>
public sealed record PathCommand(char Kind, string[] Arguments);

/// <summary>A path of a geometry: its own coordinate space (<see cref="Width"/>/<see cref="Height"/>, 0 = the shape's), fill and stroke.</summary>
public sealed record GeometryPathDefinition(long Width, long Height, PathFillMode Fill, bool Stroke, IReadOnlyList<PathCommand> Commands);

/// <summary>
/// An adjust handle (a:ahXY / a:ahPolar): which adjust values it moves (x/y or radius/angle), their limits (formulas or numbers)
/// and its position (guide names).
/// </summary>
public sealed record AdjustHandle(
    bool IsPolar,
    string? ReferenceX,
    string? MinimumX,
    string? MaximumX,
    string? ReferenceY,
    string? MinimumY,
    string? MaximumY,
    string PositionX,
    string PositionY);

/// <summary>
/// A geometry as DrawingML defines it (a:custGeom, and the presets of presetShapeDefinitions.xml): adjust values with defaults,
/// guides, handles, connection sites, the text rectangle and the paths. Presets are written in a compact text form (see
/// <see cref="PresetGeometries"/>); custom geometries are read from their XML.
/// </summary>
public sealed record GeometryDefinition(
    IReadOnlyList<GeometryGuide> Adjusts,
    IReadOnlyList<GeometryGuide> Guides,
    IReadOnlyList<AdjustHandle> Handles,
    IReadOnlyList<(string Angle, string X, string Y)> ConnectionSites,
    (string Left, string Top, string Right, string Bottom)? TextRect,
    IReadOnlyList<GeometryPathDefinition> Paths)
{
    /// <summary>
    /// Parses the compact form: lines "av name value", "gd name formula…", "ah x|y|xy refX minX maxX refY minY maxY posX posY"
    /// ("-" = none), "tx l t r b", "cx angle x y", and "p [w=… h=…] [fill=none|lighten|lightenLess|darken|darkenLess]
    /// [stroke=false] : M x y L x y A wR hR stAng swAng Q x1 y1 x y C x1 y1 x2 y2 x y Z".
    /// </summary>
    public static GeometryDefinition Parse(string text)
    {
        var adjusts = new List<GeometryGuide>();
        var guides = new List<GeometryGuide>();
        var handles = new List<AdjustHandle>();
        var sites = new List<(string, string, string)>();
        (string, string, string, string)? textRect = null;
        var paths = new List<GeometryPathDefinition>();
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                continue;
            }

            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            switch (parts[0])
            {
                case "av":
                    adjusts.Add(new GeometryGuide(parts[1], "val " + parts[2]));
                    break;
                case "gd":
                    guides.Add(new GeometryGuide(parts[1], string.Join(' ', parts.Skip(2))));
                    break;
                case "ah":
                    handles.Add(new AdjustHandle(parts[1] == "polar", None(parts[2]), None(parts[3]), None(parts[4]), None(parts[5]), None(parts[6]), None(parts[7]), parts[8], parts[9]));
                    break;
                case "tx":
                    textRect = (parts[1], parts[2], parts[3], parts[4]);
                    break;
                case "cx":
                    sites.Add((parts[1], parts[2], parts[3]));
                    break;
                case "p":
                    paths.Add(ParsePath(parts));
                    break;
            }
        }

        return new GeometryDefinition(adjusts, guides, handles, sites, textRect, paths);
    }

    private static string? None(string value) => value == "-" ? null : value;

    private static GeometryPathDefinition ParsePath(string[] parts)
    {
        long width = 0, height = 0;
        var fill = PathFillMode.Normal;
        var stroke = true;
        var index = 1;
        for (; index < parts.Length && parts[index] != ":"; index++)
        {
            var (key, value) = (parts[index].Split('=')[0], parts[index].Split('=')[1]);
            switch (key)
            {
                case "w":
                    width = long.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "h":
                    height = long.Parse(value, CultureInfo.InvariantCulture);
                    break;
                case "fill":
                    fill = ParseFill(value);
                    break;
                case "stroke":
                    stroke = value != "false";
                    break;
            }
        }

        var commands = new List<PathCommand>();
        index++;
        while (index < parts.Length)
        {
            var kind = parts[index][0];
            var count = kind switch
            {
                'M' or 'L' => 2,
                'A' or 'Q' => 4,
                'C' => 6,
                _ => 0,
            };
            commands.Add(new PathCommand(kind, parts.Skip(index + 1).Take(count).ToArray()));
            index += count + 1;
        }

        return new GeometryPathDefinition(width, height, fill, stroke, commands);
    }

    private static PathFillMode ParseFill(string value) => value switch
    {
        "none" => PathFillMode.None,
        "lighten" => PathFillMode.Lighten,
        "lightenLess" => PathFillMode.LightenLess,
        "darken" => PathFillMode.Darken,
        "darkenLess" => PathFillMode.DarkenLess,
        _ => PathFillMode.Normal,
    };

    /// <summary>Reads an a:custGeom element (namespace-agnostic: local names).</summary>
    public static GeometryDefinition FromCustomGeometry(XElement custom)
    {
        static IEnumerable<XElement> Children(XElement? parent, string name) =>
            parent?.Elements().Where(element => element.Name.LocalName == name) ?? [];

        static XElement? Child(XElement? parent, string name) => Children(parent, name).FirstOrDefault();

        static string Attribute(XElement element, string name, string fallback = "0") =>
            element.Attributes().FirstOrDefault(attribute => attribute.Name.LocalName == name)?.Value ?? fallback;

        var adjusts = Children(Child(custom, "avLst"), "gd").Select(gd => new GeometryGuide(Attribute(gd, "name"), Attribute(gd, "fmla", "val 0"))).ToList();
        var guides = Children(Child(custom, "gdLst"), "gd").Select(gd => new GeometryGuide(Attribute(gd, "name"), Attribute(gd, "fmla", "val 0"))).ToList();
        var handles = new List<AdjustHandle>();
        foreach (var handle in Child(custom, "ahLst")?.Elements() ?? [])
        {
            var position = Child(handle, "pos");
            var (x, y) = position is null ? ("0", "0") : (Attribute(position, "x"), Attribute(position, "y"));
            if (handle.Name.LocalName == "ahPolar")
            {
                handles.Add(new AdjustHandle(true, handle.Attribute("gdRefR")?.Value, handle.Attribute("minR")?.Value, handle.Attribute("maxR")?.Value,
                    handle.Attribute("gdRefAng")?.Value, handle.Attribute("minAng")?.Value, handle.Attribute("maxAng")?.Value, x, y));
            }
            else
            {
                handles.Add(new AdjustHandle(false, handle.Attribute("gdRefX")?.Value, handle.Attribute("minX")?.Value, handle.Attribute("maxX")?.Value,
                    handle.Attribute("gdRefY")?.Value, handle.Attribute("minY")?.Value, handle.Attribute("maxY")?.Value, x, y));
            }
        }

        var sites = Children(Child(custom, "cxnLst"), "cxn")
            .Select(cxn => (Attribute(cxn, "ang"), Child(cxn, "pos") is { } pos ? Attribute(pos, "x") : "0", Child(cxn, "pos") is { } p2 ? Attribute(p2, "y") : "0"))
            .ToList();
        var rect = Child(custom, "rect");
        (string, string, string, string)? textRect = rect is null ? null : (Attribute(rect, "l", "l"), Attribute(rect, "t", "t"), Attribute(rect, "r", "r"), Attribute(rect, "b", "b"));
        var paths = new List<GeometryPathDefinition>();
        foreach (var path in Children(Child(custom, "pathLst"), "path"))
        {
            var commands = new List<PathCommand>();
            foreach (var command in path.Elements())
            {
                var points = Children(command, "pt").SelectMany(pt => new[] { Attribute(pt, "x"), Attribute(pt, "y") }).ToArray();
                switch (command.Name.LocalName)
                {
                    case "moveTo":
                        commands.Add(new PathCommand('M', points));
                        break;
                    case "lnTo":
                        commands.Add(new PathCommand('L', points));
                        break;
                    case "quadBezTo":
                        commands.Add(new PathCommand('Q', points));
                        break;
                    case "cubicBezTo":
                        commands.Add(new PathCommand('C', points));
                        break;
                    case "arcTo":
                        commands.Add(new PathCommand('A', [Attribute(command, "wR"), Attribute(command, "hR"), Attribute(command, "stAng"), Attribute(command, "swAng")]));
                        break;
                    case "close":
                        commands.Add(new PathCommand('Z', []));
                        break;
                }
            }

            paths.Add(new GeometryPathDefinition(
                long.Parse(Attribute(path, "w"), CultureInfo.InvariantCulture),
                long.Parse(Attribute(path, "h"), CultureInfo.InvariantCulture),
                ParseFill(Attribute(path, "fill", "norm")),
                Attribute(path, "stroke", "1") is not ("0" or "false"),
                commands));
        }

        return new GeometryDefinition(adjusts, guides, handles, sites, textRect, paths);
    }
}
