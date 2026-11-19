using System.Text;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixWrite.Core;
using ThixWrite.Core.Documents;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite.Tests;

/// <summary>ThixWrite's definition, its new documents and the formats through the business layer.</summary>
public sealed class DocumentTests
{
    [Fact]
    public void ThixWrite_works_on_docx_odt_and_txt_and_declares_rtf_as_planned()
    {
        var definition = ThixWriteApp.Definition;

        Assert.Equal("thixwrite", definition.Id);
        Assert.Equal("#185ABD", definition.Accent);
        Assert.Equal("Write", definition.Suffix);
        Assert.Equal(".docx", definition.DefaultType.Extension);
        Assert.Equal(DocumentTypeSupport.Supported, definition.DefaultType.Support);
        Assert.Equal([".docx", ".odt", ".txt"], definition.SupportedTypes.Select(type => type.Extension));
        Assert.Equal(".docx", definition.SaveType.Extension);
        Assert.All(definition.DocumentTypes.Where(type => type.Extension is ".rtf"), type => Assert.False(type.IsSupported));
        Assert.Equal("thixwrite-update.json", definition.ManifestFileName);
        Assert.Equal("ThixWriteSetup.exe", definition.SetupFileName);
        Assert.Equal("info.schmoldt.thixwrite", definition.MacBundleId);
        Assert.Equal(@"Software\Microsoft\Windows\CurrentVersion\Uninstall\ThixWrite", definition.UninstallKey);
    }

    [Fact]
    public void New_documents_take_the_first_preferred_font_the_machine_has()
    {
        Assert.Equal(new DocumentFonts("Aptos", "Aptos Display"), DocumentFonts.Choose(["Arial", "Aptos", "Aptos Display", "Calibri"]));
        Assert.Equal(new DocumentFonts("Calibri", "Calibri Light"), DocumentFonts.Choose(["arial", "calibri", "calibri light"]));
        Assert.Equal(new DocumentFonts("Aptos", "Aptos"), DocumentFonts.Choose(["Aptos"]));
        Assert.Equal(new DocumentFonts("Helvetica Neue", "Helvetica Neue"), DocumentFonts.Choose(["Helvetica Neue", "Arial"]));
        Assert.Equal(new DocumentFonts("Arial", "Arial"), DocumentFonts.Choose(["Times New Roman"]));

        var document = BlankDocument.Create(new DocumentFonts("Helvetica Neue", "Avenir"));
        Assert.Equal("Helvetica Neue", document.Styles.CharacterDefaults.FontFamily);
        Assert.Equal("Avenir", document.Styles.Find(BuiltInStyles.Heading1)!.Character.FontFamily);
        Assert.Equal("Avenir", document.Styles.Find(BuiltInStyles.Title)!.Character.FontFamily);
        Assert.DoesNotContain(document.Styles.Styles, style => style.Character.FontFamily is BuiltInStyles.BodyFont or BuiltInStyles.HeadingFont);
        Assert.Equal(595.3, document.FinalSection.PageWidth, 1);
        Assert.Equal(70.85, document.FinalSection.MarginLeft, 1); // 2.5 cm
        Assert.Contains(WriteFonts.Default.Body, DocumentFonts.Preferred.Select(fonts => fonts.Body));
    }

    [Fact]
    public void A_text_file_is_read_with_its_encoding_and_written_back_the_same_way()
    {
        using var data = new TestData();
        var withBom = Path.Combine(data.DocumentsFolder, "bom.txt");
        File.WriteAllText(withBom, "Grüße aus Köln", new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));

        var document = DocumentService.Open(withBom, name => new WordDocument(name));
        Assert.Equal("Grüße aus Köln", document.Content.GetPlainText().TrimEnd());
        Assert.Equal(WordFileFormat.PlainText, document.SaveFormat);

        document.Session.Move(ThixWrite.Core.Editing.CaretMovement.DocumentEnd);
        document.Session.InsertText("!");
        DocumentService.Save(document);
        Assert.Equal("Grüße aus Köln!", File.ReadAllText(withBom).TrimEnd());
    }

    [Fact]
    public void A_docx_saved_as_txt_and_back_as_docx_writes_each_format()
    {
        using var data = new TestData();
        var document = new WordDocument("Dokument1", SampleDocument.Create(new DocumentFonts("Arial", "Arial")));
        var text = Path.Combine(data.DocumentsFolder, "Bericht.txt");
        var docx = Path.Combine(data.DocumentsFolder, "Bericht.docx");

        DocumentService.SaveAs(document, text);
        Assert.StartsWith("Projektbericht November", File.ReadAllText(text));
        DocumentService.SaveAs(document, docx);
        Assert.Equal((byte)'P', File.ReadAllBytes(docx)[0]);
        Assert.Equal(WordFileFormat.Docx, document.SaveFormat);

        var reopened = DocumentService.Open(docx, name => new WordDocument(name));
        Assert.True(reopened.Content.ContentEquals(document.Content));
    }
}
