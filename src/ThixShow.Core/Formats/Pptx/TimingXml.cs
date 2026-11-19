using System.Globalization;
using System.Xml.Linq;
using ThixShow.Core.Model;
using static ThixShow.Core.Formats.Pptx.Ns;

namespace ThixShow.Core.Formats.Pptx;

/// <summary>
/// Reads and writes slide transitions (p:transition, also inside mc:AlternateContent for p14/p159 effects) and the animation timing
/// tree (p:timing): the main sequence becomes <see cref="AnimationEffect"/>s with their behaviours; interactive sequences and the
/// rest are kept. Unchanged transitions and timings are written as read.
/// </summary>
internal static class TimingXml
{
    public static readonly XNamespace P159 = "http://schemas.microsoft.com/office/powerpoint/2015/09/main";

    // ---------------------------------------------------------------- transitions

    /// <summary>The transition of a slide element (its p:transition or the mc:AlternateContent holding one), or null.</summary>
    public static SlideTransition? ReadTransition(XElement slide)
    {
        var direct = slide.Element(P + "transition");
        var alternate = slide.Elements(Mc + "AlternateContent").FirstOrDefault(element => element.Descendants(P + "transition").Any());
        if (direct is null && alternate is null)
        {
            return null;
        }

        var source = direct ?? alternate!;
        var transition = direct ?? alternate!.Elements(Mc + "Choice").SelectMany(choice => choice.Elements(P + "transition")).FirstOrDefault()
            ?? alternate!.Element(Mc + "Fallback")?.Element(P + "transition");
        if (transition is null)
        {
            return null;
        }

        var effect = transition.Elements().FirstOrDefault(element => element.Name.LocalName is not ("sndAc" or "extLst"));
        var kind = effect?.Name.LocalName switch
        {
            null => TransitionKind.None,
            "fade" => TransitionKind.Fade,
            "push" => TransitionKind.Push,
            "wipe" => TransitionKind.Wipe,
            "split" => TransitionKind.Split,
            "cover" => TransitionKind.Cover,
            "pull" => TransitionKind.Uncover,
            "cut" => TransitionKind.Cut,
            "zoom" => TransitionKind.Zoom,
            "dissolve" => TransitionKind.Dissolve,
            "randomBar" => TransitionKind.RandomBars,
            "morph" => TransitionKind.Morph,
            _ => TransitionKind.Other,
        };
        var direction = kind switch
        {
            TransitionKind.Split when effect.Attr("orient") is not null || effect.Attr("dir") is not null => $"{effect.Attr("orient") ?? "horz"}-{effect.Attr("dir") ?? "out"}",
            TransitionKind.Push or TransitionKind.Wipe or TransitionKind.Cover or TransitionKind.Uncover or TransitionKind.Zoom or TransitionKind.RandomBars => effect.Attr("dir"),
            _ => null,
        };
        return new SlideTransition(
            kind,
            direction,
            transition.Int(P14 + "dur"),
            transition.Attr("spd"),
            transition.Bool("advClick") ?? true,
            transition.Int("advTm"),
            effect.Bool("thruBlk") ?? false,
            XmlFragment.From(source));
    }

    public static XElement WriteTransition(SlideTransition transition)
    {
        if (transition.Source is { } source && ReadTransition(new XElement(P + "sld", source.ToElement())) == transition)
        {
            return source.ToElement();
        }

        XElement Plain(bool withDuration, bool morph, bool fallback = false)
        {
            var element = new XElement(P + "transition");
            element.Set("spd", transition.Speed ?? (fallback ? SpeedOf(transition.EffectiveDurationMs) : null));
            if (withDuration && transition.DurationMs is { } duration)
            {
                element.SetAttributeValue(P14 + "dur", Text(duration));
            }

            element.Set("advClick", transition.AdvanceOnClick ? null : false);
            element.Set("advTm", transition.AdvanceAfterMs);
            if (Effect(transition, morph) is { } effect)
            {
                element.Add(effect);
            }

            return element;
        }

        var needsChoice = transition.DurationMs is not null || transition.Kind == TransitionKind.Morph;
        if (!needsChoice)
        {
            return Plain(false, false);
        }

        var requires = transition.Kind == TransitionKind.Morph ? "p159" : "p14";
        var choice = new XElement(Mc + "Choice", new XAttribute("Requires", requires), Plain(true, true));
        if (transition.Kind == TransitionKind.Morph)
        {
            choice.SetAttributeValue(XNamespace.Xmlns + "p159", P159.NamespaceName);
        }

        choice.SetAttributeValue(XNamespace.Xmlns + "p14", P14.NamespaceName);
        return new XElement(Mc + "AlternateContent", new XAttribute(XNamespace.Xmlns + "mc", Mc.NamespaceName), choice, new XElement(Mc + "Fallback", Plain(false, false, fallback: true)));
    }

    private static string SpeedOf(int milliseconds) => milliseconds <= 500 ? "fast" : milliseconds <= 750 ? "med" : "slow";

    private static XElement? Effect(SlideTransition transition, bool morph)
    {
        var direction = transition.Direction;
        switch (transition.Kind)
        {
            case TransitionKind.Fade:
                {
                    var fade = new XElement(P + "fade");
                    fade.Set("thruBlk", transition.ThroughBlack ? true : null);
                    return fade;
                }

            case TransitionKind.Push:
                return WithDirection(P + "push", direction, "l");
            case TransitionKind.Wipe:
                return WithDirection(P + "wipe", direction, "l");
            case TransitionKind.Cover:
                return WithDirection(P + "cover", direction, "l");
            case TransitionKind.Uncover:
                return WithDirection(P + "pull", direction, "l");
            case TransitionKind.Split:
                {
                    var parts = (direction ?? "horz-out").Split('-');
                    var split = new XElement(P + "split");
                    split.Set("orient", parts[0] == "horz" ? null : parts[0]);
                    split.Set("dir", parts.Length > 1 && parts[1] != "out" ? parts[1] : null);
                    return split;
                }

            case TransitionKind.Cut:
                return new XElement(P + "cut");
            case TransitionKind.Zoom:
                return WithDirection(P + "zoom", direction, "in");
            case TransitionKind.Dissolve:
                return new XElement(P + "dissolve");
            case TransitionKind.RandomBars:
                return WithDirection(P + "randomBar", direction, "horz");
            case TransitionKind.Morph:
                return morph ? new XElement(P159 + "morph", new XAttribute("option", "byObject")) : new XElement(P + "fade");
            default:
                return null;
        }
    }

    private static XElement WithDirection(XName name, string? direction, string fallback)
    {
        var element = new XElement(name);
        element.Set("dir", direction is null || direction == fallback ? null : direction);
        return element;
    }

    // ---------------------------------------------------------------- timing

    public static SlideTiming? ReadTiming(XElement? timing)
    {
        if (timing is null)
        {
            return null;
        }

        var root = timing.Element(P + "tnLst")?.Element(P + "par")?.Element(P + "cTn");
        var sequences = root?.Element(P + "childTnLst")?.Elements(P + "seq").ToList() ?? [];
        var main = sequences.FirstOrDefault(seq => seq.Element(P + "cTn").Attr("nodeType") == "mainSeq");
        var effects = new List<AnimationEffect>();
        foreach (var clickGroup in main?.Element(P + "cTn")?.Element(P + "childTnLst")?.Elements(P + "par") ?? [])
        {
            foreach (var subGroup in clickGroup.Element(P + "cTn")?.Element(P + "childTnLst")?.Elements(P + "par") ?? [])
            {
                foreach (var effect in subGroup.Element(P + "cTn")?.Element(P + "childTnLst")?.Elements(P + "par") ?? [])
                {
                    if (ReadEffect(effect) is { } read)
                    {
                        effects.Add(read);
                    }
                }
            }
        }

        var interactive = sequences.Where(seq => seq != main).Select(XmlFragment.From).ToList();
        return new SlideTiming(effects, interactive, XmlFragment.From(timing));
    }

    public static AnimationEffect? ReadEffect(XElement par)
    {
        var node = par.Element(P + "cTn");
        if (node is null)
        {
            return null;
        }

        var behaviors = node.Element(P + "childTnLst")?.Elements().Select(ReadBehavior).ToList() ?? [];
        var target = node.Descendants(P + "spTgt").FirstOrDefault();
        var range = target?.Element(P + "txEl")?.Element(P + "pRg");
        return new AnimationEffect(
            target.Int("spid") ?? 0,
            node.Attr("presetClass") switch
            {
                "entr" => AnimationClass.Entrance,
                "emph" => AnimationClass.Emphasis,
                "exit" => AnimationClass.Exit,
                "path" => AnimationClass.MotionPath,
                _ => AnimationClass.Other,
            },
            node.Int("presetID") ?? 0,
            node.Int("presetSubtype") ?? 0,
            node.Attr("nodeType") switch
            {
                "withEffect" => AnimationTrigger.WithPrevious,
                "afterEffect" => AnimationTrigger.AfterPrevious,
                _ => AnimationTrigger.OnClick,
            },
            Delay(node),
            behaviors,
            range.Int("st"),
            range.Int("end"),
            node.Int("grpId") ?? 0,
            node.Int("repeatCount"),
            XmlFragment.From(par));
    }

    private static int Delay(XElement? node) =>
        node?.Element(P + "stCondLst")?.Elements(P + "cond").Select(cond => cond.Attr("delay")).FirstOrDefault(delay => delay is not null and not "indefinite") is { } value
        && int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var delay) ? delay : 0;

    private static int Duration(XElement? node) =>
        int.TryParse(node.Attr("dur"), NumberStyles.Integer, CultureInfo.InvariantCulture, out var duration) ? duration : 0;

    private static AnimationBehavior ReadBehavior(XElement element)
    {
        var behavior = element.Element(P + "cBhvr");
        var node = behavior?.Element(P + "cTn");
        var (delay, duration) = (Delay(node), Duration(node));
        var attribute = behavior?.Element(P + "attrNameLst")?.Elements(P + "attrName").FirstOrDefault()?.Value;
        AnimationBehavior result = element.Name.LocalName switch
        {
            "set" when attribute == "style.visibility" =>
                new VisibilityBehavior(delay, duration, element.Element(P + "to")?.Element(P + "strVal").Attr("val") != "hidden"),
            "set" => new PropertyBehavior(delay, duration, attribute ?? "", ValueList<AnimationKey>.Empty, To: element.Element(P + "to")?.Elements().FirstOrDefault().Attr("val"), CalcMode: "set"),
            "animEffect" => new FilterBehavior(delay, duration, element.Attr("filter") ?? "fade", element.Attr("transition") != "out"),
            "anim" => new PropertyBehavior(
                delay,
                duration,
                attribute ?? "",
                element.Element(P + "tavLst")?.Elements(P + "tav").Select(tav => new AnimationKey(
                    tav.Int("tm") ?? 0,
                    tav.Element(P + "val")?.Elements().FirstOrDefault().Attr("val") ?? "0",
                    tav.Attr("fmla"))).ToList() ?? [],
                element.Attr("from"),
                element.Attr("to"),
                element.Attr("by"),
                element.Attr("valueType"),
                element.Attr("calcmode")),
            "animScale" => new ScaleBehavior(
                delay,
                duration,
                element.Element(P + "by").Int("x"),
                element.Element(P + "by").Int("y"),
                element.Element(P + "from").Int("x"),
                element.Element(P + "from").Int("y"),
                element.Element(P + "to").Int("x"),
                element.Element(P + "to").Int("y")),
            "animRot" => new RotateBehavior(delay, duration, element.Int("by") ?? 0),
            "animMotion" => new MotionBehavior(delay, duration, element.Attr("path") ?? "M 0 0 E", element.Attr("origin"), element.Attr("pathEditMode"), element.Int("rAng")),
            _ => new PreservedBehavior(delay, duration, XmlFragment.From(element)),
        };
        return result with
        {
            Acceleration = node.Int("accel") ?? 0,
            Deceleration = node.Int("decel") ?? 0,
            AutoReverse = node.Bool("autoRev") ?? false,
        };
    }

    /// <summary>
    /// The p:timing of a slide: as read while the model is unchanged; else the main sequence written anew (click groups of
    /// with/after chains with their delays), the kept interactive sequences after it, and a build list for the animated text shapes.
    /// </summary>
    public static XElement? WriteTiming(SlideTiming timing, Func<int, bool> isTextShape)
    {
        if (timing.Source is { } source && ReadTiming(source.ToElement()) == timing)
        {
            return source.ToElement();
        }

        if (timing.MainSequence.IsEmpty && timing.InteractiveSequences.IsEmpty)
        {
            return null;
        }

        var ids = new IdCounter();
        var mainId = 0;
        var children = new XElement(P + "childTnLst");
        if (!timing.MainSequence.IsEmpty)
        {
            var mainNode = new XElement(P + "cTn", new XAttribute("id", "0"), new XAttribute("dur", "indefinite"), new XAttribute("nodeType", "mainSeq"));
            mainId = ids.Next(mainNode);
            mainNode.Add(new XElement(P + "childTnLst", ClickGroups(timing.MainSequence, ids, mainId)));
            children.Add(new XElement(P + "seq", new XAttribute("concurrent", "1"), new XAttribute("nextAc", "seek"),
                mainNode,
                new XElement(P + "prevCondLst", new XElement(P + "cond", new XAttribute("evt", "onPrev"), new XAttribute("delay", "0"), new XElement(P + "tgtEl", new XElement(P + "sldTgt"))))
                , new XElement(P + "nextCondLst", new XElement(P + "cond", new XAttribute("evt", "onNext"), new XAttribute("delay", "0"), new XElement(P + "tgtEl", new XElement(P + "sldTgt"))))));
        }

        foreach (var sequence in timing.InteractiveSequences)
        {
            children.Add(sequence.ToElement());
        }

        var rootNode = new XElement(P + "cTn", new XAttribute("id", "0"), new XAttribute("dur", "indefinite"), new XAttribute("restart", "never"), new XAttribute("nodeType", "tmRoot"), children);
        ids.Next(rootNode);
        var element = new XElement(P + "timing", new XElement(P + "tnLst", new XElement(P + "par", rootNode)));
        var builds = timing.MainSequence
            .Where(effect => isTextShape(effect.ShapeId))
            .GroupBy(effect => (effect.ShapeId, effect.GroupId))
            .Select(group => group.Any(effect => effect.ParagraphStart is not null)
                ? new XElement(P + "bldP", new XAttribute("spid", Text(group.Key.ShapeId)), new XAttribute("grpId", Text(group.Key.GroupId)), new XAttribute("build", "p"))
                : new XElement(P + "bldP", new XAttribute("spid", Text(group.Key.ShapeId)), new XAttribute("grpId", Text(group.Key.GroupId)), new XAttribute("animBg", "1")))
            .ToList();
        if (builds.Count > 0)
        {
            element.Add(new XElement(P + "bldLst", builds));
        }

        Renumber(element);
        return element;
    }

    private sealed class IdCounter
    {
        private int _next = 1_000_000;

        public int Next(XElement node)
        {
            var id = _next++;
            node.SetAttributeValue("id", Text(id));
            return id;
        }
    }

    private static IEnumerable<XElement> ClickGroups(ValueList<AnimationEffect> effects, IdCounter ids, int mainId)
    {
        var groups = new List<List<List<AnimationEffect>>>();
        foreach (var effect in effects)
        {
            if (groups.Count == 0 || effect.Trigger == AnimationTrigger.OnClick)
            {
                groups.Add([[effect]]);
            }
            else if (effect.Trigger == AnimationTrigger.AfterPrevious)
            {
                groups[^1].Add([effect]);
            }
            else
            {
                groups[^1][^1].Add(effect);
            }
        }

        for (var groupIndex = 0; groupIndex < groups.Count; groupIndex++)
        {
            var group = groups[groupIndex];
            var groupNode = new XElement(P + "cTn", new XAttribute("fill", "hold"));
            ids.Next(groupNode);
            var conditions = new XElement(P + "stCondLst", new XElement(P + "cond", new XAttribute("delay", "indefinite")));
            if (groupIndex == 0 && group[0][0].Trigger != AnimationTrigger.OnClick)
            {
                conditions.Add(new XElement(P + "cond", new XAttribute("evt", "onBegin"), new XAttribute("delay", "0"), new XElement(P + "tn", new XAttribute("val", Text(mainId)))));
            }

            groupNode.Add(conditions);
            var subGroups = new XElement(P + "childTnLst");
            var offset = 0;
            foreach (var subGroup in group)
            {
                var subNode = new XElement(P + "cTn", new XAttribute("fill", "hold"));
                ids.Next(subNode);
                subNode.Add(new XElement(P + "stCondLst", new XElement(P + "cond", new XAttribute("delay", Text(offset)))));
                subNode.Add(new XElement(P + "childTnLst", subGroup.Select(effect => WriteEffect(effect, ids))));
                subGroups.Add(new XElement(P + "par", subNode));
                offset += subGroup.Max(effect => effect.DelayMs + effect.DurationMs);
            }

            groupNode.Add(subGroups);
            yield return new XElement(P + "par", groupNode);
        }
    }

    private static XElement WriteEffect(AnimationEffect effect, IdCounter ids)
    {
        if (effect.Extra is { } extra && ReadEffect(extra.ToElement()) is { } read && Same(read, effect))
        {
            var kept = extra.ToElement();
            var node = kept.Element(P + "cTn")!;
            node.SetAttributeValue("nodeType", NodeType(effect.Trigger));
            foreach (var cTn in kept.Descendants(P + "cTn"))
            {
                ids.Next(cTn);
            }

            return kept;
        }

        var effectNode = new XElement(P + "cTn");
        ids.Next(effectNode);
        effectNode.SetAttributeValue("presetID", Text(effect.PresetId));
        effectNode.SetAttributeValue("presetClass", effect.Class switch
        {
            AnimationClass.Entrance => "entr",
            AnimationClass.Emphasis => "emph",
            AnimationClass.Exit => "exit",
            AnimationClass.MotionPath => "path",
            _ => "verb",
        });
        effectNode.SetAttributeValue("presetSubtype", Text(effect.PresetSubtype));
        effectNode.Set("repeatCount", effect.RepeatCount);
        effectNode.SetAttributeValue("fill", "hold");
        effectNode.SetAttributeValue("grpId", Text(effect.GroupId));
        effectNode.SetAttributeValue("nodeType", NodeType(effect.Trigger));
        effectNode.Add(new XElement(P + "stCondLst", new XElement(P + "cond", new XAttribute("delay", Text(effect.DelayMs)))));
        effectNode.Add(new XElement(P + "childTnLst", effect.Behaviors.Select(behavior => WriteBehavior(behavior, effect, ids))));
        return new XElement(P + "par", effectNode);
    }

    /// <summary>Two effects alike except for the trigger and the kept XML (the trigger is the click group's, written anew).</summary>
    private static bool Same(AnimationEffect first, AnimationEffect second) =>
        first with { Trigger = AnimationTrigger.OnClick, Extra = null } == second with { Trigger = AnimationTrigger.OnClick, Extra = null };

    private static string NodeType(AnimationTrigger trigger) => trigger switch
    {
        AnimationTrigger.WithPrevious => "withEffect",
        AnimationTrigger.AfterPrevious => "afterEffect",
        _ => "clickEffect",
    };

    private static XElement Behavior(AnimationBehavior behavior, AnimationEffect effect, IdCounter ids, string? attribute, bool hold = true, params XAttribute[] behaviorAttributes)
    {
        var node = new XElement(P + "cTn");
        ids.Next(node);
        node.SetAttributeValue("dur", Text(behavior.DurationMs));
        node.Set("accel", behavior.Acceleration == 0 ? null : behavior.Acceleration);
        node.Set("decel", behavior.Deceleration == 0 ? null : behavior.Deceleration);
        node.Set("autoRev", behavior.AutoReverse ? true : null);
        if (hold)
        {
            node.SetAttributeValue("fill", "hold");
        }

        if (behavior.DelayMs != 0)
        {
            node.Add(new XElement(P + "stCondLst", new XElement(P + "cond", new XAttribute("delay", Text(behavior.DelayMs)))));
        }

        var target = new XElement(P + "spTgt", new XAttribute("spid", Text(effect.ShapeId)));
        if (effect.ParagraphStart is { } start)
        {
            target.Add(new XElement(P + "txEl", new XElement(P + "pRg", new XAttribute("st", Text(start)), new XAttribute("end", Text(effect.ParagraphEnd ?? start)))));
        }

        var element = new XElement(P + "cBhvr", behaviorAttributes, node, new XElement(P + "tgtEl", target));
        if (attribute is not null)
        {
            element.Add(new XElement(P + "attrNameLst", attribute.Split(' ').Select(name => new XElement(P + "attrName", name))));
        }

        return element;
    }

    private static XElement WriteBehavior(AnimationBehavior behavior, AnimationEffect effect, IdCounter ids)
    {
        switch (behavior)
        {
            case VisibilityBehavior visibility:
                return new XElement(P + "set",
                    Behavior(visibility, effect, ids, "style.visibility"),
                    new XElement(P + "to", new XElement(P + "strVal", new XAttribute("val", visibility.Visible ? "visible" : "hidden"))));
            case FilterBehavior filter:
                return new XElement(P + "animEffect", new XAttribute("transition", filter.In ? "in" : "out"), new XAttribute("filter", filter.Filter),
                    Behavior(filter, effect, ids, null, hold: false));
            case PropertyBehavior { CalcMode: "set" } set:
                return new XElement(P + "set",
                    Behavior(set, effect, ids, set.Attribute),
                    new XElement(P + "to", new XElement(P + "strVal", new XAttribute("val", set.To ?? ""))));
            case PropertyBehavior property:
                {
                    var element = new XElement(P + "anim");
                    element.Set("by", property.By);
                    element.Set("from", property.From);
                    element.Set("to", property.To);
                    element.Set("calcmode", property.CalcMode);
                    element.Set("valueType", property.ValueType);
                    element.Add(Behavior(property, effect, ids, property.Attribute, true, new XAttribute("additive", "base")));
                    if (!property.Keys.IsEmpty)
                    {
                        element.Add(new XElement(P + "tavLst", property.Keys.Select(key =>
                        {
                            var tav = new XElement(P + "tav", new XAttribute("tm", Text(key.Time)));
                            tav.Set("fmla", key.Formula);
                            tav.Add(new XElement(P + "val", new XElement(P + "strVal", new XAttribute("val", key.Value))));
                            return tav;
                        })));
                    }

                    return element;
                }

            case ScaleBehavior scale:
                {
                    var element = new XElement(P + "animScale", Behavior(scale, effect, ids, null));
                    if (scale.ByX is not null || scale.ByY is not null)
                    {
                        element.Add(new XElement(P + "by", new XAttribute("x", Text(scale.ByX ?? 100000)), new XAttribute("y", Text(scale.ByY ?? 100000))));
                    }

                    if (scale.FromX is not null || scale.FromY is not null)
                    {
                        element.Add(new XElement(P + "from", new XAttribute("x", Text(scale.FromX ?? 100000)), new XAttribute("y", Text(scale.FromY ?? 100000))));
                    }

                    if (scale.ToX is not null || scale.ToY is not null)
                    {
                        element.Add(new XElement(P + "to", new XAttribute("x", Text(scale.ToX ?? 100000)), new XAttribute("y", Text(scale.ToY ?? 100000))));
                    }

                    return element;
                }

            case RotateBehavior rotate:
                return new XElement(P + "animRot", new XAttribute("by", Text(rotate.By)), Behavior(rotate, effect, ids, "r"));
            case MotionBehavior motion:
                {
                    var element = new XElement(P + "animMotion");
                    element.Set("origin", motion.Origin);
                    element.SetAttributeValue("path", motion.Path);
                    element.Set("pathEditMode", motion.PathEditMode);
                    element.Set("rAng", motion.RotationCenterX);
                    element.SetAttributeValue("ptsTypes", "");
                    element.Add(Behavior(motion, effect, ids, "ppt_x ppt_y"));
                    return element;
                }

            case PreservedBehavior preserved:
                {
                    var element = preserved.Xml.ToElement();
                    foreach (var node in element.Descendants(P + "cTn"))
                    {
                        ids.Next(node);
                    }

                    return element;
                }

            default:
                throw new InvalidOperationException("Unknown behaviour.");
        }
    }

    /// <summary>Numbers every p:cTn 1, 2, 3 … in document order and points p:tn references at the new numbers.</summary>
    private static void Renumber(XElement timing)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        var next = 1;
        foreach (var node in timing.Descendants(P + "cTn"))
        {
            var old = node.Attr("id") ?? "";
            var id = Text(next++);
            map.TryAdd(old, id);
            node.SetAttributeValue("id", id);
        }

        foreach (var reference in timing.Descendants(P + "tn"))
        {
            if (reference.Attr("val") is { } value && map.TryGetValue(value, out var mapped))
            {
                reference.SetAttributeValue("val", mapped);
            }
        }
    }
}
