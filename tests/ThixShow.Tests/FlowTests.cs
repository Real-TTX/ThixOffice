using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Core.Platform;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Documents;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;
using ThixShow.Editor;
using static ThixShow.Tests.TestData;

namespace ThixShow.Tests;

/// <summary>
/// ThixShow's documents through the window flow: recovery after a crash, closing without saving, and presentations in ThixDrive
/// (open from the Drive, save into it, a waiting upload, a version saved in between) against a cloud in memory.
/// </summary>
public sealed class FlowTests
{
    [AvaloniaFact]
    public async Task After_a_crash_the_presentation_comes_back_in_a_window_of_its_own()
    {
        using var data = new TestData();
        var window = await NewWindowAsync();
        await TypeTitleAsync(window, "Fast verloren");
        await window.Commands[ShowCommandIds.NewSlide].ExecuteAsync();
        window.CloseKeepingRecovery(); // what the setup does before an update, what a crash leaves
        await SettleAsync();

        Assert.Equal(1, TestData.App.RecoverDocuments());
        var recovered = TestData.App.DocumentWindows.Single();
        await SettleAsync();
        var presentation = EditorOf(recovered).Session.Presentation;
        Assert.Equal(2, presentation.SlideCount);
        Assert.Equal("Fast verloren", SlideTitles.TitleOf(presentation, presentation.Slides[0]));
        Assert.EndsWith("(wiederhergestellt)", recovered.DocumentNameText, StringComparison.Ordinal);
        Assert.True(recovered.ShowsUnsavedMarker);
        Assert.True(recovered.MessageBar.IsVisible);
    }

    [AvaloniaFact]
    public async Task Closing_without_saving_keeps_the_file_and_leaves_no_recovery()
    {
        using var data = new TestData();
        var path = Path.Combine(data.DocumentsFolder, "Vortrag.pptx");
        DocumentService.SaveAs(new ShowDocument("Vortrag", SampleDeck.Create()), path);
        var before = File.ReadAllBytes(path);
        var window = (await TestData.App.OpenFileAsync(path))!;
        await SettleAsync();
        await TypeTitleAsync(window, " – verworfen");
        window.WriteRecovery();
        Assert.Single(DocumentService.RecoveredDocuments());

        window.Close();
        await SettleAsync();
        window.OwnedWindows.OfType<QuestionDialog>().Single().Buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Nicht speichern
        await SettleAsync();

        Assert.Equal(before, File.ReadAllBytes(path));
        Assert.Empty(TestData.App.DocumentWindows);
        Assert.Empty(DocumentService.RecoveredDocuments());
    }

    [AvaloniaFact]
    public async Task A_presentation_from_ThixDrive_opens_saves_back_and_asks_when_someone_saved_in_between()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync();
        var file = cloud.AddFile(null, "Vortrag.pptx", PptxBytes(SampleDeck.Create()));

        // Öffnen › ThixDrive lists the presentation (only ThixShow's types) and opens it into the untouched window.
        var window = TestData.App.NewDocumentWindow();
        window.Backstage.Open(OfficeWindow.OpenPageId);
        var page = (OpenPage)window.Backstage.CurrentPage!;
        page.ShowPlace(DocumentPlace.Drive);
        await page.Drive!.LoadTask;
        Settle();
        page.Drive.Select("Vortrag.pptx");
        await page.Drive.ActivateSelectedAsync();
        await SettleAsync();

        Assert.Equal(file.Id, window.Document.Drive!.ItemId);
        Assert.Equal(6, EditorOf(window).Session.Presentation.SlideCount);
        Assert.Equal(CloudIndicatorKind.InDrive, window.CloudIndicator.Kind);

        // Strg+S uploads a new version.
        await TypeTitleAsync(window, " 2027");
        Assert.True(await window.SaveAsync());
        Assert.Contains("2027", TitleIn(cloud.ContentOf(file.Id)), StringComparison.Ordinal);

        // Someone else saved meanwhile: the question, the copy first; Überschreiben makes ours the current one.
        cloud.SaveElsewhere(file.Id, PptxBytes(MasterFactory.CreatePresentation()));
        await TypeTitleAsync(window, "!");
        var saving = window.SaveAsync();
        var dialog = await DialogAsync(window);
        Assert.Equal(DriveDialogs.ConflictTitle, dialog.Title);
        Assert.Equal(["Als Kopie speichern", "Später", "Überschreiben"], dialog.Buttons.Select(button => ((IconLabel)button.Content!).Text));
        dialog.Buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.True(await saving);
        Assert.EndsWith("2027!", TitleIn(cloud.ContentOf(file.Id)), StringComparison.Ordinal);
        Assert.Equal(DriveSyncState.Uploaded, window.Document.DriveState);
    }

    [AvaloniaFact]
    public async Task Offline_a_presentation_is_saved_here_and_uploaded_by_a_click_and_Speichern_unter_goes_into_the_Drive()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync();
        var folder = cloud.AddFolder(null, "Vorträge");
        var window = await NewWindowAsync();
        await TypeTitleAsync(window, "Unterwegs");

        // Speichern unter › ThixDrive: the name gets .pptx.
        window.Backstage.Open(OfficeWindow.SaveAsPageId);
        var page = (SaveAsPage)window.Backstage.CurrentPage!;
        page.ShowPlace(DocumentPlace.Drive);
        await page.Drive!.LoadTask;
        Settle();
        await page.Drive.NavigateAsync(folder.Id);
        page.Drive.FileNameField.Text = "Unterwegs";
        await page.Drive.SaveAsync();
        var item = cloud.FindByName(folder.Id, "Unterwegs.pptx")!;
        Assert.Equal("Unterwegs", TitleIn(cloud.ContentOf(item.Id)));

        // Offline: saved on this computer, the title bar says so; back online a click uploads it.
        cloud.IsOffline = true;
        await TypeTitleAsync(window, " im Zug");
        Assert.True(await window.SaveAsync());
        Settle();
        Assert.Equal(CloudIndicatorKind.NotUploaded, window.CloudIndicator.Kind);
        Assert.False(window.Document.IsDirty);

        cloud.IsOffline = false;
        window.CloudIndicator.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => window.Document.DriveState == DriveSyncState.Uploaded, "the retry uploads");
        Assert.Equal("Unterwegs im Zug", TitleIn(cloud.ContentOf(item.Id)));
    }

    /// <summary>Types at the end of the first slide's title (a click into it, then text input).</summary>
    private static async Task TypeTitleAsync(OfficeWindow window, string text)
    {
        var editor = EditorOf(window);
        var session = editor.Session;
        session.GoToSlide(session.Presentation.Slides[0].Id);
        var title = session.CurrentSlide!.Shapes.OfType<AutoShape>().First(shape => shape.Placeholder?.IsTitle == true);
        editor.Canvas.EditTextOf(title.Id);
        editor.Canvas.Focus();
        await TypeAsync(window, text);
        session.EndTextEditing();
        await SettleAsync();
    }

    private static byte[] PptxBytes(Presentation presentation)
    {
        using var stream = new MemoryStream();
        ThixShow.Core.Formats.Pptx.PptxWriter.Write(presentation, stream);
        return stream.ToArray();
    }

    private static string TitleIn(byte[] pptx)
    {
        var presentation = ThixShow.Core.Formats.Pptx.PptxReader.Read(new MemoryStream(pptx));
        return SlideTitles.TitleOf(presentation, presentation.Slides[0]);
    }

    private static async Task<SampleCloud> SignedInAsync()
    {
        var cloud = new SampleCloud();
        CloudService.HandlerForTests = () => cloud;
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "TEST-PC");
        return cloud;
    }

    private static async Task<QuestionDialog> DialogAsync(OfficeWindow window)
    {
        await UntilAsync(() => window.OwnedWindows.OfType<QuestionDialog>().Any(), "a question");
        var dialog = window.OwnedWindows.OfType<QuestionDialog>().Last();
        Settle();
        return dialog;
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        for (var round = 0; round < 300 && !condition(); round++)
        {
            Settle();
            await Task.Delay(10);
        }

        Assert.True(condition(), $"waited in vain for {what}");
    }
}
