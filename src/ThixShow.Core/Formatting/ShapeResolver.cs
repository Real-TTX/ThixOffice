using ThixOffice.Text.Drawing;
using ThixShow.Core.Drawing;
using ThixShow.Core.Model;

namespace ThixShow.Core.Formatting;

/// <summary>
/// The look of a shape after inheritance: transform, geometry, fill, outline, effects, body properties and the list-style chain of
/// its text. Null fill/outline = nothing to draw.
/// </summary>
public sealed record ResolvedShape(
    SlideElement Shape,
    Transform2D Transform,
    ShapeGeometry Geometry,
    Fill? Fill,
    Outline? Outline,
    OuterShadow? Shadow,
    ColorContext Colors,
    BodyProperties Body,
    IReadOnlyList<ListStyle> TextStyles,
    RunProperties? StyleFont,
    TextBody? Text);

/// <summary>
/// Resolves a shape's effective properties (ECMA-376 Part 1, 19.3.1 and 20.1.4.2): its own spPr first, then the layout's and
/// master's placeholder it inherits from, then the theme styles its p:style references (fill/line/effect with the reference's
/// colour as phClr, font with the scheme font). The text style chain is: presentation default text style → the master's title /
/// body / other style (placeholders) → the master placeholder's, layout placeholder's and shape's list styles.
/// </summary>
public static class ShapeResolver
{
    /// <summary>The default insets of a text body (0.1" left/right, 0.05" top/bottom).</summary>
    public const long DefaultHorizontalInset = 91440;

    public const long DefaultVerticalInset = 45720;

    public static ResolvedShape Resolve(SlideContext context, SlideElement shape)
    {
        var placeholder = shape.Placeholder;
        var layoutShape = placeholder is null ? null : context.LayoutPlaceholder(placeholder);
        var masterShape = placeholder is null ? null : context.MasterPlaceholder(layoutShape?.Placeholder ?? placeholder);
        if (context.Kind == SlideKind.Master)
        {
            masterShape = null;
        }

        var chain = new[] { shape, layoutShape, masterShape }.Where(item => item is not null).Cast<SlideElement>().ToList();
        var transform = chain.Select(item => item.Transform).FirstOrDefault(item => item is not null) ?? new Transform2D(0, 0, 0, 0);
        var style = StyleOf(shape) ?? chain.Skip(1).Select(StyleOf).FirstOrDefault(item => item is not null);
        var formats = context.Theme.Formats;

        var geometry = chain.Select(GeometryOf).FirstOrDefault(item => item is not null) ?? ShapeGeometry.Rectangle;
        var fill = chain.Select(FillOf).FirstOrDefault(item => item is not null);
        var colors = context.Colors;
        if (fill is null && style is not null && formats.FillOf(style.Fill.Index) is { } styleFill)
        {
            fill = styleFill;
            colors = colors.WithPlaceholder(style.Fill.Color);
        }

        var fillColors = colors;
        var outline = ResolveOutline(chain, style, formats, out var outlineColor);
        var effects = chain.Select(EffectsOf).FirstOrDefault(item => item is not null);
        if (effects is null && style is not null && formats.EffectOf(style.Effect.Index) is { } styleEffects && styleEffects.Shadow is not null)
        {
            effects = styleEffects with { Shadow = styleEffects.Shadow with { Color = Substitute(styleEffects.Shadow.Color, style.Effect.Color) } };
        }

        var body = BodyProperties.Empty;
        foreach (var item in Enumerable.Reverse(chain))
        {
            body = body.Merge(TextOf(item)?.Body);
        }

        var styles = TextStyleChain(context, shape, chain);
        RunProperties? styleFont = null;
        if (style is { Font: { } font } && font.Index != "none")
        {
            styleFont = new RunProperties(
                Latin: new TextFont(font.Index == "major" ? "+mj-lt" : "+mn-lt"),
                EastAsian: new TextFont(font.Index == "major" ? "+mj-ea" : "+mn-ea"),
                ComplexScript: new TextFont(font.Index == "major" ? "+mj-cs" : "+mn-cs"),
                Fill: font.Color is null ? null : new SolidFill(font.Color));
        }

        if (outline is not null && outlineColor is not null && outline.Fill is SolidFill { Color.IsPlaceholder: true } or GradientFill)
        {
            outline = outline with { Fill = SubstituteFill(outline.Fill, outlineColor) };
        }

        if (fill is not null && !ReferenceEquals(fillColors, context.Colors))
        {
            fill = SubstituteFill(fill, fillColors.PlaceholderColor);
        }

        return new ResolvedShape(shape, transform, geometry, fill, outline, effects?.Shadow, context.Colors, body, styles, styleFont, TextOf(shape));
    }

    /// <summary>The fill of a slide's background: its own, the layout's, the master's (a theme reference resolved with its colour).</summary>
    public static Fill? ResolveBackground(SlideContext context)
    {
        var backgrounds = new[] { context.Slide.Background, context.Kind == SlideKind.Slide ? context.Layout?.Background : null, context.Master.Background };
        foreach (var background in backgrounds)
        {
            if (background is null)
            {
                continue;
            }

            if (background.Fill is { } fill)
            {
                return fill;
            }

            if (background.Reference is { } reference && context.Theme.Formats.FillOf(reference.Index) is { } themeFill)
            {
                return SubstituteFill(themeFill, reference.Color);
            }
        }

        return new SolidFill(ColorSpec.Scheme("bg1"));
    }

    /// <summary>The effective paragraph properties of a paragraph in a shape (chain + the paragraph's own).</summary>
    public static ParagraphProperties ParagraphOf(ResolvedShape shape, TextParagraph paragraph)
    {
        var level = Math.Clamp(paragraph.Properties.Level ?? 0, 0, 8);
        var properties = ParagraphProperties.Empty;
        foreach (var style in shape.TextStyles)
        {
            properties = properties.Merge(style.Default).Merge(style.LevelOf(level));
            if (ReferenceEquals(style, shape.TextStyles[0]) && shape.StyleFont is { } styleFont)
            {
                properties = properties with { DefaultRun = (properties.DefaultRun ?? RunProperties.Empty).Merge(styleFont) };
            }
        }

        return properties.Merge(paragraph.Properties) with { Level = level };
    }

    /// <summary>The effective run properties of a run in a paragraph with <paramref name="paragraph"/> effective properties.</summary>
    public static RunProperties RunOf(ParagraphProperties paragraph, RunProperties run) =>
        (paragraph.DefaultRun ?? RunProperties.Empty).Merge(run);

    /// <summary>A run's look in plain values: family (theme fonts resolved), size in points, colour …</summary>
    public static EffectiveRun Effective(RunProperties run, SlideContext context, double fontScale = 1)
    {
        var family = context.Theme.Fonts.Resolve(run.Latin?.Typeface ?? "+mn-lt");
        if (family.Length == 0)
        {
            family = context.Theme.Fonts.Minor.Latin;
        }

        var size = (run.Size ?? 1800) / 100.0 * fontScale;
        var color = ColorResolver.ResolveFill(run.Fill, context.Colors) ?? ColorResolver.Resolve(ColorSpec.Scheme("tx1"), context.Colors);
        if (run.Click is not null && run.Fill is null)
        {
            color = ColorResolver.Resolve(ColorSpec.Scheme("hlink"), context.Colors);
        }

        return new EffectiveRun(
            family,
            Math.Max(1, size),
            run.Bold ?? false,
            run.Italic ?? false,
            run.Underline is { } underline && underline != "none" || run.Click is not null,
            run.Strike is { } strike && strike != "noStrike",
            (run.Baseline ?? 0) / 100000.0,
            run.Caps ?? "none",
            (run.Spacing ?? 0) / 100.0 * fontScale,
            color,
            run.Highlight is null ? null : ColorResolver.Resolve(run.Highlight, context.Colors));
    }

    private static IReadOnlyList<ListStyle> TextStyleChain(SlideContext context, SlideElement shape, IReadOnlyList<SlideElement> chain)
    {
        var styles = new List<ListStyle> { context.Presentation.DefaultTextStyle };
        var placeholder = shape.Placeholder;
        if (context.Kind == SlideKind.Notes)
        {
            styles.Add(context.Master.TextStyles.Other);
        }
        else if (placeholder is not null)
        {
            styles.Add(placeholder.IsTitle ? context.Master.TextStyles.Title
                : placeholder.MasterType == PlaceholderType.Body ? context.Master.TextStyles.Body
                : context.Master.TextStyles.Other);
        }

        foreach (var item in chain.Skip(1).Reverse())
        {
            if (TextOf(item)?.ListStyle is { } inherited)
            {
                styles.Add(inherited);
            }
        }

        if (TextOf(shape)?.ListStyle is { } own)
        {
            styles.Add(own);
        }

        return styles;
    }

    private static Outline? ResolveOutline(IReadOnlyList<SlideElement> chain, ShapeStyle? style, FormatScheme formats, out ColorSpec? placeholderColor)
    {
        placeholderColor = null;
        Outline? outline = null;
        if (style is not null && formats.LineOf(style.Line.Index) is { } styleLine)
        {
            outline = styleLine;
            placeholderColor = style.Line.Color;
        }

        foreach (var item in Enumerable.Reverse(chain))
        {
            if (OutlineOf(item) is { } own)
            {
                outline = (outline ?? new Outline()).Merge(own);
            }
        }

        return outline;
    }

    private static ColorSpec Substitute(ColorSpec color, ColorSpec? placeholder)
    {
        if (!color.IsPlaceholder || placeholder is null)
        {
            return color;
        }

        return placeholder with { Modifiers = placeholder.Modifiers.AddRange(color.Modifiers) };
    }

    /// <summary>A theme style's fill with phClr replaced by <paramref name="placeholder"/>.</summary>
    public static Fill? SubstituteFill(Fill? fill, ColorSpec? placeholder) => fill switch
    {
        SolidFill solid => new SolidFill(Substitute(solid.Color, placeholder)),
        GradientFill gradient => gradient with { Stops = gradient.Stops.Select(stop => stop with { Color = Substitute(stop.Color, placeholder) }).ToList() },
        PatternFill pattern => pattern with
        {
            Foreground = pattern.Foreground is null ? null : Substitute(pattern.Foreground, placeholder),
            Background = pattern.Background is null ? null : Substitute(pattern.Background, placeholder),
        },
        _ => fill,
    };

    private static ShapeStyle? StyleOf(SlideElement shape) => shape switch
    {
        AutoShape auto => auto.Style,
        ConnectorShape connector => connector.Style,
        PictureShape picture => picture.Style,
        _ => null,
    };

    private static ShapeGeometry? GeometryOf(SlideElement shape) => shape switch
    {
        AutoShape auto => auto.Geometry,
        ConnectorShape connector => connector.Geometry,
        PictureShape picture => picture.Geometry,
        _ => null,
    };

    private static Fill? FillOf(SlideElement shape) => shape switch
    {
        AutoShape auto => auto.Fill,
        ConnectorShape connector => connector.Fill,
        GroupShape group => group.Fill,
        _ => null,
    };

    private static Outline? OutlineOf(SlideElement shape) => shape switch
    {
        AutoShape auto => auto.Outline,
        ConnectorShape connector => connector.Outline,
        PictureShape picture => picture.Outline,
        _ => null,
    };

    private static ShapeEffects? EffectsOf(SlideElement shape) => shape switch
    {
        AutoShape auto => auto.Effects,
        ConnectorShape connector => connector.Effects,
        PictureShape picture => picture.Effects,
        GroupShape group => group.Effects,
        _ => null,
    };

    private static TextBody? TextOf(SlideElement shape) => shape is AutoShape auto ? auto.Text : null;
}

/// <summary>A run's resolved look: font family and size (points), styles, baseline shift (fraction of the size), letter spacing (points), colours.</summary>
public sealed record EffectiveRun(
    string Family,
    double Size,
    bool Bold,
    bool Italic,
    bool Underline,
    bool Strike,
    double Baseline,
    string Caps,
    double Spacing,
    RgbColor Color,
    RgbColor? Highlight);
