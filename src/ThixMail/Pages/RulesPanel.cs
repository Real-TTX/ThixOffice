using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using MatDesktop;
using ThixMail.Core;
using ThixMail.Core.Model;
using ThixOffice.Core.Models;
using ThixOffice.Desktop;
using ThixOffice.Desktop.Controls;
using ThixOffice.Desktop.Dialogs;

namespace ThixMail.Pages;

/// <summary>
/// Einstellungen › Regeln: the rules in their order (a disabled one says so), below them Neue Regel · Bearbeiten · Nach oben · Nach unten · Regeln jetzt
/// anwenden · gap · Löschen. The rules run on new mail in a Posteingang; "Regeln jetzt anwenden" runs them on the Posteingang of every account (one undo step).
/// </summary>
public sealed class RulesPanel : UserControl
{
    private readonly Window _owner;
    private readonly MailDocument _document;
    private readonly ListBox _list = new() { MaxHeight = 320, MinHeight = 80 };
    private readonly InfoBar _message = new();
    private IReadOnlyList<MailRule> _rules = [];

    public RulesPanel(Window owner, MailDocument document)
    {
        _owner = owner;
        _document = document;
        AutomationProperties.SetName(_list, "Regeln");
        _list.DoubleTapped += async (_, _) => await EditAsync();
        NewButton = new Button { Content = new IconLabel { Icon = "plus", Text = "Neue Regel" } }.Themed("MatButton.Primary");
        NewButton.Click += async (_, _) => await NewAsync();
        EditButton = new Button { Content = new IconLabel { Icon = "edit", Text = "Bearbeiten" } };
        EditButton.Click += async (_, _) => await EditAsync();
        UpButton = new Button { Content = new IconLabel { Icon = "arrow-up", Text = "Nach oben" } };
        UpButton.Click += (_, _) => MoveSelected(-1);
        DownButton = new Button { Content = new IconLabel { Icon = "arrow-down", Text = "Nach unten" } };
        DownButton.Click += (_, _) => MoveSelected(1);
        RunButton = new Button { Content = new IconLabel { Icon = "play", Text = "Regeln jetzt anwenden" } };
        RunButton.Click += (_, _) => RunNow();
        DeleteButton = new Button { Content = new IconLabel { Icon = "trash", Text = "Löschen" } }.Themed("MatButton.Danger");
        ActionBar.SetSeparated(DeleteButton, true);
        DeleteButton.Click += async (_, _) => await DeleteAsync();
        _list.SelectionChanged += (_, _) => UpdateButtons();
        var hint = new TextBlock
        {
            Text = "Regeln laufen auf diesem Computer, sobald neue Nachrichten im Posteingang ankommen – in der Reihenfolge der Liste. " +
                   "Verschiebt oder löscht eine Regel eine Nachricht, laufen danach keine weiteren.",
            TextWrapping = TextWrapping.Wrap,
            FontSize = MatTokens.Type.FsSm,
        };
        hint.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
        Content = new StackPanel
        {
            MaxWidth = 760,
            HorizontalAlignment = HorizontalAlignment.Left,
            Spacing = 10,
            Margin = new Thickness(0, 0, 16, 0),
            Children = { hint, _message, _list, new ActionBar { Children = { NewButton, EditButton, UpButton, DownButton, RunButton, DeleteButton } } },
        };
        Load();
    }

    public ListBox List => _list;

    public InfoBar Message => _message;

    public Button NewButton { get; }

    public Button EditButton { get; }

    public Button UpButton { get; }

    public Button DownButton { get; }

    public Button RunButton { get; }

    public Button DeleteButton { get; }

    /// <summary>The dialog while it is open (tests).</summary>
    public RuleDialog? OpenDialog { get; private set; }

    private MailRule? Selected => _list.SelectedIndex >= 0 && _list.SelectedIndex < _rules.Count ? _rules[_list.SelectedIndex] : null;

    private void Load(string? select = null)
    {
        var selected = select ?? Selected?.Id;
        _rules = _document.Accounts.Rules;
        _list.ItemsSource = _rules.Select(r => new RuleRow(r, _document)).ToList();
        _list.SelectedIndex = Math.Max(_rules.ToList().FindIndex(r => r.Id == selected), _rules.Count > 0 ? 0 : -1);
        UpdateButtons();
    }

    private void UpdateButtons()
    {
        var index = _list.SelectedIndex;
        EditButton.IsEnabled = DeleteButton.IsEnabled = Selected is not null;
        UpButton.IsEnabled = index > 0;
        DownButton.IsEnabled = index >= 0 && index < _rules.Count - 1;
        RunButton.IsEnabled = _rules.Any(r => r.IsEnabled);
    }

    public async Task NewAsync(MailRule? template = null)
    {
        var dialog = new RuleDialog(_document, template ?? new MailRule { Order = _rules.Count == 0 ? 0 : _rules.Max(r => r.Order) + 1 }, isNew: true);
        OpenDialog = dialog;
        if (await dialog.ShowAsync(_owner) && dialog.Saved is { } saved)
        {
            Load(saved.Id);
        }

        OpenDialog = null;
    }

    public async Task EditAsync()
    {
        if (Selected is not { } rule)
        {
            return;
        }

        var dialog = new RuleDialog(_document, rule, isNew: false);
        OpenDialog = dialog;
        if (await dialog.ShowAsync(_owner))
        {
            Load(rule.Id);
        }

        OpenDialog = null;
    }

    private void MoveSelected(int direction)
    {
        var index = _list.SelectedIndex;
        var other = index + direction;
        if (Selected is not { } rule || other < 0 || other >= _rules.Count)
        {
            return;
        }

        // renumber in the new order (orders may have gaps or ties)
        var ordered = _rules.ToList();
        ordered.RemoveAt(index);
        ordered.Insert(other, rule);
        for (var position = 0; position < ordered.Count; position++)
        {
            if (ordered[position].Order != position)
            {
                _document.Accounts.Save(ordered[position] with { Order = position });
            }
        }

        Load(rule.Id);
    }

    /// <summary>"Regeln jetzt anwenden": on the Posteingang of every account (one undoable step).</summary>
    public void RunNow()
    {
        var changed = 0;
        foreach (var account in _document.Accounts.Accounts)
        {
            if (_document.Store.FolderByRole(account.Id, FolderRole.Inbox) is { } inbox)
            {
                changed += _document.Session.ApplyRulesNow(inbox.Id);
            }
        }

        _message.Show(changed == 0 ? "Keine Nachricht im Posteingang passt zu einer Regel." : changed == 1 ? "Eine Nachricht wurde nach den Regeln geändert (Rückgängig: Strg+Z)."
            : $"{changed} Nachrichten wurden nach den Regeln geändert (Rückgängig: Strg+Z).", InfoSeverity.Success, isClosable: true);
    }

    private async Task DeleteAsync()
    {
        if (Selected is { } rule && await QuestionDialog.ConfirmAsync(_owner, "Regel löschen?", $"Die Regel „{rule.Name}“ wird gelöscht. Nachrichten, die sie schon geändert hat, bleiben, wie sie sind.", "Löschen", isDestructive: true))
        {
            _document.Accounts.RemoveRule(rule.Id);
            Load();
        }
    }

    private sealed class RuleRow : StackPanel
    {
        public RuleRow(MailRule rule, MailDocument document)
        {
            Margin = new Thickness(0, 3);
            var name = new TextBlock { Text = rule.IsEnabled ? rule.Name : $"{rule.Name} (ausgeschaltet)", FontWeight = FontWeight.SemiBold };
            var detail = new TextBlock { Text = Describe(rule, document), FontSize = MatTokens.Type.FsSm, TextTrimming = TextTrimming.CharacterEllipsis };
            detail.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            if (!rule.IsEnabled)
            {
                name.Use(TextBlock.ForegroundProperty, "Mat.TextMuted");
            }

            Children.Add(name);
            Children.Add(detail);
            AutomationProperties.SetName(this, name.Text);
        }
    }

    /// <summary>"Wenn Absender enthält „Shop“ und Anlage → In „Rechnungen“ verschieben, als gelesen markieren".</summary>
    public static string Describe(MailRule rule, MailDocument document)
    {
        var conditions = new List<string>();
        if (rule.FromContains is { } from)
        {
            conditions.Add($"Absender enthält „{from}“");
        }

        if (rule.ToContains is { } to)
        {
            conditions.Add($"Empfänger enthält „{to}“");
        }

        if (rule.SubjectContains is { } subject)
        {
            conditions.Add($"Betreff enthält „{subject}“");
        }

        if (rule.HasAttachment)
        {
            conditions.Add("mit Anlage");
        }

        var actions = new List<string>();
        if (rule.MoveToFolder is { } path && rule.AccountId is { } accountId)
        {
            var folder = document.Store.Folders(accountId).FirstOrDefault(f => f.Path == path);
            actions.Add(folder is null ? $"In „{path}“ verschieben (Ordner fehlt)" : $"In „{folder.DisplayName}“ verschieben");
        }

        if (rule.Delete)
        {
            actions.Add("löschen");
        }

        if (rule.MarkRead)
        {
            actions.Add("als gelesen markieren");
        }

        if (rule.Category is { } category)
        {
            actions.Add($"Kategorie „{category}“");
        }

        if (rule.Flag)
        {
            actions.Add("kennzeichnen");
        }

        var account = rule.AccountId is null ? "Alle Konten" : document.Accounts.Account(rule.AccountId)?.DisplayName ?? "Konto fehlt";
        return $"{account} · Wenn {string.Join(" und ", conditions)} → {string.Join(", ", actions)}";
    }
}

/// <summary>Neue Regel / Regel bearbeiten: Name, Konto, "Wenn" (Absender, Empfänger, Betreff enthält; mit Anlage) and "Dann" (verschieben, gelesen, Kategorie, kennzeichnen, löschen, keine weiteren).</summary>
public sealed class RuleDialog : OfficeDialog
{
    private readonly MailDocument _document;
    private readonly MailRule _rule;
    private readonly MatFormField _nameField;
    private readonly MatFormField _accountField;
    private readonly MatFormField _conditionsField;
    private readonly MatFormField _actionsField;
    private readonly MatFormField _folderField;
    private readonly MatFormField _categoryField;
    private IReadOnlyList<MailAccount> _accounts = [];
    private IReadOnlyList<MailFolder> _folders = [];

    public RuleDialog(MailDocument document, MailRule rule, bool isNew)
        : base(isNew ? "Neue Regel" : "Regel bearbeiten", 520)
    {
        _document = document;
        _rule = rule;
        _accounts = document.Accounts.Accounts;
        NameBox = Box("Name der Regel", rule.Name);
        AccountBox = new ComboBox { ItemsSource = new[] { "Alle Konten" }.Concat(_accounts.Select(a => a.DisplayName)).ToList(), MinWidth = 240 };
        AccountBox.SelectedIndex = rule.AccountId is null ? 0 : Math.Max(0, _accounts.ToList().FindIndex(a => a.Id == rule.AccountId) + 1);
        AutomationProperties.SetName(AccountBox, "Konto");
        FromBox = Box("Absender enthält", rule.FromContains ?? "");
        ToBox = Box("Empfänger enthält", rule.ToContains ?? "");
        SubjectBox = Box("Betreff enthält", rule.SubjectContains ?? "");
        AttachmentBox = new CheckBox { Content = "Die Nachricht hat eine Anlage", IsChecked = rule.HasAttachment };
        MoveBox = new CheckBox { Content = "In Ordner verschieben", IsChecked = rule.MoveToFolder is not null };
        FolderBox = new ComboBox { MinWidth = 240 };
        AutomationProperties.SetName(FolderBox, "Zielordner");
        ReadBox = new CheckBox { Content = "Als gelesen markieren", IsChecked = rule.MarkRead };
        CategorizeBox = new CheckBox { Content = "Kategorisieren", IsChecked = rule.Category is not null };
        CategoryBox = Box("Kategorie", rule.Category ?? document.Accounts.Categories.FirstOrDefault()?.Name ?? "");
        FlagBox = new CheckBox { Content = "Zur Nachverfolgung kennzeichnen", IsChecked = rule.Flag };
        DeleteBox = new CheckBox { Content = "Löschen (in „Gelöschte Elemente“)", IsChecked = rule.Delete };
        StopBox = new CheckBox { Content = "Keine weiteren Regeln anwenden", IsChecked = rule.StopProcessing };
        EnabledBox = new CheckBox { Content = "Regel ist eingeschaltet", IsChecked = rule.IsEnabled };

        _nameField = Field("Name", NameBox);
        _accountField = Field("Gilt für", AccountBox);
        _folderField = Field("Zielordner", FolderBox);
        _categoryField = Field("Kategorie", CategoryBox);
        _conditionsField = Field("Wenn", new StackPanel { Spacing = 8, Children = { Field("Absender enthält", FromBox), Field("Empfänger (An oder Cc) enthält", ToBox), Field("Betreff enthält", SubjectBox), AttachmentBox } });
        _actionsField = Field("Dann", new StackPanel { Spacing = 6, Children = { MoveBox, _folderField, ReadBox, CategorizeBox, _categoryField, FlagBox, DeleteBox, StopBox } });
        AccountBox.SelectionChanged += (_, _) => FillFolders(null);
        MoveBox.IsCheckedChanged += (_, _) => UpdateVisibility();
        CategorizeBox.IsCheckedChanged += (_, _) => UpdateVisibility();
        FillFolders(rule.MoveToFolder);
        UpdateVisibility();
        SetBody(new ScrollViewer
        {
            MaxHeight = 620,
            Content = new StackPanel { Spacing = 12, Margin = new Thickness(0, 0, 12, 0), Children = { _nameField, _accountField, _conditionsField, _actionsField, EnabledBox } },
        });
    }

    public TextBox NameBox { get; }
    public ComboBox AccountBox { get; }
    public TextBox FromBox { get; }
    public TextBox ToBox { get; }
    public TextBox SubjectBox { get; }
    public CheckBox AttachmentBox { get; }
    public CheckBox MoveBox { get; }
    public ComboBox FolderBox { get; }
    public CheckBox ReadBox { get; }
    public CheckBox CategorizeBox { get; }
    public TextBox CategoryBox { get; }
    public CheckBox FlagBox { get; }
    public CheckBox DeleteBox { get; }
    public CheckBox StopBox { get; }
    public CheckBox EnabledBox { get; }

    /// <summary>The rule as saved (null until OK).</summary>
    public MailRule? Saved { get; private set; }

    /// <summary>The error shown at a field (tests).</summary>
    public string? ErrorText => new[] { _nameField, _accountField, _conditionsField, _actionsField, _folderField, _categoryField }.Select(f => f.Error).FirstOrDefault(e => e is not null);

    private static TextBox Box(string name, string text)
    {
        var box = new TextBox { Text = text };
        AutomationProperties.SetName(box, name);
        return box;
    }

    private string? AccountId => AccountBox.SelectedIndex > 0 ? _accounts[AccountBox.SelectedIndex - 1].Id : null;

    private void FillFolders(string? select)
    {
        var current = select ?? (FolderBox.SelectedIndex >= 0 && FolderBox.SelectedIndex < _folders.Count ? _folders[FolderBox.SelectedIndex].Path : null);
        _folders = AccountId is { } id ? [.. _document.Store.Folders(id).Where(f => f.IsSelectable).OrderBy(f => f.RoleOrder).ThenBy(f => f.Path, StringComparer.CurrentCultureIgnoreCase)] : [];
        FolderBox.ItemsSource = _folders.Select(f => f.Role == FolderRole.None ? f.Path.Replace(f.Delimiter, '/') : f.DisplayName).ToList();
        FolderBox.SelectedIndex = _folders.ToList().FindIndex(f => f.Path == current);
        FolderBox.IsEnabled = _folders.Count > 0;
        FolderBox.PlaceholderText = _folders.Count == 0 ? "Erst ein Konto wählen" : "Ordner wählen";
    }

    private void UpdateVisibility()
    {
        _folderField.IsVisible = MoveBox.IsChecked == true;
        _categoryField.IsVisible = CategorizeBox.IsChecked == true;
    }

    protected override bool Apply()
    {
        foreach (var field in new[] { _nameField, _accountField, _conditionsField, _actionsField, _folderField, _categoryField })
        {
            field.Error = null;
        }

        var rule = _rule with
        {
            Name = NameBox.Text ?? "",
            AccountId = AccountId,
            FromContains = FromBox.Text,
            ToContains = ToBox.Text,
            SubjectContains = SubjectBox.Text,
            HasAttachment = AttachmentBox.IsChecked == true,
            MoveToFolder = MoveBox.IsChecked == true ? FolderBox.SelectedIndex >= 0 && FolderBox.SelectedIndex < _folders.Count ? _folders[FolderBox.SelectedIndex].Path : "" : null,
            MarkRead = ReadBox.IsChecked == true,
            Category = CategorizeBox.IsChecked == true ? CategoryBox.Text : null,
            Flag = FlagBox.IsChecked == true,
            Delete = DeleteBox.IsChecked == true,
            StopProcessing = StopBox.IsChecked == true,
            IsEnabled = EnabledBox.IsChecked == true,
        };
        if (MoveBox.IsChecked == true && rule.MoveToFolder is "")
        {
            (AccountId is null ? _accountField : _folderField).Error = AccountId is null
                ? "Eine Regel, die verschiebt, gilt für ein Konto: Bitte wählen Sie das Konto."
                : "Bitte wählen Sie den Zielordner.";
            return false;
        }

        try
        {
            Saved = MailRules.Validate(rule, _document.Store);
            _document.Accounts.Save(Saved);
            return true;
        }
        catch (BusinessException exception)
        {
            (exception.Field switch
            {
                nameof(MailRule.Name) => _nameField,
                nameof(MailRule.AccountId) => _accountField,
                "Conditions" => _conditionsField,
                nameof(MailRule.MoveToFolder) => _folderField,
                nameof(MailRule.Category) => _categoryField,
                _ => _actionsField,
            }).Error = exception.Message;
            return false;
        }
    }
}
