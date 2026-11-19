using ThixShow.Core.Model;

namespace ThixShow.Core.Show;

/// <summary>An effect placed in time: its start (ms) within its build step and the top-level shape it moves.</summary>
public sealed record ScheduledEffect(AnimationEffect Effect, int StartMs, int LayerShapeId)
{
    public int EndMs => StartMs + Effect.DurationMs;
}

/// <summary>
/// One build step of a slide: what one click starts (or, for <see cref="IsAutomatic"/>, what starts by itself when the slide
/// appears – effects "after/with previous" before the first click). Its effects with their start times; its duration.
/// </summary>
public sealed record BuildStep(IReadOnlyList<ScheduledEffect> Effects, bool IsAutomatic)
{
    public int DurationMs => Effects.Count == 0 ? 0 : Effects.Max(effect => effect.EndMs);
}

/// <summary>
/// The animation timeline of one slide, built from its main sequence (ECMA-376 timing model as PowerPoint writes it): effects are cut
/// into build steps at every "on click" effect; within a step "with previous" effects start with the effect before them and "after
/// previous" ones when the effects before them have ended; each effect's own delay comes on top. Shapes whose first effect is an
/// entrance start hidden. Effects on shapes inside groups move their top-level group (a documented simplification).
/// </summary>
public sealed class SlideTimeline
{
    private SlideTimeline(IReadOnlyList<BuildStep> steps, IReadOnlySet<int> initiallyHidden)
    {
        Steps = steps;
        InitiallyHidden = initiallyHidden;
    }

    public IReadOnlyList<BuildStep> Steps { get; }

    /// <summary>Top-level shapes hidden until their entrance plays.</summary>
    public IReadOnlySet<int> InitiallyHidden { get; }

    /// <summary>True when the first step starts by itself when the slide appears.</summary>
    public bool StartsAutomatically => Steps.Count > 0 && Steps[0].IsAutomatic;

    /// <summary>How many clicks the slide needs before the next click leaves it.</summary>
    public int ClickCount => Steps.Count(step => !step.IsAutomatic);

    /// <summary>The top-level shapes any effect moves (they get layers of their own in the show).</summary>
    public IReadOnlySet<int> AnimatedShapes => Steps.SelectMany(step => step.Effects).Select(effect => effect.LayerShapeId).ToHashSet();

    public static SlideTimeline Build(Slide slide)
    {
        var effects = slide.Timing?.MainSequence ?? ValueList<AnimationEffect>.Empty;
        var topLevel = new Dictionary<int, int>();
        foreach (var shape in slide.Shapes)
        {
            foreach (var inner in ShapeTree.All([shape]))
            {
                topLevel[inner.Id] = shape.Id;
            }
        }

        var steps = new List<BuildStep>();
        List<ScheduledEffect>? current = null;
        var automatic = false;
        var chainStart = 0;
        var chainEnd = 0;
        foreach (var effect in effects)
        {
            if (!topLevel.TryGetValue(effect.ShapeId, out var layer))
            {
                continue; // an effect on a shape that is gone
            }

            if (current is null || effect.Trigger == AnimationTrigger.OnClick)
            {
                if (current is not null)
                {
                    steps.Add(new BuildStep(current, automatic));
                }

                automatic = current is null && effect.Trigger != AnimationTrigger.OnClick;
                current = [];
                chainStart = 0;
                chainEnd = 0;
            }
            else if (effect.Trigger == AnimationTrigger.AfterPrevious)
            {
                chainStart = chainEnd;
            }

            var scheduled = new ScheduledEffect(effect, chainStart + effect.DelayMs, layer);
            current.Add(scheduled);
            chainEnd = Math.Max(chainEnd, scheduled.EndMs);
        }

        if (current is not null)
        {
            steps.Add(new BuildStep(current, automatic));
        }

        var hidden = new HashSet<int>();
        var seen = new HashSet<int>();
        foreach (var effect in steps.SelectMany(step => step.Effects))
        {
            if (seen.Add(effect.LayerShapeId) && effect.Effect.Class == AnimationClass.Entrance)
            {
                hidden.Add(effect.LayerShapeId);
            }
        }

        return new SlideTimeline(steps, hidden);
    }
}
