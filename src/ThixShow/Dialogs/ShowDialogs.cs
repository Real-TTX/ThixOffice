using Avalonia.Automation;
using Avalonia.Controls;
using ThixOffice.Core.Business;
using ThixOffice.Core.Models;
using ThixOffice.Desktop.Dialogs;
using ThixOffice.Desktop.Shell;
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
