using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using MatDesktop;
using ThixOffice.Core.Documents;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core;
using ThixWrite.Core.Documents;
using ThixWrite.Dialogs;
using ThixWrite.Editor;
using ThixWrite.Pages;

namespace ThixWrite;

/// <summary>
/// ThixWrite on ThixOffice.Desktop: the definition (ThixWriteApp), the document (<see cref="WordDocument"/>: docx and plain
/// text), the editor (<see cref="WriteEditor"/>), ThixWrite's commands and ribbon, the backstage page Drucken und Exportieren,
/// its self test and snapshot scenes. Start-up, single instance, setup, updates, the window, the backstage and the settings
/// are OfficeApplication's.
/// </summary>
public partial class App : OfficeApplication
{
    public const string FindScene = "main-find";
    public const string PageSetupScene = "dialog-page-setup";
    public const string ExportScene = "backstage-export";

    public override OfficeAppDefinition Definition => ThixWriteApp.Definition;

    public override IReadOnlyList<string> SnapshotScenes => [FindScene, PageSetupScene, ExportScene];

    public override MatRibbonDefinition CreateRibbon(OfficeWindow window) => WriteRibbon.Create(window);

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        base.Initialize();
    }

    /// <summary>A new document: Word's styles and A4 with the fonts this machine has (<see cref="WriteFonts.Default"/>).</summary>
    public override OfficeDocument CreateDocument(string untitledName) => new WordDocument(untitledName, BlankDocument.Create(WriteFonts.Default));

    public override OfficeEditor CreateEditor(OfficeWindow window, OfficeDocument document) => new WriteEditor(window, (WordDocument)document);

    public override void AddCommands(OfficeWindow window)
    {
        WriteCommands.Add(window);
        window.Commands.Add(new ThixOffice.Core.Commands.OfficeCommand(WriteCommandIds.ExportPdf, "Als PDF exportieren", () => window.Backstage.Open(ExportPage.PageId))
        {
            Icon = "export",
            Shortcut = "Ctrl+P",
            Description = "Speichert die Seiten als PDF-Datei – zum Weitergeben und Drucken.",
            Keywords = ["pdf", "drucken", "exportieren", "ausdrucken"],
        });
    }

    public override IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) => [ExportPage.Item(window)];

    public override IEnumerable<(string Name, Func<Task<string>> Check)> SelfTestChecks() => [("ThixWrite editor, docx, PDF", () => WriteSelfTest.RunAsync(this))];

    protected override void FillSampleDocument(OfficeDocument document) =>
        ((WordDocument)document).Load(SampleDocument.Create(WriteFonts.Default));

    public override TopLevel ShowSnapshotScene(OfficeWindow window, string name)
    {
        var editor = (WriteEditor)window.Editor;
        switch (name)
        {
            case FindScene:
                editor.FindBar.Open(replace: true);
                editor.FindBar.SearchBox.Text = "Versandkosten";
                editor.FindBar.ReplacementBox.Text = "Lieferkosten";
                return window;
            case PageSetupScene:
                var dialog = new PageSetupDialog(editor);
                _ = dialog.ShowAsync(window);
                return dialog;
            case ExportScene:
                window.Backstage.Open(ExportPage.PageId);
                return window;
            default:
                return window;
        }
    }

    public override void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
        switch (name)
        {
            case FindScene:
                ((WriteEditor)window.Editor).FindBar.Close();
                break;
            case PageSetupScene when shown is Window dialog:
                dialog.Close();
                break;
            case ExportScene:
                window.Backstage.Close();
                break;
        }
    }
}
