using ThixCalc.Core.Functions;
using ThixCalc.Core.Globalization;
using Xunit.Abstractions;

namespace ThixCalc.Core.Tests.Functions;

/// <summary>
/// The library against the list of Excel's worksheet functions (ExcelFunctions.tsv: English, German, category, since): every
/// function is implemented or named as not supported, every German name is the one German Excel shows, and every function has
/// German help.
/// </summary>
public class FunctionCoverageTests(ITestOutputHelper output)
{
    private static readonly IReadOnlyList<(string English, string German, string Category, string Since)> Excel = Read();

    private static IReadOnlyList<(string, string, string, string)> Read() =>
        File.ReadAllLines(Path.Combine(AppContext.BaseDirectory, "Functions", "ExcelFunctions.tsv"))
            .Skip(1)
            .Where(line => line.Length > 0)
            .Select(line => line.Split('\t'))
            .Select(parts => (parts[0], parts[1], parts[2], parts[3]))
            .ToList();

    [Fact]
    public void Every_excel_function_is_implemented_or_named_as_not_supported()
    {
        var missing = Excel.Where(entry => FunctionRegistry.Find(entry.English) is null && !FunctionRegistry.IsUnsupported(entry.English))
            .Select(entry => entry.English)
            .ToList();
        var implemented = Excel.Count(entry => FunctionRegistry.Find(entry.English) is { IsUnsupported: false });
        output.WriteLine($"Excel functions: {Excel.Count}, implemented: {implemented}, not supported: {Excel.Count(entry => FunctionRegistry.IsUnsupported(entry.English))}");
        Assert.True(missing.Count == 0, "Missing: " + string.Join(", ", missing));
    }

    [Fact]
    public void German_names_are_those_of_german_excel()
    {
        var wrong = Excel
            .Where(entry => CalcLocale.German.FunctionName(entry.English) != entry.German)
            .Select(entry => $"{entry.English}: {CalcLocale.German.FunctionName(entry.English)} ≠ {entry.German}")
            .ToList();
        Assert.True(wrong.Count == 0, string.Join(" | ", wrong));
        Assert.All(Excel, entry => Assert.Equal(entry.English, CalcLocale.German.CanonicalFunctionName(entry.German)));
    }

    [Fact]
    public void Every_function_has_german_help()
    {
        var without = FunctionHelp.All(CalcLocale.German)
            .Where(info => info.Description.StartsWith("Funktion der Kategorie", StringComparison.Ordinal))
            .Select(info => info.Canonical)
            .ToList();
        Assert.True(without.Count == 0, "Without help: " + string.Join(", ", without));
    }
}
