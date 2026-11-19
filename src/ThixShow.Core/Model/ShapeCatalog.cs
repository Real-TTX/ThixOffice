using ThixShow.Core.Drawing;

namespace ThixShow.Core.Model;

/// <summary>A category of the shapes gallery and its preset geometries in PowerPoint's order.</summary>
public sealed record ShapeCategory(string Name, IReadOnlyList<string> Presets);

/// <summary>
/// The shapes gallery as PowerPoint groups it (Linien, Rechtecke, Standardformen, Blockpfeile, Formelformen, Flussdiagramm, Sterne
/// und Banner, Legenden) – only presets the geometry engine knows – and the German names of the common ones (tooltips).
/// </summary>
public static class ShapeCatalog
{
    private static readonly (string Name, string[] Presets)[] Groups =
    [
        ("Linien", ["line", "straightConnector1", "bentConnector3", "curvedConnector3"]),
        ("Rechtecke", ["rect", "roundRect", "snip1Rect", "snip2SameRect", "snip2DiagRect", "snipRoundRect", "round1Rect", "round2SameRect", "round2DiagRect"]),
        ("Standardformen", ["ellipse", "triangle", "rtTriangle", "parallelogram", "trapezoid", "diamond", "pentagon", "hexagon", "heptagon", "octagon", "decagon", "dodecagon",
            "pie", "chord", "teardrop", "frame", "halfFrame", "corner", "diagStripe", "plus", "plaque", "can", "cube", "bevel", "donut", "noSmoking", "blockArc",
            "foldedCorner", "smileyFace", "heart", "lightningBolt", "sun", "moon", "cloud", "arc", "bracketPair", "bracePair", "leftBracket", "rightBracket", "leftBrace", "rightBrace"]),
        ("Blockpfeile", ["rightArrow", "leftArrow", "upArrow", "downArrow", "leftRightArrow", "upDownArrow", "quadArrow", "leftRightUpArrow", "bentArrow", "uturnArrow",
            "leftUpArrow", "bentUpArrow", "curvedRightArrow", "curvedLeftArrow", "curvedUpArrow", "curvedDownArrow", "stripedRightArrow", "notchedRightArrow", "homePlate",
            "chevron", "rightArrowCallout", "downArrowCallout", "leftArrowCallout", "upArrowCallout", "leftRightArrowCallout", "quadArrowCallout", "circularArrow"]),
        ("Formelformen", ["mathPlus", "mathMinus", "mathMultiply", "mathDivide", "mathEqual", "mathNotEqual"]),
        ("Flussdiagramm", ["flowChartProcess", "flowChartAlternateProcess", "flowChartDecision", "flowChartInputOutput", "flowChartPredefinedProcess", "flowChartInternalStorage",
            "flowChartDocument", "flowChartMultidocument", "flowChartTerminator", "flowChartPreparation", "flowChartManualInput", "flowChartManualOperation", "flowChartConnector",
            "flowChartOffpageConnector", "flowChartPunchedCard", "flowChartPunchedTape", "flowChartSummingJunction", "flowChartOr", "flowChartCollate", "flowChartSort",
            "flowChartExtract", "flowChartMerge", "flowChartOnlineStorage", "flowChartDelay", "flowChartMagneticTape", "flowChartMagneticDisk", "flowChartMagneticDrum", "flowChartDisplay"]),
        ("Sterne und Banner", ["irregularSeal1", "irregularSeal2", "star4", "star5", "star6", "star7", "star8", "star10", "star12", "star16", "star24", "star32",
            "ribbon2", "ribbon", "ellipseRibbon2", "ellipseRibbon", "verticalScroll", "horizontalScroll", "wave", "doubleWave"]),
        ("Legenden", ["wedgeRectCallout", "wedgeRoundRectCallout", "wedgeEllipseCallout", "cloudCallout", "borderCallout1", "borderCallout2", "borderCallout3",
            "accentCallout1", "accentCallout2", "accentCallout3", "callout1", "callout2", "callout3", "accentBorderCallout1", "accentBorderCallout2", "accentBorderCallout3"]),
    ];

    private static readonly Dictionary<string, string> Names = new(StringComparer.Ordinal)
    {
        ["line"] = "Linie", ["straightConnector1"] = "Pfeil", ["bentConnector3"] = "Gewinkelte Verbindung", ["curvedConnector3"] = "Gekrümmte Verbindung",
        ["rect"] = "Rechteck", ["roundRect"] = "Rechteck: abgerundete Ecken", ["snip1Rect"] = "Rechteck: eine Ecke abgeschnitten",
        ["ellipse"] = "Ellipse", ["triangle"] = "Gleichschenkliges Dreieck", ["rtTriangle"] = "Rechtwinkliges Dreieck", ["parallelogram"] = "Parallelogramm",
        ["trapezoid"] = "Trapez", ["diamond"] = "Raute", ["pentagon"] = "Fünfeck", ["hexagon"] = "Sechseck", ["octagon"] = "Achteck", ["plus"] = "Kreuz",
        ["can"] = "Zylinder", ["cube"] = "Würfel", ["donut"] = "Ring", ["heart"] = "Herz", ["lightningBolt"] = "Blitz", ["sun"] = "Sonne", ["moon"] = "Mond",
        ["cloud"] = "Wolke", ["smileyFace"] = "Smiley", ["arc"] = "Bogen", ["frame"] = "Rahmen", ["teardrop"] = "Träne",
        ["rightArrow"] = "Pfeil: nach rechts", ["leftArrow"] = "Pfeil: nach links", ["upArrow"] = "Pfeil: nach oben", ["downArrow"] = "Pfeil: nach unten",
        ["leftRightArrow"] = "Pfeil: nach links und rechts", ["upDownArrow"] = "Pfeil: nach oben und unten", ["quadArrow"] = "Pfeil: in vier Richtungen",
        ["chevron"] = "Pfeil: Chevron", ["homePlate"] = "Pfeil: Fünfeck", ["circularArrow"] = "Pfeil: gebogen",
        ["mathPlus"] = "Plus", ["mathMinus"] = "Minus", ["mathMultiply"] = "Multiplizieren", ["mathDivide"] = "Dividieren", ["mathEqual"] = "Gleich", ["mathNotEqual"] = "Ungleich",
        ["flowChartProcess"] = "Flussdiagramm: Prozess", ["flowChartDecision"] = "Flussdiagramm: Verzweigung", ["flowChartTerminator"] = "Flussdiagramm: Grenzstelle",
        ["flowChartDocument"] = "Flussdiagramm: Dokument", ["flowChartInputOutput"] = "Flussdiagramm: Daten", ["flowChartConnector"] = "Flussdiagramm: Verbindungsstelle",
        ["star4"] = "Stern: 4 Zacken", ["star5"] = "Stern: 5 Zacken", ["star6"] = "Stern: 6 Zacken", ["star8"] = "Stern: 8 Zacken", ["star12"] = "Stern: 12 Zacken",
        ["irregularSeal1"] = "Explosion: 8 Zacken", ["irregularSeal2"] = "Explosion: 14 Zacken", ["wave"] = "Welle", ["doubleWave"] = "Doppelwelle",
        ["wedgeRectCallout"] = "Sprechblase: rechteckig", ["wedgeRoundRectCallout"] = "Sprechblase: rechteckig mit abgerundeten Ecken",
        ["wedgeEllipseCallout"] = "Sprechblase: oval", ["cloudCallout"] = "Gedankenblase: wolkenförmig",
    };

    /// <summary>The categories with the presets the geometry engine knows (lines are drawn as lines).</summary>
    public static IReadOnlyList<ShapeCategory> Categories { get; } =
        [.. Groups.Select(group => new ShapeCategory(group.Name, [.. group.Presets.Where(preset => preset is "line" or "straightConnector1" || PresetGeometries.IsKnown(preset))]))
            .Where(category => category.Presets.Count > 0)];

    /// <summary>True for the presets drawn as a line from the press to the release.</summary>
    public static bool IsLine(string preset) => preset is "line" or "straightConnector1" or "bentConnector3" or "curvedConnector3";

    /// <summary>The German name of a preset (its id when ThixShow has none).</summary>
    public static string NameOf(string preset) => Names.TryGetValue(preset, out var name) ? name : preset;
}
