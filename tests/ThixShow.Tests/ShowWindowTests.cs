using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.VisualTree;
using ThixOffice.Desktop.Controls;
using ThixShow.Editor;
using ThixShow.Show;
using static ThixShow.Tests.TestData;

namespace ThixShow.Tests;

/// <summary>The show window's control bar, the screens (the choice, the automatic rule, Anzeige tauschen) and Alt+F5 – through real input.</summary>
public sealed class ShowWindowTests
{
    private static readonly ShowScreen Notebook = new(0, new PixelRect(0, 0, 2560, 1440), IsPrimary: true);
    private static readonly ShowScreen Projector = new(1, new PixelRect(2560, 0, 1920, 1080), IsPrimary: false);

    [AvaloniaFact]
    public async Task The_control_bar_comes_with_the_mouse_and_goes_when_it_rests()
    {
        using var data = new TestData();
        SlideShowWindow.IdleDelay = TimeSpan.FromMilliseconds(400);
        var window = await SampleWindowAsync();
        await window.Commands[ShowCommandIds.StartShow].ExecuteAsync();
        await SettleAsync();
        var show = SlideShowWindow.Current!;
        Assert.False(show.ControlsShown);

        // The first move only tells where the mouse is (a window appearing under a resting mouse gets one); the next shows the bar.
        show.MouseMove(new Point(300, 300));
        show.MouseMove(new Point(320, 310));
        Assert.True(show.ControlsShown);
        Assert.True(show.ControlBar.IsHitTestVisible);
        Assert.False(show.IsCursorHidden);

        // A click on the bar's Weiter goes on and is not a click on the slide (which would go on a second time).
        var next = show.ControlBar.GetVisualDescendants().OfType<Button>().First(button => Avalonia.Automation.AutomationProperties.GetName(button) == "Weiter");
        await ClickOnAsync(show, next);
        Assert.Equal(1, show.Controller.Show.SlideIndex);

        // The bar is moved away from, then the mouse rests: bar and cursor go.
        show.MouseMove(new Point(700, 200));
        await WaitForAsync(() => !show.ControlsShown);
        Assert.False(show.ControlsShown, $"{show.Bounds} {show.ClientSize} {show.WindowState} {show.Surface.Bounds}");
        Assert.False(show.ControlBar.IsHitTestVisible);
        Assert.True(show.IsCursorHidden);

        show.MouseMove(new Point(720, 260));
        Assert.True(show.ControlsShown);
        Assert.False(show.IsCursorHidden);

        // Alle Folien from the bar, a tile goes there; the keys stay the show's.
        await ClickOnAsync(show, show.AllSlidesButton);
        Assert.True(show.Navigator.IsOpen);
        Assert.Equal(6, show.Navigator.Tiles.Count);
        show.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await SettleAsync();
        Assert.False(show.Navigator.IsOpen);
        Assert.NotNull(SlideShowWindow.Current); // Esc closed the slides, not the show

        await ClickOnAsync(show, show.AllSlidesButton);
        await ClickOnAsync(show, show.Navigator.Tiles[4]);
        Assert.False(show.Navigator.IsOpen);
        Assert.Equal(4, show.Controller.Show.SlideIndex);

        show.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await SettleAsync();
        Assert.Null(SlideShowWindow.Current);
    }

    [AvaloniaFact]
    public async Task With_two_screens_the_show_goes_to_the_projector_and_anzeige_tauschen_trades_them()
    {
        using var data = new TestData();
        ShowScreens.ForTests = () => [Notebook, Projector];
        var window = await SampleWindowAsync();
        await window.Commands[ShowCommandIds.StartShow].ExecuteAsync();
        await SettleAsync();
        var show = SlideShowWindow.Current!;
        var presenter = show.PresenterView!;
        Assert.Equal(Projector, show.AudienceScreen);
        Assert.Equal(Notebook, show.PresenterScreen);
        Assert.Equal(Projector.Bounds.Position, show.Position);
        Assert.True(presenter.SwapButton.IsVisible);

        await ClickOnAsync(presenter, presenter.SwapButton);
        Assert.Equal(Notebook, show.AudienceScreen);
        Assert.Equal(Projector, show.PresenterScreen);
        Assert.Equal(Notebook.Bounds.Position, show.Position);
        Assert.Equal(Projector.Bounds.Position, presenter.Position);
        Assert.Same(presenter, show.PresenterView);

        // The right button's menu offers it too, and trades back.
        var surfaceCentre = show.Surface.TranslatePoint(new Point(200, 200), show)!.Value;
        show.MouseDown(surfaceCentre, MouseButton.Right);
        show.MouseUp(surfaceCentre, MouseButton.Right);
        await SettleAsync();
        var swap = show.Surface.ContextMenu!.Items.OfType<MenuItem>().Single(item => (string?)item.Header == "Anzeige tauschen");
        swap.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(MenuItem.ClickEvent));
        await SettleAsync();
        Assert.Equal(Projector, show.AudienceScreen);
        show.Surface.ContextMenu.Close();

        // The presenter view's keys drive the show; Esc there ends everything.
        presenter.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        await SettleAsync();
        Assert.Equal(1, show.Controller.Show.StepsStarted + show.Controller.Show.SlideIndex);
        presenter.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await SettleAsync();
        Assert.Null(SlideShowWindow.Current);
    }

    [AvaloniaFact]
    public async Task Alt_F5_tries_the_presenter_view_in_a_window_on_one_screen()
    {
        using var data = new TestData();
        ShowScreens.ForTests = () => [Notebook];
        var window = await SampleWindowAsync();

        if (OperatingSystem.IsMacOS())
        {
            await PressAsync(window, PhysicalKey.Enter, RawInputModifiers.Meta | RawInputModifiers.Alt);
        }
        else
        {
            await PressAsync(window, PhysicalKey.F5, RawInputModifiers.Alt);
        }

        var show = SlideShowWindow.Current!;
        Assert.NotNull(show);
        Assert.True(show.IsTestMode);
        Assert.False(show.IsVisible); // the audience's window waits
        var presenter = show.PresenterView!;
        Assert.True(presenter.IsVisible);
        Assert.NotEqual(WindowState.FullScreen, presenter.WindowState);
        Assert.True(presenter.SwapButton.IsVisible);
        Assert.Equal("Folie 1 von 6", presenter.PositionText.Text);

        // Anzeige tauschen shows the show itself on this screen, and back again through its menu.
        await ClickOnAsync(presenter, presenter.SwapButton);
        Assert.True(show.IsVisible);
        Assert.False(presenter.IsVisible);
        show.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        await SettleAsync();
        Assert.Equal(1, show.Controller.Show.SlideIndex);

        show.SwapDisplays();
        await SettleAsync();
        Assert.True(presenter.IsVisible);
        Assert.False(show.IsVisible);
        Assert.Equal("Folie 2 von 6", presenter.PositionText.Text);

        // Closing the presenter view ends the show (there is no other view in the test).
        presenter.Close();
        await SettleAsync();
        Assert.Null(SlideShowWindow.Current);
    }

    [AvaloniaFact]
    public async Task The_screen_list_is_read_when_it_opens_and_names_every_connected_screen()
    {
        using var data = new TestData();
        ShowScreens.ForTests = () => [Notebook, Projector];
        var window = await SampleWindowAsync();
        window.Ribbon.SelectTab("slideShow");
        await SettleAsync();
        var button = window.GetVisualDescendants().OfType<RibbonFlyoutButton>().Single(item => item.Label.StartsWith("Bildschirm", StringComparison.Ordinal));
        Assert.Equal("Bildschirm: Automatisch", button.Label);

        await ClickOnAsync(window, button);
        Assert.True(button.IsOpen);
        Assert.Equal(["Automatisch", "Bildschirm 1 – 2560 × 1440 (Hauptbildschirm)", "Bildschirm 2 – 1920 × 1080"], EntriesOf(button));

        var entry = button.FlyoutContent!.GetLogicalButtons().Single(item => Avalonia.Automation.AutomationProperties.GetName(item) == "Bildschirm 1 – 2560 × 1440 (Hauptbildschirm)");
        entry.RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();
        Assert.Equal(0, ShowSettings.AudienceScreen);
        Assert.Equal("Bildschirm: 1", button.Label);
        Assert.Equal("Bildschirm 1 – 2560 × 1440 (Hauptbildschirm)", ((ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[ShowCommandIds.ChooseScreen]).Value);

        // A third screen is connected: the next opening lists it.
        ShowScreens.ForTests = () => [Notebook, Projector, new ShowScreen(2, new PixelRect(4480, 0, 3840, 2160), false)];
        button.Open();
        await SettleAsync();
        Assert.Equal(4, EntriesOf(button).Count);
        Assert.Contains("Bildschirm 3 – 3840 × 2160", EntriesOf(button));
    }

    [AvaloniaFact]
    public async Task A_remembered_screen_that_is_gone_counts_as_automatic()
    {
        using var data = new TestData();
        ShowScreens.ForTests = () => [Notebook, Projector, new ShowScreen(2, new PixelRect(4480, 0, 1920, 1080), false)];
        var window = await SampleWindowAsync();
        var choice = (ThixOffice.Core.Commands.OfficeValueCommand)window.Commands[ShowCommandIds.ChooseScreen];
        await choice.ApplyAsync("Bildschirm 3 – 1920 × 1080");
        Assert.Equal(2, ShowSettings.AudienceScreen);
        Assert.Equal("Bildschirm 3 – 1920 × 1080", choice.Value);

        // The third screen is unplugged: the choice reads Automatisch, and the show goes where the automatic rule says.
        ShowScreens.ForTests = () => [Notebook, Projector];
        Assert.Equal("Automatisch", choice.Value);
        Assert.Null(ShowScreens.Chosen(ShowScreens.All(window)));
        await window.Commands[ShowCommandIds.StartShow].ExecuteAsync();
        await SettleAsync();
        var show = SlideShowWindow.Current!;
        Assert.Equal(Projector, show.AudienceScreen);
        Assert.Equal(2, ShowSettings.AudienceScreen); // still remembered for when it comes back
        show.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await SettleAsync();

        // One screen and no choice: the show fills the window's screen, no presenter view.
        ShowScreens.ForTests = () => [Notebook];
        await window.Commands[ShowCommandIds.StartShow].ExecuteAsync();
        await SettleAsync();
        show = SlideShowWindow.Current!;
        Assert.Equal(Notebook, show.AudienceScreen);
        Assert.Null(show.PresenterView);
        Assert.False(show.CanSwap);
        show.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await SettleAsync();
    }

    [AvaloniaFact]
    public async Task The_presenter_view_remembers_the_notes_size_and_pauses_the_timer()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var controller = new ShowController(EditorOf(window).Session.Presentation, new Core.Show.SlideShowOptions());
        controller.Start();
        var presenter = new PresenterWindow(controller, () => new DateTime(2026, 11, 19, 9, 5, 0));
        presenter.Show();
        await SettleAsync();
        Assert.Equal(ShowSettings.DefaultNotesSize, presenter.NotesText.FontSize);

        await ClickOnAsync(presenter, ButtonNamed(presenter, "Notizen vergrößern"));
        await ClickOnAsync(presenter, ButtonNamed(presenter, "Notizen vergrößern"));
        Assert.Equal(22, presenter.NotesText.FontSize);
        ThixOffice.Core.Business.SettingsService.ResetCache();
        Assert.Equal(22, ShowSettings.NotesSize);

        await ClickOnAsync(presenter, ButtonNamed(presenter, "Zeitgeber anhalten"));
        Assert.True(controller.IsTimerPaused);
        Assert.NotNull(ButtonNamed(presenter, "Zeitgeber fortsetzen"));

        await ClickOnAsync(presenter, ButtonNamed(presenter, "Bildschirm schwarz"));
        Assert.Equal(BlankScreen.Black, controller.Blank);

        await ClickOnAsync(presenter, presenter.AllSlides);
        Assert.True(presenter.Navigator.IsOpen);
        presenter.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        presenter.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await SettleAsync();
        Assert.False(presenter.Navigator.IsOpen);
        Assert.Equal(1, controller.Show.SlideIndex);
        presenter.Close();

        var second = new PresenterWindow(controller);
        Assert.Equal(22, second.NotesText.FontSize);
        controller.End();
    }

    private static List<string> EntriesOf(RibbonFlyoutButton button) =>
        [.. button.FlyoutContent!.GetLogicalButtons().Select(item => Avalonia.Automation.AutomationProperties.GetName(item) ?? "")];

    private static Button ButtonNamed(Visual root, string name) =>
        root.GetVisualDescendants().OfType<Button>().First(button => Avalonia.Automation.AutomationProperties.GetName(button) == name && button.IsEffectivelyVisible);

    /// <summary>A left click in the middle of a control, through the window's input.</summary>
    private static async Task ClickOnAsync(TopLevel top, Control control)
    {
        await SettleAsync();
        var point = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), top)!.Value;
        top.MouseMove(point);
        top.MouseDown(point, MouseButton.Left);
        top.MouseUp(point, MouseButton.Left);
        await SettleAsync();
    }

    private static async Task WaitForAsync(Func<bool> condition)
    {
        for (var wait = 0; wait < 100 && !condition(); wait++)
        {
            await Task.Delay(20);
            await SettleAsync();
        }
    }
}

internal static class LogicalButtons
{
    /// <summary>The buttons of a flyout's content (not in a visual tree of a window while closed).</summary>
    public static IEnumerable<Button> GetLogicalButtons(this Control root) =>
        Avalonia.LogicalTree.LogicalExtensions.GetLogicalDescendants(root).OfType<Button>();
}
