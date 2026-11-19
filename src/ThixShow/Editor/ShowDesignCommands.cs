using System.Globalization;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;

namespace ThixShow.Editor;

/// <summary>
/// The commands of Entwurf (designs, Foliengröße with Maximieren / Passend sicherstellen, Hintergrund), Übergänge (the gallery,
/// Effektoptionen, Dauer, Auf alle anwenden, Nächste Folie bei Mausklick / nach Zeit, Vorschau) and Animationen (the gallery by
/// Eingang / Hervorgehoben / Beenden / Animationspfade, Effektoptionen, Start, Dauer, Verzögerung, Animationsbereich, Vorschau).
/// </summary>
internal static class ShowDesignCommands
{
    /// <summary>The transitions of the gallery with PowerPoint's German names.</summary>
    public static IReadOnlyList<(string Id, string Label, TransitionKind Kind)> Transitions { get; } =
    [
        ("none", "Ohne", TransitionKind.None),
        ("cut", "Schnitt", TransitionKind.Cut),
        ("fade", "Verblassen", TransitionKind.Fade),
        ("push", "Schieben", TransitionKind.Push),
        ("wipe", "Wischen", TransitionKind.Wipe),
        ("split", "Teilen", TransitionKind.Split),
        ("uncover", "Aufdecken", TransitionKind.Uncover),
        ("cover", "Bedecken", TransitionKind.Cover),
        ("zoom", "Zoom", TransitionKind.Zoom),
    ];

    /// <summary>Effektoptionen of a transition (the OOXML direction): Von rechts … for Schieben, Wischen, Bedecken, Aufdecken.</summary>
    public static IReadOnlyList<(string Id, string Label)> TransitionDirections { get; } =
    [
        ("l", "Von rechts"), ("r", "Von links"), ("u", "Von unten"), ("d", "Von oben"),
    ];

    /// <summary>The animation gallery: Eingang, Hervorgehoben, Beenden, Animationspfade.</summary>
    public static IReadOnlyList<(string Group, AnimationPreset[] Presets)> AnimationGroups { get; } =
    [
        ("Eingang", [AnimationPreset.Appear, AnimationPreset.Fade, AnimationPreset.FlyIn, AnimationPreset.FloatIn, AnimationPreset.Split, AnimationPreset.Wipe, AnimationPreset.Wheel, AnimationPreset.Zoom, AnimationPreset.GrowAndTurn]),
        ("Hervorgehoben", [AnimationPreset.Pulse, AnimationPreset.Spin, AnimationPreset.GrowShrink, AnimationPreset.Transparency]),
        ("Beenden", [AnimationPreset.Disappear, AnimationPreset.FadeOut, AnimationPreset.FlyOut, AnimationPreset.FloatOut, AnimationPreset.WipeOut, AnimationPreset.ZoomOut]),
        ("Animationspfade", [AnimationPreset.MotionLine]),
    ];

    public static IReadOnlyList<(string Id, string Label, AnimationDirection Direction)> AnimationDirections { get; } =
    [
        ("bottom", "Von unten", AnimationDirection.Bottom), ("left", "Von links", AnimationDirection.Left),
        ("right", "Von rechts", AnimationDirection.Right), ("top", "Von oben", AnimationDirection.Top),
    ];

    public static IReadOnlyList<(string Id, string Label, AnimationTrigger Trigger)> AnimationStarts { get; } =
    [
        ("click", "Beim Klicken", AnimationTrigger.OnClick), ("with", "Mit Vorherigen", AnimationTrigger.WithPrevious), ("after", "Nach Vorherigen", AnimationTrigger.AfterPrevious),
    ];

    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public static void Add(OfficeWindow window, Func<ShowEditor?> editorOf, Action<string, Action<ShowEditor>> run)
    {
        var commands = window.Commands;
        bool HasSlide() => editorOf()?.Session.CurrentSlide is not null;
        bool HasShapes() => editorOf() is { } editor && !editor.Session.Selection.Shapes.IsEmpty;
        SlideTransition? Transition() => editorOf()?.Session.CurrentSlide?.Transition;

        // ===== Entwurf =====
        commands.Add(new OfficeValueCommand(ShowCommandIds.Design, "Designs", OfficeValueKind.Choice,
            () => editorOf()?.Session.Presentation is { Masters.Count: > 0 } presentation ? BuiltInThemes.All.FirstOrDefault(design => design.Theme.Name == presentation.Masters[0].Theme.Name)?.Id : null,
            value => run("Design", editor =>
            {
                if (value is not null && BuiltInThemes.Find(value) is { } design)
                {
                    editor.Session.ApplyDesign(design);
                }
            }))
        {
            Icon = "palette", CanExecute = () => editorOf() is not null, Options = [.. BuiltInThemes.All.Select(design => design.Id)],
            Description = "Gibt der Präsentation ein Design: Farben, Schriftarten, Hintergründe und Layouts.", Keywords = ["thema", "vorlage", "farben"],
        });
        commands.Add(new OfficeCommand(ShowCommandIds.SlideSize, "Foliengröße", () => Task.CompletedTask)
        {
            Icon = "orientation", CanExecute = () => editorOf() is not null, IsSearchable = false, Description = "Standard (4:3), Breitbild (16:9) oder eine eigene Größe.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.SlideSizeStandard, "Standard (4:3)", () => SetSizeAsync(window, editorOf(), SlideSize.Standard))
        {
            Icon = "orientation", CanExecute = () => editorOf() is not null, IsChecked = () => IsSize(editorOf(), SlideSize.Standard),
            Description = "Folien im Format 4:3 (25,4 × 19,05 cm).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.SlideSizeWidescreen, "Breitbild (16:9)", () => SetSizeAsync(window, editorOf(), SlideSize.Widescreen))
        {
            Icon = "orientation", CanExecute = () => editorOf() is not null, IsChecked = () => IsSize(editorOf(), SlideSize.Widescreen),
            Description = "Folien im Format 16:9 (33,867 × 19,05 cm).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.SlideSizeCustom, "Benutzerdefinierte Foliengröße …", () => CustomSizeAsync(window, editorOf()))
        {
            Icon = "orientation", CanExecute = () => editorOf() is not null, Description = "Legt Breite und Höhe der Folien in Zentimetern fest.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.FormatBackground, "Hintergrund formatieren", OfficeValueKind.Color, () => editorOf()?.BackgroundColor,
            value => run("Hintergrund", editor =>
            {
                editor.BackgroundColor = value;
                editor.Session.SetBackground(editor.SelectedSlideIds, ShowPalette.ToColorSpec(value) is { } color ? new Background(new SolidFill(color)) : null);
            }))
        {
            Icon = "fill", CanExecute = HasSlide, Description = "Gibt den markierten Folien eine Hintergrundfarbe (Automatisch: die des Layouts).",
        });

        // ===== Übergänge =====
        commands.Add(new OfficeValueCommand(ShowCommandIds.Transition, "Übergang", OfficeValueKind.Choice,
            () => Transition() is { } transition ? Transitions.FirstOrDefault(entry => entry.Kind == transition.Kind).Id ?? "none" : "none",
            value => run("Übergang", editor =>
            {
                var kind = Transitions.FirstOrDefault(entry => entry.Id == value).Kind;
                var current = editor.Session.CurrentSlide?.Transition;
                var transition = kind == TransitionKind.None
                    ? (current is { AdvanceAfterMs: not null } || current is { AdvanceOnClick: false } ? new SlideTransition(TransitionKind.None, AdvanceOnClick: current.AdvanceOnClick, AdvanceAfterMs: current.AdvanceAfterMs) : null)
                    : new SlideTransition(kind, current?.Direction, current?.DurationMs, AdvanceOnClick: current?.AdvanceOnClick ?? true, AdvanceAfterMs: current?.AdvanceAfterMs);
                editor.Session.SetTransition(editor.SelectedSlideIds, transition);
            }))
        {
            Icon = "transition", CanExecute = HasSlide, Options = [.. Transitions.Select(entry => entry.Id)],
            Description = "Wählt, wie die Folie in der Bildschirmpräsentation erscheint.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.Transition + ".direction", "Effektoptionen", OfficeValueKind.Choice,
            () => Transition()?.Direction ?? "l",
            value => run("Effektoptionen", editor =>
            {
                if (editor.Session.CurrentSlide?.Transition is { } transition)
                {
                    editor.Session.SetTransition(editor.SelectedSlideIds, transition with { Direction = value });
                }
            }))
        {
            Icon = "sliders", CanExecute = () => Transition()?.Kind is TransitionKind.Push or TransitionKind.Wipe or TransitionKind.Cover or TransitionKind.Uncover,
            Options = [.. TransitionDirections.Select(entry => entry.Id)], Description = "Die Richtung des Übergangs.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.TransitionDuration, "Dauer", OfficeValueKind.Number,
            () => Transition() is { } transition ? (transition.EffectiveDurationMs / 1000.0).ToString("0.##", Invariant) : "0",
            value => run("Dauer", editor =>
            {
                if (editor.Session.CurrentSlide?.Transition is { } transition && double.TryParse(value, NumberStyles.Float, Invariant, out var seconds))
                {
                    editor.Session.SetTransition(editor.SelectedSlideIds, transition with { DurationMs = (int)Math.Round(Math.Clamp(seconds, 0.01, 59) * 1000) });
                }
            }))
        {
            CanExecute = () => Transition() is { Kind: not TransitionKind.None }, Description = "Wie lange der Übergang dauert (Sekunden).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.ApplyToAll, "Auf alle anwenden", () => run("Auf alle anwenden", editor => editor.Session.ApplyTransitionToAll()))
        {
            Icon = "check-square", CanExecute = HasSlide, Description = "Gibt allen Folien den Übergang und die Zeiten dieser Folie.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.AdvanceOnClick, "Bei Mausklick", () => run("Nächste Folie", editor =>
        {
            var transition = editor.Session.CurrentSlide?.Transition ?? new SlideTransition(TransitionKind.None);
            editor.Session.SetTransition(editor.SelectedSlideIds, transition with { AdvanceOnClick = !transition.AdvanceOnClick });
        }))
        {
            Icon = "check", CanExecute = HasSlide, IsChecked = () => Transition()?.AdvanceOnClick ?? true,
            Description = "Die nächste Folie kommt mit einem Mausklick.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.AdvanceAfter, "Nach", OfficeValueKind.Number,
            () => Transition()?.AdvanceAfterMs is { } after ? (after / 1000.0).ToString("0.##", Invariant) : "0",
            value => run("Nächste Folie", editor =>
            {
                if (!double.TryParse(value, NumberStyles.Float, Invariant, out var seconds))
                {
                    return;
                }

                var transition = editor.Session.CurrentSlide?.Transition ?? new SlideTransition(TransitionKind.None);
                editor.Session.SetTransition(editor.SelectedSlideIds, transition with { AdvanceAfterMs = seconds <= 0 ? null : (int)Math.Round(Math.Min(seconds, 86399) * 1000) });
            }))
        {
            CanExecute = HasSlide, Description = "Die nächste Folie kommt nach dieser Zeit (Sekunden; 0 = aus).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.PreviewTransition, "Vorschau", () => editorOf()?.Preview())
        {
            Icon = "play", CanExecute = HasSlide, Description = "Spielt den Übergang und die Animationen der Folie ab.",
        });

        // ===== Animationen =====
        commands.Add(new OfficeValueCommand(ShowCommandIds.Animation, "Animation", OfficeValueKind.Choice,
            () => SelectedEffect(editorOf()) is { } effect ? AnimationPresets.PresetOf(effect.Effect)?.ToString() : "none",
            value => run("Animation", editor =>
            {
                var effects = EffectsOfSelection(editor);
                if (value == "none")
                {
                    foreach (var (index, _) in effects.OrderByDescending(entry => entry.Index))
                    {
                        editor.Session.RemoveAnimation(index);
                    }

                    return;
                }

                if (!Enum.TryParse<AnimationPreset>(value, out var preset))
                {
                    return;
                }

                if (effects.Count > 0)
                {
                    foreach (var (index, _) in effects)
                    {
                        editor.Session.ChangeAnimation(index, preset, editor.AnimationDirection);
                    }
                }
                else
                {
                    editor.Session.AddAnimation(preset, editor.AnimationDirection);
                }
            }))
        {
            Icon = "animation", CanExecute = HasShapes, Options = ["none", .. AnimationGroups.SelectMany(group => group.Presets).Select(preset => preset.ToString())],
            Description = "Gibt den markierten Formen einen Effekt in der Bildschirmpräsentation.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.Animation + ".direction", "Effektoptionen", OfficeValueKind.Choice,
            () => editorOf() is { } editor ? AnimationDirections.First(entry => entry.Direction == editor.AnimationDirection).Id : null,
            value => run("Effektoptionen", editor =>
            {
                editor.AnimationDirection = AnimationDirections.FirstOrDefault(entry => entry.Id == value).Direction;
                foreach (var (index, effect) in EffectsOfSelection(editor))
                {
                    if (AnimationPresets.PresetOf(effect) is { } preset)
                    {
                        editor.Session.ChangeAnimation(index, preset, editor.AnimationDirection);
                    }
                }
            }))
        {
            Icon = "sliders", CanExecute = () => SelectedEffect(editorOf()) is { } entry && AnimationPresets.PresetOf(entry.Effect) is AnimationPreset.FlyIn or AnimationPreset.FlyOut or AnimationPreset.Wipe or AnimationPreset.WipeOut or AnimationPreset.FloatIn or AnimationPreset.FloatOut,
            Options = [.. AnimationDirections.Select(entry => entry.Id)], Description = "Die Richtung des Effekts.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.AnimationStart, "Start", OfficeValueKind.Choice,
            () => SelectedEffect(editorOf()) is { } entry ? AnimationStarts.First(start => start.Trigger == entry.Effect.Trigger).Label : null,
            value => run("Start", editor =>
            {
                var trigger = AnimationStarts.FirstOrDefault(start => start.Label == value || start.Id == value).Trigger;
                foreach (var (index, _) in EffectsOfSelection(editor))
                {
                    editor.Session.SetAnimationTiming(index, trigger: trigger);
                }
            }))
        {
            Icon = "play", CanExecute = () => SelectedEffect(editorOf()) is not null, Options = [.. AnimationStarts.Select(start => start.Label)],
            Description = "Wann der Effekt beginnt: beim Klicken, mit dem vorherigen oder nach dem vorherigen.",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.AnimationDuration, "Dauer", OfficeValueKind.Number,
            () => SelectedEffect(editorOf()) is { } entry ? (entry.Effect.DurationMs / 1000.0).ToString("0.##", Invariant) : null,
            value => run("Dauer", editor =>
            {
                if (double.TryParse(value, NumberStyles.Float, Invariant, out var seconds))
                {
                    foreach (var (index, _) in EffectsOfSelection(editor))
                    {
                        editor.Session.SetAnimationTiming(index, durationMs: (int)Math.Round(Math.Clamp(seconds, 0.01, 59) * 1000));
                    }
                }
            }))
        {
            CanExecute = () => SelectedEffect(editorOf()) is not null, Description = "Wie lange der Effekt dauert (Sekunden).",
        });
        commands.Add(new OfficeValueCommand(ShowCommandIds.AnimationDelay, "Verzögerung", OfficeValueKind.Number,
            () => SelectedEffect(editorOf()) is { } entry ? (entry.Effect.DelayMs / 1000.0).ToString("0.##", Invariant) : null,
            value => run("Verzögerung", editor =>
            {
                if (double.TryParse(value, NumberStyles.Float, Invariant, out var seconds))
                {
                    foreach (var (index, _) in EffectsOfSelection(editor))
                    {
                        editor.Session.SetAnimationTiming(index, delayMs: (int)Math.Round(Math.Clamp(seconds, 0, 59) * 1000));
                    }
                }
            }))
        {
            CanExecute = () => SelectedEffect(editorOf()) is not null, Description = "Wie lange der Effekt nach seinem Start wartet (Sekunden).",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.AnimationPane, "Animationsbereich", () => editorOf()?.ToggleAnimationPane())
        {
            Icon = "list", CanExecute = HasSlide, IsChecked = () => editorOf()?.AnimationPaneVisible == true,
            Description = "Zeigt die Effekte der Folie in ihrer Reihenfolge – zum Umordnen, Ändern und Entfernen.",
        });
        commands.Add(new OfficeCommand(ShowCommandIds.PreviewAnimation, "Vorschau", () => editorOf()?.Preview())
        {
            Icon = "play", CanExecute = () => editorOf()?.Session.CurrentSlide is { Timing: not null } or { Transition: not null },
            Description = "Spielt die Animationen der Folie ab.",
        });
    }

    /// <summary>The effects (index in the slide's sequence) of the selected shapes.</summary>
    public static IReadOnlyList<(int Index, AnimationEffect Effect)> EffectsOfSelection(ShowEditor editor)
    {
        var ids = editor.Session.Selection.Shapes.ToHashSet();
        var effects = editor.Session.CurrentSlide?.Timing?.MainSequence;
        if (effects is null || effects.Value.IsEmpty)
        {
            return [];
        }

        return [.. effects.Value.Select((effect, index) => (index, effect)).Where(entry => ids.Contains(entry.effect.ShapeId))];
    }

    private static (int Index, AnimationEffect Effect)? SelectedEffect(ShowEditor? editor) =>
        editor is null ? null : EffectsOfSelection(editor) is { Count: > 0 } effects ? effects[0] : null;

    private static bool IsSize(ShowEditor? editor, SlideSize size) =>
        editor?.Session.Presentation.SlideSize is { } current && current.Width == size.Width && current.Height == size.Height;

    /// <summary>A new slide size: PowerPoint's question whether the content is maximized or made to fit.</summary>
    private static async Task SetSizeAsync(OfficeWindow window, ShowEditor? editor, SlideSize size)
    {
        if (editor is null || IsSize(editor, size))
        {
            return;
        }

        var answer = await QuestionDialog.AskAsync(window, "Foliengröße",
            "Sie skalieren auf eine neue Foliengröße. Möchten Sie die Größe Ihres Inhalts maximieren oder ihn so skalieren, dass er auf die neue Folie passt?",
            new DialogChoice("Passend skalieren", "check", "MatButton.Primary", IsDefault: true),
            new DialogChoice("Maximieren", "zoom-in"),
            new DialogChoice("Abbrechen", "x", IsCancel: true));
        if (answer is 0 or 1)
        {
            await editor.RunAsync("Foliengröße", () => editor.Session.SetSlideSize(size, answer == 0 ? SlideScaleMode.Fit : SlideScaleMode.Maximize));
        }
    }

    /// <summary>Benutzerdefinierte Foliengröße: width × height in cm (1–142 cm, PowerPoint's range).</summary>
    private static async Task CustomSizeAsync(OfficeWindow window, ShowEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        var current = editor.Session.Presentation.SlideSize;
        var german = CultureInfo.GetCultureInfo("de-DE");
        var text = $"{Units.EmuToCentimeters(current.Width).ToString("0.##", german)} x {Units.EmuToCentimeters(current.Height).ToString("0.##", german)}";
        string? error = null;
        while (true)
        {
            var answer = await TextInputDialog.AskAsync(window, "Foliengröße", "Breite x Höhe (cm)", text, "OK", error);
            if (answer is null)
            {
                return;
            }

            var parts = answer.Split(['x', 'X', '×'], StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 2 && double.TryParse(parts[0], NumberStyles.Float, german, out var width) && double.TryParse(parts[1], NumberStyles.Float, german, out var height)
                && width is >= 1 and <= 142 && height is >= 1 and <= 142)
            {
                await SetSizeAsync(window, editor, new SlideSize(Units.CentimetersToEmu(width), Units.CentimetersToEmu(height), "custom"));
                return;
            }

            (text, error) = (answer, "Geben Sie Breite und Höhe zwischen 1 und 142 cm ein, z. B. „25,4 x 19,05“.");
        }
    }
}
