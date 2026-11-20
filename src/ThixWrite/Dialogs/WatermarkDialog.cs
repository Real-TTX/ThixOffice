using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Shell;
using ThixWrite.Core.Model;
using ThixWrite.Editor;

namespace ThixWrite.Dialogs;

/// <summary>
/// Entwurf › Wasserzeichen › Benutzerdefiniertes Wasserzeichen ("Gedrucktes Wasserzeichen" in Word): Kein Wasserzeichen, Bildwasserzeichen
/// (picture, scale, Auswaschen) or Textwasserzeichen (text, font, colour, Halbtransparent, diagonal or horizontal).
/// </summary>
public sealed class WatermarkDialog : WriteDialog
{
    private static readonly (string Label, double Scale)[] Scales = [("Automatisch", 0), ("50 %", 0.5), ("100 %", 1), ("150 %", 1.5), ("200 %", 2), ("500 %", 5)];

    private readonly WriteEditor _editor;
    private readonly Window _owner;
    private readonly TextBlock _pictureName;

    public WatermarkDialog(Window owner, WriteEditor editor)
        : base("Gedrucktes Wasserzeichen", 500)
    {
        _owner = owner;
        _editor = editor;
        var current = editor.Session.CurrentWatermark;
        Picture = current?.Picture;
        None = new RadioButton { Content = "Kein Wasserzeichen", GroupName = "watermark", IsChecked = current is null };
        PictureChoice = new RadioButton { Content = "Bildwasserzeichen", GroupName = "watermark", IsChecked = current?.Picture is not null };
        TextChoice = new RadioButton { Content = "Textwasserzeichen", GroupName = "watermark", IsChecked = current is { Picture: null } };

        var choose = new Button { Content = "Bild auswählen …" };
        choose.Click += async (_, _) => await ChoosePictureAsync();
        _pictureName = new TextBlock { VerticalAlignment = VerticalAlignment.Center, Text = Picture is null ? "Kein Bild" : $"{Picture.PixelWidth} × {Picture.PixelHeight} Pixel" }
            .Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        Scale = new ComboBox { ItemsSource = Scales.Select(scale => scale.Label).ToList(), Width = 140 };
        Scale.SelectedIndex = Math.Max(0, Array.FindIndex(Scales, scale => Math.Abs(scale.Scale - (current?.Scale ?? 0)) < 0.01));
        Washout = new CheckBox { Content = "Auswaschen", IsChecked = current?.Washout ?? true };

        var theme = editor.Session.Document.Theme;
        WatermarkText = new ComboBox { ItemsSource = Watermark.Texts, IsEditable = true, Width = 260, Text = current?.Text ?? Watermark.Texts[1] };
        WatermarkFont = new ComboBox
        {
            ItemsSource = new[] { theme.MinorFont, theme.MajorFont, "Calibri", "Arial", "Times New Roman", "Georgia", "Verdana" }.Distinct().ToList(),
            Width = 200,
        };
        WatermarkFont.SelectedItem = current?.FontFamily is { } family && ((List<string>)WatermarkFont.ItemsSource).Contains(family) ? family : theme.MinorFont;
        var colors = new[] { DocumentPalette.WatermarkSilver }.Concat(DocumentPalette.FontColors(theme)).Distinct().ToList();
        Color = new ComboBox { ItemsSource = colors, Width = 160, ItemTemplate = BordersDialog.Swatches() };
        Color.SelectedItem = current?.Color is { Length: 6 } hex && colors.Contains("#" + hex.ToUpperInvariant()) ? "#" + hex.ToUpperInvariant() : DocumentPalette.WatermarkSilver;
        SemiTransparent = new CheckBox { Content = "Halbtransparent", IsChecked = current?.SemiTransparent ?? true };
        Diagonal = new RadioButton { Content = "Diagonal", GroupName = "layout", IsChecked = current?.Diagonal ?? true };
        var horizontal = new RadioButton { Content = "Horizontal", GroupName = "layout", IsChecked = !(current?.Diagonal ?? true) };
        AutomationProperties.SetName(WatermarkText, "Text");
        AutomationProperties.SetName(WatermarkFont, "Schriftart");
        AutomationProperties.SetName(Color, "Farbe");
        AutomationProperties.SetName(Scale, "Skalieren");

        var pictureOptions = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(28, 0, 0, 0),
            Children =
            {
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10, Children = { choose, _pictureName } },
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Field("Skalieren", Scale), Washout } },
            },
        };
        var textOptions = new StackPanel
        {
            Spacing = 10,
            Margin = new Thickness(28, 0, 0, 0),
            Children =
            {
                Field("Text", WatermarkText),
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { Field("Schriftart", WatermarkFont), Field("Farbe", Color) } },
                SemiTransparent,
                new StackPanel { Orientation = Orientation.Horizontal, Spacing = 16, Children = { new TextBlock { Text = "Layout:", VerticalAlignment = VerticalAlignment.Center }, Diagonal, horizontal } },
            },
        };
        void Sync()
        {
            pictureOptions.IsEnabled = PictureChoice.IsChecked == true;
            textOptions.IsEnabled = TextChoice.IsChecked == true;
        }

        None.IsCheckedChanged += (_, _) => Sync();
        PictureChoice.IsCheckedChanged += (_, _) => Sync();
        TextChoice.IsCheckedChanged += (_, _) => Sync();
        Sync();
        SetBody(new StackPanel { Spacing = 12, Children = { None, PictureChoice, pictureOptions, TextChoice, textOptions } });
    }

    public RadioButton None { get; }

    public RadioButton PictureChoice { get; }

    public RadioButton TextChoice { get; }

    /// <summary>The chosen picture (Bild auswählen …).</summary>
    public ImageData? Picture { get; set; }

    public ComboBox Scale { get; }

    public CheckBox Washout { get; }

    public ComboBox WatermarkText { get; }

    public ComboBox WatermarkFont { get; }

    public ComboBox Color { get; }

    public CheckBox SemiTransparent { get; }

    public RadioButton Diagonal { get; }

    public static Task<bool> ShowAsync(Window owner, WriteEditor editor) => new WatermarkDialog(owner, editor).ShowAsync(owner);

    private async Task ChoosePictureAsync()
    {
        var path = await OfficeApplication.FileDialogs.PickPictureAsync(this, DocumentService.SuggestedFolder(_editor.Document));
        if (path is null)
        {
            return;
        }

        try
        {
            Picture = WriteEditor.ReadPicture(path);
            _pictureName.Text = Path.GetFileName(path);
            PictureChoice.IsChecked = true;
        }
        catch (BusinessException exception)
        {
            _pictureName.Text = exception.Message;
        }
    }

    protected override bool Apply()
    {
        var session = _editor.Session;
        if (None.IsChecked == true)
        {
            session.SetWatermark(null);
            return true;
        }

        if (PictureChoice.IsChecked == true)
        {
            if (Picture is null)
            {
                _pictureName.Text = "Bitte wählen Sie ein Bild aus.";
                return false;
            }

            session.SetWatermark(new Watermark { Picture = Picture, Scale = Scales[Math.Max(0, Scale.SelectedIndex)].Scale, Washout = Washout.IsChecked == true });
            return true;
        }

        var text = (WatermarkText.Text ?? "").Trim();
        if (text.Length == 0)
        {
            return false;
        }

        session.SetWatermark(new Watermark
        {
            Text = text,
            FontFamily = WatermarkFont.SelectedItem as string ?? "Calibri",
            Color = (Color.SelectedItem as string ?? DocumentPalette.WatermarkSilver).TrimStart('#').ToUpperInvariant(),
            SemiTransparent = SemiTransparent.IsChecked == true,
            Diagonal = Diagonal.IsChecked == true,
        });
        return true;
    }
}
