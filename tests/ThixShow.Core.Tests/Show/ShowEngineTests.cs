using ThixOffice.Text.Drawing;
using ThixOffice.Text.Skia;
using ThixShow.Core.Model;
using ThixShow.Core.Show;

namespace ThixShow.Core.Tests.Show;

public class ShowEngineTests
{
    private static readonly TimeSpan Zero = TimeSpan.Zero;

    private static TimeSpan Ms(double milliseconds) => TimeSpan.FromMilliseconds(milliseconds);

    /// <summary>Three slides; the second has a 1 s fade and two click steps on its rectangle (id 20) and a second shape (id 21).</summary>
    private static Presentation Deck(SlideTransition? transition = null, params AnimationEffect[] effects)
    {
        var presentation = TestDecks.Deck(3);
        var second = presentation.Slides[1];
        second = second with
        {
            Shapes = second.Shapes.AddRange([TestDecks.Rectangle(20, 1270000, 1270000, 1270000, 1270000, "accent1"), TestDecks.Rectangle(21, 5080000, 1270000, 1270000, 1270000, "accent2")]),
            Transition = transition ?? new SlideTransition(TransitionKind.Fade, DurationMs: 1000),
            Timing = new SlideTiming(effects.Length > 0 ? effects :
            [
                AnimationPresets.Create(AnimationPreset.FlyIn, 20, direction: AnimationDirection.Left),
                AnimationPresets.Create(AnimationPreset.Fade, 21, AnimationTrigger.WithPrevious),
                AnimationPresets.Create(AnimationPreset.Spin, 20, AnimationTrigger.AfterPrevious, delayMs: 250),
                AnimationPresets.Create(AnimationPreset.WipeOut, 21, direction: AnimationDirection.Top),
            ]),
            Notes = new NotesSlide { Shapes = [new AutoShape { Id = 3, Placeholder = new PlaceholderInfo(PlaceholderType.Body, 1), Text = TextBody.FromText("Hier betonen") }] },
        };
        return presentation with { Slides = presentation.Slides.SetItem(1, second) };
    }

    [Fact]
    public void Timelines_cut_effects_into_build_steps()
    {
        var timeline = SlideTimeline.Build(Deck().Slides[1]);
        Assert.Equal(2, timeline.Steps.Count);
        Assert.Equal(2, timeline.ClickCount);
        Assert.False(timeline.StartsAutomatically);
        var first = timeline.Steps[0].Effects;
        Assert.Equal([0, 0, 750], first.Select(effect => effect.StartMs).ToList());
        Assert.Equal(2750, timeline.Steps[0].DurationMs);
        Assert.Equal(new HashSet<int> { 20, 21 }, timeline.InitiallyHidden.ToHashSet());

        var automatic = SlideTimeline.Build(Deck(null, AnimationPresets.Create(AnimationPreset.Fade, 20, AnimationTrigger.AfterPrevious)).Slides[1]);
        Assert.True(automatic.StartsAutomatically);
        Assert.Equal(0, automatic.ClickCount);
    }

    [Fact]
    public void Effects_move_fade_wipe_scale_rotate_and_follow_paths()
    {
        var box = new ShapeBox(100, 100, 100, 50, 1000, 500);
        ShapeState At(AnimationEffect effect, double milliseconds, ShapeState? from = null) => EffectEvaluator.Apply(from ?? ShapeState.HiddenState, effect, milliseconds, box);

        var fade = At(AnimationPresets.Create(AnimationPreset.Fade, 1), 250);
        Assert.True(fade.Visible);
        Assert.Equal(0.5, fade.Opacity, 6);

        var fly = AnimationPresets.Create(AnimationPreset.FlyIn, 1, direction: AnimationDirection.Left);
        // Starts with its centre at 0 – w/2 (fraction): x offset = (−0.05 − 0.15) · 1000 = −200 pt.
        Assert.Equal(-200, At(fly, 0).OffsetX, 6);
        Assert.Equal(-100, At(fly, 250).OffsetX, 6);
        Assert.Equal(0, At(fly, 500).OffsetX, 6);

        var wipe = At(AnimationPresets.Create(AnimationPreset.Wipe, 1), 250);
        Assert.Equal(WipeSide.Bottom, wipe.RevealSide);
        Assert.Equal(0.5, wipe.Revealed, 6);

        Assert.Equal(180, At(AnimationPresets.Create(AnimationPreset.Spin, 1), 1000, ShapeState.Shown).Rotation, 6);
        Assert.Equal(1.5, At(AnimationPresets.Create(AnimationPreset.GrowShrink, 1), 5000, ShapeState.Shown).ScaleX, 6);
        Assert.Equal(1.025, At(AnimationPresets.Create(AnimationPreset.Pulse, 1), 125, ShapeState.Shown).ScaleX, 6);
        Assert.Equal(1, At(AnimationPresets.Create(AnimationPreset.Pulse, 1), 5000, ShapeState.Shown).ScaleX, 6);
        Assert.Equal(250, At(AnimationPresets.Create(AnimationPreset.MotionLine, 1, direction: AnimationDirection.Right), 2000, ShapeState.Shown).OffsetX, 6);
        Assert.Equal(0.5, At(AnimationPresets.Create(AnimationPreset.Transparency, 1), 1, ShapeState.Shown).Opacity, 6);
        Assert.Equal(0.5, At(AnimationPresets.Create(AnimationPreset.Zoom, 1), 250).ScaleX, 6);

        var gone = At(AnimationPresets.Create(AnimationPreset.FadeOut, 1), 600, ShapeState.Shown);
        Assert.False(gone.Visible);
    }

    [Theory]
    [InlineData("0-#ppt_w/2", -0.05)]
    [InlineData("1+#ppt_h/2", 1.05)]
    [InlineData("#ppt_y+.1", 0.35)]
    [InlineData("2*(1+3)^2", 32)]
    [InlineData("max(1,#ppt_x)", 1)]
    [InlineData("sin(0)+cos(0)", 1)]
    public void Value_formulas_evaluate(string formula, double expected)
    {
        var variables = new Dictionary<string, double> { ["#ppt_x"] = 0.15, ["#ppt_y"] = 0.25, ["#ppt_w"] = 0.1, ["#ppt_h"] = 0.1 };
        Assert.Equal(expected, EffectEvaluator.Formula(formula, variables), 6);
    }

    [Fact]
    public void Acceleration_and_deceleration_keep_the_ends()
    {
        Assert.Equal(0, EffectEvaluator.Ease(0, 0.5, 0.5), 6);
        Assert.Equal(1, EffectEvaluator.Ease(1, 0.5, 0.5), 6);
        Assert.Equal(0.5, EffectEvaluator.Ease(0.5, 0.5, 0.5), 6);
        Assert.True(EffectEvaluator.Ease(0.1, 0.5, 0) < 0.1);
        var previous = 0.0;
        for (var step = 1; step <= 100; step++)
        {
            var value = EffectEvaluator.Ease(step / 100.0, 0.3, 0.2);
            Assert.True(value >= previous);
            previous = value;
        }

        var (x, y) = EffectEvaluator.MotionPoint("M 0 0 L 1 0 L 1 1 E", 0.75);
        Assert.Equal((1.0, 0.5), (Math.Round(x, 6), Math.Round(y, 6)));
    }

    [Fact]
    public void The_show_plays_transitions_build_steps_and_slides_by_clicks()
    {
        var show = new SlideShow(Deck(), TestDecks.Measurer);
        show.Start(Zero);
        Assert.Equal(ShowPhase.Slide, show.Phase);
        Assert.Equal(0, show.SlideIndex);
        show.Next(Ms(100));
        Assert.Equal(1, show.SlideIndex);
        Assert.Equal(ShowPhase.Transition, show.Phase);

        var halfway = show.Frame(Ms(600));
        Assert.True(halfway.InTransition);
        Assert.Equal(2, halfway.Layers.Count);
        Assert.Equal(0.5, halfway.Layers[1].Opacity, 6);

        var built = show.Frame(Ms(1200));
        Assert.Equal(ShowPhase.Slide, show.Phase);
        Assert.Equal(0, show.StepsStarted);
        Assert.False(show.IsAnimating(Ms(1200)));

        show.Next(Ms(2000));
        Assert.Equal(1, show.StepsStarted);
        Assert.True(show.IsAnimating(Ms(2100)));
        var flying = show.Frame(Ms(2250));
        var layer = flying.Layers.Single(item => !item.Transform.IsIdentity && item.Opacity >= 1);
        Assert.True(layer.Transform.OffsetX < 0);
        show.Next(Ms(2300)); // a click during the effects finishes them
        Assert.False(show.IsAnimating(Ms(2300)));
        Assert.Equal(1, show.StepsStarted);
        show.Next(Ms(3000));
        Assert.Equal(2, show.StepsStarted);
        show.Next(Ms(3100));
        show.Next(Ms(4000));
        Assert.Equal(2, show.SlideIndex);

        show.Previous(Ms(4100));
        Assert.Equal(1, show.SlideIndex);
        Assert.Equal(2, show.StepsStarted);
        show.Previous(Ms(4200));
        Assert.Equal(1, show.StepsStarted);

        show.GoTo(2, Ms(5000));
        show.Next(Ms(5100));
        Assert.Equal(ShowPhase.End, show.Phase);
        Assert.True(show.Frame(Ms(5200)).IsEnd);
        show.Next(Ms(5300));
        Assert.True(show.IsFinished);
    }

    [Fact]
    public void Timed_slides_advance_by_themselves_and_hidden_slides_are_skipped()
    {
        var presentation = Deck(new SlideTransition(TransitionKind.Cut, AdvanceAfterMs: 2000), AnimationPresets.Create(AnimationPreset.Fade, 20, AnimationTrigger.AfterPrevious));
        presentation = presentation with { Slides = presentation.Slides.SetItem(0, presentation.Slides[0] with { Hidden = true }) };
        var show = new SlideShow(presentation, TestDecks.Measurer);
        Assert.Equal([1, 2], show.Shown);
        show.Start(Zero);
        Assert.Equal(1, show.SlideIndex);
        Assert.Equal(1, show.StepsStarted); // the automatic step
        show.Update(Ms(1999));
        Assert.Equal(1, show.SlideIndex);
        show.Update(Ms(2000));
        Assert.Equal(2, show.SlideIndex);

        var looping = new SlideShow(presentation, TestDecks.Measurer, new SlideShowOptions(Loop: true, IncludeHidden: true));
        looping.Start(Zero);
        looping.Next(Ms(10));
        looping.Update(Ms(5000));
        looping.Next(Ms(5010));
        Assert.Equal(0, looping.SlideIndex);
    }

    [Fact]
    public void Transitions_move_clip_and_fade_the_slides()
    {
        var size = SlideSize.Widescreen;
        ShowFrame FrameOf(SlideTransition transition, double at)
        {
            var show = new SlideShow(Deck(transition with { DurationMs = 1000 }), TestDecks.Measurer);
            show.Start(Zero);
            show.Next(Ms(0));
            return show.Frame(Ms(at));
        }

        var push = FrameOf(new SlideTransition(TransitionKind.Push, "u"), 250);
        Assert.Equal(-0.25 * size.HeightPoints, push.Layers[0].Transform.OffsetY, 6);
        Assert.Equal(0.75 * size.HeightPoints, push.Layers[1].Transform.OffsetY, 6);

        var wipe = FrameOf(new SlideTransition(TransitionKind.Wipe, "r"), 500);
        Assert.Equal(0.5 * size.WidthPoints, wipe.Layers[1].Clip!.Value.Width, 6);

        var split = FrameOf(new SlideTransition(TransitionKind.Split, "vert-out"), 500);
        Assert.Equal(0.25 * size.WidthPoints, split.Layers[1].Clip!.Value.X, 6);

        var cover = FrameOf(new SlideTransition(TransitionKind.Cover, "d"), 500);
        Assert.True(cover.Layers[0].Transform.IsIdentity);
        Assert.Equal(-0.5 * size.HeightPoints, cover.Layers[1].Transform.OffsetY, 6);

        var uncover = FrameOf(new SlideTransition(TransitionKind.Uncover, "l"), 500);
        Assert.Equal(-0.5 * size.WidthPoints, uncover.Layers[1].Transform.OffsetX, 6);

        var black = FrameOf(new SlideTransition(TransitionKind.Fade, ThroughBlack: true), 250);
        Assert.Single(black.Layers);
        Assert.Equal(0.5, black.Layers[0].Opacity, 6);

        var morph = FrameOf(new SlideTransition(TransitionKind.Morph), 500);
        Assert.Equal(0.5, morph.Layers[1].Opacity, 6);
    }

    [Fact]
    public void Frames_are_deterministic_and_draw()
    {
        var show = new SlideShow(Deck(), TestDecks.Measurer);
        show.Start(Zero);
        show.Next(Ms(0));
        show.Next(Ms(1000));
        var first = show.Frame(Ms(1300)).ToDisplayList();
        var second = show.Frame(Ms(1300)).ToDisplayList();
        Assert.Equal(TestDecks.Describe(first.Items), TestDecks.Describe(second.Items));
        Assert.Contains(first.Items, item => item is OpacityBeginItem);
        var png = SkiaVectorRenderer.RenderPng(first, 320);
        Assert.True(png.Length > 100);
    }

    [Fact]
    public void The_presenter_sees_notes_the_next_slide_and_the_time()
    {
        var show = new SlideShow(Deck(), TestDecks.Measurer);
        show.Start(Ms(1000));
        show.Next(Ms(1000));
        show.Update(Ms(3000));
        var view = show.Presenter(Ms(61000), new DateTime(2026, 11, 19, 14, 30, 0));
        Assert.Equal(2, view.SlideNumber);
        Assert.Equal("Hier betonen", view.Notes);
        Assert.Equal(2, view.NextSlideIndex);
        Assert.True(view.NextIsBuildStep);
        Assert.Equal(2, view.RemainingClicks);
        Assert.Equal(TimeSpan.FromSeconds(60), view.Elapsed);
        Assert.Equal(192, show.Thumbnail(view.NextSlideIndex!.Value, 192).Width, 6);
    }
}
