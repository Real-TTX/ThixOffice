namespace ThixShow.Core.Model;

/// <summary>The effects ThixShow offers in its animation gallery (German names), each a PowerPoint preset id and subtype.</summary>
public enum AnimationPreset
{
    Appear,
    Fade,
    FlyIn,
    Wipe,
    Zoom,
    Split,
    Wheel,
    FloatIn,
    GrowAndTurn,
    Spin,
    GrowShrink,
    Transparency,
    Pulse,
    Disappear,
    FadeOut,
    FlyOut,
    WipeOut,
    ZoomOut,
    FloatOut,
    MotionLine,
}

/// <summary>Where an effect comes from or goes to (fly in from the bottom …).</summary>
public enum AnimationDirection
{
    Bottom,
    Left,
    Right,
    Top,
}

/// <summary>
/// Builds the common effects the way PowerPoint writes them (preset id/class/subtype and the behaviours of ECMA-376's timing model:
/// p:set of visibility, p:animEffect filters, p:anim of ppt_x/ppt_y/ppt_w/ppt_h with keyframe formulas, p:animScale, p:animRot,
/// p:animMotion), so files open with the same effect in PowerPoint and the show engine plays them.
/// </summary>
public static class AnimationPresets
{
    /// <summary>The German name of a preset for the gallery.</summary>
    public static string Label(AnimationPreset preset) => preset switch
    {
        AnimationPreset.Appear => "Erscheinen",
        AnimationPreset.Fade => "Verblassen",
        AnimationPreset.FlyIn => "Hineinfliegen",
        AnimationPreset.Wipe => "Wischen",
        AnimationPreset.Zoom => "Zoom",
        AnimationPreset.Split => "Teilen",
        AnimationPreset.Wheel => "Rad",
        AnimationPreset.FloatIn => "Hineinschweben",
        AnimationPreset.GrowAndTurn => "Wachsen und drehen",
        AnimationPreset.Spin => "Rotieren",
        AnimationPreset.GrowShrink => "Vergrößern/Verkleinern",
        AnimationPreset.Transparency => "Transparenz",
        AnimationPreset.Pulse => "Impuls",
        AnimationPreset.Disappear => "Verschwinden",
        AnimationPreset.FadeOut => "Verblassen (Beenden)",
        AnimationPreset.FlyOut => "Hinausfliegen",
        AnimationPreset.WipeOut => "Wischen (Beenden)",
        AnimationPreset.ZoomOut => "Zoom (Beenden)",
        AnimationPreset.FloatOut => "Hinausschweben",
        AnimationPreset.MotionLine => "Linien",
        _ => preset.ToString(),
    };

    public static AnimationClass ClassOf(AnimationPreset preset) => preset switch
    {
        AnimationPreset.Spin or AnimationPreset.GrowShrink or AnimationPreset.Transparency or AnimationPreset.Pulse => AnimationClass.Emphasis,
        AnimationPreset.Disappear or AnimationPreset.FadeOut or AnimationPreset.FlyOut or AnimationPreset.WipeOut or AnimationPreset.ZoomOut or AnimationPreset.FloatOut => AnimationClass.Exit,
        AnimationPreset.MotionLine => AnimationClass.MotionPath,
        _ => AnimationClass.Entrance,
    };

    /// <summary>The preset subtype of a direction (PowerPoint: 4 bottom, 8 left, 2 right, 1 top).</summary>
    public static int Subtype(AnimationDirection direction) => direction switch
    {
        AnimationDirection.Left => 8,
        AnimationDirection.Right => 2,
        AnimationDirection.Top => 1,
        _ => 4,
    };

    public static AnimationDirection DirectionOf(int subtype) => subtype switch
    {
        8 => AnimationDirection.Left,
        2 => AnimationDirection.Right,
        1 => AnimationDirection.Top,
        _ => AnimationDirection.Bottom,
    };

    /// <summary>
    /// A new effect of <paramref name="preset"/> on shape <paramref name="shapeId"/>: duration (ms, null = the preset's), direction for
    /// fly/wipe effects.
    /// </summary>
    public static AnimationEffect Create(AnimationPreset preset, int shapeId, AnimationTrigger trigger = AnimationTrigger.OnClick, int? durationMs = null, AnimationDirection direction = AnimationDirection.Bottom, int delayMs = 0)
    {
        var duration = durationMs ?? DefaultDuration(preset);
        var subtype = Subtype(direction);
        (int Id, AnimationBehavior[] Behaviors, int Subtype) definition = preset switch
        {
            AnimationPreset.Appear => (1, Show(0), 0),
            AnimationPreset.Fade => (10, [.. Show(0), new FilterBehavior(0, duration, "fade", true)], 0),
            AnimationPreset.FlyIn => (2, [.. Show(0), .. Fly(duration, direction, true)], subtype),
            AnimationPreset.Wipe => (22, [.. Show(0), new FilterBehavior(0, duration, WipeFilter(direction), true)], subtype),
            AnimationPreset.Zoom => (53, [.. Show(0), Property(duration, "ppt_w", "0", "#ppt_w"), Property(duration, "ppt_h", "0", "#ppt_h"), new FilterBehavior(0, duration, "fade", true)], 16),
            AnimationPreset.Split => (16, [.. Show(0), new FilterBehavior(0, duration, "barn(inVertical)", true)], 21),
            AnimationPreset.Wheel => (21, [.. Show(0), new FilterBehavior(0, duration, "wheel(1)", true)], 1),
            AnimationPreset.FloatIn => (42, [.. Show(0), new FilterBehavior(0, duration, "fade", true), Property(duration, "ppt_x", "#ppt_x", "#ppt_x"), Property(duration, "ppt_y", "#ppt_y+.1", "#ppt_y")], 0),
            AnimationPreset.GrowAndTurn => (31, [.. Show(0), Property(duration, "ppt_w", "0", "#ppt_w"), Property(duration, "ppt_h", "0", "#ppt_h"), Property(duration, "style.rotation", "90", "0"), new FilterBehavior(0, duration, "fade", true)], 0),
            AnimationPreset.Spin => (8, [new RotateBehavior(0, duration, 21600000)], 0),
            AnimationPreset.GrowShrink => (6, [new ScaleBehavior(0, duration, 150000, 150000)], 0),
            AnimationPreset.Transparency => (9, [new PropertyBehavior(0, duration, "style.opacity", ValueList<AnimationKey>.Empty, To: "0.5", CalcMode: "set")], 0),
            AnimationPreset.Pulse => (26, [new ScaleBehavior(0, duration / 2, 105000, 105000) { AutoReverse = true }], 0),
            AnimationPreset.Disappear => (1, Hide(0), 0),
            AnimationPreset.FadeOut => (10, [new FilterBehavior(0, duration, "fade", false), .. Hide(duration - 1)], 0),
            AnimationPreset.FlyOut => (2, [.. Fly(duration, direction, false), .. Hide(duration - 1)], subtype),
            AnimationPreset.WipeOut => (22, [new FilterBehavior(0, duration, WipeFilter(direction), false), .. Hide(duration - 1)], subtype),
            AnimationPreset.ZoomOut => (53, [Property(duration, "ppt_w", "#ppt_w", "0"), Property(duration, "ppt_h", "#ppt_h", "0"), new FilterBehavior(0, duration, "fade", false), .. Hide(duration - 1)], 16),
            AnimationPreset.FloatOut => (42, [new FilterBehavior(0, duration, "fade", false), Property(duration, "ppt_x", "#ppt_x", "#ppt_x"), Property(duration, "ppt_y", "#ppt_y", "#ppt_y-.1"), .. Hide(duration - 1)], 0),
            AnimationPreset.MotionLine => (42, [new MotionBehavior(0, duration, MotionPath(direction))], DirectionSubtypeForPath(direction)),
            _ => (1, Show(0), 0),
        };
        return new AnimationEffect(shapeId, ClassOf(preset), definition.Id, definition.Subtype, trigger, delayMs, definition.Behaviors);
    }

    /// <summary>The preset an effect was made from (by class, id and its behaviours), or null for effects ThixShow did not make.</summary>
    public static AnimationPreset? PresetOf(AnimationEffect effect) => (effect.Class, effect.PresetId) switch
    {
        (AnimationClass.Entrance, 1) => AnimationPreset.Appear,
        (AnimationClass.Entrance, 10) => AnimationPreset.Fade,
        (AnimationClass.Entrance, 2) => AnimationPreset.FlyIn,
        (AnimationClass.Entrance, 22) => AnimationPreset.Wipe,
        (AnimationClass.Entrance, 53) => AnimationPreset.Zoom,
        (AnimationClass.Entrance, 16) => AnimationPreset.Split,
        (AnimationClass.Entrance, 21) => AnimationPreset.Wheel,
        (AnimationClass.Entrance, 42) => AnimationPreset.FloatIn,
        (AnimationClass.Entrance, 31) => AnimationPreset.GrowAndTurn,
        (AnimationClass.Emphasis, 8) => AnimationPreset.Spin,
        (AnimationClass.Emphasis, 6) => AnimationPreset.GrowShrink,
        (AnimationClass.Emphasis, 9) => AnimationPreset.Transparency,
        (AnimationClass.Emphasis, 26) => AnimationPreset.Pulse,
        (AnimationClass.Exit, 1) => AnimationPreset.Disappear,
        (AnimationClass.Exit, 10) => AnimationPreset.FadeOut,
        (AnimationClass.Exit, 2) => AnimationPreset.FlyOut,
        (AnimationClass.Exit, 22) => AnimationPreset.WipeOut,
        (AnimationClass.Exit, 53) => AnimationPreset.ZoomOut,
        (AnimationClass.Exit, 42) => AnimationPreset.FloatOut,
        (AnimationClass.MotionPath, _) => AnimationPreset.MotionLine,
        _ => null,
    };

    /// <summary>An effect with its timing scaled to <paramref name="durationMs"/> (every behaviour proportionally).</summary>
    public static AnimationEffect WithDuration(AnimationEffect effect, int durationMs)
    {
        var current = Math.Max(1, effect.Behaviors.IsEmpty ? 1 : effect.Behaviors.Max(behavior => behavior.DelayMs + behavior.DurationMs));
        var factor = durationMs / (double)current;
        int Scale(int value) => value <= 1 ? value : (int)Math.Round(value * factor);
        return effect with
        {
            Behaviors = effect.Behaviors.Select(behavior => behavior with { DurationMs = Scale(behavior.DurationMs), DelayMs = Scale(behavior.DelayMs) }).ToList(),
            Extra = null,
        };
    }

    public static int DefaultDuration(AnimationPreset preset) => preset switch
    {
        AnimationPreset.Appear or AnimationPreset.Disappear => 1,
        AnimationPreset.Spin or AnimationPreset.GrowShrink or AnimationPreset.MotionLine => 2000,
        AnimationPreset.Transparency => 2000,
        AnimationPreset.FlyIn or AnimationPreset.FlyOut => 500,
        AnimationPreset.FloatIn or AnimationPreset.FloatOut => 1000,
        _ => 500,
    };

    private static AnimationBehavior[] Show(int delay) => [new VisibilityBehavior(delay, 1, true)];

    private static AnimationBehavior[] Hide(int delay) => [new VisibilityBehavior(Math.Max(0, delay), 1, false)];

    private static PropertyBehavior Property(int duration, string attribute, string from, string to) =>
        new(0, duration, attribute, [new AnimationKey(0, from), new AnimationKey(100000, to)], ValueType: "num", CalcMode: "lin");

    private static AnimationBehavior[] Fly(int duration, AnimationDirection direction, bool into)
    {
        var (x, y) = direction switch
        {
            AnimationDirection.Left => ("0-#ppt_w/2", "#ppt_y"),
            AnimationDirection.Right => ("1+#ppt_w/2", "#ppt_y"),
            AnimationDirection.Top => ("#ppt_x", "0-#ppt_h/2"),
            _ => ("#ppt_x", "1+#ppt_h/2"),
        };
        return into
            ? [Property(duration, "ppt_x", x, "#ppt_x"), Property(duration, "ppt_y", y, "#ppt_y")]
            : [Property(duration, "ppt_x", "#ppt_x", x), Property(duration, "ppt_y", "#ppt_y", y)];
    }

    /// <summary>The wipe filter of a direction: the word names the side the wipe starts from (PowerPoint's default "from bottom" is wipe(down)).</summary>
    public static string WipeFilter(AnimationDirection direction) => direction switch
    {
        AnimationDirection.Left => "wipe(left)",
        AnimationDirection.Right => "wipe(right)",
        AnimationDirection.Top => "wipe(up)",
        _ => "wipe(down)",
    };

    private static string MotionPath(AnimationDirection direction) => direction switch
    {
        AnimationDirection.Left => "M 0 0 L -0.25 0 E",
        AnimationDirection.Right => "M 0 0 L 0.25 0 E",
        AnimationDirection.Top => "M 0 0 L 0 -0.25 E",
        _ => "M 0 0 L 0 0.25 E",
    };

    private static int DirectionSubtypeForPath(AnimationDirection direction) => direction switch
    {
        AnimationDirection.Left => 8,
        AnimationDirection.Right => 2,
        AnimationDirection.Top => 1,
        _ => 4,
    };
}
