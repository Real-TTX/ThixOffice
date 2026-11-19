using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;
using ThixWrite.Editor;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>The whole shell flow in the real ThixWrite, driven like a user would.</summary>
public sealed class FlowTests
{
    [AvaloniaFact]
    public async Task New_type_save_as_docx_and_save_again()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Brief.docx");
        data.Dialogs.SaveTargets.Enqueue(target);
        var window = await NewWindowAsync();
        Assert.Equal("Dokument1", window.DocumentNameText);
        Assert.Equal("0 Wörter", window.StatusBar.TextOf(WriteEditor.WordsItem));
        Assert.Equal("Seite 1 von 1", window.StatusBar.TextOf(WriteEditor.PageItem));

        await TypeAsync(window, "Sehr geehrte Frau Berger");
        Assert.True(window.ShowsUnsavedMarker);
        Assert.Equal("4 Wörter", window.StatusBar.TextOf(WriteEditor.WordsItem));

        await PressAsync(window, PhysicalKey.S, CommandKey); // no file yet: Speichern unter
        Assert.True(File.Exists(target));
        Assert.Equal("Brief.docx", window.DocumentNameText);
        Assert.False(window.ShowsUnsavedMarker);
        Assert.Equal("Brief.docx – ThixWrite", window.Title);
        Assert.Equal(target, RecentDocuments.List()[0].Document.Path);
        var reopened = DocumentService.Open(target, name => new WordDocument(name));
        Assert.Equal("Sehr geehrte Frau Berger", reopened.Content.GetPlainText().TrimEnd());

        await TypeAsync(window, ",");
        await PressAsync(window, PhysicalKey.S, CommandKey); // into the same file, no dialog
        Assert.Equal("Sehr geehrte Frau Berger,", DocumentService.Open(target, name => new WordDocument(name)).Content.GetPlainText().TrimEnd());
        Assert.Empty(data.Dialogs.SaveTargets);
    }

    [AvaloniaFact]
    public async Task Speichern_unter_as_txt_writes_plain_text_and_keeps_that_type()
    {
        using var data = new TestData();
        var target = Path.Combine(data.DocumentsFolder, "Notiz.txt");
        var window = await NewWindowAsync();
        await TypeAsync(window, "Erste Zeile\nZweite Zeile");

        Assert.True(await window.SaveToAsync(target));
        Assert.Equal("Erste Zeile\nZweite Zeile", File.ReadAllText(target).Replace("\r\n", "\n").TrimEnd()); // CRLF, like Notepad
        Assert.Equal(WordFileFormat.PlainText, EditorOf(window).Document.SaveFormat);

        await TypeAsync(window, "!");
        await PressAsync(window, PhysicalKey.S, CommandKey);
        Assert.EndsWith("Zweite Zeile!", File.ReadAllText(target).TrimEnd());
    }

    [AvaloniaFact]
    public async Task Open_goes_into_an_untouched_new_window_and_an_open_file_comes_to_the_front()
    {
        using var data = new TestData();
        var path = data.File("Notizen.txt", "Einkaufen: Brot");
        var window = await NewWindowAsync();
        data.Dialogs.OpenAnswers.Enqueue([path]);

        await window.OpenWithDialogAsync(); // "Öffnen" › "Durchsuchen"
        await SettleAsync();

        Assert.Single(TestData.App.DocumentWindows);
        Assert.Equal("Notizen.txt", window.DocumentNameText);
        Assert.Equal("Einkaufen: Brot", TextOf(window));
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
        await SettleAsync();
        var page = Assert.IsType<OpenPage>(window.Backstage.CurrentPage);
        Assert.Equal(["Zweites.txt", "Erstes.txt"], page.Rows.Select(row => row.FileName));

        page.Select(first);
        page.FindControl<Button>("PinButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();
        Assert.Equal(["Erstes.txt", "Zweites.txt"], page.Rows.Select(row => row.FileName));
        Assert.True(page.Rows[0].IsPinned);

        page.Select(second);
        page.FindControl<Button>("RemoveButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();
        Assert.Equal(["Erstes.txt"], page.Rows.Select(row => row.FileName));
        Assert.True(File.Exists(second));

        page.Select(first);
        page.FindControl<Button>("OpenButton")!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();
        Assert.Equal("Erstes.txt", window.DocumentNameText);
        Assert.False(window.Backstage.IsOpen);
    }

    [AvaloniaFact]
    public async Task Closing_with_changes_asks_and_Speichern_saves_first()
    {
        using var data = new TestData();
        var path = data.File("Bericht.txt", "Alt");
        var window = await TestData.App.OpenFileAsync(path);
        await SettleAsync();
        EditorOf(window!).FocusDocument();
        await SettleAsync();
        await PressAsync(window!, PhysicalKey.End, CommandKey);
        await TypeAsync(window!, " und neu");

        window!.Close();
        await SettleAsync();
        var dialog = window.OwnedWindows.OfType<QuestionDialog>().Single();
        Assert.Contains("„Bericht.txt“", dialog.Message);
        dialog.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Speichern
        await SettleAsync();

        Assert.Equal("Alt und neu", File.ReadAllText(path).TrimEnd());
        Assert.Empty(TestData.App.DocumentWindows);
    }

    [AvaloniaFact]
    public async Task Closing_without_saving_keeps_the_file_and_leaves_no_recovery()
    {
        using var data = new TestData();
        var path = data.File("Bericht.txt", "Alt");
        var window = await TestData.App.OpenFileAsync(path);
        await SettleAsync();
        EditorOf(window!).Session.InsertText("Verworfen");
        await SettleAsync();
        window!.WriteRecovery();
        Assert.Single(DocumentService.RecoveredDocuments());

        window.Close();
        await SettleAsync();
        window.OwnedWindows.OfType<QuestionDialog>().Single().Buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Nicht speichern
        await SettleAsync();

        Assert.Equal("Alt", File.ReadAllText(path));
        Assert.Empty(TestData.App.DocumentWindows);
        Assert.Empty(DocumentService.RecoveredDocuments());
    }

    [AvaloniaFact]
    public async Task After_a_crash_the_changes_come_back_in_a_window_of_their_own()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Fast verloren");
        await PressAsync(window, PhysicalKey.A, CommandKey);
        await PressAsync(window, PhysicalKey.F, CommandKey | RawInputModifiers.Shift); // fett
        window.CloseKeepingRecovery(); // what the setup does before an update, what a crash leaves
        await SettleAsync();

        Assert.Equal(1, TestData.App.RecoverDocuments());
        var recovered = TestData.App.DocumentWindows.Single();
        await SettleAsync();
        Assert.Equal("Fast verloren", TextOf(recovered));
        Assert.True(EditorOf(recovered).Session.GetFormatting().Bold.Is(true) || EditorOf(recovered).Document.Content.Blocks[0] is ThixWrite.Core.Model.Paragraph { Inlines: [ThixWrite.Core.Model.TextRun { Properties.Bold: true }] });
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
        await SettleAsync();

        Assert.Equal("Weitergereicht.txt", TestData.App.DocumentWindows.Single().DocumentNameText);
        Assert.Equal([path], OfficeApplication.DocumentFilesIn([path, "--update", "nicht-da.txt"]));

        // Without files: a window comes to the front, none is added.
        TestData.App.HandleActivation([]);
        await SettleAsync();
        Assert.Single(TestData.App.DocumentWindows);
    }

    [AvaloniaFact]
    public async Task A_file_ThixWrite_cannot_open_is_refused_with_a_message()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var odt = data.File("Vertrag.odt", "PK");

        var task = TestData.App.OpenFileAsync(odt, window);
        await SettleAsync();
        var message = window.OwnedWindows.OfType<QuestionDialog>().Single();
        Assert.Contains("(noch) nicht öffnen", message.Message);
        message.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await SettleAsync();
        Assert.Null(await task);
        Assert.Equal("Dokument1", window.DocumentNameText);
    }

    [AvaloniaFact]
    public async Task Datum_und_Uhrzeit_is_found_by_the_search_and_inserted_at_the_caret()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeAsync(window, "Heute: ");

        window.Search.Box.Text = "datum";
        await SettleAsync();
        Assert.Equal(WriteCommandIds.InsertDateTime, window.Search.Results[0].Id);
        await window.Search.RunSelectedAsync();
        await SettleAsync();

        Assert.Matches(@"^Heute: \d{2}\.\d{2}\.\d{4} \d{2}:\d{2}$", TextOf(window));
        EditorOf(window).FocusDocument();
        await SettleAsync();
        await PressAsync(window, PhysicalKey.Z, CommandKey);
        Assert.Equal("Heute: ", TextOf(window));
    }

    [AvaloniaFact]
    public async Task The_ribbon_is_filled_like_Word_s_and_leaves_out_only_what_comes_later()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        await SettleAsync();

        Assert.Equal(["Start", "Einfügen", "Layout", "Referenzen", "Überprüfen", "Ansicht"], window.RibbonDefinition.VisibleTabs.Select(tab => tab.Label));
        Assert.Equal(["Tabellenentwurf", "Layout", "Bildformat", "Kopf- und Fußzeile"], window.RibbonDefinition.Tabs.Where(tab => tab.Context is not null).Select(tab => tab.Label));
        Assert.Equal(["Rückgängig", "Zwischenablage", "Schriftart", "Absatz", "Formatvorlagen", "Bearbeiten"], window.RibbonDefinition.Tabs[0].Groups.Select(group => group.Label));
        Assert.Equal(["Tabellen", "Illustrationen", "Seiten", "Kopf- und Fußzeile", "Links", "Text", "Symbole"], window.RibbonDefinition.Tabs[1].Groups.Select(group => group.Label));
        Assert.Equal(["Seite einrichten", "Absatz"], window.RibbonDefinition.Tabs[2].Groups.Select(group => group.Label));
        Assert.Equal(["Inhaltsverzeichnis", "Fußnoten"], window.RibbonDefinition.Tabs[3].Groups.Select(group => group.Label));
        Assert.Equal(["Kommentare"], window.RibbonDefinition.Tabs[4].Groups.Select(group => group.Label));
        Assert.Equal(["Anzeigen", "Zoom", "Menüband"], window.RibbonDefinition.Tabs[5].Groups.Select(group => group.Label));
        Assert.Empty(window.RibbonDefinition.Problems());
        Assert.All(window.RibbonDefinition.AllItems().Where(item => item is not (MatDesktop.MatRibbonStack or MatDesktop.MatRibbonRow or MatDesktop.MatRibbonSeparator or MatDesktop.MatRibbonCustom)),
            item => Assert.NotNull(window.Commands.Find(item.Id)));

        // Every item of the ribbon has its command now.
        Assert.Empty(window.RibbonBinding.MissingCommandIds);

        Assert.All(window.Commands.All.Where(command => command.CurrentShortcut is not null), command => Assert.NotNull(Ui.Gesture(command.CurrentShortcut)));
        Assert.Equal(window.Commands.All.Count, window.Commands.All.Select(command => command.Id).Distinct().Count());
        var shortcuts = window.Commands.All.Where(command => command.CurrentShortcut is not null).GroupBy(command => command.CurrentShortcut).Where(group => group.Count() > 1);
        Assert.Empty(shortcuts.Select(group => $"{group.Key}: {string.Join(", ", group.Select(command => command.Id))}"));
        Assert.Contains(CommonCommandIds.Save, window.Commands.All.Select(command => command.Id));
    }

    [AvaloniaFact]
    public async Task The_zoom_of_the_status_bar_scales_the_pages_and_stays_when_a_file_comes_in()
    {
        using var data = new TestData();
        var path = data.File("Notizen.txt", "Text");
        var window = TestData.App.NewDocumentWindow();
        await SettleAsync();

        window.StatusBar.View.Zoom = 150;
        await SettleAsync();
        Assert.Equal(150, window.Editor.Zoom);
        Assert.Equal(1.5 * 96 / 72, EditorOf(window).Surface.Scale, 6);

        await TestData.App.OpenFileAsync(path, window); // into the untouched window: a new editor, the same zoom
        await SettleAsync();
        Assert.Equal(150, window.Editor.Zoom);
        Assert.Equal(1.5 * 96 / 72, EditorOf(window).Surface.Scale, 6);
        Assert.Equal(150, window.StatusBar.View.Zoom);
    }
}
