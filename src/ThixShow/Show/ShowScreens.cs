using System.Globalization;
using Avalonia;
using Avalonia.Controls;

namespace ThixShow.Show;

/// <summary>A connected screen as the show sees it: its place in the system's list (0-based), its pixels, whether it is the main one.</summary>
public sealed record ShowScreen(int Index, PixelRect Bounds, bool IsPrimary)
{
    /// <summary>"Bildschirm 1 – 2560 × 1440 (Hauptbildschirm)".</summary>
    public string Label
    {
        get
        {
            var text = $"{ShowScreens.NameOf(Index)} – {Bounds.Width.ToString(CultureInfo.CurrentCulture)} × {Bounds.Height.ToString(CultureInfo.CurrentCulture)}";
            return IsPrimary ? text + " (Hauptbildschirm)" : text;
        }
    }
}

/// <summary>Where the audience and the presenter view go.</summary>
/// <param name="Audience">The screen of the show (null: no screen known, the editor's place).</param>
/// <param name="Presenter">The other screen for the presenter view, when there are two.</param>
public sealed record ShowArrangement(ShowScreen? Audience, ShowScreen? Presenter);

/// <summary>
/// The screens of Bildschirmpräsentation › Bildschirm, read anew each time they are needed (a projector comes and goes). The automatic
/// rule: with two screens the show goes to the one that is not the main screen and the presenter view to the other; with one screen
/// the show fills the screen of the editor's window. A chosen screen that is no longer connected counts as automatic.
/// </summary>
public static class ShowScreens
{
    public const string Automatic = "Automatisch";

    /// <summary>The automatic rule in words (the command's description and the choice's hint).</summary>
    public const string AutomaticRule = "Automatisch: Mit zwei Bildschirmen läuft die Präsentation auf dem, der nicht der Hauptbildschirm ist, und die Referentenansicht auf dem anderen; mit einem Bildschirm auf dem Bildschirm des Fensters.";

    /// <summary>Tests and snapshots give their own screens here (the headless platform has one of its own).</summary>
    internal static Func<IReadOnlyList<ShowScreen>>? ForTests { get; set; }

    /// <summary>The connected screens now.</summary>
    public static IReadOnlyList<ShowScreen> All(Window window)
    {
        if (ForTests is { } screens)
        {
            return screens();
        }

        return [.. window.Screens.All.Select((screen, index) => new ShowScreen(index, screen.Bounds, screen.IsPrimary))];
    }

    /// <summary>"Bildschirm 2" for the index 1.</summary>
    public static string NameOf(int index) => $"Bildschirm {(index + 1).ToString(CultureInfo.CurrentCulture)}";

    /// <summary>The chosen screen when it is connected; null for automatic (also when the chosen one is gone).</summary>
    public static ShowScreen? Chosen(IReadOnlyList<ShowScreen> screens) =>
        ShowSettings.AudienceScreen is { } index && index >= 0 && index < screens.Count ? screens[index] : null;

    /// <summary>The screen the window is on (by its bounds), else the main screen, else the first.</summary>
    public static ShowScreen? ScreenOf(Window window, IReadOnlyList<ShowScreen> screens)
    {
        if (screens.Count == 0)
        {
            return null;
        }

        var own = window.Screens.ScreenFromWindow(window);
        return (own is null ? null : screens.FirstOrDefault(screen => screen.Bounds == own.Bounds))
               ?? screens.FirstOrDefault(screen => screen.IsPrimary)
               ?? screens[0];
    }

    /// <summary>The audience's screen and the presenter's: the chosen one or the automatic rule, the presenter on another screen.</summary>
    public static ShowArrangement Arrange(IReadOnlyList<ShowScreen> screens, ShowScreen? windowScreen)
    {
        var audience = Chosen(screens)
                       ?? (screens.Count > 1 ? screens.FirstOrDefault(screen => !screen.IsPrimary) ?? screens[^1] : windowScreen);
        if (screens.Count < 2 || audience is null)
        {
            return new ShowArrangement(audience, null);
        }

        var presenter = windowScreen is not null && windowScreen != audience
            ? windowScreen
            : screens.FirstOrDefault(screen => screen != audience);
        return new ShowArrangement(audience, presenter);
    }

    /// <summary>The value of the choice: "Automatisch" or the chosen screen's label (automatic when it is gone).</summary>
    public static string ChoiceOf(IReadOnlyList<ShowScreen> screens) => Chosen(screens)?.Label ?? Automatic;

    /// <summary>The screen index of a choice ("Bildschirm 2 – 1920 × 1080", "Bildschirm 2" or an index "1"); null for Automatisch.</summary>
    public static int? IndexOf(string? value)
    {
        var text = value?.Trim() ?? "";
        const string prefix = "Bildschirm ";
        if (text.StartsWith(prefix, StringComparison.Ordinal))
        {
            var digits = new string([.. text[prefix.Length..].TakeWhile(char.IsAsciiDigit)]);
            return int.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var number) && number > 0 ? number - 1 : null;
        }

        return int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out var index) ? index : null;
    }
}
