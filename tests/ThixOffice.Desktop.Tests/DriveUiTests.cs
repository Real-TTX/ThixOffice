using System.Text;
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

namespace ThixOffice.Desktop.Tests;

/// <summary>
/// ThixCloud and ThixDrive in the window, against a cloud in memory: the places of Öffnen and Speichern unter, the sign-in, the
/// browser in both modes, the title bar's state, the conflict question, a waiting upload when closing, the Cloud tab, the policy.
/// </summary>
public sealed class DriveUiTests
{
    [AvaloniaFact]
    public async Task The_ThixDrive_place_shows_the_sign_in_until_signed_in_and_then_the_folders_and_documents()
    {
        using var data = new TestData();
        var cloud = Use(SampleCloud.WithSampleDrive());
        cloud.AddFile(null, "Brief.tst", Utf8("Hallo"));
        var window = TestData.App.NewDocumentWindow();
        window.Backstage.Open(OfficeWindow.OpenPageId);
        var page = (OpenPage)window.Backstage.CurrentPage!;

        page.ShowPlace(DocumentPlace.Drive);
        TestData.Settle();
        var browser = page.Drive!;
        Assert.True(browser.ShowsSignIn);
        Assert.False(window.CloudIndicator.IsVisible); // not signed in, a local document: nothing to say

        var signIn = browser.SignInPanel;
        signIn.ShowSignInWay(password: true);
        signIn.ServerUrlField.Text = SampleCloud.ServerUrl;
        signIn.UserNameField.Text = SampleCloud.UserName;
        signIn.PasswordField.Text = SampleCloud.Password;
        await signIn.ConnectWithPasswordAsync();
        await UntilAsync(() => !browser.ShowsSignIn && browser.Rows.Count > 0, "the list after the sign-in");
        await browser.LoadTask;
        TestData.Settle();

        Assert.Equal(["Privat", "Projekte", "Rechnungen", "Brief.tst"], browser.Rows.Select(row => row.Name)); // folders first, only the app's type
        Assert.Equal("ThixDrive", browser.CrumbText);
        Assert.Equal("folder", browser.Rows[0].Icon);
        Assert.Equal("", signIn.PasswordField.Text); // the password is not kept
        Assert.Equal(CloudIndicatorKind.SignedIn, window.CloudIndicator.Kind);

        await browser.NavigateAsync(browser.Rows.Single(row => row.Name == "Projekte").Item.Id);
        Assert.Equal("ThixDrive › Projekte", browser.CrumbText);
        await browser.SearchAsync("brief");
        Assert.Equal(["Brief.tst"], browser.Rows.Select(row => row.Name)); // across the Drive, the type filter still applies
        Assert.True(browser.Rows[0].ShowsLocation);
        Assert.Equal("ThixDrive › Suche: „brief“", browser.CrumbText);
    }

    [AvaloniaFact]
    public async Task A_document_opened_from_ThixDrive_comes_into_the_window_and_the_title_bar_says_so()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("Hallo aus der Cloud"));
        var window = TestData.App.NewDocumentWindow();
        window.Backstage.Open(OfficeWindow.OpenPageId);
        var page = (OpenPage)window.Backstage.CurrentPage!;
        page.ShowPlace(DocumentPlace.Drive);
        await page.Drive!.LoadTask;
        TestData.Settle();

        page.Drive.Select("Brief.tst");
        await page.Drive.ActivateSelectedAsync();
        TestData.Settle();

        Assert.False(window.Backstage.IsOpen);
        Assert.Equal(file.Id, window.Document.Drive!.ItemId); // the untouched new document was replaced
        Assert.Equal("Hallo aus der Cloud", ((TestTextDocument)window.Document).Text);
        Assert.Equal("Brief.tst", window.DocumentNameText);
        Assert.Equal(CloudIndicatorKind.InDrive, window.CloudIndicator.Kind);
        Assert.Equal("In ThixDrive", window.CloudIndicator.Text);

        window.Backstage.Open(OfficeWindow.OpenPageId);
        var recent = ((OpenPage)window.Backstage.CurrentPage!).Rows.Single();
        Assert.True(recent.IsInDrive);
        Assert.Equal("cloud", recent.Icon);
    }

    [AvaloniaFact]
    public async Task Ctrl_S_uploads_and_a_version_saved_in_between_asks_with_the_copy_first_and_overwrite_set_apart()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var window = (await TestData.App.OpenFromDriveAsync(file.Id))!;
        var editor = (TestEditor)window.Editor;

        editor.Box.Text = "neu";
        TestData.Settle();
        Assert.True(await window.SaveAsync());
        Assert.Equal("neu", Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal(CloudIndicatorKind.InDrive, window.CloudIndicator.Kind);

        cloud.SaveElsewhere(file.Id, Utf8("von Bert"));
        editor.Box.Text = "von Anna";
        TestData.Settle();
        var saving = window.SaveAsync();
        var dialog = await DialogAsync(window);
        Assert.Equal(DriveDialogs.ConflictTitle, dialog.Title);
        Assert.Equal(["Als Kopie speichern", "Später", "Überschreiben"], dialog.Buttons.Select(button => ((IconLabel)button.Content!).Text));
        Assert.True(dialog.Buttons[0].IsDefault);
        Assert.True(ActionBar.GetSeparated(dialog.Buttons[2]));

        dialog.Buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Überschreiben
        Assert.True(await saving);
        Assert.Equal("von Anna", Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal(DriveSyncState.Uploaded, window.Document.DriveState);
    }

    [AvaloniaFact]
    public async Task Later_keeps_the_conflict_in_the_title_bar_and_a_click_there_saves_a_copy()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var window = (await TestData.App.OpenFromDriveAsync(file.Id))!;
        cloud.SaveElsewhere(file.Id, Utf8("von Bert"));
        ((TestEditor)window.Editor).Box.Text = "von Anna";
        TestData.Settle();

        var saving = window.SaveAsync();
        (await DialogAsync(window)).Buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Später
        Assert.True(await saving);
        TestData.Settle();
        Assert.Equal(CloudIndicatorKind.Conflict, window.CloudIndicator.Kind);
        Assert.Equal("Konflikt", window.CloudIndicator.Text);

        window.CloudIndicator.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        (await DialogAsync(window)).Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Als Kopie speichern
        await UntilAsync(() => window.Document.DriveState == DriveSyncState.Uploaded, "the copy is uploaded");

        Assert.Equal("Brief (2).tst", window.Document.Drive!.Name);
        Assert.Equal("von Bert", Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal("Brief (2).tst", window.DocumentNameText);
    }

    [AvaloniaFact]
    public async Task Offline_the_document_is_saved_here_the_title_bar_says_Nicht_hochgeladen_and_a_click_uploads_it()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var window = (await TestData.App.OpenFromDriveAsync(file.Id))!;
        ((TestEditor)window.Editor).Box.Text = "unterwegs";
        TestData.Settle();
        cloud.IsOffline = true;

        Assert.True(await window.SaveAsync());
        TestData.Settle();

        Assert.False(window.Document.IsDirty);
        Assert.Equal(CloudIndicatorKind.NotUploaded, window.CloudIndicator.Kind);
        Assert.Equal("Nicht hochgeladen", window.CloudIndicator.Text);
        Assert.True(window.MessageBar.IsVisible);
        Assert.Contains("auf diesem Computer gespeichert", window.MessageBar.Text, StringComparison.Ordinal);
        Assert.Equal("unterwegs", File.ReadAllText(window.Document.FilePath!));

        cloud.IsOffline = false;
        window.CloudIndicator.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await UntilAsync(() => window.Document.DriveState == DriveSyncState.Uploaded, "the retry uploads");
        TestData.Settle();

        Assert.Equal("unterwegs", Encoding.UTF8.GetString(cloud.ContentOf(file.Id)));
        Assert.Equal(CloudIndicatorKind.InDrive, window.CloudIndicator.Kind);
        Assert.False(window.MessageBar.IsVisible);
    }

    [AvaloniaFact]
    public async Task Closing_with_a_waiting_upload_asks_and_Trotzdem_schliessen_keeps_the_working_copy()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var file = cloud.AddFile(null, "Brief.tst", Utf8("alt"));
        var window = (await TestData.App.OpenFromDriveAsync(file.Id))!;
        ((TestEditor)window.Editor).Box.Text = "nicht verlieren";
        TestData.Settle();
        cloud.IsOffline = true;
        await window.SaveAsync();

        window.Close();
        var dialog = await DialogAsync(window);
        Assert.Equal(["Erneut versuchen", "Abbrechen", "Trotzdem schließen"], dialog.Buttons.Select(button => ((IconLabel)button.Content!).Text));
        dialog.Buttons[1].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Abbrechen
        await UntilAsync(() => window.OwnedWindows.Count == 0, "the question closes");
        Assert.Contains(window, TestData.App.DocumentWindows);

        window.Close();
        (await DialogAsync(window)).Buttons[2].RaiseEvent(new RoutedEventArgs(Button.ClickEvent)); // Trotzdem schließen
        await UntilAsync(() => !TestData.App.DocumentWindows.Contains(window), "the window closes");

        var pending = Assert.Single(DriveService.PendingUploads());
        Assert.Equal("nicht verlieren", File.ReadAllText(pending.WorkingCopy));
    }

    [AvaloniaFact]
    public async Task Speichern_unter_into_ThixDrive_adds_the_type_and_asks_before_replacing()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(new SampleCloud());
        var folder = cloud.AddFolder(null, "Projekte");
        var window = TestData.App.NewDocumentWindow();
        ((TestEditor)window.Editor).Box.Text = "Plan A";
        TestData.Settle();
        var browser = await SaveAsBrowserAsync(window);
        await browser.NavigateAsync(folder.Id);

        browser.FileNameField.Text = "Plan";
        await browser.SaveAsync();

        var item = cloud.FindByName(folder.Id, "Plan.tst")!;
        Assert.Equal("Plan A", Encoding.UTF8.GetString(cloud.ContentOf(item.Id)));
        Assert.Equal(item.Id, window.Document.Drive!.ItemId);
        Assert.False(window.Backstage.IsOpen);

        var second = TestData.App.NewDocumentWindow();
        ((TestEditor)second.Editor).Box.Text = "Plan B";
        TestData.Settle();
        var other = await SaveAsBrowserAsync(second);
        await other.NavigateAsync(folder.Id);
        other.FileNameField.Text = "plan";
        var saving = other.SaveAsync();
        var dialog = await DialogAsync(second);
        Assert.Equal(["Anderen Namen wählen", "Ersetzen"], dialog.Buttons.Select(button => ((IconLabel)button.Content!).Text));
        dialog.Buttons[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        await saving;

        Assert.Null(second.Document.Drive);
        Assert.Equal("Plan A", Encoding.UTF8.GetString(cloud.ContentOf(item.Id)));
        Assert.True(second.Backstage.IsOpen);
    }

    [AvaloniaFact]
    public async Task A_bad_name_is_shown_at_the_file_name()
    {
        using var data = new TestData();
        await SignedInAsync(new SampleCloud());
        var window = TestData.App.NewDocumentWindow();
        var browser = await SaveAsBrowserAsync(window);

        browser.FileNameField.Text = "a/b";
        await browser.SaveAsync();

        var field = (MatDesktop.MatFormField)browser.FileNameField.Parent!;
        Assert.False(string.IsNullOrEmpty(field.Error));
        Assert.Null(window.Document.Drive);
    }

    [AvaloniaFact]
    public async Task The_Cloud_tab_shows_the_account_and_signs_out_after_asking()
    {
        using var data = new TestData();
        var cloud = await SignedInAsync(SampleCloud.WithSampleDrive());
        var window = TestData.App.NewDocumentWindow();

        window.ShowSettings(SettingsPage.CloudTab);
        TestData.Settle();
        var settings = (SettingsPage)window.Backstage.CurrentPage!;
        Assert.Equal(SettingsPage.CloudTab, settings.CurrentTab);
        Assert.True(settings.ShowsCloudAccount);
        Assert.False(settings.CloudSignInPanel.IsVisible);

        await CloudService.DisconnectAsync();
        TestData.Settle();
        Assert.False(settings.ShowsCloudAccount);
        Assert.True(settings.CloudSignInPanel.IsVisible);
        Assert.Contains("POST /api/v1/devices/logout", cloud.Requests);
    }

    [AvaloniaFact]
    public void Under_the_policy_DisableCloud_the_place_ThixDrive_is_not_offered()
    {
        using var data = new TestData();
        data.Policies.Add(AccessPolicy.DisableCloud);
        var window = TestData.App.NewDocumentWindow();
        window.Backstage.Open(OfficeWindow.OpenPageId);
        var page = (OpenPage)window.Backstage.CurrentPage!;

        page.ShowPlace(DocumentPlace.Drive);

        Assert.Equal(DocumentPlace.Recent, page.Place);
        Assert.Null(page.Drive);
        Assert.False(window.CloudIndicator.IsVisible);
    }

    private static async Task<DriveBrowser> SaveAsBrowserAsync(OfficeWindow window)
    {
        window.Backstage.Open(OfficeWindow.SaveAsPageId);
        var page = (SaveAsPage)window.Backstage.CurrentPage!;
        page.ShowPlace(DocumentPlace.Drive);
        await page.Drive!.LoadTask;
        TestData.Settle();
        return page.Drive;
    }

    private static SampleCloud Use(SampleCloud cloud)
    {
        CloudService.HandlerForTests = () => cloud;
        return cloud;
    }

    private static async Task<SampleCloud> SignedInAsync(SampleCloud cloud)
    {
        Use(cloud);
        await CloudService.ConnectAsync(SampleCloud.ServerUrl, SampleCloud.UserName, SampleCloud.Password, "TEST-PC");
        return cloud;
    }

    private static byte[] Utf8(string text) => Encoding.UTF8.GetBytes(text);

    /// <summary>The question the window shows (it is shown asynchronously).</summary>
    private static async Task<QuestionDialog> DialogAsync(OfficeWindow window)
    {
        await UntilAsync(() => window.OwnedWindows.OfType<QuestionDialog>().Any(), "a question");
        var dialog = window.OwnedWindows.OfType<QuestionDialog>().Last();
        TestData.Settle();
        return dialog;
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
