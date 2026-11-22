using Avalonia.Headless.XUnit;
using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Sync;
using ThixMail.Core.Tests;
using ThixMail.Editor;
using ThixMail.Pages;
using static ThixMail.Tests.TestData;

namespace ThixMail.Tests;

/// <summary>The browser sign-in on the Konten page: the form chooses it for Gmail/Microsoft, says honestly when ThixMail has no client id, signs in.</summary>
public sealed class OAuthUiTests
{
    private const string Gmail = "anna.berger@gmail.com";

    [AvaloniaFact]
    public async Task Without_a_client_id_the_form_says_that_an_app_password_is_needed_for_now()
    {
        using var data = new TestData();
        var window = await data.WindowAsync(sample: false);
        await window.Commands.ExecuteAsync(MailCommandIds.AddAccount);
        await SettleAsync();
        var form = ((AccountsPage)window.Backstage.CurrentPage!).Form!;

        form.AddressBox.Text = Gmail;
        await SettleAsync();

        Assert.True(form.ShowsSignInChoice);
        Assert.False(form.OffersBrowserSignIn);
        Assert.False(form.UsesBrowserSignIn);
        Assert.True(form.PasswordBox.IsEffectivelyVisible);
        Assert.Contains("noch nicht freigeschaltet", form.SignInText.Text, StringComparison.Ordinal);
        Assert.Contains("App-Passwort", form.SignInText.Text, StringComparison.Ordinal);

        form.AddressBox.Text = "anna@outlook.de";
        await SettleAsync();
        Assert.Contains("Microsoft", form.SignInText.Text, StringComparison.Ordinal);

        form.AddressBox.Text = SampleMailbox.Address;
        await SettleAsync();
        Assert.False(form.ShowsSignInChoice);
    }

    [AvaloniaFact]
    public async Task A_Gmail_account_signs_in_in_the_browser_and_syncs_with_its_token()
    {
        using var data = new TestData();
        var provider = new FakeOAuthServer().Plug();
        data.Server.AddMailbox(Gmail, "nie-benutzt");
        data.Server.Deliver(Gmail, "INBOX", new MailAddress("Jonas Weber", "jonas.weber@example.de"), "Willkommen bei OAuth", "Hallo");
        provider.Issued = token => data.Server.AcceptAccessToken(Gmail, token);
        AccountForm.BrowserForTests = provider.BrowseAsync;
        var window = await data.WindowAsync(sample: false);
        await window.Commands.ExecuteAsync(MailCommandIds.AddAccount);
        await SettleAsync();
        var page = (AccountsPage)window.Backstage.CurrentPage!;
        var form = page.Form!;

        form.SenderBox.Text = "Anna Berger";
        form.AddressBox.Text = Gmail;
        await SettleAsync();
        Assert.True(form.UsesBrowserSignIn);
        Assert.False(form.PasswordBox.IsEffectivelyVisible); // the password is typed at Google, not here

        await ClickAsync(window, form.NextButton);
        await UntilAsync(() => form.Result.Text.StartsWith("Verbindung hergestellt", StringComparison.Ordinal), () => $"the test with the token ({form.Result.Text})");
        Assert.True(form.HasBrowserSignIn);
        Assert.Contains("angemeldet", form.SignInText.Text, StringComparison.Ordinal);
        await ClickAsync(window, form.SaveButton);
        await UntilAsync(() => page.Form is null, "the saved account");

        var editor = EditorOf(window);
        var account = editor.Document.Accounts.Accounts.Single();
        Assert.Equal(MailAuthentication.OAuth, account.Authentication);
        Assert.Equal(OAuthProviders.GoogleId, account.OAuthProvider);
        Assert.NotNull(AccountService.OAuthTokensOf(editor.Document.Accounts, account.Id));
        Assert.Null(AccountService.Password(editor.Document.Accounts, account, AccountService.ImapSecret));
        window.Backstage.Close();
        await UntilAsync(() => editor.Rows.Any(r => r.IsMessage), "the messages fetched through XOAUTH2");
        Assert.True(data.Server.XOAuth2SignIns > 0);
    }

    [AvaloniaFact]
    public async Task Refused_consent_is_explained_and_an_ended_sign_in_asks_to_sign_in_again()
    {
        using var data = new TestData();
        var provider = new FakeOAuthServer().Plug();
        data.Server.AddMailbox(Gmail, "nie-benutzt");
        provider.Issued = token => data.Server.AcceptAccessToken(Gmail, token);
        AccountForm.BrowserForTests = provider.BrowseAsync;
        var window = await data.WindowAsync(sample: false);
        await window.Commands.ExecuteAsync(MailCommandIds.AddAccount);
        await SettleAsync();
        var page = (AccountsPage)window.Backstage.CurrentPage!;
        var form = page.Form!;
        form.AddressBox.Text = Gmail;

        provider.Consent = FakeConsent.Refuse;
        await ClickAsync(window, form.NextButton);
        await UntilAsync(() => form.Result.Text.Contains("nicht erlaubt", StringComparison.Ordinal), () => $"the refused consent ({form.Result.Text})");
        Assert.False(await form.SaveAsync());

        provider.Consent = FakeConsent.Grant;
        await ClickAsync(window, form.BrowserButton);
        await UntilAsync(() => form.Result.Text.StartsWith("Verbindung hergestellt", StringComparison.Ordinal), "the second sign-in");
        Assert.True(await form.SaveAsync());

        // the provider ends the sign-in: the account says so, its mail stays
        var editor = EditorOf(window);
        var account = editor.Document.Accounts.Accounts.Single();
        await UntilAsync(() => editor.Document.StatusOf(account.Id).Phase == SyncPhase.Idle, "the first sync");
        provider.ExpireRefreshTokens();
        data.Server.RevokeAccessTokens(Gmail);
        data.Server.FailNext(1); // the open connection drops: the next one must sign in
        editor.Document.SendReceiveAll();
        await UntilAsync(() => editor.Document.StatusOf(account.Id).Phase == SyncPhase.SignInFailed, "the ended sign-in");
        Assert.Contains("erneut an", editor.Document.StatusOf(account.Id).Message, StringComparison.Ordinal);

        page.ShowForm(account);
        Assert.Equal("Erneut im Browser anmelden", ((ThixOffice.Desktop.Controls.IconLabel)page.Form!.BrowserButton.Content!).Text);
        await ClickAsync(window, page.Form.BrowserButton);
        await UntilAsync(() => page.Form.Result.Text.StartsWith("Verbindung hergestellt", StringComparison.Ordinal), "signing in again");
        Assert.True(await page.Form.SaveAsync());
        await UntilAsync(() => editor.Document.StatusOf(account.Id).Phase == SyncPhase.Idle, "the sync with the new sign-in");
    }
}
