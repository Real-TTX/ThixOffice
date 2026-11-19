using Avalonia.Automation;
using Avalonia.Controls;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
using ThixShow.Core.Editing;
using ThixShow.Core.Model;
using ThixShow.Editor;

namespace ThixShow.Dialogs;

/// <summary>Zoom: the presets of PowerPoint (Anpassen, 400 … 33 %) or a percentage.</summary>
public sealed class ZoomDialog : OfficeDialog
{
    private readonly ShowEditor _editor;
    private readonly List<(RadioButton Button, double? Percent)> _choices = [];

    public ZoomDialog(ShowEditor editor)
        : base("Zoom", 420)
    {
        _editor = editor;
        Percent = new NumericUpDown { Value = (decimal)editor.Zoom, Minimum = 10, Maximum = 400, Increment = 10, FormatString = "0", MinWidth = 140, HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Left };
        AutomationProperties.SetName(Percent, "Prozent");
        var presets = new StackPanel { Spacing = 6 };
        foreach (var (label, percent) in new (string, double?)[] { ("Anpassen", null), ("400 %", 400), ("200 %", 200), ("100 %", 100), ("66 %", 66), ("50 %", 50), ("33 %", 33) })
        {
            var button = new RadioButton { Content = label, GroupName = "zoom", IsChecked = percent is null ? editor.FitsWindow : !editor.FitsWindow && percent == editor.Zoom };
            var value = percent;
            button.IsCheckedChanged += (_, _) =>
            {
                if (button.IsChecked == true && value is { } chosen)
                {
                    Percent.Value = (decimal)chosen;
                }
            };
            _choices.Add((button, percent));
            presets.Children.Add(button);
        }

        var body = new Grid { ColumnDefinitions = new ColumnDefinitions("*,24,*") };
        var custom = Field("Prozent", Percent);
        Grid.SetColumn(custom, 2);
        body.Children.Add(Field("Vergrößerung", presets));
        body.Children.Add(custom);
        SetBody(body);
    }

    public NumericUpDown Percent { get; }

    public static Task<bool> ShowAsync(Window owner, ShowEditor editor) => new ZoomDialog(editor).ShowAsync(owner);

    protected override bool Apply()
    {
        if (_choices.Any(choice => choice.Button.IsChecked == true && choice.Percent is null))
        {
            _editor.FitToWindow();
            return true;
        }

        if (Percent.Value is not { } percent)
        {
            return false;
        }

        _editor.Zoom = (double)percent;
        return true;
    }
}

/// <summary>Link einfügen (Strg+K): the address of a web page or mail; checked in the business layer (http, https, mailto only).</summary>
internal static class LinkDialog
{
    public static async Task ShowAsync(OfficeWindow window, ShowEditor? editor)
    {
        if (editor is null)
        {
            return;
        }

        string? error = null;
        var text = "https://";
        while (true)
        {
            var answer = await TextInputDialog.AskAsync(window, "Link einfügen", "Adresse", text, "Einfügen", error);
            if (answer is null)
            {
                return;
            }

            try
            {
                var address = LinkService.Normalize(answer);
                await editor.RunAsync("Link", () => editor.Session.SetTextLink(Hyperlink.Url(address)));
                return;
            }
            catch (BusinessException exception)
            {
                (text, error) = (answer, exception.Message);
            }
        }
    }
}

/// <summary>Abschnitt hinzufügen / umbenennen: the name of a section (PowerPoint's "Abschnitt umbenennen").</summary>
internal static class SectionDialogs
{
    public static async Task AddAsync(OfficeWindow window, ShowEditor? editor)
    {
        if (editor?.Session.Selection.CurrentSlide is not { } slideId)
        {
            return;
        }

        var name = await TextInputDialog.AskAsync(window, "Abschnitt hinzufügen", "Abschnittsname", "Unbenannter Abschnitt", "Hinzufügen");
        if (!string.IsNullOrWhiteSpace(name))
        {
            await editor.RunAsync("Abschnitt hinzufügen", () => editor.Session.AddSection(name.Trim(), slideId));
        }
    }

    public static async Task RenameAsync(OfficeWindow window, ShowEditor? editor)
    {
        if (editor?.Session.Selection.CurrentSlide is not { } slideId || editor.Session.Presentation.SectionOf(slideId) is not { } section)
        {
            return;
        }

        var name = await TextInputDialog.AskAsync(window, "Abschnitt umbenennen", "Abschnittsname", section.Name, "Umbenennen");
        if (!string.IsNullOrWhiteSpace(name))
        {
            await editor.RunAsync("Abschnitt umbenennen", () => editor.Session.RenameSection(section.Id, name.Trim()));
        }
    }
}

/// <summary>
/// Kopf- und Fußzeile (Einfügen): Datum und Uhrzeit (automatisch aktualisieren or fest), Foliennummer, Fußzeile with its text, Auf
/// Titelfolie nicht anzeigen; Übernehmen gives it to the selected slides, or – with "Für alle Folien" – to every slide.
/// </summary>
public sealed class HeaderFooterDialog : OfficeDialog
{
    private readonly ShowEditor _editor;

    public HeaderFooterDialog(ShowEditor editor, bool date = false, bool slideNumber = false)
        : base("Kopf- und Fußzeile", 480)
    {
        _editor = editor;
        var current = editor.Session.CurrentSlide is { } slide ? editor.Session.HeaderFooterOf(slide) : new HeaderFooterSettings();
        Date = new CheckBox { Content = "Datum und Uhrzeit", IsChecked = current.Date || date };
        Automatic = new RadioButton { Content = "Automatisch aktualisieren", GroupName = "date", IsChecked = current.DateAutomatic, Margin = new Avalonia.Thickness(26, 0, 0, 0) };
        Fixed = new RadioButton { Content = "Fest", GroupName = "date", IsChecked = !current.DateAutomatic, Margin = new Avalonia.Thickness(26, 0, 0, 0) };
        FixedDate = new TextBox { Text = current.FixedDate.Length > 0 ? current.FixedDate : DateTime.Now.ToString("dd.MM.yyyy", System.Globalization.CultureInfo.GetCultureInfo("de-DE")), Margin = new Avalonia.Thickness(52, 0, 0, 0) };
        SlideNumber = new CheckBox { Content = "Foliennummer", IsChecked = current.SlideNumber || slideNumber };
        Footer = new CheckBox { Content = "Fußzeile", IsChecked = current.Footer };
        FooterText = new TextBox { Text = current.FooterText, Margin = new Avalonia.Thickness(26, 0, 0, 0) };
        NotOnTitle = new CheckBox { Content = "Auf Titelfolie nicht anzeigen", IsChecked = current.NotOnTitleSlide };
        ForAll = new CheckBox { Content = "Für alle Folien übernehmen", IsChecked = true };
        AutomationProperties.SetName(FixedDate, "Festes Datum");
        AutomationProperties.SetName(FooterText, "Text der Fußzeile");
        OkButton.Content = new ThixOffice.Desktop.Controls.IconLabel { Icon = "check", Text = "Übernehmen" };
        SetBody(new StackPanel
        {
            Spacing = 8,
            Children = { Date, Automatic, Fixed, FixedDate, SlideNumber, Footer, FooterText, new Separator(), NotOnTitle, ForAll },
        });
    }

    public CheckBox Date { get; }

    public RadioButton Automatic { get; }

    public RadioButton Fixed { get; }

    public TextBox FixedDate { get; }

    public CheckBox SlideNumber { get; }

    public CheckBox Footer { get; }

    public TextBox FooterText { get; }

    public CheckBox NotOnTitle { get; }

    public CheckBox ForAll { get; }

    public static Task<bool> ShowAsync(Window owner, ShowEditor editor, bool date = false, bool slideNumber = false) =>
        new HeaderFooterDialog(editor, date, slideNumber).ShowAsync(owner);

    protected override bool Apply()
    {
        var settings = new HeaderFooterSettings(
            Date: Date.IsChecked == true,
            DateAutomatic: Automatic.IsChecked == true,
            FixedDate: FixedDate.Text ?? "",
            SlideNumber: SlideNumber.IsChecked == true,
            Footer: Footer.IsChecked == true,
            FooterText: FooterText.Text ?? "",
            NotOnTitleSlide: NotOnTitle.IsChecked == true);
        var slides = ForAll.IsChecked == true ? [.. _editor.Session.Presentation.Slides.Select(slide => slide.Id)] : _editor.SelectedSlideIds;
        _ = _editor.RunAsync("Kopf- und Fußzeile", () => _editor.Session.SetHeaderFooter(settings, slides));
        return true;
    }
}
