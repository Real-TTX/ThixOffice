using System.Globalization;
using System.Xml.Linq;
using ThixShow.Core.Formatting;
using ThixShow.Core.Model;
using static ThixOffice.Core.Formats.Odf.OdfXml;

namespace ThixShow.Core.Formats.Odp;

public static partial class OdpWriter
{
    private sealed partial class Writer
    {
        // ------------------------------------------------------------ text

        private static Bullet? Visible(Bullet? bullet) => bullet is CharacterBullet or AutoNumberBullet or PictureBullet ? bullet : null;

        private static int LevelOf(TextParagraph paragraph) => Math.Clamp(paragraph.Properties.Level ?? 0, 0, 8);

        /// <summary>
        /// The paragraphs of a text body: plain ones as text:p, bulleted or indented ones as text:list (nested by level, one list style
        /// per run of paragraphs whose levels have one bullet each; text:list-header for an indented paragraph without bullet). In an
        /// outline placeholder a list whose bullets all come from the master names no style (the outline styles give them).
        /// </summary>
        private List<XElement> WriteText(ShapeScope scope, TextBody body, ResolvedShape resolved, bool outline)
        {
            var result = new List<XElement>();
            var paragraphs = body.Paragraphs;
            var index = 0;
            while (index < paragraphs.Count)
            {
                var effective = ShapeResolver.ParagraphOf(resolved, paragraphs[index]);
                if (Visible(effective.Bullet) is null && LevelOf(paragraphs[index]) == 0)
                {
                    result.Add(WriteParagraph(scope, paragraphs[index]));
                    index++;
                    continue;
                }

                var levels = new Dictionary<int, ParagraphProperties>();
                var start = index;
                var inherited = true;
                while (index < paragraphs.Count)
                {
                    var paragraph = paragraphs[index];
                    var properties = ShapeResolver.ParagraphOf(resolved, paragraph);
                    var bullet = Visible(properties.Bullet);
                    var level = LevelOf(paragraph);
                    if (bullet is null && level == 0)
                    {
                        break;
                    }

                    if (levels.TryGetValue(level, out var known) && bullet is not null && Visible(known.Bullet) is { } knownBullet && !Equals(knownBullet, bullet))
                    {
                        break;
                    }

                    if (!levels.TryGetValue(level, out known) || (Visible(known.Bullet) is null && bullet is not null))
                    {
                        levels[level] = properties;
                    }

                    inherited &= paragraph.Properties.Bullet is null;
                    index++;
                }

                var listLevels = levels.OrderBy(pair => pair.Key).Select(pair => ListLevel(pair.Key + 1, pair.Value, scope.Colors)).OfType<XElement>().ToList();
                var styleName = (outline && inherited) || listLevels.Count == 0 ? null : scope.Sink.AddList(listLevels);
                result.Add(WriteList(scope, resolved, paragraphs.Skip(start).Take(index - start).ToList(), styleName));
            }

            return result;
        }

        private XElement WriteList(ShapeScope scope, ResolvedShape resolved, List<TextParagraph> paragraphs, string? styleName)
        {
            var root = new XElement(TextNs + "list");
            if (styleName is not null)
            {
                root.Add(new XAttribute(TextNs + "style-name", styleName));
            }

            var lists = new List<XElement> { root };
            var items = new List<XElement?> { null };
            foreach (var paragraph in paragraphs)
            {
                var level = LevelOf(paragraph);
                var header = Visible(ShapeResolver.ParagraphOf(resolved, paragraph).Bullet) is null;
                while (lists.Count > level + 1)
                {
                    lists.RemoveAt(lists.Count - 1);
                    items.RemoveAt(items.Count - 1);
                }

                while (lists.Count < level + 1)
                {
                    var depth = lists.Count - 1;
                    var host = items[depth];
                    if (host is null || host.Name == TextNs + "list-header")
                    {
                        host = new XElement(TextNs + "list-item");
                        lists[depth].Add(host);
                        items[depth] = host;
                    }

                    var nested = new XElement(TextNs + "list");
                    host.Add(nested);
                    lists.Add(nested);
                    items.Add(null);
                }

                var item = new XElement(TextNs + (header ? "list-header" : "list-item"), WriteParagraph(scope, paragraph));
                lists[level].Add(item);
                items[level] = item;
            }

            return root;
        }

        private XElement WriteParagraph(ShapeScope scope, TextParagraph paragraph)
        {
            var element = new XElement(TextNs + "p");
            var endProperties = paragraph.Runs.IsEmpty ? TextProperties(Plain(paragraph.EndProperties), scope.Colors) : null;
            if (scope.Sink.Add("paragraph", "P", [ParagraphProperties(Reduced(paragraph.Properties)), endProperties]) is { } style)
            {
                element.Add(new XAttribute(TextNs + "style-name", style));
            }

            foreach (var run in paragraph.Runs)
            {
                switch (run)
                {
                    case TextSpan span:
                        element.Add(Styled(scope, TextContent(span.Content), span.RunProperties));
                        break;
                    case LineBreak:
                        element.Add(new XElement(TextNs + "line-break"));
                        break;
                    case TextField { Type: "slidenum" } field:
                        element.Add(Styled(scope, [new XElement(TextNs + "page-number", field.Content)], field.RunProperties));
                        break;
                    case TextField { Type: { } type } field when type.StartsWith("datetime", StringComparison.Ordinal):
                        element.Add(Styled(scope, [new XElement(TextNs + "date", field.Content)], field.RunProperties));
                        break;
                    case TextField field:
                        element.Add(Styled(scope, TextContent(field.Content), field.RunProperties));
                        break;
                }
            }

            return element;
        }

        /// <summary>Run properties without what a style cannot carry (links, file bookkeeping).</summary>
        private static RunProperties? Plain(RunProperties? run) =>
            run is null ? null : run with { Click = null, Hover = null, Dirty = null, SpellingError = null, Extra = null };

        private IEnumerable<object> Styled(ShapeScope scope, IEnumerable<object> content, RunProperties properties)
        {
            object[] nodes = [.. content];
            if (scope.Sink.Add("text", "T", [TextProperties(Plain(properties), scope.Colors)]) is { } style)
            {
                nodes = [new XElement(TextNs + "span", new XAttribute(TextNs + "style-name", style), nodes)];
            }

            if (properties.Click is { } link && LinkTarget(link) is { } target)
            {
                return [new XElement(TextNs + "a", new XAttribute(XLinkNs + "type", "simple"), new XAttribute(XLinkNs + "href", target), nodes)];
            }

            return nodes;
        }

        private string? LinkTarget(Hyperlink link) => link.Kind switch
        {
            HyperlinkKind.Url => link.Target,
            HyperlinkKind.Slide when link.SlideId is { } id && presentation.IndexOf(id) is >= 0 and var index =>
                "#" + (presentation.Slides[index].Name ?? $"page{index + 1}"),
            _ => null,
        };

        // ------------------------------------------------------------ transitions and animations

        private static void AddTransition(XElement properties, XElement timingRoot, SlideTransition? transition)
        {
            if (transition is null)
            {
                return;
            }

            if (transition.Kind is not (TransitionKind.None or TransitionKind.Cut))
            {
                var (type, subtype, reverse) = OdpNames.Transition(transition) ?? ("fade", "crossfade", false);
                properties.Add(new XAttribute(SmilNs + "type", type), new XAttribute(SmilNs + "subtype", subtype));
                if (reverse)
                {
                    properties.Add(new XAttribute(SmilNs + "direction", "reverse"));
                }

                if (subtype == "fadeOverColor")
                {
                    properties.Add(new XAttribute(SmilNs + "fadeColor", "#000000"));
                }

                var speed = transition.Speed switch
                {
                    "fast" => "fast",
                    "slow" => "slow",
                    "med" => "medium",
                    _ => OdpNames.SpeedOf(transition.EffectiveDurationMs),
                };
                properties.Add(new XAttribute(PresentationNs + "transition-speed", speed));
                if (transition.DurationMs is { } duration)
                {
                    properties.Add(new XAttribute(SmilNs + "dur", OdpNames.Seconds(duration)));
                }
            }

            if (transition.AdvanceAfterMs is { } after)
            {
                properties.Add(new XAttribute(PresentationNs + "transition-type", "automatic"),
                    new XAttribute(PresentationNs + "duration", Duration(after / 86400000.0)));
            }
        }

        /// <summary>The effects ODF takes: appear, fade and fly, in and out, on whole shapes (LibreOffice's ooo-… presets).</summary>
        public static bool IsWritable(AnimationEffect effect) =>
            effect.ParagraphStart is null && AnimationPresets.PresetOf(effect) is AnimationPreset.Appear or AnimationPreset.Fade or AnimationPreset.FlyIn or AnimationPreset.Disappear or AnimationPreset.FadeOut or AnimationPreset.FlyOut;

        private static void AddAnimations(XElement timingRoot, Slide slide, ShapeScope scope)
        {
            var effects = slide.Timing?.MainSequence.Where(effect => IsWritable(effect) && slide.FindShape(effect.ShapeId) is { Placeholder: not { IsFooterKind: true } }).ToList() ?? [];
            if (effects.Count == 0)
            {
                return;
            }

            var sequence = new XElement(AnimNs + "seq", new XAttribute(PresentationNs + "node-type", "main-sequence"));
            XElement? click = null;
            XElement? group = null;
            var groupStart = 0;
            var groupEnd = 0;
            foreach (var effect in effects)
            {
                if (effect.Trigger == AnimationTrigger.OnClick || click is null)
                {
                    click = new XElement(AnimNs + "par", new XAttribute(SmilNs + "begin", effect.Trigger == AnimationTrigger.OnClick ? "next" : "0s"));
                    sequence.Add(click);
                    group = null;
                    groupEnd = 0;
                }

                if (group is null || effect.Trigger == AnimationTrigger.AfterPrevious)
                {
                    groupStart = group is null ? 0 : groupEnd;
                    group = new XElement(AnimNs + "par", new XAttribute(SmilNs + "begin", OdpNames.Seconds(groupStart)), new XAttribute(SmilNs + "fill", "hold"));
                    click.Add(group);
                }

                group.Add(WriteEffect(effect, scope.IdPrefix + effect.ShapeId.ToString(CultureInfo.InvariantCulture)));
                groupEnd = Math.Max(groupEnd, groupStart + effect.DelayMs + effect.DurationMs);
            }

            timingRoot.Add(sequence);
        }

        private static XElement WriteEffect(AnimationEffect effect, string target)
        {
            var preset = AnimationPresets.PresetOf(effect)!.Value;
            var isFly = preset is AnimationPreset.FlyIn or AnimationPreset.FlyOut;
            var duration = (isFly
                ? effect.Behaviors.OfType<PropertyBehavior>().FirstOrDefault()?.DurationMs
                : effect.Behaviors.OfType<FilterBehavior>().FirstOrDefault()?.DurationMs) ?? AnimationPresets.DefaultDuration(preset);
            var entrance = preset is AnimationPreset.Appear or AnimationPreset.Fade or AnimationPreset.FlyIn;
            var direction = AnimationPresets.DirectionOf(effect.PresetSubtype);
            var element = new XElement(AnimNs + "par", new XAttribute(SmilNs + "begin", OdpNames.Seconds(effect.DelayMs)), new XAttribute(SmilNs + "fill", "hold"),
                new XAttribute(PresentationNs + "node-type", effect.Trigger switch
                {
                    AnimationTrigger.WithPrevious => "with-previous",
                    AnimationTrigger.AfterPrevious => "after-previous",
                    _ => "on-click",
                }),
                new XAttribute(PresentationNs + "preset-class", entrance ? "entrance" : "exit"),
                new XAttribute(PresentationNs + "preset-id", preset switch
                {
                    AnimationPreset.Appear => "ooo-entrance-appear",
                    AnimationPreset.Fade => "ooo-entrance-fade-in",
                    AnimationPreset.FlyIn => "ooo-entrance-fly-in",
                    AnimationPreset.Disappear => "ooo-exit-disappear",
                    AnimationPreset.FlyOut => "ooo-exit-fly-out",
                    _ => "ooo-exit-fade-out",
                }));
            if (isFly)
            {
                element.Add(new XAttribute(PresentationNs + "preset-sub-type", OdpNames.FlySubtype(direction)));
            }

            XElement Visibility(int begin, bool visible) => new(AnimNs + "set", new XAttribute(SmilNs + "begin", OdpNames.Seconds(begin)), new XAttribute(SmilNs + "dur", "0.001s"),
                new XAttribute(SmilNs + "fill", "hold"), new XAttribute(SmilNs + "targetElement", target), new XAttribute(SmilNs + "attributeName", "visibility"),
                new XAttribute(SmilNs + "to", visible ? "visible" : "hidden"));
            XElement Fade(bool into) => new(AnimNs + "transitionFilter", new XAttribute(SmilNs + "dur", OdpNames.Seconds(duration)), new XAttribute(SmilNs + "targetElement", target),
                new XAttribute(SmilNs + "type", "fade"), new XAttribute(SmilNs + "subtype", "crossfade"), into ? null : new XAttribute(SmilNs + "mode", "out"));
            // Fly: x and y move between the shape's place and just outside the slide (ODF's formulas: x, y, width, height in slide units).
            XElement[] Fly(bool into)
            {
                var (outsideX, outsideY) = direction switch
                {
                    AnimationDirection.Left => ("0-width/2", "y"),
                    AnimationDirection.Right => ("1+width/2", "y"),
                    AnimationDirection.Top => ("x", "0-height/2"),
                    _ => ("x", "1+height/2"),
                };
                XElement Move(string attribute, string outside) => new(AnimNs + "animate", new XAttribute(SmilNs + "dur", OdpNames.Seconds(duration)),
                    new XAttribute(SmilNs + "fill", "hold"), new XAttribute(SmilNs + "targetElement", target), new XAttribute(SmilNs + "attributeName", attribute),
                    new XAttribute(SmilNs + "values", into ? outside + ";" + attribute : attribute + ";" + outside), new XAttribute(SmilNs + "keyTimes", "0;1"),
                    new XAttribute(PresentationNs + "additive", "base"));
                return [Move("x", outsideX), Move("y", outsideY)];
            }

            switch (preset)
            {
                case AnimationPreset.FlyIn:
                    element.Add(Visibility(0, true), Fly(true));
                    break;
                case AnimationPreset.FlyOut:
                    element.Add(Fly(false), Visibility(Math.Max(0, duration - 1), false));
                    break;
                case AnimationPreset.Appear:
                    element.Add(Visibility(0, true));
                    break;
                case AnimationPreset.Fade:
                    element.Add(Visibility(0, true), Fade(true));
                    break;
                case AnimationPreset.Disappear:
                    element.Add(Visibility(0, false));
                    break;
                default:
                    element.Add(Fade(false), Visibility(Math.Max(0, duration - 1), false));
                    break;
            }

            return element;
        }
    }
}
