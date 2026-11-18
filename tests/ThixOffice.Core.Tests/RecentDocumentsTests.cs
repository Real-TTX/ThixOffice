using ThixOffice.Core.Business;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests;

/// <summary>"Zuletzt verwendet": order, pins, the length of the list, files that are gone, search and sort.</summary>
public sealed class RecentDocumentsTests
{
    [Fact]
    public void The_document_used_last_comes_first_and_is_listed_once()
    {
        using var data = new TestData();
        var a = data.File("a.tst");
        var b = data.File("b.tst");
        var now = DateTimeOffset.Now;

        RecentDocuments.Add(a, now.AddMinutes(-2));
        RecentDocuments.Add(b, now.AddMinutes(-1));
        RecentDocuments.Add(a, now);

        Assert.Equal([a, b], RecentDocuments.List().Select(item => item.Document.Path));
    }

    [Fact]
    public void Pinned_documents_stay_on_top_and_never_fall_out()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { RecentListSize = 2 });
        var pinned = data.File("vertrag.tst");
        RecentDocuments.Add(pinned, DateTimeOffset.Now.AddDays(-30));
        RecentDocuments.Pin(pinned, true);
        for (var index = 0; index < 5; index++)
        {
            RecentDocuments.Add(data.File($"neu{index}.tst"), DateTimeOffset.Now.AddMinutes(index));
        }

        var list = RecentDocuments.List();
        Assert.Equal(3, list.Count); // the pinned one and the newest two
        Assert.Equal(pinned, list[0].Document.Path);
        Assert.True(list[0].Document.IsPinned);
        Assert.Equal("neu4.tst", list[1].Document.FileName);

        RecentDocuments.Add(pinned);
        Assert.True(RecentDocuments.List()[0].Document.IsPinned); // opening it again keeps the pin
        RecentDocuments.Pin(pinned, false);
        Assert.False(RecentDocuments.List().Single(item => item.Document.Path == pinned).Document.IsPinned);
    }

    [Fact]
    public void Removing_an_entry_never_touches_the_file()
    {
        using var data = new TestData();
        var path = data.File("bleibt.tst");
        RecentDocuments.Add(path);

        RecentDocuments.Remove(path);

        Assert.Empty(RecentDocuments.List());
        Assert.True(File.Exists(path));
        Assert.Throws<BusinessException>(() => RecentDocuments.Pin(path, true));
    }

    [Fact]
    public void A_file_that_is_gone_is_marked_until_it_is_removed()
    {
        using var data = new TestData();
        var kept = data.File("da.tst");
        var gone = data.File("weg.tst");
        RecentDocuments.Add(kept);
        RecentDocuments.Add(gone);
        File.Delete(gone);

        var list = RecentDocuments.List();
        Assert.False(list.Single(item => item.Document.Path == gone).Exists);
        Assert.True(list.Single(item => item.Document.Path == kept).Exists);

        Assert.Equal(1, RecentDocuments.RemoveMissing());
        Assert.Equal([kept], RecentDocuments.List().Select(item => item.Document.Path));
    }

    [Fact]
    public void The_list_is_searched_by_name_and_folder_and_sorted_by_name()
    {
        using var data = new TestData();
        RecentDocuments.Add(data.File(Path.Combine("Rechnungen", "Zahlung.tst")));
        RecentDocuments.Add(data.File("Angebot.tst"));
        RecentDocuments.Add(data.File("Brief.tst"));

        Assert.Equal(["Zahlung.tst"], RecentDocuments.List("rechnung").Select(item => item.Document.FileName));
        Assert.Equal(["Angebot.tst", "Brief.tst", "Zahlung.tst"], RecentDocuments.List(order: RecentDocumentOrder.Name).Select(item => item.Document.FileName));
        Assert.Empty(RecentDocuments.List("gibtsnicht"));
    }

    [Fact]
    public void Save_as_offers_the_default_folder_and_the_recent_ones()
    {
        using var data = new TestData();
        SettingsService.Update(settings => settings with { DefaultSaveFolder = data.DocumentsFolder });
        var invoices = data.File(Path.Combine("Rechnungen", "Zahlung.tst"));
        RecentDocuments.Add(invoices);
        RecentDocuments.Add(data.File("Brief.tst"));

        Assert.Equal([data.DocumentsFolder, Path.GetDirectoryName(invoices)!], RecentDocuments.RecentFolders());
    }

    [Fact]
    public void The_list_survives_a_restart()
    {
        using var data = new TestData();
        var path = data.File("a.tst");
        RecentDocuments.Add(path);
        RecentDocuments.Pin(path, true);
        RecentDocuments.ResetCache();

        Assert.True(RecentDocuments.List().Single().Document.IsPinned);
    }
}
