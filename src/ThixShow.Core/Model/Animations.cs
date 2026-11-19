namespace ThixShow.Core.Model;

/// <summary>The common slide transitions; the others are <see cref="Other"/> (kept as XML, shown as a fade).</summary>
public enum TransitionKind
{
    None,
    Cut,
    Fade,
    Push,
    Wipe,
    Split,
    Cover,
    Uncover,
    Zoom,
    Dissolve,
    RandomBars,
    Morph,
    Other,
}

/// <summary>
/// p:transition: the effect (<see cref="Kind"/>; <see cref="Direction"/> "l", "r", "u", "d", "lu" …, for split "horz-in" style
/// orient+dir), its duration (p14:dur or the speed's), advance on click and after a time. <see cref="Source"/> keeps the element
/// as read (sounds, p14/p15 effects, morph's mc:AlternateContent) and is written back while the model is unchanged.
/// </summary>
public sealed record SlideTransition(
    TransitionKind Kind,
    string? Direction = null,
    int? DurationMs = null,
    string? Speed = null,
    bool AdvanceOnClick = true,
    int? AdvanceAfterMs = null,
    bool ThroughBlack = false,
    XmlFragment? Source = null)
{
    public static SlideTransition None { get; } = new(TransitionKind.None);

    /// <summary>The duration in ms: the explicit one, else the speed's (fast 500, medium 750, slow 1000; PowerPoint's default 700).</summary>
    public int EffectiveDurationMs => DurationMs ?? Speed switch
    {
        "fast" => 500,
        "med" => 750,
        "slow" => 1000,
        _ => Kind is TransitionKind.None or TransitionKind.Cut ? 0 : 700,
    };

    /// <summary>The German name ("Verblassen" …).</summary>
    public string Label => Kind switch
    {
        TransitionKind.None => "Ohne",
        TransitionKind.Cut => "Schnitt",
        TransitionKind.Fade => "Verblassen",
        TransitionKind.Push => "Schieben",
        TransitionKind.Wipe => "Wischen",
        TransitionKind.Split => "Teilen",
        TransitionKind.Cover => "Aufdecken",
        TransitionKind.Uncover => "Abdecken",
        TransitionKind.Zoom => "Zoom",
        TransitionKind.Dissolve => "Auflösen",
        TransitionKind.RandomBars => "Zufällige Balken",
        TransitionKind.Morph => "Morphen",
        _ => "Anderer Übergang",
    };
}

/// <summary>Entrance, emphasis, exit, motion path (presetClass entr/emph/exit/path); verb/media effects are <see cref="Other"/>.</summary>
public enum AnimationClass
{
    Entrance,
    Emphasis,
    Exit,
    MotionPath,
    Other,
}

/// <summary>When an effect starts: on a click, with the previous effect, after the previous effect.</summary>
public enum AnimationTrigger
{
    OnClick,
    WithPrevious,
    AfterPrevious,
}

/// <summary>
/// One behaviour of an effect (p:set, p:animEffect, p:anim, p:animScale, p:animRot, p:animMotion), its own start delay and
/// duration (ms) within the effect, acceleration/deceleration (1000ths of a percent of the duration).
/// </summary>
public abstract record AnimationBehavior(int DelayMs, int DurationMs)
{
    public int Acceleration { get; init; }

    public int Deceleration { get; init; }

    public bool AutoReverse { get; init; }

    public int EndMs => DelayMs + (DurationMs * (AutoReverse ? 2 : 1));
}

/// <summary>p:set of style.visibility: shows or hides the shape at the behaviour's start.</summary>
public sealed record VisibilityBehavior(int DelayMs, int DurationMs, bool Visible) : AnimationBehavior(DelayMs, DurationMs);

/// <summary>p:animEffect: a filter transition in or out ("fade", "wipe(down)", "barn(inVertical)", "wheel(1)" …).</summary>
public sealed record FilterBehavior(int DelayMs, int DurationMs, string Filter, bool In) : AnimationBehavior(DelayMs, DurationMs);

/// <summary>A keyframe of a property animation: time (0–100000) and value formula ("0-#ppt_w/2", "#ppt_x", "1").</summary>
public sealed record AnimationKey(int Time, string Value, string? Formula = null);

/// <summary>
/// p:anim of a property ("ppt_x", "ppt_y", "ppt_w", "ppt_h", "style.rotation", "style.opacity", "r"): keyframes or from/to/by.
/// Positions are fractions of the slide; formulas use #ppt_x, #ppt_y, #ppt_w, #ppt_h (the shape's own values).
/// </summary>
public sealed record PropertyBehavior(
    int DelayMs,
    int DurationMs,
    string Attribute,
    ValueList<AnimationKey> Keys,
    string? From = null,
    string? To = null,
    string? By = null,
    string? ValueType = null,
    string? CalcMode = null) : AnimationBehavior(DelayMs, DurationMs);

/// <summary>p:animScale: scale by (1000ths of a percent) or from/to.</summary>
public sealed record ScaleBehavior(int DelayMs, int DurationMs, int? ByX, int? ByY, int? FromX = null, int? FromY = null, int? ToX = null, int? ToY = null)
    : AnimationBehavior(DelayMs, DurationMs);

/// <summary>p:animRot: rotate by an angle (60000ths of a degree).</summary>
public sealed record RotateBehavior(int DelayMs, int DurationMs, int By) : AnimationBehavior(DelayMs, DurationMs);

/// <summary>
/// p:animMotion: a motion path ("M 0 0 L 0.25 0.1 E" – fractions of the slide, relative to the shape's position).
/// </summary>
public sealed record MotionBehavior(int DelayMs, int DurationMs, string Path, string? Origin = "layout", string? PathEditMode = "relative", int? RotationCenterX = null)
    : AnimationBehavior(DelayMs, DurationMs);

/// <summary>A behaviour ThixShow does not play (colour animations, commands, media): kept as XML, its time counts.</summary>
public sealed record PreservedBehavior(int DelayMs, int DurationMs, XmlFragment Xml) : AnimationBehavior(DelayMs, DurationMs);

/// <summary>
/// One effect of the main sequence: the shape (and maybe a paragraph range of its text), class and preset (id/subtype as PowerPoint
/// names them), trigger, delay before it starts (ms) and its behaviours. <see cref="DurationMs"/> is the end of its last behaviour.
/// <see cref="Extra"/> keeps the effect's p:par as read (written back while the effect is unchanged).
/// </summary>
public sealed record AnimationEffect(
    int ShapeId,
    AnimationClass Class,
    int PresetId,
    int PresetSubtype,
    AnimationTrigger Trigger,
    int DelayMs,
    ValueList<AnimationBehavior> Behaviors,
    int? ParagraphStart = null,
    int? ParagraphEnd = null,
    int GroupId = 0,
    int? RepeatCount = null,
    XmlFragment? Extra = null)
{
    public int DurationMs => Behaviors.IsEmpty ? 0 : Behaviors.Max(behavior => behavior.EndMs) * Math.Max(1, (RepeatCount ?? 1000) / 1000);
}

/// <summary>
/// p:timing of a slide: the main sequence (the click-by-click effects ThixShow models and plays) and what else the timing tree
/// has (interactive sequences started by clicking a shape, media, build lists): kept as the whole element in <see cref="Source"/>,
/// written back unchanged while the main sequence is unchanged; otherwise the main sequence is written anew and the interactive
/// sequences from <see cref="InteractiveSequences"/>.
/// </summary>
public sealed record SlideTiming(ValueList<AnimationEffect> MainSequence, ValueList<XmlFragment> InteractiveSequences = default, XmlFragment? Source = null)
{
    public static SlideTiming Empty { get; } = new(ValueList<AnimationEffect>.Empty);

    public bool IsEmpty => MainSequence.IsEmpty && InteractiveSequences.IsEmpty && Source is null;
}
