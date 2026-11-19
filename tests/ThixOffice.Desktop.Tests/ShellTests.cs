using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Tests;

/// <summary>The document window of every app: its parts, the search, the backstage, the keyboard, the unsaved prompt, the slots.</summary>
public sealed class ShellTests
{
    [AvaloniaFact]
    public void A_new_window_has_title_bar_quick_access_ribbon_editor_and_status_bar()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();

        Assert.Equal("ThixTest", window.TitleBar.AppName);
        Assert.Equal("Test1", window.DocumentNameText);
        Assert.Equal("Test1 – ThixTest", window.Title);
        Assert.False(window.ShowsUnsavedMarker);
        Assert.Equal(["Speichern", "Rückgängig", "Wiederholen"], window.QuickAccessButtons.Select(button => Avalonia.Automation.AutomationProperties.GetName(button)));
        Assert.Equal(["save", "undo", "redo"], window.QuickAccessButtons.Select(button => ((MatIcon)button.Content!).Icon));
        Assert.Equal(Ui.Tooltip("Rückgängig", "Ctrl+Z"), ToolTip.GetTip(window.QuickAccessButtons[1]));
        Assert.False(window.QuickAccessButtons[1].IsEnabled); // nothing to undo yet
        Assert.True(window.QuickAccessButtons[0].IsEnabled);
        Assert.Contains(window.QuickAccess, window.TitleBar.GetVisualDescendants());
        Assert.Contains(window.Ribbon, window.GetVisualDescendants());
        Assert.Contains(window.StatusBar.View, window.GetVisualDescendants());
        Assert.Equal("0 Zeichen", window.StatusBar.TextOf("chars"));
        Assert.Equal(CommandSearchBox.Placeholder, window.Search.Box.Watermark);
        Assert.Contains(window, TestData.App.DocumentWindows);
    }

    [AvaloniaFact]
    public void Typing_makes_the_document_unsaved_and_the_quick_access_follows()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var editor = (TestEditor)window.Editor;

        editor.Box.Text = "Hallo";
        TestData.Settle();

        Assert.True(window.Document.IsDirty);
        Assert.True(window.ShowsUnsavedMarker);
        Assert.StartsWith("● ", window.Title);
        Assert.True(window.QuickAccessButtons[1].IsEnabled);
        Assert.Equal("5 Zeichen", window.StatusBar.TextOf("chars"));

        window.QuickAccessButtons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Rückgängig
        TestData.Settle();
        Assert.Equal("", editor.Box.Text);
        Assert.True(window.QuickAccessButtons[2].IsEnabled);
    }

    [AvaloniaFact]
    public async Task The_search_finds_commands_of_the_shell_and_of_the_app_and_runs_them()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();

        window.Search.Box.Text = "speich";
        TestData.Settle();
        Assert.Equal([CommonCommandIds.Save, CommonCommandIds.SaveAs], window.Search.Results.Take(2).Select(command => command.Id)); // label matches first, then descriptions
        Assert.True(window.Search.IsOpen);

        window.Search.Box.Text = "optionen";
        TestData.Settle();
        Assert.Equal(CommonCommandIds.Settings, window.Search.Results[0].Id);
        Assert.True(await window.Search.RunSelectedAsync());
        TestData.Settle();
        Assert.Equal(OfficeWindow.SettingsPageId, window.Backstage.CurrentPageId);
        Assert.False(window.Search.IsOpen);
        Assert.Equal("", window.Search.Box.Text);

        window.Backstage.Close();
        ((TestEditor)window.Editor).Box.Text = "leise";
        window.Search.Box.Text = "versalien";
        TestData.Settle();
        Assert.Equal(TestOfficeApp.ShoutCommand, window.Search.Results.Single().Id);
        await window.Search.RunSelectedAsync();
        TestData.Settle();
        Assert.Equal("LEISE", ((TestEditor)window.Editor).Box.Text);
    }

    [AvaloniaFact]
    public void Alt_Q_goes_to_the_search_and_Escape_back_to_the_document()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();

        window.KeyPressQwerty(PhysicalKey.Q, RawInputModifiers.Alt);
        TestData.Settle();
        Assert.True(window.Search.Box.IsFocused);

        window.KeyTextInput("neu");
        TestData.Settle();
        Assert.Equal(CommonCommandIds.New, window.Search.Results[0].Id);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        TestData.Settle();
        Assert.False(window.Search.IsOpen);
        Assert.True(((TestEditor)window.Editor).Box.IsFocused);
    }

    [AvaloniaFact]
    public void Every_backstage_page_opens_and_the_version_leads_to_Info()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();

        var pages = new (string Id, Type Page)[]
        {
            (OfficeWindow.NewPageId, typeof(NewPage)),
            (OfficeWindow.OpenPageId, typeof(OpenPage)),
            (OfficeWindow.SaveAsPageId, typeof(SaveAsPage)),
            (OfficeWindow.SettingsPageId, typeof(SettingsPage)),
            (OfficeWindow.AboutPageId, typeof(AboutPage)),
        };
        foreach (var (id, page) in pages)
        {
            window.Backstage.Open(id);
            TestData.Settle();
            Assert.True(window.Backstage.IsOpen);
            Assert.Equal(id, window.Backstage.CurrentPageId);
            Assert.IsType(page, window.Backstage.CurrentPage);
        }

        var backstage = window.Backstage.View;
        window.Backstage.Open(OfficeWindow.NewPageId);
        backstage.VersionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.IsType<AboutPage>(window.Backstage.CurrentPage);
        Assert.Equal(OfficeWindow.AboutPageId, window.Backstage.CurrentPageId);
        Assert.Equal(["Neu", "Öffnen", "Speichern", "Speichern unter", "Schließen"], backstage.Items.Select(item => item.Label));
        Assert.Equal(["file-new", "folder-open", "save", "save-as", "x"], backstage.Items.Select(item => item.Icon));
        Assert.Equal(["Einstellungen"], backstage.LowerItems.Select(item => item.Label)); // Info is a page without an entry
        Assert.Null(backstage.Current);
        Assert.StartsWith("Version ", backstage.VersionButton.GetVisualDescendants().OfType<TextBlock>().First().Text);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        TestData.Settle();
        Assert.False(window.Backstage.IsOpen);
        Assert.Null(window.Backstage.CurrentPageId);
    }

    [AvaloniaFact]
    public void Datei_opens_the_backstage_below_the_title_bar_and_Escape_closes_it()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();

        window.Ribbon.FileButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.True(window.Backstage.IsOpen);
        Assert.Equal(OfficeWindow.NewPageId, window.Backstage.CurrentPageId);
        Assert.Equal(1, Grid.GetRow(window.Backstage.View)); // the title bar stays above it
        Assert.False(window.Ribbon.HandlesWindowKeys); // Alt is not the hidden ribbon's

        // A command entry runs and closes: Speichern without a file asks where (the fake dialog says: nowhere).
        window.Backstage.View.Items[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.False(window.Backstage.IsOpen);
        Assert.Equal(1, data.Dialogs.SaveAsked);

        window.Backstage.Open(OfficeWindow.OpenPageId);
        TestData.Settle();
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        TestData.Settle();
        Assert.False(window.Backstage.IsOpen);
        Assert.True(window.Ribbon.HandlesWindowKeys);
        Assert.True(((TestEditor)window.Editor).Box.IsFocused);
    }

    [AvaloniaFact]
    public void The_ribbon_shows_the_tabs_and_groups_of_the_app_and_leaves_out_what_has_no_command()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();

        Assert.Equal("Datei", window.Ribbon.FileButton.Content);
        Assert.Equal(["home", "view"], window.RibbonDefinition.Tabs.Select(tab => tab.Id)); // "Später" had only missing commands
        Assert.Equal(["Start", "Ansicht"], window.Ribbon.TabButtons.Values.Select(button => button.Content));
        Assert.Equal(["doc", "size"], window.RibbonDefinition.Tabs[0].Groups.Select(group => group.Id));
        Assert.Equal([CommonCommandIds.New, CommonCommandIds.Save, TestOfficeApp.ShoutCommand], window.RibbonDefinition.Tabs[0].Groups[0].Items.Select(item => item.Id));
        Assert.Equal([TestOfficeApp.MissingCommand, TestOfficeApp.MissingCommand + "2"], window.RibbonBinding.MissingCommandIds);
        Assert.Empty(window.RibbonDefinition.Problems());

        // Label, icon, shortcut and tooltip come from the command.
        var save = window.RibbonDefinition.Get<MatRibbonButton>(CommonCommandIds.Save);
        Assert.Equal("Speichern", save.Label);
        Assert.Equal("save", save.Icon);
        Assert.Equal(Ui.ShortcutText("Ctrl+S"), save.Shortcut);
        Assert.Equal("Speichert das Dokument unter seinem Namen.", save.ToolTip);
        Assert.Equal("file-new", window.RibbonDefinition.Get<MatRibbonButton>(CommonCommandIds.New).Icon);
        Assert.NotNull(window.Ribbon.ViewOf(TestOfficeApp.ShoutCommand));
        Assert.Equal(["9", "10", "11", "12"], window.RibbonDefinition.Get<MatRibbonComboBox>(TestOfficeApp.SizeCommand).Options);
    }

    [AvaloniaFact]
    public void The_state_of_the_commands_shows_in_the_ribbon_and_a_ribbon_button_runs_its_command()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();
        var shout = window.RibbonDefinition.Get<MatRibbonButton>(TestOfficeApp.ShoutCommand);
        Assert.False(shout.IsEnabled); // nothing to shout yet
        Assert.False(window.Ribbon.ViewOf(TestOfficeApp.ShoutCommand)!.IsEnabled);

        ((TestEditor)window.Editor).Box.Text = "leise";
        TestData.Settle();
        Assert.True(shout.IsEnabled);
        Assert.True(window.Ribbon.ViewOf(TestOfficeApp.ShoutCommand)!.IsEnabled);

        ((Button)window.Ribbon.ViewOf(TestOfficeApp.ShoutCommand)!).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.Equal("LEISE", ((TestEditor)window.Editor).Box.Text);
        Assert.True(window.QuickAccessButtons[1].IsEnabled); // Rückgängig: in the ribbon's state as well

        // A toggle shows the checked state of its command.
        var fold = window.RibbonDefinition.Get<MatRibbonToggle>(CommonCommandIds.ToggleRibbon);
        Assert.False(fold.IsChecked);
        window.RibbonDefinition.Invoke(fold);
        TestData.Settle();
        Assert.True(window.Ribbon.IsCollapsed);
        Assert.True(fold.IsChecked);
    }

    [AvaloniaFact]
    public async Task A_value_command_takes_the_value_of_its_combo_box_and_the_search_leads_into_it()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();
        var combo = window.RibbonDefinition.Get<MatRibbonComboBox>(TestOfficeApp.SizeCommand);
        Assert.Equal("11", combo.Text);

        window.RibbonDefinition.Commit(combo, "12");
        TestData.Settle();
        Assert.Equal("12", TestData.App.Size);

        TestData.App.Size = "9"; // the selection moved: the command says so, the combo box follows
        window.Commands.Invalidate();
        TestData.Settle();
        Assert.Equal("9", combo.Text);

        window.Search.Box.Text = "schriftgrad";
        TestData.Settle();
        Assert.Equal(TestOfficeApp.SizeCommand, window.Search.Results[0].Id);
        Assert.True(await window.Search.RunSelectedAsync());
        TestData.Settle();
        var focused = window.FocusManager!.GetFocusedElement() as Visual;
        Assert.NotNull(focused);
        Assert.True(window.Ribbon.ViewOf(TestOfficeApp.SizeCommand) == focused || window.Ribbon.ViewOf(TestOfficeApp.SizeCommand)!.IsVisualAncestorOf(focused!));
    }

    [AvaloniaFact]
    public void Ctrl_F1_folds_the_ribbon_once_and_the_state_is_remembered()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();
        var changes = 0;
        window.Ribbon.CollapsedChanged += (_, _) => changes++;

        window.KeyPressQwerty(OperatingSystem.IsMacOS() ? PhysicalKey.R : PhysicalKey.F1, OperatingSystem.IsMacOS() ? RawInputModifiers.Meta | RawInputModifiers.Alt : RawInputModifiers.Control);
        TestData.Settle();
        Assert.Equal(1, changes); // the window's shortcut, not the ribbon's as well
        Assert.True(window.Ribbon.IsCollapsed);
        Assert.True(SettingsService.Get().IsRibbonCollapsed);
        Assert.True(window.Commands[CommonCommandIds.ToggleRibbon].Checked);

        // The ribbon's own chevron is remembered too, and a new window starts the way the last one was left.
        window.Ribbon.ExpandButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.False(window.Ribbon.IsCollapsed);
        Assert.False(SettingsService.Get().IsRibbonCollapsed);
        window.Ribbon.CollapseButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.True(SettingsService.Get().IsRibbonCollapsed);

        var second = TestData.App.NewDocumentWindow();
        TestData.Settle();
        Assert.True(second.Ribbon.IsCollapsed);
    }

    [AvaloniaFact]
    public async Task The_shortcuts_of_the_commands_work_and_Alt_shows_the_key_tips()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();

        window.KeyPressQwerty(PhysicalKey.O, TestData.CommandKey);
        TestData.Settle();
        Assert.Equal(OfficeWindow.OpenPageId, window.Backstage.CurrentPageId);
        window.Backstage.Close();

        window.KeyPressQwerty(OperatingSystem.IsMacOS() ? PhysicalKey.S : PhysicalKey.F12, OperatingSystem.IsMacOS() ? TestData.CommandKey | RawInputModifiers.Shift : RawInputModifiers.None);
        await Task.Yield();
        TestData.Settle();
        Assert.Equal(1, data.Dialogs.SaveAsked); // Speichern unter

        window.KeyPressQwerty(PhysicalKey.N, TestData.CommandKey);
        TestData.Settle();
        Assert.Equal(2, TestData.App.DocumentWindows.Count);
        var second = TestData.App.DocumentWindows[1];
        second.KeyPressQwerty(PhysicalKey.W, TestData.CommandKey);
        TestData.Settle();
        Assert.Single(TestData.App.DocumentWindows);

        ((TestEditor)window.Editor).Box.Focus();
        window.KeyPressQwerty(PhysicalKey.AltLeft, RawInputModifiers.Alt);
        window.KeyReleaseQwerty(PhysicalKey.AltLeft, RawInputModifiers.None);
        TestData.Settle();
        Assert.Equal(MatRibbonKeyTipMode.Tabs, window.Ribbon.KeyTipMode);
        Assert.Equal("D", window.Ribbon.ShownKeyTips.Single(tip => tip.Value == MatRibbon.FileId).Key);
        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        TestData.Settle();
        Assert.Equal(MatRibbonKeyTipMode.None, window.Ribbon.KeyTipMode);
        Assert.False(window.Backstage.IsOpen);
    }

    [AvaloniaFact]
    public void The_zoom_of_the_status_bar_and_the_editor_follow_each_other()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();
        Assert.Equal(100, window.StatusBar.View.Zoom);
        Assert.Equal(100, window.Editor.Zoom);

        window.StatusBar.View.ZoomInButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.Equal(110, window.Editor.Zoom);

        window.Editor.Zoom = 1000; // held to 10–500 %
        TestData.Settle();
        Assert.Equal(OfficeEditor.MaxZoom, window.Editor.Zoom);
        Assert.Equal(OfficeEditor.MaxZoom, window.StatusBar.View.Zoom);
        Assert.False(window.Commands[CommonCommandIds.ZoomIn].IsEnabled);

        _ = window.Commands.ExecuteAsync(CommonCommandIds.Zoom100);
        TestData.Settle();
        Assert.Equal(100, window.StatusBar.View.Zoom);
        _ = window.Commands.ExecuteAsync(CommonCommandIds.ZoomOut);
        TestData.Settle();
        Assert.Equal(90, window.Editor.Zoom);
    }

    [Fact]
    public void Closing_the_last_window_ends_the_app_on_Windows_but_not_on_macOS()
    {
        Assert.True(OfficeApplication.LastWindowEndsApp(isMacOS: false));
        Assert.False(OfficeApplication.LastWindowEndsApp(isMacOS: true));
    }

    [AvaloniaFact]
    public async Task Closing_asks_about_unsaved_changes_and_Abbrechen_keeps_the_window()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        ((TestEditor)window.Editor).Box.Text = "Wichtig";
        TestData.Settle();

        window.Close();
        TestData.Settle();
        var dialog = window.OwnedWindows.OfType<QuestionDialog>().Single();
        Assert.Equal(SaveChangesDialog.Title, dialog.Title);
        Assert.Contains("„Test1“", dialog.Message);
        Assert.Equal(["Speichern", "Abbrechen", "Nicht speichern"], dialog.Buttons.Select(button => ((Controls.IconLabel)button.Content!).Text));

        dialog.Buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Abbrechen
        TestData.Settle();
        await Task.Yield();
        TestData.Settle();
        Assert.True(window.IsVisible);
        Assert.Contains(window, TestData.App.DocumentWindows);

        window.Close();
        TestData.Settle();
        window.OwnedWindows.OfType<QuestionDialog>().Single().Buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Nicht speichern
        TestData.Settle();
        await Task.Yield();
        TestData.Settle();
        Assert.DoesNotContain(window, TestData.App.DocumentWindows);
    }

    [AvaloniaFact]
    public async Task Saving_from_the_prompt_asks_where_and_then_closes()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Gerettet.tst");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = TestData.App.NewDocumentWindow();
        ((TestEditor)window.Editor).Box.Text = "Nicht verlieren";
        TestData.Settle();

        window.Close();
        TestData.Settle();
        window.OwnedWindows.OfType<QuestionDialog>().Single().Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Speichern
        for (var round = 0; round < 5; round++)
        {
            await Task.Yield();
            TestData.Settle();
        }

        Assert.Equal("Nicht verlieren", File.ReadAllText(target));
        Assert.DoesNotContain(window, TestData.App.DocumentWindows);
        Assert.Equal(target, RecentDocuments.List()[0].Document.Path);
    }

    [AvaloniaFact]
    public void The_settings_save_through_the_business_layer_and_a_lock_shows()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        window.ShowSettings(SettingsPage.SaveTab);
        TestData.Settle();
        var settings = Assert.IsType<SettingsPage>(window.Backstage.CurrentPage);
        Assert.Equal(SettingsPage.SaveTab, settings.CurrentTab);

        data.Policies.Add(AccessPolicy.LockConfiguration);
        window.ShowSettings(SettingsPage.GeneralTab);
        TestData.Settle();
        var locked = Assert.IsType<SettingsPage>(window.Backstage.CurrentPage);
        Assert.False(locked.FindControl<ComboBox>("ThemeChoice")!.IsEnabled);
        Assert.True(locked.FindControl<Controls.InfoBar>("PolicyNote")!.IsVisible);
    }
}
