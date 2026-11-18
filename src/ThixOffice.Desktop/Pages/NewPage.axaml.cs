using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ThixOffice.Core.Infrastructure;
using ThixOffice.Desktop.Shell;

namespace ThixOffice.Desktop.Pages;

/// <summary>Datei › Neu: an empty document in a window of its own (Strg+N does the same).</summary>
public partial class NewPage : UserControl
{
    private readonly OfficeWindow? _window;

    /// <summary>For the XAML previewer.</summary>
    public NewPage()
    {
        InitializeComponent();
    }

    public NewPage(OfficeWindow window)
        : this()
    {
        _window = window;
        var definition = AppInfo.Definition;
        BlankLabel.Text = definition.BlankDocumentLabel;
        AutomationProperties.SetName(BlankButton, definition.BlankDocumentLabel);
        ToolTip.SetTip(BlankButton, Ui.Tooltip(definition.BlankDocumentLabel, "Ctrl+N"));
        Hint.Text = $"Ein neues Dokument öffnet sich in einem eigenen Fenster. Gespeichert wird es als {definition.SaveType.Description} ({definition.SaveType.Extension}).";
    }

    private void OnBlankClick(object? sender, RoutedEventArgs e)
    {
        _window?.Backstage.Close();
        OfficeApplication.Instance.NewDocumentWindow();
    }
}
