using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixOffice.Text.Skia;
using ThixWrite.Editor;

namespace ThixWrite.Pages;

/// <summary>
/// Datei › Exportieren: "Als PDF exportieren" (the pages exactly as laid out, fonts embedded, text stays text – PdfExport
/// through Skia). Printing is the page Drucken (<see cref="PrintPage"/>).
/// </summary>
public sealed class ExportPage : UserControl
{
    public const string PageId = "export";

    private readonly OfficeWindow _window;
    private readonly InfoBar _message = new();

    public ExportPage(OfficeWindow window)
    {
        _window = window;
        var title = new TextBlock { Text = "Exportieren" }.Themed("MatText.PageTitle");
        ExportButton = Tile("export", "Als PDF exportieren", "Die Seiten genau wie im Dokument – Schriften eingebettet, Text bleibt durchsuchbar");
        ExportButton.Click += async (_, _) => await ExportPdfAsync();
        AutomationProperties.SetName(ExportButton, "Als PDF exportieren");

        Content = new ScrollViewer
        {
            VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto,
            Content = new StackPanel
            {
                Margin = new Thickness(32, 26),
                Spacing = 16,
                MaxWidth = 900,
                HorizontalAlignment = HorizontalAlignment.Left,
                Children = { title, ExportButton, _message },
            },
        };
    }

    /// <summary>"Als PDF exportieren" (tests click it).</summary>
    public Button ExportButton { get; }

    /// <summary>The result of the last export (tests).</summary>
    public InfoBar Message => _message;

    /// <summary>The backstage entry of the page.</summary>
    public static BackstageItem Item(OfficeWindow window) => new(PageId, "Exportieren", "export", () => new ExportPage(window));

    /// <summary>Asks where, writes the PDF and says where it is; false when cancelled or refused.</summary>
    public async Task<bool> ExportPdfAsync()
    {
        if (_window.Editor is not WriteEditor editor)
        {
            return false;
        }

        var name = Path.ChangeExtension(DocumentService.SuggestedFileName(_window.Document), ".pdf");
        var target = await OfficeApplication.FileDialogs.PickExportTargetAsync(_window, name, DocumentService.SuggestedFolder(_window.Document), "PDF-Dokument", ".pdf");
        if (target is null)
        {
            return false;
        }

        try
        {
            var metadata = new PdfMetadata(Title: Path.GetFileNameWithoutExtension(_window.Document.Title));
            var written = DocumentService.Export(target, ".pdf", stream => PdfExport.Export(editor.Layout.GetDisplayLists(), stream, metadata, WriteFonts.Measurer));
            _message.Show($"Exportiert: {written}", InfoSeverity.Success);
            return true;
        }
        catch (BusinessException exception)
        {
            _message.Show(exception.Message, InfoSeverity.Error);
            return false;
        }
    }

    private static Button Tile(string icon, string title, string text)
    {
        var content = new Grid
        {
            ColumnDefinitions = new ColumnDefinitions("Auto,*"),
            Children =
            {
                new MatIcon { Icon = icon, Size = 28, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center }.Use(MatIcon.ForegroundProperty, "Mat.Accent"),
            },
        };
        var texts = new StackPanel
        {
            Spacing = 2,
            Children =
            {
                new TextBlock { Text = title, FontWeight = FontWeight.SemiBold },
                new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap }.Themed("MatText.Muted"),
            },
        };
        Grid.SetColumn(texts, 1);
        content.Children.Add(texts);
        return new Button
        {
            Content = content,
            Padding = new Thickness(14, 12),
            MinWidth = 260,
            MaxWidth = 460,
            HorizontalAlignment = HorizontalAlignment.Left,
            HorizontalContentAlignment = HorizontalAlignment.Left,
        };
    }
}
