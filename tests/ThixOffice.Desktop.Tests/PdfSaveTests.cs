using System.Text;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Interactivity;
using ThixOffice.Core.Business;
using ThixOffice.Core.Commands;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Tests;

/// <summary>
/// PDF where Word users look for it: "PDF (*.pdf)" in Speichern unter, the searchable command "Als PDF speichern" – a copy, the
/// document keeps its file, type and unsaved state; the message bar says where it went and opens it.
/// </summary>
public sealed class PdfSaveTests
{
    [AvaloniaFact]
    public async Task Speichern_unter_as_PDF_writes_a_copy_and_the_document_keeps_its_file_and_changes()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        var own = Path.Combine(data.DocumentsFolder, "Brief.tst");
        ((TestEditor)window.Editor).Box.Text = "Hallo";
        TestData.Settle();
        Assert.True(await window.SaveToAsync(own));
        ((TestEditor)window.Editor).Box.Text = "Hallo Welt";
        TestData.Settle();
        Assert.True(window.Document.IsDirty);

        var pdf = Path.Combine(data.DocumentsFolder, "Brief.pdf");
        Assert.True(await window.SaveToAsync(pdf));
        TestData.Settle();

        Assert.StartsWith("%PDF", Encoding.ASCII.GetString(File.ReadAllBytes(pdf), 0, 4));
        Assert.Equal(own, window.Document.FilePath);
        Assert.Equal(".tst", window.Document.FileType!.Extension);
        Assert.True(window.Document.IsDirty);
        Assert.Equal("Hallo", File.ReadAllText(own));
        Assert.DoesNotContain(RecentDocuments.List(), entry => entry.Document.Path == pdf);

        Assert.True(window.MessageBar.IsVisible);
        Assert.Contains("Als PDF gespeichert", window.MessageBar.Text);
        Assert.True(window.MessageBar.ActionButton.IsVisible);
        window.MessageBar.ActionButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Assert.Equal([pdf], data.Opened);
    }

    [AvaloniaFact]
    public async Task Als_PDF_speichern_is_a_searchable_command_and_a_tile_of_Speichern_unter()
    {
        using var data = new TestData();
        var window = TestData.App.NewDocumentWindow();
        ((TestEditor)window.Editor).Box.Text = "Bericht";
        TestData.Settle();

        Assert.Contains(window.Commands.Search("pdf"), command => command.Id == CommonCommandIds.SaveAsPdf);
        var target = Path.Combine(data.DocumentsFolder, "Bericht"); // without extension: .pdf is added
        data.Dialogs.SaveTargets.Enqueue(target);
        Assert.True(await window.Commands.ExecuteAsync(CommonCommandIds.SaveAsPdf));
        Assert.True(File.Exists(target + ".pdf"));
        Assert.Null(window.Document.FilePath);
        Assert.True(window.Document.IsDirty);

        window.Backstage.Open(OfficeWindow.SaveAsPageId);
        var page = (SaveAsPage)window.Backstage.CurrentPage!;
        page.ShowPlace(DocumentPlace.Computer);
        TestData.Settle();
        Assert.True(page.PdfButton.IsVisible);
    }

    [AvaloniaFact]
    public void The_business_layer_knows_PDF_as_an_export_type_only()
    {
        using var data = new TestData();
        Assert.Equal(DocumentType.Pdf, DocumentService.ExportTypeOf("C:/x/Bericht.PDF"));
        Assert.Null(DocumentService.ExportTypeOf("Bericht.tst"));
        Assert.Null(TestOfficeApp.TestDefinition.SupportedTypeOf("Bericht.pdf")); // never opened
        Assert.Equal(".pdf", TestOfficeApp.TestDefinition.SaveAsTypes[^1].Extension);
        var refusal = Assert.Throws<BusinessException>(() => DocumentService.TargetPath(Path.Combine(data.DocumentsFolder, "x.doc")));
        Assert.Contains(".pdf", refusal.Message);
    }
}
