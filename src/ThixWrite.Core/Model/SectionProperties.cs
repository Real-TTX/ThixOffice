namespace ThixWrite.Core.Model;

/// <summary>Page orientation ("Hochformat" / "Querformat").</summary>
public enum PageOrientation
{
    Portrait,
    Landscape,
}

/// <summary>Where a section starts.</summary>
public enum SectionStart
{
    NextPage,
    Continuous,
    EvenPage,
    OddPage,
    NextColumn,
}

/// <summary>
/// Page setup of a section, immutable, lengths in points, with its headers and footers. Page number formats, line numbering,
/// the document grid … are kept as <see cref="UnknownXml"/> (relationship ids stay valid: see Formats.Docx.DocxWriter).
/// </summary>
public sealed record SectionProperties
{
    /// <summary>A4 portrait with Word's German margins (2.5 cm, bottom 2 cm).</summary>
    public static readonly SectionProperties Default = new();

    public double PageWidth { get; init; } = 595.3;

    public double PageHeight { get; init; } = 841.9;

    public PageOrientation Orientation { get; init; } = PageOrientation.Portrait;

    public double MarginTop { get; init; } = 70.85;

    public double MarginBottom { get; init; } = 56.7;

    public double MarginLeft { get; init; } = 70.85;

    public double MarginRight { get; init; } = 70.85;

    public double HeaderDistance { get; init; } = 35.4;

    public double FooterDistance { get; init; } = 35.4;

    public double Gutter { get; init; }

    public SectionStart Start { get; init; } = SectionStart.NextPage;

    /// <summary>Number of text columns (columns are laid out from 1b on; 1a keeps the value).</summary>
    public int ColumnCount { get; init; } = 1;

    public double ColumnSpacing { get; init; } = 35.4;

    /// <summary>"Erste Seite anders".</summary>
    public bool TitlePage { get; init; }

    /// <summary>The headers of the section (kinds it does not set come from the section before).</summary>
    public HeaderFooterSet Headers { get; init; } = HeaderFooterSet.Empty;

    /// <summary>The footers of the section.</summary>
    public HeaderFooterSet Footers { get; init; } = HeaderFooterSet.Empty;

    /// <summary>Section properties ThixWrite does not model (header/footer references, page numbering …), as XML.</summary>
    public string? UnknownXml { get; init; }

    /// <summary>The width available for text.</summary>
    public double ContentWidth => Math.Max(1, PageWidth - MarginLeft - MarginRight - Gutter);

    /// <summary>The height available for text.</summary>
    public double ContentHeight => Math.Max(1, PageHeight - MarginTop - MarginBottom);

    /// <summary>The same page turned: width and height swap, the margins turn with it.</summary>
    public SectionProperties WithOrientation(PageOrientation orientation)
    {
        if (orientation == Orientation)
        {
            return this;
        }

        return this with
        {
            Orientation = orientation,
            PageWidth = PageHeight,
            PageHeight = PageWidth,
        };
    }
}
