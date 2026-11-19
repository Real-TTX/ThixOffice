using Avalonia;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixShow.Core.Model;
using ThixShow.Editor;
using ThixShow.Pages;
using ThixShow.Show;
using static ThixShow.Tests.TestData;

namespace ThixShow.Tests;

/// <summary>The thumbnails, the sorter, the show and the presenter view, print layouts, find – through real input.</summary>
public sealed class SlidesTests
{
    [AvaloniaFact]
    public async Task Thumbnails_add_select_reorder_and_delete_slides()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        var editor = EditorOf(window);
        var session = editor.Session;

        await PressAsync(window, PhysicalKey.M, CommandKey);
        await PressAsync(window, PhysicalKey.M, CommandKey);
        Assert.Equal(3, session.Presentation.SlideCount);
        Assert.Equal(2, editor.CurrentIndex);
        Assert.Equal("Folie 3 von 3", window.StatusBar.TextOf(ShowEditor.SlideItem));

        // A click on the first thumbnail shows it; the keyboard is then in the pane: ↓ goes on.
        var first = editor.Thumbnails.RectOf(0)!.Value;
        await ClickAsync(window, editor.Thumbnails.TranslatePoint(first.Center, window)!.Value);
        Assert.Equal(0, editor.CurrentIndex);
        await PressAsync(window, PhysicalKey.ArrowDown);
        Assert.Equal(1, editor.CurrentIndex);

        // Dragging the first slide below the last moves it there.
        var ids = session.Presentation.Slides.Select(slide => slide.Id).ToList();
        var last = editor.Thumbnails.RectOf(2)!.Value;
        await DragAsync(window, editor.Thumbnails.TranslatePoint(first.Center, window)!.Value, editor.Thumbnails.TranslatePoint(last.BottomLeft + new Avalonia.Vector(20, 2), window)!.Value);
        Assert.Equal([ids[1], ids[2], ids[0]], session.Presentation.Slides.Select(slide => slide.Id).ToList());

        // Entf in the pane deletes the current slide.
        await PressAsync(window, PhysicalKey.Delete);
        Assert.Equal(2, session.Presentation.SlideCount);
    }

    [AvaloniaFact]
    public async Task The_sorter_shows_every_slide_and_hides_one()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);
        await window.Commands[ShowCommandIds.ViewSorter].ExecuteAsync();
        await SettleAsync();
        Assert.Equal(ShowView.Sorter, editor.CurrentView);
        Assert.True(editor.Sorter.IsEffectivelyVisible);
        Assert.Equal(6, editor.Sorter.Entries.Count(entry => entry.SlideIndex >= 0));

        await PressAsync(window, PhysicalKey.ArrowRight);
        Assert.Equal(1, editor.CurrentIndex);
        await window.Commands[ShowCommandIds.HideSlide].ExecuteAsync();
        Assert.True(editor.Session.Presentation.Slides[1].Hidden);
        await window.Commands[ShowCommandIds.ViewNormal].ExecuteAsync();
        Assert.Equal(ShowView.Normal, editor.CurrentView);
    }

    [AvaloniaFact]
    public async Task The_show_runs_with_powerpoints_keys_and_idles_between_clicks()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);
        await window.Commands[ShowCommandIds.StartShow].ExecuteAsync();
        await SettleAsync();
        var show = SlideShowWindow.Current!;
        var controller = show.Controller;
        Assert.Equal(0, controller.Show.SlideIndex);

        show.KeyPressQwerty(PhysicalKey.Space, RawInputModifiers.None);
        await SettleAsync();
        Assert.Equal(1, controller.Show.SlideIndex);

        // The fade runs at 60 frames per second, then the show idles: no timer until the next click.
        Assert.True(controller.IsTicking);
        for (var wait = 0; wait < 100 && controller.IsTicking; wait++)
        {
            await Task.Delay(30);
            await SettleAsync();
        }

        Assert.False(controller.IsTicking);
        // Slide 2 has a click step: the next key builds it, the slide stays.
        show.KeyPressQwerty(PhysicalKey.ArrowRight, RawInputModifiers.None);
        await SettleAsync();
        Assert.Equal(1, controller.Show.SlideIndex);
        Assert.Equal(1, controller.Show.StepsStarted);

        show.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);
        await SettleAsync();
        Assert.Equal(BlankScreen.Black, controller.Blank);
        show.KeyPressQwerty(PhysicalKey.B, RawInputModifiers.None);

        // "5" + Eingabe goes to slide 5.
        show.KeyPressQwerty(PhysicalKey.Digit5, RawInputModifiers.None);
        show.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None);
        await SettleAsync();
        Assert.Equal(4, controller.Show.SlideIndex);

        show.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        await SettleAsync();
        Assert.Null(SlideShowWindow.Current);
        Assert.Equal(4, editor.CurrentIndex);
    }

    [AvaloniaFact]
    public async Task The_presenter_view_shows_position_next_step_and_notes()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);
        var controller = new ShowController(editor.Session.Presentation, new Core.Show.SlideShowOptions());
        controller.Start();
        var presenter = new PresenterWindow(controller, () => new DateTime(2026, 11, 19, 9, 5, 0));
        presenter.Show();
        await SettleAsync();

        Assert.Equal("Folie 1 von 6", presenter.PositionText.Text);
        Assert.Equal("09:05", presenter.ClockText.Text);
        Assert.Contains("Begrüßung", presenter.NotesText.Text);
        Assert.Equal("Nächste Folie", presenter.NextLabel.Text);

        controller.GoTo(1);
        await SettleAsync();
        Assert.Equal("Folie 2 von 6", presenter.PositionText.Text);
        Assert.Equal("Nächster Schritt", presenter.NextLabel.Text);
        controller.End();
        presenter.Close();
    }

    [AvaloniaFact]
    public async Task Print_layouts_give_slides_notes_pages_and_handouts()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);

        Assert.Equal(6, ShowPages.DisplayLists(editor, 0).Count);
        Assert.Equal(6, ShowPages.DisplayLists(editor, 1).Count);
        Assert.Single(ShowPages.DisplayLists(editor, 6)); // 6 per page
        Assert.Equal(2, ShowPages.DisplayLists(editor, 5).Count); // 4 per page

        window.ShowBackstage(ShowPrintPage.PageId);
        await SettleAsync();
        var page = Assert.IsType<ShowPrintPage>(window.Backstage.CurrentPage);
        page.ScopeChoice.SelectedIndex = 6;
        page.VariantChoice.SelectedIndex = 1;
        await SettleAsync();
        Assert.Equal("Seite 1 von 1", page.PageLabel);
    }

    [AvaloniaFact]
    public async Task Find_goes_through_the_slides_and_replace_all_changes_every_one()
    {
        using var data = new TestData();
        var window = await SampleWindowAsync();
        var editor = EditorOf(window);

        await PressAsync(window, PhysicalKey.H, CommandKey);
        Assert.True(editor.FindBar.IsVisible);
        editor.FindBar.SearchBox.Text = "Ziele";
        await SettleAsync();
        Assert.Equal("2 Treffer", editor.FindBar.CountText);
        Assert.True(editor.FindBar.FindNext());
        Assert.Equal(1, editor.CurrentIndex);
        editor.FindBar.ReplacementBox.Text = "Reiseziele";
        Assert.Equal(2, editor.FindBar.ReplaceAll());
        await SettleAsync();
        Assert.Equal("Neue Reiseziele im Norden", SlideTitles.TitleOf(editor.Session.Presentation, editor.Session.Presentation.Slides[3]));
    }
}
