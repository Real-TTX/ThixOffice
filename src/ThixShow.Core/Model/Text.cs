namespace ThixShow.Core.Model;

/// <summary>Paragraph alignment (a:pPr algn).</summary>
public enum TextAlignment
{
    Left,
    Center,
    Right,
    Justify,
    JustifyLow,
    Distributed,
    ThaiDistributed,
}

/// <summary>Vertical anchoring of the text in its box (a:bodyPr anchor).</summary>
public enum TextAnchor
{
    Top,
    Center,
    Bottom,
    Justified,
    Distributed,
}

/// <summary>A line or paragraph spacing: a percentage of the line (100000 = single) or an exact size in hundredths of a point.</summary>
public readonly record struct TextSpacing(int Value, bool IsPoints)
{
    public static TextSpacing Percent(int value) => new(value, false);

    public static TextSpacing Points(double points) => new((int)Math.Round(points * 100), true);

    public override string ToString() => IsPoints ? $"{Value / 100.0:0.##}pt" : $"{Value / 1000.0:0.#}%";
}

/// <summary>a:bodyPr's autofit: none, shrink text on overflow (normAutofit with the scale PowerPoint stored), resize shape.</summary>
public abstract record AutoFit;

/// <summary>a:noAutofit.</summary>
public sealed record NoAutoFit : AutoFit
{
    public static NoAutoFit Instance { get; } = new();
}

/// <summary>
/// a:normAutofit: the text shrinks to fit. <see cref="FontScale"/> (1000ths of a percent) and <see cref="LineSpacingReduction"/>
/// are the values the file stored; the layout recomputes them (<see cref="Layout.TextAutoFit"/>).
/// </summary>
public sealed record NormalAutoFit(int? FontScale = null, int? LineSpacingReduction = null) : AutoFit;

/// <summary>a:spAutoFit: the shape grows with its text.</summary>
public sealed record ShapeAutoFit : AutoFit
{
    public static ShapeAutoFit Instance { get; } = new();
}

/// <summary>
/// a:bodyPr: insets (EMU), anchoring, wrapping, vertical text ("horz", "vert", "vert270", "eaVert" … kept), rotation, columns and
/// autofit; every value optional so that a placeholder's body properties layer over the layout's and master's.
/// <see cref="Extra"/> keeps the element as read (a:prstTxWarp, a:scene3d, a:flatTx, unknown attributes).
/// </summary>
public sealed record BodyProperties(
    long? LeftInset = null,
    long? TopInset = null,
    long? RightInset = null,
    long? BottomInset = null,
    TextAnchor? Anchor = null,
    bool? AnchorCenter = null,
    bool? Wrap = null,
    string? Vertical = null,
    int? Rotation = null,
    int? Columns = null,
    long? ColumnSpacing = null,
    AutoFit? AutoFit = null,
    bool? Upright = null,
    bool? RightToLeftColumns = null,
    XmlFragment? Extra = null)
{
    public static BodyProperties Empty { get; } = new();

    /// <summary>These properties with <paramref name="over"/>'s where it has them.</summary>
    public BodyProperties Merge(BodyProperties? over) => over is null ? this : new BodyProperties(
        over.LeftInset ?? LeftInset,
        over.TopInset ?? TopInset,
        over.RightInset ?? RightInset,
        over.BottomInset ?? BottomInset,
        over.Anchor ?? Anchor,
        over.AnchorCenter ?? AnchorCenter,
        over.Wrap ?? Wrap,
        over.Vertical ?? Vertical,
        over.Rotation ?? Rotation,
        over.Columns ?? Columns,
        over.ColumnSpacing ?? ColumnSpacing,
        over.AutoFit ?? AutoFit,
        over.Upright ?? Upright,
        over.RightToLeftColumns ?? RightToLeftColumns,
        over.Extra ?? Extra);
}

/// <summary>A typeface of a run (a:latin, a:ea, a:cs, a:sym): the name ("+mn-lt" for theme fonts) and the font hints as read.</summary>
public sealed record TextFont(string Typeface, string? Panose = null, string? PitchFamily = null, string? CharacterSet = null);

/// <summary>What a link does: open an address, jump to a slide, or a show action (next/previous/first/last slide, end show).</summary>
public enum HyperlinkKind
{
    Url,
    Slide,
    NextSlide,
    PreviousSlide,
    FirstSlide,
    LastSlide,
    EndShow,
    Other,
}

/// <summary>
/// a:hlinkClick / a:hlinkMouseOver of a run or a shape. <see cref="Target"/>: the address (Url) or the slide's part as read
/// (Slide, resolved to <see cref="SlideId"/>); <see cref="Action"/> the ppaction:// string; <see cref="Extra"/> keeps sounds and
/// other children/attributes.
/// </summary>
public sealed record Hyperlink(HyperlinkKind Kind, string? Target = null, uint? SlideId = null, string? Action = null, string? Tooltip = null, XmlFragment? Extra = null)
{
    public static Hyperlink Url(string address, string? tooltip = null) => new(HyperlinkKind.Url, address, Tooltip: tooltip);

    public static Hyperlink ToSlide(uint slideId) => new(HyperlinkKind.Slide, SlideId: slideId, Action: "ppaction://hlinksldjump");
}

/// <summary>
/// a:rPr / a:defRPr / a:endParaRPr: every value optional (inheritance through list styles). <see cref="Size"/> in hundredths of a
/// point, <see cref="Baseline"/> in 1000ths of a percent (30000 = superscript), <see cref="Spacing"/> in hundredths of a point.
/// <see cref="Extra"/> keeps children ThixShow does not own (a:effectLst, a:highlight's extensions, a:uLn …) as one a:rPr.
/// </summary>
public sealed record RunProperties(
    int? Size = null,
    bool? Bold = null,
    bool? Italic = null,
    string? Underline = null,
    string? Strike = null,
    int? Baseline = null,
    string? Caps = null,
    int? Spacing = null,
    int? Kerning = null,
    string? Language = null,
    Fill? Fill = null,
    Outline? Outline = null,
    ColorSpec? Highlight = null,
    TextFont? Latin = null,
    TextFont? EastAsian = null,
    TextFont? ComplexScript = null,
    TextFont? Symbol = null,
    Hyperlink? Click = null,
    Hyperlink? Hover = null,
    string? AlternativeLanguage = null,
    bool? Dirty = null,
    bool? SpellingError = null,
    XmlFragment? Extra = null)
{
    public static RunProperties Empty { get; } = new();

    /// <summary>These properties with <paramref name="over"/>'s where it has them (links and file bookkeeping are not inherited).</summary>
    public RunProperties Merge(RunProperties? over) => over is null ? this : new RunProperties(
        over.Size ?? Size,
        over.Bold ?? Bold,
        over.Italic ?? Italic,
        over.Underline ?? Underline,
        over.Strike ?? Strike,
        over.Baseline ?? Baseline,
        over.Caps ?? Caps,
        over.Spacing ?? Spacing,
        over.Kerning ?? Kerning,
        over.Language ?? Language,
        over.Fill ?? Fill,
        over.Outline ?? Outline,
        over.Highlight ?? Highlight,
        over.Latin ?? Latin,
        over.EastAsian ?? EastAsian,
        over.ComplexScript ?? ComplexScript,
        over.Symbol ?? Symbol,
        over.Click,
        over.Hover,
        over.AlternativeLanguage ?? AlternativeLanguage,
        over.Dirty,
        over.SpellingError,
        over.Extra);

    /// <summary>The size in points, or null.</summary>
    public double? SizeInPoints => Size / 100.0;
}

/// <summary>A bullet (a:buNone, a:buChar, a:buAutoNum, a:buBlip).</summary>
public abstract record Bullet;

/// <summary>a:buNone.</summary>
public sealed record NoBullet : Bullet
{
    public static NoBullet Instance { get; } = new();
}

/// <summary>a:buChar: a character ("•", "–", "§" in Wingdings …).</summary>
public sealed record CharacterBullet(string Character) : Bullet;

/// <summary>a:buAutoNum: a numbering scheme ("arabicPeriod", "alphaLcParenR", "romanUcPeriod" …) starting at <see cref="StartAt"/>.</summary>
public sealed record AutoNumberBullet(string Scheme, int? StartAt = null) : Bullet;

/// <summary>a:buBlip: a picture bullet.</summary>
public sealed record PictureBullet(ImageData? Image) : Bullet;

/// <summary>A tab stop: position in EMU and alignment ("l", "ctr", "r", "dec").</summary>
public sealed record TabStop(long Position, string Alignment = "l");

/// <summary>
/// a:pPr / a:lvlNpPr: paragraph properties, every value optional. Margins and indent in EMU. Bullet colour/size/font: null =
/// inherit; <see cref="BulletColorFollowsText"/> / <see cref="BulletSizeFollowsText"/> / <see cref="BulletFontFollowsText"/> are
/// a:buClrTx / a:buSzTx / a:buFontTx; <see cref="BulletSize"/> is a percentage (1000ths) or points.
/// </summary>
public sealed record ParagraphProperties(
    int? Level = null,
    TextAlignment? Alignment = null,
    long? LeftMargin = null,
    long? RightMargin = null,
    long? Indent = null,
    long? DefaultTabSize = null,
    bool? RightToLeft = null,
    string? FontAlignment = null,
    bool? EastAsianLineBreak = null,
    bool? LatinLineBreak = null,
    bool? HangingPunctuation = null,
    TextSpacing? LineSpacing = null,
    TextSpacing? SpaceBefore = null,
    TextSpacing? SpaceAfter = null,
    ColorSpec? BulletColor = null,
    bool? BulletColorFollowsText = null,
    TextSpacing? BulletSize = null,
    bool? BulletSizeFollowsText = null,
    TextFont? BulletFont = null,
    bool? BulletFontFollowsText = null,
    Bullet? Bullet = null,
    ValueList<TabStop>? Tabs = null,
    RunProperties? DefaultRun = null,
    XmlFragment? Extra = null)
{
    public static ParagraphProperties Empty { get; } = new();

    /// <summary>These properties with <paramref name="over"/>'s where it has them (default run properties merge too).</summary>
    public ParagraphProperties Merge(ParagraphProperties? over)
    {
        if (over is null)
        {
            return this;
        }

        var bulletColorOver = over.BulletColor is not null || over.BulletColorFollowsText is not null;
        var bulletSizeOver = over.BulletSize is not null || over.BulletSizeFollowsText is not null;
        var bulletFontOver = over.BulletFont is not null || over.BulletFontFollowsText is not null;
        return new ParagraphProperties(
            over.Level ?? Level,
            over.Alignment ?? Alignment,
            over.LeftMargin ?? LeftMargin,
            over.RightMargin ?? RightMargin,
            over.Indent ?? Indent,
            over.DefaultTabSize ?? DefaultTabSize,
            over.RightToLeft ?? RightToLeft,
            over.FontAlignment ?? FontAlignment,
            over.EastAsianLineBreak ?? EastAsianLineBreak,
            over.LatinLineBreak ?? LatinLineBreak,
            over.HangingPunctuation ?? HangingPunctuation,
            over.LineSpacing ?? LineSpacing,
            over.SpaceBefore ?? SpaceBefore,
            over.SpaceAfter ?? SpaceAfter,
            bulletColorOver ? over.BulletColor : BulletColor,
            bulletColorOver ? over.BulletColorFollowsText : BulletColorFollowsText,
            bulletSizeOver ? over.BulletSize : BulletSize,
            bulletSizeOver ? over.BulletSizeFollowsText : BulletSizeFollowsText,
            bulletFontOver ? over.BulletFont : BulletFont,
            bulletFontOver ? over.BulletFontFollowsText : BulletFontFollowsText,
            over.Bullet ?? Bullet,
            over.Tabs ?? Tabs,
            (DefaultRun ?? RunProperties.Empty).Merge(over.DefaultRun),
            over.Extra ?? Extra);
    }
}

/// <summary>
/// a:lstStyle (and p:titleStyle, p:bodyStyle, p:otherStyle, p:defaultTextStyle): the default paragraph properties (a:defPPr)
/// and those of levels 1–9.
/// </summary>
public sealed record ListStyle(ParagraphProperties? Default, ValueList<ParagraphProperties?> Levels, XmlFragment? Extensions = null)
{
    public static ListStyle Empty { get; } = new(null, ValueList<ParagraphProperties?>.Empty);

    public bool IsEmpty => Default is null && Levels.All(level => level is null) && Extensions is null;

    /// <summary>The properties of level <paramref name="level"/> (0-based), or null.</summary>
    public ParagraphProperties? LevelOf(int level) => level >= 0 && level < Levels.Count ? Levels[level] : null;

    /// <summary>A list style with level <paramref name="level"/> (0-based) replaced.</summary>
    public ListStyle WithLevel(int level, ParagraphProperties? properties)
    {
        var levels = Levels.ToList();
        while (levels.Count <= level)
        {
            levels.Add(null);
        }

        levels[level] = properties;
        return this with { Levels = levels };
    }
}

/// <summary>A piece of a paragraph.</summary>
public abstract record TextRun
{
    /// <summary>The characters this run stands for in the paragraph's text (a line break is "\v", a preserved run "￼").</summary>
    public abstract string Text { get; }

    public abstract RunProperties Properties { get; }
}

/// <summary>a:r: text in one format.</summary>
public sealed record TextSpan(string Content, RunProperties RunProperties) : TextRun
{
    public override string Text => Content;

    public override RunProperties Properties => RunProperties;
}

/// <summary>a:br: a line break inside the paragraph (Umschalt+Eingabe).</summary>
public sealed record LineBreak(RunProperties RunProperties) : TextRun
{
    public const char Character = '\v';

    public override string Text => "\v";

    public override RunProperties Properties => RunProperties;
}

/// <summary>a:fld: a field (slide number "slidenum", date "datetime1" …) with its last text.</summary>
public sealed record TextField(string Id, string? Type, string Content, RunProperties RunProperties, ParagraphProperties? FieldParagraphProperties = null) : TextRun
{
    public override string Text => Content;

    public override RunProperties Properties => RunProperties;
}

/// <summary>A run ThixShow keeps but does not edit (a math zone inside mc:AlternateContent, unknown elements): one object character.</summary>
public sealed record PreservedRun(XmlFragment Xml) : TextRun
{
    public const char Character = '￼';

    public override string Text => "￼";

    public override RunProperties Properties => RunProperties.Empty;
}

/// <summary>a:p: paragraph properties, runs and the end-of-paragraph run properties (a:endParaRPr: the format of an empty line).</summary>
public sealed record TextParagraph(ParagraphProperties Properties, ValueList<TextRun> Runs, RunProperties? EndProperties = null)
{
    public static TextParagraph Empty { get; } = new(ParagraphProperties.Empty, ValueList<TextRun>.Empty);

    /// <summary>The paragraph as text ("\v" for line breaks).</summary>
    public string Text => string.Concat(Runs.Select(run => run.Text));

    public int Length => Runs.Sum(run => run.Text.Length);

    public int Level => Properties.Level ?? 0;

    /// <summary>A paragraph with plain text in one format.</summary>
    public static TextParagraph Plain(string text, RunProperties? properties = null, ParagraphProperties? paragraph = null) =>
        new(paragraph ?? ParagraphProperties.Empty, text.Length == 0 ? ValueList<TextRun>.Empty : [new TextSpan(text, properties ?? RunProperties.Empty)], properties);
}

/// <summary>
/// p:txBody / a:txBody: body properties, the shape's own list style and the paragraphs (at least one in a valid file).
/// </summary>
public sealed record TextBody(BodyProperties Body, ListStyle ListStyle, ValueList<TextParagraph> Paragraphs)
{
    public static TextBody Empty { get; } = new(BodyProperties.Empty, ListStyle.Empty, [TextParagraph.Empty]);

    /// <summary>A body with one paragraph per line of <paramref name="text"/> ("\n" separates paragraphs).</summary>
    public static TextBody FromText(string text, BodyProperties? body = null) =>
        new(body ?? BodyProperties.Empty, ListStyle.Empty, text.Split('\n').Select(line => TextParagraph.Plain(line)).ToList());

    /// <summary>All text, paragraphs separated by "\n".</summary>
    public string PlainText => string.Join("\n", Paragraphs.Select(paragraph => paragraph.Text));

    public bool IsEmpty => Paragraphs.All(paragraph => paragraph.Length == 0);
}
