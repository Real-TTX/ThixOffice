using ThixOffice.Text;
using ThixOffice.Text.Drawing;
using ThixShow.Core.Formatting;
using ThixShow.Core.Layout;
using ThixShow.Core.Model;

namespace ThixShow.Core.Show;

/// <summary>Where a slide show is: a transition, a slide (with its build steps), or the end screen.</summary>
public enum ShowPhase
{
    Transition,
    Slide,
    End,
}

/// <summary>How a show runs: the first slide, hidden slides, looping until Esc, slide timings (auto-advance).</summary>
public sealed record SlideShowOptions(int StartSlide = 0, bool IncludeHidden = false, bool Loop = false, bool UseTimings = true);

/// <summary>
/// The slide show engine, UI-free and deterministic: a state machine over the shown slides and their build steps
/// (<see cref="SlideTimeline"/>), driven by clicks (<see cref="Next"/>, <see cref="Previous"/>, <see cref="GoTo"/>) and a clock the UI
/// passes in (<c>now</c>, monotonic). <see cref="Frame"/> gives, for any moment, the layers to draw: static content as shared display
/// lists, animated shapes as layers with opacity, transform and clip from their effects, and during transitions the old and new
/// slide with the transition's parameters (fade, push, wipe, split, cover, uncover, zoom; others fade, cut cuts). Slide display lists
/// are rendered once per slide and reused for every frame, so a UI can draw at 60 fps.
/// </summary>
public sealed class SlideShow
{
    private readonly Presentation _presentation;
    private readonly ITextMeasurer _measurer;
    private readonly RenderOptions _renderOptions;
    private readonly Dictionary<int, SlideContent> _content = [];
    private TimeSpan _phaseStart;
    private TimeSpan _stepStart;
    private TimeSpan _slideStart;
    private TimeSpan? _showStart;
    private int? _previousPosition;
    private int _previousSteps;

    public SlideShow(Presentation presentation, ITextMeasurer measurer, SlideShowOptions? options = null, RenderOptions? renderOptions = null)
    {
        _presentation = presentation;
        _measurer = measurer;
        Options = options ?? new SlideShowOptions();
        _renderOptions = renderOptions ?? new RenderOptions();
        Shown = Enumerable.Range(0, presentation.SlideCount).Where(index => Options.IncludeHidden || !presentation.Slides[index].Hidden).ToList();
        if (Shown.Count == 0 && presentation.SlideCount > 0)
        {
            Shown = [Math.Clamp(Options.StartSlide, 0, presentation.SlideCount - 1)];
        }
    }

    public SlideShowOptions Options { get; }

    /// <summary>The indexes (into the presentation) of the slides the show shows, in order.</summary>
    public IReadOnlyList<int> Shown { get; }

    /// <summary>The position in <see cref="Shown"/>.</summary>
    public int Position { get; private set; }

    public ShowPhase Phase { get; private set; } = ShowPhase.Slide;

    /// <summary>How many build steps of the current slide have started.</summary>
    public int StepsStarted { get; private set; }

    /// <summary>True after a click on the end screen (or <see cref="End"/>): the UI closes the show.</summary>
    public bool IsFinished { get; private set; }

    public int SlideIndex => Shown.Count == 0 ? 0 : Shown[Position];

    public Slide CurrentSlide => _presentation.Slides[SlideIndex];

    public SlideTimeline Timeline => ContentOf(SlideIndex).Timeline;

    private double Width => _presentation.SlideSize.WidthPoints;

    private double Height => _presentation.SlideSize.HeightPoints;

    /// <summary>Starts the show at <see cref="SlideShowOptions.StartSlide"/> (its transition plays from black).</summary>
    public void Start(TimeSpan now)
    {
        _showStart = now;
        var start = Shown.ToList().FindIndex(index => index >= Options.StartSlide);
        Position = start < 0 ? 0 : start;
        _previousPosition = null;
        BeginSlide(now, transition: true);
    }

    /// <summary>A click / → / Leertaste: finishes a running transition or effect, else starts the next build step, else the next slide.</summary>
    public void Next(TimeSpan now)
    {
        Update(now);
        switch (Phase)
        {
            case ShowPhase.End:
                IsFinished = true;
                return;
            case ShowPhase.Transition:
                _phaseStart = now - TimeSpan.FromMilliseconds(TransitionDuration(CurrentSlide));
                Update(now);
                return;
        }

        if (IsStepRunning(now))
        {
            _stepStart = now - TimeSpan.FromMilliseconds(Timeline.Steps[StepsStarted - 1].DurationMs + 1);
            return;
        }

        if (StepsStarted < Timeline.Steps.Count)
        {
            StartStep(now);
            return;
        }

        if (Options.UseTimings && CurrentSlide.Transition is { AdvanceOnClick: false, AdvanceAfterMs: not null })
        {
            return; // this slide advances only by its timing
        }

        AdvanceSlide(now);
    }

    /// <summary>← / Bild auf: undoes the last build step, else shows the previous slide fully built.</summary>
    public void Previous(TimeSpan now)
    {
        Update(now);
        if (Phase == ShowPhase.End)
        {
            Phase = ShowPhase.Slide;
            return;
        }

        var automatic = Timeline.StartsAutomatically ? 1 : 0;
        if (Phase == ShowPhase.Slide && StepsStarted > automatic)
        {
            StepsStarted--;
            _stepStart = now - TimeSpan.FromDays(1);
            return;
        }

        if (Position > 0)
        {
            Position--;
            Phase = ShowPhase.Slide;
            StepsStarted = Timeline.Steps.Count;
            _stepStart = now - TimeSpan.FromDays(1);
            _slideStart = now;
        }
    }

    /// <summary>Jumps to a position of <see cref="Shown"/> (Nummer + Eingabe, the presenter's slide list): no transition, not built.</summary>
    public void GoTo(int position, TimeSpan now)
    {
        if (Shown.Count == 0)
        {
            return;
        }

        _previousPosition = null;
        Position = Math.Clamp(position, 0, Shown.Count - 1);
        BeginSlide(now, transition: false);
    }

    /// <summary>Esc: ends the show.</summary>
    public void End()
    {
        Phase = ShowPhase.End;
        IsFinished = true;
    }

    /// <summary>True while something moves (a transition or an effect): the UI keeps drawing frames; otherwise it may wait for input.</summary>
    public bool IsAnimating(TimeSpan now)
    {
        Update(now);
        return Phase == ShowPhase.Transition || IsStepRunning(now) || (Options.UseTimings && CurrentSlide.Transition?.AdvanceAfterMs is not null && Phase == ShowPhase.Slide);
    }

    /// <summary>Moves the show on by the clock: transitions end, automatic steps start, timed slides advance.</summary>
    public void Update(TimeSpan now)
    {
        for (var guard = 0; guard < 1000; guard++)
        {
            if (Phase == ShowPhase.Transition && now - _phaseStart >= TimeSpan.FromMilliseconds(TransitionDuration(CurrentSlide)))
            {
                Phase = ShowPhase.Slide;
                _slideStart = _phaseStart + TimeSpan.FromMilliseconds(TransitionDuration(CurrentSlide));
                if (Timeline.StartsAutomatically && StepsStarted == 0)
                {
                    StartStep(_slideStart);
                }

                continue;
            }

            if (Phase == ShowPhase.Slide && Options.UseTimings && CurrentSlide.Transition?.AdvanceAfterMs is { } after
                && StepsStarted == Timeline.Steps.Count && !IsStepRunning(now))
            {
                var stepsEnd = StepsStarted == 0 ? _slideStart : _stepStart + TimeSpan.FromMilliseconds(Timeline.Steps[^1].DurationMs);
                var due = Max(_slideStart + TimeSpan.FromMilliseconds(after), stepsEnd);
                if (now >= due && (Position + 1 < Shown.Count || Options.Loop))
                {
                    AdvanceSlide(due);
                    continue;
                }
            }

            return;
        }
    }

    private static TimeSpan Max(TimeSpan first, TimeSpan second) => first > second ? first : second;

    private bool IsStepRunning(TimeSpan now) =>
        Phase == ShowPhase.Slide && StepsStarted > 0 && now - _stepStart < TimeSpan.FromMilliseconds(Timeline.Steps[StepsStarted - 1].DurationMs);

    private void StartStep(TimeSpan now)
    {
        StepsStarted++;
        _stepStart = now;
    }

    private void AdvanceSlide(TimeSpan now)
    {
        if (Position + 1 < Shown.Count)
        {
            (_previousPosition, _previousSteps) = (Position, StepsStarted);
            Position++;
            BeginSlide(now, transition: true);
            return;
        }

        if (Options.Loop && Shown.Count > 0)
        {
            (_previousPosition, _previousSteps) = (Position, StepsStarted);
            Position = 0;
            BeginSlide(now, transition: true);
            return;
        }

        Phase = ShowPhase.End;
        _phaseStart = now;
    }

    private void BeginSlide(TimeSpan now, bool transition)
    {
        StepsStarted = 0;
        _phaseStart = now;
        _slideStart = now;
        _stepStart = now;
        if (transition && TransitionDuration(CurrentSlide) > 0)
        {
            Phase = ShowPhase.Transition;
            return;
        }

        Phase = ShowPhase.Slide;
        if (Timeline.StartsAutomatically)
        {
            StartStep(now);
        }
    }

    private static int TransitionDuration(Slide slide) =>
        slide.Transition is { Kind: not (TransitionKind.None or TransitionKind.Cut) } transition ? transition.EffectiveDurationMs : 0;

    // ------------------------------------------------------------ frames

    /// <summary>The layers to draw at <paramref name="now"/>.</summary>
    public ShowFrame Frame(TimeSpan now)
    {
        Update(now);
        if (Phase == ShowPhase.End)
        {
            return EndFrame();
        }

        if (Phase == ShowPhase.Transition)
        {
            var transition = CurrentSlide.Transition!;
            var duration = Math.Max(1, TransitionDuration(CurrentSlide));
            var progress = Math.Clamp((now - _phaseStart).TotalMilliseconds / duration, 0, 1);
            var incoming = SlideLayers(SlideIndex, 0, null, now);
            var outgoing = _previousPosition is { } previous ? SlideLayers(Shown[previous], _previousSteps, null, now) : null;
            return new ShowFrame(Width, Height, TransitionLayers(transition, progress, outgoing, incoming), SlideIndex, true, false);
        }

        var stepTime = StepsStarted > 0 ? (now - _stepStart).TotalMilliseconds : 0;
        return new ShowFrame(Width, Height, SlideLayers(SlideIndex, StepsStarted, stepTime, now), SlideIndex, false, false);
    }

    /// <summary>A slide's layers with <paramref name="steps"/> build steps started, the last at <paramref name="stepTime"/> ms (null: finished).</summary>
    private List<ShowLayer> SlideLayers(int slideIndex, int steps, double? stepTime, TimeSpan now)
    {
        var content = ContentOf(slideIndex);
        var states = States(content, steps, stepTime);
        var layers = new List<ShowLayer>();
        foreach (var segment in content.Segments)
        {
            if (segment.ShapeId is not { } shapeId)
            {
                layers.Add(ShowLayer.Static(segment.Items));
                continue;
            }

            var state = states.GetValueOrDefault(shapeId, ShapeState.Shown);
            if (!state.Visible || state.Opacity <= 0 || (state.RevealSide is not null && state.Revealed <= 0))
            {
                continue;
            }

            var box = content.Boxes[shapeId];
            var (centerX, centerY) = (box.X + (box.Width / 2), box.Y + (box.Height / 2));
            var transform = state.IsIdentity
                ? Matrix2D.Identity
                : Matrix2D.Translation(-centerX, -centerY)
                  * Matrix2D.Scaling(state.ScaleX, state.ScaleY)
                  * Matrix2D.Rotation(state.Rotation)
                  * Matrix2D.Translation(centerX + state.OffsetX, centerY + state.OffsetY);
            layers.Add(new ShowLayer(segment.Items, state.Opacity, transform, Reveal(box, state)));
        }

        return layers;
    }

    private static ClipRect? Reveal(ShapeBox box, ShapeState state)
    {
        if (state.RevealSide is not { } side || state.Revealed >= 1)
        {
            return null;
        }

        var r = state.Revealed;
        // A little margin so outlines at the edge are not cut while fully revealed.
        var (x, y, w, h) = (box.X - 2, box.Y - 2, box.Width + 4, box.Height + 4);
        return side switch
        {
            WipeSide.Left => new ClipRect(x, y, w * r, h),
            WipeSide.Right => new ClipRect(x + (w * (1 - r)), y, w * r, h),
            WipeSide.Top => new ClipRect(x, y, w, h * r),
            WipeSide.Bottom => new ClipRect(x, y + (h * (1 - r)), w, h * r),
            WipeSide.CenterVertical => new ClipRect(x + (w * (1 - r) / 2), y, w * r, h),
            _ => new ClipRect(x, y + (h * (1 - r) / 2), w, h * r),
        };
    }

    private static Dictionary<int, ShapeState> States(SlideContent content, int steps, double? stepTime)
    {
        var states = content.Timeline.InitiallyHidden.ToDictionary(id => id, _ => ShapeState.HiddenState);
        for (var index = 0; index < Math.Min(steps, content.Timeline.Steps.Count); index++)
        {
            var step = content.Timeline.Steps[index];
            var time = index == steps - 1 && stepTime is { } current ? current : double.MaxValue / 4;
            foreach (var scheduled in step.Effects)
            {
                var local = time - scheduled.StartMs;
                if (local < 0)
                {
                    continue;
                }

                var state = states.GetValueOrDefault(scheduled.LayerShapeId, ShapeState.Shown);
                states[scheduled.LayerShapeId] = EffectEvaluator.Apply(state, scheduled.Effect, Math.Min(local, scheduled.Effect.DurationMs + 1e6), content.Boxes[scheduled.LayerShapeId]);
            }
        }

        return states;
    }

    private List<ShowLayer> TransitionLayers(SlideTransition transition, double p, List<ShowLayer>? outgoing, List<ShowLayer> incoming)
    {
        var (width, height) = (Width, Height);
        var old = outgoing is null ? null : Combine(outgoing);
        var next = Combine(incoming);
        ShowLayer Moved(ShowLayer layer, double dx, double dy) => layer with { Transform = layer.Transform * Matrix2D.Translation(dx, dy) };
        var direction = transition.Direction;
        var layers = new List<ShowLayer>();
        void Under()
        {
            if (old is not null)
            {
                layers.Add(old);
            }
        }

        (double X, double Y) Vector(string? dir, string fallback) => (dir ?? fallback) switch
        {
            "r" => (1, 0),
            "u" => (0, -1),
            "d" => (0, 1),
            "lu" => (-1, -1),
            "ru" => (1, -1),
            "ld" => (-1, 1),
            "rd" => (1, 1),
            _ => (-1, 0),
        };

        switch (transition.Kind)
        {
            case TransitionKind.Push:
                {
                    var (vx, vy) = Vector(direction, "l");
                    if (old is not null)
                    {
                        layers.Add(Moved(old, vx * width * p, vy * height * p));
                    }

                    layers.Add(Moved(next, -vx * width * (1 - p), -vy * height * (1 - p)));
                    break;
                }

            case TransitionKind.Cover:
                {
                    var (vx, vy) = Vector(direction, "l");
                    Under();
                    layers.Add(Moved(next, -vx * width * (1 - p), -vy * height * (1 - p)));
                    break;
                }

            case TransitionKind.Uncover:
                {
                    var (vx, vy) = Vector(direction, "l");
                    layers.Add(next);
                    if (old is not null)
                    {
                        layers.Add(Moved(old, vx * width * p, vy * height * p));
                    }

                    break;
                }

            case TransitionKind.Wipe:
                {
                    Under();
                    var clip = (direction ?? "l") switch
                    {
                        "r" => new ClipRect(0, 0, width * p, height),
                        "u" => new ClipRect(0, height * (1 - p), width, height * p),
                        "d" => new ClipRect(0, 0, width, height * p),
                        _ => new ClipRect(width * (1 - p), 0, width * p, height),
                    };
                    layers.Add(next with { Clip = clip });
                    break;
                }

            case TransitionKind.Split:
                {
                    var parts = (direction ?? "horz-out").Split('-');
                    var horizontal = parts[0] != "vert";
                    var outward = parts.Length < 2 || parts[1] != "in";
                    ClipRect Band(double amount) => horizontal
                        ? new ClipRect(0, height * (1 - amount) / 2, width, height * amount)
                        : new ClipRect(width * (1 - amount) / 2, 0, width * amount, height);
                    if (outward)
                    {
                        Under();
                        layers.Add(next with { Clip = Band(p) });
                    }
                    else
                    {
                        layers.Add(next);
                        if (old is not null)
                        {
                            layers.Add(old with { Clip = Band(1 - p) });
                        }
                    }

                    break;
                }

            case TransitionKind.Zoom:
                {
                    Under();
                    var scale = 0.3 + (0.7 * p);
                    layers.Add(next with
                    {
                        Opacity = p,
                        Transform = Matrix2D.Scaling(scale, scale, width / 2, height / 2),
                    });
                    break;
                }

            case TransitionKind.Fade when transition.ThroughBlack:
                if (p < 0.5)
                {
                    if (old is not null)
                    {
                        layers.Add(old with { Opacity = 1 - (2 * p) });
                    }
                }
                else
                {
                    layers.Add(next with { Opacity = (2 * p) - 1 });
                }

                break;
            default:
                // Fade, and every transition ThixShow does not draw (dissolve, random bars, morph, p14 effects) as a fade.
                Under();
                layers.Add(next with { Opacity = p });
                break;
        }

        return layers;
    }

    /// <summary>A slide's layers as one layer (for moving the whole slide in a transition).</summary>
    private static ShowLayer Combine(List<ShowLayer> layers)
    {
        if (layers.Count == 1 && layers[0].Transform.IsIdentity && layers[0].Opacity >= 1 && layers[0].Clip is null)
        {
            return layers[0];
        }

        return ShowLayer.Static(new ShowFrame(0, 0, layers, 0, false, false).ToDisplayList().Items.Skip(1).ToList());
    }

    private ShowFrame EndFrame()
    {
        // PowerPoint's words, set quietly in the middle: the line in light grey, the hint under it dimmer.
        const string title = "Ende der Bildschirmpräsentation";
        const string hint = "Zum Beenden klicken oder Esc drücken.";
        var titleFont = new FontSpec("Segoe UI", Math.Max(14, Height / 22));
        var hintFont = new FontSpec("Segoe UI", Math.Max(10, Height / 36));
        var shapedTitle = _measurer.Shape(title, titleFont);
        var shapedHint = _measurer.Shape(hint, hintFont);
        var titleMetrics = _measurer.GetMetrics(titleFont);
        var hintMetrics = _measurer.GetMetrics(hintFont);
        var gap = hintFont.Size * 0.8;
        var total = titleMetrics.Ascent + titleMetrics.Descent + gap + hintMetrics.Ascent + hintMetrics.Descent;
        var top = (Height - total) / 2;
        var items = new List<DisplayItem>();
        TextBodyLayouter.AddText(items, (Width - shapedTitle.Width) / 2, top + titleMetrics.Ascent, shapedTitle, 0, title.Length, titleFont, new RgbColor(0xE6, 0xE6, 0xE6), 0);
        var hintBaseline = top + titleMetrics.Ascent + titleMetrics.Descent + gap + hintMetrics.Ascent;
        TextBodyLayouter.AddText(items, (Width - shapedHint.Width) / 2, hintBaseline, shapedHint, 0, hint.Length, hintFont, new RgbColor(0x8C, 0x8C, 0x8C), 0);
        return new ShowFrame(Width, Height, [ShowLayer.Static(items)], SlideIndex, false, true);
    }

    // ------------------------------------------------------------ slide content (rendered once per slide)

    private sealed record Segment(IReadOnlyList<DisplayItem> Items, int? ShapeId);

    private sealed record SlideContent(SlideTimeline Timeline, IReadOnlyList<Segment> Segments, IReadOnlyDictionary<int, ShapeBox> Boxes);

    private SlideContent ContentOf(int slideIndex)
    {
        if (_content.TryGetValue(slideIndex, out var content))
        {
            return content;
        }

        var slide = _presentation.Slides[slideIndex];
        var timeline = SlideTimeline.Build(slide);
        var animated = timeline.AnimatedShapes;
        var context = SlideContext.For(_presentation, slide);
        var segments = new List<Segment>();
        var boxes = new Dictionary<int, ShapeBox>();
        var current = new List<DisplayItem>();
        SlideRenderer.RenderBackdrop(current, context, _measurer, _renderOptions, slideIndex + 1);
        foreach (var shape in slide.Shapes)
        {
            var paragraphLayers = timeline.ParagraphLayers.Where(pair => pair.Value.ShapeId == shape.Id).ToList();
            bool InLayer(int paragraph) => paragraphLayers.Any(pair => paragraph >= pair.Value.Start && paragraph <= pair.Value.End);
            var baseShape = paragraphLayers.Count == 0 ? shape : ParagraphMask.Keep(shape, paragraph => !InLayer(paragraph), withShape: true);
            if (!animated.Contains(shape.Id) && paragraphLayers.Count == 0)
            {
                SlideRenderer.RenderElement(current, context, shape, _measurer, _renderOptions, slideIndex + 1, null);
                continue;
            }

            if (!animated.Contains(shape.Id))
            {
                SlideRenderer.RenderElement(current, context, baseShape, _measurer, _renderOptions, slideIndex + 1, null);
            }
            else
            {
                if (current.Count > 0)
                {
                    segments.Add(new Segment(current, null));
                    current = [];
                }

                var items = new List<DisplayItem>();
                SlideRenderer.RenderElement(items, context, baseShape, _measurer, _renderOptions, slideIndex + 1, null);
                segments.Add(new Segment(items, shape.Id));
                var bounds = Editing.EditingSession.RotatedBounds(ShapeResolver.Resolve(context, shape).Transform);
                boxes[shape.Id] = new ShapeBox(Units.EmuToPoints(bounds.X), Units.EmuToPoints(bounds.Y), Units.EmuToPoints(bounds.Width), Units.EmuToPoints(bounds.Height), Width, Height);
            }

            // Each animated paragraph range: a layer of its own with only its lines, its box the lines' area.
            foreach (var (layer, range) in paragraphLayers)
            {
                if (current.Count > 0)
                {
                    segments.Add(new Segment(current, null));
                    current = [];
                }

                var items = new List<DisplayItem>();
                SlideRenderer.RenderElement(items, context, ParagraphMask.Keep(shape, paragraph => paragraph >= range.Start && paragraph <= range.End, withShape: false), _measurer, _renderOptions, slideIndex + 1, null);
                segments.Add(new Segment(items, layer));
                boxes[layer] = ParagraphBox(slide, shape, range.Start, range.End);
            }
        }

        if (current.Count > 0)
        {
            segments.Add(new Segment(current, null));
        }

        content = new SlideContent(timeline, segments, boxes);
        _content[slideIndex] = content;
        return content;
    }

    /// <summary>The area of a shape's paragraphs on the slide (the shape's box when the text cannot be laid out).</summary>
    private ShapeBox ParagraphBox(Slide slide, SlideElement shape, int start, int end)
    {
        if (TextGeometry.Of(_presentation, slide, new Editing.TextTarget(shape.Id), _measurer) is { } placed
            && placed.Layout.Lines.Where(line => line.Paragraph >= start && line.Paragraph <= end).ToList() is { Count: > 0 } lines)
        {
            var layout = placed.Layout;
            var (top, bottom) = (lines.Min(line => line.Top), lines.Max(line => line.Bottom));
            var corners = new[] { placed.Matrix.Apply(layout.BoxLeft, top), placed.Matrix.Apply(layout.BoxLeft + layout.BoxWidth, top), placed.Matrix.Apply(layout.BoxLeft, bottom), placed.Matrix.Apply(layout.BoxLeft + layout.BoxWidth, bottom) };
            var (left, upper) = (corners.Min(point => point.X), corners.Min(point => point.Y));
            return new ShapeBox(left, upper, corners.Max(point => point.X) - left, corners.Max(point => point.Y) - upper, Width, Height);
        }

        var bounds = Editing.EditingSession.RotatedBounds(ShapeResolver.Resolve(SlideContext.For(_presentation, slide), shape).Transform);
        return new ShapeBox(Units.EmuToPoints(bounds.X), Units.EmuToPoints(bounds.Y), Units.EmuToPoints(bounds.Width), Units.EmuToPoints(bounds.Height), Width, Height);
    }

    // ------------------------------------------------------------ presenter view

    /// <summary>The presenter's view: slide number, the next slide (or that the next click builds this one), notes, times.</summary>
    public PresenterView Presenter(TimeSpan now, DateTime clock)
    {
        Update(now);
        var remaining = Timeline.Steps.Count - StepsStarted;
        int? next = Position + 1 < Shown.Count ? Shown[Position + 1] : Options.Loop && Shown.Count > 0 ? Shown[0] : null;
        return new PresenterView(
            SlideIndex + 1,
            Position + 1,
            Shown.Count,
            next,
            remaining > 0 && Phase == ShowPhase.Slide,
            CurrentSlide.NotesText,
            _showStart is { } start ? now - start : TimeSpan.Zero,
            clock,
            Math.Max(0, remaining));
    }

    /// <summary>A thumbnail of a slide as it first appears (for the presenter's "next slide").</summary>
    public PageDisplayList Thumbnail(int slideIndex, double width) =>
        SlideRenderer.RenderThumbnail(_presentation, _presentation.Slides[slideIndex], _measurer, width);
}
