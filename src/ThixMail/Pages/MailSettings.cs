using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Model;
using ThixMail.Core.Security;
using ThixMail.Editor;
using ThixOffice.Core.Business;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Pages;
using ThixOffice.Desktop.Shell;

namespace ThixMail.Pages;

/// <summary>
/// ThixMail's tabs of Einstellungen (after Allgemein): Lesen (reading pane, when a message counts as read, remote pictures, the offline store's size),
/// Verfassen (the font of new messages), Signaturen (several, each account's default for new messages and for replies) and Benachrichtigungen (a
/// notification for new mail, keep running in the notification area – off by default). Each control saves at once.
/// </summary>
public static class MailSettings
{
    public const string ReadingTab = "mail.reading";
    public const string ComposeTab = "mail.compose";
    public const string SignaturesTab = "mail.signatures";
    public const string NotificationsTab = "mail.notifications";

    public static IEnumerable<SettingsTab> Tabs(OfficeWindow window) =>
    [
        new(ReadingTab, "Lesen", () => Reading(window)),
        new(ComposeTab, "Verfassen", Compose),
        new(SignaturesTab, "Signaturen", () => new SignaturesPanel(((MailEditor)window.Editor).Document)),
        new(NotificationsTab, "Benachrichtigungen", Notifications),
    ];

    private static StackPanel Panel() => new() { MaxWidth = 760, HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 0, 16, 0) };

    private static MatSettingRow Row(string title, string description, Control control) => new() { Title = title, Description = description, Content = control };

    private static Control Reading(OfficeWindow window)
    {
        var editor = (MailEditor)window.Editor;
        var pane = new ComboBox { ItemsSource = new[] { "Rechts", "Unten", "Aus" }, SelectedIndex = (int)editor.Pane, MinWidth = 160 };
        AutomationProperties.SetName(pane, "Lesebereich");
        pane.SelectionChanged += (_, _) => editor.Pane = (ReadingPanePosition)Math.Max(0, pane.SelectedIndex);
        var seconds = new ComboBox { ItemsSource = new[] { "Sofort beim Anzeigen", "Nach 5 Sekunden", "Nach 15 Sekunden" }, MinWidth = 200 };
        seconds.SelectedIndex = SettingsService.AppValue(MailDocument.Settings.MarkReadSeconds) switch { "5" => 1, "15" => 2, _ => 0 };
        AutomationProperties.SetName(seconds, "Als gelesen markieren");
        seconds.SelectionChanged += (_, _) => SettingsService.RememberAppValue(MailDocument.Settings.MarkReadSeconds, seconds.SelectedIndex switch { 1 => "5", 2 => "15", _ => null });
        var budget = new ComboBox { ItemsSource = new[] { "250 MB", "500 MB", "1 GB", "5 GB" }, MinWidth = 120 };
        budget.SelectedIndex = SettingsService.AppValue(MailDocument.Settings.CacheBudgetMb) switch { "250" => 0, "1024" => 2, "5120" => 3, _ => 1 };
        AutomationProperties.SetName(budget, "Offline-Speicher");
        budget.SelectionChanged += (_, _) => SettingsService.RememberAppValue(MailDocument.Settings.CacheBudgetMb, budget.SelectedIndex switch { 0 => "250", 2 => "1024", 3 => "5120", _ => null });
        var used = new TextBlock { Text = $"Belegt: {MessageView.Size(editor.Document.Store.CachedBytes)}", FontSize = MatTokens.Type.FsSm };
        used.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var images = new TextBlock
        {
            Text = "Bilder aus dem Internet werden nie von selbst geladen: der Absender erführe sonst, dass und wann Sie die Nachricht lesen. Laden Sie sie je Nachricht mit „Bilder laden“ oder für vertraute Absender immer.",
            TextWrapping = TextWrapping.Wrap,
        };
        images.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var panel = Panel();
        panel.Children.Add(Row("Lesebereich", "Wo die ausgewählte Nachricht angezeigt wird.", pane));
        panel.Children.Add(Row("Als gelesen markieren", "Wann eine Nachricht im Lesebereich als gelesen gilt.", seconds));
        panel.Children.Add(Row("Bilder aus dem Internet", "", images));
        panel.Children.Add(Row("Offline-Speicher", "Wie viel Platz die Texte und Anlagen geöffneter Nachrichten auf diesem Computer belegen dürfen. Ältere werden bei Bedarf wieder geladen.", new StackPanel { Spacing = 4, Children = { budget, used } }));
        return panel;
    }

    private static Control Compose()
    {
        var fonts = ThixWrite.Editor.WriteFonts.Families;
        var font = new ComboBox { ItemsSource = fonts, MinWidth = 200, SelectedItem = SettingsService.AppValue(MailDocument.Settings.ComposeFont) ?? ThixWrite.Editor.WriteFonts.Default.Body };
        AutomationProperties.SetName(font, "Schriftart neuer Nachrichten");
        font.SelectionChanged += (_, _) => SettingsService.RememberAppValue(MailDocument.Settings.ComposeFont, font.SelectedItem as string);
        var size = new ComboBox { ItemsSource = new[] { "10", "11", "12", "14" }, MinWidth = 80, SelectedItem = SettingsService.AppValue(MailDocument.Settings.ComposeSize) ?? "11" };
        AutomationProperties.SetName(size, "Schriftgrad neuer Nachrichten");
        size.SelectionChanged += (_, _) => SettingsService.RememberAppValue(MailDocument.Settings.ComposeSize, size.SelectedItem as string);
        var format = new TextBlock
        {
            Text = "ThixMail sendet HTML mit einer Textfassung für Programme, die kein HTML zeigen. Antworten zitieren die ursprüngliche Nachricht unter einem Kopfblock wie Outlook.",
            TextWrapping = TextWrapping.Wrap,
        };
        format.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        var panel = Panel();
        panel.Children.Add(Row("Schriftart", "Für neue Nachrichten, Antworten und Weiterleitungen.", font));
        panel.Children.Add(Row("Schriftgrad", "In Punkt.", size));
        panel.Children.Add(Row("Format", "", format));
        return panel;
    }

    private static Control Notifications()
    {
        var notify = new ToggleSwitch { IsChecked = SettingsService.AppValue(MailDocument.Settings.Notifications) != "false" };
        AutomationProperties.SetName(notify, "Benachrichtigung bei neuer E-Mail");
        notify.IsCheckedChanged += (_, _) => SettingsService.RememberAppValue(MailDocument.Settings.Notifications, notify.IsChecked == true ? null : "false");
        var keep = new ToggleSwitch { IsChecked = SettingsService.AppValue(MailDocument.Settings.KeepRunning) == "true" };
        AutomationProperties.SetName(keep, "Im Infobereich weiterlaufen");
        keep.IsCheckedChanged += (_, _) => SettingsService.RememberAppValue(MailDocument.Settings.KeepRunning, keep.IsChecked == true ? "true" : null);
        var panel = Panel();
        panel.Children.Add(Row("Benachrichtigung bei neuer E-Mail", "Zeigt Absender und Betreff neuer Nachrichten im Posteingang – nicht, solange das Fenster von ThixMail vorn ist.", notify));
        panel.Children.Add(Row(OperatingSystem.IsMacOS() ? "Nach dem Schließen weiter abrufen" : "Im Infobereich weiterlaufen",
            "Wenn Sie das Fenster schließen, ruft ThixMail weiter E-Mails ab und meldet neue (Symbol im Infobereich; Beenden über sein Menü).", keep));
        return panel;
    }
}

/// <summary>Einstellungen › Signaturen: the list (toolbar-free: few entries) with Neu · Umbenennen · gap · Löschen, the editor of the chosen one, and each account's defaults.</summary>
public sealed class SignaturesPanel : UserControl
{
    private readonly MailDocument _document;
    private readonly ListBox _list = new() { MaxHeight = 200, MinHeight = 80 };
    private readonly TextBox _editor = new() { AcceptsReturn = true, TextWrapping = TextWrapping.Wrap, MinHeight = 140 };
    private readonly StackPanel _defaults = new() { Spacing = 8 };
    private IReadOnlyList<MailSignature> _signatures = [];
    private bool _loading;

    public SignaturesPanel(MailDocument document)
    {
        _document = document;
        AutomationProperties.SetName(_list, "Signaturen");
        AutomationProperties.SetName(_editor, "Text der Signatur");
        _list.SelectionChanged += (_, _) => ShowSelected();
        _editor.LostFocus += (_, _) => SaveText();
        NewButton = new Button { Content = new IconLabel { Icon = "plus", Text = "Neu" } }.Themed("MatButton.Primary");
        NewButton.Click += (_, _) => New();
        var delete = new Button { Content = new IconLabel { Icon = "trash", Text = "Löschen" } }.Themed("MatButton.Danger");
        ActionBar.SetSeparated(delete, true);
        delete.Click += (_, _) =>
        {
            if (Selected is { } signature)
            {
                _document.Accounts.RemoveSignature(signature.Id);
                Load();
            }
        };
        DeleteButton = delete;
        var hint = new TextBlock { Text = "Der Text steht unter neuen Nachrichten oder über dem Zitat einer Antwort. Leere Zeilen bleiben erhalten.", TextWrapping = TextWrapping.Wrap, FontSize = MatTokens.Type.FsSm };
        hint.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        Content = new StackPanel
        {
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 10,
            Margin = new Thickness(0, 0, 16, 0),
            Children =
            {
                _list,
                new ActionBar { Children = { NewButton, DeleteButton } },
                new MatFormField { Label = "Signatur bearbeiten", Content = _editor },
                hint,
                new TextBlock { Text = "Standardsignaturen" }.Themed("MatText.SectionTitle"),
                _defaults,
            },
        };
        Load();
    }

    public Button NewButton { get; }

    public Button DeleteButton { get; }

    public TextBox Editor => _editor;

    public ListBox List => _list;

    private MailSignature? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _signatures.Count ? _signatures[_list.SelectedIndex] : null;

    private void Load()
    {
        _loading = true;
        var selected = Selected?.Id;
        _signatures = _document.Accounts.Signatures;
        _list.ItemsSource = _signatures.Select(s => s.Name).ToList();
        _list.SelectedIndex = Math.Max(_signatures.ToList().FindIndex(s => s.Id == selected), _signatures.Count > 0 ? 0 : -1);
        _loading = false;
        ShowSelected();
        BuildDefaults();
    }

    private void ShowSelected()
    {
        if (_loading)
        {
            return;
        }

        _editor.IsEnabled = Selected is not null;
        _editor.Text = Selected?.Text ?? "";
    }

    /// <summary>"Neu": a signature named "Signatur n" with the sender's name, chosen for editing.</summary>
    public void New()
    {
        SaveText();
        var name = $"Signatur {_signatures.Count + 1}";
        var text = $"Viele Grüße\n{SettingsService.AuthorName}";
        _document.Accounts.Save(new MailSignature(Guid.NewGuid().ToString("N"), name, MailHtml.TextAsHtml(text), text));
        Load();
        _list.SelectedIndex = _signatures.ToList().FindIndex(s => s.Name == name);
    }

    /// <summary>The edited text is kept (the HTML made from it line by line).</summary>
    public void SaveText()
    {
        if (Selected is { } signature && (_editor.Text ?? "") != signature.Text)
        {
            var text = (_editor.Text ?? "").Replace("\r\n", "\n", StringComparison.Ordinal);
            _document.Accounts.Save(signature with { Text = text, Html = MailHtml.TextAsHtml(text) });
            _signatures = _document.Accounts.Signatures;
        }
    }

    private void BuildDefaults()
    {
        _defaults.Children.Clear();
        var options = new List<string> { "(keine)" };
        options.AddRange(_signatures.Select(s => s.Name));
        foreach (var account in _document.Accounts.Accounts)
        {
            ComboBox Choice(string? current, Func<MailAccount, string?, MailAccount> set, string label)
            {
                var box = new ComboBox { ItemsSource = options, MinWidth = 180, SelectedIndex = Math.Max(0, _signatures.ToList().FindIndex(s => s.Id == current) + 1) };
                AutomationProperties.SetName(box, $"{label} ({account.DisplayName})");
                box.SelectionChanged += (_, _) =>
                {
                    var id = box.SelectedIndex > 0 ? _signatures[box.SelectedIndex - 1].Id : null;
                    _document.Accounts.Save(set(_document.Accounts.Account(account.Id) ?? account, id));
                };
                return box;
            }

            var row = new MatSettingRow
            {
                Title = account.DisplayName,
                Description = "Neue Nachrichten · Antworten und Weiterleitungen",
                Content = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Spacing = 8,
                    Children =
                    {
                        Choice(account.NewSignatureId, (a, id) => a with { NewSignatureId = id }, "Neue Nachrichten"),
                        Choice(account.ReplySignatureId, (a, id) => a with { ReplySignatureId = id }, "Antworten"),
                    },
                },
            };
            _defaults.Children.Add(row);
        }
    }
}
