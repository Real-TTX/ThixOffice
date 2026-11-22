using ThixMail.Core.Accounts;
using ThixMail.Core.Mime;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixMail.Core.Transport;

namespace ThixMail.Core.Tests;

/// <summary>
/// ThixMail against a REAL IMAP/SMTP server through MailKit – only when THIXMAIL_IMAP_HOST is set (scripts/greenmail-test.ps1 starts a throwaway
/// GreenMail container on random ports and sets THIXMAIL_IMAP_PORT, THIXMAIL_IMAPS_PORT, THIXMAIL_SMTP_PORT, THIXMAIL_USER, THIXMAIL_PASSWORD).
/// Without them every test returns at once.
/// </summary>
public sealed class RealServerTests
{
    private static string? Host => Environment.GetEnvironmentVariable("THIXMAIL_IMAP_HOST");

    private static int Port(string name) => int.Parse(Environment.GetEnvironmentVariable(name) ?? "0", System.Globalization.CultureInfo.InvariantCulture);

    private static MailAccount Account(ConnectionSecurity imapSecurity = ConnectionSecurity.None, int? imapPort = null)
    {
        var user = Environment.GetEnvironmentVariable("THIXMAIL_USER") ?? "";
        return new MailAccount
        {
            Id = "real",
            Name = "GreenMail",
            SenderName = "Anna Test",
            Address = user,
            Imap = new ServerSettings(Host!, imapPort ?? Port("THIXMAIL_IMAP_PORT"), imapSecurity),
            ImapUser = user,
            Smtp = new ServerSettings(Host!, Port("THIXMAIL_SMTP_PORT"), ConnectionSecurity.None),
            SmtpUser = user,
        };
    }

    private static string Password => Environment.GetEnvironmentVariable("THIXMAIL_PASSWORD") ?? "";

    [Fact]
    public async Task A_real_server_signs_in_sends_syncs_flags_moves_and_deletes()
    {
        if (Host is null)
        {
            return;
        }

        using var data = new TestData();
        var account = Account();
        var test = await AccountService.TestAsync(MailKitTransport.Instance, account, Password, Password, CancellationToken.None);
        Assert.True(test.Succeeded, test.Message); // (GreenMail's test setup accepts any password: the refusal is covered by SampleMailServer)

        using var store = MailStore.Open(Path.Combine(data.Folder, "real.db"));
        using var accounts = AccountStore.Open(Path.Combine(data.Folder, "real-settings.db"));
        accounts.Save(account);
        AccountService.SetPassword(accounts, account.Id, AccountService.ImapSecret, Password);
        await using var sync = new MailSync(store, account, MailKitTransport.Instance, (a, kind) => AccountService.Password(accounts, a, kind));
        var session = new MailSession(store, accounts, _ => { });
        await sync.SyncOnceAsync();
        var inbox = store.FolderByRole(account.Id, FolderRole.Inbox) ?? throw new InvalidOperationException("no INBOX");

        // send to ourselves through SMTP, then it arrives by IMAP
        var subject = $"ThixMail-Test {Guid.NewGuid():N}";
        session.Send(new Draft
        {
            AccountId = account.Id,
            To = [new MailAddress("Anna Test", account.Address)],
            Subject = subject,
            Html = "<p>Grüße mit <b>Umlauten</b> äöü</p>",
            Attachments = [new DraftAttachment("Übersicht.txt", "text/plain", "Inhalt"u8.ToArray())],
        });
        await sync.SyncOnceAsync();
        Assert.Empty(store.Outbox());
        MessageHeader? arrived = null;
        for (var round = 0; round < 20 && arrived is null; round++)
        {
            await sync.SyncOnceAsync();
            arrived = store.Headers([.. store.List(new MessageQuery([inbox.Id])).Select(e => e.Id)]).FirstOrDefault(h => h.Subject == subject);
            if (arrived is null)
            {
                await Task.Delay(250);
            }
        }

        Assert.NotNull(arrived);
        Assert.True(arrived!.HasAttachments);
        var mail = MailReader.Read((await sync.FetchBodyAsync(arrived.Id, CancellationToken.None))!);
        Assert.Contains("Umlauten äöü", mail.Text, StringComparison.Ordinal);
        Assert.Equal("Übersicht.txt", mail.Attachments.Single().FileName);

        // flags, a new folder, a move, delete for good
        session.SetRead([arrived.Id], true);
        session.SetFlagged([arrived.Id], true);
        var folderId = session.CreateFolder(account.Id, null, "ThixMailTest" + Guid.NewGuid().ToString("N")[..6]);
        await sync.SyncOnceAsync();
        Assert.True(store.Header(arrived.Id)!.IsFlagged);
        session.Move([arrived.Id], folderId);
        await sync.SyncOnceAsync();
        await sync.SyncOnceAsync();
        var moved = store.List(new MessageQuery([folderId])).Select(e => store.Header(e.Id)!).Single();
        Assert.Equal(subject, moved.Subject);
        Assert.True(moved.IsRead && moved.IsFlagged);
        session.EmptyFolder(folderId);
        await sync.SyncOnceAsync();
        await sync.SyncOnceAsync();
        Assert.Empty(store.List(new MessageQuery([folderId])));
        session.DeleteFolder(folderId);
        await sync.SyncOnceAsync();
        Assert.Null(store.Folder(folderId));
    }

    [Fact]
    public async Task A_real_self_signed_certificate_is_refused_until_it_is_trusted()
    {
        if (Host is null || Port("THIXMAIL_IMAPS_PORT") == 0)
        {
            return;
        }

        var account = Account(ConnectionSecurity.SslOnConnect, Port("THIXMAIL_IMAPS_PORT"));
        var refused = await AccountService.TestAsync(MailKitTransport.Instance, account, Password, Password, CancellationToken.None);
        Assert.Equal(MailErrorKind.Certificate, refused.Kind);
        Assert.NotNull(refused.Certificate);
        Assert.Equal(64, refused.Certificate!.Thumbprint.Length);
        var trusted = AccountService.TrustCertificate(account, refused.Certificate);
        var accepted = await AccountService.TestAsync(MailKitTransport.Instance, trusted, Password, Password, CancellationToken.None);
        Assert.True(accepted.Succeeded, accepted.Message);
    }
}
