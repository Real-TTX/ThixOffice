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
using ThixOffice.Text.Drawing;

namespace ThixOffice.Desktop.Pages;

/// <summary>
/// Datei › Exportieren of every app: "Als PDF exportieren" (the pages exactly as laid out, fonts embedded, text stays text – PdfExport
/// through Skia). The app supplies its pages (in points) and the measurer that laid them out. Printing is the page Drucken (<see cref="OfficePrintPage"/>).
/// </summary>
public class OfficeExportPage : UserControl
{
    public const string PageId = "export";

    private readonly OfficeWindow _window;
    private readonly InfoBar _message = new();
    private readonly Func<IReadOnlyList<PageDisplayList>> _pages;
    private readonly SkiaTextMeasurer _measurer;
    private readonly StackPanel _panel;

    public OfficeExportPage(OfficeWindow window, Func<IReadOnlyList<PageDisplayList>> pages, SkiaTextMeasurer measurer, string description = "Die Seiten genau wie im Dokument – Schriften eingebettet, Text bleibt durchsuchbar")
    {
        _window = window;
        _pages = pages;
        _measurer = measurer;
        var title = new TextBlock { Text = "Exportieren" }.Themed("MatText.PageTitle");
        ExportButton = Tile("export", "Als PDF exportieren", description);
        ExportButton.Click += async (_, _) => await ExportPdfAsync();
        AutomationProperties.SetName(ExportButton, "Als PDF exportieren");

        _panel = new StackPanel
        {
            Margin = new Thickness(32, 26),
            Spacing = 16,
            MaxWidth = 900,
            HorizontalAlignment = HorizontalAlignment.Left,
            Children = { title, ExportButton, _message },
        };
        Content = new ScrollViewer { VerticalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto, Content = _panel };
    }

    /// <summary>"Als PDF exportieren" (tests click it).</summary>
    public Button ExportButton { get; }

    /// <summary>The result of the last export (tests).</summary>
    public InfoBar Message => _message;

    /// <summary>An app's further way of exporting (ThixShow: slides as pictures), below "Als PDF exportieren" and above the result.</summary>
    protected void AddSection(Control section) => _panel.Children.Insert(_panel.Children.IndexOf(_message), section);

    /// <summary>Asks where, writes the PDF and says where it is; false when cancelled or refused.</summary>
    public async Task<bool> ExportPdfAsync()
    {
        var name = Path.ChangeExtension(DocumentService.SuggestedFileName(_window.Document), ".pdf");
        var target = await OfficeApplication.FileDialogs.PickExportTargetAsync(_window, name, DocumentService.SuggestedFolder(_window.Document), "PDF-Dokument", ".pdf");
        if (target is null)
        {
            return false;
        }

        try
        {
            var metadata = new PdfMetadata(Title: Path.GetFileNameWithoutExtension(_window.Document.Title));
            var written = DocumentService.Export(target, ".pdf", stream => PdfExport.Export(_pages(), stream, metadata, _measurer));
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
