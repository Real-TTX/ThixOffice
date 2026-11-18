using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
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
        Assert.False(window.QuickAccessButtons[1].IsEnabled); // nothing to undo yet
        Assert.IsType<TemporaryCommandBar>(window.Ribbon);
        Assert.IsType<TemporaryStatusBar>(window.StatusBar);
        Assert.IsType<TemporaryBackstage>(window.Backstage);
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

        var backstage = (TemporaryBackstage)window.Backstage;
        backstage.Open(OfficeWindow.NewPageId);
        backstage.Menu.VersionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        TestData.Settle();
        Assert.IsType<AboutPage>(backstage.CurrentPage);
        Assert.Equal(["Zurück", "Neu", "Öffnen", "Speichern", "Speichern unter", "Schließen"], backstage.Menu.Items.Select(item => item.Label));
        Assert.Equal("Einstellungen", backstage.Menu.Settings!.Label);

        window.KeyPressQwerty(PhysicalKey.Escape, RawInputModifiers.None);
        TestData.Settle();
        Assert.False(window.Backstage.IsOpen);
    }

    [AvaloniaFact]
    public void Datei_in_the_ribbon_opens_the_backstage_and_Ctrl_F1_folds_the_ribbon()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var ribbon = (TemporaryCommandBar)window.Ribbon;
        TestData.Settle();
        Assert.Equal(["file.new", "file.save", "test.shout"], ribbon.CommandButtons.Keys.Order());

        window.KeyPressQwerty(PhysicalKey.F1, TestData.CommandKey);
        TestData.Settle();
        Assert.True(window.Ribbon.IsCollapsed);
        Assert.True(SettingsService.Get().IsRibbonCollapsed);
        Assert.True(window.Commands[CommonCommandIds.ToggleRibbon].Checked);

        window.KeyPressQwerty(PhysicalKey.F1, TestData.CommandKey);
        TestData.Settle();
        Assert.False(window.Ribbon.IsCollapsed);

        window.KeyPressQwerty(PhysicalKey.O, TestData.CommandKey);
        TestData.Settle();
        Assert.Equal(OfficeWindow.OpenPageId, window.Backstage.CurrentPageId);
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
    public void The_ribbon_slot_takes_another_implementation_without_changing_the_shell()
    {
        using var data = new TestData();
        var fake = new FakeRibbon();
        OfficeShellParts.CreateRibbon = () => fake;

        var window = TestData.App.NewDocumentWindow();
        TestData.Settle();

        Assert.Same(fake, window.Ribbon);
        Assert.Same(window.Commands, fake.Commands);
        Assert.Equal(TestData.App.Ribbon, fake.Layout);
        fake.InvokeFile();
        Assert.True(window.Backstage.IsOpen);
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

    /// <summary>A ribbon that only records what the shell gives it: the slot is an interface, nothing more.</summary>
    private sealed class FakeRibbon : IOfficeRibbon
    {
        public Control View { get; } = new Border();

        public string? SelectedTabId { get; set; }

        public bool IsCollapsed { get; set; }

        public OfficeCommandRegistry? Commands { get; private set; }

        public RibbonLayout? Layout { get; private set; }

        public event EventHandler? FileTabInvoked;

        public event EventHandler<bool>? CollapsedChanged
        {
            add { }
            remove { }
        }

        public void Bind(RibbonLayout layout, OfficeCommandRegistry commands)
        {
            Layout = layout;
            Commands = commands;
        }

        public void InvokeFile() => FileTabInvoked?.Invoke(this, EventArgs.Empty);
    }
}
