namespace ThixOffice.Text;

/// <summary>
/// What to measure and draw with when a document's font is not installed: metric-compatible substitutes first (Carlito for Calibri,
/// Caladea for Cambria, Liberation Sans/Serif/Mono for Arial, Times New Roman and Courier New – same advance widths, so lines break
/// where Word breaks them), then fonts that look alike on Windows and macOS. Only measuring and drawing use them: the document keeps
/// its font names.
/// </summary>
public static class FontSubstitutes
{
    private static readonly string[] Sans = ["Helvetica Neue", "Arial", "Liberation Sans", "Arimo", "Helvetica"];
    private static readonly string[] Serif = ["Times New Roman", "Liberation Serif", "Tinos", "Times"];
    private static readonly string[] Mono = ["Courier New", "Liberation Mono", "Cousine", "Menlo", "Consolas", "Courier"];

    private static readonly Dictionary<string, string[]> Table = new(StringComparer.OrdinalIgnoreCase)
    {
        ["Aptos"] = ["Calibri", "Carlito", "Segoe UI", .. Sans],
        ["Aptos Display"] = ["Calibri Light", "Calibri", "Carlito", "Segoe UI", .. Sans],
        ["Aptos Light"] = ["Calibri Light", "Calibri", "Carlito", "Segoe UI Light", .. Sans],
        ["Aptos Narrow"] = ["Arial Narrow", "Liberation Sans Narrow", "Calibri", "Carlito", .. Sans],
        ["Calibri"] = ["Carlito", "Aptos", "Segoe UI", .. Sans],
        ["Calibri Light"] = ["Carlito", "Calibri", "Aptos Light", "Segoe UI Light", .. Sans],
        ["Cambria"] = ["Caladea", "Georgia", .. Serif],
        ["Cambria Math"] = ["Cambria", "Caladea", "STIX Two Math", "Latin Modern Math", .. Serif],
        ["Arial"] = ["Liberation Sans", "Arimo", "Helvetica", "Helvetica Neue"],
        ["Helvetica"] = ["Arial", "Liberation Sans", "Arimo", "Helvetica Neue"],
        ["Helvetica Neue"] = ["Arial", "Liberation Sans", "Arimo", "Helvetica"],
        ["Arial Narrow"] = ["Liberation Sans Narrow", "Helvetica Neue Condensed", .. Sans],
        ["Times New Roman"] = ["Liberation Serif", "Tinos", "Times"],
        ["Times"] = ["Times New Roman", "Liberation Serif", "Tinos"],
        ["Courier New"] = ["Liberation Mono", "Cousine", "Courier", "Menlo", "Consolas"],
        ["Courier"] = ["Courier New", "Liberation Mono", "Cousine", "Menlo"],
        ["Consolas"] = ["Menlo", "Liberation Mono", "Cousine", "Courier New"],
        ["Segoe UI"] = ["Helvetica Neue", ".AppleSystemUIFont", "SF Pro Text", "Arial", "Liberation Sans", "Noto Sans"],
        ["Segoe UI Light"] = ["Segoe UI", "Helvetica Neue", "Arial", "Liberation Sans"],
        ["Segoe UI Semibold"] = ["Segoe UI", "Helvetica Neue", "Arial", "Liberation Sans"],
        ["Georgia"] = ["Gelasio", .. Serif],
        ["Garamond"] = ["EB Garamond", "Garamond Premier Pro", "Georgia", .. Serif],
        ["Book Antiqua"] = ["Palatino Linotype", "Palatino", "TeX Gyre Pagella", .. Serif],
        ["Palatino Linotype"] = ["Palatino", "Book Antiqua", "TeX Gyre Pagella", .. Serif],
        ["Verdana"] = ["DejaVu Sans", "Tahoma", .. Sans],
        ["Tahoma"] = ["Verdana", "DejaVu Sans", .. Sans],
        ["Trebuchet MS"] = ["Fira Sans", .. Sans],
        ["Century Gothic"] = ["URW Gothic", "Avenir", "Futura", .. Sans],
        ["Franklin Gothic Book"] = ["Libre Franklin", .. Sans],
        ["Gill Sans MT"] = ["Gill Sans", .. Sans],
        ["Corbel"] = ["Calibri", "Carlito", .. Sans],
        ["Candara"] = ["Calibri", "Carlito", .. Sans],
        ["Constantia"] = ["Cambria", "Caladea", "Georgia", .. Serif],
        ["Lucida Console"] = ["Menlo", .. Mono],
        ["Lucida Sans Unicode"] = ["Lucida Grande", .. Sans],
        ["MS Sans Serif"] = ["Microsoft Sans Serif", .. Sans],
        ["MS Serif"] = Serif,
    };

    /// <summary>The families to try for <paramref name="family"/>, in order: the font itself, then its substitutes (no duplicates).</summary>
    public static IReadOnlyList<string> CandidatesFor(string family)
    {
        var result = new List<string> { family };
        if (Table.TryGetValue(family.Trim(), out var substitutes))
        {
            foreach (var substitute in substitutes)
            {
                if (!result.Contains(substitute, StringComparer.OrdinalIgnoreCase))
                {
                    result.Add(substitute);
                }
            }
        }

        return result;
    }
}
