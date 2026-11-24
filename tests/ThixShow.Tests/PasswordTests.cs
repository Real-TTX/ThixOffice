using Avalonia.Headless.XUnit;
using Avalonia.Input;
using ThixOffice.Core.Business;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Documents;
using ThixShow.Core.Model;
using ThixShow.Editor;
using static ThixShow.Tests.TestData;

namespace ThixShow.Tests;

/// <summary>A .pptx protected with "Mit Kennwort verschlüsseln" in the real ThixShow: opened with its password, saved encrypted again.</summary>
public sealed class PasswordTests
{
    private const string Password = "Geheim-2026";

    [AvaloniaFact]
    public async Task A_protected_pptx_opens_with_its_password_and_stays_protected()
    {
        using var data = new TestData();
        var plain = Path.Combine(data.DocumentsFolder, "Muster.pptx");
        DocumentService.SaveAs(TestData.App.CreateSampleDocument(plain), plain);
        var path = Path.Combine(data.DocumentsFolder, "Geschützt.pptx");
        using (var output = File.Create(path))
        {
            OfficeEncryption.Encrypt(File.ReadAllBytes(plain), Password, output);
        }

        var expected = DocumentService.Open(plain, name => new ShowDocument(name)).Presentation;
        var window = await NewWindowAsync();
        var opening = TestData.App.OpenFileAsync(path, window);
        await SettleAsync();
        var dialog = Assert.Single(window.OwnedWindows.OfType<DocumentPasswordDialog>());
        dialog.PasswordBox.Text = Password;
        dialog.Confirm();
        for (var round = 0; round < 200 && !opening.IsCompleted; round++)
        {
            await SettleAsync();
        }

        var opened = Assert.IsType<OfficeWindow>(await opening);
        var presentation = ((ShowDocument)opened.Document).Presentation;
        Assert.Equal(Titles(expected), Titles(presentation));
        Assert.True(opened.Document.HasPassword);
        Assert.True(opened.Commands.Find(ThixOffice.Core.Commands.CommonCommandIds.Encrypt)!.Checked);

        Assert.True(await opened.Commands.ExecuteAsync(ShowCommandIds.NewSlide));
        await PressAsync(opened, PhysicalKey.S, CommandKey);
        Assert.False(opened.Document.IsDirty);
        using (var file = File.OpenRead(path))
        {
            Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(file));
        }

        var again = DocumentService.Open(path, name => new ShowDocument(name), Password);
        Assert.Equal(expected.SlideCount + 1, again.Presentation.SlideCount);
    }

    [AvaloniaFact]
    public async Task A_protected_odp_opens_with_its_password_and_stays_protected()
    {
        using var data = new TestData();
        var plain = Path.Combine(data.DocumentsFolder, "Muster.odp");
        DocumentService.SaveAs(TestData.App.CreateSampleDocument(plain), plain);
        var path = Path.Combine(data.DocumentsFolder, "Geschützt.odp");
        using (var output = File.Create(path))
        {
            OfficeEncryption.Encrypt(File.ReadAllBytes(plain), Password, output);
        }

        var expected = DocumentService.Open(plain, name => new ShowDocument(name)).Presentation;
        var window = await NewWindowAsync();
        var opening = TestData.App.OpenFileAsync(path, window);
        await SettleAsync();
        var dialog = Assert.Single(window.OwnedWindows.OfType<DocumentPasswordDialog>());
        dialog.PasswordBox.Text = Password;
        dialog.Confirm();
        for (var round = 0; round < 200 && !opening.IsCompleted; round++)
        {
            await SettleAsync();
        }

        var opened = Assert.IsType<OfficeWindow>(await opening);
        Assert.Equal(Titles(expected), Titles(((ShowDocument)opened.Document).Presentation));
        Assert.True(opened.Document.HasPassword);
        Assert.True(opened.Commands.Find(ThixOffice.Core.Commands.CommonCommandIds.Encrypt)!.Checked);

        Assert.True(await opened.Commands.ExecuteAsync(ShowCommandIds.NewSlide));
        await PressAsync(opened, PhysicalKey.S, CommandKey);
        await AnswerUntilSavedAsync(opened);
        Assert.False(opened.Document.IsDirty);
        using (var file = File.OpenRead(path))
        {
            Assert.Equal(EncryptedFileKind.OdfEncrypted, OfficeEncryption.Detect(file));
        }

        var again = DocumentService.Open(path, name => new ShowDocument(name), Password);
        Assert.Equal(expected.SlideCount + 1, again.Presentation.SlideCount);
    }

    /// <summary>Answers every question of <paramref name="window"/> with its first choice until the document is saved.</summary>
    private static async Task AnswerUntilSavedAsync(OfficeWindow window)
    {
        for (var round = 0; round < 400 && window.Document.IsDirty; round++)
        {
            if (window.OwnedWindows.OfType<ThixOffice.Desktop.Dialogs.QuestionDialog>().LastOrDefault() is { } question)
            {
                question.Buttons[0].RaiseEvent(new Avalonia.Interactivity.RoutedEventArgs(Avalonia.Controls.Button.ClickEvent));
            }

            await SettleAsync();
        }
    }

    private static List<string> Titles(Presentation presentation) => [.. presentation.Slides.Select(slide => SlideTitles.TitleOf(presentation, slide))];
}
