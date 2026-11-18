using System.Text;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixWrite.Core;
using ThixWrite.Core.Documents;

namespace ThixWrite.Tests;

/// <summary>ThixWrite's definition and its phase-0 document.</summary>
public sealed class DocumentTests
{
    [Fact]
    public void ThixWrite_works_on_txt_and_declares_docx_odt_and_rtf_as_planned()
    {
        var definition = ThixWriteApp.Definition;

        Assert.Equal("thixwrite", definition.Id);
        Assert.Equal("#185ABD", definition.Accent);
        Assert.Equal("Write", definition.Suffix);
        Assert.Equal(".docx", definition.DefaultType.Extension);
        Assert.Equal(DocumentTypeSupport.Planned, definition.DefaultType.Support);
        Assert.Equal([".txt"], definition.SupportedTypes.Select(type => type.Extension));
        Assert.Equal(".txt", definition.SaveType.Extension);
        Assert.All(definition.DocumentTypes.Where(type => type.Extension != ".txt"), type => Assert.False(type.IsSupported));
        Assert.Equal("thixwrite-update.json", definition.ManifestFileName);
        Assert.Equal("ThixWriteSetup.exe", definition.SetupFileName);
        Assert.Equal("info.schmoldt.thixwrite", definition.MacBundleId);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ThixWrite", definition.UninstallKey);
    }

    [Fact]
    public void Typing_in_a_row_is_one_undo_step_and_redo_brings_it_back()
    {
        var document = new TextDocument("Dokument1");
        var start = new DateTime(2026, 11, 18, 10, 0, 0, DateTimeKind.Utc);

        document.SetText("H", isTyping: true, now: start);
        document.SetText("Ha", isTyping: true, now: start.AddMilliseconds(300));
        document.SetText("Hal", isTyping: true, now: start.AddMilliseconds(600));
        document.SetText("Hallo", isTyping: true, now: start.AddSeconds(5)); // after a pause: a step of its own

        Assert.True(document.IsDirty);
        document.Undo();
        Assert.Equal("Hal", document.Text);
        document.Undo();
        Assert.Equal("", document.Text);
        Assert.False(document.CanUndo);
        document.Redo();
        document.Redo();
        Assert.Equal("Hallo", document.Text);
        Assert.False(document.CanRedo);
    }

    [Fact]
    public void Words_and_characters_are_counted()
    {
        var document = new TextDocument("Dokument1");
        document.Load("Zwei Wörter\nund drei");
        Assert.Equal((4, 19), document.Count());
        Assert.False(document.IsDirty);
        Assert.False(document.CanUndo);
    }

    [Fact]
    public void Text_files_are_read_with_or_without_BOM_and_written_as_UTF8()
    {
        using var data = new TestData();
        var withBom = Path.Combine(data.DocumentsFolder, "bom.txt");
        File.WriteAllText(withBom, "Grüße aus Köln", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var document = DocumentService.Open(withBom, name => new TextDocument(name));
        Assert.Equal("Grüße aus Köln", document.Text);

        document.SetText(document.Text + "!");
        DocumentService.Save(document);
        var bytes = File.ReadAllBytes(withBom);
        Assert.NotEqual(0xEF, bytes[0]);
        Assert.Equal("Grüße aus Köln!", Encoding.UTF8.GetString(bytes));
    }

    [Fact]
    public void A_Word_document_is_never_opened_into_the_placeholder()
    {
        using var data = new TestData();
        var docx = data.File("Vertrag.docx", "PK…");
        var exception = Assert.Throws<BusinessException>(() => DocumentService.Open(docx, name => new TextDocument(name)));
        Assert.Contains("(noch) nicht öffnen", exception.Message);
        Assert.Throws<BusinessException>(() => DocumentService.SaveAs(new TextDocument("x"), Path.Combine(data.DocumentsFolder, "x.docx")));
    }
}
