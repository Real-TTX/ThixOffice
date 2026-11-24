using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Documents;
using static ThixWrite.Tests.TestData;

namespace ThixWrite.Tests;

/// <summary>A .docx protected with "Mit Kennwort verschlüsseln" in the real ThixWrite: opened with its password, saved encrypted again.</summary>
public sealed class PasswordTests
{
    private const string Password = "Geheim-2026";

    [AvaloniaFact]
    public async Task A_protected_docx_opens_with_its_password_and_stays_protected()
    {
        using var data = new TestData();
        var (path, expected) = ProtectedSample(data);
        var window = await NewWindowAsync();

        var opened = await OpenWithPasswordAsync(window, path);

        Assert.Equal(expected, ((WordDocument)opened.Document).Content.GetPlainText());
        Assert.True(opened.Document.HasPassword);
        Assert.True(opened.ShowsProtectedMarker);

        await TypeAsync(opened, "Vertraulich ");
        await PressAsync(opened, PhysicalKey.S, CommandKey);
        Assert.False(opened.Document.IsDirty);
        using (var file = File.OpenRead(path))
        {
            Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(file));
        }

        var again = DocumentService.Open(path, name => new WordDocument(name), Password);
        Assert.Contains("Vertraulich ", again.Content.GetPlainText(), StringComparison.Ordinal);
    }

    [AvaloniaFact]
    public async Task Saving_a_protected_document_as_odt_asks_and_then_saves_without_the_password()
    {
        using var data = new TestData();
        var (path, _) = ProtectedSample(data);
        var opened = await OpenWithPasswordAsync(await NewWindowAsync(), path);
        var odt = Path.Combine(data.DocumentsFolder, "Offen.odt");

        var refused = opened.SaveToAsync(odt);
        var question = await QuestionAsync(opened);
        Assert.Equal("„Offen.odt“ kann nicht mit einem Kennwort geschützt werden. Ohne Kennwort speichern?", question.Message);
        question.Buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Abbrechen
        Assert.False(await refused);
        Assert.False(File.Exists(odt));
        Assert.True(opened.Document.HasPassword);

        var saving = opened.SaveToAsync(odt);
        while (!saving.IsCompleted)
        {
            if (opened.OwnedWindows.OfType<QuestionDialog>().LastOrDefault() is { } next)
            {
                next.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Ohne Kennwort speichern (and the odt question)
            }

            await SettleAsync();
        }

        Assert.True(await saving);
        Assert.Equal((byte)'P', File.ReadAllBytes(odt)[0]);
        Assert.False(opened.Document.HasPassword);
        Assert.False(opened.ShowsProtectedMarker);
    }

    private static (string Path, string Text) ProtectedSample(TestData data)
    {
        var plain = Path.Combine(data.DocumentsFolder, "Muster.docx");
        var sample = (WordDocument)TestData.App.CreateSampleDocument(plain);
        DocumentService.SaveAs(sample, plain);
        var path = Path.Combine(data.DocumentsFolder, "Geschützt.docx");
        using (var output = File.Create(path))
        {
            OfficeEncryption.Encrypt(File.ReadAllBytes(plain), Password, output);
        }

        return (path, DocumentService.Open(plain, name => new WordDocument(name)).Content.GetPlainText());
    }

    private static async Task<OfficeWindow> OpenWithPasswordAsync(OfficeWindow window, string path)
    {
        var opening = TestData.App.OpenFileAsync(path, window);
        await SettleAsync();
        var dialog = Assert.Single(window.OwnedWindows.OfType<DocumentPasswordDialog>());
        dialog.PasswordBox.Text = Password;
        dialog.Confirm();
        for (var round = 0; round < 200 && !opening.IsCompleted; round++)
        {
            await SettleAsync();
        }

        return Assert.IsType<OfficeWindow>(await opening);
    }

    private static async Task<QuestionDialog> QuestionAsync(OfficeWindow window)
    {
        for (var round = 0; round < 200 && !window.OwnedWindows.OfType<QuestionDialog>().Any(); round++)
        {
            await SettleAsync();
        }

        return window.OwnedWindows.OfType<QuestionDialog>().Single();
    }
}
