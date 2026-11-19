using ThixShow.Core.Model;

namespace ThixShow.Core.Editing;

/// <summary>What happens to the content when the slide size changes ("Maximieren" / "Passend skalieren" / nothing).</summary>
public enum SlideScaleMode
{
    None,
    Fit,
    Maximize,
}

public sealed partial class EditingSession
{
    // ------------------------------------------------------------ design

    /// <summary>
    /// Applies a built-in design to the whole presentation: a new master with its layouts replaces the masters; every slide moves to
    /// the layout of the same type (else "Titel und Inhalt"), its placeholders keep their content.
    /// </summary>
    public void ApplyDesign(ThemeDesign design) =>
        Apply("Design", presentation =>
        {
            var master = MasterFactory.CreateMaster(design, presentation.SlideSize, presentation.NextMasterOrLayoutId);
            var slides = presentation.Slides.Select(slide =>
            {
                var type = presentation.LayoutOf(slide).Type;
                var layout = master.Layouts.FirstOrDefault(item => item.Type == type)
                             ?? master.Layouts.FirstOrDefault(item => item.Type == "obj")
                             ?? master.Layouts[0];
                return slide with { LayoutId = layout.Id };
            }).ToList();
            return presentation with { Masters = [master], Slides = slides };
        });

    /// <summary>A colour variant: the colours of every master's theme.</summary>
    public void SetThemeColors(ColorScheme colors) =>
        Apply("Farben", presentation => presentation with
        {
            Masters = presentation.Masters.Select(master => master with { Theme = master.Theme with { Colors = colors } }).ToList(),
        });

    /// <summary>The fonts of every master's theme.</summary>
    public void SetThemeFonts(FontScheme fonts) =>
        Apply("Schriftarten", presentation => presentation with
        {
            Masters = presentation.Masters.Select(master => master with { Theme = master.Theme with { Fonts = fonts } }).ToList(),
        });

    /// <summary>
    /// Changes the slide size. <see cref="SlideScaleMode.Fit"/> shrinks the content uniformly so everything fits (centred),
    /// <see cref="SlideScaleMode.Maximize"/> scales it to fill (content may run over the edges), <see cref="SlideScaleMode.None"/>
    /// keeps positions. Masters and layouts scale alike; font sizes stay (gap: PowerPoint scales them with "Passend").
    /// </summary>
    public void SetSlideSize(SlideSize size, SlideScaleMode mode = SlideScaleMode.Fit) =>
        Apply("Foliengröße", presentation =>
        {
            if (presentation.SlideSize == size)
            {
                return presentation;
            }

            var old = presentation.SlideSize;
            var (ratioX, ratioY) = (size.Width / (double)old.Width, size.Height / (double)old.Height);
            var scale = mode switch
            {
                SlideScaleMode.Fit => Math.Min(ratioX, ratioY),
                SlideScaleMode.Maximize => Math.Max(ratioX, ratioY),
                _ => 1,
            };
            var offsetX = mode == SlideScaleMode.None ? 0 : (size.Width - (old.Width * scale)) / 2;
            var offsetY = mode == SlideScaleMode.None ? 0 : (size.Height - (old.Height * scale)) / 2;
            Transform2D Scale(Transform2D transform) => transform with
            {
                X = (long)Math.Round((transform.X * scale) + offsetX),
                Y = (long)Math.Round((transform.Y * scale) + offsetY),
                Width = (long)Math.Round(transform.Width * scale),
                Height = (long)Math.Round(transform.Height * scale),
            };
            ValueList<SlideElement> Shapes(ValueList<SlideElement> shapes) =>
                shapes.Select(shape => shape.Transform is { } transform ? shape with { Transform = Scale(transform) } : shape).ToList();

            return presentation with
            {
                SlideSize = size,
                Masters = presentation.Masters.Select(master => master with
                {
                    Shapes = Shapes(master.Shapes),
                    Layouts = master.Layouts.Select(layout => layout with { Shapes = Shapes(layout.Shapes) }).ToList(),
                }).ToList(),
                Slides = presentation.Slides.Select(slide => slide with { Shapes = Shapes(slide.Shapes) }).ToList(),
            };
        });

    /// <summary>Sets the background of slides (null: from the layout/master again).</summary>
    public void SetBackground(IReadOnlyList<uint> slideIds, Background? background) =>
        Apply("Hintergrund formatieren", presentation => slideIds.Aggregate(presentation, (current, id) => WithSlide(current, id, slide => slide with { Background = background })));

    /// <summary>Gives every slide the background of <paramref name="background"/> ("Auf alle anwenden").</summary>
    public void SetBackgroundForAll(Background? background) => SetBackground(Presentation.Slides.Select(slide => slide.Id).ToList(), background);

    // ------------------------------------------------------------ transitions

    /// <summary>Sets the transition of slides (null or <see cref="TransitionKind.None"/> removes it).</summary>
    public void SetTransition(IReadOnlyList<uint> slideIds, SlideTransition? transition) =>
        Apply("Übergang", presentation => slideIds.Aggregate(presentation, (current, id) => WithSlide(current, id, slide =>
            slide with { Transition = transition is null || (transition.Kind == TransitionKind.None && transition.AdvanceAfterMs is null && transition.AdvanceOnClick) ? null : transition })));

    /// <summary>"Auf alle anwenden": every slide gets the current slide's transition.</summary>
    public void ApplyTransitionToAll()
    {
        var transition = CurrentSlide?.Transition;
        SetTransition(Presentation.Slides.Select(slide => slide.Id).ToList(), transition);
    }

    // ------------------------------------------------------------ animations

    private void EditTiming(string label, Func<ValueList<AnimationEffect>, ValueList<AnimationEffect>> change)
    {
        var slideId = RequireSlide();
        Apply(label, presentation => WithSlide(presentation, slideId, slide =>
        {
            var timing = slide.Timing ?? SlideTiming.Empty;
            var effects = change(timing.MainSequence);
            if (effects == timing.MainSequence)
            {
                return slide;
            }

            var updated = timing with { MainSequence = effects };
            return slide with { Timing = updated.MainSequence.IsEmpty && updated.InteractiveSequences.IsEmpty ? null : updated };
        }));
    }

    /// <summary>Adds an effect for each selected shape at the end of the slide's sequence.</summary>
    public void AddAnimation(AnimationPreset preset, AnimationDirection direction = AnimationDirection.Bottom, AnimationTrigger trigger = AnimationTrigger.OnClick, IReadOnlyList<int>? shapeIds = null)
    {
        var ids = shapeIds ?? SelectedShapeIds();
        if (ids.Count == 0)
        {
            return;
        }

        EditTiming("Animation hinzufügen", effects => effects.AddRange(ids.Select((id, index) =>
            AnimationPresets.Create(preset, id, index == 0 ? trigger : AnimationTrigger.WithPrevious, direction: direction))));
    }

    /// <summary>Replaces the effect at <paramref name="index"/> by another preset (trigger, delay and duration kept where sensible).</summary>
    public void ChangeAnimation(int index, AnimationPreset preset, AnimationDirection direction = AnimationDirection.Bottom) =>
        EditTiming("Animation ändern", effects =>
        {
            if (index < 0 || index >= effects.Count)
            {
                return effects;
            }

            var old = effects[index];
            return effects.SetItem(index, AnimationPresets.Create(preset, old.ShapeId, old.Trigger, direction: direction, delayMs: old.DelayMs) with
            {
                ParagraphStart = old.ParagraphStart,
                ParagraphEnd = old.ParagraphEnd,
                GroupId = old.GroupId,
            });
        });

    public void RemoveAnimation(int index) =>
        EditTiming("Animation entfernen", effects => index >= 0 && index < effects.Count ? effects.RemoveAt(index) : effects);

    /// <summary>Moves an effect in the sequence (Früher / Später).</summary>
    public void MoveAnimation(int from, int to) =>
        EditTiming("Animation verschieben", effects =>
        {
            if (from < 0 || from >= effects.Count || to < 0 || to >= effects.Count || from == to)
            {
                return effects;
            }

            var effect = effects[from];
            return effects.RemoveAt(from).Insert(to, effect);
        });

    /// <summary>Changes start (Beim Klicken / Mit Vorherigen / Nach Vorherigen), delay and duration of an effect.</summary>
    public void SetAnimationTiming(int index, AnimationTrigger? trigger = null, int? delayMs = null, int? durationMs = null) =>
        EditTiming("Anzeigedauer", effects =>
        {
            if (index < 0 || index >= effects.Count)
            {
                return effects;
            }

            var effect = effects[index];
            if (durationMs is { } duration)
            {
                effect = AnimationPresets.WithDuration(effect, Math.Max(1, duration));
            }

            effect = effect with { Trigger = trigger ?? effect.Trigger, DelayMs = Math.Max(0, delayMs ?? effect.DelayMs) };
            return effect == effects[index] ? effects : effects.SetItem(index, effect);
        });

    /// <summary>Removes every effect of shapes that are gone from the slide (after deleting shapes).</summary>
    internal static Slide WithoutDeadAnimations(Slide slide)
    {
        if (slide.Timing is not { } timing)
        {
            return slide;
        }

        var ids = ShapeTree.All(slide.Shapes).Select(shape => shape.Id).ToHashSet();
        var effects = timing.MainSequence.RemoveAll(effect => !ids.Contains(effect.ShapeId));
        if (effects.Count == timing.MainSequence.Count)
        {
            return slide;
        }

        return slide with { Timing = effects.IsEmpty && timing.InteractiveSequences.IsEmpty ? null : timing with { MainSequence = effects } };
    }

    // ------------------------------------------------------------ notes

    /// <summary>Sets the notes of a slide (paragraphs separated by "\n"); creates its notes page when it has none.</summary>
    public void SetNotesText(uint slideId, string text) =>
        Apply("Notizen", presentation => WithSlide(presentation, slideId, slide =>
        {
            var notes = slide.Notes ?? new NotesSlide
            {
                Shapes =
                [
                    new AutoShape { Id = 2, Name = "Folienbildplatzhalter 1", Placeholder = new PlaceholderInfo(PlaceholderType.SlideImage) },
                    new AutoShape { Id = 3, Name = "Notizenplatzhalter 2", Placeholder = new PlaceholderInfo(PlaceholderType.Body, 1), Text = TextBody.Empty },
                ],
            };
            var body = notes.NotesBody;
            if (body is null)
            {
                var id = ShapeTree.All(notes.Shapes).Select(shape => shape.Id).DefaultIfEmpty(1).Max() + 1;
                body = new AutoShape { Id = id, Name = $"Notizenplatzhalter {id - 1}", Placeholder = new PlaceholderInfo(PlaceholderType.Body, 1), Text = TextBody.Empty };
                notes = notes with { Shapes = notes.Shapes.Add(body) };
            }

            if (body.Text?.PlainText == text)
            {
                return slide;
            }

            var format = body.Text is { } old ? TextEdits.PropertiesAt(old, new TextPosition(0, 0)) : new RunProperties(Language: "de-DE");
            var updated = body with
            {
                Text = (body.Text ?? TextBody.Empty) with
                {
                    Paragraphs = text.Split('\n').Select(line => TextParagraph.Plain(line, format)).ToList(),
                },
            };
            return slide with { Notes = notes with { Shapes = ShapeTree.Replace(notes.Shapes, body.Id, _ => updated) } };
        }));
}
