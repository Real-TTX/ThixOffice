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
    public const string PrintScene = "backstage-print";
    public const string TableScene = "main-table";
    public const string PictureScene = "main-picture";
    public const string HeaderScene = "main-header";
    public const string LayoutScene = "main-layout";
    public const string ViewScene = "main-view";
    public const string ReferencesScene = "main-references";
    public const string CommentsScene = "main-comments";
    public const string SpellingScene = "main-spelling";
    public const string WordFloatingScene = "main-word-floating";
    public const string WordLetterScene = "main-word-letter";
    public const string SpellingDialogScene = "dialog-spelling";
    public const string DesignScene = "main-design";

    public override OfficeAppDefinition Definition => ThixWriteApp.Definition;

    /// <summary>ThixWrite underlines unknown words while the user types (Einstellungen › Allgemein can turn it off).</summary>
    public override bool ChecksSpelling => true;

    public override IReadOnlyList<string> SnapshotScenes => [LayoutScene, ViewScene, TableScene, PictureScene, HeaderScene, ReferencesScene, CommentsScene, SpellingScene, SpellingDialogScene, FindScene, PageSetupScene, PrintScene, ExportScene, WordFloatingScene, WordLetterScene, DesignScene];

    public override MatRibbonDefinition CreateRibbon(OfficeWindow window) => WriteRibbon.Create(window);

    /// <summary>The grid of Einfügen › Tabelle.</summary>
    public override Control? CreateRibbonContent(OfficeWindow window, MatRibbonCustom item) =>
        item.Id == WriteCommandIds.TablePicker
            ? new ThixOffice.Desktop.Controls.TablePicker(window, WriteCommandIds.TablePicker, (rows, columns) => (window.Editor as WriteEditor)?.Session.InsertTable(rows, columns), WriteCommandIds.InsertTable)
            : null;

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
        TableCommands.Add(window);
        PictureCommands.Add(window);
        ShapeCommands.Add(window);
        InsertCommands.Add(window);
        HomeCommands.Add(window);
        DesignCommands.Add(window);
        HeaderFooterCommands.Add(window);
        LinkCommands.Add(window);
        TextCommands.Add(window);
        ReferenceCommands.Add(window);
        ReviewCommands.Add(window);
        LayoutCommands.Add(window);
        // Layout › Umbrüche › Seite: the Seitenumbruch of Einfügen in a second place.
        window.Commands.Add(new ThixOffice.Core.Commands.OfficeCommand($"{WriteCommandIds.PageBreak}.layout", "Seitenumbruch", () => window.Commands.ExecuteAsync(WriteCommandIds.PageBreak))
        {
            Icon = "page-break", IsSearchable = false, CanExecute = () => window.Commands.Find(WriteCommandIds.PageBreak)?.IsEnabled == true,
        });
        window.Commands.Add(new ThixOffice.Core.Commands.OfficeCommand(WriteCommandIds.Print, "Drucken", () => window.ShowBackstage(PrintPage.PageId))
        {
            Icon = "print",
            Shortcut = "Ctrl+P",
            Description = "Druckt das Dokument – mit Vorschau, Druckerwahl, Exemplaren und Seitenauswahl.",
            Keywords = ["drucken", "ausdrucken", "drucker", "papier", "vorschau"],
        });
        window.Commands.Add(new ThixOffice.Core.Commands.OfficeCommand(WriteCommandIds.ExportPdf, "Als PDF exportieren", () => window.ShowBackstage(ExportPage.PageId))
        {
            Icon = "export",
            Description = "Speichert die Seiten als PDF-Datei – zum Weitergeben und Archivieren.",
            Keywords = ["pdf", "exportieren", "weitergeben"],
        });
    }

    public override IEnumerable<BackstageItem> CreateBackstageItems(OfficeWindow window) => [PrintPage.Item(window), ExportPage.Item(window)];

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
            case WordFloatingScene or WordLetterScene:
                // Documents as Word writes them (from the real-world corpus): floating pictures, shapes, text boxes; a DIN 5008 letter.
                var resource = name == WordFloatingScene ? "corpus-floating.docx" : "corpus-letter.docx";
                using (var stream = typeof(App).Assembly.GetManifestResourceStream(resource)!)
                {
                    var word = new WordDocument(name == WordFloatingScene ? "Bilder und Formen" : "Brief", ThixWrite.Core.Formats.Docx.DocxReader.Read(stream));
                    window.ReplaceDocument(word);
                }

                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return window;
            case DesignScene:
                // Entwurf open: the design Facette and the watermark VERTRAULICH on the sample report.
                window.Ribbon.SelectTab("design");
                editor.Session.ApplyDesign(ThixWrite.Core.Model.DocumentDesign.Find("Facette")!);
                editor.Session.SetWatermark(new ThixWrite.Core.Model.Watermark { Text = "VERTRAULICH", FontFamily = editor.Session.Document.Theme.MinorFont });
                editor.ZoomToWholePage();
                return window;
            case LayoutScene:
                window.Ribbon.SelectTab("layout");
                editor.ShowMarks = true;
                return window;
            case ViewScene:
                window.Ribbon.SelectTab("view");
                editor.ShowRuler = true;
                editor.ZoomToMultiplePages(); // Ansicht › Mehrere Seiten
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
            case PrintScene:
                window.ShowBackstage(PrintPage.PageId);
                return window;
            case ReferencesScene:
                // Referenzen open, the foot of the first page with its footnote in view.
                window.Ribbon.SelectTab("references");
                editor.ShowNavigation = true;
                editor.Layout.Update();
                var foot = editor.Layout.Pages[0].Footnotes.FirstOrDefault()?.Lines[0].Top ?? 0;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                editor.Scroller.Offset = new Avalonia.Vector(0, Math.Max(0, editor.Surface.ToSurface(0, 0, foot, 0, 0).Y - 360));
                return window;
            case SpellingScene:
                // Überprüfen open, a typing error with its red wavy line.
                window.Ribbon.SelectTab("review");
                MakeTypo(editor);
                return window;
            case SpellingDialogScene:
                MakeTypo(editor);
                var spelling = new SpellingDialog(editor);
                _ = spelling.ShowAsync(window);
                return spelling;
            case CommentsScene:
                // Überprüfen open, a comment on "4,8 %" with its card at the right.
                window.Ribbon.SelectTab("review");
                var paragraph = editor.Document.Content.ParagraphAt(SampleDocument.BodyParagraph)!;
                var at = paragraph.Text.IndexOf("4,8 %", StringComparison.Ordinal);
                editor.Session.Select(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, at), new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, at + 5));
                editor.Document.Session.AddComment(ThixOffice.Core.Business.SettingsService.AuthorName, ThixOffice.Core.Business.SettingsService.AuthorInitials,
                    "Ist das schon der Wert nach Abzug der Umtauschfälle?", new DateTime(2026, 11, 18, 9, 41, 0));
                editor.ShowComments = true;
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                return window;
            case HeaderScene:
                // The header of the first page edited in place: the body dimmed, the tab Kopf- und Fußzeile.
                editor.EditHeaderFooter(0, footer: false);
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Ribbon.SelectTab("headerFooterTab");
                return window;
            case PictureScene:
                // The chart selected: its frame and handles, Bildformat open.
                var picture = editor.Document.Content.Blocks.OfType<ThixWrite.Core.Model.Paragraph>().Select((paragraph, index) => (paragraph, index)).First(item => item.paragraph.Inlines.Any(inline => inline is ThixWrite.Core.Model.ImageInline));
                var block = editor.Document.Content.Blocks.ToList().IndexOf(picture.paragraph);
                editor.Session.Select(new ThixWrite.Core.Model.DocumentPosition(block, 0), new ThixWrite.Core.Model.DocumentPosition(block, 1));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Ribbon.SelectTab("pictureFormat");
                return window;
            case TableScene:
                // The caret in the table's last row: the contextual tabs show, Layout (Tabelle) is open.
                var table = ThixWrite.Core.Model.TableSpan.All(editor.Document.Content).First();
                editor.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(table.BlocksOf(3, 1).First, 2));
                Avalonia.Threading.Dispatcher.UIThread.RunJobs();
                window.Ribbon.SelectTab("tableLayout");
                return window;
            default:
                return window;
        }
    }

    /// <summary>"Versandkostn" for "Versandkosten" in the summary (the snapshots' spelling check knows every other word), checked at once.</summary>
    private static void MakeTypo(WriteEditor editor)
    {
        var paragraph = editor.Document.Content.ParagraphAt(SampleDocument.BodyParagraph)!;
        var at = paragraph.Text.IndexOf("Versandkosten", StringComparison.Ordinal);
        editor.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, at + 11));
        editor.Session.DeleteForward();
        editor.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, 0));
        editor.Spelling.CheckNow([0]);
        Avalonia.Threading.Dispatcher.UIThread.RunJobs();
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
            case SpellingScene or SpellingDialogScene:
                if (shown is Window spellingDialog && shown != window)
                {
                    spellingDialog.Close();
                }

                editor.Session.Undo();
                editor.Session.ClearHistory();
                MarkSampleUnchanged(editor.Document);
                editor.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, 0));
                window.Ribbon.SelectTab("home");
                break;
            case CommentsScene:
                editor.Session.Undo();
                editor.Session.ClearHistory();
                MarkSampleUnchanged(editor.Document);
                editor.ShowComments = false;
                editor.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, 0));
                window.Ribbon.SelectTab("home");
                break;
            case ReferencesScene:
                editor.ShowNavigation = false;
                editor.Scroller.Offset = default;
                window.Ribbon.SelectTab("home");
                break;
            case ViewScene:
                editor.ShowRuler = false;
                editor.Zoom = 100;
                window.Ribbon.SelectTab("home");
                break;
            case HeaderScene:
                editor.CloseHeaderFooter();
                editor.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, 0));
                window.Ribbon.SelectTab("home");
                break;
            case TableScene or PictureScene:
                editor.Session.SetCaret(new ThixWrite.Core.Model.DocumentPosition(SampleDocument.BodyParagraph, 0));
                window.Ribbon.SelectTab("home");
                break;
            case FindScene:
                editor.FindBar.Close();
                break;
            case PageSetupScene when shown is Window dialog:
                dialog.Close();
                break;
            case ExportScene or PrintScene:
                window.Backstage.Close();
                break;
        }
    }
}
