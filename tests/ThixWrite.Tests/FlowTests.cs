using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;
using ThixWrite.Editor;

namespace ThixWrite.Tests;

/// <summary>The whole shell flow in the real ThixWrite, driven like a user would.</summary>
public sealed class FlowTests
{
    private static PlaceholderEditor EditorOf(OfficeWindow window) => (PlaceholderEditor)window.Editor;

    [AvaloniaFact]
    public async Task New_type_save_as_and_save_again()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Brief.txt");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = TestData.App.NewDocumentWindow();
        await TestData.SettleAsync();
        Assert.Equal("Dokument1", window.DocumentNameText);
        Assert.Equal("0 Wörter", window.StatusBar.TextOf(PlaceholderEditor.WordsItem));

        EditorOf(window).TextBox.Focus();
        window.KeyTextInput("Sehr geehrte Frau Berger");
        await TestData.SettleAsync();
        Assert.True(window.ShowsUnsavedMarker);
        Assert.Equal("4 Wörter", window.StatusBar.TextOf(PlaceholderEditor.WordsItem));

        window.KeyPressQwerty(PhysicalKey.S, TestData.CommandKey); // no file yet: Speichern unter
        await TestData.SettleAsync();
        Assert.Equal("Sehr geehrte Frau Berger", File.ReadAllText(target));
        Assert.Equal("Brief.txt", window.DocumentNameText);
        Assert.False(window.ShowsUnsavedMarker);
        Assert.Equal("Brief.txt – ThixWrite", window.Title);
        Assert.Equal(target, RecentDocuments.List()[0].Document.Path);

        window.KeyTextInput(",");
        await TestData.SettleAsync();
        window.KeyPressQwerty(PhysicalKey.S, TestData.CommandKey); // into the same file, no dialog
        await TestData.SettleAsync();
        Assert.Equal("Sehr geehrte Frau Berger,", File.ReadAllText(target));
        Assert.Empty(data.Dialogs.SaveTargets);
    }

    [AvaloniaFact]
    public async Task Open_goes_into_an_untouched_new_window_and_an_open_file_comes_to_the_front()
    {
        using var data = new TestData();
        var path = data.File("Notizen.txt", "Einkaufen: Brot");
        var window = TestData.App.NewDocumentWindow();
        data.Dialogs.OpenAnswers.Enqueue([path]);

        await window.OpenWithDialogAsync(); // "Öffnen" › "Durchsuchen"
        await TestData.SettleAsync();

        Assert.Single(TestData.App.DocumentWindows);
        Assert.Equal("Notizen.txt", window.DocumentNameText);
        Assert.Equal("Einkaufen: Brot", EditorOf(window).TextBox.Text);
        Assert.False(window.Backstage.IsOpen);

        // The same file again: the window that has it, no second one.
        Assert.Same(window, await TestData.App.OpenFileAsync(path));
        Assert.Single(TestData.App.DocumentWindows);

        // Another file while this one is used: a window of its own.
        var other = data.File("Liste.txt", "eins");
        var second = await TestData.App.OpenFileAsync(other);
        Assert.NotSame(window, second);
        Assert.Equal(2, TestData.App.DocumentWindows.Count);
    }

    [AvaloniaFact]
    public async Task The_recent_list_opens_pins_and_removes()
    {
        using var data = new TestData();
        var first = data.File("Erstes.txt", "1");
        var second = data.File("Zweites.txt", "2");
        RecentDocuments.Add(first, DateTimeOffset.Now.AddHours(-1));
        RecentDocuments.Add(second);
        var window = TestData.App.NewDocumentWindow();

        window.Backstage.Open(OfficeWindow.OpenPageId);
        await TestData.SettleAsync();
        var page = Assert.IsType<OpenPage>(window.Backstage.CurrentPage);
        Assert.Equal(["Zweites.txt", "Erstes.txt"], page.Rows.Select(row => row.FileName));

        page.Select(first);
        page.FindControl<Button>("PinButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await TestData.SettleAsync();
        Assert.Equal(["Erstes.txt", "Zweites.txt"], page.Rows.Select(row => row.FileName));
        Assert.True(page.Rows[0].IsPinned);

        page.Select(second);
        page.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await TestData.SettleAsync();
        Assert.Equal(["Erstes.txt"], page.Rows.Select(row => row.FileName));
        Assert.True(File.Exists(second));

        page.Select(first);
        page.FindControl<Button>("OpenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await TestData.SettleAsync();
        Assert.Equal("Erstes.txt", window.DocumentNameText);
        Assert.False(window.Backstage.IsOpen);
    }

    [AvaloniaFact]
    public async Task Closing_with_changes_asks_and_Speichern_saves_first()
    {
        using var data = new TestData();
        var path = data.File("Bericht.txt", "Alt");
        var window = await TestData.App.OpenFileAsync(path);
        EditorOf(window!).TextBox.Text = "Neu";
        await TestData.SettleAsync();

        window!.Close();
        await TestData.SettleAsync();
        var dialog = window.OwnedWindows.OfType<QuestionDialog>().Single();
        Assert.Contains("„Bericht.txt“", dialog.Message);
        dialog.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Speichern
        await TestData.SettleAsync();

        Assert.Equal("Neu", File.ReadAllText(path));
        Assert.Empty(TestData.App.DocumentWindows);
    }

    [AvaloniaFact]
    public async Task Closing_without_saving_keeps_the_file_and_leaves_no_recovery()
    {
        using var data = new TestData();
        var path = data.File("Bericht.txt", "Alt");
        var window = await TestData.App.OpenFileAsync(path);
        EditorOf(window!).TextBox.Text = "Verworfen";
        await TestData.SettleAsync();
        window!.WriteRecovery();
        Assert.Single(DocumentService.RecoveredDocuments());

        window.Close();
        await TestData.SettleAsync();
        window.OwnedWindows.OfType<QuestionDialog>().Single().Buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Nicht speichern
        await TestData.SettleAsync();

        Assert.Equal("Alt", File.ReadAllText(path));
        Assert.Empty(TestData.App.DocumentWindows);
        Assert.Empty(DocumentService.RecoveredDocuments());
    }

    [AvaloniaFact]
    public async Task After_a_crash_the_changes_come_back_in_a_window_of_their_own()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        EditorOf(window).TextBox.Text = "Fast verloren";
        await TestData.SettleAsync();
        window.CloseKeepingRecovery(); // what the setup does before an update, what a crash leaves
        await TestData.SettleAsync();

        Assert.Equal(1, TestData.App.RecoverDocuments());
        var recovered = TestData.App.DocumentWindows.Single();
        Assert.Equal("Fast verloren", ((TextDocument)recovered.Document).Text);
        Assert.Equal("Dokument1 (wiederhergestellt)", recovered.DocumentNameText);
        Assert.True(recovered.ShowsUnsavedMarker);
        Assert.True(recovered.MessageBar.IsVisible);
    }

    [AvaloniaFact]
    public async Task A_second_start_hands_its_files_over_and_command_line_arguments_are_filtered()
    {
        using var data = new TestData();
        var path = data.File("Weitergereicht.txt", "aus dem Explorer");
        TestData.App.NewDocumentWindow();

        // What MatSingleInstance's listener gets from a second ThixWrite.exe started with a file.
        TestData.App.HandleActivation(OfficeApplication.DocumentFilesIn(["--quiet", path, Path.Combine(data.Folder, "fehlt.txt")]));
        await TestData.SettleAsync();

        Assert.Equal("Weitergereicht.txt", TestData.App.DocumentWindows.Single().DocumentNameText);
        Assert.Equal([path], OfficeApplication.DocumentFilesIn([path, "--update", "nicht-da.txt"]));

        // Without files: a window comes to the front, none is added.
        TestData.App.HandleActivation([]);
        await TestData.SettleAsync();
        Assert.Single(TestData.App.DocumentWindows);
    }

    [AvaloniaFact]
    public async Task A_file_ThixWrite_cannot_open_is_refused_with_a_message()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var docx = data.File("Vertrag.docx", "PK");

        var task = TestData.App.OpenFileAsync(docx, window);
        await TestData.SettleAsync();
        var message = window.OwnedWindows.OfType<QuestionDialog>().Single();
        Assert.Contains("(noch) nicht öffnen", message.Message);
        message.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await TestData.SettleAsync();
        Assert.Null(await task);
        Assert.Equal("Dokument1", window.DocumentNameText);
    }

    [AvaloniaFact]
    public async Task Datum_und_Uhrzeit_is_found_by_the_search_and_inserted_at_the_caret()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        EditorOf(window).TextBox.Text = "Heute: ";
        EditorOf(window).TextBox.CaretIndex = 7;

        window.Search.Box.Text = "datum";
        await TestData.SettleAsync();
        Assert.Equal(WriteRibbon.InsertDateTime, window.Search.Results[0].Id);
        await window.Search.RunSelectedAsync();
        await TestData.SettleAsync();

        Assert.Matches(@"^Heute: \d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$", EditorOf(window).TextBox.Text);
        window.KeyPressQwerty(PhysicalKey.Z, TestData.CommandKey);
        await TestData.SettleAsync();
        Assert.Equal("Heute: ", EditorOf(window).TextBox.Text);
    }

    [AvaloniaFact]
    public void Every_command_of_the_ribbon_exists_in_a_window()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        Assert.All(WriteRibbon.Layout.CommandIds, id => Assert.NotNull(window.Commands.Find(id)));
        Assert.All(window.Commands.All.Where(command => command.Shortcut is not null), command => Assert.NotNull(Ui.Gesture(command.Shortcut)));
        Assert.Equal(window.Commands.All.Count, window.Commands.All.Select(command => command.Id).Distinct().Count());
        Assert.Contains(CommonCommandIds.Save, window.Commands.All.Select(command => command.Id));
    }
}
