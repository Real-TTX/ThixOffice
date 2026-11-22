using System.Diagnostics;
using System.Globalization;
using System.Text;
using MailStore = ThixMail.Core.Store.MailStore;
using MimeKit;
using ThixMail.Core.Accounts;
using ThixMail.Core.Model;
using ThixMail.Core.Store;
using ThixMail.Core.Sync;
using ThixMail.Core.Transport;
using Xunit.Abstractions;

namespace ThixMail.Core.Tests;

/// <summary>
/// A large mailbox against a REAL server (scripts/greenmail-test.ps1 -Large 20000): one folder with THIXMAIL_LARGE_COUNT messages and 60 folders with
/// 50 each are appended over IMAP, then ThixMail's sync runs as it does in the app – the first round (the newest 1,000 per folder), the rounds until
/// everything is here, an unchanged round, new mail, flag changes from another client, search – with times, memory and the store's size written to
/// the test output and to THIXMAIL_LARGE_REPORT. Without the variables it returns at once.
/// </summary>
public sealed class LargeMailboxTests(ITestOutputHelper output)
{
    private const int Folders = 60;
    private const int PerFolder = 50;
    private const string Needle = "Zitronenfalter";

    private static int Count => int.TryParse(Environment.GetEnvironmentVariable("THIXMAIL_LARGE_COUNT"), NumberStyles.None, CultureInfo.InvariantCulture, out var count) ? count : 0;

    private static string Host => Environment.GetEnvironmentVariable("THIXMAIL_IMAP_HOST") ?? "";

    private static int ImapPort => int.Parse(Environment.GetEnvironmentVariable("THIXMAIL_IMAP_PORT") ?? "0", CultureInfo.InvariantCulture);

    private static string User => Environment.GetEnvironmentVariable("THIXMAIL_USER") ?? "";

    private static string Password => Environment.GetEnvironmentVariable("THIXMAIL_PASSWORD") ?? "";

    private readonly StringBuilder _report = new();

    /// <summary>
    /// Writes the big folder as .eml files for GreenMail's preload (THIXMAIL_LARGE_GENERATE=&lt;folder&gt;: &lt;folder&gt;/&lt;user&gt;/INBOX/n.eml) – appending
    /// 20,000 messages one by one over IMAP takes GreenMail far too long; the server reads them at its start.
    /// </summary>
    [Fact]
    public async Task Generate_the_preload()
    {
        if (Environment.GetEnvironmentVariable("THIXMAIL_LARGE_GENERATE") is not { Length: > 0 } folder || Count == 0)
        {
            return;
        }

        var inbox = Path.Combine(folder, User, "INBOX");
        Directory.CreateDirectory(inbox);
        for (var i = 0; i < Count; i++)
        {
            await File.WriteAllBytesAsync(Path.Combine(inbox, $"{i:000000}.eml"), Raw(i, "Posteingang"));
        }
    }

    [Fact]
    public async Task A_large_mailbox_syncs_completely_quickly_and_incrementally()
    {
        if (Count == 0 || Host.Length == 0)
        {
            return;
        }

        var account = new MailAccount
        {
            Id = "large",
            Name = "Groß",
            SenderName = "Anna Test",
            Address = User,
            Imap = new ServerSettings(Host, ImapPort, ConnectionSecurity.None),
            ImapUser = User,
            Smtp = new ServerSettings(Host, int.Parse(Environment.GetEnvironmentVariable("THIXMAIL_SMTP_PORT") ?? "0", CultureInfo.InvariantCulture), ConnectionSecurity.None),
            SmtpUser = User,
        };

        var clock = Stopwatch.StartNew();
        await FillAsync(account);
        Note($"Server gefüllt: {Count:N0} Nachrichten im Posteingang + {Folders} Ordner × {PerFolder} = {Count + Folders * PerFolder:N0} in {clock.Elapsed.TotalSeconds:0.0} s");

        using var data = new TestData();
        using var store = MailStore.Open(Path.Combine(data.Folder, "large.db"));
        using var accounts = AccountStore.Open(Path.Combine(data.Folder, "large-settings.db"));
        accounts.Save(account);
        AccountService.SetPassword(accounts, account.Id, AccountService.ImapSecret, Password);
        await using var sync = new MailSync(store, account, MailKitTransport.Instance, (a, kind) => AccountService.Password(accounts, a, kind));
        var memoryBefore = Process.GetCurrentProcess().PrivateMemorySize64;

        // the first round: folders listed, the newest 1,000 of every folder – what the person sees first
        clock.Restart();
        var cpu = Cpu();
        var first = await sync.SyncOnceAsync();
        var firstTime = clock.Elapsed;
        var inbox = store.FolderByRole(account.Id, FolderRole.Inbox)!;
        Note($"Erste Runde: {first.NewMessages:N0} Köpfe in {firstTime.TotalSeconds:0.0} s ({store.Folders(account.Id).Count} Ordner; CPU von ThixMail {(Cpu() - cpu).TotalSeconds:0.0} s)");
        Assert.Equal(Math.Min(Count, MailSync.Window) + Folders * PerFolder, first.NewMessages);

        // the rest as the app does it: the loop fetches the older windows round after round (without waiting in IDLE) and reconnects by itself
        clock.Restart();
        cpu = Cpu();
        var dropped = 0;
        sync.Backoff = TimeSpan.FromSeconds(1);
        sync.StatusChanged += (_, status) =>
        {
            if (status.Phase == SyncPhase.Offline)
            {
                Interlocked.Increment(ref dropped);
            }
        };
        sync.Start();
        for (var wait = 0; store.Uids(inbox.Id).Count < Count && wait < 20 * 60 * 4; wait++)
        {
            await Task.Delay(250);
        }

        await sync.StopAsync();
        var completeTime = clock.Elapsed + firstTime;
        Assert.Equal(Count, store.Uids(inbox.Id).Count);
        Note($"Vollständig durch die Schleife: {completeTime.TotalSeconds:0.0} s gesamt (CPU von ThixMail {(Cpu() - cpu).TotalSeconds:0.0} s; Verbindungsabbrüche des Servers: {dropped})");

        // an unchanged round (what the loop does every 10 minutes)
        clock.Restart();
        cpu = Cpu();
        var unchanged = await sync.SyncOnceAsync();
        Note($"Unveränderte Runde: {clock.Elapsed.TotalMilliseconds:0} ms, CPU {(Cpu() - cpu).TotalMilliseconds:0} ms ({unchanged.NewMessages} neu, {unchanged.FlagChanges} Kennzeichen)");
        Assert.Equal(0, unchanged.NewMessages);

        // new mail and flag changes from another client
        await using (var other = await MailKitTransport.Instance.ConnectImapAsync(account, Password, CancellationToken.None))
        {
            for (var i = 0; i < 25; i++)
            {
                await other.AppendAsync("INBOX", Raw(Count + i, "Neu"), Model.MessageFlags.None, DateTimeOffset.Now, CancellationToken.None);
            }

            var present = await other.UidsAsync("INBOX", CancellationToken.None);
            var uids = present.Take(25).Concat(present.Skip(present.Count - 50).Take(25)).ToList(); // 25 of the oldest, 25 recent ones
            await other.SetFlagsAsync("INBOX", uids, Model.MessageFlags.Flagged, Model.MessageFlags.None, [], [], CancellationToken.None);
        }

        clock.Restart();
        cpu = Cpu();
        var incremental = await sync.SyncOnceAsync();
        Note($"Inkrementell (25 neu, 50 gekennzeichnet bei einem anderen Programm): {clock.Elapsed.TotalMilliseconds:0} ms, CPU {(Cpu() - cpu).TotalMilliseconds:0} ms – {incremental.NewMessages} neu, " +
             $"{store.List(new MessageQuery([inbox.Id], ListFilter.Flagged)).Count} von 50 gekennzeichneten sichtbar (die 25 neueren sofort, die ältesten mit dem wandernden Fenster)");
        Assert.Equal(25, incremental.NewMessages);

        // the list and the search on the whole store
        clock.Restart();
        var all = store.List(new MessageQuery([inbox.Id]));
        Note($"Liste des Posteingangs ({all.Count:N0}): {clock.Elapsed.TotalMilliseconds:0} ms");
        clock.Restart();
        var found = store.List(new MessageQuery([.. store.Folders(account.Id).Select(f => f.Id)], Search: Needle));
        Note($"Suche „{Needle}“ in allen Ordnern: {found.Count} Treffer in {clock.Elapsed.TotalMilliseconds:0} ms");
        Assert.Equal((Count + 499) / 500, found.Count);

        var memoryAfter = Process.GetCurrentProcess().PrivateMemorySize64;
        Microsoft.Data.Sqlite.SqliteConnection.ClearAllPools();
        var size = new FileInfo(Path.Combine(data.Folder, "large.db")).Length;
        Note($"Speicher des Testprozesses: {memoryBefore / 1048576.0:0} MB → {memoryAfter / 1048576.0:0} MB; verwaltet {GC.GetTotalMemory(false) / 1048576.0:0} MB; Datei {size / 1048576.0:0.0} MB");

        if (Environment.GetEnvironmentVariable("THIXMAIL_LARGE_REPORT") is { Length: > 0 } file)
        {
            await File.WriteAllTextAsync(file, _report.ToString());
        }
    }

    private static TimeSpan Cpu() => Process.GetCurrentProcess().TotalProcessorTime;

    private void Note(string line)
    {
        _report.AppendLine(line);
        output.WriteLine(line);
    }

    /// <summary>Fills the server through the same transport (another "client"): the big folder, then the project folders.</summary>
    private async Task FillAsync(MailAccount account)
    {
        if (Environment.GetEnvironmentVariable("THIXMAIL_LARGE_PRELOADED") == "1")
        {
            // GreenMail reads the preload after it opened its ports (and drops a session whose folder grows meanwhile): wait on fresh connections
            for (var wait = 0; wait < 600; wait++)
            {
                try
                {
                    await using var probe = await MailKitTransport.Instance.ConnectImapAsync(account, Password, CancellationToken.None);
                    if ((await probe.OpenAsync("INBOX", CancellationToken.None)).Count >= Count)
                    {
                        break;
                    }
                }
                catch (MailConnectionException)
                {
                }

                await Task.Delay(1000);
            }
        }

        await using var client = await MailKitTransport.Instance.ConnectImapAsync(account, Password, CancellationToken.None);
        var date = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(1));
        var preloaded = Environment.GetEnvironmentVariable("THIXMAIL_LARGE_PRELOADED") == "1";
        if (preloaded)
        {
            // GreenMail reads the preload after it opened its ports: wait for all of it; then 70 % read, as people's mailboxes are
            var uids = await client.UidsAsync("INBOX", CancellationToken.None);
            Assert.Equal(Count, uids.Count);
            await client.SetFlagsAsync("INBOX", [.. uids.Where((_, index) => index % 10 < 7)], Model.MessageFlags.Seen, Model.MessageFlags.None, [], [], CancellationToken.None);
        }

        for (var i = 0; i < Count && !preloaded; i++)
        {
            await client.AppendAsync("INBOX", Raw(i, "Posteingang"), i % 10 < 7 ? Model.MessageFlags.Seen : Model.MessageFlags.None, date.AddMinutes(-37 * (Count - i)), CancellationToken.None);
            if (i % 2000 == 1999)
            {
                output.WriteLine($"… {i + 1:N0}");
            }
        }

        var projects = await client.CreateFolderAsync(null, "Projekte", CancellationToken.None);
        for (var f = 0; f < Folders; f++)
        {
            var path = await client.CreateFolderAsync(projects, $"P{f:00}", CancellationToken.None);
            for (var i = 0; i < PerFolder; i++)
            {
                await client.AppendAsync(path, Raw(i, $"P{f:00}"), Model.MessageFlags.Seen, date.AddDays(-i), CancellationToken.None);
            }
        }
    }

    private static byte[] Raw(int number, string where)
    {
        using var stream = new MemoryStream();
        Message(number, where).WriteTo(stream);
        return stream.ToArray();
    }

    private static readonly string[] Senders = ["Jonas Weber", "Lena Koch", "Mehmet Yılmaz", "Sofia Rossi", "Paul Schäfer", "Clara Neumann"];

    private static readonly string[] Words = ["Angebot", "Rechnung", "Termin", "Projekt", "Bericht", "Freigabe", "Entwurf", "Abstimmung", "Lieferung", "Rückfrage"];

    private static MimeMessage Message(int number, string where)
    {
        var sender = Senders[number % Senders.Length];
        var message = new MimeMessage
        {
            Subject = $"{Words[number % Words.Length]} {number}{(number % 500 == 0 && where == "Posteingang" ? " " + Needle : "")} ({where})",
            Date = new DateTimeOffset(2026, 11, 20, 12, 0, 0, TimeSpan.FromHours(1)).AddMinutes(-37 * (Count - number)),
            MessageId = $"<large-{where}-{number}@thix.test>",
        };
        message.From.Add(new MailboxAddress(sender, sender.Split(' ')[0].ToLowerInvariant() + "@beispiel.de"));
        message.To.Add(new MailboxAddress("Anna Test", User));
        message.Body = new TextPart("plain")
        {
            Text = $"Hallo Anna,\n\nanbei {Words[(number + 3) % Words.Length]} Nummer {number}. Viele Grüße\n{sender}\n" + new string('.', 400 + number % 1200),
        };
        return message;
    }
}
