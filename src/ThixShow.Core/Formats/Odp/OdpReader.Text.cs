using System.Text;
using System.Xml.Linq;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

public static partial class OdpReader
{
    private sealed partial class Reader
    {
        // ------------------------------------------------------------ text

        /// <summary>
        /// The paragraphs of text:p / text:h / text:list children. <paramref name="inherited"/> gives the bullet a level has without a
        /// bullet of its own (the master's outline levels for placeholders, none for shapes): a bullet equal to it is left to inheritance,
        /// a paragraph outside a list where one is inherited gets "no bullet". <paramref name="defaultList"/> is the list style of a list
        /// that names none (an outline placeholder's).
        /// </summary>
        private List<TextParagraph> ReadParagraphs(IEnumerable<XElement> children, ReadScope scope, Func<int, Bullet?> inherited, XElement? defaultList)
        {
            var paragraphs = new List<TextParagraph>();
            foreach (var child in children)
            {
                Walk(child, -1, defaultList, header: false);
            }

            return paragraphs;

            void Walk(XElement element, int depth, XElement? listStyle, bool header)
            {
                if (element.Name == TextNs + "p" || element.Name == TextNs + "h")
                {
                    paragraphs.Add(ReadParagraph(element, Math.Clamp(depth, 0, 8), depth >= 0 && !header, listStyle, scope, inherited));
                    return;
                }

                if (element.Name == TextNs + "list")
                {
                    var style = _index.ListStyle((string?)element.Attribute(TextNs + "style-name"), scope.Part) ?? listStyle;
                    foreach (var item in element.Elements())
                    {
                        var isHeader = item.Name == TextNs + "list-header";
                        if (!isHeader && item.Name != TextNs + "list-item")
                        {
                            continue;
                        }

                        foreach (var content in item.Elements())
                        {
                            Walk(content, depth + 1, style, isHeader);
                        }
                    }

                    return;
                }

                if (element.Name.Namespace == TextNs && element.Name.LocalName is "section" or "soft-page-break")
                {
                    foreach (var content in element.Elements())
                    {
                        Walk(content, depth, listStyle, header);
                    }
                }
            }
        }

        private TextParagraph ReadParagraph(XElement element, int level, bool inList, XElement? listStyle, ReadScope scope, Func<int, Bullet?> inherited)
        {
            var styleName = (string?)element.Attribute(TextNs + "style-name");
            var props = _index.Resolve("paragraph", styleName, scope.Part);
            var properties = ReadParagraphProperties(props.Of("paragraph-properties"));
            var inheritedBullet = inherited(level);
            if (inList)
            {
                var (bullet, font, color, size) = ReadBullet(listStyle, level + 1);
                bullet ??= inheritedBullet ?? new CharacterBullet("•");
                properties = Equals(bullet, inheritedBullet)
                    ? properties
                    : properties with { Bullet = bullet, BulletFont = font, BulletColor = color, BulletSize = size };
            }
            else if (inheritedBullet is not null)
            {
                properties = properties with { Bullet = NoBullet.Instance };
            }

            if (level > 0)
            {
                properties = properties with { Level = level };
            }

            var runs = new RunReader(this, scope).Read(element);
            RunProperties? end;
            if (runs.Count == 0)
            {
                end = ReadRunProperties(props.Of("text-properties"));
            }
            else
            {
                var last = runs[^1].Properties with { Click = null, Hover = null };
                end = last == RunProperties.Empty ? null : last;
            }

            return new TextParagraph(properties, runs, end);
        }

        /// <summary>The runs of a paragraph: spans by their styles, links, spaces and tabs, line breaks, slide number and date fields.</summary>
        private sealed class RunReader(Reader reader, ReadScope scope)
        {
            private readonly List<TextRun> _runs = [];
            private readonly StringBuilder _text = new();
            private RunProperties _properties = RunProperties.Empty;
            private bool _collapsedSpace = true;

            public List<TextRun> Read(XElement paragraph)
            {
                Walk(paragraph, RunProperties.Empty, null);
                Flush();
                if (_runs.Count > 0 && _runs[^1] is TextSpan { Content.Length: > 0 } last && _collapsedSpace && last.Content.EndsWith(' '))
                {
                    var trimmed = last.Content[..^1];
                    _runs[^1] = last with { Content = trimmed };
                    if (trimmed.Length == 0)
                    {
                        _runs.RemoveAt(_runs.Count - 1);
                    }
                }

                return _runs;
            }

            private void Walk(XElement element, RunProperties current, Hyperlink? link)
            {
                foreach (var node in element.Nodes())
                {
                    switch (node)
                    {
                        case XText text:
                            AppendCollapsed(text.Value, current, link);
                            break;
                        case XElement child when child.Name == TextNs + "span":
                            var style = reader.ReadRunProperties(reader._index.Resolve("text", (string?)child.Attribute(TextNs + "style-name"), scope.Part).Of("text-properties"));
                            Walk(child, current.Merge(style) with { Click = null, Hover = null }, link);
                            break;
                        case XElement child when child.Name == TextNs + "a":
                            Walk(child, current, reader.LinkOf((string?)child.Attribute(XLinkNs + "href")) ?? link);
                            break;
                        case XElement child when child.Name == TextNs + "s":
                            Append(new string(' ', Math.Clamp((int?)child.Attribute(TextNs + "c") ?? 1, 1, 1000)), current, link);
                            _collapsedSpace = false;
                            break;
                        case XElement child when child.Name == TextNs + "tab":
                            Append("\t", current, link);
                            _collapsedSpace = false;
                            break;
                        case XElement child when child.Name == TextNs + "line-break":
                            Flush();
                            _runs.Add(new LineBreak(With(current, link)));
                            _collapsedSpace = true;
                            break;
                        case XElement child when child.Name == TextNs + "page-number":
                            Flush();
                            _runs.Add(new TextField(OdpNames.SlideNumberFieldId, "slidenum", PlainText(child), With(current, link)));
                            _collapsedSpace = false;
                            break;
                        case XElement child when child.Name == TextNs + "date" || child.Name == TextNs + "time" || child.Name == PresentationNs + "date-time":
                            Flush();
                            _runs.Add(new TextField(OdpNames.DateFieldId, "datetime1", PlainText(child), With(current, link)));
                            _collapsedSpace = false;
                            break;
                        case XElement child when child.Name == TextNs + "note" || child.Name == OfficeNs + "annotation":
                            reader.Lose("comments");
                            break;
                        case XElement child when child.Name == PresentationNs + "footer" || child.Name == OfficeNs + "annotation-end" || child.Name.LocalName.StartsWith("bookmark", StringComparison.Ordinal):
                            break;
                        case XElement child:
                            Walk(child, current, link);
                            break;
                    }
                }
            }

            private static RunProperties With(RunProperties properties, Hyperlink? link) => link is null ? properties : properties with { Click = link };

            /// <summary>Text as ODF means it: runs of white space are one space, white space at the start of a paragraph none.</summary>
            private void AppendCollapsed(string text, RunProperties current, Hyperlink? link)
            {
                var builder = new StringBuilder();
                foreach (var character in text)
                {
                    if (character is ' ' or '\t' or '\n' or '\r')
                    {
                        if (!_collapsedSpace)
                        {
                            builder.Append(' ');
                            _collapsedSpace = true;
                        }

                        continue;
                    }

                    builder.Append(character);
                    _collapsedSpace = false;
                }

                if (builder.Length > 0)
                {
                    Append(builder.ToString(), current, link);
                }
            }

            private void Append(string text, RunProperties current, Hyperlink? link)
            {
                var properties = With(current, link);
                if (_text.Length > 0 && properties != _properties)
                {
                    Flush();
                }

                if (_text.Length == 0 && _runs.Count > 0 && _runs[^1] is TextSpan previous && previous.RunProperties == properties)
                {
                    _runs.RemoveAt(_runs.Count - 1);
                    _text.Append(previous.Content);
                }

                _properties = properties;
                _text.Append(text);
            }

            private void Flush()
            {
                if (_text.Length > 0)
                {
                    _runs.Add(new TextSpan(_text.ToString(), _properties));
                    _text.Clear();
                }
            }
        }

        /// <summary>A link of xlink:href: an address, or "#name" of a slide.</summary>
        private Hyperlink? LinkOf(string? href)
        {
            if (string.IsNullOrEmpty(href))
            {
                return null;
            }

            if (href.StartsWith('#'))
            {
                return _slideIds.TryGetValue(href[1..], out var id) ? Hyperlink.ToSlide(id) : null;
            }

            return Hyperlink.Url(href);
        }

        // ------------------------------------------------------------ transitions and effects

        private SlideTransition? ReadTransition(XElement? properties)
        {
            if (properties is null)
            {
                return null;
            }

            var type = (string?)properties.Attribute(SmilNs + "type");
            var automatic = (string?)properties.Attribute(PresentationNs + "transition-type") == "automatic";
            int? after = automatic && OdpNames.Milliseconds((string?)properties.Attribute(PresentationNs + "duration")) is { } milliseconds ? milliseconds : null;
            if (type is null && after is null)
            {
                return null;
            }

            var (kind, direction, throughBlack) = OdpNames.TransitionOf(type, (string?)properties.Attribute(SmilNs + "subtype"), (string?)properties.Attribute(SmilNs + "direction") == "reverse");
            if (kind is null)
            {
                Lose("transition");
                kind = TransitionKind.Fade;
            }

            var duration = OdpNames.Milliseconds((string?)properties.Attribute(SmilNs + "dur"));
            var speedWord = (string?)properties.Attribute(PresentationNs + "transition-speed");
            // "medium" is ThixShow's default speed; a speed word that only states the exact duration is no speed of its own.
            var speed = speedWord switch
            {
                "fast" => "fast",
                "slow" => "slow",
                _ => null,
            };
            if (duration is { } exact && speedWord == OdpNames.SpeedOf(exact))
            {
                speed = null;
            }

            return new SlideTransition(kind.Value, direction, duration, speed, AdvanceAfterMs: after, ThroughBlack: throughBlack);
        }

        /// <summary>The main sequence's appear and fade effects of a page (others are counted as not shown).</summary>
        private SlideTiming? ReadTiming(XElement page, ReadScope scope)
        {
            var root = page.Elements(AnimNs + "par").FirstOrDefault(element => (string?)element.Attribute(PresentationNs + "node-type") == "timing-root");
            if (root is null)
            {
                return null;
            }

            Lose("interactive", root.Descendants(AnimNs + "seq").Count(sequence => (string?)sequence.Attribute(PresentationNs + "node-type") == "interactive-sequence"));
            var main = root.Descendants(AnimNs + "seq").FirstOrDefault(sequence => (string?)sequence.Attribute(PresentationNs + "node-type") == "main-sequence");
            if (main is null)
            {
                return null;
            }

            var effects = new List<AnimationEffect>();
            foreach (var node in main.Descendants().Where(element => element.Attribute(PresentationNs + "preset-class") is not null))
            {
                var target = node.DescendantsAndSelf().Select(element => (string?)element.Attribute(SmilNs + "targetElement")).FirstOrDefault(value => value is not null);
                var trigger = (string?)node.Attribute(PresentationNs + "node-type") switch
                {
                    "with-previous" => AnimationTrigger.WithPrevious,
                    "after-previous" => AnimationTrigger.AfterPrevious,
                    _ => AnimationTrigger.OnClick,
                };
                AnimationPreset? preset = ((string?)node.Attribute(PresentationNs + "preset-class"), (string?)node.Attribute(PresentationNs + "preset-id")) switch
                {
                    ("entrance", "ooo-entrance-appear") => AnimationPreset.Appear,
                    ("entrance", "ooo-entrance-fade-in") => AnimationPreset.Fade,
                    ("exit", "ooo-exit-disappear") => AnimationPreset.Disappear,
                    ("exit", "ooo-exit-fade-out") => AnimationPreset.FadeOut,
                    _ => null,
                };
                if (preset is null || target is null || !scope.IdsByName.TryGetValue(target, out var shapeId) || node.Descendants().Any(element => element.Attribute(AnimNs + "sub-item") is not null))
                {
                    Lose("animation");
                    continue;
                }

                var duration = OdpNames.Milliseconds((string?)node.Descendants(AnimNs + "transitionFilter").FirstOrDefault()?.Attribute(SmilNs + "dur"));
                var delay = OdpNames.Milliseconds((string?)node.Attribute(SmilNs + "begin")) ?? 0;
                effects.Add(AnimationPresets.Create(preset.Value, shapeId, trigger, preset is AnimationPreset.Appear or AnimationPreset.Disappear ? null : duration, delayMs: Math.Max(0, delay)));
            }

            return effects.Count == 0 ? null : new SlideTiming(effects);
        }
    }
}
