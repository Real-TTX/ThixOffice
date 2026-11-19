namespace ThixCalc.Core.Model;

/// <summary>A paper of SpreadsheetML's paperSize codes with its size in millimetres (portrait).</summary>
public sealed record SheetPaper(int Code, string Label, double WidthMillimetres, double HeightMillimetres)
{
    /// <summary>The papers ThixCalc offers (A4 first – the German default), by their SpreadsheetML codes.</summary>
    public static IReadOnlyList<SheetPaper> All { get; } =
    [
        new(9, "A4", 210, 297),
        new(11, "A5", 148, 210),
        new(8, "A3", 297, 420),
        new(1, "Letter", 215.9, 279.4),
        new(5, "Legal", 215.9, 355.6),
    ];

    /// <summary>The paper of a code (A4 for an unknown one).</summary>
    public static SheetPaper Of(int code) => All.FirstOrDefault(paper => paper.Code == code) ?? All[0];
}

/// <summary>
/// Seitenlayout of a worksheet (SpreadsheetML pageSetup, pageMargins, printOptions, headerFooter, sheetPr/pageSetUpPr): paper,
/// orientation, margins in inches as the file keeps them, scaling (percent, or fit to pages wide × tall; 0 = as many as
/// needed), grid lines and headings on paper, centring, and the simple header/footer texts with Excel's codes (&amp;L &amp;C &amp;R
/// sections, &amp;P page, &amp;N pages, &amp;D date, &amp;T time, &amp;A sheet, &amp;F file). The print area and the rows to repeat are the
/// sheet's built-in names _xlnm.Print_Area / _xlnm.Print_Titles.
/// </summary>
public sealed record PageSetup
{
    public static PageSetup Default { get; } = new();

    public int PaperSize { get; init; } = 9;

    public bool Landscape { get; init; }

    public int Scale { get; init; } = 100;

    /// <summary>Fit to pages instead of scaling (sheetPr/pageSetUpPr fitToPage).</summary>
    public bool FitToPage { get; init; }

    public int FitToWidth { get; init; } = 1;

    public int FitToHeight { get; init; } = 1;

    public double LeftMargin { get; init; } = 0.7;

    public double RightMargin { get; init; } = 0.7;

    public double TopMargin { get; init; } = 0.75;

    public double BottomMargin { get; init; } = 0.75;

    public double HeaderMargin { get; init; } = 0.3;

    public double FooterMargin { get; init; } = 0.3;

    public bool PrintGridLines { get; init; }

    public bool PrintHeadings { get; init; }

    public bool CenterHorizontally { get; init; }

    public bool CenterVertically { get; init; }

    /// <summary>Over, then down (false: down, then over – Excel's default).</summary>
    public bool OverThenDown { get; init; }

    /// <summary>The odd (= every) page's header text with Excel's codes, or null.</summary>
    public string? Header { get; init; }

    public string? Footer { get; init; }

    public int? FirstPageNumber { get; init; }

    /// <summary>The paper's size in points as the page lies (landscape swaps).</summary>
    public (double Width, double Height) PaperPoints
    {
        get
        {
            var paper = SheetPaper.Of(PaperSize);
            var width = paper.WidthMillimetres / 25.4 * 72;
            var height = paper.HeightMillimetres / 25.4 * 72;
            return Landscape ? (height, width) : (width, height);
        }
    }
}
