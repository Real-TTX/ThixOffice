using ThixCalc.Core.Calculation;

namespace ThixCalc.Core.Functions;

/// <summary>
/// The functions ThixCalc cannot compute offline ("nicht unterstützt"): OLAP cubes, real-time data, stock history, pictures from
/// the web, translation and Copilot. They parse, are named in Funktion einfügen, keep the value their file had (and its spill) and
/// are written back unchanged; a new one shows #DATEN_ABRUFEN.
/// </summary>
internal static class UnsupportedFunctions
{
    public static readonly IReadOnlyList<string> Names =
    [
        "CUBEKPIMEMBER", "CUBEMEMBER", "CUBEMEMBERPROPERTY", "CUBERANKEDMEMBER", "CUBESET", "CUBESETCOUNT", "CUBEVALUE", "RTD",
        "STOCKHISTORY", "FIELDVALUE", "IMAGE", "TRANSLATE", "DETECTLANGUAGE", "COPILOT",
    ];

    public static void Register(FunctionRegistrar r)
    {
        static Operand Unavailable(FunctionCall call) => throw new UnavailableFunctionException();

        r.Category = FunctionCategory.Cube;
        r.Add("CUBEKPIMEMBER", 3, 4, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("CUBEMEMBER", 2, 3, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("CUBEMEMBERPROPERTY", 3, 3, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("CUBERANKEDMEMBER", 3, 4, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("CUBESET", 2, 5, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("CUBESETCOUNT", 1, 1, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("CUBEVALUE", 1, 255, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Category = FunctionCategory.Lookup;
        r.Add("RTD", 3, 255, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("FIELDVALUE", 2, 2, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("IMAGE", 1, 5, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Category = FunctionCategory.Financial;
        r.Add("STOCKHISTORY", 2, 255, Unavailable, [ArgumentKind.Any], array: true, unsupported: true);
        r.Category = FunctionCategory.Text;
        r.Add("TRANSLATE", 1, 3, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("DETECTLANGUAGE", 1, 1, Unavailable, [ArgumentKind.Any], unsupported: true);
        r.Add("COPILOT", 1, 255, Unavailable, [ArgumentKind.Any], unsupported: true);
    }
}
