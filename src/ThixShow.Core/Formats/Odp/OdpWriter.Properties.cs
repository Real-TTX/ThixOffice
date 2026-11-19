using System.Globalization;
using System.Xml.Linq;
using ThixOffice.Core.Formats.Odf;
using ThixShow.Core.Drawing;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

public static partial class OdpWriter
{
    /// <summary>
    /// The automatic styles of one XML part (content.xml or styles.xml): each distinct style written once, named with the part's
    /// prefix ("gr1", "P1" in content.xml; "Mgr1", "MP1" in styles.xml).
    /// </summary>
    private sealed class StyleSink(string prefix)
    {
        private readonly Dictionary<string, string> _byKey = new(StringComparer.Ordinal);
        private readonly Dictionary<string, int> _counters = new(StringComparer.Ordinal);

        public List<XElement> Styles { get; } = [];

        /// <summary>A style:style of <paramref name="family"/> with <paramref name="properties"/> (null when there is nothing to say and no parent).</summary>
        public string? Add(string family, string letters, IEnumerable<XElement?> properties, string? parent = null, IEnumerable<XAttribute>? extra = null)
        {
            var children = properties.Where(property => property is not null && (property.HasAttributes || property.HasElements)).Cast<XElement>().ToList();
            var attributes = extra?.ToList() ?? [];
            if (children.Count == 0 && parent is null && attributes.Count == 0)
            {
                return null;
            }

            var key = family + "|" + parent + "|" + string.Concat(attributes.Select(attribute => attribute.ToString())) + "|" + string.Concat(children.Select(child => child.ToString(SaveOptions.DisableFormatting)));
            if (_byKey.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = Next(letters);
            var style = new XElement(StyleNs + "style", new XAttribute(StyleNs + "name", name), new XAttribute(StyleNs + "family", family));
            if (parent is not null)
            {
                style.Add(new XAttribute(StyleNs + "parent-style-name", parent));
            }

            style.Add(attributes, children);
            Styles.Add(style);
            _byKey[key] = name;
            return name;
        }

        /// <summary>A text:list-style made of <paramref name="levels"/> (deduplicated like the styles).</summary>
        public string AddList(IEnumerable<XElement> levels)
        {
            var list = levels.ToList();
            var key = "list|" + string.Concat(list.Select(level => level.ToString(SaveOptions.DisableFormatting)));
            if (_byKey.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = Next("L");
            Styles.Add(new XElement(TextNs + "list-style", new XAttribute(StyleNs + "name", name), list));
            _byKey[key] = name;
            return name;
        }

        private string Next(string letters)
        {
            var count = _counters.GetValueOrDefault(letters) + 1;
            _counters[letters] = count;
            return prefix + letters + count.ToString(CultureInfo.InvariantCulture);
        }
    }

    private sealed partial class Writer
    {
        private readonly List<XElement> _named = [];
        private readonly Dictionary<string, string> _namedByKey = new(StringComparer.Ordinal);
        private readonly SortedSet<string> _fonts = new(StringComparer.Ordinal);

        // ------------------------------------------------------------ colours

        private static string HexOf(ColorSpec color, ColorContext colors)
        {
            var rgb = ColorResolver.Resolve(color, colors);
            return $"#{rgb.R:X2}{rgb.G:X2}{rgb.B:X2}";
        }

        /// <summary>The main colour of a fill (a gradient's first stop), or null.</summary>
        private static ColorSpec? ColorOfFill(Fill? fill) => fill switch
        {
            SolidFill solid => solid.Color,
            GradientFill { Stops.Count: > 0 } gradient => gradient.Stops[0].Color,
            PatternFill pattern => pattern.Foreground,
            _ => null,
        };

        // ------------------------------------------------------------ fills

        /// <summary>The draw:fill attributes of <paramref name="fill"/> (nothing for null: inherited).</summary>
        private void AddFill(XElement properties, Fill? fill, ColorContext colors)
        {
            switch (fill)
            {
                case null or GroupFill:
                    return;
                case NoFill:
                    properties.Add(new XAttribute(DrawNs + "fill", "none"));
                    return;
                case SolidFill solid:
                    properties.Add(new XAttribute(DrawNs + "fill", "solid"), new XAttribute(DrawNs + "fill-color", HexOf(solid.Color, colors)));
                    if (solid.Color.Opacity < 1)
                    {
                        properties.Add(new XAttribute(DrawNs + "opacity", Percent(solid.Color.Opacity)));
                    }

                    return;
                case GradientFill gradient when gradient.Stops.Count > 0:
                    properties.Add(new XAttribute(DrawNs + "fill", "gradient"), new XAttribute(DrawNs + "fill-color", HexOf(gradient.Stops[0].Color, colors)),
                        new XAttribute(DrawNs + "fill-gradient-name", Gradient(gradient, colors)));
                    return;
                case PictureFill { Image: { } image } picture:
                    properties.Add(new XAttribute(DrawNs + "fill", "bitmap"), new XAttribute(DrawNs + "fill-image-name", FillImage(image)),
                        new XAttribute(StyleNs + "repeat", picture.Mode is TileMode ? "repeat" : "stretch"));
                    return;
                case PatternFill { Foreground: { } foreground }:
                    properties.Add(new XAttribute(DrawNs + "fill", "solid"), new XAttribute(DrawNs + "fill-color", HexOf(foreground, colors)));
                    return;
                default:
                    properties.Add(new XAttribute(DrawNs + "fill", "none"));
                    return;
            }
        }

        /// <summary>A draw:gradient in office:styles (radial ones run from the edge inwards in ODF: their stops are reversed).</summary>
        private string Gradient(GradientFill gradient, ColorContext colors)
        {
            var radial = gradient.PathKind is not null;
            var stops = gradient.Stops.OrderBy(stop => stop.Position).Select(stop => (Offset: stop.Position / 100000.0, Color: HexOf(stop.Color, colors))).ToList();
            if (radial)
            {
                stops = stops.Select(stop => (1 - stop.Offset, stop.Color)).Reverse().ToList();
            }

            var element = new XElement(DrawNs + "gradient",
                new XAttribute(DrawNs + "style", gradient.PathKind switch
                {
                    GradientPathKind.Circle => "radial",
                    GradientPathKind.Rectangle or GradientPathKind.Shape => "rectangular",
                    _ => "linear",
                }));
            if (radial && gradient.FillToRect is { } focus)
            {
                element.Add(new XAttribute(DrawNs + "cx", Percent((focus.Left + (100000 - focus.Right)) / 200000.0)),
                    new XAttribute(DrawNs + "cy", Percent((focus.Top + (100000 - focus.Bottom)) / 200000.0)));
            }

            element.Add(new XAttribute(DrawNs + "start-color", stops[0].Color), new XAttribute(DrawNs + "end-color", stops[^1].Color),
                new XAttribute(DrawNs + "start-intensity", "100%"), new XAttribute(DrawNs + "end-intensity", "100%"));
            var degrees = radial ? 0 : ((90 - ((gradient.LinearAngle ?? 0) / 60000.0)) % 360 + 360) % 360;
            element.Add(new XAttribute(DrawNs + "angle", degrees.ToString("0.#####", CultureInfo.InvariantCulture) + "deg"), new XAttribute(DrawNs + "border", "0%"));
            foreach (var (offset, color) in stops)
            {
                element.Add(new XElement(LoExtNs + "gradient-stop", new XAttribute(SvgNs + "offset", offset.ToString("0.#####", CultureInfo.InvariantCulture)),
                    new XAttribute(LoExtNs + "color-type", "rgb"), new XAttribute(LoExtNs + "color-value", color)));
            }

            return Named(element, "Gradient");
        }

        private string FillImage(ImageData image)
        {
            var path = Picture(image);
            return Named(new XElement(DrawNs + "fill-image", new XAttribute(XLinkNs + "href", path), new XAttribute(XLinkNs + "type", "simple"),
                new XAttribute(XLinkNs + "show", "embed"), new XAttribute(XLinkNs + "actuate", "onLoad")), "Bitmap");
        }

        /// <summary>A shared element of office:styles (gradient, picture, dash, marker), written once per content, named in order.</summary>
        private string Named(XElement element, string kind, string? fixedName = null)
        {
            var key = kind + "|" + element.ToString(SaveOptions.DisableFormatting);
            if (_namedByKey.TryGetValue(key, out var existing))
            {
                return existing;
            }

            var name = fixedName ?? $"{kind}_{_named.Count(item => item.Name == element.Name) + 1}";
            var attributes = element.Attributes().ToList();
            element.RemoveAttributes();
            element.Add(new XAttribute(DrawNs + "name", name), new XAttribute(DrawNs + "display-name", name.Replace("_20_", " ", StringComparison.Ordinal).Replace('_', ' ')), attributes);
            _named.Add(element);
            _namedByKey[key] = name;
            return name;
        }

        // ------------------------------------------------------------ outlines

        /// <summary>The stroke attributes of <paramref name="outline"/> (nothing for null).</summary>
        private void AddStroke(XElement properties, Outline? outline, ColorContext colors)
        {
            if (outline is null)
            {
                return;
            }

            var dashed = outline.Dash is { IsSolid: false };
            if (outline.Fill is NoFill)
            {
                properties.Add(new XAttribute(DrawNs + "stroke", "none"));
            }
            else if (dashed)
            {
                properties.Add(new XAttribute(DrawNs + "stroke", "dash"), new XAttribute(DrawNs + "stroke-dash", Dash(outline.Dash!, outline.Cap ?? LineCap.Flat)));
            }
            else if (outline.Fill is not null || outline.Dash is not null)
            {
                properties.Add(new XAttribute(DrawNs + "stroke", "solid"));
            }

            if (ColorOfFill(outline.Fill) is { } color)
            {
                properties.Add(new XAttribute(SvgNs + "stroke-color", HexOf(color, colors)));
                if (color.Opacity < 1)
                {
                    properties.Add(new XAttribute(SvgNs + "stroke-opacity", Percent(color.Opacity)));
                }
            }

            if (outline.Width is { } width)
            {
                properties.Add(new XAttribute(SvgNs + "stroke-width", OdpNames.Cm(width)));
            }

            if (outline.Cap is { } cap)
            {
                properties.Add(new XAttribute(SvgNs + "stroke-linecap", cap switch
                {
                    LineCap.Round => "round",
                    LineCap.Square => "square",
                    _ => "butt",
                }));
            }

            if (outline.Join is { } join)
            {
                properties.Add(new XAttribute(DrawNs + "stroke-linejoin", join switch
                {
                    LineJoin.Round => "round",
                    LineJoin.Bevel => "bevel",
                    _ => "miter",
                }));
            }

            var lineWidth = outline.Width ?? 9525;
            if (outline.Head is { Kind: not LineEndKind.None } head)
            {
                properties.Add(new XAttribute(DrawNs + "marker-start", Marker(head.Kind)), new XAttribute(DrawNs + "marker-start-width", OdpNames.Cm((long)Math.Round(lineWidth * OdpNames.MarkerFactor(head.Width)))));
            }

            if (outline.Tail is { Kind: not LineEndKind.None } tail)
            {
                properties.Add(new XAttribute(DrawNs + "marker-end", Marker(tail.Kind)), new XAttribute(DrawNs + "marker-end-width", OdpNames.Cm((long)Math.Round(lineWidth * OdpNames.MarkerFactor(tail.Width)))));
            }
        }

        private string Marker(LineEndKind kind)
        {
            var (viewBox, path) = OdpNames.MarkerShape(kind);
            var element = new XElement(DrawNs + "marker", new XAttribute(SvgNs + "viewBox", viewBox), new XAttribute(SvgNs + "d", path));
            return Named(element, "Marker", OdpNames.MarkerName(kind));
        }

        /// <summary>A draw:stroke-dash of a dash pattern: lengths in percent of the line width.</summary>
        private string Dash(LineDash dash, LineCap cap)
        {
            var pattern = dash.Pattern(cap);
            if (pattern.Length < 2)
            {
                pattern = [4, 3];
            }

            var first = pattern[0];
            var distance = pattern[1];
            var rest = pattern.Skip(2).Where((_, index) => index % 2 == 0).ToList();
            var element = new XElement(DrawNs + "stroke-dash", new XAttribute(DrawNs + "style", cap == LineCap.Flat ? "rect" : "round"),
                new XAttribute(DrawNs + "dots1", 1), new XAttribute(DrawNs + "dots1-length", Percent(first)));
            if (rest.Count > 0)
            {
                element.Add(new XAttribute(DrawNs + "dots2", rest.Count), new XAttribute(DrawNs + "dots2-length", Percent(rest[0])));
            }

            element.Add(new XAttribute(DrawNs + "distance", Percent(distance)));
            return Named(element, "Dash", dash.Preset is { } preset ? "Dash_" + preset : null);
        }

        // ------------------------------------------------------------ effects and body

        private static void AddShadow(XElement properties, ShapeEffects? effects, ColorContext colors)
        {
            if (effects?.Shadow is not { } shadow)
            {
                return;
            }

            var angle = shadow.Direction / 60000.0 * Math.PI / 180;
            properties.Add(new XAttribute(DrawNs + "shadow", "visible"),
                new XAttribute(DrawNs + "shadow-offset-x", OdpNames.Cm(shadow.Distance * Math.Cos(angle))),
                new XAttribute(DrawNs + "shadow-offset-y", OdpNames.Cm(shadow.Distance * Math.Sin(angle))),
                new XAttribute(DrawNs + "shadow-color", HexOf(shadow.Color, colors)),
                new XAttribute(DrawNs + "shadow-opacity", Percent(shadow.Color.Opacity)),
                new XAttribute(LoExtNs + "shadow-blur", OdpNames.Cm(shadow.BlurRadius)));
        }

        /// <summary>The text frame attributes of body properties (insets, anchoring, wrapping, direction, autofit).</summary>
        private static void AddBody(XElement properties, BodyProperties? body)
        {
            if (body is null)
            {
                return;
            }

            void Inset(string side, long? value)
            {
                if (value is { } inset)
                {
                    properties.Add(new XAttribute(FoNs + "padding-" + side, OdpNames.Cm(inset)));
                }
            }

            Inset("left", body.LeftInset);
            Inset("top", body.TopInset);
            Inset("right", body.RightInset);
            Inset("bottom", body.BottomInset);
            if (body.Anchor is { } anchor)
            {
                properties.Add(new XAttribute(DrawNs + "textarea-vertical-align", anchor switch
                {
                    TextAnchor.Center => "middle",
                    TextAnchor.Bottom => "bottom",
                    TextAnchor.Justified or TextAnchor.Distributed => "justify",
                    _ => "top",
                }));
            }

            if (body.AnchorCenter is { } center)
            {
                properties.Add(new XAttribute(DrawNs + "textarea-horizontal-align", center ? "center" : "justify"));
            }

            if (body.Wrap is { } wrap)
            {
                properties.Add(new XAttribute(FoNs + "wrap-option", wrap ? "wrap" : "no-wrap"));
            }

            if (body.Vertical is { } vertical)
            {
                properties.Add(new XAttribute(StyleNs + "writing-mode", vertical switch
                {
                    "vert" or "eaVert" or "wordArtVertRtl" or "mongolianVert" or "wordArtVert" => "tb-rl",
                    "vert270" => "bt-lr",
                    _ => "lr-tb",
                }));
            }

            switch (body.AutoFit)
            {
                case NoAutoFit:
                    properties.Add(new XAttribute(DrawNs + "auto-grow-height", "false"));
                    break;
                case NormalAutoFit:
                    properties.Add(new XAttribute(DrawNs + "fit-to-size", "false"), new XAttribute(StyleNs + "shrink-to-fit", "true"));
                    break;
                case ShapeAutoFit:
                    properties.Add(new XAttribute(DrawNs + "auto-grow-height", "true"));
                    break;
            }
        }

        // ------------------------------------------------------------ text

        /// <summary>style:paragraph-properties of a paragraph's own properties (null when it has none ODF can say).</summary>
        private static XElement? ParagraphProperties(ParagraphProperties? paragraph)
        {
            if (paragraph is null)
            {
                return null;
            }

            var element = new XElement(StyleNs + "paragraph-properties");
            if (paragraph.Alignment is { } alignment)
            {
                element.Add(new XAttribute(FoNs + "text-align", alignment switch
                {
                    TextAlignment.Center => "center",
                    TextAlignment.Right => "end",
                    TextAlignment.Left => "start",
                    _ => "justify",
                }));
            }

            if (paragraph.LeftMargin is { } left)
            {
                element.Add(new XAttribute(FoNs + "margin-left", OdpNames.Cm(left)));
            }

            if (paragraph.RightMargin is { } right)
            {
                element.Add(new XAttribute(FoNs + "margin-right", OdpNames.Cm(right)));
            }

            if (paragraph.Indent is { } indent)
            {
                element.Add(new XAttribute(FoNs + "text-indent", OdpNames.Cm(indent)));
            }

            // Spacing in lines (DrawingML's percentages) becomes points: a line is 1.2 × the font size.
            var fontSize = (paragraph.DefaultRun?.Size ?? 1800) / 100.0;
            double PointsOf(TextSpacing spacing) => spacing.IsPoints ? spacing.Value / 100.0 : spacing.Value / 100000.0 * fontSize * 1.2;
            if (paragraph.SpaceBefore is { } before)
            {
                element.Add(new XAttribute(FoNs + "margin-top", Length(PointsOf(before))));
            }

            if (paragraph.SpaceAfter is { } after)
            {
                element.Add(new XAttribute(FoNs + "margin-bottom", Length(PointsOf(after))));
            }

            if (paragraph.LineSpacing is { } spacing)
            {
                element.Add(new XAttribute(FoNs + "line-height", spacing.IsPoints ? Length(spacing.Value / 100.0) : Percent(spacing.Value / 100000.0)));
            }

            return element;
        }

        /// <summary>style:text-properties of run properties (null when empty).</summary>
        private XElement? TextProperties(RunProperties? run, SlideContextColors context)
        {
            if (run is null)
            {
                return null;
            }

            var family = run.Latin is { } latin ? context.Font(latin.Typeface) : null;
            if (family is not null)
            {
                _fonts.Add(family);
            }

            var underline = run.Underline is { } value ? OdpNames.Underline(value) : ((string, bool)?)null;
            var font = new OdfFont
            {
                Family = family,
                Size = run.Size / 100.0,
                Bold = run.Bold,
                Italic = run.Italic,
                Underline = underline?.Item1,
                UnderlineDouble = underline?.Item2 ?? false,
                Strikethrough = run.Strike is { } strike ? strike != "noStrike" : null,
                Position = run.Baseline is { } baseline ? baseline / 1000.0 : null,
                Color = ColorOfFill(run.Fill) is { } color ? HexOf(color, context.Colors)[1..] : null,
            };
            var element = font.Write();
            if (run.Caps is { } caps)
            {
                element.Add(caps == "small"
                    ? new XAttribute(FoNs + "font-variant", "small-caps")
                    : new XAttribute(FoNs + "text-transform", caps == "all" ? "uppercase" : "none"));
            }

            if (run.Spacing is { } spacing)
            {
                element.Add(new XAttribute(FoNs + "letter-spacing", Length(spacing / 100.0)));
            }

            if (run.Language is { } language)
            {
                var parts = language.Split('-');
                element.Add(new XAttribute(FoNs + "language", parts[0]));
                if (parts.Length > 1)
                {
                    element.Add(new XAttribute(FoNs + "country", parts[1]));
                }
            }

            if (run.Highlight is { } highlight)
            {
                element.Add(new XAttribute(FoNs + "background-color", HexOf(highlight, context.Colors)));
            }

            if (run.EastAsian is { } eastAsian && context.Font(eastAsian.Typeface) is { } asian)
            {
                _fonts.Add(asian);
                element.Add(new XAttribute(StyleNs + "font-name-asian", asian));
            }

            if (run.ComplexScript is { } complex && context.Font(complex.Typeface) is { } complexFamily)
            {
                _fonts.Add(complexFamily);
                element.Add(new XAttribute(StyleNs + "font-name-complex", complexFamily));
            }

            return element.HasAttributes ? element : null;
        }

        /// <summary>A text:list-level-style-* of a bullet at <paramref name="level"/> (1-based).</summary>
        private XElement? ListLevel(int level, ParagraphProperties effective, SlideContextColors context)
        {
            var properties = new XElement(StyleNs + "list-level-properties");
            if (effective.LeftMargin is { } margin)
            {
                properties.Add(new XAttribute(TextNs + "space-before", OdpNames.Cm(Math.Max(0, margin + Math.Min(0, effective.Indent ?? 0)))));
            }

            if (effective.Indent is < 0 and var indent)
            {
                properties.Add(new XAttribute(TextNs + "min-label-width", OdpNames.Cm(-indent)));
            }

            var text = new XElement(StyleNs + "text-properties");
            if (effective.BulletFont is { } font && context.Font(font.Typeface) is { } family)
            {
                _fonts.Add(family);
                text.Add(new XAttribute(FoNs + "font-family", family));
            }

            if (effective.BulletColor is { } color)
            {
                text.Add(new XAttribute(FoNs + "color", HexOf(color, context.Colors)));
            }

            if (effective.BulletSize is { IsPoints: false } size)
            {
                text.Add(new XAttribute(FoNs + "font-size", Percent(size.Value / 100000.0)));
            }

            switch (effective.Bullet)
            {
                case CharacterBullet character:
                    return new XElement(TextNs + "list-level-style-bullet", new XAttribute(TextNs + "level", level),
                        new XAttribute(TextNs + "bullet-char", character.Character.Length > 0 ? character.Character[..1] : "•"), properties, text.HasAttributes ? text : null);
                case AutoNumberBullet number:
                    var (format, prefix, suffix) = OdpNames.Numbering(number.Scheme);
                    var element = new XElement(TextNs + "list-level-style-number", new XAttribute(TextNs + "level", level), new XAttribute(StyleNs + "num-format", format));
                    if (prefix.Length > 0)
                    {
                        element.Add(new XAttribute(StyleNs + "num-prefix", prefix));
                    }

                    if (suffix.Length > 0)
                    {
                        element.Add(new XAttribute(StyleNs + "num-suffix", suffix));
                    }

                    if (number.StartAt is { } start)
                    {
                        element.Add(new XAttribute(TextNs + "start-value", start));
                    }

                    element.Add(properties, text.HasAttributes ? text : null);
                    return element;
                case PictureBullet:
                    return new XElement(TextNs + "list-level-style-bullet", new XAttribute(TextNs + "level", level), new XAttribute(TextNs + "bullet-char", "•"), properties);
                default:
                    return null;
            }
        }
    }

    /// <summary>The colours and theme fonts of the slide being written.</summary>
    private sealed record SlideContextColors(ColorContext Colors, FontScheme Fonts)
    {
        /// <summary>The family of a typeface ("+mn-lt" resolved), null for an empty name.</summary>
        public string? Font(string typeface)
        {
            var family = Fonts.Resolve(typeface);
            return string.IsNullOrWhiteSpace(family) ? null : family;
        }
    }
}
