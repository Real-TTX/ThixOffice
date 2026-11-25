using Avalonia.Controls;
using Avalonia.Input;
using ThixShow.Core.Model;

namespace ThixShow.Show;

/// <summary>
/// PowerPoint's keys and mouse in a slide show (the full-screen window, the presenter view and the reading view share them):
/// Weiter – Leertaste, →, ↓, Bild ab, N, Eingabe, a click, the wheel down; Zurück – Rücktaste, ←, ↑, Bild auf, P, the wheel up;
/// Pos1 / Ende – first / last slide; a number + Eingabe – that slide; B or . – black screen, W or , – white screen; Esc or
/// Strg+Pause or "-" – end. The right button opens the show's menu.
/// </summary>
internal static class ShowNavigation
{
    /// <summary>Handles a key; true when it was one of the show's.</summary>
    public static bool HandleKey(ShowController controller, Key key, KeyModifiers modifiers)
    {
        if (key is >= Key.D0 and <= Key.D9 && modifiers == KeyModifiers.None)
        {
            controller.TypeDigit((char)('0' + (key - Key.D0)));
            return true;
        }

        if (key is >= Key.NumPad0 and <= Key.NumPad9)
        {
            controller.TypeDigit((char)('0' + (key - Key.NumPad0)));
            return true;
        }

        switch (key)
        {
            case Key.Enter or Key.Return:
                if (!controller.CommitTyped())
                {
                    controller.Next();
                }

                return true;
            case Key.Space or Key.Right or Key.Down or Key.PageDown or Key.N:
                controller.Next();
                return true;
            case Key.Back or Key.Left or Key.Up or Key.PageUp or Key.P:
                controller.Previous();
                return true;
            case Key.Home:
                controller.GoTo(0);
                return true;
            case Key.End:
                controller.GoTo(controller.Show.Shown.Count - 1);
                return true;
            case Key.B or Key.OemPeriod:
                controller.ToggleBlank(BlankScreen.Black);
                return true;
            case Key.W or Key.OemComma:
                controller.ToggleBlank(BlankScreen.White);
                return true;
            case Key.Escape or Key.OemMinus or Key.Subtract:
                controller.End();
                return true;
            default:
                return false;
        }
    }

    /// <summary>The wheel: down = next, up = previous.</summary>
    public static void HandleWheel(ShowController controller, PointerWheelEventArgs e)
    {
        if (e.Delta.Y < 0)
        {
            controller.Next();
        }
        else if (e.Delta.Y > 0)
        {
            controller.Previous();
        }

        e.Handled = true;
    }

    /// <summary>
    /// The show's menu (the right button, the control bar's "…"): Weiter, Zurück, Gehe zu Folie, Alle Folien, Bildschirm, Referentenansicht,
    /// Anzeige tauschen, Präsentation beenden – the entries whose action is given.
    /// </summary>
    public static ContextMenu Menu(ShowController controller, ShowMenuActions actions)
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, Action action)
        {
            var item = new MenuItem { Header = header };
            item.Click += (_, _) => action();
            return item;
        }

        menu.Items.Add(Item("Weiter", controller.Next));
        menu.Items.Add(Item("Zurück", controller.Previous));
        var goTo = new MenuItem { Header = "Gehe zu Folie" };
        foreach (var index in controller.Show.Shown)
        {
            var slide = controller.Presentation.Slides[index];
            var title = SlideTitles.TitleOf(controller.Presentation, slide);
            var position = controller.Show.Shown.ToList().IndexOf(index);
            goTo.Items.Add(Item($"{index + 1} {title}", () => controller.GoTo(position)));
        }

        menu.Items.Add(goTo);
        if (actions.AllSlides is { } allSlides)
        {
            menu.Items.Add(Item("Alle Folien", allSlides));
        }

        var screen = new MenuItem { Header = "Bildschirm" };
        screen.Items.Add(Item("Schwarzer Bildschirm", () => controller.ToggleBlank(BlankScreen.Black)));
        screen.Items.Add(Item("Weißer Bildschirm", () => controller.ToggleBlank(BlankScreen.White)));
        menu.Items.Add(screen);
        if (actions.TogglePresenter is { } togglePresenter)
        {
            menu.Items.Add(Item(actions.PresenterShown ? "Referentenansicht ausblenden" : "Referentenansicht anzeigen", togglePresenter));
        }

        if (actions.SwapDisplays is { } swap)
        {
            menu.Items.Add(Item("Anzeige tauschen", swap));
        }

        menu.Items.Add(new Separator());
        menu.Items.Add(Item("Präsentation beenden", controller.End));
        return menu;
    }
}

/// <summary>What the show's menu offers besides navigation (null: the entry is left out).</summary>
internal sealed record ShowMenuActions(Action? TogglePresenter = null, bool PresenterShown = false, Action? SwapDisplays = null, Action? AllSlides = null);
