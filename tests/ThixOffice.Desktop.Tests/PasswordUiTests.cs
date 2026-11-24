using System.Text;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Formats.OfficeCrypto;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Tests;

/// <summary>
/// Password-protected documents in the shell: opening asks for the password (a wrong one is said at the field, Abbrechen opens nothing),
/// "Mit Kennwort verschlüsseln" sets it and Strg+S writes the file encrypted, a protected recovery file is asked for instead of discarded.
/// </summary>
public sealed class PasswordUiTests
{
    private const string Password = "Geheim-2026";

    [AvaloniaFact]
    public async Task A_protected_file_opens_after_a_wrong_and_the_right_password()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var path = Encrypted(data, "Bericht.tst", "Geheimer Text");

        var opening = TestData.App.OpenFileAsync(path, window);
        var dialog = await PasswordDialogAsync(window);
        Assert.Equal("„Bericht.tst“ ist geschützt. Geben Sie das Kennwort ein, um die Datei zu öffnen.", dialog.Message);
        Assert.Null(dialog.DiscardButton);

        dialog.PasswordBox.Text = "falsch";
        dialog.Confirm();
        await UntilAsync(() => dialog.Error is not null, "the error at the field");
        Assert.Equal(WrongPasswordException.Text, dialog.Error);
        Assert.True(dialog.IsVisible); // another try
        Assert.False(opening.IsCompleted);

        dialog.PasswordBox.Text = Password;
        dialog.KeyPressQwerty(PhysicalKey.Enter, RawInputModifiers.None); // Enter = OK
        var opened = await WithinAsync(opening);

        Assert.Same(window, opened); // the untouched new document was replaced
        Assert.Equal("Geheimer Text", ((TestEditor)opened!.Editor).Box.Text);
        Assert.True(opened.Document.HasPassword);
        Assert.False(opened.Document.IsDirty);
        Assert.True(opened.ShowsProtectedMarker);
        Assert.True(opened.Commands[CommonCommandIds.Encrypt].Checked);
    }

    [AvaloniaFact]
    public async Task Abbrechen_opens_nothing_and_says_nothing()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var path = Encrypted(data, "Bericht.tst", "Geheimer Text");

        var opening = TestData.App.OpenFileAsync(path, window);
        var dialog = await PasswordDialogAsync(window);
        dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

        Assert.Null(await WithinAsync(opening));
        TestData.Settle();
        Assert.Empty(window.OwnedWindows); // no "Öffnen nicht möglich"
        Assert.Single(TestData.App.DocumentWindows);
        Assert.True(window.Document.IsPristine);
        Assert.Empty(RecentDocuments.List());
    }

    [AvaloniaFact]
    public async Task Mit_Kennwort_verschluesseln_protects_the_next_save()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        ((TestEditor)window.Editor).Box.Text = "Vertraulich";
        TestData.Settle();
        var path = Path.Combine(data.DocumentsFolder, "Brief.tst");
        Assert.True(await window.SaveToAsync(path));
        Assert.Contains(window.Commands.Search("kennwort"), command => command.Id == CommonCommandIds.Encrypt);
        Assert.False(window.ShowsProtectedMarker);

        var encrypting = window.Commands.ExecuteAsync(CommonCommandIds.Encrypt);
        await UntilAsync(() => window.OwnedWindows.OfType<EncryptDocumentDialog>().Any(), "the dialog");
        var dialog = window.OwnedWindows.OfType<EncryptDocumentDialog>().Single();
        TestData.Settle();
        dialog.PasswordBox.Text = Password;
        dialog.ConfirmationBox.Text = "anders";
        dialog.Confirm();
        TestData.Settle();
        Assert.True(dialog.IsVisible);
        Assert.Equal("Die Kennwörter stimmen nicht überein.", Fields(dialog).Single(field => FormErrors.GetField(field) == OfficeEncryption.ConfirmationField).Error);

        dialog.ConfirmationBox.Text = Password;
        dialog.Confirm();
        Assert.True(await WithinAsync(encrypting));
        TestData.Settle();
        Assert.True(window.Document.HasPassword);
        Assert.True(window.Document.IsDirty);
        Assert.True(window.ShowsProtectedMarker);
        Assert.Contains("beim nächsten Speichern", window.MessageBar.Text);

        window.KeyPressQwerty(PhysicalKey.S, TestData.CommandKey);
        await UntilAsync(() => !window.Document.IsDirty, "the save");
        using (var file = File.OpenRead(path))
        {
            Assert.Equal(EncryptedFileKind.OfficeAgile, OfficeEncryption.Detect(file));
            Assert.Equal("Vertraulich", Encoding.UTF8.GetString(OfficeEncryption.Decrypt(file, Password)));
        }
    }

    [AvaloniaFact]
    public async Task The_Speichern_unter_page_offers_the_password()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        window.Backstage.Open(OfficeWindow.SaveAsPageId);
        var page = (SaveAsPage)window.Backstage.CurrentPage!;
        page.ShowPlace(DocumentPlace.Computer);
        TestData.Settle();
        Assert.True(page.PasswordButton.IsVisible);

        page.PasswordButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => window.OwnedWindows.OfType<EncryptDocumentDialog>().Any(), "the dialog");
        var dialog = window.OwnedWindows.OfType<EncryptDocumentDialog>().Single();
        dialog.PasswordBox.Text = Password;
        dialog.ConfirmationBox.Text = Password;
        dialog.Confirm();
        await UntilAsync(() => !window.Backstage.IsOpen, "the backstage closes");
        Assert.True(window.Document.HasPassword);
    }

    [AvaloniaFact]
    public async Task A_protected_recovery_file_is_asked_for_kept_on_Abbrechen_and_discarded_only_on_request()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var lost = new TestTextDocument("Bericht.tst");
        lost.Set("ungespeichert");
        lost.SetPassword(Password);
        DocumentService.WriteRecovery(lost);

        // Abbrechen: the file stays for the next start
        Assert.Equal(0, TestData.App.RecoverDocuments());
        Assert.Single(TestData.App.ProtectedRecoveries);
        var recovering = TestData.App.RecoverProtectedDocumentsAsync();
        var dialog = await PasswordDialogAsync(window);
        Assert.Equal("„Bericht.tst“ wird wiederhergestellt. Geben Sie das Kennwort des Dokuments ein.", dialog.Message);
        Assert.True(ActionBar.GetSeparated(dialog.DiscardButton!));
        dialog.CancelButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, await WithinAsync(recovering));
        Assert.Single(DocumentService.RecoveredDocuments());

        // the password brings it back, protected
        Assert.Equal(0, TestData.App.RecoverDocuments());
        recovering = TestData.App.RecoverProtectedDocumentsAsync();
        dialog = await PasswordDialogAsync(window);
        dialog.PasswordBox.Text = Password;
        dialog.Confirm();
        Assert.Equal(1, await WithinAsync(recovering));
        var recovered = TestData.App.DocumentWindows.Single(shown => shown.Document.IsRecovered);
        Assert.Equal("ungespeichert", ((TestEditor)recovered.Editor).Box.Text);
        Assert.True(recovered.Document.HasPassword);
        Assert.Empty(DocumentService.RecoveredDocuments());

        // Verwerfen deletes it
        DocumentService.WriteRecovery(lost);
        Assert.Equal(0, TestData.App.RecoverDocuments());
        recovering = TestData.App.RecoverProtectedDocumentsAsync();
        dialog = await PasswordDialogAsync(recovered);
        dialog.DiscardButton!.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal(0, await WithinAsync(recovering));
        Assert.Empty(DocumentService.RecoveredDocuments());
    }

    private static string Encrypted(TestData data, string name, string content)
    {
        var path = Path.Combine(data.DocumentsFolder, name);
        using var file = File.Create(path);
        OfficeEncryption.Encrypt(Encoding.UTF8.GetBytes(content), Password, file);
        return path;
    }

    private static IEnumerable<MatFormField> Fields(Window dialog) =>
        Avalonia.VisualTree.VisualExtensions.GetVisualDescendants(dialog).OfType<MatFormField>();

    private static async Task<DocumentPasswordDialog> PasswordDialogAsync(Window owner)
    {
        await UntilAsync(() => owner.OwnedWindows.OfType<DocumentPasswordDialog>().Any(), "the password dialog");
        TestData.Settle();
        return owner.OwnedWindows.OfType<DocumentPasswordDialog>().Last();
    }

    private static async Task<T> WithinAsync<T>(Task<T> task)
    {
        await UntilAsync(() => task.IsCompleted, "the flow to end");
        return await task;
    }

    private static async Task UntilAsync(Func<bool> condition, string what)
    {
        for (var round = 0; round < 300 && !condition(); round++)
        {
            TestData.Settle();
            await Task.Delay(10);
        }

        Assert.True(condition(), $"waited in vain for {what}");
    }
}
