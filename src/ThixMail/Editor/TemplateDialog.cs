using Avalonia.Automation;
using Avalonia.Controls;
using ThixMail.Core;
using ThixMail.Core.Model;
using MatDesktop;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;

namespace ThixMail.Editor;

/// <summary>Neue E-Mail aus Vorlage: the templates by name; OK starts a message with the chosen one, "Vorlage löschen" removes one.</summary>
public sealed class TemplateDialog : OfficeDialog
{
    private readonly MailDocument _document;
    private IReadOnlyList<MailTemplate> _templates = [];

    public TemplateDialog(MailDocument document)
        : base("Neue E-Mail aus Vorlage", 420)
    {
        _document = document;
        AutomationProperties.SetName(List, "Vorlagen");
        List.DoubleTapped += (_, _) => Confirm();
        DeleteButton = new Button { Content = new IconLabel { Icon = "trash", Text = "Vorlage löschen" } }.Themed("MatButton.Danger");
        DeleteButton.Click += (_, _) =>
        {
            if (Selected is { } template)
            {
                _document.Accounts.RemoveTemplate(template.Id);
                Load();
            }
        };
        Load();
        SetBody(new StackPanel { Spacing = 10, Children = { List, new ActionBar { Children = { DeleteButton } } } });
    }

    public ListBox List { get; } = new() { MinHeight = 120, MaxHeight = 320 };

    public Button DeleteButton { get; }

    /// <summary>The template chosen (after OK).</summary>
    public MailTemplate? Chosen { get; private set; }

    private MailTemplate? Selected => List.SelectedIndex >= 0 && List.SelectedIndex < _templates.Count ? _templates[List.SelectedIndex] : null;

    private void Load()
    {
        _templates = _document.Accounts.Templates;
        List.ItemsSource = _templates.Select(t => t.Name).ToList();
        List.SelectedIndex = _templates.Count > 0 ? 0 : -1;
        DeleteButton.IsEnabled = OkButton.IsEnabled = _templates.Count > 0;
    }

    protected override bool Apply()
    {
        Chosen = Selected;
        return Chosen is not null;
    }
}
