using System.Globalization;
using System.Xml.Linq;
using ThixOffice.Core.Formats.Odf;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

public static partial class OdpReader
{
    /// <summary>Which part a style reference is resolved in: styles.xml (master pages) or content.xml (slides).</summary>
    private enum Part
    {
        Styles,
        Content,
    }

    /// <summary>
    /// The merged properties of a style chain: one element per property kind (graphic, paragraph, text, drawing-page, table-cell,
    /// table-column, table-row) whose attributes are the chain's, the leaf winning; the text:list-style inside graphic properties.
    /// </summary>
    private sealed class Props
    {
        private readonly Dictionary<string, XElement> _elements = new(StringComparer.Ordinal);

        public static Props Empty { get; } = new();

        public XElement? ListStyle { get; private set; }

        public XElement? Of(string kind) => _elements.GetValueOrDefault(kind);

        public string? Attribute(string kind, XName name) => (string?)Of(kind)?.Attribute(name);

        public string? Graphic(XName name) => Attribute("graphic-properties", name);

        public bool IsEmpty => _elements.Count == 0 && ListStyle is null;

        public void Merge(XElement style)
        {
            foreach (var properties in style.Elements().Where(element => element.Name.Namespace == StyleNs && element.Name.LocalName.EndsWith("-properties", StringComparison.Ordinal)))
            {
                if (!_elements.TryGetValue(properties.Name.LocalName, out var merged))
                {
                    merged = new XElement(properties.Name);
                    _elements[properties.Name.LocalName] = merged;
                }

                foreach (var attribute in properties.Attributes())
                {
                    merged.SetAttributeValue(attribute.Name, attribute.Value);
                }

                if (properties.Element(TextNs + "list-style") is { } list)
                {
                    ListStyle = list;
                }
            }
        }
    }

    /// <summary>Every style of the package by family and name, the named drawing styles (gradients, pictures, dashes, markers) and font faces.</summary>
    private sealed class StyleIndex
    {
        private readonly Dictionary<string, XElement> _common = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _stylesAutomatic = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _contentAutomatic = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _defaults = new(StringComparer.Ordinal);
        private readonly Dictionary<string, XElement> _named = new(StringComparer.Ordinal);
        private readonly Dictionary<string, string> _fonts = new(StringComparer.Ordinal);

        public StyleIndex(XDocument content, XDocument? styles)
        {
            foreach (var document in new[] { styles, content })
            {
                foreach (var face in document?.Root?.Element(OfficeNs + "font-face-decls")?.Elements(StyleNs + "font-face") ?? [])
                {
                    if ((string?)face.Attribute(StyleNs + "name") is { } name)
                    {
                        _fonts[name] = ((string?)face.Attribute(SvgNs + "font-family") ?? name).Trim().Trim('\'', '"');
                    }
                }
            }

            foreach (var element in styles?.Root?.Element(OfficeNs + "styles")?.Elements() ?? [])
            {
                Register(element, _common);
            }

            foreach (var element in styles?.Root?.Element(OfficeNs + "automatic-styles")?.Elements() ?? [])
            {
                Register(element, _stylesAutomatic);
            }

            foreach (var element in content.Root?.Element(OfficeNs + "automatic-styles")?.Elements() ?? [])
            {
                Register(element, _contentAutomatic);
            }

            foreach (var element in content.Root?.Element(OfficeNs + "styles")?.Elements() ?? [])
            {
                Register(element, _common);
            }
        }

        private void Register(XElement element, Dictionary<string, XElement> styles)
        {
            if (element.Name == StyleNs + "default-style" && (string?)element.Attribute(StyleNs + "family") is { } defaultFamily)
            {
                _defaults[defaultFamily] = element;
                return;
            }

            if (element.Name.Namespace == DrawNs && (string?)element.Attribute(DrawNs + "name") is { } drawName)
            {
                _named[element.Name.LocalName + "|" + drawName] = element;
                return;
            }

            if ((string?)element.Attribute(StyleNs + "name") is not { } name)
            {
                return;
            }

            if (element.Name == TextNs + "list-style")
            {
                styles["list|" + name] = element;
            }
            else if (element.Name == StyleNs + "style" || element.Name == StyleNs + "page-layout")
            {
                styles[((string?)element.Attribute(StyleNs + "family") ?? element.Name.LocalName) + "|" + name] = element;
            }
        }

        /// <summary>The family of a font face name (its svg:font-family), or the name itself.</summary>
        public string FontFamily(string name) => _fonts.TryGetValue(name, out var family) ? family : name.Trim().Trim('\'', '"');

        public XElement? Named(string kind, string? name) => name is null ? null : _named.GetValueOrDefault(kind + "|" + name);

        /// <summary>A style of the part (its automatic styles first) or of office:styles.</summary>
        public XElement? Find(string family, string? name, Part part, out bool automatic)
        {
            automatic = false;
            if (name is null)
            {
                return null;
            }

            var key = family + "|" + name;
            var local = part == Part.Content ? _contentAutomatic : _stylesAutomatic;
            if (local.TryGetValue(key, out var style))
            {
                automatic = true;
                return style;
            }

            return _common.GetValueOrDefault(key);
        }

        public XElement? PageLayout(string? name) => name is null ? null : _stylesAutomatic.GetValueOrDefault("page-layout|" + name) ?? _common.GetValueOrDefault("page-layout|" + name);

        public XElement? ListStyle(string? name, Part part) =>
            name is null ? null : (part == Part.Content ? _contentAutomatic : _stylesAutomatic).GetValueOrDefault("list|" + name) ?? _common.GetValueOrDefault("list|" + name);

        /// <summary>
        /// The properties of a style and its parents (leaf wins). <paramref name="ownOnly"/>: the style alone. A chain that reaches
        /// ThixShow's base style stops before it (its values are the model's defaults); one that does not gets the family's default style.
        /// </summary>
        public Props Resolve(string family, string? name, Part part, bool ownOnly = false)
        {
            var props = new Props();
            var chain = new List<XElement>();
            var reachedBase = false;
            var current = Find(family, name, part, out _);
            var seen = new HashSet<XElement>();
            while (current is not null && seen.Add(current))
            {
                if ((string?)current.Attribute(StyleNs + "name") == OdpNames.BaseStyleName)
                {
                    reachedBase = true;
                    break;
                }

                chain.Add(current);
                if (ownOnly)
                {
                    reachedBase = true;
                    break;
                }

                current = Find(family, (string?)current.Attribute(StyleNs + "parent-style-name"), part, out _);
            }

            if (!reachedBase && _defaults.TryGetValue(family, out var fallback))
            {
                props.Merge(fallback);
            }

            for (var index = chain.Count - 1; index >= 0; index--)
            {
                props.Merge(chain[index]);
            }

            return props;
        }

        /// <summary>The base style's properties (the presentation's default text), or null when the file has none.</summary>
        public Props? Base() => _common.TryGetValue("graphic|" + OdpNames.BaseStyleName, out var style) ? Merged(style) : null;

        private static Props Merged(XElement style)
        {
            var props = new Props();
            props.Merge(style);
            return props;
        }
    }

    private sealed partial class Reader
    {
        // ------------------------------------------------------------ colours, fills, outlines

        private static ColorSpec? Color(string? value, string? opacity = null)
        {
            if (ColorOf(value) is not { } hex)
            {
                return null;
            }

            var color = ColorSpec.Rgb(hex);
            return PercentOf(opacity) is { } alpha && alpha < 1 ? color.WithOpacity(alpha) : color;
        }

        /// <summary>The fill of graphic (or drawing-page) properties; null when they say nothing.</summary>
        private Fill? ReadFill(XElement? properties)
        {
            if (properties is null)
            {
                return null;
            }

            var kind = (string?)properties.Attribute(DrawNs + "fill");
            switch (kind)
            {
                case "none":
                    return NoFill.Instance;
                case "solid":
                    return Color((string?)properties.Attribute(DrawNs + "fill-color"), (string?)properties.Attribute(DrawNs + "opacity")) is { } solid
                        ? new SolidFill(solid)
                        : new SolidFill(ColorSpec.Rgb("729FCF"));
                case "gradient":
                    if (ReadGradient(_index.Named("gradient", (string?)properties.Attribute(DrawNs + "fill-gradient-name"))) is { } gradient)
                    {
                        return gradient;
                    }

                    return Color((string?)properties.Attribute(DrawNs + "fill-color")) is { } color ? new SolidFill(color) : NoFill.Instance;
                case "bitmap":
                    var image = _index.Named("fill-image", (string?)properties.Attribute(DrawNs + "fill-image-name"));
                    if (image is not null && Picture((string?)image.Attribute(XLinkNs + "href")) is { } data)
                    {
                        PictureFillMode mode = (string?)properties.Attribute(StyleNs + "repeat") == "repeat" ? new TileMode() : StretchMode.Full;
                        return new PictureFill(data, RelativeRect.Zero, mode);
                    }

                    return NoFill.Instance;
                case "hatch":
                    var hatch = _index.Named("hatch", (string?)properties.Attribute(DrawNs + "fill-hatch-name"));
                    var background = (string?)properties.Attribute(DrawNs + "fill-hatch-solid") == "true" ? Color((string?)properties.Attribute(DrawNs + "fill-color")) : null;
                    return new PatternFill("ltUpDiag", Color((string?)hatch?.Attribute(DrawNs + "color")) ?? ColorSpec.Rgb("000000"), background ?? ColorSpec.Rgb("FFFFFF"));
                default:
                    return null;
            }
        }

        /// <summary>A draw:gradient as a gradient fill (loext:gradient-stop when present, else start and end colour; axial mirrored).</summary>
        private static GradientFill? ReadGradient(XElement? gradient)
        {
            if (gradient is null)
            {
                return null;
            }

            var style = (string?)gradient.Attribute(DrawNs + "style") ?? "linear";
            var radial = style is "radial" or "ellipsoid" or "square" or "rectangular";
            var stops = new List<(double Offset, ColorSpec Color)>();
            foreach (var stop in gradient.Elements(LoExtNs + "gradient-stop"))
            {
                if (Color((string?)stop.Attribute(LoExtNs + "color-value")) is { } color && NumberOf((string?)stop.Attribute(SvgNs + "offset")) is { } offset)
                {
                    stops.Add((offset, color));
                }
            }

            if (stops.Count < 2)
            {
                var start = Color((string?)gradient.Attribute(DrawNs + "start-color")) ?? ColorSpec.Rgb("000000");
                var end = Color((string?)gradient.Attribute(DrawNs + "end-color")) ?? ColorSpec.Rgb("FFFFFF");
                var border = PercentOf((string?)gradient.Attribute(DrawNs + "border")) ?? 0;
                stops = style == "axial" ? [(0, start), (0.5, end), (1, start)] : border > 0 ? [(0, start), (border, start), (1, end)] : [(0, start), (1, end)];
            }

            if (radial)
            {
                stops = stops.Select(stop => (1 - stop.Offset, stop.Color)).Reverse().ToList();
            }

            var model = stops.Select(stop => new GradientStop((int)Math.Round(stop.Offset * 100000), stop.Color)).ToList();
            if (radial)
            {
                RelativeRect? focus = null;
                if (PercentOf((string?)gradient.Attribute(DrawNs + "cx")) is { } cx && PercentOf((string?)gradient.Attribute(DrawNs + "cy")) is { } cy)
                {
                    var (x, y) = ((int)Math.Round(cx * 100000), (int)Math.Round(cy * 100000));
                    focus = new RelativeRect(x, y, 100000 - x, 100000 - y);
                }

                return new GradientFill(model, LinearAngle: null, PathKind: style is "radial" or "ellipsoid" ? GradientPathKind.Circle : GradientPathKind.Rectangle, FillToRect: focus);
            }

            var text = (string?)gradient.Attribute(DrawNs + "angle");
            var degrees = text is null ? 0 : text.Any(char.IsLetter) ? Degrees(text) ?? 0 : (NumberOf(text) ?? 0) / 10;
            var angle = (((90 - degrees) % 360) + 360) % 360;
            return new GradientFill(model, LinearAngle: Units.NormalizeAngle((int)Math.Round(angle * 60000)));
        }

        /// <summary>The outline of graphic properties; null when they say nothing about the line.</summary>
        private Outline? ReadOutline(XElement? properties)
        {
            if (properties is null)
            {
                return null;
            }

            var stroke = (string?)properties.Attribute(DrawNs + "stroke");
            var color = Color((string?)properties.Attribute(SvgNs + "stroke-color"), (string?)properties.Attribute(SvgNs + "stroke-opacity"));
            var width = OdpNames.Emu((string?)properties.Attribute(SvgNs + "stroke-width"));
            Fill? fill = stroke == "none" ? NoFill.Instance : color is null ? null : new SolidFill(color);
            LineDash? dash = stroke == "dash" ? ReadDash(_index.Named("stroke-dash", (string?)properties.Attribute(DrawNs + "stroke-dash")), width) : null;
            LineCap? cap = (string?)properties.Attribute(SvgNs + "stroke-linecap") switch
            {
                "round" => LineCap.Round,
                "square" => LineCap.Square,
                "butt" => LineCap.Flat,
                _ => null,
            };
            LineJoin? join = (string?)properties.Attribute(DrawNs + "stroke-linejoin") switch
            {
                "round" => LineJoin.Round,
                "bevel" => LineJoin.Bevel,
                "miter" => LineJoin.Miter,
                _ => null,
            };
            var head = ReadMarker(properties, "start", width);
            var tail = ReadMarker(properties, "end", width);
            if (stroke is null && fill is null && width is null && cap is null && join is null && head is null && tail is null)
            {
                return null;
            }

            return new Outline(width, fill, dash, cap, join, Head: head, Tail: tail);
        }

        private static LineEnd? ReadMarker(XElement properties, string end, long? lineWidth)
        {
            if (OdpNames.MarkerKind((string?)properties.Attribute(DrawNs + "marker-" + end)) is not { } kind)
            {
                return null;
            }

            var width = OdpNames.Emu((string?)properties.Attribute(DrawNs + $"marker-{end}-width"));
            var size = width is { } markerWidth ? OdpNames.MarkerSizeOf(markerWidth / (double)(lineWidth ?? 9525)) : LineEndSize.Medium;
            return new LineEnd(kind, size);
        }

        /// <summary>A draw:stroke-dash as a preset when its pattern is one, else as custom stops.</summary>
        private static LineDash ReadDash(XElement? dash, long? lineWidth)
        {
            if (dash is null)
            {
                return new LineDash("dash");
            }

            var width = Math.Max(1, lineWidth ?? 9525) / (double)Units.EmuPerPoint;
            double Length(string name, double fallback)
            {
                var text = (string?)dash.Attribute(DrawNs + name);
                return PercentOf(text) is { } percent ? percent : Points(text) is { } points ? points / width : fallback;
            }

            var pattern = new List<double>();
            var distance = Length("distance", 3);
            var dots1 = (int?)NumberOf((string?)dash.Attribute(DrawNs + "dots1")) ?? 0;
            var dots2 = (int?)NumberOf((string?)dash.Attribute(DrawNs + "dots2")) ?? 0;
            for (var index = 0; index < dots1; index++)
            {
                pattern.AddRange([Length("dots1-length", 1), distance]);
            }

            for (var index = 0; index < dots2; index++)
            {
                pattern.AddRange([Length("dots2-length", 1), distance]);
            }

            if (pattern.Count == 0)
            {
                return new LineDash("dash");
            }

            foreach (var preset in OdpNames.DashPresets)
            {
                var known = new LineDash(preset).Pattern(LineCap.Flat);
                if (known.Length == pattern.Count && known.Zip(pattern).All(pair => Math.Abs(pair.First - pair.Second) < 0.01))
                {
                    return new LineDash(preset);
                }
            }

            var stops = new List<(int, int)>();
            for (var index = 0; index + 1 < pattern.Count; index += 2)
            {
                stops.Add(((int)Math.Round(pattern[index] * 100000), (int)Math.Round(pattern[index + 1] * 100000)));
            }

            return new LineDash(null, stops);
        }

        private static ShapeEffects? ReadShadow(XElement? properties)
        {
            if ((string?)properties?.Attribute(DrawNs + "shadow") != "visible")
            {
                return null;
            }

            var x = OdpNames.Emu((string?)properties!.Attribute(DrawNs + "shadow-offset-x")) ?? 0;
            var y = OdpNames.Emu((string?)properties.Attribute(DrawNs + "shadow-offset-y")) ?? 0;
            var distance = (long)Math.Round(Math.Sqrt((x * (double)x) + (y * (double)y)));
            var direction = Units.NormalizeAngle((int)Math.Round(Math.Atan2(y, x) * 180 / Math.PI * 60000));
            var color = Color((string?)properties.Attribute(DrawNs + "shadow-color")) ?? ColorSpec.Rgb("808080");
            if (PercentOf((string?)properties.Attribute(DrawNs + "shadow-opacity")) is { } opacity)
            {
                color = color.WithOpacity(opacity);
            }

            var blur = OdpNames.Emu((string?)properties.Attribute(LoExtNs + "shadow-blur")) ?? 0;
            return new ShapeEffects(new OuterShadow(blur, distance, direction, color));
        }

        private static BodyProperties ReadBody(XElement? properties)
        {
            if (properties is null)
            {
                return BodyProperties.Empty;
            }

            long? Inset(string side) => OdpNames.Emu((string?)properties.Attribute(FoNs + "padding-" + side)) ?? OdpNames.Emu((string?)properties.Attribute(FoNs + "padding"));
            TextAnchor? anchor = (string?)properties.Attribute(DrawNs + "textarea-vertical-align") switch
            {
                "top" => TextAnchor.Top,
                "middle" => TextAnchor.Center,
                "bottom" => TextAnchor.Bottom,
                "justify" => TextAnchor.Justified,
                _ => null,
            };
            bool? center = (string?)properties.Attribute(DrawNs + "textarea-horizontal-align") switch
            {
                "center" => true,
                "justify" => false,
                _ => null,
            };
            bool? wrap = (string?)properties.Attribute(FoNs + "wrap-option") switch
            {
                "wrap" => true,
                "no-wrap" => false,
                _ => null,
            };
            var vertical = (string?)properties.Attribute(StyleNs + "writing-mode") switch
            {
                "tb-rl" or "tb" => "vert",
                "bt-lr" => "vert270",
                "lr-tb" or "lr" => "horz",
                _ => null,
            };
            AutoFit? autoFit = null;
            if ((string?)properties.Attribute(StyleNs + "shrink-to-fit") == "true" || (string?)properties.Attribute(DrawNs + "fit-to-size") == "shrink-to-fit")
            {
                autoFit = new NormalAutoFit();
            }
            else if ((string?)properties.Attribute(DrawNs + "auto-grow-height") is { } grow)
            {
                autoFit = grow == "true" ? ShapeAutoFit.Instance : NoAutoFit.Instance;
            }

            return new BodyProperties(Inset("left"), Inset("top"), Inset("right"), Inset("bottom"), anchor, center, wrap, vertical, AutoFit: autoFit);
        }

        // ------------------------------------------------------------ text properties

        /// <summary>Paragraph properties of style:paragraph-properties (what <see cref="OdpWriter"/> writes, and LibreOffice's equivalents).</summary>
        private static ParagraphProperties ReadParagraphProperties(XElement? properties)
        {
            if (properties is null)
            {
                return ParagraphProperties.Empty;
            }

            TextAlignment? alignment = (string?)properties.Attribute(FoNs + "text-align") switch
            {
                "start" or "left" => TextAlignment.Left,
                "center" => TextAlignment.Center,
                "end" or "right" => TextAlignment.Right,
                "justify" => TextAlignment.Justify,
                _ => null,
            };
            TextSpacing? Spacing(XName name) => Points((string?)properties.Attribute(name)) is { } points ? TextSpacing.Points(points) : null;
            TextSpacing? lineSpacing = null;
            if ((string?)properties.Attribute(FoNs + "line-height") is { } height)
            {
                lineSpacing = PercentOf(height) is { } percent ? TextSpacing.Percent((int)Math.Round(percent * 100000)) : Points(height) is { } points ? TextSpacing.Points(points) : null;
            }

            return new ParagraphProperties(
                Alignment: alignment,
                LeftMargin: OdpNames.Emu((string?)properties.Attribute(FoNs + "margin-left")),
                RightMargin: OdpNames.Emu((string?)properties.Attribute(FoNs + "margin-right")),
                Indent: OdpNames.Emu((string?)properties.Attribute(FoNs + "text-indent")),
                SpaceBefore: Spacing(FoNs + "margin-top"),
                SpaceAfter: Spacing(FoNs + "margin-bottom"),
                LineSpacing: lineSpacing);
        }

        /// <summary>Run properties of style:text-properties (null when there are none).</summary>
        private RunProperties? ReadRunProperties(XElement? properties)
        {
            if (properties is null || !properties.HasAttributes)
            {
                return null;
            }

            var font = OdfFont.Read(properties);
            var family = (string?)properties.Attribute(StyleNs + "font-name") is { } fontName ? _index.FontFamily(fontName) : font.Family;
            string? caps = (string?)properties.Attribute(FoNs + "font-variant") == "small-caps" ? "small"
                : (string?)properties.Attribute(FoNs + "text-transform") switch
                {
                    "uppercase" => "all",
                    "none" => "none",
                    _ => null,
                };
            var language = (string?)properties.Attribute(FoNs + "language");
            var country = (string?)properties.Attribute(FoNs + "country");
            string? culture = language is null or "zxx" or "none" ? null : country is null or "none" ? language : $"{language}-{country}";
            TextFont? Typeface(XName name, XName family) =>
                (string?)properties.Attribute(name) is { } face ? new TextFont(_index.FontFamily(face))
                : (string?)properties.Attribute(family) is { } plain ? new TextFont(plain.Trim().Trim('\'', '"'))
                : null;
            var run = new RunProperties(
                Size: font.Size is { } size ? (int)Math.Round(size * 100) : null,
                Bold: font.Bold,
                Italic: font.Italic,
                Underline: OdpNames.UnderlineOf(font.Underline, font.UnderlineDouble),
                Strike: font.Strikethrough is { } strike ? strike ? "sngStrike" : "noStrike" : null,
                Baseline: font.Position is { } position ? (int)Math.Round(position * 1000) : null,
                Caps: caps,
                Spacing: Points((string?)properties.Attribute(FoNs + "letter-spacing")) is { } spacing ? (int)Math.Round(spacing * 100) : null,
                Language: culture,
                Fill: font.Color is { } color ? new SolidFill(ColorSpec.Rgb(color)) : null,
                Highlight: Color((string?)properties.Attribute(FoNs + "background-color")),
                Latin: family is null ? null : new TextFont(family),
                EastAsian: Typeface(StyleNs + "font-name-asian", StyleNs + "font-family-asian"),
                ComplexScript: Typeface(StyleNs + "font-name-complex", StyleNs + "font-family-complex"));
            return run == RunProperties.Empty ? null : run;
        }

        /// <summary>The bullet of a list style's level (1-based), its font, colour and size; null when the level has none.</summary>
        private (Bullet? Bullet, TextFont? Font, ColorSpec? Color, TextSpacing? Size) ReadBullet(XElement? listStyle, int level)
        {
            var element = listStyle?.Elements().FirstOrDefault(item => (int?)item.Attribute(TextNs + "level") == level);
            if (element is null)
            {
                return (null, null, null, null);
            }

            var text = element.Element(StyleNs + "text-properties");
            var font = (string?)text?.Attribute(FoNs + "font-family") is { } family ? new TextFont(family.Trim().Trim('\'', '"'))
                : (string?)text?.Attribute(StyleNs + "font-name") is { } face ? new TextFont(_index.FontFamily(face)) : null;
            var color = Color((string?)text?.Attribute(FoNs + "color"));
            var size = PercentOf((string?)text?.Attribute(FoNs + "font-size")) is { } percent ? TextSpacing.Percent((int)Math.Round(percent * 100000)) : (TextSpacing?)null;
            Bullet? bullet = element.Name.LocalName switch
            {
                "list-level-style-bullet" => (string?)element.Attribute(TextNs + "bullet-char") is { Length: > 0 } character ? new CharacterBullet(character) : null,
                "list-level-style-number" when (string?)element.Attribute(StyleNs + "num-format") is { Length: > 0 } format => new AutoNumberBullet(
                    OdpNames.SchemeOf(format, (string?)element.Attribute(StyleNs + "num-prefix"), (string?)element.Attribute(StyleNs + "num-suffix")),
                    (int?)NumberOf((string?)element.Attribute(TextNs + "start-value"))),
                "list-level-style-image" => new CharacterBullet("•"),
                _ => null,
            };
            return (bullet, font, color, size);
        }

        private static string Invariant(double value) => value.ToString(CultureInfo.InvariantCulture);
    }
}
