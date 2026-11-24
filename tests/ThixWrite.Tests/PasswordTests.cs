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
    public async Task Saving_a_protected_document_as_txt_asks_and_then_saves_without_the_password()
    {
        using var data = new TestData();
        var (path, _) = ProtectedSample(data);
        var opened = await OpenWithPasswordAsync(await NewWindowAsync(), path);
        var txt = Path.Combine(data.DocumentsFolder, "Offen.txt");

        var refused = opened.SaveToAsync(txt);
        var question = await QuestionAsync(opened);
        Assert.Equal("„Offen.txt“ kann nicht mit einem Kennwort geschützt werden. Ohne Kennwort speichern?", question.Message);
        question.Buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Abbrechen
        Assert.False(await refused);
        Assert.False(File.Exists(txt));
        Assert.True(opened.Document.HasPassword);

        Assert.True(await AnswerEveryQuestionAsync(opened, opened.SaveToAsync(txt))); // Ohne Kennwort speichern

        Assert.NotEqual(0xD0, File.ReadAllBytes(txt)[0]); // plain text, not an encrypted compound file
        Assert.Contains("Wichtig: Die Versan", File.ReadAllText(txt), StringComparison.Ordinal);
        Assert.False(opened.Document.HasPassword);
        Assert.False(opened.ShowsProtectedMarker);
    }

    [AvaloniaFact]
    public async Task A_protected_document_saved_as_odt_keeps_its_password_without_asking()
    {
        using var data = new TestData();
        var (path, expected) = ProtectedSample(data);
        var opened = await OpenWithPasswordAsync(await NewWindowAsync(), path);
        var odt = Path.Combine(data.DocumentsFolder, "Geschützt.odt");
        var asked = new List<string>();

        Assert.True(await AnswerEveryQuestionAsync(opened, opened.SaveToAsync(odt), asked));

        Assert.DoesNotContain(asked, message => message.Contains("Kennwort", StringComparison.Ordinal));
        Assert.True(opened.Document.HasPassword);
        using (var file = File.OpenRead(odt))
        {
            Assert.Equal(EncryptedFileKind.OdfEncrypted, OfficeEncryption.Detect(file));
        }

        Assert.Throws<PasswordRequiredException>(() => DocumentService.Open(odt, name => new WordDocument(name)));
        Assert.Equal(expected, DocumentService.Open(odt, name => new WordDocument(name), Password).Content.GetPlainText());
    }

    [AvaloniaFact]
    public async Task A_protected_odt_opens_with_its_password_and_stays_protected()
    {
        using var data = new TestData();
        var plain = Path.Combine(data.DocumentsFolder, "Muster.odt");
        DocumentService.SaveAs(TestData.App.CreateSampleDocument(plain), plain);
        var path = Path.Combine(data.DocumentsFolder, "Geschützt.odt");
        using (var output = File.Create(path))
        {
            OfficeEncryption.Encrypt(File.ReadAllBytes(plain), Password, output);
        }

        var expected = DocumentService.Open(plain, name => new WordDocument(name)).Content.GetPlainText();
        var opened = await OpenWithPasswordAsync(await NewWindowAsync(), path);

        Assert.Equal(expected, ((WordDocument)opened.Document).Content.GetPlainText());
        Assert.True(opened.Document.HasPassword);
        Assert.True(opened.CanProtectDocument);

        await TypeAsync(opened, "Vertraulich ");
        await PressAsync(opened, PhysicalKey.S, CommandKey);
        await AnswerEveryQuestionAsync(opened, null);
        Assert.False(opened.Document.IsDirty);
        using (var file = File.OpenRead(path))
        {
            Assert.Equal(EncryptedFileKind.OdfEncrypted, OfficeEncryption.Detect(file));
        }

        Assert.Throws<WrongPasswordException>(() => DocumentService.Open(path, name => new WordDocument(name), "falsch"));
        Assert.Contains("Vertraulich ", DocumentService.Open(path, name => new WordDocument(name), Password).Content.GetPlainText(), StringComparison.Ordinal);
    }

    /// <summary>Answers every question of <paramref name="window"/> with its first choice until <paramref name="saving"/> ends (null: until the document is saved).</summary>
    private static async Task<bool> AnswerEveryQuestionAsync(OfficeWindow window, Task<bool>? saving, List<string>? asked = null)
    {
        for (var round = 0; round < 400 && (saving is null ? window.Document.IsDirty : !saving.IsCompleted); round++)
        {
            if (window.OwnedWindows.OfType<QuestionDialog>().LastOrDefault() is { } question)
            {
                asked?.Add(question.Message);
                question.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
            }

            await SettleAsync();
        }

        return saving is null || await saving;
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
