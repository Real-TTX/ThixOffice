using System.Text;
using ThixOffice.Core.Business;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Core.Models;

namespace ThixOffice.Core.Tests;

/// <summary>The bytes on disk: atomic save, backup, a file another program holds, recovery files; and opening / saving documents.</summary>
public sealed class DocumentFilesTests
{
    [Fact]
    public void Saving_writes_the_whole_document_and_leaves_no_temporary_file()
    {
        using var data = new TestData();
        var path = Path.Combine(data.DocumentsFolder, "bericht.tst");

        DocumentFiles.SaveAtomic(path, stream => stream.Write("erste Fassung"u8));
        DocumentFiles.SaveAtomic(path, stream => stream.Write("zweite Fassung"u8));

        Assert.Equal("zweite Fassung", File.ReadAllText(path));
        Assert.Equal(["bericht.tst"], Directory.GetFiles(data.DocumentsFolder).Select(Path.GetFileName));
    }

    [Fact]
    public void A_save_that_fails_halfway_leaves_the_document_as_it_was()
    {
        using var data = new TestData();
        var path = data.File("wichtig.tst", "unversehrt");

        Assert.Throws<InvalidOperationException>(() => DocumentFiles.SaveAtomic(path, stream =>
        {
            stream.Write("halb"u8);
            throw new InvalidOperationException("Absturz beim Schreiben");
        }));

        Assert.Equal("unversehrt", File.ReadAllText(path));
        Assert.Single(Directory.GetFiles(data.DocumentsFolder));
    }

    [Fact]
    public void The_previous_version_can_stay_as_a_backup()
    {
        using var data = new TestData();
        var path = data.File("brief.tst", "alt");

        DocumentFiles.SaveAtomic(path, stream => stream.Write("neu"u8), keepBackup: true);

        Assert.Equal("neu", File.ReadAllText(path));
        Assert.Equal("alt", File.ReadAllText(path + ".bak"));
    }

    [Fact]
    public void A_document_another_program_holds_is_not_overwritten()
    {
        using var data = new TestData();
        var path = data.File("offen.tst", "im anderen Programm");
        using (new FileStream(path, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.True(DocumentFiles.IsLockedByAnotherProgram(path));
            var exception = Assert.Throws<BusinessException>(() => DocumentFiles.SaveAtomic(path, stream => stream.Write("x"u8)));
            Assert.Contains("anderen Programm", exception.Message);
        }

        Assert.False(DocumentFiles.IsLockedByAnotherProgram(path));
        Assert.Equal("im anderen Programm", File.ReadAllText(path));
    }

    [Fact]
    public void A_folder_that_is_gone_is_said_in_words()
    {
        using var data = new TestData();
        var exception = Assert.Throws<BusinessException>(() => DocumentFiles.SaveAtomic(Path.Combine(data.Folder, "weg", "a.tst"), _ => { }));
        Assert.Contains("Ordner", exception.Message);
        Assert.Throws<BusinessException>(() => DocumentFiles.ReadAll(Path.Combine(data.Folder, "fehlt.tst")));
    }

    [Fact]
    public void Recovery_files_are_written_listed_and_removed()
    {
        using var data = new TestData();
        var id = Guid.NewGuid();
        DocumentFiles.WriteRecovery(new RecoveryInfo(id, "Brief.tst", null, ".tst", DateTimeOffset.Now), stream => stream.Write("ungespeichert"u8));

        var info = Assert.Single(DocumentFiles.ListRecovery());
        Assert.Equal("Brief.tst", info.Title);
        using (var content = DocumentFiles.ReadRecovery(id))
        {
            Assert.Equal("ungespeichert", Encoding.UTF8.GetString(content.ToArray()));
        }

        DocumentFiles.DeleteRecovery(id);
        Assert.Empty(DocumentFiles.ListRecovery());
    }

    [Fact]
    public void Save_as_adds_the_save_type_and_refuses_what_the_app_cannot_write()
    {
        using var data = new TestData();
        var document = new TestDocument(DocumentService.NextUntitledName());
        document.Type("Inhalt");
        Assert.Equal("Test1", document.UntitledName);
        Assert.Equal("Test1.tst", DocumentService.SuggestedFileName(document));

        var saved = DocumentService.SaveAs(document, Path.Combine(data.DocumentsFolder, "Ohne Endung"));

        Assert.EndsWith("Ohne Endung.tst", saved);
        Assert.Equal("Inhalt", File.ReadAllText(saved));
        Assert.False(document.IsDirty);
        Assert.Equal(saved, document.FilePath);
        Assert.Equal(saved, RecentDocuments.List()[0].Document.Path);

        var planned = Assert.Throws<BusinessException>(() => DocumentService.SaveAs(document, Path.Combine(data.DocumentsFolder, "a.tsx")));
        Assert.Equal("FileName", planned.Field);
        Assert.Throws<BusinessException>(() => DocumentService.SaveAs(document, " "));
    }

    [Fact]
    public void Opening_reads_the_document_and_puts_it_into_the_recent_list()
    {
        using var data = new TestData();
        var path = data.File("Brief.tst", "Sehr geehrte Damen und Herren");

        var document = DocumentService.Open(path, name => new TestDocument(name));

        Assert.Equal("Sehr geehrte Damen und Herren", document.Content);
        Assert.Equal("Brief.tst", document.Title);
        Assert.False(document.IsDirty);
        Assert.False(document.IsPristine);
        Assert.Equal(path, RecentDocuments.List()[0].Document.Path);

        var unsupported = data.File("Brief.tsx");
        var exception = Assert.Throws<BusinessException>(() => DocumentService.Open(unsupported, name => new TestDocument(name)));
        Assert.Contains("nicht öffnen", exception.Message);
    }

    [Fact]
    public void Save_needs_a_file_first()
    {
        using var data = new TestData();
        var document = new TestDocument(DocumentService.NextUntitledName());
        Assert.True(document.IsPristine);
        Assert.Throws<BusinessException>(() => DocumentService.Save(document));
    }

    [Fact]
    public void A_crash_leaves_unsaved_changes_that_come_back_as_an_unsaved_document()
    {
        using var data = new TestData();
        var document = new TestDocument(DocumentService.NextUntitledName());
        document.Type("Nicht verlieren!");
        DocumentService.WriteRecovery(document);

        var info = Assert.Single(DocumentService.RecoveredDocuments());
        var recovered = DocumentService.Recover(info, name => new TestDocument(name));

        Assert.Equal("Nicht verlieren!", recovered.Content);
        Assert.True(recovered.IsDirty);
        Assert.True(recovered.IsRecovered);
        Assert.Equal("Test1 (wiederhergestellt)", recovered.UntitledName);
        Assert.Empty(DocumentService.RecoveredDocuments());

        // Saving removes the recovery file the window wrote meanwhile.
        DocumentService.WriteRecovery(recovered);
        DocumentService.SaveAs(recovered, Path.Combine(data.DocumentsFolder, "gerettet.tst"));
        Assert.Empty(DocumentService.RecoveredDocuments());
        Assert.False(Directory.Exists(AppPaths.RecoveryDirectory) && Directory.GetFiles(AppPaths.RecoveryDirectory).Length > 0);
    }
}
