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
    public const string LayoutScene = "main-layout";
    public const string ViewScene = "main-view";

    public override OfficeAppDefinition Definition => ThixWriteApp.Definition;

    public override IReadOnlyList<string> SnapshotScenes => [LayoutScene, ViewScene, FindScene, PageSetupScene, ExportScene];

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
        window.Commands.Add(new ThixOffice.Core.Commands.OfficeCommand(WriteCommandIds.ExportPdf, "Als PDF exportieren", () => window.ShowBackstage(ExportPage.PageId))
        {
            Icon = "export",
            Shortcut = "Ctrl+P",
            Description = "Speichert die Seiten als PDF-Datei – zum Weitergeben und Drucken.",
            Keywords = ["pdf", "drucken", "exportieren", "ausdrucken"],
        });
    }

    public override IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) => [ExportPage.Item(window)];

    public override IEnumerable<(string Name, Func<Task<string>> Check)> SelfTestChecks() => [("ThixWrite editor, docx, PDF", () => WriteSelfTest.RunAsync(this))];

    /// <summary>The sample report, the caret at the start of its first body paragraph (Standard, like a user who just clicked there).</summary>
    protected override void FillSampleDocument(OfficeDocument document)
    {
        var word = (WordDocument)document;
        word.Load(SampleDocument.Create(WriteFonts.Default));
        word.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, 0));
    }

    public override TopLevel ShowSnapshotScene(OfficeWindow window, string name)
    {
        var editor = (WriteEditor)window.Editor;
        switch (name)
        {
            case LayoutScene:
                window.Ribbon.SelectTab("layout");
                editor.ShowMarks = true;
                return window;
            case ViewScene:
                window.Ribbon.SelectTab("view");
                editor.ZoomToWholePage();
                return window;
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
                window.ShowBackstage(ExportPage.PageId);
                return window;
            default:
                return window;
        }
    }

    public override void EndSnapshotScene(OfficeWindow window, string name, TopLevel shown)
    {
        var editor = (WriteEditor)window.Editor;
        switch (name)
        {
            case LayoutScene:
                editor.ShowMarks = false;
                window.Ribbon.SelectTab("home");
                break;
            case ViewScene:
                editor.Zoom = 100;
                window.Ribbon.SelectTab("home");
                break;
            case FindScene:
                editor.FindBar.Close();
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
