using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Shell;
using ThixOffice.Desktop.Rendering;

namespace ThixOffice.Desktop.Pages;

/// <summary>What an app prints: its pages now, the page of the caret, its own choices (ThixCalc: Aktives Blatt, Arbeitsmappe, Auswahl), Seite einrichten.</summary>
public interface IPrintSource
{
    /// <summary>What to print (an extra choice above the pages), empty when the app has none.</summary>
    IReadOnlyList<string> Scopes { get; }

    /// <summary>The chosen scope (index into <see cref="Scopes"/>).</summary>
    int Scope { get; set; }

    /// <summary>The pages as they will print (taken on the UI thread).</summary>
    PrintablePages Snapshot();

    /// <summary>The page of the caret / the active cell (Aktuelle Seite drucken).</summary>
    int CurrentPage { get; }

    /// <summary>Opens the app's Seite einrichten; true when something changed (null: the app has none).</summary>
    Func<Task<bool>>? PageSetup { get; }
}

/// <summary>
/// Datei › Drucken of every app, like Word's and Excel's: on the left "Drucken" with the number of copies, the printer, which pages (Alle Seiten,
/// Aktuelle Seite, Benutzerdefinierter Druck with "Seiten: 1-3, 5"), and the orientation and paper of the document (changed
/// in "Seite einrichten"); on the right a preview of the pages that will be printed, with paging. Printing goes through
/// <see cref="PrintService"/> (policy, validation, the system's printer) on a worker thread; the pages are the ones of the
/// layout, drawn with Skia (<see cref="PrintablePages"/>). The app supplies its pages through <see cref="IPrintSource"/>.
/// </summary>
public class OfficePrintPage : UserControl
{
    public const string PageId = "print";

    private const double PreviewWidth = 430;
    private const double PreviewHeight = 560;

    private readonly OfficeWindow _window;
    private readonly IPrintSource _source;
    private readonly ComboBox _scope = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly IReadOnlyList<PrinterInfo> _printers;
    private readonly ComboBox _printer = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly ComboBox _range = new() { MinWidth = 280, HorizontalAlignment = HorizontalAlignment.Stretch };
    private readonly TextBox _custom = new() { Watermark = "z. B. 1-3, 5" };
    private readonly TextBlock _orientation = new();
    private readonly TextBlock _paper = new();
    private readonly Image _preview = new() { Stretch = Stretch.Fill };
    private readonly TextBlock _pageLabel = new() { VerticalAlignment = VerticalAlignment.Center, MinWidth = 110, TextAlignment = TextAlignment.Center };
    private readonly InfoBar _message = new();
    private PrintablePages _pages;
    private IReadOnlyList<int> _previewPages = [];
    private int _previewPosition;

    public OfficePrintPage(OfficeWindow window, IPrintSource source)
    {
        _window = window;
        _source = source;
        _pages = Snapshot();
        _printers = PrintService.GetPrinters();

        var title = new TextBlock { Text = "Drucken" }.Themed("MatText.PageTitle");
        PrintButton = new Button { Content = new IconLabel { Icon = "print", Text = "Drucken" }, IsDefault = true }.Themed("MatButton.Primary");
        AutomationProperties.SetName(PrintButton, "Drucken");
        PrintButton.Click += async (_, _) => await PrintAsync();
        Copies = new NumericUpDown { Minimum = 1, Maximum = PrintService.MaxCopies, Value = 1, Increment = 1, FormatString = "0", Width = 120 };
        AutomationProperties.SetName(Copies, "Exemplare");
        var printRow = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            Spacing = 16,
            Children = { PrintButton, Field("Exemplare", Copies, PrintService.CopiesField) },
        };

        _printer.ItemsSource = _printers.Select(printer => printer.IsDefault ? $"{printer.Name} (Standard)" : printer.Name).ToList();
        _printer.SelectedIndex = _printers.Count > 0 ? 0 : -1;
        AutomationProperties.SetName(_printer, "Drucker");

        _range.ItemsSource = new[] { "Alle Seiten drucken", "Aktuelle Seite drucken", "Benutzerdefinierter Druck" };
        _range.SelectedIndex = 0;
        AutomationProperties.SetName(_range, "Seiten");
        AutomationProperties.SetName(_custom, "Seitenangabe");
        _range.SelectionChanged += (_, _) => OnChoiceChanged();
        _custom.TextChanged += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_custom.Text) && Range != PrintRange.Custom)
            {
                Range = PrintRange.Custom; // typing pages means "Benutzerdefinierter Druck", like Word
            }

            OnChoiceChanged();
        };

        var pageSetup = new Button { Content = "Seite einrichten …", IsVisible = source.PageSetup is not null }.Themed("MatButton.Link");
        pageSetup.Click += async (_, _) =>
        {
            if (_source.PageSetup is { } setup && await setup())
            {
                _pages = Snapshot();
                OnChoiceChanged();
            }
        };

        var settings = new StackPanel
        {
            Spacing = 12,
            Width = 320,
            Children =
            {
                title,
                printRow,
                Field("Drucker", _printer, PrintService.PrinterField),
                Section("Einstellungen"),
                Field("Drucken", _scope, null),
                Field("Seiten", _range, null),
                Field("Seitenangabe", _custom, PrintService.PagesField),
                Fact("orientation", _orientation),
                Fact("file-text", _paper),
                pageSetup,
                _message,
            },
        };

        var previous = PagingButton("chevron-left", "Vorherige Seite", -1);
        var next = PagingButton("chevron-right", "Nächste Seite", 1);
        PreviousButton = previous;
        NextButton = next;
        var paging = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            HorizontalAlignment = HorizontalAlignment.Center,
            Spacing = 6,
            Children = { previous, _pageLabel, next },
        };
        var paper = new Border { Child = _preview, HorizontalAlignment = HorizontalAlignment.Center, BorderThickness = new Thickness(1) }
            .Use(Border.BorderBrushProperty, "Mat.Border");
        var previewArea = new Grid
        {
            RowDefinitions = new RowDefinitions("*,Auto"),
            Margin = new Thickness(24, 0, 0, 0),
            Children = { new Border { Child = paper, Height = PreviewHeight + 2, VerticalAlignment = VerticalAlignment.Top } },
        };
        Grid.SetRow(paging, 1);
        paging.Margin = new Thickness(0, 12, 0, 0);
        previewArea.Children.Add(paging);

        var layout = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), Margin = new Thickness(32, 26) };
        Grid.SetColumn(previewArea, 1);
        layout.Children.Add(settings);
        layout.Children.Add(previewArea);
        Content = new ScrollViewer { Content = layout, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Auto };

        _scope.ItemsSource = source.Scopes;
        _scope.SelectedIndex = source.Scopes.Count > 0 ? Math.Clamp(source.Scope, 0, source.Scopes.Count - 1) : -1;
        AutomationProperties.SetName(_scope, "Was drucken");
        if (_scope.Parent is Control scopeField)
        {
            scopeField.IsVisible = source.Scopes.Count > 0;
        }

        _scope.SelectionChanged += (_, _) =>
        {
            if (_scope.SelectedIndex >= 0 && _scope.SelectedIndex != _source.Scope)
            {
                _source.Scope = _scope.SelectedIndex;
                _pages = Snapshot();
                OnChoiceChanged();
            }
        };

        ShowAvailability();
        OnChoiceChanged();
    }

    /// <summary>The app's choice of what to print (ThixCalc: Aktives Blatt, Gesamte Arbeitsmappe, Auswahl).</summary>
    public ComboBox ScopeChoice => _scope;

    public Button PrintButton { get; }

    public NumericUpDown Copies { get; }

    public ComboBox Printer => _printer;

    public TextBox CustomPages => _custom;

    public Button PreviousButton { get; }

    public Button NextButton { get; }

    /// <summary>The message under the settings (no printer, policy, an error of the system).</summary>
    public InfoBar Message => _message;

    /// <summary>"Seite 2 von 5" under the preview (tests).</summary>
    public string PageLabel => _pageLabel.Text ?? "";

    /// <summary>The page shown in the preview (0-based), or -1.</summary>
    public int PreviewPage => _previewPages.Count == 0 ? -1 : _previewPages[_previewPosition];

    /// <summary>"Hochformat" / "Querformat" and the paper of the page in the preview.</summary>
    public string OrientationText => _orientation.Text ?? "";

    public string PaperText => _paper.Text ?? "";

    public PrintRange Range
    {
        get => (PrintRange)Math.Max(0, _range.SelectedIndex);
        set => _range.SelectedIndex = (int)value;
    }

    /// <summary>Checks the input, prints on a worker thread and closes the backstage; false when refused (the message says why).</summary>
    public async Task<bool> PrintAsync()
    {
        FormErrors.ShowError(this, null, "");
        _message.Hide();
        if (_printer.SelectedIndex < 0 || _printer.SelectedIndex >= _printers.Count)
        {
            ShowError(new BusinessException("Bitte wählen Sie einen Drucker.", PrintService.PrinterField));
            return false;
        }

        PrintJob job;
        try
        {
            var pages = PrintService.PagesOf(Range, _custom.Text, _source.CurrentPage, _pages.Count);
            job = new PrintJob(Path.GetFileNameWithoutExtension(_window.Document.Title), _printers[_printer.SelectedIndex].Name, (int)(Copies.Value ?? 1), pages, _pages);
        }
        catch (BusinessException exception)
        {
            ShowError(exception);
            return false;
        }

        PrintButton.IsEnabled = false;
        _message.Show($"Wird an „{job.PrinterName}“ gesendet …", InfoSeverity.Info);
        try
        {
            await Task.Run(() => PrintService.Print(job));
            _message.Hide();
            _window.Backstage.Close();
            return true;
        }
        catch (BusinessException exception)
        {
            ShowError(exception);
            return false;
        }
        finally
        {
            PrintButton.IsEnabled = _printers.Count > 0 && !PrintService.IsDisabled;
        }
    }

    /// <summary>Shows another page of the preview (-1 / +1).</summary>
    public void Page(int direction)
    {
        if (_previewPages.Count == 0)
        {
            return;
        }

        _previewPosition = Math.Clamp(_previewPosition + direction, 0, _previewPages.Count - 1);
        ShowPreview();
    }

    private PrintablePages Snapshot() => _source.Snapshot();

    private void ShowAvailability()
    {
        if (PrintService.IsDisabled)
        {
            _message.Show("Das Drucken wurde von einem Administrator deaktiviert (Richtlinie DisablePrinting). Als PDF exportieren ist weiterhin möglich.", InfoSeverity.Warning);
        }
        else if (_printers.Count == 0)
        {
            _message.Show("Es ist kein Drucker eingerichtet. Richten Sie einen in den Einstellungen des Systems ein – oder exportieren Sie das Dokument als PDF.", InfoSeverity.Warning);
        }

        PrintButton.IsEnabled = _printers.Count > 0 && !PrintService.IsDisabled;
        _printer.IsEnabled = _printers.Count > 0;
    }

    /// <summary>The choice of pages changed: the preview shows the pages that would be printed (all of them while the text is not valid).</summary>
    private void OnChoiceChanged()
    {
        var current = PreviewPage;
        try
        {
            _previewPages = PrintService.PagesOf(Range, _custom.Text, _source.CurrentPage, _pages.Count);
            FormErrors.ShowError(this, null, "");
        }
        catch (BusinessException)
        {
            _previewPages = [.. Enumerable.Range(0, _pages.Count)];
        }

        var position = _previewPages is List<int> list ? list.IndexOf(current) : _previewPages.ToList().IndexOf(current);
        _previewPosition = Range == PrintRange.All && current < 0 ? Math.Min(_source.CurrentPage, _previewPages.Count - 1) : Math.Max(0, position);
        ShowPreview();
    }

    private void ShowPreview()
    {
        if (_previewPages.Count == 0)
        {
            _pageLabel.Text = "";
            _preview.Source = null;
            return;
        }

        var index = _previewPages[_previewPosition];
        _pageLabel.Text = $"Seite {_previewPosition + 1} von {_previewPages.Count}";
        PreviousButton.IsEnabled = _previewPosition > 0;
        NextButton.IsEnabled = _previewPosition < _previewPages.Count - 1;
        var (width, height) = _pages.SizeOf(index);
        _orientation.Text = width > height ? "Querformat" : "Hochformat";
        _paper.Text = PaperSize.Describe(width, height);

        // The page fitted into the preview box, drawn at twice its size on screen (sharp on a Retina display).
        var scale = Math.Min(PreviewWidth / width, PreviewHeight / height);
        _preview.Width = Math.Round(width * scale);
        _preview.Height = Math.Round(height * scale);
        var dpi = scale * 72 * 2;
        var raster = _pages.Render(index, dpi, 0, (int)Math.Ceiling(height / 72 * dpi));
        var bitmap = new WriteableBitmap(new PixelSize(raster.Width, raster.Height), new Vector(96, 96), PixelFormat.Bgra8888, AlphaFormat.Premul);
        using (var buffer = bitmap.Lock())
        {
            for (var row = 0; row < raster.Height; row++)
            {
                Marshal.Copy(raster.Pixels, row * raster.Width * 4, buffer.Address + (row * buffer.RowBytes), raster.Width * 4);
            }
        }

        (_preview.Source as IDisposable)?.Dispose();
        _preview.Source = bitmap;
    }

    private void ShowError(BusinessException exception)
    {
        if (!FormErrors.ShowError(this, exception.Field, exception.Message))
        {
            _message.Show(exception.Message, InfoSeverity.Error);
        }
    }

    private Button PagingButton(string icon, string name, int direction)
    {
        var button = new Button { Content = new MatIcon { Icon = icon, Size = 16 } }.Themed("MatButton.Icon");
        AutomationProperties.SetName(button, name);
        ToolTip.SetTip(button, name);
        button.Click += (_, _) => Page(direction);
        return button;
    }

    private static MatFormField Field(string label, Control control, string? field)
    {
        var formField = new MatFormField { Label = label, Content = control };
        if (field is not null)
        {
            FormErrors.SetField(formField, field);
        }

        return formField;
    }

    private static TextBlock Section(string text) => new TextBlock { Text = text, Margin = new Thickness(0, 8, 0, 0) }.Themed("MatText.SectionTitle");

    private static Control Fact(string icon, TextBlock text) => new StackPanel
    {
        Orientation = Orientation.Horizontal,
        Spacing = 10,
        Children =
        {
            new MatIcon { Icon = icon, Size = 16, VerticalAlignment = VerticalAlignment.Center }.Use(MatIcon.ForegroundProperty, "Mat.TextMuted"),
            text,
        },
    };
}
